using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros] <c>/joint_trajectory</c> (<c>trajectory_msgs/JointTrajectory</c>), as ros2_control's joint trajectory controller
    /// takes it on its topic: the named joints follow the points in time — between two points a cubic through both
    /// positions and velocities (or a straight line when the points give no velocities), after the last one they hold it.
    /// The joints' motors track the moving target with their own torque and speed limits, so a trajectory faster than the
    /// robot can go lags as the real one would. A new trajectory replaces the one running (from now, as an empty stamp asks).
    /// </summary>
    public sealed partial class RosWorldLink
    {
        public const string JointTrajectoryTopic = "/joint_trajectory";

        private sealed class Trajectory
        {
            public double Start;
            public List<(Sandbox.WorldJoint Joint, bool Hinge, int Column)> Joints = new List<(Sandbox.WorldJoint, bool, int)>();
            public List<(double Time, double[] Positions, double[]? Velocities)> Points = new List<(double, double[], double[]?)>();
            public double[] From = Array.Empty<double>();   // where the joints were when it came (the segment before the first point)
        }

        private Trajectory? _trajectory;

        /// <summary>Trajectories taken from <c>/joint_trajectory</c>, and whether one is still moving.</summary>
        public long Trajectories { get; private set; }

        public bool Following => _trajectory != null;

        private void FollowTrajectory(JsonNode msg, double time)
        {
            if (msg["joint_names"] is not JsonArray names || msg["points"] is not JsonArray points || points.Count == 0) return;
            var t = new Trajectory { Start = time };
            for (int c = 0; c < names.Count; c++)
            {
                string name = (string?)names[c] ?? string.Empty;
                if (_byName.TryGetValue(name, out var x) && x.Joint.Drive == Sandbox.WorldJoint.DrivePosition)
                {
                    if (x.Joint.MimicOf == null) t.Joints.Add((x.Joint, x.Hinge, c));   // a mimic follows its master
                }
                else
                {
                    UnknownNames.Add(name);
                }
            }

            if (t.Joints.Count == 0) return;
            foreach (var p in points)
            {
                if (p?["positions"] is not JsonArray q || q.Count < names.Count) continue;
                double at = Seconds(p["time_from_start"]);
                var positions = t.Joints.Select(j => Number(q[j.Column])).ToArray();
                double[]? velocities = p["velocities"] is JsonArray v && v.Count >= names.Count ? t.Joints.Select(j => Number(v[j.Column])).ToArray() : null;
                if (positions.Any(double.IsNaN) || double.IsNaN(at)) continue;
                if (velocities != null && velocities.Any(double.IsNaN)) velocities = null;
                if (t.Points.Count > 0 && at <= t.Points[t.Points.Count - 1].Time) continue;   // time must go forward
                t.Points.Add((at, positions, velocities));
            }

            if (t.Points.Count == 0) return;
            t.From = t.Joints.Select(j => JointPosition(j.Joint)).ToArray();
            _trajectory = t;
            Trajectories++;
        }

        /// <summary>Sets the joints' targets where the running trajectory is at simulated <paramref name="time"/>.</summary>
        private void StepTrajectory(double time)
        {
            var t = _trajectory;
            if (t == null) return;
            double s = time - t.Start;
            var last = t.Points[t.Points.Count - 1];
            bool done = s >= last.Time;
            for (int j = 0; j < t.Joints.Count; j++)
            {
                double q = done ? last.Positions[j] : At(t, j, s);
                var (joint, hinge, _) = t.Joints[j];
                _physics.SetJointTarget(joint.Id, hinge ? q * 180 / Math.PI : q);
            }

            if (done) _trajectory = null;   // the targets stay at the last point
        }

        /// <summary>Joint <paramref name="j"/> at <paramref name="s"/> s from the start: cubic Hermite between the points around it.</summary>
        private static double At(Trajectory t, int j, double s)
        {
            double t0 = 0, q0 = t.From[j], v0 = 0;
            foreach (var p in t.Points)
            {
                if (s <= p.Time)
                {
                    double h = p.Time - t0, u = h > 0 ? (s - t0) / h : 1;
                    double q1 = p.Positions[j];
                    if (p.Velocities == null) return q0 + (q1 - q0) * u;
                    double v1 = p.Velocities[j];
                    double u2 = u * u, u3 = u2 * u;
                    return (2 * u3 - 3 * u2 + 1) * q0 + (u3 - 2 * u2 + u) * h * v0 + (-2 * u3 + 3 * u2) * q1 + (u3 - u2) * h * v1;
                }

                t0 = p.Time;
                q0 = p.Positions[j];
                v0 = p.Velocities?[j] ?? 0;
            }

            return q0;
        }

        private double JointPosition(Sandbox.WorldJoint joint)
        {
            var scene = _physics.Scene.Joints.FirstOrDefault(s => s.Id == joint.Id);
            return scene?.Angle ?? 0;
        }

        private static double Seconds(JsonNode? duration)
        {
            if (duration == null) return double.NaN;
            double sec = Number(duration["sec"]), nanosec = Number(duration["nanosec"]);
            if (double.IsNaN(nanosec)) nanosec = 0;
            return double.IsNaN(sec) ? double.NaN : sec + nanosec * 1e-9;
        }
    }
}
