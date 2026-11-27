using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[constraints] Unity's aim, position, rotation and parent constraints: kept offsets, followed sources, levelled aim.</summary>
    public class WorldConstraintTests
    {
        private static WorldObject Box(WorldDocument w, double x, double y, double z, double yaw = 0) =>
            w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = x, Y = y, Z = z, Yaw = yaw });

        [Fact]
        public void APositionConstraintKeepsItsOffsetOnTheAxesItFollows()
        {
            var w = new WorldDocument();
            var source = Box(w, 0, 0, 0.1);
            var all = Box(w, 0.2, 0, 0.1);
            var onlyX = Box(w, 0.2, 0.4, 0.1);
            WorldConstraints.AddPosition(all, source);
            WorldConstraints.AddPosition(onlyX, source, "x");
            source.Pose.X = 0.5;
            source.Pose.Y = 0.3;
            Assert.Equal(2, WorldConstraints.Apply(w));
            Assert.Equal(0.7, all.Pose.X, 9);
            Assert.Equal(0.3, all.Pose.Y, 9);
            Assert.Equal(0.7, onlyX.Pose.X, 9);
            Assert.Equal(0.4, onlyX.Pose.Y, 9);   // Y does not follow
            Assert.Equal(0, WorldConstraints.Apply(w));   // settled
        }

        [Fact]
        public void ARotationConstraintTurnsWithItsSource()
        {
            var w = new WorldDocument();
            var source = Box(w, 0, 0, 0.1, yaw: 30);
            var o = Box(w, 0.3, 0, 0.1, yaw: 10);
            WorldConstraints.AddRotation(o, source);
            source.Pose.Yaw = 90;
            WorldConstraints.Apply(w);
            Assert.Equal(70, o.Pose.Yaw, 6);
            Assert.Equal(0.3, o.Pose.X, 9);   // it turns in place
        }

        [Fact]
        public void AParentConstraintCarriesThePartAndWhatIsMountedOnIt()
        {
            var w = new WorldDocument();
            var source = Box(w, 0, 0, 0.1);
            var o = Box(w, 0.1, 0, 0.1);
            var rider = Box(w, 0.1, 0, 0.2);
            w.Joints.Add(new WorldJoint { Id = "J1", Kind = WorldJointKind.Fixed, ObjectA = rider.Id, ObjectB = o.Id, AnchorX = 0.1, AnchorZ = 0.2 });
            WorldConstraints.AddParent(o, source);
            source.Pose.X = 1;
            source.Pose.Yaw = 90;
            WorldConstraints.Apply(w);
            Assert.Equal(1, o.Pose.X, 9);
            Assert.Equal(0.1, o.Pose.Y, 9);
            Assert.Equal(90, o.Pose.Yaw, 6);
            Assert.Equal(1, rider.Pose.X, 9);   // the part mounted on it came along
            Assert.Equal(0.1, rider.Pose.Y, 9);
            Assert.Equal(0.2, rider.Pose.Z, 9);
            Assert.Equal(0.1, w.Joints[0].AnchorY, 9);   // and its anchor
        }

        [Fact]
        public void AnAimPointsItsAxisAtTheTargetAndStaysLevel()
        {
            var w = new WorldDocument();
            var camera = Box(w, 0, 0, 0.2);
            var target = Box(w, 1, 1, 0.2);
            WorldConstraints.AddAim(camera, target);
            WorldConstraints.Apply(w);
            var forward = camera.Pose.Orientation.Rotate(new Vec3(1, 0, 0));
            Assert.Equal(1 / Math.Sqrt(2), forward.X, 6);
            Assert.Equal(1 / Math.Sqrt(2), forward.Y, 6);
            Assert.Equal(1, camera.Pose.Orientation.Rotate(new Vec3(0, 0, 1)).Z, 6);   // not rolled
            target.Pose.Z = 1.2;   // a target above: it tilts up to it, still not rolled sideways
            WorldConstraints.Apply(w);
            var f = camera.Pose.Orientation.Rotate(new Vec3(1, 0, 0));
            var toward = (target.Pose.Position - camera.Pose.Position).Normalized();
            Assert.Equal(1, f.Dot(toward), 6);
            Assert.Equal(0, camera.Pose.Orientation.Rotate(new Vec3(0, 1, 0)).Z, 6);
            Assert.Equal(1, WorldConstraints.AxisVector("-Z").Z * -1, 9);
        }

        [Fact]
        public void LoopsAreRefusedAndASwitchedOffConstraintDoesNothing()
        {
            var w = new WorldDocument();
            var a = Box(w, 0, 0, 0.1);
            var b = Box(w, 0.2, 0, 0.1);
            WorldConstraints.AddPosition(b, a);
            Assert.False(WorldConstraints.CanUse(w, a, b));   // a following b would follow itself
            Assert.True(WorldConstraints.CanUse(w, b, a));
            WorldComponents.SetOn(b, WorldConstraints.PositionKey, false);
            a.Pose.X = 3;
            Assert.Equal(0, WorldConstraints.Apply(w));
            Assert.Equal(0.2, b.Pose.X, 9);
            WorldComponents.SetOn(b, WorldConstraints.PositionKey, true);
            WorldConstraints.Apply(w);
            Assert.Equal(3.2, b.Pose.X, 9);
        }

        [Fact]
        public void CopiedTogetherTheCopyFollowsTheCopiedSource()
        {
            var w = new WorldDocument();
            var a = Box(w, 0, 0, 0.1);
            var b = Box(w, 0.2, 0, 0.1);
            WorldConstraints.AddAim(b, a);
            var made = WorldFragment.Take(w, new[] { a.Id, b.Id }).PasteInto(w, new Vec3(0, 1, 0));
            var copyA = w.Objects[w.Objects.Count - 2];
            var copyB = w.Objects[w.Objects.Count - 1];
            Assert.Equal(copyA.Id, copyB.Parameters[WorldConstraints.AimKey]);
            Assert.Equal(a.Id, b.Parameters[WorldConstraints.AimKey]);
            Assert.NotEmpty(made);
        }
    }
}
