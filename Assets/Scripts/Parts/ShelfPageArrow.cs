namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// A clickable arrow that pages the shelf.
    ///
    /// World geometry rather than screen UI, deliberately. In VR there is no
    /// screen to put a button on, and a control that exists in the workshop
    /// can be reached by hand later without being redesigned.
    /// </summary>
    public sealed class ShelfPageArrow : MonoBehaviour, IWorkshopInteractable
    {
        [SerializeField] private PartShelf shelf;

        [Tooltip("+1 for next page, -1 for previous.")]
        [SerializeField] private int direction = 1;

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

        public void Configure(PartShelf owner, int pageDirection)
        {
            shelf = owner;
            direction = pageDirection;
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
            if (shelf != null)
            {
                shelf.ChangePage(direction);
            }
        }
    }
}
