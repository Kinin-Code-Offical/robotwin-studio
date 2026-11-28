using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>
    /// [split] The circuit in independent parts (<see cref="AnalogOptions.SplitIndependentParts"/>). Two robots in one world
    /// share only the reference: their unknowns never meet in the matrix, yet solved together every edge of one cut the
    /// step of both, and every device of both was stamped at every step either of them needed. Here each part keeps its
    /// own matrix, step size, error history and restarts; all parts meet at the end of every <see cref="Step"/> call.
    ///
    /// The parts come from what the devices touch: every matrix entry and right-hand side they add and every unknown they
    /// read, from the operating point to the end of the first step, and a device's own branches. A device and the ones its
    /// Setup added (a relay's coil and its contacts) are one part, so are the devices of one component (AnalogDevice.Group:
    /// an encoder motor's motor and encoder). Devices that touch nothing share a part of their own.
    /// Later a device may reach into another part (a pin that starts to drive its supply, a switch that closes onto
    /// another net): the stamp of every device that had a discontinuity is checked against its part before the call, and
    /// what still reaches out during it is joined at the start of the next call (<see cref="Rejoins"/>).
    /// </summary>
    public sealed partial class AnalogSimulator
    {
        /// <summary>[split] One part of the circuit solved on its own, or the whole circuit.</summary>
        private sealed class Part
        {
            internal int Id;
            internal int[] Unknowns = Array.Empty<int>();   // the simulator's unknowns: its nodes, ascending, then its branches
            internal int NodeCount;
            internal AnalogDevice[] Devices = Array.Empty<AnalogDevice>();
            internal MnaSystem Mna = null!;
            internal double[] Scratch = Array.Empty<double>();
            internal bool HasNonlinear;
            internal StampContext Stamp = null!;
            internal AcceptContext Accept = null!;
            internal StepStatistics Stats = null!;
            internal List<AnalogEvent> Events = null!;
            internal UnknownWatch? Watch;
            internal Action<double>[]? Observers;
            internal bool FloatingReported;   // [islands] its no-reference warning given
            internal PartSignals? Signals;     // [perf] publishes its own nodes and devices

            // [islands] One reference per piece of the part that has no path to ground (Newton, FindIslandTies).
            internal bool IslandsStale = true;
            internal readonly List<(int Node, double G)> Ties = new List<(int Node, double G)>();

            internal double Time;
            internal double HNext, HPrev, HPrev2;

            /// <summary>
            /// [perf] The step the part was going at before its last discontinuity: a restart begins at a hundredth of it
            /// (never below <see cref="AnalogOptions.RestartStep"/>). A fixed 0.1 µs restart made every edge of a circuit that
            /// otherwise strides along climb back over a dozen steps: the ESP32 quadcopter's circuit took 20 500 steps per
            /// simulated second, 14 200 now.
            /// </summary>
            internal double HCruise;

            internal bool ForceBackwardEuler = true;
            internal int StepsSinceDiscontinuity;

            internal double RestartStep(AnalogOptions o) => Math.Min(Math.Max(o.RestartStep, HCruise * 0.01), o.MaxStep);

            /// <summary>Something changed abruptly: the next step restarts small with backward Euler.</summary>
            internal void Restart(AnalogOptions o)
            {
                IslandsStale = true;
                ForceBackwardEuler = true;
                StepsSinceDiscontinuity = 0;
                HNext = Math.Min(HNext, RestartStep(o));
            }

            internal void CopyPace(Part from)
            {
                Time = from.Time;
                HNext = from.HNext;
                HPrev = from.HPrev;
                HPrev2 = from.HPrev2;
                HCruise = from.HCruise;
                ForceBackwardEuler = from.ForceBackwardEuler;
                StepsSinceDiscontinuity = from.StepsSinceDiscontinuity;
            }
        }

        private int[] _family = Array.Empty<int>();       // the device whose Setup added each one (-1: none)
        private UnknownWatch? _recording;                  // finding the parts: what each device touches
        private Part[]? _parts;                            // null: solved whole
        private int[] _owner = Array.Empty<int>();         // the part of each unknown
        private int[] _devicePart = Array.Empty<int>();    // the part of each device
        private readonly List<(int[] Nodes, Action<double> Call)> _observers = new List<(int[] Nodes, Action<double> Call)>();

        /// <summary>
        /// [split] The parts in lanes: a lane's parts are solved one after the other on one thread, the lanes side by side.
        /// An observer's parts are one lane (it is never called from two threads at once), so are all parts holding a
        /// device that must run alone (<see cref="AnalogDevice.RunsBesideOthers"/>). Heaviest lane first.
        /// </summary>
        private Part[][]? _lanes;
        private Part[]? _aloneLane;   // the parts with a device that runs alone (AnalogDevice.RunsBesideOthers), or null

        // [split] side by side or one after the other, whichever the last measurement found cheaper (the result is the same)
        private const int MeasureCalls = 256, RemeasureEvery = 16384;
        private long _splitCalls;
        private double _costInTurn = double.NaN, _costSideBySide = double.NaN;   // stopwatch ticks per call
        private double _measured;
        private int _measuredCalls;
        private bool _measuringSideBySide;

        /// <summary>[split] Tests: always (true) or never (false) side by side, instead of by measurement.</summary>
        internal bool? ForceSideBySide { get; set; }

        /// <summary>[split] Whether the last call solved the parts side by side on several cores.</summary>
        public bool PartsSideBySide { get; private set; }

        /// <summary>[split] How many parts the circuit is solved in (1: whole).</summary>
        public int PartCount => _parts?.Length ?? 1;

        /// <summary>[split] The part a node is solved in (0 while whole; -1 for the reference).</summary>
        public int PartOf(int node)
        {
            if (node <= 0 || node > _nodeCount) return -1;
            return _parts == null ? 0 : _owner[node - 1];
        }

        /// <summary>[split] How many times parts were joined because a device reached from one into another.</summary>
        public int Rejoins { get; private set; }

        /// <summary>
        /// [split] Calls <paramref name="onSubstep"/> after every accepted step of the part(s) holding one of
        /// <paramref name="nodes"/> (every step while the circuit is solved whole), with the time: a board that time-stamps its
        /// input edges, an instrument that samples at the solver's resolution. Within one part the times rise; an observer
        /// whose nodes lie in two parts hears both, one after the other.
        /// </summary>
        public void Observe(IEnumerable<int> nodes, Action<double> onSubstep)
        {
            if (onSubstep == null) throw new ArgumentNullException(nameof(onSubstep));
            _observers.Add(((nodes ?? Array.Empty<int>()).Distinct().ToArray(), onSubstep));
            AssignObservers();
        }

        /// <summary>[split] Forgets every <see cref="Observe"/>.</summary>
        public void ClearObservers()
        {
            _observers.Clear();
            AssignObservers();
        }

        private void AssignObservers()
        {
            _whole.Observers = _observers.Count == 0 ? null : _observers.Select(o => o.Call).ToArray();
            if (_parts == null) return;
            var lists = new List<Action<double>>?[_parts.Length];
            foreach (var (nodes, call) in _observers)
            {
                var at = new HashSet<int>();
                foreach (int n in nodes)
                    if (n > 0 && n <= _nodeCount) at.Add(_owner[n - 1]);
                if (at.Count == 0)
                    for (int p = 0; p < _parts.Length; p++) at.Add(p);   // no node of the circuit: hears everything
                foreach (int p in at.OrderBy(p => p)) (lists[p] ??= new List<Action<double>>()).Add(call);
            }

            for (int p = 0; p < _parts.Length; p++) _parts[p].Observers = lists[p]?.ToArray();
            BuildLanes();
        }

        private void BuildLanes()
        {
            var parts = _parts!;
            var join = new int[parts.Length];
            for (int i = 0; i < join.Length; i++) join[i] = i;
            int Root(int x)
            {
                while (join[x] != x)
                {
                    join[x] = join[join[x]];
                    x = join[x];
                }

                return x;
            }

            void Join(int a, int b)
            {
                a = Root(a);
                b = Root(b);
                if (a != b) join[Math.Max(a, b)] = Math.Min(a, b);
            }

            foreach (var (nodes, _) in _observers)
            {
                int first = -1;
                foreach (int n in nodes)
                {
                    if (n <= 0 || n > _nodeCount) continue;
                    int p = _owner[n - 1];
                    if (first < 0) first = p;
                    else Join(first, p);
                }

                if (first < 0)
                    for (int p = 1; p < parts.Length; p++) Join(0, p);   // hears every part: one lane
            }

            int alone = -1;
            foreach (var p in parts)
            {
                if (p.Devices.All(d => d.RunsBesideOthers)) continue;
                if (alone < 0) alone = p.Id;
                else Join(alone, p.Id);
            }

            var lanes = parts.GroupBy(p => Root(p.Id)).Select(g => g.OrderBy(p => p.Id).ToArray())
                .OrderByDescending(l => l.Sum(p => p.Devices.Length + p.Unknowns.Length)).ThenBy(l => l[0].Id).ToList();
            // the lane of the devices that run alone goes on the caller's thread, after the others (native code may be bound
            // to the thread that loaded it)
            _aloneLane = alone < 0 ? null : lanes.First(l => l.Any(p => Root(p.Id) == Root(alone)));
            if (_aloneLane != null) lanes.Remove(_aloneLane);
            _lanes = lanes.ToArray();
        }

        /// <summary>From <see cref="Initialize"/>: watch what every device touches until the end of the first step.</summary>
        private void StartRecording()
        {
            _parts = null;
            _lanes = null;
            _aloneLane = null;
            _splitCalls = 0;
            _costInTurn = _costSideBySide = double.NaN;
            _measured = 0;
            _measuredCalls = 0;
            _owner = Array.Empty<int>();
            _devicePart = Array.Empty<int>();
            _recording = Options.SplitIndependentParts ? UnknownWatch.Recorder(_size, _devices.Length) : null;
            _mna.Watch = _recording;
            _stamp.Watch = _recording;
            _accept.Watch = _recording;
            AssignObservers();
        }

        /// <summary>After the first step: the parts from what the devices touched; the circuit stays whole when it is one.</summary>
        private void Split()
        {
            var w = _recording!;
            _recording = null;
            _mna.Watch = null;
            _stamp.Watch = null;
            _accept.Watch = null;
            int n = _size, d = _devices.Length;
            if (n == 0 || d < 2) return;

            // a device's branches are its own, even one nothing else reads
            for (int i = 0; i < d; i++)
            {
                var dev = _devices[i];
                for (int k = 0; k < dev.BranchCount; k++) w.TouchBy(i, dev.BranchBase + k);
            }

            // a family is one part
            var group = new int[d];
            for (int i = 0; i < d; i++) group[i] = i;
            int Group(int x)
            {
                while (group[x] != x)
                {
                    group[x] = group[group[x]];
                    x = group[x];
                }

                return x;
            }

            void Together(int x, int y)
            {
                int a = Group(x), b = Group(y);
                if (a != b) group[Math.Max(a, b)] = Math.Min(a, b);
            }

            var firstOfComponent = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < d; i++)
            {
                if (_family[i] >= 0) Together(i, _family[i]);
                // the devices of one component (an encoder motor: the motor and the encoder that reads its shaft)
                if (_devices[i].Group is { } component)
                {
                    if (firstOfComponent.TryGetValue(component, out int first)) Together(i, first);
                    else firstOfComponent[component] = i;
                }
            }

            var groupFirst = Enumerable.Repeat(-1, d).ToArray();
            for (int i = 0; i < d; i++)
            {
                int f = w.First![i];
                if (f < 0) continue;
                int r = Group(i);
                if (groupFirst[r] < 0) groupFirst[r] = f;
                else w.Union(groupFirst[r], f);
            }

            // the parts: unknowns joined to one another that some device works on, numbered by their lowest unknown
            var worked = new bool[n];
            for (int i = 0; i < d; i++)
            {
                int f = groupFirst[Group(i)];
                if (f >= 0) worked[w.Find(f)] = true;
            }

            const int Idle = -2;
            var partOfRoot = Enumerable.Repeat(-1, n).ToArray();
            var owner = new int[n];
            int count = 0;
            for (int u = 0; u < n; u++)
            {
                int r = w.Find(u);
                if (!worked[r])
                {
                    owner[u] = Idle;
                    continue;
                }

                if (partOfRoot[r] < 0) partOfRoot[r] = count++;
                owner[u] = partOfRoot[r];
            }

            if (count < 2) return;   // one part: nothing to gain, the circuit stays whole

            var devicePart = new int[d];
            bool idle = owner.Contains(Idle);
            for (int i = 0; i < d; i++)
            {
                int f = groupFirst[Group(i)];
                devicePart[i] = f >= 0 ? owner[f] : Idle;
                if (devicePart[i] == Idle) idle = true;
            }

            // what no device works on, and the devices that touch nothing (a device that only keeps time): a part of their own
            if (idle)
            {
                int id = count++;
                for (int u = 0; u < n; u++)
                    if (owner[u] == Idle) owner[u] = id;
                for (int i = 0; i < d; i++)
                    if (devicePart[i] == Idle) devicePart[i] = id;
            }

            var parts = BuildParts(owner, devicePart, count);
            foreach (var p in parts) p.CopyPace(_whole);   // the history is per unknown: every part goes on at the whole circuit's pace
            _parts = parts;
            AssignObservers();
        }

        private Part[] BuildParts(int[] owner, int[] devicePart, int count)
        {
            var nodes = new List<int>[count];
            var branches = new List<int>[count];
            var devices = new List<AnalogDevice>[count];
            for (int p = 0; p < count; p++)
            {
                nodes[p] = new List<int>();
                branches[p] = new List<int>();
                devices[p] = new List<AnalogDevice>();
            }

            for (int u = 0; u < _size; u++) (u < _nodeCount ? nodes : branches)[owner[u]].Add(u);
            for (int i = 0; i < _devices.Length; i++) devices[devicePart[i]].Add(_devices[i]);

            var local = new int[_size];
            var parts = new Part[count];
            for (int p = 0; p < count; p++)
            {
                var unknowns = nodes[p].Concat(branches[p]).ToArray();
                for (int j = 0; j < unknowns.Length; j++) local[unknowns[j]] = j;
                var watch = UnknownWatch.ForPart(owner, local, p);
                var mna = new MnaSystem { Watch = watch, UseDenseSolver = _mna.UseDenseSolver };
                mna.Resize(unknowns.Length);
                parts[p] = new Part
                {
                    Signals = new PartSignals(this, unknowns, nodes[p].Count, devices[p].ToArray()),
                    Id = p,
                    Unknowns = unknowns,
                    NodeCount = nodes[p].Count,
                    Devices = devices[p].ToArray(),
                    Mna = mna,
                    Scratch = new double[unknowns.Length],
                    HasNonlinear = devices[p].Any(x => x.IsNonlinear),
                    Stamp = new StampContext { Watch = watch },
                    Accept = new AcceptContext { Watch = watch },
                    Stats = new StepStatistics(),
                    Events = new List<AnalogEvent>(),
                    Watch = watch
                };
            }

            _owner = owner;
            _devicePart = devicePart;
            return parts;
        }

        /// <summary>One <see cref="Step"/> of the split circuit: every part from <see cref="Time"/> to <paramref name="tEnd"/>.</summary>
        private void StepParts(double tEnd, double timeEps, double dt)
        {
            if (Reached()) Rejoin();   // a device reached into another part during the last call
            if (_parts == null)
            {
                StepWhole(tEnd, timeEps, dt);
                return;
            }

            // the stamp of every device that changed abruptly is checked against its part first: a pin that now drives its
            // supply, a switch that closed onto another net joins the two before they are solved apart
            bool probed = false;
            foreach (var p in _parts!)
            {
                p.Stats.Reset();
                foreach (var device in p.Devices)
                {
                    if (!device.ConsumeDiscontinuity()) continue;
                    p.Restart(Options);
                    Probe(p, device);
                    probed = true;
                }
            }

            if (probed && Reached()) Rejoin();
            if (_parts == null)
            {
                _whole.Time = Time;
                Advance(_whole, tEnd, timeEps, dt);   // joined into one: whole again (its discontinuities already taken)
                return;
            }

            var parts = _parts;
            bool sideBySide = SideBySide();
            long started = Stopwatch.GetTimestamp();
            if (sideBySide)
            {
                double from = Time;
                var lanes = _lanes!;
                WorkTeam.Each(lanes.Length, l =>
                {
                    foreach (var p in lanes[l])
                    {
                        p.Time = from;
                        Advance(p, tEnd, timeEps, dt);
                        p.Signals!.Publish(false);
                    }
                });
                if (_aloneLane != null)
                    foreach (var p in _aloneLane)
                    {
                        p.Time = from;
                        Advance(p, tEnd, timeEps, dt);
                        p.Signals!.Publish(false);
                    }
            }
            else
            {
                foreach (var p in parts)
                {
                    p.Time = Time;
                    Advance(p, tEnd, timeEps, dt);
                    p.Signals!.Publish(false);
                }
            }

            // a signal not where it was last time (a new one, a device that reports differently now): that part again, in
            // turn, looking its names up
            foreach (var p in parts)
                if (p.Signals!.Missed) p.Signals.Publish(true);

            Measured(sideBySide, Stopwatch.GetTimestamp() - started);
            PartsSideBySide = sideBySide;

            List<AnalogEvent>? added = null;
            foreach (var p in parts)
            {
                if (p.FloatingReported) _floatingReported = true;
                Statistics.Add(p.Stats);
                if (p.Events.Count == 0) continue;
                (added ??= new List<AnalogEvent>()).AddRange(p.Events);
                p.Events.Clear();
            }

            if (added != null) _events.AddRange(added.Count > 1 ? added.OrderBy(e => e.Time) : (IEnumerable<AnalogEvent>)added);   // stable: a part's own order kept
        }

        /// <summary>
        /// [split] Side by side when there are lanes to share and nobody listens to every step (<see cref="SubstepAccepted"/>
        /// is called from one thread), and the last measurement found it cheaper: handing small parts to other cores costs
        /// more than it saves. The way not in use is measured again now and then, as the circuit's load changes.
        /// </summary>
        private bool SideBySide()
        {
            if (_lanes == null || _lanes.Length + (_aloneLane != null ? 1 : 0) < 2 || SubstepAccepted != null || WorkTeam.Shared.Workers == 0) return false;
            if (ForceSideBySide is { } forced) return forced;
            if (double.IsNaN(_costInTurn)) return false;
            if (double.IsNaN(_costSideBySide)) return true;
            bool better = _costSideBySide < _costInTurn;
            return _splitCalls % RemeasureEvery < MeasureCalls ? !better : better;
        }

        private void Measured(bool sideBySide, long ticks)
        {
            _splitCalls++;
            if (sideBySide != _measuringSideBySide)
            {
                _measured = 0;
                _measuredCalls = 0;
                _measuringSideBySide = sideBySide;
            }

            _measured += ticks;
            if (++_measuredCalls < MeasureCalls) return;
            if (sideBySide) _costSideBySide = _measured / _measuredCalls;
            else _costInTurn = _measured / _measuredCalls;
            _measured = 0;
            _measuredCalls = 0;
        }

        /// <summary>A device's stamp against its part: what it adds is only checked, nothing written.</summary>
        private void Probe(Part p, AnalogDevice device)
        {
            var w = p.Watch!;
            ConfigureStamp(p, p.Time, Math.Min(p.HNext, Options.MaxStep), false, false, Options.Gmin, 1.0);
            p.Stamp.X = _x;
            p.Stamp.LimitingApplied = false;
            w.Probe = true;
            try
            {
                device.Stamp(p.Stamp);
            }
            finally
            {
                w.Probe = false;
            }
        }

        private bool Reached()
        {
            foreach (var p in _parts!)
                if (p.Watch!.Reached.Count > 0) return true;
            return false;
        }

        /// <summary>Joins every part a device reached into with the device's own; all parts are at <see cref="Time"/>.</summary>
        private void Rejoin()
        {
            var old = _parts!;
            var join = new int[old.Length];
            for (int i = 0; i < join.Length; i++) join[i] = i;
            int Root(int x)
            {
                while (join[x] != x)
                {
                    join[x] = join[join[x]];
                    x = join[x];
                }

                return x;
            }

            foreach (var p in old)
            {
                foreach (int u in p.Watch!.Reached)
                {
                    int a = Root(p.Id), b = Root(_owner[u]);
                    if (a != b) join[Math.Max(a, b)] = Math.Min(a, b);
                }

                p.Watch.Reached.Clear();
            }

            var renumber = Enumerable.Repeat(-1, old.Length).ToArray();
            int count = 0;
            for (int i = 0; i < old.Length; i++)
                if (renumber[Root(i)] < 0) renumber[Root(i)] = count++;
            Rejoins++;

            var members = new List<Part>[count];
            for (int i = 0; i < count; i++) members[i] = new List<Part>();
            foreach (var p in old) members[renumber[Root(p.Id)]].Add(p);

            void Joined(Part into, List<Part> from)
            {
                if (from.Count == 1)
                {
                    into.CopyPace(from[0]);
                    into.IslandsStale = from[0].IslandsStale;
                    return;
                }

                // histories of different parts are not one: the joined part starts again, at the slowest pace of its members
                into.Time = Time;
                into.HNext = from.Min(m => m.HNext);
                into.HCruise = from.Select(m => m.HCruise).Where(h => h > 0).DefaultIfEmpty(0).Min();
                into.HPrev = into.HPrev2 = 0;
                into.Restart(Options);
            }

            if (count == 1)
            {
                Joined(_whole, members[0]);
                _parts = null;
                _lanes = null;
                _aloneLane = null;
                AssignObservers();
                return;
            }

            var owner = _owner.Select(o => renumber[Root(o)]).ToArray();
            var devicePart = _devicePart.Select(o => renumber[Root(o)]).ToArray();
            var parts = BuildParts(owner, devicePart, count);
            for (int i = 0; i < count; i++) Joined(parts[i], members[i]);
            _parts = parts;
            AssignObservers();
        }

        /// <summary>
        /// [perf] A part's own publishing: its node voltages and its devices' reports, by slot. It may run beside other
        /// parts' (each has its own positional cache); a signal not found at its position is not looked up there, the part
        /// is published again afterwards, in turn, with names (<see cref="Missed"/>).
        /// </summary>
        private sealed class PartSignals : ISignalSink
        {
            private readonly AnalogSimulator _sim;
            private readonly int[] _unknowns;
            private readonly int _nodes;
            private readonly AnalogDevice[] _devices;
            private readonly SignalKeyCache _keys = new SignalKeyCache();
            private bool _lookUp;

            internal PartSignals(AnalogSimulator sim, int[] unknowns, int nodes, AnalogDevice[] devices)
            {
                _sim = sim;
                _unknowns = unknowns;
                _nodes = nodes;
                _devices = devices;
            }

            internal bool Missed { get; private set; }

            internal void Publish(bool lookUp)
            {
                var table = _sim._signals;
                var slots = _sim._netSlots;
                var x = _sim._x;
                for (int i = 0; i < _nodes; i++) table.Set(slots[_unknowns[i] + 1], x[_unknowns[i]]);
                _lookUp = lookUp;
                Missed = false;
                _keys.Rewind();
                foreach (var device in _devices) device.Report(this);
            }

            void ISignalSink.Write(string deviceId, string key, double value)
            {
                if (_lookUp)
                {
                    _sim._signals.Set(_keys.Slot(deviceId, key, _sim._signals), value);
                    return;
                }

                if (_keys.TrySlot(deviceId, key, out int slot)) _sim._signals.Set(slot, value);
                else Missed = true;
            }
        }

        /// <summary>[ac] The stamps the AC analysis set up are the next real step's to set again.</summary>
        private void ForceBackwardEulerEverywhere()
        {
            _whole.ForceBackwardEuler = true;
            if (_parts != null)
                foreach (var p in _parts) p.ForceBackwardEuler = true;
        }
    }

    /// <summary>
    /// [split] What a device touches. While the parts are being found (<see cref="Recorder"/>) it joins every unknown a
    /// device adds to or reads with the others the same device touched. While a part is solved (<see cref="ForPart"/>) it
    /// takes each addition to the part's own numbering and keeps what lies outside the part (<see cref="Reached"/>): such
    /// an addition is dropped, such a read sees the other part as it stands.
    /// </summary>
    internal sealed class UnknownWatch
    {
        private readonly int[]? _parent;   // recording: union-find over the unknowns
        internal readonly int[]? First;    // recording: each device's first unknown (-1: none yet)
        internal int Device = -1;          // recording: the device stamping or reading
        private readonly int[] _owner;
        private readonly int[] _local;
        private readonly int _part;
        internal bool Probe;               // a stamp only checked: nothing is written
        internal readonly List<int> Reached = new List<int>();

        private UnknownWatch(int[]? parent, int[]? first, int[] owner, int[] local, int part)
        {
            _parent = parent;
            First = first;
            _owner = owner;
            _local = local;
            _part = part;
        }

        internal static UnknownWatch Recorder(int size, int devices)
        {
            var parent = new int[size];
            for (int i = 0; i < size; i++) parent[i] = i;
            return new UnknownWatch(parent, Enumerable.Repeat(-1, devices).ToArray(), Array.Empty<int>(), Array.Empty<int>(), -1);
        }

        internal static UnknownWatch ForPart(int[] owner, int[] local, int part) => new UnknownWatch(null, null, owner, local, part);

        /// <summary>An entry (row, col), both unknowns: false when it is not to be written.</summary>
        internal bool Map(ref int row, ref int col)
        {
            if (First != null)
            {
                Touch(row);
                Touch(col);
                return true;
            }

            bool outside = _owner[row] != _part && Outside(row);
            outside |= _owner[col] != _part && Outside(col);   // both ends kept: each part it reaches is joined
            if (outside || Probe) return false;
            row = _local[row];
            col = _local[col];
            return true;
        }

        /// <summary>A right-hand side.</summary>
        internal bool Map(ref int row)
        {
            if (First != null)
            {
                Touch(row);
                return true;
            }

            if (_owner[row] != _part && Outside(row)) return false;
            if (Probe) return false;
            row = _local[row];
            return true;
        }

        /// <summary>A conductance between two unknowns, either of them the reference (negative).</summary>
        internal bool MapPair(ref int a, ref int b)
        {
            if (First != null)
            {
                if (a >= 0) Touch(a);
                if (b >= 0) Touch(b);
                return true;
            }

            bool outside = a >= 0 && _owner[a] != _part && Outside(a);
            outside |= b >= 0 && _owner[b] != _part && Outside(b);
            if (outside || Probe) return false;
            if (a >= 0) a = _local[a];
            if (b >= 0) b = _local[b];
            return true;
        }

        internal void Read(int unknown)
        {
            if (First != null) Touch(unknown);
            else if (_owner[unknown] != _part) Outside(unknown);
        }

        /// <summary>Keeps an unknown outside the part; always true (it is outside).</summary>
        private bool Outside(int unknown)
        {
            if (Reached.Count < 256) Reached.Add(unknown);
            return true;
        }

        private void Touch(int u)
        {
            if (Device >= 0) TouchBy(Device, u);
        }

        internal void TouchBy(int device, int u)
        {
            int f = First![device];
            if (f < 0) First[device] = u;
            else Union(f, u);
        }

        internal int Find(int x)
        {
            var p = _parent!;
            while (p[x] != x)
            {
                p[x] = p[p[x]];
                x = p[x];
            }

            return x;
        }

        internal void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) _parent![Math.Max(a, b)] = Math.Min(a, b);
        }
    }
}
