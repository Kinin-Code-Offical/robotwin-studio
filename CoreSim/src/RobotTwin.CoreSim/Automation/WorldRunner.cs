using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>Physical shape of world objects and cable lengths (the app takes them from its 3D models).</summary>
    public interface IWorldGeometry
    {
        WorldBodyGeometry GeometryOf(WorldObject o);
        double CableLength(WorldCable cable);
    }

    /// <summary>
    /// Headless run of a <see cref="WorldDocument"/> (.rwworld): the same compiled circuit, ATmega328P /
    /// ESP32 firmware co-simulation, I²C modules and <see cref="WorldPhysics"/> the app's world page runs,
    /// stepped deterministically. Radio links (IR/RF/Wi-Fi between boards), sounds and the USB-cable
    /// serial gating of the app are not part of the headless run.
    /// </summary>
    public sealed class WorldRunner : SimRunner
    {
        private enum PortMode
        {
            Voltage,
            Speed,
            Torque,
            Angle
        }

        private sealed class PortCommand
        {
            public PortMode Mode;
            public double Value;
            public double MaxTorque = double.PositiveInfinity;
            public DcMotor? Motor;
            public ServoMotor? Servo;
            public SceneJoint? Joint;
        }

        private readonly Dictionary<string, PortCommand> _ports = new Dictionary<string, PortCommand>(StringComparer.OrdinalIgnoreCase);
        private Arena? _floor;

        public WorldRunner(WorldDocument world, RunnerOptions? options = null, IWorldGeometry? geometry = null)
            : base(options ?? new RunnerOptions())
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Geometry = geometry ?? new ApproximateGeometry(world, Library);
            Reset();
        }

        public WorldDocument World { get; }
        public IWorldGeometry Geometry { get; }
        public WorldCircuit? Circuit { get; private set; }
        public WorldPhysics? Physics { get; private set; }
        public override string Kind => "world";
        public override string Title => World.Title;

        protected override void Build()
        {
            _ports.Clear();
            _floor = null;
            try
            {
                Circuit = WorldCompiler.Compile(World, Library, Geometry.CableLength);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is AnalogSimulationException)
            {
                throw new ApiException(ApiException.InvalidParams, "Dünya devresi kurulamadı / world circuit failed: " + ex.Message);
            }

            Compiled = Circuit.Compiled;
            foreach (var w in Compiled.Warnings) AddEvent(new RunnerEvent(0, "derleyici", RunnerEventKind.Warning, w));
            Simulator = StartSolver(Compiled.Circuit, World.Environment.AmbientC);
            AttachFirmware();
            try
            {
                Physics = WorldPhysics.Build(World, Geometry.GeometryOf, Circuit);
                Physics.DriveOverride = DrivePort;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Physics = null;
                AddEvent(new RunnerEvent(0, "fizik", RunnerEventKind.Warning, "Fizik sahnesi kurulamadı: " + ex.Message));
            }

            AddEvent(new RunnerEvent(0, "dünya", RunnerEventKind.Info,
                $"{World.Objects.Count} nesne, {World.Cables.Count} kablo, {Compiled.DevicesByPart.Count} parça tek devrede çözülüyor"));
        }

        protected override void Teardown()
        {
            base.Teardown();
            Physics = null;
            Circuit = null;
        }

        private void AttachFirmware()
        {
            foreach (var prog in Circuit!.Programmables)
            {
                var o = World.Find(prog.ObjectId);
                var board = new BoardInfo
                {
                    ObjectId = prog.ObjectId,
                    RefDes = prog.RefDes,
                    Name = (o?.Name ?? prog.ObjectId) + " " + prog.InnerRefDes,
                    PartId = prog.Definition.Id,
                    Mcu = IsEsp32Part(prog.Definition.Id) ? "esp32" : "atmega328p"
                };
                Boards.Add(board);
                if (!Options.Firmware)
                {
                    board.Firmware = "(kapalı / disabled)";
                    continue;
                }

                prog.Parameters.TryGetValue("firmware", out var fw);
                if (IsEsp32Part(prog.Definition.Id))
                {
                    string? source = prog.Firmware?.Source;
                    if (string.IsNullOrWhiteSpace(source) && !string.IsNullOrEmpty(fw))
                    {
                        var path = Options.ResolveFirmware(fw);
                        if (path != null && System.IO.File.Exists(path)) source = System.IO.File.ReadAllText(path);
                    }

                    board.Firmware = prog.Firmware?.FileName ?? fw ?? string.Empty;
                    if (AttachEsp32(board, source) is { } esp) AttachI2c(esp.I2c, prog.RefDes, World.Environment.Gravity);
                    continue;
                }

                string? hex = OverrideHex(prog.RefDes, prog.ObjectId) ?? prog.Firmware?.Hex;
                board.Firmware = FirmwareOverrides.Count > 0 && OverrideHex(prog.RefDes, prog.ObjectId) != null ? "(API)" : prog.Firmware?.FileName ?? string.Empty;
                if (hex == null && !string.IsNullOrEmpty(fw))
                {
                    var path = Options.ResolveFirmware(fw);
                    if (path != null && System.IO.File.Exists(path) && path.EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
                    {
                        hex = System.IO.File.ReadAllText(path);
                        board.Firmware = System.IO.Path.GetFileName(path);
                    }
                }

                if (hex == null)
                {
                    AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Warning, "Yüklü yazılım yok (kart boşta)."));
                    continue;
                }

                string refDes = prog.RefDes;
                var cosim = AttachAvr(board, hex, () => Simulator == null ? 0 : Compiled!.PinVoltage(Simulator, refDes, "5V"));
                if (cosim != null) AttachI2c(cosim.I2c, prog.RefDes, World.Environment.Gravity);
            }
        }

        private string? OverrideHex(string refDes, string objectId) =>
            FirmwareOverrides.TryGetValue(refDes, out var h) ? h : FirmwareOverrides.TryGetValue(objectId, out h) ? h : null;

        protected override IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> AllParts()
        {
            foreach (var o in World.Objects)
            {
                if (o.Kind == WorldObjectKind.Board)
                {
                    var sch = World.FindDesign(o.DesignId)?.Package.Schematic;
                    if (sch == null) continue;
                    foreach (var p in sch.Parts)
                    {
                        var def = Library.Find(p.PartId);
                        if (def != null) yield return (WorldCircuit.Prefix(o.Id) + p.RefDes, def, sch.ResolveParameters(Library, p));
                    }
                }
                else if (o.Kind == WorldObjectKind.Component && o.PartId != null)
                {
                    var def = Library.Find(o.PartId);
                    var r = Circuit!.ComponentRef(o.Id);
                    if (def == null || r == null) continue;
                    var preset = o.PresetId != null ? Library.FindPreset(o.PresetId) : null;
                    yield return (r, def, Library.ResolveParameters(def, preset, o.Parameters));
                }
            }
        }

        protected override HashSet<int> Reach(int node)
        {
            var set = new HashSet<int> { node };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var cable in Circuit!.CableDevices.Values)
                {
                    if (set.Contains(cable.A) && set.Add(cable.B)) grew = true;
                    if (set.Contains(cable.B) && set.Add(cable.A)) grew = true;
                }
            }

            return set;
        }

        protected override string ObjectOfDevice(string deviceId)
        {
            if (deviceId.StartsWith("cable:", StringComparison.Ordinal)) return deviceId;
            int slash = deviceId.IndexOf('/');
            return slash > 0 ? deviceId.Substring(0, slash) : deviceId;
        }

        protected override void AdvanceMechanics(double dt) => Physics?.Advance(dt, Simulator);

        protected override void DrainMechanicsEvents()
        {
            if (Physics == null) return;
            foreach (var pe in Physics.DrainEvents())
            {
                bool broke = pe.Kind != PhysicsEventKind.Impact;
                AddEvent(new RunnerEvent(pe.Time, pe.Source, broke ? RunnerEventKind.Failure : RunnerEventKind.Impact, pe.Message, pe.Value));
                if (broke && World.Find(pe.Source) != null) Failed.Add(pe.Source);
            }
        }

        // ================================================================ objects and bodies

        public WorldObject ResolveObject(string key) =>
            World.Find(key) ?? World.Objects.FirstOrDefault(o => string.Equals(o.Name, key, StringComparison.OrdinalIgnoreCase))
            ?? throw ApiException.Missing("Nesne / object '" + key + "'");

        public override IEnumerable<string> ObjectIds() => World.Objects.Select(o => o.Id);

        public override bool TryGetBody(string objectId, out BodyState state)
        {
            state = default;
            if (Physics == null) return false;
            var pose = Physics.PoseOf(objectId);
            if (pose == null) return false;
            var (pos, q) = pose.Value;
            var body = Physics.Scene.FindBody(objectId);
            Vec3 v = Vec3.Zero, w = Vec3.Zero;
            if (body != null && !body.IsStatic)
            {
                w = body.AngularVelocity;
                v = body.Velocity + w.Cross(pos - body.Position);
            }

            state = new BodyState(pos, q, v, w);
            return true;
        }

        /// <summary>Bodies rigidly or jointly connected to an object's body (its assembly).</summary>
        public HashSet<SceneBody> AssemblyOf(string objectId)
        {
            var set = new HashSet<SceneBody>();
            if (Physics == null) return set;
            var start = Physics.Scene.FindBody(objectId);
            if (start == null) return set;
            set.Add(start);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var j in Physics.Scene.Joints)
                {
                    if (j.B == null) continue;
                    if (set.Contains(j.A) && set.Add(j.B)) grew = true;
                    if (set.Contains(j.B) && set.Add(j.A)) grew = true;
                }
            }

            return set;
        }

        public override bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, string? ignoreObject, out SceneRayHit hit)
        {
            hit = default;
            if (Physics == null) return false;
            var ignore = ignoreObject != null ? AssemblyOf(ignoreObject) : null;
            return Physics.Scene.Raycast(origin, direction, maxDistance, out hit, ignore == null || ignore.Count == 0 ? null : b => !ignore.Contains(b));
        }

        public override Vec3 Gravity => new Vec3(0, 0, -World.Environment.Gravity);

        public override Arena? Floor
        {
            get
            {
                if (_floor == null && (World.Environment.Track != "none" || World.Environment.Lines.Count > 0))
                {
                    try
                    {
                        _floor = World.Environment.ToArena();
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        _floor = null;
                    }
                }

                return _floor;
            }
        }

        public override (double R, double G, double B) ColorOf(SceneBody? body)
        {
            if (body == null) return (0.55, 0.55, 0.57);
            var o = body.Tag as WorldObject ?? World.Find(body.Id.Split(':')[0]);
            if (o == null) return body.Id == "obstacle" ? (0.62, 0.36, 0.25) : (0.6, 0.6, 0.62);
            if (TryParseColor(o.Color, out var c)) return c;
            if (o.Kind == WorldObjectKind.Board) return (0.12, 0.34, 0.19);
            if (o.Kind == WorldObjectKind.Component) return (0.22, 0.24, 0.30);
            switch ((o.Material ?? string.Empty).ToUpperInvariant())
            {
                case "KAUÇUK":
                case "KAUCUK":
                    return (0.14, 0.14, 0.15);
                case "AHŞAP":
                case "AHSAP":
                case "MDF":
                case "KONTRPLAK":
                    return (0.78, 0.63, 0.43);
                case "ALÜMİNYUM":
                case "ALUMINYUM":
                case "ÇELİK":
                case "CELIK":
                    return (0.7, 0.72, 0.75);
                case "FR4":
                    return (0.12, 0.34, 0.19);
                default:
                    return (0.86, 0.86, 0.84);
            }
        }

        private static bool TryParseColor(string? hex, out (double, double, double) c)
        {
            c = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            string h = hex!.Trim().TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            c = (((v >> 16) & 255) / 255.0, ((v >> 8) & 255) / 255.0, (v & 255) / 255.0);
            return true;
        }

        public override void SetPose(string objectId, Vec3? position, Quat? orientation, bool zeroVelocity)
        {
            var o = ResolveObject(objectId);
            if (Physics == null) throw new ApiException(ApiException.Unsupported, "Fizik yok / no physics");
            var current = Physics.PoseOf(o.Id) ?? throw ApiException.Missing("Gövde / body '" + o.Id + "'");
            var p = position ?? current.Position;
            var q = orientation ?? current.Orientation;
            var turn = (q * current.Orientation.Conjugate).Normalized();
            foreach (var b in AssemblyOf(o.Id))
            {
                b.Position = p + turn.Rotate(b.Position - current.Position);
                b.Orientation = (turn * b.Orientation).Normalized();
                if (zeroVelocity)
                {
                    b.Velocity = Vec3.Zero;
                    b.AngularVelocity = Vec3.Zero;
                }
            }
        }

        public override void Randomize(NoiseSource noise, double positionSigma, double yawSigmaRad)
        {
            if (Physics == null) return;
            var done = new HashSet<SceneBody>();
            foreach (var o in World.Objects)
            {
                if (o.IsStatic) continue;
                var body = Physics.Scene.FindBody(o.Id);
                if (body == null || done.Contains(body)) continue;
                var group = AssemblyOf(o.Id);
                foreach (var b in group) done.Add(b);
                var pose = Physics.PoseOf(o.Id)!.Value;
                double dx = noise.Gaussian(positionSigma), dy = noise.Gaussian(positionSigma), dyaw = noise.Gaussian(yawSigmaRad);
                SetPose(o.Id, pose.Position + new Vec3(dx, dy, 0), Quat.FromAxisAngle(Vec3.UnitZ, dyaw) * pose.Orientation, true);
            }
        }

        // ================================================================ actuators

        private (WorldObject Object, DcMotor? Motor, Servo? Servo) Actuated(WorldObject o)
        {
            if (Circuit == null || o.Kind != WorldObjectKind.Component) return (o, null, null);
            var r = Circuit.ComponentRef(o.Id);
            if (r == null) return (o, null, null);
            var devices = Compiled!.DevicesOf(r);
            return (o, devices.OfType<DcMotor>().FirstOrDefault(), devices.OfType<Servo>().FirstOrDefault());
        }

        public override JsonArray ListActuators()
        {
            var list = new JsonArray();
            foreach (var o in World.Objects)
            {
                var g = SafeGeometry(o);
                if (g?.PortName == null) continue;
                var (_, motor, servo) = Actuated(o);
                var a = new JsonObject
                {
                    ["name"] = o.Id,
                    ["label"] = o.Name,
                    ["object"] = o.Id,
                    ["type"] = motor != null ? "motor" : servo != null ? "servo" : "joint",
                    ["port"] = g.PortName,
                    ["modes"] = motor != null ? J.Strs(new[] { "voltage", "speed", "duty", "torque", "release" })
                        : servo != null ? J.Strs(new[] { "angle", "release" }) : J.Strs(new[] { "speed", "torque", "angle", "release" }),
                    ["commanded"] = _ports.ContainsKey(o.Id)
                };
                if (motor != null)
                {
                    a["gear_ratio"] = J.N(motor.GearRatio);
                    a["motor_constant"] = J.N(motor.MotorConstant);
                    a["resistance"] = J.N(motor.Resistance);
                }

                list.Add(a);
            }

            if (Compiled != null)
            {
                foreach (var d in Compiled.Circuit.Devices.OfType<AveragedPwmDriver>())
                {
                    list.Add(new JsonObject
                    {
                        ["name"] = d.Id,
                        ["label"] = d.Id,
                        ["object"] = ObjectOfDevice(d.Id),
                        ["type"] = "driver",
                        ["modes"] = J.Strs(new[] { "duty", "release" }),
                        ["commanded"] = DriverOverridden(d)
                    });
                }
            }

            return list;
        }

        private WorldBodyGeometry? SafeGeometry(WorldObject o)
        {
            try
            {
                return Geometry.GeometryOf(o);
            }
            catch (Exception ex) when (ex is FormatException || ex is InvalidOperationException || ex is ArgumentException)
            {
                return null;
            }
        }

        public override JsonObject SetActuator(string name, JsonObject p)
        {
            string mode = (J.Str(p, "mode") ?? string.Empty).ToLowerInvariant();
            var driver = Compiled?.Circuit.Devices.OfType<AveragedPwmDriver>().FirstOrDefault(d => string.Equals(d.Id, name, StringComparison.OrdinalIgnoreCase));
            if (driver != null)
            {
                if (mode == "release")
                {
                    ReleaseDriver(driver);
                    return new JsonObject { ["name"] = driver.Id, ["released"] = true };
                }

                double duty = J.Num(p, "value") ?? J.ReqNum(p, "duty");
                SetDriverDuty(driver, duty);
                return new JsonObject { ["name"] = driver.Id, ["type"] = "driver", ["duty"] = J.N(driver.Duty) };
            }

            var o = ResolveObject(name);
            if (mode == "release" || mode == "none")
            {
                ReleaseActuator(o.Id);
                return new JsonObject { ["name"] = o.Id, ["released"] = true };
            }

            var (_, motor, servo) = Actuated(o);
            double value = J.Num(p, "value") ?? double.NaN;
            var cmd = new PortCommand { Motor = motor };
            if (_ports.TryGetValue(o.Id, out var old)) cmd.Joint = old.Joint;
            if (string.IsNullOrEmpty(mode)) mode = servo != null ? "angle" : "voltage";
            double supply = J.Num(p, "supply", 6.0);
            switch (mode)
            {
                case "voltage":
                    if (motor == null) throw new ApiException(ApiException.Unsupported, o.Name + ": DC motor değil / not a DC motor");
                    cmd.Mode = PortMode.Voltage;
                    cmd.Value = Need(value, "value");
                    break;
                case "duty":
                    if (motor == null) throw new ApiException(ApiException.Unsupported, o.Name + ": DC motor değil / not a DC motor");
                    cmd.Mode = PortMode.Voltage;
                    cmd.Value = Math.Max(-1, Math.Min(1, Need(value, "value"))) * supply;
                    break;
                case "speed":
                    cmd.Mode = PortMode.Speed;
                    cmd.Value = Need(value, "value");
                    cmd.MaxTorque = J.Num(p, "max_torque") ?? (motor != null ? StallTorque(motor, supply) : 1.0);
                    break;
                case "torque":
                    cmd.Mode = PortMode.Torque;
                    cmd.Value = Need(value, "value");
                    break;
                case "angle":
                    cmd.Mode = PortMode.Angle;
                    double deg = J.Num(p, "value") ?? J.ReqNum(p, "angle");
                    cmd.Value = (deg - 90.0) * Math.PI / 180.0;
                    o.Parameters.TryGetValue("model", out var model);
                    cmd.Servo = ServoMotor.ForModel(model ?? o.PresetId);
                    if (servo == null) cmd.Servo.StallTorque = J.Num(p, "max_torque", 1.0);
                    cmd.Servo.SupplyVoltage = cmd.Servo.RatedVoltage;
                    cmd.Servo.CommandAngle = cmd.Value;
                    break;
                default:
                    throw ApiException.BadParam("mode", "voltage | duty | speed | torque | angle | release");
            }

            if (SafeGeometry(o)?.PortName == null) throw new ApiException(ApiException.Unsupported, o.Name + ": hareketli çıkışı yok / has no moving port");
            _ports[o.Id] = cmd;
            return new JsonObject { ["name"] = o.Id, ["type"] = motor != null ? "motor" : servo != null ? "servo" : "joint", ["mode"] = mode, ["value"] = J.N(cmd.Value) };
        }

        private static double Need(double v, string name) => double.IsNaN(v) ? throw ApiException.BadParam(name, "gerekli / required") : v;

        /// <summary>Output-shaft stall torque of a DC motor at a supply voltage (N·m).</summary>
        public static double StallTorque(DcMotor m, double volts) =>
            Math.Abs(m.MotorConstant * volts / Math.Max(1e-6, m.Resistance) * m.GearRatio * m.GearEfficiency);

        public override void ReleaseActuator(string name)
        {
            var driver = Compiled?.Circuit.Devices.OfType<AveragedPwmDriver>().FirstOrDefault(d => string.Equals(d.Id, name, StringComparison.OrdinalIgnoreCase));
            if (driver != null)
            {
                ReleaseDriver(driver);
                return;
            }

            var o = ResolveObject(name);
            if (_ports.TryGetValue(o.Id, out var cmd) && cmd.Joint != null)
            {
                cmd.Joint.MotorTorque = 0;
                cmd.Joint.Drive = JointDrive.None;
            }

            _ports.Remove(o.Id);
        }

        public override void ReleaseAll()
        {
            base.ReleaseAll();
            foreach (var id in _ports.Keys.ToList()) ReleaseActuator(id);
        }

        /// <summary>WorldPhysics hook: drives a port the client commands.</summary>
        private bool DrivePort(string objectId, SceneJoint joint)
        {
            if (!_ports.TryGetValue(objectId, out var cmd)) return false;
            cmd.Joint = joint;
            joint.MotorTorque = 0;
            switch (cmd.Mode)
            {
                case PortMode.Voltage:
                {
                    // DC motor torque–speed line at this voltage: no-load speed V/k, stall torque k·V/R (at the gearbox output).
                    var m = cmd.Motor!;
                    double w0 = cmd.Value / Math.Max(1e-9, m.MotorConstant) / Math.Max(1e-9, m.GearRatio);
                    double stall = StallTorque(m, cmd.Value);
                    joint.Drive = JointDrive.Velocity;
                    joint.TargetSpeed = w0;
                    joint.Damping = stall / Math.Max(1e-6, Math.Abs(w0));
                    joint.MaxTorque = stall + 1e-5;
                    joint.Friction = 0;
                    break;
                }

                case PortMode.Speed:
                    joint.Drive = JointDrive.Velocity;
                    joint.TargetSpeed = cmd.Value;
                    joint.Damping = 0;
                    joint.MaxTorque = cmd.MaxTorque;
                    joint.Friction = 0;
                    break;
                case PortMode.Torque:
                    joint.Drive = JointDrive.None;
                    joint.MotorTorque = cmd.Value;
                    joint.Friction = 0;
                    break;
                case PortMode.Angle:
                    cmd.Servo!.Drive(joint);
                    break;
            }

            return true;
        }

        /// <summary>Angle (rad) and speed (rad/s) of an object's moving port.</summary>
        public (double Angle, double Speed)? PortState(string objectId)
        {
            if (Physics == null) return null;
            var g = SafeGeometry(World.Find(objectId)!);
            if (g?.PortName == null) return null;
            var joint = Physics.Scene.Joints.FirstOrDefault(j => j.Id == objectId + ":" + g.PortName);
            return joint == null ? ((double, double)?)null : (joint.Angle, joint.Speed);
        }

        // ================================================================ differential drive

        public override JsonObject DriveDifferential(double v, double w, JsonObject p)
        {
            if (Physics == null) throw new ApiException(ApiException.Unsupported, "Fizik yok / no physics");
            var left = J.StrList(p, "left").Select(x => ResolveObject(x).Id).ToList();
            var right = J.StrList(p, "right").Select(x => ResolveObject(x).Id).ToList();
            string? baseId = J.Str(p, "base");
            var wheels = new List<(string Motor, double Lateral, double Sign, double Radius)>();
            var motors = World.Objects.Where(o => Actuated(o).Motor != null && SafeGeometry(o)?.PortName != null).ToList();
            if (left.Count > 0 || right.Count > 0) motors = motors.Where(o => left.Contains(o.Id) || right.Contains(o.Id)).ToList();
            if (motors.Count == 0) throw new ApiException(ApiException.Unsupported, "Tekerlek motoru bulunamadı / no wheel motors found");
            baseId ??= BaseOf(motors[0].Id);
            var basePose = Physics.PoseOf(baseId) ?? throw ApiException.Missing("Taban / base '" + baseId + "'");
            var fwd = basePose.Orientation.Rotate(Vec3.UnitX);
            var leftAxis = basePose.Orientation.Rotate(Vec3.UnitY);
            foreach (var m in motors)
            {
                var g = SafeGeometry(m)!;
                var pose = Physics.PoseOf(m.Id)!.Value;
                var axis = pose.Orientation.Rotate(g.PortAxis).Normalized();
                var pivot = pose.Position + pose.Orientation.Rotate(g.PortPivot);
                // Track from the wheel centres when a wheel hangs on the shaft (contact points), else the shaft pivots.
                var wheelOn = WheelOn(m.Id);
                var centre = wheelOn != null && Physics.Scene.FindBody(wheelOn.Id) is { } wb ? wb.Position : pivot;
                double lateral = (centre - basePose.Position).Dot(leftAxis);
                if (left.Contains(m.Id)) lateral = Math.Abs(lateral) + 1e-6;
                if (right.Contains(m.Id)) lateral = -Math.Abs(lateral) - 1e-6;
                double s = axis.Cross(Vec3.UnitZ).Dot(fwd);
                if (Math.Abs(s) < 0.3) continue;   // not a wheel axle (a vertical or forward shaft)
                double radius = J.Num(p, "wheel_radius") ?? WheelRadiusOn(m.Id) ?? 0.0325;
                wheels.Add((m.Id, lateral, Math.Sign(s), radius));
            }

            if (wheels.Count == 0) throw new ApiException(ApiException.Unsupported, "Yatay akslı tekerlek motoru yok / no motor with a sideways axle");
            var lefts = wheels.Where(x => x.Lateral > 0).ToList();
            var rights = wheels.Where(x => x.Lateral <= 0).ToList();
            double track = J.Num(p, "track") ?? (lefts.Count > 0 && rights.Count > 0 ? lefts.Average(x => x.Lateral) - rights.Average(x => x.Lateral) : 0.12);
            string mode = J.Str(p, "mode") ?? "speed";
            double supply = J.Num(p, "supply", 6.0);
            var result = new JsonArray();
            foreach (var wh in wheels)
            {
                double side = wh.Lateral > 0 ? v - w * track / 2 : v + w * track / 2;
                double omega = wh.Sign * side / wh.Radius;
                var m = Actuated(World.Find(wh.Motor)!).Motor!;
                var cmd = new JsonObject();
                if (mode == "voltage")
                {
                    cmd["mode"] = "voltage";
                    cmd["value"] = omega * m.GearRatio * m.MotorConstant;
                }
                else
                {
                    cmd["mode"] = "speed";
                    cmd["value"] = omega;
                    cmd["max_torque"] = J.Num(p, "max_torque") ?? StallTorque(m, supply);
                }

                SetActuator(wh.Motor, cmd);
                result.Add(new JsonObject { ["motor"] = wh.Motor, ["side"] = wh.Lateral > 0 ? "left" : "right", ["wheel_speed"] = J.N(omega), ["radius"] = J.N(wh.Radius) });
            }

            return new JsonObject { ["base"] = baseId, ["track"] = J.N(track), ["wheels"] = result };
        }

        /// <summary>The object a motor is fixed to (the chassis), following fixed joints.</summary>
        private string BaseOf(string motorId)
        {
            var j = World.Joints.FirstOrDefault(x => x.ObjectA == motorId && x.ObjectB != null && string.IsNullOrEmpty(x.PortB));
            return j?.ObjectB ?? motorId;
        }

        private WorldObject? WheelOn(string motorId)
        {
            foreach (var j in World.Joints)
            {
                if (j.ObjectB != motorId || string.IsNullOrEmpty(j.PortB)) continue;
                if (World.Find(j.ObjectA) is { } w) return w;
            }

            return null;
        }

        private double? WheelRadiusOn(string motorId)
        {
            foreach (var j in World.Joints)
            {
                if (j.ObjectB != motorId || string.IsNullOrEmpty(j.PortB)) continue;
                var w = World.Find(j.ObjectA);
                if (w == null) continue;
                if (w.Kind == WorldObjectKind.Body && (w.Shape == "wheel" || w.Shape == "cylinder" || w.Shape == "sphere")) return w.SizeX / 2;
                var g = SafeGeometry(w);
                if (g != null) return Math.Max(g.Max.X - g.Min.X, g.Max.Z - g.Min.Z) / 2;
            }

            return null;
        }

        // ================================================================ circuit

        public override double NodeVoltage(string node)
        {
            var sim = Simulator ?? throw new ApiException(ApiException.NotLoaded, "Yüklü dünya yok / nothing loaded");
            int colon = node.IndexOf(':');
            if (colon > 0 && Circuit != null && World.Find(node.Substring(0, colon)) is { } o)
            {
                double v = Circuit.Voltage(sim, new CableEnd { ObjectId = o.Id, Pin = node.Substring(colon + 1) });
                if (!double.IsNaN(v)) return v;
            }

            return sim.NodeVoltage(node);
        }

        /// <summary>Full refdes of a part: "object" (component), "object/R1" or "object R1" (part on a board).</summary>
        public string PartRef(string key)
        {
            if (Circuit == null) throw new ApiException(ApiException.NotLoaded, "Yüklü dünya yok / nothing loaded");
            if (Compiled!.DevicesOf(key).Count > 0) return key;
            var parts = key.Split(new[] { '/', ' ' }, 2);
            var o = World.Find(parts[0]) ?? World.Objects.FirstOrDefault(x => string.Equals(x.Name, parts[0], StringComparison.OrdinalIgnoreCase));
            if (o != null)
            {
                if (parts.Length == 2) return Circuit.FullRefDes(o.Id, parts[1]);
                var r = Circuit.ComponentRef(o.Id);
                if (r != null) return r;
            }

            return key;
        }

        // ================================================================ describe

        public override JsonObject Describe()
        {
            var objects = new JsonArray();
            foreach (var o in World.Objects)
            {
                var g = SafeGeometry(o);
                var j = new JsonObject
                {
                    ["id"] = o.Id,
                    ["name"] = o.Name,
                    ["kind"] = o.Kind.ToString().ToLowerInvariant(),
                    ["part"] = o.PartId,
                    ["preset"] = o.PresetId,
                    ["static"] = o.IsStatic,
                    ["material"] = o.Material,
                    ["color"] = o.Color,
                    ["failed"] = Failed.Contains(o.Id)
                };
                if (o.Kind == WorldObjectKind.Body)
                {
                    j["shape"] = o.Shape;
                    j["size"] = J.V(new Vec3(o.SizeX, o.SizeY, o.SizeZ));
                }

                if (TryGetBody(o.Id, out var s))
                {
                    j["position"] = J.V(s.Position);
                    j["orientation"] = J.Q(s.Orientation);
                }

                if (g != null)
                {
                    var geo = new JsonObject { ["shape"] = g.Shape, ["min"] = J.V(g.Min), ["max"] = J.V(g.Max), ["mass"] = J.N(o.MassKg ?? g.MassKg) };
                    if (g.PortName != null) geo["port"] = new JsonObject { ["name"] = g.PortName, ["pivot"] = J.V(g.PortPivot), ["axis"] = J.V(g.PortAxis) };
                    if (g.SensorOrigin is { } so) geo["sensor"] = new JsonObject { ["origin"] = J.V(so), ["axis"] = J.V(g.SensorAxis) };
                    j["geometry"] = geo;
                }

                objects.Add(j);
            }

            var joints = new JsonArray();
            foreach (var jt in World.Joints)
            {
                joints.Add(new JsonObject
                {
                    ["id"] = jt.Id,
                    ["kind"] = jt.Kind.ToString().ToLowerInvariant(),
                    ["child"] = jt.ObjectA,
                    ["parent"] = jt.ObjectB,
                    ["parent_port"] = string.IsNullOrEmpty(jt.PortB) ? null : jt.PortB,
                    ["anchor"] = J.V(new Vec3(jt.AnchorX, jt.AnchorY, jt.AnchorZ)),
                    ["axis"] = J.V(new Vec3(jt.AxisX, jt.AxisY, jt.AxisZ)),
                    ["lower"] = jt.Lower.HasValue ? J.N(jt.Lower.Value) : null,
                    ["upper"] = jt.Upper.HasValue ? J.N(jt.Upper.Value) : null
                });
            }

            var cables = new JsonArray();
            foreach (var c in World.Cables)
            {
                cables.Add(new JsonObject { ["id"] = c.Id, ["name"] = c.Name, ["a"] = c.A.ToString(), ["b"] = c.B.ToString(), ["awg"] = J.N(c.Awg) });
            }

            var env = World.Environment;
            return new JsonObject
            {
                ["kind"] = Kind,
                ["title"] = Title,
                ["description"] = World.Description,
                ["environment"] = new JsonObject
                {
                    ["name"] = env.Name,
                    ["width"] = J.N(env.Width),
                    ["depth"] = J.N(env.Depth),
                    ["gravity"] = J.N(env.Gravity),
                    ["friction"] = J.N(env.Friction),
                    ["track"] = env.Track,
                    ["ambient_c"] = J.N(env.AmbientC),
                    ["start"] = Floor is { } a ? J.Arr(new[] { a.StartX, a.StartY, a.StartYaw }) : null,
                    ["obstacles"] = new JsonArray(env.Obstacles.Select(ob => (JsonNode)new JsonObject
                    {
                        ["x"] = J.N(ob.X), ["y"] = J.N(ob.Y), ["size_x"] = J.N(ob.SizeX), ["size_y"] = J.N(ob.SizeY), ["height"] = J.N(ob.Height)
                    }).ToArray())
                },
                ["objects"] = objects,
                ["joints"] = joints,
                ["cables"] = cables,
                ["boards"] = new JsonArray(Boards.Select(b => (JsonNode)b.ToJson()).ToArray()),
                ["actuators"] = ListActuators(),
                ["sensors"] = Sensors.List()
            };
        }
    }
}
