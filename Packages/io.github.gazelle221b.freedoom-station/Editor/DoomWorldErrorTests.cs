using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UdonSharpEditor;

// Exercises the SDK callback failure separately from actual saved proxy data.
public static class DoomWorldErrorTests
{
    [Serializable] public class Result {
        public bool passed, oldSdkExpected;
        public int invalidCallbackExceptions, importErrors, shaderWarnings, shaderErrors, framebufferCells;
        public string[] messages;
    }
    public static void Run()
    {
        string output=Environment.GetEnvironmentVariable("DOOM_ERRORS_OUT") ?? "DoomWorldErrorVerification";
        EditorApplication.Exit(Check(output)?0:2);
    }
    public static bool Check(string output, bool savePrefab=true)
    {
        Directory.CreateDirectory(output);
        var result=new Result { oldSdkExpected=Environment.GetEnvironmentVariable("DOOM_ERRORS_EXPECT_OLD")=="1" };
        var messages=new List<string>();
        try {
            var go=new GameObject("Destroyed SDK serialization regression target");
            var proxy=go.AddUdonSharpComponent<DoomLicenseButton>();
            var callbacks=(ISerializationCallbackReceiver)proxy;
            UnityEngine.Object.DestroyImmediate(go);
            if(ReferenceEquals(proxy,null) || proxy!=null) throw new Exception("Expected a destroyed native object with a live managed wrapper");
            foreach(Action callback in new Action[]{callbacks.OnBeforeSerialize,callbacks.OnAfterDeserialize}) {
                try { callback(); }
                catch(ArgumentNullException e) { result.invalidCallbackExceptions++; messages.Add(e.GetType().Name+": "+e.ParamName); }
            }
            if(result.invalidCallbackExceptions != (result.oldSdkExpected ? 2 : 0)) throw new Exception("Invalid-object callback regression");
            Application.LogCallback capture=(message,stack,type)=> {
                if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert) { result.importErrors++; messages.Add(message); }
            };
            Application.logMessageReceived += capture;
            try {
                const string station="Assets/Doom/DoomStation.prefab";
                if(savePrefab) AssetDatabase.ImportAsset(station,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                var root=PrefabUtility.LoadPrefabContents(station);
                try {
                    foreach(var behaviour in root.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) {
                        var live=(ISerializationCallbackReceiver)behaviour;
                        live.OnBeforeSerialize(); live.OnAfterDeserialize();
                        if(!UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour)) throw new Exception("Lost Udon backing reference");
                    }
                    if(savePrefab) PrefabUtility.SaveAsPrefabAsset(root,station);
                } finally { PrefabUtility.UnloadPrefabContents(root); }
                if(savePrefab) AssetDatabase.SaveAssets();
            } finally { Application.logMessageReceived -= capture; }
            Shader shader=Shader.Find("Nix/fb");
            if(!shader) throw new Exception("Nix/fb missing");
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(shader),ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
            var material=new Material(shader);
            var rt=RenderTexture.GetTemporary(81,25,0,RenderTextureFormat.ARGBFloat);
            try { material.SetFloat("_Init",1); Graphics.Blit(null,rt,material); }
            finally { RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(material); }
            foreach(var message in ShaderUtil.GetShaderMessages(shader)) {
                messages.Add(message.severity+": "+message.message+" ("+message.file+":"+message.line+")");
                if(message.severity.ToString()=="Warning") result.shaderWarnings++;
                if(message.severity.ToString()=="Error") result.shaderErrors++;
            }
            if(!result.oldSdkExpected) result.framebufferCells=DoomFramebufferProbe.Check();
            if(!result.oldSdkExpected && (result.importErrors!=0 || result.shaderErrors!=0 || result.shaderWarnings!=0)) throw new Exception("Prefab import/save or shader diagnostics failed");
            result.passed=true;
        } catch(Exception e) { messages.Add(e.ToString()); Debug.LogException(e); }
        result.messages=messages.ToArray();
        File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(result,true));
        Debug.Log("DOOM_WORLD_ERROR_TESTS_"+(result.passed?"PASS":"FAIL")+" callbacks="+result.invalidCallbackExceptions+" importErrors="+result.importErrors+" shaderWarnings="+result.shaderWarnings);
        return result.passed;
    }
}
