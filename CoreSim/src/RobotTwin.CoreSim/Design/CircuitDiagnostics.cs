using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Instruments;
using static RobotTwin.CoreSim.Instruments.InvariantText;

namespace RobotTwin.CoreSim.Design
{
    public enum DiagnosisSeverity
    {
        Info,
        Warning,
        Error
    }

    /// <summary>One finding of the fault diagnosis: what is wrong, why it matters and how to fix it.</summary>
    public sealed class Diagnosis
    {
        public Diagnosis(DiagnosisSeverity severity, string code, string title, string detail, string suggestion, params string[] parts)
        {
            Severity = severity;
            Code = code;
            Title = title;
            Detail = detail;
            Suggestion = suggestion;
            Parts = parts.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public DiagnosisSeverity Severity { get; }

        /// <summary>Stable identifier of the rule ("LED_NO_RESISTOR", "OVER_RATING"...).</summary>
        public string Code { get; }

        public string Title { get; }
        public string Detail { get; }
        public string Suggestion { get; }

        /// <summary>Parts to highlight (first one is the main suspect).</summary>
        public List<string> Parts { get; }

        public string? Net { get; set; }

        /// <summary>Remaining margin to the rating in percent (negative = beyond the rating).</summary>
        public double? MarginPercent { get; set; }

        public override string ToString() => $"[{Severity}] {Code} {string.Join(",", Parts)}: {Title}";
    }

    /// <summary>A stress quantity of a device against its rating.</summary>
    public readonly struct DeviceStress
    {
        public DeviceStress(string key, string quantity, string unit, double value, double rating, double ratio)
        {
            Key = key;
            Quantity = quantity;
            Unit = unit;
            Value = value;
            Rating = rating;
            Ratio = ratio;
        }

        public string Key { get; }
        public string Quantity { get; }
        public string Unit { get; }
        public double Value { get; }
        public double Rating { get; }

        /// <summary>Stress / rating (1 = at the rating).</summary>
        public double Ratio { get; }

        public double MarginPercent => (1.0 - Ratio) * 100.0;
    }

    /// <summary>Peak stresses seen during a run (smoothed over ~20 ms so inrush spikes do not count as overloads).</summary>
    public sealed class StressTracker
    {
        public sealed class Peak
        {
            public string DeviceId { get; set; } = string.Empty;
            public DeviceStress Stress { get; set; }
            public double Time { get; set; }
        }

        private readonly Dictionary<string, Peak> _peaks = new Dictionary<string, Peak>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _smooth = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private double _lastTime = double.NaN;

        public double SmoothingTime { get; set; } = 20e-3;

        public IReadOnlyDictionary<string, Peak> Peaks => _peaks;

        public void Clear()
        {
            _peaks.Clear();
            _smooth.Clear();
            _lastTime = double.NaN;
        }

        public void Sample(AnalogCircuit circuit, double time, double ambientC = 25.0)
        {
            double dt = double.IsNaN(_lastTime) ? 0.0 : Math.Max(0.0, time - _lastTime);
            _lastTime = time;
            double a = dt <= 0 ? 1.0 : 1.0 - Math.Exp(-dt / SmoothingTime);
            foreach (var device in circuit.Devices)
            {
                if (device.IsFailed) continue;
                foreach (var s in CircuitDiagnostics.Stresses(device, ambientC))
                {
                    string key = device.Id + "|" + s.Key;
                    double r = s.Ratio;
                    if (s.Key != "T")
                    {
                        r = _smooth.TryGetValue(key, out double prev) ? prev + (r - prev) * a : r * a;
                        _smooth[key] = r;
                    }

                    if (!_peaks.TryGetValue(key, out var peak) || r > peak.Stress.Ratio)
                    {
                        double value = s.Ratio > 0 ? s.Value * r / s.Ratio : s.Value;
                        _peaks[key] = new Peak
                        {
                            DeviceId = device.Id,
                            Stress = new DeviceStress(s.Key, s.Quantity, s.Unit, value, s.Rating, r),
                            Time = time
                        };
                    }
                }
            }
        }

