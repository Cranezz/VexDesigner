namespace VexDesigner.UI
{
    using System.Globalization;
    using TMPro;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;
    using VexDesigner.Parts;

    /// <summary>
    /// The saw's numbers, top left, each one editable and each one tied to the
    /// thing it measures out on the machine.
    ///
    /// Pointing at a field lights up the line it refers to, out on the metal,
    /// and pointing at the line lights up the field. That link is the whole
    /// point of the panel: a column of six numbers means nothing on its own,
    /// and the moment it says which is which the machine explains itself.
    ///
    /// Everything can be typed as well as dragged, because a builder working to
    /// a drawing already knows the number and dialling it in by hand would be a
    /// poor joke.
    /// </summary>
    public sealed class SawPanel : MonoBehaviour
    {
        [SerializeField] private SawController controller;
        [SerializeField] private GameObject panel;

        [Header("Fields")]
        [SerializeField] private TMP_InputField angleXField;
        [SerializeField] private TMP_InputField angleYField;
        [SerializeField] private TMP_InputField angleZField;
        [SerializeField] private TMP_InputField nearField;
        [SerializeField] private TMP_InputField farField;
        [SerializeField] private TMP_InputField bladeField;

        [Header("Readouts")]
        [SerializeField] private TextMeshProUGUI stockLabel;
        [SerializeField] private TextMeshProUGUI hintLabel;
        [SerializeField] private Button cutButton;

        private SawAnnotations annotations;

        /// <summary>What the pointer is over in the panel, if anything.</summary>
        public SawAnnotations.Item HoveredField { get; private set; }

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindAnyObjectByType<SawController>();
            }

            Bind(angleXField, SawAnnotations.Item.RotateX,
                text => Saw()?.SetRotation(0, ParseDegrees(text, Saw().Rotation.x)));

            Bind(angleYField, SawAnnotations.Item.RotateY,
                text => Saw()?.SetRotation(1, ParseDegrees(text, Saw().Rotation.y)));

            Bind(angleZField, SawAnnotations.Item.RotateZ,
                text => Saw()?.SetRotation(2, ParseDegrees(text, Saw().Rotation.z)));

            Bind(nearField, SawAnnotations.Item.NearFace,
                text => Saw()?.SetNearFace(ParseInches(text, Saw().NearFaceInches)));

            Bind(farField, SawAnnotations.Item.FarFace,
                text => Saw()?.SetFarFace(ParseInches(text, Saw().FarFaceInches)));

            Bind(bladeField, SawAnnotations.Item.BladeAngle,
                text => Saw()?.SetBladeAngle(ParseSignedDegrees(text, Saw().BladeAngle)));

            if (cutButton != null)
            {
                cutButton.onClick.AddListener(() => controller?.TakeCut());
            }
        }

        /// <summary>
        /// Wires a field to its value and to the drawing it belongs to.
        /// </summary>
        private void Bind(
            TMP_InputField field, SawAnnotations.Item item, System.Action<string> apply)
        {
            if (field == null)
            {
                return;
            }

            field.onEndEdit.AddListener(text => apply(text));

            var triggers = field.gameObject.AddComponent<EventTrigger>();

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => HoveredField = item);
            triggers.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ =>
            {
                if (HoveredField == item)
                {
                    HoveredField = SawAnnotations.Item.None;
                }
            });

            triggers.triggers.Add(exit);
        }

        private SawStation Saw() => controller == null ? null : controller.Open;

        private void Update()
        {
            SawStation saw = Saw();
            bool open = saw != null;

            if (panel != null && panel.activeSelf != open)
            {
                panel.SetActive(open);
            }

            if (!open)
            {
                HoveredField = SawAnnotations.Item.None;
                return;
            }

            if (annotations == null)
            {
                annotations = saw.GetComponentInChildren<SawAnnotations>();
            }

            // The panel and the machine take turns: whichever the pointer is
            // actually over wins, so hovering a field lights the line and
            // hovering the line lights the field.
            SawAnnotations.Item lit = HoveredField != SawAnnotations.Item.None
                ? HoveredField
                : controller.HoveredItem;

            annotations?.Highlight(lit);

            Glow(angleXField, lit == SawAnnotations.Item.RotateX);
            Glow(angleYField, lit == SawAnnotations.Item.RotateY);
            Glow(angleZField, lit == SawAnnotations.Item.RotateZ);
            Glow(nearField, lit == SawAnnotations.Item.NearFace);
            Glow(farField, lit == SawAnnotations.Item.FarFace);
            Glow(bladeField, lit == SawAnnotations.Item.BladeAngle);

            Fill(angleXField, saw.Rotation.x, "0.##");
            Fill(angleYField, saw.Rotation.y, "0.##");
            Fill(angleZField, saw.Rotation.z, "0.##");
            Fill(nearField, saw.NearFaceInches, "0.000");
            Fill(farField, saw.FarFaceInches, "0.000");
            Fill(bladeField, saw.BladeAngle, "0.00");

            if (stockLabel != null)
            {
                stockLabel.text =
                    $"<b>{saw.StockLengthInches:0.000} in</b> of stock   →   " +
                    $"keeping <b>{saw.KeptLengthInches:0.000} in</b>";
            }

            if (hintLabel != null)
            {
                hintLabel.text =
                    "Drag the stock, the ball, or the blade\n" +
                    "<b>Shift</b> 1/8 in · 15°    <b>Ctrl</b> 1/16 in · 1°\n" +
                    "Right-drag orbits · <b>Enter</b> cuts · <b>Esc</b> leaves";
            }

            if (cutButton != null)
            {
                cutButton.interactable = saw.HasPart && Mathf.Abs(saw.OffcutInches) > 0.0005f;
            }
        }

        /// <summary>
        /// Marks a field as the one the machine is pointing at, by lightening
        /// its background rather than by outlining it. An outline on a small
        /// field crowds the number it is meant to draw attention to.
        /// </summary>
        private static void Glow(TMP_InputField field, bool lit)
        {
            if (field == null)
            {
                return;
            }

            var background = field.GetComponent<Image>();

            if (background != null)
            {
                background.color = lit
                    ? new Color(0.36f, 0.40f, 0.48f)
                    : new Color(0.16f, 0.17f, 0.20f);
            }
        }

        private static void Fill(TMP_InputField field, float value, string format)
        {
            if (field == null || field.isFocused)
            {
                return;
            }

            string text = value.ToString(format, CultureInfo.InvariantCulture);

            if (field.text != text)
            {
                field.SetTextWithoutNotify(text);
            }
        }

        // ------------------------------------------------------------------
        // Reading what was typed
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads a length in inches, decimal or as a workshop says it aloud -
        /// "7 5/16". Negative is allowed, since a cut can sit beyond the end of
        /// the stock.
        /// </summary>
        public static float ParseInches(string text, float fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            text = text.Trim().Replace("\"", string.Empty);

            bool negative = text.StartsWith("-");

            if (negative)
            {
                text = text.Substring(1);
            }

            string[] parts = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            float total = 0f;

            foreach (string piece in parts)
            {
                int slash = piece.IndexOf('/');

                if (slash > 0)
                {
                    if (float.TryParse(piece.Substring(0, slash), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float numerator) &&
                        float.TryParse(piece.Substring(slash + 1), NumberStyles.Float,
                            CultureInfo.InvariantCulture, out float denominator) &&
                        Mathf.Abs(denominator) > 0.0001f)
                    {
                        total += numerator / denominator;
                        continue;
                    }

                    return fallback;
                }

                if (!float.TryParse(piece, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out float whole))
                {
                    return fallback;
                }

                total += whole;
            }

            return negative ? -total : total;
        }

        /// <summary>An angle folded onto 0-360, for turning the stock over.</summary>
        public static float ParseDegrees(string text, float fallback)
        {
            if (!Read(text, out float degrees))
            {
                return fallback;
            }

            return SawStation.Normalise(degrees);
        }

        /// <summary>
        /// A blade angle, kept signed. Zero is square and the sign says which
        /// way the mitre leans, so folding it onto 0-360 would throw away the
        /// only thing the sign was carrying.
        /// </summary>
        public static float ParseSignedDegrees(string text, float fallback)
        {
            if (!Read(text, out float degrees))
            {
                return fallback;
            }

            return Mathf.Clamp(degrees, -SawStation.MaxBladeAngle, SawStation.MaxBladeAngle);
        }

        private static bool Read(string text, out float degrees)
        {
            return float.TryParse(
                (text ?? string.Empty).Trim().Replace("°", string.Empty),
                NumberStyles.Float, CultureInfo.InvariantCulture, out degrees);
        }
    }
}
