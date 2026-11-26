using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox.Sensing;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [sensorlog] The Sensors panel's data log: switched on, every reading of the run goes to a CSV per sensor with the
    /// truth beside it and the active fault as its label (Sandbox/Sensing/SensorLog.cs); when the run stops the tables are
    /// written to Documents\RobotWin\Sensör kayıtları\&lt;world&gt;-&lt;time&gt; and the folder is offered.
    /// </summary>
    public partial class WorldPage
    {
        private CheckBox? _logSwitch;
        private TextBlock? _logNote;
        private Button? _logOpen;
        private SensorLog? _log;
        private string? _logFolder;
        private bool _logHooked;

        private FrameworkElement SensorLogBar()
        {
            var bar = new DockPanel { Margin = new Thickness(4, 2, 4, 6) };
            _logSwitch = new CheckBox
            {
                Content = "Sensör verisini kaydet (CSV, etiketli)", Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Çalışma boyunca her sensörün ölçümü, simülasyonun bildiği gerçek değer ve o andaki arıza senaryosu (etiket) bir CSV tablosuna yazılır"
            };
            _logSwitch.Click += (_, _) =>
            {
                // Switched on during a run: from now; switched off: what was taken is still written when the run stops.
                if (_logSwitch.IsChecked == true && _log == null && _session.Sensing is { } s) StartLog(s);
                if (_logSwitch.IsChecked != true) _log?.Detach();
                UpdateLogNote();
            };
            DockPanel.SetDock(_logSwitch, Dock.Left);
            bar.Children.Add(_logSwitch);
            _logOpen = new Button { Content = "Klasörü aç", Style = (Style)FindResource("Btn.Ghost"), Visibility = Visibility.Collapsed, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2) };
            _logOpen.Click += (_, _) =>
            {
                if (_logFolder != null && Directory.Exists(_logFolder))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = _logFolder, UseShellExecute = true });
            };
            DockPanel.SetDock(_logOpen, Dock.Right);
            bar.Children.Add(_logOpen);
            _logNote = new TextBlock { Style = (Style)FindResource("Text.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            bar.Children.Add(_logNote);
            if (!_logHooked)
            {
                _logHooked = true;
                // The sensing is built as the run starts, before its first step: the log attaches there and then, so the
                // first readings are in it (a deferred attach would miss what the simulation thread did meanwhile).
                _session.SensingStarted += s =>
                {
                    if (Dispatcher.CheckAccess()) OnLogSensingStarted(s);
                    else Dispatcher.BeginInvoke(new Action(() => OnLogSensingStarted(s)));
                };
                // The run's log is taken as it stops (a restart's new log is not the one written), written on the UI thread.
                _session.StateChanged += (_, _) =>
                {
                    if (_session.State != SimState.Stopped || _log is not { } done) return;
                    _log = null;
                    done.Detach();
                    Dispatcher.BeginInvoke(new Action(() => SaveLog(done)));
                };
            }

            return bar;
        }

        private void OnLogSensingStarted(WorldSensing sensing)
        {
            if (_logSwitch?.IsChecked == true) StartLog(sensing);
        }

        private void StartLog(WorldSensing sensing)
        {
            if (World == null) return;
            _log?.Detach();
            _log = new SensorLog(World);
            _log.Attach(sensing.Sensors);
            UpdateLogNote();
        }

        /// <summary>Writes a stopped run's tables to <paramref name="folder"/> (by default a new one under Documents\RobotWin).</summary>
        private void SaveLog(SensorLog log, string? folder = null)
        {
            if (log.Rows == 0)
            {
                UpdateLogNote();
                return;
            }

            if (folder == null)
            {
                string stem = string.Concat((string.IsNullOrWhiteSpace(World?.Title) ? Shell.Lang.T("Dünya") : World!.Title).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "RobotWin", Shell.Lang.T("Sensör kayıtları"),
                    stem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            }

            try
            {
                var files = log.Save(folder);
                _logFolder = folder;
                if (_logNote != null) _logNote.Text = Shell.Lang.T($"{log.Rows} satır, {files.Count - 1} sensör: {folder}");
                if (_logOpen != null) _logOpen.Visibility = Visibility.Visible;
                W.Status = $"Sensör kaydı yazıldı: {files.Count - 1} CSV, {log.Rows} satır";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                W.Status = "Sensör kaydı yazılamadı: " + ex.Message;
            }
        }

        private void UpdateLogNote()
        {
            if (_logNote == null) return;
            if (_logSwitch?.IsChecked != true)
            {
                if (_logFolder == null) _logNote.Text = "";
                return;
            }

            _logNote.Text = Shell.Lang.T(_log != null ? "Kaydediliyor; dünya durunca CSV'ler yazılır." : "Dünyayı çalıştırınca kayıt başlar.");
        }

        // ---------------------------------------------------------------- snapshot "<template>:sensors:csv"

        /// <summary>Before the snapshot's run: the switch on, so the run is logged from its first step.</summary>
        private void SensorLogSnapshotBegin()
        {
            if (_snapshotModes.Contains("csv") && _logSwitch != null) _logSwitch.IsChecked = true;
        }

        /// <summary>After it: the tables written to the temp folder (never the user's documents) and the panel showing them.</summary>
        private void SensorLogSnapshotFinish()
        {
            if (!_snapshotModes.Contains("csv") || _log is not { } log) return;
            _log = null;
            log.Detach();
            SaveLog(log, Path.Combine(Path.GetTempPath(), "robotwin-sensorlog-snapshot"));
        }
    }
}
