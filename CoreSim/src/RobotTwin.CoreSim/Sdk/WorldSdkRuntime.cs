using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sdk.Expressions;

namespace RobotTwin.CoreSim.Sdk
{
    public enum SdkEventLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>A message for the world's event log (Turkish).</summary>
    public readonly struct SdkEvent
    {
        public SdkEvent(double time, string source, string message, SdkEventLevel level)
        {
            Time = time;
            Source = source;
            Message = message;
            Level = level;
        }

        public double Time { get; }
        public string Source { get; }
        public string Message { get; }
        public SdkEventLevel Level { get; }
        public override string ToString() => $"{Time:0.000} {Source}: {Message}";
    }

    /// <summary>Live state of one behaviour or field (shown in the Scripts &amp; Plugins panel).</summary>
    public sealed class SdkRunnerStatus
    {
        public string Id { get; internal set; } = string.Empty;
        public string Name { get; internal set; } = string.Empty;
        public string Kind { get; internal set; } = string.Empty;
        public string? ObjectId { get; internal set; }
        public bool Active { get; internal set; }
        public int Errors { get; internal set; }
        public string? LastError { get; internal set; }

        /// <summary>Average wall time per physics step (ms).</summary>
        public double AverageMs { get; internal set; }
    }

    /// <summary>Environment readout for one object (inspector).</summary>
    public readonly struct AeroReadout
    {
        public AeroReadout(Vec3 wind, Vec3 relativeAir, Vec3 drag, Vec3 buoyancy, Vec3 sdkForce, Vec3 dragArea)
        {
            Wind = wind;
            RelativeAir = relativeAir;
            Drag = drag;
            Buoyancy = buoyancy;
            SdkForce = sdkForce;
            DragArea = dragArea;
        }

        public Vec3 Wind { get; }

        /// <summary>Air velocity relative to the body (wind − body velocity).</summary>
        public Vec3 RelativeAir { get; }

        public Vec3 Drag { get; }
        public Vec3 Buoyancy { get; }
        public Vec3 SdkForce { get; }
        public Vec3 DragArea { get; }
    }

    /// <summary>
    /// The world's environment and user code, run inside the fixed physics step of
    /// <see cref="WorldPhysics"/>: air density and wind (with turbulence and gusts, seeded),
    /// aerodynamic drag on every body (through <see cref="PhysicsScene.WindProvider"/> and
    /// <see cref="RigidBody.DragArea"/>), buoyancy, force fields, wind sources, expression
    /// behaviours and plugin/script behaviours. Every user call is isolated: exceptions are
    /// caught, reported as events and the offender is switched off after repeated errors.
    /// </summary>
    public sealed class WorldSdkRuntime
    {
        /// <summary>Errors after which a behaviour or field is switched off for the rest of the run.</summary>
        public const int MaxErrors = 3;

        /// <summary>Registry used when <see cref="Attach"/> is not given one (hosts may replace it).</summary>
        public static Func<BehaviorRegistry> DefaultRegistry { get; set; } = () => BehaviorRegistry.Shared;

        private readonly WorldDocument _world;
        private readonly WorldSdkSettings _s;
        private readonly WorldCircuit? _circuit;
        private readonly BehaviorRegistry _registry;
        private readonly List<SimBody> _bodies = new List<SimBody>();
        private readonly Dictionary<SceneBody, SimBody> _bySceneBody = new Dictionary<SceneBody, SimBody>();
        private readonly List<SimJoint> _joints = new List<SimJoint>();
        private readonly List<Runner> _runners = new List<Runner>();
        private readonly List<WindRunner> _windSources = new List<WindRunner>();
        private readonly Dictionary<string, double> _published = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private readonly List<SdkEvent> _events = new List<SdkEvent>();
        private readonly List<SensorDevice> _sensors = new List<SensorDevice>();
        private readonly HashSet<SceneBody> _buoyant = new HashSet<SceneBody>();
        private readonly AirState _air;
        private readonly CircuitAccess _circuitAccess;
        private AnalogSimulator? _sim;
        private bool _started, _stopped;
        private double _dt = 1e-3;
        private int _jointCount = -1;

        private WorldSdkRuntime(WorldDocument world, WorldPhysics physics, WorldCircuit? circuit, BehaviorRegistry registry)
        {
            _world = world;
            _s = world.Sdk ?? new WorldSdkSettings();
            Physics = physics;
            _circuit = circuit;
            _registry = registry;
            var env = world.Environment;
            TemperatureC = env.AmbientC;
            Pressure = Atmosphere.PressureAt(_s.Atmosphere.PressureHPa * 100, _s.Atmosphere.AltitudeM);
            AirDensity = _s.Atmosphere.DensityOverride is double d ? Math.Max(0, d)
                : env.AirDensity is double legacy ? Math.Max(0, legacy)
                : Atmosphere.DensityFor(_s.Atmosphere, env.AmbientC);
            Wind = new WindModel(_s.Wind, new Vec3(env.WindX, env.WindY, env.WindZ), env.Gust, _s.Seed);
            _air = new AirState(this);
            _circuitAccess = new CircuitAccess(this);
        }

        /// <summary>Builds the runtime for a freshly built world and wires it into the scene (called by WorldPhysics.Build).</summary>
        public static WorldSdkRuntime Attach(WorldDocument world, WorldPhysics physics, WorldCircuit? circuit, BehaviorRegistry? registry = null)
        {
            var r = new WorldSdkRuntime(world, physics, circuit, registry ?? DefaultRegistry());
            r.Setup();
            return r;
        }

        public WorldPhysics Physics { get; }
        public PhysicsScene Scene => Physics.Scene;
        public WindModel Wind { get; }
        public double AirDensity { get; }
        public double TemperatureC { get; }
        public double Pressure { get; }
        public double Time { get; private set; }
        public long StepIndex { get; private set; }

        public IReadOnlyList<ISimBody> Bodies => _bodies;
        public IReadOnlyList<ISimJoint> Joints => _joints;
        public IAirState Air => _air;

        /// <summary>Values published by behaviours and expression locals ("Ad.değişken").</summary>
        public IReadOnlyDictionary<string, double> Published => _published;

