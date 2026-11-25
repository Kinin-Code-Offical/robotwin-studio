using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>One physical inconsistency of a world: Kind is "gömülü" (joined parts in each other), "kablo" (a wire through a solid) or "yol yok" (no way for a wire); Amount in m.</summary>
    public sealed record ConsistencyIssue(string Kind, string A, string B, double Amount, string Message);

    /// <summary>
    /// [audit] What the collision filter hides. Parts joined to each other do not collide (they touch at their mounting
    /// faces), so a motor placed half inside the firewall it is screwed to, or a servo sunk into the wing it drives,
    /// would never be noticed by the physics. Wires are checked the same way: a wire may lie on a part, never run
    /// through it. Both are reported so a world can be built as it would be in reality.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        /// <summary>
        /// Parts joined to each other (directly or through one part, the pairs the collision filter skips) whose solids
        /// overlap by more than <paramref name="tolerance"/> (m). Parts turning on a hinge, slider or ball joint are
        /// left out: an axle runs through its bore.
        /// </summary>
        public IReadOnlyList<ConsistencyIssue> JoinedOverlaps(double tolerance = 0.001)
        {
            // A bearing's two sides: what turns (a wheel, a caster ball, a shaft) and everything rigid with what it turns
            // in (the axle, the fork, the socket screwed to the chassis) really do overlap, the axle in its bore.
            var moving = new HashSet<(SceneBody, SceneBody)>();
            foreach (var j in Scene.Joints)
            {
                if (j.B == null || j.Kind == JointKind.Fixed) continue;
                foreach (var x in FixedSet(j.A))
                    foreach (var y in FixedSet(j.B))
                    {
                        moving.Add((x, y));
                        moving.Add((y, x));
                    }
            }

            // [couple] Meshing gears' tip circles overlap by design (their teeth fill each other's gaps); a nut surrounds its screw.
            foreach (var c in Scene.Couplings)
            {
                foreach (var x in FixedSet(c.A.ChildBody))
                    foreach (var y in FixedSet(c.B.ChildBody))
                    {
                        moving.Add((x, y));
                        moving.Add((y, x));
                    }
            }

            var result = new List<ConsistencyIssue>();
            var seen = new HashSet<(string, string)>();
            foreach (var (a, b) in Scene.NoCollide)
            {
                if (moving.Contains((a, b)) || !Solid(a) || !Solid(b)) continue;
                if (a.Tag is not WorldObject oa || b.Tag is not WorldObject ob || ReferenceEquals(oa, ob)) continue;
                double depth = Overlap(a, b);
                if (depth <= tolerance) continue;
                var key = string.CompareOrdinal(oa.Id, ob.Id) < 0 ? (oa.Id, ob.Id) : (ob.Id, oa.Id);
                if (!seen.Add(key)) continue;
                result.Add(new ConsistencyIssue("gömülü", oa.Id, ob.Id, depth, $"{NameOf(oa)} ile {NameOf(ob)} birbirine {depth * 1000:0.0} mm gömülü (bağlı parçalar yalnızca yüzeyde değmeli)"));
            }

            return result;
        }

        /// <summary>
        /// Wires running through a solid part as they lie now (the plug's housing in its own part and propeller discs
        /// excepted), and wires with no way between their plugs at all.
        /// </summary>
        public IReadOnlyList<ConsistencyIssue> CableCrossings()
        {
            var result = new List<ConsistencyIssue>();
            foreach (var c in _cables)
            {
                string label = string.IsNullOrEmpty(c.Cable.Name) ? c.Cable.Id : c.Cable.Name;
                if (BlockedCables.Contains(c.Cable.Id))
                    result.Add(new ConsistencyIssue("yol yok", c.Cable.Id, string.Empty, 0, $"kablo {label}: uçları arasında katı bir parçanın içinden geçmeyen yol yok (kapalı kutuya kablo deliği ekleyin)"));
                foreach (var end in new[] { c.Rope.A, c.Rope.B })
                {
                    if (!end.Attached) continue;
                    var at = end.World;
                    foreach (var b in Scene.Bodies)
                    {
                        if (ReferenceEquals(b, end.Body) || !Solid(b) || (c.Rope.Ignore != null && c.Rope.Ignore.Contains(b))) continue;
                        var shape = b.EffectiveShape!;
                        if ((at - b.Position).Length > shape.BoundingRadius + 0.001) continue;
                        if (!b.SignedDistanceSolid(shape, b.Orientation.InverseRotate(at - b.Position), 0, out double d, out _) || d > -c.Rope.Radius) continue;
                        string where = b.Tag is WorldObject wo ? NameOf(wo) : b.Id;
                        result.Add(new ConsistencyIssue("kablo", c.Cable.Id, b.Tag is WorldObject wb ? wb.Id : b.Id, -d, $"kablo {label}: {(ReferenceEquals(end, c.Rope.A) ? "A" : "B")} ucunun fişi {where} içinde kalıyor ({-d * 1000:0.0} mm); parçaları ayırın"));
                    }
                }

                foreach (var (body, inside) in c.Rope.Crossings(Scene))
                {
                    string part = body.Tag is WorldObject o ? NameOf(o) : body.Id;
                    result.Add(new ConsistencyIssue("kablo", c.Cable.Id, body.Tag is WorldObject ow ? ow.Id : body.Id, inside, $"kablo {label} {part} içinden geçiyor ({inside * 1000:0} mm)"));
                }
            }

            return result;
        }

        /// <summary>Bodies held rigidly to <paramref name="body"/> by fixed joints of any kind (welds, screws, glue), itself included.</summary>
        private HashSet<SceneBody> FixedSet(SceneBody body)
        {
            var set = new HashSet<SceneBody> { body };
            var queue = new Queue<SceneBody>();
            queue.Enqueue(body);
            while (queue.Count > 0)
            {
                var b = queue.Dequeue();
                foreach (var j in Scene.Joints)
                {
                    if (j.Kind != JointKind.Fixed || !j.Enabled || j.Broken || j.B == null) continue;
                    var other = ReferenceEquals(j.A, b) ? j.B : ReferenceEquals(j.B, b) ? j.A : null;
                    if (other != null && set.Add(other)) queue.Enqueue(other);
                }
            }

            return set;
        }

        private static bool Solid(SceneBody b) => b.Collides && b.EffectiveShape is { HasSolid: true } s && s.Kind != ShapeKind.Mesh;

        /// <summary>
        /// Penetration depth of two convex solids (the deepest piece pair of concave ones): the least overlap of their
        /// projections over the separating axes of boxes, hull faces, cylinder axes and the line between the centres.
        /// </summary>
        private static double Overlap(SceneBody a, SceneBody b)
        {
            double worst = 0;
            foreach (var pa in PiecesOf(a))
                foreach (var pb in PiecesOf(b))
                    worst = Math.Max(worst, Overlap(a, pa, b, pb));
            return worst;
        }

        private static IEnumerable<CollisionShape> PiecesOf(SceneBody b) => b.Pieces is { Length: > 0 } p ? p : new[] { b.EffectiveShape! };

        private static double Overlap(SceneBody a, CollisionShape sa, SceneBody b, CollisionShape sb)
        {
            var axes = new List<Vec3>();
            void Frame(SceneBody body, CollisionShape s, List<Vec3> own)
            {
                var q = body.Orientation;
                if (s.Kind == ShapeKind.Box || s.Kind == ShapeKind.Cylinder)
                {
                    own.Add(q.Rotate(Vec3.UnitX));
                    own.Add(q.Rotate(Vec3.UnitY));
                    own.Add(q.Rotate(Vec3.UnitZ));
                }
                else if (s.Kind != ShapeKind.Sphere)
                {
                    foreach (var pl in s.Planes) own.Add(q.Rotate(pl.Normal));
                }
            }

            var ea = new List<Vec3>();
            var eb = new List<Vec3>();
            Frame(a, sa, ea);
            Frame(b, sb, eb);
            axes.AddRange(ea);
            axes.AddRange(eb);
            if (ea.Count <= 3 && eb.Count <= 3)
            {
                foreach (var u in ea)
                    foreach (var v in eb)
                    {
                        var c = u.Cross(v);
                        if (c.LengthSquared > 1e-10) axes.Add(c.Normalized());
                    }
            }

            var centres = PieceCentre(b, sb) - PieceCentre(a, sa);
            if (centres.LengthSquared > 1e-14) axes.Add(centres.Normalized());
            double min = double.PositiveInfinity;
            foreach (var n in axes)
            {
                var (a0, a1) = Project(a, sa, n);
                var (b0, b1) = Project(b, sb, n);
                double overlap = Math.Min(a1, b1) - Math.Max(a0, b0);
                if (overlap <= 0) return 0;
                min = Math.Min(min, overlap);
            }

            return double.IsInfinity(min) ? 0 : min;
        }

        private static Vec3 PieceCentre(SceneBody body, CollisionShape s)
        {
            if (s.Vertices.Count == 0) return body.Position;
            var c = Vec3.Zero;
            foreach (var v in s.Vertices) c += v;
            return body.ToWorld(c / s.Vertices.Count);
        }

        /// <summary>The interval a convex shape covers along a world axis.</summary>
        private static (double Lo, double Hi) Project(SceneBody body, CollisionShape s, Vec3 n)
        {
            var q = body.Orientation;
            var ln = q.InverseRotate(n);
            double c = body.Position.Dot(n);
            switch (s.Kind)
            {
                case ShapeKind.Sphere:
                    return (c - s.Radius, c + s.Radius);
                case ShapeKind.Box:
                {
                    var h = s.HalfExtents;
                    double r = h.X * Math.Abs(ln.X) + h.Y * Math.Abs(ln.Y) + h.Z * Math.Abs(ln.Z);
                    return (c - r, c + r);
                }

                case ShapeKind.Cylinder:
                {
                    double r = s.HalfHeight * Math.Abs(ln.Z) + s.Radius * Math.Sqrt(Math.Max(0, 1 - ln.Z * ln.Z));
                    return (c - r, c + r);
                }

                default:
                {
                    double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
                    foreach (var v in s.Vertices)
                    {
                        double d = v.Dot(ln);
                        lo = Math.Min(lo, d);
                        hi = Math.Max(hi, d);
                    }

                    return double.IsInfinity(lo) ? (c, c) : (c + lo, c + hi);
                }
            }
        }
    }
}
