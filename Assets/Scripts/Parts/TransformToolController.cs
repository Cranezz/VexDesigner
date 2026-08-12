namespace VexDesigner.Parts
{
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
        private float dragStartOffset;
        private Vector3 dragStartVector;

        private bool relativeAxes;

        public bool IsActive { get; private set; }

        public bool IsDragging => dragging != null;

        public bool RelativeAxes => relativeAxes;

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

            // The view is deliberately NOT locked during a drag.
            //
            // In first person the aim ray *is* the cursor: it comes from where
            // the head is pointing. Locking the view froze the ray, so the drag
            // recomputed the same position every frame and nothing moved - the
            // gizmo appeared completely dead while also trapping the camera.
            //
            // Grabbing locks the view for the opposite reason: there the mouse
            // is being used to rotate the part instead of to aim.
            interactionLock.CameraOrbitLocked = false;
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
            bool rotating = actions.RotateModifierHeld;
            moveHandles.gameObject.SetActive(!rotating);
            rotateHandles.gameObject.SetActive(rotating);

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
            TransformHandle handle = RaycastForHandle();

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
                BeginDrag(handle);
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

        private void BeginDrag(TransformHandle handle)
        {
            dragging = handle;
            dragAxis = handle.WorldAxis;
            dragOrigin = gizmoRoot.transform.position;

            // Deliberately positioning a part is a statement that it belongs
            // there, so it is pinned. Otherwise gravity would undo the
            // adjustment the moment the part was deselected, which is the
            // opposite of what a precision tool is for.
            selection?.SetFrozen(true);

            if (handle.HandleKind == TransformHandle.Kind.Move)
            {
                dragStartOffset = ProjectOntoAxis(pointer.AimRay, dragOrigin, dragAxis);
            }
            else
            {
                dragStartVector = ProjectOntoPlane(pointer.AimRay, dragOrigin, dragAxis);
            }
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
                float delta = (offset - dragStartOffset) * PrecisionFactor;

                selection.Translate(dragAxis * delta);
                dragStartOffset = offset;
                dragOrigin = gizmoRoot.transform.position;
            }
            else
            {
                Vector3 current = ProjectOntoPlane(pointer.AimRay, dragOrigin, dragAxis);
                if (current.sqrMagnitude < 1e-6f || dragStartVector.sqrMagnitude < 1e-6f)
                {
                    return;
                }

                float angle = Vector3.SignedAngle(dragStartVector, current, dragAxis)
                    * PrecisionFactor;

                selection.Rotate(Quaternion.AngleAxis(angle, dragAxis), dragOrigin);
                dragStartVector = current;
            }
        }

        private void EndDrag()
        {
            if (hovered != null)
            {
                hovered.SetHighlighted(false);
                hovered = null;
            }

            dragging = null;
            interactionLock.CameraOrbitLocked = false;
        }

        private float PrecisionFactor => actions.PrecisionHeld ? 0.2f : 1f;

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

        private TransformHandle RaycastForHandle()
        {
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

            CreateRotateHandle(Vector3.right, new Color(0.95f, 0.25f, 0.25f));
            CreateRotateHandle(Vector3.up, new Color(0.35f, 0.9f, 0.35f));
            CreateRotateHandle(Vector3.forward, new Color(0.3f, 0.5f, 1f));

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

        private void CreateRotateHandle(Vector3 axis, Color colour)
        {
            var root = new GameObject($"Rotate_{axis}");
            root.transform.SetParent(rotateHandles, false);
            root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);

            const float radius = 0.75f;
            AddPiece(root.transform, GizmoMeshes.Torus(),
                Vector3.zero, Vector3.one * radius, colour);

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

        private void AddPiece(
            Transform parent, Mesh mesh, Vector3 localPosition, Vector3 scale, Color colour)
        {
            var go = new GameObject("Piece");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = handleMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var block = new MaterialPropertyBlock();
            block.SetColor(Shader.PropertyToID("_BaseColor"), colour);
            renderer.SetPropertyBlock(block);
        }
    }
}
