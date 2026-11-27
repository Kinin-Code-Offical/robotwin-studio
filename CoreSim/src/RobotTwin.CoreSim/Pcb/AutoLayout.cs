using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    public sealed class PlacementResult
    {
        public int Placed { get; internal set; }
        public bool BoardResized { get; internal set; }
        public List<string> Unplaced { get; } = new List<string>();
    }

    /// <summary>
    /// Greedy connectivity-driven placer: big parts first, connectors to the board
    /// edge, every part at the free spot (and rotation) closest to what it connects to.
    /// </summary>
    public static class AutoPlacer
    {
        private const double Grid = 1.27;
        private const double Spacing = 1.0;

        public static PlacementResult Place(BoardDocument board, bool allowResize = true, bool keepLocked = true)
        {
            var result = new PlacementResult();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                result.Unplaced.Clear();
                if (TryPlace(board, keepLocked, result)) return result;
                if (!allowResize) return result;
                board.Width = Math.Ceiling(board.Width * 1.2);
                board.Height = Math.Ceiling(board.Height * 1.2);
                result.BoardResized = true;
            }

            return result;
        }

        private static bool IsConnector(BoardComponent c) =>
            c.FootprintId.StartsWith("HDR", StringComparison.OrdinalIgnoreCase) ||
            c.FootprintId.StartsWith("TERM", StringComparison.OrdinalIgnoreCase) ||
            c.FootprintId.StartsWith("WIRE", StringComparison.OrdinalIgnoreCase);

        private static bool TryPlace(BoardDocument board, bool keepLocked, PlacementResult result)
        {
            var placed = new List<(BoardComponent Comp, (double MinX, double MinY, double MaxX, double MaxY) Box)>();
            var todo = new List<BoardComponent>();

            // Mounting holes are keep-outs; a polygon outline restricts where courtyards may go.
            foreach (var h in board.MountingHoles)
            {
                double r = Math.Max(h.Drill, h.PadDiameter) / 2 + board.Rules.HoleClearance;
                placed.Add((new BoardComponent { RefDes = "#H" }, (h.X - r, h.Y - r, h.X + r, h.Y + r)));
            }

            List<PolyRegion>? inside = null;
            if (board.Outline != null && board.Outline.Count >= 3)
            {
                inside = PolyOffset.Offset(PolyBool.Union(new[] { board.OutlinePolygon(0.02) }), -(board.Rules.EdgeClearance + 0.5), 0.02);
            }

            bool InOutline((double MinX, double MinY, double MaxX, double MaxY) b) =>
                inside == null ||
                Polygon.AnyContains(inside, b.MinX, b.MinY) && Polygon.AnyContains(inside, b.MaxX, b.MinY) &&
                Polygon.AnyContains(inside, b.MaxX, b.MaxY) && Polygon.AnyContains(inside, b.MinX, b.MaxY) &&
                Polygon.AnyContains(inside, (b.MinX + b.MaxX) / 2, (b.MinY + b.MaxY) / 2);
            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                if (keepLocked && comp.Locked) placed.Add((comp, comp.Courtyard(fp)));
                else todo.Add(comp);
            }

            // Large parts and connectors first, then by connection count.
            todo = todo
                .OrderByDescending(c => Area(c))
                .ThenByDescending(c => c.PadNets.Count)
                .ThenBy(c => c.RefDes, StringComparer.Ordinal)
                .ToList();

            double edge = board.Rules.EdgeClearance + 0.5;
            result.Placed = 0;
            foreach (var comp in todo)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId)!;
                bool connector = IsConnector(comp);
                double bestScore = double.PositiveInfinity;
                double bestX = 0, bestY = 0;
                int bestRot = 0;

                // Anchor points: pads of already-placed parts on the same nets.
                var anchors = new List<(double X, double Y, string Net)>();
                foreach (var (other, _) in placed)
                {
                    var ofp = FootprintLibrary.Get(other.FootprintId);
                    if (ofp == null) continue;
                    foreach (var pad in ofp.Pads)
                    {
                        if (other.PadNets.TryGetValue(pad.Name, out var net) && comp.PadNets.Values.Contains(net, StringComparer.OrdinalIgnoreCase))
                        {
                            var p = other.ToBoard(pad.X, pad.Y);
                            anchors.Add((p.X, p.Y, net));
                        }
                    }
                }

                foreach (int rot in new[] { 0, 90, 180, 270 })
                {
                    comp.Rotation = rot;
                    comp.X = 0;
                    comp.Y = 0;
                    var box0 = comp.Courtyard(fp);
                    double w = box0.MaxX - box0.MinX, h = box0.MaxY - box0.MinY;
                    for (double y = edge; y + h <= board.Height - edge + 1e-9; y += Grid)
                    {
                        for (double x = edge; x + w <= board.Width - edge + 1e-9; x += Grid)
                        {
                            // Translate so that the courtyard's corner lands on (x, y).
                            double ox = x - box0.MinX, oy = y - box0.MinY;
                            var box = (x, y, x + w, y + h);
                            if (Overlaps(box, placed) || !InOutline(box)) continue;

                            double score = 0;
                            foreach (var pad in fp.Pads)
                            {
                                if (!comp.PadNets.TryGetValue(pad.Name, out var net)) continue;
                                comp.X = ox;
                                comp.Y = oy;
                                var p = comp.ToBoard(pad.X, pad.Y);
                                double nearest = double.PositiveInfinity;
                                foreach (var a in anchors)
                                {
                                    if (!string.Equals(a.Net, net, StringComparison.OrdinalIgnoreCase)) continue;
                                    double d = Math.Abs(a.X - p.X) + Math.Abs(a.Y - p.Y);
                                    if (d < nearest) nearest = d;
                                }

                                if (!double.IsInfinity(nearest)) score += nearest;
                            }

                            double cx = x + w / 2, cy = y + h / 2;
                            if (connector)
                            {
                                // Connectors hug an edge.
                                double toEdge = Math.Min(Math.Min(x, y), Math.Min(board.Width - (x + w), board.Height - (y + h)));
                                score += toEdge * 4.0;
                            }
                            else
                            {
                                score += 0.15 * (Math.Abs(cx - board.Width / 2) + Math.Abs(cy - board.Height / 2));
                            }

                            if (rot != 0) score += 0.5;
                            if (score < bestScore - 1e-9)
                            {
                                bestScore = score;
                                bestX = ox;
                                bestY = oy;
                                bestRot = rot;
                            }
                        }
                    }
                }

                if (double.IsPositiveInfinity(bestScore))
                {
                    result.Unplaced.Add(comp.RefDes);
                    return false;
                }

                comp.Rotation = bestRot;
                comp.X = Math.Round(bestX, 4);
                comp.Y = Math.Round(bestY, 4);
                placed.Add((comp, comp.Courtyard(fp)));
                result.Placed++;
            }

            return true;
        }

        private static double Area(BoardComponent c)
        {
            var fp = FootprintLibrary.Get(c.FootprintId);
            if (fp == null) return 0;
            return (fp.CourtyardMaxX - fp.CourtyardMinX) * (fp.CourtyardMaxY - fp.CourtyardMinY);
        }

        private static bool Overlaps((double MinX, double MinY, double MaxX, double MaxY) box,
            List<(BoardComponent Comp, (double MinX, double MinY, double MaxX, double MaxY) Box)> placed)
        {
            foreach (var (_, b) in placed)
            {
                if (box.MinX < b.MaxX + Spacing && box.MaxX + Spacing > b.MinX &&
                    box.MinY < b.MaxY + Spacing && box.MaxY + Spacing > b.MinY)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class RoutingResult
    {
        public int Connections { get; internal set; }
        public int Routed { get; internal set; }
        public int ViasAdded { get; internal set; }
        public double TotalLength { get; internal set; }

        /// <summary>SMD pads of plane nets joined to their inner-layer plane with a via.</summary>
        public int PlaneConnections { get; internal set; }

        public List<string> Failed { get; } = new List<string>();
        public bool Complete => Failed.Count == 0;
    }

    /// <summary>
    /// Multi-layer grid maze router (A* with 45-degree moves and through vias). Pads,
    /// existing copper, mounting holes and the board outline are rasterised as keep-outs
    /// inflated by their net class clearance plus half the narrowest track; nets with
    /// wider tracks or larger clearances check extra cells, so routed tracks are DRC-clean
    /// by construction. Inner layers that carry a whole-board zone are treated as planes:
    /// they are not routed on, through-hole pads of the plane's net reach it directly and
    /// SMD pads get a via down to it. Failed connections are moved to the front and
    /// everything is rerouted (rip-up and retry).
    /// </summary>
    public sealed class AutoRouter
    {
        private const int Free = -1;
        private const int Blocked = -2;

        private readonly BoardDocument _board;
        private readonly double _grid;
        private readonly int _nx, _ny;
        private readonly List<CopperLayer> _layers;
        private readonly HashSet<string> _planeNets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly double _baseHalf, _minClearance;
        private int[][] _owner = Array.Empty<int[]>();
        private int[][] _core = Array.Empty<int[]>();
        private readonly Dictionary<string, int> _netIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _netNames = new List<string>();

        public AutoRouter(BoardDocument board)
        {
            _board = board;
            _grid = Math.Max(0.1, board.Rules.RoutingGrid);
            _nx = (int)Math.Ceiling(board.Width / _grid) + 1;
            _ny = (int)Math.Ceiling(board.Height / _grid) + 1;
            _baseHalf = board.NetClasses.Min(c => c.TrackWidth) / 2;
            _minClearance = board.NetClasses.Min(c => c.Clearance);

            // Inner layers carrying a whole-board zone are planes.
            _layers = new List<CopperLayer>();
            foreach (var l in board.CopperLayers())
            {
                var plane = PcbLayers.IsOuter(l) ? null : board.Zones.FirstOrDefault(z => z.Layer == l && z.IsWholeBoard && !string.IsNullOrEmpty(z.Net));
                if (plane != null) _planeNets.Add(plane.Net);
                else _layers.Add(l);
            }

            if (_layers.Count == 0) _layers.Add(CopperLayer.Top);
        }

        public double ViaCost { get; set; } = 12.0;
        public double BendCost { get; set; } = 0.4;
        public int Passes { get; set; } = 4;

        /// <summary>Layers the router puts tracks on (planes excluded).</summary>
        public IReadOnlyList<CopperLayer> RoutingLayers => _layers;

        public IReadOnlyCollection<string> PlaneNets => _planeNets;

        private int L => _layers.Count;

        private int LayerIndex(CopperLayer l) => _layers.IndexOf(l);

        /// <summary>
        /// [problems] Only <paramref name="nets"/> drawn again (the DRC fix "Hattı yeniden çiz"): their tracks and vias are
        /// taken up, every other net's copper stays where it is as an obstacle, and their connections are routed among it.
        /// </summary>
        public RoutingResult RouteNets(IEnumerable<string> nets)
        {
            var only = new HashSet<string>(nets.Where(n => !string.IsNullOrEmpty(n)), StringComparer.OrdinalIgnoreCase);
            _board.Tracks.RemoveAll(t => only.Contains(t.Net));
            _board.Vias.RemoveAll(v => only.Contains(v.Net));
            return Route(false, only);
        }

        public RoutingResult Route(bool clearExisting = true) => Route(clearExisting, null);

        private RoutingResult Route(bool clearExisting, HashSet<string>? only)
        {
            if (clearExisting)
            {
                _board.Tracks.Clear();
                _board.Vias.Clear();
            }

            // Pad-to-pad connections; plane nets: only SMD pads, each to the plane.
            var connections = _board.PadConnections().Where(c => !_planeNets.Contains(c.Net)).Select(c => (c.Net, c.A, (PlacedPad?)c.B)).ToList();
            foreach (var pad in _board.Pads())
            {
                if (_planeNets.Contains(pad.Net) && !pad.Pad.IsThroughHole) connections.Add((pad.Net, pad, null));
            }

            if (only != null) connections = connections.Where(c => only.Contains(c.Net)).ToList();

            var order = connections
                .OrderByDescending(c => _board.ClassFor(c.Net).TrackWidth)
                .ThenBy(c => c.Item3 == null ? 0 : Math.Abs(c.A.X - c.Item3.Value.X) + Math.Abs(c.A.Y - c.Item3.Value.Y))
                .ToList();

            RoutingResult result = new RoutingResult();
            var baseTracks = _board.Tracks.ToList();
            var baseVias = _board.Vias.ToList();
            for (int pass = 0; pass < Math.Max(1, Passes); pass++)
            {
                _board.Tracks.Clear();
                _board.Tracks.AddRange(baseTracks);
                _board.Vias.Clear();
                _board.Vias.AddRange(baseVias);
                BuildObstacles();

                result = new RoutingResult { Connections = order.Count };
                var failed = new List<(string Net, PlacedPad A, PlacedPad? B)>();
                foreach (var conn in order)
                {
                    if (RouteConnection(conn.Net, conn.A, conn.Item3, result))
                    {
                        result.Routed++;
                        if (conn.Item3 == null) result.PlaneConnections++;
                    }
                    else
                    {
                        failed.Add(conn);
                    }
                }

                foreach (var f in failed)
                {
                    result.Failed.Add(f.B == null
                        ? $"{f.Net}: {f.A.Component.RefDes}.{f.A.Pad.Name} → düzlem"
                        : $"{f.Net}: {f.A.Component.RefDes}.{f.A.Pad.Name} → {f.B.Value.Component.RefDes}.{f.B.Value.Pad.Name}");
                }

                if (failed.Count == 0) break;
                order = failed.Concat(order.Except(failed)).ToList();
            }

            result.TotalLength = _board.Tracks.Sum(t => t.Length);
            result.ViasAdded = _board.Vias.Count;
            return result;
        }

        private int NetId(string net)
        {
            if (!_netIndex.TryGetValue(net, out int id))
            {
                id = _netNames.Count;
                _netNames.Add(net);
                _netIndex[net] = id;
            }

            return id;
        }

        private double ClearanceOf(string? net) => _board.ClassFor(net).Clearance;

        private void BuildObstacles()
        {
            _owner = new int[L][];
            _core = new int[L][];
            for (int l = 0; l < L; l++)
            {
                _owner[l] = new int[_nx * _ny];
                _core[l] = new int[_nx * _ny];
                Array.Fill(_owner[l], Free);
                Array.Fill(_core[l], Free);
            }

            var rules = _board.Rules;

            // Board outline (inset by the edge clearance and half the narrowest track).
            var inset = PolyOffset.Offset(PolyBool.Union(new[] { _board.OutlinePolygon(0.01) }), -(rules.EdgeClearance + _baseHalf), 0.01);
            var edges = inset.SelectMany(r => r.Contours()).ToList();
            var xs = new List<double>();
            for (int j = 0; j < _ny; j++)
            {
                double y = j * _grid;
                xs.Clear();
                foreach (var c in edges)
                {
                    for (int k = 0, n = c.Count; k < n; k++)
                    {
                        var a = c[k];
                        var b = c[(k + 1) % n];
                        if ((a.Y > y) == (b.Y > y)) continue;
                        xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                    }
                }

                xs.Sort();
                int p = 0;
                for (int i = 0; i < _nx; i++)
                {
                    double x = i * _grid;
                    while (p < xs.Count && xs[p] <= x) p++;
                    if ((p & 1) == 0)
                    {
                        for (int l = 0; l < L; l++) _owner[l][j * _nx + i] = Blocked;
                    }
                }
            }

            foreach (var pad in _board.Pads())
            {
                int net = string.IsNullOrEmpty(pad.Net) ? Blocked : NetId(pad.Net);
                double halo = ClearanceOf(pad.Net) + _baseHalf;
                if (pad.Pad.IsThroughHole && pad.Pad.NonPlated)
                {
                    for (int l = 0; l < L; l++) MarkRect(l, pad.X, pad.Y, pad.Pad.Drill / 2, pad.Pad.Drill / 2, rules.HoleClearance + _baseHalf, Blocked, false);
                    continue;
                }

                for (int l = 0; l < L; l++)
                {
                    if (!pad.OnLayer(_layers[l])) continue;
                    MarkRect(l, pad.X, pad.Y, pad.Width / 2, pad.Height / 2, halo, net);
                }

                if (pad.Pad.IsThroughHole)
                {
                    // Drilled holes also keep vias away (vias must pass on every layer).
                    MarkRect(0, pad.X, pad.Y, pad.Pad.Drill / 2, pad.Pad.Drill / 2, ClearanceOf(pad.Net), net, coreToo: false);
                }
            }

            foreach (var h in _board.MountingHoles)
            {
                double r = h.Plated ? h.PadDiameter / 2 : h.Drill / 2;
                double c = h.Plated ? ClearanceOf(h.Net) : rules.HoleClearance;
                int net = h.Plated && !string.IsNullOrEmpty(h.Net) ? NetId(h.Net) : Blocked;
                for (int l = 0; l < L; l++) MarkSegment(l, h.X, h.Y, h.X, h.Y, r, net, c);
            }

            // [keepout] rule areas: their sides block every layer they hold on
            foreach (var a in _board.RuleAreas.Where(a => a.NoTracks && a.Outline.Count >= 3))
            {
                for (int l = 0; l < L; l++)
                {
                    if (!a.OnLayer(_layers[l])) continue;
                    foreach (var (p, q) in a.Edges()) MarkSegment(l, p.X, p.Y, q.X, q.Y, 0, Blocked, 0.05);
                }
            }

            foreach (var t in _board.Tracks) MarkTrack(t);
            foreach (var v in _board.Vias) MarkVia(v);
        }

        private void MarkRect(int layer, double cx, double cy, double hx, double hy, double halo, int net, bool coreToo = true)
        {
            int i0 = Math.Max(0, (int)Math.Floor((cx - hx - halo) / _grid));
            int i1 = Math.Min(_nx - 1, (int)Math.Ceiling((cx + hx + halo) / _grid));
            int j0 = Math.Max(0, (int)Math.Floor((cy - hy - halo) / _grid));
            int j1 = Math.Min(_ny - 1, (int)Math.Ceiling((cy + hy + halo) / _grid));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    double x = i * _grid, y = j * _grid;
                    double dx = Math.Max(0, Math.Abs(x - cx) - hx), dy = Math.Max(0, Math.Abs(y - cy) - hy);
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    int k = j * _nx + i;
                    if (d <= 1e-9 && coreToo && net >= 0) _core[layer][k] = net;
                    if (d < halo + Sagitta(halo)) Claim(layer, k, net);
                }
            }
        }

        /// <summary>
        /// Keep-outs are tested at cell centres only; a straight or diagonal step between two
        /// free cells can pass closer to a corner by up to this much, so it is added to the halo.
        /// </summary>
        private double Sagitta(double halo) => halo <= 0 ? 0 : halo - Math.Sqrt(Math.Max(0, halo * halo - _grid * _grid / 2));

        private void Claim(int layer, int k, int net)
        {
            int cur = _owner[layer][k];
            if (cur == Free) _owner[layer][k] = net;
            else if (cur != net) _owner[layer][k] = Blocked;
        }

        private void MarkSegment(int layer, double x1, double y1, double x2, double y2, double half, int net, double clearance)
        {
            double halo = half + clearance + _baseHalf;
            int i0 = Math.Max(0, (int)Math.Floor((Math.Min(x1, x2) - halo) / _grid));
            int i1 = Math.Min(_nx - 1, (int)Math.Ceiling((Math.Max(x1, x2) + halo) / _grid));
            int j0 = Math.Max(0, (int)Math.Floor((Math.Min(y1, y2) - halo) / _grid));
            int j1 = Math.Min(_ny - 1, (int)Math.Ceiling((Math.Max(y1, y2) + halo) / _grid));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    double d = Geometry.PointSegmentDistance(i * _grid, j * _grid, x1, y1, x2, y2);
                    int k = j * _nx + i;
                    if (d <= half && net >= 0) _core[layer][k] = net;
                    if (d < halo + Sagitta(halo)) Claim(layer, k, net);
                }
            }
        }

        private void MarkTrack(Track t)
        {
            int l = LayerIndex(t.Layer);
            if (l < 0) return;
            MarkSegment(l, t.X1, t.Y1, t.X2, t.Y2, t.Width / 2, NetId(t.Net), ClearanceOf(t.Net));
        }

        private void MarkVia(Via v)
        {
            int net = NetId(v.Net);
            for (int l = 0; l < L; l++)
            {
                if (v.Spans(_layers[l], _board.LayerCount)) MarkSegment(l, v.X, v.Y, v.X, v.Y, v.Diameter / 2, net, ClearanceOf(v.Net));
            }
        }

        private bool Passable(int layer, int i, int j, int net, int extra)
        {
            if (i < 0 || j < 0 || i >= _nx || j >= _ny) return false;
            int k = j * _nx + i;
            if (_core[layer][k] != net)
            {
                int o = _owner[layer][k];
                if (o != Free && o != net) return false;
            }

            // Wide tracks and vias need their whole footprint clear, even when centred in their own pad.
            if (extra <= 0) return true;
            for (int dj = -extra; dj <= extra; dj++)
            {
                for (int di = -extra; di <= extra; di++)
                {
                    int ii = i + di, jj = j + dj;
                    if (ii < 0 || jj < 0 || ii >= _nx || jj >= _ny) return false;
                    int kk = jj * _nx + ii;
                    if (_core[layer][kk] == net) continue;
                    int oo = _owner[layer][kk];
                    if (oo != Free && oo != net) return false;
                }
            }

            return true;
        }

        private bool ViaPassable(int i, int j, int net, int extra)
        {
            for (int l = 0; l < L; l++)
            {
                if (!Passable(l, i, j, net, extra)) return false;
            }

            return true;
        }

        private static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] Dy = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Routes pad a to pad b, or (b null) pad a to a via onto its net's plane.</summary>
        private bool RouteConnection(string netName, PlacedPad a, PlacedPad? b, RoutingResult result)
        {
            int net = NetId(netName);
            var cls = _board.ClassFor(netName);
            double width = cls.TrackWidth;
            double extraClear = Math.Max(0, cls.Clearance - _minClearance);
            int extra = (int)Math.Ceiling(Math.Max(0, width / 2 - _baseHalf + extraClear) / _grid - 1e-9);
            double viaHalf = cls.ViaDiameter / 2 - _baseHalf + extraClear;
            int viaExtra = (int)Math.Ceiling(Math.Max(0, viaHalf) / _grid - 1e-9);
            bool toPlane = b == null;

            int si = (int)Math.Round(a.X / _grid), sj = (int)Math.Round(a.Y / _grid);
            int ti = toPlane ? si : (int)Math.Round(b!.Value.X / _grid), tj = toPlane ? sj : (int)Math.Round(b!.Value.Y / _grid);

            int n = _nx * _ny;
            var gScore = new double[L * n];
            var came = new int[L * n];
            var dirOf = new sbyte[L * n];
            Array.Fill(gScore, double.PositiveInfinity);
            Array.Fill(came, -1);
            var open = new PriorityQueue();

            bool targetOn(int layer) => !toPlane && b!.Value.OnLayer(_layers[layer]);
            double H(int i, int j) => toPlane ? 0 : Heuristic(i, j, ti, tj);
            for (int l = 0; l < L; l++)
            {
                if (!a.OnLayer(_layers[l])) continue;
                int s = l * n + sj * _nx + si;
                gScore[s] = 0;
                dirOf[s] = -1;
                open.Push(s, H(si, sj));
            }

            int goal = -1;
            int expanded = 0;
            int maxExpand = toPlane ? 200_000 : 4_000_000;
            while (open.Count > 0)
            {
                int cur = open.Pop(out double f);
                int layer = cur / n, cell = cur % n, ci = cell % _nx, cj = cell / _nx;
                if (f - H(ci, cj) > gScore[cur] + 1e-9) continue;
                if (!toPlane && ci == ti && cj == tj && targetOn(layer))
                {
                    goal = cur;
                    break;
                }

                // Plane connection: a via can drop here (not inside the pad itself).
                if (toPlane && (Math.Abs(ci - si) + Math.Abs(cj - sj)) * _grid > Math.Max(a.Width, a.Height) / 2 + cls.ViaDiameter / 2 + cls.Clearance &&
                    ViaPassable(ci, cj, net, viaExtra))
                {
                    goal = cur;
                    break;
                }

                if (++expanded > maxExpand) break;
                double g = gScore[cur];
                int prevDir = dirOf[cur];

                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + Dx[d], nj = cj + Dy[d];
                    if (!Passable(layer, ni, nj, net, extra)) continue;
                    if (d >= 4 && (!Passable(layer, ci + Dx[d], cj, net, extra) || !Passable(layer, ci, cj + Dy[d], net, extra))) continue;
                    double step = d >= 4 ? 1.41421356 : 1.0;
                    if (prevDir >= 0 && prevDir != d) step += BendCost;
                    int next = layer * n + nj * _nx + ni;
                    double ng = g + step;
                    if (ng < gScore[next] - 1e-12)
                    {
                        gScore[next] = ng;
                        came[next] = cur;
                        dirOf[next] = (sbyte)d;
                        open.Push(next, ng + H(ni, nj));
                    }
                }

                // Through via to any other routing layer.
                if (L > 1 && ViaPassable(ci, cj, net, viaExtra))
                {
                    for (int other = 0; other < L; other++)
                    {
                        if (other == layer) continue;
                        int next = other * n + cell;
                        double ng = g + ViaCost;
                        if (ng < gScore[next] - 1e-12)
                        {
                            gScore[next] = ng;
                            came[next] = cur;
                            dirOf[next] = -1;
                            open.Push(next, ng + H(ci, cj));
                        }
                    }
                }
            }

            if (goal < 0) return false;

            // Walk back and emit straight segments.
            var path = new List<int>();
            for (int s = goal; s >= 0; s = came[s]) path.Add(s);
            path.Reverse();

            int startIdx = 0;
            for (int idx = 1; idx <= path.Count; idx++)
            {
                bool end = idx == path.Count;
                bool layerChange = !end && path[idx] / n != path[idx - 1] / n;
                bool bend = !end && !layerChange && idx >= 2 && path[idx - 1] / n == path[idx - 2] / n &&
                            Direction(path[idx - 2], path[idx - 1], n) != Direction(path[idx - 1], path[idx], n);
                if (end || layerChange || bend)
                {
                    int p0 = path[startIdx], p1 = path[idx - 1];
                    if (p0 % n != p1 % n)
                    {
                        var t = new Track
                        {
                            Net = netName,
                            Layer = _layers[p0 / n],
                            X1 = (p0 % n % _nx) * _grid,
                            Y1 = (p0 % n / _nx) * _grid,
                            X2 = (p1 % n % _nx) * _grid,
                            Y2 = (p1 % n / _nx) * _grid,
                            Width = width
                        };
                        _board.Tracks.Add(t);
                        MarkTrack(t);
                    }

                    if (layerChange) AddVia(netName, p1, n, cls);
                    startIdx = idx - 1;
                    if (layerChange) startIdx = idx;
                }
            }

            // Short stubs from the grid points to the true pad centres (pads are not always on the grid).
            AddStub(netName, path[0], n, a, width);
            if (toPlane) AddVia(netName, path[path.Count - 1], n, cls);
            else AddStub(netName, path[path.Count - 1], n, b!.Value, width);
            return true;
        }

        private void AddVia(string net, int state, int n, NetClass cls)
        {
            var v = new Via
            {
                Net = net,
                X = (state % n % _nx) * _grid,
                Y = (state % n / _nx) * _grid,
                Diameter = cls.ViaDiameter,
                Drill = cls.ViaDrill
            };
            _board.Vias.Add(v);
            MarkVia(v);
        }

        private void AddStub(string net, int state, int n, PlacedPad pad, double width)
        {
            double gx = (state % n % _nx) * _grid, gy = (state % n / _nx) * _grid;
            if (Math.Abs(gx - pad.X) < 1e-6 && Math.Abs(gy - pad.Y) < 1e-6) return;
            var t = new Track { Net = net, Layer = _layers[state / n], X1 = pad.X, Y1 = pad.Y, X2 = gx, Y2 = gy, Width = width };
            _board.Tracks.Add(t);
            MarkTrack(t);
        }

        private int Direction(int from, int to, int n)
        {
            int a = from % n, b = to % n;
            int dx = b % _nx - a % _nx, dy = b / _nx - a / _nx;
            return (Math.Sign(dx) + 1) * 3 + Math.Sign(dy) + 1;
        }

        private static double Heuristic(int i, int j, int ti, int tj)
        {
            int dx = Math.Abs(i - ti), dy = Math.Abs(j - tj);
            return Math.Max(dx, dy) + 0.41421356 * Math.Min(dx, dy);
        }

        /// <summary>Binary min-heap of (state, priority); deterministic tie-breaking by insertion order.</summary>
        internal sealed class PriorityQueue
        {
            private readonly List<(double P, long Seq, int S)> _heap = new List<(double, long, int)>();
            private long _seq;

            public int Count => _heap.Count;

            public void Push(int state, double priority)
            {
                _heap.Add((priority, _seq++, state));
                int i = _heap.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (Less(_heap[i], _heap[p]))
                    {
                        (_heap[i], _heap[p]) = (_heap[p], _heap[i]);
                        i = p;
                    }
                    else break;
                }
            }

            public int Pop(out double priority)
            {
                var top = _heap[0];
                var last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);
                if (_heap.Count > 0)
                {
                    _heap[0] = last;
                    int i = 0;
                    while (true)
                    {
                        int l = 2 * i + 1, r = l + 1, m = i;
                        if (l < _heap.Count && Less(_heap[l], _heap[m])) m = l;
                        if (r < _heap.Count && Less(_heap[r], _heap[m])) m = r;
                        if (m == i) break;
                        (_heap[i], _heap[m]) = (_heap[m], _heap[i]);
                        i = m;
                    }
                }

                priority = top.P;
                return top.S;
            }

            private static bool Less((double P, long Seq, int S) a, (double P, long Seq, int S) b) =>
                a.P < b.P || (a.P == b.P && a.Seq < b.Seq);
        }
    }

    public static class Geometry
    {
        public static double PointSegmentDistance(double px, double py, double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            double len2 = dx * dx + dy * dy;
            double t = len2 > 0 ? ((px - x1) * dx + (py - y1) * dy) / len2 : 0;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            double cx = x1 + t * dx - px, cy = y1 + t * dy - py;
            return Math.Sqrt(cx * cx + cy * cy);
        }

        public static double SegmentSegmentDistance(double ax1, double ay1, double ax2, double ay2,
            double bx1, double by1, double bx2, double by2)
        {
            if (SegmentsIntersect(ax1, ay1, ax2, ay2, bx1, by1, bx2, by2)) return 0;
            return Math.Min(
                Math.Min(PointSegmentDistance(ax1, ay1, bx1, by1, bx2, by2), PointSegmentDistance(ax2, ay2, bx1, by1, bx2, by2)),
                Math.Min(PointSegmentDistance(bx1, by1, ax1, ay1, ax2, ay2), PointSegmentDistance(bx2, by2, ax1, ay1, ax2, ay2)));
        }

        private static bool SegmentsIntersect(double ax1, double ay1, double ax2, double ay2,
            double bx1, double by1, double bx2, double by2)
        {
            double d1 = Cross(bx2 - bx1, by2 - by1, ax1 - bx1, ay1 - by1);
            double d2 = Cross(bx2 - bx1, by2 - by1, ax2 - bx1, ay2 - by1);
            double d3 = Cross(ax2 - ax1, ay2 - ay1, bx1 - ax1, by1 - ay1);
            double d4 = Cross(ax2 - ax1, ay2 - ay1, bx2 - ax1, by2 - ay1);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;

        /// <summary>Distance from a segment to an axis-aligned rectangle (0 when they touch).</summary>
        public static double SegmentRectDistance(double x1, double y1, double x2, double y2,
            double cx, double cy, double hx, double hy)
        {
            double minX = cx - hx, maxX = cx + hx, minY = cy - hy, maxY = cy + hy;
            if (InRect(x1, y1) || InRect(x2, y2)) return 0;
            double best = Math.Min(
                Math.Min(SegmentSegmentDistance(x1, y1, x2, y2, minX, minY, maxX, minY), SegmentSegmentDistance(x1, y1, x2, y2, maxX, minY, maxX, maxY)),
                Math.Min(SegmentSegmentDistance(x1, y1, x2, y2, maxX, maxY, minX, maxY), SegmentSegmentDistance(x1, y1, x2, y2, minX, maxY, minX, minY)));
            return best;

            bool InRect(double x, double y) => x >= minX && x <= maxX && y >= minY && y <= maxY;
        }

        public static double RectRectDistance(double ax, double ay, double ahx, double ahy, double bx, double by, double bhx, double bhy)
        {
            double dx = Math.Max(0, Math.Abs(ax - bx) - ahx - bhx);
            double dy = Math.Max(0, Math.Abs(ay - by) - ahy - bhy);
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
