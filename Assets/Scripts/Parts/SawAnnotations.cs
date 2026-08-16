namespace VexDesigner.Parts
{
    using UnityEngine;

    /// <summary>
    /// The dimensions drawn on the machine: where the cut meets each face of
    /// the stock, and what angle the blade is swung to.
    ///
    /// A technical drawing rather than a set of gauges. An angled cut meets the
    /// two faces of the stock at two different places, and confusing the two is
    /// the commonest way to cut a part to the wrong length - so both are drawn
    /// where they actually are, on the metal, with the number beside the line
    /// it belongs to. Somebody reading this off the screen has everything they
    /// need to make the same part on a real saw.
    ///
    /// Labels are billboarded and sized in screen space, so they stay legible
    /// and stay the same size wherever the view is moved to.
    /// </summary>
    public sealed class SawAnnotations : MonoBehaviour
    {
        private const float InchesToMetres = 0.0254f;

        /// <summary>Which dimension a highlight refers to.</summary>
        public enum Item
        {
            None,
            NearFace,
            FarFace,
            BladeAngle,
            RotateX,
            RotateY,
            RotateZ,
        }

        [SerializeField] private SawStation saw;

        [Header("Colours")]
        [Tooltip("The near face - the short side of a mitre.")]
        [SerializeField] private Color nearColour = new Color(0.25f, 0.85f, 1f);

        [Tooltip("The far face against the fence - the long side.")]
        [SerializeField] private Color farColour = new Color(1f, 0.35f, 0.85f);

        [SerializeField] private Color angleColour = new Color(1f, 0.85f, 0.2f);

        [Tooltip("How wide a dimension line is drawn, in metres.")]
        [SerializeField] private float lineWidth = 0.0022f;

        [Tooltip("Extra width added while an item is highlighted. Deliberately " +
                 "small - the glow should read as attention, not as a different " +
                 "drawing.")]
        [SerializeField] private float highlightWidth = 0.0016f;

        private Line nearLine;
        private Line farLine;
        private Line angleLine;

        private Label nearLabel;
        private Label farLabel;
        private Label angleLabel;

        private Item highlighted = Item.None;

        /// <summary>Marks one dimension as the one being pointed at.</summary>
        public void Highlight(Item item)
        {
            highlighted = item;
        }

        private void LateUpdate()
        {
            if (saw == null)
            {
                saw = GetComponentInParent<SawStation>();
            }

            bool show = saw != null && saw.HasPart;

            SetShown(show);

            if (!show || !saw.CutEndpoints(out Vector3 near, out Vector3 far, out Vector3 origin))
            {
                return;
            }

            Camera camera = Camera.main;

            // The two lengths, each drawn along the face it measures.
            Vector3 alongNear = Project(origin, near, saw.transform.right);
            Vector3 alongFar = Project(origin, far, saw.transform.right);

            Vector3 nearStart = new Vector3(alongNear.x, near.y, near.z);
            Vector3 farStart = new Vector3(alongFar.x, far.y, far.z);

            // Held clear of the metal so the line is readable against it.
            Vector3 nearLift = saw.transform.up * (0.35f * InchesToMetres);
            Vector3 farLift = saw.transform.up * (1.1f * InchesToMetres);

            Draw(nearLine, ProjectOnto(origin, near, saw.transform.right) + nearLift,
                near + nearLift, nearColour, Item.NearFace);

            Draw(farLine, ProjectOnto(origin, far, saw.transform.right) + farLift,
                far + farLift, farColour, Item.FarFace);

            // The blade, as a line on the bed from the fence forward.
            Vector3 blade = saw.BladePoint;
            Vector3 bladeDirection =
                Quaternion.AngleAxis(saw.BladeAngle, saw.transform.up) * -saw.transform.forward;

            Draw(angleLine, blade, blade + (bladeDirection * (7f * InchesToMetres)),
                angleColour, Item.BladeAngle);

            Place(nearLabel, Midpoint(ProjectOnto(origin, near, saw.transform.right), near) +
                    nearLift, $"{saw.NearFaceInches:0.000} in", nearColour,
                highlighted == Item.NearFace, camera);

            Place(farLabel, Midpoint(ProjectOnto(origin, far, saw.transform.right), far) +
                    farLift, $"{saw.FarFaceInches:0.000} in", farColour,
                highlighted == Item.FarFace, camera);

            Place(angleLabel, blade + (bladeDirection * (7.8f * InchesToMetres)),
                $"{saw.BladeAngle:0.00}°", angleColour,
                highlighted == Item.BladeAngle, camera);
        }

        private static Vector3 Midpoint(Vector3 a, Vector3 b) => (a + b) * 0.5f;

        /// <summary>
        /// The point level with <paramref name="origin"/> but on the same face
        /// as <paramref name="at"/>, so a length is drawn along the face it
        /// measures rather than diagonally across the part.
        /// </summary>
        private static Vector3 ProjectOnto(Vector3 origin, Vector3 at, Vector3 along)
        {
            Vector3 offset = origin - at;
            return at + (along * Vector3.Dot(offset, along));
        }

        private static Vector3 Project(Vector3 origin, Vector3 at, Vector3 along)
        {
            return ProjectOnto(origin, at, along);
        }

        // ------------------------------------------------------------------
        // Drawing
        // ------------------------------------------------------------------

        private void Draw(Line line, Vector3 from, Vector3 to, Color colour, Item item)
        {
            if (line == null)
            {
                return;
            }

            bool lit = highlighted == item;

            // The glow is the same line, a little wider and a lot whiter. A
            // separate outline object would double the geometry to say
            // something a colour already says.
            line.Set(from, to,
                lit ? Color.Lerp(colour, Color.white, 0.75f) : colour,
                lineWidth + (lit ? highlightWidth : 0f));
        }

        private static void Place(
            Label label, Vector3 position, string text, Color colour, bool lit, Camera camera)
        {
            if (label == null)
            {
                return;
            }

            label.Set(position, text, lit ? Color.white : colour, lit, camera);
        }

        private void SetShown(bool shown)
        {
            EnsureBuilt();

            nearLine.SetShown(shown);
            farLine.SetShown(shown);
            angleLine.SetShown(shown);

            nearLabel.SetShown(shown);
            farLabel.SetShown(shown);
            angleLabel.SetShown(shown);
        }

        private void EnsureBuilt()
        {
            if (nearLine != null)
            {
                return;
            }

            nearLine = Line.Create(transform, "NearFaceLine");
            farLine = Line.Create(transform, "FarFaceLine");
            angleLine = Line.Create(transform, "BladeAngleLine");

            nearLabel = Label.Create(transform, "NearFaceLabel");
            farLabel = Label.Create(transform, "FarFaceLabel");
            angleLabel = Label.Create(transform, "BladeAngleLabel");
        }

        // ------------------------------------------------------------------
        // Pieces
        // ------------------------------------------------------------------

        /// <summary>A dimension line with an arrowhead at each end.</summary>
        private sealed class Line : MonoBehaviour
        {
            private static readonly int ColourId = Shader.PropertyToID("_BaseColor");

            private Transform shaft;
            private Transform headA;
            private Transform headB;
            private Material material;

            public static Line Create(Transform parent, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);

                var line = go.AddComponent<Line>();
                line.Build();
                return line;
            }

            private void Build()
            {
                Shader shader = Shader.Find("VexDesigner/GizmoOverlay")
                    ?? Shader.Find("Universal Render Pipeline/Unlit");

                material = new Material(shader) { name = name };

                shaft = Piece(PrimitiveType.Cube);
                headA = Piece(PrimitiveType.Cube);
                headB = Piece(PrimitiveType.Cube);
            }

            private Transform Piece(PrimitiveType type)
            {
                GameObject go = GameObject.CreatePrimitive(type);
                go.transform.SetParent(transform, false);
                DestroyImmediate(go.GetComponent<Collider>());

                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                return go.transform;
            }

            public void Set(Vector3 from, Vector3 to, Color colour, float width)
            {
                material.SetColor(ColourId, colour);

                Vector3 delta = to - from;
                float length = delta.magnitude;

                if (length < 1e-5f)
                {
                    transform.localScale = Vector3.zero;
                    return;
                }

                transform.localScale = Vector3.one;

                Vector3 direction = delta / length;
                Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

                shaft.SetPositionAndRotation((from + to) * 0.5f, rotation);
                shaft.localScale = new Vector3(width, width, length);

                // Arrowheads: short fat stubs, which read as arrows at this
                // size and cost nothing to build.
                float head = Mathf.Min(width * 4f, length * 0.2f);

                headA.SetPositionAndRotation(from + (direction * head * 0.5f), rotation);
                headA.localScale = new Vector3(width * 3f, width * 3f, head);

                headB.SetPositionAndRotation(to - (direction * head * 0.5f), rotation);
                headB.localScale = new Vector3(width * 3f, width * 3f, head);
            }

            public void SetShown(bool shown)
            {
                if (gameObject.activeSelf != shown)
                {
                    gameObject.SetActive(shown);
                }
            }
        }

        /// <summary>
        /// A number that faces the camera and stays the same size on screen.
        /// </summary>
        private sealed class Label : MonoBehaviour
        {
            /// <summary>
            /// Height on screen, as a fraction of the viewport. Scaled by
            /// distance every frame, because a label fixed in world units is
            /// unreadable across the bench and covers the machine up close -
            /// which is exactly what the first version did.
            /// </summary>
            private const float ScreenHeight = 0.028f;

            private TMPro.TextMeshPro text;
            private TMPro.TextMeshPro shadow;

            public static Label Create(Transform parent, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);

                var label = go.AddComponent<Label>();
                label.Build();
                return label;
            }

            private void Build()
            {
                // A dark copy behind the bright one, offset by a hair. Cheaper
                // than an outline shader and legible against both bare
                // aluminium and the dark bed.
                shadow = Make(new Color(0f, 0f, 0f, 0.85f), 0.02f);
                text = Make(Color.white, 0f);
            }

            private TMPro.TextMeshPro Make(Color colour, float behind)
            {
                var go = new GameObject(behind > 0f ? "Shadow" : "Text");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(behind, -behind, 0f);

                var label = go.AddComponent<TMPro.TextMeshPro>();
                label.fontSize = 4f;
                label.alignment = TMPro.TextAlignmentOptions.Center;
                label.color = colour;
                label.rectTransform.sizeDelta = new Vector2(1.4f, 0.4f);

                return label;
            }

            public void Set(Vector3 position, string content, Color colour, bool bold, Camera camera)
            {
                text.text = content;
                shadow.text = content;
                text.color = colour;
                text.fontStyle = bold ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;

                transform.position = position;

                if (camera == null)
                {
                    return;
                }

                transform.rotation = Quaternion.LookRotation(
                    position - camera.transform.position, camera.transform.up);

                // Sized from the distance to the camera, so the number is the
                // same height on screen wherever the view is.
                float distance = Vector3.Distance(position, camera.transform.position);
                float height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

                transform.localScale = Vector3.one * (height * ScreenHeight);
            }

            public void SetShown(bool shown)
            {
                if (gameObject.activeSelf != shown)
                {
                    gameObject.SetActive(shown);
                }
            }
        }
    }
}
