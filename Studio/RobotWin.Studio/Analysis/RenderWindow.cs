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
    public sealed partial class RenderWindow : Window
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
        private readonly CheckBox _aero = new CheckBox { Content = "Hava akışı (3B, zamanla)", IsChecked = true, ToolTip = "Dünyadaki gerçek hava: rüzgâr ve parçaların kendi hareketi havayı iter. Hava durgunsa ve parçalar kıpırdamıyorsa akış yoktur." };
        private readonly CheckBox _tunnel = new CheckBox { Content = "Rüzgâr tüneli testi", IsChecked = false, ToolTip = "Dünyada olmayan sabit bir hava: montaj birkaç anda kayıttaki duruşunda tutulur ve seçilen hız ve yönde hava üflenir (sürükleme katsayısı, ızgara belirsizliği)." };
        private readonly TextBox _aeroSpeed = new TextBox { Width = 70, Text = "2" };
        private readonly TextBox _aeroDir = new TextBox { Width = 70, Text = "0" };
        private readonly TextBox _aeroElev = new TextBox { Width = 70, Text = "0" };
        private readonly CheckBox _thermal = new CheckBox { Content = "Isınma (kartlar, motorlar)", IsChecked = true };
        private readonly CheckBox _meshCheck = new CheckBox { Content = "Ağ yakınsaması denetimi", IsChecked = false, ToolTip = "Her parçanın en kötü anı 1,4 kat daha ince ağla yeniden çözülür ve gerilmenin ne kadar değiştiği yazılır (%10'dan fazlası: sonuç ağa duyarlı)." };
        private readonly CheckBox _bands = new CheckBox { Content = "Belirsizlik bandı (4 tolerans koşusu)", IsChecked = false, ToolTip = "Parça değerleri tolerans bantlarından (klonlar ±%35–40) çekilerek 4 kez daha çalıştırılır; grafiklerde gerçek parçaların düşebileceği aralık gölge olarak görünür. Render süresi ~5 katına çıkar." };
        private readonly Button _render = new Button { Content = "Render al", Padding = new Thickness(14, 6, 14, 6) };
        private readonly ProgressBar _progress = new ProgressBar { Height = 6, Maximum = 1, Margin = new Thickness(0, 10, 0, 4) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly Button _csv = new Button { Content = "Tüm kanallar CSV", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Button _save = new Button { Content = "Kaydet (.rwanalysis)", IsEnabled = false, Margin = new Thickness(0, 6, 0, 0) };
        private readonly Button _open = new Button { Content = "Kayıtlı render'ı aç…", Margin = new Thickness(0, 6, 0, 0), ToolTip = "Bu dünya için daha önce kaydedilmiş bir render'ı (.rwanalysis) yeniden hesaplamadan oynatır." };

        // timeline
        private readonly Button _play = new Button { Content = "▶", Width = 34 };
        private readonly ComboBox _speed = new ComboBox { Width = 72 };
        private readonly Slider _timeline = new Slider { Minimum = 0, Maximum = 1, IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _time = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Consolas") };
        private readonly ComboBox _mode = new ComboBox { Width = 210 };
        private readonly ComboBox _scale = new ComboBox { Width = 220 };
        private readonly CheckBox _forces = new CheckBox { Content = "Kuvvetler", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _air = new CheckBox { Content = "Hava", IsChecked = false, VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _follow = new CheckBox { Content = "Kamera takip", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _legend = new TextBlock { Margin = new Thickness(10), Foreground = Brushes.White, FontSize = 12, IsHitTestVisible = false };

        // results
        private readonly TabControl _tabs = new TabControl { Height = 250 };
        private readonly ListBox _channels = new ListBox { SelectionMode = SelectionMode.Multiple };
        private readonly TextBox _filter = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        private readonly LinePlot _plot = new LinePlot();
        private readonly List<BenchSeries> _bench = new List<BenchSeries>();
        private readonly ComboBox _benchPick = new ComboBox { MinWidth = 160, Margin = new Thickness(6, 0, 6, 0) };
        private readonly TextBlock _benchResult = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
        private BenchComparison? _benchLast;
        private BenchSeries? _benchShown;
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
            AnalysisRun.Esp32Builder ??= RobotWin.Studio.Automation.StudioApiHost.BuildEsp32;   // ESP32 boards run their firmware in the render too
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
            _scene.ReadOnly = true;   // a replay shows what was simulated: nothing in it can be moved or rewired

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
            AttachThermalHover(view);   // [thermal] Analysis/RenderWindow.Thermal.cs
            center.Children.Add(view);
            var bar = BuildTimeline();
            Grid.SetRow(bar, 1);
            center.Children.Add(bar);
            BuildTabs();
            Grid.SetRow(_tabs, 2);
            center.Children.Add(_tabs);
            root.Children.Add(center);
            Content = root;

            foreach (var cb in new[] { _aero, _tunnel, _thermal, _bands, _meshCheck, _forces, _air, _follow }) cb.Foreground = Res("B.Text");
            _timer.Tick += (_, _) => Tick();
            Closed += (_, _) =>
            {
                _timer.Stop();
                _cancel?.Cancel();
            };
            _legend.Text = "Render alınınca kayıt burada saniye saniye oynatılır.";
        }

        public AnalysisResult? Result => _result;

        /// <summary>Colour mode of the replay (0 stress, 1 none, 2 speed, 3 temperature, 4 electrical).</summary>
        public void SetColourMode(int mode) => _mode.SelectedIndex = Math.Max(0, Math.Min(_mode.Items.Count - 1, mode));

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
            int ra = Array.IndexOf(Environment.GetCommandLineArgs(), "--render-accuracy");   // snapshot runs
            if (ra >= 0 && ra + 1 < Environment.GetCommandLineArgs().Length && Enum.TryParse<AccuracyPreset>(Environment.GetCommandLineArgs()[ra + 1], out var acc)) _accuracy.SelectedIndex = (int)acc;
            _accuracy.MinWidth = 150;
            left.Children.Add(Row("Doğruluk", _accuracy));
            left.Children.Add(Row("Gerilme kare hızı (fps)", _stressFps));
            left.Children.Add(Caption("Gerilmesi hesaplanacak parçalar"));
            // Every physical part: bodies and models, and components (servos, motors, boards) too, which carry the
            // loads of what is mounted on them. A fixed part counts when something is fastened to it.
            bool Jointed(WorldObject o) => _world.Joints.Any(j => j.ObjectA == o.Id || j.ObjectB == o.Id) || _world.MountedOn(o.Id).Any();
            foreach (var o in _world.Objects.Where(o => (o.Kind == WorldObjectKind.Body || o.Kind == WorldObjectKind.Model || o.Kind == WorldObjectKind.Component)
                                                        && !WorldPhysics.IsLiquid(o) && (!o.IsStatic || Jointed(o))))
            {
                bool on = o.Kind != WorldObjectKind.Component ? !o.IsStatic : Jointed(o);
                _parts.Children.Add(new CheckBox { Content = o.Name, Tag = o.Id, IsChecked = on, Foreground = Foreground, Margin = new Thickness(0, 1, 0, 1) });
            }
            if (_parts.Children.Count == 0) _parts.Children.Add(new TextBlock { Text = "(gerilme taşıyan parça yok)", Style = (Style)Application.Current.FindResource("Text.Caption") });
            left.Children.Add(_parts);
            left.Children.Add(new Border { Child = _aero, Margin = new Thickness(0, 10, 0, 0) });
            var tunnel = new StackPanel { Margin = new Thickness(18, 2, 0, 0), Visibility = Visibility.Collapsed };
            tunnel.Children.Add(Row("Hava hızı (m/s)", _aeroSpeed));
            tunnel.Children.Add(Row("Estiği yön (°, 0 = +X, 90 = +Y)", _aeroDir));
            tunnel.Children.Add(Row("Eğim (°, + yukarı)", _aeroElev));
            _tunnel.Margin = new Thickness(18, 4, 0, 0);
            _tunnel.Checked += (_, _) => tunnel.Visibility = Visibility.Visible;
            _tunnel.Unchecked += (_, _) => tunnel.Visibility = Visibility.Collapsed;
            _aero.Checked += (_, _) => _tunnel.IsEnabled = true;
            _aero.Unchecked += (_, _) => _tunnel.IsEnabled = false;
            left.Children.Add(_tunnel);
            left.Children.Add(tunnel);
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
            _open.Click += (_, _) => Open();
            left.Children.Add(_csv);
            left.Children.Add(_save);
            left.Children.Add(_open);
        }

        private static double Num(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        private static double Any(TextBox box, double fallback) =>
            double.TryParse(box.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) && !double.IsInfinity(v) ? v : fallback;

        /// <summary>[replay] Inputs of the recorded live run; the render feeds them back at their times.</summary>
        public RobotTwin.CoreSim.Automation.InputJournal? Inputs { get; set; }

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
                AeroTunnel = _tunnel.IsChecked == true,
                AeroSpeed = Num(_aeroSpeed, 2),
                AeroDirectionDeg = Any(_aeroDir, 0),
                AeroElevationDeg = Math.Max(-90, Math.Min(90, Any(_aeroElev, 0))),
                AeroResolution = 18,
                AeroPasses = 15,
                Thermal = _thermal.IsChecked == true,
                ThermalExtend = 0,
                ThermalFrames = Math.Max(10, (int)(duration * 10)),
                UncertaintyRuns = _bands.IsChecked == true ? 4 : 0,
                StressMeshCheck = _meshCheck.IsChecked == true,
                Inputs = Inputs
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
            foreach (var m in new[] { "Renk: gerilme (von Mises)", "Renk: yok", "Renk: hız oku", "Renk: sıcaklık", "Renk: elektrik (akım, güç)" }) _mode.Items.Add(m);
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
                // Typing in a box (the channel search, settings) keeps its keys: Space, arrows, Home and End edit the text.
                if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase || e.OriginalSource is PasswordBox
                    || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase || Keyboard.FocusedElement is ComboBox { IsEditable: true }) return;
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
            var benchBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var import = new Button { Content = "Tezgâh ölçümü içe aktar (CSV)…", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Başlık satırı + sütunlar: ilk sütun zaman (s, ms veya µs; başlıktan anlaşılır), diğerleri ölçülen sinyaller (A, mA, V, mV, derece…). Örn. Arduino + INA219 ile servo akımı." };
            import.Click += (_, _) => ImportBench();
            var compare = new Button { Content = "Seçili kanalla karşılaştır", Padding = new Thickness(8, 2, 8, 2) };
            compare.Click += (_, _) => CompareBench();
            benchBar.Children.Add(import);
            benchBar.Children.Add(_benchPick);
            benchBar.Children.Add(compare);
            var plotArea = new DockPanel();
            DockPanel.SetDock(benchBar, Dock.Top);
            DockPanel.SetDock(_benchResult, Dock.Bottom);
            plotArea.Children.Add(benchBar);
            plotArea.Children.Add(_benchResult);
            plotArea.Children.Add(_plot);
            graphs.Children.Add(plotArea);
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
            _playback.Liquids = r.Liquids;
            _playback.Cables = r.Cables;
            if (r.AeroFrames.Count > 0) _air.IsChecked = true;   // the air was computed: show it
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

            foreach (var a in r.AeroFrames.Where(a => a.Notes.Count > 0)) log.AppendLine($"[akış t={a.Time:0.00} s] " + string.Join(" ", a.Notes));
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

            if (_benchShown != null && _benchLast != null)
            {
                // The measurement, shifted by the found lag so it lines up with the simulation.
                _plot.Series.Add(new PlotSeries { Name = "ölçüm: " + _benchShown.Name, Unit = _plot.Series.FirstOrDefault()?.Unit ?? string.Empty, X = _benchShown.T.Select(x => x - _benchLast.Lag).ToArray(), Y = _benchShown.V, Color = Color.FromRgb(0xF5, 0x9E, 0x0B) });
            }

            _plot.Cursor = _t;
            _plot.Refresh();
        }

        private void ImportBench()
        {
            var dlg = new OpenFileDialog { Filter = "Ölçüm kaydı (*.csv;*.txt)|*.csv;*.txt|Tüm dosyalar|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var series = BenchComparison.ParseCsv(File.ReadAllText(dlg.FileName));
                _bench.Clear();
                _bench.AddRange(series);
                _benchPick.Items.Clear();
                foreach (var b in series) _benchPick.Items.Add(new ComboBoxItem { Content = b.Name, Tag = b });
                if (_benchPick.Items.Count > 0) _benchPick.SelectedIndex = 0;
                _benchResult.Text = $"{Path.GetFileName(dlg.FileName)}: {series.Count} sinyal, {series.FirstOrDefault()?.T.Length ?? 0} örnek, {series.FirstOrDefault()?.T.LastOrDefault() ?? 0:0.###} s. Soldan karşılaştırılacak simülasyon kanalını seçin.";
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                _benchResult.Text = "Ölçüm okunamadı: " + ex.Message;
            }
        }

        private void CompareBench()
        {
            if (_result == null || _benchPick.SelectedItem is not ComboBoxItem { Tag: BenchSeries raw }) return;
            var item = _channels.SelectedItems.Cast<ListBoxItem>().FirstOrDefault();
            var c = item == null ? null : _result.Channels.Find((string)item.Tag);
            if (c == null)
            {
                _benchResult.Text = "Önce soldan bir simülasyon kanalı seçin (ör. servonun akımı).";
                return;
            }

            // mA / mV logs against A / V channels.
            double scale = raw.Name.IndexOf("mA", StringComparison.Ordinal) >= 0 && c.Unit == "A" || raw.Name.IndexOf("mV", StringComparison.Ordinal) >= 0 && c.Unit == "V" ? 1e-3 : 1;
            var meas = new BenchSeries { Name = raw.Name, T = raw.T, V = raw.V.Select(x => x * scale).ToArray() };
            _benchLast = BenchComparison.Compare(_result.Channels.TimeArray(), c.ToArray(), meas);
            _benchShown = meas;
            _benchResult.Text = $"{c.Label} ↔ {raw.Name}: " + _benchLast.Summary(c.Unit)
                + (Math.Abs(_benchLast.Gain - 1) > 0.15 ? $"  — gerçek parça modelden %{(_benchLast.Gain - 1) * 100:+0;-0} farklı; parça 'klon' mu, besleme gerilimi aynı mı?" : string.Empty);
            PlotSelected();
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
            _scene.SetTints(_mode.SelectedIndex == 4 ? ElectricTints(i, legend) : null);
            _scene.CableColor = _mode.SelectedIndex == 4 ? CableCurrentColor(i, legend) : null;
            ThermalCamera(_mode.SelectedIndex == 3, i, t, legend);   // [thermal] the models themselves coloured like a thermal camera
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
                var qb = q * st.BodyTurn;   // the body frame the mesh is in (a wheel stands on its edge)
                var mesh = new MeshGeometry3D();
                for (int n = 0; n < st.NodeCount; n++)
                {
                    var p = st.Node(n);
                    if (f.Displacement.Length >= 3 * st.NodeCount)
                        p += new Vec3(f.Displacement[3 * n], f.Displacement[3 * n + 1], f.Displacement[3 * n + 2]) * exaggerate;
                    var w = centre + qb.Rotate(p);
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

        /// <summary>Cable colours by recorded current at frame <paramref name="i"/> (dark blue no current, red the render's peak).</summary>
        private Func<WorldCable, Color?> CableCurrentColor(int i, StringBuilder legend)
        {
            var store = _result!.Channels;
            double peak = 1e-9;
            var now = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in _world.Cables)
            {
                if (store.Find("dev/cable:" + c.Id + ":I") is not { } ch) continue;
                peak = Math.Max(peak, ch.ToArray().Where(double.IsFinite).Select(Math.Abs).DefaultIfEmpty(0).Max());
                if (i < ch.Count && double.IsFinite(ch[i])) now[c.Id] = Math.Abs(ch[i]);
            }

            var top = now.OrderByDescending(kv => kv.Value).Take(3)
                .Select(kv => (_world.Cables.FirstOrDefault(x => x.Id == kv.Key) is { } cb ? CableName(cb) : kv.Key) + " " + SiValue.Format(kv.Value, "A"));
            legend.AppendLine($"Kablolar akımla renkli: koyu mavi akım yok, kırmızı {SiValue.Format(peak, "A")}; en yüklü: " + string.Join(", ", top));
            return c => now.TryGetValue(c.Id, out double a) ? LinePlot.Heat(0.08 + 0.92 * Math.Min(1, a / peak)) : (Color?)null;
        }

        private string CableName(WorldCable c) => $"{_world.Find(c.A.ObjectId)?.Name} {c.A.Pin} → {_world.Find(c.B.ObjectId)?.Name} {c.B.Pin}";

        /// <summary>Parts glow by the electrical power their devices dissipate at frame <paramref name="i"/>.</summary>
        private Dictionary<string, Color> ElectricTints(int i, StringBuilder legend)
        {
            var store = _result!.Channels;
            var power = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var peak = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in store.Channels)
            {
                if (!c.Name.StartsWith("dev/", StringComparison.Ordinal) || c.Unit != "W" || c.Name.StartsWith("dev/cable:", StringComparison.Ordinal)) continue;
                string key = c.Name.Substring(4);
                int slash = key.IndexOf('/');
                string obj = slash > 0 ? key.Substring(0, slash) : key.Split(':')[0];
                if (i < c.Count && double.IsFinite(c[i])) power[obj] = (power.TryGetValue(obj, out var p) ? p : 0) + Math.Abs(c[i]);
                peak[obj] = (peak.TryGetValue(obj, out var q) ? q : 0) + c.ToArray().Where(double.IsFinite).Select(Math.Abs).DefaultIfEmpty(0).Max();
            }

            double max = Math.Max(1e-6, peak.Values.DefaultIfEmpty(0).Max());
            var tints = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in power)
            {
                double k = Math.Min(1, kv.Value / max);
                var h = LinePlot.Heat(0.15 + 0.85 * k);
                tints[kv.Key] = Color.FromRgb((byte)(h.R * k), (byte)(h.G * k), (byte)(h.B * k));
            }

            var hot = power.OrderByDescending(kv => kv.Value).Take(3).Select(kv => (_world.Find(kv.Key)?.Name ?? kv.Key) + " " + SiValue.Format(kv.Value, "W"));
            legend.AppendLine("Parçalar harcadığı elektrik gücüyle parlıyor; en çok: " + string.Join(", ", hot));
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
                var qb = pose.Orientation * (st?.BodyTurn ?? Quat.Identity);
                foreach (var (p, f, kind) in ls.Loads)
                {
                    if (f.Length < _forceMax * 0.01) continue;
                    var wp = centre + qb.Rotate(p);
                    var wf = qb.Rotate(f);
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
            if (a.Tracers != null)
            {
                Smoke(g, a, legend);
                return;
            }

            // Wind tunnel: 3D streamlines through the solved flow, coloured by local speed.
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

            // The force arrow at the middle of the assembly's cells.
            double cx = 0, cy = 0, cz = 0;
            int cn = 0;
            int nx = a.Nx, ny = a.Ny, nz = a.Nz;
            if (a.Solid.Length == nx * ny * nz)
                for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    if (a.Solid[(z * ny + y) * nx + x])
                    {
                        cx += x;
                        cy += y;
                        cz += z;
                        cn++;
                    }

            if (cn > 0 && a.Force.Length > 0)
            {
                var centre = a.Origin + a.AxisX * ((cx / cn + 0.5) * a.Dx) + a.AxisY * ((cy / cn + 0.5) * a.Dx) + a.AxisZ * ((cz / cn + 0.5) * a.Dx);
                g.Children.Add(Arrow(centre, centre + a.Force.Normalized() * 0.08, Color.FromRgb(0xA8, 0x55, 0xF7), 0.0025));
            }

            legend.AppendLine($"Rüzgâr tüneli (t = {a.Time:0.00} s): {a.Speed:0.##} m/s hava, montaja kuvvet {SiValue.Format(a.Force.Length, "N")}, Cd {a.MeanCd:0.00} (mor ok); akım çizgileri: mavi yavaşlayan, sarı serbest akış, kırmızı hızlanan hava. Dünyada bu hava yoktur.");
        }

        /// <summary>
        /// [air] The real air as smoke: every particle the air carries is a short streak along its velocity, as a camera
        /// would see it in a 1/25 s exposure, coloured by speed. Still air leaves no streak.
        /// </summary>
        private void Smoke(Model3DGroup g, RobotTwin.CoreSim.Analysis.CfdResult a, StringBuilder legend)
        {
            var tr = a.Tracers!;
            int n = tr.Length / 6;
            if (n == 0)
            {
                legend.AppendLine($"Hava (t = {a.Time:0.00} s): bu anda hava durgun.");
                return;
            }

            var mesh = new MeshGeometry3D();
            double vmax = Math.Max(1e-6, a.Speed);
            double r = Math.Max(0.0008, Math.Min(0.0025, a.Dx * 0.12));
            const double exposure = 0.04;
            var pos = new Point3DCollection(n * 6);
            var uv = new PointCollection(n * 6);
            var idx = new Int32Collection(n * 18);
            for (int i = 0; i < n; i++)
            {
                var p = new Vec3(tr[6 * i], tr[6 * i + 1], tr[6 * i + 2]);
                var v = new Vec3(tr[6 * i + 3], tr[6 * i + 4], tr[6 * i + 5]);
                double sp = v.Length;
                var dir = sp > 1e-9 ? v / sp : Vec3.UnitX;
                double len = Math.Max(3 * r, sp * exposure);
                var tail = p - dir * len;
                var side = dir.AnyPerpendicular().Normalized();
                var up = dir.Cross(side).Normalized();
                double u = Math.Min(0.999, sp / vmax);
                int b0 = pos.Count;
                for (int k = 0; k < 3; k++)
                {
                    double ang = k * 2 * Math.PI / 3;
                    var off = (side * Math.Cos(ang) + up * Math.Sin(ang)) * r;
                    var h = p + off;
                    var tl = tail + off * 0.3;   // the streak thins out behind
                    pos.Add(new Point3D(h.X, h.Y, h.Z));
                    pos.Add(new Point3D(tl.X, tl.Y, tl.Z));
                    uv.Add(new Point(u, 0.5));
                    uv.Add(new Point(u, 0.5));
                }

                for (int k = 0; k < 3; k++)
                {
                    int h0 = b0 + 2 * k, t0 = h0 + 1, h1 = b0 + 2 * ((k + 1) % 3), t1 = h1 + 1;
                    idx.Add(h0);
                    idx.Add(t0);
                    idx.Add(t1);
                    idx.Add(h0);
                    idx.Add(t1);
                    idx.Add(h1);
                }
            }

            mesh.Positions = pos;
            mesh.TextureCoordinates = uv;
            mesh.TriangleIndices = idx;
            var mat = new MaterialGroup();
            mat.Children.Add(new DiffuseMaterial(HeatBrush));
            mat.Children.Add(new EmissiveMaterial(HeatBrush) { Color = Color.FromRgb(170, 170, 170) });
            g.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            legend.AppendLine($"Hava (t = {a.Time:0.00} s): havayla taşınan duman, {n} parçacık hareket ediyor; mavi yavaş, kırmızı {vmax:0.##} m/s. Havanın hareketli parçalara kuvveti {SiValue.Format(a.Force.Length, "N")}.");
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

        /// <summary>Shows a saved render of this world (its poses are matched to the world's objects by id).</summary>
        public void ShowSaved(AnalysisResult r)
        {
            // The settings column shows what the opened render was computed with, not this window's defaults.
            var s = r.Settings;
            _duration.Text = s.Duration.ToString("0.###", CultureInfo.InvariantCulture);
            _accuracy.SelectedIndex = (int)s.Accuracy;
            _aero.IsChecked = s.Aero;
            _tunnel.IsChecked = s.AeroTunnel;
            _aeroSpeed.Text = s.AeroSpeed.ToString("0.##", CultureInfo.InvariantCulture);
            _aeroDir.Text = s.AeroDirectionDeg.ToString("0.#", CultureInfo.InvariantCulture);
            _aeroElev.Text = s.AeroElevationDeg.ToString("0.#", CultureInfo.InvariantCulture);
            _thermal.IsChecked = s.Thermal;
            _meshCheck.IsChecked = s.StressMeshCheck;
            _bands.IsChecked = s.UncertaintyRuns > 0;
            foreach (var cb in _parts.Children.OfType<CheckBox>()) cb.IsChecked = s.StressObjects.Contains((string)cb.Tag);
            Show(r);
            if (!string.Equals(r.WorldTitle, _world.Title, StringComparison.Ordinal))
                _status.Text = $"Uyarı: bu render \"{r.WorldTitle}\" dünyasına ait; nesneler kimliklerine göre eşleştirildi.";
        }

        private void Open()
        {
            var dlg = new OpenFileDialog { Filter = "RobotWin analizi (*.rwanalysis)|*.rwanalysis" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                ShowSaved(AnalysisResult.Load(dlg.FileName));
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is System.Text.Json.JsonException)
            {
                _status.Text = "Açılamadı: " + ex.Message;
            }
        }

        private void Save()
        {
            if (_result == null) return;
            var dlg = new SaveFileDialog { Filter = "RobotWin analizi (*.rwanalysis)|*.rwanalysis", FileName = _world.Title + AnalysisResult.Extension };
            if (dlg.ShowDialog(this) != true) return;
            _result.Save(dlg.FileName);
            _status.Text = "Kaydedildi: " + dlg.FileName;
        }

        /// <summary>Saves the shown result to a file (snapshot runs: --render-save).</summary>
        public void SaveResult(string path) => _result?.Save(path);

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

            public double Value(string channel) =>
                Store?.Find(channel) is { } c && Index >= 0 && Index < c.Count ? c[Index] : double.NaN;

            public double Time => Store != null && Index >= 0 && Index < Store.Count ? Store.Time(Index) : 0;

            /// <summary>[liquid] Recorded liquids of the shown result.</summary>
            public System.Collections.Generic.List<RobotTwin.CoreSim.Analysis.LiquidTrack>? Liquids { get; set; }

            /// <summary>[render] Recorded cable ropes of the shown result.</summary>
            public System.Collections.Generic.List<RobotTwin.CoreSim.Analysis.CableTrack>? Cables { get; set; }

            public (System.Collections.Generic.IReadOnlyList<Vec3> Points, bool PlugA, bool PlugB)? CableAt(string cableId)
            {
                if (Cables == null) return null;
                foreach (var c in Cables)
                    if (string.Equals(c.CableId, cableId, System.StringComparison.OrdinalIgnoreCase))
                        return c.At(Time + 1e-9) is { } r ? (r.Points, r.PlugA, r.PlugB) : null;
                return null;
            }

            public (float[] Positions, double Spacing, string Kind, float[]? Velocities)? LiquidAt(string objectId)
            {
                if (Liquids == null) return null;
                foreach (var l in Liquids)
                {
                    if (!string.Equals(l.ObjectId, objectId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    int f = l.FrameAt(Time + 1e-9);
                    if (f < 0) return null;
                    var pos = l.Positions(f);
                    float[]? vel = null;
                    if (f > 0)
                    {
                        // Particles keep their numbers between frames: the move since the last frame is each one's velocity.
                        var prev = l.Positions(f - 1);
                        double dt = l.Times[f] - l.Times[f - 1];
                        if (prev.Length == pos.Length && dt > 0)
                        {
                            vel = new float[pos.Length];
                            for (int i = 0; i < pos.Length; i++) vel[i] = (float)((pos[i] - prev[i]) / dt);
                        }
                    }

                    return (pos, l.Spacing, l.Kind, vel);
                }

                return null;
            }
        }
    }
}
