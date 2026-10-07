using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharp;
using VRC.Udon;

[InitializeOnLoad]
public static class DialImportRepair
{
    private const string Request = "Library/DialImportRepair.request";
    private const string Prefab = "Assets/ThirdParty/PiMaker/Dial/_PREFAB.prefab";
    static DialImportRepair() { EditorApplication.update += PollRequest; }
    private static void PollRequest()
    {
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating &&
            !EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Request)) Repair();
    }
    [MenuItem("Tools/New World/Repair Official Dial References")]
    public static void Repair()
    {
        if (File.Exists(Request)) File.Delete(Request);
        UdonSharpProgramAsset.UdonSharpCheckAbsent();
        UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync(new UdonSharp.Compiler.UdonSharpCompileOptions { IsEditorBuild = true });
        if (UdonSharpProgramAsset.AnyUdonSharpScriptHasError()) throw new InvalidOperationException("UdonSharp compile failed");
        GameObject root = PrefabUtility.LoadPrefabContents(Prefab);
        int fonts = 0;
        int programs = 0;
        int meshes = 0;
        try
        {
            foreach (Text label in root.GetComponentsInChildren<Text>(true))
                if (label.font == null) { label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); fonts++; }
            foreach (MeshCollider collider in root.GetComponentsInChildren<MeshCollider>(true))
                if (collider.sharedMesh == null)
                {
                    var visual = collider.GetComponent<MeshFilter>();
                    if (visual == null || visual.sharedMesh == null)
                        throw new InvalidOperationException("Missing Dial collider and matching visual mesh: " + collider.name);
                    collider.sharedMesh = visual.sharedMesh;
                    meshes++;
                }
            foreach (UdonBehaviour behaviour in root.GetComponentsInChildren<UdonBehaviour>(true))
            {
                var program = behaviour.programSource as UdonSharpProgramAsset;
                if (program == null || program.GetSerializedUdonProgramAsset() == null)
                    throw new InvalidOperationException("Missing Dial Udon program source");
                var serialized = new SerializedObject(behaviour);
                serialized.FindProperty("serializedProgramAsset").objectReferenceValue = program.GetSerializedUdonProgramAsset();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                programs++;
            }
            if (programs != 3) throw new InvalidOperationException("Expected three Dial Udon behaviours");
            PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Library/DialImportRepair.result.json", JsonUtility.ToJson(new Result { passed = true, programs = programs, repairedFonts = fonts, repairedMeshes = meshes }, true));
            Debug.Log("DIAL_IMPORT_REPAIRED programs=" + programs + " fonts=" + fonts);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    [Serializable] private class Result { public bool passed; public int programs; public int repairedFonts; public int repairedMeshes; }
}
