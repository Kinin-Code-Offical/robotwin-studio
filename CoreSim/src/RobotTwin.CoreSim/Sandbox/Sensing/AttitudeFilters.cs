using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// [fusion] An attitude estimator fed with what an IMU measured (rate in rad/s and specific force in m/s², body axes)
    /// and, when there is one, a magnetometer (any unit, body axes). <see cref="Estimate"/> turns body axes into world
    /// axes (Z up), as the physics' own orientation does, so the two compare directly. Given the world's magnetic field
    /// (<see cref="MagneticReference"/>) a filter that uses the magnetometer estimates heading against the world's axes;
    /// without it, against magnetic north taken as +X.
    /// </summary>
    public abstract class AttitudeFilter
    {
        public abstract string Name { get; }

        public Quat Estimate { get; protected set; } = Quat.Identity;

        /// <summary>The world's magnetic field (world axes, any unit); null = north along +X.</summary>
        public Vec3? MagneticReference { get; set; }

        public bool Started { get; private set; }

        /// <summary>Whether it reads the magnetometer (heading); without it a filter's heading only integrates the gyro.</summary>
        public bool UsesMagnetometer { get; set; } = true;

        public void Update(double dt, Vec3 gyro, Vec3 accel, Vec3? mag)
        {
            var m = UsesMagnetometer && mag is { } mm && mm.LengthSquared > 1e-18 ? mm : (Vec3?)null;
            if (!Started)
            {
                // Aligned from the first sample at rest: tilt from gravity, heading from the field.
                Estimate = Align(accel, m, MagneticReference);
                Started = true;
                Reset();
                return;
            }

            if (dt <= 0 || dt > 1) return;
            Step(dt, gyro, accel, m);
        }

        protected virtual void Reset() { }

        protected abstract void Step(double dt, Vec3 gyro, Vec3 accel, Vec3? mag);

        /// <summary>The world's field in world axes, or north along +X with the measured dip.</summary>
        protected Vec3 Reference(Vec3 magBody)
        {
            if (MagneticReference is { } r) return r.Normalized();
            var h = Estimate.Rotate(magBody.Normalized());
            return new Vec3(Math.Sqrt(h.X * h.X + h.Y * h.Y), 0, h.Z);
        }

        /// <summary>Roll and pitch from gravity, yaw from the field (or 0): body → world.</summary>
        public static Quat Align(Vec3 accel, Vec3? mag, Vec3? reference)
        {
            double roll = Math.Atan2(accel.Y, accel.Z);
            double pitch = Math.Atan2(-accel.X, Math.Sqrt(accel.Y * accel.Y + accel.Z * accel.Z));
            var tilt = Quat.FromAxisAngle(Vec3.UnitY, pitch) * Quat.FromAxisAngle(Vec3.UnitX, roll);
            double yaw = 0;
            if (mag is { } m)
            {
                var h = tilt.Rotate(m);   // the field with the tilt taken out
                double north = reference is { } r ? Math.Atan2(r.Y, r.X) : 0;
                yaw = north - Math.Atan2(h.Y, h.X);
            }

            return (Quat.FromAxisAngle(Vec3.UnitZ, yaw) * tilt).Normalized();
        }

        /// <summary>q advanced by body rates over dt (exact for a constant rate).</summary>
        protected static Quat Integrate(Quat q, Vec3 rate, double dt)
        {
            double angle = rate.Length * dt;
            return angle < 1e-15 ? q : (q * Quat.FromAxisAngle(rate / rate.Length, angle)).Normalized();
        }

        /// <summary>World +Z (up) in body axes for an orientation.</summary>
        protected static Vec3 Up(Quat q) => q.InverseRotate(Vec3.UnitZ);
    }

    /// <summary>
    /// [fusion] Integrated gyro alone: exact for a perfect gyro, and a picture of how its bias and noise accumulate
    /// into tilt and heading with nothing to pull them back.
    /// </summary>
    public sealed class GyroIntegration : AttitudeFilter
    {
        public override string Name => "Yalnız jiroskop";

        protected override void Step(double dt, Vec3 gyro, Vec3 accel, Vec3? mag) => Estimate = Integrate(Estimate, gyro, dt);
    }

    /// <summary>
    /// [fusion] Complementary filter on angles: the gyro's rates integrated for the short term, the accelerometer's
    /// tilt (and the magnetometer's tilt-compensated heading) pulled in for the long term with weight 1 − α per step,
    /// α = τ/(τ + dt). The textbook first filter, and what most hobby flight code runs.
    /// </summary>
    public sealed class ComplementaryFilter : AttitudeFilter
    {
        private double _roll, _pitch, _yaw;

        public override string Name => "Tümleyen";

        /// <summary>Time constant (s) over which the accelerometer and magnetometer correct the gyro.</summary>
        public double TimeConstant { get; set; } = 1.0;

        protected override void Reset() => (_roll, _pitch, _yaw) = Estimate.ToEuler();

        protected override void Step(double dt, Vec3 gyro, Vec3 accel, Vec3? mag)
        {
            // Body rates → Euler rates (ZYX).
            double sr = Math.Sin(_roll), cr = Math.Cos(_roll), cp = Math.Cos(_pitch), tp = Math.Tan(_pitch);
            if (Math.Abs(cp) < 1e-6) cp = 1e-6 * Math.Sign(cp == 0 ? 1 : cp);
            _roll += (gyro.X + sr * tp * gyro.Y + cr * tp * gyro.Z) * dt;
            _pitch += (cr * gyro.Y - sr * gyro.Z) * dt;
            _yaw += (sr / cp * gyro.Y + cr / cp * gyro.Z) * dt;

            double alpha = TimeConstant / (TimeConstant + dt);
            var measured = Align(accel, mag, MagneticReference).ToEuler();
            _roll = Blend(_roll, measured.Roll, alpha);
            _pitch = Blend(_pitch, measured.Pitch, alpha);
            if (mag != null) _yaw = Blend(_yaw, measured.Yaw, alpha);
            Estimate = (Quat.FromAxisAngle(Vec3.UnitZ, _yaw) * Quat.FromAxisAngle(Vec3.UnitY, _pitch) * Quat.FromAxisAngle(Vec3.UnitX, _roll)).Normalized();
        }

        /// <summary>α·a + (1 − α)·b along the shorter way round.</summary>
        private static double Blend(double a, double b, double alpha)
        {
            double d = Math.IEEERemainder(b - a, 2 * Math.PI);
            return Math.IEEERemainder(a + (1 - alpha) * d, 2 * Math.PI);
        }
    }

    /// <summary>
    /// [fusion] Mahony's explicit complementary filter on SO(3): the cross product between measured and estimated
    /// gravity (and field) is fed back into the rates through a proportional gain and an integral one, and the integral
    /// settles at the gyro's bias — so it also estimates the bias, <see cref="GyroBias"/>.
    /// </summary>
    public sealed class MahonyFilter : AttitudeFilter
    {
        private Vec3 _integral;

        public override string Name => "Mahony";

        public double Kp { get; set; } = 1.0;

        public double Ki { get; set; } = 0.05;

        /// <summary>Its estimate of the gyro's bias (rad/s): what the integral term subtracts.</summary>
        public Vec3 GyroBias => -_integral;

        protected override void Reset() => _integral = Vec3.Zero;

        protected override void Step(double dt, Vec3 gyro, Vec3 accel, Vec3? mag)
        {
            var e = Vec3.Zero;
            if (accel.LengthSquared > 1e-12) e += accel.Normalized().Cross(Up(Estimate));
            if (mag is { } m)
            {
                var b = Reference(m);
                e += m.Normalized().Cross(Estimate.InverseRotate(b));
            }

            _integral += e * (Ki * dt);
            Estimate = Integrate(Estimate, gyro + e * Kp + _integral, dt);
        }
    }

    /// <summary>
    /// [fusion] Madgwick's gradient-descent filter: each step moves the gyro-integrated orientation down the gradient of
    /// the mismatch between where gravity (and the field) should appear in body axes and where they were measured, by
    /// β rad/s. The gradient is taken numerically over the quaternion's four components, the same objective as the
    /// paper's closed form.
    /// </summary>
    public sealed class MadgwickFilter : AttitudeFilter
    {
        public override string Name => "Madgwick";

        /// <summary>Gradient step (rad/s): √(3/4)·gyro error, 0.1 for a hobby MEMS gyro.</summary>
        public double Beta { get; set; } = 0.1;

        private static double Mismatch(Quat q, Vec3 a, Vec3? m, Vec3 b)
        {
            var g = q.InverseRotate(Vec3.UnitZ) - a;
            double f = g.LengthSquared;
            if (m is { } mm) f += (q.InverseRotate(b) - mm).LengthSquared;
            return 0.5 * f;
        }

        protected override void Step(double dt, Vec3 gyro, Vec3 accel, Vec3? mag)
        {
            var q = Estimate;
            // Rate of change from the gyro: ½ q ⊗ (0, ω).
            var dot = Half(q * new Quat(0, gyro.X, gyro.Y, gyro.Z));
            if (accel.LengthSquared > 1e-12)
            {
                var a = accel.Normalized();
                Vec3? m = mag?.Normalized();
                var b = m is { } mv ? Reference(mv) : Vec3.Zero;
                const double h = 1e-6;
                double G(int i)
                {
                    Quat Step(double s) => new Quat(q.W + (i == 0 ? s : 0), q.X + (i == 1 ? s : 0), q.Y + (i == 2 ? s : 0), q.Z + (i == 3 ? s : 0)).Normalized();
                    return (Mismatch(Step(h), a, m, b) - Mismatch(Step(-h), a, m, b)) / (2 * h);
                }

                double g0 = G(0), g1 = G(1), g2 = G(2), g3 = G(3);
                double n = Math.Sqrt(g0 * g0 + g1 * g1 + g2 * g2 + g3 * g3);
                if (n > 1e-12) dot = new Quat(dot.W - Beta * g0 / n, dot.X - Beta * g1 / n, dot.Y - Beta * g2 / n, dot.Z - Beta * g3 / n);
            }

            Estimate = new Quat(q.W + dot.W * dt, q.X + dot.X * dt, q.Y + dot.Y * dt, q.Z + dot.Z * dt).Normalized();
        }

        private static Quat Half(Quat q) => new Quat(0.5 * q.W, 0.5 * q.X, 0.5 * q.Y, 0.5 * q.Z);
    }
}
