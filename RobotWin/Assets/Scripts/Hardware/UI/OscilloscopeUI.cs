using UnityEngine;
using UnityEngine.UI;
using RobotWin.Hardware.Instruments;

namespace RobotWin.Hardware.UI
{
    /// <summary>
    /// Controls the specific UI elements of the Oscilloscope Floating Window.
    /// Bridges the backend Oscilloscope data to the frontend display.
    /// </summary>
    public class OscilloscopeUI : MonoBehaviour
    {
        [Header("Backend Reference")]
        public Oscilloscope sourceInstrument;

        [Header("Window")]
        public RectTransform windowRoot;
        public RectTransform contentRoot;
        public Text titleText;
        public Button closeButton;

        [Header("UI Controls")]
        public RawImage screenDisplay;
        public Button powerBtn;
        public Text powerLabel;
        public Slider timebaseSlider;
        public Slider voltsSlider;
        public Text timebaseText;
        public Text voltsText;
        public InputField boardField;
        public InputField pinField;
        public Button applyButton;

        private bool _closeHooked;
        private bool _running = true;

        public void SetSource(Oscilloscope source)
        {
            sourceInstrument = source;
            EnsureUI();
            SyncInputsFromSource();
            BindScreenTexture();
            UpdatePowerState(sourceInstrument != null && sourceInstrument.enabled);
        }

        void Awake()
        {
            EnsureUI();
            HookCloseButton();
        }

        void OnEnable()
        {
            HookCloseButton();
        }

        void Start()
        {
            BindScreenTexture();
            SyncInputsFromSource();
        }

        void OnDestroy()
        {
            if (timebaseSlider != null)
            {
                timebaseSlider.onValueChanged.RemoveListener(OnTimebaseChanged);
            }
            if (voltsSlider != null)
            {
                voltsSlider.onValueChanged.RemoveListener(OnVoltsChanged);
            }
            if (powerBtn != null)
            {
                powerBtn.onClick.RemoveListener(ToggleRun);
            }
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
            }
            if (applyButton != null)
            {
                applyButton.onClick.RemoveListener(ApplySimPinFromUi);
            }
        }

        private void EnsureUI()
        {
            var root = GetUiRoot();
            EnsureRootLayout(root);

            if (screenDisplay == null)
            {
                CreateScreenPanel(root);
            }

            if (timebaseSlider == null || voltsSlider == null || timebaseText == null || voltsText == null || powerBtn == null)
            {
                CreateControls(root);
            }
            else if (powerLabel == null && powerBtn != null)
            {
                powerLabel = powerBtn.GetComponentInChildren<Text>(true);
            }

            if (boardField == null || pinField == null || applyButton == null)
            {
                CreateConfigInputs(root);
            }

            if (timebaseSlider != null)
            {
                timebaseSlider.onValueChanged.RemoveListener(OnTimebaseChanged);
                timebaseSlider.onValueChanged.AddListener(OnTimebaseChanged);
            }
            if (voltsSlider != null)
            {
                voltsSlider.onValueChanged.RemoveListener(OnVoltsChanged);
                voltsSlider.onValueChanged.AddListener(OnVoltsChanged);
            }
            if (powerBtn != null)
            {
                powerBtn.onClick.RemoveListener(ToggleRun);
                powerBtn.onClick.AddListener(ToggleRun);
            }
            if (applyButton != null)
            {
                applyButton.onClick.RemoveListener(ApplySimPinFromUi);
                applyButton.onClick.AddListener(ApplySimPinFromUi);
            }
        }

        private Transform GetUiRoot()
        {
            return contentRoot != null ? contentRoot : transform;
        }

