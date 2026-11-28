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

        /// <summary>[fatigue] Ultimate tensile strength (Pa); 0: 1.1 × the yield strength.</summary>
        public double Ultimate
        {
            get => _ultimate > 0 ? _ultimate : 1.1 * Yield;
            set => _ultimate = value;
        }

        private double _ultimate;

        /// <summary>[fatigue] Fully reversed fatigue strength at 10⁷ cycles (Pa); 0: 0.3 × the ultimate strength.</summary>
        public double FatigueStrength
        {
            get => _fatigue > 0 ? _fatigue : 0.3 * Ultimate;
            set => _fatigue = value;
        }

        private double _fatigue;

        /// <summary>[fatigue] Below its fatigue strength the material lasts for ever (steels); otherwise the S-N line goes on.</summary>
        public bool EnduranceLimit { get; set; }

        public double Density { get; set; } = 1240;

        /// <summary>Thermal conductivity (W/m·K).</summary>
        public double Conductivity { get; set; } = 0.13;

        /// <summary>Specific heat (J/kg·K).</summary>
        public double SpecificHeat { get; set; } = 1800;

        /// <summary>Surface emissivity (0–1).</summary>
        public double Emissivity { get; set; } = 0.9;

        /// <summary>Linear thermal expansion coefficient (1/K).</summary>
        public double Alpha { get; set; } = 68e-6;

        /// <summary>Glass transition (°C): polymers lose most of their stiffness and strength above it. NaN for metals.</summary>
        public double GlassTransition { get; set; } = double.NaN;

        /// <summary>Melting / decomposition temperature (°C): the part flows or chars and no longer holds.</summary>
        public double MeltingPoint { get; set; } = double.NaN;

        /// <summary>Stiffness left above the glass transition (rubbery plateau / glassy modulus).</summary>
        public double RubberyRatio { get; set; } = 0.03;

        /// <summary>Gradual softening below T_g (fraction per kelvin above 25 °C).</summary>
        public double SofteningPerK { get; set; } = 0.004;

        /// <summary>
        /// E(T)/E(25 °C): slow linear softening, then the glass transition as a smooth step (width ±4 K)
        /// down to the rubbery plateau. Metals only soften slowly in the range a robot sees.
        /// </summary>
        public double ModulusFactor(double tempC)
        {
            double f = Math.Max(0.2, 1 - SofteningPerK * Math.Max(0, tempC - 25));
            if (double.IsNaN(GlassTransition)) return f;
            double step = 1.0 / (1.0 + Math.Exp((tempC - GlassTransition) / 4.0));
            double g = RubberyRatio + (1 - RubberyRatio) * step;
            if (!double.IsNaN(MeltingPoint) && tempC >= MeltingPoint) g = 0;
            return f * g;
        }

        /// <summary>Yield(T)/Yield(25 °C): follows the stiffness for polymers (glassy → rubbery → melt).</summary>
        public double YieldFactor(double tempC) => ModulusFactor(tempC);

        /// <summary>Plain-language state of the material at a temperature.</summary>
        public string StateAt(double tempC)
        {
            if (!double.IsNaN(MeltingPoint) && tempC >= MeltingPoint) return "eriyor";
            if (!double.IsNaN(GlassTransition) && tempC >= GlassTransition) return "yumuşadı";
            if (!double.IsNaN(GlassTransition) && tempC >= GlassTransition - 10) return "yumuşamaya yaklaşıyor";
            return "katı";
        }

        /// <summary>A copy with E and yield at a temperature.</summary>
        public StructuralMaterial AtTemperature(double tempC)
        {
            var m = Clone();
            m.E = E * ModulusFactor(tempC);
            m.Yield = Yield * YieldFactor(tempC);
            return m;
        }

        public double ShearModulus => E / (2 * (1 + Nu));
        public double Lame => E * Nu / ((1 + Nu) * (1 - 2 * Nu));
        public double Diffusivity => Conductivity / (Density * SpecificHeat);

        public StructuralMaterial Clone() => (StructuralMaterial)MemberwiseClone();

        private static readonly Dictionary<string, StructuralMaterial> Table = new Dictionary<string, StructuralMaterial>(StringComparer.OrdinalIgnoreCase)
        {
            ["PLA"] = new StructuralMaterial { Name = "PLA", E = 3.5e9, Nu = 0.36, Yield = 50e6, Density = 1240, Conductivity = 0.13, SpecificHeat = 1800, Emissivity = 0.92, Alpha = 68e-6, GlassTransition = 60, MeltingPoint = 160, RubberyRatio = 0.02, Ultimate = 55e6, FatigueStrength = 17e6 },
            ["ABS"] = new StructuralMaterial { Name = "ABS", E = 2.2e9, Nu = 0.35, Yield = 40e6, Density = 1050, Conductivity = 0.17, SpecificHeat = 1400, Emissivity = 0.92, Alpha = 90e-6, GlassTransition = 105, MeltingPoint = 230, RubberyRatio = 0.01, Ultimate = 42e6, FatigueStrength = 12e6 },
            ["PETG"] = new StructuralMaterial { Name = "PETG", E = 2.1e9, Nu = 0.38, Yield = 50e6, Density = 1270, Conductivity = 0.2, SpecificHeat = 1200, Emissivity = 0.9, Alpha = 68e-6, GlassTransition = 80, MeltingPoint = 230, RubberyRatio = 0.01, Ultimate = 53e6, FatigueStrength = 15e6 },
            ["ALÜMİNYUM"] = new StructuralMaterial { Name = "Alüminyum 6061-T6", E = 69e9, Nu = 0.33, Yield = 276e6, Density = 2700, Conductivity = 167, SpecificHeat = 896, Emissivity = 0.1, Alpha = 23e-6, SofteningPerK = 0.0004, MeltingPoint = 582, Ultimate = 310e6, FatigueStrength = 97e6 },
            ["ÇELİK"] = new StructuralMaterial { Name = "Yapı çeliği S235", E = 200e9, Nu = 0.29, Yield = 235e6, Density = 7850, Conductivity = 50, SpecificHeat = 490, Emissivity = 0.3, Alpha = 12e-6, SofteningPerK = 0.0003, MeltingPoint = 1450, Ultimate = 360e6, FatigueStrength = 180e6, EnduranceLimit = true },
            ["AHŞAP"] = new StructuralMaterial { Name = "Ahşap (çam, lif yönü)", E = 9e9, Nu = 0.3, Yield = 40e6, Density = 500, Conductivity = 0.12, SpecificHeat = 1700, Emissivity = 0.9, Alpha = 5e-6, SofteningPerK = 0.002, MeltingPoint = 270, Ultimate = 80e6, FatigueStrength = 24e6 },
            ["MDF"] = new StructuralMaterial { Name = "MDF", E = 3.5e9, Nu = 0.25, Yield = 18e6, Density = 750, Conductivity = 0.1, SpecificHeat = 1700, Emissivity = 0.9, Alpha = 10e-6, SofteningPerK = 0.002, MeltingPoint = 250, Ultimate = 20e6, FatigueStrength = 6e6 },
            ["KONTRPLAK"] = new StructuralMaterial { Name = "Kontrplak", E = 8e9, Nu = 0.3, Yield = 35e6, Density = 600, Conductivity = 0.13, SpecificHeat = 1700, Emissivity = 0.9, Alpha = 6e-6, SofteningPerK = 0.002, MeltingPoint = 270, Ultimate = 40e6, FatigueStrength = 12e6 },
            ["FR4"] = new StructuralMaterial { Name = "FR4", E = 22e9, Nu = 0.14, Yield = 300e6, Density = 1850, Conductivity = 0.3, SpecificHeat = 1100, Emissivity = 0.9, Alpha = 14e-6, GlassTransition = 135, MeltingPoint = 300, RubberyRatio = 0.3, Ultimate = 320e6, FatigueStrength = 96e6 },
            ["KAUÇUK"] = new StructuralMaterial { Name = "Kauçuk", E = 5e6, Nu = 0.48, Yield = 15e6, Density = 1100, Conductivity = 0.16, SpecificHeat = 2000, Emissivity = 0.92, Alpha = 200e-6, SofteningPerK = 0.003, MeltingPoint = 200, Ultimate = 20e6, FatigueStrength = 6e6 },
            ["BALSA"] = new StructuralMaterial { Name = "Balsa (lif yönü)", E = 3.4e9, Nu = 0.3, Yield = 12e6, Density = 160, Conductivity = 0.05, SpecificHeat = 2900, Emissivity = 0.9, Alpha = 4e-6, SofteningPerK = 0.002, MeltingPoint = 270, Ultimate = 14e6, FatigueStrength = 4e6 },
            ["KARBON"] = new StructuralMaterial { Name = "Karbon fiber (pultrüzyon)", E = 110e9, Nu = 0.3, Yield = 1200e6, Density = 1600, Conductivity = 5, SpecificHeat = 800, Emissivity = 0.9, Alpha = 1e-6, GlassTransition = 120, MeltingPoint = 300, RubberyRatio = 0.6, Ultimate = 1300e6, FatigueStrength = 780e6 },
            ["KURŞUN"] = new StructuralMaterial { Name = "Kurşun", E = 16e9, Nu = 0.44, Yield = 12e6, Density = 11340, Conductivity = 35, SpecificHeat = 130, Emissivity = 0.08, Alpha = 29e-6, SofteningPerK = 0.002, MeltingPoint = 327, Ultimate = 17e6, FatigueStrength = 7e6 },
            // [gas] bottle PET is biaxially stretched (much stronger than moulded PET); kraft paper tube for airframes.
            ["PET"] = new StructuralMaterial { Name = "PET şişe (çift yönlü gerilmiş)", E = 4e9, Nu = 0.38, Yield = 160e6, Density = 1380, Conductivity = 0.15, SpecificHeat = 1100, Emissivity = 0.9, Alpha = 60e-6, GlassTransition = 75, MeltingPoint = 250, RubberyRatio = 0.02, Ultimate = 180e6, FatigueStrength = 54e6 },
            ["KARTON"] = new StructuralMaterial { Name = "Karton boru (kraft)", E = 3e9, Nu = 0.3, Yield = 25e6, Density = 700, Conductivity = 0.07, SpecificHeat = 1400, Emissivity = 0.9, Alpha = 10e-6, SofteningPerK = 0.002, MeltingPoint = 230, Ultimate = 30e6, FatigueStrength = 9e6 },
            ["AKRİLİK"] = new StructuralMaterial { Name = "Akrilik (PMMA)", E = 3.2e9, Nu = 0.37, Yield = 70e6, Density = 1180, Conductivity = 0.19, SpecificHeat = 1470, Emissivity = 0.9, Alpha = 70e-6, GlassTransition = 105, MeltingPoint = 160, RubberyRatio = 0.01, Ultimate = 72e6, FatigueStrength = 18e6 },
            ["CAM"] = new StructuralMaterial { Name = "Cam (soda-kireç)", E = 70e9, Nu = 0.22, Yield = 40e6, Density = 2500, Conductivity = 1.0, SpecificHeat = 840, Emissivity = 0.92, Alpha = 9e-6, GlassTransition = 560, MeltingPoint = 1000, RubberyRatio = 0.01, Ultimate = 45e6, FatigueStrength = 14e6 },
            ["BAKIR"] = new StructuralMaterial { Name = "Bakır", E = 117e9, Nu = 0.34, Yield = 70e6, Density = 8960, Conductivity = 400, SpecificHeat = 385, Emissivity = 0.05, Alpha = 17e-6, SofteningPerK = 0.0004, MeltingPoint = 1085, Ultimate = 220e6, FatigueStrength = 76e6 },
            // [drive] acetal gears, brass nuts and pinions, die-cast (Zamak) bearing housings.
            ["POM"] = new StructuralMaterial { Name = "Asetal (POM)", E = 2.8e9, Nu = 0.35, Yield = 65e6, Density = 1410, Conductivity = 0.31, SpecificHeat = 1470, Emissivity = 0.9, Alpha = 110e-6, SofteningPerK = 0.004, MeltingPoint = 175, Ultimate = 70e6, FatigueStrength = 32e6 },
            ["PİRİNÇ"] = new StructuralMaterial { Name = "Pirinç (CuZn39Pb3)", E = 97e9, Nu = 0.34, Yield = 250e6, Density = 8470, Conductivity = 120, SpecificHeat = 380, Emissivity = 0.1, Alpha = 20e-6, SofteningPerK = 0.0004, MeltingPoint = 890, Ultimate = 430e6, FatigueStrength = 150e6 },
            ["ÇİNKO"] = new StructuralMaterial { Name = "Çinko döküm (Zamak 3)", E = 85e9, Nu = 0.27, Yield = 210e6, Density = 6600, Conductivity = 113, SpecificHeat = 419, Emissivity = 0.1, Alpha = 27e-6, SofteningPerK = 0.001, MeltingPoint = 385, Ultimate = 280e6, FatigueStrength = 48e6 }
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
                "ACETAL" or "ASETAL" or "DELRIN" => "POM",
                "PIRINÇ" or "PIRINC" or "BRASS" => "PİRİNÇ",
                "ÇINKO" or "CINKO" or "ZINC" or "ZAMAK" => "ÇİNKO",
                "CARBON" => "KARBON",
                "KURSUN" or "LEAD" => "KURŞUN",
                var k => k
            };
            return Table.TryGetValue(key, out var m) ? m.Clone() : Table["PLA"].Clone();
        }

        public static IEnumerable<string> Names => Table.Keys;
    }
}
