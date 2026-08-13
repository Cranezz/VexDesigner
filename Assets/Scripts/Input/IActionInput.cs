namespace VexDesigner.InputSources
{
    /// <summary>
    /// Discrete commands, named by intent rather than by key.
    ///
    /// Naming by intent is what makes rebinding possible without touching any
    /// consumer, and it is the same reason a VR controller can implement this
    /// interface later - "freeze" means something on a controller even though
    /// "K" does not.
    /// </summary>
    public interface IActionInput
    {
        /// <summary>Pin the held or targeted assembly in mid-air, or release it.</summary>
        bool FreezePressed { get; }

        /// <summary>Toggle crouch.</summary>
        bool CrouchPressed { get; }

        /// <summary>Switch between grab mode and the transform tool.</summary>
        bool ModeTogglePressed { get; }

        /// <summary>Switch between global and part-relative axes.</summary>
        bool RelativeTogglePressed { get; }

        /// <summary>Held to swap the move tool for the rotate tool.</summary>
        bool RotateModifierHeld { get; }

        /// <summary>
        /// Held for fine control: slower rotation and smaller distance steps.
        /// Assembling a robot needs both coarse positioning and thousandth-inch
        /// nudges, and one sensitivity cannot serve both.
        /// </summary>
        bool PrecisionHeld { get; }

        /// <summary>Jump.</summary>
        bool JumpPressed { get; }

        /// <summary>
        /// Held to snap movement and rotation to fixed increments.
        /// </summary>
        bool SnapHeld { get; }
    }
}
