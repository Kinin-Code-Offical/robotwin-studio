using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// Collision filtering inside assemblies and detection of parts that start inside each other.
    /// Only parts that are joined directly, or through a single intermediate body (a wheel on a
    /// motor's shaft rotor, two parts screwed onto the same plate) skip collisions with each other;
    /// everything else in an assembly collides like in the real world, so a wheel cannot pass
    /// through the chassis it is mounted under.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        /// <summary>Joint hops within which two bodies of one assembly do not collide.</summary>
        public const int NoCollideHops = 2;

        private void FilterAssemblyCollisions()
        {
            var adjacency = new Dictionary<SceneBody, List<SceneBody>>();
            void Link(SceneBody a, SceneBody b)
            {
                if (!adjacency.TryGetValue(a, out var la)) adjacency[a] = la = new List<SceneBody>();
                if (!adjacency.TryGetValue(b, out var lb)) adjacency[b] = lb = new List<SceneBody>();
                la.Add(b);
                lb.Add(a);
            }

            foreach (var j in Scene.Joints)
            {
                if (_shellJoints.Contains(j)) continue;   // [shell] walls collide with what sits inside
                if (j.B != null) Link(j.A, j.B);
            }

            var seen = new HashSet<(SceneBody, SceneBody)>();
            foreach (var start in adjacency.Keys)
            {
                // Breadth-first up to NoCollideHops joints away.
                var depth = new Dictionary<SceneBody, int> { [start] = 0 };
                var queue = new Queue<SceneBody>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    var b = queue.Dequeue();
                    int d = depth[b];
                    if (d == NoCollideHops) continue;
                    foreach (var n in adjacency[b])
                    {
                        if (depth.ContainsKey(n)) continue;
                        depth[n] = d + 1;
                        queue.Enqueue(n);
                    }
                }

                foreach (var other in depth.Keys)
                {
                    if (ReferenceEquals(other, start)) continue;
                    var key = start.GetHashCode() <= other.GetHashCode() ? (start, other) : (other, start);
                    if (seen.Add(key)) Scene.NoCollide.Add(key);
                }
            }
        }

        /// <summary>
        /// Pairs of objects whose collision shapes overlap by more than <paramref name="tolerance"/> (m)
        /// although they are meant to collide — e.g. a wheel placed inside the chassis. Uses oriented
        /// bounding boxes of the collision shapes (separating-axis test) and reports the overlap depth.
        /// </summary>
        public IReadOnlyList<(string A, string B, double Depth)> FindInterpenetrations(double tolerance = 0.0005)
        {
            var result = new List<(string, string, double)>();
            var ignore = new HashSet<(SceneBody, SceneBody)>();
            foreach (var (a, b) in Scene.NoCollide)
            {
                ignore.Add((a, b));
                ignore.Add((b, a));
            }

            var list = _items.Values.Where(i => i.Body.Collides && i.Body.EffectiveShape != null).ToList();
            for (int x = 0; x < list.Count; x++)
            {
                for (int y = x + 1; y < list.Count; y++)
                {
                    var a = list[x].Body;
                    var b = list[y].Body;
                    if (a.IsStatic && b.IsStatic) continue;
                    if (ignore.Contains((a, b))) continue;
                    double depth = ObbOverlap(a, b);
                    if (depth > tolerance) result.Add((list[x].Object.Id, list[y].Object.Id, depth));
                }
            }

            return result;
        }

        private static (Vec3 C, Vec3[] Axes, Vec3 Half) Obb(SceneBody body)
        {
            var s = body.EffectiveShape!;
            if (s.Kind == ShapeKind.ConvexHull && s.Vertices.Count >= 4)
            {
                // A part shaped by its model: the tight box around its hull in the body frame.
                var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
                var hi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
                foreach (var v in s.Vertices)
                {
                    lo = new Vec3(Math.Min(lo.X, v.X), Math.Min(lo.Y, v.Y), Math.Min(lo.Z, v.Z));
                    hi = new Vec3(Math.Max(hi.X, v.X), Math.Max(hi.Y, v.Y), Math.Max(hi.Z, v.Z));
                }

                var bq = body.Orientation;
                return (body.Position + bq.Rotate((lo + hi) * 0.5), new[] { bq.Rotate(Vec3.UnitX), bq.Rotate(Vec3.UnitY), bq.Rotate(Vec3.UnitZ) }, (hi - lo) * 0.5);
            }

            Vec3 half = s.Kind switch
            {
                ShapeKind.Box => s.HalfExtents,
                ShapeKind.Sphere => new Vec3(s.Radius, s.Radius, s.Radius),
                ShapeKind.Cylinder => new Vec3(s.Radius, s.Radius, s.HalfHeight),
                _ => s.HalfExtents.LengthSquared > 0 ? s.HalfExtents : new Vec3(s.BoundingRadius, s.BoundingRadius, s.BoundingRadius)
            };
            var q = body.Orientation;
            return (body.Position, new[] { q.Rotate(Vec3.UnitX), q.Rotate(Vec3.UnitY), q.Rotate(Vec3.UnitZ) }, half);
        }

        /// <summary>Penetration depth of two oriented boxes (0 when separated), minimum over the 15 SAT axes.</summary>
        private static double ObbOverlap(SceneBody ba, SceneBody bb)
        {
            var a = Obb(ba);
            var b = Obb(bb);
            var d = b.C - a.C;
            double min = double.PositiveInfinity;
            IEnumerable<Vec3> Axes()
            {
                foreach (var u in a.Axes) yield return u;
                foreach (var v in b.Axes) yield return v;
                foreach (var u in a.Axes)
                {
                    foreach (var v in b.Axes)
                    {
                        var c = u.Cross(v);
                        if (c.LengthSquared > 1e-10) yield return c.Normalized();
                    }
                }
            }

            foreach (var axis in Axes())
            {
                double ra = a.Half.X * Math.Abs(a.Axes[0].Dot(axis)) + a.Half.Y * Math.Abs(a.Axes[1].Dot(axis)) + a.Half.Z * Math.Abs(a.Axes[2].Dot(axis));
                double rb = b.Half.X * Math.Abs(b.Axes[0].Dot(axis)) + b.Half.Y * Math.Abs(b.Axes[1].Dot(axis)) + b.Half.Z * Math.Abs(b.Axes[2].Dot(axis));
                double overlap = ra + rb - Math.Abs(d.Dot(axis));
                if (overlap <= 0) return 0;
                if (overlap < min) min = overlap;
            }

            return min;
        }
    }
}
