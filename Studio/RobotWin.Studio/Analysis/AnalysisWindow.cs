using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Analysis
{
    /// <summary>
    /// Offline analysis ("render mode"): the world runs off-screen for a chosen duration at a chosen
    /// accuracy, every signal is recorded, then FEM stress and flow are computed. Results: channel
    /// plots, stress map, flow field, notes; saved as .rwanalysis or exported as CSV.
    /// </summary>
    public sealed class AnalysisWindow : Window
    {
        private readonly WorldDocument _world;
        private readonly IWorldGeometry _geometry;
        private readonly ComboBox _preset = new ComboBox();
        private readonly ComboBox _accuracy = new ComboBox();
        private readonly TextBox _duration = new TextBox { Width = 70 };
        private readonly TextBox _rate = new TextBox { Width = 70 };
        private readonly StackPanel _stressList = new StackPanel();
        private readonly ComboBox _aeroObject = new ComboBox();
        private readonly TextBox _aeroSpeed = new TextBox { Width = 70 };
        private readonly ComboBox _aeroAxis = new ComboBox();
        private readonly CheckBox _stress = new CheckBox { Content = "Gerilme (FEM)" };
        private readonly CheckBox _aero = new CheckBox { Content = "Akış (CFD, 2B kesit)" };
        private readonly CheckBox _thermal = new CheckBox { Content = "Isı (kart / parça ısınması)" };
        private readonly TextBox _extend = new TextBox { Width = 70 };
        private readonly ComboBox _thermalPart = new ComboBox { MinWidth = 160 };
        private readonly Image _heat = new Image { Stretch = Stretch.Uniform };
        private readonly LinePlot _heatPlot = new LinePlot { Title = "Sıcaklık", Height = 190 };
        private readonly TextBlock _heatInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        private readonly Button _run = new Button { Content = "Analizi başlat", Padding = new Thickness(14, 6, 14, 6) };
        private readonly Button _save = new Button { Content = "Kaydet (.rwanalysis)", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Button _csv = new Button { Content = "CSV olarak dışa aktar", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        private readonly ProgressBar _progress = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 10, 0, 4) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly TabControl _tabs = new TabControl();
        private readonly ListBox _channels = new ListBox { SelectionMode = SelectionMode.Multiple };
        private readonly TextBox _filter = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        private readonly LinePlot _plot = new LinePlot();
        private readonly LinePlot _stressPlot = new LinePlot { Title = "En yüksek von Mises gerilmesi", Height = 170 };
        private readonly StressView _stressView = new StressView();
        private readonly ComboBox _stressPart = new ComboBox { MinWidth = 160 };
        private readonly TextBlock _stressInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        private readonly Image _flow = new Image { Stretch = Stretch.Uniform };
        private readonly LinePlot _cdPlot = new LinePlot { Title = "Sürükleme (Cd) ve kaldırma (Cl) katsayısı", Height = 170 };
        private readonly TextBlock _aeroInfo = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        private readonly TextBox _log = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) };
        private CancellationTokenSource? _cancel;
        private AnalysisResult? _result;

        public AnalysisWindow(WorldDocument world, IWorldGeometry geometry)
        {
            _world = world;
            _geometry = geometry;
            Title = "Analiz modu — " + world.Title;
            Width = 1280;
            Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res("B.Surface");
            Foreground = Res("B.Text");
            _log.FontFamily = (FontFamily)Application.Current.FindResource("F.Mono");
            _log.Background = Res("B.Base");
            _log.Foreground = Res("B.Text");

            var root = new DockPanel { Background = Res("B.Surface") };   // also what a snapshot of the content shows
            var left = new StackPanel { Width = 300, Margin = new Thickness(16) };
            DockPanel.SetDock(left, Dock.Left);
            root.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Width = 332 });
            DockPanel.SetDock((UIElement)root.Children[0], Dock.Left);
            root.Children.Add(_tabs);

            left.Children.Add(Caption("Hazır ayar"));
            foreach (AnalysisPreset p in Enum.GetValues(typeof(AnalysisPreset))) _preset.Items.Add(new ComboBoxItem { Content = AnalysisSettings.PresetName(p), Tag = p });
            left.Children.Add(_preset);
            left.Children.Add(Caption("Doğruluk"));
            foreach (AccuracyPreset a in Enum.GetValues(typeof(AccuracyPreset))) _accuracy.Items.Add(new ComboBoxItem { Content = AnalysisSettings.AccuracyName(a), Tag = a });
            left.Children.Add(_accuracy);
            left.Children.Add(Row("Süre (s)", _duration));
            left.Children.Add(Row("Kayıt hızı (örnek/s)", _rate));

            left.Children.Add(Caption("Hesaplar"));
            left.Children.Add(_stress);
            left.Children.Add(new Border { Child = _stressList, Margin = new Thickness(20, 2, 0, 6) });
            left.Children.Add(_aero);
            left.Children.Add(Row("Nesne", _aeroObject));
            left.Children.Add(Row("Hava hızı (m/s)", _aeroSpeed));
            left.Children.Add(Row("Akış yönü", _aeroAxis));
            foreach (var ax in new[] { "+X", "-X", "+Y", "-Y" }) _aeroAxis.Items.Add(ax);
            left.Children.Add(new Border { Child = _thermal, Margin = new Thickness(0, 8, 0, 0) });
            left.Children.Add(Row("Sonra ısıtmaya devam (s)", _extend));

            _run.Style = (Style)Application.Current.FindResource("Btn.Primary");
            _run.Margin = new Thickness(0, 16, 0, 0);
            _run.Click += (_, _) => RunOrCancel();
            left.Children.Add(_run);
            left.Children.Add(_progress);
            left.Children.Add(_status);
            _save.Click += (_, _) => Save();
            _csv.Click += (_, _) => ExportCsv();
            left.Children.Add(_save);
            left.Children.Add(_csv);
            left.Children.Add(new TextBlock
            {
                Text = "Gerçek zamanlı oynatmadan bağımsızdır: dünya ekrana bağlı olmadan, seçilen doğrulukta baştan sona hesaplanır. Sonuçlar dünya dosyanızı değiştirmez.",
                Style = (Style)Application.Current.FindResource("Text.Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            });

            var movable = world.Objects.Where(o => o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model || o.Kind == WorldObjectKind.Component).ToList();
            foreach (var o in movable)
            {
                _stressList.Children.Add(new CheckBox { Content = o.Name, Tag = o.Id, Foreground = Foreground, Margin = new Thickness(0, 1, 0, 1) });
                _aeroObject.Items.Add(new ComboBoxItem { Content = o.Name, Tag = o.Id });
            }

            // Results.
            var plotTab = new DockPanel { Margin = new Thickness(8) };
            var chanPanel = new DockPanel { Width = 300, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(_filter, Dock.Top);
            chanPanel.Children.Add(_filter);
            chanPanel.Children.Add(_channels);
            DockPanel.SetDock(chanPanel, Dock.Left);
            plotTab.Children.Add(chanPanel);
            plotTab.Children.Add(_plot);
            _filter.TextChanged += (_, _) => FillChannels();
            _channels.SelectionChanged += (_, _) => PlotSelected();
            _tabs.Items.Add(new TabItem { Header = "Grafikler", Content = plotTab });

            var stressTab = new DockPanel { Margin = new Thickness(8) };
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(new TextBlock { Text = "Parça: ", VerticalAlignment = VerticalAlignment.Center });
            top.Children.Add(_stressPart);
            DockPanel.SetDock(top, Dock.Top);
            DockPanel.SetDock(_stressInfo, Dock.Top);
            DockPanel.SetDock(_stressPlot, Dock.Bottom);
            stressTab.Children.Add(top);
            stressTab.Children.Add(_stressInfo);
            stressTab.Children.Add(_stressPlot);
            stressTab.Children.Add(_stressView);
            _stressPart.SelectionChanged += (_, _) => ShowStress();
            _tabs.Items.Add(new TabItem { Header = "Gerilme", Content = stressTab });

            var aeroTab = new DockPanel { Margin = new Thickness(8) };
            DockPanel.SetDock(_aeroInfo, Dock.Top);
            DockPanel.SetDock(_cdPlot, Dock.Bottom);
            aeroTab.Children.Add(_aeroInfo);
            aeroTab.Children.Add(_cdPlot);
            aeroTab.Children.Add(new Border { Background = Res("B.Base"), Child = _flow });
            _tabs.Items.Add(new TabItem { Header = "Akış", Content = aeroTab });

            var heatTab = new DockPanel { Margin = new Thickness(8) };
            var heatTop = new StackPanel { Orientation = Orientation.Horizontal };
            heatTop.Children.Add(new TextBlock { Text = "Nesne: ", VerticalAlignment = VerticalAlignment.Center });
            heatTop.Children.Add(_thermalPart);
            DockPanel.SetDock(heatTop, Dock.Top);
            DockPanel.SetDock(_heatInfo, Dock.Top);
            DockPanel.SetDock(_heatPlot, Dock.Bottom);
            heatTab.Children.Add(heatTop);
            heatTab.Children.Add(_heatInfo);
            heatTab.Children.Add(_heatPlot);
            heatTab.Children.Add(new Border { Background = Res("B.Base"), Child = _heat });
            _thermalPart.SelectionChanged += (_, _) => ShowThermal();
            _tabs.Items.Add(new TabItem { Header = "Isı", Content = heatTab });
            _tabs.Items.Add(new TabItem { Header = "Günlük", Content = _log });
            Content = root;

            _preset.SelectionChanged += (_, _) => ApplyPreset();
            _preset.SelectedIndex = (int)AnalysisPreset.Full;
        }

        /// <summary>Settings shown in the form.</summary>
        public AnalysisSettings Settings { get; private set; } = new AnalysisSettings();

        /// <summary>Starts the run right away (snapshots, automation).</summary>
        public Task RunNow() => Run();

        private Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private TextBlock Caption(string text) => new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("Text.Caption"), Margin = new Thickness(0, 10, 0, 4) };

        private static FrameworkElement Row(string label, FrameworkElement input)
        {
            var g = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(input, Dock.Right);
            g.Children.Add(input);
            if (input is ComboBox cb) cb.MinWidth = 140;
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return g;
        }

        private void ApplyPreset()
        {
            if (_preset.SelectedItem is not ComboBoxItem { Tag: AnalysisPreset p }) return;
            var s = AnalysisSettings.ForPreset(p, _world);
            if (p == AnalysisPreset.Custom) s = Read();
            Load(s);
        }

        private void Load(AnalysisSettings s)
        {
            Settings = s;
            _accuracy.SelectedIndex = (int)s.Accuracy;
            _duration.Text = s.Duration.ToString("0.###", CultureInfo.InvariantCulture);
            _rate.Text = s.OutputRate.ToString("0", CultureInfo.InvariantCulture);
            _stress.IsChecked = s.Stress;
            foreach (CheckBox cb in _stressList.Children) cb.IsChecked = s.StressObjects.Contains((string)cb.Tag);
            _aero.IsChecked = s.Aero;
            _aeroObject.SelectedItem = _aeroObject.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == s.AeroObject);
            _aeroSpeed.Text = s.AeroSpeed.ToString("0.###", CultureInfo.InvariantCulture);
            _aeroAxis.SelectedItem = s.AeroAxis;
            _thermal.IsChecked = s.Thermal;
            _extend.Text = s.ThermalExtend.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static double Num(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        private AnalysisSettings Read()
        {
            var s = Settings.Clone();
            if (_accuracy.SelectedItem is ComboBoxItem { Tag: AccuracyPreset a }) s.Accuracy = a;
            s.Duration = Math.Min(600, Num(_duration, s.Duration));
            s.OutputRate = Math.Min(20000, Num(_rate, s.OutputRate));
            s.Stress = _stress.IsChecked == true;
            s.StressObjects = _stressList.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
            s.Aero = _aero.IsChecked == true;
            s.AeroObject = (_aeroObject.SelectedItem as ComboBoxItem)?.Tag as string;
            s.AeroSpeed = Num(_aeroSpeed, s.AeroSpeed);
            s.AeroAxis = _aeroAxis.SelectedItem as string ?? "+X";
            s.Thermal = _thermal.IsChecked == true;
            s.ThermalExtend = double.TryParse(_extend.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double ext) ? Math.Clamp(ext, 0, 3600) : s.ThermalExtend;
            return s;
        }

        private void RunOrCancel()
        {
            if (_cancel != null)
            {
                _cancel.Cancel();
                return;
            }

            _ = Run();
        }

        private async Task Run()
        {
            var s = Read();
            Settings = s;
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _run.Content = "İptal et";
            _save.IsEnabled = _csv.IsEnabled = false;
            _status.Text = "Başlıyor…";
            var progress = new Progress<(string Phase, double Value)>(p =>
            {
                _progress.Value = p.Value;
                _status.Text = $"{p.Phase} · %{p.Value * 100:0}";
            });
            IProgress<(string, double)> report = progress;
            try
            {
                // Solvers report very often; pass on only visible changes so the UI thread stays free.
                string lastPhase = string.Empty;
                double lastValue = -1;
                long lastTick = 0;
                void OnProgress(string phase, double v)
                {
                    long now = Environment.TickCount64;
                    if (phase == lastPhase && Math.Abs(v - lastValue) < 0.01 && now - lastTick < 250) return;
                    lastPhase = phase;
                    lastValue = v;
                    lastTick = now;
                    report.Report((phase, v));
                }

                var result = await Task.Run(() => AnalysisRun.Run(_world, s, _geometry, token, OnProgress), token);
                Show(result);
            }
            catch (OperationCanceledException)
            {
                _status.Text = "İptal edildi.";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
            {
                _status.Text = "Analiz yapılamadı: " + ex.Message;
            }
            finally
            {
                _cancel = null;
                _run.Content = "Analizi başlat";
                _progress.Value = 0;
            }
        }

        private void Show(AnalysisResult r)
        {
            _result = r;
            _save.IsEnabled = _csv.IsEnabled = true;
            _status.Text = $"{r.SimulatedSeconds:0.###} s benzetim, {r.Channels.Count} örnek × {r.Channels.Channels.Count} kanal, {r.WallSeconds:0.0} s sürdü."
                + (r.Cancelled ? " (iptal edildi)" : string.Empty) + (r.HaltReason.Length > 0 ? " Durdu: " + r.HaltReason : string.Empty);
            FillChannels();
            foreach (var name in new[] { "scene/ke", "scene.ke" })
            {
                var item = _channels.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == name);
                if (item != null) item.IsSelected = true;
            }

            if (_channels.SelectedItems.Count == 0 && _channels.Items.Count > 0) ((ListBoxItem)_channels.Items[0]).IsSelected = true;

            _stressPart.Items.Clear();
            foreach (var st in r.Stress) _stressPart.Items.Add(new ComboBoxItem { Content = st.Name, Tag = st });
            if (_stressPart.Items.Count > 0) _stressPart.SelectedIndex = 0;
            else _stressView.Show(null, null);

            _thermalPart.Items.Clear();
            foreach (var th in r.Thermal) _thermalPart.Items.Add(new ComboBoxItem { Content = th.Name, Tag = th });
            if (_thermalPart.Items.Count > 0) _thermalPart.SelectedIndex = 0;
            else
            {
                _heat.Source = null;
                _heatInfo.Text = r.Settings.Thermal ? "Güç harcayan parça bulunamadı." : "Isı analizi seçilmedi.";
            }

            var a = r.Aero;
            _flow.Source = FlowImage.Render(a, true);
            _cdPlot.Series.Clear();
            if (a != null)
            {
                _aeroInfo.Text = $"{a.Name}: hava {a.Speed:0.##} m/s ({a.Axis}), Re = {a.ReynoldsPhysical:0}. Ortalama Cd = {a.MeanCd:0.00}, Cl = {a.MeanCl:0.00}, Strouhal = {a.Strouhal:0.000}. "
                    + $"Tahmini sürükleme kuvveti {SiValue.Format(a.DragForce, "N")}. Görüntü: girdaplılık (mavi/kırmızı), koyu = katı.\n" + string.Join(" ", a.Notes);
                var t = a.Times.ToArray();
                _cdPlot.Series.Add(new PlotSeries { Name = "Cd", X = t, Y = a.Cd.ToArray(), Color = LinePlot.Palette[0] });
                _cdPlot.Series.Add(new PlotSeries { Name = "Cl", X = t, Y = a.Cl.ToArray(), Color = LinePlot.Palette[1] });
            }
            else _aeroInfo.Text = "Akış analizi seçilmedi.";
            _cdPlot.Refresh();

            var log = new StringBuilder();
            log.AppendLine(r.Settings.ToString());
            foreach (var l in r.Log) log.AppendLine(l);
            foreach (var st in r.Stress)
            {
                log.AppendLine($"[{st.Name}] {st.Method}, {st.Material}");
                foreach (var note in st.Notes) log.AppendLine("  " + note);
            }

            log.AppendLine();
            foreach (var e in r.Events.Take(500)) log.AppendLine($"{e.Time,9:0.0000} s  {e.Source}: {e.Message}");
            foreach (var s in r.Serial.Take(500)) log.AppendLine($"{s.Time,9:0.0000} s  [{s.Board}] {s.Text.TrimEnd()}");
            _log.Text = log.ToString();
            if (r.Stress.Count > 0) _tabs.SelectedIndex = 1;
        }

        private void FillChannels()
        {
            _channels.Items.Clear();
            if (_result == null) return;
            string f = _filter.Text.Trim();
            foreach (var c in _result.Channels.Channels)
            {
                string label = $"{c.Group} · {c.Label} [{c.Unit}]";
                if (f.Length > 0 && label.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                _channels.Items.Add(new ListBoxItem { Content = label, Tag = c.Name, Foreground = Foreground });
            }
        }

        private void PlotSelected()
        {
            _plot.Series.Clear();
            if (_result == null) return;
            var t = _result.Channels.TimeArray();
            int k = 0;
            foreach (ListBoxItem item in _channels.SelectedItems)
            {
                var c = _result.Channels.Find((string)item.Tag);
                if (c == null) continue;
                _plot.Series.Add(new PlotSeries { Name = $"{c.Group} {c.Label}", Unit = c.Unit, X = t, Y = c.ToArray(), Color = LinePlot.Palette[k++ % LinePlot.Palette.Length] });
                if (k >= 6) break;
            }

            _plot.Refresh();
        }

        private void ShowStress()
        {
            if (_stressPart.SelectedItem is not ComboBoxItem { Tag: StressResult st })
            {
                _stressView.Show(null, null);
                return;
            }

            var crit = st.Critical;
            _stressView.Show(st, crit);
            _stressInfo.Text = crit == null
                ? string.Join(" ", st.Notes)
                : $"{st.Name} · {st.Material} (akma {SiValue.Format(st.Yield, "Pa")}) · en kritik an t = {crit.Time:0.000} s: σ_vM = {SiValue.Format(crit.MaxVonMises, "Pa")}, "
                  + $"en büyük yer değiştirme {SiValue.Format(crit.MaxDisplacement, "m")}, emniyet katsayısı {crit.MinSafetyFactor:0.0}"
                  + (crit.MinSafetyFactor < 1 ? " — PARÇA AKAR/KIRILIR" : crit.MinSafetyFactor < 2 ? " — sınırda" : string.Empty);
            _stressPlot.Series.Clear();
            _stressPlot.Series.Add(new PlotSeries
            {
                Name = "σ_vM en yüksek",
                Unit = "Pa",
                X = st.Frames.Select(f => f.Time).ToArray(),
                Y = st.Frames.Select(f => f.MaxVonMises).ToArray(),
                Color = LinePlot.Palette[3]
            });
            _stressPlot.Refresh();
        }

        private void ShowThermal()
        {
            _heatPlot.Series.Clear();
            if (_thermalPart.SelectedItem is not ComboBoxItem { Tag: ThermalResult t })
            {
                _heatPlot.Refresh();
                return;
            }

            _heat.Source = HeatImage.Render(t, t.Frames.Count - 1);
            var times = t.History.Select(h => h.Time).ToArray();
            _heatPlot.Series.Add(new PlotSeries { Name = "en sıcak nokta", Unit = "°C", X = times, Y = t.History.Select(h => h.Max).ToArray(), Color = LinePlot.Palette[3] });
            _heatPlot.Series.Add(new PlotSeries { Name = "ortalama", Unit = "°C", X = times, Y = t.History.Select(h => h.Mean).ToArray(), Color = LinePlot.Palette[1] });
            _heatPlot.Refresh();
            double p = t.History.Count > 0 ? t.History[^1].Power : 0;
            _heatInfo.Text = $"{t.Name} · {t.Material}: en yüksek {t.PeakTemperature:0.0} °C, son güç {SiValue.Format(p, "W")}. "
                + (t.Sources.Count > 0 ? "Isı kaynakları: " + string.Join(", ", t.Sources.Select(x => x.Name)) + ". " : string.Empty)
                + string.Join(" ", t.Notes);
        }

        private void Save()
        {
            if (_result == null) return;
            var dlg = new SaveFileDialog { Filter = "RobotWin analizi (*.rwanalysis)|*.rwanalysis", FileName = _world.Title + AnalysisResult.Extension };
            if (dlg.ShowDialog(this) != true) return;
            _result.Save(dlg.FileName);
            _status.Text = "Kaydedildi: " + dlg.FileName;
        }

        private void ExportCsv()
        {
            if (_result == null) return;
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = _world.Title + "-analiz.csv" };
            if (dlg.ShowDialog(this) != true) return;
            var ch = _result.Channels;
            using var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
            w.WriteLine("t [s]," + string.Join(",", ch.Channels.Select(c => $"\"{c.Group} {c.Label} [{c.Unit}]\"")));
            for (int i = 0; i < ch.Count; i++)
                w.WriteLine(ch.Time(i).ToString("R", CultureInfo.InvariantCulture) + "," + string.Join(",", ch.Channels.Select(c => i < c.Count ? c[i].ToString("G9", CultureInfo.InvariantCulture) : string.Empty)));
            _status.Text = "CSV yazıldı: " + dlg.FileName;
        }
    }
}
