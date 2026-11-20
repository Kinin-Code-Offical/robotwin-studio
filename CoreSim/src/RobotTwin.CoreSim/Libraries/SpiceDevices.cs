using System;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Linear controlled sources for SPICE subcircuits (E, G, F, H elements).
    // Sign conventions follow SPICE: a source's current flows from n+ through the
    // source to n-; F/H sense the branch current of a voltage source (+ to - inside it).

    /// <summary>E: V(n+,n-) = gain · V(nc+,nc-).</summary>
    public sealed class SpiceVcvs : AnalogDevice
    {
        public SpiceVcvs(string id, int plus, int minus, int controlPlus, int controlMinus, double gain) : base(id)
        {
            Plus = plus;
            Minus = minus;
            ControlPlus = controlPlus;
            ControlMinus = controlMinus;
            Gain = gain;
        }

        public int Plus { get; }
        public int Minus { get; }
        public int ControlPlus { get; }
        public int ControlMinus { get; }
        public double Gain { get; set; }
        public double Voltage { get; private set; }
        public double Current { get; private set; }

        public override int BranchCount => 1;

        public override void Stamp(StampContext ctx)
        {
            int k = BranchBase;
            ctx.AddBranchCurrent(Plus, Minus, k);
            ctx.AddRowNode(k, Plus, 1.0);
            ctx.AddRowNode(k, Minus, -1.0);
            ctx.AddRowNode(k, ControlPlus, -Gain);
            ctx.AddRowNode(k, ControlMinus, Gain);
        }

        public override void Accept(AcceptContext ctx)
        {
            Voltage = ctx.V(Plus, Minus);
            Current = -ctx.Unknown(BranchBase);
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
        }
    }

    /// <summary>G: I(n+→n-) = gm · V(nc+,nc-).</summary>
    public sealed class SpiceVccs : AnalogDevice
    {
        public SpiceVccs(string id, int plus, int minus, int controlPlus, int controlMinus, double transconductance) : base(id)
        {
            Plus = plus;
            Minus = minus;
            ControlPlus = controlPlus;
            ControlMinus = controlMinus;
            Transconductance = transconductance;
        }

        public int Plus { get; }
        public int Minus { get; }
        public int ControlPlus { get; }
        public int ControlMinus { get; }
        public double Transconductance { get; set; }
        public double Current { get; private set; }

        public override void Stamp(StampContext ctx) => ctx.AddTransconductance(Plus, Minus, ControlPlus, ControlMinus, Transconductance);

        public override void Accept(AcceptContext ctx) => Current = Transconductance * ctx.V(ControlPlus, ControlMinus);

        public override void Report(ISignalSink sink) => sink.Write(Id, "I", Current);
    }

    /// <summary>F: I(n+→n-) = gain · I(Vcontrol).</summary>
    public sealed class SpiceCccs : AnalogDevice
    {
        private readonly VoltageSource _control;

        public SpiceCccs(string id, int plus, int minus, VoltageSource control, double gain) : base(id)
        {
            Plus = plus;
            Minus = minus;
            _control = control ?? throw new ArgumentNullException(nameof(control));
            Gain = gain;
        }

        public int Plus { get; }
        public int Minus { get; }
        public double Gain { get; set; }
        public double Current { get; private set; }

        public override void Stamp(StampContext ctx)
        {
            int k = _control.BranchBase;
            if (k < 0) return;
            if (Plus > 0) ctx.AddMatrix(Plus - 1, k, Gain);
            if (Minus > 0) ctx.AddMatrix(Minus - 1, k, -Gain);
        }

        public override void Accept(AcceptContext ctx)
        {
            if (_control.BranchBase >= 0) Current = Gain * ctx.Unknown(_control.BranchBase);
        }

        public override void Report(ISignalSink sink) => sink.Write(Id, "I", Current);
    }

    /// <summary>H: V(n+,n-) = r · I(Vcontrol).</summary>
    public sealed class SpiceCcvs : AnalogDevice
    {
        private readonly VoltageSource _control;

        public SpiceCcvs(string id, int plus, int minus, VoltageSource control, double transresistance) : base(id)
        {
            Plus = plus;
            Minus = minus;
            _control = control ?? throw new ArgumentNullException(nameof(control));
            Transresistance = transresistance;
        }

        public int Plus { get; }
        public int Minus { get; }
        public double Transresistance { get; set; }
        public double Voltage { get; private set; }

        public override int BranchCount => 1;

        public override void Stamp(StampContext ctx)
        {
            int k = BranchBase;
            ctx.AddBranchCurrent(Plus, Minus, k);
            ctx.AddRowNode(k, Plus, 1.0);
            ctx.AddRowNode(k, Minus, -1.0);
            if (_control.BranchBase >= 0) ctx.AddMatrix(k, _control.BranchBase, -Transresistance);
        }

        public override void Accept(AcceptContext ctx) => Voltage = ctx.V(Plus, Minus);

        public override void Report(ISignalSink sink) => sink.Write(Id, "V", Voltage);
    }
}
