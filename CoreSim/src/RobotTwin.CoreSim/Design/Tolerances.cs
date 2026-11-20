using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Instruments;
using static RobotTwin.CoreSim.Instruments.InvariantText;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>How component values are chosen when a schematic is compiled.</summary>
    public enum ToleranceMode
    {
        /// <summary>Every part has exactly its nominal value.</summary>
        Nominal,

        /// <summary>Each part gets one fixed, reproducible "as manufactured" value drawn from its tolerance band.</summary>
        Realistic,

        /// <summary>Like <see cref="Realistic"/> for the interactive run; the Monte Carlo analysis draws a new set per run.</summary>
        MonteCarlo
    }

    /// <summary>Project setting: tolerance mode, seed and Monte Carlo run count (saved with the schematic).</summary>
    public sealed class ToleranceSettings
    {
        public ToleranceMode Mode { get; set; } = ToleranceMode.Nominal;

        /// <summary>Seed of the per-part draws; the same seed always builds the same "board".</summary>
        public int Seed { get; set; } = 1;

        public int Runs { get; set; } = 50;

        public bool Active => Mode != ToleranceMode.Nominal;
    }

    /// <summary>A tolerance band "5" (±5 %), "-20/+80", "±10%".</summary>
    public readonly struct ToleranceBand
    {
        public ToleranceBand(double lower, double upper)
        {
            Lower = lower;
            Upper = upper;
        }

        /// <summary>Largest negative deviation as a positive fraction (0.2 = −20 %).</summary>
        public double Lower { get; }

        /// <summary>Largest positive deviation as a fraction (0.8 = +80 %).</summary>
        public double Upper { get; }

        public bool IsZero => Lower == 0 && Upper == 0;

        public static ToleranceBand Parse(string? text, double fallbackPercent)
        {
            if (string.IsNullOrWhiteSpace(text)) return new ToleranceBand(fallbackPercent / 100, fallbackPercent / 100);
            string s = text!.Trim().Replace("%", string.Empty).Replace("±", string.Empty).Replace(" ", string.Empty).Replace(',', '.');
            int slash = s.IndexOf('/');
            if (slash > 0)
            {
                if (double.TryParse(s.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out double a) &&
                    double.TryParse(s.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out double b))
                {
                    return new ToleranceBand(Math.Abs(a) / 100, Math.Abs(b) / 100);
                }
            }

            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
            {
                return new ToleranceBand(Math.Abs(p) / 100, Math.Abs(p) / 100);
            }

            return new ToleranceBand(fallbackPercent / 100, fallbackPercent / 100);
        }

        /// <summary>Maps a sample g ∈ [−1, 1] onto the band.</summary>
        public double Deviation(double g) => g >= 0 ? g * Upper : g * Lower;

        public override string ToString() =>
            Lower == Upper
                ? "±" + (Upper * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %"
                : "−" + (Lower * 100).ToString("0.##", CultureInfo.InvariantCulture) + "/+" + (Upper * 100).ToString("0.##", CultureInfo.InvariantCulture) + " %";
    }

    /// <summary>The actual value one parameter of one part received.</summary>
    public sealed class ToleranceDraw
    {
        public ToleranceDraw(string key, string label, string unit, double nominal, double actual, string note)
        {
            Key = key;
            Label = label;
            Unit = unit;
            Nominal = nominal;
            Actual = actual;
            Note = note;
        }

        public string Key { get; }
        public string Label { get; }
        public string Unit { get; }
        public double Nominal { get; }
        public double Actual { get; }
        public string Note { get; }

        public double DeviationPercent => Nominal != 0 ? (Actual / Nominal - 1.0) * 100.0 : 0.0;

        public override string ToString() =>
            I($"{Label}: {SiValue.Format(Nominal, Unit, 4)} → {SiValue.Format(Actual, Unit, 4)} ({DeviationPercent:+0.00;-0.00} %)");
    }

    /// <summary>
    /// Draws manufacturing variation for the catalog parts: resistor/capacitor/inductor value
    /// tolerances (asymmetric for electrolytics), zener voltage, LED forward voltage, transistor
    /// hFE and MOSFET threshold spread, regulator output and op-amp offset, plus the
    /// temperature characteristic of ceramic dielectrics (C0G, X7R, X5R, Y5V) and electrolytics
    /// at the ambient temperature. Draws are deterministic: seed × run × part × parameter.
    /// </summary>
    public static class ToleranceModel
    {
        /// <summary>
        /// Replaces nominal values in <paramref name="parameters"/> by drawn values; returns what was drawn.
        /// Nominal mode (or null settings) returns nothing and leaves the parameters untouched.
        /// </summary>
        public static List<ToleranceDraw> Apply(PartDefinition def, Dictionary<string, string> parameters, string refDes,
            ToleranceSettings? settings, int run, double ambientC)
        {
            var draws = new List<ToleranceDraw>();
            if (settings == null || !settings.Active) return draws;

            void Vary(string key, string label, string unit, ToleranceBand band, double tempFactor = 1.0, string note = "")
            {
                if (!parameters.TryGetValue(key, out var raw) || !SiValue.TryParse(raw, out double nominal)) return;
                double g = Sample(settings.Seed, run, refDes, key);
                double actual = nominal * (1.0 + band.Deviation(g)) * tempFactor;
                parameters[key] = actual.ToString("R", CultureInfo.InvariantCulture);
                draws.Add(new ToleranceDraw(key, label, unit, nominal, actual, note.Length > 0 ? note : band.ToString()));
            }

            ToleranceBand Band(string key, double fallback) =>
                ToleranceBand.Parse(parameters.TryGetValue(key, out var t) ? t : null, fallback);

            switch (def.Id)
            {
                case "resistor":
                    Vary("resistance", "Direnç", "Ω", Band("tolerance", 5));
                    break;
                case "potentiometer":
                    Vary("resistance", "Toplam direnç", "Ω", Band("tolerance", 20));
                    break;
                case "ntc":
                    Vary("resistance", "R25", "Ω", Band("tolerance", 5));
                    break;
                case "ldr":
                    Vary("resistance", "R @10 lux", "Ω", Band("tolerance", 30));
                    break;
                case "capacitor":
                {
                    string dielectric = parameters.TryGetValue("dielectric", out var d) ? d : "X7R";
                    double tf = CeramicTemperatureFactor(dielectric, ambientC);
                    var band = Band("tolerance", 10);
                    Vary("capacitance", "Kapasite", "F", band, tf, I($"{band} · {dielectric} @ {ambientC:0} °C ({(tf - 1) * 100:+0.0;-0.0} %)"));
                    break;
                }

                case "electrolytic":
                {
                    double tf = ElectrolyticTemperatureFactor(ambientC);
                    var band = Band("tolerance", 20);
                    Vary("capacitance", "Kapasite", "F", band, tf, I($"{band} · Al-elko @ {ambientC:0} °C ({(tf - 1) * 100:+0.0;-0.0} %)"));
                    break;
                }

                case "inductor":
                    Vary("inductance", "Endüktans", "H", Band("tolerance", 20));
                    break;
                case "zener":
                    Vary("voltage", "Zener gerilimi", "V", Band("tolerance", 5));
                    break;
                case "led":
                    Vary("vf", "İleri gerilim", "V", Band("vftol", 5));
                    break;
                case "npn":
                case "pnp":
                    Vary("bf", "hFE", "", Band("hfespread", 40));
                    break;
                case "darlington":
                    Vary("bf1", "β1", "", Band("hfespread", 40));
                    Vary("bf2", "β2", "", Band("hfespread", 40));
                    break;
                case "nmos":
                case "pmos":
                    Vary("vth", "Eşik gerilimi", "V", Band("vthspread", 25));
                    break;
                case "regulator":
                    Vary("vout", "Çıkış gerilimi", "V", Band("tolerance", 2));
                    break;
                case "opamp":
                {
                    double vosMax = parameters.TryGetValue("vosmax", out var vm) ? SiValue.Parse(vm, 3e-3) : 3e-3;
                    double nominal = parameters.TryGetValue("vos", out var vo) ? SiValue.Parse(vo, 0) : 0;
                    double actual = nominal + vosMax * Sample(settings.Seed, run, refDes, "vos");
                    parameters["vos"] = actual.ToString("R", CultureInfo.InvariantCulture);
                    draws.Add(new ToleranceDraw("vos", "Giriş ofseti", "V", nominal, actual, "en çok ±" + SiValue.Format(vosMax, "V")));
                    break;
                }

                case "dc-motor":
                    Vary("rpm", "Yüksüz devir", "rpm", Band("tolerance", 10));
                    Vary("istall", "Kilitlenme akımı", "A", Band("tolerance", 10));
                    Vary("i0", "Yüksüz akım", "A", Band("tolerance", 15));
                    break;
                case "servo":
                    Vary("istall", "Kilitlenme akımı", "A", Band("tolerance", 10));
                    Vary("imove", "Hareket akımı", "A", Band("tolerance", 10));
                    Vary("speed", "Hız", "s", Band("tolerance", 8));
                    break;

                default: // [libs] user-made parts: "tol.<key>" parameters give per-parameter bands
                    foreach (var tp in def.Parameters.Where(x => x.Key.StartsWith("tol.", StringComparison.OrdinalIgnoreCase)).ToList())
                    {
                        string key = tp.Key.Substring(4);
                        var vp = def.Parameters.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
                        Vary(key, vp?.Label ?? key, vp?.Unit ?? string.Empty, Band(tp.Key, 0));
                    }

                    break;
            }

            return draws;
        }

        /// <summary>
        /// Clone ("yan sanayi") parts: a different winding and controller chip than the datasheet part.
        /// Bench measurements of SG90 / TT-motor clones differ from the datasheet by up to 30–40 % in
        /// current and ~20 % in speed. The deviation is fixed per physical part (seeded by its
        /// designator), so it applies in every run — not only in a tolerance analysis.
        /// </summary>
        public static List<ToleranceDraw> ApplyVariant(PartDefinition def, Dictionary<string, string> parameters, string refDes)
        {
            var draws = new List<ToleranceDraw>();
            if (!parameters.TryGetValue("variant", out var v) || !IsClone(v)) return draws;
            void Vary(string key, string label, string unit, double percent)
            {
                if (!parameters.TryGetValue(key, out var raw) || !SiValue.TryParse(raw, out double nominal)) return;
                double g = Sample(0x6C6F6E, 0, refDes, "clone." + key);
                double actual = nominal * (1.0 + g * percent / 100.0);
                parameters[key] = actual.ToString("R", CultureInfo.InvariantCulture);
                draws.Add(new ToleranceDraw(key, label, unit, nominal, actual, I($"klon ±{percent:0} %")));
            }

            switch (def.Id)
            {
                case "servo":
                    Vary("istall", "Kilitlenme akımı", "A", 35);
                    Vary("imove", "Hareket akımı", "A", 40);
                    Vary("speed", "Hız", "s", 20);
                    break;
                case "dc-motor":
                    Vary("istall", "Kilitlenme akımı", "A", 35);
                    Vary("i0", "Yüksüz akım", "A", 40);
                    Vary("rpm", "Yüksüz devir", "rpm", 20);
                    break;
            }

            return draws;
        }

        public static bool IsClone(string? variant) =>
            variant != null && (variant.Equals("klon", StringComparison.OrdinalIgnoreCase) || variant.Equals("clone", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// How far a part's real current may sit from the model (fraction) and why — used to show
        /// readings with their honest uncertainty instead of false precision.
        /// </summary>
        public static (double Fraction, string Reason) CurrentSpread(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            bool clone = parameters.TryGetValue("variant", out var v) && IsClone(v);
            string model = parameters.TryGetValue("model", out var m) ? m : def.Name;
            switch (def.Id)
            {
                case "servo":
                case "dc-motor":
                    return clone ? (0.40, model + " klon: sargı ve sürücü farkı") : (0.15, model + ": datasheet tipik değeri");
                case "resistor":
                    return (ToleranceBand.Parse(parameters.TryGetValue("tolerance", out var t) ? t : null, 5).Upper, "direnç toleransı");
                case "led":
                    return (0.10, "LED ileri gerilim yayılımı");
                default:
                    return def.Symbol == SymbolKind.Board || def.Symbol == SymbolKind.Module ? (0.20, model + ": besleme akımı tipik değer") : (0, string.Empty);
            }
        }

        /// <summary>The draws a part would get, without compiling (for the property panel).</summary>
        public static List<ToleranceDraw> Preview(Schematic schematic, PartLibrary library, SchematicPart part,
            ToleranceSettings? settings, int run = 0, double ambientC = 25.0)
        {
            var def = library.Find(part.PartId);
            if (def == null) return new List<ToleranceDraw>();
            var parameters = schematic.ResolveParameters(library, part);
            return Apply(def, parameters, part.RefDes, settings, run, ambientC);
        }

        /// <summary>Truncated normal sample in [−1, 1] (σ = 0.5, so ±1 is the 2σ edge of the band).</summary>
        public static double Sample(int seed, int run, string refDes, string key)
        {
            ulong h = InstrumentRandom.Hash(refDes.ToUpperInvariant() + "\u0001" + key.ToLowerInvariant(),
                InstrumentRandom.Mix((ulong)(uint)seed * 0x100000001B3UL + (ulong)(uint)run));
            var rng = new InstrumentRandom(h);
            double g = 0.5 * rng.NextGaussian();
            return Math.Max(-1.0, Math.Min(1.0, g));
        }

        /// <summary>Capacitance relative to 25 °C for ceramic dielectrics (EIA class I/II characteristics).</summary>
        public static double CeramicTemperatureFactor(string dielectric, double tempC)
        {
            double d = tempC - 25.0;
            switch ((dielectric ?? "X7R").Trim().ToUpperInvariant())
            {
                case "C0G":
                case "NP0":
                    return 1.0 + 0e-6 * d;
                case "X5R":
                    return 1.0 - 0.13 * Math.Pow(Math.Max(-1.2, Math.Min(1.2, d / 70.0)), 2) + 0.01 * d / 70.0;
                case "Y5V":
                    return d >= 0 ? Math.Max(0.15, 1.0 - 0.82 * Math.Pow(d / 60.0, 2)) : Math.Max(0.3, 1.0 - 0.56 * Math.Pow(d / 55.0, 2));
                case "Z5U":
                    return d >= 0 ? Math.Max(0.3, 1.0 - 0.56 * Math.Pow(d / 60.0, 2)) : Math.Max(0.5, 1.0 - 0.3 * Math.Pow(d / 15.0, 2));
                default:
                    // X7R: within ±15 % from −55 to +125 °C, down at both ends.
                    return 1.0 - 0.12 * Math.Pow(Math.Max(-1.0, Math.Min(1.0, d / 100.0)), 2) + 0.01 * d / 100.0;
            }
        }

        /// <summary>Aluminium electrolytic capacitance relative to 25 °C (falls in the cold, rises a little when hot).</summary>
        public static double ElectrolyticTemperatureFactor(double tempC)
        {
            double d = tempC - 25.0;
            return d < 0 ? Math.Max(0.6, 1.0 + 0.004 * d) : Math.Min(1.12, 1.0 + 0.001 * d);
        }
    }

    /// <summary>Options for <see cref="CircuitCompiler.Compile(Schematic, PartLibrary, CompileOptions)"/>.</summary>
    public sealed class CompileOptions
    {
        public ToleranceSettings? Tolerances { get; set; }

        /// <summary>Monte Carlo run index (0 = the interactive "realistic" board).</summary>
        public int ToleranceRun { get; set; }

        public double AmbientC { get; set; } = 25.0;
    }
}
