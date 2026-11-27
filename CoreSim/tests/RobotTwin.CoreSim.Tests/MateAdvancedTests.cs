using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[mate-adv] SolidWorks' symmetric and width mates: mirror images in a plane; a tab centred in a groove.</summary>
    public class MateAdvancedTests
    {
        private static MateFeature Face(WorldObject o, double x, double y, double z, double nx, double ny, double nz) =>
            new MateFeature { Object = o.Id, Kind = MateFeatureKind.Plane, X = x, Y = y, Z = z, DX = nx, DY = ny, DZ = nz };

        private static MateFeature Point(WorldObject o) => new MateFeature { Object = o.Id, Kind = MateFeatureKind.Point };

        /// <summary>A fixed base whose plane x = 0 is the mirror, a fixed cube left of it, a free cube to place.</summary>
        private static (WorldDocument W, WorldObject Base, WorldObject Left, WorldObject Right) Scene()
        {
            var w = new WorldDocument();
            var bed = w.AddBody("box", 0.4, 0.3, 0.02, new WorldPose { Z = -0.01 });
            bed.IsStatic = true;
            var left = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = -0.1, Y = 0.02, Z = 0.05 });
            left.IsStatic = true;
            var right = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = 0.13, Y = -0.01, Z = 0.07 });
            return (w, bed, left, right);
        }

        [Fact]
        public void TwoPointsMirrorEachOtherInThePlane()
        {
            var (w, bed, left, right) = Scene();
            var (_, r) = WorldMates.Add(w, MateKind.Symmetric, Point(left), Point(right), c: Face(bed, 0, 0, 0, 1, 0, 0));
            Assert.Equal(0.1, right.Pose.X, 6);
            Assert.Equal(0.02, right.Pose.Y, 6);
            Assert.Equal(0.05, right.Pose.Z, 6);
            Assert.Equal(3, r.Freedom);   // a point is held; the cube may still turn about it
        }

        [Fact]
        public void TwoFacesMirrorEachOtherAndTheRestStaysFree()
        {
            var (w, bed, left, right) = Scene();
            right.Pose.Yaw = 8;
            // the left cube's right face (x = −0.075, outward +x) and the right cube's left face: mirror images in x = 0
            var (mate, r) = WorldMates.Add(w, MateKind.Symmetric, Face(left, 0.025, 0, 0, 1, 0, 0), Face(right, -0.025, 0, 0, -1, 0, 0), c: Face(bed, 0, 0, 0, 1, 0, 0));
            var n = right.Pose.Orientation.Rotate(new Vec3(-1, 0, 0));
            Assert.Equal(-1, n.X, 6);   // squared up to face the mirror
            Assert.Equal(0.075, right.Pose.ToWorld(new Vec3(-0.025, 0, 0)).X, 6);
            Assert.Equal(3, r.Freedom);   // it may still slide in its plane and turn about its normal
            Assert.Equal(MateKind.Symmetric, WorldDocument.FromJson(w.ToJson()).Mates!.Single().Kind);
            Assert.NotNull(WorldDocument.FromJson(w.ToJson()).Mates!.Single().C);
            Assert.NotNull(mate.Clone().C);
        }

        [Fact]
        public void ATabSitsInTheMiddleOfAGroove()
        {
            var w = new WorldDocument();
            var channel = w.AddBody("box", 0.2, 0.1, 0.02, new WorldPose());
            channel.IsStatic = true;
            var tab = w.AddBody("box", 0.04, 0.02, 0.03, new WorldPose { Y = 0.015, Z = 0.03, Yaw = 10 });
            // the groove's inner faces at y = ±0.03 face each other; the tab's faces at its own y = ±0.01
            var (_, r) = WorldMates.Add(w, MateKind.Width,
                Face(channel, 0, -0.03, 0, 0, 1, 0), Face(tab, 0, -0.01, 0, 0, -1, 0), c: Face(channel, 0, 0.03, 0, 0, -1, 0), d: Face(tab, 0, 0.01, 0, 0, 1, 0));
            Assert.Equal(0, right(tab).Y, 6);
            var side = tab.Pose.Orientation.Rotate(new Vec3(0, 1, 0));
            Assert.Equal(1, Math.Abs(side.Y), 6);   // its faces square to the groove's
            Assert.Equal(3, r.Freedom);   // along the groove, up and down, and turning about the groove's normal
            static Vec3 right(WorldObject o) => o.Pose.Position;
        }

        [Fact]
        public void AWidthTakesFourFacesAndASymmetricAPlane()
        {
            var w = new WorldDocument();
            var a = w.AddBody("box", 0.1, 0.1, 0.02, new WorldPose());
            var b = w.AddBody("box", 0.1, 0.1, 0.02, new WorldPose { X = 0.3 });
            var tab = w.AddBody("box", 0.02, 0.02, 0.02, new WorldPose { X = 0.15 });
            var m = new WorldMate { Kind = MateKind.Width, A = Face(a, 0, 0, 0, 1, 0, 0), B = Face(tab, -0.01, 0, 0, -1, 0, 0), C = Face(b, 0, 0, 0, -1, 0, 0) };
            Assert.False(WorldMates.Fits(m));   // the tab's other face is missing
            m.D = Face(tab, 0.01, 0, 0, 1, 0, 0);
            Assert.True(WorldMates.Fits(m));   // the groove's faces on two walls
            m.D.Kind = MateFeatureKind.Axis;
            Assert.False(WorldMates.Fits(m));
            Assert.False(WorldMates.Fits(new WorldMate { Kind = MateKind.Symmetric, A = Point(a), B = Point(b) }));   // no plane
        }
    }
}
