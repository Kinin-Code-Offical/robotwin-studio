using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [align] Measures what the eye catches as "not quite in line" (--align-report): inside a button, the icon's and the
    /// label's vertical centres; along a toolbar row, the controls' centres and heights, and icons of different sizes.
    /// Each finding names the page, the row or the button (its tooltip or text) and the pixels it is off by.
    /// </summary>
    public static class AlignAudit
    {
        /// <summary>Off by more than this (device-independent pixels) shows at 100 % and more.</summary>
        public const double Tolerance = 1.0;

        public sealed record Finding(string Kind, string What, double Off, string Detail);

        public static List<Finding> Find(DependencyObject root)
        {
            var found = new List<Finding>();
            Walk(root, found, new HashSet<DependencyObject>());
            return found;
        }

        public static string Line(Finding f, string page) =>
            string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2:0.0}px\t{3}\t{4}", page, f.Kind, f.Off, f.What, f.Detail);

        private static void Walk(DependencyObject d, List<Finding> found, HashSet<DependencyObject> seen)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && !u.IsVisible) return;
            if (d is ButtonBase b) CheckButton(b, found);
            if (d is Panel p && IsRow(p)) CheckRow(p, found);
            if (d is Visual)
            {
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), found, seen);
            }
        }

        // ---------------------------------------------------------------- inside a button: icon and label

        private static void CheckButton(ButtonBase b, List<Finding> found)
        {
            if (b.ActualHeight < 8 || b.Content is not DependencyObject content) return;
            var icons = Descendants(content).OfType<Theme.Icon>().Where(Shown).ToList();
            var texts = Descendants(content).OfType<TextBlock>().Where(t => Shown(t) && !string.IsNullOrWhiteSpace(t.Text)).ToList();
            if (icons.Count == 0 || texts.Count == 0) return;
            // a label under its icon (a big ribbon button) is centred across, not along
            var icon = icons[0];
            var text = texts[0];
            var ib = Bounds(icon, b);
            var tb = Bounds(text, b);
            if (ib == null || tb == null) return;
            bool beside = tb.Value.Left >= ib.Value.Right - 1 || tb.Value.Right <= ib.Value.Left + 1;
            if (!beside) return;
            // the drawn icon against the label's cap-height middle (what the eye lines up), not the two boxes
            double iconMid = ib.Value.Top + (icon.DrawnCentreY > 0 ? icon.DrawnCentreY : ib.Value.Height / 2);
            double textMid = tb.Value.Top + Theme.TextOptics.CapCentre(text);
            double off = Math.Abs(iconMid - textMid);
            if (off > 0.75)
                found.Add(new Finding("ikon-yazı", Name(b), off, $"ikon merkezi {iconMid:0.0}, yazının büyük harf ortası {textMid:0.0} (düğme {b.ActualHeight:0} px)"));
            // the button's own content off its centre (padding on one side only)
            var all = Rect.Union(ib.Value, tb.Value);
            double offCentre = Math.Abs(Mid(all) - b.ActualHeight / 2);
            if (offCentre > Tolerance + 0.5)
                found.Add(new Finding("düğme-içi", Name(b), offCentre, $"içerik merkezi {Mid(all):0.0}, düğmenin ortası {b.ActualHeight / 2:0.0}"));
        }

        // ---------------------------------------------------------------- along a row of controls

        private static bool IsRow(Panel p) =>
            p is StackPanel { Orientation: Orientation.Horizontal } || p is WrapPanel { Orientation: Orientation.Horizontal } || p is DockPanel;

        private static void CheckRow(Panel p, List<Finding> found)
        {
            var controls = p.Children.OfType<FrameworkElement>()
                .Where(c => Shown(c) && (c is ButtonBase || c is ComboBox || c is TextBox) && c.ActualHeight >= 16 && !(p is DockPanel && DockPanel.GetDock(c) is Dock.Top or Dock.Bottom))
                .ToList();
            if (controls.Count < 3) return;
            // wrapped rows: only those sharing a line with the first one
            var first = Bounds(controls[0], p);
            if (first == null) return;
            var line = controls.Select(c => (C: c, B: Bounds(c, p))).Where(x => x.B != null && Math.Abs(Mid(x.B!.Value) - Mid(first.Value)) < first.Value.Height).ToList();
            if (line.Count < 3) return;
            double median = line.Select(x => Mid(x.B!.Value)).OrderBy(v => v).ElementAt(line.Count / 2);
            foreach (var (c, r) in line)
            {
                double off = Math.Abs(Mid(r!.Value) - median);
                if (off > Tolerance) found.Add(new Finding("satır-merkez", Name(c), off, $"satır: {RowName(p)}; merkezi {Mid(r.Value):0.0}, satırın {median:0.0}"));
            }

            var buttons = line.Where(x => x.C is ButtonBase).ToList();
            var heights = buttons.Select(x => Math.Round(x.C.ActualHeight)).Distinct().OrderBy(h => h).ToList();
            if (heights.Count > 1 && heights[heights.Count - 1] - heights[0] > 2)
                found.Add(new Finding("satır-yükseklik", RowName(p), heights[heights.Count - 1] - heights[0], "düğme yükseklikleri: " + string.Join(", ", heights.Select(h => h.ToString("0", CultureInfo.InvariantCulture)))));

            var sizes = buttons.SelectMany(x => Descendants(x.C).OfType<Theme.Icon>().Where(Shown).Take(1)).Select(i => i.Size).Distinct().OrderBy(s => s).ToList();
            if (sizes.Count > 1 && sizes[sizes.Count - 1] - sizes[0] > 1.5)
                found.Add(new Finding("ikon-boyu", RowName(p), sizes[sizes.Count - 1] - sizes[0], "ikon boyları: " + string.Join(", ", sizes.Select(s => s.ToString("0.#", CultureInfo.InvariantCulture)))));
        }

        /// <summary>
        /// [align] Every icon of the theme whose drawing is not centred in its 24-unit box (its outline's middle off the box's
        /// middle): drawn centred in a button it still looks off. Off by units of the box; at 16 px one unit is ⅔ px.
        /// </summary>
        public static List<string> IconCentres(ResourceDictionary resources, double tolerance = 0.75)
        {
            var lines = new List<string>();
            foreach (var (key, g) in Geometries(resources).OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var b = g.Bounds;
                if (b.IsEmpty) continue;
                double dx = b.Left + b.Width / 2 - 12, dy = b.Top + b.Height / 2 - 12;
                if (Math.Abs(dx) > tolerance || Math.Abs(dy) > tolerance)
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "icon	{0}	dx {1:0.00}	dy {2:0.00}	bounds {3:0.#},{4:0.#} {5:0.#}x{6:0.#}", key, dx, dy, b.Left, b.Top, b.Width, b.Height));
            }

            return lines;
        }

        private static IEnumerable<(string Key, Geometry G)> Geometries(ResourceDictionary d)
        {
            foreach (var k in d.Keys)
                if (k is string s && s.StartsWith("I.", StringComparison.Ordinal) && d[k] is Geometry g) yield return (s, g);
            foreach (var m in d.MergedDictionaries)
                foreach (var x in Geometries(m)) yield return x;
        }

        // ---------------------------------------------------------------- helpers

        private static bool Shown(FrameworkElement e) => e.IsVisible && e.ActualWidth > 0.5 && e.ActualHeight > 0.5;

        private static double Mid(Rect r) => r.Top + r.Height / 2;

        private static Rect? Bounds(FrameworkElement e, Visual ancestor)
        {
            try
            {
                return e.TransformToAncestor(ancestor).TransformBounds(new Rect(e.RenderSize));
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject d)
        {
            if (d is not Visual) yield break;
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                yield return c;
                foreach (var g in Descendants(c)) yield return g;
            }
        }

        private static string Name(FrameworkElement e)
        {
            if (!string.IsNullOrEmpty(e.Name)) return e.Name;
            if (e.ToolTip is string tip) return Short(tip);
            if (e is ContentControl { Content: string s }) return Short(s);
            var text = Descendants(e).OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Text));
            return text != null ? Short(text.Text) : e.GetType().Name;
        }

        private static string RowName(Panel p)
        {
            for (DependencyObject? d = p; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is FrameworkElement { Name.Length: > 0 } fe) return fe.Name + (ReferenceEquals(fe, p) ? string.Empty : " içinde");
            return p.GetType().Name;
        }

        private static string Short(string s)
        {
            s = s.Replace('\n', ' ').Trim();
            return s.Length > 48 ? s.Substring(0, 48) + "…" : s;
        }
    }
}
