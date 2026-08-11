namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Marks a collider that parts may be set down on.
    ///
    /// A marker component rather than a Unity layer, deliberately. Layers are a
    /// fixed set of 32 configured in project settings, they are invisible in
    /// code review, and getting one wrong produces a raycast that silently
    /// misses. A component is self-documenting, unlimited, and visible on the
    /// object it applies to.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PlacementSurface : MonoBehaviour
    {
    }
}
