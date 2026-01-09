using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace RobotTwin.UI
{
    internal static class SearchFieldHelpers
    {
        private const string FocusWithinClass = "rw-focus-within";
        private const string FocusWithinHookedClass = "rw-focus-within--hooked";

        /// <summary>
        /// Configures a TextField to behave like a "hint" search box:
        /// - If empty, shows <paramref name="hintText"/> as the field value.
        /// - On focus/click, selects all (so typing replaces instantly).
        /// - On blur, restores hint if left empty.
        ///
        /// IMPORTANT: consumers should use <see cref="GetEffectiveQuery"/> when filtering.
        /// </summary>
        public static void SetupHint(TextField field, string hintText)
        {
            if (field == null) return;
            if (string.IsNullOrWhiteSpace(hintText)) hintText = string.Empty;

            Action ensureHintVisible = () =>
            {
                if (field == null) return;
                if (string.IsNullOrWhiteSpace(field.value))
                {
                    field.SetValueWithoutNotify(hintText);
                    field.AddToClassList("rw-hint");
                }
            };

            Action<string> syncHintStyling = value =>
            {
                if (field == null) return;
                if (string.Equals(value ?? string.Empty, hintText, StringComparison.Ordinal))
                {
                    field.AddToClassList("rw-hint");
                }
                else
                {
                    field.RemoveFromClassList("rw-hint");
                }
            };

            ensureHintVisible();

            // Selecting in FocusInEvent can be flaky depending on frame timing; schedule makes it reliable.
            field.RegisterCallback<FocusInEvent>(_ =>
            {
                field.schedule.Execute(() =>
                {
                    if (field == null) return;
                    field.SelectAll();
                });
            });

            // Make pointer click behave like "select all" as well (helps on first click).
            field.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt == null) return;
                if (evt.button != 0) return;
                field.schedule.Execute(() =>
                {
                    if (field == null) return;
                    field.Focus();
                    field.SelectAll();
                });
            });

            field.RegisterValueChangedCallback(evt =>
            {
                syncHintStyling(evt?.newValue);
            });

            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                ensureHintVisible();
                syncHintStyling(field.value);
            });

            // Initialize styling.
            syncHintStyling(field.value);
        }

        public static string GetEffectiveQuery(TextField field, string hintText)
        {
            if (field == null) return string.Empty;
            string v = field.value ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(hintText) && string.Equals(v, hintText, StringComparison.Ordinal))
            {
                return string.Empty;
            }
            return v.Trim();
        }

        /// <summary>
        /// Simple behavior: whenever focused, select all current text.
        /// Use this for "real" inputs (not hint-as-value).
        /// </summary>
        public static void SetupSelectAllOnFocus(TextField field)
        {
            if (field == null) return;
            field.RegisterCallback<FocusInEvent>(_ => field.schedule.Execute(() => field.SelectAll()));
            field.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt == null) return;
                if (evt.button != 0) return;
                field.schedule.Execute(() =>
                {
                    field.Focus();
                    field.SelectAll();
                });
            });
        }

        public static void ApplyToSearchFields(VisualElement root)
        {
            if (root == null) return;

            // Generic: any TextField with class "search-field" gets select-all-on-focus.
            // Enumerate directly to avoid allocation
            var searchFieldQuery = root.Query<TextField>(className: "search-field");
            searchFieldQuery.ForEach(tf =>
            {
                if (tf != null)
                {
                    SetupSelectAllOnFocus(tf);
                }
            });

            // UI Toolkit versions vary in supported pseudo-classes. We avoid relying on :focus-within
            // by explicitly toggling a class on the common search row container when any descendant
            // gains/loses focus.
            var searchRowQuery = root.Query<VisualElement>(className: "rw-search-row");
            searchRowQuery.ForEach(row =>
            {
                if (row == null) return;
                if (row.ClassListContains(FocusWithinHookedClass)) return;

                row.AddToClassList(FocusWithinHookedClass);

                row.RegisterCallback<FocusInEvent>(_ =>
                {
                    // FocusIn bubbles, so the row sees focus on any descendant.
                    row.AddToClassList(FocusWithinClass);
                });

                row.RegisterCallback<FocusOutEvent>(_ =>
                {
                    // Focus transitions inside the row can emit FocusOut before the next FocusIn.
                    // Schedule a check so we only remove when focus truly left the row subtree.
                    row.schedule.Execute(() =>
                    {
                        var focused = row.panel?.focusController?.focusedElement as VisualElement;
                        if (focused == null || !row.Contains(focused))
                        {
                            row.RemoveFromClassList(FocusWithinClass);
                        }
                    });
                });
            });
        }
    }
}
