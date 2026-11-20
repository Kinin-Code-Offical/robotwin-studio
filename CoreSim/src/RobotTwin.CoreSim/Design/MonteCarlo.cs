using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>What is measured on the probed net in every Monte Carlo run.</summary>
    public enum MonteCarloMetric
    {
        /// <summary>Voltage at the end of the run.</summary>
        Final,
        Mean,
        Max,
        Min,
        PeakToPeak,
        Rms
    }

    public sealed class MonteCarloOptions
    {
        public int Runs { get; set; } = 50;
        public int Seed { get; set; } = 1;

        /// <summary>Net whose voltage is measured.</summary>
        public string Net { get; set; } = string.Empty;

        public MonteCarloMetric Metric { get; set; } = MonteCarloMetric.Final;

        /// <summary>Simulated time of each run.</summary>
        public double Duration { get; set; } = 10e-3;

        /// <summary>The measurement window starts here (skip power-on transients).</summary>
        public double MeasureFrom { get; set; }

        public double MaxStep { get; set; } = 1e-4;
        public double AmbientC { get; set; } = 25.0;

        /// <summary>Parts may still burn during a run (off by default: the spread of the healthy circuit is wanted).</summary>
        public bool EnableDamage { get; set; }

        public int Bins { get; set; }
    }

    public sealed class MonteCarloResult
    {
        public List<double> Values { get; } = new List<double>();
        public double Nominal { get; set; } = double.NaN;
        public double Min { get; set; } = double.NaN;
        public double Max { get; set; } = double.NaN;
        public double Mean { get; set; } = double.NaN;
        public double StdDev { get; set; } = double.NaN;
        public int[] Histogram { get; set; } = Array.Empty<int>();
        public double BinStart { get; set; }
        public double BinWidth { get; set; }
        public List<string> Errors { get; } = new List<string>();
        public bool Cancelled { get; set; }
        public MonteCarloOptions Options { get; set; } = new MonteCarloOptions();

        /// <summary>Run index (1-based) with the lowest and the highest value.</summary>
        public int MinRun { get; set; }
        public int MaxRun { get; set; }

        public void Summarize(int bins)
        {
            var ok = Values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (ok.Count == 0) return;
            Min = ok.Min();
            Max = ok.Max();
            Mean = ok.Average();
            double mean = Mean;
            StdDev = ok.Count > 1 ? Math.Sqrt(ok.Sum(v => (v - mean) * (v - mean)) / (ok.Count - 1)) : 0.0;
            MinRun = Values.IndexOf(Min) + 1;
            MaxRun = Values.IndexOf(Max) + 1;
            if (bins <= 0) bins = Math.Max(5, Math.Min(24, (int)Math.Ceiling(Math.Sqrt(ok.Count)) + 2));
            double span = Max - Min;
            if (span <= Math.Abs(Mean) * 1e-12 || span <= 1e-15)
            {
                span = Math.Max(Math.Abs(Mean) * 1e-3, 1e-9);
                BinStart = Mean - span / 2;
            }
            else
            {
                BinStart = Min;
            }

            BinWidth = span / bins;
            Histogram = new int[bins];
            foreach (double v in ok)
            {
                int b = (int)((v - BinStart) / BinWidth);
                if (b >= bins) b = bins - 1;
                if (b < 0) b = 0;
                Histogram[b]++;
            }
        }
    }

    /// <summary>
    /// Monte Carlo tolerance analysis: the circuit is compiled N times, each time with a new
    /// draw of every part's value from its tolerance band, simulated, and one measurement of the
    /// chosen net is collected. Firmware is not run; boards keep their pins as inputs.
    /// </summary>
    public static class MonteCarlo
    {
        public static MonteCarloResult Run(Schematic schematic, PartLibrary library, MonteCarloOptions options,
            CancellationToken cancel = default, IProgress<int>? progress = null)
        {
            var result = new MonteCarloResult { Options = options };
            var settings = new ToleranceSettings { Mode = ToleranceMode.MonteCarlo, Seed = options.Seed, Runs = options.Runs };
            result.Nominal = RunOnce(schematic, library, options, null, 0, out string? nominalError);
            if (nominalError != null) result.Errors.Add("nominal: " + nominalError);

            for (int run = 1; run <= options.Runs; run++)
            {
                if (cancel.IsCancellationRequested)
                {
                    result.Cancelled = true;
                    break;
                }

                double v = RunOnce(schematic, library, options, settings, run, out string? error);
                if (error != null) result.Errors.Add($"#{run}: {error}");
                result.Values.Add(v);
                progress?.Report(run);
            }

            result.Summarize(options.Bins);
            return result;
        }

        /// <summary>One simulation; returns the metric on the net (NaN when the run failed).</summary>
        public static double RunOnce(Schematic schematic, PartLibrary library, MonteCarloOptions options,
            ToleranceSettings? settings, int run, out string? error)
        {
            error = null;
            try
            {
                var compiled = CircuitCompiler.Compile(schematic, library, new CompileOptions
                {
                    Tolerances = settings,
                    ToleranceRun = run,
                    AmbientC = options.AmbientC
                });
                if (!compiled.NodeByNet.TryGetValue(options.Net, out int node))
                {
                    error = $"'{options.Net}' hattı yok";
                    return double.NaN;
                }

                var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions
                {
                    MaxStep = options.MaxStep,
                    AmbientTempC = options.AmbientC,
                    EnableDamage = options.EnableDamage
                });
                try
                {
                    sim.Initialize(InitialState.OperatingPoint);
                }
                catch (AnalogSimulationException)
                {
                    sim.Initialize(InitialState.Zero);
                }

                double min = double.PositiveInfinity, max = double.NegativeInfinity, area = 0, area2 = 0, span = 0;
                double prevT = double.NaN, prevV = double.NaN;
                void Sample(double t)
                {
                    double v = sim.NodeVoltage(node);
                    if (t >= options.MeasureFrom)
                    {
                        if (v < min) min = v;
                        if (v > max) max = v;
                        if (!double.IsNaN(prevT) && prevT >= options.MeasureFrom)
                        {
                            double dt = t - prevT;
                            area += 0.5 * (v + prevV) * dt;
                            area2 += 0.5 * (v * v + prevV * prevV) * dt;
                            span += dt;
                        }
                    }

                    prevT = t;
                    prevV = v;
                }

                sim.SubstepAccepted = Sample;
                Sample(0.0);
                double duration = Math.Max(options.Duration, 1e-6);
                double chunk = Math.Min(duration / 20.0, 5e-3);
                while (sim.Time < duration - 1e-15)
                {
                    sim.Step(Math.Min(chunk, duration - sim.Time));
                }

                switch (options.Metric)
                {
                    case MonteCarloMetric.Mean: return span > 0 ? area / span : prevV;
                    case MonteCarloMetric.Max: return max;
                    case MonteCarloMetric.Min: return min;
                    case MonteCarloMetric.PeakToPeak: return max - min;
                    case MonteCarloMetric.Rms: return span > 0 ? Math.Sqrt(Math.Max(0, area2 / span)) : Math.Abs(prevV);
                    default: return sim.NodeVoltage(node);
                }
            }
            catch (AnalogSimulationException ex)
            {
                error = ex.Message;
                return double.NaN;
            }
        }

        public static string MetricName(MonteCarloMetric metric)
        {
            switch (metric)
            {
                case MonteCarloMetric.Mean: return "Ortalama";
                case MonteCarloMetric.Max: return "En yüksek";
                case MonteCarloMetric.Min: return "En düşük";
                case MonteCarloMetric.PeakToPeak: return "Tepeden tepeye";
                case MonteCarloMetric.Rms: return "RMS";
                default: return "Son değer";
            }
        }
    }
}
