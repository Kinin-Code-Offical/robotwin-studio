using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    public sealed partial class PhysicsScene
    {
        /// <summary>
        /// [perf] Whether two solids are certainly farther apart than <paramref name="margin"/>. A pair whose bounding boxes
        /// overlap may still be far apart (a motor under a glider's wing): the contact tests then went over every face and
        /// every candidate point for nothing, 27 µs a test for the wing's 64-corner hull against the motor, 66 000 tests a
        /// flight. Gilbert–Johnson–Keerthi on the solids' support functions (a concave one by its hull, which is no farther)
        /// gives at every step a lower bound on their gap; once it passes the margin, no contact can come of the pair, which
        /// is what every test below would find. Not proven (touching, overlapping, or not settled in a few steps): false.
        /// </summary>
        internal static bool Apart(CollisionShape sa, Vec3 xa, Quat qa, CollisionShape sb, Vec3 xb, Quat qb, double margin) =>
            Apart(sa, xa, qa, sb, xb, qb, margin, out _);

        /// <summary>The same, with the lower bound on the gap that proved it (valid only when true).</summary>
        internal static bool Apart(CollisionShape sa, Vec3 xa, Quat qa, CollisionShape sb, Vec3 xb, Quat qb, double margin, out double gap)
        {
            gap = 0;
            margin = Math.Max(0, margin);
            Span<Vec3> s = stackalloc Vec3[4];
            int n = 0;
            var v = xa + qa.Rotate(sa.LocalCentre) - (xb + qb.Rotate(sb.LocalCentre));   // a point of A − B, near its middle
            for (int iter = 0; iter < 24; iter++)
            {
                double vv = v.LengthSquared;
                if (vv < 1e-24) return false;
                var w = xa + qa.Rotate(sa.Support(qa.InverseRotate(-v))) - (xb + qb.Rotate(sb.Support(qb.InverseRotate(v))));
                double vw = v.Dot(w);   // every point of A − B lies at least vw / |v| along v
                if (vw > 0 && vw * vw > margin * margin * vv)
                {
                    gap = vw / Math.Sqrt(vv);
                    return true;
                }

                if (vv - vw <= 1e-9 * vv) return false;   // settled: the gap is |v|, not beyond the margin
                s[n++] = w;
                v = Nearest(s, ref n);
                if (n == 4) return false;   // the origin is inside: they overlap
            }

            return false;
        }

        /// <summary>
        /// A pair proved apart: the gap then, and where the second body stood in the first one's frame (the gap between two
        /// solids depends on that alone). By body: the pair may come either way round.
        /// </summary>
        private struct ApartMemo
        {
            public CollisionShape SA, SB;
            public int A;
            public double Gap;
            public Vec3 T;   // B's position in A's frame
            public Quat R;   // B's orientation in A's frame
        }

        /// <summary>
        /// [perf] Pairs proved apart, by pair and piece: as long as the second body has moved less, in the first one's frame,
        /// than the gap that was proved (a point of its solid moves at most the displacement plus the turn times its reach),
        /// they are still apart and are not tested again. Relative, so two parts of a flying plane that never touch stay
        /// settled at any speed. Forgotten when the bodies change.
        /// </summary>
        private readonly Dictionary<(long Pair, int Piece), ApartMemo> _apart = new Dictionary<(long Pair, int Piece), ApartMemo>();
        private int _apartBodies = -1;

        /// <summary>Whether the pair is known or proved apart now (and remembered so); false: test it.</summary>
        private bool StillApart(int a, int b, CollisionShape sa, Vec3 xa, Quat qa, CollisionShape sb, Vec3 xb, Quat qb, double margin)
        {
            if (_apartBodies != Bodies.Count || _apart.Count > 4096 + 16 * Bodies.Count)
            {
                _apart.Clear();
                _apartBodies = Bodies.Count;
            }

            var key = (PairKey(a, b), _featureBase);
            if (_apart.TryGetValue(key, out var m))
            {
                bool same = m.A == a;
                if (ReferenceEquals(same ? m.SA : m.SB, sa) && ReferenceEquals(same ? m.SB : m.SA, sb))
                {
                    var (t, r) = same ? Relative(xa, qa, xb, qb) : Relative(xb, qb, xa, qa);
                    double dot = Math.Abs(r.W * m.R.W + r.X * m.R.X + r.Y * m.R.Y + r.Z * m.R.Z);
                    double turn = dot >= 1 ? 0 : 2 * Math.Acos(dot);
                    var second = same ? sb : sa;
                    double moved = (t - m.T).Length + turn * (second.LocalCentre.Length + second.LocalRadius);
                    if (m.Gap - moved > Math.Max(0, margin)) return true;
                }
            }

            if (Apart(sa, xa, qa, sb, xb, qb, margin, out double gap))
            {
                var (t, r) = Relative(xa, qa, xb, qb);
                _apart[key] = new ApartMemo { SA = sa, SB = sb, A = a, Gap = gap, T = t, R = r };
                return true;
            }

            _apart.Remove(key);
            return false;
        }

        /// <summary>Body B's pose in body A's frame.</summary>
        private static (Vec3 T, Quat R) Relative(Vec3 xa, Quat qa, Vec3 xb, Quat qb) => (qa.InverseRotate(xb - xa), qa.Conjugate * qb);

        /// <summary>The point of the simplex nearest the origin; the simplex keeps only the corners that carry it.</summary>
        private static Vec3 Nearest(Span<Vec3> s, ref int n)
        {
            switch (n)
            {
                case 1:
                    return s[0];
                case 2:
                {
                    Vec3 a = s[0], ab = s[1] - a;
                    double len = ab.LengthSquared;
                    double t = len > 0 ? Math.Max(0, Math.Min(1, -a.Dot(ab) / len)) : 0;
                    if (t <= 0)
                    {
                        n = 1;
                        return a;
                    }

                    if (t >= 1)
                    {
                        s[0] = s[1];
                        n = 1;
                        return s[0];
                    }

                    return a + ab * t;
                }

                case 3:
                    return Face(s, ref n, 0, 1, 2);
                default:
                {
                    // the faces the origin lies beyond; the nearest of their points
                    Span<int> face = stackalloc int[12] { 0, 1, 2, 3, 0, 2, 3, 1, 0, 3, 1, 2 };
                    double best = double.MaxValue;
                    int bestFace = -1;
                    for (int f = 0; f < 4; f++)
                    {
                        int i0 = f < 3 ? face[f * 4] : 1, i1 = f < 3 ? face[f * 4 + 1] : 3, i2 = f < 3 ? face[f * 4 + 2] : 2, i3 = f < 3 ? face[f * 4 + 3] : 0;
                        Vec3 a = s[i0], b = s[i1], c = s[i2], d = s[i3];
                        var nf = (b - a).Cross(c - a);
                        double sp = (-a).Dot(nf), sd = (d - a).Dot(nf);
                        if (Math.Abs(sd) >= 1e-18 && sp * sd >= 0) continue;   // the origin is on the tetrahedron's side
                        var wt = ConvexDistance.TriangleWeights(a, b, c);
                        var q = a * wt.Wa + b * wt.Wb + c * wt.Wc;
                        double dist = q.LengthSquared;
                        if (dist < best)
                        {
                            best = dist;
                            bestFace = f;
                        }
                    }

                    if (bestFace < 0) return Vec3.Zero;   // inside (n stays 4)
                    int j0 = bestFace < 3 ? face[bestFace * 4] : 1, j1 = bestFace < 3 ? face[bestFace * 4 + 1] : 3, j2 = bestFace < 3 ? face[bestFace * 4 + 2] : 2;
                    Vec3 p0 = s[j0], p1 = s[j1], p2 = s[j2];
                    s[0] = p0;
                    s[1] = p1;
                    s[2] = p2;
                    n = 3;
                    return Face(s, ref n, 0, 1, 2);
                }
            }
        }

        private static Vec3 Face(Span<Vec3> s, ref int n, int ia, int ib, int ic)
        {
            Vec3 a = s[ia], b = s[ib], c = s[ic];
            var w = ConvexDistance.TriangleWeights(a, b, c);
            var p = a * w.Wa + b * w.Wb + c * w.Wc;
            int k = 0;
            if (w.Wa > 0) s[k++] = a;
            if (w.Wb > 0) s[k++] = b;
            if (w.Wc > 0) s[k++] = c;
            if (k == 0) s[k++] = a;
            n = k;
            return p;
        }
    }
}
