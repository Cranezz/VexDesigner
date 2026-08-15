namespace VexDesigner.EditorTools
{
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using TMPro;
    using UnityEngine.UI;
    using VexDesigner.InputSources;
    using VexDesigner.Parts;
    using VexDesigner.Player;
    using VexDesigner.UI;

    /// <summary>
    /// Builds the whole playable scene from code: the garage, the workbench,
    /// the parts shelf, the player, and the on-screen crosshair.
    ///
    /// Generating the scene rather than hand-placing it means the layout is
    /// reviewable in a diff, reproducible after a bad merge, and self-
    /// documenting about why things are the size they are. Unity scene files
    /// are YAML blobs nobody can read, so a hand-built scene is effectively
    /// undocumented.
    ///
    /// UNITS. Everything here is authored in inches, because that is what VEX
    /// parts and building rules are specified in. Inches convert to metres
    /// exactly once, at point of use, via <see cref="In"/>.
    ///
    /// World space stays 1 unit = 1 metre. Unity's physics and every OpenXR
    /// runtime assume it, and a project built at 1 unit = 1 inch has hands
    /// forty times the wrong size the moment a headset is attached.
    /// </summary>
    public static class WorkshopSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string TexturesFolder = "Assets/Textures";
        private const string ScenePath = ScenesFolder + "/Workshop.unity";

        private const float InchesToMetres = 0.0254f;

        // --- Build area -------------------------------------------------
        // VEX games size robots at 18 in cubed, expanding to 24 in in most
        // seasons. The mat is larger than the expanded limit in both
        // directions so a fully expanded robot still has working room.
        private const float MatWidthIn = 36f;
        private const float MatDepthIn = 30f;
        private const float MatThicknessIn = 0.12f;

        // --- Work table -------------------------------------------------
        private const float TableWidthIn = 72f;
        private const float TableDepthIn = 36f;
        private const float TableHeightIn = 36f;
        private const float TableTopThicknessIn = 1.5f;
        private const float LegThicknessIn = 3f;
        private const float LegInsetIn = 4f;

        // Table sits toward the middle of the garage, clear of both benches.
        private const float TableCentreZIn = -10f;

        // --- Parts shelf ------------------------------------------------
        // Deeper than wide on purpose: a 35-hole C-channel is 17.5 in long and
        // has to lie down inside the region, so its long axis runs front to
        // back where there is room.
        private const float ShelfWidthIn = 16f;
        private const float ShelfDepthIn = 30f;
        private static float ShelfCentreXIn => -(TableWidthIn * 0.5f) - 12f;

        private static float In(float inches) => inches * InchesToMetres;

        // --- Table saw --------------------------------------------------
        // Far enough along the bench to be clear of the mat, and far enough
        // back from the end that its nine-inch bed sits on the wood rather
        // than over the edge.
        private const float SawCentreXIn = 28f;

        [MenuItem("VexDesigner/Rebuild Workshop Scene")]
        public static void BuildMenuItem()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild Workshop Scene",
                    "This replaces Assets/Scenes/Workshop.unity with a freshly " +
                    "generated scene.\n\nAny hand edits to that scene will be lost.",
                    "Rebuild",
                    "Cancel"))
            {
                return;
            }

            Build();
            EditorSceneManager.OpenScene(ScenePath);
        }

        /// <summary>
        /// Entry point for headless invocation:
        /// Unity.exe -batchmode -quit -projectPath &lt;path&gt;
        ///           -executeMethod VexDesigner.EditorTools.WorkshopSceneBuilder.BuildFromCommandLine
        /// </summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                Debug.Log("[WorkshopSceneBuilder] Scene built successfully.");
            }
            catch (System.Exception e)
            {
                // In batch mode an uncaught exception still exits 0, which
                // would let a broken build pass for a good one.
                Debug.LogError($"[WorkshopSceneBuilder] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Build()
        {
            EnsureFolder(ScenesFolder);

            // The scene is meaningless without its surfaces, so generate them
            // if missing rather than silently producing untextured boxes.
            if (SurfaceTextureGenerator.NeedsGeneration())
            {
                Debug.Log("[WorkshopSceneBuilder] Textures missing; generating them first.");
                SurfaceTextureGenerator.Generate();
            }

            // Definitions must exist before the shelf can list them. The shelf
            // loads them at runtime from Resources, so a part converted later
            // appears without touching this scene.
            PartLibraryBuilder.Rebuild();

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GarageRoomBuilder.Build();

            BuildWorkTable();
            BuildMat();
            BuildShelf();
            BuildSaw();
            BuildPlayer();
            BuildInterface();
            BuildMeasurementDisplay();

            VerifyShaders();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[WorkshopSceneBuilder] Garage {GarageRoomBuilder.RoomWidthIn}x" +
                $"{GarageRoomBuilder.RoomDepthIn}x{GarageRoomBuilder.RoomHeightIn} in. " +
                $"Table {TableWidthIn}x{TableDepthIn}x{TableHeightIn} in, " +
                $"mat {MatWidthIn}x{MatDepthIn} in with a 1 in grid.");
        }

        // ------------------------------------------------------------------
        // Workbench
        // ------------------------------------------------------------------

        private static void BuildWorkTable()
        {
            var root = new GameObject("WorkTable");
            root.isStatic = true;

            float topCentreY = TableHeightIn - (TableTopThicknessIn * 0.5f);

            GameObject top = Box(root, "TableTop",
                new Vector3(0f, topCentreY, TableCentreZIn),
                new Vector3(TableWidthIn, TableTopThicknessIn, TableDepthIn),
                WorkshopMaterials.BenchWood, TableWidthIn, TableDepthIn);

            // The bare tabletop is a valid surface too, so parts set down
            // beside the mat land on the bench rather than refusing to place.
            top.AddComponent<PlacementSurface>();

            float legHeightIn = TableHeightIn - TableTopThicknessIn;
            float x = (TableWidthIn * 0.5f) - LegInsetIn - (LegThicknessIn * 0.5f);
            float z = (TableDepthIn * 0.5f) - LegInsetIn - (LegThicknessIn * 0.5f);

            var offsets = new[]
            {
                new Vector2(+x, +z), new Vector2(-x, +z),
                new Vector2(+x, -z), new Vector2(-x, -z),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                Box(root, $"Leg_{i}",
                    new Vector3(offsets[i].x, legHeightIn * 0.5f, TableCentreZIn + offsets[i].y),
                    new Vector3(LegThicknessIn, legHeightIn, LegThicknessIn),
                    WorkshopMaterials.BenchWood, LegThicknessIn, legHeightIn);
            }
        }

        private static void BuildMat()
        {
            var root = new GameObject("MatRoot");

            GameObject mat = Box(root, "CuttingMat",
                new Vector3(0f, TableHeightIn + (MatThicknessIn * 0.5f), TableCentreZIn),
                new Vector3(MatWidthIn, MatThicknessIn, MatDepthIn),
                WorkshopMaterials.CuttingMat, MatWidthIn, MatDepthIn);

            // Parts may be set down here. The mat is the primary work surface,
            // and its one-inch grid doubles as the scene's ruler.
            mat.AddComponent<PlacementSurface>();
        }

        private static void BuildShelf()
        {
            var root = new GameObject("PartsShelf");
            root.transform.position =
                new Vector3(In(ShelfCentreXIn), In(TableHeightIn), In(TableCentreZIn));

            var shelf = root.AddComponent<PartShelf>();

            var so = new SerializedObject(shelf);
            so.FindProperty("regionWidthIn").floatValue = ShelfWidthIn;
            so.FindProperty("regionDepthIn").floatValue = ShelfDepthIn;
            so.FindProperty("partMaterial").objectReferenceValue =
                WorkshopMaterials.Aluminium.Material;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A separate stand under the shelf region, so parts are not
            // floating beside the main table.
            var stand = new GameObject("ShelfStand");
            stand.isStatic = true;
            Box(stand, "StandTop",
                new Vector3(ShelfCentreXIn, TableHeightIn - 0.75f, TableCentreZIn),
                new Vector3(ShelfWidthIn + 3f, 1.5f, ShelfDepthIn + 3f),
                WorkshopMaterials.BenchWood, ShelfWidthIn, ShelfDepthIn);

            for (int i = 0; i < 4; i++)
            {
                float sx = ShelfCentreXIn + ((i % 2 == 0 ? 1 : -1) * (ShelfWidthIn * 0.4f));
                float sz = TableCentreZIn + ((i < 2 ? 1 : -1) * (ShelfDepthIn * 0.4f));
                Box(stand, $"StandLeg_{i}",
                    new Vector3(sx, (TableHeightIn - 1.5f) * 0.5f, sz),
                    new Vector3(2.5f, TableHeightIn - 1.5f, 2.5f),
                    WorkshopMaterials.Steel, 3f, TableHeightIn);
            }

            float controlsZ = TableCentreZIn + (ShelfDepthIn * 0.5f) + 2.5f;
            BuildPageArrow(root, shelf, -1, new Vector3(ShelfCentreXIn - 4f, TableHeightIn, controlsZ));
            BuildPageArrow(root, shelf, +1, new Vector3(ShelfCentreXIn + 4f, TableHeightIn, controlsZ));
            BuildPageLabel(root, shelf, new Vector3(ShelfCentreXIn, TableHeightIn + 0.1f, controlsZ));
        }

        private static void BuildPageArrow(
            GameObject parent, PartShelf shelf, int direction, Vector3 positionIn)
        {
            var go = new GameObject(direction < 0 ? "PageArrow_Prev" : "PageArrow_Next");
            go.transform.SetParent(parent.transform, true);
            go.transform.position = new Vector3(In(positionIn.x), In(positionIn.y), In(positionIn.z));
            go.transform.rotation = Quaternion.Euler(0f, direction < 0 ? 180f : 0f, 0f);

            go.AddComponent<MeshFilter>().sharedMesh = BuildArrowMesh();
            go.AddComponent<MeshRenderer>().sharedMaterial = WorkshopMaterials.Steel.Material;

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(In(3f), In(1.5f), In(3f));
            box.center = new Vector3(0f, In(0.5f), 0f);

            go.AddComponent<Highlightable>();
            go.AddComponent<ShelfPageArrow>().Configure(shelf, direction);
        }

        /// <summary>
        /// A flat chevron lying on the bench. Built from code because it is
        /// three vertices, and pulling in an art asset for that would be
        /// sillier than the ten lines below.
        /// </summary>
        private static Mesh BuildArrowMesh()
        {
            float halfLength = In(1.2f);
            float halfWidth = In(1.2f);
            float lift = In(0.06f);

            var mesh = new Mesh { name = "PageArrow" };
            mesh.vertices = new[]
            {
                new Vector3(halfLength, lift, 0f),
                new Vector3(-halfLength * 0.6f, lift, halfWidth),
                new Vector3(-halfLength * 0.6f, lift, -halfWidth),
            };

            // Wound both ways so it is visible from below as well as above.
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildPageLabel(GameObject parent, PartShelf shelf, Vector3 positionIn)
        {
            var go = new GameObject("PageLabel");
            go.transform.SetParent(parent.transform, true);
            go.transform.position = new Vector3(In(positionIn.x), In(positionIn.y), In(positionIn.z));

            // Lie flat on the bench, reading toward the near edge.
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            var text = go.AddComponent<TMPro.TextMeshPro>();
            text.text = "1/1";

            // World-space TMP sizes in world units, so this is a physical
            // height on the bench, not a screen size. Roughly 1 inch tall.
            text.fontSize = 0.32f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.92f, 0.93f, 0.95f);

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(In(6f), In(2f));
            }

            shelf.AttachLabel(text);
        }

        // ------------------------------------------------------------------
        // Player
        // ------------------------------------------------------------------

        /// <summary>
        /// Puts the saw on the right-hand end of the bench.
        ///
        /// At the end rather than in the middle for the reason a real one is:
        /// stock has to hang off both sides of the blade, and a 35-hole
        /// C-channel is nearly a foot and a half long. The end of the bench
        /// gives it somewhere to hang.
        /// </summary>
        private static void BuildSaw()
        {
            float top = In(TableHeightIn);

            // Standing on the right-hand end of the bench and facing right, so
            // the player walks to the end of the bench to use it and the stock
            // runs front to back with room to overhang at both ends. Laid along
            // the bench instead, the machine sat in the middle of the working
            // area and the mat had nowhere to go.
            GameObject saw = SawStationBuilder.Build(
                new Vector3(SawCentreXIn * InchesToMetres, top, In(TableCentreZIn)),
                Quaternion.Euler(0f, -90f, 0f));

            // Reported because the machine's footprint has to land on the
            // bench, and a saw hanging off the end is not visible from a
            // headless build.
            Bounds footprint = default;
            bool first = true;

            foreach (Renderer renderer in saw.GetComponentsInChildren<Renderer>())
            {
                if (first) { footprint = renderer.bounds; first = false; }
                else { footprint.Encapsulate(renderer.bounds); }
            }

            Debug.Log(
                $"[WorkshopSceneBuilder] Saw at {SawCentreXIn:0.#} in along the bench, " +
                $"spanning x {footprint.min.x / InchesToMetres:0.#} to " +
                $"{footprint.max.x / InchesToMetres:0.#} in, z " +
                $"{footprint.min.z / InchesToMetres:0.#} to " +
                $"{footprint.max.z / InchesToMetres:0.#} in. Bench is x " +
                $"{-TableWidthIn * 0.5f:0.#} to {TableWidthIn * 0.5f:0.#}, z " +
                $"{TableCentreZIn - TableDepthIn * 0.5f:0.#} to " +
                $"{TableCentreZIn + TableDepthIn * 0.5f:0.#}.");
        }

        private static void BuildPlayer()
        {
            // Global physics settings, applied before anything spawns. Unity's
            // defaults assume character-scale objects; VEX parts are one to two
            // orders of magnitude smaller. See PhysicsTuning.
            new GameObject("PhysicsTuning").AddComponent<PhysicsTuning>();

            var player = new GameObject("Player");
            player.transform.position = GarageRoomBuilder.PlayerSpawnPosition;
            player.transform.rotation = Quaternion.Euler(0f, GarageRoomBuilder.PlayerSpawnYaw, 0f);

            var controller = player.AddComponent<CharacterController>();
            // Deliberately much smaller than a person.
            //
            // An 18-inch-wide capsule keeps the player a hand's breadth away
            // from everything, which is exactly the wrong distance for work
            // done at arm's length on quarter-inch holes - you cannot get your
            // eye near the join you are trying to judge. A narrow body lets the
            // player stand right against the bench.
            //
            // Nothing here needs a realistic collider: there is one character,
            // no combat, and nothing that has to squeeze through a doorway.
            controller.height = In(60f);
            controller.radius = In(2f);
            controller.center = new Vector3(0f, In(34f), 0f);

            // Generous step and slope so the player is not stopped by the
            // small lips and thresholds a built environment is full of.
            controller.stepOffset = In(8f);
            controller.slopeLimit = 50f;
            controller.skinWidth = In(0.4f);

            // Head is a child. Yaw turns the body, pitch turns the head - which
            // is what lets VR take over the camera transform later without the
            // two fighting.
            var head = new GameObject("Head");
            head.transform.SetParent(player.transform, false);
            head.transform.localPosition = new Vector3(0f, In(66f), 0f);

            Camera cam = head.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.fieldOfView = 70f;

            // Tight near plane because the player leans in close to small
            // parts; the 0.3 default would clip a screw out of view.
            cam.nearClipPlane = In(0.8f);
            cam.farClipPlane = In(2000f);
            head.AddComponent<AudioListener>();

            var input = player.AddComponent<FirstPersonInput>();
            var inputSo = new SerializedObject(input);
            inputSo.FindProperty("aimCamera").objectReferenceValue = cam;
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            var fps = player.AddComponent<FirstPersonController>();
            var fpsSo = new SerializedObject(fps);
            fpsSo.FindProperty("head").objectReferenceValue = head.transform;
            fpsSo.ApplyModifiedPropertiesWithoutUndo();

            // Footsteps live on their own child so the AudioSource is not
            // competing with anything else on the player.
            var steps = new GameObject("Footsteps");
            steps.transform.SetParent(player.transform, false);
            steps.AddComponent<AudioSource>();
            steps.AddComponent<FootstepAudio>();

            // Placement needs the pointer, which needs the camera, so all
            // three live together.
            player.AddComponent<InteractionLock>();
            player.AddComponent<PartPlacementController>();

            // The precision alternative to grabbing. Only one of the two is
            // enabled at a time - they cannot share the primary click - and G
            // swaps between them.
            player.AddComponent<TransformToolController>();
            player.AddComponent<SawController>();

            // Spawn point is baked in rather than read back from the room
            // builder at runtime, because that builder is editor-only code and
            // does not exist in a real build.
            player.AddComponent<PlayerSpawn>().Configure(
                GarageRoomBuilder.PlayerSpawnPosition, GarageRoomBuilder.PlayerSpawnYaw);
        }

        /// <summary>
        /// The trail and distance readout shown while dragging a part with the
        /// transform tool.
        ///
        /// World space rather than screen space: the line has to sit in the
        /// scene along the actual path travelled, and the label follows it.
        /// </summary>
        private static void BuildMeasurementDisplay()
        {
            var root = new GameObject("MeasurementDisplay");

            var lineGo = new GameObject("Trail");
            lineGo.transform.SetParent(root.transform, false);

            var line = lineGo.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = In(0.12f);
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;

            // Same overlay shader as the gizmo, so the trail stays visible when
            // it passes behind the bench - which is exactly when knowing how far
            // something has moved matters most.
            Shader overlay = Shader.Find("VexDesigner/GizmoOverlay");
            if (overlay != null)
            {
                var mat = new Material(overlay) { name = "MeasurementTrail" };
                mat.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.02f));
                line.material = mat;
            }

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(root.transform, false);

            var text = labelGo.AddComponent<TMPro.TextMeshPro>();
            text.text = "0\"";

            // World-space TMP sizes in world units. The shelf's page label sits
            // at 0.32 and reads about an inch tall, so this is roughly two and
            // a half inches at the reference distance - which is what a floating
            // measurement needs to be legible while a part is being dragged.
            //
            // The previous 0.11 worked out at about a third of an inch, which
            // is why it read as microscopic.
            text.fontSize = 0.8f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = Color.black;
            text.fontStyle = TMPro.FontStyles.Bold;
            text.enabled = false;

            // Black text with a white outline. Black alone disappears against
            // the mat and into shadow; the outline is what keeps it readable
            // over every surface in the workshop without a background panel.
            text.fontMaterial.EnableKeyword("OUTLINE_ON");
            text.outlineColor = new Color32(255, 255, 255, 255);
            text.outlineWidth = 0.14f;

            // Drawn over geometry, like the trail. A measurement hidden behind
            // the bench is missing at precisely the moment it is being read.
            if (text.fontMaterial.HasProperty("_ZTestMode"))
            {
                text.fontMaterial.SetFloat(
                    "_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            }

            var rect = labelGo.GetComponent<RectTransform>();
            if (rect != null)
            {
                // Generous, so a long reading like 11' 2 1/2" never wraps or
                // clips. An over-large rect costs nothing; a tight one silently
                // truncates the number.
                rect.sizeDelta = new Vector2(1.6f, 0.3f);
            }

            var display = root.AddComponent<MeasurementDisplay>();
            var so = new SerializedObject(display);
            so.FindProperty("line").objectReferenceValue = line;
            so.FindProperty("label").objectReferenceValue = text;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Interface
        // ------------------------------------------------------------------

        private static void BuildInterface()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();

            GameObject dot = CreateHudImage(canvasGo.transform, "Dot", 10f);
            dot.GetComponent<Image>().sprite = Crosshair.GetDotSprite();

            // A badge below the aim point, not a replacement for it. Small
            // enough to read as an annotation on the crosshair and far enough
            // down to leave the hole being aimed at completely clear.
            GameObject padlock = CreateHudImage(canvasGo.transform, "Padlock", 16f);
            padlock.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -18f);
            padlock.GetComponent<Image>().sprite = Crosshair.GetPadlockSprite();
            padlock.GetComponent<Image>().color = new Color(0.62f, 0.8f, 1f, 0.9f);
            padlock.GetComponent<Image>().enabled = false;

            GameObject prompt = BuildUsePrompt(canvasGo.transform);

            BuildKeybindHints(canvasGo.transform);
            BuildMessageBanner(canvasGo.transform);
            BuildDeletionPreview(canvasGo.transform);
            PauseMenuBuilder.Build(canvasGo.transform);

            var crosshair = canvasGo.AddComponent<Crosshair>();
            var so = new SerializedObject(crosshair);
            so.FindProperty("dot").objectReferenceValue = dot.GetComponent<Image>();
            so.FindProperty("padlock").objectReferenceValue = padlock.GetComponent<Image>();
            so.FindProperty("usePrompt").objectReferenceValue =
                prompt.GetComponent<TMPro.TextMeshProUGUI>();
            so.ApplyModifiedPropertiesWithoutUndo();

            BuildSawPanel(canvasGo.transform);

            // An EventSystem is required for any UI interaction. Without one
            // the pause menu's buttons would render but never respond.
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<UnityEngine.EventSystems.EventSystem>();
                events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        /// <summary>
        /// Corner viewport showing the parts a destructive button will remove.
        ///
        /// Rendered by a second camera into a RenderTexture. Moving the
        /// player's own view instead would be disorienting and would lose their
        /// place in the workshop.
        /// </summary>
        private static void BuildDeletionPreview(Transform parent)
        {
            // Saved as an asset, not created in memory. A RenderTexture made at
            // build time and referenced by a saved scene would be a dangling
            // reference the moment the editor reloads.
            const string texturePath = TexturesFolder + "/DeletionPreviewRT.renderTexture";
            var texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(texturePath);

            if (texture == null)
            {
                texture = new RenderTexture(480, 320, 24) { name = "DeletionPreviewRT" };
                AssetDatabase.CreateAsset(texture, texturePath);
            }

            var camGo = new GameObject("PreviewCamera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.targetTexture = texture;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            cam.fieldOfView = 45f;
            cam.enabled = false;

            // Depth below the player's camera so it can never take over the
            // main view if it is accidentally left enabled.
            cam.depth = -10;

            var frameGo = new GameObject("PreviewFrame", typeof(RectTransform), typeof(Image));
            frameGo.transform.SetParent(parent, false);

            var frameRect = frameGo.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(1f, 1f);
            frameRect.anchorMax = new Vector2(1f, 1f);
            frameRect.pivot = new Vector2(1f, 1f);
            frameRect.anchoredPosition = new Vector2(-28f, -28f);
            frameRect.sizeDelta = new Vector2(384f, 256f);

            var frameImage = frameGo.GetComponent<Image>();
            frameImage.color = new Color(0f, 0f, 0f, 0.85f);
            frameImage.raycastTarget = false;

            var displayGo = new GameObject("PreviewImage", typeof(RectTransform), typeof(RawImage));
            displayGo.transform.SetParent(frameGo.transform, false);

            var displayRect = displayGo.GetComponent<RectTransform>();
            displayRect.anchorMin = Vector2.zero;
            displayRect.anchorMax = Vector2.one;
            displayRect.offsetMin = new Vector2(4f, 4f);
            displayRect.offsetMax = new Vector2(-4f, -4f);

            var raw = displayGo.GetComponent<RawImage>();
            raw.texture = texture;
            raw.raycastTarget = false;

            // Lives on the canvas, not on the frame it controls. A component on
            // a disabled object never runs Awake, so it would never register
            // itself and every call to show it would silently do nothing.
            var preview = parent.gameObject.AddComponent<DeletionPreview>();
            var so = new SerializedObject(preview);
            so.FindProperty("previewCamera").objectReferenceValue = cam;
            so.FindProperty("display").objectReferenceValue = raw;
            so.FindProperty("frame").objectReferenceValue = frameRect;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildMessageBanner(Transform parent)
        {
            var go = new GameObject("MessageBanner", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();

            // Top centre, below where a title bar would sit, so it reads as a
            // notification rather than part of the scene.
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -48f);
            rect.sizeDelta = new Vector2(900f, 52f);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(go.transform, false);

            var textRect = textGo.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            text.fontSize = 28f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.text = string.Empty;
            text.fontStyle = TMPro.FontStyles.Bold;

            // A black outline so the warning stays readable against the pale
            // concrete floor as well as against dark shadow. Touching
            // fontMaterial instances the material, which is what allows an
            // outline here without affecting every other label in the scene.
            text.fontMaterial.EnableKeyword("OUTLINE_ON");
            text.outlineColor = new Color32(0, 0, 0, 255);
            text.outlineWidth = 0.28f;

            var banner = go.AddComponent<MessageBanner>();
            var so = new SerializedObject(banner);
            so.FindProperty("label").objectReferenceValue = text;
            so.FindProperty("group").objectReferenceValue = go.GetComponent<CanvasGroup>();
            so.ApplyModifiedPropertiesWithoutUndo();

            go.GetComponent<CanvasGroup>().alpha = 0f;
            go.GetComponent<CanvasGroup>().blocksRaycasts = false;
        }

        private static void BuildKeybindHints(Transform parent)
        {
            var go = new GameObject("KeybindHints", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();

            // Anchored to the bottom-right corner so it stays put across
            // resolutions rather than drifting with the canvas centre.
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-28f, 24f);
            rect.sizeDelta = new Vector2(420f, 200f);

            var text = go.AddComponent<TMPro.TextMeshProUGUI>();
            text.fontSize = 20f;
            text.alignment = TMPro.TextAlignmentOptions.BottomRight;
            text.color = new Color(0.88f, 0.90f, 0.93f, 0.85f);
            text.raycastTarget = false;
            text.richText = true;

            go.AddComponent<KeybindHints>();
        }

        /// <summary>
        /// The "press E" line, just under the crosshair.
        ///
        /// Below rather than beside it, and small, because it appears while
        /// the user is aiming at something and must not sit on top of what
        /// they are aiming at.
        /// </summary>
        private static GameObject BuildUsePrompt(Transform parent)
        {
            var go = new GameObject("UsePrompt", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -42f);
            rect.sizeDelta = new Vector2(420f, 30f);

            var text = go.AddComponent<TMPro.TextMeshProUGUI>();
            text.fontSize = 20f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.95f, 0.75f);
            text.raycastTarget = false;
            text.enabled = false;

            return go;
        }

        /// <summary>
        /// The saw's readouts and keypad, down the right-hand side.
        ///
        /// Hidden until the saw is opened, and built here rather than by hand
        /// for the same reason as the rest of the scene: a panel of fifteen
        /// wired-up components is not reviewable as a scene file.
        /// </summary>
        private static void BuildSawPanel(Transform parent)
        {
            var panel = new GameObject("SawPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-24f, 0f);
            rect.sizeDelta = new Vector2(430f, 470f);

            panel.GetComponent<Image>().color = new Color(0.07f, 0.08f, 0.10f, 0.88f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 10f;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            TMPro.TextMeshProUGUI Heading(string content, float size, Color colour)
            {
                var go = new GameObject("Line", typeof(RectTransform));
                go.transform.SetParent(panel.transform, false);
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, size * 1.5f);

                var text = go.AddComponent<TMPro.TextMeshProUGUI>();
                text.text = content;
                text.fontSize = size;
                text.color = colour;
                text.raycastTarget = false;

                var element = go.AddComponent<LayoutElement>();
                element.minHeight = size * 1.5f;
                element.preferredHeight = size * 1.5f;

                return text;
            }

            Heading("TABLE SAW", 26f, new Color(1f, 0.85f, 0.3f));

            TMPro.TextMeshProUGUI stock = Heading("", 17f, new Color(0.8f, 0.85f, 0.9f));
            TMPro.TextMeshProUGUI feed = Heading("", 22f, Color.white);
            TMPro.TextMeshProUGUI blade = Heading("", 22f, Color.white);
            TMPro.TextMeshProUGUI rotation = Heading("", 18f, Color.white);

            TMP_InputField Field(string label)
            {
                var row = new GameObject(label + "Row", typeof(RectTransform));
                row.transform.SetParent(panel.transform, false);

                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 8f;
                rowLayout.childControlWidth = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childControlHeight = true;

                var rowElement = row.AddComponent<LayoutElement>();
                rowElement.minHeight = 34f;
                rowElement.preferredHeight = 34f;

                var caption = new GameObject("Caption", typeof(RectTransform));
                caption.transform.SetParent(row.transform, false);

                var captionText = caption.AddComponent<TMPro.TextMeshProUGUI>();
                captionText.text = label;
                captionText.fontSize = 17f;
                captionText.color = new Color(0.7f, 0.75f, 0.82f);
                captionText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
                captionText.raycastTarget = false;

                var captionElement = caption.AddComponent<LayoutElement>();
                captionElement.minWidth = 150f;
                captionElement.preferredWidth = 150f;
                captionElement.minHeight = 30f;

                var field = new GameObject(label, typeof(RectTransform), typeof(Image));
                field.transform.SetParent(row.transform, false);
                field.GetComponent<Image>().color = new Color(0.15f, 0.16f, 0.19f);

                var fieldElement = field.AddComponent<LayoutElement>();
                fieldElement.minWidth = 190f;
                fieldElement.preferredWidth = 190f;
                fieldElement.minHeight = 30f;

                var viewport = new GameObject("Text", typeof(RectTransform));
                viewport.transform.SetParent(field.transform, false);

                var viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = new Vector2(8f, 2f);
                viewportRect.offsetMax = new Vector2(-8f, -2f);

                var viewportText = viewport.AddComponent<TMPro.TextMeshProUGUI>();
                viewportText.fontSize = 18f;
                viewportText.color = Color.white;
                viewportText.alignment = TMPro.TextAlignmentOptions.MidlineLeft;

                var input = field.AddComponent<TMP_InputField>();
                input.textComponent = viewportText;
                input.textViewport = viewportRect;

                return input;
            }

            TMP_InputField feedField = Field("Feed (in)");
            TMP_InputField bladeField = Field("Blade (deg)");
            TMP_InputField rotateX = Field("Turn X (deg)");
            TMP_InputField rotateY = Field("Turn Y (deg)");
            TMP_InputField rotateZ = Field("Turn Z (deg)");

            var buttonGo = new GameObject("Cut", typeof(RectTransform), typeof(Image));
            buttonGo.transform.SetParent(panel.transform, false);
            buttonGo.GetComponent<Image>().color = new Color(0.75f, 0.2f, 0.18f);

            var buttonElement = buttonGo.AddComponent<LayoutElement>();
            buttonElement.minHeight = 48f;
            buttonElement.preferredHeight = 48f;

            var buttonLabel = new GameObject("Label", typeof(RectTransform));
            buttonLabel.transform.SetParent(buttonGo.transform, false);

            var labelRect = buttonLabel.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var labelText = buttonLabel.AddComponent<TMPro.TextMeshProUGUI>();
            labelText.text = "CUT";
            labelText.fontSize = 26f;
            labelText.alignment = TMPro.TextAlignmentOptions.Center;
            labelText.raycastTarget = false;

            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = buttonGo.GetComponent<Image>();

            TMPro.TextMeshProUGUI hint = Heading("", 14f, new Color(0.6f, 0.65f, 0.72f));
            hint.enableWordWrapping = true;
            hint.GetComponent<LayoutElement>().preferredHeight = 60f;

            var saw = panel.AddComponent<VexDesigner.UI.SawInterface>();
            var so = new SerializedObject(saw);

            so.FindProperty("panel").objectReferenceValue = panel;
            so.FindProperty("feedLabel").objectReferenceValue = feed;
            so.FindProperty("bladeLabel").objectReferenceValue = blade;
            so.FindProperty("rotationLabel").objectReferenceValue = rotation;
            so.FindProperty("stockLabel").objectReferenceValue = stock;
            so.FindProperty("hintLabel").objectReferenceValue = hint;
            so.FindProperty("feedField").objectReferenceValue = feedField;
            so.FindProperty("bladeField").objectReferenceValue = bladeField;
            so.FindProperty("rotateXField").objectReferenceValue = rotateX;
            so.FindProperty("rotateYField").objectReferenceValue = rotateY;
            so.FindProperty("rotateZField").objectReferenceValue = rotateZ;
            so.FindProperty("cutButton").objectReferenceValue = button;
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
        }

        private static GameObject CreateHudImage(Transform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return go;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

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
                block.SetVector("_BaseMap_ST", new Vector4(tiling.x, tiling.y, 0f, 0f));
                renderer.SetPropertyBlock(block);
            }

            return go;
        }

        /// <summary>
        /// Checks that hand-written shaders actually resolve.
        ///
        /// A broken shader does not fail the build - it just fails to be found
        /// at runtime, and the code falls back or renders magenta. For the
        /// gizmo that would show up as handles that are simply invisible,
        /// which looks like a logic bug and is hunted for in the wrong place.
        /// Cheaper to assert it here.
        /// </summary>
        private static void VerifyShaders()
        {
            var required = new[]
            {
                "VexDesigner/GizmoOverlay",
                "VexDesigner/GizmoTransparent",
                "VexDesigner/PartOutline",
                "VexDesigner/PartOutlineMask",
                "VexDesigner/SawPreview",
            };

            foreach (string name in required)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError(
                        $"[WorkshopSceneBuilder] Shader '{name}' not found. The " +
                        "transform gizmo will fall back and may be invisible.");
                }
                else if (!shader.isSupported)
                {
                    Debug.LogError(
                        $"[WorkshopSceneBuilder] Shader '{name}' failed to compile " +
                        "on this platform.");
                }
                else
                {
                    Debug.Log($"[WorkshopSceneBuilder] Shader OK: {name}");
                }
            }
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(
                EditorBuildSettings.scenes);

            if (scenes.Exists(s => s.path == ScenePath))
            {
                return;
            }

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string unityPath)
        {
            if (AssetDatabase.IsValidFolder(unityPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(unityPath)!.Replace('\\', '/');
            string leaf = Path.GetFileName(unityPath);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
