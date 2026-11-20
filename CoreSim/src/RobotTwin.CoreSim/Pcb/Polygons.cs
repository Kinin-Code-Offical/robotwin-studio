using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>A point in board millimetres (Y down, like the rest of the board model).</summary>
    public struct PcbPoint : IEquatable<PcbPoint>
    {
        public PcbPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; set; }
        public double Y { get; set; }

        public bool Equals(PcbPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is PcbPoint p && Equals(p);
        public override int GetHashCode() => HashCode.Combine(X + 0.0, Y + 0.0);
        public override string ToString() => $"({X:0.####}, {Y:0.####})";
    }

    /// <summary>
    /// A filled area: one outer contour (positive shoelace area) and its holes
    /// (negative area). Produced by <see cref="PolyBool"/> and <see cref="PolyOffset"/>.
    /// </summary>
    public sealed class PolyRegion
    {
        public List<PcbPoint> Outer { get; set; } = new List<PcbPoint>();
        public List<List<PcbPoint>> Holes { get; set; } = new List<List<PcbPoint>>();

        /// <summary>Net filled area (outer minus holes).</summary>
        public double Area => Polygon.Area(Outer) + Holes.Sum(h => Polygon.Area(h));

        public IEnumerable<List<PcbPoint>> Contours()
        {
            yield return Outer;
            foreach (var h in Holes) yield return h;
        }

        public bool Contains(double x, double y)
        {
            if (Polygon.Winding(Outer, x, y) == 0) return false;
            foreach (var h in Holes)
            {
                if (Polygon.Winding(h, x, y) != 0) return false;
            }

            return true;
        }

        public (double MinX, double MinY, double MaxX, double MaxY) Bounds() => Polygon.Bounds(Outer);

        public PolyRegion Clone() => new PolyRegion { Outer = new List<PcbPoint>(Outer), Holes = Holes.Select(h => new List<PcbPoint>(h)).ToList() };
    }

    public enum PolyFillRule
    {
        NonZero,
        Positive,
        EvenOdd
    }

    public enum PolyOp
    {
        Union,
        Intersection,
        Difference,
        Xor
    }

    /// <summary>Simple polygon helpers.</summary>
    public static class Polygon
    {
        /// <summary>Shoelace area: positive for counter-clockwise contours (in a Y-up frame).</summary>
        public static double Area(IReadOnlyList<PcbPoint> p)
        {
            double a = 0;
            for (int i = 0, n = p.Count; i < n; i++)
            {
                var u = p[i];
                var v = p[(i + 1) % n];
                a += u.X * v.Y - v.X * u.Y;
            }

            return a / 2;
        }

        public static int Winding(IReadOnlyList<PcbPoint> p, double x, double y)
        {
            int w = 0;
            for (int i = 0, n = p.Count; i < n; i++)
            {
                var a = p[i];
                var b = p[(i + 1) % n];
                if (a.Y <= y)
                {
                    if (b.Y > y && Cross(a, b, x, y) > 0) w++;
                }
                else if (b.Y <= y && Cross(a, b, x, y) < 0)
                {
                    w--;
                }
            }

            return w;
        }

        private static double Cross(PcbPoint a, PcbPoint b, double x, double y) => (b.X - a.X) * (y - a.Y) - (x - a.X) * (b.Y - a.Y);

        public static (double MinX, double MinY, double MaxX, double MaxY) Bounds(IEnumerable<PcbPoint> pts)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var p in pts)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }

            return (minX, minY, maxX, maxY);
        }

        public static List<PcbPoint> Reversed(IEnumerable<PcbPoint> p)
        {
            var l = new List<PcbPoint>(p);
            l.Reverse();
            return l;
        }

        /// <summary>Returns the contour oriented counter-clockwise (positive area).</summary>
        public static List<PcbPoint> CounterClockwise(IEnumerable<PcbPoint> p)
        {
            var l = new List<PcbPoint>(p);
            if (Area(l) < 0) l.Reverse();
            return l;
        }

        public static bool AnyContains(IEnumerable<PolyRegion> regions, double x, double y)
        {
            foreach (var r in regions)
            {
                var b = r.Bounds();
                if (x < b.MinX || x > b.MaxX || y < b.MinY || y > b.MaxY) continue;
                if (r.Contains(x, y)) return true;
            }

            return false;
        }

        /// <summary>Smallest distance from a point to any edge of the region's contours.</summary>
        public static double DistanceToEdges(PolyRegion r, double x, double y)
        {
            double best = double.PositiveInfinity;
            foreach (var c in r.Contours())
            {
                for (int i = 0, n = c.Count; i < n; i++)
                {
                    var a = c[i];
                    var b = c[(i + 1) % n];
                    double d = Geometry.PointSegmentDistance(x, y, a.X, a.Y, b.X, b.Y);
                    if (d < best) best = d;
                }
            }

            return best;
        }

        /// <summary>
        /// Merges the holes into the outer contour with zero-width cut-ins (KiCad's
        /// "fractured" polygon), giving one simple contour that Gerber regions and KiCad
        /// filled_polygon accept. Holes are linked left to right with horizontal bridges.
        /// </summary>
        public static List<PcbPoint> Fracture(PolyRegion region)
        {
            var outer = new List<PcbPoint>(region.Outer);
            if (Area(outer) < 0) outer.Reverse();
            var holes = region.Holes
                .Select(h => Area(h) > 0 ? Reversed(h) : new List<PcbPoint>(h))
                .Where(h => h.Count >= 3)
                .OrderBy(h => h.Min(p => p.X))
                .ToList();
            foreach (var hole in holes)
            {
                int m = 0;
                for (int i = 1; i < hole.Count; i++)
                {
                    if (hole[i].X < hole[m].X || (hole[i].X == hole[m].X && hole[i].Y < hole[m].Y)) m = i;
                }

                var p = hole[m];
                double bestX = double.NegativeInfinity;
                int edge = -1;
                for (int i = 0, n = outer.Count; i < n; i++)
                {
                    var a = outer[i];
                    var b = outer[(i + 1) % n];
                    if ((a.Y > p.Y) == (b.Y > p.Y)) continue;
                    double x = a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y);
                    if (x <= p.X && x > bestX)
                    {
                        bestX = x;
                        edge = i;
                    }
                }

                if (edge < 0) continue;   // cannot happen for a hole inside the outer contour
                var q = new PcbPoint(bestX, p.Y);
                var merged = new List<PcbPoint>(outer.Count + hole.Count + 3);
                for (int i = 0; i <= edge; i++) merged.Add(outer[i]);
                if (!merged[merged.Count - 1].Equals(q)) merged.Add(q);
                for (int k = 0; k <= hole.Count; k++) merged.Add(hole[(m + k) % hole.Count]);
                merged.Add(q);
                for (int i = edge + 1; i < outer.Count; i++)
                {
                    if (i == edge + 1 && outer[i].Equals(q)) continue;
                    merged.Add(outer[i]);
                }

                outer = merged;
            }

            return outer;
        }
    }

    /// <summary>
    /// Boolean operations on polygon sets by scan-beam decomposition: every distinct
    /// vertex height and every edge crossing splits the plane into horizontal beams in
    /// which no two edges cross; inside each beam the edges are ordered left to right and
    /// the winding numbers of the subject and clip sets decide which trapezoids are
    /// filled. The trapezoids' outline (slanted sides plus the XOR of the horizontal
    /// intervals at each beam boundary) is then linked into contours, and connected
    /// trapezoids give the islands that group holes with their outer contour.
    /// Horizontal and overlapping edges need no special cases.
    /// </summary>
    public static class PolyBool
    {
        private const double Eps = 1e-9;

        private sealed class Edge
        {
            public double X0, Y0, X1, Y1, Slope;
            public int Wind, Cls;
            public double Xb, Xt;

            public double XAt(double y) => y == Y0 ? X0 : y == Y1 ? X1 : X0 + (y - Y0) * Slope;
        }

        private struct Span
        {
            public double LXb, LXt, RXb, RXt;
            public int Id;
        }

        private struct Seg
        {
            public double AX, AY, BX, BY;
            public int Island;
        }

        public static List<PolyRegion> Union(IEnumerable<IReadOnlyList<PcbPoint>> paths, PolyFillRule rule = PolyFillRule.NonZero) =>
            Execute(paths, null, PolyOp.Union, rule);

        public static List<PolyRegion> Union(IEnumerable<PolyRegion> regions) =>
            Execute(regions.SelectMany(r => r.Contours()), null, PolyOp.Union);

        public static List<PolyRegion> Difference(IEnumerable<PolyRegion> subject, IEnumerable<IReadOnlyList<PcbPoint>> clip) =>
            Execute(subject.SelectMany(r => r.Contours()), clip, PolyOp.Difference);

        public static List<PolyRegion> Intersection(IEnumerable<PolyRegion> subject, IEnumerable<IReadOnlyList<PcbPoint>> clip) =>
            Execute(subject.SelectMany(r => r.Contours()), clip, PolyOp.Intersection);

        public static List<PolyRegion> Execute(IEnumerable<IReadOnlyList<PcbPoint>> subject, IEnumerable<IReadOnlyList<PcbPoint>>? clip,
            PolyOp op, PolyFillRule rule = PolyFillRule.NonZero)
        {
            var edges = new List<Edge>();
            AddPaths(edges, subject, 0);
            if (clip != null) AddPaths(edges, clip, 1);
            if (edges.Count == 0) return new List<PolyRegion>();

            edges.Sort((a, b) => a.Y0.CompareTo(b.Y0));
            var ys = new List<double>(edges.Count * 2);
            foreach (var e in edges)
            {
                ys.Add(e.Y0);
                ys.Add(e.Y1);
            }

            ys.Sort();
            int w = 0;
            for (int i = 0; i < ys.Count; i++)
            {
                if (w == 0 || ys[i] != ys[w - 1]) ys[w++] = ys[i];
            }

            ys.RemoveRange(w, ys.Count - w);

            bool Filled(int ws, int wc)
            {
                bool s = rule == PolyFillRule.NonZero ? ws != 0 : rule == PolyFillRule.Positive ? ws > 0 : (ws & 1) != 0;
                bool c = rule == PolyFillRule.NonZero ? wc != 0 : rule == PolyFillRule.Positive ? wc > 0 : (wc & 1) != 0;
                switch (op)
                {
                    case PolyOp.Intersection: return s && c;
                    case PolyOp.Difference: return s && !c;
                    case PolyOp.Xor: return s != c;
                    default: return s || c;
                }
            }

            var active = new List<Edge>();
            var segs = new List<Seg>();
            var parent = new List<int>();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            var prevSpans = new List<Span>();
            double prevTop = double.NaN;
            var spans = new List<Span>();
            int next = 0;

            void EmitBeam(double ya, double yb)
            {
                spans.Clear();
                int ws = 0, wc = 0;
                bool inside = false;
                Edge? left = null;
                foreach (var e in active)
                {
                    if (e.Cls == 0) ws += e.Wind;
                    else wc += e.Wind;
                    bool now = Filled(ws, wc);
                    if (!inside && now)
                    {
                        left = e;
                    }
                    else if (inside && !now)
                    {
                        var s = new Span { LXb = left!.Xb, LXt = left.Xt, RXb = e.Xb, RXt = e.Xt };
                        if (spans.Count > 0)
                        {
                            var last = spans[spans.Count - 1];
                            if (Math.Abs(last.RXb - s.LXb) <= Eps && Math.Abs(last.RXt - s.LXt) <= Eps)
                            {
                                last.RXb = s.RXb;
                                last.RXt = s.RXt;
                                spans[spans.Count - 1] = last;
                                inside = now;
                                continue;
                            }
                        }

                        if (s.RXb - s.LXb > Eps || s.RXt - s.LXt > Eps) spans.Add(s);
                    }

                    inside = now;
                }

                for (int i = 0; i < spans.Count; i++)
                {
                    var s = spans[i];
                    s.Id = parent.Count;
                    parent.Add(s.Id);
                    spans[i] = s;
                    segs.Add(new Seg { AX = s.RXb, AY = ya, BX = s.RXt, BY = yb, Island = s.Id });
                    segs.Add(new Seg { AX = s.LXt, AY = yb, BX = s.LXb, BY = ya, Island = s.Id });
                }

                if (prevTop == ya)
                {
                    // Connectivity across the boundary.
                    int i = 0, j = 0;
                    while (i < prevSpans.Count && j < spans.Count)
                    {
                        var a = prevSpans[i];
                        var b = spans[j];
                        double lo = Math.Max(a.LXt, b.LXb), hi = Math.Min(a.RXt, b.RXb);
                        if (hi - lo > Eps)
                        {
                            int ra = Find(a.Id), rb = Find(b.Id);
                            if (ra != rb) parent[ra] = rb;
                        }

                        if (a.RXt < b.RXb) i++;
                        else j++;
                    }

                    Horizontal(ya, prevSpans, spans, segs);
                }
                else
                {
                    if (!double.IsNaN(prevTop)) Horizontal(prevTop, prevSpans, null, segs);
                    Horizontal(ya, null, spans, segs);
                }

                (prevSpans, spans) = (spans, prevSpans);
                prevTop = yb;
            }

            for (int k = 0; k + 1 < ys.Count; k++)
            {
                double y0 = ys[k], y1 = ys[k + 1];
                active.RemoveAll(e => e.Y1 <= y0);
                while (next < edges.Count && edges[next].Y0 <= y0)
                {
                    if (edges[next].Y1 > y0) active.Add(edges[next]);
                    next++;
                }

                double ya = y0;
                while (ya < y1)
                {
                    foreach (var e in active)
                    {
                        e.Xb = e.XAt(ya);
                        e.Xt = e.XAt(y1);
                    }

                    // Insertion sort: the order barely changes between beams.
                    for (int i = 1; i < active.Count; i++)
                    {
                        var e = active[i];
                        int j = i - 1;
                        while (j >= 0 && Before(e, active[j]))
                        {
                            active[j + 1] = active[j];
                            j--;
                        }

                        active[j + 1] = e;
                    }

                    double yc = y1;
                    for (int i = 0; i + 1 < active.Count; i++)
                    {
                        var a = active[i];
                        var b = active[i + 1];
                        if (a.Xt - b.Xt <= Eps) continue;
                        double d0 = b.Xb - a.Xb, d1 = b.Xt - a.Xt;
                        double t = d0 / (d0 - d1);
                        double yy = ya + t * (y1 - ya);
                        if (yy > ya + Eps && yy < yc) yc = yy;
                    }

                    if (yc < y1 - Eps)
                    {
                        foreach (var e in active) e.Xt = e.XAt(yc);
                        EmitBeam(ya, yc);
                        ya = yc;
                    }
                    else
                    {
                        EmitBeam(ya, y1);
                        ya = y1;
                    }
                }
            }

            if (!double.IsNaN(prevTop)) Horizontal(prevTop, prevSpans, null, segs);
            return Build(segs, Find);
        }

        private static bool Before(Edge a, Edge b) => a.Xb < b.Xb || (a.Xb == b.Xb && a.Xt < b.Xt);

        private static void AddPaths(List<Edge> edges, IEnumerable<IReadOnlyList<PcbPoint>> paths, int cls)
        {
            foreach (var path in paths)
            {
                int n = path.Count;
                if (n < 3) continue;
                for (int i = 0; i < n; i++)
                {
                    var a = path[i];
                    var b = path[(i + 1) % n];
                    if (a.Y == b.Y) continue;
                    if (double.IsNaN(a.X) || double.IsNaN(a.Y) || double.IsNaN(b.X) || double.IsNaN(b.Y)) continue;
                    var e = a.Y < b.Y
                        ? new Edge { X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y, Wind = -1, Cls = cls }
                        : new Edge { X0 = b.X, Y0 = b.Y, X1 = a.X, Y1 = a.Y, Wind = 1, Cls = cls };
                    e.Slope = (e.X1 - e.X0) / (e.Y1 - e.Y0);
                    edges.Add(e);
                }
            }
        }

        /// <summary>
        /// Horizontal boundary pieces at one beam boundary: the signed difference of the
        /// intervals above (bottom sides, running +x) and below (top sides, running -x).
        /// Using signed coverage rather than a boolean XOR keeps every vertex balanced even
        /// when rounding makes neighbouring intervals overlap by a few ulps.
        /// </summary>
        private static void Horizontal(double y, List<Span>? below, List<Span>? above, List<Seg> segs)
        {
            var ev = new List<(double X, int D, int Id)>();
            if (below != null)
            {
                foreach (var s in below)
                {
                    ev.Add((s.LXt, -1, s.Id));
                    ev.Add((s.RXt, +1, s.Id));
                }
            }

            if (above != null)
            {
                foreach (var s in above)
                {
                    ev.Add((s.LXb, +1, s.Id));
                    ev.Add((s.RXb, -1, s.Id));
                }
            }

            if (ev.Count == 0) return;
            ev.Sort((a, b) => a.X.CompareTo(b.X));
            int net = 0, island = ev[0].Id;
            for (int k = 0; k < ev.Count; k++)
            {
                net += ev[k].D;
                if (ev[k].D > 0 || island < 0) island = ev[k].Id;
                if (k + 1 >= ev.Count) break;
                double xa = ev[k].X, xb = ev[k + 1].X;
                if (!(xb > xa) || net == 0) continue;
                for (int m = 0; m < Math.Abs(net); m++)
                {
                    segs.Add(net > 0
                        ? new Seg { AX = xa, AY = y, BX = xb, BY = y, Island = island }
                        : new Seg { AX = xb, AY = y, BX = xa, BY = y, Island = island });
                }
            }
        }

        private readonly struct Key : IEquatable<Key>
        {
            public Key(double x, double y)
            {
                X = x + 0.0;
                Y = y + 0.0;
            }

            public readonly double X, Y;
            public bool Equals(Key o) => X == o.X && Y == o.Y;
            public override bool Equals(object? obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private static List<PolyRegion> Build(List<Seg> segs, Func<int, int> find)
        {
            var outgoing = new Dictionary<Key, List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                var k = new Key(segs[i].AX, segs[i].AY);
                if (!outgoing.TryGetValue(k, out var list)) outgoing[k] = list = new List<int>(2);
                list.Add(i);
            }

            var used = new bool[segs.Count];
            var loops = new List<(List<PcbPoint> Pts, int Island)>();
            for (int s0 = 0; s0 < segs.Count; s0++)
            {
                if (used[s0]) continue;
                used[s0] = true;
                var pts = new List<PcbPoint> { new PcbPoint(segs[s0].AX, segs[s0].AY) };
                int cur = s0;
                bool closed = false;
                for (int guard = 0; guard <= segs.Count; guard++)
                {
                    var c = segs[cur];
                    var at = new Key(c.BX, c.BY);
                    double dx = c.BX - c.AX, dy = c.BY - c.AY;
                    int best = -1;
                    double bestAngle = double.NegativeInfinity;
                    if (outgoing.TryGetValue(at, out var cands))
                    {
                        foreach (int o in cands)
                        {
                            if (used[o] && o != s0) continue;
                            var n = segs[o];
                            double ox = n.BX - n.AX, oy = n.BY - n.AY;
                            double angle = Math.Atan2(dx * oy - dy * ox, dx * ox + dy * oy);
                            if (angle > bestAngle)
                            {
                                bestAngle = angle;
                                best = o;
                            }
                        }
                    }

                    if (best < 0) break;
                    if (best == s0)
                    {
                        closed = true;
                        break;
                    }

                    used[best] = true;
                    pts.Add(new PcbPoint(segs[best].AX, segs[best].AY));
                    cur = best;
                }

                if (!closed) continue;
                var simple = Simplify(pts);
                if (simple.Count >= 3 && Math.Abs(Polygon.Area(simple)) > 1e-12) loops.Add((simple, find(segs[s0].Island)));
            }

            // Group by island: positive loops are outer contours, negative ones holes.
            var regions = new List<PolyRegion>();
            foreach (var g in loops.GroupBy(l => l.Island))
            {
                var outers = g.Where(l => Polygon.Area(l.Pts) > 0).Select(l => new PolyRegion { Outer = l.Pts }).ToList();
                var holes = g.Where(l => Polygon.Area(l.Pts) < 0).Select(l => l.Pts).ToList();
                if (outers.Count == 0) continue;
                foreach (var h in holes)
                {
                    PolyRegion? owner = outers[0];
                    if (outers.Count > 1)
                    {
                        owner = null;
                        double bestArea = double.PositiveInfinity;
                        foreach (var o in outers)
                        {
                            if (Polygon.Winding(o.Outer, h[0].X, h[0].Y) != 0 || Polygon.Winding(o.Outer, (h[0].X + h[1].X) / 2, (h[0].Y + h[1].Y) / 2) != 0)
                            {
                                double a = Polygon.Area(o.Outer);
                                if (a < bestArea)
                                {
                                    bestArea = a;
                                    owner = o;
                                }
                            }
                        }

                        owner ??= outers.OrderByDescending(o => Polygon.Area(o.Outer)).First();
                    }

                    owner.Holes.Add(h);
                }

                regions.AddRange(outers);
            }

            return regions;
        }

        /// <summary>Drops duplicate and collinear points (beam slicing splits every edge).</summary>
        private static List<PcbPoint> Simplify(List<PcbPoint> pts)
        {
            var list = new List<PcbPoint>(pts.Count);
            foreach (var p in pts)
            {
                if (list.Count > 0)
                {
                    var l = list[list.Count - 1];
                    if (Math.Abs(l.X - p.X) < 1e-9 && Math.Abs(l.Y - p.Y) < 1e-9) continue;
                }

                list.Add(p);
            }

            while (list.Count > 1 && Math.Abs(list[0].X - list[list.Count - 1].X) < 1e-9 && Math.Abs(list[0].Y - list[list.Count - 1].Y) < 1e-9)
            {
                list.RemoveAt(list.Count - 1);
            }

            bool changed = true;
            while (changed && list.Count >= 3)
            {
                changed = false;
                var outList = new List<PcbPoint>(list.Count);
                int n = list.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = outList.Count > 0 ? outList[outList.Count - 1] : list[(i - 1 + n) % n];
                    var b = list[i];
                    var c = list[(i + 1) % n];
                    double abx = b.X - a.X, aby = b.Y - a.Y, bcx = c.X - b.X, bcy = c.Y - b.Y;
                    double acx = c.X - a.X, acy = c.Y - a.Y;
                    double len = Math.Sqrt(acx * acx + acy * acy);
                    double dist = len > 1e-12 ? Math.Abs(abx * acy - aby * acx) / len : Math.Sqrt(abx * abx + aby * aby);
                    if (dist < 1e-7 && (abx * bcx + aby * bcy > 0 || len > 1e-9))
                    {
                        changed = true;
                        continue;
                    }

                    outList.Add(b);
                }

                list = outList;
            }

            return list;
        }
    }

    /// <summary>
    /// Polygon offsetting (Minkowski sum/difference with a disc), Clipper style: every
    /// contour is offset edge by edge with round joins on the outside of corners, and the
    /// raw (self-overlapping) result is cleaned up by a positive-winding union.
    /// </summary>
    public static class PolyOffset
    {
        public const double DefaultTolerance = 0.002;

        public static List<PolyRegion> Offset(IEnumerable<PolyRegion> regions, double delta, double tolerance = DefaultTolerance)
        {
            var list = regions.ToList();
            if (Math.Abs(delta) < 1e-9) return list.Select(r => r.Clone()).ToList();
            var raw = new List<IReadOnlyList<PcbPoint>>();
            foreach (var r in list)
            {
                foreach (var c in r.Contours())
                {
                    var o = OffsetContour(c, delta, tolerance);
                    if (o.Count >= 3) raw.Add(o);
                }
            }

            return PolyBool.Execute(raw, null, PolyOp.Union, PolyFillRule.Positive);
        }

        /// <summary>Offsets closed contours (oriented: outer CCW, holes CW).</summary>
        public static List<PolyRegion> OffsetContours(IEnumerable<IReadOnlyList<PcbPoint>> contours, double delta, double tolerance = DefaultTolerance)
        {
            var raw = new List<IReadOnlyList<PcbPoint>>();
            foreach (var c in contours)
            {
                var o = Math.Abs(delta) < 1e-9 ? new List<PcbPoint>(c) : OffsetContour(c, delta, tolerance);
                if (o.Count >= 3) raw.Add(o);
            }

            return PolyBool.Execute(raw, null, PolyOp.Union, PolyFillRule.Positive);
        }

        private static List<PcbPoint> OffsetContour(IReadOnlyList<PcbPoint> c, double delta, double tolerance)
        {
            // Drop duplicate points first so every edge has a direction.
            var pts = new List<PcbPoint>(c.Count);
            foreach (var p in c)
            {
                if (pts.Count == 0 || Math.Abs(pts[pts.Count - 1].X - p.X) > 1e-12 || Math.Abs(pts[pts.Count - 1].Y - p.Y) > 1e-12) pts.Add(p);
            }

            while (pts.Count > 1 && Math.Abs(pts[0].X - pts[pts.Count - 1].X) <= 1e-12 && Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) <= 1e-12) pts.RemoveAt(pts.Count - 1);
            int n = pts.Count;
            var result = new List<PcbPoint>(n * 3);
            if (n < 3) return result;
            double step = Shapes.StepAngle(Math.Abs(delta), tolerance);
            for (int i = 0; i < n; i++)
            {
                var prev = pts[(i - 1 + n) % n];
                var cur = pts[i];
                var next = pts[(i + 1) % n];
                double d1x = cur.X - prev.X, d1y = cur.Y - prev.Y;
                double d2x = next.X - cur.X, d2y = next.Y - cur.Y;
                double l1 = Math.Sqrt(d1x * d1x + d1y * d1y), l2 = Math.Sqrt(d2x * d2x + d2y * d2y);
                d1x /= l1;
                d1y /= l1;
                d2x /= l2;
                d2y /= l2;
                double n1x = d1y * delta, n1y = -d1x * delta;
                double n2x = d2y * delta, n2y = -d2x * delta;
                double cross = d1x * d2y - d1y * d2x, dot = d1x * d2x + d1y * d2y;
                if (Math.Abs(cross) < 1e-12 && dot > 0)
                {
                    result.Add(new PcbPoint(cur.X + n1x, cur.Y + n1y));
                    continue;
                }

                bool round = delta > 0 ? cross > 0 || (Math.Abs(cross) < 1e-12 && dot < 0) : cross < 0;
                if (round)
                {
                    double theta = Math.Atan2(cross, dot);
                    if (Math.Abs(cross) < 1e-12 && dot < 0) theta = delta > 0 ? Math.PI : -Math.PI;
                    int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(theta) / step));
                    for (int k = 0; k <= steps; k++)
                    {
                        double a = theta * k / steps;
                        double ca = Math.Cos(a), sa = Math.Sin(a);
                        result.Add(new PcbPoint(cur.X + n1x * ca - n1y * sa, cur.Y + n1x * sa + n1y * ca));
                    }
                }
                else
                {
                    result.Add(new PcbPoint(cur.X + n1x, cur.Y + n1y));
                    result.Add(cur);
                    result.Add(new PcbPoint(cur.X + n2x, cur.Y + n2y));
                }
            }

            return result;
        }
    }

    /// <summary>Polygon approximations of copper primitives (counter-clockwise, slightly circumscribed so clearances stay safe).</summary>
    public static class Shapes
    {
        /// <summary>Angular step for arcs of radius r with at most <paramref name="tolerance"/> chord error.</summary>
        public static double StepAngle(double r, double tolerance = PolyOffset.DefaultTolerance)
        {
            if (r <= tolerance * 2) return Math.PI / 4;
            double a = 2 * Math.Acos(1 - tolerance / r);
            return Math.Max(2 * Math.PI / 128, Math.Min(Math.PI / 4, a));
        }

        private static void Arc(List<PcbPoint> pts, double cx, double cy, double r, double from, double to, double tolerance, bool circumscribe)
        {
            double step = StepAngle(r, tolerance);
            int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) / step));
            double R = circumscribe ? r / Math.Cos(Math.Abs(to - from) / n / 2) : r;
            for (int i = 0; i <= n; i++)
            {
                double a = from + (to - from) * i / n;
                pts.Add(new PcbPoint(cx + R * Math.Cos(a), cy + R * Math.Sin(a)));
            }
        }

        public static List<PcbPoint> Circle(double cx, double cy, double r, double tolerance = PolyOffset.DefaultTolerance, bool circumscribe = true)
        {
            double step = StepAngle(r, tolerance);
            int n = Math.Max(8, (int)Math.Ceiling(2 * Math.PI / step));
            double R = circumscribe ? r / Math.Cos(Math.PI / n) : r;
            var pts = new List<PcbPoint>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(new PcbPoint(cx + R * Math.Cos(a), cy + R * Math.Sin(a)));
            }

            return pts;
        }

        /// <summary>A segment with round ends: everything within r of the segment (a track).</summary>
        public static List<PcbPoint> Stadium(double x1, double y1, double x2, double y2, double r, double tolerance = PolyOffset.DefaultTolerance)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) return Circle(x1, y1, r, tolerance);
            double a = Math.Atan2(dy, dx);
            var pts = new List<PcbPoint>();
            Arc(pts, x2, y2, r, a - Math.PI / 2, a + Math.PI / 2, tolerance, true);
            Arc(pts, x1, y1, r, a + Math.PI / 2, a + 3 * Math.PI / 2, tolerance, true);
            return pts;
        }

        public static List<PcbPoint> Rect(double minX, double minY, double maxX, double maxY) => new List<PcbPoint>
        {
            new PcbPoint(minX, minY), new PcbPoint(maxX, minY), new PcbPoint(maxX, maxY), new PcbPoint(minX, maxY)
        };

        public static List<PcbPoint> RoundRect(double cx, double cy, double hw, double hh, double radius, double tolerance = PolyOffset.DefaultTolerance)
        {
            radius = Math.Max(0, Math.Min(radius, Math.Min(hw, hh)));
            if (radius < 1e-6) return Rect(cx - hw, cy - hh, cx + hw, cy + hh);
            var pts = new List<PcbPoint>();
            double ix = hw - radius, iy = hh - radius;
            Arc(pts, cx + ix, cy - iy, radius, -Math.PI / 2, 0, tolerance, true);
            Arc(pts, cx + ix, cy + iy, radius, 0, Math.PI / 2, tolerance, true);
            Arc(pts, cx - ix, cy + iy, radius, Math.PI / 2, Math.PI, tolerance, true);
            Arc(pts, cx - ix, cy - iy, radius, Math.PI, 3 * Math.PI / 2, tolerance, true);
            return pts;
        }

        /// <summary>A pad outline grown by <paramref name="inflate"/> (exact Minkowski sum with a disc, up to arc tessellation).</summary>
        public static List<PcbPoint> Pad(PadShape shape, double cx, double cy, double w, double h, double cornerRatio, double inflate,
            double tolerance = PolyOffset.DefaultTolerance)
        {
            double hw = w / 2, hh = h / 2;
            switch (shape)
            {
                case PadShape.Circle:
                    return Circle(cx, cy, Math.Max(hw, hh) + inflate, tolerance);
                case PadShape.Oval:
                    return RoundRect(cx, cy, hw + inflate, hh + inflate, Math.Min(hw, hh) + inflate, tolerance);
                case PadShape.RoundRect:
                    return RoundRect(cx, cy, hw + inflate, hh + inflate, cornerRatio * Math.Min(w, h) + inflate, tolerance);
                default:
                    return RoundRect(cx, cy, hw + inflate, hh + inflate, Math.Max(0, inflate), tolerance);
            }
        }

        /// <summary>A rectangle of the given width along a segment (square ends), e.g. a thermal spoke.</summary>
        public static List<PcbPoint> Bar(double x1, double y1, double x2, double y2, double width)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-12) return new List<PcbPoint>();
            double nx = -dy / len * width / 2, ny = dx / len * width / 2;
            return Polygon.CounterClockwise(new[]
            {
                new PcbPoint(x1 - nx, y1 - ny), new PcbPoint(x2 - nx, y2 - ny), new PcbPoint(x2 + nx, y2 + ny), new PcbPoint(x1 + nx, y1 + ny)
            });
        }
    }
}
