using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros] A running world on ROS 2 through rosbridge, as a robot driver would put it there: <c>/clock</c> (the simulated
    /// time, for nodes with <c>use_sim_time</c>), <c>/joint_states</c> (every hinge and slider by its URDF name: position in
    /// rad or m, velocity) and, the other way, <c>/joint_command</c> (a <c>sensor_msgs/JointState</c> whose positions, or
    /// velocities for a speed motor, become the joints' motor targets — the topic Isaac Sim's ROS bridge also uses).
    /// A robot free on the ground adds <c>/odom</c>, <c>/tf</c> and <c>/cmd_vel</c> (RosWorldLink.Mobile.cs); the sensors its
    /// description declares publish their scans and IMU readings (RosWorldLink.Sensors.cs).
    /// <see cref="Step"/> runs with the simulation; the network work is the client's own.
    /// </summary>
    public sealed partial class RosWorldLink
    {
        public const string ClockTopic = "/clock", JointStatesTopic = "/joint_states", JointCommandTopic = "/joint_command";

        private readonly RosBridgeClient _ros;
        private readonly WorldPhysics _physics;
        private readonly List<(WorldJoint Joint, bool Hinge)> _joints;
        private readonly Dictionary<string, (WorldJoint Joint, bool Hinge)> _byName;
        private readonly double _statePeriod, _clockPeriod;
        private double _lastState = double.NegativeInfinity, _lastClock = double.NegativeInfinity;
        private double[] _lastPositions;
        private double _lastPositionsTime = double.NaN;

        public RosWorldLink(RosBridgeClient ros, WorldDocument world, WorldPhysics physics, double jointStateHz = 50, double clockHz = 100)
        {
            _ros = ros;
            _physics = physics;
            _joints = world.Joints.Where(j => j.Kind != WorldJointKind.Fixed).Select(j => (j, j.Kind == WorldJointKind.Hinge)).ToList();
            _byName = new Dictionary<string, (WorldJoint, bool)>(StringComparer.Ordinal);
            foreach (var x in _joints) _byName[x.Joint.RosName] = x;
            _lastPositions = new double[_joints.Count];
            _statePeriod = 1 / Math.Max(1, jointStateHz);
            _clockPeriod = 1 / Math.Max(1, clockHz);
            ros.Advertise(ClockTopic, "rosgraph_msgs/msg/Clock");
            ros.Advertise(JointStatesTopic, "sensor_msgs/msg/JointState");
            ros.Subscribe(JointCommandTopic, "sensor_msgs/msg/JointState");
            ros.Subscribe(JointTrajectoryTopic, "trajectory_msgs/msg/JointTrajectory");
            FindBases(world);
            FindSensors(world);
        }

        /// <summary>The joints ROS sees, by name.</summary>
        public IReadOnlyCollection<string> JointNames => _byName.Keys;

        /// <summary>Commands taken from <c>/joint_command</c> (joints set), and names in them that no joint has.</summary>
        public long Commands { get; private set; }

        public HashSet<string> UnknownNames { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Publishes what is due at simulated <paramref name="time"/> (s) and applies the commands that came.</summary>
        public void Step(double time)
        {
            while (_ros.TryReceive(out string topic, out var msg))
            {
                if (topic == JointCommandTopic) Command(msg);
                else if (topic == JointTrajectoryTopic) FollowTrajectory(msg, time);
                else Twist(topic, msg, time);
            }

            StepTrajectory(time);
            DriveBases(time);
            if (!_ros.Connected) return;
            if (time - _lastClock >= _clockPeriod - 1e-9)
            {
                _lastClock = time;
                _ros.Publish(ClockTopic, new JsonObject { ["clock"] = Stamp(time) });
            }

            if (time - _lastState >= _statePeriod - 1e-9)
            {
                _lastState = time;
                PublishJointStates(time);
                PublishBases(time);
            }

            StepSensors(time);
        }

        private void PublishJointStates(double time)
        {
            var names = new JsonArray();
            var positions = new JsonArray();
            var velocities = new JsonArray();
            double dt = double.IsNaN(_lastPositionsTime) ? 0 : time - _lastPositionsTime;
            for (int i = 0; i < _joints.Count; i++)
            {
                var (joint, _) = _joints[i];
                double q = _physics.Scene.Joints.FirstOrDefault(s => s.Id == joint.Id)?.Angle ?? 0;   // rad or m
                names.Add(joint.RosName);
                positions.Add(q);
                velocities.Add(dt > 0 ? (q - _lastPositions[i]) / dt : 0.0);
                _lastPositions[i] = q;
            }

            _lastPositionsTime = time;
            _ros.Publish(JointStatesTopic, new JsonObject
            {
                ["header"] = new JsonObject { ["stamp"] = Stamp(time), ["frame_id"] = string.Empty },
                ["name"] = names,
                ["position"] = positions,
                ["velocity"] = velocities,
                ["effort"] = new JsonArray()
            });
        }

        /// <summary>A <c>/joint_command</c>: each named joint's motor to its position (rad or m), or a speed motor to its velocity.</summary>
        private void Command(JsonNode msg)
        {
            if (msg["name"] is not JsonArray names) return;
            _trajectory = null;   // a direct command takes over from a trajectory
            var positions = msg["position"] as JsonArray;
            var velocities = msg["velocity"] as JsonArray;
            for (int i = 0; i < names.Count; i++)
            {
                string name = (string?)names[i] ?? string.Empty;
                if (!_byName.TryGetValue(name, out var x))
                {
                    UnknownNames.Add(name);
                    continue;
                }

                bool speed = x.Joint.Drive == WorldJoint.DriveSpeedMode;
                var values = speed ? velocities : positions;
                if (values == null || i >= values.Count || values[i] is null) continue;
                double v = (double)values[i]!;
                if (double.IsNaN(v)) continue;
                if (_physics.SetJointTarget(x.Joint.Id, x.Hinge ? v * 180 / Math.PI : v)) Commands++;
            }
        }

        private static JsonObject Stamp(double time)
        {
            long sec = (long)Math.Floor(time);
            long nanosec = (long)Math.Round((time - sec) * 1e9);
            if (nanosec >= 1_000_000_000)
            {
                sec++;
                nanosec -= 1_000_000_000;
            }

            return new JsonObject { ["sec"] = sec, ["nanosec"] = nanosec };
        }
    }
}
