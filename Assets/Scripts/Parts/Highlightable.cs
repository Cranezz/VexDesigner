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
        [SerializeField] private Color highlightColour = new Color(0.18f, 0.52f, 1f);

        [Tooltip("Emission strength. Above 1 the glow blooms in HDR.")]
        [SerializeField] private float intensity = 1.6f;

        [Tooltip("Seconds to fade in and out. Instant switching reads as a " +
                 "flicker when the cursor crosses several objects quickly.")]
        [SerializeField] private float fadeTime = 0.07f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private float current;
        private float target;

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

        private void Apply()
        {
            Color emission = highlightColour * (intensity * current);

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
