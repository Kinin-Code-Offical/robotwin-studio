using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>What the hosting program (headless tool or the app) provides to an API session.</summary>
    public sealed class ApiHostOptions
    {
        /// <summary>"headless" (cross-platform tool) or "studio" (the app's exact 3D geometry).</summary>
        public string Backend { get; set; } = "headless";

        public PartLibrary Library { get; set; } = PartLibrary.Default;

        /// <summary>Geometry for a world (world, its file path or null). Null = baked file beside the world, else approximate.</summary>
        public Func<WorldDocument, string?, IWorldGeometry>? Geometry { get; set; }

        public List<string> FirmwareSearchPath { get; } = new List<string>();
        public Func<string, string?>? Esp32Builder { get; set; }

        /// <summary>Asked to exit by the "shutdown" method.</summary>
        public Action? Shutdown { get; set; }

        /// <summary>Required in "hello" before anything else when set.</summary>
        public string? Token { get; set; }
    }

    /// <summary>
    /// The automation API: JSON-RPC 2.0 methods over one simulation (world or vehicle). Transport-free —
    /// <see cref="ApiServer"/> feeds it lines from TCP clients; tests call <see cref="Handle"/> directly.
    /// Not thread-safe: the server serialises calls.
    /// </summary>
    public sealed class ApiSession : IDisposable
    {
        public const string ApiVersion = "1.0";

        private readonly ApiHostOptions _host;
        private readonly Dictionary<string, (Func<JsonObject?, JsonNode?> Run, string Help)> _methods =
            new Dictionary<string, (Func<JsonObject?, JsonNode?>, string)>(StringComparer.Ordinal);

        private JsonObject? _loadParams;

        public ApiSession(ApiHostOptions? host = null)
        {
            _host = host ?? new ApiHostOptions();
            Register();
        }

        public SimRunner? Runner { get; private set; }

        public IReadOnlyCollection<string> MethodNames => _methods.Keys;

        public void Dispose()
        {
            Runner?.Dispose();
            Runner = null;
        }

        // ================================================================ JSON-RPC framing

        /// <summary>Handles one JSON-RPC request (or batch) line; returns the response line (null for notifications).</summary>
        public string? Handle(string line, ref bool authorized)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch (JsonException ex)
            {
                return Error(null, ApiException.ParseError, "JSON çözümlenemedi / parse error: " + ex.Message).ToJsonString(J.Wire);
            }

            if (node is JsonArray batch)
            {
                var results = new JsonArray();
                foreach (var item in batch)
                {
                    var r = HandleOne(item as JsonObject, ref authorized);
                    if (r != null) results.Add(r);
                }

                return results.Count == 0 ? null : results.ToJsonString(J.Wire);
            }

            return HandleOne(node as JsonObject, ref authorized)?.ToJsonString(J.Wire);
        }

        /// <summary>Convenience for in-process use (always authorised).</summary>
        public string? Handle(string line)
        {
            bool ok = true;
            return Handle(line, ref ok);
        }

        private JsonObject? HandleOne(JsonObject? req, ref bool authorized)
        {
            if (req == null) return Error(null, ApiException.InvalidRequest, "İstek bir JSON nesnesi olmalı / request must be an object");
            var id = req["id"]?.DeepClone();
            bool notification = !req.ContainsKey("id");
            string? method = J.Str(req, "method");
            if (method == null) return Error(id, ApiException.InvalidRequest, "'method' yok / missing");
            var p = req["params"] as JsonObject;
            if (req["params"] is JsonArray) return Error(id, ApiException.InvalidParams, "params bir nesne olmalı / params must be an object");
            try
            {
                if (_host.Token != null && !authorized)
                {
                    if (method != "hello" || J.Str(p, "token") != _host.Token)
                        throw new ApiException(ApiException.Unauthorized, "Önce hello {token} gönderin / send hello {token} first");
                    authorized = true;
                }

                var result = Call(method, p);
                if (notification) return null;
                return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
            }
            catch (ApiException ex)
            {
                return notification ? null : Error(id, ex.Code, ex.Message);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException || ex is InvalidDataException ||
                                       ex is FormatException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is KeyNotFoundException ||
                                       ex is NullReferenceException || ex is IndexOutOfRangeException)
            {
                return notification ? null : Error(id, ApiException.InternalError, ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static JsonObject Error(JsonNode? id, int code, string message) => new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };

        /// <summary>Runs one method with its params (throws <see cref="ApiException"/> on errors).</summary>
        public JsonNode? Call(string method, JsonObject? p)
        {
            if (!_methods.TryGetValue(method, out var m)) throw new ApiException(ApiException.MethodNotFound, "Bilinmeyen yöntem / unknown method: " + method);
            return m.Run(p);
        }

        private SimRunner R => Runner ?? throw new ApiException(ApiException.NotLoaded, "Önce world.load çağırın / call world.load first");

        private WorldRunner W => R as WorldRunner ?? throw new ApiException(ApiException.Unsupported, "Bu yöntem dünya (.rwworld) modunda çalışır / world mode only");

        private VehicleRunner V => R as VehicleRunner ?? throw new ApiException(ApiException.Unsupported, "Bu yöntem araç modunda çalışır / vehicle mode only");

        private void Add(string name, string help, Func<JsonObject?, JsonNode?> run) => _methods[name] = (run, help);

        // ================================================================ methods

        private void Register()
        {
            Add("hello", "{token?} → sunucu bilgisi / server info", p => new JsonObject
            {
                ["name"] = "RobotWin Studio",
                ["api_version"] = ApiVersion,
                ["protocol"] = "JSON-RPC 2.0, one JSON object per line (UTF-8, \\n)",
                ["backend"] = _host.Backend,
                ["loaded"] = Runner != null,
                ["sensor_types"] = J.Strs(SensorRegistry.Types),
                ["examples"] = J.Strs(new[] { "car", "line-follower", "obstacle-avoider", "quadcopter" })
            });
            Add("ping", "→ \"pong\"", p => "pong");
            Add("methods", "→ yöntem listesi / method list with help", p =>
            {
                var o = new JsonObject();
                foreach (var kv in _methods.OrderBy(k => k.Key, StringComparer.Ordinal)) o[kv.Key] = kv.Value.Help;
                return o;
            });
            Add("shutdown", "sunucuyu kapatır / stops the server", p =>
            {
                _host.Shutdown?.Invoke();
                return true;
            });

            // ---------------------------------------------------------------- loading
            Add("world.load", "{path | example | world_json, seed?, firmware?, max_step?, rel_tol?, damage?, geometry?, environment?, arena?, sensors?[]} → describe", p =>
            {
                Load(p ?? new JsonObject());
                return R.Describe();
            });
            Add("world.reset", "{seed?, randomize?: {position, yaw_deg}, observe?[]} → {time, seed, observation}", Reset);
            Add("world.describe", "→ nesneler, kartlar, eyleyiciler, sensörler / objects, boards, actuators, sensors", p => R.Describe());
            Add("world.save", "{path} — yüklü dünyayı .rwworld olarak kaydeder (uygulamada açılır) / saves the loaded world", p =>
            {
                string path = J.ReqStr(p, "path");
                if (!path.EndsWith(WorldDocument.Extension, StringComparison.OrdinalIgnoreCase)) path += WorldDocument.Extension;
                W.World.Save(path);
                return path;
            });
            Add("world.export_geometry", "{path?} → bu dünyanın gövde geometrisi (JSON) / baked body geometry", p =>
            {
                var w = W;
                string json = BakedGeometry.Export(w.World, w.Geometry);
                if (J.Str(p, "path") is { } path) File.WriteAllText(path, json);
                return JsonNode.Parse(json);
            });

            // ---------------------------------------------------------------- stepping
            Add("sim.step", "{dt=0.01, steps=1, actions?[], observe?[], pace?, stop_on_failure?, events?} → {time, rtf, observation, ...}", Step);
            Add("sim.status", "→ zaman, gerçek-zaman oranı, arızalar / time, real-time factor, failures", p => R.Status());
            Add("events.poll", "{max?} → olaylar (arıza, uyarı, çarpma) / events since last poll", p =>
                new JsonArray(R.DrainEvents(J.Int(p, "max", 1000)).Select(e => (JsonNode)e.ToJson()).ToArray()));

            // ---------------------------------------------------------------- objects
            Add("object.list", "→ [{id, name, kind}]", p =>
            {
                var a = new JsonArray();
                if (R is WorldRunner wr)
                {
                    foreach (var o in wr.World.Objects) a.Add(new JsonObject { ["id"] = o.Id, ["name"] = o.Name, ["kind"] = o.Kind.ToString().ToLowerInvariant(), ["static"] = o.IsStatic });
                }
                else
                {
                    foreach (var id in R.ObjectIds()) a.Add(new JsonObject { ["id"] = id, ["name"] = id, ["kind"] = id.Split(':')[0] });
                }

                return a;
            });
            Add("object.get_pose", "{id} → {position, orientation[x,y,z,w], rpy, linear_velocity, angular_velocity}", p => Pose(ObjectId(J.ReqStr(p, "id"))));
            Add("object.set_pose", "{id, position?, orientation?[x,y,z,w] | rpy_deg?, keep_velocity?} — tüm montaj birlikte taşınır / moves the whole assembly", p =>
            {
                string id = ObjectId(J.ReqStr(p, "id"));
                var q = J.Quat(p, "orientation");
                if (q == null && J.Vec(p, "rpy_deg") is { } rpy) q = J.FromRpy(rpy.X * Math.PI / 180, rpy.Y * Math.PI / 180, rpy.Z * Math.PI / 180);
                R.SetPose(id, J.Vec(p, "position"), q, !J.Bool(p, "keep_velocity", false));
                return Pose(id);
            });

            // ---------------------------------------------------------------- sensors
            Add("sensor.list", "→ [{name, type, object, ...}]", p => R.Sensors.List());
            Add("sensor.types", "→ sensor.add türleri / types for sensor.add", p => J.Strs(SensorRegistry.Types));
            Add("sensor.read", "{name | names[]} → okuma(lar) / reading(s)", p =>
            {
                var names = J.StrList(p, "names");
                if (names.Count == 0 && J.Str(p, "name") is { } one) return R.Sensors.Read(one);
                var o = new JsonObject();
                foreach (var n in names.Count > 0 ? names : R.Sensors.All.Where(s => s.Type != "camera" && s.Type != "depth").Select(s => s.Name).ToList()) o[n] = Observe(n);
                return o;
            });
            Add("sensor.add", "{name?, type, object?, offset?[x,y,z], rpy_deg?[r,p,y], ...params} → sensör / sensor", p =>
            {
                var spec = new SensorSpec
                {
                    Name = J.Str(p, "name") ?? string.Empty,
                    Type = J.ReqStr(p, "type"),
                    Object = J.Str(p, "object") is { } obj ? ObjectId(obj) : null,
                    Offset = J.Vec(p, "offset") ?? Vec3.Zero
                };
                if (J.Vec(p, "rpy_deg") is { } rpy) spec.Rotation = J.FromRpy(rpy.X * Math.PI / 180, rpy.Y * Math.PI / 180, rpy.Z * Math.PI / 180);
                else if (J.Quat(p, "rotation") is { } q) spec.Rotation = q;
                var prm = p != null ? (JsonObject)p.DeepClone() : new JsonObject();
                foreach (var k in new[] { "name", "type", "object", "offset", "rpy_deg", "rotation" }) prm.Remove(k);
                spec.Params = prm;
                var s = R.Sensors.Add(spec);
                var d = s.Describe();
                d["name"] = s.Name;
                d["type"] = s.Type;
                d["object"] = s.Object;
                return d;
            });
            Add("sensor.remove", "{name} → bool", p => R.Sensors.Remove(J.ReqStr(p, "name")));

            // ---------------------------------------------------------------- actuators
            Add("actuator.list", "→ motorlar, servolar, sürücüler / motors, servos, drivers", p => R.ListActuators());
            Add("actuator.set", "{name, mode: voltage|duty|speed|torque|angle|release, value, max_torque?, supply?}", p => R.SetActuator(J.ReqStr(p, "name"), p!));
            Add("actuator.release", "{name? (yoksa hepsi / all when omitted)}", p =>
            {
                if (J.Str(p, "name") is { } n) R.ReleaseActuator(n);
                else R.ReleaseAll();
                return true;
            });
            Add("pin.write", "{board?, pin, mode: high|low|input|pullup|pwm|servo|release, duty?, frequency?, angle?}", p =>
            {
                R.WritePin(J.Str(p, "board"), J.ReqStr(p, "pin"), J.ReqStr(p, "mode"), J.Num(p, "duty", 0), J.Num(p, "frequency", 490), J.Num(p, "angle", 90));
                return R.ReadPin(J.Str(p, "board"), J.ReqStr(p, "pin"));
            });
            Add("pin.read", "{board?, pin} → {voltage, level, mode, pwm_duty}", p => R.ReadPin(J.Str(p, "board"), J.ReqStr(p, "pin")));
            Add("drive.differential", "{v (m/s), w (rad/s), left?[], right?[], wheel_radius?, track?, mode?: speed|voltage, max_speed?}", p =>
                R.DriveDifferential(J.ReqNum(p, "v"), J.Num(p, "w", 0), p ?? new JsonObject()));
            Add("drive.stop", "tekerlekleri bırakır / releases wheel commands", p =>
            {
                R.ReleaseAll();
                return true;
            });

            // ---------------------------------------------------------------- flight (UAV)
            Add("flight.status", "→ {armed, mode, throttle, altitude, roll, pitch, yaw, ...}", p => V.FlightStatus());
            Add("flight.arm", "{armed=true}", p => V.SetFlight(new JsonObject { ["armed"] = J.Bool(p, "armed", true) }));
            Add("flight.set", "{throttle 0..1, roll/pitch/yaw −1..1, mode: angle|altitude, target_altitude m, armed?}", p => V.SetFlight(p ?? new JsonObject()));
            Add("flight.motors", "{duty[4]} — kuruluyken FC'yi ezer / raw motor duties (overrides the FC)", p =>
            {
                var fc = V.Flight;
                if (!(J.Get(p, "duty") is JsonArray d)) throw ApiException.BadParam("duty", "[d1, d2, d3, d4]");
                for (int i = 0; i < 4 && i < d.Count; i++)
                {
                    var o = fc.Outputs[i];
                    if (o == null) continue;
                    double duty = J.Item(d, i);
                    if (double.IsNaN(duty)) R.ReleaseDriver(o);
                    else R.SetDriverDuty(o, duty);
                }

                return V.FlightStatus();
            });

            // ---------------------------------------------------------------- serial / firmware
            Add("serial.read", "{board?, clear=true} → {refdes: text}", p =>
            {
                var o = new JsonObject();
                foreach (var kv in R.ReadSerial(J.Str(p, "board"), J.Bool(p, "clear", true))) o[kv.Key] = kv.Value;
                return o;
            });
            Add("serial.write", "{text, board?}", p =>
            {
                R.WriteSerial(J.ReqStr(p, "text"), J.Str(p, "board"));
                return true;
            });
            Add("firmware.set", "{board, hex? | path?} — sıfırlamada yüklenir / applied on the next reset", p =>
            {
                var b = R.ResolveBoard(J.Str(p, "board"));
                string? hex = J.Str(p, "hex") ?? (J.Str(p, "path") is { } path ? File.ReadAllText(path) : null);
                if (hex == null) R.FirmwareOverrides.Remove(b.RefDes);
                else R.FirmwareOverrides[b.RefDes] = hex;
                return b.ToJson();
            });
            Add("firmware.enable", "{enabled} — kart yazılımlarını aç/kapa (sıfırlar) / run firmware or not (resets)", p =>
            {
                R.Options.Firmware = J.Bool(p, "enabled", true);
                R.Reset();
                return R.Status();
            });

            // ---------------------------------------------------------------- circuit
            Add("circuit.voltage", "{node: net adı | \"nesne:pin\"} → V", p => J.N(R.NodeVoltage(J.ReqStr(p, "node"))));
            Add("circuit.part", "{ref: \"O3\" | \"O1/R2\" | refdes} → akım, sıcaklık, arıza / currents, temperature, failure", p =>
                R.PartState(R is WorldRunner wr ? wr.PartRef(J.ReqStr(p, "ref")) : J.ReqStr(p, "ref")));
            Add("circuit.signals", "{prefix, max=500} → çözücü sinyalleri / raw solver signals", p => R.Signals(J.Str(p, "prefix") ?? string.Empty, J.Int(p, "max", 500)));
        }

        // ================================================================ load / reset / step

        private void Load(JsonObject p)
        {
            var options = new RunnerOptions
            {
                Library = _host.Library,
                Seed = J.Int(p, "seed", 0),
                Firmware = J.Bool(p, "firmware", true),
                MaxStep = J.Num(p, "max_step", 2e-4),
                RelTol = J.Num(p, "rel_tol", 1e-3),
                EnableDamage = J.Bool(p, "damage", true),
                Esp32Builder = _host.Esp32Builder
            };
            options.FirmwareSearchPath.AddRange(_host.FirmwareSearchPath);
            SimRunner runner;
            string? path = J.Str(p, "path");
            string? example = J.Str(p, "example");
            string? json = J.Str(p, "world_json");
            if (path != null)
            {
                if (!File.Exists(path)) throw ApiException.Missing("Dosya / file '" + path + "'");
                if (path.EndsWith(WorldDocument.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    var world = WorldDocument.Load(path);
                    ApplyEnvironment(world, p);
                    runner = new WorldRunner(world, options, GeometryFor(world, path, J.Str(p, "geometry")));
                }
                else
                {
                    var pkg = ProjectPackage.Load(path);
                    ApplyArena(pkg.Schematic, p);
                    runner = new VehicleRunner(pkg.Schematic, pkg, options);
                }
            }
            else if (json != null)
            {
                var world = WorldDocument.FromJson(json);
                ApplyEnvironment(world, p);
                runner = new WorldRunner(world, options, GeometryFor(world, null, J.Str(p, "geometry")));
            }
            else if (example != null)
            {
                if (example == "car")
                {
                    var world = WorldExamples.Car(options.Library);
                    ApplyEnvironment(world, p);
                    runner = new WorldRunner(world, options, GeometryFor(world, null, J.Str(p, "geometry")));
                }
                else
                {
                    var sch = VehicleRunner.Template(example, options.Library) ?? throw ApiException.BadParam("example", "car | line-follower | obstacle-avoider | quadcopter");
                    ApplyArena(sch, p);
                    runner = new VehicleRunner(sch, null, options);
                }
            }
            else
            {
                throw ApiException.BadParam("path", "path, example veya world_json gerekli / one of path, example, world_json is required");
            }

            Runner?.Dispose();
            Runner = runner;
            _loadParams = (JsonObject)p.DeepClone();
            if (J.Get(p, "sensors") is JsonArray sensors)
            {
                foreach (var s in sensors)
                {
                    if (s is JsonObject so) Call("sensor.add", so);
                }
            }
        }

        /// <summary>world.load "environment": {track, track_scale, friction, gravity, ambient_c, obstacles: [{x, y, size_x, size_y, height}], clear_obstacles}.</summary>
        private static void ApplyEnvironment(WorldDocument world, JsonObject p)
        {
            if (!(J.Get(p, "environment") is JsonObject e)) return;
            var env = world.Environment;
            if (J.Str(e, "track") is { } track) env.Track = track;
            if (J.Num(e, "track_scale") is { } scale) env.TrackScale = scale;
            if (J.Num(e, "friction") is { } fr) env.Friction = fr;
            if (J.Num(e, "gravity") is { } g) env.Gravity = g;
            if (J.Num(e, "ambient_c") is { } t) env.AmbientC = t;
            if (J.Bool(e, "clear_obstacles", false)) env.Obstacles.Clear();
            if (J.Get(e, "obstacles") is JsonArray obstacles)
            {
                foreach (var n in obstacles)
                {
                    if (!(n is JsonObject o)) continue;
                    env.Obstacles.Add(new WorldObstacle
                    {
                        X = J.Num(o, "x", 0), Y = J.Num(o, "y", 0),
                        SizeX = J.Num(o, "size_x", 0.2), SizeY = J.Num(o, "size_y", 0.2), Height = J.Num(o, "height", 0.15)
                    });
                }
            }
        }

        /// <summary>world.load "arena" for vehicles: oval, eight, obstacles, empty … (the Montaj page's arenas).</summary>
        private static void ApplyArena(Schematic sch, JsonObject p)
        {
            if (J.Str(p, "arena") is { } arena && sch.Assembly != null) sch.Assembly.Arena = arena;
        }

        private IWorldGeometry GeometryFor(WorldDocument world, string? path, string? bakedPath)
        {
            var approx = new ApproximateGeometry(world, _host.Library);
            if (bakedPath != null) return BakedGeometry.Parse(File.ReadAllText(bakedPath), approx);
            if (_host.Geometry != null) return _host.Geometry(world, path);
            if (path != null && BakedGeometry.TryLoadBeside(path, approx) is { } baked) return baked;
            return approx;
        }

        private JsonNode Reset(JsonObject? p)
        {
            var r = R;
            int? seed = J.Num(p, "seed") is { } s ? (int)s : (int?)null;
            r.Reset(seed);
            if (J.Get(p, "randomize") is JsonObject rnd)
            {
                var noise = new NoiseSource(r.Seed * 7919 + 17);
                r.Randomize(noise, J.Num(rnd, "position", 0.02), J.Num(rnd, "yaw_deg", 5) * Math.PI / 180);
            }

            var result = new JsonObject { ["time"] = J.N(r.Time), ["seed"] = r.Seed };
            var observe = J.StrList(p, "observe");
            if (observe.Count > 0) result["observation"] = ObserveAll(observe);
            return result;
        }

        private JsonNode Step(JsonObject? p)
        {
            var r = R;
            if (J.Get(p, "actions") is JsonArray actions)
            {
                foreach (var a in actions)
                {
                    if (a is JsonObject ao) Act(ao);
                }
            }

            double dt = J.Num(p, "dt", 0.01);
            if (!(dt > 0) || dt > 10) throw ApiException.BadParam("dt", "0 < dt ≤ 10 s");
            int steps = J.Int(p, "steps", 1);
            if (J.Num(p, "duration") is { } duration) steps = Math.Max(1, (int)Math.Round(duration / dt));
            if (steps < 0 || steps > 10_000_000) throw ApiException.BadParam("steps", "0..1e7");
            double pace = J.Num(p, "pace", 0);
            bool stopOnFailure = J.Bool(p, "stop_on_failure", false);
            int failedBefore = r.Failed.Count;
            var wall = Stopwatch.StartNew();
            double simTime = 0;
            int done = 0;
            for (int i = 0; i < steps; i++)
            {
                double covered = r.Step(dt);
                simTime += covered;
                done++;
                if (pace > 0)
                {
                    double ahead = simTime / pace - wall.Elapsed.TotalSeconds;
                    if (ahead > 0.0005) Thread.Sleep(TimeSpan.FromSeconds(ahead));
                }

                if (r.Halted || (stopOnFailure && r.Failed.Count > failedBefore)) break;
            }

            double wallTime = wall.Elapsed.TotalSeconds;
            var result = new JsonObject
            {
                ["time"] = J.N(r.Time),
                ["steps"] = done,
                ["sim_time"] = J.N(simTime),
                ["wall_time"] = J.N(wallTime),
                ["rtf"] = J.N(wallTime > 0 ? simTime / wallTime : double.NaN),
                ["halted"] = r.Halted,
                ["failed"] = J.Strs(r.Failed.OrderBy(x => x, StringComparer.Ordinal)),
                ["events_pending"] = r.PendingEvents
            };
            var observe = J.StrList(p, "observe");
            if (observe.Count > 0) result["observation"] = ObserveAll(observe);
            if (J.Bool(p, "events", false)) result["events"] = new JsonArray(r.DrainEvents().Select(e => (JsonNode)e.ToJson()).ToArray());
            return result;
        }

        /// <summary>One action of sim.step: {actuator|name, mode, value} | {pin, board?, mode,...} | {drive: {v, w}} | {flight: {...}}.</summary>
        private void Act(JsonObject a)
        {
            if (J.Get(a, "drive") is JsonObject d)
            {
                R.DriveDifferential(J.ReqNum(d, "v"), J.Num(d, "w", 0), d);
            }
            else if (J.Get(a, "flight") is JsonObject f)
            {
                V.SetFlight(f);
            }
            else if (J.Str(a, "pin") is { } pin)
            {
                R.WritePin(J.Str(a, "board"), pin, J.ReqStr(a, "mode"), J.Num(a, "duty", 0), J.Num(a, "frequency", 490), J.Num(a, "angle", 90));
            }
            else if ((J.Str(a, "actuator") ?? J.Str(a, "name")) is { } name)
            {
                R.SetActuator(name, a);
            }
            else if (J.Str(a, "method") is { } method)
            {
                Call(method, a["params"] as JsonObject);
            }
            else
            {
                throw ApiException.BadParam("actions", "{actuator, mode, value} | {pin, mode} | {drive} | {flight} | {method, params}");
            }
        }

        private JsonObject ObserveAll(IEnumerable<string> names)
        {
            var o = new JsonObject();
            foreach (var n in names) o[n] = Observe(n);
            return o;
        }

        /// <summary>A sensor by name, "pose:&lt;object&gt;", "status", "flight", or a part's circuit state ("part:&lt;ref&gt;").</summary>
        private JsonNode? Observe(string name)
        {
            var r = R;
            if (r.Sensors.TryGet(name, out _)) return r.Sensors.Read(name);
            if (name.StartsWith("pose:", StringComparison.Ordinal)) return Pose(ObjectId(name.Substring(5)));
            if (name.StartsWith("part:", StringComparison.Ordinal)) return Call("circuit.part", new JsonObject { ["ref"] = name.Substring(5) });
            if (name.StartsWith("pin:", StringComparison.Ordinal))
            {
                var parts = name.Substring(4).Split('.');
                return parts.Length == 2 ? r.ReadPin(parts[0], parts[1]) : r.ReadPin(null, parts[0]);
            }

            if (name == "status") return r.Status();
            if (name == "flight") return V.FlightStatus();
            throw ApiException.Missing("Gözlem / observation '" + name + "'");
        }

        private string ObjectId(string key)
        {
            if (R is WorldRunner w) return w.ResolveObject(key).Id;
            if (R.TryGetBody(key, out _)) return key;
            throw ApiException.Missing("Nesne / object '" + key + "'");
        }

        private JsonObject Pose(string id)
        {
            if (!R.TryGetBody(id, out var s)) throw ApiException.Missing("Gövde / body '" + id + "'");
            var (roll, pitch, yaw) = s.Orientation.ToEuler();
            return new JsonObject
            {
                ["id"] = id,
                ["position"] = J.V(s.Position),
                ["orientation"] = J.Q(s.Orientation),
                ["rpy"] = J.Arr(new[] { roll, pitch, yaw }),
                ["linear_velocity"] = J.V(s.Velocity),
                ["angular_velocity"] = J.V(s.AngularVelocity)
            };
        }
    }
}
