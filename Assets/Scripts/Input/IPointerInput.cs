namespace VexDesigner.InputSources
{
    using UnityEngine;

    /// <summary>
    /// Describes where the user is aiming and what they are asking to do there.
    ///
    /// Deliberately expressed as a **ray**, not a screen position. A mouse
    /// produces that ray by projecting a cursor through the camera; a VR
    /// controller produces it directly from its own pose. Everything that
    /// consumes this interface works unchanged either way, which is the point.
    ///
    /// Returning screen coordinates instead would bake in the assumption that
    /// pointing happens on a flat display, and that assumption is exactly what
    /// breaks in a headset.
    /// </summary>
    public interface IPointerInput
    {
        /// <summary>World-space ray the user is currently aiming.</summary>
        Ray AimRay { get; }

        /// <summary>True on the frame the primary action begins.</summary>
        bool PrimaryPressedThisFrame { get; }

        /// <summary>
        /// True for as long as the primary action is held.
        ///
        /// Distinct from the press edge because the two suit different jobs:
        /// picking a part up is a moment, but dragging a gizmo axis is a
        /// continuous gesture that should end when the button comes up.
        /// </summary>
        bool PrimaryHeld { get; }

        /// <summary>
        /// True while the "keep going" modifier is held - Alt on desktop.
        /// Used to place several parts in a row without returning to the tray.
        /// </summary>
        bool RepeatModifierHeld { get; }

        /// <summary>
        /// True when the pointer is over UI and should not interact with the
        /// world. Prevents a click on a button from also placing a part.
        /// </summary>
        bool IsOverInterface { get; }

        /// <summary>
        /// True while the secondary action is held - right mouse on desktop.
        /// Used to rotate a carried part.
        /// </summary>
        bool SecondaryHeld { get; }

        /// <summary>
        /// True on the frame the secondary action begins.
        ///
        /// Distinct from the held state because the two mean different things
        /// here: held drives a continuous rotation, whereas the press edge is
        /// what anchors a hole for mating.
        /// </summary>
        bool SecondaryPressedThisFrame { get; }

        /// <summary>
        /// Raw pointer movement this frame, in device units. Only meaningful
        /// paired with a held button; on its own the aim ray already says
        /// where the user is pointing.
        /// </summary>
        Vector2 DragDelta { get; }
    }
}
