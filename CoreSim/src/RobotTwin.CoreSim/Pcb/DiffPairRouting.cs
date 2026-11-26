using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[diffroute] A differential pair as the router lays it: both nets' tracks, their lengths, and whether it fits.</summary>
    public sealed class DiffPairRoute
    {
        public List<Track> Positive { get; } = new List<Track>();
        public List<Track> Negative { get; } = new List<Track>();

        public double PositiveLength => Positive.Sum(t => t.Length);
        public double NegativeLength => Negative.Sum(t => t.Length);

        /// <summary>Length difference (mm): the corners' inner line is the shorter.</summary>
        public double Skew => Math.Abs(PositiveLength - NegativeLength);

        /// <summary>Clear of other nets' copper, and the halves do not cross.</summary>
        public bool Clear { get; internal set; } = true;

        /// <summary>Why it does not fit (null when it does).</summary>
        public string? Problem { get; internal set; }

        public IEnumerable<Track> All => Positive.Concat(Negative);
    }

    /// <summary>
    /// [diffroute] KiCad's "Route Differential Pair": the two nets of a pair (USB D+/D−, CAN H/L, LVDS) laid together
    /// from one centreline, each half offset by half the pair's pitch (track width plus gap) with mitred corners, so they
    /// keep the pair's gap all the way; short fan-outs join them to their pads. The halves are checked against other nets'
    /// copper, and a pair whose two ends are in opposite order (the halves would cross) is flagged.
    /// </summary>
    public static class DiffPairRouting
    {
        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>The pair a net belongs to: one the board defines, else one found from the net names (D+/D−, CANH/CANL …).</summary>
        public static DiffPair? PairOf(BoardDocument board, string net)
        {
            if (string.IsNullOrEmpty(net)) return null;
            return board.DiffPairs.FirstOrDefault(p => Same(p.Positive, net) || Same(p.Negative, net))
                   ?? NetLengths.Detect(board).FirstOrDefault(p => Same(p.Positive, net) || Same(p.Negative, net));
        }

        /// <summary>
        /// The pair's two pads nearest a point: a pad of each net, the two on the same part first (a connector's D+ and
        /// D−, a chip's pair), nearest the point; null when the board has no pad of one of them.
        /// </summary>
        public static (PlacedPad P, PlacedPad N)? PadsNear(BoardDocument board, DiffPair pair, double x, double y)
        {
            var pads = board.Pads().ToList();
            var ps = pads.Where(p => Same(p.Net, pair.Positive)).ToList();
            var ns = pads.Where(p => Same(p.Net, pair.Negative)).ToList();
            if (ps.Count == 0 || ns.Count == 0) return null;
            double D(PlacedPad p) => Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
            var best = ps.SelectMany(p => ns.Select(n => (p, n)))
                .OrderBy(c => ReferenceEquals(c.p.Component, c.n.Component) ? 0 : 1)
                .ThenBy(c => Math.Min(D(c.p), D(c.n)) + Math.Sqrt((c.p.X - c.n.X) * (c.p.X - c.n.X) + (c.p.Y - c.n.Y) * (c.p.Y - c.n.Y)) * 0.25)
                .First();
            return (best.p, best.n);
        }

        /// <summary>
        /// Lays the pair on a layer: from the start pads (P, N), along the centreline points, to the end pads when given
        /// (else the lines stop at the last point). The P half runs on the side its start pad is on.
        /// </summary>
        public static DiffPairRoute Route(BoardDocument board, DiffPair pair, CopperLayer layer, double width, (PcbPoint P, PcbPoint N) start,
            IReadOnlyList<PcbPoint> path, (PcbPoint P, PcbPoint N)? end = null)
        {
            var route = new DiffPairRoute();
            var startMid = Mid(start.P, start.N);
            var centre = new List<PcbPoint> { startMid };
            foreach (var p in path) Add(centre, p);
            if (end is { } e) Add(centre, Mid(e.P, e.N));
            if (centre.Count < 2) return route;

            double half = (width + pair.Gap) / 2;
            // the P half keeps to the side of the first leg its start pad is on
            var d0 = Dir(centre[0], centre[1]);
            double side = Cross(d0, Sub(start.P, startMid)) >= 0 ? 1 : -1;
            var pLine = Offset(centre, half * side);
            var nLine = Offset(centre, -half * side);
            if (end is { } fin)
            {
                var endMid = centre[centre.Count - 1];
                var dl = Dir(centre[centre.Count - 2], endMid);
                double endSide = Cross(dl, Sub(fin.P, endMid)) >= 0 ? 1 : -1;
                if (endSide != side)
                {
                    route.Clear = false;
                    route.Problem = "Çiftin uçları ters sırada: yollar kesişir (bir uçta geçiş viası ya da ters bağlantı gerekir)";
                }
            }

            // each half leaves its pad at 45° and joins its line a little way along (KiCad's breakout), not between the pads
            var pPts = new List<PcbPoint> { start.P };
            var nPts = new List<PcbPoint> { start.N };
            if (end is { } last)
            {
                pPts.AddRange(Join(start.P, last.P, pLine));
                nPts.AddRange(Join(start.N, last.N, nLine));
                pPts.Add(last.P);
                nPts.Add(last.N);
            }
            else
            {
                pPts.AddRange(Enter(start.P, pLine));
                nPts.AddRange(Enter(start.N, nLine));
            }

            route.Positive.AddRange(Tracks(pair.Positive, layer, width, pPts));
            route.Negative.AddRange(Tracks(pair.Negative, layer, width, nPts));

            if (route.Clear)
            {
                var rp = new InteractiveRouter(board, pair.Positive, width);
                var rn = new InteractiveRouter(board, pair.Negative, width);
                if (!rp.PathClear(layer, pPts) || !rn.PathClear(layer, nPts))
                {
                    route.Clear = false;
                    route.Problem = "Çift başka bir ağın bakırına çok yakın";
                }
            }

            return route;
        }

        /// <summary>A half's line from where it is joined from its start pad on, the pad reaching it at 45°.</summary>
        private static List<PcbPoint> Enter(PcbPoint pad, List<PcbPoint> line)
        {
            if (line.Count < 2) return new List<PcbPoint>(line);
            var u = Dir(line[0], line[1]);
            var rel = Sub(pad, line[0]);
            double t = Dot(rel, u) + Math.Abs(Cross(u, rel));   // along the line as far as it is across
            double leg = Math.Sqrt(Sub(line[1], line[0]).X * Sub(line[1], line[0]).X + Sub(line[1], line[0]).Y * Sub(line[1], line[0]).Y);
            var result = new List<PcbPoint>();
            if (t <= 1e-9) result.Add(line[0]);
            else if (t < leg - 1e-6) result.Add(new PcbPoint(line[0].X + u.X * t, line[0].Y + u.Y * t));
            result.AddRange(line.Skip(1));
            return result;
        }

        /// <summary>The line entered from the start pad and left toward the end pad, both at 45°.</summary>
        private static List<PcbPoint> Join(PcbPoint from, PcbPoint to, List<PcbPoint> line)
        {
            var entered = Enter(from, line);
            if (entered.Count < 2) return entered;
            var back = Enter(to, Enumerable.Reverse(entered).ToList());
            back.Reverse();
            return back;
        }

        private static void Add(List<PcbPoint> pts, PcbPoint p)
        {
            var last = pts[pts.Count - 1];
            if (Math.Abs(last.X - p.X) < 1e-6 && Math.Abs(last.Y - p.Y) < 1e-6) return;
            // a point on the straight line through the last two adds nothing
            if (pts.Count >= 2)
            {
                var a = pts[pts.Count - 2];
                if (Math.Abs(Cross(Dir(a, last), Dir(last, p))) < 1e-9 && Dot(Dir(a, last), Dir(last, p)) > 0)
                {
                    pts[pts.Count - 1] = p;
                    return;
                }
            }

            pts.Add(p);
        }

        /// <summary>A polyline moved sideways by a distance (left of its direction when positive), its corners mitred.</summary>
        public static List<PcbPoint> Offset(IReadOnlyList<PcbPoint> line, double by)
        {
            var result = new List<PcbPoint>(line.Count);
            for (int i = 0; i < line.Count; i++)
            {
                PcbPoint n;
                if (i == 0) n = Left(Dir(line[0], line[1]));
                else if (i == line.Count - 1) n = Left(Dir(line[i - 1], line[i]));
                else
                {
                    var a = Left(Dir(line[i - 1], line[i]));
                    var b = Left(Dir(line[i], line[i + 1]));
                    var m = new PcbPoint(a.X + b.X, a.Y + b.Y);
                    double len = Math.Sqrt(m.X * m.X + m.Y * m.Y);
                    if (len < 1e-9)
                    {
                        n = a;   // turning straight back: keep the side
                    }
                    else
                    {
                        m = new PcbPoint(m.X / len, m.Y / len);
                        double k = 1 / Math.Max(0.25, Dot(m, b));   // a mitre keeps the distance from both legs
                        n = new PcbPoint(m.X * k, m.Y * k);
                    }
                }

                result.Add(new PcbPoint(line[i].X + n.X * by, line[i].Y + n.Y * by));
            }

            return result;
        }

        private static IEnumerable<Track> Tracks(string net, CopperLayer layer, double width, List<PcbPoint> pts)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                if (Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6) continue;
                yield return new Track { Net = net, Layer = layer, Width = width, X1 = Math.Round(a.X, 4), Y1 = Math.Round(a.Y, 4), X2 = Math.Round(b.X, 4), Y2 = Math.Round(b.Y, 4) };
            }
        }

        private static PcbPoint Mid(PcbPoint a, PcbPoint b) => new PcbPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        private static PcbPoint Sub(PcbPoint a, PcbPoint b) => new PcbPoint(a.X - b.X, a.Y - b.Y);
        private static double Cross(PcbPoint a, PcbPoint b) => a.X * b.Y - a.Y * b.X;
        private static double Dot(PcbPoint a, PcbPoint b) => a.X * b.X + a.Y * b.Y;
        private static PcbPoint Left(PcbPoint d) => new PcbPoint(-d.Y, d.X);

        private static PcbPoint Dir(PcbPoint a, PcbPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
            return l < 1e-12 ? new PcbPoint(1, 0) : new PcbPoint(dx / l, dy / l);
        }
    }
}
