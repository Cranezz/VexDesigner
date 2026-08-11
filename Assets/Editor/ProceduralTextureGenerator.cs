namespace VexDesigner.EditorTools
{
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Generates the workshop's surface textures procedurally and writes them
    /// out as PNG assets.
    ///
    /// Generated rather than downloaded for three reasons: the repository stays
    /// self-contained with no licence questions, the textures are guaranteed
    /// tileable, and - the important one - the cutting mat's grid can be
    /// generated at an exact pixel-per-inch ratio.
    ///
    /// That last point matters more than it sounds. The mat is not decoration;
    /// it is the scene's ruler. If its squares are exactly one inch, then any
    /// imported part can be checked against it by eye. A 17.5" C-channel must
    /// span 17.5 squares. Anything else means the import scale is wrong, and
    /// you find out immediately instead of after building half a robot.
    /// </summary>
    public static class ProceduralTextureGenerator
    {
        private const string TexturesFolder = "Assets/Textures";

        // The mat tile is six inches square at 128 pixels per inch. Six divides
        // evenly into the mat's 36 x 30 inch size, so the tile repeats a whole
        // number of times and no grid line ever lands mid-square.
        private const int MatInchesPerTile = 6;
        private const int MatPixelsPerInch = 128;

        private const int SurfaceSize = 1024;

        [MenuItem("VexDesigner/Regenerate Workshop Textures")]
        public static void GenerateMenuItem()
        {
            Generate();
            Debug.Log("[Textures] Regenerated workshop textures.");
        }

        public static void GenerateFromCommandLine()
        {
            try
            {
                Generate();
                Debug.Log("[Textures] Generated successfully.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Textures] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        public static void Generate()
        {
            if (!AssetDatabase.IsValidFolder(TexturesFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Textures");
            }

            WritePng("Wood_Albedo", BuildWood(SurfaceSize));
            WritePng("Concrete_Albedo", BuildConcrete(SurfaceSize));
            WritePng("CuttingMat_Albedo", BuildCuttingMat());

            AssetDatabase.Refresh();
        }

        // ------------------------------------------------------------------
        // Surfaces
        // ------------------------------------------------------------------

        private static Color[] BuildWood(int size)
        {
            var pixels = new Color[size * size];

            var light = new Color(0.62f, 0.44f, 0.26f);
            var dark = new Color(0.38f, 0.25f, 0.14f);
            var seam = new Color(0.18f, 0.11f, 0.06f);

            const int planks = 5;
            float plankHeight = 1f / planks;

            for (int y = 0; y < size; y++)
            {
                float v = y / (float)size;
                int plankIndex = Mathf.FloorToInt(v * planks);
                float withinPlank = (v * planks) - plankIndex;

                // Offset each plank so the grain does not line up across seams,
                // which is the main thing that makes tiled wood look fake.
                float plankOffset = plankIndex * 0.37f;

                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;

                    // Grain runs along the plank. The noise term warps the
                    // rings so they are not mechanically parallel.
                    float warp = Fbm(u * 2f, v * 8f, 4, 4, plankIndex) * 2.2f;
                    float rings = Mathf.Abs(Mathf.Sin(
                        ((withinPlank * 9f) + warp + plankOffset) * Mathf.PI));
                    rings = Mathf.Pow(rings, 0.55f);

                    // Fine lengthwise fibre.
                    float fibre = Fbm(u * 40f, v * 6f, 3, 16, plankIndex + 99) * 0.14f;

                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(rings + fibre));

                    // Darken towards the plank seams.
                    float edge = Mathf.Min(withinPlank, 1f - withinPlank);
                    if (edge < 0.035f)
                    {
                        c = Color.Lerp(seam, c, Mathf.SmoothStep(0f, 1f, edge / 0.035f));
                    }

                    pixels[(y * size) + x] = c;
                }
            }

            return pixels;
        }

        private static Color[] BuildConcrete(int size)
        {
            var pixels = new Color[size * size];

            var baseTone = new Color(0.40f, 0.40f, 0.42f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float v = y / (float)size;

                    // Broad blotching plus finer mottling. Concrete reads as
                    // wrong without variation at several scales at once.
                    float broad = Fbm(u, v, 4, 4, 7);
                    float fine = Fbm(u * 6f, v * 6f, 4, 24, 21);

                    float tone = 0.82f + (broad * 0.30f) + (fine * 0.14f);

                    Color c = baseTone * tone;

                    // Sparse aggregate speckle.
                    float speck = Hash01(x, y, 1337);
                    if (speck > 0.9955f)
                    {
                        c *= 0.55f;
                    }
                    else if (speck < 0.0025f)
                    {
                        c *= 1.35f;
                    }

                    c.a = 1f;
                    pixels[(y * size) + x] = c;
                }
            }

            return pixels;
        }

        private static Color[] BuildCuttingMat()
        {
            int size = MatInchesPerTile * MatPixelsPerInch;
            var pixels = new Color[size * size];

            var baseColour = new Color(0.085f, 0.095f, 0.105f);
            var minorLine = new Color(0.62f, 0.65f, 0.67f);
            var majorLine = new Color(0.88f, 0.90f, 0.92f);

            // Line widths in pixels. Kept odd-ish and small so a line reads as
            // a line rather than a stripe at typical viewing distance.
            const float minorWidth = 1.6f;
            const float majorWidth = 3.0f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Slight tonal noise so the mat is not a dead flat colour.
                    float grain = (Hash01(x, y, 5150) - 0.5f) * 0.02f;
                    Color c = baseColour + new Color(grain, grain, grain, 0f);

                    float dxMinor = DistanceToNearestGridLine(x, MatPixelsPerInch);
                    float dyMinor = DistanceToNearestGridLine(y, MatPixelsPerInch);
                    float dMinor = Mathf.Min(dxMinor, dyMinor);

                    // Major lines fall on the tile boundary, i.e. every six
                    // inches, matching a real cutting mat's heavier rules.
                    float dxMajor = DistanceToNearestGridLine(x, size);
                    float dyMajor = DistanceToNearestGridLine(y, size);
                    float dMajor = Mathf.Min(dxMajor, dyMajor);

                    if (dMinor < minorWidth)
                    {
                        c = Color.Lerp(minorLine, c, dMinor / minorWidth);
                    }

                    if (dMajor < majorWidth)
                    {
                        c = Color.Lerp(majorLine, c, dMajor / majorWidth);
                    }

                    c.a = 1f;
                    pixels[(y * size) + x] = c;
                }
            }

            return pixels;
        }

        /// <summary>
        /// Distance in pixels from <paramref name="coordinate"/> to the nearest
        /// multiple of <paramref name="spacing"/>, wrapping at the texture edge
        /// so lines stay continuous across tile seams.
        /// </summary>
        private static float DistanceToNearestGridLine(int coordinate, int spacing)
        {
            int within = coordinate % spacing;
            return Mathf.Min(within, spacing - within);
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

        /// <summary>
        /// Value noise on a wrapping lattice. The wrap is what makes the
        /// resulting textures tile without a visible seam.
        /// </summary>
        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;

            int wx0 = Wrap(x0, period);
            int wy0 = Wrap(y0, period);
            int wx1 = Wrap(x0 + 1, period);
            int wy1 = Wrap(y0 + 1, period);

            float a = Hash01(wx0, wy0, seed);
            float b = Hash01(wx1, wy0, seed);
            float c = Hash01(wx0, wy1, seed);
            float d = Hash01(wx1, wy1, seed);

            // Smoothstep the interpolant so the lattice does not show as a
            // grid of visible creases.
            fx = fx * fx * (3f - (2f * fx));
            fy = fy * fy * (3f - (2f * fy));

            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float x, float y, int octaves, int basePeriod, int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float total = 0f;
            int period = basePeriod;
            float frequency = basePeriod;

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

        private static int Wrap(int value, int period)
        {
            return ((value % period) + period) % period;
        }

        // ------------------------------------------------------------------
        // Output
        // ------------------------------------------------------------------

        private static void WritePng(string name, Color[] pixels)
        {
            int size = Mathf.RoundToInt(Mathf.Sqrt(pixels.Length));
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.SetPixels(pixels);
            texture.Apply();

            string path = $"{TexturesFolder}/{name}.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;

                // The mat's grid lines are thin and high contrast, so they
                // alias badly at grazing angles without heavy anisotropy.
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
        }
    }
}
