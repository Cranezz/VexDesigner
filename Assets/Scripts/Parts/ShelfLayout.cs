namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Where one part sits once the shelf has been arranged.
    /// </summary>
    public struct ShelfPlacement
    {
        public PartDefinition Definition;
        public Quaternion Rotation;

        /// <summary>Centre of the part's footprint, relative to the shelf's
        /// near-left corner. Y is the height needed to rest on the surface.</summary>
        public Vector3 LocalPosition;

        public int Page;
    }

    /// <summary>
    /// Arranges parts into pages on a rectangular patch of table.
    ///
    /// Plain C# with no MonoBehaviour and no scene access, so the packing can
    /// be unit tested - which matters, because "how many pages do I get" is
    /// exactly the kind of logic that breaks silently when a part of an
    /// unusual shape turns up.
    ///
    /// The algorithm is shelf packing: parts are laid end to end along the
    /// region's long axis, wrapping into a new column when the run is full,
    /// and onto a new page when the columns are full. Sorting by width first
    /// keeps similar parts together and stops one wide part from wasting a
    /// whole column.
    ///
    /// The practical consequence is what was asked for: a page holds many
    /// small parts or few large ones, decided by the parts themselves rather
    /// than by a fixed count.
    /// </summary>
    public static class ShelfLayout
    {
        public static List<ShelfPlacement> Arrange(
            IReadOnlyList<PartDefinition> parts,
            float runLength,
            float runWidth,
            float padding,
            out int pageCount)
        {
            var placements = new List<ShelfPlacement>();
            pageCount = 0;

            if (parts == null || parts.Count == 0)
            {
                return placements;
            }

            // Measure everything first so it can be sorted before placing.
            var measured = new List<(PartDefinition def, Quaternion rot, Vector3 size)>();
            foreach (PartDefinition def in parts)
            {
                if (def == null || def.mesh == null)
                {
                    continue;
                }

                Quaternion rot = LieFlatRotation(def.mesh.bounds.size, out Vector3 size);
                measured.Add((def, rot, size));
            }

            // Widest first. Shelf packing wastes the least space when the
            // items that constrain a column are placed while the column is
            // still empty.
            measured.Sort((a, b) => b.size.x.CompareTo(a.size.x));

            float usableLength = runLength - (padding * 2f);
            float usableWidth = runWidth - (padding * 2f);

            float z = padding;
            float x = padding;
            float columnWidth = 0f;
            int page = 0;

            foreach (var (def, rot, size) in measured)
            {
                // A part larger than the whole region can never be placed;
                // laying it out anyway would silently overlap its neighbours.
                if (size.x > usableWidth || size.z > usableLength)
                {
                    Debug.LogWarning(
                        $"[Shelf] '{def.displayName}' is {size.z / 0.0254f:F1} x " +
                        $"{size.x / 0.0254f:F1} in and does not fit the shelf region. Skipped.");
                    continue;
                }

                // Run out of length: start a new column.
                if (z + size.z > usableLength + padding)
                {
                    z = padding;
                    x += columnWidth + padding;
                    columnWidth = 0f;
                }

                // Run out of columns: start a new page.
                if (x + size.x > usableWidth + padding)
                {
                    page++;
                    x = padding;
                    z = padding;
                    columnWidth = 0f;
                }

                placements.Add(new ShelfPlacement
                {
                    Definition = def,
                    Rotation = rot,
                    LocalPosition = new Vector3(
                        x + (size.x * 0.5f),
                        size.y * 0.5f,
                        z + (size.z * 0.5f)),
                    Page = page,
                });

                z += size.z + padding;
                columnWidth = Mathf.Max(columnWidth, size.x);
            }

            pageCount = page + 1;
            return placements;
        }

        /// <summary>
        /// Rotation that lays a part down the way a person would: longest
        /// dimension along the shelf run, shortest dimension vertical.
        ///
        /// Without this, parts appear in whatever orientation the original CAD
        /// modeller happened to use, so a C-channel might stand on end and a
        /// screw might point at the ceiling. Orienting by bounding box is
        /// crude but it is right for the overwhelming majority of VEX parts,
        /// which are extrusions and fasteners.
        /// </summary>
        public static Quaternion LieFlatRotation(Vector3 size, out Vector3 rotatedSize)
        {
            // Rank the local axes by extent.
            int longest = 0, shortest = 0;
            for (int i = 1; i < 3; i++)
            {
                if (size[i] > size[longest]) { longest = i; }
                if (size[i] < size[shortest]) { shortest = i; }
            }

            int middle = 3 - longest - shortest;

            // Degenerate case: a cube-ish part where two axes tie. Any
            // consistent assignment works; this just avoids middle == longest.
            if (longest == shortest)
            {
                longest = 0;
                middle = 1;
                shortest = 2;
            }

            // Target: longest -> Z (along the run), middle -> X, shortest -> Y.
            Vector3 imageOfLocalY = WorldAxisFor(1, longest, middle, shortest);
            Vector3 imageOfLocalZ = WorldAxisFor(2, longest, middle, shortest);

            rotatedSize = new Vector3(size[middle], size[shortest], size[longest]);

            // LookRotation maps local Z onto the first argument and local Y
            // onto the second, which is exactly the mapping just built.
            return Quaternion.LookRotation(imageOfLocalZ, imageOfLocalY);
        }

        private static Vector3 WorldAxisFor(int localAxis, int longest, int middle, int shortest)
        {
            if (localAxis == longest) { return Vector3.forward; }
            if (localAxis == middle) { return Vector3.right; }
            if (localAxis == shortest) { return Vector3.up; }
            return Vector3.up;
        }
    }
}
