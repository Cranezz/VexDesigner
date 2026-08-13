namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Shared flag letting one system claim an input the camera also wants.
    ///
    /// Right-drag orbits the camera, but right-drag while carrying a part is
    /// meant to rotate the part. Rather than the camera knowing about part
    /// placement or the reverse, both read this. Neither depends on the other,
    /// and a third system can claim the same input later without either
    /// changing.
    /// </summary>
    public sealed class InteractionLock : MonoBehaviour
    {
        /// <summary>
        /// True while something other than the camera owns the orbit gesture.
        /// </summary>
        public bool CameraOrbitLocked { get; set; }

        /// <summary>
        /// True while the player should stay put.
        ///
        /// Separate from the look lock because the two are wanted at different
        /// times: carrying a part locks the view while rotating but must leave
        /// walking free, whereas turning a gizmo ring should hold the player
        /// still - walking away mid-rotation drags the whole reference frame
        /// out from under the gesture.
        /// </summary>
        public bool MovementLocked { get; set; }
    }
}
