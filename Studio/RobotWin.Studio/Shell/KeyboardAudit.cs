using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [a11y] What the keyboard cannot reach: the window is walked with Tab as a user would (focus moved to the next
    /// element until it comes round again), and every visible, enabled control a mouse could use (buttons, toggles,
    /// boxes, lists, sliders, tabs) that the walk never landed on is reported, with why when it is plain (taken out of
    /// the tab order, not focusable). --keyboard-report writes it.
    /// </summary>
    public static class KeyboardAudit
    {
        public sealed record Finding(string Kind, string Text, string Why, string Where);

        public static List<Finding> Find(Window window, int limit = 3000)
        {
            var reached = Order(window, limit);
            var found = new List<Finding>();
            if (reached == null)
            {
                found.Add(new Finding("—", string.Empty, "Tab sırası okunamadı (WPF'in iç gezinme API'si bulunamadı)", "—"));
                return found;
            }

            // a list is reached through its items (WPF gives the focus to an item, the list itself is no tab stop)
            var within = new HashSet<DependencyObject>();
            foreach (var r in reached)
                for (var p = Parent(r); p != null && within.Add(p); p = Parent(p)) { }
            Walk(window, reached, within, found, new HashSet<DependencyObject>());
            if (found.Count > 0 && LastStop is { } last)
                found.Add(new Finding("—", $"{reached.Count} durak, ilki {(reached.Count > 0 ? Path(reached.First()) : "—")}", LastEnd, Path(last)));   // where the walk stopped: a trap shows here
            return found;
        }

        /// <summary>The first visible, enabled tab stop in the window's visual order.</summary>
        private static DependencyObject? FirstStop(DependencyObject d)
        {
            if (d is UIElement u && (!u.IsVisible || !u.IsEnabled)) return null;
            if (d is Control c && c.Focusable && c.IsTabStop && c.ActualWidth >= 4) return c;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
                if (FirstStop(VisualTreeHelper.GetChild(d, i)) is { } f)
                    return f;
            return null;
        }

        /// <summary>The last element the Tab walk reached, and why it stopped there.</summary>
        private static DependencyObject? LastStop { get; set; }

        private static string LastEnd { get; set; } = string.Empty;

        /// <summary>
        /// The window's Tab order as WPF itself computes it (KeyboardNavigation's own next-tab rule, read through reflection
        /// so nothing takes the focus away from what the user is doing): from the first tab stop, next after next until it
        /// comes round. Null when the rule cannot be read.
        /// </summary>
        public static HashSet<DependencyObject>? Order(Window window, int limit = 3000)
        {
            var current = typeof(KeyboardNavigation).GetProperty("Current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
            var next = current?.GetType().GetMethod("GetNextTab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null,
                new[] { typeof(DependencyObject), typeof(DependencyObject), typeof(bool) }, null);
            if (current == null || next == null) return null;
            var reached = new HashSet<DependencyObject>();
            // the walk starts where a user's first Tab does: the first tab stop in the window's own order (not wherever WPF
            // last remembered an active element, which made the walk start in a side panel now and then)
            DependencyObject? e = FirstStop(window);
            if (e != null) reached.Add(e);
            LastStop = e;
            LastEnd = "sınıra ulaştı";
            for (int i = 0; i < limit; i++)
            {
                var n = next.Invoke(current, new object?[] { e, window, false }) as DependencyObject;
                if (n == null)
                {
                    LastEnd = "Tab sırası burada bitiyor (sonraki yok)";
                    break;
                }

                if (!reached.Add(n))
                {
                    LastEnd = ReferenceEquals(n, reached.First()) ? "Tab sırası başa döndü" : "Tab sırası burada dönüyor: " + Path(n);
                    break;
                }

                e = n;
                LastStop = n;
            }

            return reached;
        }

        private static void Walk(DependencyObject d, HashSet<DependencyObject> reached, HashSet<DependencyObject> within, List<Finding> found, HashSet<DependencyObject> seen)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && (!u.IsVisible || !u.IsEnabled)) return;
            bool listReached = d is ItemsControl && within.Contains(d);
            if (d is Control c && Interactive(c) && !reached.Contains(c) && !listReached && !Inside(c, reached))
            {
                string why = !c.Focusable ? "odak alamaz (Focusable kapalı)" : !c.IsTabStop ? "Tab sırasında değil (IsTabStop kapalı)"
                    : KeyboardNavigation.GetTabNavigation(Parent(c) ?? c) == KeyboardNavigationMode.None ? "kapsayıcısı Tab'ı geçiriyor"
                    : "Tab ile ulaşılmadı";
                found.Add(new Finding(c.GetType().Name, Label(c), why, Path(c)));
            }

            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), reached, within, found, seen);
        }

        /// <summary>A control a pointer can use: its keyboard path is what is audited (scroll bars' own parts and the like are not).</summary>
        private static bool Interactive(Control c)
        {
            if (c is ScrollBar || c is Thumb || c is RepeatButton || c is GridSplitter) return false;
            if (c.TemplatedParent is ScrollBar || c.TemplatedParent is ComboBox || c.TemplatedParent is Slider || c.TemplatedParent is DatePicker) return false;   // a part of another control
            if (c.ActualWidth < 4 || c.ActualHeight < 4) return false;
            return c is ButtonBase || c is TextBox || c is ComboBox || c is Slider || c is ListBox || c is TabItem || c is PasswordBox || c is TreeView;
        }

        /// <summary>A list's or a tree's items are reached through the list itself (arrow keys move inside it).</summary>
        private static bool Inside(DependencyObject c, HashSet<DependencyObject> reached)
        {
            for (var p = Parent(c); p != null; p = Parent(p))
                if ((p is ListBox || p is TreeView || p is TabControl || p is Menu || p is ToolBar) && reached.Contains(p)) return true;
            return false;
        }

        private static DependencyObject? Parent(DependencyObject d) => VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);

        private static string Label(Control c)
        {
            string text = c switch
            {
                ContentControl cc when cc.Content is string s => s,
                ContentControl cc when cc.Content is TextBlock tb => tb.Text,
                TextBox t => t.Text,
                _ => string.Empty
            };
            if (text.Length == 0 && c.ToolTip is string tip) text = tip;
            if (text.Length == 0) text = AutomationName(c);
            return text.Length > 60 ? text.Substring(0, 60) + "…" : text;
        }

        private static string AutomationName(Control c) => System.Windows.Automation.AutomationProperties.GetName(c) ?? c.Name ?? string.Empty;

        private static string Path(DependencyObject e)
        {
            var parts = new List<string>();
            for (var p = e; p != null && parts.Count < 6; p = Parent(p))
            {
                if (p is FrameworkElement fe) parts.Add(fe.GetType().Name + (string.IsNullOrEmpty(fe.Name) ? string.Empty : "#" + fe.Name));
            }

            return string.Join(" < ", parts);
        }

        /// <summary>One line per finding, for --keyboard-report.</summary>
        public static string Line(Finding f, string page) => $"{page}\t{f.Kind}\t{f.Why}\t{f.Where}\t{f.Text}";
    }
}