        /// <summary>True when there is any wind (mean, turbulence, gusts or a wind source).</summary>
        public bool HasWind => !Wind.IsCalm || _windSources.Count > 0;

        public IReadOnlyList<SdkRunnerStatus> Status => _runners.Select(r => r.Snapshot()).Concat(_windSources.Select(w => w.Snapshot())).ToList();

        public List<SdkEvent> DrainEvents()
        {
            var list = _events.ToList();
            _events.Clear();
            return list;
        }

        internal void Report(string source, string message, SdkEventLevel level)
        {
            if (_events.Count < 500) _events.Add(new SdkEvent(Time, source, message, level));
        }

        // ================================================================ setup

        private void Setup()
        {
            RefreshBodies();
            RefreshJoints();
            var aero = _s.Aero;
            var shapeDrag = new List<RobotTwin.CoreSim.Mechanics.SceneBody>();
            foreach (var sb in _bodies)
            {
                if (sb.Raw.IsStatic) continue;
                if (sb.Raw.Tag is WorldObject o)
                {
                    var ov = _s.AeroOf(o.Id);
                    bool drag = aero.Drag && (ov?.Drag ?? true);
                    sb.Raw.DragArea = drag ? Aerodynamics.DragArea(sb.Raw.Shape, ov?.Cd, ov?.AreaM2) : Vec3.Zero;
                    if (drag && ov?.Cd == null && ov?.AreaM2 == null) shapeDrag.Add(sb.Raw);   // a user's Cd / area stays as given
                    if (aero.Buoyancy || ov?.Buoyancy == true)
                    {
                        _buoyant.Add(sb.Raw);
                        if (ov?.VolumeM3 is double v) sb.VolumeOverride = v;
                    }
                }
                else if (!aero.Drag) sb.Raw.DragArea = Vec3.Zero;
            }

            // [drag] joined parts shield each other's faces: leading face, base and skin friction (Mechanics/AssemblyDrag.cs)
            RobotTwin.CoreSim.Mechanics.AssemblyDrag.Apply(Scene, shapeDrag);
            Scene.AirDensity = AirDensity;
            BuildFields();
            BuildBehaviors();
            Scene.Wind = Vec3.Zero;
            Scene.GustIntensity = 0;
            Scene.WindProvider = HasWind ? (p, _) => WindAt(p) : (Func<Vec3, double, Vec3>?)null;
            if (_circuit != null)
            {
                foreach (var list in _circuit.Compiled.DevicesByPart.Values)
                {
                    foreach (var d in list.OfType<SensorDevice>()) _sensors.Add(d);
                }
            }
        }

        private void RefreshBodies()
        {
            if (_bodies.Count == Scene.Bodies.Count && _bodies.Count > 0) return;
            foreach (var b in Scene.Bodies)
            {
                if (_bySceneBody.ContainsKey(b)) continue;
                var sb = new SimBody(b, this);
                _bodies.Add(sb);
                _bySceneBody[b] = sb;
            }

            if (_bodies.Count != Scene.Bodies.Count)
            {
                var live = new HashSet<SceneBody>(Scene.Bodies);
                _bodies.RemoveAll(x => !live.Contains(x.Raw));
            }
        }

        private void RefreshJoints()
        {
            if (_jointCount == Scene.Joints.Count) return;
            _jointCount = Scene.Joints.Count;
            _joints.Clear();
            foreach (var j in Scene.Joints) _joints.Add(new SimJoint(j, this));
        }

        internal SimBody? Wrap(SceneBody? b) => b != null && _bySceneBody.TryGetValue(b, out var s) ? s : null;

        public ISimBody? FindBody(string idOrName)
        {
            if (string.IsNullOrWhiteSpace(idOrName)) return null;
            string key = idOrName.Trim();
            return _bodies.FirstOrDefault(b => string.Equals(b.Id, key, StringComparison.OrdinalIgnoreCase))
                   ?? _bodies.FirstOrDefault(b => string.Equals(b.Name, key, StringComparison.OrdinalIgnoreCase));
        }

        private bool PluginEnabled(string key, string what)
        {
            string plugin = BehaviorRegistry.PluginOf(key);
            if (plugin == BehaviorRegistry.BuiltinId || plugin == BehaviorRegistry.ScriptId) return true;
            if (_s.EnabledPlugins.Any(p => string.Equals(p, plugin, StringComparison.OrdinalIgnoreCase))) return true;
            Report(what, $"'{plugin}' eklentisi bu dünyada etkin değil; Betikler / Eklentiler panelinden etkinleştirin.", SdkEventLevel.Warning);
            return false;
        }

        private void BuildFields()
        {
            foreach (var f in _s.Fields)
            {
                if (!f.Enabled) continue;
                string name = string.IsNullOrWhiteSpace(f.Name) ? (f.Kind ?? "alan") : f.Name;
                var targets = new HashSet<string>(f.Targets, StringComparer.OrdinalIgnoreCase);
                switch ((f.Kind ?? string.Empty).ToLowerInvariant())
                {
                    case "fan":
                    {
                        var spec = f;
                        _windSources.Add(new WindRunner(f.Id, name, "Fan / hava akımı", p => FieldGeometry.Fan(spec, p)));
                        break;
                    }

                    case "vortex" when !f.Acceleration:
                    {
                        var spec = f;
                        _windSources.Add(new WindRunner(f.Id, name, "Girdap (rüzgâr)", p => FieldGeometry.Vortex(spec, p)));
                        break;
                    }

                    case "expression":
                    {
                        ExpressionForce program;
                        try
                        {
                            program = new ExpressionForce(f.Expression ?? string.Empty, true, f.Parameters);
                        }
                        catch (ExpressionException ex)
                        {
                            Report(name, "İfade derlenemedi — " + ex.Message, SdkEventLevel.Error);
                            break;
                        }

                        var spec = f;
                        if (program.WritesWind)
                        {
                            _windSources.Add(new WindRunner(f.Id, name, "İfade (rüzgâr)",
                                p => spec.Bounded && !FieldGeometry.Inside(spec, p) ? Vec3.Zero : program.WindAt(p, Time, Wind.At(p), Scene.Gravity, AirDensity, TemperatureC)));
                        }

                        if (program.WritesForce) _runners.Add(new FieldRunner(this, f, name, "İfade alanı", targets, new ExpressionFieldBody(this, spec, program)));
                        break;
                    }

                    case "plugin":
                    {
                        var info = f.PluginType != null ? _registry.Find(f.PluginType) : null;
                        if (info == null)
                        {
                            Report(name, $"Alan türü '{f.PluginType}' bulunamadı (eklenti yüklü mü?).", SdkEventLevel.Warning);
                            break;
                        }

                        if (!PluginEnabled(info.Key, name)) break;
                        object instance;
                        try
                        {
                            instance = BehaviorRegistry.Create(info, f.Parameters, w => Report(name, w, SdkEventLevel.Warning));
                            if (instance is ForceField ff) ff.Configure(f.Parameters, f);
                        }
                        catch (Exception ex)
                        {
                            Report(name, "Alan oluşturulamadı — " + Describe(ex), SdkEventLevel.Error);
                            break;
                        }

                        if (instance is IForceField field) _runners.Add(new FieldRunner(this, f, name, "Eklenti alanı", targets, new PluginFieldBody(field)));
                        if (instance is IWindSource ws)
                        {
                            var src = ws;
                            _windSources.Add(new WindRunner(f.Id, name, "Eklenti rüzgârı", p => src.WindAt(p, Time)));
                        }

                        break;
                    }

                    default:
                        _runners.Add(new FieldRunner(this, f, name, KindName(f.Kind), targets, new BuiltinFieldBody(this, f)));
                        break;
                }
            }
        }

