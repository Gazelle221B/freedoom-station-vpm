using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

[InitializeOnLoad]
public static class DoomVerify
{
    static bool running, initialized;
    static CustomRenderTexture vm, fb;
    static Material probeMaterial;
    static Material worldMaterial;
    static Material verificationMaterial, worldFramebufferMaterial, verificationFramebufferMaterial;
    static Material timeProbe;
    static RenderTexture state;
    static string output;
    static double started, timeout, lastProgress;
    static int unityFrames, lastFrame=-1, bootFrame=-1, endFrames;
    static int requiredFrames = 3;
    static bool productionDisplayAudit;
    static uint clock, firstClock;
    static Camera renderCamera;
    static readonly StringBuilder uart = new StringBuilder();
    static readonly StringBuilder line = new StringBuilder();
    static double bootSeconds;
    static double previousEnd;
    static int previousEndFrame;
    static readonly StringBuilder measurements = new StringBuilder();
    static DoomVerify() { EditorApplication.update += Update; }
    public static void Run()
    {
        CheckComparison();
        EditorSceneManager.OpenScene(DoomBootstrap.ScenePath);
        foreach (var c in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            if (c.GetType().Name=="DoomControl") c.enabled=false;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<VRC.Udon.UdonBehaviour>()) c.enabled=false;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<Camera>())
            if (c.name=="UART acknowledgment camera") c.enabled=false;
        vm=AssetDatabase.LoadAssetAtPath<CustomRenderTexture>("Assets/Doom/VM.asset");
        fb=AssetDatabase.LoadAssetAtPath<CustomRenderTexture>("Assets/Doom/FB.asset");
        if (!vm || !fb) throw new Exception("Run DoomBootstrap.Setup first");
        // Use capture markers only in verification; preserve the world DTB.
        worldMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/VM.mat");
        if (!worldMaterial) throw new Exception("World VM material missing");
        verificationMaterial=new Material(worldMaterial) { hideFlags=HideFlags.HideAndDontSave };
        worldFramebufferMaterial=fb.material;
        verificationFramebufferMaterial=new Material(worldFramebufferMaterial) { hideFlags=HideFlags.HideAndDontSave };
        productionDisplayAudit=Environment.GetEnvironmentVariable("DOOM_VERIFY_PRODUCTION")=="1";
        string verifyDtb=productionDisplayAudit?"doom-auto":Environment.GetEnvironmentVariable("DOOM_VERIFY_DTB") ?? "doom-auto-verify";
        foreach(string lane in new[] { "r", "g", "b", "a" })
            verificationMaterial.SetTexture("_Data_DTB_"+lane.ToUpperInvariant(),DoomBootstrap.DataTexture(verifyDtb+"."+lane));
        vm.material=verificationMaterial; vm.initializationMaterial=verificationMaterial;
        fb.material=verificationFramebufferMaterial;
        output=Environment.GetEnvironmentVariable("DOOM_VERIFY_OUT") ?? Path.GetFullPath("DoomVerification");
        Directory.CreateDirectory(output);
        timeout=ReadNumber("DOOM_VERIFY_TIMEOUT",1800);
        requiredFrames=Math.Max(3,(int)ReadNumber("DOOM_VERIFY_FRAMES",3));
        vm.material.SetInt("_Ticks",(int)ReadNumber("DOOM_VERIFY_TICKS",4096));
        vm.material.SetInt("_TicksDivisor",1); vm.material.SetInt("_Init",1);
        vm.material.SetInt("_InitRaw",0); vm.material.SetInt("_DoTick",0);
        fb.material.SetInt("_Init",1);
        probeMaterial=new Material(Shader.Find("Hidden/DoomStateProbe")) { hideFlags=HideFlags.HideAndDontSave };
        state=new RenderTexture(256,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) { filterMode=FilterMode.Point,hideFlags=HideFlags.HideAndDontSave };
        state.Create(); vm.Create(); fb.Create(); vm.Initialize(); fb.Initialize();
        QualitySettings.vSyncCount=0; Application.targetFrameRate=-1;
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
        started=EditorApplication.timeSinceStartup; running=true; initialized=false;
        Debug.Log("DOOM_VERIFY_START GPU="+SystemInfo.graphicsDeviceName+" API="+SystemInfo.graphicsDeviceType+" timeout="+timeout+" requiredFrames="+requiredFrames);
        EditorApplication.EnterPlaymode();
    }
    static double ReadNumber(string name,double fallback) {
        return double.TryParse(Environment.GetEnvironmentVariable(name),out double n)?n:fallback;
    }
    static uint[] ReadState()
    {
        Graphics.Blit(vm,state,probeMaterial);
        var req=AsyncGPUReadback.Request(state,0); req.WaitForCompletion();
        if (req.hasError) throw new Exception("VM GPU readback failed");
        var packed=req.GetData<Color32>(); var words=new uint[64*64*4];
        for(int y=0;y<64;y++) for(int x=0;x<64;x++) for(int lane=0;lane<4;lane++) {
            var c=packed[y*256+x+lane*64];
            words[(y*64+x)*4+lane]=(uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24);
        }
        return words;
    }
    static void Update()
    {
        if (!running) return;
        try {
            double elapsed=EditorApplication.timeSinceStartup-started;
            if (elapsed>timeout) { Finish(false,"timeout"); return; }
            if (!EditorApplication.isPlaying || Time.frameCount==lastFrame) return;
            lastFrame=Time.frameCount; unityFrames++;
            if (unityFrames==1) {
                // SDK Play Mode saves/reimports CRT assets, dropping transient
                // material references. Rebind after that transition, then reset.
                Debug.Log("DOOM_PLAY_BINDINGS vmMaterialMissing="+!vm.material+" fbMaterialMissing="+!fb.material);
                vm.material=verificationMaterial; vm.initializationMaterial=verificationMaterial;
                fb.material=verificationFramebufferMaterial;
                vm.Initialize(); fb.Initialize();
            }
            uint[] words=ReadState();
            clock=words[28*4+1]; uint pc=words[36*4+3];
            if (unityFrames==3) {
                vm.material.SetInt("_Init",0); fb.material.SetInt("_Init",0);
                initialized=true; firstClock=clock;
                Debug.Log("DOOM_RESET_STATE PC=0x"+pc.ToString("x8")+" clock="+clock+" mtime="+words[10*4+3]+" unityTime="+Time.time);
            }
            if (initialized) {
                uint count=words[11*4+3];
                if (count<64) for (uint i=0;i<=count;i++) Consume((byte)words[12*4+i]);
            }
            if (elapsed-lastProgress>=10) {
                lastProgress=elapsed;
                if (!timeProbe) timeProbe=new Material(Shader.Find("Hidden/DoomTimeProbe")) { hideFlags=HideFlags.HideAndDontSave };
                timeProbe.SetFloat("_ProbeTime",Time.time/20);
                var timeRT=RenderTexture.GetTemporary(4,1,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
                Graphics.Blit(null,timeRT,timeProbe);
                var timeReq=AsyncGPUReadback.Request(timeRT,0); timeReq.WaitForCompletion();
                var timeWords=timeReq.GetData<Color32>().Select(c=>(uint)c.r|((uint)c.g<<8)|((uint)c.b<<16)|((uint)c.a<<24)).ToArray();
                Debug.Log("DOOM_TIME_PROBE builtinMs="+timeWords[0]+" floatMtime="+timeWords[1]+" doubleMtime="+timeWords[2]+" explicitDoubleMtime="+timeWords[3]);
                RenderTexture.ReleaseTemporary(timeRT);
                string latest=SaveTerminal("terminal-latest");
                if(productionDisplayAudit && Compare(latest)) {
                    DoomDisplayAudit.Inspect(output);
                    var full=DoomDisplayAudit.Capture(AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/Console.mat"),fb,output,"production-full-crt");
                    UnityEngine.Object.DestroyImmediate(full);
                    Finish(true,"Production DTB E1M1 matched; full terminal and CRT captured"); return;
                }
                File.WriteAllText(Path.Combine(output,"uart.txt"),uart.ToString());
                string diagnostic=$"frames={unityFrames} seconds={elapsed:F2} clock={clock} stepsPerSec={(clock-firstClock)/Math.Max(elapsed,1):F2} pc=0x{pc:x8} stall={words[28*4]} mcause=0x{words[(44+0x340/4)*4+2]:x8} mtval=0x{words[(44+0x340/4)*4+3]:x8} mtime={words[10*4+3]} mtimecmp={words[10*4+1]} unityTime={Time.time:F3}";
                Debug.Log("DOOM_PROGRESS "+diagnostic);
                File.AppendAllText(Path.Combine(output,"progress.txt"),diagnostic+"\n");
            }
            if (!renderCamera) {
                renderCamera=UnityEngine.Object.FindObjectsOfType<Camera>().FirstOrDefault(c=>c.name=="Camera");
                // SDK Play Mode strips the world's EditorOnly preview camera.
                // Create a verification camera after that cleanup when needed.
                if (!renderCamera) {
                    var cameraObject=new GameObject("Doom verification camera") { hideFlags=HideFlags.HideAndDontSave };
                    renderCamera=cameraObject.AddComponent<Camera>(); renderCamera.enabled=false;
                    renderCamera.cullingMask=0;
                    renderCamera.targetTexture=new RenderTexture(16,16,0) { hideFlags=HideFlags.HideAndDontSave };
                }
            }
            renderCamera.Render();
            // In batchmode the CRT's builtin clock was read back as zero even
            // while Time.time advanced. Supply the existing uniform from the
            // host control path; the CPU/MMU shader remains unchanged.
            float seconds=Time.time;
            Shader.SetGlobalVector("_Time",new Vector4(seconds/20,seconds,seconds*2,seconds*3));
            vm.Update(1); fb.Update(1);
        } catch (Exception e) { Debug.LogException(e); Finish(false,e.Message); }
    }
    static void Consume(byte b)
    {
        if (b==0) return;
        char c=(char)b; uart.Append(c);
        if (c=='\r') return;
        if (c!='\n') { line.Append(c); return; }
        string text=line.ToString(); line.Clear();
        if (text.Contains("RVC_DOOM_AUTOSTART_ASCII") && bootFrame<0) {
            bootFrame=unityFrames; bootSeconds=EditorApplication.timeSinceStartup-started;
            Debug.Log("DOOM_LINUX_BOOTED frames="+bootFrame+" seconds="+bootSeconds);
        }
        if (text.Contains("RVC_DOOM_FRAME_END ")) {
            endFrames++;
            double now=EditorApplication.timeSinceStartup-started;
            measurements.AppendLine($"doomFrame={endFrames} unityFrames={unityFrames-previousEndFrame} seconds={now-previousEnd:F3} clock={clock}");
            previousEnd=now; previousEndFrame=unityFrames;
            string terminal=SaveTerminal("frame-"+endFrames);
            if (endFrames>=requiredFrames) {
                bool matched=Compare(terminal);
                Finish(matched,matched?"E1M1 scene and HUD matched C reference":"E1M1 scene/HUD mismatch; see comparison.txt");
            }
        }
    }
    static string SaveTerminal(string name)
    {
        var old=RenderTexture.active; RenderTexture.active=fb;
        var t=new Texture2D(81,25,TextureFormat.RGBA32,false,true);
        t.ReadPixels(new Rect(0,0,81,25),0,0); t.Apply(); RenderTexture.active=old;
        File.WriteAllBytes(Path.Combine(output,name+"-rt.png"),t.EncodeToPNG());
        var console=AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/Console.mat");
        var image=RenderTexture.GetTemporary(1280,400,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
        Graphics.Blit(fb,image,console);
        RenderTexture.active=image;
        var view=new Texture2D(1280,400,TextureFormat.RGBA32,false,true);
        view.ReadPixels(new Rect(0,0,1280,400),0,0); view.Apply();
        File.WriteAllBytes(Path.Combine(output,name+"-console.png"),view.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(view);
        RenderTexture.active=old; RenderTexture.ReleaseTemporary(image);
        var p=t.GetPixels32(); var result=new StringBuilder();
        for (int y=24;y>=0;y--) { for(int x=1;x<81;x++) {
            byte c=p[y*81+x].r; result.Append(c>=32&&c<127?(char)c:' ');
        } result.Append('\n'); }
        UnityEngine.Object.DestroyImmediate(t);
        File.WriteAllText(Path.Combine(output,name+".txt"),result.ToString());
        return result.ToString();
    }
    static bool Compare(string actual)
    {
        string golden=Environment.GetEnvironmentVariable("DOOM_GOLDEN");
        if (string.IsNullOrEmpty(golden) || !File.Exists(golden)) return false;
        bool matched=CompareRows(File.ReadAllLines(golden),actual.Split('\n'),out string detail);
        bool startup=bootFrame>=0 && uart.ToString().Contains("Z_Init") && uart.ToString().Contains("R_Init");
        File.WriteAllText(Path.Combine(output,"comparison.txt"),detail+"startup="+startup+"\n");
        return matched && startup;
    }
    // One common row offset tolerates marker/scroll latency. Scene rows cannot
    // independently match a HUD row; shared blanks do not count as evidence.
    static bool CompareRows(string[] reference,string[] rows,out string detail)
    {
        int[] sceneRows={0,3,6,9,12,15,18};
        int[] hudRows={20,21,22,23};
        var report=new StringBuilder("threshold=.72; require upper>=2 middle>=2 HUD>=2; zero-based rows\n");
        bool passed=false;
        for(int offset=-2;offset<=2;offset++) {
            int upper=0,middle=0,hud=0;
            report.AppendLine("offset="+offset);
            foreach(int y in sceneRows.Concat(hudRows)) {
                string r=y<reference.Length?reference[y].TrimEnd('\r'):"";
                int ay=y+offset;
                string a=ay>=0&&ay<rows.Length?rows[ay].TrimEnd('\r'):"";
                int evidence=0,equal=0,features=0;
                for(int x=0;x<79;x++) {
                    char rc=x<r.Length?r[x]:' ',ac=x<a.Length?a[x]:' ';
                    if(rc!=' ') features++;
                    if(rc==' '&&ac==' ') continue;
                    evidence++; if(rc==ac) equal++;
                }
                double score=equal/(double)Math.Max(evidence,1);
                bool hit=features>=12&&score>=.72;
                report.AppendLine($"referenceRow={y} actualRow={ay} score={score:F4} features={features} match={hit}");
                if(hit) { if(y>=20) hud++; else if(y<=6) upper++; else middle++; }
            }
            bool aligned=upper>=2&&middle>=2&&hud>=2;
            report.AppendLine($"upper={upper}/3 middle={middle}/4 HUD={hud}/4 pass={aligned}");
            passed|=aligned;
        }
        detail=report.ToString(); return passed;
    }
    static void CheckComparison()
    {
        string golden=Environment.GetEnvironmentVariable("DOOM_GOLDEN");
        if(string.IsNullOrEmpty(golden)||!File.Exists(golden)) throw new FileNotFoundException("C golden reference required",golden);
        var reference=File.ReadAllLines(golden);
        var blank=Enumerable.Repeat(new string(' ',80),25).ToArray();
        var hudOnly=(string[])blank.Clone();
        for(int y=20;y<24&&y<reference.Length;y++) hudOnly[y]=reference[y];
        var shifted=new[]{new string(' ',80)}.Concat(reference).ToArray();
        if(!CompareRows(reference,reference,out _)||!CompareRows(reference,shifted,out _)||
           CompareRows(reference,blank,out _)||CompareRows(reference,hudOnly,out _))
            throw new Exception("Scene/HUD comparison self-check failed");
        Debug.Log("DOOM_COMPARISON_SELF_CHECK passed golden/shifted; rejected blank/HUD-only");
    }
    static void Finish(bool passed,string reason)
    {
        if (!running) return; running=false;
        SaveTerminal(passed?"e1m1":"failure");
        if(passed && GameObject.Find("License Board")) DoomLicenseBoardBuild.CaptureWorld("crt-and-board-e1m1");
        File.WriteAllText(Path.Combine(output,"uart.txt"),uart.ToString());
        File.WriteAllText(Path.Combine(output,"frames.txt"),measurements.ToString());
        File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(new Result {
            passed=passed,reason=reason,unityFrames=unityFrames,seconds=EditorApplication.timeSinceStartup-started,
            bootFrames=bootFrame,bootSeconds=bootSeconds,doomFrames=endFrames,clock=clock,
            gpu=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString()
        },true));
        Debug.Log("DOOM_VERIFY_RESULT passed="+passed+" reason="+reason);
        // SDK Play Mode can save CRT assets. Restore persistent references so
        // a transient verification material is never serialized into the world.
        vm.material=worldMaterial; vm.initializationMaterial=worldMaterial;
        fb.material=worldFramebufferMaterial;
        EditorUtility.SetDirty(vm); EditorUtility.SetDirty(fb); AssetDatabase.SaveAssets();
        EditorApplication.Exit(passed?0:2);
    }
    [Serializable] class Result { public bool passed; public string reason,gpu,api; public int unityFrames,bootFrames,doomFrames; public double seconds,bootSeconds;public uint clock; }
}
