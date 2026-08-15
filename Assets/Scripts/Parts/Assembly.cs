namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Works out which parts are joined to which, from the fasteners actually
    /// holding them.
    ///
    /// Rebuilt from nothing every time rather than adjusted. The first version
    /// merged groups as screws went in, which can only ever join things: once
    /// two parts were in a group there was no record of *why*, so taking the
    /// nut off could not take them apart again. Worse, a part held by two
    /// screws would come apart when either one was removed, because nothing
    /// knew the other was still holding it.
    ///
    /// Deriving the whole answer from the current set of fasteners avoids both.
    /// Remove a nut, rebuild, and the parts it alone held fall apart while
    /// everything else stays exactly as it was - not because anything tracked
    /// the difference, but because the question is asked afresh.
    ///
    /// This also happens to be the form multiplayer needs (ARCHITECTURE.md
    /// section 6): the fasteners are the document, and grouping is presentation
    /// derived from it. Two machines running this on the same fasteners get the
    /// same assemblies without exchanging a word about groups.
    /// </summary>
    public static class Assembly
    {
        /// <summary>
        /// Recomputes every group in the workshop.
        ///
        /// Cheap enough to call on any change: a robot is hundreds of parts,
        /// not millions, and the alternative is bookkeeping that is wrong in
        /// ways nobody notices until a build falls apart.
        /// </summary>
        /// <param name="held">
        /// A part in the user's hand, which fastens nothing while it is there.
        ///
        /// This is how a build comes apart. Picking up a nut rebuilds without
        /// it, so whatever that nut alone was holding falls free immediately -
        /// while anything a second screw still holds stays exactly as it was,
        /// because the question is asked of every fastener rather than of a
        /// remembered answer.
        /// </param>
        public static void Rebuild(PartInstance held = null)
        {
            PartHoles heldHoles = held == null ? null : held.GetComponent<PartHoles>();

            IReadOnlyList<PartInstance> parts = PartInstance.All;
            IReadOnlyList<PlacedScrew> screws = PlacedScrew.All;

            // Frozen-ness belongs to the user, not to the graph, so it has to
            // survive a rebuild. Remembered per part rather than per group,
            // since the groups are about to stop existing.
            var frozen = new HashSet<PartInstance>();

            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].IsFrozen)
                {
                    frozen.Add(parts[i]);
                }
            }

            // Everything alone again.
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null)
                {
                    PartGroup.CreateFor(parts[i]);
                }
            }

            for (int i = 0; i < screws.Count; i++)
            {
                Join(screws[i], heldHoles);
            }

            foreach (PartInstance part in frozen)
            {
                part?.Group?.SetFrozen(true);
            }
        }

        /// <summary>
        /// Joins everything one screw clamps: from under its head down to the
        /// deepest thing gripping it.
        /// </summary>
        private static void Join(PlacedScrew screw, PartHoles held)
        {
            if (screw == null)
            {
                return;
            }

            screw.RecomputePasses(held);

            float grip = screw.GripDepth();

            if (grip < 0f)
            {
                // A screw resting in a hole with nothing on the end. It holds
                // nothing, which is exactly what a real one does.
                return;
            }

            PartGroup group = screw.GetComponent<PartInstance>()?.Group;

            if (group == null)
            {
                return;
            }

            IReadOnlyList<ScrewPass> passes = screw.Passes;

            for (int i = 0; i < passes.Count; i++)
            {
                // Only what is between the head and the grip. Metal further
                // down the shank than the nut is threaded onto, not clamped by
                // it - it can still slide off.
                if (passes[i].Entry > grip + 1e-4f)
                {
                    continue;
                }

                var member = passes[i].Part == null
                    ? null
                    : passes[i].Part.GetComponent<PartInstance>();

                if (member != null && member.Group != null && member.Group != group)
                {
                    group.Merge(member.Group);

                    // Merging can leave the screw in a different group than the
                    // one it started in, so re-read rather than assume.
                    group = screw.GetComponent<PartInstance>().Group;
                }
            }

        }
    }
}
