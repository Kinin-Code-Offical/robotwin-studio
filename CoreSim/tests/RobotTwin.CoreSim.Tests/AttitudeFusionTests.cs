using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[fusion] Attitude filters on a simulated MPU-6050 and compass, scored against the true orientation.</summary>
    public class AttitudeFusionTests
    {
        private readonly ITestOutputHelper _out;

        public AttitudeFusionTests(ITestOutputHelper output) => _out = output;

        /// <summary>
        /// A body tumbling slowly in place for a minute (rates up to 0.5 rad/s on every axis), read by the IMU model with
        /// its turn-on bias, bias walk, white noise and warm-up, and by a field-only magnetometer with 1 % noise.
        /// </summary>
        private FusionExperiment Run(int seed, bool compass, double seconds = 60)
        {
            var world = new WorldDocument();
            var fx = new SensingEnvironment().MagneticField();
            var field = new Vec3(fx.X, fx.Y, fx.Z);
            var x = new FusionExperiment(world, "imu");
            if (!compass)
                foreach (var f in x.Filters) f.UsesMagnetometer = false;
            var imu = new ImuModel(ImuSpec.Mpu6050, seed);
            var rng = new Random(seed);
            var q = Quat.FromAxisAngle(Vec3.UnitZ, 0.6) * Quat.FromAxisAngle(Vec3.UnitX, 0.2);
            const double dt = 0.01;
            for (double t = 0; t < seconds; t += dt)
            {
                var body = new Vec3(0.5 * Math.Sin(0.7 * t), 0.4 * Math.Cos(0.5 * t), 0.3 * Math.Sin(0.23 * t));
                var world_ = q.Rotate(body);
                imu.Update(dt, q, Vec3.Zero, world_, 9.81, addNoise: true, mountC: 25);
                var m = q.InverseRotate(field);
                m += new Vec3(rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5) * (0.02 * field.Length);
                x.Add(t, t == 0 ? 0 : dt, imu.Gyro, imu.Accel, compass ? m : null, q);
                q = (q * Quat.FromAxisAngle(body.Normalized(), body.Length * dt)).Normalized();
            }

            foreach (var line in x.Summary()) _out.WriteLine(line);
            return x;
        }

        private static FusionExperiment.Score Of<T>(FusionExperiment x) => x.Scores.Single(s => s.Filter is T);

        [Fact]
        public void FiltersHoldTheTiltThatTheGyroAloneLoses()
        {
            var x = Run(3, compass: false);
            double gyro = Of<GyroIntegration>(x).TiltRmsDeg, comp = Of<ComplementaryFilter>(x).TiltRmsDeg,
                   mahony = Of<MahonyFilter>(x).TiltRmsDeg, madgwick = Of<MadgwickFilter>(x).TiltRmsDeg;
            Assert.True(gyro > 20, $"gyro alone {gyro:0.0}°");   // a 3 °/s turn-on bias for a minute
            Assert.InRange(comp, 0, 6);
            Assert.InRange(mahony, 0, 3);
            Assert.InRange(madgwick, 0, 4);
            // Mahony's integral learns the gyro's bias.
            var mf = (MahonyFilter)Of<MahonyFilter>(x).Filter;
            _out.WriteLine($"Mahony's bias estimate {mf.GyroBias * 180 / Math.PI} °/s");
            Assert.True(mf.GyroBias.Length * 180 / Math.PI > 0.5);
        }

        [Fact]
        public void TheCompassHoldsTheHeading()
        {
            var without = Run(5, compass: false);
            var with = Run(5, compass: true);
            double drift = Of<MahonyFilter>(without).HeadingRmsDeg, held = Of<MahonyFilter>(with).HeadingRmsDeg;
            _out.WriteLine($"Mahony heading RMS: {drift:0.0}° without the compass, {held:0.0}° with it");
            Assert.True(held < 5);
            Assert.True(drift > 3 * held);
            Assert.InRange(Of<MadgwickFilter>(with).HeadingRmsDeg, 0, 8);
            Assert.InRange(Of<ComplementaryFilter>(with).HeadingRmsDeg, 0, 10);
            Assert.Contains("mahony_tilt_err_deg", with.Csv().Split('\n')[0]);
        }
    }
}
