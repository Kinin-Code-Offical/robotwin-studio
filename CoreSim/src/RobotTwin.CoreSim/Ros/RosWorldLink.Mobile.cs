using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros] Robots free on the ground, as a mobile robot's driver puts them on ROS: <c>/odom</c> (<c>nav_msgs/Odometry</c>,
    /// the base link's pose from where it started and its velocity in its own frame), <c>/tf</c> (odom → base link; the
    /// links above it come from <c>robot_state_publisher</c> with the URDF and <c>/joint_states</c>) and <c>/cmd_vel</c>
    /// (<c>geometry_msgs/Twist</c>: forward speed and turn rate, turned into its drive wheels' speeds the way
    /// ros2_control's diff drive controller does, with the wheel radius and track taken from the robot's own shapes).
    /// The odometry is the simulation's truth: no wheel slip in it, as Gazebo's ground-truth odometry.
    /// A second robot's topics and frames go under <c>/robot2</c>, and so on.
    /// </summary>
    public sealed partial class RosWorldLink
    {
        public const string OdomTopic = "/odom", TfTopic = "/tf", CmdVelTopic = "/cmd_vel";

        /// <summary>A <c>/cmd_vel</c> older than this (s, simulated) stops the wheels, as the diff drive controller's timeout.</summary>
        public const double CmdVelTimeout = 0.5;

        private readonly List<MobileBase> _bases = new List<MobileBase>();

        /// <summary>The robots free on the ground, with their odometry and drive wheels.</summary>
        public IReadOnlyList<MobileBase> Bases => _bases;

        /// <summary>A robot base free on the ground: its frame, topics and drive wheels.</summary>
        public sealed class MobileBase
        {
            internal MobileBase(WorldObject part, string ns, Vec3 origin)
            {
                Part = part;
                Namespace = ns;
                Origin = origin;
            }

            internal WorldObject Part { get; }

            /// <summary>"" for the first robot, "/robot2" … for the next.</summary>
            public string Namespace { get; }

            /// <summary>The base link's frame (its URDF name, under the namespace).</summary>
            public string Frame => Prefix + (Part.Link ?? Part.Name);

            public string OdomFrame => Prefix + "odom";

            public string OdomTopic => Namespace + RosWorldLink.OdomTopic;

            public string CmdVelTopic => Namespace + RosWorldLink.CmdVelTopic;

            private string Prefix => Namespace.Length == 0 ? string.Empty : Namespace.Substring(1) + "/";

            /// <summary>The link's frame origin in the part's frame.</summary>
            internal Vec3 Origin { get; }

            internal readonly List<(WorldJoint Joint, double Lateral, double DegPerMetre)> Wheels = new List<(WorldJoint, double, double)>();

            /// <summary>The drive wheels' joints (URDF names).</summary>
            public IEnumerable<string> WheelNames => Wheels.Select(w => w.Joint.RosName);

            /// <summary>Distance between the outermost drive wheels (m) and their mean rolling radius (m).</summary>
            public double WheelSeparation { get; internal set; }

            public double WheelRadius { get; internal set; }

            /// <summary>Twists taken from <c>/cmd_vel</c>, and the last one (m/s, rad/s).</summary>
            public long Twists { get; internal set; }

            public double Speed { get; internal set; }

            public double TurnRate { get; internal set; }

            internal double TwistTime = double.NegativeInfinity;
            internal bool Driving;
            internal Transform3? Start;
        }

        /// <summary>The robots whose base is a robot-description link free on the ground (not static, not mounted on anything).</summary>
        private void FindBases(WorldDocument world)
        {
            var mounted = new HashSet<string>(world.Joints.Select(j => j.ObjectA), StringComparer.OrdinalIgnoreCase);
            bool tf = false;
            foreach (var part in world.Objects.Where(o => o.Link != null && !o.IsStatic && !mounted.Contains(o.Id)))
            {
                if (_physics.MotionOf(part.Id) is not { } now) continue;
                var origin = part.ModelOrigin is { Length: 3 } m ? new Vec3(m[0], m[1], m[2]) : Vec3.Zero;
                var b = new MobileBase(part, _bases.Count == 0 ? string.Empty : "/robot" + (_bases.Count + 1), origin);

                // Drive wheels: speed-driven hinges on the base whose axis lies across it (a caster's swivel does not roll it).
                var placed = Frame(part.Pose.Position, part.Pose.Orientation, origin);
                var radii = new List<double>();
                foreach (var j in world.Joints.Where(j => j.Kind == WorldJointKind.Hinge && j.Drive == WorldJoint.DriveSpeedMode && string.Equals(j.ObjectB, part.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    var axis = placed.Q.InverseRotate(new Vec3(j.AxisX, j.AxisY, j.AxisZ).Normalized());
                    if (Math.Abs(axis.Y) < 0.5) continue;
                    double? r = _physics.RollingRadius(j.ObjectA, now.Orientation.Rotate(axis));
                    if (r is not > 1e-4) continue;
                    var anchor = placed.Inverse().Apply(new Vec3(j.AnchorX, j.AnchorY, j.AnchorZ));
                    // Turning about +y rolls a wheel forward (+x) at ω·r; about an axis tilted from y, by its y share.
                    b.Wheels.Add((j, anchor.Y, 180 / Math.PI / (r.Value * axis.Y)));
                    radii.Add(r.Value);
                }

                if (b.Wheels.Count > 0)
                {
                    b.WheelSeparation = b.Wheels.Max(w => w.Lateral) - b.Wheels.Min(w => w.Lateral);
                    b.WheelRadius = radii.Average();
                    _ros.Subscribe(b.CmdVelTopic, "geometry_msgs/msg/Twist");
                }

                _ros.Advertise(b.OdomTopic, "nav_msgs/msg/Odometry");
                if (!tf) _ros.Advertise(TfTopic, "tf2_msgs/msg/TFMessage");
                tf = true;
                _bases.Add(b);
            }
        }

        private static Transform3 Frame(Vec3 position, Quat orientation, Vec3 origin) => new Transform3(position + orientation.Rotate(origin), orientation);

        /// <summary>A <c>/cmd_vel</c> for one of the bases: its forward speed and turn rate, from now until it times out.</summary>
        private void Twist(string topic, JsonNode msg, double time)
        {
            var b = _bases.FirstOrDefault(x => x.CmdVelTopic == topic);
            if (b == null || b.Wheels.Count == 0) return;
            double v = Number(msg["linear"]?["x"]), w = Number(msg["angular"]?["z"]);
            if (double.IsNaN(v) || double.IsNaN(w)) return;
            b.Speed = v;
            b.TurnRate = w;
            b.TwistTime = time;
            b.Twists++;
            b.Driving = true;
            foreach (var (joint, lateral, degPerMetre) in b.Wheels) _physics.SetJointTarget(joint.Id, (v - w * lateral) * degPerMetre);
        }

        private static double Number(JsonNode? n)
        {
            if (n is not JsonValue value) return 0;
            if (value.TryGetValue(out double d)) return d;
            if (value.TryGetValue(out long l)) return l;
            return double.NaN;
        }

        /// <summary>Stops the wheels of a base whose last twist is too old (a teleop that went away must not drive on).</summary>
        private void DriveBases(double time)
        {
            foreach (var b in _bases)
            {
                if (!b.Driving || time - b.TwistTime <= CmdVelTimeout) continue;
                b.Driving = false;
                b.Speed = b.TurnRate = 0;
                foreach (var (joint, _, _) in b.Wheels) _physics.SetJointTarget(joint.Id, 0);
            }
        }

        private void PublishBases(double time)
        {
            if (_bases.Count == 0) return;
            var transforms = new JsonArray();
            foreach (var b in _bases)
            {
                if (_physics.MotionOf(b.Part.Id) is not { } m) continue;
                var frame = Frame(m.Position, m.Orientation, b.Origin);
                b.Start ??= new Transform3(frame.P, YawOnly(frame.Q));   // odom: where the robot started, level
                var pose = b.Start.Value.Inverse() * frame;
                // The base link's own velocity, in its own frame (REP 105: the twist is in the child frame).
                var lever = m.Orientation.Rotate(b.Origin);
                var v = frame.Q.InverseRotate(m.Velocity + m.AngularVelocity.Cross(lever));
                var w = frame.Q.InverseRotate(m.AngularVelocity);
                var stamp = Stamp(time);
                _ros.Publish(b.OdomTopic, new JsonObject
                {
                    ["header"] = new JsonObject { ["stamp"] = stamp, ["frame_id"] = b.OdomFrame },
                    ["child_frame_id"] = b.Frame,
                    ["pose"] = new JsonObject { ["pose"] = Pose(pose) },
                    ["twist"] = new JsonObject { ["twist"] = new JsonObject { ["linear"] = Xyz(v), ["angular"] = Xyz(w) } }
                });
                transforms.Add(new JsonObject
                {
                    ["header"] = new JsonObject { ["stamp"] = Stamp(time), ["frame_id"] = b.OdomFrame },
                    ["child_frame_id"] = b.Frame,
                    ["transform"] = new JsonObject { ["translation"] = Xyz(pose.P), ["rotation"] = Rotation(pose.Q) }
                });
            }

            if (transforms.Count > 0) _ros.Publish(TfTopic, new JsonObject { ["transforms"] = transforms });
        }

        private static Quat YawOnly(Quat q)
        {
            var (_, _, yaw) = q.ToEuler();
            return Quat.FromAxisAngle(Vec3.UnitZ, yaw);
        }

        private static JsonObject Xyz(Vec3 v) => new JsonObject { ["x"] = v.X, ["y"] = v.Y, ["z"] = v.Z };

        private static JsonObject Rotation(Quat q) => new JsonObject { ["x"] = q.X, ["y"] = q.Y, ["z"] = q.Z, ["w"] = q.W };

        private static JsonObject Pose(Transform3 t) => new JsonObject { ["position"] = Xyz(t.P), ["orientation"] = Rotation(t.Q) };
    }
}
