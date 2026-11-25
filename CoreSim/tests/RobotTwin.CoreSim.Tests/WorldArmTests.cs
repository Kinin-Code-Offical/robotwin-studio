using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[kin] Kinematics of arms built in a world: the chain, forward / inverse kinematics, loads, reach, and the physics.</summary>
    public class WorldArmTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public WorldArmTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static (WorldDocument World, Func<WorldObject, WorldBodyGeometry> Geometry) ServoArm()
        {
            var w = WorldExamples.ServoArm(Lib);
            return (w, new ApproximateGeometry(w, Lib).GeometryOf);
        }

        private static WorldObject Named(WorldDocument w, string name) => w.Objects.First(o => o.Name == name);

        private static double Deg(double d) => d * Math.PI / 180;

        [Fact]
        public void TheServoArmTemplateIsAChainOfTwoServos()
        {
            var (w, g) = ServoArm();
            var tip = Named(w, "Kol 2");
            var arm = WorldArm.Build(w, g, tip.Id)!;
            Assert.NotNull(arm);
            Assert.Equal(Named(w, "Taban (18 mm kontrplak)").Id, arm.BaseId);
            Assert.False(arm.Grounded);
            Assert.Equal(new[] { "Taban (18 mm kontrplak)", "Omuz servosu", "Kol 1", "Dirsek servosu", "Kol 2" }, arm.Path.Select(id => w.Find(id)!.Name));
            Assert.Equal(new[] { "Omuz servosu", "Dirsek servosu" }, arm.Axes.Select(a => a.Name));
            Assert.All(arm.Axes, a => Assert.Equal(WorldAxisKind.Servo, a.Kind));
            Assert.All(arm.Axes, a => Assert.True(Math.Abs(a.Spec.Axis.Z - 1) < 1e-9));
            // The tool point is the far end of the 80 mm link.
            var s2 = Named(w, "Dirsek servosu");
            double pivot2X = s2.Pose.X - 0.0054;
            Assert.Equal(pivot2X + 0.072, arm.Tip(new double[2]).X, 6);
            // Servo commands: 90° as built, 0–180° travel.
            Assert.Equal(90, arm.Axes[0].Display(0), 9);
            Assert.Equal(-Math.PI / 2, arm.Axes[0].Spec.Min, 9);
        }

        [Fact]
        public void ForwardKinematicsIsThePlanarTwoLinkArm()
        {
            var (w, g) = ServoArm();
            var arm = WorldArm.Build(w, g, Named(w, "Kol 2").Id)!;
            var p1 = arm.Axes[0].Spec.Point;
            var p2 = arm.Axes[1].Spec.Point;
            var t0 = arm.Tip(new double[2]);
            double a = Deg(30), b = Deg(-45);
            // By hand: the elbow turns about the shoulder, the tip about both.
            Vec3 Rot(Vec3 v, double angle) => new Vec3(v.X * Math.Cos(angle) - v.Y * Math.Sin(angle), v.X * Math.Sin(angle) + v.Y * Math.Cos(angle), v.Z);
            var elbow = p1 + Rot(p2 - p1, a);
            var want = elbow + Rot(t0 - p2, a + b);
            var got = arm.Tip(new[] { a, b });
            _out.WriteLine($"tip {got} want {want}");
            Assert.True((got - want).Length < 1e-9);
        }

        [Fact]
        public void InverseKinematicsFindsTheAnglesOfAnyReachablePoint()
        {
            var (w, g) = ServoArm();
            var arm = WorldArm.Build(w, g, Named(w, "Kol 2").Id)!;
            var rng = new Random(3);
            for (int k = 0; k < 25; k++)
            {
                var q = new[] { Deg(-80 + 160 * rng.NextDouble()), Deg(-80 + 160 * rng.NextDouble()) };
                var target = arm.Tip(q);
                var s = arm.Solve(target, new double[2]);
                Assert.True(s.Success, $"pose {k}: {s.Message}");
                Assert.True(s.Error < 1e-4);
                Assert.True(arm.Model.WithinLimits(s.Q));
            }
        }

        [Fact]
        public void APointOffTheArmsPlaneGivesTheNearestOne()
        {
            var (w, g) = ServoArm();
            var arm = WorldArm.Build(w, g, Named(w, "Kol 2").Id)!;
            var on = arm.Tip(new[] { Deg(20), Deg(30) });
            var s = arm.Solve(on + new Vec3(0, 0, 0.02), new double[2]);
            _out.WriteLine(s.Message);
            // The tip goes to the target's foot on its plane, and says so.
            Assert.True(s.Success);
            Assert.InRange(s.OffPlane, 0.0199, 0.0201);
            Assert.InRange(s.Error, 0.0199, 0.0201);
            Assert.Contains("düzlem", s.Message);
            Assert.True((s.Reached - on).Length < 1e-4);
            Assert.True(arm.Reach(new double[2]).Planar);
            // A target inside the elbow's reach on the plane cannot be reached: the nearest point, and why.
            var p1 = arm.Axes[0].Spec.Point;
            var inner = arm.Solve(new Vec3(p1.X + 0.06, p1.Y, on.Z), new double[2]);
            _out.WriteLine(inner.Message);
            Assert.False(inner.Success);
            Assert.Contains("eksenin sınırı", inner.Message);   // a servo's, or a motorised joint's
            Assert.DoesNotContain("düzlem", inner.Message);
        }

        [Fact]
        public void ReachIsTheAnnulusOfTheTwoLinks()
        {
            var (w, g) = ServoArm();
            var arm = WorldArm.Build(w, g, Named(w, "Kol 2").Id)!;
            var p1 = arm.Axes[0].Spec.Point;
            var p2 = arm.Axes[1].Spec.Point;
            var t0 = arm.Tip(new double[2]);
            // Lengths across the (vertical) axes: the elbow sits 34 mm above the shoulder.
            Vec3 Across(Vec3 v) => new Vec3(v.X, v.Y, 0);
            double l1 = Across(p2 - p1).Length, l2 = Across(t0 - p2).Length;
            var reach = arm.Reach(new double[2], 4096);
            _out.WriteLine($"links {l1 * 1000:F1} + {l2 * 1000:F1} mm, reach {reach.Nearest * 1000:F1}–{reach.Farthest * 1000:F1} mm");
            Assert.Equal(l1 + l2, reach.Farthest, 4);
            // The elbow turns ±90°: the nearest the tip gets is √(l1² + l2²).
            Assert.Equal(Math.Sqrt(l1 * l1 + l2 * l2), reach.Nearest, 3);
        }

        [Fact]
        public void DexterityFallsToZeroWhenTheArmIsStretched()
        {
            var (w, g) = ServoArm();
            var arm = WorldArm.Build(w, g, Named(w, "Kol 2").Id)!;
            double straight = arm.Dexterity(new[] { Deg(10), 0.0 });
            double half = arm.Dexterity(new[] { Deg(10), Deg(45) });
            double bent = arm.Dexterity(new[] { Deg(10), Deg(90) });
            _out.WriteLine($"stretched {straight:F3}, elbow 45° {half:F3}, elbow 90° {bent:F3}");
            Assert.True(straight < 1e-6);
            // Elbow at 90°: the two servos push the tip at the angle the links make at the tip, sin = l1 / √(l1² + l2²).
            Assert.InRange(bent, 0.7, 0.8);
            Assert.InRange(half, 0.2, bent);
        }

        [Fact]
        public void TheChainFromTheOtherEndIsTheInverseMotion()
        {
            var (w, g) = ServoArm();
            var table = Named(w, "Taban (18 mm kontrplak)");
            var link = Named(w, "Kol 2");
            var fwd = WorldArm.Build(w, g, link.Id, table.Id)!;
            var rev = WorldArm.Build(w, g, table.Id, link.Id, new Vec3(0.07, 0.05, 0))!;
            Assert.Equal(new[] { "Dirsek servosu", "Omuz servosu" }, rev.Axes.Select(a => a.Name));
            // The same physics values: shoulder +25°, elbow −40°. From the link's side the table moves the inverse way.
            var qf = new[] { Deg(25), Deg(-40) };
            var qr = new[] { Deg(-40), Deg(25) };
            var motion = ArmKinematics.LinkTransforms(fwd.Model, qf)[1];
            var corner = table.Pose.ToWorld(new Vec3(0.07, 0.05, 0));
            var want = motion.Inverse().Apply(corner);
            var got = rev.Tip(qr);
            Assert.True((got - want).Length < 1e-9, $"{got} vs {want}");
        }

        [Fact]
        public void AServoOnItsSideCarriesTheArmsWeight()
        {
            var w = new WorldDocument();
            var servo = w.AddComponent(Lib, "servo-sg90", new WorldPose { Z = 0.1, Roll = 90 });
            servo.IsStatic = true;
            var pivot = servo.Pose.ToWorld(new Vec3(-0.0054, 0, 0.031));
            var link = w.AddBody("box", 0.10, 0.012, 0.004, new WorldPose { X = pivot.X + 0.05, Y = pivot.Y - 0.006, Z = pivot.Z - 0.002 });
            link.MassKg = 0.02;
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = link.Id, ObjectB = servo.Id, PortB = "horn" });
            var arm = WorldArm.Build(w, new ApproximateGeometry(w, Lib).GeometryOf, link.Id)!;
            Assert.True(arm.Grounded);
            Assert.Equal(servo.Id, arm.BaseId);
            var load = arm.GravityLoad(new double[1]);
            // 20 g whose centre is 50 mm out along a horizontal axis: 0.02 × 9.81 × 0.05 = 9.8 mN·m.
            var com = link.Pose.ToWorld(new Vec3(0, 0, 0.002));
            double armLength = Math.Abs(com.X - pivot.X);
            _out.WriteLine($"load {load[0] * 1000:F2} mN·m, lever {armLength * 1000:F1} mm, stall {arm.Axes[0].StallTorque:F2} N·m");
            Assert.Equal(0.02 * 9.81 * armLength, Math.Abs(load[0]), 6);
            // Turned straight down the arm carries nothing.
            var down = arm.GravityLoad(new[] { Math.Sign(load[0]) * Math.PI / 2 });
            Assert.True(Math.Abs(down[0]) < 1e-6 * Math.Abs(load[0]) + 1e-9 || Math.Abs(arm.GravityLoad(new[] { -Math.Sign(load[0]) * Math.PI / 2 })[0]) < 1e-9);
        }

        [Fact]
        public void ThePhysicsArmGoesWhereTheKinematicsSays()
        {
            var (w, g) = ServoArm();
            var arm = WorldArm.Build(w, g, Named(w, "Kol 2").Id)!;
            var p = WorldPhysics.Build(w, g, null);
            var target = arm.Tip(new[] { Deg(35), Deg(-60) });
            var s = arm.Solve(target, new double[2]);
            Assert.True(s.Success, s.Message);
            // No circuit here: the horns are driven to the solved angles directly, stiff as a powered SG90.
            var want = arm.Axes.Select((a, i) => (a.ObjectId, s.Q[i])).ToDictionary(x => x.ObjectId, x => x.Item2);
            p.DriveOverride = (id, j) =>
            {
                if (!want.TryGetValue(id, out double angle)) return false;
                j.Drive = JointDrive.Position;
                j.TargetAngle = angle;
                j.Stiffness = 5;
                j.Damping = 0.02;
                j.MaxTorque = 0.18;
                return true;
            };

            for (int i = 0; i < 1500; i++) p.Advance(1e-3, null);
            var q = arm.Measure(p);
            var measured = arm.MeasuredTip(p)!.Value;
            var predicted = arm.PredictedTip(p);
            var onBase = arm.BaseMotion(p).Apply(target);
            _out.WriteLine($"axes {q[0] * 180 / Math.PI:F2}°, {q[1] * 180 / Math.PI:F2}° (wanted {s.Q[0] * 180 / Math.PI:F2}°, {s.Q[1] * 180 / Math.PI:F2}°)");
            _out.WriteLine($"tip: physics {measured}, model {predicted}, target {onBase}: model error {(measured - predicted).Length * 1000:F3} mm, target error {(measured - onBase).Length * 1000:F3} mm");
            Assert.True(Math.Abs(q[0] - s.Q[0]) < Deg(1) && Math.Abs(q[1] - s.Q[1]) < Deg(1));
            Assert.True((measured - predicted).Length < 0.5e-3, "the model and the physics agree on the tip");
            Assert.True((measured - onBase).Length < 2e-3, "the arm reaches the target");
        }

        [Fact]
        public void AHeldServoFollowsThePanelInsteadOfTheFirmware()
        {
            var (w, g) = ServoArm();
            using var r = new WorldRunner(w, new RunnerOptions { Firmware = false });
            var shoulder = Named(w, "Omuz servosu");
            r.Physics!.HoldServo(shoulder.Id, 135);
            r.Step(1.0);
            double angle = WorldArmAxis.ServoDegrees(r.Physics.PortAngle(shoulder.Id));
            _out.WriteLine($"held at 135°: horn at {angle:F1}°");
            Assert.InRange(angle, 131, 139);
            Assert.Contains(shoulder.Id, r.Physics.HeldServos);
            r.Physics.HoldServo(shoulder.Id, null);
            Assert.Empty(r.Physics.HeldServos);
        }
    }
}
