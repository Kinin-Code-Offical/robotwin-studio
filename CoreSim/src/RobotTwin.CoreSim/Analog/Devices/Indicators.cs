using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>Active buzzer: a resistive load that sounds once enough current flows.</summary>
    public sealed class Buzzer : Resistor
    {
        public Buzzer(string id, int plus, int minus, double ratedVoltage = 5.0, double ratedCurrent = 0.03)
            : base(id, plus, minus, ratedVoltage / ratedCurrent)
        {
            RatedCurrent = ratedCurrent;
        }

        public double RatedCurrent { get; }

        /// <summary>Loudness relative to the rated drive (0..1); a reversed buzzer stays silent.</summary>
        public double Loudness => Current <= RatedCurrent * 0.3 ? 0.0 : Math.Min(1.0, Current / RatedCurrent);

        public override void Report(ISignalSink sink)
        {
            base.Report(sink);
            sink.Write(Id, "SOUND", Loudness);
        }
    }

    /// <summary>
    /// Incandescent lamp: tungsten filament whose resistance rises about ten-fold
    /// from cold to operating temperature, giving the real inrush current.
    /// </summary>
    public sealed class Lamp : Resistor
    {
        private double _heat;

        public Lamp(string id, int a, int b, double ratedVoltage, double ratedPower)
            : base(id, a, b, ratedVoltage * ratedVoltage / ratedPower)
        {
            RatedVoltage = ratedVoltage;
            RatedPower = ratedPower;
        }

        public double RatedVoltage { get; }
        public double RatedPower { get; }
        public double FilamentTimeConstant { get; set; } = 0.05;

        /// <summary>Light output relative to rated (0..~1.5).</summary>
        public double Brightness => Math.Pow(Math.Max(0.0, Power) / RatedPower, 1.6);

        protected override double NominalResistance() => Resistance * (0.1 + 0.9 * _heat);

        public override void Reset()
        {
            base.Reset();
            _heat = 0.0;
        }

        public override void Accept(AcceptContext ctx)
        {
            base.Accept(ctx);
            if (ctx.IsDc)
            {
                return;
            }

            double target = Math.Min(1.3, Math.Sqrt(Math.Max(0.0, Power) / RatedPower));
            _heat = target + (_heat - target) * Math.Exp(-ctx.Dt / FilamentTimeConstant);
            if (ctx.EnableDamage && !IsFailed && Math.Abs(Voltage) > RatedVoltage * 1.6)
            {
                Fail(FailureMode.Open, $"flaman {Math.Abs(Voltage):F1} V'ta koptu", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            base.Report(sink);
            sink.Write(Id, "L", IsFailed ? 0.0 : Brightness);
        }
    }

    /// <summary>Ideal ammeter: a 0 V source that reports the current flowing in at + and out at -.</summary>
    public sealed class Ammeter : VoltageSource
    {
        public Ammeter(string id, int plus, int minus) : base(id, plus, minus, new DcWaveform(0.0))
        {
        }

        public double Reading => -Current;

        public override void Report(ISignalSink sink) => sink.Write(Id, "I", Reading);
    }

    /// <summary>
    /// Sensor module output driven by the simulation host: either a fraction of
    /// the module supply (ratiometric sensors, digital outputs) or an absolute
    /// voltage (LM35-style temperature sensors), behind an output resistance.
    /// </summary>
    public sealed class ControlledSource : AnalogDevice
    {
        private double _level;

        public ControlledSource(string id, int output, int supply, int ground, bool ratiometric = true) : base(id)
        {
            Output = output;
            Supply = supply;
            Ground = ground;
            Ratiometric = ratiometric;
        }

        public int Output { get; }
        public int Supply { get; }
        public int Ground { get; }
        public bool Ratiometric { get; }
        public double OutputResistance { get; set; } = 100.0;
        public double SupplyCurrent { get; set; } = 5e-3;

        /// <summary>Ratiometric: 0..1 of the supply. Absolute: volts.</summary>
        public double Level
        {
            get => _level;
            set
            {
                if (_level == value) return;
                _level = value;
                PendingDiscontinuity = true;
            }
        }

        public override int BranchCount => 1;

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Supply, Ground, SupplyCurrent / 5.0);
            int k = BranchBase;

            // The output only works while the module is powered.
            ctx.AddBranchCurrent(Supply, Output, k);
            ctx.AddRowNode(k, Output, 1.0);
            ctx.AddRowNode(k, Ground, -1.0);
            ctx.AddMatrix(k, k, -OutputResistance);
            if (Ratiometric)
            {
                ctx.AddRowNode(k, Supply, -_level);
                ctx.AddRowNode(k, Ground, _level);
            }
            else
            {
                double vs = ctx.V(Supply, Ground);
                double gate = vs > 2.5 ? 1.0 : Math.Max(0.0, vs / 2.5);
                ctx.AddRhs(k, _level * gate);
            }
        }

        public override void Report(ISignalSink sink) => sink.Write(Id, "LEVEL", _level);
    }
}
