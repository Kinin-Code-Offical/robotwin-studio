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
    /// <summary>[uav] The motor glider: real-size prop and motor, CG by battery position, flown by its ESP32 firmware.</summary>
    public class MotorGliderTests
    {
        private readonly ITestOutputHelper _out;

        public MotorGliderTests(ITestOutputHelper output) => _out = output;

        [Esp32Fact]
        public void MotorGliderIsBalancedByItsBatteryClimbsUnderPowerAndGlides()
        {
            var w = WorldExamples.MotorGlider(PartLibrary.Default);
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-plane-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            var pod = w.Objects.First(o => o.Name.StartsWith("Gövde"));
            var prop = w.Objects.First(o => o.Name.StartsWith("Pervane"));
            double mass = 0;
            var cg = Vec3.Zero;
            foreach (var b in r.Physics!.Scene.Bodies.Where(b => !b.IsStatic))
            {
                mass += b.Mass;
                cg += b.Position * b.Mass;
            }

            cg /= mass;
            double cgChord = (0.08 - cg.X) / 0.15;
            var fit = r.Physics.ShellFit(w);
            foreach (var i in fit) _out.WriteLine("fit: " + i.Message);
            _out.WriteLine($"mass {mass * 1000:F0} g, CG at {cgChord * 100:F0} % of the chord (no nose lead)");

            Assert.True(r.TryGetBody(pod.Id, out var s0));
            double z0 = s0.Position.Z, maxClimb = double.NegativeInfinity, maxRoll = 0, thrust = 0, rpm = 0, minZ = double.MaxValue;
            for (double t = 0; t < 12; t += 0.05)
            {
                r.Step(0.05);
                Assert.True(r.TryGetBody(pod.Id, out var s));
                if (t > 3 && t < 9.5) maxClimb = Math.Max(maxClimb, s.Position.Z - z0);
                if (t > 2.6) maxRoll = Math.Max(maxRoll, Math.Abs(Math.Asin(Math.Max(-1, Math.Min(1, s.Orientation.Rotate(Vec3.UnitY).Z))) * 180 / Math.PI));
                if (t > 2.6) minZ = Math.Min(minZ, s.Position.Z);
                if (Math.Abs(t - 5) < 0.026 && r.Physics.PropReadout(prop.Id) is { } pr)
                {
                    thrust = pr.Thrust;
                    rpm = pr.Rpm;
                }
                if (Math.Abs(t - 5) < 0.026)
                {
                    foreach (var kv in r.Compiled!.DevicesByPart)
                        foreach (var dv in kv.Value)
                        {
                            if (dv is RobotTwin.CoreSim.Analog.Devices.DcMotor m) _out.WriteLine($"motor {m.OutputRpm:F0} rpm, {m.Current:F2} A, {m.Voltage:F2} V, R {m.Resistance:F3}→{m.HotResistance:F3} Ω, winding {m.Thermal.TemperatureC:F1} °C");
                            if (dv is RobotTwin.CoreSim.Analog.Devices.Battery b) _out.WriteLine($"battery {b.Current:F2} A, Rs {b.SeriesResistance:F3} Ω, polarization {b.PolarizationVoltage:F3} V, T {b.Thermal.TemperatureC:F1} °C");
                            if (dv is RobotTwin.CoreSim.Analog.Devices.AveragedPwmDriver e && e.PulseNode >= 0) _out.WriteLine($"ESC duty {e.Duty:F2}, pulse {e.LastPulseWidth * 1e6:F0} us, out {e.OutputVoltage:F2} V {e.OutputCurrent:F2} A");
                        }
                    foreach (var (label, temp) in r.Physics.PartTemperatures(w.Objects.First(o => o.Name.StartsWith("Motor")).Id)) _out.WriteLine($"  {label} {temp:F1} °C");
                }
            }

            _out.WriteLine(string.Concat(r.ReadSerial(null, false).Values));
            _out.WriteLine($"under power at 5 s: prop {rpm:F0} rpm, thrust {thrust:F2} N; best climb {maxClimb:F1} m; max bank {maxRoll:F0}°; lowest {minZ:F2} m");
            Assert.Empty(fit);
            Assert.InRange(mass, 0.27, 0.34);
            Assert.InRange(cgChord, 0.28, 0.38);                // balanced by the battery position alone
            Assert.InRange(rpm, 7000, 11000);                   // 2212 1000KV on 3S, loaded by an 8" prop
            Assert.True(thrust > 1.5, "a real-size prop pulls the plane");
            Assert.True(maxClimb > 3, "it climbs under power");
            Assert.True(maxRoll < 45, "the flight controller keeps the wings level");
        }
    }
}
