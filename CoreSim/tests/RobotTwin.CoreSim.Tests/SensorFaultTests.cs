using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sensorfault] Sensors failing as they fail on a robot, from the time their scenario says.</summary>
    public class SensorFaultTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public SensorFaultTests(ITestOutputHelper output) => _out = output;

        /// <summary>A part powered from a battery, the world compiled, its sensing built and run for a while.</summary>
        private static (WorldDocument W, WorldObject Part, WorldSensing Sensing, AnalogSimulator Sim) Rig(string preset, double z, Action<WorldObject>? fault = null)
        {
            var w = new WorldDocument();
            var part = w.AddComponent(Lib, preset, new WorldPose { Z = z });
            var bat = w.AddComponent(Lib, "bat-4nimh", new WorldPose { X = -0.5, Y = -0.5 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = part.Id, Pin = "VCC" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = part.Id, Pin = "GND" });
            fault?.Invoke(part);
            var wc = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            return (w, part, WorldSensing.Build(w, wc, null), sim);
        }

        private static void Run(WorldSensing s, AnalogSimulator sim, double seconds, double dt = 1e-4)
        {
            double until = sim.Time + seconds;
            while (sim.Time < until - 1e-12)
            {
                sim.Step(dt);
                s.Advance(sim.Time, dt, sim);
            }
        }

        private static void Give(WorldObject o, string id, double at = 0, double? value = null)
        {
            var f = MachineFaults.For(o.PartId).Single(x => x.Id == id);
            MachineFaults.Set(o, f, at);
            if (value is { } v) o.Parameters[MachineFaults.ValueKey] = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private double LinePhotocurrent(Action<WorldObject>? fault)
        {
            var (_, part, sensing, sim) = Rig("tcrt5000-module", 0.0025, fault);
            Run(sensing, sim, 0.02);
            return sensing.Sensors.Get<LineSensorReading>(part.Id)!.Channels[0].Photocurrent;
        }

        [Fact]
        public void ADirtyLensWeakensALineSensorAndADeadLedLeavesItDark()
        {
            double clean = LinePhotocurrent(null);
            double dirty = LinePhotocurrent(o => Give(o, "line_dirty", 0, 40));
            double dead = LinePhotocurrent(o => Give(o, "line_led_dead"));
            _out.WriteLine($"fotoakım: temiz {clean * 1e6:0.0} µA, kirli (%40) {dirty * 1e6:0.0} µA, LED ölü {dead * 1e6:0.00} µA");
            Assert.InRange(dirty / clean, 0.3, 0.5);
            Assert.InRange(dead, 0, clean * 0.02);
        }

        [Fact]
        public void TheImuDriftsFreezesAndFallsSilentWhenItsScenarioStarts()
        {
            var (_, part, sensing, sim) = Rig("gy-521", 0.01);
            var chip = new Mpu6050();
            sensing.BindMpu6050(part.Id, chip);
            Give(part, "imu_gyro_drift", 0.05, 5);
            Run(sensing, sim, 0.04, 1e-3);
            double before = chip.GyroZ;
            Run(sensing, sim, 0.04, 1e-3);
            double after = chip.GyroZ;
            _out.WriteLine($"jiroskop Z: önce {before * 180 / Math.PI:0.00} °/s, kayma başlayınca {after * 180 / Math.PI:0.00} °/s");
            Assert.InRange((after - before) * 180 / Math.PI, 4.0, 6.0);

            MachineFaults.Clear(part);
            Give(part, "imu_frozen", 0);
            double frozen = chip.AccelZ;
            Run(sensing, sim, 0.02, 1e-3);
            Assert.Equal(frozen, chip.AccelZ);

            MachineFaults.Clear(part);
            Give(part, "imu_offline", 0);
            Run(sensing, sim, 0.005, 1e-3);
            Assert.False(chip.Start(false));   // no ACK on its address
        }

        [Fact]
        public void TheSensorLogLabelsEachRowWithTheFaultActiveThen()
        {
            var (w, part, sensing, sim) = Rig("gy-521", 0.01);
            var chip = new Mpu6050();
            sensing.BindMpu6050(part.Id, chip);
            Give(part, "imu_vibration", 0.1, 4);
            var log = new SensorLog(w);
            log.Attach(sensing.Sensors);
            Run(sensing, sim, 0.2, 1e-3);
            log.Detach();
            string csv = log.Csv(part.Id)!;
            var lines = csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            _out.WriteLine(lines[0]);
            _out.WriteLine(lines[1]);
            _out.WriteLine(lines[^1]);
            Assert.StartsWith("time_s,ax,ay,az,gx,gy,gz,true_ax", lines[0]);
            Assert.EndsWith(",normal", lines[1]);
            Assert.EndsWith(",imu_vibration", lines[^1]);
            Assert.InRange(lines.Length - 1, 18, 22);   // 100 Hz for 0.2 s
            // The vibration shows in what it measured, not in the truth beside it.
            var late = lines.Skip(lines.Length / 2 + 2).Select(l => l.Split(',')).ToList();
            double spread(int col) => late.Select(c => double.Parse(c[col], System.Globalization.CultureInfo.InvariantCulture)).Max() -
                                      late.Select(c => double.Parse(c[col], System.Globalization.CultureInfo.InvariantCulture)).Min();
            Assert.True(spread(3) > 5 * Math.Max(1e-6, spread(9)));
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "rw-sensorlog-" + Guid.NewGuid().ToString("N"));
            try
            {
                var files = log.Save(dir);
                Assert.Contains(files, f => f.EndsWith("README.txt", StringComparison.Ordinal));
                Assert.Contains(files, f => f.EndsWith(".csv", StringComparison.Ordinal));
            }
            finally
            {
                System.IO.Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void TheGpsLosesItsSkyAndMultipathPullsItsPositionAway()
        {
            var env = new SensingEnvironment();
            var clear = new GpsReceiver(GpsSpec.Neo6M, env, 7, "sıcak") { RateHz = 1 };
            var blocked = new GpsReceiver(GpsSpec.Neo6M, env, 7, "sıcak") { RateHz = 1, Blocked = true };
            for (int s = 0; s < 20; s++)
            {
                clear.Update(s, Vec3.Zero, Vec3.Zero, true);
                blocked.Update(s, Vec3.Zero, Vec3.Zero, true);
            }

            Assert.True(clear.Latest!.Valid);
            Assert.False(blocked.Latest!.Valid);
            Assert.Contains("$GPGGA", blocked.Latest.Nmea);   // the module still talks, with no position

            var (_, part, sensing, sim) = Rig("gps-neo6m", 0.02, o => Give(o, "gps_multipath", 0, 8));
            Run(sensing, sim, 3.0, 1e-3);
            var fix = sensing.Sensors.Get<GpsReading>(part.Id)!.Fix;
            var (lat, lon, _) = GpsReceiver.ToGeodetic(sensing.Environment, 0, 0, 0);
            double east = (fix.Longitude - lon) * 111_320 * Math.Cos(lat * Math.PI / 180);
            _out.WriteLine($"yansımalı GPS doğuya {east:0.0} m kaydı");
            Assert.InRange(east, 8 - 6, 8 + 6);   // 8 m east, within the receiver's own CEP
        }
    }
}
