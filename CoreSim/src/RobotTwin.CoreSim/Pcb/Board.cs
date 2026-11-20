using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Pcb
{
    public sealed class DesignRules
    {
        public double Clearance { get; set; } = 0.2;
        public double TrackWidth { get; set; } = 0.3;
        public double PowerTrackWidth { get; set; } = 0.8;
        public double ViaDiameter { get; set; } = 0.9;
        public double ViaDrill { get; set; } = 0.45;
        public double EdgeClearance { get; set; } = 0.5;
        public double MinTrackWidth { get; set; } = 0.15;
        public double MinDrill { get; set; } = 0.3;

        /// <summary>Copper weight: 35 µm = 1 oz.</summary>
        public double CopperThicknessUm { get; set; } = 35.0;

        /// <summary>Allowed temperature rise for the IPC-2221 current-capacity check.</summary>
        public double AllowedTempRiseC { get; set; } = 10.0;

        /// <summary>Routing grid in millimetres.</summary>
        public double RoutingGrid { get; set; } = 0.25;

        /// <summary>Minimum copper ring around a plated hole ((pad - drill) / 2).</summary>
        public double MinAnnularRing { get; set; } = 0.13;

        /// <summary>Minimum distance between the edges of two drilled holes.</summary>
        public double MinHoleToHole { get; set; } = 0.25;

        /// <summary>Copper clearance to a non-plated hole (mounting hole).</summary>
        public double HoleClearance { get; set; } = 0.25;

        /// <summary>Silkscreen line width used for board texts and the legend.</summary>
        public double SilkWidth { get; set; } = 0.15;
    }

    public sealed class BoardComponent
    {
        public string RefDes { get; set; } = string.Empty;
        public string FootprintId { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Degrees, multiple of 90.</summary>
        public int Rotation { get; set; }
        public CopperLayer Side { get; set; } = CopperLayer.Top;
        public bool Locked { get; set; }

        /// <summary>Pad name -> net name.</summary>
        public Dictionary<string, string> PadNets { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public (double X, double Y) ToBoard(double x, double y)
        {
            if (Side == CopperLayer.Bottom) x = -x;
            int r = ((Rotation % 360) + 360) % 360;
            double tx, ty;
            switch (r)
            {
                case 90: tx = y; ty = -x; break;
                case 180: tx = -x; ty = -y; break;
                case 270: tx = -y; ty = x; break;
                default: tx = x; ty = y; break;
            }

            return (X + tx, Y + ty);
        }

        /// <summary>Pad size after rotation.</summary>
        public (double W, double H) PadSize(Pad pad) =>
            Rotation % 180 == 0 ? (pad.Width, pad.Height) : (pad.Height, pad.Width);

        public (double MinX, double MinY, double MaxX, double MaxY) Courtyard(Footprint fp)
        {
            var a = ToBoard(fp.CourtyardMinX, fp.CourtyardMinY);
            var b = ToBoard(fp.CourtyardMaxX, fp.CourtyardMaxY);
            return (Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
        }
    }

    public sealed class Track
    {
        public string Net { get; set; } = string.Empty;
        public CopperLayer Layer { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Width { get; set; } = 0.3;

        public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    }

    public enum ViaKind
    {
        Through,
        BlindBuried
    }

    public sealed class Via
    {
        public string Net { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Diameter { get; set; } = 0.9;
        public double Drill { get; set; } = 0.45;

        /// <summary>First and last copper layer the via connects (Top..Bottom = through via).</summary>
        public CopperLayer StartLayer { get; set; } = CopperLayer.Top;
        public CopperLayer EndLayer { get; set; } = CopperLayer.Bottom;

        public bool IsThrough(int layerCount)
        {
            int a = PcbLayers.StackIndex(StartLayer, layerCount), b = PcbLayers.StackIndex(EndLayer, layerCount);
            if (a < 0 || b < 0) return true;
            return Math.Min(a, b) <= 0 && Math.Max(a, b) >= PcbLayers.Normalize(layerCount) - 1;
        }

        public ViaKind Kind(int layerCount) => IsThrough(layerCount) ? ViaKind.Through : ViaKind.BlindBuried;

        /// <summary>True when the via has copper on <paramref name="layer"/>.</summary>
        public bool Spans(CopperLayer layer, int layerCount)
        {
            if (IsThrough(layerCount)) return PcbLayers.Exists(layer, layerCount);
            int i = PcbLayers.StackIndex(layer, layerCount);
            int a = PcbLayers.StackIndex(StartLayer, layerCount), b = PcbLayers.StackIndex(EndLayer, layerCount);
            return i >= 0 && i >= Math.Min(a, b) && i <= Math.Max(a, b);
        }

        /// <summary>Outermost layers in stack order (top-most first).</summary>
        public (CopperLayer From, CopperLayer To) Span(int layerCount)
        {
            if (IsThrough(layerCount)) return (CopperLayer.Top, CopperLayer.Bottom);
            int a = PcbLayers.StackIndex(StartLayer, layerCount), b = PcbLayers.StackIndex(EndLayer, layerCount);
            return a <= b ? (StartLayer, EndLayer) : (EndLayer, StartLayer);
        }
    }

    /// <summary>How a zone joins the pads of its own net.</summary>
    public enum ZonePadConnection
    {
        /// <summary>Thermal relief: a gap around the pad bridged by spokes.</summary>
        Thermal,

        /// <summary>The pour touches the pad all around.</summary>
        Solid,

        /// <summary>The pad is kept clear like a foreign pad.</summary>
        None
    }

    public enum IslandRemoval
    {
        /// <summary>Remove every island that touches no item of the zone's net.</summary>
        Always,
        Never,

        /// <summary>Remove unconnected islands smaller than <see cref="Zone.MinIslandArea"/>.</summary>
        BelowArea
    }

    /// <summary>One filled island of a zone, stored compactly (x, y pairs).</summary>
    public sealed class ZoneFillIsland
    {
        public double[] Outline { get; set; } = Array.Empty<double>();
        public List<double[]> Holes { get; set; } = new List<double[]>();

        public static ZoneFillIsland From(PolyRegion r) =>
            new ZoneFillIsland { Outline = Pack(r.Outer), Holes = r.Holes.Select(Pack).ToList() };

        private static double[] Pack(List<PcbPoint> pts)
        {
            var a = new double[pts.Count * 2];
            for (int i = 0; i < pts.Count; i++)
            {
                a[2 * i] = Math.Round(pts[i].X, 5);
                a[2 * i + 1] = Math.Round(pts[i].Y, 5);
            }

            return a;
        }

        private static List<PcbPoint> Unpack(double[] a)
        {
            var l = new List<PcbPoint>(a.Length / 2);
            for (int i = 0; i + 1 < a.Length; i += 2) l.Add(new PcbPoint(a[i], a[i + 1]));
            return l;
        }

        public PolyRegion ToRegion() => new PolyRegion { Outer = Unpack(Outline), Holes = (Holes ?? new List<double[]>()).Select(Unpack).ToList() };
    }

    /// <summary>
    /// Copper pour on one layer for one net (KiCad zone semantics): a polygon outline (or
    /// the whole board when empty), clearance to other nets, minimum width, thermal
    /// reliefs to its own pads, priority against overlapping zones and island removal.
    /// <see cref="ZoneFiller"/> computes <see cref="Fill"/>.
    /// </summary>
    public sealed class Zone
    {
        private List<ZoneFillIsland> _fill = new List<ZoneFillIsland>();
        private List<PolyRegion>? _regions;

        public string Net { get; set; } = "GND";
        public CopperLayer Layer { get; set; } = CopperLayer.Bottom;
        public string Name { get; set; } = string.Empty;

        /// <summary>Outline in board coordinates; empty means "the whole board".</summary>
        public List<PcbPoint> Outline { get; set; } = new List<PcbPoint>();

        /// <summary>Higher priority zones are filled first and keep their copper where zones overlap.</summary>
        public int Priority { get; set; }

        /// <summary>Copper clearance to other nets; 0 uses the net classes.</summary>
        public double Clearance { get; set; }

        /// <summary>Minimum copper width (thinner necks are removed, corners get this radius).</summary>
        public double MinWidth { get; set; } = 0.25;

        public ZonePadConnection PadConnection { get; set; } = ZonePadConnection.Thermal;
        public double ThermalGap { get; set; } = 0.5;
        public double ThermalSpokeWidth { get; set; } = 0.5;
        public IslandRemoval IslandRemoval { get; set; } = IslandRemoval.Always;

        /// <summary>Area limit for <see cref="IslandRemoval.BelowArea"/> (mm²).</summary>
        public double MinIslandArea { get; set; } = 10;

        /// <summary>Filled copper from the last fill (empty until filled).</summary>
        public List<ZoneFillIsland> Fill
        {
            get => _fill;
            set
            {
                _fill = value ?? new List<ZoneFillIsland>();
                _regions = null;
            }
        }

        /// <summary>Signature of the board state the fill was computed from (see <see cref="ZoneFiller.Signature"/>).</summary>
        public string FillHash { get; set; } = string.Empty;

        [JsonIgnore]
        public bool IsFilled => !string.IsNullOrEmpty(FillHash);

        /// <summary>The fill as polygon regions (cached).</summary>
        [JsonIgnore]
        public List<PolyRegion> FilledRegions => _regions ??= _fill.Select(i => i.ToRegion()).ToList();

        [JsonIgnore]
        public bool IsWholeBoard => Outline == null || Outline.Count < 3;

        public void ClearFill()
        {
            Fill = new List<ZoneFillIsland>();
            FillHash = string.Empty;
        }

        public Zone CloneSettings() => new Zone
        {
            Net = Net, Layer = Layer, Name = Name, Outline = new List<PcbPoint>(Outline ?? new List<PcbPoint>()), Priority = Priority,
            Clearance = Clearance, MinWidth = MinWidth, PadConnection = PadConnection, ThermalGap = ThermalGap,
            ThermalSpokeWidth = ThermalSpokeWidth, IslandRemoval = IslandRemoval, MinIslandArea = MinIslandArea
        };
    }

    /// <summary>A board outline vertex; <see cref="Radius"/> rounds the corner with a tangent arc.</summary>
    public sealed class OutlinePoint
    {
        public OutlinePoint()
        {
        }

        public OutlinePoint(double x, double y, double radius = 0)
        {
            X = x;
            Y = y;
            Radius = radius;
        }

        public double X { get; set; }
        public double Y { get; set; }
        public double Radius { get; set; }
    }

    /// <summary>A board-outline primitive: a line, or an arc from (X1, Y1) to (X2, Y2) around (CX, CY).</summary>
    public readonly struct OutlineElement
    {
        public OutlineElement(double x1, double y1, double x2, double y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            IsArc = false;
            CX = CY = R = MidX = MidY = 0;
            CounterClockwise = false;
        }

        public OutlineElement(double x1, double y1, double x2, double y2, double cx, double cy, double r, double midX, double midY, bool ccw)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            IsArc = true;
            CX = cx;
            CY = cy;
            R = r;
            MidX = midX;
            MidY = midY;
            CounterClockwise = ccw;
        }

        public bool IsArc { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double CX { get; }
        public double CY { get; }
        public double R { get; }
        public double MidX { get; }
        public double MidY { get; }

        /// <summary>Arc direction in board coordinates (Y down): true when the angle increases from start to end.</summary>
        public bool CounterClockwise { get; }
    }

    /// <summary>A drilled mounting hole; with <see cref="PadDiameter"/> larger than the drill it is plated with a copper ring.</summary>
    public sealed class MountingHole
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Drill { get; set; } = 3.2;
        public double PadDiameter { get; set; }
        public string Net { get; set; } = string.Empty;

        [JsonIgnore]
        public bool Plated => PadDiameter > Drill + 1e-9;
    }

    /// <summary>Free text on a graphic layer (silkscreen, fab, drawings).</summary>
    public sealed class BoardText
    {
        public string Text { get; set; } = string.Empty;
        public double X { get; set; }
        public double Y { get; set; }
        public double Size { get; set; } = 1.5;
        public double Thickness { get; set; } = 0.15;
        public GraphicLayer Layer { get; set; } = GraphicLayer.SilkTop;

        /// <summary>Degrees, multiple of 90.</summary>
        public int Rotation { get; set; }

        [JsonIgnore]
        public bool Mirrored => Layer == GraphicLayer.SilkBottom || Layer == GraphicLayer.FabBottom;
    }

    /// <summary>An aligned dimension between two points, drawn <see cref="Offset"/> mm to the side.</summary>
    public sealed class BoardDimension
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Offset { get; set; } = 3;
        public double TextSize { get; set; } = 1.0;
        public GraphicLayer Layer { get; set; } = GraphicLayer.Drawings;

        [JsonIgnore]
        public double Length => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));

        /// <summary>Extension lines, the dimension line, arrow heads and the text anchor (board mm, angle in degrees).</summary>
        public (List<(double X1, double Y1, double X2, double Y2)> Lines, double TextX, double TextY, double Angle) Geometry()
        {
            var lines = new List<(double, double, double, double)>();
            double dx = X2 - X1, dy = Y2 - Y1, len = Math.Max(1e-9, Length);
            double ux = dx / len, uy = dy / len, nx = -uy, ny = ux;
            double sign = Offset < 0 ? -1 : 1;
            double ax = X1 + nx * Offset, ay = Y1 + ny * Offset, bx = X2 + nx * Offset, by = Y2 + ny * Offset;
            lines.Add((X1, Y1, ax + nx * 0.5 * sign, ay + ny * 0.5 * sign));
            lines.Add((X2, Y2, bx + nx * 0.5 * sign, by + ny * 0.5 * sign));
            lines.Add((ax, ay, bx, by));
            double head = Math.Min(1.0, len / 4);
            foreach (var (px, py, sx) in new[] { (ax, ay, 1.0), (bx, by, -1.0) })
            {
                lines.Add((px, py, px + (ux * sx * 0.94 + nx * 0.34) * head, py + (uy * sx * 0.94 + ny * 0.34) * head));
                lines.Add((px, py, px + (ux * sx * 0.94 - nx * 0.34) * head, py + (uy * sx * 0.94 - ny * 0.34) * head));
            }

            double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            double gap = TextSize * 0.6 + 0.3;
            return (lines, (ax + bx) / 2 + nx * gap * sign, (ay + by) / 2 + ny * gap * sign, angle);
        }

        public string Label(bool mil = false) =>
            mil ? (Length / 0.0254).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " mil"
                : Length.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " mm";
    }

    /// <summary>A placed pad in board coordinates, with its net.</summary>
    public readonly struct PlacedPad
    {
        public PlacedPad(BoardComponent component, Pad pad, double x, double y, double w, double h, string net)
        {
            Component = component;
            Pad = pad;
            X = x;
            Y = y;
            Width = w;
            Height = h;
            Net = net;
        }

        public BoardComponent Component { get; }
        public Pad Pad { get; }
        public double X { get; }
        public double Y { get; }
        public double Width { get; }
        public double Height { get; }
        public string Net { get; }

        /// <summary>Copper layer of an SMD pad after the part is flipped to its side.</summary>
        public CopperLayer SmdLayer => Component.Side == CopperLayer.Top ? Pad.Layer : PcbLayers.Opposite(Pad.Layer);

        /// <summary>True when the pad has copper on <paramref name="layer"/> (through-hole pads: every layer).</summary>
        public bool OnLayer(CopperLayer layer) =>
            Pad.IsThroughHole ? !Pad.NonPlated : SmdLayer == layer;

        /// <summary>Corner radius of the pad's copper (for round-rect pads).</summary>
        public double CornerRadius => Pad.Shape switch
        {
            PadShape.RoundRect => Pad.CornerRatio * Math.Min(Width, Height),
            PadShape.Oval => Math.Min(Width, Height) / 2,
            PadShape.Circle => Math.Max(Width, Height) / 2,
            _ => 0
        };
    }

    /// <summary>
    /// The PCB layout: a 2/4/6-layer board with a rectangular (rounded) or polygon outline,
    /// parts, tracks, vias, zones, net classes, mounting holes and drawings.
    /// </summary>
    public sealed class BoardDocument
    {
        private List<NetClass>? _netClasses;
        private Dictionary<string, string> _assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Board size (the outline's bounding box from the origin).</summary>
        public double Width { get; set; } = 80;
        public double Height { get; set; } = 60;

        /// <summary>Corner radius of the rectangular outline (used when <see cref="Outline"/> is empty).</summary>
        public double CornerRadius { get; set; } = 1.0;

        /// <summary>Polygon outline (clockwise or counter-clockwise, rounded corners per vertex); empty = rectangle.</summary>
        public List<OutlinePoint> Outline { get; set; } = new List<OutlinePoint>();

        public DesignRules Rules { get; set; } = new DesignRules();
        public BoardStackup Stackup { get; set; } = new BoardStackup();
        public List<BoardComponent> Components { get; set; } = new List<BoardComponent>();
        public List<Track> Tracks { get; set; } = new List<Track>();
        public List<Via> Vias { get; set; } = new List<Via>();
        public List<Zone> Zones { get; set; } = new List<Zone>();
        public List<MountingHole> MountingHoles { get; set; } = new List<MountingHole>();
        public List<BoardText> Texts { get; set; } = new List<BoardText>();
        public List<BoardDimension> Dimensions { get; set; } = new List<BoardDimension>();

        /// <summary>Nets that carry supply current and get wide tracks (they default to the Power class).</summary>
        public List<string> PowerNets { get; set; } = new List<string>();

        /// <summary>Net classes; created from <see cref="Rules"/> (Default and Power) when a board has none.</summary>
        public List<NetClass> NetClasses
        {
            get
            {
                if (_netClasses == null || _netClasses.Count == 0)
                {
                    _netClasses = new List<NetClass>
                    {
                        new NetClass { Name = NetClass.DefaultName, Clearance = Rules.Clearance, TrackWidth = Rules.TrackWidth, ViaDiameter = Rules.ViaDiameter, ViaDrill = Rules.ViaDrill },
                        new NetClass { Name = NetClass.PowerName, Clearance = Rules.Clearance, TrackWidth = Rules.PowerTrackWidth, ViaDiameter = Rules.ViaDiameter, ViaDrill = Rules.ViaDrill }
                    };
                }

                return _netClasses;
            }
            set => _netClasses = value;
        }

        /// <summary>Explicit net → class assignments (nets not listed: Power if in <see cref="PowerNets"/>, else Default).</summary>
        public Dictionary<string, string> NetClassAssignments
        {
            get => _assignments;
            set => _assignments = new Dictionary<string, string>(value ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        }

        [JsonIgnore]
        public int LayerCount => Stackup.CopperLayers;

        /// <summary>Copper layers in stack order (top first).</summary>
        public IReadOnlyList<CopperLayer> CopperLayers() => PcbLayers.Stack(LayerCount);

        public bool HasLayer(CopperLayer layer) => PcbLayers.Exists(layer, LayerCount);

        [JsonIgnore]
        public NetClass DefaultClass => NetClasses.FirstOrDefault(c => string.Equals(c.Name, NetClass.DefaultName, StringComparison.OrdinalIgnoreCase)) ?? NetClasses[0];

        public NetClass? FindClass(string name) =>
            NetClasses.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>The class of a net: explicit assignment, else Power for supply nets, else Default.</summary>
        public NetClass ClassFor(string? net)
        {
            if (!string.IsNullOrEmpty(net))
            {
                if (_assignments.TryGetValue(net!, out var name) && FindClass(name) is { } assigned) return assigned;
                if (PowerNets.Contains(net!, StringComparer.OrdinalIgnoreCase) && FindClass(NetClass.PowerName) is { } power) return power;
            }

            return DefaultClass;
        }

        public string ClassNameFor(string? net) => ClassFor(net).Name;

        public void AssignClass(string net, string className)
        {
            if (string.IsNullOrEmpty(net)) return;
            _assignments[net] = className;
        }

        /// <summary>Required copper clearance between two nets: the larger of the two classes' clearances.</summary>
        public double ClearanceBetween(string? a, string? b) => Math.Max(ClassFor(a).Clearance, ClassFor(b).Clearance);

        /// <summary>The largest clearance any class asks for.</summary>
        public double MaxClearance() => NetClasses.Max(c => c.Clearance);

        public BoardComponent? Find(string refDes) =>
            Components.FirstOrDefault(c => string.Equals(c.RefDes, refDes, StringComparison.OrdinalIgnoreCase));

        // ------------------------------------------------------------- outline

        /// <summary>The outline as lines and tangent arcs (rectangle with <see cref="CornerRadius"/> when no polygon is set).</summary>
        public List<OutlineElement> OutlineElements()
        {
            var pts = Outline != null && Outline.Count >= 3
                ? Outline
                : new List<OutlinePoint>
                {
                    new OutlinePoint(0, 0, CornerRadius), new OutlinePoint(Width, 0, CornerRadius),
                    new OutlinePoint(Width, Height, CornerRadius), new OutlinePoint(0, Height, CornerRadius)
                };
            int n = pts.Count;
            var corners = new (double Ax, double Ay, double Bx, double By, double Cx, double Cy, double R, bool Arc)[n];
            for (int i = 0; i < n; i++)
            {
                var p = pts[i];
                var prev = pts[(i - 1 + n) % n];
                var next = pts[(i + 1) % n];
                double r = Math.Max(0, p.Radius);
                double ux = prev.X - p.X, uy = prev.Y - p.Y, vx = next.X - p.X, vy = next.Y - p.Y;
                double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
                if (r < 1e-9 || lu < 1e-9 || lv < 1e-9)
                {
                    corners[i] = (p.X, p.Y, p.X, p.Y, p.X, p.Y, 0, false);
                    continue;
                }

                ux /= lu;
                uy /= lu;
                vx /= lv;
                vy /= lv;
                double cos = Math.Max(-1, Math.Min(1, ux * vx + uy * vy));
                double half = Math.Acos(cos) / 2;
                if (half < 1e-6 || half > Math.PI / 2 - 1e-6)
                {
                    corners[i] = (p.X, p.Y, p.X, p.Y, p.X, p.Y, 0, false);
                    continue;
                }

                double t = r / Math.Tan(half);
                double tMax = Math.Min(lu, lv) / 2;
                if (t > tMax)
                {
                    t = tMax;
                    r = t * Math.Tan(half);
                }

                double bx = ux + vx, by = uy + vy, bl = Math.Sqrt(bx * bx + by * by);
                double d = r / Math.Sin(half);
                corners[i] = (p.X + ux * t, p.Y + uy * t, p.X + vx * t, p.Y + vy * t, p.X + bx / bl * d, p.Y + by / bl * d, r, true);
            }

            var result = new List<OutlineElement>();
            for (int i = 0; i < n; i++)
            {
                var c = corners[i];
                if (c.Arc)
                {
                    double a0 = Math.Atan2(c.Ay - c.Cy, c.Ax - c.Cx), a1 = Math.Atan2(c.By - c.Cy, c.Bx - c.Cx);
                    double sweep = a1 - a0;
                    while (sweep > Math.PI) sweep -= 2 * Math.PI;
                    while (sweep < -Math.PI) sweep += 2 * Math.PI;
                    double am = a0 + sweep / 2;
                    result.Add(new OutlineElement(c.Ax, c.Ay, c.Bx, c.By, c.Cx, c.Cy, c.R, c.Cx + c.R * Math.Cos(am), c.Cy + c.R * Math.Sin(am), sweep > 0));
                }

                var nc = corners[(i + 1) % n];
                if (Math.Abs(nc.Ax - c.Bx) + Math.Abs(nc.Ay - c.By) > 1e-9) result.Add(new OutlineElement(c.Bx, c.By, nc.Ax, nc.Ay));
            }

            return result;
        }

        /// <summary>The outline as a counter-clockwise polygon (arcs tessellated).</summary>
        public List<PcbPoint> OutlinePolygon(double tolerance = 0.005)
        {
            var pts = new List<PcbPoint>();
            foreach (var e in OutlineElements())
            {
                if (!e.IsArc)
                {
                    pts.Add(new PcbPoint(e.X1, e.Y1));
                    continue;
                }

                double a0 = Math.Atan2(e.Y1 - e.CY, e.X1 - e.CX), a1 = Math.Atan2(e.Y2 - e.CY, e.X2 - e.CX);
                double sweep = a1 - a0;
                while (sweep > Math.PI) sweep -= 2 * Math.PI;
                while (sweep < -Math.PI) sweep += 2 * Math.PI;
                int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / Shapes.StepAngle(e.R, tolerance)));
                for (int k = 0; k < steps; k++)
                {
                    double a = a0 + sweep * k / steps;
                    pts.Add(new PcbPoint(e.CX + e.R * Math.Cos(a), e.CY + e.R * Math.Sin(a)));
                }
            }

            return Polygon.CounterClockwise(pts);
        }

        /// <summary>Distance from a point to the board edge, negative outside the board.</summary>
        public double EdgeDistance(double x, double y)
        {
            var poly = OutlinePolygon(0.01);
            double best = double.PositiveInfinity;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                best = Math.Min(best, Geometry.PointSegmentDistance(x, y, a.X, a.Y, b.X, b.Y));
            }

            return Polygon.Winding(poly, x, y) != 0 ? best : -best;
        }

        /// <summary>Re-computes <see cref="Width"/>/<see cref="Height"/> from the polygon outline.</summary>
        public void FitSizeToOutline()
        {
            if (Outline == null || Outline.Count < 3) return;
            Width = Math.Round(Outline.Max(p => p.X), 4);
            Height = Math.Round(Outline.Max(p => p.Y), 4);
        }

        public IEnumerable<PlacedPad> Pads()
        {
            foreach (var comp in Components)
            {
                var fp = FootprintLibrary.Get(comp.FootprintId);
                if (fp == null) continue;
                foreach (var pad in fp.Pads)
                {
                    var (x, y) = comp.ToBoard(pad.X, pad.Y);
                    var (w, h) = comp.PadSize(pad);
                    comp.PadNets.TryGetValue(pad.Name, out var net);
                    yield return new PlacedPad(comp, pad, x, y, w, h, net ?? string.Empty);
                }
            }
        }

        public IEnumerable<string> NetNames() =>
            Components.SelectMany(c => c.PadNets.Values).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase);

        /// <summary>Track width of a net's class.</summary>
        public double TrackWidthFor(string net) => ClassFor(net).TrackWidth;

        /// <summary>Via size of a net's class.</summary>
        public (double Diameter, double Drill) ViaFor(string net)
        {
            var c = ClassFor(net);
            return (c.ViaDiameter, c.ViaDrill);
        }

        /// <summary>
        /// Creates or refreshes the board from a schematic: adds new parts, removes
        /// deleted ones, keeps the placement of the ones that stay, and refreshes all
        /// pad nets. Parts without a footprint (ground symbols, meters) are skipped.
        /// </summary>
        public static BoardDocument Synchronize(Schematic schematic, PartLibrary library, BoardDocument? existing = null)
        {
            var board = existing ?? new BoardDocument();
            var netlist = schematic.BuildNetlist(library);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var part in schematic.Parts)
            {
                var def = library.Find(part.PartId);
                if (def == null) continue;
                var parameters = schematic.ResolveParameters(library, part);
                string fpId = def.ResolveFootprint(parameters);
                var fp = FootprintLibrary.Get(fpId);
                if (fp == null) continue;

                var comp = board.Find(part.RefDes);
                if (comp == null)
                {
                    comp = new BoardComponent { RefDes = part.RefDes };
                    board.Components.Add(comp);
                }

                comp.FootprintId = fpId;
                comp.Value = !string.IsNullOrEmpty(def.ValueKey) && parameters.TryGetValue(def.ValueKey, out var v) ? v : def.Name;
                comp.PadNets.Clear();
                foreach (var pin in def.Pins)
                {
                    var net = netlist.NetOf(part.RefDes, pin.Name);
                    if (net == null) continue;
                    string pad = def.ResolvePad(pin, parameters);
                    if (fp.FindPad(pad) != null) comp.PadNets[pad] = net.Name;
                }

                // Tactile buttons have two pads per side that are joined inside.
                if (fp.Id.StartsWith("TACT", StringComparison.OrdinalIgnoreCase))
                {
                    if (comp.PadNets.TryGetValue("1", out var n1)) comp.PadNets["2"] = n1;
                    if (comp.PadNets.TryGetValue("3", out var n3)) comp.PadNets["4"] = n3;
                }

                keep.Add(part.RefDes);
            }

            board.Components.RemoveAll(c => !keep.Contains(c.RefDes));
            var liveNets = new HashSet<string>(board.NetNames(), StringComparer.OrdinalIgnoreCase);
            board.Tracks.RemoveAll(t => !liveNets.Contains(t.Net));
            board.Vias.RemoveAll(v => !liveNets.Contains(v.Net));

            // Supply nets: ground, labelled rails and anything touching a source or regulator output.
            board.PowerNets.Clear();
            foreach (var net in netlist.Nets)
            {
                bool power = net.IsGround ||
                             !net.IsAutoNamed && net.Name.IndexOf("V", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             net.Connections.Any(c =>
                             {
                                 var p = schematic.FindPart(c.RefDes);
                                 var d = p != null ? library.Find(p.PartId) : null;
                                 return d != null && (d.Symbol == SymbolKind.Battery || d.Symbol == SymbolKind.VoltageSource ||
                                                      d.Symbol == SymbolKind.Motor || d.Symbol == SymbolKind.Regulator && c.Pin != "GND");
                             });
                if (power && liveNets.Contains(net.Name)) board.PowerNets.Add(net.Name);
            }

            return board;
        }

        /// <summary>Pads grouped by net (only nets with two or more pads need routing).</summary>
        public Dictionary<string, List<PlacedPad>> PadsByNet()
        {
            var result = new Dictionary<string, List<PlacedPad>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pad in Pads())
            {
                if (string.IsNullOrEmpty(pad.Net)) continue;
                if (!result.TryGetValue(pad.Net, out var list))
                {
                    list = new List<PlacedPad>();
                    result[pad.Net] = list;
                }

                list.Add(pad);
            }

            return result;
        }

        /// <summary>
        /// The ratsnest: for every net, the shortest connections that would join the groups
        /// of pads not yet connected by copper (tracks, vias, zone fills).
        /// </summary>
        public List<(string Net, PlacedPad A, PlacedPad B)> Ratsnest() => BoardConnectivity.Build(this).Ratsnest();

        /// <summary>Minimum-spanning-tree connections between all pads of each net, ignoring copper.</summary>
        public List<(string Net, PlacedPad A, PlacedPad B)> PadConnections()
        {
            var result = new List<(string, PlacedPad, PlacedPad)>();
            foreach (var kv in PadsByNet())
            {
                var pads = kv.Value;
                if (pads.Count < 2) continue;
                var inTree = new bool[pads.Count];
                var best = new double[pads.Count];
                var from = new int[pads.Count];
                for (int i = 0; i < pads.Count; i++)
                {
                    best[i] = double.PositiveInfinity;
                    from[i] = -1;
                }

                best[0] = 0;
                for (int step = 0; step < pads.Count; step++)
                {
                    int u = -1;
                    for (int i = 0; i < pads.Count; i++)
                    {
                        if (!inTree[i] && (u < 0 || best[i] < best[u])) u = i;
                    }

                    inTree[u] = true;
                    if (from[u] >= 0) result.Add((kv.Key, pads[from[u]], pads[u]));
                    for (int i = 0; i < pads.Count; i++)
                    {
                        if (inTree[i]) continue;
                        double d = Math.Abs(pads[i].X - pads[u].X) + Math.Abs(pads[i].Y - pads[u].Y);
                        if (d < best[i])
                        {
                            best[i] = d;
                            from[i] = u;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>DC resistance of a copper track (resistivity of copper at 20 C).</summary>
        public double TrackResistance(Track track)
        {
            const double Rho = 1.68e-8;
            double um = PcbLayers.IsOuter(track.Layer) ? Rules.CopperThicknessUm : Stackup.InnerCopperUm;
            double area = track.Width * 1e-3 * um * 1e-6;
            return Rho * track.Length * 1e-3 / area;
        }

        /// <summary>IPC-2221 current capacity of a track on a given layer (inner layers: inner copper, internal factor).</summary>
        public double CurrentCapacity(double widthMm, CopperLayer layer) =>
            PcbLayers.IsOuter(layer) ? CurrentCapacity(widthMm) : CurrentCapacity(widthMm, false, Stackup.InnerCopperUm);

        /// <summary>IPC-2221 external-layer current capacity for a track width.</summary>
        public double CurrentCapacity(double widthMm, bool external = true, double copperUm = 0)
        {
            double k = external ? 0.048 : 0.024;
            double thicknessMil = (copperUm > 0 ? copperUm : Rules.CopperThicknessUm) / 25.4;
            double areaMil2 = widthMm / 0.0254 * thicknessMil;
            return k * Math.Pow(Rules.AllowedTempRiseC, 0.44) * Math.Pow(areaMil2, 0.725);
        }
    }
}
