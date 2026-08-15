namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// Runtime access to a part's holes: which one is being aimed at, and
    /// where it is in the world.
    ///
    /// Holes are found by measuring against the aim ray rather than by giving
    /// each one a collider. A C-channel has 174 holes and so 348 faces; that
    /// many colliders per part, on a robot of dozens of parts, is tens of
    /// thousands of collider transforms for something a handful of dot products
    /// answers exactly.
    ///
    /// Measuring against the ray also sidesteps the part's collider being a
    /// convex hull. The hull fills in the channel, so a surface hit on it is
    /// nowhere near the real metal - but the hole positions are known exactly,
    /// so the collider never needs to be consulted at all.
    /// </summary>
    /// <remarks>
    /// Runs in the editor as well as in play mode. The registry below is built
    /// from OnEnable, and Unity only calls that outside play mode for scripts
    /// marked this way - so without it the editor's own tools would ask what a
    /// screw passes through and be told, quite wrongly, that the answer is
    /// nothing at all.
    /// </remarks>
    [ExecuteAlways]
    public sealed class PartHoles : MonoBehaviour
    {
        /// <summary>
        /// How close the aim ray must pass to a hole's centre, as a fraction of
        /// its width. Slightly under a half, so two neighbouring holes cannot
        /// both be candidates and the nearer one always wins cleanly.
        /// </summary>
        [SerializeField] private float aimToleranceFraction = 0.45f;

        [Tooltip("How far short of the hole the line-of-sight test stops, in " +
                 "metres. Enough to clear the rim of the opening it is aiming " +
                 "at, far less than the thinnest VEX wall.")]
        [SerializeField] private float occlusionSkin = 0.0004f;

        private PartDefinition definition;
        private MeshRayTester tester;
        private bool testerResolved;

        /// <summary>
        /// Every part in the scene that has holes.
        ///
        /// Kept as a list rather than found on demand. Working out what a screw
        /// runs through means asking every part in the workshop, and that
        /// question is asked on every frame a nut is being offered to a screw -
        /// which is far too often to be scanning the whole scene graph for
        /// components.
        /// </summary>
        private static readonly System.Collections.Generic.List<PartHoles> Live =
            new System.Collections.Generic.List<PartHoles>();

        public static System.Collections.Generic.IReadOnlyList<PartHoles> All => Live;

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>
        /// This part's holes.
        ///
        /// Normally the part type's, shared by every copy. A part that has been
        /// cut gets its own set, because a cut takes holes away from *this*
        /// piece of metal and the others are untouched - which is the same
        /// reason cuts live on the instance rather than on the definition.
        /// </summary>
        public HoleSet Holes => cutHoles ?? (definition == null ? null : definition.holeSet);

        private HoleSet cutHoles;

        public void SetOverride(HoleSet holes) => cutHoles = holes;

        public void ClearOverride() => cutHoles = null;

        public bool HasHoles => Holes != null && !Holes.IsEmpty;

        public void Initialise(PartDefinition partDefinition)
        {
            definition = partDefinition;
        }

        private void Awake()
        {
            if (definition == null)
            {
                var instance = GetComponent<PartInstance>();
                definition = instance == null ? null : instance.Definition;
            }
        }

        /// <summary>
        /// The hole face the ray is pointing at, if any.
        ///
        /// Only faces turned toward the viewer are considered, so aiming at a
        /// surface finds the opening on that side rather than the one behind
        /// it. <paramref name="wantFarSide"/> flips to the other face of the
        /// same hole, which is what lets the far side be worked with without
        /// walking around the part.
        /// </summary>
        public bool TryAim(Ray worldRay, bool wantFarSide, out HoleHit hit)
        {
            hit = default;

            if (!HasHoles)
            {
                return false;
            }

            Hole[] holes = Holes.holes;
            float bestDistance = float.MaxValue;
            bool found = false;

            for (int i = 0; i < holes.Length; i++)
            {
                if (!Consider(holes[i], holes[i].front, worldRay, i, false,
                        ref bestDistance, ref hit, ref found))
                {
                    Consider(holes[i], holes[i].back, worldRay, i, true,
                        ref bestDistance, ref hit, ref found);
                }
            }

            if (found && wantFarSide)
            {
                hit = Flip(hit);
            }

            return found;
        }

        private bool Consider(
            Hole hole, HoleFace face, Ray ray, int index, bool isBack,
            ref float bestDistance, ref HoleHit hit, ref bool found)
        {
            Vector3 worldPosition = transform.TransformPoint(face.localPosition);
            Vector3 worldNormal = transform.TransformDirection(face.localNormal).normalized;

            // Facing away: this is the far side of the material, and picking it
            // would mean aiming at one surface and selecting the opposite one.
            if (Vector3.Dot(worldNormal, -ray.direction) <= 0.05f)
            {
                return false;
            }

            Vector3 toFace = worldPosition - ray.origin;
            float along = Vector3.Dot(toFace, ray.direction);

            if (along <= 0f)
            {
                return false;
            }

            float offset = (toFace - (ray.direction * along)).magnitude;
            float tolerance = face.width * aimToleranceFraction * transform.lossyScale.x;

            if (offset > tolerance)
            {
                return false;
            }

            // Nearest along the ray, not nearest to it. Two holes lined up
            // behind each other should resolve to the front one, however
            // precisely the ray happens to pass through the far one's centre.
            if (along >= bestDistance)
            {
                return true;
            }

            // Checked last, because it is the only expensive part of this test
            // and by here almost everything has already been ruled out.
            if (IsBehindMaterial(worldPosition, ray))
            {
                return false;
            }

            bestDistance = along;
            found = true;

            hit = new HoleHit
            {
                Part = this,
                HoleIndex = index,
                IsBackFace = isBack,
                Face = face,
                Shape = hole.shape,
                WorldPosition = worldPosition,
                WorldNormal = worldNormal,
            };

            return true;
        }

        /// <summary>
        /// True when the part's own metal stands between the viewer and this
        /// opening.
        ///
        /// Without this a hole could be picked straight through the part it is
        /// in. A C-channel is the clear case: looked at from the side, the far
        /// flange's inside faces point back at the viewer and pass every other
        /// test, so aiming at solid metal on the near flange would select a
        /// hole two inches behind it.
        ///
        /// Note this is about *other* material, not the hole's own far face:
        /// reaching the opposite side of the same hole is deliberate, and is
        /// what the far-side key does.
        /// </summary>
        private bool IsBehindMaterial(Vector3 worldPosition, Ray ray)
        {
            MeshRayTester tester = Tester();

            if (tester == null)
            {
                // No readable mesh to consult. Allowing the pick is the right
                // failure: an over-permissive aim is a nuisance, whereas
                // rejecting everything would make the part unusable.
                return false;
            }

            Vector3 localTarget = transform.InverseTransformPoint(worldPosition);
            Vector3 localOrigin = transform.InverseTransformPoint(ray.origin);

            // The skin has to clear the rim of the hole being aimed at, which
            // sits exactly on the surface the test ends at. A hair under half a
            // millimetre: wide enough to miss the rim, far short of the
            // thinnest VEX wall.
            return tester.SegmentBlocked(localOrigin, localTarget, occlusionSkin);
        }

        private MeshRayTester Tester()
        {
            if (!testerResolved)
            {
                testerResolved = true;
                tester = MeshRayTester.For(definition == null ? null : definition.mesh);
            }

            return tester;
        }

        private HoleHit Flip(HoleHit hit)
        {
            Hole hole = Holes.holes[hit.HoleIndex];
            HoleFace other = hit.IsBackFace ? hole.front : hole.back;

            return new HoleHit
            {
                Part = hit.Part,
                HoleIndex = hit.HoleIndex,
                IsBackFace = !hit.IsBackFace,
                Face = other,
                Shape = hole.shape,
                WorldPosition = transform.TransformPoint(other.localPosition),
                WorldNormal = transform.TransformDirection(other.localNormal).normalized,
            };
        }

        /// <summary>World placement of a hole face, for mating and screws.</summary>
        public HoleHit FaceAt(int holeIndex, bool backFace)
        {
            Hole hole = Holes.holes[holeIndex];
            HoleFace face = backFace ? hole.back : hole.front;

            return new HoleHit
            {
                Part = this,
                HoleIndex = holeIndex,
                IsBackFace = backFace,
                Face = face,
                Shape = hole.shape,
                WorldPosition = transform.TransformPoint(face.localPosition),
                WorldNormal = transform.TransformDirection(face.localNormal).normalized,
            };
        }
    }

    /// <summary>One hole face, resolved into world space.</summary>
    public struct HoleHit
    {
        public PartHoles Part;
        public int HoleIndex;
        public bool IsBackFace;
        public HoleFace Face;
        public Vector3 WorldPosition;
        public Vector3 WorldNormal;

        /// <summary>Rounded square or circle. Drives how the marker is drawn.</summary>
        public HoleShape Shape;

        public bool IsValid => Part != null;

        public bool SameAs(HoleHit other)
        {
            return Part == other.Part &&
                   HoleIndex == other.HoleIndex &&
                   IsBackFace == other.IsBackFace;
        }
    }
}
