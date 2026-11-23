using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>[liquid] A liquid's physical properties at room temperature (20 °C).</summary>
    public sealed class LiquidKind
    {
        public LiquidKind(string id, string name, double density, double viscosity, double surfaceTension, uint color, double opacity)
        {
            Id = id;
            Name = name;
            Density = density;
            Viscosity = viscosity;
            SurfaceTension = surfaceTension;
            Color = color;
            Opacity = opacity;
        }

        public string Id { get; }

        /// <summary>Name shown in the app (Turkish).</summary>
        public string Name { get; }

        /// <summary>Density ρ (kg/m³).</summary>
        public double Density { get; }

        /// <summary>Dynamic viscosity μ (Pa·s).</summary>
        public double Viscosity { get; }

        /// <summary>Surface tension against air σ (N/m).</summary>
        public double SurfaceTension { get; }

        /// <summary>Kinematic viscosity ν = μ/ρ (m²/s).</summary>
        public double KinematicViscosity => Viscosity / Density;

        /// <summary>Body colour (0xRRGGBB) of a thick layer, for drawing.</summary>
        public uint Color { get; }

        /// <summary>How much light a 1 cm layer stops (0 = clear, 1 = opaque), for drawing.</summary>
        public double Opacity { get; }

        // Handbook values (CRC Handbook of Chemistry and Physics; Engineering Toolbox) at 20 °C.
        public static readonly LiquidKind Water = new LiquidKind("water", "Su", 998.2, 1.002e-3, 0.0728, 0x3F8FC9, 0.04);
        public static readonly LiquidKind SeaWater = new LiquidKind("seawater", "Deniz suyu", 1024.8, 1.08e-3, 0.0735, 0x2E8A9E, 0.06);
        public static readonly LiquidKind Ethanol = new LiquidKind("ethanol", "Etil alkol", 789.3, 1.20e-3, 0.0223, 0xE8F2F7, 0.02);
        public static readonly LiquidKind Milk = new LiquidKind("milk", "Süt", 1030, 2.0e-3, 0.045, 0xF4F1E8, 0.97);
        public static readonly LiquidKind Oil = new LiquidKind("oil", "Ayçiçek yağı", 919, 0.0558, 0.0335, 0xE3B53A, 0.35);
        public static readonly LiquidKind Glycerol = new LiquidKind("glycerol", "Gliserin", 1261, 1.41, 0.0634, 0xF2F2E6, 0.05);
        public static readonly LiquidKind Honey = new LiquidKind("honey", "Bal", 1420, 10, 0.050, 0xC98A1A, 0.6);

        public static IReadOnlyList<LiquidKind> All { get; } = new[] { Water, SeaWater, Ethanol, Milk, Oil, Glycerol, Honey };

        public static LiquidKind Find(string? id)
        {
            foreach (var k in All)
                if (string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(k.Name, id, StringComparison.OrdinalIgnoreCase))
                    return k;
            return Water;
        }
    }
}
