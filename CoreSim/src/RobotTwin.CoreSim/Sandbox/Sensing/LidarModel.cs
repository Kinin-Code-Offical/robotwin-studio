using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>Datasheet description of a scanning LiDAR.</summary>
    public sealed class LidarSpec
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>Range measurements per second per channel.</summary>
        public double SampleRate { get; set; } = 8000;

        /// <summary>Default, slowest and fastest head rotation (Hz).</summary>
        public double ScanRate { get; set; } = 5.5;

        public double MinScanRate { get; set; } = 1;
        public double MaxScanRate { get; set; } = 10;

        public double MinRange { get; set; } = 0.15;

        /// <summary>Range on a white (≈80 % reflective) target (m); dark targets return from shorter.</summary>
        public double MaxRange { get; set; } = 12;

        /// <summary>Range noise: fraction of the distance (1σ) and a constant part (m).</summary>
        public double RangeNoiseFraction { get; set; } = 0.01;

        public double RangeNoiseFloor { get; set; } = 0.0005;

        /// <summary>Range resolution (m).</summary>
        public double Resolution { get; set; } = 0.0005;

        /// <summary>Elevation of each laser channel (degrees); one entry = a 2D scanner.</summary>
        public double[] ChannelElevationsDeg { get; set; } = { 0 };

        /// <summary>Angles grow clockwise seen from above (RPLIDAR convention).</summary>
        public bool Clockwise { get; set; } = true;

        public double SupplyVoltage { get; set; } = 5;
        public double SupplyCurrent { get; set; } = 0.3;

        public double AngularResolutionDeg(double scanRate) => 360.0 * scanRate / Math.Max(1, SampleRate);

        /// <summary>RPLIDAR A1-class triangulation scanner: 360°, 8000 samples/s, 5.5 Hz (1–10), 0.15–12 m, ≈1 % range error.</summary>
        public static LidarSpec A1 => new LidarSpec { Id = "A1", Name = "360° 2D LiDAR (A1 sınıfı)" };

        /// <summary>LD06-class time-of-flight scanner: 4500 samples/s, 10 Hz (5–13), 0.02–12 m, ±45 mm.</summary>
        public static LidarSpec Ld06 => new LidarSpec
        {
            Id = "LD06", Name = "360° ToF LiDAR (LD06 sınıfı)", SampleRate = 4500, ScanRate = 10, MinScanRate = 5, MaxScanRate = 13, MinRange = 0.02,
            MaxRange = 12, RangeNoiseFraction = 0.0, RangeNoiseFloor = 0.015, Resolution = 0.001, Clockwise = true, SupplyCurrent = 0.18
        };

        /// <summary>
        /// 16-channel spinning LiDAR (VLP-16 class): ±15° in 2° steps, 10 Hz (5–20), 100 m, ±3 cm. The real
        /// sensor has 0.1–0.4° azimuth steps (≈300 000 points/s); the default here is 1° (57 600 points/s) to keep
        /// the ray casting real-time — set <c>azimuth</c> smaller if the computer keeps up.
        /// </summary>
        public static LidarSpec Vlp16 => new LidarSpec
        {
            Id = "VLP16", Name = "16 kanallı 3D LiDAR (VLP-16 sınıfı)", SampleRate = 3600, ScanRate = 10, MinScanRate = 5, MaxScanRate = 20,
            MinRange = 0.4, MaxRange = 100, RangeNoiseFraction = 0, RangeNoiseFloor = 0.015, Resolution = 0.002,
            ChannelElevationsDeg = Enumerable.Range(0, 16).Select(i => -15.0 + 2 * i).ToArray(), Clockwise = true, SupplyVoltage = 12, SupplyCurrent = 0.67
        };

        public static LidarSpec For(string? model)
        {
            string m = (model ?? string.Empty).ToUpperInvariant();
            if (m.Contains("16") || m.Contains("3D")) return Vlp16;
            if (m.Contains("LD06") || m.Contains("TOF")) return Ld06;
            return A1;
        }
    }

    /// <summary>One completed revolution.</summary>
    public sealed class LidarScan
    {
        public double Time { get; set; }
        public double ScanRate { get; set; }

        /// <summary>Angle of each sample in the sensor's convention (degrees, clockwise from the front for 2D scanners).</summary>
        public float[] AnglesDeg { get; set; } = Array.Empty<float>();

        /// <summary>Elevation of each sample's channel (degrees).</summary>
        public float[] ElevationsDeg { get; set; } = Array.Empty<float>();

        /// <summary>Measured ranges (m); NaN = no return.</summary>
        public float[] Ranges { get; set; } = Array.Empty<float>();

        /// <summary>Return strength 0–255 (target reflectance and distance).</summary>
        public byte[] Intensities { get; set; } = Array.Empty<byte>();

        /// <summary>Hit points in world coordinates (NaN when no return) — for drawing.</summary>
        public Vec3[] Points { get; set; } = Array.Empty<Vec3>();

        /// <summary>Sensor origin when the scan finished.</summary>
        public Vec3 Origin { get; set; }

        public int Valid => Ranges.Count(r => !float.IsNaN(r));
    }

    /// <summary>
    /// A spinning range finder: every sample is a ray cast at the head's current angle and the
    /// body's current pose (so a moving robot sees the motion smeared through one scan, like
    /// the real sensor), with distance-dependent noise, reflectance-dependent maximum range and
    /// range quantisation. Completed revolutions are published as <see cref="LidarScan"/>.
    /// </summary>
    public sealed class LidarScanner
    {
        private readonly Random _rng;
        private double _pending;
        private int _index;
        private float[] _angles = Array.Empty<float>();
        private float[] _elev = Array.Empty<float>();
        private float[] _ranges = Array.Empty<float>();
        private byte[] _intensity = Array.Empty<byte>();
        private Vec3[] _points = Array.Empty<Vec3>();
        private double _scanRate;

        public LidarScanner(LidarSpec spec, double scanRate, double azimuthStepDeg = 0, int seed = 1)
        {
            Spec = spec;
            _rng = new Random(seed);
            ScanRate = scanRate;
            AzimuthStepDeg = azimuthStepDeg;
            Reallocate();
        }

        public LidarSpec Spec { get; }

        /// <summary>Head rotation (Hz), clamped to the sensor's range.</summary>
        public double ScanRate
        {
            get => _scanRate;
            set => _scanRate = Math.Max(Spec.MinScanRate, Math.Min(Spec.MaxScanRate, value <= 0 ? Spec.ScanRate : value));
        }

        /// <summary>For multi-channel sensors: azimuth step (degrees); 0 = from the sample rate.</summary>
        public double AzimuthStepDeg { get; set; }

        /// <summary>Samples per revolution per channel.</summary>
        public int SamplesPerRevolution { get; private set; }

        public LidarScan? Latest { get; private set; }

        /// <summary>Raised when a revolution completes.</summary>
        public event Action<LidarScan>? ScanCompleted;

        /// <summary>Spinning (powered and motor on).</summary>
        public bool Running { get; set; } = true;

        private void Reallocate()
        {
            double step = AzimuthStepDeg > 0 ? AzimuthStepDeg : Spec.AngularResolutionDeg(ScanRate);
            SamplesPerRevolution = Math.Max(8, (int)Math.Round(360.0 / step));
            int n = SamplesPerRevolution * Spec.ChannelElevationsDeg.Length;
            _angles = new float[n];
            _elev = new float[n];
            _ranges = new float[n];
            _intensity = new byte[n];
            _points = new Vec3[n];
            _index = 0;
        }

        /// <summary>
        /// Takes the samples due over <paramref name="dt"/>.
        /// <paramref name="cast"/>: world ray (origin, unit direction, max distance) → (distance or +∞, target reflectance 0–1).
        /// </summary>
        public void Advance(double time, double dt, Vec3 origin, Quat orientation, Func<Vec3, Vec3, double, (double Distance, double Reflectance)> cast)
        {
            if (!Running || dt <= 0) return;
            int channels = Spec.ChannelElevationsDeg.Length;
            if (_ranges.Length != SamplesPerRevolution * channels) Reallocate();
            double columnsPerSecond = SamplesPerRevolution * ScanRate;
            _pending += dt * columnsPerSecond;
            int columns = (int)_pending;
            _pending -= columns;
            columns = Math.Min(columns, SamplesPerRevolution);
            for (int c = 0; c < columns; c++)
            {
                double az = _index * 360.0 / SamplesPerRevolution;
                double math = (Spec.Clockwise ? -az : az) * Math.PI / 180;
                for (int ch = 0; ch < channels; ch++)
                {
                    double el = Spec.ChannelElevationsDeg[ch] * Math.PI / 180;
                    var local = new Vec3(Math.Cos(el) * Math.Cos(math), Math.Cos(el) * Math.Sin(math), Math.Sin(el));
                    var dir = orientation.Rotate(local);
                    var (d, refl) = cast(origin, dir, Spec.MaxRange * 1.05);
                    double reach = Spec.MaxRange * Math.Max(0.3, Math.Min(1.0, Math.Sqrt(Math.Max(0.01, refl) / 0.8)));
                    int k = _index * channels + ch;
                    _angles[k] = (float)az;
                    _elev[k] = (float)Spec.ChannelElevationsDeg[ch];
                    if (double.IsInfinity(d) || d > reach || d < Spec.MinRange)
                    {
                        _ranges[k] = float.NaN;
                        _intensity[k] = 0;
                        _points[k] = new Vec3(double.NaN, double.NaN, double.NaN);
                        continue;
                    }

                    double sigma = Spec.RangeNoiseFloor + Spec.RangeNoiseFraction * d;
                    double m = d + sigma * Gauss();
                    if (Spec.Resolution > 0) m = Math.Round(m / Spec.Resolution) * Spec.Resolution;
                    _ranges[k] = (float)Math.Max(Spec.MinRange, m);
                    _intensity[k] = (byte)Math.Max(1, Math.Min(255, 255 * refl * Math.Min(1, 2.0 / Math.Max(0.5, d))));
                    _points[k] = origin + dir * m;
                }

                _index++;
                if (_index >= SamplesPerRevolution)
                {
                    Latest = new LidarScan
                    {
                        Time = time, ScanRate = ScanRate, AnglesDeg = (float[])_angles.Clone(), ElevationsDeg = (float[])_elev.Clone(), Ranges = (float[])_ranges.Clone(),
                        Intensities = (byte[])_intensity.Clone(), Points = (Vec3[])_points.Clone(), Origin = origin
                    };
                    _index = 0;
                    ScanCompleted?.Invoke(Latest);
                }
            }
        }

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
