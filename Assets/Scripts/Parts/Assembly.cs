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

            // Assemblies are physically welded, and the record of that lives on
            // the groups about to be discarded. Everything is taken apart
            // first and put back at the end, so the welds follow the parts
            // into whatever assembly they now belong to.
            PartGroup.UnweldAll();

            IReadOnlyList<PartInstance> parts = PartInstance.All;
            IReadOnlyList<PlacedScrew> screws = PlacedScrew.All;

            // Worked out again from scratch alongside the groups, for the same
            // reason: a part that has left an assembly must start colliding
            // with its old neighbours again.
            CollisionExemptions.Clear();

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

            // Parts bolted face to face overlap by a fraction of a millimetre,
            // and a whole robot of those pushing apart at once is an assembly
            // that shakes itself to pieces.
            var seen = new HashSet<PartGroup>();

            for (int i = 0; i < parts.Count; i++)
            {
                PartGroup group = parts[i] == null ? null : parts[i].Group;

                if (group == null || group.Members.Count < 2 || !seen.Add(group))
                {
                    continue;
                }

                // Bolted parts overlap by a fraction of a millimetre, and a
                // whole robot of those pushing apart at once shakes itself to
                // pieces. Excused before welding, since the pairs are the same
                // either way and a welded body will not consult them.
                CollisionExemptions.ExcuseWithin(group.Members);

                // And then the assembly becomes one object, permanently - not
                // only while someone is holding it. An assembly that is one
                // body when carried and several when let go falls apart the
                // moment it is unfrozen.
                group.Weld();
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

            // A screw is *inside* the metal it passes through, and a C-channel's
            // collider is the convex hull of its mesh - a solid block filling
            // the channel. So a driven screw is buried an inch deep in a solid
            // object, which the solver reads as a huge penetration and fires the
            // screw out at speed. Excused whether or not anything is fastened,
            // because the overlap is there either way.
            var self = screw.GetComponent<PartInstance>();

            foreach (ScrewPass pass in screw.Passes)
            {
                if (pass.Part != null)
                {
                    CollisionExemptions.ExcusePair(self, pass.Part.GetComponent<PartInstance>());
                }
            }

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
