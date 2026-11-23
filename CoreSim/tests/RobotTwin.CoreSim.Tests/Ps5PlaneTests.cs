using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[ps5plane] The DualSense-flown trainer: balanced airframe, free-rolling gear, and a whole circuit flown through the controller.</summary>
    public class Ps5PlaneTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public Ps5PlaneTests(ITestOutputHelper output) => _out = output;

        private static (double Roll, double Pitch, double Heading) Attitude(Quat q)
        {
            var x = q.Rotate(Vec3.UnitX);
            var y = q.Rotate(Vec3.UnitY);
            return (Math.Asin(Math.Clamp(y.Z, -1, 1)) * 180 / Math.PI,    // + = left wing up (banked right)
                    Math.Asin(Math.Clamp(x.Z, -1, 1)) * 180 / Math.PI,    // + = nose up
                    Math.Atan2(x.Y, x.X) * 180 / Math.PI);
        }

        [Fact]
        public void TheAirframeIsBalancedByItsBatteryAndEverythingFitsThePod()
        {
            var w = WorldExamples.Ps5Plane(Lib);
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var pod = w.Objects.First(o => o.Name.StartsWith("Gövde"));
            double mass = 0;
            var cg = Vec3.Zero;
            foreach (var b in r.AssemblyOf(pod.Id))
            {
                mass += b.Mass;
                cg += b.Position * b.Mass;
            }

            cg /= mass;
            var wing = w.Objects.First(o => WorldPhysics.IsWing(o) && o.Name.StartsWith("Kanat"));
            double leadingEdge = wing.Pose.X + wing.SizeX / 2;
            double cgChord = (leadingEdge - cg.X) / wing.SizeX;
            var fit = r.Physics!.ShellFit(w);
            foreach (var i in fit) _out.WriteLine("fit: " + i.Message);
            _out.WriteLine($"mass {mass * 1000:F0} g, CG at {cgChord * 100:F0} % of the chord");
            Assert.InRange(mass, 0.35, 0.45);
            Assert.InRange(cgChord, 0.25, 0.35);
            Assert.Empty(fit);
            Assert.Equal(3, w.Joints.Count(j => j.Kind == WorldJointKind.Hinge));   // nose and main wheels turn on their axles
            Assert.Contains(w.Objects, o => o.PartId == WirelessParts.GamepadId && o.Parameters.TryGetValue("pairedWith", out var p) && p.Length > 0);
        }

        [Fact]
        public void PushedAlongTheRunwayItRollsStraightOnItsWheels()
        {
            var w = WorldExamples.Ps5Plane(Lib);
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var pod = w.Objects.First(o => o.Name.StartsWith("Gövde"));
            foreach (var b in r.AssemblyOf(pod.Id)) b.Velocity = new Vec3(6, 0, 0);
            Assert.True(r.TryGetBody(pod.Id, out var s0));
            r.Step(2.0);
            Assert.True(r.TryGetBody(pod.Id, out var s1));
            double heading = Attitude(s1.Orientation).Heading;
            _out.WriteLine($"rolled {s1.Position.X - s0.Position.X:F2} m, drift {s1.Position.Y - s0.Position.Y:F3} m, heading {heading:F2}°, speed {s1.Velocity.Length:F2} m/s");
            Assert.True(s1.Position.X - s0.Position.X > 6, "free wheels roll");
            Assert.True(Math.Abs(heading) < 2, "fixed nose wheel and mains keep it straight");
            Assert.True(Math.Abs(s1.Position.Y - s0.Position.Y) < 0.1);
            Assert.InRange(s1.Position.Z, 0.10, 0.13);   // still standing on its gear
        }

        [Esp32Fact]
        public void TheControllerFliesACircuitAndALostLinkGlidesItDown()
        {
            var w = WorldExamples.Ps5Plane(Lib);
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-ps5plane-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            var padObject = w.Objects.First(o => o.PartId == WirelessParts.GamepadId);
            var pad = (GamepadNode)r.Radio!.Find(padObject.Id)!;
            var input = pad.Input;

            string phase = "bekle";
            double turned = 0, lastHeading = 0, since = 0, maxBank = 0, turnAltLoss = 0, turnStartAlt = 0, levelRoll = double.NaN;
            double failMaxBank = 0, failMinPitch = 0, glideSpeed = 0, glideSink = 0, glideAlt = 0, peakAlt = 0;
            bool connected = false, reconnected = false, turnedRight = false;
            for (double t = 0; t < 32 && phase != "bitti"; t += 0.05)
            {
                Assert.True(r.TryGetBody(pod.Id, out var s));
                var (roll, pitch, heading) = Attitude(s.Orientation);
                double alt = s.Position.Z - 0.115, speed = s.Velocity.Length;
                peakAlt = Math.Max(peakAlt, alt);
                turned += Math.IEEERemainder(heading - lastHeading, 360);
                lastHeading = heading;
                connected |= pad.Connected;
                input.MiscButtons = (byte)(t >= 2.5 && t < 2.7 ? GamepadState.MiscStart : 0);   // Options: arm
                input.AxisRX = 0;
                input.AxisRY = 0;
                switch (phase)
                {
                    case "bekle":
                        input.Throttle = 0;
                        if (t > 3) phase = "kalkış";
                        break;
                    case "kalkış":
                        input.Throttle = 1023;
                        if (speed > 8.5) input.AxisRY = 200;   // stick back: rotate
                        if (alt > 2) phase = "tırmanış";
                        break;
                    case "tırmanış":
                        input.Throttle = 1023;
                        if (alt > 8) (phase, since) = ("seyir", t);
                        break;
                    case "seyir":
                        input.Throttle = 560;
                        if (t - since > 1) (phase, since, turned, turnStartAlt) = ("sağ dönüş", t, 0, alt);
                        break;
                    case "sağ dönüş":
                        input.Throttle = 620;
                        input.AxisRX = 400;   // right stick to the right
                        maxBank = Math.Max(maxBank, roll);
                        turnAltLoss = Math.Min(turnAltLoss, alt - turnStartAlt);
                        turnedRight |= turned <= -150;
                        if (turnedRight || t - since > 9) (phase, since) = ("düz", t);
                        break;
                    case "düz":
                        input.Throttle = 560;
                        if (t - since > 2.5)
                        {
                            levelRoll = roll;
                            pad.PoweredOn = false;   // the controller dies
                            (phase, since) = ("kumanda kapalı", t);
                        }

                        break;
                    case "kumanda kapalı":
                        if (t - since > 0.5)
                        {
                            failMaxBank = Math.Max(failMaxBank, Math.Abs(roll));
                            failMinPitch = Math.Min(failMinPitch, pitch);
                        }

                        if (t - since > 3)
                        {
                            pad.PoweredOn = true;
                            (phase, since) = ("geri", t);
                        }

                        break;
                    case "geri":
                        input.Throttle = 0;
                        if (pad.Connected) reconnected = true;
                        if (reconnected || t - since > 4) (phase, since) = ("süzülüş", t);
                        break;
                    case "süzülüş":
                        input.Throttle = 0;   // back in control, power off: the throttle-pitch mix noses it down to glide
                        if (t - since > 1.5)
                        {
                            glideSpeed = speed;
                            glideSink = -s.Velocity.Z;
                            glideAlt = alt;
                        }

                        if (t - since > 3) phase = "bitti";
                        break;
                    default:
                        input.Throttle = 0;
                        break;
                }

                r.Step(0.05);
            }

            string serial = string.Concat(r.ReadSerial(null, false).Values);
            _out.WriteLine(serial.Length > 4000 ? serial.Substring(serial.Length - 4000) : serial);
            Assert.True(r.TryGetBody(pod.Id, out var end));
            var (endRoll, endPitch, _) = Attitude(end.Orientation);
            _out.WriteLine($"peak {peakAlt:F1} m; turn bank {maxBank:F0}°, height change {turnAltLoss:F1} m; level roll {levelRoll:F1}°; " +
                           $"failsafe max bank {failMaxBank:F0}°, lowest pitch {failMinPitch:F0}°; glide {glideSpeed:F1} m/s sinking {glideSink:F2} m/s at {glideAlt:F1} m; " +
                           $"end: phase {phase}, z {end.Position.Z:F2}, roll {endRoll:F0}°, pitch {endPitch:F0}°");

            Assert.True(connected, "the bonded controller connects");
            Assert.Contains("Motor kuruldu", serial);
            Assert.True(peakAlt > 8, "full throttle and a pull on the stick take it off the runway and up");
            Assert.Equal("bitti", phase);
            Assert.True(turnedRight, "right stick turns it right through 150°");
            Assert.InRange(maxBank, 18, 45);   // Dengeli mode: the stick asks for a bank angle
            Assert.True(turnAltLoss > -3, "the turn holds its height");
            Assert.True(Math.Abs(levelRoll) < 6, "released sticks level the wings");
            Assert.Contains("koptu", serial);
            Assert.True(failMaxBank < 25 && failMinPitch > -30, "with no link it glides in a gentle circle, not a spiral");
            Assert.True(reconnected, "the controller reconnects when it comes back on in range");
            Assert.True(glideAlt > 1, "still flying when the glide is measured");
            Assert.InRange(glideSpeed, 6.5, 11);
            Assert.True(glideSink < 2.0, "power-off glide better than 4:1");
        }
    }
}
