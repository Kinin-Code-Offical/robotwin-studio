using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>An infrared source: a TV-style remote or an IR LED on a board.</summary>
    public sealed class IrEmitter
    {
        private readonly List<(double Start, double End)> _marks = new List<(double, double)>();

        public IrEmitter(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public Func<Vec3> Position { get; set; } = () => Vec3.Zero;

        /// <summary>Optical axis (world frame, unit).</summary>
        public Func<Vec3> Direction { get; set; } = () => Vec3.UnitX;

        /// <summary>Angle where the intensity falls to half (°): ≈ 20° for a 5 mm 940 nm LED, ≈ 30° for a remote.</summary>
        public double HalfAngleDeg { get; set; } = 25.0;

        /// <summary>On-axis distance at which a TSOP382xx still decodes at nominal drive (m).</summary>
        public double RangeM { get; set; } = 8.0;

        /// <summary>
        /// For emitters driven by firmware: sampled once per slice, true while a 30–60 kHz
        /// carrier is on (null for scripted remotes).
        /// </summary>
        public Func<bool>? LiveCarrier { get; set; }

        /// <summary>Relative drive (1 = nominal current); scales the range by √drive.</summary>
        public Func<double> Drive { get; set; } = () => 1.0;

        internal IReadOnlyList<(double Start, double End)> Marks => _marks;

        internal void AddMark(double start, double end)
        {
            if (end <= start) return;
            if (_marks.Count > 0 && start <= _marks[^1].End + 1e-9)
            {
                _marks[^1] = (_marks[^1].Start, Math.Max(_marks[^1].End, end));
                return;
            }

            _marks.Add((start, end));
        }

        internal void Forget(double before) => _marks.RemoveAll(m => m.End < before);

        public bool CarrierAt(double t) => _marks.Any(m => t >= m.Start && t < m.End);

        /// <summary>Time of the last mark (for drawing the beam).</summary>
        public double LastActivity => _marks.Count > 0 ? _marks[^1].End : double.NegativeInfinity;
    }

    /// <summary>A 38 kHz IR receiver module (TSOP38238): output low while it sees a carrier.</summary>
    public sealed class IrReceiver
    {
        public IrReceiver(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }
        public string Name { get; }
        public Func<Vec3> Position { get; set; } = () => Vec3.Zero;

        /// <summary>Direction the lens faces (world frame, unit).</summary>
        public Func<Vec3> Facing { get; set; } = () => Vec3.UnitX;

        /// <summary>Half-sensitivity angle (°); TSOP382xx ≈ 45°.</summary>
        public double HalfAngleDeg { get; set; } = 45.0;

        /// <summary>Open-collector output with the module's 30 kΩ pull-up (null in tests without a circuit).</summary>
        public DigitalPin? Output { get; set; }

        /// <summary>Circuit nodes of VS and GND (−1 = none); the session sets <see cref="IsPowered"/> from them.</summary>
        public int SupplyNode { get; set; } = -1;
        public int GroundNode { get; set; } = -1;

        public Func<bool> IsPowered { get; set; } = () => true;

        /// <summary>Carrier seen during the last slice.</summary>
        public bool Active { get; internal set; }

        /// <summary>Output edges (time, level) in the last call of <see cref="IrChannel.Advance"/> (for tests and the view).</summary>
        public List<(double Time, bool High)> Edges { get; } = new List<(double, bool)>();

        internal bool Low;
    }

    /// <summary>
    /// 38 kHz infrared between emitters and receivers: line of sight only (opaque obstacles
    /// block it), Lambertian emitter lobe and receiver directivity, inverse-square range.
    /// Scripted frames (NEC) are reproduced edge-exactly on the receivers' output pins.
    /// </summary>
    public sealed class IrChannel
    {
        private readonly RadioMedium _medium;

        internal IrChannel(RadioMedium medium) => _medium = medium;

        public List<IrEmitter> Emitters { get; } = new List<IrEmitter>();
        public List<IrReceiver> Receivers { get; } = new List<IrReceiver>();

        /// <summary>
        /// Relative signal at the receiver (≥ 1 decodes): I(θ)·D(φ)·(R/d)², with Lambertian
        /// orders from the half angles; 0 when blocked or behind either part.
        /// </summary>
        public double Signal(IrEmitter e, IrReceiver r)
        {
            var pe = e.Position();
            var pr = r.Position();
            var v = pr - pe;
            double d = v.Length;
            if (d < 1e-4) return 1e6;
            var u = v / d;
            double cosE = e.Direction().Normalized().Dot(u);
            double cosR = r.Facing().Normalized().Dot(-u);
            if (cosE <= 0 || cosR <= 0) return 0;
            foreach (var o in _medium.Propagation.Obstacles)
            {
                if (o.OpaqueToInfrared && o.Blocks(pe, pr)) return 0;
            }

            double me = Order(e.HalfAngleDeg), mr = Order(r.HalfAngleDeg);
            double range = e.RangeM * Math.Sqrt(Math.Max(0, e.Drive()));
            return Math.Pow(cosE, me) * Math.Pow(cosR, mr) * (range / d) * (range / d);
        }

        public bool LineOfSight(IrEmitter e, IrReceiver r) => Signal(e, r) >= 1.0;

        private static double Order(double halfAngleDeg)
        {
            double c = Math.Cos(Math.Max(1, Math.Min(89, halfAngleDeg)) * Math.PI / 180);
            return Math.Log(0.5) / Math.Log(c);
        }

        /// <summary>
        /// Queues an NEC frame (9 ms mark, 4.5 ms space, address, ~address, command, ~command
        /// LSB first, 562.5 µs marks, 562.5/1687.5 µs spaces, stop mark) starting at <paramref name="start"/>.
        /// Returns the end time.
        /// </summary>
        public double SendNec(IrEmitter e, double start, byte address, byte command, bool repeat = false)
        {
            const double unit = 562.5e-6;
            double t = start;
            if (repeat)
            {
                e.AddMark(t, t + 9e-3);
                t += 9e-3 + 2.25e-3;
                e.AddMark(t, t + unit);
                return t + unit;
            }

            e.AddMark(t, t + 9e-3);
            t += 9e-3 + 4.5e-3;
            uint frame = (uint)(address | ((byte)~address << 8) | (command << 16) | ((byte)~command << 24));
            for (int i = 0; i < 32; i++)
            {
                e.AddMark(t, t + unit);
                t += unit + (((frame >> i) & 1) != 0 ? 3 * unit : unit);
            }

            e.AddMark(t, t + unit);
            return t + unit;
        }

        /// <summary>Sets every receiver output for the slice [now, now + dt], scheduling exact edges on its pin.</summary>
        public void Advance(double now, double dt)
        {
            double end = now + dt;
            foreach (var e in Emitters)
            {
                if (e.LiveCarrier != null && e.LiveCarrier()) e.AddMark(now, end);
                e.Forget(now - 0.5);
            }

            foreach (var r in Receivers)
            {
                r.Edges.Clear();
                if (!r.IsPowered())
                {
                    r.Active = false;
                    continue;
                }

                // Union of visible marks over the slice.
                var marks = new List<(double S, double E)>();
                foreach (var e in Emitters)
                {
                    if (e.Marks.Count == 0 || e.LastActivity < now) continue;
                    if (!LineOfSight(e, r)) continue;
                    foreach (var m in e.Marks)
                    {
                        if (m.End <= now || m.Start >= end) continue;
                        marks.Add((Math.Max(now, m.Start), Math.Min(end, m.End)));
                    }
                }

                marks.Sort((a, b) => a.S.CompareTo(b.S));
                bool low = r.Low;
                double cursor = now;
                bool any = false;
                foreach (var (s, e2) in marks)
                {
                    if (e2 <= cursor) continue;
                    double s2 = Math.Max(s, cursor);
                    any = true;
                    if (!low)
                    {
                        Edge(r, s2, false);
                        low = true;
                    }

                    // Gap before the next mark is resolved on the next iteration.
                    cursor = e2;
                    bool continues = marks.Any(m => m.S <= e2 + 1e-9 && m.E > e2);
                    if (!continues && e2 < end - 1e-12)
                    {
                        Edge(r, e2, true);
                        low = false;
                    }
                }

                if (!any && low)
                {
                    Edge(r, now, true);
                    low = false;
                }

                r.Low = low;
                r.Active = any;
            }
        }

        private static void Edge(IrReceiver r, double t, bool high)
        {
            r.Edges.Add((t, high));
            r.Output?.Schedule(t, high ? PinMode.InputPullup : PinMode.OutputLow);
        }
    }
}
