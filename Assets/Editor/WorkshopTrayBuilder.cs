namespace VexDesigner.EditorTools
{
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Builds the compartmented parts organiser that sits on the left of the
    /// table, modelled on a workshop drawer tray.
    ///
    /// All dimensions are in inches and converted once at point of use, for the
    /// same reason as the rest of the scene - VEX parts are specified in inches
    /// and a tray that cannot hold a 17.5" C-channel is a tray that is wrong.
    /// The compartments run front-to-back precisely so long stock fits.
    /// </summary>
    public static class WorkshopTrayBuilder
    {
        private const float InchesToMetres = 0.0254f;

        private const float TrayWidthIn = 16f;
        private const float TrayDepthIn = 26f;
        private const float TrayWallHeightIn = 2.2f;
        private const float WallThicknessIn = 0.4f;
        private const float FloorThicknessIn = 0.3f;

        private const int CompartmentCount = 3;

        private static float In(float inches) => inches * InchesToMetres;

        /// <summary>
        /// Creates the tray at <paramref name="centreIn"/> (inches, on the
        /// table surface at <paramref name="surfaceHeightIn"/>).
        /// </summary>
        public static GameObject Build(
            Vector2 centreIn,
            float surfaceHeightIn,
            Material shellMaterial,
            Material partMaterial,
            PartDefinition stockedPart)
        {
            var root = new GameObject("PartsTray");
            root.isStatic = true;

            float innerWidth = TrayWidthIn - (2f * WallThicknessIn);
            float compartmentWidth =
                (innerWidth - ((CompartmentCount - 1) * WallThicknessIn)) / CompartmentCount;
            float innerDepth = TrayDepthIn - (2f * WallThicknessIn);

            BuildShell(root, centreIn, surfaceHeightIn, shellMaterial);

            // Compartment centres, left to right.
            float firstCentreX =
                centreIn.x - (innerWidth * 0.5f) + (compartmentWidth * 0.5f);

            for (int i = 0; i < CompartmentCount; i++)
            {
                float x = firstCentreX + (i * (compartmentWidth + WallThicknessIn));

                // Dividers sit between compartments, not outside them.
                if (i > 0)
                {
                    float dividerX = x - (compartmentWidth * 0.5f) - (WallThicknessIn * 0.5f);
                    Box(root.transform, $"Divider_{i}",
                        new Vector3(dividerX, surfaceHeightIn + (TrayWallHeightIn * 0.5f), centreIn.y),
                        new Vector3(WallThicknessIn, TrayWallHeightIn - FloorThicknessIn, innerDepth),
                        shellMaterial);
                }

                // Only the first compartment is stocked for now; the others
                // exist so the tray reads as an organiser rather than a box,
                // and so adding a part type later is just assigning a
                // definition rather than rebuilding geometry.
                PartDefinition contents = i == 0 ? stockedPart : null;

                BuildCompartment(
                    root.transform, i,
                    new Vector2(x, centreIn.y),
                    surfaceHeightIn,
                    compartmentWidth, innerDepth,
                    shellMaterial, partMaterial, contents);
            }

            return root;
        }

        private static void BuildShell(
            GameObject root, Vector2 centreIn, float surfaceHeightIn, Material material)
        {
            float wallCentreY = surfaceHeightIn + (TrayWallHeightIn * 0.5f);

            Box(root.transform, "TrayFloor",
                new Vector3(centreIn.x, surfaceHeightIn + (FloorThicknessIn * 0.5f), centreIn.y),
                new Vector3(TrayWidthIn, FloorThicknessIn, TrayDepthIn),
                material);

            float halfW = (TrayWidthIn - WallThicknessIn) * 0.5f;
            float halfD = (TrayDepthIn - WallThicknessIn) * 0.5f;

            Box(root.transform, "Wall_Left",
                new Vector3(centreIn.x - halfW, wallCentreY, centreIn.y),
                new Vector3(WallThicknessIn, TrayWallHeightIn, TrayDepthIn), material);

            Box(root.transform, "Wall_Right",
                new Vector3(centreIn.x + halfW, wallCentreY, centreIn.y),
                new Vector3(WallThicknessIn, TrayWallHeightIn, TrayDepthIn), material);

            Box(root.transform, "Wall_Front",
                new Vector3(centreIn.x, wallCentreY, centreIn.y - halfD),
                new Vector3(TrayWidthIn, TrayWallHeightIn, WallThicknessIn), material);

            Box(root.transform, "Wall_Back",
                new Vector3(centreIn.x, wallCentreY, centreIn.y + halfD),
                new Vector3(TrayWidthIn, TrayWallHeightIn, WallThicknessIn), material);
        }

        private static void BuildCompartment(
            Transform parent, int index, Vector2 centreIn, float surfaceHeightIn,
            float widthIn, float depthIn,
            Material shellMaterial, Material partMaterial, PartDefinition contents)
        {
            var bin = new GameObject($"Bin_{index}");
            bin.transform.SetParent(parent, false);

            float floorTopIn = surfaceHeightIn + FloorThicknessIn;

            // The collider covers the whole compartment volume, extended above
            // the walls, so the user can aim anywhere over the compartment
            // rather than having to hit a specific part.
            var collider = bin.AddComponent<BoxCollider>();
            collider.center = new Vector3(
                In(centreIn.x),
                In(floorTopIn + (TrayWallHeightIn * 0.5f)),
                In(centreIn.y));
            collider.size = new Vector3(In(widthIn), In(TrayWallHeightIn), In(depthIn));

            var highlight = bin.AddComponent<Highlightable>();
            var partBin = bin.AddComponent<PartBin>();
            partBin.Configure(contents);

            // An inner floor panel gives the highlight something to light up
            // even when the compartment is empty.
            GameObject panel = Box(bin.transform, "Panel",
                new Vector3(centreIn.x, floorTopIn + 0.02f, centreIn.y),
                new Vector3(widthIn * 0.98f, 0.04f, depthIn * 0.98f),
                shellMaterial);
            panel.GetComponent<Collider>().enabled = false;

            if (contents != null && contents.mesh != null)
            {
                StockCompartment(bin.transform, centreIn, floorTopIn, widthIn, contents, partMaterial);
            }
        }

        /// <summary>
        /// Lays display copies of the part in the compartment. These are
        /// visual only - no collider, no physics. Taking one spawns a fresh
        /// object rather than removing these, because the bin is an infinite
        /// source.
        /// </summary>
        private static void StockCompartment(
            Transform bin, Vector2 centreIn, float floorTopIn, float widthIn,
            PartDefinition definition, Material material)
        {
            const int displayCount = 3;

            // Lay the part along the compartment's long axis, resting on the
            // face that a C-channel naturally sits on.
            var rotation = Quaternion.Euler(0f, 90f, 90f);

            for (int i = 0; i < displayCount; i++)
            {
                var go = new GameObject($"Display_{definition.partId}_{i}");
                go.transform.SetParent(bin, false);
                go.transform.rotation = rotation;

                go.AddComponent<MeshFilter>().sharedMesh = definition.mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;

                // Spread the copies across the compartment width.
                float spacing = widthIn / (displayCount + 1f);
                float x = centreIn.x - (widthIn * 0.5f) + (spacing * (i + 1));

                // Position from the rendered bounds rather than the mesh
                // origin: an imported CAD mesh has its origin wherever the
                // original modeller left it, which is rarely the centre.
                go.transform.position = new Vector3(In(x), 0f, In(centreIn.y));
                Bounds b = renderer.bounds;
                float lift = In(floorTopIn) - b.min.y;
                go.transform.position += new Vector3(0f, lift, 0f);
            }
        }

        private static GameObject Box(
            Transform parent, string name, Vector3 centreIn, Vector3 sizeIn, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(In(centreIn.x), In(centreIn.y), In(centreIn.z));
            go.transform.localScale = new Vector3(In(sizeIn.x), In(sizeIn.y), In(sizeIn.z));
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.isStatic = true;
            return go;
        }
    }
}
