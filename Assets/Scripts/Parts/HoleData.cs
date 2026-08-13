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
    /// A hole passing through a part: two faces, and the material between them.
    /// </summary>
    [Serializable]
    public struct Hole
    {
        public HoleFace front;
        public HoleFace back;

        [Tooltip("Material thickness between the two faces, in metres.")]
        public float depth;

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
