using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [perf] The automatic performance regression check (<c>--perf-check</c>): every world template runs headless, with the
    /// bodies' shapes the world page uses, for the same simulated time, and what one simulated second costs (best of two,
    /// after a warm-up) is set against a baseline.
    /// Each cost is also divided by a fixed CPU workload timed in the same run, so a baseline from another computer still
    /// means something; on the same computer the raw times are compared. Slower than the tolerance is a regression: the
    /// report says which and the exit code is 1. <c>--perf-update</c> makes the run the new baseline.
    /// </summary>
    internal static class PerfCheck
    {
        public const string DefaultBaseline = "tools/perf/baseline.json";

        private sealed class Entry
        {
            public string Id = string.Empty;
            public double MsPerSecond = double.NaN;   // wall time per simulated second (ms)
            public double Score = double.NaN;         // the same over the calibration (dimensionless)
            public string? Error;
            public string Parts = string.Empty;       // where the time went, for the report
        }

        /// <summary>
        /// The check runs the code as the app does, tiered and with dynamic PGO, but in a process where methods start to be
        /// counted for the optimising tier at once (no 100 ms delay), and each world is warmed up long enough to be measured
        /// on its optimised code. With .NET's default delay a world measured early in a run still ran partly unoptimised
        /// code (the car cost 277 ms per simulated second measured alone, 103 after four other worlds). The check once ran
        /// with tiered compilation off instead; that is not how the app runs: without PGO .NET 10 ran our physics 26 % slower
        /// than .NET 9, with it 32 % faster (the SCARA cell: 108, 87 and 59 ms per simulated second). Unless this already is
        /// that process, the app starts itself again so and returns its exit code.
        /// </summary>
        public static int? RunFullyOptimised(string[] args)
        {
            if (Environment.GetEnvironmentVariable(ChildMarker) == "1" || Environment.ProcessPath is not { } exe) return null;
            var start = new ProcessStartInfo(exe) { UseShellExecute = false };
            foreach (var a in args) start.ArgumentList.Add(a);
            start.Environment[ChildMarker] = "1";
            start.Environment["DOTNET_TieredPGO"] = "1";
            start.Environment["DOTNET_TC_CallCountingDelayMs"] = "0";
            start.Environment.Remove("DOTNET_TieredCompilation");
            using var child = Process.Start(start);
            if (child == null) return null;
            child.WaitForExit();
            return child.ExitCode;
        }

        private const string ChildMarker = "ROBOTWIN_PERF_CHECK";

        /// <summary>Simulated time each world runs before it is timed: its methods reach the optimising tier.</summary>
        private const double WarmUp = 0.5;

        public static int Run(string reportPath, string? baselinePath, bool update, double seconds, double tolerance, IReadOnlyCollection<string>? only)
        {
            baselinePath ??= Path.Combine(RepoRoot() ?? AppContext.BaseDirectory, DefaultBaseline);
            var report = new StringBuilder();
            report.AppendLine($"RobotWin Studio performans denetimi · {DateTime.Now:yyyy-MM-dd HH:mm} · {seconds.ToString("0.##", CultureInfo.InvariantCulture)} s/şablon · tolerans %{tolerance * 100:0}");
            double calibration = Calibrate();
            report.AppendLine($"Kalibrasyon (64 kutu, 1 s fizik): {calibration.ToString("0.0", CultureInfo.InvariantCulture)} ms");
            Write(reportPath, report);

            // Every world once, untimed, first: the code they share reaches the optimising tier before any is timed (that
            // takes wall time, and a light world's warm-up is a few milliseconds of it), so a world's cost no longer depends
            // on what was measured before it.
            var chosen = Examples.WorldTemplates.All.Where(t => only == null || only.Contains(t.Id)).ToList();
            foreach (var template in chosen)
            {
                try
                {
                    Measure(template, WarmUp);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException || ex is KeyNotFoundException || ex is NotSupportedException || ex is ApiException)
                {
                }
            }

            System.Threading.Thread.Sleep(500);   // the optimising compiler works in the background
            var entries = new List<Entry>();
            foreach (var template in chosen)
            {
                var e = new Entry { Id = template.Id };
                entries.Add(e);
                try
                {
                    double best = double.PositiveInfinity;
                    for (int rep = 0; rep < 2; rep++)
                    {
                        // the worlds before leave garbage that the collector would otherwise sweep while this one is timed
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                        var (ms, parts) = Measure(template, seconds);
                        if (ms >= best) continue;
                        best = ms;
                        e.Parts = parts;
                    }

                    e.MsPerSecond = best / seconds;
                    e.Score = e.MsPerSecond / calibration;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException || ex is KeyNotFoundException || ex is NotSupportedException || ex is ApiException)
                {
                    e.Error = ex.Message;
                }

                report.AppendLine(e.Error == null
                    ? $"{e.Id,-16} {e.MsPerSecond.ToString("0.0", CultureInfo.InvariantCulture),9} ms/s   puan {e.Score.ToString("0.00", CultureInfo.InvariantCulture),7}   {e.Parts}"
                    : $"{e.Id,-16} HATA: {e.Error}");
                Write(reportPath, report);
            }

            var baseline = Load(baselinePath);
            int regressions = 0;
            if (baseline == null)
            {
                report.AppendLine($"Karşılaştırılacak ölçüm yok ({baselinePath}): bu koşu ölçüm olarak kaydedildi.");
                update = true;
            }
            else
            {
                // The same computer (calibration within 15 %): the raw times; another one: the scores.
                bool sameMachine = Math.Abs(calibration / baseline.Value.Calibration - 1) < 0.15;
                report.AppendLine();
                report.AppendLine($"Karşılaştırma ({baseline.Value.Created}, {(sameMachine ? "aynı bilgisayar: süreler" : "başka bilgisayar: kalibrasyona göre puanlar")}):");
                foreach (var e in entries.Where(x => x.Error == null))
                {
                    if (!baseline.Value.Entries.TryGetValue(e.Id, out var b)) continue;
                    double before = sameMachine ? b.MsPerSecond : b.Score, now = sameMachine ? e.MsPerSecond : e.Score;
                    if (!(before > 0)) continue;
                    double change = now / before - 1;
                    bool slower = change > tolerance;
                    if (slower) regressions++;
                    report.AppendLine($"{e.Id,-16} {Signed(change),8}   {(slower ? "YAVAŞLADI" : change < -tolerance ? "hızlandı" : "aynı")}");
                }

                foreach (var gone in baseline.Value.Entries.Keys.Where(k => entries.All(e => e.Id != k) && only == null))
                    report.AppendLine($"{gone,-16} (artık şablon değil)");
                report.AppendLine(regressions == 0 ? "Gerileme yok." : $"{regressions} şablonda gerileme var.");
            }

            if (update)
            {
                Save(baselinePath, calibration, seconds, entries);
                report.AppendLine("Ölçüm kaydedildi: " + baselinePath);
            }

            Write(reportPath, report);
            return regressions > 0 ? 1 : 0;
        }

        /// <summary>
        /// One run of a template: built and warmed up (firmware built, caches filled), then timed (ms), with where the time
        /// went: the boards' code, the circuit (with its solver's substeps per simulated second), and the mechanics' parts as
        /// the physics times them.
        /// </summary>
        private static (double Ms, string Parts) Measure(Examples.WorldTemplate template, double seconds)
        {
            var options = new RunnerOptions { Esp32Builder = Automation.StudioApiHost.BuildEsp32 };
            options.FirmwareSearchPath.Add(Path.Combine(AppContext.BaseDirectory, "Firmware"));
            // The bodies' shapes the world page gives the physics (the parts' 3D hulls, the cables' real pins), not the
            // headless tool's boxes: what a world costs in the app.
            var world = template.Build();
            using var runner = new WorldRunner(world, options, Automation.StudioApiHost.SceneGeometry.For(world));
            runner.Step(WarmUp);
            var t = runner.Physics?.Times;
            double[] Physics() => t == null ? new double[8] : new[] { t.Bodies, t.Cables, t.Liquids, t.Thermal, t.Drives, t.Air, t.Scripts, t.Sensors };
            var before = Physics();
            double fw0 = runner.FirmwareSeconds, ck0 = runner.CircuitSeconds;
            long steps0 = runner.CircuitSubsteps;
            var watch = Stopwatch.StartNew();
            runner.Step(seconds);
            double ms = watch.Elapsed.TotalMilliseconds;
            var after = Physics();
            string[] names = { "gövdeler", "kablolar", "sıvılar", "ısı", "sürücüler", "hava", "betikler", "sensörler" };
            var parts = new List<(string Name, double Ms)>
            {
                ("yazılım", (runner.FirmwareSeconds - fw0) * 1000),
                ($"devre ({((runner.CircuitSubsteps - steps0) / seconds).ToString("0", CultureInfo.InvariantCulture)} adım/s)", (runner.CircuitSeconds - ck0) * 1000)
            };
            for (int i = 0; i < names.Length; i++) parts.Add((names[i], (after[i] - before[i]) * 1000));
            return (ms, string.Join(" · ", parts.Where(p => p.Ms / ms >= 0.05).OrderByDescending(p => p.Ms)
                .Select(p => p.Name + " %" + (p.Ms / ms * 100).ToString("0", CultureInfo.InvariantCulture))));
        }

        /// <summary>
        /// A fixed piece of our own work, timed the same way: 64 boxes falling into a pile for 1 s at the solver's step,
        /// best of three (ms). What the templates cost is set against it.
        /// </summary>
        private static double Calibrate()
        {
            double best = double.PositiveInfinity;
            for (int rep = 0; rep < 5; rep++)   // the first ones also bring its code to the optimising tier
            {
                var s = new PhysicsScene { GroundMaterial = "wood" };
                for (int i = 0; i < 64; i++)
                    s.AddBody(SceneBody.Box("b" + i, new Vec3(0.05, 0.05, 0.05), 0.1, new Vec3(0.012 * (i % 8), 0.012 * (i / 8 % 8), 0.05 + 0.06 * (i / 8)), "wood"));
                var watch = Stopwatch.StartNew();
                for (int k = 0; k < 100; k++) s.Step(0.01);
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }

            return best;
        }

        private static string Signed(double change) =>
            (change >= 0 ? "+" : "−") + Math.Abs(change * 100).ToString("0.0", CultureInfo.InvariantCulture) + " %";

        private static void Write(string path, StringBuilder report)
        {
            try
            {
                File.WriteAllText(path, report.ToString(), new UTF8Encoding(false));
            }
            catch (IOException)
            {
            }
        }

        private readonly record struct Baseline(string Created, double Calibration, Dictionary<string, Entry> Entries);

        private static Baseline? Load(string path)
        {
            try
            {
                if (!File.Exists(path) || JsonNode.Parse(File.ReadAllText(path)) is not JsonObject root) return null;
                var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
                if (root["templates"] is JsonObject list)
                    foreach (var (id, node) in list)
                        if (node is JsonObject o)
                            entries[id] = new Entry { Id = id, MsPerSecond = (double?)o["ms_per_s"] ?? double.NaN, Score = (double?)o["score"] ?? double.NaN };
                return new Baseline((string?)root["created"] ?? "?", (double?)root["calibration_ms"] ?? double.NaN, entries);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is InvalidOperationException || ex is FormatException)
            {
                return null;
            }
        }

        private static void Save(string path, double calibration, double seconds, List<Entry> entries)
        {
            var list = new JsonObject();
            foreach (var e in entries.Where(x => x.Error == null))
                list[e.Id] = new JsonObject { ["ms_per_s"] = Math.Round(e.MsPerSecond, 2), ["score"] = Math.Round(e.Score, 4) };
            var root = new JsonObject
            {
                ["created"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                ["machine"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture) + " çekirdek",
                ["calibration_ms"] = Math.Round(calibration, 2),
                ["seconds"] = seconds,
                ["templates"] = list
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        }

        /// <summary>The repository the app runs from (its tools/ next to Studio/), for the default baseline; null when installed.</summary>
        private static string? RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "tools")) && Directory.Exists(Path.Combine(dir.FullName, "Studio"))) return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
