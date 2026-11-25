using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [phys] Contacts over a long wait: what friction holds must stay held, and what stands must stay standing, for tens
    /// of seconds and at a coarser step too. Slow creep and drift only show over time (an impulse solver that loses a
    /// little each step walks a box down a slope it should hold).
    /// </summary>
    public class LongRestTests
    {
        private readonly ITestOutputHelper _out;

        public LongRestTests(ITestOutputHelper output) => _out = output;

        private static void Run(PhysicsScene s, double seconds, double dt)
        {
            s.MaxSubstep = dt;   // the solver's own step (the app uses 0.5 ms)
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++) s.Step(dt);
        }

        private static SceneBody BoxOnSlope(PhysicsScene s, double angle)
        {
            var q = Quat.FromAxisAngle(Vec3.UnitY, -angle);
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.1, 0.1, 0.05), 0.3, q.Rotate(new Vec3(0, 0, 0.0249)), "wood"));
            b.Orientation = q;
            return b;
        }

        [Theory]
        [InlineData(5e-4, 20)]
        [InlineData(2e-3, 20)]
        [InlineData(5e-4, 25)]
        [InlineData(2e-3, 25)]
        public void ABoxHeldByFrictionStaysOnTheSlopeForHalfAMinute(double dt, double degrees)
        {
            // wood on wood: μs = 0.50 holds it up to 26.6° (tan 25° = 0.47). Gravity adds 8 mm/s along a 25° slope in one
            // 2 ms step: judged after that, the contact counted as sliding and kinetic friction (0.30) let the box go.
            double angle = degrees * Math.PI / 180;
            var s = new PhysicsScene { Terrain = PlaneTerrain.Incline(angle), GroundMaterial = "wood" };
            var b = BoxOnSlope(s, angle);
            Run(s, 0.3, dt);
            var p0 = b.Position;
            Run(s, 30, dt);
            double moved = (b.Position - p0).Length;
            _out.WriteLine($"{degrees}° at {dt * 1000} ms: moved {moved * 1000:F3} mm in 30 s");
            Assert.True(moved < 1e-3, $"crept {moved * 1000:F2} mm in 30 s on {degrees}° at {dt * 1000} ms");
        }

        [Theory]
        [InlineData(5e-4)]
        [InlineData(2e-3)]
        public void ABoxOnASlopeSteeperThanItsFrictionSlidesAtTheKineticRate(double dt)
        {
            // tan 30° = 0.58 > μs = 0.50: it breaks away and slides with g·(sin θ − μk·cos θ) = 2.36 m/s² (μk = 0.30).
            double angle = 30 * Math.PI / 180;
            var s = new PhysicsScene { Terrain = PlaneTerrain.Incline(angle), GroundMaterial = "wood" };
            var b = BoxOnSlope(s, angle);
            Run(s, 0.2, dt);
            var p0 = b.Position;
            var v0 = b.Velocity.Length;
            Run(s, 1, dt);
            double moved = (b.Position - p0).Length, expected = 9.81 * (Math.Sin(angle) - 0.30 * Math.Cos(angle));
            double a = 2 * (moved - v0) / 1.0;   // s = v0·t + ½·a·t² over t = 1 s
            _out.WriteLine($"{dt * 1000} ms: slid {moved:F3} m in 1 s, a = {a:F3} m/s² (expected {expected:F3})");
            Assert.InRange(a, expected * 0.97, expected * 1.03);
        }

        [Theory]
        [InlineData(5e-4, 20)]
        [InlineData(2e-3, 10)]
        public void ATallStackStandsForTwentySeconds(double dt, int count)
        {
            // Wooden blocks, every other one 2 mm off centre, so no interface is symmetric.
            var s = new PhysicsScene { GroundMaterial = "wood" };
            var boxes = Enumerable.Range(0, count)
                .Select(i => s.AddBody(SceneBody.Box("b" + i, new Vec3(0.1, 0.1, 0.1), 0.2, new Vec3(0.002 * (i % 2), 0, 0.05 + 0.1 * i + 0.0005 * i), "wood")))
                .ToList();
            Run(s, 2, dt);
            var top = boxes.Last();
            var p0 = top.Position;
            Run(s, 20, dt);
            double drift = (top.Position - p0).Length, height = 0.1 * count - 0.05;
            double maxV = boxes.Max(x => x.Velocity.Length);
            double tilt = boxes.Max(x => Math.Acos(Math.Min(1, x.Orientation.Rotate(Vec3.UnitZ).Z)));
            _out.WriteLine($"{count} blocks at {dt * 1000} ms: top at {top.Position.Z * 1000:F2} mm, drifted {drift * 1000:F3} mm in 20 s, vmax {maxV:E2} m/s, tilt {tilt:E2} rad");
            Assert.True(drift < 1e-3, $"the top drifted {drift * 1000:F2} mm in 20 s");
            Assert.InRange(top.Position.Z, height - 2e-3, height + 1e-3);
            Assert.True(maxV < 2e-3 && tilt < 1e-2, "the stack is at rest");
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        public void AHeavyBlockOnALightOneStands(double ratio)
        {
            // A sequential-impulse solver's weak spot: a heavy body on a light one. The light block must neither sink into
            // the ground nor let the heavy one through, and the pair must come to rest.
            var s = new PhysicsScene { GroundMaterial = "wood" };
            var light = s.AddBody(SceneBody.Box("light", new Vec3(0.1, 0.1, 0.05), 0.1, new Vec3(0, 0, 0.025), "wood"));
            var heavy = s.AddBody(SceneBody.Box("heavy", new Vec3(0.1, 0.1, 0.05), 0.1 * ratio, new Vec3(0, 0, 0.0755), "wood"));
            Run(s, 5, 5e-4);
            double lightSink = 0.025 - light.Position.Z, gap = (heavy.Position.Z - light.Position.Z) - 0.05;
            _out.WriteLine($"mass ratio {ratio}: light block {lightSink * 1000:F3} mm into the ground, the heavy one {-gap * 1000:F3} mm into the light one, v {Math.Max(light.Velocity.Length, heavy.Velocity.Length):E2} m/s");
            Assert.True(lightSink < 0.003 && -gap < 0.003, "within 3 mm of the surfaces");
            Assert.True(light.Velocity.Length < 2e-3 && heavy.Velocity.Length < 2e-3, "at rest");
        }
    }
}
