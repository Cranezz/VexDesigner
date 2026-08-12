namespace VexDesigner.Parts
{
    using System.Collections.Generic;
    using TMPro;
    using UnityEngine;
    using VexDesigner.UI;

    /// <summary>
    /// A wall button that deletes parts, and asks first.
    ///
    /// Confirmation is on the button face rather than in a dialog: the button
    /// is a physical object in the workshop, and a floating window would be a
    /// different kind of thing appearing from nowhere. It also survives the
    /// move to VR without redesign.
    ///
    /// The delay before the confirm can be taken is deliberate friction. A
    /// two-click confirm with no wait is only a formality - the second click
    /// lands before anyone has read the first. Making the button unclickable
    /// while the grey bar drains forces the question to be seen, and the
    /// preview alongside it shows exactly what is at stake.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ConfirmButton : MonoBehaviour, IWorkshopInteractable
    {
        public enum Target
        {
            /// <summary>Only parts that have ended up on the floor.</summary>
            FloorParts,

            /// <summary>Every part in the workshop.</summary>
            AllParts,
        }

        private enum State
        {
            Idle,
            Waiting,
            Armed,
            Done,
        }

        [SerializeField] private Target target = Target.FloorParts;
        [SerializeField] private string idleLabel = "CLEAR FLOOR";
        [SerializeField] private string confirmLabel = "Delete parts on the floor?";
        [SerializeField] private string doneLabel = "DONE";

        [Tooltip("Seconds the button stays unclickable while the question is " +
                 "readable. The grey bar drains across this time.")]
        [SerializeField] private float confirmDelay = 5f;

        [Tooltip("Seconds the confirm stays armed before giving up.")]
        [SerializeField] private float armedTimeout = 8f;

        [Tooltip("Seconds the result is shown before returning to normal.")]
        [SerializeField] private float doneTime = 2f;

        [SerializeField] private TextMeshPro label;
        [SerializeField] private Transform greyBar;

        [Tooltip("Height below which a part counts as being on the floor, in " +
                 "inches. Comfortably under the 36 in bench.")]
        [SerializeField] private float floorThresholdIn = 10f;

        private const float InchesToMetres = 0.0254f;

        /// <summary>
        /// The button currently mid-confirmation, if any. Only one at a time -
        /// two buttons both asking a destructive question is a good way to
        /// answer the wrong one.
        /// </summary>
        private static ConfirmButton active;

        private Highlightable highlight;
        private State state = State.Idle;
        private float stateTimer;
        private bool interactable = true;

        public bool Interactable
        {
            get => interactable && (active == null || active == this);
            set
            {
                interactable = value;
                if (highlight != null)
                {
                    highlight.Interactable = Interactable;
                    if (!Interactable)
                    {
                        highlight.SetHighlighted(false);
                    }
                }
            }
        }

        public void Configure(Target buttonTarget, string idle, string confirm)
        {
            target = buttonTarget;
            idleLabel = idle;
            confirmLabel = confirm;
        }

        public void Bind(TextMeshPro faceLabel, Transform bar)
        {
            label = faceLabel;
            greyBar = bar;
        }

        private void Awake()
        {
            highlight = GetComponent<Highlightable>();
            SetState(State.Idle);
        }

        private void Update()
        {
            if (state == State.Idle)
            {
                return;
            }

            stateTimer += Time.deltaTime;

            switch (state)
            {
                case State.Waiting:
                    UpdateGreyBar(1f - Mathf.Clamp01(stateTimer / confirmDelay));
                    RefreshPreview();

                    if (stateTimer >= confirmDelay)
                    {
                        SetState(State.Armed);
                    }
                    break;

                case State.Armed:
                    RefreshPreview();

                    // Give up rather than staying armed forever; an armed
                    // delete-everything button is a trap left lying around.
                    if (stateTimer >= armedTimeout)
                    {
                        SetState(State.Idle);
                    }
                    break;

                case State.Done:
                    if (stateTimer >= doneTime)
                    {
                        SetState(State.Idle);
                    }
                    break;
            }
        }

        public void SetHovered(bool hovered)
        {
            if (highlight != null)
            {
                highlight.SetHighlighted(hovered && Interactable);
            }
        }

        public void OnPrimaryClick(PartPlacementController controller)
        {
            switch (state)
            {
                case State.Idle:
                    if (active != null && active != this)
                    {
                        return;
                    }

                    active = this;
                    SetState(State.Waiting);
                    break;

                case State.Waiting:
                    // Deliberately ignored. The bar has not drained, so the
                    // question has not been up long enough to have been read.
                    break;

                case State.Armed:
                    int removed = Delete();
                    label?.SetText(removed == 0
                        ? "NOTHING TO DELETE"
                        : $"DELETED {removed}");
                    SetState(State.Done);
                    break;

                case State.Done:
                    break;
            }
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        private void SetState(State next)
        {
            state = next;
            stateTimer = 0f;

            switch (next)
            {
                case State.Idle:
                    label?.SetText(idleLabel);
                    UpdateGreyBar(0f);
                    DeletionPreview.Hide();
                    if (active == this)
                    {
                        active = null;
                    }
                    break;

                case State.Waiting:
                    label?.SetText(confirmLabel);
                    UpdateGreyBar(1f);
                    RefreshPreview();
                    break;

                case State.Armed:
                    label?.SetText($"{confirmLabel}\n<size=60%>CLICK TO CONFIRM</size>");
                    UpdateGreyBar(0f);
                    break;

                case State.Done:
                    UpdateGreyBar(0f);
                    DeletionPreview.Hide();
                    if (active == this)
                    {
                        active = null;
                    }
                    break;
            }
        }

        /// <summary>
        /// Drains the grey overlay from full to empty. The bar shrinks from one
        /// edge rather than fading, so remaining time is readable at a glance
        /// rather than having to be judged from a brightness.
        /// </summary>
        private void UpdateGreyBar(float fill)
        {
            if (greyBar == null)
            {
                return;
            }

            greyBar.gameObject.SetActive(fill > 0.001f);

            Vector3 scale = greyBar.localScale;
            scale.x = Mathf.Clamp01(fill);
            greyBar.localScale = scale;

            // Quad pivots are central, so shrinking alone would close in from
            // both sides. Offsetting by half the lost width pins the left edge.
            Vector3 position = greyBar.localPosition;
            position.x = -(1f - Mathf.Clamp01(fill)) * 0.5f;
            greyBar.localPosition = position;
        }

        private void RefreshPreview()
        {
            List<PartInstance> doomed = Collect();
            DeletionPreview.Show(BoundsOf(doomed), doomed.Count > 0);
        }

        // ------------------------------------------------------------------
        // Deletion
        // ------------------------------------------------------------------

        private List<PartInstance> Collect()
        {
            float threshold = floorThresholdIn * InchesToMetres;
            var found = new List<PartInstance>();

            foreach (PartInstance part in
                     FindObjectsByType<PartInstance>(FindObjectsSortMode.None))
            {
                if (part == null)
                {
                    continue;
                }

                // Pinned parts are exempt from both buttons. Freezing is an
                // explicit statement that a part is where it is wanted.
                if (part.IsFrozen)
                {
                    continue;
                }

                if (target == Target.AllParts)
                {
                    found.Add(part);
                    continue;
                }

                var renderer = part.GetComponentInChildren<Renderer>();

                // Measured from the part's lowest point, not its origin, which
                // for an imported CAD mesh can sit anywhere.
                if (renderer != null && renderer.bounds.min.y <= threshold)
                {
                    found.Add(part);
                }
            }

            return found;
        }

        private static Bounds BoundsOf(List<PartInstance> parts)
        {
            bool any = false;
            Bounds bounds = default;

            foreach (PartInstance part in parts)
            {
                var renderer = part.GetComponentInChildren<Renderer>();
                if (renderer == null)
                {
                    continue;
                }

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private int Delete()
        {
            List<PartInstance> doomed = Collect();

            foreach (PartInstance part in doomed)
            {
                Destroy(part.gameObject);
            }

            return doomed.Count;
        }
    }
}
