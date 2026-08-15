namespace VexDesigner.Parts
{
    using System.Collections.Generic;
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
        [Tooltip("Closest a carried part can be drawn in, in metres.\n\n" +
                 "About three inches: near enough to put an eye to a join, and " +
                 "no further out than the player's own body, so scrolling all " +
                 "the way in is not stopped short by nothing visible.")]
        [SerializeField] private float minCarryDistance = 0.08f;
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

        [Tooltip("How close the crosshair must pass to a screw's axis to be " +
                 "pointing at it, in metres. A quarter inch: wide enough that a " +
                 "screw is a target rather than a pixel.")]
        [SerializeField] private float screwAimTolerance = 0.0064f;

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

        /// <summary>Where the carried hole would thread onto the screw under the crosshair.</summary>
        private NutSeating screwSeat;

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

        private readonly List<Collider> suspendedColliders = new List<Collider>();

        // --- Fitting a screw or a nut ---------------------------------------

        /// <summary>The hole a carried screw is lined up with.</summary>
        private HoleHit screwTarget;

        /// <summary>Where a carried nut would go on the screw under the cursor.</summary>
        private NutSeating nutTarget;

        /// <summary>True while a fastener is snapped and awaiting a click.</summary>
        private bool fastenerPreview;

        private HoleHighlighter fastenerMarker;

        [Tooltip("Colour of the seat a carried nut would take on a screw. " +
                 "Turns to the warning colour when the screw is too short.")]
        [SerializeField] private Color nutSeatColour = new Color(0.4f, 1f, 0.5f);

        /// <summary>True while a part is being positioned by one of its holes.</summary>
        public bool IsCarryingByHole => carryingByHole;

        /// <summary>True while a carried screw or nut is lined up to be fitted.</summary>
        public bool IsFittingFastener => fastenerPreview;

        /// <summary>True when the carried hole is lined up on a destination.</summary>
        public bool HoleIsSnapped => carryingByHole && HasSnapTarget;

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
            //
            // Only in grab mode. The transform tool never picks anything up, so
            // a hole there is just a place on the part - it selects, and the
            // gizmo lands on the hole that was clicked.
            bool transformActive = TransformTool != null && TransformTool.IsActive;

            if (HasHoleTarget && !transformActive && pointer.PrimaryPressedThisFrame)
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
            bool farSide = actions != null && actions.FarSideHeld;

            if (!TryAimAnyHole(pointer.AimRay, farSide, out HoleHit hit))
            {
                ClearHoleAim();
                return;
            }

            PartHoles holes = hit.Part;

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

        /// <summary>
        /// The nearest hole on any part along the ray.
        ///
        /// Every part the ray touches is asked, not just the one the hover test
        /// settled on. Those are different questions and the difference showed:
        /// aiming down a hole means the ray misses that part's metal
        /// altogether, so the hover landed on whatever was *behind* it and the
        /// hole selected was the one on the far side of the gap - reliably
        /// picking the hole behind the one being aimed at.
        ///
        /// Asking every candidate and keeping the nearest makes "the first hole
        /// along the ray" true by construction, which is the rule a person
        /// would expect from pointing at something.
        /// </summary>
        private bool TryAimAnyHole(Ray ray, bool farSide, out HoleHit best)
        {
            best = default;

            int count = Physics.RaycastNonAlloc(
                ray, hits, aimDistance, ~0, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (IsCarriedCollider(hits[i].collider))
                {
                    continue;
                }

                var holes = hits[i].collider.GetComponentInParent<PartHoles>();

                if (holes == null || !holes.HasHoles ||
                    holes.GetComponent<PickupHandle>() is not { Interactable: true })
                {
                    continue;
                }

                if (!holes.TryAim(ray, farSide, out HoleHit hit))
                {
                    continue;
                }

                float along = Vector3.Dot(hit.WorldPosition - ray.origin, ray.direction);

                if (along < bestDistance)
                {
                    bestDistance = along;
                    best = hit;
                }
            }

            return best.IsValid;
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
            var holePart = hit.Part.GetComponent<PartInstance>();

            // The body belongs to the assembly, not to the part the hole is
            // in. Grabbing a follower found no Rigidbody, one was added to
            // cope, and adding it broke the very weld holding the robot
            // together - so clicking a hole quietly took that part out of its
            // own assembly and carried it off alone.
            PartInstance instance = holePart?.Group?.Leader ?? holePart;
            GameObject go = instance != null ? instance.gameObject : hit.Part.gameObject;

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

            // The hole is re-expressed in the carried body's own space. Every
            // pose calculation then works on the thing that actually moves,
            // and a hole belonging to a bolted-on bracket positions the whole
            // robot exactly as one in the body itself would.
            carriedHole = hit;
            carriedHole.Face = new HoleFace
            {
                localPosition = go.transform.InverseTransformPoint(hit.WorldPosition),
                localNormal = go.transform.InverseTransformDirection(hit.WorldNormal),
                width = hit.Face.width,
            };
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
            SetAssemblyGhosted(true);
        }

        /// <summary>
        /// Turns the whole assembly to glass, not just the part in hand.
        ///
        /// A bolted robot held by one of its channels is a single object, and
        /// ghosting one part of it would say otherwise - as well as leaving
        /// most of the join the user is trying to judge still opaque.
        /// </summary>
        private void SetAssemblyGhosted(bool ghosted)
        {
            PartGroup group = carriedInstance?.Group;

            if (group == null)
            {
                return;
            }

            foreach (PartInstance part in group.Members)
            {
                if (part == null)
                {
                    continue;
                }

                var partGhost = part.GetComponent<PartGhost>()
                    ?? part.gameObject.AddComponent<PartGhost>();

                partGhost.SetGhosted(ghosted);
            }

            ghost = ghosted ? carried.GetComponent<PartGhost>() : null;
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

            if (HasSnapTarget || rotatingAboutHole || Mathf.Abs(delta) < 0.001f)
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
            bool wasSnapped = HasSnapTarget;

            // A screw first. Threading a part onto one is a more specific
            // intention than lining its hole up with another hole, and if a
            // screw is what the crosshair is on then it is what was meant.
            if (TryScrewSeat())
            {
                return;
            }

            screwSeat = default;

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
        /// Threads the carried hole onto the screw under the crosshair, if
        /// there is one.
        ///
        /// The part slides up the shank until it meets metal, exactly as a nut
        /// does - so looking near the head of a screw already through a plate
        /// brings the held hole flush against that plate, which is where it
        /// would physically end up.
        /// </summary>
        private bool TryScrewSeat()
        {
            PlacedScrew screw = AimedScrew();

            if (screw == null || carriedHoles == null || !carriedHoles.HasHoles)
            {
                return false;
            }

            // The held part is in the hand, not on the screw, so it must not
            // count as something the screw already passes through.
            screw.RecomputePasses(carriedHoles);

            float thickness = carriedHoles.Holes.holes[carriedHole.HoleIndex].depth;
            NutSeating seat = FastenerFitting.FindSeating(screw, thickness, pointer.AimRay);

            if (!seat.IsValid || !seat.Fits)
            {
                return false;
            }

            if (!screwSeat.IsValid)
            {
                // Coming onto a screw from free carry, the roll starts where the
                // part already is rather than snapping to an arbitrary zero.
                holeRoll = 0f;
            }

            screwSeat = seat;
            snapTarget = default;

            snapMarker ??= HoleHighlighter.Create("SnapTargetHole", snapColour);
            snapMarker.SetColour(snapColour);
            snapMarker.Show(new HoleHit
            {
                Part = carriedHoles,
                Face = carriedHole.Face,
                Shape = carriedHole.Shape,
                WorldPosition = seat.WorldPosition,
                WorldNormal = seat.WorldNormal,
            });

            return true;
        }

        /// <summary>True while the carried hole has somewhere to go.</summary>
        private bool HasSnapTarget => snapTarget.IsValid || screwSeat.IsValid;

        /// <summary>Where the carried hole is coming to rest, either way.</summary>
        private Vector3 SeatPosition => screwSeat.IsValid
            ? screwSeat.WorldPosition
            : snapTarget.WorldPosition;

        /// <summary>The axis it turns about, either way.</summary>
        private Vector3 SeatNormal => screwSeat.IsValid
            ? screwSeat.WorldNormal
            : snapTarget.WorldNormal;

        /// <summary>
        /// Puts the part where the current state says it goes: on the join if
        /// there is one, hanging off the crosshair if not.
        /// </summary>
        private void ApplyHolePose()
        {
            Transform moving = carried.transform;

            if (screwSeat.IsValid)
            {
                // Turn the held hole to look back up the screw, the way it
                // would if it had been slid on from the free end.
                Vector3 axis = screwSeat.WorldNormal;
                Vector3 current = (targetRotation * carriedHole.Face.localNormal).normalized;

                Quaternion rotation = Quaternion.FromToRotation(current, axis) * targetRotation;
                rotation = Quaternion.AngleAxis(holeRoll, axis) * rotation;

                Vector3 offset = rotation *
                    Vector3.Scale(carriedHole.Face.localPosition, moving.lossyScale);

                moving.SetPositionAndRotation(screwSeat.WorldPosition - offset, rotation);

                ringZeroDirection = Vector3.ProjectOnPlane(
                    screwSeat.Screw.transform.right, axis).normalized;
            }
            else if (snapTarget.IsValid)
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

            if (!HasSnapTarget)
            {
                MessageBanner.Warn("Hold the hole against something first");
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
            if (cam == null || !HasSnapTarget)
            {
                return;
            }

            Vector3 radial = Quaternion.AngleAxis(holeRoll, SeatNormal)
                * ringZeroDirection.normalized;

            Vector3 world = SeatPosition + (radial * HoleRotationRing.RadiusMetres);
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

            // Either kind of seat has an axis to turn about. Reading only the
            // hole-to-hole one left a part threaded onto a screw unable to
            // rotate at all: the dial came up, and every reading was taken
            // against a zero vector.
            Vector3 axis = SeatNormal;
            Vector3 centre = SeatPosition;
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
            if (!rotatingAboutHole || !HasSnapTarget)
            {
                rotationRing?.Hide();
                return;
            }

            rotationRing ??= HoleRotationRing.Create(snapColour);
            rotationRing.Show(SeatPosition, SeatNormal, ringZeroDirection, holeRoll);
        }

        /// <summary>
        /// Lets go of the hole: either leaving the part where the ghost was, or
        /// putting it back where it came from.
        /// </summary>
        private void EndHoleCarry(bool commit)
        {
            GameObject placed = carried;
            bool mated = commit && HasSnapTarget;
            bool onScrew = commit && screwSeat.IsValid;
            PlacedScrew seatedOn = screwSeat.Screw;

            if (!commit && placed != null)
            {
                placed.transform.SetPositionAndRotation(
                    holeCarryStartPosition, holeCarryStartRotation);
            }

            SetAssemblyGhosted(false);
            ghost = null;

            StopHoleRotation();

            snapTarget = default;
            screwSeat = default;
            snapMarker?.Hide();
            fastenerMarker?.Hide();
            fastenerPreview = false;

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

            // A part threaded onto a screw changes what that screw holds, so
            // the assembly has to be worked out again.
            if (onScrew && seatedOn != null)
            {
                seatedOn.Refresh();
            }
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

            // Taking a nut off is how a build comes apart. Everything else
            // picks up the whole assembly; a nut picks up only itself, and
            // whatever it alone was holding falls apart behind it.
            //
            // Only what it *alone* held. A part with four screws through it
            // stays put when one nut comes off, which is the whole reason the
            // grouping is derived from the fasteners rather than remembered.
            LoosenNut(existing.GetComponent<PartInstance>());

            var instance = existing.GetComponent<PartInstance>();

            // A bolted assembly is one rigid body, and that body belongs to the
            // leader. Grabbing a follower would find no Rigidbody, and adding
            // one would break the very weld that holds the robot together.
            PartInstance leader = instance?.Group?.Leader;

            if (leader != null && leader != instance)
            {
                existing = leader.gameObject;
                instance = leader;
            }

            PartDefinition existingDefinition = instance == null ? null : instance.Definition;

            // A frozen part stays frozen when grabbed - only K releases it -
            // but it is still fully movable while held. See AttachToHand.
            Vector3 grabPoint = hasLastHit ? lastHitPoint : existing.transform.position;

            // A screw is held by its head, wherever it was clicked. Held by a
            // point halfway down the shank it hangs at an angle and has to be
            // aimed by its middle, when the head is the part you actually put
            // against the metal - and the end that has to line up with a hole.
            if (existingDefinition != null && existingDefinition.IsScrew)
            {
                grabPoint = existing.transform.TransformPoint(
                    existingDefinition.fastener.localSeatPoint);
            }
            float distance = hasLastHit
                ? Mathf.Clamp(lastHitDistance, minCarryDistance, maxCarryDistance)
                : Vector3.Distance(pointer.AimRay.origin, existing.transform.position);

            AttachToHand(existing, existingDefinition, distance, grabPoint);
        }

        /// <summary>
        /// Rebuilds the assembly as though the nut about to be picked up were
        /// already off the screw.
        ///
        /// Done before the grab rather than after, because the grab reads the
        /// group to decide what comes with it. Rebuilding afterwards would pick
        /// the whole robot up by the nut and only then let go of it.
        /// </summary>
        private static void LoosenNut(PartInstance instance)
        {
            if (instance == null || instance.Definition == null || !instance.Definition.IsNut)
            {
                return;
            }

            Assembly.Rebuild(instance);
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

            // And none of it shoves the player around. Walking into what you
            // are carrying is the commonest way to knock a build over, and it
            // is never what anyone meant to do.
            IgnorePlayerCollision(true);

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

            if (actions != null && actions.FreezePressed)
            {
                ToggleFreezeCarried();
                return;
            }

            // A screw over a hole, or a nut over a screw, stops being carried
            // and starts being fitted. It is the same gesture either way -
            // hold it where it goes, click to leave it there - which is what
            // makes fastening feel like part of placing rather than a mode of
            // its own.
            if (UpdateFastenerPreview())
            {
                if (pointer.PrimaryPressedThisFrame)
                {
                    CommitFastener();
                }

                return;
            }

            CarryWithBody();

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

        // ------------------------------------------------------------------
        // Fitting screws and nuts
        // ------------------------------------------------------------------

        /// <summary>
        /// Lines a carried screw up with the hole under the crosshair, or a
        /// carried nut with the screw under it. True while something is lined
        /// up and a click would fit it.
        /// </summary>
        private bool UpdateFastenerPreview()
        {
            PartDefinition definition = carriedDefinition;

            if (definition == null || carried == null)
            {
                return EndFastenerPreview();
            }

            if (definition.IsScrew)
            {
                return UpdateScrewPreview(definition);
            }

            // A nut goes on a screw if there is one under the crosshair, and
            // otherwise mates hole to hole like anything else.
            if (definition.IsNut && UpdateNutPreview(definition))
            {
                return true;
            }

            // One hole means there is nothing to choose. Making the user click
            // precisely on a nut's bore to line it up would be asking them to
            // name the only option there is, so grabbing it anywhere is enough.
            if (!definition.IsScrew && definition.holeSet != null &&
                definition.holeSet.Count == 1)
            {
                return UpdateSingleHoleMate(definition);
            }

            return EndFastenerPreview();
        }

        /// <summary>
        /// Lines up a one-holed part with the hole under the crosshair, however
        /// it happens to have been picked up.
        /// </summary>
        private bool UpdateSingleHoleMate(PartDefinition definition)
        {
            var holes = RaycastFor<PartHoles>(out _);
            bool farSide = actions != null && actions.FarSideHeld;

            PartHoles own = carried.GetComponent<PartHoles>();

            if (holes == null || holes == own || !holes.HasHoles ||
                !holes.TryAim(pointer.AimRay, farSide, out HoleHit hit))
            {
                return EndFastenerPreview();
            }

            Hole hole = definition.holeSet.holes[0];

            // Whichever face is already pointing at the target is the one that
            // seats against it. Always taking the front face meant a nut held
            // the natural way up was turned over to put its far side down -
            // the wrong side, and a needless somersault on the way.
            Vector3 wanted = -hit.WorldNormal;

            HoleFace seat =
                Vector3.Dot(targetRotation * hole.front.localNormal, wanted) >=
                Vector3.Dot(targetRotation * hole.back.localNormal, wanted)
                    ? hole.front
                    : hole.back;

            screwTarget = hit;
            nutTarget = default;
            mateFace = seat;

            BeginFastenerPreview();

            if (HoleMating.ComputePose(
                    seat, targetRotation, hit, squareOnSnapDegrees, 0f,
                    carried.transform.lossyScale,
                    out Vector3 position, out Quaternion rotation, out _))
            {
                PlaceGhost(position, rotation);
            }

            fastenerMarker ??= HoleHighlighter.Create("FastenerSeat", nutSeatColour);
            fastenerMarker.SetColour(nutSeatColour);
            fastenerMarker.Show(hit);

            mating = true;
            return true;
        }

        /// <summary>True while a one-holed part is being lined up on a hole.</summary>
        private bool mating;

        /// <summary>The face of it that will meet the metal.</summary>
        private HoleFace mateFace;

        private bool UpdateScrewPreview(PartDefinition definition)
        {
            var holes = RaycastFor<PartHoles>(out _);
            bool farSide = actions != null && actions.FarSideHeld;

            if (holes == null || !holes.HasHoles ||
                !holes.TryAim(pointer.AimRay, farSide, out HoleHit hit))
            {
                return EndFastenerPreview();
            }

            screwTarget = hit;
            nutTarget = default;
            mating = false;

            BeginFastenerPreview();

            if (FastenerFitting.ScrewPose(
                    definition, targetRotation, hit, carried.transform.lossyScale,
                    out Vector3 position, out Quaternion rotation))
            {
                PlaceGhost(position, rotation);
            }

            fastenerMarker ??= HoleHighlighter.Create("FastenerSeat", nutSeatColour);
            fastenerMarker.SetColour(nutSeatColour);
            fastenerMarker.Show(hit);

            return true;
        }

        private bool UpdateNutPreview(PartDefinition definition)
        {
            PlacedScrew screw = AimedScrew();

            if (screw == null)
            {
                return EndFastenerPreview();
            }

            // Recomputed every frame rather than when the screw was placed. The
            // parts a screw runs through can be moved after the fact, and a nut
            // offered against a stale idea of where the metal is would seat in
            // mid-air.
            //
            // Passes only. Grouping happens on the click, not on the hover.
            screw.RecomputePasses(carried.GetComponent<PartHoles>());

            NutSeating seating = FastenerFitting.FindNutSeating(
                screw, definition, pointer.AimRay);

            // No thread left for it. Not an error - there is simply nowhere
            // on this screw for this nut, so it stays in the hand.
            if (!seating.IsValid || !seating.Fits)
            {
                return EndFastenerPreview();
            }

            nutTarget = seating;
            screwTarget = default;
            mating = false;

            BeginFastenerPreview();

            FastenerFitting.NutPose(
                definition, targetRotation, seating, carried.transform.lossyScale,
                out Vector3 position, out Quaternion rotation);

            PlaceGhost(position, rotation);

            fastenerMarker ??= HoleHighlighter.Create("FastenerSeat", nutSeatColour);
            fastenerMarker.SetColour(nutSeatColour);

            fastenerMarker.Show(new HoleHit
            {
                Part = screw.GetComponent<PartHoles>(),
                Face = new HoleFace
                {
                    localPosition = Vector3.zero,
                    localNormal = Vector3.forward,
                    width = definition.holeSet.IsEmpty
                        ? 0.006f
                        : definition.holeSet.holes[0].front.width * 1.6f,
                },
                WorldPosition = seating.WorldPosition,
                WorldNormal = seating.WorldNormal,
            });

            return true;
        }

        /// <summary>
        /// Switches the carried part from being chased by physics to being
        /// placed exactly, the first time a fastener lines up.
        /// </summary>
        private void BeginFastenerPreview()
        {
            if (fastenerPreview)
            {
                return;
            }

            fastenerPreview = true;

            if (carriedBody != null)
            {
                carriedBody.isKinematic = true;
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;
            }

            SuspendColliders(carried);

            ghost = carried.GetComponent<PartGhost>() ?? carried.AddComponent<PartGhost>();
            ghost.SetGhosted(true);
        }

        private bool EndFastenerPreview()
        {
            if (!fastenerPreview)
            {
                return false;
            }

            fastenerPreview = false;
            screwTarget = default;
            nutTarget = default;
            mating = false;

            fastenerMarker?.Hide();

            ghost?.SetGhosted(false);
            ghost = null;

            RestoreColliders();

            // Back into the hand, where physics can chase the aim again.
            if (carriedBody != null && carried != null)
            {
                carriedBody.isKinematic = false;
                carriedBody.useGravity = false;
                carriedBody.linearVelocity = Vector3.zero;
                carriedBody.angularVelocity = Vector3.zero;

                targetRotation = carried.transform.rotation;
                lastCarrierYaw = transform.eulerAngles.y;
            }

            return false;
        }

        private void PlaceGhost(Vector3 position, Quaternion rotation)
        {
            carried.transform.SetPositionAndRotation(position, rotation);

            if (carriedBody != null)
            {
                carriedBody.position = position;
                carriedBody.rotation = rotation;
            }
        }

        /// <summary>
        /// Leaves the fastener where the preview put it, and works out what it
        /// now holds together.
        /// </summary>
        private void CommitFastener()
        {
            GameObject placed = carried;
            PlacedScrew screw = nutTarget.Screw;
            NutSeating seating = nutTarget;
            bool wasMating = mating;

            ghost?.SetGhosted(false);
            ghost = null;

            fastenerMarker?.Hide();
            RestoreColliders();

            fastenerPreview = false;
            screwTarget = default;
            nutTarget = default;

            // Pinned where it was placed. A fastener is positioned to a
            // thousandth of an inch and gravity would undo that immediately.
            HoleMating.SyncBody(placed.transform);

            Release();

            if (placed == null)
            {
                return;
            }

            if (screw != null)
            {
                // Nothing to record. The nut is now sitting on the screw, and
                // the screw finds it there.
                Assembly.Rebuild();

                MessageBanner.Info(seating.InGap
                    ? "Nut fitted in the gap — everything above it is joined"
                    : "Nut tightened — the stack is joined");

                return;
            }

            if (wasMating)
            {
                // Touching, not joined. A screw still has to go through it.
                Assembly.Rebuild();
                return;
            }

            // A screw. It records what it runs through, which is what decides
            // whether anything is actually held together.
            var driven = placed.GetComponent<PlacedScrew>()
                ?? placed.AddComponent<PlacedScrew>();

            driven.Refresh();

            MessageBanner.Info(driven.GripDepth() >= 0f
                ? "Screwed in — the stack is joined"
                : "Screw placed — add a nut to fasten it");
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
            if (!IsCarrying || carryingByHole || fastenerPreview ||
                carriedBody == null || pointer == null)
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

            IgnorePlayerCollision(false);

            // Back in the world, so back in the graph. A nut set down on the
            // screw it came off grips again, without anything having tracked
            // that it was ever removed.
            bool wasNut = carriedDefinition != null && carriedDefinition.IsNut;

            group?.SetGrabbed(false);
            group?.WakeNeighbours();

            carried = null;
            carriedCollider = null;
            carriedBody = null;
            carriedInstance = null;
            carriedDefinition = null;

            interactionLock.CameraOrbitLocked = false;

            if (wasNut)
            {
                Assembly.Rebuild();
            }

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
            int bestRank = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (hits[i].distance >= bestDistance && bestRank == 0)
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

                bool metal = TrueDistance(hits[i], out float distance);

                // Solid metal always beats a part the ray only passes near.
                // Without that a nut inside a C-channel could never be
                // clicked, because the channel's hull is an invisible wall
                // across the opening and is always nearer.
                int rank = metal ? 0 : 1;

                if (rank > bestRank || (rank == bestRank && distance >= bestDistance))
                {
                    continue;
                }

                best = candidate;
                bestRank = rank;
                bestDistance = distance;
                nearestHit = hits[i];
            }

            return best;
        }

        /// <summary>
        /// How far away a part really is, as opposed to how far away its
        /// collider claims to be.
        ///
        /// The two differ badly, because a part's collider is the convex hull
        /// of its mesh. A C-channel's hull is a solid block filling the
        /// channel, so anything sitting inside the channel - a nut on the end
        /// of a screw, most obviously - is behind an invisible wall as far as
        /// the physics engine is concerned, and every click meant for it lands
        /// on the C-channel instead.
        ///
        /// Testing the actual triangles puts that right: the hull is ignored
        /// where there is no metal, so the nut is simply the nearest thing and
        /// the click reaches it.
        /// </summary>
        /// <summary>
        /// How far away a part's actual metal is, and whether the ray met any.
        ///
        /// The two differ badly, because a part's collider is the convex hull
        /// of its mesh. A C-channel's hull is a solid block filling the
        /// channel, so anything inside the channel - a nut on the end of a
        /// screw, most obviously - sits behind an invisible wall as far as the
        /// physics engine is concerned, and every click meant for it lands on
        /// the C-channel instead.
        ///
        /// Missing the metal entirely is not a reason to ignore the part,
        /// though, and treating it as one broke hole aiming outright: pointing
        /// *at* a hole means pointing at a gap in the metal, and on a
        /// C-channel's web the ray goes cleanly through the hole and out of
        /// the open side without touching anything. The part is still what the
        /// user is pointing at. So a near miss still counts - it just loses to
        /// anything the ray genuinely hits.
        /// </summary>
        private bool TrueDistance(RaycastHit hit, out float distance)
        {
            distance = hit.distance;

            var filter = hit.collider.GetComponentInParent<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
            {
                // Not a part - the bench, a wall. The collider is the shape.
                return true;
            }

            MeshRayTester tester = MeshRayTester.For(filter.sharedMesh);

            if (tester == null)
            {
                return true;
            }

            Transform t = filter.transform;
            Ray ray = pointer.AimRay;

            Vector3 localOrigin = t.InverseTransformPoint(ray.origin);
            Vector3 localDirection = t.InverseTransformDirection(ray.direction).normalized;

            if (!tester.FirstCrossing(localOrigin, localDirection, aimDistance, out float local))
            {
                return false;
            }

            distance = local * t.lossyScale.x;
            return true;
        }

        /// <summary>
        /// <typeparamref name="T"/> on the very first thing the ray meets, or
        /// null if something else is in front of it.
        ///
        /// Different from <see cref="RaycastFor{T}"/>, which looks past
        /// anything lacking the component. Both are wanted: reaching a part
        /// behind the bench is right, reaching a screw behind a plate is not.
        /// </summary>
        /// <summary>
        /// The placed screw the user is pointing at, found by how near the aim
        /// passes to it rather than by what it hits.
        ///
        /// A screw is four millimetres across and most of its length is inside
        /// the metal it holds, so requiring the ray to strike its collider as
        /// the nearest thing in the scene made short screws impossible to
        /// address at all - the surrounding part is always in the way, and on a
        /// screw that does not protrude there is nothing exposed to hit.
        ///
        /// Measuring to the screw's *axis* asks the question the user is
        /// actually asking, which is "that screw, there".
        /// </summary>
        private PlacedScrew AimedScrew()
        {
            Ray ray = pointer.AimRay;

            PlacedScrew best = null;
            float bestAlong = float.MaxValue;

            IReadOnlyList<PlacedScrew> screws = PlacedScrew.All;

            for (int i = 0; i < screws.Count; i++)
            {
                PlacedScrew screw = screws[i];

                if (screw == null || IsCarriedCollider(screw.GetComponent<Collider>()))
                {
                    continue;
                }

                Vector3 seat = screw.Seat;
                Vector3 direction = screw.Direction;

                // Head included: pointing at the head is pointing at the screw,
                // and on a short one the head is most of what can be seen.
                float headBack = screw.HeadHeight;

                float nearest = float.MaxValue;
                float along = 0f;

                // Sampled along the shank rather than solved, because the
                // segment is short and the closest-approach solution needs
                // clamping and special cases for a ray nearly parallel to it.
                const int samples = 12;

                for (int s = 0; s <= samples; s++)
                {
                    float t = Mathf.Lerp(-headBack, screw.Length, s / (float)samples);
                    Vector3 point = seat + (direction * t);

                    Vector3 offset = point - ray.origin;
                    float depth = Vector3.Dot(offset, ray.direction);

                    if (depth <= 0f)
                    {
                        continue;
                    }

                    float miss = (offset - (ray.direction * depth)).magnitude;

                    if (miss < nearest)
                    {
                        nearest = miss;
                        along = depth;
                    }
                }

                // Generously wide - about a quarter inch, twice the shank - so
                // a screw can be pointed at rather than aimed at.
                if (nearest > screwAimTolerance || along >= bestAlong)
                {
                    continue;
                }

                best = screw;
                bestAlong = along;
            }

            return best;
        }

        private T RaycastNearest<T>() where T : class
        {
            int count = Physics.RaycastNonAlloc(
                pointer.AimRay, hits, aimDistance, ~0, QueryTriggerInteraction.Ignore);

            Collider nearest = null;
            float nearestDistance = float.MaxValue;
            int nearestRank = int.MaxValue;

            for (int i = 0; i < count; i++)
            {
                if (IsCarriedCollider(hits[i].collider))
                {
                    continue;
                }

                // Ranked by real metal first, exactly as the hover test is.
                // Judging by collider distance alone made a screw inside a
                // C-channel unreachable: the channel's convex hull fills the
                // channel, so the hull was always nearer than the screw, the
                // nearest thing under the crosshair was the channel, and a nut
                // could never find a screw to go on. A screw short enough to
                // sit entirely inside the hull could never take one at all.
                bool metal = TrueDistance(hits[i], out float distance);
                int rank = metal ? 0 : 1;

                if (rank > nearestRank ||
                    (rank == nearestRank && distance >= nearestDistance))
                {
                    continue;
                }

                nearest = hits[i].collider;
                nearestDistance = distance;
                nearestRank = rank;
            }

            return nearest == null ? null : nearest.GetComponentInParent<T>();
        }

        /// <summary>
        /// Stops the player and what they are holding from pushing each other.
        ///
        /// Both directions matter. A carried part driven into the player's
        /// capsule gets shoved aside and ends up floating off the crosshair;
        /// and a player who walks forward into a held part is pushed back by
        /// their own hands, which feels like the controls have stopped working.
        /// </summary>
        private void IgnorePlayerCollision(bool ignore)
        {
            Collider self = GetComponent<Collider>()
                ?? GetComponentInParent<Collider>();

            PartGroup group = carriedInstance?.Group;

            if (self == null || group == null)
            {
                return;
            }

            foreach (PartInstance part in group.Members)
            {
                if (part == null)
                {
                    continue;
                }

                foreach (Collider collider in part.GetComponentsInChildren<Collider>())
                {
                    Physics.IgnoreCollision(collider, self, ignore);
                }
            }
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
