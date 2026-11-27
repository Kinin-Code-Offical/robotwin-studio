using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[bezier] KiCad's Bézier curves on the sheet (bezier) and on the board (gr_curve).</summary>
    public class BezierTests
    {
        [Fact]
        public void TheCurveIsCutFinelyEnoughAndKeepsItsEnds()
        {
            var pts = CubicBezier.Flatten(0, 0, 10, 20, 30, -20, 40, 0, 0.01);
            Assert.Equal((0.0, 0.0), pts[0]);
            Assert.Equal((40.0, 0.0), pts[^1]);
            // every true point lies near the cut line
            for (int i = 0; i <= 400; i++)
            {
                var (x, y) = CubicBezier.At(0, 0, 10, 20, 30, -20, 40, 0, i / 400.0);
                double best = double.MaxValue;
                for (int k = 0; k + 1 < pts.Count; k++) best = Math.Min(best, Geometry.PointSegmentDistance(x, y, pts[k].X, pts[k].Y, pts[k + 1].X, pts[k + 1].Y));
                Assert.True(best <= 0.0101, $"{best} at t={i / 400.0}");
            }

            Assert.Equal(4 + 1, CubicBezier.Flatten(0, 0, 1, 0, 2, 0, 3, 0, 0.01).Count);   // a straight one needs no more than the least
        }

        [Fact]
        public void ASheetCurveIsDrawnPickedAndGoesToKiCadAndBack()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var b = SchematicEditing.Bounds(SchematicEditing.AllItems(sch), PartLibrary.Default)!.Value;
            var curve = new SchematicShape { Kind = SheetShapeKind.Bezier, Points = { new GridPoint(b.MaxX + 4, b.MaxY), new GridPoint(b.MaxX + 6, b.MaxY - 6), new GridPoint(b.MaxX + 10, b.MaxY + 6), new GridPoint(b.MaxX + 12, b.MaxY) } };
            sch.Shapes.Add(curve);
            var outline = curve.Outline();
            Assert.Equal(curve.Points[0], outline[0]);
            Assert.Equal(curve.Points[3], outline[^1]);
            var box = curve.Bounds();
            Assert.True(box.MaxY < b.MaxY + 6 && box.MinY > b.MaxY - 6);   // inside its control points' hull, short of them
            Assert.Contains(curve, SchematicEditing.ItemsInRect(sch, PartLibrary.Default, new GridRect(b.MaxX + 3, b.MaxY - 7, b.MaxX + 13, b.MaxY + 7), false));

            string text = KiCadExporter.ExportSchematic(sch, PartLibrary.Default, "curve");
            Assert.Contains("(bezier", text);
            var back = KiCadSchematicImport.Import(text, PartLibrary.Default).Schematic;
            var again = Assert.Single(back.Shapes, s => s.Kind == SheetShapeKind.Bezier);
            Assert.Equal(4, again.Points.Count);
        }

        [Fact]
        public void ABoardCurveIsStrokedForTheLegendAndWrittenAsAKiCadCurve()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            var curve = new BoardGraphic { Shape = GraphicShape.Bezier, Points = { new PcbPoint(5, 40), new PcbPoint(10, 30), new PcbPoint(20, 50), new PcbPoint(25, 40) } };
            Assert.True(curve.IsComplete);
            var strokes = curve.Strokes();
            Assert.True(strokes.Count >= 4);
            Assert.Equal(5, strokes[0].X1, 9);
            Assert.Equal(25, strokes[^1].X2, 9);
            Assert.Equal(0, curve.DistanceTo(15, 40), 1);   // it passes through its middle
            Assert.StartsWith("eğri", curve.Describe());
            board.Graphics.Add(curve);

            var silk = GerberExporter.SilkStrokes(board, CopperLayer.Top);
            Assert.True(silk.Count(s => s.Stroke.X1 >= 5 - 1e-9 && s.Stroke.X2 <= 25 + 1e-9 && s.Stroke.Y1 > 30 && s.Stroke.Y1 < 50) >= 4);
            string pcb = KiCadExporter.ExportPcb(board, "curve");
            Assert.Contains("(gr_curve", pcb);
            Assert.Equal(GraphicShape.Bezier, board.Clone().Graphics.Single().Shape);
        }
    }
}
