using System;
using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// Phototransistor between collector and emitter: a light-controlled current source that
    /// saturates (V_CE(sat) knee ≈ 0.15 V) when the load cannot supply the photocurrent. With a
    /// pull-up resistor it gives the familiar "dark = high, white = low" sensor output.
    /// </summary>
    public sealed class Phototransistor : AnalogDevice
    {
        private double _iph;

        public Phototransistor(string id, int collector, int emitter) : base(id)
        {
            Collector = collector;
            Emitter = emitter;
        }

        public int Collector { get; }
        public int Emitter { get; }

        /// <summary>Knee voltage of the saturation region (V).</summary>
        public double Knee { get; set; } = 0.15;

        /// <summary>Light-generated collector current (A), set by the world's optics every sensing tick.</summary>
        public double Photocurrent
        {
            get => _iph;
            set
            {
                double v = Math.Max(0, value);
                if (Math.Abs(v - _iph) <= 1e-12) return;
                if (Math.Abs(v - _iph) > 0.02 * Math.Max(1e-6, _iph)) PendingDiscontinuity = true;
                _iph = v;
            }
        }

        /// <summary>Current last solved (A).</summary>
        public double Current { get; private set; }

        public override bool IsNonlinear => true;

        private double _vLast = double.NaN;

        public override void Stamp(StampContext ctx)
        {
            double v = ctx.V(Collector, Emitter);
            // A saturating current source has almost no slope above the knee, so a plain Newton step
            // overshoots far below zero; land in the knee region instead and limit large steps.
            if (!double.IsNaN(_vLast))
            {
                if (v < 0.5 * Knee && _vLast > 3 * Knee)
                {
                    v = 2.5 * Knee;
                    ctx.LimitingApplied = true;
                }
                else if (Math.Abs(v - _vLast) > 2.0)
                {
                    v = _vLast + Math.Sign(v - _vLast) * 2.0;
                    ctx.LimitingApplied = true;
                }
            }

            _vLast = v;
            const double leak = 1e-9;
            var (iv, gv) = Characteristic(v);
            double i = iv + leak * v;
            double g = gv + leak;
            ctx.AddConductance(Collector, Emitter, g);
            ctx.AddCurrent(Collector, Emitter, i - g * v);
        }

        public override void Accept(AcceptContext ctx)
        {
            double v = ctx.V(Collector, Emitter);
            _vLast = v;
            Current = Characteristic(v).I;
        }

        /// <summary>I_C(V_CE): i_ph·(1 − e^(−v/knee)) forward, a small resistance (knee / i_ph) when reversed.</summary>
        private (double I, double G) Characteristic(double v)
        {
            if (v < 0) return (_iph * v / Knee, _iph / Knee);
            double e = Math.Exp(-Math.Min(60, v / Knee));
            return (_iph * (1 - e), _iph / Knee * e);
        }

        public override void Reset() => _vLast = double.NaN;

        public override void Report(ISignalSink sink) => sink.Write(Id, "IPH", _iph);
    }

    /// <summary>
    /// LM393/LM339-style comparator with an open-collector output: pulls <see cref="Output"/> low
    /// while the signal input is below the reference (a fraction of the supply set by the
    /// module's trimmer). No hysteresis, like the cheap modules — noise near the threshold
    /// makes the output chatter.
    /// </summary>
    public sealed class OpenCollectorComparator : AnalogDevice
    {
        private bool _low;

        public OpenCollectorComparator(string id, int input, int supply, int ground, int output) : base(id)
        {
            Input = input;
            Supply = supply;
            Ground = ground;
            Output = output;
        }

        public int Input { get; }
        public int Supply { get; }
        public int Ground { get; }
        public int Output { get; }

        /// <summary>Reference as a fraction of the supply (the blue trimmer).</summary>
        public double Threshold { get; set; } = 0.5;

        /// <summary>Input offset (V), a per-part tolerance.</summary>
        public double Offset { get; set; }

        /// <summary>Optional hysteresis as a fraction of the supply (0 on the common modules).</summary>
        public double Hysteresis { get; set; }

        /// <summary>Output saturation resistance (Ω).</summary>
        public double OnResistance { get; set; } = 60;

        public bool OutputLow => _low;

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Supply, Ground, 0.4e-3 / 5.0);
            ctx.AddConductance(Output, Ground, _low ? 1.0 / OnResistance : 1e-9);
        }

        public override void Accept(AcceptContext ctx)
        {
            double vs = ctx.V(Supply, Ground);
            if (vs < 1.8)
            {
                Set(false);
                return;
            }

            double vin = ctx.V(Input, Ground) + Offset;
            double h = (_low ? 1 : -1) * Hysteresis * vs / 2;
            Set(vin < Threshold * vs + h);
        }

        private void Set(bool low)
        {
            if (low == _low) return;
            _low = low;
            PendingDiscontinuity = true;
        }

        public override void Report(ISignalSink sink) => sink.Write(Id, "LOW", _low ? 1 : 0);
    }

    /// <summary>
    /// Incremental quadrature encoder: channels A and B 90° apart, push-pull outputs to the
    /// supply. The disc angle comes from the shaft it is mounted on (a DC motor's rotor), so
    /// the edges appear on the circuit pins at the real rate for the firmware to count.
    /// </summary>
    public sealed class QuadratureEncoder : AnalogDevice
    {
        private int _state = -1;
        private long _quarters;

        public QuadratureEncoder(string id, int a, int b, int supply, int ground, Func<double> angle) : base(id)
        {
            A = a;
            B = b;
            Supply = supply;
            Ground = ground;
            Angle = angle;
        }

        public int A { get; }
        public int B { get; }
        public int Supply { get; }
        public int Ground { get; }

        /// <summary>Disc angle in radians.</summary>
        public Func<double> Angle { get; }

        /// <summary>Lines (pulses) per disc revolution, per channel.</summary>
        public double PulsesPerRevolution { get; set; } = 11;

        public double OutputResistance { get; set; } = 220;

        public double SupplyCurrent { get; set; } = 5e-3;

        /// <summary>Signed quadrature count (4 per line) since reset.</summary>
        public long Count => _quarters;

        public bool LevelA => _state == 1 || _state == 2;
        public bool LevelB => _state == 2 || _state == 3;

        public override int BranchCount => 2;

        public override void BeginStep(StepInfo step) => Update();

        private void Update()
        {
            double phase = Angle() / (2 * Math.PI) * PulsesPerRevolution * 4;
            long q = (long)Math.Floor(phase);
            int s = (int)(((q % 4) + 4) % 4);
            if (s == _state) return;
            _state = s;
            _quarters = q;
            PendingDiscontinuity = true;
        }

        public override void Reset()
        {
            _state = -1;
            _quarters = 0;
        }

        public override void Stamp(StampContext ctx)
        {
            if (_state < 0) Update();
            ctx.AddConductance(Supply, Ground, SupplyCurrent / 5.0);
            StampOutput(ctx, BranchBase, A, LevelA);
            StampOutput(ctx, BranchBase + 1, B, LevelB);
        }

        private void StampOutput(StampContext ctx, int k, int node, bool high)
        {
            ctx.AddBranchCurrent(Supply, node, k);
            ctx.AddRowNode(k, node, 1.0);
            ctx.AddRowNode(k, Ground, -1.0);
            ctx.AddMatrix(k, k, -OutputResistance);
            double level = high ? 1.0 : 0.0;
            ctx.AddRowNode(k, Supply, -level);
            ctx.AddRowNode(k, Ground, level);
        }

        public override void Report(ISignalSink sink) => sink.Write(Id, "COUNT", _quarters);
    }
}
