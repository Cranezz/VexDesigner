namespace VexDesigner.EditorTools
{
    using UnityEditor;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Prints what a fastener mesh actually measures, on every axis.
    ///
    /// Here because guessing at geometry from a distance wastes more time than
    /// it saves. When the baked figures disagreed with the catalogue, this is
    /// what settled which of the two was lying.
    /// </summary>
    public static class FastenerDiagnostics
    {
        private const float InchesToMetres = 0.0254f;

        [MenuItem("VexDesigner/Report Fastener Geometry")]
        public static void Report()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:PartDefinition"))
            {
                var definition = AssetDatabase.LoadAssetAtPath<PartDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));

                if (definition == null || definition.mesh == null)
                {
                    continue;
                }

                if (!definition.IsNut && !definition.IsScrew)
                {
                    continue;
                }

                Mesh mesh = definition.mesh;
                Vector3 size = mesh.bounds.size / InchesToMetres;

                Debug.Log(
                    $"[Geometry] {definition.saving.id} {definition.displayName}: " +
                    $"bounds {size.x:0.000} x {size.y:0.000} x {size.z:0.000} in, " +
                    $"{mesh.vertexCount} verts, {mesh.triangles.Length / 3} tris, " +
                    $"axis {definition.fastener.localAxis}, " +
                    $"submeshes {mesh.subMeshCount}");
            }
        }
    }
}
