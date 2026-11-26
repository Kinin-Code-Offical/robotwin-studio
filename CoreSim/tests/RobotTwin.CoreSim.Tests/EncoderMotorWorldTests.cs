using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[encoder] Encoder gearmotors turning their wheels in a headless run, as they do in the app.</summary>
    public class EncoderMotorWorldTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public EncoderMotorWorldTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// The sensor rover's drive (Studio's "sensor-rover" template): two JGA25 encoder motors under an aluminium plate,
        /// 65 mm wheels on their shafts, a ball caster; the left motor straight from 4×AA, the right through 10 Ω.
        /// </summary>
        private static (WorldDocument W, WorldObject Plate, WorldObject Left) Rover(double plateWidth)
        {
            var w = new WorldDocument { Title = "rover" };
            w.Environment = new WorldSpec { Width = 3.0, Depth = 2.4, Walls = true, WallHeight = 0.4, Friction = 0.9, Track = "none" };
            const double r = 0.0325, shaftZ = 0.0125, motorH = 0.025, ball = 0.02;
            double motorZ = r - shaftZ, plateZ = motorZ + motorH;
            var plate = w.AddBody("plate", 0.24, plateWidth, 0.004, new WorldPose { Y = -0.35, Z = plateZ }, "ALÜMİNYUM");
            var ml = w.AddComponent(Lib, "motor-jga25-encoder", new WorldPose { X = -0.05, Y = -0.35 + 0.07, Z = motorZ });
            var mr = w.AddComponent(Lib, "motor-jga25-encoder", new WorldPose { X = -0.05, Y = -0.35 - 0.07, Z = motorZ, Yaw = 180 });
            var wl = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.05, Y = ml.Pose.Y + 0.0095 + 0.013 });
            var wr = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.05, Y = mr.Pose.Y - 0.0095 - 0.013 });
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = 0.09, Y = -0.35 }, "ÇELİK");
            var post = w.AddBody("box", 0.014, 0.014, plateZ - ball * 0.8, new WorldPose { X = 0.09, Y = -0.35, Z = ball * 0.8 }, "PLA");
            double top = plateZ + 0.004;
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = 0.03, Y = -0.4, Z = top });
            var res = w.AddComponent(Lib, "res-10R-2W", new WorldPose { X = 0.03, Y = -0.29, Z = WorldExamples.OnPins(top) });
            foreach (var o in new[] { ml, mr, caster, post, bat, res }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wl.Id, ObjectB = ml.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wr.Id, ObjectB = mr.Id, PortB = "shaft" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = ml.Id, Pin = "M1" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = ml.Id, Pin = "M2" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = res.Id, Pin = "A" });
            w.Connect(new CableEnd { ObjectId = res.Id, Pin = "B" }, new CableEnd { ObjectId = mr.Id, Pin = "M2" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = mr.Id, Pin = "M1" });
            return (w, plate, ml);
        }

        private (double Travel, double Load) Drive(double plateWidth, double seconds)
        {
            var (w, plate, left) = Rover(plateWidth);
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), new RunnerOptions { Firmware = false });
            var body = runner.Physics!.Scene.Bodies.Single(b => b.Id == plate.Id);
            var start = body.Position;
            while (runner.Time < seconds - 1e-9) runner.Step(0.01);
            var motor = runner.Circuit!.DevicesOf(left.Id, "M1").OfType<RobotTwin.CoreSim.Analog.Devices.DcMotor>().Single();
            double travel = (body.Position - start).Length;
            _out.WriteLine($"şasi {plateWidth * 1000:0} mm geniş: {seconds} s'de {travel * 1000:0} mm; sol motor {motor.AngularVelocity / motor.GearRatio * 60 / (2 * Math.PI):0} dev/dk, yük {motor.LoadTorque * 1000:0} mN·m");
            return (travel, motor.LoadTorque);
        }

        [Fact]
        public void EncoderMotorsDriveTheirWheelsHeadless()
        {
            // 6 V on a 12 V, 100 rpm motor: about 50 rpm at the wheel, 0.17 m/s on 65 mm wheels.
            var (travel, load) = Drive(0.15, 1.0);
            Assert.InRange(travel, 0.10, 0.25);
            Assert.InRange(Math.Abs(load), 0, 0.05);
        }

        [Fact]
        public void AWheelRubbingTheChassisEdgeBindsTheDrive()
        {
            // A 160 mm plate reaches 0.5 mm past the wheels' inner faces: the wheels rub its edge and the motors stall.
            var (travel, load) = Drive(0.16, 1.0);
            Assert.InRange(travel, 0, 0.05);
            Assert.True(Math.Abs(load) > 0.3);
        }
    }
}