        public IEnumerable<Peak> PeaksOf(string deviceId) => _peaks.Values.Where(p => string.Equals(p.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Fault diagnosis ("Arıza tespiti"): structural checks on the schematic (shorted supplies,
    /// LEDs without a series resistor, reversed polarity, floating CMOS / MOSFET / MCU inputs,
    /// unconnected pins, missing ground, motors on pins or on undersized drivers, missing flyback
    /// diodes, regulator headroom) plus, while simulating, every part against its ratings with the
    /// remaining margin, regulator dropout and battery overload, and an explanation of why a part burned.
    /// </summary>
    public static class CircuitDiagnostics
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ ratings

        /// <summary>Stress quantities of a device against its ratings.</summary>
        public static IEnumerable<DeviceStress> Stresses(AnalogDevice device, double ambientC = 25.0)
        {
            DeviceStress S(string key, string quantity, string unit, double value, double rating) =>
                new DeviceStress(key, quantity, unit, value, rating, rating > 0 ? Math.Abs(value) / rating : 0.0);

            DeviceStress Temp(double t, double max) =>
                new DeviceStress("T", "Sıcaklık", "°C", t, max, max > ambientC ? Math.Max(0.0, (t - ambientC) / (max - ambientC)) : 0.0);

            switch (device)
            {
                case Lamp lamp:
                    yield return S("V", "Gerilim", "V", lamp.Voltage, lamp.RatedVoltage * 1.1);
                    break;
                case Buzzer _:
                    break;
                case Resistor r when r.PowerRating > 0 && !(r is Thermistor) && !(r is Photoresistor):
                    yield return S("P", "Güç", "W", r.Power, r.PowerRating);
                    break;
                case Capacitor c:
                    if (c.VoltageRating > 0) yield return S("V", "Gerilim", "V", c.Voltage, c.VoltageRating);
                    if (c.Polarized && c.Voltage < 0) yield return S("VR", "Ters gerilim", "V", -c.Voltage, 1.5);
                    break;
                case Diode d:
                    if (d.MaxCurrent > 0 && d.Current > 0) yield return S("I", "İleri akım", "A", d.Current, d.MaxCurrent);
                    if (d.Thermal != null) yield return Temp(d.Thermal.TemperatureC, d.MaxJunctionTempC);
                    if (!(d is Led) && d.BreakdownVoltage > 0 && d.Voltage < 0 && d.BreakdownCurrent < 1e-3)
                    {
                        yield return S("VR", "Ters gerilim", "V", -d.Voltage, d.BreakdownVoltage);
                    }

                    break;
                case Bjt q:
                    if (q.MaxCollectorCurrent > 0) yield return S("IC", "Kollektör akımı", "A", q.CollectorCurrent, q.MaxCollectorCurrent);
                    if (q.MaxCollectorEmitterVoltage > 0) yield return S("VCE", "Vce", "V", (int)q.Polarity * q.Vce > 0 ? q.Vce : 0.0, q.MaxCollectorEmitterVoltage);
                    if (q.Thermal != null) yield return Temp(q.Thermal.TemperatureC, q.MaxJunctionTempC);
                    break;
                case Mosfet m:
                    if (m.MaxDrainSourceVoltage > 0) yield return S("VDS", "Vds", "V", (int)m.Polarity * m.Vds > 0 ? m.Vds : 0.0, m.MaxDrainSourceVoltage);
                    if (m.MaxGateSourceVoltage > 0) yield return S("VGS", "Vgs", "V", m.Vgs, m.MaxGateSourceVoltage);
                    if (m.MaxDrainCurrent > 0) yield return S("ID", "Drain akımı", "A", m.DrainCurrent, m.MaxDrainCurrent);
                    if (m.Thermal != null) yield return Temp(m.Thermal.TemperatureC, m.MaxJunctionTempC);
                    break;
                case LinearRegulator reg:
                    if (reg.MaxInputVoltage > 0) yield return S("VIN", "Giriş gerilimi", "V", reg.Vin, reg.MaxInputVoltage);
                    if (reg.CurrentLimit > 0) yield return S("I", "Çıkış akımı", "A", reg.Current, reg.CurrentLimit);
                    if (reg.Thermal != null) yield return Temp(reg.Thermal.TemperatureC, reg.ShutdownTempC);
                    break;
                case Fuse f:
                    yield return S("I", "Akım", "A", f.Current, f.RatedCurrent);
                    break;
                case DigitalPin p:
                    yield return S("I", "Pin akımı", "A", p.Current, p.AbsoluteMaxCurrent);
                    break;
                case DcMotor motor:
                    yield return Temp(motor.Thermal.TemperatureC, motor.MaxWindingTempC);
                    break;
                case AveragedPwmDriver drv:
                    yield return S("I", "Çıkış akımı", "A", drv.OutputCurrent, drv.CurrentRating);
                    yield return Temp(drv.Thermal.TemperatureC, 150.0);
                    break;
                case Battery b when b.CapacityAh > 0:
                    yield return S("I", "Deşarj akımı", "A", b.Current, MaxDischargeCurrent(b));
                    yield return Temp(b.Thermal.TemperatureC, b.IsLithium ? 60.0 : 55.0);
                    break;
                case Multimeter dmm:
                    if (dmm.MilliAmpNode >= 0 && !dmm.MilliAmpFuseBlown && !dmm.UsesAmpJack && MultimeterSpec.IsCurrent(dmm.Mode))
                    {
                        yield return S("IMA", "mA girişi", "A", dmm.LastCurrent, MultimeterSpec.MilliAmpFuse);
                    }

                    break;
            }
        }

        /// <summary>Continuous discharge current a pack is rated for (C-rate × capacity).</summary>
        public static double MaxDischargeCurrent(Battery b)
        {
            double c;
            switch (b.Chemistry)
            {
                case BatteryChemistry.LithiumIon: c = 5.0; break;
                case BatteryChemistry.LiFePO4: c = 3.0; break;
                case BatteryChemistry.NiMH: c = 2.0; break;
                case BatteryChemistry.LeadAcid: c = 1.0; break;
                case BatteryChemistry.LithiumCoin: c = 0.05; break;
                case BatteryChemistry.ZincCarbon: c = 0.3; break;
                default: c = 0.5; break;
            }

            return c * b.CapacityAh;
        }

        // ------------------------------------------------------------------ analysis

        private sealed class Model
        {
            public Schematic Sch = null!;
            public PartLibrary Lib = null!;
            public Netlist Netlist = null!;
            public CompiledCircuit? Compiled;
            public AnalogSimulator? Sim;
            public readonly Dictionary<string, string> NetOfPin = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, Net> Nets = new Dictionary<string, Net>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, (SchematicPart Part, PartDefinition Def, Dictionary<string, string> P)> Parts =
                new Dictionary<string, (SchematicPart, PartDefinition, Dictionary<string, string>)>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Nets held at a known positive voltage by a stiff source: net → (volts, description).</summary>
            public readonly Dictionary<string, (double Volts, string Source)> Supply = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);

            /// <summary>Nets driven by a microcontroller pin: net → (volts, "U1.D13").</summary>
            public readonly Dictionary<string, (double Volts, string Source)> McuPins = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);

            public readonly HashSet<string> Negative = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public string? Net(string refDes, string pin) => NetOfPin.TryGetValue(refDes + "." + pin, out var n) ? n : null;

            public bool IsGround(string? net) => net != null && Nets.TryGetValue(net, out var n) && n.IsGround;

            public bool IsLow(string? net) => net != null && (IsGround(net) || Negative.Contains(net));

            public IEnumerable<NetConnection> Conns(string? net) =>
                net != null && Nets.TryGetValue(net, out var n) ? n.Connections : Enumerable.Empty<NetConnection>();

            public PartDefinition? Def(string refDes) => Parts.TryGetValue(refDes, out var p) ? p.Def : null;

            public double Num(string refDes, string key, double fallback) =>
                Parts.TryGetValue(refDes, out var p) && p.P.TryGetValue(key, out var v) ? SiValue.Parse(v, fallback) : fallback;
        }

        public static List<Diagnosis> Analyze(Schematic schematic, PartLibrary library, CompiledCircuit? compiled = null,
            AnalogSimulator? simulator = null, StressTracker? stress = null, double ambientC = 25.0)
        {
            var m = new Model
            {
                Sch = schematic,
                Lib = library,
                Compiled = compiled ?? CircuitCompiler.Compile(schematic, library),
                Sim = simulator
            };
            m.Netlist = m.Compiled!.Netlist;
            foreach (var net in m.Netlist.Nets)
            {
                m.Nets[net.Name] = net;
                foreach (var c in net.Connections) m.NetOfPin[c.RefDes + "." + c.Pin] = net.Name;
            }

            foreach (var part in schematic.Parts)
            {
                var def = library.Find(part.PartId);
                if (def != null) m.Parts[part.RefDes] = (part, def, schematic.ResolveParameters(library, part));
            }

            BuildSupplies(m);

            var list = new List<Diagnosis>();
            CheckFailed(m, list, stress);
            CheckShorts(m, list);
            CheckLeds(m, list);
            CheckPolarity(m, list);
            CheckFloatingInputs(m, list);
            CheckMotorsAndCoils(m, list);
            CheckRegulators(m, list, ambientC);
            CheckOpenPins(m, list);
            CheckGround(m, list);
            if (simulator != null) CheckRatings(m, list, stress, ambientC);

            // One entry per (code, part): the first (most specific) wins.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return list
                .Where(d => seen.Add(d.Code + "|" + string.Join(",", d.Parts) + "|" + d.Title))
                .OrderByDescending(d => d.Severity)
                .ThenBy(d => d.Parts.FirstOrDefault() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void BuildSupplies(Model m)
        {
            foreach (var (refDes, (part, def, p)) in m.Parts.Select(kv => (kv.Key, kv.Value)))
            {
                switch (def.Id)
                {
                    case "dc-source":
                        AddSupply(m, refDes, "+", "-", SiValue.Parse(p.TryGetValue("voltage", out var v) ? v : "5", 5), refDes);
                        break;
                    case "bench-psu":
                        AddSupply(m, refDes, "+", "-", SiValue.Parse(p.TryGetValue("voltage", out var bv) ? bv : "12", 12), refDes);
                        break;
                    case "acdc-psu":
                    {
                        // [psu] The highest voltage it reaches: the DC set point plus the AC peak.
                        string mode = p.TryGetValue("mode", out var md) ? md : "dc";
                        double dc = mode == "ac" ? 0 : SiValue.Parse(p.TryGetValue("voltage", out var dv) ? dv : "12", 12);
                        double peak = mode == "dc" ? 0 : Math.Sqrt(2) * SiValue.Parse(p.TryGetValue("vrms", out var rv) ? rv : "12", 12);
                        AddSupply(m, refDes, "+", "-", dc + peak, refDes);
                        break;
                    }
                    case "battery":
                    {
                        var chem = Enum.TryParse(p.TryGetValue("chemistry", out var c) ? c : "Alkaline", true, out BatteryChemistry ch) ? ch : BatteryChemistry.Alkaline;
                        double cells = Math.Max(1, SiValue.Parse(p.TryGetValue("cells", out var n) ? n : "1", 1));
                        AddSupply(m, refDes, "+", "-", cells * Battery.NominalCellVoltage(chem), refDes);
                        break;
                    }

                    case "regulator":
                    {
                        string? outNet = m.Net(refDes, "OUT");
                        if (outNet != null && !m.IsGround(outNet)) m.Supply[outNet] = (SiValue.Parse(p.TryGetValue("vout", out var vo) ? vo : "5", 5), refDes + " çıkışı");
                        break;
                    }
                }

                if (def.Symbol == SymbolKind.Board)
                {
                    bool esp = def.Id.IndexOf("esp32", StringComparison.OrdinalIgnoreCase) >= 0;
                    foreach (var pin in def.Pins)
                    {
                        string? net = m.Net(refDes, pin.Name);
                        if (net == null || m.IsGround(net)) continue;
                        if (pin.Name == "5V") m.Supply[net] = (5.0, refDes + " 5V");
                        else if (pin.Name == "3V3") m.Supply[net] = (3.3, refDes + " 3V3");
                        else if (IsIoPin(pin.Name)) m.McuPins[net] = (esp ? 3.3 : 5.0, refDes + "." + pin.Name);
                    }
                }
            }

            // Named rails ("5V", "12V", "3V3") that a source drives.
            foreach (var net in m.Netlist.Nets)
            {
                if (net.IsGround || net.IsAutoNamed || m.Supply.ContainsKey(net.Name)) continue;
                if (char.IsDigit(net.Name[0]) && SiValue.TryParse(net.Name, out double volts) && volts > 0 && volts < 60)
                {
                    m.Supply[net.Name] = (volts, net.Name + " hattı");
                }
            }
        }

        private static void AddSupply(Model m, string refDes, string plus, string minus, double volts, string source)
        {
            string? p = m.Net(refDes, plus), n = m.Net(refDes, minus);
            if (n != null) m.Negative.Add(n);
            if (p != null && p != n && !m.IsGround(p)) m.Supply[p] = (volts, source);
        }

        private static bool IsIoPin(string name) =>
            name.Length >= 2 && (name[0] == 'D' || name[0] == 'A') && char.IsDigit(name[1]) ||
            name.StartsWith("IO", StringComparison.OrdinalIgnoreCase) && name.Length > 2 && char.IsDigit(name[2]) ||
            name.StartsWith("GPIO", StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ failures

        private static void CheckFailed(Model m, List<Diagnosis> list, StressTracker? stress)
        {
            if (m.Compiled == null) return;
            foreach (var kv in m.Compiled.DevicesByPart)
            {
                foreach (var device in kv.Value)
                {
                    if (!device.IsFailed) continue;
                    string what = device.Failure == FailureMode.Short ? "kısa devre oldu" : "açık devre oldu (yandı)";
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, "FAILED", $"{kv.Key} hasar gördü: {what}",
                        ExplainFailure(m.Compiled, device, stress), FailureSuggestion(m, kv.Key, device, stress), kv.Key));
                }

                if (kv.Value.FirstOrDefault() is Multimeter dmm && (dmm.MilliAmpFuseBlown || dmm.AmpFuseBlown))
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, "METER_FUSE", $"{kv.Key}: multimetre sigortası atık",
                        $"{(dmm.MilliAmpFuseBlown ? "mA (F 440 mA)" : "A (F 11 A)")} girişinin sigortası attı; bu girişle akım okunamaz.",
                        "Akım ölçerken multimetreyi devreye SERİ bağlayın, gerilim ölçerken kırmızı probu VΩ girişine takın. Parçayı onararak sigortayı değiştirin.", kv.Key));
                }
            }
        }

