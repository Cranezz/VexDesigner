namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// One hole a screw runs through, and where along the screw it sits.
    ///
    /// Distances are measured from under the head, which is the natural zero:
    /// it is where the screw meets the first piece of metal, and it is the
    /// figure the catalogue length is quoted against. So a pass at 0.00 to 0.06
    /// is the first wall, and a screw is long enough for a nut exactly when the
    /// nut still fits before the shank runs out.
    /// </summary>
    public struct ScrewPass
    {
        public PartHoles Part;
        public int HoleIndex;

        /// <summary>Distance from under the head to the near face, in metres.</summary>
        public float Entry;

        /// <summary>Distance from under the head to the far face, in metres.</summary>
        public float Exit;

        /// <summary>Whether reaching this hole clamps the stack together.</summary>
        public bool Grips;

        public float Thickness => Exit - Entry;
    }

    /// <summary>
    /// The line a screw occupies, and what it finds along that line.
    ///
    /// Kept as plain geometry with no reference to a scene object, because the
    /// same question - what does this line pass through - is asked at three
    /// different moments: while a screw is being previewed and nothing has been
    /// placed yet, when it is committed, and every time a nut is offered to it.
    /// A version that needed a placed screw could only answer the second.
    /// </summary>
    public static class ScrewLine
    {
        /// <summary>
        /// How far off the axis a hole's centre may sit and still count as being
        /// on the screw, as a fraction of the hole's width.
        ///
        /// Generous on purpose. Parts are placed by hand and settle under
        /// physics, so holes that a builder would call lined up are routinely a
        /// few thousandths out - and a screw that refuses to notice a hole it
        /// visibly passes through is worse than one that is slightly forgiving.
        /// </summary>
        private const float OffAxisTolerance = 0.6f;

        /// <summary>
        /// How far from parallel a hole's axis may be and still count, in
        /// degrees. A screw does not bend, so a hole at a slant to it is a hole
        /// it passes beside rather than through.
        /// </summary>
        private const float AngleToleranceDegrees = 12f;

        /// <summary>
        /// Every hole the screw passes through, nearest the head first.
        /// </summary>
        /// <param name="seat">Point under the head, where it meets the metal.</param>
        /// <param name="direction">Unit vector down the shank.</param>
        /// <param name="length">Usable shank length, in metres.</param>
        /// <param name="ignore">The screw's own part, which is never a target.</param>
        public static void Gather(
            Vector3 seat, Vector3 direction, float length,
            List<ScrewPass> results, PartHoles ignore = null, PartHoles alsoIgnore = null)
        {
            results.Clear();

            float cosLimit = Mathf.Cos(AngleToleranceDegrees * Mathf.Deg2Rad);

            IReadOnlyList<PartHoles> parts = PartHoles.All;

            for (int p = 0; p < parts.Count; p++)
            {
                PartHoles part = parts[p];

                if (part == null || part == ignore || part == alsoIgnore ||
                    !part.HasHoles)
                {
                    continue;
                }

                Hole[] holes = part.Holes.holes;

                for (int i = 0; i < holes.Length; i++)
                {
                    if (TryPass(part, holes[i], i, seat, direction, length, cosLimit,
                            out ScrewPass pass))
                    {
                        results.Add(pass);
                    }
                }
            }

            results.Sort((a, b) => a.Entry.CompareTo(b.Entry));
        }

        private static bool TryPass(
            PartHoles part, Hole hole, int index,
            Vector3 seat, Vector3 direction, float length, float cosLimit,
            out ScrewPass pass)
        {
            pass = default;

            Transform t = part.transform;

            Vector3 frontPosition = t.TransformPoint(hole.front.localPosition);
            Vector3 backPosition = t.TransformPoint(hole.back.localPosition);
            Vector3 holeAxis = t.TransformDirection(hole.front.localNormal).normalized;

            // Parallel either way round: a screw driven in from the back of a
            // part uses the same hole as one driven in from the front.
            if (Mathf.Abs(Vector3.Dot(holeAxis, direction)) < cosLimit)
            {
                return false;
            }

            float front = Vector3.Dot(frontPosition - seat, direction);
            float back = Vector3.Dot(backPosition - seat, direction);

            float entry = Mathf.Min(front, back);
            float exit = Mathf.Max(front, back);

            // Wholly behind the head, or past the end of the thread. A hole the
            // screw only partly reaches does not count - it has not gone
            // through, so nothing is fastened.
            if (entry < -0.0005f || exit > length + 0.0005f)
            {
                return false;
            }

            // Both openings have to sit on the shank, not merely near it. The
            // near one alone would accept a hole angled just inside the
            // tolerance whose far side is nowhere near the screw.
            float tolerance = hole.front.width * OffAxisTolerance * t.lossyScale.x;

            if (OffAxis(frontPosition, seat, direction) > tolerance ||
                OffAxis(backPosition, seat, direction) > tolerance)
            {
                return false;
            }

            pass = new ScrewPass
            {
                Part = part,
                HoleIndex = index,
                Entry = entry,
                Exit = exit,
                Grips = hole.Grips,
            };

            return true;
        }

        private static float OffAxis(Vector3 point, Vector3 origin, Vector3 direction)
        {
            Vector3 offset = point - origin;
            return (offset - (direction * Vector3.Dot(offset, direction))).magnitude;
        }

        /// <summary>
        /// The gaps between material along the screw, as (start, end) pairs.
        ///
        /// Air, in other words - the stretches of bare shank between one plate
        /// and the next, plus the run past the last plate to the end of the
        /// thread. These are the places a nut can be threaded onto, which is
        /// what makes it possible to clamp two parts together in the middle of
        /// a screw and leave the rest of it free.
        /// </summary>
        public static void Gaps(
            IReadOnlyList<ScrewPass> passes, float length, List<Vector2> results)
        {
            results.Clear();

            float cursor = 0f;

            for (int i = 0; i < passes.Count; i++)
            {
                ScrewPass pass = passes[i];

                // Overlapping holes - two parts flush against each other - leave
                // no gap between them, and a negative-width one would be worse
                // than none.
                if (pass.Entry > cursor + 1e-5f)
                {
                    results.Add(new Vector2(cursor, pass.Entry));
                }

                cursor = Mathf.Max(cursor, pass.Exit);
            }

            if (length > cursor + 1e-5f)
            {
                results.Add(new Vector2(cursor, length));
            }
        }
    }
}