        private static string KindName(string? kind) => (kind ?? string.Empty).ToLowerInvariant() switch
        {
            "uniform" => "Düzgün alan",
            "radial" => "Merkezcil alan",
            "vortex" => "Girdap (kuvvet)",
            "conveyor" => "Konveyör bölgesi",
            _ => kind ?? "?"
        };

        private void BuildBehaviors()
        {
            foreach (var a in _s.Behaviors)
            {
                if (!a.Enabled) continue;
                SimBody? self = null;
                string objName = string.Empty;
                if (!string.IsNullOrEmpty(a.ObjectId))
                {
                    var o = _world.Find(a.ObjectId);
                    self = _bodies.FirstOrDefault(b => b.Raw.Tag is WorldObject wo && string.Equals(wo.Id, a.ObjectId, StringComparison.OrdinalIgnoreCase));
                    objName = o?.Name ?? a.ObjectId!;
                    if (self == null)
                    {
                        Report(a.Name.Length > 0 ? a.Name : a.Type, $"'{objName}' nesnesinin fizik gövdesi yok; davranış çalışmayacak.", SdkEventLevel.Warning);
                        continue;
                    }
                }

                if (a.IsExpression)
                {
                    string name = a.Name.Length > 0 ? a.Name : (objName.Length > 0 ? objName + " · ifade" : "İfade");
                    if (self == null)
                    {
                        Report(name, "İfade davranışı bir nesneye bağlı olmalı (dünya geneli için 'İfade alanı' kullanın).", SdkEventLevel.Warning);
                        continue;
                    }

                    try
                    {
                        var program = new ExpressionForce(a.Expression ?? string.Empty, false, a.Parameters);
                        _runners.Add(new BehaviorRunner(this, a, name, "İfade", self, new ExpressionBehavior(program, name)));
                    }
                    catch (ExpressionException ex)
                    {
                        Report(name, "İfade derlenemedi — " + ex.Message, SdkEventLevel.Error);
                    }

                    continue;
                }

                var info = _registry.Find(a.Type);
                string label = a.Name.Length > 0 ? a.Name : (info?.Name ?? a.Type) + (objName.Length > 0 ? " · " + objName : string.Empty);
                if (info == null || info.Kind != SdkTypeKind.Behavior)
                {
                    Report(label, $"Davranış türü '{a.Type}' bulunamadı (eklenti/betik yüklü mü?).", SdkEventLevel.Warning);
                    continue;
                }

                if (!PluginEnabled(info.Key, label)) continue;
                try
                {
                    var instance = (IWorldBehavior)BehaviorRegistry.Create(info, a.Parameters, w => Report(label, w, SdkEventLevel.Warning));
                    _runners.Add(new BehaviorRunner(this, a, label, info.PluginId == BehaviorRegistry.ScriptId ? "Betik" : info.PluginId == BehaviorRegistry.BuiltinId ? "Hazır" : "Eklenti", self, instance));
                }
                catch (Exception ex)
                {
                    Report(label, "Davranış oluşturulamadı — " + Describe(ex), SdkEventLevel.Error);
                }
            }
        }

        // ================================================================ stepping

        /// <summary>Before the solver: wind, buoyancy, fields, behaviours (forces act for the whole step).</summary>
        public void BeforeStep(double dt, AnalogSimulator? sim)
        {
            if (_stopped) return;
            _sim = sim;
            _dt = dt;
            if (_bodies.Count != Scene.Bodies.Count) RefreshBodies();
            RefreshJoints();
            if (!_started)
            {
                _started = true;
                foreach (var r in _runners) r.Start();
            }

            Wind.Advance(dt);
            foreach (var b in _bodies) b.BeginStep();
            if (_buoyant.Count > 0)
            {
                var g = Scene.Gravity;
                foreach (var b in _bodies)
                {
                    if (!_buoyant.Contains(b.Raw) || b.Raw.IsStatic) continue;
                    b.ApplyForce(g * (-AirDensity * b.Volume));
                }
            }

            double ang = _s.Aero.AngularDamping;
            if (ang > 0 && AirDensity > 0 && _s.Aero.Drag)
            {
                foreach (var b in _bodies)
                {
                    var raw = b.Raw;
                    if (raw.IsStatic || raw.DragArea.LengthSquared == 0) continue;
                    // Propellers already carry their aerodynamic torque Q = C_Q·ρ·n²·D⁵ (Mechanics/Propeller):
                    // damping them again would count the same air drag twice.
                    if (IsRotorAero(raw)) continue;
                    var w = raw.AngularVelocity;
                    double wl = w.Length;
                    if (wl < 1e-6) continue;
                    double r = raw.Shape?.BoundingRadius ?? 0.05;
                    // Rotational drag ~ c·ρ·r⁵·|ω|·ω, limited so that it cannot reverse the spin within a step.
                    double k = ang * AirDensity * Math.Pow(r, 5) * wl;
                    double imin = Math.Min(raw.Inertia.X, Math.Min(raw.Inertia.Y, raw.Inertia.Z));
                    k = Math.Min(k, imin / Math.Max(1e-6, dt));
                    raw.AddTorque(w * -k);
                }
            }

            foreach (var r in _runners) r.Step(dt);
        }

