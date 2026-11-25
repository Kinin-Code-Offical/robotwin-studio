using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Branding;
using RobotWin.Studio.Examples;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    public partial class App : Application
    {
        /// <summary>Set by a snapshot preparation step to render something other than the main window (a dialog).</summary>
        internal static FrameworkElement? SnapshotTarget { get; set; }

        /// <summary>A --snapshot (or other unattended) run: nothing may wait for an answer.</summary>
        internal static bool IsSnapshot { get; private set; }

        /// <summary>Where the snapshot goes (checks write their log beside it).</summary>
        internal static string? SnapshotPath { get; private set; }

        protected override void OnExit(ExitEventArgs e)
        {
            Shell.Backups.EndSession();   // [backup] a normal exit
            base.OnExit(e);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            DispatcherUnhandledException += OnUnhandled;

            var args = e.Args;
            string? Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            // Called by the installer / uninstaller.
            if (args.Contains("--register-file-types") || args.Contains("--unregister-file-types"))
            {
                try
                {
                    if (args.Contains("--register-file-types")) Shell.FileAssociations.Register();
                    else Shell.FileAssociations.Unregister();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is IOException)
                {
                    Shutdown(1);
                    return;
                }

                Shutdown(0);
                return;
            }

            if (Arg("--export-logo") is { } logoDir)
            {
                Logo.Export(logoDir);
                Shutdown(0);
                return;
            }

            if (Arg("--gallery") is { } galleryDir)
            {
                Models3D.ModelGallery.ShowPins = args.Contains("--gallery-pins");
                // Contact sheets of every 3D component model (visual check of the model library).
                int cols = Arg("--gallery-columns") is { } gc ? int.Parse(gc, CultureInfo.InvariantCulture) : 4;
                var tile = (Arg("--gallery-tile") ?? "460x340").Split('x');
                Models3D.ModelGallery.Export(galleryDir, Arg("--gallery-filter"), cols, int.Parse(tile[0], CultureInfo.InvariantCulture), int.Parse(tile[1], CultureInfo.InvariantCulture));
                Shutdown(0);
                return;
            }

            if (Arg("--esp32-compat") is { } compatReport)
            {
                Shutdown(Esp32CompatCommand(compatReport));   // [esp32hw] every ESP32 sketch of the templates, built for the real chip
                return;
            }

            if (Arg("--esp32-compare") is { } compareReport)
            {
                // [esp32hw] stage 6: every ESP32 world template in both modes, the differences in a markdown report
                // 8 s by default: long enough for the templates' setup (the quad calibrates its IMU for 2 s, takes off at 4 s).
                double seconds = double.TryParse(Arg("--esp32-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : 8.0;
                Shutdown(Firmware.Esp32ModeComparison.Run(compareReport, seconds));
                return;
            }

            if (Arg("--perf-check") is { } perfReport)
            {
                // [perf] every world template headless, its cost per simulated second against a baseline; 1 = a regression
                double perfSeconds = double.TryParse(Arg("--perf-seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ps) && ps > 0 ? ps : 1.0;
                double perfTolerance = double.TryParse(Arg("--perf-tolerance"), NumberStyles.Float, CultureInfo.InvariantCulture, out var pt) && pt > 0 ? pt / 100 : 0.25;
                Shutdown(Shell.PerfCheck.Run(perfReport, Arg("--perf-baseline"), args.Contains("--perf-update"), perfSeconds, perfTolerance, Arg("--perf-filter")?.Split(',')));
                return;
            }

            if (Arg("--esp32-build") is { } esp32Sketch)
            {
                // [esp32hw] --esp32-build sketch.ino [--esp32-out folder] [--esp32-install]: the hardware-compatible build of
                // a sketch for a real ESP32, without a window; the images go to the folder, the log to the console's file.
                Shutdown(Esp32BuildCommand(esp32Sketch, Arg("--esp32-out"), args.Contains("--esp32-install")));
                return;
            }

            if (Arg("--render-test") is { } renderTestPath)
            {
                Shutdown(Render.RenderSelfTest.Run(renderTestPath));   // [render] the scene mirror and the GPU renderer, drawn and read back
                return;
            }

            if (args.Contains("--selftest"))
            {
                int code = SelfTest(Arg("--selftest-out"));
                Shutdown(code);
                return;
            }

            if (Automation.StudioApiHost.TryRun(this, args)) return;   // [api] --api <port> / --bake-geometry: windowless automation server

            Shell.Themes.Apply();   // [theme] before any window takes the brushes
            // [lang] the language pack, before any window is built (--lang en: this run only)
            // Command-line runs (pictures, checks, benches) speak Turkish unless --lang says otherwise: their checks and
            // the guide's pictures are written for it. The app itself opens in the language of the settings.
            bool automated = args.Any(a => a is "--snapshot" or "--selftest" or "--bench" or "--startup-report" or "--overlap-report" or "--gallery" or "--api");
            Shell.Lang.Load(Arg("--lang") ?? (automated ? "tr" : Shell.AppSettings.Current.Language));
            if (Shell.Lang.Current != "tr")
            {
                // Numbers and dates in the language's own way (1.5 mm, not 1,5 mm), on every thread from here on.
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = Shell.Lang.Culture;
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Shell.Lang.Culture;
            }

            if (Arg("--lang-missing") is { } missingLog)
            {
                // [lang] every text the pack had no translation for in this run (windows, canvases, messages), at exit
                Shell.Lang.Misses = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
                Exit += (_, _) =>
                {
                    var lines = Shell.Lang.MissesSoFar().Select(s => System.Text.Json.JsonSerializer.Serialize(new { page = "run", s }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
                    File.AppendAllLines(missingLog, lines);
                };
            }

            // [render] --renderer gpu|wpf — this run only (nothing is saved): who draws the 3D views.
            if (Arg("--renderer") is { } rendererArg) Shell.AppSettings.Current.Renderer3D = rendererArg;
            RobotTwin.CoreSim.Localization.Translate = Shell.Lang.T;   // [lang] what the simulator writes into files (BOM …)
            Shell.Lang.Install();
            Shell.StartupSplash.Open();   // [startup] the first window, on its own thread
            Shell.Backups.BeginSession();   // [backup] a run left open by a crash shows on the start page
            Shell.StartupTrace.Mark("Parça kütüphanesi yükleniyor", 0.05);
            Shell.UserLibrary.LoadAll(RobotTwin.CoreSim.Design.PartLibrary.Default);
            Shell.StartupTrace.Mark("Arayüz hazırlanıyor", 0.25);
            var window = new MainWindow();
            MainWindow = window;
            window.ContentRendered += (_, _) =>
            {
                Shell.StartupTrace.Mark("İlk çizim", 1);
                Shell.StartupSplash.Finish(window);
                if (Arg("--startup-report") is { } report)
                {
                    // measured: the timings, then out (after the window has had a moment to settle)
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                    t.Tick += (_, _) =>
                    {
                        t.Stop();
                        Shell.StartupTrace.Mark("İlk çizimden 400 ms sonra", 1);
                        var (exact, patterns) = Shell.Lang.Size;
                        File.WriteAllText(report, Shell.StartupTrace.Report() + $"\ndil: {Shell.Lang.Current}, {exact} metin, {patterns} kalıp, etkin {Shell.Lang.Active}; 'Ayarlar' → '{Shell.Lang.T("Ayarlar")}'\n");
                        window.ForceClose();
                    };
                    t.Start();
                }
            };

            if (Arg("--overlap-report") is { } overlapPath)
            {
                window.Left = -2000;
                window.ShowActivated = false;
                window.ShowInTaskbar = false;
                window.Show();
                File.WriteAllText(overlapPath, window.OverlapReportAllTemplates());
                window.ForceClose();
                return;
            }

            if (Arg("--bench") is { } benchPath)
            {
                RunBench(window, benchPath, Arg("--bench-seconds"));
                return;
            }

            if (Arg("--snapshot") is { } splashShot && Arg("--page") == "splash")
            {
                Shell.StartupSplash.RenderPreview(splashShot, 1.25);   // [startup]
                Shutdown(0);
                return;
            }

            if (Arg("--snapshot") is { } snapshotPath)
            {
                IsSnapshot = true;
                SnapshotPath = snapshotPath;
                if (Arg("--page") == "model")
                {
                    // 3D modeller: rendered on its own while it is not part of MainWindow's navigation.
                    window.Navigate("model");
                    Pages.ModelPage.E2eLogPath = Path.ChangeExtension(snapshotPath, ".txt");
                    if (window.CurrentPage != "model")
                    {
                        Pages.ModelPage.RunSnapshot(snapshotPath, Arg("--size"), Arg("--fault"), () => Shutdown(0));
                        return;
                    }
                }

                RunSnapshot(window, snapshotPath, Arg("--page") ?? "home", Arg("--example"), Arg("--simulate"), Arg("--size"), Arg("--fault"), Arg("--wait"));
                return;
            }

            string? file = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal) && File.Exists(a));
            Shell.StartupTrace.Mark("Pencere açılıyor", 0.92);
            if (Shell.Unattended.MeasuringStartup)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = -2200;
                window.ShowActivated = false;
            }

            window.Show();
            if (file != null) window.OpenFile(file);
            else if (!Shell.AppSettings.Current.ShowHomeOnStart && Shell.RecentFiles.Load().FirstOrDefault() is { } last) window.OpenFile(last);
        }

        private string? _lastCrash;
        private int _crashRepeats;

        private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                // The same error raised every frame/layout pass is written once (with a repeat count),
                // and the log rotates at 5 MB so it can never fill the disk.
                string key = e.Exception.GetType().FullName + ":" + e.Exception.Message;
                if (key == _lastCrash)
                {
                    _crashRepeats++;
                }
                else
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio");
                    Directory.CreateDirectory(dir);
                    string log = Path.Combine(dir, "crash.log");
                    if (File.Exists(log) && new FileInfo(log).Length > 5 * 1024 * 1024) File.Move(log, log + ".old", true);
                    string repeats = _crashRepeats > 0 ? $"(önceki hata {_crashRepeats} kez daha tekrarlandı)\n" : string.Empty;
                    File.AppendAllText(log, repeats + $"[{DateTime.Now:O}] {e.Exception}\n\n");
                    _lastCrash = key;
                    _crashRepeats = 0;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            Workspace.Current.Simulation.Stop();
            Workspace.Current.Status = "Beklenmeyen hata: " + e.Exception.Message;
            e.Handled = true;
        }

        /// <summary>Renders a page to PNG without user interaction (used for docs and visual checks).</summary>
        private void RunSnapshot(MainWindow window, string path, string page, string? example, string? simulate, string? size, string? fault = null, string? wait = null)
        {
            int w = 1600, h = 960;
            if (size != null)
            {
                var parts = size.Split('x');
                w = int.Parse(parts[0], CultureInfo.InvariantCulture);
                h = int.Parse(parts[1], CultureInfo.InvariantCulture);
            }

            // Narrow sizes emulate the page inside a split view / detached window.
            window.MinWidth = Math.Min(window.MinWidth, w);
            window.MinHeight = Math.Min(window.MinHeight, h);
            window.Width = w;
            window.Height = h;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -w - 200;
            window.Top = 0;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();

            if (example != null)
            {
                var ex = ExampleProjects.All.FirstOrDefault(x => x.Id == example);
                if (ex != null) Workspace.Current.Load(ex.Build(), null);
            }

            // [esp32hw] --esp32-mode hardware|fast: every ESP32 of this run (circuit and world) in that mode, nothing saved, to
            // see a design on the Xtensa emulator or compare the two modes.
            if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--esp32-mode").Skip(1).FirstOrDefault() is { } espMode)
                RobotTwin.CoreSim.Design.Esp32Parts.RunModeOverride = espMode == "hardware" ? RobotTwin.CoreSim.Design.Esp32Parts.HardwareMode : RobotTwin.CoreSim.Design.Esp32Parts.FastMode;

            if (Environment.GetCommandLineArgs().Contains("--nav-expanded")) window.ExpandRailForSnapshot();
            if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--theme").Skip(1).FirstOrDefault() is { } themeArg)
            {
                // [theme] --theme pembe:gul — this run only (nothing is saved): every page in that theme and accent.
                var parts = themeArg.Split(':');
                Shell.AppSettings.Current.Theme = parts[0];   // in memory only: a command-line run saves no settings
                if (parts.Length > 1) Shell.AppSettings.Current.Accent = parts[1];
                Shell.Themes.Apply();
            }
            window.Navigate(page);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            int phase = 0;
            timer.Tick += (_, _) =>
            {
                phase++;
                if (phase == 1)
                {
                    window.PrepareSnapshot(page, simulate, fault);
                    if (Environment.GetCommandLineArgs().Contains("--perf")) window.ShowPerfForSnapshot();   // [perf] this run only
                    // [perf] --memory-log file: the world runs live through --wait while the memory in use after a full
                    // collection is written every two seconds (a long run that leaks shows a rising line).
                    if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--memory-log").Skip(1).FirstOrDefault() is { } memoryPath)
                    {
                        window.ShowPerfForSnapshot();
                        var started = System.Diagnostics.Stopwatch.StartNew();
                        File.WriteAllText(memoryPath, "seconds\tmanaged MB\tworking set MB\n");
                        var sampler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                        sampler.Tick += (_, _) =>
                        {
                            long managed = GC.GetTotalMemory(true);
                            File.AppendAllText(memoryPath, string.Format(CultureInfo.InvariantCulture, "{0:0}\t{1:0.0}\t{2:0}\n",
                                started.Elapsed.TotalSeconds, managed / 1048576.0, Environment.WorkingSet / 1048576.0));
                        };
                        sampler.Start();
                    }

                    return;
                }

                int extra = wait != null ? (int)(double.Parse(wait, CultureInfo.InvariantCulture) / 0.4) : 0;
                if (phase < 4 + extra) return;
                timer.Stop();
                window.UpdateLayout();
                var root = SnapshotTarget ?? (FrameworkElement)window.Content;
                root.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(window);
                var bmp = new RenderTargetBitmap((int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY),
                    96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(root);
                var shot = WithPopups(bmp, root, dpi);   // [code] open popups (completion list, call tip) are windows of their own
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(shot));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var fs = File.Create(path)) enc.Save(fs);
                // [theme] --contrast-report file: text that cannot be read on this page in this theme (every window and open
                // popup), added to the file (one per line).
                if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--contrast-report").Skip(1).FirstOrDefault() is { } contrastPath)
                {
                    var under = (window.TryFindResource("B.Base") as SolidColorBrush)?.Color ?? Colors.Black;
                    var lines = new System.Collections.Generic.List<string>();
                    foreach (var source in PresentationSource.CurrentSources.OfType<PresentationSource>().ToList())
                        if (source.RootVisual is { } rootVisual)
                            lines.AddRange(Shell.ContrastAudit.Find(rootVisual, under).Select(f => Shell.ContrastAudit.Line(f, page + (fault != null ? ":" + fault : string.Empty), Shell.AppSettings.Current.Theme)));
                    File.AppendAllLines(contrastPath, lines.Distinct());
                }

                // [lang] --lang-missing file: the page's texts the pack leaves in Turkish, added to the file (one per line).
                if (Environment.GetCommandLineArgs().SkipWhile(a => a != "--lang-missing").Skip(1).FirstOrDefault() is { } missingPath)
                {
                    var missing = Shell.Lang.Untranslated(window);
                    // The event log as a whole (a list shows only the rows in view): each entry's source and message.
                    foreach (var entry in window.RunLogs())
                        foreach (var s in new[] { entry.Device, entry.Message })
                            if (!string.IsNullOrWhiteSpace(s) && s.Any(char.IsLetter) && Shell.Lang.T(s) == s)
                                missing.Add(s);
                    if (page == "library")
                    {
                        // The catalogue and the examples as a whole (a list shows only the rows in view).
                        var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
                        var data = lib.Parts.SelectMany(p => new[] { p.Name, p.Category, p.Description })
                            .Concat(lib.Presets.SelectMany(p => new[] { p.Name, p.Category, p.Description }))
                            .Concat(ExampleProjects.All.SelectMany(x => new[] { x.Title, x.Description, x.Level }));
                        foreach (var s in data)
                        {
                            if (!string.IsNullOrWhiteSpace(s) && s.Any(char.IsLetter) && Shell.Lang.T(s) == s) missing.Add(s);
                        }
                    }

                    File.AppendAllLines(missingPath, missing.Select(s => System.Text.Json.JsonSerializer.Serialize(new { page, s }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })));
                }

                Workspace.Current.Simulation.Stop();
                window.ForceClose();
            };
            timer.Start();
        }

        /// <summary>Popups the snapshot should draw over the page (each registers itself while open).</summary>
        public static readonly System.Collections.Generic.List<System.Windows.Controls.Primitives.Popup> SnapshotPopups = new System.Collections.Generic.List<System.Windows.Controls.Primitives.Popup>();

        private static BitmapSource WithPopups(RenderTargetBitmap page, FrameworkElement root, DpiScale dpi)
        {
            var open = SnapshotPopups.Where(p => p.IsOpen && p.Child is FrameworkElement { IsVisible: true }).ToList();
            // The window's adorner layer (the guide's outline, drag marks) sits above the page, outside what was rendered.
            var adorners = System.Windows.Documents.AdornerLayer.GetAdornerLayer(root);
            bool adorned = adorners != null && VisualTreeHelper.GetChildrenCount(adorners) > 0;
            if (open.Count == 0 && !adorned) return page;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawImage(page, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                if (adorned)
                {
                    var layer = new RenderTargetBitmap(page.PixelWidth, page.PixelHeight, page.DpiX, page.DpiY, PixelFormats.Pbgra32);
                    layer.Render(adorners!);
                    dc.DrawImage(layer, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                }

                foreach (var p in open)
                {
                    var child = (FrameworkElement)p.Child;
                    child.UpdateLayout();
                    // Where the popup asked to be (relative to its target): the snapshot window is off screen, so Windows
                    // moves the real popup onto a monitor and its screen position says nothing.
                    var at = p.PlacementTarget is UIElement target
                        ? target.TranslatePoint(new Point(p.HorizontalOffset, p.VerticalOffset), root)
                        : new Point(p.HorizontalOffset, p.VerticalOffset);
                    var pic = new RenderTargetBitmap(Math.Max(1, (int)(child.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)(child.ActualHeight * dpi.DpiScaleY)),
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    pic.Render(child);
                    dc.DrawImage(pic, new Rect(at.X, at.Y, child.ActualWidth, child.ActualHeight));
                }
            }

            var all = new RenderTargetBitmap(page.PixelWidth, page.PixelHeight, page.DpiX, page.DpiY, PixelFormats.Pbgra32);
            all.Render(visual);
            return all;
        }

        /// <summary>
        /// Frame-rate benchmark: every world template runs live for a few seconds in a real
        /// 1600×960 window (off screen) and the frame times and per-subsystem costs are written out.
        /// </summary>
        private void RunBench(MainWindow window, string path, string? seconds)
        {
            double secs = seconds != null ? double.Parse(seconds, CultureInfo.InvariantCulture) : 4;
            window.Width = 1600;
            window.Height = 960;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -1800;
            window.Top = 0;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Show();
            Perf.Start();
            // --bench-filter id1,id2: only these templates (quick A/B runs between versions).
            var only = Environment.GetCommandLineArgs().SkipWhile(a => a != "--bench-filter").Skip(1).FirstOrDefault()?.Split(',');
            var templates = Examples.WorldTemplates.All.Where(t => only == null || only.Contains(t.Id)).ToList();
            var log = new StringBuilder($"RobotWin Studio kare hızı ölçümü · {DateTime.Now:yyyy-MM-dd HH:mm} · {secs:0.#} s/şablon · 1600×960{Environment.NewLine}");
            int index = -1;
            var phaseStart = DateTime.Now;
            int phase = 0;   // 0 = load, 1 = idle, 2 = running
            // The log is written as it grows, with the step under way last: a run that hangs or is stopped still says
            // what it measured and where it stopped.
            void Write(string? doing)
            {
                try
                {
                    File.WriteAllText(path, log + (doing != null ? doing + Environment.NewLine : string.Empty));
                }
                catch (IOException)
                {
                }
            }

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                double elapsed = (DateTime.Now - phaseStart).TotalSeconds;
                switch (phase)
                {
                    case 0:
                        index++;
                        if (index >= templates.Count)
                        {
                            timer.Stop();
                            File.WriteAllText(path, log.ToString());
                            window.ForceClose();
                            return;
                        }

                        Write($"{templates[index].Id,-14} açılıyor…");
                        window.NewWorld(templates[index].Build());
                        window.Navigate("world");
                        Write($"{templates[index].Id,-14} durgun ölçülüyor…");
                        phase = 1;
                        phaseStart = DateTime.Now;
                        Perf.BeginRecording();
                        break;
                    case 1 when elapsed > 1.5:
                        log.AppendLine($"{templates[index].Id,-14} durgun     {Perf.EndRecording()}");
                        log.AppendLine($"{string.Empty,-14} yükleme    {window.WorldLoadProfile}");   // [perf] where opening it went
                        Write($"{templates[index].Id,-14} çalıştırılıyor…");
                        window.RunWorldForBench();
                        Write($"{templates[index].Id,-14} çalışırken ölçülüyor…");
                        phase = 2;
                        phaseStart = DateTime.Now;
                        Perf.BeginRecording();
                        break;
                    case 2 when elapsed > secs:
                        log.AppendLine($"{templates[index].Id,-14} çalışırken {Perf.EndRecording()}");
                        log.AppendLine($"{string.Empty,-14} başlatma   {window.WorldStartProfile}");   // [perf] where pressing Run went
                        Write($"{templates[index].Id,-14} durduruluyor…");
                        window.StopWorldForBench();
                        Write(null);
                        phase = 0;
                        break;
                }
            };
            timer.Start();
        }

        /// <summary>Headless end-to-end check of every example: compile, simulate, lay out a PCB, run DRC.</summary>
        /// <summary>
        /// [esp32hw] The two modes' compatibility: every ESP32 sketch in the world templates and examples built with the
        /// real toolchain; one line each (template, board, flash, RAM, time, or the first error). 0 when all build.
        /// </summary>
        private static int Esp32CompatCommand(string reportPath)
        {
            var lines = new System.Collections.Generic.List<string> { $"ESP32 uyumluluğu · Arduino-ESP32 {Firmware.ArduinoCompiler.Esp32CoreVersion} · {DateTime.Now:yyyy-MM-dd HH:mm}" };
            if (!Firmware.ArduinoCompiler.Esp32ToolchainInstalled)
            {
                lines.Add("ESP32 araç zinciri kurulu değil (--esp32-build … --esp32-install).");
                File.WriteAllLines(reportPath, lines);
                return 2;
            }

            var sketches = new System.Collections.Generic.List<(string Where, string Board, string Source)>();
            foreach (var template in Examples.WorldTemplates.All)
            {
                RobotTwin.CoreSim.Sandbox.WorldDocument world;
                try
                {
                    world = template.Build();
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
                {
                    continue;
                }

                foreach (var o in world.Objects)
                {
                    bool esp32 = o.PartId?.StartsWith("esp32", StringComparison.OrdinalIgnoreCase) == true || o.Firmware?.Target?.StartsWith("esp32", StringComparison.OrdinalIgnoreCase) == true;
                    if (!esp32) continue;
                    if (o.Firmware is { } fw && fw.Source.Trim().Length > 0)
                        sketches.Add((template.Id, o.Name, fw.Bundle()));
                    else if (o.Parameters.TryGetValue("firmware", out var param) && Simulation.SimulationSession.ResolveFirmwarePath(param) is { } path && path.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                        sketches.Add((template.Id, o.Name, new RobotTwin.CoreSim.Formats.FirmwareSource { Source = File.ReadAllText(path) }.Bundle()));
                }
            }

            foreach (var ex in ExampleProjects.All)
            {
                var project = ex.Build();
                foreach (var part in project.Parts.Where(p => p.PartId.StartsWith("esp32", StringComparison.OrdinalIgnoreCase)))
                    if (part.Parameters.TryGetValue("firmware", out var param) && Simulation.SimulationSession.ResolveFirmwarePath(param) is { } path && File.Exists(path))
                        sketches.Add((ex.Id, part.RefDes, new RobotTwin.CoreSim.Formats.FirmwareSource { Source = File.ReadAllText(path) }.Bundle()));
            }

            int failed = 0;
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var (where, board, source) in sketches)
            {
                if (!seen.Add(RobotTwin.CoreSim.Formats.FirmwareSource.HashOf(source))) continue;
                if (Firmware.ArduinoCompiler.Esp32HardwareBlocker(source) is { } blocker)
                {
                    lines.Add($"bekliyor {where,-14} {board,-24} {blocker}");
                    File.WriteAllLines(reportPath, lines);
                    continue;
                }

                var r = Firmware.ArduinoCompiler.CompileEsp32HardwareAsync(source, "compat-" + where).GetAwaiter().GetResult();
                if (r.Success)
                {
                    lines.Add($"tamam  {where,-16} {board,-24} flash {r.FlashBytes * 100.0 / Math.Max(1, r.FlashMax),5:F1}%  RAM {r.RamBytes * 100.0 / Math.Max(1, r.RamMax),5:F1}%  {r.Duration.TotalSeconds,4:F0} s");
                }
                else
                {
                    failed++;
                    var first = r.Diagnostics.FirstOrDefault(d => d.IsError);
                    lines.Add($"HATA   {where,-16} {board,-24} {(first != null ? $"{first.File}:{first.Line}: {first.Message}" : "derlenemedi")}");
                }

                File.WriteAllLines(reportPath, lines);
            }

            int waiting = lines.Count(l => l.StartsWith("bekliyor", StringComparison.Ordinal));
            lines.Add(failed == 0
                ? $"sonuç: {seen.Count - waiting}/{seen.Count} çizim gerçek ESP32 için derlendi" + (waiting > 0 ? $", {waiting} çizim Bluepad32 çekirdeğini bekliyor" : string.Empty)
                : $"sonuç: {failed}/{seen.Count} çizim derlenemedi");
            File.WriteAllLines(reportPath, lines);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>[esp32hw] Builds a sketch for a real ESP32 (installing the toolchain first when asked); 0 on success.</summary>
        private static int Esp32BuildCommand(string sketch, string? outFolder, bool install)
        {
            string logPath = Path.ChangeExtension(Path.GetFullPath(sketch), ".esp32build.txt");
            var log = new StringBuilder();
            void Line(string s)
            {
                lock (log) log.AppendLine(s);
            }

            try
            {
                // A sketch that is not there is said plainly (it used to throw out of the windowless run and leave the
                // process waiting on an error dialog).
                if (!File.Exists(sketch))
                {
                    Console.Error.WriteLine("Çizim bulunamadı: " + sketch);
                    Line("Çizim bulunamadı: " + sketch);
                    return 4;
                }

                if (!Firmware.ArduinoCompiler.Esp32ToolchainInstalled)
                {
                    if (!install)
                    {
                        Line("ESP32 araç zinciri kurulu değil: --esp32-install ile kurun.");
                        return 2;
                    }

                    if (!Firmware.ArduinoCompiler.InstallEsp32ToolchainAsync(Line).GetAwaiter().GetResult()) return 3;
                }

                string source = new RobotTwin.CoreSim.Formats.FirmwareSource { Source = File.ReadAllText(sketch) }.Bundle();
                var result = Firmware.ArduinoCompiler.CompileEsp32HardwareAsync(source, "cli-" + Path.GetFileNameWithoutExtension(sketch), Line).GetAwaiter().GetResult();
                foreach (var d in result.Diagnostics) Line($"{d.File}:{d.Line}:{d.Column}: {(d.IsError ? "hata" : "uyarı")}: {d.Message}");
                if (!result.Success || result.OutputFolder == null) return 1;
                if (outFolder != null)
                {
                    Directory.CreateDirectory(outFolder);
                    foreach (var file in Directory.GetFiles(result.OutputFolder, "sketch.ino.*").Where(f => !f.EndsWith(".map", StringComparison.OrdinalIgnoreCase)))
                        File.Copy(file, Path.Combine(outFolder, Path.GetFileNameWithoutExtension(sketch) + Path.GetFileName(file).Substring("sketch.ino".Length)), true);
                }

                Line($"tamam · flash {result.FlashBytes}/{result.FlashMax} B · RAM {result.RamBytes}/{result.RamMax} B · {result.Duration.TotalSeconds:F0} s");
                return 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Line("Derleme yapılamadı: " + ex.Message);
                return 4;
            }
            finally
            {
                try
                {
                    File.WriteAllText(logPath, log.ToString());
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    Console.Error.WriteLine(log.ToString());
                }
            }
        }

        private static int SelfTest(string? outPath)
        {
            var log = new StringBuilder();
            int failures = 0;
            var lib = PartLibrary.Default;
            foreach (var ex in ExampleProjects.All)
            {
                try
                {
                    var sch = ex.Build();
                    var compiled = CircuitCompiler.Compile(sch, lib);
                    var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 2e-4 });
                    sim.Initialize();
                    RobotTwin.CoreSim.Mechanics.MechatronicSystem? mech = null;
                    if (sch.Assembly != null && sch.Assembly.Kind != RobotTwin.CoreSim.Mechanics.VehicleKind.None)
                    {
                        mech = new RobotTwin.CoreSim.Mechanics.MechatronicSystem(sch.Assembly, sch, lib, compiled, sim);
                        if (mech.Flight != null)
                        {
                            mech.Flight.TargetAltitude = 0.5;
                            mech.Flight.Throttle = 0.5;
                            mech.Flight.Armed = true;
                        }
                    }

                    int steps = mech != null ? 1500 : 100;
                    for (int i = 0; i < steps; i++)
                    {
                        sim.Step(2e-3);
                        mech?.Advance(2e-3);
                    }

                    if (mech != null)
                    {
                        log.AppendLine($"{ex.Id,-15} vehicle={mech.Kind} mass={mech.TotalMass * 1000:F0}g t={mech.Time:F2}s dist={mech.Distance:F2}m alt={mech.Altitude:F2}m events={mech.DrainEvents().Count(e => e.Severe)}");
                    }
                    var board = BoardDocument.Synchronize(sch, lib);
                    board.Width = 90;
                    board.Height = 70;
                    var place = AutoPlacer.Place(board);
                    var route = new AutoRouter(board).Route();
                    var drc = DesignRuleChecker.Check(board);
                    int errors = drc.Count(v => v.Severity == DrcSeverity.Error);
                    log.AppendLine($"{ex.Id,-15} parts={sch.Parts.Count,2} nets={compiled.Netlist.Nets.Count,2} warn={compiled.Warnings.Count} " +
                                   $"sim=OK t={sim.Time:0.000}s fail={sim.Events.Count(e => e.Kind == AnalogEventKind.Failure)} " +
                                   $"pcb={board.Components.Count} {board.Width}x{board.Height}mm routed={route.Routed}/{route.Connections} vias={route.ViasAdded} drcErrors={errors}");
                    foreach (var w in compiled.Warnings) log.AppendLine("    warn: " + w);
                    foreach (var f in route.Failed) log.AppendLine("    unrouted: " + f);
                    foreach (var v in drc.Where(v => v.Severity == DrcSeverity.Error).Take(5)) log.AppendLine("    drc: " + v);
                    if (!route.Complete || errors > 0) failures++;
                }
                catch (Exception exn)
                {
                    failures++;
                    log.AppendLine($"{ex.Id,-15} EXCEPTION {exn.GetType().Name}: {exn.Message}");
                }
            }

            // [setup] The Arduino compiler that comes with the installers and the portable app really builds a sketch.
            if (File.Exists(Firmware.ArduinoCompiler.BundledCli))
            {
                try
                {
                    var build = Firmware.ArduinoCompiler.CompileAsync("void setup() { pinMode(13, OUTPUT); }\nvoid loop() { digitalWrite(13, !digitalRead(13)); delay(500); }\n",
                        "arduino:avr:uno", "selftest-blink").GetAwaiter().GetResult();
                    bool ok = build.Success && build.Hex != null && build.Hex.StartsWith(":", StringComparison.Ordinal);
                    log.AppendLine($"arduino-cli     bundled={Firmware.ArduinoCompiler.IsBundled(Firmware.ArduinoCompiler.FindCli())} build={(ok ? "OK" : "FAIL")} flash={build.FlashBytes}B in {build.Duration.TotalSeconds:F1}s");
                    if (!ok)
                    {
                        failures++;
                        log.AppendLine(build.Log);
                    }
                }
                catch (Exception exn) when (exn is System.ComponentModel.Win32Exception || exn is IOException || exn is InvalidOperationException)
                {
                    failures++;
                    log.AppendLine("arduino-cli     EXCEPTION " + exn.Message);
                }
            }

            log.AppendLine(failures == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures})");
            if (outPath != null) File.WriteAllText(outPath, log.ToString());
            return failures == 0 ? 0 : 1;
        }
    }
}
