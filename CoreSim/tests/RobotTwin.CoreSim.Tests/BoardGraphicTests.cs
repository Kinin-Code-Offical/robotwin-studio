using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[graphics] Drawn shapes on the PCB: their strokes, saved with the board, written to the Gerber legend and to KiCad, held by the silk DRC.</summary>
    public class BoardGraphicTests
    {
        private static BoardGraphic G(GraphicShape shape, params double[] xy)
        {
            var g = new BoardGraphic { Shape = shape };
            for (int i = 0; i + 1 < xy.Length; i += 2) g.Points.Add(new PcbPoint(xy[i], xy[i + 1]));
            return g;
        }

        [Fact]
        public void ShapesBecomeStrokesThatFollowTheCurve()
        {
            var line = G(GraphicShape.Line, 0, 0, 3, 4);
            Assert.Single(line.Strokes());
            Assert.Equal("çizgi 5 mm", line.Describe());

            var rect = G(GraphicShape.Rectangle, 0, 0, 10, 4);
            Assert.Equal(4, rect.Strokes().Count);
            Assert.Equal(28, rect.Strokes().Sum(s => Math.Sqrt((s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1))), 9);

            var circle = G(GraphicShape.Circle, 5, 5, 8, 5);
            Assert.Equal(3, circle.Radius, 9);
            foreach (var s in circle.Strokes(0.01))
            {
                double mx = (s.X1 + s.X2) / 2 - 5, my = (s.Y1 + s.Y2) / 2 - 5;
                Assert.InRange(Math.Sqrt(mx * mx + my * my), 3 - 0.0101, 3);   // a chord's middle strays at most the tolerance
            }

            double length = circle.Strokes(0.001).Sum(s => Math.Sqrt((s.X2 - s.X1) * (s.X2 - s.X1) + (s.Y2 - s.Y1) * (s.Y2 - s.Y1)));
            Assert.Equal(2 * Math.PI * 3, length, 1);

            var arc = G(GraphicShape.Arc, 0, 0, 2, 0, 0, 2);   // a quarter from +x to +y
            var (start, sweep) = arc.ArcAngles();
            Assert.Equal(0, start, 9);
            Assert.Equal(Math.PI / 2, sweep, 9);
            Assert.Equal("yay R2 mm, 90°", arc.Describe());

            Assert.Equal(0, G(GraphicShape.Polygon, 0, 0, 4, 0, 4, 4).DistanceTo(4, 2), 9);
            var filled = G(GraphicShape.Rectangle, 0, 0, 10, 10);
            filled.Filled = true;
            Assert.Equal(0, filled.DistanceTo(5, 5));
            Assert.False(G(GraphicShape.Polygon, 0, 0, 1, 1).IsComplete);
        }

        [Fact]
        public void TheyAreSavedWrittenToTheLegendAndToKiCadAndCheckedOverPads()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            AutoPlacer.Place(board);
            var rect = G(GraphicShape.Rectangle, 2, 2, 12, 8);
            rect.Filled = true;
            board.Graphics.Add(rect);
            board.Graphics.Add(new BoardGraphic { Shape = GraphicShape.Circle, Layer = GraphicLayer.FabTop, Points = { new PcbPoint(30, 30), new PcbPoint(32, 30) } });
            board.Graphics.Add(G(GraphicShape.Arc, 40, 20, 43, 20, 40, 23));
            board.Graphics.Add(G(GraphicShape.Polygon, 50, 5, 55, 5, 52, 9));

            var copy = board.Clone();
            Assert.Equal(4, copy.Graphics.Count);
            Assert.True(copy.Graphics[0].Filled);
            Assert.Equal(GraphicLayer.FabTop, copy.Graphics[1].Layer);

            // the legend: the filled rectangle's outline and its hatching are top silkscreen strokes; the fab circle is not
            var silk = GerberExporter.SilkStrokes(board, CopperLayer.Top);
            Assert.True(silk.Count(s => s.Stroke.X1 >= 2 - 1e-9 && s.Stroke.X2 <= 12 + 1e-9 && s.Stroke.Y1 >= 2 - 1e-9 && s.Stroke.Y1 <= 8 + 1e-9) > 4 + 10);
            Assert.DoesNotContain(silk, s => Math.Abs(s.Stroke.X1 - 32) < 1e-6 && Math.Abs(s.Stroke.Y1 - 30) < 1e-6);

            string pcb = KiCadExporter.ExportPcb(board, "deneme");
            Assert.Contains("(gr_rect", pcb);
            Assert.Contains("(fill solid)", pcb);
            Assert.Contains("(gr_circle", pcb);
            Assert.Contains("(gr_arc", pcb);
            Assert.Contains("(gr_poly", pcb);
            Assert.Contains("\"F.Fab\"", pcb);

            // a line drawn across a pad on the silkscreen is reported like the footprints' legend
            var pad = board.Pads().First(p => p.Pad.IsThroughHole);
            board.Graphics.Add(G(GraphicShape.Line, pad.X - 3, pad.Y, pad.X + 3, pad.Y));
            var issues = DesignRuleChecker.SilkOverPads(board);
            Assert.Contains(issues, i => i.Message.StartsWith("Serigrafi çizimi (çizgi 6 mm)", StringComparison.Ordinal));
        }
    }
}
