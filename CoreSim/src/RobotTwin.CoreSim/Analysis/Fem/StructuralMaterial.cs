using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analysis.Fem
{
    /// <summary>
    /// [analysis] Bulk properties for the offline solvers: elasticity (E, ν), strength (yield or
    /// tensile strength for brittle/printed parts), density and thermal data (k, c_p, ε).
    /// Typical handbook values; printed plastics are for 100 % infill along the layers.
    /// </summary>
    public sealed class StructuralMaterial
    {
        public string Name { get; set; } = "PLA";

        /// <summary>Young's modulus (Pa).</summary>
        public double E { get; set; } = 3.5e9;

        /// <summary>Poisson's ratio.</summary>
        public double Nu { get; set; } = 0.36;

        /// <summary>Yield (or tensile) strength (Pa) used for the safety factor.</summary>
        public double Yield { get; set; } = 50e6;

        public double Density { get; set; } = 1240;

        /// <summary>Thermal conductivity (W/m·K).</summary>
        public double Conductivity { get; set; } = 0.13;

        /// <summary>Specific heat (J/kg·K).</summary>
        public double SpecificHeat { get; set; } = 1800;

        /// <summary>Surface emissivity (0–1).</summary>
        public double Emissivity { get; set; } = 0.9;

        public double ShearModulus => E / (2 * (1 + Nu));
        public double Lame => E * Nu / ((1 + Nu) * (1 - 2 * Nu));
        public double Diffusivity => Conductivity / (Density * SpecificHeat);

        public StructuralMaterial Clone() => (StructuralMaterial)MemberwiseClone();

        private static readonly Dictionary<string, StructuralMaterial> Table = new Dictionary<string, StructuralMaterial>(StringComparer.OrdinalIgnoreCase)
        {
            ["PLA"] = new StructuralMaterial { Name = "PLA", E = 3.5e9, Nu = 0.36, Yield = 50e6, Density = 1240, Conductivity = 0.13, SpecificHeat = 1800, Emissivity = 0.92 },
            ["ABS"] = new StructuralMaterial { Name = "ABS", E = 2.2e9, Nu = 0.35, Yield = 40e6, Density = 1050, Conductivity = 0.17, SpecificHeat = 1400, Emissivity = 0.92 },
            ["PETG"] = new StructuralMaterial { Name = "PETG", E = 2.1e9, Nu = 0.38, Yield = 50e6, Density = 1270, Conductivity = 0.2, SpecificHeat = 1200, Emissivity = 0.9 },
            ["ALÜMİNYUM"] = new StructuralMaterial { Name = "Alüminyum 6061-T6", E = 69e9, Nu = 0.33, Yield = 276e6, Density = 2700, Conductivity = 167, SpecificHeat = 896, Emissivity = 0.1 },
            ["ÇELİK"] = new StructuralMaterial { Name = "Yapı çeliği S235", E = 200e9, Nu = 0.29, Yield = 235e6, Density = 7850, Conductivity = 50, SpecificHeat = 490, Emissivity = 0.3 },
            ["AHŞAP"] = new StructuralMaterial { Name = "Ahşap (çam, lif yönü)", E = 9e9, Nu = 0.3, Yield = 40e6, Density = 500, Conductivity = 0.12, SpecificHeat = 1700, Emissivity = 0.9 },
            ["MDF"] = new StructuralMaterial { Name = "MDF", E = 3.5e9, Nu = 0.25, Yield = 18e6, Density = 750, Conductivity = 0.1, SpecificHeat = 1700, Emissivity = 0.9 },
            ["KONTRPLAK"] = new StructuralMaterial { Name = "Kontrplak", E = 8e9, Nu = 0.3, Yield = 35e6, Density = 600, Conductivity = 0.13, SpecificHeat = 1700, Emissivity = 0.9 },
            ["FR4"] = new StructuralMaterial { Name = "FR4", E = 22e9, Nu = 0.14, Yield = 300e6, Density = 1850, Conductivity = 0.3, SpecificHeat = 1100, Emissivity = 0.9 },
            ["KAUÇUK"] = new StructuralMaterial { Name = "Kauçuk", E = 5e6, Nu = 0.48, Yield = 15e6, Density = 1100, Conductivity = 0.16, SpecificHeat = 2000, Emissivity = 0.92 },
            ["BAKIR"] = new StructuralMaterial { Name = "Bakır", E = 117e9, Nu = 0.34, Yield = 70e6, Density = 8960, Conductivity = 400, SpecificHeat = 385, Emissivity = 0.05 }
        };

        /// <summary>Material for a world object material name (Turkish or ASCII spelling); PLA when unknown.</summary>
        public static StructuralMaterial For(string? name)
        {
            string key = (name ?? "PLA").Trim().ToUpperInvariant() switch
            {
                "ALUMINYUM" or "ALÜMINYUM" or "ALUMINIUM" or "ALUMINUM" => "ALÜMİNYUM",
                "CELIK" or "ÇELIK" or "STEEL" => "ÇELİK",
                "AHSAP" or "WOOD" => "AHŞAP",
                "KAUCUK" or "RUBBER" => "KAUÇUK",
                "COPPER" => "BAKIR",
                var k => k
            };
            return Table.TryGetValue(key, out var m) ? m.Clone() : Table["PLA"].Clone();
        }

        public static IEnumerable<string> Names => Table.Keys;
    }
}
