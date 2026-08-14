namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using UnityEngine;
    using VexDesigner.InputSources;
    using VexDesigner.UI;

    /// <summary>
    /// The precision alternative to grabbing: select a placed part, then drag
    /// an axis handle to move or turn it exactly.
    ///
    /// Grabbing is fast and physical but never exact - it is a hand, and hands
    /// wobble. Assembly eventually needs "move this half an inch along X and
    /// nothing else", which is what a gizmo is for.
    ///
    /// The two coexist rather than replacing each other. Taking parts from the
    /// shelf works identically in both modes; the mode only changes what
    /// clicking an *already placed* part does - grab it, or select it.
    /// </summary>
    public sealed class TransformToolController : MonoBehaviour
    {
        [Header("Gizmo")]
        [Tooltip("On-screen size as a fraction of the distance to the camera. " +
                 "Constant apparent size keeps it usable whether the part is at " +
                 "arm's length or across the garage.")]
        [SerializeField] private float screenScale = 0.16f;

        [SerializeField] private float aimDistance = 12f;

        [Header("Snapping (hold Shift)")]
        [Tooltip("Movement increment in inches. Half an inch is the VEX hole " +
                 "pitch, so snapped parts land hole-to-hole.")]
        [SerializeField] private float moveSnapInches = 0.5f;

        [Tooltip("Rotation increment in degrees, matching the ring ticks.")]
        [SerializeField] private float rotationSnapDegrees = 15f;

        private IPointerInput pointer;
        private IActionInput actions;
        private PartPlacementController placement;
        private InteractionLock interactionLock;

        private readonly RaycastHit[] hits = new RaycastHit[24];

        private PartGroup selection;
        private GameObject gizmoRoot;
        private Transform moveHandles;
        private Transform rotateHandles;
        private Material handleMaterial;

        private TransformHandle hovered;
        private TransformHandle dragging;

        // Captured on mouse-down so the whole drag measures against a fixed
        // reference. Frame-to-frame deltas accumulate drift over a long drag.
        private Vector3 dragAxis;
        private Vector3 dragOrigin;
        private float lastAxisOffset;

        /// <summary>Where the assembly's centre was when the drag began.</summary>
        private Vector3 dragStartCentre;

        /// <summary>
        /// Unsnapped position along the drag axis. Snapping reads this rather
        /// than the part's actual position, which after a snap is always
        /// exactly on the grid and so can never accumulate enough movement to
        /// reach the next point.
        /// </summary>
        private float snapVirtualOffset;

        /// <summary>
        /// Screen direction that means "turn this ring forwards", derived from
        /// the point on the ring that was grabbed. Dragging along it rotates
        /// one way, against it the other - so grabbing the top of a ring and
        /// pulling right turns it clockwise, as the ring itself would.
        /// </summary>
        private Vector2 rotateScreenTangent;

        private float rotateAccumulated;

        /// <summary>
        /// Rotation actually applied, as opposed to the raw amount asked for.
        /// The two differ while snapping, and the sweep arc has to follow this
        /// one - an arc drawn from the raw total would sit between the ticks
        /// while the part sat on one.
        /// </summary>
        private float rotateApplied;
        private MeshRenderer arcRenderer;
        private Mesh arcMesh;

        /// <summary>
        /// Radial vector from the ring centre to the point that was grabbed.
        /// The sweep arc starts here, so it grows from under the cursor rather
        /// than from an arbitrary zero.
        /// </summary>
        private Vector3 rotateStartRadial;

        /// <summary>
        /// Each rotation ring with its own material, so their draw order can be
        /// set individually. They all share a centre, so Unity's usual
        /// distance sort cannot separate them - which is why the far ring
        /// sometimes drew over the near one.
        /// </summary>
        private readonly List<(Vector3 axis, Material material)> ringMaterials =
            new List<(Vector3, Material)>();

        private bool relativeAxes;

        public bool IsActive { get; private set; }

        public bool IsDragging => dragging != null;

        /// <summary>
        /// True while a rotation ring is being dragged. The view is locked and
        /// the crosshair hidden for the duration, because the mouse is turning
        /// the part rather than aiming at anything.
        /// </summary>
        public bool IsRotating =>
            dragging != null && dragging.HandleKind != TransformHandle.Kind.Move;

        public bool RelativeAxes => relativeAxes;

        /// <summary>Snap increment for movement, in inches. Set from settings.</summary>
        public void SetMoveSnapInches(float inches)
        {
            moveSnapInches = Mathf.Max(0.001f, inches);
        }

        /// <summary>Snap increment for rotation, in degrees. Set from settings.</summary>
        public void SetRotationSnapDegrees(float degrees)
        {
            rotationSnapDegrees = Mathf.Max(0.1f, degrees);
        }

        private void Awake()
        {
            pointer = GetComponentInChildren<IPointerInput>();
            actions = GetComponentInChildren<IActionInput>();
            placement = GetComponent<PartPlacementController>();
            interactionLock = GetComponent<InteractionLock>()
                ?? gameObject.AddComponent<InteractionLock>();

            BuildGizmo();
        }

        private void Update()
        {
            if (pointer == null || actions == null)
            {
                return;
            }

            if (actions.ModeTogglePressed)
            {
                SetActive(!IsActive);
            }

            if (!IsActive)
            {
                return;
            }

            if (actions.RelativeTogglePressed)
            {
                relativeAxes = !relativeAxes;
                MessageBanner.Info(relativeAxes ? "Axes: part-relative" : "Axes: global");
            }

            UpdateGizmo();

            if (dragging != null)
            {
                ContinueDrag();
            }
            else
            {
                UpdateHandleHover();
            }

            // While a handle is under the cursor or being dragged, the grab
            // system must not also act on the click - otherwise reaching for an
            // axis picks up the part instead.
            if (placement != null)
            {
                placement.SuppressInput = IsDragging || hovered != null;
            }

            // Locked only while turning a ring.
            //
            // Moving must leave the view free: in first person the aim ray *is*
            // the cursor, so a frozen view means a frozen ray and the drag
            // reports no movement at all. Rotation has the opposite need - it
            // reads the mouse directly, and a view that swings away mid-turn
            // takes the part out of sight.
            interactionLock.CameraOrbitLocked = IsRotating;

            // Walking away mid-rotation drags the whole reference frame out
            // from under the gesture, so the player is held still too.
            interactionLock.MovementLocked = IsRotating;
        }

        // ------------------------------------------------------------------
        // Mode
        // ------------------------------------------------------------------

        private void SetActive(bool active)
        {
            IsActive = active;

            if (!active)
            {
                EndDrag();
                Select(null);

                if (placement != null)
                {
                    placement.SuppressInput = false;
                }
            }

            UpdateGizmoVisible();
            MessageBanner.Info(active ? "Transform tool" : "Grab mode");
        }

        // ------------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------------

        /// <summary>
        /// Selects an assembly. Called by <see cref="PickupHandle"/> when the
        /// tool is active, so that clicking a placed part selects it instead of
        /// picking it up.
        /// </summary>
        public void Select(PartGroup group)
        {
            if (ReferenceEquals(group, selection))
            {
                return;
            }

            // Hand the previous selection back to physics before letting go.
            if (selection != null)
            {
                selection.SetGrabbed(false);
                SetKinematic(selection, selection.IsFrozen);
                selection.WakeNeighbours();
            }

            selection = group;

            if (selection != null)
            {
                selection.SetGrabbed(true);

                // Held still while selected: gizmo edits are meant to be exact,
                // and gravity pulling the part off a freshly set position would
                // defeat the point.
                SetKinematic(selection, true);
            }

            UpdateGizmoVisible();
        }

        private static void SetKinematic(PartGroup group, bool kinematic)
        {
            foreach (PartInstance part in group.Members)
            {
                var body = part == null ? null : part.GetComponent<Rigidbody>();
                if (body == null)
                {
                    continue;
                }

                body.isKinematic = kinematic;

                // Interpolation off while the gizmo owns the part. It smooths
                // the rendered transform between physics steps, which fights
                // direct positioning and makes precise placement drift and
                // stutter. Restored on deselect, where it is wanted again.
                body.interpolation = kinematic
                    ? RigidbodyInterpolation.None
                    : RigidbodyInterpolation.Interpolate;

                if (kinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }

        private void UpdateGizmoVisible()
        {
            if (gizmoRoot != null)
            {
                gizmoRoot.SetActive(IsActive && selection != null);
            }
        }

        // ------------------------------------------------------------------
        // Gizmo placement
        // ------------------------------------------------------------------

        private void UpdateGizmo()
        {
            if (gizmoRoot == null || selection == null || !gizmoRoot.activeSelf)
            {
                return;
            }

            // R swaps arrows for rings, held rather than toggled: rotation is
            // usually wanted for one adjustment, not a whole session.
            //
            // Latched while a ring is actually being turned. Letting go of R
            // mid-drag would otherwise swap the handles out from under the
            // gesture and hand the view back to the camera halfway through a
            // rotation.
            bool rotating = actions.RotateModifierHeld || IsRotating;
            moveHandles.gameObject.SetActive(!rotating);
            rotateHandles.gameObject.SetActive(rotating);

            SortRotationRings();

            Vector3 centre = selection.GetCentre();
            gizmoRoot.transform.position = centre;

            gizmoRoot.transform.rotation = relativeAxes && selection.Members.Count > 0
                ? selection.Members[0].transform.rotation
                : Quaternion.identity;

            Camera cam = Camera.main;
            if (cam != null)
            {
                float distance = Vector3.Distance(cam.transform.position, centre);
                gizmoRoot.transform.localScale = Vector3.one * (distance * screenScale);
            }
        }

        // ------------------------------------------------------------------
        // Dragging
        // ------------------------------------------------------------------

        private void UpdateHandleHover()
        {
            TransformHandle handle = RaycastForHandle(out RaycastHit hit);

            if (handle != hovered)
            {
                hovered?.SetHighlighted(false);
                hovered = handle;
                hovered?.SetHighlighted(true);
            }

            if (!pointer.PrimaryPressedThisFrame)
            {
                return;
            }

            if (handle != null)
            {
                BeginDrag(handle, hit.point);
                return;
            }

            // Clicking empty space clears the selection. Clicking a part is
            // handled by PickupHandle, which calls Select - so if the grab
            // system found nothing either, there was nothing there.
            if (placement != null && !placement.HasTarget)
            {
                Select(null);
            }
        }

        private void BeginDrag(TransformHandle handle, Vector3 grabPoint)
        {
            dragging = handle;
            dragAxis = handle.WorldAxis;

            // The axis line stays fixed for the whole drag. Recomputing it from
            // the moving gizmo made the reference chase the part by a frame,
            // which is what made moves lag behind the cursor.
            dragOrigin = gizmoRoot.transform.position;
            dragStartCentre = selection != null ? selection.GetCentre() : dragOrigin;

            // Deliberately positioning a part is a statement that it belongs
            // there, so it is pinned. Otherwise gravity would undo the
            // adjustment the moment the part was deselected, which is the
            // opposite of what a precision tool is for.
            selection?.SetFrozen(true);

            if (handle.HandleKind == TransformHandle.Kind.Move)
            {
                lastAxisOffset = ProjectOntoAxis(pointer.AimRay, dragOrigin, dragAxis);
                snapVirtualOffset = Vector3.Dot(dragStartCentre, dragAxis);
            }
            else
            {
                BeginRotateDrag(grabPoint);
            }
        }

        /// <summary>
        /// Works out which way the mouse must move to turn the ring forwards,
        /// from the point on the ring that was grabbed.
        ///
        /// The tangent at the grab point, projected to screen space. Grab the
        /// top of a ring and pull right and it turns the way the ring does -
        /// which is how a physical dial behaves, and is far more predictable
        /// than mapping raw horizontal movement to rotation regardless of where
        /// the ring was taken hold of.
        /// </summary>
        private void BeginRotateDrag(Vector3 grabPoint)
        {
            rotateAccumulated = 0f;
            rotateApplied = 0f;

            Camera cam = Camera.main;
            if (cam == null)
            {
                rotateScreenTangent = Vector2.right;
                return;
            }

            Vector3 radial = Vector3.ProjectOnPlane(grabPoint - dragOrigin, dragAxis);
            if (radial.sqrMagnitude < 1e-8f)
            {
                rotateScreenTangent = Vector2.right;
                rotateStartRadial = Vector3.zero;
                return;
            }

            rotateStartRadial = radial.normalized;
            Vector3 tangent = Vector3.Cross(dragAxis, radial).normalized;

            Vector3 screenA = cam.WorldToScreenPoint(grabPoint);
            Vector3 screenB = cam.WorldToScreenPoint(grabPoint + (tangent * 0.05f));
            Vector2 screenTangent = (Vector2)(screenB - screenA);

            rotateScreenTangent = screenTangent.sqrMagnitude > 1e-6f
                ? screenTangent.normalized
                : Vector2.right;
        }

        private void ContinueDrag()
        {
            // Press and hold, release to finish.
            //
            // Click-to-start/click-to-finish was the wrong call here: a gizmo
            // axis is a continuous drag, and every other 3D tool ends it on
            // button release. Requiring a second click left the cursor captured
            // with no obvious way out.
            if (selection == null || !pointer.PrimaryHeld)
            {
                EndDrag();
                return;
            }

            if (dragging.HandleKind == TransformHandle.Kind.Move)
            {
                float offset = ProjectOntoAxis(pointer.AimRay, dragOrigin, dragAxis);
                float delta = (offset - lastAxisOffset) * PrecisionFactor;
                lastAxisOffset = offset;

                if (actions.SnapHeld)
                {
                    delta = SnappedAxisDelta(delta);
                }
                else
                {
                    // Keep the snap accumulator in step with reality, so
                    // turning snapping on mid-drag does not jump the part by
                    // however far it moved while snapping was off.
                    snapVirtualOffset = Vector3.Dot(selection.GetCentre(), dragAxis) + delta;
                }

                selection.Translate(dragAxis * delta);

                // Trail back to where it started, labelled with how far it has
                // come, so a move can be made to a measurement rather than by
                // eye.
                MeasurementDisplay.Show(dragStartCentre, selection.GetCentre());
            }
            else
            {
                // Rotation reads the mouse directly rather than the aim ray,
                // because the view is locked while turning - a frozen view
                // means a frozen ray, which would report no movement at all.
                float along = Vector2.Dot(pointer.DragDelta, rotateScreenTangent);
                float angle = along * 0.35f * PrecisionFactor;

                if (actions.SnapHeld)
                {
                    // Snap the running total, then apply only the difference.
                    // Snapping each frame's increment instead would quantise a
                    // slow drag to zero and never move at all.
                    float before = Mathf.Round(rotateAccumulated / rotationSnapDegrees);
                    rotateAccumulated += angle;
                    float after = Mathf.Round(rotateAccumulated / rotationSnapDegrees);

                    angle = (after - before) * rotationSnapDegrees;
                }
                else
                {
                    rotateAccumulated += angle;
                }

                if (Mathf.Approximately(angle, 0f))
                {
                    DrawRotationArc();
                    return;
                }

                rotateApplied += angle;
                selection.Rotate(Quaternion.AngleAxis(angle, dragAxis), dragOrigin);

                DrawRotationArc();
            }
        }

        /// <summary>
        /// Draws the swept angle as a bright arc on the ring being turned, so
        /// how far the part has come is visible without counting.
        /// </summary>
        private void DrawRotationArc()
        {
            if (arcRenderer == null || dragging == null || rotateStartRadial == Vector3.zero)
            {
                return;
            }

            float scale = gizmoRoot.transform.localScale.x;
            float radius = scale * 0.75f;

            // Wrap at a full turn rather than clamping. Past 360 degrees a
            // filled ring says nothing more, so it empties and begins again -
            // which also reads as "you have gone all the way round".
            float sweep = rotateApplied % 360f;

            if (Mathf.Abs(sweep) < 1f)
            {
                arcRenderer.enabled = false;
                return;
            }

            // Only a little fatter than the ring, and sitting exactly on it, so
            // it reads as that ring lighting up rather than as a second coil
            // wrapped around the outside.
            BuildArcMesh(dragOrigin, dragAxis, rotateStartRadial,
                radius, scale * 0.020f, sweep);

            arcRenderer.enabled = true;
        }

        private void EndDrag()
        {
            if (hovered != null)
            {
                hovered.SetHighlighted(false);
                hovered = null;
            }

            dragging = null;
            rotateAccumulated = 0f;
            rotateApplied = 0f;
            interactionLock.MovementLocked = false;

            MeasurementDisplay.Hide();
            if (arcRenderer != null)
            {
                arcRenderer.enabled = false;
            }

            interactionLock.CameraOrbitLocked = false;
        }

        private float PrecisionFactor => actions.PrecisionHeld ? 0.2f : 1f;

        /// <summary>
        /// Snaps movement to the global grid, but only along the axis being
        /// dragged.
        ///
        /// This is the distinction that matters. Snapping the whole position
        /// would jump the part on all three axes the instant snapping is
        /// switched on - the Protobot behaviour where a part at
        /// (0, 0, 0.15") suddenly moves on axes nobody touched. Here only the
        /// dragged component is quantised: that part moves to (0, 0, 0.25")
        /// and its other two coordinates are left exactly as they were.
        ///
        /// The grid is still global rather than relative to where the drag
        /// started, so parts snapped independently line up with each other.
        /// </summary>
        private float SnappedAxisDelta(float rawDelta)
        {
            float step = moveSnapInches * 0.0254f;
            if (step <= 0f)
            {
                return rawDelta;
            }

            // Accumulate the *unsnapped* intent separately and snap that.
            //
            // Basing the snap on the part's current position could never move
            // it: after the first snap the part sits exactly on a grid point,
            // so "grid point plus this frame's small movement" always rounds
            // straight back to the same point. The drag was being thrown away
            // one frame at a time no matter how far the cursor travelled.
            snapVirtualOffset += rawDelta;

            float snapped = Mathf.Round(snapVirtualOffset / step) * step;
            float current = Vector3.Dot(selection.GetCentre(), dragAxis);

            return snapped - current;
        }

        /// <summary>
        /// Distance along a line to the point nearest the aim ray: the standard
        /// closest approach of two skew lines.
        /// </summary>
        private static float ProjectOntoAxis(Ray ray, Vector3 lineOrigin, Vector3 lineDir)
        {
            Vector3 w = lineOrigin - ray.origin;
            float a = Vector3.Dot(lineDir, lineDir);
            float b = Vector3.Dot(lineDir, ray.direction);
            float c = Vector3.Dot(ray.direction, ray.direction);
            float d = Vector3.Dot(lineDir, w);
            float e = Vector3.Dot(ray.direction, w);

            float denominator = (a * c) - (b * b);

            // Near-parallel: ill-conditioned, and the handle is nearly edge-on
            // anyway, so refuse rather than jump.
            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return 0f;
            }

            return ((b * e) - (c * d)) / denominator;
        }

        private static Vector3 ProjectOntoPlane(Ray ray, Vector3 pivot, Vector3 normal)
        {
            var plane = new Plane(normal, pivot);
            return plane.Raycast(ray, out float enter)
                ? ray.GetPoint(enter) - pivot
                : Vector3.zero;
        }

        private TransformHandle RaycastForHandle(out RaycastHit nearestHit)
        {
            nearestHit = default;

            if (gizmoRoot == null || !gizmoRoot.activeSelf)
            {
                return null;
            }

            int count = Physics.RaycastNonAlloc(
                pointer.AimRay, hits, aimDistance, ~0, QueryTriggerInteraction.Collide);

            TransformHandle best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                var candidate = hits[i].collider.GetComponentInParent<TransformHandle>();
                if (candidate == null || hits[i].distance >= bestDistance)
                {
                    continue;
                }

                // Only the visible set. The hidden arrows still have colliders,
                // and hitting an invisible handle is baffling.
                if (!candidate.gameObject.activeInHierarchy)
                {
                    continue;
                }

                best = candidate;
                bestDistance = hits[i].distance;
                nearestHit = hits[i];
            }

            return best;
        }

        // ------------------------------------------------------------------
        // Gizmo construction
        // ------------------------------------------------------------------

        private void BuildGizmo()
        {
            gizmoRoot = new GameObject("TransformGizmo");
            moveHandles = new GameObject("Move").transform;
            rotateHandles = new GameObject("Rotate").transform;
            moveHandles.SetParent(gizmoRoot.transform, false);
            rotateHandles.SetParent(gizmoRoot.transform, false);

            // Draws over everything. A gizmo buried inside the part it is
            // manipulating is useless, and that is the normal case: the handles
            // sit at the assembly's centre, which for a C-channel is inside
            // solid aluminium.
            Shader shader = Shader.Find("VexDesigner/GizmoOverlay")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");

            handleMaterial = new Material(shader) { name = "GizmoHandle" };

            // Red X, green Y, blue Z - the convention every 3D tool uses.
            CreateMoveHandle(Vector3.right, new Color(0.95f, 0.25f, 0.25f));
            CreateMoveHandle(Vector3.up, new Color(0.35f, 0.9f, 0.35f));
            CreateMoveHandle(Vector3.forward, new Color(0.3f, 0.5f, 1f));

            CreateMoveHub();

            CreateRotateHandle(Vector3.right, new Color(0.95f, 0.25f, 0.25f));
            CreateRotateHandle(Vector3.up, new Color(0.35f, 0.9f, 0.35f));
            CreateRotateHandle(Vector3.forward, new Color(0.3f, 0.5f, 1f));

            BuildRotationArc();

            gizmoRoot.SetActive(false);
        }

        private void CreateMoveHandle(Vector3 axis, Color colour)
        {
            var root = new GameObject($"Move_{axis}");
            root.transform.SetParent(moveHandles, false);
            root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);

            AddPiece(root.transform, GizmoMeshes.Shaft(),
                new Vector3(0f, 0.2f, 0f), new Vector3(0.03f, 0.6f, 0.03f), colour);

            AddPiece(root.transform, GizmoMeshes.Cone(),
                new Vector3(0f, 0.8f, 0f), new Vector3(0.11f, 0.22f, 0.11f), colour);

            // Starts clear of the centre. A capsule running through the origin
            // covers the part itself, so clicking the part hit an arrow instead
            // - which is what made selection feel like it was grabbing.
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.height = 0.95f;
            collider.radius = 0.085f;
            collider.center = new Vector3(0f, 0.58f, 0f);
            collider.isTrigger = true;

            root.AddComponent<TransformHandle>()
                .Configure(TransformHandle.Kind.Move, axis, colour);
        }

        /// <summary>
        /// The white ball the move arrows radiate from.
        ///
        /// Purely a marker: it shows exactly which point is being moved, which
        /// three arrows meeting in mid-air only imply. Deliberately given no
        /// collider - a handle here would sit right where the part is and
        /// swallow clicks meant for it, which is the same mistake the arrows
        /// themselves once made by running through the origin.
        /// </summary>
        private void CreateMoveHub()
        {
            var hub = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            hub.name = "Move_Hub";
            hub.transform.SetParent(moveHandles, false);
            hub.transform.localScale = Vector3.one * 0.12f;
            Object.Destroy(hub.GetComponent<Collider>());

            var material = new Material(handleMaterial) { name = "GizmoHub" };
            material.SetColor("_BaseColor", new Color(0.96f, 0.96f, 0.97f));

            var renderer = hub.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void CreateRotateHandle(Vector3 axis, Color colour)
        {
            var root = new GameObject($"Rotate_{axis}");
            root.transform.SetParent(rotateHandles, false);
            root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);

            const float radius = 0.75f;

            // Its own material, so this ring's draw order can be set apart
            // from the other two. See SortRotationRings.
            var ringMaterial = new Material(handleMaterial) { name = $"Ring_{axis}" };
            ringMaterials.Add((axis, ringMaterial));

            AddPiece(root.transform, GizmoMeshes.Torus(),
                Vector3.zero, Vector3.one * radius, colour, ringMaterial);

            // Graduations, in the ring's own colour so they read as part of it.
            // Black marks looked like damage to the ring rather than like
            // markings on it.
            var tickMaterial = new Material(ringMaterial) { name = $"Ticks_{axis}" };
            tickMaterial.SetColor("_BaseColor", colour * 1.25f);

            AddPiece(root.transform, GizmoMeshes.RingTicks(),
                Vector3.zero, Vector3.one * radius, colour, tickMaterial);

            // Boxes around the ring, not a convex mesh collider.
            //
            // The convex hull of a torus is a solid disc, so a convex collider
            // would cover the whole centre - the part included - and swallow
            // every click meant for the part underneath.
            const int segments = 12;
            for (int i = 0; i < segments; i++)
            {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                var segment = new GameObject($"Seg_{i}");
                segment.transform.SetParent(root.transform, false);
                segment.transform.localPosition =
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                segment.transform.localRotation =
                    Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);

                var box = segment.AddComponent<BoxCollider>();
                box.size = new Vector3(0.09f, 0.09f, (radius * 2f * Mathf.PI / segments) * 1.1f);
                box.isTrigger = true;
            }

            root.AddComponent<TransformHandle>()
                .Configure(TransformHandle.Kind.Rotate, axis, colour);
        }

        private void BuildRotationArc()
        {
            var go = new GameObject("RotationArc");

            // A generated tube rather than a LineRenderer. A LineRenderer is a
            // camera-facing ribbon, so the arc collapsed to a flat sliver as
            // soon as the ring was viewed edge-on - exactly when it was needed
            // to judge how far a rotation had gone.
            arcMesh = new Mesh { name = "RotationArc" };
            arcMesh.MarkDynamic();

            go.AddComponent<MeshFilter>().sharedMesh = arcMesh;

            arcRenderer = go.AddComponent<MeshRenderer>();
            arcRenderer.sharedMaterial = new Material(handleMaterial) { name = "ArcSweep" };
            arcRenderer.sharedMaterial.SetColor(
                Shader.PropertyToID("_BaseColor"), new Color(1f, 0.86f, 0.25f));

            // Above every ring, so the sweep is never hidden by the ring it is
            // drawn on.
            arcRenderer.sharedMaterial.renderQueue = 4010;
            arcRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arcRenderer.receiveShadows = false;
            arcRenderer.enabled = false;
        }

        /// <summary>
        /// Builds a torus segment sweeping <paramref name="sweepDegrees"/> from
        /// <paramref name="startRadial"/>, in world space.
        /// </summary>
        private void BuildArcMesh(
            Vector3 centre, Vector3 axis, Vector3 startRadial,
            float radius, float tubeRadius, float sweepDegrees)
        {
            // Finer than before on both axes. Six sides and three-degree steps
            // gave a visibly faceted, ribbed tube that looked like a spring
            // rather than a smooth highlight.
            int segments = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(sweepDegrees) / 1.5f), 3, 260);
            const int sides = 10;

            var vertices = new Vector3[(segments + 1) * sides];
            var triangles = new int[segments * sides * 6];

            Vector3 radialDir = startRadial.normalized;

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Quaternion turn = Quaternion.AngleAxis(sweepDegrees * t, axis);

                Vector3 outward = turn * radialDir;
                Vector3 ringCentre = centre + (outward * radius);

                for (int j = 0; j < sides; j++)
                {
                    float phi = (j / (float)sides) * Mathf.PI * 2f;
                    Vector3 offset =
                        (outward * (Mathf.Cos(phi) * tubeRadius)) +
                        (axis * (Mathf.Sin(phi) * tubeRadius));

                    vertices[(i * sides) + j] = ringCentre + offset;
                }
            }

            int index = 0;
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < sides; j++)
                {
                    int next = (j + 1) % sides;
                    int a = (i * sides) + j;
                    int b = (i * sides) + next;
                    int c = ((i + 1) * sides) + j;
                    int d = ((i + 1) * sides) + next;

                    triangles[index++] = a;
                    triangles[index++] = c;
                    triangles[index++] = b;
                    triangles[index++] = b;
                    triangles[index++] = c;
                    triangles[index++] = d;
                }
            }

            arcMesh.Clear();
            arcMesh.vertices = vertices;
            arcMesh.triangles = triangles;
            arcMesh.RecalculateNormals();
            arcMesh.RecalculateBounds();
        }

        /// <summary>
        /// Puts the rings in draw order, nearest last.
        ///
        /// All three share a centre, so Unity's per-object distance sort sees
        /// them as equidistant and picks an arbitrary order - which is why the
        /// far ring sometimes covered the near one. Sorting by each ring's
        /// closest point instead, and driving the render queue from that, makes
        /// the ordering match what the user sees.
        /// </summary>
        private void SortRotationRings()
        {
            Camera cam = Camera.main;
            if (cam == null || ringMaterials.Count == 0)
            {
                return;
            }

            Vector3 centre = gizmoRoot.transform.position;
            float radius = gizmoRoot.transform.localScale.x * 0.75f;
            Vector3 eye = cam.transform.position;

            // Rank by how close each ring gets to the eye. For a ring, that is
            // the point in its plane directly toward the camera.
            var ranked = new List<(float distance, Material material)>(ringMaterials.Count);

            foreach ((Vector3 axis, Material material) in ringMaterials)
            {
                Vector3 worldAxis = gizmoRoot.transform.TransformDirection(axis).normalized;
                Vector3 toEye = Vector3.ProjectOnPlane(eye - centre, worldAxis);

                Vector3 closest = toEye.sqrMagnitude > 1e-8f
                    ? centre + (toEye.normalized * radius)
                    : centre;

                ranked.Add((Vector3.Distance(eye, closest), material));
            }

            // Farthest first, so the nearest is drawn last and ends up on top.
            ranked.Sort((a, b) => b.distance.CompareTo(a.distance));

            for (int i = 0; i < ranked.Count; i++)
            {
                ranked[i].material.renderQueue = 4000 + i;
            }
        }

        private void AddPiece(
            Transform parent, Mesh mesh, Vector3 localPosition, Vector3 scale, Color colour,
            Material material = null)
        {
            var go = new GameObject("Piece");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : handleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var block = new MaterialPropertyBlock();
            block.SetColor(Shader.PropertyToID("_BaseColor"), colour);
            renderer.SetPropertyBlock(block);
        }
    }
}
