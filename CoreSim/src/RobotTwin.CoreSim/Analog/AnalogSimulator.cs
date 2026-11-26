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
        private readonly MnaSystem _mna = new MnaSystem();
        private readonly StampContext _stamp = new StampContext();
        private readonly AcceptContext _accept = new AcceptContext();
        private readonly Dictionary<string, double> _signals =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly List<AnalogEvent> _events = new List<AnalogEvent>();
        private readonly SignalKeyCache _signalKeys = new SignalKeyCache();
        private string[] _netSignalKeys = Array.Empty<string>();

        private AnalogDevice[] _devices = Array.Empty<AnalogDevice>();
        private double[] _x = Array.Empty<double>();
        private double[] _xNew = Array.Empty<double>();
        private double[] _xPrev = Array.Empty<double>();
        private double[] _xPrev2 = Array.Empty<double>();
        private double[] _scratch = Array.Empty<double>();
        private bool _floatingReported;
        private int _nodeCount;
        private int _size;
        private bool _hasNonlinear;
        private bool _initialized;
        private bool _forceBackwardEuler = true;
        private int _stepsSinceDiscontinuity;
        private double _hPrev2;
        private double _hNext;
        private double _hPrev;

        /// <summary>
        /// [perf] The step the circuit was going at before its last discontinuity: a restart begins at a hundredth of it
        /// (never below <see cref="AnalogOptions.RestartStep"/>). A fixed 0.1 µs restart made every edge of a circuit that
        /// otherwise strides along climb back over a dozen steps: the ESP32 quadcopter's circuit took 20 500 steps per
        /// simulated second, 14 200 now.
        /// </summary>
        private double _hCruise;

        private double RestartAfterDiscontinuity() => Math.Min(Math.Max(Options.RestartStep, _hCruise * 0.01), Options.MaxStep);

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
            for (int i = 0; i < _circuit.Devices.Count; i++)
            {
                _circuit.Devices[i].Setup(_circuit);
            }

            _devices = new AnalogDevice[_circuit.Devices.Count];
            for (int i = 0; i < _devices.Length; i++) _devices[i] = _circuit.Devices[i];

            _nodeCount = _circuit.NodeCount;
            _netSignalKeys = new string[_nodeCount + 1];
            for (int n = 1; n <= _nodeCount; n++) _netSignalKeys[n] = "NET:" + _circuit.NodeNames[n];
            int branch = _nodeCount;
            _hasNonlinear = false;
            foreach (var device in _devices)
            {
                device.BranchBase = device.BranchCount > 0 ? branch : -1;
                branch += device.BranchCount;
                if (device.IsNonlinear) _hasNonlinear = true;
            }

            _size = branch;
            _mna.Resize(_size);
            _x = new double[_size];
            _xNew = new double[_size];
            _xPrev = new double[_size];
            _xPrev2 = new double[_size];
            _scratch = new double[_size];

            Time = 0.0;
            _hNext = Options.RestartStep;
            _hPrev = 0.0;
            _hCruise = 0.0;
            _forceBackwardEuler = true;
            _stepsSinceDiscontinuity = 0;
            _events.Clear();
            _signals.Clear();
            _floatingReported = false;

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
            _signals[_signalKeys.Get(deviceId, key)] = value;

        /// <summary>Tells the simulator that something outside changed abruptly (used by hosts that edit devices directly).</summary>
        public void NotifyDiscontinuity()
        {
            _forceBackwardEuler = true;
            _stepsSinceDiscontinuity = 0;
            _hNext = Math.Min(_hNext, RestartAfterDiscontinuity());
        }

        /// <summary>Advances simulated time by exactly <paramref name="dt"/> seconds.</summary>
        public StepStatistics Step(double dt)
        {
            if (!_initialized) throw new InvalidOperationException("Call Initialize() first.");
            if (!(dt > 0.0)) throw new ArgumentOutOfRangeException(nameof(dt));

            Statistics.Reset();
            double tEnd = Time + dt;
            double timeEps = Math.Max(1e-15, Math.Abs(tEnd) * 1e-13);

            foreach (var device in _devices)
            {
                if (device.ConsumeDiscontinuity()) NotifyDiscontinuity();
            }

            int guard = 0;
            while (tEnd - Time > timeEps)
            {
                if (++guard > MaxSubstepsPerCall)
                {
                    throw new AnalogSimulationException(
                        $"Zaman adımı çöktü: t = {Time:G6} s anında {dt:G3} s'lik adım için {MaxSubstepsPerCall} alt adımdan fazlası gerekti.");
                }

                double planned = Math.Min(_hNext, Options.MaxStep);
                double h = Math.Min(planned, tEnd - Time);
                // [perf] No sliver before the end of the call: a full step that would leave less than a quarter of itself
                // becomes two even ones (a host slicing time in 50 µs pieces otherwise took a 0.1 µs step at the end of
                // most of them, and the next piece had to climb back from it).
                double rest = tEnd - Time - h;
                if (rest > timeEps && rest < 0.25 * h) h = (tEnd - Time) / 2;
                bool cut = h < planned * (1 - 1e-9);   // shortened to end the call where asked, not by the error
                bool hitsBreakpoint = false;
                double breakpoint = NextBreakpoint(Time);
                if (breakpoint - Time > timeEps && breakpoint < Time + h - timeEps)
                {
                    h = breakpoint - Time;
                    hitsBreakpoint = true;
                }
                else if (Math.Abs(breakpoint - (Time + h)) <= timeEps)
                {
                    hitsBreakpoint = true;
                }

                bool trapezoidal = Options.Method == IntegrationMethod.Trapezoidal && !_forceBackwardEuler;
                bool converged = SolveStep(Time + h, h, trapezoidal, out int iterations);
                Statistics.NewtonIterations += iterations;

                bool forced = false;
                if (!converged)
                {
                    if (h <= Options.MinStep * 1.0001)
                    {
                        // Nothing smaller to try: accept the best iterate and report it.
                        forced = true;
                        Statistics.ForcedSteps++;
                        _events.Add(new AnalogEvent(Time + h, "solver", AnalogEventKind.Warning,
                            "Newton yinelemesi en küçük zaman adımında yakınsamadı."));
                    }
                    else
                    {
                        Statistics.RejectedSteps++;
                        _hNext = Math.Max(h * 0.25, Options.MinStep);
                        _forceBackwardEuler = true;
                        continue;
                    }
                }

                double growth = _stepsSinceDiscontinuity < 2 && iterations <= 4 ? 4.0 : 2.0;
                if (!forced && Options.ErrorControl && _stepsSinceDiscontinuity >= 2 && _hPrev > 0.0)
                {
                    // Local truncation error from divided differences; the step size follows
                    // h·ratio^(-1/(order+1)), so steps settle where the error is just inside
                    // tolerance instead of oscillating between doubling and rejection.
                    double ratio = EstimateErrorRatio(h, trapezoidal);
                    double exponent = trapezoidal && _stepsSinceDiscontinuity >= 3 ? 1.0 / 3.0 : 0.5;
                    double factor = ratio > 1e-12 ? 0.9 * Math.Pow(ratio, -exponent) : 4.0;
                    if (ratio > 1.0 && h > Options.MinStep * 4.0)
                    {
                        Statistics.RejectedSteps++;
                        _hNext = Math.Max(h * Math.Max(0.2, Math.Min(0.9, factor)), Options.MinStep);
                        continue;
                    }

                    growth = Math.Max(0.5, Math.Min(4.0, factor));
                }

                if (iterations > 12) growth = Math.Min(growth, 1.0);

                // Commit the step.
                Array.Copy(_xPrev, _xPrev2, _size);
                Array.Copy(_x, _xPrev, _size);
                Array.Copy(_xNew, _x, _size);
                _hPrev2 = _hPrev;
                _hPrev = h;
                Time += h;
                Statistics.Substeps++;
                if (h < Statistics.SmallestStep) Statistics.SmallestStep = h;

                _accept.X = _x;
                _accept.Time = Time;
                _accept.Dt = h;
                _accept.IsDc = false;
                _accept.AmbientTempC = Options.AmbientTempC;
                _accept.EnableDamage = Options.EnableDamage;
                foreach (var device in _devices) device.Accept(_accept);
                if (_accept.Events.Count > 0)
                {
                    _events.AddRange(_accept.Events);
                    _accept.Events.Clear();
                }

                SubstepAccepted?.Invoke(Time);

                bool discontinuity = _accept.ConsumeDiscontinuity() || hitsBreakpoint;
                foreach (var device in _devices)
                {
                    if (device.ConsumeDiscontinuity()) discontinuity = true;
                }

                if (discontinuity)
                {
                    _forceBackwardEuler = true;
                    _stepsSinceDiscontinuity = 0;
                    _hNext = RestartAfterDiscontinuity();
                }
                else
                {
                    _forceBackwardEuler = false;
                    _stepsSinceDiscontinuity++;
                    // Never below the floor: a step accepted at the minimum because the error could not be met there would
                    // otherwise halve on every step, down to 1e-21 s where the companion models are all round-off.
                    _hNext = Math.Min(Math.Max(h * growth, Options.MinStep), Options.MaxStep);
                    // [perf] a step cut short only to end the call, with the error allowing all the growth there is: the next
                    // call goes on at the step that was planned, not at four times the cut one
                    if (cut && !hitsBreakpoint && growth >= 4.0 - 1e-9) _hNext = Math.Max(_hNext, planned);
                    if (_stepsSinceDiscontinuity >= 3) _hCruise = _hNext;   // settled: the pace a restart starts from
                }
            }

            Time = tEnd;
            PublishSignals();
            return Statistics;
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

            _forceBackwardEuler = true;
            _stepsSinceDiscontinuity = 0;
            return true;
        }

        private bool SolveOperatingPointOnce()
        {
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
            foreach (var device in _devices) device.Accept(_accept);
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
            ConfigureStamp(Time, 0.0, true, false, gmin, sourceScale);
            _shunt = shunt;
            _shuntAnchor = anchor;
            bool ok;
            try
            {
                ok = Newton(Options.MaxNewtonIterations * 3, out _);
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

        private bool SolveStep(double time, double h, bool trapezoidal, out int iterations)
        {
            var info = new StepInfo(time, h, false, trapezoidal);
            foreach (var device in _devices) device.BeginStep(info);
            Array.Copy(_x, _xNew, _size);
            ConfigureStamp(time, h, false, trapezoidal, Options.Gmin, 1.0);
            return Newton(Options.MaxNewtonIterations, out iterations);
        }

        private void ConfigureStamp(double time, double h, bool isDc, bool trapezoidal, double gmin, double sourceScale)
        {
            _stamp.Mna = _mna;
            _stamp.Time = time;
            _stamp.Dt = h;
            _stamp.IsDc = isDc;
            _stamp.Trapezoidal = trapezoidal;
            _stamp.Gmin = gmin;
            _stamp.SourceScale = sourceScale;
            _stamp.AmbientTempC = Options.AmbientTempC;
        }

        /// <summary>Newton-Raphson on the iterate held in <c>_xNew</c>.</summary>
        private bool Newton(int maxIterations, out int iterations)
        {
            var scratch = _scratch;

            for (iterations = 1; iterations <= maxIterations; iterations++)
            {
                _mna.Clear();
                _stamp.X = _xNew;
                _stamp.LimitingApplied = false;
                foreach (var device in _devices) device.Stamp(_stamp);
                _mna.AddDiagonal(_nodeCount, _stamp.Gmin);
                if (_shunt > 0.0 && _shuntAnchor != null)
                {
                    _mna.AddDiagonal(_nodeCount, _shunt);
                    for (int n = 0; n < _nodeCount; n++) _mna.AddRhs(n, _shunt * _shuntAnchor[n]);
                }

                if (!_mna.Solve(scratch, out int singular))
                {
                    // Floating islands (parts whose pins go nowhere) have no DC reference.
                    // Tie every node to ground through a growing shunt before giving up;
                    // a genuine loop of voltage sources stays singular and is reported.
                    bool solved = false;
                    for (double shunt = 1e-9; shunt <= 1e-3 && !solved; shunt *= 1000.0)
                    {
                        _mna.AddDiagonal(_nodeCount, shunt);
                        solved = _mna.Solve(scratch, out singular);
                    }

                    if (!solved) throw new AnalogSimulationException(DescribeSingular(singular));
                    if (!_floatingReported)
                    {
                        _floatingReported = true;
                        _events.Add(new AnalogEvent(_stamp.Time, "solver", AnalogEventKind.Warning,
                            "Devrede referanssız (boşta) kısımlar var; toprağa yüksek dirençle bağlanarak çözüldü."));
                    }
                }

                bool converged = !_stamp.LimitingApplied;
                if (converged && _hasNonlinear)
                {
                    double relTol = Options.RelTol, voltageTol = Options.VoltageTol, currentTol = Options.CurrentTol;
                    for (int i = 0; i < _size; i++)
                    {
                        double a = scratch[i], b = _xNew[i];
                        double tol = relTol * Math.Max(Math.Abs(a), Math.Abs(b)) +
                                     (i < _nodeCount ? voltageTol : currentTol);
                        if (Math.Abs(a - b) > tol)
                        {
                            converged = false;
                            break;
                        }
                    }
                }

                Array.Copy(scratch, _xNew, _size);
                if (!_hasNonlinear) return true;
                if (converged && iterations > 1) return true;
            }

            iterations = maxIterations;
            return false;
        }

        /// <summary>
        /// Ratio of the deviation from linear extrapolation to the allowed error,
        /// evaluated on node voltages. Above 1 the step is too coarse.
        /// </summary>
        /// <summary>
        /// Worst ratio of the estimated local truncation error to the allowed error over
        /// all node voltages. Trapezoidal: LTE ≈ h³·x‴/12 from the third divided difference
        /// of the last four points; backward Euler: LTE ≈ h²·x″/2 from the second.
        /// </summary>
        private double EstimateErrorRatio(double h, bool trapezoidal)
        {
            double worst = 0.0;
            double relTol = Options.ErrorRelTol, absTol = Options.ErrorAbsTol;
            double h1 = _hPrev, h2 = _hPrev2;
            bool third = trapezoidal && h2 > 0.0 && _stepsSinceDiscontinuity >= 3;
            for (int i = 0; i < _nodeCount; i++)
            {
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

        private double NextBreakpoint(double time)
        {
            double next = double.PositiveInfinity;
            foreach (var device in _devices)
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
                _signals[_netSignalKeys[n]] = _x[n - 1];
            }

            _signalKeys.Rewind();
            foreach (var device in _devices) device.Report(this);
        }

        private string DescribeSingular(int index)
        {
            if (index >= 0 && index < _nodeCount)
            {
                return $"Devre matrisi '{_circuit.NodeNames[index + 1]}' düğümünde tekil (çözülemiyor).";
            }

            foreach (var device in _devices)
            {
                if (device.BranchBase >= 0 && index >= device.BranchBase && index < device.BranchBase + device.BranchCount)
                {
                    return $"Devre matrisi '{device.Id}' parçasında tekil (çözülemiyor); " +
                           "büyük olasılıkla gerilim kaynakları ya da bobinler bir döngü oluşturuyor.";
                }
            }

            return "Devre matrisi tekil (çözülemiyor).";
        }
    }
}
