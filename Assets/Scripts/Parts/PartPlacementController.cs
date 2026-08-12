namespace VexDesigner.Parts
{
    using UnityEngine;
    using VexDesigner.InputSources;
    using VexDesigner.UI;

    /// <summary>
    /// Drives world interaction: taking parts, carrying them at arm's length,
    /// rotating, freezing, and setting them down.
    ///
    /// A part is held by the point on it that was clicked, and rotates about
    /// that same point. Grabbing by the origin instead makes a long part swing
    /// wildly around a pivot that may not even be inside it, since an imported
    /// CAD mesh's origin is wherever the modeller left it.
    ///
    /// Click to pick up, click again to drop: a two-state machine rather than a
    /// held-button drag. Holding a button through a long placement is tiring
    /// and has no natural VR equivalent, whereas "grab, move, release" maps
    /// straight onto a controller trigger.
    /// </summary>
    public sealed class PartPlacementController : MonoBehaviour
    {
        [Tooltip("How far the aim ray reaches, in world units.")]
        [SerializeField] private float aimDistance = 12f;

        [Header("Carry")]
        [SerializeField] private float minCarryDistance = 0.25f;
        [SerializeField] private float maxCarryDistance = 4f;

        [Tooltip("Fraction of the current distance moved per scroll notch.")]
        [SerializeField] private float carryZoomFraction = 0.35f;

        [Header("Rotation")]
        [SerializeField] private float rotationDegreesPerPixel = 0.45f;
        [SerializeField] private bool invertRotateYaw = true;
        [SerializeField] private bool invertRotatePitch = true;

        [Header("Carry physics")]
        [Tooltip("How hard the part chases the aim point, per second. Higher " +
                 "feels rigid, lower feels like carrying something heavy.")]
        [SerializeField] private float followStrength = 16f;

        [Tooltip("Speed cap, in metres per second. Without it a part that has " +
                 "fallen behind the aim accelerates hard enough to punch " +
                 "through whatever it hits.")]
        [SerializeField] private float maxCarrySpeed = 6f;

        private IPointerInput pointer;
        private ILookInput look;
        private IActionInput actions;
        private InteractionLock interactionLock;

        private readonly RaycastHit[] hits = new RaycastHit[24];

        private IWorkshopInteractable hovered;

        private GameObject carried;
        private PartDefinition carriedDefinition;
        private Collider carriedCollider;
        private Rigidbody carriedBody;
        private PartInstance carriedInstance;

        /// <summary>Grabbed point, in the carried part's local space.</summary>
        private Vector3 grabLocalPoint;
        private float carryDistance;

        // Where the last raycast hit, so a click can grab by the exact point
        // the user aimed at rather than by the object's origin.
        private Vector3 lastHitPoint;
        private float lastHitDistance;
        private bool hasLastHit;

        public bool IsCarrying => carried != null;

        /// <summary>True when a click would do something. Drives the hand cursor.</summary>
        public bool HasTarget => hovered != null;

        public bool CarriedIsFrozen => carriedInstance != null && carriedInstance.IsFrozen;

        private void Awake()
        {
            pointer = GetComponentInChildren<IPointerInput>();
            look = GetComponentInChildren<ILookInput>();
            actions = GetComponentInChildren<IActionInput>();

            if (pointer == null)
            {
                Debug.LogError(
                    $"{nameof(PartPlacementController)} found no {nameof(IPointerInput)}. " +
                    "Add a FirstPersonInput component. Interaction is disabled.",
                    this);
            }

            interactionLock = GetComponent<InteractionLock>()
                ?? gameObject.AddComponent<InteractionLock>();
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
            interactionLock.CameraOrbitLocked = false;

            var target = RaycastFor<IWorkshopInteractable>(out RaycastHit hit);
            if (target != null && !target.Interactable)
            {
                target = null;
            }

            hasLastHit = target != null;
            if (hasLastHit)
            {
                lastHitPoint = hit.point;
                lastHitDistance = hit.distance;
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
        // Taking
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a fresh part and puts it in hand, at the distance of
        /// whatever was clicked. Spawning at a fixed distance instead made a
        /// part taken from the shelf appear far away and inside the bench.
        /// </summary>
        public void BeginCarryNew(PartDefinition definition)
        {
            if (definition == null)
            {
                Debug.LogWarning("[Parts] Tried to take a part with no definition.");
                return;
            }

            GameObject go = PartFactory.Create(definition, withPhysics: false);
            if (go == null)
            {
                return;
            }

            float distance = hasLastHit
                ? Mathf.Clamp(lastHitDistance, minCarryDistance, maxCarryDistance)
                : 0.85f;

            // Place it at the aim point first, then grab it by its centre, so
            // it arrives exactly where the shelf copy was.
            Ray ray = pointer.AimRay;
            go.transform.position = ray.origin + (ray.direction * distance);

            AttachToHand(go, definition, distance, go.transform.position);
        }

        /// <summary>Picks up a part that is already in the world.</summary>
        public void BeginCarryExisting(GameObject existing)
        {
            if (existing == null)
            {
                return;
            }

            var instance = existing.GetComponent<PartInstance>();

            // A frozen part stays frozen when grabbed. Only K releases it -
            // otherwise anchoring a sub-assembly would be undone by the very
            // act of reaching for it.
            Vector3 grabPoint = hasLastHit ? lastHitPoint : existing.transform.position;
            float distance = hasLastHit
                ? Mathf.Clamp(lastHitDistance, minCarryDistance, maxCarryDistance)
                : Vector3.Distance(pointer.AimRay.origin, existing.transform.position);

            AttachToHand(
                existing,
                instance != null ? instance.Definition : null,
                distance,
                grabPoint);
        }

        private void AttachToHand(
            GameObject go, PartDefinition definition, float distance, Vector3 grabWorldPoint)
        {
            carried = go;
            carriedDefinition = definition;
            carriedInstance = go.GetComponent<PartInstance>();
            carryDistance = distance;
            grabLocalPoint = go.transform.InverseTransformPoint(grabWorldPoint);

            // A carried part stays a live physics body so it collides with the
            // bench and with other parts. Teleporting it to the aim point
            // instead would tunnel it straight through walls, since a
            // transform assignment skips collision entirely.
            //
            // Gravity is off - the part hangs where it is put - but the body
            // is otherwise fully simulated, and FixedUpdate steers it.
            carriedBody = go.GetComponent<Rigidbody>();
            if (carriedBody == null && definition != null)
            {
                PartFactory.AddPhysics(go, definition);
                carriedBody = go.GetComponent<Rigidbody>();
            }

            if (carriedBody != null)
            {
                carriedBody.isKinematic = CarriedIsFrozen;
                carriedBody.useGravity = false;
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;
            }

            // The collider stays enabled so collision works; the aim ray skips
            // the carried group instead. See RaycastFor.
            carriedCollider = go.GetComponent<Collider>();

            carriedInstance?.Group?.SetGrabbed(true);

            // Whatever this part was supporting has to be told it lost its
            // support, or a stack hangs in mid-air.
            carriedInstance?.Group?.WakeNeighbours();

            SetWorldInteractable(false);
        }

        // ------------------------------------------------------------------
        // Carrying
        // ------------------------------------------------------------------

        private void UpdateCarrying()
        {
            bool rotating = pointer.SecondaryHeld;

            // Claim the look gesture only while actually rotating, so the
            // player can still look around and walk while holding something.
            interactionLock.CameraOrbitLocked = rotating;

            if (actions != null && actions.FreezePressed)
            {
                ToggleFreezeCarried();
                return;
            }

            if (rotating)
            {
                RotateCarried(pointer.DragDelta);
            }
            else if (CarriedIsFrozen)
            {
                WarnIfTryingToMove();
            }
            else
            {
                // Movement itself happens in FixedUpdate, with physics.
                AdjustCarryDistance();
            }

            if (pointer.PrimaryPressedThisFrame)
            {
                Release();
            }
        }

        /// <summary>
        /// World-space position of the point the part is held by.
        /// </summary>
        private Vector3 GrabWorldPoint =>
            carried.transform.TransformPoint(grabLocalPoint);

        private void AdjustCarryDistance()
        {
            float scroll = look?.ZoomDelta ?? 0f;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            // Proportional, so a notch feels the same near or far.
            carryDistance = Mathf.Clamp(
                carryDistance * (1f + (scroll * carryZoomFraction)),
                minCarryDistance,
                maxCarryDistance);
        }

        /// <summary>
        /// Steers the carried body toward the aim point using velocity, run in
        /// FixedUpdate alongside the rest of physics.
        ///
        /// A proportional controller rather than exact tracking. Setting the
        /// velocity that would close the gap in a single step makes the part
        /// effectively immovable, and it shoves anything it touches across the
        /// room. Chasing at a bounded speed means the part is stopped by the
        /// bench instead of driving through it, and lags slightly behind fast
        /// movement, which reads as weight.
        /// </summary>
        private void FixedUpdate()
        {
            if (!IsCarrying || carriedBody == null || CarriedIsFrozen)
            {
                return;
            }

            if (pointer == null || pointer.SecondaryHeld)
            {
                // Hold position while rotating.
                carriedBody.linearVelocity = Vector3.zero;
                return;
            }

            Ray ray = pointer.AimRay;
            Vector3 target = ray.origin + (ray.direction * carryDistance);

            // Chase the grabbed point, not the object's origin, so the part
            // hangs off the aim where it was picked up.
            Vector3 delta = target - GrabWorldPoint;

            carriedBody.linearVelocity = Vector3.ClampMagnitude(
                delta * followStrength, maxCarrySpeed);

            // Collisions would otherwise set the part tumbling out of the
            // user's control.
            carriedBody.angularVelocity = Vector3.zero;
        }

        private void RotateCarried(Vector2 drag)
        {
            float yawSign = invertRotateYaw ? -1f : 1f;
            float pitchSign = invertRotatePitch ? -1f : 1f;

            Camera cam = Camera.main;
            Vector3 pitchAxis = cam != null ? cam.transform.right : Vector3.right;

            Quaternion delta =
                Quaternion.AngleAxis(drag.x * rotationDegreesPerPixel * yawSign, Vector3.up) *
                Quaternion.AngleAxis(drag.y * rotationDegreesPerPixel * pitchSign, pitchAxis);

            // Pivot on the grabbed point, so the part turns about where it is
            // held rather than swinging around a distant origin.
            Vector3 pivot = GrabWorldPoint;

            if (carriedInstance?.Group != null)
            {
                carriedInstance.Group.Rotate(delta, pivot);
            }
            else
            {
                Transform t = carried.transform;
                t.rotation = delta * t.rotation;
                t.position = pivot + (delta * (t.position - pivot));
            }

            // Rotation is applied to the transform directly, which a live
            // Rigidbody would otherwise fight on the next physics step.
            if (carriedBody != null && !carriedBody.isKinematic)
            {
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;
            }
        }

        private void WarnIfTryingToMove()
        {
            bool aiming = pointer.DragDelta.sqrMagnitude > 4f;
            bool scrolling = !Mathf.Approximately(look?.ZoomDelta ?? 0f, 0f);

            if (aiming || scrolling)
            {
                // Silence here would be indistinguishable from a bug. Naming
                // the key teaches the binding exactly when it is wanted.
                MessageBanner.Warn("Part is frozen — press K to unfreeze");
            }
        }

        private void ToggleFreezeCarried()
        {
            PartGroup group = carriedInstance?.Group;
            if (group == null)
            {
                return;
            }

            group.SetFrozen(!group.IsFrozen);

            if (group.IsFrozen)
            {
                MessageBanner.Info("Frozen — K to release");
            }
        }

        private void Release()
        {
            bool frozen = CarriedIsFrozen;

            if (carriedBody != null)
            {
                carriedBody.isKinematic = frozen;

                // Gravity back on, unless the part is pinned. It was only ever
                // off so the part would hang where it was put while carried.
                carriedBody.useGravity = !frozen;

                if (frozen)
                {
                    carriedBody.linearVelocity = Vector3.zero;
                    carriedBody.angularVelocity = Vector3.zero;
                }
            }
            else if (carriedDefinition != null)
            {
                PartFactory.AddPhysics(carried, carriedDefinition);
                var body = carried.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.isKinematic = frozen;
                    body.useGravity = !frozen;
                }
            }

            GameObject placed = carried;
            PartDefinition definition = carriedDefinition;
            PartGroup group = carriedInstance?.Group;

            group?.SetGrabbed(false);
            group?.WakeNeighbours();

            carried = null;
            carriedCollider = null;
            carriedBody = null;
            carriedInstance = null;
            carriedDefinition = null;

            interactionLock.CameraOrbitLocked = false;

            if (definition != null && placed != null)
            {
                var instance = placed.GetComponent<PartInstance>();
                placed.name = instance != null
                    ? $"{definition.displayName} #{instance.InstanceId}"
                    : definition.displayName;
            }

            // Alt held: keep going with another of the same part, so a run of
            // identical parts does not mean a return trip to the shelf.
            if (!frozen && pointer.RepeatModifierHeld && definition != null)
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

                // The carried part keeps its collider so it can collide with
                // the world, so the aim ray has to ignore it explicitly -
                // otherwise the thing in your hands permanently blocks the
                // view of everything behind it.
                if (IsCarriedCollider(hits[i].collider))
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

        private bool IsCarriedCollider(Collider collider)
        {
            if (carried == null || collider == null)
            {
                return false;
            }

            var instance = collider.GetComponentInParent<PartInstance>();
            if (instance == null)
            {
                return false;
            }

            // Compare by group, so an assembly held by one of its parts does
            // not have the rest of itself blocking the aim.
            return instance == carriedInstance ||
                (carriedInstance != null && instance.Group == carriedInstance.Group);
        }

        /// <summary>
        /// Turns every interactable on or off. While carrying, nothing
        /// highlights and nothing responds - that silence is how the user is
        /// told the only available action is to put the part down.
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
