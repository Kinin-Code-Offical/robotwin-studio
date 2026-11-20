using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>Datasheet noise figures of a MEMS IMU.</summary>
    public sealed class ImuSpec
    {
        public string Name { get; set; } = "MPU-6050";

        /// <summary>Accelerometer noise density (µg/√Hz) and gyroscope rate noise density (°/s/√Hz).</summary>
        public double AccelNoiseDensityUg { get; set; } = 400;

        public double GyroNoiseDensityDps { get; set; } = 0.005;

        /// <summary>Turn-on bias spread (1σ): accelerometer (mg) and gyroscope (°/s).</summary>
        public double AccelBiasMg { get; set; } = 40;

        public double GyroBiasDps { get; set; } = 3;

        /// <summary>Bias random walk: accelerometer (mg/√s) and gyroscope (°/s/√s).</summary>
        public double AccelBiasWalk { get; set; } = 0.2;

        public double GyroBiasWalk { get; set; } = 0.01;

        /// <summary>Measurement bandwidth (digital low-pass, Hz).</summary>
        public double Bandwidth { get; set; } = 260;

        /// <summary>InvenSense MPU-6050: 400 µg/√Hz, 0.005 °/s/√Hz, ±50 mg / ±20 °/s offsets (typ. much smaller after reset).</summary>
        public static ImuSpec Mpu6050 => new ImuSpec();
    }

    /// <summary>
    /// IMU truth-to-measurement model: specific force and angular rate of the body from the physics,
    /// plus a turn-on bias, a slow bias random walk and white noise of the datasheet density.
    /// </summary>
    public sealed class ImuModel
    {
        private readonly Random _rng;
        private Vec3 _accelBias, _gyroBias;
        private Vec3 _lastVelocity;
        private bool _started;
        private Vec3 _accelFiltered;

        public ImuModel(ImuSpec spec, int seed)
        {
            Spec = spec;
            _rng = new Random(seed);
            _accelBias = RandomVec(spec.AccelBiasMg * 1e-3 * 9.80665);
            _gyroBias = RandomVec(spec.GyroBiasDps * Math.PI / 180);
        }

        public ImuSpec Spec { get; }

        /// <summary>Last true specific force and rate in the body frame (m/s², rad/s), before errors.</summary>
        public Vec3 TrueAccel { get; private set; }

        public Vec3 TrueGyro { get; private set; }

        /// <summary>Last measured values (bias + noise included).</summary>
        public Vec3 Accel { get; private set; }

        public Vec3 Gyro { get; private set; }

        public Vec3 AccelBias => _accelBias;
        public Vec3 GyroBias => _gyroBias;

        /// <summary>White-noise σ per sample for the bandwidth (m/s², rad/s).</summary>
        public double AccelSigma => Spec.AccelNoiseDensityUg * 1e-6 * 9.80665 * Math.Sqrt(Spec.Bandwidth * 1.57);

        public double GyroSigma => Spec.GyroNoiseDensityDps * Math.PI / 180 * Math.Sqrt(Spec.Bandwidth * 1.57);

        /// <param name="velocity">World velocity of the body (m/s).</param>
        /// <param name="angularVelocity">World angular velocity (rad/s).</param>
        /// <param name="gravity">Gravitational acceleration (m/s², positive).</param>
        public void Update(double dt, Quat orientation, Vec3 velocity, Vec3 angularVelocity, double gravity, bool addNoise = true)
        {
            if (dt <= 0) return;
            var a = _started ? (velocity - _lastVelocity) / dt : Vec3.Zero;
            _started = true;
            _lastVelocity = velocity;
            // Contacts make single-step accelerations spiky; the chip's DLPF smooths them.
            double alpha = 1 - Math.Exp(-2 * Math.PI * Spec.Bandwidth * dt);
            _accelFiltered = _accelFiltered + (a - _accelFiltered) * alpha;
            var specific = _accelFiltered + new Vec3(0, 0, gravity);
            TrueAccel = orientation.InverseRotate(specific);
            TrueGyro = orientation.InverseRotate(angularVelocity);

            double sq = Math.Sqrt(dt);
            _accelBias += RandomVec(Spec.AccelBiasWalk * 1e-3 * 9.80665 * sq);
            _gyroBias += RandomVec(Spec.GyroBiasWalk * Math.PI / 180 * sq);
            Accel = TrueAccel + _accelBias + (addNoise ? RandomVec(AccelSigma) : Vec3.Zero);
            Gyro = TrueGyro + _gyroBias + (addNoise ? RandomVec(GyroSigma) : Vec3.Zero);
        }

        private Vec3 RandomVec(double sigma) => new Vec3(Gauss() * sigma, Gauss() * sigma, Gauss() * sigma);

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }

    /// <summary>Datasheet description of a GNSS receiver module.</summary>
    public sealed class GpsSpec
    {
        public string Name { get; set; } = "NEO-6M";

        /// <summary>Horizontal position accuracy, circular error probable (m), open sky.</summary>
        public double Cep { get; set; } = 2.5;

        public double MaxRateHz { get; set; } = 5;
        public double ColdStartS { get; set; } = 27;
        public double WarmStartS { get; set; } = 27;
        public double HotStartS { get; set; } = 1;

        /// <summary>Velocity accuracy (m/s, 1σ).</summary>
        public double VelocityAccuracy { get; set; } = 0.1;

        public int Baud { get; set; } = 9600;
        public double SupplyCurrent { get; set; } = 0.045;

        /// <summary>u-blox NEO-6M: 2.5 m CEP, ≤5 Hz, cold/warm start 27 s, hot start 1 s, 0.1 m/s.</summary>
        public static GpsSpec Neo6M => new GpsSpec();

        /// <summary>u-blox NEO-M8N: 2.5 m CEP (GPS+GLONASS ≈ 2.0 m), ≤10 Hz single-GNSS, cold start 26 s, hot 1 s, 0.05 m/s.</summary>
        public static GpsSpec NeoM8N => new GpsSpec { Name = "NEO-M8N", Cep = 2.0, MaxRateHz = 10, ColdStartS = 26, WarmStartS = 25, HotStartS = 1, VelocityAccuracy = 0.05, SupplyCurrent = 0.05 };

        public static GpsSpec For(string? model) => (model ?? string.Empty).ToUpperInvariant().Contains("M8") ? NeoM8N : Neo6M;
    }

    /// <summary>A position fix (WGS-84) as the receiver reports it.</summary>
    public sealed class GpsFix
    {
        public double Time { get; set; }
        public bool Valid { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double Altitude { get; set; }
        public double SpeedMps { get; set; }
        public double CourseDeg { get; set; }
        public int Satellites { get; set; }
        public double Hdop { get; set; }

        /// <summary>Error of this fix in the world frame (m) — the simulator knows the truth.</summary>
        public double ErrorEast { get; set; }

        public double ErrorNorth { get; set; }

        /// <summary>NMEA sentences of this epoch (RMC, VTG, GGA, GSA, GLL), CR/LF terminated.</summary>
        public string Nmea { get; set; } = string.Empty;
    }

    /// <summary>
    /// GNSS receiver: time to first fix after power-up, a slowly wandering (Gauss–Markov, τ ≈ 60 s)
    /// position error sized to the CEP and the sky view, plus small epoch noise; NMEA output at
    /// the update rate. World +X = east, +Y = north, origin at the environment's latitude/longitude.
    /// </summary>
    public sealed class GpsReceiver
    {
        private const double A = 6378137.0, E2 = 6.69437999014e-3;
        private readonly Random _rng;
        private double _errE, _errN, _errU;
        private double _nextEpoch;
        private double _powerOn = double.NaN;

        public GpsReceiver(GpsSpec spec, SensingEnvironment env, int seed, string start = "sıcak")
        {
            Spec = spec;
            Environment = env;
            _rng = new Random(seed);
            Start = start;
            RateHz = 1;
        }

        public GpsSpec Spec { get; }
        public SensingEnvironment Environment { get; }

        /// <summary>"soğuk", "ılık", "sıcak" (cold / warm / hot start).</summary>
        public string Start { get; set; }

        public double RateHz { get; set; }
        public GpsFix? Latest { get; private set; }

        public double TimeToFirstFix => Environment.Sky == "indoor" ? double.PositiveInfinity
            : (Start.StartsWith("so", StringComparison.OrdinalIgnoreCase) ? Spec.ColdStartS : Start.StartsWith("ı", StringComparison.OrdinalIgnoreCase) || Start.StartsWith("i", StringComparison.OrdinalIgnoreCase) ? Spec.WarmStartS : Spec.HotStartS)
              * (Environment.Sky == "urban" ? 1.5 : 1);

        /// <summary>CEP for the sky view (m).</summary>
        public double EffectiveCep => Spec.Cep * (Environment.Sky == "urban" ? 2.5 : Environment.Sky == "indoor" ? 8 : 1);

        /// <summary>Converts a world point to latitude/longitude/altitude.</summary>
        public static (double Lat, double Lon, double Alt) ToGeodetic(SensingEnvironment env, double east, double north, double up)
        {
            double lat0 = env.OriginLatitude * Math.PI / 180;
            double s = Math.Sin(lat0);
            double rn = A / Math.Sqrt(1 - E2 * s * s);
            double rm = rn * (1 - E2) / (1 - E2 * s * s);
            return (env.OriginLatitude + north / rm * 180 / Math.PI, env.OriginLongitude + east / (rn * Math.Cos(lat0)) * 180 / Math.PI, env.OriginAltitude + up);
        }

        /// <summary>Advances to <paramref name="time"/> with the antenna's true position and velocity; returns a new fix at each epoch.</summary>
        public GpsFix? Update(double time, Vec3 position, Vec3 velocity, bool powered)
        {
            if (!powered)
            {
                _powerOn = double.NaN;
                return null;
            }

            if (double.IsNaN(_powerOn))
            {
                _powerOn = time;
                _nextEpoch = time;
                double s0 = EffectiveCep / 1.1774;
                _errE = Gauss() * s0;
                _errN = Gauss() * s0;
                _errU = Gauss() * s0 * 1.8;
            }

            if (time + 1e-9 < _nextEpoch) return null;
            double rate = Math.Max(0.1, Math.Min(Spec.MaxRateHz, RateHz));
            double dt = 1.0 / rate;
            _nextEpoch += dt;
            if (_nextEpoch < time) _nextEpoch = time + dt;

            // Gauss–Markov wander: σ = CEP / 1.1774 per horizontal axis.
            double sigma = EffectiveCep / 1.1774, tau = 60.0;
            double phi = Math.Exp(-dt / tau), q = sigma * Math.Sqrt(1 - phi * phi);
            _errE = _errE * phi + q * Gauss();
            _errN = _errN * phi + q * Gauss();
            _errU = _errU * phi + 1.8 * q * Gauss();

            bool valid = time - _powerOn >= TimeToFirstFix;
            int sats = Environment.Sky switch { "indoor" => _rng.Next(0, 4), "urban" => _rng.Next(5, 9), _ => _rng.Next(8, 13) };
            if (!valid) sats = Math.Min(sats, (int)((time - _powerOn) / Math.Max(1, TimeToFirstFix) * 4));
            double hdop = Math.Round((sats > 3 ? 4.5 / Math.Sqrt(sats) : 99) * (Environment.Sky == "urban" ? 1.6 : 1), 2);
            double ve = velocity.X + Gauss() * Spec.VelocityAccuracy, vn = velocity.Y + Gauss() * Spec.VelocityAccuracy;
            double ee = _errE + Gauss() * 0.1 * sigma, en = _errN + Gauss() * 0.1 * sigma;
            var (lat, lon, alt) = ToGeodetic(Environment, position.X + ee, position.Y + en, position.Z + _errU);
            var fix = new GpsFix
            {
                Time = time, Valid = valid, Latitude = lat, Longitude = lon, Altitude = alt, Satellites = sats, Hdop = hdop,
                SpeedMps = Math.Sqrt(ve * ve + vn * vn), CourseDeg = (Math.Atan2(ve, vn) * 180 / Math.PI + 360) % 360,
                ErrorEast = ee, ErrorNorth = en
            };
            fix.Nmea = Nmea(fix);
            Latest = fix;
            return fix;
        }

        /// <summary>UTC of a simulated instant (the simulation starts at 2026-06-01 09:00:00 UTC).</summary>
        public static DateTime Utc(double time) => new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc).AddSeconds(time);

        public static string Nmea(GpsFix f)
        {
            var c = CultureInfo.InvariantCulture;
            var t = Utc(f.Time);
            string hms = t.ToString("HHmmss.ff", c);
            string date = t.ToString("ddMMyy", c);
            string lat = Deg(f.Latitude, 2), ns = f.Latitude >= 0 ? "N" : "S";
            string lon = Deg(f.Longitude, 3), ew = f.Longitude >= 0 ? "E" : "W";
            double knots = f.SpeedMps * 1.943844;
            var sb = new StringBuilder();
            if (f.Valid)
            {
                sb.Append(Sentence($"GPRMC,{hms},A,{lat},{ns},{lon},{ew},{knots.ToString("0.000", c)},{f.CourseDeg.ToString("0.00", c)},{date},,,A"));
                sb.Append(Sentence($"GPVTG,{f.CourseDeg.ToString("0.00", c)},T,,M,{knots.ToString("0.000", c)},N,{(f.SpeedMps * 3.6).ToString("0.000", c)},K,A"));
                sb.Append(Sentence($"GPGGA,{hms},{lat},{ns},{lon},{ew},1,{f.Satellites:00},{f.Hdop.ToString("0.00", c)},{f.Altitude.ToString("0.0", c)},M,36.9,M,,"));
                sb.Append(Sentence($"GPGSA,A,3,04,05,09,12,17,24,25,,,,,,{(f.Hdop * 1.6).ToString("0.00", c)},{f.Hdop.ToString("0.00", c)},{(f.Hdop * 1.3).ToString("0.00", c)}"));
                sb.Append(Sentence($"GPGLL,{lat},{ns},{lon},{ew},{hms},A,A"));
            }
            else
            {
                sb.Append(Sentence($"GPRMC,{hms},V,,,,,,,{date},,,N"));
                sb.Append(Sentence("GPVTG,,,,,,,,,N"));
                sb.Append(Sentence($"GPGGA,{hms},,,,,0,{f.Satellites:00},99.99,,,,,,"));
                sb.Append(Sentence("GPGSA,A,1,,,,,,,,,,,,,99.99,99.99,99.99"));
                sb.Append(Sentence($"GPGLL,,,,,{hms},V,N"));
            }

            return sb.ToString();
        }

        /// <summary>"$BODY*CS\r\n" with the XOR checksum.</summary>
        public static string Sentence(string body)
        {
            byte cs = 0;
            foreach (char ch in body) cs ^= (byte)ch;
            return "$" + body + "*" + cs.ToString("X2", CultureInfo.InvariantCulture) + "\r\n";
        }

        private static string Deg(double v, int degDigits)
        {
            v = Math.Abs(v);
            int d = (int)v;
            double m = (v - d) * 60;
            return d.ToString(new string('0', degDigits), CultureInfo.InvariantCulture) + m.ToString("00.00000", CultureInfo.InvariantCulture);
        }

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }

    /// <summary>
    /// Honeywell HMC5883L 3-axis magnetometer (GY-271), I²C 0x1E: configuration A/B, mode, data
    /// X/Z/Y (big-endian, two's complement), status and the "H43" identification registers;
    /// gain table 1370…230 LSB/Ga, overflow reads −4096. The field is the Earth's field turned
    /// into the module's frame plus a hard-iron offset and ≈2 mGa noise.
    /// </summary>
    public sealed class Hmc5883l : II2cDevice
    {
        private static readonly int[] LsbPerGauss = { 1370, 1090, 820, 660, 440, 390, 330, 230 };
        private readonly byte[] _regs = new byte[13];
        private readonly Random _rng = new Random(5883);
        private int _pointer;
        private bool _first;

        public Hmc5883l(byte address = 0x1E)
        {
            Address = address;
            Reset();
        }

        public byte Address { get; }

        /// <summary>Field at the sensor in its own axes (Gauss).</summary>
        public double FieldX { get; set; }

        public double FieldY { get; set; }
        public double FieldZ { get; set; }

        /// <summary>Noise σ (Gauss).</summary>
        public double Noise { get; set; } = 0.002;

        public bool Powered { get; set; } = true;

        public void Reset()
        {
            Array.Clear(_regs, 0, _regs.Length);
            _regs[0] = 0x10;
            _regs[1] = 0x20;
            _regs[2] = 0x01;
            _regs[10] = (byte)'H';
            _regs[11] = (byte)'4';
            _regs[12] = (byte)'3';
            _pointer = 0;
        }

        /// <summary>Configuration register A (output rate, averaging).</summary>
        public byte ConfigA => _regs[0];

        /// <summary>Gain setting in LSB per Gauss.</summary>
        public int Gain => LsbPerGauss[(_regs[1] >> 5) & 7];

        /// <summary>Takes a measurement into the data registers (continuous mode, or after a single-measurement request).</summary>
        public void Measure()
        {
            int mode = _regs[2] & 3;
            if (mode >= 2) return;
            Store(3, FieldX);
            Store(5, FieldZ);
            Store(7, FieldY);
            _regs[9] |= 1;
            if (mode == 1) _regs[2] = (byte)((_regs[2] & ~3) | 3);   // single → idle
        }

        private void Store(int reg, double gauss)
        {
            double v = (gauss + Noise * Gauss()) * Gain;
            int raw = v > 2047 || v < -2048 ? -4096 : (int)Math.Round(v);
            _regs[reg] = (byte)((raw >> 8) & 0xFF);
            _regs[reg + 1] = (byte)(raw & 0xFF);
        }

        public bool Start(bool read)
        {
            if (!Powered) return false;
            _first = !read;
            return true;
        }

        public bool Write(byte value)
        {
            if (_first)
            {
                _pointer = value % 13;
                _first = false;
                return true;
            }

            if (_pointer <= 2) _regs[_pointer] = value;
            if (_pointer == 2 && (value & 3) <= 1) Measure();
            _pointer = (_pointer + 1) % 13;
            return true;
        }

        public byte Read(bool ack)
        {
            byte v = _regs[_pointer];
            if (_pointer == 8) _regs[9] &= 0xFE;   // data read → not ready
            _pointer++;
            if (_pointer == 9 && (_regs[2] & 3) == 0) _pointer = 3;
            if (_pointer >= 13) _pointer = 0;
            return v;
        }

        public void Stop()
        {
        }

        private double Gauss()
        {
            double u1 = 1.0 - _rng.NextDouble(), u2 = _rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
