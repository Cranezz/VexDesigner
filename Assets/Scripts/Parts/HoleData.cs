namespace VexDesigner.Parts
{
    using System;
    using UnityEngine;

    /// <summary>
    /// One opening of a hole: where it breaks the surface, and which way that
    /// surface faces.
    ///
    /// A hole through sheet metal has two of these, one per side. They are kept
    /// as separate entities rather than as a single hole with a depth because
    /// nearly everything done with a hole is done to *one face of it*: a screw
    /// enters through one side, two parts mate face to face, and a nut seats on
    /// the far side. A single centre point would leave every one of those
    /// needing to work out which side was meant.
    /// </summary>
    [Serializable]
    public struct HoleFace
    {
        [Tooltip("Centre of the opening, in the part's local space.")]
        public Vector3 localPosition;

        [Tooltip("Outward surface normal, in the part's local space. This is " +
                 "the direction a screw head faces when inserted from this side.")]
        public Vector3 localNormal;

        /// <summary>Width of the opening across the flats, in metres.</summary>
        public float width;
    }

    /// <summary>
    /// What a hole does when a screw goes through it.
    ///
    /// The distinction is what makes a robot an assembly rather than a pile of
    /// parts touching each other. A screw dropped through a stack of C-channel
    /// holds nothing; the same screw with a nut on the end holds everything
    /// between them. Marking that on the *hole* rather than on the part is what
    /// lets a nut and a threaded standoff behave identically without either
    /// knowing about the other.
    /// </summary>
    public enum HoleType
    {
        /// <summary>
        /// A plain opening. Says where something can be put; grips nothing.
        /// Every hole in a C-channel or a plate is one of these.
        /// </summary>
        Normal,

        /// <summary>
        /// Bites on the thread. A screw reaching one of these clamps everything
        /// between its head and this hole into a single assembly.
        /// </summary>
        Threaded,

        /// <summary>
        /// Grips the shaft rather than the thread - shaft collars and clamps.
        /// Reserved: currently treated as <see cref="Normal"/>.
        /// </summary>
        Clamp,
    }

    /// <summary>
    /// What a hole looks like.
    ///
    /// VEX structure is drilled as rounded squares so a square shaft can pass
    /// through, and that shape is what makes the grid recognisable. A nut's
    /// bore is a plain tapped circle. Marking a round hole with a square is a
    /// small lie that reads immediately as wrong.
    /// </summary>
    public enum HoleShape
    {
        /// <summary>Rounded square. The VEX structural hole.</summary>
        Square,

        /// <summary>Plain circle. Nut bores, and anything simply drilled.</summary>
        Round,
    }

    /// <summary>
    /// A hole passing through a part: two faces, and the material between them.
    /// </summary>
    [Serializable]
    public struct Hole
    {
        public HoleFace front;
        public HoleFace back;

        [Tooltip("Material thickness between the two faces, in metres.")]
        public float depth;

        [Tooltip("What happens when a screw reaches this hole. See HoleType.")]
        public HoleType type;

        [Tooltip("Rounded square for VEX structure, circle for a drilled bore.")]
        public HoleShape shape;

        /// <summary>True if a screw reaching this hole forms an assembly.</summary>
        public bool Grips => type == HoleType.Threaded;

        /// <summary>Centre of the hole, halfway through the material.</summary>
        public Vector3 LocalCentre => (front.localPosition + back.localPosition) * 0.5f;

        /// <summary>Axis through the hole, pointing out of the front face.</summary>
        public Vector3 LocalAxis => front.localNormal;

        /// <summary>
        /// The face whose normal best faces <paramref name="localViewDirection"/>
        /// - the side being looked at.
        /// </summary>
        public HoleFace FacingSide(Vector3 localViewDirection)
        {
            return Vector3.Dot(front.localNormal, -localViewDirection) >=
                   Vector3.Dot(back.localNormal, -localViewDirection)
                ? front
                : back;
        }

        public HoleFace OppositeOf(HoleFace face)
        {
            return Vector3.Dot(face.localNormal, front.localNormal) > 0f ? back : front;
        }
    }

    /// <summary>
    /// Every hole found in one part type, stored on its definition.
    ///
    /// Computed once in the editor and saved, never recomputed at runtime.
    /// Holes are what screws snap to and what save files refer to, so they have
    /// to be identical in every session and on every machine - a
    /// floating-point difference between two runs of the detector would be
    /// enough to move a hole and break an existing build.
    /// </summary>
    [Serializable]
    public sealed class HoleSet
    {
        [Tooltip("Detected holes, in the part's local space.")]
        public Hole[] holes = new Hole[0];

        [Tooltip("Spacing the detector actually measured between neighbouring " +
                 "holes, in inches. Should land on the part's declared pitch - " +
                 "if it does not, either the detection or the import scale is " +
                 "wrong, and this is how that shows up.")]
        public float measuredPitchInches;

        [Tooltip("When detection last ran, so a stale set is obvious.")]
        public string generatedAt = "";

        public int Count => holes == null ? 0 : holes.Length;

        public bool IsEmpty => Count == 0;
    }
}
