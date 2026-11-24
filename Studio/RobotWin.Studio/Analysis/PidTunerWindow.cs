using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Analysis
{
    /// <summary>
    /// [pid] PID tuning of a motor's speed loop, in three steps on a copy of the world:
    /// <list type="number">
    /// <item>a PWM step identifies the loop as a first-order-plus-dead-time model (the motor, its gearbox and whatever it drives);</item>
    /// <item>the classic rules give gains, each with the model's predicted response;</item>
    /// <item>the chosen gains run in the full physics, so the real response is seen next to the predicted one, and the code is copied.</item>
    /// </list>
    /// Units are the firmware's: PWM counts (0–255) in, output-shaft rpm out (CoreSim Robotics/PidTuning.cs, MotorBench.cs).
    /// </summary>
    public sealed class PidTunerWindow : Window
    {
        private readonly WorldDocument _world;
        private readonly IWorldGeometry _geometry;
        private readonly string _motorId;

        private readonly TextBox _supply = new TextBox { Width = 70, Text = "6" };
        private readonly TextBox _stepPwm = new TextBox { Width = 70, Text = "200" };
        private readonly TextBox _period = new TextBox { Width = 70, Text = "10" };
        private readonly TextBox _closedLoop = new TextBox { Width = 70, Text = "" };
        private readonly TextBox _setpoint = new TextBox { Width = 70, Text = "" };
        private readonly CheckBox _reverse = new CheckBox { Content = "İki yönlü sürücü (−255…255)", Margin = new Thickness(0, 4, 0, 8), ToolTip = "H köprüsü motoru geri de sürebiliyorsa: fren için negatif PWM kullanılır" };
        private readonly Button _runStep = new Button { Content = "Adım testini çalıştır", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 4, 0, 8) };
        private readonly TextBlock _model = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        private readonly ListBox _rules = new ListBox { Height = 210, Margin = new Thickness(0, 4, 0, 8) };
        private readonly Button _tryPhysics = new Button { Content = "Gerçek fizikte dene", Padding = new Thickness(12, 5, 12, 5), IsEnabled = false };
        private readonly Button _copy = new Button { Content = "Kodu kopyala", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0), IsEnabled = false };
        private readonly TextBlock _result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        private readonly LinePlot _stepPlot = new LinePlot { Title = "Adım testi: ölçülen hız ve model", XLabel = "t (s)" };
        private readonly LinePlot _loopPlot = new LinePlot { Title = "Kapalı çevrim: ayar noktası, modelin öngördüğü ve fizikte olan", XLabel = "t (s)" };
        private readonly LinePlot _pwmPlot = new LinePlot { Title = "PWM komutu", XLabel = "t (s)" };

        private List<(double T, double Y)>? _step;
        private Fopdt? _fit;
        private Fopdt _plant;
        private double _stepCommand;
        private List<PidRule> _ruleList = new List<PidRule>();
        private LoopResponse? _real;
        private bool _busy;

        public PidTunerWindow(WorldDocument world, IWorldGeometry geometry, string motorId, double supplyVolts)
        {
            _world = world;
            _geometry = geometry;
            _motorId = motorId;
            string name = world.Find(motorId)?.Name ?? motorId;
            Title = "PID ayarlayıcı — " + name;
            Width = 1280;
            Height = 820;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Res("B.Surface");
            Foreground = Res("B.Text");
            _supply.Text = supplyVolts.ToString("0.##", CultureInfo.InvariantCulture);
            _reverse.Style = (Style)Application.Current.FindResource("Switch");

            var root = new DockPanel { Background = Res("B.Surface") };
            var left = new StackPanel { Width = 330, Margin = new Thickness(14) };
            var scroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            DockPanel.SetDock(scroll, Dock.Left);
            root.Children.Add(scroll);

            left.Children.Add(Note($"{name} motorunun hız döngüsü. Deneyler dünyanın bir kopyasında, yazılım olmadan ve tam fizikle yapılır: motorun yükü, taktığınız tekerlek ve sürdüğü araçtır. Birimler yazılımınızınkiler: komut PWM (0–255), hız çıkış milinin dev/dk'sı."));
            left.Children.Add(Caption("1. Adım testi"));
            left.Children.Add(Row("Besleme (V)", _supply, "PWM 255'in motora verdiği gerilim: pil ya da sürücünün çıkışı"));
            left.Children.Add(Row("Test PWM'i", _stepPwm, "Duran motora birden verilen PWM; hız oturana kadar ölçülür"));
            left.Children.Add(_runStep);
            left.Children.Add(_model);

            left.Children.Add(Caption("2. Kural"));
            left.Children.Add(Row("Döngü periyodu (ms)", _period, "Yazılımınızda PID'in kaç milisaniyede bir çalıştığı; yarım periyot ölü zamana eklenir"));
            left.Children.Add(Row("Kapalı çevrim τc (ms)", _closedLoop, "SIMC ve IMC'nin ayar düğmesi: küçük = hızlı ama hassas, büyük = yavaş ama sağlam. Boş: kendiliğinden"));
            left.Children.Add(Row("Ayar noktası (dev/dk)", _setpoint, "Denenecek hız; boş: test hızının %60'ı"));
            left.Children.Add(_reverse);
            left.Children.Add(_rules);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(_tryPhysics);
            buttons.Children.Add(_copy);
            left.Children.Add(buttons);
            left.Children.Add(_result);
            left.Children.Add(_status);

            var plots = new Grid { Margin = new Thickness(0, 10, 14, 10) };
            plots.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            plots.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.4, GridUnitType.Star) });
            plots.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0.7, GridUnitType.Star) });
            foreach (var (plot, row) in new[] { (_stepPlot, 0), (_loopPlot, 1), (_pwmPlot, 2) })
            {
                var border = new Border { BorderBrush = Res("B.Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6), Child = plot };
                Grid.SetRow(border, row);
                plots.Children.Add(border);
            }

            root.Children.Add(plots);
            Content = root;

            _runStep.Click += async (_, _) => await RunStep();
            _rules.SelectionChanged += (_, _) => ShowPrediction();
            _period.LostFocus += (_, _) => Retune();
            _closedLoop.LostFocus += (_, _) => Retune();
            _setpoint.LostFocus += (_, _) => ShowPrediction();
            _reverse.Click += (_, _) => ShowPrediction();
            _tryPhysics.Click += async (_, _) => await TryPhysics();
            _copy.Click += (_, _) => CopyCode();
            _status.Text = "Adım testiyle başlayın.";
        }

        private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        private TextBlock Caption(string text) => new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("Text.Overline"), Margin = new Thickness(0, 12, 0, 6) };

        private TextBlock Note(string text) => new TextBlock { Text = text, Style = (Style)Application.Current.FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };

        private FrameworkElement Row(string label, Control box, string tip)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6), ToolTip = tip };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("B.TextSecondary") });
            Grid.SetColumn(box, 1);
            g.Children.Add(box);
            return g;
        }

        private static double Num(TextBox b, double fallback) =>
            double.TryParse(b.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v) ? v : fallback;

        private double Period => Math.Max(0.001, Num(_period, 10) / 1000);
        private double UMin => _reverse.IsChecked == true ? -255 : 0;
        private const double UMax = 255;

        private double Setpoint => _setpoint.Text.Trim().Length > 0 ? Num(_setpoint, 0) : 0.6 * (_step?[^1].Y ?? 0);

        private static string Ms(double s) => (s * 1000).ToString(s < 0.1 ? "0.#" : "0", CultureInfo.CurrentCulture) + " ms";

        private static string Pct(double v) => "%" + v.ToString("0.#", CultureInfo.CurrentCulture);

        private static string G(double v) => double.IsInfinity(v) ? "∞" : v.ToString("G3", CultureInfo.CurrentCulture);

        private void Busy(bool on, string text)
        {
            _busy = on;
            _runStep.IsEnabled = !on;
            _tryPhysics.IsEnabled = !on && _fit != null;
            _status.Text = text;
        }

        // ================================================================ 1. step test

        private async Task RunStep()
        {
            if (_busy) return;
            double supply = Math.Max(0.5, Num(_supply, 6));
            _stepCommand = Math.Max(10, Math.Min(255, Num(_stepPwm, 200)));
            var bench = new MotorBench(_world, _geometry, _motorId, supply);
            Busy(true, "Adım testi çalışıyor (dünyanın kopyasında, tam fizikle)…");
            try
            {
                _step = await Task.Run(() => bench.StepTest(_stepCommand));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is ApiException)
            {
                Busy(false, "Adım testi yapılamadı: " + ex.Message);
                return;
            }

            _fit = PidTuning.Identify(_step, _stepCommand);
            if (_fit == null)
            {
                Busy(false, "Hız oturmadı (ya da hiç değişmedi): birinci dereceden model kurulamıyor. Motor bir şeye takılı mı, besleme doğru mu?");
                DrawStep();
                return;
            }

            var m = _fit.Value;
            _model.Text = $"Model: K = {G(m.Gain)} dev/dk / PWM · τ = {Ms(m.TimeConstant)} · θ = {Ms(m.DeadTime)}. "
                          + $"PWM {_stepCommand:0} ile {_step[^1].Y.ToString("0", CultureInfo.CurrentCulture)} dev/dk'ya {Ms(_step[^1].T)} içinde oturdu.";
            Retune();
            DrawStep();
            Busy(false, "Bir kural seçin; grafikte modelin öngördüğü yanıt görünür. Sonra gerçek fizikte deneyin.");
        }

        private void DrawStep()
        {
            _stepPlot.Series.Clear();
            if (_step == null) return;
            _stepPlot.Series.Add(new PlotSeries { Name = "Ölçülen hız", Unit = "dev/dk", X = _step.Select(s => s.T).ToArray(), Y = _step.Select(s => s.Y).ToArray(), Color = LinePlot.Palette[0] });
            if (_fit is { } m)
            {
                var xs = _step.Select(s => s.T).ToArray();
                _stepPlot.Series.Add(new PlotSeries { Name = "Model", Unit = "dev/dk", X = xs, Y = xs.Select(t => _step[0].Y + m.Step(t, _stepCommand)).ToArray(), Color = LinePlot.Palette[1] });
            }

            _stepPlot.Refresh();
        }

        // ================================================================ 2. rules

        private void Retune()
        {
            if (_fit is not { } m) return;
            // The sample-and-hold of a loop running every period adds half a period of dead time.
            _plant = new Fopdt(m.Gain, m.TimeConstant, m.DeadTime + Period / 2);
            double? tc = _closedLoop.Text.Trim().Length > 0 ? Num(_closedLoop, 0) / 1000 : null;
            // With hardly any dead time the default asks for a loop a third as slow as the motor (and a few periods).
            tc ??= Math.Max(_plant.DeadTime, Math.Max(0.3 * _plant.TimeConstant, 3 * Period));
            _ruleList = PidTuning.Rules(_plant, tc);
            int keep = Math.Max(0, _rules.SelectedIndex);
            _rules.Items.Clear();
            double sp = Setpoint;
            foreach (var r in _ruleList)
            {
                var pred = PidTuning.Simulate(_plant, r.Gains, sp, Horizon(), Period, UMin, UMax);
                var item = new StackPanel { Margin = new Thickness(2, 3, 2, 3) };
                item.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold });
                item.Children.Add(new TextBlock { Text = $"Kp {G(r.Gains.Kp)} · Ki {G(r.Gains.Ki)} · Kd {G(r.Gains.Kd)}", FontFamily = (FontFamily)Application.Current.FindResource("F.Mono"), FontSize = 11.5 });
                item.Children.Add(new TextBlock { Text = $"model: yükselme {Ms(pred.RiseTime)}, aşma {Pct(pred.Overshoot)}, oturma {(double.IsNaN(pred.SettlingTime) ? "—" : Ms(pred.SettlingTime))}", Style = (Style)Application.Current.FindResource("Text.Caption") });
                item.Children.Add(new TextBlock { Text = r.Note, Style = (Style)Application.Current.FindResource("Text.Caption"), TextWrapping = TextWrapping.Wrap });
                _rules.Items.Add(new ListBoxItem { Content = item, Tag = r });
            }

            _rules.SelectedIndex = Math.Min(keep, _rules.Items.Count - 1);
            _tryPhysics.IsEnabled = !_busy;
            _copy.IsEnabled = true;
            ShowPrediction();
        }

        private double Horizon() => Math.Max(0.3, Math.Min(5, 6 * (_plant.TimeConstant + _plant.DeadTime)));

        private PidRule? Chosen => (_rules.SelectedItem as ListBoxItem)?.Tag as PidRule;

        private void ShowPrediction()
        {
            _real = null;
            _result.Text = string.Empty;
            DrawLoop(null);
        }

        private void DrawLoop(LoopResponse? real)
        {
            _loopPlot.Series.Clear();
            _pwmPlot.Series.Clear();
            if (Chosen is not { } rule || _fit == null)
            {
                _loopPlot.Refresh();
                _pwmPlot.Refresh();
                return;
            }

            double sp = Setpoint;
            var pred = PidTuning.Simulate(_plant, rule.Gains, sp, Horizon(), Period, UMin, UMax);
            var xs = pred.Trace.Select(p => p.T).ToArray();
            _loopPlot.Series.Add(new PlotSeries { Name = "Ayar noktası", Unit = "dev/dk", X = new[] { 0, xs[^1] }, Y = new[] { sp, sp }, Color = Color.FromRgb(0x8E, 0x8E, 0x97) });
            _loopPlot.Series.Add(new PlotSeries { Name = "Model", Unit = "dev/dk", X = xs, Y = pred.Trace.Select(p => p.Y).ToArray(), Color = LinePlot.Palette[1] });
            _pwmPlot.Series.Add(new PlotSeries { Name = "PWM (model)", Unit = "", X = xs, Y = pred.Trace.Select(p => p.U).ToArray(), Color = LinePlot.Palette[1] });
            if (real != null)
            {
                var rx = real.Trace.Select(p => p.T).ToArray();
                _loopPlot.Series.Add(new PlotSeries { Name = "Fizik", Unit = "dev/dk", X = rx, Y = real.Trace.Select(p => p.Y).ToArray(), Color = LinePlot.Palette[0] });
                _pwmPlot.Series.Add(new PlotSeries { Name = "PWM (fizik)", Unit = "", X = rx, Y = real.Trace.Select(p => p.U).ToArray(), Color = LinePlot.Palette[0] });
            }

            _loopPlot.Refresh();
            _pwmPlot.Refresh();
        }

        // ================================================================ 3. the physics and the code

        private async Task TryPhysics()
        {
            if (_busy || Chosen is not { } rule || _fit == null) return;
            double supply = Math.Max(0.5, Num(_supply, 6));
            var bench = new MotorBench(_world, _geometry, _motorId, supply);
            double sp = Setpoint, horizon = Horizon(), period = Period, umin = UMin;
            Busy(true, $"{rule.Name} gerçek fizikte deneniyor…");
            try
            {
                _real = await Task.Run(() => bench.ClosedLoop(rule.Gains, sp, horizon, period, umin, UMax));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is ApiException)
            {
                Busy(false, "Deneme yapılamadı: " + ex.Message);
                return;
            }

            var pred = PidTuning.Simulate(_plant, rule.Gains, sp, horizon, period, umin, UMax);
            var sb = new StringBuilder();
            sb.AppendLine($"{rule.Name}, {sp.ToString("0", CultureInfo.CurrentCulture)} dev/dk:");
            sb.AppendLine($"Model : yükselme {Ms(pred.RiseTime)}, aşma {Pct(pred.Overshoot)}, oturma {(double.IsNaN(pred.SettlingTime) ? "—" : Ms(pred.SettlingTime))}, kalıcı hata {Pct(Math.Abs(pred.SteadyError))}");
            sb.Append($"Fizik : yükselme {Ms(_real.RiseTime)}, aşma {Pct(_real.Overshoot)}, oturma {(double.IsNaN(_real.SettlingTime) ? "—" : Ms(_real.SettlingTime))}, kalıcı hata {Pct(Math.Abs(_real.SteadyError))}");
            if (_real.Saturated > 0.3) sb.Append($"\nPWM zamanın {Pct(_real.Saturated * 100)}'inde sınırda: bu hız için motor ya da besleme yetmiyor olabilir.");
            if (_real.Overshoot > pred.Overshoot + 15) sb.Append("\nFizikteki aşma modelden çok fazla: yük doğrusal değil (sürtünme, boşluk). Daha büyük bir τc deneyin.");
            _result.Text = sb.ToString();
            DrawLoop(_real);
            Busy(false, "Beğendiyseniz kodu kopyalayın; beğenmediyseniz τc'yi ya da kuralı değiştirip yeniden deneyin.");
        }

        private void CopyCode()
        {
            if (Chosen is not { } rule) return;
            try
            {
                Clipboard.SetText(PidTuning.ArduinoCode(rule.Gains, Period, UMin, UMax, rule.Name));
                _status.Text = "PID kodu panoya kopyalandı: y ölçtüğünüz dev/dk, dönen değer analogWrite'a giden PWM.";
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                _status.Text = "Pano şu an kullanılamıyor; yeniden deneyin.";
            }
        }

        /// <summary>For a snapshot: runs the step test and the first rule in the physics, synchronously.</summary>
        public async Task RunAllForSnapshot()
        {
            await RunStep();
            if (_fit != null) await TryPhysics();
        }
    }
}
