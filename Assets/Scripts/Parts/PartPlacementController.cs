namespace VexDesigner.Parts
{
    using UnityEngine;
    using VexDesigner.InputSources;

    /// <summary>
    /// Drives taking a part from a bin, carrying it on the cursor, and setting
    /// it down.
    ///
    /// A deliberate two-state machine - idle or carrying - rather than a drag
    /// gesture. Click to pick up, click again to drop. Holding a mouse button
    /// down across a long placement is tiring and, more importantly, has no
    /// sensible VR equivalent, whereas "grab, move, release" maps directly to
    /// a controller trigger later.
    ///
    /// While carrying, every bin is switched non-interactive: no highlight, no
    /// response to a click. That is the feedback telling the user that taking a
    /// second part is not an available action right now.
    /// </summary>
    public sealed class PartPlacementController : MonoBehaviour
    {
        [Tooltip("How far the aim ray reaches, in world units.")]
        [SerializeField] private float aimDistance = 12f;

        [Tooltip("Gap left between a part and the surface when it is set down, " +
                 "so physics starts from a clean non-overlapping state.")]
        [SerializeField] private float placementClearance = 0.0015f;

        private IPointerInput pointer;
        private PartBin[] bins;

        private readonly RaycastHit[] hits = new RaycastHit[16];

        private PartBin hoveredBin;
        private GameObject carried;
        private PartDefinition carriedDefinition;
        private Collider carriedCollider;
        private bool hasValidDrop;

        public bool IsCarrying => carried != null;

        private void Awake()
        {
            pointer = GetComponentInChildren<IPointerInput>();
            if (pointer == null)
            {
                Debug.LogError(
                    $"{nameof(PartPlacementController)} found no {nameof(IPointerInput)}. " +
                    "Add a MousePointerInput component. Part placement is disabled.",
                    this);
            }

            bins = FindObjectsByType<PartBin>(FindObjectsSortMode.None);
        }

        private void Update()
        {
            if (pointer == null || pointer.IsOverInterface)
            {
                return;
            }

            if (IsCarrying)
            {
                UpdateCarrying();
            }
            else
            {
                UpdateIdle();
            }
        }

        // ------------------------------------------------------------------
        // Idle: looking for a bin to take from
        // ------------------------------------------------------------------

        private void UpdateIdle()
        {
            PartBin bin = RaycastFor<PartBin>(out _);

            if (bin != hoveredBin)
            {
                if (hoveredBin != null)
                {
                    hoveredBin.SetHovered(false);
                }

                hoveredBin = bin;

                if (hoveredBin != null)
                {
                    hoveredBin.SetHovered(true);
                }
            }

            if (hoveredBin != null && pointer.PrimaryPressedThisFrame)
            {
                TakeFrom(hoveredBin);
            }
        }

        private void TakeFrom(PartBin bin)
        {
            if (bin.Part == null)
            {
                Debug.LogWarning($"[Parts] Bin '{bin.name}' has no part assigned.", bin);
                return;
            }

            bin.SetHovered(false);
            hoveredBin = null;

            BeginCarry(bin.Part);
        }

        private void BeginCarry(PartDefinition definition)
        {
            // Physics off while carried: the part is following the cursor, and
            // a Rigidbody fighting that produces jitter and stray collisions.
            carried = PartFactory.Create(definition, withPhysics: false);
            if (carried == null)
            {
                return;
            }

            carriedDefinition = definition;

            // The carried collider must not block the aim ray, or the part
            // would be permanently in the way of the surface it is trying to
            // land on.
            carriedCollider = carried.GetComponent<Collider>();
            if (carriedCollider != null)
            {
                carriedCollider.enabled = false;
            }

            SetBinsInteractable(false);
        }

        // ------------------------------------------------------------------
        // Carrying: following the surface under the cursor
        // ------------------------------------------------------------------

        private void UpdateCarrying()
        {
            PlacementSurface surface = RaycastFor<PlacementSurface>(out RaycastHit hit);
            hasValidDrop = surface != null;

            if (hasValidDrop)
            {
                carried.transform.position = RestingPosition(hit.point);
            }

            // No placement without a surface under the cursor. This is what
            // stops parts being dropped into mid-air: aiming off the table
            // simply does nothing rather than spawning a falling part.
            if (hasValidDrop && pointer.PrimaryPressedThisFrame)
            {
                Place();
            }
        }

        /// <summary>
        /// Position that leaves the part's lowest point resting on the surface,
        /// rather than its origin - which for an imported CAD mesh is wherever
        /// the original modeller happened to put it.
        /// </summary>
        private Vector3 RestingPosition(Vector3 surfacePoint)
        {
            var filter = carried.GetComponent<MeshFilter>();
            float bottomOffset = filter != null && filter.sharedMesh != null
                ? filter.sharedMesh.bounds.min.y
                : 0f;

            return new Vector3(
                surfacePoint.x,
                surfacePoint.y - bottomOffset + placementClearance,
                surfacePoint.z);
        }

        private void Place()
        {
            if (carriedCollider != null)
            {
                carriedCollider.enabled = true;
            }

            // Physics takes over from here: the part settles onto whatever is
            // beneath it rather than hovering at the exact drop height.
            PartFactory.AddPhysics(carried, carriedDefinition);

            GameObject placed = carried;
            PartDefinition definition = carriedDefinition;

            carried = null;
            carriedCollider = null;
            carriedDefinition = null;

            placed.name = $"{definition.displayName} #{placed.GetComponent<PartInstance>()?.InstanceId}";

            // Alt held: keep going with another of the same part, so a run of
            // identical parts does not mean a return trip to the tray each time.
            if (pointer.RepeatModifierHeld)
            {
                BeginCarry(definition);
            }
            else
            {
                SetBinsInteractable(true);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Nearest object along the aim ray carrying component
        /// <typeparamref name="T"/>.
        ///
        /// Uses a component marker rather than a layer mask - see
        /// <see cref="PlacementSurface"/> for why. RaycastNonAlloc avoids
        /// allocating a hit array every frame, which at 60fps is otherwise a
        /// steady drip of garbage.
        /// </summary>
        private T RaycastFor<T>(out RaycastHit nearestHit) where T : Component
        {
            nearestHit = default;

            int count = Physics.RaycastNonAlloc(
                pointer.AimRay, hits, aimDistance, ~0, QueryTriggerInteraction.Ignore);

            T best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var candidate = hits[i].collider.GetComponentInParent<T>();
                if (candidate == null || hits[i].distance >= bestDistance)
                {
                    continue;
                }

                best = candidate;
                bestDistance = hits[i].distance;
                nearestHit = hits[i];
            }

            return best;
        }

        private void SetBinsInteractable(bool value)
        {
            for (int i = 0; i < bins.Length; i++)
            {
                if (bins[i] != null)
                {
                    bins[i].Interactable = value;
                }
            }
        }

        private void OnDisable()
        {
            // Do not strand a carried part in the scene with physics off.
            if (carried != null)
            {
                Destroy(carried);
                carried = null;
            }

            SetBinsInteractable(true);
        }
    }
}
