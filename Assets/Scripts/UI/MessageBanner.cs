namespace VexDesigner.UI
{
    using TMPro;
    using UnityEngine;

    /// <summary>
    /// Brief message across the top of the screen, for telling the user why
    /// something they just tried did not happen.
    ///
    /// The rule this exists to serve: an action that silently does nothing is
    /// indistinguishable from a bug. Trying to drag a frozen part and having it
    /// simply not move reads as broken; the same non-movement plus "this part
    /// is frozen - press K to unfreeze" reads as a rule, and teaches the
    /// binding at the moment it is wanted.
    ///
    /// Repeats of the same message refresh the timer rather than queueing, so
    /// holding a key down does not build a backlog of identical banners.
    /// </summary>
    public sealed class MessageBanner : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private CanvasGroup group;

        [Tooltip("Seconds held at full opacity before fading.")]
        [SerializeField] private float holdTime = 1.4f;

        [SerializeField] private float fadeTime = 0.45f;

        private static MessageBanner instance;

        private string currentMessage;
        private float remaining;

        private void Awake()
        {
            instance = this;

            if (label == null) { label = GetComponentInChildren<TextMeshProUGUI>(); }
            if (group == null) { group = GetComponent<CanvasGroup>(); }
            if (group != null) { group.alpha = 0f; }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// Shows a warning. Safe to call every frame - the message only
        /// restarts when its text changes or it has already faded.
        /// </summary>
        public static void Warn(string message)
        {
            if (instance != null)
            {
                // Saturated, not pastel. A washed-out warning is easy to miss
                // against a workshop full of mid-tone browns and greys.
                instance.Show(message, new Color(1f, 0.13f, 0.10f));
            }
        }

        public static void Info(string message)
        {
            if (instance != null)
            {
                instance.Show(message, new Color(0.92f, 0.94f, 0.97f));
            }
        }

        private void Show(string message, Color colour)
        {
            if (label != null)
            {
                label.text = message;
                label.color = colour;
            }

            currentMessage = message;
            remaining = holdTime + fadeTime;
        }

        private void Update()
        {
            if (group == null || remaining <= 0f)
            {
                return;
            }

            remaining -= Time.unscaledDeltaTime;

            group.alpha = remaining > fadeTime
                ? 1f
                : Mathf.Clamp01(remaining / fadeTime);

            if (remaining <= 0f)
            {
                currentMessage = null;
            }
        }
    }
}
