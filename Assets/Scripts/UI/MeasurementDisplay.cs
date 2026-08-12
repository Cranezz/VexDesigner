namespace VexDesigner.UI
{
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// Shows how far something has been moved: a line from where it started to
    /// where it is now, labelled in feet, inches and fractions.
    ///
    /// Imperial fractions rather than decimals because that is how VEX parts
    /// and every tape measure in a workshop are marked. "2 1/2 inches" is
    /// directly checkable against a real part; "2.5039 inches" is not.
    /// </summary>
    public sealed class MeasurementDisplay : MonoBehaviour
    {
        [SerializeField] private LineRenderer line;
        [SerializeField] private TextMeshPro label;

        [Tooltip("Smallest fraction shown, as a denominator. 16 gives " +
                 "sixteenths, which is the finest marking on a typical rule.")]
        [SerializeField] private int fractionDenominator = 16;

        [Tooltip("Inset from the screen edge kept when the label is dragged " +
                 "back into view, as a fraction of the viewport.")]
        [SerializeField] private float viewportMargin = 0.08f;

        private static MeasurementDisplay instance;

        private void Awake()
        {
            instance = this;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        public static void Show(Vector3 from, Vector3 to)
        {
            if (instance != null)
            {
                instance.Draw(from, to);
            }
        }

        public static void Hide()
        {
            if (instance != null)
            {
                instance.SetVisible(false);
            }
        }

        private void Draw(Vector3 from, Vector3 to)
        {
            SetVisible(true);

            if (line != null)
            {
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }

            if (label == null)
            {
                return;
            }

            float inches = Vector3.Distance(from, to) / 0.0254f;
            label.text = FormatImperial(inches, fractionDenominator);

            Vector3 anchor = ChooseLabelPoint(from, to);
            label.transform.position = anchor;

            // Always face the viewer, so the measurement is readable from
            // wherever the drag is being watched from.
            Camera cam = Camera.main;
            if (cam != null)
            {
                label.transform.rotation = Quaternion.LookRotation(
                    label.transform.position - cam.transform.position, Vector3.up);
            }
        }

        /// <summary>
        /// Picks a point on the line for the label.
        ///
        /// The midpoint normally, but if that is off screen the label slides
        /// along the line to the furthest point still comfortably in view -
        /// so a long drag that runs out of frame still reports its length
        /// rather than taking the number away with it.
        /// </summary>
        private Vector3 ChooseLabelPoint(Vector3 from, Vector3 to)
        {
            Camera cam = Camera.main;
            Vector3 midpoint = (from + to) * 0.5f;

            if (cam == null || IsComfortablyVisible(cam, midpoint))
            {
                return midpoint;
            }

            // Walk back from the midpoint toward the start, which is where the
            // user was looking when the drag began and so is most likely on
            // screen. First point that is visible wins.
            const int steps = 12;
            for (int i = 1; i <= steps; i++)
            {
                float t = 0.5f * (1f - (i / (float)steps));
                Vector3 candidate = Vector3.Lerp(from, to, t);

                if (IsComfortablyVisible(cam, candidate))
                {
                    return candidate;
                }
            }

            return from;
        }

        private bool IsComfortablyVisible(Camera cam, Vector3 worldPoint)
        {
            Vector3 viewport = cam.WorldToViewportPoint(worldPoint);

            return viewport.z > 0f &&
                   viewport.x > viewportMargin && viewport.x < 1f - viewportMargin &&
                   viewport.y > viewportMargin && viewport.y < 1f - viewportMargin;
        }

        private void SetVisible(bool visible)
        {
            if (line != null) { line.enabled = visible; }
            if (label != null) { label.enabled = visible; }
        }

        // ------------------------------------------------------------------
        // Formatting
        // ------------------------------------------------------------------

        /// <summary>
        /// Formats inches the way a tape measure reads: 134.5 becomes
        /// 11' 2 1/2".
        /// </summary>
        public static string FormatImperial(float totalInches, int denominator)
        {
            if (denominator < 1)
            {
                denominator = 16;
            }

            // Round to the nearest marking first, so the parts below never
            // disagree with each other - rounding afterwards can turn
            // 11' 11 16/16" into a fraction that should have carried.
            int ticks = Mathf.RoundToInt(totalInches * denominator);

            int feet = ticks / (12 * denominator);
            ticks -= feet * 12 * denominator;

            int inches = ticks / denominator;
            int remainder = ticks - (inches * denominator);

            string fraction = string.Empty;
            if (remainder > 0)
            {
                int divisor = Gcd(remainder, denominator);
                fraction = $" {remainder / divisor}/{denominator / divisor}";
            }

            if (feet > 0)
            {
                return $"{feet}' {inches}{fraction}\"";
            }

            return $"{inches}{fraction}\"";
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = b;
                b = a % b;
                a = t;
            }

            return a;
        }
    }
}
