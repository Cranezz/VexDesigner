namespace VexDesigner.EditorTools
{
    using TMPro;
    using UnityEditor;
    using UnityEditor.Events;
    using UnityEngine;
    using UnityEngine.UI;
    using VexDesigner.UI;

    /// <summary>
    /// Builds the pause menu and settings page.
    ///
    /// Generated rather than hand-assembled for the same reason as the rest of
    /// the scene: a Unity UI hierarchy built by dragging is dozens of nested
    /// objects that no diff can explain, whereas this is a readable list of
    /// what exists and why.
    /// </summary>
    public static class PauseMenuBuilder
    {
        private static readonly Color PanelColour = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        private static readonly Color ButtonColour = new Color(0.16f, 0.18f, 0.22f, 1f);
        private static readonly Color AccentColour = new Color(0.22f, 0.45f, 0.75f, 1f);
        private static readonly Color TextColour = new Color(0.92f, 0.94f, 0.97f, 1f);

        public static void Build(Transform canvas)
        {
            GameObject root = Panel(canvas, "PauseMenu", PanelColour);
            Stretch(root.GetComponent<RectTransform>());

            // The component lives on the canvas, NOT on the panel it hides.
            //
            // A component on a disabled GameObject never runs Update, so
            // putting it on the panel meant the menu could switch itself off
            // but nothing was left listening to switch it back on - Escape
            // reached nothing at all.
            var menu = canvas.gameObject.AddComponent<PauseMenu>();

            GameObject main = BuildMainPage(root.transform, menu);
            GameObject settings = BuildSettingsPage(root.transform, menu);

            var so = new SerializedObject(menu);
            so.FindProperty("rootPanel").objectReferenceValue = root;
            so.FindProperty("mainPage").objectReferenceValue = main;
            so.FindProperty("settingsPage").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------------

        private static GameObject BuildMainPage(Transform parent, PauseMenu menu)
        {
            GameObject page = Column(parent, "MainPage", 380f);

            Title(page.transform, "PAUSED");

            Button(page.transform, "Resume", AccentColour, menu, nameof(PauseMenu.Close));
            Button(page.transform, "Settings", ButtonColour, menu, nameof(PauseMenu.ShowSettings));

            Spacer(page.transform, 14f);

            // File operations grouped together and separated from navigation,
            // so the destructive-adjacent items are not adjacent to Resume.
            Button(page.transform, "Save File", ButtonColour, menu, nameof(PauseMenu.SaveFile));
            Button(page.transform, "Load File", ButtonColour, menu, nameof(PauseMenu.LoadFile));
            Button(page.transform, "Import Part (OBJ)", ButtonColour, menu, nameof(PauseMenu.ImportPart));
            Button(page.transform, "Export Build (OBJ)", ButtonColour, menu, nameof(PauseMenu.ExportObj));

            Spacer(page.transform, 14f);

            Button(page.transform, "Exit to Menu",
                new Color(0.42f, 0.16f, 0.16f, 1f), menu, nameof(PauseMenu.ExitToMenu));

            Note(page.transform,
                "File actions are placeholders — they log what they will do.");

            return page;
        }

        private static GameObject BuildSettingsPage(Transform parent, PauseMenu menu)
        {
            GameObject page = Column(parent, "SettingsPage", 620f);
            var panel = page.AddComponent<SettingsPanel>();

            Title(page.transform, "SETTINGS");

            TMP_Dropdown resolution = DropdownRow(page.transform, "Resolution");
            Toggle fullscreen = ToggleRow(page.transform, "Fullscreen");

            Slider quality = SliderRow(page.transform, "Quality", 0f, 3f, true, out var qualityValue);
            Slider sensitivity = SliderRow(page.transform, "Look sensitivity", 0.02f, 0.4f, false, out var sensValue);
            Slider volume = SliderRow(page.transform, "Volume", 0f, 1f, false, out var volumeValue);

            TMP_InputField moveSnap = InputRow(page.transform, "Move snap (in)");
            TMP_InputField rotateSnap = InputRow(page.transform, "Rotate snap (deg)");

            Note(page.transform,
                "Quality currently drives shadows. It will drive part mesh " +
                "density once import quality exists — that is the setting " +
                "that will matter with hundreds of parts on screen.");

            TextMeshProUGUI status = Status(page.transform);

            Spacer(page.transform, 8f);

            // Apply commits; Back leaves without committing. Reopening reloads
            // from saved, so leaving really does discard.
            Button(page.transform, "Apply", AccentColour, panel, nameof(SettingsPanel.Apply));
            Button(page.transform, "Back", ButtonColour, menu, nameof(PauseMenu.ShowMain));

            var so = new SerializedObject(panel);
            so.FindProperty("resolutionDropdown").objectReferenceValue = resolution;
            so.FindProperty("fullscreenToggle").objectReferenceValue = fullscreen;
            so.FindProperty("qualitySlider").objectReferenceValue = quality;
            so.FindProperty("qualityValue").objectReferenceValue = qualityValue;
            so.FindProperty("sensitivitySlider").objectReferenceValue = sensitivity;
            so.FindProperty("sensitivityValue").objectReferenceValue = sensValue;
            so.FindProperty("volumeSlider").objectReferenceValue = volume;
            so.FindProperty("volumeValue").objectReferenceValue = volumeValue;
            so.FindProperty("moveSnapField").objectReferenceValue = moveSnap;
            so.FindProperty("rotateSnapField").objectReferenceValue = rotateSnap;
            so.FindProperty("statusLabel").objectReferenceValue = status;
            so.ApplyModifiedPropertiesWithoutUndo();

            page.SetActive(false);
            return page;
        }

        private static TextMeshProUGUI Status(Transform parent)
        {
            var go = new GameObject("Status", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = string.Empty;
            label.fontSize = 15f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(1f, 0.78f, 0.35f, 1f);

            go.AddComponent<LayoutElement>().minHeight = 22f;
            return label;
        }

        // ------------------------------------------------------------------
        // Widgets
        // ------------------------------------------------------------------

        private static GameObject Column(Transform parent, string name, float width)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, 0f);

            var layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            // Height follows the contents, so adding a row later does not need
            // the panel resizing by hand.
            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return go;
        }

        private static void Title(Transform parent, string text)
        {
            var go = new GameObject("Title", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 34f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = TextColour;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 56f;
        }

        private static void Note(Transform parent, string text)
        {
            var go = new GameObject("Note", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 14f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.62f, 0.66f, 0.72f, 1f);
            label.textWrappingMode = TextWrappingModes.Normal;

            var element = go.AddComponent<LayoutElement>();
            element.minHeight = 44f;
        }

        private static void Spacer(Transform parent, float height)
        {
            var go = new GameObject("Spacer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().minHeight = height;
        }

        private static void Button(
            Transform parent, string text, Color colour, Component target, string method)
        {
            var go = new GameObject($"Button_{text}", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var image = go.AddComponent<Image>();
            image.color = colour;

            var button = go.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(colour, Color.white, 0.22f);
            colors.pressedColor = Color.Lerp(colour, Color.black, 0.25f);
            button.colors = colors;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch(labelGo.GetComponent<RectTransform>());

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 19f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = TextColour;

            go.AddComponent<LayoutElement>().minHeight = 42f;

            // Persistent listener rather than a runtime AddListener, so the
            // wiring is visible in the Inspector and survives being saved.
            var call = System.Delegate.CreateDelegate(
                typeof(UnityEngine.Events.UnityAction), target, method)
                as UnityEngine.Events.UnityAction;

            UnityEventTools.AddPersistentListener(button.onClick, call);
        }

        /// <summary>
        /// A labelled slider with a live numeric readout.
        ///
        /// Every child sets a preferred *height* as well as a width. Inside a
        /// HorizontalLayoutGroup that controls child height, an element with
        /// only a preferred width collapses to zero pixels tall - which is why
        /// the first version's sliders, dropdown and input fields were present
        /// in the hierarchy but completely invisible.
        /// </summary>
        private static Slider SliderRow(
            Transform parent, string caption, float min, float max, bool wholeNumbers,
            out TextMeshProUGUI valueLabel)
        {
            GameObject row = Row(parent, caption, out _);

            var sliderGo = new GameObject("Slider", typeof(RectTransform));
            sliderGo.transform.SetParent(row.transform, false);

            var element = sliderGo.AddComponent<LayoutElement>();
            element.preferredWidth = 190f;
            element.preferredHeight = 20f;

            var slider = sliderGo.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;

            // Track, inset vertically so it reads as a groove rather than a bar
            // filling the whole row.
            var background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(sliderGo.transform, false);
            var backRect = background.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 0.5f);
            backRect.anchorMax = new Vector2(1f, 0.5f);
            backRect.sizeDelta = new Vector2(0f, 6f);
            backRect.anchoredPosition = Vector2.zero;
            background.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            var fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRect.sizeDelta = new Vector2(-10f, 6f);
            fillAreaRect.anchoredPosition = Vector2.zero;

            var fill = new GameObject("Fill", typeof(RectTransform));
            fill.transform.SetParent(fillArea.transform, false);
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.sizeDelta = new Vector2(10f, 0f);
            fill.AddComponent<Image>().color = AccentColour;

            // The handle is what makes it look like a slider rather than a
            // progress bar, and gives a target big enough to grab.
            var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            var handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.sizeDelta = new Vector2(-16f, 0f);
            handleAreaRect.anchoredPosition = Vector2.zero;

            var handle = new GameObject("Handle", typeof(RectTransform));
            handle.transform.SetParent(handleArea.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(16f, 20f);
            handle.AddComponent<Image>().color = new Color(0.86f, 0.89f, 0.94f, 1f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();
            slider.direction = Slider.Direction.LeftToRight;

            // Readout on the right, so a value can be set to a number rather
            // than to a position.
            var valueGo = new GameObject("Value", typeof(RectTransform));
            valueGo.transform.SetParent(row.transform, false);

            var valueElement = valueGo.AddComponent<LayoutElement>();
            valueElement.preferredWidth = 62f;
            valueElement.preferredHeight = 20f;

            valueLabel = valueGo.AddComponent<TextMeshProUGUI>();
            valueLabel.fontSize = 15f;
            valueLabel.color = new Color(0.72f, 0.78f, 0.86f, 1f);
            valueLabel.alignment = TextAlignmentOptions.Right;

            return slider;
        }

        private static TMP_Dropdown DropdownRow(Transform parent, string caption)
        {
            GameObject row = Row(parent, caption, out _);

            var go = new GameObject("Dropdown", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            var dropElement = go.AddComponent<LayoutElement>();
            dropElement.preferredWidth = 190f;
            dropElement.preferredHeight = 28f;

            go.AddComponent<Image>().color = ButtonColour;
            var dropdown = go.AddComponent<TMP_Dropdown>();

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            Stretch(labelGo.GetComponent<RectTransform>(), 8f);

            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.fontSize = 15f;
            label.color = TextColour;
            label.alignment = TextAlignmentOptions.Left;
            dropdown.captionText = label;

            // The template is what the dropdown clones for its option list.
            // Without one it opens to an empty box.
            GameObject template = BuildDropdownTemplate(go.transform, dropdown);
            dropdown.template = template.GetComponent<RectTransform>();
            template.SetActive(false);

            return dropdown;
        }

        private static GameObject BuildDropdownTemplate(Transform parent, TMP_Dropdown dropdown)
        {
            var template = new GameObject("Template", typeof(RectTransform));
            template.transform.SetParent(parent, false);

            var rect = template.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, 150f);

            template.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);
            var scroll = template.AddComponent<ScrollRect>();

            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(template.transform, false);
            Stretch(viewport.GetComponent<RectTransform>());
            viewport.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 28f);

            var item = new GameObject("Item", typeof(RectTransform));
            item.transform.SetParent(content.transform, false);
            var itemRect = item.GetComponent<RectTransform>();
            itemRect.anchorMin = new Vector2(0f, 0.5f);
            itemRect.anchorMax = new Vector2(1f, 0.5f);
            itemRect.sizeDelta = new Vector2(0f, 26f);

            var itemToggle = item.AddComponent<Toggle>();

            var itemBackground = new GameObject("Item Background", typeof(RectTransform));
            itemBackground.transform.SetParent(item.transform, false);
            Stretch(itemBackground.GetComponent<RectTransform>());
            itemBackground.AddComponent<Image>().color = ButtonColour;
            itemToggle.targetGraphic = itemBackground.GetComponent<Image>();

            var itemLabelGo = new GameObject("Item Label", typeof(RectTransform));
            itemLabelGo.transform.SetParent(item.transform, false);
            Stretch(itemLabelGo.GetComponent<RectTransform>(), 8f);

            var itemLabel = itemLabelGo.AddComponent<TextMeshProUGUI>();
            itemLabel.fontSize = 15f;
            itemLabel.color = TextColour;
            itemLabel.alignment = TextAlignmentOptions.Left;

            scroll.content = contentRect;
            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.horizontal = false;

            dropdown.itemText = itemLabel;
            return template;
        }

        private static Toggle ToggleRow(Transform parent, string caption)
        {
            GameObject row = Row(parent, caption, out _);

            var go = new GameObject("Toggle", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);

            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = 32f;
            element.preferredHeight = 26f;

            var background = go.AddComponent<Image>();
            background.color = new Color(0.10f, 0.11f, 0.13f, 1f);

            var toggle = go.AddComponent<Toggle>();
            toggle.targetGraphic = background;

            var checkGo = new GameObject("Checkmark", typeof(RectTransform));
            checkGo.transform.SetParent(go.transform, false);
            Stretch(checkGo.GetComponent<RectTransform>(), 5f);
            toggle.graphic = checkGo.AddComponent<Image>();
            ((Image)toggle.graphic).color = AccentColour;

            return toggle;
        }

        private static TMP_InputField InputRow(Transform parent, string caption)
        {
            GameObject row = Row(parent, caption, out _);

            var go = new GameObject("Input", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            var inputElement = go.AddComponent<LayoutElement>();
            inputElement.preferredWidth = 110f;
            inputElement.preferredHeight = 28f;
            go.AddComponent<Image>().color = new Color(0.10f, 0.11f, 0.13f, 1f);

            var field = go.AddComponent<TMP_InputField>();

            var area = new GameObject("Text Area", typeof(RectTransform));
            area.transform.SetParent(go.transform, false);
            Stretch(area.GetComponent<RectTransform>(), 6f);
            area.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(area.transform, false);
            Stretch(textGo.GetComponent<RectTransform>());

            var text = textGo.AddComponent<TextMeshProUGUI>();
            text.fontSize = 15f;
            text.color = TextColour;

            field.textViewport = area.GetComponent<RectTransform>();
            field.textComponent = text;
            field.contentType = TMP_InputField.ContentType.DecimalNumber;

            return field;
        }

        private static GameObject Row(
            Transform parent, string caption, out TextMeshProUGUI captionLabel)
        {
            var row = new GameObject($"Row_{caption}", typeof(RectTransform));
            row.transform.SetParent(parent, false);

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            row.AddComponent<LayoutElement>().minHeight = 34f;

            var labelGo = new GameObject("Caption", typeof(RectTransform));
            labelGo.transform.SetParent(row.transform, false);
            var captionElement = labelGo.AddComponent<LayoutElement>();
            captionElement.preferredWidth = 190f;
            captionElement.preferredHeight = 24f;

            captionLabel = labelGo.AddComponent<TextMeshProUGUI>();
            captionLabel.text = caption;
            captionLabel.fontSize = 16f;
            captionLabel.color = TextColour;
            captionLabel.alignment = TextAlignmentOptions.Left;

            return row;
        }

        private static GameObject Panel(Transform parent, string name, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = colour;
            return go;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
