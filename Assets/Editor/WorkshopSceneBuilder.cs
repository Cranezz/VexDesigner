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
    /// documenting about *why* things are where they are. Unity scene files
    /// are YAML blobs that nobody can meaningfully read, so a scene assembled
    /// by hand is effectively undocumented.
    ///
    /// Everything here is greybox. It is meant to be replaced by real art.
    ///
    /// Units are metres (see docs/ARCHITECTURE.md section 1). Dimensions are
    /// chosen to match a real workbench so that VR scale is correct from the
    /// outset — a table that "looks fine" on a monitor but is 1.4x oversized
    /// feels immediately wrong in a headset.
    /// </summary>
    public static class WorkshopSceneBuilder
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string MaterialsFolder = "Assets/Materials";
        private const string ScenePath = ScenesFolder + "/Workshop.unity";

        // Real-workbench dimensions, in metres.
        private const float TableHeight = 0.90f;
        private const float TableWidth = 1.80f;
        private const float TableDepth = 0.80f;
        private const float TableTopThickness = 0.05f;

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

            Material floorMat = CreateMaterial("Floor", new Color(0.24f, 0.24f, 0.26f), 0.9f);
            Material tableMat = CreateMaterial("TableTop", new Color(0.45f, 0.33f, 0.22f), 0.7f);
            Material frameMat = CreateMaterial("TableFrame", new Color(0.18f, 0.19f, 0.21f), 0.45f);
            Material plateMat = CreateMaterial("BuildPlate", new Color(0.16f, 0.42f, 0.55f), 0.55f);

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            BuildFloor(floorMat);
            BuildTable(tableMat, frameMat);
            BuildPlate(plateMat);
            BuildCameraRig();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterInBuildSettings();
            AssetDatabase.SaveAssets();
        }

        private static void BuildLighting()
        {
            var sunGo = new GameObject("Directional Light");
            sunGo.transform.SetPositionAndRotation(
                new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));

            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.color = new Color(1f, 0.97f, 0.91f);
            sun.shadows = LightShadows.Soft;

            // A workshop is an interior with a lot of bounced light. Flat
            // ambient keeps the underside of parts readable, which matters when
            // the user is trying to line up screw holes.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.42f, 0.45f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.32f, 0.32f, 0.34f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.17f);
        }

        private static void BuildFloor(Material mat)
        {
            GameObject floor = CreateBox(
                "Floor", new Vector3(0f, -0.025f, 0f), new Vector3(8f, 0.05f, 8f), mat);
            floor.isStatic = true;
        }

        private static void BuildTable(Material topMat, Material frameMat)
        {
            var root = new GameObject("WorkshopTable");
            root.isStatic = true;

            float topCentreY = TableHeight - (TableTopThickness * 0.5f);
            GameObject top = CreateBox(
                "TableTop",
                new Vector3(0f, topCentreY, 0f),
                new Vector3(TableWidth, TableTopThickness, TableDepth),
                topMat);
            top.transform.SetParent(root.transform, true);

            // Legs inset from the corners so they read as a real frame rather
            // than a solid block.
            const float legThickness = 0.07f;
            const float inset = 0.10f;
            float legHeight = TableHeight - TableTopThickness;
            float x = (TableWidth * 0.5f) - inset - (legThickness * 0.5f);
            float z = (TableDepth * 0.5f) - inset - (legThickness * 0.5f);

            var offsets = new[]
            {
                new Vector3(+x, legHeight * 0.5f, +z),
                new Vector3(-x, legHeight * 0.5f, +z),
                new Vector3(+x, legHeight * 0.5f, -z),
                new Vector3(-x, legHeight * 0.5f, -z),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject leg = CreateBox(
                    $"Leg_{i}",
                    offsets[i],
                    new Vector3(legThickness, legHeight, legThickness),
                    frameMat);
                leg.transform.SetParent(root.transform, true);
                leg.isStatic = true;
            }
        }

        private static void BuildPlate(Material mat)
        {
            // Sits proud of the tabletop so it reads as a distinct working
            // surface. Parts will be assembled here.
            GameObject plate = CreateBox(
                "BuildPlate",
                new Vector3(0f, TableHeight + 0.006f, 0f),
                new Vector3(0.70f, 0.012f, 0.45f),
                mat);
            plate.isStatic = true;
        }

        private static void BuildCameraRig()
        {
            // Rig parent holds the orbit logic; camera child stays at local
            // zero. See WorkshopCameraRig for why this split matters for VR.
            var rig = new GameObject("CameraRig");
            rig.AddComponent<MouseLookInput>();
            rig.AddComponent<WorkshopCameraRig>();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = Vector3.zero;
            camGo.transform.localRotation = Quaternion.identity;

            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 60f;

            // Near plane is tight because the user will get close to small
            // parts; a default 0.3 would clip screws out of view when
            // inspecting them.
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.12f, 0.14f);

            camGo.AddComponent<AudioListener>();
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

        private static Material CreateMaterial(string name, Color colour, float smoothnessInverse)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                return existing;
            }

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
                mat.SetFloat("_Smoothness", 1f - smoothnessInverse);
            }
            if (mat.HasProperty("_Glossiness"))
            {
                mat.SetFloat("_Glossiness", 1f - smoothnessInverse);
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
