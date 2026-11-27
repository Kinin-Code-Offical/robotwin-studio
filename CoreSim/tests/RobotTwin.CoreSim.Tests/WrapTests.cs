using System;
using System.Linq;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[wrap] SolidWorks' wrap: a profile laid round a cylinder as an emboss or a deboss, a closed solid that follows the curve.</summary>
    public class WrapTests
    {
        [Fact]
        public void AnEmbossIsAClosedCurvedBandOfTheRightVolume()
        {
            var spec = PrimitiveSchema.Create(PrimitiveKind.Wrap);
            spec.Sketch!.Holes.Clear();
            spec.Sketch.Outer = SketchShape.Rect(30, 10);
            spec.Set("d", 40);
            spec.Set("depth", 2);
            var mesh = PrimitiveBuilder.Build(spec);
            var tri = mesh.Triangulate();
            Assert.True(tri.AnalyzeTopology().IsClosed);

            // every point on the inner (r − 0.1) or the outer (r + 2) cylinder
            foreach (var v in mesh.Vertices)
            {
                double rho = Math.Sqrt(v.X * v.X + v.Y * v.Y);
                Assert.True(Math.Abs(rho - 19.9) < 1e-6 || Math.Abs(rho - 22) < 1e-6, $"radius {rho}");
            }

            // the volume of the band: area / r × (r1² − r0²) / 2, the faces' chords taking a hair off it
            double expected = 30 * 10 / 20.0 * (22 * 22 - 19.9 * 19.9) / 2;
            Assert.InRange(Math.Abs(mesh.Volume()), expected * 0.99, expected * 1.001);
            Assert.InRange(mesh.Bounds.Max.Z - mesh.Bounds.Min.Z, 10 - 1e-6, 10 + 1e-6);   // the axis keeps the profile's height
        }

        [Fact]
        public void ADebossSinksAndAHoleStaysOpen()
        {
            var spec = PrimitiveSchema.Create(PrimitiveKind.Wrap);   // the default: a band with a window
            spec.Set("d", 30);
            spec.Set("depth", -1);
            var mesh = PrimitiveBuilder.Build(spec);
            Assert.True(mesh.Triangulate().AnalyzeTopology().IsClosed);
            double outer = mesh.Vertices.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y)), inner = mesh.Vertices.Min(v => Math.Sqrt(v.X * v.X + v.Y * v.Y));
            Assert.InRange(outer, 15.1 - 1e-6, 15.1 + 1e-6);   // a hair outside the surface, to cut it cleanly
            Assert.InRange(inner, 14 - 1e-6, 14 + 1e-6);
            // the window: nothing at the profile's centre (angle 0, height 0)
            Assert.DoesNotContain(mesh.Vertices, v => Math.Abs(Math.Atan2(v.Y, v.X)) < 0.05 && Math.Abs(v.Z) < 1.5);
        }
    }
}
