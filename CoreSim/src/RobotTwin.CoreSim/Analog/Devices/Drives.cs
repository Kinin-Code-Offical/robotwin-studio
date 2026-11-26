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

        // ---------------------------------------------------------------- [esc] servo-pulse throttle

        /// <summary>
        /// ESC mode: the duty follows a servo pulse on this node (1000 µs = stop, 2000 µs = full, as
        /// BLHeli / SimonK ESCs read it). The ESC arms only after it has seen low-throttle pulses for
        /// <see cref="ArmTime"/>, and stops the motor when pulses are lost for <see cref="SignalTimeout"/>.
        /// </summary>
        public int PulseNode { get; set; } = -1;

        public double PulseMin { get; set; } = 1.05e-3;
        public double PulseMax { get; set; } = 1.95e-3;
        public double ArmTime { get; set; } = 0.3;
        public double SignalTimeout { get; set; } = 0.25;

        /// <summary>
        /// Current limit (A): above it the ESC pulls its duty back, as BLHeli's start-up power limit
        /// does — a stopped motor is nearly a short (0.1 Ω) and would otherwise draw tens of amps.
        /// 0 = 1.5 × <see cref="CurrentRating"/>.
        /// </summary>
        public double CurrentLimit { get; set; }

        /// <summary>Throttle slew (full scale per second): an ESC ramps, it does not step.</summary>
        public double ThrottleSlew { get; set; } = 25;

        public bool Armed { get; private set; }

        /// <summary>Throttle decoded from the last valid pulse (0..1).</summary>
        public double Throttle { get; private set; }

        public double LastPulseWidth { get; private set; }

        private bool _pulseHigh;
        private double _pulseRise = -1, _lastPulse = -1, _lowSince = -1;

        private void DecodePulse(AcceptContext ctx)
        {
            double v = ctx.V(PulseNode, Ground);
            bool high = _pulseHigh ? v > 0.8 : v > 1.6;   // 3.3 V and 5 V logic both read
            if (high && !_pulseHigh) _pulseRise = ctx.Time;
            if (!high && _pulseHigh && _pulseRise >= 0)
            {
                double w = ctx.Time - _pulseRise;
                if (w > 0.7e-3 && w < 2.3e-3)
                {
                    LastPulseWidth = w;
                    _lastPulse = ctx.Time;
                    double thr = AnalogMath.Clamp((w - PulseMin) / (PulseMax - PulseMin), 0.0, 1.0);
                    if (!Armed)
                    {
                        // Arming: a run of stop-throttle pulses (no motor start on power-up at half stick).
                        if (thr <= 0.0)
                        {
                            if (_lowSince < 0) _lowSince = ctx.Time;
                            if (ctx.Time - _lowSince >= ArmTime) Armed = true;
                        }
                        else _lowSince = -1;
                    }

                    Throttle = Armed ? thr : 0.0;
                }
            }

            _pulseHigh = high;
            if (_lastPulse >= 0 && ctx.Time - _lastPulse > SignalTimeout) Throttle = 0.0;   // signal lost: failsafe stop
            double step = ThrottleSlew * ctx.Dt;
            double next = _duty + AnalogMath.Clamp(Throttle - _duty, -step, step);
            double limit = CurrentLimit > 0 ? CurrentLimit : 1.5 * CurrentRating;
            double i = Math.Abs(OutputCurrent);
            if (i > limit) next = Math.Min(next, _duty * limit / i);   // current limit: back off in proportion
            Duty = next;
        }

        public override int BranchCount => 1;

        public override void Reset()
        {
            Thermal.Reset(25.0);
            OutputCurrent = OutputVoltage = Power = 0.0;
            Armed = false;
            Throttle = LastPulseWidth = 0.0;
            _pulseHigh = false;
            _pulseRise = _lastPulse = _lowSince = -1;
            if (PulseNode >= 0) _duty = 0.0;
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
            if (PulseNode >= 0 && !ctx.IsDc) DecodePulse(ctx);   // [esc]
            else if (ControlNode >= 0 && !ctx.IsDc)
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
            if (PulseNode >= 0)
            {
                sink.Write(Id, "ARMED", Armed ? 1 : 0);
                sink.Write(Id, "PULSE", LastPulseWidth);
            }
        }
    }

    /// <summary>
    /// HC-SR04 ultrasonic ranger: a ≥10 µs pulse on TRIG starts a measurement;
    /// about 450 µs later ECHO goes high for 2·d / c (38 ms when nothing echoes), c the speed of sound in the air at
    /// <see cref="AirTemperatureC"/>: 331 m/s at 0 °C, 343 at 20 °C, 355 at 40 °C, so a sketch that divides by 58 µs/cm
    /// reads 3 % short on a cold morning and long on a hot day, as the real sensor does.
    /// The distance and the air's temperature come from the world simulation.
    /// </summary>
    public sealed class UltrasonicSensor : AnalogDevice
    {
        private bool _trigHigh;
        private double _trigRise = -1;
        private double _echoStart = -1, _echoEnd = -1;
        private bool _echoHigh;
        private double _lastAccept;

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

        /// <summary>[air] Temperature of the air the pulse crosses (°C, set by the world).</summary>
        public double AirTemperatureC { get; set; } = 20.0;

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
            _lastAccept = 0;
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
            // The pulse rose somewhere since the last accepted point: its width runs from there, so a 10 µs TRIG counts
            // whatever steps the solver took through it (from the first high point, a short first low step lost the edge).
            if (trig && !_trigHigh) _trigRise = _lastAccept;
            if (!trig && _trigHigh && _trigRise >= 0 && t - _trigRise >= 9e-6 && _echoStart < 0 && !_echoHigh)
            {
                double d = Distance;
                double width = d >= MinRange && d <= MaxRange ? 2.0 * d / RobotTwin.CoreSim.Sdk.Atmosphere.SpeedOfSound(AirTemperatureC) : 0.038;
                _echoStart = t + 450e-6;
                _echoEnd = _echoStart + width;
                LastPulseWidth = width;
            }

            _trigHigh = trig;
            _lastAccept = t;
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
