using System;
using UnityEditor;
using UnityEngine;

// Bind locally built textures without rebuilding or moving the station/board.
public static class DoomWorldTextures
{
    public static void Run()
    {
        try
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/VM.mat");
            var console=AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/Console.mat");
            if(!material || !console) throw new Exception("Doom station materials missing");
            foreach(string lane in new[]{"r","g","b","a"})
            {
                material.SetTexture("_Data_RAM_"+lane.ToUpperInvariant(),DoomBootstrap.DataTexture("linux_payload."+lane));
                material.SetTexture("_Data_DTB_"+lane.ToUpperInvariant(),DoomBootstrap.DataTexture("doom-auto."+lane));
                DoomBootstrap.DataTexture("doom-auto-verify."+lane);
                DoomBootstrap.DataTexture("doom-auto-trace."+lane);
            }
            console.SetTexture("_Font",DoomBootstrap.DataTexture("doom-font"));
            EditorUtility.SetDirty(material);
            EditorUtility.SetDirty(console);
            AssetDatabase.SaveAssets();
            Debug.Log("DOOM_WORLD_TEXTURES_READY");
            EditorApplication.Exit(0);
        }
        catch(Exception e) { Debug.LogException(e); EditorApplication.Exit(2); }
    }
}
