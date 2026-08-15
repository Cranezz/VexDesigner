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
        /// The end of the stack, tightened flush against the last piece of
        /// metal - and on a bare screw, right up against the head. Where the
        /// user is looking along the screw makes no difference, because
        /// pointing at a screw means "put a nut on this screw" and that has
        /// one answer.
        /// </summary>
        public static NutSeating FindNutSeating(
            PlacedScrew screw, PartDefinition nut, Ray aim)
        {
            if (screw == null || nut == null || !nut.IsNut)
            {
                return default;
            }

            return FindSeating(screw, nut.NutThicknessMetres, aim);
        }

        /// <summary>
        /// Where anything of a given thickness comes to rest on a screw.
        ///
        /// A nut is only the commonest case. A C-channel held by one of its
        /// holes threads onto a screw by exactly the same rule - slide it up
        /// the shank until it meets metal - and it would be strange for the two
        /// to behave differently when the user is doing the same thing with
        /// their hands.
        /// </summary>
        /// <param name="thickness">
        /// How much shank the thing takes up: a nut's height, or the depth of
        /// the hole being threaded on.
        /// </param>
        public static NutSeating FindSeating(PlacedScrew screw, float thickness, Ray aim)
        {
            var seating = new NutSeating();

            if (screw == null)
            {
                return seating;
            }

            seating.Screw = screw;

            Vector3 seat = screw.Seat;
            Vector3 direction = screw.Direction;
            float length = screw.Length;

            // Reused rather than allocated: this runs every frame a nut is
            // held over a screw.
            ScrewLine.Gaps(screw.Passes, length, gaps);

            // The end of the stack, flush against the last piece of metal.
            // That is where a nut goes.
            //
            // An earlier version chose between every stretch of bare shank by
            // which one the cursor was nearest, so the answer moved as the user
            // looked around and the same screw took a nut in different places
            // depending on where they happened to be pointing. Clever, and
            // nobody asked for it. Pointing at a screw means "nut on this
            // screw", and that has one answer.
            float distance = LastExit(screw);
            bool inGap = false;

            if (distance + thickness > length + 1e-4f)
            {
                // No thread left past the metal. Fall back to the deepest
                // stretch of bare shank that will take it - a screw that only
                // reaches halfway can still clamp what it does reach, which is
                // better than refusing to fasten anything at all.
                distance = -1f;

                for (int i = gaps.Count - 1; i >= 0; i--)
                {
                    if (gaps[i].x + thickness <= length + 1e-4f)
                    {
                        distance = gaps[i].x;
                        inGap = i < gaps.Count - 1;
                        break;
                    }
                }

                if (distance < 0f)
                {
                    // Nowhere on this screw will take it. Not an error - the
                    // wrong nut or the wrong screw, and both are obvious from
                    // looking at them.
                    return default;
                }
            }

            seating.Distance = distance;
            seating.InGap = inGap;
            seating.Fits = true;
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
            // A nut has a right way up. Its axis is baked running from the face
            // that meets the metal toward the free end, so putting that axis
            // down the screw puts the correct face against the join - flange
            // first on a keps nut, flat face first on a nylock.
            //
            // Turning it whichever way needed the least rotation was the
            // earlier rule, and it seated half of them upside down.
            Vector3 axis = nut.fastener.localAxis.normalized;
            Vector3 current = (currentRotation * axis).normalized;
            Vector3 down = -seating.WorldNormal;

            rotation = Quaternion.FromToRotation(current, down) * currentRotation;

            // The seat point is that same face, so no half-height fudge is
            // needed: put the face on the join and the nut is placed.
            Vector3 offset = rotation * Vector3.Scale(nut.fastener.localSeatPoint, nutScale);
            position = seating.WorldPosition - offset;
        }

    }
}
