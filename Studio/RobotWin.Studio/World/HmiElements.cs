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
using RobotTwin.CoreSim.Industrial;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.World
{
    /// <summary>[hmi-designer] What an element reads and writes: the running world's PLCs, or nothing while it is designed.</summary>
    internal sealed class HmiContext
    {
        /// <summary>(plc, variable) → value; NaN when the world is stopped or the variable is unknown.</summary>
        public Func<string, string, double> Read { get; init; } = (_, _) => double.NaN;

        public Action<string, string, double> Write { get; init; } = (_, _, _) => { };

        /// <summary>Simulated time (s), for trends and alarms.</summary>
        public Func<double> Time { get; init; } = () => 0;

        /// <summary>Operated by the user (the running panel), or laid out (the designer: elements take no input).</summary>
        public bool Live { get; init; }

        public HmiAlarmMonitor Alarms { get; init; } = new HmiAlarmMonitor();

        /// <summary>A PLC's display name by its object id.</summary>
        public Func<string, string> PlcName { get; init; } = id => id;

        /// <summary>[hmi-screens] Shows another screen of the panel (a screen button pressed).</summary>
        public Action<string> ShowScreen { get; init; } = _ => { };
    }

    /// <summary>
    /// [hmi-designer] The operator panel's elements as WPF visuals, the same in the designer and on the running panel: a
    /// flat card with its label and the element (lamp, button, switch, number, set point, bar, dial, trend, motor
    /// faceplate, alarm list, text). Each adds what refreshes it to <c>refresh</c>.
    /// </summary>
    internal static class HmiElements
    {
        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private static string Fmt(double v, string unit) =>
            double.IsNaN(v) ? "—" : v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : Math.Abs(v) < 10 ? "0.##" : "0.#", CultureInfo.CurrentCulture) + (unit.Length > 0 ? " " + unit : string.Empty);

        public static FrameworkElement Build(HmiWidget w, HmiContext ctx, List<Action> refresh)
        {
            double Read(string variable) => variable.Length == 0 ? double.NaN : ctx.Read(w.Plc, variable);
            void Write(string variable, double value)
            {
                if (ctx.Live && variable.Length > 0) ctx.Write(w.Plc, variable, value);
            }

            string title = w.Label.Length > 0 ? w.Label : w.Variable.Length > 0 ? w.Variable : Lang.T(KindName(w.Kind));
            var dock = new DockPanel { LastChildFill = true };
            if (w.Kind != HmiWidget.Text)
            {
                var head = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 4) };
                DockPanel.SetDock(head, Dock.Top);
                dock.Children.Add(head);
            }

            UIElement body;
            switch (w.Kind)
            {
                case HmiWidget.Lamp:
                {
                    var lamp = new Ellipse { Width = 34, Height = 34, Fill = Res("B.Border"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                    refresh.Add(() => lamp.Fill = Read(w.Variable) is var v && !double.IsNaN(v) && v != 0 ? Res("B.Success") : Res("B.Border"));
                    body = lamp;
                    break;
                }

                case HmiWidget.Button:
                {
                    var button = new Button { Content = w.Label.Length > 0 ? w.Label : Lang.T("Bas"), Padding = new Thickness(10, 6, 10, 6) };
                    button.PreviewMouseLeftButtonDown += (_, _) => Write(w.Variable, 1);
                    button.PreviewMouseLeftButtonUp += (_, _) => Write(w.Variable, 0);
                    button.MouseLeave += (_, e) =>
                    {
                        if (e.LeftButton == MouseButtonState.Pressed) Write(w.Variable, 0);
                    };
                    body = button;
                    break;
                }

                case HmiWidget.Switch:
                {
                    var toggle = new CheckBox { Content = Lang.T("Açık"), Style = (Style)Application.Current.FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
                    bool updating = false;
                    toggle.Click += (_, _) =>
                    {
                        if (!updating) Write(w.Variable, toggle.IsChecked == true ? 1 : 0);
                    };
                    refresh.Add(() =>
                    {
                        updating = true;
                        double v = Read(w.Variable);
                        toggle.IsChecked = !double.IsNaN(v) && v != 0;
                        updating = false;
                    });
                    body = toggle;
                    break;
                }

                case HmiWidget.Number:
                {
                    var number = new TextBlock { FontSize = TypeScale.Headline, FontFamily = (FontFamily)Application.Current.FindResource("F.Mono"), VerticalAlignment = VerticalAlignment.Center, Text = "—" };
                    refresh.Add(() => number.Text = Fmt(Read(w.Variable), w.Unit));
                    body = number;
                    break;
                }

                case HmiWidget.SetPoint:
                {
                    var box = new TextBox { ToolTip = Lang.T("Değeri yazıp Enter'a basın"), VerticalAlignment = VerticalAlignment.Center };
                    box.KeyDown += (_, e) =>
                    {
                        if (e.Key != Key.Enter) return;
                        if (double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) Write(w.Variable, Math.Max(w.Min, Math.Min(w.Max, v)));
                    };
                    refresh.Add(() =>
                    {
                        if (box.IsKeyboardFocusWithin) return;
                        double v = Read(w.Variable);
                        box.Text = double.IsNaN(v) ? string.Empty : v.ToString("0.###", CultureInfo.InvariantCulture);
                    });
                    body = box;
                    break;
                }

                case HmiWidget.Bar:
                {
                    var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var bar = new ProgressBar { Minimum = w.Min, Maximum = w.Max > w.Min ? w.Max : w.Min + 1, Height = 14 };
                    var value = new TextBlock { FontSize = TypeScale.Caption, Margin = new Thickness(0, 4, 0, 0), Text = "—" };
                    stack.Children.Add(bar);
                    stack.Children.Add(value);
                    refresh.Add(() =>
                    {
                        double v = Read(w.Variable);
                        bar.Value = double.IsNaN(v) ? bar.Minimum : Math.Max(bar.Minimum, Math.Min(bar.Maximum, v));
                        value.Text = Fmt(v, w.Unit);
                    });
                    body = stack;
                    break;
                }

                case HmiWidget.Gauge:
                {
                    var dial = new HmiDial { Min = w.Min, Max = w.Max, Unit = w.Unit };
                    refresh.Add(() => dial.Value = Read(w.Variable));
                    body = dial;
                    break;
                }

                case HmiWidget.Trend:
                {
                    var trend = new HmiTrend { Min = w.Min, Max = w.Max, Seconds = Math.Max(1, w.Seconds), Names = w.Series().ToList() };
                    refresh.Add(() => trend.Add(ctx.Time(), trend.Names.Select(Read).ToArray()));
                    body = trend;
                    break;
                }

                case HmiWidget.Motor:
                    body = Motor(w, Read, Write, refresh);
                    break;
                case HmiWidget.Navigate:
                {
                    // [hmi-screens] a button to another screen: its label, or the screen's name
                    var go = new Button { Content = w.Label.Length > 0 ? w.Label : w.Target.Length > 0 ? "→ " + Lang.T(w.Target) : Lang.T("Ekran seçin"), Padding = new Thickness(10, 6, 10, 6) };
                    go.Click += (_, _) =>
                    {
                        if (ctx.Live && w.Target.Length > 0) ctx.ShowScreen(w.Target);
                    };
                    dock.Children.Clear();   // the button is the element: no title above it
                    body = go;
                    break;
                }
                case HmiWidget.Alarms:
                    body = AlarmList(ctx, refresh);
                    break;
                default:
                    body = new TextBlock { Text = title, FontSize = TypeScale.Title, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                    break;
            }

            dock.Children.Add(body);
            var card = new Border
            {
                Width = w.Width,
                Height = w.Height,
                Padding = new Thickness(10, 8, 10, 8),
                Background = w.Kind is HmiWidget.Text or HmiWidget.Navigate ? Brushes.Transparent : Res("B.Raised"),
                BorderBrush = w.Kind is HmiWidget.Text or HmiWidget.Navigate ? Brushes.Transparent : Res("B.Border"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = dock,
                ToolTip = HmiWidget.Bound(w.Kind) ? $"{ctx.PlcName(w.Plc)} · {w.Variable}" : null,
            };
            if (!ctx.Live) card.IsHitTestVisible = false;   // the designer moves it; it does nothing itself
            return card;
        }

        /// <summary>A motor's faceplate: running and fault lamps, Start and Stop (each held TRUE while pressed).</summary>
        private static UIElement Motor(HmiWidget w, Func<string, double> read, Action<string, double> write, List<Action> refresh)
        {
            var stack = new StackPanel();
            var state = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var run = new Ellipse { Width = 18, Height = 18, Fill = Res("B.Border"), Margin = new Thickness(0, 0, 6, 0) };
            var runText = new TextBlock { Text = "—", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var fault = new Ellipse { Width = 18, Height = 18, Fill = Res("B.Border"), Margin = new Thickness(0, 0, 6, 0) };
            var faultText = new TextBlock { Text = Lang.T("Arıza"), VerticalAlignment = VerticalAlignment.Center, Foreground = Res("B.TextSecondary") };
            state.Children.Add(run);
            state.Children.Add(runText);
            state.Children.Add(fault);
            state.Children.Add(faultText);
            stack.Children.Add(state);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            Button Push(string text, string role)
            {
                var b = new Button { Content = Lang.T(text), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 6, 0) };
                b.PreviewMouseLeftButtonDown += (_, _) => write(w.Role(role), 1);
                b.PreviewMouseLeftButtonUp += (_, _) => write(w.Role(role), 0);
                b.MouseLeave += (_, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed) write(w.Role(role), 0);
                };
                return b;
            }

            buttons.Children.Add(Push("Başlat", HmiWidget.RoleStart));
            buttons.Children.Add(Push("Durdur", HmiWidget.RoleStop));
            stack.Children.Add(buttons);
            refresh.Add(() =>
            {
                double r = read(w.Role(HmiWidget.RoleRunning)), f = read(w.Role(HmiWidget.RoleFault));
                bool running = !double.IsNaN(r) && r != 0, faulted = !double.IsNaN(f) && f != 0;
                run.Fill = running ? Res("B.Success") : Res("B.Border");
                runText.Text = double.IsNaN(r) ? "—" : Lang.T(running ? "Çalışıyor" : "Durdu");
                fault.Fill = faulted ? Res("B.Danger") : Res("B.Border");
            });
            return stack;
        }

        /// <summary>The alarms still active or not yet acknowledged, most severe first; a click acknowledges one.</summary>
        private static UIElement AlarmList(HmiContext ctx, List<Action> refresh)
        {
            var dock = new DockPanel();
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var ackAll = new Button { Content = Lang.T("Onayla"), ToolTip = Lang.T("Tümünü onayla"), Style = (Style)Application.Current.FindResource("Btn.Ghost"), Padding = new Thickness(6, 1, 6, 1), MinHeight = 22 };
            ackAll.Click += (_, _) => ctx.Alarms.AcknowledgeAll(ctx.Time());
            DockPanel.SetDock(ackAll, Dock.Right);
            bar.Children.Add(ackAll);
            var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Res("B.TextSecondary"), FontSize = TypeScale.Caption, TextTrimming = TextTrimming.CharacterEllipsis };
            bar.Children.Add(count);
            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
            var list = new StackPanel();
            dock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            int shown = -1;
            refresh.Add(() =>
            {
                var items = ctx.Alarms.Shown.Take(50).ToList();
                count.Text = Lang.T($"{ctx.Alarms.ActiveCount} etkin · {ctx.Alarms.UnacknowledgedCount} onaysız");
                int stamp = items.Sum(e => e.GetHashCode() ^ (e.Active ? 1 : 0) ^ (e.Acked ? 2 : 0));
                if (stamp == shown && list.Children.Count == items.Count) return;
                shown = stamp;
                list.Children.Clear();
                if (items.Count == 0) list.Children.Add(new TextBlock { Text = Lang.T("Alarm yok"), Foreground = Res("B.TextSecondary") });
                foreach (var e in items)
                {
                    var ev = e;
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = Lang.T("Tıklayın: onayla") };
                    var level = new Border
                    {
                        Background = ev.Alarm.Severity <= 1 ? Res("B.Danger") : ev.Alarm.Severity <= 3 ? Res("B.Warning") : Res("B.Info"),
                        CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 6, 0),
                        Child = new TextBlock { Text = ev.Alarm.Level, Foreground = Res("B.OnPrimary"), FontSize = TypeScale.Caption, FontWeight = FontWeights.SemiBold },
                    };
                    DockPanel.SetDock(level, Dock.Left);
                    row.Children.Add(level);
                    var time = new TextBlock { Text = ev.Raised.ToString("0.0", CultureInfo.CurrentCulture) + " s", FontSize = TypeScale.Caption, Foreground = Res("B.TextSecondary"), Margin = new Thickness(6, 0, 0, 0) };
                    DockPanel.SetDock(time, Dock.Right);
                    row.Children.Add(time);
                    row.Children.Add(new TextBlock
                    {
                        Text = Lang.T(ev.Alarm.Describe()), TextTrimming = TextTrimming.CharacterEllipsis,
                        FontWeight = ev.Acked ? FontWeights.Normal : FontWeights.SemiBold,
                        Foreground = ev.Active ? Res("B.Text") : Res("B.TextSecondary"),
                    });
                    row.MouseLeftButtonUp += (_, _) => ctx.Alarms.Acknowledge(ev, ctx.Time());
                    list.Children.Add(row);
                }
            });
            return dock;
        }

        /// <summary>The palette's name of a kind.</summary>
        public static string KindName(string kind) => kind switch
        {
            HmiWidget.Lamp => "Lamba",
            HmiWidget.Button => "Düğme",
            HmiWidget.Switch => "Anahtar",
            HmiWidget.Number => "Sayı",
            HmiWidget.SetPoint => "Ayar değeri",
            HmiWidget.Bar => "Çubuk",
            HmiWidget.Gauge => "Kadran",
            HmiWidget.Trend => "Trend",
            HmiWidget.Motor => "Motor",
            HmiWidget.Alarms => "Alarm listesi",
            HmiWidget.Navigate => "Ekrana git",
            _ => "Yazı",
        };

        /// <summary>A PLC's variables (its program compiled now), for choosing what an element shows.</summary>
        public static List<string> PlcVariables(WorldObject? plc)
        {
            if (plc == null) return new List<string>();
            StProgram? program;
            if (plc.Parameters.TryGetValue("language", out var language) && language.Trim() == CellParts.PlcLadder)
                program = LdProgram.Load(plc.Parameters.TryGetValue("ladder", out var json) ? json : string.Empty).Compile(out _);
            else
                program = StProgram.Compile(plc.Parameters.TryGetValue("program", out var text) && !string.IsNullOrWhiteSpace(text) ? text : CellParts.PlcExample, out _);
            return program?.Watch.Select(v => v.Name).Where(n => !n.StartsWith("_", StringComparison.Ordinal)).ToList() ?? new List<string>();
        }
    }

    /// <summary>[hmi-designer] A dial: a 270° track, the value's arc over it, the value in the middle, min and max at the ends.</summary>
    internal sealed class HmiDial : FrameworkElement
    {
        private double _value = double.NaN;

        public double Min { get; set; }
        public double Max { get; set; } = 100;
        public string Unit { get; set; } = string.Empty;

        public double Value
        {
            get => _value;
            set
            {
                if (value.Equals(_value)) return;
                _value = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 20) return;
            var c = new Point(ActualWidth / 2, ActualHeight / 2 + size * 0.06);
            double r = size * 0.42, thick = Math.Max(6, size * 0.08);
            var track = (Brush)Application.Current.FindResource("B.Border");
            var fill = (Brush)Application.Current.FindResource("B.Accent");
            var text = (Brush)Application.Current.FindResource("B.Text");
            var quiet = (Brush)Application.Current.FindResource("B.TextSecondary");
            const double start = 135, sweep = 270;
            dc.DrawGeometry(null, new Pen(track, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Arc(c, r, start, sweep));
            double span = Max > Min ? Max - Min : 1;
            if (!double.IsNaN(_value))
            {
                double f = Math.Max(0, Math.Min(1, (_value - Min) / span));
                if (f > 0.002) dc.DrawGeometry(null, new Pen(fill, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Arc(c, r, start, sweep * f));
            }

            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface((FontFamily)Application.Current.FindResource("F.Mono"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            string s = double.IsNaN(_value) ? "—" : _value.ToString(Math.Abs(_value) < 10 ? "0.##" : "0.#", CultureInfo.CurrentCulture);
            var big = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, Math.Max(10, size * 0.16), text, dpi);
            dc.DrawText(big, new Point(c.X - big.Width / 2, c.Y - big.Height / 2));
            if (Unit.Length > 0)
            {
                var u = new FormattedText(Unit, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(9, size * 0.075), quiet, dpi);
                dc.DrawText(u, new Point(c.X - u.Width / 2, c.Y + big.Height / 2));
            }

            foreach (var (v, deg) in new[] { (Min, start), (Max, start + sweep) })
            {
                var t = new FormattedText(v.ToString("0.##", CultureInfo.CurrentCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(9, size * 0.065), quiet, dpi);
                double a = deg * Math.PI / 180;
                var p = new Point(c.X + Math.Cos(a) * (r + thick * 0.2), c.Y + Math.Sin(a) * (r + thick * 0.2) + thick * 0.6);
                dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y));
            }
        }

        private static Geometry Arc(Point c, double r, double startDeg, double sweepDeg)
        {
            double a0 = startDeg * Math.PI / 180, a1 = (startDeg + sweepDeg) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
                ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
            }

            g.Freeze();
            return g;
        }
    }

    /// <summary>[hmi-designer] A trend: up to four variables over the last seconds, on the element's range, with their names.</summary>
    internal sealed class HmiTrend : FrameworkElement
    {
        private readonly List<(double T, double[] V)> _samples = new List<(double, double[])>();

        public double Min { get; set; }
        public double Max { get; set; } = 100;
        public double Seconds { get; set; } = 30;
        public List<string> Names { get; set; } = new List<string>();

        /// <summary>[plc-workspace] The range follows what is shown (the watch chart) instead of <see cref="Min"/> … <see cref="Max"/>.</summary>
        public bool AutoRange { get; set; }

        private static readonly string[] SeriesBrushes = { "B.Accent", "B.Success", "B.Warning", "B.Info" };

        public void Add(double t, double[] values)
        {
            if (_samples.Count > 0 && t < _samples[^1].T) _samples.Clear();   // the world started again
            if (_samples.Count > 0 && t - _samples[^1].T < 1e-6) return;
            _samples.Add((t, values));
            while (_samples.Count > 0 && _samples[0].T < t - Seconds * 1.1) _samples.RemoveAt(0);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 20 || h < 20) return;
            var border = new Pen((Brush)Application.Current.FindResource("B.Border"), 1);
            var quiet = (Brush)Application.Current.FindResource("B.TextSecondary");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface("Segoe UI");
            const double legend = 16;
            var plot = new Rect(0, 0, w, Math.Max(10, h - legend));
            dc.DrawRectangle(null, border, plot);
            for (int i = 1; i < 4; i++) dc.DrawLine(border, new Point(plot.Left, plot.Top + plot.Height * i / 4), new Point(plot.Right, plot.Top + plot.Height * i / 4));
            double now = _samples.Count > 0 ? _samples[^1].T : 0, t0 = now - Seconds;
            double lo = Min, hi = Max;
            if (AutoRange)
            {
                var shown = _samples.Where(s => s.T >= t0).SelectMany(s => s.V).Where(v => !double.IsNaN(v)).ToList();
                lo = shown.Count > 0 ? shown.Min() : 0;
                hi = shown.Count > 0 ? shown.Max() : 1;
                double pad = Math.Max(1e-9, (hi - lo) * 0.1);
                if (hi - lo < 1e-9) pad = Math.Max(1, Math.Abs(hi) * 0.1);
                lo -= pad;
                hi += pad;
            }

            double span = hi > lo ? hi - lo : 1, Min0 = lo;
            for (int s = 0; s < Names.Count && s < SeriesBrushes.Length; s++)
            {
                var brush = (Brush)Application.Current.FindResource(SeriesBrushes[s]);
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    bool open = false;
                    foreach (var (t, v) in _samples)
                    {
                        if (t < t0 || s >= v.Length || double.IsNaN(v[s]))
                        {
                            open = false;
                            continue;
                        }

                        var p = new Point(plot.Left + (t - t0) / Seconds * plot.Width, plot.Bottom - Math.Max(0, Math.Min(1, (v[s] - Min0) / span)) * plot.Height);
                        if (!open) ctx.BeginFigure(p, false, false);
                        else ctx.LineTo(p, true, false);
                        open = true;
                    }
                }

                g.Freeze();
                dc.DrawGeometry(null, new Pen(brush, 1.6), g);
                var label = new FormattedText(Names[s], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 11, brush, dpi);
                dc.DrawText(label, new Point(s * Math.Max(60, w / 4), plot.Bottom + 2));
            }

            var max = new FormattedText(hi.ToString("0.##", CultureInfo.CurrentCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 10, quiet, dpi);
            dc.DrawText(max, new Point(plot.Right - max.Width - 3, plot.Top + 1));
            var min = new FormattedText(lo.ToString("0.##", CultureInfo.CurrentCulture), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 10, quiet, dpi);
            dc.DrawText(min, new Point(plot.Right - min.Width - 3, plot.Bottom - min.Height - 1));
        }
    }
}
