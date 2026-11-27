using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[analysis] SolidWorks' Draft Analysis, Curvature and Symmetry Check on a body's faces.</summary>
    public class FaceAnalysisTests
    {
        private static readonly Affine Here = Affine.Translate(Vec3.Zero);

        [Fact]
        public void DraftSortsFacesByTheirAngleToThePull()
        {
            var box = PrimitiveBuilder.Box(20, 20, 10);
            var d = FaceAnalysis.Draft(box, Here, Vec3.UnitZ, 1);
            Assert.Equal(400, d.Area[FaceAnalysis.DraftPositive], 6);   // the top comes off
            Assert.Equal(400, d.Area[FaceAnalysis.DraftNegative], 6);   // the bottom is the other half's
            Assert.Equal(800, d.Area[FaceAnalysis.DraftRequired], 6);   // straight walls drag

            // a frustum narrowing upward: its wall has about 11° of draft, enough
            var cone = PrimitiveBuilder.Cone(10, 8, 10, 32);
            var c = FaceAnalysis.Draft(cone, Here, Vec3.UnitZ, 1);
            Assert.Equal(0, c.Area[FaceAnalysis.DraftRequired], 6);
            int wall = Enumerable.Range(0, cone.Faces.Count).First(f => Math.Abs(cone.FaceNormalRaw(f).Normalized().Z) < 0.9);
            Assert.InRange(FaceAnalysis.DraftAngle(cone, Here, wall, Vec3.UnitZ), 11, 11.6);
            Assert.True(FaceAnalysis.Draft(cone, Here, Vec3.UnitZ, 15).Area[FaceAnalysis.DraftRequired] > 0);   // not enough for 15°

            // the body turned upside down by its placement: the pull is the world's
            var flipped = FaceAnalysis.Draft(cone, Affine.Rotate(Vec3.UnitX, 180), Vec3.UnitZ, 1);
            Assert.Equal(c.Area[FaceAnalysis.DraftPositive], flipped.Area[FaceAnalysis.DraftNegative], 6);
        }

        [Fact]
        public void CurvatureBandsFacesByTheirRadius()
        {
            var box = FaceAnalysis.Curvature(PrimitiveBuilder.Box(20, 20, 10), Here);
            Assert.All(box.Class, c => Assert.Equal(0, c));   // flat everywhere: the edges are creases

            var rod = PrimitiveBuilder.Cylinder(5, 10, 48);
            var k = FaceAnalysis.Curvature(rod, Here);
            for (int f = 0; f < rod.Faces.Count; f++)
            {
                bool cap = Math.Abs(rod.FaceNormalRaw(f).Normalized().Z) > 0.9;
                Assert.Equal(cap ? 0 : 3, k.Class[f]);   // radius 5 mm: the 2…10 mm band
            }

            var drum = FaceAnalysis.Curvature(PrimitiveBuilder.Cylinder(30, 10, 64), Here);
            Assert.True(drum.Area[2] > 0 && drum.Area[3] == 0);   // radius 30 mm: the 10…50 mm band
        }

        [Fact]
        public void SymmetryFindsTheFacesWithoutAMirrorImage()
        {
            var box = PrimitiveBuilder.Box(20, 20, 10);
            var whole = FaceAnalysis.Symmetry(box, Here, Vec3.Zero, Vec3.UnitX);
            Assert.Equal(0, whole.Area[FaceAnalysis.Asymmetric], 6);

            var holed = Csg.Difference(PrimitiveBuilder.Box(30, 20, 5), PrimitiveBuilder.Cylinder(2, 8, 24).Translate(new Vec3(7, 0, 0)));
            var acrossX = FaceAnalysis.Symmetry(holed, Here, Vec3.Zero, Vec3.UnitX);
            Assert.True(acrossX.Area[FaceAnalysis.Asymmetric] > 0);   // the hole has no twin on the other side
            int wall = Enumerable.Range(0, holed.Faces.Count).First(f =>
                Math.Abs(holed.FaceNormalRaw(f).Normalized().Z) < 0.1 && (holed.FaceCenter(f) - new Vec3(7, 0, holed.FaceCenter(f).Z)).Length < 2.5);
            Assert.Equal(FaceAnalysis.Asymmetric, acrossX.Class[wall]);

            var acrossY = FaceAnalysis.Symmetry(holed, Here, Vec3.Zero, Vec3.UnitY);
            Assert.Equal(0, acrossY.Area[FaceAnalysis.Asymmetric], 6);   // the hole is on the plane
        }
    }
}
