namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Draws a filled marker over one hole face.
    ///
    /// One of these is created per purpose rather than per hole - the aimed
    /// hole, and the anchored one during mating. A part has hundreds of holes
    /// and at most a couple are ever marked, so the marker moves to the hole
    /// rather than every hole owning a marker.
    ///
    /// Filled rather than outlined: with holes a quarter inch apart, a ring is
    /// hard to attribute to one of them, and the highlight should read as the
    /// hole lighting up rather than as something drawn around it.
    /// </summary>
    public sealed class HoleHighlighter : MonoBehaviour
    {
        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private Material material;
        private float currentWidth = -1f;
        private HoleShape currentShape = HoleShape.Square;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static HoleHighlighter Create(string name, Color colour)
        {
            var go = new GameObject(name);
            var highlighter = go.AddComponent<HoleHighlighter>();
            highlighter.Build(colour);
            return highlighter;
        }

        private void Build(Color colour)
        {
            filter = gameObject.AddComponent<MeshFilter>();
            meshRenderer = gameObject.AddComponent<MeshRenderer>();

            // Drawn over whatever is behind it. A hole marker sits exactly on a
            // surface, so a depth-tested one flickers against the metal it is
            // marking; and the surface it marks is always the one being looked
            // at, so there is nothing meaningful for it to hide behind.
            Shader shader = Shader.Find("VexDesigner/GizmoOverlay")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

            material = new Material(shader) { name = $"{name} marker" };
            material.SetColor(BaseColorId, colour);

            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            Hide();
        }

        public void SetColour(Color colour)
        {
            material.SetColor(BaseColorId, colour);
        }

        public void Show(HoleHit hit)
        {
            if (!hit.IsValid)
            {
                Hide();
                return;
            }

            // Mesh rebuilt only when the size changes; every hole on a part is
            // usually the same width, so this is nearly always a cache hit.
            if (!Mathf.Approximately(currentWidth, hit.Face.width) ||
                currentShape != hit.Shape)
            {
                currentWidth = hit.Face.width;
                currentShape = hit.Shape;
                filter.sharedMesh = HoleMarkerMesh.Filled(hit.Face.width, hit.Shape);
            }

            transform.position = hit.WorldPosition;
            transform.rotation = Quaternion.LookRotation(hit.WorldNormal);
            transform.localScale = Vector3.one * hit.Part.transform.lossyScale.x;

            meshRenderer.enabled = true;
        }

        public void Hide()
        {
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }
        }
    }
}
