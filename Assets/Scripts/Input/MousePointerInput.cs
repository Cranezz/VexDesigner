namespace VexDesigner.InputSources
{
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.InputSystem;

    /// <summary>
    /// Desktop implementation of <see cref="IPointerInput"/>: turns the mouse
    /// cursor into a world-space aim ray.
    ///
    /// The camera reference is resolved lazily rather than cached in Awake,
    /// because the scene's camera is created by a builder and component order
    /// is not guaranteed.
    /// </summary>
    public sealed class MousePointerInput : MonoBehaviour, IPointerInput
    {
        [SerializeField] private Camera aimCamera;

        public Ray AimRay { get; private set; }
        public bool PrimaryPressedThisFrame { get; private set; }
        public bool RepeatModifierHeld { get; private set; }
        public bool IsOverInterface { get; private set; }
        public bool SecondaryHeld { get; private set; }
        public Vector2 DragDelta { get; private set; }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            Camera cam = ResolveCamera();

            if (mouse == null || cam == null)
            {
                PrimaryPressedThisFrame = false;
                RepeatModifierHeld = false;
                IsOverInterface = false;
                SecondaryHeld = false;
                DragDelta = Vector2.zero;
                return;
            }

            Vector2 screen = mouse.position.ReadValue();
            AimRay = cam.ScreenPointToRay(screen);

            PrimaryPressedThisFrame = mouse.leftButton.wasPressedThisFrame;
            SecondaryHeld = mouse.rightButton.isPressed;
            DragDelta = mouse.delta.ReadValue();

            Keyboard keyboard = Keyboard.current;
            RepeatModifierHeld = keyboard != null &&
                (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);

            // EventSystem.current is null until a UI canvas exists, which is
            // the normal state in phase 1.
            IsOverInterface = EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();
        }

        private Camera ResolveCamera()
        {
            if (aimCamera == null)
            {
                aimCamera = GetComponentInChildren<Camera>();
            }

            if (aimCamera == null)
            {
                aimCamera = Camera.main;
            }

            return aimCamera;
        }
    }
}
