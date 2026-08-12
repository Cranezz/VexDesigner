namespace VexDesigner.EditorTools
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Creates the workshop's material assets from the generated textures.
    ///
    /// Central so that tiling density is decided in one place. Tiling is
    /// expressed in *inches per texture repeat* rather than as a raw repeat
    /// count, because that is the only way the same material looks right on a
    /// 20 ft wall and a 2 ft cabinet door - a fixed repeat count would stretch
    /// one and cram the other.
    /// </summary>
    public static class WorkshopMaterials
    {
        private const string Folder = "Assets/Materials";
        private const string TexturesFolder = "Assets/Textures";
        private const float InchesToMetres = 0.0254f;

        public sealed class Surface
        {
            public Material Material;

            /// <summary>Inches of surface covered by one texture repeat.</summary>
            public float InchesPerTile;

            /// <summary>Tiling for a face of the given size, in inches.</summary>
            public Vector2 TilingFor(float widthIn, float heightIn)
            {
                return new Vector2(widthIn / InchesPerTile, heightIn / InchesPerTile);
            }
        }

        public static Surface Concrete { get; private set; }
        public static Surface Drywall { get; private set; }
        public static Surface CinderBlock { get; private set; }
        public static Surface BenchWood { get; private set; }
        public static Surface PaintedMetal { get; private set; }
        public static Surface Pegboard { get; private set; }
        public static Surface CuttingMat { get; private set; }
        public static Surface Steel { get; private set; }
        public static Surface Aluminium { get; private set; }
        public static Surface CabinetBlue { get; private set; }

        public static void BuildAll()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            Concrete = Textured("Concrete", "Concrete", 96f, roughness: 0.93f);
            Drywall = Textured("Drywall", "Drywall", 72f, roughness: 0.95f);
            CinderBlock = Textured("CinderBlock", "CinderBlock", 48f, roughness: 0.92f);
            BenchWood = Textured("BenchWood", "BenchWood", 36f, roughness: 0.62f);
            PaintedMetal = Textured("PaintedMetal", "PaintedMetal", 48f, roughness: 0.42f,
                tint: new Color(0.78f, 0.79f, 0.80f));
            Pegboard = Textured("Pegboard", "Pegboard", 16f, roughness: 0.85f);

            CuttingMat = Textured("CuttingMat", "CuttingMat", 6f, roughness: 0.80f,
                hasNormal: false);

            // Untextured metals. Real steel and aluminium at this scale read
            // through their reflectance far more than through surface detail,
            // so a texture would add cost for nothing.
            Steel = Flat("Steel", new Color(0.32f, 0.34f, 0.37f), roughness: 0.45f, metallic: 0.9f);
            Aluminium = Flat("Aluminium", new Color(0.68f, 0.70f, 0.74f), roughness: 0.42f, metallic: 0.88f);
            CabinetBlue = Flat("CabinetBlue", new Color(0.16f, 0.25f, 0.42f), roughness: 0.45f, metallic: 0.15f);

            AssetDatabase.SaveAssets();
        }

        private static Surface Textured(
            string name, string textureName, float inchesPerTile, float roughness,
            Color? tint = null, bool hasNormal = true)
        {
            Material mat = CreateOrLoad(name);

            ApplyCommon(mat, tint ?? Color.white, roughness, metallic: 0f);

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(
                $"{TexturesFolder}/{textureName}_Albedo.png");

            if (albedo != null && mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", albedo);
            }
            else if (albedo == null)
            {
                Debug.LogWarning(
                    $"[Materials] Missing {textureName}_Albedo. Run " +
                    "VexDesigner > Regenerate Workshop Textures.");
            }

            if (hasNormal)
            {
                var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"{TexturesFolder}/{textureName}_Normal.png");

                if (normal != null && mat.HasProperty("_BumpMap"))
                {
                    // The keyword has to be enabled explicitly or URP ignores
                    // the map entirely, with no warning.
                    mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_BumpMap", normal);
                    mat.SetFloat("_BumpScale", 1f);
                }
            }

            EditorUtility.SetDirty(mat);
            return new Surface { Material = mat, InchesPerTile = inchesPerTile };
        }

        private static Surface Flat(string name, Color colour, float roughness, float metallic)
        {
            Material mat = CreateOrLoad(name);
            ApplyCommon(mat, colour, roughness, metallic);
            EditorUtility.SetDirty(mat);
            return new Surface { Material = mat, InchesPerTile = 12f };
        }

        private static void ApplyCommon(Material mat, Color colour, float roughness, float metallic)
        {
            if (mat.HasProperty("_BaseColor")) { mat.SetColor("_BaseColor", colour); }
            if (mat.HasProperty("_Color")) { mat.SetColor("_Color", colour); }
            if (mat.HasProperty("_Smoothness")) { mat.SetFloat("_Smoothness", 1f - roughness); }
            if (mat.HasProperty("_Glossiness")) { mat.SetFloat("_Glossiness", 1f - roughness); }
            if (mat.HasProperty("_Metallic")) { mat.SetFloat("_Metallic", metallic); }

            // Emission on but black: invisible until Highlightable raises it.
            // The keyword cannot be set from a property block, so anything that
            // might ever be hovered has to have it enabled here.
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (mat.HasProperty("_EmissionColor")) { mat.SetColor("_EmissionColor", Color.black); }
        }

        private static Material CreateOrLoad(string name)
        {
            string path = $"{Folder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[Materials] URP Lit shader not found; falling back to the " +
                    "default shader. Is Universal RP installed and assigned in " +
                    "Project Settings > Graphics?");
                shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            }

            var mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>
        /// Flat coloured face for a wall button. Slightly emissive so the
        /// label stays readable in the shadow the pegboard casts, without
        /// relying on a light happening to reach that corner.
        /// </summary>
        public static Material CreateButtonFace(string name, Color colour)
        {
            Material mat = CreateOrLoad($"Button_{name}");
            ApplyCommon(mat, colour, roughness: 0.55f, metallic: 0f);

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", colour * 0.35f);
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Emissive material for the fluorescent tubes. Separate because it is
        /// the one surface that should be bright regardless of lighting.
        /// </summary>
        public static Material CreateLightTube()
        {
            Material mat = CreateOrLoad("LightTube");
            ApplyCommon(mat, new Color(0.95f, 0.96f, 0.92f), roughness: 0.3f, metallic: 0f);

            if (mat.HasProperty("_EmissionColor"))
            {
                // Above 1 so it blooms in HDR and reads as a light source
                // rather than a white-painted box.
                mat.SetColor("_EmissionColor", new Color(1.9f, 1.92f, 1.8f));
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
