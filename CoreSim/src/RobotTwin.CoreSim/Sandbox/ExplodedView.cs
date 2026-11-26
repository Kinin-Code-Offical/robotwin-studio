using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[explode] One step of an exploded view: parts moved along a direction by a distance (SolidWorks' explode step).</summary>
    public sealed class ExplodeStep
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>The parts this step moves (by id); what is joined onto them goes with them.</summary>
        public List<string> Objects { get; set; } = new List<string>();

        /// <summary>The direction in world coordinates (normalised when used).</summary>
        public double DirX { get; set; }

        public double DirY { get; set; }

        public double DirZ { get; set; } = 1;

        /// <summary>How far (metres).</summary>
        public double Distance { get; set; } = 0.05;

        /// <summary>Each part moves along its own line from the assembly's middle instead of the direction (Fusion's radial explode).</summary>
        public bool Radial { get; set; }

        public Vec3 Direction => new Vec3(DirX, DirY, DirZ).Normalized();

        public string Describe(Func<string, string> nameOf) =>
            (string.IsNullOrEmpty(Name) ? string.Join(", ", Objects.Select(nameOf)) : Name) + $" · {Distance * 1000:0.#} mm" + (Radial ? " · dışa doğru" : $" · ({DirX:0.##}, {DirY:0.##}, {DirZ:0.##})");
    }

    /// <summary>How an automatic explode spreads the parts.</summary>
    public enum ExplodeLayout
    {
        /// <summary>Along one axis, the parts in their order on it, each a gap farther (SolidWorks' auto-space).</summary>
        Axis,

        /// <summary>Every part outward from the middle (Fusion's auto explode).</summary>
        Radial
    }

    /// <summary>
    /// [explode] An exploded view of an assembly or world, as SolidWorks' and Fusion's: steps that move parts apart along
    /// a direction, played in order (0 = assembled, 1 = fully exploded) for the slider and the animation; parts joined
    /// onto a moved part go with it, and their own steps add to it. Display only: the parts' poses are not changed.
    /// </summary>
    public static class ExplodedView
    {
        /// <summary>Each moved part's offset (world metres) at an amount of the explode, 0…1 across the steps in order.</summary>
        public static Dictionary<string, Vec3> Offsets(WorldDocument world, IReadOnlyList<ExplodeStep> steps, double amount = 1, IWorldGeometry? geometry = null)
        {
            var own = new Dictionary<string, Vec3>(StringComparer.OrdinalIgnoreCase);
            if (steps.Count == 0) return own;
            amount = Math.Max(0, Math.Min(1, amount));
            double played = amount * steps.Count;
            Vec3? middle = null;
            for (int i = 0; i < steps.Count; i++)
            {
                double f = Math.Max(0, Math.Min(1, played - i));
                if (f <= 0) break;
                var s = steps[i];
                foreach (var id in s.Objects)
                {
                    var o = world.Find(id);
                    if (o == null) continue;
                    Vec3 dir;
                    if (s.Radial)
                    {
                        middle ??= Middle(world, geometry);
                        dir = (CentreOf(o, geometry) - middle.Value).Normalized();
                        if (dir.LengthSquared < 1e-12) dir = Vec3.UnitZ;
                    }
                    else
                    {
                        dir = s.Direction;
                    }

                    own[o.Id] = (own.TryGetValue(o.Id, out var was) ? was : Vec3.Zero) + dir * (s.Distance * f);
                }
            }

            // parts joined onto a moved part ride along: a part's offset is its own plus its parent's
            var parent = Parents(world);
            var total = new Dictionary<string, Vec3>(StringComparer.OrdinalIgnoreCase);
            Vec3 Total(string id, int depth)
            {
                if (total.TryGetValue(id, out var t)) return t;
                var sum = own.TryGetValue(id, out var o) ? o : Vec3.Zero;
                if (depth < 32 && parent.TryGetValue(id, out var p)) sum += Total(p, depth + 1);
                total[id] = sum;
                return sum;
            }

            foreach (var o in world.Objects) Total(o.Id, 0);
            return total.Where(kv => kv.Value.LengthSquared > 0).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Steps that pull the given parts (every part when null) apart. A part joined onto another moves one
        /// <paramref name="gap"/> away from it (it rides along with its parent, so a chain opens up level by level); of the
        /// free parts the biggest stays as the base and the others move along the axis in their order on it, each a gap
        /// farther than the one before (or outward from the middle, radially). The deepest parts come off first, as in a
        /// disassembly.
        /// </summary>
        public static List<ExplodeStep> Auto(WorldDocument world, IWorldGeometry geometry, ExplodeLayout layout, Vec3 axis, double gap = 0.03, ICollection<string>? only = null)
        {
            var parent = Parents(world);
            var parts = world.Objects.Where(o => only == null || only.Contains(o.Id)).ToList();
            var steps = new List<ExplodeStep>();
            if (parts.Count < 2 && !parts.Any(o => parent.ContainsKey(o.Id))) return steps;
            var dir = axis.Normalized();
            if (dir.LengthSquared < 1e-12) dir = Vec3.UnitZ;
            var centre = parts.ToDictionary(o => o.Id, o => CentreOf(o, geometry), StringComparer.OrdinalIgnoreCase);

            int Depth(string id)
            {
                int d = 0;
                while (d < 32 && parent.TryGetValue(id, out var p))
                {
                    id = p;
                    d++;
                }

                return d;
            }

            // joined parts: away from their parent, deepest first
            foreach (var o in parts.Where(o => parent.ContainsKey(o.Id)).OrderByDescending(o => Depth(o.Id)).ThenByDescending(o => centre[o.Id].Dot(dir)))
            {
                var p = world.Find(parent[o.Id]);
                if (p == null) continue;
                var from = CentreOf(p, geometry);
                Vec3 d;
                if (layout == ExplodeLayout.Radial)
                {
                    d = (centre[o.Id] - from).Normalized();
                    if (d.LengthSquared < 1e-12) d = dir;
                }
                else
                {
                    d = Away(o, p, geometry, dir, gap);
                }

                steps.Add(new ExplodeStep { Name = o.Name, Objects = { o.Id }, DirX = d.X, DirY = d.Y, DirZ = d.Z, Distance = gap });
            }

            // free parts: the biggest stays
            var free = parts.Where(o => !parent.ContainsKey(o.Id)).ToList();
            if (free.Count < 2) return steps;
            var baseId = free.OrderByDescending(o => Volume(geometry.GeometryOf(o))).First().Id;
            if (layout == ExplodeLayout.Radial)
            {
                var middle = Middle(world, geometry);
                foreach (var o in free.Where(o => o.Id != baseId).OrderByDescending(o => (centre[o.Id] - middle).Length))
                {
                    double away = (centre[o.Id] - middle).Length;
                    steps.Add(new ExplodeStep { Name = o.Name, Objects = { o.Id }, Radial = true, Distance = gap + away });
                }

                return steps;
            }

            var basePart = world.Find(baseId)!;
            // over the base: up the axis in order, each a gap farther; under it: down; beside it (on the same floor, as a
            // board next to a chassis): outward across, never down into the floor
            var groups = free.Where(o => o.Id != baseId)
                .Select(o => (o, d: Away(o, basePart, geometry, dir, gap), at: Math.Abs((centre[o.Id] - centre[baseId]).Dot(dir))))
                .GroupBy(x => (Math.Round(x.d.X, 3), Math.Round(x.d.Y, 3), Math.Round(x.d.Z, 3)));
            foreach (var g in groups)
            {
                bool along = Math.Abs(g.First().d.Dot(dir)) > 0.5;
                var row = g.OrderBy(x => along ? x.at : (centre[x.o.Id] - centre[baseId]).Length).ToList();
                for (int i = row.Count - 1; i >= 0; i--)   // the outermost first
                {
                    var d = row[i].d;
                    steps.Add(new ExplodeStep { Name = row[i].o.Name, Objects = { row[i].o.Id }, DirX = d.X, DirY = d.Y, DirZ = d.Z, Distance = along ? gap * (i + 1) : gap });
                }
            }

            return steps;
        }

        /// <summary>
        /// Which way a part comes away from the one it sits on or beside: along the axis when it is wholly over or under it
        /// (under only while that keeps it above the floor), otherwise across, away from its middle.
        /// </summary>
        private static Vec3 Away(WorldObject part, WorldObject from, IWorldGeometry geometry, Vec3 dir, double gap)
        {
            var (plo, phi) = Extent(part, geometry, dir);
            var (flo, fhi) = Extent(from, geometry, dir);
            double tol = Math.Max(1e-4, 0.15 * Math.Min(phi - plo, fhi - flo));
            if (plo >= fhi - tol) return dir;   // over it
            // the floor (z = 0) stops a part standing on it from going down; one already under it is in a world without one
            bool onFloor = dir.Z > 0.9 && plo >= -1e-6;
            if (phi <= flo + tol && (!onFloor || plo - gap >= -1e-9)) return -dir;   // under it, with room above the floor
            // beside it: across the axis, away from the other's middle
            var c = CentreOf(part, geometry) - CentreOf(from, geometry);
            var across = c - dir * c.Dot(dir);
            if (across.LengthSquared < 1e-12) across = dir.AnyPerpendicular();
            return across.Normalized();
        }

        /// <summary>A part's reach along a direction: the lowest and highest of its box's corners in the world.</summary>
        private static (double Lo, double Hi) Extent(WorldObject o, IWorldGeometry geometry, Vec3 dir)
        {
            var g = geometry.GeometryOf(o);
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (double x in new[] { g.Min.X, g.Max.X })
            foreach (double y in new[] { g.Min.Y, g.Max.Y })
            foreach (double z in new[] { g.Min.Z, g.Max.Z })
            {
                double d = o.Pose.ToWorld(new Vec3(x, y, z)).Dot(dir);
                lo = Math.Min(lo, d);
                hi = Math.Max(hi, d);
            }

            return (lo, hi);
        }

        /// <summary>Each part's parent through its joint (the part it is joined onto).</summary>
        public static Dictionary<string, string> Parents(WorldDocument world)
        {
            var parent = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var j in world.Joints)
            {
                if (j.ObjectB != null && !parent.ContainsKey(j.ObjectA) && j.ObjectA != j.ObjectB) parent[j.ObjectA] = j.ObjectB;
            }

            return parent;
        }

        private static double Volume(WorldBodyGeometry g)
        {
            var s = g.Max - g.Min;
            return Math.Max(0, s.X) * Math.Max(0, s.Y) * Math.Max(0, s.Z);
        }

        private static Vec3 CentreOf(WorldObject o, IWorldGeometry? geometry)
        {
            if (geometry == null) return new Vec3(o.Pose.X, o.Pose.Y, o.Pose.Z);
            var g = geometry.GeometryOf(o);
            return o.Pose.ToWorld((g.Min + g.Max) / 2);
        }

        private static Vec3 Middle(WorldDocument world, IWorldGeometry? geometry)
        {
            if (world.Objects.Count == 0) return Vec3.Zero;
            var sum = Vec3.Zero;
            foreach (var o in world.Objects) sum += CentreOf(o, geometry);
            return sum / world.Objects.Count;
        }
    }
}
