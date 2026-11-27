using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[boundary] SolidWorks' boundary boss: a smooth solid through several profiles, square to its ends if asked.</summary>
    public class BoundaryTests
    {
        private static double Radius(RobotTwin.CoreSim.Mechanics.Vec3 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);

        [Fact]
        public void TheBottlePassesThroughEveryProfileAndIsClosed()
        {
            var spec = PrimitiveSchema.Create(PrimitiveKind.Boundary);
            var mesh = PrimitiveBuilder.Build(spec);
            Assert.True(mesh.Triangulate().AnalyzeTopology().IsClosed);
            Assert.InRange(mesh.Bounds.Max.Z - mesh.Bounds.Min.Z, 50 - 1e-6, 50 + 1e-6);
            var belly = mesh.Vertices.Where(v => Math.Abs(v.Z) < 1e-9).ToList();   // the middle profile, at the solid's centre
            Assert.NotEmpty(belly);
            Assert.All(belly, v => Assert.InRange(Radius(v), 19 - 1e-6, 19 + 1e-6));
            var neck = mesh.Vertices.Where(v => Math.Abs(v.Z - 25) < 1e-9).ToList();
            Assert.All(neck, v => Assert.InRange(Radius(v), 8 - 1e-6, 8 + 1e-6));
            var foot = mesh.Vertices.Where(v => Math.Abs(v.Z + 25) < 1e-9).ToList();
            Assert.All(foot, v => Assert.True(Math.Abs(v.X) <= 15 + 1e-6 && Math.Abs(v.Y) <= 15 + 1e-6));
            // square to the ends, the neck rises straight: just below it the bottle is still nearly the neck's width
            var underNeck = mesh.Vertices.Where(v => v.Z > 22 && v.Z < 24.999).ToList();
            Assert.NotEmpty(underNeck);
            Assert.True(underNeck.Max(Radius) < 9, $"under the neck {underNeck.Max(Radius)}");

            // running on at the ends, it curves through the profiles: fuller between belly and neck than a straight run
            var circles = new List<List<Vec2>> { SketchShape.Circle(30).ToPolygon(64), SketchShape.Circle(38).ToPolygon(64), SketchShape.Circle(16).ToPolygon(64) };
            var round = PrimitiveBuilder.Boundary(circles, new List<double> { 0, 25, 50 }, 10, false, 64);
            var between = round.Vertices.Where(v => Math.Abs(v.Z - 12.5) < 1e-6).ToList();
            Assert.NotEmpty(between);
            Assert.InRange(between.Average(Radius), 14.43, 14.45);   // the Catmull-Rom curve's value there; a straight run gives 13.5
        }

        [Fact]
        public void TwoProfilesRunStraightUnlessSquareToTheEnds()
        {
            var bottom = SketchShape.Circle(20).ToPolygon(64);
            var top = SketchShape.Circle(40).ToPolygon(64);
            var straight = PrimitiveBuilder.Boundary(new List<List<Vec2>> { bottom, top }, new List<double> { 0, 30 }, 10, false, 64);
            var loft = PrimitiveBuilder.Loft(bottom, top, 30, 0, 64);
            Assert.InRange(Math.Abs(straight.Volume()), Math.Abs(loft.Volume()) * 0.995, Math.Abs(loft.Volume()) * 1.005);

            // square to the ends: it leaves the foot straight up, so the first row is narrower than the straight run's
            var square = PrimitiveBuilder.Boundary(new List<List<Vec2>> { bottom, top }, new List<double> { 0, 30 }, 10, true, 64);
            double firstRow = square.Vertices.Where(v => Math.Abs(v.Z - (-15 + 3)) < 1e-6).Average(Radius);
            Assert.True(firstRow < 10 + 10 / 10.0 - 0.1, $"first row radius {firstRow}");
            Assert.True(square.Triangulate().AnalyzeTopology().IsClosed);
        }
    }
}
