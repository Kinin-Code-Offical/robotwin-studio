using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using RobotTwin.CoreSim.Sdk;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[air] [temp] The world's temperature reaching its air and its sensors.</summary>
    public class TemperatureDriftTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public TemperatureDriftTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AirFollowsItsTemperature()
        {
            Assert.Equal(1.225, Atmosphere.Density(15, Atmosphere.SeaLevelPressure), 3);
            Assert.Equal(1.184, Atmosphere.Density(25, Atmosphere.SeaLevelPressure), 3);
            Assert.Equal(331.3, Atmosphere.SpeedOfSound(0), 1);
            Assert.Equal(343.2, Atmosphere.SpeedOfSound(20), 1);
            Assert.Equal(1.79e-5, Atmosphere.Viscosity(15), 7);
            // A world left at its default takes the air of its own temperature.
            Assert.Equal(Atmosphere.Density(25, Atmosphere.SeaLevelPressure), new WorldSpec().ToArena().AirDensity, 6);
            Assert.Equal(Atmosphere.Density(-10, Atmosphere.SeaLevelPressure), new WorldSpec { AmbientC = -10 }.ToArena().AirDensity, 6);
            Assert.Equal(0.02, new WorldSpec { AmbientC = -60, AirDensity = 0.02 }.ToArena().AirDensity, 6);
        }

        private static double EchoWidth(double airC, double distance)
        {
            var c = new AnalogCircuit();
            int vcc = c.Node("vcc"), trig = c.Node("trig"), echo = c.Node("echo");
            c.Add(new VoltageSource("V1", vcc, 0, 5.0));
            var pulse = c.Add(new VoltageSource("VT", trig, 0, 0.0));
            var sonar = c.Add(new UltrasonicSensor("U1", vcc, trig, echo, 0) { Distance = distance, AirTemperatureC = airC, RangeNoise = 0 });
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            sim.Step(1e-4);
            pulse.Waveform = 5.0;
            sim.Step(12e-6);
            pulse.Waveform = 0.0;
            sim.Step(20e-3);
            return sonar.LastPulseWidth;
        }

        [Fact]
        public void AnHcSr04EchoesAtTheSpeedOfSoundInItsAir()
        {
            double cold = EchoWidth(0, 1.0), hot = EchoWidth(40, 1.0);
            // A sketch dividing by 58 µs/cm (345 m/s): 1 m reads 104 cm at 0 °C and 97 cm at 40 °C.
            _out.WriteLine($"1 m: {cold * 1e3:0.000} ms at 0 °C → {cold * 1e6 / 58:0.0} cm, {hot * 1e3:0.000} ms at 40 °C → {hot * 1e6 / 58:0.0} cm");
            Assert.Equal(2.0 / Atmosphere.SpeedOfSound(0), cold, 9);   // 331.3 m/s
            Assert.Equal(2.0 / Atmosphere.SpeedOfSound(40), hot, 9);   // 354.8 m/s

            // In a world the sensor takes the world's air.
            var w = new WorldDocument();
            w.Environment.AmbientC = -5;
            var sonar = w.AddComponent(Lib, "hc-sr04", new WorldPose());
            sonar.IsStatic = true;
            var wc = WorldCompiler.Compile(w, Lib, _ => 0.2);
            var dev = wc.Compiled.DevicesOf(wc.ComponentRef(sonar.Id)!).OfType<UltrasonicSensor>().Single();
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, wc);
            for (int i = 0; i < 50; i++) p.Advance(1e-3, null);
            Assert.Equal(-5, dev.AirTemperatureC);
        }

        [Fact]
        public void AnImuWarmsAfterPowerOnAndItsGyroDriftsWithIt()
        {
            var model = new ImuModel(ImuSpec.Mpu6050, 11);
            double dt = 0.01;
            void Run(double seconds, double mount)
            {
                for (double t = 0; t < seconds; t += dt) model.Update(dt, Quat.Identity, Vec3.Zero, Vec3.Zero, 9.81, false, mount);
            }

            Run(0.01, 25);
            var cold = model.GyroOffset;
            double t0 = model.TemperatureC;
            Run(300, 25);
            var warm = model.GyroOffset;
            double t1 = model.TemperatureC;
            Run(600, 60);   // the motor driver beside it heats the board to 60 °C
            var hot = model.GyroOffset;
            double dps(Vec3 a, Vec3 b) => (a - b).Length * 180 / Math.PI;
            _out.WriteLine($"chip {t0:0.00} → {t1:0.00} °C in 5 min: gyro zero moved {dps(warm, cold):0.000} °/s; board at 60 °C: chip {model.TemperatureC:0.0} °C, moved {dps(hot, cold):0.00} °/s");
            Assert.InRange(t0, 25, 25.01);
            Assert.InRange(t1, 27.9, 28.0);   // 3 °C of self-heating, 5 time constants
            Assert.InRange(model.TemperatureC, 62.9, 63.0);
            // The warm-up drift is real but small; 35 °C more is tens of times more (bias walk aside).
            Assert.True(dps(hot, cold) > 0.3);
            Assert.True(dps(hot, cold) > 5 * dps(warm, cold));
        }

        [Fact]
        public void TheMpu6050ReportsItsChipTemperature()
        {
            var w = new WorldDocument();
            w.Environment.AmbientC = 10;
            var imu = w.AddComponent(Lib, "gy-521", new WorldPose { Z = 0.01 });
            var bat = w.AddComponent(Lib, "bat-4nimh", new WorldPose { X = -0.5, Y = -0.5 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = imu.Id, Pin = "VCC" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = imu.Id, Pin = "GND" });
            var wc = WorldCompiler.Compile(w, Lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var sensing = WorldSensing.Build(w, wc, null);
            var chip = new Mpu6050();
            sensing.BindMpu6050(imu.Id, chip);
            double readings = 0;
            sensing.Sensors.Published += r => { if (r is ImuReading i) readings = i.TemperatureC; };
            for (int k = 0; k < 2000; k++)
            {
                sim.Step(1e-3);
                sensing.Advance(sim.Time, 1e-3, sim);
            }

            _out.WriteLine($"after 2 s at 10 °C: chip {chip.TemperatureC:0.000} °C, published {readings:0.000} °C");
            Assert.InRange(chip.TemperatureC, 10.0, 10.2);   // 2 s of a 60 s warm-up toward 13 °C
            Assert.Equal(chip.TemperatureC, readings, 2);
        }
    }
}
