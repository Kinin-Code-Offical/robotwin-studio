using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>
    /// Everything of the SDK that is saved with a world (<c>WorldDocument.Sdk</c>): the
    /// atmosphere, wind and aerodynamics, force fields, attached behaviours and expressions,
    /// C# scripts and which plugins the world uses. Plain data; <see cref="WorldSdkRuntime"/>
    /// turns it into forces inside the fixed physics step.
    /// </summary>
    public sealed class WorldSdkSettings
    {
        public AtmosphereSettings Atmosphere { get; set; } = new AtmosphereSettings();
        public WindSettings Wind { get; set; } = new WindSettings();
        public AeroSettings Aero { get; set; } = new AeroSettings();

        public List<ForceFieldSpec> Fields { get; set; } = new List<ForceFieldSpec>();
        public List<BehaviorAttachment> Behaviors { get; set; } = new List<BehaviorAttachment>();

        /// <summary>Per-object aerodynamic overrides (drag coefficient, area, buoyancy).</summary>
        public List<AeroOverride> ObjectAero { get; set; } = new List<AeroOverride>();

        /// <summary>C# behaviour scripts saved inside the world (compiled with the .NET SDK if installed).</summary>
        public List<WorldScript> Scripts { get; set; } = new List<WorldScript>();

        /// <summary>Ids of the plugins this world uses (their behaviours, fields and parts).</summary>
        public List<string> EnabledPlugins { get; set; } = new List<string>();

        /// <summary>Seed of every random process (turbulence, gusts, behaviour random streams).</summary>
        public long Seed { get; set; } = 1;

        /// <summary>Warn when one behaviour/field uses more than this much wall time per physics step on average (ms).</summary>
        public double StepBudgetMs { get; set; } = 0.25;

        /// <summary>Draw wind arrows and the wind sock in the 3D view.</summary>
        public bool ShowWind { get; set; } = true;

        public AeroOverride? AeroOf(string objectId) =>
            ObjectAero.FirstOrDefault(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));

        public AeroOverride AeroFor(string objectId)
        {
            var a = AeroOf(objectId);
            if (a == null)
            {
                a = new AeroOverride { ObjectId = objectId };
                ObjectAero.Add(a);
            }

            return a;
        }

        public IEnumerable<BehaviorAttachment> BehaviorsOf(string? objectId) =>
            Behaviors.Where(b => string.Equals(b.ObjectId ?? string.Empty, objectId ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        public string NextId(string prefix)
        {
            int max = 0;
            foreach (var id in Fields.Select(f => f.Id).Concat(Behaviors.Select(b => b.Id)).Concat(Scripts.Select(s => s.Id)))
            {
                if (id != null && id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id.Substring(prefix.Length), out int n)) max = Math.Max(max, n);
            }

            return prefix + (max + 1);
        }

        /// <summary>True when nothing differs from a new world (nothing configured).</summary>
        [JsonIgnore]
        public bool IsDefault =>
            Fields.Count == 0 && Behaviors.Count == 0 && Scripts.Count == 0 && ObjectAero.Count == 0 && Wind.Speed == 0 && Wind.Turbulence == "none" && !Wind.Gusts;
    }

    /// <summary>Air state. Temperature comes from the world's ambient temperature (Ortam sıcaklığı).</summary>
    public sealed class AtmosphereSettings
    {
        /// <summary>Sea-level reference pressure (QNH, hPa).</summary>
        public double PressureHPa { get; set; } = 1013.25;

        /// <summary>Altitude of the world's floor above sea level (m).</summary>
        public double AltitudeM { get; set; }

        /// <summary>Relative humidity 0–1 (humid air is slightly lighter).</summary>
        public double Humidity { get; set; }

        /// <summary>Fixed density (kg/m³) instead of computing it; null = from temperature, pressure, altitude, humidity.</summary>
        public double? DensityOverride { get; set; }
    }

    /// <summary>Wind: a mean wind with a height profile, continuous turbulence and discrete gusts.</summary>
    public sealed class WindSettings
    {
        /// <summary>Mean horizontal speed at the reference height (m/s).</summary>
        public double Speed { get; set; }

        /// <summary>Direction the wind blows TOWARDS, degrees counter-clockwise from +X (0 = towards +X, 90 = towards +Y).</summary>
        public double DirectionDeg { get; set; }

        /// <summary>Mean vertical component (m/s, + up): thermals, down-wash.</summary>
        public double Vertical { get; set; }

        /// <summary>"uniform", "power" (u ∝ (z/zref)^α) or "log" (u ∝ ln(z/z0)).</summary>
        public string Profile { get; set; } = "uniform";

        public double ReferenceHeight { get; set; } = 1.0;

        /// <summary>Power-law exponent α (0.10 open water … 0.14 open land … 0.3 city).</summary>
        public double PowerExponent { get; set; } = 0.143;

        /// <summary>Roughness length z0 of the log profile (m): 0.0002 water, 0.03 grass, 1 city.</summary>
        public double Roughness { get; set; } = 0.03;

        /// <summary>"none", "dryden" (seeded filtered noise) or "sine" (the older sum of sines).</summary>
        public string Turbulence { get; set; } = "none";

        /// <summary>Standard deviation of the horizontal turbulence (m/s).</summary>
        public double Intensity { get; set; } = 0.5;

        /// <summary>Vertical turbulence σ as a fraction of the horizontal σ.</summary>
        public double VerticalRatio { get; set; } = 0.5;

        /// <summary>Turbulence length scale L (m): correlation time = L / airspeed.</summary>
        public double LengthScale { get; set; } = 10.0;

        /// <summary>Discrete "1 − cos" gusts along the mean wind.</summary>
        public bool Gusts { get; set; }

        public double GustAmplitude { get; set; } = 2.0;
        public double GustDuration { get; set; } = 1.5;

        /// <summary>Mean time between gusts (s), exponentially distributed.</summary>
        public double GustInterval { get; set; } = 6.0;

        [JsonIgnore]
        public Vec3 MeanVector
        {
            get
            {
                double a = DirectionDeg * Math.PI / 180;
                return new Vec3(Speed * Math.Cos(a), Speed * Math.Sin(a), Vertical);
            }
        }
    }

    public sealed class AeroSettings
    {
        /// <summary>Quadratic air drag on every movable object (from its projected area and drag coefficient).</summary>
        public bool Drag { get; set; } = true;

        /// <summary>Air buoyancy ρ·V·g on every object (otherwise only on objects that ask for it).</summary>
        public bool Buoyancy { get; set; }

        /// <summary>Rotational air damping coefficient (N·m·s per m⁵, multiplied by size⁵·ρ); 0 = none.</summary>
        public double AngularDamping { get; set; } = 0.05;
    }

    public sealed class AeroOverride
    {
        public string ObjectId { get; set; } = string.Empty;
        public bool Drag { get; set; } = true;

        /// <summary>Drag coefficient instead of the shape's (box 1.05, sphere 0.47, cylinder 0.82/1.0 …).</summary>
        public double? Cd { get; set; }

        /// <summary>Reference area (m²) in every direction instead of the projected area.</summary>
        public double? AreaM2 { get; set; }

        public bool Buoyancy { get; set; }

        /// <summary>Displaced volume for buoyancy (m³); null = the collision shape's volume.</summary>
        public double? VolumeM3 { get; set; }
    }

    /// <summary>
    /// A force field or wind source. Kinds: "uniform", "radial", "vortex", "fan", "conveyor",
    /// "expression", "plugin". Region fields act inside an oriented box (centre, size, yaw).
    /// </summary>
    public sealed class ForceFieldSpec
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = "uniform";
        public bool Enabled { get; set; } = true;

        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }

        /// <summary>Region size (m); used when <see cref="Bounded"/> (always for fan and conveyor).</summary>
        public double SizeX { get; set; } = 0.5;
        public double SizeY { get; set; } = 0.5;
        public double SizeZ { get; set; } = 0.5;

        /// <summary>Region yaw about +Z (degrees).</summary>
        public double Yaw { get; set; }

        public bool Bounded { get; set; }

        /// <summary>Direction (field, fan outlet, belt, vortex axis); normalised at run time.</summary>
        public double DirX { get; set; } = 1;
        public double DirY { get; set; }
        public double DirZ { get; set; }

        /// <summary>
        /// Uniform: N (or m/s² as acceleration). Radial: N at <see cref="Radius"/> (positive attracts).
        /// Vortex: tangential air speed (m/s) at the core radius. Fan: outlet air speed (m/s).
        /// Conveyor: belt speed (m/s).
        /// </summary>
        public double Strength { get; set; } = 1.0;

        /// <summary>Strength is an acceleration (same for every mass) instead of a force.</summary>
        public bool Acceleration { get; set; }

        /// <summary>Radial/vortex radius (m): core radius, cut-off of "linear"/"gaussian" falloff.</summary>
        public double Radius { get; set; } = 0.5;

        /// <summary>Radial falloff: "none", "linear", "inverse-square", "gaussian".</summary>
        public string Falloff { get; set; } = "inverse-square";

        /// <summary>Vortex: radial inflow and updraft as fractions of the tangential speed.</summary>
        public double Inflow { get; set; }

        public double Updraft { get; set; }

        /// <summary>Conveyor grip: largest belt force as a friction coefficient × weight.</summary>
        public double Grip { get; set; } = 0.6;

        /// <summary>Program for "expression" fields: per body, writes F.x/F.y/F.z (and T.*) or wind.x/wind.y/wind.z.</summary>
        public string Expression { get; set; } = string.Empty;

        /// <summary>Type key of a plugin field ("pluginId:Namespace.Type").</summary>
        public string? PluginType { get; set; }

        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Object ids this field acts on; empty = every movable object.</summary>
        public List<string> Targets { get; set; } = new List<string>();

        [JsonIgnore]
        public Vec3 Center => new Vec3(X, Y, Z);
        [JsonIgnore]
        public Vec3 Size => new Vec3(SizeX, SizeY, SizeZ);
        [JsonIgnore]
        public Vec3 Direction => new Vec3(DirX, DirY, DirZ).Normalized();
        [JsonIgnore]
        public Quat Orientation => Quat.FromAxisAngle(Vec3.UnitZ, Yaw * Math.PI / 180);

        public ForceFieldSpec Clone()
        {
            var c = (ForceFieldSpec)MemberwiseClone();
            c.Parameters = new Dictionary<string, string>(Parameters, StringComparer.OrdinalIgnoreCase);
            c.Targets = new List<string>(Targets);
            return c;
        }
    }

    /// <summary>
    /// A behaviour attached to an object (or to the world when <see cref="ObjectId"/> is null):
    /// an expression ("expression"), a built-in ("builtin:…"), a plugin type ("pluginId:Type")
    /// or a script type ("script:Type").
    /// </summary>
    public sealed class BehaviorAttachment
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = ExpressionType;
        public string? ObjectId { get; set; }
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = string.Empty;

        /// <summary>Program of an "expression" behaviour.</summary>
        public string Expression { get; set; } = string.Empty;

        /// <summary>Values of the behaviour's [Parameter] properties (invariant culture text).</summary>
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public const string ExpressionType = "expression";

        [JsonIgnore]
        public bool IsExpression => string.Equals(Type, ExpressionType, StringComparison.OrdinalIgnoreCase);
    }

    public sealed class WorldScript
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = "Betik.cs";
        public string Source { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
    }
}
