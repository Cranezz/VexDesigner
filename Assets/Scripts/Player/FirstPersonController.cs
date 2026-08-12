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
        [Tooltip("Eye height in inches. 66 in is roughly average standing eye " +
                 "level and matters for VR comfort more than it looks.")]
        [SerializeField] private float eyeHeightIn = 66f;

        [Header("Look limits")]
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;

        [Header("Physics")]
        [SerializeField] private float gravity = -9.81f;

        [Tooltip("Downward force kept applied while grounded. Without it the " +
                 "controller intermittently reports not-grounded on flat floor " +
                 "and the player judders.")]
        [SerializeField] private float groundStick = -2f;

        [SerializeField] private Transform head;

        private CharacterController controller;
        private ILookInput input;
        private float pitch;
        private float verticalVelocity;

        /// <summary>Metres travelled horizontally this frame. Footsteps use it.</summary>
        public float DistanceTravelledThisFrame { get; private set; }

        public bool IsGrounded => controller != null && controller.isGrounded;

        public bool MovementEnabled { get; set; } = true;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            input = GetComponentInChildren<ILookInput>();

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
            }

            ApplyMovement();
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
