using System;
using System.Diagnostics;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public class MechatronicsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public MechatronicsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static (CompiledCircuit Circuit, AnalogSimulator Sim, MechatronicSystem Mech) Start(Schematic sch)
        {
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var mech = new MechatronicSystem(sch.Assembly!, sch, Lib, compiled, sim);
            return (compiled, sim, mech);
        }

        private static void Run(AnalogSimulator sim, MechatronicSystem mech, double seconds, Action<double>? each = null)
        {
            const double dt = 1e-3;
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++)
            {
                sim.Step(dt);
                mech.Advance(dt);
                each?.Invoke(sim.Time);
            }
        }

        [Fact]
        public void BodyFallsAndRestsOnTheFloor()
        {
            var world = new World3D(Arena.Create("empty"));
            world.Body.Mass = 0.2;
            world.Body.Inertia = new Vec3(1e-4, 1e-4, 1e-4);
            foreach (double x in new[] { -0.05, 0.05 })
            {
                foreach (double y in new[] { -0.05, 0.05 }) world.Body.ContactPoints.Add(new Vec3(x, y, 0));
            }

            world.Body.Position = new Vec3(0, 0, 0.3);
            for (int i = 0; i < 2000; i++) world.Step(1e-3);
            Assert.InRange(world.Body.Position.Z, -0.003, 0.0);
            Assert.True(world.Body.Velocity.Length < 1e-3);
            var up = world.Body.Orientation.Rotate(Vec3.UnitZ);
            Assert.True(up.Z > 0.999);
        }

        [Fact]
        public void AssemblyRoundTripsThroughProjectFile()
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            var back = Schematic.FromJson(sch.ToJson());
            Assert.NotNull(back.Assembly);
            Assert.Equal(VehicleKind.Rover, back.Assembly!.Kind);
            Assert.Equal(2, back.Assembly.Motors.Count);
            Assert.Equal(2, back.Assembly.Sensors.Count(s => s.Kind == SensorKind.Line));
            Assert.Null(back.Assembly.StartX);
        }

        [Fact]
        public void QuadcopterTakesOffAndHoldsAltitude()
        {
            var sch = VehicleTemplates.MicroQuadcopter(Lib);
            Assert.Equal(4, sch.Assembly!.Motors.Count);
            var (_, sim, mech) = Start(sch);
            Assert.NotNull(mech.Flight);
            _out.WriteLine($"mass {mech.TotalMass * 1000:F1} g, hover duty {mech.Flight!.HoverDuty:F2}, rate gain {mech.Flight.RateGain:F2}");

            var watch = Stopwatch.StartNew();
            Run(sim, mech, 0.2);
            mech.Flight.Mode = FlightMode.AltitudeHold;
            mech.Flight.Throttle = 0.5;
            mech.Flight.TargetAltitude = 0.8;
            mech.Flight.Armed = true;
            double maxTilt = 0;
            int k = 0;
            Run(sim, mech, 5.0, t =>
            {
                if (++k % 250 == 0) _out.WriteLine($"t {t:F2} z {mech.Body.Position.Z:F3} vz {mech.Body.Velocity.Z:F2} cmd {mech.Flight.Command[0]:F2} I {sim.Circuit.Devices.OfType<Battery>().First().Current:F2}");
                if (t > 1.5) maxTilt = Math.Max(maxTilt, Math.Max(Math.Abs(mech.Flight.Roll), Math.Abs(mech.Flight.Pitch)));
            });

            var events = mech.DrainEvents();
            foreach (var e in events) _out.WriteLine($"{e.Time:F2} {e.Source}: {e.Message}");
            var bat = sim.Circuit.Devices.OfType<Battery>().First();
            _out.WriteLine($"z = {mech.Body.Position.Z:F3} m, pos = {mech.Body.Position}, tilt max {maxTilt * 57.3:F1}°, " +
                           $"battery {bat.Voltage:F2} V {bat.Current:F2} A, wall {watch.Elapsed.TotalSeconds:F1} s");
            Assert.DoesNotContain(events, e => e.Severe);
            Assert.InRange(mech.Body.Position.Z, 0.7, 0.9);
            Assert.True(maxTilt < 6 * Math.PI / 180, $"tilt {maxTilt * 57.3:F1}°");
            Assert.True(Math.Abs(mech.Body.Velocity.Z) < 0.1);
            Assert.InRange(bat.Current, 1.0, 12.0);
        }

        [Fact]
        public void QuadcopterFliesForwardWhenPitched()
        {
            var sch = VehicleTemplates.MicroQuadcopter(Lib);
            var (_, sim, mech) = Start(sch);
            mech.Flight!.TargetAltitude = 0.6;
            mech.Flight.Throttle = 0.5;
            mech.Flight.Armed = true;
            Run(sim, mech, 2.5);
            double x0 = mech.Body.Position.X;
            mech.Flight.PitchInput = 0.5;
            Run(sim, mech, 1.0);
            mech.Flight.PitchInput = 0;
            Run(sim, mech, 0.3);
            _out.WriteLine($"moved {mech.Body.Position.X - x0:F2} m, z {mech.Body.Position.Z:F2}");
            Assert.True(mech.Body.Position.X - x0 > 0.3);
            Assert.True(mech.Body.Position.Z > 0.3);
        }

        [Fact]
        public void LineFollowerStaysOnTheOvalTrack()
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            var (_, sim, mech) = Start(sch);
            var arena = mech.Arena;
            var line = arena.Lines[0];
            double worst = 0;
            var watch = Stopwatch.StartNew();
            int samples = 0;
            Run(sim, mech, 12.0, t =>
            {
                if (++samples % 20 != 0 || t < 0.5) return;
                var p = mech.Body.ToWorld(new Vec3(0.06, 0, 0));
                double best = double.PositiveInfinity;
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    best = Math.Min(best, Geometry.PointSegmentDistance(p.X, p.Y, line[i].X, line[i].Y, line[i + 1].X, line[i + 1].Y));
                }

                worst = Math.Max(worst, best);
            });

            foreach (var e in mech.DrainEvents()) _out.WriteLine($"{e.Time:F2} {e.Source}: {e.Message}");
            var motors = sim.Circuit.Devices.OfType<DcMotor>().ToList();
            _out.WriteLine($"distance {mech.Distance:F2} m, worst offset {worst * 1000:F0} mm, speed {mech.Speed:F2} m/s, " +
                           $"motor rpm {string.Join("/", motors.Select(m => m.OutputRpm.ToString("F0")))}, wall {watch.Elapsed.TotalSeconds:F1} s");
            Assert.True(mech.Distance > 2.5, $"distance {mech.Distance:F2}");
            Assert.True(worst < 0.06, $"worst offset {worst * 1000:F0} mm");
        }

        [Fact]
        public void ShortedLipoGoesIntoThermalRunaway()
        {
            var sch = VehicleTemplates.MicroQuadcopter(Lib);
            var (compiled, sim, mech) = Start(sch);
            var bat = compiled.Circuit.Devices.OfType<Battery>().Single();
            bat.ShortCircuitResistance = 0.05;
            double failedAt = -1;
            Run(sim, mech, 30, t =>
            {
                if (failedAt < 0 && bat.IsFailed) failedAt = t;
            });
            var events = sim.DrainEvents().Where(e => e.Kind == AnalogEventKind.Failure).ToList();
            foreach (var e in events) _out.WriteLine($"{e.Time:F2} {e.DeviceId}: {e.Message}");
            Assert.True(failedAt > 2 && failedAt < 30, $"failed at {failedAt}");
            Assert.Contains(events, e => e.DeviceId == bat.Id && e.Message.Contains("termal kaçak"));
            bat.Repair();
            Assert.False(bat.IsFailed);
            Assert.Equal(0, bat.ShortCircuitResistance);
        }

        [Fact]
        public void JammedCorelessMotorBurnsOut()
        {
            // A coreless drone motor wired straight to a 1S pack, shaft locked: stall current cooks the winding.
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-1s-lipo-300", new GridPoint(0, 0));
            var m = sch.AddPart(Lib, "motor-8520", new GridPoint(20, 0));
            VehicleTemplates.Tie(sch, Lib, bat, "+", "VBAT");
            VehicleTemplates.Tie(sch, Lib, bat, "-", "GND");
            VehicleTemplates.Tie(sch, Lib, m, "A", "VBAT");
            VehicleTemplates.Tie(sch, Lib, m, "B", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var motor = compiled.Circuit.Devices.OfType<DcMotor>().Single();
            motor.Jammed = true;
            double burnt = -1;
            for (int i = 0; i < 30000 && burnt < 0; i++)
            {
                sim.Step(1e-3);
                if (motor.IsFailed) burnt = sim.Time;
            }

            _out.WriteLine($"stall {motor.Current:F2} A, burnt at {burnt:F1} s: {motor.FailureReason}");
            Assert.True(burnt > 1 && burnt < 30, $"burnt at {burnt}");
            Assert.Equal(0, motor.AngularVelocity, 6);
        }

        private static string? FindStudioFirmware(string name)
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = System.IO.Path.Combine(dir.FullName, "Studio", "RobotWin.Studio", "Firmware", name);
                if (System.IO.File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        [Fact]
        public void ArduinoObstacleAvoiderDrivesAndTurnsAwayFromObstacles()
        {
            string? hex = FindStudioFirmware("obstacle-avoider.hex");
            Assert.NotNull(hex);
            var sch = VehicleTemplates.ObstacleAvoider(Lib, hex!);
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var mech = new MechatronicSystem(sch.Assembly!, sch, Lib, compiled, sim);
            var mcu = new RobotTwin.CoreSim.Firmware.Atmega328P();
            mcu.LoadHex(System.IO.File.ReadAllText(hex!));
            var uno = sch.Parts.Single(p => p.PartId == "arduino-uno").RefDes;
            var fw = new RobotTwin.CoreSim.Firmware.FirmwareCoSimulation(mcu, compiled, uno, () => compiled.PinVoltage(sim, uno, "5V") - compiled.PinVoltage(sim, uno, "GND1"));
            var serial = new System.Text.StringBuilder();
            fw.SerialOutput += t => serial.Append(t);
            var ranger = mech.Sensors.Single(x => x.Mount.Kind == SensorKind.Ultrasonic);
            double minSeen = double.PositiveInfinity, maxYawRate = 0;
            var watch = Stopwatch.StartNew();
            const double slice = 50e-6;
            for (int i = 0; i < (int)(5.0 / slice); i++)
            {
                fw.Advance(slice, sim.Time);
                sim.Step(slice);
                mech.Advance(slice);
                if (i % 200 == 0)
                {
                    minSeen = Math.Min(minSeen, ranger.Value);
                    maxYawRate = Math.Max(maxYawRate, Math.Abs(mech.Body.AngularVelocity.Z));
                }
            }

            string text = serial.ToString();
            _out.WriteLine($"distance {mech.Distance:F2} m, nearest echo {minSeen * 100:F0} cm, max yaw rate {maxYawRate:F2} rad/s, wall {watch.Elapsed.TotalSeconds:F1} s");
            _out.WriteLine($"pos {mech.Body.Position} yaw {mech.Body.Orientation.ToEuler().Yaw:F2}");
            _out.WriteLine(text.Length > 700 ? text.Substring(0, 700) : text);
            foreach (var e in mech.DrainEvents()) _out.WriteLine($"{e.Time:F2} {e.Source}: {e.Message}");
            Assert.Contains("hazir", text);
            Assert.Contains("mesafe:", text);
            Assert.True(mech.Distance > 0.6, $"distance {mech.Distance:F2}");
            Assert.True(maxYawRate > 0.5, "the robot never turned");
        }

        [Fact]
        public void CustomWorldWithMoonGravityIsSavedAndUsed()
        {
            var sch = VehicleTemplates.MicroQuadcopter(Lib);
            sch.Assembly!.Arena = "custom";
            sch.Assembly.World = new WorldSpec { Width = 4, Depth = 3, Gravity = 1.62, AmbientC = -20, Track = "oval", TrackScale = 1.2 };
            sch.Assembly.World.Obstacles.Add(new WorldObstacle { X = 1, Y = 0.5, SizeX = 0.3, SizeY = 0.3, Height = 0.4 });
            var back = Schematic.FromJson(sch.ToJson());
            Assert.Equal(1.62, back.Assembly!.World!.Gravity);
            Assert.Single(back.Assembly.World.Obstacles);
            var arena = back.Assembly.CreateArena();
            Assert.Equal(4, arena.Width);
            Assert.Single(arena.Obstacles);
            Assert.NotEmpty(arena.Lines);

            var (_, sim, mech) = Start(back);
            Assert.Equal(-1.62, mech.World.Gravity.Z, 6);
            mech.Body.Position = new Vec3(0, 0, 0.5);
            Run(sim, mech, 0.5);
            // On the Moon a 0.5 m drop takes ~0.79 s; on Earth it would already have landed.
            Assert.InRange(mech.Body.Position.Z, 0.2, 0.4);
        }

        [Fact]
        public void ImportedChassisModelSetsMassCentreOfMassAndStillFollowsTheLine()
        {
            var sch = VehicleTemplates.LineFollower(Lib);
            // A 3D-printed tub: 200×130×30 mm box whose top is at the deck, offset 20 mm to the rear.
            var tub = MeshModel.CreateBox(new Vec3(0.20, 0.13, 0.03));
            tub.Translate(new Vec3(-0.02, 0, -0.015));
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rw-chassis-" + Guid.NewGuid().ToString("N") + ".stl");
            tub.Save(path);
            var loaded = MeshModel.Load(path); // STL is millimetres by convention
            System.IO.File.Delete(path);
            sch.Assembly!.ChassisMesh = loaded.ToEmbedded();
            sch.Assembly.ChassisMeshName = "tub.stl";
            sch.Assembly.ChassisMaterial = "PLA";
            sch.Assembly.ChassisInfill = 0.15;
            var back = Schematic.FromJson(sch.ToJson());
            Assert.NotNull(back.Assembly!.ChassisMesh);

            var (_, sim, mech) = Start(back);
            double tubMass = 0.20 * 0.13 * 0.03 * MaterialDensity.Printed(MaterialDensity.Pla, 0.15);
            _out.WriteLine($"mass {mech.TotalMass * 1000:F0} g (tub {tubMass * 1000:F0} g), COM {mech.CenterOfMass}");
            foreach (var (n, m) in mech.MassBreakdown) _out.WriteLine($"  {n}: {m * 1000:F1} g");
            Assert.Contains(mech.MassBreakdown, x => x.Name.Contains("tub.stl") && Math.Abs(x.Mass - tubMass) < 1e-3);
            Assert.True(mech.CenterOfMass.X < 0, "the heavy tub sits to the rear");
            Assert.True(mech.Body.ContactPoints.Count >= 8);

            Run(sim, mech, 6.0);
            _out.WriteLine($"distance {mech.Distance:F2} m");
            Assert.True(mech.Distance > 1.5, $"distance {mech.Distance:F2}");
            Assert.True(mech.Body.Orientation.Rotate(Vec3.UnitZ).Z > 0.95, "stays upright");
        }

        [Fact]
        public void UltrasonicSensorSeesTheWall()
        {
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-4aa", new GridPoint(0, 0));
            VehicleTemplates.Tie(sch, Lib, bat, "+", "V5");
            VehicleTemplates.Tie(sch, Lib, bat, "-", "GND");
            var us = sch.AddPart(Lib, "hc-sr04", new GridPoint(20, 0));
            VehicleTemplates.Tie(sch, Lib, us, "VCC", "V5");
            VehicleTemplates.Tie(sch, Lib, us, "GND", "GND");
            sch.Parts.Add(new SchematicPart { RefDes = "M1", PartId = "dc-motor", Position = new GridPoint(40, 0) });
            sch.Parts.Add(new SchematicPart { RefDes = "M2", PartId = "dc-motor", Position = new GridPoint(50, 0) });
            sch.Assembly = AssemblyDocument.AutoConfigure(sch, Lib, new AssemblyDocument { Kind = VehicleKind.Rover, Arena = "empty", StartX = 2.0, StartYaw = 0 });
            var (_, sim, mech) = Start(sch);
            Run(sim, mech, 0.05);
            var link = mech.Sensors.Single(s => s.Mount.Kind == SensorKind.Ultrasonic);
            double expected = mech.Arena.Width / 2 - mech.Body.ToWorld(new Vec3(link.Mount.X, 0, 0) - mech.CenterOfMass).X;
            Assert.InRange(link.Ranger!.Distance, expected - 0.02, expected + 0.02);
        }
    }
}
