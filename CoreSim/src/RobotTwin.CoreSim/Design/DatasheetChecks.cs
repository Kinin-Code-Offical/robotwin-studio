using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [datasheet] One figure of a part's datasheet set against its model: what is measured, under which conditions,
    /// the datasheet's typical value and the range it guarantees (min … max; a typical-only figure gets the tolerance
    /// the check states), and how the model is measured: the part built as the library builds it, in a small test
    /// circuit, simulated.
    /// </summary>
    public sealed class DatasheetCheck
    {
        /// <summary>The quantity and its test conditions ("Yük regülasyonu, 5 mA → 1,5 A").</summary>
        public string Quantity { get; init; } = string.Empty;

        /// <summary>Where the figure comes from (the datasheet, its table or figure).</summary>
        public string Source { get; init; } = string.Empty;

        /// <summary>The unit the values are shown in, and its size in SI (mV: 1e-3).</summary>
        public string Unit { get; init; } = string.Empty;

        public double Scale { get; init; } = 1;

        /// <summary>The datasheet's typical value (SI), NaN when it gives none.</summary>
        public double Typical { get; init; } = double.NaN;

        /// <summary>The range the model must fall in (SI): the datasheet's min … max, or typical ± the stated tolerance.</summary>
        public double Low { get; init; } = double.NegativeInfinity;

        public double High { get; init; } = double.PositiveInfinity;

        internal Func<double> Measure { get; init; } = () => double.NaN;
    }

    /// <summary>[datasheet] A check's outcome: the model's value and whether it falls in the datasheet's range.</summary>
    public sealed class DatasheetResult
    {
        public DatasheetResult(DatasheetCheck check, double value)
        {
            Check = check;
            Value = value;
        }

        public DatasheetCheck Check { get; }

        public double Value { get; }

        /// <summary>Within the range (a part-per-million allowance: a model set to a datasheet's limit sits on it).</summary>
        public bool Pass => double.IsFinite(Value) && Value >= Check.Low - 1e-6 * Math.Abs(Check.Low) && Value <= Check.High + 1e-6 * Math.Abs(Check.High);
    }

    /// <summary>
    /// [datasheet] The library's parts compared with their datasheets (the library's "veri sayfasıyla karşılaştırma"
    /// badge): for each part number, figures from its maker's datasheet and the model's own value, measured by simulating
    /// the part as the library builds it. The same list is a test (every check must pass), so the badge and the tests
    /// never disagree. Results are kept once computed.
    /// </summary>
    public static class DatasheetChecks
    {
        private static readonly ConcurrentDictionary<string, IReadOnlyList<DatasheetResult>> Cache = new ConcurrentDictionary<string, IReadOnlyList<DatasheetResult>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The part numbers that have checks.</summary>
        public static IEnumerable<string> Parts => new[] { "lm7805", "irfz44n", "irf540n", "2n7000", "1n4148", "1n4007", "1n5819", "1n5822", "bc547b", "2n2222a", "tip120" };

        /// <summary>The checks of a part number (its preset id), empty when it has none.</summary>
        public static IReadOnlyList<DatasheetCheck> For(string presetId, PartLibrary? library = null)
        {
            var lib = library ?? PartLibrary.Default;
            switch (presetId.ToLowerInvariant())
            {
                case "lm7805":
                {
                    const string ti = "TI LM340/LM7805 (SNOSBT0L), LM7805C elektriksel özellikler";
                    return new[]
                    {
                        new DatasheetCheck { Quantity = "Çıkış gerilimi, 500 mA, giriş 10 V", Source = ti, Unit = "V", Typical = 5.0, Low = 4.75, High = 5.25, Measure = () => RegulatorOut(lib, presetId, 10, 0.5) },
                        new DatasheetCheck { Quantity = "Yük regülasyonu, 5 mA → 1,5 A", Source = ti, Unit = "mV", Scale = 1e-3, Typical = 0.010, Low = 0, High = 0.025, Measure = () => RegulatorOut(lib, presetId, 10, 0.005) - RegulatorOut(lib, presetId, 10, 1.5) },
                        new DatasheetCheck { Quantity = "Çıkış direnci, 1 kHz (tipik ±%25)", Source = ti, Unit = "mΩ", Scale = 1e-3, Typical = 0.008, Low = 0.006, High = 0.010, Measure = () => RegulatorImpedance(lib, presetId, 1e3) },
                        new DatasheetCheck { Quantity = "Düşme gerilimi, 1 A (tipik ±%25)", Source = ti, Unit = "V", Typical = 2.0, Low = 1.5, High = 2.5, Measure = () => RegulatorDropout(lib, presetId, 1.0) },
                    };
                }

                case "irfz44n":
                    return Fet(lib, presetId, "Infineon IRFZ44N", rdsMax: 0.0175, vgs: 10, id: 25, vthMin: 2, vthMax: 4, vthId: 250e-6);
                case "irf540n":
                    return Fet(lib, presetId, "Infineon IRF540N", rdsMax: 0.044, vgs: 10, id: 16, vthMin: 2, vthMax: 4, vthId: 250e-6);
                case "2n7000":
                    return Fet(lib, presetId, "onsemi 2N7000", rdsMax: 5.0, vgs: 10, id: 0.5, vthMin: 0.8, vthMax: 3.0, vthId: 1e-3);

                // diodes: the forward voltage at the datasheet's test current (its maximum, 25 °C)
                case "1n4148":
                    return new[] { Forward(lib, presetId, "onsemi 1N4148", 10e-3, 1.0) };
                case "1n4007":
                    return new[] { Forward(lib, presetId, "onsemi 1N4001–1N4007", 1.0, 1.1) };
                case "1n5819":
                    return new[] { Forward(lib, presetId, "onsemi 1N5817–1N5819", 1.0, 0.6) };
                case "1n5822":
                    return new[] { Forward(lib, presetId, "onsemi 1N5820–1N5822", 3.0, 0.525) };

                // transistors: the DC current gain at the datasheet's collector current and voltage
                case "bc547b":
                    return new[] { Gain(lib, presetId, "onsemi BC546–BC548 (B grubu)", 2e-3, 5, 200, 450) };
                case "2n2222a":
                    return new[]
                    {
                        Gain(lib, presetId, "onsemi P2N2222A", 10e-3, 10, 75, double.PositiveInfinity),
                        Gain(lib, presetId, "onsemi P2N2222A", 150e-3, 10, 100, 300),
                    };
                case "tip120":
                    return new[]
                    {
                        Gain(lib, presetId, "onsemi TIP120", 0.5, 3, 1000, double.PositiveInfinity),
                        Gain(lib, presetId, "onsemi TIP120", 3.0, 3, 1000, double.PositiveInfinity),
                    };
                default:
                    return Array.Empty<DatasheetCheck>();
            }
        }

        /// <summary>Runs (once) and returns a part number's checks.</summary>
        public static IReadOnlyList<DatasheetResult> Results(string presetId, PartLibrary? library = null) =>
            Cache.GetOrAdd(presetId, id => For(id, library).Select(c => new DatasheetResult(c, SafeMeasure(c))).ToArray());

        private static double SafeMeasure(DatasheetCheck c)
        {
            try
            {
                return c.Measure();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is KeyNotFoundException)
            {
                return double.NaN;
            }
        }

        // ---------------------------------------------------------------- diodes and transistors

        private static DatasheetCheck Forward(PartLibrary lib, string presetId, string source, double current, double vfMax)
        {
            string at = current >= 1 ? $"{current:0.#} A" : $"{current * 1000:0.#} mA";
            return new DatasheetCheck { Quantity = $"İleri gerilim Vf, If {at} (en çok)", Source = source, Unit = "V", Low = 0, High = vfMax, Measure = () => DiodeForward(lib, presetId, current) };
        }

        private static double DiodeForward(PartLibrary lib, string presetId, double current)
        {
            var c = new AnalogCircuit();
            int a = c.Node("a");
            c.Add(new CurrentSource("IF", a, 0, current));   // pushes the forward current into the anode
            BuildPart(c, lib, presetId, new Dictionary<string, int> { ["A"] = a, ["K"] = 0 }, nonlinearHeat: false);
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            return sim.NodeVoltage("a");
        }

        private static DatasheetCheck Gain(PartLibrary lib, string presetId, string source, double ic, double vce, double min, double max)
        {
            string at = ic >= 1 ? $"{ic:0.#} A" : $"{ic * 1000:0.#} mA";
            return new DatasheetCheck
            {
                Quantity = $"Akım kazancı hFE, Ic {at}, Vce {vce:0.#} V" + (double.IsPositiveInfinity(max) ? " (en az)" : string.Empty), Source = source, Unit = string.Empty,
                Low = min, High = max, Measure = () => BjtGain(lib, presetId, ic, vce),
            };
        }

        /// <summary>hFE = I_C / I_B with the collector held at <paramref name="vce"/> and the base driven to the collector current.</summary>
        private static double BjtGain(PartLibrary lib, string presetId, double ic, double vce)
        {
            (double Ic, double Ib) At(double vbe)
            {
                var c = new AnalogCircuit();
                int b = c.Node("b"), col = c.Node("c");
                var vb = c.Add(new VoltageSource("VB", b, 0, vbe));
                var vc = c.Add(new VoltageSource("VC", col, 0, vce));
                BuildPart(c, lib, presetId, new Dictionary<string, int> { ["B"] = b, ["C"] = col, ["E"] = 0 }, nonlinearHeat: false);
                var sim = new AnalogSimulator(c);
                sim.Initialize(InitialState.OperatingPoint);
                return (Math.Abs(vc.Current), Math.Abs(vb.Current));   // what the sources deliver into the collector and the base
            }

            double lo = 0.3, hi = 2.5;
            for (int k = 0; k < 60; k++)
            {
                double mid = (lo + hi) / 2;
                if (At(mid).Ic < ic) lo = mid;
                else hi = mid;
            }

            var (icAt, ib) = At((lo + hi) / 2);
            return ib > 0 ? icAt / ib : double.NaN;
        }

        // ---------------------------------------------------------------- MOSFETs

        private static DatasheetCheck[] Fet(PartLibrary lib, string presetId, string source, double rdsMax, double vgs, double id, double vthMin, double vthMax, double vthId)
        {
            string at = id >= 1 ? $"{id:0.#} A" : $"{id * 1000:0.#} mA";
            string thAt = vthId >= 1e-3 ? $"{vthId * 1000:0.#} mA" : $"{vthId * 1e6:0} µA";
            return new[]
            {
                new DatasheetCheck { Quantity = $"Rds(on), Vgs {vgs:0.#} V, Id {at} (en çok)", Source = source, Unit = rdsMax < 1 ? "mΩ" : "Ω", Scale = rdsMax < 1 ? 1e-3 : 1, High = rdsMax, Low = 0, Measure = () => FetOnResistance(lib, presetId, vgs, id) },
                new DatasheetCheck { Quantity = $"Eşik gerilimi Vgs(th), Vds = Vgs, Id {thAt}", Source = source, Unit = "V", Low = vthMin, High = vthMax, Measure = () => FetThreshold(lib, presetId, vthId) },
            };
        }

        /// <summary>The on-resistance at the datasheet's gate drive and drain current: the drain held at that current.</summary>
        private static double FetOnResistance(PartLibrary lib, string presetId, double vgs, double id)
        {
            var c = new AnalogCircuit();
            int g = c.Node("g"), d = c.Node("d");
            c.Add(new VoltageSource("VG", g, 0, vgs));
            c.Add(new CurrentSource("ID", d, 0, id));   // pushes the drain current into the drain
            BuildPart(c, lib, presetId, new Dictionary<string, int> { ["G"] = g, ["D"] = d, ["S"] = 0 }, nonlinearHeat: false);
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            return sim.NodeVoltage("d") / id;
        }

        /// <summary>The gate voltage at which the drain, tied to the gate, carries the datasheet's threshold current.</summary>
        private static double FetThreshold(PartLibrary lib, string presetId, double id)
        {
            var c = new AnalogCircuit();
            int n = c.Node("n");
            c.Add(new CurrentSource("ID", n, 0, id));
            BuildPart(c, lib, presetId, new Dictionary<string, int> { ["G"] = n, ["D"] = n, ["S"] = 0 }, nonlinearHeat: false);
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            return sim.NodeVoltage("n");
        }

        // ---------------------------------------------------------------- regulators

        private static double RegulatorOut(PartLibrary lib, string presetId, double vin, double load)
        {
            var (sim, _) = Regulator(lib, presetId, vin, load);
            return sim.NodeVoltage("out");
        }

        /// <summary>|Z| at <paramref name="frequency"/>: a 50 mA sine on a 500 mA load, the output's swing over the current's.</summary>
        private static double RegulatorImpedance(PartLibrary lib, string presetId, double frequency)
        {
            const double ia = 0.05;
            var (sim, _) = Regulator(lib, presetId, 10, new SineWaveform(0.5, ia, frequency), 1 / (frequency * 200));
            double period = 1 / frequency, lo = double.MaxValue, hi = double.MinValue;
            while (sim.Time < 5 * period)
            {
                sim.Step(period / 200);
                if (sim.Time < 3 * period) continue;
                double v = sim.NodeVoltage("out");
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }

            return (hi - lo) / (2 * ia);
        }

        /// <summary>The input-to-output difference at which the output has fallen 100 mV below its value at 10 V (at a load).</summary>
        private static double RegulatorDropout(PartLibrary lib, string presetId, double load)
        {
            double nominal = RegulatorOut(lib, presetId, 10, load);
            double lo = 0, hi = 10;
            for (int k = 0; k < 40; k++)
            {
                double vin = (lo + hi) / 2;
                if (RegulatorOut(lib, presetId, vin, load) < nominal - 0.1) lo = vin;
                else hi = vin;
            }

            double v = (lo + hi) / 2;
            return v - RegulatorOut(lib, presetId, v, load);
        }

        private static (AnalogSimulator Sim, AnalogCircuit Circuit) Regulator(PartLibrary lib, string presetId, double vin, Waveform load, double maxStep = 1e-3)
        {
            var c = new AnalogCircuit();
            int i = c.Node("in"), o = c.Node("out");
            c.Add(new VoltageSource("VIN", i, 0, vin));
            c.Add(new CurrentSource("IL", 0, o, load));   // draws the load from the output
            BuildPart(c, lib, presetId, new Dictionary<string, int> { ["IN"] = i, ["OUT"] = o, ["GND"] = 0 }, nonlinearHeat: false);
            var sim = new AnalogSimulator(c, new AnalogOptions { MaxStep = maxStep, RelTol = 1e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            return (sim, c);
        }

        /// <summary>The part number built into <paramref name="circuit"/> as the library builds it, its pins on the given nodes (cold: no self-heating).</summary>
        private static void BuildPart(AnalogCircuit circuit, PartLibrary lib, string presetId, IReadOnlyDictionary<string, int> pins, bool nonlinearHeat)
        {
            var preset = lib.FindPreset(presetId) ?? throw new KeyNotFoundException(presetId);
            var def = lib.Get(preset.PartId);
            var parameters = lib.ResolveParameters(def, preset, null);
            if (!nonlinearHeat) parameters["rth"] = "0";   // a datasheet's figures are at 25 °C junction (pulsed tests)
            var ctx = new PartBuildContext("U1", circuit, name => pins.TryGetValue(name, out int n) ? n : circuit.InternalNode("U1", "nc-" + name), parameters, new List<AnalogDevice>());
            (def.Build ?? throw new InvalidOperationException(presetId + ": elektriksel modeli yok")).Invoke(ctx);
        }
    }
}
