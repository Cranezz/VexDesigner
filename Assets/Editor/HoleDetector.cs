namespace VexDesigner.EditorTools
{
    using System.Collections.Generic;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Finds the screw holes in a part's mesh by shooting rays through it.
    ///
    /// A hole is somewhere a ray passes clean through while every ray around it
    /// hits metal. That is very nearly the definition of a hole, and it assumes
    /// nothing about where holes ought to be - so it works on an irregular
    /// bracket as well as on a C-channel, and it can never report a hole on a
    /// solid part.
    ///
    /// An earlier version rasterised each flat face and looked for enclosed
    /// gaps. It failed on exactly the part it was written for: the holes in a
    /// C-channel's flange run into the bend at the top, so within the flat part
    /// of that face the gap reaches the edge and reads as "outside the part"
    /// rather than as a hole. Rays do not care that the surrounding metal
    /// curves away; they only care that it is there.
    ///
    /// Both faces of every hole come out of the same pass, taken from where the
    /// neighbouring rays entered and left the material - so there is no
    /// separate step to pair the two sides and no chance of pairing them wrong.
    /// </summary>
    public static class HoleDetector
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>
        /// Ray spacing. A VEX hole is around a quarter inch, so this puts about
        /// twenty rays across one - enough to place its centre well within a
        /// thousandth of an inch.
        /// </summary>
        private const float SampleInches = 0.012f;

        private const float MinHoleWidthInches = 0.08f;
        private const float MaxHoleWidthInches = 0.60f;

        /// <summary>Surfaces smaller than this are fillets and trim.</summary>
        private const float MinFaceAreaSquareInches = 0.5f;

        /// <summary>Per-axis diagnostics.</summary>
        public static bool Verbose;

        public struct Result
        {
            public HoleSet Holes;
            public int AxisCount;
            public int RejectedCount;
            public int MergedCount;
            public string Summary;
        }

        public static Result Detect(Mesh mesh, float declaredPitchInches)
        {
            var result = new Result { Holes = new HoleSet() };

            if (mesh == null || !mesh.isReadable)
            {
                result.Summary = "Mesh missing or not readable.";
                return result;
            }

            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;

            List<Vector3> axes = FindAxes(vertices, indices);
            result.AxisCount = axes.Count;

            var holes = new List<Hole>();
            int rejected = 0;

            foreach (Vector3 axis in axes)
            {
                holes.AddRange(ScanAxis(mesh, vertices, indices, axis, ref rejected));
            }

            result.RejectedCount = rejected;

            int before = holes.Count;
            holes = Merge(holes);
            result.MergedCount = before - holes.Count;

            holes.Sort((a, b) => a.LocalCentre.sqrMagnitude.CompareTo(b.LocalCentre.sqrMagnitude));

            if (Verbose)
            {
                LogSpacingHistogram(holes);
            }

            result.Holes.holes = holes.ToArray();
            result.Holes.measuredPitchInches = MeasurePitch(holes);
            result.Holes.generatedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            result.Summary = Describe(result, declaredPitchInches);

            return result;
        }

        // ------------------------------------------------------------------
        // Which directions to look along
        // ------------------------------------------------------------------

        /// <summary>
        /// The distinct directions the part's large flat surfaces face.
        ///
        /// Holes are drilled perpendicular to a surface, so these are the only
        /// directions worth scanning. Opposite pairs are merged - one pass
        /// along an axis sees both sides of the material anyway.
        /// </summary>
        private static List<Vector3> FindAxes(Vector3[] vertices, int[] indices)
        {
            var normals = new List<Vector3>();
            var areas = new List<float>();

            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];

                Vector3 cross = Vector3.Cross(b - a, c - a);
                float area = cross.magnitude * 0.5f;

                if (area < 1e-12f)
                {
                    continue;
                }

                Vector3 normal = cross.normalized;

                int match = -1;
                for (int j = 0; j < normals.Count; j++)
                {
                    // Absolute dot, so a surface and the one facing back at it
                    // count as the same axis.
                    if (Mathf.Abs(Vector3.Dot(normals[j], normal)) > 0.999f)
                    {
                        match = j;
                        break;
                    }
                }

                if (match < 0)
                {
                    normals.Add(normal);
                    areas.Add(area);
                }
                else
                {
                    areas[match] += area;
                }
            }

            float minArea = MinFaceAreaSquareInches * InchesToMetres * InchesToMetres;

            // Relative to the largest surface as well as absolute.
            //
            // A bend is tessellated into strips, and on a part as long as a
            // C-channel each strip accumulates enough area to look like a real
            // face on its own - which produced six scan axes at eleven-degree
            // intervals, all finding the same holes over again. A true flat
            // face is a large fraction of the biggest one; a facet of a curve
            // is not.
            float largest = 0f;
            foreach (float area in areas)
            {
                largest = Mathf.Max(largest, area);
            }

            float threshold = Mathf.Max(minArea, largest * 0.3f);
            var axes = new List<Vector3>();

            for (int i = 0; i < normals.Count; i++)
            {
                if (areas[i] >= threshold)
                {
                    axes.Add(normals[i]);
                }
            }

            return axes;
        }

        // ------------------------------------------------------------------
        // Scan one axis
        // ------------------------------------------------------------------

        private static IEnumerable<Hole> ScanAxis(
            Mesh mesh, Vector3[] vertices, int[] indices, Vector3 axis, ref int rejected)
        {
            var holes = new List<Hole>();

            Vector3 u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-6f)
            {
                u = Vector3.Cross(axis, Vector3.forward);
            }

            u.Normalize();
            Vector3 v = Vector3.Cross(axis, u).normalized;

            Bounds bounds = mesh.bounds;
            float extent = bounds.size.magnitude;
            Vector3 centre = bounds.center;

            float halfU = 0f;
            float halfV = 0f;
            foreach (Vector3 vertex in vertices)
            {
                Vector3 offset = vertex - centre;
                halfU = Mathf.Max(halfU, Mathf.Abs(Vector3.Dot(offset, u)));
                halfV = Mathf.Max(halfV, Mathf.Abs(Vector3.Dot(offset, v)));
            }

            float step = SampleInches * InchesToMetres;

            // Margin on every side, so the open air around the part is inside
            // the grid. The flood fill needs that to tell air from a hole.
            int width = Mathf.CeilToInt((halfU * 2f) / step) + 6;
            int height = Mathf.CeilToInt((halfV * 2f) / step) + 6;

            if (width < 5 || height < 5 || (long)width * height > 20_000_000L)
            {
                return holes;
            }

            var grid = new TriangleGrid(vertices, indices, centre, u, v, halfU, halfV, step);

            var hit = new bool[width * height];
            var segments = new List<Vector2>[width * height];

            float start = -extent;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float du = (x - (width * 0.5f)) * step;
                    float dv = (y - (height * 0.5f)) * step;

                    Vector3 origin = centre + (u * du) + (v * dv) + (axis * start);

                    int index = (y * width) + x;
                    segments[index] = grid.Cast(origin, axis, du, dv);
                    hit[index] = segments[index].Count > 0;
                }
            }

            holes.AddRange(ExtractHoles(
                hit, segments, width, height, step, centre, u, v, axis, start, ref rejected));

            if (Verbose)
            {
                int solid = 0;
                foreach (bool h in hit)
                {
                    if (h) { solid++; }
                }

                Debug.Log(
                    $"[Holes]   axis={axis} grid={width}x{height} " +
                    $"solid={solid * 100f / hit.Length:F1}% holes={holes.Count}");
            }

            return holes;
        }

        private static IEnumerable<Hole> ExtractHoles(
            bool[] hit, List<Vector2>[] segments, int width, int height, float step,
            Vector3 centre, Vector3 u, Vector3 v, Vector3 axis, float start,
            ref int rejected)
        {
            var holes = new List<Hole>();
            var visited = new bool[hit.Length];
            var queue = new Queue<int>();

            float minWidth = MinHoleWidthInches * InchesToMetres;
            float maxWidth = MaxHoleWidthInches * InchesToMetres;

            for (int seed = 0; seed < hit.Length; seed++)
            {
                if (hit[seed] || visited[seed])
                {
                    continue;
                }

                queue.Clear();
                queue.Enqueue(seed);
                visited[seed] = true;

                var cells = new List<int>();
                bool touchesBorder = false;

                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    cells.Add(index);

                    int x = index % width;
                    int y = index / width;

                    // Reaching the edge of the grid means this gap is the open
                    // air around the part, not a hole through it.
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                    {
                        touchesBorder = true;
                    }

                    Enqueue(x - 1, y, width, height, hit, visited, queue);
                    Enqueue(x + 1, y, width, height, hit, visited, queue);
                    Enqueue(x, y - 1, width, height, hit, visited, queue);
                    Enqueue(x, y + 1, width, height, hit, visited, queue);
                }

                if (touchesBorder)
                {
                    continue;
                }

                float sumU = 0f, sumV = 0f;
                float minU = float.MaxValue, maxU = float.MinValue;
                float minV = float.MaxValue, maxV = float.MinValue;

                foreach (int index in cells)
                {
                    float du = ((index % width) - (width * 0.5f)) * step;
                    float dv = ((index / width) - (height * 0.5f)) * step;

                    sumU += du;
                    sumV += dv;
                    minU = Mathf.Min(minU, du); maxU = Mathf.Max(maxU, du);
                    minV = Mathf.Min(minV, dv); maxV = Mathf.Max(maxV, dv);
                }

                float openingWidth = Mathf.Max(maxU - minU, maxV - minV) + step;

                if (openingWidth < minWidth || openingWidth > maxWidth)
                {
                    rejected++;
                    continue;
                }

                // Each wall the surrounding rays pass through is a separate
                // hole.
                //
                // A ray down the axis of a C-channel goes through the near
                // flange and the far one, because their holes line up. Treating
                // that as a single hole would give one opening two inches deep
                // instead of two holes an eighth of an inch deep, and a screw
                // would have nothing sensible to seat against.
                List<Vector2> walls = SurroundingWalls(cells, hit, segments, width, height);

                if (walls.Count == 0)
                {
                    rejected++;
                    continue;
                }

                Vector3 axisCentre = centre +
                                     (u * (sumU / cells.Count)) +
                                     (v * (sumV / cells.Count));

                foreach (Vector2 wall in walls)
                {
                    holes.Add(new Hole
                    {
                        // The rays met the near side first, so its outward
                        // normal points back along the ray.
                        front = new HoleFace
                        {
                            localPosition = axisCentre + (axis * (start + wall.x)),
                            localNormal = -axis,
                            width = openingWidth,
                        },
                        back = new HoleFace
                        {
                            localPosition = axisCentre + (axis * (start + wall.y)),
                            localNormal = axis,
                            width = openingWidth,
                        },
                        depth = Mathf.Abs(wall.y - wall.x),
                    });
                }
            }

            return holes;
        }

        /// <summary>
        /// The walls the rays around a hole pass through, as (near, far) pairs.
        ///
        /// Taken from the neighbours because the rays through the hole itself
        /// hit nothing by definition. The number of walls is the value most of
        /// them agree on rather than the largest seen: a hole beside a bend has
        /// the odd neighbour clipping extra material, and that neighbour should
        /// not invent a wall the hole does not actually pass through.
        /// </summary>
        private static List<Vector2> SurroundingWalls(
            List<int> cells, bool[] hit, List<Vector2>[] segments,
            int width, int height)
        {
            var neighbours = new List<List<Vector2>>();
            var seen = new HashSet<int>();

            foreach (int index in cells)
            {
                int x = index % width;
                int y = index / width;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        int n = (ny * width) + nx;
                        if (!hit[n] || !seen.Add(n))
                        {
                            continue;
                        }

                        neighbours.Add(segments[n]);
                    }
                }
            }

            var walls = new List<Vector2>();
            if (neighbours.Count == 0)
            {
                return walls;
            }

            // Most common wall count among the neighbours.
            var tally = new Dictionary<int, int>();
            foreach (List<Vector2> list in neighbours)
            {
                tally.TryGetValue(list.Count, out int count);
                tally[list.Count] = count + 1;
            }

            int wallCount = 0;
            int best = 0;
            foreach (KeyValuePair<int, int> pair in tally)
            {
                if (pair.Value > best)
                {
                    best = pair.Value;
                    wallCount = pair.Key;
                }
            }

            for (int w = 0; w < wallCount; w++)
            {
                var nears = new List<float>();
                var fars = new List<float>();

                foreach (List<Vector2> list in neighbours)
                {
                    if (list.Count == wallCount)
                    {
                        nears.Add(list[w].x);
                        fars.Add(list[w].y);
                    }
                }

                if (nears.Count == 0)
                {
                    continue;
                }

                nears.Sort();
                fars.Sort();
                walls.Add(new Vector2(nears[nears.Count / 2], fars[fars.Count / 2]));
            }

            return walls;
        }

        private static void Enqueue(
            int x, int y, int width, int height, bool[] hit, bool[] visited, Queue<int> queue)
        {
            if (x < 0 || y < 0 || x >= width || y >= height)
            {
                return;
            }

            int index = (y * width) + x;
            if (hit[index] || visited[index])
            {
                return;
            }

            visited[index] = true;
            queue.Enqueue(index);
        }

        // ------------------------------------------------------------------
        // Ray casting
        // ------------------------------------------------------------------

        /// <summary>
        /// Triangles bucketed by their footprint in the scan plane.
        ///
        /// Without it every ray would be tested against every triangle -
        /// 120,000 rays against 13,000 triangles is over a billion tests per
        /// axis. Bucketing brings each ray down to the handful it could meet.
        /// </summary>
        private sealed class TriangleGrid
        {
            private readonly Vector3[] vertices;
            private readonly int[] indices;
            private readonly Vector3 origin;
            private readonly Vector3 u;
            private readonly Vector3 v;
            private readonly float cell;
            private readonly int columns;
            private readonly int rows;
            private readonly float halfU;
            private readonly float halfV;
            private readonly List<int>[] buckets;

            public TriangleGrid(
                Vector3[] vertices, int[] indices, Vector3 origin,
                Vector3 u, Vector3 v, float halfU, float halfV, float step)
            {
                this.vertices = vertices;
                this.indices = indices;
                this.origin = origin;
                this.u = u;
                this.v = v;
                this.halfU = halfU;
                this.halfV = halfV;

                // Buckets much coarser than the ray spacing; fine buckets cost
                // more to fill than they save on lookup.
                cell = step * 12f;
                columns = Mathf.Max(1, Mathf.CeilToInt((halfU * 2f) / cell) + 4);
                rows = Mathf.Max(1, Mathf.CeilToInt((halfV * 2f) / cell) + 4);

                buckets = new List<int>[columns * rows];

                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    Vector2 a = Flatten(vertices[indices[i]]);
                    Vector2 b = Flatten(vertices[indices[i + 1]]);
                    Vector2 c = Flatten(vertices[indices[i + 2]]);

                    Vector2 lo = Vector2.Min(a, Vector2.Min(b, c));
                    Vector2 hi = Vector2.Max(a, Vector2.Max(b, c));

                    int x0 = Column(lo.x), x1 = Column(hi.x);
                    int y0 = Row(lo.y), y1 = Row(hi.y);

                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++)
                        {
                            int index = (y * columns) + x;
                            buckets[index] ??= new List<int>();
                            buckets[index].Add(i);
                        }
                    }
                }
            }

            /// <summary>
            /// Every stretch of material the ray passes through, as (enter,
            /// leave) pairs along the axis. Empty means the ray missed the part
            /// entirely - which is what a hole looks like.
            /// </summary>
            public List<Vector2> Cast(Vector3 rayOrigin, Vector3 axis, float du, float dv)
            {
                var segments = new List<Vector2>();

                List<int> bucket = buckets[(Row(dv) * columns) + Column(du)];
                if (bucket == null)
                {
                    return segments;
                }

                var hits = new List<float>();

                foreach (int i in bucket)
                {
                    if (RayTriangle(rayOrigin, axis,
                            vertices[indices[i]],
                            vertices[indices[i + 1]],
                            vertices[indices[i + 2]],
                            out float t))
                    {
                        hits.Add(t);
                    }
                }

                if (hits.Count < 2)
                {
                    return segments;
                }

                hits.Sort();

                // Merge hits that are essentially the same point. A ray
                // crossing a shared edge meets both triangles, and that pair
                // would otherwise read as a zero-thickness wall.
                const float weld = 1e-5f;
                var distinct = new List<float> { hits[0] };

                for (int i = 1; i < hits.Count; i++)
                {
                    if (hits[i] - distinct[distinct.Count - 1] > weld)
                    {
                        distinct.Add(hits[i]);
                    }
                }

                // A closed surface is entered and left in pairs.
                for (int i = 0; i + 1 < distinct.Count; i += 2)
                {
                    segments.Add(new Vector2(distinct[i], distinct[i + 1]));
                }

                return segments;
            }

            private Vector2 Flatten(Vector3 point)
            {
                Vector3 offset = point - origin;
                return new Vector2(Vector3.Dot(offset, u), Vector3.Dot(offset, v));
            }

            private int Column(float du) =>
                Mathf.Clamp(Mathf.FloorToInt((du + halfU) / cell) + 2, 0, columns - 1);

            private int Row(float dv) =>
                Mathf.Clamp(Mathf.FloorToInt((dv + halfV) / cell) + 2, 0, rows - 1);
        }

        /// <summary>Moller-Trumbore, double sided.</summary>
        private static bool RayTriangle(
            Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            const float epsilon = 1e-10f;
            t = 0f;

            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 h = Vector3.Cross(direction, edge2);
            float det = Vector3.Dot(edge1, h);

            // Only rays parallel to the triangle are rejected. A back face is
            // as much of a surface as a front one, and the exit wall of a sheet
            // is always a back face.
            if (Mathf.Abs(det) < epsilon)
            {
                return false;
            }

            float inverse = 1f / det;
            Vector3 s = origin - a;
            float bu = Vector3.Dot(s, h) * inverse;

            if (bu < 0f || bu > 1f)
            {
                return false;
            }

            Vector3 q = Vector3.Cross(s, edge1);
            float bv = Vector3.Dot(direction, q) * inverse;

            if (bv < 0f || bu + bv > 1f)
            {
                return false;
            }

            t = Vector3.Dot(edge2, q) * inverse;
            return t > epsilon;
        }

        /// <summary>
        /// Collapses holes that are really the same hole found twice.
        ///
        /// Two axes can both see through the same opening where surfaces meet,
        /// and a single hole can be split into two regions when a stray ray
        /// through it clips a corner. Both show up as duplicates far closer
        /// together than any real pair of holes - VEX holes are half an inch
        /// apart, so anything within a tenth of an inch is the same hole.
        /// </summary>
        private static List<Hole> Merge(List<Hole> holes)
        {
            const float mergeInches = 0.12f;
            float threshold = mergeInches * InchesToMetres;

            var kept = new List<Hole>();

            foreach (Hole hole in holes)
            {
                bool duplicate = false;

                for (int i = 0; i < kept.Count; i++)
                {
                    if (Vector3.Distance(kept[i].LocalCentre, hole.LocalCentre) > threshold)
                    {
                        continue;
                    }

                    // Same place, same direction: the same hole.
                    if (Mathf.Abs(Vector3.Dot(kept[i].LocalAxis, hole.LocalAxis)) < 0.9f)
                    {
                        continue;
                    }

                    // Keep whichever reads wider; a split fragment measures
                    // narrower than the whole opening.
                    if (hole.front.width > kept[i].front.width)
                    {
                        kept[i] = hole;
                    }

                    duplicate = true;
                    break;
                }

                if (!duplicate)
                {
                    kept.Add(hole);
                }
            }

            return kept;
        }

        /// <summary>
        /// Distribution of nearest-neighbour distances. A clean detection is a
        /// single spike at the hole pitch; anything else says what went wrong.
        /// </summary>
        private static void LogSpacingHistogram(List<Hole> holes)
        {
            if (holes.Count < 2)
            {
                return;
            }

            var buckets = new SortedDictionary<int, int>();

            for (int i = 0; i < holes.Count; i++)
            {
                float best = float.MaxValue;

                for (int j = 0; j < holes.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    best = Mathf.Min(
                        best, Vector3.Distance(holes[i].LocalCentre, holes[j].LocalCentre));
                }

                int bucket = Mathf.RoundToInt(best / InchesToMetres * 20f);
                buckets.TryGetValue(bucket, out int count);
                buckets[bucket] = count + 1;
            }

            var text = new System.Text.StringBuilder("[Holes]   spacing histogram: ");
            foreach (KeyValuePair<int, int> pair in buckets)
            {
                text.Append($"{pair.Key / 20f:F2}in x{pair.Value}  ");
            }

            Debug.Log(text.ToString());
        }

        // ------------------------------------------------------------------
        // Checks
        // ------------------------------------------------------------------

        private static float MeasurePitch(List<Hole> holes)
        {
            if (holes.Count < 2)
            {
                return 0f;
            }

            var nearest = new List<float>(holes.Count);

            for (int i = 0; i < holes.Count; i++)
            {
                float best = float.MaxValue;

                for (int j = 0; j < holes.Count; j++)
                {
                    if (i == j)
                    {
                        continue;
                    }

                    float d = Vector3.Distance(holes[i].LocalCentre, holes[j].LocalCentre);
                    if (d < best)
                    {
                        best = d;
                    }
                }

                if (best < float.MaxValue)
                {
                    nearest.Add(best);
                }
            }

            nearest.Sort();
            return nearest[nearest.Count / 2] / InchesToMetres;
        }

        private static string Describe(Result result, float declaredPitchInches)
        {
            float measured = result.Holes.measuredPitchInches;
            string pitch = measured > 0f
                ? $"{measured:F3} in (declared {declaredPitchInches:F3})"
                : "n/a";

            // VEX lays holes out staggered, not in a square grid: alternate
            // rows are offset by half a pitch. The nearest neighbour is then
            // the diagonal, at pitch/sqrt(2) - about 0.354 in for a half-inch
            // pitch. Checking only against the pitch itself reports a perfectly
            // correct detection as broken, which is exactly what it did.
            float staggered = declaredPitchInches / Mathf.Sqrt(2f);

            bool aligned = Mathf.Abs(measured - declaredPitchInches) < 0.02f;
            bool offset = Mathf.Abs(measured - staggered) < 0.02f;

            if (measured > 0f)
            {
                pitch += offset ? "  staggered rows" : aligned ? "  aligned rows" : string.Empty;
            }

            string warning = string.Empty;
            if (measured > 0f && !aligned && !offset)
            {
                warning = $"\nSpacing matches neither the pitch ({declaredPitchInches:F3} in) " +
                          $"nor a staggered layout ({staggered:F3} in). Either detection is " +
                          "finding something other than holes, or the import scale is wrong.";
            }

            return $"{result.Holes.Count} holes across {result.AxisCount} axes " +
                   $"({result.RejectedCount} openings rejected by size).\n" +
                   $"Spacing: {pitch}{warning}";
        }
    }
}
