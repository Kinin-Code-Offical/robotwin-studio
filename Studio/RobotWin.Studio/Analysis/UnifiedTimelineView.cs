using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Analysis
{
    /// <summary>
    /// [unified-timeline] A render's whole record on one time axis (CoreSim Analysis/TimelineLanes.cs): events, serial
    /// lines, each board's interrupts and pins, circuit signals, joints, bodies and contacts, in groups that fold. The
    /// playback's moment is the cursor; a click moves the playback there. The wheel scrolls the lanes, Ctrl+wheel zooms
    /// around the pointer, Shift+wheel or a drag moves along time, a double click shows the whole run. A group's header
    /// folds it; its "+N" opens the lanes that stayed behind (quantities that never moved, the rest of a body's channels).
    /// </summary>
    public sealed class UnifiedTimelineView : FrameworkElement
    {
        private const double LabelWidth = 190, AxisHeight = 22, HeaderHeight = 20;
        private List<TimelineLane> _lanes = new List<TimelineLane>();
        private ChannelStore _store = new ChannelStore();
        private double _duration = 1, _viewStart, _viewSpan = 1, _cursor, _scroll;
        private readonly HashSet<string> _folded = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _full = new HashSet<string>(StringComparer.Ordinal);
        private Point? _hover;
        private Point _down;
        private double _downStart;
        private bool _dragging;

        // what the last render laid out, for hit tests: rows by their top
        private readonly List<(double Top, double Height, string Group, TimelineLane? Lane, Rect More)> _rows = new List<(double, double, string, TimelineLane?, Rect)>();

        /// <summary>The playback asks to go to a time (s from the render's start).</summary>
        public event Action<double>? TimeClicked;

        public UnifiedTimelineView()
        {
            ClipToBounds = true;
            Focusable = true;
            ToolTip = Lang.T("Tıklayın: oynatma o ana gider · tekerlek: şeritler · Ctrl+tekerlek: yakınlaştır · Shift+tekerlek ya da sürükleme: zamanda kay · çift tık: tüm koşu");
        }

        public void Load(AnalysisResult result)
        {
            _lanes = TimelineLanes.Build(result);
            _store = result.Channels;
            _duration = Math.Max(1e-3, Math.Max(result.SimulatedSeconds, _store.Count > 0 ? _store.Time(_store.Count - 1) : 0));
            _viewStart = 0;
            _viewSpan = _duration;
            _scroll = 0;
            _folded.Clear();
            _full.Clear();
            InvalidateVisual();
        }

        public int LaneCount => _lanes.Count;

        /// <summary>The playback's moment; the view follows it when it leaves the window.</summary>
        public new double Cursor   // the playback's moment (not the mouse pointer)
        {
            get => _cursor;
            set
            {
                if (Math.Abs(value - _cursor) < 1e-12) return;
                _cursor = value;
                if (_cursor > _viewStart + _viewSpan || _cursor < _viewStart) _viewStart = Math.Max(0, Math.Min(_duration - _viewSpan, _cursor - _viewSpan * 0.1));
                InvalidateVisual();
            }
        }

        private double PlotWidth => Math.Max(10, ActualWidth - LabelWidth - 8);

        private double X(double t) => LabelWidth + (t - _viewStart) / _viewSpan * PlotWidth;

        private double T(double x) => _viewStart + (x - LabelWidth) / PlotWidth * _viewSpan;

        private static double LaneHeight(TimelineLane l) => l.Kind switch
        {
            TimelineLaneKind.Analog when l.Binary => 18,
            TimelineLaneKind.Analog => 34,
            TimelineLaneKind.Spans => 16,
            _ => 18,
        };

        private double ContentHeight()
        {
            double h = 0;
            foreach (var g in _lanes.GroupBy(l => l.Group))
            {
                h += HeaderHeight;
                if (_folded.Contains(g.Key)) continue;
                foreach (var l in g) if (l.Default || _full.Contains(g.Key)) h += LaneHeight(l);
            }

            return h;
        }

        private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        private Brush ToneBrush(string tone) => tone switch
        {
            "failure" => Res("B.Danger", Brushes.IndianRed),
            "warning" => Res("B.Warning", Brushes.Orange),
            "impact" => Res("B.Accent", Brushes.DodgerBlue),
            "serial" => Res("B.Info", Brushes.SteelBlue),
            "logic" => Res("B.Success", Brushes.LimeGreen),
            "isr" => Res("B.Warning", Brushes.Orange),
            "analog" => Res("B.Accent", Brushes.DodgerBlue),
            _ => Res("B.TextSecondary", Brushes.Gray),
        };

        protected override Size MeasureOverride(Size availableSize) =>
            new Size(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 240 : availableSize.Height);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var text = Res("B.Text", Brushes.White);
            var quiet = Res("B.TextSecondary", Brushes.Gray);
            var border = new Pen(Res("B.Border", Brushes.DimGray), 1);
            dc.DrawRectangle(Res("B.Base", Brushes.Black), null, new Rect(0, 0, w, h));
            var face = new Typeface("Segoe UI");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText F(string s, Brush b, double size = 11, bool bold = false) =>
                new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? new Typeface(face.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal) : face, size, b, dpi)
                { MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            _rows.Clear();
            if (_lanes.Count == 0)
            {
                dc.DrawText(F(Lang.T("Render alınınca olaylar, yazılım, devre ve hareket burada tek zaman ekseninde görünür."), quiet), new Point(12, 10));
                return;
            }

            double max = Math.Max(0, ContentHeight() - (h - AxisHeight));
            _scroll = Math.Max(0, Math.Min(max, _scroll));
            double t0 = _viewStart, t1 = _viewStart + _viewSpan;

            // ---------------------------------------------------------------- lanes
            dc.PushClip(new RectangleGeometry(new Rect(0, AxisHeight, w, Math.Max(0, h - AxisHeight))));
            double y = AxisHeight - _scroll;
            foreach (var g in _lanes.GroupBy(l => l.Group))
            {
                bool folded = _folded.Contains(g.Key), full = _full.Contains(g.Key);
                int hidden = g.Count(l => !l.Default);
                var head = new Rect(0, y, w, HeaderHeight);
                if (y + HeaderHeight > AxisHeight && y < h)
                {
                    dc.DrawRectangle(Res("B.Raised", Brushes.DimGray), null, head);
                    var label = F((folded ? "▸ " : "▾ ") + Lang.T(g.Key), text, 11, true);
                    label.MaxTextWidth = Math.Max(10, w - 90);
                    dc.DrawText(label, new Point(6, y + 3));
                }

                var more = Rect.Empty;
                if (hidden > 0 && !folded)
                {
                    var m = F(full ? Lang.T("daha az") : "+" + hidden, Res("B.Accent", Brushes.DodgerBlue), 11, true);
                    more = new Rect(w - m.Width - 14, y, m.Width + 10, HeaderHeight);
                    if (y + HeaderHeight > AxisHeight && y < h) dc.DrawText(m, new Point(more.X + 5, y + 3));
                }

                _rows.Add((y, HeaderHeight, g.Key, null, more));
                y += HeaderHeight;
                if (folded) continue;
                foreach (var lane in g)
                {
                    if (!lane.Default && !full) continue;
                    double lh = LaneHeight(lane);
                    _rows.Add((y, lh, g.Key, lane, Rect.Empty));
                    if (y + lh > AxisHeight && y < h)
                    {
                        dc.DrawLine(border, new Point(0, y + lh - 0.5), new Point(w, y + lh - 0.5));
                        var title = F(Lang.T(lane.Title), quiet);
                        title.MaxTextWidth = LabelWidth - 14;
                        dc.DrawText(title, new Point(12, y + (lh - title.Height) / 2));
                        dc.PushClip(new RectangleGeometry(new Rect(LabelWidth, y, PlotWidth, lh)));
                        DrawLane(dc, lane, y, lh, t0, t1, F);
                        dc.Pop();
                    }

                    y += lh;
                }
            }

            // the playback's moment and the pointer's
            double cx = X(_cursor);
            if (cx >= LabelWidth && cx <= w) dc.DrawLine(new Pen(Res("B.Primary", Brushes.White), 1.5), new Point(cx, AxisHeight), new Point(cx, h));
            if (_hover is { } hp && hp.X >= LabelWidth) dc.DrawLine(new Pen(quiet, 1) { DashStyle = DashStyles.Dash }, new Point(hp.X, AxisHeight), new Point(hp.X, h));
            dc.Pop();

            // scroll position
            if (max > 0)
            {
                double track = h - AxisHeight, thumb = Math.Max(20, track * (h - AxisHeight) / (h - AxisHeight + max));
                dc.DrawRectangle(Res("B.BorderStrong", Brushes.Gray), null, new Rect(w - 4, AxisHeight + (track - thumb) * _scroll / max, 3, thumb));
            }

            // ---------------------------------------------------------------- the time axis (stays on top)
            dc.DrawRectangle(Res("B.Surface", Brushes.Black), null, new Rect(0, 0, w, AxisHeight));
            dc.DrawLine(border, new Point(0, AxisHeight - 0.5), new Point(w, AxisHeight - 0.5));
            double step = NiceStep(_viewSpan / Math.Max(1, PlotWidth / 90));
            for (double t = Math.Ceiling(t0 / step) * step; t <= t1 + 1e-12; t += step)
            {
                double x = X(t);
                dc.DrawLine(border, new Point(x, AxisHeight - 6), new Point(x, AxisHeight));
                var tick = F(TimelineLaneText.Time(t, step), quiet, 10);
                if (x + 3 + tick.Width <= w - 2) dc.DrawText(tick, new Point(x + 3, 4));   // a label that would run off the edge is left out
            }

            // the pointer's readout: its time and what the lane under it says
            if (_hover is { } p && p.X >= LabelWidth)
            {
                double t = T(p.X);
                string read = TimelineLaneText.Time(t, step / 100);
                var row = _rows.FirstOrDefault(r => r.Lane != null && p.Y >= r.Top && p.Y < r.Top + r.Height);
                if (row.Lane != null)
                {
                    string v = row.Lane.ReadAt(t, _viewSpan / PlotWidth * 3, _store);
                    if (v.Length > 0) read += "  ·  " + Lang.T(row.Lane.Title) + ": " + Lang.T(v);
                }

                var rt = F(read, text, 11);
                rt.MaxTextWidth = Math.Max(40, w - LabelWidth - 10);
                double rx = Math.Min(Math.Max(LabelWidth + 4, p.X + 8), w - rt.Width - 6);
                dc.DrawRectangle(Res("B.Raised", Brushes.DimGray), null, new Rect(rx - 4, 2, rt.Width + 8, AxisHeight - 4));
                dc.DrawText(rt, new Point(rx, 4));
            }

            dc.DrawText(F(Lang.T("Zaman"), quiet, 10), new Point(8, 5));
        }

        private void DrawLane(DrawingContext dc, TimelineLane lane, double y, double lh, double t0, double t1, Func<string, Brush, double, bool, FormattedText> f)
        {
            var brush = ToneBrush(lane.Tone);
            var pen = new Pen(brush, 1.2);
            double pw = PlotWidth, perPx = _viewSpan / pw;
            switch (lane.Kind)
            {
                case TimelineLaneKind.Marks:
                {
                    // one tick per pixel column that has any; a lane full of them reads as density
                    double last = double.NegativeInfinity;
                    foreach (var (t, _) in lane.Marks)
                    {
                        if (t < t0 - perPx || t > t1 + perPx) continue;
                        double x = Math.Round(X(t));
                        if (x == last) continue;
                        last = x;
                        dc.DrawRectangle(brush, null, new Rect(x - 1, y + 3, 2, lh - 6));
                    }

                    break;
                }

                case TimelineLaneKind.Spans:
                {
                    double lastEnd = double.NegativeInfinity;
                    foreach (var (s, e) in lane.Spans)
                    {
                        if (e < t0 || s > t1) continue;
                        double x0 = X(Math.Max(s, t0)), x1 = Math.Max(x0 + 1, X(Math.Min(e, t1)));
                        if (x1 <= lastEnd) continue;
                        x0 = Math.Max(x0, lastEnd);
                        lastEnd = x1;
                        dc.DrawRectangle(brush, null, new Rect(x0, y + 3, x1 - x0, lh - 6));
                    }

                    break;
                }

                case TimelineLaneKind.Logic when lane.Logic != null:
                {
                    var l = lane.Logic;
                    double hi = y + 4, lo = y + lh - 4;
                    double start = Math.Max(t0, l.Start);
                    if (start > t1) break;
                    bool level = l.LevelAt(start);
                    double x = X(start);
                    int i = FirstEdge(l.Edges, start);
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(new Point(x, level ? hi : lo), false, false);
                        while (i < l.Edges.Count && l.Edges[i] <= t1)
                        {
                            double ex = X(l.Edges[i]);
                            // many edges in one pixel: a solid block (a PWM or a serial byte at this zoom)
                            int j = i;
                            while (j + 1 < l.Edges.Count && X(l.Edges[j + 1]) - ex < 1) j++;
                            if (j > i)
                            {
                                g.LineTo(new Point(ex, level ? hi : lo), true, false);
                                dc.DrawRectangle(brush, null, new Rect(ex, hi, Math.Max(1, X(l.Edges[j]) - ex), lo - hi));
                                level ^= ((j - i + 1) & 1) == 1;
                                g.BeginFigure(new Point(X(l.Edges[j]), level ? hi : lo), false, false);
                                i = j + 1;
                                continue;
                            }

                            g.LineTo(new Point(ex, level ? hi : lo), true, false);
                            level = !level;
                            g.LineTo(new Point(ex, level ? hi : lo), true, false);
                            i++;
                        }

                        g.LineTo(new Point(X(t1), level ? hi : lo), true, false);
                    }

                    geo.Freeze();
                    dc.DrawGeometry(null, pen, geo);
                    break;
                }

                case TimelineLaneKind.Analog when lane.Channel != null && _store.Count > 0:
                {
                    var c = lane.Channel;
                    if (lane.Binary)
                    {
                        DrawSteps(dc, pen, c, y + 4, y + lh - 4, t0, t1);
                        break;
                    }

                    double range = lane.Max - lane.Min, top = y + 4, bottom = y + lh - 5;
                    double Yv(double v) => range <= 0 ? (top + bottom) / 2 : bottom - (v - lane.Min) / range * (bottom - top);
                    int a = Math.Max(0, TimelineLanes.FrameAt(_store, t0) - 1), b = Math.Min(Math.Min(c.Count, _store.Count) - 1, TimelineLanes.FrameAt(_store, t1) + 1);
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        // frames that share a pixel column become one stroke through their lowest and highest value
                        bool open = false, any = false;
                        double colX = 0, first = 0, colMin = 0, colMax = 0, last = 0;
                        void Flush()
                        {
                            if (!any) return;
                            if (!open) g.BeginFigure(new Point(colX, Yv(first)), false, false);
                            else g.LineTo(new Point(colX, Yv(first)), true, false);
                            if (colMax > colMin)
                            {
                                g.LineTo(new Point(colX, Yv(colMin)), true, false);
                                g.LineTo(new Point(colX, Yv(colMax)), true, false);
                                g.LineTo(new Point(colX, Yv(last)), true, false);
                            }

                            open = true;
                            any = false;
                        }

                        for (int k = a; k <= b; k++)
                        {
                            double v = c[k];
                            if (double.IsNaN(v) || double.IsInfinity(v))
                            {
                                Flush();
                                open = false;
                                continue;
                            }

                            double x = Math.Round(X(_store.Time(k)));
                            if (any && x == colX)
                            {
                                colMin = Math.Min(colMin, v);
                                colMax = Math.Max(colMax, v);
                                last = v;
                                continue;
                            }

                            Flush();
                            colX = x;
                            first = colMin = colMax = last = v;
                            any = true;
                        }

                        Flush();
                    }

                    geo.Freeze();
                    dc.DrawGeometry(null, pen, geo);
                    var range2 = f(TimelineLaneText.Range(lane), Res("B.TextMuted", Brushes.Gray), 9, false);
                    dc.DrawText(range2, new Point(LabelWidth + PlotWidth - range2.Width - 4, y + 1));
                    break;
                }
            }
        }

        /// <summary>A 0/1 channel as a logic trace: a step at each frame where it changes.</summary>
        private void DrawSteps(DrawingContext dc, Pen pen, Channel c, double hi, double lo, double t0, double t1)
        {
            int a = Math.Max(0, TimelineLanes.FrameAt(_store, t0)), b = Math.Min(Math.Min(c.Count, _store.Count) - 1, TimelineLanes.FrameAt(_store, t1) + 1);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool open = false;
                double prevY = 0;
                for (int k = a; k <= b; k++)
                {
                    double v = c[k];
                    if (double.IsNaN(v))
                    {
                        open = false;
                        continue;
                    }

                    double x = X(Math.Max(t0, _store.Time(k))), yv = v >= 0.5 ? hi : lo;
                    if (!open) g.BeginFigure(new Point(x, yv), false, false);
                    else if (yv != prevY)
                    {
                        g.LineTo(new Point(x, prevY), true, false);
                        g.LineTo(new Point(x, yv), true, false);
                    }

                    open = true;
                    prevY = yv;
                }

                if (open) g.LineTo(new Point(X(Math.Min(t1, _store.Time(b))), prevY), true, false);
            }

            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private static int FirstEdge(List<double> edges, double t)
        {
            int lo = 0, hi = edges.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (edges[mid] <= t) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        private static double NiceStep(double raw)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(1e-9, raw))));
            foreach (double m in new[] { 1, 2, 5, 10 })
                if (m * p >= raw) return m * p;
            return 10 * p;
        }

        // ---------------------------------------------------------------- input

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            var p = e.GetPosition(this);
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                double t = T(Math.Max(LabelWidth, p.X));
                double span = Math.Max(2e-6, Math.Min(_duration, _viewSpan * (e.Delta > 0 ? 0.8 : 1.25)));
                _viewStart = Math.Max(0, Math.Min(_duration - span, t - (t - _viewStart) * span / _viewSpan));
                _viewSpan = span;
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                _viewStart = Math.Max(0, Math.Min(_duration - _viewSpan, _viewStart - Math.Sign(e.Delta) * _viewSpan * 0.15));
            }
            else
            {
                _scroll -= e.Delta / 120.0 * 48;
            }

            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            if (e.ClickCount == 2)
            {
                _viewStart = 0;
                _viewSpan = _duration;
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            foreach (var r in _rows.Where(r => r.Lane == null && p.Y >= r.Top && p.Y < r.Top + r.Height && p.Y >= AxisHeight))
            {
                if (!r.More.IsEmpty && r.More.Contains(p))
                {
                    if (!_full.Remove(r.Group)) _full.Add(r.Group);
                }
                else if (!_folded.Remove(r.Group)) _folded.Add(r.Group);

                InvalidateVisual();
                e.Handled = true;
                return;
            }

            _down = p;
            _downStart = _viewStart;
            _dragging = false;
            CaptureMouse();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            _hover = p;
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            {
                if (Math.Abs(p.X - _down.X) > 3) _dragging = true;
                if (_dragging) _viewStart = Math.Max(0, Math.Min(_duration - _viewSpan, _downStart - (p.X - _down.X) / PlotWidth * _viewSpan));
            }

            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!IsMouseCaptured) return;
            ReleaseMouseCapture();
            var p = e.GetPosition(this);
            if (!_dragging && p.X >= LabelWidth && p.Y >= AxisHeight) TimeClicked?.Invoke(Math.Max(0, Math.Min(_duration, T(p.X))));
            _dragging = false;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            _hover = null;
            InvalidateVisual();
        }
    }

    /// <summary>[unified-timeline] Times and ranges as the view writes them.</summary>
    internal static class TimelineLaneText
    {
        /// <summary>A time written as finely as <paramref name="step"/> needs: s down to 10 ms steps, ms down to 10 µs, then µs.</summary>
        public static string Time(double t, double step)
        {
            var c = CultureInfo.CurrentCulture;
            if (step >= 0.01) return t.ToString(step >= 1 ? "0.#" : step >= 0.1 ? "0.0#" : "0.0##", c) + " s";
            if (step >= 1e-5) return (t * 1e3).ToString(step >= 1e-3 ? "0.#" : "0.0##", c) + " ms";
            return (t * 1e6).ToString("0.#", c) + " µs";
        }

        public static string Range(TimelineLane l) => string.Format(CultureInfo.CurrentCulture, "{0} … {1}{2}", Fmt(l.Min), Fmt(l.Max), l.Unit.Length > 0 ? " " + l.Unit : string.Empty);

        private static string Fmt(double v)
        {
            double a = Math.Abs(v);
            return a != 0 && (a < 1e-3 || a >= 1e5) ? v.ToString("0.##e0", CultureInfo.CurrentCulture) : v.ToString(a < 1 ? "0.###" : "0.##", CultureInfo.CurrentCulture);
        }
    }
}
