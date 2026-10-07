using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

[InitializeOnLoad]
public static class NewWorldPackageInstaller
{
    private const string Key = "NewWorldPackageInstaller.";
    private const string RequestFile = "Library/NewWorldPackages.request";
    private const string ResultFile = "Library/NewWorldPackages.result.json";
    private static AddAndRemoveRequest install;
    private static ListRequest list;
    private static readonly string[] Sources = {
        "https://github.com/REDSIM/VRCLightVolumes.git?path=/Packages/red.sim.lightvolumes#v.3.0.0-dev.20",
        "https://github.com/PiMaker/ltcgi.git#v1.7.3"
    };
    static NewWorldPackageInstaller()
    {
        if (File.Exists(RequestFile) || SessionState.GetBool(Key + "Active", false))
            EditorApplication.update += Tick;
    }
    [MenuItem("Tools/New World/Install Lighting Packages")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before installing packages.");
        SessionState.SetBool(Key + "Active", true);
        SessionState.SetFloat(Key + "Started", (float)EditorApplication.timeSinceStartup);
        install = Client.AddAndRemove(Sources);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Debug.Log("NEW_WORLD_PACKAGES_INSTALLING " + string.Join(", ", Sources));
    }
    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!SessionState.GetBool(Key + "Active", false))
        {
            if (!File.Exists(RequestFile)) return;
            File.Delete(RequestFile);
            Install();
            return;
        }
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + "Started", 0) > 600)
        { Complete(false, "Package resolution exceeded 600 seconds"); return; }
        if (install != null)
        {
            if (!install.IsCompleted) return;
            if (install.Status != StatusCode.Success)
            { Complete(false, install.Error == null ? "UPM install failed" : install.Error.message); return; }
            install = null;
        }
        if (list == null) { list = Client.List(true); return; }
        if (!list.IsCompleted) return;
        if (list.Status != StatusCode.Success)
        { Complete(false, list.Error == null ? "UPM list failed" : list.Error.message); return; }
        string[] requested = { "red.sim.lightvolumes", "at.pimaker.ltcgi" };
        string[] resolved = list.Result.Where(p => requested.Contains(p.name)).Select(p => p.name + "@" + p.version).ToArray();
        bool ok = list.Result.Any(p => p.name == "red.sim.lightvolumes" && p.version == "3.0.0-dev.20") &&
                  list.Result.Any(p => p.name == "at.pimaker.ltcgi" && p.version == "1.7.3");
        Complete(ok, string.Join(", ", resolved));
    }
    private static void Complete(bool ok, string details)
    {
        SessionState.SetBool(Key + "Active", false);
        EditorApplication.update -= Tick;
        list = null;
        install = null;
        File.WriteAllText(ResultFile, JsonUtility.ToJson(new Result { passed = ok, details = details }, true));
        Debug.Log("NEW_WORLD_PACKAGES_RESULT passed=" + ok + " " + details);
    }
    [Serializable] private class Result { public bool passed; public string details; }
}
