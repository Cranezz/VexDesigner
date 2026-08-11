namespace VexDesigner.InputSources
{
    using UnityEngine;

    /// <summary>
    /// Describes *what the user wants the view to do*, with no reference to how
    /// they asked for it.
    ///
    /// This indirection is the whole VR strategy in one file. The camera rig
    /// never mentions a mouse, so when VR arrives we add a new implementation
    /// of this interface and change nothing above it. Had the rig called
    /// Mouse.current directly, every camera behaviour would need rewriting.
    /// </summary>
    public interface ILookInput
    {
        /// <summary>
        /// Requested orbit this frame. x = yaw (degrees), y = pitch (degrees).
        /// Already scaled to degrees by the implementation, so consumers do not
        /// need to know anything about device sensitivity or units.
        /// </summary>
        Vector2 LookDelta { get; }

        /// <summary>
        /// Requested change in viewing distance this frame, as a fraction of
        /// the current distance. Positive moves the viewer closer.
        /// </summary>
        float ZoomDelta { get; }

        /// <summary>
        /// Requested lateral shift of the point being looked at, in world units
        /// relative to the current view orientation. Screen-relative dragging.
        /// </summary>
        Vector2 PanDelta { get; }

        /// <summary>
        /// Requested travel across the workspace this frame, in world units.
        /// x = right, y = forward, both relative to the current view heading.
        ///
        /// Distinct from <see cref="PanDelta"/> because it is a sustained
        /// held-key motion rather than a one-to-one drag, so the camera scales
        /// it by delta time. In VR this becomes stick locomotion.
        /// </summary>
        Vector2 MoveDelta { get; }
    }
}
