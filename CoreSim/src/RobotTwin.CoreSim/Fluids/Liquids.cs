using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Fluids
{
    /// <summary>A liquid: density, dynamic viscosity and display colour.</summary>
    public sealed class LiquidSpec
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;

        /// <summary>kg/m³ at 20 °C.</summary>
        public double Density { get; init; }

        /// <summary>Pa·s at 20 °C.</summary>
        public double Viscosity { get; init; }

        public string Color { get; init; } = "#3A7BD5";

        /// <summary>Surface tension (N/m), informative.</summary>
        public double SurfaceTension { get; init; } = 0.072;

        public double KinematicViscosity => Viscosity / Density;
    }

    /// <summary>
    /// [fluid] Liquid table, typical values at 20 °C and 1 atm (CRC Handbook of Chemistry and Physics,
    /// 97th ed.; oils and honey vary with grade and moisture, the values are representative).
    /// </summary>
    public static class Liquids
    {
        public static IReadOnlyList<LiquidSpec> All { get; } = new[]
        {
            new LiquidSpec { Id = "water", Name = "Tatlı su", Density = 998.2, Viscosity = 1.002e-3, Color = "#3A86C8", SurfaceTension = 0.0728 },
            new LiquidSpec { Id = "seawater", Name = "Deniz suyu (35 ‰)", Density = 1024.8, Viscosity = 1.08e-3, Color = "#2C6E8F", SurfaceTension = 0.0735 },
            new LiquidSpec { Id = "mercury", Name = "Cıva", Density = 13546, Viscosity = 1.55e-3, Color = "#B8BCC2", SurfaceTension = 0.487 },
            new LiquidSpec { Id = "glycerin", Name = "Gliserin (saf)", Density = 1261, Viscosity = 1.41, Color = "#E8E4C8", SurfaceTension = 0.063 },
            new LiquidSpec { Id = "olive-oil", Name = "Zeytinyağı", Density = 911, Viscosity = 0.084, Color = "#B5A642", SurfaceTension = 0.032 },
            new LiquidSpec { Id = "motor-oil", Name = "Motor yağı (SAE 30)", Density = 875, Viscosity = 0.29, Color = "#7A5A1E", SurfaceTension = 0.031 },
            new LiquidSpec { Id = "ethanol", Name = "Etanol", Density = 789, Viscosity = 1.2e-3, Color = "#D8E8F0", SurfaceTension = 0.0223 },
            new LiquidSpec { Id = "gasoline", Name = "Benzin", Density = 740, Viscosity = 0.6e-3, Color = "#E8D890", SurfaceTension = 0.021 },
            new LiquidSpec { Id = "diesel", Name = "Dizel", Density = 832, Viscosity = 2.5e-3, Color = "#C8B060", SurfaceTension = 0.028 },
            new LiquidSpec { Id = "honey", Name = "Bal", Density = 1420, Viscosity = 10, Color = "#D89A1E", SurfaceTension = 0.05 }
        };

        public static LiquidSpec Water => All[0];

        public static LiquidSpec Find(string? id) =>
            All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Name, id, StringComparison.OrdinalIgnoreCase)) ?? Water;
    }

    /// <summary>
    /// [fluid] A body of liquid: an axis-aligned basin (pool, tank, sea patch) with a free surface,
    /// optional linear (Airy) waves and a uniform current. Waves: η = A·cos(k·x − ωt) with the full
    /// dispersion relation ω² = g·k·tanh(k·d); water particles move with the deep-water orbital velocity
    /// A·ω·e^(k·z) under the crest. Hydrostatic pressure p = p_surface + ρ·g·depth.
    /// </summary>
    public sealed class WaterBody
    {
        public string Id { get; set; } = "water";
        public LiquidSpec Liquid { get; set; } = Liquids.Water;

        /// <summary>Basin bounds (world, m). The surface is at <see cref="SurfaceZ"/> (≤ Max.Z).</summary>
        public Vec3 Min { get; set; } = new Vec3(-1, -1, 0);

        public Vec3 Max { get; set; } = new Vec3(1, 1, 0.5);

        public double SurfaceZ { get; set; } = 0.4;

        public double WaveAmplitude { get; set; }
        public double WaveLength { get; set; } = 2.0;

        /// <summary>Direction the waves travel (deg from +X).</summary>
        public double WaveDirectionDeg { get; set; }

        public Vec3 Current { get; set; }

        /// <summary>Pressure on the free surface (Pa): the atmosphere at that height.</summary>
        public double SurfacePressure { get; set; } = 101325;

        public double Gravity { get; set; } = 9.81;

        public double Depth => SurfaceZ - Min.Z;

        public double WaveNumber => 2 * Math.PI / Math.Max(1e-3, WaveLength);

        public double WaveFrequency
        {
            get
            {
                double k = WaveNumber;
                return Math.Sqrt(Gravity * k * Math.Tanh(k * Math.Max(1e-3, Depth)));
            }
        }

        public bool ContainsXY(double x, double y) => x >= Min.X && x <= Max.X && y >= Min.Y && y <= Max.Y;

        /// <summary>Free-surface height at a point and time.</summary>
        public double SurfaceAt(double x, double y, double t)
        {
            if (WaveAmplitude <= 0) return SurfaceZ;
            double a = WaveDirectionDeg * Math.PI / 180;
            double ph = WaveNumber * (x * Math.Cos(a) + y * Math.Sin(a)) - WaveFrequency * t;
            return SurfaceZ + WaveAmplitude * Math.Cos(ph);
        }

        /// <summary>Surface normal (up) at a point and time.</summary>
        public Vec3 NormalAt(double x, double y, double t)
        {
            if (WaveAmplitude <= 0) return Vec3.UnitZ;
            double a = WaveDirectionDeg * Math.PI / 180;
            double k = WaveNumber;
            double ph = k * (x * Math.Cos(a) + y * Math.Sin(a)) - WaveFrequency * t;
            double g = -WaveAmplitude * k * Math.Sin(ph);
            return new Vec3(-g * Math.Cos(a), -g * Math.Sin(a), 1).Normalized();
        }

        /// <summary>Velocity of the water at a point (current + wave orbital motion).</summary>
        public Vec3 VelocityAt(Vec3 p, double t)
        {
            if (WaveAmplitude <= 0) return Current;
            double a = WaveDirectionDeg * Math.PI / 180;
            double k = WaveNumber, w = WaveFrequency;
            double ph = k * (p.X * Math.Cos(a) + p.Y * Math.Sin(a)) - w * t;
            double decay = Math.Exp(k * Math.Min(0, p.Z - SurfaceZ));
            double u = WaveAmplitude * w * decay;
            return Current + new Vec3(u * Math.Cos(ph) * Math.Cos(a), u * Math.Cos(ph) * Math.Sin(a), u * Math.Sin(ph));
        }

        /// <summary>Absolute pressure at a point (Pa): the surface pressure, plus ρ·g·depth below the surface.</summary>
        public double PressureAt(Vec3 p, double t)
        {
            double depth = SurfaceAt(p.X, p.Y, t) - p.Z;
            return SurfacePressure + (depth > 0 && ContainsXY(p.X, p.Y) && p.Z >= Min.Z ? Liquid.Density * Gravity * depth : 0);
        }
    }
}
