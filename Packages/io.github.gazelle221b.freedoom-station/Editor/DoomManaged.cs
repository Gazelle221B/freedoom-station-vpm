using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

// Managed-project installation: reuse PiMaker shaders and leave World.unity intact.
public static class DoomManaged
{
    public const string StationPath="Assets/Doom/DoomStation.prefab";
    public static void Prepare()
    {
        try {
            const string core="Packages/io.github.gazelle221b.freedoom-station/ThirdParty/PiMaker/rvc/rvc/main.shader";
            if(!AssetDatabase.LoadAssetAtPath<Shader>(core)) throw new Exception("Managed rvc shader missing");
            EditorSceneManager.OpenScene(DoomBootstrap.ScenePath);
            DoomSceneValidation.EnsurePipelineManager(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            foreach(string name in new[]{"Nix/rvc","Nix/fb","Nix/console","Nix/Display"})
                if(!Shader.Find(name)) throw new Exception("Shader missing: "+name);
            var control=UnityEngine.Object.FindObjectOfType<DoomControl>();
            if(!control||!control.Vm||!control.Fb) throw new Exception("Doom VM/terminal references missing");
            if(control.Vm.material.shader!=AssetDatabase.LoadAssetAtPath<Shader>(core))
                throw new Exception("Doom uses a duplicate CPU shader");
            if(UnityEngine.Object.FindObjectsOfType<DoomKeyButton>().Length!=8)
                throw new Exception("Expected eight Doom interact buttons");
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            var root=GameObject.Find("Doom Station")??new GameObject("Doom Station");
            string[] standalone={"World Descriptor","Spawn","Camera","Light","Floor"};
            foreach(var obj in root.scene.GetRootGameObjects())
                if(obj!=root&&!standalone.Contains(obj.name)) obj.transform.SetParent(root.transform,true);
            PrefabUtility.SaveAsPrefabAssetAndConnect(root,StationPath,InteractionMode.AutomatedAction);
            EditorSceneManager.SaveScene(root.scene,DoomBootstrap.ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("DOOM_MANAGED_READY scene="+DoomBootstrap.ScenePath+" prefab="+StationPath+
                          " core="+core+" GPU="+SystemInfo.graphicsDeviceName);
            EditorApplication.Exit(0);
        } catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }

    [MenuItem("Tools/Doom/Open verified Doom scene")]
    public static void OpenScene()
    {
        if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
            EditorSceneManager.OpenScene(DoomBootstrap.ScenePath);
            DoomSceneValidation.EnsurePipelineManager(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),true);
        }
    }
    [MenuItem("Tools/Doom/Add station to current scene")]
    public static void AddStation()
    {
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(StationPath);
        if(!prefab) throw new Exception("Run DoomManaged.Prepare first");
        var station=(GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(station,"Add Doom station");
        Selection.activeGameObject=station;
    }
}
