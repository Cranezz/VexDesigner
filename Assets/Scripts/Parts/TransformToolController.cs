namespace VexDesigner.Parts
{
    using UnityEngine;
    using VexDesigner.InputSources;
    using VexDesigner.UI;

    /// <summary>
    /// The precision alternative to grabbing: select a part, then drag an axis
    /// handle to move or turn it exactly.
    ///
    /// Grabbing is fast and physical but never exact - it is a hand, and hands
    /// wobble. Assembling a robot eventually needs "move this 0.5 inches along
    /// X and nothing else", which is what a gizmo is for. The two modes swap
    /// with G rather than coexisting, because a click cannot both grab a part
    /// and drag a handle.
    ///
    /// A selected assembly is held kinematic. Gizmo edits are meant to be
    /// exact, and gravity dragging the part off a freshly set position the
    /// moment it is released would defeat the point.
    /// </summary>
    public sealed class TransformToolController : MonoBehaviour
    {
        [Header("Gizmo")]
        [Tooltip("On-screen size of the gizmo, as a fraction of the distance " +
                 "to the camera. Constant apparent size means it stays usable " +
                 "whether the part is at arm's length or across the garage.")]
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

        // Drag state, captured on mouse-down so the whole drag is measured
        // against a fixed reference rather than the previous frame. Frame-to-
        // frame deltas accumulate drift over a long drag.
        private Vector3 dragAxis;
        private Vector3 dragOrigin;
        private float dragStartOffset;
        private Vector3 dragStartVector;

        private bool relativeAxes;

        public bool IsActive { get; private set; }

        /// <summary>True while an axis is being dragged. Blocks look.</summary>
        public bool IsDragging => dragging != null;

        private void Awake()
        {
            pointer = GetComponentInChildren<IPointerInput>();
            actions = GetComponentInChildren<IActionInput>();
            placement = GetComponent<PartPlacementController>();
            interactionLock = GetComponent<InteractionLock>()
                ?? gameObject.AddComponent<InteractionLock>();

            BuildGizmo();
            SetActive(false);
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

            UpdateGizmoVisibility();

            if (dragging != null)
            {
                ContinueDrag();
            }
            else
            {
                UpdateHoverAndClick();
            }

            interactionLock.CameraOrbitLocked = IsDragging;
        }

        // ------------------------------------------------------------------
        // Mode
        // ------------------------------------------------------------------

        private void SetActive(bool active)
        {
            IsActive = active;

            // Grab and transform cannot share the primary click, so exactly one
            // of them is live at a time.
            if (placement != null)
            {
                placement.enabled = !active;
            }

            if (!active)
            {
                EndDrag();
                Select(null);
            }

            if (gizmoRoot != null)
            {
                gizmoRoot.SetActive(active && selection != null);
            }

            MessageBanner.Info(active ? "Transform tool — G for grab" : "Grab mode — G for transform");
        }

        // ------------------------------------------------------------------
        // Selection
        // ------------------------------------------------------------------

        private void UpdateHoverAndClick()
        {
            TransformHandle handle = RaycastFor<TransformHandle>(out _);

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

            // Clicking a part selects its whole assembly; clicking nothing
            // clears the selection.
            var instance = RaycastFor<PartInstance>(out _);
            Select(instance?.Group);
        }

        private void Select(PartGroup group)
        {
            if (ReferenceEquals(group, selection))
            {
                return;
            }

            // Restore the old selection to physics before letting go of it.
            if (selection != null)
            {
                selection.SetGrabbed(false);
                SetKinematic(selection, selection.IsFrozen);
            }

            selection = group;

            if (selection != null)
            {
                selection.SetGrabbed(true);
                SetKinematic(selection, true);
            }

            if (gizmoRoot != null)
            {
                gizmoRoot.SetActive(IsActive && selection != null);
            }
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

        // ------------------------------------------------------------------
        // Gizmo placement
        // ------------------------------------------------------------------

        private void UpdateGizmoVisibility()
        {
            if (gizmoRoot == null || selection == null)
            {
                return;
            }

            // R swaps translation for rotation, held rather than toggled: it is
            // usually wanted for one adjustment, not for a whole session.
            bool rotating = actions.RotateModifierHeld;
            moveHandles.gameObject.SetActive(!rotating);
            rotateHandles.gameObject.SetActive(rotating);

            Vector3 centre = selection.GetCentre();
            gizmoRoot.transform.position = centre;

            // Global axes by default. Y switches to the part's own axes, which
            // is what you want once a part has been turned off-axis.
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

        private void BeginDrag(TransformHandle handle)
        {
            dragging = handle;
            dragAxis = handle.WorldAxis;
            dragOrigin = gizmoRoot.transform.position;

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
            // Click to start, click to finish - the same two-state pattern as
            // grabbing, and far kinder than holding a button down through a
            // long precise adjustment.
            if (selection == null || pointer.PrimaryPressedThisFrame)
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
            dragging = null;
            interactionLock.CameraOrbitLocked = false;
        }

        private float PrecisionFactor => actions.PrecisionHeld ? 0.2f : 1f;

        /// <summary>
        /// Distance along a line to the point nearest the aim ray. Standard
        /// closest-approach of two skew lines.
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

            // Near-parallel: the intersection is ill-conditioned and the handle
            // is nearly edge-on anyway, so refuse rather than jump.
            if (Mathf.Abs(denominator) < 1e-6f)
            {
                return 0f;
            }

            return ((b * e) - (c * d)) / denominator;
        }

        /// <summary>
        /// Vector from the pivot to where the aim ray crosses the plane through
        /// that pivot with the given normal.
        /// </summary>
        private static Vector3 ProjectOntoPlane(Ray ray, Vector3 pivot, Vector3 normal)
        {
            var plane = new Plane(normal, pivot);
            return plane.Raycast(ray, out float enter)
                ? ray.GetPoint(enter) - pivot
                : Vector3.zero;
        }

        private T RaycastFor<T>(out RaycastHit nearestHit) where T : class
        {
            nearestHit = default;

            int count = Physics.RaycastNonAlloc(
                pointer.AimRay, hits, aimDistance, ~0, QueryTriggerInteraction.Collide);

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

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            handleMaterial = new Material(shader) { name = "GizmoHandle" };
            handleMaterial.EnableKeyword("_EMISSION");
            handleMaterial.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // Red X, green Y, blue Z - the convention every 3D tool uses, and
            // deviating from it would be actively confusing.
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
                new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.8f, 0.03f), colour);

            AddPiece(root.transform, GizmoMeshes.Cone(),
                new Vector3(0f, 0.8f, 0f), new Vector3(0.11f, 0.22f, 0.11f), colour);

            // A single capsule covering the whole arm, rather than colliders on
            // each piece: the arrow is thin, and a fiddly hit target defeats
            // the purpose of a precision tool.
            var collider = root.AddComponent<CapsuleCollider>();
            collider.direction = 1;
            collider.height = 1.1f;
            collider.radius = 0.09f;
            collider.center = new Vector3(0f, 0.5f, 0f);
            collider.isTrigger = true;

            root.AddComponent<TransformHandle>()
                .Configure(TransformHandle.Kind.Move, axis, colour);
        }

        private void CreateRotateHandle(Vector3 axis, Color colour)
        {
            var root = new GameObject($"Rotate_{axis}");
            root.transform.SetParent(rotateHandles, false);

            // The torus is built in XZ, so its own axis is +Y.
            root.transform.rotation = Quaternion.FromToRotation(Vector3.up, axis);

            AddPiece(root.transform, GizmoMeshes.Torus(),
                Vector3.zero, Vector3.one * 0.75f, colour);

            var collider = root.AddComponent<MeshCollider>();
            collider.sharedMesh = GizmoMeshes.Torus();
            collider.convex = true;
            collider.isTrigger = true;

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
            block.SetColor(Shader.PropertyToID("_EmissionColor"), colour * 0.25f);
            renderer.SetPropertyBlock(block);
        }
    }
}
