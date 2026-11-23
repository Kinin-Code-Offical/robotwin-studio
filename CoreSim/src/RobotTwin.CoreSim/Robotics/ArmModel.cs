using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    public enum ArmJointType
    {
        Revolute,
        Prismatic
    }

    /// <summary>
    /// One axis of a serial arm, described in the arm's zero configuration in the base frame
    /// (product-of-exponentials form): the joint turns about <see cref="Axis"/> through
    /// <see cref="Point"/> (or slides along it). Angles in radians, travel in metres.
    /// </summary>
    public sealed class ArmJointSpec
    {
        public string Name { get; set; } = string.Empty;
        public ArmJointType Type { get; set; } = ArmJointType.Revolute;
        public Vec3 Axis { get; set; } = Vec3.UnitZ;
        public Vec3 Point { get; set; }

        /// <summary>Travel limits (rad or m).</summary>
        public double Min { get; set; } = -Math.PI;
        public double Max { get; set; } = Math.PI;

        /// <summary>Rated speed (rad/s or m/s) and acceleration (rad/s² or m/s²).</summary>
        public double MaxSpeed { get; set; } = Math.PI;
        public double MaxAccel { get; set; } = 4 * Math.PI;

        /// <summary>Peak torque of the drive at the joint output (N·m), or force for a prismatic axis (N).</summary>
        public double MaxTorque { get; set; } = 100;

        /// <summary>Coulomb friction of gearbox and bearings (N·m or N).</summary>
        public double Friction { get; set; }

        /// <summary>Position error that trips a following-error fault (rad or m).</summary>
        public double TrackTolerance { get; set; } = 0.05;

        public bool IsPrismatic => Type == ArmJointType.Prismatic;

        /// <summary>Display value: degrees for revolute joints, millimetres for prismatic ones.</summary>
        public double ToDisplay(double q) => IsPrismatic ? q * 1000 : q * 180 / Math.PI;

        public double FromDisplay(double v) => IsPrismatic ? v / 1000 : v * Math.PI / 180;

        public string Unit => IsPrismatic ? "mm" : "°";
    }

    /// <summary>Rigid link carried by a joint: a box/cylinder for collision and mass (zero configuration, base frame).</summary>
    public sealed class ArmLinkSpec
    {
        public string Name { get; set; } = string.Empty;
        public Vec3 Center { get; set; }
        public Vec3 Size { get; set; } = new Vec3(0.05, 0.05, 0.05);
        public double Mass { get; set; } = 1;

        /// <summary>"box" or "cylinder-x/y/z".</summary>
        public string Shape { get; set; } = "box";
    }

    public enum ArmIkKind
    {
        /// <summary>Damped least squares only.</summary>
        Numeric,

        /// <summary>6R with J1 vertical, J2/J3 parallel, spherical wrist (J4 ⟂ J5 ⟂ J6 meet in the wrist centre).</summary>
        SphericalWrist6,

        /// <summary>RRPR SCARA: two vertical revolute axes, vertical quill, tool roll.</summary>
        Scara4
    }

    /// <summary>
    /// A serial robot arm: axes, links, flange and the geometry its closed-form inverse
    /// kinematics needs. The base frame sits on the mounting face, Z up, X forward.
    /// </summary>
    public sealed class ArmModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ArmJointSpec> Joints { get; } = new List<ArmJointSpec>();

        /// <summary>Link moved by each joint (same count as <see cref="Joints"/>).</summary>
        public List<ArmLinkSpec> Links { get; } = new List<ArmLinkSpec>();

        /// <summary>Fixed base housing (bolted to the floor/pedestal).</summary>
        public ArmLinkSpec Base { get; set; } = new ArmLinkSpec();

        /// <summary>Flange frame in the zero configuration; its +Z is the tool approach direction.</summary>
        public Transform3 Flange { get; set; } = Transform3.Identity;

        public ArmIkKind Ik { get; set; } = ArmIkKind.Numeric;

        /// <summary>Spherical-wrist 6R geometry (m): J2 height, J2 forward offset, upper arm, forearm offset, forearm length, wrist→flange.</summary>
        public double D1 { get; set; }
        public double A1 { get; set; }
        public double A2 { get; set; }
        public double A3 { get; set; }
        public double D4 { get; set; }
        public double D6 { get; set; }

        /// <summary>Rated payload at the flange (kg) and maximum reach of the wrist centre (m).</summary>
        public double RatedPayload { get; set; }
        public double Reach { get; set; }

        /// <summary>Position repeatability (m), informational.</summary>
        public double Repeatability { get; set; }

        /// <summary>Rest/home pose (rad/m).</summary>
        public double[] Home { get; set; } = Array.Empty<double>();

        /// <summary>Linear TCP speed limit (m/s) and rotational limit (rad/s) for linear moves.</summary>
        public double MaxLinearSpeed { get; set; } = 2.0;
        public double MaxLinearAccel { get; set; } = 8.0;
        public double MaxRotSpeed { get; set; } = Math.PI;

        /// <summary>True when the arm is driven by hobby servos (shown differently in the pendant).</summary>
        public bool ServoDriven { get; set; }

        public int Dof => Joints.Count;

        public double TotalMovingMass => Links.Sum(l => l.Mass);

        public bool WithinLimits(IReadOnlyList<double> q, double margin = 1e-9)
        {
            for (int i = 0; i < Joints.Count; i++)
            {
                if (q[i] < Joints[i].Min - margin || q[i] > Joints[i].Max + margin) return false;
            }

            return true;
        }

        public double[] Clamp(IReadOnlyList<double> q)
        {
            var r = new double[Joints.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Math.Max(Joints[i].Min, Math.Min(Joints[i].Max, q[i]));
            return r;
        }
    }

    /// <summary>Built-in arm models (generic designs with realistic proportions and drive ratings).</summary>
    public static class ArmCatalog
    {
        public const string Industrial6 = "RW6-850";
        public const string Scara = "RWS-500";
        public const string Desktop6 = "RWD-6";

        private static double D(double deg) => deg * Math.PI / 180;

        private static readonly Dictionary<string, Func<ArmModel>> Builders = new Dictionary<string, Func<ArmModel>>(StringComparer.OrdinalIgnoreCase)
        {
            [Industrial6] = BuildIndustrial6,
            [Scara] = BuildScara,
            [Desktop6] = BuildDesktop6
        };

        public static IReadOnlyCollection<string> Ids => Builders.Keys;

        private static readonly Dictionary<string, ArmModel> Cache = new Dictionary<string, ArmModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Model by id (shared instance; do not modify). Unknown ids give the 6-axis arm.</summary>
        public static ArmModel Get(string? id)
        {
            string key = id != null && Builders.ContainsKey(id) ? id : Industrial6;
            lock (Cache)
            {
                if (!Cache.TryGetValue(key, out var m))
                {
                    m = Builders[key]();
                    Cache[key] = m;
                }

                return m;
            }
        }

        private static ArmJointSpec R(string name, Vec3 axis, Vec3 point, double min, double max, double speed, double accel, double torque, double friction, double tol) =>
            new ArmJointSpec
            {
                Name = name, Type = ArmJointType.Revolute, Axis = axis, Point = point, Min = D(min), Max = D(max), MaxSpeed = D(speed), MaxAccel = D(accel),
                MaxTorque = torque, Friction = friction, TrackTolerance = D(tol)
            };

        private static ArmLinkSpec L(string name, double cx, double cy, double cz, double sx, double sy, double sz, double mass, string shape = "box") =>
            new ArmLinkSpec { Name = name, Center = new Vec3(cx, cy, cz), Size = new Vec3(sx, sy, sz), Mass = mass, Shape = shape };

        /// <summary>
        /// 6-axis articulated arm, ~5 kg payload, 850 mm wrist reach (compact assembly/handling class).
        /// Zero pose: upper arm vertical, forearm horizontal pointing +X, flange facing +X.
        /// </summary>
        private static ArmModel BuildIndustrial6()
        {
            const double d1 = 0.330, a1 = 0.075, a2 = 0.365, a3 = 0.035, d4 = 0.405, d6 = 0.080;
            double zf = d1 + a2 + a3;
            var m = new ArmModel
            {
                Id = Industrial6,
                Name = "RW6-850 · 6 eksenli endüstriyel kol",
                Description = "6 eksen, 5 kg yük, 850 mm erişim, ±0.03 mm tekrarlanabilirlik, küresel bilek",
                Ik = ArmIkKind.SphericalWrist6,
                D1 = d1, A1 = a1, A2 = a2, A3 = a3, D4 = d4, D6 = d6,
                RatedPayload = 5.0,
                Reach = a1 + a2 + Math.Sqrt(d4 * d4 + a3 * a3),
                Repeatability = 0.00003,
                MaxLinearSpeed = 2.0,
                MaxLinearAccel = 10.0,
                MaxRotSpeed = D(360),
                Flange = new Transform3(new Vec3(a1 + d4 + d6, 0, zf), Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2)),
                Home = new[] { 0.0, D(-10), D(25), 0.0, D(75), 0.0 },
                Base = L("Taban", 0, 0, 0.085, 0.27, 0.27, 0.17, 12, "cylinder-z")
            };
            m.Joints.Add(R("J1", Vec3.UnitZ, Vec3.Zero, -170, 170, 370, 1200, 220, 4, 2.5));
            m.Joints.Add(R("J2", Vec3.UnitY, new Vec3(a1, 0, d1), -95, 135, 310, 1000, 320, 5, 2.5));
            m.Joints.Add(R("J3", Vec3.UnitY, new Vec3(a1, 0, d1 + a2), -150, 120, 410, 1300, 160, 3, 2.5));
            m.Joints.Add(R("J4", Vec3.UnitX, new Vec3(a1, 0, zf), -190, 190, 550, 2500, 30, 0.6, 3));
            m.Joints.Add(R("J5", Vec3.UnitY, new Vec3(a1 + d4, 0, zf), -125, 125, 545, 2500, 30, 0.6, 3));
            m.Joints.Add(R("J6", Vec3.UnitX, new Vec3(a1 + d4, 0, zf), -360, 360, 1000, 4000, 15, 0.3, 3));
            m.Links.Add(L("Döner gövde", 0.02, 0, 0.255, 0.22, 0.22, 0.17, 7.0));
            m.Links.Add(L("Üst kol", a1, -0.005, d1 + a2 / 2, 0.11, 0.14, a2 + 0.08, 5.5));
            m.Links.Add(L("Dirsek", a1 + 0.03, 0, d1 + a2 + 0.02, 0.17, 0.13, 0.13, 3.5));
            m.Links.Add(L("Ön kol", a1 + 0.235, 0, zf, 0.25, 0.085, 0.085, 1.6, "cylinder-x"));
            m.Links.Add(L("Bilek", a1 + d4, 0, zf, 0.09, 0.085, 0.085, 0.8));
            m.Links.Add(L("Flanş", a1 + d4 + d6 - 0.012, 0, zf, 0.024, 0.063, 0.063, 0.2, "cylinder-x"));
            return m;
        }

        /// <summary>4-axis SCARA, 500 mm reach (250 + 250), 150 mm stroke, 3 kg payload.</summary>
        private static ArmModel BuildScara()
        {
            const double a1 = 0.25, a2 = 0.25, hTop = 0.40;
            var m = new ArmModel
            {
                Id = Scara,
                Name = "RWS-500 · 4 eksenli SCARA",
                Description = "SCARA, 3 kg yük, 500 mm erişim, 150 mm Z stroku, ±0.01 mm tekrarlanabilirlik",
                Ik = ArmIkKind.Scara4,
                A1 = a1, A2 = a2,
                RatedPayload = 3.0,
                Reach = a1 + a2,
                Repeatability = 0.00001,
                MaxLinearSpeed = 3.0,
                MaxLinearAccel = 15.0,
                MaxRotSpeed = D(720),
                Flange = new Transform3(new Vec3(a1 + a2, 0, hTop - 0.22), Quat.FromAxisAngle(Vec3.UnitX, Math.PI)),
                Home = new[] { D(30), D(-60), 0.02, 0.0 },
                Base = L("Taban", 0, 0, 0.17, 0.20, 0.24, 0.34, 9)
            };
            m.Joints.Add(R("J1", Vec3.UnitZ, Vec3.Zero, -130, 130, 420, 3000, 60, 1.0, 2));
            m.Joints.Add(R("J2", Vec3.UnitZ, new Vec3(a1, 0, 0), -145, 145, 720, 4000, 30, 0.6, 2));
            m.Joints.Add(new ArmJointSpec
            {
                Name = "J3", Type = ArmJointType.Prismatic, Axis = new Vec3(0, 0, -1), Point = new Vec3(a1 + a2, 0, 0), Min = 0, Max = 0.150,
                MaxSpeed = 1.1, MaxAccel = 6, MaxTorque = 180, Friction = 3, TrackTolerance = 0.004
            });
            m.Joints.Add(R("J4", Vec3.UnitZ, new Vec3(a1 + a2, 0, 0), -360, 360, 2500, 10000, 4, 0.05, 4));
            m.Links.Add(L("1. kol", a1 / 2 + 0.01, 0, hTop + 0.035, a1 + 0.14, 0.13, 0.07, 4.5));
            m.Links.Add(L("2. kol", a1 + a2 / 2 + 0.02, 0, hTop + 0.06, a2 + 0.12, 0.11, 0.09, 3.5));
            m.Links.Add(L("Mil", a1 + a2, 0, hTop - 0.08, 0.02, 0.02, 0.28, 0.6, "cylinder-z"));
            m.Links.Add(L("Flanş", a1 + a2, 0, hTop - 0.214, 0.05, 0.05, 0.012, 0.15, "cylinder-z"));   // its face on the flange frame, where the tool sits
            return m;
        }

        /// <summary>Desktop 6-DOF servo arm (hobby/education): metal-gear servos, ~200 mm reach, 150 g payload.</summary>
        private static ArmModel BuildDesktop6()
        {
            const double d1 = 0.072, a2 = 0.105, d4 = 0.098, d6 = 0.035;
            double zf = d1 + a2;
            var m = new ArmModel
            {
                Id = Desktop6,
                Name = "RWD-6 · masaüstü 6 eksen servo kol",
                Description = "Metal dişli servolar (J1–J3 ~1.3 N·m, bilek ~0.25 N·m), 150 g yük, ~200 mm erişim",
                Ik = ArmIkKind.SphericalWrist6,
                D1 = d1, A1 = 0, A2 = a2, A3 = 0, D4 = d4, D6 = d6,
                RatedPayload = 0.15,
                Reach = a2 + d4,
                Repeatability = 0.002,
                ServoDriven = true,
                MaxLinearSpeed = 0.25,
                MaxLinearAccel = 1.5,
                MaxRotSpeed = D(180),
                Flange = new Transform3(new Vec3(d4 + d6, 0, zf), Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2)),
                Home = new[] { 0.0, D(-15), D(40), 0.0, D(60), 0.0 },
                Base = L("Taban", 0, 0, 0.025, 0.12, 0.12, 0.05, 0.35, "cylinder-z")
            };
            m.Joints.Add(R("J1", Vec3.UnitZ, Vec3.Zero, -90, 90, 300, 1500, 1.3, 0.02, 4));
            m.Joints.Add(R("J2", Vec3.UnitY, new Vec3(0, 0, d1), -90, 90, 300, 1500, 1.3, 0.02, 4));
            m.Joints.Add(R("J3", Vec3.UnitY, new Vec3(0, 0, zf), -90, 120, 300, 1500, 1.3, 0.02, 4));
            m.Joints.Add(R("J4", Vec3.UnitX, new Vec3(0, 0, zf), -90, 90, 450, 2500, 0.25, 0.005, 5));
            m.Joints.Add(R("J5", Vec3.UnitY, new Vec3(d4, 0, zf), -90, 90, 450, 2500, 0.25, 0.005, 5));
            m.Joints.Add(R("J6", Vec3.UnitX, new Vec3(d4, 0, zf), -90, 90, 450, 2500, 0.25, 0.005, 5));
            m.Links.Add(L("Döner tabla", 0, 0, d1 - 0.012, 0.06, 0.06, 0.03, 0.09, "cylinder-z"));
            m.Links.Add(L("Omuz kolu", 0, 0, d1 + a2 / 2, 0.028, 0.045, a2 + 0.02, 0.07));
            m.Links.Add(L("Dirsek", 0.025, 0, zf, 0.06, 0.04, 0.035, 0.05));
            m.Links.Add(L("Ön kol", 0.07, 0, zf, 0.05, 0.03, 0.03, 0.03));
            m.Links.Add(L("Bilek", d4, 0, zf, 0.03, 0.035, 0.03, 0.025));
            m.Links.Add(L("Flanş", d4 + d6 - 0.005, 0, zf, 0.01, 0.03, 0.03, 0.008, "cylinder-x"));
            return m;
        }
    }
}
