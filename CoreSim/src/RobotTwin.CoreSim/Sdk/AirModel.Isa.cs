using System;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>State of the standard atmosphere at one altitude.</summary>
    public readonly struct IsaPoint
    {
        public IsaPoint(double altitude, double temperatureK, double pressure, double density, double speedOfSound, double viscosity, double gravity)
        {
            AltitudeM = altitude;
            TemperatureK = temperatureK;
            PressurePa = pressure;
            Density = density;
            SpeedOfSound = speedOfSound;
            Viscosity = viscosity;
            Gravity = gravity;
        }

        /// <summary>Geometric altitude above mean sea level (m).</summary>
        public double AltitudeM { get; }

        public double TemperatureK { get; }
        public double TemperatureC => TemperatureK - 273.15;
        public double PressurePa { get; }

        /// <summary>Air density (kg/m³).</summary>
        public double Density { get; }

        /// <summary>a = √(γ·R·T) (m/s).</summary>
        public double SpeedOfSound { get; }

        /// <summary>Dynamic viscosity μ (Pa·s), Sutherland's law.</summary>
        public double Viscosity { get; }

        /// <summary>Gravitational acceleration at that altitude, g₀·(r₀/(r₀+z))² (m/s²).</summary>
        public double Gravity { get; }

        public double KinematicViscosity => Viscosity / Math.Max(1e-12, Density);
    }

    /// <summary>
    /// [aero] International Standard Atmosphere (ISO 2533:1975 = U.S. Standard Atmosphere 1976 up to
    /// 86 km): seven layers of constant lapse rate in geopotential altitude H = r₀·z/(r₀+z),
    /// T = T_b + L_b·(H − H_b), p = p_b·(T_b/T)^(g₀/(R·L_b)) (or p_b·exp(−g₀(H−H_b)/(R·T_b)) when L_b = 0),
    /// ρ = p/(R·T), a = √(γ·R·T). Constants: g₀ = 9.80665 m/s², R = 287.05287 J/(kg·K), γ = 1.4,
    /// r₀ = 6 356 766 m. A non-standard day is handled the usual way: the sea-level pressure (QNH)
    /// scales the pressure profile and a temperature offset ΔT (ISA+ΔT) changes T and ρ (the small
    /// effect of ΔT on the pressure profile is neglected).
    /// </summary>
    public static partial class Atmosphere
    {
        public const double IsaGravity = 9.80665;
        public const double IsaGasConstant = 287.05287;
        public const double IsaGamma = 1.4;
        public const double IsaSeaLevelTemperature = 288.15;
        public const double EarthRadius = 6356766.0;

        // Layer bases (geopotential m), base temperatures (K), lapse rates (K/m), base pressures (Pa).
        private static readonly double[] LayerH = { 0, 11000, 20000, 32000, 47000, 51000, 71000, 84852 };
        private static readonly double[] LayerL = { -0.0065, 0.0, 0.001, 0.0028, 0.0, -0.0028, -0.002 };
        private static readonly double[] LayerT = new double[8];
        private static readonly double[] LayerP = new double[8];

        static Atmosphere()
        {
            LayerT[0] = IsaSeaLevelTemperature;
            LayerP[0] = SeaLevelPressure;
            for (int i = 0; i < 7; i++)
            {
                double dh = LayerH[i + 1] - LayerH[i];
                LayerT[i + 1] = LayerT[i] + LayerL[i] * dh;
                LayerP[i + 1] = LayerPressure(i, LayerH[i + 1]);
            }
        }

        private static double LayerPressure(int i, double h)
        {
            double tb = LayerT[i], l = LayerL[i], dh = h - LayerH[i];
            if (l == 0) return LayerP[i] * Math.Exp(-IsaGravity * dh / (IsaGasConstant * tb));
            return LayerP[i] * Math.Pow(tb / (tb + l * dh), IsaGravity / (IsaGasConstant * l));
        }

        /// <summary>Geopotential altitude (m) of a geometric altitude (m).</summary>
        public static double Geopotential(double z) => EarthRadius * z / (EarthRadius + z);

        /// <summary>Geometric altitude (m) of a geopotential altitude (m).</summary>
        public static double Geometric(double h) => EarthRadius * h / (EarthRadius - h);

        /// <summary>
        /// ISA at a geometric altitude (m, −5 km … 86 km; clamped). <paramref name="seaLevelPa"/> is the
        /// sea-level pressure (QNH), <paramref name="deltaT"/> the ISA temperature offset (K).
        /// </summary>
        public static IsaPoint Isa(double altitudeM, double seaLevelPa = SeaLevelPressure, double deltaT = 0)
        {
            double z = Math.Max(-5000, Math.Min(86000, altitudeM));
            double h = Geopotential(z);
            int i = 0;
            while (i < 6 && h >= LayerH[i + 1]) i++;
            double tStd = LayerT[i] + LayerL[i] * (h - LayerH[i]);
            double p = LayerPressure(i, h) * (seaLevelPa / SeaLevelPressure);
            double t = Math.Max(1, tStd + deltaT);
            double rho = p / (IsaGasConstant * t);
            double a = Math.Sqrt(IsaGamma * IsaGasConstant * t);
            double mu = Viscosity(t - 273.15);
            double r = EarthRadius / (EarthRadius + z);
            return new IsaPoint(z, t, p, rho, a, mu, IsaGravity * r * r);
        }

        /// <summary>Pressure altitude (m) of a static pressure for a sea-level pressure: the inverse of <see cref="Isa"/> (what a barometric altimeter shows).</summary>
        public static double PressureAltitude(double pressurePa, double seaLevelPa = SeaLevelPressure)
        {
            double p = pressurePa * SeaLevelPressure / Math.Max(1, seaLevelPa);
            int i = 0;
            while (i < 6 && p <= LayerP[i + 1]) i++;
            double tb = LayerT[i], l = LayerL[i], pb = LayerP[i];
            double h = l == 0
                ? LayerH[i] - IsaGasConstant * tb / IsaGravity * Math.Log(p / pb)
                : LayerH[i] + tb / l * (Math.Pow(p / pb, -IsaGasConstant * l / IsaGravity) - 1);
            return Geometric(h);
        }

        /// <summary>Speed of sound (m/s) at a temperature (°C) for dry air.</summary>
        public static double SpeedOfSound(double tempC) => Math.Sqrt(IsaGamma * IsaGasConstant * Math.Max(1, tempC + 273.15));
    }
}
