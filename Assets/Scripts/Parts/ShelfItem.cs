namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// A display copy of a part sitting on the shelf. Clicking it hands the
    /// user a fresh copy; the display copy itself never moves.
    ///
    /// The shelf is an infinite source. A real team orders more parts, and
    /// modelling scarcity would add friction with no design benefit.
    /// </summary>
    public sealed class ShelfItem : MonoBehaviour, IWorkshopInteractable
    {
        [SerializeField] private PartDefinition definition;

        private Highlightable highlight;
        private bool interactable = true;

        public PartDefinition Definition => definition;

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

        public void Configure(PartDefinition partDefinition)
        {
            definition = partDefinition;
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
            controller.BeginCarryNew(definition);
        }
    }
}
