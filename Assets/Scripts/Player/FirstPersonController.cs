namespace VexDesigner.Player
{
    using UnityEngine;
    using VexDesigner.InputSources;

    /// <summary>
    /// Walks the player around the garage.
    ///
    /// The camera is a child at eye height and is only ever *pitched* here;
    /// yaw turns the body. That split is not cosmetic - in VR the headset
    /// owns the camera transform entirely, and a script also writing to it
    /// fights the tracking and makes people ill. Keeping yaw on the body and
    /// pitch on the camera means the VR version simply stops applying pitch.
    ///
    /// Uses CharacterController rather than a Rigidbody: it gives reliable
    /// step-over-small-obstacles and slope behaviour without the character
    /// being shovable by the parts they are carrying.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        [Header("Body")]
        [Tooltip("Eye height in inches.\n\n" +
                 "Set below real adult height (66 in) on purpose. VEX screws " +
                 "are a quarter inch across and nearly invisible from a " +
                 "standing adult's eyeline; a shorter viewpoint makes the whole " +
                 "workshop read larger and the parts correspondingly bigger.\n\n" +
                 "Note this trades against VR realism, where eye height should " +
                 "match the wearer. Expect to revisit it when VR lands.")]
        [SerializeField] private float eyeHeightIn = 50f;

        [Tooltip("Eye height while crouched, in inches.")]
        [SerializeField] private float crouchEyeHeightIn = 30f;

        [Tooltip("Seconds to move between standing and crouched.")]
        [SerializeField] private float crouchTransition = 0.14f;

        [Header("Look limits")]
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Physics")]
        [SerializeField] private float gravity = -9.81f;

        [Tooltip("Downward force kept applied while grounded. Without it the " +
                 "controller intermittently reports not-grounded on flat floor " +
                 "and the player judders.")]
        [SerializeField] private float groundStick = -2f;

        [Tooltip("Jump height in inches. Modest - this is a workshop, not a " +
                 "platformer, and a big jump makes a 9 ft ceiling feel low.")]
        [SerializeField] private float jumpHeightIn = 16f;

        [SerializeField] private Transform head;

        private CharacterController controller;
        private ILookInput input;
        private IActionInput actions;
        private float pitch;
        private float verticalVelocity;

        private bool crouched;
        private float currentEyeHeight;
        private float eyeHeightVelocity;

        public bool IsCrouched => crouched;

        /// <summary>Metres travelled horizontally this frame. Footsteps use it.</summary>
        public float DistanceTravelledThisFrame { get; private set; }

        public bool IsGrounded => controller != null && controller.isGrounded;

        public bool MovementEnabled { get; set; } = true;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponentInChildren<ILookInput>();
            actions = GetComponentInChildren<IActionInput>();
            currentEyeHeight = eyeHeightIn;

            if (input == null)
            {
                Debug.LogError(
                    $"{nameof(FirstPersonController)} found no {nameof(ILookInput)}. " +
                    "Add a FirstPersonInput component. The player cannot move.",
                    this);
            }

            if (head == null && Camera.main != null)
            {
                head = Camera.main.transform;
            }
        }

        private void Update()
        {
            if (input == null)
            {
                return;
            }

            if (MovementEnabled)
            {
                ApplyLook();

                if (actions != null && actions.CrouchPressed)
                {
                    crouched = !crouched;
                }
            }

            ApplyCrouch();
            ApplyMovement();
        }

        /// <summary>
        /// Eases the head between standing and crouched height.
        ///
        /// Only the camera moves; the collider is left at full height. That is
        /// deliberate for now - shrinking the collider lets the player crouch
        /// into geometry and then stand up inside it, which needs a headroom
        /// check to do properly. Crouching here is for getting eye-level with
        /// the bench, not for fitting through gaps.
        /// </summary>
        private void ApplyCrouch()
        {
            if (head == null)
            {
                return;
            }

            float targetHeight = crouched ? crouchEyeHeightIn : eyeHeightIn;

            currentEyeHeight = Mathf.SmoothDamp(
                currentEyeHeight, targetHeight, ref eyeHeightVelocity, crouchTransition);

            head.localPosition = new Vector3(0f, currentEyeHeight * InchesToMetres, 0f);
        }

        private void ApplyLook()
        {
            Vector2 look = input.LookDelta;

            // Yaw the body, pitch the head. See the class comment.
            transform.Rotate(Vector3.up, look.x, Space.World);

            pitch = Mathf.Clamp(pitch + look.y, minPitch, maxPitch);
            if (head != null)
            {
                head.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private void ApplyMovement()
        {
            Vector2 move = MovementEnabled ? input.MoveDelta : Vector2.zero;

            Vector3 horizontal =
                (transform.right * move.x) + (transform.forward * move.y);

            if (controller.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = groundStick;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;
            }

            if (MovementEnabled && controller.isGrounded &&
                actions != null && actions.JumpPressed)
            {
                // v = sqrt(2gh) gives exactly the requested height, so tuning
                // the jump is a matter of stating how high rather than guessing
                // at an impulse.
                float height = jumpHeightIn * InchesToMetres;
                verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * height);
            }

            Vector3 motion = (horizontal + (Vector3.up * verticalVelocity)) * Time.deltaTime;
            controller.Move(motion);

            // Measured from the actual displacement rather than the input, so
            // walking into a wall correctly counts as going nowhere.
            DistanceTravelledThisFrame =
                new Vector2(controller.velocity.x, controller.velocity.z).magnitude * Time.deltaTime;
        }

        /// <summary>
        /// Places the player at a spawn point. Called on load so every session
        /// starts at the garage door rather than wherever the scene was saved.
        /// </summary>
        public void Teleport(Vector3 position, float yaw)
        {
            // CharacterController overwrites transform changes while enabled,
            // so it has to be switched off across the move.
            bool wasEnabled = controller.enabled;
            controller.enabled = false;

            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            pitch = 0f;
            if (head != null)
            {
                head.localRotation = Quaternion.identity;
            }

            controller.enabled = wasEnabled;
        }

        private void OnValidate()
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null && head != null)
            {
                head.localPosition = new Vector3(0f, eyeHeightIn * InchesToMetres, 0f);
            }
        }
    }
}
