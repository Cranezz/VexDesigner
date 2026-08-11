namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// One compartment of the parts organiser. Holds a single part type and
    /// dispenses copies of it.
    ///
    /// The bin is an infinite source - taking a part does not deplete it. A
    /// real VEX team orders more parts; modelling scarcity here would add
    /// friction with no design benefit.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PartBin : MonoBehaviour
    {
        [SerializeField] private PartDefinition part;

        [Tooltip("Where dispensed parts appear to come from, and where the " +
                 "display copies sit. Defaults to this object's centre.")]
        [SerializeField] private Transform displayAnchor;

        private Highlightable highlight;

        public PartDefinition Part => part;

        /// <summary>
        /// Whether this bin currently responds to the pointer. Set false for
        /// every bin while the user is carrying a part, so that neither the
        /// highlight nor the click does anything - which is how the user is
        /// told that taking a second part is not the available action.
        /// </summary>
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

        private bool interactable = true;

        private void Awake()
        {
            highlight = GetComponent<Highlightable>();
            if (displayAnchor == null)
            {
                displayAnchor = transform;
            }
        }

        public void SetHovered(bool hovered)
        {
            if (highlight != null)
            {
                highlight.SetHighlighted(hovered && interactable);
            }
        }

        /// <summary>Point a newly taken part should start from.</summary>
        public Vector3 DispensePoint =>
            displayAnchor != null ? displayAnchor.position : transform.position;

        public void Configure(PartDefinition definition)
        {
            part = definition;
        }
    }
}
