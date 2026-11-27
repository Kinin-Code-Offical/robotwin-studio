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
        Polygon,

        /// <summary>[bezier] A cubic Bézier curve (KiCad's gr_curve): start, two control points, end.</summary>
        Bezier,

        /// <summary>[image] A reference picture between two corners (KiCad's image): seen while laying out, not made.</summary>
        Image,

        /// <summary>[table] KiCad's table: cells of text in rows and columns, ruled, from its top-left corner.</summary>
        Table
    }

    /// <summary>
    /// [graphics] A drawn shape on a silkscreen, fab or drawing layer (KiCad's gr_line / gr_rect / gr_circle / gr_arc /
    /// gr_poly): logos, keep-clear outlines, assembly marks, notes' frames. Its points, by shape:
    /// line: start, end; rectangle: two opposite corners; circle: centre, a point on it; arc: centre, start, end (drawn
    /// counter-clockwise from start to end, in the board's y-down frame clockwise on screen); polygon: its corners;
    /// Bézier: start, two control points, end.
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

        /// <summary>[image] The picture, base64 PNG (images only).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ImageData { get; set; }

        /// <summary>[table] The columns' widths (mm), left to right (tables only).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<double>? ColumnWidths { get; set; }

        /// <summary>[table] The rows' heights (mm), top to bottom (tables only).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<double>? RowHeights { get; set; }

        /// <summary>[table] The cells' text, row by row ("\n" breaks a line) (tables only).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Cells { get; set; }

        /// <summary>[table] The cells' text height (mm); 0: 1.2 mm.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double TextSize { get; set; }

        [JsonIgnore]
        public int Columns => ColumnWidths?.Count ?? 0;

        [JsonIgnore]
        public int Rows => RowHeights?.Count ?? 0;

        [JsonIgnore]
        public double CellTextSize => TextSize > 0 ? TextSize : 1.2;

        /// <summary>[table] A table of the given rows and columns, every cell the same size, from its top-left corner.</summary>
        public static BoardGraphic Table(double x, double y, int rows, int columns, double cellWidth = 12, double cellHeight = 3, GraphicLayer layer = GraphicLayer.SilkTop)
        {
            var t = new BoardGraphic
            {
                Shape = GraphicShape.Table, Layer = layer, Width = 0.15,
                ColumnWidths = Enumerable.Repeat(cellWidth, Math.Max(1, columns)).ToList(),
                RowHeights = Enumerable.Repeat(cellHeight, Math.Max(1, rows)).ToList(),
                Cells = Enumerable.Repeat(string.Empty, Math.Max(1, rows) * Math.Max(1, columns)).ToList(),
                Points = { new PcbPoint(x, y) }
            };
            t.FitTable();
            return t;
        }

        /// <summary>[table] The text of a cell (empty past the list).</summary>
        public string Cell(int row, int column) => RobotTwin.CoreSim.Design.TableGrid.Cell(Cells, Columns, row, column);

        public void SetCell(int row, int column, string text) => RobotTwin.CoreSim.Design.TableGrid.SetCell(Cells ??= new List<string>(), Columns, row, column, text);

        /// <summary>[table] Sets the table's far corner from its columns and rows (after a size or a row or column changes).</summary>
        public void FitTable()
        {
            if (Shape != GraphicShape.Table || Points.Count == 0 || ColumnWidths == null || RowHeights == null) return;
            var far = new PcbPoint(Points[0].X + ColumnWidths.Sum(), Points[0].Y + RowHeights.Sum());
            if (Points.Count < 2) Points.Add(far);
            else Points[1] = far;
            Cells ??= new List<string>();
            int n = Rows * Columns;
            while (Cells.Count < n) Cells.Add(string.Empty);
            if (Cells.Count > n) Cells.RemoveRange(n, Cells.Count - n);
        }

        /// <summary>[table] Adds or takes away a row or a column, keeping the other cells' text where it was.</summary>
        public void ResizeTable(int rows, int columns)
        {
            (ColumnWidths, RowHeights, Cells) = RobotTwin.CoreSim.Design.TableGrid.Resize(ColumnWidths ?? new List<double>(), RowHeights ?? new List<double>(), Cells, rows, columns, 12, 3);
            FitTable();
        }

        /// <summary>[table] The margin inside a cell (mm).</summary>
        public double CellMargin => CellTextSize * 0.4;

        /// <summary>[table] The rules and the cells' text as strokes, mirrored on a bottom layer as KiCad mirrors them.</summary>
        private void TableStrokes(List<Segment2> s)
        {
            if (ColumnWidths == null || RowHeights == null || Points.Count == 0) return;
            double x0 = Points[0].X, y0 = Points[0].Y, w = ColumnWidths.Sum(), h = RowHeights.Sum();
            var mine = new List<Segment2>();
            foreach (var (a, b) in Edges(new List<PcbPoint> { new PcbPoint(x0, y0), new PcbPoint(x0 + w, y0), new PcbPoint(x0 + w, y0 + h), new PcbPoint(x0, y0 + h) }))
                mine.Add(new Segment2(a.X, a.Y, b.X, b.Y));
            double x = x0;
            for (int c = 0; c + 1 < ColumnWidths.Count; c++)
            {
                x += ColumnWidths[c];
                mine.Add(new Segment2(x, y0, x, y0 + h));
            }

            double y = y0;
            for (int r = 0; r + 1 < RowHeights.Count; r++)
            {
                y += RowHeights[r];
                mine.Add(new Segment2(x0, y, x0 + w, y));
            }

            double size = CellTextSize, m = CellMargin, pitch = size * 1.6;
            y = y0;
            for (int r = 0; r < RowHeights.Count; r++)
            {
                x = x0;
                for (int c = 0; c < ColumnWidths.Count; c++)
                {
                    var lines = Cell(r, c).Replace("\r", string.Empty).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        double lw = StrokeFont.Measure(lines[i], size);
                        StrokeFont.DrawCentered((x1, y1, x2, y2, _) => mine.Add(new Segment2(x1, y1, x2, y2)), lines[i], x + m + lw / 2, y + m + size / 2 + i * pitch, size, Width, 0, false);
                    }

                    x += ColumnWidths[c];
                }

                y += RowHeights[r];
            }

            bool mirrored = Layer is GraphicLayer.SilkBottom or GraphicLayer.FabBottom;
            double axis = 2 * x0 + w;
            foreach (var seg in mine) s.Add(mirrored ? new Segment2(axis - seg.X1, seg.Y1, axis - seg.X2, seg.Y2) : seg);
        }

        /// <summary>Whether the points are enough for the shape.</summary>
        public bool IsComplete => Shape switch
        {
            GraphicShape.Arc => Points.Count >= 3,
            GraphicShape.Polygon => Points.Count >= 3,
            GraphicShape.Bezier => Points.Count >= 4,
            GraphicShape.Image => Points.Count >= 2 && ImageData != null,
            GraphicShape.Table => Points.Count >= 2 && Columns > 0 && Rows > 0,
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
            if (!IsComplete || Shape == GraphicShape.Image) return s;   // [image] a picture has no strokes: nothing goes to the legend
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

                case GraphicShape.Table:
                    TableStrokes(s);
                    break;
                case GraphicShape.Bezier:
                {
                    var pts = RobotTwin.CoreSim.Design.CubicBezier.Flatten(p[0].X, p[0].Y, p[1].X, p[1].Y, p[2].X, p[2].Y, p[3].X, p[3].Y, tolerance);
                    for (int i = 0; i + 1 < pts.Count; i++) s.Add(new Segment2(pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y));
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
            if (Shape is GraphicShape.Image or GraphicShape.Table)
            {
                // [image] [table] picked anywhere on it
                double dx = Math.Max(Math.Min(Points[0].X, Points[1].X) - x, Math.Max(0, x - Math.Max(Points[0].X, Points[1].X)));
                double dy = Math.Max(Math.Min(Points[0].Y, Points[1].Y) - y, Math.Max(0, y - Math.Max(Points[0].Y, Points[1].Y)));
                return Math.Sqrt(Math.Max(0, dx) * Math.Max(0, dx) + Math.Max(0, dy) * Math.Max(0, dy));
            }

            if (Filled && Area().Count >= 3 && Inside(Area(), x, y)) return 0;
            double best = double.PositiveInfinity;
            foreach (var seg in Strokes(0.05)) best = Math.Min(best, Geometry.PointSegmentDistance(x, y, seg.X1, seg.Y1, seg.X2, seg.Y2));
            return Math.Max(0, best - Width / 2);
        }

        public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
        {
            if (Shape is GraphicShape.Image or GraphicShape.Table && Points.Count >= 2)
                return (Math.Min(Points[0].X, Points[1].X), Math.Min(Points[0].Y, Points[1].Y), Math.Max(Points[0].X, Points[1].X), Math.Max(Points[0].Y, Points[1].Y));
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
                GraphicShape.Table => $"tablo {Rows} × {Columns}",
                GraphicShape.Image => $"resim {Math.Abs(Points[1].X - Points[0].X):0.##} × {Math.Abs(Points[1].Y - Points[0].Y):0.##} mm",
                GraphicShape.Bezier => $"eğri {Strokes(0.05).Sum(q => Math.Sqrt((q.X2 - q.X1) * (q.X2 - q.X1) + (q.Y2 - q.Y1) * (q.Y2 - q.Y1))):0.##} mm",
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
