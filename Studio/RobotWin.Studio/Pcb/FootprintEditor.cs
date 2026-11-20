using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pcb
{
    public enum FootprintTool
    {
        Select,
        SmdPad,
        ThtPad,
        SilkLine,
        SilkCircle,
        FabLine,
        Text
    }

    /// <summary>
    /// Footprint drawing surface: pads (SMD/THT, rect/round/oval/round-rect, drills and
    /// numbers), silkscreen lines and circles, fab lines, texts and the courtyard, edited
    /// with the mouse on a snap grid. Footprint coordinates are millimetres, Y down.
    /// </summary>
    public sealed class FootprintEditCanvas : FrameworkElement
    {
        private static readonly Color Back = Color.FromRgb(0x0E, 0x0E, 0x10);
        private static readonly Color Gold = Color.FromRgb(0xB8, 0xB8, 0xBE);
        private static readonly Color SilkColor = Color.FromRgb(0xED, 0xED, 0xEF);
        private static readonly Color FabColor = Color.FromRgb(0x9A, 0xA4, 0xB8);
        private static readonly Color Court = Color.FromRgb(0xC0, 0x6C, 0xC0);
        private static readonly Color Accent = Color.FromRgb(0x6E, 0x9B, 0xFF);

        private double _scale = 40;
        private Vector _offset;
        private bool _panning, _dragging, _fitted;
        private Point _panAt;
        private Vector _panStart;
        private (double X, double Y) _dragFrom;
        private (double X, double Y) _cursor;
        private (double X, double Y)? _lineStart;

        public FootprintEditCanvas(Footprint fp)
        {
            Footprint = fp;
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
        }

        public Footprint Footprint { get; }
        public FootprintTool Tool { get; private set; } = FootprintTool.Select;
        public double Grid { get; set; } = 0.05;

        /// <summary>Selected item: a Pad, a FootprintText or ("silk"|"silkc"|"fab", index).</summary>
        public object? Selected { get; private set; }

        /// <summary>Template for new pads (copied).</summary>
        public Pad PadTemplate { get; set; } = new Pad { Width = 1.6, Height = 1.6, Drill = 0.8, Shape = PadShape.Circle };
        public Pad SmdTemplate { get; set; } = new Pad { Width = 1.0, Height = 1.3, Shape = PadShape.RoundRect, CornerRatio = 0.25 };

        public event EventHandler? Changed;
        public event EventHandler? SelectionChanged;

        public void SetTool(FootprintTool tool)
        {
            Tool = tool;
            _lineStart = null;
            Cursor = tool == FootprintTool.Select ? null : Cursors.Cross;
            InvalidateVisual();
        }

        public void Select(object? item)
        {
            Selected = item;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        private void Change()
        {
            Changed?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }

        private Point S(double x, double y) => new Point(x * _scale + _offset.X, y * _scale + _offset.Y);
        private (double X, double Y) ToFp(Point p) => ((p.X - _offset.X) / _scale, (p.Y - _offset.Y) / _scale);
        private double Snap(double v) => Math.Round(Math.Round(v / Grid) * Grid, 6);

        public void Fit()
        {
            if (ActualWidth < 10) return;
            var fp = Footprint;
            double minX = Math.Min(-2, fp.CourtyardMinX), minY = Math.Min(-2, fp.CourtyardMinY), maxX = Math.Max(2, fp.CourtyardMaxX), maxY = Math.Max(2, fp.CourtyardMaxY);
            foreach (var p in fp.Pads)
            {
                minX = Math.Min(minX, p.X - p.Width);
                maxX = Math.Max(maxX, p.X + p.Width);
                minY = Math.Min(minY, p.Y - p.Height);
                maxY = Math.Max(maxY, p.Y + p.Height);
            }

            _scale = Math.Max(4, Math.Min(200, Math.Min((ActualWidth - 60) / (maxX - minX), (ActualHeight - 60) / (maxY - minY))));
            _offset = new Vector(ActualWidth / 2 - (minX + maxX) / 2 * _scale, ActualHeight / 2 - (minY + maxY) / 2 * _scale);
            InvalidateVisual();
        }

        private static Pen P(Color c, double w, bool dash = false)
        {
            var pen = new Pen(SymbolRenderer.Brush(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dash) pen.DashStyle = DashStyles.Dash;
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (!_fitted && ActualWidth > 10)
            {
                _fitted = true;
                Fit();
            }

            dc.DrawRectangle(SymbolRenderer.Brush(Back), null, new Rect(0, 0, ActualWidth, ActualHeight));
            double g = Grid;
            while (g * _scale < 12) g *= 2;
            var gridPen = P(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1);
            var (x0, y0) = ToFp(new Point(0, 0));
            var (x1, y1) = ToFp(new Point(ActualWidth, ActualHeight));
            for (double x = Math.Ceiling(x0 / g) * g; x <= x1; x += g) dc.DrawLine(gridPen, S(x, y0), S(x, y1));
            for (double y = Math.Ceiling(y0 / g) * g; y <= y1; y += g) dc.DrawLine(gridPen, S(x0, y), S(x1, y));
            var axis = P(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 1);
            dc.DrawLine(axis, S(-1, 0), S(1, 0));
            dc.DrawLine(axis, S(0, -1), S(0, 1));

            var fp = Footprint;
            dc.DrawRectangle(null, P(Court, 1, true), new Rect(S(fp.CourtyardMinX, fp.CourtyardMinY), S(fp.CourtyardMaxX, fp.CourtyardMaxY)));

            var dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            foreach (var pad in fp.Pads)
            {
                var c = S(pad.X, pad.Y);
                double w = pad.Width * _scale, h = pad.Height * _scale;
                var color = pad.IsThroughHole ? Gold : pad.Layer == CopperLayer.Top ? BoardCanvas.LayerColor(CopperLayer.Top) : BoardCanvas.LayerColor(CopperLayer.Bottom);
                var brush = SymbolRenderer.Brush(pad.NonPlated ? Back : color);
                var outline = pad.NonPlated ? P(Gold, 1, true) : null;
                switch (pad.Shape)
                {
                    case PadShape.Circle:
                        dc.DrawEllipse(brush, outline, c, Math.Max(w, h) / 2, Math.Max(w, h) / 2);
                        break;
                    case PadShape.Rect:
                        dc.DrawRectangle(brush, outline, new Rect(c.X - w / 2, c.Y - h / 2, w, h));
                        break;
                    case PadShape.Oval:
                        dc.DrawRoundedRectangle(brush, outline, new Rect(c.X - w / 2, c.Y - h / 2, w, h), Math.Min(w, h) / 2, Math.Min(w, h) / 2);
                        break;
                    default:
                        double r = pad.CornerRatio * Math.Min(w, h);
                        dc.DrawRoundedRectangle(brush, outline, new Rect(c.X - w / 2, c.Y - h / 2, w, h), r, r);
                        break;
                }

                if (pad.IsThroughHole) dc.DrawEllipse(SymbolRenderer.Brush(Back), null, c, pad.Drill / 2 * _scale, pad.Drill / 2 * _scale);
                double size = Math.Min(14, Math.Min(w, h) * 0.45);
                if (size >= 6 && !pad.NonPlated)
                {
                    var ft = new FormattedText(pad.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.UiSemi, size, SymbolRenderer.Brush(Color.FromRgb(0x10, 0x10, 0x12)), dip);
                    dc.DrawText(ft, new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2));
                }

                if (ReferenceEquals(pad, Selected)) dc.DrawRectangle(null, P(Accent, 2), new Rect(c.X - w / 2 - 3, c.Y - h / 2 - 3, w + 6, h + 6));
            }

            var silk = P(SilkColor, Math.Max(1, 0.15 * _scale));
            for (int i = 0; i < fp.Silkscreen.Count; i++)
            {
                var s = fp.Silkscreen[i];
                dc.DrawLine(IsSel("silk", i) ? P(Accent, Math.Max(2, 0.15 * _scale)) : silk, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }

            for (int i = 0; i < fp.SilkCircles.Count; i++)
            {
                var (x, y, r) = fp.SilkCircles[i];
                dc.DrawEllipse(null, IsSel("silkc", i) ? P(Accent, Math.Max(2, 0.15 * _scale)) : silk, S(x, y), r * _scale, r * _scale);
            }

            var fab = P(FabColor, Math.Max(1, 0.1 * _scale));
            for (int i = 0; i < fp.FabLines.Count; i++)
            {
                var s = fp.FabLines[i];
                dc.DrawLine(IsSel("fab", i) ? P(Accent, 2) : fab, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }

            foreach (var t in fp.Texts)
            {
                var pen = ReferenceEquals(t, Selected) ? P(Accent, Math.Max(1.5, 0.15 * _scale)) : P(t.Layer == GraphicLayer.FabTop ? FabColor : SilkColor, Math.Max(1, 0.15 * _scale));
                StrokeFont.DrawCentered((a, b, c, d, _) => dc.DrawLine(pen, S(a, b), S(c, d)), t.Text, t.X, t.Y, t.Size, 0.15, 0, false);
            }

            // Pending line / circle.
            if (_lineStart is { } start)
            {
                var cur = (Snap(_cursor.X), Snap(_cursor.Y));
                var pen = P(Accent, 1.5);
                if (Tool == FootprintTool.SilkCircle)
                {
                    double r = Math.Sqrt((cur.Item1 - start.X) * (cur.Item1 - start.X) + (cur.Item2 - start.Y) * (cur.Item2 - start.Y));
                    dc.DrawEllipse(null, pen, S(start.X, start.Y), r * _scale, r * _scale);
                }
                else
                {
                    dc.DrawLine(pen, S(start.X, start.Y), S(cur.Item1, cur.Item2));
                }
            }

            var label = new FormattedText(string.Format(CultureInfo.InvariantCulture, "x {0:0.000}  y {1:0.000} mm · ızgara {2} mm", _cursor.X, _cursor.Y, Grid),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Mono, 11, SymbolRenderer.Brush(Color.FromRgb(0x6C, 0x6C, 0x74)), dip);
            dc.DrawText(label, new Point(10, ActualHeight - 20));
        }

        private bool IsSel(string kind, int index) => Selected is ValueTuple<string, int> s && s.Item1 == kind && s.Item2 == index;

        private object? Hit(double x, double y)
        {
            var fp = Footprint;
            double tol = 5 / _scale;
            foreach (var t in fp.Texts)
            {
                double w = StrokeFont.Measure(t.Text, t.Size);
                if (Math.Abs(x - t.X) <= w / 2 + tol && Math.Abs(y - t.Y) <= t.Size / 2 + tol) return t;
            }

            for (int i = fp.Pads.Count - 1; i >= 0; i--)
            {
                var p = fp.Pads[i];
                if (Math.Abs(x - p.X) <= p.Width / 2 && Math.Abs(y - p.Y) <= p.Height / 2) return p;
            }

            for (int i = 0; i < fp.Silkscreen.Count; i++)
            {
                var s = fp.Silkscreen[i];
                if (RobotTwin.CoreSim.Pcb.Geometry.PointSegmentDistance(x, y, s.X1, s.Y1, s.X2, s.Y2) <= tol) return ("silk", i);
            }

            for (int i = 0; i < fp.SilkCircles.Count; i++)
            {
                var (cx, cy, r) = fp.SilkCircles[i];
                if (Math.Abs(Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r) <= tol) return ("silkc", i);
            }

            for (int i = 0; i < fp.FabLines.Count; i++)
            {
                var s = fp.FabLines[i];
                if (RobotTwin.CoreSim.Pcb.Geometry.PointSegmentDistance(x, y, s.X1, s.Y1, s.X2, s.Y2) <= tol) return ("fab", i);
            }

            return null;
        }

        /// <summary>Next free numeric pad name.</summary>
        public string NextPadName()
        {
            int max = 0;
            foreach (var p in Footprint.Pads)
            {
                if (int.TryParse(p.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) max = Math.Max(max, n);
            }

            return (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var p = e.GetPosition(this);
            var b = ToFp(p);
            _scale = Math.Max(2, Math.Min(400, _scale * (e.Delta > 0 ? 1.2 : 1 / 1.2)));
            _offset = new Vector(p.X - b.X * _scale, p.Y - b.Y * _scale);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            CaptureMouse();
            if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right)
            {
                if (e.ChangedButton == MouseButton.Right && _lineStart != null)
                {
                    _lineStart = null;
                    InvalidateVisual();
                    return;
                }

                _panning = true;
                _panAt = p;
                _panStart = _offset;
                return;
            }

            var (x, y) = ToFp(p);
            double sx = Snap(x), sy = Snap(y);
            var fp = Footprint;
            switch (Tool)
            {
                case FootprintTool.SmdPad:
                case FootprintTool.ThtPad:
                {
                    var template = Tool == FootprintTool.SmdPad ? SmdTemplate : PadTemplate;
                    var pad = template.Clone();
                    pad.Name = NextPadName();
                    pad.X = sx;
                    pad.Y = sy;
                    if (Tool == FootprintTool.SmdPad) pad.Drill = 0;
                    fp.Pads.Add(pad);
                    Select(pad);
                    Change();
                    return;
                }

                case FootprintTool.SilkLine:
                case FootprintTool.FabLine:
                case FootprintTool.SilkCircle:
                    if (_lineStart is { } start)
                    {
                        if (Math.Abs(start.X - sx) + Math.Abs(start.Y - sy) < 1e-9) return;
                        if (Tool == FootprintTool.SilkCircle)
                        {
                            fp.SilkCircles.Add((start.X, start.Y, Math.Sqrt((sx - start.X) * (sx - start.X) + (sy - start.Y) * (sy - start.Y))));
                            _lineStart = null;
                        }
                        else
                        {
                            var seg = new Segment2(start.X, start.Y, sx, sy);
                            if (Tool == FootprintTool.SilkLine) fp.Silkscreen.Add(seg);
                            else fp.FabLines.Add(seg);
                            _lineStart = (sx, sy);   // continue as a polyline; right click / Esc ends
                        }

                        Change();
                    }
                    else
                    {
                        _lineStart = (sx, sy);
                    }

                    InvalidateVisual();
                    return;
                case FootprintTool.Text:
                {
                    var t = new FootprintText { Text = "TEXT", X = sx, Y = sy, Size = 1.0 };
                    fp.Texts.Add(t);
                    Select(t);
                    SetTool(FootprintTool.Select);
                    Change();
                    return;
                }
            }

            Select(Hit(x, y));
            _dragging = Selected != null;
            _dragFrom = (sx, sy);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_panning)
            {
                _offset = _panStart + (p - _panAt);
                InvalidateVisual();
                return;
            }

            _cursor = ToFp(p);
            if (_dragging && e.LeftButton == MouseButtonState.Pressed && Selected != null)
            {
                double sx = Snap(_cursor.X), sy = Snap(_cursor.Y);
                double dx = sx - _dragFrom.X, dy = sy - _dragFrom.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > 1e-9)
                {
                    MoveSelected(dx, dy);
                    _dragFrom = (sx, sy);
                    Change();
                }
            }

            InvalidateVisual();
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            ReleaseMouseCapture();
            _panning = false;
            if (_dragging) SelectionChanged?.Invoke(this, EventArgs.Empty);
            _dragging = false;
        }

        public void MoveSelected(double dx, double dy)
        {
            var fp = Footprint;
            switch (Selected)
            {
                case Pad pad:
                    pad.X = Math.Round(pad.X + dx, 6);
                    pad.Y = Math.Round(pad.Y + dy, 6);
                    break;
                case FootprintText t:
                    t.X = Math.Round(t.X + dx, 6);
                    t.Y = Math.Round(t.Y + dy, 6);
                    break;
                case ValueTuple<string, int> s when s.Item1 == "silk":
                {
                    var l = fp.Silkscreen[s.Item2];
                    fp.Silkscreen[s.Item2] = new Segment2(l.X1 + dx, l.Y1 + dy, l.X2 + dx, l.Y2 + dy);
                    break;
                }

                case ValueTuple<string, int> s when s.Item1 == "fab":
                {
                    var l = fp.FabLines[s.Item2];
                    fp.FabLines[s.Item2] = new Segment2(l.X1 + dx, l.Y1 + dy, l.X2 + dx, l.Y2 + dy);
                    break;
                }

                case ValueTuple<string, int> s when s.Item1 == "silkc":
                {
                    var c = fp.SilkCircles[s.Item2];
                    fp.SilkCircles[s.Item2] = (c.X + dx, c.Y + dy, c.R);
                    break;
                }
            }
        }

        public void DeleteSelected()
        {
            var fp = Footprint;
            switch (Selected)
            {
                case Pad pad:
                    fp.Pads.Remove(pad);
                    break;
                case FootprintText t:
                    fp.Texts.Remove(t);
                    break;
                case ValueTuple<string, int> s when s.Item1 == "silk":
                    fp.Silkscreen.RemoveAt(s.Item2);
                    break;
                case ValueTuple<string, int> s when s.Item1 == "fab":
                    fp.FabLines.RemoveAt(s.Item2);
                    break;
                case ValueTuple<string, int> s when s.Item1 == "silkc":
                    fp.SilkCircles.RemoveAt(s.Item2);
                    break;
                default:
                    return;
            }

            Select(null);
            Change();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.Escape:
                    if (_lineStart != null) _lineStart = null;
                    else if (Tool != FootprintTool.Select) SetTool(FootprintTool.Select);
                    else Select(null);
                    InvalidateVisual();
                    return;
                case Key.Delete:
                case Key.Back:
                    DeleteSelected();
                    return;
                case Key.Left:
                    MoveSelected(-Grid, 0);
                    Change();
                    return;
                case Key.Right:
                    MoveSelected(Grid, 0);
                    Change();
                    return;
                case Key.Up:
                    MoveSelected(0, -Grid);
                    Change();
                    return;
                case Key.Down:
                    MoveSelected(0, Grid);
                    Change();
                    return;
                case Key.F:
                    Fit();
                    return;
            }

            e.Handled = false;
        }
    }

    /// <summary>
    /// Footprint editor window: create a footprint from scratch or from a copy of an
    /// existing one, edit pads (type, shape, size, drill, number), silkscreen, fab and
    /// texts, generate pad rows and dual rows, fit the courtyard, and save it to the user
    /// footprint library (optionally assigning it to the part it was opened from).
    /// </summary>
    public sealed class FootprintEditorWindow : Window
    {
        private readonly FootprintEditCanvas _canvas;
        private readonly Footprint _fp;
        private readonly StackPanel _props = new StackPanel();
        private readonly StackPanel _selection = new StackPanel();
        private readonly TextBlock _summary = new TextBlock();
        private readonly TextBox _idBox;
        private readonly Dictionary<FootprintTool, ToggleButton> _toolButtons = new Dictionary<FootprintTool, ToggleButton>();
        private readonly string? _refDes;

        public FootprintEditorWindow(Footprint? source, string? refDes = null)
        {
            _refDes = refDes;
            Title = "Footprint düzenleyici";
            Width = 1180;
            Height = 760;
            MinWidth = 820;
            MinHeight = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = B("B.Base");
            Foreground = B("B.Text");
            ShowInTaskbar = false;

            string id;
            if (source == null) id = UniqueId("USER-FP");
            else if (FootprintLibrary.IsBuiltIn(source.Id)) id = UniqueId("USER-" + source.Id);
            else id = source.Id;
            _fp = source != null ? source.Clone(id) : NewFootprint(id);
            _canvas = new FootprintEditCanvas(_fp);
            _canvas.Changed += (_, _) => UpdateSummary();
            _canvas.SelectionChanged += (_, _) => BuildSelection();

            var root = new DockPanel { Background = B("B.Base") };
            // Toolbar
            var bar = new Border { BorderBrush = B("B.Border"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 6, 10, 6) };
            var tools = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            void Tool(FootprintTool t, string text, string tip)
            {
                var b = new ToggleButton { Content = text, Style = S("Chip"), ToolTip = tip, Margin = new Thickness(0, 0, 4, 0), IsChecked = t == FootprintTool.Select };
                b.Click += (_, _) =>
                {
                    _canvas.SetTool(t);
                    foreach (var kv in _toolButtons) kv.Value.IsChecked = kv.Key == t;
                    _canvas.Focus();
                };
                _toolButtons[t] = b;
                tools.Children.Add(b);
            }

            Tool(FootprintTool.Select, "Seç / taşı", "Seç, sürükle; ok tuşları ızgara kadar kaydırır, Del siler");
            Tool(FootprintTool.SmdPad, "SMD ped", "Tıklayarak SMD ped ekle (numara otomatik artar)");
            Tool(FootprintTool.ThtPad, "THT ped", "Tıklayarak delikli ped ekle");
            Tool(FootprintTool.SilkLine, "Serigrafi çizgi", "Tıkla-tıkla çizgi; sağ tık / Esc bitirir");
            Tool(FootprintTool.SilkCircle, "Serigrafi çember", "Merkeze, sonra yarıçapa tıklayın");
            Tool(FootprintTool.FabLine, "Fab çizgi", "Montaj (F.Fab) çizgisi");
            Tool(FootprintTool.Text, "Yazı", "Serigrafi yazısı ekle");
            tools.Children.Add(new Border { Width = 1, Height = 20, Background = B("B.Border"), Margin = new Thickness(8, 0, 8, 0) });
            tools.Children.Add(new TextBlock { Text = "Izgara", Style = S("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var grid = Combo(new[] { 0.01, 0.05, 0.1, 0.25, 0.5, 0.635, 1.27 }, 0.05, v => { _canvas.Grid = v; _canvas.InvalidateVisual(); }, v => v.ToString("0.###", CultureInfo.InvariantCulture) + " mm");
            grid.Width = 90;
            tools.Children.Add(grid);
            var fitCourt = new Button { Content = "Courtyard'ı sığdır", Style = S("Btn"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Pedlerin ve çizimlerin çevresine 0.25 mm pay ile (IPC-7351)" };
            fitCourt.Click += (_, _) =>
            {
                _fp.FitCourtyard();
                BuildProps();
                _canvas.InvalidateVisual();
            };
            tools.Children.Add(fitCourt);
            var fit = new Button { Content = "Sığdır (F)", Style = S("Btn"), Margin = new Thickness(6, 0, 0, 0) };
            fit.Click += (_, _) => _canvas.Fit();
            tools.Children.Add(fit);
            bar.Child = tools;
            DockPanel.SetDock(bar, Dock.Top);
            root.Children.Add(bar);

            // Buttons
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 10, 12, 12) };
            var cancel = new Button { Content = "Vazgeç", Style = S("Btn"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
            var save = new Button { Content = "Kütüphaneye kaydet", Style = S(refDes == null ? "Btn.Primary" : "Btn"), Margin = new Thickness(0, 0, 8, 0) };
            save.Click += (_, _) => Save(false);
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            if (refDes != null)
            {
                var assign = new Button { Content = $"Kaydet ve {refDes} parçasına ata", Style = S("Btn.Primary"), IsDefault = true };
                assign.Click += (_, _) => Save(true);
                buttons.Children.Add(assign);
            }

            var bottom = new DockPanel();
            _summary.Style = S("Text.Caption");
            _summary.VerticalAlignment = VerticalAlignment.Center;
            _summary.Margin = new Thickness(14, 0, 0, 0);
            DockPanel.SetDock(buttons, Dock.Right);
            bottom.Children.Add(buttons);
            bottom.Children.Add(_summary);
            DockPanel.SetDock(bottom, Dock.Bottom);
            root.Children.Add(bottom);

            // Side panel
            _idBox = new TextBox { Text = _fp.Id };
            var side = new Border { Width = 320, BorderBrush = B("B.Border"), BorderThickness = new Thickness(1, 0, 0, 0), Background = B("B.Base") };
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Margin = new Thickness(16, 6, 16, 16), Children = { _props, _selection } } };
            side.Child = scroll;
            DockPanel.SetDock(side, Dock.Right);
            root.Children.Add(side);
            root.Children.Add(_canvas);
            Content = root;

            BuildProps();
            BuildSelection();
            UpdateSummary();
            Loaded += (_, _) => _canvas.Focus();
        }

        /// <summary>The saved footprint (null until saved).</summary>
        public Footprint? Saved { get; private set; }

        public bool AssignToPart { get; private set; }

        private static string UniqueId(string stem)
        {
            string id = stem;
            for (int i = 2; FootprintLibrary.UserIds.Contains(id) || FootprintLibrary.IsBuiltIn(id); i++) id = stem + "-" + i.ToString(CultureInfo.InvariantCulture);
            return id;
        }

        private static Footprint NewFootprint(string id)
        {
            var fp = new Footprint { Id = id, Description = "Yeni footprint", Height = 2 };
            fp.Pads.Add(new Pad { Name = "1", X = -1.27, Y = 0, Width = 1.7, Height = 1.7, Drill = 1.0, Shape = PadShape.Rect });
            fp.Pads.Add(new Pad { Name = "2", X = 1.27, Y = 0, Width = 1.7, Height = 1.7, Drill = 1.0, Shape = PadShape.Circle });
            fp.Texts.Add(new FootprintText { Text = "%R", X = 0, Y = 2.2, Layer = GraphicLayer.FabTop });
            fp.FitCourtyard();
            fp.Silkscreen.Add(new Segment2(fp.CourtyardMinX + 0.1, fp.CourtyardMinY + 0.1, fp.CourtyardMaxX - 0.1, fp.CourtyardMinY + 0.1));
            return fp;
        }

        private void UpdateSummary()
        {
            int tht = _fp.Pads.Count(p => p.IsThroughHole), smd = _fp.Pads.Count - tht;
            _summary.Text = $"{_fp.Pads.Count} ped ({smd} SMD, {tht} THT) · {_fp.Silkscreen.Count + _fp.SilkCircles.Count} serigrafi · courtyard {_fp.CourtyardMaxX - _fp.CourtyardMinX:0.##}×{_fp.CourtyardMaxY - _fp.CourtyardMinY:0.##} mm" +
                            (_fp.Pads.GroupBy(p => p.Name).Any(g => g.Count() > 1 && g.Key.Length > 0) ? " · dikkat: aynı numaralı pedler var" : string.Empty);
        }

        private void BuildProps()
        {
            _props.Children.Clear();
            _props.Children.Add(Overline("Footprint", 6));
            _props.Children.Add(Row("Kimlik", _idBox, 170));
            _props.Children.Add(Row("Açıklama", Text(_fp.Description, v => _fp.Description = v), 170));
            _props.Children.Add(Row("Gövde yüksekliği (mm)", Number(_fp.Height, v => _fp.Height = v, 0.05, 100)));
            _props.Children.Add(Caption($"Courtyard: {Fmt(_fp.CourtyardMinX)}, {Fmt(_fp.CourtyardMinY)} → {Fmt(_fp.CourtyardMaxX)}, {Fmt(_fp.CourtyardMaxY)}", 2));
            var court = new Grid();
            for (int i = 0; i < 4; i++) court.ColumnDefinitions.Add(new ColumnDefinition());
            var boxes = new[]
            {
                Number(_fp.CourtyardMinX, v => { _fp.CourtyardMinX = v; _canvas.InvalidateVisual(); }, -1e6, 1e6),
                Number(_fp.CourtyardMinY, v => { _fp.CourtyardMinY = v; _canvas.InvalidateVisual(); }, -1e6, 1e6),
                Number(_fp.CourtyardMaxX, v => { _fp.CourtyardMaxX = v; _canvas.InvalidateVisual(); }, -1e6, 1e6),
                Number(_fp.CourtyardMaxY, v => { _fp.CourtyardMaxY = v; _canvas.InvalidateVisual(); }, -1e6, 1e6)
            };
            for (int i = 0; i < 4; i++)
            {
                boxes[i].Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0);
                Grid.SetColumn(boxes[i], i);
                court.Children.Add(boxes[i]);
            }

            _props.Children.Add(court);

            _props.Children.Add(Overline("Ped dizisi oluştur"));
            int count = 8, start = 1;
            double pitch = 2.54, row = 7.62, w = 1.6, h = 1.6, drill = 0.8;
            bool smd = false, dual = true;
            var shape = PadShape.Oval;
            _props.Children.Add(Row("Ped sayısı", Number(count, v => count = (int)Math.Round(v), 0)));
            _props.Children.Add(Row("Aralık / adım (mm)", Number(pitch, v => pitch = v, 0.05, 100)));
            _props.Children.Add(Row("Sıralar arası (mm)", Number(row, v => row = v, 0.05, 200)));
            _props.Children.Add(Row("Ped G × Y (mm)", PairEditor(w, h, v => w = v, v => h = v), 130));
            _props.Children.Add(Row("Delik (mm, SMD: yok)", Number(drill, v => drill = v, 0.05, 20)));
            _props.Children.Add(Row("Şekil", Combo(new[] { PadShape.Oval, PadShape.Circle, PadShape.Rect, PadShape.RoundRect }, shape, v => shape = v, ShapeName), 130));
            _props.Children.Add(Row("İlk numara", Number(start, v => start = (int)Math.Round(v), 0)));
            _props.Children.Add(Switch("SMD (delik yok)", smd, v => smd = v));
            _props.Children.Add(Switch("Çift sıra (DIP/SOIC numaralama)", dual, v => dual = v));
            _props.Children.Add(Buttons(Button("Pedleri değiştir", (_, _) =>
            {
                _fp.Pads.Clear();
                Generate(count, start, pitch, row, w, h, smd ? 0 : drill, shape, dual);
            }, true, "Mevcut pedleri siler ve diziyi oluşturur"), Button("Ekle", (_, _) => Generate(count, start, pitch, row, w, h, smd ? 0 : drill, shape, dual))));
        }

        private UIElement PairEditor(double a, double b, Action<double> setA, Action<double> setB)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var x = Number(a, setA, 0.01, 100);
            var y = Number(b, setB, 0.01, 100);
            Grid.SetColumn(y, 2);
            g.Children.Add(x);
            g.Children.Add(y);
            return g;
        }

        private static string ShapeName(PadShape s) => s switch
        {
            PadShape.Circle => "Yuvarlak",
            PadShape.Rect => "Dikdörtgen",
            PadShape.Oval => "Oval",
            _ => "Yuvarlatılmış dikdörtgen"
        };

        /// <summary>Single row (1..n along X) or dual row (1..n/2 down the left, n/2+1..n up the right, pin 1 square).</summary>
        private void Generate(int count, int start, double pitch, double row, double w, double h, double drill, PadShape shape, bool dual)
        {
            if (count <= 0) return;
            void Add(int number, double x, double y, bool first)
            {
                _fp.Pads.Add(new Pad
                {
                    Name = number.ToString(CultureInfo.InvariantCulture), X = Math.Round(x, 6), Y = Math.Round(y, 6), Width = w, Height = h, Drill = drill,
                    Shape = first && drill > 0 ? PadShape.Rect : shape, CornerRatio = 0.25, Layer = CopperLayer.Top
                });
            }

            if (dual && count >= 2)
            {
                int half = count / 2;
                double y0 = -(half - 1) * pitch / 2;
                for (int i = 0; i < half; i++)
                {
                    Add(start + i, -row / 2, y0 + i * pitch, i == 0);
                    Add(start + count - 1 - i, row / 2, y0 + i * pitch, false);
                }
            }
            else
            {
                double x0 = -(count - 1) * pitch / 2;
                for (int i = 0; i < count; i++) Add(start + i, x0 + i * pitch, 0, i == 0);
            }

            _fp.FitCourtyard();
            BuildProps();
            UpdateSummary();
            _canvas.Fit();
        }

        private void BuildSelection()
        {
            _selection.Children.Clear();
            switch (_canvas.Selected)
            {
                case Pad pad:
                {
                    _selection.Children.Add(Overline("Seçili ped"));
                    _selection.Children.Add(Row("Numara", Text(pad.Name, v => { pad.Name = v.Trim(); _canvas.InvalidateVisual(); UpdateSummary(); }), 130));
                    string kind = pad.IsThroughHole ? (pad.NonPlated ? "NPTH" : "THT") : "SMD";
                    _selection.Children.Add(Row("Tür", Combo(new[] { "SMD", "THT", "NPTH" }, kind, v =>
                    {
                        if (v == "SMD")
                        {
                            pad.Drill = 0;
                            pad.NonPlated = false;
                        }
                        else
                        {
                            if (pad.Drill <= 0) pad.Drill = Math.Max(0.3, Math.Min(pad.Width, pad.Height) * 0.5);
                            pad.NonPlated = v == "NPTH";
                        }

                        BuildSelection();
                        _canvas.InvalidateVisual();
                        UpdateSummary();
                    }), 130));
                    _selection.Children.Add(Row("Şekil", Combo(new[] { PadShape.Rect, PadShape.Circle, PadShape.Oval, PadShape.RoundRect }, pad.Shape, v => { pad.Shape = v; _canvas.InvalidateVisual(); }, ShapeName), 130));
                    _selection.Children.Add(Row("Genişlik × yükseklik", PairEditor(pad.Width, pad.Height, v => { pad.Width = v; _canvas.InvalidateVisual(); }, v => { pad.Height = v; _canvas.InvalidateVisual(); }), 130));
                    if (pad.Shape == PadShape.RoundRect) _selection.Children.Add(Row("Köşe oranı", Number(pad.CornerRatio, v => { pad.CornerRatio = v; _canvas.InvalidateVisual(); }, 0, 0.5, true)));
                    if (pad.IsThroughHole) _selection.Children.Add(Row("Delik (mm)", Number(pad.Drill, v => { pad.Drill = v; _canvas.InvalidateVisual(); }, 0.05, 20)));
                    else _selection.Children.Add(Row("Katman", Combo(new[] { CopperLayer.Top, CopperLayer.Bottom }, pad.Layer, v => { pad.Layer = v; _canvas.InvalidateVisual(); }, PcbLayers.DisplayName), 130));
                    _selection.Children.Add(Row("X / Y (mm)", PairEditor(pad.X, pad.Y, v => { pad.X = v; _canvas.InvalidateVisual(); }, v => { pad.Y = v; _canvas.InvalidateVisual(); }), 130));
                    if (pad.IsThroughHole && !pad.NonPlated && (Math.Min(pad.Width, pad.Height) - pad.Drill) / 2 < 0.13)
                    {
                        _selection.Children.Add(new TextBlock { Text = "Halka 0.13 mm'den dar: DRC hatası verir.", Foreground = B("B.Warning"), FontSize = 12, TextWrapping = TextWrapping.Wrap });
                    }

                    _selection.Children.Add(Buttons(
                        Button("Şablon yap", (_, _) =>
                        {
                            if (pad.IsThroughHole) _canvas.PadTemplate = pad.Clone();
                            else _canvas.SmdTemplate = pad.Clone();
                        }, tip: "Yeni pedler bu ölçülerle eklenir"),
                        Button("Sil", (_, _) => _canvas.DeleteSelected())));
                    break;
                }

                case FootprintText t:
                    _selection.Children.Add(Overline("Seçili yazı"));
                    _selection.Children.Add(Row("Metin (%R = referans)", Text(t.Text, v => { t.Text = v; _canvas.InvalidateVisual(); }), 130));
                    _selection.Children.Add(Row("Yükseklik (mm)", Number(t.Size, v => { t.Size = v; _canvas.InvalidateVisual(); }, 0.2, 20)));
                    _selection.Children.Add(Row("Katman", Combo(new[] { GraphicLayer.SilkTop, GraphicLayer.FabTop }, t.Layer, v => { t.Layer = v; _canvas.InvalidateVisual(); }, v => v == GraphicLayer.SilkTop ? "Serigrafi" : "Fab"), 130));
                    _selection.Children.Add(Buttons(Button("Sil", (_, _) => _canvas.DeleteSelected())));
                    break;
                case ValueTuple<string, int> s:
                    _selection.Children.Add(Overline(s.Item1 == "fab" ? "Seçili fab çizgisi" : s.Item1 == "silkc" ? "Seçili serigrafi çemberi" : "Seçili serigrafi çizgisi"));
                    _selection.Children.Add(Caption("Sürükleyerek taşıyın; Del siler.", 0));
                    _selection.Children.Add(Buttons(Button("Sil", (_, _) => _canvas.DeleteSelected())));
                    break;
                default:
                    _selection.Children.Add(Overline("Seçim"));
                    _selection.Children.Add(Caption("Bir pede veya çizgiye tıklayın. Sağ/orta tuş: kaydır, tekerlek: yakınlaştır.", 0));
                    break;
            }
        }

        private void Save(bool assign)
        {
            string id = _idBox.Text.Trim();
            if (id.Length == 0 || id.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0)
            {
                RobotWin.Studio.Shell.Dialog.Show(this, "Geçersiz kimlik", "Footprint kimliği boş olamaz ve / \\ : * ? \" < > | içeremez.", "Tamam");
                return;
            }

            if (FootprintLibrary.IsBuiltIn(id))
            {
                RobotWin.Studio.Shell.Dialog.Show(this, "Yerleşik footprint", $"'{id}' yerleşik bir footprint; değiştirilmiş kopyayı başka bir kimlikle kaydedin (ör. USER-{id}).", "Tamam");
                return;
            }

            if (_fp.Pads.Count == 0)
            {
                RobotWin.Studio.Shell.Dialog.Show(this, "Ped yok", "En az bir ped ekleyin.", "Tamam");
                return;
            }

            var dup = _fp.Pads.Where(p => p.Name.Length > 0).GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0 && RobotWin.Studio.Shell.Dialog.Show(this, "Aynı numaralı pedler", $"{string.Join(", ", dup)} numaraları birden fazla pedde. Aynı numaralı pedler aynı hatta bağlanır. Yine de kaydedilsin mi?", "Kaydet", "Vazgeç") != 0)
            {
                return;
            }

            var saved = _fp.Clone(id);
            FootprintLibrary.Register(saved);
            Saved = saved;
            AssignToPart = assign;
            DialogResult = true;
        }

        /// <summary>For snapshots: select the first pad so the property panel is populated.</summary>
        public void SelectFirstPad() => _canvas.Select(_fp.Pads.FirstOrDefault());
    }
}
