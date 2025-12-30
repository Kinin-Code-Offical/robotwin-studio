using UnityEngine;
using UnityEngine.UIElements;

namespace RobotTwin.UI
{
    public static class UiResponsive
    {
        public static void Bind(
            VisualElement root,
            float compactMax,
            float mediumMax,
            string compactClass,
            string mediumClass,
            string wideClass)
        {
            if (root == null) return;

            void Apply()
            {
                float width = root.resolvedStyle.width;
                if (width <= 0)
                {
                    width = Screen.width;
                }

                string nextClass = width < compactMax
                    ? compactClass
                    : width < mediumMax
                        ? mediumClass
                        : wideClass;

                if (string.IsNullOrWhiteSpace(nextClass)) return;
                if (root.ClassListContains(nextClass)) return;

                if (!string.IsNullOrWhiteSpace(compactClass)) root.RemoveFromClassList(compactClass);
                if (!string.IsNullOrWhiteSpace(mediumClass)) root.RemoveFromClassList(mediumClass);
                if (!string.IsNullOrWhiteSpace(wideClass)) root.RemoveFromClassList(wideClass);

                root.AddToClassList(nextClass);
            }

            Apply();
            root.RegisterCallback<GeometryChangedEvent>(_ => Apply());
        }
    }
}
