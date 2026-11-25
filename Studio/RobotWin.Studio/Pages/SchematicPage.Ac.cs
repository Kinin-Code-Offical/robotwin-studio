using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Analysis;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [ac] The circuit page's "Frekans cevabı" tab: the board compiled afresh, its operating point solved, and the small-signal
    /// response from a chosen voltage source to a chosen net drawn as a Bode plot (gain in dB and phase against frequency on a
    /// logarithmic axis), with the low-frequency gain, the −3 dB corner and the peak. The running simulation is not touched.
    /// </summary>
    public partial class SchematicPage
    {
        private RadioButton? _dockAc;
        private Grid? _acPane;
        private ComboBox? _acSource, _acOut, _acRef;
        private TextBox? _acFrom, _acTo;
        private TextBlock? _acResult;
        private LinePlot? _acGain, _acPhase;

        private void BuildAcDock()
        {
            if (DockMc.Parent is not Panel tabs || McPane.Parent is not Grid host) return;
            _dockAc = new RadioButton
            {
                Style = (Style)FindResource("Chip"), Content = "Frekans cevabı", GroupName = "schdock", Margin = new Thickness(4, 0, 0, 0),
                ToolTip = "AC analizi: bir kaynaktan bir ağa küçük işaret kazancı ve faz (Bode çizimi)"
            };
            tabs.Children.Insert(tabs.Children.IndexOf(DockMc) + 1, _dockAc);
            _acPane = new Grid { Visibility = Visibility.Collapsed };
            _acPane.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            _acPane.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Margin = new Thickness(14, 8, 10, 10) };
            _acSource = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Uyarılan gerilim kaynağı (1 V küçük işaret); öteki kaynaklar dinlenir" };
            _acOut = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Kazancı ölçülen ağ" };
            _acRef = new ComboBox { Margin = new Thickness(0, 0, 0, 6), ToolTip = "Çıkışın referansı (öntanımlı toprak)" };
            _acFrom = new TextBox { Text = "10", Width = 80 };
            _acTo = new TextBox { Text = "1e6", Width = 80 };
            left.Children.Add(new TextBlock { Text = "Giriş kaynağı" });
            left.Children.Add(_acSource);
            left.Children.Add(new TextBlock { Text = "Çıkış ağı" });
            left.Children.Add(_acOut);
            left.Children.Add(new TextBlock { Text = "Referans" });
            left.Children.Add(_acRef);
            var range = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            range.Children.Add(new TextBlock { Text = "Hz", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            range.Children.Add(_acFrom);
            range.Children.Add(new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) });
            range.Children.Add(_acTo);
            left.Children.Add(range);
            var run = new Button { Content = "Hesapla", Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)FindResource("Btn.Primary") };
            run.Click += (_, _) => RunAc();
            left.Children.Add(run);
            _acResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), FontFamily = (FontFamily)FindResource("F.Mono"), FontSize = 12 };
            left.Children.Add(_acResult);
            _acPane.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var plots = new Grid { Margin = new Thickness(4, 8, 10, 8) };
            plots.ColumnDefinitions.Add(new ColumnDefinition());
            plots.ColumnDefinitions.Add(new ColumnDefinition());
            string Hz(double logF)
            {
                double f = Math.Pow(10, logF);
                return f >= 1e6 ? (f / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + " MHz" : f >= 1e3 ? (f / 1e3).ToString("0.##", CultureInfo.InvariantCulture) + " kHz" : f.ToString("0.##", CultureInfo.InvariantCulture) + " Hz";
            }

            _acGain = new LinePlot { Title = "Kazanç (dB)", XLabel = "frekans", XTickLabel = Hz };
            _acPhase = new LinePlot { Title = "Faz (°)", XLabel = "frekans", XTickLabel = Hz, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(_acPhase, 1);
            plots.Children.Add(_acGain);
            plots.Children.Add(_acPhase);
            Grid.SetColumn(plots, 1);
            _acPane.Children.Add(plots);
            host.Children.Add(_acPane);
        }

        /// <summary>Fills the source and net lists from the board as it is now.</summary>
        private void RefreshAcChoices()
        {
            if (_acSource == null || _acOut == null || _acRef == null) return;
            CompiledCircuit compiled;
            try
            {
                compiled = CircuitCompiler.Compile(W.Schematic, W.Library);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                if (_acResult != null) _acResult.Text = "Devre derlenemedi: " + ex.Message;
                return;
            }

            string? keepSource = _acSource.SelectedItem as string, keepOut = _acOut.SelectedItem as string;
            _acSource.ItemsSource = compiled.Circuit.Devices.OfType<VoltageSource>().Select(d => d.Id).OrderBy(x => x, StringComparer.CurrentCulture).ToList();
            var nets = compiled.NodeByNet.Keys.OrderBy(x => x, StringComparer.CurrentCulture).ToList();
            _acOut.ItemsSource = nets;
            _acRef.ItemsSource = new[] { "GND" }.Concat(nets.Where(n => !string.Equals(n, "GND", StringComparison.OrdinalIgnoreCase))).ToList();
            _acSource.SelectedItem = keepSource != null && _acSource.Items.Contains(keepSource) ? keepSource : _acSource.Items.Count > 0 ? _acSource.Items[0] : null;
            _acOut.SelectedItem = keepOut != null && _acOut.Items.Contains(keepOut) ? keepOut : _acOut.Items.Count > 0 ? _acOut.Items[_acOut.Items.Count - 1] : null;
            _acRef.SelectedIndex = 0;
            if (_acSource.Items.Count == 0 && _acResult != null) _acResult.Text = "Devrede gerilim kaynağı yok: bir pil, güç kaynağı ya da sinyal üreteci ekleyin.";
        }

        private void RunAc()
        {
            if (_acSource?.SelectedItem is not string source || _acOut?.SelectedItem is not string output || _acResult == null || _acGain == null || _acPhase == null) return;
            if (!double.TryParse(_acFrom!.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double from) || from <= 0) from = 10;
            if (!double.TryParse(_acTo!.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double to) || to <= from) to = from * 1e5;
            try
            {
                var compiled = CircuitCompiler.Compile(W.Schematic, W.Library);
                var sim = new AnalogSimulator(compiled.Circuit);
                sim.Initialize(InitialState.OperatingPoint);
                int outNode = compiled.NodeByNet.TryGetValue(output, out var o) ? o : 0;
                string reference = _acRef!.SelectedItem as string ?? "GND";
                int refNode = string.Equals(reference, "GND", StringComparison.OrdinalIgnoreCase) ? 0 : compiled.NodeByNet.TryGetValue(reference, out var rn) ? rn : 0;
                var points = sim.AcResponse(source, outNode, refNode, AnalogSimulator.Decades(from, to, 30)).Where(p => !double.IsNaN(p.Gain.Real)).ToList();
                if (points.Count == 0)
                {
                    _acResult.Text = "Çözüm yok: devre bu kaynakla tekil (kaynağın uçları kısa ya da açık mı?).";
                    return;
                }

                var x = points.Select(p => Math.Log10(p.Frequency)).ToArray();
                // The phase unwrapped, so a falling response does not jump from −180° to +180°.
                var phase = new double[points.Count];
                double offset = 0;
                for (int i = 0; i < points.Count; i++)
                {
                    double raw = points[i].PhaseDegrees + offset;
                    if (i > 0 && raw - phase[i - 1] > 180) offset -= 360;
                    else if (i > 0 && raw - phase[i - 1] < -180) offset += 360;
                    phase[i] = points[i].PhaseDegrees + offset;
                }

                _acGain.Series.Clear();
                _acGain.Series.Add(new PlotSeries { Name = output, Unit = "dB", X = x, Y = points.Select(p => p.Decibels).ToArray(), Color = LinePlot.Palette[0] });
                _acGain.Refresh();
                _acPhase.Series.Clear();
                _acPhase.Series.Add(new PlotSeries { Name = output, Unit = "°", X = x, Y = phase, Color = LinePlot.Palette[1] });
                _acPhase.Refresh();
                double corner = AnalogSimulator.Corner(points);
                var peak = points.OrderByDescending(p => p.Decibels).First();
                string F(double f) => f >= 1e6 ? $"{f / 1e6:0.###} MHz" : f >= 1e3 ? $"{f / 1e3:0.###} kHz" : $"{f:0.##} Hz";
                _acResult.Text = $"{F(points[0].Frequency)}: {points[0].Decibels:0.00} dB, {phase[0]:0.0}°\n" +
                                 (double.IsNaN(corner) ? "−3 dB köşesi aralıkta yok" : $"−3 dB köşesi: {F(corner)}") + "\n" +
                                 $"En yüksek: {peak.Decibels:0.00} dB, {F(peak.Frequency)}";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                _acResult.Text = "Hesaplanamadı: " + ex.Message;
            }
        }
    }
}
