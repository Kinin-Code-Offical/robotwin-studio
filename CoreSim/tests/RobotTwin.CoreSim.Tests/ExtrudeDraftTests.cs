using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[draft] An extrusion with a draft angle: its walls lean in (a moulded part's), its holes open up; zero changes nothing.</summary>
    public class ExtrudeDraftTests
    {
        private static List<Vec2> Square(double s, double cx = 0, double cy = 0) =>
            new List<Vec2> { new Vec2(cx - s / 2, cy - s / 2), new Vec2(cx + s / 2, cy - s / 2), new Vec2(cx + s / 2, cy + s / 2), new Vec2(cx - s / 2, cy + s / 2) };

        [Fact]
        public void AFiveDegreeDraftMakesAFrustum()
        {
            var m = PrimitiveBuilder.Extrude(Square(20), new List<List<Vec2>>(), 10, 5);
            double top = 20 - 2 * 10 * Math.Tan(5 * Math.PI / 180);
            var upper = m.Vertices.Where(v => v.Z > 0).ToList();
            Assert.Equal(top, upper.Max(v => v.X) - upper.Min(v => v.X), 6);
            double a1 = 400, a2 = top * top;
            Assert.Equal(10.0 / 3 * (a1 + a2 + Math.Sqrt(a1 * a2)), m.Volume(), 3);   // a square frustum
            Assert.Equal(4000, PrimitiveBuilder.Extrude(Square(20), new List<List<Vec2>>(), 10).Volume(), 6);   // no draft: a prism
        }

        [Fact]
        public void AHoleOpensUpTheWayTheWallsLean()
        {
            var hole = Square(6);
            hole.Reverse();
            var m = PrimitiveBuilder.Extrude(Square(20), new List<List<Vec2>> { hole }, 10, 3);
            double d = 10 * Math.Tan(3 * Math.PI / 180);
            var upperHole = m.Vertices.Where(v => v.Z > 0 && Math.Abs(v.X) < 8 && Math.Abs(v.Y) < 8).ToList();
            Assert.Equal(6 + 2 * d, upperHole.Max(v => v.X) - upperHole.Min(v => v.X), 6);   // the hole is wider at the top
            Assert.True(m.Volume() < 4000 - 360);   // less material than the straight part with its hole
        }
    }
}
