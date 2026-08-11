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
        /// True while the "keep going" modifier is held - Alt on desktop.
        /// Used to place several parts in a row without returning to the tray.
        /// </summary>
        bool RepeatModifierHeld { get; }

        /// <summary>
        /// True when the pointer is over UI and should not interact with the
        /// world. Prevents a click on a button from also placing a part.
        /// </summary>
        bool IsOverInterface { get; }
    }
}
