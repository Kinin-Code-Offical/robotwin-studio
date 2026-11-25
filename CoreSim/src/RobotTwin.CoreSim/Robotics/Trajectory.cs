using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    public enum ProfileShape
    {
        /// <summary>Constant acceleration, cruise, constant deceleration (infinite jerk at the corners).</summary>
        Trapezoid,

        /// <summary>Jerk-limited: acceleration ramps up and down smoothly (cosine), no steps in acceleration.</summary>
        SCurve
    }

    /// <summary>
    /// Normalised motion profile s(t) from 0 to 1 in <see cref="Duration"/> seconds, with
    /// acceleration time <see cref="AccelTime"/> at each end. For distance D the physical peak
    /// speed is D·<see cref="PeakRate"/> and peak acceleration D·<see cref="PeakAccel"/>.
    /// </summary>
    public sealed class MotionProfile
    {
        public ProfileShape Shape { get; private set; }
        public double Duration { get; private set; }
        public double AccelTime { get; private set; }

        /// <summary>Peak ds/dt (1/s).</summary>
        public double PeakRate { get; private set; }

        /// <summary>Peak d²s/dt² (1/s²).</summary>
        public double PeakAccel => AccelTime <= 0 ? 0 : Shape == ProfileShape.SCurve ? 2 * PeakRate / AccelTime : PeakRate / AccelTime;

        /// <summary>
        /// Peak d³s/dt³ (1/s³): the S-curve's cosine ramp reaches 2π·rate/Ta²; the trapezoid's acceleration steps (no
        /// finite jerk: infinity).
        /// </summary>
        public double PeakJerk => AccelTime <= 0 ? 0 : Shape == ProfileShape.SCurve ? 2 * Math.PI * PeakRate / (AccelTime * AccelTime) : double.PositiveInfinity;

        public static MotionProfile Zero { get; } = new MotionProfile { Duration = 0, AccelTime = 0, PeakRate = 0 };

        /// <summary>Fastest profile covering <paramref name="distance"/> with speed and (peak) acceleration limits.</summary>
        public static MotionProfile Plan(double distance, double vmax, double amax, ProfileShape shape)
        {
            distance = Math.Abs(distance);
            if (distance < 1e-12 || vmax <= 0 || amax <= 0) return new MotionProfile { Shape = shape };
            // The S-curve reaches speed v in 2v/a (mean acceleration a/2), the trapezoid in v/a.
            double k = shape == ProfileShape.SCurve ? 2.0 : 1.0;
            double ta = k * vmax / amax;
            double v = vmax;
            if (v * ta > distance)
            {
                // Never reaches full speed: D = v·Ta = k·v²/a.
                v = Math.Sqrt(distance * amax / k);
                ta = k * v / amax;
            }

            double duration = distance / v + ta;
            return FromTimes(duration, ta, shape);
        }

        /// <summary>Normalised profile with a given duration and acceleration time (Ta ≤ T/2).</summary>
        public static MotionProfile FromTimes(double duration, double accelTime, ProfileShape shape)
        {
            if (duration <= 1e-9) return new MotionProfile { Shape = shape };
            accelTime = Math.Max(1e-6, Math.Min(duration / 2, accelTime));
            return new MotionProfile { Shape = shape, Duration = duration, AccelTime = accelTime, PeakRate = 1.0 / (duration - accelTime) };
        }

        /// <summary>Same shape stretched to a longer duration.</summary>
        public MotionProfile Stretched(double factor) =>
            factor <= 1 ? this : FromTimes(Duration * factor, AccelTime * factor, Shape);

        /// <summary>Position (0..1), rate and acceleration at time t.</summary>
        public (double S, double Rate, double Accel) Evaluate(double t)
        {
            if (Duration <= 0) return (1, 0, 0);
            if (t <= 0) return (0, 0, 0);
            if (t >= Duration) return (1, 0, 0);
            double ta = AccelTime, v = PeakRate;
            if (t < ta) return Ramp(t);
            if (t <= Duration - ta)
            {
                double sa = v * ta / 2;
                return (sa + v * (t - ta), v, 0);
            }

            var (s, r, a) = Ramp(Duration - t);
            return (1 - s, r, -a);
        }

        private (double S, double Rate, double Accel) Ramp(double t)
        {
            double ta = AccelTime, v = PeakRate;
            if (Shape == ProfileShape.Trapezoid)
            {
                double a = v / ta;
                return (0.5 * a * t * t, a * t, a);
            }

            // a(t) = A(1 − cos(2πt/Ta))/2, A = 2v/Ta.
            double A = 2 * v / ta, w = 2 * Math.PI / ta;
            double acc = A * (1 - Math.Cos(w * t)) / 2;
            double rate = A / 2 * (t - Math.Sin(w * t) / w);
            double pos = A / 2 * (t * t / 2 + (Math.Cos(w * t) - 1) / (w * w));
            return (pos, rate, acc);
        }
    }

    /// <summary>A planned motion the controller samples every control tick.</summary>
    public abstract class ArmMotion
    {
        public MotionProfile Profile { get; protected set; } = MotionProfile.Zero;
        public double Duration => Profile.Duration;

        /// <summary>Joint positions and rates at time t (t ≥ Duration gives the end point).</summary>
        public abstract void Sample(double t, double[] q, double[] qd);

        public abstract double[] End { get; }

        /// <summary>"MoveJ" / "MoveL".</summary>
        public abstract string Kind { get; }

        /// <summary>The same path in <paramref name="factor"/> times the time (≥ 1): speeds fall by the factor, accelerations by its square, jerk by its cube.</summary>
        public ArmMotion Stretched(double factor)
        {
            var copy = (ArmMotion)MemberwiseClone();
            copy.Profile = Profile.Stretched(factor);
            return copy;
        }
    }

    /// <summary>
    /// Synchronised point-to-point joint move: every axis starts and stops together, the
    /// duration set by whichever axis is limited most by its speed/acceleration rating.
    /// </summary>
    public sealed class JointMotion : ArmMotion
    {
        private readonly double[] _start, _end;

        public JointMotion(ArmModel model, IReadOnlyList<double> start, IReadOnlyList<double> end, double speedFraction, ProfileShape shape, double accelFraction = 1.0)
        {
            int n = model.Dof;
            _start = new double[n];
            _end = new double[n];
            for (int i = 0; i < n; i++)
            {
                _start[i] = start[i];
                _end[i] = end[i];
            }

            speedFraction = Math.Max(0.01, Math.Min(1.0, speedFraction));
            accelFraction = Math.Max(0.01, Math.Min(1.0, accelFraction));
            // Slowest axis sets the duration and the shape (acceleration share).
            MotionProfile? slow = null;
            for (int i = 0; i < n; i++)
            {
                var j = model.Joints[i];
                var p = MotionProfile.Plan(_end[i] - _start[i], j.MaxSpeed * speedFraction, j.MaxAccel * accelFraction, shape);
                if (slow == null || p.Duration > slow.Duration) slow = p;
            }

            Profile = slow ?? MotionProfile.Zero;
            // Make sure no axis exceeds its own limits with the shared timing.
            for (int pass = 0; pass < 3 && Profile.Duration > 0; pass++)
            {
                double factor = 1;
                for (int i = 0; i < n; i++)
                {
                    var j = model.Joints[i];
                    double d = Math.Abs(_end[i] - _start[i]);
                    double v = d * Profile.PeakRate, a = d * Profile.PeakAccel;
                    factor = Math.Max(factor, v / (j.MaxSpeed * speedFraction));
                    factor = Math.Max(factor, Math.Sqrt(a / (j.MaxAccel * accelFraction)));
                    // Jerk falls with the cube of the stretch.
                    if (j.MaxJerk > 0 && Profile.Shape == ProfileShape.SCurve) factor = Math.Max(factor, Math.Cbrt(d * Profile.PeakJerk / j.MaxJerk));
                }

                if (factor <= 1 + 1e-9) break;
                Profile = Profile.Stretched(factor * (1 + 1e-6));
            }
        }

        public override string Kind => "MoveJ";
        public override double[] End => _end;

        public override void Sample(double t, double[] q, double[] qd)
        {
            var (s, rate, _) = Profile.Evaluate(t);
            for (int i = 0; i < _start.Length; i++)
            {
                double d = _end[i] - _start[i];
                q[i] = _start[i] + d * s;
                qd[i] = d * rate;
            }
        }
    }

    /// <summary>
    /// Straight-line TCP move (linear position, spherical orientation interpolation). The path
    /// is solved to joint space up front on a dense grid so reachability, joint limits and
    /// singular configurations (sudden wrist/shoulder flips) are detected before the arm moves;
    /// the timing then respects both the Cartesian limits and every joint's speed rating.
    /// </summary>
    public sealed class LinearMotion : ArmMotion
    {
        private readonly double[][] _samples;
        private readonly double[] _end;

        private LinearMotion(double[][] samples, MotionProfile profile)
        {
            _samples = samples;
            _end = samples[samples.Length - 1];
            Profile = profile;
        }

        public override string Kind => "MoveL";
        public override double[] End => _end;

        /// <summary>Joint path samples (for tests and drawing).</summary>
        public IReadOnlyList<double[]> Path => _samples;

        public static LinearMotion? Plan(ArmModel model, IReadOnlyList<double> start, Transform3 target, Transform3 tool, double speed, ProfileShape shape, out string error, double accelFraction = 1.0)
        {
            error = string.Empty;
            int n = model.Dof;
            var from = ArmKinematics.Forward(model, start, tool);
            double dist = (target.P - from.P).Length;
            double angle = Transform3.AngleBetween(from.Q, target.Q);
            int steps = (int)Math.Ceiling(Math.Max(dist / 0.002, angle / (0.75 * Math.PI / 180)));
            steps = Math.Max(2, Math.Min(4000, steps));
            var samples = new double[steps + 1][];
            samples[0] = new double[n];
            for (int i = 0; i < n; i++) samples[0][i] = start[i];
            for (int k = 1; k <= steps; k++)
            {
                double u = k / (double)steps;
                var pose = Transform3.Lerp(from, target, u);
                var ik = ArmKinematics.Inverse(model, pose, samples[k - 1], tool);
                if (!ik.Success)
                {
                    error = $"Doğrusal yol %{u * 100:0} noktasında çözülemedi: {ik.Message}";
                    return null;
                }

                // A large joint jump between neighbouring path points = passing through a singularity.
                for (int i = 0; i < n; i++)
                {
                    double jump = Math.Abs(ik.Joints[i] - samples[k - 1][i]);
                    double allowed = model.Joints[i].IsPrismatic ? 0.02 : 12 * Math.PI / 180;
                    if (jump > allowed)
                    {
                        error = $"Doğrusal yol %{u * 100:0} noktasında tekilliğe giriyor ({model.Joints[i].Name} {model.Joints[i].ToDisplay(jump):0} {model.Joints[i].Unit} sıçrıyor)";
                        return null;
                    }
                }

                samples[k] = ik.Joints;
            }

            speed = Math.Max(1e-4, Math.Min(model.MaxLinearSpeed, speed));
            accelFraction = Math.Max(0.01, Math.Min(1.0, accelFraction));
            double amax = model.MaxLinearAccel * accelFraction;
            var pLin = MotionProfile.Plan(dist, speed, amax, shape);
            var pRot = MotionProfile.Plan(angle, model.MaxRotSpeed * Math.Min(1, speed / model.MaxLinearSpeed * 2), model.MaxRotSpeed * 4 * accelFraction, shape);
            var profile = pLin.Duration >= pRot.Duration ? pLin : pRot;
            if (profile.Duration <= 0)
            {
                // Joint-only change (e.g. same pose): nothing to do.
                profile = MotionProfile.FromTimes(0.01, 0.005, shape);
            }

            // Joint speed and acceleration along the path: q'(u)·s'(t) and q''(u)·s'² + q'(u)·s''.
            for (int pass = 0; pass < 3; pass++)
            {
                double factor = 1;
                for (int k = 1; k <= steps; k++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        double dqdu = (samples[k][i] - samples[k - 1][i]) * steps;
                        var j = model.Joints[i];
                        factor = Math.Max(factor, Math.Abs(dqdu) * profile.PeakRate / j.MaxSpeed);
                        factor = Math.Max(factor, Math.Sqrt(Math.Abs(dqdu) * profile.PeakAccel / j.MaxAccel));
                        if (j.MaxJerk > 0 && profile.Shape == ProfileShape.SCurve) factor = Math.Max(factor, Math.Cbrt(Math.Abs(dqdu) * profile.PeakJerk / j.MaxJerk));
                    }
                }

                if (factor <= 1 + 1e-9) break;
                profile = profile.Stretched(factor * 1.001);
            }

            return new LinearMotion(samples, profile);
        }

        public override void Sample(double t, double[] q, double[] qd)
        {
            var (s, rate, _) = Profile.Evaluate(t);
            int steps = _samples.Length - 1;
            double x = s * steps;
            int k = Math.Max(0, Math.Min(steps - 1, (int)Math.Floor(x)));
            double f = x - k;
            var a = _samples[k];
            var b = _samples[k + 1];
            for (int i = 0; i < q.Length; i++)
            {
                q[i] = a[i] + (b[i] - a[i]) * f;
                qd[i] = (b[i] - a[i]) * steps * rate;
            }
        }
    }
}
