using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>[urdf] A pose in URDF: <c>xyz</c> (m) and <c>rpy</c> (rad, R = Rz(yaw)·Ry(pitch)·Rx(roll)).</summary>
    public readonly struct UrdfPose
    {
        public UrdfPose(Vec3 xyz, Vec3 rpy)
        {
            Xyz = xyz;
            Rpy = rpy;
        }

        public Vec3 Xyz { get; }
        public Vec3 Rpy { get; }

        public Transform3 ToTransform() => Transform3.FromRpy(Xyz.X, Xyz.Y, Xyz.Z, Rpy.X, Rpy.Y, Rpy.Z);

        public static readonly UrdfPose Identity = new UrdfPose(Vec3.Zero, Vec3.Zero);
    }

    public enum UrdfGeometryKind
    {
        Box,
        Cylinder,
        Sphere,
        Mesh
    }

    /// <summary>[urdf] A visual or collision shape: a box, a cylinder along its Z, a sphere or a mesh file.</summary>
    public sealed class UrdfGeometry
    {
        public UrdfGeometryKind Kind { get; set; }
        public Vec3 Size { get; set; }
        public double Radius { get; set; }
        public double Length { get; set; }

        /// <summary>The mesh file as written (<c>package://…</c>, <c>file://…</c> or a path).</summary>
        public string? Filename { get; set; }

        public Vec3 Scale { get; set; } = new Vec3(1, 1, 1);
    }

    public sealed class UrdfShape
    {
        public UrdfPose Origin { get; set; } = UrdfPose.Identity;
        public UrdfGeometry Geometry { get; set; } = new UrdfGeometry();
        public string? MaterialName { get; set; }

        /// <summary>Colour given inline or by the named material (r, g, b, a in 0…1), when there is one.</summary>
        public double[]? Rgba { get; set; }
    }

    public sealed class UrdfLink
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Mass (kg); 0 when the link has no inertial element.</summary>
        public double Mass { get; set; }

        public UrdfPose InertialOrigin { get; set; } = UrdfPose.Identity;
        public List<UrdfShape> Visuals { get; } = new List<UrdfShape>();
        public List<UrdfShape> Collisions { get; } = new List<UrdfShape>();

        public bool HasGeometry => Visuals.Count > 0 || Collisions.Count > 0;
    }

    public sealed class UrdfJoint
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>revolute, continuous, prismatic, fixed, floating or planar.</summary>
        public string Type { get; set; } = "fixed";

        public string Parent { get; set; } = string.Empty;
        public string Child { get; set; } = string.Empty;
        public UrdfPose Origin { get; set; } = UrdfPose.Identity;

        /// <summary>In the joint frame; URDF's default is X.</summary>
        public Vec3 Axis { get; set; } = Vec3.UnitX;

        /// <summary>Limits (rad or m), effort (N·m or N) and velocity (rad/s or m/s) as written; null when absent.</summary>
        public double? Lower { get; set; }
        public double? Upper { get; set; }
        public double Effort { get; set; }
        public double Velocity { get; set; }
        public double Damping { get; set; }
        public double Friction { get; set; }

        /// <summary>The joint this one follows (<c>mimic</c>), when it does.</summary>
        public string? Mimic { get; set; }

        public bool Moves => Type is "revolute" or "continuous" or "prismatic";
    }

    /// <summary>
    /// [urdf] A robot description (ROS URDF): links with their shapes and masses, joined by joints into a tree. Read from
    /// a .urdf, or from a .xacro after <see cref="Xacro"/> has expanded it.
    /// </summary>
    public sealed class UrdfRobot
    {
        public string Name { get; set; } = string.Empty;
        public List<UrdfLink> Links { get; } = new List<UrdfLink>();
        public List<UrdfJoint> Joints { get; } = new List<UrdfJoint>();

        /// <summary>What the reader left out or could not read, for the import's report.</summary>
        public List<string> Notes { get; } = new List<string>();

        public UrdfLink? Link(string name) => Links.FirstOrDefault(l => l.Name == name);

        /// <summary>The link no joint carries (the robot's base); with several, the first.</summary>
        public UrdfLink? Root
        {
            get
            {
                var children = new HashSet<string>(Joints.Select(j => j.Child), StringComparer.Ordinal);
                return Links.FirstOrDefault(l => !children.Contains(l.Name));
            }
        }

        /// <summary>Reads a .urdf or a .xacro file (<paramref name="args"/>: the xacro's <c>$(arg …)</c> values).</summary>
        public static UrdfRobot Load(string path, IReadOnlyDictionary<string, string>? args = null)
        {
            var doc = path.EndsWith(".xacro", StringComparison.OrdinalIgnoreCase) || Xacro.Uses(XDocument.Load(path))
                ? Xacro.Expand(path, args)
                : XDocument.Load(path);
            return Parse(doc);
        }

        public static UrdfRobot Parse(XDocument doc)
        {
            var root = doc.Root ?? throw new InvalidDataException("Boş URDF.");
            if (root.Name.LocalName != "robot") throw new InvalidDataException($"URDF'nin kökü <robot> olmalı, <{root.Name.LocalName}> bulundu.");
            var robot = new UrdfRobot { Name = (string?)root.Attribute("name") ?? "robot" };
            var materials = new Dictionary<string, double[]>(StringComparer.Ordinal);
            foreach (var m in root.Elements().Where(e => e.Name.LocalName == "material"))
                if ((string?)m.Attribute("name") is { } mn && Rgba(m) is { } c) materials[mn] = c;

            foreach (var e in root.Elements())
            {
                switch (e.Name.LocalName)
                {
                    case "link":
                        robot.Links.Add(ParseLink(e, materials, robot.Notes));
                        break;
                    case "joint":
                        robot.Joints.Add(ParseJoint(e));
                        break;
                    case "material":
                    case "gazebo":
                    case "transmission":
                    case "ros2_control":
                        break;
                    default:
                        robot.Notes.Add($"<{e.Name.LocalName}> öğesi okunmadı.");
                        break;
                }
            }

            foreach (var j in robot.Joints)
            {
                if (robot.Link(j.Parent) == null) throw new InvalidDataException($"'{j.Name}' ekleminin üst bağlantısı '{j.Parent}' yok.");
                if (robot.Link(j.Child) == null) throw new InvalidDataException($"'{j.Name}' ekleminin alt bağlantısı '{j.Child}' yok.");
            }

            if (robot.Links.Count == 0) throw new InvalidDataException("URDF'de bağlantı (<link>) yok.");
            return robot;
        }

        private static UrdfLink ParseLink(XElement e, Dictionary<string, double[]> materials, List<string> notes)
        {
            var link = new UrdfLink { Name = (string?)e.Attribute("name") ?? string.Empty };
            if (Child(e, "inertial") is { } inertial)
            {
                link.Mass = Num(Child(inertial, "mass")?.Attribute("value"), 0);
                link.InertialOrigin = Pose(Child(inertial, "origin"));
            }

            foreach (var v in e.Elements().Where(x => x.Name.LocalName == "visual"))
                if (Shape(v, materials, link.Name, notes) is { } s) link.Visuals.Add(s);
            foreach (var c in e.Elements().Where(x => x.Name.LocalName == "collision"))
                if (Shape(c, materials, link.Name, notes) is { } s) link.Collisions.Add(s);
            return link;
        }

        private static UrdfShape? Shape(XElement e, Dictionary<string, double[]> materials, string link, List<string> notes)
        {
            var g = Child(e, "geometry")?.Elements().FirstOrDefault();
            if (g == null) return null;
            var geometry = new UrdfGeometry();
            switch (g.Name.LocalName)
            {
                case "box":
                    geometry.Kind = UrdfGeometryKind.Box;
                    geometry.Size = Vec(g.Attribute("size"), new Vec3(0.1, 0.1, 0.1));
                    break;
                case "cylinder":
                    geometry.Kind = UrdfGeometryKind.Cylinder;
                    geometry.Radius = Num(g.Attribute("radius"), 0.05);
                    geometry.Length = Num(g.Attribute("length"), 0.1);
                    break;
                case "sphere":
                    geometry.Kind = UrdfGeometryKind.Sphere;
                    geometry.Radius = Num(g.Attribute("radius"), 0.05);
                    break;
                case "mesh":
                    geometry.Kind = UrdfGeometryKind.Mesh;
                    geometry.Filename = (string?)g.Attribute("filename");
                    geometry.Scale = Vec(g.Attribute("scale"), new Vec3(1, 1, 1));
                    break;
                default:
                    notes.Add($"{link}: <{g.Name.LocalName}> geometrisi okunmadı.");
                    return null;
            }

            var shape = new UrdfShape { Origin = Pose(Child(e, "origin")), Geometry = geometry };
            if (Child(e, "material") is { } m)
            {
                shape.MaterialName = (string?)m.Attribute("name");
                shape.Rgba = Rgba(m) ?? (shape.MaterialName != null && materials.TryGetValue(shape.MaterialName, out var c) ? c : null);
            }

            return shape;
        }

        private static UrdfJoint ParseJoint(XElement e)
        {
            var j = new UrdfJoint
            {
                Name = (string?)e.Attribute("name") ?? string.Empty,
                Type = ((string?)e.Attribute("type") ?? "fixed").Trim().ToLowerInvariant(),
                Parent = (string?)Child(e, "parent")?.Attribute("link") ?? string.Empty,
                Child = (string?)Child(e, "child")?.Attribute("link") ?? string.Empty,
                Origin = Pose(Child(e, "origin")),
                Axis = Vec(Child(e, "axis")?.Attribute("xyz"), Vec3.UnitX),
                Mimic = (string?)Child(e, "mimic")?.Attribute("joint")
            };
            if (j.Axis.Length < 1e-12) j.Axis = Vec3.UnitX;
            j.Axis = j.Axis.Normalized();
            if (Child(e, "limit") is { } limit)
            {
                if (limit.Attribute("lower") != null) j.Lower = Num(limit.Attribute("lower"), 0);
                if (limit.Attribute("upper") != null) j.Upper = Num(limit.Attribute("upper"), 0);
                j.Effort = Num(limit.Attribute("effort"), 0);
                j.Velocity = Num(limit.Attribute("velocity"), 0);
            }

            if (Child(e, "dynamics") is { } dynamics)
            {
                j.Damping = Num(dynamics.Attribute("damping"), 0);
                j.Friction = Num(dynamics.Attribute("friction"), 0);
            }

            return j;
        }

        private static XElement? Child(XElement e, string name) => e.Elements().FirstOrDefault(x => x.Name.LocalName == name);

        private static UrdfPose Pose(XElement? origin) =>
            origin == null ? UrdfPose.Identity : new UrdfPose(Vec(origin.Attribute("xyz"), Vec3.Zero), Vec(origin.Attribute("rpy"), Vec3.Zero));

        private static double[]? Rgba(XElement material)
        {
            var c = (string?)material.Elements().FirstOrDefault(x => x.Name.LocalName == "color")?.Attribute("rgba");
            if (c == null) return null;
            var v = Numbers(c);
            return v.Length >= 3 ? new[] { v[0], v[1], v[2], v.Length > 3 ? v[3] : 1.0 } : null;
        }

        private static double Num(XAttribute? a, double fallback) =>
            a != null && double.TryParse(a.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private static Vec3 Vec(XAttribute? a, Vec3 fallback)
        {
            if (a == null) return fallback;
            var v = Numbers(a.Value);
            return v.Length >= 3 ? new Vec3(v[0], v[1], v[2]) : v.Length == 1 ? new Vec3(v[0], v[0], v[0]) : fallback;
        }

        private static double[] Numbers(string s) =>
            s.Split(new[] { ' ', '\t', '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN)
                .Where(v => !double.IsNaN(v)).ToArray();
    }
}
