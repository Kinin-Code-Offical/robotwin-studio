using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using static RobotTwin.CoreSim.Instruments.InvariantText;

namespace RobotTwin.CoreSim.Instruments
{
    /// <summary>Position of the rotary switch of a handheld digital multimeter.</summary>
    public enum MultimeterMode
    {
        DcVoltage,
        AcVoltage,
        DcCurrent,
        AcCurrent,
        Resistance,
        Continuity,
        Diode,
        Capacitance,
        Frequency
    }

    /// <summary>The jack the red test lead is plugged into (black is always in COM).</summary>
    public enum MultimeterJack
    {
        VoltOhm,
        MilliAmp,
        Amp
    }

    /// <summary>One measurement range: full scale, display resolution and (for active ranges) the source/shunt resistance.</summary>
    public sealed class MeterRange
    {
        public MeterRange(double fullScale, int decimals, double displayScale, string unit, double aux = 0.0)
        {
            FullScale = fullScale;
            Decimals = decimals;
            DisplayScale = displayScale;
            Unit = unit;
            Aux = aux;
        }

        /// <summary>Largest value shown before "OL", in SI units.</summary>
        public double FullScale { get; }

        /// <summary>Digits after the decimal point on the display.</summary>
        public int Decimals { get; }

        /// <summary>SI value × DisplayScale = displayed number (1e3 for mV, 1e-3 for kΩ).</summary>
        public double DisplayScale { get; }

        public string Unit { get; }

        /// <summary>Ω ranges: test source resistance. Current ranges: shunt. Capacitance: charge resistance.</summary>
        public double Aux { get; }

        /// <summary>Value of one count (least significant digit), SI units.</summary>
        public double Resolution => Math.Pow(10, -Decimals) / DisplayScale;

        public string Label => (FullScale * DisplayScale).ToString("0.###", CultureInfo.InvariantCulture) + " " + Unit;
    }

    /// <summary>Specification of the simulated handheld meter (a 6000-count true-RMS autoranging DMM).</summary>
    public static class MultimeterSpec
    {
        public const int Counts = 6000;
        public const double InputResistance = 10e6;
        public const double UpdateRate = 3.0;

        public const double OhmOpenVoltage = 1.0;
        public const double DiodeOpenVoltage = 3.3;
        public const double DiodeSourceResistance = 2700.0;
        public const double DiodeOverload = 3.0;
        public const double CapOpenVoltage = 3.0;
        public const double CapLow = 0.1;
        public const double CapHigh = 1.0;
        public const double CapDischargeResistance = 10.0;
        public const double ContinuityThreshold = 30.0;

        /// <summary>F 440 mA fast fuse in the mA jack, F 11 A in the A jack.</summary>
        public const double MilliAmpFuse = 0.44;
        public const double AmpFuse = 11.0;
        public const double MilliAmpShuntDefault = 1.8;
        public const double AmpShunt = 0.03;

        /// <summary>True-RMS converter bandwidth for the AC functions.</summary>
        public const double AcBandwidth = 20e3;

        /// <summary>ln((Voc - V1) / (Voc - V2)) for the RC charge timing.</summary>
        public static readonly double CapLogRatio = Math.Log((CapOpenVoltage - CapLow) / (CapOpenVoltage - CapHigh));

        private static readonly MeterRange[] Volt =
        {
            new MeterRange(0.6, 1, 1e3, "mV"), new MeterRange(6, 3, 1, "V"), new MeterRange(60, 2, 1, "V"),
            new MeterRange(600, 1, 1, "V"), new MeterRange(1000, 0, 1, "V")
        };

        private static readonly MeterRange[] MilliAmp =
        {
            new MeterRange(6e-3, 3, 1e3, "mA", 18.0), new MeterRange(60e-3, 2, 1e3, "mA", 1.8), new MeterRange(600e-3, 1, 1e3, "mA", 1.8)
        };

        private static readonly MeterRange[] Amp =
        {
            new MeterRange(6, 3, 1, "A", AmpShunt), new MeterRange(10, 2, 1, "A", AmpShunt)
        };

        private static readonly MeterRange[] Ohm =
        {
            new MeterRange(600, 1, 1, "Ω", 600), new MeterRange(6e3, 3, 1e-3, "kΩ", 6e3), new MeterRange(60e3, 2, 1e-3, "kΩ", 60e3),
            new MeterRange(600e3, 1, 1e-3, "kΩ", 600e3), new MeterRange(6e6, 3, 1e-6, "MΩ", 6e6), new MeterRange(60e6, 2, 1e-6, "MΩ", 60e6)
        };

        private static readonly MeterRange[] Continuity = { new MeterRange(600, 1, 1, "Ω", 600) };

