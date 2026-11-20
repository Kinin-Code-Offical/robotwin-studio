using System;
using System.Diagnostics;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// The general multi-body engine against closed-form results: integrator, contacts with
    /// Coulomb friction / restitution / rolling resistance, joints, drives, breakage, air.
    /// </summary>
    public class PhysicsSceneTests
    {
        private const double G = 9.81;
        private readonly ITestOutputHelper _out;

        public PhysicsSceneTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static void Run(PhysicsScene s, double seconds, double dt = 1e-3, Action? each = null)
        {
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++)
            {
                s.Step(dt);
                each?.Invoke();
            }
        }

        private static void Rel(double expected, double actual, double tol, string what) =>
            Assert.True(Math.Abs(actual - expected) <= tol * Math.Abs(expected), $"{what}: expected {expected:G6}, got {actual:G6} ({(actual - expected) / expected:P2})");

        // ================================================================== integrator

        [Fact]
        public void FreeFlightFollowsTheBallisticParabola()
        {
            var s = new PhysicsScene { GroundEnabled = false, AirDensity = 0 };
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.1), 1.0));
            var v0 = new Vec3(3, -1, 5);
            b.Velocity = v0;
            Run(s, 1.0);
            var expected = v0 * 1.0 + new Vec3(0, 0, -G) * 0.5;
            // Symplectic Euler: position error h·g·t/2 (h = substep).
            Assert.True((b.Position - expected).Length < s.MaxSubstep * G, $"{b.Position} vs {expected}");
            Assert.True((b.Velocity - (v0 + new Vec3(0, 0, -G))).Length < 1e-9);
        }

        [Fact]
        public void TorqueFreeAsymmetricBodyKeepsAngularMomentumAndEnergy()
        {
            var s = new PhysicsScene { GroundEnabled = false, Gravity = Vec3.Zero, AirDensity = 0 };
            var b = s.AddBody(SceneBody.Box("brick", new Vec3(0.3, 0.15, 0.05), 2.0));
            // Spin mostly about the (stable) major axis with a wobble.
            b.AngularVelocity = new Vec3(0.3, 0.2, 12);
            var l0 = b.SpinMomentum;
            double e0 = b.KineticEnergy;
            Run(s, 5.0);
            double dl = (b.SpinMomentum - l0).Length / l0.Length;
            double de = Math.Abs(b.KineticEnergy - e0) / e0;
            _out.WriteLine($"|ΔL|/L = {dl:E2}, ΔE/E = {de:E2}");
            Assert.True(dl < 2e-3, $"angular momentum drift {dl:P3}");
            Assert.True(de < 5e-3, $"energy drift {de:P3}");
        }

        // ================================================================== contacts

        [Fact]
        public void RestingBoxNeitherJittersNorSinks()
        {
            var s = new PhysicsScene { GroundMaterial = "wood" };
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.2, 0.1, 0.05), 0.5, new Vec3(0, 0, 0.03), "pla"));
            Run(s, 1.0);
            double z0 = b.Position.Z;
            double maxV = 0, maxW = 0, minZ = double.MaxValue, maxZ = double.MinValue;
            Run(s, 4.0, each: () =>
            {
                maxV = Math.Max(maxV, b.Velocity.Length);
                maxW = Math.Max(maxW, b.AngularVelocity.Length);
                minZ = Math.Min(minZ, b.Position.Z);
                maxZ = Math.Max(maxZ, b.Position.Z);
            });
            _out.WriteLine($"z {z0:F6} range {(maxZ - minZ) * 1e6:F2} µm, vmax {maxV:E2}, wmax {maxW:E2}");
            Assert.InRange(z0, 0.025 - s.LinearSlop - 2e-4, 0.025 + 1e-5);
            Assert.True(maxZ - minZ < 2e-5, "no sinking or bouncing");
            Assert.True(maxV < 2e-4 && maxW < 2e-3, "no jitter");
        }

        [Fact]
        public void BoxStaysOnInclineBelowTheStaticFrictionAngle()
        {
            // wood on wood: μs = 0.50, μk = 0.30. tan 20° = 0.36 < μs.
            double angle = 20 * Math.PI / 180;
            var s = new PhysicsScene { Terrain = PlaneTerrain.Incline(angle), GroundMaterial = "wood" };
            var q = Quat.FromAxisAngle(Vec3.UnitY, -angle);
            // Start in contact: a box dropped even 0.1 mm picks up tangential speed, and with μk < tanθ it would keep sliding.
            var start = q.Rotate(new Vec3(0, 0, 0.0249));
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.05), 0.3, start, "wood"));
            b.Orientation = q;
            Run(s, 0.3);
            var p0 = b.Position;
            Run(s, 2.0);
            double moved = (b.Position - p0).Length;
            _out.WriteLine($"moved {moved * 1000:F3} mm");
            Assert.True(moved < 1e-3, $"crept {moved * 1000:F2} mm");
        }

        [Fact]
        public void BoxSlidesDownSteepInclineWithKineticFriction()
        {
            // tan 30° = 0.577 > μs = 0.5 → slides with a = g(sinθ − μk cosθ).
            double angle = 30 * Math.PI / 180;
            var s = new PhysicsScene { Terrain = PlaneTerrain.Incline(angle), GroundMaterial = "wood" };
            var q = Quat.FromAxisAngle(Vec3.UnitY, -angle);
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.05), 0.3, q.Rotate(new Vec3(0, 0, 0.0249)), "wood"));
            b.Orientation = q;
            Run(s, 0.2);
            var v0 = b.Velocity.Length;
            Run(s, 0.5);
            double a = (b.Velocity.Length - v0) / 0.5;
            var pair = Materials.Pair("wood", "wood");
            double expected = G * (Math.Sin(angle) - pair.KineticFriction * Math.Cos(angle));
            Rel(expected, a, 0.02, "acceleration down the slope");
        }

        [Fact]
        public void SlidingBoxStopsAfterTheCoulombDistance()
        {
            var s = new PhysicsScene { GroundMaterial = "concrete" };
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.04), 0.5, new Vec3(0, 0, 0.0198), "steel"));
            Run(s, 0.1);
            b.Velocity = new Vec3(1.5, 0, 0);
            double x0 = b.Position.X;
            Run(s, 1.5);
            double mu = Materials.Pair("steel", "concrete").KineticFriction;
            Rel(1.5 * 1.5 / (2 * mu * G), b.Position.X - x0, 0.02, "stopping distance");
            Assert.True(b.Velocity.Length < 1e-3);
        }

        [Fact]
        public void BallBouncesToRestitutionSquaredHeight()
        {
            var s = new PhysicsScene { GroundMaterial = "steel", AirDensity = 0 };
            double r = 0.02, h0 = 0.5;
            var b = s.AddBody(SceneBody.Sphere("ball", r, 0.033, new Vec3(0, 0, h0 + r), "steel"));
            double e = Materials.Pair("steel", "steel").Restitution;
            bool bounced = false;
            double apex = 0;
            Run(s, 1.2, dt: 2.5e-4, each: () =>
            {
                if (b.Velocity.Z > 0.1) bounced = true;
                if (bounced) apex = Math.Max(apex, b.Position.Z - r);
            });
            _out.WriteLine($"e = {e}, apex {apex:F4} m (expected {e * e * h0:F4})");
            Rel(e * e * h0, apex, 0.05, "rebound height");
        }

        [Fact]
        public void SlidingBallEndsRollingAtFiveSeventhsOfItsSpeed()
        {
            // Angular momentum about the contact point is conserved: v_final = 5/7·v0 for a solid sphere.
            var s = new PhysicsScene { GroundMaterial = "rubber", AirDensity = 0 };
            double r = 0.03;
            var b = s.AddBody(SceneBody.Sphere("ball", r, 0.1, new Vec3(0, 0, r - 0.0002), "rubber"));
            Run(s, 0.05);
            b.Velocity = new Vec3(2, 0, 0);
            b.AngularVelocity = Vec3.Zero;
            Run(s, 0.4, dt: 2.5e-4);
            double crr = Materials.Pair("rubber", "rubber").RollingResistance;
            // Rolling resistance continues to slow it a little after the slip has ended.
            double v = b.Velocity.X;
            _out.WriteLine($"v = {v:F4} (5/7·v0 = {2 * 5.0 / 7:F4}), ω·r = {b.AngularVelocity.Y * r:F4}, Crr {crr}");
            Assert.InRange(v, 2 * 5.0 / 7 - 0.05, 2 * 5.0 / 7 + 0.01);
            Assert.True(Math.Abs(b.AngularVelocity.Y * r - v) < 0.01, "rolling without slip");
        }

        [Fact]
        public void RollingBallDeceleratesByRollingResistance()
        {
            // Pure rolling of a solid sphere with rolling torque C_rr·N·r: a = C_rr·g / (1 + 2/5).
            var s = new PhysicsScene { GroundMaterial = "carpet", AirDensity = 0 };
            double r = 0.03;
            var b = s.AddBody(SceneBody.Sphere("ball", r, 0.1, new Vec3(0, 0, r - 0.0002), "rubber"));
            Run(s, 0.05);
            b.Velocity = new Vec3(1.0, 0, 0);
            b.AngularVelocity = new Vec3(0, 1.0 / r, 0);
            Run(s, 0.1);
            double v1 = b.Velocity.X;
            Run(s, 0.5);
            double a = (v1 - b.Velocity.X) / 0.5;
            double crr = Materials.Pair("rubber", "carpet").RollingResistance;
            Rel(crr * G / 1.4, a, 0.05, "rolling deceleration");
        }

        [Fact]
        public void CollisionConservesMomentumAndAppliesRestitution()
        {
            var s = new PhysicsScene { GroundEnabled = false, Gravity = Vec3.Zero, AirDensity = 0 };
            var a = s.AddBody(SceneBody.Sphere("a", 0.03, 0.2, new Vec3(-0.2, 0, 0), "steel"));
            var c = s.AddBody(SceneBody.Sphere("c", 0.04, 0.5, new Vec3(0.2, 0.01, 0), "steel"));
            a.Velocity = new Vec3(2, 0, 0);
            c.Velocity = new Vec3(-0.5, 0, 0);
            var p0 = a.LinearMomentum + c.LinearMomentum;
            var n = Vec3.Zero;
            double vrel0 = 0;
            for (int i = 0; i < 400; i++)
            {
                var d = (c.Position - a.Position).Normalized();
                double rel = (a.Velocity - c.Velocity).Dot(d);
                if (rel > 0)
                {
                    n = d;
                    vrel0 = rel;
                }

                s.Step(1e-3);
            }

            var p1 = a.LinearMomentum + c.LinearMomentum;
            Assert.True((p1 - p0).Length < 1e-9, $"momentum {p0} → {p1}");
            double e = Materials.Pair("steel", "steel").Restitution;
            double vrel1 = (c.Velocity - a.Velocity).Dot(n);
            Rel(e * vrel0, vrel1, 0.02, "separation speed = e × approach speed");
        }

        [Fact]
        public void StackedBoxesRestStably()
        {
            var s = new PhysicsScene { GroundMaterial = "wood" };
            var boxes = Enumerable.Range(0, 6)
                .Select(i => s.AddBody(SceneBody.Box("b" + i, new Vec3(0.1, 0.1, 0.1), 0.2, new Vec3(0.002 * (i % 2), 0, 0.05 + 0.1 * i + 0.0005 * i), "wood")))
                .ToList();
            Run(s, 3.0);
            var top = boxes.Last();
            double maxV = boxes.Max(b => b.Velocity.Length);
            double tilt = boxes.Max(b => Math.Acos(Math.Min(1, b.Orientation.Rotate(Vec3.UnitZ).Z)));
            _out.WriteLine($"top {top.Position}, vmax {maxV:E2}, tilt {tilt:E2}, contacts {s.ContactCount}");
            Assert.InRange(top.Position.Z, 0.55 - 6 * s.LinearSlop - 1e-3, 0.551);
            // Alternating 2 mm offsets make every interface asymmetric; the stack must still stand.
            Assert.True(Math.Abs(top.Position.X - 0.002) < 0.003 && Math.Abs(top.Position.Y) < 0.002, "stack did not drift");
            Assert.True(maxV < 2e-3 && tilt < 1e-2, "stack at rest");
        }

        [Fact]
        public void CylinderRollsDownSlopeAtTwoThirdsOfFrictionlessAcceleration()
        {
            // Solid cylinder rolling without slip: a = g·sinθ / (1 + I/(m r²)) = (2/3)·g·sinθ.
            double angle = 10 * Math.PI / 180;
            var s = new PhysicsScene { Terrain = PlaneTerrain.Incline(angle), GroundMaterial = "rubber", AirDensity = 0 };
            double r = 0.04;
            var tilt = Quat.FromAxisAngle(Vec3.UnitY, -angle);
            var axisToY = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
            var b = s.AddBody(SceneBody.Cylinder("roller", r, 0.05, 0.4, tilt.Rotate(new Vec3(0, 0, r - 0.0002)), "steel"));
            b.Orientation = tilt * axisToY;
            Run(s, 0.2);
            double v0 = b.Velocity.Length;
            Run(s, 0.5);
            double a = (b.Velocity.Length - v0) / 0.5;
            double crr = Materials.Pair("steel", "rubber").RollingResistance;
            double expected = (G * Math.Sin(angle) - crr * G * Math.Cos(angle)) / 1.5;
            Rel(expected, a, 0.03, "rolling acceleration");
        }

        [Fact]
        public void SphereRestsOnConcaveStaticMesh()
        {
            // A static triangle mesh (BVH) with a step: the ball lands on the upper level.
            var s = new PhysicsScene { GroundEnabled = true };
            var step = MeshModel.CreateBox(new Vec3(0.4, 0.4, 0.1)).Translate(new Vec3(0, 0, 0.05));
            s.AddBody(SceneBody.StaticMesh("step", step, "wood"));
            var ball = s.AddBody(SceneBody.Sphere("ball", 0.02, 0.05, new Vec3(0.05, 0.03, 0.3), "rubber"));
            Run(s, 1.5);
            _out.WriteLine($"ball {ball.Position}");
            Assert.InRange(ball.Position.Z, 0.12 - 0.001, 0.12 + 0.0005);
            Assert.True(ball.Velocity.Length < 0.01);
        }

        // ================================================================== joints

        [Fact]
        public void HingePendulumHasThePhysicalPendulumPeriodAndKeepsItsEnergy()
        {
            var s = new PhysicsScene { GroundEnabled = false, AirDensity = 0 };
            double len = 0.3, t = 0.01, m = 0.2;
            var rod = s.AddBody(SceneBody.Box("rod", new Vec3(len, t, t), m, new Vec3(len / 2, 0, 1.0)));
            // Hanging from the pivot, released 5° off vertical.
            double amp = 5 * Math.PI / 180;
            var hang = Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2 - amp);
            rod.Orientation = hang;
            rod.Position = new Vec3(0, 0, 1.0) + hang.Rotate(new Vec3(len / 2, 0, 0));
            var hinge = s.AddJoint(JointKind.Hinge, rod, null, new Vec3(0, 0, 1.0), Vec3.UnitY, "pivot");
            double inertia = m * (len * len + t * t) / 12 + m * len * len / 4;
            double period = 2 * Math.PI * Math.Sqrt(inertia / (m * G * len / 2)) * (1 + amp * amp / 16);
            double e0 = s.KineticEnergy() + s.PotentialEnergy();
            double prev = hinge.Angle, firstCross = -1, lastCross = -1;
            int crossings = 0;
            Run(s, 10 * period, dt: 1e-3, each: () =>
            {
                double a = hinge.Angle - amp; // oscillates about +amp (hanging)
                if (prev - amp < 0 && a >= 0)
                {
                    if (firstCross < 0) firstCross = s.Time;
                    lastCross = s.Time;
                    crossings++;
                }

                prev = hinge.Angle;
            });
            double measured = (lastCross - firstCross) / (crossings - 1);
            double e1 = s.KineticEnergy() + s.PotentialEnergy();
            _out.WriteLine($"T = {measured:F5} s (analytic {period:F5}), energy drift {(e1 - e0) / (m * G * len / 2 * amp * amp / 2):P2} of the swing energy");
            Rel(period, measured, 0.005, "period");
            Assert.True(Math.Abs(e1 - e0) < 0.03 * (m * G * len / 2 * (1 - Math.Cos(amp))), "energy conserved within 3 % of the swing energy");
            Assert.True(hinge.WorldAnchor.Z > 1.0 - 1e-4 && Math.Abs(hinge.WorldAnchor.X) < 1e-4, "pivot stays put");
        }

        [Fact]
        public void PositionDriveHoldsArmAgainstGravityWithTheProportionalSag()
        {
            // Steady state of a PD drive: Kp·θ = m·g·(L/2)·cos θ.
            var s = new PhysicsScene { GroundEnabled = false, AirDensity = 0 };
            var basePlate = s.AddBody(SceneBody.Box("base", new Vec3(0.05, 0.05, 0.05), 1.0, new Vec3(0, 0, 0.5)));
            basePlate.IsStatic = true;
            double len = 0.1, m = 0.05, kp = 1.0;
            var arm = s.AddBody(SceneBody.Box("arm", new Vec3(len, 0.01, 0.01), m, new Vec3(0.025 + len / 2, 0, 0.5)));
            var j = s.AddJoint(JointKind.Hinge, basePlate, arm, new Vec3(0.025, 0, 0.5), Vec3.UnitY, "shoulder");
            j.Drive = JointDrive.Position;
            j.TargetAngle = 0;
            j.Stiffness = kp;
            j.Damping = 0.02;
            Run(s, 3.0);
            double theta = 0;
            for (int i = 0; i < 20; i++) theta = m * G * len / 2 * Math.Cos(theta) / kp;
            _out.WriteLine($"sag {j.Angle * 180 / Math.PI:F3}° (analytic {theta * 180 / Math.PI:F3}°), drive {j.DriveTorque:F4} N·m");
            Rel(theta, j.Angle, 0.01, "sag");
            Rel(m * G * len / 2 * Math.Cos(theta), -j.DriveTorque, 0.01, "holding torque");
        }

        [Fact]
        public void Sg90ServoMovesAtDatasheetSpeedAndDrawsStallCurrentWhenBlocked()
        {
            var s = new PhysicsScene { GroundEnabled = false, AirDensity = 0, Gravity = Vec3.Zero };
            var mount = s.AddBody(SceneBody.Box("base", new Vec3(0.05, 0.05, 0.05), 1.0, new Vec3(0, 0, 0.5)));
            mount.IsStatic = true;
            var horn = s.AddBody(SceneBody.Box("horn", new Vec3(0.03, 0.005, 0.003), 0.002, new Vec3(0.04, 0, 0.5)));
            var j = s.AddJoint(JointKind.Hinge, mount, horn, new Vec3(0.025, 0, 0.5), Vec3.UnitZ, "servo");
            var servo = ServoMotor.ForModel("SG90");
            servo.SupplyVoltage = servo.RatedVoltage;
            servo.CommandAngle = 120 * Math.PI / 180;
            double t60 = -1;
            for (int i = 0; i < 400; i++)
            {
                servo.Drive(j);
                s.Step(1e-3);
                servo.UpdateCurrent(j);
                if (t60 < 0 && j.Angle >= 70 * Math.PI / 180) t60 = s.Time;
            }

            // From 10° to 70° at the datasheet speed (the first 10° include the spin-up).
            double t10 = 0;
            _out.WriteLine($"0→70°: {t60:F3} s, moving current at speed {servo.SupplyCurrent:F3} A");
            Assert.True(t60 > 0);
            Assert.InRange(t60 - t10, 0.98 * 0.1 * 70 / 60, 0.1 * 70 / 60 * 1.25);
            // It stops where the remaining drive torque no longer beats the gear friction.
            double stopBand = Math.Max(servo.DeadBand, servo.ProportionalBand * servo.FrictionTorque / servo.StallTorque);
            Assert.InRange(j.Angle, servo.CommandAngle - stopBand - 2e-3, servo.CommandAngle + stopBand + 2e-3);

            // Block the horn: stall current.
            j.Upper = j.Angle + 0.01;
            servo.CommandAngle = j.Angle + 1.0;
            for (int i = 0; i < 100; i++)
            {
                servo.Drive(j);
                s.Step(1e-3);
                servo.UpdateCurrent(j);
            }

            _out.WriteLine($"stalled: motor torque {servo.MotorTorque:F4} N·m (joint drive {j.DriveTorque:F4}), current {servo.SupplyCurrent:F3} A, speed {j.Speed:E2}");
            Rel(servo.StallTorque, servo.MotorTorque, 0.01, "stall torque (torque–speed line at the blocked shaft)");
            Rel(servo.StallTorque, j.DriveTorque, 0.05, "torque the joint receives");
            Rel(servo.StallCurrent, servo.SupplyCurrent, 0.01, "stall current");
        }

        [Fact]
        public void SliderMotorStopsAtTheLimit()
        {
            var s = new PhysicsScene { GroundEnabled = false, Gravity = Vec3.Zero, AirDensity = 0 };
            var rail = s.AddBody(SceneBody.Box("rail", new Vec3(0.3, 0.02, 0.02), 1.0));
            rail.IsStatic = true;
            var carriage = s.AddBody(SceneBody.Box("car", new Vec3(0.04, 0.04, 0.02), 0.1, new Vec3(0, 0, 0.02)));
            var j = s.AddJoint(JointKind.Slider, rail, carriage, new Vec3(0, 0, 0.02), Vec3.UnitX, "slide");
            j.Upper = 0.1;
            j.Drive = JointDrive.Velocity;
            j.TargetSpeed = 0.2;
            j.MaxTorque = 5;
            Run(s, 0.25);
            Rel(0.05, j.Position, 0.02, "position after 0.25 s at 0.2 m/s");
            Run(s, 1.0);
            Assert.InRange(j.Position, 0.0995, 0.1005);
            Assert.True(Math.Abs(carriage.Position.Y) < 1e-6 && Math.Abs(carriage.Position.Z - 0.02) < 1e-6, "stays on the axis");
        }

        [Fact]
        public void FixedJointWeldsTwoFallingBodies()
        {
            var s = new PhysicsScene { GroundEnabled = false, AirDensity = 0 };
            var a = s.AddBody(SceneBody.Box("a", new Vec3(0.1, 0.1, 0.1), 1.0, new Vec3(0, 0, 1)));
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.1), 0.3, new Vec3(0.1, 0, 1)));
            a.AngularVelocity = new Vec3(0, 0, 2);
            s.AddJoint(JointKind.Fixed, a, b, new Vec3(0.05, 0, 1), Vec3.UnitZ, "weld");
            var rel0 = a.Orientation.InverseRotate(b.Position - a.Position);
            Run(s, 1.0);
            var rel1 = a.Orientation.InverseRotate(b.Position - a.Position);
            Assert.True((rel1 - rel0).Length < 1e-4, $"{rel0} → {rel1}");
            var qrel = a.Orientation.Conjugate * b.Orientation;
            Assert.True(Math.Abs(Math.Abs(qrel.W) - 1) < 1e-6);
        }

        [Fact]
        public void JointBreaksAboveItsBreakForceAndRaisesAnIncident()
        {
            foreach (double limit in new[] { 8.0, 12.0 })
            {
                var s = new PhysicsScene { GroundEnabled = false, AirDensity = 0 };
                var weight = s.AddBody(SceneBody.Box("weight", new Vec3(0.05, 0.05, 0.05), 1.0, new Vec3(0, 0, 0.5)));
                var j = s.AddJoint(JointKind.Ball, weight, null, new Vec3(0, 0, 0.525), Vec3.UnitZ, "hook");
                j.BreakForce = limit;
                Run(s, 0.5);
                var events = s.DrainEvents();
                _out.WriteLine($"limit {limit} N: load {j.ReactionLoad:F2} N, broken {j.Broken}");
                if (limit < G)
                {
                    Assert.True(j.Broken);
                    Assert.Contains(events, e => e.Kind == PhysicsEventKind.JointBroken && e.Source == "hook");
                    Assert.True(weight.Position.Z < 0.0, "falls once released");
                }
                else
                {
                    Assert.False(j.Broken);
                    Rel(G, j.ReactionLoad, 0.01, "carries the weight");
                }
            }
        }

        [Fact]
        public void HingeWheeledCarAcceleratesStraightUnderConstantMotorTorque()
        {
            var s = new PhysicsScene { GroundMaterial = "steel", AirDensity = 0 };
            double r = 0.03, mw = 0.03, mc = 0.5, tau = 0.01;
            var chassis = s.AddBody(SceneBody.Box("chassis", new Vec3(0.2, 0.12, 0.04), mc, new Vec3(0, 0, r), "aluminium"));
            var axisY = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
            var wheels = new SceneJoint[4];
            int k = 0;
            foreach (double x in new[] { -0.07, 0.07 })
            {
                foreach (double y in new[] { -0.075, 0.075 })
                {
                    var w = s.AddBody(SceneBody.Cylinder($"w{k}", r, 0.02, mw, new Vec3(x, y, r), "steel"));
                    w.Orientation = axisY;
                    wheels[k++] = s.AddJoint(JointKind.Hinge, chassis, w, new Vec3(x, y, r), Vec3.UnitY, $"axle{k}");
                }
            }

            Run(s, 0.2);
            var p0 = chassis.Position;
            double v0 = chassis.Velocity.X;
            // Rear wheels driven.
            wheels[0].MotorTorque = tau;
            wheels[1].MotorTorque = tau;
            Run(s, 0.5);
            double a = (chassis.Velocity.X - v0) / 0.5;
            double iw = mw * r * r / 2, mTotal = mc + 4 * mw;
            double crr = Materials.Pair("steel", "steel").RollingResistance;
            double expected = (2 * tau / r - crr * mTotal * G) / (mTotal + 4 * iw / (r * r));
            double heading = Math.Abs(chassis.Orientation.ToEuler().Yaw);
            _out.WriteLine($"a = {a:F4} m/s² (analytic {expected:F4}), heading drift {heading * 180 / Math.PI:F3}°, lateral {chassis.Position.Y - p0.Y:E2} m");
            Rel(expected, a, 0.05, "car acceleration");
            Assert.True(heading < 0.5 * Math.PI / 180, "drives straight");
            Assert.True(Math.Abs(chassis.Position.Y - p0.Y) < 2e-3);
            Assert.True(Math.Abs(wheels[0].Speed * r - chassis.Velocity.X) < 0.02, "wheels roll without slip");
        }

        // ================================================================== air

        [Fact]
        public void FallingBallReachesAirDragTerminalVelocity()
        {
            var s = new PhysicsScene { GroundEnabled = false };
            double r = 0.02, m = 0.0027;
            var ball = s.AddBody(SceneBody.Sphere("pingpong", r, m));
            Run(s, 6.0);
            double cda = ball.DragArea.X;
            double vt = Math.Sqrt(2 * m * G / (s.AirDensity * cda));
            Rel(vt, -ball.Velocity.Z, 0.01, "terminal velocity");
        }

        [Fact]
        public void WindCarriesALightBodyUpToTheWindSpeed()
        {
            var s = new PhysicsScene { GroundEnabled = false, Gravity = Vec3.Zero, Wind = new Vec3(5, 0, 0) };
            var ball = s.AddBody(SceneBody.Sphere("balloon", 0.05, 0.005));
            s.Step(1e-3);
            double a0 = ball.Velocity.X / 1e-3;
            Rel(0.5 * s.AirDensity * ball.DragArea.X * 25 / 0.005, a0, 0.02, "initial wind acceleration");
            // dv/dt = c(w − v)²/m → v = w − 1/(1/w + c·t/m): within 1 % after 50 s.
            Run(s, 50);
            Rel(5, ball.Velocity.X, 0.015, "drifts with the wind");
        }

        // ================================================================== terrain, regions

        [Fact]
        public void BallSettlesAtTheBottomOfAHeightMapValley()
        {
            var bowl = HeightMapTerrain.FromFunction(2, 2, 0.02, (x, y) => 0.5 * (x * x + y * y));
            var s = new PhysicsScene { Terrain = bowl, GroundMaterial = "carpet" };
            double r = 0.02;
            var ball = s.AddBody(SceneBody.Sphere("ball", r, 0.05, new Vec3(0.3, 0.1, bowl.Height(0.3, 0.1) + r + 0.001), "rubber"));
            Run(s, 15);
            _out.WriteLine($"ball {ball.Position}");
            Assert.True(Math.Sqrt(ball.Position.X * ball.Position.X + ball.Position.Y * ball.Position.Y) < 0.03);
            Assert.InRange(ball.Position.Z, r - 0.001, r + 0.001);
        }

        [Fact]
        public void IcePatchLengthensTheStoppingDistance()
        {
            double Slide(string? region)
            {
                var s = new PhysicsScene { GroundMaterial = "wood" };
                if (region != null) s.Regions.Add(new SurfaceRegion(1.5, 0, 4, 1, region));
                var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.04), 0.5, new Vec3(0, 0, 0.0198), "steel"));
                Run(s, 0.05);
                b.Velocity = new Vec3(0.5, 0, 0);
                double x0 = b.Position.X;
                Run(s, 6);
                return b.Position.X - x0;
            }

            double onWood = Slide(null), onIce = Slide("ice");
            Rel(0.25 / (2 * Materials.Pair("steel", "wood").KineticFriction * G), onWood, 0.03, "on wood");
            Rel(0.25 / (2 * Materials.Pair("steel", "ice").KineticFriction * G), onIce, 0.03, "on ice");
        }

        // ================================================================== materials, determinism, speed

        [Fact]
        public void MaterialPairsAreSymmetricAndUseTabulatedValues()
        {
            Assert.True(Materials.IsTabulated("rubber", "asphalt"));
            var p = Materials.Pair("Rubber", "asfalt");
            Assert.Equal(0.9, p.StaticFriction, 9);
            Assert.Equal(0.75, p.KineticFriction, 9);
            Assert.Equal(Materials.Pair("aluminium", "steel").StaticFriction, Materials.Pair("Çelik", "Alüminyum").StaticFriction, 12);
            Assert.True(Materials.Pair("rubber", "ice").KineticFriction < 0.1);
            // Untabulated pair: geometric mean.
            var mix = Materials.Pair("pla", "carpet");
            Assert.Equal(Math.Sqrt(0.4 * 0.6), mix.StaticFriction, 9);
            Assert.Equal("plastic", Materials.Canonical("bilinmeyen"));
            foreach (var a in Materials.Names)
            foreach (var b in Materials.Names)
            {
                var ab = Materials.Pair(a, b);
                var ba = Materials.Pair(b, a);
                Assert.Equal(ab.StaticFriction, ba.StaticFriction);
                Assert.True(ab.KineticFriction <= ab.StaticFriction);
            }
        }

        private static PhysicsScene Chain(int links, double height = 0.6)
        {
            var s = new PhysicsScene { GroundMaterial = "wood" };
            SceneBody? prev = null;
            for (int i = 0; i < links; i++)
            {
                var b = s.AddBody(SceneBody.Box("l" + i, new Vec3(0.05, 0.02, 0.02), 0.05, new Vec3(0.025 + 0.05 * i, 0, height), "pla"));
                s.AddJoint(JointKind.Hinge, b, prev, new Vec3(0.05 * i, 0, height), Vec3.UnitY, "j" + i);
                prev = b;
            }

            return s;
        }

        [Fact]
        public void SimulationIsDeterministic()
        {
            var a = Chain(8);
            var b = Chain(8);
            Run(a, 1.5);
            Run(b, 1.5);
            for (int i = 0; i < a.Bodies.Count; i++)
            {
                Assert.Equal(a.Bodies[i].Position.X, b.Bodies[i].Position.X);
                Assert.Equal(a.Bodies[i].Position.Z, b.Bodies[i].Position.Z);
                Assert.Equal(a.Bodies[i].Orientation.W, b.Bodies[i].Orientation.W);
            }
        }

        [Fact]
        public void TwentyBodiesAndTwentyJointsStepFasterThanRealTime()
        {
            // A hanging 20-link chain that falls onto the floor and piles up (contacts + joints).
            var s = Chain(20, 0.3);
            Run(s, 0.05);
            int maxContacts = 0;
            var watch = Stopwatch.StartNew();
            Run(s, 1.0, each: () => maxContacts = Math.Max(maxContacts, s.ContactCount));
            double msPerMs = watch.Elapsed.TotalMilliseconds / 1000.0;
            _out.WriteLine($"{s.Bodies.Count} bodies, {s.Joints.Count} joints, up to {maxContacts} contacts: {msPerMs:F3} ms per simulated ms");
            Assert.True(msPerMs < 2.0, $"{msPerMs:F2} ms per ms");
            // The chain stays connected (joint drift small).
            foreach (var j in s.Joints.Where(j => j.B != null))
            {
                var pa = j.A.ToWorld(j.LocalAnchorA);
                var pb = j.B!.ToWorld(j.LocalAnchorB);
                Assert.True((pa - pb).Length < 2e-3, $"{j.Id} separated by {(pa - pb).Length * 1000:F2} mm");
            }
        }
    }
}
