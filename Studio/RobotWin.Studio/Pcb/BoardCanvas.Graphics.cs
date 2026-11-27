using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [graphics] KiCad's drawing tools on the PCB: line (chained: each click ends one and starts the next), rectangle (two
    /// corners), circle (centre, radius), arc (centre, start, end) and polygon (corners, double-click or Enter closes) on the
    /// chosen graphic layer; drawn, picked, moved and deleted like every other board item. A standalone via (stitching) is
    /// placed with the via tool, taking the net of the copper under it.
    /// </summary>
    public sealed partial class BoardCanvas
    {
        private readonly List<PcbPoint> _gfx = new List<PcbPoint>();
        private readonly List<(BoardGraphic G, List<PcbPoint> P)> _moveGraphics = new List<(BoardGraphic, List<PcbPoint>)>();
        private readonly List<(RuleArea A, List<PcbPoint> P)> _moveAreas = new List<(RuleArea, List<PcbPoint>)>();

        /// <summary>[keepout] Raised when a rule area's polygon is closed.</summary>
        public event EventHandler<List<PcbPoint>>? KeepoutDrawn;

        /// <summary>[keepout] Rule areas: a hatched outline in the danger colour, on the layers they hold (all when none is hidden).</summary>
        private void DrawRuleAreas(DrawingContext dc, BoardDocument board)
        {
            foreach (var a in board.RuleAreas)
            {
                if (a.Outline.Count < 3 || a.Layers.Count > 0 && a.Layers.All(l => HiddenLayers.Contains(l))) continue;
                var color = A(Danger, a.OnLayer(_activeLayer) ? (byte)0xC0 : (byte)0x60);
                var geo = Poly(a.Outline);
                var pen = new Pen(SymbolRenderer.Brush(color), 1.4) { DashStyle = DashStyles.Dash };
                pen.Freeze();
                dc.DrawGeometry(null, pen, geo);
                // 45° hatching clipped to the area
                var b = geo.Bounds;
                dc.PushClip(geo);
                var hatch = P(A(Danger, (byte)0x45), 1, false);
                for (double k = b.Left - b.Height; k < b.Right; k += 10) dc.DrawLine(hatch, new Point(k, b.Bottom), new Point(k + b.Height, b.Top));
                dc.Pop();
            }
        }

        private RuleArea? HitRuleArea(double x, double y)
        {
            if (Board == null) return null;
            double tol = 5 / _scale;
            foreach (var a in Board.RuleAreas)
            {
                if (a.Outline.Count < 3) continue;
                if (a.Edges().Any(e => RobotTwin.CoreSim.Pcb.Geometry.PointSegmentDistance(x, y, e.A.X, e.A.Y, e.B.X, e.B.Y) <= tol)) return a;
            }

            return null;
        }

        /// <summary>The layer the drawing tools draw on (the tool options bar sets it).</summary>
        public GraphicLayer GraphicDrawLayer { get; set; } = GraphicLayer.SilkTop;

        /// <summary>Line width of new shapes (mm).</summary>
        public double DrawWidth { get; set; } = 0.15;

        /// <summary>New rectangles, circles and polygons filled in.</summary>
        public bool DrawFilled { get; set; }

        /// <summary>[image] The picture waiting to be placed and its size (mm).</summary>
        private (string Data, double W, double H)? _pendingImage;

        private BoardGraphic? PendingImageAt(double x, double y) => _pendingImage is not { } p ? null : new BoardGraphic
        {
            Shape = GraphicShape.Image, Layer = GraphicDrawLayer, ImageData = p.Data,
            Points = { new PcbPoint(SnapX(x) - p.W / 2, SnapY(y) - p.H / 2), new PcbPoint(SnapX(x) + p.W / 2, SnapY(y) + p.H / 2) }
        };

        private void PlaceImage(double x, double y)
        {
            var board = Board;
            if (board == null || PendingImageAt(x, y) is not { } image) return;
            _pendingImage = null;
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            board.Graphics.Add(image);
            Edited($"Çizim eklendi: {image.Describe()} · {LayerName(image.Layer)}");
            SetTool(PcbTool.Select);
        }

        /// <summary>[table] A 3 × 3 table from the clicked corner on the drawing layer, then its cells to fill in.</summary>
        private void PlaceTable(double x, double y)
        {
            var board = Board;
            if (board == null) return;
            var table = BoardGraphic.Table(SnapX(x), SnapY(y), 3, 3, 12, 3, GraphicDrawLayer);
            table.Width = DrawWidth;
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            board.Graphics.Add(table);
            Edited($"Çizim eklendi: {table.Describe()} · {LayerName(table.Layer)}");
            SetTool(PcbTool.Select);
            Select(table);
            EditRequested?.Invoke(this, table);
        }

        /// <summary>[image] A picture in its frame, seen through a little so the copper under it still shows.</summary>
        private void DrawImageGraphic(DrawingContext dc, BoardGraphic g, double opacity)
        {
            if (g.Points.Count < 2) return;
            var r = new Rect(S(g.Points[0].X, g.Points[0].Y), S(g.Points[1].X, g.Points[1].Y));
            dc.PushOpacity(opacity);
            if (RobotWin.Studio.Theme.Pictures.Decode(g.ImageData) is { } bitmap) dc.DrawImage(bitmap, r);
            else dc.DrawRectangle(null, P(GraphicColor(g.Layer), 1), r);
            dc.Pop();
        }

        public static bool IsGraphicTool(PcbTool t) => t is PcbTool.Line or PcbTool.Rectangle or PcbTool.Circle or PcbTool.Arc or PcbTool.Polygon or PcbTool.Bezier;

        private static GraphicShape ShapeOf(PcbTool t) => t switch
        {
            PcbTool.Line => GraphicShape.Line,
            PcbTool.Rectangle => GraphicShape.Rectangle,
            PcbTool.Circle => GraphicShape.Circle,
            PcbTool.Arc => GraphicShape.Arc,
            PcbTool.Bezier => GraphicShape.Bezier,
            _ => GraphicShape.Polygon
        };

        /// <summary>[bezier] The curve's points from the clicks (start, end, first control, second control), the cursor standing in for those still to come.</summary>
        private static List<PcbPoint> BezierPoints(IReadOnlyList<PcbPoint> clicks)
        {
            var s = clicks[0];
            var e = clicks.Count > 1 ? clicks[1] : s;
            var c1 = clicks.Count > 2 ? clicks[2] : s;
            var c2 = clicks.Count > 3 ? clicks[3] : clicks.Count > 2 ? clicks[2] : e;
            return new List<PcbPoint> { s, c1, c2, e };
        }

        private int PointsNeeded => _tool switch
        {
            PcbTool.Arc => 3,
            PcbTool.Bezier => 4,
            PcbTool.Polygon => int.MaxValue,
            _ => 2
        };

        private void GraphicClick(double x, double y, bool doubleClick)
        {
            var p = Magnet(x, y);   // [magnet]
            if (_tool is PcbTool.Line or PcbTool.Polygon && _gfx.Count > 0) p = Angle45(_gfx[^1], p);   // [angle45]
            if (_tool == PcbTool.Polygon && (doubleClick || (_gfx.Count >= 3 && Near(_gfx[0], p))))
            {
                FinishGraphic();
                return;
            }

            if (_gfx.Count > 0 && Near(_gfx[^1], p)) return;   // a second click on the same spot
            _gfx.Add(p);
            if (_gfx.Count >= PointsNeeded) FinishGraphic();
            InvalidateVisual();
        }

        /// <summary>Enter: closes a polygon being drawn.</summary>
        private bool FinishGraphicByKey()
        {
            if (_tool != PcbTool.Polygon || _gfx.Count < 3) return false;
            FinishGraphic();
            return true;
        }

        /// <summary>Enter closes a polygon, Backspace takes the last point back, Esc drops the shape being drawn (a second Esc leaves the tool).</summary>
        private bool GraphicKey(System.Windows.Input.Key key)
        {
            switch (key)
            {
                case System.Windows.Input.Key.Enter:
                    return FinishGraphicByKey();
                case System.Windows.Input.Key.Back when _gfx.Count > 0:
                    _gfx.RemoveAt(_gfx.Count - 1);
                    InvalidateVisual();
                    return true;
                case System.Windows.Input.Key.Escape when _gfx.Count > 0:
                    _gfx.Clear();
                    Say(ToolHint());
                    InvalidateVisual();
                    return true;
                default:
                    return false;
            }
        }

        private void FinishGraphic()
        {
            var board = Board;
            if (board == null) return;
            var g = new BoardGraphic { Shape = ShapeOf(_tool), Layer = GraphicDrawLayer, Width = DrawWidth, Points = _tool == PcbTool.Bezier ? BezierPoints(_gfx) : _gfx.ToList(), Filled = DrawFilled && _tool is PcbTool.Rectangle or PcbTool.Circle or PcbTool.Polygon };
            var last = _gfx[^1];
            _gfx.Clear();
            if (!g.IsComplete || g.Shape == GraphicShape.Circle && g.Radius < 1e-3)
            {
                InvalidateVisual();
                return;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            board.Graphics.Add(g);
            Edited($"Çizim eklendi: {g.Describe()} · {LayerName(g.Layer)}");
            if (_tool == PcbTool.Line) _gfx.Add(last);   // chained lines, as KiCad draws them
            InvalidateVisual();
        }

        public static string LayerName(GraphicLayer l) => l switch
        {
            GraphicLayer.SilkTop => "Üst serigrafi",
            GraphicLayer.SilkBottom => "Alt serigrafi",
            GraphicLayer.FabTop => "Üst montaj (fab)",
            GraphicLayer.FabBottom => "Alt montaj (fab)",
            _ => "Çizimler"
        };

        private bool Near(PcbPoint a, PcbPoint b) => Math.Abs(a.X - b.X) * _scale < 4 && Math.Abs(a.Y - b.Y) * _scale < 4;

        private Color GraphicColor(GraphicLayer l) => l switch
        {
            GraphicLayer.SilkTop => A(Silk, (byte)0xC8),
            GraphicLayer.SilkBottom => A(Silk, (byte)0x68),
            GraphicLayer.FabTop or GraphicLayer.FabBottom => A(Drawing, (byte)0x90),
            _ => A(Drawing, (byte)0xC0)
        };

        private bool GraphicShown(BoardGraphic g) => g.Layer switch
        {
            GraphicLayer.SilkTop => ShowSilk && !HiddenLayers.Contains(CopperLayer.Top),
            GraphicLayer.SilkBottom => ShowSilk && !HiddenLayers.Contains(CopperLayer.Bottom),
            _ => ShowDrawings
        };

        private void DrawGraphics(DrawingContext dc, BoardDocument board)
        {
            foreach (var g in board.Graphics)
            {
                if (!GraphicShown(g)) continue;
                if (g.Shape == GraphicShape.Image)
                {
                    DrawImageGraphic(dc, g, 0.8);
                    continue;
                }

                var color = GraphicColor(g.Layer);
                if (g.Filled && g.Area().Count >= 3) dc.DrawGeometry(SymbolRenderer.Brush(color), null, Poly(g.Area()));
                var pen = P(color, Math.Max(1, g.Width * _scale));
                dc.DrawGeometry(null, pen, StrokePath(g.Strokes(0.5 / Math.Max(1, _scale))));
            }
        }

        /// <summary>
        /// The strokes as one path, joined where one ends and the next starts: a see-through pen laid over itself at every
        /// joint (each piece drawn alone) beads a curve with darker dots.
        /// </summary>
        private System.Windows.Media.Geometry StrokePath(List<Segment2> strokes)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                Point? end = null;
                foreach (var s in strokes)
                {
                    var a = S(s.X1, s.Y1);
                    if (end is not { } e || (e - a).LengthSquared > 1e-6) ctx.BeginFigure(a, false, false);
                    var b = S(s.X2, s.Y2);
                    ctx.LineTo(b, true, true);
                    end = b;
                }
            }

            geo.Freeze();
            return geo;
        }

        /// <summary>The shape being drawn, its last point following the cursor.</summary>
        private void DrawGraphicPreview(DrawingContext dc)
        {
            if (_tool == PcbTool.Image && PendingImageAt(_cursor.X, _cursor.Y) is { } ghost)
            {
                DrawImageGraphic(dc, ghost, 0.5);
                return;
            }

            if (!IsGraphicTool(_tool) || _gfx.Count == 0) return;
            var pts = _gfx.ToList();
            var cursor = Magnet(_cursor.X, _cursor.Y);   // [magnet]
            pts.Add(_tool is PcbTool.Line or PcbTool.Polygon ? Angle45(pts[^1], cursor) : cursor);   // [angle45]
            var g = new BoardGraphic { Shape = ShapeOf(_tool), Layer = GraphicDrawLayer, Width = DrawWidth, Points = _tool == PcbTool.Bezier ? BezierPoints(pts) : pts };
            var pen = new Pen(SymbolRenderer.Brush(A(Accent, (byte)0xD0)), Math.Max(1, DrawWidth * _scale)) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            if (_tool == PcbTool.Bezier && pts.Count >= 3)
            {
                // [bezier] the control points' handles, as KiCad shows them
                var handle = new Pen(SymbolRenderer.Brush(A(Accent, (byte)0x90)), 1) { DashStyle = DashStyles.Dot };
                handle.Freeze();
                dc.DrawLine(handle, S(g.Points[0].X, g.Points[0].Y), S(g.Points[1].X, g.Points[1].Y));
                dc.DrawLine(handle, S(g.Points[3].X, g.Points[3].Y), S(g.Points[2].X, g.Points[2].Y));
            }
            if (_tool == PcbTool.Arc && pts.Count == 2)
            {
                // centre and start: the radius line until the end is picked
                dc.DrawLine(pen, S(pts[0].X, pts[0].Y), S(pts[1].X, pts[1].Y));
            }
            else if (_tool == PcbTool.Polygon && pts.Count == 2)
            {
                dc.DrawLine(pen, S(pts[0].X, pts[0].Y), S(pts[1].X, pts[1].Y));
            }
            else
            {
                foreach (var s in g.Strokes(0.5 / Math.Max(1, _scale))) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }

            foreach (var p in _gfx) dc.DrawRectangle(SymbolRenderer.Brush(Accent), null, new Rect(S(p.X, p.Y) - new Vector(3, 3), new Size(6, 6)));
            Say(g.IsComplete ? $"{g.Describe()} · {LayerName(GraphicDrawLayer)}" : ToolHint());
        }

        private BoardGraphic? HitGraphic(double x, double y)
        {
            if (Board == null) return null;
            double tol = 4 / _scale;
            return Board.Graphics.AsEnumerable().Reverse().FirstOrDefault(g => GraphicShown(g) && g.DistanceTo(x, y) <= tol);
        }

        private void DrawSelectedGraphic(DrawingContext dc, BoardGraphic g)
        {
            if (g.Shape is GraphicShape.Image or GraphicShape.Table && g.Points.Count >= 2)
            {
                // [image] [table] the frame round it, not every stroke of its text thickened
                dc.DrawRectangle(null, P(A(Accent, (byte)0xC0), 2), new Rect(S(g.Points[0].X, g.Points[0].Y) - new Vector(2, 2), S(g.Points[1].X, g.Points[1].Y) + new Vector(2, 2)));
                return;
            }

            var pen = P(A(Accent, (byte)0xC0), Math.Max(1, g.Width * _scale) + 4);
            foreach (var s in g.Strokes(0.5 / Math.Max(1, _scale))) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
            foreach (var p in g.Points) dc.DrawRectangle(SymbolRenderer.Brush(Accent), null, new Rect(S(p.X, p.Y) - new Vector(3, 3), new Size(6, 6)));
        }

        // ---------------------------------------------------------------- [tuning] length tuner

        /// <summary>How the length tuner draws its meanders (the tool options bar sets them).</summary>
        public MeanderSettings Tuning { get; } = new MeanderSettings();

        /// <summary>A length the tuner aims at instead of the pair's or group's (mm); null: the pair or group decides.</summary>
        public double? TuneTarget { get; set; }

        private void TuneClick(double x, double y)
        {
            var board = Board;
            if (board == null) return;
            if (HitTrack(x, y) is not { } seg)
            {
                Say("Uzunluk ayarı: bir yol parçasına tıklayın");
                return;
            }

            var result = LengthTuning.Tune(board, seg, TuneTarget, Tuning);
            if (!result.Ok || result.Replacement == null)
            {
                Say(result.Message);
                return;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            LengthTuning.Apply(board, seg, result);
            double ps = LengthTuning.Delay(board, result.Net);
            Edited(result.Message + $" · gecikme {ps:0} ps");
        }

        // ---------------------------------------------------------------- [headsup] the view bar's actions

        /// <summary>Zooms about the canvas' centre (the view bar's + and −).</summary>
        public void ZoomBy(double factor) => Zoom(new Point(ActualWidth / 2, ActualHeight / 2), factor);

        /// <summary>Frames the selection (KiCad's "zoom to selection"); the whole board when nothing is selected.</summary>
        public void FitSelection()
        {
            var board = Board;
            if (board == null || ActualWidth < 10) return;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            void Add(double ax, double ay, double bx, double by)
            {
                x0 = Math.Min(x0, Math.Min(ax, bx));
                y0 = Math.Min(y0, Math.Min(ay, by));
                x1 = Math.Max(x1, Math.Max(ax, bx));
                y1 = Math.Max(y1, Math.Max(ay, by));
            }

            foreach (var o in _sel)
            {
                switch (o)
                {
                    case BoardComponent c when FootprintLibrary.Get(c.FootprintId) is { } fp:
                        var cy = c.Courtyard(fp);
                        Add(cy.MinX, cy.MinY, cy.MaxX, cy.MaxY);
                        break;
                    case Track t:
                        Add(t.X1, t.Y1, t.X2, t.Y2);
                        break;
                    case Via v:
                        Add(v.X - v.Diameter, v.Y - v.Diameter, v.X + v.Diameter, v.Y + v.Diameter);
                        break;
                    case BoardText t:
                    {
                        var tb = Bounds(RobotTwin.CoreSim.Pcb.Silk.TextStrokes(t));   // [textbox] the text as drawn, a box's lines and frame too
                        Add(tb.MinX, tb.MinY, tb.MaxX, tb.MaxY);
                        break;
                    }
                    case MountingHole h:
                        Add(h.X - h.Drill, h.Y - h.Drill, h.X + h.Drill, h.Y + h.Drill);
                        break;
                    case BoardGraphic g when g.IsComplete:
                        var gb = g.Bounds();
                        Add(gb.MinX, gb.MinY, gb.MaxX, gb.MaxY);
                        break;
                    case Zone z when !z.IsWholeBoard:
                        foreach (var p in z.Outline) Add(p.X, p.Y, p.X, p.Y);
                        break;
                    case BoardDimension d:
                        Add(d.X1, d.Y1, d.X2, d.Y2);
                        foreach (var l in d.Geometry().Lines) Add(l.X1, l.Y1, l.X2, l.Y2);   // [dims] its line out to the side and its leader too
                        break;
                }
            }

            if (x0 > x1)
            {
                Fit();
                return;
            }

            double w = Math.Max(2, x1 - x0), h2 = Math.Max(2, y1 - y0);
            _scale = Math.Max(1, Math.Min(160, Math.Min((ActualWidth - 160) / w, (ActualHeight - 160) / h2)));
            _offset = new Vector(ActualWidth / 2 - (x0 + x1) / 2 * _scale, ActualHeight / 2 - (y0 + y1) / 2 * _scale);
            InvalidateVisual();
        }

        // ---------------------------------------------------------------- [flyout] routing options

        /// <summary>The width new tracks are routed at (mm); null: each net's class decides (KiCad's "use net class width").</summary>
        public double? RouteWidth { get; set; }

        /// <summary>The via new vias get (diameter, drill; mm); null: the net's class decides.</summary>
        public (double Diameter, double Drill)? RouteVia { get; set; }

        /// <summary>A via of the net's class, or the one the options bar chose.</summary>
        public (double Diameter, double Drill) ViaFor(BoardDocument board, string net)
        {
            if (RouteVia is { } v) return v;
            var cls = board.ClassFor(net);
            return (cls.ViaDiameter, cls.ViaDrill);
        }

        // ---------------------------------------------------------------- via tool

        /// <summary>A via where clicked, on the net of the copper under it, checked against the clearance and drill rules (<see cref="PlaceVia"/>).</summary>
        private void ViaClick(double x, double y) => PlaceVia(x, y);
    }
}
