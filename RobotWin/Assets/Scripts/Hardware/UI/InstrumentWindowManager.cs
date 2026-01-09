using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

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

            if (windowContainer == null)
            {
                Debug.LogError("[InstrumentWindowManager] windowContainer is not assigned.");
                return null;
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

            GameObject newWindow = null;

            if (prefab != null)
            {
                newWindow = Instantiate(prefab, windowContainer);
            }
            else
            {
                // God Tier Fallback: Generate Window Runtime
                Debug.LogWarning($"[InstrumentWindowManager] Missing Prefab for {instrumentType}. Generating Runtime Window.");
                newWindow = CreateFallbackWindow(instrumentType);
                newWindow.transform.SetParent(windowContainer, false);
            }

            if (!string.IsNullOrEmpty(title))
            {
                newWindow.name = title;
            }

            _activeWindows.Add(newWindow);
            return newWindow;
        }

        private GameObject CreateFallbackWindow(string type)
        {
            GameObject win = new GameObject(type + "_Window");
            
            // Background
            Image bg = win.AddComponent<Image>();
            bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
            
            // Dimensions
            RectTransform rt = win.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300, 200);
            
            // Draggable
            win.AddComponent<DraggableWindow>();
            
            // Specific UI Component
            if (type == "Oscilloscope")
            {
                win.AddComponent<OscilloscopeUI>(); // Will auto-gen its controls in Awake/EnsureUI
                rt.sizeDelta = new Vector2(400, 300);
            }
            else if (type == "Multimeter")
            {
                win.AddComponent<MultimeterUI>(); // Will auto-gen its controls
                rt.sizeDelta = new Vector2(250, 100);
            }
            
            return win;
        }

        public void CloseWindow(GameObject window)
        {
            if (window == null) return;

            if (_activeWindows.Remove(window))
            {
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
        }
    }
}
