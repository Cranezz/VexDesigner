namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Makes a part see-through while it is being positioned by one of its
    /// holes.
    ///
    /// Transparency is doing real work here, not decoration. A part held by a
    /// hole is being lined up against another part, and an opaque one hides
    /// exactly the join the user is trying to judge. It also distinguishes a
    /// proposed position from a committed one - nothing solid has happened
    /// until the second click.
    ///
    /// The original materials are put back on release rather than rebuilt, so a
    /// part that had been recoloured keeps its colour.
    /// </summary>
    public sealed class PartGhost : MonoBehaviour
    {
        private Renderer[] renderers;
        private Material[][] original;
        private static Material shared;

        /// <summary>
        /// Every part currently see-through.
        ///
        /// Kept so that none can be stranded. Ghosting was applied to the
        /// members of the assembly being carried and lifted from the members of
        /// the assembly at the time of release - and those are not always the
        /// same set, because bolting something on rebuilds the groups in
        /// between. Any part that left the assembly mid-carry stayed
        /// transparent for good, with nothing left holding a reference to put
        /// it right.
        /// </summary>
        private static readonly System.Collections.Generic.List<PartGhost> Ghosted =
            new System.Collections.Generic.List<PartGhost>();

        /// <summary>Makes every part in the workshop solid again.</summary>
        public static void RestoreAll()
        {
            for (int i = Ghosted.Count - 1; i >= 0; i--)
            {
                if (Ghosted[i] != null)
                {
                    Ghosted[i].SetGhosted(false);
                }
            }

            Ghosted.Clear();
        }

        public bool IsGhosted { get; private set; }

        public void SetGhosted(bool ghosted)
        {
            if (ghosted == IsGhosted)
            {
                return;
            }

            IsGhosted = ghosted;

            if (ghosted)
            {
                // The border comes off first, or it would be captured as part
                // of the original set and put back twice.
                GetComponent<PartOutline>()?.Hide();

                Capture();
                Apply(GhostMaterial());
                Ghosted.Add(this);
            }
            else
            {
                Restore();
                Ghosted.Remove(this);
                GetComponent<PartOutline>()?.Reapply();
            }
        }

        private void Capture()
        {
            renderers = GetComponentsInChildren<Renderer>();
            original = new Material[renderers.Length][];

            for (int i = 0; i < renderers.Length; i++)
            {
                original[i] = renderers[i].sharedMaterials;
            }
        }

        private void Apply(Material material)
        {
            foreach (Renderer renderer in renderers)
            {
                var replacement = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < replacement.Length; i++)
                {
                    replacement[i] = material;
                }

                renderer.sharedMaterials = replacement;
            }
        }

        private void Restore()
        {
            if (renderers == null || original == null)
            {
                return;
            }

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    renderers[i].sharedMaterials = original[i];
                }
            }

            renderers = null;
            original = null;
        }

        private void OnDisable()
        {
            // A part destroyed or disabled mid-placement would otherwise come
            // back ghosted the next time it appeared.
            if (IsGhosted)
            {
                Restore();
                Ghosted.Remove(this);
                IsGhosted = false;
            }
        }

        /// <summary>
        /// One shared translucent material for every ghosted part.
        ///
        /// URP needs the blend mode, the keyword and the render queue all set
        /// together - an alpha below one on an opaque material changes nothing,
        /// which is a common way to spend an hour wondering why transparency
        /// does not work.
        /// </summary>
        private static Material GhostMaterial()
        {
            if (shared != null)
            {
                return shared;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");

            shared = new Material(shader) { name = "PartGhost" };

            shared.SetFloat("_Surface", 1f);
            shared.SetFloat("_Blend", 0f);
            shared.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            shared.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            shared.SetFloat("_ZWrite", 0f);
            shared.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            shared.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (shared.HasProperty("_BaseColor"))
            {
                shared.SetColor("_BaseColor", new Color(0.55f, 0.85f, 1f, 0.38f));
            }

            if (shared.HasProperty("_Metallic"))
            {
                shared.SetFloat("_Metallic", 0f);
            }

            return shared;
        }
    }
}
