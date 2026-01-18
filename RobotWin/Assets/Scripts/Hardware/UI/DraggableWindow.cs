using UnityEngine;
using UnityEngine.EventSystems;

namespace RobotWin.Hardware.UI
{
    /// <summary>
    /// Utility for creating draggable floating windows in Unity UI (Canvas).
    /// Add this to the Header/Title Bar of the Window Prefab.
    /// Optimized with null checks and proper initialization.
    /// </summary>
    public class DraggableWindow : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        private RectTransform _windowTransform;
        private Canvas _canvas;
        private bool _isInitialized;
        private bool _warned;

        void Awake()
        {
            Initialize();
        }

        void OnEnable()
        {
            if (!_isInitialized)
            {
                Initialize();
            }
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            _canvas = GetComponentInParent<Canvas>();
            _windowTransform = FindWindowRoot();
            if (!Application.isPlaying || !gameObject.scene.IsValid() || !gameObject.scene.isLoaded)
            {
                _isInitialized = true;
                return;
            }
            if (_canvas == null)
            {
                Canvas anyCanvas = null;
#if UNITY_2022_2_OR_NEWER
                anyCanvas = Object.FindFirstObjectByType<Canvas>();
#else
                anyCanvas = Object.FindObjectOfType<Canvas>();
#endif
                if (anyCanvas != null)
                {
                    _canvas = anyCanvas;
                }
            }
            if (_canvas == null || _windowTransform == null)
            {
                if (!_warned)
                {
                    if (_windowTransform == null)
                    {
                        Debug.LogWarning($"[DraggableWindow] Could not locate window root for {gameObject.name}");
                    }
                    if (_canvas == null)
                    {
                        Debug.LogWarning($"[DraggableWindow] Could not find parent Canvas on {gameObject.name}");
                    }
                    _warned = true;
                }
            }

            _isInitialized = true;
        }

        private RectTransform FindWindowRoot()
        {
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

            if (lastRect != null) return lastRect;
            return GetComponent<RectTransform>();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_isInitialized) Initialize();

            if (_windowTransform != null)
            {
                _windowTransform.SetAsLastSibling(); // Bring to front
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_isInitialized) Initialize();

            if (_canvas == null || _windowTransform == null) return;

            _windowTransform.anchoredPosition += eventData.delta / _canvas.scaleFactor;
        }
    }
}
