namespace VexDesigner.Parts
{
    using UnityEngine;
    using VexDesigner.InputSources;

    /// <summary>
    /// Drives world interaction: taking parts from the shelf, carrying them at
    /// arm's length, rotating, freezing, and setting them down.
    ///
    /// A part is carried at a distance out along the aim ray, not snapped to a
    /// surface. Surface snapping made it impossible to hold anything above the
    /// bench, or to assemble a robot in the air - which is exactly what you do
    /// when building one. The scroll wheel pushes the part away or draws it in.
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
        [SerializeField] private float minCarryDistance = 0.3f;
        [SerializeField] private float maxCarryDistance = 2.5f;
        [SerializeField] private float defaultCarryDistance = 0.85f;

        [Tooltip("Fraction of the current distance moved per scroll notch, so " +
                 "the same flick feels right whether the part is close or far.")]
        [SerializeField] private float carryZoomFraction = 0.13f;

        [Header("Rotation")]
        [SerializeField] private float rotationDegreesPerPixel = 0.45f;

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
        private float carryDistance;

        public bool IsCarrying => carried != null;

        /// <summary>True when a click would do something. Drives the hand cursor.</summary>
        public bool HasTarget => hovered != null;

        /// <summary>Whether the carried assembly is currently pinned.</summary>
        public bool CarriedIsFrozen =>
            carriedInstance != null && carriedInstance.IsFrozen;

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

            // A pinned part can be rotated and released without picking it up,
            // so an anchored sub-assembly can be adjusted in place.
            PartInstance frozen = FrozenUnderCursor();
            bool rotatingFrozen = frozen != null && pointer.SecondaryHeld;
            interactionLock.CameraOrbitLocked = rotatingFrozen;

            if (rotatingFrozen)
            {
                RotateGroup(frozen.Group, pointer.DragDelta);
                return;
            }

            if (frozen != null && actions != null && actions.FreezePressed)
            {
                frozen.Group.SetFrozen(false);
                return;
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

        private PartInstance FrozenUnderCursor()
        {
            var instance = RaycastFor<PartInstance>(out _);
            return instance != null && instance.IsFrozen ? instance : null;
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
                AttachToHand(go, definition, defaultCarryDistance);
            }
        }

        /// <summary>Picks up a part that is already in the world.</summary>
        public void BeginCarryExisting(GameObject existing)
        {
            if (existing == null)
            {
                return;
            }

            var instance = existing.GetComponent<PartInstance>();

            // Picking a pinned part up releases the pin: the user has clearly
            // decided to move it, and making them unfreeze first would be a
            // pointless extra step.
            instance?.Group?.SetFrozen(false);

            // Keep it at the distance it already is, so it does not jump toward
            // or away from the player the instant it is grabbed.
            float distance = Vector3.Distance(pointer.AimRay.origin, existing.transform.position);

            AttachToHand(
                existing,
                instance != null ? instance.Definition : null,
                Mathf.Clamp(distance, minCarryDistance, maxCarryDistance));
        }

        private void AttachToHand(GameObject go, PartDefinition definition, float distance)
        {
            carried = go;
            carriedDefinition = definition;
            carriedInstance = go.GetComponent<PartInstance>();
            carryDistance = distance;

            // Physics off while carried: the part follows the aim, and a live
            // Rigidbody fighting that produces jitter and stray collisions.
            carriedBody = go.GetComponent<Rigidbody>();
            if (carriedBody != null)
            {
                carriedBody.isKinematic = true;
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;
            }

            // The carried collider must not block the aim ray, or the part
            // would permanently obstruct whatever is behind it.
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

            // Claim the look gesture so right-drag turns the part instead of
            // the head. Without this both move at once and the rotation is
            // impossible to aim.
            interactionLock.CameraOrbitLocked = rotating;

            if (actions != null && actions.FreezePressed)
            {
                ToggleFreezeCarried();
                return;
            }

            if (rotating)
            {
                RotateGroup(carriedInstance?.Group, pointer.DragDelta);
            }
            else if (!CarriedIsFrozen)
            {
                AdjustCarryDistance();
                FollowAim();
            }

            if (pointer.PrimaryPressedThisFrame)
            {
                Place();
            }
        }

        private void AdjustCarryDistance()
        {
            float scroll = look?.ZoomDelta ?? 0f;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            // Proportional, so a notch feels the same whether the part is at
            // arm's length or across the room.
            carryDistance = Mathf.Clamp(
                carryDistance * (1f + (scroll * carryZoomFraction)),
                minCarryDistance,
                maxCarryDistance);
        }

        private void FollowAim()
        {
            Ray ray = pointer.AimRay;
            carried.transform.position = ray.origin + (ray.direction * carryDistance);
        }

        private void RotateGroup(PartGroup group, Vector2 drag)
        {
            if (group == null)
            {
                return;
            }

            // Yaw about world up keeps the part level as it turns; pitch about
            // the camera's right makes tilt follow the drag direction.
            Camera cam = Camera.main;
            Vector3 pitchAxis = cam != null ? cam.transform.right : Vector3.right;

            Quaternion delta =
                Quaternion.AngleAxis(drag.x * rotationDegreesPerPixel, Vector3.up) *
                Quaternion.AngleAxis(-drag.y * rotationDegreesPerPixel, pitchAxis);

            group.Rotate(delta, group.GetCentre());
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
                // Pinning hands the part back to the world but leaves it where
                // it is, so an assembly can be built in mid-air.
                Release(keepKinematic: true);
            }
        }

        private void Place()
        {
            Release(keepKinematic: CarriedIsFrozen);
        }

        private void Release(bool keepKinematic)
        {
            if (carriedCollider != null)
            {
                carriedCollider.enabled = true;
            }

            if (carriedBody != null)
            {
                carriedBody.isKinematic = keepKinematic;
            }
            else if (carriedDefinition != null)
            {
                PartFactory.AddPhysics(carried, carriedDefinition);
                var body = carried.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.isKinematic = keepKinematic;
                }
            }

            GameObject placed = carried;
            PartDefinition definition = carriedDefinition;

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
            if (!keepKinematic && pointer.RepeatModifierHeld && definition != null)
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
