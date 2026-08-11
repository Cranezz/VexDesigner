namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using Debug = UnityEngine.Debug;

    /// <summary>
    /// Watches Assets/PartInbox. Drop any number of .step files in there and
    /// they are converted to meshes in Assets/Parts, then deleted.
    ///
    /// The deletion is the point. STEP files are large - several megabytes
    /// each - and they are a *source* format the running game never reads. VEX
    /// publishes them, so they can always be fetched again. Keeping them in the
    /// repository would multiply its size for no runtime benefit, and git
    /// history is permanent, so a file committed once is committed forever.
    ///
    /// Conversion runs FreeCAD headlessly. It is genuinely slow - tens of
    /// seconds per part - so this is deliberately a batch operation with a
    /// cancellable progress bar rather than something that blocks silently.
    /// </summary>
    public sealed class StepInboxImporter : AssetPostprocessor
    {
        public const string InboxFolder = "Assets/PartInbox";
        private const string PartsFolder = "Assets/Parts";
        private const string ConverterScript = "tools/step_to_obj.py";
        private const string FreeCadPathPref = "VexDesigner.FreeCadPath";

        private static readonly string[] StepExtensions = { ".step", ".stp" };

        // ------------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------------

        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var pending = new List<string>();

            foreach (string path in imported)
            {
                if (IsStepInInbox(path))
                {
                    pending.Add(path);
                }
            }

            if (pending.Count == 0)
            {
                return;
            }

            // Deferred: deleting and creating assets from inside an import
            // callback re-enters the asset pipeline, which Unity does not
            // appreciate. delayCall runs it once the current import settles.
            EditorApplication.delayCall += () => ConvertBatch(pending);
        }

        private static bool IsStepInInbox(string path)
        {
            string normalised = path.Replace('\\', '/');
            if (!normalised.StartsWith(InboxFolder + "/"))
            {
                return false;
            }

            string extension = Path.GetExtension(normalised).ToLowerInvariant();
            return System.Array.IndexOf(StepExtensions, extension) >= 0;
        }

        // ------------------------------------------------------------------
        // Menu entries
        // ------------------------------------------------------------------

        [MenuItem("VexDesigner/Convert STEP Inbox Now")]
        public static void ConvertInboxMenuItem()
        {
            EnsureInboxExists();

            var pending = new List<string>();
            foreach (string file in Directory.GetFiles(InboxFolder))
            {
                string path = file.Replace('\\', '/');
                if (IsStepInInbox(path))
                {
                    pending.Add(path);
                }
            }

            if (pending.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "STEP Inbox",
                    $"No .step files found in {InboxFolder}.\n\n" +
                    "Drop STEP files into that folder and they convert " +
                    "automatically, or run this again afterwards.",
                    "OK");
                return;
            }

            ConvertBatch(pending);
        }

        [MenuItem("VexDesigner/Locate FreeCAD...")]
        public static void LocateFreeCadMenuItem()
        {
            string picked = EditorUtility.OpenFilePanel(
                "Select freecadcmd.exe", @"C:\Program Files", "exe");

            if (!string.IsNullOrEmpty(picked))
            {
                EditorPrefs.SetString(FreeCadPathPref, picked);
                Debug.Log($"[STEP] FreeCAD set to {picked}");
            }
        }

        private static void EnsureInboxExists()
        {
            if (!AssetDatabase.IsValidFolder(InboxFolder))
            {
                AssetDatabase.CreateFolder("Assets", "PartInbox");
            }
        }

        // ------------------------------------------------------------------
        // Conversion
        // ------------------------------------------------------------------

        private static void ConvertBatch(List<string> stepAssetPaths)
        {
            string freeCad = FindFreeCad();
            if (freeCad == null)
            {
                Debug.LogError(
                    "[STEP] FreeCAD not found. Install FreeCAD, or point at it " +
                    "with VexDesigner > Locate FreeCAD. STEP files left in the " +
                    "inbox untouched.");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            string script = Path.Combine(projectRoot, ConverterScript);

            if (!File.Exists(script))
            {
                Debug.LogError($"[STEP] Converter script missing: {script}");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PartsFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Parts");
            }

            var converted = new List<string>();
            bool cancelled = false;

            try
            {
                for (int i = 0; i < stepAssetPaths.Count; i++)
                {
                    string stepPath = stepAssetPaths[i];
                    string name = Path.GetFileNameWithoutExtension(stepPath);

                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Converting STEP files",
                            $"{name}  ({i + 1} of {stepAssetPaths.Count})",
                            i / (float)stepAssetPaths.Count))
                    {
                        cancelled = true;
                        break;
                    }

                    string objAssetPath = $"{PartsFolder}/{Sanitise(name)}.obj";
                    string absStep = Path.Combine(projectRoot, stepPath);
                    string absObj = Path.Combine(projectRoot, objAssetPath);

                    if (RunConverter(freeCad, script, absStep, absObj, out string error))
                    {
                        converted.Add(objAssetPath);
                        AssetDatabase.DeleteAsset(stepPath);
                    }
                    else
                    {
                        // Left in place on failure so the source is not lost
                        // and the problem can be retried after fixing it.
                        Debug.LogError($"[STEP] {name} failed to convert: {error}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();

            if (converted.Count > 0)
            {
                PartLibraryBuilder.Rebuild();
                Debug.Log(
                    $"[STEP] Converted {converted.Count} part(s) into {PartsFolder} " +
                    "and removed the source STEP files." +
                    (cancelled ? " Batch was cancelled early." : ""));
            }
        }

        private static bool RunConverter(
            string freeCadExe, string script, string stepPath, string objPath, out string error)
        {
            var info = new ProcessStartInfo
            {
                FileName = freeCadExe,
                Arguments = $"\"{script}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Parameters go through the environment because freecadcmd treats
            // trailing command-line arguments as documents to open - it would
            // try to open the not-yet-existing output file and fail.
            info.EnvironmentVariables["STEP_IN"] = stepPath;
            info.EnvironmentVariables["OBJ_OUT"] = objPath;

            try
            {
                using var process = Process.Start(info);
                if (process == null)
                {
                    error = "Process failed to start.";
                    return false;
                }

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!File.Exists(objPath))
                {
                    // FreeCAD exits 0 even when a script raises, so the output
                    // file's existence is the only trustworthy success signal.
                    error = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    return false;
                }

                foreach (string line in stdout.Split('\n'))
                {
                    // Surface the measured dimensions - the number that proves
                    // the scale is right.
                    if (line.Contains(" mm  =  "))
                    {
                        Debug.Log($"[STEP] {line.Trim()}");
                    }
                }

                error = null;
                return true;
            }
            catch (System.Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        private static string Sanitise(string name)
        {
            // Unity is unhappy with several characters that Windows allows in
            // file names, and VEX downloads arrive with spaces and brackets.
            var cleaned = name.Trim().ToLowerInvariant()
                .Replace(' ', '-')
                .Replace('(', '-')
                .Replace(')', '-')
                .Replace('#', '-');

            while (cleaned.Contains("--"))
            {
                cleaned = cleaned.Replace("--", "-");
            }

            return cleaned.Trim('-');
        }

        private static string FindFreeCad()
        {
            string saved = EditorPrefs.GetString(FreeCadPathPref, string.Empty);
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
            {
                return saved;
            }

            var roots = new[]
            {
                @"C:\Program Files",
                @"C:\Program Files (x86)",
            };

            foreach (string root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string dir in Directory.GetDirectories(root, "FreeCAD*"))
                {
                    string exe = Path.Combine(dir, "bin", "freecadcmd.exe");
                    if (File.Exists(exe))
                    {
                        EditorPrefs.SetString(FreeCadPathPref, exe);
                        return exe;
                    }
                }
            }

            return null;
        }
    }
}
