namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Cuts a mesh with a plane and keeps one side, capping the exposed face.
    ///
    /// This is the piece the original Protobot did not have. There, a part was
    /// modelled as a set of pre-cut lengths and "cutting" swapped which one was
    /// drawn - so a 35-hole C-channel could become a 20-hole C-channel and
    /// nothing else. Here the geometry is actually divided, which is what makes
    /// an angled cut, a cut through a hole, or a cut at 7.317 inches possible at
    /// all.
    ///
    /// Planar rather than general CSG, deliberately. A saw makes planar cuts and
    /// nothing else, so the whole apparatus of boolean solid modelling - which
    /// is difficult to make robust and slow when it is - buys nothing. A plane
    /// also happens to be four numbers, which is what makes a cut cheap to save,
    /// cheap to undo, and cheap to send over a network.
    ///
    /// The kept side is the side the plane's normal points toward.
    /// </summary>
    public static class MeshSlicer
    {
        /// <summary>
        /// How close to the plane a vertex may be and still count as on it.
        /// A hundredth of a millimetre: far below any feature of a VEX part,
        /// far above the noise in a converted CAD file.
        /// </summary>
        private const float OnPlane = 1e-5f;

        /// <summary>
        /// Grid used to recognise two cut points as the same corner. Vertices
        /// arrive split - the same corner appears once per face that meets
        /// there - so the cut outline has to be re-stitched from coordinates.
        /// </summary>
        private const float WeldGrid = 1e-5f;

        public sealed class Result
        {
            public Mesh mesh;

            /// <summary>Closed outlines of the cut face, in mesh space.</summary>
            public List<List<Vector3>> outlines = new List<List<Vector3>>();

            /// <summary>False when the plane missed the mesh entirely.</summary>
            public bool cut;

            /// <summary>True when nothing was left on the kept side.</summary>
            public bool empty;
        }

        /// <summary>
        /// Cuts <paramref name="source"/> with <paramref name="plane"/>, both in
        /// the same local space, and returns the kept side.
        /// </summary>
        public static Result Slice(Mesh source, Plane plane, bool cap = true)
        {
            var result = new Result();

            if (source == null || !source.isReadable)
            {
                Debug.LogError(
                    "[Cutting] Mesh is missing or not readable, so it cannot be " +
                    "cut. Check the part import settings.");

                return result;
            }

            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uvs = source.uv;
            int[] triangles = source.triangles;

            bool hasNormals = normals != null && normals.Length == positions.Length;
            bool hasUvs = uvs != null && uvs.Length == positions.Length;

            var build = new Builder(hasNormals, hasUvs);
            var segments = new List<(Vector3 a, Vector3 b)>();

            var distance = new float[positions.Length];
            bool anyBelow = false;
            bool anyAbove = false;

            for (int i = 0; i < positions.Length; i++)
            {
                distance[i] = plane.GetDistanceToPoint(positions[i]);

                anyBelow |= distance[i] <= 0f;
                anyAbove |= distance[i] > 0f;
            }

            if (!anyBelow)
            {
                // Nothing to remove: the plane is outside the mesh, or exactly
                // on its surface. Handing back the original avoids rebuilding
                // an identical mesh and, more importantly, avoids reporting a
                // cut that did not happen.
                result.mesh = source;
                return result;
            }

            result.cut = true;

            if (!anyAbove)
            {
                result.empty = true;
                return result;
            }

            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int i0 = triangles[t];
                int i1 = triangles[t + 1];
                int i2 = triangles[t + 2];

                SliceTriangle(
                    build, segments, positions, normals, uvs, hasNormals, hasUvs,
                    distance, i0, i1, i2);
            }

            if (cap)
            {
                BuildOutlines(segments, result.outlines);
                CapFace(build, result.outlines, plane);
            }

            result.mesh = build.ToMesh(source.name + " (cut)");
            result.empty = result.mesh.vertexCount == 0;
            return result;
        }

        // ------------------------------------------------------------------
        // One triangle
        // ------------------------------------------------------------------

        private static void SliceTriangle(
            Builder build, List<(Vector3, Vector3)> segments,
            Vector3[] positions, Vector3[] normals, Vector2[] uvs,
            bool hasNormals, bool hasUvs, float[] distance,
            int i0, int i1, int i2)
        {
            index[0] = i0;
            index[1] = i1;
            index[2] = i2;

            // A strict sign test, with no band around the plane.
            //
            // Treating near-zero distances as "on the plane, therefore kept"
            // seems kinder and is not: a vertex counted as on the plane
            // produces no crossing on the edges leading to it, so the cut
            // outline arrives at that corner and stops. About half a percent
            // of edges came out unmatched that way, which is a mesh with holes
            // in it. Strict signs mean every crossing is a real crossing and
            // every outline joins up.
            for (int i = 0; i < 3; i++)
            {
                keep[i] = distance[index[i]] > 0f;
            }

            int kept = (keep[0] ? 1 : 0) + (keep[1] ? 1 : 0) + (keep[2] ? 1 : 0);

            if (kept == 0)
            {
                return;
            }

            if (kept == 3)
            {
                build.Triangle(
                    build.Vertex(positions, normals, uvs, hasNormals, hasUvs, i0),
                    build.Vertex(positions, normals, uvs, hasNormals, hasUvs, i1),
                    build.Vertex(positions, normals, uvs, hasNormals, hasUvs, i2));

                return;
            }

            // Rotated so the odd corner out sits in a known slot. Only cyclic
            // rotations are used, so the winding - and therefore which way the
            // face points - is untouched.
            //
            // Getting this wrong is not subtle in its consequences and was not
            // subtle in its cause: the first version worked out the crossings
            // before deciding which case it was in, so it asked for the
            // crossing on edges that do not cross. That invented a vertex from
            // two same-signed distances *and* cached it against the edge, so
            // the triangle on the other side of that edge was later handed the
            // invented point instead of the real one. The result kept material
            // it should have removed and could not be sealed.
            int start;

            if (kept == 1)
            {
                start = keep[0] ? 0 : (keep[1] ? 1 : 2);
            }
            else
            {
                // The lost corner goes last, so the two survivors lead.
                int lost = !keep[0] ? 0 : (!keep[1] ? 1 : 2);
                start = (lost + 1) % 3;
            }

            int ia = index[start];
            int ib = index[(start + 1) % 3];
            int ic = index[(start + 2) % 3];

            float da = distance[ia];
            float db = distance[ib];
            float dc = distance[ic];

            int a = build.Vertex(positions, normals, uvs, hasNormals, hasUvs, ia);

            if (kept == 1)
            {
                // Only A survives, so the edges leaving it are the ones cut.
                int ab = build.Crossing(
                    positions, normals, uvs, hasNormals, hasUvs, ia, ib, da, db);

                int ca = build.Crossing(
                    positions, normals, uvs, hasNormals, hasUvs, ic, ia, dc, da);

                build.Triangle(a, ab, ca);
                segments.Add((build.Position(ab), build.Position(ca)));
                return;
            }

            // A and B survive and C does not, so what is left is a
            // quadrilateral: A, B, and the two crossings on the edges into C.
            int b = build.Vertex(positions, normals, uvs, hasNormals, hasUvs, ib);

            int bc = build.Crossing(
                positions, normals, uvs, hasNormals, hasUvs, ib, ic, db, dc);

            int ca2 = build.Crossing(
                positions, normals, uvs, hasNormals, hasUvs, ic, ia, dc, da);

            build.Triangle(a, b, bc);
            build.Triangle(a, bc, ca2);

            segments.Add((build.Position(bc), build.Position(ca2)));
        }

        // Scratch, so slicing a mesh of thirteen thousand triangles does not
        // allocate thirteen thousand tiny arrays.
        private static readonly int[] index = new int[3];
        private static readonly bool[] keep = new bool[3];

        // ------------------------------------------------------------------
        // The cut face
        // ------------------------------------------------------------------

        /// <summary>
        /// Chains the loose cut edges into closed outlines.
        ///
        /// Each sliced triangle contributes one edge lying on the plane, in no
        /// particular order. Joined end to end they form the outline of the cut
        /// face - one loop for a solid bar, several for a C-channel cut where
        /// its walls are separate, and one with a notch where the cut passes
        /// through a hole.
        /// </summary>
        private static void BuildOutlines(
            List<(Vector3 a, Vector3 b)> segments, List<List<Vector3>> outlines)
        {
            var links = new Dictionary<Vector3Int, List<int>>();

            for (int i = 0; i < segments.Count; i++)
            {
                Link(links, Key(segments[i].a), i);
                Link(links, Key(segments[i].b), i);
            }

            var used = new bool[segments.Count];

            for (int i = 0; i < segments.Count; i++)
            {
                if (used[i])
                {
                    continue;
                }

                var loop = new List<Vector3>();

                used[i] = true;
                loop.Add(segments[i].a);

                Vector3 head = segments[i].b;
                Vector3 start = segments[i].a;

                while (true)
                {
                    loop.Add(head);

                    if (Key(head) == Key(start))
                    {
                        // Closed. The repeated point is dropped, since a loop
                        // is implicitly closed.
                        loop.RemoveAt(loop.Count - 1);
                        break;
                    }

                    int next = FindNext(links, used, segments, head);

                    if (next < 0)
                    {
                        // An open chain. Happens on a mesh that was not closed
                        // to begin with; the cap will be imperfect there, which
                        // is better than refusing to cut.
                        break;
                    }

                    used[next] = true;
                    head = Key(segments[next].a) == Key(head)
                        ? segments[next].b
                        : segments[next].a;
                }

                if (loop.Count >= 3)
                {
                    outlines.Add(loop);
                }
            }
        }

        private static void Link(Dictionary<Vector3Int, List<int>> links, Vector3Int key, int index)
        {
            if (!links.TryGetValue(key, out List<int> list))
            {
                list = new List<int>();
                links[key] = list;
            }

            list.Add(index);
        }

        private static int FindNext(
            Dictionary<Vector3Int, List<int>> links, bool[] used,
            List<(Vector3 a, Vector3 b)> segments, Vector3 at)
        {
            if (!links.TryGetValue(Key(at), out List<int> list))
            {
                return -1;
            }

            foreach (int index in list)
            {
                if (!used[index])
                {
                    return index;
                }
            }

            return -1;
        }

        private static Vector3Int Key(Vector3 point)
        {
            return new Vector3Int(
                Mathf.RoundToInt(point.x / WeldGrid),
                Mathf.RoundToInt(point.y / WeldGrid),
                Mathf.RoundToInt(point.z / WeldGrid));
        }

        /// <summary>
        /// Fills each outline with triangles, so the cut leaves a surface
        /// rather than a hole into the inside of the part.
        /// </summary>
        private static void CapFace(Builder build, List<List<Vector3>> outlines, Plane plane)
        {
            // The new face looks away from the material that was kept.
            Vector3 normal = -plane.normal;

            Vector3 basisU = Vector3.Cross(normal, Vector3.up);

            if (basisU.sqrMagnitude < 1e-8f)
            {
                basisU = Vector3.Cross(normal, Vector3.right);
            }

            basisU.Normalize();
            Vector3 basisV = Vector3.Cross(normal, basisU);

            foreach (List<Vector3> loop in outlines)
            {
                if (loop.Count < 3)
                {
                    continue;
                }

                var flat = new List<Vector2>(loop.Count);

                foreach (Vector3 point in loop)
                {
                    flat.Add(new Vector2(
                        Vector3.Dot(point, basisU), Vector3.Dot(point, basisV)));
                }

                var indices = new List<int>();
                EarClip(flat, indices);

                var mapped = new int[loop.Count];

                for (int i = 0; i < loop.Count; i++)
                {
                    mapped[i] = build.CapVertex(loop[i], normal, flat[i]);
                }

                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    build.Triangle(
                        mapped[indices[i]], mapped[indices[i + 1]], mapped[indices[i + 2]]);
                }
            }
        }

        /// <summary>
        /// Triangulates a simple polygon by repeatedly removing ears.
        ///
        /// Chosen over anything cleverer because a cut cross-section is small -
        /// a C-channel profile is a couple of dozen points - and ear clipping
        /// is the only method simple enough to be obviously correct at that
        /// size. It cannot handle a loop inside another loop, which would be a
        /// hole in the middle of the cut face; that needs a cut running along a
        /// hole rather than across one, which a saw does not do.
        /// </summary>
        private static void EarClip(List<Vector2> polygon, List<int> indices)
        {
            int count = polygon.Count;

            if (count < 3)
            {
                return;
            }

            var remaining = new List<int>(count);

            for (int i = 0; i < count; i++)
            {
                remaining.Add(i);
            }

            // Wound consistently, so "inside" means the same thing throughout.
            if (SignedArea(polygon) < 0f)
            {
                remaining.Reverse();
            }

            int guard = count * count;

            while (remaining.Count > 3 && guard-- > 0)
            {
                bool clipped = false;

                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i - 1 + remaining.Count) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];

                    if (!IsEar(polygon, remaining, previous, current, next))
                    {
                        continue;
                    }

                    indices.Add(previous);
                    indices.Add(current);
                    indices.Add(next);

                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    // Degenerate or self-intersecting. Fanning what is left is
                    // wrong in detail but leaves a surface rather than a hole,
                    // which is the point of the cap.
                    break;
                }
            }

            for (int i = 1; i + 1 < remaining.Count; i++)
            {
                indices.Add(remaining[0]);
                indices.Add(remaining[i]);
                indices.Add(remaining[i + 1]);
            }
        }

        private static bool IsEar(
            List<Vector2> polygon, List<int> remaining, int previous, int current, int next)
        {
            Vector2 a = polygon[previous];
            Vector2 b = polygon[current];
            Vector2 c = polygon[next];

            // Reflex corners are never ears.
            if (Cross(b - a, c - b) <= 0f)
            {
                return false;
            }

            foreach (int index in remaining)
            {
                if (index == previous || index == current || index == next)
                {
                    continue;
                }

                if (InTriangle(polygon[index], a, b, c))
                {
                    return false;
                }
            }

            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => (a.x * b.y) - (a.y * b.x);

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);

            bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
            bool positive = d1 > 0f || d2 > 0f || d3 > 0f;

            return !(negative && positive);
        }

        private static float SignedArea(List<Vector2> polygon)
        {
            float area = 0f;

            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];

                area += (a.x * b.y) - (b.x * a.y);
            }

            return area * 0.5f;
        }

        // ------------------------------------------------------------------
        // Accumulating the result
        // ------------------------------------------------------------------

        private sealed class Builder
        {
            private readonly List<Vector3> positions = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uvs = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();

            private readonly Dictionary<int, int> reused = new Dictionary<int, int>();
            private readonly Dictionary<(int, int), int> crossings =
                new Dictionary<(int, int), int>();

            private readonly bool hasNormals;
            private readonly bool hasUvs;

            public Builder(bool hasNormals, bool hasUvs)
            {
                this.hasNormals = hasNormals;
                this.hasUvs = hasUvs;
            }

            public Vector3 Position(int index) => positions[index];

            public int Vertex(
                Vector3[] sourcePositions, Vector3[] sourceNormals, Vector2[] sourceUvs,
                bool useNormals, bool useUvs, int index)
            {
                if (reused.TryGetValue(index, out int existing))
                {
                    return existing;
                }

                int added = Add(
                    sourcePositions[index],
                    useNormals ? sourceNormals[index] : Vector3.up,
                    useUvs ? sourceUvs[index] : Vector2.zero);

                reused[index] = added;
                return added;
            }

            /// <summary>
            /// The point where an edge meets the plane, interpolated.
            ///
            /// Cached per edge so the two triangles sharing it produce the
            /// same vertex to the last bit - if they did not, the cut outline
            /// would not join up and the cap would have gaps.
            /// </summary>
            public int Crossing(
                Vector3[] sourcePositions, Vector3[] sourceNormals, Vector2[] sourceUvs,
                bool useNormals, bool useUvs, int from, int to, float dFrom, float dTo)
            {
                var key = from < to ? (from, to) : (to, from);

                if (crossings.TryGetValue(key, out int existing))
                {
                    return existing;
                }

                // Interpolated from the lower-numbered end regardless of which
                // way the edge was traversed, so both triangles agree.
                int a = key.Item1;
                int b = key.Item2;

                float da = a == from ? dFrom : dTo;
                float db = b == to ? dTo : dFrom;

                float t = Mathf.Approximately(da - db, 0f) ? 0.5f : da / (da - db);
                t = Mathf.Clamp01(t);

                int added = Add(
                    Vector3.Lerp(sourcePositions[a], sourcePositions[b], t),
                    useNormals
                        ? Vector3.Slerp(sourceNormals[a], sourceNormals[b], t)
                        : Vector3.up,
                    useUvs ? Vector2.Lerp(sourceUvs[a], sourceUvs[b], t) : Vector2.zero);

                crossings[key] = added;
                return added;
            }

            public int CapVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                return Add(position, normal, uv);
            }

            private int Add(Vector3 position, Vector3 normal, Vector2 uv)
            {
                positions.Add(position);

                if (hasNormals)
                {
                    normals.Add(normal.normalized);
                }

                if (hasUvs)
                {
                    uvs.Add(uv);
                }

                return positions.Count - 1;
            }

            public void Triangle(int a, int b, int c)
            {
                if (a == b || b == c || a == c)
                {
                    return;
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };

                // A cut C-channel keeps most of its parent's detail, and the
                // parent already needs 32-bit indices.
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                mesh.SetVertices(positions);

                if (hasNormals && normals.Count == positions.Count)
                {
                    mesh.SetNormals(normals);
                }

                if (hasUvs && uvs.Count == positions.Count)
                {
                    mesh.SetUVs(0, uvs);
                }

                mesh.SetTriangles(triangles, 0);

                if (!hasNormals || normals.Count != positions.Count)
                {
                    mesh.RecalculateNormals();
                }

                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
