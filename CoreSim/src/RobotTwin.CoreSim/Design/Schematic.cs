using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Design
{
    public struct GridPoint : IEquatable<GridPoint>
    {
        public GridPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Points closer than this (grid units) are the same electrical point.</summary>
        public const double Tolerance = 1e-3;

        public bool Equals(GridPoint other) => Math.Abs(X - other.X) < Tolerance && Math.Abs(Y - other.Y) < Tolerance;

        public override bool Equals(object? obj) => obj is GridPoint p && Equals(p);

        public override int GetHashCode() =>
            unchecked((int)Math.Round(X * 100) * 397 ^ (int)Math.Round(Y * 100));

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", X, Y);

        public static GridPoint operator +(GridPoint a, GridPoint b) => new GridPoint(a.X + b.X, a.Y + b.Y);
        public static GridPoint operator -(GridPoint a, GridPoint b) => new GridPoint(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>A part placed on the schematic sheet.</summary>
    public sealed class SchematicPart
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>[sheets] The sheet it is on (<see cref="SchematicSheet.Uid"/>); null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        /// <summary>
        /// [integrity] The part's identity for its whole life: it stays when the designator changes, so the PCB and the
        /// Montaj follow a renamed part instead of dropping it and adding a new one. Empty in files saved before it existed
        /// (<see cref="Schematic.EnsurePartUids"/> gives one); a copy gets its own.
        /// </summary>
        public string Uid { get; set; } = string.Empty;

        public string RefDes { get; set; } = string.Empty;
        public string PartId { get; set; } = string.Empty;
        public string? PresetId { get; set; }
        public GridPoint Position { get; set; }

        /// <summary>Rotation in degrees, multiple of 90, counter-clockwise on screen.</summary>
        public int Rotation { get; set; }
        public bool Mirror { get; set; }

        /// <summary>Parameter values that differ from the preset/definition.</summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Transforms a point from symbol coordinates to sheet coordinates.</summary>
        public GridPoint Transform(double x, double y)
        {
            if (Mirror) x = -x;
            int r = ((Rotation % 360) + 360) % 360;
            double tx, ty;
            switch (r)
            {
                case 90: tx = y; ty = -x; break;
                case 180: tx = -x; ty = -y; break;
                case 270: tx = -y; ty = x; break;
                default: tx = x; ty = y; break;
            }

            return new GridPoint(Position.X + tx, Position.Y + ty);
        }
    }

    public sealed class SchematicWire
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>[sheets] The sheet it is on (<see cref="SchematicSheet.Uid"/>); null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        public List<GridPoint> Points { get; set; } = new List<GridPoint>();
    }

    /// <summary>A named label attached to a point; every label with the same text is one net.</summary>
    public sealed class NetLabel
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>[sheets] The sheet it is on (<see cref="SchematicSheet.Uid"/>); null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        public string Name { get; set; } = string.Empty;
        public GridPoint Position { get; set; }

        /// <summary>
        /// [directive] KiCad's directive label (netclass flag): it names no net; the net its point is on gets this net
        /// class, and the board takes it when it is brought up to date. Null: an ordinary label.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NetClass { get; set; }

        [JsonIgnore]
        public bool IsDirective => !string.IsNullOrWhiteSpace(NetClass);

        /// <summary>
        /// Drawn as a global label (KiCad's flag shape). A sheet is a single page, so local and
        /// global labels connect the same way: every label with the same name is one net.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Global { get; set; }

        /// <summary>
        /// [sheets] KiCad's hierarchical label: inside a sub-sheet it joins the sheet symbol's pin of the same name on the
        /// parent sheet (and nothing else by its name).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Hierarchical { get; set; }

        /// <summary>[sheets] Which way a hierarchical or global label carries its signal (its outline).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public LabelShape Shape { get; set; }
    }

    /// <summary>A "no connection" flag (KiCad's X marker) on a pin that is deliberately left open.</summary>
    public sealed class NoConnectFlag
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>[sheets] The sheet it is on (<see cref="SchematicSheet.Uid"/>); null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        public GridPoint Position { get; set; }
    }

    /// <summary>
    /// [notes] Text on the sheet that is not a net: a title, a note to whoever builds it, a block's description
    /// (KiCad's text and text box). Its top left corner sits at <see cref="Position"/>; it may run over several lines.
    /// </summary>
    public sealed class SchematicNote
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>[sheets] The sheet it is on (<see cref="SchematicSheet.Uid"/>); null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        public string Text { get; set; } = string.Empty;
        public GridPoint Position { get; set; }

        /// <summary>Letter height in grid units (1 = the size of part names).</summary>
        public double Size { get; set; } = 1.0;

        /// <summary>A frame around it (KiCad's text box).</summary>
        public bool Boxed { get; set; }

        public bool Bold { get; set; }

        /// <summary>The area it takes, in grid units (as the sheet's font sets it: about 0.6 of the height per letter).</summary>
        public GridRect Bounds()
        {
            var lines = (Text.Length == 0 ? " " : Text).Replace("\r", string.Empty).Split('\n');
            double w = Math.Max(1, lines.Max(l => l.Length)) * 0.6 * Size + (Boxed ? 1 : 0);
            double h = lines.Length * 1.3 * Size + (Boxed ? 1 : 0);
            return new GridRect(Position.X, Position.Y, Position.X + w, Position.Y + h);
        }
    }

    /// <summary>[shapes] What a sheet drawing is (KiCad's schematic graphics).</summary>
    public enum SheetShapeKind
    {
        /// <summary>A polyline through its points.</summary>
        Line,

        /// <summary>A rectangle between two corners.</summary>
        Rectangle,

        /// <summary>A circle: its centre and a point on it.</summary>
        Circle,

        /// <summary>An arc through three points: start, a point on it, end.</summary>
        Arc,

        /// <summary>[bezier] A cubic Bézier curve: start, first control point, second control point, end.</summary>
        Bezier,

        /// <summary>[image] A picture (a logo, a photo of the wiring, a block diagram) between two corners; it stays upright.</summary>
        Image,

        /// <summary>[table] KiCad's table: ruled cells of text from its top-left corner; it stays upright.</summary>
        Table,

        /// <summary>[rule-area] KiCad's rule area on the sheet: every net inside it takes its net class (<see cref="SchematicShape.NetClass"/>).</summary>
        RuleArea
    }

    /// <summary>
    /// [shapes] A drawing on the sheet that is not a wire (KiCad's lines, rectangles, circles and arcs): a block's frame,
    /// a divider, a mechanical outline. It carries no net; it is selected, moved, turned, mirrored, copied and deleted
    /// like the rest of the sheet.
    /// </summary>
    public sealed class SchematicShape
    {
        /// <summary>[group] KiCad's group: items with the same name are picked and moved together; null when ungrouped.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Group { get; set; }

        /// <summary>[sheets] The sheet it is on (<see cref="SchematicSheet.Uid"/>); null: the root sheet.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Sheet { get; set; }

        public SheetShapeKind Kind { get; set; }

        /// <summary>Line: its points; rectangle: two corners; circle: centre, then a point on it; arc: start, middle, end; Bézier: start, two control points, end.</summary>
        public List<GridPoint> Points { get; set; } = new List<GridPoint>();

        /// <summary>Stroke width in millimetres (KiCad's default schematic line, 0.15 mm).</summary>
        public double Width { get; set; } = 0.15;

        /// <summary>Filled with the stroke's colour (rectangles and circles).</summary>
        public bool Filled { get; set; }

        /// <summary>A dashed stroke (KiCad's dash style).</summary>
        public bool Dashed { get; set; }

        /// <summary>[image] The picture, base64 PNG (images only).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ImageData { get; set; }

        /// <summary>[rule-area] The net class a rule area gives the nets inside it.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NetClass { get; set; }

        /// <summary>[table] The columns' widths and the rows' heights in grid steps (tables only).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<double>? ColumnWidths { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<double>? RowHeights { get; set; }

        /// <summary>[table] The cells' text, row by row ("\n" breaks a line).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? Cells { get; set; }

        /// <summary>[table] The cells' text height (mm); 0: KiCad's 1.27 mm.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double TextSize { get; set; }

        [JsonIgnore]
        public int Columns => ColumnWidths?.Count ?? 0;

        [JsonIgnore]
        public int Rows => RowHeights?.Count ?? 0;

        [JsonIgnore]
        public double CellTextSize => TextSize > 0 ? TextSize : 1.27;

        /// <summary>[table] A table of the given rows and columns, every cell the same size (grid steps), from its top-left corner.</summary>
        public static SchematicShape Table(GridPoint corner, int rows, int columns, double cellWidth = 6, double cellHeight = 1.5)
        {
            var t = new SchematicShape
            {
                Kind = SheetShapeKind.Table,
                ColumnWidths = Enumerable.Repeat(cellWidth, Math.Max(1, columns)).ToList(),
                RowHeights = Enumerable.Repeat(cellHeight, Math.Max(1, rows)).ToList(),
                Cells = Enumerable.Repeat(string.Empty, Math.Max(1, rows) * Math.Max(1, columns)).ToList(),
                Points = { corner }
            };
            t.FitTable();
            return t;
        }

        public string Cell(int row, int column) => TableGrid.Cell(Cells, Columns, row, column);

        public void SetCell(int row, int column, string text) => TableGrid.SetCell(Cells ??= new List<string>(), Columns, row, column, text);

        /// <summary>[table] Sets the far corner from the columns and rows, the cells to their count.</summary>
        public void FitTable()
        {
            if (Kind != SheetShapeKind.Table || Points.Count == 0 || ColumnWidths == null || RowHeights == null) return;
            var far = new GridPoint(Points[0].X + ColumnWidths.Sum(), Points[0].Y + RowHeights.Sum());
            if (Points.Count < 2) Points.Add(far);
            else Points[1] = far;
            Cells ??= new List<string>();
            int n = Rows * Columns;
            while (Cells.Count < n) Cells.Add(string.Empty);
            if (Cells.Count > n) Cells.RemoveRange(n, Cells.Count - n);
        }

        /// <summary>[table] Adds or takes away rows and columns, the other cells keeping their text.</summary>
        public void ResizeTable(int rows, int columns)
        {
            (ColumnWidths, RowHeights, Cells) = TableGrid.Resize(ColumnWidths ?? new List<double>(), RowHeights ?? new List<double>(), Cells, rows, columns, 6, 1.5);
            FitTable();
        }

        /// <summary>[image] The middle of the shape's first two points (an image's centre).</summary>
        [JsonIgnore]
        public GridPoint Centre => Points.Count >= 2 ? new GridPoint((Points[0].X + Points[1].X) / 2, (Points[0].Y + Points[1].Y) / 2) : Points.Count == 1 ? Points[0] : new GridPoint(0, 0);

        /// <summary>[image] Moves an image so its centre is there, its size and its upright kept.</summary>
        public void MoveCentreTo(GridPoint centre)
        {
            var c = Centre;
            for (int i = 0; i < Points.Count; i++) Points[i] = new GridPoint(Points[i].X + centre.X - c.X, Points[i].Y + centre.Y - c.Y);
        }

        /// <summary>The circle's radius in grid units.</summary>
        public double Radius => Points.Count >= 2 ? Math.Sqrt(Sq(Points[1].X - Points[0].X) + Sq(Points[1].Y - Points[0].Y)) : 0;

        private static double Sq(double v) => v * v;

        /// <summary>The arc's circle through its three points, or null when they are on one line.</summary>
        public (GridPoint Centre, double Radius)? ArcCircle()
        {
            if (Points.Count < 3) return null;
            var (a, b, c) = (Points[0], Points[1], Points[2]);
            double d = 2 * (a.X * (b.Y - c.Y) + b.X * (c.Y - a.Y) + c.X * (a.Y - b.Y));
            if (Math.Abs(d) < 1e-12) return null;
            double a2 = a.X * a.X + a.Y * a.Y, b2 = b.X * b.X + b.Y * b.Y, c2 = c.X * c.X + c.Y * c.Y;
            var o = new GridPoint((a2 * (b.Y - c.Y) + b2 * (c.Y - a.Y) + c2 * (a.Y - b.Y)) / d, (a2 * (c.X - b.X) + b2 * (a.X - c.X) + c2 * (b.X - a.X)) / d);
            return (o, Math.Sqrt(Sq(a.X - o.X) + Sq(a.Y - o.Y)));
        }

        /// <summary>Points along the shape (a closed outline for a rectangle or circle), for bounds, picking and export.</summary>
        public List<GridPoint> Outline(int segments = 48)
        {
            var pts = new List<GridPoint>();
            switch (Kind)
            {
                case SheetShapeKind.Rectangle or SheetShapeKind.Image or SheetShapeKind.Table or SheetShapeKind.RuleArea when Points.Count >= 2:
                    var (p, q) = (Points[0], Points[1]);
                    pts.AddRange(new[] { p, new GridPoint(q.X, p.Y), q, new GridPoint(p.X, q.Y), p });
                    break;
                case SheetShapeKind.Circle when Points.Count >= 2:
                    double r = Radius;
                    for (int k = 0; k <= segments; k++) pts.Add(new GridPoint(Points[0].X + r * Math.Cos(2 * Math.PI * k / segments), Points[0].Y + r * Math.Sin(2 * Math.PI * k / segments)));
                    break;
                case SheetShapeKind.Arc when ArcCircle() is { } circle:
                {
                    double A(GridPoint g) => Math.Atan2(g.Y - circle.Centre.Y, g.X - circle.Centre.X);
                    double a0 = A(Points[0]), am = A(Points[1]), a1 = A(Points[2]);
                    double sweep = a1 - a0, toMid = am - a0;
                    while (sweep <= -Math.PI) sweep += 2 * Math.PI;
                    while (sweep > Math.PI) sweep -= 2 * Math.PI;
                    while (toMid <= -Math.PI) toMid += 2 * Math.PI;
                    while (toMid > Math.PI) toMid -= 2 * Math.PI;
                    // the way round that passes the middle point
                    if (Math.Sign(toMid) != Math.Sign(sweep) || Math.Abs(toMid) > Math.Abs(sweep)) sweep = sweep > 0 ? sweep - 2 * Math.PI : sweep + 2 * Math.PI;
                    for (int k = 0; k <= segments; k++)
                    {
                        double t = a0 + sweep * k / segments;
                        pts.Add(new GridPoint(circle.Centre.X + circle.Radius * Math.Cos(t), circle.Centre.Y + circle.Radius * Math.Sin(t)));
                    }

                    break;
                }

                case SheetShapeKind.Bezier when Points.Count >= 4:
                {
                    var (a, b, c, d) = (Points[0], Points[1], Points[2], Points[3]);
                    foreach (var (x, y) in CubicBezier.Flatten(a.X, a.Y, b.X, b.Y, c.X, c.Y, d.X, d.Y, 0.02)) pts.Add(new GridPoint(x, y));
                    break;
                }

                default:
                    pts.AddRange(Points);
                    break;
            }

            return pts;
        }

        public GridRect Bounds()
        {
            var pts = Outline();
            if (pts.Count == 0) return new GridRect(0, 0, 0, 0);
            var r = GridRect.FromPoint(pts[0]);
            foreach (var p in pts) r = r.Union(p);
            return r;
        }
    }

    public sealed class NetConnection
    {
        public NetConnection(string refDes, string pin)
        {
            RefDes = refDes;
            Pin = pin;
        }

        public string RefDes { get; }
        public string Pin { get; }

        public override string ToString() => RefDes + "." + Pin;
    }

    public sealed class Net
    {
        public string Name { get; set; } = string.Empty;
        public List<NetConnection> Connections { get; } = new List<NetConnection>();
        public bool IsGround { get; set; }

        /// <summary>True when no label or power symbol named the net (the name is derived from a pin).</summary>
        public bool IsAutoNamed { get; set; }
    }

    public sealed class Netlist
    {
        public List<Net> Nets { get; } = new List<Net>();
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>Net name of each wire (index-aligned with <see cref="Schematic.Wires"/>); null if it touches nothing named.</summary>
        public List<string?> WireNets { get; } = new List<string?>();

        /// <summary>Net name of each label (index-aligned with <see cref="Schematic.Labels"/>).</summary>
        public List<string?> LabelNets { get; } = new List<string?>();

        /// <summary>Points where three or more connections meet (drawn as junction dots).</summary>
        public List<GridPoint> Junctions { get; } = new List<GridPoint>();

        /// <summary>[sheets] The sheet of each junction (index-aligned with <see cref="Junctions"/>; null: the root).</summary>
        public List<string?> JunctionSheets { get; } = new List<string?>();

        public Net? NetOf(string refDes, string pin) =>
            Nets.FirstOrDefault(n => n.Connections.Any(c =>
                string.Equals(c.RefDes, refDes, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(c.Pin, pin, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The schematic document: parts, wires and labels. Also the project file format.</summary>
    public sealed partial class Schematic
    {
        public const int FormatVersion = 1;

        public int Version { get; set; } = FormatVersion;
        public string Title { get; set; } = "Yeni Devre";

        /// <summary>
        /// [assembly] Stable id of the project (empty in files saved before it existed; see <see cref="EnsureProjectId"/>).
        /// Its Montaj and the parts and cables made from it carry it, so they keep their link when the title changes.
        /// </summary>
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Gives the project an id when it has none yet; returns it.</summary>
        public string EnsureProjectId()
        {
            if (string.IsNullOrEmpty(ProjectId)) ProjectId = Guid.NewGuid().ToString("N");
            return ProjectId;
        }
        public List<SchematicPart> Parts { get; set; } = new List<SchematicPart>();
        public List<SchematicWire> Wires { get; set; } = new List<SchematicWire>();
        public List<NetLabel> Labels { get; set; } = new List<NetLabel>();

        /// <summary>
        /// [directive] [rule-area] The net classes the sheet gives: first its rule areas' (every net with a wire, a pin or a
        /// label inside one), then its directive labels', which win (a later one on the same net over an earlier).
        /// </summary>
        public Dictionary<string, string> NetClassDirectives(Netlist netlist, PartLibrary? library = null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var area in Shapes.Where(s => s.Kind == SheetShapeKind.RuleArea && s.Points.Count >= 2 && !string.IsNullOrWhiteSpace(s.NetClass)))
            {
                var r = GridRect.FromCorners(area.Points[0], area.Points[1]);
                string cls = area.NetClass!.Trim();
                for (int i = 0; i < Wires.Count && i < netlist.WireNets.Count; i++)
                {
                    if (!SameSheet(Wires[i].Sheet, area.Sheet)) continue;   // [sheets]
                    var w = Wires[i].Points;
                    bool inside = w.Any(r.Contains) || Enumerable.Range(0, Math.Max(0, w.Count - 1)).Any(k => r.IntersectsSegment(w[k], w[k + 1]));
                    if (inside && netlist.WireNets[i] is { } wn) map[wn] = cls;
                }

                for (int i = 0; i < Labels.Count && i < netlist.LabelNets.Count; i++)
                    if (!Labels[i].IsDirective && SameSheet(Labels[i].Sheet, area.Sheet) && r.Contains(Labels[i].Position) && netlist.LabelNets[i] is { } ln)
                        map[ln] = cls;
                if (library != null)
                    foreach (var (part, pin, pos) in PinPositions(library))
                        if (SameSheet(part.Sheet, area.Sheet) && r.Contains(pos) && netlist.NetOf(part.RefDes, pin.Name) is { } pn)
                            map[pn.Name] = cls;
            }

            for (int i = 0; i < Labels.Count && i < netlist.LabelNets.Count; i++)
                if (Labels[i].IsDirective && netlist.LabelNets[i] is { } net)
                    map[net] = Labels[i].NetClass!.Trim();
            return map;
        }

        /// <summary>No-connect flags on pins that are intentionally unconnected (silences ERC).</summary>
        public List<NoConnectFlag> NoConnects { get; set; } = new List<NoConnectFlag>();

        /// <summary>[notes] Texts and text boxes on the sheet (no electrical meaning).</summary>
        public List<SchematicNote> Notes { get; set; } = new List<SchematicNote>();

        /// <summary>[shapes] Lines, rectangles, circles and arcs drawn on the sheet (KiCad's schematic graphics).</summary>
        public List<SchematicShape> Shapes { get; set; } = new List<SchematicShape>();

        /// <summary>Optional PCB layout saved alongside the schematic.</summary>
        public Pcb.BoardDocument? Board { get; set; }

        /// <summary>Mechanical build (robot / drone) the circuits are mounted on.</summary>
        public Mechanics.AssemblyDocument? Assembly { get; set; }

        /// <summary>Component tolerance setting for simulation (null = nominal values).</summary>
        public ToleranceSettings? Tolerances { get; set; }

        /// <summary>
        /// [variants] Alternatives of this design (<see cref="DesignVariant"/>): parts left off, values changed, parts swapped.
        /// </summary>
        public List<DesignVariant> Variants { get; set; } = new List<DesignVariant>();

        /// <summary>[bom] Where each BOM line is bought (Design/BomSourcing.cs), by the line's identity; and the currency its prices are in.</summary>
        public Dictionary<string, BomSource> Sourcing { get; set; } = new Dictionary<string, BomSource>(StringComparer.Ordinal);

        public string BomCurrency { get; set; } = "USD";

        /// <summary>[bom] How many boards the costing is for.</summary>
        public int BomBoards { get; set; } = 1;

        /// <summary>[erc-policy] The user's ERC severity per rule code; rules not here report as the checker says.</summary>
        public Dictionary<string, ErcRuleSeverity> ErcSeverities { get; set; } = new Dictionary<string, ErcRuleSeverity>(StringComparer.Ordinal);

        /// <summary>[erc-policy] ERC markers the user accepted (see <see cref="ErcPolicy"/>).</summary>
        public List<ErcExclusion> ErcExclusions { get; set; } = new List<ErcExclusion>();

        /// <summary>[variants] The alternative simulated and listed now, by name; null: the design as drawn.</summary>
        public string? ActiveVariant { get; set; }

        public SchematicPart? FindPart(string refDes) =>
            Parts.FirstOrDefault(p => string.Equals(p.RefDes, refDes, StringComparison.OrdinalIgnoreCase));

        /// <summary>Next free designator for a prefix ("R" -> "R3").</summary>
        public string NextRefDes(string prefix)
        {
            int max = 0;
            foreach (var part in Parts)
            {
                if (!part.RefDes.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(part.RefDes.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    max = Math.Max(max, n);
                }
            }

            return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        public SchematicPart AddPart(PartLibrary library, string presetOrPartId, GridPoint position, int rotation = 0)
        {
            var preset = library.FindPreset(presetOrPartId);
            var def = preset != null ? library.Get(preset.PartId) : library.Get(presetOrPartId);
            var part = new SchematicPart
            {
                Uid = NewPartUid(),
                RefDes = NextRefDes(def.RefPrefix),
                PartId = def.Id,
                PresetId = preset?.Id,
                Position = position,
                Rotation = rotation
            };
            Parts.Add(part);
            return part;
        }

        /// <summary>[integrity] A new part identity (<see cref="SchematicPart.Uid"/>).</summary>
        public static string NewPartUid() => Guid.NewGuid().ToString("N");

        /// <summary>
        /// [integrity] Gives every part an identity: one from a file saved before identities existed gets a new one, and of
        /// parts sharing one (a pasted or duplicated copy carries its original's) the first keeps it, the others get their own.
        /// </summary>
        public void EnsurePartUids()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in Parts)
            {
                if (!string.IsNullOrEmpty(p.Uid) && seen.Add(p.Uid)) continue;
                p.Uid = NewPartUid();
                seen.Add(p.Uid);
            }
        }

        public SchematicWire AddWire(params GridPoint[] points)
        {
            var wire = new SchematicWire { Points = points.ToList() };
            Wires.Add(wire);
            return wire;
        }

        public IEnumerable<(SchematicPart Part, PinDefinition Pin, GridPoint Position)> PinPositions(PartLibrary library)
        {
            foreach (var part in Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null) continue;
                foreach (var pin in def.Pins)
                {
                    yield return (part, pin, part.Transform(pin.X, pin.Y));
                }
            }
        }

        public Dictionary<string, string> ResolveParameters(PartLibrary library, SchematicPart part)
        {
            var def = library.Get(part.PartId);
            var preset = part.PresetId != null ? library.FindPreset(part.PresetId) : null;
            return library.ResolveParameters(def, preset, part.Parameters);
        }

        // ------------------------------------------------------------- connectivity

        /// <summary>
        /// Builds the netlist. Wire vertices, pin tips and labels that coincide are
        /// joined; a wire end lying on another wire segment forms a T-junction.
        /// </summary>
        public Netlist BuildNetlist(PartLibrary library)
        {
            // [sheets] Every point is a place on one sheet: wires, pins and labels meet only on the same sheet. Local labels
            // join on their own sheet; global labels and power symbols on every sheet; a sub-sheet's hierarchical labels join
            // the pins of its symbol on the parent sheet.
            var netlist = new Netlist();
            var uf = new UnionFind();
            var pointIds = new Dictionary<(string Sheet, GridPoint P), int>();
            static string Key(string? sheet) => sheet ?? string.Empty;

            int Id(string? sheet, GridPoint p)
            {
                var key = (Key(sheet), p);
                if (!pointIds.TryGetValue(key, out int id))
                {
                    id = uf.Add();
                    pointIds[key] = id;
                }

                return id;
            }

            var segments = new List<(string Sheet, GridPoint A, GridPoint B)>();
            foreach (var wire in Wires)
            {
                for (int i = 0; i + 1 < wire.Points.Count; i++)
                {
                    uf.Union(Id(wire.Sheet, wire.Points[i]), Id(wire.Sheet, wire.Points[i + 1]));
                    segments.Add((Key(wire.Sheet), wire.Points[i], wire.Points[i + 1]));
                }

                if (wire.Points.Count == 1) Id(wire.Sheet, wire.Points[0]);
            }

            var pins = PinPositions(library).ToList();
            foreach (var (part, _, pos) in pins) Id(part.Sheet, pos);
            foreach (var label in Labels) Id(label.Sheet, label.Position);
            var known = new HashSet<string>(Sheets.Select(x => x.Uid), StringComparer.Ordinal);
            foreach (var symbol in Sheets)
                foreach (var pin in symbol.Pins)
                    Id(symbol.Sheet, pin.Position);

            // T-junctions: any point that lies on the interior of a segment on its sheet joins it.
            var bySheet = segments.GroupBy(x => x.Sheet).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (var point in pointIds.Keys.ToList())
            {
                if (!bySheet.TryGetValue(point.Sheet, out var onSheet)) continue;
                foreach (var (_, a, b) in onSheet)
                {
                    if (OnSegment(point.P, a, b)) uf.Union(pointIds[point], pointIds[(point.Sheet, a)]);
                }
            }

            // Labels with the same name (per scope), ground/power symbols, sheet pins and internal part connections.
            var byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var namedRoots = new List<(int Id, string Name, bool Ground, int Rank)>();

            void Join(int id, string key)
            {
                if (byKey.TryGetValue(key, out int other)) uf.Union(id, other);
                else byKey[key] = id;
            }

            // A name's rank: global ones and power first, then the root's labels, then deeper sheets' (KiCad's drivers).
            void Name(int id, string name, bool ground, int rank) => namedRoots.Add((id, name, ground, rank));

            foreach (var label in Labels)
            {
                if (label.IsDirective || string.IsNullOrWhiteSpace(label.Name)) continue;   // [directive] it gives a class, not a name
                string name = label.Name.Trim();
                int id = pointIds[(Key(label.Sheet), label.Position)];
                bool sub = label.Sheet != null && known.Contains(label.Sheet);
                int depth = sub ? SheetDepth(label.Sheet) : 0;
                if (label.Hierarchical && sub)
                {
                    Name(id, SheetPath(label.Sheet) + name, IsGroundName(name), 100 + depth);   // joined through the sheet pin below
                }
                else if (label.Global || !sub)
                {
                    Join(id, ":" + name);   // the root's labels and global labels share one scope (a single sheet stays as it was)
                    Name(id, name, IsGroundName(name), 0);   // as before sheets: first placed, first named
                }
                else
                {
                    Join(id, label.Sheet + ":" + name);
                    Name(id, SheetPath(label.Sheet) + name, IsGroundName(name), 10 + depth);
                }
            }

            foreach (var symbol in Sheets)
            {
                var inside = Labels.Where(l => l.Hierarchical && SameSheet(l.Sheet, symbol.Uid) && !string.IsNullOrWhiteSpace(l.Name)).ToList();
                foreach (var pin in symbol.Pins)
                {
                    if (string.IsNullOrWhiteSpace(pin.Name)) continue;
                    int at = pointIds[(Key(symbol.Sheet), pin.Position)];
                    foreach (var l in inside.Where(l => string.Equals(l.Name.Trim(), pin.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                        uf.Union(at, pointIds[(Key(l.Sheet), l.Position)]);
                    Name(at, SheetPath(symbol.Sheet) == "/" ? pin.Name.Trim() : SheetPath(symbol.Sheet) + pin.Name.Trim(), false, 200 + SheetDepth(symbol.Sheet));
                }
            }

            var pinIds = new Dictionary<(SchematicPart, string), int>();
            foreach (var (part, pin, pos) in pins)
            {
                int id = pointIds[(Key(part.Sheet), pos)];
                pinIds[(part, pin.Name)] = id;
                var def = library.Get(part.PartId);
                if (def.Symbol == SymbolKind.Ground)
                {
                    Join(id, ":GND");
                    Name(id, "GND", true, 0);
                }
                else if (def.Symbol == SymbolKind.PowerRail)
                {
                    var parameters = ResolveParameters(library, part);
                    string net = parameters.TryGetValue("net", out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : "VCC";
                    Join(id, ":" + net);
                    Name(id, net, IsGroundName(net), 0);
                }
            }

            foreach (var part in Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null)
                {
                    netlist.Warnings.Add($"{part.RefDes}: bilinmeyen parça '{part.PartId}'.");
                    continue;
                }

                foreach (var group in def.InternalConnections)
                {
                    for (int i = 1; i < group.Length; i++)
                    {
                        if (pinIds.TryGetValue((part, group[0]), out int a) && pinIds.TryGetValue((part, group[i]), out int b))
                        {
                            uf.Union(a, b);
                        }
                    }
                }
            }

            // Collect nets.
            var nets = new Dictionary<int, Net>();
            foreach (var (part, pin, pos) in pins)
            {
                var def = library.Get(part.PartId);
                if (def.Symbol == SymbolKind.Ground || def.Symbol == SymbolKind.PowerRail) continue;
                int root = uf.Find(pointIds[(Key(part.Sheet), pos)]);
                if (!nets.TryGetValue(root, out var net))
                {
                    net = new Net();
                    nets[root] = net;
                }

                net.Connections.Add(new NetConnection(part.RefDes, pin.Name));
            }

            // Names: explicit names win (ground first, then by rank), otherwise "<first part>.<pin>".
            foreach (var (id, name, ground, _) in namedRoots.OrderByDescending(r => r.Ground).ThenBy(r => r.Rank))
            {
                int root = uf.Find(id);
                if (!nets.TryGetValue(root, out var net))
                {
                    net = new Net();
                    nets[root] = net;
                }

                if (string.IsNullOrEmpty(net.Name))
                {
                    net.Name = name;
                    net.IsGround = ground;
                }
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var net in nets.Values.OrderBy(n => n.Connections.Count > 0 ? n.Connections[0].ToString() : n.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrEmpty(net.Name))
                {
                    net.Name = net.Connections.Count > 0 ? net.Connections[0].RefDes + "." + net.Connections[0].Pin : "?";
                    net.IsAutoNamed = true;
                }

                string unique = net.Name;
                for (int k = 2; !used.Add(unique); k++) unique = net.Name + "_" + k.ToString(CultureInfo.InvariantCulture);
                net.Name = unique;
                netlist.Nets.Add(net);

                if (net.Connections.Count == 1 && !net.IsGround)
                {
                    // Boards, connectors and ICs routinely leave pins unused; only flag small parts.
                    var only = net.Connections[0];
                    var owner = FindPart(only.RefDes);
                    var ownerDef = owner != null ? library.Find(owner.PartId) : null;
                    var pinDef = ownerDef?.FindPin(only.Pin);
                    bool flagged = owner != null && pinDef != null &&
                                   NoConnects.Any(nc => SameSheet(nc.Sheet, owner.Sheet) && nc.Position.Equals(owner.Transform(pinDef.X, pinDef.Y)));
                    if (ownerDef != null && ownerDef.Pins.Count <= 3 && !flagged)
                    {
                        netlist.Warnings.Add($"{only.RefDes}.{only.Pin} hiçbir yere bağlı değil.");
                    }
                }
            }

            // Wire / label -> net name, and junction dots.
            var nameByRoot = new Dictionary<int, string>();
            foreach (var kv in nets) nameByRoot[kv.Key] = kv.Value.Name;
            foreach (var wire in Wires)
            {
                string? name = null;
                if (wire.Points.Count > 0 && nameByRoot.TryGetValue(uf.Find(pointIds[(Key(wire.Sheet), wire.Points[0])]), out var n)) name = n;
                netlist.WireNets.Add(name);
            }

            foreach (var label in Labels)
            {
                netlist.LabelNets.Add(nameByRoot.TryGetValue(uf.Find(pointIds[(Key(label.Sheet), label.Position)]), out var n) ? n : null);
            }

            var degree = new Dictionary<(string Sheet, GridPoint P), int>();
            void Bump((string, GridPoint) p, int by) => degree[p] = (degree.TryGetValue(p, out int d) ? d : 0) + by;
            foreach (var wire in Wires)
            {
                for (int i = 0; i < wire.Points.Count; i++)
                {
                    bool end = i == 0 || i == wire.Points.Count - 1;
                    Bump((Key(wire.Sheet), wire.Points[i]), wire.Points.Count == 1 ? 0 : (end ? 1 : 2));
                }
            }

            foreach (var point in degree.Keys.ToList())
            {
                if (!bySheet.TryGetValue(point.Sheet, out var onSheet)) continue;
                foreach (var (_, a, b) in onSheet)
                {
                    if (OnSegment(point.P, a, b)) Bump(point, 2);
                }
            }

            foreach (var (part, _, pos) in pins)
            {
                var key = (Key(part.Sheet), pos);
                if (degree.ContainsKey(key)) Bump(key, 1);
            }

            foreach (var kv in degree)
            {
                if (kv.Value < 3) continue;
                netlist.Junctions.Add(kv.Key.P);
                netlist.JunctionSheets.Add(kv.Key.Sheet.Length == 0 ? null : kv.Key.Sheet);
            }

            return netlist;
        }

        private static bool IsGroundName(string name) =>
            string.Equals(name.Trim(), "GND", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name.Trim(), "0", StringComparison.Ordinal);

        private static bool OnSegment(GridPoint p, GridPoint a, GridPoint b)
        {
            if (p.Equals(a) || p.Equals(b)) return false;
            double cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            double len = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            if (len < GridPoint.Tolerance || Math.Abs(cross) / len > GridPoint.Tolerance) return false;
            double dot = (p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y);
            return dot > 0 && dot < len * len;
        }

        // ------------------------------------------------------------- persistence

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static Schematic FromJson(string json)
        {
            var doc = JsonSerializer.Deserialize<Schematic>(json, JsonOptions) ?? new Schematic();
            if (doc.Version > FormatVersion)
            {
                throw new InvalidDataException($"Bu dosya daha yeni bir sürümle kaydedilmiş (v{doc.Version}).");
            }

            foreach (var part in doc.Parts)
            {
                part.Parameters = new Dictionary<string, string>(part.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            }

            doc.NoConnects ??= new List<NoConnectFlag>();
            doc.Sheets ??= new List<SchematicSheet>();   // [sheets]
            return doc;
        }

        public void Save(string path) => File.WriteAllText(path, ToJson());

        public static Schematic Load(string path) => FromJson(File.ReadAllText(path));
    }

    internal sealed class UnionFind
    {
        private readonly List<int> _parent = new List<int>();

        public int Add()
        {
            _parent.Add(_parent.Count);
            return _parent.Count - 1;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }

            return x;
        }

        public void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }
    }
}
