using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[route] Wires run where a real one can: around parts, through openings, never through a wall.</summary>
    public class CableRouteTests
    {
        private readonly ITestOutputHelper _out;

        public CableRouteTests(ITestOutputHelper output) => _out = output;

        private static WorldBodyGeometry Geometry(WorldObject o) =>
            new WorldBodyGeometry { Min = new Vec3(-o.SizeX / 2, -o.SizeY / 2, 0), Max = new Vec3(o.SizeX / 2, o.SizeY / 2, o.SizeZ), MassKg = o.MassKg ?? 0.02 };

        /// <summary>A 160×60×40 mm box, 2 mm walls, lid screwed on: two blocks inside, one outside behind it.</summary>
        private static (WorldDocument W, WorldObject Shell, WorldObject In1, WorldObject In2, WorldObject Out) Box(string? holes)
        {
            var w = new WorldDocument();
            var s = w.AddBody("box", 0.16, 0.06, 0.04, new WorldPose(), "PLA");
            s.Name = "Kutu";
            s.IsStatic = true;
            s.Parameters["shell"] = "true";
            s.Parameters["wall"] = "0.002";
            if (holes != null) s.Parameters["holes"] = holes;
            var lid = w.AddBody("box", 0.16, 0.06, 0.002, new WorldPose { Z = 0.04 }, "PLA");
            lid.Name = "Kapak";
            lid.IsStatic = true;
            lid.Parameters["lid.of"] = s.Id;
            lid.Parameters["screwed"] = "true";
            var in1 = w.AddBody("box", 0.03, 0.03, 0.01, new WorldPose { X = 0.05, Z = 0.002 }, "PLA");
            in1.Name = "Kart";
            in1.IsStatic = true;
            var in2 = w.AddBody("box", 0.03, 0.03, 0.01, new WorldPose { X = -0.05, Z = 0.002 }, "PLA");
            in2.Name = "Sürücü";
            in2.IsStatic = true;
            var outside = w.AddBody("box", 0.03, 0.03, 0.03, new WorldPose { X = -0.16 }, "PLA");
            outside.Name = "Motor";
            outside.IsStatic = true;
            return (w, s, in1, in2, outside);
        }

        private static CablePins? Pins(WorldDocument w, WorldCable c)
        {
            // Pins on the top face of each block.
            Vec3 Top(string id) => new Vec3(0, 0, w.Find(id)!.SizeZ);
            return new CablePins(Top(c.A.ObjectId), Top(c.B.ObjectId));
        }

        /// <summary>
        /// A propeller's swept disc lies across the straight way between two plugs: the route goes around it (a spinning
        /// prop would cut a wire through it), and a wire laid there and left to settle stays out of it.
        /// </summary>
        [Fact]
        public void AWireKeepsOutOfAPropellersDisc()
        {
            var scene = new PhysicsScene();
            Vec3 centre = new Vec3(0, 0, 0.03), axis = Vec3.UnitZ;
            const double radius = 0.05, half = 0.008;
            Vec3? Push(Vec3 x, double clearance)
            {
                var d = x - centre;
                double along = d.Dot(axis);
                var radial = d - axis * along;
                if (Math.Abs(along) >= half + clearance || radial.Length >= radius + clearance) return null;
                double up = half + clearance - along, down = -half - clearance - along, outward = radius + clearance - radial.Length;
                double vertical = Math.Abs(up) < Math.Abs(down) ? up : down;
                return Math.Abs(vertical) <= outward ? axis * vertical : radial.Normalized() * outward;
            }

            bool Inside(Vec3 x, double clearance) => Push(x, clearance) != null;
            var from = new Vec3(-0.09, 0, 0.03);
            var to = new Vec3(0.09, 0, 0.03);
            var route = CableRouter.Route(scene, from, to, 0.001, null, 250_000, Inside);
            Assert.NotNull(route);
            for (int i = 1; i < route!.Count; i++)
                for (int k = 0; k <= 20; k++)
                {
                    var x = route[i - 1] + (route[i] - route[i - 1]) * (k / 20.0);
                    Assert.False(Inside(x, 0.0005), $"route point ({x.X:F3}, {x.Y:F3}, {x.Z:F3}) inside the disc");
                }

            _out.WriteLine($"around the disc: {CableRouter.Length(route) * 1000:F0} mm (straight 180 mm)");
            Assert.True(CableRouter.Length(route) > 0.18);

            // A wire laid straight through the disc and settled under gravity: the keep-out moves it out.
            var rope = new CableRope("w", new RopeEnd { Local = from, Exit = Vec3.UnitX }, new RopeEnd { Local = to, Exit = -Vec3.UnitX }, 0.2, 24) { KeepOut = Push };
            rope.LayAlong(new[] { new Vec3(0, 0, 0.03) });
            Assert.True(rope.KeepOutDepth() > 0);
            rope.Relax(scene, 300);
            Assert.True(rope.KeepOutDepth() < 1e-4, $"after settling the wire still lies {rope.KeepOutDepth() * 1000:F2} mm in the disc");
        }

        [Fact]
        public void AWireBetweenTwoPartsInAClosedBoxStaysInside()
        {
            var (w, shell, in1, in2, _) = Box(null);
            var cable = w.Connect(new CableEnd { ObjectId = in1.Id, Pin = "1" }, new CableEnd { ObjectId = in2.Id, Pin = "1" }, 24);
            var p = WorldPhysics.Build(w, Geometry, null);
            p.AttachCables(w, c => Pins(w, c), 300);
            for (int i = 0; i < 500; i++) p.Advance(1e-3, null);
            var pts = p.CablePoints(cable.Id)!;
            var issues = p.CableCrossings().Concat(p.ShellFit(w).Where(x => x.Kind == "delik").Select(x => new ConsistencyIssue(x.Kind, x.ObjectId, "", 0, x.Message))).ToList();
            foreach (var x in issues) _out.WriteLine(x.Message);
            double maxY = pts.Max(q => Math.Abs(q.Y)), maxZ = pts.Max(q => q.Z), maxX = pts.Max(q => Math.Abs(q.X));
            _out.WriteLine($"wire within |x| ≤ {maxX * 1000:F1}, |y| ≤ {maxY * 1000:F1}, z ≤ {maxZ * 1000:F1} mm; length {p.RopeOf(cable.Id)!.Length * 1000:F0} mm");
            Assert.Empty(issues);
            Assert.True(maxY < 0.028 && maxZ < 0.040 && maxX < 0.078, "the wire stays in the box's 156×56×38 mm cavity");
            Assert.Empty(p.BlockedCables);
        }

        [Fact]
        public void AWireLeavingABoxGoesOutThroughItsHole()
        {
            var (w, shell, _, in2, outside) = Box("-x");
            var cable = w.Connect(new CableEnd { ObjectId = in2.Id, Pin = "1" }, new CableEnd { ObjectId = outside.Id, Pin = "1" }, 24);
            var p = WorldPhysics.Build(w, Geometry, null);
            p.AttachCables(w, c => Pins(w, c), 300);
            for (int i = 0; i < 500; i++) p.Advance(1e-3, null);
            var pts = p.CablePoints(cable.Id)!;
            // Where it crosses the back wall's plane (x = −80 mm): inside the 10 mm opening centred at mid-height.
            int k = Enumerable.Range(1, pts.Count - 1).First(i => (pts[i - 1].X + 0.08) * (pts[i].X + 0.08) <= 0);
            var a = pts[k - 1];
            var b = pts[k];
            var at = a + (b - a) * ((-0.08 - a.X) / (b.X - a.X));
            var (size, centreZ) = WorldPhysics.HoleOf(shell, new Vec3(0.16, 0.06, 0.04), 1);
            _out.WriteLine($"crosses the back wall at y {at.Y * 1000:F1} mm, z {at.Z * 1000:F1} mm (hole {size * 1000:F0} mm at z {centreZ * 1000:F0} mm); length {p.RopeOf(cable.Id)!.Length * 1000:F0} mm");
            foreach (var x in p.CableCrossings()) _out.WriteLine(x.Message);
            foreach (var x in p.ShellFit(w)) _out.WriteLine(x.Message);
            Assert.Empty(p.CableCrossings());
            Assert.True(Math.Abs(at.Y) < size / 2 && Math.Abs(at.Z - centreZ) < size / 2, "out through the opening");
            Assert.DoesNotContain(p.ShellFit(w), x => x.Kind == "delik");
        }

        [Fact]
        public void AWireIntoAClosedBoxHasNoWayAndIsReported()
        {
            var (w, _, _, in2, outside) = Box(null);
            var cable = w.Connect(new CableEnd { ObjectId = in2.Id, Pin = "1" }, new CableEnd { ObjectId = outside.Id, Pin = "1" }, 24);
            var p = WorldPhysics.Build(w, Geometry, null);
            p.AttachCables(w, c => Pins(w, c), 100);
            var issues = p.CableCrossings();
            foreach (var x in issues) _out.WriteLine($"{x.Kind}: {x.Message}");
            Assert.Contains(cable.Id, p.BlockedCables);
            Assert.Contains(issues, x => x.Kind == "yol yok");
        }

        [Fact]
        public void WiresStayInsideABoxFlyingFastAndTurning()
        {
            // The closed box, its parts and wiring as one flying airframe: 12 m/s, climbing, rolling a little, under
            // gravity. The wires inside must stay inside, as in a plane's fuselage.
            var (w, shell, in1, in2, _) = Box(null);
            foreach (var o in new[] { shell, in1, in2 }) o.IsStatic = false;
            var lid = w.Objects.First(o => WorldPhysics.LidOf(o) != null);
            lid.IsStatic = false;
            foreach (var o in new[] { in1, in2 })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = shell.Id });
            var c1 = w.Connect(new CableEnd { ObjectId = in1.Id, Pin = "1" }, new CableEnd { ObjectId = in2.Id, Pin = "1" }, 26);
            var p = WorldPhysics.Build(w, Geometry, null);
            p.Scene.GroundEnabled = false;
            p.AttachCables(w, c => Pins(w, c), 300);
            var frame = p.Scene.FindBody(shell.Id)!;
            for (int i = 0; i < 2000; i++)
            {
                // Thrust and lift hold it on a gentle climbing turn: 12 m/s, rolling at 0.3 rad/s.
                foreach (var b in p.Scene.Bodies)
                {
                    if (b.IsStatic) continue;
                    if (i == 0)
                    {
                        b.Velocity = new Vec3(12, 0, 3);
                        b.AngularVelocity = new Vec3(0.3, 0, 0);
                    }

                    b.AddForce(-p.Scene.Gravity * b.Mass * 1.1);
                }

                p.Advance(1e-3, null);
            }

            var issues = p.CableCrossings();
            foreach (var x in issues) _out.WriteLine(x.Message);
            _out.WriteLine($"box at {frame.Position}, v {frame.Velocity}; cable step {p.CableSeconds * 1000:F0} ms for 2 s");
            Assert.Empty(issues);
        }

        [Fact]
        public void WiresStayInsideAnAcceleratingBoxAndInItsHole()
        {
            // A take-off run: the box and everything fixed to it speeds up at 12 m/s² from rest. One wire lies inside, one
            // runs out through the back wall's hole to a part fixed behind it.
            var (w, shell, in1, in2, outside) = Box("-x");
            foreach (var o in new[] { shell, in1, in2, outside }) o.IsStatic = false;
            var lid = w.Objects.First(o => WorldPhysics.LidOf(o) != null);
            lid.IsStatic = false;
            outside.Pose.Z = 0.0;
            var arm = w.AddBody("box", 0.08, 0.02, 0.004, new WorldPose { X = -0.12, Z = -0.004 }, "PLA");
            foreach (var o in new[] { in1, in2, arm })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = shell.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = outside.Id, ObjectB = arm.Id });
            w.Connect(new CableEnd { ObjectId = in1.Id, Pin = "1" }, new CableEnd { ObjectId = in2.Id, Pin = "1" }, 26);
            w.Connect(new CableEnd { ObjectId = in1.Id, Pin = "2" }, new CableEnd { ObjectId = outside.Id, Pin = "1" }, 18);
            var p = WorldPhysics.Build(w, Geometry, null);
            p.Scene.GroundEnabled = false;
            p.AttachCables(w, c => Pins(w, c), 300);
            Assert.Empty(p.CableCrossings());
            int worst = 0;
            string first = "";
            for (int i = 0; i < 1500; i++)
            {
                foreach (var b in p.Scene.Bodies)
                    if (!b.IsStatic) b.AddForce((new Vec3(12, 0, 0) - p.Scene.Gravity) * b.Mass);
                p.Advance(1e-3, null);
                if (i % 50 != 49) continue;
                var now = p.CableCrossings();
                if (now.Count > 0 && first.Length == 0) _out.WriteLine($"first at t {(i + 1) * 1e-3:F2} s, v {p.Scene.FindBody(shell.Id)!.Velocity.X:F1} m/s: {now[0].Message}; carried {p.Ropes.Count(x => x.Carrying)}");
                if (now.Count > worst)
                {
                    worst = now.Count;
                    first = $"t {(i + 1) * 1e-3:F2} s: {now[0].Message}";
                }
            }

            var frame = p.Scene.FindBody(shell.Id)!;
            _out.WriteLine($"box at {frame.Velocity.X:F1} m/s; most crossings {worst} ({first})");
            Assert.Equal(0, worst);
        }

        [Fact]
        public void AFallingWireDoesNotTunnelThroughAThinPlate()
        {
            // A 1.2 m wire hung from two points 1 m up drops its slack at ≈ 3 m/s onto a 2 mm plate 0.6 m up: 6 mm per
            // 2 ms step, three times the plate's thickness. It must come to rest on it, not under it.
            var scene = new PhysicsScene { GroundEnabled = false };
            var plate = SceneBody.StaticBox("plate", new Vec3(1, 1, 0.002), new Vec3(0, 0, 0.6), Quat.Identity, "wood");
            scene.AddBody(plate);
            var a = new RopeEnd { Local = new Vec3(-0.05, 0, 1.0), Exit = -Vec3.UnitZ };
            var b = new RopeEnd { Local = new Vec3(0.05, 0, 1.0), Exit = -Vec3.UnitZ };
            var rope = new CableRope("C", a, b, 1.2, 24);
            rope.Lay(Enumerable.Range(0, 2).Select(i => new Vec3(i == 0 ? -0.05 : 0.05, 0, 1.0)).ToList());
            double lowest = double.MaxValue;
            for (int i = 0; i < 1000; i++)
            {
                rope.Step(scene, 0.002, false);
                lowest = Math.Min(lowest, rope.Points.Min(q => q.Z));
            }

            _out.WriteLine($"lowest point {lowest * 1000:F1} mm (plate top at 601 mm), wire radius {rope.Radius * 1000:F2} mm");
            Assert.True(lowest > 0.601 - 0.0005, "the wire lies on the plate");
        }

        [Fact]
        public void AShellWallWithAHoleIsAFrameAroundIt()
        {
            var w = new WorldDocument();
            var s = w.AddBody("box", 0.1, 0.06, 0.04, new WorldPose(), "PLA");
            s.Parameters["shell"] = "true";
            s.Parameters["holes"] = "+x";
            s.Parameters["hole"] = "0.012";
            var boxes = WorldPhysics.WallBoxes(s, new Vec3(0.1, 0.06, 0.04));
            Assert.Equal(4, boxes.Count(b => b.Wall == 0));   // left, right, below and above the opening
            Assert.Equal(1, boxes.Count(b => b.Wall == 1));
            double area = boxes.Where(b => b.Wall == 0).Sum(b => b.Size.Y * b.Size.Z);
            Assert.Equal(0.06 * 0.038 - 0.012 * 0.012, area, 9);
            var p = WorldPhysics.Build(w, Geometry, null);
            var front = p.WallsOf(s.Id)[0];
            Assert.Equal(4, front.Pieces!.Length);
        }
    }
}
