using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[graphics] The shapes KiCad's drawing tools make on a graphic layer.</summary>
    public enum GraphicShape
    {
        Line,
        Rectangle,
        Circle,
        Arc,
        Polygon
    }

    /// <summary>
    /// [graphics] A drawn shape on a silkscreen, fab or drawing layer (KiCad's gr_line / gr_rect / gr_circle / gr_arc /
    /// gr_poly): logos, keep-clear outlines, assembly marks, notes' frames. Its points, by shape:
    /// line: start, end; rectangle: two opposite corners; circle: centre, a point on it; arc: centre, start, end (drawn
    /// counter-clockwise from start to end, in the board's y-down frame clockwise on screen); polygon: its corners.
    /// </summary>
    public sealed class BoardGraphic
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        public GraphicShape Shape { get; set; }
        public GraphicLayer Layer { get; set; } = GraphicLayer.SilkTop;
        public List<PcbPoint> Points { get; set; } = new List<PcbPoint>();

        /// <summary>Line width (mm).</summary>
        public double Width { get; set; } = 0.15;

        /// <summary>A rectangle, circle or polygon filled in (a solid logo or marking).</summary>
        public bool Filled { get; set; }

        /// <summary>Whether the points are enough for the shape.</summary>
        public bool IsComplete => Shape switch
        {
            GraphicShape.Arc => Points.Count >= 3,
            GraphicShape.Polygon => Points.Count >= 3,
            _ => Points.Count >= 2
        };

        public double Radius => Points.Count >= 2 ? Dist(Points[0], Points[1]) : 0;

        /// <summary>
        /// The shape's centre line as straight strokes (curves cut so no point strays more than <paramref name="tolerance"/>
        /// mm from the true curve): what the Gerber writer, the DRC and the 3D view draw.
        /// </summary>
        public List<Segment2> Strokes(double tolerance = 0.02)
        {
            var s = new List<Segment2>();
            if (!IsComplete) return s;
            var p = Points;
            switch (Shape)
            {
                case GraphicShape.Line:
                    s.Add(new Segment2(p[0].X, p[0].Y, p[1].X, p[1].Y));
                    break;
                case GraphicShape.Rectangle:
                    foreach (var (a, b) in Edges(Corners())) s.Add(new Segment2(a.X, a.Y, b.X, b.Y));
                    break;
                case GraphicShape.Polygon:
                    foreach (var (a, b) in Edges(p)) s.Add(new Segment2(a.X, a.Y, b.X, b.Y));
                    break;
                case GraphicShape.Circle:
                    Curve(s, p[0], Radius, 0, 2 * Math.PI, tolerance);
                    break;
                case GraphicShape.Arc:
                {
                    var (a0, sweep) = ArcAngles();
                    Curve(s, p[0], Radius, a0, sweep, tolerance);
                    break;
                }
            }

            return s;
        }

        /// <summary>The outline as a closed polygon (rectangle, circle, polygon), for filling; empty for open shapes.</summary>
        public List<PcbPoint> Area(double tolerance = 0.02)
        {
            if (!IsComplete) return new List<PcbPoint>();
            switch (Shape)
            {
                case GraphicShape.Rectangle:
                    return Corners();
                case GraphicShape.Polygon:
                    return Points.ToList();
                case GraphicShape.Circle:
                    return Strokes(tolerance).Select(seg => new PcbPoint(seg.X1, seg.Y1)).ToList();
                default:
                    return new List<PcbPoint>();
            }
        }

        public List<PcbPoint> Corners()
        {
            var a = Points[0];
            var b = Points[1];
            return new List<PcbPoint> { a, new PcbPoint(b.X, a.Y), b, new PcbPoint(a.X, b.Y) };
        }

        /// <summary>The arc's start angle and its sweep (radians, positive counter-clockwise in maths axes).</summary>
        public (double Start, double Sweep) ArcAngles()
        {
            var c = Points[0];
            double a0 = Math.Atan2(Points[1].Y - c.Y, Points[1].X - c.X);
            double a1 = Math.Atan2(Points[2].Y - c.Y, Points[2].X - c.X);
            double sweep = a1 - a0;
            while (sweep <= 0) sweep += 2 * Math.PI;
            return (a0, sweep);
        }

        /// <summary>Moves the shape by (dx, dy).</summary>
        public void Translate(double dx, double dy)
        {
            for (int i = 0; i < Points.Count; i++) Points[i] = new PcbPoint(Points[i].X + dx, Points[i].Y + dy);
        }

        /// <summary>Distance from a point to the shape's line (0 inside a filled shape).</summary>
        public double DistanceTo(double x, double y)
        {
            if (!IsComplete) return double.PositiveInfinity;
            if (Filled && Area().Count >= 3 && Inside(Area(), x, y)) return 0;
            double best = double.PositiveInfinity;
            foreach (var seg in Strokes(0.05)) best = Math.Min(best, Geometry.PointSegmentDistance(x, y, seg.X1, seg.Y1, seg.X2, seg.Y2));
            return Math.Max(0, best - Width / 2);
        }

        public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
        {
            var strokes = Strokes(0.05);
            if (strokes.Count == 0) return (0, 0, 0, 0);
            double h = Width / 2;
            return (strokes.Min(s => Math.Min(s.X1, s.X2)) - h, strokes.Min(s => Math.Min(s.Y1, s.Y2)) - h,
                    strokes.Max(s => Math.Max(s.X1, s.X2)) + h, strokes.Max(s => Math.Max(s.Y1, s.Y2)) + h);
        }

        /// <summary>A short description ("dikdörtgen 10 × 4 mm · Üst serigrafi") for lists and the undo history.</summary>
        public string Describe()
        {
            string what = Shape switch
            {
                GraphicShape.Line => $"çizgi {Dist(Points[0], Points[1]):0.##} mm",
                GraphicShape.Rectangle => $"dikdörtgen {Math.Abs(Points[1].X - Points[0].X):0.##} × {Math.Abs(Points[1].Y - Points[0].Y):0.##} mm",
                GraphicShape.Circle => $"daire Ø{2 * Radius:0.##} mm",
                GraphicShape.Arc => $"yay R{Radius:0.##} mm, {ArcAngles().Sweep * 180 / Math.PI:0}°",
                _ => $"çokgen, {Points.Count} köşe"
            };
            return what;
        }

        private static IEnumerable<(PcbPoint A, PcbPoint B)> Edges(IReadOnlyList<PcbPoint> pts)
        {
            for (int i = 0; i < pts.Count; i++) yield return (pts[i], pts[(i + 1) % pts.Count]);
        }

        private static void Curve(List<Segment2> s, PcbPoint c, double r, double a0, double sweep, double tolerance)
        {
            if (r <= 0) return;
            // a chord of angle θ strays r(1 − cos θ/2) from the arc
            double step = 2 * Math.Acos(Math.Max(-1, Math.Min(1, 1 - Math.Min(tolerance, r) / r)));
            int n = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / Math.Max(step, 1e-3)));
            double px = c.X + r * Math.Cos(a0), py = c.Y + r * Math.Sin(a0);
            for (int i = 1; i <= n; i++)
            {
                double a = a0 + sweep * i / n;
                double x = c.X + r * Math.Cos(a), y = c.Y + r * Math.Sin(a);
                s.Add(new Segment2(px, py, x, y));
                px = x;
                py = y;
            }
        }

        private static bool Inside(List<PcbPoint> poly, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Y > y) != (poly[j].Y > y) && x < (poly[j].X - poly[i].X) * (y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
            }

            return inside;
        }

        private static double Dist(PcbPoint a, PcbPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
