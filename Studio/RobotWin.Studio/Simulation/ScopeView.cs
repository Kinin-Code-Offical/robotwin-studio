using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Simulation
{
    public enum ScopeCursorMode
    {
        Off,
        Time,
        Voltage
    }

    /// <summary>
    /// The screen of the 4-channel digital oscilloscope: 10 × 8 graticule, triggered traces in
    /// the classic channel colours, ground and trigger markers, status line, scale readouts,
    /// automatic measurements, cursors and XY mode. Settings live in <see cref="Oscilloscope"/>.
    /// </summary>
    public sealed class ScopeView : FrameworkElement
    {
        private static readonly Color Back = Color.FromRgb(0x07, 0x08, 0x09);
        private static readonly Color Screen = Color.FromRgb(0x0A, 0x0B, 0x0C);
        private static readonly Color GridLine = Color.FromRgb(0x22, 0x24, 0x27);
        private static readonly Color GridAxis = Color.FromRgb(0x3A, 0x3D, 0x42);
        private static readonly Color Muted = Color.FromRgb(0x6C, 0x6C, 0x74);
        private static readonly Color Text = Color.FromRgb(0xC4, 0xC4, 0xCA);
        private static readonly Color TriggerColor = Color.FromRgb(0xFF, 0x9A, 0x2E);
        private static readonly Color CursorColor = Color.FromRgb(0xE8, 0xE8, 0xEC);
        private static readonly Color RunColor = Color.FromRgb(0x5B, 0xB9, 0x8B);
        private static readonly Color StopColor = Color.FromRgb(0xE5, 0x48, 0x4D);
        private static readonly Color WaitColor = Color.FromRgb(0xD4, 0xA8, 0x4B);
        public static readonly Color MathColor = Color.FromRgb(0xB5, 0x8C, 0xFF);

        /// <summary>Classic channel colours: yellow, cyan, magenta, blue.</summary>
        public static readonly Color[] ChannelColors =
        {
            Color.FromRgb(0xF2, 0xD1, 0x3A), Color.FromRgb(0x3A, 0xD6, 0xE5), Color.FromRgb(0xE0, 0x5A, 0xD0), Color.FromRgb(0x4A, 0x8C, 0xFF)
        };

        private Rect _plot;
        private int _dragCursor = -1;

        public ScopeView()
        {
            ClipToBounds = true;
            Focusable = true;
            var sim = Workspace.Current.Simulation;
            sim.Updated += (_, _) =>
            {
                if (sim.IsActive)
                {
                    for (int i = 0; i < sim.ScopeAutoScale.Length; i++)
                    {
                        if (sim.ScopeAutoScale[i] && sim.Time > 2e-3 && sim.Scope.AutoScaleChannel(sim.Scope.Channels[i], sim.Time, true))
                        {
                            sim.ScopeAutoScale[i] = false;
                            sim.NotifyInstrumentsChanged();
                        }
                    }

                    sim.Scope.Acquire(sim.Time);
                }

                InvalidateVisual();
            };
            sim.InstrumentsChanged += (_, _) => InvalidateVisual();
            sim.Probes.CollectionChanged += (_, _) => InvalidateVisual();
        }

        private static SimulationSession Sim => Workspace.Current.Simulation;

        private static Oscilloscope Scope => Sim.Scope;

        public ScopeCursorMode CursorMode { get; set; }

        /// <summary>Cursor positions in divisions: T1, T2 (0..10 from the left), V1, V2 (−4..4 from the centre).</summary>
        public double[] Cursors { get; } = { 3.0, 7.0, 2.0, -2.0 };

        /// <summary>Channel the cursors and the measurement row refer to (4 = math).</summary>
        public int MeasureChannel { get; set; }

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Label(string s, Color c, double size = 11, bool bold = false) =>
            new FormattedText(RobotWin.Studio.Shell.Lang.T(s), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                bold ? Typefaces.UiSemi : Typefaces.Mono, size, SymbolRenderer.Brush(c), Dpi);

        private static Pen Pen(Color c, double w, bool dashed = false)
        {
            var p = new Pen(SymbolRenderer.Brush(c), w);
            if (dashed) p.DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0);
            p.Freeze();
            return p;
        }

        public static Color ColorOf(int channel) => channel >= 0 && channel < ChannelColors.Length ? ChannelColors[channel] : MathColor;

        public void ForceAcquire()
        {
            Scope.ResetTriggerScan();
            if (Sim.IsActive || !double.IsNaN(Sim.ReplayTime)) Scope.Acquire(Sim.InstrumentTime);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            using var gate = SimThread.Hold();   // [perf] the probes are sampled on the simulation thread
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(SymbolRenderer.Brush(Back), null, new Rect(0, 0, w, h));
            var frame = Scope.Frame;

            const double top = 24, bottom = 50, side = 26;
            double ph = Math.Max(40, h - top - bottom);
            double pw = Math.Max(60, w - 2 * side);
            // 10 × 8 divisions; in a wide, short dock a division may be up to four times as wide as tall.
            double divH = ph / 8.0, divW = Math.Min(pw / 10.0, 4.0 * divH);
            pw = divW * 10;
            _plot = new Rect(Math.Round(side + (w - 2 * side - pw) / 2) + 0.5, Math.Round(top) + 0.5, Math.Round(pw), Math.Round(ph));
            var plot = _plot;
            dc.DrawRectangle(SymbolRenderer.Brush(Screen), Pen(GridAxis, 1), plot);
            DrawGraticule(dc, plot);
            DrawStatus(dc, frame, w);

            if (Scope.Channels.All(c => c.Source == null))
            {
                var hint = new FormattedText(
                    RobotWin.Studio.Shell.Lang.T(Sim.IsActive
                        ? "Probu bağlamak için şemada bir kabloya tıklayın (CH1, CH2…).\nCtrl + parçaya tıklama: akım probu."
                        : "Simülasyonu başlatın, ardından ölçmek istediğiniz kabloya tıklayın."),
                    CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typefaces.Ui, 12.5, SymbolRenderer.Brush(Muted), Dpi)
                {
                    TextAlignment = TextAlignment.Center
                };
                dc.DrawText(hint, new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2 - hint.Height / 2));
                DrawScaleRow(dc, plot);
                return;
            }

            if (frame != null)
            {
                dc.PushClip(new RectangleGeometry(plot));
                if (Scope.XY) DrawXY(dc, plot, frame);
                else
                {
                    if (frame.Math != null) DrawTrace(dc, plot, frame, frame.Math, MathColor);
                    for (int i = frame.Traces.Count - 1; i >= 0; i--)
                    {
                        var t = frame.Traces[i];
                        DrawTrace(dc, plot, frame, t, ColorOf(t.Channel?.Index ?? 0));
                    }
                }

                dc.Pop();
            }

            if (!Scope.XY)
            {
                DrawMarkers(dc, plot, frame);
                DrawCursors(dc, plot, frame);
            }

            DrawScaleRow(dc, plot);
            DrawMeasurements(dc, plot, frame);
        }

        private void DrawGraticule(DrawingContext dc, Rect plot)
        {
            var dot = Pen(GridLine, 1, true);
            for (int i = 1; i < 10; i++)
            {
                double x = Math.Round(plot.Left + plot.Width * i / 10) + 0.5;
                dc.DrawLine(i == 5 ? Pen(GridAxis, 1) : dot, new Point(x, plot.Top), new Point(x, plot.Bottom));
            }

            for (int i = 1; i < 8; i++)
            {
                double y = Math.Round(plot.Top + plot.Height * i / 8) + 0.5;
                dc.DrawLine(i == 4 ? Pen(GridAxis, 1) : dot, new Point(plot.Left, y), new Point(plot.Right, y));
            }

            // Minor ticks (5 per division) on the centre axes and the border.
            var tick = Pen(GridAxis, 1);
            double cx = Math.Round(plot.Left + plot.Width / 2) + 0.5, cy = Math.Round(plot.Top + plot.Height / 2) + 0.5;
            for (int i = 0; i <= 50; i++)
            {
                double x = plot.Left + plot.Width * i / 50.0;
                dc.DrawLine(tick, new Point(x, cy - 3), new Point(x, cy + 3));
                dc.DrawLine(tick, new Point(x, plot.Top), new Point(x, plot.Top + 3));
                dc.DrawLine(tick, new Point(x, plot.Bottom - 3), new Point(x, plot.Bottom));
            }

            for (int i = 0; i <= 40; i++)
            {
                double y = plot.Top + plot.Height * i / 40.0;
                dc.DrawLine(tick, new Point(cx - 3, y), new Point(cx + 3, y));
                dc.DrawLine(tick, new Point(plot.Left, y), new Point(plot.Left + 3, y));
                dc.DrawLine(tick, new Point(plot.Right - 3, y), new Point(plot.Right, y));
            }
        }

        private void DrawStatus(DrawingContext dc, ScopeFrame? frame, double w)
        {
            string run;
            Color runColor;
            if (!Scope.Running)
            {
                run = "STOP";
                runColor = StopColor;
            }
            else if (Scope.Status == TriggerStatus.Waiting)
            {
                run = Scope.TriggerMode == TriggerMode.Single ? "TEK" : "BEKLE";
                runColor = WaitColor;
            }
            else
            {
                run = "RUN";
                runColor = RunColor;
            }

            var runText = Label(run, Back, 11, true);
            var badge = new Rect(_plot.Left, 4, runText.Width + 12, 16);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(runColor), null, badge, 3, 3);
            dc.DrawText(runText, new Point(badge.Left + 6, badge.Top + (16 - runText.Height) / 2));

            string status = Shell.Lang.T((frame?.Status ?? Scope.Status) switch
            {
                TriggerStatus.Triggered => "Trig'd",
                TriggerStatus.Auto => "Auto (tetiksiz)",
                TriggerStatus.Roll => "Roll",
                TriggerStatus.Waiting => "Tetik bekleniyor",
                _ => "Durduruldu"
            });   // [lang] before it joins the status line
            double fs = Scope.SampleRate;
            var hText = Label($"H {SiValue.Format(Scope.SecondsPerDiv, "s")}/div   ◀▶ {SiValue.Format(Scope.Delay, "s")}", Text, 10.5);
            dc.DrawText(hText, new Point(_plot.Right - hText.Width, 4 + (16 - hText.Height) / 2));
            double room = _plot.Right - hText.Width - 12 - (badge.Right + 10);
            string points = Shell.Lang.T($"{(frame?.Length ?? Scope.RecordLength) / 1000.0:0.#}k nokta".Replace(',', '.'));   // [lang] before it joins the line
            foreach (string candidate in new[]
            {
                $"{status}   {SiValue.Format(fs, "Sa/s", 3)}   {points}   BW {SiValue.Format(Scope.Bandwidth, "Hz")}",
                $"{status}   {SiValue.Format(fs, "Sa/s", 3)}",
                status
            })
            {
                var st = Label(candidate, Text, 10.5);
                if (st.Width > room && candidate != status) continue;
                dc.DrawText(st, new Point(badge.Right + 10, 4 + (16 - st.Height) / 2));
                break;
            }
        }

        private void DrawScaleRow(DrawingContext dc, Rect plot)
        {
            double x = plot.Left;
            double y = plot.Bottom + 5;
            foreach (var ch in Scope.Channels)
            {
                var color = ch.Source == null ? Muted : ColorOf(ch.Index);
                if (!ch.Enabled) color = Muted;
                string coupling = ch.Coupling == ScopeCoupling.GND ? "⏚" : ch.Coupling.ToString();
                string text = ch.Source == null
                    ? $"{ch.Index + 1}  —"
                    : $"{ch.Index + 1} {SiValue.Format(ch.Scale, ch.Unit)}/div {coupling}{(ch.IsCurrent ? " " + Shell.Lang.T("akım") : $" {ch.ProbeAttenuation:0}×")}{(ch.BandwidthLimit ? " BW" : "")}{(ch.Invert ? " INV" : "")}";
                var ft = Label(text.Replace(" ", " "), color, 10.5);
                var box = new Rect(x, y, ft.Width + 10, ft.Height + 4);
                dc.DrawRoundedRectangle(null, Pen(ch.Index == Sim.SelectedScopeChannel ? color : Color.FromArgb(0x50, color.R, color.G, color.B), 1), box, 3, 3);
                dc.DrawText(ft, new Point(x + 5, y + 2));
                x = box.Right + 6;
            }

            if (Scope.MathFunction != ScopeMathFunction.Off)
            {
                var math = Scope.Frame?.Math;   // [scope] its name and unit follow the function (CH1×CH2 in W, ∫CH2 in C …)
                var ft = Label(math != null ? $"M {math.Name} {SiValue.Format(Scope.MathScale, math.Unit)}/div" : "M —", MathColor, 10.5);
                dc.DrawText(ft, new Point(x + 2, y + 2));
                x += ft.Width + 12;
            }

            var src = Scope.Channels[Math.Max(0, Math.Min(3, Scope.TriggerSource))];
            string slope = Scope.TriggerSlope == TriggerSlope.Rising ? "↗" : "↘";
            string mode = Scope.TriggerMode switch { TriggerMode.Normal => "Normal", TriggerMode.Single => "Tek", _ => "Auto" };
            var trig = Label($"T CH{src.Index + 1} {slope} {SiValue.Format(Scope.TriggerLevel, src.Unit, 3)} {mode}" +
                             (Scope.Holdoff > 0 ? $" HO {SiValue.Format(Scope.Holdoff, "s")}" : string.Empty), TriggerColor, 10.5);
            dc.DrawText(trig, new Point(Math.Max(x, plot.Right - trig.Width), y + 2));
        }

        private void DrawMarkers(DrawingContext dc, Rect plot, ScopeFrame? frame)
        {
            double divH = plot.Height / 8.0;
            foreach (var ch in Scope.Channels)
            {
                if (ch.Source == null || !ch.Enabled) continue;
                double y = plot.Top + plot.Height / 2 - ch.Position * divH;
                y = Math.Max(plot.Top + 6, Math.Min(plot.Bottom - 6, y));
                var color = ColorOf(ch.Index);
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(new Point(plot.Left - 16, y - 6), true, true);
                    s.LineTo(new Point(plot.Left - 6, y - 6), true, true);
                    s.LineTo(new Point(plot.Left - 1, y), true, true);
                    s.LineTo(new Point(plot.Left - 6, y + 6), true, true);
                    s.LineTo(new Point(plot.Left - 16, y + 6), true, true);
                }

                g.Freeze();
                dc.DrawGeometry(SymbolRenderer.Brush(color), null, g);
                var n = Label((ch.Index + 1).ToString(CultureInfo.InvariantCulture), Back, 9.5, true);
                dc.DrawText(n, new Point(plot.Left - 14, y - n.Height / 2));
            }

            // Trigger level on the right edge.
            var src = Scope.Channels[Math.Max(0, Math.Min(3, Scope.TriggerSource))];
            if (src.Source != null)
            {
                double ty = plot.Top + plot.Height / 2 - (Scope.TriggerLevel / src.Scale + src.Position) * divH;
                bool off = ty < plot.Top || ty > plot.Bottom;
                ty = Math.Max(plot.Top + 6, Math.Min(plot.Bottom - 6, ty));
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(new Point(plot.Right + 16, ty - 6), true, true);
                    s.LineTo(new Point(plot.Right + 6, ty - 6), true, true);
                    s.LineTo(new Point(plot.Right + 1, ty), true, true);
                    s.LineTo(new Point(plot.Right + 6, ty + 6), true, true);
                    s.LineTo(new Point(plot.Right + 16, ty + 6), true, true);
                }

                g.Freeze();
                dc.DrawGeometry(off ? null : SymbolRenderer.Brush(TriggerColor), Pen(TriggerColor, 1), g);
                var t = Label("T", off ? TriggerColor : Back, 9.5, true);
                dc.DrawText(t, new Point(plot.Right + 8, ty - t.Height / 2));
            }

            // Trigger point on the top edge.
            double tx = plot.Left + plot.Width / 2 - Scope.Delay / Scope.SecondsPerDiv * plot.Width / 10.0;
            if (frame != null && frame.Status != TriggerStatus.Roll && tx >= plot.Left && tx <= plot.Right)
            {
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(new Point(tx - 6, plot.Top - 9), true, true);
                    s.LineTo(new Point(tx + 6, plot.Top - 9), true, true);
                    s.LineTo(new Point(tx, plot.Top - 1), true, true);
                }

                g.Freeze();
                dc.DrawGeometry(SymbolRenderer.Brush(TriggerColor), null, g);
            }
        }

        private static void DrawTrace(DrawingContext dc, Rect plot, ScopeFrame frame, ScopeTrace trace, Color color)
        {
            var v = trace.Values;
            double divH = plot.Height / 8.0;
            double X(int i) => plot.Left + frame.DivX(i) * plot.Width / 10.0;
            double Y(double value) => plot.Top + plot.Height / 2 - trace.DivOf(value) * divH;

            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                bool started = false;
                int column = int.MinValue;
                double colMin = 0, colMax = 0, colFirst = 0, colLast = 0;
                void Flush()
                {
                    if (column == int.MinValue) return;
                    double x = column + 0.5;
                    var pf = new Point(x, Y(colFirst));
                    if (!started)
                    {
                        s.BeginFigure(pf, false, false);
                        started = true;
                    }
                    else
                    {
                        s.LineTo(pf, true, true);
                    }

                    if (colMax != colMin)
                    {
                        s.LineTo(new Point(x, Y(colMax)), true, true);
                        s.LineTo(new Point(x, Y(colMin)), true, true);
                    }

                    s.LineTo(new Point(x, Y(colLast)), true, true);
                }

                for (int i = 0; i < v.Length; i++)
                {
                    double value = v[i];
                    if (double.IsNaN(value))
                    {
                        Flush();
                        column = int.MinValue;
                        started = false;
                        continue;
                    }

                    int col = (int)Math.Floor(X(i));
                    if (col != column)
                    {
                        Flush();
                        column = col;
                        colMin = colMax = colFirst = colLast = value;
                    }
                    else
                    {
                        colMin = Math.Min(colMin, value);
                        colMax = Math.Max(colMax, value);
                        colLast = value;
                    }
                }

                Flush();
            }

            g.Freeze();
            // A soft halo under the trace imitates the phosphor/LCD glow of a real screen.
            var halo = new Pen(SymbolRenderer.Brush(Color.FromArgb(0x38, color.R, color.G, color.B)), 4.0) { LineJoin = PenLineJoin.Round };
            halo.Freeze();
            dc.DrawGeometry(null, halo, g);
            var pen = new Pen(SymbolRenderer.Brush(color), 1.4) { LineJoin = PenLineJoin.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        private void DrawXY(DrawingContext dc, Rect plot, ScopeFrame frame)
        {
            var a = frame.TraceOf(Scope.Channels[0]);
            var b = frame.TraceOf(Scope.Channels[1]);
            if (a == null || b == null)
            {
                var hint = Label("XY modu için CH1 (X) ve CH2 (Y) bağlı olmalı", Muted, 11);
                dc.DrawText(hint, new Point(plot.Left + plot.Width / 2 - hint.Width / 2, plot.Top + plot.Height / 2));
                return;
            }

            double divW = plot.Width / 10.0, divH = plot.Height / 8.0;
            var brush = SymbolRenderer.Brush(Color.FromArgb(0xB0, ChannelColors[0].R, ChannelColors[0].G, ChannelColors[0].B));
            int step = Math.Max(1, a.Values.Length / 4000);
            for (int i = 0; i < a.Values.Length; i += step)
            {
                double x = a.Values[i], y = b.Values[i];
                if (double.IsNaN(x) || double.IsNaN(y)) continue;
                var p = new Point(plot.Left + plot.Width / 2 + a.DivOf(x) * divW, plot.Top + plot.Height / 2 - b.DivOf(y) * divH);
                dc.DrawRectangle(brush, null, new Rect(p.X - 0.8, p.Y - 0.8, 1.6, 1.6));
            }

            var label = Label("XY  X = CH1   Y = CH2", Text, 10.5);
            dc.DrawText(label, new Point(plot.Left + 6, plot.Top + 4));
        }

        private ScopeTrace? MeasureTrace(ScopeFrame? frame)
        {
            if (frame == null) return null;
            if (MeasureChannel == 4) return frame.Math;
            return frame.TraceOf(Scope.Channels[Math.Max(0, Math.Min(3, MeasureChannel))]) ?? frame.Traces.FirstOrDefault();
        }

        private void DrawCursors(DrawingContext dc, Rect plot, ScopeFrame? frame)
        {
            if (CursorMode == ScopeCursorMode.Off) return;
            var pen = Pen(CursorColor, 1, true);
            double divW = plot.Width / 10.0, divH = plot.Height / 8.0;
            var trace = MeasureTrace(frame);
            string unit = trace?.Unit ?? "V";
            double scale = trace?.Scale ?? 1.0, pos = trace?.Position ?? 0.0;
            var lines = new List<string>();
            if (CursorMode == ScopeCursorMode.Time)
            {
                double x1 = plot.Left + Cursors[0] * divW, x2 = plot.Left + Cursors[1] * divW;
                dc.DrawLine(pen, new Point(x1, plot.Top), new Point(x1, plot.Bottom));
                dc.DrawLine(pen, new Point(x2, plot.Top), new Point(x2, plot.Bottom));
                dc.DrawText(Label("T1", CursorColor, 10), new Point(x1 + 3, plot.Bottom - 16));
                dc.DrawText(Label("T2", CursorColor, 10), new Point(x2 + 3, plot.Bottom - 16));
                double dt = (Cursors[1] - Cursors[0]) * Scope.SecondsPerDiv;
                lines.Add($"ΔT {SiValue.Format(dt, "s", 4)}");
                if (Math.Abs(dt) > 0) lines.Add($"1/ΔT {SiValue.Format(1.0 / Math.Abs(dt), "Hz", 4)}");
                if (frame != null && trace != null)
                {
                    double v1 = Oscilloscope.ValueAt(frame, trace, frame.StartTime + Cursors[0] * Scope.SecondsPerDiv);
                    double v2 = Oscilloscope.ValueAt(frame, trace, frame.StartTime + Cursors[1] * Scope.SecondsPerDiv);
                    lines.Add($"T1: {SiValue.Format(v1, unit, 3)}  T2: {SiValue.Format(v2, unit, 3)}");
                }
            }
            else
            {
                double y1 = plot.Top + plot.Height / 2 - Cursors[2] * divH, y2 = plot.Top + plot.Height / 2 - Cursors[3] * divH;
                dc.DrawLine(pen, new Point(plot.Left, y1), new Point(plot.Right, y1));
                dc.DrawLine(pen, new Point(plot.Left, y2), new Point(plot.Right, y2));
                dc.DrawText(Label("V1", CursorColor, 10), new Point(plot.Left + 4, y1 - 15));
                dc.DrawText(Label("V2", CursorColor, 10), new Point(plot.Left + 4, y2 - 15));
                double v1 = (Cursors[2] - pos) * scale, v2 = (Cursors[3] - pos) * scale;
                lines.Add($"V1 {SiValue.Format(v1, unit, 3)}  V2 {SiValue.Format(v2, unit, 3)}");
                lines.Add($"ΔV {SiValue.Format(v1 - v2, unit, 4)}");
            }

            var ft = Label(string.Join("\n", lines), CursorColor, 10.5);
            var box = new Rect(plot.Right - ft.Width - 16, plot.Top + 6, ft.Width + 10, ft.Height + 6);
            dc.DrawRoundedRectangle(SymbolRenderer.Brush(Color.FromArgb(0xD8, 0x0A, 0x0B, 0x0C)), Pen(GridAxis, 1), box, 3, 3);
            dc.DrawText(ft, new Point(box.Left + 5, box.Top + 3));
        }

        private void DrawMeasurements(DrawingContext dc, Rect plot, ScopeFrame? frame)
        {
            var trace = MeasureTrace(frame);
            if (trace == null) return;
            var m = trace.Measurements;
            string u = trace.Unit;
            string F(double v, string unit, int d = 3) => double.IsNaN(v) || double.IsInfinity(v) ? "?" : SiValue.Format(v, unit, d);
            var items = new List<string>
            {
                $"Vpp {F(m.PeakToPeak, u)}", $"Vrms {F(m.Rms, u)}", $"Ort {F(m.Mean, u)}", $"Maks {F(m.Max, u)}", $"Min {F(m.Min, u)}",
                $"f {F(m.Frequency, "Hz", 4)}", $"T {F(m.Period, "s", 4)}",
                $"Doluluk {(double.IsNaN(m.Duty) ? "?" : Shell.Lang.Percent(m.Duty * 100, "0.0"))}",   // [lang] "%46,9" / "46.9%"
                $"Yükselme {F(m.RiseTime, "s")}", $"Düşme {F(m.FallTime, "s")}"
            };
            if (u != "V")
            {
                // [scope] a current's readings are App / Arms; a power, a charge or a slew rate (the math) just pp / RMS
                string pp = u == "A" ? "App" : "pp", rms = u == "A" ? "Arms" : "RMS";
                for (int i = 0; i < 3; i++) items[i] = items[i].Replace("Vpp", pp).Replace("Vrms", rms);
            }

            var color = MeasureChannel == 4 ? MathColor : ColorOf(trace.Channel?.Index ?? 0);
            // [lang] Each reading in the language in use before they are joined into one line (the line is no text of the pack).
            string head = (trace.Channel != null ? trace.Channel.Name : trace.Name) + (trace.Clipped ? Shell.Lang.T(" ⚠ kırpıldı") : string.Empty);
            var ft = Label(head + "   " + string.Join("   ", items.Select(Shell.Lang.T)), color, 10.5);
            double y = plot.Bottom + 27;
            if (ft.Width > plot.Width)
            {
                ft.MaxTextWidth = plot.Width;
                ft.Trimming = TextTrimming.CharacterEllipsis;
                ft.MaxLineCount = 1;
            }

            dc.DrawText(ft, new Point(plot.Left, y));
        }

        // ------------------------------------------------------------------ cursor dragging

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();
            if (CursorMode == ScopeCursorMode.Off || e.ChangedButton != MouseButton.Left) return;
            var p = e.GetPosition(this);
            _dragCursor = NearestCursor(p);
            if (_dragCursor >= 0)
            {
                CaptureMouse();
                MoveCursor(p);
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_dragCursor >= 0)
            {
                MoveCursor(p);
                return;
            }

            Cursor = CursorMode != ScopeCursorMode.Off && NearestCursor(p) >= 0
                ? (CursorMode == ScopeCursorMode.Time ? Cursors_.SizeWE : Cursors_.SizeNS)
                : Cursors_.Arrow;
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            if (_dragCursor >= 0)
            {
                _dragCursor = -1;
                ReleaseMouseCapture();
            }
        }

        private int NearestCursor(Point p)
        {
            if (!_plot.Contains(p)) return -1;
            double divW = _plot.Width / 10.0, divH = _plot.Height / 8.0;
            if (CursorMode == ScopeCursorMode.Time)
            {
                double d0 = Math.Abs(p.X - (_plot.Left + Cursors[0] * divW)), d1 = Math.Abs(p.X - (_plot.Left + Cursors[1] * divW));
                return Math.Min(d0, d1) < 10 ? (d0 <= d1 ? 0 : 1) : (d0 <= d1 ? 0 : 1);
            }

            double cy = _plot.Top + _plot.Height / 2;
            double e0 = Math.Abs(p.Y - (cy - Cursors[2] * divH)), e1 = Math.Abs(p.Y - (cy - Cursors[3] * divH));
            return e0 <= e1 ? 2 : 3;
        }

        private void MoveCursor(Point p)
        {
            double divW = _plot.Width / 10.0, divH = _plot.Height / 8.0;
            if (_dragCursor <= 1) Cursors[_dragCursor] = Math.Max(0, Math.Min(10, (p.X - _plot.Left) / divW));
            else Cursors[_dragCursor] = Math.Max(-4, Math.Min(4, (_plot.Top + _plot.Height / 2 - p.Y) / divH));
            InvalidateVisual();
        }

        private static class Cursors_
        {
            public static Cursor SizeWE => System.Windows.Input.Cursors.SizeWE;
            public static Cursor SizeNS => System.Windows.Input.Cursors.SizeNS;
            public static Cursor Arrow => System.Windows.Input.Cursors.Arrow;
        }
    }
}
