using System;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [phys] What no force inside a system can change: linear and angular momentum through collisions, a free body's
    /// angular momentum and energy, the mechanical energy of a body in vacuum, and that contacts never give energy.
    /// Drag is off, so only the solver is measured.
    /// </summary>
    public class ConservationTests
    {
        private readonly ITestOutputHelper _out;

        public ConservationTests(ITestOutputHelper output) => _out = output;

        private static PhysicsScene Vacuum(bool gravity = false) =>
            new PhysicsScene { Gravity = gravity ? new Vec3(0, 0, -9.81) : Vec3.Zero, GroundEnabled = false, AirDensity = 0 };

        private static SceneBody Free(SceneBody b)
        {
            b.LinearDrag = 0;
            b.AngularDrag = 0;
            b.DragArea = Vec3.Zero;
            return b;
        }

        private static Vec3 Momentum(params SceneBody[] bodies)
        {
            var p = Vec3.Zero;
            foreach (var b in bodies) p += b.Velocity * b.Mass;
            return p;
        }

        /// <summary>Angular momentum about the origin: spin plus the orbit of each body's mass.</summary>
        private static Vec3 AngularMomentum(params SceneBody[] bodies)
        {
            var l = Vec3.Zero;
            foreach (var b in bodies)
                l += Mat3.RotatedDiagonal(b.Orientation * b.PrincipalAxes, b.Inertia) * b.AngularVelocity + b.Position.Cross(b.Velocity * b.Mass);
            return l;
        }

        private static double Kinetic(params SceneBody[] bodies)
        {
            double e = 0;
            foreach (var b in bodies)
            {
                var w = b.AngularVelocity;
                e += 0.5 * b.Mass * b.Velocity.LengthSquared + 0.5 * w.Dot(Mat3.RotatedDiagonal(b.Orientation * b.PrincipalAxes, b.Inertia) * w);
            }

            return e;
        }

        [Fact]
        public void AHeadOnCollisionKeepsMomentumAndFollowsNewtonsRestitution()
        {
            // Steel on steel (e = 0.6), 1 kg at +2 m/s meets 3 kg at −1 m/s: the total momentum stays −1 kg·m/s, the
            // speed of separation is e times the speed of approach, and energy is lost, never made.
            var s = Vacuum();
            var a = s.AddBody(Free(SceneBody.Sphere("a", 0.05, 1, new Vec3(-0.3, 0, 0), "steel")));
            var b = s.AddBody(Free(SceneBody.Sphere("b", 0.05, 3, new Vec3(0.3, 0, 0), "steel")));
            a.Velocity = new Vec3(2, 0, 0);
            b.Velocity = new Vec3(-1, 0, 0);
            var p0 = Momentum(a, b);
            double approach = (a.Velocity - b.Velocity).X, e0 = Kinetic(a, b);
            for (int i = 0; i < 1000; i++) s.Step(1e-3);
            var p1 = Momentum(a, b);
            double separation = (b.Velocity - a.Velocity).X, e1 = Kinetic(a, b);
            double e = Materials.Pair(a.MaterialIndex, b.MaterialIndex).Restitution;
            _out.WriteLine($"momentum {p0.X:F6} → {p1.X:F6} kg·m/s (off by {(p1 - p0).Length:E2}); separation/approach {separation / approach:F4} (e = {e}); energy {e0:F4} → {e1:F4} J; after: a {a.Velocity.X:F4}, b {b.Velocity.X:F4} m/s, spin {a.AngularVelocity.Length:E1} rad/s");
            Assert.True((p1 - p0).Length < 1e-9 * 4, "no outside force: the momentum stays");
            Assert.InRange(separation / approach, e - 0.02, e + 0.02);
            Assert.True(e1 < e0, "the collision loses energy");
            // What it loses: (1 − e²) of the energy of the relative motion, ½ μ v² with μ = m₁m₂/(m₁ + m₂).
            double lost = (1 - e * e) * 0.5 * (a.Mass * b.Mass / (a.Mass + b.Mass)) * approach * approach;
            _out.WriteLine($"energy lost {e0 - e1:F4} J, theory {lost:F4} J");
            Assert.InRange(e0 - e1, lost * 0.98, lost * 1.02);
        }

        [Fact]
        public void AnOffCentreHitKeepsLinearAndAngularMomentum()
        {
            // A sphere strikes a free box near its end in empty space: the box flies off spinning. Momentum and angular
            // momentum (about any fixed point) of the two together are what they were.
            var s = Vacuum();
            var box = s.AddBody(Free(SceneBody.Box("box", new Vec3(0.4, 0.08, 0.08), 2, new Vec3(0, 0, 0), "aluminium")));
            var ball = s.AddBody(Free(SceneBody.Sphere("ball", 0.03, 0.5, new Vec3(0.15, -0.4, 0), "steel")));
            ball.Velocity = new Vec3(0, 3, 0);
            var p0 = Momentum(box, ball);
            var l0 = AngularMomentum(box, ball);
            double e0 = Kinetic(box, ball);
            for (int i = 0; i < 600; i++) s.Step(1e-3);
            var p1 = Momentum(box, ball);
            var l1 = AngularMomentum(box, ball);
            double e1 = Kinetic(box, ball);
            _out.WriteLine($"p {p0} → {p1} (off {(p1 - p0).Length:E2}); L {l0} → {l1} (off {(l1 - l0).Length:E2} of |L| {l0.Length:F4}); E {e0:F4} → {e1:F4} J; box spin {box.AngularVelocity.Length:F3} rad/s");
            Assert.True(box.AngularVelocity.Length > 1, "the box was struck off its centre and spins");
            Assert.True((p1 - p0).Length < 1e-6 * p0.Length, "momentum");
            Assert.True((l1 - l0).Length < 0.01 * l0.Length, "angular momentum");
            Assert.True(e1 <= e0 * (1 + 1e-9), "no energy made");
        }

        [Fact]
        public void AFreeBodyKeepsItsAngularMomentumAndEnergyAndFlipsAboutItsMiddleAxis()
        {
            // A box spun about its intermediate axis is unstable (tennis racket theorem, Dzhanibekov): it flips over and
            // back, while its angular momentum and its rotational energy stay as they were.
            var s = Vacuum();
            var b = s.AddBody(Free(SceneBody.Box("b", new Vec3(0.3, 0.2, 0.1), 1.5)));
            b.AngularVelocity = new Vec3(0.02, 6, 0.02);   // about the middle (y) axis, a little off
            var l0 = AngularMomentum(b);
            double e0 = Kinetic(b);
            double minAlong = double.MaxValue;
            for (int i = 0; i < 6000; i++)
            {
                s.Step(1e-3);
                var axis = b.Orientation.Rotate(new Vec3(0, 1, 0));   // the box's own middle axis, in the world
                minAlong = Math.Min(minAlong, axis.Dot(l0.Normalized()));
            }

            var l1 = AngularMomentum(b);
            double e1 = Kinetic(b);
            double angle = Math.Acos(Math.Clamp(l0.Normalized().Dot(l1.Normalized()), -1, 1)) * 180 / Math.PI;
            _out.WriteLine($"|L| {l0.Length:F5} → {l1.Length:F5} ({(l1.Length / l0.Length - 1) * 100:+0.00;-0.00} %), turned {angle:F3}°; E {e0:F5} → {e1:F5} J ({(e1 / e0 - 1) * 100:+0.00;-0.00} %); middle axis came to {Math.Acos(Math.Clamp(minAlong, -1, 1)) * 180 / Math.PI:F0}° from L");
            Assert.True(minAlong < -0.9, "it flipped: its middle axis turned round against the angular momentum");
            Assert.InRange(l1.Length / l0.Length, 0.98, 1.0 + 1e-6);
            Assert.True(angle < 1, "and the angular momentum keeps its direction");
            Assert.InRange(e1 / e0, 0.97, 1.0 + 1e-6);
        }

        [Fact]
        public void ABodyThrownInVacuumKeepsItsMechanicalEnergy()
        {
            // Under gravity alone: kinetic plus potential energy stays, while the body rises and falls for two seconds.
            var s = Vacuum(gravity: true);
            var b = s.AddBody(Free(SceneBody.Box("b", new Vec3(0.05, 0.05, 0.05), 0.4, new Vec3(0, 0, 1))));
            b.Velocity = new Vec3(3, 1, 9);
            b.AngularVelocity = new Vec3(2, -3, 5);
            double E() => Kinetic(b) + b.Mass * 9.81 * b.Position.Z;
            double e0 = E(), worst = 0;
            for (int i = 0; i < 2000; i++)
            {
                s.Step(1e-3);
                worst = Math.Max(worst, Math.Abs(E() - e0));
            }

            _out.WriteLine($"E {e0:F5} J, largest change in 2 s {worst:E2} J ({worst / e0 * 100:F4} %), ends at z {b.Position.Z:F3} m");
            Assert.True(worst < 0.002 * e0, "kinetic + potential energy within 0.2 %");
        }

        [Fact]
        public void BouncesOnlyEverLoseEnergy()
        {
            // A box tossed onto the ground, spinning: through every bounce and slide its energy (kinetic + potential)
            // never rises above what it had. A contact that pushed harder than it was pressed would make energy.
            var s = new PhysicsScene { AirDensity = 0 };
            var b = s.AddBody(Free(SceneBody.Box("b", new Vec3(0.1, 0.06, 0.04), 0.3, new Vec3(0, 0, 0.6), "steel")));
            b.Velocity = new Vec3(1.5, 0.3, 0);
            b.AngularVelocity = new Vec3(4, 7, -2);
            double E() => Kinetic(b) + b.Mass * 9.81 * b.Position.Z;
            double e0 = E(), last = e0, worstRise = 0;
            for (int i = 0; i < 3000; i++)
            {
                s.Step(1e-3);
                double e = E();
                worstRise = Math.Max(worstRise, e - last);
                last = Math.Min(last, e);   // the lowest energy so far: any rise above it is energy made
            }

            _out.WriteLine($"E {e0:F4} → {E():F4} J; largest rise above the lowest so far {worstRise:E2} J ({worstRise / e0 * 100:F3} % of the start)");
            Assert.True(worstRise < 0.01 * e0, "no bounce gives more than 1 % of the energy back");
            Assert.True(E() < 0.5 * e0, "and it comes to rest");
        }
    }
}
