namespace VexDesigner.UI
{
    using UnityEngine;
    using UnityEngine.UI;
    using VexDesigner.Parts;

    /// <summary>
    /// The aiming dot, and the free pointer that replaces it during a dial
    /// gesture.
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
        [SerializeField] private Image pointer;
        [SerializeField] private PartPlacementController placement;
        [SerializeField] private TransformToolController transformTool;

        [Tooltip("Dot colour when a click would do nothing in particular.")]
        [SerializeField] private Color idleColour = new Color(1f, 1f, 1f, 0.85f);

        [Tooltip("Dot colour when a click would take hold of something, or " +
                 "put down what is already held.")]
        [SerializeField] private Color grabColour = new Color(1f, 0.28f, 0.24f);

        private static Sprite dotSprite;
        private static Sprite padlockSprite;
        private static Sprite pointerSprite;

        private RectTransform pointerRect;
        private VexDesigner.InputSources.IPointerInput pointerInput;

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

            pointerInput = FindAnyObjectByType<VexDesigner.InputSources.FirstPersonInput>();

            if (pointer != null)
            {
                pointerRect = pointer.rectTransform;
            }
        }

        private void Update()
        {
            if (placement == null)
            {
                return;
            }

            UpdatePointer();

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
        /// Draws the free pointer where the input layer says it is.
        ///
        /// Drawn in the HUD rather than handed to the operating system, so it
        /// cannot wander out of the window in the middle of a gesture.
        /// </summary>
        private void UpdatePointer()
        {
            if (pointer == null || pointerInput == null)
            {
                return;
            }

            bool visible = pointerInput.PointerVisible;
            pointer.enabled = visible;

            if (!visible || pointerRect == null)
            {
                return;
            }

            Vector2 screen = pointerInput.PointerScreenPosition;
            var canvas = pointerRect.parent as RectTransform;

            if (canvas != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvas, screen, null, out Vector2 local))
            {
                pointerRect.anchoredPosition = local;
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

        /// <summary>
        /// The free pointer: an arrow, so it reads as a cursor rather than as
        /// a second crosshair. Its tip is the hot spot, and the sprite pivot is
        /// set to match when it is built into the HUD.
        /// </summary>
        public static Sprite GetPointerSprite()
        {
            if (pointerSprite != null)
            {
                return pointerSprite;
            }

            string[] rows =
            {
                "X...............",
                "XX..............",
                "XOX.............",
                "XOOX............",
                "XOOOX...........",
                "XOOOOX..........",
                "XOOOOOX.........",
                "XOOOOOOX........",
                "XOOOOOOOX.......",
                "XOOOOXXXXX......",
                "XOOXOX..........",
                "XOX.XOX.........",
                "XX..XOX.........",
                "X....XOX........",
                "......XX........",
                "................",
            };

            const int size = 16;
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                // Bitmap rows read top-down; texture rows read bottom-up.
                string row = rows[size - 1 - y];

                for (int x = 0; x < size; x++)
                {
                    char c = x < row.Length ? row[x] : '.';

                    // Black outline round a white body, so the pointer stays
                    // legible over pale aluminium and dark shadow alike.
                    pixels[(y * size) + x] =
                        c == 'O' ? Color.white :
                        c == 'X' ? new Color(0f, 0f, 0f, 0.85f) :
                        Color.clear;
                }
            }

            pointerSprite = BuildSprite(pixels, size, "CrosshairPointer");
            return pointerSprite;
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
