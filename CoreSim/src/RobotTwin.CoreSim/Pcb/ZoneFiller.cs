using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// Fills copper zones the way KiCad does: the zone outline (clipped to the board
    /// outline inset by the edge clearance) is shrunk by half the minimum width, every
    /// foreign copper item is subtracted grown by its clearance plus half the minimum
    /// width, and the result is grown back by half the minimum width. This removes necks
    /// thinner than the minimum width and rounds every corner. Pads of the zone's own
    /// net get a thermal gap bridged by spokes (or a solid joint), and islands that touch
    /// nothing of the net are removed. Zones are filled in priority order; a lower
    /// priority zone keeps clear of the copper of higher priority zones.
    /// </summary>
    public static class ZoneFiller
    {
        private const double Tolerance = 0.005;

        /// <summary>Fills every zone (highest priority first). Returns the time spent in milliseconds.</summary>
        public static double FillAll(BoardDocument board)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var zone in FillOrder(board)) Fill(board, zone);
            return watch.Elapsed.TotalMilliseconds;
        }

        /// <summary>Priority order: higher priority first, then list order.</summary>
        public static List<Zone> FillOrder(BoardDocument board) =>
            board.Zones.Select((z, i) => (z, i)).OrderByDescending(t => t.z.Priority).ThenBy(t => t.i).Select(t => t.z).ToList();

        /// <summary>Zones on the same layer that win over <paramref name="zone"/> where they overlap.</summary>
        private static IEnumerable<Zone> Higher(BoardDocument board, Zone zone)
        {
            int index = board.Zones.IndexOf(zone);
            for (int i = 0; i < board.Zones.Count; i++)
            {
                var z = board.Zones[i];
                if (ReferenceEquals(z, zone) || z.Layer != zone.Layer) continue;
                if (z.Priority > zone.Priority || (z.Priority == zone.Priority && i < index)) yield return z;
            }
        }

        /// <summary>True when the board changed in a way that affects this zone since it was filled.</summary>
        public static bool IsOutdated(BoardDocument board, Zone zone) => !zone.IsFilled || zone.FillHash != Signature(board, zone);

        /// <summary>Refills only zones whose fill is out of date; returns how many were refilled.</summary>
        public static int RefillOutdated(BoardDocument board)
        {
            int n = 0;
            foreach (var zone in FillOrder(board))
            {
                if (!IsOutdated(board, zone)) continue;
                Fill(board, zone);
                n++;
            }

            return n;
        }

        public static void Fill(BoardDocument board, Zone zone)
        {
            zone.Fill = Compute(board, zone).Select(ZoneFillIsland.From).ToList();
            zone.FillHash = Signature(board, zone);
        }

        private static double ClearanceTo(BoardDocument board, Zone zone, string? net) =>
            Math.Max(zone.Clearance, board.ClearanceBetween(zone.Net, net));

        private static bool Same(string? a, string? b) => !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        public static List<PolyRegion> Compute(BoardDocument board, Zone zone)
        {
            if (!board.HasLayer(zone.Layer)) return new List<PolyRegion>();
            double r = Math.Max(0, zone.MinWidth / 2);
            int layers = board.LayerCount;
            var layer = zone.Layer;

            // Allowed area: zone outline ∩ board inset by the edge clearance.
            var boardRegion = PolyBool.Union(new[] { board.OutlinePolygon(Tolerance) });
            var allowed = PolyOffset.Offset(boardRegion, -board.Rules.EdgeClearance, Tolerance);
            if (!zone.IsWholeBoard)
            {
                allowed = PolyBool.Intersection(allowed, new[] { Polygon.CounterClockwise(zone.Outline) });
            }

            // [keepout] rule areas that forbid pours on this layer are cut out
            var keepouts = board.RuleAreas.Where(a => a.NoPour && a.OnLayer(layer) && a.Outline.Count >= 3).Select(a => (IReadOnlyList<PcbPoint>)Polygon.CounterClockwise(a.Outline)).ToList();
            if (keepouts.Count > 0 && allowed.Count > 0) allowed = PolyBool.Difference(allowed, keepouts);

            if (allowed.Count == 0) return allowed;
            var core = r > 0 ? PolyOffset.Offset(allowed, -r, Tolerance) : allowed;

            // Foreign copper (grown by clearance + r for the fill, by the clearance alone for spokes).
            var obstacles = new List<IReadOnlyList<PcbPoint>>();
            var spokeObstacles = new List<IReadOnlyList<PcbPoint>>();
            var thermal = new List<PlacedPad>();
            void Add(Func<double, List<PcbPoint>> shape, double clearance)
            {
                obstacles.Add(shape(clearance + r));
                spokeObstacles.Add(shape(clearance));
            }

            foreach (var pad in board.Pads())
            {
                if (pad.Pad.IsThroughHole && pad.Pad.NonPlated)
                {
                    var p0 = pad;
                    Add(g => Shapes.Circle(p0.X, p0.Y, p0.Pad.Drill / 2 + g, Tolerance), board.Rules.HoleClearance);
                    continue;
                }

                if (!pad.OnLayer(layer)) continue;
                var p = pad;
                Func<double, List<PcbPoint>> shape = g => Shapes.Pad(p.Pad.Shape, p.X, p.Y, p.Width, p.Height, p.Pad.CornerRatio, g, Tolerance);
                if (Same(pad.Net, zone.Net))
                {
                    switch (zone.PadConnection)
                    {
                        case ZonePadConnection.Solid:
                            break;
                        case ZonePadConnection.None:
                            Add(shape, ClearanceTo(board, zone, pad.Net));
                            break;
                        default:
                            obstacles.Add(shape(zone.ThermalGap + r));
                            thermal.Add(pad);
                            break;
                    }

                    continue;
                }

                Add(shape, ClearanceTo(board, zone, pad.Net));
            }

            foreach (var t in board.Tracks)
            {
                if (t.Layer != layer || Same(t.Net, zone.Net)) continue;
                var tt = t;
                Add(g => Shapes.Stadium(tt.X1, tt.Y1, tt.X2, tt.Y2, tt.Width / 2 + g, Tolerance), ClearanceTo(board, zone, t.Net));
            }

            foreach (var v in board.Vias)
            {
                if (!v.Spans(layer, layers))
                {
                    continue;
                }

                if (Same(v.Net, zone.Net)) continue;
                var vv = v;
                Add(g => Shapes.Circle(vv.X, vv.Y, vv.Diameter / 2 + g, Tolerance), ClearanceTo(board, zone, v.Net));
            }

            foreach (var h in board.MountingHoles)
            {
                if (h.Plated && Same(h.Net, zone.Net)) continue;
                var hh = h;
                double radius = h.Plated ? h.PadDiameter / 2 : h.Drill / 2;
                double c = h.Plated ? ClearanceTo(board, zone, h.Net) : board.Rules.HoleClearance;
                Add(g => Shapes.Circle(hh.X, hh.Y, radius + g, Tolerance), c);
            }

            // [teardrop] another net's teardrops are copper like its tracks: the pour keeps its clearance from them
            foreach (var td in Teardrops.Of(board))
            {
                if (td.Layer != layer || Same(td.Net, zone.Net)) continue;
                double c = ClearanceTo(board, zone, td.Net);
                foreach (var reg in PolyOffset.OffsetContours(new[] { td.Outline }, c + r, Tolerance)) obstacles.AddRange(reg.Contours());
                foreach (var reg in PolyOffset.OffsetContours(new[] { td.Outline }, c, Tolerance)) spokeObstacles.AddRange(reg.Contours());
            }

            foreach (var z in Higher(board, zone))
            {
                if (Same(z.Net, zone.Net) || !z.IsFilled || z.FilledRegions.Count == 0) continue;
                double c = ClearanceTo(board, zone, z.Net);
                foreach (var reg in PolyOffset.Offset(z.FilledRegions, c + r, Tolerance)) obstacles.AddRange(reg.Contours());
                foreach (var reg in PolyOffset.Offset(z.FilledRegions, c, Tolerance)) spokeObstacles.AddRange(reg.Contours());
            }

            var cut = PolyBool.Difference(core, obstacles);
            var fill = r > 0 ? PolyOffset.Offset(cut, r, Tolerance) : cut;

            // Thermal spokes, kept when they reach the poured copper.
            if (thermal.Count > 0 && fill.Count > 0)
            {
                var spokes = new List<IReadOnlyList<PcbPoint>>();
                double w = Math.Max(zone.ThermalSpokeWidth, 0.05);
                foreach (var pad in thermal)
                {
                    foreach (var (dx, dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
                    {
                        double half = dx != 0 ? pad.Width / 2 : pad.Height / 2;
                        double reach = half + zone.ThermalGap + Math.Max(r, w / 2);
                        double tip = half + zone.ThermalGap + r + 0.02;
                        if (!Polygon.AnyContains(fill, pad.X + dx * tip, pad.Y + dy * tip)) continue;
                        spokes.Add(Shapes.Bar(pad.X, pad.Y, pad.X + dx * reach, pad.Y + dy * reach, w));
                    }
                }

                if (spokes.Count > 0)
                {
                    var clipped = PolyBool.Difference(PolyBool.Intersection(allowed, spokes), spokeObstacles);
                    if (clipped.Count > 0) fill = PolyBool.Union(fill.Concat(clipped));
                }
            }

            if (zone.IslandRemoval == IslandRemoval.Never || string.IsNullOrEmpty(zone.Net)) return fill;
            var kept = new List<PolyRegion>();
            foreach (var region in fill)
            {
                if (Touches(board, zone, region) || zone.IslandRemoval == IslandRemoval.BelowArea && region.Area >= zone.MinIslandArea)
                {
                    kept.Add(region);
                }
            }

            return kept;
        }

        /// <summary>True when an island touches a pad, track, via or plated hole of the zone's net.</summary>
        private static bool Touches(BoardDocument board, Zone zone, PolyRegion region)
        {
            var b = region.Bounds();
            bool Near(double x, double y, double grow) => x + grow >= b.MinX && x - grow <= b.MaxX && y + grow >= b.MinY && y - grow <= b.MaxY;
            foreach (var p in board.Pads())
            {
                if (!Same(p.Net, zone.Net) || !p.OnLayer(zone.Layer) || !Near(p.X, p.Y, Math.Max(p.Width, p.Height))) continue;
                if (BoardConnectivity.RegionTouchesRect(region, p.X, p.Y, p.Width / 2, p.Height / 2)) return true;
            }

            foreach (var t in board.Tracks)
            {
                if (!Same(t.Net, zone.Net) || t.Layer != zone.Layer) continue;
                if (!Near((t.X1 + t.X2) / 2, (t.Y1 + t.Y2) / 2, t.Length / 2 + t.Width)) continue;
                if (BoardConnectivity.RegionTouchesSegment(region, t.X1, t.Y1, t.X2, t.Y2, t.Width / 2)) return true;
            }

            foreach (var v in board.Vias)
            {
                if (!Same(v.Net, zone.Net) || !v.Spans(zone.Layer, board.LayerCount) || !Near(v.X, v.Y, v.Diameter)) continue;
                if (BoardConnectivity.RegionTouchesSegment(region, v.X, v.Y, v.X, v.Y, v.Diameter / 2)) return true;
            }

            foreach (var h in board.MountingHoles)
            {
                if (!h.Plated || !Same(h.Net, zone.Net) || !Near(h.X, h.Y, h.PadDiameter)) continue;
                if (BoardConnectivity.RegionTouchesSegment(region, h.X, h.Y, h.X, h.Y, h.PadDiameter / 2)) return true;
            }

            return false;
        }

        /// <summary>
        /// Fingerprint of everything that shapes a zone's fill: its settings, the outline and
        /// rules, every copper item on its layer and the fills of higher priority zones.
        /// </summary>
        public static string Signature(BoardDocument board, Zone zone)
        {
            var sb = new StringBuilder(4096);
            void N(double v) => sb.Append(Math.Round(v, 5).ToString("R", CultureInfo.InvariantCulture)).Append(',');
            void S(string? v) => sb.Append(v ?? string.Empty).Append('|');

            S(zone.Net);
            S(zone.Layer.ToString());
            N(zone.Priority);
            N(zone.Clearance);
            N(zone.MinWidth);
            S(zone.PadConnection.ToString());
            N(zone.ThermalGap);
            N(zone.ThermalSpokeWidth);
            S(zone.IslandRemoval.ToString());
            N(zone.MinIslandArea);
            foreach (var p in zone.Outline ?? new List<PcbPoint>())
            {
                N(p.X);
                N(p.Y);
            }

            foreach (var a in board.RuleAreas.Where(a => a.NoPour && a.OnLayer(zone.Layer)))   // [keepout] a rule area changed: the fill is old
            {
                S("keepout");
                foreach (var p in a.Outline)
                {
                    N(p.X);
                    N(p.Y);
                }
            }

            S("board");
            N(board.LayerCount);
            N(board.Rules.EdgeClearance);
            N(board.Rules.HoleClearance);
            foreach (var e in board.OutlineElements())
            {
                N(e.X1);
                N(e.Y1);
                N(e.R);
            }

            foreach (var c in board.NetClasses)
            {
                S(c.Name);
                N(c.Clearance);
            }

            S("pads");
            foreach (var p in board.Pads())
            {
                if (!p.OnLayer(zone.Layer) && !(p.Pad.IsThroughHole && p.Pad.NonPlated)) continue;
                S(p.Net);
                S(board.ClassNameFor(p.Net));
                N(p.X);
                N(p.Y);
                N(p.Width);
                N(p.Height);
                S(p.Pad.Shape.ToString());
                N(p.Pad.CornerRatio);
                N(p.Pad.Drill);
            }

            S("tracks");
            foreach (var t in board.Tracks)
            {
                if (t.Layer != zone.Layer) continue;
                S(t.Net);
                S(board.ClassNameFor(t.Net));
                N(t.X1);
                N(t.Y1);
                N(t.X2);
                N(t.Y2);
                N(t.Width);
            }

            S("vias");
            foreach (var v in board.Vias)
            {
                if (!v.Spans(zone.Layer, board.LayerCount)) continue;
                S(v.Net);
                S(board.ClassNameFor(v.Net));
                N(v.X);
                N(v.Y);
                N(v.Diameter);
            }

            S("holes");
            foreach (var h in board.MountingHoles)
            {
                S(h.Net);
                N(h.X);
                N(h.Y);
                N(h.Drill);
                N(h.PadDiameter);
            }

            S("higher");
            foreach (var z in Higher(board, zone))
            {
                S(z.Net);
                S(z.FillHash);
            }

            // FNV-1a 64.
            ulong hash = 14695981039346656037UL;
            foreach (char ch in sb.ToString())
            {
                hash = (hash ^ ch) * 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
