using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using UdonSharp;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class NewWorldPackageValidation
{
    private const string Request = "Library/NewWorldPackageValidation.request";
    static NewWorldPackageValidation()
    {
        EditorApplication.update += PollRequest;
    }
    private static void PollRequest()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request)) Validate();
    }
    [MenuItem("Tools/New World/Validate Imported Packages")]
    public static void Validate()
    {
        if (File.Exists(Request)) File.Delete(Request);
        string[] roots = { "Packages/red.sim.lightvolumes", "Packages/at.pimaker.ltcgi", "Assets/ThirdParty/PiMaker/rvc", "Assets/ThirdParty/PiMaker/Dial" };
        var errors = new List<string>();
        var optionalReferences = new List<string>();
        var generatedReferences = new List<string>();
        int shaders = 0;
        int references = 0;
        int udonPrograms = 0;
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions { IsEditorBuild = false });
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) errors.Add("VRChat client UdonSharp compilation failed");
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions { IsEditorBuild = true });
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) errors.Add("Editor UdonSharp compilation failed");
        foreach (string root in roots)
        {
            if (!AssetDatabase.IsValidFolder(root)) { errors.Add("Missing asset root: " + root); continue; }
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null) { errors.Add("Failed to load shader: " + path); continue; }
                shaders++;
                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    if (message.severity.ToString() == "Error") errors.Add(path + ": " + message.message);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                udonPrograms++;
                if (program.GetSerializedUdonProgramAsset() == null || program.GetRealProgram() == null)
                    errors.Add("Missing compiled Udon program: " + path);
            }
        }
        foreach (string path in AssetDatabase.GetAllAssetPaths().Where(path => roots.Any(root => path.StartsWith(root + "/", StringComparison.Ordinal))))
            {
                string extension = Path.GetExtension(path);
                if (extension != ".mat" && extension != ".asset" && extension != ".prefab") continue;
                var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
                string disk = package == null ? path : package.resolvedPath + path.Substring(package.assetPath.Length);
                if (!File.Exists(disk)) { errors.Add("Missing dependency asset file: " + path); continue; }
                string text = File.ReadAllText(disk);
                foreach (Match match in Regex.Matches(text, @"guid: ([a-f0-9]{32})"))
                {
                    string guid = match.Groups[1].Value;
                    if (guid.StartsWith("0000000000000000")) continue;
                    references++;
                    if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) continue;
                    string detail = path + " -> " + guid;
                    if (path.Contains("/Shaders/Amplify/") || path.Contains("/Shaders/ASE Functions/") ||
                        path.Contains("/Light Volume Debugger/VRCFury/") || path.Contains("/Light Volume Debugger/ModularAvatar/"))
                    { optionalReferences.Add(detail); continue; }
                    if (IsGeneratedOrInactiveReference(path, text, guid))
                    { generatedReferences.Add(detail); continue; }
                    errors.Add("Missing dependency reference: " + detail);
                }
            }
        bool lightVolumeBridge = Type.GetType("pi.LTCGI.LVAdapter.LightVolumeLTCGI, LTCGI_LightVolumes") != null;
        if (!lightVolumeBridge) errors.Add("LTCGI LightVolumes bridge type is unavailable");
        var result = new Result { passed = errors.Count == 0, shaders = shaders, assetReferences = references, udonPrograms = udonPrograms, lightVolumeBridge = lightVolumeBridge, errors = errors.ToArray(), optionalReferences = optionalReferences.Distinct().ToArray(), generatedOrInactiveReferences = generatedReferences.Distinct().ToArray() };
        File.WriteAllText("Library/NewWorldPackageValidation.result.json", JsonUtility.ToJson(result, true));
        Debug.Log("NEW_WORLD_PACKAGE_VALIDATED passed=" + result.passed + " shaders=" + shaders + " errors=" + errors.Count);
        foreach (string error in errors) Debug.LogWarning(error);
    }
    private static bool IsGeneratedOrInactiveReference(string path, string text, string guid)
    {
        // UdonSharp recreates serialized bytecode from the source assets. Check the
        // actual compiled program above, rather than an upstream bytecode GUID.
        if (Regex.IsMatch(text, @"serialized(?:Udon)?ProgramAsset:\s*\{[^}]*guid: " + guid)) return true;
        var crt = AssetDatabase.LoadAssetAtPath<CustomRenderTexture>(path);
        if (crt != null && crt.initializationSource != CustomRenderTextureInitializationSource.Material &&
            Regex.IsMatch(text, @"m_InitMaterial:\s*\{[^}]*guid: " + guid)) return true;
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null)
        {
            var texture = Regex.Match(text, @"- (\w+):\s*m_Texture:\s*\{[^}]*guid: " + guid);
            if (texture.Success && !material.HasProperty(texture.Groups[1].Value)) return true;
            // Controller.cs/ControllerLOD.cs assigns video/LOD input textures;
            // LV_LTCGI_Adapter.cs assigns the baked volume to _LV_Volume.
            if (path.EndsWith("/Prefilter Blur/LOD1s_mat.mat") && texture.Groups[1].Value == "_MainTex") return true;
            if (path.EndsWith("/LightVolumes/LV_Mat_LTCGI.mat") && texture.Groups[1].Value == "_LV_Volume") return true;
        }
        return false;
    }
    [Serializable] private class Result { public bool passed; public int shaders; public int assetReferences; public int udonPrograms; public bool lightVolumeBridge; public string[] errors; public string[] optionalReferences; public string[] generatedOrInactiveReferences; }
}
