namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// One cut taken out of a part.
    ///
    /// A saw blade is a plane, so a cut *is* a plane - see ARCHITECTURE.md
    /// section 2. Storing it as the plane's definition rather than as the
    /// resulting geometry is what makes cuts about sixteen bytes each, which
    /// in turn is what lets a save file, an undo entry and a network message
    /// all be the same object.
    /// </summary>
    [System.Serializable]
    public struct CutOperation
    {
        [Tooltip("Distance along the part's cut axis from its zero end, in " +
                 "inches. Measured from the part, not from world space, so a " +
                 "cut survives the part being moved.")]
        public float distanceInches;

        [Tooltip("Blade rotation about the cut axis, in degrees. 0 is square.")]
        public float bladeAngleDegrees;

        [Tooltip("Which end is kept. The blade defines two halves and this says " +
                 "which one survives.")]
        public bool keepPositiveSide;

        [Tooltip("Where the length was measured to on an angled cut: the short " +
                 "point, the long point, or the middle.")]
        public CutReference measuredTo;
    }

    /// <summary>
    /// Which point on an angled cut the stated length refers to.
    ///
    /// On a square cut all three are the same, which is why this can be
    /// ignored until angled cuts exist - but it has to be recorded from the
    /// start, or old save files become ambiguous the day it starts to matter.
    /// </summary>
    public enum CutReference
    {
        LongSide = 0,
        ShortSide = 1,
        Middle = 2,
    }

    /// <summary>
    /// Everything done to one specific part after it left the shelf.
    ///
    /// Currently the ordered list of cuts; paint and other alterations belong
    /// here too when they exist.
    ///
    /// Deliberately separate from <see cref="PartDefinition"/>: the definition
    /// describes a part *type* and is shared by every copy, whereas cuts belong
    /// to one specific piece on the bench. Putting them on the definition would
    /// mean cutting one C-channel cut every C-channel.
    ///
    /// Geometry is never stored. On load the part is rebuilt by re-applying
    /// these planes to the pristine imported mesh, in order, which is why
    /// geometry cannot degrade across save and load cycles.
    /// </summary>
    public sealed class Modifications : MonoBehaviour
    {
        [SerializeField] private List<CutOperation> cuts = new List<CutOperation>();

        public IReadOnlyList<CutOperation> Cuts => cuts;

        public bool HasCuts => cuts.Count > 0;

        public void Add(CutOperation cut)
        {
            cuts.Add(cut);
        }

        /// <summary>
        /// Drops the most recent cut. Undo re-slices from the original mesh
        /// rather than trying to reverse a slice, which is not possible - the
        /// removed vertices are gone.
        /// </summary>
        public bool RemoveLast()
        {
            if (cuts.Count == 0)
            {
                return false;
            }

            cuts.RemoveAt(cuts.Count - 1);
            return true;
        }

        public void Clear()
        {
            cuts.Clear();
        }

        public void Load(IEnumerable<CutOperation> saved)
        {
            cuts.Clear();
            cuts.AddRange(saved);
        }
    }
}