        private bool IsRotorAero(RobotTwin.CoreSim.Mechanics.SceneBody raw)
        {
            foreach (var pr in Scene.Propellers)
            {
                if (ReferenceEquals(pr.Body, raw)) return true;
            }

            return raw.Tag is WorldObject o && RobotTwin.CoreSim.Sandbox.ReadyParts.Of(o)?.Kind == RobotTwin.CoreSim.Sandbox.ReadyPartKind.Propeller;
        }

        /// <summary>After the solver: behaviours' OnAfterStep, sensor outputs, statistics.</summary>
        public void AfterStep(double dt, AnalogSimulator? sim)
        {
            if (_stopped) return;
            _sim = sim;
            foreach (var r in _runners) r.After(dt);
            if (_sensors.Count > 0)
            {
                foreach (var d in _sensors)
                {
                    if (d.Signal.Length > 0 && _published.TryGetValue(d.Signal, out double v)) d.Feed(v);
                }
            }

            Time += dt;
            StepIndex++;
            if (StepIndex % 2000 == 0) CheckBudget();
        }

        private void CheckBudget()
        {
            double budget = _s.StepBudgetMs;
            if (budget <= 0) return;
            foreach (var r in _runners.Cast<RunnerBase>().Concat(_windSources))
            {
                double avg = r.AverageMs;
                if (avg > budget && !r.BudgetWarned)
                {
                    r.BudgetWarned = true;
                    Report(r.Name, $"adım başına ortalama {avg.ToString("0.###", CultureInfo.InvariantCulture)} ms harcıyor (bütçe {budget.ToString("0.###", CultureInfo.InvariantCulture)} ms); " +
                                   "simülasyon gerçek zamanın gerisinde kalabilir.", SdkEventLevel.Warning);
                }
            }
        }

        /// <summary>Calls OnStop on every started behaviour. Further steps do nothing.</summary>
        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            if (_started) foreach (var r in _runners) r.Stop();
            Scene.WindProvider = null;
        }

        // ================================================================ wind

        /// <summary>Total air velocity at a point: wind model + every wind source (fans, vortices, expressions, plugins).</summary>
        public Vec3 WindAt(Vec3 p)
        {
            var w = Wind.At(p, Scene.GroundHeight(p.X, p.Y));
            for (int i = 0; i < _windSources.Count; i++)
            {
                var src = _windSources[i];
                if (!src.Active) continue;
                w += src.Sample(p, this);
            }

            return w;
        }

        /// <summary>Wind, drag and SDK forces on an object (for the inspector).</summary>
        public AeroReadout? Readout(string objectId)
        {
            var b = _bodies.FirstOrDefault(x => x.Raw.Tag is WorldObject o && string.Equals(o.Id, objectId, StringComparison.OrdinalIgnoreCase));
            if (b == null) return null;
            var wind = WindAt(b.Raw.Position);
            var rel = wind - b.Raw.Velocity;
            var drag = Aerodynamics.Drag(b.Raw, -rel, AirDensity);
            var buoy = _buoyant.Contains(b.Raw) ? Scene.Gravity * (-AirDensity * b.Volume) : Vec3.Zero;
            return new AeroReadout(wind, rel, drag, buoy, b.LastSdkForce, b.Raw.DragArea);
        }

        /// <summary>
        /// Wind for drawing when the world is not running: mean wind with its profile and the fans,
        /// vortices and expression winds at t = 0 (no turbulence or gusts).
        /// </summary>
        public static Func<Vec3, Vec3> Preview(WorldDocument world)
        {
            var s = world.Sdk ?? new WorldSdkSettings();
            var env = world.Environment;
            var calm = new WindSettings
            {
                Speed = s.Wind.Speed, DirectionDeg = s.Wind.DirectionDeg, Vertical = s.Wind.Vertical, Profile = s.Wind.Profile,
                ReferenceHeight = s.Wind.ReferenceHeight, PowerExponent = s.Wind.PowerExponent, Roughness = s.Wind.Roughness
            };
            var model = new WindModel(calm, new Vec3(env.WindX, env.WindY, env.WindZ), 0, s.Seed);
            double rho = Atmosphere.DensityFor(s.Atmosphere, env.AmbientC);
            var g = new Vec3(0, 0, -env.Gravity);
            var sources = new List<Func<Vec3, Vec3>>();
            foreach (var f in s.Fields.Where(f => f.Enabled))
            {
                var spec = f;
                switch ((f.Kind ?? string.Empty).ToLowerInvariant())
                {
                    case "fan":
                        sources.Add(p => FieldGeometry.Fan(spec, p));
                        break;
                    case "vortex" when !f.Acceleration:
                        sources.Add(p => FieldGeometry.Vortex(spec, p));
                        break;
                    case "expression":
                        try
                        {
                            var program = new ExpressionForce(f.Expression ?? string.Empty, true, f.Parameters);
                            if (program.WritesWind) sources.Add(p => spec.Bounded && !FieldGeometry.Inside(spec, p) ? Vec3.Zero : program.WindAt(p, 0, model.At(p), g, rho, env.AmbientC));
                        }
                        catch (ExpressionException)
                        {
                            // Shown in the panel; nothing to draw.
                        }

                        break;
                }
            }

            return p =>
            {
                var w = model.At(p);
                foreach (var src in sources)
                {
                    var v = src(p);
                    if (!double.IsNaN(v.X) && !double.IsNaN(v.Y) && !double.IsNaN(v.Z)) w += v;
                }

                return w;
            };
        }

        // ================================================================ helpers

