using System;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[rigidbody] Unity's Is Kinematic, and the centre of mass and inertia set by hand, in our solver.</summary>
    public class KinematicAndCentreTests
    {
        private static SceneBody Kinematic(SceneBody b, Vec3 velocity)
        {
            b.IsKinematic = true;
            b.IsStatic = true;
            b.Velocity = velocity;
            return b;
        }

        [Fact]
        public void AKinematicBodyKeepsItsOwnMotionAndPushesWhatItMeets()
        {
            var scene = new PhysicsScene();   // gravity on, ground at z = 0
            var pusher = Kinematic(SceneBody.Box("pusher", new Vec3(0.05, 0.2, 0.05), 5, new Vec3(-0.2, 0, 0.3)), new Vec3(0.2, 0, 0));
            var crate = SceneBody.Box("crate", new Vec3(0.05, 0.05, 0.05), 0.2, new Vec3(0, 0, 0.3 - 0.0));
            scene.AddBody(pusher);
            scene.AddBody(crate);
            crate.Position = new Vec3(0, 0, 0.025);   // on the ground
            pusher.Position = new Vec3(-0.2, 0, 0.03);
            for (int i = 0; i < 1500; i++) scene.Step(1e-3);   // 1.5 s at 0.2 m/s: 0.3 m

            Assert.InRange(pusher.Position.X, 0.0999, 0.1001);   // exactly where its speed took it, the crate in its way
            Assert.Equal(0.03, pusher.Position.Z, 9);             // gravity does not pull it
            Assert.Equal(0.2, pusher.Velocity.X, 12);             // nothing slowed it
            Assert.True(crate.Position.X > 0.1, $"crate at {crate.Position.X}");   // pushed ahead of it

            // a static wall does not stop it either
            var s2 = new PhysicsScene { Gravity = Vec3.Zero };
            var k = Kinematic(SceneBody.Box("k", new Vec3(0.05, 0.05, 0.05), 1, new Vec3(0, 0, 0.5)), new Vec3(0.5, 0, 0));
            s2.AddBody(k);
            s2.AddBody(SceneBody.StaticBox("wall", new Vec3(0.02, 0.5, 0.5), new Vec3(0.1, 0, 0.5), Quat.Identity));
            for (int i = 0; i < 400; i++) s2.Step(1e-3);
            Assert.InRange(k.Position.X, 0.1999, 0.2001);
        }

        [Fact]
        public void AKinematicBodyWakesASleepingOne()
        {
            var scene = new PhysicsScene();
            var crate = SceneBody.Box("crate", new Vec3(0.05, 0.05, 0.05), 0.2, new Vec3(0, 0, 0.025));
            scene.AddBody(crate);
            for (int i = 0; i < 3000; i++) scene.Step(1e-3);   // settles and falls asleep
            var pusher = Kinematic(SceneBody.Box("pusher", new Vec3(0.05, 0.2, 0.05), 5, new Vec3(-0.15, 0, 0.03)), new Vec3(0.2, 0, 0));
            scene.AddBody(pusher);
            for (int i = 0; i < 1500; i++) scene.Step(1e-3);
            Assert.True(crate.Position.X > 0.1, $"crate at {crate.Position.X}");
        }

        [Fact]
        public void TheCentreOfMassMovesWhileTheSolidStaysWhereItWas()
        {
            var box = SceneBody.Box("b", new Vec3(0.1, 0.1, 0.02), 1, new Vec3(0, 0, 0.5));
            var before = box.Shape!.WorldBounds(box.Position, box.Orientation);
            box.ShiftCentreOfMass(new Vec3(0.03, 0, -0.005));
            var after = box.Shape!.WorldBounds(box.Position, box.Orientation);
            Assert.Equal(0.53, box.Position.X + 0.5, 9);
            Assert.InRange((after.Min - before.Min).Length, 0, 1e-9);
            Assert.InRange((after.Max - before.Max).Length, 0, 1e-9);

            var ball = SceneBody.Sphere("s", 0.02, 1, new Vec3(0, 0, 0.5));
            ball.ShiftCentreOfMass(new Vec3(0, 0, -0.01));
            var bb = ball.Shape!.WorldBounds(ball.Position, ball.Orientation);
            Assert.InRange(bb.Max.Z, 0.5199, 0.5201);
            Assert.InRange(bb.Min.Z, 0.4799, 0.4801);
        }

        [Fact]
        public void APlankWithItsCentreOfMassPastItsEdgeTipsOver()
        {
            double Tilt(double comX)
            {
                var scene = new PhysicsScene();
                var plank = SceneBody.Box("p", new Vec3(0.1, 0.04, 0.1), 0.5, new Vec3(0, 0, 0.05));
                plank.ShiftCentreOfMass(new Vec3(comX, 0, 0));
                scene.AddBody(plank);
                for (int i = 0; i < 1500; i++) scene.Step(1e-3);
                var up = plank.Orientation.Rotate(Vec3.UnitZ);
                return Math.Acos(Math.Max(-1, Math.Min(1, up.Z))) * 180 / Math.PI;
            }

            Assert.InRange(Tilt(0.03), 0, 1);     // over its foot: it stands
            Assert.True(Tilt(0.07) > 30);         // past its edge: it falls over
        }

        [Fact]
        public void APartsParametersMakeItKinematicAndSetItsCentreAndInertia()
        {
            var w = new WorldDocument();
            var lift = new WorldObject { Id = w.NextId("O"), Name = "Asansör", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.1, SizeY = 0.1, SizeZ = 0.02, MassKg = 1, Pose = new WorldPose { Z = 0.2 } };
            lift.Parameters[WorldPhysics.KinematicKey] = "1";
            lift.Parameters[WorldPhysics.KinematicVelocityKey] = "0,0,0.1";
            w.Objects.Add(lift);
            var cart = new WorldObject { Id = w.NextId("O"), Name = "Araba", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.1, SizeY = 0.06, SizeZ = 0.04, MassKg = 0.5, Pose = new WorldPose { X = 1, Z = 0.3 } };
            cart.Parameters[WorldPhysics.RigidbodyCentreKey] = "0.02,0,-0.01";
            cart.Parameters[WorldPhysics.RigidbodyInertiaKey] = "0.001,0.002,0.003";
            w.Objects.Add(cart);
            var p = WorldPhysics.Build(w, new ApproximateGeometry(w, RobotTwin.CoreSim.Design.PartLibrary.Default).GeometryOf, null);
            var liftBody = p.Scene.Bodies.First(b => b.Tag == lift);
            var cartBody = p.Scene.Bodies.First(b => b.Tag == cart);
            Assert.True(liftBody.IsKinematic && liftBody.IsStatic);
            Assert.Equal(new Vec3(0.001, 0.002, 0.003), cartBody.Inertia);
            var start = p.PoseOf(cart.Id)!.Value.Position;
            Assert.InRange((start - new Vec3(1, 0, 0.3)).Length, 0, 1e-9);   // the part stays where it was put
            Assert.InRange((cartBody.Position - new Vec3(1.02, 0, 0.29)).Length, 0, 1e-9);   // its centre of mass where it was set

            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            Assert.InRange(p.PoseOf(lift.Id)!.Value.Position.Z, 0.2999, 0.3001);   // 1 s at 0.1 m/s, up
        }
    }
}
