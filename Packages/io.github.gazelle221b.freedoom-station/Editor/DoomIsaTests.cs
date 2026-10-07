using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

/// <summary>
/// Bounded GPU riscv-tests ISA harness.
///
/// Uses production Nix/rvc with its existing _DoTick single-step control.
/// Observes executed ecall and a7==93 before the next instruction runs.
///
/// Entry: Unity -batchmode -executeMethod DoomIsaTests.Run
/// Env:
///   DOOM_ISA_DATA  — directory with {test}.r.png/.g.png/.b.png/.a.png
///   DOOM_ISA_OUT   — output directory (default: IsaResults)
///   One guest CPU step per GPU update; no test-specific CPU shader fork.
/// </summary>
[InitializeOnLoad]
public static class DoomIsaTests
{
    // State layout generated from _Nix/rvc/src/types.h.pp.
    // Row 28: stall(r), clock(g), commits(b), xreg[0](a)
    // Row 40: debug_do_tick(r), debug_last_ins(g), debug_last_stall(b), debug_arb_0(a)
    // In DoomStateProbe readback (256x64 ARGB32):
    //   pixel at (row%64 + channel*64, row/64) contains the word

    private const int MAX_FRAMES = 20000;

    private static string s_dataDir;
    private static string s_outDir;
    private static int s_ticksPerFrame;
    private static string[] s_testNames;
    private static int s_testIndex = -1;
    private static List<TestResult> s_results;

    // Per-test CRT state
    private static CustomRenderTexture s_vm;
    private static Material s_vmMaterial;
    private static Material s_probeMaterial;
    private static RenderTexture s_stateRT;
    private static int s_frameCount;
    private static uint s_prevClock;
    private static int s_stallFrames;
    private static Camera s_camera;
    private static Color32[] s_readback;
    private static int s_lastUnityFrame=-1;

