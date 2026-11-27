using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[holes] Round holes found in meshes, and SolidWorks' Hole Alignment between stacked parts.</summary>
    public class HoleAlignmentTests
    {
        /// <summary>A 30 × 30 × 5 mm plate centred on the origin with Ø3.2 holes at the given centres (mm), in metres.</summary>
        private static MeshModel Plate(params (double X, double Y)[] holes)
        {
            var plate = PrimitiveBuilder.Box(30, 30, 5);
            foreach (var (x, y) in holes)
                plate = Csg.Difference(plate, PrimitiveBuilder.Cylinder(1.6, 8, 32).Translate(new Vec3(x, y, 0)));
            return plate.ToMeshModel();
        }

        private sealed class MeshGeometry : IWorldGeometry
        {
            public WorldBodyGeometry GeometryOf(WorldObject o) =>
                new WorldBodyGeometry { Min = new Vec3(-0.015, -0.015, -0.0025), Max = new Vec3(0.015, 0.015, 0.0025), Mesh = o.Mesh == null ? null : MeshModel.FromEmbedded(o.Mesh), Shape = "mesh" };

            public double CableLength(WorldCable cable) => 0;
        }

        [Fact]
        public void AHoleIsFoundWithItsAxisSizeAndDepthButABossIsNot()
        {
            var holes = HoleFinder.Find(Plate((5, 0), (-8, 6)));
            Assert.Equal(2, holes.Count);
            var h = holes.OrderByDescending(x => x.Center.X).First();
            Assert.InRange(h.Diameter * 1000, 3.15, 3.25);
            Assert.InRange(Math.Abs(h.Axis.Z), 0.999, 1.0);
            Assert.InRange(h.Depth * 1000, 4.95, 5.05);
            Assert.InRange(h.Center.X * 1000, 4.95, 5.05);
            Assert.InRange(Math.Abs(h.Center.Y * 1000), 0, 0.05);
            Assert.InRange(Math.Abs(h.Center.Z * 1000), 0, 0.05);

            Assert.Empty(HoleFinder.Find(PrimitiveBuilder.Cylinder(4, 10, 32).ToMeshModel()));   // a boss, not a hole
            Assert.Empty(HoleFinder.Find(PrimitiveBuilder.Box(10, 10, 10).ToMeshModel()));
        }

        private static (WorldDocument w, WorldObject a, WorldObject b) Stack(double offsetMm, double liftMm = 5)
        {
            var w = new WorldDocument();
            var a = w.AddModel(Plate((5, 0)), "alt.stl", new WorldPose());
            var b = w.AddModel(Plate((5 + offsetMm, 0)), "ust.stl", new WorldPose { Z = liftMm / 1000 });
            return (w, a, b);
        }

        [Fact]
        public void HolesSlightlyOutOfLineAreReportedLinedUpAndFarApartAreNot()
        {
            var (w, a, b) = Stack(0.4);
            var found = HoleAlignment.Find(w, new MeshGeometry());
            var m = Assert.Single(found);
            Assert.InRange(m.DeviationMm, 0.38, 0.42);
            Assert.InRange(m.DiameterAMm, 3.15, 3.25);
            Assert.Equal(new[] { a.Id, b.Id }.OrderBy(x => x), new[] { m.A, m.B }.OrderBy(x => x));
            Assert.InRange(m.PointA.Z * 1000, 2.4, 2.6);   // where the plates meet
            Assert.InRange(Math.Abs(m.PointA.Z - m.PointB.Z) * 1000, 0, 0.01);

            Assert.Empty(HoleAlignment.Find(w, new MeshGeometry(), new HoleAlignmentOptions { MaxDeviationMm = 0.3 }));
            Assert.Empty(HoleAlignment.Find(Stack(0).w, new MeshGeometry()));      // lined up
            Assert.Empty(HoleAlignment.Find(Stack(6).w, new MeshGeometry()));      // two separate holes
            Assert.Empty(HoleAlignment.Find(Stack(0.4, 25).w, new MeshGeometry()));   // far apart along the axis
            Assert.Single(HoleAlignment.Find(Stack(0.4, 5.8).w, new MeshGeometry()));  // a washer's gap
        }
    }
}
