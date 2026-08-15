namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Draws a coloured border round a part.
    ///
    /// Added as a second material on the part's own renderers rather than as a
    /// duplicate object. The mesh is then drawn twice from one renderer, which
    /// costs a draw call and no transforms, and - importantly - the border
    /// follows the part exactly without anything having to keep a copy in step
    /// with it.
    /// </summary>
    public sealed class PartOutline : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");

        private static readonly Dictionary<int, Material> Shared =
            new Dictionary<int, Material>();

        private Renderer[] renderers;
        private Material current;
        private Material currentMask;

        public bool IsShowing => current != null;

        /// <summary>
        /// Shows the border in the given colour, or hides it.
        ///
        /// The material is shared per colour and thickness, so a robot of
        /// thirty frozen parts uses one material rather than thirty.
        /// </summary>
        public void Show(Color colour, float thicknessPixels = 3f)
        {
            Material material = Resolve(colour, thicknessPixels);

            if (material == current)
            {
                return;
            }

            Hide();

            if (material == null)
            {
                return;
            }

            current = material;
            currentMask = MaskMaterial();

            foreach (Renderer renderer in Renderers())
            {
                var materials = new List<Material>(renderer.sharedMaterials);

                // Mask first, border second, and their render queues put them
                // in that order too. The border is drawn only where the mask
                // is absent, so a mask that arrived afterwards would leave the
                // part unoutlined for a frame and flicker.
                if (currentMask != null)
                {
                    materials.Add(currentMask);
                }

                materials.Add(material);
                renderer.sharedMaterials = materials.ToArray();
            }
        }

        public void Hide()
        {
            if (current == null)
            {
                return;
            }

            foreach (Renderer renderer in Renderers())
            {
                if (renderer == null)
                {
                    continue;
                }

                var materials = new List<Material>(renderer.sharedMaterials);

                // Removed by identity, so a part that also had its materials
                // swapped for the ghost does not lose the wrong one.
                materials.Remove(current);

                if (currentMask != null)
                {
                    materials.Remove(currentMask);
                }

                renderer.sharedMaterials = materials.ToArray();
            }

            current = null;
            currentMask = null;
        }

        /// <summary>
        /// Re-applies the border after something else has rewritten the
        /// renderer's materials - the ghost, for instance.
        /// </summary>
        public void Reapply()
        {
            if (current == null)
            {
                return;
            }

            Material material = current;
            current = null;
            Show(GetColour(material), material.GetFloat(ThicknessId));
        }

        private static Color GetColour(Material material) =>
            material.GetColor(BaseColorId);

        private Renderer[] Renderers()
        {
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>();
            }

            return renderers;
        }

        /// <summary>
        /// The stencil stamp that confines the border to the silhouette. One
        /// for the whole workshop; it has no settings.
        /// </summary>
        private static Material MaskMaterial()
        {
            if (maskMaterial != null)
            {
                return maskMaterial;
            }

            Shader shader = Shader.Find("VexDesigner/PartOutlineMask");

            if (shader == null)
            {
                return null;
            }

            maskMaterial = new Material(shader) { name = "PartOutlineMask" };
            return maskMaterial;
        }

        private static Material maskMaterial;

        private static Material Resolve(Color colour, float thickness)
        {
            Shader shader = Shader.Find("VexDesigner/PartOutline");

            if (shader == null)
            {
                // A missing shader must not take the part with it. Without the
                // border the part simply looks unmarked, which is survivable;
                // a null material would render it magenta or not at all.
                return null;
            }

            int key = colour.GetHashCode() ^ (Mathf.RoundToInt(thickness * 100f) * 397);

            if (Shared.TryGetValue(key, out Material existing) && existing != null)
            {
                return existing;
            }

            var material = new Material(shader) { name = "PartOutline" };
            material.SetColor(BaseColorId, colour);
            material.SetFloat(ThicknessId, thickness);

            Shared[key] = material;
            return material;
        }

        private void OnDisable()
        {
            Hide();
        }
    }
}
