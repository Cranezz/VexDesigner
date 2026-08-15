namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Makes an object glow when the user is aiming at it.
    ///
    /// Uses emission rather than tinting the base colour, which matters more
    /// than it sounds: the parts tray is near-black, and a colour tint is a
    /// *multiply* against the surface, so tinting a black object blue leaves it
    /// black. Emission is additive and shows up on any surface regardless of
    /// how dark it is.
    ///
    /// Driven through a MaterialPropertyBlock so no material asset is modified.
    /// Writing to renderer.material instead would silently clone the material
    /// per object, which quietly multiplies draw calls and leaks the clones.
    ///
    /// Requires the renderer's material to have the _EMISSION keyword enabled.
    /// The workshop builder does this when creating bin materials; a material
    /// without it will simply not glow, rather than error.
    /// </summary>
    public sealed class Highlightable : MonoBehaviour
    {
        [Tooltip("Hover colour. White, so hovering *lightens* the part rather " +
                 "than tinting it - the point is to say which part is under the " +
                 "crosshair, not to recolour it.")]
        [SerializeField] private Color highlightColour = Color.white;

        [Tooltip("Emission strength. Kept low: a hover should read as the part " +
                 "being a shade brighter, not as it lighting up.")]
        [SerializeField] private float intensity = 0.45f;

        [Tooltip("Seconds to fade in and out. Instant switching reads as a " +
                 "flicker when the cursor crosses several objects quickly.")]
        [SerializeField] private float fadeTime = 0.07f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("Border colour for a part pinned in mid-air. Blue, and drawn " +
                 "as an outline rather than a glow, so it cannot be mistaken " +
                 "for the white wash of a hover.")]
        [SerializeField] private Color pinnedColour = new Color(0.15f, 0.5f, 1f);

        [Tooltip("Border width in pixels, held constant on screen.")]
        [SerializeField] private float outlineThickness = 3f;

        private PartOutline outline;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float current;
        private float target;

        [Tooltip("Colour shown while a part is held. White so it is instantly " +
                 "distinguishable from the blue of hover and of frozen.")]
        [SerializeField] private Color grabbedColour = new Color(1f, 1f, 1f);

        /// <summary>
        /// Persistent glow, independent of hover. Used to mark a frozen part,
        /// which has to stay visibly marked when the cursor moves away.
        /// </summary>
        private bool pinned;

        /// <summary>Set while the part is in hand.</summary>
        private bool grabbed;

        /// <summary>
        /// Scales the hover glow down without switching it off.
        ///
        /// Used when a specific hole is being aimed at: the hole lights up
        /// fully and its part drops to a faint wash, which says "this hole, on
        /// this part" in one glance. A part at full brightness would compete
        /// with the hole and make it ambiguous which is being selected.
        /// </summary>
        public float HoverScale
        {
            get => hoverScale;
            set
            {
                if (Mathf.Approximately(hoverScale, value))
                {
                    return;
                }

                hoverScale = value;

                // Repaint immediately. Update only repaints while fading, so a
                // change made once the glow has settled would otherwise not
                // show until the next time the cursor moved on or off.
                if (renderers != null)
                {
                    Apply();
                }
            }
        }

        private float hoverScale = 1f;

        /// <summary>
        /// When false, the object refuses to highlight. Used to signal "you
        /// cannot interact with this right now" - for instance while the user
        /// is already carrying a part.
        /// </summary>
        public bool Interactable { get; set; } = true;

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
        }

        public void SetHighlighted(bool on)
        {
            target = (on && Interactable) ? 1f : 0f;
        }

        /// <summary>
        /// Marks the object as pinned in mid-air. Persists regardless of hover,
        /// because the user needs to see at a glance which parts are anchored
        /// without having to sweep the cursor over them.
        /// </summary>
        public void SetPinned(bool value)
        {
            if (pinned == value)
            {
                return;
            }

            pinned = value;
            ApplyOutline();
            Apply();
        }

        private void Update()
        {
            if (Mathf.Approximately(current, target))
            {
                return;
            }

            current = fadeTime <= 0f
                ? target
                : Mathf.MoveTowards(current, target, Time.deltaTime / fadeTime);

            Apply();
        }

        /// <summary>
        /// Marks the object as held. Distinct from both hover and pinned,
        /// because a part can be all three at once and the user needs to be
        /// able to tell which.
        /// </summary>
        public void SetGrabbed(bool value)
        {
            if (grabbed == value)
            {
                return;
            }

            grabbed = value;
            Apply();
        }

        private void Apply()
        {
            // The three states compose by taking the brightest channel rather
            // than replacing each other, so a held frozen part reads as both
            // rather than the newer state hiding the older one.
            // Hover and grab still compose by taking the brightest channel, so
            // a held part that is also under the crosshair reads as both.
            //
            // Frozen is deliberately not in this mix any more. It was a blue
            // glow competing with a blue-white one, and on a pale aluminium
            // part the difference between "pinned" and "hovered" came down to
            // a shade. A border is a different *kind* of mark, so the two can
            // be told apart at a glance and can be shown at once.
            Color emission = highlightColour * (intensity * current * HoverScale);

            if (grabbed)
            {
                emission = Brightest(emission, grabbedColour * (intensity * 0.9f));
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(block);
                block.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>Puts the frozen border up or takes it down.</summary>
        private void ApplyOutline()
        {
            if (outline == null)
            {
                outline = GetComponent<PartOutline>() ?? gameObject.AddComponent<PartOutline>();
            }

            if (pinned)
            {
                outline.Show(pinnedColour, outlineThickness);
            }
            else
            {
                outline.Hide();
            }
        }

        private static Color Brightest(Color a, Color b)
        {
            return new Color(
                Mathf.Max(a.r, b.r), Mathf.Max(a.g, b.g), Mathf.Max(a.b, b.b));
        }

        private void OnDisable()
        {
            // Leaving an object glowing after it is switched off is a
            // surprisingly common source of "stuck highlight" bugs.
            current = 0f;
            target = 0f;

            if (renderers != null)
            {
                Apply();
            }
        }
    }
}
