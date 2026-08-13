namespace VexDesigner.EditorTools
{
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Builds the garage the workshop sits in.
    ///
    /// A real two-car garage: 20 ft square with a 9 ft ceiling. Getting the
    /// room's true size right matters more than it sounds, because the player
    /// now walks around inside it - a room built to look right from one fixed
    /// camera angle feels immediately wrong the moment you can pace across it,
    /// and it would feel wrong again in VR.
    ///
    /// Authored in inches throughout, converted once at point of use.
    /// </summary>
    public static class GarageRoomBuilder
    {
        private const float InchesToMetres = 0.0254f;

        // Room shell.
        public const float RoomWidthIn = 240f;   // X
        public const float RoomDepthIn = 240f;   // Z
        public const float RoomHeightIn = 108f;
        private const float WallThicknessIn = 6f;

        // Garage door, on the -Z wall. The player enters here.
        private const float DoorWidthIn = 108f;
        private const float DoorHeightIn = 84f;

        // Benches.
        private const float BenchHeightIn = 36f;
        private const float BenchDepthIn = 24f;
        private const float BenchTopThicknessIn = 1.5f;

        public static Vector3 PlayerSpawnPosition =>
            new Vector3(0f, In(4f), In(-(RoomDepthIn * 0.5f) + 26f));

        /// <summary>Facing into the room, away from the garage door.</summary>
        public const float PlayerSpawnYaw = 0f;

        private static float In(float inches) => inches * InchesToMetres;

        public static GameObject Build()
        {
            WorkshopMaterials.BuildAll();

            var root = new GameObject("Garage");

            BuildShell(root);
            BuildGarageDoor(root);
            BuildBackBench(root);
            BuildSideBench(root);
            BuildClutter(root);
            BuildLighting(root);

            return root;
        }

        // ------------------------------------------------------------------
        // Shell
        // ------------------------------------------------------------------

        private static void BuildShell(GameObject root)
        {
            float halfW = RoomWidthIn * 0.5f;
            float halfD = RoomDepthIn * 0.5f;
            float half = WallThicknessIn * 0.5f;

            Box(root, "Floor",
                new Vector3(0f, -half, 0f),
                new Vector3(RoomWidthIn, WallThicknessIn, RoomDepthIn),
                WorkshopMaterials.Concrete, RoomWidthIn, RoomDepthIn);

            Box(root, "Ceiling",
                new Vector3(0f, RoomHeightIn + half, 0f),
                new Vector3(RoomWidthIn, WallThicknessIn, RoomDepthIn),
                WorkshopMaterials.Drywall, RoomWidthIn, RoomDepthIn);

            // Right-hand wall is bare block, as in the reference. The others
            // are finished drywall.
            Box(root, "Wall_Right",
                new Vector3(halfW + half, RoomHeightIn * 0.5f, 0f),
                new Vector3(WallThicknessIn, RoomHeightIn, RoomDepthIn),
                WorkshopMaterials.CinderBlock, RoomDepthIn, RoomHeightIn);

            Box(root, "Wall_Left",
                new Vector3(-halfW - half, RoomHeightIn * 0.5f, 0f),
                new Vector3(WallThicknessIn, RoomHeightIn, RoomDepthIn),
                WorkshopMaterials.Drywall, RoomDepthIn, RoomHeightIn);

            Box(root, "Wall_Back",
                new Vector3(0f, RoomHeightIn * 0.5f, halfD + half),
                new Vector3(RoomWidthIn, RoomHeightIn, WallThicknessIn),
                WorkshopMaterials.Drywall, RoomWidthIn, RoomHeightIn);

            BuildFrontWallWithOpening(root, halfD, half);
        }

        /// <summary>
        /// The front wall, built as three pieces around the door opening.
        /// Cutting a hole in a box needs real geometry work; three boxes is
        /// exact, cheap, and keeps every piece a clean collider.
        /// </summary>
        private static void BuildFrontWallWithOpening(GameObject root, float halfD, float half)
        {
            float sidePanel = (RoomWidthIn - DoorWidthIn) * 0.5f;
            float sideCentre = (DoorWidthIn + sidePanel) * 0.5f;
            float headerHeight = RoomHeightIn - DoorHeightIn;

            Box(root, "Wall_Front_L",
                new Vector3(-sideCentre, RoomHeightIn * 0.5f, -halfD - half),
                new Vector3(sidePanel, RoomHeightIn, WallThicknessIn),
                WorkshopMaterials.Drywall, sidePanel, RoomHeightIn);

            Box(root, "Wall_Front_R",
                new Vector3(sideCentre, RoomHeightIn * 0.5f, -halfD - half),
                new Vector3(sidePanel, RoomHeightIn, WallThicknessIn),
                WorkshopMaterials.Drywall, sidePanel, RoomHeightIn);

            Box(root, "Wall_Front_Header",
                new Vector3(0f, DoorHeightIn + (headerHeight * 0.5f), -halfD - half),
                new Vector3(DoorWidthIn, headerHeight, WallThicknessIn),
                WorkshopMaterials.Drywall, DoorWidthIn, headerHeight);
        }

        // ------------------------------------------------------------------
        // Garage door
        // ------------------------------------------------------------------

        private static void BuildGarageDoor(GameObject root)
        {
            var door = new GameObject("GarageDoor");
            door.transform.SetParent(root.transform, false);

            const int panels = 4;
            float panelHeight = DoorHeightIn / panels;
            float z = -(RoomDepthIn * 0.5f) + 2f;

            for (int i = 0; i < panels; i++)
            {
                float y = (panelHeight * i) + (panelHeight * 0.5f);

                // A small gap between panels reads as the hinge line without
                // needing separate trim geometry.
                Box(door, $"Panel_{i}",
                    new Vector3(0f, y, z),
                    new Vector3(DoorWidthIn, panelHeight - 0.75f, 2.5f),
                    WorkshopMaterials.PaintedMetal, DoorWidthIn, panelHeight);

                // Windows in the top panel only, as most sectional doors have.
                if (i == panels - 1)
                {
                    for (int w = 0; w < 4; w++)
                    {
                        float wx = -DoorWidthIn * 0.5f + (DoorWidthIn * (w + 0.5f) / 4f);
                        Box(door, $"Window_{w}",
                            new Vector3(wx, y, z - 0.6f),
                            new Vector3(DoorWidthIn / 6f, panelHeight * 0.45f, 1.2f),
                            WorkshopMaterials.Steel, 12f, 12f);
                    }
                }
            }

            // Track and torsion tube above the opening.
            Box(door, "TorsionTube",
                new Vector3(0f, DoorHeightIn + 4f, z + 2f),
                new Vector3(DoorWidthIn + 12f, 3f, 3f),
                WorkshopMaterials.Steel, 24f, 3f);
        }

        // ------------------------------------------------------------------
        // Benches
        // ------------------------------------------------------------------

        private static void BuildBackBench(GameObject root)
        {
            var bench = new GameObject("BackBench");
            bench.transform.SetParent(root.transform, false);

            float z = (RoomDepthIn * 0.5f) - (BenchDepthIn * 0.5f) - 3f;
            const float lengthIn = 168f;

            Box(bench, "Counter",
                new Vector3(0f, BenchHeightIn - (BenchTopThicknessIn * 0.5f), z),
                new Vector3(lengthIn, BenchTopThicknessIn, BenchDepthIn),
                WorkshopMaterials.BenchWood, lengthIn, BenchDepthIn);

            // Cabinet run beneath, with drawer faces.
            const int cabinets = 4;
            float cabinetWidth = lengthIn / cabinets;
            float cabinetHeight = BenchHeightIn - BenchTopThicknessIn;

            for (int i = 0; i < cabinets; i++)
            {
                float x = -lengthIn * 0.5f + (cabinetWidth * (i + 0.5f));

                Box(bench, $"Cabinet_{i}",
                    new Vector3(x, cabinetHeight * 0.5f, z + 1f),
                    new Vector3(cabinetWidth - 1f, cabinetHeight, BenchDepthIn - 2f),
                    WorkshopMaterials.CabinetBlue, cabinetWidth, cabinetHeight);

                for (int d = 0; d < 3; d++)
                {
                    float dy = (cabinetHeight / 3f * (d + 0.5f));
                    Box(bench, $"DrawerFace_{i}_{d}",
                        new Vector3(x, dy, z - (BenchDepthIn * 0.5f) + 0.6f),
                        new Vector3(cabinetWidth - 3f, (cabinetHeight / 3f) - 1.2f, 1.2f),
                        WorkshopMaterials.CabinetBlue, cabinetWidth, cabinetHeight / 3f);

                    Box(bench, $"Handle_{i}_{d}",
                        new Vector3(x, dy, z - (BenchDepthIn * 0.5f) - 0.4f),
                        new Vector3(cabinetWidth * 0.4f, 0.8f, 0.8f),
                        WorkshopMaterials.Steel, 6f, 1f);
                }
            }

            BuildPegboard(bench, z);
            BuildWallButtons(bench);
        }

        /// <summary>
        /// The two destructive buttons, beside the tool board.
        ///
        /// Physical controls on the wall rather than menu entries: they are
        /// workshop actions, and a control that exists in the room can be
        /// reached by hand in VR later without being redesigned as a panel.
        /// </summary>
        private static void BuildWallButtons(GameObject parent)
        {
            // Left-hand wall, clear of the benches. Facing into the room, so
            // the outward normal is +X.
            float x = -(RoomWidthIn * 0.5f) + 3.2f;
            const float z = 46f;
            var outward = Vector3.right;

            BuildConfirmButton(parent, new Vector3(x, 58f, z), outward,
                ConfirmButton.Target.FloorParts,
                "CLEAR FLOOR",
                "Delete parts on the floor?",
                new Color(0.85f, 0.62f, 0.15f));

            // Red, larger consequence, placed lower so it is not the one
            // reached for by habit.
            BuildConfirmButton(parent, new Vector3(x, 40f, z), outward,
                ConfirmButton.Target.AllParts,
                "DELETE ALL",
                "Delete EVERY part?",
                new Color(0.72f, 0.12f, 0.10f));
        }

        /// <summary>
        /// Builds a labelled confirm button on a wall.
        ///
        /// <paramref name="outward"/> is the wall's normal into the room, which
        /// is all that is needed to orient every piece - so the same builder
        /// works on any wall without a special case per surface.
        /// </summary>
        private static void BuildConfirmButton(
            GameObject parent, Vector3 positionIn, Vector3 outward,
            ConfirmButton.Target target,
            string idleLabel, string confirmLabel, Color faceColour)
        {
            // Round, like a real workshop stop button. Diameter is set by the
            // longest label that has to read across the room; the confirmation
            // question is longer still and auto-sizes down.
            const float diameterIn = 26f;

            var mount = new GameObject($"Button_{target}");
            mount.transform.SetParent(parent.transform, false);

            Vector3 centre = new Vector3(In(positionIn.x), In(positionIn.y), In(positionIn.z));

            // Unity's cylinder runs along its local Y and is two units tall, so
            // aligning Y with the wall normal points the flat faces into the
            // room, and thickness scales are halved.
            Quaternion facing = Quaternion.FromToRotation(Vector3.up, outward);
            float radius = In(diameterIn) * 0.5f;

            GameObject plate = Cylinder(mount, "Plate", centre, facing,
                radius + In(1.6f), In(0.6f), WorkshopMaterials.Steel.Material);

            // Frame is decoration; leaving its collider live would let the user
            // aim at the surround and wonder why nothing happened.
            plate.GetComponent<Collider>().enabled = false;

            GameObject face = Cylinder(mount, "Button", centre + (outward * In(1.1f)),
                facing, radius, In(1.1f),
                WorkshopMaterials.CreateButtonFace(target.ToString(), faceColour));

            face.isStatic = false;
            face.AddComponent<Highlightable>();
            var button = face.AddComponent<ConfirmButton>();
            button.Configure(target, idleLabel, confirmLabel);

            Vector3 frontOfFace = centre + (outward * In(2.4f));

            // TextMeshPro reads correctly when its forward points the same way
            // the reader is looking - that is, *into* the wall. Facing it out
            // of the wall renders it back-to-front, which is what made the
            // first version come out mirrored.
            Quaternion readable = Quaternion.LookRotation(-outward, Vector3.up);

            // The countdown disc, in its own anchor scaled to the button so it
            // can be driven in 0..1 without knowing the real diameter.
            //
            // Behind the label and very thin. The first version was a
            // centimetre thick and sat proud of the face, so it swept across
            // the text and hid the question it was counting down for.
            var barAnchor = new GameObject("FillAnchor");
            barAnchor.transform.SetParent(mount.transform, false);
            barAnchor.transform.position = frontOfFace - (outward * In(0.35f));
            barAnchor.transform.rotation = facing;
            barAnchor.transform.localScale = new Vector3(radius * 2f, In(0.06f), radius * 2f);

            var bar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bar.name = "GreyBar";
            bar.transform.SetParent(barAnchor.transform, false);
            bar.transform.localPosition = Vector3.zero;
            bar.transform.localScale = Vector3.one;
            Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.GetComponent<MeshRenderer>().sharedMaterial =
                WorkshopMaterials.CreateTransparentFill();

            // The label is NOT parented to the scaled anchor. A non-uniform
            // parent scale stretches glyphs, which is why the first version was
            // both squashed and hard to read.
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(mount.transform, false);
            labelGo.transform.position = frontOfFace;
            labelGo.transform.rotation = readable;

            var text = labelGo.AddComponent<TMPro.TextMeshPro>();
            text.text = idleLabel;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.textWrappingMode = TMPro.TextWrappingModes.Normal;
            text.color = Color.white;

            // World units, so this is a real physical letter height. Auto-size
            // lets the long confirmation question shrink to fit while the short
            // idle label stays large.
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.07f;
            text.fontSizeMax = 0.30f;
            text.fontSize = 0.30f;

            var rect = labelGo.GetComponent<RectTransform>();
            if (rect != null)
            {
                // Inscribed square inside the circle, less a small margin.
                // Anything larger would spill past the button's edge.
                float inscribed = radius * 1.38f;
                rect.sizeDelta = new Vector2(inscribed, inscribed);
            }

            button.Bind(text, bar.transform);
        }

        /// <summary>
        /// Disc facing along <paramref name="facing"/>'s up axis. Unity's
        /// cylinder is two units tall, so the thickness scale is halved.
        /// </summary>
        private static GameObject Cylinder(
            GameObject parent, string name, Vector3 centre, Quaternion facing,
            float radius, float thickness, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = centre;
            go.transform.rotation = facing;
            go.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
            go.isStatic = true;

            if (material != null)
            {
                go.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            return go;
        }

        /// <summary>Box placed by world position and world-axis size.</summary>
        private static GameObject BoxWorld(
            GameObject parent, string name, Vector3 centre, Vector3 size,
            WorkshopMaterials.Surface surface, float tileWidthIn, float tileHeightIn)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = centre;
            go.transform.localScale = size;
            go.isStatic = true;

            if (surface?.Material != null)
            {
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = surface.Material;

                Vector2 tiling = surface.TilingFor(tileWidthIn, tileHeightIn);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, 0f, 0f));
                renderer.SetPropertyBlock(block);
            }

            return go;
        }

        private static void BuildPegboard(GameObject parent, float benchZ)
        {
            const float boardWidth = 96f;
            const float boardHeight = 42f;
            float boardBottom = BenchHeightIn + 10f;
            float z = (RoomDepthIn * 0.5f) - 3.4f;

            Box(parent, "Pegboard",
                new Vector3(0f, boardBottom + (boardHeight * 0.5f), z),
                new Vector3(boardWidth, boardHeight, 0.75f),
                WorkshopMaterials.Pegboard, boardWidth, boardHeight);

            // Hanging tools, as simple silhouettes. At the distance these are
            // ever seen, shape and spacing carry the read - detail would be
            // invisible and expensive.
            var random = new System.Random(4242);
            for (int i = 0; i < 14; i++)
            {
                float x = -boardWidth * 0.42f + (boardWidth * 0.84f * i / 13f);
                float length = 6f + (float)random.NextDouble() * 8f;
                float y = boardBottom + boardHeight - 6f - (float)random.NextDouble() * 4f;

                Box(parent, $"Tool_{i}_Handle",
                    new Vector3(x, y - (length * 0.5f), z - 1.2f),
                    new Vector3(1.1f, length, 1.1f),
                    i % 3 == 0 ? WorkshopMaterials.Steel : WorkshopMaterials.CabinetBlue,
                    2f, length);

                Box(parent, $"Tool_{i}_Head",
                    new Vector3(x, y, z - 1.2f),
                    new Vector3(3.4f, 1.8f, 1.6f),
                    WorkshopMaterials.Steel, 4f, 2f);
            }

            // Open shelving above the pegboard.
            for (int s = 0; s < 2; s++)
            {
                float y = boardBottom + boardHeight + 8f + (s * 12f);
                Box(parent, $"Shelf_{s}",
                    new Vector3(-40f, y, z - 3f),
                    new Vector3(56f, 1.2f, 8f),
                    WorkshopMaterials.BenchWood, 56f, 8f);

                for (int c = 0; c < 4; c++)
                {
                    Box(parent, $"Can_{s}_{c}",
                        new Vector3(-62f + (c * 12f), y + 3.2f, z - 3f),
                        new Vector3(4f, 5.5f, 4f),
                        c % 2 == 0 ? WorkshopMaterials.Steel : WorkshopMaterials.CabinetBlue,
                        4f, 5.5f);
                }
            }
        }

        private static void BuildSideBench(GameObject root)
        {
            var bench = new GameObject("SideBench");
            bench.transform.SetParent(root.transform, false);

            float x = (RoomWidthIn * 0.5f) - (BenchDepthIn * 0.5f) - 3f;
            const float lengthIn = 150f;
            float zCentre = 20f;

            Box(bench, "Counter",
                new Vector3(x, BenchHeightIn - (BenchTopThicknessIn * 0.5f), zCentre),
                new Vector3(BenchDepthIn, BenchTopThicknessIn, lengthIn),
                WorkshopMaterials.BenchWood, BenchDepthIn, lengthIn);

            // Open shelf instead of cabinets, matching the reference.
            Box(bench, "LowerShelf",
                new Vector3(x, 12f, zCentre),
                new Vector3(BenchDepthIn - 3f, 1.2f, lengthIn - 4f),
                WorkshopMaterials.BenchWood, BenchDepthIn, lengthIn);

            for (int i = 0; i < 4; i++)
            {
                float z = zCentre - (lengthIn * 0.5f) + (lengthIn * (i + 0.5f) / 4f);
                Box(bench, $"Leg_{i}",
                    new Vector3(x, BenchHeightIn * 0.5f, z),
                    new Vector3(2.5f, BenchHeightIn, 2.5f),
                    WorkshopMaterials.Steel, 3f, BenchHeightIn);

                Box(bench, $"StorageBox_{i}",
                    new Vector3(x, 16f, z),
                    new Vector3(14f, 7f, 16f),
                    i % 2 == 0 ? WorkshopMaterials.CabinetBlue : WorkshopMaterials.BenchWood,
                    14f, 7f);
            }
        }

        private static void BuildClutter(GameObject root)
        {
            var clutter = new GameObject("Clutter");
            clutter.transform.SetParent(root.transform, false);

            // Cardboard boxes stacked in the far corner.
            Box(clutter, "Carton_A",
                new Vector3(-96f, 9f, -76f), new Vector3(20f, 18f, 18f),
                WorkshopMaterials.BenchWood, 20f, 18f);

            Box(clutter, "Carton_B",
                new Vector3(-96f, 24f, -78f), new Vector3(16f, 12f, 14f),
                WorkshopMaterials.BenchWood, 16f, 12f);

            Box(clutter, "Carton_C",
                new Vector3(-100f, 7f, -50f), new Vector3(14f, 14f, 14f),
                WorkshopMaterials.BenchWood, 14f, 14f);

            // Structural post, as the reference has.
            Box(clutter, "Post",
                new Vector3(72f, RoomHeightIn * 0.5f, 96f),
                new Vector3(5.5f, RoomHeightIn, 5.5f),
                WorkshopMaterials.Steel, 6f, RoomHeightIn);
        }

        // ------------------------------------------------------------------
        // Lighting
        // ------------------------------------------------------------------

        private static void BuildLighting(GameObject root)
        {
            var lighting = new GameObject("Lighting");
            lighting.transform.SetParent(root.transform, false);

            Material tube = WorkshopMaterials.CreateLightTube();

            // Three fixtures across the ceiling. Real fluorescent tubes are
            // 48 in; spacing them at 80 in gives the even, slightly cold fill
            // a garage actually has.
            var positions = new[]
            {
                new Vector3(0f, RoomHeightIn - 8f, 70f),
                new Vector3(0f, RoomHeightIn - 8f, 0f),
                new Vector3(-60f, RoomHeightIn - 8f, -70f),
            };

            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];

                GameObject housing = Box(lighting, $"Fixture_{i}_Housing",
                    p + new Vector3(0f, 2.5f, 0f),
                    new Vector3(52f, 3f, 8f),
                    WorkshopMaterials.PaintedMetal, 52f, 8f);
                housing.isStatic = true;

                GameObject glass = Box(lighting, $"Fixture_{i}_Tube",
                    p, new Vector3(48f, 1.6f, 5f), null, 1f, 1f);
                glass.GetComponent<MeshRenderer>().sharedMaterial = tube;
                glass.GetComponent<Collider>().enabled = false;

                var lightGo = new GameObject($"Fixture_{i}_Light");
                lightGo.transform.SetParent(lighting.transform, false);
                lightGo.transform.position = new Vector3(In(p.x), In(p.y - 2f), In(p.z));

                Light light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = In(260f);

                // Slightly cool and green-leaning, which is what fluorescent
                // tubes actually look like. A neutral white reads as studio
                // lighting rather than a garage.
                light.color = new Color(0.93f, 0.97f, 1f);
                light.intensity = 5.2f;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.75f;
            }

            // Weak ambient fill. Without any, the undersides of parts on the
            // bench go completely black and become unreadable - which matters
            // when the user is lining up screw holes.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.20f, 0.21f, 0.24f);
            RenderSettings.ambientEquatorColor = new Color(0.15f, 0.15f, 0.17f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.09f);
            RenderSettings.fog = false;
        }

        // ------------------------------------------------------------------
        // Helper
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a box in inches, with texture tiling set per object.
        ///
        /// Tiling goes through a MaterialPropertyBlock rather than a material
        /// variant, because otherwise every differently-sized surface would
        /// need its own material asset - dozens of them, all identical except
        /// for two numbers.
        /// </summary>
        private static GameObject Box(
            GameObject parent, string name, Vector3 centreIn, Vector3 sizeIn,
            WorkshopMaterials.Surface surface, float tileWidthIn, float tileHeightIn)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = new Vector3(In(centreIn.x), In(centreIn.y), In(centreIn.z));
            go.transform.localScale = new Vector3(In(sizeIn.x), In(sizeIn.y), In(sizeIn.z));
            go.isStatic = true;

            if (surface?.Material != null)
            {
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = surface.Material;

                Vector2 tiling = surface.TilingFor(tileWidthIn, tileHeightIn);
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);

                // _BaseMap_ST is (scaleX, scaleY, offsetX, offsetY). URP's Lit
                // shader transforms the normal map with the same value, so one
                // property covers both.
                block.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, 0f, 0f));
                renderer.SetPropertyBlock(block);
            }

            return go;
        }
    }
}
