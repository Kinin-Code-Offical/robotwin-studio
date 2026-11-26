using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[bearing-heat] A bearing's friction heats the parts it joins: a plastic bushing far more than a ball bearing.</summary>
    public class BearingHeatTests
    {
        private readonly ITestOutputHelper _out;

        public BearingHeatTests(ITestOutputHelper output) => _out = output;

        /// <summary>A steel disc spun at 20 rev/s on a vertical shaft in this bearing for 20 s: its heat now and its temperature.</summary>
        private (double Heat, double Expected, double Temperature) Spin(string bearing)
        {
            var world = new WorldDocument();
            var disc = new WorldObject
            {
                Id = "disc", Name = "disc", Kind = WorldObjectKind.Body, Shape = "cylinder", Material = "ÇELİK",
                SizeX = 0.1, SizeY = 0.1, SizeZ = 0.02, Pose = new WorldPose { Z = 0.3 }
            };
            world.Objects.Add(disc);
            world.Joints.Add(new WorldJoint
            {
                Id = "shaft", Kind = WorldJointKind.Hinge, ObjectA = disc.Id, AnchorZ = 0.3, AxisZ = 1,
                Drive = WorldJoint.DriveSpeedMode, DriveTarget = 7200, DriveForce = 5, Bearing = bearing, BearingBore = 20
            });
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            while (runner.Time < 20 - 1e-6) runner.Step(0.01);
            var physics = runner.Physics!;
            var joint = physics.Scene.Joints.Single(j => j.Id == "shaft");
            double mass = physics.Scene.Bodies.First(b => b.Id == "disc" || b.Id.StartsWith("disc", StringComparison.Ordinal)).Mass;
            double expected = Bearings.Find(bearing)!.Mu * mass * 9.81 * 0.020 / 2 * Math.Abs(joint.Speed);   // μ·W·d/2·ω
            return (physics.BearingHeat("shaft"), expected, physics.TemperatureOf("disc"));
        }

        [Fact]
        public void ABushingHeatsWhatItSitsInAsItsFrictionTimesItsSpeed()
        {
            var plastic = Spin("plastic");
            var ball = Spin("ball");
            _out.WriteLine($"plastic bushing: {plastic.Heat:F3} W (expected {plastic.Expected:F3}), disc {plastic.Temperature:F3} °C; ball bearing: {ball.Heat:F4} W, disc {ball.Temperature:F3} °C");
            Assert.InRange(plastic.Heat, plastic.Expected * 0.95, plastic.Expected * 1.05);
            Assert.InRange(ball.Heat, ball.Expected * 0.95, ball.Expected * 1.05);
            Assert.True(plastic.Temperature - 25 > 20 * (ball.Temperature - 25), "a bushing at 100× the friction warms the disc far more");
            Assert.True(plastic.Temperature > 25.02, $"the disc warmed: {plastic.Temperature:F3} °C");
        }
    }
}
