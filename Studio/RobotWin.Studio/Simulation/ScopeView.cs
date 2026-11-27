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

        /// <summary>[decode] The decode lane on screen (for clicks), and the field picked in it.</summary>
        private Rect _decodeLane = Rect.Empty;
        private (double Start, double End)? _pickedField;

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
            // [timebase] the instant the logic analyzer's pointer is on, marked here
            Action redraw = InvalidateVisual;
            Loaded += (_, _) => TimeCursor.Changed += redraw;
            Unloaded += (_, _) => TimeCursor.Changed -= redraw;
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

        /// <summary>[mask] The band of ±<paramref name="divisions"/> about a kept trace, as a flat translucent fill.</summary>
        private static void DrawMaskBand(DrawingContext dc, Rect plot, ScopeFrame frame, ScopeTrace trace, double divisions, Color color)
        {
            var v = trace.Values;
            if (v.Length < 2) return;
            double divH = plot.Height / 8.0;
            int step = Math.Max(1, v.Length / Math.Max(1, (int)plot.Width));
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                bool started = false;
                for (int i = 0; i < v.Length; i += step)
                {
                    var p = new Point(plot.Left + frame.DivX(i) * plot.Width / 10.0, plot.Top + plot.Height / 2 - (trace.DivOf(v[i]) + divisions) * divH);
                    if (!started)
                    {
                        s.BeginFigure(p, true, true);
                        started = true;
                    }
                    else s.LineTo(p, false, false);
                }

                for (int i = (v.Length - 1) / step * step; i >= 0; i -= step)
                    s.LineTo(new Point(plot.Left + frame.DivX(i) * plot.Width / 10.0, plot.Top + plot.Height / 2 - (trace.DivOf(v[i]) - divisions) * divH), false, false);
            }

            g.Freeze();
            dc.DrawGeometry(SymbolRenderer.Brush(Color.FromArgb(0x26, color.R, color.G, color.B)), null, g);
        }

        /// <summary>[ref] A channel's colour for its kept trace: the same hue, a third as strong.</summary>
        private static Color Faint(Color c) => Color.FromArgb(0x58, c.R, c.G, c.B);

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
                    // [ref] the kept screen under the live one, in its channels' colours, faint
                    if (Scope.Reference is { } kept)
                    {
                        // [mask] the band a live trace must stay in, round each kept one
                        if (Scope.MaskDivisions > 0)
                            foreach (var t in kept.Traces)
                                DrawMaskBand(dc, plot, kept, t, Scope.MaskDivisions, ColorOf(t.Channel?.Index ?? 0));
                        if (kept.Math != null) DrawTrace(dc, plot, kept, kept.Math, Faint(MathColor));
                        foreach (var t in kept.Traces) DrawTrace(dc, plot, kept, t, Faint(ColorOf(t.Channel?.Index ?? 0)));
                    }

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
                if (frame != null) DrawDecode(dc, plot, frame);   // [decode]
                if (frame != null) DrawSharedTime(dc, plot, frame);   // [timebase]
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
            // [ref] a kept screen says so, with how far each live trace strays from it (RMS) when they line up
            if (Scope.Reference is { } kept && frame != null)
            {
                var dev = frame.Traces.Select(t => (t, d: Oscilloscope.ReferenceDeviation(kept, frame, t))).Where(x => !double.IsNaN(x.d))
                    .Select(x => $"CH{(x.t.Channel?.Index ?? 0) + 1} Δ{SiValue.Format(x.d, x.t.Unit)}");
                status += "   REF " + string.Join(" ", dev);
                // [mask] pass while every live sample stays in the band, fail with how many left it
                if (Scope.MaskDivisions > 0)
                {
                    int bad = frame.Traces.Sum(t => Math.Max(0, Oscilloscope.MaskViolations(kept, frame, t, Scope.MaskDivisions)));
                    status += bad == 0 ? "   " + Shell.Lang.T("MASKE: GEÇTİ") : "   " + Shell.Lang.T($"MASKE: KALDI ({bad})");
                }
            }
            else if (Scope.Reference != null) status += "   REF";

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

        /// <summary>[decode] The serial decoder's lane along the bottom of the screen: bytes as boxes, START/STOP and ACK as marks, errors red.</summary>
        private void DrawDecode(DrawingContext dc, Rect plot, ScopeFrame frame)
        {
            _decodeLane = Rect.Empty;
            if (Scope.DecodeProtocol.Length == 0) return;
            var fields = Scope.Decode(frame);
            double laneH = 18, y = plot.Bottom - laneH - 3;
            _decodeLane = new Rect(plot.Left, y - 2, plot.Width, laneH + 4);
            dc.DrawRectangle(SymbolRenderer.Brush(Color.FromArgb(0xC8, 0x0A, 0x0B, 0x0C)), null, new Rect(plot.Left, y - 2, plot.Width, laneH + 4));
            double X(double t) => plot.Left + (t - frame.StartTime) / (10.0 * frame.SecondsPerDiv) * plot.Width;
            var label = new FormattedText(Scope.DecodeProtocol, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Mono, 10, SymbolRenderer.Brush(Muted), Dpi);
            dc.DrawText(label, new Point(plot.Left + 3, y - label.Height - 1));
            var dataColor = Color.FromRgb(0x4A, 0x8C, 0xFF);
            foreach (var f in fields)
            {
                double x0 = X(f.Start), x1 = X(f.End);
                if (x1 < plot.Left || x0 > plot.Right) continue;
                var color = f.Error ? StopColor : f.Kind is "start" or "stop" ? RunColor : f.Kind == "address" ? TriggerColor : f.Kind == "ack" ? Muted : dataColor;
                if (f.End - f.Start <= 0 || x1 - x0 < 3)
                {
                    // A mark: START, STOP, ACK.
                    dc.DrawLine(Pen(color, 1.5), new Point(x0, y), new Point(x0, y + laneH));
                    var mark = new FormattedText(f.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Mono, 9.5, SymbolRenderer.Brush(color), Dpi);
                    dc.DrawText(mark, new Point(x0 + 2, y + (laneH - mark.Height) / 2));
                    continue;
                }

                var box = new Rect(Math.Max(plot.Left, x0), y, Math.Max(2, Math.Min(plot.Right, x1) - Math.Max(plot.Left, x0)), laneH);
                bool picked = _pickedField is { } pf && Math.Abs(pf.Start - f.Start) < 1e-12 && Math.Abs(pf.End - f.End) < 1e-12;
                dc.DrawRoundedRectangle(SymbolRenderer.Brush(Color.FromArgb(picked ? (byte)0x99 : (byte)0x55, color.R, color.G, color.B)), Pen(picked ? CursorColor : color, picked ? 1.6 : 1), box, 3, 3);
                var text = new FormattedText(f.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Mono, 10, SymbolRenderer.Brush(Text), Dpi);
                if (text.Width + 4 <= box.Width) dc.DrawText(text, new Point(box.Left + (box.Width - text.Width) / 2, y + (laneH - text.Height) / 2));
            }
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
            if (e.ChangedButton == MouseButton.Left && PickDecoded(e.GetPosition(this)))
            {
                e.Handled = true;
                return;
            }

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

        /// <summary>
        /// [decode] A click on a decoded byte: the time cursors go to its start and end (its duration in the readout), its
        /// wire is highlighted in the circuit, and the status line reads it with its time.
        /// </summary>
        private bool PickDecoded(Point p)
        {
            if (_decodeLane.IsEmpty || !_decodeLane.Contains(p) || Scope.Frame is not { } frame) return false;
            double t = frame.StartTime + (p.X - _plot.Left) / _plot.Width * 10.0 * frame.SecondsPerDiv;
            var field = Scope.Decode(frame).Where(f => f.End > f.Start).OrderBy(f => t < f.Start ? f.Start - t : t > f.End ? t - f.End : 0).FirstOrDefault();
            if (field == null) return false;
            double slack = 4.0 / _plot.Width * 10.0 * frame.SecondsPerDiv;
            if (t < field.Start - slack || t > field.End + slack) return false;
            _pickedField = (field.Start, field.End);
            CursorMode = ScopeCursorMode.Time;
            Cursors[0] = Math.Max(0, Math.Min(10, (field.Start - frame.StartTime) / frame.SecondsPerDiv));
            Cursors[1] = Math.Max(0, Math.Min(10, (field.End - frame.StartTime) / frame.SecondsPerDiv));
            int ch = Math.Max(0, Math.Min(Scope.Channels.Count - 1, Scope.DecodeA));
            string? wire = Scope.Channels[ch].Source is ProbeChannel probe && probe.Kind == ProbeKind.Voltage ? probe.Name : null;
            if (wire != null) Shell.CrossProbe.HighlightNet(this, wire);
            Workspace.Current.Status = $"{Scope.DecodeProtocol} {field.Text} · {SiValue.Format(field.Start, "s")} · {SiValue.Format(field.End - field.Start, "s")}" + (wire != null ? " · " + wire : string.Empty);
            CursorsMoved?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return true;
        }

        /// <summary>The n-th decoded byte on screen picked (a snapshot); laid out first so the lane is where a click would find it.</summary>
        public void PickDecodedForSnapshot(int n)
        {
            UpdateLayout();
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) OnRender(dc);
            if (Scope.Frame is not { } frame || _decodeLane.IsEmpty) return;
            var field = Scope.Decode(frame).Where(f => f.End > f.Start).Skip(n).FirstOrDefault();
            if (field == null) return;
            double x = _plot.Left + ((field.Start + field.End) / 2 - frame.StartTime) / (10.0 * frame.SecondsPerDiv) * _plot.Width;
            PickDecoded(new Point(x, _decodeLane.Top + _decodeLane.Height / 2));
        }

        /// <summary>[decode] The cursors moved by a pick (the panel shows its cursor mode).</summary>
        public event EventHandler? CursorsMoved;

        /// <summary>
        /// [timebase] The instant another instrument's pointer is on (the Code page's timeline, the same session's time),
        /// as a dashed line across the plot; and this window remembered for "Osiloskopla eşle".
        /// </summary>
        private void DrawSharedTime(DrawingContext dc, Rect plot, ScopeFrame frame)
        {
            double span = 10.0 * frame.SecondsPerDiv;
            TimeCursor.ScopeShows(Sim, frame.StartTime, frame.StartTime + span);
            if (TimeCursor.For(Sim, this) is not { } t || span <= 0) return;
            double x = plot.Left + (t - frame.StartTime) / span * plot.Width;
            if (x < plot.Left || x > plot.Right) return;
            var brush = TryFindResource("B.Accent") as Brush ?? Brushes.DodgerBlue;
            var pen = new Pen(brush, 1.2) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var read = Label("t = " + t.ToString("0.000000", CultureInfo.CurrentCulture) + " s", (brush as SolidColorBrush)?.Color ?? Colors.DodgerBlue, 10.5);
            double rx = Math.Min(x + 4, plot.Right - read.Width - 2);
            dc.DrawRectangle(SymbolRenderer.Brush(Back), null, new Rect(rx - 2, plot.Top + 2, read.Width + 4, read.Height));
            dc.DrawText(read, new Point(rx, plot.Top + 2));
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            TimeCursor.Set(Sim, this, null);   // [timebase]
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var p = e.GetPosition(this);
            // [timebase] the instant under the pointer, for the logic analyzer to mark
            TimeCursor.Set(Sim, this, !Scope.XY && Scope.Frame is { } f && _plot.Contains(p) && _plot.Width > 0
                ? f.StartTime + (p.X - _plot.Left) / _plot.Width * 10.0 * f.SecondsPerDiv
                : null);
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
