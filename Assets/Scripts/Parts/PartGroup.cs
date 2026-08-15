namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// A set of parts treated as one thing.
    ///
    /// Today every part is its own group of one, which looks like pointless
    /// indirection. It is not. Once screws join parts together, freezing,
    /// selecting and moving all have to act on the whole assembly rather than
    /// the single part under the cursor - and retrofitting that later means
    /// touching every one of those systems. Introducing the concept now, while
    /// it is trivial, costs almost nothing and means those systems are already
    /// written against the right unit.
    ///
    /// Plain C#, no MonoBehaviour: a group is document state (see
    /// ARCHITECTURE.md section 6), not a scene object.
    /// </summary>
    public sealed class PartGroup
    {
        private readonly List<PartInstance> members = new List<PartInstance>();

        public IReadOnlyList<PartInstance> Members => members;

        public bool IsFrozen { get; private set; }

        public static PartGroup CreateFor(PartInstance part)
        {
            var group = new PartGroup();
            group.Add(part);
            return group;
        }

        public void Add(PartInstance part)
        {
            if (part == null || members.Contains(part))
            {
                return;
            }

            members.Add(part);
            part.AssignGroup(this);
        }

        /// <summary>
        /// Absorbs another group. This is what joining two parts with a screw
        /// will call.
        /// </summary>
        public void Merge(PartGroup other)
        {
            if (other == null || ReferenceEquals(other, this))
            {
                return;
            }

            // Copy first: Add reassigns each part's group, which mutates the
            // list being iterated.
            var incoming = new List<PartInstance>(other.members);
            foreach (PartInstance part in incoming)
            {
                Add(part);
            }

            other.members.Clear();

            // A merged assembly inherits frozen-ness if either side was held,
            // which is less surprising than silently dropping an anchored part.
            if (other.IsFrozen)
            {
                SetFrozen(true);
            }
        }

        /// <summary>
        /// Pins the group in mid-air, or releases it back to physics.
        /// </summary>
        public void SetFrozen(bool frozen)
        {
            IsFrozen = frozen;

            foreach (PartInstance part in members)
            {
                if (part == null)
                {
                    continue;
                }

                var body = part.GetComponent<Rigidbody>();

                // A follower has no body of its own while the assembly is being
                // carried - it is part of the leader's. Freezing it is the
                // leader's business.
                if (body != null)
                {
                    body.isKinematic = frozen;

                    if (frozen)
                    {
                        // Clear momentum, or the part lurches when unfrozen
                        // using velocity it accumulated before being pinned.
                        body.linearVelocity = Vector3.zero;
                        body.angularVelocity = Vector3.zero;
                    }
                }

                var highlight = part.GetComponent<Highlightable>();
                if (highlight != null)
                {
                    highlight.SetPinned(frozen);
                }
            }
        }

        /// <summary>Marks every member as held, or releases them.</summary>
        public void SetGrabbed(bool grabbed)
        {
            foreach (PartInstance part in members)
            {
                var highlight = part == null ? null : part.GetComponent<Highlightable>();
                if (highlight != null)
                {
                    highlight.SetGrabbed(grabbed);
                }
            }
        }

        /// <summary>
        /// Wakes any sleeping body resting against this group.
        ///
        /// Unity puts settled rigidbodies to sleep to save cost, and a sleeping
        /// body does not notice that whatever was holding it up has gone. Take
        /// a part out from under a stack and the stack hangs in mid-air until
        /// something else disturbs it. Waking the neighbours on every move is
        /// what makes support actually behave like support.
        /// </summary>
        public void WakeNeighbours()
        {
            foreach (PartInstance part in members)
            {
                var renderer = part == null ? null : part.GetComponentInChildren<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                Bounds bounds = renderer.bounds;

                // Padded, because a resting contact sits fractionally outside
                // the bounds and would otherwise be missed.
                Collider[] nearby = Physics.OverlapBox(
                    bounds.center,
                    bounds.extents + (Vector3.one * 0.02f),
                    Quaternion.identity,
                    ~0,
                    QueryTriggerInteraction.Ignore);

                foreach (Collider collider in nearby)
                {
                    var body = collider.attachedRigidbody;
                    if (body != null && !body.isKinematic)
                    {
                        body.WakeUp();
                    }
                }
            }
        }

        /// <summary>
        /// Moves every member by the same offset.
        ///
        /// Writes through the Rigidbody where there is one. Setting
        /// transform.position on an interpolated body does not stick: the
        /// interpolator overwrites the rendered transform from the last two
        /// physics positions every frame, so the part visibly refuses to move
        /// while its transform is being set correctly - which is exactly how
        /// the transform tool appeared to be doing nothing.
        /// </summary>
        public void Translate(Vector3 delta)
        {
            foreach (PartInstance part in members)
            {
                if (part == null)
                {
                    continue;
                }

                var body = part.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position += delta;
                }

                part.transform.position += delta;
            }
        }

        /// <summary>Rotates the whole group about a shared pivot.</summary>
        public void Rotate(Quaternion delta, Vector3 pivot)
        {
            foreach (PartInstance part in members)
            {
                if (part == null)
                {
                    continue;
                }

                Transform t = part.transform;
                t.rotation = delta * t.rotation;
                t.position = pivot + (delta * (t.position - pivot));

                // Keep the body in step, for the same reason as Translate.
                var body = part.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.position = t.position;
                    body.rotation = t.rotation;
                }
            }
        }

        /// <summary>
        /// Makes the whole assembly one rigid body, held together by the
        /// member in hand.
        ///
        /// The followers are *parented* to the leader and have their own
        /// Rigidbody removed, which in Unity makes their colliders part of the
        /// leader's body. That is not an approximation of being joined - it is
        /// one body with several shapes, so the parts cannot drift apart at any
        /// speed, and the assembly collides with the bench as the single object
        /// it is.
        ///
        /// The first attempt copied the leader's pose onto the followers every
        /// physics step. It looked right standing still and came apart the
        /// moment anything moved quickly, which is what a copied pose always
        /// does: the leader is interpolated between physics steps and the
        /// copies are not, so they lag by up to a frame - and a frame at
        /// carrying speed is a visible gap.
        ///
        /// Joints were the other option and are worse here. A joint is a spring
        /// with a very high stiffness, so it stretches under load and rings
        /// afterwards; two dozen of them in a robot is a machine that wobbles.
        /// </summary>
        public void BeginFollow(PartInstance leader)
        {
            EndFollow();

            if (leader == null || members.Count < 2)
            {
                return;
            }

            followLeader = leader;
            CarriedLeader = leader;
            Transform lead = leader.transform;

            foreach (PartInstance part in members)
            {
                if (part == null || part == leader)
                {
                    continue;
                }

                var body = part.GetComponent<Rigidbody>();

                follow.Add(new Follower
                {
                    part = part,
                    parent = part.transform.parent,
                    definition = part.Definition,
                    wasKinematic = body != null && body.isKinematic,
                });

                // A child with its own Rigidbody stays a separate body no
                // matter how it is parented, so it has to go for the colliders
                // to be adopted.
                if (body != null)
                {
                    Object.Destroy(body);
                }

                part.transform.SetParent(lead, true);
            }
        }

        /// <summary>
        /// Kept for callers that used to drive the followers by hand. The
        /// parenting does the work now, every frame, for free.
        /// </summary>
        public void UpdateFollow()
        {
        }

        public void EndFollow()
        {
            foreach (Follower follower in follow)
            {
                if (follower.part == null)
                {
                    continue;
                }

                follower.part.transform.SetParent(follower.parent, true);

                // Physics back, from the part's own specification rather than
                // from whatever the destroyed body happened to hold.
                if (follower.definition != null &&
                    follower.part.GetComponent<Rigidbody>() == null)
                {
                    PartFactory.AddPhysics(follower.part.gameObject, follower.definition);
                }

                var body = follower.part.GetComponent<Rigidbody>();

                if (body != null)
                {
                    body.isKinematic = IsFrozen || follower.wasKinematic;
                    body.useGravity = !body.isKinematic;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            follow.Clear();

            if (CarriedLeader == followLeader)
            {
                CarriedLeader = null;
            }

            followLeader = null;
        }

        /// <summary>
        /// The part currently holding an assembly together in the user's hand,
        /// if any.
        ///
        /// Static because the weld has to survive the assembly being worked
        /// out again. Groups are thrown away and rebuilt whenever a fastener
        /// changes, and the records of what was welded to what live on the
        /// group - so placing a nut while holding the robot discarded them,
        /// leaving those parts parented to the leader with no bodies of their
        /// own and no way back. They looked exactly as though the new part had
        /// stolen them.
        /// </summary>
        public static PartInstance CarriedLeader { get; private set; }

        private struct Follower
        {
            public PartInstance part;
            public Transform parent;
            public PartDefinition definition;
            public bool wasKinematic;
        }

        private readonly List<Follower> follow = new List<Follower>();
        private PartInstance followLeader;

        /// <summary>True while the assembly is welded together in hand.</summary>
        public bool IsCarried => followLeader != null;

        /// <summary>Centre of the group's rendered bounds.</summary>
        public Vector3 GetCentre()
        {
            bool any = false;
            Bounds bounds = default;

            foreach (PartInstance part in members)
            {
                var renderer = part == null ? null : part.GetComponentInChildren<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return any ? bounds.center : Vector3.zero;
        }
    }
}
