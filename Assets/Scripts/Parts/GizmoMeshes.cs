namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Procedural meshes for the transform gizmo.
    ///
    /// Built in code because a cone and a torus are a dozen lines of loops
    /// each, and an art dependency for two primitives would be silly. Both are
    /// generated once and shared across every handle.
    ///
    /// Meshes are built along +Y and rotated into place by the handle, so one
    /// mesh serves all three axes.
    /// </summary>
    public static class GizmoMeshes
    {
        private static Mesh cone;
        private static Mesh torus;
        private static Mesh shaft;

        /// <summary>Unit cone: base at origin, tip at +Y, radius 0.5.</summary>
        public static Mesh Cone()
        {
            if (cone != null)
            {
                return cone;
            }

            const int segments = 16;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            // Tip, then base centre, then the base ring.
            vertices.Add(new Vector3(0f, 1f, 0f));
            vertices.Add(Vector3.zero);

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = 2 + i;
                int b = 2 + ((i + 1) % segments);

                triangles.Add(0); triangles.Add(b); triangles.Add(a);   // side
                triangles.Add(1); triangles.Add(a); triangles.Add(b);   // base
            }

            cone = Build("GizmoCone", vertices, triangles);
            return cone;
        }

        /// <summary>Unit cylinder along +Y, from 0 to 1, radius 0.5.</summary>
        public static Mesh Shaft()
        {
            if (shaft != null)
            {
                return shaft;
            }

            const int segments = 12;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * 0.5f;
                float z = Mathf.Sin(angle) * 0.5f;
                vertices.Add(new Vector3(x, 0f, z));
                vertices.Add(new Vector3(x, 1f, z));
            }

            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = ((i + 1) % segments) * 2;
                int d = c + 1;

                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(b); triangles.Add(d); triangles.Add(c);
            }

            shaft = Build("GizmoShaft", vertices, triangles);
            return shaft;
        }

        /// <summary>
        /// Torus in the XZ plane, ring radius 1, tube radius 0.02. Used for the
        /// rotation handles, one per axis.
        /// </summary>
        public static Mesh Torus()
        {
            if (torus != null)
            {
                return torus;
            }

            const int ringSegments = 48;
            const int tubeSegments = 8;
            const float tubeRadius = 0.02f;

            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            for (int i = 0; i < ringSegments; i++)
            {
                float u = (i / (float)ringSegments) * Mathf.PI * 2f;
                Vector3 centre = new Vector3(Mathf.Cos(u), 0f, Mathf.Sin(u));
                Vector3 outward = centre.normalized;

                for (int j = 0; j < tubeSegments; j++)
                {
                    float v = (j / (float)tubeSegments) * Mathf.PI * 2f;
                    Vector3 offset =
                        (outward * (Mathf.Cos(v) * tubeRadius)) +
                        (Vector3.up * (Mathf.Sin(v) * tubeRadius));

                    vertices.Add(centre + offset);
                }
            }

            for (int i = 0; i < ringSegments; i++)
            {
                int nextRing = ((i + 1) % ringSegments) * tubeSegments;
                int thisRing = i * tubeSegments;

                for (int j = 0; j < tubeSegments; j++)
                {
                    int nextTube = (j + 1) % tubeSegments;

                    int a = thisRing + j;
                    int b = thisRing + nextTube;
                    int c = nextRing + j;
                    int d = nextRing + nextTube;

                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            torus = Build("GizmoTorus", vertices, triangles);
            return torus;
        }

        private static Mesh Build(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
