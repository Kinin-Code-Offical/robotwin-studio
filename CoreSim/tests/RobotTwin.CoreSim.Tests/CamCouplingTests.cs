using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[cam] A cam on a turning shaft lifts its follower along its profile, carrying the follower's weight.</summary>
    public class CamCouplingTests
    {
        private static double LiftAfter(double seconds, int lobes)
        {
            var w = new WorldDocument();
            var shaft = new WorldObject { Id = "mil", Name = "mil", Kind = WorldObjectKind.Body, Shape = "cylinder", SizeX = 0.03, SizeY = 0.03, SizeZ = 0.01, Pose = new WorldPose { Z = 0.2 } };
            w.Objects.Add(shaft);
            var follower = new WorldObject { Id = "itici", Name = "itici", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.01, SizeY = 0.01, SizeZ = 0.05, MassKg = 0.1, Pose = new WorldPose { X = 0.1, Z = 0.2 } };
            w.Objects.Add(follower);
            var camJoint = new WorldJoint { Id = "j-mil", Kind = WorldJointKind.Hinge, ObjectA = "mil", AnchorZ = 0.2, AxisZ = 1, Drive = WorldJoint.DriveSpeedMode, DriveTarget = 90, DriveForce = 2 };
            var slide = new WorldJoint { Id = "j-itici", Kind = WorldJointKind.Slider, ObjectA = "itici", AnchorX = 0.1, AnchorZ = 0.2, AxisZ = 1 };
            w.Joints.Add(camJoint);
            w.Joints.Add(slide);
            w.Couplings = new() { new WorldCoupling { Id = "kam", Kind = WorldCoupling.Cam, JointA = camJoint.Id, JointB = slide.Id, SizeA = 10, SizeB = lobes } };
            w = WorldDocument.FromJson(w.ToJson());
            using var runner = new WorldRunner(w, new RunnerOptions { Firmware = false });
            while (runner.Time < seconds - 1e-6) runner.Step(0.005);
            return runner.Physics!.Scene.Joints.Single(s => s.Id == slide.Id).Angle * 1000;   // mm
        }

        [Fact]
        public void AnEccentricLiftsHalfWayAtAQuarterTurnAndAllTheWayAtHalf()
        {
            Assert.InRange(LiftAfter(1, 1), 4.5, 5.5);    // 90°: L/2
            Assert.InRange(LiftAfter(2, 1), 9.5, 10.5);   // 180°: the full 10 mm, against the follower's weight
        }

        [Fact]
        public void TwoLobesLiftTwiceATurn()
        {
            Assert.InRange(LiftAfter(1, 2), 9.5, 10.5);   // 90° on a two-lobe cam is its top
            Assert.InRange(LiftAfter(2, 2), -0.5, 0.5);   // and 180° back at the base circle
        }
    }
}
