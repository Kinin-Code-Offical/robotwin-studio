using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotWin.Studio.Theme;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [overlays] The World view's tool strips on every page (the user's ask): a page's tools stand in a column on the edge
    /// of its canvas, with a ≡ handle to drag them to another edge, a corner, anywhere over the canvas or into a window of
    /// their own; a group's tools (drawing) open as a strip of their own beside it. The layout is kept per page.
    /// </summary>
    public static class PageOverlays
    {
        /// <summary>Lays an overlay layer over a page's canvas (a grid cell) with the page's overlays, in their kept layout.</summary>
        public static OverlayHost Attach(Panel over, string key, string defaultLayout, IEnumerable<SceneOverlay> overlays, int column = -1, int row = -1)
        {
            var host = new OverlayHost();
            Panel.SetZIndex(host, 6);
            if (column >= 0) Grid.SetColumn(host, column);
            if (row >= 0) Grid.SetRow(host, row);
            over.Children.Add(host);
            foreach (var o in overlays) host.Add(o);
            host.LoadLayout(defaultLayout);
            if (AppSettings.Current.OverlayLayouts.TryGetValue(key, out var kept)) host.LoadLayout(kept);
            host.Arrange();
            host.LayoutChanged += (_, _) =>
            {
                AppSettings.Current.OverlayLayouts[key] = host.SaveLayout();
                AppSettings.Current.Save();
            };
            return host;
        }

        /// <summary>A named control of the page moved into an overlay (its handlers and state stay the page's).</summary>
        public static T Take<T>(T element) where T : FrameworkElement
        {
            switch (element.Parent)
            {
                case Panel panel: panel.Children.Remove(element); break;
                case Decorator border: border.Child = null; break;
                case ContentControl content: content.Content = null; break;
            }

            element.Margin = new Thickness(1);
            return element;
        }

        /// <summary>A thin line between a strip's groups, across the strip.</summary>
        public static Border Divider(Orientation orientation)
        {
            var d = new Border { Margin = new Thickness(4) };
            if (orientation == Orientation.Vertical)
            {
                d.Height = 1;
                d.Width = 24;   // in a wrapping column it has no width of its own to stretch to
            }
            else d.Width = 1;
            d.SetResourceReference(Border.BackgroundProperty, "B.Border");
            return d;
        }

        /// <summary>A strip of the page's controls in order; a null stands for a divider.</summary>
        public static Panel Strip(Orientation orientation, IEnumerable<FrameworkElement?> items)
        {
            // [layout] a column wraps into a second one when the view is shorter than the strip (a large display scale)
            Panel p = orientation == Orientation.Vertical ? new WrapPanel { Orientation = Orientation.Vertical } : new StackPanel { Orientation = orientation };
            foreach (var item in items) p.Children.Add(item == null ? Divider(orientation) : Take(item));
            return p;
        }

        /// <summary>The controls standing in a XAML panel, in order (a thin border there is a divider: null).</summary>
        public static List<FrameworkElement?> ItemsOf(Panel panel) =>
            panel.Children.OfType<FrameworkElement>().Select(c => c is Border { Child: null } b && (b.Height <= 1 || b.Width <= 1) ? null : c).ToList();
    }

    /// <summary>
    /// [overlays] A group's tools as a strip of their own (the drawing tools: line, rectangle, circle …), opened by the
    /// group's button beside the page's tool strip instead of a menu that closes after each pick; the tool in use is lit.
    /// </summary>
    public sealed class ToolPanel
    {
        private readonly IReadOnlyList<ToolChoice> _choices;
        private readonly Func<string, bool> _active;
        private readonly Action<string> _choose;
        private readonly List<(ToggleButton Button, string Tag)> _buttons = new List<(ToggleButton, string)>();

        public ToolPanel(string id, string title, string icon, IReadOnlyList<ToolChoice> choices, Func<string, bool> active, Action<string> choose)
        {
            _choices = choices;
            _active = active;
            _choose = choose;
            Overlay = new SceneOverlay(id, title, Application.Current.TryFindResource(icon) as Geometry, Build) { Shown = false };
        }

        public SceneOverlay Overlay { get; }

        public bool IsOpen => Overlay.Shown;

        /// <summary>Opens or closes the strip.</summary>
        public void Toggle(bool? open = null)
        {
            Overlay.Shown = open ?? !Overlay.Shown;
            Overlay.Host?.Changed();
        }

        private FrameworkElement Build(Orientation orientation)
        {
            _buttons.Clear();
            var p = new StackPanel { Orientation = orientation };
            foreach (var c in _choices)
            {
                var b = new ToggleButton
                {
                    Content = new Icon { Data = Application.Current.TryFindResource(c.Icon) as Geometry, Size = 16 },
                    ToolTip = Lang.T(c.Label) + (c.Keys != null ? "  (" + c.Keys + ")" : string.Empty), Margin = new Thickness(1), IsChecked = _active(c.Tag)
                };
                b.SetResourceReference(FrameworkElement.StyleProperty, "Toggle.Tool");
                var tag = c.Tag;
                b.Click += (_, _) =>
                {
                    _choose(tag);
                    Sync();
                };
                _buttons.Add((b, tag));
                p.Children.Add(b);
            }

            return p;
        }

        /// <summary>Lights the tool in use (the page calls it when its tool changes).</summary>
        public void Sync()
        {
            foreach (var (b, tag) in _buttons) b.IsChecked = _active(tag);
        }
    }
}
