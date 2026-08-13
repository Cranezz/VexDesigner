namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Moves one part so that a chosen hole sits flat against a hole on
    /// another part.
    ///
    /// Kept apart from the interaction code because it is pure geometry with
    /// no notion of clicks or highlights: given two hole faces, it works out
    /// where the moving part has to be. That makes it testable, and it will be
    /// reused verbatim when a screw drives the same alignment.
    ///
    /// Mating does not join the parts. They are two parts touching until a
    /// screw goes through them - which is when a group forms and they start
    /// moving as one.
    /// </summary>
    public static class HoleMating
    {
        /// <summary>
        /// Brings <paramref name="mover"/> to <paramref name="target"/>, face
        /// to face.
        ///
        /// The two surfaces end up in contact with their normals opposed, which
        /// is what "flat against each other" means for sheet metal - not the
        /// normals aligned, which would bury one part inside the other.
        /// </summary>
        public static bool Mate(HoleHit mover, HoleHit target, float rollSnapDegrees = 90f)
        {
            if (!mover.IsValid || !target.IsValid || mover.Part == target.Part)
            {
                return false;
            }

            Transform moving = mover.Part.transform;

            // Turn the moving part so its hole faces back at the target's.
            Quaternion align = Quaternion.FromToRotation(
                mover.WorldNormal, -target.WorldNormal);

            moving.rotation = align * moving.rotation;

            SnapRoll(moving, target, rollSnapDegrees);

            // Then slide it so the two openings are concentric. Rotation first:
            // turning about the part's own origin moves the hole, so any
            // translation worked out beforehand would be stale.
            Vector3 holeNow = moving.TransformPoint(mover.Face.localPosition);
            moving.position += target.WorldPosition - holeNow;

            SyncBody(moving);
            return true;
        }

        /// <summary>
        /// Turns the part about the mating axis until its own axes line up with
        /// the target's, to the nearest quarter turn.
        ///
        /// Without this the parts meet at whatever angle they happened to be
        /// held at, which is almost never wanted - two C-channels bolted
        /// together are square to each other. Snapping to quarter turns gives
        /// that for free while still allowing the four sensible orientations.
        /// </summary>
        private static void SnapRoll(Transform moving, HoleHit target, float snapDegrees)
        {
            Vector3 axis = target.WorldNormal;
            Transform reference = target.Part.transform;

            // Compare the two parts' right vectors, flattened onto the mating
            // plane. Anything perpendicular to the axis works; right is as good
            // a choice as any and is stable for parts that are axis-aligned.
            Vector3 movingRef = Vector3.ProjectOnPlane(moving.right, axis);
            Vector3 targetRef = Vector3.ProjectOnPlane(reference.right, axis);

            // Degenerate when the part's right happens to run along the mating
            // axis; forward is then guaranteed not to.
            if (movingRef.sqrMagnitude < 1e-6f)
            {
                movingRef = Vector3.ProjectOnPlane(moving.forward, axis);
                targetRef = Vector3.ProjectOnPlane(reference.forward, axis);
            }

            if (movingRef.sqrMagnitude < 1e-6f || targetRef.sqrMagnitude < 1e-6f)
            {
                return;
            }

            float angle = Vector3.SignedAngle(movingRef.normalized, targetRef.normalized, axis);

            if (snapDegrees > 0f)
            {
                angle = Mathf.Round(angle / snapDegrees) * snapDegrees;
            }

            moving.rotation = Quaternion.AngleAxis(angle, axis) * moving.rotation;
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
        private static void SyncBody(Transform moving)
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
