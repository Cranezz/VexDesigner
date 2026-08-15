namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Cuts real parts and checks what comes out.
    ///
    /// A bad slice is not obvious by eye. The part looks broadly right and
    /// then behaves oddly - a collider that leaks, a face that vanishes at a
    /// grazing angle, a hole that a screw still snaps to although the metal
    /// around it was sawn away. The properties that matter are measurable, so
    /// they are measured.
    ///
    /// Watertightness is the one that catches nearly everything. A closed mesh
    /// has every edge shared by exactly two triangles; a slice that fails to
    /// cap, caps twice, or leaves the cut outline unjoined breaks that, and
    /// almost no other bug does.
    /// </summary>
    public static class CuttingTests
    {
        private const float InchesToMetres = 0.0254f;

        private static int failures;
        private static int checks;

        [MenuItem("VexDesigner/Run Cutting Tests")]
        public static void Run()
        {
            failures = 0;
            checks = 0;

            SquareCutAcrossAChannel();
            AngledCut();
            CutRemovesTheHolesItPassesThrough();
            CutsReplayIdentically();
            UndoRestoresTheWholePart();

            if (failures == 0)
            {
                Debug.Log($"[CuttingTests] All {checks} checks passed.");
            }
            else
            {
                Debug.LogError($"[CuttingTests] {failures} of {checks} checks FAILED.");
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// The everyday cut: straight across, keep the left-hand piece.
        /// </summary>
        private static void SquareCutAcrossAChannel()
        {
            Mesh source = ChannelMesh();

            if (source == null)
            {
                return;
            }

            // Measured first, because a cut can only be as closed as what it
            // was given. A converted CAD file is not always a closed solid, and
            // blaming the slicer for the model's own gaps wastes an evening.
            sourceOpenEdges = OpenEdges(source);
            int sourceOpen = sourceOpenEdges;

            Debug.Log(
                $"[CuttingTests] source channel: {source.triangles.Length / 3} tris, " +
                $"{sourceOpen} open edge(s) before any cut.");

            Bounds bounds = source.bounds;

            // Half way along the longest side.
            int axis = LongestAxis(bounds);
            Vector3 normal = Axis(axis);

            var plane = new Plane(-normal, bounds.center);
            MeshSlicer.Result result = MeshSlicer.Slice(source, plane);

            True("the plane cut something", result.cut);
            True("something is left", !result.empty && result.mesh.vertexCount > 0);

            if (result.mesh == null)
            {
                return;
            }

            Bounds cutBounds = result.mesh.bounds;

            Near("the kept half is half as long",
                cutBounds.size[axis], bounds.size[axis] * 0.5f, bounds.size[axis] * 0.02f);

            Near("and unchanged across",
                cutBounds.size[(axis + 1) % 3], bounds.size[(axis + 1) % 3], 0.001f);

            True("a cut face was built", result.outlines.Count > 0);
            True("the result is closed", IsWatertight(result.mesh, "square cut"));

            Debug.Log(
                $"[CuttingTests] square cut: {source.triangles.Length / 3} tris in, " +
                $"{result.mesh.triangles.Length / 3} out, " +
                $"{result.outlines.Count} outline(s), " +
                $"{cutBounds.size[axis] / InchesToMetres:0.000} in long.");
        }

        /// <summary>
        /// A mitre. The interesting part is that the cap is no longer aligned
        /// to anything, so the projection used to triangulate it has to be
        /// derived from the plane rather than assumed.
        /// </summary>
        private static void AngledCut()
        {
            Mesh source = ChannelMesh();

            if (source == null)
            {
                return;
            }

            Bounds bounds = source.bounds;
            int axis = LongestAxis(bounds);

            Vector3 normal = Quaternion.AngleAxis(30f, Axis((axis + 1) % 3)) * -Axis(axis);
            var plane = new Plane(normal.normalized, bounds.center);

            MeshSlicer.Result result = MeshSlicer.Slice(source, plane);

            True("the angled plane cut something", result.cut);
            True("something is left after a mitre", !result.empty);

            if (result.mesh == null || result.mesh.vertexCount == 0)
            {
                return;
            }

            True("a mitred cut face was built", result.outlines.Count > 0);
            True("the mitred result is closed", IsWatertight(result.mesh, "angled cut"));

            // Every remaining vertex must be on the kept side, or the slice
            // left material it was supposed to remove.
            float worst = 0f;

            foreach (Vector3 vertex in result.mesh.vertices)
            {
                worst = Mathf.Min(worst, plane.GetDistanceToPoint(vertex));
            }

            Near("nothing survives on the wrong side", worst, 0f, 0.0001f);

            Debug.Log(
                $"[CuttingTests] 30 degree cut: {result.mesh.triangles.Length / 3} tris, " +
                $"{result.outlines.Count} outline(s).");
        }

        /// <summary>
        /// A hole the blade went through is not a hole any more.
        /// </summary>
        private static void CutRemovesTheHolesItPassesThrough()
        {
            PartDefinition definition = Load("CCHL-2");

            if (definition == null || definition.holeSet == null)
            {
                return;
            }

            GameObject go = PartFactory.Create(definition, withPhysics: false);

            try
            {
                var part = go.GetComponent<PartInstance>();
                var holes = go.GetComponent<PartHoles>();

                int before = holes.Holes.Count;
                True("the channel starts with holes", before > 0);

                Bounds bounds = definition.mesh.bounds;
                int axis = LongestAxis(bounds);

                var plane = new Plane(-Axis(axis), bounds.center);
                PartCutting.Cut(part, plane, 0f, 0f);

                int after = holes.Holes.Count;

                True("cutting removes holes", after < before);
                True("but not all of them", after > 0);

                // Nothing may remain on the removed side.
                foreach (Hole hole in holes.Holes.holes)
                {
                    float d = Mathf.Min(
                        plane.GetDistanceToPoint(hole.front.localPosition),
                        plane.GetDistanceToPoint(hole.back.localPosition));

                    if (d < hole.front.width * 0.5f)
                    {
                        True("every surviving hole is clear of the blade", false);
                        break;
                    }
                }

                checks++;

                Debug.Log(
                    $"[CuttingTests] holes: {before} before, {after} after a cut " +
                    "through the middle.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The property the save format rests on: the same cuts on the same
        /// part give the same geometry, every time.
        /// </summary>
        private static void CutsReplayIdentically()
        {
            PartDefinition definition = Load("CCHL-2");

            if (definition == null)
            {
                return;
            }

            Bounds bounds = definition.mesh.bounds;
            int axis = LongestAxis(bounds);

            var first = new Plane(-Axis(axis), bounds.center);
            var second = new Plane(
                Axis(axis), bounds.center - (Axis(axis) * (bounds.size[axis] * 0.25f)));

            int countA = CutTwice(definition, first, second, out Bounds boundsA);
            int countB = CutTwice(definition, first, second, out Bounds boundsB);

            Near("replaying gives the same triangle count", countA, countB, 0.1f);
            Near("and the same size",
                Vector3.Distance(boundsA.size, boundsB.size), 0f, 1e-6f);

            Debug.Log(
                $"[CuttingTests] replay: {countA} tris both times, " +
                $"{boundsA.size.magnitude / InchesToMetres:0.0000} in across.");
        }

        private static int CutTwice(
            PartDefinition definition, Plane first, Plane second, out Bounds bounds)
        {
            GameObject go = PartFactory.Create(definition, withPhysics: false);

            try
            {
                var part = go.GetComponent<PartInstance>();

                PartCutting.Cut(part, first, 0f, 0f);
                PartCutting.Cut(part, second, 0f, 0f);

                Mesh mesh = go.GetComponent<MeshFilter>().sharedMesh;
                bounds = mesh.bounds;
                return mesh.triangles.Length / 3;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Undo re-slices from the original rather than trying to reverse a
        /// slice, so it has to give back exactly what was there before.
        /// </summary>
        private static void UndoRestoresTheWholePart()
        {
            PartDefinition definition = Load("CCHL-2");

            if (definition == null)
            {
                return;
            }

            GameObject go = PartFactory.Create(definition, withPhysics: false);

            try
            {
                var part = go.GetComponent<PartInstance>();
                var holes = go.GetComponent<PartHoles>();

                int holesBefore = holes.Holes.Count;
                int trianglesBefore = definition.mesh.triangles.Length / 3;

                Bounds bounds = definition.mesh.bounds;
                var plane = new Plane(-Axis(LongestAxis(bounds)), bounds.center);

                PartCutting.Cut(part, plane, 0f, 0f);
                True("the part is shorter after cutting",
                    go.GetComponent<MeshFilter>().sharedMesh.bounds.size.magnitude <
                    bounds.size.magnitude);

                PartCutting.Undo(part);

                Mesh restored = go.GetComponent<MeshFilter>().sharedMesh;

                Near("undo restores every triangle",
                    restored.triangles.Length / 3, trianglesBefore, 0.1f);
                Near("undo restores every hole", holes.Holes.Count, holesBefore, 0.1f);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// True when every edge is shared by exactly two triangles.
        ///
        /// The single most useful property of a cut result. A cap that was not
        /// built, was built twice, or was built from an outline that did not
        /// join up all break it, and very little else does.
        /// </summary>
        private static bool IsWatertight(Mesh mesh, string what)
        {
            checks++;
            int open = OpenEdges(mesh);

            if (open > sourceOpenEdges)
            {
                failures++;
                Debug.LogError(
                    $"[CuttingTests] FAILED: {what} left {open} open edges, and " +
                    $"the mesh it was cut from had {sourceOpenEdges}. The cut " +
                    "added holes of its own.");

                return false;
            }

            return true;
        }

        private static int sourceOpenEdges;

        /// <summary>
        /// Edges not shared by exactly two triangles - the gaps in a surface.
        /// </summary>
        private static int OpenEdges(Mesh mesh)
        {

            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;

            var edges = new Dictionary<(Vector3Int, Vector3Int), int>();

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Count(edges, vertices, triangles[t], triangles[t + 1]);
                Count(edges, vertices, triangles[t + 1], triangles[t + 2]);
                Count(edges, vertices, triangles[t + 2], triangles[t]);
            }

            int open = 0;

            foreach (KeyValuePair<(Vector3Int, Vector3Int), int> edge in edges)
            {
                if (edge.Value != 2)
                {
                    open++;
                }
            }

            return open;
        }

        private static void Count(
            Dictionary<(Vector3Int, Vector3Int), int> edges,
            Vector3[] vertices, int a, int b)
        {
            Vector3Int ka = Key(vertices[a]);
            Vector3Int kb = Key(vertices[b]);

            if (ka == kb)
            {
                return;
            }

            // Unordered, so the two triangles sharing an edge agree about it
            // however they are wound.
            var key = Compare(ka, kb) < 0 ? (ka, kb) : (kb, ka);

            edges.TryGetValue(key, out int count);
            edges[key] = count + 1;
        }

        private static int Compare(Vector3Int a, Vector3Int b)
        {
            if (a.x != b.x) { return a.x - b.x; }
            if (a.y != b.y) { return a.y - b.y; }
            return a.z - b.z;
        }

        private static Vector3Int Key(Vector3 point)
        {
            const float grid = 1e-5f;

            return new Vector3Int(
                Mathf.RoundToInt(point.x / grid),
                Mathf.RoundToInt(point.y / grid),
                Mathf.RoundToInt(point.z / grid));
        }

        private static int LongestAxis(Bounds bounds)
        {
            Vector3 size = bounds.size;
            return size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
        }

        private static Vector3 Axis(int index) =>
            index == 0 ? Vector3.right : (index == 1 ? Vector3.up : Vector3.forward);

        private static Mesh ChannelMesh()
        {
            PartDefinition definition = Load("CCHL-2");
            return definition == null ? null : definition.mesh;
        }

        private static PartDefinition Load(string id)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PartDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (definition != null && definition.Matches(id))
                {
                    return definition;
                }
            }

            Debug.LogError($"[CuttingTests] No part with ID '{id}'. Test skipped.");
            failures++;
            return null;
        }

        private static void Near(string what, float actual, float expected, float tolerance)
        {
            checks++;

            if (Mathf.Abs(actual - expected) > tolerance)
            {
                failures++;
                Debug.LogError(
                    $"[CuttingTests] FAILED: {what}. Expected {expected:0.00000}, " +
                    $"got {actual:0.00000}.");
            }
        }

        private static void True(string what, bool condition)
        {
            checks++;

            if (!condition)
            {
                failures++;
                Debug.LogError($"[CuttingTests] FAILED: {what}.");
            }
        }
    }
}
