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
    /// [layout] What breaks when the window is small or the display scale large (--layout-report, D2 "Erişilebilirlik ve
    /// DPI"): a control the user acts on (a button, a toggle, a box, a list) that is cut off by the window or by a clipping
    /// panel, or that lies over another one. At 150 % a 1920 × 1080 screen gives the app 1280 × 720 device-independent
    /// pixels, at 200 % 960 × 540: the same pages must still be usable there. Controls inside a scrolling area are fine
    /// when they scroll out of view; parts of a control's own template (a combo box's arrow) are not counted apart.
    /// </summary>
    public static class LayoutAudit
    {
        /// <summary>Cut off by more than this (device-independent pixels) is a finding.</summary>
        public const double Tolerance = 2.0;

        public sealed record Finding(string Kind, string What, string Where, string Detail);

        public static List<Finding> Find(Visual root)
        {
            var controls = new List<(FrameworkElement Control, Rect Box)>();
            Collect(root, root, controls, new HashSet<DependencyObject>());
            var found = new List<Finding>();
            var window = new Rect(0, 0, (root as FrameworkElement)?.ActualWidth ?? 0, (root as FrameworkElement)?.ActualHeight ?? 0);
            foreach (var (c, box) in controls)
            {
                // cut off by the window
                if (window.Width > 0 && (box.Right > window.Right + Tolerance || box.Bottom > window.Bottom + Tolerance || box.Left < -Tolerance || box.Top < -Tolerance))
                {
                    found.Add(new Finding("pencere dışında", Name(c), Path(c), string.Format(CultureInfo.InvariantCulture, "{0:0} × {1:0} @ ({2:0}, {3:0}), pencere {4:0} × {5:0}", box.Width, box.Height, box.X, box.Y, window.Width, window.Height)));
                    continue;
                }

                // cut off by a clipping panel that does not scroll
                if (ClippedBy(c, root, box) is { } clip)
                    found.Add(new Finding("kırpılmış", Name(c), Path(c), string.Format(CultureInfo.InvariantCulture, "{0:0} px dışarıda ({1})", clip.Off, clip.By)));
            }

            // over another one
            for (int i = 0; i < controls.Count; i++)
            {
                for (int j = i + 1; j < controls.Count; j++)
                {
                    var (a, ra) = controls[i];
                    var (b, rb) = controls[j];
                    if (IsAncestor(a, b) || IsAncestor(b, a)) continue;
                    var overlap = Rect.Intersect(ra, rb);
                    if (overlap.IsEmpty) continue;
                    double smaller = Math.Min(ra.Width * ra.Height, rb.Width * rb.Height);
                    if (smaller <= 0 || overlap.Width * overlap.Height < 0.25 * smaller) continue;
                    found.Add(new Finding("üst üste", Name(a) + " ↔ " + Name(b), Path(a), string.Format(CultureInfo.InvariantCulture, "{0:0} × {1:0} px kesişiyor", overlap.Width, overlap.Height)));
                }
            }

            return found;
        }

        public static string Line(Finding f, string page, string size) =>
            string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3}\t{4}\t{5}", page, size, f.Kind, f.What, f.Where, f.Detail);

        private static void Collect(DependencyObject d, Visual root, List<(FrameworkElement, Rect)> into, HashSet<DependencyObject> seen)
        {
            if (!seen.Add(d)) return;
            if (d is UIElement u && (!u.IsVisible || u.Opacity <= 0.01)) return;
            if (d is FrameworkElement fe && IsActionable(fe) && fe.ActualWidth > 1 && fe.ActualHeight > 1)
            {
                try
                {
                    var box = fe.TransformToAncestor(root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
                    // what can be seen of it: a scrolling area shows only its viewport (scrolled out is fine, not cut off)
                    var visible = Visible(fe, root, box);
                    if (!visible.IsEmpty && visible.Width > 1 && visible.Height > 1) into.Add((fe, visible));
                }
                catch (InvalidOperationException)
                {
                    // not under this root (a popup's own tree): left to its own window's pass
                }

                if (fe is ComboBox or TextBoxBase or Slider or ScrollBar) return;   // their template's parts are theirs
            }

            if (d is Visual)
            {
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) Collect(VisualTreeHelper.GetChild(d, i), root, into, seen);
            }
        }

        /// <summary>The part of a control inside every scrolling viewport above it (empty: scrolled out of view).</summary>
        private static Rect Visible(FrameworkElement c, Visual root, Rect box)
        {
            for (DependencyObject? p = VisualTreeHelper.GetParent(c); p != null && !ReferenceEquals(p, root); p = VisualTreeHelper.GetParent(p))
            {
                if (p is not ScrollContentPresenter sp || sp.ActualWidth <= 0) continue;
                try
                {
                    box.Intersect(sp.TransformToAncestor(root).TransformBounds(new Rect(0, 0, sp.ActualWidth, sp.ActualHeight)));
                }
                catch (InvalidOperationException)
                {
                    return Rect.Empty;
                }

                if (box.IsEmpty) return box;
            }

            return box;
        }

        /// <summary>What a user clicks or types into, and not a part of another control's template.</summary>
        private static bool IsActionable(FrameworkElement e) =>
            e.TemplatedParent == null && e is ButtonBase or ComboBox or TextBoxBase or Slider
            && !(e is ButtonBase && e.TemplatedParent is Control);

        /// <summary>The first clipping ancestor (not a scrolling one) that cuts the control off, and by how much.</summary>
        private static (double Off, string By)? ClippedBy(FrameworkElement c, Visual root, Rect box)
        {
            for (DependencyObject? p = VisualTreeHelper.GetParent(c); p != null && !ReferenceEquals(p, root); p = VisualTreeHelper.GetParent(p))
            {
                if (p is ScrollContentPresenter || p is ScrollViewer || p is VirtualizingPanel) return null;   // it scrolls: out of view is fine
                if (p is FrameworkElement f && f.ClipToBounds && f.ActualWidth > 0 && f.ActualHeight > 0)
                {
                    Rect area;
                    try
                    {
                        area = f.TransformToAncestor(root).TransformBounds(new Rect(0, 0, f.ActualWidth, f.ActualHeight));
                    }
                    catch (InvalidOperationException)
                    {
                        return null;
                    }

                    double off = Math.Max(Math.Max(area.Left - box.Left, box.Right - area.Right), Math.Max(area.Top - box.Top, box.Bottom - area.Bottom));
                    if (off > Tolerance) return (off, Path(f));
                }
            }

            return null;
        }

        private static bool IsAncestor(DependencyObject a, DependencyObject b)
        {
            for (var p = VisualTreeHelper.GetParent(b); p != null; p = VisualTreeHelper.GetParent(p))
                if (ReferenceEquals(p, a)) return true;
            return false;
        }

        private static string Name(FrameworkElement e)
        {
            if (!string.IsNullOrEmpty(e.Name)) return e.GetType().Name + "#" + e.Name;
            if (e.ToolTip is string tip && tip.Length > 0) return e.GetType().Name + " \"" + Short(tip) + "\"";
            if (e is ContentControl { Content: string text }) return e.GetType().Name + " \"" + Short(text) + "\"";
            return e.GetType().Name;
        }

        private static string Short(string s)
        {
            s = s.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
            return s.Length > 48 ? s.Substring(0, 48) + "…" : s;
        }

        private static string Path(DependencyObject e)
        {
            var parts = new List<string>();
            for (var p = VisualTreeHelper.GetParent(e); p != null && parts.Count < 4; p = VisualTreeHelper.GetParent(p))
            {
                if (p is FrameworkElement f && !string.IsNullOrEmpty(f.Name)) parts.Add(f.GetType().Name + "#" + f.Name);
                else if (p is UserControl || p is Window) parts.Add(p.GetType().Name);
            }

            return parts.Count == 0 ? "—" : string.Join(" < ", parts);
        }
    }
}
