namespace VexDesigner.CameraControl
{
    using UnityEngine;
    using VexDesigner.InputSources;

    /// <summary>
    /// Orbits and moves the view around the workshop table.
    ///
    /// IMPORTANT - component placement. This goes on a *parent* object, with
    /// the Camera as a child sitting at local position zero. The rig never
    /// touches the camera's own transform.
    ///
    /// That separation is not decoration. In VR the headset drives the camera
    /// transform directly, and anything else writing to it fights the tracking
    /// system, which is a well-known way to make people motion sick. Moving a
    /// parent instead is how every VR locomotion system works, and it means
    /// this object becomes the XR Origin later with no restructuring.
    /// </summary>
    public sealed class WorkshopCameraRig : MonoBehaviour
    {
        [Header("Focus")]
        [Tooltip("World point the view orbits around.")]
        [SerializeField] private Vector3 focusPoint = new Vector3(0f, 0.9144f, 0f);

        [Tooltip("Half-extent of travel along X, in world units.")]
        [SerializeField] private float panLimitX = 0.95f;

        [Tooltip("Half-extent of travel along Z, in world units.")]
        [SerializeField] private float panLimitZ = 0.5f;

        [Tooltip("How far the focus may be raised or lowered from its start.")]
        [SerializeField] private float panLimitY = 0.4f;

        [Header("Orbit")]
        [SerializeField] private float yaw = 35f;
        [SerializeField] private float pitch = 28f;

        [Tooltip("Lowest pitch, in degrees. Kept above zero so the view cannot " +
                 "drop below the tabletop and look up through it.")]
        [SerializeField] private float minPitch = 4f;

        [SerializeField] private float maxPitch = 85f;

        [Header("Distance")]
        [SerializeField] private float distance = 1.32f;
        [SerializeField] private float minDistance = 0.15f;
        [SerializeField] private float maxDistance = 3.55f;

        [Header("Feel")]
        [Tooltip("Seconds to catch up to the target. 0 = instant. A little " +
                 "smoothing reads as more precise, not less.")]
        [SerializeField, Range(0f, 0.3f)] private float smoothTime = 0.06f;

        private ILookInput input;
        private Vector3 focusOrigin;

        // Smoothed state, plus the velocity accumulators SmoothDamp requires.
        private float smoothedYaw;
        private float smoothedPitch;
        private float smoothedDistance;
        private Vector3 smoothedFocus;
        private float yawVelocity;
        private float pitchVelocity;
        private float distanceVelocity;
        private Vector3 focusVelocity;

        private void Awake()
        {
            // GetComponentInChildren works with interfaces, so the concrete
            // input source stays swappable without this class naming it.
            input = GetComponentInChildren<ILookInput>();
            if (input == null)
            {
                Debug.LogError(
                    $"{nameof(WorkshopCameraRig)} found no {nameof(ILookInput)} " +
                    "on itself or its children. Add a MouseLookInput component. " +
                    "The camera will not respond until one is present.",
                    this);
            }

            focusOrigin = focusPoint;

            smoothedYaw = yaw;
            smoothedPitch = pitch;
            smoothedDistance = distance;
            smoothedFocus = focusPoint;

            ApplyTransform(smoothedYaw, smoothedPitch, smoothedDistance, smoothedFocus);
        }

        private void LateUpdate()
        {
            if (input != null)
            {
                ReadInput();
            }

            // LateUpdate so the view settles after everything else has moved
            // for the frame; doing this in Update produces a one-frame lag that
            // reads as jitter.
            smoothedYaw = Mathf.SmoothDamp(smoothedYaw, yaw, ref yawVelocity, smoothTime);
            smoothedPitch = Mathf.SmoothDamp(smoothedPitch, pitch, ref pitchVelocity, smoothTime);
            smoothedDistance = Mathf.SmoothDamp(smoothedDistance, distance, ref distanceVelocity, smoothTime);
            smoothedFocus = Vector3.SmoothDamp(smoothedFocus, focusPoint, ref focusVelocity, smoothTime);

            ApplyTransform(smoothedYaw, smoothedPitch, smoothedDistance, smoothedFocus);
        }

        private void ReadInput()
        {
            Vector2 look = input.LookDelta;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch + look.y, minPitch, maxPitch);

            // Zoom arrives as a fraction of current distance, so a notch moves
            // the same *proportion* whether you are close in or far out. A
            // fixed step is uselessly slow when far away and violently fast up
            // close.
            distance = Mathf.Clamp(
                distance * (1f - input.ZoomDelta), minDistance, maxDistance);

            Vector3 shift = Vector3.zero;

            // Drag-pan: screen-relative, so it tracks the cursor exactly.
            Vector2 pan = input.PanDelta;
            if (pan != Vector2.zero)
            {
                shift += (transform.right * pan.x + transform.up * pan.y) * distance;
            }

            // Key-move: travels across the workspace on the ground plane.
            // Uses the flattened heading rather than the camera's forward, so
            // looking down at the table does not shrink how far W travels.
            Vector2 move = input.MoveDelta;
            if (move != Vector2.zero)
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);

                // Looking straight down leaves no usable heading; fall back to
                // the rig's up vector, which points "north" on the table then.
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
                }

                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized * -1f;

                shift += (right * move.x) + (forward * move.y);
            }

            if (shift != Vector3.zero)
            {
                focusPoint = ClampToWorkspace(focusPoint + shift);
            }
        }

        /// <summary>
        /// Constrains the focus to a box around its starting point.
        ///
        /// Deliberately a box, not a sphere. The workspace is a rectangular
        /// table, and a spherical limit makes the reachable area shrink as you
        /// move diagonally - which feels like hitting an invisible wall well
        /// short of the table edge.
        /// </summary>
        private Vector3 ClampToWorkspace(Vector3 candidate)
        {
            Vector3 offset = candidate - focusOrigin;
            offset.x = Mathf.Clamp(offset.x, -panLimitX, panLimitX);
            offset.y = Mathf.Clamp(offset.y, -panLimitY, panLimitY);
            offset.z = Mathf.Clamp(offset.z, -panLimitZ, panLimitZ);
            return focusOrigin + offset;
        }

        private void ApplyTransform(float y, float p, float d, Vector3 focus)
        {
            Quaternion rotation = Quaternion.Euler(p, y, 0f);
            transform.SetPositionAndRotation(
                focus - (rotation * Vector3.forward * d),
                rotation);
        }

        /// <summary>
        /// Re-centres the view on a world point. Intended for "frame the
        /// selected part" behaviour later on.
        /// </summary>
        public void SetFocus(Vector3 worldPoint)
        {
            focusOrigin = worldPoint;
            focusPoint = worldPoint;
        }

        private void OnValidate()
        {
            // Keeps the inspector honest: nonsensical ranges silently produce
            // a camera that appears broken for no visible reason.
            minDistance = Mathf.Max(0.05f, minDistance);
            maxDistance = Mathf.Max(minDistance + 0.05f, maxDistance);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
            minPitch = Mathf.Clamp(minPitch, -89f, 89f);
            maxPitch = Mathf.Clamp(maxPitch, minPitch + 1f, 89f);
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 origin = Application.isPlaying ? focusOrigin : focusPoint;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(focusPoint, 0.03f);

            // Show the travel box so the limits are visible while tuning.
            Gizmos.color = new Color(0f, 1f, 1f, 0.25f);
            Gizmos.DrawWireCube(
                origin, new Vector3(panLimitX * 2f, panLimitY * 2f, panLimitZ * 2f));
        }
    }
}
