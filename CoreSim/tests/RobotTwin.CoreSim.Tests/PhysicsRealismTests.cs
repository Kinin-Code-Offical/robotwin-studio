using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[phys] Cables as ropes, tyre slip, servo torque limits, supports, sleeping, CCD.</summary>
    public class PhysicsRealismTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public PhysicsRealismTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>Bodies from their size; SG90 23×12×22.7 mm with the horn 5.4 mm off-centre, 31 mm up; TT motor as in WorldPhysicsTests.</summary>
        private static WorldBodyGeometry Geometry(WorldObject o)
        {
            if (o.Kind == WorldObjectKind.Body)
            {
                var g = new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = 0.05 };
                if (o.Shape == "wheel")
                {
                    g.Min = new Vec3(-o.SizeX / 2, -o.SizeZ / 2, 0);
                    g.Max = new Vec3(o.SizeX / 2, o.SizeZ / 2, o.SizeX);
                    g.Shape = "cylinder-y";
                    g.MassKg = 0.03;
                }
                else if (o.Shape == "sphere")
                {
                    g.Shape = "sphere";
                    g.Max = new Vec3(o.SizeX / 2, o.SizeX / 2, o.SizeX);
                    g.Min = new Vec3(-o.SizeX / 2, -o.SizeX / 2, 0);
                }

                return g;
            }

            if (o.PartId == "dc-motor")
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0185, -0.01125, 0), Max = new Vec3(0.0185, 0.01125, 0.0188), MassKg = 0.03,
                    PortName = "shaft", PortPivot = new Vec3(-0.011, 0.0185, 0.0094), PortAxis = Vec3.UnitY
                };
            }

            if (o.PartId == "servo")
            {
                return new WorldBodyGeometry
                {
                    Min = new Vec3(-0.0114, -0.0061, 0), Max = new Vec3(0.0114, 0.0061, 0.0227), MassKg = 0.009,
                    PortName = "horn", PortPivot = new Vec3(-0.0054, 0, 0.031), PortAxis = Vec3.UnitZ
                };
            }

            return new WorldBodyGeometry { Min = new Vec3(-0.03, -0.02, 0), Max = new Vec3(0.03, 0.02, 0.015), MassKg = 0.05 };
        }

        // ================================================================ cables

        [Fact]
        public void RopeDrapesOverABoxWithoutPassingThroughIt()
        {
            var scene = new PhysicsScene();
            scene.AddBody(SceneBody.StaticBox("box", new Vec3(0.08, 0.08, 0.05), new Vec3(0, 0, 0.025), Quat.Identity));
            var a = new RopeEnd { Local = new Vec3(-0.10, 0, 0.07), Exit = new Vec3(1, 0, 0) };
            var b = new RopeEnd { Local = new Vec3(0.10, 0, 0.07), Exit = new Vec3(-1, 0, 0) };
            var rope = new CableRope("C1", a, b, 0.32, 24);
            // Laid as an arch above the box, the cable falls onto it and hangs down both sides.
            for (int i = 0; i < 1500; i++) rope.Step(scene, 1e-3, false);
            var pts = rope.Points;
            var over = pts.Where(p => Math.Abs(p.X) < 0.035 && Math.Abs(p.Y) < 0.035).ToList();
            _out.WriteLine($"{rope.Segments} segments, max penetration {rope.MaxPenetration * 1000:F2} mm, length {rope.CurrentLength() * 1000:F0}/{rope.Length * 1000:F0} mm, " +
                           $"points over the box at z {string.Join(" ", over.Select(p => (p.Z * 1000).ToString("F1")))} mm");
            Assert.True(rope.MaxPenetration < 0.0006, "no particle may sink into the box or the floor");
            Assert.NotEmpty(over);
            Assert.All(over, p => Assert.True(p.Z > 0.05, "the cable lies on the box top, not inside it"));
            Assert.All(pts, p => Assert.True(p.Z > 0, "and never under the floor"));
            Assert.InRange(rope.CurrentLength(), rope.Length * 0.98, rope.Length * 1.03);
        }

        [Fact]
        public void RelaxedCableRestsOnTheFloorBetweenTwoPartsAndKeepsItsLength()
        {
            var scene = new PhysicsScene();
            // Plugs pointing towards each other (a lead coming out of a part's side).
            var a = new RopeEnd { Local = new Vec3(-0.1, 0, 0.02), Exit = new Vec3(1, 0, 0) };
            var b = new RopeEnd { Local = new Vec3(0.1, 0, 0.02), Exit = new Vec3(-1, 0, 0) };
            var rope = new CableRope("C", a, b, CableRope.DefaultLength(0.2), 24);
            rope.Relax(scene, 300);
            double minZ = rope.Points.Min(p => p.Z);
            _out.WriteLine($"lowest point {minZ * 1000:F2} mm, radius {rope.Radius * 1000:F2} mm, length {rope.CurrentLength() * 1000:F0}/{rope.Length * 1000:F0} mm");
            Assert.InRange(minZ, rope.Radius - 0.0003, rope.Radius + 0.002);
            Assert.InRange(rope.CurrentLength(), rope.Length * 0.98, rope.Length * 1.03);
        }

        [Fact]
        public void TautCablePullsOnItsPlugsAndUnplugsOpeningTheCircuit()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.05 });
            bat.IsStatic = true;
            var cart = w.AddComponent(Lib, "motor-tt", new WorldPose { X = 0.05 });
            cart.MassKg = 0.2;
            var cable = w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = cart.Id, Pin = "A" }, 24);
            cable.Length = 0.16;
            var wc = WorldCompiler.Compile(w, Lib, c => c.Length);
            var p = WorldPhysics.Build(w, Geometry, wc);
            p.AttachCables(w, c => new CablePins(new Vec3(0, 0, 0.015), new Vec3(0, 0, 0.03)), 200);
            var body = p.Scene.FindBody(cart.Id)!;
            var rope = p.RopeOf(cable.Id)!;
            double peak = 0;
            bool unplugged = false;
            PhysicsEvent? ev = null;
            for (int i = 0; i < 2000 && !unplugged; i++)
            {
                // Someone drags the part away with 8 N.
                body.AddForce(new Vec3(8, 0, 0));
                p.Advance(1e-3, null);
                peak = Math.Max(peak, rope.B.Tension);
                ev = p.DrainEvents().FirstOrDefault(e => e.Kind == PhysicsEventKind.Detached) ?? ev;
                unplugged = !rope.B.Attached || !rope.A.Attached;
            }

            var wire = wc.CableDevices[cable.Id];
            _out.WriteLine($"peak tension {peak:F2} N, pull-out {rope.PullOutForce:F1} N, event: {ev?.Message}, wire {wire.Resistance:G3} Ω");
            Assert.True(unplugged, "a jumper pulled with 8 N must come off its pin");
            Assert.NotNull(ev);
            Assert.True(wire.Resistance > 1e9, "the unplugged cable no longer conducts");
            Assert.True(peak > rope.PullOutForce * 0.9);
        }

        [Fact]
        public void SlackCableNeitherPullsNorUnplugs()
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.05 });
            var cart = w.AddBody("box", 0.05, 0.05, 0.03, new WorldPose { X = 0.06 });
            var cable = w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = cart.Id, Pin = "X" }, 24);
            var p = WorldPhysics.Build(w, Geometry, null);
            p.AttachCables(w, c => new CablePins(new Vec3(0, 0, 0.015), new Vec3(0, 0, 0.03)), 200);
            for (int i = 0; i < 1000; i++) p.Advance(1e-3, null);
            var rope = p.RopeOf(cable.Id)!;
            var moved = p.PoseOf(cart.Id)!.Value.Position - new Vec3(0.06, 0, 0);
            _out.WriteLine($"tension {rope.A.Tension * 1000:F1} / {rope.B.Tension * 1000:F1} mN, cart moved {moved.Length * 1000:F2} mm");
            Assert.True(rope.A.Attached && rope.B.Attached);
            Assert.True(moved.Length < 0.001, "a slack cable's own weight does not drag a part across the table");
        }

        // ================================================================ wheels on the floor

        private static (double Slip, double Speed) SpinWheelOn(string floor, double torque)
        {
            var scene = new PhysicsScene { GroundMaterial = floor };
            // 65 mm rubber tyre carrying a 0.25 kg share of a small robot (lumped into the wheel).
            var wheel = SceneBody.Cylinder("wheel", 0.0325, 0.026, 0.25, new Vec3(0, 0, 0.0325), "rubber");
            wheel.Orientation = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);   // axle along world y
            scene.AddBody(wheel);
            for (int i = 0; i < 500; i++)
            {
                wheel.AddTorque(new Vec3(0, torque, 0));   // +y spin rolls toward +x
                scene.Step(1e-3);
            }

            double spin = wheel.AngularVelocity.Y * 0.0325;
            double v = wheel.Velocity.X;
            return (spin > 1e-6 ? (spin - v) / spin : 0, v);
        }

        [Fact]
        public void DrivenWheelSlipsOnIceButRollsOnConcrete()
        {
            // TT motor + 1:48 gearbox stall ≈ 0.08 N·m; normal load 2.45 N.
            var ice = SpinWheelOn("ice", 0.08);
            var concrete = SpinWheelOn("concrete", 0.03);
            _out.WriteLine($"ice: slip {ice.Slip:P0}, v {ice.Speed:F2} m/s; concrete: slip {concrete.Slip:P1}, v {concrete.Speed:F2} m/s");
            Assert.True(ice.Slip > 0.5, "on ice the tyre spins (μ ≈ 0.08–0.15 cannot carry the torque)");
            Assert.True(concrete.Slip < 0.05, "on concrete it rolls");
            Assert.True(concrete.Speed > ice.Speed);
        }

        [Fact]
        public void CarStallsAgainstAWallAndItsMotorsDrawMoreCurrent()
        {
            double Run(bool wall, out double current)
            {
                var w = WorldExamples.Car(Lib);
                if (wall) w.Environment.Obstacles.Add(new WorldObstacle { X = 0.16, Y = 0, SizeX = 0.04, SizeY = 0.4, Height = 0.1 });
                var plate = w.Objects[0];
                var wc = WorldCompiler.Compile(w, Lib, c => 0.12);
                var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
                sim.Initialize(InitialState.OperatingPoint);
                var p = WorldPhysics.Build(w, Geometry, wc);
                var start = p.PoseOf(plate.Id)!.Value.Position;
                for (int i = 0; i < 2000; i++)
                {
                    sim.Step(1e-3);
                    p.Advance(1e-3, sim);
                }

                var motors = w.Objects.Where(o => o.PartId == "dc-motor").Select(o => wc.Compiled.DevicesOf(wc.ComponentRef(o.Id)!).OfType<DcMotor>().Single()).ToList();
                current = motors.Average(m => Math.Abs(m.Current));
                var d = p.PoseOf(plate.Id)!.Value.Position - start;
                return Math.Sqrt(d.X * d.X + d.Y * d.Y);
            }

            double free = Run(false, out double iFree);
            double blocked = Run(true, out double iBlocked);
            _out.WriteLine($"free: {free * 1000:F0} mm at {iFree * 1000:F0} mA; against a wall: {blocked * 1000:F0} mm at {iBlocked * 1000:F0} mA");
            Assert.True(free > 0.25);
            Assert.True(blocked < 0.09, "the wall (front at 14 cm) stops the car");
            Assert.True(iBlocked > iFree * 1.3, "a blocked robot loads its motors");
        }

        // ================================================================ support and gravity

        [Fact]
        public void UnsupportedPartsFallAndRestOnWhatIsBelowThem()
        {
            var w = new WorldDocument();
            var low = w.AddBody("box", 0.1, 0.1, 0.04, new WorldPose { Z = 0.02 });
            var high = w.AddBody("box", 0.05, 0.05, 0.02, new WorldPose { X = 0.01, Z = 0.15 });
            var off = w.AddBody("box", 0.03, 0.03, 0.03, new WorldPose { X = 0.3, Z = 0.1 });
            var p = WorldPhysics.Build(w, Geometry, null);
            for (int i = 0; i < 1500; i++)
            {
                p.Advance(1e-3, null);
                if (i % 100 == 0) _out.WriteLine($"t {i} off z {p.PoseOf(off.Id)!.Value.Position.Z * 1000:F2} up {p.PoseOf(off.Id)!.Value.Orientation.Rotate(Vec3.UnitZ)} sleep {p.Scene.IsSleeping(p.Scene.FindBody(off.Id)!)}");
            }

            double zl = p.PoseOf(low.Id)!.Value.Position.Z, zh = p.PoseOf(high.Id)!.Value.Position.Z, zo = p.PoseOf(off.Id)!.Value.Position.Z;
            _out.WriteLine($"lower box base {zl * 1000:F1} mm, upper box base {zh * 1000:F1} mm (on 40 mm), loose box {zo * 1000:F1} mm");
            Assert.InRange(zl, -0.002, 0.002);
            Assert.InRange(zh, 0.038, 0.042);
            Assert.InRange(zo, -0.002, 0.002);
        }

        /// <summary>
        /// The servo-arm layout (SG90 on a base, arm, second SG90, arm) with a free base: a
        /// 17 g 60×40×12 mm wooden block cannot hold the outstretched arm (its centre of mass lies
        /// beyond the base edge), a 150 g 140×100×18 mm plywood base can.
        /// </summary>
        [Theory]
        [InlineData(0.06, 0.04, 0.012, 0.017, false)]
        [InlineData(0.14, 0.10, 0.018, 0.150, true)]
        public void ServoArmBaseHoldsOrTipsByItsSupportPolygon(double sx, double sy, double sz, double mass, bool holds)
        {
            var w = new WorldDocument();
            var bas = w.AddBody("box", sx, sy, sz, new WorldPose { X = 0.04 }, "AHŞAP");
            bas.MassKg = mass;
            var s1 = w.AddComponent(Lib, "servo-sg90", new WorldPose { X = 0.04, Z = sz });
            double pivotX = s1.Pose.X - 0.0054, pivotZ = s1.Pose.Z + 0.031;
            var arm1 = w.AddBody("box", 0.11, 0.018, 0.003, new WorldPose { X = pivotX + 0.045, Z = pivotZ + 0.0002 });
            arm1.MassKg = 0.0074;
            var s2 = w.AddComponent(Lib, "servo-sg90", new WorldPose { X = pivotX + 0.085, Z = arm1.Pose.Z + 0.003 });
            double pivot2X = s2.Pose.X - 0.0054, pivot2Z = s2.Pose.Z + 0.031;
            var arm2 = w.AddBody("box", 0.08, 0.014, 0.003, new WorldPose { X = pivot2X + 0.032, Z = pivot2Z + 0.0002 });
            arm2.MassKg = 0.0042;
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = s1.Id, ObjectB = bas.Id });
            w.Joints.Add(new WorldJoint { Id = "J2", ObjectA = arm1.Id, ObjectB = s1.Id, PortB = "horn" });
            w.Joints.Add(new WorldJoint { Id = "J3", ObjectA = s2.Id, ObjectB = arm1.Id });
            w.Joints.Add(new WorldJoint { Id = "J4", ObjectA = arm2.Id, ObjectB = s2.Id, PortB = "horn" });
            var p = WorldPhysics.Build(w, Geometry, null);
            // Unpowered servos would let the arm swing; lock both horns as a powered servo holding still does.
            foreach (var j in p.Scene.Joints.Where(j => j.Kind == JointKind.Hinge))
            {
                j.Drive = JointDrive.Position;
                j.Stiffness = 5;
                j.Damping = 0.02;
                j.MaxTorque = 0.18;
            }

            for (int i = 0; i < 2000; i++) p.Advance(1e-3, null);
            var q = p.PoseOf(bas.Id)!.Value.Orientation;
            double tilt = Math.Acos(Math.Max(-1, Math.Min(1, q.Rotate(Vec3.UnitZ).Z))) * 180 / Math.PI;
            _out.WriteLine($"base {sx * 1000:F0}×{sy * 1000:F0}×{sz * 1000:F0} mm, {mass * 1000:F0} g: tilt {tilt:F1}°");
            if (holds) Assert.True(tilt < 2, "a heavy, wide base keeps the arm up");
            else Assert.True(tilt > 10, "a light small base tips over");
        }

        // ================================================================ servo torque

        private double HoldArm(double armMass, out double current)
        {
            var w = new WorldDocument();
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.2 });
            // The servo lies on its side on a stand: the horn axis is horizontal, the arm is loaded by gravity.
            var servo = w.AddComponent(Lib, "servo-sg90", new WorldPose { Z = 0.1, Roll = 90 });
            servo.IsStatic = true;
            var pivot = servo.Pose.ToWorld(new Vec3(-0.0054, 0, 0.031));
            var arm = w.AddBody("box", 0.10, 0.012, 0.004, new WorldPose { X = pivot.X + 0.05, Y = pivot.Y - 0.006, Z = pivot.Z - 0.002 });
            arm.MassKg = armMass;
            w.Joints.Add(new WorldJoint { Id = "J1", ObjectA = arm.Id, ObjectB = servo.Id, PortB = "horn" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = servo.Id, Pin = "GND" });
            var wc = WorldCompiler.Compile(w, Lib, c => 0.2);
            var s = wc.Compiled.DevicesOf(wc.ComponentRef(servo.Id)!).OfType<Servo>().Single();
            var sim = new AnalogSimulator(wc.Compiled.Circuit, new AnalogOptions { MaxStep = 1e-3 });
            sim.Initialize(InitialState.OperatingPoint);
            var p = WorldPhysics.Build(w, Geometry, wc);
            typeof(Servo).GetProperty("TargetAngleDeg")!.SetValue(s, 90.0);   // hold the arm level
            for (int i = 0; i < 1500; i++)
            {
                sim.Step(1e-3);
                p.Advance(1e-3, sim);
            }

            current = s.SupplyCurrent;
            return Math.Abs(p.PortAngle(servo.Id)) * 180 / Math.PI;
        }

        [Fact]
        public void Sg90HoldsALightArmButAnOverloadedOneSags()
        {
            // SG90 stall ≈ 1.8 kg·cm = 0.177 N·m at 4.8 V (≈ 0.22 N·m on a 6 V pack, plus ≈ 0.07 N·m of gear
            // friction that resists back-driving). 60 g at 5 cm = 0.03 N·m; 800 g at 5 cm = 0.39 N·m.
            double light = HoldArm(0.06, out double iLight);
            double heavy = HoldArm(0.80, out double iHeavy);
            _out.WriteLine($"60 g arm: off by {light:F1}° at {iLight * 1000:F0} mA; 800 g arm: sagged {heavy:F1}° at {iHeavy * 1000:F0} mA");
            Assert.True(light < 6, "within the servo's torque the arm is held near the command");
            Assert.True(heavy > 30, "beyond the stall torque the arm sags");
            Assert.True(iHeavy > iLight, "and the stalled servo draws more current");
        }

        // ================================================================ nothing acts before start

        [Fact]
        public void BuildingTheWorldAndLayingItsCablesMovesNothing()
        {
            var w = WorldExamples.Car(Lib);
            var poses = w.Objects.ToDictionary(o => o.Id, o => o.Pose.Clone());
            var wc = WorldCompiler.Compile(w, Lib, c => 0.12);
            var p = WorldPhysics.Build(w, Geometry, wc);
            p.AttachCables(w, c => new CablePins(new Vec3(0, 0, 0.01), new Vec3(0, 0, 0.01)), 300);
            foreach (var o in w.Objects)
            {
                var pose = p.PoseOf(o.Id)!.Value;
                Assert.True((pose.Position - poses[o.Id].Position).Length < 1e-9, $"{o.Name} moved before the start");
                Assert.Equal(0.0, p.PortAngle(o.Id));
                Assert.True(o.Pose.X == poses[o.Id].X && o.Pose.Z == poses[o.Id].Z);
            }

            foreach (var b in p.Scene.Bodies) Assert.True(b.Velocity.Length == 0 && b.AngularVelocity.Length == 0);
            Assert.Equal(0.0, p.Scene.Time);
            Assert.Empty(p.DrainEvents());
        }

        // ================================================================ engine

        [Fact]
        public void RestingBodiesFallAsleepAndWakeWhenHit()
        {
            var scene = new PhysicsScene { SleepEnabled = true };
            var box = scene.AddBody(SceneBody.Box("box", new Vec3(0.05, 0.05, 0.05), 0.1, new Vec3(0, 0, 0.025), "wood"));
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            Assert.True(scene.IsSleeping(box), "a box at rest goes to sleep");
            double z = box.Position.Z;
            for (int i = 0; i < 1000; i++) scene.Step(1e-3);
            Assert.Equal(z, box.Position.Z);   // no creeping while asleep

            var ball = scene.AddBody(SceneBody.Sphere("ball", 0.01, 0.2, new Vec3(-0.2, 0, 0.025), "steel"));
            ball.Velocity = new Vec3(2, 0, 0);
            for (int i = 0; i < 300; i++) scene.Step(1e-3);
            _out.WriteLine($"box pushed to x = {box.Position.X * 1000:F1} mm, asleep {scene.IsSleeping(box)}");
            Assert.True(box.Position.X > 0.002, "the ball wakes the box and pushes it");
        }

        [Fact]
        public void FastSmallBallDoesNotTunnelThroughAThinPlate()
        {
            var scene = new PhysicsScene { GroundEnabled = false, Gravity = Vec3.Zero };
            scene.AddBody(SceneBody.StaticBox("plate", new Vec3(0.002, 0.2, 0.2), new Vec3(0, 0, 0), Quat.Identity, "aluminium"));
            var ball = scene.AddBody(SceneBody.Sphere("ball", 0.002, 0.001, new Vec3(-0.05, 0, 0), "steel"));
            ball.Velocity = new Vec3(60, 0, 0);
            for (int i = 0; i < 20; i++) scene.Step(1e-3);
            _out.WriteLine($"ball at x = {ball.Position.X * 1000:F1} mm, vx {ball.Velocity.X:F1} m/s");
            Assert.True(ball.Position.X < 0, "the ball bounces off the 2 mm plate instead of passing through");
        }

        [Fact]
        public void SpinningRubberBallStopsSpinningOnTheFloor()
        {
            var scene = new PhysicsScene { GroundMaterial = "wood" };
            var ball = scene.AddBody(SceneBody.Sphere("ball", 0.02, 0.05, new Vec3(0, 0, 0.02), "rubber"));
            ball.AngularVelocity = new Vec3(0, 0, 30);
            for (int i = 0; i < 3000; i++) scene.Step(1e-3);
            _out.WriteLine($"spin after 3 s: {ball.AngularVelocity.Z:F2} rad/s");
            Assert.True(Math.Abs(ball.AngularVelocity.Z) < 1, "torsional friction of the contact patch stops the spin");
        }

        [Fact]
        public void FloorGripKeepsTheMaterialPairs()
        {
            var scene = new PhysicsScene { GroundMaterial = "wood", GroundGrip = 0.9 };
            var rubber = scene.GroundProperties("rubber", 0, 0);
            var pla = scene.GroundProperties("pla", 0, 0);
            var steel = scene.GroundProperties("steel", 0, 0);
            _out.WriteLine($"rubber {rubber}, pla {pla}, steel {steel}");
            Assert.Equal(0.9, rubber.StaticFriction, 6);
            Assert.True(pla.StaticFriction < rubber.StaticFriction * 0.7);
            Assert.True(steel.RollingResistance < rubber.RollingResistance);
        }
    }
}
