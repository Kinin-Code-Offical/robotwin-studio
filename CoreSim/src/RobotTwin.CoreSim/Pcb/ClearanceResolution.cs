using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [clearance-res] KiCad's Inspect → Clearance Resolution: for two items on the board (tracks, vias, a part's pads,
    /// pours), which rule sets the clearance between them, from which net class (or a pour's own clearance), and how
    /// far apart their copper actually is on each layer they share.
    /// </summary>
    public sealed class ClearanceReport
    {
        public string NetA { get; set; } = string.Empty;
        public string NetB { get; set; } = string.Empty;
        public string ClassA { get; set; } = string.Empty;
        public string ClassB { get; set; } = string.Empty;
        public double ClearanceA { get; set; }
        public double ClearanceB { get; set; }

        /// <summary>A pour's own clearance, when one of the two is a pour and it asks for more than the classes.</summary>
        public double ZoneClearance { get; set; }

        public double Required { get; set; }
        public bool SameNet { get; set; }

        /// <summary>Copper-to-copper distance on every copper layer both have copper on (0 when they touch).</summary>
        public List<(CopperLayer Layer, double Actual)> Layers { get; set; } = new List<(CopperLayer, double)>();

        /// <summary>Where the required clearance comes from, in words (Turkish, the interface's source language).</summary>
        public string Source { get; set; } = string.Empty;

        public bool Ok => SameNet || Layers.All(l => l.Actual >= Required - 1e-9);
    }

    public static class ClearanceResolution
    {
        private sealed class Piece
        {
            public CopperLayer Layer;
            public string Net = string.Empty;
            public double X1, Y1, X2, Y2, Half;   // a segment (a point when both ends meet) of a half-width
            public IReadOnlyList<PcbPoint>? Poly;   // or a polygon
        }

        /// <summary>The report for two board items (Track, Via, BoardComponent, Zone); null when one carries no copper.</summary>
        public static ClearanceReport? Between(BoardDocument board, object a, object b)
        {
            var pa = Pieces(board, a);
            var pb = Pieces(board, b);
            if (pa.Count == 0 || pb.Count == 0) return null;
            // a part: the pad nearest the other item stands for it (KiCad asks for two pads or items)
            (Piece A, Piece B, double D)? best = null;
            foreach (var x in pa)
            foreach (var y in pb)
            {
                if (x.Layer != y.Layer) continue;
                if (!string.IsNullOrEmpty(x.Net) && string.Equals(x.Net, y.Net, StringComparison.Ordinal) && (a is BoardComponent || b is BoardComponent) && pa.Concat(pb).Select(p => p.Net).Distinct().Count() > 1) continue;
                double d = Distance(x, y);
                if (best == null || d < best.Value.D) best = (x, y, d);
            }

            var first = best?.A ?? pa[0];
            var second = best?.B ?? pb[0];
            var ca = board.ClassFor(first.Net);
            var cb = board.ClassFor(second.Net);
            var r = new ClearanceReport
            {
                NetA = first.Net,
                NetB = second.Net,
                ClassA = ca.Name,
                ClassB = cb.Name,
                ClearanceA = ca.Clearance,
                ClearanceB = cb.Clearance,
                SameNet = !string.IsNullOrEmpty(first.Net) && string.Equals(first.Net, second.Net, StringComparison.Ordinal)
            };
            r.Required = board.ClearanceBetween(first.Net, second.Net);
            r.ZoneClearance = Math.Max(a is Zone za ? za.Clearance : 0, b is Zone zb ? zb.Clearance : 0);
            string Mm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            r.Source = r.SameNet
                ? "Aynı ağ: aralık kuralı yok"
                : ca.Clearance >= cb.Clearance
                    ? $"{ca.Name} sınıfı ({first.Net}) {Mm(ca.Clearance)} mm ≥ {cb.Name} sınıfı ({second.Net}) {Mm(cb.Clearance)} mm"
                    : $"{cb.Name} sınıfı ({second.Net}) {Mm(cb.Clearance)} mm > {ca.Name} sınıfı ({first.Net}) {Mm(ca.Clearance)} mm";
            if (!r.SameNet && r.ZoneClearance > r.Required)
            {
                r.Required = r.ZoneClearance;
                r.Source += $"; dolgunun kendi aralığı {Mm(r.ZoneClearance)} mm daha büyük";
            }

            foreach (var layer in pa.Select(p => p.Layer).Intersect(pb.Select(p => p.Layer)).Distinct())
            {
                double d = double.MaxValue;
                foreach (var x in pa.Where(p => p.Layer == layer && (best == null || p.Net == first.Net)))
                foreach (var y in pb.Where(p => p.Layer == layer && (best == null || p.Net == second.Net)))
                    d = Math.Min(d, Distance(x, y));
                if (d < double.MaxValue) r.Layers.Add((layer, Math.Max(0, d)));
            }

            return r;
        }

        private static List<Piece> Pieces(BoardDocument board, object item)
        {
            var list = new List<Piece>();
            int layers = board.LayerCount;
            switch (item)
            {
                case Track t:
                    list.Add(new Piece { Layer = t.Layer, Net = t.Net, X1 = t.X1, Y1 = t.Y1, X2 = t.X2, Y2 = t.Y2, Half = t.Width / 2 });
                    break;
                case Via v:
                    foreach (var l in PcbLayers.Stack(layers).Where(l => v.Spans(l, layers)))
                        list.Add(new Piece { Layer = l, Net = v.Net, X1 = v.X, Y1 = v.Y, X2 = v.X, Y2 = v.Y, Half = v.Diameter / 2 });
                    break;
                case BoardComponent c:
                    foreach (var p in board.Pads().Where(p => ReferenceEquals(p.Component, c)))
                    foreach (var l in PcbLayers.Stack(layers).Where(p.OnLayer))
                        list.Add(new Piece { Layer = l, Net = p.Net, Poly = Shapes.Rect(p.X - p.Width / 2, p.Y - p.Height / 2, p.X + p.Width / 2, p.Y + p.Height / 2) });
                    break;
                case Zone z when z.IsFilled:
                    foreach (var region in z.FilledRegions)
                    foreach (var contour in region.Contours())
                        list.Add(new Piece { Layer = z.Layer, Net = z.Net, Poly = contour });
                    break;
            }

            return list;
        }

        private static double Distance(Piece a, Piece b)
        {
            if (a.Poly != null && b.Poly != null) return CopperDistance.PolygonToPolygon(a.Poly, b.Poly);
            if (a.Poly != null) return CopperDistance.SegmentToPolygon(b.X1, b.Y1, b.X2, b.Y2, a.Poly) - b.Half;
            if (b.Poly != null) return CopperDistance.SegmentToPolygon(a.X1, a.Y1, a.X2, a.Y2, b.Poly) - a.Half;
            return CopperDistance.SegmentToSegment(a.X1, a.Y1, a.X2, a.Y2, b.X1, b.Y1, b.X2, b.Y2) - a.Half - b.Half;
        }
    }

    /// <summary>Distances between copper shapes: segments (or points) and polygons, 0 when they touch or overlap.</summary>
    internal static class CopperDistance
    {
        public static double SegmentToPolygon(double x1, double y1, double x2, double y2, IReadOnlyList<PcbPoint> poly)
        {
            if (Polygon.Winding(poly, x1, y1) != 0 || Polygon.Winding(poly, x2, y2) != 0) return 0;
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var c = poly[(i + 1) % poly.Count];
                best = Math.Min(best, SegmentToSegment(x1, y1, x2, y2, a.X, a.Y, c.X, c.Y));
            }

            return best;
        }

        public static double PolygonToPolygon(IReadOnlyList<PcbPoint> a, IReadOnlyList<PcbPoint> b)
        {
            double best = double.MaxValue;
            for (int i = 0; i < a.Count; i++)
            {
                var p = a[i];
                var q = a[(i + 1) % a.Count];
                best = Math.Min(best, SegmentToPolygon(p.X, p.Y, q.X, q.Y, b));
                if (best <= 0) return 0;
            }

            return Polygon.Winding(a, b[0].X, b[0].Y) != 0 ? 0 : best;
        }

        public static double SegmentToSegment(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            if (Cross(ax, ay, bx, by, cx, cy) * Cross(ax, ay, bx, by, dx, dy) < 0 && Cross(cx, cy, dx, dy, ax, ay) * Cross(cx, cy, dx, dy, bx, by) < 0) return 0;
            return Math.Min(Math.Min(PointToSegment(ax, ay, cx, cy, dx, dy), PointToSegment(bx, by, cx, cy, dx, dy)),
                Math.Min(PointToSegment(cx, cy, ax, ay, bx, by), PointToSegment(dx, dy, ax, ay, bx, by)));
        }

        private static double Cross(double ax, double ay, double bx, double by, double px, double py) => (bx - ax) * (py - ay) - (by - ay) * (px - ax);

        public static double PointToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
            double u = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * vx + (py - ay) * vy) / l2));
            double ex = px - (ax + u * vx), ey = py - (ay + u * vy);
            return Math.Sqrt(ex * ex + ey * ey);
        }
    }
}
