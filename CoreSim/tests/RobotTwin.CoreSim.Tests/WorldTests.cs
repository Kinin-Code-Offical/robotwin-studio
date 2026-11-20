using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public class WorldTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public WorldTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>Power board: 9 V battery on a 2-pin header (1 = +9V, 2 = GND).</summary>
        private static ProjectPackage PowerBoard()
        {
            var s = new Schematic { Title = "Güç kartı" };
            var bat = s.AddPart(Lib, "bat-9v", new GridPoint(0, 0));
            var j = s.AddPart(Lib, "hdr-1x2", new GridPoint(20, 0));
            VehicleTemplates.Tie(s, Lib, bat, "+", "VBAT");
            VehicleTemplates.Tie(s, Lib, bat, "-", "GND");
            VehicleTemplates.Tie(s, Lib, j, "1", "VBAT");
            VehicleTemplates.Tie(s, Lib, j, "2", "GND");
            return new ProjectPackage { Schematic = s };
        }

        /// <summary>LED board: header (1 = V+, 2 = GND), 470 Ω and a red LED.</summary>
        private static ProjectPackage LedBoard()
        {
            var s = new Schematic { Title = "LED kartı" };
            var j = s.AddPart(Lib, "hdr-1x2", new GridPoint(0, 0));
            var r = s.AddPart(Lib, "res-470", new GridPoint(20, 0));
            var led = s.AddPart(Lib, "led5-red", new GridPoint(40, 0));
            VehicleTemplates.Tie(s, Lib, j, "1", "VIN");
            VehicleTemplates.Tie(s, Lib, j, "2", "GND");
            VehicleTemplates.Tie(s, Lib, r, "A", "VIN");
            VehicleTemplates.Tie(s, Lib, r, "B", "LA");
            VehicleTemplates.Tie(s, Lib, led, "A", "LA");
            VehicleTemplates.Tie(s, Lib, led, "K", "GND");
            return new ProjectPackage { Schematic = s };
        }

        private static AnalogSimulator Run(WorldCircuit wc, double seconds, double dt = 1e-3)
        {
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            for (double t = 0; t < seconds; t += dt) sim.Step(dt);
            return sim;
        }

        [Fact]
        public void TwoDesignedBoardsWorkOnlyWhenCabledTogether()
        {
            var world = new WorldDocument();
            var power = world.AddBoard(world.AddDesign(PowerBoard()), new WorldPose { X = 0 });
            var leds = world.AddBoard(world.AddDesign(LedBoard()), new WorldPose { X = 0.3 });

            var dark = WorldCompiler.Compile(world, Lib);
            var simDark = Run(dark, 0.01);
            var ledDark = dark.DevicesOf(leds.Id, "D1").OfType<Led>().Single();
            Assert.True(Math.Abs(ledDark.Current) < 1e-6, $"no cable, LED current {ledDark.Current}");

            world.Connect(new CableEnd { ObjectId = power.Id, Pin = "J1.1" }, new CableEnd { ObjectId = leds.Id, Pin = "J1.1" });
            world.Connect(new CableEnd { ObjectId = power.Id, Pin = "J1.2" }, new CableEnd { ObjectId = leds.Id, Pin = "J1.2" }, color: "#222222");
            var lit = WorldCompiler.Compile(world, Lib, c => 0.3);
            foreach (var w in lit.Compiled.Warnings) _out.WriteLine("warn: " + w);
            var sim = Run(lit, 0.01);
            var led = lit.DevicesOf(leds.Id, "D1").OfType<Led>().Single();
            _out.WriteLine($"LED {led.Current * 1000:F2} mA");
            Assert.InRange(led.Current, 0.012, 0.018);   // (9 − 1.9) / 470 ≈ 15 mA

            // The cable drops I·R: 30 cm of AWG 24 ≈ 25 mΩ.
            var cable = lit.CableDevices.Values.First();
            Assert.InRange(cable.Resistance, 0.02, 0.03);
        }

        [Fact]
        public void FreeStandingBatteryDrivesFreeStandingMotorThroughCables()
        {
            var world = new WorldDocument();
            var bat = world.AddComponent(Lib, "bat-4aa", new WorldPose());
            var motor = world.AddComponent(Lib, "motor-tt", new WorldPose { X = 0.2 });
            world.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, awg: 22);
            world.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, awg: 22);
            var wc = WorldCompiler.Compile(world, Lib, c => 0.25);
            Run(wc, 0.5);
            var m = wc.Compiled.DevicesOf(wc.ComponentRef(motor.Id)!).OfType<DcMotor>().Single();
            _out.WriteLine($"motor {m.OutputRpm:F0} rpm, {m.Current * 1000:F0} mA");
            Assert.True(m.OutputRpm > 100, $"rpm {m.OutputRpm:F0}");
        }

        [Fact]
        public void ThinCableAcrossALipoBurnsOpen()
        {
            var world = new WorldDocument();
            var bat = world.AddComponent(Lib, "bat-2s-lipo", new WorldPose());
            var r = world.AddComponent(Lib, "res-1R-1W", new WorldPose { X = 0.1 });
            r.Parameters["power"] = "1000";   // the load survives; the wire is the weak link
            world.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = r.Id, Pin = "A" }, awg: 30);
            world.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = r.Id, Pin = "B" }, awg: 30);
            var wc = WorldCompiler.Compile(world, Lib, c => 0.5);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3, EnableDamage = true });
            sim.Initialize(InitialState.OperatingPoint);
            double burnedAt = double.NaN;
            for (double t = 0; t < 30 && double.IsNaN(burnedAt); t += 0.01)
            {
                sim.Step(0.01);
                if (wc.CableDevices.Values.Any(c => c.IsFailed)) burnedAt = sim.Time;
            }

            foreach (var e in sim.Events.Take(5)) _out.WriteLine(e.ToString());
            _out.WriteLine($"AWG30 ampacity {WorldCable.Ampacity(30):F2} A, burned at {burnedAt:F2} s");
            Assert.False(double.IsNaN(burnedAt), "an AWG 30 wire carrying ~7 A must burn");
        }

        [Fact]
        public void WorldFileKeepsBoardsFirmwareCablesAndJoints()
        {
            var world = new WorldDocument { Title = "Test dünyası" };
            var pkg = LedBoard();
            pkg.Firmware.Add(new FirmwareSource { BoardRefDes = "U1", Source = "void setup(){} void loop(){}" });
            var design = world.AddDesign(pkg, @"C:\x\led.rwproj");
            var a = world.AddBoard(design, new WorldPose { X = 0.1, Yaw = 90 });
            var b = world.AddBoard(design, new WorldPose { X = 0.4 });
            var servo = world.AddComponent(Lib, "servo-sg90", new WorldPose { Z = 0.05 });
            var arm = world.AddBody("box", 0.08, 0.01, 0.004, new WorldPose { Z = 0.08 });
            var tub = MeshModel.CreateBox(new Vec3(0.1, 0.05, 0.02));
            world.AddModel(tub, "kasa.stl", new WorldPose { Z = 0.2 });
            world.Connect(new CableEnd { ObjectId = a.Id, Pin = "J1.1" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" });
            world.Joints.Add(new WorldJoint { Id = world.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm.Id, ObjectB = servo.Id, PortB = "horn" });

            string path = Path.Combine(Path.GetTempPath(), "rw-world-" + Guid.NewGuid().ToString("N") + WorldDocument.Extension);
            try
            {
                world.Save(path);
                var back = WorldDocument.Load(path);
                Assert.Equal("Test dünyası", back.Title);
                Assert.Equal(5, back.Objects.Count);
                Assert.Single(back.Designs);
                Assert.Equal(3, back.Designs[0].Package.Schematic.Parts.Count);
                Assert.Equal("void setup(){} void loop(){}", back.Designs[0].Package.FirmwareFor("U1")!.Source);
                Assert.Equal(90, back.Find(a.Id)!.Pose.Yaw);
                Assert.Single(back.Cables);
                Assert.Equal("horn", back.Joints.Single().PortB);
                Assert.NotNull(back.Objects.Single(o => o.Kind == WorldObjectKind.Model).Mesh);
                Assert.Equal(2, back.Objects.Count(o => o.DesignId == design.Id));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void MountedPartsMoveAndTurnWithWhatTheyAreMountedOn()
        {
            var w = new WorldDocument();
            var servo = w.AddComponent(Lib, "servo-sg90", new WorldPose { X = 0.1 });
            var arm = w.AddBody("box", 0.1, 0.02, 0.003, new WorldPose { X = 0.15, Z = 0.03 });
            var tip = w.AddBody("sphere", 0.01, 0.01, 0.01, new WorldPose { X = 0.2, Z = 0.033 });
            var loose = w.AddBody("box", 0.05, 0.05, 0.05, new WorldPose { X = -0.3 });
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = arm.Id, ObjectB = servo.Id, PortB = "horn" });
            w.Joints.Add(new WorldJoint { Id = "J2", ObjectA = tip.Id, ObjectB = arm.Id });
            Assert.Equal(new[] { arm.Id, tip.Id }, w.MountedOn(servo.Id).Select(o => o.Id).ToArray());

            w.MoveAssembly(servo, 0.05, 0.02, 0);
            Assert.Equal(0.15, servo.Pose.X, 9);
            Assert.Equal(0.20, arm.Pose.X, 9);
            Assert.Equal(0.25, tip.Pose.X, 9);
            Assert.Equal(0.02, tip.Pose.Y, 9);
            Assert.Equal(-0.3, loose.Pose.X, 9);

            w.RotateAssembly(servo, 90);
            Assert.Equal(0.15, tip.Pose.X, 9);          // 0.1 m in +X turned into +Y around the servo
            Assert.Equal(0.12, tip.Pose.Y, 9);
            Assert.Equal(90, arm.Pose.Yaw, 9);
            Assert.Equal(0, loose.Pose.Yaw, 9);
        }

        [Fact]
        public void CableGaugeTablesArePhysical()
        {
            // AWG 24: 0.511 mm → 0.205 mm², 84 mΩ/m.
            Assert.InRange(WorldCable.CrossSection(24) * 1e6, 0.200, 0.210);
            Assert.InRange(WorldCable.Resistance(24, 1.0), 0.080, 0.088);
            Assert.InRange(WorldCable.Ampacity(24), 3.0, 4.0);
            Assert.InRange(WorldCable.Ampacity(12), 35, 45);
        }
    }
}
