using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// Ear-clipping triangulation of simple polygons with holes (an "earcut"-style
    /// linked-list algorithm: hole bridging, ear clipping, local-intersection cure and
    /// split fallback). Collinear boundary points are kept on the first pass so that
    /// faces with T-junction vertices still triangulate watertight.
    /// </summary>
    public static class Triangulator
    {
        private sealed class Node
        {
            public int I;
            public double X, Y;
            public Node Prev = null!, Next = null!;
            public bool Steiner;
        }

        /// <summary>
        /// Triangulates an outer ring (any orientation) with optional holes. Returned indices
        /// refer to the concatenation outer + holes[0] + holes[1] …; triangles are counter-clockwise.
        /// </summary>
        public static List<int> Triangulate(IReadOnlyList<Vec2> outer, IReadOnlyList<IReadOnlyList<Vec2>>? holes = null)
        {
            var tris = new List<int>();
            if (outer.Count < 3) return tris;
            var outerNode = LinkedList(outer, 0, true);
            if (outerNode == null || outerNode.Next == outerNode.Prev) return tris;
            if (holes != null && holes.Count > 0)
            {
                int offset = outer.Count;
                var queue = new List<Node>();
                foreach (var h in holes)
                {
                    if (h.Count >= 3)
                    {
                        var list = LinkedList(h, offset, false);
                        if (list != null)
                        {
                            if (list == list.Next) list.Steiner = true;
                            queue.Add(LeftMost(list));
                        }
                    }

                    offset += h.Count;
                }

                queue.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
                foreach (var h in queue) outerNode = EliminateHole(h, outerNode);
            }

            EarcutLinked(outerNode, tris, 0);
            return tris;
        }

        /// <summary>Triangulates a planar 3D polygon; returns corner indices (0..n-1), counter-clockwise about <paramref name="normal"/>.</summary>
        public static List<int> TriangulatePolygon(IReadOnlyList<Vec3> points, Vec3 normal)
        {
            int n = points.Count;
            var result = new List<int>(Math.Max(0, (n - 2) * 3));
            if (n < 3) return result;
            if (n == 3)
            {
                result.Add(0);
                result.Add(1);
                result.Add(2);
                return result;
            }

            var (u, v) = Basis(normal);
            var pts = new Vec2[n];
            for (int i = 0; i < n; i++) pts[i] = new Vec2(points[i].Dot(u), points[i].Dot(v));
            if (n == 4 && IsConvexQuad(pts))
            {
                // Split along the shorter diagonal (nicer for subdivision and shading).
                double d02 = (pts[2] - pts[0]).Length, d13 = (pts[3] - pts[1]).Length;
                if (d02 <= d13) result.AddRange(new[] { 0, 1, 2, 0, 2, 3 });
                else result.AddRange(new[] { 1, 2, 3, 1, 3, 0 });
                return result;
            }

            result.AddRange(Triangulate(pts));
            return result;
        }

        /// <summary>Orthonormal (u, v) with u × v = n.</summary>
        public static (Vec3 U, Vec3 V) Basis(Vec3 normal)
        {
            var n = normal.Normalized();
            if (n.Length < 0.5) n = Vec3.UnitZ;
            var a = Math.Abs(n.X) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            var u = (a - n * a.Dot(n)).Normalized();
            var v = n.Cross(u);
            return (u, v);
        }

        private static bool IsConvexQuad(Vec2[] p)
        {
            for (int i = 0; i < 4; i++)
            {
                var a = p[i];
                var b = p[(i + 1) & 3];
                var c = p[(i + 2) & 3];
                if ((b - a).Cross(c - b) <= 1e-12 * Math.Max(1, (b - a).Length * (c - b).Length)) return false;
            }

            return true;
        }

        public static double SignedArea(IReadOnlyList<Vec2> ring)
        {
            double s = 0;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) s += ring[j].Cross(ring[i]);
            return s / 2;
        }

        // ------------------------------------------------------------------ linked list

        private static Node? LinkedList(IReadOnlyList<Vec2> pts, int offset, bool ccw)
        {
            Node? last = null;
            double area = SignedArea(pts);
            if (ccw == (area > 0))
            {
                for (int i = 0; i < pts.Count; i++) last = Insert(offset + i, pts[i].X, pts[i].Y, last);
            }
            else
            {
                for (int i = pts.Count - 1; i >= 0; i--) last = Insert(offset + i, pts[i].X, pts[i].Y, last);
            }

            if (last != null && EqualsPt(last, last.Next))
            {
                Remove(last);
                last = last.Next;
            }

            return last;
        }

        private static Node Insert(int i, double x, double y, Node? last)
        {
            var p = new Node { I = i, X = x, Y = y };
            if (last == null)
            {
                p.Prev = p;
                p.Next = p;
            }
            else
            {
                p.Next = last.Next;
                p.Prev = last;
                last.Next.Prev = p;
                last.Next = p;
            }

            return p;
        }

        private static void Remove(Node p)
        {
            p.Next.Prev = p.Prev;
            p.Prev.Next = p.Next;
        }

        private static bool EqualsPt(Node a, Node b) => a.X == b.X && a.Y == b.Y;

        /// <summary>Twice the signed area of (p, q, r), negative for a counter-clockwise (left) turn.</summary>
        private static double Area(Node p, Node q, Node r) => (q.Y - p.Y) * (r.X - q.X) - (q.X - p.X) * (r.Y - q.Y);

        private static Node? Filter(Node? start, Node? end, bool keepCollinear)
        {
            if (start == null) return null;
            end ??= start;
            var p = start;
            bool again;
            do
            {
                again = false;
                if (!p.Steiner && (EqualsPt(p, p.Next) || (!keepCollinear && Area(p.Prev, p, p.Next) == 0)))
                {
                    Remove(p);
                    p = end = p.Prev;
                    if (p == p.Next) break;
                    again = true;
                }
                else
                {
                    p = p.Next;
                }
            } while (again || p != end);

            return end;
        }

        // ------------------------------------------------------------------ ear clipping

        private static void EarcutLinked(Node? ear, List<int> tris, int pass)
        {
            if (ear == null) return;
            var stop = ear;
            int guard = 0;
            while (ear.Prev != ear.Next)
            {
                if (++guard > 10_000_000) return;
                var prev = ear.Prev;
                var next = ear.Next;
                if (IsEar(ear))
                {
                    tris.Add(prev.I);
                    tris.Add(ear.I);
                    tris.Add(next.I);
                    Remove(ear);
                    ear = next.Next;
                    stop = next.Next;
                    continue;
                }

                ear = next;
                if (ear == stop)
                {
                    if (pass == 0) EarcutLinked(Filter(ear, null, false), tris, 1);
                    else if (pass == 1)
                    {
                        var cured = CureLocalIntersections(Filter(ear, null, false), tris);
                        EarcutLinked(cured, tris, 2);
                    }
                    else if (pass == 2) SplitEarcut(ear, tris);

                    break;
                }
            }
        }

        private static bool IsEar(Node ear)
        {
            var a = ear.Prev;
            var b = ear;
            var c = ear.Next;
            if (Area(a, b, c) >= 0) return false;
            double x0 = Math.Min(a.X, Math.Min(b.X, c.X)), y0 = Math.Min(a.Y, Math.Min(b.Y, c.Y));
            double x1 = Math.Max(a.X, Math.Max(b.X, c.X)), y1 = Math.Max(a.Y, Math.Max(b.Y, c.Y));
            var p = c.Next;
            while (p != a)
            {
                if (p.X >= x0 && p.X <= x1 && p.Y >= y0 && p.Y <= y1 &&
                    !(p.X == a.X && p.Y == a.Y) && !(p.X == c.X && p.Y == c.Y) && !(p.X == b.X && p.Y == b.Y) &&
                    PointInTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, p.X, p.Y) && Area(p.Prev, p, p.Next) >= 0) return false;
                p = p.Next;
            }

            return true;
        }

        private static bool PointInTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
            (cx - px) * (ay - py) >= (ax - px) * (cy - py) &&
            (ax - px) * (by - py) >= (bx - px) * (ay - py) &&
            (bx - px) * (cy - py) >= (cx - px) * (by - py);

        private static Node? CureLocalIntersections(Node? start, List<int> tris)
        {
            if (start == null) return null;
            var p = start;
            do
            {
                var a = p.Prev;
                var b = p.Next.Next;
                if (!EqualsPt(a, b) && Intersects(a, p, p.Next, b) && LocallyInside(a, b) && LocallyInside(b, a))
                {
                    tris.Add(a.I);
                    tris.Add(p.I);
                    tris.Add(b.I);
                    Remove(p);
                    Remove(p.Next);
                    p = start = b;
                }

                p = p.Next;
            } while (p != start);

            return Filter(p, null, false);
        }

        private static void SplitEarcut(Node start, List<int> tris)
        {
            var a = start;
            do
            {
                var b = a.Next.Next;
                while (b != a.Prev)
                {
                    if (a.I != b.I && IsValidDiagonal(a, b))
                    {
                        var c = Split(a, b);
                        var a2 = Filter(a, a.Next, false);
                        var c2 = Filter(c, c.Next, false);
                        EarcutLinked(a2, tris, 0);
                        EarcutLinked(c2, tris, 0);
                        return;
                    }

                    b = b.Next;
                }

                a = a.Next;
            } while (a != start);
        }

        private static bool IsValidDiagonal(Node a, Node b) =>
            a.Next.I != b.I && a.Prev.I != b.I && !IntersectsPolygon(a, b) &&
            ((LocallyInside(a, b) && LocallyInside(b, a) && MiddleInside(a, b) && (Area(a.Prev, a, b.Prev) != 0 || Area(a, b.Prev, b) != 0)) ||
             (EqualsPt(a, b) && Area(a.Prev, a, a.Next) > 0 && Area(b.Prev, b, b.Next) > 0));

        private static int Sign(double v) => v > 0 ? 1 : v < 0 ? -1 : 0;

        private static bool OnSegment(Node p, Node q, Node r) =>
            q.X <= Math.Max(p.X, r.X) && q.X >= Math.Min(p.X, r.X) && q.Y <= Math.Max(p.Y, r.Y) && q.Y >= Math.Min(p.Y, r.Y);

        private static bool Intersects(Node p1, Node q1, Node p2, Node q2)
        {
            int o1 = Sign(Area(p1, q1, p2)), o2 = Sign(Area(p1, q1, q2)), o3 = Sign(Area(p2, q2, p1)), o4 = Sign(Area(p2, q2, q1));
            if (o1 != o2 && o3 != o4) return true;
            if (o1 == 0 && OnSegment(p1, p2, q1)) return true;
            if (o2 == 0 && OnSegment(p1, q2, q1)) return true;
            if (o3 == 0 && OnSegment(p2, p1, q2)) return true;
            if (o4 == 0 && OnSegment(p2, q1, q2)) return true;
            return false;
        }

        private static bool IntersectsPolygon(Node a, Node b)
        {
            var p = a;
            do
            {
                if (p.I != a.I && p.Next.I != a.I && p.I != b.I && p.Next.I != b.I && Intersects(p, p.Next, a, b)) return true;
                p = p.Next;
            } while (p != a);

            return false;
        }

        private static bool LocallyInside(Node a, Node b) =>
            Area(a.Prev, a, a.Next) < 0
                ? Area(a, b, a.Next) >= 0 && Area(a, a.Prev, b) >= 0
                : Area(a, b, a.Prev) < 0 || Area(a, a.Next, b) < 0;

        private static bool MiddleInside(Node a, Node b)
        {
            var p = a;
            bool inside = false;
            double px = (a.X + b.X) / 2, py = (a.Y + b.Y) / 2;
            do
            {
                if ((p.Y > py) != (p.Next.Y > py) && p.Next.Y != p.Y && px < (p.Next.X - p.X) * (py - p.Y) / (p.Next.Y - p.Y) + p.X) inside = !inside;
                p = p.Next;
            } while (p != a);

            return inside;
        }

        private static Node Split(Node a, Node b)
        {
            var a2 = new Node { I = a.I, X = a.X, Y = a.Y };
            var b2 = new Node { I = b.I, X = b.X, Y = b.Y };
            var an = a.Next;
            var bp = b.Prev;
            a.Next = b;
            b.Prev = a;
            a2.Next = an;
            an.Prev = a2;
            b2.Next = a2;
            a2.Prev = b2;
            bp.Next = b2;
            b2.Prev = bp;
            return b2;
        }

        // ------------------------------------------------------------------ holes

        private static Node LeftMost(Node start)
        {
            var p = start;
            var left = start;
            do
            {
                if (p.X < left.X || (p.X == left.X && p.Y < left.Y)) left = p;
                p = p.Next;
            } while (p != start);

            return left;
        }

        private static Node EliminateHole(Node hole, Node outer)
        {
            var bridge = FindHoleBridge(hole, outer);
            if (bridge == null) return outer;
            var bridgeReverse = Split(bridge, hole);
            Filter(bridgeReverse, bridgeReverse.Next, true);
            return Filter(bridge, bridge.Next, true) ?? outer;
        }

        private static Node? FindHoleBridge(Node hole, Node outer)
        {
            var p = outer;
            double hx = hole.X, hy = hole.Y, qx = double.NegativeInfinity;
            Node? m = null;
            do
            {
                if (hy <= p.Y && hy >= p.Next.Y && p.Next.Y != p.Y)
                {
                    double x = p.X + (hy - p.Y) * (p.Next.X - p.X) / (p.Next.Y - p.Y);
                    if (x <= hx && x > qx)
                    {
                        qx = x;
                        m = p.X < p.Next.X ? p : p.Next;
                        if (x == hx) return m;
                    }
                }

                p = p.Next;
            } while (p != outer);

            if (m == null) return null;
            var stop = m;
            double mx = m.X, my = m.Y, tanMin = double.PositiveInfinity;
            p = m;
            do
            {
                if (hx >= p.X && p.X >= mx && hx != p.X &&
                    PointInTriangle(hy < my ? hx : qx, hy, mx, my, hy < my ? qx : hx, hy, p.X, p.Y))
                {
                    double tan = Math.Abs(hy - p.Y) / (hx - p.X);
                    if (LocallyInside(p, hole) && (tan < tanMin || (tan == tanMin && (p.X > m.X || (p.X == m.X && SectorContainsSector(m, p))))))
                    {
                        m = p;
                        tanMin = tan;
                    }
                }

                p = p.Next;
            } while (p != stop);

            return m;
        }

        private static bool SectorContainsSector(Node m, Node p) => Area(m.Prev, m, p.Prev) < 0 && Area(p.Next, m, m.Next) < 0;
    }
}
