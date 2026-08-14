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
        /// Rolls the part about the mating axis until its own axes line up with
        /// the target's, to the nearest quarter turn.
        ///
        /// Without this the parts meet at whatever angle they happened to be
        /// held at, which is almost never wanted - two C-channels bolted
        /// together are square to each other. Snapping to quarter turns gives
        /// that for free while still allowing the four sensible orientations,
        /// and anything else is reached by turning the dial.
        /// </summary>
        private static Quaternion SnapRoll(
            Quaternion rotation, HoleHit target, float snapDegrees, out Vector3 zeroDirection)
        {
            Vector3 axis = target.WorldNormal;
            Transform reference = target.Part.transform;

            // Compare the two parts' right vectors, flattened onto the mating
            // plane. Anything perpendicular to the axis works; right is as good
            // a choice as any and is stable for parts that are axis-aligned.
            Vector3 movingRef = Vector3.ProjectOnPlane(rotation * Vector3.right, axis);
            Vector3 targetRef = Vector3.ProjectOnPlane(reference.right, axis);

            // Degenerate when the part's right happens to run along the mating
            // axis; forward is then guaranteed not to.
            if (movingRef.sqrMagnitude < 1e-6f)
            {
                movingRef = Vector3.ProjectOnPlane(rotation * Vector3.forward, axis);
                targetRef = Vector3.ProjectOnPlane(reference.forward, axis);
            }

            if (movingRef.sqrMagnitude < 1e-6f || targetRef.sqrMagnitude < 1e-6f)
            {
                zeroDirection = movingRef.sqrMagnitude > 1e-6f
                    ? movingRef.normalized
                    : Vector3.ProjectOnPlane(Vector3.up, axis).normalized;

                return rotation;
            }

            float angle = Vector3.SignedAngle(movingRef.normalized, targetRef.normalized, axis);

            if (snapDegrees > 0f)
            {
                angle = Mathf.Round(angle / snapDegrees) * snapDegrees;
            }

            Quaternion snapped = Quaternion.AngleAxis(angle, axis) * rotation;

            zeroDirection = (Quaternion.AngleAxis(angle, axis) * movingRef).normalized;
            return snapped;
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
