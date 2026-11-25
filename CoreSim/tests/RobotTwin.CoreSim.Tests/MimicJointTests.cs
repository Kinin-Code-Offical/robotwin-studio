using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[urdf] A mimic joint follows its master (a gripper's second finger) instead of holding a target of its own.</summary>
    public class MimicJointTests
    {
        private readonly ITestOutputHelper _out;

        public MimicJointTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheSecondFingerFollowsTheFirst()
        {
            var world = new WorldDocument();
            var r = UrdfImport.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_gripper.urdf"), world);
            Assert.DoesNotContain(r.Notes, n => n.Contains("mimic"));
            var left = world.Joints.Single(j => j.Name == "finger_left_joint");
            var right = world.Joints.Single(j => j.Name == "finger_right_joint");
            Assert.Equal(left.Id, right.MimicOf);
            Assert.Null(right.MimicFactor);   // multiplier 1
            Assert.Equal(WorldJoint.DrivePosition, right.Drive);

            // Saved and opened again, the coupling stays.
            var again = WorldDocument.FromJson(world.ToJson());
            Assert.Equal(left.Id, again.Joints.Single(j => j.Name == "finger_right_joint").MimicOf);

            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            double Travel(WorldJoint j) => runner.Physics!.Scene.Joints.Single(s => s.Id == j.Id).Angle;
            Assert.True(runner.Physics!.SetJointTarget(left.Id, 0.02));   // open the left finger 20 mm
            while (runner.Time < 0.6 - 1e-6) runner.Step(0.01);
            double l = Travel(left), rr = Travel(right);
            _out.WriteLine($"sol parmak {l * 1000:F2} mm, sağ parmak {rr * 1000:F2} mm");
            Assert.InRange(l, 0.0195, 0.0205);
            Assert.InRange(rr, l - 0.0005, l + 0.0005);   // the right one follows

            // A target given to the follower is not its own: it keeps following.
            runner.Physics.SetJointTarget(right.Id, 0.0);
            runner.Physics.SetJointTarget(left.Id, 0.01);
            double until = runner.Time + 0.6;
            while (runner.Time < until - 1e-6) runner.Step(0.01);
            Assert.InRange(Travel(right), Travel(left) - 0.0005, Travel(left) + 0.0005);
            Assert.InRange(Travel(left), 0.0095, 0.0105);
        }
    }
}
