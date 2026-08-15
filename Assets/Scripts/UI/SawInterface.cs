namespace VexDesigner.UI
{
    using System.Globalization;
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;
    using VexDesigner.Parts;

    /// <summary>
    /// The saw's readouts, its keypad, and the button that takes the cut.
    ///
    /// The knobs are the quick way and this is the exact one. A builder working
    /// to a drawing knows the number before they touch the machine, and making
    /// them dial 7.317 inches by hand when they could type it would be a poor
    /// joke - so every control has a field, and every field shows what the
    /// knob is currently reading.
    ///
    /// Distances are shown to a thousandth of an inch because that is the
    /// precision the cut actually has, and hiding it would leave the user
    /// unable to tell 7.3 from 7.317.
    /// </summary>
    public sealed class SawInterface : MonoBehaviour
    {
        [SerializeField] private SawController controller;
        [SerializeField] private GameObject panel;

        [SerializeField] private TextMeshProUGUI feedLabel;
        [SerializeField] private TextMeshProUGUI bladeLabel;
        [SerializeField] private TextMeshProUGUI rotationLabel;
        [SerializeField] private TextMeshProUGUI stockLabel;
        [SerializeField] private TextMeshProUGUI hintLabel;

        [SerializeField] private TMP_InputField feedField;
        [SerializeField] private TMP_InputField bladeField;
        [SerializeField] private TMP_InputField rotateXField;
        [SerializeField] private TMP_InputField rotateYField;
        [SerializeField] private TMP_InputField rotateZField;

        [SerializeField] private Button cutButton;

        private void Awake()
        {
            if (controller == null)
            {
                controller = FindAnyObjectByType<SawController>();
            }

            Bind(feedField, text => Saw()?.SetFeed(ParseInches(text, Saw().FeedInches)));

            Bind(bladeField, text =>
                Saw()?.SetBladeAngle(ParseDegrees(text, Saw().BladeAngle)));

            Bind(rotateXField, text =>
                Saw()?.SetRotation(0, ParseDegrees(text, Saw().Rotation.x)));

            Bind(rotateYField, text =>
                Saw()?.SetRotation(1, ParseDegrees(text, Saw().Rotation.y)));

            Bind(rotateZField, text =>
                Saw()?.SetRotation(2, ParseDegrees(text, Saw().Rotation.z)));

            if (cutButton != null)
            {
                cutButton.onClick.AddListener(TakeCut);
            }
        }

        private static void Bind(TMP_InputField field, System.Action<string> apply)
        {
            if (field != null)
            {
                field.onEndEdit.AddListener(text => apply(text));
            }
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
                return;
            }

            Set(feedLabel, $"Feed  {saw.FeedInches:0.000} in");
            Set(bladeLabel, $"Blade  {saw.BladeAngle:0.00}°");

            Set(rotationLabel,
                $"Turn  X {saw.Rotation.x:0.##}°   " +
                $"Y {saw.Rotation.y:0.##}°   Z {saw.Rotation.z:0.##}°");

            Set(stockLabel,
                saw.HasPart
                    ? $"Stock  {saw.StockLengthInches:0.000} in  " +
                      $"→  {Mathf.Max(0f, saw.StockLengthInches - saw.OffcutInches):0.000} in " +
                      $"after cutting off {saw.OffcutInches:0.000} in"
                    : "No stock on the bed");

            Set(hintLabel,
                "Drag a knob to set it • Shift for finer steps • " +
                "Ctrl for free • Right-drag to pan • Scroll to zoom • " +
                "Esc to leave");

            if (cutButton != null)
            {
                // Nothing to cut is not an error worth a message; the button
                // simply cannot be pressed.
                cutButton.interactable = saw.HasPart && saw.OffcutInches > 0.0005f;
            }

            // Typing into a field while the knob is being turned would fight
            // the user, so the fields follow the knobs unless one is focused.
            Fill(feedField, saw.FeedInches.ToString("0.000", CultureInfo.InvariantCulture));
            Fill(bladeField, saw.BladeAngle.ToString("0.00", CultureInfo.InvariantCulture));
            Fill(rotateXField, saw.Rotation.x.ToString("0.##", CultureInfo.InvariantCulture));
            Fill(rotateYField, saw.Rotation.y.ToString("0.##", CultureInfo.InvariantCulture));
            Fill(rotateZField, saw.Rotation.z.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private void TakeCut()
        {
            SawStation saw = Saw();

            if (saw == null || !saw.Cut())
            {
                return;
            }

            MessageBanner.Info("Cut");
        }

        private static void Set(TextMeshProUGUI label, string text)
        {
            if (label != null && label.text != text)
            {
                label.text = text;
            }
        }

        private static void Fill(TMP_InputField field, string text)
        {
            if (field != null && !field.isFocused && field.text != text)
            {
                field.SetTextWithoutNotify(text);
            }
        }

        // ------------------------------------------------------------------
        // Reading what was typed
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads a length in inches, as a builder would write it.
        ///
        /// Accepts a decimal - 7.317 - or the way it is actually said aloud in
        /// a workshop, which is a whole number and a fraction: "7 5/16". Both
        /// forms appear on drawings and refusing either would send the user to
        /// a calculator.
        /// </summary>
        public static float ParseInches(string text, float fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            text = text.Trim().Replace("\"", string.Empty);

            string[] parts = text.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            float total = 0f;

            foreach (string piece in parts)
            {
                int slash = piece.IndexOf('/');

                if (slash > 0)
                {
                    string top = piece.Substring(0, slash);
                    string bottom = piece.Substring(slash + 1);

                    if (float.TryParse(top, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out float numerator) &&
                        float.TryParse(bottom, NumberStyles.Float, CultureInfo.InvariantCulture,
                            out float denominator) &&
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

            return Mathf.Max(0f, total);
        }

        /// <summary>
        /// Reads an angle, folded onto 0-360.
        ///
        /// Anything is allowed in: 720 is two full turns and means 0, and -45
        /// is the same place as 315. Both are things people type, and both have
        /// an obvious right answer, so neither is an error.
        /// </summary>
        public static float ParseDegrees(string text, float fallback)
        {
            if (!float.TryParse(
                    (text ?? string.Empty).Trim().Replace("°", string.Empty),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float degrees))
            {
                return fallback;
            }

            return SawStation.Normalise(degrees);
        }
    }
}
