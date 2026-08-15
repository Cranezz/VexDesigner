namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// The rounded-square outline drawn around a hole.
    ///
    /// Matched to the real opening rather than drawn as a circle or a plain
    /// square, because the shape is how the user recognises what is being
    /// highlighted. A circle over a square hole reads as an overlay sitting on
    /// top; the right outline reads as the hole itself lighting up.
    ///
    /// Generated in the XY plane facing +Z, so a marker is placed by pointing
    /// its forward along the hole's surface normal.
    /// </summary>
    public static class HoleMarkerMesh
    {
        private static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();

        /// <summary>
        /// VEX holes are square with generously rounded corners - close to a
        /// squircle. As a fraction of the half-width, this is about right by
        /// eye against the real part.
        /// </summary>
        private const float CornerRadiusFraction = 0.32f;

        /// <summary>
        /// Solid rounded square filling a hole of the given width.
        ///
        /// A fill rather than a ring, because the highlight should read as the
        /// hole itself lighting up. An outline reads as something drawn around
        /// the hole, and at a glance it is hard to tell which of two adjacent
        /// holes a ring belongs to.
        /// </summary>
        public static Mesh Filled(float width, HoleShape shape = HoleShape.Square)
        {
            // Shape folded into the key, or a round hole would be handed the
            // square mesh cached for the same width.
            int key = (Mathf.RoundToInt(width * 100000f) * 2) +
                      (shape == HoleShape.Round ? 1 : 0) + 7;

            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            var edge = shape == HoleShape.Round
                ? Circle(width * 0.5f)
                : Profile(width * 0.5f);

            // Triangle fan from the centre. The profile is convex, so a fan is
            // both correct and the cheapest way to fill it.
            var vertices = new Vector3[edge.Count + 1];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < edge.Count; i++)
            {
                vertices[i + 1] = new Vector3(edge[i].x, edge[i].y, 0f);
            }

            var triangles = new int[edge.Count * 3];
            for (int i = 0; i < edge.Count; i++)
            {
                int next = (i + 1) % edge.Count;

                triangles[i * 3] = 0;
                triangles[(i * 3) + 1] = i + 1;
                triangles[(i * 3) + 2] = next + 1;
            }

            var mesh = new Mesh { name = "HoleFill" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Points around a circle of the given radius.
        ///
        /// Enough segments that a hole a quarter of an inch across reads as
        /// round rather than as a polygon, and few enough that a part with a
        /// hundred of them costs nothing.
        /// </summary>
        private static List<Vector2> Circle(float radius)
        {
            const int segments = 28;
            var points = new List<Vector2>(segments);

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                points.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
            }

            return points;
        }

        /// <summary>
        /// Ring outlining a hole of the given width across the flats.
        /// </summary>
        public static Mesh Outline(float width, float lineThickness)
        {
            // Cached by rounded dimensions: a robot has hundreds of identical
            // holes and they should share one mesh.
            int key = (Mathf.RoundToInt(width * 100000f) * 31) +
                      Mathf.RoundToInt(lineThickness * 100000f);

            if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            float half = width * 0.5f;
            var inner = Profile(half);
            var outer = Profile(half + lineThickness);

            var vertices = new Vector3[inner.Count * 2];
            var triangles = new int[inner.Count * 6];

            for (int i = 0; i < inner.Count; i++)
            {
                vertices[i * 2] = new Vector3(inner[i].x, inner[i].y, 0f);
                vertices[(i * 2) + 1] = new Vector3(outer[i].x, outer[i].y, 0f);
            }

            for (int i = 0; i < inner.Count; i++)
            {
                int next = (i + 1) % inner.Count;

                int a = i * 2;
                int b = a + 1;
                int c = next * 2;
                int d = c + 1;

                int t = i * 6;
                triangles[t] = a;
                triangles[t + 1] = b;
                triangles[t + 2] = c;
                triangles[t + 3] = b;
                triangles[t + 4] = d;
                triangles[t + 5] = c;
            }

            var mesh = new Mesh { name = "HoleOutline" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Cache[key] = mesh;
            return mesh;
        }

        /// <summary>
        /// Points around a rounded square of the given half-width, going
        /// anticlockwise from the +X side.
        /// </summary>
        private static List<Vector2> Profile(float half)
        {
            const int cornerSegments = 6;

            float radius = half * CornerRadiusFraction;
            float flat = half - radius;

            var points = new List<Vector2>((cornerSegments + 1) * 4);

            // Four corners, each swept a quarter turn. The straight sections
            // fall out of the gaps between them.
            var centres = new[]
            {
                new Vector2(flat, flat),
                new Vector2(-flat, flat),
                new Vector2(-flat, -flat),
                new Vector2(flat, -flat),
            };

            for (int corner = 0; corner < 4; corner++)
            {
                float start = corner * 90f;

                for (int i = 0; i <= cornerSegments; i++)
                {
                    float angle = (start + (i / (float)cornerSegments * 90f)) * Mathf.Deg2Rad;
                    points.Add(centres[corner] +
                               (new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
                }
            }

            return points;
        }
    }
}
