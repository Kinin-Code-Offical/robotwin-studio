using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[section] The part editor's section view: what stays of each object, its cut faces, its place in the world.</summary>
    public class SectionViewTests
    {
        [Fact]
        public void TheFrontIsCutAwayAndTheCutIsFilled()
        {
            var box = PrimitiveBuilder.Box(20, 20, 20);   // −10…10 on every axis
            var (mesh, caps) = SectionView.Cut(box, Affine.Identity, new SectionPlane { Axis = SectionAxis.X, Offset = 5 });
            Assert.Equal(15 * 20 * 20, mesh.Volume(), 6);
            Assert.NotEmpty(caps);
            Assert.All(caps, f => Assert.All(mesh.Faces[f], i => Assert.Equal(5, mesh.Vertices[i].X, 9)));
            Assert.Equal(400, caps.Sum(f => mesh.FaceNormalRaw(f).Length / 2), 6);   // the cut is the whole 20 × 20 face

            var (flipped, _) = SectionView.Cut(box, Affine.Identity, new SectionPlane { Axis = SectionAxis.X, Offset = 5, Flip = true });
            Assert.Equal(5 * 20 * 20, flipped.Volume(), 6);
        }

        [Fact]
        public void ThePlaneIsInTheWorldNotTheObject()
        {
            var box = PrimitiveBuilder.Box(20, 20, 20);
            var moved = Affine.Translate(new Vec3(0, 0, 30));   // the box spans z 20…40 in the world
            Assert.Equal(20 * 20 * 10, SectionView.Cut(box, moved, new SectionPlane { Axis = SectionAxis.Z, Offset = 30 }).Mesh.Volume(), 6);
            Assert.True(SectionView.Cut(box, moved, new SectionPlane { Axis = SectionAxis.Z, Offset = 10 }).Mesh.IsEmpty);   // wholly in front
            var whole = SectionView.Cut(box, moved, new SectionPlane { Axis = SectionAxis.Z, Offset = 50 });
            Assert.Equal(8000, whole.Mesh.Volume(), 6);
            Assert.Empty(whole.Caps);

            var doc = new ModelDocument();
            ModelOps.AddPrimitive(doc, PrimitiveKind.Box);
            var (lo, hi) = SectionView.Range(doc, SectionAxis.X);
            Assert.True(hi > lo);
        }
    }
}
