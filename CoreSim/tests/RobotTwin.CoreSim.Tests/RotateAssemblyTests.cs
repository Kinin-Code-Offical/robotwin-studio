using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[gizmo] Turning an assembly about any axis: places, orientations, and the joints' anchors and axes with them.</summary>
    public class RotateAssemblyTests
    {
        private static WorldObject Box(WorldDocument w, string name, double x, double y, double z)
        {
            var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.02, SizeZ = 0.02, Pose = new WorldPose { X = x, Y = y, Z = z } };
            w.Objects.Add(o);
            return o;
        }

        [Fact]
        public void AQuarterTurnAboutXCarriesThePartsAndTheirHinge()
        {
            var w = new WorldDocument();
            var arm = Box(w, "Kol", 0, 0, 0.1);
            var tip = Box(w, "Uç", 0, 0.05, 0.1);
            w.Joints.Add(new WorldJoint { ObjectA = tip.Id, ObjectB = arm.Id, Kind = WorldJointKind.Hinge, AnchorX = 0, AnchorY = 0.04, AnchorZ = 0.1, AxisX = 0, AxisY = 0, AxisZ = 1 });
            w.RotateAssembly(arm, Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2), arm.Pose.Position);

            Assert.Equal(0, tip.Pose.Y, 9);       // (0, 0.05) above the pivot turns to straight up
            Assert.Equal(0.15, tip.Pose.Z, 9);
            Assert.Equal(90, tip.Pose.Roll, 6);
            Assert.Equal(90, arm.Pose.Roll, 6);
            var j = w.Joints.Single();
            Assert.Equal(0.14, j.AnchorZ, 9);     // the hinge went with the part
            Assert.Equal(-1, j.AxisY, 9);         // its axis z turned to −y
            Assert.Equal(0, j.AxisZ, 9);
        }

        [Fact]
        public void TheYawOverloadStillTurnsAboutTheVerticalAndTurnsTheAxesToo()
        {
            var w = new WorldDocument();
            var a = Box(w, "A", 0.1, 0, 0);
            var b = Box(w, "B", 0.2, 0, 0);
            w.Joints.Add(new WorldJoint { ObjectA = b.Id, ObjectB = a.Id, Kind = WorldJointKind.Hinge, AnchorX = 0.15, AxisX = 1, AxisY = 0, AxisZ = 0 });
            w.RotateAssembly(a, 90);
            Assert.Equal(0.1, b.Pose.X, 9);
            Assert.Equal(0.1, b.Pose.Y, 9);
            Assert.Equal(90, b.Pose.Yaw, 6);
            var j = w.Joints.Single();
            Assert.Equal(0.05, j.AnchorY, 9);
            Assert.Equal(1, j.AxisY, 9);
        }
    }
}
