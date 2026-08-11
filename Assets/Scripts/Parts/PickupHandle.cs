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
            controller.BeginCarryExisting(gameObject);
        }
    }
}
