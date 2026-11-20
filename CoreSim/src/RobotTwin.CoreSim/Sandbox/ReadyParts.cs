using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>What a ready-made mechanical part is.</summary>
    public enum ReadyPartKind
    {
        /// <summary>Rubber tyre on a hub; grips in every direction.</summary>
        Wheel,

        /// <summary>Omni wheel: free rollers at 90° to the hub let it slide along its axle.</summary>
        OmniWheel,

        /// <summary>Mecanum wheel: free rollers at 45° — it only pushes along the roller axis.</summary>
        MecanumWheel,

        /// <summary>Steel ball in a housing; rolls in every direction.</summary>
        BallCaster,

        /// <summary>Hobby propeller (thrust and drag torque from its coefficients).</summary>
        Propeller
    }

    /// <summary>
    /// A ready-made mechanical part with real dimensions, mass and (for propellers) static
    /// aerodynamic coefficients. Sizes are in metres, masses in kilograms.
    /// </summary>
    public sealed class ReadyPartSpec
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public ReadyPartKind Kind { get; init; }

        /// <summary>Tyre outer diameter / propeller diameter / caster ball diameter.</summary>
        public double Diameter { get; init; }

        /// <summary>Tyre width / propeller hub height / caster total height.</summary>
        public double Width { get; init; }

        public double MassKg { get; init; }

        /// <summary>Shaft diameter the hub fits (m).</summary>
        public double Bore { get; init; }

        /// <summary>Shaft fit in words ("TT çift-D 5.4 × 3.7 mm").</summary>
        public string BoreText { get; init; } = string.Empty;

        /// <summary>World material of the tread / body (density and friction).</summary>
        public string Material { get; init; } = "KAUÇUK";

        /// <summary>Hub / blade colour "#RRGGBB".</summary>
        public string Color { get; init; } = "#2A2A2E";

        /// <summary>Model style: "tt", "small", "n20", "offroad", "omni", "mecanum", "caster", "prop".</summary>
        public string Style { get; init; } = "tt";

        /// <summary>
        /// Distance from the shaft tip to the wheel's centre plane along the shaft (positive = the
        /// centre lies beyond the tip). For propellers: how far the shaft tip sits below the hub top.
        /// </summary>
        public double MountOffset { get; init; }

        // ---- rollers
        public int Rollers { get; init; }

        /// <summary>Angle between the roller axes and the hub axle (90° omni, 45° mecanum).</summary>
        public double RollerAngleDeg { get; init; }

        /// <summary>
        /// Mecanum: +1 = "A" (rollers lean like "\" seen from above at the contact), −1 = "B".
        /// Propeller: +1 = CCW (thrust along +axis when spinning counter-clockwise seen from the thrust side),
        /// −1 = CW ("R" props, pushing the same way when spinning clockwise).
        /// </summary>
        public int Hand { get; init; } = 1;

        // ---- caster
        /// <summary>Housing diameter of a ball caster (m).</summary>
        public double HousingDiameter { get; init; }

        // ---- propeller
        public int Blades { get; init; }

        /// <summary>Geometric pitch (m; 4.5" = 0.1143 m).</summary>
        public double Pitch { get; init; }

        /// <summary>Static thrust coefficient C_T in T = C_T·ρ·n²·D⁴ (n in rev/s).</summary>
        public double ThrustCoefficient { get; init; }

        /// <summary>Static power coefficient C_P in P = C_P·ρ·n³·D⁵.</summary>
        public double PowerCoefficient { get; init; }

        /// <summary>Torque coefficient C_Q = C_P / 2π in Q = C_Q·ρ·n²·D⁵.</summary>
        public double TorqueCoefficient => PowerCoefficient / (2 * Math.PI);

        public double HubDiameter { get; init; }

        public bool IsWheel => Kind == ReadyPartKind.Wheel || Kind == ReadyPartKind.OmniWheel || Kind == ReadyPartKind.MecanumWheel;

        /// <summary>Mounted on a shaft (wheels and propellers; not the ball caster).</summary>
        public bool MountsOnShaft => IsWheel || Kind == ReadyPartKind.Propeller;

        public double Radius => Diameter / 2;

        /// <summary>Spin inertia about the hub axis (kg·m²): tyre-heavy wheels, tapered blades.</summary>
        public double AxialInertia => Kind switch
        {
            ReadyPartKind.Propeller => MassKg * Diameter * Diameter / (Blades >= 3 ? 13.0 : 15.0),
            ReadyPartKind.BallCaster => 0.4 * MassKg * Radius * Radius,
            _ => 0.75 * MassKg * Radius * Radius
        };
    }

    /// <summary>
    /// Static propeller aerodynamics (momentum-blade-element coefficients measured on a thrust
    /// stand): T = C_T·ρ·n²·D⁴ and Q = C_Q·ρ·n²·D⁵ with n in revolutions per second.
    /// </summary>
    public static class PropellerAero
    {
        public const double SeaLevelDensity = 1.225;

        /// <summary>Thrust (N) at a shaft speed in rpm.</summary>
        public static double Thrust(ReadyPartSpec p, double rpm, double rho = SeaLevelDensity)
        {
            double n = rpm / 60.0;
            return p.ThrustCoefficient * rho * n * Math.Abs(n) * Math.Pow(p.Diameter, 4);
        }

        /// <summary>Aerodynamic drag torque on the shaft (N·m) at a speed in rpm.</summary>
        public static double Torque(ReadyPartSpec p, double rpm, double rho = SeaLevelDensity)
        {
            double n = rpm / 60.0;
            return p.TorqueCoefficient * rho * n * Math.Abs(n) * Math.Pow(p.Diameter, 5);
        }

        /// <summary>Shaft power (W) at a speed in rpm.</summary>
        public static double Power(ReadyPartSpec p, double rpm, double rho = SeaLevelDensity) =>
            Math.Abs(Torque(p, rpm, rho) * rpm * 2 * Math.PI / 60.0);

        /// <summary>Thrust per (rad/s)² at sea level: k_T = C_T·ρ·D⁴ / 4π².</summary>
        public static double ThrustPerOmega2(ReadyPartSpec p, double rho = SeaLevelDensity) =>
            p.ThrustCoefficient * rho * Math.Pow(p.Diameter, 4) / (4 * Math.PI * Math.PI);

        /// <summary>Torque per (rad/s)² at sea level: k_Q = C_Q·ρ·D⁵ / 4π².</summary>
        public static double TorquePerOmega2(ReadyPartSpec p, double rho = SeaLevelDensity) =>
            p.TorqueCoefficient * rho * Math.Pow(p.Diameter, 5) / (4 * Math.PI * Math.PI);

        /// <summary>Rpm at which a propeller produces a given thrust (N).</summary>
        public static double RpmForThrust(ReadyPartSpec p, double thrust, double rho = SeaLevelDensity) =>
            60.0 * Math.Sqrt(Math.Max(0, thrust) / (p.ThrustCoefficient * rho * Math.Pow(p.Diameter, 4)));
    }

    /// <summary>
    /// Catalogue of ready-made wheels, casters and propellers, placed in a world as
    /// <see cref="WorldObjectKind.Body"/> objects with <see cref="WorldObject.Shape"/> = "ready" and
    /// <see cref="WorldObject.PresetId"/> = the part id. Frames: wheels have their axle along
    /// local Y and rest on z = 0 (centre at z = radius); propellers spin about local Z with the
    /// hub bottom at the origin; a ball caster stands on its ball with the mounting plate on top.
    /// </summary>
    public static class ReadyParts
    {
        public const string Shape = "ready";
        public const string GroupWheels = "Hazır tekerlekler";
        public const string GroupProps = "Pervaneler";

        private const string TtBore = "TT çift-D mil (Ø5.4 × 3.7 mm)";
        private const string D3Bore = "3 mm D-mil (N20)";

        public static IReadOnlyList<ReadyPartSpec> All { get; } = Catalogue();

        private static List<ReadyPartSpec> Catalogue()
        {
            var list = new List<ReadyPartSpec>
            {
            new ReadyPartSpec
            {
                Id = "wheel-tt-65", Name = "TT tekerlek 65 × 26 mm", Kind = ReadyPartKind.Wheel, Style = "tt",
                Description = "Sarı göbek, tırtıklı kauçuk lastik · 28 g · TT motor miline geçer",
                Diameter = 0.065, Width = 0.026, MassKg = 0.028, Bore = 0.0054, BoreText = TtBore, Color = "#F2C230", MountOffset = 0.0065
            },
            new ReadyPartSpec
            {
                Id = "wheel-43", Name = "Küçük tekerlek 43 × 19 mm", Kind = ReadyPartKind.Wheel, Style = "small",
                Description = "Siyah göbek, yumuşak kauçuk · 10 g · N20 / 3 mm D-mil",
                Diameter = 0.043, Width = 0.019, MassKg = 0.010, Bore = 0.003, BoreText = D3Bore, Color = "#2A2A2E", MountOffset = 0.0035
            },
            new ReadyPartSpec
            {
                Id = "wheel-n20-34", Name = "N20 tekerlek 34 × 7 mm", Kind = ReadyPartKind.Wheel, Style = "n20",
                Description = "Beyaz göbekli ince mini robot tekerleği · 4 g · 3 mm D-mil",
                Diameter = 0.034, Width = 0.007, MassKg = 0.004, Bore = 0.003, BoreText = D3Bore, Color = "#EDEDE8", MountOffset = -0.003
            },
            new ReadyPartSpec
            {
                Id = "wheel-offroad-85", Name = "Arazi lastiği 85 × 35 mm", Kind = ReadyPartKind.Wheel, Style = "offroad",
                Description = "Kabaralı yumuşak lastik, 12 mm altıgen göbek + 6 mm kaplin · 58 g",
                Diameter = 0.085, Width = 0.035, MassKg = 0.058, Bore = 0.006, BoreText = "12 mm altıgen göbek, 6 mm D-mil kaplini", Color = "#C8CCD2", MountOffset = 0.0075
            },
            new ReadyPartSpec
            {
                Id = "omni-60", Name = "Omni tekerlek 60 mm (çift sıra)", Kind = ReadyPartKind.OmniWheel, Style = "omni",
                Description = "2 × 9 serbest makara · ekseni boyunca serbestçe kayar · 34 g",
                Diameter = 0.060, Width = 0.025, MassKg = 0.034, Bore = 0.0054, BoreText = TtBore + " adaptörlü", Color = "#3A3A40",
                MountOffset = 0.0045, Rollers = 18, RollerAngleDeg = 90
            },
            new ReadyPartSpec
            {
                Id = "mecanum-65-a", Name = "Mekanum tekerlek 65 mm (A)", Kind = ReadyPartKind.MecanumWheel, Style = "mecanum",
                Description = "10 makara 45° eğik · A tipi (sol ön / sağ arka) · 45 g",
                Diameter = 0.065, Width = 0.032, MassKg = 0.045, Bore = 0.0054, BoreText = TtBore, Color = "#D9DCE0",
                MountOffset = 0.008, Rollers = 10, RollerAngleDeg = 45, Hand = 1
            },
            new ReadyPartSpec
            {
                Id = "mecanum-65-b", Name = "Mekanum tekerlek 65 mm (B)", Kind = ReadyPartKind.MecanumWheel, Style = "mecanum",
                Description = "10 makara 45° eğik · B tipi (sağ ön / sol arka) · 45 g",
                Diameter = 0.065, Width = 0.032, MassKg = 0.045, Bore = 0.0054, BoreText = TtBore, Color = "#D9DCE0",
                MountOffset = 0.008, Rollers = 10, RollerAngleDeg = 45, Hand = -1
            },
            new ReadyPartSpec
            {
                Id = "caster-15", Name = "Bilyeli sarhoş teker Ø15 mm", Kind = ReadyPartKind.BallCaster, Style = "caster",
                Description = "Çelik bilye plastik yuvada, her yöne döner · 20 mm yükseklik · 19 g",
                Diameter = 0.015, Width = 0.020, HousingDiameter = 0.026, MassKg = 0.019, Material = "ÇELİK", Color = "#2A2A2E"
            },

            };

            // Propellers. Coefficients: static thrust-stand values for small fixed-pitch props
            // (UIUC propeller database range; micro props lose C_T to low Reynolds numbers).
            list.AddRange(Prop("prop-55", "55 mm 2 kanat", "8520 / 0720 coreless motor için, 1 mm mile geçme", 0.055, 0.030, 2, 0.00045, 0.10, 0.057, 0.0042, 0.0050, 0.0008, "#F2F2EE", "#1E1E22"));
            list.AddRange(Prop("prop-65", "65 mm 2 kanat", "8520 coreless motor için büyük pervane", 0.065, 0.036, 2, 0.00075, 0.105, 0.060, 0.0045, 0.0055, 0.0008, "#F2F2EE", "#1E1E22"));
            list.AddRange(Prop("prop-5045", "5045 (5 × 4.5\") 2 kanat", "130 motor / fırçasız 2204 için, 5 mm mil", 0.127, 0.1143, 2, 0.0034, 0.115, 0.055, 0.012, 0.0070, 0.005, "#F28C28", "#1E1E22"));
            list.AddRange(Prop("prop-5040x3", "5040 (5 × 4\") 3 kanat", "Yarış dronu pervanesi, 5 mm mil", 0.127, 0.1016, 3, 0.0042, 0.140, 0.072, 0.013, 0.0075, 0.005, "#2FC2B0", "#E5484D"));
            return list;
        }

        private static IEnumerable<ReadyPartSpec> Prop(string id, string size, string text, double d, double pitch, int blades, double mass,
            double ct, double cp, double hubD, double hubH, double bore, string colourCcw, string colourCw)
        {
            foreach (int hand in new[] { 1, -1 })
            {
                yield return new ReadyPartSpec
                {
                    Id = id + (hand > 0 ? "-ccw" : "-cw"),
                    Name = "Pervane " + size + (hand > 0 ? " (CCW)" : " (CW, R)"),
                    Kind = ReadyPartKind.Propeller, Style = "prop",
                    Description = text + " · " + (mass * 1000).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " g · " +
                                  (hand > 0 ? "saat yönünün tersine döner" : "saat yönünde döner"),
                    Diameter = d, Width = hubH, Pitch = pitch, Blades = blades, MassKg = mass, ThrustCoefficient = ct, PowerCoefficient = cp,
                    HubDiameter = hubD, Bore = bore, BoreText = (bore * 1000).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " mm mil",
                    Material = "ABS", Color = hand > 0 ? colourCcw : colourCw, Hand = hand, MountOffset = 0.0005
                };
            }
        }

        public static ReadyPartSpec? Find(string? id) =>
            id == null ? null : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The ready part a world object is, or null.</summary>
        public static ReadyPartSpec? Of(WorldObject? o) =>
            o != null && o.Kind == WorldObjectKind.Body && string.Equals(o.Shape, Shape, StringComparison.OrdinalIgnoreCase) ? Find(o.PresetId) : null;

        /// <summary>Adds a ready part with its catalogue size, mass and material.</summary>
        public static WorldObject Add(WorldDocument world, string id, WorldPose pose)
        {
            var spec = Find(id) ?? throw new ArgumentException("Bilinmeyen hazır parça: " + id, nameof(id));
            var o = new WorldObject
            {
                Id = world.NextId("O"),
                Name = world.UniqueName(ShortName(spec)),
                Kind = WorldObjectKind.Body,
                Shape = Shape,
                PresetId = spec.Id,
                SizeX = spec.Diameter,
                SizeY = spec.Kind == ReadyPartKind.BallCaster ? spec.HousingDiameter : spec.Diameter,
                SizeZ = spec.Width,
                MassKg = spec.MassKg,
                Material = spec.Material,
                Color = spec.Color,
                Pose = pose
            };
            world.Objects.Add(o);
            return o;
        }

        private static string ShortName(ReadyPartSpec s) => s.Kind switch
        {
            ReadyPartKind.Propeller => "Pervane",
            ReadyPartKind.OmniWheel => "Omni tekerlek",
            ReadyPartKind.MecanumWheel => s.Hand > 0 ? "Mekanum A" : "Mekanum B",
            ReadyPartKind.BallCaster => "Sarhoş teker",
            _ => "Tekerlek"
        };

        /// <summary>Physical body of a ready part in its own frame (for <see cref="WorldPhysics"/>).</summary>
        public static WorldBodyGeometry? Geometry(WorldObject o)
        {
            var s = Of(o);
            if (s == null) return null;
            double r = s.Radius;
            switch (s.Kind)
            {
                case ReadyPartKind.Propeller:
                    return new WorldBodyGeometry { Min = new Vec3(-r, -r, 0), Max = new Vec3(r, r, s.Width), MassKg = s.MassKg, Shape = "prop-z" };
                case ReadyPartKind.BallCaster:
                    double h = s.HousingDiameter / 2;
                    return new WorldBodyGeometry { Min = new Vec3(-h, -h, 0), Max = new Vec3(h, h, s.Width), MassKg = s.MassKg, Shape = "caster" };
                default:
                    return new WorldBodyGeometry
                    {
                        Min = new Vec3(-r, -s.Width / 2, 0), Max = new Vec3(r, s.Width / 2, 2 * r), MassKg = s.MassKg,
                        Shape = s.Kind == ReadyPartKind.OmniWheel ? "omni-y" : s.Kind == ReadyPartKind.MecanumWheel ? "mecanum-y" : "cylinder-y"
                    };
            }
        }

        /// <summary>Hub axis of a shaft-mounted part in its own frame (Y for wheels, Z for propellers).</summary>
        public static Vec3 HubAxis(ReadyPartSpec s) => s.Kind == ReadyPartKind.Propeller ? Vec3.UnitZ : Vec3.UnitY;

        /// <summary>
        /// Pose that puts a shaft-mounted part on a shaft: its hub axis along <paramref name="axis"/>
        /// (pointing out of the motor) and the hub at the right depth on the shaft whose end is
        /// <paramref name="tip"/> (world coordinates).
        /// </summary>
        public static WorldPose SnapPose(ReadyPartSpec s, Vec3 tip, Vec3 axis)
        {
            var a = axis.Normalized();
            if (a.LengthSquared < 1e-12) a = Vec3.UnitZ;
            double roll, yaw;
            Vec3 hubLocal;
            Vec3 hubWorld;
            if (s.Kind == ReadyPartKind.Propeller)
            {
                // Local Z → a: q = Rz(yaw)·Rx(roll), Rx maps Z to (0, −sin r, cos r).
                double sr = Math.Sqrt(Math.Max(0, 1 - a.Z * a.Z));
                roll = Math.Atan2(sr, a.Z);
                yaw = sr > 1e-9 ? Math.Atan2(a.X, -a.Y) : 0;
                hubLocal = Vec3.Zero;
                // The shaft passes through the hub; its tip sits just under the hub top.
                hubWorld = tip - a * (s.Width - s.MountOffset);
            }
            else
            {
                // Local Y → a: q = Rz(yaw)·Rx(roll), Rx maps Y to (0, cos r, sin r).
                roll = Math.Asin(Math.Max(-1, Math.Min(1, a.Z)));
                double c = Math.Sqrt(Math.Max(0, 1 - a.Z * a.Z));
                yaw = c > 1e-9 ? Math.Atan2(-a.X, a.Y) : 0;
                hubLocal = new Vec3(0, 0, s.Radius);
                hubWorld = tip + a * s.MountOffset;
            }

            var pose = new WorldPose { Roll = roll * 180 / Math.PI, Yaw = yaw * 180 / Math.PI };
            var p = hubWorld - pose.Orientation.Rotate(hubLocal);
            pose.X = p.X;
            pose.Y = p.Y;
            pose.Z = p.Z;
            return pose;
        }

        /// <summary>
        /// Mounts a shaft-mounted part on a port of another object: moves it onto the shaft and adds the
        /// fixed joint (replacing any earlier mount of the part). <paramref name="pivot"/>/<paramref name="axis"/>
        /// are the port in the parent's frame.
        /// </summary>
        public static WorldJoint? AttachToPort(WorldDocument world, WorldObject part, WorldObject parent, string port, Vec3 pivot, Vec3 axis)
        {
            var s = Of(part);
            if (s == null || !s.MountsOnShaft) return null;
            var tip = parent.Pose.ToWorld(pivot);
            var dir = parent.Pose.Orientation.Rotate(axis).Normalized();
            var pose = SnapPose(s, tip, dir);
            // Whatever is mounted on the part comes along.
            world.MoveAssembly(part, pose.X - part.Pose.X, pose.Y - part.Pose.Y, pose.Z - part.Pose.Z);
            part.Pose.Roll = pose.Roll;
            part.Pose.Pitch = pose.Pitch;
            part.Pose.Yaw = pose.Yaw;
            world.Joints.RemoveAll(j => j.ObjectA == part.Id && j.Kind == WorldJointKind.Fixed);
            var joint = new WorldJoint
            {
                Id = world.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = part.Id, ObjectB = parent.Id, PortB = port,
                AnchorX = tip.X, AnchorY = tip.Y, AnchorZ = tip.Z, AxisX = dir.X, AxisY = dir.Y, AxisZ = dir.Z
            };
            world.Joints.Add(joint);
            return joint;
        }
    }
}
