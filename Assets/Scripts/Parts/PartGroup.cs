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

        /// <summary>Moves every member by the same offset.</summary>
        public void Translate(Vector3 delta)
        {
            foreach (PartInstance part in members)
            {
                if (part != null)
                {
                    part.transform.position += delta;
                }
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
            }
        }

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
