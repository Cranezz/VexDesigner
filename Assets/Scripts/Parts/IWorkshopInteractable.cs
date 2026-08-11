namespace VexDesigner.Parts
{
    /// <summary>
    /// Anything the user can aim at and click.
    ///
    /// One interface covers three quite different things - a part on the
    /// shelf, a page arrow, and a part already placed on the table - so the
    /// pointer code has a single path for hover and click rather than a
    /// growing chain of type tests. Adding a new clickable thing later means
    /// implementing this, not editing the controller.
    /// </summary>
    public interface IWorkshopInteractable
    {
        /// <summary>
        /// When false, the object ignores hover and click entirely. Used to
        /// shut down the whole shelf while the user is carrying something:
        /// the absence of any highlight is what tells them that picking up a
        /// second part is not an available action.
        /// </summary>
        bool Interactable { get; set; }

        void SetHovered(bool hovered);

        void OnPrimaryClick(PartPlacementController controller);
    }
}
