using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] Physical surface layers painted on a terrain (asphalt, gravel, mud …).

    /// <summary>Friction pair of a surface layer against one class of body material.</summary>
    public readonly struct LayerFriction
    {
        public LayerFriction(double staticFriction, double kineticFriction, double rollingResistance)
        {
            Static = staticFriction;
            Kinetic = Math.Min(kineticFriction, staticFriction);
            Rolling = rollingResistance;
        }

        public double Static { get; }
        public double Kinetic { get; }

        /// <summary>Rolling resistance coefficient C_rr of a wheel of that material on the layer.</summary>
        public double Rolling { get; }
    }

    /// <summary>
    /// Terramechanics of a deformable soil (Bekker–Wong):
    /// <list type="bullet">
    /// <item>pressure–sinkage p = (k_c/b + k_φ)·zⁿ; a rigid wheel of diameter D, width b under load W sinks
    /// z₀ = [3W / ((3 − n)(k_c + b·k_φ)·√D)]^(2/(2n+1)); a flat plate under pressure p sinks (p/(k_c/b + k_φ))^(1/n);</item>
    /// <item>slip sinkage: z = z₀·(1 + k_s·|s|) — a spinning tyre digs itself in;</item>
    /// <item>compaction rolling resistance C_rr ≈ √(z/D);</item>
    /// <item>shear strength (Mohr–Coulomb) τ_max = c + σ·tan φ, so the largest thrust is H = A·c + W·tan φ over the
    /// contact area A = b·L (L ≈ √(D·z) chord of the sunken arc), developed with slip by Janosi–Hanamoto:
    /// H(s) = H_max·[1 − K/(s·L)·(1 − e^(−s·L/K))] with the shear deformation modulus K.</item>
    /// </list>
    /// Dry sand uses Wong's values (n = 1.1, k_c = 0.99 kN/m^2.1, k_φ = 1528 kN/m^3.1, c = 1.04 kPa, φ = 28°,
    /// K ≈ 1 cm); gravel, mud and turf are calibrated for robot-sized wheels (Bekker underestimates the
    /// sinkage of small wheels), so they are plausible rather than measured.
    /// </summary>
    public readonly struct SoilModel
    {
        public SoilModel(double n, double kc, double kphi, double maxSinkFraction, double cohesion, double frictionAngleDeg, double shearModulus, double slipSinkage)
        {
            N = n;
            Kc = kc;
            Kphi = kphi;
            MaxSinkFraction = maxSinkFraction;
            Cohesion = cohesion;
            FrictionAngle = frictionAngleDeg;
            ShearModulus = shearModulus;
            SlipSinkage = slipSinkage;
        }

        public double N { get; }

        /// <summary>Cohesive modulus k_c (N/m^(n+1)).</summary>
        public double Kc { get; }

        /// <summary>Frictional modulus k_φ (N/m^(n+2)).</summary>
        public double Kphi { get; }

        /// <summary>Sinkage never exceeds this fraction of the wheel radius.</summary>
        public double MaxSinkFraction { get; }

        /// <summary>Cohesion c (Pa).</summary>
        public double Cohesion { get; }

        /// <summary>Internal friction angle φ (degrees).</summary>
        public double FrictionAngle { get; }

        /// <summary>Shear deformation modulus K (m).</summary>
        public double ShearModulus { get; }

        /// <summary>Slip-sinkage factor k_s (z = z₀·(1 + k_s·|s|)).</summary>
        public double SlipSinkage { get; }

        /// <summary>Static sinkage (m) of a rigid wheel (radius, width in m) under a load (N).</summary>
        public double Sinkage(double load, double radius, double width)
        {
            if (load <= 0 || radius <= 0) return 0;
            double b = Math.Max(1e-3, width), d = 2 * radius;
            double k = (Kc + b * Kphi) * Math.Max(0.05, 3 - N) * Math.Sqrt(d);
            if (k <= 0) return 0;
            double z = Math.Pow(3 * load / k, 2 / (2 * N + 1));
            return Math.Min(z, MaxSinkFraction * radius);
        }

        /// <summary>Wheel sinkage including slip sinkage, capped like <see cref="Sinkage"/>.</summary>
        public double Sinkage(double load, double radius, double width, double slip) =>
            Math.Min(Sinkage(load, radius, width) * (1 + SlipSinkage * Math.Min(1, Math.Abs(slip))), MaxSinkFraction * radius);

        /// <summary>Sinkage (m) of a flat plate of smaller side <paramref name="width"/> under <paramref name="pressure"/> (Pa).</summary>
        public double PlateSinkage(double pressure, double width, double cap = 0.05)
        {
            if (pressure <= 0) return 0;
            double k = Kc / Math.Max(1e-3, width) + Kphi;
            return Math.Min(cap, Math.Pow(pressure / k, 1 / N));
        }

        /// <summary>Contact length of a wheel sunk by z (chord of the immersed arc, at least a small tyre patch).</summary>
        public static double ContactLength(double radius, double sink) => Math.Max(0.15 * radius, Math.Sqrt(2 * radius * Math.Max(0, sink)));

        /// <summary>Largest thrust coefficient H_max/W = (A·c + W·tan φ)/W.</summary>
        public double PeakTraction(double load, double area) =>
            load <= 1e-9 ? Math.Tan(FrictionAngle * Math.PI / 180) : (area * Cohesion + load * Math.Tan(FrictionAngle * Math.PI / 180)) / load;

        /// <summary>
        /// Janosi–Hanamoto: share of the peak thrust developed at slip s over contact length L
        /// (floored at <paramref name="floor"/> — a parked wheel still holds by soil creep before failure).
        /// </summary>
        public double ShearDevelopment(double slip, double contactLength, double floor = 0.3)
        {
            double j = Math.Abs(slip) * Math.Max(1e-4, contactLength) / Math.Max(1e-5, ShearModulus);
            double f = j < 1e-6 ? 0.5 * j : 1 - (1 - Math.Exp(-j)) / j;
            return Math.Max(floor, Math.Min(1, f));
        }

        /// <summary>Compaction (bulldozing) rolling resistance of that sinkage: C_rr ≈ √(z/D).</summary>
        public static double RollingFromSinkage(double sink, double radius) => sink <= 0 || radius <= 0 ? 0 : Math.Sqrt(sink / (2 * radius));
    }

    /// <summary>
    /// A paintable surface: friction against rubber / hard plastic / steel, rolling resistance,
    /// restitution and contact stiffness, and — for loose ground — a soil model so tyres sink in.
    /// </summary>
    public sealed class TerrainLayer
    {
        public TerrainLayer(string id, string name, string material, uint colorArgb, LayerFriction rubber, LayerFriction plastic, LayerFriction steel,
            double restitution, double stiffness, SoilModel? soil = null, string? note = null)
        {
            Id = id;
            Name = name;
            Material = material;
            ColorArgb = colorArgb;
            Rubber = rubber;
            Plastic = plastic;
            Steel = steel;
            Restitution = restitution;
            Stiffness = stiffness;
            Soil = soil;
            Note = note ?? string.Empty;
        }

        /// <summary>Stable id stored in files ("asphalt", "gravel" …).</summary>
        public string Id { get; }

        /// <summary>Turkish display name.</summary>
        public string Name { get; }

        /// <summary>Closest entry of the <see cref="Materials"/> table (for sensors and labels).</summary>
        public string Material { get; }

        /// <summary>Average colour (#AARRGGBB) — swatches and far-away rendering.</summary>
        public uint ColorArgb { get; }

        public LayerFriction Rubber { get; }
        public LayerFriction Plastic { get; }
        public LayerFriction Steel { get; }
        public double Restitution { get; }
        public double Stiffness { get; }

        /// <summary>Deformable ground (sand, gravel, mud, grass); null = rigid.</summary>
        public SoilModel? Soil { get; }

        public bool IsLoose => Soil != null;
        public string Note { get; }

        /// <summary>Index in <see cref="TerrainLayers.All"/>.</summary>
        public int Index { get; internal set; }

        public LayerFriction For(BodyMaterialClass c) => c switch
        {
            BodyMaterialClass.Rubber => Rubber,
            BodyMaterialClass.Metal => Steel,
            _ => Plastic
        };

        public override string ToString() => $"{Name} μ(kauçuk)={Rubber.Static:0.##}/{Rubber.Kinetic:0.##} Crr={Rubber.Rolling:0.###}";
    }

    /// <summary>How a body material behaves against a terrain layer.</summary>
    public enum BodyMaterialClass
    {
        /// <summary>Hard plastics, wood, composites, glass.</summary>
        Plastic,

        /// <summary>Rubber tyres, foam.</summary>
        Rubber,

        /// <summary>Steel, aluminium, brass (casters, skids).</summary>
        Metal
    }

    /// <summary>The catalogue of terrain surface layers.</summary>
    public static class TerrainLayers
    {
        private static readonly Dictionary<string, TerrainLayer> ById = new Dictionary<string, TerrainLayer>(StringComparer.OrdinalIgnoreCase);
        private static readonly BodyMaterialClass[] ClassByMaterial;

        static TerrainLayers()
        {
            static LayerFriction F(double s, double k, double crr) => new LayerFriction(s, k, crr);
            // Friction: rubber (tyre) / hard plastic (PLA, ABS, wood) / steel. Dry values from the
            // usual engineering tables; loose ground: the soil's shear strength limits traction.
            //                id           name                    material    colour        rubber μs μk Crr       plastic                 steel                   e     k(N/m)  soil
            Add(new TerrainLayer("asphalt", "Asfalt", "asphalt", 0xFF3C3D40, F(0.90, 0.75, 0.012), F(0.55, 0.45, 0.010), F(0.60, 0.50, 0.004), 0.40, 1e6));
            Add(new TerrainLayer("concrete", "Beton", "concrete", 0xFF9C9A94, F(1.00, 0.80, 0.010), F(0.55, 0.45, 0.010), F(0.55, 0.45, 0.003), 0.40, 1e6));
            Add(new TerrainLayer("tile", "Fayans / düz zemin", "tile", 0xFFD8D4CC, F(0.70, 0.55, 0.008), F(0.40, 0.30, 0.006), F(0.40, 0.30, 0.002), 0.50, 1e6));
            Add(new TerrainLayer("grass", "Çim", "grass", 0xFF4E7A32, F(0.55, 0.45, 0.060), F(0.40, 0.32, 0.080), F(0.35, 0.30, 0.090), 0.15, 1e4,
                new SoilModel(0.7, 2.0e4, 2.5e6, 0.12, 1.0e3, 30, 0.005, 0.3), "Kökler zemini tutar: az batma, yüksek yuvarlanma direnci."));
            Add(new TerrainLayer("dirt", "Sıkışmış toprak", "regolith", 0xFF7A5A3C, F(0.75, 0.60, 0.025), F(0.50, 0.40, 0.030), F(0.50, 0.40, 0.020), 0.20, 1e5,
                null, "Sert, kuru toprak yol: batmaz, asfalttan biraz kaygan."));
            Add(new TerrainLayer("gravel", "Gevşek çakıl", "regolith", 0xFF8E867A, F(0.60, 0.42, 0.050), F(0.50, 0.40, 0.060), F(0.45, 0.38, 0.050), 0.10, 3e4,
                new SoilModel(0.9, 4.0e3, 1.2e6, 0.25, 0.3e3, 38, 0.015, 0.6), "Taneler kayar: kalkışta patinaj, frende uzun duruş."));
            Add(new TerrainLayer("sand", "Kum", "sand", 0xFFD2BC8C, F(0.60, 0.45, 0.100), F(0.50, 0.40, 0.150), F(0.45, 0.40, 0.150), 0.05, 1e4,
                new SoilModel(1.1, 9.9e2, 1.53e6, 0.35, 1.04e3, 28, 0.010, 0.9), "Kuru kum (Bekker n=1.1, k_c≈1 kN/m^2.1): teker gömülür."));
            Add(new TerrainLayer("mud", "Çamur", "sand", 0xFF4A3828, F(0.35, 0.22, 0.150), F(0.30, 0.20, 0.180), F(0.25, 0.18, 0.180), 0.02, 5e3,
                new SoilModel(0.6, 1.0e3, 1.5e5, 0.40, 0.8e3, 8, 0.008, 1.2), "Islak kil: düşük tutunma, derin batma."));
            Add(new TerrainLayer("wet", "Islak zemin", "asphalt", 0xFF2C3036, F(0.55, 0.45, 0.012), F(0.30, 0.22, 0.010), F(0.30, 0.25, 0.004), 0.35, 1e6,
                null, "Islak asfalt / beton: kuru hâlin yaklaşık yarısı tutunma."));
            Add(new TerrainLayer("ice", "Buz", "ice", 0xFFCFE3EE, F(0.15, 0.08, 0.010), F(0.08, 0.05, 0.010), F(0.03, 0.01, 0.001), 0.20, 5e5));
            Add(new TerrainLayer("carpet", "Halı", "carpet", 0xFF5A6478, F(0.90, 0.70, 0.040), F(0.50, 0.40, 0.050), F(0.40, 0.32, 0.060), 0.10, 2e4));
            Add(new TerrainLayer("rubbermat", "Kauçuk paspas", "rubber", 0xFF26262A, F(1.15, 1.00, 0.020), F(0.70, 0.60, 0.015), F(0.60, 0.50, 0.010), 0.60, 3e4));

            var names = new List<string>(Materials.Names);
            int count = 0;
            foreach (var n in names) count = Math.Max(count, Materials.IndexOf(n) + 1);
            ClassByMaterial = new BodyMaterialClass[count];
            foreach (var n in names)
            {
                ClassByMaterial[Materials.IndexOf(n)] = n switch
                {
                    "rubber" or "foam" => BodyMaterialClass.Rubber,
                    "steel" or "aluminium" or "brass" => BodyMaterialClass.Metal,
                    _ => BodyMaterialClass.Plastic
                };
            }
        }

        private static readonly List<TerrainLayer> List = new List<TerrainLayer>();

        private static void Add(TerrainLayer l)
        {
            l.Index = List.Count;
            List.Add(l);
            ById[l.Id] = l;
        }

        public static IReadOnlyList<TerrainLayer> All => List;
        public static int Count => List.Count;

        public static TerrainLayer? Find(string? id) => id != null && ById.TryGetValue(id, out var l) ? l : null;

        public static int IndexOf(string? id) => Find(id)?.Index ?? 0;

        public static TerrainLayer Get(int index) => List[Math.Max(0, Math.Min(List.Count - 1, index))];

        public static BodyMaterialClass ClassOf(int materialIndex) =>
            materialIndex >= 0 && materialIndex < ClassByMaterial.Length ? ClassByMaterial[materialIndex] : BodyMaterialClass.Plastic;

        public static BodyMaterialClass ClassOf(string material) => ClassOf(Materials.IndexOf(material));
    }
}
