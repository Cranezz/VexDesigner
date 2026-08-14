namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Where a nut can go on a screw, and why.
    /// </summary>
    public struct NutSeating
    {
        public PlacedScrew Screw;

        /// <summary>Distance from under the head to the nut's near face.</summary>
        public float Distance;

        /// <summary>World position of that face.</summary>
        public Vector3 WorldPosition;

        /// <summary>Direction the nut's near face points - back up the screw.</summary>
        public Vector3 WorldNormal;

        /// <summary>False when the screw runs out before the nut fits.</summary>
        public bool Fits;

        /// <summary>True when the nut lands in a gap rather than at the end.</summary>
        public bool InGap;

        public bool IsValid => Screw != null;
    }

    /// <summary>
    /// The geometry of putting fasteners where they go.
    ///
    /// Pure functions over poses, kept away from the click handling for the
    /// same reason <see cref="HoleMating"/> is: a preview and a commit must
    /// agree exactly, and the surest way to get that is for both to call the
    /// same code rather than for one to approximate the other.
    /// </summary>
    public static class FastenerFitting
    {
        private static readonly List<Vector2> gaps = new List<Vector2>();

        /// <summary>
        /// Where a screw sits when driven into <paramref name="hole"/>.
        ///
        /// The head lands on the surface and the shank runs into the material,
        /// which is the only way a screw ever goes into a hole. The roll about
        /// its own axis is left alone: a screw is near enough symmetric that
        /// choosing one would be inventing detail nobody asked for.
        /// </summary>
        public static bool ScrewPose(
            PartDefinition screw, Quaternion currentRotation, HoleHit hole,
            Vector3 screwScale, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = currentRotation;

            if (screw == null || !hole.IsValid || !screw.IsScrew)
            {
                return false;
            }

            // Into the material, so the head ends up against the face being
            // aimed at rather than buried behind it.
            Vector3 axis = -hole.WorldNormal.normalized;

            Vector3 current = (currentRotation * screw.fastener.localAxis).normalized;
            rotation = Quaternion.FromToRotation(current, axis) * currentRotation;

            Vector3 seatOffset = rotation *
                Vector3.Scale(screw.fastener.localSeatPoint, screwScale);

            position = hole.WorldPosition - seatOffset;
            return true;
        }

        /// <summary>
        /// Where a nut goes on a screw, given where on it the user is pointing.
        ///
        /// Two answers are possible and both are right. By default the nut goes
        /// on the end, tightened up against the last piece of metal - which is
        /// what a nut is for and what happens ninety-nine times in a hundred.
        /// But a screw with a gap in the middle of its stack can take a nut in
        /// that gap, clamping only what is above it and leaving the rest of the
        /// shank free. That is a real thing to do with a real screw, so pointing
        /// at that stretch of shank offers it.
        /// </summary>
        /// <param name="aim">Ray the user is pointing with.</param>
        public static NutSeating FindNutSeating(
            PlacedScrew screw, PartDefinition nut, Ray aim)
        {
            var seating = new NutSeating();

            if (screw == null || nut == null || !nut.IsNut)
            {
                return seating;
            }

            seating.Screw = screw;

            Vector3 seat = screw.Seat;
            Vector3 direction = screw.Direction;
            float length = screw.Length;
            float thickness = nut.NutThicknessMetres;

            // Reused rather than allocated: this runs every frame a nut is
            // held over a screw.
            ScrewLine.Gaps(screw.Passes, length, gaps);

            // Where along the shank the user is pointing: the closest approach
            // of the aim ray to the screw's own line.
            float pointed = Mathf.Clamp(ClosestApproach(aim, seat, direction), 0f, length);

            float distance = -1f;
            bool inGap = false;

            for (int i = 0; i < gaps.Count; i++)
            {
                Vector2 gap = gaps[i];

                if (pointed < gap.x - 1e-4f || pointed > gap.y + 1e-4f)
                {
                    continue;
                }

                // The nut tightens up against whatever is above it, so it seats
                // at the top of the gap rather than wherever the cursor happens
                // to be. A nut floating in mid-shank holds nothing.
                distance = gap.x;

                // The last gap is the run past the final plate: that is the
                // ordinary end-of-screw position, not a clamp in a gap.
                inGap = i < gaps.Count - 1;
                break;
            }

            if (distance < 0f)
            {
                // Pointing at metal, or at nothing in particular. Fall back to
                // the end of the last plate, which is where a nut goes.
                distance = LastExit(screw);
                inGap = false;
            }

            seating.Distance = distance;
            seating.InGap = inGap;
            seating.Fits = screw.NutFits(distance, thickness);
            seating.WorldPosition = seat + (direction * distance);

            // The nut's near face looks back up the screw, at the metal it is
            // being tightened against.
            seating.WorldNormal = -direction;

            return seating;
        }

        private static float LastExit(PlacedScrew screw)
        {
            float exit = 0f;

            foreach (ScrewPass pass in screw.Passes)
            {
                exit = Mathf.Max(exit, pass.Exit);
            }

            return exit;
        }

        /// <summary>
        /// Places a nut so its near face sits on the seating point.
        ///
        /// The nut is turned to face back up the screw. Which of its two faces
        /// that is does not matter - a nut is symmetric end to end - so the one
        /// already pointing the right way is kept, saving a needless half turn.
        /// </summary>
        public static void NutPose(
            PartDefinition nut, Quaternion currentRotation, NutSeating seating,
            Vector3 nutScale, out Vector3 position, out Quaternion rotation)
        {
            Vector3 axis = nut.fastener.localAxis.normalized;
            Vector3 current = (currentRotation * axis).normalized;

            // Flip only if it is currently pointing the wrong way.
            Vector3 wanted = Vector3.Dot(current, seating.WorldNormal) >= 0f
                ? seating.WorldNormal
                : -seating.WorldNormal;

            rotation = Quaternion.FromToRotation(current, wanted) * currentRotation;

            // The seating point is a face, but the nut's origin is its middle,
            // so it has to be pushed down the screw by half its height.
            float half = nut.NutThicknessMetres * 0.5f;
            Vector3 centre = seating.WorldPosition - (seating.WorldNormal * half);

            Vector3 offset = rotation * Vector3.Scale(nut.fastener.localSeatPoint, nutScale);
            position = centre - offset;
        }

        /// <summary>
        /// Distance along a line to the point nearest a ray: the standard
        /// closest approach of two skew lines.
        /// </summary>
        private static float ClosestApproach(Ray ray, Vector3 origin, Vector3 direction)
        {
            Vector3 w = origin - ray.origin;

            float a = Vector3.Dot(direction, direction);
            float b = Vector3.Dot(direction, ray.direction);
            float c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(direction, w);
            float e = Vector3.Dot(ray.direction, w);

            float denominator = (a * c) - (b * b);

            // Looking straight down the screw. Every point on it is equally
            // near, so there is nothing to choose between them.
            if (Mathf.Abs(denominator) < 1e-9f)
            {
                return 0f;
            }

            return ((b * e) - (c * d)) / denominator;
        }
    }
}
