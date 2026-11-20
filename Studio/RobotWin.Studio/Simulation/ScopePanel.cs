using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Simulation
{
    /// <summary>Front panel of the oscilloscope: run control, horizontal, trigger, vertical (per channel), cursors, math, XY.</summary>
    public sealed class ScopePanel : ScrollViewer
    {
        private readonly ScopeView _view;
        private readonly StackPanel _root = new StackPanel { Margin = new Thickness(12, 8, 10, 10) };
        private readonly List<Action> _refresh = new List<Action>();
        private readonly Button _run;

        public ScopePanel(ScopeView view)
        {
            _view = view;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Content = _root;

            // ---- run control
            var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            _run = Btn("RUN / STOP", () =>
            {
                if (Scope.Running) Scope.Stop();
                else Scope.Run();
                _view.ForceAcquire();
            }, primary: true);
            top.Children.Add(_run);
            top.Children.Add(Btn("TEK", () =>
            {
                Scope.Single();
                _view.InvalidateVisual();
            }, tip: "Tek tetikleme: bir kez yakala ve dur"));
            top.Children.Add(Btn("AUTOSET", () =>
            {
                Scope.Autoset(Sim.InstrumentTime);
                _view.ForceAcquire();
            }, tip: "V/div, zaman/div ve tetik seviyesini sinyale göre ayarla"));
            _root.Children.Add(top);

            // ---- horizontal
            Section("Yatay");
            Stepper("Zaman/div", () => SiValue.Format(Scope.SecondsPerDiv, "s") + "/div", dir =>
            {
                Scope.SecondsPerDiv = Steps125.Next(Scope.SecondsPerDiv, dir, Oscilloscope.MinSecondsPerDiv, Oscilloscope.MaxSecondsPerDiv);
            });
            Stepper("Konum", () => SiValue.Format(Scope.Delay, "s"), dir => Scope.Delay += dir * Scope.SecondsPerDiv, () => Scope.Delay = 0);

            // ---- trigger
            Section("Tetik");
            Chips("Mod", new[] { "Auto", "Normal", "Tek" }, () => (int)Scope.TriggerMode, i =>
            {
                if (i == 2) Scope.Single();
                else
                {
                    Scope.TriggerMode = (TriggerMode)i;
                    Scope.Run();
                }
            });
            Chips("Kaynak", new[] { "1", "2", "3", "4" }, () => Scope.TriggerSource, i => Scope.TriggerSource = i, colored: true);
            Chips("Eğim", new[] { "↗ yükselen", "↘ düşen" }, () => (int)Scope.TriggerSlope, i => Scope.TriggerSlope = (TriggerSlope)i);
            Stepper("Seviye", () => SiValue.Format(Scope.TriggerLevel, TriggerChannel.Unit, 3), dir => Scope.TriggerLevel += dir * 0.2 * TriggerChannel.Scale, () =>
            {
                var t = Scope.Frame?.TraceOf(TriggerChannel);
                if (t != null && !double.IsNaN(t.Measurements.Max)) Scope.TriggerLevel = 0.5 * (t.Measurements.Max + t.Measurements.Min);
            }, "%50");
            Stepper("Holdoff", () => Scope.Holdoff <= 0 ? "kapalı" : SiValue.Format(Scope.Holdoff, "s"), dir =>
            {
                if (Scope.Holdoff <= 0) Scope.Holdoff = dir > 0 ? 1e-6 : 0;
                else if (dir < 0 && Scope.Holdoff <= 1e-6) Scope.Holdoff = 0;
                else Scope.Holdoff = Steps125.Next(Scope.Holdoff, dir, 1e-6, 10);
            });

            // ---- vertical
            Section("Dikey");
            Chips(null, new[] { "CH1", "CH2", "CH3", "CH4" }, () => Sim.SelectedScopeChannel, i => Sim.SelectedScopeChannel = i, colored: true);
            var onRow = Row();
            var on = new CheckBox { Style = Res("Switch"), Content = "Ekranda", Margin = new Thickness(0, 0, 14, 0) };
            on.Click += (_, _) =>
            {
                Selected.Enabled = on.IsChecked == true;
                Changed();
            };
            onRow.Children.Add(on);
            _refresh.Add(() => on.IsChecked = Selected.Enabled);
            _root.Children.Add(onRow);
            Chips("Prob", new[] { "1×", "10×" }, () => Selected.ProbeAttenuation >= 5 ? 1 : 0, i =>
            {
                double old = Selected.ProbeAttenuation;
                Selected.ProbeAttenuation = i == 1 ? 10 : 1;
                Selected.Scale *= Selected.ProbeAttenuation / old;
                Sim.BindScopeLoads();
            });
            Stepper("V/div", () => SiValue.Format(Selected.Scale, Selected.Unit) + "/div", dir =>
                Selected.Scale = Steps125.Next(Selected.Scale, dir, Selected.MinScale, Selected.MaxScale));
            Stepper("Konum", () => Selected.Position.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " div",
                dir => Selected.Position = Math.Max(-8, Math.Min(8, Selected.Position + 0.5 * dir)), () => Selected.Position = 0);
            Chips("Kuplaj", new[] { "DC", "AC", "GND" }, () => (int)Selected.Coupling, i => Selected.Coupling = (ScopeCoupling)i);
            var opts = Row();
            var bw = new CheckBox { Style = Res("Switch"), Content = "BW 20 MHz", Margin = new Thickness(0, 0, 14, 0), ToolTip = "20 MHz bant genişliği sınırı (gürültüyü azaltır, hızlı kenarları yavaşlatır)" };
            bw.Click += (_, _) =>
            {
                Selected.BandwidthLimit = bw.IsChecked == true;
                Changed();
            };
            var inv = new CheckBox { Style = Res("Switch"), Content = "Ters" };
            inv.Click += (_, _) =>
            {
                Selected.Invert = inv.IsChecked == true;
                Changed();
            };
            opts.Children.Add(bw);
            opts.Children.Add(inv);
            _refresh.Add(() =>
            {
                bw.IsChecked = Selected.BandwidthLimit;
                inv.IsChecked = Selected.Invert;
            });
            _root.Children.Add(opts);
            var probeRow = Row();
            var probeLabel = new TextBlock { Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180 };
            _refresh.Add(() => probeLabel.Text = Sim.ScopeProbes[Sim.SelectedScopeChannel] is { } p ? "Prob: " + p.Name : "Prob bağlı değil (kabloya tıklayın)");
            var detach = Btn("Çıkar", () => Sim.DetachScopeChannel(Sim.SelectedScopeChannel), ghost: true);
            detach.Margin = new Thickness(0, 0, 8, 0);
            probeRow.Children.Add(detach);
            probeRow.Children.Add(probeLabel);
            _root.Children.Add(probeRow);

            // ---- display
            Section("Ekran ve ölçüm");
            Chips("İmleç", new[] { "Kapalı", "Zaman", "Gerilim" }, () => (int)_view.CursorMode, i => _view.CursorMode = (ScopeCursorMode)i);
            Chips("Matematik", new[] { "Kapalı", "1−2", "1+2" }, () => (int)Scope.MathFunction, i =>
            {
                Scope.MathFunction = (ScopeMathFunction)i;
                Scope.MathScale = Scope.Channels[0].Scale;
            });
            Chips("Ölçüm", new[] { "1", "2", "3", "4", "M" }, () => _view.MeasureChannel, i => _view.MeasureChannel = i, colored: true);
            var dispRow = Row();
            var xy = new CheckBox { Style = Res("Switch"), Content = "XY", Margin = new Thickness(0, 0, 14, 0), ToolTip = "CH1 yatay, CH2 dikey (Lissajous)" };
            xy.Click += (_, _) =>
            {
                Scope.XY = xy.IsChecked == true;
                Changed();
            };
            var load = new CheckBox { Style = Res("Switch"), Content = "Prob yüklemesi", ToolTip = "Prob, devreye 1 MΩ ∥ 110 pF (1×) veya 10 MΩ ∥ 12 pF (10×) olarak bağlanır" };
            load.Click += (_, _) =>
            {
                Sim.ProbeLoading = load.IsChecked == true;
                Sim.BindScopeLoads();
                Changed();
            };
            dispRow.Children.Add(xy);
            dispRow.Children.Add(load);
            _refresh.Add(() =>
            {
                xy.IsChecked = Scope.XY;
                load.IsChecked = Sim.ProbeLoading;
            });
            _root.Children.Add(dispRow);

            var bottom = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            bottom.Children.Add(Btn("Tüm probları çıkar", () => Sim.ClearProbes(), ghost: true));
            var save = Btn("Kaydı kaydet (.rwsim)", () => (Window.GetWindow(this) as MainWindow)?.SaveRecording(), ghost: true);
            save.ToolTip = "Kanallar, olaylar, seri çıktı, araç yörüngesi ve proje tek dosyada";
            bottom.Children.Add(save);
            _root.Children.Add(bottom);

            Sim.InstrumentsChanged += (_, _) => Refresh();
            Sim.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) Refresh();
            };
            Loaded += (_, _) => Refresh();
        }

        private static SimulationSession Sim => Workspace.Current.Simulation;
        private static Oscilloscope Scope => Sim.Scope;
        private static ScopeChannel Selected => Scope.Channels[Math.Max(0, Math.Min(3, Sim.SelectedScopeChannel))];
        private static ScopeChannel TriggerChannel => Scope.Channels[Math.Max(0, Math.Min(3, Scope.TriggerSource))];

        public void Refresh()
        {
            foreach (var r in _refresh) r();
            _run.Content = Scope.Running ? "■  STOP" : "▶  RUN";
        }

        private void Changed()
        {
            _view.ForceAcquire();
            Refresh();
        }

        private Style Res(string key) => (Style)FindResource(key);

        private void Section(string text) =>
            _root.Children.Add(new TextBlock { Text = text, Style = Res("Text.Overline"), Margin = new Thickness(0, 12, 0, 5) });

        private WrapPanel Row() => new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };

        private Button Btn(string text, Action click, bool primary = false, bool ghost = false, string? tip = null)
        {
            var b = new Button
            {
                Content = text,
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 12,
                ToolTip = tip
            };
            if (primary) b.Style = Res("Btn.Primary");
            else if (ghost) b.Style = Res("Btn.Ghost");
            b.Click += (_, _) =>
            {
                click();
                Refresh();
            };
            return b;
        }

        private void Stepper(string label, Func<string> value, Action<int> step, Action? reset = null, string resetText = "0")
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 5), LastChildFill = false };
            var name = new TextBlock { Text = label, Width = 72, Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(name);
            Button Small(string t, Action a)
            {
                var b = new Button { Content = t, Width = 26, Height = 24, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), FontSize = 12 };
                b.Click += (_, _) =>
                {
                    a();
                    _view.ForceAcquire();
                    Refresh();
                };
                return b;
            }

            var text = new TextBlock
            {
                Width = 96,
                TextAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontFamily = (FontFamily)FindResource("F.Mono"),
                FontSize = 12,
                Foreground = (Brush)FindResource("B.Text")
            };
            row.Children.Add(Small("−", () => step(-1)));
            row.Children.Add(text);
            row.Children.Add(Small("+", () => step(1)));
            if (reset != null)
            {
                var r = Small(resetText, reset);
                r.Width = resetText.Length > 1 ? 38 : 26;
                r.Margin = new Thickness(4, 0, 0, 0);
                row.Children.Add(r);
            }

            _refresh.Add(() => text.Text = value());
            _root.Children.Add(row);
        }

        private void Chips(string? label, string[] items, Func<int> get, Action<int> set, bool colored = false)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            if (label != null)
            {
                row.Children.Add(new TextBlock { Text = label, Width = 72, Style = Res("Text.Caption"), VerticalAlignment = VerticalAlignment.Center });
            }

            var buttons = new List<ToggleButton>();
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var b = new ToggleButton
                {
                    Content = items[i],
                    Style = Res("Chip"),
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 11
                };
                if (colored)
                {
                    var c = index < 4 ? ScopeView.ColorOf(index) : ScopeView.MathColor;
                    b.Foreground = new SolidColorBrush(c);
                }

                b.Click += (_, _) =>
                {
                    set(index);
                    _view.ForceAcquire();
                    Refresh();
                };
                buttons.Add(b);
                row.Children.Add(b);
            }

            _refresh.Add(() =>
            {
                int current = get();
                for (int i = 0; i < buttons.Count; i++) buttons[i].IsChecked = i == current;
            });
            _root.Children.Add(row);
        }
    }
}
