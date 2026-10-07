using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;
using UdonSharp;
using VRC.SDK3.Editor;

public static class DoomBuild
{
    static Task<string> build;
    static double deadline;
    public static void Run()
    {
        EditorSceneManager.OpenScene(DoomBootstrap.ScenePath);
        DoomSceneValidation.EnsurePipelineManager(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        // The public Build API produces a local bundle; it never uploads.
        EditorWindow.GetWindow<VRCSdkControlPanel>();
        deadline=EditorApplication.timeSinceStartup+1200;
        EditorApplication.update+=Poll;
    }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup>deadline) { Fail("SDK build timeout"); return; }
        if (build==null) {
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkWorldBuilderApi>(out var builder)) return;
            // Populate the SDK's scene list through its public validation API.
            // A warm CLI startup can reach Build before the panel's first GUI pass.
            if (!builder.IsValidBuilder(out string reason)) { Fail(reason); return; }
            build=builder.Build(); return;
        }
        if (!build.IsCompleted) return;
        if (build.IsFaulted || build.IsCanceled) { Fail(build.Exception?.ToString() ?? "SDK build canceled"); return; }
        string output=Environment.GetEnvironmentVariable("DOOM_BUILD_OUT") ?? "DoomBuild";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"bundle-path.txt"),build.Result);
        Debug.Log("DOOM_SDK_BUILD_SUCCESS "+build.Result);
        EditorApplication.update-=Poll; EditorApplication.Exit(0);
    }
    static void Fail(string reason) {
        Debug.LogError("DOOM_SDK_BUILD_FAILED "+reason);
        EditorApplication.update-=Poll; EditorApplication.Exit(2);
    }
}
