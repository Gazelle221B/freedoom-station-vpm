using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3.Components;

// Scene creation must not depend on selecting the descriptor in its Inspector.
[InitializeOnLoad]
public static class DoomSceneValidation
{
    const string RequestPath="UserSettings/Doom/repair-scene.request";
    const string ResultPath="UserSettings/Doom/repair-scene-result.json";

    static DoomSceneValidation() { EditorApplication.delayCall+=ApplyPendingRepair; }

    public static bool EnsurePipelineManager(Scene scene, bool undo=false)
    {
        if(!scene.IsValid()||!scene.isLoaded) throw new Exception("Doom scene is not loaded");
        var descriptors=scene.GetRootGameObjects()
            .SelectMany(root=>root.GetComponentsInChildren<VRCSceneDescriptor>(true)).ToArray();
        if(descriptors.Length!=1) throw new Exception("Expected exactly one Doom scene descriptor");
        var descriptor=descriptors[0];
        var managers=scene.GetRootGameObjects()
            .SelectMany(root=>root.GetComponentsInChildren<PipelineManager>(true)).ToArray();
        if(managers.Length==1&&managers[0].gameObject==descriptor.gameObject) return false;
        if(managers.Length!=0) throw new Exception("Unexpected PipelineManager location or duplicates; preserve existing world IDs");
        var manager=undo ? Undo.AddComponent<PipelineManager>(descriptor.gameObject)
                         : descriptor.gameObject.AddComponent<PipelineManager>();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    [MenuItem("Tools/Doom/Repair scene descriptor")]
    public static void RepairCurrentScene()
    {
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=DoomBootstrap.ScenePath)
            throw new Exception("Open the verified Doom scene before repairing its descriptor");
        var changed=EnsurePipelineManager(scene,true);
        Debug.Log("DOOM_SCENE_DESCRIPTOR_READY changed="+changed+" scene="+scene.path);
    }

    // One local request, consumed after a script reload; no background scene mutation.
    static void ApplyPendingRepair()
    {
        if(!File.Exists(RequestPath)) return;
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling) {
            EditorApplication.delayCall+=ApplyPendingRepair; return;
        }
        File.Delete(RequestPath);
        var result=new RepairResult();
        try {
            var scene=SceneManager.GetActiveScene();
            result.scene=scene.path;
            if(scene.path!=DoomBootstrap.ScenePath) throw new Exception("Active scene is not DoomWorld; no scene changed");
            result.preexistingUnsavedChanges=scene.isDirty;
            result.changed=EnsurePipelineManager(scene,true);
            // Preserve preexisting unsaved scene edits rather than saving them implicitly.
            result.saved=!scene.isDirty || (!result.preexistingUnsavedChanges&&EditorSceneManager.SaveScene(scene));
            result.success=true;
            foreach(var window in Resources.FindObjectsOfTypeAll<VRCSdkControlPanel>()) window.Repaint();
            Debug.Log("DOOM_SCENE_DESCRIPTOR_READY changed="+result.changed+" saved="+result.saved+" scene="+scene.path);
        } catch(Exception e) { result.error=e.ToString(); Debug.LogException(e); }
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
        File.WriteAllText(ResultPath,JsonUtility.ToJson(result,true));
    }

    [Serializable] public class RepairResult {
        public string scene,error;
        public bool success,changed,saved,preexistingUnsavedChanges;
    }
}
