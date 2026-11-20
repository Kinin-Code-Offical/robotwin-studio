using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public class WorldPhysicsTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public WorldPhysicsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>Simple geometry: bodies from their size, TT motor 37×22.5×18.8 mm with a shaft along +Y.</summary>
        private static WorldBodyGeometry Geometry(WorldObject o)
        {
            if (o.Kind == WorldObjectKind.Body)
            {
                var g = new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = 0.05 };
                if (o.Shape == "wheel")
                {
                    g.Min = new Vec3(-o.SizeX / 2, -o.SizeZ / 2, 0);
                    g.Max = new Vec3(o.SizeX / 2, o.SizeZ / 2, o.SizeX);
                    g.Shape = "cylinder-y";
                    g.MassKg = 0.03;
                }

                return g;
            }

            if (o.PartId == "dc-motor")
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0185, -0.01125, 0), Max = new Vec3(0.0185, 0.01125, 0.0188), MassKg = 0.03,
                    PortName = "shaft", PortPivot = new Vec3(-0.011, 0.0185, 0.0094), PortAxis = Vec3.UnitY
                };
            }

            if (o.PartId == "servo")
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0114, -0.0061, 0), Max = new Vec3(0.0114, 0.0061, 0.0227), MassKg = 0.009,
                    PortName = "horn", PortPivot = new Vec3(-0.0054, 0, 0.031), PortAxis = Vec3.UnitZ
                };
            }

            if (o.PartId == "hc-sr04")
            {
                // Module standing up, transducers looking along -Y.
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0225, -0.008, 0), Max = new Vec3(0.0225, 0.004, 0.023), MassKg = 0.009,
                    SensorOrigin = new Vec3(0, -0.008, 0.014), SensorAxis = new Vec3(0, -1, 0)
                };
            }

            return new WorldBodyGeometry { Min = new Vec3(-0.03, -0.02, 0), Max = new Vec3(0.03, 0.02, 0.015), MassKg = 0.05 };
        }

        [Fact]
        public void DroppedBoxFallsAndRestsOnTheFloor()
        {
            var w = new WorldDocument();
            var box = w.AddBody("box", 0.1, 0.1, 0.05, new WorldPose { Z = 0.3 });
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 1500; i++) p.Advance(1e-3, null);
            var pose = p.PoseOf(box.Id)!.Value;
            _out.WriteLine($"box base z = {pose.Position.Z * 1000:F2} mm");
            Assert.InRange(pose.Position.Z, -0.002, 0.002);
        }

        [Fact]
        public void BatteryDrivenMotorTurnsTheWheelOnItsShaft()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.2 });
            var stand = w.AddBody("box", 0.06, 0.04, 0.03, new WorldPose());
            stand.IsStatic = true;
            var motor = w.AddComponent(Lib, "motor-tt", new WorldPose { Z = 0.03 });
            var wheel = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.011, Y = 0.0335, Z = 0.0394 - 0.0325 });
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = motor.Id, ObjectB = stand.Id });
            w.Joints.Add(new WorldJoint { Id = "J2", ObjectA = wheel.Id, ObjectB = motor.Id, PortB = "shaft" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 22);
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 22);
            var wc = WorldCompiler.Compile(w, Lib, c => 0.25);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            double start = p.PortAngle(motor.Id);
            for (int i = 0; i < 1000; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
            }

            var m = wc.Compiled.DevicesOf(wc.ComponentRef(motor.Id)!).OfType<DcMotor>().Single();
            double turns = (p.PortAngle(motor.Id) - start) / (2 * Math.PI);
            _out.WriteLine($"motor {m.OutputRpm:F0} rpm, {m.Current * 1000:F0} mA, load {m.LoadTorque * 1000:F2} mN·m, shaft turned {turns:F2} rev");
            Assert.True(Math.Abs(p.PortAngle(motor.Id) - start) > 1.0, "the shaft must turn");
            var wp = p.PoseOf(wheel.Id)!.Value;
            var hub = wp.Position + wp.Orientation.Rotate(new Vec3(0, 0, 0.0325));
            _out.WriteLine($"wheel hub at z = {hub.Z * 1000:F1} mm (shaft at 39.4 mm)");
            Assert.InRange(hub.Z, 0.036, 0.043);   // the wheel stays on the shaft (does not fall to the floor)
        }

        [Fact]
        public void ServoHoldsItsArmAtTheCommandedAngle()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.2 });
            var gen = w.AddComponent(Lib, "signal-generator", new WorldPose { X = -0.1 });
            // 50 Hz, 1.5 ms pulses = centre.
            gen.Parameters["shape"] = "square";
            var servo = w.AddComponent(Lib, "servo-sg90", new WorldPose());
            servo.IsStatic = true;
            var arm = w.AddBody("box", 0.08, 0.01, 0.003, new WorldPose { X = -0.0054 + 0.04, Z = 0.031 });
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = arm.Id, ObjectB = servo.Id, PortB = "horn" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = servo.Id, Pin = "GND" });
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var s = wc.Compiled.DevicesOf(wc.ComponentRef(servo.Id)!).OfType<Servo>().Single();
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            // Drive the servo command directly (no pulse source needed for the mechanical check).
            typeof(Servo).GetProperty("TargetAngleDeg")!.SetValue(s, 135.0);
            for (int i = 0; i < 1500; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
            }

            double deg = p.PortAngle(servo.Id) * 180 / Math.PI;
            _out.WriteLine($"horn {deg:F1}° (target +45°), servo reports {s.AngleDeg:F1}°, current {s.SupplyCurrent * 1000:F0} mA");
            Assert.InRange(deg, 38, 52);
            Assert.InRange(s.AngleDeg, 128, 142);
        }
        [Fact]
        public void UltrasonicSensorMeasuresTheBoxInFrontOfIt()
        {
            var w = new WorldDocument();
            var sonar = w.AddComponent(Lib, "hc-sr04", new WorldPose());
            sonar.IsStatic = true;
            var wall = w.AddBody("box", 0.2, 0.02, 0.05, new WorldPose { Y = -0.31 });
            wall.IsStatic = true;
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var dev = wc.Compiled.DevicesOf(wc.ComponentRef(sonar.Id)!).OfType<UltrasonicSensor>().Single();
            var p = WorldPhysics.Build(w, Geometry, wc);
            for (int i = 0; i < 50; i++) p.Advance(1e-3, null);
            _out.WriteLine($"sonar reads {dev.Distance * 1000:F1} mm (wall face at 292 mm from the sensor face)");
            Assert.InRange(dev.Distance, 0.285, 0.30);

            // Nothing in front: beyond the maximum range.
            wall.Pose = new WorldPose { Y = 0.5 };
            var p2 = WorldPhysics.Build(w, Geometry, wc);
            for (int i = 0; i < 50; i++) p2.Advance(1e-3, null);
            Assert.True(dev.Distance > dev.MaxRange);
        }

        [Fact]
        public void CarExampleStartsWithoutPartsInsideEachOther()
        {
            var w = WorldExamples.Car(Lib);
            var p = WorldPhysics.Build(w, Geometry, null);
            var overlaps = p.FindInterpenetrations();
            foreach (var (a, b, d) in overlaps) _out.WriteLine($"{w.Find(a)?.Name} ↔ {w.Find(b)?.Name}: {d * 1000:F1} mm");
            Assert.Empty(overlaps);
        }

        [Fact]
        public void TwoWheelCarDrivesAcrossTheFloor()
        {
            var w = WorldExamples.Car(Lib);
            var plate = w.Objects[0];
            var wc = WorldCompiler.Compile(w, Lib, c => 0.12);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            var start = p.PoseOf(plate.Id)!.Value.Position;
            for (int i = 0; i < 2000; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
            }

            var end = p.PoseOf(plate.Id)!.Value;
            var d = end.Position - start;
            var up = end.Orientation.Rotate(Vec3.UnitZ);
            var motors = w.Objects.Where(o => o.PartId == "dc-motor").Select(o => wc.Compiled.DevicesOf(wc.ComponentRef(o.Id)!).OfType<DcMotor>().Single()).ToList();
            _out.WriteLine($"moved ({d.X * 1000:F0}, {d.Y * 1000:F0}, {d.Z * 1000:F0}) mm, up.z {up.Z:F3}; motors {string.Join(", ", motors.Select(m => $"{m.OutputRpm:F0} rpm {m.Current * 1000:F0} mA"))}");
            Assert.True(new Vec3(d.X, d.Y, 0).Length > 0.25, "the car must drive (≥ 25 cm in 2 s)");
            Assert.True(up.Z > 0.9, "and stay upright");
        }
    }
}
