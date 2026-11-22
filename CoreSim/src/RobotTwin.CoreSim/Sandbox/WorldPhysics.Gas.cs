using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>A gas: specific gas constant R (J/kg·K) and heat-capacity ratio γ.</summary>
    public readonly struct GasSpecies
    {
        public GasSpecies(string name, double r, double gamma)
        {
            Name = name;
            R = r;
            Gamma = gamma;
        }

        public string Name { get; }
        public double R { get; }
        public double Gamma { get; }
        public double Cp => Gamma * R / (Gamma - 1);
        public double Cv => R / (Gamma - 1);

        public static GasSpecies Air { get; } = new GasSpecies("hava", 287.05, 1.40);

        public static GasSpecies For(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "co2" or "karbondioksit" => new GasSpecies("CO₂", 188.9, 1.29),
            "n2" or "azot" or "nitrogen" => new GasSpecies("azot", 296.8, 1.40),
            "he" or "helyum" or "helium" => new GasSpecies("helyum", 2077.1, 1.66),
            "h2" or "hidrojen" or "hydrogen" => new GasSpecies("hidrojen", 4124.2, 1.41),
            _ => Air
        };
    }

    /// <summary>
    /// A solid propellant: density, burn-rate law r = a·pⁿ (Saint-Robert/Vieille, piecewise by pressure)
    /// and its combustion products (flame temperature with combustion efficiency, R, γ).
    /// </summary>
    public sealed class Propellant
    {
        public string Name { get; init; } = string.Empty;
        public double Density { get; init; }                 // kg/m³ as cast / pressed
        public double FlameTemperature { get; init; }        // K, delivered (not ideal)
        public GasSpecies Products { get; init; }

        /// <summary>Surface temperature at which the propellant lights (K).</summary>
        public double IgnitionTemperature { get; init; } = 600;

        /// <summary>Temperature of the burning surface (K), for erosive burning.</summary>
        public double BurningSurfaceTemperature { get; init; } = 800;

        /// <summary>Solid propellant thermal conductivity (W/m·K) and specific heat (J/kg·K).</summary>
        public double SolidConductivity { get; init; } = 0.4;
        public double SolidHeatCapacity { get; init; } = 1300;

        /// <summary>Emissivity of the particle-laden products towards the grain and the wall.</summary>
        public double GasEmissivity { get; init; } = 0.3;

        /// <summary>Noble–Abel co-volume of the products (m³/kg): p·(V − m·b) = m·R·T.</summary>
        public double Covolume { get; init; } = 1.0e-3;

        /// <summary>(upper pressure MPa, a in mm/s·MPa⁻ⁿ, n) segments, ascending.</summary>
        public (double UpToMPa, double A, double N)[] BurnRate { get; init; } = Array.Empty<(double, double, double)>();

        /// <summary>Linear burn rate (m/s) at chamber pressure <paramref name="pa"/> (Pa absolute).</summary>
        public double Rate(double pa)
        {
            double mpa = Math.Max(0.02, pa / 1e6);
            foreach (var (upTo, a, n) in BurnRate)
                if (mpa <= upTo) return a * Math.Pow(mpa, n) * 1e-3;
            var last = BurnRate[BurnRate.Length - 1];
            return last.A * Math.Pow(mpa, last.N) * 1e-3;
        }

        /// <summary>
        /// Richard Nakka's measured sorbitol/dextrose "rocket candy" (65/35 KNO₃) and a generic
        /// ammonium-perchlorate composite (APCP); products are the two-phase effective values used for
        /// nozzle design (Nakka, "Solid Rocket Motor Theory"; Sutton &amp; Biblarz ch. 12).
        /// </summary>
        public static Propellant For(string? name) => (name ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "KNDX" => new Propellant
            {
                Name = "KNDX", Density = 1785, FlameTemperature = 1500, Products = new GasSpecies("KNDX ürünleri", 8314.46 / 42.4, 1.131),
                BurnRate = new[] { (0.779, 7.0, 0.30), (2.572, 8.875, 0.619), (5.930, 7.553, 0.0), (8.502, 3.841, 0.688), (11.2, 17.2, -0.148) },
                IgnitionTemperature = 580, BurningSurfaceTemperature = 800
            },
            "APCP" => new Propellant
            {
                Name = "APCP", Density = 1700, FlameTemperature = 2800, Products = new GasSpecies("APCP ürünleri", 8314.46 / 25.0, 1.20),
                BurnRate = new[] { (30.0, 3.5, 0.35) },
                IgnitionTemperature = 690, BurningSurfaceTemperature = 950, SolidHeatCapacity = 1250, GasEmissivity = 0.4
            },
            _ => new Propellant
            {
                Name = "KNSB", Density = 1800, FlameTemperature = 1600, Products = new GasSpecies("KNSB ürünleri", 8314.46 / 39.9, 1.137),
                BurnRate = new[] { (0.807, 10.708, 0.625), (1.503, 8.763, 0.0), (3.792, 7.852, -0.013), (7.033, 3.907, 0.535), (10.67, 9.653, 0.064), (30.0, 9.653, 0.064) },
                IgnitionTemperature = 590, BurningSurfaceTemperature = 800
            }
        };

        /// <summary>
        /// Igniter pyrogen (black-powder class, KNO₃-based like the sugar propellants): two-phase effective
        /// products at a delivered ≈ 2000 K. An e-match head alone is ≈ 0.03 g of it.
        /// </summary>
        public static Propellant Pyrogen { get; } = new Propellant
        {
            Name = "ateşleyici", Density = 1700, FlameTemperature = 2000, Products = new GasSpecies("ateşleyici ürünleri", 8314.46 / 40.0, 1.14),
            BurnRate = new[] { (30.0, 10.0, 0.3) }
        };
    }

    /// <summary>What a pressure vessel is doing right now (for the inspector, the API and tests).</summary>
    public sealed record GasReadout(
        double GaugePressure, double TemperatureK, double GasMass, double WaterLitres, double GrainLeft,
        double Thrust, double MassFlow, double ExitVelocity, bool Choked, bool Burning, bool Burst, string Phase,
        double LitFraction = 0, double SurfaceK = 0, bool IgniterBurning = false);

    /// <summary>
    /// [gas] Pressure vessels in the world, one physics for all of them: a PET bottle pumped up with
    /// air and part-filled with water, a CO₂ cartridge, a helium balloon tank, or a motor casing holding
    /// a solid propellant grain. Each vessel is a body ("vessel" = true) whose interior holds gas
    /// (ideal gas, p = m·R·T / V, its own R and γ, mixed by mass as products are added), optionally
    /// water and a propellant grain. Nothing about "rockets" is special-cased: a grain burns at
    /// r = a·pⁿ over its burning surface and adds hot gas, the nozzle passes isentropic flow (choked
    /// at the throat when the pressure ratio is high enough, expanding to its exit area), water is
    /// pushed out ahead of the gas by Bernoulli under the felt acceleration, and the momentum and
    /// pressure thrust ṁ·vₑ + (pₑ − pₐ)·Aₑ act on the body at the nozzle. Thrust, chamber pressure,
    /// burn time, the vessel's falling mass, heating of its wall (through the thermal network) and
    /// bursting when the hoop stress reaches the wall's strength all follow from the same equations.
    /// Parameters (object): vessel, gas, pressure (bar gauge), volume (L, else from the shape and
    /// wall), wall (mm), water (L), nozzle (throat mm), nozzle.exit (mm), nozzle.axis (±x/±y/±z,
    /// where the jet leaves), valve.at (s: the launcher holds the nozzle closed until then), grain
    /// (KNSB/KNDX/APCP), grain.diameter, grain.core, grain.length (mm per segment), grain.segments,
    /// igniter (e-match object; or ignite.at in s), burst (bar gauge, else from the wall).
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private sealed class Vessel
        {
            public Item Item = null!;
            public double Volume;                 // m³ interior
            public double Wall;                   // m
            public double InnerRadius;            // m (for hoop stress and the water column area)
            public double InnerLength;            // m along the casing
            public double CrossArea;              // m² across the nozzle axis
            public double BurstGauge;             // Pa
            public double DryMass;                // kg (body without contents)
            public Vec3 DryInertia;
            public Vec3 NozzleLocal, AxisLocal;   // object frame: nozzle exit centre, jet direction
            public double Throat, Exit;           // m² areas (0 = closed)
            public double Cd = 0.95;
            public double ValveAt;
            public double IgniteAt = double.NaN;
            public string? Igniter;

            // igniter: an e-match head plus an optional pyrogen charge, burning for IgniterTime
            public double IgniterCharge = 0.5e-3;      // kg
            public double IgniterTime = 0.02;          // s
            public double IgniterStart = double.NaN;   // s
            public double IgniterLeft;                 // kg not yet burned
            public bool IgnitionFailed, FlameSpreadReported, FirstLitReported, BurnoutReported;
            public double HottestSurface;               // K, before the grain lit (for the failure report)

            // contents
            public double AirMass, ProductMass, IgniterGasMass;   // kg of the initial gas, grain products, igniter products
            public GasSpecies Gas;
            public double U;                      // J internal energy of the gas
            public double WaterVolume;            // m³
            public Propellant? Grain;
            public SolidGrain? Charge;
            public bool Ignited, Burst, WaterOut;   // Ignited: the igniter has been fired

            // readout
            public double Thrust, MassFlow, ExitVelocity;
            public bool Choked;
            public Vec3 PrevVelocity;
            public double HeatJ;                  // J into the wall since the last thermal step

            public double GasMass => AirMass + ProductMass + IgniterGasMass;

            public bool BurnedOut => Charge != null && Charge.EverLit && Charge.BurnedOut;

            public bool Burning => Charge != null && Charge.AnyLit;

            public bool IgniterBurning(double time) => !double.IsNaN(IgniterStart) && time >= IgniterStart && IgniterLeft > 0;

            public GasSpecies Mix
            {
                get
                {
                    double m = GasMass;
                    if (m <= 1e-15 || ProductMass + IgniterGasMass <= 0) return Gas;
                    var pr = Grain?.Products ?? Propellant.Pyrogen.Products;
                    var ig = Propellant.Pyrogen.Products;
                    double r = (AirMass * Gas.R + ProductMass * pr.R + IgniterGasMass * ig.R) / m;
                    double cp = (AirMass * Gas.Cp + ProductMass * pr.Cp + IgniterGasMass * ig.Cp) / m;
                    string name = ProductMass >= AirMass && ProductMass >= IgniterGasMass ? pr.Name : IgniterGasMass > AirMass ? ig.Name : Gas.Name;
                    return new GasSpecies(name, r, cp / (cp - r));
                }
            }

            public double GrainVolume => Charge?.Volume ?? 0;

            public double BurningArea => Charge?.LitArea ?? 0;

            public double GasVolume => Math.Max(1e-9, Volume - WaterVolume - GrainVolume);

            public double Temperature => U / Math.Max(1e-12, GasMass * Mix.Cv);

            /// <summary>Noble–Abel: the hot products' own volume (co-volume b) is not free to the gas.</summary>
            public double Pressure
            {
                get
                {
                    double b = (Grain?.Covolume ?? Propellant.Pyrogen.Covolume) * (ProductMass + IgniterGasMass);
                    double free = Math.Max(0.1 * GasVolume, GasVolume - b);
                    return GasMass * Mix.R * Temperature / free;
                }
            }
        }

        private readonly List<Vessel> _vessels = new List<Vessel>();

        public static bool IsVessel(WorldObject o) => o.Kind == WorldObjectKind.Body && Flag(o, "vessel");

        private static double Num(WorldObject o, string key, double fallback) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        private static Vec3 AxisOf(string? text) => (text ?? "-z").Trim().ToLowerInvariant() switch
        {
            "+x" or "x" => Vec3.UnitX,
            "-x" => -Vec3.UnitX,
            "+y" or "y" => Vec3.UnitY,
            "-y" => -Vec3.UnitY,
            "+z" or "z" => Vec3.UnitZ,
            _ => -Vec3.UnitZ
        };

        /// <summary>Builds the vessels after the bodies (called from <see cref="Build"/>).</summary>
        private void BuildGas(WorldDocument world)
        {
            _vessels.Clear();
            double ambientK = world.Environment.AmbientC + 273.15;
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                if (!IsVessel(o) || it.Body == null) continue;
                bool round = o.Shape is "cylinder" or "sphere";
                double wall = Math.Max(0.0002, Num(o, "wall", 2.0) / 1000);
                double rOut = o.Shape == "sphere" ? o.SizeX / 2 : round ? o.SizeX / 2 : Math.Min(o.SizeX, o.SizeY) / 2;
                double rIn = Math.Max(1e-4, rOut - wall);
                double inner = o.Shape switch
                {
                    "sphere" => 4.0 / 3 * Math.PI * rIn * rIn * rIn,
                    "cylinder" => Math.PI * rIn * rIn * Math.Max(1e-4, o.SizeZ - 2 * wall),
                    _ => Math.Max(1e-9, (o.SizeX - 2 * wall) * (o.SizeY - 2 * wall) * (o.SizeZ - 2 * wall))
                };
                double volume = Num(o, "volume", 0) > 0 ? Num(o, "volume", 0) / 1000 : inner;
                var axis = AxisOf(o.Parameters.TryGetValue("nozzle.axis", out var ax) ? ax : null);
                // The nozzle sits on the face the jet leaves through (object origin: bottom centre).
                double hz = o.Shape == "sphere" ? o.SizeX : o.SizeZ;
                var face = new Vec3(axis.X * o.SizeX / 2, axis.Y * (o.Shape == "cylinder" ? o.SizeX : o.SizeY) / 2, hz / 2 + axis.Z * hz / 2);

                // Thin-wall hoop stress σ = p·r/t (σ = p·r/2t for a sphere) at the wall material's strength.
                var mat = StructuralMaterial.For(o.Material);
                double burst = Num(o, "burst", 0) > 0 ? Num(o, "burst", 0) * 1e5 : mat.Yield * wall / rIn * (o.Shape == "sphere" ? 2 : 1);

                double throat = Num(o, "nozzle", 0) / 1000, exit = Math.Max(throat, Num(o, "nozzle.exit", 0) / 1000);
                var v = new Vessel
                {
                    Item = it,
                    Volume = volume,
                    Wall = wall,
                    InnerRadius = rIn,
                    CrossArea = Math.Abs(axis.Z) > 0.5 && round ? Math.PI * rIn * rIn : Math.Max(1e-6, volume / Math.Max(1e-3, Math.Abs(axis.Z) > 0.5 ? o.SizeZ : Math.Abs(axis.X) > 0.5 ? o.SizeX : o.SizeY)),
                    BurstGauge = burst,
                    DryMass = it.Body.Mass,
                    DryInertia = it.Body.Inertia,
                    NozzleLocal = face,
                    AxisLocal = axis,
                    Throat = Math.PI / 4 * throat * throat,
                    Exit = Math.PI / 4 * exit * exit,
                    ValveAt = Num(o, "valve.at", 0),
                    Gas = GasSpecies.For(o.Parameters.TryGetValue("gas", out var g) ? g : null),
                    WaterVolume = Math.Min(volume * 0.95, Num(o, "water", 0) / 1000),
                    Igniter = o.Parameters.TryGetValue("igniter", out var ig) && ig.Trim().Length > 0 ? ig.Trim() : null,
                    IgniteAt = Num(o, "ignite.at", double.NaN)
                };
                v.InnerLength = o.Shape == "sphere" ? 2 * rIn : Math.Max(1e-3, (Math.Abs(axis.X) > 0.5 ? o.SizeX : Math.Abs(axis.Y) > 0.5 ? o.SizeY : o.SizeZ) - 2 * wall);
                if (o.Parameters.TryGetValue("grain", out var grain) && grain.Trim().Length > 0)
                {
                    v.Grain = Propellant.For(grain);
                    double outer = Num(o, "grain.diameter", 2 * rIn * 1000 - 0.5) / 1000;
                    double core = Num(o, "grain.core", outer * 1000 / 3) / 1000;
                    int segments = Math.Max(1, (int)Num(o, "grain.segments", 1));
                    double length = Num(o, "grain.length", Math.Max(5, (o.SizeZ - 2 * wall) * 1000 / segments - 1)) / 1000;
                    v.Charge = new SolidGrain(v.Grain, outer, core, length, segments, ambientK);
                    // Igniter: an e-match head (≈ 0.03 g) plus the pyrogen charge given in grams (default 0.5 g).
                    v.IgniterCharge = (0.03 + Math.Max(0, Num(o, "igniter.charge", 0.5))) / 1000;
                    v.IgniterTime = Math.Max(1e-3, Num(o, "igniter.time", 20) / 1000);
                    // The igniter: named, or an e-match jointed to the motor.
                    v.Igniter ??= world.Joints.Where(j => j.ObjectA == o.Id || j.ObjectB == o.Id)
                        .Select(j => j.ObjectA == o.Id ? j.ObjectB : j.ObjectA)
                        .FirstOrDefault(id => id != null && world.Find(id) is { } e && (e.PartId ?? string.Empty).Replace("-", string.Empty).Contains("ematch", StringComparison.OrdinalIgnoreCase));
                }

                double p0 = AmbientPressure(it) + Num(o, "pressure", 0) * 1e5;
                v.AirMass = p0 * v.GasVolume / (v.Gas.R * ambientK);
                v.U = v.AirMass * v.Gas.Cv * ambientK;
                v.PrevVelocity = it.Body.Velocity;
                _vessels.Add(v);
                UpdateVesselMass(v);
            }
        }

        private double AmbientPressure(Item it)
        {
            double z = it.Body?.Position.Z ?? 0;
            return RobotTwin.CoreSim.Sdk.Atmosphere.Isa(Math.Max(-400, z)).PressurePa;
        }

        private void UpdateVesselMass(Vessel v)
        {
            var b = v.Item.Body;
            double contents = v.GasMass + v.WaterVolume * 998.0 + v.GrainVolume * (v.Grain?.Density ?? 0);
            double m = v.DryMass + contents;
            double k = m / Math.Max(1e-9, b.Mass);
            b.Mass = m;
            b.Inertia = v.DryInertia * (m / Math.Max(1e-9, v.DryMass));
            _ = k;
        }

        /// <summary>The state of a vessel (null when the object holds no gas).</summary>
        public GasReadout? GasOf(string objectId)
        {
            var v = _vessels.FirstOrDefault(x => string.Equals(x.Item.Object.Id, objectId, StringComparison.OrdinalIgnoreCase));
            if (v == null) return null;
            double pa = AmbientPressure(v.Item);
            var c = v.Charge;
            double lit = c?.LitFraction ?? 0;
            string phase = v.Burst ? "patladı"
                : v.IgnitionFailed ? "ateşlenemedi (yakıt yüzeyi tutuşma sıcaklığına çıkmadı)"
                : v.IgniterBurning(Scene.Time) && !(c?.AnyLit ?? false) ? "ateşleyici yanıyor, yakıt ısınıyor"
                : v.Burning && lit < 0.98 ? $"alev yayılıyor (yüzeyin %{lit * 100:0}'i yanıyor)"
                : v.Burning ? "yanıyor"
                : v.WaterVolume > 1e-7 && v.MassFlow > 0 ? "su püskürtüyor"
                : v.MassFlow > 1e-6 ? "gaz çıkıyor" : Scene.Time < v.ValveAt ? "rampada (vana kapalı)" : "dengede";
            double grainLeft = c == null ? 0 : c.Volume / Math.Max(1e-12, c.InitialVolume);
            double surface = c == null ? 0 : c.AnyLit ? c.Propellant.BurningSurfaceTemperature : c.HottestUnlitSurface;
            return new GasReadout(v.Pressure - pa, v.Temperature, v.GasMass, v.WaterVolume * 1000, grainLeft, v.Thrust, v.MassFlow, v.ExitVelocity, v.Choked, v.Burning, v.Burst, phase,
                lit, surface, v.IgniterBurning(Scene.Time));
        }

        /// <summary>Where a vessel's jet leaves (world) and which way it goes; null without a vessel.</summary>
        public (Vec3 Point, Vec3 Direction, bool Water, bool Hot)? NozzleOf(string objectId)
        {
            var v = _vessels.FirstOrDefault(x => string.Equals(x.Item.Object.Id, objectId, StringComparison.OrdinalIgnoreCase));
            if (v == null || PoseOf(objectId) is not { } pose) return null;
            return (pose.Position + pose.Orientation.Rotate(v.NozzleLocal), pose.Orientation.Rotate(v.AxisLocal).Normalized(), v.WaterVolume > 1e-9, v.Burning || v.IgniterBurning(Scene.Time));
        }

        /// <summary>One physics step of every vessel: ignite, burn, flow, thrust, heat, mass, burst.</summary>
        private void StepGas(double dt, Analog.AnalogSimulator? sim)
        {
            var lit = new List<int>();
            var pyro = Propellant.Pyrogen;
            foreach (var v in _vessels)
            {
                var body = v.Item.Body;
                var o = v.Item.Object;
                if (PoseOf(o.Id) is not { } pose) continue;
                var (pos, q) = pose;
                var axisW = q.Rotate(v.AxisLocal).Normalized();
                var nozzleW = pos + q.Rotate(v.NozzleLocal);
                double pa = RobotTwin.CoreSim.Sdk.Atmosphere.Isa(Math.Max(-400, nozzleW.Z)).PressurePa;

                // Felt (specific) acceleration: what holds water against the nozzle.
                var accel = (body.Velocity - v.PrevVelocity) / dt;
                v.PrevVelocity = body.Velocity;
                var felt = accel - Scene.Gravity;

                // Firing — an e-match whose bridgewire fired, or a set time — lights the igniter, not the grain:
                // the grain lights where its surface has soaked up enough heat from the igniter's gas.
                if (v.Charge != null && !v.Ignited)
                {
                    double at = !double.IsNaN(v.IgniteAt) && Scene.Time >= v.IgniteAt ? v.IgniteAt : double.NaN;
                    if (double.IsNaN(at) && v.Igniter != null && _circuit?.ComponentRef(v.Igniter) is { } er)
                    {
                        var fired = _circuit.Compiled.DevicesOf(er).OfType<Design.EMatch>().FirstOrDefault(e => e.Fired);
                        if (fired != null) at = double.IsNaN(fired.FireTime) ? Scene.Time : Math.Min(Scene.Time, fired.FireTime);
                    }

                    if (!double.IsNaN(at))
                    {
                        v.Ignited = true;
                        v.IgniterStart = Math.Max(at, Scene.Time - dt);
                        v.IgniterLeft = v.IgniterCharge;
                        Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.Impact,
                            $"{o.Name}: ateşleyici yandı ({v.IgniterCharge * 1000:0.00} g barut, {v.IgniterTime * 1000:0} ms)", 0, false));
                    }
                }

                bool open = v.Throat > 0 && Scene.Time >= v.ValveAt || v.Burst;
                double thrustImpulse = 0, flowMass = 0, vExit = 0;
                bool choked = false;

                // The chamber's time constant can be well under a millisecond: sub-step the gas.
                double t = 0;
                int guard = 0;
                while (t < dt - 1e-12 && guard++ < 6000)
                {
                    var mix = v.Mix;
                    double T = Math.Max(50, v.Temperature), p = v.Pressure, m = v.GasMass;
                    double area = v.Burst ? Math.Max(v.Throat, 0.3 * v.CrossArea) : v.Throat;
                    double exit = v.Burst ? area : v.Exit;
                    double now = Scene.Time - dt + t;

                    // The igniter's hot gas (entering at the head end) and the grain's products.
                    double ign = v.IgniterLeft > 0 && now >= v.IgniterStart - 1e-12 ? v.IgniterCharge / v.IgniterTime : 0;
                    double gen = v.Charge?.Evaluate(p, ign) ?? 0;

                    // Water first, while it covers the nozzle under the felt acceleration.
                    double feltAlong = felt.Dot(-axisW);   // > 0: pressed towards the nozzle
                    bool water = open && v.WaterVolume > 1e-9 && feltAlong > 0.5 && p > pa;
                    double outGas = 0, outWater = 0, ve = 0, pe = pa, ae = exit;
                    bool ch = false;
                    if (water)
                    {
                        double head = v.WaterVolume / Math.Max(1e-6, v.CrossArea);
                        double ratio = Math.Min(0.9, area / Math.Max(area, v.CrossArea));
                        double dv2 = Math.Max(0, 2 * (p - pa) / 998.0 + 2 * feltAlong * head) / (1 - ratio * ratio);
                        ve = Math.Sqrt(dv2);
                        outWater = 998.0 * 0.98 * area * ve;
                        ae = area;
                    }
                    else if (open && area > 0 && p > pa * 1.0001)
                    {
                        double g = mix.Gamma, r = mix.R;
                        double cr = Math.Pow(2 / (g + 1), g / (g - 1));
                        double pr = pa / p;
                        if (pr <= cr)
                        {
                            ch = true;
                            outGas = v.Cd * area * p * Math.Sqrt(g / (r * T)) * Math.Pow(2 / (g + 1), (g + 1) / (2 * (g - 1)));
                            double me = exit > area * 1.0001 ? SupersonicMach(exit / area, g) : 1.0;
                            pe = p * Math.Pow(1 + (g - 1) / 2 * me * me, -g / (g - 1));
                            // Over-expanded far enough, the boundary layer separates where the wall pressure falls to
                            // Schmucker's limit p_sep = p_a·(1.88·M − 1)^−0.64, and the jet leaves the wall there.
                            if (me > 1.05 && pe < pa * Math.Pow(1.88 * me - 1, -0.64))
                            {
                                double lo = 1.0, hi = me;
                                for (int it = 0; it < 50; it++)
                                {
                                    double mid = 0.5 * (lo + hi);
                                    double pm = p * Math.Pow(1 + (g - 1) / 2 * mid * mid, -g / (g - 1));
                                    if (pm > pa * Math.Pow(Math.Max(1e-6, 1.88 * mid - 1), -0.64)) lo = mid;
                                    else hi = mid;
                                }

                                me = 0.5 * (lo + hi);
                                pe = p * Math.Pow(1 + (g - 1) / 2 * me * me, -g / (g - 1));
                                ae = area * AreaRatioAt(me, g);
                            }

                            double tExit = T / (1 + (g - 1) / 2 * me * me);
                            ve = me * Math.Sqrt(g * r * tExit);
                        }
                        else
                        {
                            outGas = v.Cd * area * p * Math.Sqrt(2 * g / ((g - 1) * r * T) * (Math.Pow(pr, 2 / g) - Math.Pow(pr, (g + 1) / g)));
                            ve = Math.Sqrt(Math.Max(0, 2 * mix.Cp * T * (1 - Math.Pow(pr, (g - 1) / g))));
                            ae = area;
                        }
                    }
                    else if (open && area > 0 && p < pa * 0.999 && v.WaterVolume <= 1e-9)
                    {
                        // Air drawn back in once the vessel is below ambient (subsonic inflow).
                        var air = GasSpecies.Air;
                        double pr = p / pa, ta = Math.Max(200, (_ambient + 273.15));
                        outGas = -v.Cd * area * pa * Math.Sqrt(2 * air.Gamma / ((air.Gamma - 1) * air.R * ta) * (Math.Pow(pr, 2 / air.Gamma) - Math.Pow(pr, (air.Gamma + 1) / air.Gamma)));
                    }

                    // Step size: keep each sub-step a small part of the gas's turnover time, and short while the grain soaks.
                    double flow = Math.Abs(outGas) + gen + ign + 1e-12;
                    double h = Math.Min(dt - t, Math.Max(1e-7, 0.05 * Math.Max(1e-9, m) / flow));
                    if (water) h = Math.Min(h, Math.Max(1e-6, 0.02 * v.WaterVolume / Math.Max(1e-12, outWater / 998.0)));
                    if (v.Ignited && v.Charge is { } soaking && soaking.LitFraction < 0.999 && !soaking.BurnedOut && now < v.IgniterStart + v.IgniterTime + 0.5)
                        h = Math.Min(h, 2e-4);

                    // Wall heat: forced convection along the bore (Dittus–Boelter) and radiation over the casing the grain does
                    // not cover, plus the nozzle throat (Bartz) while hot gas is choked through it.
                    double wallT = (_thermal.TryGetValue(o.Id, out var tn) ? tn.T : _ambient) + 273.15;
                    double bore = 2 * v.InnerRadius;
                    double hBore = Math.Max(20, SolidGrain.TubeConvection(mix, T, Math.Max(0, outGas) / Math.Max(1e-9, v.CrossArea), bore));
                    double covered = v.Charge == null ? 0 : Math.Max(0, Math.PI * bore * v.Charge.Segments * v.Charge.SegmentLength - v.Charge.ExposedWall * bore / Math.Max(1e-6, v.Charge.Outer));
                    double exposed = 2 * Math.PI * v.InnerRadius * v.InnerRadius + Math.Max(0, Math.PI * bore * v.InnerLength - covered);
                    double eps = v.ProductMass + v.IgniterGasMass > 0.1 * m ? (v.Grain ?? pyro).GasEmissivity : 0;
                    double qWall = hBore * exposed * (T - wallT) + eps * 5.670374e-8 * exposed * (Math.Pow(T, 4) - Math.Pow(wallT, 4));
                    if (ch && T > 500)
                    {
                        double dt0 = Math.Sqrt(4 * area / Math.PI), g = mix.Gamma;
                        double cStar = Math.Sqrt(mix.R * T) / (Math.Sqrt(g) * Math.Pow(2 / (g + 1), (g + 1) / (2 * (g - 1))));
                        double mu = SolidGrain.Viscosity(mix, T), prn = SolidGrain.Prandtl(mix);
                        double hBartz = 0.026 / Math.Pow(dt0, 0.2) * Math.Pow(mu, 0.2) * mix.Cp / Math.Pow(prn, 0.6) * Math.Pow(p / cStar, 0.8);
                        double recovery = T * (1 + Math.Pow(prn, 1.0 / 3) * (g - 1) / 2) / (1 + (g - 1) / 2);
                        qWall += hBartz * Math.PI * dt0 * 2 * dt0 * (recovery - wallT);   // throat region: ± one throat diameter
                    }

                    // The grain: regression, and the soak of the surfaces not yet burning (heat drawn from the gas).
                    lit.Clear();
                    double soak = v.Charge?.Advance(h, ign, pyro.FlameTemperature, T, mix, lit) ?? 0;
                    if (lit.Count > 0 && v.Charge is { } gch)
                    {
                        if (!v.FirstLitReported)
                        {
                            v.FirstLitReported = true;
                            int z = lit[0];
                            string where = z < gch.Zones / 3.0 ? "baş tarafında" : z >= 2 * gch.Zones / 3.0 ? "lüle tarafında" : "ortasında";
                            Scene.Report(new PhysicsEvent(now, o.Id, PhysicsEventKind.Impact,
                                $"{o.Name}: yakıt ({v.Grain!.Name}) {where} tutuştu, ateşleyiciden {(now - v.IgniterStart) * 1000:0.0} ms sonra", 0, false));
                        }

                        if (!v.FlameSpreadReported && gch.LitFraction > 0.999)
                        {
                            v.FlameSpreadReported = true;
                            Scene.Report(new PhysicsEvent(now, o.Id, PhysicsEventKind.Impact,
                                $"{o.Name}: alev tüm yüzeye yayıldı ({(now - v.IgniterStart) * 1000:0.0} ms)", 0, false));
                        }
                    }

                    // Energy: products in at their flame temperatures, stagnation enthalpy out, work on the water, heat to the wall and grain.
                    double dVgas = outWater / 998.0 * h;
                    double hOut = outGas >= 0 ? mix.Cp * T : GasSpecies.Air.Cp * (_ambient + 273.15);
                    v.U += (gen * (v.Grain?.Products.Cp ?? 0) * (v.Grain?.FlameTemperature ?? 0) + ign * pyro.Products.Cp * pyro.FlameTemperature
                            - outGas * hOut - qWall) * h - soak - p * dVgas;
                    v.HeatJ += qWall * h;
                    if (outGas >= 0)
                    {
                        double share = Math.Min(1, outGas * h / Math.Max(1e-12, m));
                        v.ProductMass = Math.Max(0, v.ProductMass * (1 - share));
                        v.IgniterGasMass = Math.Max(0, v.IgniterGasMass * (1 - share));
                        v.AirMass = Math.Max(0, v.AirMass * (1 - share));
                    }
                    else
                    {
                        v.AirMass -= outGas * h;
                    }

                    v.ProductMass += gen * h;
                    if (ign > 0)
                    {
                        double burned = Math.Min(v.IgniterLeft, ign * h);
                        v.IgniterGasMass += burned;
                        v.IgniterLeft -= burned;
                    }

                    v.WaterVolume = Math.Max(0, v.WaterVolume - dVgas);
                    if (v.WaterVolume <= 1e-9 && !v.WaterOut && water)
                    {
                        v.WaterOut = true;
                        Scene.Report(new PhysicsEvent(now, o.Id, PhysicsEventKind.Impact, $"{o.Name}: su bitti, hava çıkıyor", 0, false));
                    }

                    if (v.U < 0) v.U = Math.Max(1e-9, v.GasMass) * v.Mix.Cv * 50;

                    double massFlow = Math.Max(0, outGas) + outWater;
                    thrustImpulse += (massFlow * ve + (water ? 0 : Math.Max(0, outGas) > 0 ? (pe - pa) * ae : 0)) * h;
                    flowMass += massFlow * h;
                    vExit = ve;
                    choked |= ch;
                    t += h;
                }

                if (v.Charge is { } c)
                {
                    if (v.Ignited && !c.EverLit && !v.IgnitionFailed && v.IgniterLeft <= 0 && Scene.Time > v.IgniterStart + v.IgniterTime + 0.3)
                    {
                        v.IgnitionFailed = true;
                        Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.Impact,
                            $"{o.Name}: ateşlenemedi — yakıt yüzeyi en çok {v.HottestSurface - 273.15:0} °C oldu, {v.Grain!.Name} {v.Grain.IgnitionTemperature - 273.15:0} °C'de tutuşur (ateşleyici {v.IgniterCharge * 1000:0.00} g)", 0, false));
                    }

                    if (!c.EverLit) v.HottestSurface = Math.Max(v.HottestSurface, c.HottestUnlitSurface);
                    if (c.EverLit && c.BurnedOut && !v.BurnoutReported)
                    {
                        v.BurnoutReported = true;
                        Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.Impact, $"{o.Name}: yakıt bitti", 0, false));
                    }
                }

                v.Thrust = thrustImpulse / dt;
                v.MassFlow = flowMass / dt;
                v.ExitVelocity = vExit;
                v.Choked = choked;
                if (Math.Abs(v.Thrust) > 1e-9) body.AddForceAtPoint(-axisW * v.Thrust, nozzleW);
                UpdateVesselMass(v);

                // The wall gives way when the hoop stress reaches its strength.
                if (!v.Burst && v.Pressure - pa > v.BurstGauge)
                {
                    v.Burst = true;
                    Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.BodyBroken,
                        $"{o.Name} patladı: {(v.Pressure - pa) / 1e5:0.0} bar (çeper dayanımı {v.BurstGauge / 1e5:0.0} bar)", v.Pressure - pa, true));
                }
            }
        }

        /// <summary>A/A* of isentropic flow at Mach <paramref name="m"/>.</summary>
        private static double AreaRatioAt(double m, double g) =>
            1 / m * Math.Pow(2 / (g + 1) * (1 + (g - 1) / 2 * m * m), (g + 1) / (2 * (g - 1)));

        /// <summary>The supersonic Mach number of a nozzle with exit/throat area ratio <paramref name="eps"/>.</summary>
        private static double SupersonicMach(double eps, double g)
        {
            double lo = 1.0, hi = 20.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (AreaRatioAt(mid, g) < eps) lo = mid;
                else hi = mid;
            }

            return 0.5 * (lo + hi);
        }

        /// <summary>Wall heat from the gas since the last thermal step (W over <paramref name="h"/> seconds).</summary>
        private double TakeGasHeat(string objectId, double h)
        {
            double j = 0;
            foreach (var v in _vessels)
            {
                if (!string.Equals(v.Item.Object.Id, objectId, StringComparison.OrdinalIgnoreCase)) continue;
                j += v.HeatJ;
                v.HeatJ = 0;
            }

            return h > 0 ? j / h : 0;
        }
    }
}
