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
                // Followers are children of the leader, so the leader carries
                // them. Moving them as well would move them twice.
                if (IsWelded && part != Leader)
                {
                    continue;
                }

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
                if (part == null || (IsWelded && part != Leader))
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
        /// Makes the assembly one rigid body: one Rigidbody, several colliders.
        ///
        /// The followers are parented to the leader and have their own
        /// Rigidbody removed, which in Unity makes their colliders part of the
        /// leader's body. That is not an approximation of being joined - it is
        /// literally one body with several shapes, so bolted parts cannot drift
        /// apart at any speed, under any force, and the assembly falls, tips
        /// and lands as the single object it is.
        ///
        /// Two earlier attempts came up short in the same way, by treating the
        /// weld as something that only had to hold while the user was holding
        /// the robot. Copying the leader's pose onto the others every physics
        /// step looked right standing still and came apart the moment anything
        /// moved, because the leader is interpolated between steps and copies
        /// are not. Welding only during a carry survived that, and then let the
        /// robot fall to pieces the instant it was unfrozen - each part its own
        /// body again, each finding its own way to the floor.
        ///
        /// Joints were the other option and are worse. A joint is a very stiff
        /// spring, so it stretches under load and rings afterwards; two dozen
        /// of them is a robot that wobbles.
        /// </summary>
        public void Weld()
        {
            if (IsWelded || members.Count < 2)
            {
                return;
            }

            Leader = ChooseLeader();

            if (Leader == null)
            {
                return;
            }

            Transform lead = Leader.transform;

            foreach (PartInstance part in members)
            {
                if (part == null || part == Leader)
                {
                    continue;
                }

                var body = part.GetComponent<Rigidbody>();

                welds.Add(new Weld_
                {
                    part = part,
                    parent = part.transform.parent,
                    definition = part.Definition,
                });

                // A child with its own Rigidbody stays a separate body however
                // it is parented, so it has to go for the colliders to be
                // adopted into the leader's.
                if (body != null)
                {
                    Object.DestroyImmediate(body);
                }

                part.transform.SetParent(lead, true);
            }

            Welded.Add(this);
        }

        public void Unweld()
        {
            foreach (Weld_ weld in welds)
            {
                if (weld.part == null)
                {
                    continue;
                }

                weld.part.transform.SetParent(weld.parent, true);

                if (weld.definition != null &&
                    weld.part.GetComponent<Rigidbody>() == null)
                {
                    PartFactory.AddPhysics(weld.part.gameObject, weld.definition);
                }

                var body = weld.part.GetComponent<Rigidbody>();

                if (body != null)
                {
                    // Only the group's current state decides this. Remembering
                    // what a part had been when it was welded meant unfreezing
                    // an assembly left the followers pinned in mid-air while
                    // the leader fell out of the robot.
                    body.isKinematic = IsFrozen;
                    body.useGravity = !IsFrozen;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }

            welds.Clear();
            Welded.Remove(this);
            Leader = null;
        }

        /// <summary>
        /// Takes every assembly apart, so the graph can be worked out again.
        ///
        /// Static because the welds outlive the groups that made them: groups
        /// are thrown away and rebuilt whenever a fastener changes, and a weld
        /// whose group had been discarded would leave those parts parented to
        /// another with no bodies of their own and no way back.
        /// </summary>
        public static void UnweldAll()
        {
            for (int i = Welded.Count - 1; i >= 0; i--)
            {
                Welded[i].Unweld();
            }

            Welded.Clear();
        }

        /// <summary>
        /// The part whose Rigidbody the whole assembly moves on.
        ///
        /// The heaviest, so the body's centre of mass and inertia are closest
        /// to the truth - a robot swinging about its C-channel behaves far
        /// better than one swinging about whichever nut happened to be first
        /// in the list.
        /// </summary>
        private PartInstance ChooseLeader()
        {
            PartInstance best = null;
            float heaviest = -1f;

            foreach (PartInstance part in members)
            {
                if (part == null)
                {
                    continue;
                }

                float mass = part.Definition == null ? 0f : part.Definition.MassGrams;

                if (mass > heaviest)
                {
                    heaviest = mass;
                    best = part;
                }
            }

            return best;
        }

        /// <summary>The body the assembly moves on, or null if it is one part.</summary>
        public PartInstance Leader { get; private set; }

        public bool IsWelded => Leader != null;

        private struct Weld_
        {
            public PartInstance part;
            public Transform parent;
            public PartDefinition definition;
        }

        private readonly List<Weld_> welds = new List<Weld_>();

        private static readonly List<PartGroup> Welded = new List<PartGroup>();

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