        internal static string Describe(Exception ex)
        {
            if (ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null) ex = tie.InnerException;
            string where = string.Empty;
            var st = ex.StackTrace;
            if (!string.IsNullOrEmpty(st))
            {
                var first = st!.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
                if (first != null) where = " [" + (first.StartsWith("at ", StringComparison.Ordinal) ? first.Substring(3) : first) + "]";
            }

            return ex.GetType().Name + ": " + ex.Message + where;
        }

        internal void Publish(string name, double value) => _published[name] = value;

        internal double Read(string name) => _published.TryGetValue(name, out double v) ? v : double.NaN;

        internal AnalogSimulator? Simulator => _sim;
        internal WorldCircuit? CircuitModel => _circuit;
        internal WorldDocument WorldDoc => _world;
        internal double Dt => _dt;
        internal IBehaviorContext NewContext(Runner runner, SimBody? self, IReadOnlyDictionary<string, string> parameters, string label) =>
            new Context(this, runner, self, parameters, label);
        internal CircuitAccess CircuitView => _circuitAccess;

        // ================================================================ runners

        internal abstract class RunnerBase
        {
            protected long Ticks;
            protected long Calls;

            public string Id = string.Empty;
            public string Name = string.Empty;
            public string KindLabel = string.Empty;
            public string? ObjectId;
            public bool Active = true;
            public int Errors;
            public string? LastError;
            public bool BudgetWarned;

            public double AverageMs => Calls == 0 ? 0 : Ticks * 1000.0 / Stopwatch.Frequency / Calls;

            public SdkRunnerStatus Snapshot() => new SdkRunnerStatus
            {
                Id = Id, Name = Name, Kind = KindLabel, ObjectId = ObjectId, Active = Active, Errors = Errors, LastError = LastError, AverageMs = AverageMs
            };

            /// <summary>Records an exception; switches off after <see cref="MaxErrors"/>.</summary>
            protected void Fail(WorldSdkRuntime rt, string phase, Exception ex)
            {
                Errors++;
                LastError = Describe(ex);
                rt.Report(Name, $"{phase} hata (t = {rt.Time.ToString("0.000", CultureInfo.InvariantCulture)} s) — {LastError}", SdkEventLevel.Error);
                if (Errors >= MaxErrors && Active)
                {
                    Active = false;
                    rt.Report(Name, $"{MaxErrors} hatadan sonra devre dışı bırakıldı; düzeltip simülasyonu yeniden başlatın.", SdkEventLevel.Error);
                }
            }

            protected void FailText(WorldSdkRuntime rt, string message)
            {
                Errors++;
                LastError = message;
                rt.Report(Name, message, SdkEventLevel.Error);
                if (Errors >= MaxErrors && Active)
                {
                    Active = false;
                    rt.Report(Name, $"{MaxErrors} hatadan sonra devre dışı bırakıldı.", SdkEventLevel.Error);
                }
            }
        }

        internal abstract class Runner : RunnerBase
        {
            public abstract void Start();
            public abstract void Step(double dt);
            public virtual void After(double dt) { }
            public virtual void Stop() { }
        }

        private sealed class BehaviorRunner : Runner
        {
            private readonly WorldSdkRuntime _rt;
            private readonly IWorldBehavior _behavior;
            private readonly IBehaviorContext _ctx;
            private readonly bool _after;
            private bool _started;

            public BehaviorRunner(WorldSdkRuntime rt, BehaviorAttachment a, string name, string kind, SimBody? self, IWorldBehavior behavior)
            {
                _rt = rt;
                _behavior = behavior;
                Id = a.Id;
                Name = name;
                KindLabel = kind;
                ObjectId = a.ObjectId;
                _after = behavior is IAfterStep || (behavior is WorldBehavior wb && Overrides(wb));
                _ctx = rt.NewContext(this, self, a.Parameters, name);
            }

            private static bool Overrides(WorldBehavior wb)
            {
                var m = wb.GetType().GetMethod(nameof(WorldBehavior.OnAfterStep));
                return m != null && m.DeclaringType != typeof(WorldBehavior);
            }

            public override void Start()
            {
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    _behavior.OnStart(_ctx);
                    _started = true;
                }
                catch (Exception ex)
                {
                    Fail(_rt, "Başlatılırken", ex);
                    Active = false;
                }

                Ticks += Stopwatch.GetTimestamp() - t0;
            }

            public override void Step(double dt)
            {
                if (!Active) return;
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    _behavior.OnStep(_ctx, dt);
                }
                catch (Exception ex)
                {
                    Fail(_rt, "Adım sırasında", ex);
                }

