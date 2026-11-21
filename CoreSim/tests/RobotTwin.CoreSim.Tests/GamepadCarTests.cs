using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[radio] The PS5-controlled car: its board is a real design and it drives on its own battery.</summary>
    public class GamepadCarTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public GamepadCarTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheCarBoardIsARoutedPcbWithItsFirmware()
        {
            var pkg = WorldExamples.GamepadCarBoard(Lib);
            var board = pkg.Schematic.Board!;
            Assert.Equal(7, board.Components.Count);   // ESP32, DRV8833, 3 terminals, switch, capacitor
            Assert.NotEmpty(board.Tracks);
            var errors = DesignRuleChecker.Check(board).Where(v => v.Severity == DrcSeverity.Error).ToList();
            Assert.True(errors.Count == 0, string.Join("\n", errors.Take(10)));
            var esp = pkg.Schematic.Parts.Single(p => p.PartId == Esp32Parts.DevKitV1Id);
            Assert.Equal("builtin:esp32-gamepad-car.ino", esp.Parameters["firmware"]);
            Assert.Equal("false", esp.Parameters["usb"]);
        }

        [Fact]
        public void TheCarWorldHasNoUsbTetherAndEachWheelSitsOnItsShaft()
        {
            var w = WorldExamples.GamepadCar(Lib);
            Assert.DoesNotContain(w.Objects, o => o.PartId == "usb-pc");
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            foreach (var wheel in w.Objects.Where(o => o.Shape == "wheel"))
            {
                var joint = w.Joints.Single(j => j.ObjectA == wheel.Id);
                var rotor = r.Physics!.Scene.FindBody(joint.ObjectB + ":shaft")!;
                var axis = rotor.Orientation.Rotate(Vec3.UnitZ);
                Assert.True(r.TryGetBody(wheel.Id, out var s));
                // The wheel's centre (its pose is the bottom of the tyre) lies on its motor's shaft axis: no eccentric wobble.
                var d = s.Position + s.Orientation.Rotate(new Vec3(0, 0, wheel.SizeX / 2)) - rotor.Position;
                var off = d - axis * d.Dot(axis);
                _out.WriteLine($"{wheel.Name}: {off.Length * 1000:F2} mm off the shaft axis");
                Assert.True(off.Length < 0.0005, wheel.Name + " is off its shaft by " + off.Length);
            }
        }

        [Esp32Fact]
        public void TheControllerDrivesTheCarStraightAndTurnsIt()
        {
            var w = WorldExamples.GamepadCar(Lib);
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-padcar-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var options = new RunnerOptions
            {
                Esp32Builder = src =>
                {
                    var build = new Esp32SketchCompiler().Compile(src, dir);
                    Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                    return build.DllPath;
                }
            };
            options.FirmwareSearchPath.Add(StudioFirmware());
            using var r = new WorldRunner(w, options);
            var plate = w.Objects.First(o => o.Name == "Şase");
            var padObject = w.Objects.First(o => o.PartId == WirelessParts.GamepadId);
            var pad = (GamepadNode)r.Radio!.Find(padObject.Id)!;
            r.Step(2.0);
            string serial = string.Concat(r.ReadSerial(null, false).Values);
            _out.WriteLine(serial);
            Assert.True(pad.Connected, "bonded controller reconnects: " + pad.Status);

            Assert.True(r.TryGetBody(plate.Id, out var s0));
            pad.Input.Throttle = 1023;   // R2 fully down
            r.Step(2.0);
            Assert.True(r.TryGetBody(plate.Id, out var s1));
            foreach (var m in w.Objects.Where(o => o.PartId == "dc-motor"))
            {
                var dev = r.Circuit!.Compiled.DevicesOf(r.Circuit.ComponentRef(m.Id)!).OfType<RobotTwin.CoreSim.Analog.Devices.DcMotor>().Single();
                _out.WriteLine($"{m.Name}: {dev.OutputRpm:F0} rpm, {dev.Current:F2} A");
            }

            _out.WriteLine($"plate z {s1.Position.Z * 1000:F1} mm, tilt {Math.Acos(Math.Min(1, s1.Orientation.Rotate(Vec3.UnitZ).Z)) * 180 / Math.PI:F1}°");
            foreach (var e in r.DrainEvents().Take(20)) _out.WriteLine($"[{e.Time:0.000}] {e.Source}: {e.Message}");
            var travel = s1.Position - s0.Position;
            double heading = Yaw(s1.Orientation) - Yaw(s0.Orientation);
            _out.WriteLine($"R2: moved {travel.X:F3} m forward, {travel.Y:F3} m sideways, heading change {heading:F1}°, speed {travel.Length / 2:F2} m/s");
            Assert.True(travel.X > 0.4, "R2 drives the car forward");
            Assert.True(Math.Abs(travel.Y) < 0.05, "and straight: mirror-mounted motors on one axle");
            Assert.True(Math.Abs(heading) < 3, "without veering");

            pad.Input.AxisX = 400;   // stick right
            double turn = 0, last = Yaw(s1.Orientation);
            for (int i = 0; i < 10; i++)
            {
                r.Step(0.1);
                Assert.True(r.TryGetBody(plate.Id, out var si));
                double now = Yaw(si.Orientation);
                turn += Math.IEEERemainder(now - last, 360);
                last = now;
            }

            Assert.True(r.TryGetBody(plate.Id, out var s2));
            _out.WriteLine($"stick right: heading change {turn:F1}° in 1 s");
            Assert.True(turn < -20, "steering right turns the car clockwise (seen from above)");
            Assert.True(s2.Orientation.Rotate(Vec3.UnitZ).Z > 0.95, "on its wheels");
        }

        private static double Yaw(Quat q)
        {
            var x = q.Rotate(Vec3.UnitX);
            return Math.Atan2(x.Y, x.X) * 180 / Math.PI;
        }

        private static string StudioFirmware()
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                string p = Path.Combine(d.FullName, "Studio", "RobotWin.Studio", "Firmware");
                if (Directory.Exists(p)) return p;
            }

            throw new DirectoryNotFoundException("Studio/RobotWin.Studio/Firmware");
        }
    }
}
