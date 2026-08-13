namespace VexDesigner.UI
{
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Settings, edited then applied.
    ///
    /// Changes are held pending until Apply rather than taking effect as the
    /// control moves. That matters for the ones that are disruptive to preview
    /// - a resolution change mid-drag would resize the window on every step of
    /// the slider - and it gives a clear way to abandon a change by leaving
    /// without applying.
    ///
    /// The quality control currently drives shadows. It is eventually meant to
    /// drive **part mesh density**, which is the setting that will actually
    /// matter here: a robot is hundreds of parts at tens of thousands of
    /// triangles each. That swap is stated on the page rather than hidden, so
    /// the control does not quietly change meaning later.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private Toggle fullscreenToggle;

        [Header("Quality")]
        [SerializeField] private Slider qualitySlider;
        [SerializeField] private TextMeshProUGUI qualityValue;

        [Header("Controls")]
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private TextMeshProUGUI sensitivityValue;

        [Header("Audio")]
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private TextMeshProUGUI volumeValue;

        [Header("Snapping")]
        [SerializeField] private TMP_InputField moveSnapField;
        [SerializeField] private TMP_InputField rotateSnapField;

        [Header("Status")]
        [SerializeField] private TextMeshProUGUI statusLabel;

        private Resolution[] resolutions;
        private bool wired;

        private const string KeyQuality = "vex.quality";
        private const string KeySensitivity = "vex.sensitivity";
        private const string KeyVolume = "vex.volume";
        private const string KeySnapMove = "vex.snap.move";
        private const string KeySnapRotate = "vex.snap.rotate";

        private void OnEnable()
        {
            if (!wired)
            {
                BuildResolutionList();
                WireLabels();
                wired = true;
            }

            // Reload from saved every time the page opens, so leaving without
            // applying genuinely discards the changes rather than leaving the
            // controls showing values that are not in effect.
            LoadIntoControls();
            SetStatus(string.Empty);
        }

        // ------------------------------------------------------------------
        // Apply
        // ------------------------------------------------------------------

        /// <summary>Commits every pending value and saves it.</summary>
        public void Apply()
        {
            if (qualitySlider != null)
            {
                PlayerPrefs.SetInt(KeyQuality, Mathf.RoundToInt(qualitySlider.value));
            }

            if (sensitivitySlider != null)
            {
                PlayerPrefs.SetFloat(KeySensitivity, sensitivitySlider.value);
            }

            if (volumeSlider != null)
            {
                PlayerPrefs.SetFloat(KeyVolume, volumeSlider.value);
            }

            if (moveSnapField != null &&
                float.TryParse(moveSnapField.text, out float moveSnap) && moveSnap > 0f)
            {
                PlayerPrefs.SetFloat(KeySnapMove, moveSnap);
            }

            if (rotateSnapField != null &&
                float.TryParse(rotateSnapField.text, out float rotateSnap) && rotateSnap > 0f)
            {
                PlayerPrefs.SetFloat(KeySnapRotate, rotateSnap);
            }

            PlayerPrefs.Save();
            ApplySaved();
            ApplyDisplay();

            SetStatus("Applied.");
        }

        private void ApplyDisplay()
        {
            if (fullscreenToggle != null)
            {
                Screen.fullScreenMode = fullscreenToggle.isOn
                    ? FullScreenMode.FullScreenWindow
                    : FullScreenMode.Windowed;
            }

            if (resolutionDropdown != null && resolutions != null &&
                resolutionDropdown.value >= 0 && resolutionDropdown.value < resolutions.Length)
            {
                Resolution r = resolutions[resolutionDropdown.value];
                Screen.SetResolution(r.width, r.height, Screen.fullScreenMode);
            }
        }

        /// <summary>
        /// Applies saved preferences without needing the settings page.
        ///
        /// The page lives on a disabled object until it is first opened, so its
        /// own startup never runs before then. Without this, a saved
        /// sensitivity or snap increment would sit unused until the user
        /// happened to visit settings - which looks exactly like the setting
        /// not persisting.
        /// </summary>
        public static void ApplySaved()
        {
            AudioListener.volume = PlayerPrefs.GetFloat(KeyVolume, 1f);

            var input = FindAnyObjectByType<VexDesigner.InputSources.FirstPersonInput>();
            if (input != null)
            {
                input.SetLookSensitivity(PlayerPrefs.GetFloat(KeySensitivity, 0.12f));
            }

            var tool = FindAnyObjectByType<VexDesigner.Parts.TransformToolController>();
            if (tool != null)
            {
                tool.SetMoveSnapInches(PlayerPrefs.GetFloat(KeySnapMove, 0.5f));
                tool.SetRotationSnapDegrees(PlayerPrefs.GetFloat(KeySnapRotate, 15f));
            }

            ApplyQuality(PlayerPrefs.GetInt(KeyQuality, 2));
        }

        private static void ApplyQuality(int level)
        {
            level = Mathf.Clamp(level, 0, 3);

            // Shadows are the single biggest lighting cost in a room lit by
            // three point lights, so they are what the slider actually moves
            // today.
            switch (level)
            {
                case 0:
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.shadowDistance = 0f;
                    break;

                case 1:
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowDistance = 8f;
                    break;

                case 2:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowDistance = 16f;
                    break;

                default:
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowDistance = 30f;
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------

        private void BuildResolutionList()
        {
            if (resolutionDropdown == null)
            {
                return;
            }

            resolutions = Screen.resolutions;
            resolutionDropdown.ClearOptions();

            var options = new System.Collections.Generic.List<string>();
            foreach (Resolution r in resolutions)
            {
                options.Add($"{r.width} x {r.height}");
            }

            resolutionDropdown.AddOptions(options);
        }

        /// <summary>
        /// Live numeric readouts. These update as the control moves even though
        /// the value is not applied yet - a slider with no number beside it is
        /// guesswork.
        /// </summary>
        private void WireLabels()
        {
            if (qualitySlider != null)
            {
                qualitySlider.onValueChanged.AddListener(v =>
                {
                    string[] names = { "Low", "Medium", "High", "Ultra" };
                    SetText(qualityValue, names[Mathf.Clamp(Mathf.RoundToInt(v), 0, 3)]);
                    SetStatus("Not applied");
                });
            }

            if (sensitivitySlider != null)
            {
                sensitivitySlider.onValueChanged.AddListener(v =>
                {
                    SetText(sensitivityValue, v.ToString("0.00"));
                    SetStatus("Not applied");
                });
            }

            if (volumeSlider != null)
            {
                volumeSlider.onValueChanged.AddListener(v =>
                {
                    SetText(volumeValue, $"{Mathf.RoundToInt(v * 100f)}%");
                    SetStatus("Not applied");
                });
            }

            if (moveSnapField != null)
            {
                moveSnapField.onValueChanged.AddListener(_ => SetStatus("Not applied"));
            }

            if (rotateSnapField != null)
            {
                rotateSnapField.onValueChanged.AddListener(_ => SetStatus("Not applied"));
            }

            if (resolutionDropdown != null)
            {
                resolutionDropdown.onValueChanged.AddListener(_ => SetStatus("Not applied"));
            }

            if (fullscreenToggle != null)
            {
                fullscreenToggle.onValueChanged.AddListener(_ => SetStatus("Not applied"));
            }
        }

        private void LoadIntoControls()
        {
            if (qualitySlider != null)
            {
                qualitySlider.SetValueWithoutNotify(PlayerPrefs.GetInt(KeyQuality, 2));
                string[] names = { "Low", "Medium", "High", "Ultra" };
                SetText(qualityValue, names[Mathf.Clamp((int)qualitySlider.value, 0, 3)]);
            }

            if (sensitivitySlider != null)
            {
                sensitivitySlider.SetValueWithoutNotify(
                    PlayerPrefs.GetFloat(KeySensitivity, 0.12f));
                SetText(sensitivityValue, sensitivitySlider.value.ToString("0.00"));
            }

            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(KeyVolume, 1f));
                SetText(volumeValue, $"{Mathf.RoundToInt(volumeSlider.value * 100f)}%");
            }

            if (moveSnapField != null)
            {
                moveSnapField.SetTextWithoutNotify(
                    PlayerPrefs.GetFloat(KeySnapMove, 0.5f).ToString("0.###"));
            }

            if (rotateSnapField != null)
            {
                rotateSnapField.SetTextWithoutNotify(
                    PlayerPrefs.GetFloat(KeySnapRotate, 15f).ToString("0.###"));
            }

            if (fullscreenToggle != null)
            {
                fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            }

            if (resolutionDropdown != null && resolutions != null)
            {
                for (int i = 0; i < resolutions.Length; i++)
                {
                    if (resolutions[i].width == Screen.width &&
                        resolutions[i].height == Screen.height)
                    {
                        resolutionDropdown.SetValueWithoutNotify(i);
                        break;
                    }
                }

                resolutionDropdown.RefreshShownValue();
            }
        }

        private static void SetText(TextMeshProUGUI label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        private void SetStatus(string message)
        {
            SetText(statusLabel, message);
        }
    }
}
