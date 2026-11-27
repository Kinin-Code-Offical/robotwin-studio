using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [fold] An inspector whose sections fold, as Blender's panels: each section heading (a <c>Text.Overline</c> caption,
    /// or an element tagged <c>"fold:Title"</c>) gets a chevron, and a click on it folds or opens what follows down to the
    /// next heading. The pages keep building their inspectors as before; the headings are found after each build. What is
    /// folded is kept in the settings (<see cref="AppSettings.FoldedSections"/>), per page and heading, between sessions.
    /// </summary>
    public sealed class FoldingStack : StackPanel
    {
        private const string HeadTag = "fold-head";
        private readonly Dictionary<UIElement, Visibility> _hidden = new Dictionary<UIElement, Visibility>();
        private bool _pending, _applying;

        private static readonly List<WeakReference<FoldingStack>> All = new List<WeakReference<FoldingStack>>();

        public FoldingStack()
        {
            All.Add(new WeakReference<FoldingStack>(this));
        }

        /// <summary>Folds every inspector again as the settings say (the settings changed from outside: a snapshot).</summary>
        public static void ApplyAll()
        {
            All.RemoveAll(w => !w.TryGetTarget(out _));
            foreach (var w in All)
                if (w.TryGetTarget(out var stack)) stack.Apply();
        }

        /// <summary>The page's name in the stored keys ("schematic", "pcb.board").</summary>
        public string FoldKey { get; set; } = string.Empty;

        protected override void OnVisualChildrenChanged(DependencyObject? visualAdded, DependencyObject? visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            if (visualRemoved is UIElement gone) _hidden.Remove(gone);
            if (_applying || _pending) return;
            _pending = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _pending = false;
                Apply();
            }), DispatcherPriority.Loaded);   // once the page has built the whole inspector
        }

        /// <summary>Whether a section is folded (its key: the page and the heading's source text).</summary>
        public static bool IsFolded(string key) => AppSettings.Current.FoldedSections.Contains(key);

        public static void SetFolded(string key, bool folded)
        {
            var list = AppSettings.Current.FoldedSections;
            if (folded == list.Contains(key)) return;
            if (folded) list.Add(key);
            else list.Remove(key);
            AppSettings.Current.Save();
        }

        /// <summary>Puts a chevron on every heading and folds the sections kept folded.</summary>
        public void Apply()
        {
            _applying = true;
            try
            {
                var overline = TryFindResource("Text.Overline") as Style;
                for (int i = 0; i < Children.Count; i++)
                {
                    if (Children[i] is not FrameworkElement el) continue;
                    if (Equals(el.Tag, HeadTag)) continue;
                    string? title = el.Tag is string t && t.StartsWith("fold:", StringComparison.Ordinal) ? t.Substring(5)
                        : el is TextBlock tb && overline != null && ReferenceEquals(tb.Style, overline) ? tb.Text : null;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    Children.RemoveAt(i);
                    Children.Insert(i, Head(el, Lang.Source(title!) ?? title!));
                }

                string? key = null;
                foreach (UIElement child in Children.Cast<UIElement>().ToList())
                {
                    if (child is DockPanel { Tag: string tag } head && tag == HeadTag)
                    {
                        key = (string)head.DataContext;
                        if (head.Children.Count > 0 && head.Children[0] is Theme.Icon chevron) chevron.Data = Chevron(IsFolded(key));
                        continue;
                    }

                    bool fold = key != null && IsFolded(key);
                    if (fold && !_hidden.ContainsKey(child))
                    {
                        _hidden[child] = child.Visibility;
                        child.Visibility = Visibility.Collapsed;
                    }
                    else if (!fold && _hidden.TryGetValue(child, out var was))
                    {
                        _hidden.Remove(child);
                        child.Visibility = was;
                    }
                }
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>The heading with a chevron before it, the whole row a fold button; it keeps the heading's margins.</summary>
        private FrameworkElement Head(FrameworkElement heading, string title)
        {
            string key = FoldKey + ":" + title;
            bool folded = IsFolded(key);
            var margin = heading.Margin;
            heading.Margin = new Thickness(0);
            heading.VerticalAlignment = VerticalAlignment.Center;
            var chevron = new Theme.Icon { Data = Chevron(folded), Size = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
            chevron.SetResourceReference(Theme.Icon.ForegroundProperty, "B.TextMuted");
            var row = new DockPanel { Tag = HeadTag, DataContext = key, Margin = margin, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = Lang.T("Tıkla: bölümü katla / aç") };
            row.Children.Add(chevron);
            row.Children.Add(heading);
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && IsInsideControl(d, heading)) return;   // a button or a box in a heading keeps its click
                bool fold = !IsFolded(key);
                SetFolded(key, fold);
                Apply();
                e.Handled = true;
            };
            return row;
        }

        private static Geometry Chevron(bool folded) => Geometry.Parse(folded ? "M9,6 L15,12 L9,18" : "M6,9 L12,15 L18,9");

        private static bool IsInsideControl(DependencyObject d, DependencyObject root)
        {
            for (var x = d; x != null && !ReferenceEquals(x, root); x = VisualTreeHelper.GetParent(x))
                if (x is System.Windows.Controls.Primitives.ButtonBase || x is TextBox || x is ComboBox) return true;
            return false;
        }
    }
}
