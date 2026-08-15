namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Shows the piece a cut will take off, in red, before it takes it.
    ///
    /// The part is drawn twice from one mesh, each draw clipped to one side of
    /// the blade. Nothing is sliced until the user asks for it, so the preview
    /// keeps up with a part being slid along the fence a thousandth of an inch
    /// at a time - and because both draws are bounded by the same plane the cut
    /// will use, what is shown red is exactly what will be removed.
    /// </summary>
    public static class SawPreview
    {
        private static readonly int PlaneId = Shader.PropertyToID("_CutPlane");
        private static readonly int SideId = Shader.PropertyToID("_CutSide");
        private static readonly int ColourId = Shader.PropertyToID("_BaseColor");

        private static readonly Dictionary<PartInstance, Material[]> originals =
            new Dictionary<PartInstance, Material[]>();

        private static Material keptMaterial;
        private static Material offcutMaterial;

        /// <summary>Puts a part into preview mode on the given saw.</summary>
        public static void Apply(PartInstance part, SawStation saw)
        {
            if (part == null || saw == null)
            {
                return;
            }

            var renderer = part.GetComponent<Renderer>();

            if (renderer == null || !Build())
            {
                return;
            }

            if (!originals.ContainsKey(part))
            {
                originals[part] = renderer.sharedMaterials;
            }

            keptMaterial.SetColor(ColourId, TintOf(part));
            renderer.sharedMaterials = new[] { keptMaterial, offcutMaterial };

            Refresh(saw);
        }

        /// <summary>Tells the materials where the blade is now.</summary>
        public static void Refresh(SawStation saw)
        {
            if (saw == null || keptMaterial == null)
            {
                return;
            }

            Plane plane = saw.CutPlane;
            var packed = new Vector4(plane.normal.x, plane.normal.y, plane.normal.z, plane.distance);

            keptMaterial.SetVector(PlaneId, packed);
            offcutMaterial.SetVector(PlaneId, packed);
        }

        public static void Restore(PartInstance part)
        {
            if (part == null || !originals.TryGetValue(part, out Material[] saved))
            {
                return;
            }

            var renderer = part.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterials = saved;
            }

            originals.Remove(part);
        }

        private static Color TintOf(PartInstance part)
        {
            return part.Definition != null
                ? part.Definition.colour
                : new Color(0.7f, 0.72f, 0.76f);
        }

        private static bool Build()
        {
            if (keptMaterial != null && offcutMaterial != null)
            {
                return true;
            }

            Shader shader = Shader.Find("VexDesigner/SawPreview");

            if (shader == null)
            {
                Debug.LogError(
                    "[Saw] The preview shader is missing, so the offcut cannot " +
                    "be shown. The cut itself is unaffected.");

                return false;
            }

            keptMaterial = new Material(shader) { name = "SawKept" };
            keptMaterial.SetFloat(SideId, 1f);

            offcutMaterial = new Material(shader) { name = "SawOffcut" };
            offcutMaterial.SetFloat(SideId, -1f);

            // Red and see-through: the colour of something about to be removed,
            // and transparent so the blade line behind it stays readable.
            offcutMaterial.SetColor(ColourId, new Color(1f, 0.25f, 0.2f, 0.45f));

            return true;
        }
    }
}
