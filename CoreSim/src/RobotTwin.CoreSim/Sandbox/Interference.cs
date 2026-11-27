using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[interference] Two parts that occupy the same space: how much, and where.</summary>
    public sealed class InterferencePair
    {
        public string A { get; internal set; } = string.Empty;
        public string B { get; internal set; } = string.Empty;

        /// <summary>Overlap volume (mm³); 0 for parts that only touch (when touching counts).</summary>
        public double VolumeMm3 { get; internal set; }

        /// <summary>Middle of the overlap, world metres.</summary>
        public Vec3 Center { get; internal set; }

        /// <summary>Joined by a joint (a horn on its servo, a wheel on its motor): often meant to overlap.</summary>
        public bool Joined { get; internal set; }
    }

    /// <summary>[interference] What SolidWorks' Interference Detection asks.</summary>
    public sealed class InterferenceOptions
    {
        /// <summary>Only these objects (by id) against everything; null: every pair.</summary>
        public ICollection<string>? Only { get; set; }

        /// <summary>Parts that only touch count too (SolidWorks' "treat coincidence as interference").</summary>
        public bool TouchingCounts { get; set; }

        /// <summary>Pairs joined by a joint are checked too (off: a horn on its servo is not news).</summary>
        public bool IncludeJoined { get; set; }

        /// <summary>Sampling step (mm) of the overlap volume.</summary>
        public double ResolutionMm { get; set; } = 0.5;
    }

    /// <summary>
    /// [interference] SolidWorks' Interference Detection for an assembly or world: every pair of parts whose solids
    /// overlap, with the overlap volume and where it is. Each part is its convex shape in its pose (its model's hull,
    /// or its box); pairs whose boxes miss are skipped, the rest are sampled on a grid fine enough to resolve
    /// <see cref="InterferenceOptions.ResolutionMm"/>.
    /// </summary>
    public static class Interference
    {
        internal sealed class Solid
        {
            public string Id = string.Empty;
            public ConvexHull Hull = null!;
            public Vec3 Min, Max;

            public bool Inside(Vec3 p, double tol) => Hull.Planes.All(h => h.Distance(p) <= tol);
        }

        public static List<InterferencePair> Find(WorldDocument world, IWorldGeometry geometry, InterferenceOptions? options = null)
        {
            options ??= new InterferenceOptions();
            var solids = SolidsOf(world, geometry);
            var joined = JoinedPairs(world);
            double tol = options.TouchingCounts ? 1e-5 : -1e-6;
            double step = Math.Max(1e-5, options.ResolutionMm / 1000);
            var result = new List<InterferencePair>();
            for (int i = 0; i < solids.Count; i++)
            {
                for (int k = i + 1; k < solids.Count; k++)
                {
                    var a = solids[i];
                    var b = solids[k];
                    if (options.Only != null && !options.Only.Contains(a.Id) && !options.Only.Contains(b.Id)) continue;
                    bool isJoined = joined.Contains((a.Id, b.Id));
                    if (isJoined && !options.IncludeJoined) continue;
                    var lo = new Vec3(Math.Max(a.Min.X, b.Min.X), Math.Max(a.Min.Y, b.Min.Y), Math.Max(a.Min.Z, b.Min.Z));
                    var hi = new Vec3(Math.Min(a.Max.X, b.Max.X), Math.Min(a.Max.Y, b.Max.Y), Math.Min(a.Max.Z, b.Max.Z));
                    if (hi.X < lo.X - tol || hi.Y < lo.Y - tol || hi.Z < lo.Z - tol) continue;   // boxes apart

                    // sample the common box: at most 48 cells a side, at least the asked resolution where the box allows
                    var size = hi - lo;
                    int nx = Cells(size.X, step), ny = Cells(size.Y, step), nz = Cells(size.Z, step);
                    double cx = size.X / nx, cy = size.Y / ny, cz = size.Z / nz;
                    int hits = 0;
                    var sum = Vec3.Zero;
                    for (int x = 0; x < nx; x++)
                    for (int y = 0; y < ny; y++)
                    for (int z = 0; z < nz; z++)
                    {
                        var p = new Vec3(lo.X + (x + 0.5) * cx, lo.Y + (y + 0.5) * cy, lo.Z + (z + 0.5) * cz);
                        if (!a.Inside(p, tol) || !b.Inside(p, tol)) continue;
                        hits++;
                        sum += p;
                    }

                    double cell = Math.Max(cx, 1e-12) * Math.Max(cy, 1e-12) * Math.Max(cz, 1e-12);
                    double volume = hits * cell * 1e9;   // m³ → mm³
                    bool touching = hits == 0 && options.TouchingCounts && Touch(a, b, tol);
                    if (hits == 0 && !touching) continue;
                    result.Add(new InterferencePair
                    {
                        A = a.Id, B = b.Id, VolumeMm3 = volume, Joined = isJoined,
                        Center = hits > 0 ? sum / hits : (lo + hi) / 2
                    });
                }
            }

            return result.OrderByDescending(r => r.VolumeMm3).ToList();
        }

        /// <summary>Every object as its convex solid in its pose (its model's hull, or its box); flat ones are left out.</summary>
        internal static List<Solid> SolidsOf(WorldDocument world, IWorldGeometry geometry)
        {
            var solids = new List<Solid>();
            foreach (var o in world.Objects)
            {
                if (o.Kind == WorldObjectKind.Empty) continue;   // [empty] nothing to collide
                var g = geometry.GeometryOf(o);
                var local = g.Hull is { Count: >= 4 } hull ? hull : BoxCorners(g.Min, g.Max);
                if (local.Count < 4) continue;
                var pts = local.Select(o.Pose.ToWorld).ToList();
                var h = ConvexHull.Compute(pts);
                if (h.IsDegenerate) continue;
                solids.Add(new Solid
                {
                    Id = o.Id, Hull = h,
                    Min = new Vec3(pts.Min(p => p.X), pts.Min(p => p.Y), pts.Min(p => p.Z)),
                    Max = new Vec3(pts.Max(p => p.X), pts.Max(p => p.Y), pts.Max(p => p.Z))
                });
            }

            return solids;
        }

        /// <summary>
        /// The pairs a joint ties together, both ways round; and [assembly-check] every two parts welded into one rigid
        /// body by fixed joints between their main bodies (a ball caster and its post, both fixed to the plate, move as one
        /// solid in the physics: a ball sunk into its housing is how it is built, not a clash). A fixed joint on a moving
        /// port (a wheel on a motor's shaft) does not weld its part to the rest: that one turns, its rubbing is news.
        /// </summary>
        internal static HashSet<(string, string)> JoinedPairs(WorldDocument world)
        {
            var joined = new HashSet<(string, string)>();
            var parent = new Dictionary<string, string>(StringComparer.Ordinal);
            string Root(string x)
            {
                while (parent.TryGetValue(x, out var p) && p != x) x = parent[x] = parent.TryGetValue(p, out var pp) ? pp : p;
                return x;
            }

            foreach (var j in world.Joints)
            {
                if (j.ObjectB == null) continue;   // fixed to the world: no second part
                joined.Add((j.ObjectA, j.ObjectB));
                joined.Add((j.ObjectB, j.ObjectA));
                if (j.Kind != WorldJointKind.Fixed || !string.IsNullOrEmpty(j.PortA) || !string.IsNullOrEmpty(j.PortB)) continue;
                foreach (var id in new[] { j.ObjectA, j.ObjectB })
                    if (!parent.ContainsKey(id)) parent[id] = id;
                string ra = Root(j.ObjectA), rb = Root(j.ObjectB);
                if (ra != rb) parent[ra] = rb;
            }

            foreach (var group in parent.Keys.GroupBy(Root))
            {
                var members = group.ToList();
                for (int i = 0; i < members.Count; i++)
                for (int k = i + 1; k < members.Count; k++)
                {
                    joined.Add((members[i], members[k]));
                    joined.Add((members[k], members[i]));
                }
            }

            return joined;
        }

        private static int Cells(double extent, double step) => Math.Max(1, Math.Min(48, (int)Math.Ceiling(Math.Max(0, extent) / step)));

        /// <summary>Whether two solids touch: a vertex of one on the other's surface.</summary>
        private static bool Touch(Solid a, Solid b, double tol) =>
            a.Hull.Vertices.Any(v => b.Inside(v, tol)) || b.Hull.Vertices.Any(v => a.Inside(v, tol));

        private static List<Vec3> BoxCorners(Vec3 min, Vec3 max)
        {
            if (max.X - min.X <= 0 || max.Y - min.Y <= 0 || max.Z - min.Z <= 0) return new List<Vec3>();
            var list = new List<Vec3>();
            foreach (double x in new[] { min.X, max.X })
            foreach (double y in new[] { min.Y, max.Y })
            foreach (double z in new[] { min.Z, max.Z })
                list.Add(new Vec3(x, y, z));
            return list;
        }
    }
}
