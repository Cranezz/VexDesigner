namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Creates and refreshes <see cref="PartDefinition"/> assets from the
    /// meshes in Assets/Parts.
    ///
    /// The library lives under Resources so the shelf can load it at runtime.
    /// That is what makes converting a STEP file enough on its own: the part
    /// appears on the shelf with no scene rebuild and nothing to wire up.
    ///
    /// Existing definitions are updated in place rather than recreated, so
    /// hand-edited values survive a rebuild.
    /// </summary>
    public static class PartLibraryBuilder
    {
        private const string PartsFolder = "Assets/Parts";
        private const string LibraryFolder = "Assets/Resources/PartLibrary";

        /// <summary>
        /// Measured part masses in grams, keyed by VEX SKU.
        ///
        /// Keyed by SKU rather than file name because the SKU is the stable
        /// identifier - renaming a downloaded file must not silently drop a
        /// part back to a default mass.
        ///
        /// Screw figures come from the weight table in Protobot Rebuilt, where
        /// they were measured rather than calculated. The C-channel figure is
        /// VEX's published 0.157 lb.
        /// </summary>
        private static readonly Dictionary<string, float> MassGramsBySku =
            new Dictionary<string, float>
            {
                // 8-32 star drive screws, by length.
                { "276-4990", 0.5f },   // 1/4"
                { "276-4991", 0.6f },   // 3/8"
                { "276-4992", 0.7f },   // 1/2"
                { "276-4993", 0.8f },   // 5/8"
                { "276-4994", 0.9f },   // 3/4"
                { "276-4995", 1.0f },   // 7/8"
                { "276-4996", 1.2f },   // 1.00"
                { "276-4997", 1.4f },   // 1.25"
                { "276-4998", 1.6f },   // 1.50"
                { "276-4999", 1.8f },   // 1.75"
                { "276-5004", 2.0f },   // 2.00"
                { "276-8015", 2.2f },   // 2.25"
                { "276-8016", 2.4f },   // 2.50"

                // Structure.
                { "276-2289", 71.2f },  // 1x2x1x35 C-channel, 0.157 lb
            };

        [MenuItem("VexDesigner/Rebuild Part Library")]
        public static void RebuildMenuItem()
        {
            int count = Rebuild();
            Debug.Log($"[PartLibrary] {count} part definition(s) up to date.");
        }

        public static int Rebuild()
        {
            EnsureFolder(LibraryFolder);

            if (!AssetDatabase.IsValidFolder(PartsFolder))
            {
                Debug.LogWarning($"[PartLibrary] {PartsFolder} does not exist yet.");
                return 0;
            }

            string[] meshGuids = AssetDatabase.FindAssets("t:Mesh", new[] { PartsFolder });
            var seen = new HashSet<string>();
            int count = 0;
            int unweighed = 0;

            foreach (string guid in meshGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string key = Path.GetFileNameWithoutExtension(path);

                // A model file can contain several sub-meshes; the first is the
                // part and the rest are usually helper geometry.
                if (!seen.Add(key))
                {
                    continue;
                }

                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    continue;
                }

                if (!UpdateOrCreate(key, mesh))
                {
                    unweighed++;
                }

                count++;
            }

            AssetDatabase.SaveAssets();

            if (unweighed > 0)
            {
                Debug.LogWarning(
                    $"[PartLibrary] {unweighed} part(s) have no measured mass and are " +
                    "using a placeholder. Add their SKU to MassGramsBySku - mass drives " +
                    "the physics, so a wrong value shows up as odd behaviour rather " +
                    "than as an obvious error.");
            }

            return count;
        }

        /// <summary>Returns false when no measured mass was available.</summary>
        private static bool UpdateOrCreate(string key, Mesh mesh)
        {
            string assetPath = $"{LibraryFolder}/Part_{key}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(assetPath);

            bool isNew = definition == null;
            if (isNew)
            {
                definition = ScriptableObject.CreateInstance<PartDefinition>();
                definition.partId = key;
                definition.displayName = Prettify(key);
            }

            // The mesh reference is always refreshed: re-exporting a part at a
            // different tessellation replaces the mesh, and a definition still
            // pointing at the old one would silently use stale geometry.
            definition.mesh = mesh;

            bool weighed = TryApplyMass(definition, key);

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
                $"{definition.massGrams:F2} g{(weighed ? "" : "  (placeholder)")}");

            return weighed;
        }

        /// <summary>Density of 6061 aluminium, grams per cubic centimetre.</summary>
        private const float AluminiumDensity = 2.70f;

        private static bool TryApplyMass(PartDefinition definition, string key)
        {
            foreach (KeyValuePair<string, float> entry in MassGramsBySku)
            {
                if (key.Contains(entry.Key))
                {
                    definition.massGrams = entry.Value;
                    return true;
                }
            }

            // No published figure: estimate from the mesh's own volume. Far
            // better than a flat placeholder, since it at least scales with the
            // part - a C-channel and a screw will not both come out at 100 g.
            //
            // Reported as unmeasured regardless, because it assumes solid
            // aluminium and so overestimates anything hollow, plastic or steel.
            float grams = EstimateMassGrams(definition.mesh);
            if (grams > 0f)
            {
                definition.massGrams = grams;
            }

            return false;
        }

        /// <summary>
        /// Mesh volume by the signed-tetrahedron sum: each triangle forms a
        /// tetrahedron with the origin, and the signed volumes cancel out
        /// everywhere except the enclosed solid. Correct for any closed mesh
        /// regardless of where the origin sits relative to it.
        /// </summary>
        private static float EstimateMassGrams(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable)
            {
                return 0f;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            double volume = 0.0;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }

            // Mesh units are metres. 1 m3 is 1e6 cm3.
            double cubicCentimetres = System.Math.Abs(volume) * 1e6;
            return (float)(cubicCentimetres * AluminiumDensity);
        }

        private static string Prettify(string key)
        {
            return key.Replace('-', ' ').Replace('_', '/');
        }

        private static void EnsureFolder(string unityPath)
        {
            if (AssetDatabase.IsValidFolder(unityPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(unityPath)!.Replace('\\', '/');
            string leaf = Path.GetFileName(unityPath);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
