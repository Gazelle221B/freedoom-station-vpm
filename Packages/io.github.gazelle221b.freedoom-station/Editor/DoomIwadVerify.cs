using System;
using UnityEditor;
using UnityEngine;

// Configure all lanes through the existing lossless data-texture importer,
// then run the unchanged scene/HUD comparator in Play Mode.
public static class DoomIwadVerify
{
    public static void Run()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Doom/VM.mat");
        if (!material) throw new Exception("World VM material missing");
        foreach (string lane in new[] { "r", "g", "b", "a" })
        {
            material.SetTexture("_Data_RAM_" + lane.ToUpperInvariant(),
                DoomBootstrap.DataTexture("linux_payload." + lane));
            material.SetTexture("_Data_DTB_" + lane.ToUpperInvariant(),
                DoomBootstrap.DataTexture("doom-auto." + lane));
            DoomBootstrap.DataTexture("doom-auto-verify." + lane);
            DoomBootstrap.DataTexture("doom-auto-trace." + lane);
        }
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        DoomVerify.Run();
    }
}
