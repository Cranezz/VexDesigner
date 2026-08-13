namespace VexDesigner.UI
{
    using TMPro;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Settings. Some of these do something now; the rest are placed so the
    /// shape of the page is settled before the systems behind them exist.
    ///
    /// The quality control currently drives shadows and lighting. It is
    /// eventually meant to drive **part mesh density**, which is the setting
    /// that will actually matter here - a robot is hundreds of parts at tens of
    /// thousands of triangles each. That swap is noted rather than hidden,
    /// because the control's name should not quietly change meaning later.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private Toggle fullscreenToggle;

        [Header("Quality")]
        [SerializeField] private Slider qualitySlider;
        [SerializeField] private TextMeshProUGUI qualityLabel;

        [Header("Controls")]
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private TextMeshProUGUI sensitivityLabel;

        [Header("Snapping")]
        [SerializeField] private TMP_InputField moveSnapField;
        [SerializeField] private TMP_InputField rotateSnapField;

        [Header("Audio")]
        [SerializeField] private Slider volumeSlider;

        private Resolution[] resolutions;

        private void Start()
        {
            BuildResolutionList();
            LoadValues();
        }

        /// <summary>
        /// Applies saved preferences without needing the settings page.
        ///
        /// The page lives on a disabled object until it is first opened, so its
        /// Start never runs before then. Without this, a saved sensitivity or
        /// snap increment would sit unused until the user happened to visit
        /// settings - which looks exactly like the setting not persisting.
        /// </summary>
        public static void ApplySaved()
        {
            AudioListener.volume = PlayerPrefs.GetFloat("vex.volume", 1f);

            var input = FindAnyObjectByType<VexDesigner.InputSources.FirstPersonInput>();
            if (input != null)
            {
                input.SetLookSensitivity(PlayerPrefs.GetFloat("vex.sensitivity", 0.12f));
            }

            var tool = FindAnyObjectByType<VexDesigner.Parts.TransformToolController>();
            if (tool != null)
            {
                tool.SetMoveSnapInches(PlayerPrefs.GetFloat("vex.snap.move", 0.5f));
                tool.SetRotationSnapDegrees(PlayerPrefs.GetFloat("vex.snap.rotate", 15f));
            }

            ApplyQuality(PlayerPrefs.GetInt("vex.quality", 2));
        }

        private static void ApplyQuality(int level)
        {
            level = Mathf.Clamp(level, 0, 3);

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
        // Display
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
            int current = 0;

            for (int i = 0; i < resolutions.Length; i++)
            {
                Resolution r = resolutions[i];
                options.Add($"{r.width} x {r.height}");

                if (r.width == Screen.width && r.height == Screen.height)
                {
                    current = i;
                }
            }

            resolutionDropdown.AddOptions(options);
            resolutionDropdown.value = current;
            resolutionDropdown.RefreshShownValue();
            resolutionDropdown.onValueChanged.AddListener(OnResolutionChanged);
        }

        private void OnResolutionChanged(int index)
        {
            if (resolutions == null || index < 0 || index >= resolutions.Length)
            {
                return;
            }

            Resolution r = resolutions[index];
            Screen.SetResolution(r.width, r.height, Screen.fullScreenMode);
        }

        public void OnFullscreenChanged(bool value)
        {
            Screen.fullScreenMode = value
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.Windowed;
        }

        // ------------------------------------------------------------------
        // Quality
        // ------------------------------------------------------------------

        public void OnQualityChanged(float value)
        {
            int level = Mathf.Clamp(Mathf.RoundToInt(value), 0, 3);

            // Shadows are the single biggest lighting cost in a room lit by
            // three point lights, so they are what the slider actually moves
            // today.
            ApplyQuality(level);

            if (qualityLabel != null)
            {
                string[] names = { "Low", "Medium", "High", "Ultra" };
                qualityLabel.text = $"Quality: {names[level]}";
            }

            PlayerPrefs.SetInt("vex.quality", level);
        }

        // ------------------------------------------------------------------
        // Controls
        // ------------------------------------------------------------------

        public void OnSensitivityChanged(float value)
        {
            PlayerPrefs.SetFloat("vex.sensitivity", value);

            if (sensitivityLabel != null)
            {
                sensitivityLabel.text = $"Look sensitivity: {value:F2}";
            }

            var input = FindAnyObjectByType<VexDesigner.InputSources.FirstPersonInput>();
            if (input != null)
            {
                input.SetLookSensitivity(value);
            }
        }

        public void OnMoveSnapChanged(string text)
        {
            if (!float.TryParse(text, out float inches) || inches <= 0f)
            {
                return;
            }

            PlayerPrefs.SetFloat("vex.snap.move", inches);

            var tool = FindAnyObjectByType<VexDesigner.Parts.TransformToolController>();
            if (tool != null)
            {
                tool.SetMoveSnapInches(inches);
            }
        }

        public void OnRotateSnapChanged(string text)
        {
            if (!float.TryParse(text, out float degrees) || degrees <= 0f)
            {
                return;
            }

            PlayerPrefs.SetFloat("vex.snap.rotate", degrees);

            var tool = FindAnyObjectByType<VexDesigner.Parts.TransformToolController>();
            if (tool != null)
            {
                tool.SetRotationSnapDegrees(degrees);
            }
        }

        public void OnVolumeChanged(float value)
        {
            AudioListener.volume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("vex.volume", value);
        }

        // ------------------------------------------------------------------
        // Persistence
        // ------------------------------------------------------------------

        private void LoadValues()
        {
            // PlayerPrefs rather than a settings file: these are per-machine
            // preferences, not part of a build, and should not travel with a
            // saved robot.
            if (qualitySlider != null)
            {
                qualitySlider.SetValueWithoutNotify(PlayerPrefs.GetInt("vex.quality", 2));
                OnQualityChanged(qualitySlider.value);
                qualitySlider.onValueChanged.AddListener(OnQualityChanged);
            }

            if (sensitivitySlider != null)
            {
                sensitivitySlider.SetValueWithoutNotify(
                    PlayerPrefs.GetFloat("vex.sensitivity", 0.12f));
                OnSensitivityChanged(sensitivitySlider.value);
                sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
            }

            if (volumeSlider != null)
            {
                volumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat("vex.volume", 1f));
                OnVolumeChanged(volumeSlider.value);
                volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
            }

            if (moveSnapField != null)
            {
                moveSnapField.SetTextWithoutNotify(
                    PlayerPrefs.GetFloat("vex.snap.move", 0.5f).ToString("0.###"));
                OnMoveSnapChanged(moveSnapField.text);
                moveSnapField.onEndEdit.AddListener(OnMoveSnapChanged);
            }

            if (rotateSnapField != null)
            {
                rotateSnapField.SetTextWithoutNotify(
                    PlayerPrefs.GetFloat("vex.snap.rotate", 15f).ToString("0.###"));
                OnRotateSnapChanged(rotateSnapField.text);
                rotateSnapField.onEndEdit.AddListener(OnRotateSnapChanged);
            }

            if (fullscreenToggle != null)
            {
                fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
                fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
            }
        }
    }
}
