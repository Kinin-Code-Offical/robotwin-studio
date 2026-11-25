using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// Headless run of a project with a vehicle assembly (robot car or quadcopter on the Montaj page):
    /// circuit + firmware + <see cref="MechatronicSystem"/> (wheels, tyres, propellers, flight
    /// controller, arena), stepped like the app's schematic simulation.
    /// </summary>
    public sealed class VehicleRunner : SimRunner
    {
        public const string VehicleId = "vehicle";
        private Vec3 _imuLastVelocity;

        public VehicleRunner(Schematic schematic, ProjectPackage? package = null, RunnerOptions? options = null)
            : base(options ?? new RunnerOptions())
        {
            Schematic = schematic ?? throw new ArgumentNullException(nameof(schematic));
            Package = package;
            if (schematic.Assembly == null || schematic.Assembly.Kind == VehicleKind.None)
                throw new ApiException(ApiException.Unsupported, "Projede araç montajı yok");
            Reset();
        }

        /// <summary>Built-in vehicles: line-follower, obstacle-avoider, quadcopter.</summary>
        public static Schematic? Template(string name, PartLibrary lib) => name.ToLowerInvariant() switch
        {
            "line-follower" or "linefollower" or "cizgi-izleyen" => VehicleTemplates.LineFollower(lib),
            "obstacle-avoider" or "engelden-kacan" => VehicleTemplates.ObstacleAvoider(lib),
            "quadcopter" or "drone" or "uav" or "iha" => VehicleTemplates.MicroQuadcopter(lib),
            _ => null
        };

        public Schematic Schematic { get; }
        public ProjectPackage? Package { get; }
        public MechatronicSystem? Mechanics { get; private set; }
        public override string Kind => "vehicle";
        public override string Title => Schematic.Title;

        protected override void Build()
        {
            try
            {
                Compiled = CircuitCompiler.Compile(Schematic, Library);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                throw new ApiException(ApiException.InvalidParams, "Devre derlenemedi: " + ex.Message);
            }

            foreach (var w in Compiled.Warnings) AddEvent(new RunnerEvent(0, "derleyici", RunnerEventKind.Warning, w));
            Simulator = StartSolver(Compiled.Circuit, Schematic.Assembly?.World?.AmbientC ?? 25.0);
            AttachFirmware();
            Mechanics = new MechatronicSystem(Schematic.Assembly!, Schematic, Library, Compiled, Simulator);
            _imuLastVelocity = Vec3.Zero;
            AddEvent(new RunnerEvent(0, "montaj", RunnerEventKind.Info,
                $"{(Mechanics.Kind == VehicleKind.Quadcopter ? "Quadcopter" : "Robot araba")} · {Mechanics.TotalMass * 1000:F0} g · {Mechanics.Arena.Name}"));
        }

        protected override void Teardown()
        {
            base.Teardown();
            Mechanics = null;
        }

        private void AttachFirmware()
        {
            foreach (var part in Schematic.Parts)
            {
                var def = Library.Find(part.PartId);
                if (def?.Symbol != SymbolKind.Board) continue;
                bool esp = IsEsp32Part(def.Id);
                if (!esp && def.Id != "arduino-uno" && def.Id != "arduino-nano") continue;
                var board = new BoardInfo { ObjectId = part.RefDes, RefDes = part.RefDes, Name = part.RefDes, PartId = def.Id, Mcu = esp ? "esp32" : "atmega328p" };
                Boards.Add(board);
                if (!Options.Firmware)
                {
                    board.Firmware = "(kapalı)";
                    continue;
                }

                part.Parameters.TryGetValue("firmware", out var fw);
                var embedded = Package?.FirmwareFor(part.RefDes);
                bool useProject = string.IsNullOrEmpty(fw) || fw!.StartsWith("project", StringComparison.OrdinalIgnoreCase);
                if (esp)
                {
                    string? source = useProject ? embedded?.Source : null;
                    if (string.IsNullOrWhiteSpace(source) && !string.IsNullOrEmpty(fw))
                    {
                        var path = Options.ResolveFirmware(fw);
                        if (path != null && System.IO.File.Exists(path)) source = System.IO.File.ReadAllText(path);
                    }

                    board.Firmware = embedded?.FileName ?? fw ?? string.Empty;
                    if (AttachEsp32(board, source, part.Parameters) is { } e)
                    {
                        AttachI2c(e.I2c, part.RefDes, 9.81);
                        AttachLedStrips(e, part.RefDes);
                    }
                    continue;
                }

                string? hex = FirmwareOverrides.TryGetValue(part.RefDes, out var o) ? o : null;
                board.Firmware = hex != null ? "(API)" : string.Empty;
                if (hex == null && useProject && embedded?.Hex != null)
                {
                    hex = embedded.Hex;
                    board.Firmware = embedded.FileName;
                }
                else if (hex == null && !string.IsNullOrEmpty(fw))
                {
                    var path = Options.ResolveFirmware(fw);
                    if (path != null && System.IO.File.Exists(path))
                    {
                        hex = System.IO.File.ReadAllText(path);
                        board.Firmware = System.IO.Path.GetFileName(path);
                    }
                    else
                    {
                        AddEvent(new RunnerEvent(0, part.RefDes, RunnerEventKind.Warning, "Firmware bulunamadı: " + fw));
                    }
                }

                if (hex == null) continue;
                string refDes = part.RefDes;
                var cosim = AttachAvr(board, hex, () => Simulator == null ? 0 : Compiled!.PinVoltage(Simulator, refDes, "5V"));
                if (cosim != null) AttachI2c(cosim.I2c, part.RefDes, 9.81);
            }
        }

        protected override IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> AllParts()
        {
            foreach (var p in Schematic.Parts)
            {
                var def = Library.Find(p.PartId);
                if (def != null) yield return (p.RefDes, def, Schematic.ResolveParameters(Library, p));
            }
        }

        protected override void AdvanceMechanics(double dt)
        {
            if (Mechanics == null) return;
            Mechanics.Advance(dt);
            UpdateImus(dt);
        }

        /// <summary>Feeds the vehicle's motion into MPU-6050 modules mounted on it (as the app does).</summary>
        private void UpdateImus(double dt)
        {
            if (Mechanics == null || I2cDevices.Count == 0 || dt <= 0) return;
            var body = Mechanics.Body;
            var accelWorld = (body.Velocity - _imuLastVelocity) / dt;
            _imuLastVelocity = body.Velocity;
            var specific = body.Orientation.InverseRotate(accelWorld - new Vec3(0, 0, -9.81));
            var rates = body.BodyRates;
            foreach (var s in Mechanics.Sensors)
            {
                if (s.Mount.Kind != SensorKind.Imu || !(I2cDevice(s.Mount.RefDes) is Firmware.Devices.Mpu6050 mpu)) continue;
                mpu.AccelX = specific.X;
                mpu.AccelY = specific.Y;
                mpu.AccelZ = specific.Z;
                mpu.GyroX = rates.X;
                mpu.GyroY = rates.Y;
                mpu.GyroZ = rates.Z;
            }
        }

        protected override void DrainMechanicsEvents()
        {
            if (Mechanics == null) return;
            foreach (var m in Mechanics.DrainEvents())
            {
                AddEvent(new RunnerEvent(m.Time, m.Source, m.Severe ? RunnerEventKind.Failure : RunnerEventKind.Info, m.Message));
                if (m.Severe) Failed.Add(VehicleId);
            }
        }

        // ================================================================ bodies

        public override IEnumerable<string> ObjectIds()
        {
            yield return VehicleId;
            if (Mechanics == null) yield break;
            foreach (var l in Mechanics.Links) yield return "link:" + l.Mount.Name;
            for (int i = 0; i < Mechanics.World.Movables.Count; i++) yield return "movable:" + i;
        }

        private SceneBody? BodyOf(string id)
        {
            if (Mechanics == null) return null;
            if (id == VehicleId) return Mechanics.World.VehicleBody;
            if (id.StartsWith("link:", StringComparison.Ordinal)) return Mechanics.Links.FirstOrDefault(l => l.Mount.Name == id.Substring(5))?.Body;
            if (id.StartsWith("movable:", StringComparison.Ordinal) && int.TryParse(id.Substring(8), out int i) && i >= 0 && i < Mechanics.World.Movables.Count)
                return Mechanics.World.Movables[i];
            return null;
        }

        public override bool TryGetBody(string objectId, out BodyState state)
        {
            state = default;
            var b = BodyOf(objectId);
            if (b == null) return false;
            var origin = objectId == VehicleId ? b.ToWorld(Vec3.Zero - Mechanics!.CenterOfMass) : b.Position;
            var w = b.AngularVelocity;
            state = new BodyState(origin, b.Orientation, b.Velocity + w.Cross(origin - b.Position), w);
            return true;
        }

        private bool IsOwn(SceneBody b) => Mechanics != null && (b == Mechanics.World.VehicleBody || Mechanics.Links.Any(l => l.Body == b));

        public override bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, string? ignoreObject, out SceneRayHit hit)
        {
            hit = default;
            if (Mechanics == null) return false;
            var d = direction.Normalized();
            Func<SceneBody, bool> filter = b => b.SensorVisible && (ignoreObject == null || !IsOwn(b));
            bool found = Mechanics.World.Scene.Raycast(origin, d, maxDistance, out hit, filter, true);
            double arena = Math.Abs(d.Z) < 0.2 ? Mechanics.Arena.Raycast(origin, d, found ? hit.Distance : maxDistance) : maxDistance;
            if (arena < (found ? hit.Distance : maxDistance) - 1e-9)
            {
                hit = new SceneRayHit { Distance = arena, Point = origin + d * arena, Normal = -d, Body = null, Material = "arena" };
                return true;
            }

            return found;
        }

        public override Vec3 Gravity => Mechanics?.World.Gravity ?? new Vec3(0, 0, -9.81);

        public override Arena? Floor => Mechanics?.Arena;

        public override (double R, double G, double B) ColorOf(SceneBody? body)
        {
            if (body == null) return (0.62, 0.36, 0.25);
            if (Mechanics != null && body == Mechanics.World.VehicleBody) return (0.16, 0.18, 0.22);
            return (0.85, 0.55, 0.2);
        }

        public override void SetPose(string objectId, Vec3? position, Quat? orientation, bool zeroVelocity)
        {
            var b = BodyOf(objectId) ?? throw ApiException.Missing("Nesne '" + objectId + "'");
            TryGetBody(objectId, out var current);
            var p = position ?? current.Position;
            var q = orientation ?? current.Orientation;
            var turn = (q * current.Orientation.Conjugate).Normalized();
            var bodies = new List<SceneBody> { b };
            if (objectId == VehicleId) bodies.AddRange(Mechanics!.Links.Select(l => l.Body));
            foreach (var x in bodies)
            {
                x.Position = p + turn.Rotate(x.Position - current.Position);
                x.Orientation = (turn * x.Orientation).Normalized();
                if (zeroVelocity)
                {
                    x.Velocity = Vec3.Zero;
                    x.AngularVelocity = Vec3.Zero;
                }
            }
        }

        public override void Randomize(NoiseSource noise, double positionSigma, double yawSigmaRad)
        {
            if (!TryGetBody(VehicleId, out var s)) return;
            SetPose(VehicleId, s.Position + new Vec3(noise.Gaussian(positionSigma), noise.Gaussian(positionSigma), 0),
                Quat.FromAxisAngle(Vec3.UnitZ, noise.Gaussian(yawSigmaRad)) * s.Orientation, true);
        }

        // ================================================================ actuators

        /// <summary>The PWM switch (driver channel / FC MOSFET) whose output feeds a motor, if any.</summary>
        private AveragedPwmDriver? DriverOf(DcMotor m) =>
            Compiled?.Circuit.Devices.OfType<AveragedPwmDriver>().FirstOrDefault(d => d.Output == m.A || d.Output == m.B);

        public override JsonArray ListActuators()
        {
            var list = new JsonArray();
            if (Mechanics == null) return list;
            foreach (var w in Mechanics.Wheels)
            {
                var d = w.Motor != null ? DriverOf(w.Motor) : null;
                list.Add(new JsonObject
                {
                    ["name"] = w.Mount.RefDes,
                    ["type"] = "wheel-motor",
                    ["role"] = w.Mount.Role,
                    ["driver"] = d?.Id,
                    ["modes"] = J.Strs(d != null ? new[] { "duty", "release" } : new[] { "release" }),
                    ["commanded"] = d != null && DriverOverridden(d)
                });
            }

            foreach (var p in Mechanics.Props)
            {
                var d = p.Motor != null ? DriverOf(p.Motor) : null;
                list.Add(new JsonObject
                {
                    ["name"] = p.Mount?.RefDes ?? "M" + (p.Index + 1),
                    ["type"] = "prop-motor",
                    ["index"] = p.Index,
                    ["driver"] = d?.Id,
                    ["modes"] = J.Strs(new[] { "duty", "release" }),
                    ["commanded"] = d != null && DriverOverridden(d)
                });
            }

            foreach (var d in Compiled!.Circuit.Devices.OfType<AveragedPwmDriver>())
                list.Add(new JsonObject { ["name"] = d.Id, ["type"] = "driver", ["modes"] = J.Strs(new[] { "duty", "release" }), ["commanded"] = DriverOverridden(d) });
            return list;
        }

        private AveragedPwmDriver ResolveDriver(string name)
        {
            var dev = Compiled?.Circuit.Devices.OfType<AveragedPwmDriver>().FirstOrDefault(d => string.Equals(d.Id, name, StringComparison.OrdinalIgnoreCase));
            if (dev != null) return dev;
            if (Mechanics != null)
            {
                DcMotor? motor = Mechanics.Wheels.FirstOrDefault(w => string.Equals(w.Mount.RefDes, name, StringComparison.OrdinalIgnoreCase))?.Motor
                    ?? Mechanics.Props.FirstOrDefault(p => string.Equals(p.Mount?.RefDes ?? "M" + (p.Index + 1), name, StringComparison.OrdinalIgnoreCase))?.Motor;
                if (motor != null) return DriverOf(motor) ?? throw new ApiException(ApiException.Unsupported, name + ": motoru süren PWM sürücü yok");
            }

            throw ApiException.Missing("Eyleyici '" + name + "'");
        }

        public override JsonObject SetActuator(string name, JsonObject p)
        {
            var driver = ResolveDriver(name);
            string mode = (J.Str(p, "mode") ?? "duty").ToLowerInvariant();
            if (mode == "release")
            {
                ReleaseDriver(driver);
                return new JsonObject { ["name"] = name, ["released"] = true };
            }

            if (mode != "duty") throw ApiException.BadParam("mode", "duty | release (araç modunda)");
            double duty = J.Num(p, "value") ?? J.ReqNum(p, "duty");
            SetDriverDuty(driver, duty);
            return new JsonObject { ["name"] = name, ["driver"] = driver.Id, ["duty"] = J.N(driver.Duty) };
        }

        public override void ReleaseActuator(string name) => ReleaseDriver(ResolveDriver(name));

        public override JsonObject DriveDifferential(double v, double w, JsonObject p)
        {
            if (Mechanics == null || Mechanics.Kind != VehicleKind.Rover) throw new ApiException(ApiException.Unsupported, "Kara aracı değil");
            var a = Mechanics.Assembly;
            double track = J.Num(p, "track") ?? a.Width;
            double vmax = J.Num(p, "max_speed", 0.6);
            var result = new JsonArray();
            int i = 0;
            foreach (var wl in Mechanics.Wheels)
            {
                bool left = wl.Mount.Role.StartsWith("L", StringComparison.OrdinalIgnoreCase) || (string.IsNullOrEmpty(wl.Mount.Role) && i % 2 == 0);
                i++;
                if (wl.Motor == null) continue;
                var d = DriverOf(wl.Motor);
                if (d == null) continue;
                double side = left ? v - w * track / 2 : v + w * track / 2;
                double duty = Math.Max(0, Math.Min(1, side / vmax));
                SetDriverDuty(d, duty);
                result.Add(new JsonObject
                {
                    ["motor"] = wl.Mount.RefDes, ["side"] = left ? "left" : "right", ["driver"] = d.Id, ["duty"] = J.N(duty),
                    ["clipped"] = side < 0 || side > vmax
                });
            }

            if (result.Count == 0) throw new ApiException(ApiException.Unsupported, "Tekerlek motorlarını süren PWM sürücü yok");
            return new JsonObject { ["track"] = J.N(track), ["max_speed"] = J.N(vmax), ["wheels"] = result, ["note"] = "tek yönlü sürücü: geri gidiş yok" };
        }

        // ================================================================ flight

        public FlightController Flight => Mechanics?.Flight ?? throw new ApiException(ApiException.Unsupported, "Uçuş kontrolcüsü yok");

        public JsonObject FlightStatus()
        {
            var m = Mechanics!;
            var fc = m.Flight;
            var o = new JsonObject
            {
                ["available"] = fc != null,
                ["altitude"] = J.N(m.Altitude),
                ["speed"] = J.N(m.Speed),
                ["distance"] = J.N(m.Distance),
                ["mass"] = J.N(m.TotalMass)
            };
            if (fc == null) return o;
            o["armed"] = fc.Armed;
            o["powered"] = fc.Powered;
            o["mode"] = fc.Mode == FlightMode.Angle ? "angle" : "altitude";
            o["throttle"] = J.N(fc.Throttle);
            o["roll_input"] = J.N(fc.RollInput);
            o["pitch_input"] = J.N(fc.PitchInput);
            o["yaw_input"] = J.N(fc.YawInput);
            o["target_altitude"] = J.N(fc.TargetAltitude);
            o["roll"] = J.N(fc.Roll);
            o["pitch"] = J.N(fc.Pitch);
            o["yaw"] = J.N(fc.Yaw);
            o["motor_commands"] = J.Arr(fc.Command);
            o["props_broken"] = m.Props.Count(p => p.Broken || p.ArmBroken);
            return o;
        }

        public JsonObject SetFlight(JsonObject p)
        {
            var fc = Flight;
            if (J.Str(p, "mode") is { } mode)
            {
                fc.Mode = mode.StartsWith("alt", StringComparison.OrdinalIgnoreCase) ? FlightMode.AltitudeHold : FlightMode.Angle;
                if (fc.Mode == FlightMode.AltitudeHold && !J.Has(p, "target_altitude")) fc.TargetAltitude = Math.Max(0, Mechanics!.Altitude);
            }

            // In altitude hold the throttle stick is a climb-rate command (0.5 = hold): centre it unless given.
            if (fc.Mode == FlightMode.AltitudeHold && (J.Has(p, "mode") || J.Bool(p, "armed", false)) && !J.Has(p, "throttle")) fc.Throttle = 0.5;
            if (J.Num(p, "throttle") is { } t) fc.Throttle = Math.Max(0, Math.Min(1, t));
            if (J.Num(p, "roll") is { } r) fc.RollInput = Math.Max(-1, Math.Min(1, r));
            if (J.Num(p, "pitch") is { } pi) fc.PitchInput = Math.Max(-1, Math.Min(1, pi));
            if (J.Num(p, "yaw") is { } y) fc.YawInput = Math.Max(-1, Math.Min(1, y));
            if (J.Num(p, "target_altitude") is { } alt) fc.TargetAltitude = Math.Max(0, alt);
            if (J.Has(p, "armed"))
            {
                bool arm = J.Bool(p, "armed", false);
                if (arm && !fc.Armed && fc.Mode == FlightMode.AltitudeHold && !J.Has(p, "target_altitude")) fc.TargetAltitude = Math.Max(Mechanics!.Body.Position.Z, 0.6);
                fc.Armed = arm;
            }

            return FlightStatus();
        }

        // ================================================================ describe

        public override JsonObject Describe()
        {
            var m = Mechanics!;
            var a = m.Assembly;
            var objects = new JsonArray();
            foreach (var id in ObjectIds())
            {
                var o = new JsonObject { ["id"] = id, ["name"] = id == VehicleId ? Title : id, ["kind"] = id == VehicleId ? "vehicle" : id.Split(':')[0] };
                if (TryGetBody(id, out var s))
                {
                    o["position"] = J.V(s.Position);
                    o["orientation"] = J.Q(s.Orientation);
                }

                objects.Add(o);
            }

            var sensors = new JsonArray();
            foreach (var s in m.Sensors)
                sensors.Add(new JsonObject { ["refdes"] = s.Mount.RefDes, ["kind"] = s.Mount.Kind.ToString().ToLowerInvariant(), ["x"] = J.N(s.Mount.X), ["y"] = J.N(s.Mount.Y), ["yaw"] = J.N(s.Mount.Yaw) });
            return new JsonObject
            {
                ["kind"] = Kind,
                ["title"] = Title,
                ["vehicle"] = new JsonObject
                {
                    ["type"] = m.Kind == VehicleKind.Quadcopter ? "quadcopter" : "rover",
                    ["arena"] = m.Arena.Name,
                    ["mass"] = J.N(m.TotalMass),
                    ["length"] = J.N(a.Length),
                    ["width"] = J.N(a.Width),
                    ["wheel_radius"] = J.N(a.WheelRadius),
                    ["prop_diameter"] = J.N(a.PropDiameter),
                    ["arm_length"] = J.N(a.ArmLength),
                    ["wheels"] = m.Wheels.Count,
                    ["props"] = m.Props.Count,
                    ["flight_controller"] = m.Flight != null
                },
                ["objects"] = objects,
                ["mounted_sensors"] = sensors,
                ["boards"] = new JsonArray(Boards.Select(b => (JsonNode)b.ToJson()).ToArray()),
                ["actuators"] = ListActuators(),
                ["sensors"] = Sensors.List()
            };
        }
    }
}
