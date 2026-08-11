namespace VexDesigner.InputSources
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// Desktop implementation of <see cref="ILookInput"/>.
    ///
    /// This is the only file in the camera system that knows a mouse exists.
    /// Controls:
    ///   right mouse held  - orbit
    ///   middle mouse held - pan
    ///   scroll wheel      - zoom
    ///
    /// Left mouse is deliberately left free: it will be needed for selecting
    /// and manipulating parts, and having it also drive the camera would make
    /// that ambiguous.
    /// </summary>
    public sealed class MouseLookInput : MonoBehaviour, ILookInput
    {
        [Header("Sensitivity")]
        [Tooltip("Degrees of orbit per pixel of mouse movement.")]
        [SerializeField] private float orbitDegreesPerPixel = 0.22f;

        [Tooltip("World units of pan per pixel of mouse movement.")]
        [SerializeField] private float panUnitsPerPixel = 0.0015f;

        [Tooltip("World units of zoom per scroll notch.")]
        [SerializeField] private float zoomUnitsPerNotch = 0.12f;

        [Header("Behaviour")]
        [Tooltip("Invert vertical orbit direction.")]
        [SerializeField] private bool invertY;

        public Vector2 LookDelta { get; private set; }
        public float ZoomDelta { get; private set; }
        public Vector2 PanDelta { get; private set; }

        private void Update()
        {
            // Mouse.current is null when no mouse is present, which is exactly
            // the situation in VR. Failing quietly here rather than throwing
            // means this component can simply be disabled later instead of
            // becoming a source of null-reference errors.
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                LookDelta = Vector2.zero;
                PanDelta = Vector2.zero;
                ZoomDelta = 0f;
                return;
            }

            Vector2 mouseDelta = mouse.delta.ReadValue();

            if (mouse.rightButton.isPressed)
            {
                float y = invertY ? mouseDelta.y : -mouseDelta.y;
                LookDelta = new Vector2(mouseDelta.x, y) * orbitDegreesPerPixel;
            }
            else
            {
                LookDelta = Vector2.zero;
            }

            PanDelta = mouse.middleButton.isPressed
                ? -mouseDelta * panUnitsPerPixel
                : Vector2.zero;

            // Scroll reports in units of 120 per notch on Windows.
            float scroll = mouse.scroll.ReadValue().y / 120f;
            ZoomDelta = scroll * zoomUnitsPerNotch;
        }
    }
}
