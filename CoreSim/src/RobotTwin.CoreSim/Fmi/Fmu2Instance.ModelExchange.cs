using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace RobotTwin.CoreSim.Fmi
{
    /// <summary>
    /// [fmi-me] A model-exchange FMU solved by our own integrator: the FMU gives its states' derivatives and its event
    /// indicators, an adaptive Dormand–Prince 5(4) method integrates the states (relative and absolute tolerances scaled by
    /// the states' nominal values). Time events (the FMU's next event time), state events (an event indicator changing
    /// sign, located by bisection to a picosecond) and step events (fmi2CompletedIntegratorStep) are handled as FMI 2.0
    /// asks: event mode, the discrete states iterated until they settle, continuous-time mode again. Inputs hold over a
    /// communication step, as with a co-simulation FMU.
    /// </summary>
    public sealed partial class Fmu2Instance
    {
        private const int Fmi2ModelExchange = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct EventInfo
        {
            public int NewDiscreteStatesNeeded;
            public int TerminateSimulation;
            public int NominalsOfContinuousStatesChanged;
            public int ValuesOfContinuousStatesChanged;
            public int NextEventTimeDefined;
            public double NextEventTime;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int SetTimeFn(IntPtr c, double time);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ArrayFn(IntPtr c, [In, Out] double[] values, UIntPtr n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int NewDiscreteStatesFn(IntPtr c, ref EventInfo info);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CompletedIntegratorStepFn(IntPtr c, int noSetFmuStatePriorToCurrentPoint, out int enterEventMode, out int terminateSimulation);

        private SetTimeFn? _setTime;
        private ArrayFn? _setStates, _getStates, _getDerivatives, _getIndicators, _getNominals;
        private Simple? _enterEventMode, _enterContinuous;
        private NewDiscreteStatesFn? _newDiscreteStates;
        private CompletedIntegratorStepFn? _completedStep;

        private double[] _x = Array.Empty<double>(), _z = Array.Empty<double>(), _nominal = Array.Empty<double>();
        private double _nextEvent = double.PositiveInfinity, _h;

        /// <summary>[fmi-me] Run by our integrator (model exchange) rather than the FMU's own solver.</summary>
        public bool ModelExchange { get; }

        /// <summary>[fmi-me] The integrator's tolerances (relative; absolute, times each state's nominal value).</summary>
        public double RelativeTolerance { get; set; } = 1e-6;

        public double AbsoluteTolerance { get; set; } = 1e-8;

        /// <summary>[fmi-me] Integrator steps taken and rejected, and the events handled.</summary>
        public long IntegratorSteps { get; private set; }

        public long RejectedSteps { get; private set; }

        public long StateEvents { get; private set; }

        public long TimeEvents { get; private set; }

        /// <summary>[fmi-me] The continuous states now (a copy).</summary>
        public double[] States => (double[])_x.Clone();

        private void BindModelExchange(Func<string, IntPtr> find)
        {
            T Bind<T>(string name) where T : Delegate
            {
                IntPtr p = find(name);
                if (p == IntPtr.Zero) throw new InvalidOperationException($"FMU ikilisinde {name} yok (FMI 2.0 model değişimi arabirimi eksik).");
                return Marshal.GetDelegateForFunctionPointer<T>(p);
            }

            _setTime = Bind<SetTimeFn>("fmi2SetTime");
            _setStates = Bind<ArrayFn>("fmi2SetContinuousStates");
            _getStates = Bind<ArrayFn>("fmi2GetContinuousStates");
            _getDerivatives = Bind<ArrayFn>("fmi2GetDerivatives");
            _getIndicators = Bind<ArrayFn>("fmi2GetEventIndicators");
            _enterEventMode = Bind<Simple>("fmi2EnterEventMode");
            _enterContinuous = Bind<Simple>("fmi2EnterContinuousTimeMode");
            _newDiscreteStates = Bind<NewDiscreteStatesFn>("fmi2NewDiscreteStates");
            _completedStep = Bind<CompletedIntegratorStepFn>("fmi2CompletedIntegratorStep");
            IntPtr nominals = find("fmi2GetNominalsOfContinuousStates");
            _getNominals = nominals == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<ArrayFn>(nominals);
        }

        /// <summary>After initialisation: the first event iteration, continuous-time mode, the states and indicators read.</summary>
        private void StartContinuous()
        {
            int nx = Description.ContinuousStates, nz = Description.EventIndicators;
            _x = new double[nx];
            _z = new double[nz];
            _nominal = Enumerable.Repeat(1.0, nx).ToArray();
            EventIteration();
            Check(_enterContinuous!(_component), "fmi2EnterContinuousTimeMode");
            ReadStates();
            if (_getNominals != null && nx > 0 && _getNominals(_component, _nominal, (UIntPtr)nx) <= 1)
                for (int i = 0; i < nx; i++) _nominal[i] = Math.Abs(_nominal[i]) > 0 ? Math.Abs(_nominal[i]) : 1;
            _h = 0;
        }

        private void ReadStates()
        {
            if (_x.Length > 0) Check(_getStates!(_component, _x, (UIntPtr)_x.Length), "fmi2GetContinuousStates");
            if (_z.Length > 0) Check(_getIndicators!(_component, _z, (UIntPtr)_z.Length), "fmi2GetEventIndicators");
        }

        /// <summary>Event mode's iteration: new discrete states until the model needs none; the next time event it names.</summary>
        private void EventIteration()
        {
            var info = new EventInfo { NewDiscreteStatesNeeded = 1 };
            for (int k = 0; info.NewDiscreteStatesNeeded != 0; k++)
            {
                if (k >= 1000) throw new InvalidOperationException($"{Description.ModelName}: olay yinelemesi durulmadı (1000 kez).");
                info = default;
                Check(_newDiscreteStates!(_component, ref info), "fmi2NewDiscreteStates");
                if (info.TerminateSimulation != 0) throw new InvalidOperationException($"{Description.ModelName}: FMU benzetimin bitmesini istedi.");
            }

            _nextEvent = info.NextEventTimeDefined != 0 ? info.NextEventTime : double.PositiveInfinity;
        }

        /// <summary>An event at <paramref name="time"/> with the states <paramref name="x"/>: event mode, iteration, back to continuous time.</summary>
        private void HandleEvent(double time, double[] x)
        {
            Check(_setTime!(_component, time), "fmi2SetTime");
            if (x.Length > 0) Check(_setStates!(_component, x, (UIntPtr)x.Length), "fmi2SetContinuousStates");
            Check(_enterEventMode!(_component), "fmi2EnterEventMode");
            EventIteration();
            Check(_enterContinuous!(_component), "fmi2EnterContinuousTimeMode");
            ReadStates();   // the event may have reset them (a bounce)
        }

        private double[] Derivatives(double t, double[] x)
        {
            var dx = new double[x.Length];
            Check(_setTime!(_component, t), "fmi2SetTime");
            if (x.Length == 0) return dx;
            Check(_setStates!(_component, x, (UIntPtr)x.Length), "fmi2SetContinuousStates");
            Check(_getDerivatives!(_component, dx, (UIntPtr)dx.Length), "fmi2GetDerivatives");
            return dx;
        }

        private double[] Indicators(double t, double[] x)
        {
            var z = new double[_z.Length];
            Check(_setTime!(_component, t), "fmi2SetTime");
            if (x.Length > 0) Check(_setStates!(_component, x, (UIntPtr)x.Length), "fmi2SetContinuousStates");
            if (z.Length > 0) Check(_getIndicators!(_component, z, (UIntPtr)z.Length), "fmi2GetEventIndicators");
            return z;
        }

        // Dormand–Prince 5(4): the stages' times, coefficients, the 5th-order weights and the error weights (5th − 4th).
        private static readonly double[] C = { 0, 1.0 / 5, 3.0 / 10, 4.0 / 5, 8.0 / 9, 1, 1 };
        private static readonly double[][] A =
        {
            new double[0],
            new[] { 1.0 / 5 },
            new[] { 3.0 / 40, 9.0 / 40 },
            new[] { 44.0 / 45, -56.0 / 15, 32.0 / 9 },
            new[] { 19372.0 / 6561, -25360.0 / 2187, 64448.0 / 6561, -212.0 / 729 },
            new[] { 9017.0 / 3168, -355.0 / 33, 46732.0 / 5247, 49.0 / 176, -5103.0 / 18656 },
            new[] { 35.0 / 384, 0, 500.0 / 1113, 125.0 / 192, -2187.0 / 6784, 11.0 / 84 }
        };
        private static readonly double[] E = { 71.0 / 57600, 0, -71.0 / 16695, 71.0 / 1920, -17253.0 / 339200, 22.0 / 525, -1.0 / 40 };

        /// <summary>One Dormand–Prince step: the 5th-order solution at t + h and the error's norm (≤ 1 is within tolerance).</summary>
        private double[] TryStep(double t, double[] x, double h, out double error)
        {
            int n = x.Length;
            var k = new double[7][];
            var y = new double[n];
            for (int s = 0; s < 7; s++)
            {
                for (int i = 0; i < n; i++)
                {
                    double sum = x[i];
                    for (int j = 0; j < s; j++) sum += h * A[s][j] * k[j][i];
                    y[i] = sum;
                }

                k[s] = Derivatives(t + C[s] * h, y);
            }

            // y is now the 5th-order solution (the last stage is evaluated at it)
            double norm = 0;
            for (int i = 0; i < n; i++)
            {
                double err = 0;
                for (int s = 0; s < 7; s++) err += h * E[s] * k[s][i];
                double scale = AbsoluteTolerance * _nominal[i] + RelativeTolerance * Math.Max(Math.Abs(x[i]), Math.Abs(y[i]));
                norm += (err / scale) * (err / scale);
            }

            error = n > 0 ? Math.Sqrt(norm / n) : 0;
            return (double[])y.Clone();
        }

        private static bool Crossed(double before, double after) => (before > 0) != (after > 0);

        /// <summary>Integrates the states from <see cref="Time"/> to <paramref name="end"/>, handling the events on the way.</summary>
        private void IntegrateTo(double end)
        {
            double t = Time;
            int guard = 0;
            while (t < end - 1e-15 * Math.Max(1, Math.Abs(end)))
            {
                if (++guard > 10_000_000) throw new InvalidOperationException($"{Description.ModelName}: tümleyici ilerlemiyor.");
                double target = Math.Min(end, _nextEvent);
                double span = target - t;
                if (span <= 0)
                {
                    // a time event now
                    TimeEvents++;
                    HandleEvent(t, _x);
                    continue;
                }

                double h = _h > 0 ? Math.Min(_h, span) : Math.Min(span, Math.Max(1e-6, span / 10));
                double[] xNew;
                double error;
                while (true)
                {
                    xNew = TryStep(t, _x, h, out error);
                    if (error <= 1 || h < 1e-13 * Math.Max(1, Math.Abs(t))) break;
                    RejectedSteps++;
                    h *= Math.Max(0.2, 0.9 * Math.Pow(error, -0.2));
                }

                double tNew = t + h;
                if (span - h < 1e-12 * Math.Max(1, Math.Abs(target))) tNew = target;   // land on the target, not a hair before it
                IntegratorSteps++;
                double grow = error > 0 ? Math.Min(5, Math.Max(0.2, 0.9 * Math.Pow(error, -0.2))) : 5;
                if (tNew < target) _h = h * grow;   // a step cut short by the target says nothing about the next one

                var zNew = Indicators(tNew, xNew);
                int crossed = -1;
                for (int i = 0; i < zNew.Length; i++)
                    if (Crossed(_z[i], zNew[i])) crossed = i;
                if (crossed >= 0)
                {
                    // a state event inside the step: bisect to it, then take the state just past it
                    double lo = t, hi = tNew;
                    double[] xHi = xNew;
                    for (int it = 0; it < 60 && hi - lo > 1e-12 * Math.Max(1, Math.Abs(hi)); it++)
                    {
                        double mid = 0.5 * (lo + hi);
                        var xMid = TryStep(t, _x, mid - t, out _);
                        var zMid = Indicators(mid, xMid);
                        if (Enumerable.Range(0, zMid.Length).Any(i => Crossed(_z[i], zMid[i])))
                        {
                            hi = mid;
                            xHi = xMid;
                        }
                        else lo = mid;
                    }

                    StateEvents++;
                    t = hi;
                    HandleEvent(t, xHi);
                    _h = Math.Max(_h * 0.1, 1e-9);   // start gently after the event
                    continue;
                }

                t = tNew;
                _x = xNew;
                _z = zNew;
                if (!Description.CompletedIntegratorStepNotNeeded)
                {
                    Check(_completedStep!(_component, 1, out int enterEvent, out int terminate), "fmi2CompletedIntegratorStep");
                    if (terminate != 0) throw new InvalidOperationException($"{Description.ModelName}: FMU benzetimin bitmesini istedi.");
                    if (enterEvent != 0) HandleEvent(t, _x);   // a step event
                }

                if (t >= _nextEvent - 1e-12 * Math.Max(1, Math.Abs(t)))
                {
                    TimeEvents++;
                    HandleEvent(t, _x);
                }
            }

            // the model left at the end's time and states, so its outputs are read there
            Check(_setTime!(_component, end), "fmi2SetTime");
            if (_x.Length > 0) Check(_setStates!(_component, _x, (UIntPtr)_x.Length), "fmi2SetContinuousStates");
            Time = end;
        }
    }
}
