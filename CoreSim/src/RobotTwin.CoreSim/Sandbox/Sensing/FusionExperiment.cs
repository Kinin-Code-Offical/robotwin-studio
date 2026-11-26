using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// [fusion] Attitude filters run side by side on one IMU's readings (and a compass on the same body) while the world
    /// runs, each scored against the orientation the physics knows: the tilt error (angle between the true and the
    /// estimated up) and the heading error, now and as RMS over the run. Every filter sees the same samples, with the
    /// sensors' real noise, bias, temperature drift and fault scenarios, so the comparison is the one a bench test would
    /// give — with the truth beside it.
    /// </summary>
    public sealed class FusionExperiment
    {
        private readonly WorldDocument _world;
        private WorldSensors? _source;
        private double _last = double.NaN;
        private Vec3? _mag;
        private Quat _compassToImu = Quat.Identity;
        private readonly StringBuilder _csv = new StringBuilder();

        public sealed class Score
        {
            public AttitudeFilter Filter = null!;
            public double TiltDeg, HeadingDeg;
            internal double TiltSq, HeadingSq;
            internal int Count;

            public double TiltRmsDeg => Count == 0 ? 0 : Math.Sqrt(TiltSq / Count);

            public double HeadingRmsDeg => Count == 0 ? 0 : Math.Sqrt(HeadingSq / Count);

            public double MaxTiltDeg { get; internal set; }
        }

        public FusionExperiment(WorldDocument world, string imuObjectId, string? compassObjectId = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            ImuObjectId = imuObjectId;
            CompassObjectId = compassObjectId;
            // The compass's axes in the IMU's: both ride the same body, so their placed poses fix it for the run.
            if (compassObjectId != null && world.Find(imuObjectId) is { } imu && world.Find(compassObjectId) is { } compass)
                _compassToImu = (imu.Pose.Orientation.Conjugate * compass.Pose.Orientation).Normalized();
            var reference = world.Environment.Sensing?.MagneticField() ?? new SensingEnvironment().MagneticField();
            var field = new Vec3(reference.X, reference.Y, reference.Z);
            Filters = new AttitudeFilter[] { new GyroIntegration(), new ComplementaryFilter(), new MahonyFilter(), new MadgwickFilter() };
            foreach (var f in Filters) f.MagneticReference = field;
            Scores = Filters.Select(f => new Score { Filter = f }).ToList();
            _csv.Append("time_s,true_roll_deg,true_pitch_deg,true_yaw_deg");
            foreach (var f in Filters)
            {
                string k = Key(f);
                _csv.Append(',').Append(k).Append("_roll_deg,").Append(k).Append("_pitch_deg,").Append(k).Append("_yaw_deg,").Append(k).Append("_tilt_err_deg,").Append(k).Append("_heading_err_deg");
            }

            _csv.Append('\n');
        }

        public string ImuObjectId { get; }

        public string? CompassObjectId { get; }

        public IReadOnlyList<AttitudeFilter> Filters { get; }

        public IReadOnlyList<Score> Scores { get; }

        /// <summary>Samples taken (the IMU's published rate).</summary>
        public int Samples { get; private set; }

        public bool HasCompass => _mag != null;

        public void Attach(WorldSensors sensors)
        {
            Detach();
            _source = sensors;
            sensors.Published += OnPublished;
        }

        public void Detach()
        {
            if (_source != null) _source.Published -= OnPublished;
            _source = null;
        }

        private static string Key(AttitudeFilter f) => f switch
        {
            GyroIntegration => "gyro",
            ComplementaryFilter => "complementary",
            MahonyFilter => "mahony",
            MadgwickFilter => "madgwick",
            _ => f.Name.ToLowerInvariant()
        };

        private static double Deg(double rad) => rad * 180 / Math.PI;

        private void OnPublished(SensorReading r)
        {
            if (r is CompassReading c && string.Equals(c.ObjectId, CompassObjectId, StringComparison.Ordinal))
            {
                _mag = _compassToImu.Rotate(c.Field);
                return;
            }

            if (r is not ImuReading imu || !string.Equals(imu.ObjectId, ImuObjectId, StringComparison.Ordinal)) return;
            double dt = double.IsNaN(_last) ? 0 : imu.Time - _last;
            _last = imu.Time;
            Add(imu.Time, dt, imu.Gyro, imu.Accel, _mag, imu.Orientation);
        }

        /// <summary>One sample: every filter steps, and is scored against the true orientation.</summary>
        public void Add(double time, double dt, Vec3 gyro, Vec3 accel, Vec3? mag, Quat truth)
        {
            Samples++;
            var trueUp = truth.InverseRotate(Vec3.UnitZ);
            var (tr, tp, ty) = truth.ToEuler();
            var inv = CultureInfo.InvariantCulture;
            _csv.Append(time.ToString("0.#########", inv)).Append(',').Append(Deg(tr).ToString("0.###", inv)).Append(',').Append(Deg(tp).ToString("0.###", inv)).Append(',').Append(Deg(ty).ToString("0.###", inv));
            foreach (var s in Scores)
            {
                s.Filter.Update(dt, gyro, accel, mag);
                var est = s.Filter.Estimate;
                double tilt = Deg(Math.Acos(Math.Max(-1, Math.Min(1, est.InverseRotate(Vec3.UnitZ).Dot(trueUp)))));
                var (er, ep, ey) = est.ToEuler();
                // The heading error is the error rotation's turn about the vertical (its twist about world Z), defined
                // however the body is tilted — an Euler yaw difference is not, near pitch ±90°.
                var delta = truth * est.Conjugate;
                double heading = Deg(Math.Abs(Math.IEEERemainder(2 * Math.Atan2(delta.Z, delta.W), 2 * Math.PI)));
                s.TiltDeg = tilt;
                s.HeadingDeg = heading;
                s.TiltSq += tilt * tilt;
                s.HeadingSq += heading * heading;
                s.Count++;
                s.MaxTiltDeg = Math.Max(s.MaxTiltDeg, tilt);
                _csv.Append(',').Append(Deg(er).ToString("0.###", inv)).Append(',').Append(Deg(ep).ToString("0.###", inv)).Append(',').Append(Deg(ey).ToString("0.###", inv))
                    .Append(',').Append(tilt.ToString("0.###", inv)).Append(',').Append(heading.ToString("0.###", inv));
            }

            _csv.Append('\n');
        }

        /// <summary>Every sample as CSV: the true roll, pitch and yaw, and each filter's with its tilt and heading error.</summary>
        public string Csv() => _csv.ToString();

        /// <summary>The scores in a line each ("Mahony: eğim 0.4° (RMS 0.3°) · yön 1.2°").</summary>
        public IEnumerable<string> Summary() => Scores.Select(s => string.Format(CultureInfo.InvariantCulture,
            "{0}: eğim {1:0.0}° (RMS {2:0.0}°) · yön {3:0.0}° (RMS {4:0.0}°)", s.Filter.Name, s.TiltDeg, s.TiltRmsDeg, s.HeadingDeg, s.HeadingRmsDeg));
    }
}
