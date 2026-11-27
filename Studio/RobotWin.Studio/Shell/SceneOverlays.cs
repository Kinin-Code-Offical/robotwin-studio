using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RobotWin.Studio.Shell
{
    /// <summary>[overlays] Where an overlay stands: floating, along an edge of the view, or in a corner.</summary>
    public enum OverlayDock
    {
        Floating,
        Top,
        Bottom,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,

        /// <summary>Torn off into a small window of its own (another screen too), as SolidWorks' floating toolbars.</summary>
        Window
    }

    /// <summary>[overlays] Unity's overlay layouts: Panel (with its label), Horizontal, Vertical.</summary>
    public enum OverlayLayout
    {
        Panel,
        Horizontal,
        Vertical
    }

    /// <summary>
    /// [overlays] One of a view's overlays, as Unity's (studied in its manual): a ≡ handle to drag it by, its content laid out
    /// as a panel (with its label), a row or a column, or collapsed to its icon and a ▾ that opens it. Right-click the handle
    /// for its layout, collapse / expand, or hide.
    /// </summary>
    public sealed class SceneOverlay : Border
    {
        public SceneOverlay(string id, string title, Geometry? icon, Func<Orientation, FrameworkElement> content)
        {
            Id = id;
            Title = title;
            IconData = icon;
            Content = content;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(6);
            Padding = new Thickness(2);
            Margin = new Thickness(4);
            SetResourceReference(BackgroundProperty, "B.Overlay");
            SetResourceReference(BorderBrushProperty, "B.BorderStrong");
            MouseDown += (_, e) => e.Handled = true;   // a click on an overlay is not a click on the scene under it
            MouseUp += (_, e) => e.Handled = true;
        }

        public string Id { get; }
        public string Title { get; }
        public Geometry? IconData { get; }
        public Func<Orientation, FrameworkElement> Content { get; }
        public OverlayDock Dock { get; set; } = OverlayDock.TopLeft;
        public OverlayLayout Layout { get; set; } = OverlayLayout.Panel;
        public bool Collapsed { get; set; }
        public bool Shown { get; set; } = true;
        public Point FloatAt { get; set; } = new Point(40, 40);

        /// <summary>Where it stood before it went to a window of its own ("Yerine koy" puts it back there).</summary>
        public OverlayDock DockBefore { get; set; } = OverlayDock.Left;

        /// <summary>False for a page's own tool strip: its menu has no "Gizle", so it cannot be lost.</summary>
        public bool CanHide { get; set; } = true;

        internal OverlayHost? Host { get; set; }
        private Window? _window;

        /// <summary>Its own window while torn off (null otherwise).</summary>
        public Window? OwnWindow => _window;

        /// <summary>Puts it in its own small window at <see cref="FloatAt"/> (screen, device-independent pixels).</summary>
        internal void ShowWindow(OverlayHost host)
        {
            if (_window == null)
            {
                (Parent as Panel)?.Children.Remove(this);
                Margin = new Thickness(0);
                _window = new Window
                {
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false,
                    ShowActivated = false, AllowsTransparency = true, Background = Brushes.Transparent, Title = Lang.T(Title), Owner = Window.GetWindow(host),
                    Left = FloatAt.X, Top = FloatAt.Y, Content = this
                };
                _window.Show();
            }
            else if (!_window.IsVisible) _window.Show();
        }

        /// <summary>Takes it out of its own window (docking back, or the page going away).</summary>
        internal void CloseWindow()
        {
            if (_window == null) return;
            var w = _window;
            _window = null;
            w.Content = null;
            w.Close();
            Margin = new Thickness(4);
        }

        internal void HideWindow() => _window?.Hide();

        /// <summary>Tears it off into its own window where it stands now on the screen.</summary>
        public void TearOff()
        {
            if (Dock == OverlayDock.Window) return;
            if (PresentationSource.FromVisual(this) is { CompositionTarget: { } target })
                FloatAt = target.TransformFromDevice.Transform(PointToScreen(new Point(0, 0)));
            DockBefore = Dock is OverlayDock.Floating or OverlayDock.Window ? OverlayDock.Left : Dock;
            Dock = OverlayDock.Window;
            Host?.Changed();
        }

        /// <summary>Puts it back where it stood before it was torn off.</summary>
        public void PutBack()
        {
            if (Dock != OverlayDock.Window) return;
            Dock = DockBefore;
            Host?.Changed();
        }

        /// <summary>The layout it takes where it stands: a row along the top and bottom, a column at the sides (as in Unity).</summary>
        public OverlayLayout EffectiveLayout =>
            Dock is OverlayDock.Top or OverlayDock.Bottom ? OverlayLayout.Horizontal : Dock is OverlayDock.Left or OverlayDock.Right ? OverlayLayout.Vertical : Layout;

        /// <summary>Builds its handle and content again for where it stands and how it is laid out.</summary>
        public void Rebuild()
        {
            var layout = EffectiveLayout;
            bool row = layout == OverlayLayout.Horizontal;
            var handle = new TextBlock
            {
                Text = "≡", FontSize = 14, Cursor = Cursors.SizeAll, Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = row ? HorizontalAlignment.Left : HorizontalAlignment.Center, Background = Brushes.Transparent,
                ToolTip = Lang.T("Sürükleyerek taşı, kenara bırakınca yerleşir · çift tık: ayrı pencereye al / yerine koy · sağ tık: düzen") + " (" + Lang.T(Title) + ")"
            };
            handle.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
            handle.MouseLeftButtonDown += (_, e) =>
            {
                if (_window != null)
                {
                    // in its own window: a double click puts it back, a drag moves the window
                    if (e.ClickCount >= 2) PutBack();
                    else
                    {
                        _window.DragMove();
                        FloatAt = new Point(_window.Left, _window.Top);
                        Host?.Saved();
                    }

                    e.Handled = true;
                    return;
                }

                if (e.ClickCount >= 2)
                {
                    TearOff();   // a double click on the handle tears it off, as the floating toolbars did
                    e.Handled = true;
                    return;
                }

                Host?.BeginDrag(this, e.GetPosition(Host));
                handle.CaptureMouse();
                e.Handled = true;
            };
            handle.MouseMove += (_, e) =>
            {
                if (handle.IsMouseCaptured) Host?.DragTo(e.GetPosition(Host));
            };
            handle.MouseLeftButtonUp += (_, e) =>
            {
                if (!handle.IsMouseCaptured) return;
                handle.ReleaseMouseCapture();
                Host?.EndDrag(e.GetPosition(Host));
                e.Handled = true;
            };
            handle.MouseRightButtonUp += (_, e) =>
            {
                HandleMenu(handle).IsOpen = true;
                e.Handled = true;
            };

            // [layout] a dock panel, not a stack: the content gets the height (or width) there is, so a long strip can wrap
            // into a second column when the view is short (a large display scale)
            var stack = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(handle, row ? System.Windows.Controls.Dock.Left : System.Windows.Controls.Dock.Top);
            stack.Children.Add(handle);
            if (Collapsed)
            {
                stack.Children.Add(CollapsedButton());
            }
            else
            {
                if (layout == OverlayLayout.Panel)
                {
                    var label = new TextBlock { Text = Lang.T(Title), FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(6, 0, 6, 4) };
                    label.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                    DockPanel.SetDock(label, System.Windows.Controls.Dock.Top);
                    stack.Children.Add(label);
                }

                stack.Children.Add(Content(row ? Orientation.Horizontal : Orientation.Vertical));
            }

            Child = stack;
            Lang.Translate(this);
        }

        /// <summary>Collapsed: its icon (or its label's first two letters) and a ▾ that opens it as a panel.</summary>
        private Button CollapsedButton()
        {
            var face = new StackPanel { Orientation = Orientation.Horizontal };
            if (IconData != null) face.Children.Add(new Theme.Icon { Data = IconData, Size = 14 });
            else face.Children.Add(new TextBlock { Text = Lang.T(Title).Substring(0, Math.Min(2, Lang.T(Title).Length)), FontWeight = FontWeights.SemiBold });
            face.Children.Add(new TextBlock { Text = " ▾", FontSize = 10, VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = face, Padding = new Thickness(6, 2, 6, 2), MinHeight = 0, ToolTip = Lang.T(Title) };
            b.SetResourceReference(StyleProperty, "Btn.Ghost");
            b.Click += (_, _) =>
            {
                var inner = new StackPanel { Margin = new Thickness(6) };
                inner.Children.Add(new TextBlock { Text = Lang.T(Title), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                inner.Children.Add(Content(Orientation.Vertical));
                var frame = new Border { Child = inner, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
                frame.SetResourceReference(BackgroundProperty, "B.Overlay");
                frame.SetResourceReference(BorderBrushProperty, "B.BorderStrong");
                Lang.Translate(frame);
                new Popup { Child = frame, PlacementTarget = b, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, IsOpen = true };
            };
            return b;
        }

        private ContextMenu HandleMenu(UIElement target)
        {
            var menu = new ContextMenu { PlacementTarget = target };
            bool edge = Dock is OverlayDock.Top or OverlayDock.Bottom or OverlayDock.Left or OverlayDock.Right;
            foreach (var (layout, name) in new[] { (OverlayLayout.Panel, "Panel"), (OverlayLayout.Horizontal, "Yatay"), (OverlayLayout.Vertical, "Dikey") })
            {
                // docked along an edge, an overlay keeps the edge's row or column: it can only collapse or expand there (as in Unity)
                var item = new MenuItem { Header = Lang.T(name), IsCheckable = true, IsChecked = !Collapsed && EffectiveLayout == layout, IsEnabled = !edge };
                var chosen = layout;
                item.Click += (_, _) =>
                {
                    Layout = chosen;
                    Collapsed = false;
                    Host?.Changed();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var fold = new MenuItem { Header = Lang.T(Collapsed ? "Genişlet" : "Daralt") };
            fold.Click += (_, _) =>
            {
                Collapsed = !Collapsed;
                Host?.Changed();
            };
            menu.Items.Add(fold);
            // [floatbar] its own small window (a second screen too), or back where it stood
            var own = new MenuItem { Header = Lang.T(_window == null ? "Ayrı pencereye al" : "Yerine koy") };
            own.Click += (_, _) =>
            {
                if (_window == null) TearOff();
                else PutBack();
            };
            menu.Items.Add(own);
            if (CanHide)
            {
                var hide = new MenuItem { Header = Lang.T("Gizle") };
                hide.Click += (_, _) =>
                {
                    Shown = false;
                    Host?.Changed();
                };
                menu.Items.Add(hide);
            }

            return menu;
        }
    }

    /// <summary>
    /// [overlays] The layer over a view that holds its overlays: a row along the top and the bottom, a column down each side,
    /// a stack in each corner, and the floating ones. Dragged by its handle, an overlay floats under the cursor while the
    /// drop zones show in blue; let go on one and it docks there, elsewhere it floats where it was let go. Its layout is a
    /// line of text (<see cref="SaveLayout"/>) the page keeps in its settings and its presets.
    /// </summary>
    public sealed class OverlayHost : Grid
    {
        private readonly Canvas _floating = new Canvas();
        private readonly Dictionary<OverlayDock, StackPanel> _docks = new Dictionary<OverlayDock, StackPanel>();
        private readonly Dictionary<OverlayDock, Rectangle> _zones = new Dictionary<OverlayDock, Rectangle>();
        private readonly Grid _zoneLayer = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        private SceneOverlay? _dragging;
        private Vector _grab;

        public OverlayHost()
        {
            foreach (var dock in Enum.GetValues(typeof(OverlayDock)).Cast<OverlayDock>().Where(d => d != OverlayDock.Floating && d != OverlayDock.Window))
            {
                // along an edge the overlays stand side by side (a second strip opens beside the first); in a corner they stack
                bool row = dock is OverlayDock.Top or OverlayDock.Bottom or OverlayDock.Left or OverlayDock.Right;
                var panel = new StackPanel
                {
                    Orientation = row ? Orientation.Horizontal : Orientation.Vertical,
                    HorizontalAlignment = dock is OverlayDock.Left or OverlayDock.TopLeft or OverlayDock.BottomLeft ? HorizontalAlignment.Left
                        : dock is OverlayDock.Right or OverlayDock.TopRight or OverlayDock.BottomRight ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                    VerticalAlignment = dock is OverlayDock.Top or OverlayDock.TopLeft or OverlayDock.TopRight ? VerticalAlignment.Top
                        : dock is OverlayDock.Bottom or OverlayDock.BottomLeft or OverlayDock.BottomRight ? VerticalAlignment.Bottom : VerticalAlignment.Center,
                    Margin = new Thickness(6)
                };
                _docks[dock] = panel;
                Children.Add(panel);
                var zone = new Rectangle { RadiusX = 4, RadiusY = 4, StrokeThickness = 1 };
                zone.Fill = new SolidColorBrush(Color.FromArgb(0x30, 0x3B, 0x82, 0xF6));
                zone.Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x3B, 0x82, 0xF6));
                _zones[dock] = zone;
                _zoneLayer.Children.Add(zone);
            }

            Children.Add(_floating);
            Children.Add(_zoneLayer);
            SizeChanged += (_, _) => PlaceZones();
            // [floatbar] the torn-off ones go with their page: hidden while another page is up
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible) Arrange();
                else foreach (var o in Overlays) o.HideWindow();
            };
        }

        public List<SceneOverlay> Overlays { get; } = new List<SceneOverlay>();

        /// <summary>Raised when an overlay moved, changed layout, collapsed or was hidden (the page saves the layout).</summary>
        public event EventHandler? LayoutChanged;

        public void Add(SceneOverlay overlay)
        {
            overlay.Host = this;
            Overlays.Add(overlay);
        }

        public SceneOverlay? Find(string id) => Overlays.FirstOrDefault(o => o.Id == id);

        /// <summary>[workspaces] Shows, folds or hides an overlay (a page's own tool strip is never hidden, only folded).</summary>
        public void SetState(string id, bool shown, bool folded)
        {
            if (Find(id) is not { } o) return;
            o.Shown = shown || !o.CanHide;
            o.Collapsed = folded || (!shown && !o.CanHide);
        }

        /// <summary>[workspaces] Lays the overlays out again and keeps the layout (after <see cref="SetState"/> or <see cref="LoadLayout"/>).</summary>
        public void Apply() => Changed();

        internal void Changed()
        {
            Arrange();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Only the layout to keep changed (a torn-off overlay's window moved).</summary>
        internal void Saved() => LayoutChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Puts every shown overlay where it stands, built for its layout.</summary>
        public void Arrange()
        {
            foreach (var panel in _docks.Values) panel.Children.Clear();
            _floating.Children.Clear();
            foreach (var o in Overlays.Where(x => !x.Shown || x.Dock != OverlayDock.Window)) o.CloseWindow();
            foreach (var o in Overlays.Where(x => x.Shown))
            {
                o.Rebuild();
                if (o.Dock == OverlayDock.Window)
                {
                    // its own window opens once the page is on screen (never for a picture: --snapshot shows it in place)
                    if (Unattended.Active)
                    {
                        _docks[o.DockBefore is OverlayDock.Floating or OverlayDock.Window ? OverlayDock.Left : o.DockBefore].Children.Add(o);
                        continue;
                    }

                    if (IsVisible) o.ShowWindow(this);
                    continue;
                }

                bool side = o.Dock is OverlayDock.Left or OverlayDock.Right;
                o.VerticalAlignment = side ? VerticalAlignment.Center : VerticalAlignment.Stretch;
                o.HorizontalAlignment = o.Dock is OverlayDock.Top or OverlayDock.Bottom ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
                if (o.Dock == OverlayDock.Floating)
                {
                    Canvas.SetLeft(o, Math.Max(0, Math.Min(Math.Max(0, ActualWidth - 40), o.FloatAt.X)));
                    Canvas.SetTop(o, Math.Max(0, Math.Min(Math.Max(0, ActualHeight - 30), o.FloatAt.Y)));
                    _floating.Children.Add(o);
                }
                else _docks[o.Dock].Children.Add(o);
            }
        }

        // ------------------------------------------------------------------ dragging

        internal void BeginDrag(SceneOverlay o, Point at)
        {
            var topLeft = o.TranslatePoint(new Point(0, 0), this);
            _grab = at - topLeft;
            _dragging = o;
            (o.Parent as Panel)?.Children.Remove(o);
            o.Dock = OverlayDock.Floating;
            o.FloatAt = topLeft;
            Canvas.SetLeft(o, topLeft.X);
            Canvas.SetTop(o, topLeft.Y);
            _floating.Children.Add(o);
            PlaceZones();
            _zoneLayer.Visibility = Visibility.Visible;
        }

        internal void DragTo(Point at)
        {
            if (_dragging == null) return;
            var p = at - _grab;
            Canvas.SetLeft(_dragging, p.X);
            Canvas.SetTop(_dragging, p.Y);
            var over = ZoneAt(at);
            foreach (var kv in _zones) kv.Value.Opacity = kv.Key == over ? 1 : 0.45;
        }

        internal void EndDrag(Point at)
        {
            var o = _dragging;
            _dragging = null;
            _zoneLayer.Visibility = Visibility.Collapsed;
            if (o == null) return;
            var over = ZoneAt(at);
            o.Dock = over ?? OverlayDock.Floating;
            if (over == null) o.FloatAt = at - _grab;
            Changed();
        }

        /// <summary>A drag held still for a picture (--snapshot): picked up here, carried there, not let go.</summary>
        public void BeginDragForSnapshot(SceneOverlay o, Point at) => BeginDrag(o, at);

        public void DragToForSnapshot(Point at) => DragTo(at);

        /// <summary>The drop zones: a band along each edge, a square in each corner.</summary>
        private void PlaceZones()
        {
            double w = ActualWidth, h = ActualHeight, band = 34, corner = Math.Min(110, Math.Min(w, h) / 4);
            void Put(OverlayDock d, double x, double y, double zw, double zh)
            {
                var r = _zones[d];
                r.HorizontalAlignment = HorizontalAlignment.Left;
                r.VerticalAlignment = VerticalAlignment.Top;
                r.Margin = new Thickness(x, y, 0, 0);
                r.Width = Math.Max(0, zw);
                r.Height = Math.Max(0, zh);
            }

            Put(OverlayDock.TopLeft, 4, 4, corner, corner);
            Put(OverlayDock.TopRight, w - corner - 4, 4, corner, corner);
            Put(OverlayDock.BottomLeft, 4, h - corner - 4, corner, corner);
            Put(OverlayDock.BottomRight, w - corner - 4, h - corner - 4, corner, corner);
            Put(OverlayDock.Top, corner + 8, 4, w - 2 * corner - 16, band);
            Put(OverlayDock.Bottom, corner + 8, h - band - 4, w - 2 * corner - 16, band);
            Put(OverlayDock.Left, 4, corner + 8, band, h - 2 * corner - 16);
            Put(OverlayDock.Right, w - band - 4, corner + 8, band, h - 2 * corner - 16);
        }

        private OverlayDock? ZoneAt(Point p)
        {
            foreach (var kv in _zones)
            {
                var r = kv.Value;
                var box = new Rect(r.Margin.Left, r.Margin.Top, r.Width, r.Height);
                if (box.Contains(p)) return kv.Key;
            }

            return null;
        }

        // ------------------------------------------------------------------ layout as text

        /// <summary>"id:dock:x:y:layout:collapsed:shown:before;…" (positions in device-independent pixels; on the screen for a window).</summary>
        public string SaveLayout() => string.Join(";", Overlays.Select(o => string.Join(":", o.Id, o.Dock, ((int)o.FloatAt.X).ToString(CultureInfo.InvariantCulture),
            ((int)o.FloatAt.Y).ToString(CultureInfo.InvariantCulture), o.Layout, o.Collapsed ? "1" : "0", o.Shown ? "1" : "0", o.DockBefore)));

        /// <summary>Takes a saved layout back; overlays it does not name keep theirs. Returns whether anything was read.</summary>
        public bool LoadLayout(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            bool any = false;
            foreach (var entry in text!.Split(';'))
            {
                var f = entry.Split(':');
                if (f.Length < 7 || Find(f[0]) is not { } o) continue;
                if (Enum.TryParse(f[1], out OverlayDock dock)) o.Dock = dock;
                if (double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)) o.FloatAt = new Point(x, y);
                if (Enum.TryParse(f[4], out OverlayLayout layout)) o.Layout = layout;
                o.Collapsed = f[5] == "1";
                o.Shown = f[6] == "1";
                if (f.Length >= 8 && Enum.TryParse(f[7], out OverlayDock before)) o.DockBefore = before;
                any = true;
            }

            return any;
        }
    }
}
