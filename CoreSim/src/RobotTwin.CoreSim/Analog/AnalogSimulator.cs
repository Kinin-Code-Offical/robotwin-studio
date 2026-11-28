using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog
{
    public enum IntegrationMethod
    {
        Trapezoidal,
        BackwardEuler
    }

    public enum InitialState
    {
        /// <summary>Everything discharged and at rest; supplies switch on at t = 0.</summary>
        Zero,

        /// <summary>Solve the DC operating point first (capacitors open, inductors shorted).</summary>
        OperatingPoint
    }

    public sealed class AnalogOptions
    {
        public double RelTol { get; set; } = 1e-3;
        public double VoltageTol { get; set; } = 1e-6;
        public double CurrentTol { get; set; } = 1e-9;
        public double Gmin { get; set; } = 1e-12;
        public int MaxNewtonIterations { get; set; } = 60;
        public double MaxStep { get; set; } = 1e-3;
        public double MinStep { get; set; } = 1e-11;

        /// <summary>Step used right after a discontinuity (switch, edge, device failure).</summary>
        public double RestartStep { get; set; } = 1e-7;

        public IntegrationMethod Method { get; set; } = IntegrationMethod.Trapezoidal;

        /// <summary>Reject steps whose solution strays too far from the linear prediction.</summary>
        public bool ErrorControl { get; set; } = true;
        public double ErrorRelTol { get; set; } = 5e-3;
        public double ErrorAbsTol { get; set; } = 1e-4;

        public double AmbientTempC { get; set; } = 25.0;

        /// <summary>Parts overheat, burn out or break down when driven outside their ratings.</summary>
        public bool EnableDamage { get; set; } = true;

        /// <summary>
        /// [split] Parts of the circuit that share nothing but the reference (two robots in one world, each with its own
        /// battery) are solved on their own, each at its own step, and side by side on the cores when there are enough of
        /// them: an edge in one no longer cuts the step of all. The parts are found from what the devices touch in the
        /// operating point and the first step, and joined again when a device reaches into another part later.
        /// <see cref="AnalogSimulator.SubstepAccepted"/> then comes once per accepted step of each part (times rise within a
        /// part, not across parts); an instrument that samples nodes uses <see cref="AnalogSimulator.Observe"/>.
        /// </summary>
        public bool SplitIndependentParts { get; set; }
    }

    public sealed class StepStatistics
    {
        public int Substeps { get; internal set; }
        public int NewtonIterations { get; internal set; }
        public int RejectedSteps { get; internal set; }
        public int ForcedSteps { get; internal set; }
        public double SmallestStep { get; internal set; }
        public bool Converged => ForcedSteps == 0;

        internal void Reset()
        {
            Substeps = 0;
            NewtonIterations = 0;
            RejectedSteps = 0;
            ForcedSteps = 0;
            SmallestStep = double.PositiveInfinity;
        }

        /// <summary>[split] Adds a part's counts to the whole call's.</summary>
        internal void Add(StepStatistics part)
        {
            Substeps += part.Substeps;
            NewtonIterations += part.NewtonIterations;
            RejectedSteps += part.RejectedSteps;
            ForcedSteps += part.ForcedSteps;
            if (part.SmallestStep < SmallestStep) SmallestStep = part.SmallestStep;
        }
    }

    public sealed class AnalogSimulationException : Exception
    {
        public AnalogSimulationException(string message) : base(message) { }
    }

    /// <summary>
    /// Transient circuit simulator: modified nodal analysis, Newton-Raphson for the
    /// nonlinear devices, trapezoidal/backward-Euler integration with adaptive
    /// sub-stepping. Fully deterministic: the same netlist and the same sequence of
    /// calls produce bit-identical results.
    /// </summary>
    public sealed partial class AnalogSimulator : ISignalSink
    {
        private readonly AnalogCircuit _circuit;
        private readonly MnaSystem _mna = new MnaSystem();   // the whole circuit: operating point, AC, and the transient when not split
        private readonly StampContext _stamp = new StampContext();
        private readonly AcceptContext _accept = new AcceptContext();
        private readonly SignalTable _signals = new SignalTable();   // [perf] by slot, not by name, each step
        private readonly List<AnalogEvent> _events = new List<AnalogEvent>();
        private readonly SignalKeyCache _signalKeys = new SignalKeyCache();
        private int[] _netSlots = Array.Empty<int>();

        private AnalogDevice[] _devices = Array.Empty<AnalogDevice>();
        private double[] _x = Array.Empty<double>();
        private double[] _xNew = Array.Empty<double>();
        private double[] _xPrev = Array.Empty<double>();
        private double[] _xPrev2 = Array.Empty<double>();
        private bool _floatingReported;

        private int _nodeCount;
        private int _size;
        private bool _initialized;

        /// <summary>
        /// The whole circuit as one part: the operating point and the AC analysis always, the transient while it is not
        /// split (<see cref="AnalogOptions.SplitIndependentParts"/>). Its arrays are the simulator's own.
        /// </summary>
        private Part _whole = new Part();

        public AnalogSimulator(AnalogCircuit circuit, AnalogOptions? options = null)
        {
            _circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            Options = options ?? new AnalogOptions();
        }

        public AnalogOptions Options { get; }

        /// <summary>
        /// Called after every accepted internal time step with the new time. Node
        /// voltages and device state are current at that point, which lets
        /// instruments sample waveforms at the solver's own resolution (edges included).
        /// [split] When the circuit is solved in parts, once per accepted step of each part, the parts one after the other
        /// (never two at once): times rise within a part, not across parts.
        /// </summary>
        public Action<double>? SubstepAccepted { get; set; }

        /// <summary>Safety net against a pathological netlist that keeps the step size tiny.</summary>
        public int MaxSubstepsPerCall { get; set; } = 200_000;

        public AnalogCircuit Circuit => _circuit;

        public double Time { get; private set; }

        public StepStatistics Statistics { get; } = new StepStatistics();

        /// <summary>Failures, warnings and state changes raised since the last call to <see cref="DrainEvents"/>.</summary>
        public IReadOnlyList<AnalogEvent> Events => _events;

        public IReadOnlyDictionary<string, double> Signals => _signals;

        public int UnknownCount => _size;

        public List<AnalogEvent> DrainEvents()
        {
            var copy = new List<AnalogEvent>(_events);
            _events.Clear();
            return copy;
        }

        /// <summary>Prepares the system. Must be called after the netlist is complete.</summary>
        public void Initialize(InitialState state = InitialState.Zero)
        {
            // Setup may append child devices (body diodes, gate capacitors...), so walk by index.
            // [split] A device added by another's Setup is of its family (a relay's coil and contacts): one part.
            var family = new List<int>();
            for (int i = 0; i < _circuit.Devices.Count; i++)
            {
                while (family.Count < _circuit.Devices.Count) family.Add(-1);
                int before = _circuit.Devices.Count;
                _circuit.Devices[i].Setup(_circuit);
                for (int k = before; k < _circuit.Devices.Count; k++) family.Add(i);
            }

            _devices = new AnalogDevice[_circuit.Devices.Count];
            for (int i = 0; i < _devices.Length; i++) _devices[i] = _circuit.Devices[i];
            while (family.Count < _devices.Length) family.Add(-1);
            // a second Initialize adds no children (Setup ran already): the children keep their parent's id before a '#'
            Dictionary<string, int>? byId = null;
            for (int i = 0; i < _devices.Length; i++)
            {
                string id = _devices[i].Id;
                if (family[i] >= 0 || id.IndexOf('#') < 0) continue;
                if (byId == null)
                {
                    byId = new Dictionary<string, int>(StringComparer.Ordinal);
                    for (int k = 0; k < _devices.Length; k++) byId[_devices[k].Id] = k;
                }

                for (int cut = id.LastIndexOf('#'); cut > 0; cut = id.LastIndexOf('#', cut - 1))
                {
                    if (byId.TryGetValue(id.Substring(0, cut), out int parent) && parent != i)
                    {
                        family[i] = parent;
                        break;
                    }
                }
            }

            _family = family.ToArray();

            _nodeCount = _circuit.NodeCount;
            _signals.Clear();
            _signalKeys.Clear();
            _netSlots = new int[_nodeCount + 1];
            for (int n = 1; n <= _nodeCount; n++) _netSlots[n] = _signals.Slot("NET:" + _circuit.NodeNames[n]);
            int branch = _nodeCount;
            bool nonlinear = false;
            foreach (var device in _devices)
            {
                device.BranchBase = device.BranchCount > 0 ? branch : -1;
                branch += device.BranchCount;
                if (device.IsNonlinear) nonlinear = true;
            }

            _size = branch;
            _mna.Resize(_size);
            _x = new double[_size];
            _xNew = new double[_size];
            _xPrev = new double[_size];
            _xPrev2 = new double[_size];

            var all = new int[_size];
            for (int i = 0; i < _size; i++) all[i] = i;
            _whole = new Part
            {
                Unknowns = all,
                NodeCount = _nodeCount,
                Devices = _devices,
                Mna = _mna,
                Scratch = new double[_size],
                HasNonlinear = nonlinear,
                Stamp = _stamp,
                Accept = _accept,
                Stats = Statistics,
                Events = _events,
                HNext = Options.RestartStep
            };

            Time = 0.0;
            _events.Clear();
            _floatingReported = false;
            StartRecording();   // [split] what the devices touch from here to the end of the first step

            foreach (var device in _devices) device.Reset();
            _initialized = true;

            if (state == InitialState.OperatingPoint)
            {
                if (!SolveOperatingPoint())
                {
                    throw new AnalogSimulationException("DC çalışma noktası yakınsamadı: devre kararlı bir başlangıç durumuna oturmuyor.");
                }
            }

            PublishSignals();
        }

        public double NodeVoltage(int node) => node <= 0 ? 0.0 : _x[node - 1];

        public double NodeVoltage(string name)
        {
            if (!_circuit.TryGetNode(name, out int node))
            {
                throw new ArgumentException($"Bilinmeyen düğüm '{name}'.", nameof(name));
            }

            return NodeVoltage(node);
        }

        public bool TryGetSignal(string deviceId, string key, out double value) =>
            _signals.TryGetValue(deviceId + ":" + key, out value);

        public double Signal(string deviceId, string key) =>
            _signals.TryGetValue(deviceId + ":" + key, out double value)
                ? value
                : throw new KeyNotFoundException($"'{deviceId}:{key}' sinyali yayımlanmıyor.");

        void ISignalSink.Write(string deviceId, string key, double value) =>
            _signals.Set(_signalKeys.Slot(deviceId, key, _signals), value);

        /// <summary>Tells the simulator that something outside changed abruptly (used by hosts that edit devices directly).</summary>
        public void NotifyDiscontinuity()
        {
            _whole.Restart(Options);   // [islands] a switch may have joined or cut off a part of the circuit
            if (_parts != null)
                foreach (var p in _parts) p.Restart(Options);
        }

        /// <summary>Advances simulated time by exactly <paramref name="dt"/> seconds.</summary>
        public StepStatistics Step(double dt)
        {
            if (!_initialized) throw new InvalidOperationException("Call Initialize() first.");
            if (!(dt > 0.0)) throw new ArgumentOutOfRangeException(nameof(dt));

            Statistics.Reset();
            double tEnd = Time + dt;
            double timeEps = Math.Max(1e-15, Math.Abs(tEnd) * 1e-13);

            if (_parts == null) StepWhole(tEnd, timeEps, dt);
            else StepParts(tEnd, timeEps, dt);   // [split] AnalogSimulator.Split.cs

            Time = tEnd;
            if (_parts == null) PublishSignals();   // [split] the parts publish their own, side by side
            return Statistics;
        }

        private void StepWhole(double tEnd, double timeEps, double dt)
        {
            foreach (var device in _devices)
            {
                if (device.ConsumeDiscontinuity()) NotifyDiscontinuity();
            }

            _whole.Time = Time;
            Advance(_whole, tEnd, timeEps, dt);
            if (_recording != null) Split();   // [split] the operating point and the first step decide the parts
        }

        /// <summary>Takes one part of the circuit (or all of it) from its time to <paramref name="tEnd"/>.</summary>
        private void Advance(Part p, double tEnd, double timeEps, double dt)
        {
            var stats = p.Stats;
            int guard = 0;
            while (tEnd - p.Time > timeEps)
            {
                if (++guard > MaxSubstepsPerCall)
                {
                    throw new AnalogSimulationException(
                        $"Zaman adımı çöktü: t = {p.Time:G6} s anında {dt:G3} s'lik adım için {MaxSubstepsPerCall} alt adımdan fazlası gerekti.");
                }

                double planned = Math.Min(p.HNext, Options.MaxStep);
                double h = Math.Min(planned, tEnd - p.Time);
                // [perf] No sliver before the end of the call: a full step that would leave less than a quarter of itself
                // becomes two even ones (a host slicing time in 50 µs pieces otherwise took a 0.1 µs step at the end of
                // most of them, and the next piece had to climb back from it).
                double rest = tEnd - p.Time - h;
                if (rest > timeEps && rest < 0.25 * h) h = (tEnd - p.Time) / 2;
                bool cut = h < planned * (1 - 1e-9);   // shortened to end the call where asked, not by the error
                bool hitsBreakpoint = false;
                double breakpoint = NextBreakpoint(p, p.Time);
                if (breakpoint - p.Time > timeEps && breakpoint < p.Time + h - timeEps)
                {
                    h = breakpoint - p.Time;
                    hitsBreakpoint = true;
                }
                else if (Math.Abs(breakpoint - (p.Time + h)) <= timeEps)
                {
                    hitsBreakpoint = true;
                }

                bool trapezoidal = Options.Method == IntegrationMethod.Trapezoidal && !p.ForceBackwardEuler;
                bool converged = SolveStep(p, p.Time + h, h, trapezoidal, out int iterations);
                stats.NewtonIterations += iterations;

                bool forced = false;
                if (!converged)
                {
                    if (h <= Options.MinStep * 1.0001)
                    {
                        // Nothing smaller to try: accept the best iterate and report it.
                        forced = true;
                        stats.ForcedSteps++;
                        p.Events.Add(new AnalogEvent(p.Time + h, "solver", AnalogEventKind.Warning,
                            "Newton yinelemesi en küçük zaman adımında yakınsamadı."));
                    }
                    else
                    {
                        stats.RejectedSteps++;
                        p.HNext = Math.Max(h * 0.25, Options.MinStep);
                        p.ForceBackwardEuler = true;
                        continue;
                    }
                }

                double growth = p.StepsSinceDiscontinuity < 2 && iterations <= 4 ? 4.0 : 2.0;
                if (!forced && Options.ErrorControl && p.StepsSinceDiscontinuity >= 2 && p.HPrev > 0.0)
                {
                    // Local truncation error from divided differences; the step size follows
                    // h·ratio^(-1/(order+1)), so steps settle where the error is just inside
                    // tolerance instead of oscillating between doubling and rejection.
                    double ratio = EstimateErrorRatio(p, h, trapezoidal);
                    double exponent = trapezoidal && p.StepsSinceDiscontinuity >= 3 ? 1.0 / 3.0 : 0.5;
                    double factor = ratio > 1e-12 ? 0.9 * Math.Pow(ratio, -exponent) : 4.0;
                    if (ratio > 1.0 && h > Options.MinStep * 4.0)
                    {
                        stats.RejectedSteps++;
                        p.HNext = Math.Max(h * Math.Max(0.2, Math.Min(0.9, factor)), Options.MinStep);
                        continue;
                    }

                    growth = Math.Max(0.5, Math.Min(4.0, factor));
                }

                if (iterations > 12) growth = Math.Min(growth, 1.0);

                // Commit the step.
                var u = p.Unknowns;
                for (int i = 0; i < u.Length; i++)
                {
                    int k = u[i];
                    _xPrev2[k] = _xPrev[k];
                    _xPrev[k] = _x[k];
                    _x[k] = _xNew[k];
                }

                p.HPrev2 = p.HPrev;
                p.HPrev = h;
                p.Time += h;
                stats.Substeps++;
                if (h < stats.SmallestStep) stats.SmallestStep = h;

                var accept = p.Accept;
                accept.X = _x;
                accept.Time = p.Time;
                accept.Dt = h;
                accept.IsDc = false;
                accept.AmbientTempC = Options.AmbientTempC;
                accept.EnableDamage = Options.EnableDamage;
                AcceptAll(p, accept);
                if (accept.Events.Count > 0)
                {
                    p.Events.AddRange(accept.Events);
                    accept.Events.Clear();
                }

                if (p.Observers != null)
                    foreach (var o in p.Observers) o(p.Time);   // [split] instruments and boards on this part's nodes
                SubstepAccepted?.Invoke(p.Time);

                bool discontinuity = accept.ConsumeDiscontinuity() || hitsBreakpoint;
                foreach (var device in p.Devices)
                {
                    if (device.ConsumeDiscontinuity()) discontinuity = true;
                }

                if (discontinuity)
                {
                    p.IslandsStale = true;   // [islands]
                    p.ForceBackwardEuler = true;
                    p.StepsSinceDiscontinuity = 0;
                    p.HNext = p.RestartStep(Options);
                }
                else
                {
                    p.ForceBackwardEuler = false;
                    p.StepsSinceDiscontinuity++;
                    // Never below the floor: a step accepted at the minimum because the error could not be met there would
                    // otherwise halve on every step, down to 1e-21 s where the companion models are all round-off.
                    p.HNext = Math.Min(Math.Max(h * growth, Options.MinStep), Options.MaxStep);
                    // [perf] a step cut short only to end the call, with the error allowing all the growth there is: the next
                    // call goes on at the step that was planned, not at four times the cut one
                    if (cut && !hitsBreakpoint && growth >= 4.0 - 1e-9) p.HNext = Math.Max(p.HNext, planned);
                    if (p.StepsSinceDiscontinuity >= 3) p.HCruise = p.HNext;   // settled: the pace a restart starts from
                }
            }

            p.Time = tEnd;
        }

        /// <summary>Every device of the part takes the accepted step; while the parts are being found, each one's reads are told apart.</summary>
        private void AcceptAll(Part p, AcceptContext accept)
        {
            var devices = p.Devices;
            if (_recording != null && ReferenceEquals(p, _whole))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    _recording.Device = i;
                    devices[i].Accept(accept);
                }

                _recording.Device = -1;
                return;
            }

            foreach (var device in devices) device.Accept(accept);
        }

        /// <summary>Solves the DC operating point and loads it as the present state.</summary>
        public bool SolveOperatingPoint()
        {
            if (!_initialized) throw new InvalidOperationException("Call Initialize() first.");
            Array.Clear(_x, 0, _size);

            // Devices with discrete state (logic, relays) may change state once the
            // first solution is known; re-solve until the state is self-consistent.
            for (int pass = 0; pass < 20; pass++)
            {
                if (!SolveOperatingPointOnce()) return false;
                bool changed = _accept.ConsumeDiscontinuity();
                foreach (var device in _devices)
                {
                    if (device.ConsumeDiscontinuity()) changed = true;
                }

                if (!changed) break;
            }

            _whole.ForceBackwardEuler = true;
            _whole.StepsSinceDiscontinuity = 0;
            if (_parts != null)
                foreach (var p in _parts) p.Restart(Options);   // [split] every part starts again from the new state
            return true;
        }

        private bool SolveOperatingPointOnce()
        {
            _whole.IslandsStale = true;   // [islands] devices may have changed state since the last pass
            bool ok = SolveDc(Options.Gmin, 1.0);

            if (!ok)
            {
                // Pseudo-transient continuation: every node gets a shunt to its previous
                // value that is relaxed away. Robust for saturating amplifiers/regulators.
                Array.Clear(_x, 0, _size);
                ok = SolvePseudoTransient();
            }
            if (!ok)
            {
                // Gmin stepping: start heavily damped and relax towards the real circuit.
                Array.Clear(_x, 0, _size);
                ok = true;
                for (double gmin = 1e-2; gmin >= Options.Gmin * 0.999; gmin *= 0.1)
                {
                    if (!SolveDc(gmin, 1.0))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok) ok = SolveDc(Options.Gmin, 1.0);
            }

            if (!ok)
            {
                // Source stepping: ramp every independent source from zero.
                Array.Clear(_x, 0, _size);
                ok = true;
                for (int i = 1; i <= 20 && ok; i++)
                {
                    ok = SolveDc(Options.Gmin, i / 20.0);
                }
            }

            if (!ok) return false;

            Array.Copy(_x, _xPrev, _size);
            Array.Copy(_x, _xPrev2, _size);
            _accept.X = _x;
            _accept.Time = Time;
            _accept.Dt = 0.0;
            _accept.IsDc = true;
            _accept.AmbientTempC = Options.AmbientTempC;
            _accept.EnableDamage = false;
            AcceptAll(_whole, _accept);
            _accept.Events.Clear();
            return true;
        }

        private bool SolvePseudoTransient()
        {
            var anchor = new double[_size];
            double g = 1.0;
            for (int iteration = 0; iteration < 400; iteration++)
            {
                Array.Copy(_x, anchor, _size);
                if (SolveDc(Options.Gmin, 1.0, g, anchor))
                {
                    if (g < 1e-12) return SolveDc(Options.Gmin, 1.0);
                    g *= 0.3;
                }
                else
                {
                    Array.Copy(anchor, _x, _size);
                    g *= 8.0;
                    if (g > 1e6) return false;
                }
            }

            return false;
        }

        private bool SolveDc(double gmin, double sourceScale, double shunt = 0.0, double[]? anchor = null)
        {
            var info = new StepInfo(Time, 0.0, true, false);
            foreach (var device in _devices) device.BeginStep(info);
            Array.Copy(_x, _xNew, _size);
            ConfigureStamp(_whole, Time, 0.0, true, false, gmin, sourceScale);
            _shunt = shunt;
            _shuntAnchor = anchor;
            bool ok;
            try
            {
                ok = Newton(_whole, Options.MaxNewtonIterations * 3, out _);
            }
            finally
            {
                _shunt = 0.0;
                _shuntAnchor = null;
            }

            if (ok) Array.Copy(_xNew, _x, _size);
            return ok;
        }

        private double _shunt;
        private double[]? _shuntAnchor;

        private bool SolveStep(Part p, double time, double h, bool trapezoidal, out int iterations)
        {
            var info = new StepInfo(time, h, false, trapezoidal);
            foreach (var device in p.Devices) device.BeginStep(info);
            var u = p.Unknowns;
            for (int i = 0; i < u.Length; i++) _xNew[u[i]] = _x[u[i]];
            ConfigureStamp(p, time, h, false, trapezoidal, Options.Gmin, 1.0);
            return Newton(p, Options.MaxNewtonIterations, out iterations);
        }

        private void ConfigureStamp(Part p, double time, double h, bool isDc, bool trapezoidal, double gmin, double sourceScale)
        {
            var stamp = p.Stamp;
            stamp.Mna = p.Mna;
            stamp.Time = time;
            stamp.Dt = h;
            stamp.IsDc = isDc;
            stamp.Trapezoidal = trapezoidal;
            stamp.Gmin = gmin;
            stamp.SourceScale = sourceScale;
            stamp.AmbientTempC = Options.AmbientTempC;
        }

        /// <summary>
        /// [islands] A part of the circuit with no path to ground (a battery and a motor on their own, the side of an
        /// encoder motor or a relay that nothing references) has no potential of its own: with only gmin holding it, its
        /// common level rode on round-off, a millivolt from one solve to the next, and the error control cut the step to
        /// nothing chasing it (a stalled battery-motor drive stuck at 1e-11 s). Each such part gets one reference, at the
        /// node a designer would call its ground ("GND…", then "-"), as strong as its own conductances. One connection to a
        /// part that has no other carries no current, so nothing in it changes but its level, which is now 0 V there.
        /// </summary>
        private void FindIslandTies(Part p)
        {
            p.Ties.Clear();
            foreach (var (nodes, scale) in p.Mna.FloatingIslands(p.NodeCount))
            {
                int best = nodes[0], bestScore = -1;
                foreach (int k in nodes)
                {
                    int score = ReferenceScore(_circuit.NodeNames[p.Unknowns[k] + 1]);
                    if (score > bestScore)
                    {
                        best = k;
                        bestScore = score;
                    }
                }

                p.Ties.Add((best, Math.Max(scale, 1e-3)));
            }
        }

        /// <summary>How much a node's name says it is a reference: "…GND" 4, "…-" / "V-" 3, a cathode, emitter or source 1.</summary>
        private static int ReferenceScore(string name)
        {
            int dot = name.LastIndexOf('.');
            string pin = dot >= 0 ? name.Substring(dot + 1) : name;
            if (pin.StartsWith("GND", StringComparison.OrdinalIgnoreCase) || pin.EndsWith("GND", StringComparison.OrdinalIgnoreCase)) return 4;
            if (pin == "-" || pin == "V-" || pin.Equals("VSS", StringComparison.OrdinalIgnoreCase)) return 3;
            return pin == "K" || pin == "E" || pin == "S" ? 1 : 0;
        }

        /// <summary>Newton-Raphson on the part's unknowns of the iterate held in <c>_xNew</c>.</summary>
        private bool Newton(Part p, int maxIterations, out int iterations)
        {
            var scratch = p.Scratch;
            var mna = p.Mna;
            var stamp = p.Stamp;
            var u = p.Unknowns;
            int size = u.Length, nodes = p.NodeCount;

            for (iterations = 1; iterations <= maxIterations; iterations++)
            {
                mna.Clear();
                stamp.X = _xNew;
                stamp.LimitingApplied = false;
                StampAll(p, stamp);
                if (p.IslandsStale)
                {
                    FindIslandTies(p);
                    p.IslandsStale = false;
                }

                foreach (var (node, g) in p.Ties) mna.AddOwn(node, node, g);
                mna.AddDiagonal(nodes, stamp.Gmin);
                if (_shunt > 0.0 && _shuntAnchor != null)
                {
                    mna.AddDiagonal(nodes, _shunt);
                    for (int n = 0; n < nodes; n++) mna.AddOwnRhs(n, _shunt * _shuntAnchor[u[n]]);
                }

                if (!mna.Solve(scratch, out int singular))
                {
                    // Floating islands (parts whose pins go nowhere) have no DC reference.
                    // Tie every node to ground through a growing shunt before giving up;
                    // a genuine loop of voltage sources stays singular and is reported.
                    bool solved = false;
                    for (double shunt = 1e-9; shunt <= 1e-3 && !solved; shunt *= 1000.0)
                    {
                        mna.AddDiagonal(nodes, shunt);
                        solved = mna.Solve(scratch, out singular);
                    }

                    if (!solved) throw new AnalogSimulationException(DescribeSingular(p, singular));
                    if (!_floatingReported && !p.FloatingReported)
                    {
                        // [split] said once per part, so parts solved side by side decide it alone (deterministic)
                        p.FloatingReported = true;
                        if (ReferenceEquals(p, _whole)) _floatingReported = true;
                        p.Events.Add(new AnalogEvent(stamp.Time, "solver", AnalogEventKind.Warning,
                            "Devrede referanssız (boşta) kısımlar var; toprağa yüksek dirençle bağlanarak çözüldü."));
                    }
                }

                bool converged = !stamp.LimitingApplied;
                if (converged && p.HasNonlinear)
                {
                    double relTol = Options.RelTol, voltageTol = Options.VoltageTol, currentTol = Options.CurrentTol;
                    for (int i = 0; i < size; i++)
                    {
                        double a = scratch[i], b = _xNew[u[i]];
                        double tol = relTol * Math.Max(Math.Abs(a), Math.Abs(b)) +
                                     (i < nodes ? voltageTol : currentTol);
                        if (Math.Abs(a - b) > tol)
                        {
                            converged = false;
                            break;
                        }
                    }
                }

                for (int i = 0; i < size; i++) _xNew[u[i]] = scratch[i];
                if (!p.HasNonlinear) return true;
                if (converged && iterations > 1) return true;
            }

            iterations = maxIterations;
            return false;
        }

        /// <summary>Every device of the part stamps; while the parts are being found, each one's touches are told apart.</summary>
        private void StampAll(Part p, StampContext stamp)
        {
            var devices = p.Devices;
            if (_recording != null && ReferenceEquals(p, _whole))
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    _recording.Device = i;
                    devices[i].Stamp(stamp);
                }

                _recording.Device = -1;
                return;
            }

            foreach (var device in devices) device.Stamp(stamp);
        }

        /// <summary>
        /// Worst ratio of the estimated local truncation error to the allowed error over
        /// the part's node voltages. Trapezoidal: LTE ≈ h³·x‴/12 from the third divided difference
        /// of the last four points; backward Euler: LTE ≈ h²·x″/2 from the second. Above 1 the step is too coarse.
        /// </summary>
        private double EstimateErrorRatio(Part p, double h, bool trapezoidal)
        {
            double worst = 0.0;
            double relTol = Options.ErrorRelTol, absTol = Options.ErrorAbsTol;
            double h1 = p.HPrev, h2 = p.HPrev2;
            bool third = trapezoidal && h2 > 0.0 && p.StepsSinceDiscontinuity >= 3;
            var u = p.Unknowns;
            for (int j = 0; j < p.NodeCount; j++)
            {
                int i = u[j];
                double xn = _xNew[i], x0 = _x[i], x1 = _xPrev[i];
                double d1a = (xn - x0) / h, d1b = (x0 - x1) / h1;
                double d2a = (d1a - d1b) / (h + h1);
                double lte;
                if (third)
                {
                    double d1c = (x1 - _xPrev2[i]) / h2;
                    double d2b = (d1b - d1c) / (h1 + h2);
                    double d3 = (d2a - d2b) / (h + h1 + h2);
                    lte = 0.5 * h * h * h * Math.Abs(d3);
                }
                else
                {
                    lte = h * h * Math.Abs(d2a);
                }

                double allowed = relTol * Math.Max(Math.Abs(xn), Math.Abs(x0)) + absTol;
                double ratio = lte / allowed;
                if (ratio > worst) worst = ratio;
            }

            return worst;
        }

        private static double NextBreakpoint(Part p, double time)
        {
            double next = double.PositiveInfinity;
            foreach (var device in p.Devices)
            {
                double candidate = device.NextBreakpoint(time);
                if (candidate < next) next = candidate;
            }

            return next;
        }

        private void PublishSignals()
        {
            for (int n = 1; n <= _nodeCount; n++)
            {
                _signals.Set(_netSlots[n], _x[n - 1]);
            }

            _signalKeys.Rewind();
            foreach (var device in _devices) device.Report(this);
        }

        private string DescribeSingular(Part p, int index)
        {
            if (index >= 0 && index < p.NodeCount)
            {
                return $"Devre matrisi '{_circuit.NodeNames[p.Unknowns[index] + 1]}' düğümünde tekil (çözülemiyor).";
            }

            int unknown = index >= 0 && index < p.Unknowns.Length ? p.Unknowns[index] : -1;
            foreach (var device in p.Devices)
            {
                if (device.BranchBase >= 0 && unknown >= device.BranchBase && unknown < device.BranchBase + device.BranchCount)
                {
                    return $"Devre matrisi '{device.Id}' parçasında tekil (çözülemiyor); " +
                           "büyük olasılıkla gerilim kaynakları ya da bobinler bir döngü oluşturuyor.";
                }
            }

            return "Devre matrisi tekil (çözülemiyor).";
        }
    }
}
