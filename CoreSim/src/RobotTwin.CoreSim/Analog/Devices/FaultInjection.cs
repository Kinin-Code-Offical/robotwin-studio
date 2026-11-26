using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// [faults] When a fault is on: one window from <see cref="Start"/> for <see cref="Duration"/> (0 = for good), or short
    /// windows of <see cref="Width"/> arriving at random (a Poisson process of <see cref="Rate"/> per second, the way
    /// interference and a loose contact strike) inside that span. Seeded, so a run repeats exactly.
    /// </summary>
    public sealed class FaultSchedule
    {
        private readonly List<(double On, double Off)> _windows = new List<(double, double)>();
        private Random _random;
        private double _horizon;   // windows are known up to here

        public FaultSchedule(bool random, double rate, double width, double start, double duration, int seed)
        {
            Random = random;
            Rate = Math.Max(0, rate);
            Width = Math.Max(1e-9, width);
            Start = Math.Max(0, start);
            Duration = Math.Max(0, duration);
            Seed = seed;
            _random = new Random(seed);
            _horizon = Start;
            if (!random) _windows.Add((Start, Duration > 0 ? Start + Duration : double.PositiveInfinity));
        }

        public bool Random { get; }
        public double Rate { get; }
        public double Width { get; }
        public double Start { get; }
        public double Duration { get; }
        public int Seed { get; }

        private double End => Duration > 0 ? Start + Duration : double.PositiveInfinity;

        private void Extend(double until)
        {
            if (!Random || Rate <= 0) return;
            while (_horizon <= until && _horizon < End)
            {
                // the next strike: exponential gaps, never overlapping the last
                double on = _horizon + -Math.Log(1 - _random.NextDouble()) / Rate;
                if (on >= End)
                {
                    _horizon = End;
                    break;
                }

                double off = Math.Min(End, on + Width);
                _windows.Add((on, off));
                _horizon = off;
            }
        }

        /// <summary>The simulation has reached <paramref name="t"/>: windows well behind it are not needed again.</summary>
        private void Forget(double t)
        {
            int drop = 0;
            while (drop < _windows.Count - 1 && _windows[drop].Off < t - 1e-3) drop++;
            if (drop > 0) _windows.RemoveRange(0, drop);
        }

        /// <summary>Whether the fault is on at <paramref name="t"/>.</summary>
        public bool Active(double t)
        {
            Extend(t + Width);
            Forget(t);
            foreach (var (on, off) in _windows)
            {
                if (t < on) return false;
                if (t < off) return true;
            }

            return false;
        }

        /// <summary>The next time after <paramref name="t"/> the fault turns on or off (infinity: never).</summary>
        public double Next(double t)
        {
            Extend(t + Math.Max(Width, Rate > 0 ? 10 / Rate : 0));
            foreach (var (on, off) in _windows)
            {
                if (on > t) return on;
                if (off > t) return off;
            }

            return double.PositiveInfinity;
        }

        /// <summary>Back to the first strike.</summary>
        public void Restart()
        {
            _random = new Random(Seed);
            _windows.Clear();
            _horizon = Start;
            if (!Random) _windows.Add((Start, End));
        }

        /// <summary>The windows between two times (for tests and the scope).</summary>
        public List<(double On, double Off)> Windows(double from, double to)
        {
            Extend(to);
            var list = new List<(double, double)>();
            foreach (var w in _windows)
                if (w.Off > from && w.On < to) list.Add(w);
            return list;
        }
    }

    /// <summary>
    /// [faults] A fault on a wire as the circuit feels it: a contact between two nodes that the schedule closes (a short
    /// from the line to a rail: interference that drags it low or high, a line stuck at a level) or opens (a wire in series
    /// that loses contact). The solver lands on every switching instant, so a 2 µs glitch is 2 µs long, and what the
    /// line does then is up to the circuit: a strong driver may hold its level, the receiver's filter may reject it.
    /// </summary>
    public sealed class FaultSwitch : AnalogDevice
    {
        private bool _faulted;

        public FaultSwitch(string id, int a, int b, FaultSchedule schedule, bool inSeries) : base(id)
        {
            A = a;
            B = b;
            Schedule = schedule;
            InSeries = inSeries;
        }

        public int A { get; }
        public int B { get; }
        public FaultSchedule Schedule { get; }

        /// <summary>True: a wire that conducts until the fault opens it. False: a short that closes during the fault.</summary>
        public bool InSeries { get; }

        /// <summary>Resistance of the contact when it conducts (the fault's short, or the sound wire).</summary>
        public double OnResistance { get; set; } = 1.0;

        public double OffResistance { get; set; } = 1e9;

        /// <summary>The fault is on now.</summary>
        public bool Faulted => _faulted;

        /// <summary>Faults that have struck so far.</summary>
        public int Strikes { get; private set; }

        public double Current { get; private set; }

        private bool Conducts => InSeries ? !_faulted : _faulted;

        public override void BeginStep(StepInfo step)
        {
            if (step.IsDc) return;
            bool now = Schedule.Active(step.Time - step.Dt * 0.5);
            if (now == _faulted) return;
            _faulted = now;
            if (now) Strikes++;
            PendingDiscontinuity = true;
        }

        public override double NextBreakpoint(double time) => Schedule.Next(time);

        public override void Stamp(StampContext ctx) => ctx.AddConductance(A, B, 1.0 / (Conducts ? OnResistance : OffResistance));

        public override void Accept(AcceptContext ctx) => Current = ctx.V(A, B) / (Conducts ? OnResistance : OffResistance);

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "FAULT", _faulted ? 1.0 : 0.0);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "COUNT", Strikes);
        }

        public override void Reset()
        {
            _faulted = false;
            Strikes = 0;
            Schedule.Restart();
        }
    }
}
