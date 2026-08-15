namespace VexDesigner.UI
{
    using UnityEngine;
    using UnityEngine.UI;
    using VexDesigner.Parts;

    /// <summary>
    /// The aiming dot, and the padlock badge under it.
    ///
    /// During a dial gesture there is no crosshair at all: the real mouse
    /// pointer is released and the operating system draws it, which is both
    /// what the user expects and the only cursor guaranteed to be the right
    /// size. An earlier version drew its own in the HUD, and a second cursor
    /// that does not match the real one is worse than none.
    ///
    /// The dot never changes shape, only colour. It used to swap to a hand
    /// glyph over anything grabbable, and a 34-pixel hand centred on the aim
    /// point covers the very thing being aimed at - which matters here more
    /// than in most games, because the targets are quarter-inch holes half an
    /// inch apart. Recolouring says the same thing and occludes nothing.
    ///
    /// Every sprite is generated in code. A crosshair is a handful of pixels,
    /// and generating it avoids an import step, keeps it crisp at any
    /// resolution setting, and means there is no art dependency for a purely
    /// functional element.
    /// </summary>
    public sealed class Crosshair : MonoBehaviour
    {
        [SerializeField] private Image dot;
        [SerializeField] private Image padlock;

        [Tooltip("Shown under the crosshair when a machine is within reach.")]
        [SerializeField] private TMPro.TextMeshProUGUI usePrompt;

        private VexDesigner.Parts.SawController saw;
        [SerializeField] private PartPlacementController placement;
        [SerializeField] private TransformToolController transformTool;

        [Tooltip("Dot colour when a click would do nothing in particular.")]
        [SerializeField] private Color idleColour = new Color(1f, 1f, 1f, 0.85f);

        [Tooltip("Dot colour when a click would take hold of something, or " +
                 "put down what is already held.")]
        [SerializeField] private Color grabColour = new Color(1f, 0.28f, 0.24f);

        private static Sprite dotSprite;
        private static Sprite padlockSprite;

        private void Awake()
        {
            if (placement == null)
            {
                placement = FindAnyObjectByType<PartPlacementController>();
            }

            if (transformTool == null)
            {
                transformTool = FindAnyObjectByType<TransformToolController>();
            }

        }

        private void Update()
        {
            if (placement == null)
            {
                return;
            }

            UpdateUsePrompt();

            // Hidden entirely while turning a gizmo ring or a hole dial: the
            // view is locked and the mouse is driving the rotation, so a
            // crosshair fixed to the middle of the screen points at nothing and
            // is only noise over the part.
            bool dialUp = (transformTool != null && transformTool.IsRotating) ||
                          placement.IsRotatingAboutHole;

            if (dialUp)
            {
                if (dot != null) { dot.enabled = false; }
                if (padlock != null) { padlock.enabled = false; }
                return;
            }

            // HasGrabTarget, not HasTarget: in transform mode, clicking a
            // placed part selects it rather than picking it up, and colouring
            // the dot there would promise something the click does not do.
            bool interactive = placement.HasGrabTarget || placement.IsCarrying;

            if (dot != null)
            {
                dot.enabled = true;
                dot.color = interactive ? grabColour : idleColour;
            }

            // The padlock is a badge under the dot rather than a replacement
            // for it. Swapping the crosshair out lost the aim point at exactly
            // the moment a pinned part was being lined up.
            if (padlock != null)
            {
                padlock.enabled = placement.IsCarrying && placement.CarriedIsFrozen;
            }
        }

        /// <summary>
        /// Offers the machine under the crosshair, by name of the key that
        /// takes it.
        ///
        /// Only when it is genuinely usable - near enough and looked at - so
        /// the prompt is a statement about right now rather than a label that
        /// lives on the screen.
        /// </summary>
        private void UpdateUsePrompt()
        {
            if (usePrompt == null)
            {
                return;
            }

            if (saw == null)
            {
                saw = FindAnyObjectByType<VexDesigner.Parts.SawController>();
            }

            bool offer = saw != null && !saw.IsOpen && saw.Available != null;

            if (usePrompt.enabled != offer)
            {
                usePrompt.enabled = offer;
            }

            if (offer)
            {
                usePrompt.text = saw.Available.HasPart
                    ? "<b>E</b>  Set up a cut"
                    : "<b>E</b>  Use the saw";
            }
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
