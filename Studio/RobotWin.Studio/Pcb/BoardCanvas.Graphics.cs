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

        /// <summary>The layer the drawing tools draw on (the tool options bar sets it).</summary>
        public GraphicLayer GraphicDrawLayer { get; set; } = GraphicLayer.SilkTop;

        /// <summary>Line width of new shapes (mm).</summary>
        public double DrawWidth { get; set; } = 0.15;

        /// <summary>New rectangles, circles and polygons filled in.</summary>
        public bool DrawFilled { get; set; }

        public static bool IsGraphicTool(PcbTool t) => t is PcbTool.Line or PcbTool.Rectangle or PcbTool.Circle or PcbTool.Arc or PcbTool.Polygon;

        private static GraphicShape ShapeOf(PcbTool t) => t switch
        {
            PcbTool.Line => GraphicShape.Line,
            PcbTool.Rectangle => GraphicShape.Rectangle,
            PcbTool.Circle => GraphicShape.Circle,
            PcbTool.Arc => GraphicShape.Arc,
            _ => GraphicShape.Polygon
        };

        private int PointsNeeded => _tool switch
        {
            PcbTool.Arc => 3,
            PcbTool.Polygon => int.MaxValue,
            _ => 2
        };

        private void GraphicClick(double x, double y, bool doubleClick)
        {
            var p = new PcbPoint(Snap(x), Snap(y));
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
            var g = new BoardGraphic { Shape = ShapeOf(_tool), Layer = GraphicDrawLayer, Width = DrawWidth, Points = _gfx.ToList(), Filled = DrawFilled && _tool is PcbTool.Rectangle or PcbTool.Circle or PcbTool.Polygon };
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
                var color = GraphicColor(g.Layer);
                if (g.Filled && g.Area().Count >= 3) dc.DrawGeometry(SymbolRenderer.Brush(color), null, Poly(g.Area()));
                var pen = P(color, Math.Max(1, g.Width * _scale));
                foreach (var s in g.Strokes(0.5 / Math.Max(1, _scale))) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
            }
        }

        /// <summary>The shape being drawn, its last point following the cursor.</summary>
        private void DrawGraphicPreview(DrawingContext dc)
        {
            if (!IsGraphicTool(_tool) || _gfx.Count == 0) return;
            var pts = _gfx.ToList();
            pts.Add(new PcbPoint(Snap(_cursor.X), Snap(_cursor.Y)));
            var g = new BoardGraphic { Shape = ShapeOf(_tool), Layer = GraphicDrawLayer, Width = DrawWidth, Points = pts };
            var pen = new Pen(SymbolRenderer.Brush(A(Accent, (byte)0xD0)), Math.Max(1, DrawWidth * _scale)) { DashStyle = DashStyles.Dash };
            pen.Freeze();
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
            var pen = P(A(Accent, (byte)0xC0), Math.Max(1, g.Width * _scale) + 4);
            foreach (var s in g.Strokes(0.5 / Math.Max(1, _scale))) dc.DrawLine(pen, S(s.X1, s.Y1), S(s.X2, s.Y2));
            foreach (var p in g.Points) dc.DrawRectangle(SymbolRenderer.Brush(Accent), null, new Rect(S(p.X, p.Y) - new Vector(3, 3), new Size(6, 6)));
        }

        // ---------------------------------------------------------------- via tool

        /// <summary>A via where clicked, on the net of the track, pad or zone under it (none: it stays unconnected until routed).</summary>
        private void ViaClick(double x, double y)
        {
            var board = Board;
            if (board == null) return;
            double sx = Snap(x), sy = Snap(y);
            string net = HitTrack(x, y)?.Net ?? HitPad(x, y)?.Net
                         ?? board.Zones.FirstOrDefault(z => !string.IsNullOrEmpty(z.Net) && z.FilledRegions.Any(r => r.Contains(sx, sy)))?.Net
                         ?? HighlightNet ?? string.Empty;
            var cls = board.ClassFor(net);
            BeforeEdit?.Invoke(this, EventArgs.Empty);
            board.Vias.Add(new Via { X = sx, Y = sy, Net = net, Diameter = cls.ViaDiameter, Drill = cls.ViaDrill, StartLayer = CopperLayer.Top, EndLayer = CopperLayer.Bottom });
            Edited(net.Length > 0 ? $"Via eklendi ({net})" : "Via eklendi (hattı yok: yol bağlanınca alır)");
        }
    }
}
