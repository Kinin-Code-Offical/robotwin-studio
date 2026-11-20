using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>Air properties from temperature, pressure, altitude and humidity.</summary>
    public static partial class Atmosphere   // [aero] ISA layers, speed of sound, hydrostatics in Sdk/AirModel.Isa.cs
    {
        public const double DryGasConstant = 287.058;
        public const double VaporGasConstant = 461.495;
        public const double SeaLevelPressure = 101325.0;

        /// <summary>Static pressure at an altitude for a sea-level pressure (troposphere barometric formula, h &lt; 11 km).</summary>
        public static double PressureAt(double seaLevelPa, double altitudeM) =>
            seaLevelPa * Math.Pow(Math.Max(0.0, 1 - 2.25577e-5 * Math.Min(11000, altitudeM)), 5.25588);

        /// <summary>Saturation vapour pressure over water (Pa), Tetens formula.</summary>
        public static double SaturationVaporPressure(double tempC) => 610.78 * Math.Pow(10, 7.5 * tempC / (tempC + 237.3));

        /// <summary>Density of (humid) air: ρ = p_d/(R_d·T) + p_v/(R_v·T) (kg/m³).</summary>
        public static double Density(double tempC, double pressurePa, double relativeHumidity = 0)
        {
            double t = Math.Max(1.0, tempC + 273.15);
            double pv = Math.Max(0, Math.Min(1, relativeHumidity)) * SaturationVaporPressure(tempC);
            double pd = Math.Max(0, pressurePa - pv);
            return pd / (DryGasConstant * t) + pv / (VaporGasConstant * t);
        }

        /// <summary>Dynamic viscosity of air (Pa·s), Sutherland's law.</summary>
        public static double Viscosity(double tempC)
        {
            double t = tempC + 273.15;
            return 1.716e-5 * Math.Pow(t / 273.15, 1.5) * (273.15 + 110.4) / (t + 110.4);
        }

        /// <summary>Density for the world's settings (override, or computed at the floor's altitude).</summary>
        public static double DensityFor(AtmosphereSettings a, double ambientC) =>
            a.DensityOverride is double d ? Math.Max(0, d) : Density(ambientC, PressureAt(a.PressureHPa * 100, a.AltitudeM), a.Humidity);
    }

    /// <summary>
    /// Run-time wind: mean wind with a height profile, seeded continuous turbulence and
    /// discrete gusts. Deterministic: advanced only by <see cref="Advance"/> inside the fixed
    /// physics step, and every random number comes from <see cref="SdkRandom"/> with the world seed.
    /// </summary>
    /// <remarks>
    /// Turbulence ("dryden") is a first-order Gauss–Markov process per axis,
    /// u[k+1] = a·u[k] + σ·√(1−a²)·n, a = exp(−V·Δt/L). Its spectrum is the Dryden longitudinal
    /// form σ²·2L/(πV)/(1+(LΩ)²); the true Dryden lateral/vertical spectra are second order,
    /// approximated here with the same first-order filter. Turbulence is spatially uniform
    /// (one sample for the whole world, "frozen" in space), which is adequate for objects much
    /// smaller than L.
    /// </remarks>
    public sealed class WindModel
    {
        private readonly WindSettings _s;
        private readonly Vec3 _mean;
        private readonly double _legacyGust;
        private readonly SdkRandom _rx, _ry, _rz, _rg;
        private Vec3 _turb;
        private double _gustStart = double.NaN, _gustAmp, _nextGust;
        private Vec3 _gustDir;

        /// <param name="settings">The world's wind settings.</param>
        /// <param name="legacyMean">Older WorldSpec WindX/Y/Z (added to the mean).</param>
        /// <param name="legacyGust">Older WorldSpec Gust (sum-of-sines amplitude) used when no turbulence model is chosen.</param>
        public WindModel(WindSettings settings, Vec3 legacyMean, double legacyGust, long seed)
        {
            _s = settings;
            _mean = settings.MeanVector + legacyMean;
            _legacyGust = Math.Max(0, legacyGust);
            _rx = SdkRandom.Derive(seed, "wind.u");
            _ry = SdkRandom.Derive(seed, "wind.v");
            _rz = SdkRandom.Derive(seed, "wind.w");
            _rg = SdkRandom.Derive(seed, "wind.gust");
            if (IsDryden)
            {
                // Start from the stationary distribution so statistics hold from t = 0.
                _turb = new Vec3(_rx.NextGaussian() * SigmaH, _ry.NextGaussian() * SigmaH, _rz.NextGaussian() * SigmaV);
            }

            if (_s.Gusts) _nextGust = _rg.NextExponential(Math.Max(0.1, _s.GustInterval));
            var h = new Vec3(_mean.X, _mean.Y, 0);
            _gustDir = h.Length > 1e-9 ? h.Normalized() : Vec3.UnitX;
        }

        public double Time { get; private set; }

        public bool IsDryden => string.Equals(_s.Turbulence, "dryden", StringComparison.OrdinalIgnoreCase) && _s.Intensity > 0;

        private bool IsSine => string.Equals(_s.Turbulence, "sine", StringComparison.OrdinalIgnoreCase) || (!IsDryden && _legacyGust > 0);

        private double SigmaH => Math.Max(0, _s.Intensity);
        private double SigmaV => Math.Max(0, _s.Intensity * _s.VerticalRatio);

        /// <summary>Mean wind at the reference height (m/s).</summary>
        public Vec3 Mean => _mean;

        /// <summary>Current turbulence sample (m/s).</summary>
        public Vec3 Turbulence => _turb;

        /// <summary>Current discrete gust (m/s).</summary>
        public Vec3 Gust { get; private set; }

        /// <summary>Correlation time of the turbulence (s).</summary>
        public double CorrelationTime => Math.Max(0.05, _s.LengthScale) / Math.Max(1.0, new Vec3(_mean.X, _mean.Y, 0).Length);

        public void Advance(double dt)
        {
            if (dt <= 0) return;
            Time += dt;
            if (IsDryden)
            {
                double a = Math.Exp(-dt / CorrelationTime);
                double b = Math.Sqrt(Math.Max(0, 1 - a * a));
                _turb = new Vec3(
                    a * _turb.X + b * SigmaH * _rx.NextGaussian(),
                    a * _turb.Y + b * SigmaH * _ry.NextGaussian(),
                    a * _turb.Z + b * SigmaV * _rz.NextGaussian());
            }
            else if (IsSine)
            {
                double k = _s.Turbulence == "sine" ? Math.Max(_legacyGust, _s.Intensity) : _legacyGust, t = Time;
                double gx = Math.Sin(1.3 * t) * 0.6 + Math.Sin(3.7 * t + 1.1) * 0.35 + Math.Sin(7.9 * t + 2.3) * 0.2;
                double gy = Math.Sin(1.1 * t + 0.5) * 0.6 + Math.Sin(4.3 * t + 2.0) * 0.35 + Math.Sin(8.7 * t + 0.3) * 0.2;
                double gz = Math.Sin(2.1 * t + 1.7) * 0.3 + Math.Sin(5.9 * t + 0.9) * 0.15;
                _turb = new Vec3(gx, gy, gz) * (k / 0.75);
            }
            else _turb = Vec3.Zero;

            if (_s.Gusts)
            {
                double dur = Math.Max(0.05, _s.GustDuration);
                if (double.IsNaN(_gustStart) && Time >= _nextGust)
                {
                    _gustStart = Time;
                    _gustAmp = _s.GustAmplitude * (0.5 + 0.5 * _rg.NextDouble());
                }

                if (!double.IsNaN(_gustStart))
                {
                    double u = (Time - _gustStart) / dur;
                    if (u >= 1)
                    {
                        _gustStart = double.NaN;
                        Gust = Vec3.Zero;
                        _nextGust = Time + _rg.NextExponential(Math.Max(0.1, _s.GustInterval));
                    }
                    else Gust = _gustDir * (_gustAmp * 0.5 * (1 - Math.Cos(2 * Math.PI * u)));
                }
            }
        }

        /// <summary>Profile factor of the mean wind at a height above the floor.</summary>
        public double ProfileFactor(double z)
        {
            double zr = Math.Max(0.01, _s.ReferenceHeight);
            switch ((_s.Profile ?? "uniform").ToLowerInvariant())
            {
                case "power":
                    return Math.Pow(Math.Max(0.005, z) / zr, Math.Max(0, Math.Min(1, _s.PowerExponent)));
                case "log":
                {
                    double z0 = Math.Max(1e-5, _s.Roughness);
                    if (zr <= z0) return 1;
                    return Math.Max(0, Math.Log(Math.Max(z, z0) / z0) / Math.Log(zr / z0));
                }

                default:
                    return 1;
            }
        }

        /// <summary>Wind at a point (m/s): profiled mean + turbulence + gust (height = z above the floor).</summary>
        public Vec3 At(Vec3 p, double groundZ = 0)
        {
            double f = ProfileFactor(p.Z - groundZ);
            return new Vec3(_mean.X * f, _mean.Y * f, _mean.Z) + _turb + Gust;
        }

        public bool IsCalm => _mean.LengthSquared == 0 && !IsDryden && !IsSine && !_s.Gusts;
    }
}
