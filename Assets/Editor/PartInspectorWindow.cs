namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;
    using VexDesigner.Parts;

    /// <summary>
    /// Opens one part at a time, alone, on a one-inch grid.
    ///
    /// Built for developing hole detection. The workshop scene is the wrong
    /// place for that: the part is small, surrounded by a garage, and lit for
    /// atmosphere rather than for reading geometry. Here a part sits at the
    /// origin over a ruled grid with flat light, so a detected hole can be
    /// checked against the grid by eye.
    ///
    /// Deliberately an editor tool. Hole positions must be identical every
    /// time - they are what screws snap to and what save files depend on - so
    /// detection belongs at import, computed once and stored, not recomputed at
    /// runtime where a floating-point difference could move a hole between
    /// sessions.
    /// </summary>
    public sealed class PartInspectorWindow : EditorWindow
    {
        private const string ScenePath = "Assets/Scenes/PartInspector.unity";
        private const float InchesToMetres = 0.0254f;

        private Vector2 scroll;
        private PartDefinition[] parts;
        private PartDefinition current;
        private string filter = string.Empty;

        [MenuItem("VexDesigner/Part Inspector")]
        public static void Open()
        {
            var window = GetWindow<PartInspectorWindow>("Part Inspector");
            window.minSize = new Vector2(320f, 400f);
            window.Refresh();
        }

        private void OnEnable() => Refresh();

        private void Refresh()
        {
            var found = new List<PartDefinition>();

            foreach (string guid in AssetDatabase.FindAssets("t:PartDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null)
                {
                    found.Add(definition);
                }
            }

            found.Sort((a, b) => string.CompareOrdinal(a.partId, b.partId));
            parts = found.ToArray();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                filter = GUILayout.TextField(filter, EditorStyles.toolbarSearchField);

                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    Refresh();
                }
            }

            if (parts == null || parts.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No part definitions found. Convert a STEP file into " +
                    "Assets/Parts, then run VexDesigner > Rebuild Part Library.",
                    MessageType.Info);
                return;
            }

            DrawCurrent();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Parts", EditorStyles.boldLabel);

            using var scope = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = scope.scrollPosition;

            foreach (PartDefinition part in parts)
            {
                if (!Matches(part))
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isCurrent = part == current;
                    GUI.backgroundColor = isCurrent ? new Color(0.6f, 0.8f, 1f) : Color.white;

                    if (GUILayout.Button(Label(part), GUILayout.Height(22f)))
                    {
                        LoadPart(part);
                    }

                    GUI.backgroundColor = Color.white;

                    if (GUILayout.Button("Asset", GUILayout.Width(52f)))
                    {
                        Selection.activeObject = part;
                        EditorGUIUtility.PingObject(part);
                    }
                }
            }
        }

        private bool Matches(PartDefinition part)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            string needle = filter.ToLowerInvariant();
            return part.partId.ToLowerInvariant().Contains(needle) ||
                   part.displayName.ToLowerInvariant().Contains(needle);
        }

        private static string Label(PartDefinition part)
        {
            string flags = part.hasHolePattern ? " [holes]" : string.Empty;
            return $"{part.partId}  —  {part.displayName}{flags}";
        }

        private void DrawCurrent()
        {
            if (current == null)
            {
                EditorGUILayout.HelpBox(
                    "Pick a part below to open it alone on a one-inch grid.",
                    MessageType.None);
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(current.displayName, EditorStyles.boldLabel);

                Mesh mesh = current.mesh;
                if (mesh == null)
                {
                    EditorGUILayout.HelpBox("No mesh assigned.", MessageType.Warning);
                    return;
                }

                Vector3 inches = mesh.bounds.size / InchesToMetres;

                // Sizes stated in hole pitches as well as inches. VEX geometry
                // is laid out on a 0.5 in grid, so "35.0 pitches" is a far
                // stronger signal that the scale is right than "17.500 in".
                EditorGUILayout.LabelField(
                    "Size",
                    $"{inches.x:F3} x {inches.y:F3} x {inches.z:F3} in");

                float pitch = Mathf.Max(0.01f, current.holePitchInches);
                EditorGUILayout.LabelField(
                    "In hole pitches",
                    $"{inches.x / pitch:F2} x {inches.y / pitch:F2} x {inches.z / pitch:F2}");

                EditorGUILayout.LabelField("Triangles", $"{mesh.triangles.Length / 3:N0}");
                EditorGUILayout.LabelField("Vertices", $"{mesh.vertexCount:N0}");
                EditorGUILayout.LabelField("Readable", mesh.isReadable ? "yes" : "NO");
                EditorGUILayout.LabelField(
                    "Class", $"{current.partClass} / {current.subClass}");
                EditorGUILayout.LabelField(
                    "Flags",
                    $"holes: {current.hasHolePattern}   cuttable: {current.cuttable}");

                if (!mesh.isReadable)
                {
                    EditorGUILayout.HelpBox(
                        "Mesh is not readable, so hole detection cannot see its " +
                        "vertices. Check the import settings.",
                        MessageType.Error);
                }
            }
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        private void LoadPart(PartDefinition part)
        {
            if (part == null || part.mesh == null)
            {
                return;
            }

            current = part;

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting();
            GameObject instance = BuildPart(part);
            BuildGrid(part);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Selection.activeGameObject = instance;
            FrameOn(instance);
        }

        private static GameObject BuildPart(PartDefinition part)
        {
            var go = new GameObject(part.displayName);
            go.AddComponent<MeshFilter>().sharedMesh = part.mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = WorkshopMaterials.Aluminium?.Material;

            // Sat on the grid rather than centred on the origin, so the grid
            // reads as a surface the part is lying on and distances can be
            // counted off it.
            go.transform.position = new Vector3(0f, -part.mesh.bounds.min.y, 0f);

            return go;
        }

        private static void BuildGrid(PartDefinition part)
        {
            WorkshopMaterials.BuildAll();

            // Sized to the part with a margin, and rounded up to whole feet so
            // the grid always ends on a major line.
            Vector3 size = part.mesh.bounds.size / InchesToMetres;
            float span = Mathf.Max(12f, Mathf.Ceil((Mathf.Max(size.x, size.z) + 6f) / 6f) * 6f);

            GameObject grid = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grid.name = "InchGrid";
            grid.transform.position = new Vector3(0f, -0.005f, 0f);
            grid.transform.localScale =
                new Vector3(span * InchesToMetres, 0.01f, span * InchesToMetres);

            var renderer = grid.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = WorkshopMaterials.CuttingMat.Material;

            // The mat texture is one tile per six inches, so this makes each
            // square exactly one inch regardless of how large the grid is.
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetVector("_BaseMap_ST", new Vector4(span / 6f, span / 6f, 0f, 0f));
            renderer.SetPropertyBlock(block);

            grid.GetComponent<Collider>().enabled = false;
        }

        private static void BuildLighting()
        {
            var sunGo = new GameObject("Light");
            sunGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.None;

            // Flat and bright. Atmosphere makes geometry harder to read, and
            // reading geometry is the entire purpose of this scene.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.64f, 0.68f);
        }

        private static void FrameOn(GameObject target)
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                return;
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                view.Frame(renderer.bounds, false);
            }

            view.Repaint();
        }
    }
}
