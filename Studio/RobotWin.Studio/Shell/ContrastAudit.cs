using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [theme] Finds text that cannot be read: every visible text of a window against what lies behind it (the solid
    /// backgrounds of its ancestors, the translucent ones mixed over the opaque one under them), as WCAG contrast. Text
    /// that falls back to WPF's default black (an element made in code outside the themed tree, a run with its own
    /// colour) shows up here on the dark themes; light-on-light on the light ones. --contrast-report writes it.
    /// </summary>
    public static class ContrastAudit
    {
        /// <summary>Below this, text is hard to read even when large (WCAG's minimum for large text and controls).</summary>
        public const double Minimum = 3.0;

        public sealed record Finding(string Text, double Ratio, Color Fore, Color Back, string Where);

        public static List<Finding> Find(DependencyObject root, Color fallback)
        {
            var found = new List<Finding>();
            var seen = new HashSet<DependencyObject>();
            Walk(root, fallback, found, seen);
            return found;
        }

        private static void Walk(DependencyObject d, Color fallback, List<Finding> found, HashSet<DependencyObject> seen)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && !u.IsVisible) return;
            switch (d)
            {
                case TextBlock tb:
                    Check(tb, tb.Text, tb.Foreground, fallback, found);
                    foreach (var run in Runs(tb.Inlines))
                        if (run.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue)
                            Check(tb, run.Text, run.Foreground, fallback, found);
                    break;
                case TextBox box:
                    Check(box, box.Text, box.Foreground, fallback, found);
                    break;
            }

            if (d is Visual || d is System.Windows.Media.Media3D.Visual3D)
            {
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) Walk(VisualTreeHelper.GetChild(d, i), fallback, found, seen);
            }
        }

        private static IEnumerable<Run> Runs(InlineCollection inlines)
        {
            foreach (var inline in inlines.ToList())
            {
                if (inline is Run r) yield return r;
                else if (inline is Span s)
                    foreach (var inner in Runs(s.Inlines))
                        yield return inner;
            }
        }

        private static void Check(FrameworkElement e, string text, Brush? fore, Color fallback, List<Finding> found)
        {
            if (string.IsNullOrWhiteSpace(text) || !text.Any(char.IsLetterOrDigit)) return;
            if (!e.IsEnabled || e.ActualWidth < 1 || e.ActualHeight < 1) return;   // a disabled control is dimmed on purpose
            if (fore is not SolidColorBrush fs) return;
            if (fs.Color.A * fs.Opacity < 13) return;   // invisible on purpose (the code editor's input layer under its drawn text)
            if (Opacity(e) < 0.35) return;   // faded out (hints, empty placeholders)
            var back = Behind(e, fallback);
            if (back == null) return;   // over a picture, a gradient or a 3D view: not judged
            var fc = Over(fs.Color, fs.Opacity, back.Value);
            double ratio = Ratio(fc, back.Value);
            if (ratio >= Minimum) return;
            found.Add(new Finding(text.Length > 60 ? text.Substring(0, 60) + "…" : text, ratio, fc, back.Value, Path(e)));
        }

        private static double Opacity(DependencyObject d)
        {
            double o = 1;
            for (var p = d; p != null; p = Parent(p))
                if (p is UIElement u)
                    o *= u.Opacity;
            return o;
        }

        /// <summary>
        /// What the text is drawn on: the ancestors' solid backgrounds, mixed down to the first opaque one. Only what is
        /// drawn counts: a panel's or border's background (a control's own Background shows only through its template's
        /// border, which is in the tree too).
        /// </summary>
        private static Color? Behind(DependencyObject e, Color fallback)
        {
            var layers = new List<(Color C, double A)>();
            DependencyObject? child = null;
            for (var p = e; p != null; child = p, p = Parent(p))
            {
                // A shape drawn under it in the same panel (a letter on a filled dot): that fill is what it stands on.
                if (p is Panel panel && child is UIElement onPath && e is UIElement text && Under(panel, onPath, text) is { } fill)
                {
                    layers.Add(fill);
                    if (fill.A >= 0.98) break;
                }

                Brush? b = p switch
                {
                    TextBlock t => t.Background,
                    Panel x => x.Background,
                    Border x => x.Background,
                    _ => null
                };
                if (b == null) continue;
                if (b is not SolidColorBrush s) return null;
                double a = s.Color.A / 255.0 * s.Opacity;
                if (a < 0.02) continue;
                layers.Add((s.Color, a));
                if (a >= 0.98) break;
            }

            var c = fallback;
            for (int i = layers.Count - 1; i >= 0; i--) c = Over(layers[i].C, layers[i].A, c);
            return c;
        }

        /// <summary>
        /// What an element drawn before <paramref name="onPath"/> in <paramref name="panel"/> paints under the text's
        /// middle: a filled shape (a letter on a dot) or a sibling's background (a combo box's button under its text).
        /// </summary>
        private static (Color C, double A)? Under(Panel panel, UIElement onPath, UIElement text)
        {
            Point mid;
            try
            {
                mid = text.TransformToAncestor(panel).Transform(new Point(text.RenderSize.Width / 2, text.RenderSize.Height / 2));
            }
            catch (InvalidOperationException)
            {
                return null;   // not in the same tree (a popup)
            }

            int at = panel.Children.IndexOf(onPath);
            for (int i = at - 1; i >= 0; i--)
                if (panel.Children[i] is UIElement sibling && Paint(sibling, panel, mid, 0) is { } fill)
                    return fill;
            return null;
        }

        /// <summary>The topmost solid paint of <paramref name="e"/>'s tree at <paramref name="at"/> (in <paramref name="panel"/>'s frame).</summary>
        private static (Color C, double A)? Paint(UIElement e, Visual panel, Point at, int depth)
        {
            if (!e.IsVisible || depth > 6) return null;
            Rect box;
            try
            {
                box = e.TransformToAncestor(panel).TransformBounds(new Rect(e.RenderSize));
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (!box.Contains(at)) return null;
            int n = VisualTreeHelper.GetChildrenCount(e);
            for (int i = n - 1; i >= 0; i--)
                if (VisualTreeHelper.GetChild(e, i) is UIElement c && Paint(c, panel, at, depth + 1) is { } inner && inner.A >= 0.98)
                    return inner;
            Brush? own = e switch
            {
                Shape s => s.Fill,
                Border x => x.Background,
                Panel x => x.Background,
                _ => null
            };
            if (own is SolidColorBrush sb && sb.Color.A * sb.Opacity > 250) return (sb.Color, 1.0);
            return null;
        }

        private static DependencyObject? Parent(DependencyObject d) =>
            d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        private static Color Over(Color top, double alpha, Color under)
        {
            alpha *= top.A / 255.0;
            byte Mix(byte t, byte u) => (byte)Math.Round(t * alpha + u * (1 - alpha));
            return Color.FromRgb(Mix(top.R, under.R), Mix(top.G, under.G), Mix(top.B, under.B));
        }

        /// <summary>WCAG 2 contrast ratio of two colours, 1 … 21.</summary>
        public static double Ratio(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static string Path(DependencyObject e)
        {
            var parts = new List<string>();
            for (var p = e; p != null && parts.Count < 6; p = Parent(p))
            {
                if (p is FrameworkElement f && !string.IsNullOrEmpty(f.Name)) parts.Add(f.GetType().Name + "#" + f.Name);
                else if (p is UserControl || p is Page || p is Window) parts.Add(p.GetType().Name);
            }

            if (parts.Count == 0) parts.Add(e.GetType().Name);
            return string.Join(" < ", parts);
        }

        /// <summary>One line per finding, for --contrast-report.</summary>
        public static string Line(Finding f, string page, string theme) =>
            string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2:0.0}\t#{3:X2}{4:X2}{5:X2} on #{6:X2}{7:X2}{8:X2}\t{9}\t{10}",
                page, theme, f.Ratio, f.Fore.R, f.Fore.G, f.Fore.B, f.Back.R, f.Back.G, f.Back.B, f.Where, f.Text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));
    }
}
