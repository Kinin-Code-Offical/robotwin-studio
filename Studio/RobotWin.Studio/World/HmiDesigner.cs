using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [hmi-designer] The operator panel's designer, laid out as HMI editors are: the palette on the left in sections
    /// (indicators, controls, alarms, general), the panel on a grid in the middle (drag an element in, drag it about, pull
    /// a corner to size it; arrows nudge, Delete removes, Ctrl+D duplicates), the selected element's properties on the
    /// right (label, PLC and variable, range and unit, a trend's series and window, a motor's start / stop / running /
    /// fault variables, place and size), and the alarms in their own tab (variable, level, condition, threshold, text).
    /// Every change is one undoable edit of the world.
    /// </summary>
    internal sealed class HmiDesigner : Window
    {
        private WorldDocument _world;
        private readonly Action<string, Action> _edit;
        private readonly Canvas _canvas = new Canvas { ClipToBounds = true };
        private readonly Border _paper = new Border();
        private readonly ScaleTransform _zoom = new ScaleTransform(1, 1);
        private readonly StackPanel _props = new StackPanel();
        private readonly StackPanel _alarms = new StackPanel();
        private readonly Rectangle _outline = new Rectangle { StrokeThickness = 1.5, IsHitTestVisible = false };
        private readonly Rectangle[] _handles = Enumerable.Range(0, 4).Select(_ => new Rectangle { Width = 9, Height = 9, StrokeThickness = 1, IsHitTestVisible = false }).ToArray();
        private readonly Dictionary<HmiWidget, FrameworkElement> _items = new Dictionary<HmiWidget, FrameworkElement>();
        private HmiWidget? _selected;
        private string _screen = string.Empty;   // [hmi-screens] the screen being designed ("": the first)
        private readonly StackPanel _screens = new StackPanel();

        // a drag on the panel: moving the selection, or one of its corners (0 top-left, 1 top-right, 2 bottom-right, 3 bottom-left)
        private int _corner = -1;
        private bool _moving, _moved;
        private Point _from;
        private Rect _start;

        private HmiPanelSettings Panel => _world.HmiPanel ?? new HmiPanelSettings();

        private List<HmiWidget> Widgets => _world.Hmi ?? new List<HmiWidget>();

        /// <summary>The screen being designed, by name (the first when none is picked or it is gone).</summary>
        private string Screen => Panel.ScreenNames.Contains(_screen) ? _screen : Panel.ScreenNames[0];

        /// <summary>The widgets on the screen being designed.</summary>
        private List<HmiWidget> OnScreen => Widgets.Where(w => Panel.ScreenOf(w) == Screen).ToList();

        private List<WorldObject> Plcs => _world.Objects.Where(o => o.PartId == CellParts.PlcId).ToList();

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        public HmiDesigner(Window? owner, WorldDocument world, Action<string, Action> edit)
        {
            _world = world;
            _edit = edit;
            Owner = owner;
            Title = Lang.T("HMI tasarımcısı");
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = Math.Min(1360, SystemParameters.WorkArea.Width - 24);   // a laptop's screen too
            Height = Math.Min(860, SystemParameters.WorkArea.Height - 24);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");
            _outline.Stroke = R<Brush>("B.Accent");
            foreach (var h in _handles)
            {
                h.Fill = R<Brush>("B.Surface");
                h.Stroke = R<Brush>("B.Accent");
            }

            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 14) };
            root.Children.Add(Head());
            var palette = new ScrollViewer { Content = Palette(), Width = 196, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 12, 0) };
            DockPanel.SetDock(palette, Dock.Left);
            root.Children.Add(palette);
            var props = new ScrollViewer { Content = _props, Width = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(props, Dock.Right);
            root.Children.Add(props);

            _paper.Child = _canvas;
            _paper.LayoutTransform = _zoom;
            _paper.Background = R<Brush>("B.Base");
            _paper.BorderBrush = R<Brush>("B.BorderStrong");
            _paper.BorderThickness = new Thickness(1);
            _paper.HorizontalAlignment = HorizontalAlignment.Left;
            _paper.VerticalAlignment = VerticalAlignment.Top;
            _paper.Margin = new Thickness(16);
            var stage = new ScrollViewer
            {
                Content = _paper, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = R<Brush>("B.Canvas"), Focusable = true,
            };
            _canvas.AllowDrop = true;
            _canvas.Drop += OnDrop;
            _canvas.DragOver += (_, e) =>
            {
                e.Effects = e.Data.GetDataPresent("rw-hmi-kind") ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };
            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = Lang.T("Panel"), Content = stage });
            tabs.Items.Add(new TabItem { Header = Lang.T("Alarmlar"), Content = new ScrollViewer { Content = _alarms, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(12) } });
            root.Children.Add(tabs);
            PreviewKeyDown += OnKey;

            Content = new Border
            {
                Margin = new Thickness(20), Background = R<Brush>("B.Overlay"), BorderBrush = R<Brush>("B.BorderStrong"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Child = root,
            };
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");

            if (_world.Hmi is { Count: > 0 } list && list.Any(w => w.Width <= 0 || w.Height <= 0))
                _edit("HMI paneli yerleştirildi", () => HmiLayout.Place(list, Panel));   // a panel made before the designer
            Rebuild();
            BuildAlarms();
        }

        // ---------------------------------------------------------------- head: title, screen, grid, zoom

        private UIElement Head()
        {
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed && e.OriginalSource is not Control) DragMove();
            };
            var close = new Button { Content = Lang.T("Kapat"), Style = R<Style>("Btn.Ghost"), MinWidth = 90, IsCancel = true };
            close.Click += (_, _) => Close();
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            bar.Children.Add(new TextBlock { Text = Lang.T("HMI tasarımcısı"), Style = R<Style>("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center });
            var p = Panel;
            var sizes = HmiPanelSettings.Presets.Select(s => (s.W, s.H)).ToList();
            if (!sizes.Contains((p.Width, p.Height))) sizes.Insert(0, (p.Width, p.Height));
            var screen = Combo(sizes, (p.Width, p.Height), s =>
            {
                _edit($"HMI paneli {s.Item1:0} × {s.Item2:0}", () =>
                {
                    var panel = _world.HmiPanel ??= new HmiPanelSettings();
                    panel.Width = s.Item1;
                    panel.Height = s.Item2;
                });
                Rebuild();
            }, s => string.Format(CultureInfo.CurrentCulture, "{0:0} × {1:0}", s.Item1, s.Item2));
            screen.Width = 120;
            screen.ToolTip = Lang.T("Panelin ekran boyutu (piksel): 7\" 800 × 480, 10\" 1024 × 600, 12\" 1280 × 800, 15\" 1024 × 768");
            var grid = Combo(new[] { 0.0, 4, 8, 16 }, p.Grid, g =>
            {
                _edit($"HMI ızgarası {g:0}", () => (_world.HmiPanel ??= new HmiPanelSettings()).Grid = g);
                Rebuild();
            },
                g => g <= 0 ? Lang.T("Izgara yok") : Lang.T($"Izgara {g:0} px"));
            grid.Width = 110;
            // the whole screen in view when it opens: 75 % when the middle is narrower than the panel
            double start = Width - 650 >= p.Width ? 1.0 : Width - 650 >= p.Width * 0.75 ? 0.75 : 0.5;
            _zoom.ScaleX = _zoom.ScaleY = start;
            var zoom = Combo(new[] { 0.5, 0.75, 1.0, 1.25, 1.5 }, start, z => _zoom.ScaleX = _zoom.ScaleY = z, z => (z * 100).ToString("0", CultureInfo.CurrentCulture) + " %");
            zoom.Width = 80;
            foreach (var c in new FrameworkElement[] { screen, grid, zoom })
            {
                c.Margin = new Thickness(0, 0, 8, 0);
                bar.Children.Add(c);
            }

            head.Children.Add(bar);
            DockPanel.SetDock(head, Dock.Top);
            return head;
        }

        // ---------------------------------------------------------------- palette

        private UIElement Palette()
        {
            var stack = new StackPanel();
            stack.Children.Add(Overline(Lang.T("Panel ekranları"), 0));
            stack.Children.Add(_screens);   // [hmi-screens] filled by BuildScreens
            foreach (var (section, kinds) in new[]
                     {
                         ("Göstergeler", new[] { HmiWidget.Lamp, HmiWidget.Number, HmiWidget.Bar, HmiWidget.Gauge, HmiWidget.Trend }),
                         ("Kontroller", new[] { HmiWidget.Button, HmiWidget.Switch, HmiWidget.SetPoint, HmiWidget.Motor }),
                         ("Alarm", new[] { HmiWidget.Alarms }),
                         ("Genel", new[] { HmiWidget.Text, HmiWidget.Navigate }),
                     })
            {
                stack.Children.Add(Overline(Lang.T(section), 14));
                var wrap = new WrapPanel();
                foreach (string kind in kinds) wrap.Children.Add(Tile(kind));
                stack.Children.Add(wrap);
            }

            stack.Children.Add(Caption(Lang.T("Panele sürükleyin ya da tıklayın. Öğe seçiliyken oklar kaydırır (Shift: 1 piksel), Sil kaldırır, Ctrl+D çoğaltır."), 14));
            return stack;
        }

        private Button Tile(string kind)
        {
            var content = new StackPanel { Width = 76 };
            content.Children.Add(new Path { Data = Geometry.Parse(Glyph(kind)), Stroke = R<Brush>("B.Text"), StrokeThickness = 1.6, Width = 24, Height = 24, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 4) });
            content.Children.Add(new TextBlock { Text = Lang.T(HmiElements.KindName(kind)), FontSize = TypeScale.Caption, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var tile = new Button { Content = content, Style = R<Style>("Btn.Ghost"), Padding = new Thickness(4, 6, 4, 6), Margin = new Thickness(0, 0, 4, 4), ToolTip = Lang.T(HmiElements.KindName(kind)) };
            Point down = default;
            tile.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(tile);
            tile.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(tile);
                if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < 6) return;
                DragDrop.DoDragDrop(tile, new DataObject("rw-hmi-kind", kind), DragDropEffects.Copy);
            };
            tile.Click += (_, _) => Add(kind, null);
            return tile;
        }

        /// <summary>A flat line glyph per kind for the palette.</summary>
        private static string Glyph(string kind) => kind switch
        {
            HmiWidget.Lamp => "M12,4 A8,8 0 1 1 11.99,4 M12,9 A3,3 0 1 1 11.99,9",
            HmiWidget.Number => "M3,7 L21,7 L21,17 L3,17 Z M7,10 L7,14 M11,10 L14,10 L11,14 L14,14 M17,10 L19,10 L17,12 L19,12 L17,14",
            HmiWidget.Bar => "M3,9 L21,9 L21,15 L3,15 Z M3,12 L14,12",
            HmiWidget.Gauge => "M4,17 A8,8 0 1 1 20,17 M12,12 L16,8",
            HmiWidget.Trend => "M3,4 L3,20 L21,20 M4,16 L9,11 L13,14 L20,6",
            HmiWidget.Button => "M4,7 L20,7 L20,17 L4,17 Z M8,12 L16,12",
            HmiWidget.Switch => "M7,8 L17,8 A4,4 0 0 1 17,16 L7,16 A4,4 0 0 1 7,8 M17,9.5 A2.5,2.5 0 1 1 16.99,9.5",
            HmiWidget.SetPoint => "M3,7 L21,7 L21,17 L3,17 Z M8,10 L8,14 M14,12 L18,12 M16,10 L16,14",
            HmiWidget.Motor => "M12,4 A8,8 0 1 1 11.99,4 M8,15 L8,9 L12,13 L16,9 L16,15",
            HmiWidget.Alarms => "M12,3 L21,19 L3,19 Z M12,9 L12,13 M12,15.5 L12,16.5",
            HmiWidget.Navigate => "M3,7 L21,7 L21,17 L3,17 Z M8,12 L16,12 M13,9 L16,12 L13,15",
            _ => "M5,6 L19,6 M12,6 L12,19",
        };

        // ---------------------------------------------------------------- adding, removing, duplicating

        private void Add(string kind, Point? at)
        {
            if (Plcs.Count == 0 && HmiWidget.Bound(kind))
            {
                MessageText(Lang.T("Önce dünyaya bir PLC ekleyin: öğeler PLC değişkenlerine bağlanır."));
                return;
            }

            var (w, h) = HmiWidget.DefaultSize(kind);
            var widget = new HmiWidget { Kind = kind, Plc = Plcs.FirstOrDefault()?.Id ?? string.Empty, Width = w, Height = h, Screen = Screen == Panel.ScreenNames[0] ? string.Empty : Screen };
            if (kind == HmiWidget.Navigate) widget.Target = Panel.ScreenNames.FirstOrDefault(n => n != Screen) ?? string.Empty;
            if (kind == HmiWidget.Text) widget.Label = Lang.T("Başlık");
            if (at is { } p)
            {
                widget.X = Clamp(HmiLayout.Snap(p.X - w / 2, Panel.Grid), 0, Panel.Width - w);
                widget.Y = Clamp(HmiLayout.Snap(p.Y - h / 2, Panel.Grid), 0, Panel.Height - h);
            }
            else
            {
                widget.Width = widget.Height = 0;
                HmiLayout.Place(OnScreen.Concat(new[] { widget }).ToList(), Panel);
            }

            _edit($"HMI: {HmiElements.KindName(kind)} eklendi", () => (_world.Hmi ??= new List<HmiWidget>()).Add(widget));
            _selected = widget;
            Rebuild();
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData("rw-hmi-kind") is string kind) Add(kind, e.GetPosition(_canvas));
        }

        private void Remove(HmiWidget w)
        {
            _edit($"HMI: {HmiElements.KindName(w.Kind)} kaldırıldı", () => _world.Hmi?.Remove(w));
            if (_world.Hmi is { Count: 0 }) _world.Hmi = null;
            _selected = null;
            Rebuild();
        }

        private void Duplicate(HmiWidget w)
        {
            var copy = w.Clone();
            copy.X = Clamp(w.X + 16, 0, Panel.Width - w.Width);
            copy.Y = Clamp(w.Y + 16, 0, Panel.Height - w.Height);
            _edit($"HMI: {HmiElements.KindName(w.Kind)} çoğaltıldı", () => _world.Hmi?.Add(copy));
            _selected = copy;
            Rebuild();
        }

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(Math.Max(lo, hi), v));

        private void MessageText(string text)
        {
            _props.Children.Clear();
            _props.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = R<Brush>("B.Warning") });
        }

        // ---------------------------------------------------------------- the panel

        private void Rebuild()
        {
            var p = Panel;
            _canvas.Width = p.Width;
            _canvas.Height = p.Height;
            _canvas.Background = GridBrush(p.Grid);
            _canvas.Children.Clear();
            _items.Clear();
            BuildScreens();
            var ctx = new HmiContext { Live = false, PlcName = id => _world.Find(id)?.Name ?? id };
            foreach (var w in OnScreen)
            {
                var item = HmiElements.Build(w, ctx, new List<Action>());
                Canvas.SetLeft(item, w.X);
                Canvas.SetTop(item, w.Y);
                _canvas.Children.Add(item);
                _items[w] = item;
            }

            if (_selected != null && !OnScreen.Contains(_selected)) _selected = null;
            _canvas.Children.Add(_outline);
            foreach (var h in _handles) _canvas.Children.Add(h);
            PlaceSelection();
            Properties();
        }

        /// <summary>The grid as a dotted fill of the panel (flat, the theme's border colour).</summary>
        private Brush GridBrush(double grid)
        {
            var bg = R<Brush>("B.Base");
            if (grid < 4) return bg;
            double step = grid * Math.Max(1, Math.Round(16 / grid));
            var dot = new GeometryDrawing(R<Brush>("B.Border"), null, new RectangleGeometry(new Rect(0, 0, 1, 1)));
            var cell = new GeometryDrawing(bg, null, new RectangleGeometry(new Rect(0, 0, step, step)));
            var group = new DrawingGroup();
            group.Children.Add(cell);
            group.Children.Add(dot);
            return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, step, step), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.None };
        }

        private void PlaceSelection()
        {
            bool on = _selected != null && _items.ContainsKey(_selected);
            _outline.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            foreach (var h in _handles) h.Visibility = _outline.Visibility;
            if (!on) return;
            var r = Bounds(_selected!);
            Canvas.SetLeft(_outline, r.X - 2);
            Canvas.SetTop(_outline, r.Y - 2);
            _outline.Width = r.Width + 4;
            _outline.Height = r.Height + 4;
            var corners = Corners(r);
            for (int i = 0; i < 4; i++)
            {
                Canvas.SetLeft(_handles[i], corners[i].X - 4.5);
                Canvas.SetTop(_handles[i], corners[i].Y - 4.5);
            }
        }

        private Rect Bounds(HmiWidget w) =>
            _items.TryGetValue(w, out var item) ? new Rect(Canvas.GetLeft(item), Canvas.GetTop(item), item.Width, item.Height) : new Rect(w.X, w.Y, w.Width, w.Height);

        private static Point[] Corners(Rect r) => new[] { r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft };

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            Focus();
            _corner = -1;
            if (_selected != null)
            {
                var corners = Corners(Bounds(_selected));
                for (int i = 0; i < 4; i++)
                    if (Math.Abs(p.X - corners[i].X) <= 7 && Math.Abs(p.Y - corners[i].Y) <= 7) _corner = i;
            }

            if (_corner < 0)
            {
                // the topmost element under the pointer (the last drawn)
                var hit = OnScreen.LastOrDefault(w => Bounds(w).Contains(p));
                if (!ReferenceEquals(hit, _selected))
                {
                    _selected = hit;
                    PlaceSelection();
                    Properties();
                }
            }

            if (_selected == null) return;
            _moving = true;
            _moved = false;
            _from = p;
            _start = Bounds(_selected);
            _canvas.CaptureMouse();
            e.Handled = true;
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (!_moving || _selected == null || e.LeftButton != MouseButtonState.Pressed)
            {
                _canvas.Cursor = _selected != null && Corners(Bounds(_selected)).Any(c => Math.Abs(p.X - c.X) <= 7 && Math.Abs(p.Y - c.Y) <= 7) ? Cursors.SizeNWSE : null;
                return;
            }

            double dx = p.X - _from.X, dy = p.Y - _from.Y, g = Panel.Grid;
            if (!_moved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
            _moved = true;
            var r = _start;
            if (_corner < 0)
            {
                r.X = Clamp(HmiLayout.Snap(_start.X + dx, g), 0, Panel.Width - r.Width);
                r.Y = Clamp(HmiLayout.Snap(_start.Y + dy, g), 0, Panel.Height - r.Height);
            }
            else
            {
                double left = _start.Left, top = _start.Top, right = _start.Right, bottom = _start.Bottom;
                if (_corner is 0 or 3) left = Clamp(HmiLayout.Snap(left + dx, g), 0, right - 40);
                else right = Clamp(HmiLayout.Snap(right + dx, g), left + 40, Panel.Width);
                if (_corner is 0 or 1) top = Clamp(HmiLayout.Snap(top + dy, g), 0, bottom - 30);
                else bottom = Clamp(HmiLayout.Snap(bottom + dy, g), top + 30, Panel.Height);
                r = new Rect(left, top, right - left, bottom - top);
            }

            var item = _items[_selected];
            Canvas.SetLeft(item, r.X);
            Canvas.SetTop(item, r.Y);
            item.Width = r.Width;
            item.Height = r.Height;
            PlaceSelection();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!_moving) return;
            _moving = false;
            _canvas.ReleaseMouseCapture();
            if (!_moved || _selected == null) return;
            var w = _selected;
            var r = Bounds(w);
            _edit(_corner < 0 ? $"HMI: {HmiElements.KindName(w.Kind)} taşındı" : $"HMI: {HmiElements.KindName(w.Kind)} boyutlandı", () =>
            {
                w.X = r.X;
                w.Y = r.Y;
                w.Width = r.Width;
                w.Height = r.Height;
            });
            Rebuild();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (_selected == null || Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox { IsEditable: true }) return;
            var w = _selected;
            double step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 || Panel.Grid <= 0 ? 1 : Panel.Grid;
            (double dx, double dy) = e.Key switch
            {
                Key.Left => (-step, 0.0),
                Key.Right => (step, 0.0),
                Key.Up => (0.0, -step),
                Key.Down => (0.0, step),
                _ => (0.0, 0.0),
            };
            if (dx != 0 || dy != 0)
            {
                _edit($"HMI: {HmiElements.KindName(w.Kind)} kaydırıldı", () =>
                {
                    w.X = Clamp(w.X + dx, 0, Panel.Width - w.Width);
                    w.Y = Clamp(w.Y + dy, 0, Panel.Height - w.Height);
                });
                Rebuild();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                Remove(w);
                e.Handled = true;
            }
            else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                Duplicate(w);
                e.Handled = true;
            }
        }

        // ---------------------------------------------------------------- properties

        private void Properties()
        {
            _props.Children.Clear();
            if (_selected == null)
            {
                // [hmi-screens] the screen being designed: its name, removal
                string screen = Screen;
                var names = Panel.ScreenNames;
                _props.Children.Add(Overline(Lang.T("Ekran"), 0));
                _props.Children.Add(Row(Lang.T("Ad"), Text(screen, v =>
                {
                    string name = v.Trim();
                    if (name.Length == 0 || names.Contains(name)) return;
                    _edit($"HMI ekranı: {name}", () =>
                    {
                        var panel = _world.HmiPanel ??= new HmiPanelSettings();
                        if (panel.Screens == null) panel.Screens = new List<string>(panel.ScreenNames);
                        HmiLayout.RenameScreen(_world.Hmi ?? new List<HmiWidget>(), panel, screen, name);
                    });
                    _screen = name;
                    Rebuild();
                }), 160));
                _props.Children.Add(Caption(Lang.T(names[0] == screen ? "Panel bu ekranla açılır." : "Ekran düğmeleri bu ekranı açar."), 0));
                if (names.Count > 1)
                {
                    _props.Children.Add(Buttons(Button(Lang.T("Ekranı sil"), (_, _) =>
                    {
                        _edit($"HMI ekranı silindi: {screen}", () => HmiLayout.RenameScreen(_world.Hmi ?? new List<HmiWidget>(), _world.HmiPanel ??= new HmiPanelSettings(), screen, null));
                        if (_world.Hmi is { Count: 0 }) _world.Hmi = null;
                        _screen = string.Empty;
                        Rebuild();
                    }, tip: "Ekranı ve üstündeki öğeleri siler")));
                }

                _props.Children.Add(Overline(Lang.T("Panel")));
                _props.Children.Add(Caption(Lang.T($"{Widgets.Count} öğe · {Panel.Width:0} × {Panel.Height:0} px · {_world.HmiAlarms?.Count ?? 0} alarm"), 0));
                _props.Children.Add(Caption(Lang.T("Bir öğe seçin ya da paletten ekleyin. Çalışırken panel, dünyanın HMI sekmesinde canlıdır."), 8));
                return;
            }

            var w = _selected;
            void Set(string what, Action change)
            {
                _edit($"HMI: {what}", change);
                Rebuild();
            }

            _props.Children.Add(Overline(Lang.T(HmiElements.KindName(w.Kind)), 0));
            _props.Children.Add(Row(Lang.T("Etiket"), Text(w.Label, v => Set("etiket", () => w.Label = v.Trim())), 160));
            if (w.Kind == HmiWidget.Navigate)
            {
                var targets = Panel.ScreenNames.Where(n => n != Screen).ToList();
                if (targets.Count == 0) _props.Children.Add(Caption(Lang.T("Panelde başka ekran yok: soldan + Ekran ile ekleyin."), 0));
                else _props.Children.Add(Row(Lang.T("Açtığı ekran"), Combo(targets, targets.Contains(w.Target) ? w.Target : targets[0], v => Set("ekran düğmesi", () => w.Target = v)), 160));
            }

            var plcs = Plcs;
            if (HmiWidget.Bound(w.Kind) && plcs.Count > 0)
            {
                var plc = plcs.FirstOrDefault(o => o.Id == w.Plc) ?? plcs[0];
                _props.Children.Add(Row("PLC", Combo(plcs, plc, o => Set("PLC", () => w.Plc = o.Id), o => o.Name), 160));
                var vars = HmiElements.PlcVariables(plc);
                if (w.Kind != HmiWidget.Motor)
                    _props.Children.Add(Row(Lang.T(w.Kind == HmiWidget.Trend ? "Seri 1" : "Değişken"), VariableBox(vars, w.Variable, v => Set("değişken", () => w.Variable = v)), 160));
                if (w.Kind == HmiWidget.Trend)
                {
                    foreach (string role in new[] { "2", "3", "4" })
                        _props.Children.Add(Row(Lang.T($"Seri {role}"), VariableBox(vars, w.Role(role), v => Set("seri", () => w.SetRole(role, v))), 160));
                    _props.Children.Add(Row(Lang.T("Süre (s)"), Number(w.Seconds, v => Set("trend süresi", () => w.Seconds = v), 1, 3600), 160));
                }

                if (w.Kind == HmiWidget.Motor)
                {
                    foreach (var (role, name) in new[] { (HmiWidget.RoleStart, "Başlat"), (HmiWidget.RoleStop, "Durdur"), (HmiWidget.RoleRunning, "Çalışıyor"), (HmiWidget.RoleFault, "Arıza") })
                        _props.Children.Add(Row(Lang.T(name), VariableBox(vars, w.Role(role), v => Set(name, () => w.SetRole(role, v))), 160));
                    _props.Children.Add(Caption(Lang.T("Başlat ve Durdur basılı tutuldukça TRUE yazar; Çalışıyor ve Arıza lamba olarak okunur."), 0));
                }
            }
            else if (HmiWidget.Bound(w.Kind))
            {
                _props.Children.Add(Caption(Lang.T("Dünyada PLC yok: öğe bir PLC değişkenine bağlanınca canlanır."), 0));
            }

            if (w.Kind is HmiWidget.Bar or HmiWidget.Gauge or HmiWidget.SetPoint or HmiWidget.Trend)
            {
                _props.Children.Add(Overline(Lang.T("Değer aralığı")));
                _props.Children.Add(Row(Lang.T("En küçük"), Number(w.Min, v => Set("aralık", () => w.Min = v), double.MinValue, double.MaxValue, true), 160));
                _props.Children.Add(Row(Lang.T("En büyük"), Number(w.Max, v => Set("aralık", () => w.Max = v), double.MinValue, double.MaxValue, true), 160));
            }

            if (w.Kind is HmiWidget.Number or HmiWidget.Bar or HmiWidget.Gauge or HmiWidget.SetPoint)
                _props.Children.Add(Row(Lang.T("Birim"), Text(w.Unit, v => Set("birim", () => w.Unit = v.Trim())), 160));

            _props.Children.Add(Overline(Lang.T("Konum ve boyut")));
            _props.Children.Add(Row("X", Number(w.X, v => Set("konum", () => w.X = Clamp(v, 0, Panel.Width - w.Width)), 0, 10000, true), 160));
            _props.Children.Add(Row("Y", Number(w.Y, v => Set("konum", () => w.Y = Clamp(v, 0, Panel.Height - w.Height)), 0, 10000, true), 160));
            _props.Children.Add(Row(Lang.T("Genişlik"), Number(w.Width, v => Set("boyut", () => w.Width = Math.Max(40, v)), 40, 4000), 160));
            _props.Children.Add(Row(Lang.T("Yükseklik"), Number(w.Height, v => Set("boyut", () => w.Height = Math.Max(30, v)), 30, 4000), 160));
            _props.Children.Add(Buttons(
                Button(Lang.T("Çoğalt"), (_, _) => Duplicate(w), tip: "Ctrl+D"),
                Button(Lang.T("Sil"), (_, _) => Remove(w), tip: "Delete")));
        }

        /// <summary>[hmi-screens] The screens as chips (the one designed is checked) and "+ Ekran".</summary>
        private void BuildScreens()
        {
            _screens.Children.Clear();
            var wrap = new WrapPanel();
            foreach (string name in Panel.ScreenNames)
            {
                string n = name;
                var chip = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = n, IsChecked = n == Screen, Style = R<Style>("Chip"), Margin = new Thickness(0, 0, 4, 4),
                    ToolTip = Lang.T($"{Widgets.Count(w => Panel.ScreenOf(w) == n)} öğe"),
                };
                chip.Click += (_, _) =>
                {
                    _screen = n;
                    _selected = null;
                    Rebuild();
                };
                wrap.Children.Add(chip);
            }

            var add = new Button { Content = Lang.T("+ Ekran"), Style = R<Style>("Btn.Ghost"), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 4), ToolTip = Lang.T("Panele bir ekran ekler; ekran düğmeleriyle aralarında geçilir") };
            add.Click += (_, _) =>
            {
                var names = Panel.ScreenNames;
                int k = names.Count + 1;
                string name = Lang.T($"Ekran {k}");
                while (names.Contains(name)) name = Lang.T($"Ekran {++k}");
                _edit($"HMI ekranı eklendi: {name}", () =>
                {
                    var panel = _world.HmiPanel ??= new HmiPanelSettings();
                    panel.Screens = new List<string>(panel.ScreenNames) { name };
                });
                _screen = name;
                _selected = null;
                Rebuild();
            };
            wrap.Children.Add(add);
            _screens.Children.Add(wrap);
        }

        /// <summary>A variable: one of the PLC's (its program compiled now) or an address typed in (%MW3).</summary>
        private static ComboBox VariableBox(List<string> vars, string value, Action<string> commit)
        {
            var box = new ComboBox { IsEditable = true, ItemsSource = new[] { string.Empty }.Concat(vars).ToList(), Text = value, ToolTip = Lang.T("Programdaki değişken ya da %MW3 gibi bir adres") };
            string current = value;
            void Commit()
            {
                string v = (box.Text ?? string.Empty).Trim();
                if (v == current) return;
                current = v;
                commit(v);
            }

            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is string s)
                {
                    box.Text = s;
                    Commit();
                }
            };
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
            };
            return box;
        }

        // ---------------------------------------------------------------- alarms

        private void BuildAlarms()
        {
            _alarms.Children.Clear();
            _alarms.Children.Add(new TextBlock
            {
                Text = Lang.T("Her alarm bir PLC değişkenine bağlıdır: düzeyi (HH çok yüksek, H yüksek, L düşük, LL çok düşük, mesaj), koşulu ve eşiği, operatörün okuyacağı metni. Çalışırken koşul tutunca gelir, bırakınca gider; panelin alarm listesinde onaylanana kadar kalır."),
                TextWrapping = TextWrapping.Wrap, Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 0, 0, 10),
            });
            var plcs = Plcs;
            if (plcs.Count == 0)
            {
                _alarms.Children.Add(new TextBlock { Text = Lang.T("Dünyada PLC yok: alarmlar PLC değişkenlerine bağlanır."), Foreground = R<Brush>("B.Warning") });
                return;
            }

            var head = AlarmRow();
            foreach (var (text, col) in new[] { ("PLC", 0), ("Değişken", 1), ("Düzey", 2), ("Koşul", 3), ("Eşik", 4), ("Metin", 5) })
            {
                var t = new TextBlock { Text = Lang.T(text), Style = R<Style>("Text.Caption") };
                Grid.SetColumn(t, col);
                head.Children.Add(t);
            }

            _alarms.Children.Add(head);
            foreach (var a in _world.HmiAlarms ?? new List<HmiAlarm>())
            {
                var alarm = a;
                void Set(Action change)
                {
                    _edit("HMI alarmı değişti", change);
                    BuildAlarms();
                }

                var row = AlarmRow();
                var plc = plcs.FirstOrDefault(o => o.Id == alarm.Plc) ?? plcs[0];
                var cells = new FrameworkElement[]
                {
                    Combo(plcs, plc, o => Set(() => alarm.Plc = o.Id), o => o.Name),
                    VariableBox(HmiElements.PlcVariables(plc), alarm.Variable, v => Set(() => alarm.Variable = v)),
                    Combo(HmiAlarm.Levels, alarm.Level, v => Set(() => alarm.Level = v)),
                    Combo(HmiAlarm.Conditions, alarm.Condition, v => Set(() => alarm.Condition = v)),
                    Number(alarm.Threshold, v => Set(() => alarm.Threshold = v), double.MinValue, double.MaxValue, true),
                    Text(alarm.Text, v => Set(() => alarm.Text = v.Trim())),
                };
                for (int i = 0; i < cells.Length; i++)
                {
                    cells[i].Margin = new Thickness(0, 0, 6, 0);
                    Grid.SetColumn(cells[i], i);
                    row.Children.Add(cells[i]);
                }

                var del = new Button { Content = "×", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(6, 0, 6, 0), ToolTip = Lang.T("Alarmı sil") };
                del.Click += (_, _) => Set(() =>
                {
                    _world.HmiAlarms?.Remove(alarm);
                    if (_world.HmiAlarms is { Count: 0 }) _world.HmiAlarms = null;
                });
                Grid.SetColumn(del, 6);
                row.Children.Add(del);
                _alarms.Children.Add(row);
            }

            var add = Button(Lang.T("+ Alarm ekle"), (_, _) =>
            {
                _edit("HMI alarmı eklendi", () => (_world.HmiAlarms ??= new List<HmiAlarm>()).Add(new HmiAlarm { Plc = plcs[0].Id, Level = HmiAlarm.High, Condition = ">=", Threshold = 1 }));
                BuildAlarms();
            });
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Margin = new Thickness(0, 8, 0, 0);
            _alarms.Children.Add(add);
        }

        private static Grid AlarmRow()
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            foreach (double width in new[] { 150.0, 170, 80, 70, 90, 0, 36 })
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = width > 0 ? new GridLength(width) : new GridLength(1, GridUnitType.Star) });
            return g;
        }

        /// <summary>
        /// The world changed from outside (an undo or redo puts a new copy in its place, another tab became active): the
        /// designer follows the active world, the selection kept by its place in the list; with no world it closes.
        /// </summary>
        internal void Rebind(WorldDocument? world)
        {
            if (world == null)
            {
                Close();
                return;
            }

            int index = _selected != null ? Widgets.IndexOf(_selected) : -1;
            _world = world;
            _selected = index >= 0 && index < Widgets.Count ? Widgets[index] : null;
            Rebuild();
            BuildAlarms();
        }

        /// <summary>--snapshot: the designer on the world's panel, the first element selected.</summary>
        internal void SelectFirstForSnapshot()
        {
            _selected = OnScreen.FirstOrDefault(w => w.Kind == HmiWidget.Gauge) ?? OnScreen.FirstOrDefault();
            Rebuild();
        }
    }
}
