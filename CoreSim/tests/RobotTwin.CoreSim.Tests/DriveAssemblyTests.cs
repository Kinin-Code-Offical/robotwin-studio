using System;
using System.Linq;
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

        private static WorldObject Put(WorldDocument w, string id, double x, double y, double z, bool fixedToFrame = false)
        {
            var o = DriveParts.Add(w, id, new WorldPose { X = x, Y = y, Z = z });
            o.IsStatic = fixedToFrame;
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
