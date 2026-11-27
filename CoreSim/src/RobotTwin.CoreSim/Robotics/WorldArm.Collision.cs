using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>[kin] What a pose or a move of an arm built in a world runs into.</summary>
    public sealed class WorldArmHit
    {
        /// <summary>The arm's part that hits.</summary>
        public string PartId { get; internal set; } = string.Empty;

        /// <summary>What it hits: an object outside the arm, or empty for the floor.</summary>
        public string ObstacleId { get; internal set; } = string.Empty;

        /// <summary>How far along the move (0 = where it starts, 1 = the pose it goes to).</summary>
        public double Along { get; internal set; }

        /// <summary>How deep the boxes overlap there (m).</summary>
        public double Depth { get; internal set; }

        /// <summary>The axis values where it hits.</summary>
        public double[] Q { get; internal set; } = Array.Empty<double>();

        /// <summary>True when the pose it goes to is itself in the way (not only the way there).</summary>
        public bool AtPose => Along >= 1 - 1e-9;
    }

    public sealed partial class WorldArm
    {
        /// <summary>The objects that move with an axis (the last path axis between them and the base) as built.</summary>
        private readonly List<(string Id, int Level)> _moving = new List<(string, int)>();

        /// <summary>Every object of the arm's assembly, moving or not (none of them is an obstacle to it).</summary>
        private readonly HashSet<string> _assembly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// [kin] What the arm runs into going from <paramref name="from"/> to <paramref name="to"/> (every axis turning at once,
        /// as servos told together do), as the world stands in the editor: sampled so that no axis turns more than 1° (a
        /// slider 2 mm) between samples, each moving part's box against every object outside the arm's assembly and the
        /// floor. A contact that is there where the move starts counts only as far as it gets deeper (a link resting on the
        /// table it was built on), and the tip's contacts where the move ends do not count (its point put on a surface);
        /// any other overlap deeper than <paramref name="tolerance"/> does. Null when the way is clear.
        /// </summary>
        public WorldArmHit? FirstHit(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry, IReadOnlyList<double> from, IReadOnlyList<double> to, double tolerance = 0.001)
        {
            if (_moving.Count == 0 || from.Count != Axes.Count || to.Count != Axes.Count) return null;
            int n = 1;
            for (int i = 0; i < Axes.Count; i++)
            {
                double step = Axes[i].Spec.Type == ArmJointType.Prismatic ? 0.002 : Math.PI / 180;
                n = Math.Max(n, (int)Math.Ceiling(Math.Abs(to[i] - from[i]) / step));
            }

            n = Math.Min(n, 720);
            var geo = new Dictionary<string, WorldBodyGeometry>(StringComparer.OrdinalIgnoreCase);
            WorldBodyGeometry G(WorldObject o)
            {
                if (!geo.TryGetValue(o.Id, out var g)) geo[o.Id] = g = geometry(o);
                return g;
            }

            var parts = _moving.Select(m => (m.Id, m.Level, O: world.Find(m.Id))).Where(m => m.O != null).Select(m => (m.Id, m.Level, O: m.O!)).ToList();
            var obstacles = world.Objects.Where(o => !_assembly.Contains(o.Id) && o.Kind != WorldObjectKind.Empty)
                                         .Select(o => (o.Id, Box: Box(o, G(o), Transform3.Identity))).ToList();
            var q = new double[Axes.Count];

            // the contacts where it starts, and the tip's where it ends
            var start = Contacts(0);
            var end = Contacts(1);
            for (int s = 1; s <= n; s++)
            {
                double t = (double)s / n;
                foreach (var (part, other, depth) in Overlaps(t))
                {
                    if (start.TryGetValue((part, other), out double was) && depth <= was + tolerance) continue;
                    if (string.Equals(part, TipId, StringComparison.OrdinalIgnoreCase) && end.ContainsKey((part, other))) continue;
                    if (depth <= tolerance) continue;
                    return new WorldArmHit { PartId = part, ObstacleId = other, Along = t, Depth = depth, Q = q.ToArray() };
                }
            }

            return null;

            Dictionary<(string, string), double> Contacts(double t)
            {
                var d = new Dictionary<(string, string), double>();
                foreach (var (part, other, depth) in Overlaps(t)) d[(part, other)] = depth;
                return d;
            }

            IEnumerable<(string Part, string Other, double Depth)> Overlaps(double t)
            {
                for (int i = 0; i < q.Length; i++) q[i] = from[i] + (to[i] - from[i]) * t;
                var links = ArmKinematics.LinkTransforms(Model, q);
                foreach (var (id, level, o) in parts)
                {
                    var box = Box(o, G(o), level >= 0 && level < links.Length ? links[level] : Transform3.Identity);
                    double low = box.C.Z - (box.Half.X * Math.Abs(box.Axes[0].Z) + box.Half.Y * Math.Abs(box.Axes[1].Z) + box.Half.Z * Math.Abs(box.Axes[2].Z));
                    if (low < 0) yield return (id, string.Empty, -low);
                    foreach (var (oid, ob) in obstacles)
                    {
                        double depth = WorldPhysics.BoxOverlap(box, ob);
                        if (depth > 0) yield return (id, oid, depth);
                    }
                }
            }
        }

        /// <summary>
        /// [kin] The solution checked against the scene (the roadmap's "kolun hedefe neden ulaşamadığı … çarpışma"): when the
        /// pose it gives, or the way from <paramref name="current"/> to it, runs into something, <see cref="WorldArmSolution.Hit"/>
        /// says what and the message says it plainly; the cause becomes "çarpışma" when nothing else stopped it.
        /// </summary>
        public void CheckCollision(WorldArmSolution solution, WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry, IReadOnlyList<double> current)
        {
            var hit = FirstHit(world, geometry, current, solution.Q);
            solution.Hit = hit;
            if (hit == null) return;
            string part = world.Find(hit.PartId)?.Name ?? hit.PartId;
            string other = hit.ObstacleId.Length == 0 ? "zemin" : world.Find(hit.ObstacleId)?.Name ?? hit.ObstacleId;
            var c = CultureInfo.InvariantCulture;
            string text = hit.AtPose
                ? string.Format(c, "Bu konumda {0} ile {1} çakışır ({2:0.#} mm iç içe).", part, other, hit.Depth * 1000)
                : string.Format(c, "Oraya giderken {0} ile {1} çarpışır (yolun %{2:0}'inde, {3:0.#} mm iç içe).", part, other, hit.Along * 100, hit.Depth * 1000);
            if (solution.Cause.Length == 0) solution.Cause = "çarpışma";
            solution.Message = solution.Message.Length == 0 ? text : solution.Message.TrimEnd() + "\n" + text;   // a line of its own (translated as it is)
        }

        /// <summary>An object's box (its body's local bounds) where <paramref name="motion"/> takes it from where it was built.</summary>
        private static (Vec3 C, Vec3[] Axes, Vec3 Half) Box(WorldObject o, WorldBodyGeometry g, Transform3 motion)
        {
            var local = (g.Min + g.Max) * 0.5;
            var half = (g.Max - g.Min) * 0.5;
            var rot = motion.Q * o.Pose.Orientation;
            return (motion.Apply(o.Pose.ToWorld(local)), new[] { rot.Rotate(Vec3.UnitX), rot.Rotate(Vec3.UnitY), rot.Rotate(Vec3.UnitZ) },
                new Vec3(Math.Abs(half.X), Math.Abs(half.Y), Math.Abs(half.Z)));
        }
    }
}
