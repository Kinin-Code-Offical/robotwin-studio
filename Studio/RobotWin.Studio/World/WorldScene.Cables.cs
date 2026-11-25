using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    // [phys] Cables drawn along physical ropes: while simulating the session's ropes; in the editor a
    // relaxed rope laid over the parts as they stand (gravity, contacts with every part and the floor).
    public sealed partial class WorldScene
    {
        private WorldPhysics? _cableLayout;
        private bool _cableLayoutStale = true;

        /// <summary>[perf] Where the last cable layout spent its time.</summary>
        public string LastCableProfile { get; private set; } = string.Empty;

        private System.Threading.CancellationTokenSource? _settleCancel;
        private System.Threading.Tasks.Task? _settleTask;

        /// <summary>[perf] The cables of a just-opened world are still settling (on a worker thread).</summary>
        public bool CablesSettling => _settleTask is { IsCompleted: false };

        /// <summary>
        /// [perf] Relaxes a fresh layout on a worker thread: the cables show along their routes at once and take their
        /// hanging shape a moment later, with nothing held on the UI thread. The layout belongs to the worker until it ends
        /// (<see cref="StopSettling"/> takes it back first whenever it has to change).
        /// </summary>
        private void SettleInBackground(WorldPhysics layout, int steps)
        {
            var cancel = new System.Threading.CancellationTokenSource();
            _settleCancel = cancel;
            var token = cancel.Token;
            _settleTask = System.Threading.Tasks.Task.Run(() =>
            {
                for (int done = 0; done < steps; done += 8)
                {
                    if (token.IsCancellationRequested) return;
                    layout.RelaxCables(done, Math.Min(8, steps - done), steps);
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested || _cableLayout != layout || World == null) return;
                    foreach (var c in World.Cables)
                    {
                        if (layout.CablePoints(c.Id) is { } pts) _ropeShapes[c.Id] = pts.ToArray();
                    }

                    RebuildCables();
                }));
            }, token);
        }

        /// <summary>Stops a background settle and waits for it (before the layout is changed or dropped).</summary>
        private void StopSettling()
        {
            if (_settleTask == null) return;
            _settleCancel?.Cancel();
            try
            {
                _settleTask.Wait(2000);
            }
            catch (AggregateException)
            {
            }

            _settleTask = null;
            _settleCancel = null;
        }
        private int _cableLayoutPoses;
        private readonly Dictionary<string, IReadOnlyList<Vec3>> _ropeShapes = new Dictionary<string, IReadOnlyList<Vec3>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Plug positions of a cable in its two objects' frames (from the 3D models' pins).</summary>
        public CablePins? CablePinsOf(WorldCable cable)
        {
            if (!_items.TryGetValue(cable.A.ObjectId, out var a) || !_items.TryGetValue(cable.B.ObjectId, out var b)) return null;
            return new CablePins(LocalPin(a, cable.A.Pin), LocalPin(b, cable.B.Pin));
        }

        private static Vec3 LocalPin(Item it, string pin)
        {
            foreach (var p in it.Model.Pins)
                if (p.Pin == pin)
                    return new Vec3(p.Local.X, p.Local.Y, p.Local.Z);
            return Vec3.Zero;
        }

        /// <summary>The editor's resting shape of a cable (world points), the start shape of a simulated rope.</summary>
        public IReadOnlyList<Vec3>? CableSeedOf(string cableId) => _ropeShapes.TryGetValue(cableId, out var s) ? s : null;

        /// <summary>Rope length of a cable: its own length, or the distance between its pins plus slack.</summary>
        private double RopeLength(WorldCable cable)
        {
            if (cable.Length > 0) return cable.Length;
            if (World == null) return 0.2;
            var a = World.Find(cable.A.ObjectId);
            var b = World.Find(cable.B.ObjectId);
            if (a == null || b == null) return 0.2;
            double span = (WorldAnchor(a, cable.A.Pin) - WorldAnchor(b, cable.B.Pin)).Length;
            return cable.Tied ? CableRope.TiedLength(span) : CableRope.DefaultLength(span);
        }

        private int PoseSignature()
        {
            var h = new HashCode();
            if (World == null) return 0;
            foreach (var o in World.Objects)
            {
                h.Add(o.Pose.X);
                h.Add(o.Pose.Y);
                h.Add(o.Pose.Z);
                h.Add(o.Pose.Roll);
                h.Add(o.Pose.Pitch);
                h.Add(o.Pose.Yaw);
            }

            foreach (var c in World.Cables)
            {
                h.Add(c.Length);
                h.Add(c.Awg);
            }

            return h.ToHashCode();
        }

        /// <summary>Settles the editor's cables against the parts where they stand now.</summary>
        private void EnsureCableLayout()
        {
            if (World == null || World.Cables.Count == 0)
            {
                StopSettling();
                _cableLayout = null;
                _ropeShapes.Clear();
                return;
            }

            int poses = PoseSignature();
            if (!_cableLayoutStale && _cableLayout != null && poses == _cableLayoutPoses) return;
            StopSettling();   // [perf] the layout is about to change: the worker gives it back first
            try
            {
                bool seeded = World.Cables.All(c => _ropeShapes.ContainsKey(c.Id));
                var clock = System.Diagnostics.Stopwatch.StartNew();
                if (_cableLayoutStale || _cableLayout == null)
                {
                    _cableLayout = WorldPhysics.Build(World, GeometryOf, null, new WorldPhysics.LiquidQuality { Particles = 0 });   // [liquid] no liquid to lay cables
                }
                else
                {
                    _cableLayout.SyncPoses();
                }

                double build = clock.Elapsed.TotalMilliseconds;

                int relax = seeded ? (_cableLayoutStale ? 90 : 45) : 320;
                // [perf] A fresh layout (a world just opened) settles on a worker thread instead of holding the first frame:
                // the cables appear routed at once and drop into place. Command-line runs settle at once (same pictures).
                bool later = !seeded && (!Shell.Unattended.Active || Shell.Unattended.Benchmarking);
                _cableLayout.AttachCables(World, CablePinsOf, later ? 0 : relax, CableSeedOf);
                if (later) SettleInBackground(_cableLayout, relax);
                var t = _cableLayout.LastCableTimes;
                LastCableProfile = string.Format(System.Globalization.CultureInfo.InvariantCulture, "sahne {0:F0} · çıkış {1:F0} · yol {2:F0} · serme {3:F0} · gevşetme {4:F0} ms ({5} kablo)",
                    build, t.Exits, t.Routes, t.Laying, t.Relaxing, World.Cables.Count);
                _ropeShapes.Clear();
                foreach (var c in World.Cables)
                {
                    if (_cableLayout.CablePoints(c.Id) is { } pts) _ropeShapes[c.Id] = pts.ToArray();
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                _cableLayout = null;
                _ropeShapes.Clear();
            }

            _cableLayoutStale = false;
            _cableLayoutPoses = poses;
        }

        /// <summary>
        /// Path of a cable for drawing: the simulated rope while running, otherwise the relaxed
        /// rope; ends are where its plugs are (a pulled-out plug hangs at the rope's end).
        /// </summary>
        private List<Point3D> RopePath(WorldCable cable, Point3D pa, Point3D pb, out Point3D endA, out Point3D endB, out bool plugA, out bool plugB)
        {
            endA = pa;
            endB = pb;
            plugA = plugB = true;
            IReadOnlyList<Vec3>? pts;
            if (Playback != null)
            {
                // [render] a replay draws the rope exactly as it was simulated, never the editor's layout
                var rec = Playback.CableAt(cable.Id);
                pts = rec?.Points;
                if (rec is { } r) (plugA, plugB) = (r.PlugA, r.PlugB);
            }
            else if ((pts = Session?.CablePoints(cable)) != null)
            {
                (plugA, plugB) = Session!.PlugState(cable);
            }
            else
            {
                EnsureCableLayout();
                pts = CableSeedOf(cable.Id);
            }

            if (pts == null || pts.Count < 2) return CablePath(pa, pb);
            var list = new List<Point3D>(pts.Count);
            foreach (var p in pts) list.Add(new Point3D(p.X, p.Y, p.Z));
            if (plugA) list[0] = pa;
            if (plugB) list[list.Count - 1] = pb;
            endA = list[0];
            endB = list[list.Count - 1];
            return Smooth(list);
        }

        /// <summary>Catmull–Rom subdivision so the tube looks like a cable, not a chain of sticks.</summary>
        private static List<Point3D> Smooth(List<Point3D> p)
        {
            if (p.Count < 3) return p;
            var r = new List<Point3D>(p.Count * 2);
            for (int i = 0; i + 1 < p.Count; i++)
            {
                var p0 = p[Math.Max(0, i - 1)];
                var p1 = p[i];
                var p2 = p[i + 1];
                var p3 = p[Math.Min(p.Count - 1, i + 2)];
                r.Add(p1);
                const double t = 0.5, t2 = t * t, t3 = t2 * t;
                r.Add(new Point3D(
                    0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3),
                    0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3),
                    0.5 * (2 * p1.Z + (-p0.Z + p2.Z) * t + (2 * p0.Z - 5 * p1.Z + 4 * p2.Z - p3.Z) * t2 + (-p0.Z + 3 * p1.Z - 3 * p2.Z + p3.Z) * t3)));
            }

            r.Add(p[p.Count - 1]);
            return r;
        }
    }
}
