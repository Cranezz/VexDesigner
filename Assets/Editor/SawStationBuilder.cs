namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Builds the mitre saw at the end of the workbench.
    ///
    /// Modelled on a real cold saw: a cast base, an extruded aluminium bed with
    /// a scale down it, a ground fence, and a hinged head carrying a motor, a
    /// guard and a toothed blade. It is made of primitives, but of enough of
    /// them and with the right materials that it reads as a machine tool rather
    /// than a box with a disc on it - which matters here, because the point of
    /// the workshop is that a part made in it could be made again on a real one.
    ///
    /// None of it is interactive. The controls are the stock, the ball that
    /// turns it and the blade, all grabbed where they are; the machine around
    /// them is scenery that says what kind of machine it is.
    /// </summary>
    public static class SawStationBuilder
    {
        private const float InchesToMetres = 0.0254f;

        private static float In(float inches) => inches * InchesToMetres;

        // --- Proportions ------------------------------------------------
        private const float BedWidthIn = 34f;
        private const float BedDepthIn = 11f;
        private const float BedThicknessIn = 1.1f;
        private const float FenceHeightIn = 2.6f;
        private const float FenceThicknessIn = 0.9f;
        private const float BladeFromRightIn = 11f;
        private const float BladeRadiusIn = 5f;

        // --- Palette ----------------------------------------------------
        private static readonly Color CastIron = new Color(0.13f, 0.14f, 0.16f);
        private static readonly Color Aluminium = new Color(0.62f, 0.64f, 0.68f);
        private static readonly Color MachinedSteel = new Color(0.55f, 0.57f, 0.61f);
        private static readonly Color MachineBlue = new Color(0.09f, 0.24f, 0.52f);
        private static readonly Color Warning = new Color(0.85f, 0.62f, 0.05f);
        private static readonly Color Rubber = new Color(0.07f, 0.07f, 0.08f);

        public static GameObject Build(Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("MitreSaw");
            root.transform.SetPositionAndRotation(position, rotation);

            float bedTop = In(BedThicknessIn);
            float halfWidth = In(BedWidthIn) * 0.5f;
            float halfDepth = In(BedDepthIn) * 0.5f;

            float fenceZ = halfDepth - In(FenceThicknessIn);
            float bladeX = halfWidth - In(BladeFromRightIn);

            BuildBase(root.transform, halfWidth, halfDepth);
            BuildBed(root.transform, bedTop, halfDepth);
            BuildFence(root.transform, bedTop, halfDepth);
            BuildScale(root.transform, bedTop, halfWidth, halfDepth, bladeX);

            Transform pivot = BuildHead(root.transform, bedTop, fenceZ, bladeX);

            var viewpoint = new GameObject("Viewpoint");
            viewpoint.transform.SetParent(root.transform, false);
            viewpoint.transform.localPosition = new Vector3(bladeX - In(4f), bedTop, 0f);

            var station = root.AddComponent<SawStation>();
            var so = new SerializedObject(station);

            so.FindProperty("bedY").floatValue = bedTop;
            so.FindProperty("fenceZ").floatValue = fenceZ;
            so.FindProperty("bladeX").floatValue = bladeX;
            so.FindProperty("viewpoint").objectReferenceValue = viewpoint.transform;
            so.FindProperty("bladeVisual").objectReferenceValue = pivot;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<SawHandles>();

            var annotations = new GameObject("Annotations");
            annotations.transform.SetParent(root.transform, false);
            annotations.AddComponent<SawAnnotations>();

            return root;
        }

        // ------------------------------------------------------------------
        // The machine
        // ------------------------------------------------------------------

        /// <summary>Cast pedestals under each end, ribbed and joined by a web.</summary>
        private static void BuildBase(Transform parent, float halfWidth, float halfDepth)
        {
            float footHeight = In(2.4f);

            for (int side = -1; side <= 1; side += 2)
            {
                Box(parent, "Pedestal",
                    new Vector3(side * (halfWidth - In(4f)), footHeight * 0.5f, 0f),
                    new Vector3(In(6f), footHeight, In(BedDepthIn) - In(1.2f)),
                    CastIron, 0.55f, 0.28f);
            }

            // A web between them, which is what stops a real one ringing.
            Box(parent, "Web", new Vector3(0f, footHeight * 0.55f, 0f),
                new Vector3(In(BedWidthIn) - In(9f), In(1.4f), In(3.2f)),
                CastIron, 0.55f, 0.24f);

            for (int i = -2; i <= 2; i++)
            {
                Box(parent, "Rib", new Vector3(i * In(4.5f), footHeight * 0.5f, 0f),
                    new Vector3(In(0.8f), footHeight * 0.9f, In(4.2f)),
                    CastIron, 0.55f, 0.22f);
            }

            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Box(parent, "Foot",
                        new Vector3(x * (halfWidth - In(3f)), In(0.18f),
                            z * (halfDepth - In(1.6f))),
                        new Vector3(In(1.6f), In(0.36f), In(1.6f)),
                        Rubber, 0.05f, 0.25f);
                }
            }
        }

        /// <summary>A milled aluminium plate with T-slots down it.</summary>
        private static void BuildBed(Transform parent, float bedTop, float halfDepth)
        {
            Box(parent, "Bed", new Vector3(0f, bedTop * 0.5f, 0f),
                new Vector3(In(BedWidthIn), bedTop, In(BedDepthIn)),
                Aluminium, 0.85f, 0.62f);

            // Slots drawn as dark insets rather than cut for real: identical
            // from above, and four boxes instead of a mesh.
            for (int i = -1; i <= 1; i++)
            {
                Box(parent, "Slot",
                    new Vector3(0f, bedTop - In(0.02f), i * In(3.2f)),
                    new Vector3(In(BedWidthIn) - In(2f), In(0.12f), In(0.42f)),
                    new Color(0.08f, 0.09f, 0.10f), 0.4f, 0.35f);
            }

            // A bright chamfer along the front edge, which is the detail that
            // most says "machined" at a glance.
            Box(parent, "FrontEdge",
                new Vector3(0f, bedTop - In(0.1f), -halfDepth + In(0.15f)),
                new Vector3(In(BedWidthIn), In(0.22f), In(0.3f)),
                MachinedSteel, 0.9f, 0.75f);
        }

        /// <summary>A ground steel face on a cast carrier.</summary>
        private static void BuildFence(Transform parent, float bedTop, float halfDepth)
        {
            float centreZ = halfDepth - (In(FenceThicknessIn) * 0.5f);

            Box(parent, "FenceBody",
                new Vector3(0f, bedTop + (In(FenceHeightIn) * 0.5f), centreZ),
                new Vector3(In(BedWidthIn), In(FenceHeightIn), In(FenceThicknessIn)),
                CastIron, 0.6f, 0.3f);

            // The working face, ground bright: the surface stock registers on.
            Box(parent, "FenceFace",
                new Vector3(0f, bedTop + (In(FenceHeightIn) * 0.5f),
                    centreZ - (In(FenceThicknessIn) * 0.5f) + In(0.06f)),
                new Vector3(In(BedWidthIn), In(FenceHeightIn) - In(0.3f), In(0.12f)),
                MachinedSteel, 0.95f, 0.8f);

            for (int i = -3; i <= 3; i++)
            {
                if (i == 0)
                {
                    continue;
                }

                Cylinder(parent, "FenceBolt",
                    new Vector3(i * In(4.5f), bedTop + In(FenceHeightIn) - In(0.5f),
                        centreZ - (In(FenceThicknessIn) * 0.5f) - In(0.02f)),
                    Quaternion.Euler(90f, 0f, 0f),
                    new Vector3(In(0.5f), In(0.06f), In(0.5f)),
                    MachinedSteel, 0.9f, 0.5f);
            }
        }

        /// <summary>An inch scale along the front of the bed, zeroed at the blade.</summary>
        private static void BuildScale(
            Transform parent, float bedTop, float halfWidth, float halfDepth, float bladeX)
        {
            var scale = new GameObject("Scale");
            scale.transform.SetParent(parent, false);

            float z = -halfDepth + In(0.55f);

            for (int inch = -34; inch <= 34; inch++)
            {
                float x = bladeX - In(inch);

                if (Mathf.Abs(x) > halfWidth - In(0.6f))
                {
                    continue;
                }

                bool major = inch % 6 == 0;
                bool half = inch % 2 == 0;

                if (!half && !major)
                {
                    continue;
                }

                Box(scale.transform, $"Tick{inch}",
                    new Vector3(x, bedTop + In(0.01f), z),
                    new Vector3(In(major ? 0.08f : 0.05f), In(0.02f), In(major ? 0.9f : 0.5f)),
                    major ? Color.white : new Color(0.75f, 0.76f, 0.78f), 0.1f, 0.4f);
            }
        }

        /// <summary>
        /// The head: an arm off the fence carrying a motor, a guard and the
        /// blade, hinged where the cut plane turns.
        /// </summary>
        private static Transform BuildHead(
            Transform parent, float bedTop, float fenceZ, float bladeX)
        {
            // Hinged at the fence, which is where the cut plane turns. A head
            // hinged anywhere else disagrees with the red preview the moment it
            // is swung.
            var pivot = new GameObject("HeadPivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(bladeX, bedTop, fenceZ);

            Cylinder(pivot.transform, "Turret", new Vector3(0f, In(0.25f), 0f),
                Quaternion.identity, new Vector3(In(4.5f), In(0.25f), In(4.5f)),
                CastIron, 0.6f, 0.35f);

            float reach = -In(BedDepthIn) * 0.42f;

            Box(pivot.transform, "Column", new Vector3(0f, In(3.2f), In(0.4f)),
                new Vector3(In(2.4f), In(6.4f), In(2.4f)),
                MachineBlue, 0.35f, 0.45f);

            Box(pivot.transform, "Arm", new Vector3(0f, In(6.2f), reach * 0.5f),
                new Vector3(In(2.0f), In(2.0f), Mathf.Abs(reach) + In(1.6f)),
                MachineBlue, 0.35f, 0.45f);

            Cylinder(pivot.transform, "Motor", new Vector3(In(2.6f), In(6.2f), reach),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(2.6f), In(1.8f), In(2.6f)),
                MachineBlue, 0.4f, 0.5f);

            Cylinder(pivot.transform, "MotorCap", new Vector3(In(4.5f), In(6.2f), reach),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(2.2f), In(0.25f), In(2.2f)),
                CastIron, 0.6f, 0.3f);

            var bladeRoot = new GameObject("Blade");
            bladeRoot.transform.SetParent(pivot.transform, false);
            bladeRoot.transform.localPosition = new Vector3(0f, In(3.4f), reach);

            Cylinder(bladeRoot.transform, "Plate", Vector3.zero,
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(BladeRadiusIn * 2f), In(0.045f), In(BladeRadiusIn * 2f)),
                MachinedSteel, 0.95f, 0.85f);

            // Teeth. A toothed rim is what makes a disc read as a saw blade
            // rather than as a wheel, and it is the one detail worth the
            // primitives.
            const int teeth = 48;

            for (int i = 0; i < teeth; i++)
            {
                float angle = (i / (float)teeth) * Mathf.PI * 2f;

                Box(bladeRoot.transform, "Tooth",
                    new Vector3(0f, Mathf.Cos(angle) * In(BladeRadiusIn),
                        Mathf.Sin(angle) * In(BladeRadiusIn)),
                    new Vector3(In(0.09f), In(0.28f), In(0.16f)),
                    new Color(0.82f, 0.83f, 0.85f), 0.95f, 0.9f);
            }

            Cylinder(bladeRoot.transform, "Arbor", Vector3.zero,
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(1.3f), In(0.09f), In(1.3f)),
                CastIron, 0.7f, 0.4f);

            // The guard covers the top half only, so the working edge - the
            // part that says where the cut falls - stays visible.
            Cylinder(bladeRoot.transform, "Guard", new Vector3(0f, In(1.2f), 0f),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(BladeRadiusIn * 2f + 1.2f), In(0.75f),
                    In(BladeRadiusIn * 2f + 1.2f)),
                Warning, 0.15f, 0.4f);

            Box(pivot.transform, "HandleStem", new Vector3(0f, In(8.2f), reach * 0.4f),
                new Vector3(In(0.8f), In(2.6f), In(0.8f)), CastIron, 0.5f, 0.35f);

            Cylinder(pivot.transform, "Handle", new Vector3(0f, In(9.4f), reach * 0.4f),
                Quaternion.Euler(0f, 0f, 90f),
                new Vector3(In(1.1f), In(2.4f), In(1.1f)), Rubber, 0.05f, 0.3f);

            return pivot.transform;
        }

        // ------------------------------------------------------------------
        // Primitives
        // ------------------------------------------------------------------

        private static GameObject Box(
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
            return go;
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

        /// <summary>
        /// One material per colour and finish, shared across the machine.
        ///
        /// A saw built from three hundred primitives would otherwise carry
        /// three hundred materials, and three hundred draw calls, to describe
        /// six distinct surfaces.
        /// </summary>
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
