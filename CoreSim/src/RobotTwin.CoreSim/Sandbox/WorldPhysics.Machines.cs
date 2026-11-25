using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>Geometry of the end effectors shared by physics and the 3D models (tool frame, metres; +Z = approach).</summary>
    public static class ToolGeometry
    {
        // Parallel gripper: housing, two fingers sliding along ±Y.
        public const double GripperHousingX = 0.090, GripperHousingY = 0.070, GripperHousingZ = 0.075;
        public const double FingerX = 0.026, FingerY = 0.012, FingerZ = 0.065;
        public const double FingerMinGap = 0.006;

        public static double GripperTcp => GripperHousingZ + FingerZ - 0.018;

        // Vacuum: mount block, stem, bellows cup(s).
        public const double VacuumMountZ = 0.045, VacuumStemZ = 0.030, VacuumCupZ = 0.025, VacuumStroke = 0.012;

        public static double VacuumTcp => VacuumMountZ + VacuumStemZ + VacuumCupZ;

        /// <summary>Cup centres (tool frame XY) for 1, 2 or 4 cups.</summary>
        public static IReadOnlyList<(double X, double Y)> CupLayout(int cups, double diameter) => cups switch
        {
            2 => new[] { (-0.6 * diameter - 0.01, 0.0), (0.6 * diameter + 0.01, 0.0) },
            4 => new[] { (-0.6 * diameter - 0.01, -0.45 * diameter - 0.008), (0.6 * diameter + 0.01, -0.45 * diameter - 0.008), (-0.6 * diameter - 0.01, 0.45 * diameter + 0.008), (0.6 * diameter + 0.01, 0.45 * diameter + 0.008) },
            _ => new[] { (0.0, 0.0) }
        };

        /// <summary>Flange → TCP of a tool part.</summary>
        public static Transform3 Tcp(string? partId) => partId == CellParts.GripperId
            ? Transform3.Translation(0, 0, GripperTcp)
            : partId == CellParts.VacuumId ? Transform3.Translation(0, 0, VacuumTcp) : Transform3.Identity;
    }

    /// <summary>A robot arm while the world runs: its controller, link bodies and drives.</summary>
    public sealed class MachineArm
    {
        internal MachineArm(WorldObject o, ArmModel model, ArmController controller)
        {
            Object = o;
            Model = model;
            Controller = controller;
        }

        public WorldObject Object { get; }
        public string ObjectId => Object.Id;
        public ArmModel Model { get; }

        /// <summary>The robot controller: program, jog, MoveJ/MoveL, I/O (scripts drive the arm through it).</summary>
        public ArmController Controller { get; }

        internal SceneBody BaseBody = null!;
        internal readonly List<SceneBody> Links = new List<SceneBody>();
        internal readonly List<SceneJoint> Joints = new List<SceneJoint>();
        internal double[] Start = Array.Empty<double>();
        internal readonly double[] Q = new double[8];
        internal readonly Dictionary<string, DigitalPin> Pins = new Dictionary<string, DigitalPin>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Wired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal bool LastStart;
        internal bool AutoStart;
        internal bool Booting = true;

        public MachineTool? Tool { get; internal set; }

        /// <summary>Measured joint values (rad / m).</summary>
        public double[] Measured => Q.Take(Model.Dof).ToArray();

        /// <summary>Base frame (world).</summary>
        public Transform3 BasePose { get; internal set; }

        /// <summary>World pose of every link frame (zero-configuration frame moved with the link).</summary>
        public Transform3[] LinkFrames()
        {
            var r = new Transform3[Links.Count];
            for (int i = 0; i < Links.Count; i++)
            {
                var spec = Model.Links[i];
                var q = Links[i].Orientation * WorldPhysics.ShapeTurnOf(spec.Shape).Conjugate;
                r[i] = new Transform3(Links[i].Position - q.Rotate(spec.Center), q);
            }

            return r;
        }

        /// <summary>TCP pose in the world (measured).</summary>
        public Transform3 TcpWorld => BasePose * ArmKinematics.Forward(Model, Measured, Controller.Tool);

        /// <summary>Drive torque per joint (N·m / N) in the last step.</summary>
        public double[] Torques => Joints.Select(j => j.DriveTorque).ToArray();
    }

    /// <summary>An end effector mounted on an arm flange.</summary>
    public sealed class MachineTool
    {
        internal MachineTool(WorldObject o, bool vacuum)
        {
            Object = o;
            IsVacuum = vacuum;
        }

        public WorldObject Object { get; }
        public string ObjectId => Object.Id;
        public bool IsVacuum { get; }

        internal SceneBody Body = null!;
        internal readonly List<SceneBody> Fingers = new List<SceneBody>();
        internal readonly List<SceneJoint> FingerJoints = new List<SceneJoint>();
        internal SceneBody? Cup;
        internal SceneJoint? CupJoint;
        internal SceneJoint? Grip;
        internal double Stroke, Force, Speed, Diameter, VacuumKpa, CupRadius;
        internal int Cups = 1;
        internal double OpenGap;
        internal double StallTime;
        internal double SealTime;
        internal readonly HashSet<SceneBody> Own = new HashSet<SceneBody>();

        /// <summary>Close / vacuum on.</summary>
        public bool Command { get; internal set; }

        /// <summary>Part held: gripper blocked on a part, or vacuum sealed with pressure built up.</summary>
        public bool Holding { get; internal set; }

        /// <summary>Vacuum level now (kPa below ambient).</summary>
        public double Pressure { get; internal set; }

        /// <summary>Current holding force of the vacuum (N).</summary>
        public double HoldForce { get; internal set; }

        /// <summary>Pull the held part exerts on the cup (N), for the readout.</summary>
        public double Load { get; internal set; }

        /// <summary>Finger travel from fully open (m) per finger.</summary>
        public double[] FingerTravel => FingerJoints.Select(j => j.Position).ToArray();

        /// <summary>Bellows compression (m, ≤ 0).</summary>
        public double CupTravel => CupJoint?.Position ?? 0;

        /// <summary>The body currently held by suction.</summary>
        public SceneBody? HeldBody => Grip?.B;

        public double TotalStroke => Stroke;
    }

    public sealed class MachineConveyor
    {
        internal MachineConveyor(WorldObject o) => Object = o;
        public WorldObject Object { get; }
        public string ObjectId => Object.Id;
        internal SceneBody Belt = null!;
        internal readonly List<SceneBody> Own = new List<SceneBody>();
        internal double Length, Width, Height, Speed, FeedPeriod, BoxMass, NextFeed, GuideGap;
        internal Vec3 BoxSize;
        internal bool Feed, Configured, RunWired;
        internal DigitalPin? RunPin, SensorPin;
        internal int Spawned;

        public bool Running { get; internal set; }

        /// <summary>Starts/stops the belt (ignored while the RUN input is wired).</summary>
        public void SetRunning(bool run)
        {
            if (!RunWired) Running = run;
        }
        public bool SensorOn { get; internal set; }

        /// <summary>Belt travel (m) for the moving-belt texture.</summary>
        public double Travel { get; internal set; }
    }

    public sealed class MachineCurtain
    {
        internal MachineCurtain(WorldObject o) => Object = o;
        public WorldObject Object { get; }
        public string ObjectId => Object.Id;
        internal readonly List<SceneBody> Own = new List<SceneBody>();
        internal double Width, Height;
        internal int BeamCount;
        internal DigitalPin? Ossd;

        public bool Blocked { get; internal set; }
        public List<(Vec3 From, Vec3 To, bool Cut)> Beams { get; } = new List<(Vec3, Vec3, bool)>();

        /// <summary>Remaining time of a simulated intrusion (see <see cref="WorldPhysics.InterruptCurtain"/>).</summary>
        internal double PendingTest;
    }

    /// <summary>A part created at run time by a feeder (no world object).</summary>
    public sealed class SpawnedPart
    {
        public string Id { get; internal set; } = string.Empty;
        public SceneBody Body { get; internal set; } = null!;
        public Vec3 Size { get; internal set; }
        public string Source { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// [arm] Robot work cell machines in the world physics: each arm becomes a chain of link
    /// bodies joined by hinge/slider joints whose drives track the controller's reference with
    /// rated torque limits (so payload and torque matter); tools get finger bodies or a
    /// compliant suction cup; conveyors move parts through belt friction; light curtains
    /// ray-cast their beams and open the safety circuit.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private readonly List<MachineArm> _arms = new List<MachineArm>();
        private readonly List<MachineTool> _tools = new List<MachineTool>();
        private readonly List<MachineConveyor> _conveyors = new List<MachineConveyor>();
        private readonly List<MachineCurtain> _curtains = new List<MachineCurtain>();
        private readonly List<SpawnedPart> _spawned = new List<SpawnedPart>();
        private readonly List<(double Time, string ObjectId, string Text, bool Fault)> _machineLog = new List<(double, string, string, bool)>();
        private long _machineTick;

        public IReadOnlyList<MachineArm> Arms => _arms;
        public IReadOnlyList<MachineTool> Tools => _tools;
        public IReadOnlyList<MachineConveyor> Conveyors => _conveyors;
        public IReadOnlyList<MachineCurtain> Curtains => _curtains;
        public IReadOnlyList<SpawnedPart> SpawnedParts => _spawned;

        public MachineArm? ArmOf(string objectId) => _arms.FirstOrDefault(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
        public MachineTool? ToolOf(string objectId) => _tools.FirstOrDefault(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
        public MachineConveyor? ConveyorOf(string objectId) => _conveyors.FirstOrDefault(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));
        public MachineCurtain? CurtainOf(string objectId) => _curtains.FirstOrDefault(a => string.Equals(a.ObjectId, objectId, StringComparison.OrdinalIgnoreCase));

        /// <summary>Messages of the cell machines since the last call (time, object, text, is a fault).</summary>
        public List<(double Time, string ObjectId, string Text, bool Fault)> DrainMachineMessages()
        {
            var r = _machineLog.ToList();
            _machineLog.Clear();
            return r;
        }

        private void MachineSay(string objectId, string text, bool fault) => _machineLog.Add((Scene.Time, objectId, text, fault));

        internal static Quat ShapeTurnOf(string shape) => shape switch
        {
            "cylinder-x" => Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2),
            "cylinder-y" => Quat.FromAxisAngle(Vec3.UnitX, -Math.PI / 2),
            _ => Quat.Identity
        };

        private static SceneBody LinkBody(string id, ArmLinkSpec spec, string material)
        {
            var s = spec.Size;
            return spec.Shape switch
            {
                "cylinder-x" => SceneBody.Cylinder(id, Math.Max(s.Y, s.Z) / 2, s.X, spec.Mass, default, material),
                "cylinder-y" => SceneBody.Cylinder(id, Math.Max(s.X, s.Z) / 2, s.Y, spec.Mass, default, material),
                "cylinder-z" => SceneBody.Cylinder(id, Math.Max(s.X, s.Y) / 2, s.Z, spec.Mass, default, material),
                _ => SceneBody.Box(id, s, spec.Mass, default, material)
            };
        }

        private static Dictionary<string, string> ParamsOf(WorldObject o)
        {
            var lib = PartLibrary.Default;
            var def = o.PartId != null ? lib.Find(o.PartId) : null;
            if (def == null) return new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase);
            var preset = o.PresetId != null ? lib.FindPreset(o.PresetId) : null;
            return lib.ResolveParameters(def, preset, o.Parameters);
        }

        private Transform3 FrameOf(string objectId)
        {
            var pose = PoseOf(objectId);
            return pose is { } p ? new Transform3(p.Position, p.Orientation) : Transform3.Identity;
        }

        private Dictionary<string, DigitalPin> IoPins(WorldObject o)
        {
            var map = new Dictionary<string, DigitalPin>(StringComparer.OrdinalIgnoreCase);
            if (_circuit?.ComponentRef(o.Id) is not { } r) return map;
            foreach (var d in _circuit.Compiled.DevicesOf(r))
            {
                if (d is DigitalPin dp && dp.Id.StartsWith(r + ".", StringComparison.OrdinalIgnoreCase)) map[dp.Id.Substring(r.Length + 1)] = dp;
            }

            return map;
        }

        private static HashSet<string> WiredPins(WorldDocument world, WorldObject o)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in world.Cables)
            {
                if (string.Equals(c.A.ObjectId, o.Id, StringComparison.OrdinalIgnoreCase)) set.Add(c.A.Pin);
                if (string.Equals(c.B.ObjectId, o.Id, StringComparison.OrdinalIgnoreCase)) set.Add(c.B.Pin);
            }

            return set;
        }

        // ================================================================ build

        private void BuildMachines(WorldDocument world)
        {
            foreach (var o in world.Objects)
            {
                if (o.Kind != WorldObjectKind.Component || !CellParts.IsCellPart(o.PartId) || !_items.TryGetValue(o.Id, out var it)) continue;
                var p = ParamsOf(o);
                switch (o.PartId)
                {
                    case CellParts.ArmId:
                        BuildArm(world, o, it, p);
                        break;
                    case CellParts.ConveyorId:
                        BuildConveyor(world, o, it, p);
                        break;
                    case CellParts.CurtainId:
                        BuildCurtain(o, it, p);
                        break;
                    case CellParts.ModbusIoId:
                        BuildModbus(o, p);   // [modbus] Sandbox/WorldPhysics.Modbus.cs
                        break;
                    case CellParts.PhotoId:
                        BuildPhoto(o, it, p);   // [cell] Sandbox/WorldPhysics.Switches.cs
                        break;
                    case CellParts.LimitId:
                        BuildLimit(o, it, p);
                        break;
                    case CellParts.PalletId:
                        it.Body.Material = "wood";
                        break;
                    case CellParts.FenceId:
                    case CellParts.PedestalId:
                        it.Body.Material = "steel";
                        it.Body.IsStatic = it.Body.IsStatic || !world.Joints.Any(j => string.Equals(j.ObjectA, o.Id, StringComparison.OrdinalIgnoreCase));
                        break;
                }
            }

            // Tools last: they sit on the arms' flanges.
            foreach (var j in world.Joints)
            {
                if (j.ObjectB == null || !string.Equals(j.PortB, "flange", StringComparison.OrdinalIgnoreCase)) continue;
                var arm = ArmOf(j.ObjectB);
                var o = world.Find(j.ObjectA);
                if (arm == null || o == null || !CellParts.IsTool(o.PartId) || !_items.TryGetValue(o.Id, out var it)) continue;
                BuildTool(o, it, arm, ParamsOf(o));
            }

        }

        /// <summary>Controllers read their inputs only after this boot time (the I/O circuit settles first).</summary>
        public const double ControllerBoot = 0.05;

        private void BuildArm(WorldDocument world, WorldObject o, Item it, Dictionary<string, string> p)
        {
            var model = ArmCatalog.Get(p.TryGetValue("model", out var id) ? id : null);
            var q0 = ArmProgram.ParseJoints(model, p.TryGetValue("q0", out var qs) ? qs : null) ?? model.Home.ToArray();
            if (it.Rotor != null)
            {
                Scene.Bodies.Remove(it.Rotor);
                if (it.RotorJoint != null) Scene.Joints.Remove(it.RotorJoint);
                it.RotorJoint = null;
                it.Rotor = null;
            }

            bool mounted = world.Joints.Any(j => string.Equals(j.ObjectA, o.Id, StringComparison.OrdinalIgnoreCase));
            if (!mounted) it.Body.IsStatic = true;
            it.Body.Material = "steel";
            var controller = new ArmController(model, q0)
            {
                Shape = p.TryGetValue("profile", out var prof) && prof.StartsWith("trap", StringComparison.OrdinalIgnoreCase) ? ProfileShape.Trapezoid : ProfileShape.SCurve,
                Loop = CellParts.Flag(p, "loop", true),
                Override = Math.Max(0.01, Math.Min(1, CellParts.Num(p, "override", 100) / 100)),
                AutoResumeAfterSafety = p.TryGetValue("safety_restart", out var sr) && sr.StartsWith("oto", StringComparison.OrdinalIgnoreCase)
            };
            var arm = new MachineArm(o, model, controller) { BaseBody = it.Body, Start = q0, AutoStart = CellParts.Flag(p, "autostart", false) };
            var basePose = FrameOf(o.Id);
            arm.BasePose = basePose;
            var T = ArmKinematics.LinkTransforms(model, q0);
            for (int i = 0; i < model.Dof; i++)
            {
                var spec = model.Links[i];
                var frame = basePose * T[i];
                var body = LinkBody(o.Id + ":L" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), spec, model.ServoDriven ? "abs" : "aluminium");
                body.Orientation = (frame.Q * ShapeTurnOf(spec.Shape)).Normalized();
                body.Position = frame.Apply(spec.Center);
                body.Tag = o;
                body.LinearDrag = 0;
                body.AngularDrag = 0;
                Scene.AddBody(body);
                arm.Links.Add(body);
            }

            for (int i = 0; i < model.Dof; i++)
            {
                var spec = model.Joints[i];
                var parentFrame = i == 0 ? basePose : basePose * T[i - 1];
                var axis = parentFrame.ApplyVector(spec.Axis).Normalized();
                var point = parentFrame.Apply(spec.Point);
                // Anchor on the axis next to the moving link: better conditioned than a far-away point.
                var child = arm.Links[i];
                var anchor = point + axis * (child.Position - point).Dot(axis);
                var parent = i == 0 ? it.Body : arm.Links[i - 1];
                var joint = Scene.AddJoint(spec.IsPrismatic ? JointKind.Slider : JointKind.Hinge, parent, child, anchor, axis, o.Id + ":J" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                joint.Lower = spec.Min - q0[i];
                joint.Upper = spec.Max - q0[i];
                joint.Drive = JointDrive.Velocity;
                joint.Damping = 0;
                joint.TargetSpeed = 0;
                joint.MaxTorque = spec.MaxTorque;
                joint.Friction = spec.Friction;
                arm.Joints.Add(joint);
            }

            // The last link is the flange: tools fixed to the "flange" port weld onto it.
            it.Rotor = arm.Links[arm.Links.Count - 1];
            it.PortName = "flange";

            foreach (var kv in IoPins(o)) arm.Pins[kv.Key] = kv.Value;
            foreach (var w in WiredPins(world, o)) arm.Wired.Add(w);
            o.Parameters.TryGetValue("program", out var program);
            o.Parameters.TryGetValue("points", out var points);
            if (!string.IsNullOrWhiteSpace(program)) controller.Load(ArmProgram.Parse(program, points, model));
            controller.PalletFull += () => ClearPallet(arm);
            _arms.Add(arm);
        }

        private void BuildTool(WorldObject o, Item it, MachineArm arm, Dictionary<string, string> p)
        {
            bool vacuum = o.PartId == CellParts.VacuumId;
            var tool = new MachineTool(o, vacuum) { Body = it.Body };
            // Seat the tool on the flange (its frame = the flange frame, +Z out of the flange).
            var flange = arm.BasePose * ArmKinematics.Forward(arm.Model, arm.Start);
            it.Body.Orientation = (flange.Q * it.ShapeTurn).Normalized();
            it.Body.Position = flange.Apply(it.Centre);
            it.Body.IsStatic = false;
            it.Body.Material = "aluminium";
            tool.Own.Add(it.Body);
            foreach (var l in arm.Links) tool.Own.Add(l);
            tool.Own.Add(arm.BaseBody);

            if (!vacuum)
            {
                tool.Stroke = Math.Max(0.004, CellParts.Num(p, "stroke", 60) / 1000);
                tool.Force = Math.Max(1, CellParts.Num(p, "force", 80));
                tool.Speed = Math.Max(0.005, CellParts.Num(p, "speed", 60) / 1000);
                string pad = p.TryGetValue("pad", out var pd) && pd.StartsWith("al", StringComparison.OrdinalIgnoreCase) ? "aluminium" : "rubber";
                tool.OpenGap = ToolGeometry.FingerMinGap + tool.Stroke;
                double zc = ToolGeometry.GripperHousingZ + ToolGeometry.FingerZ / 2;
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    var f = SceneBody.Box(o.Id + (side < 0 ? ":F1" : ":F2"), new Vec3(ToolGeometry.FingerX, ToolGeometry.FingerY, ToolGeometry.FingerZ), 0.06, default, pad);
                    var local = new Vec3(0, side * (tool.OpenGap / 2 + ToolGeometry.FingerY / 2), zc);
                    f.Position = flange.Apply(local);
                    f.Orientation = flange.Q;
                    f.Tag = o;
                    Scene.AddBody(f);
                    // Positive travel closes: the axis points toward the centre line.
                    var axis = flange.ApplyVector(new Vec3(0, -side, 0));
                    var j = Scene.AddJoint(JointKind.Slider, it.Body, f, f.Position, axis, o.Id + (side < 0 ? ":S1" : ":S2"));
                    j.Lower = 0;
                    j.Upper = tool.Stroke / 2;
                    j.Drive = JointDrive.Velocity;
                    j.Damping = 0;
                    j.MaxTorque = tool.Force;
                    j.TargetSpeed = -tool.Speed;
                    j.Friction = 1.0;
                    tool.Fingers.Add(f);
                    tool.FingerJoints.Add(j);
                    tool.Own.Add(f);
                }
            }
            else
            {
                tool.Diameter = Math.Max(0.01, CellParts.Num(p, "diameter", 50) / 1000);
                tool.VacuumKpa = Math.Max(1, Math.Min(95, CellParts.Num(p, "vacuum", 65)));
                tool.Cups = (int)Math.Max(1, CellParts.Num(p, "cups", 1));
                tool.CupRadius = tool.Diameter / 2;
                var layout = ToolGeometry.CupLayout(tool.Cups, tool.Diameter);
                double minX = layout.Min(c => c.X) - tool.CupRadius, maxX = layout.Max(c => c.X) + tool.CupRadius;
                double minY = layout.Min(c => c.Y) - tool.CupRadius, maxY = layout.Max(c => c.Y) + tool.CupRadius;
                double zc = ToolGeometry.VacuumMountZ + ToolGeometry.VacuumStemZ + ToolGeometry.VacuumCupZ / 2;
                var local = new Vec3((minX + maxX) / 2, (minY + maxY) / 2, zc);
                var cup = tool.Cups == 1
                    ? SceneBody.Cylinder(o.Id + ":CUP", tool.CupRadius, ToolGeometry.VacuumCupZ, 0.03, default, "rubber")
                    : SceneBody.Box(o.Id + ":CUP", new Vec3(maxX - minX, maxY - minY, ToolGeometry.VacuumCupZ), 0.03 * tool.Cups, default, "rubber");
                cup.Position = flange.Apply(local);
                cup.Orientation = flange.Q;
                cup.Tag = o;
                Scene.AddBody(cup);
                var axis = flange.ApplyVector(Vec3.UnitZ);
                var j = Scene.AddJoint(JointKind.Slider, it.Body, cup, cup.Position, axis, o.Id + ":BELLOWS");
                // Bellows: spring-loaded, compresses up to 12 mm when pressed onto a part.
                j.Lower = -ToolGeometry.VacuumStroke;
                j.Upper = 0;
                j.Drive = JointDrive.Position;
                j.TargetAngle = 0.002;
                j.Stiffness = 900 * tool.Cups;
                j.Damping = 25 * tool.Cups;
                tool.Cup = cup;
                tool.CupJoint = j;
                tool.Own.Add(cup);
            }

            arm.Tool = tool;
            arm.Controller.Tool = ToolGeometry.Tcp(o.PartId);
            arm.Controller.ToolHolding = () => tool.Holding;
            _tools.Add(tool);
        }

        private void BuildConveyor(WorldDocument world, WorldObject o, Item it, Dictionary<string, string> p)
        {
            var cv = new MachineConveyor(o)
            {
                Length = Math.Max(0.2, CellParts.Num(p, "length", 1.6)),
                Width = Math.Max(0.05, CellParts.Num(p, "width", 0.3)),
                Height = Math.Max(0.05, CellParts.Num(p, "height", 0.6)),
                Speed = CellParts.Num(p, "speed", 0.2),
                FeedPeriod = Math.Max(0.5, CellParts.Num(p, "feed_period", 6)),
                BoxMass = Math.Max(0.01, CellParts.Num(p, "box_mass", 0.8)),
                Feed = p.TryGetValue("feed", out var fd) && fd.StartsWith("kut", StringComparison.OrdinalIgnoreCase),
                Running = CellParts.Flag(p, "running", true),
                Belt = it.Body
            };
            var (bx, by, bz) = CellParts.Size3(p, "box", (0.16, 0.12, 0.10));
            cv.BoxSize = new Vec3(bx, by, bz);
            cv.NextFeed = 0.2;
            var frame = FrameOf(o.Id);
            // The belt: exact top surface at the belt height.
            var belt = it.Body;
            belt.IsStatic = true;
            belt.Material = "rubber";
            belt.Shape = CollisionShape.Box(new Vec3(cv.Length, cv.Width, cv.Height));
            it.Centre = new Vec3(0, 0, cv.Height / 2);
            it.ShapeTurn = Quat.Identity;
            belt.Orientation = frame.Q;
            belt.Position = frame.Apply(it.Centre);
            belt.Shape.MarkFloorFaces(belt.Position, belt.Orientation);
            cv.Own.Add(belt);
            // Side guides (low-friction UHMW) and the end stop.
            cv.GuideGap = cv.Feed ? Math.Min(cv.Width, by + 0.03) : cv.Width;
            foreach (double side in new[] { -1.0, 1.0 })
            {
                // Rails reach a little into the belt so nothing can wedge underneath them.
                var g = SceneBody.StaticBox(o.Id + ":guide", new Vec3(cv.Length, 0.012, 0.06), frame.Apply(new Vec3(0, side * (cv.GuideGap / 2 + 0.006), cv.Height + 0.02)), frame.Q, "ptfe");
                g.Tag = o;
                Scene.AddBody(g);
                cv.Own.Add(g);
            }

            if (CellParts.Flag(p, "stopper", true))
            {
                var s = SceneBody.StaticBox(o.Id + ":stop", new Vec3(0.02, cv.Width, 0.10), frame.Apply(new Vec3(cv.Length / 2 - 0.01, 0, cv.Height + 0.03)), frame.Q, "ptfe");
                s.Tag = o;
                Scene.AddBody(s);
                cv.Own.Add(s);
            }

            var pins = IoPins(o);
            pins.TryGetValue("RUN", out cv.RunPin);
            pins.TryGetValue("SENSOR", out cv.SensorPin);
            cv.RunWired = WiredPins(world, o).Contains("RUN");
            _conveyors.Add(cv);
        }

        private void BuildCurtain(WorldObject o, Item it, Dictionary<string, string> p)
        {
            var lc = new MachineCurtain(o)
            {
                Width = Math.Max(0.1, CellParts.Num(p, "width", 0.9)),
                Height = Math.Max(0.1, CellParts.Num(p, "height", 1.2)),
                BeamCount = (int)Math.Max(2, Math.Min(64, CellParts.Num(p, "beams", 12)))
            };
            // The bounding body would block its own beams: only the two posts are solid.
            it.Body.Collides = false;
            it.Body.SensorVisible = false;
            it.Body.IsStatic = true;
            lc.Own.Add(it.Body);
            var frame = FrameOf(o.Id);
            double h = CurtainBase + lc.Height + 0.08;
            foreach (double side in new[] { -1.0, 1.0 })
            {
                var post = SceneBody.StaticBox(o.Id + ":post", new Vec3(0.05, 0.05, h), frame.Apply(new Vec3(0, side * (lc.Width / 2 + 0.025), h / 2)), frame.Q, "aluminium");
                post.Tag = o;
                Scene.AddBody(post);
                lc.Own.Add(post);
            }

            IoPins(o).TryGetValue("OSSD", out lc.Ossd);
            Drive(lc.Ossd, true);
            _curtains.Add(lc);
        }

        /// <summary>Height of the lowest curtain beam above the floor (m).</summary>
        public const double CurtainBase = 0.25;

        // ================================================================ step

        private static bool PinHigh(DigitalPin? pin, AnalogSimulator? sim)
        {
            if (pin == null || sim == null) return false;
            double v = sim.NodeVoltage(pin.Pin) - (pin.Ground > 0 ? sim.NodeVoltage(pin.Ground) : 0);
            double vs = sim.NodeVoltage(pin.Supply) - (pin.Ground > 0 ? sim.NodeVoltage(pin.Ground) : 0);
            return v > 0.5 * Math.Max(1.0, vs);
        }

        private static void Drive(DigitalPin? pin, bool high)
        {
            if (pin == null) return;
            pin.Mode = high ? PinMode.OutputHigh : PinMode.OutputLow;
        }

        private void StepMachines(AnalogSimulator? sim)
        {
            if (_arms.Count == 0 && _conveyors.Count == 0 && _curtains.Count == 0 && _modbus.Count == 0 && _photos.Count == 0 && _limits.Count == 0) return;
            _machineTick++;
            foreach (var ph in _photos) StepPhoto(ph, Step);
            foreach (var ls in _limits) StepLimit(ls);
            foreach (var m in _modbus) StepModbus(m, sim);
            double dt = Step;
            foreach (var cv in _conveyors) StepConveyor(cv, sim, dt);
            if (_machineTick % 2 == 0)
            {
                foreach (var lc in _curtains) StepCurtain(lc, dt * 2);
            }

            foreach (var arm in _arms) StepArm(arm, sim, dt);
            foreach (var tool in _tools) StepTool(tool, dt);
            if (_machineTick % 500 == 0) CleanupSpawned();
        }

        private void StepArm(MachineArm arm, AnalogSimulator? sim, double dt)
        {
            var c = arm.Controller;
            var model = arm.Model;
            for (int i = 0; i < model.Dof; i++) arm.Q[i] = arm.Start[i] + arm.Joints[i].Angle;
            arm.BasePose = new Transform3(arm.BaseBody.Position - (arm.BaseBody.Orientation * _items[arm.ObjectId].ShapeTurn.Conjugate).Rotate(_items[arm.ObjectId].Centre),
                (arm.BaseBody.Orientation * _items[arm.ObjectId].ShapeTurn.Conjugate).Normalized());

            if (Scene.Time < ControllerBoot)
            {
                // Booting: hold the start pose.
                for (int i = 0; i < model.Dof; i++)
                {
                    arm.Joints[i].TargetSpeed = 50 * (c.Reference[i] - arm.Q[i]);
                    arm.Joints[i].MaxTorque = model.Joints[i].MaxTorque;
                }

                arm.Booting = true;
                return;
            }

            if (arm.Booting)
            {
                arm.Booting = false;
                if (arm.AutoStart) c.Start();
            }

            // ---- inputs
            if (sim != null && arm.Pins.Count > 0)
            {
                for (int k = 0; k < 4; k++)
                {
                    if (arm.Pins.TryGetValue("DI" + (k + 1), out var pin)) c.DigitalIn[k] = PinHigh(pin, sim);
                }

                bool start = arm.Wired.Contains("START") && PinHigh(arm.Pins.GetValueOrDefault("START"), sim);
                if (start && !arm.LastStart)
                {
                    if (c.State == ArmState.Paused || c.State == ArmState.SafetyStop) c.Resume();
                    else c.Start();
                }

                arm.LastStart = start;
                if (arm.Wired.Contains("STOP") && PinHigh(arm.Pins.GetValueOrDefault("STOP"), sim)) c.Pause();
            }

            bool safe;
            if (arm.Wired.Contains("SAFE")) safe = sim != null && PinHigh(arm.Pins.GetValueOrDefault("SAFE"), sim);
            else safe = _curtains.All(lc => !lc.Blocked);
            c.SafetyOk = safe;

            c.Update(dt, arm.Q);

            // ---- drives: velocity loop with position feedback, torque limited by the drive rating.
            bool brake = c.State == ArmState.Fault;
            for (int i = 0; i < model.Dof; i++)
            {
                var spec = model.Joints[i];
                var j = arm.Joints[i];
                double qref = c.Reference[i], qd = c.ReferenceRate[i];
                double kp = spec.IsPrismatic ? 60 : 50;
                double v = brake ? 0 : qd + kp * (qref - arm.Q[i]);
                double vmax = spec.MaxSpeed * 1.25;
                j.TargetSpeed = Math.Max(-vmax, Math.Min(vmax, v));
                j.MaxTorque = spec.MaxTorque * (brake ? 1.5 : 1.0);
                double load = Math.Abs(j.DriveTorque) / spec.MaxTorque;
                c.JointLoad[i] += (load - c.JointLoad[i]) * Math.Min(1, dt / 0.05);
            }

            // ---- outputs
            if (arm.Pins.Count > 0)
            {
                for (int k = 0; k < 4; k++) Drive(arm.Pins.GetValueOrDefault("DO" + (k + 1)), c.DigitalOut[k]);
                Drive(arm.Pins.GetValueOrDefault("BUSY"), c.State == ArmState.Running);
                Drive(arm.Pins.GetValueOrDefault("FAULT"), c.State == ArmState.Fault);
            }

            if (arm.Tool != null) arm.Tool.Command = c.ToolCommand;
            // [robot-control] the part in the tool is the payload the controller plans its moves for
            c.PayloadKg = arm.Tool is { Holding: true, HeldBody: { } held } ? held.Mass : 0;
            foreach (var (_, text, fault) in c.DrainMessages()) MachineSay(arm.ObjectId, text, fault);
        }

        private void StepTool(MachineTool tool, double dt)
        {
            if (!tool.IsVacuum)
            {
                bool closing = tool.Command;
                bool stalled = true;
                foreach (var j in tool.FingerJoints)
                {
                    j.TargetSpeed = closing ? tool.Speed : -tool.Speed;
                    j.MaxTorque = tool.Force;
                    // Blocked by a part: stopped clearly short of fully closed.
                    if (!(j.Position < tool.Stroke / 2 - 0.0015 && Math.Abs(j.Speed) < 0.004 && j.Position > 0.0005)) stalled = false;
                }

                tool.StallTime = closing && stalled ? tool.StallTime + dt : 0;
                tool.Holding = closing && tool.StallTime > 0.05;
                return;
            }

            // ---- vacuum: seal on a flat face, pressure builds, hold force = ΔP·A·cups.
            var cup = tool.Cup!;
            var axis = cup.Orientation.Rotate(Vec3.UnitZ);
            double area = Math.PI * tool.CupRadius * tool.CupRadius * tool.Cups;
            if (tool.Command)
            {
                if (tool.Grip == null)
                {
                    tool.Pressure += (0.08 * tool.VacuumKpa - tool.Pressure) * Math.Min(1, dt / 0.05);   // open cup: only a weak flow vacuum
                    if (TrySeal(tool, axis, out var part, out var at))
                    {
                        var j = Scene.AddJoint(JointKind.Fixed, cup, part!, at, axis, tool.ObjectId + ":VAC");
                        tool.Grip = j;
                        tool.SealTime = 0;
                    }
                }
                else
                {
                    tool.SealTime += dt;
                    tool.Pressure += (tool.VacuumKpa - tool.Pressure) * Math.Min(1, dt / 0.06);
                }
            }
            else
            {
                tool.Pressure += (0 - tool.Pressure) * Math.Min(1, dt / 0.03);   // blow-off valve
            }

            // Effective holding force (≈ 80 % of the ideal ΔP·A: lip leakage, bellows stiffness).
            tool.HoldForce = 0.8 * tool.Pressure * 1000 * area;
            tool.Holding = tool.Grip != null && tool.Pressure > 0.7 * tool.VacuumKpa;
            if (tool.Grip == null)
            {
                tool.Load = 0;
                return;
            }

            if (!tool.Command && tool.Pressure < 0.12 * tool.VacuumKpa)
            {
                Release(tool);
                return;
            }

            // Pull on the seal: tension along the cup axis, shear through friction, tilting moment through the lip.
            var n = -axis;
            var f = tool.Grip.ReactionForce;
            double tension = Math.Max(0, f.Dot(n));
            double shear = (f - n * f.Dot(n)).Length;
            var tq = tool.Grip.ReactionTorque;
            double tilt = (tq - n * tq.Dot(n)).Length;
            // Pressing the part on only tightens the seal; pulling and peeling (tilt) open it, sliding needs friction.
            double compression = Math.Max(0, -f.Dot(n));
            double pull = tension + tilt / Math.Max(0.005, tool.CupRadius * (tool.Cups > 1 ? 2.5 : 1));
            double grip = tool.HoldForce + compression;
            tool.Load = pull + shear / 0.5;
            bool lost = pull > grip || shear > 0.5 * Math.Max(0, grip - tension);
            if (tool.Command && tool.SealTime > 0.12 && lost)
            {
                double equivalent = Math.Max(pull, shear / 0.5);
                var line = _arms.FirstOrDefault(a => a.Tool == tool)?.Controller.CurrentInstruction;
                MachineSay(tool.ObjectId, $"Vakum tutuşu kaçtı: parça {equivalent:0} N çekiyor, vantuz {tool.HoldForce:0} N tutabiliyor" +
                    (line != null ? $" (satır {line.Line}: {line.Text})" : string.Empty) + $" [çekme {tension:0} N, kesme {shear:0} N, devrilme {tilt:0.0} N·m, baskı {compression:0} N]", true);
                Release(tool);
            }
        }

        private void Release(MachineTool tool)
        {
            if (tool.Grip != null) Scene.Joints.Remove(tool.Grip);
            tool.Grip = null;
            tool.Holding = false;
            tool.Load = 0;
        }

        /// <summary>All cup centres sit on one flat face of a loose part, within a few millimetres, square to the cup.</summary>
        private bool TrySeal(MachineTool tool, Vec3 axis, out SceneBody? part, out Vec3 at)
        {
            part = null;
            at = Vec3.Zero;
            var cup = tool.Cup!;
            var layout = ToolGeometry.CupLayout(tool.Cups, tool.Diameter);
            double minX = layout.Min(c => c.X) - tool.CupRadius, maxX = layout.Max(c => c.X) + tool.CupRadius;
            double minY = layout.Min(c => c.Y) - tool.CupRadius, maxY = layout.Max(c => c.Y) + tool.CupRadius;
            var centre = new Vec3((minX + maxX) / 2, (minY + maxY) / 2, 0);
            var sum = Vec3.Zero;
            foreach (var (x, y) in layout)
            {
                // Cup face in the cup body's frame (cup body centred on the layout, Z = approach).
                var face = cup.Position + cup.Orientation.Rotate(new Vec3(x - centre.X, y - centre.Y, ToolGeometry.VacuumCupZ / 2));
                if (!Scene.Raycast(face - axis * 0.004, axis, 0.009, out var hit, b => !tool.Own.Contains(b) && !b.IsStatic, ground: false)) return false;
                if (hit.Body == null || hit.Distance > 0.004 + 0.0035) return false;
                if (hit.Normal.Dot(-axis) < Math.Cos(12 * Math.PI / 180)) return false;
                if (part != null && hit.Body != part) return false;
                part = hit.Body;
                sum += hit.Point;
            }

            at = sum / layout.Count;
            return part != null;
        }

        private void StepConveyor(MachineConveyor cv, AnalogSimulator? sim, double dt)
        {
            bool run = cv.RunWired ? PinHigh(cv.RunPin, sim) : cv.Running;
            cv.Running = cv.RunWired ? run : cv.Running;
            var frame = new Transform3(cv.Belt.Position - cv.Belt.Orientation.Rotate(new Vec3(0, 0, cv.Height / 2)), cv.Belt.Orientation);
            cv.Belt.SurfaceVelocity = run ? frame.ApplyVector(Vec3.UnitX) * cv.Speed : Vec3.Zero;
            if (run) cv.Travel += cv.Speed * dt;

            if (cv.Feed && run && Scene.Time >= cv.NextFeed)
            {
                var spawn = frame.Apply(new Vec3(-cv.Length / 2 + cv.BoxSize.X / 2 + 0.03, 0, cv.Height + cv.BoxSize.Z / 2 + 0.002));
                bool free = true;
                foreach (var b in Scene.Bodies)
                {
                    if (b.IsStatic) continue;
                    var d = b.Position - spawn;
                    if (Math.Abs(d.Z) < cv.BoxSize.Z && Math.Sqrt(d.X * d.X + d.Y * d.Y) < Math.Max(cv.BoxSize.X, cv.BoxSize.Y) * 1.1)
                    {
                        free = false;
                        break;
                    }
                }

                if (free && _spawned.Count < 60)
                {
                    cv.Spawned++;
                    string id = cv.ObjectId + ":box" + cv.Spawned.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    var box = SceneBody.Box(id, cv.BoxSize, cv.BoxMass, spawn, "paper");
                    box.Orientation = frame.Q;
                    box.Velocity = frame.ApplyVector(Vec3.UnitX) * cv.Speed;
                    Scene.AddBody(box);
                    _spawned.Add(new SpawnedPart { Id = id, Body = box, Size = cv.BoxSize, Source = cv.ObjectId });
                    cv.NextFeed = Scene.Time + cv.FeedPeriod;
                }
                else
                {
                    cv.NextFeed = Scene.Time + 0.25;
                }
            }

            if (_machineTick % 5 == 0)
            {
                // Photo-eye across the belt just before the end stop.
                double x = cv.Length / 2 - 0.02 - 0.04;
                var from = frame.Apply(new Vec3(x, -cv.Width / 2 - 0.02, cv.Height + 0.025));
                var to = frame.Apply(new Vec3(x, cv.Width / 2 + 0.02, cv.Height + 0.025));
                var own = cv.Own;
                cv.SensorOn = Scene.Raycast(from, to - from, (to - from).Length, out _, b => !own.Contains(b) && !b.IsStatic, ground: false);
                Drive(cv.SensorPin, cv.SensorOn);
            }
        }

        private void StepCurtain(MachineCurtain lc, double dt)
        {
            var frame = FrameOf(lc.ObjectId);
            lc.Beams.Clear();
            bool blocked = false;
            var own = lc.Own;
            for (int k = 0; k < lc.BeamCount; k++)
            {
                double z = CurtainBase + lc.Height * k / Math.Max(1, lc.BeamCount - 1);
                var a = frame.Apply(new Vec3(0, -lc.Width / 2, z));
                var b = frame.Apply(new Vec3(0, lc.Width / 2, z));
                bool cut = Scene.Raycast(a, b - a, (b - a).Length, out var hit, x => !own.Contains(x) && x.SensorVisible, ground: false);
                lc.Beams.Add((a, cut ? hit.Point : b, cut));
                blocked |= cut;
            }

            if (lc.PendingTest > 0)
            {
                lc.PendingTest -= dt;
                blocked = true;
            }

            if (blocked && !lc.Blocked) MachineSay(lc.ObjectId, "Işık perdesi kesildi: güvenlik devresi açık", true);
            else if (!blocked && lc.Blocked) MachineSay(lc.ObjectId, "Işık perdesi serbest", false);
            lc.Blocked = blocked;
            Drive(lc.Ossd, !blocked);
        }

        /// <summary>Simulates a person reaching through a light curtain for a while.</summary>
        public void InterruptCurtain(string objectId, double seconds)
        {
            var lc = CurtainOf(objectId);
            if (lc != null) lc.PendingTest = Math.Max(lc.PendingTest, seconds);
        }

        /// <summary>Pallet full: the loaded pallet is taken away and an empty one put in its place (feeder parts placed in the cell are removed).</summary>
        private void ClearPallet(MachineArm arm)
        {
            var origin = arm.BasePose.P;
            double reach = arm.Model.Reach + 0.4;
            var held = new HashSet<SceneBody>(_tools.Where(t => t.HeldBody != null).Select(t => t.HeldBody!));
            int removed = 0;
            foreach (var s in _spawned.ToList())
            {
                var b = s.Body;
                var d = b.Position - origin;
                if (held.Contains(b) || Math.Sqrt(d.X * d.X + d.Y * d.Y) > reach || OnConveyor(b)) continue;
                RemoveSpawned(s);
                removed++;
            }

            MachineSay(arm.ObjectId, $"Palet değiştirildi ({removed} parça götürüldü)", false);
        }

        private bool OnConveyor(SceneBody b)
        {
            foreach (var cv in _conveyors)
            {
                var frame = new Transform3(cv.Belt.Position - cv.Belt.Orientation.Rotate(new Vec3(0, 0, cv.Height / 2)), cv.Belt.Orientation);
                var local = frame.Inverse().Apply(b.Position);
                if (Math.Abs(local.X) <= cv.Length / 2 + 0.05 && Math.Abs(local.Y) <= cv.Width / 2 + 0.05 && local.Z > cv.Height - 0.02 && local.Z < cv.Height + 0.4) return true;
            }

            return false;
        }

        private void RemoveSpawned(SpawnedPart s)
        {
            Scene.Joints.RemoveAll(j => j.A == s.Body || j.B == s.Body);
            foreach (var t in _tools)
            {
                if (t.Grip != null && (t.Grip.A == s.Body || t.Grip.B == s.Body)) t.Grip = null;
            }

            Scene.RemoveBody(s.Body);
            _spawned.Remove(s);
        }

        /// <summary>Feeder parts that fell off the world are removed.</summary>
        private void CleanupSpawned()
        {
            foreach (var s in _spawned.ToList())
            {
                if (s.Body.Position.Z < -2 || s.Body.Position.Length > 100) RemoveSpawned(s);
            }
        }
    }
}
