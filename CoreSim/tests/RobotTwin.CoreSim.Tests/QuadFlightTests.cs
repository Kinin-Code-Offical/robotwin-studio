using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[esc] The ESP32 quad flies on its own firmware: IMU over I²C, PID, ESC pulses, brushless motors, props.</summary>
    public class QuadFlightTests
    {
        private readonly ITestOutputHelper _out;

        public QuadFlightTests(ITestOutputHelper output) => _out = output;

        private (double MaxTilt, double Climb, double Mass, string Serial) Fly(double batteryX, double seconds)
        {
            var w = WorldExamples.Quad(PartLibrary.Default, batteryX);
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-quad-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var options = new RunnerOptions
            {
                Esp32Builder = src =>
                {
                    var build = new Esp32SketchCompiler().Compile(src, dir);
                    Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                    return build.DllPath;
                }
            };
            using var r = new WorldRunner(w, options);
            var plate = w.Objects.First(o => o.Name == "Üst plaka");
            double mass = r.Physics!.Scene.Bodies.Where(b => !b.IsStatic).Sum(b => b.Mass);
            Assert.True(r.TryGetBody(plate.Id, out var s0));
            double maxTilt = 0;
            for (double t = 0; t < seconds; t += 0.05)
            {
                r.Step(0.05);
                Assert.True(r.TryGetBody(plate.Id, out var s));
                double tilt = Math.Acos(Math.Max(-1, Math.Min(1, s.Orientation.Rotate(Vec3.UnitZ).Z))) * 180 / Math.PI;
                if (s.Position.Z - s0.Position.Z > 0.05) maxTilt = Math.Max(maxTilt, tilt);   // once airborne
            }

            foreach (var po in w.Objects.Where(o => o.Name.StartsWith("Pervane")))
                if (r.Physics!.PropReadout(po.Id) is { } pr) _out.WriteLine($"{po.Name}: {pr.Rpm:F0} rpm, itki {pr.Thrust:F2} N, {pr.Power:F1} W");
            Assert.True(r.TryGetBody(plate.Id, out var s1));
            string serial = string.Concat(r.ReadSerial(null, false).Values);
            foreach (var e in r.DrainEvents().Take(30)) _out.WriteLine($"[{e.Time:0.000}] {e.Source}: {e.Message}");
            return (maxTilt, s1.Position.Z - s0.Position.Z, mass, serial);
        }

        [Esp32Fact]
        public void TheFlightControllerHoldsTheQuadLevelEvenWithTheBatteryOffCentre()
        {
            var centred = Fly(0, 5);
            _out.WriteLine(centred.Serial);
            _out.WriteLine($"centred pack: {centred.Mass * 1000:F0} g, climbed {centred.Climb:F2} m, max tilt {centred.MaxTilt:F1}°");
            var forward = Fly(0.02, 5);
            _out.WriteLine(forward.Serial);
            _out.WriteLine($"pack 20 mm forward: climbed {forward.Climb:F2} m, max tilt {forward.MaxTilt:F1}°");
            Assert.Contains("IMU hazir", centred.Serial);
            Assert.InRange(centred.Mass, 0.25, 0.45);
            Assert.True(centred.Climb > 0.3, "it takes off on its own thrust");
            Assert.True(centred.MaxTilt < 12, "the controller keeps it level");
            Assert.True(forward.Climb > 0.3, "with the pack forward it still flies");
            Assert.True(forward.MaxTilt < 15, "the PID trims the motors against the shifted centre of mass");
        }
    }
}
