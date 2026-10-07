using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UdonSharp;

[InitializeOnLoad]
public static class RvcImportRepair
{
    private const string Root = "Assets/ThirdParty/PiMaker/rvc";
    private const string Request = "Library/RvcImportRepair.request";
    static RvcImportRepair()
    {
        EditorApplication.update += PollRequest;
    }
    private static void PollRequest()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request)) Repair();
    }

    [MenuItem("Tools/New World/Repair and Compile rvc")]
    public static void Repair()
    {
        if (File.Exists(Request)) File.Delete(Request);
        var errors = new List<string>();
        int removed = 0;
        int programs = 0;
        try
        {
            foreach (string name in new[] { "RvcRunSwitch", "KeyboardManager2" })
            {
                string path = Root + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) != null) continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(Root + "/" + name + ".cs");
                asset.ScriptVersion = UdonSharpProgramVersion.CurrentVersion;
                AssetDatabase.CreateAsset(asset, path);
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                var serialized = new SerializedObject(material);
                var textures = serialized.FindProperty("m_SavedProperties.m_TexEnvs");
                for (int i = textures.arraySize - 1; i >= 0; i--)
                {
                    var entry = textures.GetArrayElementAtIndex(i);
                    string name = entry.FindPropertyRelative("first").stringValue;
                    if (material.HasProperty(name)) continue;
                    textures.DeleteArrayElementAtIndex(i);
                    removed++;
                }
                if (serialized.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(material);
            }
            AssetDatabase.SaveAssets();
            UdonSharpProgramAsset.UdonSharpCheckAbsent();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions { IsEditorBuild = true });
            if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) errors.Add("UdonSharp compilation reported errors");
            foreach (string guid in AssetDatabase.FindAssets("t:UdonSharpProgramAsset", new[] { Root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var program = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path);
                program.UpdateProgram();
                programs++;
                if (program.GetSerializedUdonProgramAsset() == null || program.GetRealProgram() == null)
                    errors.Add("Missing compiled Udon program: " + path);
            }
            AssetDatabase.SaveAssets();
        }
        catch (Exception exception) { errors.Add(exception.ToString()); }
        if (programs < 6) errors.Add("Expected six rvc UdonSharp program assets, found " + programs);
        var result = new Result { passed = errors.Count == 0, programs = programs, removedUnusedTextureProperties = removed, errors = errors.ToArray() };
        File.WriteAllText("Library/RvcImportRepair.result.json", JsonUtility.ToJson(result, true));
        Debug.Log("RVC_IMPORT_REPAIRED passed=" + result.passed + " programs=" + programs + " errors=" + errors.Count);
        foreach (string error in errors) Debug.LogWarning(error);
    }
    [Serializable] private class Result { public bool passed; public int programs; public int removedUnusedTextureProperties; public string[] errors; }
}
