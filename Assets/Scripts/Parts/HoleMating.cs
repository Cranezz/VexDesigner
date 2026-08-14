namespace VexDesigner.Parts
{
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
            Vector3 moverNormal = (freeRotation * moverFace.localNormal).normalized;
            rotation = Quaternion.FromToRotation(moverNormal, -axis) * freeRotation;

            rotation = SnapRoll(rotation, target, rollSnapDegrees, out zeroDirection);

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
        /// Rolls the part about the mating axis until it sits square with the
        /// part it is joining, to the nearest quarter turn.
        ///
        /// Without this the parts meet at whatever angle they happened to be
        /// held at, which is almost never wanted - two C-channels bolted
        /// together are square to each other. Snapping to quarter turns gives
        /// that for free while still allowing the four sensible orientations,
        /// and anything else is reached by turning the dial.
        ///
        /// Measured by taking the rotation *between* the two parts and pulling
        /// out its twist about the mating axis, rather than by comparing a
        /// chosen pair of axis vectors. Comparing vectors was the first attempt
        /// and it does not hold up: the part's chosen reference axis is often
        /// nearly parallel to the mating axis - which is the normal case when
        /// bolting to a flange - and its projection onto the mating plane is
        /// then a very short vector whose direction is mostly rounding error.
        /// The angle it gave was arbitrary, which is why holding shift never
        /// quite landed the part square with the one it was joining.
        ///
        /// A twist has no such degenerate case. Zero twist means the part is
        /// oriented exactly as the one it meets, apart from the turn that makes
        /// them face each other, so the snap increments count from a position
        /// that actually means something.
        /// </summary>
        private static Quaternion SnapRoll(
            Quaternion rotation, HoleHit target, float snapDegrees, out Vector3 zeroDirection)
        {
            Vector3 axis = target.WorldNormal.normalized;
            Transform reference = target.Part.transform;

            // The dial's zero mark: a direction fixed in the target part, so
            // the reading means "this far from square with that part". Which of
            // the target's own axes is used does not matter as long as it is
            // stable, so the one that lies flattest in the mating plane wins -
            // the same near-parallel projection that broke the old comparison
            // would only make the mark jitter here.
            zeroDirection = FlattestAxis(reference.rotation, axis);

            float twist = TwistAbout(rotation * Quaternion.Inverse(reference.rotation), axis);

            // Turn back by the twist to sit square, then forward again by the
            // nearest whole increment.
            float correction = -twist;

            if (snapDegrees > 0f)
            {
                correction = (Mathf.Round(twist / snapDegrees) * snapDegrees) - twist;
            }

            return Quaternion.AngleAxis(correction, axis) * rotation;
        }

        /// <summary>
        /// How far <paramref name="rotation"/> turns about
        /// <paramref name="axis"/>, ignoring any tilt away from it.
        ///
        /// The swing-twist decomposition: a quaternion's vector part projected
        /// onto the axis, kept with the original scalar part, is exactly the
        /// component of the rotation that spins about that axis.
        /// </summary>
        private static float TwistAbout(Quaternion rotation, Vector3 axis)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projected = Vector3.Project(vector, axis);

            var twist = new Quaternion(projected.x, projected.y, projected.z, rotation.w);

            // A half-turn about something perpendicular to the axis leaves
            // nothing to project: the rotation has no twist component at all.
            if (twist.x * twist.x + twist.y * twist.y +
                twist.z * twist.z + twist.w * twist.w < 1e-12f)
            {
                return 0f;
            }

            twist.Normalize();
            twist.ToAngleAxis(out float angle, out Vector3 twistAxis);

            if (angle > 180f)
            {
                angle -= 360f;
            }

            // ToAngleAxis always reports a positive turn about *some* axis; if
            // that axis came out reversed, the turn is the other way round.
            return Vector3.Dot(twistAxis, axis) < 0f ? -angle : angle;
        }

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
