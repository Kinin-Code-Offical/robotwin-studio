using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    /// <summary>
    /// [integrity] The project check in the window: Dosya → Projeyi denetle, and after a project or world is opened a notice
    /// when this computer lacks something it needs (a compiler, a library, a part), with the window one click away.
    /// </summary>
    public partial class MainWindow
    {
        private Border? _notice;
        private TextBlock? _noticeText;
        private Button? _noticeAction;
        private Action? _noticeClick;
        private DispatcherTimer? _noticeTimer;

        /// <summary>Checks the active project or world: the window at once (<paramref name="quiet"/> false), or a notice when something is missing.</summary>
        public async void CheckProject(bool quiet)
        {
            var doc = W.ActiveDocument;
            string what;
            Func<Task<List<ProjectIssue>>> check;
            if (doc.Kind == DocumentKind.World && doc.World is { } world)
            {
                what = string.IsNullOrWhiteSpace(doc.BaseTitle) ? "Dünya" : doc.BaseTitle;
                check = () => ProjectChecking.CheckAsync(world, W.Library);
            }
            else
            {
                W.FlushActiveDesign();
                var design = W.DesignDocument;
                var package = design.Package;
                what = string.IsNullOrWhiteSpace(package.Schematic.Title) ? "Proje" : package.Schematic.Title;
                check = () => ProjectChecking.CheckAsync(package, W.Library);
            }

            if (!quiet)
            {
                ShowCheckWindow(what, check, null);
                return;
            }

            List<ProjectIssue> issues;
            try
            {
                issues = await check();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                return;   // a quiet check that could not run says nothing; the menu's check shows why
            }

            int missing = ProjectChecking.Blocking(issues);
            if (missing == 0) return;
            ShowNotice($"{what}: bu bilgisayarda {missing} eksik var ({Areas(issues)}).", "Göster", () => ShowCheckWindow(what, check, issues));
        }

        private void ShowCheckWindow(string what, Func<Task<List<ProjectIssue>>> check, List<ProjectIssue>? issues)
        {
            var window = new ProjectCheckWindow(this, what, check, issues);
            if (App.IsSnapshot)
            {
                // --snapshot: shown without waiting, rendered instead of the main window.
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(window) > 0 ? VisualTreeHelper.GetChild(window, 0) as FrameworkElement : null;
                return;
            }

            window.ShowDialog();
        }

        /// <summary>
        /// --snapshot --fault check-demo: a project as it may come from another computer (a part from a library this one
        /// lacks, a sketch with libraries not installed here, an ESP32 in the hardware-compatible mode), checked against
        /// what this computer really has.
        /// </summary>
        private void LoadCheckDemo()
        {
            var p = new RobotTwin.CoreSim.Formats.ProjectPackage();
            p.Schematic.Title = "Engel algılayan araç";
            p.Schematic.Parts.Add(new SchematicPart { RefDes = "U1", PartId = "arduino-uno", Position = new GridPoint(40, 40) });
            p.Schematic.Parts.Add(new SchematicPart { RefDes = "X1", PartId = "ozel-lidar-tf02", Position = new GridPoint(120, 40) });
            var esp = new SchematicPart { RefDes = "U2", PartId = Esp32Parts.DevKitCId, Position = new GridPoint(40, 120) };
            esp.Parameters[Esp32Parts.ModeKey] = Esp32Parts.HardwareMode;
            p.Schematic.Parts.Add(esp);
            p.Firmware.Add(new RobotTwin.CoreSim.Formats.FirmwareSource
            {
                BoardRefDes = "U1", Target = "arduino:avr:uno", FileName = "arac.ino",
                Source = "#include <Servo.h>\n#include <NewPing.h>\n\nServo kafa;\nNewPing sonar(12, 11, 200);\n\nvoid setup() { kafa.attach(9); }\nvoid loop() { kafa.write(sonar.ping_cm() < 20 ? 0 : 90); delay(50); }\n"
            });
            p.Firmware.Add(new RobotTwin.CoreSim.Formats.FirmwareSource
            {
                BoardRefDes = "U2", Target = ProjectCheck.Esp32Target, FileName = "telemetri.ino",
                Source = "#include <WiFi.h>\n#include <PubSubClient.h>\n\nvoid setup() { WiFi.begin(\"atolye\", \"sifre\"); }\nvoid loop() {}\n"
            });
            W.LoadPackage(p, null);
        }

        private static string Areas(List<ProjectIssue> issues)
        {
            var areas = new List<string>();
            foreach (var i in issues)
                if (i.Level != IssueLevel.Note && !areas.Contains(i.Area)) areas.Add(i.Area);
            return string.Join(" · ", areas);   // " · ": the language pack translates each
        }

        /// <summary>A notice over the page with one action; it goes by itself after a while.</summary>
        public void ShowNotice(string text, string action, Action click)
        {
            if (_notice == null)
            {
                _noticeText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 560, VerticalAlignment = VerticalAlignment.Center };
                _noticeText.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
                _noticeAction = new Button { Style = (Style)FindResource("Btn.Primary"), Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
                _noticeAction.Click += (_, _) =>
                {
                    HideNotice();
                    _noticeClick?.Invoke();
                };
                var dismiss = new Button { Content = "Kapat", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
                dismiss.Click += (_, _) => HideNotice();
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                dot.SetResourceReference(Border.BackgroundProperty, "B.Warning");
                row.Children.Add(dot);
                row.Children.Add(_noticeText);
                row.Children.Add(_noticeAction);
                row.Children.Add(dismiss);
                _notice = new Border
                {
                    Child = row,
                    Padding = new Thickness(14, 9, 10, 9),
                    Margin = new Thickness(0, 12, 0, 0),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Effect = (System.Windows.Media.Effects.Effect)FindResource("E.Popup")
                };
                _notice.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
                _notice.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
                Grid.SetRow(_notice, 1);
                Panel.SetZIndex(_notice, 950);
                Root.Children.Add(_notice);
                _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
                _noticeTimer.Tick += (_, _) => HideNotice();
            }

            _noticeText!.Text = text;
            _noticeAction!.Content = action;
            _noticeClick = click;
            _notice.Visibility = Visibility.Visible;
            _noticeTimer!.Stop();
            _noticeTimer.Start();
        }

        private void HideNotice()
        {
            _noticeTimer?.Stop();
            if (_notice != null) _notice.Visibility = Visibility.Collapsed;
        }

        /// <summary>Settings at one category ("Derleyiciler", "Güncellemeler" …).</summary>
        public void ShowSettings(string category)
        {
            Navigate("settings");
            if (_pages.ContainsKey("settings") && _pages["settings"] is Pages.SettingsPage settings) settings.ShowCategory(category);
        }

        /// <summary>The ESP32 toolchain's install (it asks first: about 1.3 GB), on the code page where its log shows.</summary>
        public void InstallEsp32Toolchain()
        {
            Navigate("code");
            _code.InstallToolchain();
        }
    }
}
