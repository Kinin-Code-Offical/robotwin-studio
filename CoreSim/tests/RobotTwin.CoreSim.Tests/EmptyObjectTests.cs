using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[empty] Unity's empty GameObject: a parent in the editor, nothing in the run.</summary>
    public class EmptyObjectTests
    {
        private static WorldObject Add(WorldDocument w, string name, WorldObjectKind kind, double x)
        {
            var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = kind, Shape = "box", SizeX = 0.02, SizeY = 0.02, SizeZ = 0.02, MassKg = 0.05, Pose = new WorldPose { X = x, Z = 0.01 } };
            w.Objects.Add(o);
            return o;
        }

        private static WorldJoint Mount(WorldDocument w, WorldObject a, WorldObject? b, WorldJointKind kind = WorldJointKind.Fixed)
        {
            var j = new WorldJoint { Id = w.NextId("J"), ObjectA = a.Id, ObjectB = b?.Id, Kind = kind, AnchorX = a.Pose.X, AnchorZ = 0.01 };
            w.Joints.Add(j);
            return j;
        }

        [Fact]
        public void PartsUnderAFreeEmptyStandFreeInTheRun()
        {
            var w = new WorldDocument();
            var group = Add(w, "Grup", WorldObjectKind.Empty, 0);
            var a = Add(w, "A", WorldObjectKind.Body, 0.05);
            var b = Add(w, "B", WorldObjectKind.Body, 0.10);
            Mount(w, a, group);
            Mount(w, b, group);

            var run = w.ForRun();
            Assert.Equal(new[] { "A", "B" }, run.Objects.Select(o => o.Name));
            Assert.Empty(run.Joints);   // not welded to each other, not to the ground
            Assert.Equal(3, w.Objects.Count);   // the editor keeps the group
            Assert.Equal(2, w.Joints.Count);
            Assert.Equal(2, w.MountedOn(group.Id).Count);   // moving the group moves both in the editor
        }

        [Fact]
        public void AnEmptyChildPassesWhatHangsOnItToItsParent()
        {
            var w = new WorldDocument();
            var arm = Add(w, "Kol", WorldObjectKind.Body, 0);
            var tcp = Add(w, "Uç noktası", WorldObjectKind.Empty, 0.05);
            var gripper = Add(w, "Tutucu", WorldObjectKind.Body, 0.06);
            Mount(w, tcp, arm);
            var hinge = Mount(w, gripper, tcp, WorldJointKind.Hinge);
            hinge.AxisZ = 1;

            var run = w.ForRun();
            var j = Assert.Single(run.Joints);
            Assert.Equal(gripper.Id, j.ObjectA);
            Assert.Equal(arm.Id, j.ObjectB);   // through the empty, onto the arm
            Assert.Equal(WorldJointKind.Hinge, j.Kind);   // its kind, anchor and axis stay
            Assert.Equal(1, j.AxisZ);
            Assert.Equal(tcp.Id, hinge.ObjectB);   // the editor's joint is untouched
        }

        [Fact]
        public void AnEmptyFixedToTheGroundHoldsItsPartsToTheGround()
        {
            var w = new WorldDocument();
            var anchor = Add(w, "Sabit nokta", WorldObjectKind.Empty, 0);
            var inner = Add(w, "İç grup", WorldObjectKind.Empty, 0);
            var part = Add(w, "Parça", WorldObjectKind.Body, 0.02);
            Mount(w, anchor, null);   // fixed to the ground
            Mount(w, inner, anchor);
            Mount(w, part, inner);

            var j = Assert.Single(w.ForRun().Joints);
            Assert.Equal(part.Id, j.ObjectA);
            Assert.Null(j.ObjectB);
        }

        [Fact]
        public void AnEmptyHasNoBodyAndNoVolume()
        {
            var w = new WorldDocument();
            var group = Add(w, "Grup", WorldObjectKind.Empty, 0);
            var a = Add(w, "A", WorldObjectKind.Body, 0.05);
            Mount(w, a, group);
            var geometry = new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default);
            var p = WorldPhysics.Build(w, geometry.GeometryOf, null);
            Assert.DoesNotContain(p.Scene.Bodies, b => b.Tag == group);
            Assert.Contains(p.Scene.Bodies, b => b.Tag == a);
            Assert.Empty(Interference.Find(w, geometry));
        }
    }
}