        private void EnsureRootLayout(Transform root)
        {
            if (root.GetComponent<VerticalLayoutGroup>() == null)
            {
                var layout = root.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.spacing = 8;
                layout.padding = new RectOffset(8, 8, 6, 6);
                layout.childAlignment = TextAnchor.UpperLeft;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
            }

            if (root.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = root.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }

        private void CreateScreenPanel(Transform root)
        {
            var panel = CreatePanel(root, "ScreenPanel", new Color(0.04f, 0.05f, 0.07f, 0.95f), 190f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(1f, 1f);
            panelRect.pivot = new Vector2(0.5f, 1f);

            var screenObj = new GameObject("ScopeScreen");
            screenObj.transform.SetParent(panel.transform, false);
            screenDisplay = screenObj.AddComponent<RawImage>();
            screenDisplay.color = Color.black;
            var screenRect = screenObj.GetComponent<RectTransform>();
            screenRect.anchorMin = new Vector2(0f, 0f);
            screenRect.anchorMax = new Vector2(1f, 1f);
            screenRect.offsetMin = new Vector2(8f, 8f);
            screenRect.offsetMax = new Vector2(-8f, -8f);
            screenObj.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        }

        private void CreateControls(Transform root)
        {
            var row = new GameObject("ControlsRow");
            row.transform.SetParent(root, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = false;

            powerBtn = CreatePowerButton(row.transform);
            CreateSliderGroup(row.transform, "Time/div", new Color(0.2f, 0.6f, 0.85f, 0.9f), 0.0001f, 0.01f, out timebaseSlider, out timebaseText);
            CreateSliderGroup(row.transform, "Volts/div", new Color(0.4f, 0.8f, 0.4f, 0.9f), 0.1f, 5f, out voltsSlider, out voltsText);
        }

        private Button CreatePowerButton(Transform parent)
        {
            var buttonObj = new GameObject("PowerButton");
            buttonObj.transform.SetParent(parent, false);
            var image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.18f, 0.18f, 0.95f);
            var button = buttonObj.AddComponent<Button>();

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            powerLabel = textObj.AddComponent<Text>();
            powerLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            powerLabel.fontSize = 11;
            powerLabel.color = Color.white;
            powerLabel.alignment = TextAnchor.MiddleCenter;
            powerLabel.text = "RUN";

            var layout = buttonObj.AddComponent<LayoutElement>();
            layout.preferredWidth = 56;
            layout.preferredHeight = 32;

            return button;
        }

        private void CreateSliderGroup(Transform parent, string label, Color accent, float min, float max, out Slider slider, out Text valueText)
        {
            var group = new GameObject(label + "Group");
            group.transform.SetParent(parent, false);
            var groupLayout = group.AddComponent<VerticalLayoutGroup>();
            groupLayout.spacing = 2;
            groupLayout.childAlignment = TextAnchor.UpperLeft;
            groupLayout.childForceExpandHeight = false;
            groupLayout.childForceExpandWidth = true;
            var groupLayoutElement = group.AddComponent<LayoutElement>();
            groupLayoutElement.preferredWidth = 210;

            var labelObj = new GameObject("Label");
            labelObj.transform.SetParent(group.transform, false);
            var labelText = labelObj.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 11;
            labelText.color = new Color(0.78f, 0.85f, 0.95f, 0.95f);
            labelText.text = label;

            slider = CreateSlider(group.transform, accent, min, max);

            var valueObj = new GameObject("Value");
            valueObj.transform.SetParent(group.transform, false);
            valueText = valueObj.AddComponent<Text>();
            valueText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            valueText.fontSize = 11;
            valueText.color = new Color(0.7f, 0.9f, 1f, 0.9f);
            valueText.text = "--";
        }

        private Slider CreateSlider(Transform parent, Color accent, float min, float max)
        {
            var sliderObj = new GameObject("Slider");
            sliderObj.transform.SetParent(parent, false);
            var slider = sliderObj.AddComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = (min + max) * 0.5f;
            slider.wholeNumbers = false;

            var layout = sliderObj.AddComponent<LayoutElement>();
            layout.preferredHeight = 18;
            layout.preferredWidth = 190;

            var background = new GameObject("Background");
            background.transform.SetParent(sliderObj.transform, false);
            var bgImage = background.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.14f, 0.16f, 0.95f);
            var bgRect = background.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.offsetMin = new Vector2(6f, 0f);
            bgRect.offsetMax = new Vector2(-6f, 0f);

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(sliderObj.transform, false);
            var fillAreaRect = fillArea.AddComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0f, 0f);
            fillAreaRect.anchorMax = new Vector2(1f, 1f);
            fillAreaRect.offsetMin = new Vector2(6f, 0f);
            fillAreaRect.offsetMax = new Vector2(-6f, 0f);

            var fill = new GameObject("Fill");
            fill.transform.SetParent(fillArea.transform, false);
            var fillImage = fill.AddComponent<Image>();
            fillImage.color = accent;
            var fillRect = fill.GetComponent<RectTransform>();
            fillRect.anchorMin = new Vector2(0f, 0.25f);
            fillRect.anchorMax = new Vector2(1f, 0.75f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var handleArea = new GameObject("Handle Slide Area");
            handleArea.transform.SetParent(sliderObj.transform, false);
            var handleAreaRect = handleArea.AddComponent<RectTransform>();
            handleAreaRect.anchorMin = new Vector2(0f, 0f);
            handleAreaRect.anchorMax = new Vector2(1f, 1f);
            handleAreaRect.offsetMin = new Vector2(6f, 0f);
            handleAreaRect.offsetMax = new Vector2(-6f, 0f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleArea.transform, false);
            var handleImage = handle.AddComponent<Image>();
            handleImage.color = new Color(0.9f, 0.9f, 0.95f, 0.95f);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(10f, 18f);

            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;

            return slider;
        }

        private void CreateConfigInputs(Transform root)
        {
            var configPanel = CreatePanel(root, "SimConfig", new Color(0.06f, 0.07f, 0.08f, 0.9f), 0f);
            var configLayout = configPanel.AddComponent<VerticalLayoutGroup>();
            configLayout.spacing = 6;
            configLayout.childAlignment = TextAnchor.UpperLeft;
            configLayout.childForceExpandHeight = false;
            configLayout.childForceExpandWidth = true;

            boardField = CreateLabeledInput(configPanel.transform, "Board", "U1");
            pinField = CreateLabeledInput(configPanel.transform, "Pin", "A0");

            applyButton = CreateButton(configPanel.transform, "Apply");
            applyButton.onClick.AddListener(ApplySimPinFromUi);
        }

        private InputField CreateLabeledInput(Transform parent, string label, string placeholder)
        {
            var row = new GameObject(label + "Row");
            row.transform.SetParent(parent, false);
            var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childForceExpandWidth = true;

            var labelObj = new GameObject(label + "Label");
            labelObj.transform.SetParent(row.transform, false);
            var labelText = labelObj.AddComponent<Text>();
            labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            labelText.fontSize = 12;
            labelText.color = new Color(0.75f, 0.85f, 0.95f, 1f);
            labelText.text = label;
            var labelLayout = labelObj.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 50;

            var inputObj = new GameObject(label + "Field");
            inputObj.transform.SetParent(row.transform, false);
            var image = inputObj.AddComponent<Image>();
            image.color = new Color(0.08f, 0.1f, 0.12f, 0.95f);
            var input = inputObj.AddComponent<InputField>();
            input.text = string.Empty;

            var inputTextObj = new GameObject("Text");
            inputTextObj.transform.SetParent(inputObj.transform, false);
            var inputTextRect = inputTextObj.AddComponent<RectTransform>();
            inputTextRect.anchorMin = new Vector2(0f, 0f);
            inputTextRect.anchorMax = new Vector2(1f, 1f);
            inputTextRect.offsetMin = new Vector2(6f, 2f);
            inputTextRect.offsetMax = new Vector2(-6f, -2f);
            var inputText = inputTextObj.AddComponent<Text>();
            inputText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            inputText.fontSize = 12;
            inputText.color = Color.white;
            inputText.alignment = TextAnchor.MiddleLeft;
            input.textComponent = inputText;

            var placeholderObj = new GameObject("Placeholder");
            placeholderObj.transform.SetParent(inputObj.transform, false);
            var placeholderRect = placeholderObj.AddComponent<RectTransform>();
            placeholderRect.anchorMin = new Vector2(0f, 0f);
            placeholderRect.anchorMax = new Vector2(1f, 1f);
            placeholderRect.offsetMin = new Vector2(6f, 2f);
            placeholderRect.offsetMax = new Vector2(-6f, -2f);
            var placeholderText = placeholderObj.AddComponent<Text>();
            placeholderText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            placeholderText.fontSize = 11;
            placeholderText.color = new Color(0.6f, 0.7f, 0.8f, 0.6f);
            placeholderText.text = placeholder;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            input.placeholder = placeholderText;

            var inputLayout = inputObj.AddComponent<LayoutElement>();
            inputLayout.minWidth = 120;
            inputLayout.flexibleWidth = 1f;

            return input;
        }

        private Button CreateButton(Transform parent, string label)
        {
            var buttonObj = new GameObject(label + "Button");
            buttonObj.transform.SetParent(parent, false);
            var image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.25f, 0.35f, 0.9f);
            var button = buttonObj.AddComponent<Button>();

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 12;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.text = label;

            var layout = buttonObj.AddComponent<LayoutElement>();
            layout.minHeight = 24;
            layout.flexibleWidth = 1f;

            return button;
        }

