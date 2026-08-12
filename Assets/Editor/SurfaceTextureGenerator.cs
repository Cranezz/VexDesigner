namespace VexDesigner.EditorTools
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Generates the workshop's surface textures procedurally: an albedo map
    /// and a matching normal map for each material.
    ///
    /// The normal maps are what separate this from the earlier flat-colour
    /// pass. A concrete floor with a uniform colour reads as grey plastic no
    /// matter how good the lighting is, because real surfaces are legible
    /// almost entirely through how light catches their relief. Every surface
    /// here is therefore built as a *height field* first; colour and normals
    /// are both derived from it, so the bumps and the shading agree.
    ///
    /// Generated rather than downloaded so the repository stays self-contained
    /// with no licence questions, and so every texture tiles seamlessly.
    /// </summary>
    public static class SurfaceTextureGenerator
    {
        private const string Folder = "Assets/Textures";
        private const int Size = 1024;

        [MenuItem("VexDesigner/Regenerate Workshop Textures")]
        public static void GenerateMenuItem()
        {
            Generate();
            Debug.Log("[Textures] Regenerated all workshop surfaces.");
        }

        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets", "Textures");
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                BuildSurface("Concrete", Concrete, normalStrength: 2.2f);
                BuildSurface("Drywall", Drywall, normalStrength: 1.1f);
                BuildSurface("CinderBlock", CinderBlock, normalStrength: 5.5f);
                BuildSurface("BenchWood", BenchWood, normalStrength: 1.8f);
                BuildSurface("PaintedMetal", PaintedMetal, normalStrength: 1.4f);
                BuildSurface("Pegboard", Pegboard, normalStrength: 4.0f);
                BuildCuttingMat();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        // ------------------------------------------------------------------
        // Surfaces. Each fills a height field and a colour buffer together, so
        // the two can never drift out of agreement.
        // ------------------------------------------------------------------

        private delegate void SurfaceFunc(int x, int y, out float height, out Color albedo);

        private static void Concrete(int x, int y, out float height, out Color albedo)
        {
            float u = x / (float)Size;
            float v = y / (float)Size;

            // Several scales at once. Concrete without broad blotching reads as
            // noise; without fine grain it reads as plastic.
            float broad = Fbm(u, v, 4, 3, 11);
            float mid = Fbm(u * 4f, v * 4f, 4, 12, 23);
            float fine = Fbm(u * 18f, v * 18f, 3, 48, 31);

            height = (broad * 0.45f) + (mid * 0.35f) + (fine * 0.20f);

            // Sparse pits, the kind a trowelled slab always has.
            float pit = Hash01(x / 3, y / 3, 777);
            if (pit > 0.9975f)
            {
                height -= 0.35f;
            }

            float tone = 0.62f + (height * 0.34f);
            albedo = new Color(tone, tone * 0.995f, tone * 0.97f, 1f);

            // Aggregate flecks.
            float fleck = Hash01(x, y, 4242);
            if (fleck > 0.9965f) { albedo *= 0.62f; }
            else if (fleck < 0.0022f) { albedo *= 1.28f; }
        }

        private static void Drywall(int x, int y, out float height, out Color albedo)
        {
            float u = x / (float)Size;
            float v = y / (float)Size;

            // Orange-peel roller texture: fine, shallow, slightly directional.
            float peel = Fbm(u * 22f, v * 22f, 3, 64, 5);
            float sag = Fbm(u * 2f, v * 3f, 3, 6, 9);

            height = (peel * 0.75f) + (sag * 0.25f);

            float tone = 0.74f + (height * 0.10f) - (sag * 0.06f);
            albedo = new Color(tone, tone * 0.985f, tone * 0.95f, 1f);
        }

        private static void CinderBlock(int x, int y, out float height, out Color albedo)
        {
            // 16 x 8 inch blocks with a 3/8 inch mortar joint. One tile covers
            // 48 inches, so three courses of three blocks.
            const int cols = 3;
            const int rows = 6;

            int blockW = Size / cols;
            int blockH = Size / rows;

            int row = y / blockH;

            // Running bond: every other course offset by half a block.
            int xOffset = (row % 2 == 0) ? 0 : blockW / 2;
            int localX = ((x + xOffset) % Size) % blockW;
            int localY = y % blockH;

            float mortar = Size * 0.006f;
            float edge = Mathf.Min(
                Mathf.Min(localX, blockW - localX),
                Mathf.Min(localY, blockH - localY));

            bool isJoint = edge < mortar;

            float u = x / (float)Size;
            float v = y / (float)Size;
            float grain = Fbm(u * 26f, v * 26f, 3, 70, 17);

            if (isJoint)
            {
                // Joints sit back from the block face and are smoother.
                height = 0.12f + (grain * 0.10f);
                float tone = 0.60f + (grain * 0.10f);
                albedo = new Color(tone, tone * 0.99f, tone * 0.96f, 1f);
            }
            else
            {
                float face = Fbm(u * 7f, v * 7f, 4, 20, 3);
                height = 0.62f + (face * 0.22f) + (grain * 0.16f);

                // Slight per-block colour variation, keyed off the block index
                // so a whole block shifts together rather than pixel by pixel.
                int blockId = ((x + xOffset) / blockW) + (row * 31);
                float shift = (Hash01(blockId, row, 55) - 0.5f) * 0.06f;

                float tone = 0.70f + (face * 0.13f) + shift;
                albedo = new Color(tone, tone * 0.995f, tone * 0.97f, 1f);
            }
        }

        private static void BenchWood(int x, int y, out float height, out Color albedo)
        {
            float u = x / (float)Size;
            float v = y / (float)Size;

            const int planks = 4;
            int plank = Mathf.FloorToInt(v * planks);
            float within = (v * planks) - plank;
            float offset = plank * 0.41f;

            float warp = Fbm(u * 2f, v * 9f, 4, 5, plank) * 2.4f;
            float rings = Mathf.Abs(Mathf.Sin(((within * 8f) + warp + offset) * Mathf.PI));
            rings = Mathf.Pow(rings, 0.6f);

            float fibre = Fbm(u * 55f, v * 7f, 3, 90, plank + 61);

            height = (rings * 0.55f) + (fibre * 0.45f);

            var light = new Color(0.60f, 0.43f, 0.26f);
            var dark = new Color(0.36f, 0.24f, 0.13f);
            albedo = Color.Lerp(dark, light, Mathf.Clamp01(rings + (fibre * 0.22f)));

            // Plank seams: a groove, not just a dark line.
            float edge = Mathf.Min(within, 1f - within);
            if (edge < 0.028f)
            {
                float t = Mathf.SmoothStep(0f, 1f, edge / 0.028f);
                height *= Mathf.Lerp(0.05f, 1f, t);
                albedo = Color.Lerp(new Color(0.15f, 0.09f, 0.05f), albedo, t);
            }
        }

        private static void PaintedMetal(int x, int y, out float height, out Color albedo)
        {
            float u = x / (float)Size;
            float v = y / (float)Size;

            // Very shallow: painted sheet steel is nearly flat, and overdoing
            // this is what makes game metal look like hammered tin.
            float orangePeel = Fbm(u * 30f, v * 30f, 3, 90, 13);
            float dent = Fbm(u * 3f, v * 3f, 3, 8, 27);

            height = (orangePeel * 0.7f) + (dent * 0.3f);

            float tone = 0.80f + (height * 0.08f);
            albedo = new Color(tone, tone, tone, 1f);
        }

        private static void Pegboard(int x, int y, out float height, out Color albedo)
        {
            // Quarter-inch holes on one-inch centres. One tile covers 16 in.
            const int holesPerTile = 16;
            int spacing = Size / holesPerTile;
            float radius = spacing * 0.16f;

            int cx = (x % spacing) - (spacing / 2);
            int cy = (y % spacing) - (spacing / 2);
            float dist = Mathf.Sqrt((cx * cx) + (cy * cy));

            float u = x / (float)Size;
            float v = y / (float)Size;
            float grain = Fbm(u * 40f, v * 40f, 3, 100, 71);

            if (dist < radius)
            {
                // Holes read as depth. Genuinely piercing the mesh would cost
                // thousands of triangles for something only ever seen at a
                // distance.
                height = 0.0f;
                albedo = new Color(0.06f, 0.05f, 0.05f, 1f);
            }
            else
            {
                float rim = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((dist - radius) / (radius * 0.6f)));
                height = 0.35f + (rim * 0.55f) + (grain * 0.10f);

                float tone = 0.56f + (grain * 0.12f);
                albedo = new Color(tone, tone * 0.82f, tone * 0.60f, 1f);
            }
        }

        // ------------------------------------------------------------------
        // The cutting mat keeps its exact one-inch grid. It is the scene's
        // ruler, so it is generated at a fixed pixel-per-inch ratio rather
        // than as a generic surface.
        // ------------------------------------------------------------------

        private static void BuildCuttingMat()
        {
            const int inchesPerTile = 6;
            const int pixelsPerInch = 128;
            int size = inchesPerTile * pixelsPerInch;

            var pixels = new Color[size * size];
            var baseColour = new Color(0.075f, 0.085f, 0.095f);
            var minorLine = new Color(0.58f, 0.61f, 0.63f);
            var majorLine = new Color(0.86f, 0.88f, 0.90f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float grain = (Hash01(x, y, 5150) - 0.5f) * 0.022f;
                    Color c = baseColour + new Color(grain, grain, grain, 0f);

                    float dMinor = Mathf.Min(
                        GridDistance(x, pixelsPerInch), GridDistance(y, pixelsPerInch));
                    float dMajor = Mathf.Min(GridDistance(x, size), GridDistance(y, size));

                    if (dMinor < 1.6f) { c = Color.Lerp(minorLine, c, dMinor / 1.6f); }
                    if (dMajor < 3.0f) { c = Color.Lerp(majorLine, c, dMajor / 3.0f); }

                    c.a = 1f;
                    pixels[(y * size) + x] = c;
                }
            }

            WritePng("CuttingMat_Albedo", pixels, size, isNormalMap: false);
        }

        private static float GridDistance(int coordinate, int spacing)
        {
            int within = coordinate % spacing;
            return Mathf.Min(within, spacing - within);
        }

        // ------------------------------------------------------------------
        // Height field -> albedo + normal
        // ------------------------------------------------------------------

        private static void BuildSurface(string name, SurfaceFunc surface, float normalStrength)
        {
            var height = new float[Size * Size];
            var albedo = new Color[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    surface(x, y, out float h, out Color c);
                    int i = (y * Size) + x;
                    height[i] = h;
                    albedo[i] = c;
                }
            }

            WritePng($"{name}_Albedo", albedo, Size, isNormalMap: false);
            WritePng($"{name}_Normal", HeightToNormal(height, normalStrength), Size, isNormalMap: true);
        }

        /// <summary>
        /// Sobel-filtered height into a tangent-space normal map.
        ///
        /// Sampling wraps at the edges, which is what keeps the normals
        /// continuous across a tile seam. Without that, every tiled surface
        /// shows a hard grid of lighting discontinuities.
        /// </summary>
        private static Color[] HeightToNormal(float[] height, float strength)
        {
            var normals = new Color[Size * Size];

            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float tl = H(height, x - 1, y - 1), t = H(height, x, y - 1), tr = H(height, x + 1, y - 1);
                    float l = H(height, x - 1, y), r = H(height, x + 1, y);
                    float bl = H(height, x - 1, y + 1), b = H(height, x, y + 1), br = H(height, x + 1, y + 1);

                    float dx = (tr + (2f * r) + br) - (tl + (2f * l) + bl);
                    float dy = (bl + (2f * b) + br) - (tl + (2f * t) + tr);

                    var n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;

                    // Pack -1..1 into 0..1. Unity's normal map importer expects
                    // this encoding and will re-expand it.
                    normals[(y * Size) + x] = new Color(
                        (n.x * 0.5f) + 0.5f,
                        (n.y * 0.5f) + 0.5f,
                        (n.z * 0.5f) + 0.5f,
                        1f);
                }
            }

            return normals;
        }

        private static float H(float[] height, int x, int y)
        {
            x = ((x % Size) + Size) % Size;
            y = ((y % Size) + Size) % Size;
            return height[(y * Size) + x];
        }

        // ------------------------------------------------------------------
        // Noise
        // ------------------------------------------------------------------

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (seed * 1013904223);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)0x7fffffff;
            }
        }

        /// <summary>Value noise on a wrapping lattice, so results tile.</summary>
        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;

            float a = Hash01(Wrap(x0, period), Wrap(y0, period), seed);
            float bb = Hash01(Wrap(x0 + 1, period), Wrap(y0, period), seed);
            float c = Hash01(Wrap(x0, period), Wrap(y0 + 1, period), seed);
            float d = Hash01(Wrap(x0 + 1, period), Wrap(y0 + 1, period), seed);

            fx = fx * fx * (3f - (2f * fx));
            fy = fy * fy * (3f - (2f * fy));

            return Mathf.Lerp(Mathf.Lerp(a, bb, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float x, float y, int octaves, int basePeriod, int seed)
        {
            float sum = 0f, amplitude = 1f, total = 0f, frequency = basePeriod;
            int period = basePeriod;

            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x * frequency, y * frequency, period, seed + i) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
                period *= 2;
            }

            return sum / total;
        }

        private static int Wrap(int value, int period) => ((value % period) + period) % period;

        // ------------------------------------------------------------------
        // Output
        // ------------------------------------------------------------------

        private static void WritePng(string name, Color[] pixels, int size, bool isNormalMap)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();

            string path = $"{Folder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                return;
            }

            if (isNormalMap)
            {
                // Marking the type matters: Unity packs normal maps
                // differently and treats them as linear. Left as Default they
                // are gamma-corrected and the lighting comes out wrong in a
                // way that is hard to diagnose by eye.
                importer.textureType = TextureImporterType.NormalMap;
                importer.sRGBTexture = false;
            }
            else
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
            }

            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }
    }
}
