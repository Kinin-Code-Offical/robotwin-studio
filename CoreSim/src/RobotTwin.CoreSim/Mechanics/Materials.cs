using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Contact behaviour of a pair of surfaces: Coulomb friction (static and kinetic),
    /// coefficient of restitution, rolling resistance coefficient (C_rr = F_roll / N) and
    /// the effective contact stiffness used to estimate impact forces and durations.
    /// </summary>
    public readonly struct ContactProperties
    {
        public ContactProperties(double staticFriction, double kineticFriction, double restitution, double rollingResistance, double stiffness)
        {
            StaticFriction = Math.Max(0, staticFriction);
            KineticFriction = Math.Max(0, Math.Min(kineticFriction, StaticFriction));
            Restitution = Math.Max(0, Math.Min(1, restitution));
            RollingResistance = Math.Max(0, rollingResistance);
            Stiffness = Math.Max(1, stiffness);
        }

        public double StaticFriction { get; }
        public double KineticFriction { get; }
        public double Restitution { get; }
        public double RollingResistance { get; }

        /// <summary>Effective normal contact stiffness (N/m) of the pair (series combination).</summary>
        public double Stiffness { get; }

        /// <summary>Duration of a short elastic impact of mass <paramref name="mass"/> (half period of m on k), seconds.</summary>
        public double ImpactDuration(double mass) => Math.PI * Math.Sqrt(Math.Max(mass, 1e-9) / Stiffness);

        public ContactProperties WithFriction(double mu) => new ContactProperties(mu, mu, Restitution, RollingResistance, Stiffness);

        public override string ToString() => $"μs={StaticFriction:G3} μk={KineticFriction:G3} e={Restitution:G2} Crr={RollingResistance:G3}";
    }

    /// <summary>A surface/bulk material: generic contact data, stiffness and strength.</summary>
    public sealed class SurfaceMaterial
    {
        public SurfaceMaterial(string name, double staticFriction, double kineticFriction, double restitution, double rollingResistance,
            double stiffness, double yieldStrength, double density)
        {
            Name = name;
            StaticFriction = staticFriction;
            KineticFriction = kineticFriction;
            Restitution = restitution;
            RollingResistance = rollingResistance;
            Stiffness = stiffness;
            YieldStrength = yieldStrength;
            Density = density;
        }

        public string Name { get; }

        /// <summary>Friction against a generic surface (used when a pair is not in the table: μ = √(μa·μb)).</summary>
        public double StaticFriction { get; }

        public double KineticFriction { get; }
        public double Restitution { get; }
        public double RollingResistance { get; }

        /// <summary>Local contact stiffness of a small robot-sized contact patch (N/m).</summary>
        public double Stiffness { get; }

        /// <summary>Tensile/flexural strength (Pa) used for breakage of parts made of this material.</summary>
        public double YieldStrength { get; }

        public double Density { get; }
    }

    /// <summary>
    /// Material table. Values are typical dry-surface engineering figures (friction tables such
    /// as the Engineering ToolBox / Marks' handbook, rolling resistance of small rubber and hard
    /// wheels); pairs that are measured are listed explicitly, other pairs fall back to
    /// μ = √(μa·μb), e = max(ea, eb), C_rr = max, stiffness in series.
    /// </summary>
    public static class Materials
    {
        private static readonly Dictionary<string, SurfaceMaterial> ByName = new Dictionary<string, SurfaceMaterial>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<(string, string), ContactProperties> PairTable = new Dictionary<(string, string), ContactProperties>();

        public const string Default = "plastic";

        static Materials()
        {
            //   name        μs    μk    e     Crr     k(N/m)  yield(Pa) ρ(kg/m³)
            M("rubber", 1.00, 0.80, 0.60, 0.012, 3e4, 15e6, 1100, "kauçuk", "lastik", "tyre", "tire");
            M("asphalt", 0.70, 0.60, 0.40, 0.010, 1e6, 3e6, 2300, "asfalt");
            M("concrete", 0.70, 0.60, 0.40, 0.010, 1e6, 3e6, 2400, "beton");
            M("wood", 0.45, 0.35, 0.45, 0.010, 3e5, 40e6, 600, "ahşap", "ahsap", "plywood", "kontrplak", "parke");
            M("mdf", 0.40, 0.33, 0.40, 0.010, 3e5, 20e6, 750);
            M("paper", 0.45, 0.40, 0.30, 0.010, 2e5, 10e6, 800, "kağıt", "kagit", "cardboard", "karton");
            M("tile", 0.50, 0.40, 0.50, 0.008, 1e6, 30e6, 2300, "fayans", "ceramic", "seramik");
            M("carpet", 0.60, 0.50, 0.10, 0.040, 2e4, 5e6, 300, "halı", "hali");
            M("grass", 0.55, 0.45, 0.15, 0.080, 1e4, 1e6, 500, "çim", "cim");
            M("sand", 0.60, 0.50, 0.05, 0.250, 1e4, 1e5, 1600, "kum");
            M("regolith", 0.60, 0.50, 0.05, 0.150, 2e4, 1e5, 1500, "ay tozu");
            M("ice", 0.10, 0.03, 0.20, 0.010, 5e5, 1e6, 917, "buz");
            M("glass", 0.90, 0.40, 0.60, 0.005, 1e6, 50e6, 2500, "cam");
            M("plastic", 0.40, 0.30, 0.45, 0.010, 2e5, 40e6, 1100, "plastik", "generic");
            M("pla", 0.40, 0.30, 0.45, 0.010, 2e5, 50e6, 1240);
            M("petg", 0.40, 0.30, 0.45, 0.010, 2e5, 50e6, 1270);
            M("abs", 0.40, 0.30, 0.45, 0.010, 2e5, 40e6, 1040);
            M("nylon", 0.30, 0.25, 0.45, 0.010, 2e5, 70e6, 1140, "naylon");
            M("acrylic", 0.50, 0.40, 0.50, 0.008, 3e5, 65e6, 1180, "akrilik", "akrİlİk", "pmma");
            M("fr4", 0.40, 0.30, 0.50, 0.008, 5e5, 300e6, 1850, "pcb");
            M("carbon", 0.40, 0.30, 0.50, 0.008, 8e5, 600e6, 1600, "karbon", "carbon fibre", "carbon fiber");
            M("aluminium", 0.61, 0.47, 0.60, 0.004, 1e6, 270e6, 2700, "aluminum", "alüminyum", "aluminyum");
            M("steel", 0.74, 0.57, 0.60, 0.002, 2e6, 350e6, 7850, "çelik", "celik");
            M("brass", 0.50, 0.40, 0.55, 0.003, 1.5e6, 200e6, 8500, "pirinç");
            M("pom", 0.25, 0.20, 0.45, 0.008, 3e5, 65e6, 1410, "asetal", "acetal", "delrin");   // [drive] gears
            M("zinc", 0.60, 0.50, 0.55, 0.003, 1.5e6, 210e6, 6600, "çinko", "cinko", "zamak");   // [drive] cast housings
            M("ptfe", 0.05, 0.04, 0.40, 0.010, 1e5, 20e6, 2200, "teflon");
            M("foam", 0.60, 0.50, 0.10, 0.050, 2e3, 0.3e6, 30, "köpük", "kopuk", "sünger");

            // Measured / commonly quoted pairs.            μs    μk    e     Crr
            P("rubber", "asphalt", 0.90, 0.75, 0.60, 0.012);
            P("rubber", "concrete", 1.00, 0.80, 0.60, 0.010);
            P("rubber", "wood", 0.75, 0.60, 0.55, 0.010);
            P("rubber", "mdf", 0.75, 0.60, 0.55, 0.010);
            P("rubber", "paper", 0.75, 0.62, 0.50, 0.010);
            P("rubber", "tile", 0.70, 0.55, 0.60, 0.008);
            P("rubber", "carpet", 0.90, 0.70, 0.20, 0.040);
            P("rubber", "grass", 0.55, 0.45, 0.20, 0.080);
            P("rubber", "sand", 0.60, 0.50, 0.10, 0.250);
            P("rubber", "regolith", 0.60, 0.50, 0.10, 0.150);
            P("rubber", "ice", 0.15, 0.08, 0.30, 0.010);
            P("rubber", "steel", 0.70, 0.55, 0.60, 0.006);
            P("rubber", "aluminium", 0.65, 0.50, 0.60, 0.006);
            P("rubber", "glass", 0.90, 0.70, 0.60, 0.006);
            P("rubber", "rubber", 1.15, 1.00, 0.70, 0.015);
            P("pla", "wood", 0.40, 0.30, 0.45, 0.010);
            P("pla", "pla", 0.35, 0.25, 0.45, 0.010);
            P("abs", "wood", 0.40, 0.30, 0.45, 0.010);
            P("aluminium", "steel", 0.61, 0.47, 0.60, 0.002);
            P("aluminium", "aluminium", 1.05, 0.95, 0.60, 0.002);
            P("steel", "steel", 0.74, 0.57, 0.65, 0.001);
            P("steel", "ice", 0.03, 0.01, 0.30, 0.001);
            P("steel", "wood", 0.45, 0.35, 0.50, 0.004);
            P("steel", "concrete", 0.55, 0.45, 0.50, 0.003);
            P("steel", "ptfe", 0.04, 0.04, 0.40, 0.002);
            P("steel", "glass", 0.60, 0.40, 0.70, 0.002);
            P("wood", "wood", 0.50, 0.30, 0.50, 0.008);
            P("wood", "concrete", 0.62, 0.50, 0.45, 0.008);
            P("glass", "glass", 0.90, 0.40, 0.70, 0.003);
            P("ice", "ice", 0.10, 0.03, 0.20, 0.005);
            P("ptfe", "ptfe", 0.04, 0.04, 0.40, 0.005);

            int n = List.Count;
            Matrix = new ContactProperties[n * n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                Matrix[i * n + j] = Compute(List[i].Name, List[j].Name);
        }

        private static readonly List<SurfaceMaterial> List = new List<SurfaceMaterial>();
        private static readonly Dictionary<string, int> IndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly ContactProperties[] Matrix;

        private static void M(string name, double us, double uk, double e, double crr, double k, double yield, double rho, params string[] aliases)
        {
            var m = new SurfaceMaterial(name, us, uk, e, crr, k, yield, rho);
            ByName[name] = m;
            IndexByName[name] = List.Count;
            List.Add(m);
            Aliases[name] = name;
            foreach (var a in aliases) Aliases[Normalize(a)] = name;
        }

        /// <summary>Index of a material (for fast pair lookups); unknown names map to "plastic".</summary>
        public static int IndexOf(string? name) => IndexByName[Canonical(name)];

        /// <summary>Contact properties by material index.</summary>
        public static ContactProperties Pair(int a, int b) => Matrix[a * List.Count + b];

        public static string NameOf(int index) => List[index].Name;

        private static void P(string a, string b, double us, double uk, double e, double crr)
        {
            var ma = ByName[a];
            var mb = ByName[b];
            var props = new ContactProperties(us, uk, e, crr, Series(ma.Stiffness, mb.Stiffness));
            PairTable[(a, b)] = props;
            PairTable[(b, a)] = props;
        }

        private static double Series(double a, double b) => a * b / (a + b);

        private static string Normalize(string s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        public static IEnumerable<string> Names => ByName.Keys;

        /// <summary>Canonical material name (aliases, Turkish names and case are accepted); unknown → "plastic".</summary>
        public static string Canonical(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Default;
            return Aliases.TryGetValue(Normalize(name!), out var c) ? c : Default;
        }

        public static bool IsKnown(string? name) => !string.IsNullOrWhiteSpace(name) && Aliases.ContainsKey(Normalize(name!));

        public static SurfaceMaterial Get(string? name) => ByName[Canonical(name)];

        /// <summary>Contact properties of two materials.</summary>
        public static ContactProperties Pair(string? a, string? b) => Pair(IndexOf(a), IndexOf(b));

        private static ContactProperties Compute(string ca, string cb)
        {
            if (PairTable.TryGetValue((ca, cb), out var p)) return p;
            var ma = ByName[ca];
            var mb = ByName[cb];
            return new ContactProperties(Math.Sqrt(ma.StaticFriction * mb.StaticFriction), Math.Sqrt(ma.KineticFriction * mb.KineticFriction),
                Math.Max(ma.Restitution, mb.Restitution), Math.Max(ma.RollingResistance, mb.RollingResistance), Series(ma.Stiffness, mb.Stiffness));
        }

        /// <summary>True when the pair comes from the measured table rather than the mixing rule.</summary>
        public static bool IsTabulated(string? a, string? b) => PairTable.ContainsKey((Canonical(a), Canonical(b)));
    }
}
