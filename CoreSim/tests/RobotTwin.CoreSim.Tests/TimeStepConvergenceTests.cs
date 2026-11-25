using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [phys] Time-step convergence: the same motion run with the solver's step (<see cref="PhysicsScene.MaxSubstep"/>)
    /// halved again and again must approach the exact answer at the integrator's order, and be close at the step the app
    /// uses (0.5 ms). A projectile, a hinged pendulum's period, a box coming to rest on the ground, and a hanging chain of
    /// hinges that must not drift apart.
    /// </summary>
    public class TimeStepConvergenceTests
    {
        private const double G = 9.81;
        private static readonly double[] Steps = { 4e-3, 2e-3, 1e-3, 5e-4, 2.5e-4 };
        private const int AppStep = 3;   // 0.5 ms, PhysicsScene's own substep
        private readonly ITestOutputHelper _out;

        public TimeStepConvergenceTests(ITestOutputHelper output) => _out = output;

        /// <summary>Runs with the solver's own step set to <paramref name="dt"/> (one substep per step).</summary>
        private static void Run(PhysicsScene s, double seconds, double dt, Action? each = null)
        {
            s.MaxSubstep = dt;
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++)
            {
                s.Step(dt);
                each?.Invoke();
            }
        }

        /// <summary>How far a joint's two anchors are apart: the child's and the parent's (or the fixed point in the world).</summary>
        private static double Separation(SceneJoint j)
        {
            var child = j.WorldAnchor;
            var parent = j.B != null ? j.A.Position + j.A.Orientation.Rotate(j.LocalAnchorA) : j.LocalAnchorB;
            return (child - parent).Length;
        }

        /// <summary>The observed order between successive halvings (log2 of the error ratio), where the errors are above noise.</summary>
        private static double[] Orders(double[] errors) =>
            Enumerable.Range(0, errors.Length - 1).Where(i => errors[i + 1] > 1e-12).Select(i => Math.Log(errors[i] / errors[i + 1], 2)).ToArray();

        [Fact]
        public void AProjectileConvergesToTheParabola()
        {
            var errors = new double[Steps.Length];
            for (int k = 0; k < Steps.Length; k++)
            {
                var s = new PhysicsScene { Gravity = new Vec3(0, 0, -G), GroundEnabled = false, AirDensity = 0 };
                // 40 cm across: a small fast body would get more substeps to keep its collisions (continuous collision
                // detection), which is the step this study varies.
                var b = s.AddBody(SceneBody.Box("b", new Vec3(0.4, 0.4, 0.4), 0.4, new Vec3(0, 0, 1)));
                b.LinearDrag = 0;
                b.AngularDrag = 0;
                b.DragArea = Vec3.Zero;
                b.Velocity = new Vec3(3, 1, 9);
                Run(s, 1.0, Steps[k]);
                var exact = new Vec3(3, 1, 1 + 9 - G / 2);
                errors[k] = (b.Position - exact).Length;
            }

            var orders = Orders(errors);
            _out.WriteLine("dt (ms): " + string.Join(", ", Steps.Select(d => (d * 1000).ToString("0.###"))));
            _out.WriteLine("error (mm): " + string.Join(", ", errors.Select(e => (e * 1000).ToString("0.####"))));
            _out.WriteLine("order: " + string.Join(", ", orders.Select(o => o.ToString("0.00"))));
            Assert.True(errors[AppStep] < 0.003, "within 3 mm after 1 s at 0.5 ms (the app's step)");
            Assert.All(orders, o => Assert.InRange(o, 0.9, 2.2));   // at least first order, steadily
            Assert.True(errors.Zip(errors.Skip(1), (a, c) => c <= a * 1.0001).All(x => x), "each halving is closer");
        }

        [Fact]
        public void APendulumsPeriodConvergesToTheAnalyticPeriod()
        {
            double len = 0.3, t = 0.01, m = 0.2, amp = 5 * Math.PI / 180;
            double inertia = m * (len * len + t * t) / 12 + m * len * len / 4;
            double period = 2 * Math.PI * Math.Sqrt(inertia / (m * G * len / 2)) * (1 + amp * amp / 16);
            var errors = new double[Steps.Length];
            for (int k = 0; k < Steps.Length; k++)
            {
                var s = new PhysicsScene { Gravity = new Vec3(0, 0, -G), GroundEnabled = false, AirDensity = 0 };
                var rod = s.AddBody(SceneBody.Box("rod", new Vec3(len, t, t), m, new Vec3(len / 2, 0, 1.0)));
                var hang = Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2 - amp);
                rod.Orientation = hang;
                rod.Position = new Vec3(0, 0, 1.0) + hang.Rotate(new Vec3(len / 2, 0, 0));
                var hinge = s.AddJoint(JointKind.Hinge, rod, null, new Vec3(0, 0, 1.0), Vec3.UnitY, "pivot");
                double prev = hinge.Angle, first = -1, last = -1;
                int crossings = 0;
                Run(s, 8 * period, Steps[k], () =>
                {
                    double a = hinge.Angle - amp;
                    if (prev - amp < 0 && a >= 0)
                    {
                        // the crossing time, between the two steps
                        double tc = s.Time - Steps[k] * a / (a - (prev - amp));
                        if (first < 0) first = tc;
                        last = tc;
                        crossings++;
                    }

                    prev = hinge.Angle;
                });
                errors[k] = Math.Abs((last - first) / (crossings - 1) - period) / period;
            }

            _out.WriteLine($"analytic period {period:F5} s; relative error at dt " + string.Join(", ", Steps.Select((d, i) => $"{d * 1000:0.###} ms: {errors[i]:E2}")));
            Assert.True(errors[AppStep] < 0.005, "period within 0.5 % at 0.5 ms");
            Assert.True(errors[Steps.Length - 1] <= errors[0] + 1e-4, "a smaller step is no worse");
        }

        [Fact]
        public void ABoxComesToRestAtTheSameHeightWhateverTheStep()
        {
            // Dropped from 30 cm, tumbling: wherever it settles, its centre ends a half-edge above the ground, the same at
            // every step (contacts must not sink deeper, or float, as the step grows).
            var heights = new double[Steps.Length];
            for (int k = 0; k < Steps.Length; k++)
            {
                var s = new PhysicsScene { Gravity = new Vec3(0, 0, -G), AirDensity = 0 };
                var b = s.AddBody(SceneBody.Box("b", new Vec3(0.06, 0.06, 0.06), 0.3, new Vec3(0, 0, 0.3)));
                b.AngularVelocity = new Vec3(1.5, -2, 0.7);
                Run(s, 3.0, Steps[k]);
                heights[k] = b.Position.Z;
                Assert.True(b.Velocity.Length < 0.01, $"at rest at dt {Steps[k] * 1000} ms ({b.Velocity.Length:E2} m/s)");
            }

            _out.WriteLine("rest height (mm): " + string.Join(", ", heights.Select(h => (h * 1000).ToString("0.###"))));
            Assert.All(heights, h => Assert.InRange(h, 0.030 - 0.0015, 0.030 + 0.0005));   // half the edge, within 1.5 mm of sinking
            Assert.True(heights.Max() - heights.Min() < 0.0015, "the same within 1.5 mm at every step");
        }

        [Fact]
        public void AHangingChainOfHingesHoldsTogether()
        {
            // Five links hanging from a fixed pivot, released 30° off and swinging for 10 s: every joint's two anchors
            // stay together (the solver's drift), at the app's step and at eight times it.
            foreach (double dt in new[] { 5e-4, 4e-3 })
            {
                var s = new PhysicsScene { Gravity = new Vec3(0, 0, -G), GroundEnabled = false, AirDensity = 0 };
                double len = 0.1, side = 0.01;
                var tilt = Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2 - 30 * Math.PI / 180);
                var dir = tilt.Rotate(new Vec3(1, 0, 0));
                var top = new Vec3(0, 0, 1.0);
                SceneBody? previous = null;
                var joints = new System.Collections.Generic.List<SceneJoint>();
                for (int i = 0; i < 5; i++)
                {
                    var anchor = top + dir * (len * i);
                    var link = s.AddBody(SceneBody.Box("l" + i, new Vec3(len, side, side), 0.05, anchor + dir * (len / 2)));
                    link.Orientation = tilt;
                    joints.Add(s.AddJoint(JointKind.Hinge, link, previous, anchor, Vec3.UnitY, "j" + i));
                    previous = link;
                }

                double worst = 0;
                Run(s, 10, dt, () => worst = Math.Max(worst, joints.Max(Separation)));
                _out.WriteLine($"dt {dt * 1000} ms: largest joint separation {worst * 1000:F3} mm");
                Assert.True(worst < (dt <= 5e-4 ? 0.001 : 0.003), $"joints hold together within {(dt <= 5e-4 ? 1 : 3)} mm at {dt * 1000} ms");
            }
        }
    }
}
