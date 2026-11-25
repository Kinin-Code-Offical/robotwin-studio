using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros] The sensors a robot description declares (<see cref="RobotSensor"/>, from Gazebo's <c>&lt;sensor&gt;</c>), run
    /// as Gazebo runs them: a laser scanner casts its rays into this world at its rate and publishes a
    /// <c>sensor_msgs/LaserScan</c>; an IMU publishes <c>sensor_msgs/Imu</c> from the motion of the part it rides on
    /// (orientation, angular rate, specific force), each with its declared Gaussian noise. The topic is the one the
    /// description gives (its plugin's remapping), under the robot's namespace; the frame is its link, which
    /// <c>robot_state_publisher</c> places from the URDF.
    /// </summary>
    public sealed partial class RosWorldLink
    {
        private sealed class SensorRun
        {
            public WorldObject Part = null!;
            public RobotSensor Spec = null!;
            public string Topic = string.Empty, Frame = string.Empty;
            public Transform3 Mount = Transform3.Identity;   // the sensor in the part's frame
            public HashSet<string> Own = null!;   // the robot's parts: a scanner does not see its own robot
            public double Last = double.NegativeInfinity, LastMotion = double.NaN;
            public ImuModel? Motion;
            public Random Rng = null!;
            public long Sent;
        }

        private readonly List<SensorRun> _sensors = new List<SensorRun>();
        private WorldSpec? _room;

        /// <summary>The robot-description sensors published: topic, kind, messages sent.</summary>
        public IEnumerable<(string Topic, string Kind, long Sent)> SensorTopics => _sensors.Select(s => (s.Topic, s.Spec.Kind, s.Sent));

        private void FindSensors(WorldDocument world)
        {
            _room = world.Environment;
            int seed = 7;
            foreach (var part in world.Objects.Where(o => o.RobotSensors is { Count: > 0 }))
            {
                // The robot it belongs to: the mobile base it is mounted on (its namespace), or the part's own assembly.
                var b = _bases.FirstOrDefault(x => x.Part == part || world.MountedOn(x.Part.Id).Contains(part));
                var root = b?.Part ?? part;
                var own = new HashSet<string>(world.MountedOn(root.Id).Select(o => o.Id).Append(root.Id), StringComparer.OrdinalIgnoreCase);
                string ns = b?.Namespace ?? string.Empty;
                foreach (var s in part.RobotSensors!)
                {
                    var p = s.Pose is { Length: 6 } ? s.Pose : new double[6];
                    var run = new SensorRun
                    {
                        Part = part,
                        Spec = s,
                        Topic = s.Topic.StartsWith("/", StringComparison.Ordinal) ? s.Topic : ns + "/" + s.Topic,
                        Frame = (ns.Length == 0 ? string.Empty : ns.Substring(1) + "/") + (string.IsNullOrEmpty(s.Frame) ? part.Link ?? part.Name : s.Frame),
                        Mount = Transform3.FromRpy(p[0], p[1], p[2], p[3], p[4], p[5]),
                        Own = own,
                        Rng = new Random(seed++ * 7919),
                        Motion = s.Kind == "imu" ? new ImuModel(ImuSpec.Mpu6050, seed) : null
                    };
                    _ros.Advertise(run.Topic, s.Kind == "imu" ? "sensor_msgs/msg/Imu" : "sensor_msgs/msg/LaserScan");
                    _sensors.Add(run);
                }
            }
        }

        private void StepSensors(double time)
        {
            foreach (var run in _sensors)
            {
                if (_physics.MotionOf(run.Part.Id) is not { } m) continue;
                var at = new Transform3(m.Position, m.Orientation) * run.Mount;
                if (run.Motion != null)
                {
                    // The IMU follows the motion every step (its acceleration is the change of its velocity).
                    var lever = m.Orientation.Rotate(run.Mount.P);
                    var v = m.Velocity + m.AngularVelocity.Cross(lever);
                    double dt = double.IsNaN(run.LastMotion) ? 0 : time - run.LastMotion;
                    run.LastMotion = time;
                    if (dt > 0) run.Motion.Update(dt, at.Q, v, m.AngularVelocity, _room?.Gravity ?? 9.80665, addNoise: false);
                }

                if (time - run.Last < 1 / Math.Max(0.1, run.Spec.Rate) - 1e-9) continue;
                run.Last = time;
                if (!_ros.Connected) continue;
                var header = new JsonObject { ["stamp"] = Stamp(time), ["frame_id"] = run.Frame };
                _ros.Publish(run.Topic, run.Motion != null ? ImuMessage(run, at, header) : ScanMessage(run, at, header));
                run.Sent++;
            }
        }

        /// <summary>A scan taken all at once from the sensor's pose (as Gazebo's ray sensor), rays counter-clockwise about its z.</summary>
        private JsonObject ScanMessage(SensorRun run, Transform3 at, JsonObject header)
        {
            var s = run.Spec;
            int n = Math.Max(1, s.Samples);
            double step = n > 1 ? (s.MaxAngle - s.MinAngle) / (n - 1) : 0;
            var ranges = new JsonArray();
            bool Theirs(SceneBody b)
            {
                int colon = b.Id.IndexOf(':');
                return !run.Own.Contains(b.Id) && !(colon > 0 && run.Own.Contains(b.Id.Substring(0, colon)));
            }

            for (int i = 0; i < n; i++)
            {
                double a = s.MinAngle + i * step;
                var dir = at.ApplyVector(new Vec3(Math.Cos(a), Math.Sin(a), 0));
                double d = Cast(at.P, dir, s.MaxRange, Theirs);
                if (d < s.MaxRange && s.Noise > 0) d += s.Noise * Gauss(run.Rng);
                // Nothing within reach: past range_max, which ROS reads as no return (JSON has no infinity).
                ranges.Add(d >= s.MinRange && d <= s.MaxRange ? Math.Round(d, 5) : s.MaxRange + 1);
            }

            return new JsonObject
            {
                ["header"] = header,
                ["angle_min"] = s.MinAngle,
                ["angle_max"] = s.MinAngle + step * (n - 1),
                ["angle_increment"] = step,
                ["time_increment"] = 0.0,
                ["scan_time"] = 1 / Math.Max(0.1, s.Rate),
                ["range_min"] = s.MinRange,
                ["range_max"] = s.MaxRange,
                ["ranges"] = ranges,
                ["intensities"] = new JsonArray()
            };
        }

        /// <summary>The nearest surface along a ray: the bodies (but the robot's own), the ground and the room's walls.</summary>
        private double Cast(Vec3 origin, Vec3 dir, double max, Func<SceneBody, bool> filter)
        {
            double best = double.PositiveInfinity;
            if (_physics.Scene.Raycast(origin, dir, max, out var hit, filter, true)) best = hit.Distance;
            if (_room is { Walls: true } room)
            {
                foreach (var (lo, hi) in WorldSensing.RoomWalls(room))
                {
                    double t = WorldSensing.RayBox(origin, dir, lo, hi);
                    if (t >= 0 && t < best && t <= max) best = t;
                }
            }

            return best;
        }

        /// <summary>An IMU's reading: its orientation in the world, angular rate and specific force in its own axes.</summary>
        private static JsonObject ImuMessage(SensorRun run, Transform3 at, JsonObject header)
        {
            var imu = run.Motion!;
            var s = run.Spec;
            var w = imu.TrueGyro + new Vec3(Gauss(run.Rng), Gauss(run.Rng), Gauss(run.Rng)) * s.Noise;
            var a = imu.TrueAccel + new Vec3(Gauss(run.Rng), Gauss(run.Rng), Gauss(run.Rng)) * s.AccelNoise;
            double gv = s.Noise * s.Noise, av = s.AccelNoise * s.AccelNoise;
            return new JsonObject
            {
                ["header"] = header,
                ["orientation"] = Rotation(at.Q),
                ["orientation_covariance"] = Diagonal(0),
                ["angular_velocity"] = Xyz(w),
                ["angular_velocity_covariance"] = Diagonal(gv),
                ["linear_acceleration"] = Xyz(a),
                ["linear_acceleration_covariance"] = Diagonal(av)
            };
        }

        private static JsonArray Diagonal(double v) => new JsonArray(v, 0.0, 0.0, 0.0, v, 0.0, 0.0, 0.0, v);

        private static double Gauss(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
    }
}
