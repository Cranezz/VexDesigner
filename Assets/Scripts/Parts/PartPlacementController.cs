namespace VexDesigner.Parts
{
    using UnityEngine;
    using VexDesigner.InputSources;

    /// <summary>
    /// Drives every world interaction: taking parts from the shelf, carrying
    /// them, rotating them, setting them down, and picking placed ones back up.
    ///
    /// A deliberate two-state machine - idle or carrying - rather than a drag
    /// gesture. Click to pick up, click again to drop. Holding a button down
    /// through a long placement is tiring, and more importantly has no natural
    /// VR equivalent, whereas "grab, move, release" maps straight onto a
    /// controller trigger later.
    /// </summary>
    public sealed class PartPlacementController : MonoBehaviour
    {
        [Tooltip("How far the aim ray reaches, in world units.")]
        [SerializeField] private float aimDistance = 12f;

        [Tooltip("Gap left between a part and the surface when set down, so " +
                 "physics starts from a clean non-overlapping state.")]
        [SerializeField] private float placementClearance = 0.0015f;

        [Tooltip("Degrees of rotation per pixel of right-drag.")]
        [SerializeField] private float rotationDegreesPerPixel = 0.5f;

        private IPointerInput pointer;
        private InteractionLock interactionLock;

        private readonly RaycastHit[] hits = new RaycastHit[24];

        private IWorkshopInteractable hovered;

        private GameObject carried;
        private PartDefinition carriedDefinition;
        private Collider carriedCollider;
        private Renderer carriedRenderer;
        private Rigidbody carriedBody;

        public bool IsCarrying => carried != null;

        /// <summary>
        /// True when something interactable is under the crosshair. Drives the
        /// hand cursor, so it has to reflect what a click would actually do.
        /// </summary>
        public bool HasTarget => hovered != null;

        private void Awake()
        {
            pointer = GetComponentInChildren<IPointerInput>();
            if (pointer == null)
            {
                Debug.LogError(
                    $"{nameof(PartPlacementController)} found no {nameof(IPointerInput)}. " +
                    "Add a MousePointerInput component. Interaction is disabled.",
                    this);
            }

            interactionLock = GetComponent<InteractionLock>();
            if (interactionLock == null)
            {
                interactionLock = gameObject.AddComponent<InteractionLock>();
            }
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
        // Idle
        // ------------------------------------------------------------------

        private void UpdateIdle()
        {
            var target = RaycastFor<IWorkshopInteractable>(out _);

            if (target != null && !target.Interactable)
            {
                target = null;
            }

            if (!ReferenceEquals(target, hovered))
            {
                hovered?.SetHovered(false);
                hovered = target;
                hovered?.SetHovered(true);
            }

            if (hovered != null && pointer.PrimaryPressedThisFrame)
            {
                // The click may destroy the hovered object - paging the shelf
                // does exactly that - so clear the reference first.
                IWorkshopInteractable clicked = hovered;
                hovered.SetHovered(false);
                hovered = null;
                clicked.OnPrimaryClick(this);
            }
        }

        // ------------------------------------------------------------------
        // Taking and carrying
        // ------------------------------------------------------------------

        /// <summary>Creates a fresh part and puts it in hand.</summary>
        public void BeginCarryNew(PartDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogWarning("[Parts] Tried to take a part with no definition.");
                return;
            }

            GameObject go = PartFactory.Create(definition, withPhysics: false);
            if (go != null)
            {
                AttachToHand(go, definition);
            }
        }

        /// <summary>Picks up a part that is already on the table.</summary>
        public void BeginCarryExisting(GameObject existing)
        {
            if (existing == null)
            {
                return;
            }

            var instance = existing.GetComponent<PartInstance>();
            AttachToHand(existing, instance != null ? instance.Definition : null);
        }

        private void AttachToHand(GameObject go, PartDefinition definition)
        {
            carried = go;
            carriedDefinition = definition;
            carriedRenderer = go.GetComponentInChildren<Renderer>();

            // Physics off while carried: the part follows the cursor, and a
            // live Rigidbody fighting that produces jitter and stray
            // collisions with whatever it passes over.
            carriedBody = go.GetComponent<Rigidbody>();
            if (carriedBody != null)
            {
                carriedBody.isKinematic = true;
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;
            }

            // The carried collider must not block the aim ray, or the part
            // would permanently obstruct the surface it is trying to land on.
            carriedCollider = go.GetComponent<Collider>();
            if (carriedCollider != null)
            {
                carriedCollider.enabled = false;
            }

            SetWorldInteractable(false);
        }

        private void UpdateCarrying()
        {
            bool rotating = pointer.SecondaryHeld;

            // Claim the orbit gesture so right-drag turns the part instead of
            // the camera. Released the moment the button is.
            interactionLock.CameraOrbitLocked = rotating;

            if (rotating)
            {
                RotateCarried(pointer.DragDelta);
            }
            else
            {
                FollowSurface();
            }

            if (pointer.PrimaryPressedThisFrame && HasValidDrop(out _))
            {
                Place();
            }
        }

        private void RotateCarried(Vector2 drag)
        {
            // Yaw about world up so the part stays level as it turns; pitch
            // about the camera's right so tilting matches the drag direction.
            float yaw = drag.x * rotationDegreesPerPixel;
            float pitch = -drag.y * rotationDegreesPerPixel;

            carried.transform.Rotate(Vector3.up, yaw, Space.World);

            Camera cam = Camera.main;
            Vector3 pitchAxis = cam != null ? cam.transform.right : Vector3.right;
            carried.transform.Rotate(pitchAxis, pitch, Space.World);
        }

        private void FollowSurface()
        {
            if (!HasValidDrop(out RaycastHit hit))
            {
                return;
            }

            Transform t = carried.transform;
            t.position = new Vector3(hit.point.x, t.position.y, hit.point.z);

            // Rest the part's lowest *rendered* point on the surface. Mesh
            // bounds alone would be wrong once the part has been rotated, and
            // a CAD mesh's origin is wherever the modeller left it anyway.
            if (carriedRenderer != null)
            {
                float lift = hit.point.y - carriedRenderer.bounds.min.y + placementClearance;
                t.position += new Vector3(0f, lift, 0f);
            }
        }

        private bool HasValidDrop(out RaycastHit hit)
        {
            return RaycastFor<PlacementSurface>(out hit) != null;
        }

        private void Place()
        {
            if (carriedCollider != null)
            {
                carriedCollider.enabled = true;
            }

            // Physics takes over: the part settles onto whatever is beneath it
            // rather than hovering at exactly the drop height.
            if (carriedBody != null)
            {
                carriedBody.isKinematic = false;
            }
            else if (carriedDefinition != null)
            {
                PartFactory.AddPhysics(carried, carriedDefinition);
            }

            GameObject placed = carried;
            PartDefinition definition = carriedDefinition;

            carried = null;
            carriedCollider = null;
            carriedRenderer = null;
            carriedBody = null;
            carriedDefinition = null;

            interactionLock.CameraOrbitLocked = false;

            if (definition != null)
            {
                var instance = placed.GetComponent<PartInstance>();
                placed.name = instance != null
                    ? $"{definition.displayName} #{instance.InstanceId}"
                    : definition.displayName;
            }

            // Alt held: keep going with another of the same part, so a run of
            // identical parts does not mean a return trip to the shelf.
            if (pointer.RepeatModifierHeld && definition != null)
            {
                BeginCarryNew(definition);
            }
            else
            {
                SetWorldInteractable(true);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Nearest object along the aim ray carrying <typeparamref name="T"/>.
        ///
        /// RaycastNonAlloc avoids allocating a hit array every frame, which at
        /// 60fps is otherwise a steady drip of garbage.
        /// </summary>
        private T RaycastFor<T>(out RaycastHit nearestHit) where T : class
        {
            nearestHit = default;

            int count = Physics.RaycastNonAlloc(
                pointer.AimRay, hits, aimDistance, ~0, QueryTriggerInteraction.Ignore);

            T best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance >= bestDistance)
                {
                    continue;
                }

                var candidate = hits[i].collider.GetComponentInParent<T>();
                if (candidate == null)
                {
                    continue;
                }

                best = candidate;
                bestDistance = hits[i].distance;
                nearestHit = hits[i];
            }

            return best;
        }

        /// <summary>
        /// Turns every interactable in the scene on or off. While carrying,
        /// nothing highlights and nothing responds - that silence is how the
        /// user is told the only available action is to put the part down.
        /// </summary>
        private void SetWorldInteractable(bool value)
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IWorkshopInteractable interactable)
                {
                    interactable.Interactable = value;
                }
            }
        }

        private void OnDisable()
        {
            if (interactionLock != null)
            {
                interactionLock.CameraOrbitLocked = false;
            }
        }
    }
}
