using System;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>The FEA mesh of a round part is round: its surface nodes lie on the true surface, not on grid steps.</summary>
    public class MeshRoundnessTests
    {
        private readonly ITestOutputHelper _out;

        public MeshRoundnessTests(ITestOutputHelper output) => _out = output;

        [Theory]
        [InlineData(0.0325, 0.013, 1500)]   // 65 × 26 mm robot wheel at the render's default mesh density
        [InlineData(0.0325, 0.013, 6000)]
        public void AWheelsMeshSurfaceLiesOnItsCylinder(double r, double hh, int cells)
        {
            var solid = new CylinderSolid(Vec3.Zero, r, hh);
            double vol = Math.PI * r * r * 2 * hh;
            double h = Math.Pow(vol / cells, 1.0 / 3);
            var mesh = TetMesh.FromSolid(solid, h, 2);
            var side = mesh.SurfaceNodes.Select(i => mesh.Nodes[i]).Where(p => Math.Abs(p.Z) < hh - 0.6 * mesh.ElementSize).ToList();
            var dev = side.Select(p => Math.Abs(Math.Sqrt(p.X * p.X + p.Y * p.Y) - r) / r).ToList();
            double worst = dev.Max(), mean = dev.Average();
            _out.WriteLine($"h {mesh.ElementSize * 1000:F2} mm, {mesh.Elements.Count} tets, side surface nodes {side.Count}, unsnapped {mesh.UnsnappedNodes}: radius error mean {mean * 100:F2} %, worst {worst * 100:F2} %");
            Assert.True(worst < 0.03, "every side surface node within 3 % of the radius: a round wheel, not a stair-stepped square");
        }

        /// <summary>
        /// A wheel's physics body is a Z cylinder turned onto its axle. The analysis geometry must carry that
        /// turn, or the render draws the wheel lying flat — a square slab instead of a disc.
        /// </summary>
        [Fact]
        public void AWheelsAnalysisGeometryStandsOnItsEdge()
        {
            var w = new RobotTwin.CoreSim.Sandbox.WorldDocument();
            var wheel = w.AddBody("wheel", 0.065, 0.026, 0.026, new RobotTwin.CoreSim.Sandbox.WorldPose { Z = 0.0325, Yaw = 30 });
            var geo = new RobotTwin.CoreSim.Automation.ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            var p = RobotTwin.CoreSim.Sandbox.WorldPhysics.Build(w, geo.GeometryOf, null);
            var part = RobotTwin.CoreSim.Analysis.AnalysisRun.PartOf(w, p.Scene, wheel.Id, p)!;
            var axle = part.BodyTurn.Rotate(Vec3.UnitZ);   // the cylinder's axis, in the wheel object's frame
            _out.WriteLine($"shape {part.Shape}, cylinder axis in the object frame ({axle.X:F3}, {axle.Y:F3}, {axle.Z:F3})");
            Assert.Equal("cylinder", part.Shape);
            Assert.True(Math.Abs(axle.Y) > 0.99, "the axis runs along the axle (object Y): the wheel stands on its edge");
        }
    }
}
