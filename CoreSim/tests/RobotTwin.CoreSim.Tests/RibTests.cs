using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[rib] SolidWorks' rib as a feature: a gusset filled from its line to the part, editable afterwards.</summary>
    public class RibTests
    {
        /// <summary>An L bracket: a 40 mm floor and a 40 mm wall, both 5 mm thick, 20 mm wide (along Z).</summary>
        private static (ModelDocument Doc, ModelObject Bracket) Bracket()
        {
            var outline = new List<Vec2> { new Vec2(0, 0), new Vec2(40, 0), new Vec2(40, 5), new Vec2(5, 5), new Vec2(5, 40), new Vec2(0, 40) };
            var mesh = PrimitiveBuilder.Extrude(outline, new List<List<Vec2>>(), 20);
            var doc = new ModelDocument();
            return (doc, ModelOps.AddMesh(doc, mesh, "Braket"));
        }

        [Fact]
        public void ARibInTheCornerFillsTheTriangleToTheWalls()
        {
            var (_, bracket) = Bracket();
            double bare = bracket.LocalMesh().Volume();
            Assert.Equal(375 * 20, bare, 3);
            double z = (bracket.LocalMesh().Bounds.Min.Z + bracket.LocalMesh().Bounds.Max.Z) / 2;
            var f = ModelFeatures.AddRib(bracket, new Vec3(5, 30, z), new Vec3(30, 5, z), Vec3.UnitZ, 4);
            Assert.True(bracket.LocalMesh().IsClosed);
            Assert.Equal(bare + 25 * 25 / 2.0 * 4, bracket.LocalMesh().Volume(), 1);   // the triangle (5,5)-(30,5)-(5,30), 4 mm thick

            f.Thickness = 6;   // edited afterwards: thicker
            Assert.Equal(bare + 25 * 25 / 2.0 * 6, bracket.LocalMesh().Volume(), 1);
            f.Suppressed = true;
            Assert.Equal(bare, bracket.LocalMesh().Volume(), 3);
        }

        [Fact]
        public void TheRibGoesTowardsThePartWhicheverWayItsLineIsDrawn()
        {
            var (_, a) = Bracket();
            var (_, b) = Bracket();
            double z = (a.LocalMesh().Bounds.Min.Z + a.LocalMesh().Bounds.Max.Z) / 2;
            ModelFeatures.AddRib(a, new Vec3(5, 30, z), new Vec3(30, 5, z), Vec3.UnitZ, 4);
            ModelFeatures.AddRib(b, new Vec3(30, 5, z), new Vec3(5, 30, z), Vec3.UnitZ * -1, 4);   // drawn the other way, the normal flipped
            Assert.Equal(a.LocalMesh().Volume(), b.LocalMesh().Volume(), 1);
            Assert.True(a.LocalMesh().Bounds.Max.X <= 40 + 1e-6);   // never past the part's bounds
        }
    }
}
