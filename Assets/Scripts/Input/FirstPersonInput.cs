namespace VexDesigner.InputSources
{
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.InputSystem;

    /// <summary>
    /// Desktop first-person input. Supplies both the look/move channel and the
    /// aim channel, because in first person they come from the same place:
    /// the head.
    ///
    /// This is the only file that knows a mouse and keyboard exist. A VR
    /// implementation of the same two interfaces takes the head pose and the
    /// controller ray instead, and nothing above changes - which is the whole
    /// point of routing everything through <see cref="ILookInput"/> and
    /// <see cref="IPointerInput"/>.
    /// </summary>
    public sealed class FirstPersonInput : MonoBehaviour, ILookInput, IPointerInput, IActionInput
    {
        [Header("Look")]
        [SerializeField] private float degreesPerPixel = 0.12f;
        [SerializeField] private bool invertY;

        [Header("Move")]
        [SerializeField] private float walkSpeed = 1.5f;
        [SerializeField] private float sprintMultiplier = 1.9f;

        [Header("Aim")]
        [SerializeField] private Camera aimCamera;

        public Vector2 LookDelta { get; private set; }
        public Vector2 MoveDelta { get; private set; }
        public float ZoomDelta { get; private set; }
        public Vector2 PanDelta => Vector2.zero;

        public Ray AimRay { get; private set; }
        public bool PrimaryPressedThisFrame { get; private set; }
        public bool SecondaryHeld { get; private set; }
        public bool RepeatModifierHeld { get; private set; }
        public bool IsOverInterface { get; private set; }
        public Vector2 DragDelta { get; private set; }

        /// <summary>
        /// True while the mouse is captured. Cleared when a menu opens so the
        /// cursor comes back.
        /// </summary>
        public bool CursorLocked { get; private set; }

        // --- Action channel -------------------------------------------------
        // Named by intent rather than by key, so a rebind or a VR controller
        // maps onto the same names without touching any consumer.

        /// <summary>Freeze or unfreeze the held part.</summary>
        public bool FreezePressed { get; private set; }

        /// <summary>Toggle crouch.</summary>
        public bool CrouchPressed { get; private set; }

        /// <summary>Switch between grab mode and the transform tool.</summary>
        public bool ModeTogglePressed { get; private set; }

        /// <summary>Switch between global and part-relative axes.</summary>
        public bool RelativeTogglePressed { get; private set; }

        /// <summary>Held to swap the move tool for the rotate tool.</summary>
        public bool RotateModifierHeld { get; private set; }

        private VexDesigner.Parts.InteractionLock interactionLock;

        private void Awake()
        {
            // Optional. Without it the camera simply always owns the look
            // gesture, which is the correct fallback.
            interactionLock = GetComponentInParent<VexDesigner.Parts.InteractionLock>();
        }

        private void OnEnable() => SetCursorLocked(true);

        private void OnDisable() => SetCursorLocked(false);

        public void SetCursorLocked(bool locked)
        {
            CursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;

            ReadLook(mouse);
            ReadMove(keyboard);
            ReadAim(mouse);
            ReadActions(keyboard, mouse);
        }

        private void ReadActions(Keyboard keyboard, Mouse mouse)
        {
            // Scroll pushes a carried part away or draws it closer. Reported
            // as a signed notch count; the consumer decides what a notch means.
            ZoomDelta = (mouse != null && CursorLocked)
                ? mouse.scroll.ReadValue().y / 120f
                : 0f;

            if (keyboard == null || !CursorLocked)
            {
                FreezePressed = false;
                CrouchPressed = false;
                ModeTogglePressed = false;
                RelativeTogglePressed = false;
                RotateModifierHeld = false;
                return;
            }

            FreezePressed = keyboard.kKey.wasPressedThisFrame;
            CrouchPressed = keyboard.cKey.wasPressedThisFrame;
            ModeTogglePressed = keyboard.gKey.wasPressedThisFrame;
            RelativeTogglePressed = keyboard.yKey.wasPressedThisFrame;
            RotateModifierHeld = keyboard.rKey.isPressed;
        }

        private void ReadLook(Mouse mouse)
        {
            // Look is suppressed while the cursor is free, or moving the mouse
            // to click a menu button would also spin the player round.
            //
            // It is also suppressed while another system has claimed the look
            // gesture - rotating a carried part. Without this the part and the
            // camera both turn at once, which is disorienting and makes it
            // impossible to aim the rotation.
            if (mouse == null || !CursorLocked ||
                (interactionLock != null && interactionLock.CameraOrbitLocked))
            {
                LookDelta = Vector2.zero;
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            float y = invertY ? delta.y : -delta.y;
            LookDelta = new Vector2(delta.x, y) * degreesPerPixel;
        }

        private void ReadMove(Keyboard keyboard)
        {
            if (keyboard == null || !CursorLocked)
            {
                MoveDelta = Vector2.zero;
                return;
            }

            var move = Vector2.zero;
            if (keyboard.wKey.isPressed) { move.y += 1f; }
            if (keyboard.sKey.isPressed) { move.y -= 1f; }
            if (keyboard.dKey.isPressed) { move.x += 1f; }
            if (keyboard.aKey.isPressed) { move.x -= 1f; }

            // Normalise so diagonals are not faster than straight lines.
            if (move.sqrMagnitude > 1f)
            {
                move.Normalize();
            }

            float speed = walkSpeed;
            if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
            {
                speed *= sprintMultiplier;
            }

            // Metres per second. The controller applies delta time, since it
            // owns the actual movement.
            MoveDelta = move * speed;
        }

        private void ReadAim(Mouse mouse)
        {
            Camera cam = ResolveCamera();
            if (cam == null)
            {
                PrimaryPressedThisFrame = false;
                SecondaryHeld = false;
                return;
            }

            // In first person the aim ray comes from the centre of the screen,
            // where the crosshair is - not from a cursor position.
            AimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            if (mouse != null)
            {
                PrimaryPressedThisFrame = mouse.leftButton.wasPressedThisFrame && CursorLocked;
                SecondaryHeld = mouse.rightButton.isPressed && CursorLocked;
                DragDelta = mouse.delta.ReadValue();
            }

            Keyboard keyboard = Keyboard.current;
            RepeatModifierHeld = keyboard != null &&
                (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);

            IsOverInterface = !CursorLocked ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        }

        private Camera ResolveCamera()
        {
            if (aimCamera == null)
            {
                aimCamera = GetComponentInChildren<Camera>() ?? Camera.main;
            }

            return aimCamera;
        }
    }
}
