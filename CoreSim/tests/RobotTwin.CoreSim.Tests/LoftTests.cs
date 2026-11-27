using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[loft] A loft between two profiles: closed, the right size at each end, a plain prism when the two are one.</summary>
    public class LoftTests
    {
        [Fact]
        public void ASquareIntoACircleIsAClosedFunnel()
        {
            var spec = PrimitiveSchema.Create(PrimitiveKind.Loft);
            var m = PrimitiveBuilder.Build(spec);
            var low = m.Vertices.Where(v => v.Z < 0).ToList();
            var high = m.Vertices.Where(v => v.Z > 0).ToList();
            Assert.Equal(30, low.Max(v => v.X) - low.Min(v => v.X), 6);   // the 30 mm square below
            Assert.InRange(high.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y)), 9.9, 10.0001);   // the 20 mm circle above
            double v = m.Volume();
            Assert.InRange(v, Math.PI * 100 * 30, 900 * 30);   // between the two prisms
            Assert.True(m.Volume() > 0);
        }

        [Fact]
        public void TwoEqualProfilesMakeAPrismAndATwistTurnsTheTop()
        {
            var square = new SketchShape { Kind = SketchShapeKind.Rectangle, Width = 30, Height = 30 }.ToPolygon(16);
            var prism = PrimitiveBuilder.Loft(square, square, 30, 0, 64);
            Assert.Equal(900 * 30, prism.Volume(), 3);

            var twisted = PrimitiveBuilder.Loft(square, square, 30, 45, 64);
            var top = twisted.Vertices.Where(v => v.Z > 0).ToList();
            Assert.Equal(15 * Math.Sqrt(2), top.Max(v => v.X), 4);   // the square's corner turned onto the X axis
            Assert.True(twisted.Volume() < prism.Volume());   // a twisted square is waisted in the middle
        }
    }
}
