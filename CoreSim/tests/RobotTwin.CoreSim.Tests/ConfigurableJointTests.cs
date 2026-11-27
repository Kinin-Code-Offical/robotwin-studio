using System;
using System.Text.Json;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[d6] Unity's character and configurable joints: the swing cone, the twist range, locked and limited slides, drives.</summary>
    public class ConfigurableJointTests
    {
        private const double Deg = 180 / Math.PI;
        private readonly ITestOutputHelper _out;

        public ConfigurableJointTests(ITestOutputHelper output) => _out = output;

        /// <summary>A 30 cm, 1 kg rod hanging from a world anchor at 1 m, its joint axis down along it.</summary>
        private static (PhysicsScene Scene, SceneBody Rod, SceneJoint Joint) Limb(Vec3 axis)
        {
            var scene = new PhysicsScene { GroundEnabled = false };
            var rod = SceneBody.Box("rod", new Vec3(0.02, 0.02, 0.3), 1.0, new Vec3(0, 0, 0.85));
            scene.AddBody(rod);
            var j = scene.AddJoint(JointKind.Configurable, rod, null, new Vec3(0, 0, 1), axis, "j");
            return (scene, rod, j);
        }

        /// <summary>Sets the rod turning at <paramref name="spin"/> about the anchor (its centre moving with it, as a real kick).</summary>
        private static void Kick(SceneBody rod, Vec3 spin)
        {
            rod.AngularVelocity = spin;
            rod.Velocity = spin.Cross(rod.Position - new Vec3(0, 0, 1));
        }

        private static void Character(SceneJoint j, double lowTwist, double highTwist, double swing1, double swing2)
        {
            j.AngularMotionX = j.AngularMotionY = j.AngularMotionZ = AxisMotion.Limited;
            j.Lower = lowTwist / Deg;
            j.Upper = highTwist / Deg;
            j.Swing1Limit = swing1 / Deg;
            j.Swing2Limit = swing2 / Deg;
        }

        [Fact]
        public void SwingStaysInItsCone()
        {
            // Kicked at 6 rad/s, a free rod would swing up to ~51°; the character joint's 30° cone stops it there.
            var (scene, rod, j) = Limb(-Vec3.UnitZ);
            Character(j, -20, 70, 30, 30);
            Kick(rod, new Vec3(6, 0, 0));
            double most = 0, drift = 0;
            for (int i = 0; i < 2000; i++)
            {
                scene.Step(1e-3);
                most = Math.Max(most, Math.Sqrt(j.Swing1 * j.Swing1 + j.Swing2 * j.Swing2) * Deg);
                drift = Math.Max(drift, (j.WorldAnchor - new Vec3(0, 0, 1)).Length);
            }

            _out.WriteLine($"widest swing {most:F2}°, anchor drift {drift * 1000:F3} mm");
            Assert.InRange(most, 28, 31.5);
            Assert.True(drift < 1e-3);

            var (free, freeRod, fj) = Limb(-Vec3.UnitZ);
            fj.AngularMotionX = fj.AngularMotionY = fj.AngularMotionZ = AxisMotion.Free;
            Kick(freeRod, new Vec3(6, 0, 0));
            double freeMost = 0;
            for (int i = 0; i < 600; i++)
            {
                free.Step(1e-3);
                freeMost = Math.Max(freeMost, Math.Sqrt(fj.Swing1 * fj.Swing1 + fj.Swing2 * fj.Swing2) * Deg);
            }

            Assert.InRange(freeMost, 48, 54);   // the same kick unhindered: energy up to 1 − cos θ = 0.367
        }

        [Fact]
        public void EllipticConeIsNarrowerAboutItsSecondAxis()
        {
            double Widest(Vec3 spin, bool first)
            {
                var (scene, rod, j) = Limb(-Vec3.UnitZ);
                j.SetSecondaryAxis(Vec3.UnitX);   // swing 1 about world X, swing 2 about −Y
                Character(j, -20, 70, 40, 10);
                Kick(rod, spin);
                double most = 0;
                for (int i = 0; i < 1500; i++)
                {
                    scene.Step(1e-3);
                    most = Math.Max(most, Math.Abs(first ? j.Swing1 : j.Swing2) * Deg);
                }

                return most;
            }

            double wide = Widest(new Vec3(8, 0, 0), true), narrow = Widest(new Vec3(0, 8, 0), false);
            _out.WriteLine($"swing 1 {wide:F2}° (40° limit), swing 2 {narrow:F2}° (10° limit)");
            Assert.InRange(wide, 37, 41.5);
            Assert.InRange(narrow, 8.5, 11);
        }

        [Fact]
        public void TwistKeepsItsRangeWhileLockedSwingsHold()
        {
            var (scene, rod, j) = Limb(-Vec3.UnitZ);
            j.AngularMotionX = AxisMotion.Limited;
            j.Lower = -30 / Deg;
            j.Upper = 30 / Deg;
            rod.AngularVelocity = new Vec3(0, 0, -10);   // spinning about the joint axis (down): a positive twist
            double hi = 0, swing = 0;
            for (int i = 0; i < 1000; i++)
            {
                scene.Step(1e-3);
                hi = Math.Max(hi, j.Twist * Deg);
                swing = Math.Max(swing, Math.Sqrt(j.Swing1 * j.Swing1 + j.Swing2 * j.Swing2) * Deg);
            }

            _out.WriteLine($"twist up to {hi:F2}° (30° limit), swing {swing:F3}°");
            Assert.InRange(hi, 28, 31);
            Assert.True(swing < 0.5);
        }

        [Fact]
        public void LimitedSlideStopsAtItsLimitAndFreeOneFalls()
        {
            var (scene, rod, j) = Limb(Vec3.UnitZ);
            j.MotionX = AxisMotion.Limited;
            j.LinearLimit = 0.05;
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            _out.WriteLine($"limited: offset {j.LinearOffset.X * 1000:F2} mm along, {j.LinearOffset.Y * 1000:F3} / {j.LinearOffset.Z * 1000:F3} mm across");
            Assert.InRange(j.LinearOffset.X, -0.0515, -0.0485);
            Assert.True(Math.Abs(j.LinearOffset.Y) < 1e-4 && Math.Abs(j.LinearOffset.Z) < 1e-4);

            var (free, freeRod, fj) = Limb(Vec3.UnitZ);
            fj.MotionX = AxisMotion.Free;
            freeRod.Velocity = new Vec3(0.3, 0.2, 0);   // the locked slides take the sideways push
            for (int i = 0; i < 300; i++) free.Step(1e-3);
            double fall = -fj.LinearOffset.X, expected = 0.5 * 9.81 * 0.3 * 0.3;
            _out.WriteLine($"free: fell {fall * 1000:F1} mm (free fall {expected * 1000:F1} mm), across {fj.LinearOffset.Y * 1000:F3} mm");
            Assert.InRange(fall, expected * 0.98, expected * 1.02);
            Assert.True(Math.Abs(fj.LinearOffset.Y) < 1e-4 && Math.Abs(fj.LinearOffset.Z) < 1e-4);
        }

        [Fact]
        public void DrivesPullToTheirTargets()
        {
            var (scene, rod, j) = Limb(Vec3.UnitX);
            scene.Gravity = Vec3.Zero;
            j.MotionX = AxisMotion.Free;
            j.LinearDriveStiffness = 2000;
            j.LinearDriveDamping = 90;   // about critical for 1 kg
            j.TargetOffset = new Vec3(0.02, 0, 0);
            j.AngularMotionX = AxisMotion.Free;
            j.AngularDriveStiffness = 2;
            j.AngularDriveDamping = 0.25;
            j.TargetRotation = SceneJoint.FromSwingTwist(45 / Deg, 0, 0);
            for (int i = 0; i < 3000; i++) scene.Step(1e-3);
            _out.WriteLine($"slide {j.LinearOffset.X * 1000:F3} mm (20 mm), twist {j.Twist * Deg:F2}° (45°)");
            Assert.InRange(j.LinearOffset.X, 0.0195, 0.0205);
            Assert.InRange(j.Twist * Deg, 44.5, 45.5);
        }

        [Fact]
        public void SplitAndItsInverseAgree()
        {
            var (t, s1, s2) = SceneJoint.SwingTwist(SceneJoint.FromSwingTwist(0.4, -0.3, 0.2));
            Assert.Equal(0.4, t, 9);
            Assert.Equal(-0.3, s1, 9);
            Assert.Equal(0.2, s2, 9);
        }

        [Fact]
        public void WorldJointsBecomeSixAxisJointsAndKeepTheirSettings()
        {
            var w = new WorldDocument();
            var arm = new WorldObject { Id = w.NextId("O"), Name = "Kol", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.02, SizeY = 0.02, SizeZ = 0.3, MassKg = 1, Pose = new WorldPose { Z = 1 } };
            w.Objects.Add(arm);
            var drop = new WorldObject { Id = w.NextId("O"), Name = "Ağırlık", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.05, SizeY = 0.05, SizeZ = 0.05, MassKg = 1, Pose = new WorldPose { X = 0.5, Z = 1 } };
            w.Objects.Add(drop);
            w.Joints.Add(new WorldJoint
            {
                Id = "omuz", Kind = WorldJointKind.Character, ObjectA = arm.Id, AnchorX = 0, AnchorY = 0, AnchorZ = 1.15, AxisZ = -1,
                Lower = WorldPhysics.CharacterLowTwist, Upper = WorldPhysics.CharacterHighTwist, SwingLimit1 = 25, SwingLimit2 = 25
            });
            var rail = new WorldJoint { Id = "ray", Kind = WorldJointKind.Configurable, ObjectA = drop.Id, AxisZ = 1, MotionX = AxisMotion.Free, AngularMotionX = AxisMotion.Free };
            w.Joints.Add(rail);

            var back = JsonSerializer.Deserialize<WorldJoint>(JsonSerializer.Serialize(rail, RobotWinFormats.Json), RobotWinFormats.Json)!;
            Assert.Equal(AxisMotion.Free, back.MotionX);
            Assert.Equal(AxisMotion.Locked, back.MotionY);
            Assert.Equal(AxisMotion.Free, back.AngularMotionX);

            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            p.Advance(0.3, null);
            var shoulder = p.SixAxisState("omuz");
            Assert.NotNull(shoulder);
            Assert.True(Math.Sqrt(shoulder!.Value.Swing1 * shoulder.Value.Swing1 + shoulder.Value.Swing2 * shoulder.Value.Swing2) < 0.5);   // hanging straight: it stays
            Assert.InRange(p.PoseOf(arm.Id)!.Value.Position.Z, 0.999, 1.001);
            Assert.InRange(1 - p.PoseOf(drop.Id)!.Value.Position.Z, 0.43, 0.45);   // the free slide is a vertical rail: free fall
            Assert.InRange(p.PoseOf(drop.Id)!.Value.Position.X, 0.499, 0.501);
            Assert.Null(p.SixAxisState("yok"));
        }
    }
}
