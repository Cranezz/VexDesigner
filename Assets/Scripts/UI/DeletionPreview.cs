namespace VexDesigner.UI
{
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Corner viewport showing what a destructive button is about to remove.
    ///
    /// A confirmation that only says "are you sure?" is a formality - people
    /// click through it without reading. Showing the actual parts that are
    /// about to disappear turns it into an informed decision, which is the
    /// entire point of asking.
    ///
    /// Rendered by a second camera into a RenderTexture rather than by moving
    /// the player's view, which would be disorienting and would lose their
    /// place in the workshop.
    /// </summary>
    public sealed class DeletionPreview : MonoBehaviour
    {
        [SerializeField] private Camera previewCamera;
        [SerializeField] private RawImage display;
        [SerializeField] private RectTransform frame;

        [Tooltip("How far back from the framed bounds the camera sits, as a " +
                 "multiple of their size.")]
        [SerializeField] private float distanceScale = 1.5f;

        private static DeletionPreview instance;

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

        public static void Show(Bounds bounds, bool hasContent)
        {
            if (instance != null)
            {
                instance.Frame(bounds, hasContent);
            }
        }

        public static void Hide()
        {
            if (instance != null)
            {
                instance.SetVisible(false);
            }
        }

        private void Frame(Bounds bounds, bool hasContent)
        {
            if (!hasContent || previewCamera == null)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            // Look down at the target from one corner, so depth is readable.
            // A straight top-down view flattens a pile of parts into an
            // ambiguous blob.
            float size = Mathf.Max(bounds.size.magnitude, 0.3f);
            Vector3 offset = new Vector3(0.6f, 0.8f, -0.9f).normalized * (size * distanceScale);

            previewCamera.transform.position = bounds.center + offset;
            previewCamera.transform.rotation =
                Quaternion.LookRotation(bounds.center - previewCamera.transform.position);

            previewCamera.nearClipPlane = 0.02f;
            previewCamera.farClipPlane = size * distanceScale * 4f;
        }

        private void SetVisible(bool visible)
        {
            if (previewCamera != null)
            {
                // The camera is switched off rather than just hidden, so it
                // costs nothing when not in use.
                previewCamera.enabled = visible;
            }

            if (display != null)
            {
                display.enabled = visible;
            }

            if (frame != null)
            {
                frame.gameObject.SetActive(visible);
            }
        }
    }
}
