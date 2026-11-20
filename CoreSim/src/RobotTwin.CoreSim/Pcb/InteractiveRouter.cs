using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// DRC-aware support for drawing one track: exact clearance checks of segments and vias
    /// against every foreign copper item and the board edge, and a walkaround path finder
    /// that follows the requested 45°/90° posture when the direct elbow is clear and
    /// otherwise hugs around obstacles (A* on a fine grid). When the target itself is inside
    /// a keep-out the path stops at the reachable point nearest to it.
    /// </summary>
    public sealed class InteractiveRouter
    {
        private readonly BoardDocument _board;
        private readonly Dictionary<CopperLayer, LayerObstacles> _layers = new Dictionary<CopperLayer, LayerObstacles>();
        private readonly List<PcbPoint> _outline;

        private sealed class Obstacle
        {
            public double X1, Y1, X2, Y2, Half, Clearance;
            public PlacedPad? Pad;
            public double MinX, MinY, MaxX, MaxY;
        }

        private sealed class LayerObstacles
        {
            public readonly Dictionary<long, List<Obstacle>> Buckets = new Dictionary<long, List<Obstacle>>();
            public double Reach;
        }

        private const double Bucket = 2.0;

        public InteractiveRouter(BoardDocument board, string net, double? width = null)
        {
            _board = board;
            Net = net ?? string.Empty;
            Class = board.ClassFor(Net);
            Width = width ?? Class.TrackWidth;
            _outline = board.OutlinePolygon(0.01);
        }

        public string Net { get; }
        public NetClass Class { get; }
        public double Width { get; set; }

        /// <summary>Resolution of the walkaround search (mm).</summary>
        public double Grid { get; set; } = 0.1;

        /// <summary>Upper bound of A* expansions per query (keeps the editor responsive).</summary>
        public int MaxExpansions { get; set; } = 120_000;

        private bool Foreign(string? net) => string.IsNullOrEmpty(net) || !string.Equals(net, Net, StringComparison.OrdinalIgnoreCase);

        private LayerObstacles For(CopperLayer layer)
        {
            if (_layers.TryGetValue(layer, out var lo)) return lo;
            lo = new LayerObstacles();
            void Add(Obstacle o)
            {
                double grow = o.Half + o.Clearance;
                if (o.Pad is { } p) grow = Math.Max(p.Width, p.Height) / 2 + o.Clearance;
                o.MinX = Math.Min(o.X1, o.X2) - grow;
                o.MaxX = Math.Max(o.X1, o.X2) + grow;
                o.MinY = Math.Min(o.Y1, o.Y2) - grow;
                o.MaxY = Math.Max(o.Y1, o.Y2) + grow;
                for (long i = (long)Math.Floor(o.MinX / Bucket); i <= (long)Math.Floor(o.MaxX / Bucket); i++)
                {
                    for (long j = (long)Math.Floor(o.MinY / Bucket); j <= (long)Math.Floor(o.MaxY / Bucket); j++)
                    {
                        long key = (i << 32) ^ (j & 0xffffffffL);
                        if (!lo.Buckets.TryGetValue(key, out var list)) lo.Buckets[key] = list = new List<Obstacle>();
                        list.Add(o);
                    }
                }
            }

            int layers = _board.LayerCount;
            foreach (var p in _board.Pads())
            {
                if (p.Pad.IsThroughHole && p.Pad.NonPlated)
                {
                    Add(new Obstacle { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Half = p.Pad.Drill / 2, Clearance = _board.Rules.HoleClearance });
                    continue;
                }

                if (!p.OnLayer(layer) || !Foreign(p.Net)) continue;
                Add(new Obstacle { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Pad = p, Clearance = _board.ClearanceBetween(Net, p.Net) });
            }

            foreach (var t in _board.Tracks)
            {
                if (t.Layer != layer || !Foreign(t.Net)) continue;
                Add(new Obstacle { X1 = t.X1, Y1 = t.Y1, X2 = t.X2, Y2 = t.Y2, Half = t.Width / 2, Clearance = _board.ClearanceBetween(Net, t.Net) });
            }

            foreach (var v in _board.Vias)
            {
                if (!v.Spans(layer, layers) || !Foreign(v.Net)) continue;
                Add(new Obstacle { X1 = v.X, Y1 = v.Y, X2 = v.X, Y2 = v.Y, Half = v.Diameter / 2, Clearance = _board.ClearanceBetween(Net, v.Net) });
            }

            foreach (var h in _board.MountingHoles)
            {
                if (h.Plated && !Foreign(h.Net)) continue;
                Add(new Obstacle
                {
                    X1 = h.X, Y1 = h.Y, X2 = h.X, Y2 = h.Y, Half = h.Plated ? h.PadDiameter / 2 : h.Drill / 2,
                    Clearance = h.Plated ? _board.ClearanceBetween(Net, h.Net) : _board.Rules.HoleClearance
                });
            }

            // Board edge.
            for (int i = 0; i < _outline.Count; i++)
            {
                var a = _outline[i];
                var b = _outline[(i + 1) % _outline.Count];
                Add(new Obstacle { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y, Half = 0, Clearance = _board.Rules.EdgeClearance });
            }

            _layers[layer] = lo;
            return lo;
        }

        private static double Distance(Obstacle o, double x1, double y1, double x2, double y2) =>
            o.Pad is { } p ? DesignRuleChecker.PadSegmentDistance(p, x1, y1, x2, y2) : Geometry.SegmentSegmentDistance(x1, y1, x2, y2, o.X1, o.Y1, o.X2, o.Y2) - o.Half;

        /// <summary>The first foreign item a segment of the given half-width would violate, or null when it is clean.</summary>
        private Obstacle? Hit(CopperLayer layer, double x1, double y1, double x2, double y2, double half)
        {
            var lo = For(layer);
            double minX = Math.Min(x1, x2) - half, maxX = Math.Max(x1, x2) + half, minY = Math.Min(y1, y2) - half, maxY = Math.Max(y1, y2) + half;
            HashSet<Obstacle>? seen = null;
            for (long i = (long)Math.Floor(minX / Bucket); i <= (long)Math.Floor(maxX / Bucket); i++)
            {
                for (long j = (long)Math.Floor(minY / Bucket); j <= (long)Math.Floor(maxY / Bucket); j++)
                {
                    if (!lo.Buckets.TryGetValue((i << 32) ^ (j & 0xffffffffL), out var list)) continue;
                    foreach (var o in list)
                    {
                        if (o.MaxX < minX || o.MinX > maxX || o.MaxY < minY || o.MinY > maxY) continue;
                        if (seen != null && !seen.Add(o)) continue;
                        if (Distance(o, x1, y1, x2, y2) - half < o.Clearance - 1e-4) return o;
                    }

                    seen ??= new HashSet<Obstacle>();
                }
            }

            // Inside the board?
            if (Polygon.Winding(_outline, x1, y1) == 0 || Polygon.Winding(_outline, x2, y2) == 0) return new Obstacle();
            return null;
        }

        /// <summary>True when a track segment on <paramref name="layer"/> keeps every clearance.</summary>
        public bool SegmentClear(CopperLayer layer, double x1, double y1, double x2, double y2) =>
            Hit(layer, x1, y1, x2, y2, Width / 2) == null;

        public bool PathClear(CopperLayer layer, IReadOnlyList<PcbPoint> pts)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                if (!SegmentClear(layer, pts[i - 1].X, pts[i - 1].Y, pts[i].X, pts[i].Y)) return false;
            }

            return true;
        }

        /// <summary>True when a via of the net's class fits at (x, y) on every layer it spans.</summary>
        public bool ViaClear(double x, double y, CopperLayer from, CopperLayer to)
        {
            foreach (var l in PcbLayers.Between(from, to, _board.LayerCount))
            {
                if (Hit(l, x, y, x, y, Class.ViaDiameter / 2) != null) return false;
            }

            // Drill-to-drill spacing on every layer (holes go through the board).
            foreach (var p in _board.Pads())
            {
                if (!p.Pad.IsThroughHole) continue;
                double d = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) - p.Pad.Drill / 2 - Class.ViaDrill / 2;
                if (d < _board.Rules.MinHoleToHole) return false;
            }

            foreach (var v in _board.Vias)
            {
                double d = Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y)) - v.Drill / 2 - Class.ViaDrill / 2;
                if (d < _board.Rules.MinHoleToHole && !string.Equals(v.Net, Net, StringComparison.OrdinalIgnoreCase)) return false;
            }

            return true;
        }

        /// <summary>The two-leg route from a to b: a 45° leg and a straight leg (or two orthogonal legs in 90° mode).</summary>
        public static List<PcbPoint> Elbow(PcbPoint a, PcbPoint b, bool diagonalFirst, bool only90 = false)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            var pts = new List<PcbPoint> { a };
            PcbPoint mid;
            if (only90)
            {
                mid = diagonalFirst ? new PcbPoint(b.X, a.Y) : new PcbPoint(a.X, b.Y);
            }
            else
            {
                double d = Math.Min(Math.Abs(dx), Math.Abs(dy));
                mid = diagonalFirst
                    ? new PcbPoint(a.X + Math.Sign(dx) * d, a.Y + Math.Sign(dy) * d)
                    : new PcbPoint(b.X - Math.Sign(dx) * d, b.Y - Math.Sign(dy) * d);
            }

            if (Math.Abs(mid.X - a.X) + Math.Abs(mid.Y - a.Y) > 1e-9 && Math.Abs(mid.X - b.X) + Math.Abs(mid.Y - b.Y) > 1e-9) pts.Add(mid);
            if (Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) > 1e-9) pts.Add(b);
            return pts;
        }

        /// <summary>
        /// Path from <paramref name="start"/> towards <paramref name="target"/> on one layer (both points included
        /// when reachable). Returns the posture elbow when it is clear; else a walkaround.
        /// </summary>
        public List<PcbPoint> Walkaround(CopperLayer layer, PcbPoint start, PcbPoint target, bool diagonalFirst, bool only90 = false)
        {
            var direct = Elbow(start, target, diagonalFirst, only90);
            if (PathClear(layer, direct)) return direct;
            var other = Elbow(start, target, !diagonalFirst, only90);
            if (PathClear(layer, other)) return other;

            // Search a window around both points; widen it (up to the whole board) while the target is not reached.
            bool targetBlocked = Hit(layer, target.X, target.Y, target.X, target.Y, Width / 2) != null;
            double margin = Math.Max(8, 0.6 * (Math.Abs(target.X - start.X) + Math.Abs(target.Y - start.Y)));
            List<PcbPoint> best = new List<PcbPoint> { start };
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var (path, reached, whole) = Search(layer, start, target, only90, margin);
                best = path;
                if (reached || targetBlocked || whole) break;
                margin *= 2.5;
            }

            return best;
        }

        private (List<PcbPoint> Path, bool Reached, bool WholeBoard) Search(CopperLayer layer, PcbPoint start, PcbPoint target, bool only90, double margin)
        {
            double minX = Math.Max(-1, Math.Min(start.X, target.X) - margin), maxX = Math.Min(_board.Width + 1, Math.Max(start.X, target.X) + margin);
            double minY = Math.Max(-1, Math.Min(start.Y, target.Y) - margin), maxY = Math.Min(_board.Height + 1, Math.Max(start.Y, target.Y) + margin);
            bool whole = minX <= -1 + 1e-9 && minY <= -1 + 1e-9 && maxX >= _board.Width + 1 - 1e-9 && maxY >= _board.Height + 1 - 1e-9;
            double g = Math.Max(Grid, Math.Max(maxX - minX, maxY - minY) / 600);
            double half = Width / 2;
            long si = (long)Math.Round(start.X / g), sj = (long)Math.Round(start.Y / g);
            long ti = (long)Math.Round(target.X / g), tj = (long)Math.Round(target.Y / g);
            long i0 = (long)Math.Floor(Math.Min(minX, start.X) / g), i1 = (long)Math.Ceiling(Math.Max(maxX, start.X) / g);
            long j0 = (long)Math.Floor(Math.Min(minY, start.Y) / g), j1 = (long)Math.Ceiling(Math.Max(maxY, start.Y) / g);
            int nx = (int)(i1 - i0 + 1), ny = (int)(j1 - j0 + 1);
            var state = new sbyte[nx * ny];   // 0 unknown, 1 free, -1 blocked
            bool Free(long i, long j)
            {
                if (i < i0 || i > i1 || j < j0 || j > j1) return false;
                int k = (int)((j - j0) * nx + (i - i0));
                if (state[k] == 0)
                {
                    double x = i * g, y = j * g;
                    state[k] = Hit(layer, x, y, x, y, half) == null ? (sbyte)1 : (sbyte)-1;
                }

                return state[k] > 0;
            }

            var result = new List<PcbPoint> { start };
            var startPt = new PcbPoint(si * g, sj * g);

            // The stub from the exact start onto the grid must itself be clean.
            if (!Free(si, sj) || !SegmentClear(layer, start.X, start.Y, startPt.X, startPt.Y)) return (result, false, whole);

            int[] dxs = only90 ? new[] { 1, -1, 0, 0 } : new[] { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dys = only90 ? new[] { 0, 0, 1, -1 } : new[] { 0, 0, 1, -1, 1, -1, 1, -1 };
            int n = nx * ny;
            var gScore = new double[n];
            var came = new int[n];
            var dirOf = new sbyte[n];
            for (int k = 0; k < n; k++)
            {
                gScore[k] = double.PositiveInfinity;
                came[k] = -1;
            }

            double H(long i, long j)
            {
                long dx = Math.Abs(i - ti), dy = Math.Abs(j - tj);
                return only90 ? dx + dy : Math.Max(dx, dy) + 0.41421356 * Math.Min(dx, dy);
            }

            int Index(long i, long j) => (int)((j - j0) * nx + (i - i0));
            var open = new AutoRouter.PriorityQueue();
            int s0 = Index(si, sj);
            gScore[s0] = 0;
            dirOf[s0] = -1;
            open.Push(s0, H(si, sj));
            int best = s0;
            double bestH = H(si, sj);
            int goal = -1, expanded = 0;
            while (open.Count > 0)
            {
                int cur = open.Pop(out double f);
                long ci = cur % nx + i0, cj = cur / nx + j0;
                double h = H(ci, cj);
                if (f - h > gScore[cur] + 1e-9) continue;
                if (h < bestH)
                {
                    bestH = h;
                    best = cur;
                }

                if (ci == ti && cj == tj)
                {
                    goal = cur;
                    break;
                }

                if (++expanded > MaxExpansions) break;
                for (int d = 0; d < dxs.Length; d++)
                {
                    long ni = ci + dxs[d], nj = cj + dys[d];
                    if (!Free(ni, nj)) continue;
                    if (d >= 4 && (!Free(ci + dxs[d], cj) || !Free(ci, cj + dys[d]))) continue;
                    double step = d >= 4 ? 1.41421356 : 1.0;
                    if (dirOf[cur] >= 0 && dirOf[cur] != d) step += 0.6;
                    int next = Index(ni, nj);
                    double ng = gScore[cur] + step;
                    if (ng < gScore[next] - 1e-12)
                    {
                        gScore[next] = ng;
                        came[next] = cur;
                        dirOf[next] = (sbyte)d;
                        open.Push(next, ng + H(ni, nj));
                    }
                }
            }

            int end = goal >= 0 ? goal : best;
            var cells = new List<int>();
            for (int s = end; s >= 0; s = came[s]) cells.Add(s);
            cells.Reverse();

            // Corners only.
            var pts = new List<PcbPoint>();
            for (int k = 0; k < cells.Count; k++)
            {
                var p = new PcbPoint((cells[k] % nx + i0) * g, (cells[k] / nx + j0) * g);
                if (k > 0 && k < cells.Count - 1)
                {
                    int a = cells[k - 1], b = cells[k], c = cells[k + 1];
                    long dx1 = b % nx - a % nx, dy1 = b / nx - a / nx, dx2 = c % nx - b % nx, dy2 = c / nx - b / nx;
                    if (dx1 == dx2 && dy1 == dy2) continue;
                }

                pts.Add(p);
            }

            if (pts.Count == 0 || Math.Abs(pts[0].X - start.X) + Math.Abs(pts[0].Y - start.Y) > 1e-9) result.AddRange(pts);
            else result.AddRange(pts.Skip(1));
            if (goal >= 0 && Math.Abs(target.X - result[result.Count - 1].X) + Math.Abs(target.Y - result[result.Count - 1].Y) > 1e-9 &&
                SegmentClear(layer, result[result.Count - 1].X, result[result.Count - 1].Y, target.X, target.Y))
            {
                result.Add(target);
            }

            bool reached = goal >= 0 && Math.Abs(target.X - result[result.Count - 1].X) + Math.Abs(target.Y - result[result.Count - 1].Y) < 1e-9;
            return (Smooth(layer, result, only90), reached, whole);
        }

        /// <summary>Removes corners whose neighbours can be joined directly at 0/45/90° (0/90° in 90° mode) without a violation.</summary>
        private List<PcbPoint> Smooth(CopperLayer layer, List<PcbPoint> pts, bool only90)
        {
            bool changed = true;
            while (changed && pts.Count > 2)
            {
                changed = false;
                for (int k = 1; k + 1 < pts.Count; k++)
                {
                    var a = pts[k - 1];
                    var c = pts[k + 1];
                    double dx = Math.Abs(c.X - a.X), dy = Math.Abs(c.Y - a.Y);
                    bool octilinear = dx < 1e-9 || dy < 1e-9 || !only90 && Math.Abs(dx - dy) < 1e-9;
                    if (octilinear && SegmentClear(layer, a.X, a.Y, c.X, c.Y))
                    {
                        pts.RemoveAt(k);
                        changed = true;
                        break;
                    }
                }
            }

            return pts;
        }
    }

    /// <summary>Editing helpers for existing copper: connected tracks and drag with rubber-banding.</summary>
    public static class TrackEditing
    {
        private const double Tol = 1e-4;

        private static bool At(double x1, double y1, double x2, double y2) => Math.Abs(x1 - x2) < Tol && Math.Abs(y1 - y2) < Tol;

        private static bool SameNet(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>Other tracks of the same net and layer with an end at (x, y).</summary>
        public static List<Track> EndsAt(BoardDocument board, Track except, double x, double y) =>
            board.Tracks.Where(t => !ReferenceEquals(t, except) && t.Layer == except.Layer && SameNet(t.Net, except.Net) &&
                                    (At(t.X1, t.Y1, x, y) || At(t.X2, t.Y2, x, y))).ToList();

        /// <summary>
        /// The whole track a segment belongs to: segments joined end to end and through vias,
        /// stopping at pads (KiCad's "select track").
        /// </summary>
        public static (List<Track> Tracks, List<Via> Vias) ConnectedTrack(BoardDocument board, Track start)
        {
            var tracks = new List<Track> { start };
            var vias = new List<Via>();
            var queue = new Queue<object>();
            queue.Enqueue(start);
            var pads = board.Pads().Where(p => SameNet(p.Net, start.Net)).ToList();
            bool OnPad(double x, double y, CopperLayer l) => pads.Any(p => p.OnLayer(l) && Math.Abs(p.X - x) <= p.Width / 2 && Math.Abs(p.Y - y) <= p.Height / 2);
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                if (item is Track t)
                {
                    foreach (var (x, y) in new[] { (t.X1, t.Y1), (t.X2, t.Y2) })
                    {
                        if (OnPad(x, y, t.Layer)) continue;
                        foreach (var u in EndsAt(board, t, x, y))
                        {
                            if (tracks.Contains(u)) continue;
                            tracks.Add(u);
                            queue.Enqueue(u);
                        }

                        foreach (var v in board.Vias)
                        {
                            if (vias.Contains(v) || !SameNet(v.Net, t.Net) || !v.Spans(t.Layer, board.LayerCount)) continue;
                            if (Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y)) <= v.Diameter / 2)
                            {
                                vias.Add(v);
                                queue.Enqueue(v);
                            }
                        }
                    }
                }
                else if (item is Via v)
                {
                    foreach (var u in board.Tracks)
                    {
                        if (tracks.Contains(u) || !SameNet(u.Net, v.Net) || !v.Spans(u.Layer, board.LayerCount)) continue;
                        if (Math.Sqrt((u.X1 - v.X) * (u.X1 - v.X) + (u.Y1 - v.Y) * (u.Y1 - v.Y)) <= v.Diameter / 2 ||
                            Math.Sqrt((u.X2 - v.X) * (u.X2 - v.X) + (u.Y2 - v.Y) * (u.Y2 - v.Y)) <= v.Diameter / 2)
                        {
                            tracks.Add(u);
                            queue.Enqueue(u);
                        }
                    }
                }
            }

            return (tracks, vias);
        }

        /// <summary>
        /// Moves a segment sideways by the component of (dx, dy) normal to it. Neighbouring
        /// segments keep their direction and are stretched to meet it (KiCad's 45° drag);
        /// where that is impossible (end on a pad or via, collinear or several neighbours) the
        /// end stays and a joining segment is inserted. Returns the tracks that were added.
        /// </summary>
        public static List<Track> DragSegment(BoardDocument board, Track t, double dx, double dy)
        {
            var added = new List<Track>();
            double len = t.Length;
            if (len < 1e-9) return added;
            double ux = (t.X2 - t.X1) / len, uy = (t.Y2 - t.Y1) / len, nx = -uy, ny = ux;
            double d = dx * nx + dy * ny;
            if (Math.Abs(d) < 1e-9) return added;
            double ox = nx * d, oy = ny * d;

            (double X, double Y) MoveEnd(double x, double y)
            {
                var neighbours = EndsAt(board, t, x, y);
                bool fixedEnd = board.Pads().Any(p => SameNet(p.Net, t.Net) && p.OnLayer(t.Layer) && Math.Abs(p.X - x) <= p.Width / 2 && Math.Abs(p.Y - y) <= p.Height / 2) ||
                                board.Vias.Any(v => SameNet(v.Net, t.Net) && Math.Abs(v.X - x) < Tol && Math.Abs(v.Y - y) < Tol);
                if (!fixedEnd && neighbours.Count == 1)
                {
                    var nb = neighbours[0];
                    bool startEnd = At(nb.X1, nb.Y1, x, y);
                    double fx = startEnd ? nb.X2 : nb.X1, fy = startEnd ? nb.Y2 : nb.Y1;   // far end stays
                    double vx = x - fx, vy = y - fy;
                    double cross = vx * uy - vy * ux;
                    if (Math.Abs(cross) > 1e-9)
                    {
                        // Intersect the neighbour's line with the moved segment's line.
                        double px = x + ox, py = y + oy;
                        double s = ((px - fx) * uy - (py - fy) * ux) / cross;
                        double ix = fx + vx * s, iy = fy + vy * s;
                        if (s > 1e-6)
                        {
                            if (startEnd)
                            {
                                nb.X1 = ix;
                                nb.Y1 = iy;
                            }
                            else
                            {
                                nb.X2 = ix;
                                nb.Y2 = iy;
                            }

                            return (ix, iy);
                        }
                    }
                }

                var join = new Track { Net = t.Net, Layer = t.Layer, Width = t.Width, X1 = x, Y1 = y, X2 = x + ox, Y2 = y + oy };
                board.Tracks.Add(join);
                added.Add(join);
                return (x + ox, y + oy);
            }

            var a = MoveEnd(t.X1, t.Y1);
            var b = MoveEnd(t.X2, t.Y2);
            t.X1 = a.X;
            t.Y1 = a.Y;
            t.X2 = b.X;
            t.Y2 = b.Y;
            return added;
        }

        /// <summary>Moves a via and drags the track ends that sit on it.</summary>
        public static void MoveVia(BoardDocument board, Via v, double x, double y)
        {
            foreach (var t in board.Tracks)
            {
                if (!SameNet(t.Net, v.Net) || !v.Spans(t.Layer, board.LayerCount)) continue;
                if (At(t.X1, t.Y1, v.X, v.Y))
                {
                    t.X1 = x;
                    t.Y1 = y;
                }

                if (At(t.X2, t.Y2, v.X, v.Y))
                {
                    t.X2 = x;
                    t.Y2 = y;
                }
            }

            v.X = x;
            v.Y = y;
        }
    }
}
