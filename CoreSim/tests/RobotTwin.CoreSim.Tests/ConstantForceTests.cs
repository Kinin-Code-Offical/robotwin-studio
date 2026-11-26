using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[cforce] Unity's Constant Force: a push and a twist on every step, along the world's or the part's axes.</summary>
    public class ConstantForceTests
    {
        /// <summary>A 0.2 kg, 5 cm cube held at 0.5 m (Z frozen, so only the constant force moves it), after 0.5 s.</summary>
        private static (WorldPhysics P, WorldObject O) Run(Action<WorldObject> setup, double seconds = 0.5)
        {
            var w = new WorldDocument();
            var o = new WorldObject { Id = w.NextId("O"), Name = "Küp", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.05, SizeY = 0.05, SizeZ = 0.05, MassKg = 0.2, Pose = new WorldPose { Z = 0.5 } };
            o.Parameters["rb.lockpos"] = "z";
            setup(o);
            w.Objects.Add(o);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            for (int i = 0; i < (int)Math.Round(seconds / 1e-3); i++) p.Advance(1e-3, null);
            return (p, o);
        }

        [Fact]
        public void AWorldPushAcceleratesThePartAsNewtonSays()
        {
            var (p, o) = Run(o => o.Parameters[WorldPhysics.ConstantForceKey] = "0.2,0,0");   // 1 m/s² on 0.2 kg
            var at = p.PoseOf(o.Id)!.Value.Position;
            Assert.InRange(at.X, 0.120, 0.127);   // ½·a·t² = 0.125 m, a little less for the air
            Assert.InRange(Math.Abs(at.Y), 0, 1e-4);
        }

        [Fact]
        public void APushAlongThePartsOwnAxesTurnsWithThePart()
        {
            var (p, o) = Run(o =>
            {
                o.Pose.Yaw = 90;   // its +X looks along the world's +Y
                o.Parameters[WorldPhysics.ConstantForceKey] = "0.2,0,0";
                o.Parameters[WorldPhysics.ConstantForceLocalKey] = "1";
            });
            var at = p.PoseOf(o.Id)!.Value.Position;
            Assert.InRange(at.Y, 0.120, 0.127);
            Assert.InRange(Math.Abs(at.X), 0, 1e-3);
        }

        [Fact]
        public void ATwistSpinsThePartAboutItsAxisOnly()
        {
            var (p, o) = Run(o =>
            {
                o.Parameters[WorldPhysics.ConstantForceKey] = "0,0,0";
                o.Parameters[WorldPhysics.ConstantTorqueKey] = "0,0,0.001";   // I = m·(a²+b²)/12 = 8.3e-5 kg·m², α = 12 rad/s²
            });
            var body = p.Scene.Bodies.First(b => !b.IsStatic && b.Tag == o);
            Assert.InRange(body.AngularVelocity.Z, 5.0, 6.1);   // α·t = 6 rad/s, less a little air
            Assert.InRange(Math.Abs(body.AngularVelocity.X) + Math.Abs(body.AngularVelocity.Y), 0, 1e-3);
            Assert.InRange(Math.Abs(p.PoseOf(o.Id)!.Value.Position.X), 0, 1e-4);   // a pure twist does not move it
        }

        [Fact]
        public void SwitchedOffItPushesNothingAndKeepsItsValues()
        {
            var (p, o) = Run(o =>
            {
                o.Parameters[WorldPhysics.ConstantForceKey] = "0.2,0,0";
                o.Parameters[WorldPhysics.ConstantForceLocalKey] = "1";
                WorldComponents.SetOn(o, WorldPhysics.ConstantForceKey, false);
            });
            Assert.InRange(Math.Abs(p.PoseOf(o.Id)!.Value.Position.X), 0, 1e-6);
            Assert.True(WorldComponents.Has(o, WorldPhysics.ConstantForceKey));
            WorldComponents.SetOn(o, WorldPhysics.ConstantForceKey, true);
            Assert.Equal("0.2,0,0", o.Parameters[WorldPhysics.ConstantForceKey]);
            Assert.Equal(2, WorldComponents.Copy(o, WorldPhysics.ConstantForceKey).Count);   // the push and its axes; the Rigidbody settings are not its
        }

        [Theory]
        [InlineData("1,2,3", true)]
        [InlineData("1.5; -2 ;0", true)]
        [InlineData("1 2 3", true)]
        [InlineData("1,2", false)]
        [InlineData("a,b,c", false)]
        [InlineData("", false)]
        public void VectorsReadAsThreeNumbers(string text, bool ok)
        {
            Assert.Equal(ok, WorldPhysics.TryVector(text, out var v));
            if (text == "1.5; -2 ;0") Assert.Equal(new Vec3(1.5, -2, 0), v);
            if (ok) Assert.True(WorldPhysics.TryVector(WorldPhysics.VectorText(v), out var back) && back.Equals(v));
        }
    }
}