        private GameObject CreatePanel(Transform parent, string name, Color color, float height)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(parent, false);
            var image = panel.AddComponent<Image>();
            image.color = color;
            var outline = panel.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.45f);
            outline.effectDistance = new Vector2(1f, -1f);
            var layout = panel.AddComponent<LayoutElement>();
            if (height > 0f)
            {
                layout.preferredHeight = height;
            }
            return panel;
        }

        private void BindScreenTexture()
        {
            if (sourceInstrument == null || screenDisplay == null) return;
            screenDisplay.texture = sourceInstrument.GetDisplayTexture();
        }

        private void ToggleRun()
        {
            UpdatePowerState(!_running);
        }

        private void UpdatePowerState(bool running)
        {
            _running = running;
            if (sourceInstrument != null)
            {
                sourceInstrument.enabled = _running;
            }
            if (powerLabel != null)
            {
                powerLabel.text = _running ? "RUN" : "HOLD";
            }
            if (powerBtn != null)
            {
                var image = powerBtn.GetComponent<Image>();
                if (image != null)
                {
                    image.color = _running
                        ? new Color(0.15f, 0.3f, 0.2f, 0.95f)
                        : new Color(0.3f, 0.15f, 0.15f, 0.95f);
                }
            }
        }

        private void HookCloseButton()
        {
            if (_closeHooked || closeButton == null) return;
            closeButton.onClick.AddListener(OnCloseClicked);
            _closeHooked = true;
        }

        private void OnCloseClicked()
        {
            var window = ResolveWindowRoot();
            if (window == null)
            {
                window = gameObject;
            }

            if (InstrumentWindowManager.Instance != null)
            {
                InstrumentWindowManager.Instance.CloseWindow(window);
            }
            else
            {
                Destroy(window);
            }
        }

        private GameObject ResolveWindowRoot()
        {
            if (windowRoot != null)
            {
                return windowRoot.gameObject;
            }

            RectTransform lastRect = null;
            Transform current = transform;
            while (current != null)
            {
                if (current.GetComponent<Canvas>() != null)
                {
                    break;
                }
                var rect = current.GetComponent<RectTransform>();
                if (rect != null)
                {
                    lastRect = rect;
                }
                current = current.parent;
            }

            return lastRect != null ? lastRect.gameObject : null;
        }

        private void OnTimebaseChanged(float val)
        {
            if (sourceInstrument != null)
            {
                sourceInstrument.timePerDiv = val;
            }
            if (timebaseText != null)
            {
                timebaseText.text = $"{val * 1000f:F2} ms/div";
            }
        }

        private void OnVoltsChanged(float val)
        {
            if (sourceInstrument != null)
            {
                sourceInstrument.voltsPerDiv = val;
            }
            if (voltsText != null)
            {
                voltsText.text = $"{val:F2} V/div";
            }
        }

        private void ApplySimPinFromUi()
        {
            if (sourceInstrument == null) return;
            sourceInstrument.SetSimPin(
                boardField != null ? boardField.text : string.Empty,
                pinField != null ? pinField.text : string.Empty);
        }

        private void SyncInputsFromSource()
        {
            if (sourceInstrument == null) return;
            if (boardField != null) boardField.SetTextWithoutNotify(sourceInstrument.simBoardId);
            if (pinField != null) pinField.SetTextWithoutNotify(sourceInstrument.simPin);
            if (timebaseSlider != null)
            {
                timebaseSlider.SetValueWithoutNotify(sourceInstrument.timePerDiv);
                OnTimebaseChanged(sourceInstrument.timePerDiv);
            }
            if (voltsSlider != null)
            {
                voltsSlider.SetValueWithoutNotify(sourceInstrument.voltsPerDiv);
                OnVoltsChanged(sourceInstrument.voltsPerDiv);
            }
        }
    }
}
