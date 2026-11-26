using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [shapes] KiCad's schematic graphics on the sheet: lines (click the corners, double click or Enter to finish),
    /// rectangles (two corners), circles (centre, then a point on it) and arcs (start, a point on it, end), drawn in the
    /// sheet's ink with a live preview; picked near their stroke (or inside when filled), then moved, turned, copied
    /// and deleted with the rest of the selection.
    /// </summary>
    public sealed partial class SchematicCanvas
    {
        private List<GridPoint>? _shapeDraft;

        private static bool IsShapeTool(EditorTool t) => t is EditorTool.Line or EditorTool.Rectangle or EditorTool.Circle or EditorTool.Arc;

        private static SheetShapeKind KindOf(EditorTool t) => t switch
        {
            EditorTool.Rectangle => SheetShapeKind.Rectangle,
            EditorTool.Circle => SheetShapeKind.Circle,
            EditorTool.Arc => SheetShapeKind.Arc,
            _ => SheetShapeKind.Line
        };

        private void ShapeClick(bool doubleClick)
        {
            var at = Snap(_mouseGrid);
            _shapeDraft ??= new List<GridPoint>();
            if (_tool == EditorTool.Line && doubleClick)
            {
                FinishShape();
                return;
            }

            if (_shapeDraft.Count == 0 || !_shapeDraft[_shapeDraft.Count - 1].Equals(at)) _shapeDraft.Add(at);
            int need = _tool switch { EditorTool.Rectangle => 2, EditorTool.Circle => 2, EditorTool.Arc => 3, _ => int.MaxValue };
            if (_shapeDraft.Count >= need) FinishShape();
            InvalidateVisual();
        }

        /// <summary>Adds the drawn shape (one undo step) when it is a real one; a shape too small to be one is dropped.</summary>
        private void FinishShape()
        {
            var pts = _shapeDraft;
            _shapeDraft = null;
            InvalidateVisual();
            if (pts == null) return;
            var shape = new SchematicShape { Kind = KindOf(_tool), Points = pts };
            bool real = shape.Kind switch
            {
                SheetShapeKind.Line => pts.Count >= 2,
                SheetShapeKind.Rectangle => pts.Count == 2 && pts[0].X != pts[1].X && pts[0].Y != pts[1].Y,
                SheetShapeKind.Circle => pts.Count == 2 && !pts[0].Equals(pts[1]),
                _ => pts.Count == 3 && shape.ArcCircle() != null
            };
            if (!real) return;
            W.BeginChange();
            Doc.Shapes.Add(shape);
            W.CommitChange(Shell.Lang.T("Çizim eklendi"));
        }

        private void DrawShapes(DrawingContext dc)
        {
            foreach (var s in Doc.Shapes) DrawShape(dc, s, _selection.Contains(s) ? Accent : TextColor);
            if (_shapeDraft is { Count: > 0 } draft && IsShapeTool(_tool))
            {
                var preview = new SchematicShape { Kind = KindOf(_tool), Points = draft.Concat(new[] { Snap(_mouseGrid) }).ToList(), Dashed = true };
                if (preview.Kind == SheetShapeKind.Arc && preview.Points.Count == 2) preview.Kind = SheetShapeKind.Line;   // until its middle is chosen
                DrawShape(dc, preview, Accent);
            }
        }

        private void DrawShape(DrawingContext dc, SchematicShape s, Color color)
        {
            var outline = s.Outline();
            if (outline.Count < 2) return;
            bool closed = s.Kind is SheetShapeKind.Rectangle or SheetShapeKind.Circle;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(ToScreen(outline[0]), s.Filled && closed, closed);
                g.PolyLineTo(outline.Skip(1).Select(ToScreen).ToList(), true, true);
            }

            geo.Freeze();
            var pen = new Pen(SymbolRenderer.Brush(color), Math.Max(1, s.Width / 2.54 * _scale)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (s.Dashed) pen.DashStyle = DashStyles.Dash;
            dc.DrawGeometry(s.Filled && closed ? SymbolRenderer.Brush(Color.FromArgb(0x55, color.R, color.G, color.B)) : null, pen, geo);
        }

        /// <summary>The topmost shape whose stroke passes within a few pixels of the point (or that contains it, when filled).</summary>
        private SchematicShape? HitShape(Point screen)
        {
            foreach (var s in Doc.Shapes.AsEnumerable().Reverse())
            {
                var pts = s.Outline().Select(ToScreen).ToList();
                double reach = Math.Max(5, s.Width / 2.54 * _scale / 2 + 3);
                for (int i = 0; i + 1 < pts.Count; i++)
                    if (Distance(screen, pts[i], pts[i + 1]) <= reach) return s;
                if (s.Filled && s.Kind is SheetShapeKind.Rectangle or SheetShapeKind.Circle && Inside(screen, pts)) return s;
            }

            return null;
        }

        private static double Distance(Point p, Point a, Point b)
        {
            var ab = b - a;
            double l2 = ab.LengthSquared;
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        private static bool Inside(Point p, List<Point> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            return inside;
        }
    }
}