        private static readonly MeterRange[] DiodeRange = { new MeterRange(DiodeOverload, 3, 1, "V", DiodeSourceResistance) };

        private static readonly MeterRange[] Cap = BuildCapRanges();

        private static readonly MeterRange[] Freq =
        {
            new MeterRange(100, 2, 1, "Hz"), new MeterRange(1e3, 1, 1, "Hz"), new MeterRange(1e4, 3, 1e-3, "kHz"),
            new MeterRange(1e5, 2, 1e-3, "kHz"), new MeterRange(1e6, 1, 1e-3, "kHz"), new MeterRange(1e7, 3, 1e-6, "MHz")
        };

        private static MeterRange[] BuildCapRanges()
        {
            var specs = new (double Fs, int Dec, double Scale, string Unit)[]
            {
                (6e-9, 3, 1e9, "nF"), (60e-9, 2, 1e9, "nF"), (600e-9, 1, 1e9, "nF"), (6e-6, 3, 1e6, "µF"),
                (60e-6, 2, 1e6, "µF"), (600e-6, 1, 1e6, "µF"), (6e-3, 3, 1e3, "mF")
            };
            var list = new MeterRange[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                // Charge resistance so that a full-scale capacitor takes ~40 ms from 0.1 V to 1 V.
                double rs = Math.Max(100.0, 0.04 / (specs[i].Fs * CapLogRatio));
                list[i] = new MeterRange(specs[i].Fs, specs[i].Dec, specs[i].Scale, specs[i].Unit, rs);
            }

            return list;
        }

        public static IReadOnlyList<MeterRange> Ranges(MultimeterMode mode, bool ampJack = false)
        {
            switch (mode)
            {
                case MultimeterMode.DcCurrent:
                case MultimeterMode.AcCurrent:
                    return ampJack ? Amp : MilliAmp;
                case MultimeterMode.Resistance: return Ohm;
                case MultimeterMode.Continuity: return Continuity;
                case MultimeterMode.Diode: return DiodeRange;
                case MultimeterMode.Capacitance: return Cap;
                case MultimeterMode.Frequency: return Freq;
                default: return Volt;
            }
        }

        /// <summary>Accuracy as ±(percent of reading + digits).</summary>
        public static (double Percent, int Digits) Accuracy(MultimeterMode mode)
        {
            switch (mode)
            {
                case MultimeterMode.DcVoltage: return (0.5, 2);
                case MultimeterMode.AcVoltage: return (1.0, 3);
                case MultimeterMode.DcCurrent: return (1.0, 3);
                case MultimeterMode.AcCurrent: return (1.5, 3);
                case MultimeterMode.Resistance: return (0.9, 2);
                case MultimeterMode.Continuity: return (0.9, 2);
                case MultimeterMode.Diode: return (1.0, 2);
                case MultimeterMode.Capacitance: return (3.0, 5);
                default: return (0.1, 2);
            }
        }

        public static bool IsCurrent(MultimeterMode mode) => mode == MultimeterMode.DcCurrent || mode == MultimeterMode.AcCurrent;

        public static string ModeName(MultimeterMode mode)
        {
            switch (mode)
            {
                case MultimeterMode.DcVoltage: return "V⎓";
                case MultimeterMode.AcVoltage: return "V~";
                case MultimeterMode.DcCurrent: return "A⎓";
                case MultimeterMode.AcCurrent: return "A~";
                case MultimeterMode.Resistance: return "Ω";
                case MultimeterMode.Continuity: return "·)))";
                case MultimeterMode.Diode: return "→|";
                case MultimeterMode.Capacitance: return "⊣⊢";
                default: return "Hz %";
            }
        }

        public static string ModeKey(MultimeterMode mode)
        {
            switch (mode)
            {
                case MultimeterMode.AcVoltage: return "acv";
                case MultimeterMode.DcCurrent: return "dca";
                case MultimeterMode.AcCurrent: return "aca";
                case MultimeterMode.Resistance: return "ohm";
                case MultimeterMode.Continuity: return "cont";
                case MultimeterMode.Diode: return "diode";
                case MultimeterMode.Capacitance: return "cap";
                case MultimeterMode.Frequency: return "freq";
                default: return "dcv";
            }
        }

        public static MultimeterMode ParseMode(string? key)
        {
            foreach (MultimeterMode m in Enum.GetValues(typeof(MultimeterMode)))
            {
                if (string.Equals(ModeKey(m), key?.Trim(), StringComparison.OrdinalIgnoreCase)) return m;
            }

            return MultimeterMode.DcVoltage;
        }

