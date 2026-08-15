namespace VexDesigner.EditorTools
{
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Builds the table saw at the end of the workbench.
    ///
    /// Much simpler than the machine it stands in for, deliberately. What the
    /// saw has to *do* is hold a part flat against a fence, show where the
    /// blade will fall, and take a slice off - so it is built as a bed, a
    /// fence, a blade and five knobs, and no effort is spent on a motor housing
    /// nobody will look at twice. The knobs are the interface, so they are the
    /// only part given real care: big enough to hit, spaced far enough apart to
    /// tell one from another, and colour-coded to the axis they turn.
    /// </summary>
    public static class SawStationBuilder
    {
        private const float InchesToMetres = 0.0254f;

        private static float In(float inches) => inches * InchesToMetres;

        // The bed is a long flat table with the fence along the back and the
        // blade a third of the way from the right, which leaves room for the
        // long side of a 35-hole channel to the left of the cut.
        private const float BedWidthIn = 34f;
        private const float BedDepthIn = 9f;
        private const float BedThicknessIn = 0.8f;
        private const float FenceHeightIn = 2.2f;
        private const float FenceThicknessIn = 0.8f;
        private const float BladeFromRightIn = 10f;

        /// <summary>
        /// Puts a saw on the workbench at <paramref name="position"/>, which is
        /// the top surface of the bench where the machine stands.
        /// </summary>
        public static GameObject Build(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("TableSaw");
            root.transform.SetPositionAndRotation(position, rotation);

            float bedTop = In(BedThicknessIn);
            float halfWidth = In(BedWidthIn) * 0.5f;
            float halfDepth = In(BedDepthIn) * 0.5f;

            float fenceZ = halfDepth - In(FenceThicknessIn);
            float bladeX = halfWidth - In(BladeFromRightIn);

            Slab(root.transform, "Bed",
                new Vector3(0f, In(BedThicknessIn) * 0.5f, 0f),
                new Vector3(In(BedWidthIn), In(BedThicknessIn), In(BedDepthIn)),
                new Color(0.20f, 0.21f, 0.24f));

            Slab(root.transform, "Fence",
                new Vector3(0f, bedTop + (In(FenceHeightIn) * 0.5f),
                    halfDepth - (In(FenceThicknessIn) * 0.5f)),
                new Vector3(In(BedWidthIn), In(FenceHeightIn), In(FenceThicknessIn)),
                new Color(0.55f, 0.45f, 0.12f));

            // The blade. Cosmetic - the cut is a plane, and this only says
            // where that plane is.
            var bladePivot = new GameObject("BladePivot");
            bladePivot.transform.SetParent(root.transform, false);
            bladePivot.transform.localPosition = new Vector3(bladeX, bedTop, 0f);

            GameObject blade = Slab(bladePivot.transform, "Blade",
                new Vector3(0f, In(3.5f), 0f),
                new Vector3(In(0.09f), In(7f), In(7f)),
                new Color(0.75f, 0.76f, 0.80f));

            Object.DestroyImmediate(blade.GetComponent<Collider>());

            // A thin bright line down the bed showing exactly where the cut
            // falls. The blade above it is round and its lowest point is hard
            // to judge from overhead; this is unambiguous.
            GameObject line = Slab(bladePivot.transform, "CutLine",
                new Vector3(0f, In(0.02f), 0f),
                new Vector3(In(0.06f), In(0.04f), In(BedDepthIn)),
                new Color(1f, 0.85f, 0.2f));

            Object.DestroyImmediate(line.GetComponent<Collider>());

            var viewpoint = new GameObject("Viewpoint");
            viewpoint.transform.SetParent(root.transform, false);
            viewpoint.transform.localPosition = new Vector3(0f, bedTop, 0f);

            // Knobs along the top of the fence, where they are reachable from
            // above and cannot be confused with the stock.
            float knobY = bedTop + In(FenceHeightIn) + In(0.35f);
            float knobZ = halfDepth - (In(FenceThicknessIn) * 0.5f);

            Knob(root.transform, "Knob_RotateX", SawKnob.Control.RotateX,
                new Vector3(-In(9f), knobY, knobZ), new Color(0.95f, 0.25f, 0.25f));

            Knob(root.transform, "Knob_RotateY", SawKnob.Control.RotateY,
                new Vector3(-In(6f), knobY, knobZ), new Color(0.35f, 0.9f, 0.35f));

            Knob(root.transform, "Knob_RotateZ", SawKnob.Control.RotateZ,
                new Vector3(-In(3f), knobY, knobZ), new Color(0.3f, 0.5f, 1f));

            Knob(root.transform, "Knob_Feed", SawKnob.Control.Feed,
                new Vector3(In(1f), knobY, knobZ), new Color(0.95f, 0.85f, 0.3f));

            Knob(root.transform, "Knob_Blade", SawKnob.Control.Blade,
                new Vector3(bladeX + In(4f), knobY, knobZ), new Color(0.9f, 0.5f, 0.15f));

            var station = root.AddComponent<SawStation>();
            var so = new UnityEditor.SerializedObject(station);

            so.FindProperty("bedY").floatValue = bedTop;
            so.FindProperty("fenceZ").floatValue = fenceZ;
            so.FindProperty("bladeX").floatValue = bladeX;
            so.FindProperty("viewpoint").objectReferenceValue = viewpoint.transform;
            so.FindProperty("bladeVisual").objectReferenceValue = bladePivot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        private static GameObject Slab(
            Transform parent, string name, Vector3 centre, Vector3 size, Color colour)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;

            Paint(go, colour, 0.35f, 0.5f);
            return go;
        }

        /// <summary>
        /// One control: a disc on a short post, with a handle so which way it
        /// has been turned is visible at a glance.
        /// </summary>
        private static void Knob(
            Transform parent, string name, SawKnob.Control control,
            Vector3 position, Color colour)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;

            var dial = new GameObject("Dial");
            dial.transform.SetParent(root.transform, false);

            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            face.name = "Face";
            face.transform.SetParent(dial.transform, false);
            face.transform.localScale = new Vector3(In(1.5f), In(0.15f), In(1.5f));
            Object.DestroyImmediate(face.GetComponent<Collider>());
            Paint(face, colour, 0.2f, 0.4f);

            GameObject grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            grip.name = "Grip";
            grip.transform.SetParent(dial.transform, false);
            grip.transform.localPosition = new Vector3(0f, In(0.25f), In(0.5f));
            grip.transform.localScale = new Vector3(In(0.28f), In(0.28f), In(0.28f));
            Object.DestroyImmediate(grip.GetComponent<Collider>());
            Paint(grip, new Color(0.1f, 0.1f, 0.11f), 0.1f, 0.5f);

            // One collider for the whole knob, generous enough to be an easy
            // target from a metre above the bed.
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = In(0.9f);
            collider.center = new Vector3(0f, In(0.1f), 0f);

            var knob = root.AddComponent<SawKnob>();
            knob.Configure(control);

            var so = new UnityEditor.SerializedObject(knob);
            so.FindProperty("dial").objectReferenceValue = dial.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Paint(GameObject go, Color colour, float metallic, float smoothness)
        {
            var renderer = go.GetComponent<MeshRenderer>();

            if (renderer == null)
            {
                return;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");

            var material = new Material(shader) { name = go.name };
            material.SetColor("_BaseColor", colour);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);

            renderer.sharedMaterial = material;
        }
    }
}
