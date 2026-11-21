using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>
    /// A sensor the API can read by name. Built-in sensors are discovered from the loaded world
    /// (ultrasonic parts, motor encoders, vehicle line/obstacle/IMU mounts); virtual sensors
    /// (IMU, GPS, odometry, LiDAR, camera …) are attached to an object with sensor.add; other
    /// modules plug their own in with <see cref="SensorRegistry.RegisterProvider"/> /
    /// <see cref="SensorRegistry.RegisterType"/>.
    /// </summary>
    public interface ISimSensor
    {
        string Name { get; }

        /// <summary>"ultrasonic", "imu", "gps", "odometry", "lidar", "camera", "depth", "range", "line", "encoder" …</summary>
        string Type { get; }

        /// <summary>Object the sensor is mounted on (null = none).</summary>
        string? Object { get; }

        /// <summary>Static description (rates, resolution, limits) for clients.</summary>
        JsonObject Describe();

        /// <summary>Current reading.</summary>
        JsonObject Read();
    }

    /// <summary>A sensor that integrates over time (called after every lockstep slice).</summary>
    public interface ITickingSensor
    {
        void Tick(double dt);
    }

    /// <summary>What sensor.add asked for: type, mount and parameters (kept across resets).</summary>
    public sealed class SensorSpec
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Object { get; set; }
        public Vec3 Offset { get; set; }
        public Quat Rotation { get; set; } = Quat.Identity;
        public JsonObject Params { get; set; } = new JsonObject();

        public double Num(string name, double fallback) => J.Num(Params, name, fallback);
    }

    public sealed class SensorRegistry
    {
        private static readonly List<Func<SimRunner, IEnumerable<ISimSensor>>> ProvidersList = new List<Func<SimRunner, IEnumerable<ISimSensor>>>();
        private static readonly Dictionary<string, Func<SimRunner, SensorSpec, ISimSensor>> TypesMap =
            new Dictionary<string, Func<SimRunner, SensorSpec, ISimSensor>>(StringComparer.OrdinalIgnoreCase)
            {
                ["imu"] = (r, s) => new ImuSensor(r, s),
                ["gps"] = (r, s) => new GpsSensor(r, s),
                ["odometry"] = (r, s) => new OdometrySensor(r, s),
                ["pose"] = (r, s) => new OdometrySensor(r, s),
                ["lidar"] = (r, s) => new LidarSensor(r, s),
                ["range"] = (r, s) => new RangeSensor(r, s),
                ["camera"] = (r, s) => new RayCamera(r, s, false),
                ["depth"] = (r, s) => new RayCamera(r, s, true),
                ["line"] = (r, s) => new LineSensor(r, s),
            };

        private static readonly object Gate = new object();

        /// <summary>Adds sensors of parts a module simulates (e.g. a LiDAR or camera part) to every run.</summary>
        public static void RegisterProvider(Func<SimRunner, IEnumerable<ISimSensor>> provider)
        {
            lock (Gate) ProvidersList.Add(provider);
        }

        /// <summary>Makes a new virtual sensor type available to sensor.add.</summary>
        public static void RegisterType(string type, Func<SimRunner, SensorSpec, ISimSensor> factory)
        {
            lock (Gate) TypesMap[type] = factory;
        }

        public static IReadOnlyCollection<string> Types
        {
            get
            {
                lock (Gate) return TypesMap.Keys.ToList();
            }
        }

        private readonly SimRunner _runner;
        private readonly List<SensorSpec> _specs = new List<SensorSpec>();
        private readonly Dictionary<string, ISimSensor> _sensors = new Dictionary<string, ISimSensor>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ITickingSensor> _ticking = new List<ITickingSensor>();

        public SensorRegistry(SimRunner runner)
        {
            _runner = runner;
            runner.Sliced += dt =>
            {
                for (int i = 0; i < _ticking.Count; i++) _ticking[i].Tick(dt);
            };
        }

        public IEnumerable<ISimSensor> All => _sensors.Values;

        /// <summary>Re-creates every sensor for the freshly built run.</summary>
        public void Rebuild()
        {
            _sensors.Clear();
            _ticking.Clear();
            foreach (var s in BuiltIn()) Put(s);
            List<Func<SimRunner, IEnumerable<ISimSensor>>> providers;
            lock (Gate) providers = ProvidersList.ToList();
            foreach (var p in providers)
            {
                try
                {
                    foreach (var s in p(_runner)) Put(s);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException)
                {
                    _runner.AddEvent(new RunnerEvent(0, "sensör", RunnerEventKind.Warning, "Sensör sağlayıcısı hata verdi: " + ex.Message));
                }
            }

            foreach (var spec in _specs) Put(Create(spec));
        }

        private void Put(ISimSensor s)
        {
            _sensors[s.Name] = s;
            if (s is ITickingSensor t) _ticking.Add(t);
        }

        private ISimSensor Create(SensorSpec spec)
        {
            Func<SimRunner, SensorSpec, ISimSensor>? factory;
            lock (Gate) TypesMap.TryGetValue(spec.Type, out factory);
            if (factory == null) throw ApiException.BadParam("type", "bilinmeyen sensör türü '" + spec.Type + "' (" + string.Join(", ", Types) + ")");
            return factory(_runner, spec);
        }

        public ISimSensor Add(SensorSpec spec)
        {
            if (string.IsNullOrWhiteSpace(spec.Name)) spec.Name = spec.Type + (_specs.Count(s => s.Type == spec.Type) + 1);
            if (spec.Object != null && !_runner.TryGetBody(spec.Object, out _)) throw ApiException.Missing("Nesne '" + spec.Object + "'");
            var sensor = Create(spec);
            _specs.RemoveAll(s => string.Equals(s.Name, spec.Name, StringComparison.OrdinalIgnoreCase));
            _specs.Add(spec);
            if (_sensors.TryGetValue(spec.Name, out var old) && old is ITickingSensor ot) _ticking.Remove(ot);
            Put(sensor);
            return sensor;
        }

        public bool Remove(string name)
        {
            bool had = _specs.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
            if (_sensors.TryGetValue(name, out var s))
            {
                if (s is ITickingSensor t) _ticking.Remove(t);
                _sensors.Remove(name);
                had = true;
            }

            return had;
        }

        public ISimSensor Get(string name) => _sensors.TryGetValue(name, out var s) ? s : throw ApiException.Missing("Sensör '" + name + "'");

        public bool TryGet(string name, out ISimSensor sensor) => _sensors.TryGetValue(name, out sensor!);

        public JsonArray List()
        {
            var a = new JsonArray();
            foreach (var s in _sensors.Values.OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                var d = s.Describe();
                d["name"] = s.Name;
                d["type"] = s.Type;
                d["object"] = s.Object;
                a.Add(d);
            }

            return a;
        }

        public JsonObject Read(string name)
        {
            var s = Get(name);
            var r = s.Read();
            r["time"] = J.N(_runner.Time);
            return r;
        }

        // ================================================================ built-in sensors

        private IEnumerable<ISimSensor> BuiltIn()
        {
            if (_runner is WorldRunner w)
            {
                foreach (var o in w.World.Objects)
                {
                    if (o.Kind != WorldObjectKind.Component || w.Circuit?.ComponentRef(o.Id) is not { } r) continue;
                    foreach (var d in w.Compiled!.DevicesOf(r))
                    {
                        if (d is UltrasonicSensor us) yield return new UltrasonicPartSensor(w, o, us);
                        if (d is DcMotor || d is Servo) yield return new EncoderSensor(w, o);
                    }
                }
            }
            else if (_runner is VehicleRunner v && v.Mechanics != null)
            {
                foreach (var link in v.Mechanics.Sensors) yield return new VehicleMountSensor(v, link);
                yield return new ImuSensor(v, new SensorSpec { Name = "imu", Type = "imu", Object = VehicleRunner.VehicleId });
                yield return new GpsSensor(v, new SensorSpec { Name = "gps", Type = "gps", Object = VehicleRunner.VehicleId });
                yield return new OdometrySensor(v, new SensorSpec { Name = "odom", Type = "odometry", Object = VehicleRunner.VehicleId });
            }
        }
    }

    // ==================================================================== base for mounted virtual sensors

    public abstract class MountedSensor : ISimSensor
    {
        protected MountedSensor(SimRunner runner, SensorSpec spec)
        {
            Runner = runner;
            Spec = spec;
            Noise = new NoiseSource(J.StableHash(spec.Name) ^ (runner.Seed * 1000003));
        }

        protected SimRunner Runner { get; }
        protected SensorSpec Spec { get; }
        protected NoiseSource Noise { get; }
        public string Name => Spec.Name;
        public string Type => Spec.Type;
        public string? Object => Spec.Object;

        /// <summary>World pose of the sensor (the mount object's frame + offset), or the offset itself without an object.</summary>
        protected bool TryPose(out Vec3 position, out Quat orientation, out BodyState body)
        {
            body = default;
            if (Spec.Object == null)
            {
                position = Spec.Offset;
                orientation = Spec.Rotation;
                return true;
            }

            if (!Runner.TryGetBody(Spec.Object, out body))
            {
                position = default;
                orientation = Quat.Identity;
                return false;
            }

            position = body.Position + body.Orientation.Rotate(Spec.Offset);
            orientation = (body.Orientation * Spec.Rotation).Normalized();
            return true;
        }

        public virtual JsonObject Describe() => new JsonObject
        {
            ["offset"] = J.V(Spec.Offset),
            ["rotation"] = J.Q(Spec.Rotation),
            ["params"] = Spec.Params.DeepClone()
        };

        public abstract JsonObject Read();
    }

    /// <summary>Accelerometer (specific force) and gyroscope in the sensor frame, with optional Gaussian noise and bias.</summary>
    public sealed class ImuSensor : MountedSensor, ITickingSensor
    {
        private Vec3 _lastVelocity, _accel;
        private double _sinceSample;
        private bool _primed;

        public ImuSensor(SimRunner runner, SensorSpec spec) : base(runner, spec)
        {
        }

        public void Tick(double dt)
        {
            _sinceSample += dt;
            if (_sinceSample < 1e-3 * 0.999) return;
            if (!TryPose(out var p, out _, out var b)) return;
            var v = b.Velocity + b.AngularVelocity.Cross(p - b.Position);
            if (_primed)
            {
                var a = (v - _lastVelocity) / _sinceSample;
                double tau = Spec.Num("filter_tau", 0.004);
                double k = tau <= 0 ? 1 : Math.Min(1, _sinceSample / tau);
                _accel = _accel + (a - _accel) * k;
            }

            _lastVelocity = v;
            _primed = true;
            _sinceSample = 0;
        }

        public override JsonObject Read()
        {
            if (!TryPose(out _, out var q, out var b)) return new JsonObject { ["valid"] = false };
            var specific = q.InverseRotate(_accel - Runner.Gravity);
            var gyro = q.InverseRotate(b.AngularVelocity);
            double na = Spec.Num("accel_noise", 0), ng = Spec.Num("gyro_noise", 0);
            specific = new Vec3(specific.X + Noise.Gaussian(na), specific.Y + Noise.Gaussian(na), specific.Z + Noise.Gaussian(na));
            gyro = new Vec3(gyro.X + Noise.Gaussian(ng), gyro.Y + Noise.Gaussian(ng), gyro.Z + Noise.Gaussian(ng));
            var (roll, pitch, yaw) = q.ToEuler();
            return new JsonObject
            {
                ["valid"] = true,
                ["frame"] = Name,
                ["orientation"] = J.Q(q),
                ["rpy"] = J.Arr(new[] { roll, pitch, yaw }),
                ["angular_velocity"] = J.V(gyro),
                ["linear_acceleration"] = J.V(specific)
            };
        }
    }

    /// <summary>GNSS fix from the local ENU position (x = east, y = north) around a configurable origin.</summary>
    public sealed class GpsSensor : MountedSensor
    {
        private const double EarthRadius = 6378137.0;

        public GpsSensor(SimRunner runner, SensorSpec spec) : base(runner, spec)
        {
        }

        public override JsonObject Read()
        {
            if (!TryPose(out var p, out _, out var b)) return new JsonObject { ["valid"] = false };
            double lat0 = Spec.Num("origin_lat", 41.0082), lon0 = Spec.Num("origin_lon", 28.9784), alt0 = Spec.Num("origin_alt", 40.0);
            double sigma = Spec.Num("noise", 0), vsigma = Spec.Num("noise_vertical", sigma * 1.5);
            double e = p.X + Noise.Gaussian(sigma), n = p.Y + Noise.Gaussian(sigma), u = p.Z + Noise.Gaussian(vsigma);
            double lat = lat0 + n / EarthRadius * 180 / Math.PI;
            double lon = lon0 + e / (EarthRadius * Math.Cos(lat0 * Math.PI / 180)) * 180 / Math.PI;
            return new JsonObject
            {
                ["valid"] = true,
                ["frame"] = Name,
                ["latitude"] = J.N(lat),
                ["longitude"] = J.N(lon),
                ["altitude"] = J.N(alt0 + u),
                ["position_enu"] = J.V(new Vec3(e, n, u)),
                ["velocity_enu"] = J.V(b.Velocity),
                ["covariance_diagonal"] = J.Arr(new[] { sigma * sigma, sigma * sigma, vsigma * vsigma })
            };
        }
    }

    /// <summary>Ground-truth pose and twist (ROS odometry convention: twist in the body frame).</summary>
    public sealed class OdometrySensor : MountedSensor
    {
        public OdometrySensor(SimRunner runner, SensorSpec spec) : base(runner, spec)
        {
        }

        public override JsonObject Read()
        {
            if (!TryPose(out var p, out var q, out var b)) return new JsonObject { ["valid"] = false };
            var v = b.Velocity + b.AngularVelocity.Cross(p - b.Position);
            double ns = Spec.Num("noise", 0);
            p = new Vec3(p.X + Noise.Gaussian(ns), p.Y + Noise.Gaussian(ns), p.Z + Noise.Gaussian(ns));
            var (roll, pitch, yaw) = q.ToEuler();
            return new JsonObject
            {
                ["valid"] = true,
                ["frame"] = Name,
                ["position"] = J.V(p),
                ["orientation"] = J.Q(q),
                ["rpy"] = J.Arr(new[] { roll, pitch, yaw }),
                ["linear_velocity"] = J.V(q.InverseRotate(v)),
                ["angular_velocity"] = J.V(q.InverseRotate(b.AngularVelocity)),
                ["linear_velocity_world"] = J.V(v)
            };
        }
    }

    /// <summary>
    /// Scanning range finder: <c>samples</c> beams from angle_min to angle_max in the sensor's x–y plane
    /// (and <c>layers</c> planes over ±vfov/2 for a 3D LiDAR). Ranges beyond range_max are null.
    /// </summary>
    public sealed class LidarSensor : MountedSensor
    {
        public LidarSensor(SimRunner runner, SensorSpec spec) : base(runner, spec)
        {
        }

        private int Samples => Math.Max(1, Math.Min(4096, (int)Spec.Num("samples", 360)));
        private int Layers => Math.Max(1, Math.Min(64, (int)Spec.Num("layers", 1)));
        private double AngleMin => Spec.Num("angle_min", -Math.PI);
        private double AngleMax => Spec.Num("angle_max", Math.PI);
        private double Increment => Samples > 1 ? (AngleMax - AngleMin) / (Math.Abs(AngleMax - AngleMin - 2 * Math.PI) < 1e-6 ? Samples : Samples - 1) : 0;

        public override JsonObject Describe()
        {
            var d = base.Describe();
            d["samples"] = Samples;
            d["layers"] = Layers;
            d["angle_min"] = J.N(AngleMin);
            d["angle_max"] = J.N(AngleMax);
            d["angle_increment"] = J.N(Increment);
            d["range_min"] = J.N(Spec.Num("range_min", 0.05));
            d["range_max"] = J.N(Spec.Num("range_max", 8.0));
            return d;
        }

        public override JsonObject Read()
        {
            if (!TryPose(out var p, out var q, out _)) return new JsonObject { ["valid"] = false };
            double rmin = Spec.Num("range_min", 0.05), rmax = Spec.Num("range_max", 8.0), sigma = Spec.Num("noise", 0);
            double vfov = Spec.Num("vfov_deg", Layers > 1 ? 30 : 0) * Math.PI / 180;
            bool ignoreOwn = J.Bool(Spec.Params, "ignore_own", true);
            var ranges = new JsonArray();
            int hits = 0;
            for (int layer = 0; layer < Layers; layer++)
            {
                double el = Layers > 1 ? -vfov / 2 + vfov * layer / (Layers - 1) : 0;
                for (int i = 0; i < Samples; i++)
                {
                    double a = AngleMin + i * Increment;
                    var local = new Vec3(Math.Cos(el) * Math.Cos(a), Math.Cos(el) * Math.Sin(a), Math.Sin(el));
                    double r = double.PositiveInfinity;
                    if (Runner.Raycast(p, q.Rotate(local), rmax, ignoreOwn ? Spec.Object : null, out var hit))
                    {
                        r = hit.Distance + Noise.Gaussian(sigma);
                        if (r < rmin) r = double.NegativeInfinity;
                        else hits++;
                    }

                    ranges.Add(J.N(r));
                }
            }

            return new JsonObject
            {
                ["valid"] = true,
                ["frame"] = Name,
                ["angle_min"] = J.N(AngleMin),
                ["angle_max"] = J.N(AngleMin + (Samples - 1) * Increment),
                ["angle_increment"] = J.N(Increment),
                ["range_min"] = J.N(rmin),
                ["range_max"] = J.N(rmax),
                ["layers"] = Layers,
                ["vfov"] = J.N(vfov),
                ["hits"] = hits,
                ["ranges"] = ranges
            };
        }
    }

    /// <summary>Single-beam range finder along the sensor's +x (IR / ToF / sonar without the part).</summary>
    public sealed class RangeSensor : MountedSensor
    {
        public RangeSensor(SimRunner runner, SensorSpec spec) : base(runner, spec)
        {
        }

        public override JsonObject Read()
        {
            if (!TryPose(out var p, out var q, out _)) return new JsonObject { ["valid"] = false };
            double rmax = Spec.Num("range_max", 4.0);
            bool hit = Runner.Raycast(p, q.Rotate(Vec3.UnitX), rmax, J.Bool(Spec.Params, "ignore_own", true) ? Spec.Object : null, out var h);
            return new JsonObject
            {
                ["valid"] = true,
                ["frame"] = Name,
                ["range"] = hit ? J.N(h.Distance + Noise.Gaussian(Spec.Num("noise", 0))) : null,
                ["range_min"] = J.N(Spec.Num("range_min", 0.02)),
                ["range_max"] = J.N(rmax),
                ["hit_object"] = hit ? (h.Body?.Tag as WorldObject)?.Id ?? h.Body?.Id : null
            };
        }
    }

    /// <summary>Downward IR reflectance (line track) under the sensor point: 0 = black, ~0.85 = white floor.</summary>
    public sealed class LineSensor : MountedSensor
    {
        public LineSensor(SimRunner runner, SensorSpec spec) : base(runner, spec)
        {
        }

        public override JsonObject Read()
        {
            if (!TryPose(out var p, out _, out _)) return new JsonObject { ["valid"] = false };
            double r = Runner.Floor?.Reflectance(p.X, p.Y) ?? 0.85;
            r = Math.Max(0, Math.Min(1, r + Noise.Gaussian(Spec.Num("noise", 0))));
            return new JsonObject { ["valid"] = true, ["frame"] = Name, ["value"] = J.N(r), ["on_line"] = r < Spec.Num("threshold", 0.4) };
        }
    }

    /// <summary>
    /// Pinhole camera rendered by ray casting (flat colours, one light): RGB8 or depth (float32 metres),
    /// row-major from the top-left, base64 in "data". The camera looks along the sensor's +x, +z up.
    /// Meant for learning pipelines (low resolution), not for photorealism.
    /// </summary>
    public sealed class RayCamera : MountedSensor
    {
        private readonly bool _depth;

        public RayCamera(SimRunner runner, SensorSpec spec, bool depth) : base(runner, spec)
        {
            _depth = depth;
        }

        private int Width => Math.Max(1, Math.Min(640, (int)Spec.Num("width", 64)));
        private int Height => Math.Max(1, Math.Min(480, (int)Spec.Num("height", 48)));

        public override JsonObject Describe()
        {
            var d = base.Describe();
            d["width"] = Width;
            d["height"] = Height;
            d["fov_deg"] = J.N(Spec.Num("fov_deg", 60));
            d["encoding"] = _depth ? "32FC1" : "rgb8";
            return d;
        }

        public override JsonObject Read()
        {
            if (!TryPose(out var p, out var q, out _)) return new JsonObject { ["valid"] = false };
            int w = Width, h = Height;
            double fov = Spec.Num("fov_deg", 60) * Math.PI / 180;
            double f = w / 2.0 / Math.Tan(fov / 2);
            double max = Spec.Num("range_max", 20);
            string? ignore = J.Bool(Spec.Params, "ignore_own", true) ? Spec.Object : null;
            var floor = Runner.Floor;
            var light = new Vec3(-0.35, 0.45, 1).Normalized();
            byte[] data = new byte[w * h * (_depth ? 4 : 3)];
            for (int v = 0; v < h; v++)
            {
                for (int u = 0; u < w; u++)
                {
                    var ray = q.Rotate(new Vec3(1, -(u + 0.5 - w / 2.0) / f, -(v + 0.5 - h / 2.0) / f).Normalized());
                    bool hit = Runner.Raycast(p, ray, max, ignore, out var hh);
                    int k = (v * w + u) * (_depth ? 4 : 3);
                    if (_depth)
                    {
                        float dist = hit ? (float)hh.Distance : float.PositiveInfinity;
                        var bytes = BitConverter.GetBytes(dist);
                        if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
                        Buffer.BlockCopy(bytes, 0, data, k, 4);
                        continue;
                    }

                    double r, g, b;
                    if (!hit)
                    {
                        double t = Math.Max(0, Math.Min(1, ray.Z * 2 + 0.3));
                        (r, g, b) = (0.62 + 0.2 * t, 0.72 + 0.15 * t, 0.86 + 0.1 * t);
                    }
                    else if (hh.Body == null && hh.Normal.Z > 0.8)
                    {
                        // Floor: track lines from the arena, otherwise a light checker of 25 cm tiles.
                        double refl = floor != null ? floor.Reflectance(hh.Point.X, hh.Point.Y) : (((int)Math.Floor(hh.Point.X / 0.25) + (int)Math.Floor(hh.Point.Y / 0.25)) & 1) == 0 ? 0.62 : 0.55;
                        (r, g, b) = (refl, refl, refl * 0.98);
                    }
                    else
                    {
                        (r, g, b) = Runner.ColorOf(hh.Body);
                    }

                    if (hit)
                    {
                        double shade = 0.45 + 0.55 * Math.Max(0, hh.Normal.Dot(light));
                        double fog = Math.Min(1, hh.Distance / max);
                        r = r * shade * (1 - fog * 0.5) + 0.75 * fog * 0.5;
                        g = g * shade * (1 - fog * 0.5) + 0.8 * fog * 0.5;
                        b = b * shade * (1 - fog * 0.5) + 0.88 * fog * 0.5;
                    }

                    data[k] = ToByte(r);
                    data[k + 1] = ToByte(g);
                    data[k + 2] = ToByte(b);
                }
            }

            return new JsonObject
            {
                ["valid"] = true,
                ["frame"] = Name,
                ["width"] = w,
                ["height"] = h,
                ["encoding"] = _depth ? "32FC1" : "rgb8",
                ["step"] = w * (_depth ? 4 : 3),
                ["fx"] = J.N(f),
                ["fy"] = J.N(f),
                ["cx"] = J.N(w / 2.0),
                ["cy"] = J.N(h / 2.0),
                ["data"] = Convert.ToBase64String(data)
            };
        }

        private static byte ToByte(double x) => (byte)Math.Max(0, Math.Min(255, Math.Round(x * 255)));
    }

    // ==================================================================== part sensors

    /// <summary>HC-SR04 part in a world: the distance its beam measures (what the firmware gets as echo time).</summary>
    public sealed class UltrasonicPartSensor : ISimSensor
    {
        private readonly WorldRunner _runner;
        private readonly WorldObject _object;
        private readonly UltrasonicSensor _device;

        public UltrasonicPartSensor(WorldRunner runner, WorldObject o, UltrasonicSensor device)
        {
            _runner = runner;
            _object = o;
            _device = device;
        }

        public string Name => _object.Id + ".range";
        public string Type => "ultrasonic";
        public string? Object => _object.Id;

        public JsonObject Describe() => new JsonObject
        {
            ["label"] = _object.Name,
            ["range_min"] = J.N(_device.MinRange),
            ["range_max"] = J.N(_device.MaxRange),
            ["field_of_view"] = J.N(0.26)
        };

        public JsonObject Read()
        {
            var beam = _runner.Physics?.SonarBeam(_object.Id);
            double d = beam?.Range ?? _device.Distance;
            var o = new JsonObject
            {
                ["frame"] = Name,
                ["range"] = J.N(d),
                ["in_range"] = d >= _device.MinRange && d <= _device.MaxRange,
                ["range_min"] = J.N(_device.MinRange),
                ["range_max"] = J.N(_device.MaxRange),
                ["echo_pulse"] = J.N(_device.LastPulseWidth)
            };
            if (beam != null)
            {
                o["from"] = J.V(beam.Value.From);
                o["to"] = J.V(beam.Value.To);
            }

            return o;
        }
    }

    /// <summary>Angle/speed of a motor shaft or servo horn (quadrature encoder with <c>cpr</c> counts per revolution).</summary>
    public sealed class EncoderSensor : ISimSensor
    {
        private readonly WorldRunner _runner;
        private readonly WorldObject _object;

        public EncoderSensor(WorldRunner runner, WorldObject o)
        {
            _runner = runner;
            _object = o;
        }

        public string Name => _object.Id + ".encoder";
        public string Type => "encoder";
        public string? Object => _object.Id;

        public JsonObject Describe() => new JsonObject { ["label"] = _object.Name, ["cpr"] = 1440 };

        public JsonObject Read()
        {
            var s = _runner.PortState(_object.Id);
            if (s == null) return new JsonObject { ["frame"] = Name, ["valid"] = false };
            return new JsonObject
            {
                ["frame"] = Name,
                ["valid"] = true,
                ["angle"] = J.N(s.Value.Angle),
                ["velocity"] = J.N(s.Value.Speed),
                ["ticks"] = (long)Math.Floor(s.Value.Angle / (2 * Math.PI) * 1440)
            };
        }
    }

    /// <summary>A sensor mounted on a Montaj-page vehicle (line, obstacle, ultrasonic, IMU).</summary>
    public sealed class VehicleMountSensor : ISimSensor
    {
        private readonly VehicleRunner _runner;
        private readonly SensorLink _link;

        public VehicleMountSensor(VehicleRunner runner, SensorLink link)
        {
            _runner = runner;
            _link = link;
        }

        public string Name => _link.Mount.RefDes;

        public string Type => _link.Mount.Kind switch
        {
            SensorKind.Line => "line",
            SensorKind.Obstacle => "obstacle",
            SensorKind.Ultrasonic => "ultrasonic",
            _ => "imu"
        };

        public string? Object => VehicleRunner.VehicleId;

        public JsonObject Describe() => new JsonObject
        {
            ["x"] = J.N(_link.Mount.X),
            ["y"] = J.N(_link.Mount.Y),
            ["z"] = J.N(_link.Mount.Z),
            ["yaw_deg"] = J.N(_link.Mount.Yaw),
            ["range"] = J.N(_link.Mount.Range)
        };

        public JsonObject Read()
        {
            var o = new JsonObject { ["frame"] = Name, ["value"] = J.N(_link.Value), ["triggered"] = _link.Triggered };
            if (_link.Mount.Kind == SensorKind.Ultrasonic || _link.Mount.Kind == SensorKind.Obstacle) o["range"] = J.N(_link.Value);
            if (_link.Mount.Kind == SensorKind.Imu && _runner.I2cDevice(_link.Mount.RefDes) is Firmware.Devices.Mpu6050 mpu)
            {
                o["linear_acceleration"] = J.V(new Vec3(mpu.AccelX, mpu.AccelY, mpu.AccelZ));
                o["angular_velocity"] = J.V(new Vec3(mpu.GyroX, mpu.GyroY, mpu.GyroZ));
            }

            return o;
        }
    }
}
