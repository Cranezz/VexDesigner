namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// One control on the saw: a knob that turns, or the slide that feeds the
    /// stock past the blade.
    ///
    /// Grabbed and turned by pointing at it, exactly as the hole dial and the
    /// gizmo rings are - the value follows where the pointer is rather than how
    /// far the mouse has moved. That consistency is worth more than it sounds:
    /// there are now four rotational controls in this project and a user who
    /// has learnt one has learnt all of them.
    /// </summary>
    public sealed class SawKnob : MonoBehaviour
    {
        public enum Control
        {
            /// <summary>Turns the stock about the saw's X axis.</summary>
            RotateX,

            /// <summary>Turns the stock about the saw's Y axis.</summary>
            RotateY,

            /// <summary>Turns the stock about the saw's Z axis.</summary>
            RotateZ,

            /// <summary>Feeds the stock along the fence, past the blade.</summary>
            Feed,

            /// <summary>Swings the blade.</summary>
            Blade,
        }

        [SerializeField] private Control kind;

        [Tooltip("Inches of feed per full turn of the knob. Only used by the " +
                 "feed control, where a turn has to mean a distance.")]
        [SerializeField] private float inchesPerTurn = 4f;

        [SerializeField] private Transform dial;

        [Tooltip("Floating readout beside the knob.")]
        [SerializeField] private TMPro.TextMeshPro readout;

        private SawStation station;

        public Control Kind => kind;

        /// <summary>
        /// Keeps the readout current and facing the camera.
        ///
        /// Beside the knob rather than only on the panel, because the knob is
        /// where the user is looking while they turn it - a number on the far
        /// side of the screen means glancing away from the thing being
        /// adjusted, which is exactly when a fine adjustment goes wrong.
        /// </summary>
        private void LateUpdate()
        {
            if (readout == null)
            {
                return;
            }

            if (station == null)
            {
                station = GetComponentInParent<SawStation>();
            }

            readout.text = Describe(station);

            Camera camera = Camera.main;

            if (camera != null)
            {
                // Billboarded, since the view now orbits the machine and a
                // fixed label would be edge-on from half of it.
                readout.transform.rotation = Quaternion.LookRotation(
                    readout.transform.position - camera.transform.position, Vector3.up);
            }
        }

        private string Describe(SawStation saw)
        {
            if (saw == null)
            {
                return string.Empty;
            }

            return kind switch
            {
                Control.Feed => $"FEED\n{saw.FeedInches:0.000} in",
                Control.Blade => $"BLADE\n{saw.BladeAngle:0.00}\u00b0",
                Control.RotateX => $"TURN X\n{saw.Rotation.x:0.##}\u00b0",
                Control.RotateY => $"TURN Y\n{saw.Rotation.y:0.##}\u00b0",
                _ => $"TURN Z\n{saw.Rotation.z:0.##}\u00b0",
            };
        }

        public void Configure(Control control)
        {
            kind = control;
        }

        /// <summary>
        /// Where the pointer is around this knob, in degrees.
        ///
        /// Read by crossing the pointer's ray with the knob's own face, so the
        /// knob follows the cursor round as drawn rather than around a circle
        /// that only agrees with it head-on.
        /// </summary>
        public float ReadAngle(Ray ray)
        {
            Vector3 axis = transform.up;
            Vector3 centre = transform.position;

            float facing = Vector3.Dot(ray.direction, axis);

            if (Mathf.Abs(facing) < 0.05f)
            {
                return 0f;
            }

            float distance = Vector3.Dot(centre - ray.origin, axis) / facing;

            if (distance <= 0f)
            {
                return 0f;
            }

            Vector3 radial = Vector3.ProjectOnPlane(
                ray.origin + (ray.direction * distance) - centre, axis);

            if (radial.sqrMagnitude < 1e-10f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(transform.forward, radial, axis);
        }

        /// <summary>What this control currently reads.</summary>
        public float Value(SawStation saw)
        {
            if (saw == null)
            {
                return 0f;
            }

            return kind switch
            {
                Control.RotateX => saw.Rotation.x,
                Control.RotateY => saw.Rotation.y,
                Control.RotateZ => saw.Rotation.z,
                Control.Feed => saw.FeedInches,
                Control.Blade => saw.BladeAngle,
                _ => 0f,
            };
        }

        /// <summary>
        /// Applies a turn of <paramref name="turnedDegrees"/> from where the
        /// knob was grabbed.
        /// </summary>
        /// <param name="coarse">Step with no modifier held.</param>
        /// <param name="fine">Step with the snap modifier held.</param>
        /// <param name="free">True to ignore both and move continuously.</param>
        public void Apply(
            SawStation saw, float start, float turnedDegrees,
            float coarse, float fine, bool free)
        {
            if (saw == null)
            {
                return;
            }

            if (kind == Control.Feed)
            {
                // A turn of the knob is a distance, so the stock can be fed a
                // couple of inches without dragging the cursor round the bed
                // several times.
                float inches = start + (turnedDegrees / 360f * inchesPerTurn);
                float step = free ? 0f : (fine > 0f ? fine : coarse);

                saw.SetFeed(Round(inches, step));
                Spin(turnedDegrees);
                return;
            }

            float degrees = start + turnedDegrees;
            float snap = free ? 0f : (fine > 0f ? fine : coarse);

            if (kind == Control.Blade)
            {
                saw.SetBladeAngle(Mathf.Clamp(Round(degrees, snap), 0f, 90f));
                Spin(turnedDegrees);
                return;
            }

            int axis = kind == Control.RotateX ? 0 : (kind == Control.RotateY ? 1 : 2);

            saw.SetRotation(axis, SawStation.Normalise(Round(degrees, snap)));
            Spin(turnedDegrees);
        }

        private static float Round(float value, float step)
        {
            return step > 0f ? Mathf.Round(value / step) * step : value;
        }

        /// <summary>Turns the knob's own face, so it looks like it is being used.</summary>
        private void Spin(float degrees)
        {
            if (dial != null)
            {
                dial.localRotation = Quaternion.AngleAxis(degrees, Vector3.up);
            }
        }
    }
}
