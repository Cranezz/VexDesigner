namespace VexDesigner.EditorTools
{
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using VexDesigner.CameraControl;
    using VexDesigner.InputSources;

    /// <summary>
    /// Builds the phase-1 workshop scene from code.
    ///
    /// Generating the scene rather than hand-placing it means the layout is
    /// reviewable in a diff, reproducible after a bad merge, and self-
    /// documenting about *why* things are the size they are. Unity scene files
    /// are YAML blobs nobody can read, so a hand-built scene is effectively
    /// undocumented.
    ///
    /// UNITS. Everything in this file is authored in **inches**, because that
    /// is what VEX parts are specified in and what the build rules are written
    /// in. Inches are converted to metres exactly once, at the point of use,
    /// via <see cref="In"/>.
    ///
    /// World space remains 1 unit = 1 metre. This is not negotiable: Unity's
    /// physics solver and every OpenXR runtime assume it, and a project built
    /// at 1 unit = 1 inch has hands roughly forty times the wrong size the
    /// moment a headset is attached. Authoring in inches while storing metres
    /// gives both - see docs/ARCHITECTURE.md section 1.
    /// </summary>
    public static class WorkshopSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string MaterialsFolder = "Assets/Materials";
        private const string TexturesFolder = "Assets/Textures";
        private const string ScenePath = ScenesFolder + "/Workshop.unity";

        private const float InchesToMetres = 0.0254f;

        // --- Build area -------------------------------------------------
        // VEX games size robots at 18" cubed, expanding to 24" in most
        // seasons. The mat is deliberately larger than the expanded limit in
        // both directions so a fully expanded robot still has working room
        // around it rather than hanging over the edge.
        private const float MatWidthIn = 36f;
        private const float MatDepthIn = 30f;
        private const float MatThicknessIn = 0.12f;

        // The mat texture tiles once every six inches and carries a one-inch
        // grid. Six divides evenly into both mat dimensions, so no grid square
        // is ever clipped. Changing the mat size to a non-multiple of six will
        // produce a partial square at the edge.
        private const float MatInchesPerTextureTile = 6f;

        // --- Table ------------------------------------------------------
        // Standard workbench proportions. Height matters for VR: a bench at
        // the wrong height is immediately and uncomfortably obvious in a
        // headset in a way it never is on a monitor.
        private const float TableWidthIn = 72f;
        private const float TableDepthIn = 36f;
        private const float TableHeightIn = 36f;
        private const float TableTopThicknessIn = 1.5f;
        private const float LegThicknessIn = 3f;
        private const float LegInsetIn = 4f;

        private const float FloorSizeIn = 480f;

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
                // would let a broken build masquerade as a passing one.
                Debug.LogError($"[WorkshopSceneBuilder] FAILED: {e}");
                EditorApplication.Exit(1);
            }
        }

        private static void Build()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(MaterialsFolder);

            // The scene is meaningless without its surfaces, so generate them
            // if they are missing rather than silently producing grey boxes.
            if (!File.Exists($"{TexturesFolder}/CuttingMat_Albedo.png"))
            {
                Debug.Log("[WorkshopSceneBuilder] Textures missing; generating them first.");
                ProceduralTextureGenerator.Generate();
            }

            Material floorMat = CreateMaterial(
                "Floor", new Color(0.75f, 0.75f, 0.76f), 0.92f,
                "Concrete_Albedo", TilingFor(FloorSizeIn, FloorSizeIn, 48f));

            Material tableMat = CreateMaterial(
                "TableTop", Color.white, 0.62f,
                "Wood_Albedo", TilingFor(TableWidthIn, TableDepthIn, 24f));

            Material frameMat = CreateMaterial(
                "TableFrame", new Color(0.20f, 0.21f, 0.23f), 0.45f, null, Vector2.one);

            Material matMat = CreateMaterial(
                "CuttingMat", Color.white, 0.80f,
                "CuttingMat_Albedo",
                TilingFor(MatWidthIn, MatDepthIn, MatInchesPerTextureTile));

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildFloor(floorMat);
            BuildTable(tableMat, frameMat);
            BuildMat(matMat);
            BuildCameraRig();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[WorkshopSceneBuilder] Table {TableWidthIn}x{TableDepthIn}x{TableHeightIn} in, " +
                $"mat {MatWidthIn}x{MatDepthIn} in with a 1 in grid. " +
                $"Mat top surface at {TableHeightIn + MatThicknessIn:F2} in " +
                $"({In(TableHeightIn + MatThicknessIn):F4} world units).");
        }

        private static void BuildLighting()
        {
            var sunGo = new GameObject("Directional Light");
            sunGo.transform.SetPositionAndRotation(
                new Vector3(0f, In(120f), 0f), Quaternion.Euler(50f, -30f, 0f));

            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.97f, 0.91f);
            sun.shadows = LightShadows.Soft;

            // A workshop is an interior full of bounced light. Flat ambient
            // keeps the underside of parts readable, which matters when the
            // user is lining up screw holes.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.44f, 0.47f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.34f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.17f, 0.18f);
        }

        private static void BuildFloor(Material mat)
        {
            GameObject floor = CreateBox(
                "Floor",
                new Vector3(0f, In(-1f), 0f),
                new Vector3(In(FloorSizeIn), In(2f), In(FloorSizeIn)),
                mat);
            floor.isStatic = true;
        }

        private static void BuildTable(Material topMat, Material frameMat)
        {
            var root = new GameObject("WorkshopTable");
            root.isStatic = true;

            float topCentreY = TableHeightIn - (TableTopThicknessIn * 0.5f);
            GameObject top = CreateBox(
                "TableTop",
                new Vector3(0f, In(topCentreY), 0f),
                new Vector3(In(TableWidthIn), In(TableTopThicknessIn), In(TableDepthIn)),
                topMat);
            top.transform.SetParent(root.transform, true);

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
                GameObject leg = CreateBox(
                    $"Leg_{i}",
                    new Vector3(In(offsets[i].x), In(legHeightIn * 0.5f), In(offsets[i].y)),
                    new Vector3(In(LegThicknessIn), In(legHeightIn), In(LegThicknessIn)),
                    frameMat);
                leg.transform.SetParent(root.transform, true);
                leg.isStatic = true;
            }
        }

        private static void BuildMat(Material mat)
        {
            GameObject buildMat = CreateBox(
                "CuttingMat",
                new Vector3(0f, In(TableHeightIn + (MatThicknessIn * 0.5f)), 0f),
                new Vector3(In(MatWidthIn), In(MatThicknessIn), In(MatDepthIn)),
                mat);
            buildMat.isStatic = true;
        }

        private static void BuildCameraRig()
        {
            // Rig parent holds the orbit logic; camera child stays at local
            // zero. See WorkshopCameraRig for why this split matters for VR.
            var rig = new GameObject("CameraRig");
            rig.AddComponent<MouseLookInput>();
            WorkshopCameraRig orbit = rig.AddComponent<WorkshopCameraRig>();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;

            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;

            // Near plane is tight because the user gets close to small parts;
            // the 0.3 default would clip a screw out of view when inspecting it.
            cam.nearClipPlane = In(0.4f);
            cam.farClipPlane = In(FloorSizeIn * 2f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.12f, 0.14f);

            camGo.AddComponent<AudioListener>();

            // Frame the mat. Set through SerializedObject rather than public
            // setters so the rig's runtime API stays free of editor concerns.
            var so = new SerializedObject(orbit);
            so.FindProperty("focusPoint").vector3Value =
                new Vector3(0f, In(TableHeightIn), 0f);
            so.FindProperty("distance").floatValue = In(52f);
            so.FindProperty("minDistance").floatValue = In(6f);
            so.FindProperty("maxDistance").floatValue = In(140f);
            so.FindProperty("maxPanFromOrigin").floatValue = In(MatWidthIn * 0.5f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Texture repeats needed so one tile covers
        /// <paramref name="inchesPerTile"/> inches of surface.
        /// </summary>
        private static Vector2 TilingFor(float widthIn, float depthIn, float inchesPerTile)
        {
            return new Vector2(widthIn / inchesPerTile, depthIn / inchesPerTile);
        }

        private static GameObject CreateBox(
            string name, Vector3 position, Vector3 size, Material material)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private static Material CreateMaterial(
            string name, Color colour, float roughness, string textureName, Vector2 tiling)
        {
            string path = $"{MaterialsFolder}/{name}.mat";

            // Fall back to the default shader if URP is somehow absent, so the
            // scene builds with visible-but-wrong materials rather than the
            // magenta "shader missing" soup that gives no clue what went wrong.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[WorkshopSceneBuilder] URP Lit shader not found. Falling back " +
                    "to the default shader. Is the Universal RP package installed " +
                    "and assigned in Project Settings > Graphics?");
                shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            }

            var mat = new Material(shader) { name = name };

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor("_BaseColor", colour);
            }
            if (mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", colour);
            }
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", 1f - roughness);
            }
            if (mat.HasProperty("_Glossiness"))
            {
                mat.SetFloat("_Glossiness", 1f - roughness);
            }

            if (!string.IsNullOrEmpty(textureName))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"{TexturesFolder}/{textureName}.png");

                if (tex == null)
                {
                    Debug.LogWarning(
                        $"[WorkshopSceneBuilder] Texture '{textureName}' not found. " +
                        "Run VexDesigner > Regenerate Workshop Textures.");
                }
                else if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", tex);
                    mat.SetTextureScale("_BaseMap", tiling);
                }
                else if (mat.HasProperty("_MainTex"))
                {
                    mat.SetTexture("_MainTex", tex);
                    mat.SetTextureScale("_MainTex", tiling);
                }
            }

            // Overwrite rather than reuse: sizes and tiling change when the
            // constants above change, and a stale material would silently keep
            // the old grid scale - which is exactly the bug this scene exists
            // to make impossible.
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mat, existing);
                Object.DestroyImmediate(mat);
                return existing;
            }

            AssetDatabase.CreateAsset(mat, path);
            return mat;
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
