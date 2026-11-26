using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [supply] The world's power-up and brown-out report. Every part with a datasheet supply range
    /// (<see cref="SupplyRatings"/>) has its supply pin watched against its own ground pin through the run: when it
    /// first came within its range (the power-up order), each time it then fell below its minimum (a brown-out: when,
    /// for how long, how deep) or rose above its maximum, and the parts that never got enough. The supply a part sees is
    /// the circuit's own: a battery's internal resistance, a thin cable, a regulator's dropout or a motor's starting
    /// current pulling the rail down all show up where they happen.
    /// </summary>
    public sealed class SupplyMonitor
    {
        public enum EventKind
        {
            Ready,
            BrownOut,
            Recovered,
            OverVoltage
        }

        public readonly struct Event
        {
            public Event(double time, Watch watch, EventKind kind, double volts, double duration = 0)
            {
                Time = time;
                Watch = watch;
                Kind = kind;
                Volts = volts;
                Duration = duration;
            }

            public double Time { get; }
            public Watch Watch { get; }
            public EventKind Kind { get; }

            /// <summary>The supply then (V): at a recovery, the lowest it went; at an over-voltage, the highest.</summary>
            public double Volts { get; }

            /// <summary>How long a brown-out lasted (s), at its recovery.</summary>
            public double Duration { get; }
        }

        /// <summary>One watched supply pin.</summary>
        public sealed class Watch
        {
            internal int Supply, Ground;
            internal bool Low, High;
            internal double DipStart, DipMin, Peak;

            public string ObjectId { get; internal set; } = "";
            public string Name { get; internal set; } = "";
            public SupplyRating Rating { get; internal set; } = null!;

            /// <summary>When it first came within its range (s); NaN while it never has.</summary>
            public double ReadyAt { get; internal set; } = double.NaN;

            public double MinSeen { get; internal set; } = double.PositiveInfinity;
            public double MaxSeen { get; internal set; } = double.NegativeInfinity;

            /// <summary>The lowest it fell after it was up (V); NaN when it never fell below its minimum.</summary>
            public double DeepestDip { get; internal set; } = double.NaN;

            public int BrownOuts { get; internal set; }
            public double LongestBrownOut { get; internal set; }

            public bool Ready => !double.IsNaN(ReadyAt);
        }

        private readonly List<Watch> _watches = new List<Watch>();
        private readonly List<Event> _events = new List<Event>();

        public IReadOnlyList<Watch> Watches => _watches;

        public IReadOnlyList<Event> Events => _events;

        /// <summary>Raised for every event as it happens (the run's log shows brown-outs when they occur).</summary>
        public event Action<Event>? Occurred;

        /// <summary>The supply pins of every rated part in the world, as compiled into its circuit.</summary>
        public static SupplyMonitor Build(WorldDocument world, WorldCircuit circuit)
        {
            var m = new SupplyMonitor();
            foreach (var o in world.Objects.Where(o => o.Kind == WorldObjectKind.Component))
            {
                foreach (var r in SupplyRatings.Of(o))
                {
                    int s = circuit.NodeOf(new CableEnd { ObjectId = o.Id, Pin = r.Pin });
                    int g = circuit.NodeOf(new CableEnd { ObjectId = o.Id, Pin = r.Ground });
                    if (s < 0 || g < 0) continue;
                    m._watches.Add(new Watch { ObjectId = o.Id, Name = string.IsNullOrWhiteSpace(o.Name) ? o.Id : o.Name, Rating = r, Supply = s, Ground = g });
                }
            }

            return m;
        }

        /// <summary>Reads every watched supply at <paramref name="time"/>.</summary>
        public void Sample(double time, AnalogSimulator sim)
        {
            foreach (var w in _watches) Sample(w, time, sim.NodeVoltage(w.Supply) - sim.NodeVoltage(w.Ground));
        }

        /// <summary>A reading of one watched supply taken elsewhere (a recorded run, a test).</summary>
        public void Record(Watch w, double time, double v) => Sample(w, time, v);

        private void Sample(Watch w, double time, double v)
        {
            if (double.IsNaN(v)) return;
            w.MinSeen = Math.Min(w.MinSeen, v);
            w.MaxSeen = Math.Max(w.MaxSeen, v);
            var r = w.Rating;
            if (!w.Ready)
            {
                if (v >= r.Min) Raise(new Event(time, w, EventKind.Ready, v), () => w.ReadyAt = time);
            }
            else if (!w.Low && v < r.Min)
            {
                w.Low = true;
                w.DipStart = time;
                w.DipMin = v;
                w.BrownOuts++;
                Raise(new Event(time, w, EventKind.BrownOut, v));
            }
            else if (w.Low)
            {
                w.DipMin = Math.Min(w.DipMin, v);
                if (v >= r.Min)
                {
                    w.Low = false;
                    double d = time - w.DipStart;
                    w.LongestBrownOut = Math.Max(w.LongestBrownOut, d);
                    w.DeepestDip = double.IsNaN(w.DeepestDip) ? w.DipMin : Math.Min(w.DeepestDip, w.DipMin);
                    Raise(new Event(time, w, EventKind.Recovered, w.DipMin, d));
                }
            }

            if (!w.High && v > r.Max)
            {
                w.High = true;
                w.Peak = v;
                Raise(new Event(time, w, EventKind.OverVoltage, v));
            }
            else if (w.High)
            {
                w.Peak = Math.Max(w.Peak, v);
                if (v <= r.Max) w.High = false;
            }
        }

        private void Raise(Event e, Action? before = null)
        {
            before?.Invoke();
            _events.Add(e);
            Occurred?.Invoke(e);
        }

        /// <summary>A brown-out still under way at the end of the run counts until then.</summary>
        public void Finish(double time)
        {
            foreach (var w in _watches.Where(w => w.Low))
            {
                w.LongestBrownOut = Math.Max(w.LongestBrownOut, time - w.DipStart);
                w.DeepestDip = double.IsNaN(w.DeepestDip) ? w.DipMin : Math.Min(w.DeepestDip, w.DipMin);
            }
        }

        private static string V(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Ms(double s) => (s * 1000).ToString(s < 0.01 ? "0.00" : "0", CultureInfo.InvariantCulture);

        /// <summary>A line for an event, as the run's log shows it.</summary>
        public static string Describe(Event e)
        {
            var w = e.Watch;
            string pin = w.Rating.Pin;
            return e.Kind switch
            {
                EventKind.Ready => $"{w.Name}: {pin} {V(e.Volts)} V, çalışma aralığında (en az {V(w.Rating.Min)} V)",
                EventKind.BrownOut => $"{w.Name}: {pin} {V(e.Volts)} V'a düştü, çalışma sınırının altında (en az {V(w.Rating.Min)} V)",
                EventKind.Recovered => $"{w.Name}: {pin} toparlandı; {Ms(e.Duration)} ms sınırın altında kaldı, en düşük {V(e.Volts)} V",
                _ => $"{w.Name}: {pin} {V(e.Volts)} V, en çok {V(w.Rating.Max)} V olmalı"
            };
        }

        /// <summary>
        /// The report: the parts in the order they came up, then each one's brown-outs and over-voltages, then the ones
        /// that never got enough.
        /// </summary>
        public List<string> Report()
        {
            var lines = new List<string>();
            int n = 0;
            foreach (var w in _watches.Where(w => w.Ready).OrderBy(w => w.ReadyAt))
                lines.Add($"{++n}. {w.Name} ({w.Rating.Pin}): {Ms(w.ReadyAt)} ms'de çalışma aralığında, {V(w.MinSeen)}–{V(w.MaxSeen)} V gördü");
            foreach (var w in _watches.Where(w => w.BrownOuts > 0))
                lines.Add($"{w.Name}: {w.BrownOuts} kez besleme düşmesi, en uzunu {Ms(w.LongestBrownOut)} ms, en düşük {V(w.DeepestDip)} V (en az {V(w.Rating.Min)} V)");
            foreach (var w in _watches.Where(w => w.MaxSeen > w.Rating.Max))
                lines.Add($"{w.Name}: {w.Rating.Pin} {V(w.MaxSeen)} V'u gördü, en çok {V(w.Rating.Max)} V olmalı");
            // Below its sheet's range is not the same as dead: a model may still run a little under it, a real part may not.
            // The highest it saw is cut, not rounded, so 4.8999 V under a 4.9 V minimum does not read as 4.90.
            foreach (var w in _watches.Where(w => !w.Ready))
                lines.Add($"{w.Name}: {w.Rating.Pin} hiç {V(w.Rating.Min)} V'a çıkmadı (en çok {(Math.Floor(w.MaxSeen * 1000) / 1000).ToString("0.000", CultureInfo.InvariantCulture)} V): veri sayfasındaki çalışma aralığının altında");
            return lines;
        }
    }
}
