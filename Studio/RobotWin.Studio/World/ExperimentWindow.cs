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
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [experiment] The experiment designer for the open world (CoreSim Automation/Experiment.cs): factors and their values,
    /// metrics and how each is reduced, repeats and seeds; runs it in the background with the app's own solvers and
    /// shows every combination's mean and 95 % confidence interval, the files written to Documents\RobotWin\Deneyler.
    /// </summary>
    internal sealed class ExperimentWindow : Window
    {
        private readonly WorldDocument _world;
        private readonly TextBox _name = new TextBox { Text = "Deney" };
        private readonly TextBox _duration = new TextBox { Text = "5" };
        private readonly TextBox _interval = new TextBox { Text = "10" };
        private readonly TextBox _repeats = new TextBox { Text = "3" };
        private readonly TextBox _seed = new TextBox { Text = "1" };
        private readonly CheckBox _tolerances = new CheckBox { Content = "Parça toleransları (her koşuda yeniden çekilir)" };
        private readonly CheckBox _firmware = new CheckBox { Content = "Kart yazılımları çalışsın", IsChecked = true };
        private readonly StackPanel _factors = new StackPanel();
        private readonly StackPanel _metrics = new StackPanel();
        private readonly ProgressBar _progress = new ProgressBar { Height = 6, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        private readonly TextBox _report = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 160 };
        private readonly Button _run;
        private readonly Button _openFolder;
        private readonly List<string> _targets;
        private readonly List<string> _sources;
        private CancellationTokenSource? _cancel;
        private string? _folder;
        private string? _outRoot;   // the snapshot's run writes to the temp folder, never to the user's documents

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        public ExperimentWindow(Window owner, WorldDocument world)
        {
            _world = world;
            Owner = owner;
            Title = "Deney";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Width = 980;
            Height = 760;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");
            _report.FontFamily = R<FontFamily>("F.Mono");
            _status.Style = R<Style>("Text.Caption");
            foreach (var c in new[] { _tolerances, _firmware }) c.Style = R<Style>("Switch");

            _targets = Targets(world);
            _sources = Sources(world);
            _name.Text = world.Title;

            var root = new DockPanel { Margin = new Thickness(22, 18, 22, 16) };
            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new TextBlock { Text = "Deney: parametre taraması", Style = R<Style>("Text.Title"), FontSize = 18 });
            head.Children.Add(new TextBlock
            {
                Text = "Etkenlerin her değer birleşimi, her biri kendi tohumuyla birkaç kez, pencere açılmadan ve uygulamanın çözücüleriyle çalışır; her ölçütün ortalaması ve %95 güven aralığı çıkar.",
                Style = R<Style>("Text.Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0)
            });
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var close = new Button { Content = "Kapat", Style = R<Style>("Btn.Ghost"), MinWidth = 96, IsCancel = true };
            close.Click += (_, _) => Close();
            _run = new Button { Content = "Çalıştır", Style = R<Style>("Btn.Primary"), MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
            _run.Click += async (_, _) => await RunOrCancel();
            _openFolder = new Button { Content = "Klasörü aç", Style = R<Style>("Btn.Ghost"), Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) };
            _openFolder.Click += (_, _) =>
            {
                if (_folder != null && Directory.Exists(_folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _folder, UseShellExecute = true });
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(_openFolder);
            buttons.Children.Add(close);
            buttons.Children.Add(_run);
            DockPanel.SetDock(buttons, Dock.Right);
            footer.Children.Add(buttons);
            var load = new Button { Content = "Deney aç…", Style = R<Style>("Btn.Ghost"), Margin = new Thickness(0, 0, 8, 0) };
            load.Click += (_, _) => LoadSpec();
            var save = new Button { Content = "Deneyi kaydet…", Style = R<Style>("Btn.Ghost") };
            save.Click += (_, _) => SaveSpec();
            var files = new StackPanel { Orientation = Orientation.Horizontal };
            files.Children.Add(load);
            files.Children.Add(save);
            footer.Children.Add(files);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var body = new StackPanel();
            var settings = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            settings.Children.Add(Field("Ad", _name, 220));
            settings.Children.Add(Field("Süre (s)", _duration, 80));
            settings.Children.Add(Field("Örnekleme (ms)", _interval, 90));
            settings.Children.Add(Field("Tekrar", _repeats, 70));
            settings.Children.Add(Field("İlk tohum", _seed, 70));
            body.Children.Add(settings);
            var switches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            _tolerances.Margin = new Thickness(0, 0, 18, 0);
            switches.Children.Add(_tolerances);
            switches.Children.Add(_firmware);
            body.Children.Add(switches);

            body.Children.Add(new TextBlock { Text = "Etkenler", Style = R<Style>("Text.Overline"), Margin = new Thickness(0, 4, 0, 2) });
            body.Children.Add(new TextBlock { Text = "Değerler noktalı virgülle (9.81; 1.62) ya da aralık ve adım sayısıyla (0.5..1.5/3).", Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 0, 0, 4) });
            body.Children.Add(_factors);
            var addFactor = new Button { Content = "Etken ekle", Style = R<Style>("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
            addFactor.Click += (_, _) => AddFactor(new ExperimentFactor { Target = _targets.FirstOrDefault() ?? "environment.AmbientC", Values = { "25" } });
            body.Children.Add(addFactor);

            body.Children.Add(new TextBlock { Text = "Ölçütler", Style = R<Style>("Text.Overline"), Margin = new Thickness(0, 4, 0, 4) });
            body.Children.Add(_metrics);
            var addMetric = new Button { Content = "Ölçüt ekle", Style = R<Style>("Btn.Ghost"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
            addMetric.Click += (_, _) => AddMetric(new ExperimentMetric { Source = _sources.FirstOrDefault() ?? "", Reduce = "final" });
            body.Children.Add(addMetric);

            body.Children.Add(new TextBlock { Text = "Sonuç", Style = R<Style>("Text.Overline"), Margin = new Thickness(0, 4, 0, 4) });
            body.Children.Add(_report);
            body.Children.Add(_progress);
            body.Children.Add(_status);
            root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            Content = new Border
            {
                Margin = new Thickness(20), Background = R<Brush>("B.Overlay"), BorderBrush = R<Brush>("B.BorderStrong"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Child = root
            };
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");

            // A first factor and metric to start from: the air's temperature, and the first moving thing's travel.
            AddFactor(new ExperimentFactor { Target = "environment.AmbientC", Values = { "0", "25", "40" } });
            AddMetric(new ExperimentMetric { Source = _sources.FirstOrDefault(s => s.EndsWith(".distance", StringComparison.Ordinal)) ?? _sources.FirstOrDefault() ?? "", Reduce = "final" });
        }

        /// <summary>The snapshot "…:experiment run": a one-second run of three temperatures, two seeds each.</summary>
        internal void RunForSnapshot()
        {
            _duration.Text = "1";
            _repeats.Text = "2";
            _outRoot = Path.Combine(Path.GetTempPath(), "robotwin-experiment-snapshot");
            _ = RunOrCancel();
        }

        private static FrameworkElement Field(string label, TextBox box, double width)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            sp.Children.Add(new TextBlock { Text = label, Style = R<Style>("Text.Caption"), Margin = new Thickness(0, 0, 0, 2) });
            box.Width = width;
            sp.Children.Add(box);
            return sp;
        }

        /// <summary>What a factor can set in this world.</summary>
        private static List<string> Targets(WorldDocument w)
        {
            var list = new List<string> { "environment.AmbientC", "environment.Gravity", "environment.Friction", "environment.WindX", "environment.WindY", "environment.Gust", "environment.WallHeight" };
            var lib = PartLibrary.Default;
            foreach (var o in w.Objects)
            {
                string n = string.IsNullOrWhiteSpace(o.Name) ? o.Id : o.Name;
                list.AddRange(new[] { n + ".x", n + ".y", n + ".z", n + ".yaw", n + ".mass" });
                if (o.PartId != null && lib.Find(o.PartId) is { } def)
                    list.AddRange(def.Parameters.Select(p => n + "." + p.Key));
            }

            return list.Distinct().ToList();
        }

        /// <summary>What a metric can read in this world.</summary>
        private static List<string> Sources(WorldDocument w)
        {
            var list = new List<string>();
            foreach (var o in w.Objects.Where(o => !o.IsStatic))
            {
                string n = string.IsNullOrWhiteSpace(o.Name) ? o.Id : o.Name;
                list.AddRange(new[] { "distance", "x", "y", "z", "speed", "roll", "pitch", "yaw", "temperature" }.Select(k => n + "." + k));
            }

            return list;
        }

        private void AddFactor(ExperimentFactor f)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var target = Choice(_targets, f.Target);
            string values = f.Values.Count > 0 ? string.Join("; ", f.Values)
                : f.From is double a && f.To is double b ? string.Format(CultureInfo.InvariantCulture, "{0}..{1}/{2}", a, b, f.Steps) : "";
            var box = new TextBox { Text = values, Margin = new Thickness(8, 0, 8, 0) };
            var remove = new Button { Content = "Kaldır", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(8, 2, 8, 2) };
            remove.Click += (_, _) => _factors.Children.Remove(row);
            DockPanel.SetDock(target, Dock.Left);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(target);
            row.Children.Add(remove);
            row.Children.Add(box);
            row.Tag = (Func<ExperimentFactor>)(() => ParseFactor(target.SelectedItem as string ?? "", box.Text));
            _factors.Children.Add(row);
        }

        private void AddMetric(ExperimentMetric m)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var source = Choice(_sources, m.Source);
            var reduce = new ComboBox { Width = 110, Margin = new Thickness(8, 0, 0, 0), ItemsSource = new[] { "final", "mean", "min", "max", "rms", "range" }, SelectedItem = m.Reduce };
            var name = new TextBox { Text = m.Name, Margin = new Thickness(8, 0, 8, 0), ToolTip = "Ölçütün adı (boş: kaynağı)" };
            var remove = new Button { Content = "Kaldır", Style = R<Style>("Btn.Ghost"), Padding = new Thickness(8, 2, 8, 2) };
            remove.Click += (_, _) => _metrics.Children.Remove(row);
            DockPanel.SetDock(source, Dock.Left);
            DockPanel.SetDock(reduce, Dock.Left);
            DockPanel.SetDock(remove, Dock.Right);
            row.Children.Add(source);
            row.Children.Add(reduce);
            row.Children.Add(remove);
            row.Children.Add(name);
            row.Tag = (Func<ExperimentMetric>)(() => new ExperimentMetric { Source = source.SelectedItem as string ?? "", Reduce = reduce.SelectedItem as string ?? "final", Name = name.Text.Trim() });
            _metrics.Children.Add(row);
        }

        /// <summary>
        /// A pick from what this world offers, with <paramref name="value"/> selected; a value the list lacks (a spec from
        /// another world, a node) joins the list first.
        /// </summary>
        private static ComboBox Choice(List<string> options, string value)
        {
            if (value.Length > 0 && !options.Contains(value)) options.Insert(0, value);
            return new ComboBox { Width = 300, ItemsSource = options.ToList(), SelectedItem = value.Length > 0 ? value : options.FirstOrDefault() };
        }

        /// <summary>"9.81; 1.62" as values, "0.5..1.5/3" as a range of three.</summary>
        private static ExperimentFactor ParseFactor(string target, string text)
        {
            var f = new ExperimentFactor { Target = target.Trim() };
            string t = text.Trim();
            int dots = t.IndexOf("..", StringComparison.Ordinal);
            if (dots > 0)
            {
                string rest = t.Substring(dots + 2);
                int slash = rest.IndexOf('/');
                f.From = Num(t.Substring(0, dots));
                f.To = Num(slash >= 0 ? rest.Substring(0, slash) : rest);
                f.Steps = slash >= 0 && int.TryParse(rest.Substring(slash + 1).Trim(), out int n) ? Math.Max(1, n) : 3;
            }
            else
            {
                f.Values.AddRange(t.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0));
            }

            return f;
        }

        private static double Num(string s) => double.Parse(s.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

        private ExperimentSpec Spec()
        {
            var spec = new ExperimentSpec
            {
                Name = string.IsNullOrWhiteSpace(_name.Text) ? "Deney" : _name.Text.Trim(),
                Duration = Num(_duration.Text),
                SampleInterval = Num(_interval.Text) / 1000,
                Repeats = Math.Max(1, (int)Num(_repeats.Text)),
                Seed = (int)Num(_seed.Text),
                Tolerances = _tolerances.IsChecked == true,
                Firmware = _firmware.IsChecked == true
            };
            spec.Factors.AddRange(_factors.Children.OfType<FrameworkElement>().Select(r => ((Func<ExperimentFactor>)r.Tag)()).Where(f => f.Target.Length > 0));
            spec.Metrics.AddRange(_metrics.Children.OfType<FrameworkElement>().Select(r => ((Func<ExperimentMetric>)r.Tag)()).Where(m => m.Source.Length > 0));
            return spec;
        }

        private void Show(ExperimentSpec spec)
        {
            var inv = CultureInfo.InvariantCulture;
            _name.Text = spec.Name;
            _duration.Text = spec.Duration.ToString("0.###", inv);
            _interval.Text = (spec.SampleInterval * 1000).ToString("0.###", inv);
            _repeats.Text = spec.Repeats.ToString(inv);
            _seed.Text = spec.Seed.ToString(inv);
            _tolerances.IsChecked = spec.Tolerances;
            _firmware.IsChecked = spec.Firmware;
            _factors.Children.Clear();
            _metrics.Children.Clear();
            foreach (var f in spec.Factors) AddFactor(f);
            foreach (var m in spec.Metrics) AddMetric(m);
        }

        private void LoadSpec()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Deney (*.json)|*.json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Show(ExperimentSpec.FromJson(File.ReadAllText(dlg.FileName)));
            }
            catch (Exception ex) when (ex is IOException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
            {
                _status.Text = Shell.Lang.T("Deney açılamadı: " + ex.Message);
            }
        }

        private void SaveSpec()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "Deney (*.json)|*.json", FileName = (_name.Text.Trim().Length > 0 ? _name.Text.Trim() : "deney") + ".json" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, Spec().ToJson(), new UTF8Encoding(false));
                _status.Text = Shell.Lang.T("Kaydedildi: " + dlg.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                _status.Text = Shell.Lang.T("Kaydedilemedi: " + ex.Message);
            }
        }

        private async Task RunOrCancel()
        {
            if (_cancel != null)
            {
                _cancel.Cancel();
                return;
            }

            ExperimentSpec spec;
            try
            {
                spec = Spec();
            }
            catch (FormatException)
            {
                _status.Text = Shell.Lang.T("Süre, örnekleme, tekrar ve tohum sayı olmalı.");
                return;
            }

            _cancel = new CancellationTokenSource();
            _run.Content = "Durdur";
            _progress.Visibility = Visibility.Visible;
            _progress.Value = 0;
            _report.Text = "";
            _status.Text = Shell.Lang.T("Çalışıyor…");
            var progress = new Progress<(int Done, int Total)>(p =>
            {
                _progress.Maximum = p.Total;
                _progress.Value = p.Done;
                _status.Text = Shell.Lang.T(string.Format(CultureInfo.InvariantCulture, "{0} / {1} koşu", p.Done, p.Total));
            });
            var world = _world;
            var token = _cancel.Token;
            try
            {
                var result = await Task.Run(() => ExperimentRunner.Run(spec, world, progress, token));
                _report.Text = Shell.Lang.T(Summary(result));
                Save(result);
            }
            catch (OperationCanceledException)
            {
                _status.Text = Shell.Lang.T("Durduruldu.");
            }
            catch (ArgumentException ex)
            {
                _status.Text = Shell.Lang.T(ex.Message);
            }
            finally
            {
                _cancel.Dispose();
                _cancel = null;
                _run.Content = "Çalıştır";
                _progress.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>Each metric, each combination: mean ± 95 % CI (n).</summary>
        private static string Summary(ExperimentResult r)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            for (int m = 0; m < r.Metrics.Count; m++)
            {
                sb.Append(r.Metrics[m]).Append('\n');
                for (int c = 0; c < r.Combinations.Count; c++)
                {
                    var s = r.StatOf(c, m);
                    string levels = r.Factors.Count == 0 ? "—" : string.Join(" · ", r.Factors.Select((f, i) => f + " = " + r.Combinations[c][i]));
                    sb.Append("  ").Append(levels).Append(": ").Append(s.Mean.ToString("G5", inv));
                    if (s.N > 1) sb.Append(" ± ").Append(s.Ci95.ToString("G3", inv));
                    sb.Append(" (n ").Append(s.N).Append(")\n");
                }
            }

            int failed = r.Runs.Count(x => x.Error.Length > 0);
            if (failed > 0) sb.Append('\n').Append(failed).Append(" koşu hata verdi: ").Append(r.Runs.First(x => x.Error.Length > 0).Error).Append('\n');
            return sb.ToString();
        }

        private void Save(ExperimentResult r)
        {
            string stem = string.Concat(r.Spec.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            string folder = Path.Combine(_outRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotWin", Shell.Lang.T("Deneyler")),
                stem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            try
            {
                Directory.CreateDirectory(folder);
                var utf8 = new UTF8Encoding(false);
                File.WriteAllText(Path.Combine(folder, "runs.csv"), r.RunsCsv(), utf8);
                File.WriteAllText(Path.Combine(folder, "summary.csv"), r.SummaryCsv(), utf8);
                File.WriteAllText(Path.Combine(folder, "report.md"), r.Markdown(_world.Title), utf8);
                File.WriteAllText(Path.Combine(folder, "experiment.json"), r.Spec.ToJson(), utf8);
                _world.Save(Path.Combine(folder, "world" + WorldDocument.Extension));   // the world as run, to repeat it
                _folder = folder;
                _openFolder.Visibility = Visibility.Visible;
                _status.Text = Shell.Lang.T($"{r.Runs.Count} koşu, {r.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s: {folder}");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _status.Text = Shell.Lang.T("Sonuç yazılamadı: " + ex.Message);
            }
        }
    }
}
