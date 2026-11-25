using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [stepper] A whole Z axis in a world: 12 V and 5 V supplies, a square wave on STEP, an A4988, a NEMA 17, a coupler to
    /// a T8×8 screw in a 608, a brass nut on a carriage riding two rods on LM8UUs. Nothing is joined by hand except the
    /// carriage's screws: the motor's steps lift it 8 mm a turn.
    /// </summary>
    public class StepperWorldTests
    {
        private readonly ITestOutputHelper _out;

        public StepperWorldTests(ITestOutputHelper output) => _out = output;

        internal static WorldDocument ZAxis(double stepRate = 200)
        {
            var w = new WorldDocument { Title = "Z ekseni" };
            WorldObject Component(string part, string? preset, string name, double x, double y, string parameters = "")
            {
                var o = new WorldObject { Id = w.NextId("O"), Name = name, Kind = WorldObjectKind.Component, PartId = part, PresetId = preset, Pose = new WorldPose { X = x, Y = y }, IsStatic = true };
                foreach (var pair in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=');
                    o.Parameters[kv[0]] = kv[1];
                }

                w.Objects.Add(o);
                return o;
            }

            WorldObject Put(string id, double x, double y, double z, bool frame = false)
            {
                var o = DriveParts.Add(w, id, new WorldPose { X = x, Y = y, Z = z });
                o.IsStatic = frame;
                return o;
            }

            void Wire(WorldObject a, string pa, WorldObject b, string pb) =>
                w.Cables.Add(new WorldCable { Id = w.NextId("C"), A = new CableEnd { ObjectId = a.Id, Pin = pa }, B = new CableEnd { ObjectId = b.Id, Pin = pb } });

            var vmot = Component("dc-source", null, "12 V", 0.15, 0.1, "voltage=12");
            var vdd = Component("dc-source", null, "5 V", 0.2, 0.1, "voltage=5");
            var gen = Component("signal-generator", null, "STEP", 0.25, 0.1, $"frequency={stepRate};amplitude=5;duty=0.1");
            var drv = Component("a4988", "a4988", "A4988", 0.15, -0.1);
            var motor = Component("stepper", "nema17-17hs4401", "NEMA 17", 0, 0);
            Wire(vmot, "+", drv, "VMOT");
            Wire(vmot, "-", drv, "GND");
            Wire(vdd, "+", drv, "VDD");
            Wire(vdd, "-", drv, "GND2");
            Wire(vdd, "+", drv, "DIR");
            Wire(gen, "+", drv, "STEP");
            Wire(gen, "-", drv, "GND2");
            Wire(drv, "1A", motor, "A+");
            Wire(drv, "1B", motor, "A-");
            Wire(drv, "2A", motor, "B+");
            Wire(drv, "2B", motor, "B-");
            // The mechanics, placed: the coupler over the motor's shaft (42–64 mm up) and the screw's foot.
            Put("coupler-5-8", 0, 0, 0.048);
            Put("screw-t8x8-300", 0, 0, 0.062);
            Put("bearing-608zz", 0, 0, 0.33, true);
            Put("shaft-8-300", 0, 0.035, 0.062, true);
            Put("shaft-8-300", 0, -0.035, 0.062, true);
            var nut = Put("nut-t8x8", 0, 0, 0.15);
            var lm1 = Put("lm8uu", 0, 0.035, 0.15);
            var lm2 = Put("lm8uu", 0, -0.035, 0.15);
            var plate = new WorldObject { Id = w.NextId("O"), Name = "Taşıyıcı", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.1, SizeZ = 0.024, MassKg = 0.2, Material = "aluminium", Pose = new WorldPose { X = 0.021, Z = 0.15 } };
            w.Objects.Add(plate);
            foreach (var part in new[] { nut, lm1, lm2 })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = part.Id, ObjectB = plate.Id, Fastening = "screws", AnchorX = part.Pose.X, AnchorY = part.Pose.Y, AnchorZ = part.Pose.Z });
            return w;
        }

        [Fact]
        public void StepsFromAGeneratorTurnTheMotorAndTheScrewLiftsTheCarriage()
        {
            var w = ZAxis();
            var report = DriveAssembly.Assemble(w);
            foreach (var line in report.Links.Concat(report.Problems)) _out.WriteLine(line);
            Assert.Empty(report.Problems);
            Assert.Contains(report.Links, l => l.Contains("motor miline"));
            Assert.Equal(1, report.Couplings);   // the screw drives the nut
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), new RunnerOptions { Firmware = false });
            var plate = w.Objects.Single(o => o.Name == "Taşıyıcı");
            double z0 = runner.Physics!.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z;
            while (runner.Time < 1.0 - 1e-6) runner.Step(0.01);
            double rise = runner.Physics.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z - z0;
            var stepper = runner.Circuit!.Compiled.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.StepperMotor>().Single();
            var rotor = runner.Physics.Scene.Joints.Single(j => j.Id.EndsWith(":shaft", StringComparison.Ordinal));
            _out.WriteLine($"1 s: motor {stepper.FullSteps:0.0} tam adım ({stepper.Angle / (2 * Math.PI):0.000} tur), rotor {rotor.Angle / (2 * Math.PI):0.000} tur, taşıyıcı {rise * 1000:0.00} mm, motor torku {stepper.Torque * 1000:0.0} mN·m");
            Assert.InRange(Math.Abs(stepper.FullSteps), 190, 202);
            Assert.InRange(Math.Abs(rotor.Angle - stepper.Angle), 0, 0.05);   // the shaft is where the electrical rotor is
            Assert.InRange(Math.Abs(rise), 0.0075, 0.0082);   // 8 mm a turn
        }
    }
}
