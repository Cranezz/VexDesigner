namespace VexDesigner.CameraControl
{
    using UnityEngine;
    using VexDesigner.InputSources;

    /// <summary>
    /// Orbits the view around a fixed focus point on the workshop table.
    ///
    /// IMPORTANT — component placement. This goes on a *parent* object, with
    /// the Camera as a child sitting at local position zero. The rig never
    /// touches the camera's own transform.
    ///
    /// That separation is not decoration. In VR the headset drives the camera
    /// transform directly and anything else writing to it fights the tracking
    /// system, which is a well-known way to make people motion sick. Moving a
    /// parent instead is how every VR locomotion system works, and it means
    /// this object becomes the XR Origin later with no restructuring.
    ///
    /// Phase 1 deliberately has no walking: the viewer is planted at the table.
    /// </summary>
    public sealed class WorkshopCameraRig : MonoBehaviour
    {
        [Header("Focus")]
        [Tooltip("World point the view orbits around. Normally the build plate.")]
        [SerializeField] private Vector3 focusPoint = new Vector3(0f, 0.95f, 0f);

        [Tooltip("How far the pivot may be panned from its starting point.")]
        [SerializeField] private float maxPanFromOrigin = 0.75f;

        [Header("Orbit")]
        [SerializeField] private float yaw = 35f;
        [SerializeField] private float pitch = 28f;

        [Tooltip("Lowest pitch, in degrees. Kept above zero so the view cannot " +
                 "drop below the tabletop and look up through it.")]
        [SerializeField] private float minPitch = 6f;

        [SerializeField] private float maxPitch = 85f;

        [Header("Distance")]
        [SerializeField] private float distance = 1.6f;
        [SerializeField] private float minDistance = 0.35f;
        [SerializeField] private float maxDistance = 4.0f;

        [Header("Feel")]
        [Tooltip("Seconds to catch up to the target orientation. 0 = instant. " +
                 "A little smoothing reads as more precise, not less.")]
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

            // Scale zoom by current distance so it feels consistent: a fixed
            // step is uselessly slow when far out and violently fast up close.
            float zoom = input.ZoomDelta * distance;
            distance = Mathf.Clamp(distance - zoom, minDistance, maxDistance);

            Vector2 pan = input.PanDelta;
            if (pan != Vector2.zero)
            {
                // Pan relative to where the viewer is looking, scaled by
                // distance for the same reason as zoom above.
                Vector3 shift = (transform.right * pan.x + transform.up * pan.y) * distance;
                Vector3 panned = focusPoint + shift;

                // Tethered to the starting point: phase 1 is a fixed
                // workstation, and free panning would let the user lose the
                // table entirely with no way to recover.
                focusPoint = focusOrigin + Vector3.ClampMagnitude(
                    panned - focusOrigin, maxPanFromOrigin);
            }
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
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(Application.isPlaying ? focusPoint : focusPoint, 0.03f);
        }
    }
}
