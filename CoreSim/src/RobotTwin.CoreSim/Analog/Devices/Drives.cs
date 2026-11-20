using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// Averaged model of a PWM switching stage (half bridge / low-side MOSFET with
    /// freewheeling): the output is duty × V(in) behind the switch resistance and the
    /// input draws duty × I(out), so power is conserved exactly. Used for motor
    /// drivers and the MOSFETs on a flight controller, where switching at tens of kHz
    /// would make every solver step tiny without changing what the motor sees.
    /// </summary>
    public sealed class AveragedPwmDriver : AnalogDevice
    {
        private double _duty;

        public AveragedPwmDriver(string id, int input, int output, int ground) : base(id)
        {
            Input = input;
            Output = output;
            Ground = ground;
        }

        public int Input { get; }
        public int Output { get; }
        public int Ground { get; }

        /// <summary>On-resistance of the switch plus wiring.</summary>
        public double Resistance { get; set; } = 0.05;

        /// <summary>Continuous current rating; far above it the switch fails short.</summary>
        public double CurrentRating { get; set; } = 5.0;

        public double ThermalResistance { get; set; } = 80.0;
        public ThermalState Thermal { get; } = new ThermalState(80.0, 0.05);

        public double Duty
        {
            get => _duty;
            set
            {
                double v = AnalogMath.Clamp(value, 0.0, 1.0);
                if (Math.Abs(v - _duty) > 1e-9) PendingDiscontinuity = Math.Abs(v - _duty) > 0.2;
                _duty = v;
            }
        }

        public double OutputCurrent { get; private set; }
        public double OutputVoltage { get; private set; }
        public double Power { get; private set; }

        /// <summary>Optional logic input: the duty then follows its filtered level (0 V → 0, LogicHigh → 1).</summary>
        public int ControlNode { get; set; } = -1;
        public double LogicHigh { get; set; } = 5.0;
        public double ControlTimeConstant { get; set; } = 2e-3;

        public override int BranchCount => 1;

        public override void Reset()
        {
            Thermal.Reset(25.0);
            OutputCurrent = OutputVoltage = Power = 0.0;
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            Thermal.Reset(25.0);
        }

        public override void Stamp(StampContext ctx)
        {
            int k = BranchBase;
            double d = Failure == FailureMode.Short ? 1.0 : Failure == FailureMode.Open ? 0.0 : _duty;
            if (Failure == FailureMode.Open)
            {
                ctx.AddRowNode(k, Output, 0.0);
                ctx.AddMatrix(k, k, 1.0); // no current
                return;
            }

            // KCL: duty·i leaves IN, (1−duty)·i leaves GND, i enters OUT.
            if (Input > 0) ctx.AddMatrix(Input - 1, k, d);
            if (Ground > 0) ctx.AddMatrix(Ground - 1, k, 1.0 - d);
            if (Output > 0) ctx.AddMatrix(Output - 1, k, -1.0);

            // V(out) − d·V(in) − (1−d)·V(gnd) − R·i = 0
            ctx.AddRowNode(k, Output, 1.0);
            ctx.AddRowNode(k, Input, -d);
            ctx.AddRowNode(k, Ground, -(1.0 - d));
            ctx.AddMatrix(k, k, -Resistance);
        }

        public override void Accept(AcceptContext ctx)
        {
            OutputCurrent = ctx.Unknown(BranchBase);
            OutputVoltage = ctx.V(Output, Ground);
            if (ControlNode >= 0 && !ctx.IsDc)
            {
                double target = AnalogMath.Clamp(ctx.V(ControlNode, Ground) / LogicHigh, 0.0, 1.0);
                double a = 1.0 - Math.Exp(-ctx.Dt / ControlTimeConstant);
                Duty = _duty + (target - _duty) * a;
            }
            // Conduction loss in the switch plus a switching-loss term that grows with current.
            Power = OutputCurrent * OutputCurrent * Resistance + 0.02 * Math.Abs(OutputCurrent);
            if (ctx.IsDc) return;
            Thermal.ThermalResistance = ThermalResistance;
            Thermal.Advance(Power, ctx.Dt, ctx.AmbientTempC);
            if (!ctx.EnableDamage || IsFailed) return;
            if (Thermal.TemperatureC > 160.0)
            {
                Fail(FailureMode.Short, $"MOSFET aşırı ısındı ({Thermal.TemperatureC:F0} °C) ve kısa devre oldu", ctx);
            }
            else if (Math.Abs(OutputCurrent) > CurrentRating * 4.0)
            {
                Fail(FailureMode.Short, $"{Math.Abs(OutputCurrent):F1} A ile sürücü yandı (sınır {CurrentRating:G3} A)", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "DUTY", _duty);
            sink.Write(Id, "I", OutputCurrent);
            sink.Write(Id, "V", OutputVoltage);
            sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>
    /// HC-SR04 ultrasonic ranger: a ≥10 µs pulse on TRIG starts a measurement;
    /// about 450 µs later ECHO goes high for 2·d / 343 m/s (38 ms when nothing echoes).
    /// The distance comes from the world simulation.
    /// </summary>
    public sealed class UltrasonicSensor : AnalogDevice
    {
        private const double SpeedOfSound = 343.0;
        private bool _trigHigh;
        private double _trigRise = -1;
        private double _echoStart = -1, _echoEnd = -1;
        private bool _echoHigh;

        public UltrasonicSensor(string id, int vcc, int trig, int echo, int ground) : base(id)
        {
            Vcc = vcc;
            Trig = trig;
            Echo = echo;
            Ground = ground;
        }

        public int Vcc { get; }
        public int Trig { get; }
        public int Echo { get; }
        public int Ground { get; }

        /// <summary>Distance to the nearest reflector in metres (set by the world).</summary>
        public double Distance { get; set; } = 4.5;

        public double MaxRange { get; set; } = 4.0;
        public double MinRange { get; set; } = 0.02;
        public bool EchoHigh => _echoHigh;
        public double LastPulseWidth { get; private set; }

        public override void Reset()
        {
            _trigHigh = _echoHigh = false;
            _trigRise = _echoStart = _echoEnd = -1;
        }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Vcc, Ground, 0.015 / 5.0);
            ctx.AddConductance(Trig, Ground, 1e-5);
            if (_echoHigh) ctx.AddConductance(Vcc, Echo, 1.0 / 50.0);
            else ctx.AddConductance(Echo, Ground, 1.0 / 50.0);
        }

        public override void Accept(AcceptContext ctx)
        {
            double t = ctx.Time;
            bool powered = ctx.V(Vcc, Ground) > 4.0;
            bool trig = powered && ctx.V(Trig, Ground) > 2.0;
            if (trig && !_trigHigh) _trigRise = t;
            if (!trig && _trigHigh && _trigRise >= 0 && t - _trigRise >= 9e-6 && _echoStart < 0 && !_echoHigh)
            {
                double d = Distance;
                double width = d >= MinRange && d <= MaxRange ? 2.0 * d / SpeedOfSound : 0.038;
                _echoStart = t + 450e-6;
                _echoEnd = _echoStart + width;
                LastPulseWidth = width;
            }

            _trigHigh = trig;
            if (!_echoHigh && _echoStart >= 0 && t >= _echoStart - 1e-12)
            {
                _echoHigh = true;
                PendingDiscontinuity = true;
            }
            else if (_echoHigh && t >= _echoEnd - 1e-12)
            {
                _echoHigh = false;
                _echoStart = _echoEnd = -1;
                PendingDiscontinuity = true;
            }
        }

        public override double NextBreakpoint(double time)
        {
            if (!_echoHigh && _echoStart > time) return _echoStart;
            if (_echoHigh && _echoEnd > time) return _echoEnd;
            return double.PositiveInfinity;
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "DIST", Distance);
            sink.Write(Id, "ECHO", _echoHigh ? 1 : 0);
        }
    }
}
