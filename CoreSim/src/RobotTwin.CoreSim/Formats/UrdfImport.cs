using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Formats
{
    public sealed class UrdfImportOptions
    {
        /// <summary>Where the robot's base goes in the world.</summary>
        public WorldPose Placement { get; set; } = new WorldPose();

        /// <summary>Base fixed to the world (an arm on its pedestal); null: fixed unless it rolls on wheels of its own.</summary>
        public bool? FixedBase { get; set; }

        /// <summary>Folders searched for ROS packages (<c>package://</c>, <c>$(find …)</c>) besides the file's own and its parents'.</summary>
        public List<string> PackagePaths { get; } = new List<string>();

        /// <summary>A xacro's arguments (<c>$(arg name)</c>).</summary>
        public Dictionary<string, string> XacroArgs { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Material of the links (density only counts where the URDF gives no mass).</summary>
        public string Material { get; set; } = "ALÜMİNYUM";
    }

    public sealed class UrdfImportResult
    {
        public UrdfRobot Robot { get; internal set; } = new UrdfRobot();
        public List<WorldObject> Objects { get; } = new List<WorldObject>();
        public List<WorldJoint> Joints { get; } = new List<WorldJoint>();
        public bool FixedBase { get; internal set; }

        /// <summary>What was left out or approximated (meshes not found, unsupported formats, xacro features).</summary>
        public List<string> Notes { get; } = new List<string>();

        public int MovingJoints => Joints.Count(j => j.Kind != WorldJointKind.Fixed);

        public string Summary =>
            $"{Robot.Name}: {Objects.Count} parça, {MovingJoints} hareketli eklem (motorlu), taban {(FixedBase ? "sabit" : "serbest")}" +
            (Notes.Count > 0 ? $", {Notes.Count} not" : string.Empty);
    }

    /// <summary>
    /// [urdf] Puts a robot description into a world: every link with a shape becomes a part (its visual meshes, or its
    /// collision ones where a visual cannot be read), placed at the zero configuration; every revolute, continuous and
    /// prismatic joint becomes a hinge or slider with its limits and a drive as strong and fast as the URDF's effort and
    /// velocity (a position drive holding 0, a speed drive for continuous joints), fixed joints weld the parts. Links without
    /// shapes (frames such as tool0) are folded into their parent.
    /// </summary>
    public static class UrdfImport
    {
        private static readonly string[] Readable = { ".stl", ".obj", ".ply", ".wrl", ".vrml", ".step", ".stp", ".iges", ".igs", ".brep" };

        public static UrdfImportResult Import(string path, WorldDocument world, UrdfImportOptions? options = null)
        {
            options ??= new UrdfImportOptions();
            string full = Path.GetFullPath(path);
            UrdfRobot robot;
            var notes = new List<string>();
            var doc = XDocument.Load(full);
            if (full.EndsWith(".xacro", StringComparison.OrdinalIgnoreCase) || Xacro.Uses(doc))
            {
                var expanded = Xacro.Expand(full, options.XacroArgs, out var xacroNotes);
                notes.AddRange(xacroNotes);
                robot = UrdfRobot.Parse(expanded);
            }
            else
            {
                robot = UrdfRobot.Parse(doc);
            }

            var result = AddToWorld(robot, Path.GetDirectoryName(full) ?? ".", world, options);
            result.Notes.InsertRange(0, notes.Concat(robot.Notes));
            return result;
        }

        public static UrdfImportResult AddToWorld(UrdfRobot robot, string baseDir, WorldDocument world, UrdfImportOptions options)
        {
            var result = new UrdfImportResult { Robot = robot };
            var root = robot.Root ?? throw new InvalidDataException("URDF'de kök bağlantı yok (her bağlantı bir eklemin altında).");
            var byParent = robot.Joints.GroupBy(j => j.Parent).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            // Each link's frame at the zero configuration, from the base's placement down the tree.
            var placement = new Transform3(options.Placement.Position, options.Placement.Orientation);
            var frame = new Dictionary<string, Transform3>(StringComparer.Ordinal) { [root.Name] = placement };
            var order = new List<UrdfJoint>();
            var queue = new Queue<string>();
            queue.Enqueue(root.Name);
            while (queue.Count > 0)
            {
                string parent = queue.Dequeue();
                if (!byParent.TryGetValue(parent, out var joints)) continue;
                foreach (var j in joints)
                {
                    if (frame.ContainsKey(j.Child)) continue;
                    frame[j.Child] = frame[parent] * j.Origin.ToTransform();
                    order.Add(j);
                    queue.Enqueue(j.Child);
                }
            }

            foreach (var l in robot.Links.Where(l => !frame.ContainsKey(l.Name)))
                result.Notes.Add($"'{l.Name}' bağlantısı kökten ulaşılamıyor (ayrı bir ağaç); alınmadı.");

            // A root with nothing to see is the world itself (ROS's "world" link): what it holds with a fixed joint is the
            // robot's base, bolted down. Otherwise an arm stands fixed, a robot on wheels of its own stays free.
            bool worldRoot = !root.HasGeometry && byParent.TryGetValue(root.Name, out var fromRoot) && fromRoot.Any(j => j.Type == "fixed");
            bool fixedBase = options.FixedBase ?? (worldRoot || !robot.Joints.Any(j => j.Type == "continuous" && j.Child.IndexOf("wheel", StringComparison.OrdinalIgnoreCase) >= 0));
            result.FixedBase = fixedBase;

            // Which links become parts: those with shapes, a base with shapes, and every link a moving joint carries.
            var moved = new HashSet<string>(robot.Joints.Where(j => j.Moves).Select(j => j.Child), StringComparer.Ordinal);
            var objectOf = new Dictionary<string, WorldObject>(StringComparer.Ordinal);
            var meshCache = new Dictionary<string, MeshModel?>(StringComparer.OrdinalIgnoreCase);
            foreach (var link in robot.Links)
            {
                if (!frame.TryGetValue(link.Name, out var t)) continue;
                bool needed = (link.Name == root.Name && link.HasGeometry) || moved.Contains(link.Name);
                var mesh = LinkMesh(link, baseDir, options.PackagePaths, meshCache, result.Notes, out string? color);
                if (mesh == null && !needed) continue;
                WorldObject o;
                if (mesh == null)
                {
                    // A frame a moving joint carries but with nothing to see (a wrist between two axes): a small ball.
                    o = world.AddBody("sphere", 0.01, 0.01, 0.01, Pose(t), options.Material);
                    o.MassKg = link.Mass > 0 ? link.Mass : 0.01;
                }
                else
                {
                    // The world rests a model's mesh on its lowest point, centred: the part's pose is moved by as much, so
                    // the mesh stays where the link's frame puts it.
                    var b = mesh.Bounds;
                    var rest = new Vec3((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2, b.Min.Z);
                    o = world.AddModel(mesh, link.Name, Pose(new Transform3(t.Apply(rest), t.Q)), options.Material);
                    if (link.Mass > 0) o.MassKg = link.Mass;
                    if (color != null) o.Color = color;
                }

                o.Name = string.Empty;   // the link's own name (AddModel shortened one with a dot in it), unique in the world
                o.Name = world.UniqueName(link.Name);
                objectOf[link.Name] = o;
                result.Objects.Add(o);
            }

            if (fixedBase && objectOf.TryGetValue(root.Name, out var baseObject)) baseObject.IsStatic = true;

            // Joints, parent first; a link folded into its parent (no part of its own) passes the parent on.
            string? PartOf(string link)
            {
                for (int guard = 0; guard < 1000; guard++)
                {
                    if (objectOf.ContainsKey(link)) return link;
                    var up = robot.Joints.FirstOrDefault(j => j.Child == link);
                    if (up == null) return null;
                    link = up.Parent;
                }

                return null;
            }

            foreach (var j in order)
            {
                if (!objectOf.TryGetValue(j.Child, out var child)) continue;
                string? parentLink = PartOf(j.Parent);
                if (parentLink == null && j.Type == "fixed")
                {
                    // Held by the world itself (a "world" root): the robot's base.
                    if (fixedBase) child.IsStatic = true;
                    continue;
                }

                var parent = parentLink != null ? objectOf[parentLink] : null;   // null: a joint to the ground
                if (j.Type is "floating" or "planar")
                {
                    result.Notes.Add($"'{j.Name}' ({j.Type}) eklemi serbest bırakıldı.");
                    continue;
                }

                var jf = frame[j.Child];   // the joint frame is the child's frame
                var axis = jf.ApplyVector(j.Axis).Normalized();
                var w = new WorldJoint
                {
                    Id = world.NextId("J"),
                    ObjectA = child.Id,
                    ObjectB = parent?.Id,
                    AnchorX = jf.P.X, AnchorY = jf.P.Y, AnchorZ = jf.P.Z,
                    AxisX = axis.X, AxisY = axis.Y, AxisZ = axis.Z
                };
                switch (j.Type)
                {
                    case "revolute":
                    case "continuous":
                        w.Kind = WorldJointKind.Hinge;
                        if (j.Type == "revolute" && j.Lower.HasValue && j.Upper.HasValue && j.Upper > j.Lower)
                        {
                            w.Lower = j.Lower.Value * 180 / Math.PI;
                            w.Upper = j.Upper.Value * 180 / Math.PI;
                        }

                        w.Drive = j.Type == "continuous" ? WorldJoint.DriveSpeedMode : WorldJoint.DrivePosition;
                        w.DriveForce = j.Effort;
                        w.DriveSpeed = j.Velocity * 180 / Math.PI;
                        // A joint whose zero lies outside its limits starts at the nearest one.
                        if (w.Lower is { } lo && w.Upper is { } hi && w.Drive == WorldJoint.DrivePosition) w.DriveTarget = Math.Max(lo, Math.Min(hi, 0));
                        break;
                    case "prismatic":
                        w.Kind = WorldJointKind.Slider;
                        if (j.Lower.HasValue && j.Upper.HasValue && j.Upper > j.Lower)
                        {
                            w.Lower = j.Lower.Value;
                            w.Upper = j.Upper.Value;
                        }

                        w.Drive = WorldJoint.DrivePosition;
                        w.DriveForce = j.Effort;
                        w.DriveSpeed = j.Velocity;
                        if (w.Lower is { } plo && w.Upper is { } phi) w.DriveTarget = Math.Max(plo, Math.Min(phi, 0));
                        break;
                    default:
                        w.Kind = WorldJointKind.Fixed;
                        break;
                }

                if (j.Friction > 0 && w.Kind != WorldJointKind.Fixed) w.Friction = j.Friction;
                if (j.Mimic != null) result.Notes.Add($"'{j.Name}' '{j.Mimic}' eklemini izliyor (mimic); kendi motoruyla ayrı sürülür.");
                world.Joints.Add(w);
                result.Joints.Add(w);
            }

            return result;
        }

        private static WorldPose Pose(Transform3 t)
        {
            var (roll, pitch, yaw) = t.Q.ToEuler();
            const double deg = 180 / Math.PI;
            return new WorldPose { X = t.P.X, Y = t.P.Y, Z = t.P.Z, Roll = roll * deg, Pitch = pitch * deg, Yaw = yaw * deg };
        }

        /// <summary>A link's shapes as one mesh in its own frame: the visuals, or the collision shapes where no visual can be read.</summary>
        private static MeshModel? LinkMesh(UrdfLink link, string baseDir, List<string> packages, Dictionary<string, MeshModel?> cache, List<string> notes, out string? color)
        {
            color = null;
            var mesh = Shapes(link.Visuals, link.Name, baseDir, packages, cache, notes, ref color, out int missing);
            if (mesh == null || missing > 0)
            {
                string? none = null;
                var collision = Shapes(link.Collisions, link.Name, baseDir, packages, cache, notes, ref none, out int missingCollision);
                if (collision != null && (mesh == null || missingCollision == 0))
                {
                    if (mesh != null) notes.Add($"{link.Name}: görsel ağ okunamadı, çarpışma ağı gösteriliyor.");
                    mesh = collision;
                }
            }

            return mesh;
        }

        private static MeshModel? Shapes(List<UrdfShape> shapes, string link, string baseDir, List<string> packages, Dictionary<string, MeshModel?> cache, List<string> notes, ref string? color, out int missing)
        {
            missing = 0;
            MeshModel? all = null;
            foreach (var s in shapes)
            {
                MeshModel? part = s.Geometry.Kind switch
                {
                    UrdfGeometryKind.Box => MeshModel.CreateBox(s.Geometry.Size),
                    UrdfGeometryKind.Cylinder => MeshModel.CreateCylinder(s.Geometry.Radius, s.Geometry.Length, 32),
                    UrdfGeometryKind.Sphere => MeshModel.CreateSphere(s.Geometry.Radius, 24, 12),
                    _ => MeshFile(s.Geometry, link, baseDir, packages, cache, notes)
                };
                if (part == null)
                {
                    missing++;
                    continue;
                }

                var t = s.Origin.ToTransform();
                part.Transform(t.Q, t.P);
                if (s.Rgba is { } c)
                {
                    var mc = MeshColor.FromFloat(c[0], c[1], c[2]);
                    color ??= mc.ToHex();
                    // Each shape in its own material's colour (an orange arm with a graphite hub), unless its file has colours.
                    if (part.TriangleColors == null && part.VertexColors == null) part.TriangleColors = Enumerable.Repeat(mc, part.TriangleCount).ToList();
                }
                if (all == null) all = part;
                else all.Append(part);
            }

            return all;
        }

        private static MeshModel? MeshFile(UrdfGeometry g, string link, string baseDir, List<string> packages, Dictionary<string, MeshModel?> cache, List<string> notes)
        {
            string file = g.Filename ?? string.Empty;
            string? path = ResolveMesh(file, baseDir, packages);
            if (path == null)
            {
                notes.Add($"{link}: ağ dosyası bulunamadı: {file}");
                return null;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (!Readable.Contains(ext))
            {
                notes.Add($"{link}: {ext} ağ biçimi okunamıyor ({Path.GetFileName(path)}).");
                return null;
            }

            if (!cache.TryGetValue(path, out var loaded))
            {
                try
                {
                    // URDF meshes are in metres whatever the format (an STL included), then scaled by the element.
                    loaded = ext == ".stl" || ext == ".obj" || ext == ".ply"
                        ? MeshModel.Load(path, new MeshLoadOptions { Units = MeshUnits.Meter })
                        : Libraries.PartModelFiles.LoadMesh(path);
                }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is NotSupportedException || ex is FormatException)
                {
                    notes.Add($"{link}: {Path.GetFileName(path)} okunamadı: {ex.Message}");
                    loaded = null;
                }

                cache[path] = loaded;
            }

            if (loaded == null) return null;
            var mesh = loaded.Clone();
            var sc = g.Scale;
            if (Math.Abs(sc.X - 1) > 1e-12 || Math.Abs(sc.Y - 1) > 1e-12 || Math.Abs(sc.Z - 1) > 1e-12)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    var v = mesh.Vertices[i];
                    mesh.Vertices[i] = new Vec3(v.X * sc.X, v.Y * sc.Y, v.Z * sc.Z);
                }

                if (sc.X * sc.Y * sc.Z < 0) mesh.FlipWinding();
                mesh.Normals = null;
            }

            return mesh;
        }

        /// <summary>A mesh reference's file: <c>package://pkg/…</c>, <c>file://…</c>, or a path relative to the URDF.</summary>
        public static string? ResolveMesh(string filename, string baseDir, IEnumerable<string> packages)
        {
            if (string.IsNullOrWhiteSpace(filename)) return null;
            string f = filename.Trim();
            if (f.StartsWith("package://", StringComparison.OrdinalIgnoreCase))
            {
                string rest = f.Substring("package://".Length);
                int slash = rest.IndexOf('/');
                if (slash <= 0) return null;
                string pkg = rest.Substring(0, slash), inside = rest.Substring(slash + 1);
                string? dir = FindPackage(pkg, baseDir, packages);
                string? p = dir != null ? Path.Combine(dir, inside.Replace('/', Path.DirectorySeparatorChar)) : null;
                if (p != null && File.Exists(p)) return p;
                // Shipped without its package folder: the same path under the URDF's folder, or the file's name next to it.
                return Existing(Path.Combine(baseDir, inside)) ?? Existing(Path.Combine(baseDir, Path.GetFileName(inside)));
            }

            if (f.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                f = f.Substring("file://".Length);
                if (f.Length > 2 && f[0] == '/' && f[2] == ':') f = f.Substring(1);   // file:///C:/…
            }

            if (Path.IsPathRooted(f)) return Existing(f) ?? Existing(Path.Combine(baseDir, Path.GetFileName(f)));
            return Existing(Path.Combine(baseDir, f)) ?? Existing(Path.Combine(baseDir, Path.GetFileName(f)));
        }

        private static string? Existing(string p) => File.Exists(p) ? Path.GetFullPath(p) : null;

        /// <summary>
        /// A ROS package's folder: the given search folders, then from the URDF's folder up to the drive's root a folder
        /// named so, or a folder whose package.xml gives that name (it and its subfolders two levels down).
        /// </summary>
        public static string? FindPackage(string name, string startDir, IEnumerable<string> extra)
        {
            foreach (var root in extra)
            {
                string direct = Path.Combine(root, name);
                if (Directory.Exists(direct)) return direct;
                if (NamedPackage(root, name, 2) is { } found) return found;
            }

            var dir = new DirectoryInfo(Path.GetFullPath(startDir));
            for (int up = 0; dir != null && up < 8; up++, dir = dir.Parent)
            {
                if (string.Equals(dir.Name, name, StringComparison.OrdinalIgnoreCase) || PackageName(dir.FullName) == name) return dir.FullName;
                string sibling = Path.Combine(dir.FullName, name);
                if (Directory.Exists(sibling)) return sibling;
            }

            return null;
        }

        private static string? NamedPackage(string root, string name, int depth)
        {
            if (depth < 0 || !Directory.Exists(root)) return null;
            if (PackageName(root) == name) return root;
            try
            {
                foreach (var d in Directory.EnumerateDirectories(root))
                    if (NamedPackage(d, name, depth - 1) is { } found) return found;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is IOException)
            {
            }

            return null;
        }

        private static string? PackageName(string dir)
        {
            string xml = Path.Combine(dir, "package.xml");
            if (!File.Exists(xml)) return null;
            try
            {
                return XDocument.Load(xml).Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value.Trim();
            }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException)
            {
                return null;
            }
        }
    }
}
