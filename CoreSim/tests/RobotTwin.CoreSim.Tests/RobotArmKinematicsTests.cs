using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    public class RobotArmKinematicsTests
    {
        private readonly ITestOutputHelper _out;

        public RobotArmKinematicsTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static double[] RandomPose(ArmModel m, Random rng, double margin = 0.05)
        {
            var q = new double[m.Dof];
            for (int i = 0; i < m.Dof; i++)
            {
                var j = m.Joints[i];
                double span = j.Max - j.Min;
                q[i] = j.Min + span * (margin + (1 - 2 * margin) * rng.NextDouble());
            }

            return q;
        }

        [Fact]
        public void ZeroPoseFlangeMatchesTheDrawing()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var f = ArmKinematics.Forward(m, new double[6]);
            Assert.Equal(0.560, f.P.X, 6);
            Assert.Equal(0.0, f.P.Y, 6);
            Assert.Equal(0.730, f.P.Z, 6);
            // Approach direction (flange Z) points forward along +X.
            Assert.Equal(1.0, f.ZAxis.X, 6);
            Assert.InRange(m.Reach, 0.84, 0.86);
        }

        [Fact]
        public void ShoulderAndElbowSignsAreAsDocumented()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            // J2 positive leans the upper arm forward: the flange moves forward and down.
            var lean = ArmKinematics.Forward(m, new[] { 0, 0.3, 0, 0, 0, 0.0 });
            Assert.True(lean.P.X > 0.56 && lean.P.Z < 0.73);
            // J3 positive drops the forearm.
            var drop = ArmKinematics.Forward(m, new[] { 0, 0, 0.3, 0, 0, 0.0 });
            Assert.True(drop.P.Z < 0.70);
        }

        [Theory]
        [InlineData(ArmCatalog.Industrial6)]
        [InlineData(ArmCatalog.Desktop6)]
        [InlineData(ArmCatalog.Scara)]
        public void AnalyticIkRoundTripsManyPoses(string id)
        {
            var m = ArmCatalog.Get(id);
            var rng = new Random(1234);
            var tool = Transform3.Translation(0, 0, 0.1);
            int ok = 0, total = 400, sameJoints = 0;
            double worstPos = 0, worstRot = 0;
            for (int k = 0; k < total; k++)
            {
                var q = RandomPose(m, rng);
                var target = ArmKinematics.Forward(m, q, tool);
                // Seed near the pose (the way a controller tracks a path) and far from it.
                var seed = q.Select(v => v + (rng.NextDouble() - 0.5) * 0.2).ToArray();
                var ik = ArmKinematics.Inverse(m, target, seed, tool);
                Assert.True(ik.Success, $"pose {k}: {ik.Message}");
                Assert.True(m.WithinLimits(ik.Joints, 1e-6));
                var got = ArmKinematics.Forward(m, ik.Joints, tool);
                worstPos = Math.Max(worstPos, (got.P - target.P).Length);
                double rot = m.Ik == ArmIkKind.Scara4 ? Math.Abs(Transform3.RotationError(got.Q, target.Q).Z) : Transform3.AngleBetween(got.Q, target.Q);
                worstRot = Math.Max(worstRot, rot);
                ok++;
                if (q.Zip(ik.Joints, (a, b) => Math.Abs(a - b)).Max() < 1e-5) sameJoints++;
            }

            _out.WriteLine($"{id}: {ok}/{total} solved, worst {worstPos * 1e6:0.###} µm / {worstRot * 180 / Math.PI * 3600:0.###}\", same branch {sameJoints}");
            Assert.True(worstPos < 1e-6);
            Assert.True(worstRot < 1e-5);
            // With a nearby seed the closest branch is the original one.
            Assert.True(sameJoints > total * 0.95, $"same branch only {sameJoints}/{total}");
        }

        [Fact]
        public void NumericIkConvergesWhereAnalyticWouldBeUsed()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var rng = new Random(7);
            for (int k = 0; k < 60; k++)
            {
                var q = RandomPose(m, rng, 0.15);
                var target = ArmKinematics.Forward(m, q);
                var seed = q.Select(v => v + (rng.NextDouble() - 0.5) * 0.3).ToArray();
                var r = ArmKinematics.SolveDls(m, target, seed, Transform3.Identity);
                Assert.True(r.Success, $"pose {k}: pos {r.PositionError * 1000:0.###} mm rot {r.OrientationError:0.####}");
            }
        }

        [Fact]
        public void UnreachableTargetIsReported()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var far = Transform3.FromRpy(1.5, 0, 0.5, Math.PI, 0, 0);
            var r = ArmKinematics.Inverse(m, far, m.Home);
            Assert.False(r.Success);
            Assert.False(string.IsNullOrEmpty(r.Message));
        }

        [Fact]
        public void WristSingularityKeepsJ4AndStillSolves()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var q = new[] { 0.2, 0.1, 0.4, 0.0, 0.0, 0.3 };
            var target = ArmKinematics.Forward(m, q);
            var seed = new[] { 0.2, 0.1, 0.4, 0.5, 0.05, 0.0 };
            var r = ArmKinematics.Inverse(m, target, seed);
            Assert.True(r.Success);
            Assert.True(r.NearSingular);
            Assert.Equal(0.5, r.Joints[3], 6);
            Assert.True((ArmKinematics.Forward(m, r.Joints).P - target.P).Length < 1e-6);
            Assert.True(ArmKinematics.Manipulability(m, q, Transform3.Identity) < ArmKinematics.Manipulability(m, m.Home, Transform3.Identity));
        }

        [Fact]
        public void JacobianMatchesFiniteDifferences()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var q = new[] { 0.3, -0.2, 0.5, 0.4, 0.7, -0.3 };
            var tcp = ArmKinematics.Forward(m, q);
            var jac = ArmKinematics.Jacobian(m, q, tcp.P);
            for (int i = 0; i < 6; i++)
            {
                var q2 = (double[])q.Clone();
                q2[i] += 1e-6;
                var d = (ArmKinematics.Forward(m, q2).P - tcp.P) / 1e-6;
                Assert.Equal(d.X, jac[0, i], 4);
                Assert.Equal(d.Y, jac[1, i], 4);
                Assert.Equal(d.Z, jac[2, i], 4);
            }
        }

        [Theory]
        [InlineData(ProfileShape.Trapezoid)]
        [InlineData(ProfileShape.SCurve)]
        public void ProfileTimingMatchesClosedForm(ProfileShape shape)
        {
            // 1 rad at 2 rad/s, 4 rad/s² (peak): trapezoid Ta = 0.5 s, T = 1/2 + 0.5 = 1.0 s.
            var p = MotionProfile.Plan(1.0, 2.0, 4.0, shape);
            double expected = shape == ProfileShape.Trapezoid ? 1.0 : 1.5;   // S-curve: Ta = 2v/a = 1 s
            if (shape == ProfileShape.SCurve)
            {
                // v·Ta = 2·1 = 2 > 1 → triangular: v = √(D·a/2) = √2, Ta = 2v/a = 0.707, T = D/v + Ta = 1.414.
                expected = 1 / Math.Sqrt(2) + 2 * Math.Sqrt(2) / 4;
            }

            Assert.Equal(expected, p.Duration, 6);
            double maxRate = 0, maxAcc = 0, prevS = 0;
            for (double t = 0; t <= p.Duration + 1e-9; t += 1e-4)
            {
                var (s, r, a) = p.Evaluate(t);
                Assert.True(s >= prevS - 1e-12);
                prevS = s;
                maxRate = Math.Max(maxRate, r);
                maxAcc = Math.Max(maxAcc, Math.Abs(a));
            }

            Assert.Equal(1.0, p.Evaluate(p.Duration).S, 9);
            Assert.True(maxRate <= 2.0 + 1e-6);
            Assert.True(maxAcc <= 4.0 + 1e-3);
        }

        [Fact]
        public void JointMoveIsSynchronisedAndWithinRatings()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var start = m.Home;
            var end = new[] { 1.2, 0.3, -0.2, 1.0, 0.4, 2.0 };
            var move = new JointMotion(m, start, end, 1.0, ProfileShape.SCurve);
            var q = new double[6];
            var qd = new double[6];
            var prev = (double[])start.Clone();
            double dt = 1e-3;
            for (double t = 0; t <= move.Duration; t += dt)
            {
                move.Sample(t, q, qd);
                for (int i = 0; i < 6; i++)
                {
                    Assert.True(Math.Abs(qd[i]) <= m.Joints[i].MaxSpeed * 1.001, $"{m.Joints[i].Name} too fast");
                }
            }

            move.Sample(move.Duration, q, qd);
            for (int i = 0; i < 6; i++) Assert.Equal(end[i], q[i], 9);
            // J6 has the largest travel but the fastest drive; the duration is the slowest axis's.
            double slowest = Enumerable.Range(0, 6).Max(i => MotionProfile.Plan(end[i] - start[i], m.Joints[i].MaxSpeed, m.Joints[i].MaxAccel, ProfileShape.SCurve).Duration);
            Assert.InRange(move.Duration, slowest - 1e-9, slowest * 1.05);
            _out.WriteLine($"MoveJ duration {move.Duration:0.###} s");
        }

        [Fact]
        public void LinearMoveKeepsTheTcpOnTheLine()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var tool = Transform3.Translation(0, 0, 0.12);
            var start = new[] { 0.0, 0.2, 0.6, 0.0, 0.8, 0.0 };
            var a = ArmKinematics.Forward(m, start, tool);
            var b = new Transform3(a.P + new Vec3(-0.15, 0.25, -0.1), a.Q);
            var move = LinearMotion.Plan(m, start, b, tool, 0.5, ProfileShape.SCurve, out var err);
            Assert.True(move != null, err);
            var q = new double[6];
            var qd = new double[6];
            double worst = 0;
            var dir = (b.P - a.P).Normalized();
            for (double t = 0; t <= move!.Duration; t += 2e-3)
            {
                move.Sample(t, q, qd);
                var p = ArmKinematics.Forward(m, q, tool).P - a.P;
                var off = p - dir * p.Dot(dir);
                worst = Math.Max(worst, off.Length);
            }

            _out.WriteLine($"MoveL {move.Duration:0.###} s, worst deviation {worst * 1e6:0.#} µm");
            Assert.True(worst < 2e-4);
            // 0.36 m at 0.5 m/s cannot take less than 0.72 s.
            Assert.True(move.Duration >= (b.P - a.P).Length / 0.5);
        }

        [Fact]
        public void LinearMoveThroughTheWristSingularityIsRefused()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            // Wrist bent down (J5 > 0), straight across so that J5 must pass through 0 with J4/J6 flipping.
            var start = new[] { 0.0, 0.0, 0.0, 1.2, 0.3, 0.0 };
            var a = ArmKinematics.Forward(m, start);
            var endQ = new[] { 0.0, 0.0, 0.0, 1.2, -0.3, 0.0 };
            var b = ArmKinematics.Forward(m, endQ);
            var move = LinearMotion.Plan(m, start, b, Transform3.Identity, 0.2, ProfileShape.Trapezoid, out var err);
            _out.WriteLine(move == null ? "refused: " + err : $"planned {move.Duration:0.###} s");
            Assert.Null(move);
            Assert.Contains("tekil", err);
        }

        [Fact]
        public void ControllerRunsAProgramWithGripAndPallet()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var c = new ArmController(m, m.Home) { Tool = Transform3.Translation(0, 0, 0.1) };
            bool holding = false;
            c.ToolHolding = () => holding;
            int full = 0;
            c.PalletFull += () => full++;
            var prog = ArmProgram.Parse(
                "PALET 2 1 1 100 0 0\nMOVEJ A HIZ=80\nMOVEL B OFS=0,0,50 HIZ=500\nMOVEL B PAL\nKAVRA\nTUTUSBEKLE 1\nBIRAK\nPALETSONRAKI\nCIKIS 1 1",
                "A = J(0, -10, 25, 0, 75, 0)\nB = C(500, 100, 300, 180, 0, 0)", m);
            Assert.Empty(prog.Errors);
            c.Load(prog);
            c.Loop = false;
            Assert.True(c.Start());
            double t = 0;
            while (c.State == ArmState.Running && t < 20)
            {
                if (c.ToolCommand) holding = true;
                c.Update(1e-3);
                t += 1e-3;
            }

            Assert.Equal(ArmState.Idle, c.State);
            Assert.True(c.DigitalOut[0]);
            Assert.Equal(1, c.PalletIndex);
            var tcp = c.TcpReference;
            Assert.Equal(0.5, tcp.P.X, 4);
            Assert.Equal(0.1, tcp.P.Y, 4);
            Assert.Equal(0.3, tcp.P.Z, 4);
            _out.WriteLine($"program took {t:0.###} s");
            // Second run uses the next pallet slot (+100 mm in X) and then wraps.
            c.Start();
            t = 0;
            while (c.State == ArmState.Running && t < 20)
            {
                c.Update(1e-3);
                t += 1e-3;
            }

            Assert.Equal(0.6, c.TcpReference.P.X, 4);
            Assert.Equal(1, full);
        }

        [Fact]
        public void SafetyStopDeceleratesAndNeedsRestart()
        {
            var m = ArmCatalog.Get(ArmCatalog.Industrial6);
            var c = new ArmController(m, m.Home);
            var prog = ArmProgram.Parse("MOVEJ A\nMOVEJ B", "A = J(90, 0, 0, 0, 45, 0)\nB = J(-90, 0, 0, 0, 45, 0)", m);
            c.Load(prog);
            c.Loop = true;
            c.Start();
            for (int i = 0; i < 300; i++) c.Update(1e-3);
            c.SafetyOk = false;
            double before = c.Reference[0];
            for (int i = 0; i < 600; i++) c.Update(1e-3);
            Assert.Equal(ArmState.SafetyStop, c.State);
            double held = c.Reference[0];
            for (int i = 0; i < 300; i++) c.Update(1e-3);
            Assert.Equal(held, c.Reference[0], 9);
            Assert.True(Math.Abs(held - before) < 0.3, "controlled stop should be short");
            c.SafetyOk = true;
            for (int i = 0; i < 100; i++) c.Update(1e-3);
            Assert.Equal(ArmState.SafetyStop, c.State);   // manual restart required
            c.Resume();
            for (int i = 0; i < 100; i++) c.Update(1e-3);
            Assert.Equal(ArmState.Running, c.State);
            Assert.NotEqual(held, c.Reference[0]);
        }

        [Fact]
        public void ProgramParserReportsErrors()
        {
            var m = ArmCatalog.Get(ArmCatalog.Scara);
            var p = ArmProgram.Parse("MOVEJ X\nZIPLA 3\nBEKLE", "A = J(0, 0, 10, 0)\nB = J(1, 2)", m);
            Assert.Equal(4, p.Errors.Count);
            Assert.True(p.Points.ContainsKey("A"));
            Assert.Equal(0.010, p.Points["A"].Joints![2], 9);
        }
    }
}
