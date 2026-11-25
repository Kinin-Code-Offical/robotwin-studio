using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// Samples the world for every sensor while a world simulates: line sensors look at the
    /// floor optics under their spots and drive their phototransistors, LiDARs cast rays into
    /// the physics scene, IMUs (MPU-6050) get the body's real motion, GPS receivers and
    /// compasses get position and field from the environment, encoder motors are reported.
    /// Cameras are rendered by the host (it owns the 3D scene) and processed through
    /// <see cref="CameraUnit.Submit"/>. All outputs go to <see cref="Sensors"/>.
    /// </summary>
    public sealed class WorldSensing
    {
        private readonly WorldDocument _world;
        private readonly WorldCircuit _circuit;
        private readonly WorldPhysics? _physics;
        private readonly Dictionary<string, int> _assembly = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SceneBody?> _bodies = new Dictionary<string, SceneBody?>(StringComparer.OrdinalIgnoreCase);
        private readonly List<LineUnit> _lines = new List<LineUnit>();
        private readonly List<LidarUnit> _lidars = new List<LidarUnit>();
        private readonly List<GpsUnit> _gps = new List<GpsUnit>();
        private readonly List<EncoderUnit> _encoders = new List<EncoderUnit>();
        private readonly List<ImuUnit> _imus = new List<ImuUnit>();
        private readonly List<CompassUnit> _compasses = new List<CompassUnit>();
        private readonly List<CameraUnit> _cameras = new List<CameraUnit>();
        private AnalogSimulator? _sim;
        private double _lineClock, _encClock;

        /// <summary>Line sensors are sampled at this rate (Hz): faster than any firmware loop reads them.</summary>
        public const double LineRate = 2000;

        private WorldSensing(WorldDocument world, WorldCircuit circuit, WorldPhysics? physics)
        {
            _world = world;
            _circuit = circuit;
            _physics = physics;
            Floor = new FloorOptics(world.Environment);
        }

        public WorldSensors Sensors { get; } = new WorldSensors();
        public FloorOptics Floor { get; }
        public SensingEnvironment Environment => Floor.Environment;
        public IReadOnlyList<CameraUnit> Cameras => _cameras;

        /// <summary>NMEA text a GPS module sends out of its TX pin (object id, text).</summary>
        public event Action<string, string>? GpsSerial;

        /// <summary>Encodes processed BGRA pixels to JPEG (set by the host; null = size estimate only).</summary>
        public static Func<byte[], int, int, int, byte[]>? JpegEncoder { get; set; }

        public static WorldSensing Build(WorldDocument world, WorldCircuit circuit, WorldPhysics? physics)
        {
            var s = new WorldSensing(world, circuit, physics);
            s.IndexAssemblies();
            int seed = 1;
            foreach (var o in world.Objects.Where(o => o.Kind == WorldObjectKind.Component && o.PartId != null))
            {
                var r = circuit.ComponentRef(o.Id);
                if (r == null) continue;
                var p = Parameters(o);
                var devices = circuit.Compiled.DevicesOf(r);
                switch (o.PartId)
                {
                    case SensingParts.Tcrt5000Id:
                    case SensingParts.Line5Id:
                    case SensingParts.Qtr8aId:
                    case SensingParts.Qtr8rcId:
                        s._lines.Add(new LineUnit(o, r, p, devices, seed++));
                        break;
                    case SensingParts.LidarId:
                        s._lidars.Add(new LidarUnit(o, r, p, seed++));
                        break;
                    case SensingParts.GpsId:
                        s._gps.Add(new GpsUnit(o, r, p, s.Environment, seed++));
                        break;
                    case SensingParts.CameraId:
                        s._cameras.Add(new CameraUnit(o, r, p));
                        break;
                    case SensingParts.EncoderMotorId:
                        if (devices.OfType<QuadratureEncoder>().FirstOrDefault() is { } enc && devices.OfType<DcMotor>().FirstOrDefault() is { } m)
                        {
                            s._encoders.Add(new EncoderUnit(o, enc, m));
                        }

                        break;
                }
            }

            return s;
        }

        private static Dictionary<string, string> Parameters(WorldObject o)
        {
            var lib = PartLibrary.Default;
            var def = o.PartId != null ? lib.Find(o.PartId) : null;
            if (def == null) return new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase);
            var preset = o.PresetId != null ? lib.FindPreset(o.PresetId) : null;
            return new Dictionary<string, string>(lib.ResolveParameters(def, preset, o.Parameters), StringComparer.OrdinalIgnoreCase);
        }

        // ================================================================ binding of I²C devices created by the host

        /// <summary>An MPU-6050 on an object (free-standing module or on a board) gets that object's motion.</summary>
        public void BindMpu6050(string objectId, Mpu6050 device)
        {
            var o = _world.Find(objectId);
            if (o == null) return;
            var model = new ImuModel(ImuSpec.Mpu6050, 6050 + _imus.Count);
            device.AccelNoise = model.AccelSigma;
            device.GyroNoise = model.GyroSigma;
            _imus.Add(new ImuUnit { Object = o, Device = device, Model = model });
        }

        public void BindCompass(string objectId, Hmc5883l device, string? hardIron)
        {
            var o = _world.Find(objectId);
            if (o == null) return;
            var hi = Vec3.Zero;
            var parts = (hardIron ?? string.Empty).Split(',');
            if (parts.Length == 3 && parts.All(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out _)))
            {
                hi = new Vec3(double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture),
                    double.Parse(parts[2], CultureInfo.InvariantCulture)) * 1e-3;
            }

            _compasses.Add(new CompassUnit { Object = o, Device = device, HardIron = hi });
        }

        // ================================================================ stepping

        /// <summary>Samples what is due at simulated <paramref name="time"/> after a step of <paramref name="dt"/>.</summary>
        public void Advance(double time, double dt, AnalogSimulator? sim)
        {
            _sim = sim;
            Sensors.Time = time;
            if (_lines.Count > 0)
            {
                _lineClock += dt;
                if (_lineClock >= 1.0 / LineRate)
                {
                    double step = _lineClock;
                    _lineClock = 0;
                    foreach (var u in _lines) SampleLine(u, time, step);
                }
            }

            foreach (var l in _lidars) SampleLidar(l, time, dt);
            foreach (var g in _gps) SampleGps(g, time);
            foreach (var i in _imus) SampleImu(i, time, dt);
            foreach (var c in _compasses) SampleCompass(c, time);
            foreach (var cam in _cameras) cam.UpdatePower(Supply(cam.RefDes, "VCC", "GND"));
            _encClock += dt;
            if (_encClock >= 0.02)
            {
                double step = _encClock;
                _encClock = 0;
                foreach (var e in _encoders) PublishEncoder(e, time);
            }
        }

        private double Supply(string refDes, string plus, string minus)
        {
            if (_sim == null) return 5.0;
            double v = _circuit.Compiled.PinVoltage(_sim, refDes, plus) - _circuit.Compiled.PinVoltage(_sim, refDes, minus);
            return double.IsNaN(v) ? 0 : v;
        }

        private double Pin(string refDes, string pin) => _sim == null ? double.NaN : _circuit.Compiled.PinVoltage(_sim, refDes, pin);

        // ================================================================ poses

        /// <summary>Current pose of an object (physics when it runs, otherwise as placed).</summary>
        public (Vec3 Position, Quat Orientation) PoseOf(WorldObject o)
        {
            if (_physics?.PoseOf(o.Id) is { } p) return p;
            return (o.Pose.Position, o.Pose.Orientation);
        }

        private SceneBody? BodyOf(WorldObject o)
        {
            if (_physics == null) return null;
            if (_bodies.TryGetValue(o.Id, out var b)) return b;
            b = _physics.Scene.Bodies.FirstOrDefault(x => ReferenceEquals(x.Tag, o));
            _bodies[o.Id] = b;
            return b;
        }

        private (Vec3 Velocity, Vec3 AngularVelocity) MotionOf(WorldObject o)
        {
            var b = BodyOf(o);
            return b == null || b.IsStatic ? (Vec3.Zero, Vec3.Zero) : (b.Velocity, b.AngularVelocity);
        }

        /// <summary>World velocity and angular velocity of an object (zero when static or without physics).</summary>
        public (Vec3 Velocity, Vec3 AngularVelocity) Motion(WorldObject o) => MotionOf(o);

        private void IndexAssemblies()
        {
            var ids = _world.Objects.Select(o => o.Id).ToList();
            var parent = ids.ToDictionary(i => i, i => i, StringComparer.OrdinalIgnoreCase);
            string Find(string x)
            {
                while (!string.Equals(parent[x], x, StringComparison.OrdinalIgnoreCase)) x = parent[x] = parent[parent[x]];
                return x;
            }

            foreach (var j in _world.Joints)
            {
                if (j.ObjectB == null || !parent.ContainsKey(j.ObjectA) || !parent.ContainsKey(j.ObjectB)) continue;
                parent[Find(j.ObjectA)] = Find(j.ObjectB);
            }

            var groups = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids)
            {
                string root = Find(id);
                if (!groups.TryGetValue(root, out int g)) groups[root] = g = groups.Count;
                _assembly[id] = g;
            }
        }

        private static string ObjectIdOf(SceneBody b)
        {
            if (b.Tag is WorldObject wo) return wo.Id;
            int c = b.Id.IndexOf(':');
            return c > 0 ? b.Id.Substring(0, c) : b.Id;
        }

        private bool SameAssembly(string a, SceneBody b) =>
            _assembly.TryGetValue(a, out int ga) && _assembly.TryGetValue(ObjectIdOf(b), out int gb) && ga == gb;

        /// <summary>
        /// Ray into the world for an object's own sensor: physics bodies (not its own assembly), the
        /// perimeter walls and the floor. Returns the distance and the target's IR reflectance.
        /// </summary>
        public (double Distance, double Reflectance) Cast(string ownerId, Vec3 origin, Vec3 dir, double max)
        {
            double best = double.PositiveInfinity, refl = 0;
            if (_physics != null && _physics.Scene.Raycast(origin, dir, max, out var hit, b => !SameAssembly(ownerId, b), false))
            {
                best = hit.Distance;
                refl = MechatronicSystem.Reflectance(hit.Material ?? string.Empty);
            }

            var env = _world.Environment;
            if (env.Walls)
            {
                foreach (var (min, mx) in RoomWalls(env))
                {
                    double t = RayBox(origin, dir, min, mx);
                    if (t >= 0 && t < best && t <= max)
                    {
                        best = t;
                        refl = 0.35;
                    }
                }
            }

            if (dir.Z < -1e-6)
            {
                double t = -origin.Z / dir.Z;
                if (t >= 0 && t < best && t <= max)
                {
                    var p = origin + dir * t;
                    best = t;
                    refl = Floor.At(p.X, p.Y).Diffuse;
                }
            }

            return (best, refl);
        }

        /// <summary>The room's walls (when it has them): four boxes 2 cm thick and 10 cm high round its floor.</summary>
        internal static (Vec3 Min, Vec3 Max)[] RoomWalls(WorldSpec env)
        {
            double w = env.Width / 2, d = env.Depth / 2;
            return new[]
            {
                (new Vec3(-w - 0.02, -d - 0.02, 0), new Vec3(w + 0.02, -d, 0.1)), (new Vec3(-w - 0.02, d, 0), new Vec3(w + 0.02, d + 0.02, 0.1)),
                (new Vec3(-w - 0.02, -d, 0), new Vec3(-w, d, 0.1)), (new Vec3(w, -d, 0), new Vec3(w + 0.02, d, 0.1))
            };
        }

        /// <summary>Where a ray enters a box (its distance), or −1 when it misses.</summary>
        internal static double RayBox(Vec3 o, Vec3 d, Vec3 min, Vec3 max)
        {
            double t0 = 0, t1 = double.PositiveInfinity;
            for (int a = 0; a < 3; a++)
            {
                double oa = a == 0 ? o.X : a == 1 ? o.Y : o.Z, da = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
                double lo = a == 0 ? min.X : a == 1 ? min.Y : min.Z, hi = a == 0 ? max.X : a == 1 ? max.Y : max.Z;
                if (Math.Abs(da) < 1e-12)
                {
                    if (oa < lo || oa > hi) return -1;
                    continue;
                }

                double ta = (lo - oa) / da, tb = (hi - oa) / da;
                if (ta > tb) (ta, tb) = (tb, ta);
                t0 = Math.Max(t0, ta);
                t1 = Math.Min(t1, tb);
                if (t0 > t1) return -1;
            }

            return t0;
        }

        // ================================================================ line sensors

        private sealed class LineUnit
        {
            public LineUnit(WorldObject o, string refDes, Dictionary<string, string> p, IReadOnlyList<AnalogDevice> devices, int seed)
            {
                Object = o;
                RefDes = refDes;
                Spec = SensingParts.LineSpec(o.PartId);
                Channels = SensingParts.LineChannels(o.PartId);
                Photo = devices.OfType<Phototransistor>().ToList();
                Comparators = devices.OfType<OpenCollectorComparator>().ToList();
                Rled = SiValue.TryParse(p.TryGetValue("rled", out var rl) ? rl : "150", out double r) ? r : 150;
                IsQtr = o.PartId == SensingParts.Qtr8aId || o.PartId == SensingParts.Qtr8rcId;
                Outputs = Enumerable.Range(1, Channels.Count).Select(i => o.PartId == SensingParts.Tcrt5000Id ? "AO" : "OUT" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
                Rng = new Random(seed * 7919);
                Drift = new double[Channels.Count];
                Gain = Enumerable.Range(0, Channels.Count).Select(_ => 1 + (Rng.NextDouble() - 0.5) * 0.1).ToArray();   // ±5 % part spread
            }

            public WorldObject Object;
            public string RefDes;
            public ReflectiveSensorSpec Spec;
            public IReadOnlyList<SensingParts.LineChannel> Channels;
            public List<Phototransistor> Photo;
            public List<OpenCollectorComparator> Comparators;
            public double Rled;
            public bool IsQtr;
            public string[] Outputs;
            public Random Rng;
            public double[] Drift;
            public double[] Gain;
        }

        private void SampleLine(LineUnit u, double time, double dt)
        {
            var (pos, q) = PoseOf(u.Object);
            var axis = q.Rotate(-Vec3.UnitZ);
            double vcc = Supply(u.RefDes, "VCC", "GND");
            double drive;
            if (u.IsQtr)
            {
                double ledon = Pin(u.RefDes, "LEDON") - Pin(u.RefDes, "GND");
                drive = double.IsNaN(ledon) || ledon > 1.5 ? Math.Max(0, Math.Min(1.2, (vcc - 1.4) / 3.6)) : 0;
            }
            else
            {
                drive = Math.Max(0, vcc - 1.2) / Math.Max(10, u.Rled) / u.Spec.NominalIf;
            }

            // Something other than the floor right under the module (a sheet, a box)?
            (double, double)? obstacle = null;
            if (_physics != null)
            {
                var c0 = pos + q.Rotate(u.Channels.Count > 0 ? u.Channels[u.Channels.Count / 2].Centre : Vec3.Zero);
                var (d, refl) = CastObjects(u.Object.Id, c0, axis, u.Spec.MaxRange * 2);
                if (!double.IsInfinity(d)) obstacle = (d, refl);
            }

            double temp = _world.Environment.AmbientC;
            var readings = new LineChannelReading[u.Channels.Count];
            double sq = Math.Sqrt(dt);
            for (int i = 0; i < u.Channels.Count; i++)
            {
                var ch = u.Channels[i];
                var centre = pos + q.Rotate(ch.Centre);
                var baseline = q.Rotate(ch.Baseline);
                var r = ReflectiveOptics.Evaluate(u.Spec, Floor, centre, axis, baseline, obstacle);

                // Slow drift (mean-reverting random walk, ≈1 % over tens of seconds) and white noise.
                u.Drift[i] += -u.Drift[i] * dt / 30.0 + 0.002 * sq * Gauss(u.Rng);
                double noise = u.Spec.Noise * Gauss(u.Rng);
                double tempFactor = 1 + u.Spec.TempCoefficient * (temp - 25);
                double iph = vcc < 2.0 ? 0 : u.Spec.PeakCurrent * drive * r.Signal * u.Gain[i] * tempFactor * (1 + u.Drift[i] + noise) + u.Spec.DarkCurrent;
                if (i < u.Photo.Count) u.Photo[i].Photocurrent = Math.Max(0, iph);

                readings[i] = new LineChannelReading
                {
                    Reflectance = r.Reflectance, Signal = r.Signal, Diffuse = r.Diffuse, Specular = r.Specular, Ambient = r.Ambient,
                    Photocurrent = Math.Max(0, iph), Voltage = Pin(u.RefDes, u.Outputs[i]) - Pin(u.RefDes, "GND"),
                    DigitalLow = i < u.Comparators.Count ? u.Comparators[i].OutputLow : null,
                    Distance = r.Distance, Spot = r.Spot, SpotRadius = r.SpotRadius, Tape = r.Tape
                };
            }

            Sensors.Publish(new LineSensorReading
            {
                ObjectId = u.Object.Id, Name = u.Object.Name, Time = time, Powered = vcc >= 2.0, Model = u.Spec.Name, Channels = readings
            });
        }

        private (double Distance, double Reflectance) CastObjects(string ownerId, Vec3 origin, Vec3 dir, double max)
        {
            if (_physics != null && _physics.Scene.Raycast(origin, dir, max, out var hit, b => !SameAssembly(ownerId, b), false))
            {
                return (hit.Distance, MechatronicSystem.Reflectance(hit.Material ?? string.Empty));
            }

            return (double.PositiveInfinity, 0);
        }

        // ================================================================ LiDAR

        private sealed class LidarUnit
        {
            public LidarUnit(WorldObject o, string refDes, Dictionary<string, string> p, int seed)
            {
                Object = o;
                RefDes = refDes;
                Model = p.TryGetValue("model", out var m) ? m : "A1";
                var spec = LidarSpec.For(Model);
                double rate = SiValue.TryParse(p.TryGetValue("scanrate", out var sr) ? sr : "0", out double r) ? r : 0;
                double az = SiValue.TryParse(p.TryGetValue("azimuth", out var a) ? a : "0", out double av) ? av : 0;
                Scanner = new LidarScanner(spec, rate, spec.ChannelElevationsDeg.Length > 1 ? Math.Max(0.1, az) : 0, seed * 104729);
                Centre = SensingParts.LidarCentre(Model);
            }

            public WorldObject Object;
            public string RefDes;
            public string Model;
            public LidarScanner Scanner;
            public Vec3 Centre;
        }

        private void SampleLidar(LidarUnit u, double time, double dt)
        {
            double vcc = Supply(u.RefDes, "VCC", "GND");
            double motor = Pin(u.RefDes, "MOTOCTL") - Pin(u.RefDes, "GND");
            bool on = vcc >= u.Scanner.Spec.SupplyVoltage * 0.85 && (double.IsNaN(motor) || motor > 1.5 || _sim == null);
            if (u.Scanner.Running != on)
            {
                u.Scanner.Running = on;
                if (!on) Sensors.Publish(new LidarReading { ObjectId = u.Object.Id, Name = u.Object.Name, Time = time, Powered = false, Model = u.Scanner.Spec.Name });
            }

            if (!on) return;
            var (pos, q) = PoseOf(u.Object);
            var origin = pos + q.Rotate(u.Centre);
            var before = u.Scanner.Latest;
            string id = u.Object.Id;
            u.Scanner.Advance(time, dt, origin, q, (o, d, max) => Cast(id, o, d, max));
            if (u.Scanner.Latest != null && !ReferenceEquals(before, u.Scanner.Latest))
            {
                Sensors.Publish(new LidarReading
                {
                    ObjectId = u.Object.Id, Name = u.Object.Name, Time = time, Model = u.Scanner.Spec.Name, Scan = u.Scanner.Latest,
                    Channels = u.Scanner.Spec.ChannelElevationsDeg.Length
                });
            }
        }

        // ================================================================ GPS

        private sealed class GpsUnit
        {
            public GpsUnit(WorldObject o, string refDes, Dictionary<string, string> p, SensingEnvironment env, int seed)
            {
                Object = o;
                RefDes = refDes;
                Receiver = new GpsReceiver(GpsSpec.For(p.TryGetValue("model", out var m) ? m : null), env, seed * 31337,
                    p.TryGetValue("start", out var st) ? st : "sıcak")
                {
                    RateHz = SiValue.TryParse(p.TryGetValue("rate", out var rt) ? rt : "1", out double r) ? r : 1
                };
            }

            public WorldObject Object;
            public string RefDes;
            public GpsReceiver Receiver;
        }

        private void SampleGps(GpsUnit g, double time)
        {
            bool powered = Supply(g.RefDes, "VCC", "GND") >= 2.7;
            var (pos, _) = PoseOf(g.Object);
            var (vel, _) = MotionOf(g.Object);
            var fix = g.Receiver.Update(time, pos, vel, powered);
            if (fix == null) return;
            Sensors.Publish(new GpsReading { ObjectId = g.Object.Id, Name = g.Object.Name, Time = time, Fix = fix });
            GpsSerial?.Invoke(g.Object.Id, fix.Nmea);
        }

        // ================================================================ IMU

        private sealed class ImuUnit
        {
            public WorldObject Object = null!;
            public Mpu6050 Device = null!;
            public ImuModel Model = null!;
            public double Clock, LastPublish = -1;
        }

        private void SampleImu(ImuUnit u, double time, double dt)
        {
            u.Clock += dt;
            if (u.Clock < 1e-3) return;
            double step = u.Clock;
            u.Clock = 0;
            var (_, q) = PoseOf(u.Object);
            var (v, w) = MotionOf(u.Object);
            u.Model.Update(step, q, v, w, _world.Environment.Gravity, addNoise: false);
            var a = u.Model.Accel;
            var g = u.Model.Gyro;
            u.Device.AccelX = a.X;
            u.Device.AccelY = a.Y;
            u.Device.AccelZ = a.Z;
            u.Device.GyroX = g.X;
            u.Device.GyroY = g.Y;
            u.Device.GyroZ = g.Z;
            u.Device.TemperatureC = _world.Environment.AmbientC + 5;
            if (time - u.LastPublish >= 0.01)
            {
                u.LastPublish = time;
                Sensors.Publish(new ImuReading
                {
                    ObjectId = u.Object.Id, Name = u.Object.Name, Time = time, Accel = a, Gyro = g, TrueAccel = u.Model.TrueAccel, TrueGyro = u.Model.TrueGyro, Orientation = q
                });
            }
        }

        // ================================================================ compass

        private sealed class CompassUnit
        {
            public WorldObject Object = null!;
            public Hmc5883l Device = null!;
            public Vec3 HardIron;
            public double Next;
        }

        private static readonly double[] HmcRates = { 0.75, 1.5, 3, 7.5, 15, 30, 75, 75 };

        private void SampleCompass(CompassUnit c, double time)
        {
            if (time < c.Next) return;
            double rate = HmcRates[(c.Device.ConfigA >> 2) & 7];
            c.Next = time + 1.0 / rate;
            var (_, q) = PoseOf(c.Object);
            var (fx, fy, fz) = _world.Environment.Sensing?.MagneticField() ?? new SensingEnvironment().MagneticField();
            var body = q.InverseRotate(new Vec3(fx, fy, fz) * 0.01) + c.HardIron;   // µT → Gauss
            c.Device.FieldX = body.X;
            c.Device.FieldY = body.Y;
            c.Device.FieldZ = body.Z;
            c.Device.Measure();
            double heading = (Math.Atan2(body.Y, body.X) * -180 / Math.PI + 360) % 360;
            Sensors.Publish(new CompassReading { ObjectId = c.Object.Id, Name = c.Object.Name, Time = time, Field = body, HeadingDeg = heading });
        }

        // ================================================================ encoders

        private sealed class EncoderUnit
        {
            public EncoderUnit(WorldObject o, QuadratureEncoder enc, DcMotor motor)
            {
                Object = o;
                Encoder = enc;
                Motor = motor;
            }

            public WorldObject Object;
            public QuadratureEncoder Encoder;
            public DcMotor Motor;
        }

        private void PublishEncoder(EncoderUnit e, double time)
        {
            Sensors.Publish(new EncoderReading
            {
                ObjectId = e.Object.Id, Name = e.Object.Name, Time = time, Count = e.Encoder.Count,
                CountsPerRevolution = e.Encoder.PulsesPerRevolution * 4 * e.Motor.GearRatio, OutputAngleRad = e.Motor.OutputAngle,
                OutputRpm = e.Motor.OutputRpm, A = e.Encoder.LevelA, B = e.Encoder.LevelB
            });
        }

        private static double Gauss(Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }

        /// <summary>World frame of a camera's lens: position, forward (+X of the lens), up.</summary>
        public (Vec3 Position, Vec3 Forward, Vec3 Up, Quat Orientation) CameraFrame(CameraUnit cam)
        {
            var (pos, q) = PoseOf(cam.Object);
            return (pos + q.Rotate(cam.Lens), q.Rotate(Vec3.UnitX), q.Rotate(Vec3.UnitZ), q);
        }
    }
}
