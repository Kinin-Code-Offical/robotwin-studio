using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotWin.Hardware.UI
{
    /// <summary>
    /// Manages the floating windows for Virtual Instruments.
    /// Ensures windows are draggable, resizable, and stay within screen bounds.
    /// Optimized to prevent memory leaks and null reference errors.
    /// </summary>
    public class InstrumentWindowManager : MonoBehaviour
    {
        public static InstrumentWindowManager Instance { get; private set; }

        [Header("Prefabs")]
        public GameObject oscilloscopeWindowPrefab;
        public GameObject multimeterWindowPrefab;
        public Transform windowContainer; // The Canvas parent

        private readonly List<GameObject> _activeWindows = new List<GameObject>();
        private readonly Dictionary<string, GameObject> _windowByType = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private int _spawnIndex;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogWarning("[InstrumentWindowManager] Duplicate instance detected. Destroying duplicate.");
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public GameObject OpenInstrumentWindow(string instrumentType, string title)
        {
            if (string.IsNullOrEmpty(instrumentType))
            {
                Debug.LogError("[InstrumentWindowManager] instrumentType is null or empty.");
                return null;
            }

            if (!EnsureWindowContainer())
            {
                Debug.LogError("[InstrumentWindowManager] windowContainer is not assigned.");
                return null;
            }

            if (_windowByType.TryGetValue(instrumentType, out var existing) && existing != null)
            {
                existing.transform.SetAsLastSibling();
                return existing;
            }

            GameObject prefab = null;
            if (instrumentType == "Oscilloscope")
            {
                prefab = oscilloscopeWindowPrefab;
            }
            else if (instrumentType == "Multimeter")
            {
                prefab = multimeterWindowPrefab;
            }

            if (prefab == null)
            {
                prefab = TryLoadPrefab(instrumentType);
            }

            GameObject newWindow = null;

            if (prefab != null)
            {
                newWindow = Instantiate(prefab, windowContainer);
            }
            else
            {
                // God Tier Fallback: Generate Window Runtime
                Debug.LogWarning($"[InstrumentWindowManager] Missing Prefab for {instrumentType}. Generating Runtime Window.");
                newWindow = CreateFallbackWindow(instrumentType, title);
            }

            if (!string.IsNullOrEmpty(title))
            {
                newWindow.name = title;
                ApplyWindowTitle(newWindow, title);
            }
            var windowRect = newWindow.GetComponent<RectTransform>();
            var scopeUi = newWindow.GetComponent<OscilloscopeUI>();
            if (scopeUi != null && scopeUi.windowRoot == null && windowRect != null)
            {
                scopeUi.windowRoot = windowRect;
            }
            var meterUi = newWindow.GetComponent<MultimeterUI>();
            if (meterUi != null && meterUi.windowRoot == null && windowRect != null)
            {
                meterUi.windowRoot = windowRect;
            }

            PositionWindow(newWindow);
            _activeWindows.Add(newWindow);
            _windowByType[instrumentType] = newWindow;
            return newWindow;
        }

        private static GameObject TryLoadPrefab(string instrumentType)
        {
            string resourcePath = null;
            if (string.Equals(instrumentType, "Oscilloscope", StringComparison.OrdinalIgnoreCase))
            {
                resourcePath = "InstrumentWindows/OscilloscopeWindow";
            }
            else if (string.Equals(instrumentType, "Multimeter", StringComparison.OrdinalIgnoreCase))
            {
                resourcePath = "InstrumentWindows/MultimeterWindow";
            }

            if (string.IsNullOrEmpty(resourcePath)) return null;
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"[InstrumentWindowManager] Missing prefab at Resources/{resourcePath}.prefab");
            }
            return prefab;
        }

        private GameObject CreateFallbackWindow(string type, string title)
        {
            GameObject win = new GameObject(type + "_Window");
            win.SetActive(false);
            win.transform.SetParent(windowContainer, false);

            var rect = win.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(360, 220);

            var bg = win.AddComponent<Image>();
            bg.color = new Color(0.07f, 0.08f, 0.1f, 0.95f);
            var outline = win.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);
            var shadow = win.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(4f, -4f);

            var layout = win.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var header = CreateHeader(win.transform, title, out var titleText, out var closeButton);
            var content = CreateContentRoot(win.transform);

            if (type == "Oscilloscope")
            {
                var ui = win.AddComponent<OscilloscopeUI>();
                ui.windowRoot = rect;
                ui.titleText = titleText;
                ui.closeButton = closeButton;
                ui.contentRoot = content;
                rect.sizeDelta = new Vector2(540, 380);
            }
            else if (type == "Multimeter")
            {
                var ui = win.AddComponent<MultimeterUI>();
                ui.windowRoot = rect;
                ui.titleText = titleText;
                ui.closeButton = closeButton;
                ui.contentRoot = content;
                rect.sizeDelta = new Vector2(360, 220);
            }

            win.SetActive(true);
            return win;
        }

        public void CloseWindow(GameObject window)
        {
            if (window == null) return;

            if (_activeWindows.Remove(window))
            {
                var typeEntry = _windowByType.FirstOrDefault(kvp => kvp.Value == window);
                if (!string.IsNullOrEmpty(typeEntry.Key))
                {
                    _windowByType.Remove(typeEntry.Key);
                }
                Destroy(window);
            }
        }

        public void CloseAll()
        {
            foreach (var w in _activeWindows)
            {
                if (w != null)
                {
                    Destroy(w);
                }
            }
            _activeWindows.Clear();
            _windowByType.Clear();
        }

        private bool EnsureWindowContainer()
        {
            if (windowContainer != null) return true;
            var bootstrap = InstrumentWindowBootstrap.Ensure();
            if (bootstrap != null && bootstrap.windowContainer != null)
            {
                windowContainer = bootstrap.windowContainer;
            }
            if (windowContainer == null)
            {
                var canvas = GetComponentInParent<Canvas>() ?? GetComponentInChildren<Canvas>();
                if (canvas != null)
                {
                    windowContainer = canvas.transform;
                }
            }
            return windowContainer != null;
        }

        private void PositionWindow(GameObject window)
        {
            if (window == null) return;
            var rect = window.GetComponent<RectTransform>();
            if (rect == null) return;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            float offset = 22f * _spawnIndex;
            _spawnIndex = (_spawnIndex + 1) % 8;
            rect.anchoredPosition = new Vector2(24f + offset, -24f - offset);
            window.transform.SetAsLastSibling();
        }

        private static void ApplyWindowTitle(GameObject window, string title)
        {
            if (window == null || string.IsNullOrWhiteSpace(title)) return;
            var labels = window.GetComponentsInChildren<Text>(true);
            foreach (var label in labels)
            {
                if (!string.Equals(label.name, "TitleText", StringComparison.OrdinalIgnoreCase)) continue;
                label.text = title;
                return;
            }
        }

        private static GameObject CreateHeader(Transform parent, string title, out Text titleText, out Button closeButton)
        {
            var header = new GameObject("Header");
            header.transform.SetParent(parent, false);
            var headerRect = header.AddComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 28f);

            var bg = header.AddComponent<Image>();
            bg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);

            var titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(header.transform, false);
            var titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.offsetMin = new Vector2(8f, 0f);
            titleRect.offsetMax = new Vector2(-40f, 0f);
            titleText = titleObj.AddComponent<Text>();
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 12;
            titleText.color = new Color(0.9f, 0.95f, 1f, 0.95f);
            titleText.text = string.IsNullOrWhiteSpace(title) ? "Instrument" : title;
            titleText.alignment = TextAnchor.MiddleLeft;

            var closeObj = new GameObject("CloseButton");
            closeObj.transform.SetParent(header.transform, false);
            var closeRect = closeObj.AddComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(26f, 20f);
            closeRect.anchoredPosition = new Vector2(-6f, 0f);
            var closeImage = closeObj.AddComponent<Image>();
            closeImage.color = new Color(0.5f, 0.1f, 0.1f, 0.9f);
            closeButton = closeObj.AddComponent<Button>();

            var closeTextObj = new GameObject("Text");
            closeTextObj.transform.SetParent(closeObj.transform, false);
            var closeTextRect = closeTextObj.AddComponent<RectTransform>();
            closeTextRect.anchorMin = Vector2.zero;
            closeTextRect.anchorMax = Vector2.one;
            closeTextRect.offsetMin = Vector2.zero;
            closeTextRect.offsetMax = Vector2.zero;
            var closeText = closeTextObj.AddComponent<Text>();
            closeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            closeText.fontSize = 12;
            closeText.color = Color.white;
            closeText.alignment = TextAnchor.MiddleCenter;
            closeText.text = "X";

            header.AddComponent<DraggableWindow>();
            return header;
        }

        private static RectTransform CreateContentRoot(Transform parent)
        {
            var content = new GameObject("Content");
            content.transform.SetParent(parent, false);
            var rect = content.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var layout = content.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rect;
        }
    }
}