        /// <summary>"Why did it burn": the failure, the peak stress against the rating, and the likely cause.</summary>
        public static string ExplainFailure(CompiledCircuit compiled, AnalogDevice device, StressTracker? stress)
        {
            var peaks = stress?.PeaksOf(device.Id).Select(p => p.Stress).ToList() ?? new List<DeviceStress>();
            string Peak(string key, string fallback)
            {
                var s = peaks.Where(p => p.Key == key).OrderByDescending(p => p.Ratio).FirstOrDefault();
                return s.Rating > 0
                    ? I($"{s.Quantity} en çok {SiValue.Format(s.Value, s.Unit, 3)}, anma {SiValue.Format(s.Rating, s.Unit, 3)} ({s.Ratio:0.0}×).")
                    : fallback;
            }

            string reason = string.IsNullOrEmpty(device.FailureReason) ? string.Empty : char.ToUpper(device.FailureReason[0], CultureInfo.GetCultureInfo("tr-TR")) + device.FailureReason.Substring(1) + ". ";
            switch (device)
            {
                case Lamp _:
                    return reason + "Flaman anma geriliminin çok üstünde çalıştırıldı.";
                case Resistor r:
                {
                    double p = Math.Abs(r.Power);
                    string pk = Peak("P", $"Harcanan güç {SiValue.Format(p, "W", 3)}, anma {SiValue.Format(r.PowerRating, "W", 3)}.");
                    return reason + pk + " Direnç, gücü ısıya çevirir; anma gücünü aşınca gövde ısınır, kömürleşir ve açık devre olur. " +
                           "P = V²/R = I²·R: direncin üzerindeki gerilimi azaltın veya daha büyük değer / daha yüksek güçlü direnç kullanın.";
                }

                case Led led:
                    return reason + Peak("I", $"LED akımı {SiValue.Format(led.Current, "A", 3)}.") +
                           $" LED nominal {SiValue.Format(led.RatedCurrent, "A")} için yapılmıştır; akım sınırlama direnci olmadan ya da çok küçük dirençle jonksiyon aşırı ısınır ve LED kalıcı olarak söner.";
                case Diode d:
                    return reason + Peak("I", Peak("T", Peak("VR", string.Empty))) +
                           " Diyot ileri yönde anma akımının, ters yönde kırılma geriliminin üzerinde zorlandı.";
                case Capacitor c:
                    return reason + (c.Polarized
                        ? "Elektrolitik kondansatör kutupludur: ters bağlanır ya da anma geriliminin üstüne çıkılırsa içindeki elektrolit gaz yapar, kapak şişer ve patlar."
                        : Peak("V", "Dielektrik, anma gerilimini aşan gerilimde delindi."));
                case Fuse f:
                    return reason + $"Sigorta {SiValue.Format(f.RatedCurrent, "A")} anma akımının üstündeki akımla eridi. Bu bir koruma: devrede kısa devre ya da aşırı yük var.";
                case Bjt q:
                    return reason + Peak("T", Peak("IC", Peak("VCE", string.Empty))) +
                           " Transistörün kayıp gücü P ≈ Vce·Ic'dir; doyuma girmeyen (yarı iletimde çalışan) ya da sınırının üstünde akım çeken transistör ısınır.";
                case Mosfet fet:
                {
                    string drive = Math.Abs(fet.Vgs) < fet.Threshold * 2.0 && !fet.IsFailed
                        ? $" Kapı gerilimi ({SiValue.Format(Math.Abs(fet.Vgs), "V")}) tam iletim için düşük: MOSFET yarı iletimde direnç gibi ısınıyor."
                        : string.Empty;
                    return reason + Peak("T", Peak("VDS", Peak("VGS", string.Empty))) + drive +
                           " Kayıp P ≈ I²·Rds(on) ya da yarı iletimde Vds·Id'dir.";
                }

                case LinearRegulator reg:
                    return reason + Peak("VIN", Peak("T", string.Empty)) +
                           $" Lineer regülatör (Vin − Vout)·I = {SiValue.Format(Math.Max(0, (reg.Vin - reg.Vout) * reg.Current), "W")} gücü ısıya çevirir.";
                case DigitalPin pin:
                    return reason + Peak("I", string.Empty) +
                           " Mikrodenetleyici pini en çok 20 mA (mutlak sınır 40 mA) verebilir; LED'i seri dirençle, motor/röle gibi yükleri transistör veya MOSFET ile sürün.";
                case DcMotor motor:
                    return reason + Peak("T", string.Empty) +
                           " Sargı akımı I²·R kadar ısı üretir; motor kilitlenince (stall) akım en yüksek değerine çıkar ve sargı yanar.";
                case AveragedPwmDriver drv:
                    return reason + Peak("I", string.Empty) + " Motor sürücü sürekli akım sınırının üstünde zorlandı.";
                case Battery b:
                    return reason + Peak("I", string.Empty) + $" Pilin iç direnci I²·R ile ısınır; {SiValue.Format(MaxDischargeCurrent(b), "A")} üstündeki sürekli akım pili aşırı ısıtır.";
                default:
                    return reason.Length > 0 ? reason : "Parça sınır değerlerinin dışında çalıştırıldı.";
            }
        }

