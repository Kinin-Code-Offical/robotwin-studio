using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [live] The selected part's live values in the inspector while the circuit runs (D3): its pins' voltages, what flows
    /// through it, a MOSFET's gate voltage and junction temperature, a servo's target and actual angle, and every rating it
    /// is held to with how close it is (a bar: green, amber near the limit, red over it). Refreshed a few times a second.
    /// </summary>
    public partial class SchematicPage
    {
        private readonly List<Action> _liveRefresh = new List<Action>();
        private readonly Stopwatch _liveClock = Stopwatch.StartNew();

        /// <summary>The live rows of the part in the inspector (only while simulating).</summary>
        private void LiveSection(SchematicPart part, PartDefinition def)
        {
            var sim = W.Simulation;
            if (!sim.IsActive || sim.Compiled == null) return;
            var devices = sim.Compiled.DevicesOf(part.RefDes).ToList();
            if (devices.Count == 0) return;
            Section("Canlı değerler");

            // the pins (a board's many pins are in the Kart pinleri tab)
            if (def.Pins.Count <= 8)
            {
                foreach (var pin in def.Pins)
                {
                    string name = pin.Name;
                    LiveRow($"{name} pini", () => SiValue.Format(sim.PinVoltage(part.RefDes, name), "V"));
                }
            }

            foreach (var device in devices)
            {
                string Prefix(string what) => devices.Count > 1 ? $"{device.Id}: {what}" : what;
                switch (device)
                {
                    case Mosfet m:
                        LiveRow(Prefix("VGS"), () => SiValue.Format(Node(m.Gate) - Node(m.Source), "V"));
                        LiveRow(Prefix("ID"), () => SiValue.Format(m.DrainCurrent, "A"));
                        LiveRow(Prefix("Güç kaybı"), () => SiValue.Format(Math.Abs(m.DrainCurrent * (Node(m.Drain) - Node(m.Source))), "W"));   // the junction's temperature is a rating below
                        break;
                    case Servo s:
                        LiveRow(Prefix("Hedef açı"), () => double.IsNaN(s.TargetAngleDeg) ? "—" : $"{s.TargetAngleDeg:0.0}°");
                        LiveRow(Prefix("Gerçek açı"), () => $"{s.AngleDeg:0.0}°");
                        LiveRow(Prefix("Akım"), () => SiValue.Format(s.SupplyCurrent, "A"));
                        break;
                    default:
                        if (!double.IsNaN(SimulationSession.DeviceCurrent(device))) LiveRow(Prefix("Akım"), () => SiValue.Format(SimulationSession.DeviceCurrent(device), "A"));
                        break;
                }

                // every rating with its margin (the fault finder's own measures)
                foreach (var stress in CircuitDiagnostics.Stresses(device))
                {
                    string key = stress.Key;
                    StressRow(Prefix(stress.Quantity), () => CircuitDiagnostics.Stresses(device).Where(x => x.Key == key).Select(x => (DeviceStress?)x).FirstOrDefault());
                }
            }

            Inspector.Children.Add(Hint("Simülasyon sürerken saniyede birkaç kez yenilenir. Çubuk: sınıra ne kadar yakın (yeşil rahat, sarı sınıra yakın, kırmızı sınırın üstünde)."));
            RefreshLive();

            double Node(int node) => node <= 0 || sim.Simulator == null ? 0 : sim.Simulator.NodeVoltage(node);
        }

        private void LiveRow(string label, Func<string> value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) });
            row.Children.Add(new TextBlock { Text = Shell.Lang.T(label), Style = (Style)FindResource("Text.Body") });
            var text = new TextBlock { Style = (Style)FindResource("Text.Mono"), HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Inspector.Children.Add(row);
            _liveRefresh.Add(() => text.Text = value());
        }

        /// <summary>A rating: its value against the limit and a bar of how much of it is used.</summary>
        private void StressRow(string label, Func<DeviceStress?> read)
        {
            var box = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(new TextBlock { Text = Shell.Lang.T(label), Style = (Style)FindResource("Text.Body") });
            var text = new TextBlock { Style = (Style)FindResource("Text.Mono") };
            Grid.SetColumn(text, 1);
            top.Children.Add(text);
            box.Children.Add(top);
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 3, 0, 0) };
            track.SetResourceReference(Border.BackgroundProperty, "B.Border");
            var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var grid = new Grid();
            grid.Children.Add(track);
            grid.Children.Add(fill);
            box.Children.Add(grid);
            Inspector.Children.Add(box);
            _liveRefresh.Add(() =>
            {
                if (read() is not { } s)
                {
                    text.Text = "—";
                    fill.Width = 0;
                    return;
                }

                text.Text = $"{SiValue.Format(s.Value, s.Unit)} / {SiValue.Format(s.Rating, s.Unit)}";
                double r = Math.Max(0, Math.Min(1, s.Ratio));
                fill.Width = Math.Max(0, grid.ActualWidth) * r;
                fill.SetResourceReference(Border.BackgroundProperty, s.Ratio >= 1 ? "B.Danger" : s.Ratio >= 0.8 ? "B.Warning" : "B.Success");
                box.ToolTip = Shell.Lang.T($"Sınırın %{s.Ratio * 100:0}'i kullanılıyor; kalan pay %{Math.Max(0, s.MarginPercent):0}");
            });
            grid.SizeChanged += (_, _) => RefreshLive();
        }

        /// <summary>Every live row read again (the simulation's frames call it; a few times a second is enough).</summary>
        private void RefreshLive(bool throttled = false)
        {
            if (_liveRefresh.Count == 0) return;
            if (throttled && _liveClock.ElapsedMilliseconds < 200) return;
            _liveClock.Restart();
            foreach (var r in _liveRefresh) r();
        }
    }
}
