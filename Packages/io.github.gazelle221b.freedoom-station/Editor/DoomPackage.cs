using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

// Package assets remain separate from the editable, per-project station and payload.
public static class DoomPackage
{
    public const string Id = "io.github.gazelle221b.freedoom-station";
    public const string Root = "Packages/" + Id;
    const string InstallRecord = "UserSettings/Doom/station-install.json";
    [Serializable] sealed class Installation { public string packageId; public string[] files; }

    [MenuItem("Tools/Doom/Install editable station assets")]
    public static void Install()
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Root + "/Editor/DoomPackage.cs");
        if (info == null) throw new InvalidOperationException("Freedoom Station package is not installed.");
        if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath("fe393ace9b354375a9cb14cdbbc28be4")))
            throw new InvalidOperationException("Import TMP Essential Resources from Window > TextMeshPro before installing the license board.");
        string source = Path.Combine(info.resolvedPath, "Templates~/Doom");
        string target = Path.GetFullPath("Assets/Doom");
        var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
        var previous = File.Exists(InstallRecord)
            ? JsonUtility.FromJson<Installation>(File.ReadAllText(InstallRecord)) : null;
        var installed = previous != null && previous.packageId == Id && previous.files != null
            ? previous.files : Array.Empty<string>();
        // Preflight the entire operation before writing. Preserve user edits and GUID owners.
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(source, file);
            string destination = Path.Combine(target, relative);
            if (File.Exists(destination) && !File.ReadAllBytes(file).SequenceEqual(File.ReadAllBytes(destination)))
            {
                // Unity may reserialize templates after import. Previously installed files,
                // including user edits, are preserved without writing them again.
                if (!installed.Contains(relative.Replace('\\', '/')))
                    throw new IOException("Existing station asset differs; back up and review it before installing: " + destination);
            }
            if (file.EndsWith(".meta", StringComparison.Ordinal))
            {
                string line = File.ReadLines(file).FirstOrDefault(value => value.StartsWith("guid: ", StringComparison.Ordinal));
                if (line == null) continue;
                if (File.Exists(destination))
                {
                    string destinationGuid = File.ReadLines(destination).FirstOrDefault(value => value.StartsWith("guid: ", StringComparison.Ordinal));
                    if (destinationGuid != line) throw new IOException("Installed asset GUID changed: " + destination);
                }
                string existing = AssetDatabase.GUIDToAssetPath(line.Substring(6).Trim());
                string expected = "Assets/Doom/" + relative.Substring(0, relative.Length - 5).Replace('\\', '/');
                if (!string.IsNullOrEmpty(existing) && existing != expected)
                    throw new IOException("GUID already belongs to " + existing + "; expected " + expected);
            }
        }
        foreach (string file in files)
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (!File.Exists(destination)) File.Copy(file, destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(InstallRecord));
        File.WriteAllText(InstallRecord, JsonUtility.ToJson(new Installation {
            packageId = Id,
            files = files.Select(file => Path.GetRelativePath(source, file).Replace('\\', '/')).ToArray()
        }, true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("DOOM_PACKAGE_INSTALLED templates=" + files.Length + " destination=Assets/Doom");
    }

    public static void InstallRun()
    {
        try { Install(); EditorApplication.Exit(0); }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(2); }
    }

    // A bounded import/Udon compile check; no payload or world upload is required.
    public static void ValidateRun()
    {
        try
        {
            Install();
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
            if (UdonSharp.UdonSharpProgramAsset.AnyUdonSharpScriptHasError())
                throw new Exception("UdonSharp compilation reported errors.");
            foreach (string name in new[] { "Nix/rvc", "Nix/fb", "Nix/console", "Nix/Display" })
                if (!Shader.Find(name)) throw new Exception("Shader missing: " + name);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DoomManaged.StationPath);
            if (!prefab) throw new Exception("Station prefab missing.");
            var root = PrefabUtility.LoadPrefabContents(DoomManaged.StationPath);
            try
            {
                foreach (var behaviour in root.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true))
                    if (!UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour))
                        throw new Exception("Missing Udon backing behaviour.");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("DOOM_PACKAGE_VALIDATION_PASS Udon compilation and station import");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(2); }
    }
}
