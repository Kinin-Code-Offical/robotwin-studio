using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Formats;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [esp32hw] Code → Gerçek kart: the open sketch built for a real ESP32 with Espressif's toolchain (the
    /// hardware-compatible mode, docs/gelistirici/ESP32_MODLARI.md), written to a board on a USB serial port, or its
    /// merged image saved for a web flasher. The simulated board keeps running the fast mode's build.
    /// </summary>
    public partial class CodePage
    {
        private string? _hardwareOut;
        private string? _hardwareBuiltFrom;
        private CancellationTokenSource? _hardwareWork;

        private void OnHardware(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = HardwareButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            bool installed = ArduinoCompiler.Esp32ToolchainInstalled;
            if (!installed)
            {
                Item(menu, $"ESP32 araç zincirini kur (Arduino-ESP32 {ArduinoCompiler.Esp32CoreVersion}, yaklaşık 4 GB)", InstallToolchain);
                menu.Items.Add(new Separator());
            }

            Item(menu, "Gerçek kart için derle", () => _ = BuildForBoard(), installed && _folder != null);
            var upload = new MenuItem { Header = "Gerçek karta yükle", IsEnabled = installed && _folder != null };
            var ports = ArduinoCompiler.SerialPorts();
            if (ports.Count == 0) upload.Items.Add(new MenuItem { Header = "Bağlı kart yok (USB kablosunu takın)", IsEnabled = false });
            foreach (var port in ports) Item(upload, port, () => _ = UploadToBoard(port));
            menu.Items.Add(upload);
            Item(menu, "Birleşik imajı (.bin) dışa aktar…", () => _ = ExportImage(), installed && _folder != null);
            Item(menu, "Derleme klasörünü aç", () =>
            {
                if (_hardwareOut != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", _hardwareOut) { UseShellExecute = true });
            }, _hardwareOut != null && Directory.Exists(_hardwareOut));
            menu.IsOpen = true;
        }

        private static void Item(ItemsControl parent, string header, Action click, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => click();
            parent.Items.Add(item);
        }

        private async void InstallToolchain()
        {
            if (Dialog.Show(OwnerWindow!, "ESP32 araç zinciri",
                    $"Espressif'in Arduino-ESP32 {ArduinoCompiler.Esp32CoreVersion} çekirdeği (xtensa derleyicisi, ESP-IDF kütüphaneleri, esptool) RobotWin'in kendi klasörüne indirilip kurulsun mu? İndirme yaklaşık 1,3 GB, kurulu hali 4 GB; Arduino IDE'nize dokunulmaz.",
                    "İndir ve kur", "Vazgeç") != 0) return;
            var work = Begin();
            BuildLog.Text = string.Empty;
            BuildStatus.Text = "ESP32 araç zinciri kuruluyor…";
            bool ok = await ArduinoCompiler.InstallEsp32ToolchainAsync(Log, work.Token);
            End(ok ? "ESP32 araç zinciri kuruldu" : "ESP32 araç zinciri kurulamadı (günlüğe bakın)", ok);
        }

        /// <summary>Builds the open sketch for a real ESP32; true when the images are ready.</summary>
        private async Task<bool> BuildForBoard()
        {
            if (_folder == null) return false;
            FlushDisk();
            string bundle = _folder.ToFirmware(ArduinoCompiler.Esp32Target).Bundle();
            if (_hardwareOut != null && _hardwareBuiltFrom == FirmwareSource.HashOf(bundle) && Directory.Exists(_hardwareOut)) return true;
            var work = Begin();
            BuildLog.Text = $"Gerçek ESP32 için derleniyor (Arduino-ESP32 {ArduinoCompiler.Esp32CoreVersion}, xtensa-esp32-elf-gcc) …\n";
            BuildStatus.Text = "Gerçek kart için derleniyor…";
            string key = SafeName(_folder.Title) + "-" + (_target?.Key ?? "klasor").Replace(':', '_');
            var result = await ArduinoCompiler.CompileEsp32HardwareAsync(bundle, key, Log, work.Token);
            if (work.IsCancellationRequested) return false;
            _diagnostics = result.Diagnostics.Select(d => d.File == "sketch.ino" ? new CodeDiagnostic(d.Line, d.Column, d.IsError, d.Message, _folder.MainFile) : d).ToList();
            ShowDiagnostics();
            Problems.ItemsSource = _diagnostics.Count > 0 ? _diagnostics : (System.Collections.IEnumerable)new[] { result.Success ? "Hata yok." : "Derleme başarısız (günlüğe bakın)." };
            if (!result.Success || result.OutputFolder == null)
            {
                End(_diagnostics.Count(d => d.IsError) is int n && n > 0 ? $"Gerçek kart derlemesi: {n} hata" : "Gerçek kart derlemesi başarısız", false);
                return false;
            }

            _hardwareOut = result.OutputFolder;
            _hardwareBuiltFrom = FirmwareSource.HashOf(bundle);
            End($"Gerçek kart için derlendi · flash {result.FlashBytes}/{result.FlashMax} B ({100.0 * result.FlashBytes / Math.Max(1, result.FlashMax):F0}%) · RAM {result.RamBytes}/{result.RamMax} B · {result.Duration.TotalSeconds:F0} s", true);
            return true;
        }

        private async Task UploadToBoard(string port)
        {
            if (!await BuildForBoard() || _hardwareOut == null) return;
            var work = Begin();
            Log($"{port} üzerindeki karta yazılıyor (esptool) …");
            BuildStatus.Text = $"{port} kartına yükleniyor…";
            bool ok = await ArduinoCompiler.UploadEsp32Async(_hardwareOut, port, Log, work.Token);
            End(ok ? $"{port} kartına yüklendi" : $"{port} kartına yüklenemedi (bağlantıyı ve BOOT düğmesini deneyin)", ok);
        }

        private async Task ExportImage()
        {
            if (!await BuildForBoard() || _hardwareOut == null) return;
            string? merged = ArduinoCompiler.Esp32MergedImage(_hardwareOut);
            if (merged == null)
            {
                W.Status = "Birleşik imaj üretilmedi.";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Birleşik ESP32 imajı",
                FileName = SafeName(_folder?.Title ?? "esp32") + ".merged.bin",
                Filter = "ESP32 imajı (*.bin)|*.bin"
            };
            if (dialog.ShowDialog(OwnerWindow) != true) return;
            File.Copy(merged, dialog.FileName, true);
            W.Status = "Birleşik imaj kaydedildi (0x0 adresine yazılır): " + Path.GetFileName(dialog.FileName);
        }

        private CancellationTokenSource Begin()
        {
            _hardwareWork?.Cancel();
            _hardwareWork = new CancellationTokenSource();
            HardwareButton.IsEnabled = BuildButton.IsEnabled = UploadButton.IsEnabled = false;
            BuildStatus.Foreground = (Brush)FindResource("B.Accent");
            BottomTabs.SelectedIndex = 0;
            return _hardwareWork;
        }

        private void End(string status, bool ok)
        {
            HardwareButton.IsEnabled = BuildButton.IsEnabled = UploadButton.IsEnabled = true;
            BuildStatus.Text = status;
            BuildStatus.Foreground = (Brush)FindResource(ok ? "B.Success" : "B.Danger");
            W.Status = status;
        }

        /// <summary>A line of arduino-cli's output into the build log (from any thread).</summary>
        private void Log(string line) => Dispatcher.BeginInvoke(() =>
        {
            BuildLog.AppendText(line + "\n");
            BuildLog.ScrollToEnd();
        });
    }
}
