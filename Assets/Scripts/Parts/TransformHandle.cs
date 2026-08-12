namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// One draggable arm of the transform gizmo.
    ///
    /// Carries only its identity - which axis, and whether it moves or rotates.
    /// All the drag maths lives in <see cref="TransformToolController"/>, so
    /// the handle is pure data and the behaviour is in one readable place
    /// rather than spread across six near-identical components.
    /// </summary>
    public sealed class TransformHandle : MonoBehaviour
    {
        public enum Kind
        {
            Move,
            Rotate,

            /// <summary>
            /// Trackball: rotation about whatever axis the drag implies, rather
            /// than about one fixed axis. Faster for a rough orientation, where
            /// picking the right ring first is more work than the turn itself.
            /// </summary>
            Free,
        }

        [SerializeField] private Kind handleKind;

        /// <summary>Axis in gizmo space: one of the three unit vectors.</summary>
        [SerializeField] private Vector3 axis = Vector3.right;

        [SerializeField] private Color colour = Color.red;

        private Renderer handleRenderer;
        private MaterialPropertyBlock block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public Kind HandleKind => handleKind;

        /// <summary>Axis in world space, following the gizmo's orientation.</summary>
        public Vector3 WorldAxis => transform.parent != null
            ? transform.parent.TransformDirection(axis).normalized
            : axis;

        public void Configure(Kind kind, Vector3 handleAxis, Color handleColour)
        {
            handleKind = kind;
            axis = handleAxis;
            colour = handleColour;

            // AddComponent runs Awake immediately, before this call, so the
            // handle has already painted itself in the default colour. Repaint
            // now that it knows which axis it is - otherwise every arm comes
            // out red.
            SetHighlighted(false);
        }

        private void Awake()
        {
            handleRenderer = GetComponentInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            SetHighlighted(false);
        }

        /// <summary>
        /// Brightens the handle under the cursor. Without it there is no way to
        /// tell which axis a drag is about to affect until it has already moved
        /// something.
        /// </summary>
        public void SetHighlighted(bool on)
        {
            if (handleRenderer == null)
            {
                return;
            }

            // The overlay shader is unlit and exposes only a base colour, so
            // the hover state brightens toward white rather than adding
            // emission. Deliberately unlit: a handle that dims with the room
            // lighting reads as an object in the scene rather than a control.
            Color tint = on ? Color.Lerp(colour, Color.white, 0.6f) : colour;

            // Alpha is preserved rather than lerped. The free-rotation ball is
            // deliberately near-transparent, and brightening it toward opaque
            // white on hover would hide the part it is wrapped around.
            tint.a = on ? Mathf.Min(1f, colour.a * 2.2f) : colour.a;

            handleRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            handleRenderer.SetPropertyBlock(block);
        }
    }
}
