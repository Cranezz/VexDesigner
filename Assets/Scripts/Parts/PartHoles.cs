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
    public sealed class PartHoles : MonoBehaviour
    {
        /// <summary>
        /// How close the aim ray must pass to a hole's centre, as a fraction of
        /// its width. Slightly under a half, so two neighbouring holes cannot
        /// both be candidates and the nearer one always wins cleanly.
        /// </summary>
        [SerializeField] private float aimToleranceFraction = 0.45f;

        private PartDefinition definition;

        public HoleSet Holes => definition == null ? null : definition.holeSet;

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

            bestDistance = along;
            found = true;

            hit = new HoleHit
            {
                Part = this,
                HoleIndex = index,
                IsBackFace = isBack,
                Face = face,
                WorldPosition = worldPosition,
                WorldNormal = worldNormal,
            };

            return true;
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

        public bool IsValid => Part != null;

        public bool SameAs(HoleHit other)
        {
            return Part == other.Part &&
                   HoleIndex == other.HoleIndex &&
                   IsBackFace == other.IsBackFace;
        }
    }
}
