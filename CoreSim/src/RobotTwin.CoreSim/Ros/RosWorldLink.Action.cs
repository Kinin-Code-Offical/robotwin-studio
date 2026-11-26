using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros-action] The world as ros2_control's joint trajectory controller is to MoveIt: a
    /// <c>control_msgs/action/FollowJointTrajectory</c> action server (through rosbridge's action support) at
    /// <c>/joint_trajectory_controller/follow_joint_trajectory</c>. A goal's trajectory runs as one on
    /// <c>/joint_trajectory</c> does; feedback (desired, actual, error) goes out ten times a second; at its end the goal
    /// succeeds when every joint the goal gives a position tolerance for is within it, or is aborted with
    /// GOAL_TOLERANCE_VIOLATED when they are not there by the goal's time tolerance (1 s when it gives none). A goal naming a
    /// joint the world does not have is aborted with INVALID_JOINTS, one without points with INVALID_GOAL; a new goal
    /// aborts the one running (as the controller preempts it); a cancel holds the joints where they are.
    /// Path tolerances are not checked.
    /// </summary>
    public sealed partial class RosWorldLink
    {
        public const string DefaultTrajectoryAction = "/joint_trajectory_controller/follow_joint_trajectory";
        public const string FollowJointTrajectoryType = "control_msgs/action/FollowJointTrajectory";

        // FollowJointTrajectory result codes and action_msgs/GoalStatus.
        public const int Successful = 0, InvalidGoal = -1, InvalidJoints = -2, GoalToleranceViolated = -5;
        private const int StatusSucceeded = 4, StatusCanceled = 5, StatusAborted = 6;

        private sealed class Goal
        {
            public string Id = string.Empty;
            public Trajectory Trajectory = null!;
            public double End, Deadline, NextFeedback;
            public double[] Tolerances = Array.Empty<double>();   // per joint of the trajectory; 0: not checked
        }

        private Goal? _goal;

        /// <summary>The action's name (where MoveIt's controller manager is told to find it).</summary>
        public string TrajectoryAction { get; private set; } = DefaultTrajectoryAction;

        /// <summary>Goals taken, and how the finished ones ended: (id, GoalStatus, error code).</summary>
        public long Goals { get; private set; }

        public List<(string Id, int Status, int ErrorCode)> GoalResults { get; } = new List<(string, int, int)>();

        private void AdvertiseTrajectoryAction(string action)
        {
            TrajectoryAction = action;
            _ros.AdvertiseAction(action, FollowJointTrajectoryType);
        }

        private void ActionOp(string op, JsonNode msg, double time)
        {
            string id = (string?)msg["id"] ?? string.Empty;
            if ((string?)msg["action"] != TrajectoryAction) return;
            if (op == "@cancel_action_goal")
            {
                if (_goal == null || _goal.Id != id) return;
                foreach (var (joint, hinge, _) in _goal.Trajectory.Joints)
                    _physics.SetJointTarget(joint.Id, hinge ? JointPosition(joint) * 180 / Math.PI : JointPosition(joint));   // hold where it is
                if (_trajectory == _goal.Trajectory) _trajectory = null;
                Finish(StatusCanceled, Successful, "canceled");
                return;
            }

            Goals++;
            var trajectory = msg["args"]?["trajectory"];
            if (trajectory?["joint_names"] is not JsonArray names || trajectory["points"] is not JsonArray points)
            {
                Reject(id, InvalidGoal, "the goal has no trajectory");
                return;
            }

            var unknown = names.Select(n => (string?)n ?? string.Empty).Where(n => !_byName.TryGetValue(n, out var x) || x.Joint.Drive != Sandbox.WorldJoint.DrivePosition).ToList();
            if (unknown.Count > 0)
            {
                Reject(id, InvalidJoints, "joints not in this world (or without a position motor): " + string.Join(", ", unknown));
                return;
            }

            var before = _trajectory;
            FollowTrajectory(trajectory, time);
            if (_trajectory == null || _trajectory == before)
            {
                Reject(id, InvalidGoal, points.Count == 0 ? "the trajectory has no points" : "no usable point (positions for every joint, time going forward)");
                return;
            }

            if (_goal != null) Finish(StatusAborted, Successful, "preempted by a new goal");   // the controller takes the newest
            var t = _trajectory;
            var tolerances = new double[t.Joints.Count];
            if (msg["args"]?["goal_tolerance"] is JsonArray given)
            {
                foreach (var g in given)
                {
                    int k = t.Joints.FindIndex(j => j.Joint.RosName == (string?)g?["name"]);
                    double tol = Number(g?["position"]);
                    if (k >= 0 && tol > 0) tolerances[k] = tol;
                }
            }

            double end = t.Start + t.Points[t.Points.Count - 1].Time;
            double slack = Seconds(msg["args"]?["goal_time_tolerance"]);
            _goal = new Goal
            {
                Id = id, Trajectory = t, End = end, Deadline = end + (slack > 0 ? slack : 1.0), NextFeedback = time, Tolerances = tolerances
            };
        }

        private void Reject(string id, int code, string why)
        {
            _ros.ActionResult(id, TrajectoryAction, Result(code, why), StatusAborted);
            GoalResults.Add((id, StatusAborted, code));
        }

        private void Finish(int status, int code, string why)
        {
            if (_goal == null) return;
            _ros.ActionResult(_goal.Id, TrajectoryAction, Result(code, why), status);
            GoalResults.Add((_goal.Id, status, code));
            _goal = null;
        }

        private static JsonObject Result(int code, string why) => new JsonObject { ["error_code"] = code, ["error_string"] = why };

        /// <summary>The running goal: its feedback, and its end once the trajectory's time is over.</summary>
        private void StepGoal(double time)
        {
            var g = _goal;
            if (g == null) return;
            var t = g.Trajectory;
            double s = time - t.Start;
            var last = t.Points[t.Points.Count - 1];
            var desired = Enumerable.Range(0, t.Joints.Count).Select(j => s >= last.Time ? last.Positions[j] : At(t, j, s)).ToArray();
            var actual = t.Joints.Select(j => JointPosition(j.Joint)).ToArray();
            if (time >= g.NextFeedback && _ros.Connected)
            {
                g.NextFeedback = time + 0.1;
                JsonObject Point(IEnumerable<double> q) => new JsonObject { ["positions"] = new JsonArray(q.Select(x => (JsonNode?)Math.Round(x, 6)).ToArray()) };
                _ros.ActionFeedback(g.Id, TrajectoryAction, new JsonObject
                {
                    ["header"] = new JsonObject { ["stamp"] = Stamp(time), ["frame_id"] = string.Empty },
                    ["joint_names"] = new JsonArray(t.Joints.Select(j => (JsonNode?)j.Joint.RosName).ToArray()),
                    ["desired"] = Point(desired),
                    ["actual"] = Point(actual),
                    ["error"] = Point(desired.Zip(actual, (d, a) => d - a))
                });
            }

            if (time < g.End) return;
            bool within = Enumerable.Range(0, t.Joints.Count).All(j => g.Tolerances[j] <= 0 || Math.Abs(last.Positions[j] - actual[j]) <= g.Tolerances[j]);
            if (within) Finish(StatusSucceeded, Successful, string.Empty);
            else if (time >= g.Deadline)
            {
                var off = Enumerable.Range(0, t.Joints.Count).Where(j => g.Tolerances[j] > 0 && Math.Abs(last.Positions[j] - actual[j]) > g.Tolerances[j])
                    .Select(j => $"{t.Joints[j].Joint.RosName} {actual[j] - last.Positions[j]:+0.####;-0.####}");
                Finish(StatusAborted, GoalToleranceViolated, "not at the goal in time: " + string.Join(", ", off));
            }
        }
    }
}