        /// <summary>Formats a value the way the LCD shows it on a range.</summary>
        public static string FormatDigits(double value, MeterRange range)
        {
            double shown = value * range.DisplayScale;
            string s = shown.ToString("F" + range.Decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (s.StartsWith("-", StringComparison.Ordinal) && Math.Abs(shown) < 0.5 * Math.Pow(10, -range.Decimals)) s = s.Substring(1);
            return s;
        }
    }

    /// <summary>What the LCD shows after one measurement cycle.</summary>
    public sealed class MultimeterReading
    {
        public MultimeterMode Mode { get; set; }

        /// <summary>False until the first complete measurement (display shows dashes).</summary>
        public bool Valid { get; set; }

        public bool Overload { get; set; }

        /// <summary>Displayed value in SI units (NaN when not valid or overloaded).</summary>
        public double Value { get; set; } = double.NaN;

        /// <summary>The quantity at the meter input before calibration error and quantisation.</summary>
        public double TrueValue { get; set; } = double.NaN;

        public string Digits { get; set; } = "----";
        public string Unit { get; set; } = string.Empty;
        public string RangeLabel { get; set; } = string.Empty;
        public bool AutoRange { get; set; }
        public bool Beep { get; set; }
        public bool Hold { get; set; }

        /// <summary>Duty cycle in percent for the frequency function.</summary>
        public double Secondary { get; set; } = double.NaN;

        public string? Warning { get; set; }

        /// <summary>Number of display updates so far (increments at the update rate).</summary>
        public int Sequence { get; set; }

        public string Text => Overload ? "OL " + Unit : Digits + " " + Unit;

        public override string ToString() => Text;
    }

    /// <summary>
    /// A 6000-count true-RMS digital multimeter as a circuit element. The VΩ jack loads the
    /// circuit with 10 MΩ (or sources the Ω / diode / capacitance test signal), the mA and A
    /// jacks are shunts to COM behind real fuses, and the reading is produced at ~3 Hz from an
    /// integrating measurement over the last window, with autorange, calibration error within
    /// the accuracy specification and "OL" on overload.
    /// </summary>
    public sealed class Multimeter : AnalogDevice
    {
        private enum CapPhase
        {
            Discharge,
            Charge
        }

        private MultimeterMode _mode = MultimeterMode.DcVoltage;
        private int _vOhm, _com, _mA, _a;
        private int _rangeIndex = 1;
        private bool _auto = true;
        private bool _ampJack;

        // Measurement window (integrating converter).
        private double _sum, _sumSq, _span, _winMin = double.PositiveInfinity, _winMax = double.NegativeInfinity;
        private double _sumMa, _sumA;
        private double _prevX = double.NaN;
        private double _acState = double.NaN, _acPrevIn = double.NaN;
        private double _nextUpdate;
        private bool _settle = true;
        private int _sequence;

        // Frequency counter.
        private readonly List<double> _rises = new List<double>();
        private readonly List<double> _falls = new List<double>();
        private double _freqLevel = double.NaN, _freqHyst;
        private bool _freqHigh;
        private double _lastUpCross = double.NaN, _lastDownCross = double.NaN;

        // Fuses.
        private double _heatMa, _heatA;

        // Capacitance timing.
        private CapPhase _capPhase = CapPhase.Discharge;
        private double _capPhaseStart;
        private double _capT1 = double.NaN;
        private double _capPrevV;
        private double _capValue = double.NaN;
        private bool _capNew;
        private bool _capOver;

        // Calibration error of this particular meter (within specification).
        private readonly double[] _gainError = new double[9];
        private readonly double[] _offsetError = new double[9];
        private readonly InstrumentRandom _noise;

        public Multimeter(string id, int voltOhm, int com, int milliAmp = -1, int amp = -1) : base(id)
        {
            _vOhm = voltOhm;
            _com = com;
            _mA = milliAmp;
            _a = amp;
            var cal = new InstrumentRandom(InstrumentRandom.Hash(id, 0xD3));
            foreach (MultimeterMode m in Enum.GetValues(typeof(MultimeterMode)))
            {
                var (pct, digits) = MultimeterSpec.Accuracy(m);
                _gainError[(int)m] = pct / 100.0 * 0.6 * cal.NextSigned();
                _offsetError[(int)m] = 0.25 * digits * cal.NextSigned();
            }

            _noise = new InstrumentRandom(InstrumentRandom.Hash(id, 0x51));
            Reading = new MultimeterReading { Mode = _mode, AutoRange = true };
        }

        // ------------------------------------------------------------------ wiring

        /// <summary>Node of the VΩ jack (-1 = no lead).</summary>
        public int VoltOhmNode
        {
            get => _vOhm;
            set
            {
                if (_vOhm == value) return;
                _vOhm = value;
                Restart();
            }
        }

