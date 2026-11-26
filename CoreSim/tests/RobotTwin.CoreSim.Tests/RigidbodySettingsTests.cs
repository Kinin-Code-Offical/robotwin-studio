using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[rigidbody] Unity's Rigidbody settings in our solver: damping, frozen position and rotation axes.</summary>
    public class RigidbodySettingsTests
    {
        private static (PhysicsScene Scene, SceneBody Box) Float()
        {
            var scene = new PhysicsScene { Gravity = Vec3.Zero };
            var box = SceneBody.Box("b", new Vec3(0.05, 0.05, 0.05), 1, new Vec3(0, 0, 1));
            scene.AddBody(box);
            return (scene, box);
        }

        [Fact]
        public void LinearDampingTakesItsShareOfTheSpeedEachSecond()
        {
            var (scene, box) = Float();
            box.LinearDamping = 2;
            box.Velocity = new Vec3(1, 0, 0);
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            Assert.InRange(box.Velocity.X, Math.Exp(-2) * 0.98, Math.Exp(-2) * 1.02);   // v·e^(−c·t)

            var (s2, spin) = Float();
            spin.AngularDamping = 1;
            spin.AngularVelocity = new Vec3(0, 0, 4);
            for (int i = 0; i < 1000; i++) s2.Step(1e-3);
            Assert.InRange(spin.AngularVelocity.Z, 4 * Math.Exp(-1) * 0.98, 4 * Math.Exp(-1) * 1.02);
        }

        [Fact]
        public void FrozenAxesHoldTheirPlaceAndTurn()
        {
            var scene = new PhysicsScene();   // gravity on
            var box = SceneBody.Box("b", new Vec3(0.05, 0.05, 0.05), 1, new Vec3(0, 0, 1));
            scene.AddBody(box);
            box.LockPosition = 4;   // Z: it does not fall
            box.Velocity = new Vec3(0.5, 0, 0);
            box.LockRotation = 3;   // X and Y: it may only turn about Z
            box.AngularVelocity = new Vec3(2, 2, 2);
            for (int i = 0; i < 500; i++) scene.Step(1e-3);
            Assert.Equal(1, box.Position.Z, 9);
            Assert.InRange(box.Position.X, 0.24, 0.26);   // still free along X
            Assert.Equal(0, box.AngularVelocity.X, 9);
            Assert.Equal(0, box.AngularVelocity.Y, 9);
            Assert.True(box.AngularVelocity.Z > 1.5);
        }

        [Fact]
        public void APartsParametersReachItsBody()
        {
            var w = new WorldDocument();
            var o = new WorldObject { Id = w.NextId("O"), Name = "Raf", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.05, SizeY = 0.05, SizeZ = 0.05, MassKg = 0.2, Pose = new WorldPose { Z = 0.5 } };
            o.Parameters["rb.lockpos"] = "z";
            o.Parameters["rb.damping"] = "0.5";
            w.Objects.Add(o);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            var body = p.Scene.Bodies.First(b => !b.IsStatic && b.Tag == o);
            Assert.Equal(4, body.LockPosition);
            Assert.Equal(0.5, body.LinearDamping);
            for (int i = 0; i < 300; i++) p.Advance(1e-3, null);
            Assert.Equal(0.5, p.PoseOf(o.Id)!.Value.Position.Z, 4);   // held in the air
        }
    }
}
