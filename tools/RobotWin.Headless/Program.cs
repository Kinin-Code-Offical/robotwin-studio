using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using RobotTwin.CoreSim.Automation;

namespace RobotWin.Headless
{
    /// <summary>
    /// RobotWin Studio without a window: runs worlds / vehicles from the command line and serves the
    /// automation API (docs/API.md). Works on Windows, Linux and macOS (.NET 9).
    /// </summary>
    public static class Program
    {
        private const string Usage = @"RobotWin.Headless — RobotWin Studio başsız simülatör / headless simulator

Kullanım / usage:
  RobotWin.Headless --api <port> [--host 127.0.0.1] [--allow-remote] [--token T] [--parent-pid PID]
                    [--world FILE.rwworld | --project FILE.rwproj | --example car|line-follower|obstacle-avoider|quadcopter]
                    [--seed N] [--no-firmware] [--firmware-dir DIR]
  RobotWin.Headless --run SECONDS (--world ... | --example ...) [--dt 0.01] [--seed N]   (toplu koşu / batch run, JSON on stdout)
  RobotWin.Headless --experiment SPEC.json [--world FILE.rwworld] [--out DIR]
                    (deney / experiment: runs.csv, summary.csv, report.md, experiment.json; 'progress n/total' on stdout)

--api 0 picks a free port; the line 'ROBOTWIN_API_READY port=<n>' on stdout tells launchers it is listening.
";

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);
            string? Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            bool Flag(string name) => args.Contains(name);

            if (args.Length == 0 || Flag("--help") || Flag("-h"))
            {
                Console.WriteLine(Usage);
                return args.Length == 0 ? 1 : 0;
            }

            // [experiment] a designed experiment (CoreSim Automation/Experiment.cs), before any world is loaded for the API
            if (Arg("--experiment") is { } specPath) return Experiment(specPath, Arg("--world"), Arg("--out"));

            var host = new ApiHostOptions { Backend = "headless", Token = Arg("--token") };
            host.FirmwareSearchPath.Add(Path.Combine(AppContext.BaseDirectory, "Firmware"));
            if (Arg("--firmware-dir") is { } fwDir) host.FirmwareSearchPath.Insert(0, Path.GetFullPath(fwDir));
            using var session = new ApiSession(host);

            // Optional preload (same parameters as world.load).
            var load = new JsonObject { ["seed"] = int.Parse(Arg("--seed") ?? "0", CultureInfo.InvariantCulture), ["firmware"] = !Flag("--no-firmware") };
            if (Arg("--world") is { } world) load["path"] = Path.GetFullPath(world);
            else if (Arg("--project") is { } project) load["path"] = Path.GetFullPath(project);
            else if (Arg("--example") is { } example) load["example"] = example;
            bool preload = load.ContainsKey("path") || load.ContainsKey("example");

            try
            {
                if (preload) session.Call("world.load", load);
            }
            catch (ApiException ex)
            {
                Console.Error.WriteLine("Yüklenemedi: " + ex.Message);
                return 2;
            }

            if (Arg("--run") is { } runText)
            {
                if (!preload)
                {
                    Console.Error.WriteLine("--run için --world / --project / --example gerekli.");
                    return 2;
                }

                return Batch(session, double.Parse(runText, CultureInfo.InvariantCulture), double.Parse(Arg("--dt") ?? "0.01", CultureInfo.InvariantCulture));
            }

            if (Arg("--api") is not { } portText)
            {
                Console.Error.WriteLine(Usage);
                return 1;
            }

            var address = IPAddress.Parse(Arg("--host") ?? "127.0.0.1");
            if (!IPAddress.IsLoopback(address) && !Flag("--allow-remote"))
            {
                Console.Error.WriteLine("Yerel olmayan adres için --allow-remote gerekli (ve --token önerilir).");
                return 1;
            }