                Ticks += Stopwatch.GetTimestamp() - t0;
                Calls++;
            }

            public override void After(double dt)
            {
                if (!Active || !_after) return;
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    if (_behavior is IAfterStep a) a.OnAfterStep(_ctx, dt);
                    else ((WorldBehavior)_behavior).OnAfterStep(_ctx, dt);
                }
                catch (Exception ex)
                {
                    Fail(_rt, "Adım sonrasında", ex);
                }

                Ticks += Stopwatch.GetTimestamp() - t0;
            }

            public override void Stop()
            {
                if (!_started) return;
                try
                {
                    _behavior.OnStop(_ctx);
                }
                catch (Exception ex)
                {
                    Fail(_rt, "Durdurulurken", ex);
                }
            }
        }

        /// <summary>Per-body evaluation of a field.</summary>
        internal interface IFieldBody
        {
            void Start(IBehaviorContext ctx);
            bool Affects(SimBody b);
            (Vec3 Force, Vec3 Torque) Evaluate(SimBody b, IBehaviorContext ctx);
        }

        private sealed class FieldRunner : Runner
        {
            private readonly WorldSdkRuntime _rt;
            private readonly ForceFieldSpec _spec;
            private readonly HashSet<string> _targets;
            private readonly IFieldBody _body;
            private readonly IBehaviorContext _ctx;

            public FieldRunner(WorldSdkRuntime rt, ForceFieldSpec spec, string name, string kind, HashSet<string> targets, IFieldBody body)
            {
                _rt = rt;
                _spec = spec;
                _targets = targets;
                _body = body;
                Id = spec.Id;
                Name = name;
                KindLabel = kind;
                _ctx = rt.NewContext(this, null, spec.Parameters, name);
            }

            public override void Start()
            {
                try
                {
                    _body.Start(_ctx);
                }
                catch (Exception ex)
                {
                    Fail(_rt, "Başlatılırken", ex);
                    Active = false;
                }
            }

            public override void Step(double dt)
            {
                if (!Active) return;
                long t0 = Stopwatch.GetTimestamp();
                foreach (var b in _rt._bodies)
                {
                    if (b.Raw.IsStatic) continue;
                    if (_targets.Count > 0 && !(b.Raw.Tag is WorldObject o && _targets.Contains(o.Id))) continue;
                    try
                    {
                        if (!_body.Affects(b)) continue;
                        var (f, t) = _body.Evaluate(b, _ctx);
                        if (!Finite(f) || !Finite(t))
                        {
                            FailText(_rt, $"'{b.Name}' için sayı olmayan kuvvet (NaN/∞) üretti; uygulanmadı.");
                            if (!Active) break;
                            continue;
                        }

                        if (f.LengthSquared > 0) b.ApplyForce(f);
                        if (t.LengthSquared > 0) b.ApplyTorque(t);
                    }
                    catch (Exception ex)
                    {
                        Fail(_rt, "Adım sırasında", ex);
                        if (!Active) break;
                    }
                }

                Ticks += Stopwatch.GetTimestamp() - t0;
                Calls++;
            }
        }

        internal static bool Finite(Vec3 v) => !(double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z) || double.IsInfinity(v.X) || double.IsInfinity(v.Y) || double.IsInfinity(v.Z));

        private sealed class WindRunner : RunnerBase
        {
            private readonly Func<Vec3, Vec3> _sample;

            public WindRunner(string id, string name, string kind, Func<Vec3, Vec3> sample)
            {
                Id = id;
                Name = name;
                KindLabel = kind;
                _sample = sample;
            }

            public Vec3 Sample(Vec3 p, WorldSdkRuntime rt)
            {
                long t0 = Stopwatch.GetTimestamp();
                try
                {
                    var v = _sample(p);
                    if (!Finite(v))
                    {
                        FailText(rt, "Rüzgâr kaynağı sayı olmayan bir hız (NaN/∞) üretti.");
                        return Vec3.Zero;
                    }

                    return v;
                }
                catch (Exception ex)
                {
                    Fail(rt, "Rüzgâr hesaplanırken", ex);
                    return Vec3.Zero;
                }
                finally
                {
                    Ticks += Stopwatch.GetTimestamp() - t0;
                    Calls++;
                }
            }
        }

        private sealed class BuiltinFieldBody : IFieldBody
        {
            private readonly WorldSdkRuntime _rt;
            private readonly ForceFieldSpec _f;

            public BuiltinFieldBody(WorldSdkRuntime rt, ForceFieldSpec f)
            {
                _rt = rt;
                _f = f;
            }

            public void Start(IBehaviorContext ctx) { }

            public bool Affects(SimBody b) => !_f.Bounded && _f.Kind != "conveyor" || FieldGeometry.Inside(_f, b.Position);

            public (Vec3 Force, Vec3 Torque) Evaluate(SimBody b, IBehaviorContext ctx)
            {
                double m = b.Mass;
                double scale = _f.Acceleration ? m : 1.0;
                switch ((_f.Kind ?? "uniform").ToLowerInvariant())
                {
                    case "uniform":
                        return (_f.Direction * (_f.Strength * scale), Vec3.Zero);
                    case "radial":
                    {
                        var d = _f.Center - b.Position;
                        double r = d.Length;
                        if (r < 1e-9) return (Vec3.Zero, Vec3.Zero);
                        double mag = _f.Strength * FieldGeometry.Falloff(_f, r) * scale;
                        return (d / r * mag, Vec3.Zero);
                    }

                    case "vortex":
                    {
                        // Acceleration mode: tangential (plus inflow/updraft) acceleration with the vortex's speed profile.
                        var v = FieldGeometry.Vortex(_f, b.Position);
                        return (v * m, Vec3.Zero);
                    }

                    case "conveyor":
                    {
                        var dir = _f.Direction;
                        var up = -_rt.Scene.Gravity.Normalized();
                        if (up.LengthSquared < 0.5) up = Vec3.UnitZ;
                        var belt = (dir - up * dir.Dot(up)).Normalized() * _f.Strength;
                        var v = b.Velocity - up * b.Velocity.Dot(up);
                        var dv = belt - v;
                        const double tau = 0.05;
                        // The belt replaces the floor under the body: besides relaxing the velocity to the belt
                        // speed, it cancels the floor's sliding friction (which the contact solver still applies).
                        double weight = m * _rt.Scene.Gravity.Length;
                        double floorMu = _rt.Scene.GroundFriction ?? 0.9;
                        var f = dv * (m / tau) + dv.Normalized() * (floorMu * weight * Math.Tanh(dv.Length / 0.01));
                        double max = (Math.Max(0, _f.Grip) + floorMu) * weight;
                        if (max > 0 && f.Length > max) f = f.Normalized() * max;
                        return (f, Vec3.Zero);
                    }

                    default:
                        return (Vec3.Zero, Vec3.Zero);
                }
            }
        }

        private sealed class ExpressionFieldBody : IFieldBody
        {
            private readonly WorldSdkRuntime _rt;
            private readonly ForceFieldSpec _f;
            private readonly ExpressionForce _p;
            private readonly Dictionary<SimBody, Vec3> _start = new Dictionary<SimBody, Vec3>();

            public ExpressionFieldBody(WorldSdkRuntime rt, ForceFieldSpec f, ExpressionForce p)
            {
                _rt = rt;
                _f = f;
                _p = p;
            }

            public void Start(IBehaviorContext ctx) { }

            public bool Affects(SimBody b) => !_f.Bounded || FieldGeometry.Inside(_f, b.Position);

            public (Vec3 Force, Vec3 Torque) Evaluate(SimBody b, IBehaviorContext ctx)
            {
                if (!_start.TryGetValue(b, out var s)) _start[b] = s = b.Position;
                return _p.ForceOn(b, _rt.Time, _rt.Dt, _rt.WindAt(b.Position), _rt.Scene.Gravity, _rt.AirDensity, _rt.TemperatureC, s);
            }
        }

        private sealed class PluginFieldBody : IFieldBody
        {
            private readonly IForceField _field;

            public PluginFieldBody(IForceField field) => _field = field;

            public void Start(IBehaviorContext ctx) { }

            public bool Affects(SimBody b) => _field.Affects(b);

            public (Vec3 Force, Vec3 Torque) Evaluate(SimBody b, IBehaviorContext ctx) => _field.Evaluate(b, ctx);
        }

        /// <summary>An expression program attached to one object.</summary>
        private sealed class ExpressionBehavior : IWorldBehavior
        {
            private readonly ExpressionForce _p;
            private readonly string _name;
            private Vec3 _start;

            public ExpressionBehavior(ExpressionForce p, string name)
            {
                _p = p;
                _name = name;
            }

            public void OnStart(IBehaviorContext ctx) => _start = ctx.Self?.Position ?? Vec3.Zero;

            public void OnStep(IBehaviorContext ctx, double dt)
            {
                var b = ctx.Self;
                if (b == null) return;
                var rt = ((Context)ctx).Runtime;
                var (f, t) = _p.ForceOn(b, ctx.Time, dt, rt.WindAt(b.Position), ctx.Gravity, ctx.Air.Density, ctx.Air.TemperatureC, _start);
                if (!Finite(f) || !Finite(t)) throw new ArithmeticException("İfade sayı olmayan bir kuvvet (NaN/∞) üretti (sıfıra bölme? negatif sayının kökü?).");
                if (f.LengthSquared > 0) b.ApplyForce(f);
                if (t.LengthSquared > 0) b.ApplyTorque(t);
                foreach (var (name, slot) in _p.Locals) ctx.Publish(_name + "." + name, _p.LocalValue(slot));
            }

            public void OnStop(IBehaviorContext ctx) { }
        }

        // ================================================================ adapters

        internal sealed class SimBody : ISimBody
        {
            private readonly WorldSdkRuntime _rt;
            private Vec3 _sdkForce;

            public SimBody(SceneBody raw, WorldSdkRuntime rt)
            {
                Raw = raw;
                _rt = rt;
                Object = raw.Tag as WorldObject;
                Id = raw.Id;
                if (Object != null)
                {
                    int colon = raw.Id.IndexOf(':');
                    Name = colon > 0 ? Object.Name + raw.Id.Substring(colon) : Object.Name;
                }
                else Name = raw.Id;
            }

            public SceneBody Raw { get; }
            public string Id { get; }
            public string Name { get; }
            public WorldObject? Object { get; }
            public bool IsStatic => Raw.IsStatic;
            public double Mass => Raw.IsStatic ? double.PositiveInfinity : Raw.Mass;
            public Vec3 Inertia => Raw.Inertia;
            public Vec3 Position => Raw.Position;
            public Quat Orientation => Raw.Orientation;
            public Vec3 Velocity => Raw.Velocity;
            public Vec3 AngularVelocity => Raw.AngularVelocity;
            public double? VolumeOverride { get; set; }
            public double Volume => VolumeOverride ?? Raw.Shape?.Volume ?? 0;
            public string Material => Raw.Material;

            public Vec3 DragArea
            {
                get => Raw.DragArea;
                set
                {
                    Raw.DragArea = value;
                    if (value.LengthSquared > 0 && _rt.Scene.WindProvider == null && _rt.HasWind) _rt.Scene.WindProvider = (p, _) => _rt.WindAt(p);
                }
            }

            public Vec3 LastSdkForce { get; private set; }

            internal void BeginStep()
            {
                LastSdkForce = _sdkForce;
                _sdkForce = Vec3.Zero;
            }

            public Vec3 LocalToWorld(Vec3 local) => Raw.ToWorld(local);
            public Vec3 WorldToLocal(Vec3 world) => Raw.Orientation.InverseRotate(world - Raw.Position);
            public Vec3 PointVelocity(Vec3 worldPoint) => Raw.PointVelocity(worldPoint);

            public void ApplyForce(Vec3 force)
            {
                if (Raw.IsStatic || !Finite(force)) return;
                Raw.AddForce(force);
                _sdkForce += force;
            }

            public void ApplyForceAtPoint(Vec3 force, Vec3 worldPoint)
            {
                if (Raw.IsStatic || !Finite(force) || !Finite(worldPoint)) return;
                Raw.AddForceAtPoint(force, worldPoint);
                _sdkForce += force;
            }

            public void ApplyLocalForce(Vec3 localForce, Vec3 localPoint) => ApplyForceAtPoint(Raw.Orientation.Rotate(localForce), Raw.ToWorld(localPoint));

            public void ApplyTorque(Vec3 torque)
            {
                if (Raw.IsStatic || !Finite(torque)) return;
                Raw.AddTorque(torque);
            }

            public void Teleport(Vec3 position, Quat orientation)
            {
                Raw.Position = position;
                Raw.Orientation = orientation.Normalized();
            }

            public void SetVelocity(Vec3 linear, Vec3 angular)
            {
                if (Raw.IsStatic) return;
                Raw.Velocity = linear;
                Raw.AngularVelocity = angular;
            }

            public override string ToString() => Name;
        }

        internal sealed class SimJoint : ISimJoint
        {
            private readonly SceneJoint _j;
            private readonly WorldSdkRuntime _rt;

            public SimJoint(SceneJoint j, WorldSdkRuntime rt)
            {
                _j = j;
                _rt = rt;
            }

            public string Id => _j.Id;
            public JointKind Kind => _j.Kind;
            public double Angle => _j.Angle;
            public double Speed => _j.Speed;
            public bool Broken => _j.Broken;
            public Vec3 Axis => _j.WorldAxis;
            public Vec3 Anchor => _j.WorldAnchor;

            public double MotorTorque
            {
                get => _j.MotorTorque;
                set => _j.MotorTorque = double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
            }

            public JointDrive Drive
            {
                get => _j.Drive;
                set => _j.Drive = value;
            }

            public double TargetAngle
            {
                get => _j.TargetAngle;
                set => _j.TargetAngle = value;
            }

            public double TargetSpeed
            {
                get => _j.TargetSpeed;
                set => _j.TargetSpeed = value;
            }

            public double Stiffness
            {
                get => _j.Stiffness;
                set => _j.Stiffness = value;
            }

            public double Damping
            {
                get => _j.Damping;
                set => _j.Damping = value;
            }

            public double MaxTorque
            {
                get => _j.MaxTorque;
                set => _j.MaxTorque = value;
            }

            public Vec3 ReactionForce => _j.ReactionForce;
            public Vec3 ReactionTorque => _j.ReactionTorque;
            public ISimBody? Parent => _j.B != null ? _rt.Wrap(_j.A) : null;
            public ISimBody Child => (ISimBody?)_rt.Wrap(_j.B ?? _j.A) ?? throw new InvalidOperationException("Mafsal gövdesi bulunamadı.");
        }

        private sealed class AirState : IAirState
        {
            private readonly WorldSdkRuntime _rt;

            public AirState(WorldSdkRuntime rt) => _rt = rt;

            public double Density => _rt.AirDensity;
            public double TemperatureC => _rt.TemperatureC;
            public double Pressure => _rt.Pressure;
            public Vec3 WindAt(Vec3 point) => _rt.WindAt(point);
        }

        internal sealed class CircuitAccess : ICircuitAccess
        {
            private readonly WorldSdkRuntime _rt;

            public CircuitAccess(WorldSdkRuntime rt) => _rt = rt;

            public bool Available => _rt._circuit != null && _rt._sim != null;

            private WorldObject? Find(string idOrName) =>
                _rt._world.Find(idOrName) ?? _rt._world.Objects.FirstOrDefault(o => string.Equals(o.Name, idOrName, StringComparison.OrdinalIgnoreCase));

            public double Voltage(string objectIdOrName, string pin)
            {
                var c = _rt._circuit;
                var sim = _rt._sim;
                var o = Find(objectIdOrName);
                if (c == null || sim == null || o == null) return double.NaN;
                if (o.Kind == WorldObjectKind.Component)
                {
                    var r = c.ComponentRef(o.Id);
                    return r == null ? double.NaN : c.Compiled.PinVoltage(sim, r, pin);
                }

                int dot = pin.IndexOf('.');
                if (dot <= 0) return double.NaN;
                return c.Compiled.PinVoltage(sim, c.FullRefDes(o.Id, pin.Substring(0, dot)), pin.Substring(dot + 1));
            }

            public double Signal(string key)
            {
                var sim = _rt._sim;
                return sim != null && sim.Signals.TryGetValue(key, out double v) ? v : double.NaN;
            }

            public IReadOnlyList<AnalogDevice> Devices(string objectIdOrName, string? innerRefDes = null)
            {
                var c = _rt._circuit;
                var o = Find(objectIdOrName);
                if (c == null || o == null) return Array.Empty<AnalogDevice>();
                if (o.Kind == WorldObjectKind.Component && string.IsNullOrEmpty(innerRefDes))
                {
                    var r = c.ComponentRef(o.Id);
                    return r == null ? Array.Empty<AnalogDevice>() : c.Compiled.DevicesOf(r);
                }

                return string.IsNullOrEmpty(innerRefDes) ? Array.Empty<AnalogDevice>() : c.DevicesOf(o.Id, innerRefDes!);
            }
        }

        private sealed class Context : IBehaviorContext
        {
            private readonly SimBody? _self;
            private readonly string _label;
            private readonly Runner _runner;
            private int _logs;

            public Context(WorldSdkRuntime rt, Runner runner, SimBody? self, IReadOnlyDictionary<string, string> parameters, string label)
            {
                Runtime = rt;
                _runner = runner;
                _self = self;
                _label = label;
                Parameters = parameters;
                Random = SdkRandom.Derive(rt._s.Seed, runner.Id + "|" + label);
            }

            public WorldSdkRuntime Runtime { get; }
            public double Time => Runtime.Time;
            public double Dt => Runtime._dt;
            public long StepIndex => Runtime.StepIndex;
            public ISimBody? Self => _self;
            public IReadOnlyList<ISimBody> Bodies => Runtime._bodies;
            public ISimBody? Body(string idOrName) => Runtime.FindBody(idOrName);
            public IReadOnlyList<ISimJoint> Joints => Runtime._joints;

            public ISimJoint? Joint(string id) => Runtime._joints.FirstOrDefault(j => string.Equals(j.Id, id, StringComparison.OrdinalIgnoreCase));

            public IAirState Air => Runtime._air;
            public Vec3 Gravity => Runtime.Scene.Gravity;
            public ICircuitAccess Circuit => Runtime._circuitAccess;
            public IReadOnlyDictionary<string, string> Parameters { get; }

            public double Param(string name, double fallback) =>
                Parameters.TryGetValue(name, out var t) && BehaviorRegistry.TryParse(t, typeof(double), out var v) && v is double d ? d : fallback;

            public SdkRandom Random { get; }

            public void Publish(string name, double value)
            {
                Runtime.Publish(name, value);
                if (_self != null && name.IndexOf('.') < 0) Runtime.Publish(_self.Name + "." + name, value);
            }

            public double Read(string name) => Runtime.Read(name);

            public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out RayHit hit, ISimBody? ignore = null)
            {
                var ign = (ignore as SimBody)?.Raw;
                bool ok = Runtime.Scene.Raycast(origin, direction, maxDistance, out var h, b => b != ign);
                hit = new RayHit { Distance = h.Distance, Point = h.Point, Normal = h.Normal, Body = Runtime.Wrap(h.Body) };
                return ok;
            }

            public void Log(string message)
            {
                if (_logs++ < 200) Runtime.Report(_label, message, SdkEventLevel.Info);
                else if (_logs == 201) Runtime.Report(_label, "Çok fazla günlük satırı; bu çalıştırmada başka mesaj gösterilmeyecek.", SdkEventLevel.Warning);
            }

            public void Warn(string message)
            {
                if (_logs++ < 200) Runtime.Report(_label, message, SdkEventLevel.Warning);
            }
        }
    }
}
