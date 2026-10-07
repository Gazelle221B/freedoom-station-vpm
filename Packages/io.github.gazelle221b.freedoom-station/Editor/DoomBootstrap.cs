using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UdonSharpEditor;
using UdonSharp;
using VRC.SDK3.Components;

public static class DoomBootstrap
{
    static AddAndRemoveRequest packages;
    static double deadline;
    public const string ScenePath = "Assets/Doom/DoomWorld.unity";
    public static void Setup()
    {
        packages = Client.AddAndRemove(new[] { "com.unity.ugui@1.0.0" }, Array.Empty<string>());
        deadline = EditorApplication.timeSinceStartup + 600;
        EditorApplication.update += InstallPoll;
    }
    static void InstallPoll()
    {
        if (!packages.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
        EditorApplication.update -= InstallPoll;
        if (!packages.IsCompleted || packages.Status != StatusCode.Success) {
            Debug.LogError("Doom package resolution failed: " + packages.Error?.message);
            EditorApplication.Exit(2); return;
        }
        try { Create(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }
    public static Texture2D DataTexture(string name)
    {
        string path = "Assets/Doom/Generated/" + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (!importer) throw new FileNotFoundException(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = true;
        importer.maxTextureSize = 8192;
        var platform = importer.GetPlatformTextureSettings("Standalone");
        platform.overridden = true; platform.format = TextureImporterFormat.RGBA32;
        platform.maxTextureSize = 8192; platform.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SetPlatformTextureSettings(platform);
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
    public static void Create()
    {
        AssetDatabase.ImportAsset("Packages/io.github.gazelle221b.freedoom-station/ThirdParty/PiMaker/rvc/rvc/framebuffer.shader",ImportAssetOptions.ForceUpdate);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        foreach(string name in new[] { "DoomControl", "DoomInputAck", "DoomKeyButton" }) {
            string programPath="Packages/io.github.gazelle221b.freedoom-station/Doom/"+name+".asset";
            var program=AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(programPath);
            if (!program) {
                program=ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                program.sourceCsScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Packages/io.github.gazelle221b.freedoom-station/Doom/Runtime/"+name+".cs");
                AssetDatabase.CreateAsset(program,programPath);
            }
        }
        AssetDatabase.SaveAssets();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        var shader = Shader.Find("Nix/rvc");
        if (!shader) throw new Exception("Nix/rvc shader missing");
        var material = new Material(shader);
        foreach (string lane in new[] { "r", "g", "b", "a" }) {
            material.SetTexture("_Data_RAM_" + lane.ToUpperInvariant(), DataTexture("linux_payload." + lane));
            material.SetTexture("_Data_DTB_" + lane.ToUpperInvariant(), DataTexture("doom-auto." + lane));
            material.SetTexture("_Data_MTD_" + lane.ToUpperInvariant(), Texture2D.blackTexture);
        }
        material.SetInt("_Init",1); material.SetInt("_DoTick",0);
        material.SetInt("_Ticks",16384); material.SetInt("_TicksDivisor",4);
        AssetDatabase.CreateAsset(material,"Assets/Doom/VM.mat");
        var vm = new CustomRenderTexture(2048,4096) {
            name="DoomVM", graphicsFormat=GraphicsFormat.R32G32B32A32_UInt,
            doubleBuffered=true, material=material, initializationMaterial=material,
            initializationSource=CustomRenderTextureInitializationSource.Material,
            updateMode=CustomRenderTextureUpdateMode.OnDemand, filterMode=FilterMode.Point,
            wrapMode=TextureWrapMode.Clamp, useMipMap=false, autoGenerateMips=false,
            updateZoneSpace=CustomRenderTextureUpdateZoneSpace.Pixel
        };
        vm.SetUpdateZones(new[] {
            new CustomRenderTextureUpdateZone { updateZoneCenter=new Vector3(32,4064,0.5f), updateZoneSize=new Vector3(64,64,1), passIndex=0,needSwap=true },
            new CustomRenderTextureUpdateZone { updateZoneCenter=new Vector3(1024,2048,0.5f), updateZoneSize=new Vector3(2048,4096,1), passIndex=1,needSwap=true }
        });
        AssetDatabase.CreateAsset(vm,"Assets/Doom/VM.asset");
        var fbMaterial = new Material(Shader.Find("Nix/fb")); fbMaterial.SetTexture("_Data",vm);
        AssetDatabase.CreateAsset(fbMaterial,"Assets/Doom/FB.mat");
        var fb = new CustomRenderTexture(81,25,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) {
            name="DoomTerminal", doubleBuffered=true, material=fbMaterial,
            initializationSource=CustomRenderTextureInitializationSource.TextureAndColor,
            initializationColor=Color.clear, updateMode=CustomRenderTextureUpdateMode.OnDemand,
            filterMode=FilterMode.Point, useMipMap=false, autoGenerateMips=false
        };
        AssetDatabase.CreateAsset(fb,"Assets/Doom/FB.asset");
        var root = new GameObject("Doom VM");
        var control=root.AddUdonSharpComponent<DoomControl>();
        control.Vm=vm; control.Fb=fb;
        var descriptor=new GameObject("World Descriptor").AddComponent<VRCSceneDescriptor>();
        DoomSceneValidation.EnsurePipelineManager(scene);
        var spawn=new GameObject("Spawn").transform; spawn.position=new Vector3(0,0,-2);
        descriptor.spawns=new[] { spawn };
        descriptor.RespawnHeightY=-10;
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.gameObject.tag="EditorOnly";
        camera.transform.position=new Vector3(0,1.5f,-4); camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=Color.black;
        camera.cullingMask=~(1<<30);
        var light=new GameObject("Light").AddComponent<Light>(); light.type=LightType.Directional;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name="Floor";
        var screen=GameObject.CreatePrimitive(PrimitiveType.Quad); screen.name="Terminal";
        screen.transform.position=new Vector3(0,2,1); screen.transform.localScale=new Vector3(4,2,1);
        var display = new Material(Shader.Find("Nix/console"));
        if (!display.shader || display.shader.name=="Hidden/InternalErrorShader") display=new Material(Shader.Find("Unlit/Texture"));
        display.SetTexture("_FBTex",fb); display.SetTexture("_MainTex",fb);
        display.SetTexture("_Font",DataTexture("doom-font"));
        display.SetColor("_FGColor",Color.white);
        display.SetFloat("_BlinkRate",0);
        AssetDatabase.CreateAsset(display,"Assets/Doom/Console.mat");
        screen.GetComponent<Renderer>().sharedMaterial=display;
        var ackCamera=new GameObject("UART acknowledgment camera").AddComponent<Camera>();
        ackCamera.orthographic=true; ackCamera.cullingMask=1<<30;
        ackCamera.clearFlags=CameraClearFlags.SolidColor; ackCamera.backgroundColor=Color.black;
        ackCamera.transform.position=new Vector3(1000,1000,1000);
        var ackTarget=new RenderTexture(384,64,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear) { filterMode=FilterMode.Point };
        AssetDatabase.CreateAsset(ackTarget,"Assets/Doom/InputAckRT.asset");
        ackCamera.targetTexture=ackTarget;
        var ackQuad=GameObject.CreatePrimitive(PrimitiveType.Quad); ackQuad.name="UART state packing";
        ackQuad.layer=30; ackQuad.transform.position=ackCamera.transform.position+Vector3.forward;
        UnityEngine.Object.DestroyImmediate(ackQuad.GetComponent<Collider>());
        var ackMat=new Material(Shader.Find("Nix/Display")); ackMat.SetTexture("_Tex",vm);
        AssetDatabase.CreateAsset(ackMat,"Assets/Doom/InputAck.mat");
        ackQuad.GetComponent<Renderer>().sharedMaterial=ackMat;
        var buffer=new Texture2D(384,64,TextureFormat.RGBA32,false,true);
        AssetDatabase.CreateAsset(buffer,"Assets/Doom/InputAckBuffer.asset");
        var ack=ackCamera.gameObject.AddUdonSharpComponent<DoomInputAck>(); ack.Control=control; ack.Buffer=buffer;
        string[] names={"W Forward","A Turn Left","S Back","D Turn Right","Fire","E Use","Enter","Esc"};
        int[] keys={119,97,115,100,32,101,10,27};
        for(int i=0;i<keys.Length;i++) {
            var button=GameObject.CreatePrimitive(PrimitiveType.Cube); button.name=names[i];
            button.transform.position=new Vector3(-1.5f+(i%4),0.85f-(i/4)*0.45f,0.5f);
            button.transform.localScale=new Vector3(.85f,.35f,.2f);
            var key=button.AddUdonSharpComponent<DoomKeyButton>(); key.Control=control; key.KeyCode=keys[i];
            var udon=UdonSharpEditorUtility.GetBackingUdonBehaviour(key);
            udon.interactText=names[i]; udon.proximity=3;
            var label=new GameObject("Label").AddComponent<TextMesh>(); label.text=names[i];
            label.fontSize=36; label.characterSize=.02f; label.anchor=TextAnchor.MiddleCenter;
            label.transform.position=button.transform.position+Vector3.back*.12f;
            UdonSharpEditorUtility.CopyProxyToUdon(key);
        }
        UdonSharpEditorUtility.CopyProxyToUdon(control);
        UdonSharpEditorUtility.CopyProxyToUdon(ack);
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        EditorSceneManager.SaveScene(scene,ScenePath);
        EditorBuildSettings.scenes=new[] { new EditorBuildSettingsScene(ScenePath,true) };
        AssetDatabase.SaveAssets();
        Debug.Log("DOOM_SETUP_COMPLETE GPU="+SystemInfo.graphicsDeviceName+" API="+SystemInfo.graphicsDeviceType);
    }
}
