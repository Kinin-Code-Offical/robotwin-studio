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

        /// <summary>[image] The picture waiting to be placed and its size in grid steps.</summary>
        private (string Data, double W, double H)? _pendingImage;

        /// <summary>[image] Starts placing a picture (base64 PNG) at its 300 ppi size: it follows the cursor until a click puts it down.</summary>
        public void BeginImage(string data)
        {
            var (w, h) = EmbeddedImage.SizeMm(data);
            if (w <= 0 || h <= 0) return;
            _pendingImage = (data, w / 2.54, h / 2.54);
            Tool = EditorTool.Image;
            InvalidateVisual();
        }

        private SchematicShape? PendingImageAt(GridPoint centre) => _pendingImage is not { } p ? null : new SchematicShape
        {
            Kind = SheetShapeKind.Image, ImageData = p.Data,
            Points = { new GridPoint(centre.X - p.W / 2, centre.Y - p.H / 2), new GridPoint(centre.X + p.W / 2, centre.Y + p.H / 2) }
        };

        private void PlaceImage()
        {
            if (PendingImageAt(Snap(_mouseGrid)) is not { } image) return;
            _pendingImage = null;
            W.BeginChange();
            Doc.Shapes.Add(image);
            W.CommitChange(Shell.Lang.T("Resim eklendi"));
            Tool = EditorTool.Select;
            SelectItems(new object[] { image });
        }

        private static bool IsShapeTool(EditorTool t) => t is EditorTool.Line or EditorTool.Rectangle or EditorTool.Circle or EditorTool.Arc or EditorTool.Bezier;

        private static SheetShapeKind KindOf(EditorTool t) => t switch
        {
            EditorTool.Rectangle => SheetShapeKind.Rectangle,
            EditorTool.Circle => SheetShapeKind.Circle,
            EditorTool.Arc => SheetShapeKind.Arc,
            EditorTool.Bezier => SheetShapeKind.Bezier,
            _ => SheetShapeKind.Line
        };

        /// <summary>[bezier] The curve's points from the clicks (start, end, first control, second control), the cursor standing in for those still to come.</summary>
        private static List<GridPoint> BezierPoints(IReadOnlyList<GridPoint> clicks)
        {
            var s = clicks[0];
            var e = clicks.Count > 1 ? clicks[1] : s;
            var c1 = clicks.Count > 2 ? clicks[2] : s;
            var c2 = clicks.Count > 3 ? clicks[3] : clicks.Count > 2 ? clicks[2] : e;
            return new List<GridPoint> { s, c1, c2, e };
        }

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
            int need = _tool switch { EditorTool.Rectangle => 2, EditorTool.Circle => 2, EditorTool.Arc => 3, EditorTool.Bezier => 4, _ => int.MaxValue };
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
            var shape = new SchematicShape { Kind = KindOf(_tool), Points = _tool == EditorTool.Bezier && pts.Count == 4 ? BezierPoints(pts) : pts };
            bool real = shape.Kind switch
            {
                SheetShapeKind.Line => pts.Count >= 2,
                SheetShapeKind.Rectangle => pts.Count == 2 && pts[0].X != pts[1].X && pts[0].Y != pts[1].Y,
                SheetShapeKind.Circle => pts.Count == 2 && !pts[0].Equals(pts[1]),
                SheetShapeKind.Bezier => pts.Count == 4 && !pts[0].Equals(pts[1]),
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
            if (_tool == EditorTool.Image && PendingImageAt(Snap(_mouseGrid)) is { } ghost)
            {
                dc.PushOpacity(0.6);
                DrawShape(dc, ghost, Accent);
                dc.Pop();
            }

            if (_shapeDraft is { Count: > 0 } draft && IsShapeTool(_tool))
            {
                var preview = new SchematicShape { Kind = KindOf(_tool), Points = draft.Concat(new[] { Snap(_mouseGrid) }).ToList(), Dashed = true };
                if (preview.Kind == SheetShapeKind.Arc && preview.Points.Count == 2) preview.Kind = SheetShapeKind.Line;   // until its middle is chosen
                if (preview.Kind == SheetShapeKind.Bezier)
                {
                    if (preview.Points.Count == 2) preview.Kind = SheetShapeKind.Line;   // until its end is chosen
                    else
                    {
                        // the control points' handles, as KiCad shows them
                        var bp = BezierPoints(preview.Points);
                        var handle = new Pen(SymbolRenderer.Brush(Color.FromArgb(0x90, Accent.R, Accent.G, Accent.B)), 1) { DashStyle = DashStyles.Dot };
                        dc.DrawLine(handle, ToScreen(bp[0]), ToScreen(bp[1]));
                        dc.DrawLine(handle, ToScreen(bp[3]), ToScreen(bp[2]));
                        preview.Points = bp;
                    }
                }
                DrawShape(dc, preview, Accent);
            }
        }

        private void DrawShape(DrawingContext dc, SchematicShape s, Color color)
        {
            if (s.Kind == SheetShapeKind.Image && s.Points.Count >= 2)
            {
                // [image] the picture in its frame; picked, its edge in the accent
                var r = new Rect(ToScreen(s.Points[0]), ToScreen(s.Points[1]));
                if (Theme.Pictures.Decode(s.ImageData) is { } bitmap) dc.DrawImage(bitmap, r);
                else dc.DrawRectangle(null, MakePen(color, 1, false), r);
                if (color == Accent) dc.DrawRectangle(null, MakePen(Accent, 1.5, false), r);
                return;
            }

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
                if ((s.Filled && s.Kind is SheetShapeKind.Rectangle or SheetShapeKind.Circle || s.Kind == SheetShapeKind.Image) && Inside(screen, pts)) return s;
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