            using var done = new ManualResetEventSlim(false);
            host.Shutdown = () => ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(100);   // let the response go out first
                done.Set();
            });
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                done.Set();
            };

            using var server = new ApiServer(session);
            try
            {
                server.Start(int.Parse(portText, CultureInfo.InvariantCulture), address);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Console.Error.WriteLine("Port açılamadı: " + ex.Message);
                return 3;
            }

            Console.WriteLine($"ROBOTWIN_API_READY port={server.Port} host={server.Address} backend=headless api={ApiSession.ApiVersion}");
            Console.Out.Flush();

            if (Arg("--parent-pid") is { } pidText && int.TryParse(pidText, out int pid))
            {
                // Exit when the launching process (e.g. a Python training script) goes away.
                var watch = new Thread(() =>
                {
                    while (!done.IsSet)
                    {
                        try
                        {
                            using var parent = Process.GetProcessById(pid);
                            if (parent.HasExited) break;
                        }
                        catch (ArgumentException)
                        {
                            break;
                        }
                        catch (InvalidOperationException)
                        {
                            break;
                        }

                        Thread.Sleep(1000);
                    }

                    done.Set();
                }) { IsBackground = true };
                watch.Start();
            }

            done.Wait();
            server.Stop();
            return 0;
        }

        /// <summary>
        /// [experiment] --experiment spec.json [--world file.rwworld] [--out folder]: every combination of the spec's factors
        /// with its seeded repeats, the metrics reduced per run, statistics per combination. Progress goes to stdout as
        /// "progress n/total", then "ROBOTWIN_EXPERIMENT_DONE folder=…". 0: every run finished; 1: some failed; 2: no start.
        /// </summary>
        private static int Experiment(string specPath, string? worldPath, string? outFolder)
        {
            try
            {
                var spec = ExperimentSpec.FromJson(File.ReadAllText(specPath));
                string specDir = Path.GetDirectoryName(Path.GetFullPath(specPath)) ?? ".";
                string? world = worldPath ?? (spec.WorldFile is { Length: > 0 } wf ? (Path.IsPathRooted(wf) ? wf : Path.Combine(specDir, wf)) : null);
                if (world == null || !File.Exists(world))
                {
                    Console.Error.WriteLine("Dünya dosyası yok / no world file: --world or the spec's worldFile.");
                    return 2;
                }

                var doc = RobotTwin.CoreSim.Sandbox.WorldDocument.Load(world);
                string stem = string.Concat(spec.Name.Split(Path.GetInvalidFileNameChars()));
                string folder = Path.GetFullPath(outFolder ?? Path.Combine(specDir, stem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
                Directory.CreateDirectory(folder);
                var result = ExperimentRunner.Run(spec, doc, new ConsoleProgress());
                var utf8 = new System.Text.UTF8Encoding(false);
                File.WriteAllText(Path.Combine(folder, "runs.csv"), result.RunsCsv(), utf8);
                File.WriteAllText(Path.Combine(folder, "summary.csv"), result.SummaryCsv(), utf8);
                File.WriteAllText(Path.Combine(folder, "report.md"), result.Markdown(doc.Title), utf8);
                spec.WorldFile = Path.GetFullPath(world);
                File.WriteAllText(Path.Combine(folder, "experiment.json"), spec.ToJson(), utf8);
                Console.WriteLine("ROBOTWIN_EXPERIMENT_DONE folder=" + folder);
                return result.Runs.Exists(r => r.Error.Length > 0) ? 1 : 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is JsonException || ex is InvalidDataException)
            {
                Console.Error.WriteLine("Deney çalıştırılamadı / experiment failed to start: " + ex.Message);
                return 2;
            }
        }

        private sealed class ConsoleProgress : IProgress<(int Done, int Total)>
        {
            public void Report((int Done, int Total) value) => Console.WriteLine($"progress {value.Done}/{value.Total}");
        }

        private static int Batch(ApiSession session, double seconds, double dt)
        {
            int steps = Math.Max(1, (int)Math.Round(seconds / dt));
            var step = (JsonObject)session.Call("sim.step", new JsonObject { ["dt"] = dt, ["steps"] = steps })!;
            var result = new JsonObject
            {
                ["status"] = session.Call("sim.status", null),
                ["step"] = step,
                ["events"] = session.Call("events.poll", new JsonObject { ["max"] = 200 }),
                ["serial"] = session.Call("serial.read", null)
            };
            Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            return session.Runner?.Halted == true ? 4 : 0;
        }
    }
}