    private static void PrepareVm()
    {
        string source=File.ReadAllText("Packages/io.github.gazelle221b.freedoom-station/ThirdParty/PiMaker/rvc/rvc/main.shader").Replace("\r","");
        const string path="Packages/io.github.gazelle221b.freedoom-station/ThirdParty/PiMaker/rvc/rvc/main.shader";
        var asset=AssetDatabase.LoadAssetAtPath<Shader>(path);
        if(!asset||ShaderUtil.ShaderHasError(asset)) {
            if(asset) foreach(var message in ShaderUtil.GetShaderMessages(asset)) Debug.LogError(message.message);
            throw new Exception("ISA VM shader failed to compile");
        }
        using(var sha=SHA256.Create()) Debug.Log("DOOM_ISA_PRODUCTION_SHADER_SHA256 "+
            BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source))).Replace("-","").ToLowerInvariant());
    }

    static DoomIsaTests() { EditorApplication.update += Update; }

    public static void Run()
    {
        s_dataDir = Environment.GetEnvironmentVariable("DOOM_ISA_DATA");
        s_outDir = Environment.GetEnvironmentVariable("DOOM_ISA_OUT") ?? "IsaResults";
        s_ticksPerFrame = 1;
        s_results = new List<TestResult>();
        s_testIndex=-1; s_lastUnityFrame=-1;

        if (string.IsNullOrEmpty(s_dataDir) || !Directory.Exists(s_dataDir))
        {
            Debug.LogError("DOOM_ISA_DATA must point to a directory with .r.png test textures");
            EditorApplication.Exit(2);
            return;
        }

        Directory.CreateDirectory(s_outDir);
        PrepareVm();

        s_testNames = Directory.GetFiles(s_dataDir, "*.r.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n.EndsWith(".r"))
            .Select(n => n.Substring(0, n.Length - 2))
            .OrderBy(n => n.StartsWith("rv32um-")?0:1).ThenBy(n=>n)
            .ToArray();
        string manifestPath=Path.Combine(s_dataDir,"manifest.json");
        if(File.Exists(manifestPath)) {
            var manifest=JsonUtility.FromJson<TestManifest>(File.ReadAllText(manifestPath));
            if(!manifest.complete||manifest.expected==null||manifest.expected.Length==0)
                throw new Exception("ISA preparation manifest incomplete");
            if(manifest.expected.Distinct().Count()!=manifest.expected.Length||
               manifest.expected.Any(n=>!s_testNames.Contains(n)))
                throw new Exception("ISA manifest has duplicate or missing texture tests");
            s_testNames=manifest.expected;
            Debug.Log("DOOM_ISA_INVENTORY suite="+manifest.suite+" count="+s_testNames.Length+
                      " testShSha256="+manifest.testShSha256);
        }
        string filter=Environment.GetEnvironmentVariable("DOOM_ISA_FILTER");
        if(!string.IsNullOrEmpty(filter)) s_testNames=s_testNames.Where(n=>filter.Split(',').Contains(n)).ToArray();

        if (s_testNames.Length == 0)
        {
            Debug.LogError("No *.r.png test textures found in " + s_dataDir);
            EditorApplication.Exit(2);
            return;
        }

        // Verify shaders
        if (!Shader.Find("Nix/rvc"))
        {
            Debug.LogError("Production Nix/rvc shader not found.");
            EditorApplication.Exit(2);
            return;
        }
        if (!Shader.Find("Hidden/DoomStateProbe"))
        {
            Debug.LogError("DoomStateProbe shader not found.");
            EditorApplication.Exit(2);
            return;
        }

        Debug.Log("DOOM_ISA_START tests=" + s_testNames.Length + " ticksPerFrame=" + s_ticksPerFrame +
                  " gpu=" + SystemInfo.graphicsDeviceName + " api=" + SystemInfo.graphicsDeviceType);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled=true;
        EditorSettings.enterPlayModeOptions=EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.EnterPlaymode();
    }

    private static void Update()
    {
        if (s_testNames == null||!EditorApplication.isPlaying||Time.frameCount==s_lastUnityFrame) return;
        try { Advance(); }
        catch(Exception e) {
            Debug.LogException(e);
            if(s_testIndex>=0&&s_testIndex<s_testNames.Length)
                Record(s_testNames[s_testIndex],"FAIL",e.Message,s_frameCount);
            Finish();
        }
    }
    private static void Advance()
    {
        s_lastUnityFrame=Time.frameCount;
        if(!s_camera) {
            var cameraObject=new GameObject("ISA render pump") { hideFlags=HideFlags.HideAndDontSave };
            s_camera=cameraObject.AddComponent<Camera>(); s_camera.enabled=false; s_camera.cullingMask=0;
            s_camera.targetTexture=new RenderTexture(16,16,0);
        }

        if (s_vm == null)
        {
            s_testIndex++;
            if (s_testIndex >= s_testNames.Length) { Finish(); return; }
            StartTest(s_testNames[s_testIndex]);
            return;
        }

        TickFrame();
    }

    private static void StartTest(string name)
    {
        Debug.Log("DOOM_ISA_TEST [" + (s_testIndex + 1) + "/" + s_testNames.Length + "] " + name);

        // Cleanup previous
        CleanupCRT();

        var vmShader = Shader.Find("Nix/rvc");
        s_vmMaterial = new Material(vmShader) { hideFlags = HideFlags.HideAndDontSave };

        // Load RAM textures (all-zero for unused areas = proper BSS clear)
        foreach (var lane in new[] { "r", "g", "b", "a" })
        {
            var ramPath = Path.Combine(s_dataDir, name + "." + lane + ".png");
            if(!File.Exists(ramPath)) throw new FileNotFoundException("Missing ISA RAM lane",ramPath);
            s_vmMaterial.SetTexture("_Data_RAM_" + lane.ToUpperInvariant(), LoadRawTexture(ramPath));
            s_vmMaterial.SetTexture("_Data_DTB_" + lane.ToUpperInvariant(), Texture2D.blackTexture);
            s_vmMaterial.SetTexture("_Data_MTD_" + lane.ToUpperInvariant(), Texture2D.blackTexture);
        }

        s_vmMaterial.SetInt("_Init", 1);
        s_vmMaterial.SetInt("_Ticks", 2); // production clamps to2; _DoTick allows only one execution
        s_vmMaterial.SetInt("_TicksDivisor", 1);
        s_vmMaterial.SetInt("_DoTick", 0);
        s_vmMaterial.SetInt("_RTC0", 0);
        s_vmMaterial.SetInt("_RTC1", 0);

        // Create CRT (same dimensions as production VM)
        s_vm = new CustomRenderTexture(2048, 4096)
        {
            name = "ISA_VM_" + name,
            graphicsFormat = GraphicsFormat.R32G32B32A32_UInt,
            doubleBuffered = true,
            material = s_vmMaterial,
            initializationMaterial = s_vmMaterial,
            initializationSource = CustomRenderTextureInitializationSource.Material,
            updateMode = CustomRenderTextureUpdateMode.OnDemand,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            updateZoneSpace = CustomRenderTextureUpdateZoneSpace.Pixel,
            hideFlags = HideFlags.HideAndDontSave
        };
        s_vm.SetUpdateZones(new[] {
            new CustomRenderTextureUpdateZone {
                updateZoneCenter = new Vector3(32, 4064, 0.5f),
                updateZoneSize = new Vector3(64, 64, 1),
                passIndex = 0, needSwap = true
            },
            new CustomRenderTextureUpdateZone {
                updateZoneCenter = new Vector3(1024, 2048, 0.5f),
                updateZoneSize = new Vector3(2048, 4096, 1),
                passIndex = 1, needSwap = true
            }
        });
        s_vm.Create();
        s_vm.Initialize();

        // State readback
        s_probeMaterial = new Material(Shader.Find("Hidden/DoomStateProbe"))
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        s_stateRT = new RenderTexture(256, 64, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        s_stateRT.Create();

        s_frameCount = 0;
        s_prevClock = 0;
        s_stallFrames = 0;
    }

    private static void TickFrame()
    {
        s_frameCount++;

        // Supply _Time uniform (required by cpu_init -> start_time_ref)
        Shader.SetGlobalVector("_Time", new Vector4(
            Time.realtimeSinceStartup / 20f, Time.realtimeSinceStartup,
            Time.realtimeSinceStartup * 2f, Time.realtimeSinceStartup * 3f));

        // Let real Unity frames process CRT initialization before any execution.
        if (s_frameCount <= 3)
        {
            s_vm.Update(1);
            s_camera.Render();
            return;
        }
        if (s_frameCount == 4)
        {
            s_readback=null;
            uint initialPc=ReadStateWord(36,3),initialClock=ReadStateWord(28,1);
            Debug.Log($"DOOM_ISA_INIT {s_testNames[s_testIndex]} pc=0x{initialPc:x8} clock={initialClock}");
            if(initialPc!=0x80000000||initialClock!=0) {
                Record(s_testNames[s_testIndex],"FAIL","CRT reset invalid pc=0x"+initialPc.ToString("x8")+" clock="+initialClock,s_frameCount);
                return;
            }
            s_vmMaterial.SetInt("_Init", 0);
        }

        s_vmMaterial.SetInt("_DoTick",s_frameCount);
        s_vm.Update(1);
        s_camera.Render();
        s_readback=null;

        uint clock = ReadStateWord(28, 1),stall=ReadStateWord(28,0);
        uint lastInstruction=ReadStateWord(40,1),a7=ReadStateWord(33,0);
        if(s_frameCount<=14) File.AppendAllText(Path.Combine(s_outDir,"trace.txt"),
            $"test={s_testNames[s_testIndex]} frame={s_frameCount} clock={clock} pc=0x{ReadStateWord(36,3):x8} lastIns=0x{lastInstruction:x8} stall={stall} mcause=0x{ReadStateWord(252,2):x8} mtval=0x{ReadStateWord(252,3):x8}\n");
        if(clock>s_prevClock&&lastInstruction==0x00000073&&a7==93)
        {
            uint arb0=ReadStateWord(31,1);
            string verdict = arb0 == 0 ? "PASS" : "FAIL";
            string reason = arb0 == 0 ? "ecall x10==0" : "ecall x10=0x" + arb0.ToString("x8");
            Record(s_testNames[s_testIndex], verdict, reason, s_frameCount);
            s_vm = null; // triggers next test
            return;
        }

        // Check for VM fault (pc == 0 and clock > 0 means something went wrong)
        uint pc = ReadStateWord(36, 3);
        if (pc == 0 && clock > 100)
        {
            Record(s_testNames[s_testIndex], "FAIL", "fault pc=0 clock=" + clock, s_frameCount);
            s_vm = null;
            return;
        }

        // Check for stall without exit (hung)
        if (clock == s_prevClock) s_stallFrames++;
        else { s_stallFrames = 0; s_prevClock = clock; }

        if (s_stallFrames > 10)
        {
            uint x10 = ReadStateWord(31, 1);
            Record(s_testNames[s_testIndex], "FAIL",
                "stalled-no-exit stall=" + stall.ToString("x8") + " x10=0x" + x10.ToString("x8") +
                " pc=0x" + pc.ToString("x8") + " clock=" + clock, s_frameCount);
            s_vm = null;
            return;
        }

        // Timeout
        if (s_frameCount >= MAX_FRAMES)
        {
            uint x10 = ReadStateWord(31, 1);
            Record(s_testNames[s_testIndex], "FAIL",
                "timeout " + s_frameCount + " frames x10=0x" + x10.ToString("x8") +
                " pc=0x" + pc.ToString("x8") + " clock=" + clock, s_frameCount);
            s_vm = null;
            return;
        }
    }

    private static uint ReadStateWord(int row, int channel)
    {
        // DoomStateProbe maps: CRT pixel (row%64 + channel*64, row/64) -> word
        if(s_readback==null) {
            Graphics.Blit(s_vm,s_stateRT,s_probeMaterial);
            var req=AsyncGPUReadback.Request(s_stateRT,0); req.WaitForCompletion();
            if(req.hasError) throw new Exception("ISA state readback failed");
            s_readback=req.GetData<Color32>().ToArray();
        }
        var raw=s_readback;
        int x = (row % 64) + (channel * 64);
        int y = row / 64;
        int idx = x + y * 256;
        if (idx < 0 || idx >= raw.Length) throw new Exception("ISA state coordinate outside readback");

        var c = raw[idx];
        return (uint)c.r | ((uint)c.g << 8) | ((uint)c.b << 16) | ((uint)c.a << 24);
    }

    private static Texture2D LoadRawTexture(string path)
    {
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        tex.LoadImage(File.ReadAllBytes(path));
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }

    private static void Record(string name, string verdict, string reason, int frames)
    {
        try {
            if(s_vm) File.AppendAllText(Path.Combine(s_outDir,"trace.txt"),
                $"FINAL test={name} verdict={verdict} frames={frames} clock={ReadStateWord(28,1)} "+
                $"pc=0x{ReadStateWord(36,3):x8} lastIns=0x{ReadStateWord(40,1):x8} "+
                $"mcause=0x{ReadStateWord(252,2):x8} mtval=0x{ReadStateWord(252,3):x8}\n");
        } catch(Exception e) { Debug.LogWarning("ISA final state unavailable: "+e.Message); }
        var r = new TestResult { test = name, verdict = verdict, reason = reason, frames = frames };
        s_results.Add(r);
        Debug.Log("DOOM_ISA_RESULT " + name + " " + verdict + " " + reason + " frames=" + frames);
        CleanupCRT();
    }

    private static void CleanupCRT()
    {
        if (s_vm != null)
        {
            if (s_vm.material != null) {
                foreach(string lane in new[]{"R","G","B","A"}) {
                    var texture=s_vm.material.GetTexture("_Data_RAM_"+lane);
                    if(texture&&texture!=Texture2D.blackTexture) UnityEngine.Object.DestroyImmediate(texture);
                }
                UnityEngine.Object.DestroyImmediate(s_vm.material);
            }
            UnityEngine.Object.DestroyImmediate(s_vm);
        }
        if (s_probeMaterial != null) UnityEngine.Object.DestroyImmediate(s_probeMaterial);
        if (s_stateRT != null) { s_stateRT.Release(); UnityEngine.Object.DestroyImmediate(s_stateRT); }
        s_vm=null; s_vmMaterial=null; s_probeMaterial=null; s_stateRT=null;
    }

    private static void Finish()
    {
        CleanupCRT();
        if(s_camera) {
            UnityEngine.Object.DestroyImmediate(s_camera.targetTexture);
            UnityEngine.Object.DestroyImmediate(s_camera.gameObject);
        }

        int passed = s_results.Count(r => r.verdict == "PASS");
        int failed = s_results.Count(r => r.verdict == "FAIL");
        int skipped = s_results.Count(r => r.verdict == "SKIP");

        // Markdown report
        var md = new StringBuilder();
        md.AppendLine("# ISA Test Results");
        md.AppendLine();
        md.AppendLine("GPU: " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + ")");
        md.AppendLine("Ticks/frame: " + s_ticksPerFrame + "  Max frames: " + MAX_FRAMES);
        md.AppendLine("Passed: **" + passed + "**  Failed: **" + failed + "**  Skipped: " + skipped + "  Total: " + s_results.Count);
        md.AppendLine();
        md.AppendLine("| Test | Verdict | Reason | Frames |");
        md.AppendLine("|------|---------|--------|--------|");
        foreach (var r in s_results.OrderBy(r => r.test))
            md.AppendLine("| " + r.test + " | " + r.verdict + " | " + r.reason + " | " + r.frames + " |");

        // Tested / not tested summary
        md.AppendLine();
        md.AppendLine("## Summary");
        var tested = s_results.Where(r => r.verdict != "SKIP").ToList();
        var notTested = s_results.Where(r => r.verdict == "SKIP").ToList();
        md.AppendLine("- Tested: " + tested.Count + " (" + tested.Count(r => r.verdict == "PASS") + " pass, " + tested.Count(r => r.verdict == "FAIL") + " fail)");
        if (notTested.Count > 0)
        {
            md.AppendLine("- Not tested: " + notTested.Count);
            foreach (var r in notTested)
                md.AppendLine("  - " + r.test + ": " + r.reason);
        }

        File.WriteAllText(Path.Combine(s_outDir, "report.md"), md.ToString());

        // JSON
        File.WriteAllText(Path.Combine(s_outDir, "results.json"),
            JsonUtility.ToJson(new ReportData
            {
                passed = passed, failed = failed, skipped = skipped,
                total = s_results.Count, ticksPerFrame = s_ticksPerFrame,
                gpu = SystemInfo.graphicsDeviceName, api = SystemInfo.graphicsDeviceType.ToString(),
                tests = s_results
            }, true));

        Debug.Log("DOOM_ISA_COMPLETE " + passed + " passed, " + failed + " failed, " + skipped + " skipped");
        Debug.Log("Report: " + Path.Combine(s_outDir, "report.md"));

        EditorApplication.Exit(failed==0&&skipped==0&&passed==s_testNames.Length ? 0 : 1);
    }

    [Serializable]
    public class TestManifest
    {
        public string suite, testShSha256;
        public string[] expected;
        public bool complete;
    }

    [Serializable]
    public class TestResult
    {
        public string test;
        public string verdict;
        public string reason;
        public int frames;
    }

    [Serializable]
    public class ReportData
    {
        public int passed, failed, skipped, total, ticksPerFrame;
        public string gpu, api;
        public List<TestResult> tests;
    }
}
