namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Builds the mitre saw at the end of the workbench.
    ///
    /// Deliberately plain. The machine is scenery around a working area, and
    /// anything on the bed competes with the stock lying on it - which is the
    /// one thing the user is actually looking at. An earlier version had ribs,
    /// webs, feet and slots, and the bed came out looking like a parts bin.
    ///
    /// It is also stacked properly now: base, then bed, then fence. Before, the
    /// base was two and a half inches tall while the bed sat one inch up, so
    /// the whole substructure stood up through the working surface.
    ///
    /// None of it is interactive. The controls are the stock, the ball that
    /// turns it and the blade, all grabbed where they are.
    /// </summary>
    public static class SawStationBuilder
    {
        private const float InchesToMetres = 0.0254f;

        private static float In(float inches) => inches * InchesToMetres;

        // --- Proportions ------------------------------------------------
        private const float BedWidthIn = 34f;
        private const float BedDepthIn = 11f;

        /// <summary>Height of the plinth the bed sits on.</summary>
        private const float BaseHeightIn = 1.8f;

        private const float BedThicknessIn = 1.0f;
        private const float FenceHeightIn = 2.4f;
        private const float FenceThicknessIn = 0.9f;
        private const float BladeFromRightIn = 11f;

        /// <summary>
        /// Blade radius. Small enough to clear the working area: a ten-inch
        /// blade on an eleven-inch bed filled the screen and hid the stock it
        /// was about to cut.
        /// </summary>
        private const float BladeRadiusIn = 3.5f;

        // --- Palette ----------------------------------------------------
        private static readonly Color CastIron = new Color(0.15f, 0.16f, 0.18f);
        private static readonly Color Aluminium = new Color(0.60f, 0.62f, 0.66f);
        private static readonly Color MachinedSteel = new Color(0.55f, 0.57f, 0.61f);
        private static readonly Color MachineBlue = new Color(0.10f, 0.26f, 0.55f);
        private static readonly Color BladeSteel = new Color(0.70f, 0.72f, 0.76f);

        public static GameObject Build(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("MitreSaw");
            root.transform.SetPositionAndRotation(position, rotation);

            float baseTop = In(BaseHeightIn);
            float bedTop = baseTop + In(BedThicknessIn);

            float halfWidth = In(BedWidthIn) * 0.5f;
            float halfDepth = In(BedDepthIn) * 0.5f;

            float fenceZ = halfDepth - In(FenceThicknessIn);
            float bladeX = halfWidth - In(BladeFromRightIn);

            BuildBase(root.transform, baseTop, halfWidth);
            BuildBed(root.transform, baseTop, bedTop, halfDepth);
            BuildFence(root.transform, bedTop, halfDepth);
            BuildScale(root.transform, bedTop, halfWidth, halfDepth, bladeX);

            Transform pivot = BuildHead(root.transform, bedTop, fenceZ, bladeX, out Transform head);

            var viewpoint = new GameObject("Viewpoint");
            viewpoint.transform.SetParent(root.transform, false);
            viewpoint.transform.localPosition = new Vector3(bladeX - In(5f), bedTop, 0f);

            var station = root.AddComponent<SawStation>();
            var so = new SerializedObject(station);

            so.FindProperty("bedY").floatValue = bedTop;
            so.FindProperty("fenceZ").floatValue = fenceZ;
            so.FindProperty("bladeX").floatValue = bladeX;
            so.FindProperty("viewpoint").objectReferenceValue = viewpoint.transform;
            so.FindProperty("bladeVisual").objectReferenceValue = pivot;
            so.FindProperty("head").objectReferenceValue = head;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<SawHandles>();

            var annotations = new GameObject("Annotations");
            annotations.transform.SetParent(root.transform, false);
            annotations.AddComponent<SawAnnotations>();

            return root;
        }

        // ------------------------------------------------------------------

        /// <summary>A plain plinth under each end, entirely below the bed.</summary>
        private static void BuildBase(Transform parent, float baseTop, float halfWidth)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Box(parent, "Plinth",
                    new Vector3(side * (halfWidth - In(5f)), baseTop * 0.5f, 0f),
                    new Vector3(In(8f), baseTop, In(BedDepthIn) - In(1.5f)),
                    CastIron, 0.5f, 0.3f);
            }
        }

        /// <summary>A clean milled plate. Nothing on it but the stock.</summary>
        private static void BuildBed(
            Transform parent, float baseTop, float bedTop, float halfDepth)
        {
            Box(parent, "Bed",
                new Vector3(0f, (baseTop + bedTop) * 0.5f, 0f),
                new Vector3(In(BedWidthIn), bedTop - baseTop, In(BedDepthIn)),
                Aluminium, 0.85f, 0.6f);

            // One bright chamfer along the front edge. The single detail worth
            // keeping: it is what says "machined" at a glance, and it is at the
            // edge rather than in the middle of the working area.
            Box(parent, "FrontEdge",
                new Vector3(0f, bedTop - In(0.08f), -halfDepth + In(0.12f)),
                new Vector3(In(BedWidthIn), In(0.18f), In(0.24f)),
                MachinedSteel, 0.9f, 0.75f);
        }

        private static void BuildFence(Transform parent, float bedTop, float halfDepth)
        {
            float centreZ = halfDepth - (In(FenceThicknessIn) * 0.5f);

            Box(parent, "Fence",
                new Vector3(0f, bedTop + (In(FenceHeightIn) * 0.5f), centreZ),
                new Vector3(In(BedWidthIn), In(FenceHeightIn), In(FenceThicknessIn)),
                CastIron, 0.55f, 0.32f);

            // The working face, ground bright: the surface stock registers on.
            Box(parent, "FenceFace",
                new Vector3(0f, bedTop + (In(FenceHeightIn) * 0.5f),
                    centreZ - (In(FenceThicknessIn) * 0.5f) + In(0.05f)),
                new Vector3(In(BedWidthIn), In(FenceHeightIn) - In(0.25f), In(0.1f)),
                MachinedSteel, 0.92f, 0.8f);
        }

        /// <summary>An inch scale along the front lip, zeroed at the blade.</summary>
        private static void BuildScale(
            Transform parent, float bedTop, float halfWidth, float halfDepth, float bladeX)
        {
            var scale = new GameObject("Scale");
            scale.transform.SetParent(parent, false);

            float z = -halfDepth + In(0.5f);

            for (int inch = -34; inch <= 34; inch++)
            {
                float x = bladeX - In(inch);

                if (Mathf.Abs(x) > halfWidth - In(0.6f) || inch % 2 != 0)
                {
                    continue;
                }

                bool major = inch % 6 == 0;

                Box(scale.transform, $"Tick{inch}",
                    new Vector3(x, bedTop + In(0.01f), z),
                    new Vector3(In(0.06f), In(0.02f), In(major ? 0.7f : 0.4f)),
                    major ? Color.white : new Color(0.7f, 0.72f, 0.75f), 0.1f, 0.4f);
            }
        }

        /// <summary>
        /// The head: a column at the fence carrying an arm, a motor and the
        /// blade, hinged where the cut plane turns.
        /// </summary>
        private static Transform BuildHead(
            Transform parent, float bedTop, float fenceZ, float bladeX, out Transform head)
        {
            var pivot = new GameObject("HeadPivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(bladeX, bedTop, fenceZ);

            // Everything that can get between the camera and the stock lives
            // under here, so it can be faded out in one move while the machine
            // is being set up.
            var headRoot = new GameObject("Head");
            headRoot.transform.SetParent(pivot.transform, false);
            head = headRoot.transform;

            float reach = -In(BedDepthIn) * 0.34f;

            Box(headRoot.transform, "Column", new Vector3(0f, In(3.6f), In(0.3f)),
                new Vector3(In(2.0f), In(7.2f), In(2.0f)),
                MachineBlue, 0.35f, 0.45f);

            Box(headRoot.transform, "Arm", new Vector3(0f, In(7.0f), reach * 0.5f),
                new Vector3(In(1.7f), In(1.7f), Mathf.Abs(reach) + In(1.4f)),
                MachineBlue, 0.35f, 0.45f);

            Cylinder(headRoot.transform, "Motor", new Vector3(In(2.2f), In(7.0f), reach),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(2.2f), In(1.6f), In(2.2f)),
                MachineBlue, 0.4f, 0.5f);

            // Hung so the blade just reaches the bed - a saw blade has to touch
            // what it cuts - while the bulk of it stays up out of the way.
            var bladeRoot = new GameObject("Blade");
            bladeRoot.transform.SetParent(headRoot.transform, false);
            bladeRoot.transform.localPosition =
                new Vector3(0f, In(BladeRadiusIn) - In(0.25f), reach);

            Cylinder(bladeRoot.transform, "Plate", Vector3.zero,
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(BladeRadiusIn * 2f), In(0.04f), In(BladeRadiusIn * 2f)),
                BladeSteel, 0.95f, 0.85f);

            // A ring of teeth. The one detail that makes a disc read as a saw
            // blade rather than a wheel, and cheap at twenty-four.
            const int teeth = 24;

            for (int i = 0; i < teeth; i++)
            {
                float angle = (i / (float)teeth) * Mathf.PI * 2f;

                Box(bladeRoot.transform, "Tooth",
                    new Vector3(0f, Mathf.Cos(angle) * In(BladeRadiusIn),
                        Mathf.Sin(angle) * In(BladeRadiusIn)),
                    new Vector3(In(0.07f), In(0.22f), In(0.13f)),
                    new Color(0.85f, 0.86f, 0.88f), 0.95f, 0.9f);
            }

            Cylinder(bladeRoot.transform, "Arbor", Vector3.zero,
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(1.0f), In(0.08f), In(1.0f)),
                CastIron, 0.7f, 0.4f);

            return pivot.transform;
        }

        // ------------------------------------------------------------------
        // Primitives
        // ------------------------------------------------------------------

        private static void Box(
            Transform parent, string name, Vector3 centre, Vector3 size,
            Color colour, float metallic, float smoothness)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localScale = size;

            Object.DestroyImmediate(go.GetComponent<Collider>());
            Paint(go, colour, metallic, smoothness);
        }

        private static void Cylinder(
            Transform parent, string name, Vector3 centre, Quaternion rotation,
            Vector3 size, Color colour, float metallic, float smoothness)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre;
            go.transform.localRotation = rotation;
            go.transform.localScale = size;

            Object.DestroyImmediate(go.GetComponent<Collider>());
            Paint(go, colour, metallic, smoothness);
        }

        /// <summary>One material per colour and finish, shared across the machine.</summary>
        private static void Paint(GameObject go, Color colour, float metallic, float smoothness)
        {
            var renderer = go.GetComponent<MeshRenderer>();

            if (renderer == null)
            {
                return;
            }

            int key = colour.GetHashCode() ^
                (Mathf.RoundToInt(metallic * 100f) * 31) ^
                (Mathf.RoundToInt(smoothness * 100f) * 131);

            if (!materials.TryGetValue(key, out Material material) || material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                {
                    name = "SawSurface",
                };

                material.SetColor("_BaseColor", colour);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);

                materials[key] = material;
            }

            renderer.sharedMaterial = material;
        }

        private static readonly Dictionary<int, Material> materials =
            new Dictionary<int, Material>();
    }
}
