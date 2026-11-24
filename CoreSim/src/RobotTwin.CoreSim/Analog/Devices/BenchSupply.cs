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

    /// <summary>[psu] What an adjustable supply puts out.</summary>
    public enum SupplyOutput
    {
        Dc,
        Ac,

        /// <summary>A sine riding on the DC set point.</summary>
        AcDc
    }

    /// <summary>
    /// Laboratory bench power supply: CV/CC regulation with an adjustable current limit,
    /// over-voltage protection that latches the output off, an output switch, and a
    /// bleeder across the terminals. On DC it cannot sink current, so a load that back-feeds it
    /// above the set voltage simply lifts the output (and may trip OVP). [psu] Set to AC (or AC on
    /// DC) it is a programmable AC source: the set point follows V_dc + √2·V_rms·sin(2πft + φ), the
    /// output stage sources and sinks up to the current limit (reactive loads push current back
    /// for half a cycle), and the readout shows true-RMS voltage and current.
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

        /// <summary>[psu] DC, AC or AC on DC.</summary>
        public SupplyOutput Output { get; set; } = SupplyOutput.Dc;

        /// <summary>[psu] AC amplitude as RMS (V).</summary>
        public double AcRms { get; set; }

        /// <summary>[psu] AC frequency (Hz).</summary>
        public double Frequency { get; set; } = 50.0;

        /// <summary>[psu] AC phase at t = 0 (degrees).</summary>
        public double PhaseDegrees { get; set; }

        public bool IsAc => Output != SupplyOutput.Dc && AcRms > 0.0;

        /// <summary>[psu] True-RMS output voltage and current over the last cycle (DC: the plain values).</summary>
        public double RmsVoltage { get; private set; }

        public double RmsCurrent { get; private set; }

        private double _sumV, _sumI, _window, _lastTime = double.NaN, _lastV, _lastI;
        private long _cycle = -1;

        /// <summary>[psu] The set point at a time.</summary>
        public double SetPoint(double time)
        {
            double dc = Output == SupplyOutput.Ac ? 0.0 : _voltage;
            if (!IsAc) return dc;
            double w = 2.0 * Math.PI * Math.Max(1e-3, Frequency);
            return dc + Math.Sqrt(2.0) * AcRms * Math.Sin(w * time + PhaseDegrees * Math.PI / 180.0);
        }

        /// <summary>[psu] An AC output is resolved with at least 40 points per cycle.</summary>
        public override double NextBreakpoint(double time)
        {
            if (!IsAc || !_enabled) return double.PositiveInfinity;
            double step = 1.0 / (40.0 * Math.Max(1e-3, Frequency));
            return (Math.Floor(time / step + 1e-9) + 1.0) * step;
        }

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

            double target = ctx.V(Minus) + SetPoint(ctx.Time);
            _stage.OutputResistance = OutputResistance;
            _stage.SourceLimit = _limit;
            _stage.SinkLimit = IsAc ? _limit : 0.0;   // [psu] an AC source works in all four quadrants
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
                double target = ctx.V(Minus) + SetPoint(ctx.Time);
                if (_stage.Update(branch, ctx.V(Plus), target)) ctx.RequestDiscontinuity();
            }

            // [psu] True RMS like a meter: the mean of the squares over each whole cycle (trapezoids between steps),
            // shown when the cycle completes; during the first cycle, over what has passed.
            if (IsAc && !ctx.IsDc)
            {
                double f = Math.Max(1e-3, Frequency);
                double dt = double.IsNaN(_lastTime) ? 0.0 : Math.Max(0.0, ctx.Time - _lastTime);
                _sumV += 0.5 * (_lastV * _lastV + OutputVoltage * OutputVoltage) * dt;
                _sumI += 0.5 * (_lastI * _lastI + OutputCurrent * OutputCurrent) * dt;
                _window += dt;
                long cycle = (long)Math.Floor(ctx.Time * f + 1e-9);
                if (_cycle < 0) _cycle = cycle;
                if (cycle != _cycle && _window > 0.0)
                {
                    RmsVoltage = Math.Sqrt(_sumV / _window);
                    RmsCurrent = Math.Sqrt(_sumI / _window);
                    _sumV = _sumI = _window = 0.0;
                    _cycle = cycle;
                }
                else if (_window > 0.0 && cycle == _cycle && RmsVoltage == 0.0)
                {
                    RmsVoltage = Math.Sqrt(_sumV / _window);
                    RmsCurrent = Math.Sqrt(_sumI / _window);
                }
            }
            else
            {
                RmsVoltage = Math.Abs(OutputVoltage);
                RmsCurrent = Math.Abs(OutputCurrent);
            }

            _lastTime = ctx.Time;
            _lastV = OutputVoltage;
            _lastI = OutputCurrent;
            if (ctx.IsDc || !_enabled || _ovpTripped) return;
            if (OvpLevel > 0.0 && Math.Abs(OutputVoltage) > OvpLevel)
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
            if (IsAc)
            {
                sink.Write(Id, "VRMS", RmsVoltage);
                sink.Write(Id, "IRMS", RmsCurrent);
            }

            sink.Write(Id, "P", Power);
            sink.Write(Id, "CC", Regulation == SupplyRegulation.ConstantCurrent ? 1.0 : 0.0);
        }
    }
}
