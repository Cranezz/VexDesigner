namespace VexDesigner.UI
{
    using UnityEngine;
    using UnityEngine.UI;
    using VexDesigner.Parts;

    /// <summary>
    /// The aiming dot, which becomes a hand when something can be interacted
    /// with.
    ///
    /// Both sprites are generated in code. A crosshair is a handful of pixels,
    /// and generating it avoids an import step, keeps it crisp at any
    /// resolution setting, and means there is no art dependency for a purely
    /// functional element.
    /// </summary>
    public sealed class Crosshair : MonoBehaviour
    {
        [SerializeField] private Image dot;
        [SerializeField] private Image hand;
        [SerializeField] private Image padlock;
        [SerializeField] private PartPlacementController placement;

        private static Sprite dotSprite;
        private static Sprite handSprite;
        private static Sprite padlockSprite;

        private void Awake()
        {
            if (placement == null)
            {
                placement = FindAnyObjectByType<PartPlacementController>();
            }
        }

        private void Update()
        {
            if (placement == null)
            {
                return;
            }

            // Three states, in priority order. The padlock wins because
            // "you are holding something that will not move" is the single
            // most useful thing to know at that moment.
            bool holdingFrozen = placement.IsCarrying && placement.CarriedIsFrozen;
            bool interactive = !holdingFrozen && (placement.HasTarget || placement.IsCarrying);

            if (dot != null) { dot.enabled = !interactive && !holdingFrozen; }
            if (hand != null) { hand.enabled = interactive; }
            if (padlock != null) { padlock.enabled = holdingFrozen; }
        }

        // ------------------------------------------------------------------
        // Generated sprites
        // ------------------------------------------------------------------

        public static Sprite GetDotSprite()
        {
            if (dotSprite != null)
            {
                return dotSprite;
            }

            const int size = 16;
            var pixels = new Color[size * size];
            float centre = (size - 1) * 0.5f;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre));

                    // Soft edge, plus a dark ring so the dot stays visible
                    // against both the pale concrete floor and dark shadow.
                    float core = Mathf.Clamp01(1f - Mathf.Max(0f, d - 2.2f));
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 3.6f));

                    Color c = Color.white * core;
                    c += new Color(0f, 0f, 0f, ring * 0.55f);
                    c.a = Mathf.Clamp01(core + (ring * 0.55f));
                    pixels[(y * size) + x] = c;
                }
            }

            dotSprite = BuildSprite(pixels, size, "CrosshairDot");
            return dotSprite;
        }

        public static Sprite GetHandSprite()
        {
            if (handSprite != null)
            {
                return handSprite;
            }

            // A blocky pointing-hand glyph. Drawn as a bitmap because at 32px
            // it is more legible hand-placed than any procedural curve.
            string[] rows =
            {
                "................................",
                "................................",
                "..............XX................",
                ".............X..X...............",
                ".............X..X...............",
                ".............X..X...............",
                ".............X..X.XX............",
                ".............X..X.X.X...........",
                ".............X..X.X.X.XX........",
                ".............X..X.X.X.X.X.......",
                "....XX.......X..X.X.X.X.X.......",
                "...X..X......X..............X...",
                "...X...X.....................X..",
                "....X...X....................X..",
                ".....X...X...................X..",
                "......X...X..................X..",
                ".......X.....................X..",
                "........X....................X..",
                ".........X..................X...",
                "..........X.................X...",
                "...........X...............X....",
                "............X.............X.....",
                ".............X...........X......",
                "..............XXXXXXXXXXX.......",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
            };

            const int size = 32;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                // Bitmap rows read top-down; texture rows read bottom-up.
                string row = rows[size - 1 - y];
                for (int x = 0; x < size; x++)
                {
                    bool on = x < row.Length && row[x] == 'X';
                    pixels[(y * size) + x] = on ? Color.white : Color.clear;
                }
            }

            handSprite = BuildSprite(pixels, size, "CrosshairHand");
            return handSprite;
        }

        public static Sprite GetPadlockSprite()
        {
            if (padlockSprite != null)
            {
                return padlockSprite;
            }

            // A closed padlock: shackle arch over a solid body.
            string[] rows =
            {
                "................................",
                "................................",
                "................................",
                "................................",
                "..........XXXXXXXXXX............",
                "........XX..........XX..........",
                ".......X..............X.........",
                "......X................X........",
                "......X................X........",
                "......X................X........",
                "......X................X........",
                "......X................X........",
                "....XXXXXXXXXXXXXXXXXXXXXX......",
                "....XXXXXXXXXXXXXXXXXXXXXX......",
                "....XXXXXXXXXXXXXXXXXXXXXX......",
                "....XXXXXXXXXX..XXXXXXXXXX......",
                "....XXXXXXXXX....XXXXXXXXX......",
                "....XXXXXXXXX....XXXXXXXXX......",
                "....XXXXXXXXXX..XXXXXXXXXX......",
                "....XXXXXXXXXX..XXXXXXXXXX......",
                "....XXXXXXXXXX..XXXXXXXXXX......",
                "....XXXXXXXXXXXXXXXXXXXXXX......",
                "....XXXXXXXXXXXXXXXXXXXXXX......",
                "....XXXXXXXXXXXXXXXXXXXXXX......",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
                "................................",
            };

            padlockSprite = FromBitmap(rows, "CrosshairPadlock");
            return padlockSprite;
        }

        /// <summary>
        /// Turns an ASCII bitmap into a sprite. Bitmap rows read top-down and
        /// texture rows read bottom-up, so the rows are indexed in reverse.
        /// </summary>
        private static Sprite FromBitmap(string[] rows, string name)
        {
            int size = rows.Length;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                string row = rows[size - 1 - y];
                for (int x = 0; x < size; x++)
                {
                    bool on = x < row.Length && row[x] == 'X';
                    pixels[(y * size) + x] = on ? Color.white : Color.clear;
                }
            }

            return BuildSprite(pixels, size, name);
        }

        private static Sprite BuildSprite(Color[] pixels, int size, string name)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            texture.SetPixels(pixels);
            texture.Apply();

            return Sprite.Create(
                texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
