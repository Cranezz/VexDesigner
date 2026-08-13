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
        [SerializeField] private float carryZoomFraction = 0.05f;

        [Tooltip("Multiplier applied to rotation and distance while the " +
                 "precision modifier is held.")]
        [SerializeField] private float precisionScale = 0.22f;

        [Header("Rotation")]
        [SerializeField] private float rotationDegreesPerPixel = 0.45f;
        [SerializeField] private bool invertRotateYaw;

        [Tooltip("Pitch only. Yaw reads correctly as-is; vertical drag did not.")]
        [SerializeField] private bool invertRotatePitch = true;

        [Tooltip("Fastest the carried part will turn, in degrees per second. " +
                 "Rotation is driven through the physics solver so it collides, " +
                 "and an uncapped rate would let it spin through the bench " +
                 "between steps for the same reason fast movement used to.")]
        [SerializeField] private float maxAngularSpeed = 1200f;

        [Tooltip("How far ahead of the part its target orientation may get, in " +
                 "degrees. Caps how much unfulfilled rotation can pile up from " +
                 "a fast flick.")]
        [SerializeField] private float maxTargetLead = 25f;

        [Header("Carry physics")]
        [Tooltip("How hard the part chases the aim point, per second. Higher " +
                 "feels rigid, lower feels like carrying something heavy.")]
        [SerializeField] private float followStrength = 30f;

        [Tooltip("Speed cap, in metres per second.\n\n" +
                 "This is the tunnelling control. A part that has fallen behind " +
                 "the aim would otherwise accelerate without limit and cover " +
                 "more than its own length per physics step, punching through " +
                 "whatever it meets. Capped speed plus speculative contacts " +
                 "plus a 100 Hz step is what keeps it on the right side of the " +
                 "bench.")]
        [SerializeField] private float maxCarrySpeed = 3.5f;

        private IPointerInput pointer;
        private ILookInput look;
        private IActionInput actions;
        private InteractionLock interactionLock;

        private readonly RaycastHit[] hits = new RaycastHit[24];
        private readonly Collider[] overlapBuffer = new Collider[16];

        private IWorkshopInteractable hovered;

        private GameObject carried;
        private PartDefinition carriedDefinition;
        private Collider carriedCollider;
        private Rigidbody carriedBody;
        private PartInstance carriedInstance;

        /// <summary>Grabbed point, in the carried part's local space.</summary>
        private Vector3 grabLocalPoint;
        private float carryDistance;

        /// <summary>
        /// Orientation the part is being steered toward. Drag moves this, and
        /// the solver decides how much of it the world actually allows.
        /// </summary>
        private Quaternion targetRotation = Quaternion.identity;

        /// <summary>
        /// Player heading last frame, so the carried part can be turned with
        /// the body. Without this the part stays fixed in world space while
        /// the player turns, which feels like it is floating on a stand rather
        /// than being held.
        /// </summary>
        private float lastCarrierYaw;

        // Where the last raycast hit, so a click can grab by the exact point
        // the user aimed at rather than by the object's origin.
        private Vector3 lastHitPoint;
        private float lastHitDistance;
        private bool hasLastHit;

        public bool IsCarrying => carried != null;

        /// <summary>True when something interactable is under the crosshair.</summary>
        public bool HasTarget => hovered != null;

        /// <summary>
        /// Set by the transform tool while a gizmo handle is under the cursor,
        /// so reaching for an axis does not also pick the part up.
        /// </summary>
        public bool SuppressInput { get; set; }

        /// <summary>
        /// True when clicking would put something in the user's hand. Drives
        /// the hand cursor, so it must reflect what a click actually does -
        /// in transform mode, clicking a placed part selects rather than grabs.
        /// </summary>
        public bool HasGrabTarget
        {
            get
            {
                if (hovered == null)
                {
                    return false;
                }

                bool transformActive = TransformTool != null && TransformTool.IsActive;
                return !(transformActive && hovered is PickupHandle);
            }
        }

        /// <summary>Sibling tool, if present. Cached lazily; may be null.</summary>
        public TransformToolController TransformTool
        {
            get
            {
                if (transformTool == null)
                {
                    transformTool = GetComponent<TransformToolController>();
                }

                return transformTool;
            }
        }

        private TransformToolController transformTool;

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

            // A gizmo handle is under the cursor; the transform tool owns this
            // click. Clear any stale hover so the crosshair does not keep
            // showing a hand over a part that clicking will not pick up.
            if (SuppressInput && !IsCarrying)
            {
                if (hovered != null)
                {
                    hovered.SetHovered(false);
                    hovered = null;
                }

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

            // Pull the spawn point back toward the viewer by the part's own
            // radius. Placing it exactly at the shelf copy's surface put it
            // half inside the shelf, and depenetration promptly shoved it out
            // through the bench - which is why new screws appeared under the
            // table.
            float clearance = 0.02f;
            if (definition.mesh != null)
            {
                clearance += definition.mesh.bounds.extents.magnitude;
            }

            float distance = hasLastHit
                ? Mathf.Clamp(lastHitDistance - clearance, minCarryDistance, maxCarryDistance)
                : 0.85f;

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
            targetRotation = go.transform.rotation;
            lastCarrierYaw = transform.eulerAngles.y;

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

                // Rotate about the grabbed point rather than the part's middle.
                //
                // Physics always turns a body about its centre of mass, so
                // moving the centre of mass to where the part was grabbed makes
                // it pivot there - the same result as rotating about an
                // arbitrary pivot, but through the solver rather than around
                // it, so the rotation still collides.
                carriedBody.centerOfMass = grabLocalPoint;
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

            // Look is locked while rotating, and for the whole time a frozen
            // part is held. A pinned part does not move, so letting the view
            // swing away from it would only break the illusion of holding it -
            // and would immediately lose sight of what is being rotated.
            interactionLock.CameraOrbitLocked = rotating || CarriedIsFrozen;

            CarryWithBody();

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
        /// Turns the carried part with the player's body, so it stays oriented
        /// the same way relative to them as they turn - the way something
        /// actually held would.
        /// </summary>
        private void CarryWithBody()
        {
            float yaw = transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(lastCarrierYaw, yaw);
            lastCarrierYaw = yaw;

            if (Mathf.Abs(delta) < 0.001f)
            {
                return;
            }

            Quaternion turn = Quaternion.AngleAxis(delta, Vector3.up);
            targetRotation = turn * targetRotation;

            // A pinned part has no live body to steer, so it is turned
            // directly. Rotating it about the grab point keeps it under the
            // aim rather than swinging away.
            if (CarriedIsFrozen && carriedInstance?.Group != null)
            {
                RotateFrozenGroup(turn);
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

            float step = carryZoomFraction * PrecisionFactor;

            // Proportional, so a notch feels the same near or far.
            carryDistance = Mathf.Clamp(
                carryDistance * (1f + (scroll * step)),
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
            if (!IsCarrying || carriedBody == null || CarriedIsFrozen || pointer == null)
            {
                return;
            }

            DriveRotation();
            DrivePosition();
        }

        /// <summary>
        /// Turns the part toward its target orientation using angular velocity.
        ///
        /// Assigning the rotation directly is what let parts be twisted into
        /// the bench: a transform write skips the solver entirely, so nothing
        /// ever objects to the resulting overlap and the part only squeezes
        /// itself out afterwards. Driving the rotation as velocity means the
        /// same contact solver that stops linear movement also stops turning.
        /// </summary>
        private void DriveRotation()
        {
            Quaternion delta = targetRotation * Quaternion.Inverse(carriedBody.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);

            // ToAngleAxis returns 0..360; past 180 the short way round is the
            // other direction.
            if (angle > 180f)
            {
                angle -= 360f;
            }

            // A near-zero rotation gives a meaningless axis, sometimes with
            // infinities in it, which would poison the rigidbody.
            if (Mathf.Abs(angle) < 0.05f || float.IsNaN(axis.x) || float.IsInfinity(axis.x))
            {
                carriedBody.angularVelocity = Vector3.zero;
                return;
            }

            float degreesPerSecond = Mathf.Clamp(
                angle / Time.fixedDeltaTime, -maxAngularSpeed, maxAngularSpeed);

            carriedBody.angularVelocity =
                axis.normalized * (degreesPerSecond * Mathf.Deg2Rad);
        }

        /// <summary>
        /// Steers the grabbed point back to the aim point, every frame,
        /// including while rotating.
        ///
        /// Position used to be frozen during rotation, which meant a part that
        /// caught the bench mid-turn was pushed aside and simply stayed there,
        /// detached from the cursor. Driving position continuously lets it
        /// settle back under the aim once it is clear.
        /// </summary>
        private void DrivePosition()
        {
            Ray ray = pointer.AimRay;
            Vector3 target = ray.origin + (ray.direction * carryDistance);

            // Chase the grabbed point, not the object's origin, so the part
            // hangs off the aim where it was picked up.
            Vector3 delta = target - GrabWorldPoint;

            carriedBody.linearVelocity = Vector3.ClampMagnitude(
                delta * followStrength, maxCarrySpeed);
        }

        /// <summary>
        /// 1 normally, smaller while the precision modifier is held. Assembling
        /// needs both coarse positioning and fine nudges, and no single
        /// sensitivity serves both.
        /// </summary>
        private float PrecisionFactor =>
            (actions != null && actions.PrecisionHeld) ? precisionScale : 1f;

        private void RotateCarried(Vector2 drag)
        {
            float yawSign = invertRotateYaw ? -1f : 1f;
            float pitchSign = invertRotatePitch ? -1f : 1f;
            float rate = rotationDegreesPerPixel * PrecisionFactor;

            Camera cam = Camera.main;
            Vector3 pitchAxis = cam != null ? cam.transform.right : Vector3.right;

            Quaternion delta =
                Quaternion.AngleAxis(drag.x * rate * yawSign, Vector3.up) *
                Quaternion.AngleAxis(drag.y * rate * pitchSign, pitchAxis);

            // Only the *target* moves here. FixedUpdate drives the body toward
            // it through the solver, so the bench can refuse the rotation.
            targetRotation = delta * targetRotation;

            // Keep the target within reach of where the part actually is.
            //
            // A fast flick asks for far more rotation than the capped angular
            // speed can deliver, so the target runs away from the body. The
            // gap then takes seconds to close, and any further input fights
            // it - which is what read as stuttering rather than spinning.
            Quaternion current = carriedBody != null
                ? carriedBody.rotation
                : carried.transform.rotation;

            if (Quaternion.Angle(current, targetRotation) > maxTargetLead)
            {
                targetRotation = Quaternion.RotateTowards(
                    current, targetRotation, maxTargetLead);
            }

            // A frozen part is kinematic, so the solver will not steer it and
            // it has to be turned directly - which means checking the result
            // by hand, or it can be twisted straight into the bench.
            if (CarriedIsFrozen && carriedInstance?.Group != null)
            {
                RotateFrozenGroup(delta);
            }
        }

        /// <summary>
        /// Turns a pinned assembly, undoing the turn if it would push the part
        /// into something.
        ///
        /// A kinematic body is invisible to the contact solver, so nothing
        /// stops it passing through the bench. Applying the rotation, testing
        /// for overlap, and reverting on failure gives the same outcome the
        /// solver would - the rotation simply stops at the surface - without
        /// making the part dynamic again.
        /// </summary>
        private void RotateFrozenGroup(Quaternion delta)
        {
            PartGroup group = carriedInstance.Group;
            Vector3 pivot = GrabWorldPoint;

            group.Rotate(delta, pivot);

            if (IsGroupOverlapping(group))
            {
                group.Rotate(Quaternion.Inverse(delta), pivot);
            }
        }

        private bool IsGroupOverlapping(PartGroup group)
        {
            foreach (PartInstance part in group.Members)
            {
                var collider = part == null ? null : part.GetComponent<Collider>();
                if (collider == null || !collider.enabled)
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                int count = Physics.OverlapBoxNonAlloc(
                    bounds.center, bounds.extents, overlapBuffer,
                    Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);

                for (int i = 0; i < count; i++)
                {
                    Collider other = overlapBuffer[i];
                    if (other == collider || IsCarriedCollider(other))
                    {
                        continue;
                    }

                    // A bounds overlap is only a hint; ComputePenetration is
                    // the exact test, and at these tolerances the difference
                    // matters - parts routinely sit within a millimetre of each
                    // other without actually intersecting.
                    if (Physics.ComputePenetration(
                            collider, collider.transform.position, collider.transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out _, out float depth) && depth > 0.0004f)
                    {
                        return true;
                    }
                }
            }

            return false;
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

                // Centre of mass was moved to the grab point so the part would
                // pivot there. Left shifted, a released part would topple in a
                // way its real mass distribution never would.
                carriedBody.ResetCenterOfMass();

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
