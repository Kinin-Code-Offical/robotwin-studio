using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>What a world object is.</summary>
    public enum WorldObjectKind
    {
        /// <summary>A circuit board the user designed (schematic + PCB + firmware), embedded in the world.</summary>
        Board,

        /// <summary>One catalogue part as a free-standing physical object: servo, motor, battery, sensor, display, Arduino …</summary>
        Component,

        /// <summary>A mechanical primitive: box, plate, cylinder, wheel, sphere.</summary>
        Body,

        /// <summary>An imported 3D model (STL/OBJ/PLY) used as a mechanical part.</summary>
        Model
    }

    /// <summary>Position (metres, Z up) and orientation (degrees, roll-pitch-yaw about X, Y, Z).</summary>
    public sealed class WorldPose
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Roll { get; set; }
        public double Pitch { get; set; }
        public double Yaw { get; set; }

        public WorldPose Clone() => (WorldPose)MemberwiseClone();

        [JsonIgnore]
        public Vec3 Position => new Vec3(X, Y, Z);

        [JsonIgnore]
        public Quat Orientation =>
            Quat.FromAxisAngle(Vec3.UnitZ, Yaw * Math.PI / 180.0) *
            Quat.FromAxisAngle(Vec3.UnitY, Pitch * Math.PI / 180.0) *
            Quat.FromAxisAngle(Vec3.UnitX, Roll * Math.PI / 180.0);

        /// <summary>Object-local point (metres) to world.</summary>
        public Vec3 ToWorld(Vec3 local) => Position + Orientation.Rotate(local);
    }

    public sealed class WorldObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public WorldObjectKind Kind { get; set; }
        public WorldPose Pose { get; set; } = new WorldPose();

        /// <summary>Fixed in the world (a table, a wall, a test stand); not moved by physics.</summary>
        public bool IsStatic { get; set; }

        /// <summary>Material of bodies and models (density, friction): PLA, PETG, ABS, ALÜMİNYUM, ÇELİK, AHŞAP, KAUÇUK …</summary>
        public string Material { get; set; } = "PLA";

        /// <summary>Mass override in kg (null = computed from geometry and parts).</summary>
        public double? MassKg { get; set; }

        /// <summary>Display colour "#RRGGBB" (null = material colour).</summary>
        public string? Color { get; set; }

        // ---- Board
        /// <summary>Id of the <see cref="WorldDesign"/> this board instantiates (several objects may share one design).</summary>
        public string? DesignId { get; set; }

        // ---- Component
        public string? PartId { get; set; }
        public string? PresetId { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Program of a stand-alone programmable component (an Arduino or ESP32 placed directly in the world).</summary>
        public FirmwareSource? Firmware { get; set; }

        // ---- Body
        /// <summary>"box", "plate", "cylinder", "wheel", "sphere".</summary>
        public string Shape { get; set; } = "box";

        /// <summary>Size in metres: box X×Y×Z; cylinder/wheel diameter = SizeX, width/height = SizeZ; sphere diameter = SizeX.</summary>
        public double SizeX { get; set; } = 0.1;
        public double SizeY { get; set; } = 0.1;
        public double SizeZ { get; set; } = 0.1;

        // ---- Model
        public EmbeddedMesh? Mesh { get; set; }
        public string? MeshName { get; set; }

        /// <summary>
        /// [assembly] The part of a Parça (model) document this object was sent from ("document id/object id"):
        /// sending that part again updates this object (shape, mass, material) and keeps where it is mounted.
        /// </summary>
        public string? ModelSource { get; set; }

        /// <summary>
        /// [assembly] Where the part's own origin (as modelled) sits in the object's frame (m, x/y/z). The mesh is shown
        /// and simulated about its bounding box, which moves when the part changes shape; this keeps the part's
        /// origin, and so its mounting holes, where they were when it is sent again.
        /// </summary>
        public double[]? ModelOrigin { get; set; }

        /// <summary>
        /// [ros] The robot-description link this part is (a URDF link's name): ROS's frame for it (<c>/tf</c>, <c>/odom</c>),
        /// whose origin is <see cref="ModelOrigin"/> in the object's frame. Null: not from a robot description.
        /// </summary>
        public string? Link { get; set; }

        /// <summary>[ros] Sensors the robot description puts on this part (a laser scanner, an IMU), published on ROS.</summary>
        public List<RobotSensor>? RobotSensors { get; set; }

        /// <summary>[assembly] The circuit part this object is ("project id/RefDes"): a Montaj follows that part.</summary>
        public string? ProjectRef { get; set; }

        /// <summary>
        /// [integrity] That part's identity (<see cref="Design.SchematicPart.Uid"/>): a renamed part keeps its object, and with
        /// it where it was put and how it was joined. Empty in Montajs saved before identities (matched by designator then).
        /// </summary>
        public string? PartUid { get; set; }

        /// <summary>
        /// [assembly] Mounted mirrored (a rover's right motor on the shared axle): a Montaj lays its cables to the
        /// opposite lead (A ↔ B), as the wires are swapped on a real kit, so "forward" in the code drives forward.
        /// </summary>
        public bool MirroredLeads { get; set; }

        /// <summary>Infill fraction for 3D-printed models and bodies (mass).</summary>
        public double Infill { get; set; } = 1.0;

        public WorldObject Clone()
        {
            var json = JsonSerializer.Serialize(this, RobotWinFormats.Json);
            return JsonSerializer.Deserialize<WorldObject>(json, RobotWinFormats.Json)!;
        }
    }

    /// <summary>A board design embedded in the world (a whole .rwproj package).</summary>
    public sealed class WorldDesign
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>Where the design came from (informational; the world keeps its own copy).</summary>
        public string? SourcePath { get; set; }

        [JsonIgnore]
        public ProjectPackage Package { get; set; } = new ProjectPackage();
    }

    /// <summary>
    /// One end of a cable: an object and an electrical pin on it. For a board the pin is
    /// "RefDes.Pin" of a part on the board (usually a connector, "J1.2"); for a component
    /// it is the part's pin name ("SIG", "V+", "GND", "D9" …).
    /// </summary>
    public sealed class CableEnd
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Pin { get; set; } = string.Empty;

        public override string ToString() => ObjectId + ":" + Pin;
    }

    public sealed class WorldCable
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>[assembly] Laid by a Montaj for this net of a project ("project id/net"); cables you lay yourself have none.</summary>
        public string? ProjectNet { get; set; }
        public CableEnd A { get; set; } = new CableEnd();
        public CableEnd B { get; set; } = new CableEnd();

        /// <summary>Length in metres; 0 = from the distance between the ends plus slack.</summary>
        public double Length { get; set; }

        /// <summary>
        /// [route] Cut to its way and held along it with cable ties (a drone's motor and battery leads, a robot's harness):
        /// with no length of its own it gets only a few millimetres of slack, and it is laid along its route instead of
        /// bowed out.
        /// </summary>
        public bool Tied { get; set; }

        /// <summary>American wire gauge (24 = typical jumper wire, 18 = motor lead, 12 = battery lead).</summary>
        public double Awg { get; set; } = 24;

        public string Color { get; set; } = "#E5484D";

        /// <summary>
        /// Contact resistance of one plugged-in end (Dupont / 2.54 mm header socket): 10–30 mΩ new,
        /// 50–100 mΩ worn. Every world cable is plugged in unless an end is marked soldered.
        /// </summary>
        public const double PlugContactResistance = 0.025;

        /// <summary>A wire soldered to a pad or terminal: the joint itself is a fraction of a milliohm.</summary>
        public const double SolderJointResistance = 0.0005;

        /// <summary>End A is soldered (no plug contact).</summary>
        public bool SolderedA { get; set; }

        /// <summary>End B is soldered (no plug contact).</summary>
        public bool SolderedB { get; set; }

        /// <summary>Contact resistance of one end; a plug being pulled loosens (tension / pull-out force, 0…1).</summary>
        public double EndContact(bool endA, double pull = 0)
        {
            if (endA ? SolderedA : SolderedB) return SolderJointResistance;
            double k = Math.Max(0, Math.Min(1, pull));
            return PlugContactResistance * (1 + 6 * k * k);
        }

        /// <summary>Contact resistance of both ends at rest (Ω).</summary>
        public double ContactResistance => EndContact(true) + EndContact(false);

        /// <summary>Copper resistance at 20 °C for the given length (Ω).</summary>
        public static double Resistance(double awg, double length)
        {
            double area = CrossSection(awg);
            return 1.724e-8 * Math.Max(1e-4, length) / area;
        }

        /// <summary>Conductor cross-section of a gauge (m²).</summary>
        public static double CrossSection(double awg)
        {
            double d = 0.127e-3 * Math.Pow(92.0, (36.0 - awg) / 39.0);
            return Math.PI * d * d / 4.0;
        }

        /// <summary>Continuous current a single PVC-insulated wire carries in free air (A), roughly.</summary>
        public static double Ampacity(double awg)
        {
            // Chassis-wiring values: AWG 28 ≈ 1.4 A … AWG 12 ≈ 41 A (≈ 1.25 dB per gauge).
            return 1.4 * Math.Pow(10.0, (28.0 - awg) * 0.0915);
        }
    }

    public enum WorldJointKind
    {
        /// <summary>Rigidly glued/screwed together.</summary>
        Fixed,

        /// <summary>Rotates about an axis (bearing, servo horn, motor shaft).</summary>
        Hinge,

        /// <summary>Slides along an axis (linear rail).</summary>
        Slider
    }

    /// <summary>
    /// A mechanical connection between two objects (or an object and the ground).
    /// <see cref="PortA"/>/<see cref="PortB"/> pick a moving sub-part: "horn" of a servo,
    /// "shaft" of a motor; empty = the object's main body.
    /// </summary>
    /// <summary>[assembly] How a project's circuit came into a Montaj.</summary>
    public enum AssemblyForm
    {
        /// <summary>Parts placed on the project's PCB on one board, the rest as separate parts (or all separate without a PCB).</summary>
        Auto,

        /// <summary>The PCB as made: what is placed on it is soldered on one board; motors, batteries … are separate, cabled to it.</summary>
        WithPcb,

        /// <summary>No PCB: every part is a separate part, cabled pin to pin (a prototype on jumpers).</summary>
        LooseParts
    }

    /// <summary>[assembly] A project brought into a Montaj (its own project, or another one added to it).</summary>
    public sealed class AssemblySource
    {
        public string ProjectId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;

        /// <summary>The .rwproj it is read from when it is not the Montaj's own project (null for its own).</summary>
        public string? Path { get; set; }

        public AssemblyForm Form { get; set; } = AssemblyForm.Auto;

        /// <summary>The form actually used (Auto resolved): true = with its PCB board.</summary>
        public bool UsedPcb { get; set; }
    }

    public sealed class WorldJoint
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>[ros] The joint's own name (an imported robot's URDF joint name, what ROS calls it); null: its id.</summary>
        public string? Name { get; set; }

        [JsonIgnore]
        public string RosName => string.IsNullOrEmpty(Name) ? Id : Name!;
        public WorldJointKind Kind { get; set; }
        public string ObjectA { get; set; } = string.Empty;
        public string PortA { get; set; } = string.Empty;

        /// <summary>Null = attached to the ground/world.</summary>
        public string? ObjectB { get; set; }
        public string PortB { get; set; } = string.Empty;

        /// <summary>Anchor point in world coordinates at the time the joint was made (m).</summary>
        public double AnchorX { get; set; }
        public double AnchorY { get; set; }
        public double AnchorZ { get; set; }

        /// <summary>Axis in world coordinates at the time the joint was made.</summary>
        public double AxisX { get; set; }
        public double AxisY { get; set; }
        public double AxisZ { get; set; } = 1;

        /// <summary>Limits in degrees (hinge) or metres (slider); null = free.</summary>
        public double? Lower { get; set; }
        public double? Upper { get; set; }

        /// <summary>Force (N) above which the joint breaks; 0 = from the fastening (unbreakable when none is set).</summary>
        public double BreakForce { get; set; }

        /// <summary>Coulomb friction of a hinge (N·m: a hub turning on an axle, a bushing) or slider (N); 0 = an ideal bearing.</summary>
        public double Friction { get; set; }

        /// <summary>[fasten] How a fixed joint is fastened: screws, glue, tape, zip tie, solder (<see cref="Sandbox.Fastening"/>); empty = unspecified.</summary>
        public string Fastening { get; set; } = string.Empty;

        /// <summary>
        /// [drive] A motor on a hinge or slider, like a servo axis of an industrial robot: <see cref="DrivePosition"/> holds
        /// the angle (°) or travel (m) at <see cref="DriveTarget"/>, <see cref="DriveSpeedMode"/> turns or slides at it (°/s or
        /// m/s); never with more than <see cref="DriveForce"/> (N·m or N), a position drive no faster than
        /// <see cref="DriveSpeed"/> (°/s or m/s). Empty: no motor, physics alone decides (a bearing, a rail).
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]   // a joint without a motor writes none of these
        public string? Drive { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double DriveTarget { get; set; }

        /// <summary>Peak torque (N·m) or force (N) of the drive; 0 = 10 N·m / 200 N.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double DriveForce { get; set; }

        /// <summary>Top speed of a position drive (°/s or m/s); 0 = 180 °/s / 0.5 m/s.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double DriveSpeed { get; set; }

        /// <summary>
        /// [urdf] The joint this one follows (a gripper's second finger, a URDF <c>mimic</c>): its motor holds
        /// <see cref="MimicFactor"/> × that joint's position + <see cref="MimicOffset"/> (rad or m), as the linkage that couples them.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public string? MimicOf { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double? MimicFactor { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double MimicOffset { get; set; }

        public const string DrivePosition = "position";
        public const string DriveSpeedMode = "speed";

        [System.Text.Json.Serialization.JsonIgnore]
        public bool Driven => Kind != WorldJointKind.Fixed && (Drive == DrivePosition || Drive == DriveSpeedMode);
    }

    /// <summary>
    /// An open world: boards you designed, free-standing parts, mechanical bodies and
    /// imported models, wired together with cables and joined mechanically, in one
    /// environment that is simulated electrically and physically as a whole.
    /// </summary>
    public sealed class WorldDocument
    {
        public const string Format = "robotwin.world";
        public const int FormatVersion = 1;
        public const string Extension = ".rwworld";

        public string Title { get; set; } = "Yeni Dünya";
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// [assembly] A project's Montaj: its circuit's boards and parts as real 3D parts, the parts designed on the
        /// Parça page and the joints between them (the Montaj stage opens it; other worlds are test environments).
        /// </summary>
        public bool IsAssembly { get; set; }

        /// <summary>[assembly] The project (<see cref="Design.Schematic.ProjectId"/>) a Montaj belongs to.</summary>
        public string? ProjectId { get; set; }

        /// <summary>[assembly] The projects whose circuits are in this Montaj and how each was brought in.</summary>
        public List<AssemblySource> AssemblySources { get; set; } = new List<AssemblySource>();

        public AssemblySource? SourceOf(string projectId) =>
            AssemblySources.FirstOrDefault(s => string.Equals(s.ProjectId, projectId, StringComparison.OrdinalIgnoreCase));
        public WorldSpec Environment { get; set; } = new WorldSpec { Name = "Atölye", Width = 4, Depth = 3, Walls = false, Track = "none" };
        public List<WorldObject> Objects { get; set; } = new List<WorldObject>();
        public List<WorldCable> Cables { get; set; } = new List<WorldCable>();
        public List<WorldJoint> Joints { get; set; } = new List<WorldJoint>();

        /// <summary>[objs] Workshop lighting 0 (dark room) … 1 (bright); the world's own lights matter more in the dark.</summary>
        public double RoomLight { get; set; } = 1.0;
        /// <summary>Atmosphere, wind, force fields, behaviours, scripts and plugins of the world (Sdk/WorldSdkSettings.cs).</summary>
        public RobotTwin.CoreSim.Sdk.WorldSdkSettings Sdk { get; set; } = new RobotTwin.CoreSim.Sdk.WorldSdkSettings();   // [sdk]

        [JsonIgnore]
        public List<WorldDesign> Designs { get; set; } = new List<WorldDesign>();

        /// <summary>Custom parts used by the world's components (and by embedded designs).</summary>
        public List<CustomPart> Parts { get; set; } = new List<CustomPart>();

        [JsonIgnore]
        public byte[]? Preview { get; set; }

        /// <summary>Sculpted ground with painted surface layers (saved as terrain.bin, kept out of the JSON and its undo snapshots).</summary>
        [JsonIgnore]
        public TerrainMap? Terrain { get; set; } // [terrain]

        public WorldObject? Find(string? id) =>
            id == null ? null : Objects.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

        public WorldDesign? FindDesign(string? id) =>
            id == null ? null : Designs.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Next free short id with a prefix ("O" -> "O7").</summary>
        public string NextId(string prefix)
        {
            int max = 0;
            IEnumerable<string> ids = Objects.Select(o => o.Id).Concat(Cables.Select(c => c.Id)).Concat(Joints.Select(j => j.Id)).Concat(Designs.Select(d => d.Id));
            foreach (var id in ids)
            {
                if (id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(id.Substring(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) max = Math.Max(max, n);
            }

            return prefix + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A display name not used yet ("Servo" -> "Servo 2").</summary>
        public string UniqueName(string baseName)
        {
            if (Objects.All(o => !string.Equals(o.Name, baseName, StringComparison.OrdinalIgnoreCase))) return baseName;
            for (int i = 2; ; i++)
            {
                string n = baseName + " " + i.ToString(CultureInfo.InvariantCulture);
                if (Objects.All(o => !string.Equals(o.Name, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }

        // ------------------------------------------------------------------ editing helpers

        /// <summary>Adds a design (copy of a project) and returns it; the same design can be placed several times.</summary>
        public WorldDesign AddDesign(ProjectPackage package, string? sourcePath = null)
        {
            var copy = ProjectPackage.FromBytes(package.ToBytes());
            copy.Montaj = null;   // [assembly] a board inside a world does not carry its project's Montaj
            var d = new WorldDesign
            {
                Id = NextId("D"),
                Name = string.IsNullOrWhiteSpace(copy.Schematic.Title) ? "Kart" : copy.Schematic.Title,
                SourcePath = sourcePath,
                Package = copy
            };
            Designs.Add(d);
            return d;
        }

        public WorldObject AddBoard(WorldDesign design, WorldPose pose)
        {
            var o = new WorldObject { Id = NextId("O"), Name = UniqueName(design.Name), Kind = WorldObjectKind.Board, DesignId = design.Id, Pose = pose, Material = "FR4" };
            Objects.Add(o);
            return o;
        }

        public WorldObject AddComponent(PartLibrary library, string presetOrPartId, WorldPose pose)
        {
            var preset = library.FindPreset(presetOrPartId);
            var def = preset != null ? library.Get(preset.PartId) : library.Get(presetOrPartId);
            var o = new WorldObject
            {
                Id = NextId("O"),
                Name = UniqueName(preset?.Name ?? def.Name),
                Kind = WorldObjectKind.Component,
                PartId = def.Id,
                PresetId = preset?.Id,
                Pose = pose,
                Material = "ABS"
            };
            Objects.Add(o);
            return o;
        }

        public WorldObject AddBody(string shape, double sx, double sy, double sz, WorldPose pose, string material = "PLA")
        {
            string name = shape switch
            {
                "plate" => "Plaka",
                "cylinder" => "Silindir",
                "wheel" => "Tekerlek",
                "sphere" => "Küre",
                _ => "Kutu"
            };
            var o = new WorldObject
            {
                Id = NextId("O"), Name = UniqueName(name), Kind = WorldObjectKind.Body, Shape = shape,
                SizeX = sx, SizeY = sy, SizeZ = sz, Pose = pose, Material = shape == "wheel" ? "KAUÇUK" : material
            };
            Objects.Add(o);
            return o;
        }

        public WorldObject AddModel(MeshModel mesh, string name, WorldPose pose, string material = "PLA")
        {
            var o = new WorldObject
            {
                Id = NextId("O"), Name = UniqueName(Path.GetFileNameWithoutExtension(name)), Kind = WorldObjectKind.Model,
                Mesh = mesh.ToEmbedded(), MeshName = name, Pose = pose, Material = material
            };
            Objects.Add(o);
            return o;
        }

        public WorldCable Connect(CableEnd a, CableEnd b, double awg = 24, string? color = null)
        {
            var c = new WorldCable { Id = NextId("C"), A = a, B = b, Awg = awg, Color = color ?? "#E5484D" };
            c.Name = a + " → " + b;
            Cables.Add(c);
            return c;
        }

        /// <summary>
        /// Everything mounted on an object through joints, recursively (an arm on a servo
        /// horn, a second servo on that arm …). Joint direction: <see cref="WorldJoint.ObjectA"/>
        /// is mounted on <see cref="WorldJoint.ObjectB"/>.
        /// </summary>
        public List<WorldObject> MountedOn(string objectId)
        {
            var result = new List<WorldObject>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { objectId };
            var queue = new Queue<string>();
            queue.Enqueue(objectId);
            while (queue.Count > 0)
            {
                string parent = queue.Dequeue();
                foreach (var j in Joints)
                {
                    if (!string.Equals(j.ObjectB, parent, StringComparison.OrdinalIgnoreCase) || !seen.Add(j.ObjectA)) continue;
                    var child = Find(j.ObjectA);
                    if (child == null) continue;
                    result.Add(child);
                    queue.Enqueue(child.Id);
                }
            }

            return result;
        }

        /// <summary>Moves an object and everything mounted on it by a world offset (m).</summary>
        public void MoveAssembly(WorldObject root, double dx, double dy, double dz)
        {
            foreach (var o in new[] { root }.Concat(MountedOn(root.Id)))
            {
                o.Pose.X += dx;
                o.Pose.Y += dy;
                o.Pose.Z += dz;
            }

            foreach (var j in Joints.Where(j => j.ObjectA == root.Id || MountedOn(root.Id).Any(m => m.Id == j.ObjectA)))
            {
                j.AnchorX += dx;
                j.AnchorY += dy;
                j.AnchorZ += dz;
            }
        }

        /// <summary>Turns an object and everything mounted on it about the vertical axis through the object (degrees).</summary>
        public void RotateAssembly(WorldObject root, double yawDegrees)
        {
            double a = yawDegrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            double cx = root.Pose.X, cy = root.Pose.Y;
            foreach (var o in new[] { root }.Concat(MountedOn(root.Id)))
            {
                double x = o.Pose.X - cx, y = o.Pose.Y - cy;
                o.Pose.X = cx + c * x - s * y;
                o.Pose.Y = cy + s * x + c * y;
                o.Pose.Yaw = ((o.Pose.Yaw + yawDegrees) % 360 + 360) % 360;
            }
        }

        /// <summary>Removes an object together with its cables and joints.</summary>
        public void Remove(WorldObject o)
        {
            Objects.Remove(o);
            Cables.RemoveAll(c => c.A.ObjectId == o.Id || c.B.ObjectId == o.Id);
            Joints.RemoveAll(j => j.ObjectA == o.Id || j.ObjectB == o.Id);
            if (o.DesignId != null && Objects.All(x => x.DesignId != o.DesignId)) Designs.RemoveAll(d => d.Id == o.DesignId);
        }

        // ------------------------------------------------------------------ file format

        private sealed class DesignIndex
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? SourcePath { get; set; }
        }

        /// <summary>[assembly] The world as .rwworld bytes (a project keeps its Montaj this way).</summary>
        public byte[] ToBytes()
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) WriteTo(zip);
            return ms.ToArray();
        }

        public static WorldDocument FromBytes(byte[] data)
        {
            using var ms = new MemoryStream(data, writable: false);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            return ReadFrom(zip);
        }

        public void Save(string path) => RobotWinFormats.SaveAtomically(path, WriteTo);

        private void WriteTo(ZipArchive zip)
        {
            var info = new Manifest { Format = Format, FormatVersion = FormatVersion, Title = Title, Description = Description };
            info.Summary["objects"] = Objects.Count.ToString(CultureInfo.InvariantCulture);
            info.Summary["boards"] = Objects.Count(o => o.Kind == WorldObjectKind.Board).ToString(CultureInfo.InvariantCulture);
            info.Summary["cables"] = Cables.Count.ToString(CultureInfo.InvariantCulture);
            {
                RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(info, RobotWinFormats.Json));
                RobotWinFormats.WriteText(zip, "world.json", JsonSerializer.Serialize(this, RobotWinFormats.Json));
                var index = Designs.Select(d => new DesignIndex { Id = d.Id, Name = d.Name, SourcePath = d.SourcePath }).ToList();
                RobotWinFormats.WriteText(zip, "designs/index.json", JsonSerializer.Serialize(index, RobotWinFormats.Json));
                foreach (var d in Designs) RobotWinFormats.WriteBytes(zip, "designs/" + d.Id + ProjectPackage.Extension, d.Package.ToBytes());
                if (Preview != null) RobotWinFormats.WriteBytes(zip, "preview.png", Preview);
                if (Terrain != null) RobotWinFormats.WriteBytes(zip, "terrain.bin", Terrain.ToBytes()); // [terrain]
            }
        }

        public static WorldDocument Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            return ReadFrom(zip);
        }

        private static WorldDocument ReadFrom(ZipArchive zip)
        {
            RobotWinFormats.ReadManifest(zip, Format, FormatVersion);
            var world = JsonSerializer.Deserialize<WorldDocument>(
                RobotWinFormats.ReadText(zip, "world.json") ?? throw new InvalidDataException("Dünya dosyasında world.json yok."), RobotWinFormats.Json)
                ?? throw new InvalidDataException("world.json okunamadı.");
            var index = RobotWinFormats.ReadText(zip, "designs/index.json");
            if (index != null)
            {
                foreach (var d in JsonSerializer.Deserialize<List<DesignIndex>>(index, RobotWinFormats.Json) ?? new List<DesignIndex>())
                {
                    var bytes = RobotWinFormats.ReadBytes(zip, "designs/" + d.Id + ProjectPackage.Extension);
                    if (bytes == null) continue;
                    world.Designs.Add(new WorldDesign { Id = d.Id, Name = d.Name, SourcePath = d.SourcePath, Package = ProjectPackage.FromBytes(bytes) });
                }
            }

            world.Preview = RobotWinFormats.ReadBytes(zip, "preview.png");
            if (RobotWinFormats.ReadBytes(zip, "terrain.bin") is { } terrain) world.Terrain = TerrainMap.FromBytes(terrain); // [terrain]
            return world;
        }

        /// <summary>Deep copy (used for undo snapshots).</summary>
        public WorldDocument Clone()
        {
            var copy = JsonSerializer.Deserialize<WorldDocument>(JsonSerializer.Serialize(this, RobotWinFormats.Json), RobotWinFormats.Json)!;
            copy.Designs = Designs.Select(d => new WorldDesign { Id = d.Id, Name = d.Name, SourcePath = d.SourcePath, Package = d.Package }).ToList();
            copy.Preview = Preview;
            copy.Terrain = Terrain?.Clone(); // [terrain]
            return copy;
        }

        public string ToJson() => JsonSerializer.Serialize(this, RobotWinFormats.Json);

        /// <summary>Objects, cables, joints and environment from <see cref="ToJson"/> (designs are not part of the JSON).</summary>
        public static WorldDocument FromJson(string json) =>
            JsonSerializer.Deserialize<WorldDocument>(json, RobotWinFormats.Json) ?? throw new InvalidDataException("Dünya verisi okunamadı.");
    }
}
