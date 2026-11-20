using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// "Kod" page: write the firmware of each programmable board in the project,
    /// build it with the real toolchain and run it on the emulated board.
    /// </summary>
    public partial class CodePage : UserControl
    {
        private sealed class BoardItem
        {
            public BoardItem(SchematicPart part, PartDefinition def)
            {
                Part = part;
                Def = def;
            }

            public SchematicPart Part { get; }
            public PartDefinition Def { get; }
            public string? Fqbn => ArduinoCompiler.FqbnFor(Def.Id);
            public override string ToString() => $"{Part.RefDes} · {Def.Name}";
        }

        private BoardItem? _board;
        private bool _loading;
        private CancellationTokenSource? _build;
        private long _serialShown;

        public CodePage()
        {
            InitializeComponent();
            TemplateBox.ItemsSource = new[] { "Şablon seç…" }.Concat(SketchTemplates.All.Select(t => t.Name)).ToList();
            TemplateBox.SelectedIndex = 0;
            Editor.TextChanged += OnTextChanged;
            Editor.Box.SelectionChanged += (_, _) => CaretText.Text = $"Satır {Editor.CaretLine}, Sütun {Editor.CaretColumn}";
            W.DocumentReplaced += (_, _) => Dispatcher.BeginInvoke(new Action(RefreshBoards));
            W.DocumentChanged += (_, _) =>
            {
                if (IsVisible) RefreshBoards();
            };
            W.Simulation.SerialReceived += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateSerial));
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) UpdateSerial();
            };
            PreviewKeyDown += OnKeys;
        }

        private static Workspace W => Workspace.Current;

        public void OnShown()
        {
            RefreshBoards();
            UpdateSerial();
            Editor.Focus();
        }

        /// <summary>Used by --snapshot: builds (optionally with an injected error) and runs the current board's code.</summary>
        public void SnapshotBuildAndRun(string? fault)
        {
            if (fault == "syntax")
            {
                Editor.Text = Editor.Text.Replace("delay(500);", "delay(500)").Replace("Serial.begin", "Seriall.begin");
                OnTextChanged(this, EventArgs.Empty);
                OnBuild(this, new RoutedEventArgs());
            }
            else
            {
                OnUpload(this, new RoutedEventArgs());
            }
        }

        // ================================================================ boards and sources

        private void RefreshBoards()
        {
            var items = W.Schematic.Parts
                .Select(p => (Part: p, Def: W.Library.Find(p.PartId)))
                .Where(x => x.Def != null && x.Def.Symbol == SymbolKind.Board && ArduinoCompiler.FqbnFor(x.Def.Id) != null)
                .Select(x => new BoardItem(x.Part, x.Def!))
                .ToList();
            string? keep = _board?.Part.RefDes;
            _loading = true;
            BoardBox.ItemsSource = items;
            var select = items.FirstOrDefault(i => i.Part.RefDes == keep) ?? items.FirstOrDefault();
            BoardBox.SelectedItem = select;
            _loading = false;
            NoBoard.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (select == null)
            {
                _board = null;
                return;
            }

            if (_board == null || _board.Part != select.Part) Load(select);
        }

        private void OnBoardChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || BoardBox.SelectedItem is not BoardItem item) return;
            Load(item);
        }

        private void Load(BoardItem item)
        {
            _board = item;
            var fw = W.Project.FirmwareFor(item.Part.RefDes);
            _loading = true;
            Editor.Text = fw?.Source ?? InitialSource(item);
            Editor.Diagnostics = Array.Empty<CodeDiagnostic>();
            _loading = false;
            TargetText.Text = ArduinoCompiler.IsEsp32(item.Fqbn)
                ? "ESP32 · MSVC ile yerel derleme · Arduino-ESP32 API (GPIO, ADC, LEDC, I²C, FreeRTOS, Wi-Fi taklidi)"
                : $"{item.Fqbn} · avr-gcc (arduino-cli) · ATmega328P @ 16 MHz emülasyonu";
            UpdateBuildState(fw);
        }

        /// <summary>Starting point: the source of the board's current built-in firmware, or Blink.</summary>
        private static string InitialSource(BoardItem item)
        {
            if (item.Part.Parameters.TryGetValue("firmware", out var fw) && fw.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            {
                string ino = Path.Combine(AppContext.BaseDirectory, "Firmware", Path.ChangeExtension(fw.Substring(8), ".ino"));
                if (File.Exists(ino)) return File.ReadAllText(ino);
            }

            return ArduinoCompiler.IsEsp32(item.Fqbn) ? Esp32Blink : SketchTemplates.All[0].Source;
        }

        private const string Esp32Blink = @"// ESP32: GPIO2'deki LED'i yak-söndür, seri porta yaz.
const int LED = 2;

void setup() {
  Serial.begin(115200);
  pinMode(LED, OUTPUT);
  Serial.println(""ESP32 hazır"");
}

void loop() {
  digitalWrite(LED, HIGH);
  Serial.printf(""LED yandı, t=%lu ms\n"", millis());
  delay(500);
  digitalWrite(LED, LOW);
  delay(500);
}
";

        private void OnTextChanged(object? sender, EventArgs e)
        {
            if (_loading || _board == null) return;
            var fw = W.FirmwareFor(_board.Part.RefDes, _board.Fqbn ?? "arduino:avr:uno");
            fw.Source = Editor.Text;
            fw.FileName = "sketch.ino";
            W.MarkFirmwareChanged();
            UpdateBuildState(fw);
        }

        private void UpdateBuildState(FirmwareSource? fw)
        {
            if (fw?.Hex == null)
            {
                BuildStatus.Text = "Henüz derlenmedi";
                BuildStatus.Foreground = (Brush)FindResource("B.TextMuted");
            }
            else if (!fw.IsBuildCurrent)
            {
                BuildStatus.Text = "Kod değişti · yeniden derleyin";
                BuildStatus.Foreground = (Brush)FindResource("B.Warning");
            }
            else
            {
                BuildStatus.Text = $"Derlendi · {fw.BuiltAt?.ToLocalTime():HH:mm:ss}";
                BuildStatus.Foreground = (Brush)FindResource("B.Success");
            }
        }

        private void OnTemplate(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateBox.SelectedIndex <= 0) return;
            var (name, source) = SketchTemplates.All[TemplateBox.SelectedIndex - 1];
            TemplateBox.SelectedIndex = 0;
            if (Editor.Text.Trim().Length > 0 && Editor.Text != source)
            {
                int answer = Dialog.Show(Window.GetWindow(this), "Şablonu yükle", $"'{name}' şablonu mevcut kodun yerine geçsin mi?", "Değiştir", "Vazgeç");
                if (answer != 0) return;
            }

            Editor.Text = source;
            OnTextChanged(this, EventArgs.Empty);
        }

        private void OnAddUno(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.PlacePart("arduino-uno");

        // ================================================================ build / upload

        private async System.Threading.Tasks.Task<bool> Build()
        {
            if (_board?.Fqbn == null) return false;
            _build?.Cancel();
            _build = new CancellationTokenSource();
            var token = _build.Token;
            string source = Editor.Text;
            BuildButton.IsEnabled = UploadButton.IsEnabled = false;
            BuildStatus.Text = "Derleniyor…";
            BuildStatus.Foreground = (Brush)FindResource("B.Accent");
            BottomTabs.SelectedIndex = 0;
            BuildLog.Text = ArduinoCompiler.IsEsp32(_board.Fqbn) ? "ESP32 çizimi MSVC ile derleniyor (ilk derleme HAL'ı da kurar) …\n" : "arduino-cli compile --fqbn " + _board.Fqbn + " …\n";
            BuildResult result;
            try
            {
                string key = (W.FilePath != null ? Path.GetFileNameWithoutExtension(W.FilePath) : "adsiz") + "-" + _board.Part.RefDes;
                result = await ArduinoCompiler.CompileAsync(source, _board.Fqbn, key, token);
            }
            finally
            {
                BuildButton.IsEnabled = UploadButton.IsEnabled = true;
            }

            if (token.IsCancellationRequested) return false;
            BuildLog.Text = result.Log;
            Editor.Diagnostics = result.Diagnostics;
            Problems.ItemsSource = result.Diagnostics.Count > 0 ? result.Diagnostics : (System.Collections.IEnumerable)new[] { result.Success ? "Hata yok." : "Derleme başarısız (günlüğe bakın)." };
            var fw = W.FirmwareFor(_board.Part.RefDes, _board.Fqbn);
            if (result.Success && result.Hex != null)
            {
                fw.Source = source;
                fw.Hex = result.Hex;
                fw.BuiltAt = DateTime.UtcNow;
                fw.BuiltFromHash = FirmwareSource.HashOf(source);
                fw.Target = _board.Fqbn;
                W.MarkFirmwareChanged($"Derlendi: {_board.Part.RefDes} · {result.FlashBytes} bayt flash, {result.RamBytes} bayt RAM · {result.Duration.TotalSeconds:F1} s");
                BuildStatus.Text = result.FlashBytes > 0
                    ? $"Derlendi · flash {result.FlashBytes}/{result.FlashMax} B ({100.0 * result.FlashBytes / Math.Max(1, result.FlashMax):F0}%) · RAM {result.RamBytes}/{result.RamMax} B · {result.Duration.TotalSeconds:F1} s"
                    : $"Derlendi · {result.Duration.TotalSeconds:F1} s";
                BuildStatus.Foreground = (Brush)FindResource(result.RamMax > 0 && result.RamBytes > 0.85 * result.RamMax ? "B.Warning" : "B.Success");
                return true;
            }

            int errors = result.Diagnostics.Count(d => d.IsError);
            BuildStatus.Text = errors > 0 ? $"{errors} hata" : "Derleme başarısız";
            BuildStatus.Foreground = (Brush)FindResource("B.Danger");
            var first = result.Diagnostics.FirstOrDefault(d => d.IsError);
            if (first != null) Editor.GoTo(first.Line, Math.Max(1, first.Column));
            return false;
        }

        private async void OnBuild(object sender, RoutedEventArgs e) => await Build();

        private async void OnUpload(object sender, RoutedEventArgs e)
        {
            if (_board == null) return;
            var fw = W.Project.FirmwareFor(_board.Part.RefDes);
            if (fw == null || !fw.IsBuildCurrent || fw.Source != Editor.Text)
            {
                if (!await Build()) return;
            }

            // Point the board at the project's firmware and (re)start the simulation.
            if (!_board.Part.Parameters.TryGetValue("firmware", out var current) || current != "project")
            {
                W.BeginChange();
                _board.Part.Parameters["firmware"] = "project";
                W.CommitChange($"{_board.Part.RefDes} artık projedeki kodu çalıştırıyor");
            }

            var sim = W.Simulation;
            sim.Stop();
            if (sim.Start())
            {
                _serialShown = 0;
                SerialView.Clear();
                BottomTabs.SelectedIndex = 1;
                W.Status = $"{_board.Part.RefDes}: kod yüklendi, simülasyon çalışıyor";
            }
            else
            {
                W.Status = "Simülasyon başlatılamadı: " + sim.StatusText;
            }
        }

        private void OnStop(object sender, RoutedEventArgs e) => W.Simulation.Stop();

        private void OnReset(object sender, RoutedEventArgs e)
        {
            if (_board != null) W.Simulation.ResetBoard(_board.Part.RefDes);
        }

        private void OnProblemPick(object sender, EventArgs e)
        {
            if (Problems.SelectedItem is CodeDiagnostic d) Editor.GoTo(d.Line, Math.Max(1, d.Column));
        }

        private void OnKeys(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F7)
            {
                OnBuild(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                OnUpload(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ================================================================ serial monitor

        private void UpdateSerial()
        {
            long total = W.Simulation.SerialTotal;
            if (total < _serialShown) _serialShown = 0;
            if (total == _serialShown) return;
            string text = W.Simulation.SerialText;
            long fresh = total - _serialShown;
            if (_serialShown == 0 || fresh >= text.Length) SerialView.Text = text;
            else SerialView.AppendText(text.Substring(text.Length - (int)fresh));
            _serialShown = total;
            if (SerialView.Text.Length > 60000) SerialView.Text = SerialView.Text.Substring(SerialView.Text.Length - 40000);
            SerialView.ScrollToEnd();
        }

        private void OnSerialSend(object sender, RoutedEventArgs e)
        {
            if (SerialInput.Text.Length == 0) return;
            W.Simulation.SendSerial(SerialInput.Text + "\n");
            SerialInput.Clear();
        }

        private void OnSerialKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnSerialSend(sender, e);
        }
    }
}
