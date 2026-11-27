using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Firmware;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [timeline] The debugger's Zaman çizelgesi: the chip's own logic analyzer (<see cref="McuTrace"/>) drawn as one
    /// trace per pin that moved or drives, and one row per interrupt serviced, each handler a bar from its entry to its
    /// RETI. The right column measures the last full period of a pin and the longest run of a handler. The wheel zooms
    /// around the pointer, dragging scrolls back in time, a double click returns to now; the pointer's time is read out.
    /// </summary>
    public sealed class TimelineView : FrameworkElement
    {
        private const double LabelWidth = 96, MeasureWidth = 200, AxisHeight = 22, RowHeight = 22;
        private Atmega328P? _mcu;
        private ulong _nowCycles;
        private double _nowTime;
        private double _offset;          // seconds between now and the right edge
        private double? _pointer;        // x of the mouse, for the read-out
        private Point? _drag;
        private double _dragOffset;
        private double _needed = 120;    // the height the last drawing's rows took

        public TimelineView()
        {
            ClipToBounds = true;
            Focusable = false;
            // [timebase] the instant the scope's pointer is on, marked here
            Action redraw = InvalidateVisual;
            Loaded += (_, _) => Shell.TimeCursor.Changed += redraw;
            Unloaded += (_, _) => Shell.TimeCursor.Changed -= redraw;
        }

        /// <summary>[timebase] The session whose time this is (the program's owner), shared with the scope's cursor.</summary>
        public object? Clock { get; set; }

        /// <summary>
        /// [timebase] Shows the seconds from <paramref name="start"/> to <paramref name="end"/> (the scope's window: "Osiloskopla
        /// eşle"), as far as the recording reaches; false when it is not in the recording at all.
        /// </summary>
        public bool ShowSpan(double start, double end)
        {
            if (_mcu == null || end <= start || end < Oldest || start > _nowTime) return false;
            Window = Math.Max(20e-6, Math.Min(2.0, end - start));
            _offset = _nowTime - end;
            Clamp();
            InvalidateVisual();
            ViewChanged?.Invoke();
            return true;
        }

        /// <summary>Seconds across the plot.</summary>
        public double Window { get; private set; } = 0.02;

        /// <summary>The right edge's program time.</summary>
        public double RightEdge => _nowTime - _offset;

        /// <summary>Zoom, pan or a new look changed what is shown (the page updates its note).</summary>
        public event Action? ViewChanged;

        /// <summary>Shows <paramref name="mcu"/>'s trace up to now (<paramref name="programTime"/> is its program time at this cycle).</summary>
        public void Show(Atmega328P? mcu, double programTime)
        {
            _mcu = mcu;
            if (mcu != null)
            {
                _nowCycles = mcu.Cycles;
                _nowTime = programTime;
            }

            InvalidateVisual();
            ViewChanged?.Invoke();
        }

        /// <summary>Back to the present.</summary>
        public void Now()
        {
            _offset = 0;
            InvalidateVisual();
            ViewChanged?.Invoke();
        }

        private double PlotLeft => LabelWidth;

        private double PlotRight => Math.Max(PlotLeft + 40, ActualWidth - MeasureWidth);

        private double Oldest => _mcu?.Trace is { } tr ? _nowTime - (_nowCycles - Math.Min(_nowCycles, tr.StartCycle)) / _mcu.ClockHz : _nowTime;

        private void Clamp()
        {
            _offset = Math.Max(0, Math.Min(_offset, Math.Max(0, _nowTime - Oldest - Window * 0.5)));
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            double x = e.GetPosition(this).X, span = PlotRight - PlotLeft;
            double frac = Math.Max(0, Math.Min(1, (PlotRight - x) / span));
            double at = RightEdge - frac * Window;                       // the time under the pointer stays put
            Window = Math.Max(20e-6, Math.Min(2.0, Window * (e.Delta > 0 ? 0.5 : 2)));
            _offset = _nowTime - (at + frac * Window);
            Clamp();
            InvalidateVisual();
            ViewChanged?.Invoke();
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Now();
                return;
            }

            _drag = e.GetPosition(this);
            _dragOffset = _offset;
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            _drag = null;
            ReleaseMouseCapture();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            _pointer = p.X >= PlotLeft && p.X <= PlotRight ? p.X : null;
            // [timebase] the instant under the pointer, for the scope to mark
            Shell.TimeCursor.Set(Clock, this, _pointer is { } at && _mcu != null ? RightEdge - Window + (at - PlotLeft) / (PlotRight - PlotLeft) * Window : null);
            if (_drag is { } start)
            {
                _offset = _dragOffset + (p.X - start.X) / (PlotRight - PlotLeft) * Window;
                Clamp();
                ViewChanged?.Invoke();
            }

            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            Shell.TimeCursor.Set(Clock, this, null);   // [timebase]
            _pointer = null;
            InvalidateVisual();
        }

        private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        /// <summary>As tall as its rows, so a short panel scrolls.</summary>
        protected override Size MeasureOverride(Size availableSize) => new Size(0, _needed);

        /// <summary>"350 µs", "17.67 ms", "1.20 s".</summary>
        public static string Duration(double s)
        {
            double a = Math.Abs(s);
            var c = CultureInfo.CurrentCulture;
            if (a < 1e-3) return (s * 1e6).ToString(a < 10e-6 ? "0.0" : "0", c) + " µs";
            if (a < 1) return (s * 1e3).ToString(a < 10e-3 ? "0.00" : a < 100e-3 ? "0.0" : "0", c) + " ms";
            return s.ToString("0.00", c) + " s";
        }

        /// <summary>An axis label in the unit of the tick step, with as many decimals as the step needs ("-18 ms", "-2.5 ms").</summary>
        private static string Tick(double s, double step)
        {
            double scale = step < 1e-3 ? 1e6 : step < 1 ? 1e3 : 1;
            string unit = step < 1e-3 ? " µs" : step < 1 ? " ms" : " s";
            int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step * scale) - 1e-9));
            return (s * scale).ToString("F" + decimals, CultureInfo.CurrentCulture) + unit;
        }

        private static double NiceStep(double raw)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (m * p >= raw) return m * p;
            return 10 * p;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var text = Res("B.Text", Brushes.White);
            var quiet = Res("B.TextSecondary", Brushes.Gray);
            var border = new Pen(Res("B.Border", Brushes.DimGray), 1);
            var accent = Res("B.Accent", Brushes.DodgerBlue);
            var soft = Res("B.AccentSoft", Brushes.SlateGray);
            var tracePen = new Pen(Res("B.Success", Brushes.LimeGreen), 1.4);
            var isrBrush = Res("B.Warning", Brushes.Orange);
            dc.DrawRectangle(Res("B.Base", Brushes.Black), null, new Rect(0, 0, w, h));
            var face = new Typeface("Segoe UI");
            var mono = new Typeface("Consolas");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText T(string s, Brush b, double size = 12, Typeface? f = null) =>
                new FormattedText(Shell.Lang.T(s), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, f ?? face, size, b, dpi);

            var mcu = _mcu;
            var trace = mcu?.Trace;
            if (mcu == null || trace == null)
            {
                dc.DrawText(T("Kart çalışırken pinleri ve kesmeleri burada bir mantık analizörü gibi görürsünüz.", quiet), new Point(12, 12));
                return;
            }

            double clock = mcu.ClockHz, x0 = PlotLeft, x1 = PlotRight, span = x1 - x0;
            double right = RightEdge, left = right - Window;
            ulong Cycle(double t) => (ulong)Math.Max(0, Math.Round(_nowCycles - (_nowTime - t) * clock));
            double X(ulong c) => x0 + ((_nowTime - (_nowCycles - (double)c) / clock) - left) / Window * span;
            ulong cl = Cycle(left), cr = Cycle(right);

            // One pass: each pin's level at the left edge and its edges inside, the handlers that overlap the window.
            var level = new bool[McuTrace.Pins];
            for (int p = 0; p < McuTrace.Pins; p++) level[p] = trace.LevelAtStart(p);
            var edges = new List<(ulong Cycle, bool Level)>[McuTrace.Pins];
            for (int p = 0; p < McuTrace.Pins; p++) edges[p] = new List<(ulong, bool)>();
            var open = new Stack<(int Vector, ulong Enter)>();
            var isrs = new List<(int Vector, ulong Enter, ulong Return)>();
            int n = trace.Count;
            for (int i = 0; i < n; i++)
            {
                var e = trace[i];
                if (e.Cycle > cr && open.Count == 0) break;
                switch (e.Kind)
                {
                    case TraceKind.Pin when e.Cycle <= cl:
                        level[e.Index] = e.Level;
                        break;
                    case TraceKind.Pin when e.Cycle <= cr:
                        edges[e.Index].Add((e.Cycle, e.Level));
                        break;
                    case TraceKind.Enter when e.Cycle <= cr:
                        open.Push((e.Index, e.Cycle));
                        break;
                    case TraceKind.Return when open.Count > 0:
                        var (v, at) = open.Pop();
                        if (e.Cycle >= cl) isrs.Add((v, at, e.Cycle));
                        break;
                }
            }

            foreach (var (v, at) in open) isrs.Add((v, at, ulong.MaxValue));

            var pins = Enumerable.Range(0, McuTrace.Pins).Where(p => edges[p].Count > 0 || mcu.GetPin(p).Driving || mcu.GetPin(p).IsPwm).ToList();
            var vectors = isrs.Select(i => i.Vector).Distinct().OrderBy(v => v).ToList();

            // Time axis: ticks back from the right edge.
            double step = NiceStep(Window / Math.Max(2, span / 110));
            for (double back = 0; back <= Window + 1e-12; back += step)
            {
                double x = x1 - back / Window * span;
                dc.DrawLine(border, new Point(x, AxisHeight - 4), new Point(x, h));
                var label = T(back == 0 && _offset == 0 ? "şimdi" : Tick(-back - _offset, step), quiet, 11);
                dc.DrawText(label, new Point(Math.Max(x0, Math.Min(x - label.Width / 2, x1 - label.Width)), 3));
            }

            double y = AxisHeight;
            if (pins.Count == 0 && vectors.Count == 0)
            {
                dc.DrawText(T("Bu pencerede değişen pin ya da kesme yok: tekerlekle uzaklaştırın.", quiet), new Point(x0 + 8, y + 8));
            }

            foreach (int pin in pins)
            {
                dc.DrawText(T(AvrRegisters.PinName(pin), text, 12.5, mono), new Point(8, y + 3));
                double hi = y + 5, lo = y + RowHeight - 5;
                var state = mcu.GetPin(pin);
                if (state.IsPwm && state.PwmDuty > 0 && state.PwmDuty < 1)
                {
                    // The timer toggles it without the program: no edges here, its waveform as now.
                    dc.DrawRectangle(soft, null, new Rect(x0, hi, span, lo - hi));
                    dc.DrawText(T($"PWM {state.PwmFrequency.ToString("0", CultureInfo.CurrentCulture)} Hz · %{(state.PwmDuty * 100).ToString("0", CultureInfo.CurrentCulture)} (şimdi)", text, 11.5), new Point(x1 + 10, y + 3));
                }
                else
                {
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        // Edges closer than a pixel stack into one solid stroke, as an analyzer draws a burst.
                        bool l = level[pin];
                        g.BeginFigure(new Point(x0, l ? hi : lo), false, false);
                        foreach (var (c, nl) in edges[pin])
                        {
                            double x = Math.Max(x0, Math.Min(x1, X(c)));
                            g.LineTo(new Point(x, l ? hi : lo), true, false);
                            g.LineTo(new Point(x, nl ? hi : lo), true, false);
                            l = nl;
                        }

                        g.LineTo(new Point(x1, l ? hi : lo), true, false);
                    }

                    geo.Freeze();
                    dc.DrawGeometry(null, tracePen, geo);
                    dc.DrawText(T(PinMeasure(edges[pin], level[pin], clock), quiet, 11.5), new Point(x1 + 10, y + 3));
                }

                dc.DrawLine(border, new Point(0, y + RowHeight), new Point(w, y + RowHeight));
                y += RowHeight;
            }

            foreach (int v in vectors)
            {
                dc.DrawText(T(AvrRegisters.VectorName(v), accent, 11.5, mono), new Point(8, y + 4));
                double longest = 0;
                int count = 0;
                foreach (var (vec, enter, ret) in isrs)
                {
                    if (vec != v) continue;
                    count++;
                    double a = Math.Max(x0, X(enter)), b = ret == ulong.MaxValue ? x1 : Math.Min(x1, X(ret));
                    if (ret != ulong.MaxValue) longest = Math.Max(longest, (ret - enter) / clock);
                    dc.DrawRectangle(isrBrush, null, new Rect(a, y + 5, Math.Max(1, b - a), RowHeight - 10));
                }

                dc.DrawText(T($"{count}× · en uzun {Duration(longest)}", quiet, 11.5), new Point(x1 + 10, y + 3));
                dc.DrawLine(border, new Point(0, y + RowHeight), new Point(w, y + RowHeight));
                y += RowHeight;
            }

            double need = Math.Max(120, y + 8);
            if (Math.Abs(need - _needed) > 0.5)
            {
                _needed = need;
                Dispatcher.BeginInvoke(new Action(InvalidateMeasure));
            }

            dc.DrawLine(border, new Point(x0, AxisHeight), new Point(x0, Math.Max(y, AxisHeight)));
            dc.DrawLine(border, new Point(x1, AxisHeight), new Point(x1, Math.Max(y, AxisHeight)));

            // [timebase] the scope's pointer: a dashed accent line, or an arrow at the edge it lies beyond
            if (Shell.TimeCursor.For(Clock, this) is { } st)
            {
                double sx = x0 + (st - left) / Window * span;
                if (sx >= x0 && sx <= x1)
                {
                    var linked = new Pen(accent, 1.2) { DashStyle = DashStyles.Dash };
                    dc.DrawLine(linked, new Point(sx, AxisHeight - 2), new Point(sx, Math.Max(y, h)));
                    var sread = T(Shell.Lang.T("osiloskop") + " t = " + st.ToString("0.000000", CultureInfo.CurrentCulture) + " s", accent, 11);
                    double srx = Math.Min(sx + 4, w - sread.Width - 4);
                    dc.DrawRectangle(Res("B.Surface", Brushes.Black), null, new Rect(srx - 2, AxisHeight + 2, sread.Width + 4, sread.Height));
                    dc.DrawText(sread, new Point(srx, AxisHeight + 2));
                }
                else
                {
                    double ax = sx < x0 ? x0 + 2 : x1 - 2, dir = sx < x0 ? -1 : 1, ay = AxisHeight + 8;
                    var head = new StreamGeometry();
                    using (var g = head.Open())
                    {
                        g.BeginFigure(new Point(ax + dir * 6, ay), true, true);
                        g.LineTo(new Point(ax, ay - 5), true, true);
                        g.LineTo(new Point(ax, ay + 5), true, true);
                    }

                    head.Freeze();
                    dc.DrawGeometry(accent, null, head);
                }
            }

            if (_pointer is { } px)
            {
                var cursor = new Pen(text, 1) { DashStyle = DashStyles.Dash };
                dc.DrawLine(cursor, new Point(px, AxisHeight - 2), new Point(px, Math.Max(y, h)));
                double t = left + (px - x0) / span * Window;
                var read = T("t = " + t.ToString("0.000000", CultureInfo.CurrentCulture) + " s", text, 11);
                double rx = Math.Min(px + 4, w - read.Width - 4);
                dc.DrawRectangle(Res("B.Surface", Brushes.Black), null, new Rect(rx - 2, 2, read.Width + 4, read.Height));
                dc.DrawText(read, new Point(rx, 2));
            }
        }

        /// <summary>A pin's last full period in the window ("56.5 Hz · yüksek 1.03 ms"), or how still it was.</summary>
        private static string PinMeasure(List<(ulong Cycle, bool Level)> edges, bool startLevel, double clock)
        {
            if (edges.Count == 0) return startLevel ? "sabit yüksek" : "sabit düşük";
            var rises = edges.Where(e => e.Level).Select(e => e.Cycle).ToList();
            if (rises.Count >= 2)
            {
                ulong r0 = rises[rises.Count - 2], r1 = rises[rises.Count - 1];
                double period = (r1 - r0) / clock;
                var fall = edges.FirstOrDefault(e => !e.Level && e.Cycle > r0 && e.Cycle < r1);
                string f = period > 0 ? (1 / period).ToString(1 / period >= 100 ? "0" : "0.0", CultureInfo.CurrentCulture) + " Hz" : "?";
                return fall.Cycle > 0 ? $"{f} · yüksek {Duration((fall.Cycle - r0) / clock)}" : f;
            }

            return edges.Count == 1 ? "1 kenar" : $"{edges.Count} kenar";
        }
    }
}
