namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Makes a part that is already on the table pickable again.
    ///
    /// Separate from <see cref="ShelfItem"/> because the two mean different
    /// things: clicking a shelf item creates a new part, clicking this one
    /// picks up *this* part. Conflating them would eventually produce the
    /// classic bug where rearranging a robot silently duplicates it.
    /// </summary>
    [RequireComponent(typeof(PartInstance))]
    public sealed class PickupHandle : MonoBehaviour, IWorkshopInteractable
    {
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
            if (highlight == null)
            {
                highlight = gameObject.AddComponent<Highlightable>();
            }
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
            // In transform mode a placed part is selected, not picked up. Only
            // already-placed parts behave differently; taking a new part from
            // the shelf works the same in both modes, which is why the two
            // systems coexist rather than one replacing the other.
            TransformToolController tool = controller.TransformTool;
            if (tool != null && tool.IsActive)
            {
                var instance = GetComponent<PartInstance>();
                tool.Select(instance != null ? instance.Group : null);
                return;
            }

            controller.BeginCarryExisting(gameObject);
        }
    }
}
