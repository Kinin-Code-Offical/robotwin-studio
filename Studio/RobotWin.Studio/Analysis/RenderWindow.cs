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
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Analysis
{
    /// <summary>
    /// Render mode: a recorded stretch of the world is computed again off-screen at full accuracy —
    /// every pose, force and signal, FEM stress of every part at video frame rate, the airflow around
    /// the whole assembly and heating — and then replayed like a video in the same 3D scene: scrub the
    /// timeline, colour every part by its stress, show contact forces and the flow, click a graph to
    /// jump to that second.
    /// </summary>
    public sealed class RenderWindow : Window
    {
        private readonly WorldDocument _world;
        private readonly IWorldGeometry _geometry;
        private readonly WorldScene _scene;
        private readonly Playback _playback = new Playback();

        // settings
        private readonly TextBox _duration = new TextBox { Width = 70 };
        private readonly ComboBox _accuracy = new ComboBox();
        private readonly TextBox _stressFps = new TextBox { Width = 70, Text = "15" };
        private readonly StackPanel _parts = new StackPanel();
        private readonly CheckBox _aero = new CheckBox { Content = "Hava akışı (tüm montaj, 3B)", IsChecked = true };
        private readonly TextBox _aeroSpeed = new TextBox { Width = 70, Text = "2" };
        private readonly CheckBox _thermal = new CheckBox { Content = "Isınma (kartlar, motorlar)", IsChecked = true };
        private readonly CheckBox _meshCheck = new CheckBox { Content = "Ağ yakınsaması denetimi", IsChecked = false, ToolTip = "Her parçanın en kötü anı 1,4 kat daha ince ağla yeniden çözülür ve gerilmenin ne kadar değiştiği yazılır (%10'dan fazlası: sonuç ağa duyarlı)." };
        private readonly CheckBox _bands = new CheckBox { Content = "Belirsizlik bandı (4 tolerans koşusu)", IsChecked = false, ToolTip = "Parça değerleri tolerans bantlarından (klonlar ±%35–40) çekilerek 4 kez daha çalıştırılır; grafiklerde gerçek parçaların düşebileceği aralık gölge olarak görünür. Render süresi ~5 katına çıkar." };
        private readonly Button _render = new Button { Content = "Render al", Padding = new Thickness(14, 6, 14, 6) };
        private readonly ProgressBar _progress = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 10, 0, 4) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly Button _csv = new Button { Content = "Tüm kanallar CSV", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Button _save = new Button { Content = "Kaydet (.rwanalysis)", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };

        // timeline
        private readonly Button _play = new Button { Content = "▶", Width = 34 };
        private readonly ComboBox _speed = new ComboBox { Width = 72 };
        private readonly Slider _timeline = new Slider { Minimum = 0, Maximum = 1, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _time = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        private readonly ComboBox _mode = new ComboBox { Width = 210 };
        private readonly ComboBox _scale = new ComboBox { Width = 220 };
        private readonly CheckBox _forces = new CheckBox { Content = "Kuvvetler", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _air = new CheckBox { Content = "Hava", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _follow = new CheckBox { Content = "Kamera takip", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _legend = new TextBlock { Margin = new Thickness(10), Foreground = Brushes.White, FontSize = 12, IsHitTestVisible = false };

        // results
        private readonly TabControl _tabs = new TabControl { Height = 250 };
        private readonly ListBox _channels = new ListBox { SelectionMode = SelectionMode.Multiple };
        private readonly TextBox _filter = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        private readonly LinePlot _plot = new LinePlot();
        private readonly LinePlot _stressPlot = new LinePlot { Title = "Parçaların en yüksek von Mises gerilmesi" };
        private readonly StackPanel _partList = new StackPanel();
        private readonly ComboBox _heatPart = new ComboBox { MinWidth = 160 };
        private readonly Image _heat = new Image { Stretch = Stretch.Uniform };
        private readonly LinePlot _heatPlot = new LinePlot { Title = "Sıcaklık" };
        private readonly TextBox _log = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0) };

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private CancellationTokenSource? _cancel;
        private AnalysisResult? _result;
        private double _t;
        private bool _playing;
        private DateTime _lastTick;
        private double _stressMax = 1;
        private double _forceMax = 1;
        private bool _seeking;
        private bool _framed;

        /// <summary>Largest extent of the moving objects' positions at a frame (for the follow camera's distance).</summary>
        private double AssemblySize(RobotTwin.CoreSim.Analysis.Recording.ChannelStore store, int i)
        {
            Vec3 lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue), hi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var o in _world.Objects)
            {
                if (o.IsStatic || AnalysisRun.RecordedPose(store, o.Id, i) is not { } pp) continue;
                double r = Math.Max(o.SizeX, Math.Max(o.SizeY, o.SizeZ)) / 2;
                lo = new Vec3(Math.Min(lo.X, pp.Position.X - r), Math.Min(lo.Y, pp.Position.Y - r), Math.Min(lo.Z, pp.Position.Z - r));
                hi = new Vec3(Math.Max(hi.X, pp.Position.X + r), Math.Max(hi.Y, pp.Position.Y + r), Math.Max(hi.Z, pp.Position.Z + r));
            }

            return hi.X >= lo.X ? Math.Max(0.15, (hi - lo).Length) : 0.5;
        }
        private static readonly ImageBrush HeatBrush = MakeHeatBrush();

        public RenderWindow(WorldDocument world, IWorldGeometry geometry, double duration, PartLibrary library, Func<WorldObject, double>? massOf)
        {
            _world = world;
            _geometry = geometry;
            Title = "Render — " + world.Title;
            Width = 1440;
            Height = 900;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res("B.Surface");
            Foreground = Res("B.Text");

            _scene = new WorldScene { Library = library, MassOf = massOf };
            _scene.Load(world);
            _scene.Playback = _playback;

            var root = new DockPanel { Background = Res("B.Surface") };
            var left = new StackPanel { Width = 290, Margin = new Thickness(14) };
            var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(leftScroll, Dock.Left);
            root.Children.Add(leftScroll);
            BuildSettings(left, duration);

            var center = new Grid();
            center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var view = new Grid();
            view.Children.Add(_scene);
            view.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0xA0, 0x10, 0x10, 0x14)), Child = _legend, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8), IsHitTestVisible = false });
            center.Children.Add(view);
            var bar = BuildTimeline();
            Grid.SetRow(bar, 1);
            center.Children.Add(bar);
            BuildTabs();
            Grid.SetRow(_tabs, 2);
            center.Children.Add(_tabs);
            root.Children.Add(center);
            Content = root;

            foreach (var cb in new[] { _aero, _thermal, _bands, _meshCheck, _forces, _air, _follow }) cb.Foreground = Res("B.Text");
            _timer.Tick += (_, _) => Tick();
            Closed += (_, _) =>
            {
                _timer.Stop();
                _cancel?.Cancel();
            };
            _legend.Text = "Render alınınca kayıt burada saniye saniye oynatılır.";
        }

        public AnalysisResult? Result => _result;

        /// <summary>Jumps the replay to a time (s from the start of the render).</summary>
        public void SeekTo(double t) => Seek(t);

        /// <summary>Starts rendering right away (after a recording, snapshots).</summary>
        public Task RenderNow() => Render();

        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private TextBlock Caption(string text) => new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("Text.Caption"), Margin = new Thickness(0, 10, 0, 4) };

        private static FrameworkElement Row(string label, FrameworkElement input)
        {
            var g = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(input, Dock.Right);
            g.Children.Add(input);
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return g;
        }

        // ================================================================ settings

        private void BuildSettings(StackPanel left, double duration)
        {
            left.Children.Add(new TextBlock { Text = "Render", FontSize = 18, Margin = new Thickness(0, 0, 0, 4) });
            left.Children.Add(new TextBlock
            {
                Text = "Kayıttaki süre ekrandan bağımsız olarak, seçilen doğrulukta baştan hesaplanır: her parçanın konumu, hızı, kuvvetleri, gerilmesi, havanın montaj etrafındaki akışı ve ısınma. Sonra video gibi oynatılır.",
                Style = (Style)Application.Current.FindResource("Text.Caption"),
                TextWrapping = TextWrapping.Wrap
            });
            _duration.Text = duration.ToString("0.###", CultureInfo.InvariantCulture);
            left.Children.Add(Row("Süre (s)", _duration));
            foreach (AccuracyPreset a in Enum.GetValues(typeof(AccuracyPreset))) _accuracy.Items.Add(new ComboBoxItem { Content = AnalysisSettings.AccuracyName(a), Tag = a });
            _accuracy.SelectedIndex = (int)AccuracyPreset.Fine;
            _accuracy.MinWidth = 150;
            left.Children.Add(Row("Doğruluk", _accuracy));
            left.Children.Add(Row("Gerilme kare hızı (fps)", _stressFps));
            left.Children.Add(Caption("Gerilmesi hesaplanacak parçalar"));
            foreach (var o in _world.Objects.Where(o => !o.IsStatic && (o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model)))
                _parts.Children.Add(new CheckBox { Content = o.Name, Tag = o.Id, IsChecked = true, Foreground = Foreground, Margin = new Thickness(0, 1, 0, 1) });
            if (_parts.Children.Count == 0) _parts.Children.Add(new TextBlock { Text = "(hareketli gövde / model yok)", Style = (Style)Application.Current.FindResource("Text.Caption") });
            left.Children.Add(_parts);
            left.Children.Add(new Border { Child = _aero, Margin = new Thickness(0, 10, 0, 0) });
            left.Children.Add(Row("Duran montajda test hızı (m/s)", _aeroSpeed));
            left.Children.Add(new Border { Child = _thermal, Margin = new Thickness(0, 8, 0, 0) });
            left.Children.Add(new Border { Child = _meshCheck, Margin = new Thickness(0, 8, 0, 0) });
            left.Children.Add(new Border { Child = _bands, Margin = new Thickness(0, 8, 0, 0) });
            _render.Style = (Style)Application.Current.FindResource("Btn.Primary");
            _render.Margin = new Thickness(0, 16, 0, 0);
            _render.Click += (_, _) =>
            {
                if (_cancel != null) _cancel.Cancel();
                else _ = Render();
            };
            left.Children.Add(_render);
            left.Children.Add(_progress);
            left.Children.Add(_status);
            _csv.Click += (_, _) => ExportCsv();
            _save.Click += (_, _) => Save();
            left.Children.Add(_csv);
            left.Children.Add(_save);
        }

        private static double Num(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        private AnalysisSettings ReadSettings()
        {
            double duration = Math.Min(120, Num(_duration, 2));
            var stress = _parts.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).Take(12).ToList();
            return new AnalysisSettings
            {
                Preset = AnalysisPreset.Custom,
                Duration = duration,
                OutputRate = 200,
                Accuracy = _accuracy.SelectedItem is ComboBoxItem { Tag: AccuracyPreset a } ? a : AccuracyPreset.Fine,
                Firmware = true,
                Stress = stress.Count > 0,
                StressObjects = stress,
                StressInstants = Math.Max(2, (int)Math.Ceiling(duration * Num(_stressFps, 15))),
                StressTargetCells = 700,
                Aero = _aero.IsChecked == true,
                Aero3D = true,
                AeroSpeed = Num(_aeroSpeed, 2),
                AeroAxis = "+X",
                AeroResolution = 18,
                AeroPasses = 15,
                Thermal = _thermal.IsChecked == true,
                ThermalExtend = 0,
                ThermalFrames = Math.Max(10, (int)(duration * 10)),
                UncertaintyRuns = _bands.IsChecked == true ? 4 : 0,
                StressMeshCheck = _meshCheck.IsChecked == true
            };
        }

        private async Task Render()
        {
            var s = ReadSettings();
            _cancel = new CancellationTokenSource();
            var token = _cancel.Token;
            _render.Content = "İptal et";
            _csv.IsEnabled = _save.IsEnabled = false;
            _status.Text = "Render başlıyor…";
            var progress = new Progress<(string Phase, double Value)>(p =>
            {
                _progress.Value = p.Value;
                _status.Text = $"{p.Phase} · %{p.Value * 100:0}";
            });
            IProgress<(string, double)> report = progress;
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

            try
            {
                var result = await Task.Run(() => AnalysisRun.Run(_world, s, _geometry, token, OnProgress), token);
                Show(result);
            }
            catch (OperationCanceledException)
            {
                _status.Text = "İptal edildi.";
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
            {
                _status.Text = "Render alınamadı: " + ex.Message;
            }
            finally
            {
                _cancel = null;
                _render.Content = "Yeniden render al";
                _progress.Value = 0;
            }
        }

        // ================================================================ timeline

        private FrameworkElement BuildTimeline()
        {
            var bar = new DockPanel { Margin = new Thickness(8, 6, 8, 6) };
            var leftSide = new StackPanel { Orientation = Orientation.Horizontal };
            _play.Click += (_, _) => TogglePlay();
            leftSide.Children.Add(_play);
            foreach (var x in new[] { 0.05, 0.1, 0.25, 0.5, 1.0, 2.0 }) _speed.Items.Add(new ComboBoxItem { Content = "×" + x.ToString("0.##", CultureInfo.InvariantCulture), Tag = x });
            _speed.SelectedIndex = 3;
            _speed.Margin = new Thickness(6, 0, 6, 0);
            _speed.ToolTip = "Oynatma hızı (×0.1 = 10 kat ağır çekim)";
            leftSide.Children.Add(_speed);
            leftSide.Children.Add(_time);
            DockPanel.SetDock(leftSide, Dock.Left);
            bar.Children.Add(leftSide);

            var rightSide = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var m in new[] { "Renk: gerilme (von Mises)", "Renk: yok", "Renk: hız oku", "Renk: sıcaklık" }) _mode.Items.Add(m);
            _mode.SelectedIndex = 0;
            foreach (var m in new[] { "Ölçek: render'ın en yükseği", "Ölçek: malzemenin akma sınırı" }) _scale.Items.Add(m);
            _scale.SelectedIndex = 0;
            _mode.SelectionChanged += (_, _) => Seek(_t);
            _scale.SelectionChanged += (_, _) => Seek(_t);
            _forces.Checked += (_, _) => Seek(_t);
            _forces.Unchecked += (_, _) => Seek(_t);
            _air.Checked += (_, _) => Seek(_t);
            _air.Unchecked += (_, _) => Seek(_t);
            rightSide.Children.Add(_mode);
            rightSide.Children.Add(new Border { Width = 6 });
            rightSide.Children.Add(_scale);
            rightSide.Children.Add(new Border { Width = 10 });
            rightSide.Children.Add(_forces);
            rightSide.Children.Add(new Border { Width = 10 });
            rightSide.Children.Add(_air);
            rightSide.Children.Add(new Border { Width = 10 });
            rightSide.Children.Add(_follow);
            DockPanel.SetDock(rightSide, Dock.Right);
            bar.Children.Add(rightSide);
            _timeline.Margin = new Thickness(8, 0, 12, 0);
            _timeline.ValueChanged += (_, e) =>
            {
                if (!_seeking) Seek(e.NewValue);
            };
            bar.Children.Add(_timeline);
            PreviewKeyDown += (_, e) =>
            {
                if (_result == null) return;
                double frame = 1.0 / Math.Max(1, _result.Settings.OutputRate);
                if (e.Key == Key.Space) TogglePlay();
                else if (e.Key == Key.Right) Seek(_t + frame);
                else if (e.Key == Key.Left) Seek(_t - frame);
                else if (e.Key == Key.Home) Seek(0);
                else if (e.Key == Key.End) Seek(_result.Channels.Duration);
                else return;
                e.Handled = true;
            };
            return bar;
        }

        private void TogglePlay()
        {
            if (_result == null) return;
            _playing = !_playing;
            _play.Content = _playing ? "❚❚" : "▶";
            if (_playing)
            {
                if (_t >= _result.Channels.Duration - 1e-9) _t = 0;
                _lastTick = DateTime.Now;
                _timer.Start();
            }
            else _timer.Stop();
        }

        private void Tick()
        {
            if (_result == null || !_playing) return;
            var now = DateTime.Now;
            double dt = (now - _lastTick).TotalSeconds;
            _lastTick = now;
            double speed = _speed.SelectedItem is ComboBoxItem { Tag: double x } ? x : 1;
            double t = _t + dt * speed;
            if (t >= _result.Channels.Duration)
            {
                t = _result.Channels.Duration;
                TogglePlay();
            }

            Seek(t);
        }

        // ================================================================ results

        private void BuildTabs()
        {
            var graphs = new DockPanel { Margin = new Thickness(6) };
            var chans = new DockPanel { Width = 280, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(_filter, Dock.Top);
            chans.Children.Add(_filter);
            chans.Children.Add(_channels);
            DockPanel.SetDock(chans, Dock.Left);
            graphs.Children.Add(chans);
            graphs.Children.Add(_plot);
            _filter.TextChanged += (_, _) => FillChannels();
            _channels.SelectionChanged += (_, _) => PlotSelected();
            _plot.Seek += Seek;
            _tabs.Items.Add(new TabItem { Header = "Grafikler", Content = graphs });

            var parts = new DockPanel { Margin = new Thickness(6) };
            var list = new ScrollViewer { Content = _partList, Width = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(list, Dock.Left);
            parts.Children.Add(list);
            parts.Children.Add(_stressPlot);
            _stressPlot.Seek += Seek;
            _tabs.Items.Add(new TabItem { Header = "Parçalar · gerilme", Content = parts });

            var heat = new DockPanel { Margin = new Thickness(6) };
            var heatLeft = new DockPanel { Width = 320, Margin = new Thickness(0, 0, 6, 0) };
            DockPanel.SetDock(_heatPart, Dock.Top);
            heatLeft.Children.Add(_heatPart);
            heatLeft.Children.Add(new Border { Background = Res("B.Base"), Child = _heat, Margin = new Thickness(0, 6, 0, 0) });
            DockPanel.SetDock(heatLeft, Dock.Left);
            heat.Children.Add(heatLeft);
            heat.Children.Add(_heatPlot);
            _heatPart.SelectionChanged += (_, _) => ShowHeatPlot();
            _heatPlot.Seek += Seek;
            _tabs.Items.Add(new TabItem { Header = "Isı", Content = heat });

            _log.FontFamily = (FontFamily)Application.Current.FindResource("F.Mono");
            _log.Background = Res("B.Base");
            _log.Foreground = Res("B.Text");
            _tabs.Items.Add(new TabItem { Header = "Günlük", Content = _log });
        }

        private void Show(AnalysisResult r)
        {
            _result = r;
            _playback.Store = r.Channels;
            _csv.IsEnabled = _save.IsEnabled = true;
            _status.Text = $"{r.SimulatedSeconds:0.###} s hesaplandı: {r.Channels.Count} kare × {r.Channels.Channels.Count} kanal, {r.Stress.Count} parçada {r.Stress.Sum(x => x.Frames.Count)} gerilme çözümü, {r.AeroFrames.Count} akış karesi; {r.WallSeconds:0.0} s sürdü."
                + (r.HaltReason.Length > 0 ? " Durdu: " + r.HaltReason : string.Empty);
            _stressMax = Math.Max(1e-9, r.Stress.SelectMany(x => x.Frames).Select(f => f.MaxVonMises).DefaultIfEmpty(0).Max());
            _forceMax = Math.Max(1e-9, r.Loads.Values.SelectMany(l => l).SelectMany(l => l.Loads).Select(l => l.Force.Length).DefaultIfEmpty(0).Max());
            _timeline.Maximum = Math.Max(1e-6, r.Channels.Duration);
            FillChannels();
            foreach (var name in new[] { "scene/ke" })
            {
                var item = _channels.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == name);
                if (item != null) item.IsSelected = true;
            }

            if (_channels.SelectedItems.Count == 0 && _channels.Items.Count > 0) ((ListBoxItem)_channels.Items[0]).IsSelected = true;
            BuildPartList();
            _heatPart.Items.Clear();
            foreach (var th in r.Thermal) _heatPart.Items.Add(new ComboBoxItem { Content = th.Name, Tag = th });
            if (_heatPart.Items.Count > 0) _heatPart.SelectedIndex = 0;
            var log = new StringBuilder();
            log.AppendLine(r.Settings.ToString());
            foreach (var l in r.Log) log.AppendLine(l);
            foreach (var st in r.Stress)
            {
                log.AppendLine($"[{st.Name}] {st.Method}, {st.Material}");
                foreach (var note in st.Notes) log.AppendLine("  " + note);
            }

            foreach (var a in r.AeroFrames) log.AppendLine($"[akış t={a.Time:0.00} s] " + string.Join(" ", a.Notes));
            log.AppendLine();
            foreach (var e in r.Events.Take(500)) log.AppendLine($"{e.Time,9:0.0000} s  {e.Source}: {e.Message}");
            foreach (var s in r.Serial.Take(500)) log.AppendLine($"{s.Time,9:0.0000} s  [{s.Board}] {s.Text.TrimEnd()}");
            _log.Text = log.ToString();
            Seek(0);
        }

        private void FillChannels()
        {
            _channels.Items.Clear();
            if (_result == null) return;
            string f = _filter.Text.Trim();
            foreach (var c in _result.Channels.Channels)
            {
                if (c.Name.StartsWith("pose/", StringComparison.Ordinal)) continue;
                string label = c.Unit.Length > 0 ? $"{c.Label} [{c.Unit}]" : c.Label;
                if (f.Length > 0 && (label + " " + c.Group).IndexOf(f, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                _channels.Items.Add(new ListBoxItem { Content = label, Tag = c.Name, Foreground = Foreground, ToolTip = c.Group });
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
                var band = _result.Bands.TryGetValue(c.Name, out var bb) && bb.Lo.Length == t.Length ? bb : ((double[]?)null, (double[]?)null);
                _plot.Series.Add(new PlotSeries { Name = c.Label + (band.Item1 != null ? $" (gölge: {_result.BandRuns} tolerans koşusu)" : string.Empty), Unit = c.Unit, X = t, Y = c.ToArray(), Lo = band.Item1, Hi = band.Item2, Color = LinePlot.Palette[k++ % LinePlot.Palette.Length] });
                if (k >= 6) break;
            }

            _plot.Cursor = _t;
            _plot.Refresh();
        }

        private void BuildPartList()
        {
            _partList.Children.Clear();
            _stressPlot.Series.Clear();
            if (_result == null) return;
            int k = 0;
            foreach (var st in _result.Stress.OrderBy(x => x.Critical?.MinSafetyFactor ?? double.MaxValue))
            {
                var crit = st.Critical;
                var b = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 4),
                    Padding = new Thickness(8, 4, 8, 4),
                    Content = crit == null
                        ? new TextBlock { Text = st.Name + " — " + string.Join(" ", st.Notes), TextWrapping = TextWrapping.Wrap }
                        : new TextBlock
                        {
                            Text = $"{st.Name} · {st.Material}\nen kötü an t = {crit.Time:0.000} s: σ = {SiValue.Format(crit.MaxVonMises, "Pa")}, emniyet {crit.MinSafetyFactor:0.#}"
                                   + (crit.MinSafetyFactor < 1 ? "  — AKAR / KIRILIR" : crit.MinSafetyFactor < 2 ? "  — sınırda" : string.Empty)
                                   + (double.IsFinite(st.MeshChange) ? $"\nağ yakınsaması: %{st.MeshChange * 100:0.#} " + (st.MeshChange > 0.10 ? "(ağa duyarlı)" : "(ağdan bağımsız)") : string.Empty),
                            Foreground = crit.MinSafetyFactor < 1 ? Brushes.OrangeRed : crit.MinSafetyFactor < 2 ? Brushes.Orange : Foreground,
                            TextWrapping = TextWrapping.Wrap
                        },
                    ToolTip = "O ana git"
                };
                if (crit != null)
                {
                    double tc = crit.Time - (_result.Channels.Count > 0 ? _result.Channels.Time(0) : 0);
                    b.Click += (_, _) => Seek(tc);
                }

                _partList.Children.Add(b);
                if (RobotTwin.CoreSim.Analysis.Export.InpExport.CanExport(st))
                {
                    var export = new Button { Content = "Bu durumu dışa aktar (.inp: CalculiX / ANSYS / Abaqus)", Margin = new Thickness(12, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2),
                        ToolTip = "Aynı ağ, malzeme, mesnet ve o anki yükler (temas, eklem, yerçekimi, atalet dengesi). Başka bir çözücüyle çözüp sonucu karşılaştırın; RobotWin'in sonucu dosyada yorum olarak yazılıdır." };
                    var part = st;
                    export.Click += (_, _) =>
                    {
                        var dlg = new SaveFileDialog { Filter = "Abaqus / CalculiX girdisi (*.inp)|*.inp", FileName = part.Name + ".inp" };
                        if (dlg.ShowDialog(this) != true) return;
                        RobotTwin.CoreSim.Analysis.Export.InpExport.Save(part, dlg.FileName);
                        _status.Text = "Dışa aktarıldı: " + dlg.FileName;
                    };
                    _partList.Children.Add(export);
                }

                if (st.Frames.Count > 0)
                {
                    double t0 = _result.Channels.Count > 0 ? _result.Channels.Time(0) : 0;
                    _stressPlot.Series.Add(new PlotSeries
                    {
                        Name = st.Name,
                        Unit = "Pa",
                        X = st.Frames.Select(f => f.Time - t0).ToArray(),
                        Y = st.Frames.Select(f => f.MaxVonMises).ToArray(),
                        Color = LinePlot.Palette[k++ % LinePlot.Palette.Length]
                    });
                }
            }

            if (_result.Stress.Count == 0) _partList.Children.Add(new TextBlock { Text = "Gerilme hesaplanmadı.", Style = (Style)Application.Current.FindResource("Text.Caption") });
            _stressPlot.Refresh();
        }

        private void ShowHeatPlot()
        {
            _heatPlot.Series.Clear();
            if (_heatPart.SelectedItem is ComboBoxItem { Tag: ThermalResult t })
            {
                var x = t.History.Select(h => h.Time).ToArray();
                _heatPlot.Series.Add(new PlotSeries { Name = "en sıcak", Unit = "°C", X = x, Y = t.History.Select(h => h.Max).ToArray(), Color = LinePlot.Palette[3] });
                _heatPlot.Series.Add(new PlotSeries { Name = "ortalama", Unit = "°C", X = x, Y = t.History.Select(h => h.Mean).ToArray(), Color = LinePlot.Palette[1] });
            }

            Seek(_t);
        }

        // ================================================================ seek + overlays

        private void Seek(double t)
        {
            if (_result == null || _result.Channels.Count == 0) return;
            var store = _result.Channels;
            t = Math.Max(0, Math.Min(store.Duration, t));
            _t = t;
            int i = AnalysisRun.SampleAt(store, t);
            _playback.Index = i;
            _seeking = true;
            _timeline.Value = t;
            _seeking = false;
            _time.Text = $"t = {t:0.000} / {store.Duration:0.000} s";
            var overlay = new Model3DGroup();
            var legend = new StringBuilder($"t = {t:0.000} s · kare {i + 1}/{store.Count}\n");
            var hidden = new List<string>();
            double t0 = store.Time(0);

            if (_mode.SelectedIndex == 0) StressOverlay(overlay, i, t + t0, hidden, legend);
            else if (_mode.SelectedIndex == 2) SpeedOverlay(overlay, i, legend);
            _scene.SetTints(_mode.SelectedIndex == 3 ? TemperatureTints(i, legend) : null);
            if (_forces.IsChecked == true) ForceOverlay(overlay, i, t + t0, legend);
            if (_air.IsChecked == true) AirOverlay(overlay, t, legend);
            _scene.SetHidden(hidden);
            _scene.ShowPlayback(overlay);
            if (_follow.IsChecked == true)
            {
                // Keep the moving assembly in view: centre on the mean of everything that moves.
                Vec3 sum = Vec3.Zero;
                int count = 0;
                foreach (var o in _world.Objects)
                {
                    if (o.IsStatic || AnalysisRun.RecordedPose(store, o.Id, i) is not { } pp) continue;
                    sum += pp.Position;
                    count++;
                }

                if (count > 0) _scene.CenterOn(sum / count, _framed ? 0 : AssemblySize(store, i) * 2.2);
                _framed = true;
            }
            _legend.Text = legend.ToString().TrimEnd();

            _plot.Cursor = t;
            _plot.Refresh();
            _stressPlot.Cursor = t;
            _stressPlot.Refresh();
            _heatPlot.Cursor = t;
            _heatPlot.Refresh();
            if (_heatPart.SelectedItem is ComboBoxItem { Tag: ThermalResult th } && th.Frames.Count > 0)
            {
                int f = 0;
                for (int k = 0; k < th.Times.Count; k++)
                    if (th.Times[k] <= t + 1e-9) f = k;
                _heat.Source = HeatImage.Render(th, f);
            }
        }

        private void StressOverlay(Model3DGroup g, int i, double time, List<string> hidden, StringBuilder legend)
        {
            if (_result == null || _result.Stress.Count == 0) return;
            bool yield = _scale.SelectedIndex == 1;
            string worst = string.Empty;
            double worstRatio = -1;
            foreach (var st in _result.Stress)
            {
                if (st.Frames.Count == 0 || st.NodeCount == 0) continue;
                var f = st.Frames.OrderBy(x => Math.Abs(x.Time - time)).First();
                if (f.VonMises.Length < st.NodeCount) continue;
                var pose = AnalysisRun.RecordedPose(_result.Channels, st.ObjectId, i);
                if (pose == null) continue;
                var (pos, q, _) = pose.Value;
                double vmax = yield ? Math.Max(1, st.Yield) : _stressMax;
                double maxDisp = st.Frames.Max(x => x.MaxDisplacement);
                double size = 0;
                for (int n = 0; n < st.NodeCount; n++) size = Math.Max(size, st.Node(n).Length);
                double exaggerate = maxDisp > 0 ? 0.04 * size / maxDisp : 0;
                var centre = pos + q.Rotate(st.BodyOffset);
                var mesh = new MeshGeometry3D();
                for (int n = 0; n < st.NodeCount; n++)
                {
                    var p = st.Node(n);
                    if (f.Displacement.Length >= 3 * st.NodeCount)
                        p += new Vec3(f.Displacement[3 * n], f.Displacement[3 * n + 1], f.Displacement[3 * n + 2]) * exaggerate;
                    var w = centre + q.Rotate(p);
                    mesh.Positions.Add(new Point3D(w.X, w.Y, w.Z));
                    mesh.TextureCoordinates.Add(new Point(Math.Max(0.002, Math.Min(0.998, f.VonMises[n] / vmax)), 0.5));
                }

                int stride = st.Order == 2 ? 6 : 3;
                if (st.Method == "beam") BeamMesh(mesh, st, f, centre, q, exaggerate, vmax);
                else if (st.Surface.Length < stride) continue;
                else for (int k = 0; k + stride <= st.Surface.Length; k += stride)
                {
                    int a = st.Surface[k], b = st.Surface[k + 1], c = st.Surface[k + 2];
                    if (stride == 6)
                    {
                        int ab = st.Surface[k + 3], bc = st.Surface[k + 4], ca = st.Surface[k + 5];
                        Tri(mesh, a, ab, ca);
                        Tri(mesh, ab, b, bc);
                        Tri(mesh, ca, bc, c);
                        Tri(mesh, ab, bc, ca);
                    }
                    else Tri(mesh, a, b, c);
                }

                var mat = new MaterialGroup();
                mat.Children.Add(new DiffuseMaterial(HeatBrush));
                mat.Children.Add(new EmissiveMaterial(HeatBrush) { Color = Color.FromRgb(170, 170, 170) });
                g.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
                hidden.Add(st.ObjectId);
                double ratio = f.MaxVonMises / Math.Max(1, st.Yield);
                if (ratio > worstRatio)
                {
                    worstRatio = ratio;
                    worst = $"{st.Name}: σ = {SiValue.Format(f.MaxVonMises, "Pa")} (akmanın %{ratio * 100:0.#}'i)";
                }
            }

            legend.AppendLine("Renk: von Mises gerilmesi, mavi 0 → kırmızı " + (yield ? "akma sınırı" : SiValue.Format(_stressMax, "Pa")) + "; deformasyon büyütülmüş");
            if (worst.Length > 0) legend.AppendLine("En zorlanan: " + worst);
        }

        /// <summary>A beam drawn with its real cross-section along the deformed axis, coloured per node.</summary>
        private static void BeamMesh(MeshGeometry3D mesh, StressResult st, StressFrame f, Vec3 centre, Quat q, double exaggerate, double vmax)
        {
            mesh.Positions.Clear();
            mesh.TextureCoordinates.Clear();
            int n = st.NodeCount;
            if (n < 2) return;
            var axis = (st.Node(n - 1) - st.Node(0)).Normalized();
            var up = st.BeamUp - axis * st.BeamUp.Dot(axis);
            up = up.Length > 1e-9 ? up.Normalized() : (Math.Abs(axis.Z) < 0.9 ? Vec3.UnitZ : Vec3.UnitX);
            var side = axis.Cross(up);
            double hw = st.BeamWidth / 2, hh = st.BeamHeight / 2;
            var corners = new[] { side * hw + up * hh, side * -hw + up * hh, side * -hw + up * -hh, side * hw + up * -hh };
            for (int i = 0; i < n; i++)
            {
                var p = st.Node(i);
                if (f.Displacement.Length >= 3 * n) p += new Vec3(f.Displacement[3 * i], f.Displacement[3 * i + 1], f.Displacement[3 * i + 2]) * exaggerate;
                double u = Math.Max(0.002, Math.Min(0.998, f.VonMises.Length > i ? f.VonMises[i] / Math.Max(1e-9, vmax) : 0));
                foreach (var c in corners)
                {
                    var w = centre + q.Rotate(p + c);
                    mesh.Positions.Add(new Point3D(w.X, w.Y, w.Z));
                    mesh.TextureCoordinates.Add(new Point(u, 0.5));
                }
            }

            for (int i = 0; i + 1 < n; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    int a = 4 * i + k, b = 4 * i + (k + 1) % 4, c = 4 * (i + 1) + (k + 1) % 4, d = 4 * (i + 1) + k;
                    Tri(mesh, a, b, c);
                    Tri(mesh, a, c, d);
                }
            }

            Tri(mesh, 0, 2, 1);
            Tri(mesh, 0, 3, 2);
            int e = 4 * (n - 1);
            Tri(mesh, e, e + 1, e + 2);
            Tri(mesh, e, e + 2, e + 3);
        }

        /// <summary>[thermal] Every part glows by its recorded temperature (ambient dark → hottest of the render red).</summary>
        private Dictionary<string, Color> TemperatureTints(int i, StringBuilder legend)
        {
            var tints = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            if (_result == null) return tints;
            var store = _result.Channels;
            double amb = _world.Environment.AmbientC, max = amb + 1;
            foreach (var o in _world.Objects)
                if (store.Find("pose/" + o.Id + "/temp") is { } c)
                    max = Math.Max(max, c.ToArray().Where(double.IsFinite).DefaultIfEmpty(amb).Max());
            string hottest = string.Empty;
            double hot = double.MinValue;
            foreach (var o in _world.Objects)
            {
                if (store.Find("pose/" + o.Id + "/temp") is not { } c || i >= c.Count) continue;
                double tc = c[i];
                double k = Math.Max(0, Math.Min(1, (tc - amb) / (max - amb)));
                var h = LinePlot.Heat(0.15 + 0.85 * k);
                tints[o.Id] = Color.FromRgb((byte)(h.R * k), (byte)(h.G * k), (byte)(h.B * k));
                if (tc > hot)
                {
                    hot = tc;
                    var mat = RobotTwin.CoreSim.Analysis.Fem.StructuralMaterial.For(o.Material);
                    hottest = $"{o.Name} {tc:0.0} °C ({mat.Name}: {mat.StateAt(tc)}" + (double.IsNaN(mat.GlassTransition) ? ")" : $", camsı geçiş {mat.GlassTransition:0} °C)");
                }
            }

            legend.AppendLine($"Renk: parça sıcaklığı, ortam {amb:0} °C → kırmızı {max:0.0} °C");
            if (hottest.Length > 0) legend.AppendLine("En sıcak: " + hottest);
            return tints;
        }

        private static void Tri(MeshGeometry3D m, int a, int b, int c)
        {
            m.TriangleIndices.Add(a);
            m.TriangleIndices.Add(b);
            m.TriangleIndices.Add(c);
        }

        private void SpeedOverlay(Model3DGroup g, int i, StringBuilder legend)
        {
            if (_result == null) return;
            var store = _result.Channels;
            double vmax = 0;
            foreach (var o in _world.Objects)
            {
                if (store.Find("body/" + o.Id + "/v") is { } c) vmax = Math.Max(vmax, c.ToArray().Where(double.IsFinite).DefaultIfEmpty(0).Max());
            }

            if (vmax <= 0) return;
            foreach (var o in _world.Objects)
            {
                double V(string k) => store.Find("body/" + o.Id + "/" + k) is { } c && i < c.Count ? c[i] : 0;
                var v = new Vec3(V("vx"), V("vy"), V("vz"));
                if (v.Length < vmax * 0.02 || AnalysisRun.RecordedPose(store, o.Id, i) is not { } pose) continue;
                g.Children.Add(Arrow(pose.Position, pose.Position + v / vmax * 0.08, Color.FromRgb(0x10, 0xB9, 0x81), 0.0018));
            }

            legend.AppendLine($"Oklar: parça hızları (en uzun = {vmax:0.###} m/s)");
        }

        private void ForceOverlay(Model3DGroup g, int i, double time, StringBuilder legend)
        {
            if (_result == null || _result.Loads.Count == 0) return;
            int n = 0;
            foreach (var kv in _result.Loads)
            {
                var list = kv.Value;
                if (list.Count == 0 || AnalysisRun.RecordedPose(_result.Channels, kv.Key, i) is not { } pose) continue;
                int lo = 0, hi = list.Count - 1;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) >> 1;
                    if (list[mid].Time <= time) lo = mid;
                    else hi = mid - 1;
                }

                var ls = list[lo];
                var st = _result.Stress.FirstOrDefault(x => x.ObjectId == kv.Key);
                var centre = pose.Position + pose.Orientation.Rotate(st?.BodyOffset ?? Vec3.Zero);
                foreach (var (p, f, kind) in ls.Loads)
                {
                    if (f.Length < _forceMax * 0.01) continue;
                    var wp = centre + pose.Orientation.Rotate(p);
                    var wf = pose.Orientation.Rotate(f);
                    var color = kind == LoadSample.KindContact ? Color.FromRgb(0xF5, 0x9E, 0x0B) : Color.FromRgb(0x06, 0xB6, 0xD4);
                    g.Children.Add(Arrow(wp, wp + wf / _forceMax * 0.06, color, 0.0012));
                    n++;
                }
            }

            if (n > 0) legend.AppendLine($"Oklar: temas (turuncu) ve eklem (camgöbeği) kuvvetleri, en uzun = {SiValue.Format(_forceMax, "N")}");
        }

        private void AirOverlay(Model3DGroup g, double t, StringBuilder legend)
        {
            if (_result == null || _result.AeroFrames.Count == 0) return;
            var a = _result.AeroFrames.OrderBy(x => Math.Abs(x.Time - t)).First();
            if (a.Speed3D == null || a.Speed3D.Length < a.Nx * a.Ny * a.Nz) return;
            int nx = a.Nx, ny = a.Ny, nz = a.Nz, j = ny / 2;
            // Crop to the assembly and its near wake: half a diameter around the solid cells, 1.5 D behind.
            int x0 = nx, x1 = 0, z0 = nz, z1 = 0;
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                        if (a.Solid[(z * ny + y) * nx + x])
                        {
                            x0 = Math.Min(x0, x);
                            x1 = Math.Max(x1, x);
                            z0 = Math.Min(z0, z);
                            z1 = Math.Max(z1, z);
                        }

            if (x1 < x0) return;
            int d = (int)Math.Round(a.ReferenceLength / a.Dx);
            x0 = Math.Max(0, x0 - d / 2);
            x1 = Math.Min(nx - 1, x1 + 3 * d / 2);
            z0 = Math.Max(0, z0 - d / 3);
            z1 = Math.Min(nz - 1, z1 + d / 3);
            int w = x1 - x0 + 1, h = z1 - z0 + 1;
            double vmax = a.Speed * 1.3;   // colour: still air blue, free stream yellow-green, speeded-up red
            var px = new byte[w * h * 4];
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int c = (z * ny + j) * nx + x;
                    int o = ((z1 - z) * w + (x - x0)) * 4;
                    if (a.Solid[c]) continue;
                    var col = LinePlot.Heat(a.Speed3D[c] / vmax);
                    px[o] = col.B;
                    px[o + 1] = col.G;
                    px[o + 2] = col.R;
                    px[o + 3] = 70;
                }
            }

            int nxFull = nx, nzFull = nz;
            nx = w;
            nz = h;
            var bmp = BitmapSource.Create(nx, nz, 96, 96, PixelFormats.Bgra32, null, px, nx * 4);
            bmp.Freeze();
            var brush = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            Vec3 P(double x, double z) => a.Origin + a.AxisX * ((x + x0) * a.Dx) + a.AxisY * ((j + 0.5) * a.Dx) + a.AxisZ * ((z + z0) * a.Dx);
            var mesh = new MeshGeometry3D();
            foreach (var (x, z, u, v) in new[] { (0.0, 0.0, 0.0, 1.0), ((double)nx, 0.0, 1.0, 1.0), ((double)nx, (double)nz, 1.0, 0.0), (0.0, (double)nz, 0.0, 0.0) })
            {
                var wp = P(x, z);
                mesh.Positions.Add(new Point3D(wp.X, wp.Y, wp.Z));
                mesh.TextureCoordinates.Add(new Point(u, v));
            }

            Tri(mesh, 0, 1, 2);
            Tri(mesh, 0, 2, 3);
            var mat = new EmissiveMaterial(brush);
            g.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            // 3D streamlines through the solved flow, coloured by local speed.
            if (a.Streamlines.Count > 0)
            {
                var lines = new MeshGeometry3D();
                double radius = Math.Max(0.0005, Math.Min(0.003, a.Dx * 0.04));
                foreach (var pl in a.Streamlines) StreamTube(lines, pl, radius, a.Speed * 1.3);
                var lm = new MaterialGroup();
                lm.Children.Add(new DiffuseMaterial(HeatBrush));
                lm.Children.Add(new EmissiveMaterial(HeatBrush) { Color = Color.FromRgb(150, 150, 150) });
                g.Children.Add(new GeometryModel3D(lines, lm) { BackMaterial = lm });
            }
            var centre = a.Origin + a.AxisX * ((x0 + d) * a.Dx) + a.AxisY * (ny * a.Dx / 2) + a.AxisZ * ((z0 + z1) * a.Dx / 2);
            _ = nxFull + nzFull;
            if (a.Force.Length > 0) g.Children.Add(Arrow(centre, centre + a.Force.Normalized() * 0.08, Color.FromRgb(0xA8, 0x55, 0xF7), 0.0025));
            bool coarse = a.Notes.Any(n => n.Contains("bir hücre kalınlığında"));
            var store = _result.Channels;
            int si = AnalysisRun.SampleAt(store, t);
            double lift = 0, drag = 0;
            foreach (var c in store.Channels)
            {
                if (c.Name.StartsWith("aero/", StringComparison.Ordinal) && c.Name.EndsWith("/lift", StringComparison.Ordinal) && si < c.Count) lift += c[si];
                if (c.Name.StartsWith("aero/", StringComparison.Ordinal) && c.Name.EndsWith("/drag", StringComparison.Ordinal) && si < c.Count) drag += c[si];
            }

            if (coarse)
                legend.AppendLine($"Hava (t = {a.Time:0.00} s): {a.Speed:0.##} m/s bağıl rüzgâr; 3B akım çizgileri ve kesit (hücre {a.Dx * 1000:0} mm, ince yüzeyler kalın görünür: akış görselleştirmesi)."
                    + (lift != 0 ? $" Kuvvetler kanat modelinden: kaldırma {SiValue.Format(lift, "N")}, sürükleme {SiValue.Format(drag, "N")}, L/D {lift / Math.Max(1e-9, drag):0.0}" : string.Empty));
            else
                legend.AppendLine($"Hava (t = {a.Time:0.00} s): {a.Speed:0.##} m/s bağıl rüzgâr, montaja kuvvet {SiValue.Format(a.Force.Length, "N")}, Cd {a.MeanCd:0.00} (mor ok); kesit ve 3B akım çizgileri: mavi yavaşlayan, sarı serbest akış, kırmızı hızlanan hava");
        }

        /// <summary>A thin tube along a streamline (x, y, z, speed per point), texture u = speed / max for the heat colours.</summary>
        private static void StreamTube(MeshGeometry3D mesh, float[] pl, double radius, double vmax)
        {
            int n = pl.Length / 4;
            if (n < 2) return;
            const int seg = 5;
            int b0 = mesh.Positions.Count;
            Vec3 prevSide = Vec3.UnitY;
            for (int i = 0; i < n; i++)
            {
                var p = new Vec3(pl[4 * i], pl[4 * i + 1], pl[4 * i + 2]);
                var q = new Vec3(pl[4 * Math.Min(n - 1, i + 1)], pl[4 * Math.Min(n - 1, i + 1) + 1], pl[4 * Math.Min(n - 1, i + 1) + 2]);
                var o = new Vec3(pl[4 * Math.Max(0, i - 1)], pl[4 * Math.Max(0, i - 1) + 1], pl[4 * Math.Max(0, i - 1) + 2]);
                var t = q - o;
                if (t.LengthSquared < 1e-18) t = Vec3.UnitX;
                t = t.Normalized();
                var side = prevSide - t * prevSide.Dot(t);
                if (side.LengthSquared < 1e-12) side = Math.Abs(t.Z) < 0.9 ? t.Cross(Vec3.UnitZ) : t.Cross(Vec3.UnitX);
                side = side.Normalized();
                prevSide = side;
                var up = t.Cross(side);
                double u = Math.Max(0.002, Math.Min(0.998, pl[4 * i + 3] / vmax));
                for (int k = 0; k < seg; k++)
                {
                    double ang = 2 * Math.PI * k / seg;
                    var w = p + (side * Math.Cos(ang) + up * Math.Sin(ang)) * radius;
                    mesh.Positions.Add(new Point3D(w.X, w.Y, w.Z));
                    mesh.TextureCoordinates.Add(new Point(u, 0.5));
                }
            }

            for (int i = 0; i + 1 < n; i++)
            {
                for (int k = 0; k < seg; k++)
                {
                    int a = b0 + i * seg + k, b = b0 + i * seg + (k + 1) % seg, c = b0 + (i + 1) * seg + (k + 1) % seg, d = b0 + (i + 1) * seg + k;
                    Tri(mesh, a, b, c);
                    Tri(mesh, a, c, d);
                }
            }
        }

        private static Model3D Arrow(Vec3 from, Vec3 to, Color color, double radius)
        {
            var d = to - from;
            double len = d.Length;
            var mesh = new MeshGeometry3D();
            if (len < 1e-6) return new GeometryModel3D(mesh, new DiffuseMaterial(Brushes.Transparent));
            var axis = d / len;
            var side = Math.Abs(axis.Z) < 0.9 ? axis.Cross(Vec3.UnitZ).Normalized() : axis.Cross(Vec3.UnitX).Normalized();
            var up = axis.Cross(side);
            const int seg = 8;
            double shaft = len * 0.75;
            // shaft ring at from and at shaft end, tip cone
            for (int ring = 0; ring < 3; ring++)
            {
                double along = ring == 0 ? 0 : shaft;
                double r = ring == 2 ? radius * 2.6 : radius;
                for (int k = 0; k < seg; k++)
                {
                    double ang = 2 * Math.PI * k / seg;
                    var p = from + axis * along + (side * Math.Cos(ang) + up * Math.Sin(ang)) * r;
                    mesh.Positions.Add(new Point3D(p.X, p.Y, p.Z));
                }
            }

            mesh.Positions.Add(new Point3D(to.X, to.Y, to.Z));
            int tip = seg * 3;
            for (int k = 0; k < seg; k++)
            {
                int k1 = (k + 1) % seg;
                Tri(mesh, k, k1, seg + k1);
                Tri(mesh, k, seg + k1, seg + k);
                Tri(mesh, 2 * seg + k, 2 * seg + k1, tip);
            }

            var brush = new SolidColorBrush(color);
            var mat = new MaterialGroup();
            mat.Children.Add(new DiffuseMaterial(brush));
            mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2)))));
            return new GeometryModel3D(mesh, mat) { BackMaterial = mat };
        }

        private static ImageBrush MakeHeatBrush()
        {
            var px = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                var c = LinePlot.Heat(i / 255.0);
                px[4 * i] = c.B;
                px[4 * i + 1] = c.G;
                px[4 * i + 2] = c.R;
                px[4 * i + 3] = 255;
            }

            var bmp = BitmapSource.Create(256, 1, 96, 96, PixelFormats.Bgra32, null, px, 256 * 4);
            bmp.Freeze();
            var brush = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            brush.Freeze();
            return brush;
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
            var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = _world.Title + "-render.csv" };
            if (dlg.ShowDialog(this) != true) return;
            var ch = _result.Channels;
            using var w = new StreamWriter(dlg.FileName, false, new UTF8Encoding(true));
            w.WriteLine("t [s]," + string.Join(",", ch.Channels.Select(c => $"\"{c.Group} {c.Label} [{c.Unit}]\"")));
            for (int i = 0; i < ch.Count; i++)
                w.WriteLine(ch.Time(i).ToString("R", CultureInfo.InvariantCulture) + "," + string.Join(",", ch.Channels.Select(c => i < c.Count ? c[i].ToString("G9", CultureInfo.InvariantCulture) : string.Empty)));
            _status.Text = "CSV yazıldı: " + dlg.FileName;
        }

        /// <summary>Recorded poses at the current replay frame.</summary>
        private sealed class Playback : IScenePlayback
        {
            public RobotTwin.CoreSim.Analysis.Recording.ChannelStore? Store { get; set; }

            public int Index { get; set; }

            public (Vec3 Position, Quat Orientation)? PoseOf(string objectId) =>
                Store != null && AnalysisRun.RecordedPose(Store, objectId, Index) is { } p ? (p.Position, p.Orientation) : null;

            public double PortAngle(string objectId) =>
                Store != null && AnalysisRun.RecordedPose(Store, objectId, Index) is { } p ? p.Port : 0;
        }
    }
}