        /// <summary>Node of the COM jack (-1 = no lead).</summary>
        public int ComNode
        {
            get => _com;
            set
            {
                if (_com == value) return;
                _com = value;
                Restart();
            }
        }

        public int MilliAmpNode
        {
            get => _mA;
            set
            {
                if (_mA == value) return;
                _mA = value;
                Restart();
            }
        }

        public int AmpNode
        {
            get => _a;
            set
            {
                if (_a == value) return;
                _a = value;
                Restart();
            }
        }

        /// <summary>Moves the red lead to <paramref name="red"/> in <paramref name="jack"/> and the black lead to <paramref name="black"/>.</summary>
        public void Connect(int red, int black, MultimeterJack jack)
        {
            int vo = jack == MultimeterJack.VoltOhm ? red : -1;
            int ma = jack == MultimeterJack.MilliAmp ? red : -1;
            int a = jack == MultimeterJack.Amp ? red : -1;
            if (vo == _vOhm && ma == _mA && a == _a && black == _com) return;
            _vOhm = vo;
            _mA = ma;
            _a = a;
            _com = black;
            Restart();
        }

        public bool IsConnected => _com >= 0 && (_vOhm >= 0 || _mA >= 0 || _a >= 0);

        // ------------------------------------------------------------------ front panel

        public MultimeterMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                _auto = true;
                _rangeIndex = DefaultRange(value);
                ExternalVoltage = false;
                Reading = new MultimeterReading { Mode = value, AutoRange = true, Sequence = _sequence };
                Restart();
            }
        }

        public bool AutoRange => _auto;

        public int RangeIndex => _rangeIndex;

        public IReadOnlyList<MeterRange> Ranges => MultimeterSpec.Ranges(_mode, _ampJack);

        public MeterRange Range => Ranges[Math.Min(_rangeIndex, Ranges.Count - 1)];

        /// <summary>RANGE button: the first press locks the present range, further presses step up (wrapping).</summary>
        public void StepRange()
        {
            if (_auto)
            {
                _auto = false;
            }
            else
            {
                _rangeIndex = (_rangeIndex + 1) % Ranges.Count;
            }

            Restart();
        }

        /// <summary>Holding RANGE returns to autorange.</summary>
        public void SetAutoRange()
        {
            if (_auto) return;
            _auto = true;
            Restart();
        }

        public void SetRange(int index)
        {
            _auto = false;
            _rangeIndex = Math.Max(0, Math.Min(Ranges.Count - 1, index));
            Restart();
        }

        /// <summary>HOLD freezes the display.</summary>
        public bool Hold { get; set; }

        public double UpdateRate { get; set; } = MultimeterSpec.UpdateRate;

        public double InputResistance { get; set; } = MultimeterSpec.InputResistance;

        public MultimeterReading Reading { get; private set; }

        /// <summary>Ω / diode / capacitance: a voltage that is not the meter's own was found across the leads.</summary>
        public bool ExternalVoltage { get; private set; }

        public bool MilliAmpFuseBlown { get; private set; }

        public bool AmpFuseBlown { get; private set; }

        /// <summary>Instantaneous voltage across VΩ-COM after the last accepted step.</summary>
        public double LastVoltage { get; private set; }

        /// <summary>Instantaneous current entering the mA + A jacks.</summary>
        public double LastCurrent { get; private set; }

        /// <summary>The red lead is in the A jack (10 A ranges) rather than the mA jack.</summary>
        public bool UsesAmpJack => _ampJack;

        public double MilliAmpShunt =>
            MultimeterSpec.IsCurrent(_mode) && !_ampJack
                ? MultimeterSpec.Ranges(_mode, false)[Math.Min(_rangeIndex, 2)].Aux
                : MultimeterSpec.MilliAmpShuntDefault;

        /// <summary>Burden voltage of the current function at the last reading.</summary>
        public double BurdenVoltage => Math.Abs(LastCurrent) * (_ampJack ? MultimeterSpec.AmpShunt : MilliAmpShunt);

        private static int DefaultRange(MultimeterMode mode)
        {
            switch (mode)
            {
                case MultimeterMode.Resistance: return 5;
                case MultimeterMode.Capacitance: return 2;
                case MultimeterMode.DcCurrent:
                case MultimeterMode.AcCurrent: return 2;
                case MultimeterMode.Frequency: return 0;
                case MultimeterMode.Continuity:
                case MultimeterMode.Diode: return 0;
                default: return 1;
            }
        }

