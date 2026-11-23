using System;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[weld] Parts joined by rigid welds move as one body; the welds still report what they carry.</summary>
    public class WeldTests
    {
        private readonly ITestOutputHelper _out;

        public WeldTests(ITestOutputHelper output) => _out = output;

        private static SceneJoint Weld(PhysicsScene s, SceneBody parent, SceneBody child, Vec3 anchor)
        {
            var j = s.AddJoint(JointKind.Fixed, parent, child, anchor, Vec3.UnitZ);
            j.Weld = true;
            return j;
        }

        [Fact]
        public void AWeldCarriesTheWeightOfThePartOnIt()
        {
            var s = new PhysicsScene();
            var bottom = s.AddBody(SceneBody.Box("base", new Vec3(0.2, 0.2, 0.02), 1.0, new Vec3(0, 0, 0.01)));
            var top = s.AddBody(SceneBody.Box("top", new Vec3(0.05, 0.05, 0.05), 0.5, new Vec3(0, 0, 0.045)));
            var j = Weld(s, bottom, top, new Vec3(0, 0, 0.02));
            for (int i = 0; i < 1000; i++) s.Step(1e-3);
            _out.WriteLine($"reaction on the top part {j.ReactionForce}, torque {j.ReactionTorque}");
            Assert.InRange(j.ReactionForce.Z, 0.5 * 9.81 * 0.98, 0.5 * 9.81 * 1.02);   // holds it up against gravity
            Assert.True(Math.Abs(j.ReactionForce.X) + Math.Abs(j.ReactionForce.Y) < 0.05);
            Assert.True(j.ReactionTorque.Length < 0.01);
        }

        [Fact]
        public void ACantileverWeldCarriesTheBendingMoment()
        {
            var s = new PhysicsScene();
            var block = s.AddBody(SceneBody.Box("block", new Vec3(0.2, 0.2, 0.1), 5.0, new Vec3(0, 0, 0.05)));
            // A 200 g arm sticking out 0.2 m from the block's side at mid height: centre 0.1 m from the weld.
            var arm = s.AddBody(SceneBody.Box("arm", new Vec3(0.2, 0.02, 0.02), 0.2, new Vec3(0.2, 0, 0.05)));
            var j = Weld(s, block, arm, new Vec3(0.1, 0, 0.05));
            for (int i = 0; i < 1000; i++) s.Step(1e-3);
            double moment = 0.2 * 9.81 * 0.1;
            _out.WriteLine($"force {j.ReactionForce}, torque {j.ReactionTorque} (expected |T| = {moment:F4} N·m about y)");
            Assert.InRange(j.ReactionForce.Z, 0.2 * 9.81 * 0.97, 0.2 * 9.81 * 1.03);
            Assert.InRange(Math.Abs(j.ReactionTorque.Y), moment * 0.95, moment * 1.05);
            Assert.True(arm.Position.Z > 0.049, "the arm does not sag: welded parts are one rigid body");
        }

        [Fact]
        public void ALightPartInAWeldedChainKeepsItsAlignment()
        {
            // 400 g frame → 6 g wire leg → 10 g wheel hub welded on: a hard sideways shove on the light end.
            var s = new PhysicsScene { Gravity = Vec3.Zero, GroundEnabled = false };
            var frame = s.AddBody(SceneBody.Box("frame", new Vec3(0.26, 0.05, 0.05), 0.4, new Vec3(0, 0, 0.2)));
            var leg = s.AddBody(SceneBody.Box("leg", new Vec3(0.003, 0.003, 0.09), 0.006, new Vec3(0, 0.09, 0.13)));
            var hub = s.AddBody(SceneBody.Box("hub", new Vec3(0.02, 0.02, 0.02), 0.01, new Vec3(0, 0.09, 0.075)));
            Weld(s, frame, leg, new Vec3(0, 0.09, 0.175));
            Weld(s, leg, hub, new Vec3(0, 0.09, 0.085));
            var rest = frame.Orientation.Conjugate * hub.Orientation;
            var offset = frame.Orientation.InverseRotate(hub.Position - frame.Position);
            for (int i = 0; i < 500; i++)
            {
                hub.AddForceAtPoint(new Vec3(2, 0, 0), hub.Position + new Vec3(0, 0, -0.01));
                s.Step(1e-3);
            }

            var rel = frame.Orientation.Conjugate * hub.Orientation;
            double angle = 2 * Math.Acos(Math.Min(1, Math.Abs((rel * rest.Conjugate).W))) * 180 / Math.PI;
            double drift = (frame.Orientation.InverseRotate(hub.Position - frame.Position) - offset).Length;
            _out.WriteLine($"hub turned {angle:E2}° and moved {drift * 1000:E2} mm relative to the frame; frame spins at {frame.AngularVelocity.Length:F2} rad/s");
            Assert.True(angle < 1e-6 && drift < 1e-9, "a weld does not flex");
            Assert.True(frame.AngularVelocity.Length > 0.1, "the shove on the hub turns the whole frame");
        }

        [Fact]
        public void AWireLyingOnABodyPushesItBack()
        {
            // No gravity, no floor: a taut wire between two free blocks runs over a third block. The wire presses on
            // it and pulls the two end blocks towards it; by Newton's third law the block is pushed back, so once the
            // wire has settled the three blocks gain no more momentum. (A one-way contact let the wire pull its ends
            // forever while the block felt nothing: the whole set drifted off — a resting quad lifted off the floor.)
            var s = new PhysicsScene { Gravity = Vec3.Zero, GroundEnabled = false, AirDensity = 0 };
            var a = s.AddBody(SceneBody.Box("a", new Vec3(0.03, 0.03, 0.03), 0.05, new Vec3(-0.15, 0, 0)));
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.03, 0.03, 0.03), 0.05, new Vec3(0.15, 0, 0)));
            var c = s.AddBody(SceneBody.Box("c", new Vec3(0.04, 0.04, 0.04), 0.05, new Vec3(0, 0, -0.012)));
            var ra = new RopeEnd { Body = a, Local = new Vec3(0.015, 0, 0), Exit = new Vec3(1, 0, 0) };
            var rb = new RopeEnd { Body = b, Local = new Vec3(-0.015, 0, 0), Exit = new Vec3(-1, 0, 0) };
            var rope = new CableRope("C", ra, rb, 0.27, 26);
            Vec3 Momentum() => a.Velocity * a.Mass + b.Velocity * b.Mass + c.Velocity * c.Mass;
            var settled = Vec3.Zero;
            for (int i = 0; i < 1000; i++)
            {
                rope.Step(s, 1e-3, true);
                s.Step(1e-3);
                if (i == 299) settled = Momentum();
            }

            var drift = Momentum() - settled;
            _out.WriteLine($"block under the wire: v {c.Velocity}, moved {(c.Position - new Vec3(0, 0, -0.012)) * 1000} mm; ends {a.Velocity} {b.Velocity}; momentum gained from 0.3 s to 1 s: {drift.Length:E2} kg·m/s");
            Assert.True(c.Velocity.Z < -1e-3, "the wire pushes the block it lies on");
            Assert.True(a.Velocity.Z > 0 && b.Velocity.Z > 0, "and pulls its ends towards it");
            Assert.True(drift.Length < 1e-5, "action and reaction: no momentum from inside the set");
        }

        [Fact]
        public void AWeldedGroupTumblingFreelyKeepsItsAngularMomentum()
        {
            // In vacuum: the parts swinging round the centre of mass would otherwise feel real air drag.
            var s = new PhysicsScene { Gravity = Vec3.Zero, GroundEnabled = false, AirDensity = 0 };
            var a = s.AddBody(SceneBody.Box("a", new Vec3(0.3, 0.05, 0.02), 0.3, new Vec3(0, 0, 1)));
            var b = s.AddBody(SceneBody.Box("b", new Vec3(0.02, 0.02, 0.15), 0.1, new Vec3(0.1, 0, 1.085)));
            Weld(s, a, b, new Vec3(0.1, 0, 1.01));
            var w0 = new Vec3(3, 1, 7);   // off every principal axis: the group tumbles
            foreach (var body in new[] { a, b })
            {
                body.AngularVelocity = w0;
                body.AngularDrag = 0;
            }
            var com = (a.Position * a.Mass + b.Position * b.Mass) / (a.Mass + b.Mass);
            foreach (var body in new[] { a, b }) body.Velocity = w0.Cross(body.Position - com);
            Vec3 L()
            {
                var c = (a.Position * a.Mass + b.Position * b.Mass) / (a.Mass + b.Mass);
                var sum = Vec3.Zero;
                foreach (var body in new[] { a, b })
                    sum += Mat3.RotatedDiagonal(body.Orientation * body.PrincipalAxes, body.Inertia) * body.AngularVelocity + (body.Position - c).Cross(body.Velocity * body.Mass);
                return sum;
            }

            var l0 = L();
            for (int i = 0; i < 2000; i++) s.Step(1e-3);
            var l1 = L();
            _out.WriteLine($"|L| {l0.Length:E5} → {l1.Length:E5} ({(l1.Length / l0.Length - 1) * 100:F2} %), direction off by {Math.Acos(Math.Clamp(l0.Normalized().Dot(l1.Normalized()), -1, 1)) * 180 / Math.PI:F2}°; ω {w0} → {a.AngularVelocity}");
            // The implicit gyroscopic step damps a fast tumble a little: a single body loses ≈ 0.75 % in 2 s at this step.
            Assert.True((l1 - l0).Length < 0.015 * l0.Length, "no torque, (almost) no change of angular momentum");
            Assert.True(Math.Acos(Math.Clamp(l0.Normalized().Dot(l1.Normalized()), -1, 1)) * 180 / Math.PI < 0.5, "and it keeps its direction");
            Assert.True((a.AngularVelocity - w0).Length > 0.1, "but the rate vector moves (free tumbling)");
        }
    }
}
