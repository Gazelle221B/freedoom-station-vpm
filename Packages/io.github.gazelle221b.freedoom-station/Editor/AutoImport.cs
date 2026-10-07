using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;

// increment to reimport: 7

#if UNITY_EDITOR
[InitializeOnLoad]
public static class AutoImport
{
    private static List<string> FilesToWatch = new List<string>() {
        "Packages\\io.github.gazelle221b.freedoom-station\\ThirdParty\\PiMaker\\rvc\\rvc\\*",
		"Packages\\io.github.gazelle221b.freedoom-station\\ThirdParty\\PiMaker\\rvc\\rvc\\src\\*",
    };

    private static FileSystemWatcher[] watchers;
    private static bool[] hasChange;

    // Perl tool paths are resolved from environment variables so that no
    // absolute paths are committed to the repo:
    //   RVC_PERL   = full path to the perl executable (Strawberry/MSYS2/Cygwin)
    //   RVC_PERLPP = full path to the perlpp script (interpreters/perlpp checkout
    //                or an installed copy)
    // If a variable is unset, the bare command name is resolved through PATH.
    // When RVC_PERLPP points into an interpreters/perlpp checkout
    // (<root>/bin/perlpp with <root>/lib/Text/PerlPP.pm), the sibling lib
    // directory is added to perl's include path automatically.
    private static readonly string PathToPerl = ResolveTool("RVC_PERL", "perl");
    private static readonly string PathToPerlPP = ResolveTool("RVC_PERLPP", "perlpp");
    private const int PerlPPTimeoutMilliseconds = 60000;

    static string ResolveTool(string envName, string fallbackName)
    {
        var value = System.Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
        Debug.Log("[AutoImport] " + envName + " not set, falling back to PATH lookup: " + fallbackName);
        return fallbackName;
    }

    static AutoImport()
    {
        if (System.Environment.GetEnvironmentVariable("RVC_PACKAGE_DEVELOPMENT") != "1") return;
        if (EditorApplication.isPlaying) return;
        if (Application.isPlaying) return;

        // resolve *
        for (int i = 0; i < FilesToWatch.Count; i++)
        {
            var cur = FilesToWatch[i];
            if (cur.EndsWith("*"))
            {
                FilesToWatch.RemoveAt(i);
                i--;
                foreach (var file in Directory.GetFiles(Path.GetDirectoryName(cur)))
                {
                    if (file.EndsWith(".meta")) continue;
                    FilesToWatch.Add(file);
                }
            }
        }

        hasChange = new bool[FilesToWatch.Count];
        watchers = new FileSystemWatcher[FilesToWatch.Count];
        for (int i = 0; i < FilesToWatch.Count; i++)
        {
            hasChange[i] = false;

            if (FilesToWatch[i].EndsWith(".h")) continue;

            if (!(FilesToWatch[i].EndsWith(".pp") ||
                FilesToWatch[i].EndsWith(".shader") ||
                FilesToWatch[i].EndsWith(".cginc") ||
                FilesToWatch[i].EndsWith(".p")))
            {
                continue;
            }
            if (!File.Exists(FilesToWatch[i]))
            {
                Debug.LogWarning("[AutoImport] File doesn't exist: " + FilesToWatch[i]);
                continue;
            }
            var path = Path.GetDirectoryName(FilesToWatch[i]);
            var file = Path.GetFileName(FilesToWatch[i]);

            Debug.Log("[AutoImport] Watching: " + path + "\\" + file);
            watchers[i] = new FileSystemWatcher(path, file);
            watchers[i].NotifyFilter = NotifyFilters.LastWrite;
            watchers[i].Changed += createEventHandler(i);
            watchers[i].EnableRaisingEvents = true;
        }
        EditorApplication.update += OnUpdate;
    }

    private static FileSystemEventHandler createEventHandler(int i)
    {
        return (object sender, FileSystemEventArgs args) =>
        {
            if (args.ChangeType == WatcherChangeTypes.Changed)
            {
                hasChange[i] = true;
            }
        };
    }

    static void OnUpdate()
    {
        //if (EditorApplication.isPlaying) return;
        //if (Application.isPlaying) return;

        var importallpp = false;

        for (int i = 0; i < hasChange.Length; i++)
        {
            if (hasChange[i])
            {
                Debug.Log("[AutoImport] Asset changed: " + FilesToWatch[i]);
                hasChange[i] = false;

                if (FilesToWatch[i].EndsWith(".p"))
                {
                    importallpp = true;
                }
                else if (FilesToWatch[i].EndsWith(".pp"))
                {
                    var gen = FilesToWatch[i].Substring(0, FilesToWatch[i].Length - 3);
                    RunPerlPP(FilesToWatch[i], gen);
                    AssetDatabase.ImportAsset(gen);
                }
                else
                {
                    AssetDatabase.ImportAsset(FilesToWatch[i]);
                }
            }
        }

        if (importallpp)
        {
            foreach (var item in FilesToWatch)
            {
                if (item.EndsWith(".pp"))
                {
                    var gen = item.Substring(0, item.Length - 3);
                    RunPerlPP(item, gen);
                    AssetDatabase.ImportAsset(gen);
                }
            }
        }
    }

    static void RunPerlPP(string input, string output)
    {
        Debug.Log("[AutoImport] Running perlpp in " + System.Environment.CurrentDirectory);

        var args = new StringBuilder();
        var ppDir = Path.GetDirectoryName(PathToPerlPP);
        if (!string.IsNullOrEmpty(ppDir))
        {
            var libDir = Path.Combine(Path.GetDirectoryName(ppDir), "lib");
            if (Directory.Exists(libDir))
            {
                args.Append("-I \"").Append(libDir).Append("\" ");
            }
        }
        args.Append('"').Append(PathToPerlPP).Append("\" -o \"").Append(output).Append("\" \"").Append(input).Append('"');
        Debug.Log("[AutoImport] perl: " + PathToPerl + " args: " + args.ToString());

        var p = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo(PathToPerl, args.ToString())
            {
                WorkingDirectory = System.Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        p.OutputDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
        {
            if (e.Data != null) stdout.AppendLine(e.Data);
        };
        p.ErrorDataReceived += (object sender, System.Diagnostics.DataReceivedEventArgs e) =>
        {
            if (e.Data != null) stderr.AppendLine(e.Data);
        };

        try
        {
            if (!p.Start())
            {
                Debug.LogError("[AutoImport] Failed to start perl: " + PathToPerl);
                return;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Debug.LogError("[AutoImport] Could not launch perl '" + PathToPerl + "': " + ex.Message);
            return;
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit(PerlPPTimeoutMilliseconds))
        {
            p.Kill();
            p.WaitForExit();
            Debug.LogError("[AutoImport] perlpp timed out after " + (PerlPPTimeoutMilliseconds / 1000) + "s: " + input);
            return;
        }
        p.WaitForExit(); // drain the async output handlers

        var exitCode = p.ExitCode;
        var outText = stdout.ToString().Trim();
        var errText = stderr.ToString().Trim();

        if (outText.Length > 0) Debug.Log("[AutoImport] perlpp out: " + outText);
        if (errText.Length > 0) Debug.Log("[AutoImport] perlpp err: " + errText);

        if (exitCode != 0)
        {
            Debug.LogError("[AutoImport] perlpp failed with exit code " + exitCode + " for " + input);
            return;
        }

        Debug.Log("[AutoImport] perlpp ok (" + input + " -> " + output + ")");
    }
}
#endif