        private void Restart()
        {
            ClearWindow();
            _settle = true;
            _capPhase = CapPhase.Discharge;
            _capT1 = double.NaN;
            _capPhaseStart = double.NaN;
            _capNew = false;
            _capOver = false;
            _rises.Clear();
            _falls.Clear();
            _freqLevel = double.NaN;
            _prevX = double.NaN;
            _acState = double.NaN;
            PendingDiscontinuity = true;
        }

        private void ClearWindow()
        {
            _sum = _sumSq = _span = _sumMa = _sumA = 0.0;
            _winMin = double.PositiveInfinity;
            _winMax = double.NegativeInfinity;
        }

        public override void Reset()
        {
            Restart();
            _heatMa = _heatA = 0.0;
            _nextUpdate = 1.0 / UpdateRate;
            _sequence = 0;
            LastVoltage = LastCurrent = 0.0;
            Reading = new MultimeterReading { Mode = _mode, AutoRange = _auto };
        }

        /// <summary>Fuses replaced.</summary>
        public override void Repair()
        {
            base.Repair();
            MilliAmpFuseBlown = AmpFuseBlown = false;
            _heatMa = _heatA = 0.0;
            Restart();
        }

        // ------------------------------------------------------------------ circuit

        public override void Stamp(StampContext ctx)
        {
            if (_com < 0) return;
            if (_vOhm >= 0)
            {
                switch (_mode)
                {
                    case MultimeterMode.Resistance:
                    case MultimeterMode.Continuity:
                        StampSource(ctx, MultimeterSpec.OhmOpenVoltage, Range.Aux);
                        break;
                    case MultimeterMode.Diode:
                        StampSource(ctx, MultimeterSpec.DiodeOpenVoltage, MultimeterSpec.DiodeSourceResistance);
                        break;
                    case MultimeterMode.Capacitance:
                        if (_capPhase == CapPhase.Discharge) ctx.AddConductance(_vOhm, _com, 1.0 / MultimeterSpec.CapDischargeResistance);
                        else StampSource(ctx, MultimeterSpec.CapOpenVoltage, Range.Aux);
                        break;
                    default:
                        ctx.AddConductance(_vOhm, _com, 1.0 / InputResistance);
                        break;
                }
            }

            if (_mA >= 0 && !MilliAmpFuseBlown) ctx.AddConductance(_mA, _com, 1.0 / MilliAmpShunt);
            if (_a >= 0 && !AmpFuseBlown) ctx.AddConductance(_a, _com, 1.0 / MultimeterSpec.AmpShunt);
        }

        private void StampSource(StampContext ctx, double openVoltage, double resistance)
        {
            ctx.AddConductance(_vOhm, _com, 1.0 / resistance);
            ctx.AddCurrent(_com, _vOhm, openVoltage / resistance);
        }

        public override void Accept(AcceptContext ctx)
        {
            if (_com < 0)
            {
                LastVoltage = LastCurrent = 0.0;
                if (!ctx.IsDc && ctx.Time >= _nextUpdate)
                {
                    _nextUpdate = ctx.Time + 1.0 / UpdateRate;
                    _sequence++;
                    Reading = new MultimeterReading { Mode = _mode, AutoRange = _auto, Sequence = _sequence, Warning = "Siyah prob (COM) bağlı değil" };
                }

                return;
            }

            double v = _vOhm >= 0 ? ctx.V(_vOhm, _com) : 0.0;
            double iMa = _mA >= 0 && !MilliAmpFuseBlown ? ctx.V(_mA, _com) / MilliAmpShunt : 0.0;
            double iA = _a >= 0 && !AmpFuseBlown ? ctx.V(_a, _com) / MultimeterSpec.AmpShunt : 0.0;
            LastVoltage = v;
            LastCurrent = iMa + iA;
            if (ctx.IsDc) return;

            double t = ctx.Time, dt = ctx.Dt;
            UpdateFuse(ctx, ref _heatMa, iMa, MultimeterSpec.MilliAmpFuse, 0.05, true);
            UpdateFuse(ctx, ref _heatA, iA, MultimeterSpec.AmpFuse, 0.2, false);

            if (_mode == MultimeterMode.Capacitance)
            {
                AdvanceCapacitance(ctx, v);
            }
            else
            {
                double x = MultimeterSpec.IsCurrent(_mode) ? iMa + iA : v;
                if (_mode == MultimeterMode.AcVoltage || _mode == MultimeterMode.AcCurrent) x = AcFilter(x, dt);
                Integrate(x, dt);
                _sumMa += iMa * dt;
                _sumA += iA * dt;
                if (_mode == MultimeterMode.Frequency) CountEdges(t, dt, x);
                _prevX = x;
            }

            if (t >= _nextUpdate)
            {
                while (_nextUpdate <= t) _nextUpdate += 1.0 / UpdateRate;
                UpdateDisplay(t);
            }
        }

