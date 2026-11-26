using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox.Sensing;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sensorspec] What the sheets say about range, resolution, latency and noise, where it was missing.</summary>
    public class SensorSpecTests
    {
        private readonly ITestOutputHelper _out;

        public SensorSpecTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AGpsFixDescribesWhereTheAntennaWasOneLatencyAgo()
        {
            var env = new SensingEnvironment();
            var gps = new GpsReceiver(GpsSpec.Neo6M, env, 3, "sıcak") { RateHz = 5 };
            GpsFix? last = null;
            double output = 0;
            for (double t = 0; t < 5; t += 0.01)
            {
                var p = new Vec3(2.0 * t, 0, 0);   // 2 m/s east
                if (gps.Update(t, p, new Vec3(2, 0, 0), true) is { } fix)
                {
                    last = fix;
                    output = t;
                }
            }

            var (lat0, lon0, _) = GpsReceiver.ToGeodetic(env, 0, 0, 0);
            double east = (last!.Longitude - lon0) * 111_320 * Math.Cos(lat0 * Math.PI / 180);
            double trueEast = east - last.ErrorEast;   // the antenna where the fix says, without the receiver's own error
            _out.WriteLine($"fix stamped {last.Time:0.000} s: antenna at {trueEast:0.000} m, {2 * last.Time:0.000} m then");
            Assert.Equal(GpsSpec.Neo6M.Latency, output - last.Time, 6);   // stamped with the epoch it describes
            Assert.InRange(trueEast, 2 * last.Time - 0.03, 2 * last.Time + 0.03);   // where it was then, not 16 cm further on
        }

        private static List<double> Echoes(double sigma, int count)
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), trig = c.Node("trig"), echo = c.Node("echo");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0));
            var pulse = c.Add(new VoltageSource("VT", trig, 0, 0.0));
            var sonar = c.Add(new UltrasonicSensor("U1", vcc, trig, echo, 0) { Distance = 1.0, RangeNoise = sigma });
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            var widths = new List<double>();
            for (int k = 0; k < count; k++)
            {
                sim.Step(1e-4);
                pulse.Waveform = 5.0;
                sim.Step(12e-6);
                pulse.Waveform = 0.0;
                sim.Step(10e-3);
                widths.Add(sonar.LastPulseWidth);
            }

            return widths;
        }

        [Fact]
        public void AnHcSr04JittersByMillimetres()
        {
            var w = Echoes(0.0015, 40);
            double c = 343.2, mean = w.Average(), sd = Math.Sqrt(w.Sum(x => (x - mean) * (x - mean)) / (w.Count - 1));
            _out.WriteLine($"1 m: {mean * c / 2 * 1000:0.0} mm ± {sd * c / 2 * 1000:0.00} mm");
            Assert.InRange(sd * c / 2, 0.0008, 0.0025);
            Assert.InRange(mean * c / 2, 0.998, 1.002);
            Assert.Single(Echoes(0, 5).Distinct());   // without jitter every echo is the same
        }

        [Fact]
        public void TheCompassHasItsNoiseFloor()
        {
            var w = new RobotTwin.CoreSim.Sandbox.WorldDocument();
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var mag = w.AddComponent(lib, "gy-271", new RobotTwin.CoreSim.Sandbox.WorldPose { Z = 0.01 });
            var bat = w.AddComponent(lib, "bat-4nimh", new RobotTwin.CoreSim.Sandbox.WorldPose { X = -0.5 });
            w.Connect(new RobotTwin.CoreSim.Sandbox.CableEnd { ObjectId = bat.Id, Pin = "+" }, new RobotTwin.CoreSim.Sandbox.CableEnd { ObjectId = mag.Id, Pin = "VCC" });
            w.Connect(new RobotTwin.CoreSim.Sandbox.CableEnd { ObjectId = bat.Id, Pin = "-" }, new RobotTwin.CoreSim.Sandbox.CableEnd { ObjectId = mag.Id, Pin = "GND" });
            var wc = RobotTwin.CoreSim.Sandbox.WorldCompiler.Compile(w, lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var sensing = WorldSensing.Build(w, wc, null);
            sensing.BindCompass(mag.Id, new Hmc5883l(), null);
            var xs = new List<double>();
            sensing.Sensors.Published += r => { if (r is CompassReading cr) xs.Add(cr.Field.X); };
            for (int k = 0; k < 3000; k++)
            {
                sim.Step(1e-3);
                sensing.Advance(sim.Time, 1e-3, sim);
            }

            double mean = xs.Average(), sd = Math.Sqrt(xs.Sum(x => (x - mean) * (x - mean)) / (xs.Count - 1));
            _out.WriteLine($"{xs.Count} readings: Bx {mean * 1000:0.0} mG ± {sd * 1000:0.00} mG");
            Assert.InRange(sd, 0.0013, 0.0028);
        }
    }
}
