namespace VexDesigner.UI
{
    using System.Text;
    using TMPro;
    using UnityEngine;
    using VexDesigner.Parts;

    /// <summary>
    /// Bottom-right list of what the current situation lets you do.
    ///
    /// Contextual on purpose. A fixed list of every binding becomes wallpaper
    /// within a minute and stops being read; a list that changes when the
    /// situation changes keeps being worth glancing at, and it teaches the
    /// controls by showing them exactly when they become relevant.
    ///
    /// Movement and look are deliberately absent - anyone who has played a
    /// first-person game already knows them, and listing them would crowd out
    /// the bindings that are actually specific to this tool.
    /// </summary>
    public sealed class KeybindHints : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;
        [SerializeField] private PartPlacementController placement;

        private readonly StringBuilder builder = new StringBuilder(256);
        private string lastText = string.Empty;

        private void Awake()
        {
            if (placement == null)
            {
                placement = FindAnyObjectByType<PartPlacementController>();
            }

            if (label == null)
            {
                label = GetComponent<TextMeshProUGUI>();
            }
        }

        private void Update()
        {
            if (label == null)
            {
                return;
            }

            string text = Compose();

            // Assigning TMP text rebuilds its mesh, so only do it on a change.
            if (text != lastText)
            {
                label.text = text;
                lastText = text;
            }
        }

        private string Compose()
        {
            builder.Clear();

            if (placement == null)
            {
                return string.Empty;
            }

            if (placement.IsCarrying)
            {
                if (placement.CarriedIsFrozen)
                {
                    Add("RMB", "Rotate");
                    Add("K", "Unfreeze");
                    Add("LMB", "Release");
                }
                else
                {
                    Add("LMB", "Place");
                    Add("RMB", "Rotate");
                    Add("Scroll", "Distance");
                    Add("Alt", "Place repeatedly");
                    Add("K", "Freeze in air");
                }
            }
            else if (placement.HasTarget)
            {
                Add("LMB", "Pick up");
            }

            Add("C", "Crouch");
            return builder.ToString();
        }

        private void Add(string key, string action)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            // Rich text so the key reads as a key. TMP parses these inline.
            builder.Append("<color=#FFD479><b>").Append(key).Append("</b></color>  ")
                   .Append(action);
        }
    }
}
