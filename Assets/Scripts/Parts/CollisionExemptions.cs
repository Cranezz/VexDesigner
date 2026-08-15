namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Stops parts that are supposed to be touching from shoving each other
    /// across the room.
    ///
    /// This is the single biggest source of assemblies exploding, and the cause
    /// is the convex hull. A part's collider is the hull of its mesh, so a
    /// C-channel's collider is a solid block filling the channel - and a screw
    /// driven through it is not resting against a surface, it is buried an inch
    /// deep inside a solid object. The solver reads that as a huge penetration
    /// and does what it is supposed to do about huge penetrations, which is fire
    /// the screw out at speed.
    ///
    /// Bolted parts have the same problem in miniature: flush faces overlap by
    /// a fraction of a millimetre, and thirty of those pushing apart at once is
    /// an assembly that shakes itself to pieces.
    ///
    /// Neither overlap is an error to be resolved. A screw *is* inside the hole;
    /// two bolted plates *are* touching. So the pairs are simply excused from
    /// colliding, and the assembly holds still.
    /// </summary>
    public static class CollisionExemptions
    {
        /// <summary>
        /// Pairs currently excused. Kept so they can be put back: a part that
        /// leaves an assembly has to start colliding with its old neighbours
        /// again, or a dismantled robot would quietly pass through itself.
        /// </summary>
        private static readonly List<(Collider a, Collider b)> Excused =
            new List<(Collider, Collider)>();

        private static readonly List<Collider> Buffer = new List<Collider>();

        /// <summary>Puts every excused pair back to colliding normally.</summary>
        public static void Clear()
        {
            foreach ((Collider a, Collider b) in Excused)
            {
                if (a != null && b != null)
                {
                    Physics.IgnoreCollision(a, b, false);
                }
            }

            Excused.Clear();
        }

        /// <summary>
        /// Excuses every pair within a set of parts from colliding with each
        /// other. They still collide with everything else.
        /// </summary>
        public static void ExcuseWithin(IReadOnlyList<PartInstance> parts)
        {
            if (parts == null || parts.Count < 2)
            {
                return;
            }

            Buffer.Clear();

            foreach (PartInstance part in parts)
            {
                if (part == null)
                {
                    continue;
                }

                foreach (Collider collider in part.GetComponentsInChildren<Collider>())
                {
                    Buffer.Add(collider);
                }
            }

            for (int i = 0; i < Buffer.Count; i++)
            {
                for (int j = i + 1; j < Buffer.Count; j++)
                {
                    Excuse(Buffer[i], Buffer[j]);
                }
            }
        }

        /// <summary>
        /// Excuses one part from colliding with another - used for a screw and
        /// the metal it runs through, which overlap whether or not anything is
        /// fastened.
        /// </summary>
        public static void ExcusePair(PartInstance first, PartInstance second)
        {
            if (first == null || second == null || first == second)
            {
                return;
            }

            foreach (Collider a in first.GetComponentsInChildren<Collider>())
            {
                foreach (Collider b in second.GetComponentsInChildren<Collider>())
                {
                    Excuse(a, b);
                }
            }
        }

        private static void Excuse(Collider a, Collider b)
        {
            if (a == null || b == null || a == b)
            {
                return;
            }

            Physics.IgnoreCollision(a, b, true);
            Excused.Add((a, b));
        }
    }
}
