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

        private IWorkshopInteractable hovered;

        // --- Hole aiming ----------------------------------------------------

        private HoleHighlighter aimMarker;
        private HoleHighlighter snapMarker;
        private HoleHit aimedHole;
        private Highlightable dimmed;

        [Header("Holes")]
        [Tooltip("Colour of the hole being aimed at, on the side facing you.")]
        [SerializeField] private Color nearHoleColour = new Color(0.25f, 0.85f, 1f);

        [Tooltip("Colour used when the far side of the hole is targeted, so it " +
                 "is obvious the selection is through the material.")]
        [SerializeField] private Color farHoleColour = new Color(1f, 0.65f, 0.15f);

        [Tooltip("How far the part's own glow drops while one of its holes is " +
                 "targeted. Low enough that the hole clearly wins.")]
        [SerializeField, Range(0f, 1f)] private float partDimWhileAiming = 0.18f;

        [Tooltip("Colour of the hole a carried part is about to mate to. " +
                 "Distinct from both aim colours, since it marks a destination " +
                 "rather than a selection.")]
        [SerializeField] private Color snapColour = new Color(0.35f, 1f, 0.45f);

        [Tooltip("Degrees the roll snaps to while the snap modifier is held " +
                 "during hole rotation. Measured from the square-on position, " +
                 "so the increments are relative to the part being joined to.")]
        [SerializeField] private float holeRollSnapDegrees = 15f;

        [Tooltip("Increment the automatic roll is rounded to when a hole first " +
                 "lands on another. A quarter turn, because parts bolted " +
                 "together are square to each other far more often than not, " +
                 "and anything else is reached with the rotation dial.")]
        [SerializeField] private float squareOnSnapDegrees = 90f;

        /// <summary>The hole currently under the crosshair, if any.</summary>
        public HoleHit AimedHole => aimedHole;

        public bool HasHoleTarget => aimedHole.IsValid;

        // --- Carrying a part by one of its holes ----------------------------

        /// <summary>
        /// The hole the carried part is held by. Its <c>Face</c> is in the
        /// part's local space and so stays valid however the part is moved.
        /// </summary>
        private HoleHit carriedHole;

        private PartHoles carriedHoles;

        /// <summary>The hole under the crosshair that the carried one will meet.</summary>
        private HoleHit snapTarget;

        private bool carryingByHole;
        private bool rotatingAboutHole;

        /// <summary>Manual roll about the join, in degrees, on top of the snap.</summary>
        private float holeRoll;

        /// <summary>Roll before the dial went up, so a cancel can restore it.</summary>
        private float rollBeforeRotating;

        private Vector3 ringZeroDirection;
        private PartGhost ghost;
        private HoleRotationRing rotationRing;

        /// <summary>Pose at the moment of grabbing, so a cancel can undo it.</summary>
        private Vector3 holeCarryStartPosition;
        private Quaternion holeCarryStartRotation;

        private readonly System.Collections.Generic.List<Collider> suspendedColliders =
            new System.Collections.Generic.List<Collider>();

        /// <summary>True while a part is being positioned by one of its holes.</summary>
        public bool IsCarryingByHole => carryingByHole;

        /// <summary>True when the carried hole is lined up on a destination.</summary>
        public bool HoleIsSnapped => carryingByHole && snapTarget.IsValid;

        /// <summary>True while the rotation dial is up.</summary>
        public bool IsRotatingAboutHole => rotatingAboutHole;

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

                ClearHoleAim();
                return;
            }

            if (carryingByHole)
            {
                ClearHoleAim();
                UpdateHoleCarry();
            }
            else if (IsCarrying)
            {
                ClearHoleAim();
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

            UpdateHoleAim();

            // A hole under the crosshair takes the click. Grabbing the part by
            // that hole is what the user was pointing at; picking it up by the
            // surface is what they get anywhere else on it.
            if (HasHoleTarget && pointer.PrimaryPressedThisFrame)
            {
                BeginCarryByHole(aimedHole);
                return;
            }

            if (hovered != null && pointer.PrimaryPressedThisFrame)
            {
                // The click may destroy the hovered object - paging the shelf
                // does exactly that - so clear the reference first.
                IWorkshopInteractable clicked = hovered;
                hovered.SetHovered(false);
                hovered = null;
                ClearHoleAim();
                clicked.OnPrimaryClick(this);
            }
        }

        /// <summary>
        /// Works out which hole, if any, the crosshair is on, and marks it.
        ///
        /// The hole lights up fully and its part drops to a faint wash. Both at
        /// full brightness would leave it ambiguous whether the click is about
        /// to act on the hole or on the part.
        /// </summary>
        private void UpdateHoleAim()
        {
            PartHoles holes = null;

            if (hovered is PickupHandle handle)
            {
                holes = handle.GetComponent<PartHoles>();
            }

            bool farSide = actions != null && actions.FarSideHeld;

            if (holes == null || !holes.HasHoles ||
                !holes.TryAim(pointer.AimRay, farSide, out HoleHit hit))
            {
                ClearHoleAim();
                return;
            }

            aimedHole = hit;

            aimMarker ??= HoleHighlighter.Create("AimedHole", nearHoleColour);

            // Coloured by what the user asked for, not by which of the hole's
            // two faces happens to be stored first. Holding the far-side key
            // recolours the marker, which is the only cue that the selection is
            // now through the material rather than on this side of it.
            aimMarker.SetColour(farSide ? farHoleColour : nearHoleColour);
            aimMarker.Show(hit);

            var highlight = holes.GetComponent<Highlightable>();
            if (!ReferenceEquals(highlight, dimmed))
            {
                RestoreDimmed();
                dimmed = highlight;
            }

            if (dimmed != null)
            {
                dimmed.HoverScale = partDimWhileAiming;
            }
        }

        private void ClearHoleAim()
        {
            aimedHole = default;
            aimMarker?.Hide();
            RestoreDimmed();
        }

        private void RestoreDimmed()
        {
            if (dimmed != null)
            {
                dimmed.HoverScale = 1f;
                dimmed = null;
            }
        }

        // ------------------------------------------------------------------
        // Carrying a part by one of its holes
        // ------------------------------------------------------------------

        /// <summary>
        /// Picks a part up by the hole under the crosshair.
        ///
        /// The part turns to glass and hangs off that hole, which is the whole
        /// idea: from here on the hole is the handle, and every other hole in
        /// the workshop is somewhere it could go. Being able to see through the
        /// part matters because the join being judged is on the far side of it.
        /// </summary>
        private void BeginCarryByHole(HoleHit hit)
        {
            GameObject go = hit.Part.gameObject;
            var instance = go.GetComponent<PartInstance>();

            float distance = Mathf.Clamp(
                Vector3.Distance(pointer.AimRay.origin, hit.WorldPosition),
                minCarryDistance,
                maxCarryDistance);

            holeCarryStartPosition = go.transform.position;
            holeCarryStartRotation = go.transform.rotation;

            AttachToHand(
                go,
                instance != null ? instance.Definition : null,
                distance,
                hit.WorldPosition);

            carryingByHole = true;
            carriedHoles = hit.Part;
            carriedHole = hit;
            snapTarget = default;
            rotatingAboutHole = false;
            holeRoll = 0f;

            // Driven straight through the transform rather than through the
            // solver. A ghost is a proposal, not an object: it has to be able
            // to pass through the very part it is being lined up against, and
            // it has to land exactly on the hole rather than wherever a chase
            // controller got to this frame.
            if (carriedBody != null)
            {
                carriedBody.isKinematic = true;
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;
            }

            SuspendColliders(go);

            ghost = go.GetComponent<PartGhost>() ?? go.AddComponent<PartGhost>();
            ghost.SetGhosted(true);
        }

        private void UpdateHoleCarry()
        {
            // The view is pinned only while the dial is up, where mouse
            // movement means rotation. The rest of the time the part is being
            // aimed, and aiming is done by looking.
            interactionLock.CameraOrbitLocked = rotatingAboutHole;

            TurnFreeOrientationWithBody();

            // Freezing has to stay reachable here. Holding a part by a hole is
            // not a different kind of holding, and a pinned part the user has
            // picked up must still be releasable without first putting it down
            // somewhere to get at the key.
            if (actions != null && actions.FreezePressed)
            {
                ToggleFreezeCarried();
            }

            if (actions != null && actions.RotateModifierPressed)
            {
                ToggleHoleRotation();
            }

            if (rotatingAboutHole)
            {
                UpdateHoleRoll();
            }
            else
            {
                AdjustCarryDistance();
                UpdateSnapTarget();
            }

            ApplyHolePose();

            if (pointer.SecondaryPressedThisFrame)
            {
                // Right-click undoes one step, not everything. With the dial up
                // that step is the rotation; otherwise it is the grab itself,
                // which without this could only be escaped by putting the part
                // down somewhere and losing where it came from.
                if (rotatingAboutHole)
                {
                    holeRoll = rollBeforeRotating;
                    StopHoleRotation();
                }
                else
                {
                    EndHoleCarry(commit: false);
                }

                return;
            }

            if (pointer.PrimaryPressedThisFrame)
            {
                EndHoleCarry(commit: true);
            }
        }

        /// <summary>
        /// Turns the free orientation with the player, so a part held loose
        /// stays the same way round relative to them as they turn.
        ///
        /// Deliberately skipped while snapped or rotating. Once the part is on
        /// a join, its orientation belongs to that join; dragging it round with
        /// the body would pull it off the thing it was just aligned to.
        /// </summary>
        private void TurnFreeOrientationWithBody()
        {
            float yaw = transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(lastCarrierYaw, yaw);
            lastCarrierYaw = yaw;

            if (snapTarget.IsValid || rotatingAboutHole || Mathf.Abs(delta) < 0.001f)
            {
                return;
            }

            targetRotation = Quaternion.AngleAxis(delta, Vector3.up) * targetRotation;
        }

        /// <summary>
        /// Looks for a hole on another part to line up with.
        ///
        /// The carried part's own holes are excluded - a part cannot be mated
        /// to itself, and its holes are the ones nearest the ray by a long way.
        /// </summary>
        private void UpdateSnapTarget()
        {
            bool wasSnapped = snapTarget.IsValid;

            var holes = RaycastFor<PartHoles>(out _);
            bool farSide = actions != null && actions.FarSideHeld;

            if (holes == null || holes == carriedHoles || !holes.HasHoles ||
                !holes.TryAim(pointer.AimRay, farSide, out HoleHit hit))
            {
                if (wasSnapped)
                {
                    // Coming off a join, keep the orientation the join gave
                    // rather than springing back to how the part was held
                    // before. Snapping is usually most of the way to right, and
                    // throwing that away on the smallest wobble of the cursor
                    // would make the part impossible to hold steady.
                    targetRotation = carried.transform.rotation;
                    holeRoll = 0f;
                    StopHoleRotation();
                }

                snapTarget = default;
                snapMarker?.Hide();
                return;
            }

            snapTarget = hit;

            snapMarker ??= HoleHighlighter.Create("SnapTargetHole", snapColour);
            snapMarker.SetColour(snapColour);
            snapMarker.Show(hit);
        }

        /// <summary>
        /// Puts the part where the current state says it goes: on the join if
        /// there is one, hanging off the crosshair if not.
        /// </summary>
        private void ApplyHolePose()
        {
            Transform moving = carried.transform;

            if (snapTarget.IsValid)
            {
                // Re-resolve the destination every frame. The part it belongs
                // to can be moved by something else - another player, later -
                // and a pose computed against a stale hole position would leave
                // the ghost floating beside the join rather than on it.
                snapTarget = snapTarget.Part.FaceAt(snapTarget.HoleIndex, snapTarget.IsBackFace);
                snapMarker?.Show(snapTarget);

                if (HoleMating.ComputePose(
                        carriedHole.Face, targetRotation, snapTarget,
                        squareOnSnapDegrees, holeRoll, moving.lossyScale,
                        out Vector3 position, out Quaternion rotation,
                        out Vector3 zeroDirection))
                {
                    moving.SetPositionAndRotation(position, rotation);
                    ringZeroDirection = zeroDirection;
                }
            }
            else
            {
                Ray ray = pointer.AimRay;
                Vector3 aimPoint = ray.origin + (ray.direction * carryDistance);

                // Rotation first, then slide the grabbed hole onto the aim
                // point - turning the part moves its holes, so the offset has
                // to be measured after the turn.
                moving.rotation = targetRotation;
                moving.position += aimPoint - moving.TransformPoint(carriedHole.Face.localPosition);
            }

            // A kinematic body still has its own idea of where it is, and an
            // interpolated one rebuilds the rendered pose from it. Told
            // directly, or the ghost lags a frame behind the cursor.
            if (carriedBody != null)
            {
                carriedBody.position = moving.position;
                carriedBody.rotation = moving.rotation;
            }

            UpdateRotationRing();
        }

        /// <summary>
        /// Raises or lowers the rotation dial.
        ///
        /// Only meaningful on a join: the dial turns the part about the mating
        /// axis, and with nothing to mate to there is no axis to turn about.
        /// Off the join, R is the way back to moving the part around.
        /// </summary>
        private void ToggleHoleRotation()
        {
            if (rotatingAboutHole)
            {
                StopHoleRotation();
                MessageBanner.Info("Rotation off — move the part, R to rotate again");
                return;
            }

            if (!snapTarget.IsValid)
            {
                MessageBanner.Warn("Hold the hole against another one first");
                return;
            }

            rotatingAboutHole = true;
            rollBeforeRotating = holeRoll;

            // The pointer takes the mouse over, and starts on the dial at the
            // angle already set. Starting it in the middle of the screen would
            // yank the part round to whatever angle that happened to be, which
            // is a jump nobody asked for.
            pointer.ShowPointer(true);
            PlacePointerOnDial();

            MessageBanner.Info(
                $"Rotating about the join — hold Shift for {holeRollSnapDegrees:0}°");
        }

        private void StopHoleRotation()
        {
            rotatingAboutHole = false;
            rotationRing?.Hide();
            interactionLock.CameraOrbitLocked = false;
            pointer?.ShowPointer(false);
        }

        /// <summary>Puts the pointer on the dial where the needle already is.</summary>
        private void PlacePointerOnDial()
        {
            Camera cam = Camera.main;
            if (cam == null || !snapTarget.IsValid)
            {
                return;
            }

            Vector3 radial = Quaternion.AngleAxis(holeRoll, snapTarget.WorldNormal)
                * ringZeroDirection.normalized;

            Vector3 world = snapTarget.WorldPosition + (radial * HoleRotationRing.RadiusMetres);
            Vector3 screen = cam.WorldToScreenPoint(world);

            if (screen.z > 0f)
            {
                pointer.PlacePointer(new Vector2(screen.x, screen.y));
            }
        }

        /// <summary>
        /// Points the part at the pointer.
        ///
        /// The angle is read from where the pointer *is*, not accumulated from
        /// how far the mouse moved. Accumulating made the result depend on the
        /// path the hand took rather than on where it ended up, so the same
        /// screen position gave a different angle every time - and snapping,
        /// which rounds the running total, only bit when a fast movement
        /// happened to carry it over a boundary.
        ///
        /// Read by crossing the pointer's ray with the mating plane, so the
        /// part tracks the pointer around the dial as drawn rather than around
        /// a flat circle that only agrees with it when viewed head-on.
        /// </summary>
        private void UpdateHoleRoll()
        {
            // No zero mark means nothing to measure from, and SignedAngle
            // against a zero vector returns NaN straight into the transform.
            if (ringZeroDirection.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Vector3 axis = snapTarget.WorldNormal;
            Vector3 centre = snapTarget.WorldPosition;
            Ray ray = pointer.PointerRay;

            float facing = Vector3.Dot(ray.direction, axis);

            // Edge-on to the dial: the crossing point runs away to infinity and
            // the angle it implies is noise, so the last good one stands.
            if (Mathf.Abs(facing) < 0.08f)
            {
                return;
            }

            float distance = Vector3.Dot(centre - ray.origin, axis) / facing;
            if (distance <= 0f)
            {
                return;
            }

            Vector3 radial = Vector3.ProjectOnPlane(
                ray.origin + (ray.direction * distance) - centre, axis);

            // Pointer dead on the centre, where every angle is equally true.
            if (radial.sqrMagnitude < 1e-8f)
            {
                return;
            }

            float angle = Vector3.SignedAngle(ringZeroDirection, radial, axis);

            // Zero is the square-on position, so rounding from here lands on
            // exact alignment with the other part at every quarter turn - which
            // is the point of snapping to the part rather than to the world.
            if (actions != null && actions.SnapHeld && holeRollSnapDegrees > 0f)
            {
                angle = Mathf.Round(angle / holeRollSnapDegrees) * holeRollSnapDegrees;
            }

            holeRoll = Mathf.Repeat(angle, 360f);
        }

        private void UpdateRotationRing()
        {
            if (!rotatingAboutHole || !snapTarget.IsValid)
            {
                rotationRing?.Hide();
                return;
            }

            rotationRing ??= HoleRotationRing.Create(snapColour);
            rotationRing.Show(
                snapTarget.WorldPosition, snapTarget.WorldNormal, ringZeroDirection, holeRoll);
        }

        /// <summary>
        /// Lets go of the hole: either leaving the part where the ghost was, or
        /// putting it back where it came from.
        /// </summary>
        private void EndHoleCarry(bool commit)
        {
            GameObject placed = carried;
            bool mated = commit && snapTarget.IsValid;

            if (!commit && placed != null)
            {
                placed.transform.SetPositionAndRotation(
                    holeCarryStartPosition, holeCarryStartRotation);
            }

            ghost?.SetGhosted(false);
            ghost = null;

            StopHoleRotation();

            snapTarget = default;
            snapMarker?.Hide();

            RestoreColliders();

            if (placed != null && mated)
            {
                // Pinned rather than dropped. The part was placed against a
                // face on purpose, and gravity would pull it straight back off.
                HoleMating.SyncBody(placed.transform);
            }

            // Released while the hole flag is still set, so the repeat
            // modifier does not read this as "place another one" - Alt means
            // stamping copies from the shelf, not duplicating a part that was
            // only being repositioned.
            Release();

            carryingByHole = false;
            carriedHoles = null;
            carriedHole = default;
            holeRoll = 0f;
        }

        /// <summary>
        /// Switches off the ghost's colliders for the duration.
        ///
        /// A ghost is meant to be held inside the part it is being fitted to,
        /// so it must not push anything and nothing must push it. It also keeps
        /// the aim ray clear, so holes behind the ghost can still be chosen.
        /// </summary>
        private void SuspendColliders(GameObject go)
        {
            suspendedColliders.Clear();

            foreach (Collider collider in go.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled)
                {
                    collider.enabled = false;
                    suspendedColliders.Add(collider);
                }
            }
        }

        private void RestoreColliders()
        {
            foreach (Collider collider in suspendedColliders)
            {
                if (collider != null)
                {
                    collider.enabled = true;
                }
            }

            suspendedColliders.Clear();
        }

        // ------------------------------------------------------------------
        // Taking
        // ------------------------------------------------------------------

        /// <summary>
        /// Creates a fresh part and puts it in hand, at the distance of
        /// whatever was clicked. Spawning at a fixed distance instead made a
        /// part taken from the shelf appear far away and inside the bench.
        /// </summary>
        /// <summary>
        /// Creates a fresh part and puts it in hand.
        ///
        /// <paramref name="keepDistance"/> holds the part at the distance the
        /// last one was at. Used when placing repeatedly with Alt: without it
        /// every duplicate snapped back to wherever the shelf copy had been,
        /// undoing the reach the user had just set.
        /// </summary>
        public void BeginCarryNew(PartDefinition definition, float keepDistance = -1f)
        {
            if (definition == null)
            {
                Debug.LogWarning("[Parts] Tried to take a part with no definition.");
                return;
            }

            if (keepDistance > 0f)
            {
                GameObject repeat = PartFactory.Create(definition, withPhysics: false);
                if (repeat != null)
                {
                    Ray aim = pointer.AimRay;
                    repeat.transform.position = aim.origin + (aim.direction * keepDistance);
                    AttachToHand(repeat, definition, keepDistance, repeat.transform.position);
                }

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

            // A frozen part stays frozen when grabbed - only K releases it -
            // but it is still fully movable while held. See AttachToHand.
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
                // Made dynamic even when frozen. Freezing means "does not fall",
                // not "does not move" - a pinned sub-assembly still has to be
                // nudged into place, and being unable to move what you are
                // holding reads as the grab having failed.
                carriedBody.isKinematic = false;
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

            // Look is locked only while rotating, where mouse movement means
            // turning the part rather than turning the head.
            interactionLock.CameraOrbitLocked = rotating;

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

            targetRotation = Quaternion.AngleAxis(delta, Vector3.up) * targetRotation;
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
            if (!IsCarrying || carryingByHole || carriedBody == null || pointer == null)
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
        }


        private void ToggleFreezeCarried()
        {
            PartGroup group = carriedInstance?.Group;
            if (group == null)
            {
                return;
            }

            group.SetFrozen(!group.IsFrozen);

            MessageBanner.Info(group.IsFrozen
                ? "Frozen — floats in place, still movable. K to release"
                : "Unfrozen");
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
            float heldAt = carryDistance;

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
            if (!frozen && !carryingByHole && pointer.RepeatModifierHeld && definition != null)
            {
                BeginCarryNew(definition, heldAt);
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

            // A ghosted part left behind by a disabled controller would stay
            // see-through and intangible with nothing able to put it right.
            if (carryingByHole)
            {
                EndHoleCarry(commit: false);
            }
        }
    }
}
