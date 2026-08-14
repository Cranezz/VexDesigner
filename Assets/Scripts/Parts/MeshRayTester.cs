namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Asks a mesh whether a line of sight is clear, and where it crosses the
    /// surface.
    ///
    /// Unity's own raycasting cannot answer this. A part's collider is a convex
    /// hull, and the hull of a C-channel fills in the channel - so the physics
    /// engine reports metal across the open side where there is nothing, and
    /// reports nothing inside the flanges where there is metal. Both errors
    /// matter here: the first would block a legitimate pick, and the second is
    /// what let holes on the far side of a part be clicked straight through it.
    ///
    /// A non-convex MeshCollider would be exact, but Unity does not allow one on
    /// a moving rigidbody, and parts move constantly.
    ///
    /// So the triangles are tested directly, against a uniform grid built once
    /// per mesh and shared by every copy of that part. Queries walk only the
    /// cells the line actually enters, which for a long part is a handful out of
    /// thousands.
    /// </summary>
    public sealed class MeshRayTester
    {
        /// <summary>
        /// Target triangles per cell. Low enough that a query tests a few dozen
        /// triangles rather than thousands, high enough that the grid itself
        /// does not dominate memory.
        /// </summary>
        private const int TrianglesPerCell = 6;

        private const int MaxCellsPerAxis = 64;

        private static readonly Dictionary<Mesh, MeshRayTester> Cache =
            new Dictionary<Mesh, MeshRayTester>();

        private readonly Vector3[] vertices;
        private readonly int[] indices;

        private readonly Bounds bounds;
        private readonly Vector3 cellSize;
        private readonly Vector3Int counts;

        /// <summary>
        /// Triangle lists per cell, in compressed form: <see cref="cellStart"/>
        /// indexes into <see cref="cellItems"/>. One flat array beats tens of
        /// thousands of small lists, both to build and to walk.
        /// </summary>
        private readonly int[] cellStart;
        private readonly int[] cellItems;

        /// <summary>
        /// Query number a triangle was last tested on, so a triangle spanning
        /// several cells is only tested once per query.
        /// </summary>
        private readonly int[] stamp;

        private int query;

        public static MeshRayTester For(Mesh mesh)
        {
            if (mesh == null)
            {
                return null;
            }

            if (Cache.TryGetValue(mesh, out MeshRayTester existing))
            {
                return existing;
            }

            if (!mesh.isReadable)
            {
                Debug.LogWarning(
                    $"[Parts] Mesh '{mesh.name}' is not readable, so line of " +
                    "sight cannot be tested against it. Check the part import " +
                    "settings.");

                Cache[mesh] = null;
                return null;
            }

            var tester = new MeshRayTester(mesh);
            Cache[mesh] = tester;
            return tester;
        }

        private MeshRayTester(Mesh mesh)
        {
            vertices = mesh.vertices;
            indices = mesh.triangles;
            bounds = mesh.bounds;

            int triangles = indices.Length / 3;
            stamp = new int[triangles];

            // Cell size from the triangle count and the volume, so a dense part
            // gets a fine grid and a simple one does not pay for cells it has
            // nothing to put in.
            Vector3 size = Vector3.Max(bounds.size, Vector3.one * 1e-4f);
            float volume = size.x * size.y * size.z;
            float target = Mathf.Pow(
                volume * TrianglesPerCell / Mathf.Max(1, triangles), 1f / 3f);

            counts = new Vector3Int(
                Mathf.Clamp(Mathf.CeilToInt(size.x / target), 1, MaxCellsPerAxis),
                Mathf.Clamp(Mathf.CeilToInt(size.y / target), 1, MaxCellsPerAxis),
                Mathf.Clamp(Mathf.CeilToInt(size.z / target), 1, MaxCellsPerAxis));

            cellSize = new Vector3(
                size.x / counts.x, size.y / counts.y, size.z / counts.z);

            int cells = counts.x * counts.y * counts.z;

            // Counting pass, then a fill pass. Building the exact array up front
            // avoids growing thousands of lists and the garbage that makes.
            var tally = new int[cells + 1];

            for (int t = 0; t < triangles; t++)
            {
                Span(t, out Vector3Int lo, out Vector3Int hi);

                for (int z = lo.z; z <= hi.z; z++)
                {
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        for (int x = lo.x; x <= hi.x; x++)
                        {
                            tally[Index(x, y, z) + 1]++;
                        }
                    }
                }
            }

            for (int i = 1; i <= cells; i++)
            {
                tally[i] += tally[i - 1];
            }

            cellStart = tally;
            cellItems = new int[cellStart[cells]];

            var cursor = new int[cells];

            for (int t = 0; t < triangles; t++)
            {
                Span(t, out Vector3Int lo, out Vector3Int hi);

                for (int z = lo.z; z <= hi.z; z++)
                {
                    for (int y = lo.y; y <= hi.y; y++)
                    {
                        for (int x = lo.x; x <= hi.x; x++)
                        {
                            int cell = Index(x, y, z);
                            cellItems[cellStart[cell] + cursor[cell]] = t;
                            cursor[cell]++;
                        }
                    }
                }
            }
        }

        private void Span(int triangle, out Vector3Int lo, out Vector3Int hi)
        {
            int i = triangle * 3;
            Vector3 a = vertices[indices[i]];
            Vector3 b = vertices[indices[i + 1]];
            Vector3 c = vertices[indices[i + 2]];

            lo = Cell(Vector3.Min(a, Vector3.Min(b, c)));
            hi = Cell(Vector3.Max(a, Vector3.Max(b, c)));
        }

        private Vector3Int Cell(Vector3 point)
        {
            Vector3 local = point - bounds.min;

            return new Vector3Int(
                Mathf.Clamp(Mathf.FloorToInt(local.x / cellSize.x), 0, counts.x - 1),
                Mathf.Clamp(Mathf.FloorToInt(local.y / cellSize.y), 0, counts.y - 1),
                Mathf.Clamp(Mathf.FloorToInt(local.z / cellSize.z), 0, counts.z - 1));
        }

        private int Index(int x, int y, int z) =>
            x + (counts.x * (y + (counts.y * z)));

        /// <summary>
        /// True if solid material stands between <paramref name="from"/> and
        /// <paramref name="to"/>, both in the mesh's local space.
        ///
        /// <paramref name="skin"/> pulls the far end back along the line, so a
        /// test that ends *on* a surface does not report that surface as
        /// blocking itself - which is the normal case here, since the points
        /// being tested are hole openings lying exactly on the metal.
        /// </summary>
        public bool SegmentBlocked(Vector3 from, Vector3 to, float skin)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;

            if (length <= skin)
            {
                return false;
            }

            Vector3 direction = delta / length;
            float limit = length - skin;

            return FirstCrossing(from, direction, limit, out _);
        }

        /// <summary>
        /// Distance along the ray to the first surface, if any, within
        /// <paramref name="maxDistance"/>.
        /// </summary>
        public bool FirstCrossing(
            Vector3 origin, Vector3 direction, float maxDistance, out float distance)
        {
            distance = 0f;

            // Trim to the part's own bounds first. A ray from the player's eye
            // is usually metres away from a part a few inches across, and
            // walking empty cells to reach it would be the whole cost.
            if (!ClipToBounds(origin, direction, maxDistance, out float enter, out float exit))
            {
                return false;
            }

            query++;

            Vector3 start = origin + (direction * enter);
            Vector3Int cell = Cell(start);

            // Amanatides and Woo: step whichever axis reaches its next cell
            // boundary soonest, so every cell the line passes through is
            // visited exactly once and none that it misses.
            var step = new Vector3Int(
                direction.x > 0f ? 1 : -1,
                direction.y > 0f ? 1 : -1,
                direction.z > 0f ? 1 : -1);

            Vector3 nextBoundary = new Vector3(
                bounds.min.x + ((cell.x + (step.x > 0 ? 1 : 0)) * cellSize.x),
                bounds.min.y + ((cell.y + (step.y > 0 ? 1 : 0)) * cellSize.y),
                bounds.min.z + ((cell.z + (step.z > 0 ? 1 : 0)) * cellSize.z));

            Vector3 travel = new Vector3(
                Distance(nextBoundary.x - start.x, direction.x),
                Distance(nextBoundary.y - start.y, direction.y),
                Distance(nextBoundary.z - start.z, direction.z));

            Vector3 crossing = new Vector3(
                Distance(cellSize.x, Mathf.Abs(direction.x)),
                Distance(cellSize.y, Mathf.Abs(direction.y)),
                Distance(cellSize.z, Mathf.Abs(direction.z)));

            float end = Mathf.Min(exit, maxDistance);
            float best = float.MaxValue;

            while (true)
            {
                if (TestCell(cell, origin, direction, end, ref best))
                {
                    // A nearer hit cannot be hiding in a later cell, because
                    // cells are visited in order along the line.
                    distance = best;
                    return true;
                }

                // Advance into the next cell along whichever axis is closest.
                if (travel.x < travel.y && travel.x < travel.z)
                {
                    if (travel.x > end) { break; }
                    cell.x += step.x;
                    if (cell.x < 0 || cell.x >= counts.x) { break; }
                    travel.x += crossing.x;
                }
                else if (travel.y < travel.z)
                {
                    if (travel.y > end) { break; }
                    cell.y += step.y;
                    if (cell.y < 0 || cell.y >= counts.y) { break; }
                    travel.y += crossing.y;
                }
                else
                {
                    if (travel.z > end) { break; }
                    cell.z += step.z;
                    if (cell.z < 0 || cell.z >= counts.z) { break; }
                    travel.z += crossing.z;
                }
            }

            return false;
        }

        private static float Distance(float gap, float rate)
        {
            // A ray running exactly parallel to an axis never crosses that
            // axis's boundaries, which is infinity rather than a divide by zero.
            return Mathf.Abs(rate) < 1e-9f ? float.MaxValue : Mathf.Abs(gap / rate);
        }

        private bool TestCell(
            Vector3Int cell, Vector3 origin, Vector3 direction, float limit, ref float best)
        {
            int index = Index(cell.x, cell.y, cell.z);
            int from = cellStart[index];
            int to = cellStart[index + 1];

            bool hit = false;

            for (int i = from; i < to; i++)
            {
                int triangle = cellItems[i];

                // Triangles straddle cells; without this a large one would be
                // retested in every cell it touches.
                if (stamp[triangle] == query)
                {
                    continue;
                }

                stamp[triangle] = query;

                int v = triangle * 3;

                if (RayTriangle(origin, direction,
                        vertices[indices[v]],
                        vertices[indices[v + 1]],
                        vertices[indices[v + 2]],
                        out float t) && t > 1e-6f && t <= limit && t < best)
                {
                    best = t;
                    hit = true;
                }
            }

            return hit;
        }

        private bool ClipToBounds(
            Vector3 origin, Vector3 direction, float maxDistance,
            out float enter, out float exit)
        {
            enter = 0f;
            exit = maxDistance;

            for (int axis = 0; axis < 3; axis++)
            {
                float d = direction[axis];
                float o = origin[axis];
                float lo = bounds.min[axis];
                float hi = bounds.max[axis];

                if (Mathf.Abs(d) < 1e-9f)
                {
                    if (o < lo || o > hi)
                    {
                        return false;
                    }

                    continue;
                }

                float t0 = (lo - o) / d;
                float t1 = (hi - o) / d;

                if (t0 > t1)
                {
                    (t0, t1) = (t1, t0);
                }

                enter = Mathf.Max(enter, t0);
                exit = Mathf.Min(exit, t1);

                if (enter > exit)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Möller-Trumbore, two-sided. Two-sided matters: a part's inside
        /// surface is as much of an obstruction as its outside, and back-face
        /// culling would let a line of sight pass out through the far wall.
        /// </summary>
        private static bool RayTriangle(
            Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;

            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);

            if (Mathf.Abs(determinant) < 1e-12f)
            {
                return false;
            }

            float inverse = 1f / determinant;
            Vector3 s = origin - a;

            float u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, ab);
            float v = Vector3.Dot(direction, q) * inverse;

            if (v < 0f || u + v > 1f)
            {
                return false;
            }

            t = Vector3.Dot(ac, q) * inverse;
            return t > 0f;
        }
    }
}
