using System;
using static RobotTwin.CoreSim.Instruments.InvariantText;

namespace RobotTwin.CoreSim.Analog.Devices
{
    public enum SupplyRegulation
    {
        /// <summary>Constant voltage: the output sits at the set voltage.</summary>
        ConstantVoltage,

        /// <summary>Constant current: the load wants more than the current limit; the voltage drops.</summary>
        ConstantCurrent,

        /// <summary>Output switched off (or tripped by over-voltage protection).</summary>
        Off
    }

    /// <summary>
    /// Laboratory bench power supply: CV/CC regulation with an adjustable current limit,
    /// over-voltage protection that latches the output off, an output switch, and a
    /// bleeder across the terminals. It cannot sink current, so a load that back-feeds it
    /// above the set voltage simply lifts the output (and may trip OVP).
    /// </summary>
    public sealed class BenchSupply : AnalogDevice
    {
        private readonly OutputStage _stage = new OutputStage { SinkLimit = 0.0 };
        private bool _ovpTripped;
        private bool _enabled = true;
        private double _voltage;
        private double _limit;

        public BenchSupply(string id, int plus, int minus, double voltage, double currentLimit) : base(id)
        {
            Plus = plus;
            Minus = minus;
            _voltage = Math.Max(0.0, voltage);
            _limit = Math.Max(1e-4, currentLimit);
        }

        public int Plus { get; }
        public int Minus { get; }

        /// <summary>Voltage set point (CV).</summary>
        public double Voltage
        {
            get => _voltage;
            set
            {
                double v = Math.Max(0.0, value);
                if (v == _voltage) return;
                _voltage = v;
                PendingDiscontinuity = true;
            }
        }

        /// <summary>Current limit (CC).</summary>
        public double CurrentLimit
        {
            get => _limit;
            set
            {
                double v = Math.Max(1e-4, value);
                if (v == _limit) return;
                _limit = v;
                PendingDiscontinuity = true;
            }
        }

        /// <summary>Over-voltage protection threshold; 0 disables it.</summary>
        public double OvpLevel { get; set; }

        public bool OutputEnabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value) _ovpTripped = false;
                _stage.Mode = StageMode.Regulating;
                PendingDiscontinuity = true;
            }
        }

        public bool OvpTripped => _ovpTripped;

        public double OutputResistance { get; set; } = 0.005;

        /// <summary>Internal bleeder resistor across the output terminals.</summary>
        public double BleederResistance { get; set; } = 1e6;

        public double OutputVoltage { get; private set; }

        /// <summary>Current delivered out of the + terminal.</summary>
        public double OutputCurrent { get; private set; }

        public double Power => OutputVoltage * OutputCurrent;

        public SupplyRegulation Regulation =>
            !_enabled || _ovpTripped || _stage.Mode == StageMode.Off ? SupplyRegulation.Off
            : _stage.Mode == StageMode.CurrentLimit ? SupplyRegulation.ConstantCurrent
            : SupplyRegulation.ConstantVoltage;

        public override bool IsNonlinear => true;

        public override int BranchCount => 1;

        public override void BeginStep(StepInfo step) => _stage.BeginStep();

        public override void Reset()
        {
            _stage.Mode = StageMode.Regulating;
            _stage.LastCurrent = 0.0;
            OutputVoltage = OutputCurrent = 0.0;
        }

        public override void Repair()
        {
            base.Repair();
            _ovpTripped = false;
            _stage.Mode = StageMode.Regulating;
        }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Plus, Minus, 1.0 / BleederResistance);
            int k = BranchBase;
            if (!_enabled || _ovpTripped || IsFailed)
            {
                ctx.AddBranchCurrent(Minus, Plus, k);
                ctx.AddMatrix(k, k, 1.0);
                return;
            }

            double target = ctx.V(Minus) + _voltage;
            _stage.OutputResistance = OutputResistance;
            _stage.SourceLimit = _limit;
            _stage.SinkLimit = 0.0;
            _stage.SetControl(0, Minus, 1.0);
            _stage.Reconcile(ctx, k, Plus, target);
            _stage.Stamp(ctx, k, Minus, Plus, target, 1);
        }

        public override void Accept(AcceptContext ctx)
        {
            OutputVoltage = ctx.V(Plus, Minus);
            double branch = !_enabled || _ovpTripped ? 0.0 : ctx.Unknown(BranchBase);
            OutputCurrent = branch - OutputVoltage / BleederResistance;
            if (_enabled && !_ovpTripped && !IsFailed)
            {
                double target = ctx.V(Minus) + _voltage;
                if (_stage.Update(branch, ctx.V(Plus), target)) ctx.RequestDiscontinuity();
            }

            if (ctx.IsDc || !_enabled || _ovpTripped) return;
            if (OvpLevel > 0.0 && OutputVoltage > OvpLevel)
            {
                _ovpTripped = true;
                ctx.RequestDiscontinuity();
                ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.Warning,
                    I($"OVP devreye girdi: çıkış {OutputVoltage:F2} V > {OvpLevel:G3} V, çıkış kapatıldı")));
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", OutputVoltage);
            sink.Write(Id, "I", OutputCurrent);
            sink.Write(Id, "P", Power);
            sink.Write(Id, "CC", Regulation == SupplyRegulation.ConstantCurrent ? 1.0 : 0.0);
        }
    }
}
