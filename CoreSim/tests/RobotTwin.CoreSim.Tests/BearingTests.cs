using System;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[bearing] Bearing friction grows with the load: a heavy wheel coasts down faster on a bushing than on a ball bearing.</summary>
    public class BearingTests
    {
        private readonly ITestOutputHelper _out;

        public BearingTests(ITestOutputHelper output) => _out = output;

        /// <summary>A 10 kg flywheel on a vertical shaft, spun to 10 rad/s, after 2 s on a bearing of this friction per newton.</summary>
        private static double Coast(double perNewton, double mass = 10)
        {
            var scene = new PhysicsScene();
            var wheel = SceneBody.Cylinder("wheel", 0.1, 0.03, mass, new Vec3(0, 0, 0.5));
            scene.AddBody(wheel);
            var shaft = scene.AddJoint(JointKind.Hinge, wheel, null, new Vec3(0, 0, 0.5), Vec3.UnitZ, "shaft");
            shaft.LoadFriction = perNewton;
            wheel.AngularVelocity = new Vec3(0, 0, 10);
            for (int i = 0; i < 2000; i++) scene.Step(1e-3);
            return shaft.Speed;
        }

        [Fact]
        public void AFlywheelCoastsDownAtItsBearingsFrictionTimesItsWeight()
        {
            double inertia = 0.5 * 10 * 0.1 * 0.1;
            foreach (var (id, bore) in new[] { ("ball", 8.0), ("bronze", 8.0) })
            {
                var spec = Bearings.Find(id)!;
                double perNewton = spec.Mu * bore / 2000;
                double expected = 10 - perNewton * 10 * 9.81 / inertia * 2;   // T = μ·m·g·d/2, ω = ω0 − T/I·t
                double heavy = Coast(perNewton), light = Coast(perNewton, 2.5);
                _out.WriteLine($"{spec.Name}: 2 s sonra {heavy:0.0000} rad/s (kuram {expected:0.0000}); dörtte bir ağır tekerde {light:0.0000}");
                Assert.InRange(heavy, expected - 0.02 * (10 - expected) - 1e-4, expected + 0.02 * (10 - expected) + 1e-4);
                Assert.InRange(10 - light, (10 - heavy) * 0.97, (10 - heavy) * 1.03);   // a quarter of the load, a quarter of the inertia: the same slowing
            }

            Assert.True(Coast(Bearings.Find("bronze")!.Mu * 0.004) < Coast(Bearings.Find("ball")!.Mu * 0.004) - 1, "a bushing brakes far more than a ball bearing");
        }

        [Fact]
        public void ACarriageSlidesToAStopAtItsBearingsFriction()
        {
            foreach (var id in new[] { "linear-ball", "slide" })
            {
                var spec = Bearings.Find(id)!;
                var scene = new PhysicsScene();
                var cart = SceneBody.Box("cart", new Vec3(0.05, 0.05, 0.03), 2, new Vec3(0, 0, 0.5));
                scene.AddBody(cart);
                var rail = scene.AddJoint(JointKind.Slider, cart, null, new Vec3(0, 0, 0.5), Vec3.UnitX, "rail");
                rail.LoadFriction = spec.Mu;
                cart.Velocity = new Vec3(1, 0, 0);
                for (int i = 0; i < 500; i++) scene.Step(1e-3);
                double expected = 1 - spec.Mu * 9.81 * 0.5;   // decelerates at μ·g
                _out.WriteLine($"{spec.Name}: 0,5 s sonra {rail.Speed:0.0000} m/s (kuram {expected:0.0000}), sürtünme {rail.BearingFriction:0.000} N");
                Assert.InRange(rail.Speed, expected - 0.02 * (1 - expected) - 1e-4, expected + 0.02 * (1 - expected) + 1e-4);
            }
        }
    }
}
