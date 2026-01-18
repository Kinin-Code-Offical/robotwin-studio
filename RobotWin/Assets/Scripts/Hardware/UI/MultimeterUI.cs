using UnityEngine;
using UnityEngine.UI;
using RobotWin.Hardware.Instruments;

namespace RobotWin.Hardware.UI
{
    /// <summary>
    /// Controls the floating UI for the Digital Multimeter.
    /// Simulates a 7-segment display behavior.
    /// </summary>
    public class MultimeterUI : MonoBehaviour
    {
        [Header("Backend Reference")]
        public DigitalMultimeter sourceInstrument;

        [Header("Window")]
        public RectTransform windowRoot;
        public RectTransform contentRoot;
        public Text titleText;
        public Button closeButton;

        [Header("UI Controls")]
        public Text lcdText; // The main readout
        public Text unitText; // 'mV', 'V', 'Ohm'
        public Image modeIndicator; // Visual dial position (optional)
        public InputField boardField;
        public InputField pinAField;
        public InputField pinBField;
        public Button applyButton;

        private bool _closeHooked;

        public void SetSource(DigitalMultimeter source)
        {
            sourceInstrument = source;
            EnsureUI();
            SyncInputsFromSource();
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

        void OnDestroy()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
            }
            if (applyButton != null)
            {
                applyButton.onClick.RemoveListener(ApplySimPinsFromUi);
            }
        }

        void Update()
        {
            if (sourceInstrument != null && lcdText != null)
            {
                lcdText.text = sourceInstrument.GetValueString();
                if (unitText != null)
                {
                    unitText.text = sourceInstrument.GetUnitString();
                }
            }
        }

        private void EnsureUI()
        {
            var root = GetUiRoot();
            EnsureRootLayout(root);

            if (lcdText == null || unitText == null)
            {
                CreateDisplay(root);
            }

            if (boardField == null || pinAField == null || pinBField == null || applyButton == null)
            {
                CreateConfigInputs(root);
            }

            if (applyButton != null)
            {
                applyButton.onClick.RemoveListener(ApplySimPinsFromUi);
                applyButton.onClick.AddListener(ApplySimPinsFromUi);
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

        private void CreateDisplay(Transform root)
        {
            var panel = CreatePanel(root, "DisplayPanel", new Color(0.04f, 0.08f, 0.05f, 0.95f), 56f);
            var layout = panel.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;

            var lcdObj = new GameObject("RawLCD");
            lcdObj.transform.SetParent(panel.transform, false);
            lcdText = lcdObj.AddComponent<Text>();
            lcdText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            lcdText.fontSize = 26;
            lcdText.color = new Color(0.6f, 1f, 0.7f, 0.95f);
            lcdText.alignment = TextAnchor.MiddleRight;
            lcdText.text = "0.000";
            var lcdLayout = lcdObj.AddComponent<LayoutElement>();
            lcdLayout.flexibleWidth = 1f;
            lcdLayout.preferredWidth = 140f;

            var unitObj = new GameObject("UnitLCD");
            unitObj.transform.SetParent(panel.transform, false);
            unitText = unitObj.AddComponent<Text>();
            unitText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            unitText.fontSize = 14;
            unitText.color = new Color(0.6f, 1f, 0.7f, 0.95f);
            unitText.alignment = TextAnchor.MiddleLeft;
            unitText.text = "V";
            var unitLayout = unitObj.AddComponent<LayoutElement>();
            unitLayout.preferredWidth = 56f;

            var indicatorObj = new GameObject("ModeIndicator");
            indicatorObj.transform.SetParent(panel.transform, false);
            modeIndicator = indicatorObj.AddComponent<Image>();
            modeIndicator.color = new Color(0.2f, 0.65f, 0.25f, 0.95f);
            var indicatorLayout = indicatorObj.AddComponent<LayoutElement>();
            indicatorLayout.preferredWidth = 12f;
            indicatorLayout.preferredHeight = 12f;
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
            pinAField = CreateLabeledInput(configPanel.transform, "Pin A", "D13");
            pinBField = CreateLabeledInput(configPanel.transform, "Pin B", "GND");

            applyButton = CreateButton(configPanel.transform, "Apply");
            applyButton.onClick.AddListener(ApplySimPinsFromUi);
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
            labelText.color = new Color(0.7f, 0.85f, 0.7f, 1f);
            labelText.text = label;
            var labelLayout = labelObj.AddComponent<LayoutElement>();
            labelLayout.preferredWidth = 52;

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
            image.color = new Color(0.2f, 0.3f, 0.2f, 0.9f);
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

        private void ApplySimPinsFromUi()
        {
            if (sourceInstrument == null) return;
            sourceInstrument.SetSimPins(
                boardField != null ? boardField.text : string.Empty,
                pinAField != null ? pinAField.text : string.Empty,
                pinBField != null ? pinBField.text : string.Empty);
        }

        private void SyncInputsFromSource()
        {
            if (sourceInstrument == null) return;
            if (boardField != null) boardField.SetTextWithoutNotify(sourceInstrument.simBoardId);
            if (pinAField != null) pinAField.SetTextWithoutNotify(sourceInstrument.simPinA);
            if (pinBField != null) pinBField.SetTextWithoutNotify(sourceInstrument.simPinB);
        }
    }
}