        private static string FailureSuggestion(Model m, string refDes, AnalogDevice device, StressTracker? stress)
        {
            switch (device)
            {
                case Lamp _:
                    return "Lambayı anma gerilimiyle besleyin.";
                case Resistor r:
                {
                    double peak = stress?.PeaksOf(device.Id).Where(p => p.Stress.Key == "P").Select(p => p.Stress.Value).DefaultIfEmpty(Math.Abs(r.Power)).Max() ?? Math.Abs(r.Power);
                    double need = StandardPower(2.0 * Math.Abs(peak));
                    return $"En az {SiValue.Format(need, "W")}'lık direnç kullanın (2× güvenlik payı) ya da direnç değerini artırarak akımı düşürün. Sonra parçayı onarın (değiştirin).";
                }

                case Led _:
                {
                    var (vs, src) = SupplyFor(m, m.Net(refDes, "A"));
                    double vf = m.Num(refDes, "vf", 2.0), iF = m.Num(refDes, "if", 0.02);
                    return vs > vf
                        ? I($"LED'e seri {SiValue.Format(StandardResistor((vs - vf) / iF), "Ω")} direnç ekleyin: R = ({vs:0.##} V − {vf:0.##} V) / {SiValue.Format(iF, "A")}. Sonra LED'i değiştirin (onarın).")
                        : "LED'e seri akım sınırlama direnci ekleyin: R = (Vkaynak − Vf) / If. Sonra LED'i değiştirin (onarın).";
                }

                case Capacitor c when c.Polarized:
                    return "Kondansatörü + ucu yüksek gerilime bakacak şekilde bağlayın ve gerilim değeri devredeki en yüksek gerilimin en az 1.5 katı olanı seçin.";
                case Capacitor _:
                    return "Daha yüksek gerilim değerli kondansatör seçin.";
                case Fuse _:
                    return "Önce kısa devreyi / aşırı yükü bulun (Arıza tespiti ve multimetre ile), sonra sigortayı değiştirin. Sigortayı büyütmek sorunu gizler.";
                case Bjt _:
                case Mosfet _:
                    return "Transistörü tam doyuma/iletime sürün, akımı sınırlayın veya soğutucu ekleyin; endüktif yüklerde serbest dolaşım diyodu kullanın.";
                case LinearRegulator _:
                    return "Giriş gerilimini regülatörün sınırının altında tutun; yüksek giriş/çıkış farkında soğutucu veya anahtarlamalı regülatör kullanın.";
                case DigitalPin _:
                    return "Yükü pinden doğrudan değil, seri direnç ya da transistör/MOSFET üzerinden sürün.";
                case DcMotor _:
                    return "Motorun kilitlenmesini önleyin, akım sınırlı sürücü kullanın ve anma geriliminde çalıştırın.";
                case AveragedPwmDriver _:
                    return "Motorun kilitlenme akımına dayanabilen bir sürücü seçin (ör. BTS7960 / VNH5019) ya da akım sınırlama ekleyin.";
                case Battery _:
                    return "Kısa devreyi giderin, deşarj akımını pilin C değerine göre sınırlayın; lityum pillerde koruma devresi (BMS) kullanın.";
                default:
                    return "Parçayı sınırları içinde çalıştırın ve onarın (değiştirin).";
            }
        }

        // ------------------------------------------------------------------ structural rules

        private static bool IsLowImpedance(Model m, SchematicPart part, PartDefinition def, Dictionary<string, string> p, out (string A, string B)[] pairs)
        {
            pairs = Array.Empty<(string, string)>();
            switch (def.Id)
            {
                case "resistor":
                    if (SiValue.Parse(p.TryGetValue("resistance", out var r) ? r : "1k", 1000) > 0.5) return false;
                    pairs = new[] { ("A", "B") };
                    return true;
                case "fuse":
                case "inductor":
                case "ammeter":
                    pairs = def.Id == "ammeter" ? new[] { ("+", "-") } : new[] { ("A", "B") };
                    return true;
                case "button":
                case "switch":
                {
                    bool closed = p.TryGetValue("closed", out var c) && (c == "true" || c == "1" || c == "closed");
                    if (m.Sim != null && m.Compiled?.DevicesOf(part.RefDes).FirstOrDefault() is Switch sw) closed = sw.Closed;
                    if (!closed) return false;
                    pairs = new[] { ("A", "B") };
                    return true;
                }

                case "multimeter":
                    pairs = new[] { ("mA", "COM"), ("A", "COM") };
                    return true;
            }

            return false;
        }

        private static void CheckShorts(Model m, List<Diagnosis> list)
        {
            var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Find(string x)
            {
                while (parent.TryGetValue(x, out var p) && !string.Equals(p, x, StringComparison.OrdinalIgnoreCase)) x = p;
                return x;
            }

            var low = new List<(string RefDes, string Net)>();
            foreach (var (part, def, p) in m.Parts.Values)
            {
                if (!IsLowImpedance(m, part, def, p, out var pairs)) continue;
                foreach (var (a, b) in pairs)
                {
                    string? na = m.Net(part.RefDes, a), nb = m.Net(part.RefDes, b);
                    if (na == null || nb == null) continue;
                    string ra = Find(na), rb = Find(nb);
                    if (!string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase)) parent[ra] = rb;
                    low.Add((part.RefDes, na));
                }
            }

            string Root(string? net) => net == null ? string.Empty : Find(net);

