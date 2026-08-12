namespace VexDesigner.EditorTools
{
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
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
            if (!File.Exists($"{TexturesFolder}/Concrete_Albedo.png"))
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
            BuildPlayer();
            BuildInterface();

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
            text.text = "Page 1 / 1";
            text.fontSize = 1.2f;
            text.alignment = TMPro.TextAlignmentOptions.Center;
            text.color = new Color(0.92f, 0.93f, 0.95f);

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(In(12f), In(3f));
            }

            shelf.AttachLabel(text);
        }

        // ------------------------------------------------------------------
        // Player
        // ------------------------------------------------------------------

        private static void BuildPlayer()
        {
            var player = new GameObject("Player");
            player.transform.position = GarageRoomBuilder.PlayerSpawnPosition;
            player.transform.rotation = Quaternion.Euler(0f, GarageRoomBuilder.PlayerSpawnYaw, 0f);

            var controller = player.AddComponent<CharacterController>();
            controller.height = In(68f);
            controller.radius = In(9f);
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

            // Spawn point is baked in rather than read back from the room
            // builder at runtime, because that builder is editor-only code and
            // does not exist in a real build.
            player.AddComponent<PlayerSpawn>().Configure(
                GarageRoomBuilder.PlayerSpawnPosition, GarageRoomBuilder.PlayerSpawnYaw);
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

            GameObject hand = CreateHudImage(canvasGo.transform, "Hand", 34f);
            hand.GetComponent<Image>().sprite = Crosshair.GetHandSprite();
            hand.GetComponent<Image>().enabled = false;

            var crosshair = canvasGo.AddComponent<Crosshair>();
            var so = new SerializedObject(crosshair);
            so.FindProperty("dot").objectReferenceValue = dot.GetComponent<Image>();
            so.FindProperty("hand").objectReferenceValue = hand.GetComponent<Image>();
            so.ApplyModifiedPropertiesWithoutUndo();

            // An EventSystem is required for any UI interaction. Without one
            // the pause menu's buttons would render but never respond.
            if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var events = new GameObject("EventSystem");
                events.AddComponent<UnityEngine.EventSystems.EventSystem>();
                events.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
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
