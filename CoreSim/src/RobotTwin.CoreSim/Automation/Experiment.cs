using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// [experiment] A designed experiment on a world: the factors to vary (each over its values), how many seeded
    /// repeats of every combination, how long to run, and what to measure. Saved beside the results, so a run can be
    /// repeated exactly: same world, same values, same seeds.
    /// </summary>
    public sealed class ExperimentSpec
    {
        public string Name { get; set; } = "Deney";

        /// <summary>The world file (.rwworld JSON) when the spec stands on its own; the host's open world otherwise.</summary>
        public string? WorldFile { get; set; }

        /// <summary>Simulated seconds per run, and how often the metrics are sampled.</summary>
        public double Duration { get; set; } = 5;

        public double SampleInterval { get; set; } = 0.01;

        /// <summary>Runs per combination, each with its own seed (sensor noise, tolerances, random()): Seed, Seed + 1, …</summary>
        public int Repeats { get; set; } = 3;

        public int Seed { get; set; } = 1;

        /// <summary>Part values drawn from their tolerance bands in every run (Monte Carlo), not the nominal ones.</summary>
        public bool Tolerances { get; set; }

        /// <summary>Run the boards' firmware (off: the circuit and the mechanics alone).</summary>
        public bool Firmware { get; set; } = true;

        public List<ExperimentFactor> Factors { get; set; } = new List<ExperimentFactor>();

        public List<ExperimentMetric> Metrics { get; set; } = new List<ExperimentMetric>();

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string ToJson() => JsonSerializer.Serialize(this, Json);

        public static ExperimentSpec FromJson(string json) => JsonSerializer.Deserialize<ExperimentSpec>(json, Json) ?? new ExperimentSpec();
    }

    /// <summary>
    /// [experiment] One factor: what it sets and the values it takes. <see cref="Target"/> is
    /// "environment.&lt;setting&gt;" (AmbientC, Gravity, Friction, WindX …), "&lt;object&gt;.&lt;parameter&gt;" (a part's
    /// parameter by its key: "rpm", "fault_value" …), "&lt;object&gt;.x|y|z|yaw" (its placed pose, m and degrees) or
    /// "&lt;object&gt;.mass" (kg); an object by its name or id. Values are listed, or a range From–To in Steps.
    /// </summary>
    public sealed class ExperimentFactor
    {
        public string Target { get; set; } = "";

        public List<string> Values { get; set; } = new List<string>();

        public double? From { get; set; }

        public double? To { get; set; }

        public int Steps { get; set; } = 3;

        public IReadOnlyList<string> Levels()
        {
            if (Values.Count > 0) return Values;
            if (From is not double a || To is not double b) return Array.Empty<string>();
            int n = Math.Max(1, Steps);
            return Enumerable.Range(0, n).Select(i => (n == 1 ? a : a + (b - a) * i / (n - 1)).ToString("R", CultureInfo.InvariantCulture)).ToList();
        }
    }

    /// <summary>
    /// [experiment] One measurement per run: <see cref="Source"/> is "&lt;object&gt;.x|y|z" (m), ".speed" (m/s), ".distance"
    /// (path travelled, m), ".roll|pitch|yaw" (degrees), ".temperature" (°C) of an object, or "node:&lt;net or part.pin&gt;" (V);
    /// <see cref="Reduce"/> turns its samples into one number: final, mean, min, max, rms, range.
    /// </summary>
    public sealed class ExperimentMetric
    {
        public string Name { get; set; } = "";

        public string Source { get; set; } = "";

        public string Reduce { get; set; } = "final";
    }

    /// <summary>[experiment] What a whole experiment gave: every run, and every combination's statistics over its seeds.</summary>
    public sealed class ExperimentResult
    {
        public sealed class Run
        {
            public int Combination { get; internal set; }
            public int Seed { get; internal set; }
            public string[] Levels { get; internal set; } = Array.Empty<string>();
            public double[] Values { get; internal set; } = Array.Empty<double>();
            public string Error { get; internal set; } = "";
        }

        /// <summary>A metric over one combination's runs: count, mean, sample standard deviation, 95 % confidence half-width.</summary>
        public sealed class Stat
        {
            public int N { get; internal set; }
            public double Mean { get; internal set; }
            public double Sd { get; internal set; }
            public double Ci95 { get; internal set; }
            public double Min { get; internal set; }
            public double Max { get; internal set; }
        }

        public ExperimentSpec Spec { get; internal set; } = new ExperimentSpec();
        public List<string> Factors { get; } = new List<string>();
        public List<string> Metrics { get; } = new List<string>();
        public List<string[]> Combinations { get; } = new List<string[]>();
        public List<Run> Runs { get; } = new List<Run>();
        public DateTime Started { get; internal set; }
        public TimeSpan Elapsed { get; internal set; }

        /// <summary>Statistics of metric <paramref name="m"/> over combination <paramref name="c"/>'s runs that finished.</summary>
        public Stat StatOf(int c, int m)
        {
            var xs = Runs.Where(r => r.Combination == c && r.Error.Length == 0 && !double.IsNaN(r.Values[m])).Select(r => r.Values[m]).ToList();
            var s = new Stat { N = xs.Count };
            if (xs.Count == 0) return s;
            s.Mean = xs.Average();
            s.Min = xs.Min();
            s.Max = xs.Max();
            if (xs.Count > 1)
            {
                s.Sd = Math.Sqrt(xs.Sum(x => (x - s.Mean) * (x - s.Mean)) / (xs.Count - 1));
                s.Ci95 = StudentT95(xs.Count - 1) * s.Sd / Math.Sqrt(xs.Count);
            }

            return s;
        }

        /// <summary>Two-sided 95 % critical value of Student's t for <paramref name="df"/> degrees of freedom.</summary>
        public static double StudentT95(int df)
        {
            double[] t = { 12.706, 4.303, 3.182, 2.776, 2.571, 2.447, 2.365, 2.306, 2.262, 2.228, 2.201, 2.179, 2.160, 2.145, 2.131, 2.120, 2.110, 2.101, 2.093, 2.086, 2.080, 2.074, 2.069, 2.064, 2.060, 2.056, 2.052, 2.048, 2.045, 2.042 };
            if (df < 1) return double.NaN;
            if (df <= t.Length) return t[df - 1];
            return df <= 60 ? 2.042 - (df - 30) * (2.042 - 2.000) / 30 : df <= 120 ? 2.000 - (df - 60) * (2.000 - 1.980) / 60 : 1.960;
        }

        private static string N(double v) => double.IsNaN(v) ? "" : v.ToString("G9", CultureInfo.InvariantCulture);

        private static string Q(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

        /// <summary>Every run: its combination, seed, factor levels and metrics.</summary>
        public string RunsCsv()
        {
            var sb = new StringBuilder();
            sb.Append("combination,seed,").Append(string.Join(",", Factors.Select(Q))).Append(Factors.Count > 0 ? "," : "").Append(string.Join(",", Metrics.Select(Q))).Append(",error\n");
            foreach (var r in Runs)
                sb.Append(r.Combination + 1).Append(',').Append(r.Seed).Append(',').Append(string.Join(",", r.Levels.Select(Q))).Append(Factors.Count > 0 ? "," : "")
                  .Append(string.Join(",", r.Values.Select(N))).Append(',').Append(Q(r.Error)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Every combination with each metric's n, mean, standard deviation and 95 % confidence interval.</summary>
        public string SummaryCsv()
        {
            var sb = new StringBuilder();
            sb.Append("combination,").Append(string.Join(",", Factors.Select(Q))).Append(Factors.Count > 0 ? "," : "");
            sb.Append(string.Join(",", Metrics.Select(m => $"{Q(m + " n")},{Q(m + " mean")},{Q(m + " sd")},{Q(m + " ci95_low")},{Q(m + " ci95_high")}"))).Append('\n');
            for (int c = 0; c < Combinations.Count; c++)
            {
                sb.Append(c + 1).Append(',').Append(string.Join(",", Combinations[c].Select(Q))).Append(Factors.Count > 0 ? "," : "");
                sb.Append(string.Join(",", Metrics.Select((_, m) =>
                {
                    var s = StatOf(c, m);
                    return $"{s.N},{N(s.Mean)},{N(s.Sd)},{N(s.Mean - s.Ci95)},{N(s.Mean + s.Ci95)}";
                }))).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>A readable report: what was run, then a table per metric of mean ± 95 % CI for every combination.</summary>
        public string Markdown(string worldTitle)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("# ").Append(Spec.Name).Append("\n\n");
            sb.Append("- World: ").Append(worldTitle).Append('\n');
            sb.Append("- Started: ").Append(Started.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append(", took ").Append(Elapsed.TotalSeconds.ToString("0.0", inv)).Append(" s\n");
            sb.Append("- Each run: ").Append(Spec.Duration.ToString("0.###", inv)).Append(" s simulated, sampled every ").Append((Spec.SampleInterval * 1000).ToString("0.###", inv)).Append(" ms\n");
            sb.Append("- ").Append(Combinations.Count).Append(" combinations × ").Append(Spec.Repeats).Append(" seeds (").Append(Spec.Seed).Append('–').Append(Spec.Seed + Spec.Repeats - 1).Append(") = ").Append(Runs.Count).Append(" runs");
            int failed = Runs.Count(r => r.Error.Length > 0);
            if (failed > 0) sb.Append(", ").Append(failed).Append(" failed");
            sb.Append('\n');
            sb.Append("- Part tolerances: ").Append(Spec.Tolerances ? "drawn per run (Monte Carlo)" : "nominal").Append("; firmware: ").Append(Spec.Firmware ? "on" : "off").Append("\n\n");
            foreach (var f in Spec.Factors) sb.Append("Factor `").Append(f.Target).Append("`: ").Append(string.Join(", ", f.Levels())).Append('\n');
            foreach (var m in Spec.Metrics) sb.Append("Metric **").Append(m.Name).Append("**: ").Append(m.Reduce).Append(" of `").Append(m.Source).Append("`\n");
            for (int m = 0; m < Metrics.Count; m++)
            {
                sb.Append("\n## ").Append(Metrics[m]).Append("\n\n| ");
                foreach (var f in Factors) sb.Append(f).Append(" | ");
                sb.Append("n | mean | sd | 95 % CI |\n|");
                for (int i = 0; i < Factors.Count + 4; i++) sb.Append(" --- |");
                sb.Append('\n');
                for (int c = 0; c < Combinations.Count; c++)
                {
                    var s = StatOf(c, m);
                    sb.Append("| ");
                    foreach (var level in Combinations[c]) sb.Append(level).Append(" | ");
                    sb.Append(s.N).Append(" | ").Append(N(Math.Round(s.Mean, 6))).Append(" | ").Append(N(Math.Round(s.Sd, 6))).Append(" | ");
                    sb.Append(s.N > 1 ? $"{N(Math.Round(s.Mean - s.Ci95, 6))} … {N(Math.Round(s.Mean + s.Ci95, 6))}" : "—").Append(" |\n");
                }
            }

            foreach (var r in Runs.Where(r => r.Error.Length > 0)) sb.Append("\nRun ").Append(r.Combination + 1).Append('/').Append(r.Seed).Append(" failed: ").Append(r.Error).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// [experiment] Runs an <see cref="ExperimentSpec"/> headless: every combination of the factors' levels, each with
    /// its seeds, on a fresh copy of the world in <see cref="WorldRunner"/> (the same circuit, physics and sensors as the
    /// app), sampling the metrics through the run and reducing them to one number per run.
    /// </summary>
    public static class ExperimentRunner
    {
        public static ExperimentResult Run(ExperimentSpec spec, WorldDocument world, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancel = default)
        {
            if (spec.Metrics.Count == 0) throw new ArgumentException("Ölçülecek bir şey yok: en az bir ölçüt ekleyin.");
            var result = new ExperimentResult { Spec = spec, Started = DateTime.Now };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            result.Factors.AddRange(spec.Factors.Select(f => f.Target));
            result.Metrics.AddRange(spec.Metrics.Select(m => string.IsNullOrWhiteSpace(m.Name) ? m.Reduce + " " + m.Source : m.Name));
            var levels = spec.Factors.Select(f => f.Levels()).ToList();
            foreach (var (f, l) in spec.Factors.Zip(levels, (f, l) => (f, l)))
                if (l.Count == 0) throw new ArgumentException($"'{f.Target}' için değer yok.");
            // Every combination, the first factor slowest.
            var combos = new List<string[]> { Array.Empty<string>() };
            foreach (var l in levels) combos = combos.SelectMany(c => l.Select(v => c.Append(v).ToArray())).ToList();
            result.Combinations.AddRange(combos);
            string json = world.ToJson();
            int repeats = Math.Max(1, spec.Repeats), total = combos.Count * repeats, done = 0;
            for (int c = 0; c < combos.Count; c++)
            {
                for (int r = 0; r < repeats; r++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var run = new ExperimentResult.Run { Combination = c, Seed = spec.Seed + r, Levels = combos[c], Values = Enumerable.Repeat(double.NaN, spec.Metrics.Count).ToArray() };
                    try
                    {
                        var w = WorldDocument.FromJson(json);
                        for (int f = 0; f < spec.Factors.Count; f++) Apply(w, spec.Factors[f].Target, combos[c][f]);
                        run.Values = Measure(w, spec, run.Seed, cancel);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        run.Error = ex.Message;
                    }

                    result.Runs.Add(run);
                    progress?.Report((++done, total));
                }
            }

            result.Elapsed = watch.Elapsed;
            return result;
        }

        private static WorldObject Find(WorldDocument w, string key) =>
            w.Objects.FirstOrDefault(o => string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase))
            ?? w.Find(key) ?? throw new ArgumentException($"Dünyada '{key}' adında bir nesne yok.");

        private static double Num(string v) => double.Parse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>Sets one factor's level on a world copy.</summary>
        public static void Apply(WorldDocument w, string target, string value)
        {
            int dot = target.LastIndexOf('.');
            if (dot <= 0) throw new ArgumentException($"'{target}' bir hedef değil: nesne.ayar ya da environment.ayar yazın.");
            string head = target.Substring(0, dot), key = target.Substring(dot + 1);
            if (head.Equals("environment", StringComparison.OrdinalIgnoreCase) || head.Equals("ortam", StringComparison.OrdinalIgnoreCase))
            {
                var prop = typeof(WorldSpec).GetProperties().FirstOrDefault(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                           ?? throw new ArgumentException($"Ortamda '{key}' ayarı yok.");
                object v = prop.PropertyType == typeof(double) || prop.PropertyType == typeof(double?) ? Num(value)
                    : prop.PropertyType == typeof(bool) ? bool.Parse(value)
                    : prop.PropertyType == typeof(string) ? value
                    : throw new ArgumentException($"Ortamın '{key}' ayarı deneyle değiştirilemez.");
                prop.SetValue(w.Environment, v);
                return;
            }

            var o = Find(w, head);
            var pose = o.Pose;
            switch (key.ToLowerInvariant())
            {
                case "x":
                    o.Pose = new WorldPose { X = Num(value), Y = pose.Y, Z = pose.Z, Roll = pose.Roll, Pitch = pose.Pitch, Yaw = pose.Yaw };
                    return;
                case "y":
                    o.Pose = new WorldPose { X = pose.X, Y = Num(value), Z = pose.Z, Roll = pose.Roll, Pitch = pose.Pitch, Yaw = pose.Yaw };
                    return;
                case "z":
                    o.Pose = new WorldPose { X = pose.X, Y = pose.Y, Z = Num(value), Roll = pose.Roll, Pitch = pose.Pitch, Yaw = pose.Yaw };
                    return;
                case "yaw":
                    o.Pose = new WorldPose { X = pose.X, Y = pose.Y, Z = pose.Z, Roll = pose.Roll, Pitch = pose.Pitch, Yaw = Num(value) };
                    return;
                case "mass":
                    o.MassKg = Num(value);
                    return;
                default:
                    o.Parameters[key] = value;
                    return;
            }
        }

        private sealed class Probe
        {
            public Func<double> Read = () => double.NaN;
            public string Reduce = "final";
            public double Sum, SumSq, Min = double.PositiveInfinity, Max = double.NegativeInfinity, Last = double.NaN;
            public int Count;
            public Vec3? LastPos;
            public double Distance;
            public bool IsDistance;
        }

        private static double[] Measure(WorldDocument w, ExperimentSpec spec, int seed, CancellationToken cancel)
        {
            var options = new RunnerOptions { Seed = seed, Firmware = spec.Firmware };
            if (spec.Tolerances)
            {
                // A new draw of every part's value from its tolerance band per run (the seed picks the "board").
                options.Tolerances = new Design.ToleranceSettings { Mode = Design.ToleranceMode.MonteCarlo, Seed = spec.Seed };
                options.ToleranceRun = seed - spec.Seed;
            }

            using var runner = new WorldRunner(w, options);
            var probes = spec.Metrics.Select(m => ProbeOf(runner, w, m)).ToList();
            double dt = Math.Max(1e-4, spec.SampleInterval);
            void Sample()
            {
                foreach (var p in probes)
                {
                    double v = p.Read();
                    if (p.IsDistance)
                    {
                        v = p.Distance;
                    }

                    if (double.IsNaN(v)) continue;
                    p.Last = v;
                    p.Sum += v;
                    p.SumSq += v * v;
                    p.Count++;
                    p.Min = Math.Min(p.Min, v);
                    p.Max = Math.Max(p.Max, v);
                }
            }

            Sample();
            while (runner.Time < spec.Duration - 1e-9)
            {
                cancel.ThrowIfCancellationRequested();
                runner.Step(Math.Min(dt, spec.Duration - runner.Time));
                Sample();
            }

            return probes.Select(p => p.Count == 0 ? double.NaN : p.Reduce switch
            {
                "mean" or "ortalama" => p.Sum / p.Count,
                "min" or "en az" => p.Min,
                "max" or "en çok" => p.Max,
                "rms" => Math.Sqrt(p.SumSq / p.Count),
                "range" or "aralık" => p.Max - p.Min,
                _ => p.Last
            }).ToArray();
        }

        private static Probe ProbeOf(WorldRunner runner, WorldDocument w, ExperimentMetric m)
        {
            var p = new Probe { Reduce = (m.Reduce ?? "final").Trim().ToLowerInvariant() };
            string src = m.Source.Trim();
            if (src.StartsWith("node:", StringComparison.OrdinalIgnoreCase))
            {
                string node = src.Substring(5);
                p.Read = () => runner.NodeVoltage(node);
                return p;
            }

            int dot = src.LastIndexOf('.');
            if (dot <= 0) throw new ArgumentException($"'{src}' bir ölçüt değil: nesne.x, nesne.speed … ya da node:… yazın.");
            var o = Find(w, src.Substring(0, dot));
            string key = src.Substring(dot + 1).ToLowerInvariant();
            BodyState? State() => runner.TryGetBody(o.Id, out var s) ? s : (BodyState?)null;
            const double deg = 180 / Math.PI;
            switch (key)
            {
                case "x":
                    p.Read = () => State()?.Position.X ?? double.NaN;
                    break;
                case "y":
                    p.Read = () => State()?.Position.Y ?? double.NaN;
                    break;
                case "z":
                    p.Read = () => State()?.Position.Z ?? double.NaN;
                    break;
                case "speed":
                    p.Read = () => State()?.Velocity.Length ?? double.NaN;
                    break;
                case "roll":
                    p.Read = () => State() is { } s ? s.Orientation.ToEuler().Roll * deg : double.NaN;
                    break;
                case "pitch":
                    p.Read = () => State() is { } s ? s.Orientation.ToEuler().Pitch * deg : double.NaN;
                    break;
                case "yaw":
                    p.Read = () => State() is { } s ? s.Orientation.ToEuler().Yaw * deg : double.NaN;
                    break;
                case "temperature":
                    p.Read = () => runner.Physics?.TemperatureOf(o.Id) ?? double.NaN;
                    break;
                case "distance":
                    p.IsDistance = true;
                    p.Read = () =>
                    {
                        if (State() is not { } s) return double.NaN;
                        if (p.LastPos is { } last) p.Distance += (s.Position - last).Length;
                        p.LastPos = s.Position;
                        return p.Distance;
                    };
                    break;
                default:
                    throw new ArgumentException($"'{key}' ölçülemiyor: x, y, z, speed, distance, roll, pitch, yaw, temperature ya da node:… kullanın.");
            }

            return p;
        }
    }
}
