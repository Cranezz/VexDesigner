namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// A screw that has been driven into something, and the nut on it if there
    /// is one.
    ///
    /// This is where a pile of touching parts becomes an assembly. Mating two
    /// holes only puts parts against each other; nothing holds until a screw
    /// runs through them and finds something that grips - a nut, or a threaded
    /// hole in the last part. That is the rule the old Protobot used too, and
    /// it is the right one, because it is how a real robot works: a screw
    /// through four plates with nothing on the end falls out when you pick it
    /// up.
    /// </summary>
    [RequireComponent(typeof(PartInstance))]
    public sealed class PlacedScrew : MonoBehaviour
    {
        private readonly List<ScrewPass> passes = new List<ScrewPass>();

        private PartInstance cachedInstance;
        private PartDefinition cachedDefinition;
        private bool resolved;

        /// <summary>
        /// The part this screw is, resolved on first use rather than in Awake.
        ///
        /// Awake is not a reliable place for it. A component added to a part in
        /// the editor - which is what the fastener tests do, and what any tool
        /// that builds an assembly outside play mode would do - never gets one,
        /// and the screw then quietly answered every question with a default:
        /// zero length, and the object's own forward axis instead of the shank.
        /// It passed through metal without noticing any of it.
        /// </summary>
        private PartDefinition definition
        {
            get
            {
                Resolve();
                return cachedDefinition;
            }
        }

        private PartInstance instance
        {
            get
            {
                Resolve();
                return cachedInstance;
            }
        }

        private void Resolve()
        {
            if (resolved)
            {
                return;
            }

            resolved = true;
            cachedInstance = GetComponent<PartInstance>();
            cachedDefinition = cachedInstance == null ? null : cachedInstance.Definition;

            if (cachedDefinition == null)
            {
                Debug.LogError(
                    $"[Parts] {name} has a {nameof(PlacedScrew)} but no part " +
                    "definition. It cannot know its own length, so it will " +
                    "fasten nothing.", this);
            }
            else if (!cachedDefinition.IsScrew)
            {
                Debug.LogError(
                    $"[Parts] {cachedDefinition.displayName} is not a screw, " +
                    $"but has a {nameof(PlacedScrew)} on it.", this);
            }
        }

        /// <summary>The nut on this screw, if one has been fitted.</summary>
        public PartInstance Nut { get; private set; }

        /// <summary>Distance from under the head to the nut's near face.</summary>
        public float NutSeat { get; private set; }

        public IReadOnlyList<ScrewPass> Passes => passes;

        /// <summary>Usable shank length in metres.</summary>
        public float Length =>
            definition == null ? 0f : definition.ShankLengthMetres;

        /// <summary>World point under the head, where it meets the metal.</summary>
        public Vector3 Seat =>
            definition == null
                ? transform.position
                : transform.TransformPoint(definition.fastener.localSeatPoint);

        /// <summary>Unit vector down the shank, away from the head.</summary>
        public Vector3 Direction =>
            definition == null
                ? transform.forward
                : transform.TransformDirection(definition.fastener.localAxis).normalized;

        /// <summary>
        /// Works out what the screw currently runs through, and re-forms the
        /// assembly around it.
        ///
        /// Called whenever anything about the screw could have changed - when
        /// it is placed, when a nut goes on, when a part it holds is moved.
        /// Recomputing rather than tracking incrementally is deliberate: the
        /// parts it holds can be moved by the transform tool, deleted, or
        /// eventually moved by another player, and a cached list would quietly
        /// come to describe a robot that no longer exists.
        /// </summary>
        public void Refresh()
        {
            RecomputePasses();
            RebuildGroup();
        }

        /// <summary>
        /// Works out what the screw runs through, without joining anything.
        ///
        /// Kept separate from <see cref="Refresh"/> because a nut being *held*
        /// over a screw has to ask this question on every frame, and answering
        /// the grouping half of it as well would fasten the robot together
        /// while the user was still deciding where to put the nut - before any
        /// click, and with no way back.
        /// </summary>
        public void RecomputePasses()
        {
            if (definition == null)
            {
                return;
            }

            ScrewLine.Gather(Seat, Direction, Length, passes, GetComponent<PartHoles>());
        }

        /// <summary>
        /// Distance from under the head at which the deepest grip sits, or -1
        /// if nothing on this screw grips.
        ///
        /// The *deepest* one, so a screw through three plates into a threaded
        /// hole at the bottom holds all three rather than stopping at the first
        /// thing it met.
        /// </summary>
        public float GripDepth()
        {
            float deepest = -1f;

            foreach (ScrewPass pass in passes)
            {
                if (pass.Grips)
                {
                    deepest = Mathf.Max(deepest, pass.Exit);
                }
            }

            if (Nut != null)
            {
                deepest = Mathf.Max(deepest, NutSeat);
            }

            return deepest;
        }

        /// <summary>
        /// Puts everything the screw clamps into one group, so it moves as a
        /// single object.
        /// </summary>
        private void RebuildGroup()
        {
            float grip = GripDepth();

            if (grip < 0f || instance == null || instance.Group == null)
            {
                // Nothing holds it. The screw is sitting in a hole, not
                // fastening anything, and the parts stay independent.
                return;
            }

            PartGroup group = instance.Group;

            foreach (ScrewPass pass in passes)
            {
                // Only what lies between the head and the grip. A screw poking
                // out beyond the nut passes through nothing else, but a hole
                // further down the line than the nut is not clamped by it.
                if (pass.Entry > grip + 1e-4f)
                {
                    continue;
                }

                var member = pass.Part.GetComponent<PartInstance>();
                if (member != null && member.Group != null && member.Group != group)
                {
                    group.Merge(member.Group);
                }
            }

            if (Nut != null && Nut.Group != null && Nut.Group != group)
            {
                group.Merge(Nut.Group);
            }
        }

        /// <summary>
        /// Fits a nut at <paramref name="seatDistance"/> from under the head.
        /// </summary>
        public void AttachNut(PartInstance nut, float seatDistance)
        {
            Nut = nut;
            NutSeat = seatDistance;
            Refresh();
        }

        /// <summary>
        /// Whether a nut of <paramref name="thickness"/> still fits with its
        /// near face at <paramref name="seatDistance"/>.
        ///
        /// A nut hanging off the end of a screw is not fastened to anything, so
        /// this is a hard requirement rather than a cosmetic one - and the
        /// reason a builder reaches for a longer screw.
        /// </summary>
        public bool NutFits(float seatDistance, float thickness)
        {
            return seatDistance + thickness <= Length + 1e-4f;
        }
    }
}
