using System;
using System.Collections.Generic;
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
    /// <summary>[sense] Line/reflectance optics, sensor electronics, cameras, LiDAR, GPS, IMU, compass and encoders.</summary>
    public class SensingTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public SensingTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>A 1 m floor with a long straight tape of each finish along X at y = −0.2, 0, +0.2.</summary>
        private static WorldSpec Track(double ambientIr = 0, double sunElevation = 60)
        {
            var env = new WorldSpec { Width = 2, Depth = 2, Walls = false, Track = "none" };
            env.Sensing = new SensingEnvironment { AmbientIr = ambientIr, LightElevationDeg = sunElevation };
            env.Sensing.Tapes.Add(new FloorTape { Finish = TapeFinish.Matte, Points = { new[] { -0.5, -0.2 }, new[] { 0.5, -0.2 } } });
            env.Sensing.Tapes.Add(new FloorTape { Finish = TapeFinish.Faded, Wear = 0.6, Points = { new[] { -0.5, 0.0 }, new[] { 0.5, 0.0 } } });
            env.Sensing.Tapes.Add(new FloorTape { Finish = TapeFinish.Glossy, Points = { new[] { -0.5, 0.2 }, new[] { 0.5, 0.2 } } });
            return env;
        }

        private static ReflectiveReading Look(FloorOptics floor, double x, double y, double heightMm, double tiltDeg = 0, ReflectiveSensorSpec? spec = null)
        {
            // Tilt about the X axis (rolls the emitter→detector baseline, which lies along Y, out of the floor plane).
            double t = tiltDeg * Math.PI / 180;
            var axis = new Vec3(0, Math.Sin(t), -Math.Cos(t));
            var baseline = new Vec3(0, Math.Cos(t), Math.Sin(t));
            return ReflectiveOptics.Evaluate(spec ?? ReflectiveSensorSpec.Tcrt5000, floor, new Vec3(x, y, heightMm / 1000), axis, baseline);
        }

        [Fact]
        public void Tcrt5000DistanceCurvePeaksAtTwoAndAHalfMillimetres()
        {
            var s = ReflectiveSensorSpec.Tcrt5000;
            Assert.Equal(1.0, s.DistanceResponse(0.0025), 6);
            Assert.True(s.DistanceResponse(0.001) < 0.7);
            Assert.True(s.DistanceResponse(0.006) is > 0.45 and < 0.75);
            Assert.True(s.DistanceResponse(0.015) < 0.2);
        }

        [Fact]
        public void MatteTapeContrastIsHighAtTheRightHeightAndCollapsesWhenTooHigh()
        {
            var floor = new FloorOptics(Track());
            foreach (double h in new[] { 1.0, 2.5, 5, 8, 12 })
            {
                var white = Look(floor, 0.3, 0.4, h);
                var matte = Look(floor, 0.3, -0.2, h);
                var faded = Look(floor, 0.3, 0.0, h);
                var glossy = Look(floor, 0.3, 0.2, h);
                _out.WriteLine($"h={h,4} mm  white {white.Signal:F3}  matte {matte.Signal:F3}  faded {faded.Signal:F3}  glossy {glossy.Signal:F3} (spec {glossy.Specular:F3})");
            }

            var w25 = Look(floor, 0.3, 0.4, 2.5);
            var m25 = Look(floor, 0.3, -0.2, 2.5);
            Assert.InRange(w25.Signal, 0.85, 1.0);
            Assert.True(m25.Signal < 0.1);
            Assert.True(w25.Signal / m25.Signal > 8);
            // Too high: white itself is weak, the difference a comparator sees is small.
            var w12 = Look(floor, 0.3, 0.4, 12);
            var m12 = Look(floor, 0.3, -0.2, 12);
            Assert.True(w12.Signal - m12.Signal < 0.25 * (w25.Signal - m25.Signal));
        }

        [Fact]
        public void FadedTapeReadsMuchBrighterAndPatchy()
        {
            var floor = new FloorOptics(Track());
            var values = Enumerable.Range(0, 80).Select(i => Look(floor, -0.4 + i * 0.01, 0.0, 2.5).Signal).ToList();
            double matte = Look(floor, 0.1, -0.2, 2.5).Signal;
            _out.WriteLine($"faded: min {values.Min():F3} mean {values.Average():F3} max {values.Max():F3}; matte {matte:F3}");
            Assert.True(values.Average() > 3 * matte);
            Assert.True(values.Max() - values.Min() > 0.1);
            // Some spots read brighter than the usual 50 % comparator setting: a digital module sees "white" there.
            Assert.Contains(values, v => v > 0.45);
        }

        [Fact]
        public void GlossyTapeGlaresOnlyAtSomeHeightsAndTilts()
        {
            var floor = new FloorOptics(Track());
            double matteLow = Look(floor, 0.1, -0.2, 2.5).Signal, glossyLow = Look(floor, 0.1, 0.2, 2.5).Signal;
            Assert.True(glossyLow < 1.5 * matteLow + 0.02, $"glossy {glossyLow:F3} vs matte {matteLow:F3} at 2.5 mm");

            double best = 0, bestH = 0, bestT = 0;
            for (double h = 2; h <= 14; h += 1)
            {
                for (double t = -25; t <= 25; t += 2.5)
                {
                    double g = Look(floor, 0.1, 0.2, h, t).Signal, m = Look(floor, 0.1, -0.2, h, t).Signal;
                    if (g / Math.Max(1e-6, m) > best)
                    {
                        best = g / m;
                        bestH = h;
                        bestT = t;
                    }
                }
            }

            _out.WriteLine($"worst glare: glossy/matte = {best:F1} at h = {bestH} mm, tilt = {bestT}°");
            Assert.True(best > 2.5);
        }

        [Fact]
        public void SunlightMirroredByGlossyTapeSaturatesTheSensor()
        {
            var floor = new FloorOptics(Track(ambientIr: 1.0, sunElevation: 90));
            var glossy = Look(floor, 0.1, 0.2, 6);
            var dark = new FloorOptics(Track(ambientIr: 0));
            var white = Look(dark, 0.1, 0.4, 6);
            _out.WriteLine($"sun overhead: glossy {glossy.Signal:F3} (ambient {glossy.Ambient:F3}) vs white without sun {white.Signal:F3}");
            Assert.True(glossy.Signal > white.Signal);
        }

        // ================================================================ electrical

        private static (WorldDocument World, WorldObject Sensor) LineWorld(string preset, double x, double y, double heightMm)
        {
            var w = new WorldDocument();
            w.Environment = Track();
            var s = w.AddComponent(Lib, preset, new WorldPose { X = x, Y = y, Z = heightMm / 1000 });
            var bat = w.AddComponent(Lib, "bat-4nimh", new WorldPose { X = -0.8, Y = -0.8 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = s.Id, Pin = "VCC" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = s.Id, Pin = "GND" });
            return (w, s);
        }

        private static (double Ao, double Do) RunTcrt(double x, double y, double heightMm)
        {
            var (w, s) = LineWorld("tcrt5000-module", x - 0.0115, y, heightMm);
            var wc = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            var sensing = WorldSensing.Build(w, wc, null);
            for (int i = 0; i < 100; i++)
            {
                sim.Step(1e-4);
                sensing.Advance(sim.Time, 1e-4, sim);
            }

            var r = wc.ComponentRef(s.Id)!;
            return (wc.Compiled.PinVoltage(sim, r, "AO") - wc.Compiled.PinVoltage(sim, r, "GND"), wc.Compiled.PinVoltage(sim, r, "DO") - wc.Compiled.PinVoltage(sim, r, "GND"));
        }

        [Fact]
        public void Tcrt5000ModuleOutputsFollowTheFloor()
        {
            var white = RunTcrt(0.3, 0.45, 2.5);
            var matte = RunTcrt(0.3, -0.2, 2.5);
            var high = RunTcrt(0.3, 0.45, 15);
            _out.WriteLine($"white AO {white.Ao:F2} V DO {white.Do:F2} V · matte AO {matte.Ao:F2} V DO {matte.Do:F2} V · white at 15 mm AO {high.Ao:F2} V DO {high.Do:F2} V");
            Assert.True(white.Ao < 1.0, "white floor pulls AO low");
            Assert.True(white.Do < 0.5, "DO low on white");
            Assert.True(matte.Ao > 3.5, "black tape leaves AO high");
            Assert.True(matte.Do > 3.5, "DO high on black");
            Assert.True(high.Ao > 3.0 && high.Do > 3.0, "15 mm above white: too little light, reads like black");
        }

        [Fact]
        public void FiveChannelModuleMisreadsFadedTape()
        {
            // Middle channel over the faded tape, outer channels over white.
            var (w, s) = LineWorld("line-5ch-module", 0.1, 0.0, 2.5);
            var wc = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-4 });
            sim.Initialize(InitialState.OperatingPoint);
            var sensing = WorldSensing.Build(w, wc, null);
            int whiteReads = 0, samples = 0;
            for (double x = -0.35; x < 0.35; x += 0.01)
            {
                s.Pose.X = x;
                for (int i = 0; i < 20; i++)
                {
                    sim.Step(1e-4);
                    sensing.Advance(sim.Time, 1e-4, sim);
                }

                var r = sensing.Sensors.Get<LineSensorReading>(s.Id)!;
                samples++;
                if (r.Channels[2].DigitalLow == true) whiteReads++;
            }

            _out.WriteLine($"faded tape read as white in {whiteReads}/{samples} positions");
            Assert.True(whiteReads > 0, "worn spots trip the comparator");
        }

        [Fact]
        public void QtrRcDischargeTimeIsShortOnWhiteAndLongOnBlack()
        {
            double Decay(double y)
            {
                var (w, s) = LineWorld("qtr-8rc-array", 0.1, y, 2.0);
                var wc = WorldCompiler.Compile(w, Lib);
                var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 2e-6 });
                sim.Initialize(InitialState.OperatingPoint);
                var sensing = WorldSensing.Build(w, wc, null);
                for (int i = 0; i < 300; i++)
                {
                    sim.Step(2e-6);
                    sensing.Advance(sim.Time, 2e-6, sim);
                }

                // Charge OUT4 through a 5 V "pin" then release it and time the fall below 2.2 V.
                var r = wc.ComponentRef(s.Id)!;
                int node = wc.Compiled.PinNode(r, "OUT4");
                int vcc = wc.Compiled.PinNode(r, "VCC"), gnd = wc.Compiled.PinNode(r, "GND");
                // A GPIO pin: drives 5 V through ≈50 Ω, then becomes a high-impedance input.
                var drive = new RobotTwin.CoreSim.Analog.Devices.ControlledSource("drive", node, vcc, gnd, false) { Level = 4.8, OutputResistance = 50, SupplyCurrent = 0 };
                wc.Compiled.Circuit.Add(drive);
                var sim2 = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 2e-6 });
                sim2.Initialize(InitialState.OperatingPoint);
                for (int i = 0; i < 50; i++)
                {
                    sim2.Step(2e-6);
                    sensing.Advance(sim2.Time, 2e-6, sim2);
                }

                drive.OutputResistance = 1e9;
                drive.Level = 0;
                double t0 = sim2.Time;
                while (sim2.Time - t0 < 5e-3)
                {
                    sim2.Step(2e-6);
                    sensing.Advance(sim2.Time, 2e-6, sim2);
                    if (sim2.NodeVoltage(node) - wc.Compiled.PinVoltage(sim2, r, "GND") < 2.2) break;
                }

                return sim2.Time - t0;
            }

            double white = Decay(0.45), black = Decay(-0.2 - 0.0047625);
            _out.WriteLine($"QTR-8RC decay: white {white * 1e6:F0} µs, black {black * 1e6:F0} µs");
            Assert.True(white < 150e-6);
            Assert.True(black > 4 * white);
        }

        // ================================================================ cameras

        [Fact]
        public void CameraFrameRatesAndSizesFollowTheDatasheets()
        {
            var ov2640 = CameraModels.Find("OV2640");
            Assert.Equal(15, ov2640.SensorFps(1600, 1200));
            Assert.Equal(30, ov2640.SensorFps(800, 600));
            Assert.Equal(60, ov2640.SensorFps(320, 240));
            Assert.Equal(30, CameraModels.Find("OV7670").SensorFps(640, 480));
            Assert.Equal(90, CameraModels.Find("IMX219").SensorFps(640, 480));
            Assert.Equal(30, CameraModels.Find("IMX219").SensorFps(1920, 1080));

            Assert.Equal(153600, CameraModels.FrameBytes(320, 240, CameraPixelFormat.Rgb565));
            Assert.Equal(19200, CameraModels.FrameBytes(160, 120, CameraPixelFormat.Grayscale));
            int uxgaJpeg = CameraModels.FrameBytes(1600, 1200, CameraPixelFormat.Jpeg, 12, 1.0);
            _out.WriteLine($"UXGA JPEG q12 ≈ {uxgaJpeg / 1024} kB");
            Assert.InRange(uxgaJpeg, 100_000, 300_000);

            // Raw VGA over the ESP32's 8-bit DVP at 20 MHz: bandwidth-limited below the sensor's 30 fps.
            double vga = CameraModels.Esp32Fps(ov2640, 640, 480, CameraPixelFormat.Rgb565);
            Assert.InRange(vga, 24, 26);
            Assert.Equal(30, CameraModels.Esp32Fps(ov2640, 640, 480, CameraPixelFormat.Jpeg));

            var vfov = ov2640.VerticalFovDeg(54.5, 1600, 1200);
            Assert.InRange(vfov, 41, 43);
            Assert.Equal(CameraModels.FrameSize(13).Width, CameraModels.ParseFrameSize("UXGA", 8).Width);
        }

        private static (double Mean, double Std, double ExposureMs, double Gain) Shoot(double lux)
        {
            var p = new CameraPipeline(CameraModels.Find("OV2640"));
            const int w = 160, h = 120;
            byte[] frame = Array.Empty<byte>();
            for (int i = 0; i < 25; i++)
            {
                frame = new byte[w * h * 4];
                for (int k = 0; k < frame.Length; k += 4)
                {
                    frame[k] = frame[k + 1] = frame[k + 2] = 110;
                    frame[k + 3] = 255;
                }

                p.Process(frame, w, h, lux, 1.0 / 30, default, 54.5);
            }

            var g = new List<double>();
            for (int y = 40; y < 80; y++)
            {
                for (int x = 60; x < 100; x++) g.Add(frame[(y * w + x) * 4 + 1]);
            }

            double mean = g.Average();
            return (mean, Math.Sqrt(g.Sum(v => (v - mean) * (v - mean)) / g.Count), p.ExposureTime * 1000, p.Gain);
        }

        [Fact]
        public void DimLightMeansLongExposureHighGainAndNoise()
        {
            var bright = Shoot(3000);
            var dim = Shoot(8);
            _out.WriteLine($"3000 lx: mean {bright.Mean:F1} σ {bright.Std:F2} exp {bright.ExposureMs:F2} ms gain {bright.Gain:F1}");
            _out.WriteLine($"   8 lx: mean {dim.Mean:F1} σ {dim.Std:F2} exp {dim.ExposureMs:F2} ms gain {dim.Gain:F1}");
            Assert.True(dim.ExposureMs > 4 * bright.ExposureMs);
            Assert.True(dim.Gain > bright.Gain);
            Assert.True(dim.Std > 2 * bright.Std);
            Assert.InRange(bright.Mean, 80, 135);   // auto exposure settles near its target
        }

        // ================================================================ LiDAR, GPS, IMU, compass

        [Fact]
        public void LidarScanMatchesTheRoomWithOnePercentNoise()
        {
            var sc = new LidarScanner(LidarSpec.A1, 5.5, 0, 3);
            Assert.InRange(sc.SamplesPerRevolution, 1400, 1500);
            double t = 0;
            while (sc.Latest == null && t < 1)
            {
                sc.Advance(t, 1e-3, new Vec3(0, 0, 0.05), Quat.Identity, (o, d, max) => (2.0, 0.8));
                t += 1e-3;
            }

            Assert.NotNull(sc.Latest);
            Assert.InRange(t, 1 / 5.5 - 0.01, 1 / 5.5 + 0.01);
            var r = sc.Latest!.Ranges.Where(x => !float.IsNaN(x)).Select(x => (double)x).ToList();
            double mean = r.Average(), std = Math.Sqrt(r.Sum(v => (v - mean) * (v - mean)) / r.Count);
            _out.WriteLine($"{r.Count} samples, mean {mean:F4} m, σ {std * 1000:F1} mm");
            Assert.InRange(mean, 1.995, 2.005);
            Assert.InRange(std, 0.012, 0.03);

            // Out of range / dark target: no return.
            var far = new LidarScanner(LidarSpec.A1, 5.5);
            for (int i = 0; i < 200; i++) far.Advance(i * 1e-3, 1e-3, Vec3.Zero, Quat.Identity, (o, d, max) => (11.0, 0.05));
            Assert.All(far.Latest!.Ranges, x => Assert.True(float.IsNaN(x)));
        }

        [Fact]
        public void SixteenChannelLidarScansEveryElevation()
        {
            var sc = new LidarScanner(LidarSpec.Vlp16, 10, 1.0, 5);
            Assert.Equal(360, sc.SamplesPerRevolution);
            for (int i = 0; i < 120 && sc.Latest == null; i++)
            {
                // Floor 1 m below: only the downward channels return.
                sc.Advance(i * 1e-3, 1e-3, new Vec3(0, 0, 1.0), Quat.Identity, (o, d, max) => d.Z < -1e-6 ? (-o.Z / d.Z, 0.5) : (double.PositiveInfinity, 0));
            }

            var scan = sc.Latest!;
            Assert.Equal(360 * 16, scan.Ranges.Length);
            Assert.Equal(16, scan.ElevationsDeg.Distinct().Count());
            var down15 = scan.Ranges.Where((r, i) => Math.Abs(scan.ElevationsDeg[i] + 15) < 0.01 && !float.IsNaN(r)).Average(r => (double)r);
            Assert.InRange(down15, 1 / Math.Sin(15 * Math.PI / 180) - 0.05, 1 / Math.Sin(15 * Math.PI / 180) + 0.05);
            Assert.All(scan.Ranges.Where((r, i) => scan.ElevationsDeg[i] > 0), r => Assert.True(float.IsNaN(r)));
        }

        [Fact]
        public void SensingEnvironmentSurvivesSaveAndUndo()
        {
            var w = new WorldDocument();
            w.Environment = Track(0.3, 70);
            w.Environment.Sensing!.OriginLatitude = 39.9;
            var copy = WorldDocument.FromJson(w.ToJson());
            Assert.NotNull(copy.Environment.Sensing);
            Assert.Equal(3, copy.Environment.Sensing!.Tapes.Count);
            Assert.Equal(TapeFinish.Glossy, copy.Environment.Sensing.Tapes[2].Finish);
            Assert.Equal(0.3, copy.Environment.Sensing.AmbientIr, 9);
            Assert.Equal(39.9, copy.Clone().Environment.Sensing!.OriginLatitude, 9);
        }

        [Fact]
        public void GpsNeedsTimeToFixAndErrsAboutItsCep()
        {
            var env = new SensingEnvironment();
            Assert.Equal("$GPGLL,4916.45,N,12311.12,W,225444,A*31\r\n", GpsReceiver.Sentence("GPGLL,4916.45,N,12311.12,W,225444,A"));
            var rx = new GpsReceiver(GpsSpec.Neo6M, env, 42, "sıcak") { RateHz = 1 };
            Assert.False(rx.Update(0, Vec3.Zero, Vec3.Zero, true)!.Valid);
            var errors = new List<double>();
            for (int s = 1; s < 4000; s++)
            {
                var f = rx.Update(s, Vec3.Zero, Vec3.Zero, true);
                Assert.NotNull(f);
                Assert.True(f!.Valid);
                errors.Add(Math.Sqrt(f.ErrorEast * f.ErrorEast + f.ErrorNorth * f.ErrorNorth));
            }

            errors.Sort();
            double cep = errors[errors.Count / 2];
            _out.WriteLine($"median horizontal error {cep:F2} m (spec 2.5 m CEP)");
            Assert.InRange(cep, 1.2, 4.5);
            Assert.Contains("$GPGGA", rx.Latest!.Nmea);

            var (lat, lon, _) = GpsReceiver.ToGeodetic(env, 0, 111.0, 0);
            Assert.InRange((lat - env.OriginLatitude) * 111_000, 110, 112);
            Assert.Equal(env.OriginLongitude, lon, 9);

            var cold = new GpsReceiver(GpsSpec.Neo6M, env, 1, "soğuk");
            Assert.False(cold.Update(20, Vec3.Zero, Vec3.Zero, true)!.Valid);
            Assert.Equal(27, cold.TimeToFirstFix);
        }

        [Fact]
        public void ImuSeesGravityAndRotationWithDatasheetNoise()
        {
            var imu = new ImuModel(ImuSpec.Mpu6050, 1);
            var acc = new List<Vec3>();
            var gyr = new List<Vec3>();
            var q = Quat.FromAxisAngle(Vec3.UnitZ, 0.3);
            for (int i = 0; i < 2000; i++)
            {
                imu.Update(1e-3, q, Vec3.Zero, new Vec3(0, 0, 1.0), 9.81);
                acc.Add(imu.Accel);
                gyr.Add(imu.Gyro);
            }

            double az = acc.Average(a => a.Z), gz = gyr.Average(g => g.Z);
            double sz = Math.Sqrt(acc.Sum(a => (a.Z - az) * (a.Z - az)) / acc.Count);
            _out.WriteLine($"az {az:F3} m/s² σ {sz:F3} · gz {gz:F3} rad/s · σ_a(spec) {imu.AccelSigma:F3}");
            Assert.InRange(az, 9.81 - 1.5, 9.81 + 1.5);         // ±bias (40 mg 1σ, a few σ allowed)
            Assert.InRange(gz, 1.0 - 0.25, 1.0 + 0.25);
            Assert.InRange(sz, 0.5 * imu.AccelSigma, 1.6 * imu.AccelSigma);
        }

        [Fact]
        public void Hmc5883lReportsTheFieldThroughItsRegisters()
        {
            var m = new Hmc5883l();
            // ID registers.
            m.Start(false);
            m.Write(10);
            m.Stop();
            m.Start(true);
            Assert.Equal((byte)'H', m.Read(true));
            Assert.Equal((byte)'4', m.Read(true));
            Assert.Equal((byte)'3', m.Read(false));
            m.Noise = 0;
            m.FieldX = 0.2;
            m.FieldY = -0.1;
            m.FieldZ = -0.4;
            m.Start(false);
            m.Write(2);
            m.Write(0);   // continuous mode → measures
            m.Start(false);
            m.Write(3);
            m.Start(true);
            int Read16() => (short)((m.Read(true) << 8) | m.Read(true));
            int x = Read16(), z = Read16(), y = Read16();
            Assert.Equal((int)Math.Round(0.2 * 1090), x);
            Assert.Equal((int)Math.Round(-0.4 * 1090), z);
            Assert.Equal((int)Math.Round(-0.1 * 1090), y);
        }

        [Fact]
        public void EncoderMotorProducesQuadratureCountsAtTheShaftRate()
        {
            var w = new WorldDocument();
            var motor = w.AddComponent(Lib, "motor-n20-encoder", new WorldPose());
            var bat = w.AddComponent(Lib, "bat-4nimh", new WorldPose { X = 0.1 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "M1" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "M2" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "VCC" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "GND" });
            var wc = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 2e-5 });
            sim.Initialize(InitialState.OperatingPoint);
            var sensing = WorldSensing.Build(w, wc, null);
            var r = wc.ComponentRef(motor.Id)!;
            int edges = 0;
            bool last = false;
            while (sim.Time < 0.3)
            {
                sim.Step(2e-5);
                sensing.Advance(sim.Time, 2e-5, sim);
                bool a = wc.Compiled.PinVoltage(sim, r, "C1") - wc.Compiled.PinVoltage(sim, r, "GND") > 2.5;
                if (a != last) edges++;
                last = a;
            }

            sensing.Advance(sim.Time, 0.05, sim);   // publish the count as of now
            var e = sensing.Sensors.Get<EncoderReading>(motor.Id)!;
            double expected = Math.Abs(e.OutputAngleRad) / (2 * Math.PI) * 700 * 2;   // A-channel edges: 2 per line
            _out.WriteLine($"rpm {e.OutputRpm:F0}, count {e.Count}, A edges {edges}, expected ≈ {expected:F0}");
            Assert.True(Math.Abs(e.OutputRpm) > 100);
            Assert.InRange(edges, expected * 0.9 - 4, expected * 1.1 + 4);
            Assert.InRange(Math.Abs(e.Count), edges * 2 - 8, edges * 2 + 8);
        }
    }
}
