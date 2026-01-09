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

        void Awake()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (_isInitialized) return;

            // Header is child of Window
            if (transform.parent != null)
            {
                _windowTransform = transform.parent.GetComponent<RectTransform>();
            }

            if (_windowTransform == null)
            {
                Debug.LogWarning($"[DraggableWindow] Could not find parent RectTransform on {gameObject.name}");
                _windowTransform = GetComponent<RectTransform>(); // Fallback to self
            }

            _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                Debug.LogWarning($"[DraggableWindow] Could not find parent Canvas on {gameObject.name}");
            }

            _isInitialized = true;
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
