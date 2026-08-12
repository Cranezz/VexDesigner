namespace VexDesigner.EditorTools
{
    using System;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Regenerates the .csproj files Unity produces for IDEs.
    ///
    /// The point is not IDE support - it is that once those files exist, the
    /// scripts can be type-checked with `dotnet build` **without opening
    /// Unity**. Unity holds an exclusive lock on the project, so otherwise the
    /// only way to know whether a change compiles is to close the editor,
    /// which is a poor trade for a check that should take seconds.
    ///
    /// See tools/typecheck.sh.
    /// </summary>
    public static class ProjectFilesGenerator
    {
        [MenuItem("VexDesigner/Regenerate C# Project Files")]
        public static void GenerateMenuItem()
        {
            if (Generate())
            {
                Debug.Log("[ProjectFiles] Regenerated .csproj files.");
            }
        }

        public static void GenerateFromCommandLine()
        {
            if (!Generate())
            {
                EditorApplication.Exit(1);
            }
        }

        private static bool Generate()
        {
            // Unity's solution sync is internal and has moved between versions,
            // so try the known entry points in turn rather than binding to one.
            if (TryCodeEditorApi() || TrySyncVsReflection())
            {
                return true;
            }

            Debug.LogError(
                "[ProjectFiles] Could not trigger project file generation. " +
                "Open the project and use Edit > Preferences > External Tools > " +
                "Regenerate project files instead.");
            return false;
        }

        private static bool TryCodeEditorApi()
        {
            try
            {
                Type codeEditor = Type.GetType(
                    "Unity.CodeEditor.CodeEditor, Unity.CodeEditor");

                PropertyInfo current = codeEditor?.GetProperty(
                    "CurrentEditor", BindingFlags.Public | BindingFlags.Static);

                object editor = current?.GetValue(null);
                MethodInfo syncAll = editor?.GetType().GetMethod("SyncAll");

                if (syncAll == null)
                {
                    return false;
                }

                syncAll.Invoke(editor, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProjectFiles] CodeEditor path failed: {e.Message}");
                return false;
            }
        }

        private static bool TrySyncVsReflection()
        {
            try
            {
                Type syncVs = Type.GetType("UnityEditor.SyncVS, UnityEditor");
                MethodInfo sync = syncVs?.GetMethod(
                    "SyncSolution",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

                if (sync == null)
                {
                    return false;
                }

                sync.Invoke(null, null);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProjectFiles] SyncVS path failed: {e.Message}");
                return false;
            }
        }
    }
}
