using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[gas][launch] A model rocket fired through its launch circuit flies, peaks and comes down on its parachute.</summary>
    public class SolidRocketFlightTests
    {
        private readonly ITestOutputHelper _out;

        public SolidRocketFlightTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void TheCountdownFiresTheEMatchAndTheRocketFliesToApogeeAndDescendsOnItsParachute()
        {
            var w = WorldExamples.SolidRocket(PartLibrary.Default);
            var motor = w.Objects.First(o => o.Name.StartsWith("KNSB"));
            var body = w.Objects.First(o => o.Name.StartsWith("Gövde"));
            using var r = new WorldRunner(w);
            var p = r.Physics!;
            double z0 = p.PoseOf(body.Id)!.Value.Position.Z, apogee = z0, maxSpeed = 0, maxTilt = 0, lit = double.NaN, chuteAt = double.NaN, impulse = 0;
            var events = new List<string>();
            var descent = new List<double>();
            for (int i = 0; i < 60 * 20; i++)
            {
                r.Step(0.05);
                foreach (var e in r.DrainEvents()) events.Add(FormattableString.Invariant($"{e.Time:F3} {e.Message}"));   // physics and circuit events
                var g = p.GasOf(motor.Id)!;
                impulse += g.Thrust * 0.05;
                var pose = p.PoseOf(body.Id)!.Value;
                double t = p.Scene.Time;
                if (double.IsNaN(lit) && g.Burning) lit = t;
                apogee = Math.Max(apogee, pose.Position.Z);
                var axis = pose.Orientation.Rotate(Vec3.UnitZ);
                if (g.Burning) maxTilt = Math.Max(maxTilt, Math.Acos(Math.Min(1, axis.Z)) * 180 / Math.PI);
                var chute = p.ParachuteOf(body.Id)!.Value;
                if (chute.Deployed && double.IsNaN(chuteAt)) chuteAt = t;
                if (r.TryGetBody(body.Id, out var st))
                {
                    maxSpeed = Math.Max(maxSpeed, st.Velocity.Length);
                    if (chute.Open >= 1 && t > chuteAt + 3 && pose.Position.Z > 5) descent.Add(-st.Velocity.Z);
                }

                if (!double.IsNaN(chuteAt) && pose.Position.Z < z0 + 1) break;
            }

            foreach (var e in events.Take(12)) _out.WriteLine(e);
            double vDown = descent.Count > 0 ? descent.Average() : double.NaN;
            _out.WriteLine(FormattableString.Invariant($"lit {lit:F3} s, impulse {impulse:F0} N·s, max speed {maxSpeed:F0} m/s, apogee {apogee - z0:F0} m, tilt {maxTilt:F1}°, chute at {chuteAt:F1} s, descent {vDown:F1} m/s"));
            Assert.Contains(events, e => e.Contains("kapandı"));            // the countdown closes the arming switch
            Assert.Contains(events, e => e.Contains("köprü teli açıldı"));   // the e-match bridgewire fired
            Assert.Contains(events, e => e.Contains("ateşleyici yandı"));    // and lit the igniter
            Assert.Contains(events, e => e.Contains("tutuştu"));
            Assert.InRange(lit, 2.0, 2.06);                                 // e-match, igniter and soak take milliseconds
            Assert.InRange(impulse, 100, 150);                              // a G-class motor
            Assert.InRange(maxSpeed, 150, 330);
            Assert.InRange(apogee - z0, 400, 1600);
            Assert.True(maxTilt < 5, $"tilt {maxTilt:F1}° under thrust: the fins keep it straight");
            Assert.False(double.IsNaN(chuteAt));
            Assert.Contains(events, e => e.Contains("paraşüt atıldı"));
            Assert.InRange(vDown, 4, 10);                                   // a 45 cm canopy on ≈ 360 g
        }

        [Fact]
        public void TheRocketsDragCoefficientIsThatOfAModelRocketAndStaysSoInFlight()
        {
            // Coasting at ~150 m/s with the motor spent: nose, tube, three square-edged fins and the motor's tail.
            var w = WorldExamples.SolidRocket(PartLibrary.Default);
            foreach (var o in w.Objects) o.Parameters.Remove("vessel");
            w.Environment.Gravity = 0;
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, PartLibrary.Default).GeometryOf, null);
            var rocket = p.Scene.Bodies.Where(b => !b.IsStatic).ToList();
            double mass = rocket.Sum(b => b.Mass), area = Math.PI / 4 * 0.041 * 0.041;
            foreach (var b in rocket)
            {
                b.Position += new Vec3(0, 0, 5);
                b.Velocity = new Vec3(0, 0, 150);
            }

            double Cd(int settle)
            {
                for (int i = 0; i < settle; i++) p.Advance(1e-3, null);
                double v0 = rocket[0].Velocity.Z;
                for (int i = 0; i < 50; i++) p.Advance(1e-3, null);
                double v1 = rocket[0].Velocity.Z, v = (v0 + v1) / 2;
                return (v0 - v1) / 0.05 * mass * 2 / (p.Scene.AirDensity * v * v * area);
            }

            double early = Cd(20), late = Cd(600);
            _out.WriteLine($"Cd {early:F2} at first, {late:F2} after 0.6 s");
            Assert.InRange(early, 0.4, 0.8);   // OpenRocket-class estimates for this layout: ≈ 0.5–0.7
            Assert.InRange(late / early, 0.9, 1.1);
        }
    }
}
