namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Stores a smoothed copy of each vertex normal for the outline shader.
    ///
    /// The same job the part importer does, available at runtime for meshes
    /// that did not come from a file - a cut part, most obviously. Without it
    /// a cut part's border tears open along every hard edge, because the
    /// rendering normals are deliberately split there so machined corners stay
    /// crisp.
    /// </summary>
    public static class OutlineNormals
    {
        /// <summary>Grid on which two vertices count as the same corner.</summary>
        private const float WeldGrid = 1e-5f;

        public static void Bake(Mesh mesh)
        {
            if (mesh == null || !mesh.isReadable)
            {
                return;
            }

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;

            if (normals == null || normals.Length != vertices.Length)
            {
                return;
            }

            var averaged = new Dictionary<Vector3Int, Vector3>(vertices.Length);
            var keys = new Vector3Int[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i] / WeldGrid;

                var key = new Vector3Int(
                    Mathf.RoundToInt(v.x), Mathf.RoundToInt(v.y), Mathf.RoundToInt(v.z));

                keys[i] = key;
                averaged[key] = averaged.TryGetValue(key, out Vector3 sum)
                    ? sum + normals[i]
                    : normals[i];
            }

            var smooth = new List<Vector3>(vertices.Length);

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 sum = averaged[keys[i]];

                smooth.Add(sum.sqrMagnitude > 1e-8f ? sum.normalized : normals[i]);
            }

            mesh.SetUVs(3, smooth);
        }
    }
}
