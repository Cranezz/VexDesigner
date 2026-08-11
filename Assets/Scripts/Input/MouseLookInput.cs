namespace VexDesigner.InputSources
{
    using UnityEngine;
    using UnityEngine.InputSystem;

    /// <summary>
    /// Desktop implementation of <see cref="ILookInput"/>.
    ///
    /// This is the only file in the camera system that knows a mouse or a
    /// keyboard exists. Controls:
    ///   right mouse held  - orbit
    ///   middle mouse held - pan
    ///   scroll wheel      - zoom
    ///   WASD              - move across the workspace
    ///
    /// Left mouse is deliberately left free: it selects and places parts, and
    /// having it also drive the camera would make that ambiguous.
    /// </summary>
    public sealed class MouseLookInput : MonoBehaviour, ILookInput
    {
        [Header("Sensitivity")]
        [Tooltip("Degrees of orbit per pixel of mouse movement.")]
        [SerializeField] private float orbitDegreesPerPixel = 0.22f;

        [Tooltip("Fraction of the viewing distance panned per pixel of movement.")]
        [SerializeField] private float panFractionPerPixel = 0.0016f;

        [Tooltip("Fraction of the viewing distance zoomed per scroll notch.")]
        [SerializeField] private float zoomFractionPerNotch = 0.16f;

        [Tooltip("Metres per second of travel from the movement keys.")]
        [SerializeField] private float moveSpeed = 0.9f;

        [Tooltip("Multiplier while the sprint key is held.")]
        [SerializeField] private float sprintMultiplier = 2.5f;

        [Header("Behaviour")]
        [Tooltip("Invert vertical orbit direction.")]
        [SerializeField] private bool invertY;

        public Vector2 LookDelta { get; private set; }
        public float ZoomDelta { get; private set; }
        public Vector2 PanDelta { get; private set; }
        public Vector2 MoveDelta { get; private set; }

        private void Update()
        {
            ReadMouse();
            ReadKeyboard();
        }

        private void ReadMouse()
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
                ? -mouseDelta * panFractionPerPixel
                : Vector2.zero;

            // Scroll reports in units of 120 per notch on Windows.
            float scroll = mouse.scroll.ReadValue().y / 120f;
            ZoomDelta = scroll * zoomFractionPerNotch;
        }

        private void ReadKeyboard()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                MoveDelta = Vector2.zero;
                return;
            }

            var move = Vector2.zero;
            if (keyboard.wKey.isPressed) { move.y += 1f; }
            if (keyboard.sKey.isPressed) { move.y -= 1f; }
            if (keyboard.dKey.isPressed) { move.x += 1f; }
            if (keyboard.aKey.isPressed) { move.x -= 1f; }

            // Normalise so diagonal travel is not faster than straight travel.
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            float speed = moveSpeed;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
            {
                speed *= sprintMultiplier;
            }

            MoveDelta = move * speed * Time.deltaTime;
        }
    }
}
