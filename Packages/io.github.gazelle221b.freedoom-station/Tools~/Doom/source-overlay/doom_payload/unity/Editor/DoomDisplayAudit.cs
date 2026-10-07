using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Editor-only requests for the Unity 2022 managed project (no Pipeline package).
// The request lives outside Assets and is consumed once; runtime Udon is untouched.
[InitializeOnLoad]
public static class DoomDisplayAudit
{
    static double nextPoll;
    static DoomDisplayAudit() { EditorApplication.update += Poll; }
    [Serializable] public class Request { public string action, output; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        string path = Path.GetFullPath(".doom-editor-request.json");
        if (!File.Exists(path)) return;
        var request = JsonUtility.FromJson<Request>(File.ReadAllText(path));
        File.Delete(path);
        try {
            Directory.CreateDirectory(request.output);
            if (request.action == "Inspect") Inspect(request.output);
            else if (request.action == "CheckErrors") {
                if(EditorApplication.isPlaying) throw new Exception("Error check requires Edit Mode");
                // Clear historical Console entries once; the persisted Editor log
                // remains intact and new regression errors are still reported.
                var entries=typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
                entries?.GetMethod("Clear",System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)?.Invoke(null,null);
                if(!DoomWorldErrorTests.Check(request.output,false)) throw new Exception("Managed error regression failed");
            }
            else throw new Exception("Unsupported request: " + request.action);
        } catch (Exception e) {
            Debug.LogException(e);
            File.WriteAllText(Path.Combine(request.output, "request-error.txt"), e.ToString());
        }
    }
    public static void Run()
    {
        try { Inspect(Environment.GetEnvironmentVariable("DOOM_DISPLAY_OUT") ?? "DoomDisplayAudit"); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }
    public static void Inspect(string output)
    {
        Directory.CreateDirectory(output);
        var vm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/VM.mat");
        var console = AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/Console.mat");
        var fb = AssetDatabase.LoadAssetAtPath<CustomRenderTexture>("Assets/Doom/FB.asset");
        if (!vm || !console || !fb) throw new Exception("Managed Doom assets missing");
        var report = new StringBuilder();
        foreach (string lane in new[] { "R", "G", "B", "A" }) {
            var ram = vm.GetTexture("_Data_RAM_" + lane);
            var dtb = vm.GetTexture("_Data_DTB_" + lane);
            if (!ram || ram.width != 2048 || ram.height != 1051) throw new Exception("RAM imported size mismatch: " + lane);
            if (!AssetDatabase.GetAssetPath(dtb).EndsWith("doom-auto." + lane.ToLowerInvariant() + ".png"))
                throw new Exception("Not production DTB: " + AssetDatabase.GetAssetPath(dtb));
            report.AppendLine(lane + " RAM=" + ram.width + "x" + ram.height + " DTB=" + AssetDatabase.GetAssetPath(dtb));
        }
        report.AppendLine("terminal=" + fb.width + "x" + fb.height + " shaderGrid=" + console.GetInt("_FB_Width") + "x" + console.GetInt("_FB_Height"));
        report.AppendLine("activeScene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path + " playing=" + EditorApplication.isPlaying);
        if (fb.IsCreated()) {
            Save(fb, Path.Combine(output, "managed-terminal-rt.png"));
            Capture(console, fb, output, "managed-crt");
        }
        // Prove all 79x24 payload cells survive the actual console material/mesh.
        // Private material copy, no mutation of world assets or VM state.
        var grid = new Texture2D(81,25,TextureFormat.RGBA32,false,true) { filterMode=FilterMode.Point };
        var pixels = new Color32[81*25];
        for (int y=0;y<24;y++) for (int x=1;x<=79;x++) pixels[y*81+x] = new Color32((byte)'@',7,0,255);
        grid.SetPixels32(pixels); grid.Apply();
        var proof = Capture(console, grid, output, "all-79x24-cells");
        int visible=0;
        for(int y=0;y<24;y++) for(int x=0;x<79;x++) {
            // Console maps UV through its 2.5% border, then 80x25 glyph cells.
            int cx=Mathf.RoundToInt((((x+.5f)/80f+.0125f)/1.025f)*proof.width);
            int cy=Mathf.RoundToInt((((y+.5f)/25f+.0125f)/1.025f)*proof.height);
            bool ink=false;
            for(int dy=-6;dy<=6;dy++) for(int dx=-6;dx<=6;dx++) {
                Color c=proof.GetPixel(cx+dx,cy+dy);
                ink |= c.r>.15f || c.g>.15f || c.b>.15f;
            }
            if(ink) visible++; else throw new Exception("CRT missing glyph cell x="+x+" y="+y);
        }
        report.AppendLine("visiblePayloadCells="+visible+"/1896; no crop; original console shader/material properties");
        UnityEngine.Object.DestroyImmediate(proof); UnityEngine.Object.DestroyImmediate(grid);
        File.WriteAllText(Path.Combine(output,"display-result.txt"),report.ToString());
        Debug.Log("DOOM_DISPLAY_AUDIT_PASS "+report);
    }
    public static Texture2D Capture(Material source, Texture framebuffer, string output, string name)
    {
        var material=new Material(source) { hideFlags=HideFlags.HideAndDontSave };
        material.SetTexture("_FBTex",framebuffer);
        var quad=GameObject.CreatePrimitive(PrimitiveType.Quad); quad.hideFlags=HideFlags.HideAndDontSave;
        quad.layer=31; quad.transform.position=new Vector3(20000,20000,20000); quad.transform.localScale=new Vector3(4,2,1);
        quad.GetComponent<Renderer>().sharedMaterial=material;
        var go=new GameObject("Full CRT audit camera") { hideFlags=HideFlags.HideAndDontSave };
        var camera=go.AddComponent<Camera>(); camera.enabled=false; camera.orthographic=true;
        camera.orthographicSize=1.01f; camera.aspect=2; camera.cullingMask=1<<31;
        camera.transform.position=quad.transform.position+Vector3.back*2;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
        var rt=RenderTexture.GetTemporary(1600,800,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        camera.targetTexture=rt; camera.Render();
        var image=Read(rt); File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());
        camera.targetTexture=null; RenderTexture.ReleaseTemporary(rt);
        UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(quad); UnityEngine.Object.DestroyImmediate(material);
        return image;
    }
    static Texture2D Read(RenderTexture rt) {
        var old=RenderTexture.active; RenderTexture.active=rt;
        var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false,true);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply(); RenderTexture.active=old; return image;
    }
    static void Save(RenderTexture rt,string path) { var t=Read(rt); File.WriteAllBytes(path,t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); }
}
