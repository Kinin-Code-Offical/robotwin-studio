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
    /// <summary>The pendulum, enclosed rover and traffic light templates do what their descriptions say.</summary>
    public class MoreTemplateTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public MoreTemplateTests(ITestOutputHelper output) => _out = output;

        private static RunnerOptions Esp32(string name)
        {
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-" + name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            return new RunnerOptions
            {
                Esp32Builder = src =>
                {
                    var build = new Esp32SketchCompiler().Compile(src, dir);
                    Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                    return build.DllPath;
                }
            };
        }

        [Fact]
        public void ThePendulumSwingsWithTheCompoundPendulumPeriod()
        {
            var w = WorldExamples.Pendulum(Lib);
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var arm = w.Objects.First(o => o.Name.StartsWith("Sarkaç kolu"));
            var ball = w.Objects.First(o => o.Name.StartsWith("Sarkaç topu"));
            var joint = w.Joints.First(j => j.Kind == WorldJointKind.Hinge);
            double pivotZ = joint.AnchorZ;
            double Angle()
            {
                r.TryGetBody(ball.Id, out var b);
                return Math.Atan2(b.Position.X, pivotZ - b.Position.Z) * 180 / Math.PI;
            }

            // Theory: a 286 mm bar (axle 6 mm from its top) and a 40 mm ball whose centre is 300 mm below the axle.
            double ma = arm.MassKg!.Value, mb = ball.MassKg!.Value, len = 0.286, c = 0.28 / 2 - 0.003, rb = 0.02, d = 0.28 + rb;
            double inertia = ma * (len * len + 0.012 * 0.012) / 12 + ma * c * c + 0.4 * mb * rb * rb + mb * d * d;
            double t0 = 2 * Math.PI * Math.Sqrt(inertia / (9.81 * (ma * c + mb * d)));
            double a0 = Math.Abs(Angle()) * Math.PI / 180;
            double theory = t0 * (1 + a0 * a0 / 16 + 11 * Math.Pow(a0, 4) / 3072);

            // Zero crossings from one side to the other over 12 s: a period is two of them.
            double last = Angle(), t = 0, first = double.NaN, lastCross = double.NaN, peak = 0;
            int crossings = 0;
            while (t < 12)
            {
                r.Step(0.001);
                t += 0.001;
                double now = Angle();
                if (t > 10) peak = Math.Max(peak, Math.Abs(now));
                if (Math.Sign(now) != Math.Sign(last) && last != 0)
                {
                    double tc = t - 0.001 * now / (now - last);
                    if (double.IsNaN(first)) first = tc;
                    lastCross = tc;
                    crossings++;
                }

                last = now;
            }

            double period = 2 * (lastCross - first) / (crossings - 1);
            _out.WriteLine($"start {a0 * 180 / Math.PI:F1}°, period {period:F4} s vs theory {theory:F4} s ({(period / theory - 1) * 100:F2} %), small-angle {t0:F4} s; swing after 10 s ±{peak:F1}°");
            Assert.InRange(period / theory, 0.99, 1.01);
            Assert.InRange(peak, 15, 29.9);   // it dies away (air, bearing), slowly
        }

        [Esp32Fact]
        public void TheEnclosedRoverDrivesASquare()
        {
            var w = WorldExamples.EnclosedRover(Lib);
            using var r = new WorldRunner(w, Esp32("rover"));
            var plate = w.Objects.First(o => o.Name == "Şase");
            r.TryGetBody(plate.Id, out var s0);
            var start = s0.Position;
            double Heading()
            {
                r.TryGetBody(plate.Id, out var b);
                var x = b.Orientation.Rotate(Vec3.UnitX);
                return Math.Atan2(x.Y, x.X) * 180 / Math.PI;
            }

            double turned = 0, last = Heading(), path = 0;
            var prev = start;
            for (int i = 0; i < 90; i++)
            {
                r.Step(0.1);
                double h = Heading();
                turned += Math.IEEERemainder(h - last, 360);
                last = h;
                r.TryGetBody(plate.Id, out var b);
                path += (b.Position - prev).Length;
                prev = b.Position;
            }

            r.TryGetBody(plate.Id, out var s1);
            string serial = string.Concat(r.ReadSerial(null, false).Values);
            _out.WriteLine($"path {path:F2} m, turned {turned:F0}°, ends {(s1.Position - start).Length:F2} m from the start; serial: {serial.Replace("\n", " | ")}");
            Assert.Contains("Kenar 3", serial);
            Assert.True(path > 0.8, "it drives");
            Assert.True(Math.Abs(turned) > 150, "and turns at the corners");
        }

        [Esp32Fact]
        public void TheTrafficLightGivesThePedestrianGreenAfterTheButton()
        {
            var w = WorldExamples.TrafficLight(Lib);
            using var r = new WorldRunner(w, Esp32("traffic"));
            r.Step(16);
            string serial = string.Concat(r.ReadSerial(null, false).Values);
            _out.WriteLine(serial);
            int press = serial.IndexOf("Yaya butona bastı", StringComparison.Ordinal);
            int yellow = serial.IndexOf("Araçlara sarı", StringComparison.Ordinal);
            int walk = serial.IndexOf("yayaya yeşil", StringComparison.Ordinal);
            int back = serial.LastIndexOf("Araçlara yeşil", StringComparison.Ordinal);
            Assert.True(press > 0 && yellow > press && walk > yellow && back > walk, "button → yellow → pedestrians walk → cars green again");
        }
    }
}
