using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[spring] A joint's own spring and damper: static deflection, natural period, critical damping, beside a motor.</summary>
    public class SpringTests
    {
        private readonly ITestOutputHelper _out;

        public SpringTests(ITestOutputHelper output) => _out = output;

        /// <summary>1 kg hanging on a 1000 N/m spring along a vertical slider; its height over time.</summary>
        private static double[] Hang(double damping)
        {
            var scene = new PhysicsScene();
            var mass = SceneBody.Box("m", new Vec3(0.05, 0.05, 0.05), 1.0, new Vec3(0, 0, 0.5));
            scene.AddBody(mass);
            var slide = scene.AddJoint(JointKind.Slider, mass, null, new Vec3(0, 0, 0.5), Vec3.UnitZ, "z");
            slide.SpringStiffness = 1000;
            slide.SpringDamping = damping;
            var x = new double[1000];
            for (int i = 0; i < x.Length; i++)
            {
                scene.Step(1e-3);
                x[i] = slide.Position;
            }

            return x;
        }

        [Fact]
        public void AMassOnASpringSagsByItsWeightAndSwingsAtItsNaturalPeriod()
        {
            const double k = 1000, m = 1;
            double sag = -m * 9.81 / k, period = 2 * Math.PI * Math.Sqrt(m / k);
            var free = Hang(0);
            // Upward crossings of the rest point: one a period.
            var ups = Enumerable.Range(1, free.Length - 1).Where(i => free[i - 1] < sag && free[i] >= sag).ToList();
            double measured = (ups[^1] - ups[0]) * 1e-3 / (ups.Count - 1);
            var damped = Hang(2 * Math.Sqrt(k * m));   // critical
            _out.WriteLine($"denge {sag * 1000:0.00} mm; kritik sönümle 1 s sonra {damped[^1] * 1000:0.000} mm; serbest periyot {measured * 1000:0.0} ms (kuram {period * 1000:0.0}); en alt {free.Min() * 1000:0.00} mm");
            Assert.InRange(damped[^1], sag - 1e-4, sag + 1e-4);
            Assert.InRange(measured, period * 0.98, period * 1.02);
            Assert.InRange(free.Min(), 2 * sag * 1.05, 2 * sag * 0.9);   // dropped from rest: down to twice the sag
            Assert.True(damped.Min() > sag * 1.01, "critically damped: no overshoot");
        }

        [Fact]
        public void AMotorHoldsAHingeAgainstItsReturnSpring()
        {
            var world = new WorldDocument();
            var lever = new WorldObject { Id = "lever", Name = "lever", Kind = WorldObjectKind.Body, Shape = "cylinder", SizeX = 0.05, SizeY = 0.05, SizeZ = 0.01, Pose = new WorldPose { Z = 0.2 } };
            world.Objects.Add(lever);
            var j = new WorldJoint
            {
                Id = "pivot", Kind = WorldJointKind.Hinge, ObjectA = lever.Id, AnchorZ = 0.2, AxisZ = 1,
                Drive = WorldJoint.DrivePosition, DriveTarget = 90, DriveForce = 1,
                SpringStiffness = 0.1, SpringRest = 0   // 0.1 N·m a radian back toward 0°
            };
            world.Joints.Add(j);
            world = WorldDocument.FromJson(world.ToJson());
            Assert.Equal(0.1, world.Joints[0].SpringStiffness);
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            while (runner.Time < 1.5 - 1e-6) runner.Step(0.01);
            var scene = runner.Physics!.Scene.Joints.Single(s => s.Id == "pivot");
            var spring = runner.Physics.SpringState("pivot")!.Value;
            _out.WriteLine($"açı {scene.Angle * 180 / Math.PI:0.0}°, motor torku {scene.DriveTorque:0.0000} N·m, yay {spring.Load:0.0000} N·m");
            Assert.InRange(scene.Angle, Math.PI / 2 - 0.01, Math.PI / 2 + 0.01);
            Assert.InRange(scene.DriveTorque, 0.1 * Math.PI / 2 - 0.004, 0.1 * Math.PI / 2 + 0.004);   // the motor carries k·θ
            Assert.InRange(spring.Load, -0.1 * Math.PI / 2 - 0.004, -0.1 * Math.PI / 2 + 0.004);
            Assert.InRange(spring.Position, 89.4, 90.6);
        }
    }
}
