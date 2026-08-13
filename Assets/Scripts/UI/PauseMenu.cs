namespace VexDesigner.UI
{
    using UnityEngine;
    using UnityEngine.InputSystem;
    using VexDesigner.InputSources;
    using VexDesigner.Player;

    /// <summary>
    /// Escape menu: resume, settings, and the file operations.
    ///
    /// Pausing here means <c>Time.timeScale = 0</c>, which stops physics dead.
    /// That matters more than in most games: parts are live rigidbodies, and a
    /// menu that let them keep settling would mean the robot quietly rearranged
    /// itself while its owner was reading a dialog.
    ///
    /// The file operations are wired to real methods that currently do nothing
    /// but explain themselves. Stubs that say what they will do are far more
    /// useful than buttons that silently fail, and they fix the shape of the
    /// interface before the save format exists.
    /// </summary>
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private GameObject mainPage;
        [SerializeField] private GameObject settingsPage;

        private FirstPersonInput input;
        private FirstPersonController player;

        public bool IsOpen { get; private set; }

        private void Awake()
        {
            input = FindAnyObjectByType<FirstPersonInput>();
            player = FindAnyObjectByType<FirstPersonController>();

            // The settings page is disabled until first opened, so its own
            // startup never runs. Preferences are applied from here instead.
            SettingsPanel.ApplySaved();

            Close();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            {
                return;
            }

            // Escape backs out one level at a time rather than closing
            // everything, which is what people expect from a settings page.
            if (IsOpen && settingsPage != null && settingsPage.activeSelf)
            {
                ShowMain();
                return;
            }

            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Open()
        {
            IsOpen = true;

            if (rootPanel != null) { rootPanel.SetActive(true); }
            ShowMain();

            // Physics stops with the game. Parts are live bodies and would
            // otherwise carry on settling while the menu is up.
            Time.timeScale = 0f;

            if (input != null) { input.SetCursorLocked(false); }
            if (player != null) { player.MovementEnabled = false; }
        }

        public void Close()
        {
            IsOpen = false;

            if (rootPanel != null) { rootPanel.SetActive(false); }

            Time.timeScale = 1f;

            if (input != null) { input.SetCursorLocked(true); }
            if (player != null) { player.MovementEnabled = true; }
        }

        public void ShowMain()
        {
            if (mainPage != null) { mainPage.SetActive(true); }
            if (settingsPage != null) { settingsPage.SetActive(false); }
        }

        public void ShowSettings()
        {
            if (mainPage != null) { mainPage.SetActive(false); }
            if (settingsPage != null) { settingsPage.SetActive(true); }
        }

        // ------------------------------------------------------------------
        // File operations - deliberate stubs
        // ------------------------------------------------------------------

        /// <summary>
        /// Saves the build as structured data: part IDs, transforms, cut lists
        /// and joins. Never geometry - see ARCHITECTURE.md section 4.
        /// </summary>
        public void SaveFile()
        {
            NotImplemented(
                "Save",
                "Will write part IDs, positions, cut lists and joins as JSON. " +
                "Geometry is rebuilt from those on load, so files stay small.");
        }

        /// <summary>Loads a build back by replaying its part list and cuts.</summary>
        public void LoadFile()
        {
            NotImplemented(
                "Load",
                "Will rebuild each part from its ID and re-apply its cuts in " +
                "order, so geometry cannot degrade across save and load.");
        }

        /// <summary>
        /// Exports the assembled robot as a single OBJ, for sharing or
        /// rendering elsewhere. One-way: an exported OBJ has no part identity.
        /// </summary>
        public void ExportObj()
        {
            NotImplemented(
                "Export OBJ",
                "Will write the whole build as one mesh. One-way - an OBJ has " +
                "no part IDs or cut history, so it cannot be loaded back.");
        }

        /// <summary>
        /// Imports an OBJ as a new custom part, so non-VEX parts can be used
        /// in a build.
        /// </summary>
        public void ImportPart()
        {
            NotImplemented(
                "Import part",
                "Will bring an OBJ in as a custom part with its own definition, " +
                "so it can be placed like any other.");
        }

        public void ExitToMenu()
        {
            NotImplemented(
                "Exit to menu",
                "Needs a menu scene to exit to. Nothing to leave for yet.");
        }

        private static void NotImplemented(string action, string plan)
        {
            MessageBanner.Info($"{action}: not built yet");
            Debug.Log($"[PauseMenu] {action} — {plan}");
        }
    }
}
