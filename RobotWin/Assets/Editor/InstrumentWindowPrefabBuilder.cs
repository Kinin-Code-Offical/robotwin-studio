using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using RobotWin.Hardware.UI;

namespace RobotWin.EditorTools
{
    public static class InstrumentWindowPrefabBuilder
    {
        private const string FolderPath = "Assets/Resources/InstrumentWindows";
        private const string OscilloscopePrefabPath = "Assets/Resources/InstrumentWindows/OscilloscopeWindow.prefab";
        private const string MultimeterPrefabPath = "Assets/Resources/InstrumentWindows/MultimeterWindow.prefab";

        [MenuItem("RobotWin/Instrument Windows/Rebuild Prefabs")]
        public static void RebuildPrefabs()
        {
            BuildPrefabs(force: true);
        }

        [InitializeOnLoadMethod]
        private static void AutoBuildIfMissing()
        {
            EditorApplication.delayCall += () => BuildPrefabs(force: false);
        }

        private static void BuildPrefabs(bool force)
        {
            EnsureFolder();

            if (!force && File.Exists(OscilloscopePrefabPath) && File.Exists(MultimeterPrefabPath))
            {
                return;
            }

            var oscilloscopeRoot = BuildWindowRoot("OscilloscopeWindow", new Vector2(540f, 380f), "Oscilloscope");
            PrefabUtility.SaveAsPrefabAsset(oscilloscopeRoot, OscilloscopePrefabPath);
            Object.DestroyImmediate(oscilloscopeRoot);

            var multimeterRoot = BuildWindowRoot("MultimeterWindow", new Vector2(360f, 220f), "Multimeter");
            PrefabUtility.SaveAsPrefabAsset(multimeterRoot, MultimeterPrefabPath);
            Object.DestroyImmediate(multimeterRoot);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }
            if (!AssetDatabase.IsValidFolder(FolderPath))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "InstrumentWindows");
            }
        }

        private static GameObject BuildWindowRoot(string name, Vector2 size, string title)
        {
            var root = new GameObject(name);
            var rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = size;

            var image = root.AddComponent<Image>();
            image.color = new Color(0.07f, 0.08f, 0.1f, 0.95f);

            var outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);

            var shadow = root.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.4f);
            shadow.effectDistance = new Vector2(4f, -4f);

            var layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var header = CreateHeader(root.transform, title, out var titleText, out var closeButton);
            header.AddComponent<DraggableWindow>();

            var contentRoot = CreateContentRoot(root.transform);

            if (title == "Oscilloscope")
            {
                var ui = root.AddComponent<OscilloscopeUI>();
                ui.windowRoot = rect;
                ui.contentRoot = contentRoot;
                ui.titleText = titleText;
                ui.closeButton = closeButton;
            }
            else
            {
                var ui = root.AddComponent<MultimeterUI>();
                ui.windowRoot = rect;
                ui.contentRoot = contentRoot;
                ui.titleText = titleText;
                ui.closeButton = closeButton;
            }

            return root;
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
