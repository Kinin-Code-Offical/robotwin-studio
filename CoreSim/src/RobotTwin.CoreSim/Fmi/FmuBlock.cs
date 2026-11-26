using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Fmi
{
    /// <summary>
    /// [fmi] An FMU in the circuit, co-simulated with it: each of its inputs reads a pin's voltage (against the block's
    /// reference pin), each output drives a pin as a source behind a small resistance. The circuit and the FMU meet at
    /// communication points (every <see cref="CommunicationStep"/>, also a breakpoint of the circuit's solver): the inputs
    /// the circuit has reached go in, <c>fmi2DoStep</c> runs the FMU's own solver over the interval, and the outputs it
    /// gives drive the circuit until the next point (a zero-order hold, as co-simulation masters do).
    /// </summary>
    public sealed class FmuBlock : AnalogDevice, IDisposable
    {
        private readonly Fmu2Instance _fmu;
        private readonly int _reference;
        private readonly List<(FmuVariable Var, int Node)> _inputs;
        private readonly List<(FmuVariable Var, int Node)> _outputs;
        private readonly double[] _drive;
        private readonly IReadOnlyDictionary<string, double> _parameters;
        private bool _started;

        public FmuBlock(string id, Fmu2Instance fmu, int reference, IEnumerable<(FmuVariable Var, int Node)> inputs,
            IEnumerable<(FmuVariable Var, int Node)> outputs, IReadOnlyDictionary<string, double>? parameters = null)
            : base(id)
        {
            _fmu = fmu;
            _reference = reference;
            _inputs = inputs.ToList();
            _outputs = outputs.ToList();
            _drive = new double[_outputs.Count];
            _parameters = parameters ?? new Dictionary<string, double>();
            CommunicationStep = fmu.Description.DefaultStep is double s && s > 0 ? s : 1e-3;
        }

        /// <summary>Time between communication points, s.</summary>
        public double CommunicationStep { get; set; }

        /// <summary>The resistance each output drives its pin through, Ω.</summary>
        public double OutputResistance { get; set; } = 1.0;

        public Fmu2Instance Instance => _fmu;

        /// <summary>The value each output drives now (what the FMU gave at the last communication point).</summary>
        public IReadOnlyList<double> Drive => _drive;

        private void Start(double time, Func<int, double> volts)
        {
            var start = new Dictionary<string, double>(_parameters);
            foreach (var (v, node) in _inputs) start[v.Name] = volts(node) - volts(_reference);
            _fmu.Initialise(time, start);
            for (int k = 0; k < _outputs.Count; k++) _drive[k] = _fmu.Get(_outputs[k].Var);
            _started = true;
        }

        public override void Stamp(StampContext ctx)
        {
            if (!_started) Start(ctx.Time, ctx.V);
            double g = 1.0 / OutputResistance;
            for (int k = 0; k < _outputs.Count; k++)
            {
                int node = _outputs[k].Node;
                ctx.AddConductance(node, _reference, g);
                ctx.AddCurrent(_reference, node, g * _drive[k]);
            }

            foreach (var (_, node) in _inputs) ctx.AddConductance(node, _reference, 1e-12);   // an input reads, it does not load
        }

        public override void Accept(AcceptContext ctx)
        {
            if (ctx.IsDc || !_started) return;
            // At each communication point the circuit has reached (its breakpoints land on them), one whole step: the
            // circuit's own steps in between exchange nothing, so the outputs change, and the circuit restarts, only there.
            while (ctx.Time - _fmu.Time >= CommunicationStep * (1 - 1e-9))
            {
                double h = CommunicationStep;
                foreach (var (v, node) in _inputs) _fmu.Set(v, ctx.V(node) - ctx.V(_reference));
                _fmu.Step(h);
                bool changed = false;
                for (int k = 0; k < _outputs.Count; k++)
                {
                    double y = _fmu.Get(_outputs[k].Var);
                    changed |= Math.Abs(y - _drive[k]) > 1e-12;
                    _drive[k] = y;
                }

                if (changed) ctx.RequestDiscontinuity();
            }
        }

        public override double NextBreakpoint(double time)
        {
            if (!_started) return double.PositiveInfinity;
            double next = _fmu.Time + CommunicationStep;
            return next > time + 1e-12 ? next : double.PositiveInfinity;
        }

        public void Dispose() => _fmu.Dispose();
    }
}
