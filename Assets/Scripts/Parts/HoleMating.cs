namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Works out where a part has to sit for a chosen hole to land flat against
    /// a hole on another part.
    ///
    /// Kept apart from the interaction code because it is pure geometry with
    /// no notion of clicks or highlights: given two hole faces, it answers
    /// where the moving part goes. That makes it testable, and it will be
    /// reused verbatim when a screw drives the same alignment.
    ///
    /// The pose is computed from a *reference* orientation rather than from the
    /// part's current one. That distinction is what makes a live preview
    /// possible: while a part is being dragged by a hole it is written to every
    /// frame, so a calculation that read back its own last answer would drift -
    /// the roll snap would re-snap an already-snapped part and any manual
    /// rotation would compound frame after frame. Feeding in the free
    /// orientation each time means the same inputs always give the same pose.
    ///
    /// Mating does not join the parts. They are two parts touching until a
    /// screw goes through them - which is when a group forms and they start
    /// moving as one.
    /// </summary>
    public static class HoleMating
    {
        /// <summary>
        /// Where the moving part must be for <paramref name="moverFace"/> to
        /// meet <paramref name="target"/> face to face.
        /// </summary>
        /// <param name="moverFace">
        /// The grabbed hole face, in the moving part's own local space. Local
        /// because it must not change as the part is moved about.
        /// </param>
        /// <param name="freeRotation">
        /// The orientation the part would have if it were not snapped. The roll
        /// snap measures from here, so the part ends up at the quarter turn
        /// nearest the way it is actually being held.
        /// </param>
        /// <param name="rollSnapDegrees">
        /// Increment the automatic roll is rounded to; 0 disables it.
        /// </param>
        /// <param name="extraRollDegrees">
        /// Manual rotation about the join, applied on top of the snap.
        /// </param>
        /// <param name="moverScale">
        /// The moving part's lossy scale, needed to turn its local hole offset
        /// into a world one.
        /// </param>
        /// <param name="zeroDirection">
        /// Where the part's reference axis points at zero manual rotation. The
        /// rotation dial uses it as its zero mark, so a reading of forty-five
        /// degrees means forty-five degrees away from square.
        /// </param>
        public static bool ComputePose(
            HoleFace moverFace,
            Quaternion freeRotation,
            HoleHit target,
            float rollSnapDegrees,
            float extraRollDegrees,
            Vector3 moverScale,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 zeroDirection)
        {
            position = Vector3.zero;
            rotation = freeRotation;
            zeroDirection = Vector3.forward;

            if (!target.IsValid)
            {
                return false;
            }

            Vector3 axis = target.WorldNormal;

            // Turn the part so its hole faces back at the target's. Opposed
            // normals, not aligned ones - that is what "flat against each
            // other" means for sheet metal; aligning them would bury one part
            // inside the other.
            rotation = SquareOnto(
                freeRotation, moverFace.localNormal, target, rollSnapDegrees,
                out zeroDirection);

            if (!Mathf.Approximately(extraRollDegrees, 0f))
            {
                rotation = Quaternion.AngleAxis(extraRollDegrees, axis) * rotation;
            }

            // Then place it so the two openings are concentric. Rotation is
            // resolved first because turning the part moves its hole, so any
            // translation worked out beforehand would be stale.
            Vector3 offset = rotation * Vector3.Scale(moverFace.localPosition, moverScale);
            position = target.WorldPosition - offset;

            return true;
        }

        /// <summary>
        /// Brings <paramref name="mover"/> to <paramref name="target"/>, face to
        /// face, and pins it there.
        /// </summary>
        public static bool Mate(
            HoleHit mover, HoleHit target,
            float rollSnapDegrees = 90f, float extraRollDegrees = 0f)
        {
            if (!mover.IsValid || !target.IsValid || mover.Part == target.Part)
            {
                return false;
            }

            Transform moving = mover.Part.transform;

            if (!ComputePose(
                    mover.Face, moving.rotation, target,
                    rollSnapDegrees, extraRollDegrees, moving.lossyScale,
                    out Vector3 position, out Quaternion rotation, out _))
            {
                return false;
            }

            moving.SetPositionAndRotation(position, rotation);
            SyncBody(moving);
            return true;
        }

        /// <summary>
        /// Turns the part so its hole faces the target's and its edges line up
        /// with the target's edges.
        ///
        /// Two attempts at this were wrong in instructive ways, and both were
        /// wrong about the *swing* rather than the twist.
        ///
        /// Making the hole face the right way is a rotation with one degree of
        /// freedom left over, and the obvious way to get there -
        /// FromToRotation onto the mating axis - takes the shortest arc. The
        /// shortest arc turns about whatever in-plane axis happens to be
        /// nearest, which is almost never one of the part's own edges. Removing
        /// the leftover twist afterwards, which is what the previous version
        /// did, cannot fix that: the part is already tilted about an arbitrary
        /// axis, and twisting it about the mating axis leaves it tilted.
        ///
        /// So the answer is not to correct a rotation but to choose one. A part
        /// square with another is at one of the twenty-four orientations of a
        /// cube relative to it. Only some of those point the hole the right way,
        /// and among those the nearest to how the part is being held is the one
        /// the user meant. Picking from that set means "square" is true by
        /// construction rather than by arithmetic that might not quite land.
        /// </summary>
        private static Quaternion SquareOnto(
            Quaternion freeRotation, Vector3 localNormal, HoleHit target,
            float snapDegrees, out Vector3 zeroDirection)
        {
            Vector3 axis = target.WorldNormal.normalized;
            Quaternion frame = target.Part.transform.rotation;

            zeroDirection = FlattestAxis(frame, axis);

            // Opposed normals, not aligned ones - that is what "flat against
            // each other" means for sheet metal.
            Vector3 wanted = -axis;

            Vector3 held = (freeRotation * localNormal).normalized;
            Quaternion loose = Quaternion.FromToRotation(held, wanted) * freeRotation;

            if (snapDegrees <= 0f)
            {
                return loose;
            }

            Quaternion[] cube = CubeRotations();

            Quaternion best = loose;
            float bestAngle = float.MaxValue;
            bool found = false;

            for (int i = 0; i < cube.Length; i++)
            {
                Quaternion candidate = frame * cube[i];

                // Does this orientation point the hole at the target at all?
                if (Vector3.Dot(candidate * localNormal, wanted) < 0.995f)
                {
                    continue;
                }

                // Nearest to how the part is actually being held, so the four
                // ways round it could sit are decided by the user rather than
                // by whichever was enumerated first.
                float angle = Quaternion.Angle(candidate, freeRotation);

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = candidate;
                    found = true;
                }
            }

            if (!found)
            {
                // The hole's normal does not run along one of the part's own
                // axes - a hole on a slanted face. Nothing here is square to
                // anything, so the loose alignment is the honest answer.
                return loose;
            }

            // A detected normal can be a fraction of a degree off true, which
            // would leave the faces very slightly apart. Correcting it costs
            // less squareness than that gap costs contact.
            return Quaternion.FromToRotation(best * localNormal, wanted) * best;
        }

        /// <summary>
        /// The twenty-four ways a cube can sit: every rotation that maps a set
        /// of axes onto itself, and so every orientation in which two
        /// rectangular parts are square with each other.
        /// </summary>
        private static Quaternion[] CubeRotations()
        {
            if (cubeRotations != null)
            {
                return cubeRotations;
            }

            var found = new List<Quaternion>(24);

            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int z = 0; z < 4; z++)
                    {
                        var candidate = Quaternion.Euler(x * 90f, y * 90f, z * 90f);

                        bool duplicate = false;

                        for (int i = 0; i < found.Count; i++)
                        {
                            if (Quaternion.Angle(found[i], candidate) < 1f)
                            {
                                duplicate = true;
                                break;
                            }
                        }

                        if (!duplicate)
                        {
                            found.Add(candidate);
                        }
                    }
                }
            }

            cubeRotations = found.ToArray();
            return cubeRotations;
        }

        private static Quaternion[] cubeRotations;

        /// <summary>
        /// Whichever of a frame's three axes lies most nearly in the plane
        /// perpendicular to <paramref name="axis"/>, projected into it.
        /// </summary>
        private static Vector3 FlattestAxis(Quaternion frame, Vector3 axis)
        {
            Vector3 best = Vector3.zero;
            float bestAlignment = float.MaxValue;

            for (int i = 0; i < 3; i++)
            {
                Vector3 candidate = frame * (i == 0 ? Vector3.right
                    : i == 1 ? Vector3.up : Vector3.forward);

                float alignment = Mathf.Abs(Vector3.Dot(candidate, axis));

                if (alignment < bestAlignment)
                {
                    bestAlignment = alignment;
                    best = candidate;
                }
            }

            Vector3 flat = Vector3.ProjectOnPlane(best, axis);

            // Only reachable if the frame is degenerate, but a zero-length zero
            // mark would put NaN straight into the dial.
            return flat.sqrMagnitude < 1e-10f
                ? Vector3.ProjectOnPlane(Vector3.up, axis).normalized
                : flat.normalized;
        }

        /// <summary>
        /// Pushes the transform into the Rigidbody and stops it dead.
        ///
        /// A mated part is placed deliberately, so it is pinned rather than
        /// left to physics - gravity would otherwise pull it off the face it
        /// was just aligned to. The body is also told its new pose directly,
        /// because an interpolated body reconstructs its rendered position from
        /// previous physics steps and would visibly snap back.
        /// </summary>
        public static void SyncBody(Transform moving)
        {
            var instance = moving.GetComponent<PartInstance>();
            instance?.Group?.SetFrozen(true);

            var body = moving.GetComponent<Rigidbody>();
            if (body == null)
            {
                return;
            }

            body.position = moving.position;
            body.rotation = moving.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}
