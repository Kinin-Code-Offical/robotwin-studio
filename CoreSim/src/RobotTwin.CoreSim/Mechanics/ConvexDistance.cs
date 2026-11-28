using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>[clearance] How far apart two convex shapes are, and the two nearest points.</summary>
    public readonly struct ConvexGap
    {
        public ConvexGap(double distance, Vec3 onA, Vec3 onB, bool overlapping)
        {
            Distance = distance;
            OnA = onA;
            OnB = onB;
            Overlapping = overlapping;
        }

        /// <summary>The gap (the shapes' units); 0 when they touch or overlap.</summary>
        public double Distance { get; }

        /// <summary>The nearest point of the first shape.</summary>
        public Vec3 OnA { get; }

        /// <summary>The nearest point of the second shape.</summary>
        public Vec3 OnB { get; }

        /// <summary>The shapes share some space (their gap is 0 and the points mean nothing).</summary>
        public bool Overlapping { get; }
    }

    /// <summary>
    /// [clearance] The distance between two convex point sets (their hulls) by Gilbert–Johnson–Keerthi: the point of the
    /// Minkowski difference nearest the origin, found by a simplex of at most four support points that moves toward the
    /// origin each step; the nearest points of the two shapes come from the same weights.
    /// </summary>
    public static class ConvexDistance
    {
        private struct Vertex
        {
            public Vec3 W, A, B;   // w = a − b
        }

        public static ConvexGap Between(IReadOnlyList<Vec3> a, IReadOnlyList<Vec3> b)
        {
            if (a.Count == 0 || b.Count == 0) throw new ArgumentException("Boş şekil", a.Count == 0 ? nameof(a) : nameof(b));
            var simplex = new List<Vertex>(4) { new Vertex { W = a[0] - b[0], A = a[0], B = b[0] } };
            var weights = new List<double> { 1 };
            Vec3 v = simplex[0].W;
            double scale = Math.Max(1e-12, Extent(a) + Extent(b));
            double eps = scale * scale * 1e-12;
            for (int iter = 0; iter < 96; iter++)
            {
                double vv = v.LengthSquared;
                if (vv <= eps) return Overlap(simplex, weights);
                var w = Support(a, b, -v);
                // no support point is meaningfully nearer the origin than v: v is the answer
                if (vv - v.Dot(w.W) <= vv * 1e-10 + eps) break;
                bool seen = false;
                foreach (var s in simplex)
                {
                    if ((s.W - w.W).LengthSquared <= eps)
                    {
                        seen = true;
                        break;
                    }
                }

                if (seen) break;
                simplex.Add(w);
                (v, weights) = Closest(simplex);
                if (simplex.Count == 4) return Overlap(simplex, weights);   // the origin is inside the tetrahedron
            }

            Vec3 onA = Vec3.Zero, onB = Vec3.Zero;
            for (int i = 0; i < simplex.Count; i++)
            {
                onA += simplex[i].A * weights[i];
                onB += simplex[i].B * weights[i];
            }

            double d = v.Length;
            return d <= Math.Sqrt(eps) ? new ConvexGap(0, onA, onB, true) : new ConvexGap(d, onA, onB, false);
        }

        private static ConvexGap Overlap(List<Vertex> simplex, List<double> weights)
        {
            Vec3 p = Vec3.Zero;
            for (int i = 0; i < simplex.Count; i++) p += simplex[i].A * (i < weights.Count ? weights[i] : 0);
            return new ConvexGap(0, p, p, true);
        }

        private static double Extent(IReadOnlyList<Vec3> pts)
        {
            Vec3 lo = pts[0], hi = pts[0];
            foreach (var p in pts)
            {
                lo = Vec3.Min(lo, p);
                hi = Vec3.Max(hi, p);
            }

            return (hi - lo).Length;
        }

        private static Vertex Support(IReadOnlyList<Vec3> a, IReadOnlyList<Vec3> b, Vec3 d)
        {
            var pa = Farthest(a, d);
            var pb = Farthest(b, -d);
            return new Vertex { W = pa - pb, A = pa, B = pb };
        }

        private static Vec3 Farthest(IReadOnlyList<Vec3> pts, Vec3 d)
        {
            var best = pts[0];
            double bd = best.Dot(d);
            for (int i = 1; i < pts.Count; i++)
            {
                double t = pts[i].Dot(d);
                if (t > bd)
                {
                    bd = t;
                    best = pts[i];
                }
            }

            return best;
        }

        /// <summary>The simplex's point nearest the origin; the simplex keeps only the vertices that carry it, with their weights.</summary>
        private static (Vec3 Point, List<double> Weights) Closest(List<Vertex> s)
        {
            switch (s.Count)
            {
                case 1:
                    return (s[0].W, new List<double> { 1 });
                case 2:
                    return Segment(s, 0, 1);
                case 3:
                    return Triangle(s, 0, 1, 2);
                default:
                    return Tetrahedron(s);
            }
        }

        private static (Vec3, List<double>) Keep(List<Vertex> s, params (int Index, double Weight)[] keep)
        {
            var kept = new List<Vertex>(keep.Length);
            var w = new List<double>(keep.Length);
            var p = Vec3.Zero;
            foreach (var (i, weight) in keep)
            {
                kept.Add(s[i]);
                w.Add(weight);
                p += s[i].W * weight;
            }

            s.Clear();
            s.AddRange(kept);
            return (p, w);
        }

        private static (Vec3, List<double>) Segment(List<Vertex> s, int ia, int ib)
        {
            Vec3 a = s[ia].W, b = s[ib].W, ab = b - a;
            double len = ab.LengthSquared;
            double t = len > 0 ? Math.Max(0, Math.Min(1, -a.Dot(ab) / len)) : 0;
            if (t <= 0) return Keep(s, (ia, 1));
            if (t >= 1) return Keep(s, (ib, 1));
            return Keep(s, (ia, 1 - t), (ib, t));
        }

        /// <summary>Ericson's closest point of a triangle to a point (the origin), by its Voronoi regions.</summary>
        private static (Vec3, List<double>) Triangle(List<Vertex> s, int ia, int ib, int ic)
        {
            var r = TriangleWeights(s[ia].W, s[ib].W, s[ic].W);
            var keep = new List<(int, double)>(3);
            if (r.Wa > 0) keep.Add((ia, r.Wa));
            if (r.Wb > 0) keep.Add((ib, r.Wb));
            if (r.Wc > 0) keep.Add((ic, r.Wc));
            if (keep.Count == 0) keep.Add((ia, 1));
            return Keep(s, keep.ToArray());
        }

        internal static (double Wa, double Wb, double Wc) TriangleWeights(Vec3 a, Vec3 b, Vec3 c)
        {
            var p = Vec3.Zero;
            Vec3 ab = b - a, ac = c - a, ap = p - a;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            if (d1 <= 0 && d2 <= 0) return (1, 0, 0);
            var bp = p - b;
            double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
            if (d3 >= 0 && d4 <= d3) return (0, 1, 0);
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                double v = d1 / (d1 - d3);
                return (1 - v, v, 0);
            }

            var cp = p - c;
            double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
            if (d6 >= 0 && d5 <= d6) return (0, 0, 1);
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                double w = d2 / (d2 - d6);
                return (1 - w, 0, w);
            }

            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            {
                double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return (0, 1 - w, w);
            }

            double sum = va + vb + vc;
            if (Math.Abs(sum) < 1e-300) return (1, 0, 0);
            double denom = 1 / sum;
            double vv = vb * denom, ww = vc * denom;
            return (1 - vv - ww, vv, ww);
        }

        private static (Vec3, List<double>) Tetrahedron(List<Vertex> s)
        {
            int[][] faces = { new[] { 0, 1, 2, 3 }, new[] { 0, 2, 3, 1 }, new[] { 0, 3, 1, 2 }, new[] { 1, 3, 2, 0 } };
            double best = double.MaxValue;
            (double, double, double) bestW = default;
            int[]? bestFace = null;
            bool outside = false;
            foreach (var f in faces)
            {
                Vec3 a = s[f[0]].W, b = s[f[1]].W, c = s[f[2]].W, d = s[f[3]].W;
                var n = (b - a).Cross(c - a);
                double sp = (-a).Dot(n), sd = (d - a).Dot(n);
                bool flat = Math.Abs(sd) < 1e-18;
                if (!flat && sp * sd >= 0) continue;   // the origin is on the tetrahedron's side of this face
                outside = true;
                var w = TriangleWeights(a, b, c);
                var q = a * w.Wa + b * w.Wb + c * w.Wc;
                double dist = q.LengthSquared;
                if (dist < best)
                {
                    best = dist;
                    bestW = w;
                    bestFace = f;
                }
            }

            if (!outside || bestFace == null) return (Vec3.Zero, new List<double> { 0.25, 0.25, 0.25, 0.25 });   // inside: they overlap
            var keep = new List<(int, double)>(3);
            if (bestW.Item1 > 0) keep.Add((bestFace[0], bestW.Item1));
            if (bestW.Item2 > 0) keep.Add((bestFace[1], bestW.Item2));
            if (bestW.Item3 > 0) keep.Add((bestFace[2], bestW.Item3));
            if (keep.Count == 0) keep.Add((bestFace[0], 1));
            return Keep(s, keep.ToArray());
        }
    }
}
