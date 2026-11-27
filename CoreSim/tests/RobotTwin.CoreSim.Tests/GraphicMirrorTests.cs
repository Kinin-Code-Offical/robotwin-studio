using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mirror] [rotate] Board drawings mirrored and turned a quarter keep their shape.</summary>
    public class GraphicMirrorTests
    {
        private static BoardGraphic Arc() => new BoardGraphic
        {
            Shape = GraphicShape.Arc,
            Points = { new PcbPoint(10, 10), new PcbPoint(15, 10), new PcbPoint(10, 5) }   // centre, start, end
        };

        [Fact]
        public void MirroredArcCoversTheMirroredStretch()
        {
            var arc = Arc();
            var (_, sweep0) = arc.ArcAngles();
            var before = arc.Strokes(0.01).SelectMany(s => new[] { (s.X1, s.Y1), (s.X2, s.Y2) }).ToList();
            arc.MirrorX(20);
            var (_, sweep1) = arc.ArcAngles();
            Assert.Equal(sweep0, sweep1, 6);   // the same quarter, not the other three quarters
            Assert.Equal(30, arc.Points[0].X, 9);
            var after = arc.Strokes(0.01).SelectMany(s => new[] { (s.X1, s.Y1), (s.X2, s.Y2) }).ToList();
            // every point of the mirrored arc is a mirrored point of the original
            foreach (var (x, y) in after)
                Assert.Contains(before, p => Math.Abs(40 - p.Item1 - x) < 0.05 && Math.Abs(p.Item2 - y) < 0.05);
        }

        [Fact]
        public void MirrorTwiceIsTheSame()
        {
            var poly = new BoardGraphic { Shape = GraphicShape.Polygon, Points = { new PcbPoint(1, 1), new PcbPoint(4, 1), new PcbPoint(2, 3) } };
            var start = poly.Points.ToList();
            poly.MirrorX(7.5);
            poly.MirrorX(7.5);
            Assert.Equal(start, poly.Points);
        }

        [Fact]
        public void QuarterTurnsGoRoundAndBack()
        {
            var line = new BoardGraphic { Shape = GraphicShape.Line, Points = { new PcbPoint(5, 0), new PcbPoint(8, 0) } };
            line.RotateQuarter(0, 0, counterClockwise: false);   // clockwise on screen: +x goes to +y (y down)
            Assert.Equal(0, line.Points[0].X, 9);
            Assert.Equal(5, line.Points[0].Y, 9);
            line.RotateQuarter(0, 0, counterClockwise: true);
            Assert.Equal(5, line.Points[0].X, 9);
            Assert.Equal(0, line.Points[0].Y, 9);
            for (int i = 0; i < 4; i++) line.RotateQuarter(3, 2, false);
            Assert.Equal(8, line.Points[1].X, 9);
            Assert.Equal(0, line.Points[1].Y, 9);
        }

        [Fact]
        public void TurnedArcKeepsItsSweepAndRadius()
        {
            var arc = Arc();
            double r = arc.Radius;
            var (_, sweep0) = arc.ArcAngles();
            arc.RotateQuarter(0, 0, false);
            Assert.Equal(r, arc.Radius, 9);
            Assert.Equal(sweep0, arc.ArcAngles().Sweep, 6);
        }
    }
}
