using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [drive] Parts placed as in a real machine become one: shafts turn in their bearings, gears sit on their shafts and
    /// mesh, a lead screw drives the carriage its nut is bolted to; parts that hold nothing are called out.
    /// </summary>
    public class DriveAssemblyTests
    {
        private readonly ITestOutputHelper _out;

        public DriveAssemblyTests(ITestOutputHelper output) => _out = output;

        private static WorldObject Put(WorldDocument w, string id, double x, double y, double z, bool frame = false)
        {
            var o = DriveParts.Add(w, id, new WorldPose { X = x, Y = y, Z = z });
            o.IsStatic = frame;
            return o;
        }

        /// <summary>Two vertical shafts 30 mm apart, each in two 608 bearings bolted to the frame, a z20 driving a z40.</summary>
        private static (WorldDocument World, WorldObject ShaftA, WorldObject ShaftB) GearTrain()
        {
            var w = new WorldDocument();
            var a = Put(w, "shaft-8-100", 0, 0, 0.02);
            Put(w, "bearing-608zz", 0, 0, 0.025, true);
            Put(w, "bearing-608zz", 0, 0, 0.105, true);
            Put(w, "gear-m1-z20-b8", 0, 0, 0.06);
            var b = Put(w, "shaft-8-100", 0.03, 0, 0.02);
            Put(w, "bearing-608zz", 0.03, 0, 0.025, true);
            Put(w, "bearing-608zz", 0.03, 0, 0.105, true);
            Put(w, "gear-m1-z40-b8", 0.03, 0, 0.06);
            return (w, a, b);
        }

        [Fact]
        public void TwoGearsOnShaftsInBearingsMeshAtTheirRatio()
        {
            var (world, shaftA, shaftB) = GearTrain();
            var report = DriveAssembly.Assemble(world);
            foreach (var line in report.Links.Concat(report.Problems)) _out.WriteLine(line);
            Assert.Empty(report.Problems);
            Assert.Equal(6, report.Joints);   // four bearings (hinges), two gears on their shafts (fixed)
            Assert.Equal(1, report.Couplings);
            var coupling = world.Couplings!.Single();
            Assert.False(coupling.Reverse);   // parallel shafts: the plain gear ratio

            // A motor on shaft A: one turn a second.
            var drive = world.Joints.First(j => j.ObjectA == shaftA.Id && j.Kind == WorldJointKind.Hinge);
            drive.Drive = WorldJoint.DriveSpeedMode;
            drive.DriveTarget = 360;
            drive.DriveForce = 1;
            Assert.Equal(0, DriveAssembly.Assemble(world).Joints);   // a second pass finds nothing new

            using var runner = new WorldRunner(WorldDocument.FromJson(world.ToJson()), new RunnerOptions { Firmware = false });
            while (runner.Time < 1 - 1e-6) runner.Step(0.01);
            var hingeB = runner.Physics!.Scene.Joints.First(j => world.Joints.Any(wj => wj.Id == j.Id && wj.ObjectA == shaftB.Id && wj.Kind == WorldJointKind.Hinge));
            double rpmB = hingeB.Speed * 30 / Math.PI;
            _out.WriteLine($"çıkış mili {rpmB:0.0} dev/dk (60 dev/dk girişte, 20:40)");
            Assert.InRange(rpmB, -30.3, -29.7);
            var gearB = world.Objects.Single(o => o.PresetId == "gear-m1-z40-b8");
            Assert.InRange(runner.Physics.Scene.Bodies.Single(b => b.Id == gearB.Id).Position.Z, 0.059, 0.071);   // held on its shaft, not fallen
        }

        /// <summary>How many of B's outline corners lie inside A's tooth outline (and the other way), in the gears' plane.</summary>
        private static int Clashes(WorldObject ga, WorldObject gb)
        {
            var sa = DriveParts.Of(ga)!;
            var sb = DriveParts.Of(gb)!;
            var n = ga.Pose.Orientation.Rotate(Vec3.UnitZ);
            var e1 = gb.Pose.Position - ga.Pose.Position;
            e1 = (e1 - n * e1.Dot(n)).Normalized();
            var e2 = n.Cross(e1);
            List<(double X, double Y)> Plane(WorldObject g, DrivePartSpec s) =>
                GearGeometry.Outline(s.Module, s.Teeth).Select(p =>
                {
                    var w = g.Pose.ToWorld(new Vec3(p.X, p.Y, 0)) - ga.Pose.Position;
                    return (w.Dot(e1), w.Dot(e2));
                }).ToList();
            static bool Inside(List<(double X, double Y)> poly, (double X, double Y) q)
            {
                bool inside = false;
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    if ((poly[i].Y > q.Y) != (poly[j].Y > q.Y) && q.X < (poly[j].X - poly[i].X) * (q.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) inside = !inside;
                }

                return inside;
            }

            var a = Plane(ga, sa);
            var b = Plane(gb, sb);
            return b.Count(q => Inside(a, q)) + a.Count(q => Inside(b, q));
        }

        [Fact]
        public void MeshingGearsStartWithTheirTeethInEachOthersSpaces()
        {
            var (world, _, _) = GearTrain();
            var small = world.Objects.Single(o => o.PresetId == "gear-m1-z20-b8");
            var large = world.Objects.Single(o => o.PresetId == "gear-m1-z40-b8");
            int before = Clashes(small, large);
            DriveAssembly.Assemble(world);
            int after = Clashes(small, large);
            _out.WriteLine($"diş çakışması: kurmadan önce {before} köşe, sonra {after}; büyük dişli {large.Pose.Yaw:0.00}° döndürüldü");
            Assert.True(before > 0, "as placed, tooth 0 of each points at the other: tooth on tooth");
            Assert.Equal(0, after);
        }

        [Fact]
        public void ABeltRoundTwoPulleysTurnsTheSecondTheSameWayAtTheirRatio()
        {
            var w = new WorldDocument();
            var shaftA = Put(w, "shaft-8-100", 0, 0, 0.02);
            Put(w, "bearing-608zz", 0, 0, 0.025, true);
            Put(w, "bearing-608zz", 0, 0, 0.105, true);
            var small = Put(w, "pulley-gt2-20-b8", 0, 0, 0.06);
            var shaftB = Put(w, "shaft-8-100", 0.06, 0, 0.02);
            Put(w, "bearing-608zz", 0.06, 0, 0.025, true);
            Put(w, "bearing-608zz", 0.06, 0, 0.105, true);
            var large = Put(w, "pulley-gt2-40-b8", 0.06, 0, 0.06);
            DriveAssembly.Assemble(w);
            var (belt, message) = DriveAssembly.Belt(w, small, large);
            _out.WriteLine(message);
            Assert.NotNull(belt);
            double expected = DriveAssembly.BeltLength(0.02 / Math.PI, 0.04 / Math.PI, 0.06);
            Assert.InRange(expected, 0.1805, 0.1809);   // 2·59.66 + 18.65 + 42.71 mm
            Assert.Contains("GT2 kayış 180.68 mm (90.3 diş)", message);
            Assert.Contains("kapalı GT2 kayış 188 mm", message);   // the loop to buy, at or above it
            var motor = w.Joints.First(j => j.ObjectA == shaftA.Id && j.Kind == WorldJointKind.Hinge);
            motor.Drive = WorldJoint.DriveSpeedMode;
            motor.DriveTarget = 360;
            motor.DriveForce = 1;
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), new RunnerOptions { Firmware = false });
            while (runner.Time < 1 - 1e-6) runner.Step(0.01);
            var hingeB = runner.Physics!.Scene.Joints.First(j => w.Joints.Any(wj => wj.Id == j.Id && wj.ObjectA == shaftB.Id && wj.Kind == WorldJointKind.Hinge));
            _out.WriteLine($"büyük kasnak {hingeB.Speed * 30 / Math.PI:0.0} dev/dk");
            Assert.InRange(hingeB.Speed * 30 / Math.PI, 29.7, 30.3);   // the same way, half the speed

            var (none, why) = DriveAssembly.Belt(w, small, Put(w, "pulley-gt2-60-b8", 0.0, 0.08, 0.07));
            _out.WriteLine(why);
            Assert.Null(none);   // 10 mm out of the belt's plane (and on no shaft)
        }

        /// <summary>A z20 pinion on a shaft across a rack, its pitch circle on the rack's pitch line (rack on the floor of 50 mm).</summary>
        private static (WorldDocument World, WorldObject Shaft, WorldObject Rack, WorldObject Pinion) RackRig(bool rackSlides)
        {
            var w = new WorldDocument();
            var rack = Put(w, "rack-m1-300", 0, 0, 0.05, frame: !rackSlides);
            double zc = 0.05 + 0.010 - 0.001 + 0.010;   // pitch line 9 mm up, plus the pinion's 10 mm pitch radius
            var shaft = DriveParts.Add(w, "shaft-8-100", new WorldPose { Y = -0.05, Z = zc, Roll = -90 });
            var pinion = DriveParts.Add(w, "gear-m1-z20-b8", new WorldPose { Y = -0.004, Z = zc, Roll = -90 });
            var b1 = DriveParts.Add(w, "bearing-608zz", new WorldPose { Y = -0.03, Z = zc, Roll = -90 });
            var b2 = DriveParts.Add(w, "bearing-608zz", new WorldPose { Y = 0.025, Z = zc, Roll = -90 });
            if (rackSlides)
            {
                b1.IsStatic = b2.IsStatic = true;
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Slider, ObjectA = rack.Id, AnchorZ = 0.05, AxisX = 1, AxisZ = 0 });
            }
            else
            {
                // The pinion's bearings ride a carriage that slides along the fixed rack.
                var cart = new WorldObject { Id = w.NextId("O"), Name = "Araba", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.04, SizeY = 0.008, SizeZ = 0.04, MassKg = 0.3, Pose = new WorldPose { Y = 0.04, Z = zc - 0.02 } };
                w.Objects.Add(cart);
                foreach (var b in new[] { b1, b2 })
                    w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = b.Id, ObjectB = cart.Id, Fastening = "screws", AnchorX = b.Pose.X, AnchorY = b.Pose.Y, AnchorZ = b.Pose.Z });
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Slider, ObjectA = cart.Id, AnchorY = 0.04, AnchorZ = zc - 0.02, AxisX = 1, AxisZ = 0 });
            }

            return (w, shaft, rack, pinion);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void APinionDrivesARackOrRollsAlongIt(bool rackSlides)
        {
            var (w, shaft, rack, _) = RackRig(rackSlides);
            var report = DriveAssembly.Assemble(w);
            foreach (var line in report.Links.Concat(report.Problems)) _out.WriteLine(line);
            Assert.Empty(report.Problems);
            Assert.Equal(1, report.Couplings);
            var motor = w.Joints.First(j => j.ObjectA == shaft.Id && j.Kind == WorldJointKind.Hinge);
            motor.Drive = WorldJoint.DriveSpeedMode;
            motor.DriveTarget = 180;   // half a turn a second: π·20 mm / 2 = 31.4 mm/s
            motor.DriveForce = 1;
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), new RunnerOptions { Firmware = false });
            var moving = rackSlides ? rack : w.Objects.Single(o => o.Name == "Araba");
            double x0 = runner.Physics!.Scene.Bodies.Single(b => b.Id == moving.Id).Position.X;
            while (runner.Time < 1 - 1e-6) runner.Step(0.01);
            double travel = runner.Physics.Scene.Bodies.Single(b => b.Id == moving.Id).Position.X - x0;
            _out.WriteLine($"{(rackSlides ? "kremayer" : "araba")} 1 s'de {travel * 1000:0.00} mm gitti (kuram 31.42)");
            Assert.InRange(Math.Abs(travel), 0.0305, 0.0322);
        }

        [Fact]
        public void ALeadScrewLiftsTheCarriageItsNutIsBoltedTo()
        {
            var w = new WorldDocument();
            var screw = Put(w, "screw-t8x8-300", 0, 0, 0.01);
            Put(w, "bearing-608zz", 0, 0, 0.012, true);
            Put(w, "bearing-608zz", 0, 0, 0.30, true);
            Put(w, "shaft-8-300", 0, 0.03, 0.01, true);
            Put(w, "shaft-8-300", 0, -0.03, 0.01, true);
            var nut = Put(w, "nut-t8x8", 0, 0, 0.1);
            var lm1 = Put(w, "lm8uu", 0, 0.03, 0.1);
            var lm2 = Put(w, "lm8uu", 0, -0.03, 0.1);
            var plate = new WorldObject { Id = w.NextId("O"), Name = "Taşıyıcı", Kind = WorldObjectKind.Body, Shape = "box", SizeX = 0.03, SizeY = 0.09, SizeZ = 0.006, MassKg = 0.3, Pose = new WorldPose { X = 0.03, Z = 0.115 } };
            w.Objects.Add(plate);
            foreach (var part in new[] { nut, lm1, lm2 })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = part.Id, ObjectB = plate.Id, Fastening = "screws", AnchorX = part.Pose.X, AnchorY = part.Pose.Y, AnchorZ = part.Pose.Z });

            var report = DriveAssembly.Assemble(w);
            foreach (var line in report.Links.Concat(report.Problems)) _out.WriteLine(line);
            Assert.Empty(report.Problems);
            Assert.Equal(1, report.Couplings);
            var motor = w.Joints.First(j => j.ObjectA == screw.Id && j.Kind == WorldJointKind.Hinge);
            motor.Drive = WorldJoint.DriveSpeedMode;
            motor.DriveTarget = -360;   // a right-hand screw turned clockwise (seen from above) lifts its nut
            motor.DriveForce = 1;
            using var runner = new WorldRunner(WorldDocument.FromJson(w.ToJson()), new RunnerOptions { Firmware = false });
            double z0 = runner.Physics!.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z;
            while (runner.Time < 1 - 1e-6) runner.Step(0.01);
            double rise = runner.Physics.Scene.Bodies.Single(b => b.Id == plate.Id).Position.Z - z0;
            _out.WriteLine($"taşıyıcı 1 s'de {rise * 1000:0.00} mm yükseldi (turda 8 mm, 1 tur/s)");
            Assert.InRange(rise, 0.0075, 0.0082);
        }

        [Fact]
        public void PartsThatHoldNothingAreCalledOut()
        {
            var w = new WorldDocument();
            Put(w, "shaft-8-100", 0, 0, 0.02);   // in no bearing
            Put(w, "gear-m1-z20-b8", 0, 0, 0.05);
            Put(w, "gear-m1-z40-b8", 0.03, 0, 0.05);   // meshes with the first, but on no shaft
            Put(w, "bearing-608zz", 0.2, 0, 0.02);   // bolted to nothing
            Put(w, "shaft-5-100", 0.2, 0, 0.0);   // 5 mm shaft in an 8 mm bore: loose
            var report = DriveAssembly.Assemble(w);
            foreach (var line in report.Problems) _out.WriteLine(line);
            Assert.Equal(2, report.Problems.Count(p => p.Contains("hiçbir rulmandan geçmiyor")));   // the 8 mm one carries a gear but turns in nothing
            Assert.Contains(report.Problems, p => p.Contains("dönebileceği bir mile ya da rulmana takılı değil"));
            Assert.Contains(report.Problems, p => p.Contains("bol"));
            Assert.Equal(0, report.Couplings);
        }
    }
}