            foreach (var (part, def, p) in m.Parts.Values)
            {
                string plusPin, minusPin;
                switch (def.Id)
                {
                    case "dc-source":
                    case "bench-psu":
                    case "acdc-psu":
                    case "battery":
                        plusPin = "+";
                        minusPin = "-";
                        break;
                    case "regulator":
                        plusPin = "OUT";
                        minusPin = "GND";
                        break;
                    default:
                        if (def.Symbol != SymbolKind.Board) continue;
                        plusPin = "5V";
                        minusPin = "GND1";
                        break;
                }

                string? np = m.Net(part.RefDes, plusPin), nn = m.Net(part.RefDes, minusPin);
                if (np == null || nn == null) continue;
                if (m.Conns(np).Count() <= 1 && !string.Equals(np, nn, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(Root(np), Root(nn), StringComparison.OrdinalIgnoreCase)) continue;
                var chain = new List<string> { part.RefDes };
                chain.AddRange(low.Where(x => string.Equals(Root(x.Net), Root(np), StringComparison.OrdinalIgnoreCase)).Select(x => x.RefDes));
                bool direct = string.Equals(np, nn, StringComparison.OrdinalIgnoreCase);
                string what = def.Id == "regulator" ? "regülatör çıkışı" : def.Symbol == SymbolKind.Board ? "kartın 5V beslemesi" : "kaynağın + ve − ucu";
                var d = new Diagnosis(DiagnosisSeverity.Error, "SUPPLY_SHORT", $"{part.RefDes}: besleme kısa devre",
                    direct
                        ? $"{what} aynı hatta ({np}) bağlı: aralarında hiçbir yük yok."
                        : $"{what} {string.Join(", ", chain.Skip(1).Distinct())} üzerinden (neredeyse 0 Ω) birbirine bağlı. Kaynak sınırsız akım vermeye çalışır; sigorta atar, pil ısınır, iletkenler yanar.",
                    "Kablolamayı kontrol edin: + ile − arasında mutlaka bir yük (direnç, LED+direnç, motor...) olmalı. Ampermetre/multimetre akım girişi devreye SERİ bağlanır, kaynağın uçlarına değil.",
                    chain.Distinct().ToArray())
                { Net = np, MarginPercent = -100 };
                list.Add(d);
            }

            // Two stiff supplies with different voltages tied together.
            var byRoot = new Dictionary<string, (double V, string Src, string Net)>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in m.Supply)
            {
                if (kv.Value.Source.EndsWith("hattı", StringComparison.Ordinal)) continue;
                string root = Root(kv.Key);
                if (byRoot.TryGetValue(root, out var other) && Math.Abs(other.V - kv.Value.Volts) > 0.2 && other.Net != kv.Key)
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, "SUPPLY_CONFLICT", "Farklı gerilimli iki kaynak birbirine bağlı",
                        I($"{other.Src} ({other.V:0.##} V) ve {kv.Value.Source} ({kv.Value.Volts:0.##} V) düşük dirençle birleşiyor: aralarında büyük akım akar."),
                        "Kaynakları ayırın; ortak olan yalnızca GND olmalı.", Head(other.Src), Head(kv.Value.Source)));
                }
                else if (!byRoot.ContainsKey(root))
                {
                    byRoot[root] = (kv.Value.Volts, kv.Value.Source, kv.Key);
                }
            }
        }

        private static string Head(string source)
        {
            int cut = source.IndexOfAny(new[] { ' ', '.' });
            return cut > 0 ? source.Substring(0, cut) : source;
        }

        private static (double Volts, string Source) SupplyFor(Model m, string? net)
        {
            if (net == null) return (double.NaN, string.Empty);
            if (m.Supply.TryGetValue(net, out var s)) return s;
            if (m.McuPins.TryGetValue(net, out var p)) return p;
            return (double.NaN, string.Empty);
        }

        private static void CheckLeds(Model m, List<Diagnosis> list)
        {
            foreach (var (part, def, p) in m.Parts.Values)
            {
                if (def.Symbol != SymbolKind.Led) continue;
                string? a = m.Net(part.RefDes, "A"), k = m.Net(part.RefDes, "K");
                if (a == null || k == null) continue;
                double vf = SiValue.Parse(p.TryGetValue("vf", out var vfs) ? vfs : "2", 2.0);
                double iF = SiValue.Parse(p.TryGetValue("if", out var ifs) ? ifs : "20m", 0.02);
                var (va, srcA) = SupplyFor(m, a);
                if (!double.IsNaN(va) && m.IsLow(k))
                {
                    bool pin = m.McuPins.ContainsKey(a);
                    double r = Math.Max(1.0, (va - vf) / iF);
                    double rs = StandardResistor(r);
                    double pw = iF * iF * rs;
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, pin ? "LED_ON_PIN" : "LED_NO_RESISTOR",
                        $"{part.RefDes}: akım sınırlama direnci yok",
                        pin
                            ? I($"LED doğrudan {srcA} pinine ({va:0.#} V) ve toprağa bağlı. Pin ~{SiValue.Format((va - vf) / 25.0, "A")} vermeye zorlanır: hem LED hem pin (en çok 40 mA) zarar görür.")
                            : I($"LED anodu doğrudan {srcA} ({va:0.##} V) hattına, katodu toprağa bağlı. LED'in Vf'si ({vf:0.##} V) kaynaktan küçük; aradaki farkı karşılayacak direnç olmadığından akım yalnızca LED'in iç direnciyle sınırlanır ve LED yanar."),
                        I($"Anoda seri direnç ekleyin: R = ({va:0.##} − {vf:0.##}) V / {SiValue.Format(iF, "A")} = {SiValue.Format(r, "Ω")} → standart {SiValue.Format(rs, "Ω")} ({SiValue.Format(StandardPower(2 * pw), "W")})."),
                        part.RefDes) { Net = a, MarginPercent = -100 });
                }

                var (vk, srcK) = SupplyFor(m, k);
                if (!double.IsNaN(vk) && m.IsLow(a) && !m.McuPins.ContainsKey(k))
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, "LED_REVERSED", $"{part.RefDes}: LED ters bağlı",
                        I($"Katot (K) {srcK} hattına, anot (A) toprağa bağlı: LED ters kutuplanır ve hiç yanmaz. {vk:0.#} V ters gerilim LED'lerin dayanabildiği ~5 V'u da aşabilir."),
                        "LED'i çevirin: uzun bacak (anot, A) + tarafına, düz kenarlı bacak (katot, K) toprağa.", part.RefDes) { Net = k });
                }

                if (m.Sim != null && m.Compiled?.DevicesOf(part.RefDes).FirstOrDefault() is Led led && !led.IsFailed && led.Voltage < -1.0)
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Warning, "LED_REVERSE_BIAS", $"{part.RefDes}: LED ters kutuplanmış",
                        I($"LED üzerinde {led.Voltage:0.00} V ters gerilim var; LED sönük kalır."),
                        "LED'in yönünü kontrol edin (A yüksek, K düşük gerilime).", part.RefDes));
                }
            }
        }

        private static void CheckPolarity(Model m, List<Diagnosis> list)
        {
            foreach (var (part, def, p) in m.Parts.Values)
            {
                if (def.Symbol != SymbolKind.PolarCapacitor) continue;
                string? plus = m.Net(part.RefDes, "+"), minus = m.Net(part.RefDes, "-");
                var (vm, src) = SupplyFor(m, minus);
                bool staticReverse = !double.IsNaN(vm) && m.IsLow(plus);
                double vNow = double.NaN;
                if (m.Sim != null && m.Compiled?.DevicesOf(part.RefDes).FirstOrDefault() is Capacitor cap && !cap.IsFailed) vNow = cap.Voltage;
                if (staticReverse || vNow < -0.5)
                {
                    double rating = SiValue.Parse(p.TryGetValue("voltage", out var vr) ? vr : "16", 16);
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, "CAP_REVERSED", $"{part.RefDes}: elektrolitik kondansatör ters bağlı",
                        (staticReverse ? I($"− ucu {src} ({vm:0.##} V) hattına, + ucu toprağa bağlı.") : I($"Üzerinde {vNow:0.00} V ters gerilim var.")) +
                        " Elektrolitik kondansatörler ters gerilimde içten ısınır, gaz yapar; kapak şişer ve patlar.",
                        I($"Kondansatörü çevirin: + ucu yüksek gerilime, şeritli (−) ucu toprağa. Anma gerilimi ({rating:0.#} V) devredeki gerilimin ≥1.5 katı olmalı."),
                        part.RefDes) { Net = minus });
                }
            }
        }

        private enum Drive
        {
            None,
            Driven,
            ThroughOpenSwitch
        }

        private static void CheckFloatingInputs(Model m, List<Diagnosis> list)
        {
            foreach (var (part, def, p) in m.Parts.Values)
            {
                string[] inputs;
                string kind;
                if (def.Symbol == SymbolKind.Gate)
                {
                    inputs = def.Pins.Where(x => x.Name == "A" || x.Name == "B").Select(x => x.Name).ToArray();
                    kind = "CMOS lojik girişi";
                }
                else if (def.Symbol == SymbolKind.Nmos || def.Symbol == SymbolKind.Pmos)
                {
                    inputs = new[] { "G" };
                    kind = "MOSFET kapısı";
                }
                else if (def.Symbol == SymbolKind.OpAmp)
                {
                    inputs = new[] { "IN+", "IN-" };
                    kind = "op-amp girişi";
                }
                else if (def.Symbol == SymbolKind.Board)
                {
                    inputs = def.Pins.Where(x => IsIoPin(x.Name)).Select(x => x.Name).ToArray();
                    kind = "mikrodenetleyici girişi";
                }
                else
                {
                    continue;
                }

                foreach (string pin in inputs)
                {
                    string? net = m.Net(part.RefDes, pin);
                    bool board = def.Symbol == SymbolKind.Board;
                    if (net == null) continue;
                    var others = m.Conns(net).Where(c => !(string.Equals(c.RefDes, part.RefDes, StringComparison.OrdinalIgnoreCase) && c.Pin == pin)).ToList();
                    if (board)
                    {
                        // Unused board pins are normal; only pins wired to a switch without a pull resistor matter.
                        if (!others.Any(c => IsSwitch(m, c.RefDes))) continue;
                        var mode = (m.Compiled?.DevicesOf(part.RefDes).OfType<DigitalPin>().FirstOrDefault(d => d.Id.EndsWith("." + pin, StringComparison.OrdinalIgnoreCase)))?.Mode;
                        if (mode == PinMode.InputPullup || mode == PinMode.OutputHigh || mode == PinMode.OutputLow) continue;
                    }

                    if (others.Count == 0 && !m.IsGround(net) && !m.Supply.ContainsKey(net))
                    {
                        if (board) continue;
                        list.Add(new Diagnosis(def.Symbol == SymbolKind.OpAmp ? DiagnosisSeverity.Warning : DiagnosisSeverity.Warning, "FLOATING_INPUT",
                            $"{part.RefDes}.{pin}: {kind} boşta",
                            def.Symbol == SymbolKind.Nmos || def.Symbol == SymbolKind.Pmos
                                ? "Kapı hiçbir yere bağlı değil. MOSFET kapısı bir kondansatördür: boşta kalınca statik yükle rastgele açılır/kapanır ve yarı iletimde ısınabilir."
                                : $"{kind.Substring(0, 1).ToUpper(CultureInfo.GetCultureInfo("tr-TR")) + kind.Substring(1)} hiçbir yere bağlı değil. CMOS girişler çok yüksek empedanslıdır; boşta kalınca gürültüyle rastgele 0/1 okur ve fazladan akım çeker.",
                            def.Symbol == SymbolKind.Nmos || def.Symbol == SymbolKind.Pmos
                                ? "Kapı ile source arasına 10–100 kΩ direnç ekleyin ve kapıyı bir sürücüye bağlayın."
                                : "Kullanılmayan girişi GND'ye veya VCC'ye bağlayın; sinyal girişine 10 kΩ pull-up/pull-down ekleyin.",
                            part.RefDes) { Net = net });
                        continue;
                    }

                    var drive = Reach(m, net, part.RefDes, pin, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    if (drive == Drive.Driven) continue;
                    string sw = others.Select(c => c.RefDes).FirstOrDefault(r => IsSwitch(m, r)) ?? string.Empty;
                    list.Add(new Diagnosis(DiagnosisSeverity.Warning, "FLOATING_INPUT",
                        $"{part.RefDes}.{pin}: {kind} {(drive == Drive.ThroughOpenSwitch ? "anahtar açıkken boşta kalıyor" : "tanımsız seviyede")}",
                        drive == Drive.ThroughOpenSwitch
                            ? $"Giriş yalnızca {(sw.Length > 0 ? sw : "anahtar")} kapalıyken bir gerilime bağlanıyor; açıkken hiçbir şey seviyesini belirlemiyor (pull-up/pull-down direnci yok). Okunan değer rastgele değişir."
                            : "Bu hatta girişi bir seviyeye çeken direnç ya da sürücü yok.",
                        board
                            ? "pinMode(pin, INPUT_PULLUP) kullanın (buton GND'ye bağlıyken) ya da pinden GND'ye 10 kΩ pull-down direnci ekleyin."
                            : def.Symbol == SymbolKind.Nmos || def.Symbol == SymbolKind.Pmos
                                ? "Kapıdan source'a 10–100 kΩ direnç ekleyin: sürücü kapalıyken MOSFET kesin olarak kapanır."
                                : "Girişten GND'ye (veya VCC'ye) 10 kΩ pull-down (pull-up) direnci ekleyin.",
                        part.RefDes, sw) { Net = net });
                }
            }
        }

        private static bool IsSwitch(Model m, string refDes)
        {
            var def = m.Def(refDes);
            return def != null && (def.Symbol == SymbolKind.PushButton || def.Symbol == SymbolKind.Switch);
        }

        /// <summary>Does anything give this net a DC level? Open switches are crossed but remembered.</summary>
        private static Drive Reach(Model m, string net, string selfRef, string selfPin, HashSet<string> visited)
        {
            if (!visited.Add(net)) return Drive.None;
            if (m.IsGround(net) || m.Supply.ContainsKey(net)) return Drive.Driven;
            var result = Drive.None;
            foreach (var c in m.Conns(net))
            {
                if (string.Equals(c.RefDes, selfRef, StringComparison.OrdinalIgnoreCase) && c.Pin == selfPin) continue;
                var def = m.Def(c.RefDes);
                if (def == null) continue;
                switch (def.Symbol)
                {
                    case SymbolKind.Capacitor:
                    case SymbolKind.PolarCapacitor:
                        continue;
                    case SymbolKind.Gate when c.Pin == "A" || c.Pin == "B":
                    case SymbolKind.Nmos when c.Pin == "G":
                    case SymbolKind.Pmos when c.Pin == "G":
                    case SymbolKind.OpAmp when c.Pin.StartsWith("IN", StringComparison.Ordinal):
                        continue;
                    case SymbolKind.Board when IsIoPin(c.Pin):
                    {
                        // A board pin drives only when the firmware makes it an output or pull-up.
                        var pinDev = m.Compiled?.DevicesOf(c.RefDes).OfType<DigitalPin>().FirstOrDefault(d => d.Id.EndsWith("." + c.Pin, StringComparison.OrdinalIgnoreCase));
                        if (pinDev != null && pinDev.Mode != PinMode.Input) return Drive.Driven;
                        continue;
                    }

                    case SymbolKind.PushButton:
                    case SymbolKind.Switch:
                    {
                        bool closed = m.Compiled?.DevicesOf(c.RefDes).FirstOrDefault() is Switch sw && sw.Closed;
                        string otherPin = c.Pin == "A" ? "B" : "A";
                        string? other = m.Net(c.RefDes, otherPin);
                        if (other == null) continue;
                        var through = Reach(m, other, c.RefDes, otherPin, visited);
                        if (through == Drive.Driven) result = closed ? Drive.Driven : Drive.ThroughOpenSwitch;
                        if (result == Drive.Driven) return result;
                        continue;
                    }

                    default:
                        return Drive.Driven;
                }
            }

            return result;
        }

        private static void CheckMotorsAndCoils(Model m, List<Diagnosis> list)
        {
            foreach (var (part, def, p) in m.Parts.Values)
            {
                bool motor = def.Symbol == SymbolKind.Motor;
                bool relay = def.Symbol == SymbolKind.Relay;
                if (!motor && !relay) continue;
                string pa = motor ? "A" : "COIL1", pb = motor ? "B" : "COIL2";
                string? na = m.Net(part.RefDes, pa), nb = m.Net(part.RefDes, pb);
                if (na == null || nb == null) continue;

                double istall = motor ? SiValue.Parse(p.TryGetValue("istall", out var s) ? s : "1.5", 1.5) : 0.0;
                foreach (var net in new[] { na, nb })
                {
                    if (motor && m.McuPins.TryGetValue(net, out var mcu))
                    {
                        list.Add(new Diagnosis(DiagnosisSeverity.Error, "MOTOR_ON_PIN", $"{part.RefDes}: motor doğrudan mikrodenetleyici pinine bağlı",
                            $"{mcu.Source} pini en çok 40 mA verir; motor kalkışta {SiValue.Format(istall, "A")}'e kadar akım ister. Pin yanar, motor dönmez.",
                            "Motoru bir MOSFET/transistör ya da motor sürücü (TB6612, L298N) üzerinden sürün; serbest dolaşım diyodu ekleyin.",
                            part.RefDes, Head(mcu.Source)) { Net = net, MarginPercent = (1 - istall / 0.04) * 100 });
                    }

                    foreach (var c in m.Conns(net))
                    {
                        var d = m.Def(c.RefDes);
                        if (d == null) continue;
                        if (motor && (d.Id == "motor-driver" && c.Pin == "OUT" || d.Id == "flight-controller" && c.Pin.StartsWith("M", StringComparison.Ordinal)))
                        {
                            double imax = d.Id == "flight-controller" ? 4.0 : m.Num(c.RefDes, "imax", 1.2);
                            if (istall > imax)
                            {
                                list.Add(new Diagnosis(DiagnosisSeverity.Warning, "DRIVER_STALL", $"{part.RefDes}: kilitlenme akımı sürücü sınırının üstünde",
                                    $"Motorun kilitlenme (stall) akımı {SiValue.Format(istall, "A")}, {c.RefDes} sürücüsünün sürekli akımı {SiValue.Format(imax, "A")}. Kalkışta ve motor zorlanınca sürücü aşırı ısınır.",
                                    "Daha yüksek akımlı sürücü seçin ya da motoru yumuşak kalkışla (PWM rampası) sürün.",
                                    part.RefDes, c.RefDes) { Net = net, MarginPercent = (1 - istall / imax) * 100 });
                            }
                        }
                    }
                }

                // Switched by a transistor without a flyback diode across it.
                bool switched = new[] { na, nb }.Any(n => m.Conns(n).Any(c =>
                {
                    var d = m.Def(c.RefDes);
                    return d != null && (d.Symbol == SymbolKind.Nmos && c.Pin == "D" || d.Symbol == SymbolKind.Pmos && c.Pin == "D" ||
                                         (d.Symbol == SymbolKind.Npn || d.Symbol == SymbolKind.Darlington) && c.Pin == "C" || d.Symbol == SymbolKind.Pnp && c.Pin == "C");
                }));
                bool diode = m.Parts.Values.Any(x =>
                    (x.Def.Symbol == SymbolKind.Diode || x.Def.Symbol == SymbolKind.Schottky) &&
                    (m.Net(x.Part.RefDes, "A") == na && m.Net(x.Part.RefDes, "K") == nb || m.Net(x.Part.RefDes, "A") == nb && m.Net(x.Part.RefDes, "K") == na));
                if (switched && !diode)
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Warning, "NO_FLYBACK", $"{part.RefDes}: serbest dolaşım (flyback) diyodu yok",
                        $"{(motor ? "Motor" : "Röle bobini")} bir transistörle anahtarlanıyor. Transistör kesime gittiğinde bobin akımı aniden kesilemez; L·di/dt gerilim sıçraması transistörü delebilir.",
                        $"{part.RefDes} uçlarına ters yönde bir diyot (1N4007 / 1N5819) bağlayın: katot + tarafına.", part.RefDes));
                }
            }
        }

        private static void CheckRegulators(Model m, List<Diagnosis> list, double ambientC)
        {
            foreach (var (part, def, p) in m.Parts.Values)
            {
                if (def.Id != "regulator") continue;
                double vout = SiValue.Parse(p.TryGetValue("vout", out var vo) ? vo : "5", 5);
                double drop = SiValue.Parse(p.TryGetValue("dropout", out var dr) ? dr : "2", 2);
                double vinMax = SiValue.Parse(p.TryGetValue("vinmax", out var vm) ? vm : "35", 35);
                double rth = SiValue.Parse(p.TryGetValue("rth", out var rt) ? rt : "65", 65);
                var reg = m.Compiled?.DevicesOf(part.RefDes).FirstOrDefault() as LinearRegulator;
                double vin = m.Sim != null && reg != null ? reg.Vin : SupplyFor(m, m.Net(part.RefDes, "IN")).Volts;
                if (double.IsNaN(vin)) continue;
                if (vin > 0.5 && vin < vout + drop)
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Warning, "REG_DROPOUT", $"{part.RefDes}: regülatör düşme geriliminde",
                        I($"Giriş {vin:0.00} V; {vout:0.##} V çıkış için en az {vout + drop:0.##} V gerekir (düşme gerilimi {drop:0.##} V). Çıkış {(reg != null && m.Sim != null ? reg.Vout.ToString("0.00", Inv) + " V'a" : "ayarın altına")} düşer ve girişteki dalgalanmayı geçirir."),
                        I($"Girişi en az {vout + drop + 0.5:0.#} V yapın ya da düşük düşmeli (LDO) bir regülatör kullanın."),
                        part.RefDes) { MarginPercent = (vin / (vout + drop) - 1) * 100 });
                }

                if (vin > vinMax)
                {
                    list.Add(new Diagnosis(DiagnosisSeverity.Error, "REG_OVERVOLTAGE", $"{part.RefDes}: giriş gerilimi sınırın üstünde",
                        I($"Giriş {vin:0.0} V, regülatörün en yüksek giriş gerilimi {vinMax:0.#} V."),
                        "Giriş gerilimini düşürün veya daha yüksek gerilimli regülatör seçin.", part.RefDes) { MarginPercent = (1 - vin / vinMax) * 100 });
                }

                if (reg != null && m.Sim != null && reg.Current > 1e-3)
                {
                    double tj = ambientC + reg.Power * rth;
                    if (reg.InThermalShutdown)
                    {
                        list.Add(new Diagnosis(DiagnosisSeverity.Error, "REG_THERMAL", $"{part.RefDes}: termal kapanmada",
                            I($"Kayıp güç {SiValue.Format(reg.Power, "W")}: jonksiyon {reg.ShutdownTempC:0} °C'ye ulaştı ve regülatör çıkışı kapattı."),
                            "Soğutucu ekleyin, giriş-çıkış farkını azaltın veya anahtarlamalı (buck) regülatör kullanın.", part.RefDes));
                    }
                    else if (tj > 125)
                    {
                        list.Add(new Diagnosis(DiagnosisSeverity.Warning, "REG_HEATSINK", $"{part.RefDes}: soğutucu gerekli",
                            I($"Kayıp güç (Vin − Vout)·I = {SiValue.Format(reg.Power, "W")}; soğutucusuz Rth = {rth:0} K/W ile kalıcı jonksiyon sıcaklığı ≈ {tj:0} °C olur (sınır ~125–150 °C)."),
                            I($"Soğutucu kullanın (gerekli toplam Rth < {(125 - ambientC) / Math.Max(1e-3, reg.Power):0} K/W) veya giriş gerilimini düşürün."),
                            part.RefDes) { MarginPercent = (1 - (tj - ambientC) / (150 - ambientC)) * 100 });
                    }
                }
            }
        }

        private static void CheckOpenPins(Model m, List<Diagnosis> list)
        {
            foreach (var (part, def, p) in m.Parts.Values)
            {
                if (def.Build == null || def.Pins.Count > 5) continue;
                if (def.Symbol is SymbolKind.Board or SymbolKind.Connector or SymbolKind.Module or SymbolKind.Multimeter or SymbolKind.Ground or SymbolKind.PowerRail or SymbolKind.Relay) continue;
                foreach (var pin in def.Pins)
                {
                    if (def.Symbol is SymbolKind.Gate or SymbolKind.Nmos or SymbolKind.Pmos or SymbolKind.OpAmp && (pin.Name == "A" || pin.Name == "B" || pin.Name == "G" || pin.Name.StartsWith("IN", StringComparison.Ordinal))) continue;
                    if (def.Symbol == SymbolKind.Potentiometer && pin.Name == "W") continue;
                    string? net = m.Net(part.RefDes, pin.Name);
                    if (net == null) continue;
                    var n = m.Nets[net];
                    if (n.IsGround || n.Connections.Count > 1) continue;
                    bool labelOnly = !n.IsAutoNamed;
                    list.Add(new Diagnosis(DiagnosisSeverity.Warning, "OPEN_PIN",
                        $"{part.RefDes}.{pin.Name} bağlı değil",
                        labelOnly
                            ? $"Pin yalnızca '{net}' etiketine bağlı; bu hatta başka hiçbir parça yok. Devre burada açık: bu koldan akım akmaz."
                            : "Bu pine hiçbir kablo gelmiyor. Devre burada açık: bu koldan akım akmaz ve parça çalışmaz.",
                        "Pini devrenin ilgili noktasına kablo ile bağlayın (pine tıklayıp hedefe tıklayın).", part.RefDes) { Net = net });
                }
            }
        }

        private static void CheckGround(Model m, List<Diagnosis> list)
        {
            if (m.Compiled == null) return;
            foreach (var w in m.Compiled.Warnings)
            {
                if (w.IndexOf("GND'ye bağlı değil", StringComparison.Ordinal) < 0) continue;
                int q1 = w.IndexOf('\''), q2 = q1 >= 0 ? w.IndexOf('\'', q1 + 1) : -1;
                string netName = q1 >= 0 && q2 > q1 ? w.Substring(q1 + 1, q2 - q1 - 1) : string.Empty;
                var parts = m.Conns(netName).Select(c => c.RefDes).Take(3).ToArray();
                if (parts.Length == 0 && netName.Contains('.')) parts = new[] { netName.Substring(0, netName.IndexOf('.')) };
                list.Add(new Diagnosis(DiagnosisSeverity.Warning, "NO_GROUND", "Toprak (GND) referansı eksik",
                    $"'{netName}' içeren devre parçasının GND ile bağlantısı yok. Gerilimler bir referansa göre ölçülür; simülatör bu bölümde rastgele bir noktayı 0 V kabul etti.",
                    "Kaynağın − ucunu ve devrenin ortak noktasını bir GND sembolüne bağlayın.", parts) { Net = netName });
            }

            if (!m.Netlist.Nets.Any(n => n.IsGround) && m.Parts.Values.Any(x => x.Def.Build != null))
            {
                list.Add(new Diagnosis(DiagnosisSeverity.Warning, "NO_GROUND", "Devrede GND sembolü yok",
                    "Hiçbir hat GND olarak işaretlenmemiş.", "Kütüphaneden 'Toprak (GND)' sembolünü ekleyip kaynağın − ucuna bağlayın."));
            }
        }

        private static void CheckRatings(Model m, List<Diagnosis> list, StressTracker? stress, double ambientC)
        {
            if (m.Compiled == null) return;
            foreach (var kv in m.Compiled.DevicesByPart)
            {
                foreach (var device in kv.Value)
                {
                    if (device.IsFailed) continue;
                    var byKey = new Dictionary<string, DeviceStress>();
                    foreach (var s in Stresses(device, ambientC)) byKey[s.Key] = s;
                    if (stress != null)
                    {
                        foreach (var peak in stress.PeaksOf(device.Id))
                        {
                            if (!byKey.TryGetValue(peak.Stress.Key, out var now) || peak.Stress.Ratio > now.Ratio) byKey[peak.Stress.Key] = peak.Stress;
                        }
                    }

                    foreach (var s in byKey.Values)
                    {
                        if (s.Ratio < 0.6) continue;
                        string who = device.Id == kv.Key ? kv.Key : $"{kv.Key} ({device.Id.Substring(Math.Min(device.Id.Length, kv.Key.Length)).TrimStart('.')})";
                        var sev = s.Ratio > 1.0 ? DiagnosisSeverity.Error : s.Ratio >= 0.8 ? DiagnosisSeverity.Warning : DiagnosisSeverity.Info;
                        string valueText = SiValue.Format(Math.Abs(s.Value), s.Unit, 3), ratingText = SiValue.Format(s.Rating, s.Unit, 3);
                        string detail = s.Key == "T"
                            ? I($"{s.Quantity} {valueText}; sınır {ratingText}. Sıcaklık artışının sınıra oranı %{s.Ratio * 100:0}.")
                            : I($"{s.Quantity} {valueText}, anma değeri {ratingText} (%{s.Ratio * 100:0} kullanım).");
                        string title = sev == DiagnosisSeverity.Error
                            ? $"{who}: {s.Quantity.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} anma değerinin üstünde"
                            : sev == DiagnosisSeverity.Warning
                                ? $"{who}: {s.Quantity.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} sınıra yakın"
                                : I($"{who}: {s.Quantity.ToLower(CultureInfo.GetCultureInfo("tr-TR"))} marjı %{(1 - s.Ratio) * 100:0}");
                        list.Add(new Diagnosis(sev, sev == DiagnosisSeverity.Info ? "MARGIN" : "OVER_RATING", title, detail,
                            RatingSuggestion(device, s), kv.Key) { MarginPercent = (1 - s.Ratio) * 100 });
                    }

                    if (device is Battery b && b.StateOfCharge < 0.1 && !b.IsFailed)
                    {
                        list.Add(new Diagnosis(DiagnosisSeverity.Info, "BATTERY_LOW", $"{kv.Key}: pil bitmek üzere",
                            I($"Kalan şarj %{b.StateOfCharge * 100:0}; iç direnç yükseliyor ve gerilim düşüyor."), "Pili değiştirin veya şarj edin.", kv.Key));
                    }

                    if (device is BenchSupply psu && psu.Regulation == SupplyRegulation.ConstantCurrent)
                    {
                        list.Add(new Diagnosis(DiagnosisSeverity.Info, "PSU_CC", $"{kv.Key}: güç kaynağı akım sınırında (CC)",
                            I($"Yük {SiValue.Format(psu.CurrentLimit, "A")} sınırından fazlasını istiyor; çıkış {psu.OutputVoltage:0.00} V'a düştü (ayar {psu.Voltage:0.00} V)."),
                            "Bu bir kısa devre ya da beklenenden fazla akım çeken bir yük olabilir; akım sınırı devreyi koruyor.", kv.Key));
                    }
                }
            }
        }

        private static string RatingSuggestion(AnalogDevice device, DeviceStress s)
        {
            switch (device)
            {
                case Resistor _ when s.Key == "P":
                    return $"En az {SiValue.Format(StandardPower(2 * Math.Abs(s.Value)), "W")}'lık direnç kullanın ya da direnç değerini artırın.";
                case Capacitor _ when s.Key == "VR":
                    return "Kondansatörü doğru yönde bağlayın.";
                case Capacitor _:
                    return $"Anma gerilimi en az {SiValue.Format(1.5 * Math.Abs(s.Value), "V")} olan kondansatör seçin.";
                case Led _:
                    return "Seri direnci büyütün: R = (Vkaynak − Vf) / If.";
                case Diode _:
                    return "Daha yüksek akım/gerilim değerli diyot seçin.";
                case Bjt _ when s.Key == "T":
                case Mosfet _ when s.Key == "T":
                    return "Soğutucu ekleyin ya da transistörü tam iletime sürün (doyum / yeterli Vgs).";
                case Mosfet _ when s.Key == "VGS":
                    return "Kapı gerilimini sınırlayın (zener veya gerilim bölücü).";
                case Bjt _:
                case Mosfet _:
                    return "Daha yüksek değerli bir transistör seçin veya yükü azaltın.";
                case LinearRegulator _ when s.Key == "T":
                    return "Soğutucu ekleyin veya giriş-çıkış farkını azaltın.";
                case LinearRegulator _:
                    return "Yükü azaltın veya daha güçlü regülatör seçin.";
                case Fuse _:
                    return "Yük akımını kontrol edin; sigorta anma akımının ~%75'inden fazlası sürekli akmamalı.";
                case DigitalPin _:
                    return "Yükü transistör/MOSFET ile sürün ya da seri direnci büyütün (pin başına ≤ 20 mA).";
                case DcMotor _:
                    return "Motor zorlanıyor: yükü azaltın, kilitlenmeyi önleyin.";
                case AveragedPwmDriver _:
                    return "Daha yüksek akımlı motor sürücü seçin.";
                case Battery _:
                    return "Deşarj akımını azaltın ya da daha yüksek C değerli pil kullanın.";
                case Lamp _:
                    return "Lambayı anma geriliminde çalıştırın.";
                case Multimeter _:
                    return "Bu akım için kırmızı probu 10 A girişine takın.";
                default:
                    return "Parçayı sınırları içinde çalıştırın.";
            }
        }

        // ------------------------------------------------------------------ helpers

        private static readonly double[] E12 = { 1.0, 1.2, 1.5, 1.8, 2.2, 2.7, 3.3, 3.9, 4.7, 5.6, 6.8, 8.2 };

        /// <summary>Smallest E12 value at least <paramref name="ohms"/>.</summary>
        public static double StandardResistor(double ohms)
        {
            if (!(ohms > 0)) return 1.0;
            double exp = Math.Floor(Math.Log10(ohms));
            for (int e = (int)exp; e <= exp + 1; e++)
            {
                foreach (double mant in E12)
                {
                    double r = mant * Math.Pow(10, e);
                    if (r >= ohms * 0.999) return Math.Round(r, 6);
                }
            }

            return Math.Pow(10, exp + 1);
        }

        /// <summary>Next standard resistor power rating.</summary>
        public static double StandardPower(double watts)
        {
            foreach (double p in new[] { 0.125, 0.25, 0.5, 1, 2, 3, 5, 10, 25, 50 })
            {
                if (p >= watts) return p;
            }

            return Math.Ceiling(watts);
        }
    }
}
