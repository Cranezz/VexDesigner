namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// The things on the saw you can take hold of: the stock itself, the ball
    /// that turns it, and the blade.
    ///
    /// Everything is grabbed where it is rather than through a knob standing in
    /// for it. Five knobs on a fence meant learning which knob was which before
    /// anything could be adjusted, and "turn X" is not a thing anybody wants to
    /// do - they want to slide the stock along, spin it over, or swing the
    /// blade. Those are all visible objects, so they are all draggable objects.
    ///
    /// Snapping is the same everywhere and follows the modifier, not the
    /// control: nothing held is free movement, shift is the coarse step, and
    /// control is the fine one.
    /// </summary>
    public sealed class SawHandles : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>What is currently being dragged.</summary>
        public enum Grip
        {
            None,
            Slide,
            Blade,
            RotateX,
            RotateY,
            RotateZ,
        }

        [Header("Steps")]
        [Tooltip("Distance step with the snap modifier held, in inches.")]
        [SerializeField] private float slideCoarseInches = 0.125f;

        [Tooltip("Distance step with the precision modifier held, in inches.")]
        [SerializeField] private float slideFineInches = 0.0625f;

        [Tooltip("Angle step with the snap modifier held, in degrees.")]
        [SerializeField] private float angleCoarseDegrees = 15f;

        [Tooltip("Angle step with the precision modifier held, in degrees.")]
        [SerializeField] private float angleFineDegrees = 1f;

        [Header("Look")]
        [SerializeField] private float ballRadiusInches = 2.4f;

        private SawStation saw;

        private Transform ball;
        private Transform ringX;
        private Transform ringY;
        private Transform ringZ;
        private Transform slideLeft;
        private Transform slideRight;

        private Material ringXMaterial;
        private Material ringYMaterial;
        private Material ringZMaterial;
        private Material slideMaterial;

        private Grip held = Grip.None;
        private float gripReference;
        private float gripStart;
        private float gripTurned;

        public Grip Held => held;

        /// <summary>What the pointer is over, for the interface to light up.</summary>
        public Grip Hovered { get; private set; }

        private void Awake()
        {
            saw = GetComponentInParent<SawStation>();
            Build();
        }

        private void LateUpdate()
        {
            bool show = saw != null && saw.HasPart;

            if (ball != null && ball.gameObject.activeSelf != show)
            {
                ball.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // The ball rides on the middle of the stock, so it is always on the
            // thing it turns.
            ball.position = saw.StockCentre;
            ball.rotation = saw.transform.rotation;

            float radius = ballRadiusInches * InchesToMetres;
            ball.localScale = Vector3.one * radius;

            if (saw.StockEnds(out Vector3 left, out Vector3 right))
            {
                float lift = 0.4f * InchesToMetres;

                slideLeft.position = left - (saw.transform.right * (1.4f * InchesToMetres)) +
                    (saw.transform.up * lift);

                slideRight.position = right + (saw.transform.right * (1.4f * InchesToMetres)) +
                    (saw.transform.up * lift);

                slideLeft.rotation = Quaternion.LookRotation(-saw.transform.right, Vector3.up);
                slideRight.rotation = Quaternion.LookRotation(saw.transform.right, Vector3.up);
            }

            Tint();
        }

        // ------------------------------------------------------------------
        // Dragging
        // ------------------------------------------------------------------

        /// <summary>
        /// Works out what the pointer is over. Called every frame while the saw
        /// is open and nothing is being dragged.
        /// </summary>
        public Grip Probe(Ray ray)
        {
            Hovered = Grip.None;

            if (saw == null || !saw.HasPart)
            {
                return Grip.None;
            }

            // The blade first: it is a thin thing near the stock, and losing a
            // click for it to the much larger stock behind would be worse than
            // the reverse.
            if (NearSegment(ray, saw.BladePoint, BladeTip(), 1.1f * InchesToMetres))
            {
                Hovered = Grip.Blade;
                return Hovered;
            }

            Grip ring = NearestRing(ray);

            if (ring != Grip.None)
            {
                Hovered = ring;
                return Hovered;
            }

            if (saw.StockEnds(out Vector3 left, out Vector3 right) &&
                NearSegment(ray, left, right, 1.6f * InchesToMetres))
            {
                Hovered = Grip.Slide;
            }

            return Hovered;
        }

        /// <summary>Takes hold of whatever the pointer is over.</summary>
        public void Begin(Ray ray)
        {
            held = Probe(ray);
            gripTurned = 0f;

            switch (held)
            {
                case Grip.Slide:
                    gripReference = AlongFence(ray);
                    gripStart = saw.FeedInches;
                    break;

                case Grip.Blade:
                    gripReference = AroundBlade(ray);
                    gripStart = saw.BladeAngle;
                    break;

                case Grip.RotateX:
                case Grip.RotateY:
                case Grip.RotateZ:
                    gripReference = AroundBall(ray, Axis(held));
                    gripStart = saw.Rotation[AxisIndex(held)];
                    break;
            }
        }

        public void Release()
        {
            held = Grip.None;
        }

        /// <summary>
        /// Applies a drag. <paramref name="snap"/> and
        /// <paramref name="fine"/> are the modifier states.
        /// </summary>
        public void Drag(Ray ray, bool snap, bool fine)
        {
            if (saw == null || held == Grip.None)
            {
                return;
            }

            if (held == Grip.Slide)
            {
                float now = AlongFence(ray);
                float moved = (now - gripReference) / InchesToMetres;

                // Dragging the stock right feeds more of it past the blade.
                float step = fine ? slideFineInches : (snap ? slideCoarseInches : 0f);

                saw.SetFeed(Round(gripStart + moved, step));
                return;
            }

            float angleStep = fine ? angleFineDegrees : (snap ? angleCoarseDegrees : 0f);

            if (held == Grip.Blade)
            {
                float now = AroundBlade(ray);

                // Unwrapped, so the head can be swung right round without the
                // reading folding back on itself.
                gripTurned += Mathf.DeltaAngle(gripReference + gripTurned, now);

                saw.SetBladeAngle(Round(gripStart + gripTurned, angleStep));
                return;
            }

            Vector3 axis = Axis(held);
            float reading = AroundBall(ray, axis);

            gripTurned += Mathf.DeltaAngle(gripReference + gripTurned, reading);

            saw.SetRotation(AxisIndex(held), Round(gripStart + gripTurned, angleStep));
        }

        private static float Round(float value, float step)
        {
            return step > 0f ? Mathf.Round(value / step) * step : value;
        }

        // ------------------------------------------------------------------
        // Reading the pointer
        // ------------------------------------------------------------------

        /// <summary>How far along the fence the pointer is, in metres.</summary>
        private float AlongFence(Ray ray)
        {
            Vector3 along = saw.transform.right;
            Vector3 origin = saw.StockCentre;

            if (!PlanePoint(ray, origin, saw.transform.up, out Vector3 point))
            {
                return 0f;
            }

            return Vector3.Dot(point - origin, along);
        }

        /// <summary>Angle of the pointer about the blade's pivot.</summary>
        private float AroundBlade(Ray ray)
        {
            Vector3 pivot = saw.BladePoint;

            if (!PlanePoint(ray, pivot, saw.transform.up, out Vector3 point))
            {
                return 0f;
            }

            Vector3 radial = Vector3.ProjectOnPlane(point - pivot, saw.transform.up);

            if (radial.sqrMagnitude < 1e-10f)
            {
                return 0f;
            }

            return Vector3.SignedAngle(-saw.transform.forward, radial, saw.transform.up);
        }

        /// <summary>Angle of the pointer about one ring of the ball.</summary>
        private float AroundBall(Ray ray, Vector3 axis)
        {
            Vector3 centre = saw.StockCentre;

            if (!PlanePoint(ray, centre, axis, out Vector3 point))
            {
                return 0f;
            }

            Vector3 radial = Vector3.ProjectOnPlane(point - centre, axis);

            if (radial.sqrMagnitude < 1e-10f)
            {
                return 0f;
            }

            Vector3 reference = Vector3.ProjectOnPlane(saw.transform.right, axis);

            if (reference.sqrMagnitude < 1e-10f)
            {
                reference = Vector3.ProjectOnPlane(saw.transform.forward, axis);
            }

            return Vector3.SignedAngle(reference.normalized, radial, axis);
        }

        private static bool PlanePoint(Ray ray, Vector3 origin, Vector3 normal, out Vector3 point)
        {
            point = Vector3.zero;

            float facing = Vector3.Dot(ray.direction, normal);

            if (Mathf.Abs(facing) < 0.02f)
            {
                return false;
            }

            float distance = Vector3.Dot(origin - ray.origin, normal) / facing;

            if (distance <= 0f)
            {
                return false;
            }

            point = ray.origin + (ray.direction * distance);
            return true;
        }

        private Vector3 Axis(Grip grip)
        {
            return grip switch
            {
                Grip.RotateX => saw.transform.right,
                Grip.RotateY => saw.transform.up,
                _ => saw.transform.forward,
            };
        }

        private static int AxisIndex(Grip grip)
        {
            return grip switch
            {
                Grip.RotateX => 0,
                Grip.RotateY => 1,
                _ => 2,
            };
        }

        private Vector3 BladeTip()
        {
            Vector3 direction =
                Quaternion.AngleAxis(saw.BladeAngle, saw.transform.up) * -saw.transform.forward;

            return saw.BladePoint + (direction * (7f * InchesToMetres));
        }

        /// <summary>Which rotation ring the pointer is nearest, if any.</summary>
        private Grip NearestRing(Ray ray)
        {
            float radius = ballRadiusInches * InchesToMetres;
            float tolerance = 0.55f * InchesToMetres;

            Grip best = Grip.None;
            float nearest = tolerance;

            var candidates = new[] { Grip.RotateX, Grip.RotateY, Grip.RotateZ };

            foreach (Grip grip in candidates)
            {
                Vector3 axis = Axis(grip);

                if (!PlanePoint(ray, saw.StockCentre, axis, out Vector3 point))
                {
                    continue;
                }

                float offset = Mathf.Abs(
                    Vector3.ProjectOnPlane(point - saw.StockCentre, axis).magnitude - radius);

                if (offset < nearest)
                {
                    nearest = offset;
                    best = grip;
                }
            }

            return best;
        }

        /// <summary>True when the ray passes within a tolerance of a segment.</summary>
        private static bool NearSegment(Ray ray, Vector3 a, Vector3 b, float tolerance)
        {
            const int samples = 16;
            float nearest = float.MaxValue;

            for (int i = 0; i <= samples; i++)
            {
                Vector3 point = Vector3.Lerp(a, b, i / (float)samples);
                Vector3 offset = point - ray.origin;

                float depth = Vector3.Dot(offset, ray.direction);

                if (depth <= 0f)
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, (offset - (ray.direction * depth)).magnitude);
            }

            return nearest <= tolerance;
        }

        // ------------------------------------------------------------------
        // Appearance
        // ------------------------------------------------------------------

        private void Build()
        {
            var root = new GameObject("Handles");
            root.transform.SetParent(transform, false);
            ball = root.transform;

            Shader shader = Shader.Find("VexDesigner/GizmoOverlay")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

            ringXMaterial = new Material(shader) { name = "SawRingX" };
            ringYMaterial = new Material(shader) { name = "SawRingY" };
            ringZMaterial = new Material(shader) { name = "SawRingZ" };
            slideMaterial = new Material(shader) { name = "SawSlide" };

            ringX = Ring(root.transform, "RingX", Vector3.right, ringXMaterial);
            ringY = Ring(root.transform, "RingY", Vector3.up, ringYMaterial);
            ringZ = Ring(root.transform, "RingZ", Vector3.forward, ringZMaterial);

            slideLeft = Arrow("SlideLeft");
            slideRight = Arrow("SlideRight");
        }

        private Transform Ring(Transform parent, string name, Vector3 axis, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);

            go.AddComponent<MeshFilter>().sharedMesh = GizmoMeshes.Torus();

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go.transform;
        }

        private Transform Arrow(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);

            var cone = new GameObject("Head");
            cone.transform.SetParent(go.transform, false);
            cone.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
            cone.transform.localScale = new Vector3(
                0.5f * InchesToMetres, 0.9f * InchesToMetres, 0.5f * InchesToMetres);

            cone.AddComponent<MeshFilter>().sharedMesh = GizmoMeshes.Cone();

            var renderer = cone.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = slideMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go.transform;
        }

        /// <summary>
        /// Lights whatever is being pointed at or held.
        ///
        /// Brightening in place rather than adding an outline: the handles are
        /// already single-coloured shapes, so a colour is the whole of what
        /// they can say, and it says it without a second copy of the geometry.
        /// </summary>
        private void Tint()
        {
            Paint(ringXMaterial, new Color(0.95f, 0.25f, 0.25f), Grip.RotateX);
            Paint(ringYMaterial, new Color(0.35f, 0.9f, 0.35f), Grip.RotateY);
            Paint(ringZMaterial, new Color(0.3f, 0.5f, 1f), Grip.RotateZ);
            Paint(slideMaterial, new Color(1f, 0.55f, 0.1f), Grip.Slide);
        }

        private void Paint(Material material, Color colour, Grip grip)
        {
            bool lit = held == grip || (held == Grip.None && Hovered == grip);

            material.SetColor(Shader.PropertyToID("_BaseColor"),
                lit ? Color.Lerp(colour, Color.white, 0.6f) : colour);
        }
    }
}
