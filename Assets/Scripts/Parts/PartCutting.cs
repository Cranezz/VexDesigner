namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Applies a part's list of cuts to its pristine mesh.
    ///
    /// Always from the original, never from the last result. Re-slicing the
    /// already-sliced mesh would work once and then accumulate: every cut would
    /// inherit the previous cut's rounding, undo would be impossible because
    /// the removed geometry is gone, and a save file would reload as something
    /// subtly different from what was saved. Replaying the whole list from the
    /// imported mesh means the same cuts always give the same part, on any
    /// machine, in any session.
    ///
    /// That is also what makes the save format work: a cut robot is a part
    /// number and a handful of planes, never a mesh.
    /// </summary>
    public static class PartCutting
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>
        /// Rebuilds a part's geometry, collider and holes from its definition
        /// and its cut list.
        /// </summary>
        public static void Apply(PartInstance part)
        {
            if (part == null || part.Definition == null)
            {
                return;
            }

            PartDefinition definition = part.Definition;
            var modifications = part.GetComponent<Modifications>();

            Mesh mesh = definition.mesh;
            var outlines = new List<List<Vector3>>();

            if (modifications != null && modifications.HasCuts)
            {
                foreach (CutOperation cut in modifications.Cuts)
                {
                    var plane = new Plane(cut.localNormal.normalized, cut.localOffset);
                    MeshSlicer.Result result = MeshSlicer.Slice(mesh, plane);

                    if (result.empty)
                    {
                        Debug.LogWarning(
                            $"[Cutting] A cut on {definition.displayName} would " +
                            "remove the whole part. Ignored.");

                        continue;
                    }

                    mesh = result.mesh;
                    outlines = result.outlines;
                }
            }

            var filter = part.GetComponent<MeshFilter>();

            if (filter != null)
            {
                filter.sharedMesh = mesh;
            }

            var collider = part.GetComponent<MeshCollider>();

            if (collider != null)
            {
                collider.sharedMesh = mesh;
            }

            ApplyHoles(part, definition, modifications);

            // The outline shader extrudes along smoothed normals, which a
            // freshly cut mesh does not have.
            OutlineNormals.Bake(mesh);
        }

        /// <summary>
        /// Removes the holes a cut destroyed, and keeps the rest.
        ///
        /// A hole survives only if it is wholly on the kept side. One the blade
        /// passed through is not a hole any more - it is a notch in the edge -
        /// and leaving it in the list would let a screw snap to a place where
        /// there is no longer any metal to grip.
        /// </summary>
        private static void ApplyHoles(
            PartInstance part, PartDefinition definition, Modifications modifications)
        {
            var holes = part.GetComponent<PartHoles>();

            if (holes == null || definition.holeSet == null || definition.holeSet.IsEmpty)
            {
                return;
            }

            if (modifications == null || !modifications.HasCuts)
            {
                holes.ClearOverride();
                return;
            }

            var kept = new List<Hole>();

            foreach (Hole hole in definition.holeSet.holes)
            {
                bool survives = true;

                foreach (CutOperation cut in modifications.Cuts)
                {
                    var plane = new Plane(cut.localNormal.normalized, cut.localOffset);

                    // Both openings, and the rim around them. A hole whose edge
                    // the blade merely grazed is no longer round and no longer
                    // holds a screw.
                    float radius = hole.front.width * 0.5f;

                    if (plane.GetDistanceToPoint(hole.front.localPosition) < radius ||
                        plane.GetDistanceToPoint(hole.back.localPosition) < radius)
                    {
                        survives = false;
                        break;
                    }
                }

                if (survives)
                {
                    kept.Add(hole);
                }
            }

            holes.SetOverride(new HoleSet
            {
                holes = kept.ToArray(),
                measuredPitchInches = definition.holeSet.measuredPitchInches,
                generatedAt = definition.holeSet.generatedAt,
            });
        }

        /// <summary>
        /// Records a cut and applies it.
        ///
        /// The plane is given in the part's own local space, so it travels with
        /// the part and means the same thing wherever the part is put.
        /// </summary>
        public static bool Cut(
            PartInstance part, Plane localPlane,
            float distanceInches, float bladeAngleDegrees)
        {
            if (part == null)
            {
                return false;
            }

            var modifications = part.GetComponent<Modifications>()
                ?? part.gameObject.AddComponent<Modifications>();

            modifications.Add(new CutOperation
            {
                localNormal = localPlane.normal,
                localOffset = localPlane.distance,
                distanceInches = distanceInches,
                bladeAngleDegrees = bladeAngleDegrees,
                keepPositiveSide = true,
                measuredTo = CutReference.LongSide,
            });

            Apply(part);
            return true;
        }

        /// <summary>Takes the last cut back off, restoring what it removed.</summary>
        public static bool Undo(PartInstance part)
        {
            var modifications = part == null ? null : part.GetComponent<Modifications>();

            if (modifications == null || !modifications.RemoveLast())
            {
                return false;
            }

            Apply(part);
            return true;
        }
    }
}
