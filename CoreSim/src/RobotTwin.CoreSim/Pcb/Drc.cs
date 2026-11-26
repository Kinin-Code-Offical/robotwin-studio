using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    public enum DrcSeverity
    {
        Warning,
        Error
    }

    public sealed class DrcViolation
    {
        public DrcViolation(DrcSeverity severity, string rule, string message, double x, double y, CopperLayer? layer = null)
        {
            Severity = severity;
            Rule = rule;
            Message = message;
            X = x;
            Y = y;
            Layer = layer;
        }

        public DrcSeverity Severity { get; }
        public string Rule { get; }
        public string Message { get; }
        public double X { get; }
        public double Y { get; }

        /// <summary>Copper layer of the violation, when it belongs to one.</summary>
        public CopperLayer? Layer { get; }

        public override string ToString() => $"[{Severity}] {Rule}: {Message} @ ({X:0.##}, {Y:0.##}) mm";
    }

    /// <summary>
    /// Design rule check: per-net-class copper clearances on every layer, widths, drills,
    /// annular rings, hole-to-hole, board-edge clearance against the real outline,
    /// courtyard overlaps, silkscreen over pads, connectivity (zone fills included),
    /// dangling tracks and vias, outdated zone fills, via types and current capacity.
    /// </summary>
    public static class DesignRuleChecker
    {
        private const double Eps = 1e-3;

        public static List<DrcViolation> Check(BoardDocument board, IReadOnlyDictionary<string, double>? netCurrents = null)
        {
            var v = new List<DrcViolation>();
            var rules = board.Rules;
            var pads = board.Pads().ToList();
            int layerCount = board.LayerCount;
            var layers = board.CopperLayers();

            // ---------------------------------------------------------- items on missing layers, widths, drills
            foreach (var t in board.Tracks)
            {
                if (!board.HasLayer(t.Layer))
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Katman yok", $"{t.Net} yolu {PcbLayers.KiCadName(t.Layer)} üzerinde, kart {layerCount} katmanlı", t.X1, t.Y1, t.Layer));
                }

                if (t.Width < rules.MinTrackWidth - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Minimum yol genişliği",
                        $"{t.Net} yolu {t.Width:0.###} mm (en az {rules.MinTrackWidth} mm)", t.X1, t.Y1, t.Layer));
                }
            }

            foreach (var via in board.Vias)
            {
                if (via.Drill < rules.MinDrill - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Minimum delik", $"via deliği {via.Drill} mm (en az {rules.MinDrill} mm)", via.X, via.Y));
                }

                double ring = (via.Diameter - via.Drill) / 2;
                if (ring < rules.MinAnnularRing - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Halka genişliği", $"{via.Net} viası: halka {ring:0.###} mm (en az {rules.MinAnnularRing} mm)", via.X, via.Y));
                }

                if (!board.HasLayer(via.StartLayer) || !board.HasLayer(via.EndLayer) || via.StartLayer == via.EndLayer)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Via katmanları", $"{via.Net} viası geçersiz katman aralığında ({PcbLayers.KiCadName(via.StartLayer)}–{PcbLayers.KiCadName(via.EndLayer)})", via.X, via.Y));
                }
                else if (!via.IsThrough(layerCount) && !board.Stackup.AllowBlindBuriedVias)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Kör/gömülü via", $"{via.Net} viası kör/gömülü; kart ayarlarında izin verilmemiş", via.X, via.Y));
                }
            }

            foreach (var p in pads)
            {
                if (!p.Pad.IsThroughHole) continue;
                if (p.Pad.Drill < rules.MinDrill - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Minimum delik", $"{p.Component.RefDes}.{p.Pad.Name} deliği {p.Pad.Drill} mm", p.X, p.Y));
                }

                double ring = (Math.Min(p.Width, p.Height) - p.Pad.Drill) / 2;
                if (!p.Pad.NonPlated && ring < rules.MinAnnularRing - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Halka genişliği", $"{p.Component.RefDes}.{p.Pad.Name}: halka {ring:0.###} mm (en az {rules.MinAnnularRing} mm)", p.X, p.Y));
                }
            }

            foreach (var h in board.MountingHoles)
            {
                if (h.Plated && (h.PadDiameter - h.Drill) / 2 < rules.MinAnnularRing - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Halka genişliği", $"montaj deliği Ø{h.Drill}: halka {(h.PadDiameter - h.Drill) / 2:0.###} mm", h.X, h.Y));
                }
            }

            // ---------------------------------------------------------- copper clearances per layer
            foreach (var l in layers)
            {
                var tracks = board.Tracks.Where(t => t.Layer == l).ToList();
                var layerPads = pads.Where(p => p.OnLayer(l)).ToList();
                var vias = board.Vias.Where(x => x.Spans(l, layerCount)).ToList();
                var holes = board.MountingHoles.Where(h => h.Plated).ToList();

                for (int i = 0; i < tracks.Count; i++)
                {
                    var a = tracks[i];
                    for (int j = i + 1; j < tracks.Count; j++)
                    {
                        var b = tracks[j];
                        if (SameNet(a.Net, b.Net)) continue;
                        double need = board.ClearanceBetween(a.Net, b.Net);
                        if (!BoxesNear(a, b, need)) continue;
                        double d = Geometry.SegmentSegmentDistance(a.X1, a.Y1, a.X2, a.Y2, b.X1, b.Y1, b.X2, b.Y2) - a.Width / 2 - b.Width / 2;
                        if (d < need - Eps)
                        {
                            v.Add(Clearance(d < 0 ? "Kısa devre" : "Yol-yol aralığı", a.Net, b.Net, d, need, (a.X1 + a.X2) / 2, (a.Y1 + a.Y2) / 2, l));
                        }
                    }

                    foreach (var p in layerPads)
                    {
                        if (SameNet(a.Net, p.Net)) continue;
                        double need = board.ClearanceBetween(a.Net, p.Net);
                        double d = PadSegmentDistance(p, a.X1, a.Y1, a.X2, a.Y2) - a.Width / 2;
                        if (d < need - Eps)
                        {
                            v.Add(Clearance(d < 0 ? "Kısa devre" : "Yol-ped aralığı", a.Net, $"{p.Component.RefDes}.{p.Pad.Name}", d, need, p.X, p.Y, l));
                        }
                    }

                    foreach (var h in holes)
                    {
                        if (SameNet(a.Net, h.Net)) continue;
                        double need = board.ClearanceBetween(a.Net, h.Net);
                        double d = Geometry.PointSegmentDistance(h.X, h.Y, a.X1, a.Y1, a.X2, a.Y2) - h.PadDiameter / 2 - a.Width / 2;
                        if (d < need - Eps) v.Add(Clearance(d < 0 ? "Kısa devre" : "Yol-delik aralığı", a.Net, "montaj deliği", d, need, h.X, h.Y, l));
                    }
                }

                for (int i = 0; i < layerPads.Count; i++)
                {
                    for (int j = i + 1; j < layerPads.Count; j++)
                    {
                        var a = layerPads[i];
                        var b = layerPads[j];
                        if (ReferenceEquals(a.Component, b.Component)) continue;
                        if (!string.IsNullOrEmpty(a.Net) && SameNet(a.Net, b.Net)) continue;
                        double need = board.ClearanceBetween(a.Net, b.Net);
                        double d = PadPadDistance(a, b);
                        if (d < need - Eps)
                        {
                            v.Add(Clearance(d <= 0 ? "Kısa devre" : "Ped-ped aralığı",
                                $"{a.Component.RefDes}.{a.Pad.Name}", $"{b.Component.RefDes}.{b.Pad.Name}", d, need, a.X, a.Y, l));
                        }
                    }
                }

                for (int i = 0; i < vias.Count; i++)
                {
                    var via = vias[i];
                    foreach (var t in tracks)
                    {
                        if (SameNet(via.Net, t.Net)) continue;
                        double need = board.ClearanceBetween(via.Net, t.Net);
                        double d = Geometry.PointSegmentDistance(via.X, via.Y, t.X1, t.Y1, t.X2, t.Y2) - via.Diameter / 2 - t.Width / 2;
                        if (d < need - Eps) v.Add(Clearance(d < 0 ? "Kısa devre" : "Via-yol aralığı", via.Net, t.Net, d, need, via.X, via.Y, l));
                    }

                    foreach (var p in layerPads)
                    {
                        if (SameNet(via.Net, p.Net)) continue;
                        double need = board.ClearanceBetween(via.Net, p.Net);
                        double d = PadSegmentDistance(p, via.X, via.Y, via.X, via.Y) - via.Diameter / 2;
                        if (d < need - Eps)
                        {
                            v.Add(Clearance(d < 0 ? "Kısa devre" : "Via-ped aralığı", via.Net, $"{p.Component.RefDes}.{p.Pad.Name}", d, need, via.X, via.Y, l));
                        }
                    }

                    for (int j = i + 1; j < vias.Count; j++)
                    {
                        var w = vias[j];
                        if (SameNet(via.Net, w.Net)) continue;
                        double need = board.ClearanceBetween(via.Net, w.Net);
                        double d = Math.Sqrt((via.X - w.X) * (via.X - w.X) + (via.Y - w.Y) * (via.Y - w.Y)) - (via.Diameter + w.Diameter) / 2;
                        if (d < need - Eps) v.Add(Clearance(d < 0 ? "Kısa devre" : "Via-via aralığı", via.Net, w.Net, d, need, via.X, via.Y, l));
                    }
                }
            }

            // ---------------------------------------------------------- holes
            var drills = new List<(double X, double Y, double R, string What, CopperLayer From, CopperLayer To)>();
            foreach (var p in pads)
            {
                if (p.Pad.IsThroughHole) drills.Add((p.X, p.Y, p.Pad.Drill / 2, $"{p.Component.RefDes}.{p.Pad.Name}", CopperLayer.Top, CopperLayer.Bottom));
            }

            foreach (var via in board.Vias)
            {
                var span = via.Span(layerCount);
                drills.Add((via.X, via.Y, via.Drill / 2, $"{via.Net} viası", span.From, span.To));
            }

            foreach (var h in board.MountingHoles) drills.Add((h.X, h.Y, h.Drill / 2, $"montaj deliği Ø{h.Drill}", CopperLayer.Top, CopperLayer.Bottom));

            for (int i = 0; i < drills.Count; i++)
            {
                for (int j = i + 1; j < drills.Count; j++)
                {
                    var a = drills[i];
                    var b = drills[j];
                    if (Math.Abs(a.X - b.X) > a.R + b.R + rules.MinHoleToHole + 1 || Math.Abs(a.Y - b.Y) > a.R + b.R + rules.MinHoleToHole + 1) continue;
                    if (!SpansOverlap(a.From, a.To, b.From, b.To, layerCount)) continue;
                    double d = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) - a.R - b.R;
                    if (d < rules.MinHoleToHole - Eps)
                    {
                        v.Add(new DrcViolation(DrcSeverity.Error, "Delik-delik aralığı",
                            $"{a.What} ↔ {b.What}: {Math.Max(0, d):0.###} mm (en az {rules.MinHoleToHole} mm)", (a.X + b.X) / 2, (a.Y + b.Y) / 2));
                    }
                }
            }

            // Copper of every net keeps clear of non-plated holes.
            foreach (var h in board.MountingHoles.Where(h => !h.Plated))
            {
                foreach (var t in board.Tracks)
                {
                    double d = Geometry.PointSegmentDistance(h.X, h.Y, t.X1, t.Y1, t.X2, t.Y2) - h.Drill / 2 - t.Width / 2;
                    if (d < rules.HoleClearance - Eps) v.Add(new DrcViolation(DrcSeverity.Error, "Delik aralığı", $"{t.Net} yolu montaj deliğine {Math.Max(0, d):0.###} mm", h.X, h.Y, t.Layer));
                }

                foreach (var via in board.Vias)
                {
                    double d = Math.Sqrt((h.X - via.X) * (h.X - via.X) + (h.Y - via.Y) * (h.Y - via.Y)) - h.Drill / 2 - via.Diameter / 2;
                    if (d < rules.HoleClearance - Eps) v.Add(new DrcViolation(DrcSeverity.Error, "Delik aralığı", $"{via.Net} viası montaj deliğine {Math.Max(0, d):0.###} mm", h.X, h.Y));
                }
            }

            // ---------------------------------------------------------- board edge (real outline)
            var outline = board.OutlinePolygon(0.005);
            double EdgeDist(double x1, double y1, double x2, double y2)
            {
                double best = double.PositiveInfinity;
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    best = Math.Min(best, Geometry.SegmentSegmentDistance(x1, y1, x2, y2, a.X, a.Y, b.X, b.Y));
                }

                bool inside = Polygon.Winding(outline, x1, y1) != 0 && Polygon.Winding(outline, x2, y2) != 0;
                return inside ? best : -best;
            }

            foreach (var t in board.Tracks)
            {
                double m = EdgeDist(t.X1, t.Y1, t.X2, t.Y2) - t.Width / 2;
                if (m < rules.EdgeClearance - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Kart kenarı", $"{t.Net} yolu kenara {m:0.##} mm (en az {rules.EdgeClearance} mm)", t.X1, t.Y1, t.Layer));
                }
            }

            foreach (var via in board.Vias)
            {
                double m = EdgeDist(via.X, via.Y, via.X, via.Y) - via.Diameter / 2;
                if (m < rules.EdgeClearance - Eps) v.Add(new DrcViolation(DrcSeverity.Error, "Kart kenarı", $"{via.Net} viası kenara {m:0.##} mm", via.X, via.Y));
            }

            foreach (var p in pads)
            {
                double inner = Math.Min(p.CornerRadius, Math.Min(p.Width, p.Height) / 2);
                double hx = p.Width / 2 - inner, hy = p.Height / 2 - inner;
                double m = double.PositiveInfinity;
                for (int i = 0; i < outline.Count; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Count];
                    m = Math.Min(m, Geometry.SegmentRectDistance(a.X, a.Y, b.X, b.Y, p.X, p.Y, hx, hy) - inner);
                }

                if (Polygon.Winding(outline, p.X, p.Y) == 0) m = -1;
                if (m < rules.EdgeClearance - Eps)
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Kart kenarı",
                        $"{p.Component.RefDes}.{p.Pad.Name} kart dışına taşıyor veya kenara çok yakın", p.X, p.Y));
                }
            }

            foreach (var h in board.MountingHoles)
            {
                double r = h.Plated ? h.PadDiameter / 2 : h.Drill / 2;
                double m = EdgeDist(h.X, h.Y, h.X, h.Y) - r;
                double need = h.Plated ? rules.EdgeClearance : 0;
                if (m < need - Eps) v.Add(new DrcViolation(DrcSeverity.Error, "Kart kenarı", $"montaj deliği kenara {m:0.##} mm", h.X, h.Y));
            }

            // ---------------------------------------------------------- courtyards
            var boxes = new List<(BoardComponent C, (double MinX, double MinY, double MaxX, double MaxY) B)>();
            foreach (var c in board.Components)
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp != null) boxes.Add((c, c.Courtyard(fp)));
            }

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i].B;
                    var b = boxes[j].B;
                    if (boxes[i].C.Side != boxes[j].C.Side) continue;
                    if (a.MinX < b.MaxX && a.MaxX > b.MinX && a.MinY < b.MaxY && a.MaxY > b.MinY)
                    {
                        v.Add(new DrcViolation(DrcSeverity.Warning, "Gövde çakışması",
                            $"{boxes[i].C.RefDes} ile {boxes[j].C.RefDes} üst üste", (a.MinX + a.MaxX) / 2, (a.MinY + a.MaxY) / 2));
                    }
                }

                foreach (var h in board.MountingHoles)
                {
                    var a = boxes[i].B;
                    double r = Math.Max(h.Drill, h.PadDiameter) / 2;
                    if (h.X + r > a.MinX && h.X - r < a.MaxX && h.Y + r > a.MinY && h.Y - r < a.MaxY)
                    {
                        v.Add(new DrcViolation(DrcSeverity.Warning, "Gövde çakışması", $"{boxes[i].C.RefDes} montaj deliğinin üstünde", h.X, h.Y));
                    }
                }
            }

            // ---------------------------------------------------------- silkscreen over pads
            foreach (var issue in SilkOverPads(board, pads))
            {
                v.Add(new DrcViolation(DrcSeverity.Warning, "Serigrafi pedde", issue.Message, issue.X, issue.Y));
            }

            // ---------------------------------------------------------- connectivity
            var connectivity = BoardConnectivity.Build(board);
            foreach (var (net, x, y) in connectivity.Unrouted())
            {
                v.Add(new DrcViolation(DrcSeverity.Error, "Bağlanmamış", $"{net} hattında bağlantısı yapılmamış ped var", x, y));
            }

            foreach (var t in board.Tracks)
            {
                if (string.IsNullOrEmpty(t.Net)) continue;
                foreach (var (x, y) in new[] { (t.X1, t.Y1), (t.X2, t.Y2) })
                {
                    if (!connectivity.EndConnected(t, x, y))
                    {
                        v.Add(new DrcViolation(DrcSeverity.Warning, "Boşta yol ucu", $"{t.Net} yolunun ucu hiçbir şeye bağlı değil", x, y, t.Layer));
                    }
                }
            }

            foreach (var via in board.Vias)
            {
                int touches = 0;
                foreach (var l in layers)
                {
                    if (!via.Spans(l, layerCount)) continue;
                    bool any = board.Tracks.Any(t => t.Layer == l && SameNet(t.Net, via.Net) && Geometry.PointSegmentDistance(via.X, via.Y, t.X1, t.Y1, t.X2, t.Y2) <= via.Diameter / 2 + 1e-6) ||
                               pads.Any(p => p.OnLayer(l) && SameNet(p.Net, via.Net) && PadSegmentDistance(p, via.X, via.Y, via.X, via.Y) <= via.Diameter / 2) ||
                               board.Zones.Any(z => z.Layer == l && SameNet(z.Net, via.Net) && z.IsFilled &&
                                                    z.FilledRegions.Any(r => BoardConnectivity.RegionTouchesSegment(r, via.X, via.Y, via.X, via.Y, via.Diameter / 2)));
                    if (any) touches++;
                }

                if (touches < 2)
                {
                    v.Add(new DrcViolation(DrcSeverity.Warning, "Bağlantısız via", $"{via.Net} viası {touches} katmana bağlı (en az 2 olmalı)", via.X, via.Y));
                }
            }

            // ---------------------------------------------------------- zones
            foreach (var z in board.Zones)
            {
                var (zx, zy) = ZoneAnchor(board, z);
                if (!board.HasLayer(z.Layer))
                {
                    v.Add(new DrcViolation(DrcSeverity.Error, "Katman yok", $"{z.Net} dolgusu {PcbLayers.KiCadName(z.Layer)} üzerinde", zx, zy, z.Layer));
                }
                else if (!z.IsFilled)
                {
                    v.Add(new DrcViolation(DrcSeverity.Warning, "Dolgu yapılmamış", $"{z.Net} dolgusu ({PcbLayers.DisplayName(z.Layer)}) henüz doldurulmadı", zx, zy, z.Layer));
                }
                else if (ZoneFiller.IsOutdated(board, z))
                {
                    v.Add(new DrcViolation(DrcSeverity.Warning, "Dolgu güncel değil", $"{z.Net} dolgusu ({PcbLayers.DisplayName(z.Layer)}) son değişikliklerden önce yapılmış; yeniden doldurun", zx, zy, z.Layer));
                }
            }

            // ---------------------------------------------------------- current capacity (IPC-2221)
            if (netCurrents != null)
            {
                foreach (var t in board.Tracks)
                {
                    if (!netCurrents.TryGetValue(t.Net, out double amps)) continue;
                    double capacity = board.CurrentCapacity(t.Width, t.Layer);
                    if (Math.Abs(amps) > capacity)
                    {
                        v.Add(new DrcViolation(DrcSeverity.Error, "Akım kapasitesi",
                            $"{t.Net}: {Math.Abs(amps):0.00} A geçiyor, {t.Width:0.##} mm yol en fazla {capacity:0.00} A taşır (ΔT {rules.AllowedTempRiseC} °C)",
                            (t.X1 + t.X2) / 2, (t.Y1 + t.Y2) / 2, t.Layer));
                    }
                }
            }

            // ---------------------------------------------------------- [pairs] the board's differential pairs and length groups
            v.AddRange(NetLengths.Check(board));
            v.AddRange(RuleAreaCheck.Check(board));   // [keepout]

            // ---------------------------------------------------------- [enclosure] the box the board goes into, when it has one
            if (board.Enclosure != null) v.AddRange(EnclosureCheck.Check(board, board.Enclosure));
            return v;
        }

        private static (double X, double Y) ZoneAnchor(BoardDocument board, Zone z)
        {
            if (!z.IsWholeBoard) return (z.Outline[0].X, z.Outline[0].Y);
            return (board.Rules.EdgeClearance + 1, board.Rules.EdgeClearance + 1);
        }

        private static bool SpansOverlap(CopperLayer a0, CopperLayer a1, CopperLayer b0, CopperLayer b1, int count)
        {
            int ia0 = PcbLayers.StackIndex(a0, count), ia1 = PcbLayers.StackIndex(a1, count);
            int ib0 = PcbLayers.StackIndex(b0, count), ib1 = PcbLayers.StackIndex(b1, count);
            return Math.Max(Math.Min(ia0, ia1), Math.Min(ib0, ib1)) <= Math.Min(Math.Max(ia0, ia1), Math.Max(ib0, ib1));
        }

        private static bool BoxesNear(Track a, Track b, double need)
        {
            double grow = (a.Width + b.Width) / 2 + need;
            return Math.Min(a.X1, a.X2) - grow <= Math.Max(b.X1, b.X2) && Math.Max(a.X1, a.X2) + grow >= Math.Min(b.X1, b.X2) &&
                   Math.Min(a.Y1, a.Y2) - grow <= Math.Max(b.Y1, b.Y2) && Math.Max(a.Y1, a.Y2) + grow >= Math.Min(b.Y1, b.Y2);
        }

        /// <summary>Distance from a segment to a pad's real outline (rounded corners and circles included).</summary>
        public static double PadSegmentDistance(PlacedPad p, double x1, double y1, double x2, double y2)
        {
            double r = Math.Min(p.CornerRadius, Math.Min(p.Width, p.Height) / 2);
            if (p.Pad.Shape == PadShape.Circle)
            {
                double rr = Math.Max(p.Width, p.Height) / 2;
                return Geometry.PointSegmentDistance(p.X, p.Y, x1, y1, x2, y2) - rr;
            }

            return Geometry.SegmentRectDistance(x1, y1, x2, y2, p.X, p.Y, p.Width / 2 - r, p.Height / 2 - r) - r;
        }

        public static double PadPadDistance(PlacedPad a, PlacedPad b)
        {
            double ra = a.Pad.Shape == PadShape.Circle ? Math.Max(a.Width, a.Height) / 2 : Math.Min(a.CornerRadius, Math.Min(a.Width, a.Height) / 2);
            double rb = b.Pad.Shape == PadShape.Circle ? Math.Max(b.Width, b.Height) / 2 : Math.Min(b.CornerRadius, Math.Min(b.Width, b.Height) / 2);
            double ahx = a.Pad.Shape == PadShape.Circle ? 0 : a.Width / 2 - ra, ahy = a.Pad.Shape == PadShape.Circle ? 0 : a.Height / 2 - ra;
            double bhx = b.Pad.Shape == PadShape.Circle ? 0 : b.Width / 2 - rb, bhy = b.Pad.Shape == PadShape.Circle ? 0 : b.Height / 2 - rb;
            return Geometry.RectRectDistance(a.X, a.Y, ahx, ahy, b.X, b.Y, bhx, bhy) - ra - rb;
        }

        /// <summary>Silkscreen strokes (footprints and board texts) that run over exposed pads, one issue per part.</summary>
        public static List<(string Message, double X, double Y)> SilkOverPads(BoardDocument board, List<PlacedPad>? pads = null)
        {
            pads ??= board.Pads().ToList();
            var result = new List<(string, double, double)>();
            double half = board.Rules.SilkWidth / 2;
            foreach (var comp in board.Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                var side = comp.Side;
                var strokes = Silk.FootprintStrokes(comp, fp).Select(s => (s.X1, s.Y1, s.X2, s.Y2)).ToList();
                if (strokes.Count == 0) continue;
                int hits = 0;
                double hx = 0, hy = 0;
                foreach (var p in pads)
                {
                    if (!(p.Pad.IsThroughHole || p.SmdLayer == side)) continue;
                    foreach (var s in strokes)
                    {
                        if (PadSegmentDistance(p, s.X1, s.Y1, s.X2, s.Y2) < half - 1e-6)
                        {
                            if (hits == 0)
                            {
                                hx = p.X;
                                hy = p.Y;
                            }

                            hits++;
                            break;
                        }
                    }
                }

                if (hits > 0) result.Add(($"{comp.RefDes} serigrafisi {hits} pedin üstünden geçiyor (Gerber'de kırpılır)", hx, hy));
            }

            foreach (var g in board.Graphics)   // [graphics] a drawn shape on the legend over a pad is clipped as well
            {
                if (g.Layer != GraphicLayer.SilkTop && g.Layer != GraphicLayer.SilkBottom) continue;
                var side = g.Layer == GraphicLayer.SilkTop ? CopperLayer.Top : CopperLayer.Bottom;
                var strokes = Silk.GraphicStrokes(g);
                foreach (var p in pads)
                {
                    if (!(p.Pad.IsThroughHole || p.SmdLayer == side)) continue;
                    if (strokes.Any(s => PadSegmentDistance(p, s.X1, s.Y1, s.X2, s.Y2) < g.Width / 2))
                    {
                        result.Add(($"Serigrafi çizimi ({g.Describe()}) {p.Component.RefDes}.{p.Pad.Name} pedinin üstünde", p.X, p.Y));
                        break;
                    }
                }
            }

            foreach (var text in board.Texts)
            {
                if (text.Layer != GraphicLayer.SilkTop && text.Layer != GraphicLayer.SilkBottom) continue;
                var side = text.Layer == GraphicLayer.SilkTop ? CopperLayer.Top : CopperLayer.Bottom;
                var strokes = Silk.TextStrokes(text);
                foreach (var p in pads)
                {
                    if (!(p.Pad.IsThroughHole || p.SmdLayer == side)) continue;
                    if (strokes.Any(s => PadSegmentDistance(p, s.X1, s.Y1, s.X2, s.Y2) < text.Thickness / 2))
                    {
                        result.Add(($"'{text.Text}' yazısı {p.Component.RefDes}.{p.Pad.Name} pedinin üstünde", p.X, p.Y));
                        break;
                    }
                }
            }

            return result;
        }

        private static bool SameNet(string a, string b) =>
            !string.IsNullOrEmpty(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static DrcViolation Clearance(string rule, string a, string b, double d, double need, double x, double y, CopperLayer layer) =>
            new DrcViolation(DrcSeverity.Error, rule, $"{a} ↔ {b}: {Math.Max(0, d):0.###} mm (en az {need:0.###} mm) · {PcbLayers.DisplayName(layer)}", x, y, layer);

        /// <summary>Nets whose pads are not all joined by copper (zone fills count).</summary>
        public static List<(string Net, double X, double Y)> UnroutedNets(BoardDocument board) => BoardConnectivity.Build(board).Unrouted();
    }
}
