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
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// [sketch] The dimensioned sketch of an extrusion: lines, rectangles, circles and arcs drawn on a millimetre grid, held
    /// by relations (level, upright, parallel, square, equal, tangent, on, mid, mirrored, fixed) and driving dimensions.
    /// Every change is solved at once; the status says how much freedom is left, and a constraint that contradicts the
    /// others is refused with the ones it fights named. Its closed loops become the profile.
    /// </summary>
    internal sealed class SketchEditorWindow : Window
    {
        private enum Tool
        {
            Select,
            Line,
            Rectangle,
            Circle,
            Arc,

            /// <summary>[sketch-trim] Power trim: the clicked piece of a line, between the crossings either side, goes.</summary>
            Trim,

            /// <summary>[sketch-trim] A line's free end runs on to the first entity it meets.</summary>
            Extend
        }

        private const double SnapPx = 9;

        /// <summary>[sketch-fillet] The last fillet radius or chamfer setback (mm).</summary>
        private static double _cornerSize = 3;

        private ConstrainedSketch _sk;
        private readonly Action<ConstrainedSketch> _apply;
        private readonly Canvas _canvas = new Canvas { ClipToBounds = true };
        private readonly Border _canvasHost;
        private readonly StackPanel _relations = new StackPanel();
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _state = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock _hint = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        private readonly CheckBox _construction = new CheckBox { Content = "Yardımcı geometri", Margin = new Thickness(0, 8, 0, 0) };
        private readonly Dictionary<Tool, ToggleButton> _tools = new Dictionary<Tool, ToggleButton>();
        private readonly HashSet<string> _selection = new HashSet<string>(StringComparer.Ordinal);
        private Tool _tool = Tool.Select;
        private SketchSolveResult? _result;
        private string? _message;

        // The view: millimetres to pixels about an origin.
        private double _scale = 6, _ox = 260, _oy = 260;
        private bool _framed;

        // A tool part way through: the points clicked so far.
        private readonly List<string> _clicks = new List<string>();
        private Point _mouse;

        // Dragging a point, or the view.
        private string? _dragPoint;
        private ConstrainedSketch? _dragStart;
        private Point? _panFrom;

        public SketchEditorWindow(Window owner, ConstrainedSketch sketch, Action<ConstrainedSketch> apply)
        {
            _sk = sketch.Clone();
            _apply = apply;
            Owner = owner;
            Title = "Ölçülü eskiz";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = 1120;
            Height = 720;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");

            var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock { Text = "Ölçülü eskiz", Style = R<Style>("Text.Title"), FontSize = 18 });
            head.Children.Add(new TextBlock
            {
                Text = "Kabaca çizin, sonra ilişki ve ölçü verin: çizim ölçülere oturur. Kapalı çizgi ve yay döngüleri profil olur; en büyüğü dış şekil, içindekiler delik.",
                Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var cancel = new Button { Content = "Vazgeç", Style = R<Style>("Btn.Ghost"), MinWidth = 96, IsCancel = true };
            cancel.Click += (_, _) => Close();
            var ok = new Button { Content = "Uygula", Style = R<Style>("Btn.Primary"), MinWidth = 96, Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (_, _) =>
            {
                if (_result?.State == SketchState.Conflicting) return;
                _apply(_sk.Clone());
                Close();
            };
            footer.Children.Add(cancel);
            footer.Children.Add(ok);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Left: the tools.
            var left = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 12, 0) };
            left.Children.Add(new TextBlock { Text = "Çizim", Style = R<Style>("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            foreach (var (tool, name, tip) in new[]
            {
                (Tool.Select, "Seç / taşı", "Nokta ya da çizgi seçin (Ctrl ile çoklu), noktayı sürükleyin"),
                (Tool.Line, "Çizgi", "Tıklaya tıklaya çizgi zinciri; Esc ya da sağ tık bitirir"),
                (Tool.Rectangle, "Dikdörtgen", "İki köşe"),
                (Tool.Circle, "Daire", "Merkez, sonra çevre"),
                (Tool.Arc, "Yay", "Merkez, başlangıç, bitiş (saat yönünün tersine)"),
                (Tool.Trim, "Buda", "Çizginin, dairenin ya da yayın tıklanan parçasını kesiştiği öğeler arasında siler; hiçbir şey kesmiyorsa tamamını"),
                (Tool.Extend, "Uzat", "Çizginin tıklanan yakınındaki serbest ucunu ilk kesiştiği öğeye uzatır")
            })
            {
                var t = tool;
                var b = new ToggleButton { Content = name, ToolTip = tip, Style = R<Style>("List.Item"), Margin = new Thickness(0, 0, 0, 2) };
                b.Click += (_, _) => SetTool(t);
                _tools[t] = b;
                left.Children.Add(b);
            }

            _construction.Style = R<Style>("Switch");
            left.Children.Add(_construction);
            left.Children.Add(new TextBlock
            {
                Text = "Yakın noktaya tıklamak onu paylaşır; neredeyse yatay ya da dikey çizilen çizgi öyle kalır.",
                Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0)
            });
            DockPanel.SetDock(left, Dock.Left);
            root.Children.Add(left);

            // Right: relations, dimensions, the list and the state.
            var right = new StackPanel { Width = 300, Margin = new Thickness(12, 0, 0, 0) };
            right.Children.Add(new TextBlock { Text = "İlişkiler ve ölçüler", Style = R<Style>("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            right.Children.Add(_relations);
            right.Children.Add(_state);
            right.Children.Add(_hint);
            right.Children.Add(new TextBlock { Text = "Kısıtlar", Style = R<Style>("Text.Overline"), Margin = new Thickness(0, 12, 0, 6) });
            right.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(rightScroll, Dock.Right);
            root.Children.Add(rightScroll);

            _canvasHost = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _canvas, Focusable = true, ClipToBounds = true };
            _canvasHost.SetResourceReference(Border.BackgroundProperty, "B.Canvas");
            _canvasHost.SetResourceReference(Border.BorderBrushProperty, "B.Border");
            _canvas.Background = Brushes.Transparent;
            root.Children.Add(_canvasHost);

            var card = new Border
            {
                Margin = new Thickness(20),
                Background = R<Brush>("B.Overlay"),
                BorderBrush = R<Brush>("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Child = root
            };
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = card;
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");

            _canvas.MouseLeftButtonDown += OnDown;
            _canvas.MouseMove += OnMove;
            _canvas.MouseLeftButtonUp += OnUp;
            _canvas.MouseRightButtonDown += (_, e) =>
            {
                if (_clicks.Count > 0) EndTool();
                else _panFrom = e.GetPosition(_canvas);
                _canvas.CaptureMouse();
            };
            _canvas.MouseRightButtonUp += (_, _) =>
            {
                _panFrom = null;
                _canvas.ReleaseMouseCapture();
            };
            _canvas.MouseWheel += OnWheel;
            _canvas.SizeChanged += (_, _) =>
            {
                if (!_framed && _canvas.ActualWidth > 0) Frame();
                Redraw();
            };
            PreviewKeyDown += OnKey;

            SetTool(Tool.Select);
            Solve(null);
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        private static Brush B(string key) => (Brush)Application.Current.FindResource(key);

        private static string F(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

        // ---- view ------------------------------------------------------------------------------------------------

        private Point Screen(double x, double y) => new Point(_ox + x * _scale, _oy - y * _scale);

        private (double X, double Y) Model(Point p) => ((p.X - _ox) / _scale, (_oy - p.Y) / _scale);

        /// <summary>Fits the drawing (or 100 mm round the origin) into the canvas.</summary>
        private void Frame()
        {
            double w = Math.Max(100, _canvas.ActualWidth), h = Math.Max(100, _canvas.ActualHeight);
            double minX = -50, maxX = 50, minY = -50, maxY = 50;
            if (_sk.Points.Count > 0)
            {
                minX = _sk.Points.Min(p => p.X);
                maxX = _sk.Points.Max(p => p.X);
                minY = _sk.Points.Min(p => p.Y);
                maxY = _sk.Points.Max(p => p.Y);
                foreach (var e in _sk.Entities.Where(e => e.Kind != SketchEntityKind.Line))
                {
                    var c = _sk.Point(e.Center);
                    if (c == null) continue;
                    minX = Math.Min(minX, c.X - e.Radius);
                    maxX = Math.Max(maxX, c.X + e.Radius);
                    minY = Math.Min(minY, c.Y - e.Radius);
                    maxY = Math.Max(maxY, c.Y + e.Radius);
                }
            }

            double spanX = Math.Max(10, maxX - minX), spanY = Math.Max(10, maxY - minY);
            _scale = Math.Max(0.2, Math.Min(60, 0.75 * Math.Min(w / spanX, h / spanY)));
            _ox = w / 2 - (minX + maxX) / 2 * _scale;
            _oy = h / 2 + (minY + maxY) / 2 * _scale;
            _framed = true;
        }

        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            var at = e.GetPosition(_canvas);
            var (mx, my) = Model(at);
            _scale = Math.Max(0.2, Math.Min(200, _scale * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
            _ox = at.X - mx * _scale;
            _oy = at.Y + my * _scale;
            Redraw();
        }

        // ---- tools -----------------------------------------------------------------------------------------------

        private void SetTool(Tool t)
        {
            _tool = t;
            _clicks.Clear();
            foreach (var kv in _tools) kv.Value.IsChecked = kv.Key == t;
            _hint.Text = t switch
            {
                Tool.Line => "Çizgi: başlangıca, sonra her köşeye tıklayın. Esc ya da sağ tık bitirir.",
                Tool.Rectangle => "Dikdörtgen: bir köşeye, sonra karşı köşeye tıklayın.",
                Tool.Circle => "Daire: merkeze, sonra çevresinde bir yere tıklayın.",
                Tool.Arc => "Yay: merkez, başlangıç ve bitiş (saat yönünün tersine).",
                _ => "Seçin: noktalar ve çizgiler; sürükleyerek noktayı taşıyın. Sağ tuşla sürükleme görünümü kaydırır, tekerlek yakınlaştırır."
            };
            Redraw();
        }

        private void EndTool()
        {
            _clicks.Clear();
            Redraw();
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox) return;
            if (e.Key == Key.Escape)
            {
                if (_clicks.Count > 0) EndTool();
                else if (_tool != Tool.Select) SetTool(Tool.Select);
                else
                {
                    _selection.Clear();
                    Refresh();
                }

                e.Handled = true;
            }
            else if (e.Key == Key.Delete && _selection.Count > 0)
            {
                Change("Silindi", () =>
                {
                    foreach (var id in _selection.ToList()) _sk.Remove(id);
                    _selection.Clear();
                });
                e.Handled = true;
            }
        }

        /// <summary>The point under the cursor (within the snap distance), if any.</summary>
        private SketchPoint? PointAt(Point p, string? except = null)
        {
            SketchPoint? best = null;
            double bestD = SnapPx;
            foreach (var q in _sk.Points)
            {
                if (q.Id == except) continue;
                double d = (Screen(q.X, q.Y) - p).Length;
                if (d < bestD)
                {
                    bestD = d;
                    best = q;
                }
            }

            return best;
        }

        /// <summary>The line, circle or arc under the cursor, if any.</summary>
        private SketchEntity? EntityAt(Point p)
        {
            var (x, y) = Model(p);
            double tol = 6 / _scale;
            SketchEntity? best = null;
            double bestD = tol;
            foreach (var e in _sk.Entities)
            {
                double d = double.MaxValue;
                if (e.Kind == SketchEntityKind.Line && _sk.Point(e.Start) is { } a && _sk.Point(e.End) is { } b)
                {
                    double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                    double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - a.X) * dx + (y - a.Y) * dy) / l2));
                    d = Math.Sqrt(Math.Pow(a.X + t * dx - x, 2) + Math.Pow(a.Y + t * dy - y, 2));
                }
                else if (_sk.Point(e.Center) is { } c)
                {
                    double r = Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y));
                    d = Math.Abs(r - e.Radius);
                    if (e.Kind == SketchEntityKind.Arc && !OnArc(e, c, Math.Atan2(y - c.Y, x - c.X))) d = double.MaxValue;
                }

                if (d < bestD)
                {
                    bestD = d;
                    best = e;
                }
            }

            return best;
        }

        private bool OnArc(SketchEntity e, SketchPoint c, double angle)
        {
            var s = _sk.Point(e.Start);
            var t = _sk.Point(e.End);
            if (s == null || t == null) return false;
            double a0 = Math.Atan2(s.Y - c.Y, s.X - c.X), a1 = Math.Atan2(t.Y - c.Y, t.X - c.X);
            double sweep = Norm(a1 - a0), at = Norm(angle - a0);
            return at <= sweep;
        }

        private static double Norm(double a)
        {
            while (a < 0) a += 2 * Math.PI;
            while (a >= 2 * Math.PI) a -= 2 * Math.PI;
            return a;
        }

        /// <summary>A clicked point: an existing one near the cursor, or a new one there.</summary>
        private SketchPoint Pick(Point p)
        {
            if (PointAt(p) is { } q) return q;
            var (x, y) = Model(p);
            double grid = GridStep() / 5;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) grid = 0;
            if (grid > 0)
            {
                x = Math.Round(x / grid) * grid;
                y = Math.Round(y / grid) * grid;
            }

            return _sk.AddPoint(x, y);
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _canvasHost.Focus();
            Press(e.GetPosition(_canvas), Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }

        /// <summary>
        /// [sketch] A scripted session for the snapshot and its check: each step a tool and a click in millimetres
        /// ("line", "rect", "circle", "arc", "select", "add" adds to the selection), or "relate" with a relation's name
        /// pressed as its button is.
        /// </summary>
        internal void Script(IEnumerable<(string Step, double X, double Y, string? Name)> steps)
        {
            foreach (var (step, x, y, name) in steps)
            {
                void Use(Tool t)
                {
                    if (_tool != t) SetTool(t);   // the same tool again goes on with its clicks
                }

                switch (step)
                {
                    case "line": Use(Tool.Line); break;
                    case "rect": Use(Tool.Rectangle); break;
                    case "circle": Use(Tool.Circle); break;
                    case "arc": Use(Tool.Arc); break;
                    case "trim": Use(Tool.Trim); break;   // [sketch-trim]
                    case "extend": Use(Tool.Extend); break;
                    case "select": Use(Tool.Select); break;
                    case "add": Use(Tool.Select); break;
                    case "end": EndTool(); break;
                    case "construction": _construction.IsChecked = _construction.IsChecked != true; continue;   // [sketch-mirror] the toggle
                    case "relate":
                        foreach (var b in _relations.Children.OfType<Panel>().SelectMany(w => w.Children.OfType<Button>()))   // [sketch-fillet] the corner row too
                        {
                            if (b.Content as string != name) continue;
                            b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            break;
                        }

                        continue;
                    case "value":
                        // The constraint list's box for a dimension, typed into and committed.
                        var k = _sk.Constraints.LastOrDefault(c => Dimensional(c.Kind));
                        if (k != null && double.TryParse(name, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) Change(KindName(k.Kind), () => k.Value = v);
                        continue;
                }

                if (step == "end") continue;
                var at = Screen(x, y);
                _mouse = at;
                Press(at, step == "add");
                _dragPoint = null;
                _dragStart = null;
            }
        }

        /// <summary>The sketch as the editor has it now.</summary>
        internal ConstrainedSketch Current => _sk;

        internal SketchSolveResult? Result => _result;

        private void Press(Point p, bool add)
        {
            if (_tool == Tool.Select)
            {
                string? hit = PointAt(p)?.Id ?? EntityAt(p)?.Id;
                if (!add) _selection.Clear();
                if (hit != null)
                {
                    if (add && _selection.Contains(hit)) _selection.Remove(hit);
                    else _selection.Add(hit);
                }

                if (hit != null && _sk.Point(hit) != null)
                {
                    _dragPoint = hit;
                    _dragStart = _sk.Clone();
                    if (_canvas.IsLoaded) _canvas.CaptureMouse();
                }

                Refresh();
                return;
            }

            if (_tool is Tool.Trim or Tool.Extend)
            {
                // [sketch-trim] on the line under the pointer, at the click
                var target = EntityAt(p);
                if (target == null || (_tool == Tool.Extend && target.Kind != SketchEntityKind.Line))   // [sketch-trim] trim takes circles and arcs too
                {
                    _message = _tool == Tool.Trim ? "Budamak için bir çizgiye, daireye ya da yaya tıklayın." : "Uzatmak için bir çizgiye tıklayın.";
                    ShowState();
                    return;
                }

                var (mx, my) = Model(p);
                string? why = null;
                Change(_tool == Tool.Trim ? "Buda" : "Uzat", () =>
                {
                    why = _tool == Tool.Trim ? SketchTrim.Trim(_sk, target.Id, mx, my) : SketchTrim.Extend(_sk, target.Id, mx, my);
                    _selection.Clear();
                });
                if (why != null)
                {
                    _message = why;
                    ShowState();
                }

                return;
            }

            var before = _sk.Clone();
            bool construction = _construction.IsChecked == true;
            switch (_tool)
            {
                case Tool.Line:
                {
                    var q = Pick(p);
                    if (_clicks.Count > 0 && _sk.Point(_clicks[^1]) is { } prev && prev.Id != q.Id)
                    {
                        var line = _sk.AddLine(prev, q, construction);
                        AutoLevel(line, prev, q);
                        _clicks.Clear();
                        _clicks.Add(q.Id);
                        Commit(before, "Çizgi");
                        // Back to where the chain started: it is closed.
                        if (_chainStart == q.Id) EndTool();
                        return;
                    }

                    _clicks.Clear();
                    _clicks.Add(q.Id);
                    _chainStart = q.Id;
                    break;
                }

                case Tool.Rectangle:
                {
                    var (x, y) = Model(p);
                    if (_clicks.Count == 0)
                    {
                        _clicks.Add(Pick(p).Id);
                        break;
                    }

                    var a = _sk.Point(_clicks[0])!;
                    if (Math.Abs(x - a.X) * _scale < 4 || Math.Abs(y - a.Y) * _scale < 4) break;
                    var b = _sk.AddPoint(x, a.Y);
                    var c = PointAt(p) ?? _sk.AddPoint(x, y);
                    var d = _sk.AddPoint(a.X, y);
                    var lines = new[] { _sk.AddLine(a, b, construction), _sk.AddLine(b, c, construction), _sk.AddLine(c, d, construction), _sk.AddLine(d, a, construction) };
                    _sk.Constrain(SketchConstraintKind.Horizontal, lines[0].Id);
                    _sk.Constrain(SketchConstraintKind.Vertical, lines[1].Id);
                    _sk.Constrain(SketchConstraintKind.Horizontal, lines[2].Id);
                    _sk.Constrain(SketchConstraintKind.Vertical, lines[3].Id);
                    _clicks.Clear();
                    Commit(before, "Dikdörtgen");
                    return;
                }

                case Tool.Circle:
                {
                    if (_clicks.Count == 0)
                    {
                        _clicks.Add(Pick(p).Id);
                        break;
                    }

                    var c = _sk.Point(_clicks[0])!;
                    var (x, y) = Model(p);
                    double r = Math.Sqrt((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y));
                    if (r * _scale < 3) break;
                    _sk.AddCircle(c, Round(r), construction);
                    _clicks.Clear();
                    Commit(before, "Daire");
                    return;
                }

                case Tool.Arc:
                {
                    if (_clicks.Count < 2)
                    {
                        _clicks.Add(Pick(p).Id);
                        break;
                    }

                    var c = _sk.Point(_clicks[0])!;
                    var s = _sk.Point(_clicks[1])!;
                    double r = Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Y - c.Y) * (s.Y - c.Y));
                    var end = PointAt(p);
                    if (end == null)
                    {
                        var (x, y) = Model(p);
                        double a = Math.Atan2(y - c.Y, x - c.X);
                        end = _sk.AddPoint(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
                    }

                    if (end.Id == s.Id || r < 1e-6) break;
                    _sk.AddArc(c, s, end, construction);
                    _clicks.Clear();
                    Commit(before, "Yay");
                    return;
                }
            }

            Redraw();
        }

        private string? _chainStart;

        private double Round(double v)
        {
            double step = GridStep() / 10;
            return step > 0 ? Math.Max(step, Math.Round(v / step) * step) : v;
        }

        /// <summary>A line drawn within 3° of level or upright stays so.</summary>
        private void AutoLevel(SketchEntity line, SketchPoint a, SketchPoint b)
        {
            double angle = Math.Abs(Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI);
            if (angle < 3 || angle > 177) _sk.Constrain(SketchConstraintKind.Horizontal, line.Id);
            else if (Math.Abs(angle - 90) < 3) _sk.Constrain(SketchConstraintKind.Vertical, line.Id);
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            var p = e.GetPosition(_canvas);
            if (_panFrom is { } from && e.RightButton == MouseButtonState.Pressed)
            {
                _ox += p.X - from.X;
                _oy += p.Y - from.Y;
                _panFrom = p;
                Redraw();
                return;
            }

            _mouse = p;
            if (_dragPoint != null && e.LeftButton == MouseButtonState.Pressed && _dragStart != null)
            {
                // The point goes where the cursor is, held there while the rest is solved round it.
                var trial = _dragStart.Clone();
                var q = trial.Point(_dragPoint)!;
                var (x, y) = Model(p);
                q.X = x;
                q.Y = y;
                var hold = trial.Constrain(SketchConstraintKind.Fix, q.Id);
                var r = trial.Solve(explain: false);
                trial.Constraints.Remove(hold);
                if (r.State == SketchState.Conflicting)
                {
                    // Pinned by its constraints: it stays.
                    _message = "Bu nokta kısıtlarıyla sabit; taşınamaz.";
                }
                else
                {
                    _sk = trial;
                    _message = null;
                }

                Redraw();
                return;
            }

            if (_clicks.Count > 0) Redraw();
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (_dragPoint != null)
            {
                _dragPoint = null;
                _dragStart = null;
                _canvas.ReleaseMouseCapture();
                Solve(null);
            }
        }

        // ---- changes ---------------------------------------------------------------------------------------------

        /// <summary>Solves after a change; a change that makes the sketch contradict itself is taken back.</summary>
        private void Commit(ConstrainedSketch before, string what)
        {
            Solve(before, what);
        }

        private void Change(string what, Action change)
        {
            var before = _sk.Clone();
            change();
            Solve(before, what);
        }

        private void Solve(ConstrainedSketch? before, string? what = null)
        {
            var r = _sk.Solve();
            if (r.State == SketchState.Conflicting && before != null)
            {
                var names = r.Culprits.Select(Describe).ToList();
                _sk = before;
                _result = _sk.Solve();
                _message = $"{what ?? "Değişiklik"} geri alındı, kısıtlar çelişiyor" + (names.Count > 0 ? ": " + string.Join(" · ", names) : "");
            }
            else
            {
                _result = r;
                _message = null;
            }

            Refresh();
        }

        private void Refresh()
        {
            FillRelations();
            FillList();
            ShowState();
            Redraw();
        }

        private void ShowState()
        {
            var r = _result;
            if (r == null) return;
            string text;
            string brush;
            switch (r.State)
            {
                case SketchState.FullyConstrained:
                    text = "Tam kısıtlı: her nokta ve yarıçap belirli.";
                    brush = "B.Success";
                    break;
                case SketchState.Redundant:
                    text = "Başka kısıtları tekrarlıyor: " + string.Join(" · ", r.Culprits.Select(Describe));
                    brush = "B.Warning";
                    break;
                case SketchState.Conflicting:
                    text = "Kısıtlar çelişiyor: " + string.Join(" · ", r.Culprits.Select(Describe));
                    brush = "B.Danger";
                    break;
                default:
                    text = r.Freedom == 1 ? "1 serbestlik kaldı." : $"{r.Freedom} serbestlik kaldı.";
                    brush = "B.TextSecondary";
                    break;
            }

            _state.Text = _message != null ? _message + "\n" + text : text;
            _state.SetResourceReference(TextBlock.ForegroundProperty, _message != null ? "B.Danger" : brush);
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
        }

        // ---- relations and dimensions for the selection ----------------------------------------------------------

        private List<SketchPoint> SelPoints => _selection.Select(id => _sk.Point(id)).Where(p => p != null).Select(p => p!).ToList();

        private List<SketchEntity> SelEntities => _selection.Select(id => _sk.Entity(id)).Where(e => e != null).Select(e => e!).ToList();

        private static int _patternCount = 3;
        private static double _patternDx = 10, _patternDy, _patternAngle = 360;

        /// <summary>[sketch-pattern] The pattern row's starting values (the snapshot demo sets them; the row remembers the last used).</summary>
        internal static void PatternDefaults(int count, double dx, double dy, double angle)
        {
            _patternCount = count;
            _patternDx = dx;
            _patternDy = dy;
            _patternAngle = angle;
        }

        /// <summary>
        /// [sketch-pattern] The pattern row under the relations: the count (the original included) and either the step in
        /// X and Y (mm) or, with a centre point chosen, the angle the copies spread over (360° = all the way round).
        /// </summary>
        private void PatternRow(List<SketchEntity> ents, SketchPoint? centre)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            TextBox Box(string label, double value, string unit, string tip)
            {
                row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Style = R<Style>("Text.Caption") });
                var b = new TextBox { Text = value.ToString("0.##", CultureInfo.InvariantCulture), Width = 48, HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = tip, Margin = new Thickness(0, 0, 2, 4) };
                row.Children.Add(b);
                row.Children.Add(new TextBlock { Text = unit, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Style = R<Style>("Text.Caption") });
                return b;
            }

            static bool Read(TextBox b, out double v) => double.TryParse(b.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            var count = Box("Adet", _patternCount, "", "Kaç tane (aslı dahil)");
            TextBox? dx = null, dy = null, angle = null;
            if (centre == null)
            {
                dx = Box("X", _patternDx, "mm", "Her kopyanın bir öncekine X adımı");
                dy = Box("Y", _patternDy, "mm", "Her kopyanın bir öncekine Y adımı");
            }
            else angle = Box("Açı", _patternAngle, "°", "Kopyaların yayıldığı açı; 360° tam tur");

            string name = centre == null ? "Doğrusal çoğalt" : "Dairesel çoğalt";
            var go = new Button
            {
                Content = name, Style = R<Style>("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 4), Padding = new Thickness(8, 4, 8, 4),
                ToolTip = centre == null
                    ? "Seçilenleri adım adım sıralar; kopyalar ölçülerle aslına bağlı, aslı değişince izler"
                    : "Seçilenleri seçili noktanın çevresinde döndürerek çoğaltır; kopyalar açı ölçüsüyle aslını izler"
            };
            go.Click += (_, _) =>
            {
                if (!Read(count, out double n) || n < 2 || n > 500) return;
                _patternCount = (int)Math.Round(n);
                string? why = null;
                var ids = ents.Select(e => e.Id).ToList();
                if (centre == null)
                {
                    if (!Read(dx!, out double x) || !Read(dy!, out double y)) return;
                    _patternDx = x;
                    _patternDy = y;
                    Change(name, () =>
                    {
                        why = SketchPattern.Linear(_sk, ids, _patternCount, x, y);
                        if (why == null) _selection.Clear();
                    });
                }
                else
                {
                    if (!Read(angle!, out double a) || Math.Abs(a) < 1e-9) return;
                    _patternAngle = a;
                    Change(name, () =>
                    {
                        why = SketchPattern.Circular(_sk, ids, centre.Id, _patternCount, a);
                        if (why == null) _selection.Clear();
                    });
                }

                if (why != null)
                {
                    _message = why;
                    ShowState();
                }
            };
            row.Children.Add(go);
            _relations.Children.Add(row);
        }

        private void FillRelations()
        {
            _relations.Children.Clear();
            var pts = SelPoints;
            var ents = SelEntities;
            var lines = ents.Where(e => e.Kind == SketchEntityKind.Line).ToList();
            var rounds = ents.Where(e => e.Kind != SketchEntityKind.Line).ToList();
            var wrap = new WrapPanel();
            void Rel(string name, string tip, Action add)
            {
                var b = new Button { Content = name, ToolTip = tip, Style = R<Style>("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 4, 8, 4) };
                b.Click += (_, _) => Change(name, add);
                wrap.Children.Add(b);
            }

            if (lines.Count >= 1 && pts.Count == 0 && rounds.Count == 0)
            {
                Rel("Yatay", "Seçili çizgiler yatay", () => lines.ForEach(l => _sk.Constrain(SketchConstraintKind.Horizontal, l.Id)));
                Rel("Dikey", "Seçili çizgiler dikey", () => lines.ForEach(l => _sk.Constrain(SketchConstraintKind.Vertical, l.Id)));
            }

            if (lines.Count == 2 && pts.Count == 0 && rounds.Count == 0)
            {
                Rel("Paralel", "İki çizgi paralel", () => _sk.Constrain(SketchConstraintKind.Parallel, lines[0].Id, lines[1].Id));
                Rel("Dik", "İki çizgi birbirine dik", () => _sk.Constrain(SketchConstraintKind.Perpendicular, lines[0].Id, lines[1].Id));
                Rel("Eşit", "İki çizgi aynı boyda", () => _sk.Constrain(SketchConstraintKind.Equal, lines[0].Id, lines[1].Id));
            }

            if (rounds.Count == 2 && lines.Count == 0 && pts.Count == 0)
            {
                Rel("Eşit", "Aynı yarıçap", () => _sk.Constrain(SketchConstraintKind.Equal, rounds[0].Id, rounds[1].Id));
                Rel("Eşmerkezli", "Aynı merkez", () => _sk.Constrain(SketchConstraintKind.Concentric, rounds[0].Id, rounds[1].Id));
                Rel("Teğet", "Dıştan değer", () => _sk.Constrain(SketchConstraintKind.Tangent, rounds[0].Id, rounds[1].Id, TangentSide(rounds[0], rounds[1])));
            }

            if (lines.Count == 1 && rounds.Count == 1 && pts.Count == 0)
                Rel("Teğet", "Çizgi daireye değer", () => _sk.Constrain(SketchConstraintKind.Tangent, lines[0].Id, rounds[0].Id, TangentSide(lines[0], rounds[0])));
            if (pts.Count == 2 && ents.Count == 0)
            {
                Rel("Çakışık", "İki nokta aynı yerde", () => _sk.Constrain(SketchConstraintKind.Coincident, pts[0].Id, pts[1].Id));
                Rel("Yatay hizalı", "Aynı yükseklikte", () => _sk.Constrain(SketchConstraintKind.VerticalDistance, pts[0].Id, pts[1].Id, 0));
                Rel("Dikey hizalı", "Aynı X'te", () => _sk.Constrain(SketchConstraintKind.HorizontalDistance, pts[0].Id, pts[1].Id, 0));
            }

            if (pts.Count == 1 && lines.Count == 1 && rounds.Count == 0)
            {
                Rel("Çizgi üstünde", "Nokta çizginin doğrultusunda", () => _sk.Constrain(SketchConstraintKind.PointOnLine, pts[0].Id, lines[0].Id));
                Rel("Orta nokta", "Nokta çizginin ortasında", () => _sk.Constrain(SketchConstraintKind.Midpoint, pts[0].Id, lines[0].Id));
            }

            if (pts.Count == 1 && rounds.Count == 1 && lines.Count == 0)
                Rel("Çember üstünde", "Nokta çemberin üstünde", () => _sk.Constrain(SketchConstraintKind.PointOnCircle, pts[0].Id, rounds[0].Id));
            if (pts.Count == 2 && lines.Count == 1 && rounds.Count == 0)
                Rel("Simetrik", "İki nokta çizgiye göre ayna", () => _sk.Constrain(SketchConstraintKind.Symmetric, pts[0].Id, pts[1].Id, 0, lines[0].Id));
            if (pts.Count >= 1 && ents.Count == 0)
                Rel("Sabitle", "Nokta yerinde kalır", () => pts.ForEach(p => _sk.Constrain(SketchConstraintKind.Fix, p.Id)));

            // [sketch-mirror] SolidWorks' Mirror Entities: the one chosen construction line is the axis
            var axes = lines.Where(l => l.Construction).ToList();
            if (axes.Count == 1 && ents.Count >= 2 && pts.Count == 0)
            {
                var axisLine = axes[0];
                var mirror = new Button { Content = "Aynala", ToolTip = "Seçilenleri seçili yardımcı çizgiye göre aynalar; kopyalar simetri ilişkisiyle aslını izler", Style = R<Style>("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(8, 4, 8, 4) };
                mirror.Click += (_, _) =>
                {
                    string? why = null;
                    Change("Aynala", () =>
                    {
                        why = SketchMirror.Mirror(_sk, ents.Select(e => e.Id).ToList(), axisLine.Id);
                        if (why == null) _selection.Clear();
                    });
                    if (why != null)
                    {
                        _message = why;
                        ShowState();
                    }
                };
                wrap.Children.Add(mirror);
            }

            // Dimensions: the selection's present size, which the user then sets.
            if (lines.Count == 1 && pts.Count == 0 && rounds.Count == 0)
                Rel("Uzunluk", "Çizginin boyu", () => _sk.Constrain(SketchConstraintKind.Length, lines[0].Id, value: Round(LengthOf(lines[0]))));
            if (lines.Count == 2 && pts.Count == 0 && rounds.Count == 0)
                Rel("Açı", "Birinciden ikinciye açı", () => _sk.Constrain(SketchConstraintKind.Angle, lines[0].Id, lines[1].Id, Math.Round(AngleOf(lines[0], lines[1]), 1)));
            if (rounds.Count == 1 && lines.Count == 0 && pts.Count == 0)
            {
                Rel("Çap", "Dairenin çapı", () => _sk.Constrain(SketchConstraintKind.Diameter, rounds[0].Id, value: Round(2 * rounds[0].Radius)));
                Rel("Yarıçap", "Yarıçap", () => _sk.Constrain(SketchConstraintKind.Radius, rounds[0].Id, value: Round(rounds[0].Radius)));
            }

            if (pts.Count == 2 && ents.Count == 0)
            {
                Rel("Mesafe", "İki nokta arası", () => _sk.Constrain(SketchConstraintKind.Distance, pts[0].Id, pts[1].Id, Round(Dist(pts[0], pts[1]))));
                Rel("Yatay ölçü", "X farkı", () => _sk.Constrain(SketchConstraintKind.HorizontalDistance, pts[0].Id, pts[1].Id, Round(pts[1].X - pts[0].X)));
                Rel("Dikey ölçü", "Y farkı", () => _sk.Constrain(SketchConstraintKind.VerticalDistance, pts[0].Id, pts[1].Id, Round(pts[1].Y - pts[0].Y)));
            }

            if (wrap.Children.Count == 0)
            {
                _relations.Children.Add(new TextBlock
                {
                    Text = _selection.Count == 0
                        ? "İlişki ya da ölçü vermek için seçin: bir çizgi (boy, yatay/dikey), iki çizgi (paralel, dik, açı), daire (çap), iki nokta (mesafe)."
                        : "Bu seçimle verilebilecek ilişki yok.",
                    Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap
                });
            }
            else _relations.Children.Add(wrap);

            // [sketch-fillet] a corner of two lines: SolidWorks' Sketch Fillet and Sketch Chamfer, its size typed in mm
            if (pts.Count == 1 && ents.Count == 0
                && _sk.Entities.Count(e => e.Kind == SketchEntityKind.Line && !e.Construction && (e.Start == pts[0].Id || e.End == pts[0].Id)) == 2)
            {
                var corner = pts[0];
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 8) };
                var size = new TextBox { Text = _cornerSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Width = 56, HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Yarıçap ya da pah boyu (mm)" };
                row.Children.Add(size);
                row.Children.Add(new TextBlock { Text = "mm", Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Style = R<Style>("Text.Caption") });
                void CornerButton(string name, string tip, Func<ConstrainedSketch, string, double, string?> cut)
                {
                    var b = new Button { Content = name, ToolTip = tip, Style = R<Style>("Btn.Ghost"), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(8, 4, 8, 4) };
                    b.Click += (_, _) =>
                    {
                        if (!double.TryParse(size.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || v <= 0) return;
                        _cornerSize = v;
                        string? why = null;
                        Change(name, () =>
                        {
                            why = cut(_sk, corner.Id, v);
                            if (why == null) _selection.Clear();
                        });
                        if (why != null)
                        {
                            _message = why;
                            ShowState();
                        }
                    };
                    row.Children.Add(b);
                }

                CornerButton("Köşe radyüsü", "Köşeyi iki çizgiye teğet bir yayla yuvarlatır; köşe çizgilerin uzantısında sanal sivri olarak kalır, ölçüleri korunur", SketchCorners.Fillet);
                CornerButton("Pah", "Köşeyi düz bir kenarla keser (iki çizgide de aynı geri çekme)", SketchCorners.Chamfer);
                _relations.Children.Add(row);
            }

            // [sketch-pattern] SolidWorks' Linear / Circular Sketch Pattern: lines, circles, arcs in a row, or round one chosen point
            if (ents.Count >= 1 && pts.Count <= 1)
                PatternRow(ents, pts.Count == 1 ? pts[0] : null);

            if (_selection.Count > 0)
            {
                var del = new Button { Content = "Seçileni sil", Style = R<Style>("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 8, 4) };
                del.Click += (_, _) => Change("Silindi", () =>
                {
                    foreach (var id in _selection.ToList()) _sk.Remove(id);
                    _selection.Clear();
                });
                _relations.Children.Add(del);
            }
        }

        private double TangentSide(SketchEntity a, SketchEntity b)
        {
            if (a.Kind == SketchEntityKind.Line)
            {
                var s = _sk.Point(a.Start)!;
                var t = _sk.Point(a.End)!;
                var c = _sk.Point(b.Center)!;
                double side = (t.X - s.X) * (c.Y - s.Y) - (t.Y - s.Y) * (c.X - s.X);
                return side < 0 ? -1 : 1;
            }

            var c1 = _sk.Point(a.Center)!;
            var c2 = _sk.Point(b.Center)!;
            double d = Math.Sqrt((c2.X - c1.X) * (c2.X - c1.X) + (c2.Y - c1.Y) * (c2.Y - c1.Y));
            return d < Math.Max(a.Radius, b.Radius) ? -1 : 1;   // one inside the other: inner tangency
        }

        private double LengthOf(SketchEntity line)
        {
            var a = _sk.Point(line.Start)!;
            var b = _sk.Point(line.End)!;
            return Dist(a, b);
        }

        private static double Dist(SketchPoint a, SketchPoint b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

        private double AngleOf(SketchEntity l1, SketchEntity l2)
        {
            double A(SketchEntity l)
            {
                var a = _sk.Point(l.Start)!;
                var b = _sk.Point(l.End)!;
                return Math.Atan2(b.Y - a.Y, b.X - a.X);
            }

            double d = (A(l2) - A(l1)) * 180 / Math.PI;
            while (d <= -180) d += 360;
            while (d > 180) d -= 360;
            return d;
        }

        // ---- the constraint list ---------------------------------------------------------------------------------

        private static bool Dimensional(SketchConstraintKind k) => k is SketchConstraintKind.Distance or SketchConstraintKind.HorizontalDistance or
            SketchConstraintKind.VerticalDistance or SketchConstraintKind.Length or SketchConstraintKind.Radius or SketchConstraintKind.Diameter or SketchConstraintKind.Angle;

        private static string KindName(SketchConstraintKind k) => k switch
        {
            SketchConstraintKind.Coincident => "Çakışık",
            SketchConstraintKind.Horizontal => "Yatay",
            SketchConstraintKind.Vertical => "Dikey",
            SketchConstraintKind.Fix => "Sabit",
            SketchConstraintKind.Distance => "Mesafe",
            SketchConstraintKind.HorizontalDistance => "Yatay ölçü",
            SketchConstraintKind.VerticalDistance => "Dikey ölçü",
            SketchConstraintKind.Length => "Uzunluk",
            SketchConstraintKind.Radius => "Yarıçap",
            SketchConstraintKind.Diameter => "Çap",
            SketchConstraintKind.Angle => "Açı",
            SketchConstraintKind.Parallel => "Paralel",
            SketchConstraintKind.Perpendicular => "Dik",
            SketchConstraintKind.Equal => "Eşit",
            SketchConstraintKind.PointOnLine => "Çizgi üstünde",
            SketchConstraintKind.PointOnCircle => "Çember üstünde",
            SketchConstraintKind.Tangent => "Teğet",
            SketchConstraintKind.Midpoint => "Orta nokta",
            SketchConstraintKind.Concentric => "Eşmerkezli",
            _ => "Simetrik"
        };

        private string Describe(string id)
        {
            var k = _sk.Constraints.FirstOrDefault(c => c.Id == id);
            if (k == null) return id;
            string what = string.Join("–", new[] { k.A, k.B, k.C }.Where(s => !string.IsNullOrEmpty(s)));
            return $"{KindName(k.Kind)} ({what})";
        }

        private void FillList()
        {
            _list.Children.Clear();
            var culprits = new HashSet<string>(_result?.Culprits ?? new List<string>(), StringComparer.Ordinal);
            foreach (var k in _sk.Constraints.ToList())
            {
                var c = k;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = new TextBlock { Text = Describe(c.Id), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis };
                label.SetResourceReference(TextBlock.ForegroundProperty, culprits.Contains(c.Id) ? "B.Warning" : "B.TextSecondary");
                row.Children.Add(label);
                if (Dimensional(c.Kind))
                {
                    var box = new TextBox { Text = F(c.Value), Width = 70, Margin = new Thickness(6, 0, 4, 0), VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 2, 4, 2) };
                    void Set()
                    {
                        if (!double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return;
                        if (c.Kind is not (SketchConstraintKind.Angle or SketchConstraintKind.HorizontalDistance or SketchConstraintKind.VerticalDistance) && v <= 0) return;
                        if (Math.Abs(v - c.Value) < 1e-12) return;
                        Change(KindName(c.Kind), () => c.Value = v);
                    }

                    box.KeyDown += (_, e) =>
                    {
                        if (e.Key == Key.Enter) Set();
                    };
                    box.LostKeyboardFocus += (_, _) => Set();
                    Grid.SetColumn(box, 1);
                    row.Children.Add(box);
                }

                var rm = new Button { Content = "×", Style = R<Style>("Btn.Icon"), Width = 22, Height = 22, ToolTip = "Kısıtı kaldır" };
                rm.Click += (_, _) => Change("Kısıt kaldırıldı", () => _sk.Constraints.Remove(c));
                Grid.SetColumn(rm, 2);
                row.Children.Add(rm);
                _list.Children.Add(row);
            }

            if (_sk.Constraints.Count == 0)
                _list.Children.Add(new TextBlock { Text = "Henüz kısıt yok.", Style = R<Style>("Text.Caption") });
        }

        // ---- drawing ---------------------------------------------------------------------------------------------

        private double GridStep()
        {
            // A grid line every 1, 2, 5, 10 … mm, at least 24 px apart.
            double[] steps = { 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500 };
            foreach (double s in steps)
                if (s * _scale >= 24) return s;
            return 1000;
        }

        private void Redraw()
        {
            _canvas.Children.Clear();
            _labelRun.Clear();
            double w = _canvas.ActualWidth, h = _canvas.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var gridBrush = B("B.Border");
            double step = GridStep();
            var (x0, y1) = Model(new Point(0, 0));
            var (x1, y0) = Model(new Point(w, h));
            for (double x = Math.Floor(x0 / step) * step; x <= x1; x += step)
            {
                var p = Screen(x, 0);
                Add(new Line { X1 = p.X, Y1 = 0, X2 = p.X, Y2 = h, Stroke = gridBrush, StrokeThickness = Math.Abs(x) < step / 2 ? 1.4 : 0.5, Opacity = Math.Abs(x) < step / 2 ? 0.9 : 0.5 });
            }

            for (double y = Math.Floor(y0 / step) * step; y <= y1; y += step)
            {
                var p = Screen(0, y);
                Add(new Line { X1 = 0, Y1 = p.Y, X2 = w, Y2 = p.Y, Stroke = gridBrush, StrokeThickness = Math.Abs(y) < step / 2 ? 1.4 : 0.5, Opacity = Math.Abs(y) < step / 2 ? 0.9 : 0.5 });
            }

            var scaleText = new TextBlock { Text = $"ızgara {F(step)} mm", FontSize = 11 };
            scaleText.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
            Canvas.SetLeft(scaleText, 8);
            Canvas.SetTop(scaleText, h - 20);
            _canvas.Children.Add(scaleText);

            bool full = _result?.State == SketchState.FullyConstrained;
            var edge = B(full ? "B.Success" : "B.Text");
            var accent = B("B.Accent");
            foreach (var e in _sk.Entities)
            {
                bool sel = _selection.Contains(e.Id);
                var stroke = sel ? accent : edge;
                double thick = sel ? 2.4 : 1.6;
                var dash = e.Construction ? new DoubleCollection { 5, 4 } : null;
                if (e.Kind == SketchEntityKind.Line && _sk.Point(e.Start) is { } a && _sk.Point(e.End) is { } b)
                {
                    var pa = Screen(a.X, a.Y);
                    var pb = Screen(b.X, b.Y);
                    Add(new Line { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Stroke = stroke, StrokeThickness = thick, StrokeDashArray = dash });
                }
                else if (_sk.Point(e.Center) is { } c)
                {
                    var pc = Screen(c.X, c.Y);
                    double r = e.Radius * _scale;
                    if (e.Kind == SketchEntityKind.Circle)
                    {
                        var el = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = stroke, StrokeThickness = thick, StrokeDashArray = dash };
                        Canvas.SetLeft(el, pc.X - r);
                        Canvas.SetTop(el, pc.Y - r);
                        _canvas.Children.Add(el);
                    }
                    else if (_sk.Point(e.Start) is { } s && _sk.Point(e.End) is { } t)
                    {
                        double a0 = Math.Atan2(s.Y - c.Y, s.X - c.X), sweep = Norm(Math.Atan2(t.Y - c.Y, t.X - c.X) - a0);
                        if (sweep < 1e-9) sweep = 2 * Math.PI;
                        var ps = Screen(s.X, s.Y);
                        var pt = Screen(c.X + e.Radius * Math.Cos(a0 + sweep), c.Y + e.Radius * Math.Sin(a0 + sweep));
                        var fig = new PathFigure { StartPoint = ps };
                        // Counter-clockwise in the sketch is clockwise on screen (Y down).
                        fig.Segments.Add(new ArcSegment(pt, new Size(r, r), 0, sweep > Math.PI, SweepDirection.Counterclockwise, true));
                        Add(new Path { Data = new PathGeometry(new[] { fig }), Stroke = stroke, StrokeThickness = thick, StrokeDashArray = dash });
                    }

                    Cross(pc, stroke);
                }
            }

            // Relations as small marks, dimensions as labels.
            foreach (var k in _sk.Constraints) Mark(k);

            foreach (var p in _sk.Points)
            {
                var sp = Screen(p.X, p.Y);
                bool sel = _selection.Contains(p.Id);
                bool fixedPoint = _sk.Constraints.Any(k => k.Kind == SketchConstraintKind.Fix && k.A == p.Id);
                var dot = new Rectangle { Width = sel ? 8 : 6, Height = sel ? 8 : 6, Stroke = sel ? accent : edge, StrokeThickness = 1.2, Fill = fixedPoint || sel ? (sel ? accent : edge) : B("B.Canvas") };
                Canvas.SetLeft(dot, sp.X - dot.Width / 2);
                Canvas.SetTop(dot, sp.Y - dot.Height / 2);
                _canvas.Children.Add(dot);
            }

            // The tool's rubber band.
            if (_clicks.Count > 0 && _sk.Point(_clicks[0]) is { } first)
            {
                var rubber = B("B.Accent");
                var pf = Screen(first.X, first.Y);
                var last = _sk.Point(_clicks[^1]) ?? first;
                var pl = Screen(last.X, last.Y);
                switch (_tool)
                {
                    case Tool.Line:
                        Add(new Line { X1 = pl.X, Y1 = pl.Y, X2 = _mouse.X, Y2 = _mouse.Y, Stroke = rubber, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 3 } });
                        Label(new Point((pl.X + _mouse.X) / 2, (pl.Y + _mouse.Y) / 2), F(Math.Round((_mouse - pl).Length / _scale, 2)) + " mm", "B.Accent");
                        break;
                    case Tool.Rectangle:
                    {
                        var r = new Rectangle { Width = Math.Abs(_mouse.X - pf.X), Height = Math.Abs(_mouse.Y - pf.Y), Stroke = rubber, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 3, 3 } };
                        Canvas.SetLeft(r, Math.Min(pf.X, _mouse.X));
                        Canvas.SetTop(r, Math.Min(pf.Y, _mouse.Y));
                        _canvas.Children.Add(r);
                        Label(new Point(Math.Max(pf.X, _mouse.X) + 6, Math.Max(pf.Y, _mouse.Y) + 4), $"{F(Math.Round(Math.Abs(_mouse.X - pf.X) / _scale, 2))} × {F(Math.Round(Math.Abs(_mouse.Y - pf.Y) / _scale, 2))} mm", "B.Accent");
                        break;
                    }

                    case Tool.Circle:
                    case Tool.Arc:
                    {
                        double r = (_clicks.Count >= 2 ? (pl - pf).Length : (_mouse - pf).Length);
                        var el = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = rubber, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
                        Canvas.SetLeft(el, pf.X - r);
                        Canvas.SetTop(el, pf.Y - r);
                        _canvas.Children.Add(el);
                        Label(new Point(pf.X + r + 6, pf.Y), "r " + F(Math.Round(r / _scale, 2)) + " mm", "B.Accent");
                        break;
                    }
                }
            }
        }

        private void Add(Shape s)
        {
            s.IsHitTestVisible = false;
            _canvas.Children.Add(s);
        }

        private void Cross(Point c, Brush b)
        {
            Add(new Line { X1 = c.X - 4, Y1 = c.Y, X2 = c.X + 4, Y2 = c.Y, Stroke = b, StrokeThickness = 1 });
            Add(new Line { X1 = c.X, Y1 = c.Y - 4, X2 = c.X, Y2 = c.Y + 4, Stroke = b, StrokeThickness = 1 });
        }

        /// <summary>Labels already put at an anchor: the next goes beside them, not on top.</summary>
        private readonly Dictionary<(int, int), double> _labelRun = new Dictionary<(int, int), double>();

        private void Label(Point at, string text, string brush)
        {
            var t = new TextBlock { Text = text, FontSize = 11.5, IsHitTestVisible = false };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            var border = new Border { Child = t, Padding = new Thickness(3, 0, 3, 1), CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
            border.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            var key = ((int)Math.Round(at.X), (int)Math.Round(at.Y));
            _labelRun.TryGetValue(key, out double shift);
            border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _labelRun[key] = shift + border.DesiredSize.Width + 3;
            Canvas.SetLeft(border, at.X + shift);
            Canvas.SetTop(border, at.Y);
            _canvas.Children.Add(border);
        }

        private Point? Middle(string? entity)
        {
            var e = _sk.Entity(entity);
            if (e == null) return null;
            if (e.Kind == SketchEntityKind.Line && _sk.Point(e.Start) is { } a && _sk.Point(e.End) is { } b) return Screen((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            if (_sk.Point(e.Center) is { } c) return Screen(c.X + e.Radius * 0.7071, c.Y + e.Radius * 0.7071);
            return null;
        }

        private void Mark(SketchConstraint k)
        {
            string? text = k.Kind switch
            {
                SketchConstraintKind.Horizontal => "—",
                SketchConstraintKind.Vertical => "|",
                SketchConstraintKind.Parallel => "∥",
                SketchConstraintKind.Perpendicular => "⊥",
                SketchConstraintKind.Equal => "=",
                SketchConstraintKind.Tangent => "T",
                SketchConstraintKind.Length => F(k.Value),
                SketchConstraintKind.Diameter => "Ø" + F(k.Value),
                SketchConstraintKind.Radius => "R" + F(k.Value),
                SketchConstraintKind.Angle => F(k.Value) + "°",
                SketchConstraintKind.Distance => F(k.Value),
                SketchConstraintKind.HorizontalDistance => "↔" + F(Math.Abs(k.Value)),
                SketchConstraintKind.VerticalDistance => "↕" + F(Math.Abs(k.Value)),
                _ => null
            };
            if (text == null) return;
            Point? at = null;
            switch (k.Kind)
            {
                case SketchConstraintKind.Distance:
                case SketchConstraintKind.HorizontalDistance:
                case SketchConstraintKind.VerticalDistance:
                    if (_sk.Point(k.A) is { } a && _sk.Point(k.B) is { } b)
                    {
                        var pa = Screen(a.X, a.Y);
                        var pb = Screen(b.X, b.Y);
                        Add(new Line { X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Stroke = B("B.TextMuted"), StrokeThickness = 0.8, StrokeDashArray = new DoubleCollection { 2, 3 } });
                        at = new Point((pa.X + pb.X) / 2, (pa.Y + pb.Y) / 2);
                    }

                    break;
                default:
                    at = Middle(k.A);
                    if (k.B != null && k.Kind is SketchConstraintKind.Parallel or SketchConstraintKind.Perpendicular or SketchConstraintKind.Equal && Middle(k.B) is { } mb)
                        Label(new Point(mb.X + 6, mb.Y - 16), text, "B.TextMuted");
                    break;
            }

            if (at is { } p) Label(new Point(p.X + 6, p.Y - 16), text, Dimensional(k.Kind) ? "B.Text" : "B.TextMuted");
        }
    }
}
