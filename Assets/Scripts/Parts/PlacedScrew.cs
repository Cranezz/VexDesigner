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
    [ExecuteAlways]
    [RequireComponent(typeof(PartInstance))]
    public sealed class PlacedScrew : MonoBehaviour
    {
        /// <summary>
        /// Every screw currently driven into something. These are what the
        /// assembly graph is derived from, so they are the closest thing this
        /// project has to a source of truth about what is joined to what.
        /// </summary>
        private static readonly List<PlacedScrew> Live = new List<PlacedScrew>();

        public static IReadOnlyList<PlacedScrew> All => Live;

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

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
            Assembly.Rebuild();
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
        /// <param name="held">
        /// A part currently in the user's hand, which must not be counted.
        ///
        /// A nut being offered to this screw is already positioned on it, so
        /// its own threaded hole reads as something the screw passes through.
        /// That changed where the next seat was, which moved the nut, which
        /// changed the passes again - the nut visibly flicking between the
        /// cursor and the metal, twice a frame. It is being *held*: it is not
        /// on the screw until it is let go.
        /// </param>
        public void RecomputePasses(PartHoles held = null)
        {
            if (definition == null)
            {
                return;
            }

            ScrewLine.Gather(
                Seat, Direction, Length, passes, GetComponent<PartHoles>(), held);
        }

        /// <summary>
        /// Distance from under the head at which the deepest grip sits, or -1
        /// if nothing on this screw grips.
        ///
        /// The *deepest* one, so a screw through three plates into a threaded
        /// hole at the bottom holds all three rather than stopping at the first
        /// thing it met.
        ///
        /// A nut needs no special case. It is a part with a threaded hole, so
        /// once it is on the screw it turns up as a gripping pass like any
        /// other. An earlier version also stored which nut was fitted and where,
        /// which was the same fact written down twice - and the two could
        /// disagree, which is precisely the bookkeeping this design exists to
        /// avoid. Where the nut *is* decides what is held, and nothing else.
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

            return deepest;
        }

        /// <summary>
        /// Whether a nut of <paramref name="thickness"/> still fits with its
        /// near face at <paramref name="seatDistance"/>.
        ///
        /// Not an error when it does not. A nut that will not go on simply does
        /// not snap - the same as reaching for one in the workshop, finding no
        /// thread left, and putting it back. Telling the user off for pointing
        /// at the wrong screw would be noise, since the screw being too short
        /// is obvious from looking at it.
        /// </summary>
        public bool NutFits(float seatDistance, float thickness)
        {
            return seatDistance + thickness <= Length + 1e-4f;
        }
    }
}
