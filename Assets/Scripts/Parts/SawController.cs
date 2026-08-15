namespace VexDesigner.Parts
{
    using UnityEngine;
    using VexDesigner.InputSources;
    using VexDesigner.UI;

    /// <summary>
    /// Running the saw: getting in, turning the knobs, and getting out.
    ///
    /// A mode rather than a set of world interactions. Setting up a cut wants a
    /// steady top-down view, a free cursor and readouts to a thousandth of an
    /// inch, none of which suit standing in a workshop looking through a
    /// crosshair - and a real saw is used the same way round, by walking up to
    /// it and then working at it rather than reaching for it in passing.
    /// </summary>
    public sealed class SawController : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        [Header("Reach")]
        [Tooltip("How near the player must be to a saw for it to offer itself.")]
        [SerializeField] private float reach = 1.6f;

        [Header("Camera")]
        [Tooltip("Nearest the camera may be pulled in to the bed, in metres.")]
        [SerializeField] private float minHeight = 0.35f;

        [SerializeField] private float maxHeight = 1.8f;

        [Tooltip("How far the view may be slid off the blade, in metres. Small " +
                 "enough that the machine is always in sight.")]
        [SerializeField] private float panLimit = 0.6f;

        [Header("Knob feel")]
        [Tooltip("Snap for part rotation with no modifier, in degrees.")]
        [SerializeField] private float rotationSnap = 90f;

        [Tooltip("Snap for part rotation while the snap modifier is held.")]
        [SerializeField] private float rotationFineSnap = 15f;

        [Tooltip("Snap for the blade with no modifier, in degrees.")]
        [SerializeField] private float bladeSnap = 15f;

        [Tooltip("Snap for the blade while the snap modifier is held.")]
        [SerializeField] private float bladeFineSnap = 5f;

        [Tooltip("Feed step with no modifier, in inches.")]
        [SerializeField] private float feedSnap = 0.25f;

        [Tooltip("Feed step while the snap modifier is held, in inches.")]
        [SerializeField] private float feedFineSnap = 0.125f;

        private IPointerInput pointer;
        private IActionInput actions;
        private ILookInput look;
        private InteractionLock interactionLock;
        private PartPlacementController placement;
        private VexDesigner.Player.FirstPersonController player;

        private SawStation saw;
        private Camera view;

        private Vector3 cameraOffset;
        private float height = 0.9f;

        private SawKnob grabbed;
        private float grabbedReference;
        private float grabbedStart;

        private Transform cameraParent;
        private Vector3 cameraLocalPosition;
        private Quaternion cameraLocalRotation;

        /// <summary>True while the saw is being worked at.</summary>
        public bool IsOpen => saw != null;

        /// <summary>The saw within reach and under the crosshair, if any.</summary>
        public SawStation Available { get; private set; }

        public SawStation Open => saw;

        private void Awake()
        {
            pointer = GetComponentInChildren<IPointerInput>();
            actions = GetComponentInChildren<IActionInput>();
            look = GetComponentInChildren<ILookInput>();

            interactionLock = GetComponent<InteractionLock>();
            placement = GetComponent<PartPlacementController>();
            player = GetComponent<VexDesigner.Player.FirstPersonController>();
        }

        private void Update()
        {
            if (pointer == null)
            {
                return;
            }

            if (saw != null)
            {
                UpdateOpen();
                return;
            }

            Available = FindAvailable();

            if (Available != null && actions != null && actions.UsePressed)
            {
                Enter(Available);
            }
        }

        // ------------------------------------------------------------------
        // Getting in and out
        // ------------------------------------------------------------------

        /// <summary>
        /// The saw the player is close enough to and looking at.
        ///
        /// Both conditions, because either alone is wrong: a prompt that
        /// appears whenever the machine is on screen is noise from across the
        /// room, and one that appears whenever the player is near it fires
        /// while they are working on something else beside it.
        /// </summary>
        private SawStation FindAvailable()
        {
            SawStation best = null;
            float bestAngle = 0.55f;

            Ray aim = pointer.AimRay;

            foreach (SawStation candidate in SawStation.All)
            {
                if (candidate == null)
                {
                    continue;
                }

                Vector3 toSaw = candidate.transform.position - aim.origin;

                if (toSaw.magnitude > reach)
                {
                    continue;
                }

                float facing = Vector3.Dot(aim.direction, toSaw.normalized);

                if (facing > bestAngle)
                {
                    bestAngle = facing;
                    best = candidate;
                }
            }

            return best;
        }

        public void Enter(SawStation station)
        {
            if (station == null || saw != null)
            {
                return;
            }

            saw = station;
            view = Camera.main;

            if (view == null)
            {
                saw = null;
                return;
            }

            cameraOffset = Vector3.zero;
            height = 0.9f;

            // Taken off the player's head for the duration.
            //
            // Not merely repositioned: the camera is the player's head, and the
            // controller writes its local position every frame to hold it at
            // eye height and to ease it up and down when crouching. That runs
            // whether or not movement is enabled, so an overhead position
            // lasted exactly until the end of the frame - the view turned to
            // look down and then stayed where the player's eyes were, which is
            // precisely what was reported.
            cameraParent = view.transform.parent;
            cameraLocalPosition = view.transform.localPosition;
            cameraLocalRotation = view.transform.localRotation;

            view.transform.SetParent(null, true);

            // The controller is switched off outright rather than told not to
            // move. Half-disabling it was what hid the problem above.
            if (player != null) { player.enabled = false; }
            if (placement != null) { placement.enabled = false; }
            if (interactionLock != null) { interactionLock.CameraOrbitLocked = true; }

            pointer.ShowPointer(true);

            if (saw.HasPart)
            {
                SawPreview.Apply(saw.Docked, saw);
            }

            PlaceCamera();
        }

        public void Exit()
        {
            if (saw == null)
            {
                return;
            }

            if (saw.HasPart)
            {
                SawPreview.Restore(saw.Docked);
            }

            saw = null;
            grabbed = null;

            pointer.ShowPointer(false);

            // The camera goes back to the head it belongs to, exactly where
            // it was, so the player carries on looking where they were looking.
            if (view != null)
            {
                view.transform.SetParent(cameraParent, false);
                view.transform.localPosition = cameraLocalPosition;
                view.transform.localRotation = cameraLocalRotation;
            }

            if (player != null) { player.enabled = true; }
            if (placement != null) { placement.enabled = true; }
            if (interactionLock != null) { interactionLock.CameraOrbitLocked = false; }
        }

        // ------------------------------------------------------------------
        // Working at it
        // ------------------------------------------------------------------

        private void UpdateOpen()
        {
            if (actions != null && actions.CancelPressed)
            {
                Exit();
                return;
            }

            UpdateCamera();
            UpdateKnob();

            SawPreview.Refresh(saw);
        }

        /// <summary>
        /// A top-down view of the bed, zoomable and slidable but never far
        /// enough to lose the machine.
        /// </summary>
        private void UpdateCamera()
        {
            float zoom = look?.ZoomDelta ?? 0f;

            if (!Mathf.Approximately(zoom, 0f))
            {
                height = Mathf.Clamp(height * (1f - (zoom * 0.08f)), minHeight, maxHeight);
            }

            // Right-drag slides the view, which leaves the left button free for
            // the knobs and the buttons.
            if (pointer.SecondaryHeld)
            {
                Vector2 drag = pointer.DragDelta * (height * 0.0015f);
                cameraOffset -= new Vector3(drag.x, 0f, drag.y);

                cameraOffset = Vector3.ClampMagnitude(cameraOffset, panLimit);
            }

            PlaceCamera();
        }

        private void PlaceCamera()
        {
            if (view == null || saw == null)
            {
                return;
            }

            Transform anchor = saw.Viewpoint != null ? saw.Viewpoint : saw.transform;

            Vector3 above = anchor.position + (Vector3.up * height) + cameraOffset;

            view.transform.position = above;

            // Looking straight down, with the fence at the top of the screen,
            // so left and right on screen are left and right on the machine.
            view.transform.rotation = Quaternion.LookRotation(Vector3.down, saw.transform.forward);
        }

        /// <summary>
        /// Turning a knob, or sliding the stock.
        ///
        /// The same absolute mechanic the hole dial and the gizmo rings use:
        /// the value follows where the pointer is, not how far it has moved.
        /// </summary>
        private void UpdateKnob()
        {
            if (!pointer.PrimaryHeld)
            {
                grabbed = null;
                return;
            }

            if (grabbed == null)
            {
                grabbed = KnobUnderPointer();

                if (grabbed == null)
                {
                    return;
                }

                grabbedReference = grabbed.ReadAngle(pointer.PointerRay);
                grabbedStart = grabbed.Value(saw);
                return;
            }

            float now = grabbed.ReadAngle(pointer.PointerRay);
            float turned = Mathf.DeltaAngle(grabbedReference, now);

            grabbed.Apply(saw, grabbedStart, turned, Coarse(grabbed), Fine(grabbed), Free);
        }

        private SawKnob KnobUnderPointer()
        {
            if (!Physics.Raycast(pointer.PointerRay, out RaycastHit hit, 8f))
            {
                return null;
            }

            return hit.collider.GetComponentInParent<SawKnob>();
        }

        private bool Free => actions != null && actions.PrecisionHeld;

        private bool Snapping => actions != null && actions.SnapHeld;

        /// <summary>
        /// The step with no modifier held: a quarter inch of feed, a quarter
        /// turn of the stock, fifteen degrees of blade.
        /// </summary>
        private float Coarse(SawKnob knob)
        {
            return knob.Kind switch
            {
                SawKnob.Control.Feed => feedSnap,
                SawKnob.Control.Blade => bladeSnap,
                _ => rotationSnap,
            };
        }

        /// <summary>The step with the snap modifier held.</summary>
        private float Fine(SawKnob knob)
        {
            return knob.Kind switch
            {
                SawKnob.Control.Feed => feedFineSnap,
                SawKnob.Control.Blade => bladeFineSnap,
                _ => rotationFineSnap,
            };
        }

        /// <summary>Which step applies right now.</summary>
        public float Step(SawKnob knob)
        {
            return Free ? 0f : (Snapping ? Fine(knob) : Coarse(knob));
        }
    }
}