        private void UpdateFuse(AcceptContext ctx, ref double heat, double current, double rating, double tau, bool milliAmp)
        {
            if (milliAmp ? MilliAmpFuseBlown : AmpFuseBlown) return;
            double target = current * current * tau;
            heat = target + (heat - target) * Math.Exp(-ctx.Dt / tau);
            double limit = 1.35 * rating;
            if (!ctx.EnableDamage || heat < limit * limit * tau) return;
            if (milliAmp) MilliAmpFuseBlown = true;
            else AmpFuseBlown = true;
            ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.Failure,
                I($"{(milliAmp ? "mA" : "A")} girişinin sigortası attı: {Math.Abs(current):F2} A aktı (sigorta F {rating:G3} A). ") +
                (MultimeterSpec.IsCurrent(_mode) ? "Ölçülen akım aralığı aşıyor." : "Kırmızı prob akım girişindeyken gerilim ölçülmeye çalışıldı: kaynak kısa devre oldu.")));
            ctx.RequestDiscontinuity();
        }

        private double AcFilter(double x, double dt)
        {
            if (double.IsNaN(_acState))
            {
                _acState = x;
                _acPrevIn = x;
                return x;
            }

            double tau = 1.0 / (2.0 * Math.PI * MultimeterSpec.AcBandwidth);
            double slope = (x - _acPrevIn) / dt;
            _acState = x - slope * tau + (_acState - _acPrevIn + slope * tau) * Math.Exp(-dt / tau);
            _acPrevIn = x;
            return _acState;
        }

        private void Integrate(double x, double dt)
        {
            double xp = double.IsNaN(_prevX) ? x : _prevX;
            _sum += 0.5 * (xp + x) * dt;
            _sumSq += 0.5 * (xp * xp + x * x) * dt;
            _span += dt;
            if (x < _winMin) _winMin = x;
            if (x > _winMax) _winMax = x;
        }

        private void CountEdges(double t, double dt, double x)
        {
            if (double.IsNaN(_freqLevel) || double.IsNaN(_prevX)) return;
            double xp = _prevX, level = _freqLevel;
            if (xp < level && x >= level) _lastUpCross = t - dt + (level - xp) / (x - xp) * dt;
            if (xp > level && x <= level) _lastDownCross = t - dt + (xp - level) / (xp - x) * dt;
            if (!_freqHigh && x > level + _freqHyst)
            {
                _freqHigh = true;
                _rises.Add(double.IsNaN(_lastUpCross) ? t : _lastUpCross);
            }
            else if (_freqHigh && x < level - _freqHyst)
            {
                _freqHigh = false;
                _falls.Add(double.IsNaN(_lastDownCross) ? t : _lastDownCross);
            }

            const double gate = 1.0;
            if (_rises.Count > 4000) _rises.RemoveRange(0, 2000);
            if (_falls.Count > 4000) _falls.RemoveRange(0, 2000);
            while (_rises.Count > 0 && _rises[0] < t - gate) _rises.RemoveAt(0);
            while (_falls.Count > 0 && _falls[0] < t - gate) _falls.RemoveAt(0);
        }

        private void AdvanceCapacitance(AcceptContext ctx, double v)
        {
            double t = ctx.Time;
            if (double.IsNaN(_capPhaseStart)) _capPhaseStart = t;
            double rs = Range.Aux;
            if (_capPhase == CapPhase.Discharge)
            {
                if (t - _capPhaseStart > 1e-3 && Math.Abs(v) < 2e-3)
                {
                    _capPhase = CapPhase.Charge;
                    _capPhaseStart = t;
                    _capT1 = double.NaN;
                    ExternalVoltage = false;
                    ctx.RequestDiscontinuity();
                }
                else if (t - _capPhaseStart > 2.0)
                {
                    // Cannot discharge the part: something keeps a voltage across it.
                    ExternalVoltage = true;
                    _capNew = true;
                    _capOver = true;
                    _capPhaseStart = t;
                }

                _capPrevV = v;
                return;
            }

            double vp = _capPrevV, dt = ctx.Dt;
            double low = MultimeterSpec.CapLow, high = MultimeterSpec.CapHigh;
            if (double.IsNaN(_capT1) && vp < low && v >= low) _capT1 = t - dt + (low - vp) / (v - vp) * dt;
            if (vp < high && v >= high)
            {
                double t2 = t - dt + (high - vp) / (v - vp) * dt;
                if (double.IsNaN(_capT1)) _capT1 = t2;
                _capValue = Math.Max(0.0, (t2 - _capT1) / (rs * MultimeterSpec.CapLogRatio));
                _capNew = true;
                _capOver = false;
                BeginDischarge(ctx, t);
            }
            else if (t - _capPhaseStart > 12.0 * rs * Range.FullScale * MultimeterSpec.CapLogRatio + 5e-3)
            {
                _capNew = true;
                _capOver = true;
                BeginDischarge(ctx, t);
            }

            _capPrevV = v;
        }

        private void BeginDischarge(AcceptContext ctx, double t)
        {
            _capPhase = CapPhase.Discharge;
            _capPhaseStart = t;
            ctx.RequestDiscontinuity();
        }

        // ------------------------------------------------------------------ display

        private void UpdateDisplay(double t)
        {
            _sequence++;
            if (Hold)
            {
                ClearWindow();
                Reading.Hold = true;
                Reading.Sequence = _sequence;
                return;
            }

            if (_span > 0) _ampJack = MultimeterSpec.IsCurrent(_mode) && _a >= 0 && (_mA < 0 || Math.Abs(_sumA) > Math.Abs(_sumMa));
            if (_rangeIndex >= Ranges.Count) _rangeIndex = Ranges.Count - 1;

            if (_settle && _mode != MultimeterMode.Capacitance)
            {
                // First window after a change of function, range or lead: the input is still settling.
                _settle = false;
                SetupFrequencyLevel();
                ClearWindow();
                Reading.Sequence = _sequence;
                return;
            }

            _settle = false;
            double value;
            string? warning = null;
            bool beep = false;
            double secondary = double.NaN;
            double mean = _span > 0 ? _sum / _span : double.NaN;

            switch (_mode)
            {
                case MultimeterMode.DcVoltage:
                case MultimeterMode.DcCurrent:
                    value = mean;
                    break;
                case MultimeterMode.AcVoltage:
                case MultimeterMode.AcCurrent:
                    value = _span > 0 ? Math.Sqrt(Math.Max(0.0, _sumSq / _span - mean * mean)) : double.NaN;
                    break;
                case MultimeterMode.Resistance:
                case MultimeterMode.Continuity:
                {
                    double voc = MultimeterSpec.OhmOpenVoltage, rs = Range.Aux;
                    ExternalVoltage = mean > voc * 1.01 + 0.02 || mean < -0.02;
                    value = ExternalVoltage || mean >= voc * 0.9995 ? double.PositiveInfinity : rs * Math.Max(0.0, mean) / (voc - mean);
                    if (ExternalVoltage) warning = "Devrede gerilim var! Ω ölçümü yalnızca enerjisiz devrede geçerlidir.";
                    beep = _mode == MultimeterMode.Continuity && !ExternalVoltage && value < MultimeterSpec.ContinuityThreshold;
                    break;
                }

                case MultimeterMode.Diode:
                    ExternalVoltage = mean > MultimeterSpec.DiodeOpenVoltage * 1.01 + 0.02 || mean < -0.05;
                    value = ExternalVoltage ? double.PositiveInfinity : mean;
                    if (ExternalVoltage) warning = "Devrede gerilim var! Diyot testi enerjisiz devrede yapılır.";
                    beep = !ExternalVoltage && mean < 0.05;
                    break;
                case MultimeterMode.Capacitance:
                    if (!_capNew)
                    {
                        Reading.Sequence = _sequence;
                        return;
                    }

                    _capNew = false;
                    value = _capOver ? double.PositiveInfinity : _capValue;
                    if (ExternalVoltage) warning = "Kondansatör boşaltılamıyor: devrede gerilim var.";
                    break;
                default:
                    value = Frequency(out secondary);
                    break;
            }

            // Autorange.
            var ranges = Ranges;
            if (_auto)
            {
                int chosen = ChooseRange(ranges, value);
                if (chosen != _rangeIndex)
                {
                    _rangeIndex = chosen;
                    if (ChangesCircuit())
                    {
                        Restart();
                        Reading.Sequence = _sequence;
                        return;
                    }
                }
            }

            Reading = MakeReading(value, ranges[_rangeIndex], warning, beep, secondary);
            SetupFrequencyLevel();
            ClearWindow();
        }

        /// <summary>True for functions whose range changes the test source or shunt (the circuit sees the change).</summary>
        private bool ChangesCircuit() =>
            _mode == MultimeterMode.Resistance || _mode == MultimeterMode.Capacitance ||
            (MultimeterSpec.IsCurrent(_mode) && !_ampJack);

        private int ChooseRange(IReadOnlyList<MeterRange> ranges, double value)
        {
            int current = Math.Min(_rangeIndex, ranges.Count - 1);
            if (double.IsNaN(value)) return current;
            double a = Math.Abs(value);
            if (double.IsInfinity(a)) return Math.Min(ranges.Count - 1, current + 1);
            bool fits = a <= ranges[current].FullScale;
            bool tooBig = current > 0 && a < ranges[current - 1].FullScale * 0.9;
            if (fits && !tooBig) return current;
            for (int i = 0; i < ranges.Count; i++)
            {
                if (a <= ranges[i].FullScale * 0.95) return i;
            }

            return ranges.Count - 1;
        }

        private void SetupFrequencyLevel()
        {
            if (_mode != MultimeterMode.Frequency) return;
            if (double.IsInfinity(_winMin) || _winMax - _winMin < 0.02)
            {
                _freqLevel = double.NaN;
                return;
            }

            _freqLevel = 0.5 * (_winMin + _winMax);
            _freqHyst = Math.Max(0.005, 0.1 * (_winMax - _winMin));
        }

        private double Frequency(out double dutyPercent)
        {
            dutyPercent = double.NaN;
            if (_rises.Count < 2) return 0.0;
            double first = _rises[0], last = _rises[_rises.Count - 1];
            double period = (last - first) / (_rises.Count - 1);
            if (!(period > 0)) return 0.0;

            double high = 0.0, total = 0.0;
            for (int i = 0; i + 1 < _rises.Count; i++)
            {
                double r = _rises[i], next = _rises[i + 1];
                foreach (double f in _falls)
                {
                    if (f > r && f < next)
                    {
                        high += f - r;
                        total += next - r;
                        break;
                    }
                }
            }

            if (total > 0) dutyPercent = 100.0 * high / total;
            return 1.0 / period;
        }

        private MultimeterReading MakeReading(double value, MeterRange range, string? warning, bool beep, double secondary)
        {
            var reading = new MultimeterReading
            {
                Mode = _mode,
                Valid = true,
                TrueValue = value,
                Unit = range.Unit,
                RangeLabel = range.Label,
                AutoRange = _auto,
                Beep = beep,
                Secondary = secondary,
                Warning = warning,
                Sequence = _sequence
            };

            if (MilliAmpFuseBlown && MultimeterSpec.IsCurrent(_mode) && !_ampJack) reading.Warning ??= "mA sigortası atık: sigortayı değiştirin (parçayı onarın).";
            if (AmpFuseBlown && MultimeterSpec.IsCurrent(_mode) && _ampJack) reading.Warning ??= "A sigortası atık: sigortayı değiştirin (parçayı onarın).";

            bool overload = double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > range.FullScale ||
                            (_mode == MultimeterMode.Diode && value > MultimeterSpec.DiodeOverload);
            if (overload)
            {
                reading.Overload = true;
                reading.Digits = "OL";
                return reading;
            }

            var (pct, digits) = MultimeterSpec.Accuracy(_mode);
            double res = range.Resolution;
            double noise = 0.25 * digits * _noise.NextSigned();
            double calibrated = value * (1.0 + _gainError[(int)_mode]) + (_offsetError[(int)_mode] + noise) * res;
            if (_mode == MultimeterMode.Capacitance || _mode == MultimeterMode.Frequency || _mode == MultimeterMode.Resistance ||
                _mode == MultimeterMode.Continuity || _mode == MultimeterMode.AcVoltage || _mode == MultimeterMode.AcCurrent)
            {
                calibrated = Math.Max(0.0, calibrated);
            }

            double counts = Math.Round(calibrated / res);
            double shown = counts * res;
            reading.Value = shown;
            reading.Digits = MultimeterSpec.FormatDigits(shown, range);
            _ = pct;
            return reading;
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", LastVoltage);
            sink.Write(Id, "I", LastCurrent);
            sink.Write(Id, "READING", Reading.Valid && !Reading.Overload ? Reading.Value : double.NaN);
        }

        /// <summary>True when an independent source in the circuit is producing voltage or current (Ω readings are then invalid).</summary>
        public static bool CircuitIsEnergized(AnalogCircuit circuit)
        {
            foreach (var device in circuit.Devices)
            {
                switch (device)
                {
                    case Battery b when !b.IsFailed && b.OpenCircuitVoltage > 0.05:
                        return true;
                    case BenchSupply psu when psu.OutputEnabled && psu.Voltage > 0.05:
                        return true;
                    case Ammeter _:
                        continue;
                    case VoltageSource vs when !(vs is Battery) && Math.Abs(vs.Waveform.Value(0.0)) > 0.05:
                        return true;
                    case CurrentSource cs when Math.Abs(cs.Waveform.Value(0.0)) > 1e-9:
                        return true;
                }
            }

            return false;
        }
    }
}
