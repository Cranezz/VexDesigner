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

        [Header("Steps are set on SawHandles, next to the things they move.")]






        private IPointerInput pointer;
        private IActionInput actions;
        private ILookInput look;
        private InteractionLock interactionLock;
        private PartPlacementController placement;
        private VexDesigner.Player.FirstPersonController player;

        private SawStation saw;
        private Camera view;

        private Vector3 cameraOffset;

        /// <summary>How far the camera sits from the middle of the bed.</summary>
        private float distance = 0.9f;

        /// <summary>Degrees above horizontal. Ninety is straight down.</summary>
        private float pitch = 89f;

        /// <summary>Degrees around the machine, measured from its own forward.</summary>
        private float yaw;

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

            // The view the machine is meant to be read from: above and in
            // front, looking down the bed at an angle. Straight down flattens
            // the stock to a line and hides which way up it is, which is half
            // of what the setup is about.
            distance = 1.15f;
            pitch = 38f;
            yaw = 0f;

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

            handles?.Release();

            saw = null;
            handles = null;

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

            if (actions != null && actions.ConfirmPressed)
            {
                TakeCut();
            }

            UpdateCamera();
            UpdateHandles();

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
                distance = Mathf.Clamp(distance * (1f - (zoom * 0.08f)), minHeight, maxHeight);
            }

            if (pointer.SecondaryHeld)
            {
                Vector2 drag = pointer.DragDelta;

                if (actions != null && actions.SnapHeld)
                {
                    // Panning is done in the camera's own axes, not the world's.
                    // Sliding the view along world X while looking down a
                    // machine that is turned ninety degrees moved the picture
                    // up and down when the mouse went left and right, which is
                    // exactly as confusing as it sounds.
                    Vector3 slide =
                        (view.transform.right * -drag.x) +
                        (view.transform.up * -drag.y);

                    cameraOffset = Vector3.ClampMagnitude(
                        cameraOffset + (slide * (distance * 0.0015f)), panLimit);
                }
                else
                {
                    // Orbit. A saw is a three-dimensional object and a cut is
                    // judged by looking along the blade as much as down at it,
                    // so the view swings round the machine rather than only
                    // hanging over it.
                    yaw += drag.x * 0.25f;
                    pitch = Mathf.Clamp(pitch - (drag.y * 0.25f), 8f, 89.5f);
                }
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
            Vector3 centre = anchor.position + cameraOffset;

            // Measured from the machine's own forward, so the view starts
            // square to the fence however the saw is stood in the room.
            Quaternion swing = Quaternion.AngleAxis(yaw, Vector3.up) * saw.transform.rotation;
            Vector3 back = swing * Vector3.forward;

            Vector3 direction =
                (Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)) +
                (back * Mathf.Cos(pitch * Mathf.Deg2Rad));

            view.transform.position = centre + (direction.normalized * distance);
            view.transform.rotation = Quaternion.LookRotation(
                (centre - view.transform.position).normalized, Vector3.up);
        }

        /// <summary>
        /// Dragging the stock, the ball, or the blade.
        ///
        /// Everything is grabbed where it is, so there is one gesture for the
        /// whole machine: press on a thing, move it, let go. Which thing was
        /// grabbed decides what the movement means.
        /// </summary>
        private void UpdateHandles()
        {
            if (handles == null)
            {
                handles = saw.GetComponentInChildren<SawHandles>();

                if (handles == null)
                {
                    return;
                }
            }

            Ray ray = pointer.PointerRay;

            if (!pointer.PrimaryHeld)
            {
                handles.Release();
                handles.Probe(ray);
            }
            else if (handles.Held == SawHandles.Grip.None)
            {
                // Not started over the panel: a click on a field is for the
                // field, not for the machine behind it.
                if (!pointer.IsOverInterface)
                {
                    handles.Begin(ray);
                }
            }
            else
            {
                handles.Drag(ray, Snapping, Free);
            }
        }

        /// <summary>
        /// Which dimension the machine is currently pointing at, so the panel
        /// can light the matching field.
        /// </summary>
        public SawAnnotations.Item HoveredItem
        {
            get
            {
                if (handles == null)
                {
                    return SawAnnotations.Item.None;
                }

                SawHandles.Grip grip = handles.Held != SawHandles.Grip.None
                    ? handles.Held
                    : handles.Hovered;

                return grip switch
                {
                    SawHandles.Grip.Slide => SawAnnotations.Item.NearFace,
                    SawHandles.Grip.Blade => SawAnnotations.Item.BladeAngle,
                    SawHandles.Grip.RotateX => SawAnnotations.Item.RotateX,
                    SawHandles.Grip.RotateY => SawAnnotations.Item.RotateY,
                    SawHandles.Grip.RotateZ => SawAnnotations.Item.RotateZ,
                    _ => SawAnnotations.Item.None,
                };
            }
        }

        /// <summary>Takes the cut, for the button and the key alike.</summary>
        public void TakeCut()
        {
            if (saw != null && saw.Cut())
            {
                MessageBanner.Info("Cut");
            }
        }

        private SawHandles handles;

        private bool Free => actions != null && actions.PrecisionHeld;

        private bool Snapping => actions != null && actions.SnapHeld;



    }
}
