using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sweep] A profile swept along a path: a straight path is a prism, mitred bends keep the section (volume = area × path length).</summary>
    public class SweepTests
    {
        private static double RingArea(int n, double r) => 0.5 * n * r * r * Math.Sin(2 * Math.PI / n);

        [Fact]
        public void AStraightSweepIsACylinder()
        {
            var circle = SketchShape.Circle(10).ToPolygon(64);
            var m = PrimitiveBuilder.Sweep(circle, new List<Vec3> { new Vec3(0, 0, 0), new Vec3(0, 0, 30) }, 64);
            Assert.Equal(RingArea(64, 5) * 30, m.Volume(), 1);
            Assert.Equal(30, m.Bounds.Max.Z - m.Bounds.Min.Z, 6);
        }

        [Fact]
        public void AHandleKeepsItsSectionRoundTheBends()
        {
            var spec = PrimitiveSchema.Create(PrimitiveKind.Sweep);   // 10 mm bar: up 30, across 60, down 30
            spec.Set("seg", 64);
            var m = PrimitiveBuilder.Build(spec);
            double expected = RingArea(64, 5) * 120;
            Assert.InRange(m.Volume(), expected * 0.99, expected * 1.01);   // mitred: area × centre-line length
            Assert.Equal(70, m.Bounds.Max.X - m.Bounds.Min.X, 3);   // 60 across plus the bar's 10
            Assert.Equal(35, m.Bounds.Max.Z, 3);   // the top bar's top: 30 + 5
        }
    }
}
