using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[break] Unity's Break Torque: a bracket holding a cantilever lets go when the bending moment passes it.</summary>
    public class JointBreakTorqueTests
    {
        /// <summary>A 0.5 kg, 200 mm arm fixed to the ground at one end, 0.3 m up: about 0.49 N·m at the bracket. Its far end's height after a second.</summary>
        private static double Cantilever(double breakTorque)
        {
            var w = new WorldDocument();
            var arm = new WorldObject { Id = w.NextId("O"), Name = "Kol", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.2, SizeY = 0.02, SizeZ = 0.02, MassKg = 0.5, Pose = new WorldPose { X = 0.1, Z = 0.3 } };
            w.Objects.Add(arm);
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm.Id, ObjectB = null, AnchorX = 0, AnchorY = 0, AnchorZ = 0.31, BreakTorque = breakTorque });
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            return p.PoseOf(arm.Id)!.Value.Position.Z;
        }

        [Fact]
        public void ABracketHoldsBelowItsTorqueAndLetsGoAbove()
        {
            Assert.InRange(Cantilever(5), 0.29, 0.31);   // 0.49 N·m < 5: it holds
            Assert.True(Cantilever(0.2) < 0.1, "0.49 N·m > 0.2 N·m: it breaks and the arm falls");
            Assert.InRange(Cantilever(0), 0.29, 0.31);   // 0: never by torque
        }
    }
}
