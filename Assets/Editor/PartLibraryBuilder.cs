namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Creates and refreshes <see cref="PartDefinition"/> assets from the
    /// meshes sitting in Assets/Parts.
    ///
    /// Existing definitions are updated in place rather than recreated, so
    /// hand-entered values - masses especially - survive a rebuild. Losing
    /// those on every regeneration would make the catalogue useless.
    /// </summary>
    public static class PartLibraryBuilder
    {
        private const string PartsFolder = "Assets/Parts";
        private const string LibraryFolder = "Assets/PartLibrary";

        /// <summary>
        /// Known real-world masses in grams, keyed by mesh file name.
        ///
        /// PLACEHOLDER VALUES, estimated from aluminium density and part
        /// volume. Replace with the published VEX figures - mass drives the
        /// physics, so a wrong value shows up as a robot that behaves oddly
        /// rather than as an obvious error.
        /// </summary>
        private static readonly Dictionary<string, float> KnownMassGrams =
            new Dictionary<string, float>
            {
                { "c-channel-1x2x1x35", 85f },
            };

        [MenuItem("VexDesigner/Rebuild Part Library")]
        public static void RebuildMenuItem()
        {
            int count = Rebuild();
            Debug.Log($"[PartLibrary] {count} part definition(s) up to date.");
        }

        public static int Rebuild()
        {
            if (!AssetDatabase.IsValidFolder(LibraryFolder))
            {
                AssetDatabase.CreateFolder("Assets", "PartLibrary");
            }

            if (!AssetDatabase.IsValidFolder(PartsFolder))
            {
                Debug.LogWarning($"[PartLibrary] {PartsFolder} does not exist yet.");
                return 0;
            }

            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { PartsFolder });
            var seen = new HashSet<string>();
            int count = 0;

            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string key = Path.GetFileNameWithoutExtension(path);

                // A model file can contain several sub-meshes; the first is the
                // part itself and the rest are usually helper geometry.
                if (!seen.Add(key))
                {
                    continue;
                }

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    continue;
                }

                UpdateOrCreate(key, mesh);
                count++;
            }

            AssetDatabase.SaveAssets();
            return count;
        }

        private static void UpdateOrCreate(string key, Mesh mesh)
        {
            string assetPath = $"{LibraryFolder}/Part_{key}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(assetPath);

            bool isNew = definition == null;
            if (isNew)
            {
                definition = ScriptableObject.CreateInstance<PartDefinition>();
                definition.partId = key;
                definition.displayName = Prettify(key);

                if (KnownMassGrams.TryGetValue(key, out float grams))
                {
                    definition.massGrams = grams;
                }
            }

            // The mesh reference is always refreshed: re-exporting a part at a
            // different tessellation replaces the mesh, and a definition still
            // pointing at the old one would silently keep using stale geometry.
            definition.mesh = mesh;

            if (isNew)
            {
                AssetDatabase.CreateAsset(definition, assetPath);
            }
            else
            {
                EditorUtility.SetDirty(definition);
            }

            Debug.Log(
                $"[PartLibrary] {definition.displayName}: " +
                $"{definition.LongestDimensionInches:F3} in long, " +
                $"{definition.massGrams:F0} g");
        }

        private static string Prettify(string key)
        {
            return key.Replace('-', ' ').Replace('_', ' ');
        }
    }
}
