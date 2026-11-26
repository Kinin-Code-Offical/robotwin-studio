using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[variants] One alternative after the same run as the others (<see cref="VariantComparison"/>).</summary>
    public sealed class VariantResult
    {
        /// <summary>The alternative's name; empty for the design as drawn.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Parts it fits.</summary>
        public int Parts { get; set; }

        /// <summary>Mean power its supplies give over the run (W).</summary>
        public double SupplyW { get; set; } = double.NaN;

        /// <summary>Highest power its supplies give at any moment of the run (W).</summary>
        public double PeakSupplyW { get; set; } = double.NaN;

        /// <summary>Charge taken from its supplies over the run (mAh, for battery life).</summary>
        public double ChargeMah { get; set; } = double.NaN;

        /// <summary>The hottest part at the end and its temperature (°C).</summary>
        public double HottestC { get; set; } = double.NaN;

        public string? Hottest { get; set; }

        /// <summary>Mean voltage of the chosen net over the run (V), NaN when none was chosen or it is not in this one.</summary>
        public double NetV { get; set; } = double.NaN;

        /// <summary>Parts that failed during the run, and why.</summary>
        public List<string> Failures { get; } = new List<string>();

        /// <summary>[variants-code] What each board printed over the run (its last lines), when the code ran.</summary>
        public Dictionary<string, string> Serial { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>[variants-code] The run's warnings (a board's crash, a module out of its supply range …).</summary>
        public List<string> Notes { get; } = new List<string>();

        public string? Error { get; set; }
    }

    /// <summary>
    /// [variants] The alternatives of a design side by side: each, and the design as drawn, runs the same simulated time
    /// from the same start, and what its supplies give, the charge it takes, its hottest part, a net's voltage and any
    /// part that failed are measured alike. [variants-code] Given <c>code</c> options, the boards' code runs too (a <see cref="Automation.CircuitRunner"/>
    /// per alternative), and what each board printed is kept beside the numbers.
    /// </summary>
    public static class VariantComparison
    {
        public static List<VariantResult> Run(Schematic schematic, PartLibrary library, double seconds = 1.0, string? net = null,
            double ambientC = 25.0, CancellationToken token = default, Automation.RunnerOptions? code = null, Formats.ProjectPackage? package = null)
        {
            var results = new List<VariantResult>();
            foreach (var variant in new DesignVariant?[] { null }.Concat(schematic.Variants))
            {
                token.ThrowIfCancellationRequested();
                var r = new VariantResult { Name = variant?.Name ?? string.Empty };
                results.Add(r);
                try
                {
                    Measure(Variants.Resolve(schematic, variant), library, seconds, net, ambientC, r, token, code, package);
                }
                catch (Exception ex) when (ex is AnalogSimulationException || ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException || ex is Automation.ApiException)
                {
                    r.Error = ex.Message;
                }
            }

            return results;
        }

        private static void Measure(Schematic schematic, PartLibrary library, double seconds, string? net, double ambientC, VariantResult r, CancellationToken token,
            Automation.RunnerOptions? code, Formats.ProjectPackage? package)
        {
            r.Parts = schematic.Parts.Count(p => library.Find(p.PartId) is { } d && d.Symbol != SymbolKind.Ground && d.Symbol != SymbolKind.PowerRail);
            if (code != null)
            {
                // [variants-code] the alternative with its boards' code, stepped as the Circuit page steps it
                using var runner = new Automation.CircuitRunner(schematic, package, code, ambientC, asGiven: true);
                Sample(runner.Compiled!, runner.Simulator!, dt => runner.Step(dt), seconds, net, r, token);
                foreach (var kv in runner.ReadSerial(null, false))
                {
                    var lines = kv.Value.Replace("\r", string.Empty).Split('\n').Where(l => l.Length > 0).ToList();
                    r.Serial[kv.Key] = string.Join("\n", lines.Skip(Math.Max(0, lines.Count - 8)));
                }

                foreach (var e in runner.DrainEvents().Where(e => e.Kind is Automation.RunnerEventKind.Warning or Automation.RunnerEventKind.Failure))
                    r.Notes.Add(e.Source + ": " + e.Message);
                return;
            }

            var compiled = CircuitCompiler.Compile(schematic, library, new CompileOptions { AmbientC = ambientC });
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-4, AmbientTempC = ambientC, EnableDamage = true });
            try
            {
                sim.Initialize(InitialState.OperatingPoint);
            }
            catch (AnalogSimulationException)
            {
                sim.Initialize(InitialState.Zero);
            }

            Sample(compiled, sim, dt => sim.Step(dt), seconds, net, r, token);
        }

        /// <summary>The same measurements whatever steps the run: its supplies' power and charge, a net's voltage, the hottest part, failures.</summary>
        private static void Sample(CompiledCircuit compiled, AnalogSimulator sim, Action<double> step, double seconds, string? net, VariantResult r, CancellationToken token)
        {

            var sources = compiled.Circuit.Devices.ToList();
            int node = net != null && compiled.NodeByNet.TryGetValue(net, out int n) ? n : -1;
            double energy = 0, charge = 0, area = 0, peak = 0;
            const double dt = 1e-3;
            int steps = Math.Max(1, (int)Math.Ceiling(seconds / dt));
            for (int i = 0; i < steps; i++)
            {
                if ((i & 63) == 0) token.ThrowIfCancellationRequested();
                step(dt);
                double p = 0, amps = 0;
                foreach (var d in sources)
                {
                    switch (d)
                    {
                        case VoltageSource v:   // batteries too
                            p += Math.Max(0, v.Voltage * v.Current);
                            amps += Math.Max(0, v.Current);
                            break;
                        case BenchSupply b:
                            p += Math.Max(0, b.Power);
                            amps += Math.Max(0, b.OutputCurrent);
                            break;
                    }
                }

                energy += p * dt;
                charge += amps * dt;
                peak = Math.Max(peak, p);
                if (node >= 0) area += sim.NodeVoltage(node) * dt;
            }

            double t = steps * dt;
            r.SupplyW = energy / t;
            r.PeakSupplyW = peak;
            r.ChargeMah = charge / 3.6;
            if (node >= 0) r.NetV = area / t;
            foreach (var d in compiled.Circuit.Devices)
            {
                if (!sim.TryGetSignal(d.Id, "T", out double temp) || !(double.IsNaN(r.HottestC) || temp > r.HottestC)) continue;
                r.HottestC = temp;
                r.Hottest = d.Id;
            }

            foreach (var e in sim.Events.Where(e => e.Kind == AnalogEventKind.Failure)) r.Failures.Add(e.DeviceId + ": " + e.Message);
        }
    }
}
