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
                // Contact sheets of every 3D component model (visual check of the model library).
                int cols = Arg("--gallery-columns") is { } gc ? int.Parse(gc, CultureInfo.InvariantCulture) : 4;
                var tile = (Arg("--gallery-tile") ?? "460x340").Split('x');
                Models3D.ModelGallery.Export(galleryDir, Arg("--gallery-filter"), cols, int.Parse(tile[0], CultureInfo.InvariantCulture), int.Parse(tile[1], CultureInfo.InvariantCulture));
                Shutdown(0);
                return;
            }

            if (args.Contains("--selftest"))
            {
                int code = SelfTest(Arg("--selftest-out"));
                Shutdown(code);
                return;
            }

            Shell.UserLibrary.LoadAll(RobotTwin.CoreSim.Design.PartLibrary.Default);
            var window = new MainWindow();
            MainWindow = window;

            if (Arg("--bench") is { } benchPath)
            {
                RunBench(window, benchPath, Arg("--bench-seconds"));
                return;
            }

            if (Arg("--snapshot") is { } snapshotPath)
            {
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

            if (Environment.GetCommandLineArgs().Contains("--nav-expanded")) window.ExpandRailForSnapshot();
            window.Navigate(page);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            int phase = 0;
            timer.Tick += (_, _) =>
            {
                phase++;
                if (phase == 1)
                {
                    window.PrepareSnapshot(page, simulate, fault);
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
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using (var fs = File.Create(path)) enc.Save(fs);
                Workspace.Current.Simulation.Stop();
                window.ForceClose();
            };
            timer.Start();
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
            var templates = Examples.WorldTemplates.All.ToList();
            var log = new StringBuilder($"RobotWin Studio kare hızı ölçümü · {DateTime.Now:yyyy-MM-dd HH:mm} · {secs:0.#} s/şablon · 1600×960{Environment.NewLine}");
            int index = -1;
            var phaseStart = DateTime.Now;
            int phase = 0;   // 0 = load, 1 = idle, 2 = running
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

                        window.NewWorld(templates[index].Build());
                        window.Navigate("world");
                        phase = 1;
                        phaseStart = DateTime.Now;
                        Perf.BeginRecording();
                        break;
                    case 1 when elapsed > 1.5:
                        log.AppendLine($"{templates[index].Id,-14} durgun     {Perf.EndRecording()}");
                        window.RunWorldForBench();
                        phase = 2;
                        phaseStart = DateTime.Now;
                        Perf.BeginRecording();
                        break;
                    case 2 when elapsed > secs:
                        log.AppendLine($"{templates[index].Id,-14} çalışırken {Perf.EndRecording()}");
                        window.StopWorldForBench();
                        phase = 0;
                        break;
                }
            };
            timer.Start();
        }

        /// <summary>Headless end-to-end check of every example: compile, simulate, lay out a PCB, run DRC.</summary>
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

            log.AppendLine(failures == 0 ? "SELFTEST PASS" : $"SELFTEST FAIL ({failures})");
            if (outPath != null) File.WriteAllText(outPath, log.ToString());
            return failures == 0 ? 0 : 1;
        }
    }
}
