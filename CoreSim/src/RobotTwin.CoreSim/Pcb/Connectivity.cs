using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// Copper connectivity per net: pads, tracks, vias (on the layers they span),
    /// plated mounting holes and filled zone islands are joined when their copper
    /// touches on a shared layer. Gives the ratsnest of what is still unconnected and
    /// the unrouted-net list of the DRC.
    /// </summary>
    public sealed class BoardConnectivity
    {
        private readonly BoardDocument _board;
        private readonly Dictionary<string, NetGraph> _nets = new Dictionary<string, NetGraph>(StringComparer.OrdinalIgnoreCase);

        private sealed class NetGraph
        {
            public readonly List<PlacedPad> Pads = new List<PlacedPad>();
            public readonly List<Track> Tracks = new List<Track>();
            public readonly List<Via> Vias = new List<Via>();
            public readonly List<MountingHole> Holes = new List<MountingHole>();
            public readonly List<(Zone Zone, PolyRegion Region)> Islands = new List<(Zone, PolyRegion)>();
            public int[] Parent = Array.Empty<int>();

            public int PadBase => 0;
            public int TrackBase => Pads.Count;
            public int ViaBase => TrackBase + Tracks.Count;
            public int HoleBase => ViaBase + Vias.Count;
            public int IslandBase => HoleBase + Holes.Count;
            public int Count => IslandBase + Islands.Count;

            public int Find(int x)
            {
                while (Parent[x] != x) x = Parent[x] = Parent[Parent[x]];
                return x;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) Parent[ra] = rb;
            }
        }

        private BoardConnectivity(BoardDocument board)
        {
            _board = board;
        }

        public static BoardConnectivity Build(BoardDocument board, bool useZones = true)
        {
            var c = new BoardConnectivity(board);
            NetGraph G(string net)
            {
                if (!c._nets.TryGetValue(net, out var g)) c._nets[net] = g = new NetGraph();
                return g;
            }

            foreach (var p in board.Pads())
            {
                if (!string.IsNullOrEmpty(p.Net)) G(p.Net).Pads.Add(p);
            }

            foreach (var t in board.Tracks)
            {
                if (!string.IsNullOrEmpty(t.Net)) G(t.Net).Tracks.Add(t);
            }

            foreach (var v in board.Vias)
            {
                if (!string.IsNullOrEmpty(v.Net)) G(v.Net).Vias.Add(v);
            }

            foreach (var h in board.MountingHoles)
            {
                if (h.Plated && !string.IsNullOrEmpty(h.Net)) G(h.Net).Holes.Add(h);
            }

            if (useZones)
            {
                foreach (var z in board.Zones)
                {
                    if (string.IsNullOrEmpty(z.Net) || !z.IsFilled) continue;
                    foreach (var r in z.FilledRegions) G(z.Net).Islands.Add((z, r));
                }
            }

            foreach (var g in c._nets.Values) c.Join(g);
            return c;
        }

        private void Join(NetGraph g)
        {
            int layers = _board.LayerCount;
            g.Parent = Enumerable.Range(0, g.Count).ToArray();
            var pads = g.Pads;

            // Pads joined inside a part (two-pad buttons, multiple GND pins) and touching pads.
            for (int p = 0; p < pads.Count; p++)
            {
                for (int q = p + 1; q < pads.Count; q++)
                {
                    if (ReferenceEquals(pads[p].Component, pads[q].Component) ||
                        SharesLayer(pads[p], pads[q], layers) &&
                        Geometry.RectRectDistance(pads[p].X, pads[p].Y, pads[p].Width / 2, pads[p].Height / 2, pads[q].X, pads[q].Y, pads[q].Width / 2, pads[q].Height / 2) <= 0)
                    {
                        g.Union(p, q);
                    }
                }
            }

            for (int i = 0; i < g.Tracks.Count; i++)
            {
                var t = g.Tracks[i];
                int ti = g.TrackBase + i;
                for (int p = 0; p < pads.Count; p++)
                {
                    var pad = pads[p];
                    if (!pad.OnLayer(t.Layer)) continue;
                    if (Geometry.SegmentRectDistance(t.X1, t.Y1, t.X2, t.Y2, pad.X, pad.Y, pad.Width / 2, pad.Height / 2) <= t.Width / 2 + 1e-6) g.Union(ti, p);
                }

                for (int j = i + 1; j < g.Tracks.Count; j++)
                {
                    var u = g.Tracks[j];
                    if (u.Layer != t.Layer) continue;
                    if (Geometry.SegmentSegmentDistance(t.X1, t.Y1, t.X2, t.Y2, u.X1, u.Y1, u.X2, u.Y2) <= (t.Width + u.Width) / 2 * 0.5 + 1e-6) g.Union(ti, g.TrackBase + j);
                }

                for (int k = 0; k < g.Vias.Count; k++)
                {
                    var v = g.Vias[k];
                    if (!v.Spans(t.Layer, layers)) continue;
                    if (Geometry.PointSegmentDistance(v.X, v.Y, t.X1, t.Y1, t.X2, t.Y2) <= v.Diameter / 2 + 1e-6) g.Union(ti, g.ViaBase + k);
                }

                for (int k = 0; k < g.Holes.Count; k++)
                {
                    var h = g.Holes[k];
                    if (Geometry.PointSegmentDistance(h.X, h.Y, t.X1, t.Y1, t.X2, t.Y2) <= h.PadDiameter / 2 + t.Width / 2) g.Union(ti, g.HoleBase + k);
                }
            }

            for (int k = 0; k < g.Vias.Count; k++)
            {
                var v = g.Vias[k];
                for (int p = 0; p < pads.Count; p++)
                {
                    var pad = pads[p];
                    bool shared = _board.CopperLayers().Any(l => pad.OnLayer(l) && v.Spans(l, layers));
                    if (!shared) continue;
                    double dx = Math.Max(0, Math.Abs(v.X - pad.X) - pad.Width / 2), dy = Math.Max(0, Math.Abs(v.Y - pad.Y) - pad.Height / 2);
                    if (Math.Sqrt(dx * dx + dy * dy) <= v.Diameter / 2) g.Union(g.ViaBase + k, p);
                }

                for (int m = k + 1; m < g.Vias.Count; m++)
                {
                    var w = g.Vias[m];
                    bool shared = _board.CopperLayers().Any(l => v.Spans(l, layers) && w.Spans(l, layers));
                    if (shared && Math.Sqrt((v.X - w.X) * (v.X - w.X) + (v.Y - w.Y) * (v.Y - w.Y)) <= (v.Diameter + w.Diameter) / 2) g.Union(g.ViaBase + k, g.ViaBase + m);
                }
            }

            for (int k = 0; k < g.Holes.Count; k++)
            {
                var h = g.Holes[k];
                for (int p = 0; p < pads.Count; p++)
                {
                    var pad = pads[p];
                    double dx = Math.Max(0, Math.Abs(h.X - pad.X) - pad.Width / 2), dy = Math.Max(0, Math.Abs(h.Y - pad.Y) - pad.Height / 2);
                    if (Math.Sqrt(dx * dx + dy * dy) <= h.PadDiameter / 2) g.Union(g.HoleBase + k, p);
                }
            }

            for (int z = 0; z < g.Islands.Count; z++)
            {
                var (zone, region) = g.Islands[z];
                int zi = g.IslandBase + z;
                var b = region.Bounds();
                bool Near(double x0, double y0, double x1, double y1, double grow) =>
                    x1 + grow >= b.MinX && x0 - grow <= b.MaxX && y1 + grow >= b.MinY && y0 - grow <= b.MaxY;

                for (int p = 0; p < pads.Count; p++)
                {
                    var pad = pads[p];
                    if (!pad.OnLayer(zone.Layer)) continue;
                    if (!Near(pad.X - pad.Width / 2, pad.Y - pad.Height / 2, pad.X + pad.Width / 2, pad.Y + pad.Height / 2, 0)) continue;
                    if (RegionTouchesRect(region, pad.X, pad.Y, pad.Width / 2, pad.Height / 2)) g.Union(zi, p);
                }

                for (int i = 0; i < g.Tracks.Count; i++)
                {
                    var t = g.Tracks[i];
                    if (t.Layer != zone.Layer) continue;
                    if (!Near(Math.Min(t.X1, t.X2), Math.Min(t.Y1, t.Y2), Math.Max(t.X1, t.X2), Math.Max(t.Y1, t.Y2), t.Width)) continue;
                    if (RegionTouchesSegment(region, t.X1, t.Y1, t.X2, t.Y2, t.Width / 2)) g.Union(zi, g.TrackBase + i);
                }

                for (int k = 0; k < g.Vias.Count; k++)
                {
                    var v = g.Vias[k];
                    if (!v.Spans(zone.Layer, layers)) continue;
                    if (!Near(v.X, v.Y, v.X, v.Y, v.Diameter)) continue;
                    if (RegionTouchesSegment(region, v.X, v.Y, v.X, v.Y, v.Diameter / 2)) g.Union(zi, g.ViaBase + k);
                }

                for (int k = 0; k < g.Holes.Count; k++)
                {
                    var h = g.Holes[k];
                    if (!Near(h.X, h.Y, h.X, h.Y, h.PadDiameter)) continue;
                    if (RegionTouchesSegment(region, h.X, h.Y, h.X, h.Y, h.PadDiameter / 2)) g.Union(zi, g.HoleBase + k);
                }
            }
        }

        private static bool SharesLayer(PlacedPad a, PlacedPad b, int layers) =>
            PcbLayers.Stack(layers).Any(l => a.OnLayer(l) && b.OnLayer(l));

        /// <summary>True when the filled region overlaps the axis-aligned rectangle.</summary>
        public static bool RegionTouchesRect(PolyRegion r, double cx, double cy, double hx, double hy)
        {
            if (r.Contains(cx, cy)) return true;
            foreach (var c in r.Contours())
            {
                for (int i = 0, n = c.Count; i < n; i++)
                {
                    var a = c[i];
                    var b = c[(i + 1) % n];
                    if (Geometry.SegmentRectDistance(a.X, a.Y, b.X, b.Y, cx, cy, hx, hy) <= 0) return true;
                }
            }

            return false;
        }

        /// <summary>True when the filled region overlaps a segment of half-width <paramref name="half"/>.</summary>
        public static bool RegionTouchesSegment(PolyRegion r, double x1, double y1, double x2, double y2, double half)
        {
            if (r.Contains(x1, y1) || r.Contains(x2, y2)) return true;
            foreach (var c in r.Contours())
            {
                for (int i = 0, n = c.Count; i < n; i++)
                {
                    var a = c[i];
                    var b = c[(i + 1) % n];
                    if (Geometry.SegmentSegmentDistance(a.X, a.Y, b.X, b.Y, x1, y1, x2, y2) < half) return true;
                }
            }

            return false;
        }

        /// <summary>Groups of pads per net that copper already joins (largest group first).</summary>
        public List<List<PlacedPad>> PadGroups(string net)
        {
            var result = new List<List<PlacedPad>>();
            if (!_nets.TryGetValue(net, out var g)) return result;
            var byRoot = new Dictionary<int, List<PlacedPad>>();
            for (int p = 0; p < g.Pads.Count; p++)
            {
                int r = g.Find(p);
                if (!byRoot.TryGetValue(r, out var list)) byRoot[r] = list = new List<PlacedPad>();
                list.Add(g.Pads[p]);
            }

            result.AddRange(byRoot.Values.OrderByDescending(l => l.Count));
            return result;
        }

        public IEnumerable<string> Nets => _nets.Keys;

        /// <summary>Shortest connections joining the pad groups of each net (minimum spanning tree over groups).</summary>
        public List<(string Net, PlacedPad A, PlacedPad B)> Ratsnest()
        {
            var result = new List<(string, PlacedPad, PlacedPad)>();
            foreach (var net in _nets.Keys.OrderBy(n => n, StringComparer.Ordinal))
            {
                var groups = PadGroups(net);
                if (groups.Count < 2) continue;
                int n = groups.Count;
                var inTree = new bool[n];
                var best = new double[n];
                var from = new (int Group, PlacedPad A, PlacedPad B)[n];
                for (int i = 0; i < n; i++) best[i] = double.PositiveInfinity;
                best[0] = 0;
                for (int step = 0; step < n; step++)
                {
                    int u = -1;
                    for (int i = 0; i < n; i++)
                    {
                        if (!inTree[i] && (u < 0 || best[i] < best[u])) u = i;
                    }

                    inTree[u] = true;
                    if (step > 0) result.Add((net, from[u].A, from[u].B));
                    for (int i = 0; i < n; i++)
                    {
                        if (inTree[i]) continue;
                        foreach (var a in groups[u])
                        {
                            foreach (var b in groups[i])
                            {
                                double d = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
                                if (d < best[i])
                                {
                                    best[i] = d;
                                    from[i] = (u, a, b);
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>Nets whose pads are not all joined by copper, with the position of one stray pad.</summary>
        public List<(string Net, double X, double Y)> Unrouted()
        {
            var result = new List<(string, double, double)>();
            foreach (var net in _nets.Keys)
            {
                var groups = PadGroups(net);
                if (groups.Count < 2) continue;
                var stray = groups[1][0];
                result.Add((net, stray.X, stray.Y));
            }

            return result;
        }

        /// <summary>True when the track end at (x, y) touches another copper item of its net.</summary>
        public bool EndConnected(Track t, double x, double y)
        {
            if (!_nets.TryGetValue(t.Net, out var g)) return false;
            int layers = _board.LayerCount;
            foreach (var p in g.Pads)
            {
                if (!p.OnLayer(t.Layer)) continue;
                double dx = Math.Max(0, Math.Abs(x - p.X) - p.Width / 2), dy = Math.Max(0, Math.Abs(y - p.Y) - p.Height / 2);
                if (Math.Sqrt(dx * dx + dy * dy) <= t.Width / 2 + 1e-6) return true;
            }

            foreach (var u in g.Tracks)
            {
                if (ReferenceEquals(u, t) || u.Layer != t.Layer) continue;
                if (Geometry.PointSegmentDistance(x, y, u.X1, u.Y1, u.X2, u.Y2) <= u.Width / 2 + 1e-6) return true;
            }

            foreach (var v in g.Vias)
            {
                if (v.Spans(t.Layer, layers) && Math.Sqrt((v.X - x) * (v.X - x) + (v.Y - y) * (v.Y - y)) <= v.Diameter / 2 + 1e-6) return true;
            }

            foreach (var h in g.Holes)
            {
                if (Math.Sqrt((h.X - x) * (h.X - x) + (h.Y - y) * (h.Y - y)) <= h.PadDiameter / 2 + 1e-6) return true;
            }

            foreach (var (zone, region) in g.Islands)
            {
                if (zone.Layer == t.Layer && RegionTouchesSegment(region, x, y, x, y, t.Width / 2)) return true;
            }

            return false;
        }
    }
}
