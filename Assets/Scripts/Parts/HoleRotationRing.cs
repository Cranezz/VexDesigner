namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// A dial drawn around a hole that is being mated, showing which way the
    /// part will be rolled about the join.
    ///
    /// Mating already leaves the two parts square to each other, which is right
    /// most of the time. This is for the times it is not - a bracket that has
    /// to sit at forty-five degrees, a bar angled across a frame. The ring
    /// gives that rotation a visible axis and a visible zero, so the part is
    /// turned about the join rather than nudged about in free space and hoped
    /// into place.
    ///
    /// The ring lies in the mating plane and its needle points at the current
    /// angle, so the amount turned can be read off the ticks instead of
    /// guessed at.
    /// </summary>
    public sealed class HoleRotationRing : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>
        /// Ring radius. Big enough to grab the eye next to a half-inch hole
        /// pattern, small enough not to swallow the parts being joined.
        /// </summary>
        private const float RadiusInches = 2.6f;

        /// <summary>
        /// The same radius in world units. Public because the pointer has to be
        /// placed on the dial, and it can only do that if it knows how big the
        /// dial is.
        /// </summary>
        public const float RadiusMetres = RadiusInches * InchesToMetres;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Transform needle;
        private Material ringMaterial;

        public static HoleRotationRing Create(Color colour)
        {
            var go = new GameObject("HoleRotationRing");
            var ring = go.AddComponent<HoleRotationRing>();
            ring.Build(colour);
            ring.Hide();
            return ring;
        }

        private void Build(Color colour)
        {
            float radius = RadiusInches * InchesToMetres;

            // Drawn over the parts. The whole point of the dial is to be read
            // while it sits against metal, and a depth-tested ring would be
            // half-buried in whatever it is measuring.
            Shader shader = Shader.Find("VexDesigner/GizmoOverlay")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

            ringMaterial = new Material(shader) { name = "HoleRotationRing" };
            ringMaterial.SetColor(BaseColorId, colour);

            // The torus is authored in the XZ plane with radius 1, so the
            // object's own up axis is the axis of rotation. Aiming that up axis
            // along the mating normal is all the orienting this needs.
            var ringObject = new GameObject("Ring");
            ringObject.transform.SetParent(transform, false);
            ringObject.transform.localScale = new Vector3(radius, radius, radius);

            ringObject.AddComponent<MeshFilter>().sharedMesh = GizmoMeshes.Torus();
            Dress(ringObject.AddComponent<MeshRenderer>(), ringMaterial);

            AddTicks(radius);
            AddNeedle(radius, colour);
        }

        /// <summary>
        /// Marks at fifteen degrees, matching the snap increment.
        ///
        /// The ticks are the snap made visible: holding shift lands the needle
        /// on them, so the increment can be seen before it is chosen rather
        /// than discovered by trying it.
        /// </summary>
        private void AddTicks(float radius)
        {
            const int ticks = 24;

            var material = new Material(ringMaterial) { name = "HoleRingTicks" };
            material.SetColor(BaseColorId, new Color(0.05f, 0.05f, 0.06f));

            for (int i = 0; i < ticks; i++)
            {
                float angle = (i / (float)ticks) * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

                bool quadrant = i % 6 == 0;
                float length = radius * (quadrant ? 0.30f : 0.18f);
                float thickness = radius * 0.035f;

                var tick = new GameObject($"Tick_{i * 15}");
                tick.transform.SetParent(transform, false);

                // The shaft mesh grows along its own up axis from its origin,
                // so pointing up inward makes the tick run from the ring toward
                // the centre.
                tick.transform.localRotation = Quaternion.FromToRotation(Vector3.up, -outward);
                tick.transform.localPosition = outward * radius;
                tick.transform.localScale = new Vector3(thickness, length, thickness);

                tick.AddComponent<MeshFilter>().sharedMesh = GizmoMeshes.Shaft();
                Dress(tick.AddComponent<MeshRenderer>(), material);
            }
        }

        private void AddNeedle(float radius, Color colour)
        {
            var material = new Material(ringMaterial) { name = "HoleRingNeedle" };
            material.SetColor(BaseColorId, Color.Lerp(colour, Color.white, 0.55f));

            var pivot = new GameObject("Needle");
            pivot.transform.SetParent(transform, false);
            needle = pivot.transform;

            var arm = new GameObject("Arm");
            arm.transform.SetParent(pivot.transform, false);

            // Laid along +X at zero degrees, so the needle's local rotation
            // about Y reads directly as the angle turned.
            arm.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            arm.transform.localScale = new Vector3(radius * 0.05f, radius, radius * 0.05f);

            arm.AddComponent<MeshFilter>().sharedMesh = GizmoMeshes.Shaft();
            Dress(arm.AddComponent<MeshRenderer>(), material);
        }

        private static void Dress(MeshRenderer renderer, Material material)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>
        /// Places the dial on the join.
        ///
        /// <paramref name="zeroDirection"/> is where the needle points at zero -
        /// the orientation the plain mate would have given. Measuring from
        /// there rather than from a world axis is what makes the reading mean
        /// "turned this far from square", which is the only question being
        /// asked.
        /// </summary>
        public void Show(Vector3 position, Vector3 axis, Vector3 zeroDirection, float angle)
        {
            Vector3 flat = Vector3.ProjectOnPlane(zeroDirection, axis);

            if (flat.sqrMagnitude < 1e-8f)
            {
                // The chosen reference happens to run along the axis; any
                // perpendicular will do as a zero mark.
                flat = Vector3.ProjectOnPlane(Vector3.up, axis);

                if (flat.sqrMagnitude < 1e-8f)
                {
                    flat = Vector3.ProjectOnPlane(Vector3.right, axis);
                }
            }

            // Up along the mating axis, +X on the zero mark: the dial's own
            // frame then matches the angle being reported.
            transform.SetPositionAndRotation(
                position,
                Quaternion.LookRotation(Vector3.Cross(flat.normalized, axis), axis));

            // Local Y is the mating axis, so a local turn about it is the same
            // rotation the part is being given - the needle and the part move
            // together by construction rather than by a matched sign.
            if (needle != null)
            {
                needle.localRotation = Quaternion.Euler(0f, angle, 0f);
            }

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
