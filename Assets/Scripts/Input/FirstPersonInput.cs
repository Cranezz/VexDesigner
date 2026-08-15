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
        public bool PrimaryHeld { get; private set; }
        public bool SecondaryHeld { get; private set; }
        public bool SecondaryPressedThisFrame { get; private set; }
        public bool RepeatModifierHeld { get; private set; }
        public bool IsOverInterface { get; private set; }
        public Vector2 DragDelta { get; private set; }

        /// <summary>Ray through the free pointer; the aim ray while it is hidden.</summary>
        public Ray PointerRay { get; private set; }

        /// <summary>Free pointer position in screen pixels.</summary>
        public Vector2 PointerScreenPosition { get; private set; }

        /// <summary>True while the free pointer is being shown.</summary>
        public bool PointerVisible { get; private set; }

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

        /// <summary>Held for fine control of rotation and carry distance.</summary>
        public bool PrecisionHeld { get; private set; }

        /// <summary>Held to snap movement and rotation to fixed increments.</summary>
        public bool SnapHeld { get; private set; }

        /// <summary>Held to target the far side of the hole being aimed at.</summary>
        public bool FarSideHeld { get; private set; }

        /// <summary>Toggles the rotation ring while a hole is snapped.</summary>
        public bool RotateModifierPressed { get; private set; }

        /// <summary>Use the machine being looked at.</summary>
        public bool UsePressed { get; private set; }

        /// <summary>Back out of whatever is open.</summary>
        public bool CancelPressed { get; private set; }

        private VexDesigner.Parts.InteractionLock interactionLock;

        private void Awake()
        {
            // Optional. Without it the camera simply always owns the look
            // gesture, which is the correct fallback.
            interactionLock = GetComponentInParent<VexDesigner.Parts.InteractionLock>();
        }

        private void OnEnable() => SetCursorLocked(true);

        private void OnDisable() => SetCursorLocked(false);

        /// <summary>Degrees of look per pixel of mouse movement.</summary>
        public void SetLookSensitivity(float value)
        {
            degreesPerPixel = Mathf.Clamp(value, 0.01f, 1f);
        }

        public void SetCursorLocked(bool locked)
        {
            CursorLocked = locked;
            ApplyCursorState();
        }

        /// <summary>
        /// Frees or captures the real mouse.
        ///
        /// Two different things were being conflated. <see cref="CursorLocked"/>
        /// means "the game has the input" and gates every click and keypress;
        /// whether the operating system's pointer is captured is a separate
        /// question, and during a dial drag the answer is different for each -
        /// the game still wants the clicks, and the user wants to see where
        /// they are pointing.
        ///
        /// Drawing a pointer in the HUD was the first attempt at that, and it
        /// was the wrong answer: a second cursor that does not match the one
        /// the operating system draws is worse than no cursor, and it was the
        /// wrong size besides.
        /// </summary>
        private void ApplyCursorState()
        {
            bool free = PointerVisible || !CursorLocked;

            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
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
            // Scroll pushes a carried part away or draws it closer, reported as
            // a signed notch count.
            //
            // The raw value is either +/-120 or +/-1 depending on platform and
            // Input System version. Dividing unconditionally by 120 turned a
            // notch into 0.008 on a machine using the second convention, which
            // is why scrolling appeared to do nothing at all. Normalising by
            // magnitude handles both.
            if (mouse != null && CursorLocked)
            {
                float raw = mouse.scroll.ReadValue().y;
                ZoomDelta = Mathf.Abs(raw) > 1.5f ? raw / 120f : raw;
            }
            else
            {
                ZoomDelta = 0f;
            }

            if (keyboard == null || !CursorLocked)
            {
                FreezePressed = false;
                CrouchPressed = false;
                ModeTogglePressed = false;
                RelativeTogglePressed = false;
                RotateModifierHeld = false;
                PrecisionHeld = false;
                SnapHeld = false;
                FarSideHeld = false;
                RotateModifierPressed = false;
                UsePressed = false;
                CancelPressed = false;
                return;
            }

            FreezePressed = keyboard.kKey.wasPressedThisFrame;
            CrouchPressed = keyboard.cKey.wasPressedThisFrame;
            ModeTogglePressed = keyboard.gKey.wasPressedThisFrame;
            RelativeTogglePressed = keyboard.yKey.wasPressedThisFrame;
            RotateModifierHeld = keyboard.rKey.isPressed;
            RotateModifierPressed = keyboard.rKey.wasPressedThisFrame;
            PrecisionHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            // Shares Shift with sprinting. They never overlap in practice -
            // nobody sprints while dragging a gizmo handle - and a single
            // obvious modifier beats two arbitrary ones.
            SnapHeld = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            // Space for the far side. Held rather than toggled: it is wanted
            // for the one hole being looked at, not as a mode. Space is free
            // because there is no jumping - this is a workshop, and hopping
            // around a bench full of small parts serves nothing.
            FarSideHeld = keyboard.spaceKey.isPressed;

            UsePressed = keyboard.eKey.wasPressedThisFrame;
            CancelPressed = keyboard.escapeKey.wasPressedThisFrame;
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
                PrimaryHeld = false;
                SecondaryHeld = false;
                SecondaryPressedThisFrame = false;
                return;
            }

            // In first person the aim ray comes from the centre of the screen,
            // where the crosshair is - not from a cursor position.
            AimRay = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            MovePointer(cam, mouse);

            if (mouse != null)
            {
                PrimaryPressedThisFrame = mouse.leftButton.wasPressedThisFrame && CursorLocked;
                PrimaryHeld = mouse.leftButton.isPressed && CursorLocked;
                SecondaryHeld = mouse.rightButton.isPressed && CursorLocked;
                SecondaryPressedThisFrame = mouse.rightButton.wasPressedThisFrame && CursorLocked;
                DragDelta = mouse.delta.ReadValue();
            }

            Keyboard keyboard = Keyboard.current;
            RepeatModifierHeld = keyboard != null &&
                (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);

            // The interface only steals the pointer when the game has let go of
            // it. During a dial drag the cursor is free so the user can see it,
            // but the drag still owns the mouse - and without this exception,
            // dragging over a line of on-screen text would hand the gesture to
            // the HUD and abandon the rotation half-finished.
            IsOverInterface = !CursorLocked ||
                (!PointerVisible && EventSystem.current != null &&
                 EventSystem.current.IsPointerOverGameObject());
        }

        /// <summary>
        /// Moves the free pointer with the mouse, in screen pixels.
        ///
        /// Drawn by the game rather than handed back to the operating system.
        /// Releasing the real cursor would let it wander out of the window
        /// mid-gesture, and this project reads <see cref="CursorLocked"/> as
        /// "the game has the input" - unlocking it would gate off every click
        /// and keypress at exactly the moment they are wanted.
        /// </summary>
        private void MovePointer(Camera cam, Mouse mouse)
        {
            if (!PointerVisible || mouse == null)
            {
                PointerScreenPosition = new Vector2(Screen.width, Screen.height) * 0.5f;
                PointerRay = AimRay;
                return;
            }

            // Read straight off the real cursor. Accumulating mouse deltas into
            // a position of our own was how the HUD pointer worked, and it
            // drifted away from where the operating system thought the cursor
            // was the moment either one hit an edge.
            PointerScreenPosition = mouse.position.ReadValue();
            PointerRay = cam.ScreenPointToRay(PointerScreenPosition);
        }

        public void ShowPointer(bool visible)
        {
            if (PointerVisible == visible)
            {
                return;
            }

            PointerVisible = visible;
            ApplyCursorState();
        }

        public void PlacePointer(Vector2 screenPosition)
        {
            PointerScreenPosition = new Vector2(
                Mathf.Clamp(screenPosition.x, 0f, Screen.width),
                Mathf.Clamp(screenPosition.y, 0f, Screen.height));

            // Moves the real cursor, so the gesture starts under the user's
            // hand rather than jumping whatever it drives to wherever the
            // cursor happened to be left.
            Mouse.current?.WarpCursorPosition(PointerScreenPosition);

            Camera cam = ResolveCamera();
            if (cam != null)
            {
                PointerRay = cam.ScreenPointToRay(PointerScreenPosition);
            }
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
