using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// Resistor with temperature coefficient, self-heating and burn-out when the
    /// dissipation stays above the power rating.
    /// </summary>
    public class Resistor : AnalogDevice
    {
        private const double OpenResistance = 1e12;
        private double _effective;

        public Resistor(string id, int a, int b, double resistance) : base(id)
        {
            A = a;
            B = b;
            Resistance = resistance;
            _effective = Math.Max(resistance, MinResistance);
        }

        public const double MinResistance = 1e-6;

        public int A { get; }
        public int B { get; }

        /// <summary>Nominal resistance at 25 C, in ohms.</summary>
        public double Resistance { get; set; }

        /// <summary>Fractional change per kelvin (100 ppm/K = 1e-4).</summary>
        public double TempCoefficient { get; set; }

        /// <summary>Rated continuous power in watts; 0 disables self-heating and burn-out.</summary>
        public double PowerRating { get; set; }

        /// <summary>Body temperature at which the part burns open.</summary>
        public double MaxTempC { get; set; } = 250.0;

        public ThermalState? Thermal { get; private set; }

        public double Voltage { get; private set; }
        public double Current { get; private set; }
        public double Power { get; private set; }
        public double EffectiveResistance => _effective;

        public override void Setup(AnalogCircuit circuit)
        {
            if (PowerRating > 0.0 && Thermal == null)
            {
                // A part at rated power sits roughly 85 K above ambient; time constant of a few seconds.
                double rth = 85.0 / PowerRating;
                Thermal = new ThermalState(rth, 4.0 / rth);
            }
        }

        public override void Reset()
        {
            Thermal?.Reset(25.0);
            Voltage = Current = Power = 0.0;
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            Thermal?.Reset(25.0);
        }

        public override void BeginStep(StepInfo step)
        {
            if (Failure == FailureMode.Open)
            {
                _effective = OpenResistance;
                return;
            }

            double r = NominalResistance();
            if (Thermal != null && TempCoefficient != 0.0)
            {
                r *= 1.0 + TempCoefficient * (Thermal.TemperatureC - 25.0);
            }

            _effective = Math.Max(r, MinResistance);
        }

        /// <summary>Resistance before temperature effects; overridden by sensor-type resistors.</summary>
        protected virtual double NominalResistance() => Resistance;

        public override void Stamp(StampContext ctx) => ctx.AddConductance(A, B, 1.0 / _effective);

        public override void Accept(AcceptContext ctx)
        {
            Voltage = ctx.V(A, B);
            Current = Voltage / _effective;
            Power = Voltage * Current;
            if (Thermal == null || ctx.IsDc) return;
            Thermal.Advance(Power, ctx.Dt, ctx.AmbientTempC);
            if (ctx.EnableDamage && !IsFailed && Thermal.TemperatureC >= MaxTempC)
            {
                Fail(FailureMode.Open, $"{Thermal.TemperatureC:F0} °C'de yandı ({PowerRating:G3} W'lık parçada {Power:F2} W)", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "P", Power);
            sink.Write(Id, "R", _effective);
            if (Thermal != null) sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>
    /// Capacitor with optional series resistance, leakage, voltage rating and
    /// polarity (electrolytics fail when reverse-biased or over-volted).
    /// </summary>
    public sealed class Capacitor : AnalogDevice
    {
        private int _plate;
        private double _geq;
        private double _ieq;
        private double _vPrev;
        private double _iPrev;

        public Capacitor(string id, int a, int b, double capacitance) : base(id)
        {
            A = a;
            B = b;
            Capacitance = capacitance;
        }

        /// <summary>Positive terminal for polarised parts.</summary>
        public int A { get; }
        public int B { get; }

        public double Capacitance { get; set; }

        /// <summary>Equivalent series resistance in ohms (0 = ideal).</summary>
        public double SeriesResistance { get; set; }

        /// <summary>Parallel leakage resistance in ohms (0 = none).</summary>
        public double LeakageResistance { get; set; }

        /// <summary>Voltage across the plates at t = 0.</summary>
        public double InitialVoltage { get; set; }

        /// <summary>Maximum working voltage; 0 disables the check.</summary>
        public double VoltageRating { get; set; }

        public bool Polarized { get; set; }

        public double Voltage { get; private set; }
        public double Current { get; private set; }

        /// <summary>Energy stored in the dielectric, joules.</summary>
        public double Energy => 0.5 * Capacitance * _vPrev * _vPrev;

        public override void Setup(AnalogCircuit circuit)
        {
            _plate = SeriesResistance > 0.0 ? circuit.InternalNode(Id, "esr") : A;
        }

        public override void Reset()
        {
            _vPrev = InitialVoltage;
            _iPrev = 0.0;
            Voltage = InitialVoltage;
            Current = 0.0;
        }

        public override void BeginStep(StepInfo step)
        {
            if (step.IsDc || IsFailed)
            {
                _geq = 0.0;
                _ieq = 0.0;
                return;
            }

            if (step.Trapezoidal)
            {
                _geq = 2.0 * Capacitance / step.Dt;
                _ieq = -_geq * _vPrev - _iPrev;
            }
            else
            {
                _geq = Capacitance / step.Dt;
                _ieq = -_geq * _vPrev;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            if (SeriesResistance > 0.0) ctx.AddConductance(A, _plate, 1.0 / SeriesResistance);

            if (Failure == FailureMode.Short)
            {
                ctx.AddConductance(_plate, B, 1.0 / 0.05);
                return;
            }

            if (LeakageResistance > 0.0) ctx.AddConductance(_plate, B, 1.0 / LeakageResistance);
            if (Failure == FailureMode.Open) return;

            if (_geq > 0.0)
            {
                ctx.AddConductance(_plate, B, _geq);
                ctx.AddCurrent(_plate, B, _ieq);
            }
        }

        public override void Accept(AcceptContext ctx)
        {
            double v = ctx.V(_plate, B);
            if (IsFailed)
            {
                Voltage = v;
                Current = Failure == FailureMode.Short ? v / 0.05 : 0.0;
                _vPrev = v;
                _iPrev = 0.0;
                return;
            }

            double i = ctx.IsDc ? 0.0 : _geq * v + _ieq;
            _vPrev = v;
            _iPrev = i;
            Voltage = v;
            Current = i;

            if (!ctx.EnableDamage || ctx.IsDc) return;
            if (VoltageRating > 0.0 && Math.Abs(v) > VoltageRating * 1.3)
            {
                Fail(FailureMode.Short, $"dielektrik delindi, kondansatör patladı: {v:F1} V (anma {VoltageRating:G3} V)", ctx);
            }
            else if (Polarized && v < -1.5)
            {
                Fail(FailureMode.Short, $"ters gerilim {v:F1} V: elektrolitik kondansatör patladı", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "E", Energy);
        }
    }

    /// <summary>Inductor with series (winding) resistance and optional saturation current check.</summary>
    public sealed class Inductor : AnalogDevice
    {
        private double _iPrev;
        private double _vPrev;
        private double _req;
        private double _veq;

        public Inductor(string id, int a, int b, double inductance) : base(id)
        {
            A = a;
            B = b;
            Inductance = inductance;
        }

        public int A { get; }
        public int B { get; }
        public double Inductance { get; set; }

        /// <summary>Winding resistance; a small non-zero value keeps inductor loops solvable.</summary>
        public double SeriesResistance { get; set; } = 1e-3;

        public double InitialCurrent { get; set; }

        /// <summary>Current flowing from A to B.</summary>
        public double Current { get; private set; }
        public double Voltage { get; private set; }
        public double Energy => 0.5 * Inductance * _iPrev * _iPrev;

        public override int BranchCount => 1;

        public override void Reset()
        {
            _iPrev = InitialCurrent;
            _vPrev = 0.0;
            Current = InitialCurrent;
            Voltage = 0.0;
        }

        public override void BeginStep(StepInfo step)
        {
            if (step.IsDc)
            {
                _req = 0.0;
                _veq = 0.0;
            }
            else if (step.Trapezoidal)
            {
                _req = 2.0 * Inductance / step.Dt;
                _veq = -_req * _iPrev - _vPrev;
            }
            else
            {
                _req = Inductance / step.Dt;
                _veq = -_req * _iPrev;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            int k = BranchBase;
            ctx.AddBranchCurrent(A, B, k);

            // V(A) - V(B) - (Rs + Req) * i = Veq
            ctx.AddRowNode(k, A, 1.0);
            ctx.AddRowNode(k, B, -1.0);
            ctx.AddMatrix(k, k, -(SeriesResistance + _req));
            ctx.AddRhs(k, _veq);
        }

        public override void Accept(AcceptContext ctx)
        {
            double i = ctx.Unknown(BranchBase);
            double terminal = ctx.V(A, B);
            double vInductance = terminal - SeriesResistance * i;
            _iPrev = i;
            _vPrev = ctx.IsDc ? 0.0 : vInductance;
            Current = i;
            Voltage = terminal;
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "E", Energy);
        }
    }

    /// <summary>Two-terminal contact: push button, toggle switch, relay contact.</summary>
    public sealed class Switch : AnalogDevice
    {
        private bool _closed;

        public Switch(string id, int a, int b, bool closed = false) : base(id)
        {
            A = a;
            B = b;
            _closed = closed;
        }

        public int A { get; }
        public int B { get; }
        public double OnResistance { get; set; } = 0.05;
        public double OffResistance { get; set; } = 1e10;

        public bool Closed
        {
            get => _closed;
            set
            {
                if (_closed == value) return;
                _closed = value;
                PendingDiscontinuity = true;
            }
        }

        public double Voltage { get; private set; }
        public double Current { get; private set; }

        public override void Stamp(StampContext ctx) =>
            ctx.AddConductance(A, B, 1.0 / (_closed ? OnResistance : OffResistance));

        public override void Accept(AcceptContext ctx)
        {
            Voltage = ctx.V(A, B);
            Current = Voltage / (_closed ? OnResistance : OffResistance);
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "CLOSED", _closed ? 1.0 : 0.0);
        }
    }

    public enum PotTaper
    {
        Linear,
        Logarithmic
    }

    /// <summary>Three-terminal potentiometer: A - wiper - B.</summary>
    public sealed class Potentiometer : AnalogDevice
    {
        private double _position = 0.5;

        public Potentiometer(string id, int a, int wiper, int b, double resistance) : base(id)
        {
            A = a;
            Wiper = wiper;
            B = b;
            Resistance = resistance;
        }

        public int A { get; }
        public int Wiper { get; }
        public int B { get; }
        public double Resistance { get; set; }
        public double WiperResistance { get; set; } = 1.0;
        public PotTaper Taper { get; set; } = PotTaper.Linear;

        /// <summary>Shaft position: 0 puts the wiper at A, 1 at B.</summary>
        public double Position
        {
            get => _position;
            set => _position = AnalogMath.Clamp(value, 0.0, 1.0);
        }

        public double WiperVoltage { get; private set; }

        private double Fraction()
        {
            if (Taper == PotTaper.Linear) return _position;
            // Audio taper: about 10 % of the resistance at mid rotation.
            return (Math.Pow(81.0, _position) - 1.0) / 80.0;
        }

        public override void Stamp(StampContext ctx)
        {
            double f = Fraction();
            double ra = Math.Max(Resistance * f, 0.0) + WiperResistance;
            double rb = Math.Max(Resistance * (1.0 - f), 0.0) + WiperResistance;
            ctx.AddConductance(A, Wiper, 1.0 / ra);
            ctx.AddConductance(Wiper, B, 1.0 / rb);
        }

        public override void Accept(AcceptContext ctx) => WiperVoltage = ctx.V(Wiper);

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", WiperVoltage);
            sink.Write(Id, "POS", _position);
        }
    }

    /// <summary>Fuse with an I-squared-t heating model; blows open and stays open.</summary>
    public sealed class Fuse : AnalogDevice
    {
        private double _heat;

        public Fuse(string id, int a, int b, double ratedCurrent) : base(id)
        {
            A = a;
            B = b;
            RatedCurrent = ratedCurrent;
        }

        public int A { get; }
        public int B { get; }
        public double RatedCurrent { get; set; }
        public double ColdResistance { get; set; } = 0.05;

        /// <summary>Thermal time constant: about 0.1 s for fast-acting, 10 s for slow-blow.</summary>
        public double TimeConstant { get; set; } = 0.1;

        public double Current { get; private set; }

        public override void Reset()
        {
            _heat = 0.0;
            Current = 0.0;
        }

        /// <summary>A new fuse cartridge.</summary>
        public override void Repair()
        {
            base.Repair();
            _heat = 0.0;
        }

        public override void Stamp(StampContext ctx) =>
            ctx.AddConductance(A, B, IsFailed ? 1e-12 : 1.0 / ColdResistance);

        public override void Accept(AcceptContext ctx)
        {
            Current = IsFailed ? 0.0 : ctx.V(A, B) / ColdResistance;
            if (ctx.IsDc || IsFailed) return;

            // heat' = I^2 - heat / tau; the element melts at the heat of 1.35 x rated current.
            double target = Current * Current * TimeConstant;
            _heat = target + (_heat - target) * Math.Exp(-ctx.Dt / TimeConstant);
            double limit = 1.35 * RatedCurrent;
            if (ctx.EnableDamage && _heat >= limit * limit * TimeConstant)
            {
                Fail(FailureMode.Open, $"{Math.Abs(Current):F2} A ile attı (anma {RatedCurrent:G3} A)", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "I", Current);
            sink.Write(Id, "BLOWN", IsFailed ? 1.0 : 0.0);
        }
    }

    /// <summary>NTC thermistor following the Beta equation; sensed temperature is set by the host.</summary>
    public sealed class Thermistor : Resistor
    {
        public Thermistor(string id, int a, int b, double resistanceAt25, double beta = 3950.0)
            : base(id, a, b, resistanceAt25)
        {
            Beta = beta;
        }

        public double Beta { get; set; }

        /// <summary>Temperature of the sensed medium in Celsius.</summary>
        public double SensedTempC { get; set; } = 25.0;

        protected override double NominalResistance()
        {
            double t = SensedTempC + AnalogMath.ZeroCelsius;
            double t0 = 25.0 + AnalogMath.ZeroCelsius;
            return Resistance * Math.Exp(Beta * (1.0 / t - 1.0 / t0));
        }
    }

    /// <summary>Light-dependent resistor: R = R10 * (lux / 10)^-gamma.</summary>
    public sealed class Photoresistor : Resistor
    {
        public Photoresistor(string id, int a, int b, double resistanceAt10Lux, double gamma = 0.7)
            : base(id, a, b, resistanceAt10Lux)
        {
            Gamma = gamma;
        }

        public double Gamma { get; set; }
        public double DarkResistance { get; set; } = 1e6;

        /// <summary>Illuminance on the sensor in lux.</summary>
        public double Lux { get; set; } = 100.0;

        protected override double NominalResistance()
        {
            if (Lux <= 0.01) return DarkResistance;
            return Math.Min(DarkResistance, Resistance * Math.Pow(Lux / 10.0, -Gamma));
        }
    }
}
