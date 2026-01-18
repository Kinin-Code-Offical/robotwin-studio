using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace RobotWin.Hardware.UI
{
    public static class InstrumentWindowBootstrap
    {
        private const string HostName = "InstrumentWindowSystem";
        private const int CanvasSortOrder = 2200;
        private const string WindowContainerName = "InstrumentWindows";

        public static InstrumentWindowManager Ensure()
        {
            var existing = InstrumentWindowManager.Instance;
            if (existing != null && existing.windowContainer != null)
            {
                return existing;
            }

            var host = GameObject.Find(HostName);
            if (host == null)
            {
                host = new GameObject(HostName);
                Object.DontDestroyOnLoad(host);
            }

            EnsureEventSystem();

            var canvas = host.GetComponentInChildren<Canvas>();
            if (canvas == null)
            {
                canvas = host.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = CanvasSortOrder;
                host.AddComponent<CanvasScaler>();
                host.AddComponent<GraphicRaycaster>();
            }

            var manager = host.GetComponent<InstrumentWindowManager>();
            if (manager == null)
            {
                manager = host.AddComponent<InstrumentWindowManager>();
            }
            manager.windowContainer = EnsureWindowContainer(canvas);
            return manager;
        }

        private static void EnsureEventSystem()
        {
            var existing = FindEventSystem();
            if (existing != null)
            {
#if ENABLE_INPUT_SYSTEM
                var legacy = existing.GetComponent<StandaloneInputModule>();
                if (legacy != null)
                {
                    Object.Destroy(legacy);
                }
                if (existing.GetComponent<InputSystemUIInputModule>() == null)
                {
                    existing.gameObject.AddComponent<InputSystemUIInputModule>();
                }
#else
                if (existing.GetComponent<StandaloneInputModule>() == null)
                {
                    existing.gameObject.AddComponent<StandaloneInputModule>();
                }
#endif
                return;
            }

            var eventSystem = new GameObject("InstrumentEventSystem");
            Object.DontDestroyOnLoad(eventSystem);
            eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif
        }

        private static Transform EnsureWindowContainer(Canvas canvas)
        {
            var existing = canvas.transform.Find(WindowContainerName);
            if (existing != null)
            {
                return existing;
            }

            var container = new GameObject(WindowContainerName);
            container.transform.SetParent(canvas.transform, false);
            var rect = container.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect.transform;
        }

        private static EventSystem FindEventSystem()
        {
#if UNITY_2022_2_OR_NEWER
            return Object.FindFirstObjectByType<EventSystem>();
#else
            return Object.FindObjectOfType<EventSystem>();
#endif
        }
    }
}
