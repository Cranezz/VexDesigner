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

        [Tooltip("How far the label sits off the trail, relative to its own " +
                 "size, so the gap stays proportional at any distance.")]
        [SerializeField] private float lineOffset = 0.6f;

        [Tooltip("Distance, in metres, at which the label is drawn at its " +
                 "authored size. Beyond it the label grows in world space to " +
                 "keep the same size on screen; nearer, it shrinks.")]
        [SerializeField] private float referenceDistance = 1.5f;

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
            Camera cam = Camera.main;

            if (cam == null)
            {
                label.transform.position = anchor;
                return;
            }

            OrientAlongLine(anchor, (to - from), cam);
        }

        /// <summary>
        /// Picks a point on the line for the label.
        ///
        /// The midpoint normally, but if that is off screen the label slides
        /// along the line to the furthest point still comfortably in view -
        /// so a long drag that runs out of frame still reports its length
        /// rather than taking the number away with it.
        /// </summary>
        /// <summary>
        /// Lays the text along the trail and lifts it clear of the line.
        ///
        /// Running the text along the line rather than always horizontal means
        /// a vertical move is labelled with vertical text, which reads as
        /// belonging to that measurement rather than floating near it. The
        /// offset keeps the number off the line itself, which would otherwise
        /// strike through the digits.
        /// </summary>
        private void OrientAlongLine(Vector3 anchor, Vector3 lineDirection, Camera cam)
        {
            Vector3 toCamera = (anchor - cam.transform.position).normalized;

            // The text plane faces the camera; within that plane, the reading
            // direction follows the line.
            Vector3 along = Vector3.ProjectOnPlane(lineDirection, toCamera);

            if (along.sqrMagnitude < 1e-8f)
            {
                // Line points straight at the viewer, so it has no direction on
                // screen. Fall back to horizontal.
                along = Vector3.ProjectOnPlane(cam.transform.right, toCamera);
            }

            along.Normalize();

            // Keep text left-to-right on screen. Without this a drag in the
            // opposite direction renders the label upside down.
            if (Vector3.Dot(along, cam.transform.right) < 0f)
            {
                along = -along;
            }

            Vector3 up = Vector3.Cross(toCamera, along).normalized;

            label.transform.rotation = Quaternion.LookRotation(toCamera, up);

            // Scale with distance so the number is the same size on screen
            // wherever the part is.
            //
            // Expressed as a ratio against a reference distance, not as a raw
            // fraction. A raw fraction multiplied the authored font size by
            // about 0.05 and shrank the label to a few millimetres tall, which
            // is why it appeared to vanish entirely.
            float distance = Vector3.Distance(cam.transform.position, anchor);
            float scale = Mathf.Max(distance / Mathf.Max(0.01f, referenceDistance), 0.15f);
            label.transform.localScale = Vector3.one * scale;

            label.transform.position = anchor + (up * (lineOffset * scale));
        }

        private Vector3 ChooseLabelPoint(Vector3 from, Vector3 to)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return (from + to) * 0.5f;
            }

            // Sample the whole line and score every point, rather than testing
            // only the midpoint and giving up. A single test fails as soon as
            // the midpoint leaves frame, which is exactly when the label most
            // needs to move somewhere else.
            const int samples = 32;

            float bestScore = float.MaxValue;
            Vector3 bestPoint = (from + to) * 0.5f;
            bool foundVisible = false;

            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                Vector3 candidate = Vector3.Lerp(from, to, t);
                Vector3 viewport = cam.WorldToViewportPoint(candidate);

                if (viewport.z <= 0f)
                {
                    continue;
                }

                bool visible =
                    viewport.x > viewportMargin && viewport.x < 1f - viewportMargin &&
                    viewport.y > viewportMargin && viewport.y < 1f - viewportMargin;

                // Prefer whatever is nearest the viewer, then near the centre
                // of the screen. Nearness dominates because a label further
                // down a long trail is smaller, further from where the user is
                // looking, and more likely to be occluded.
                float distanceFromCamera = viewport.z;
                float distanceFromScreenCentre =
                    Vector2.Distance(new Vector2(viewport.x, viewport.y), new Vector2(0.5f, 0.5f));

                float score = distanceFromCamera + (distanceFromScreenCentre * 0.35f);

                // Any visible point beats any off-screen one, whatever the
                // scores; the label being readable matters more than where
                // along the line it sits.
                if (visible)
                {
                    if (!foundVisible || score < bestScore)
                    {
                        foundVisible = true;
                        bestScore = score;
                        bestPoint = candidate;
                    }
                }
                else if (!foundVisible && distanceFromScreenCentre < bestScore)
                {
                    bestScore = distanceFromScreenCentre;
                    bestPoint = candidate;
                }
            }

            return bestPoint;
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
