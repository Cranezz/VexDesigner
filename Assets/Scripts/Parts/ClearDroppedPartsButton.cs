namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;
    using VexDesigner.UI;

    /// <summary>
    /// Wall button that deletes parts which have ended up on the floor.
    ///
    /// Dropped parts accumulate fast, and hunting them down individually is
    /// tedious in a way that has nothing to do with building robots. This is
    /// the workshop equivalent of sweeping up.
    ///
    /// Deliberately limited to the floor. A button that cleared *everything*
    /// would be one misclick away from destroying an afternoon's work, whereas
    /// anything on the bench, the mat, or pinned in mid-air is somewhere the
    /// user deliberately put it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ClearDroppedPartsButton : MonoBehaviour, IWorkshopInteractable
    {
        [Tooltip("Height below which a part counts as being on the floor, in " +
                 "inches. Comfortably under the 36 in bench, and above any " +
                 "plausible resting height for a part on the floor itself.")]
        [SerializeField] private float floorThresholdIn = 10f;

        private const float InchesToMetres = 0.0254f;

        private Highlightable highlight;
        private bool interactable = true;

        public bool Interactable
        {
            get => interactable;
            set
            {
                interactable = value;
                if (highlight != null)
                {
                    highlight.Interactable = value;
                    if (!value)
                    {
                        highlight.SetHighlighted(false);
                    }
                }
            }
        }

        private void Awake()
        {
            highlight = GetComponent<Highlightable>();
        }

        public void SetHovered(bool hovered)
        {
            if (highlight != null)
            {
                highlight.SetHighlighted(hovered && interactable);
            }
        }

        public void OnPrimaryClick(PartPlacementController controller)
        {
            int removed = Clear();

            MessageBanner.Info(removed == 0
                ? "Nothing on the floor"
                : $"Cleared {removed} part{(removed == 1 ? "" : "s")} from the floor");
        }

        private int Clear()
        {
            float threshold = floorThresholdIn * InchesToMetres;
            var doomed = new List<GameObject>();

            foreach (PartInstance part in FindObjectsByType<PartInstance>(FindObjectsSortMode.None))
            {
                if (part == null || part.IsFrozen)
                {
                    continue;
                }

                var renderer = part.GetComponentInChildren<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                // Measured from the part's lowest point, not its origin, which
                // for an imported CAD mesh can sit anywhere relative to the
                // geometry.
                if (renderer.bounds.min.y <= threshold)
                {
                    doomed.Add(part.gameObject);
                }
            }

            foreach (GameObject go in doomed)
            {
                Destroy(go);
            }

            return doomed.Count;
        }
    }
}
