using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [integrity] A part's identity outlives its designator: the PCB and the Montaj follow a part by its id, so renaming it
    /// (by hand, by renumbering, in another program) keeps where it was placed.
    /// </summary>
    public class PartIdentityTests
    {
        private static PartLibrary Lib => PartLibrary.Default;

        [Fact]
        public void EveryPartHasAnIdOfItsOwnAndACopyGetsANewOne()
        {
            var sch = new Schematic();
            var r1 = sch.AddPart(Lib, "res-1000", new GridPoint(0, 0));
            var r2 = sch.AddPart(Lib, "res-1000", new GridPoint(10, 0));
            Assert.False(string.IsNullOrEmpty(r1.Uid));
            Assert.NotEqual(r1.Uid, r2.Uid);

            string uid = r1.Uid;
            var copy = new SchematicPart { RefDes = "R3", PartId = r1.PartId, Uid = r1.Uid };   // pasted with the original's id
            var old = new SchematicPart { RefDes = "R4", PartId = r1.PartId };                   // from a file saved before ids
            sch.Parts.Add(copy);
            sch.Parts.Add(old);
            sch.EnsurePartUids();
            Assert.Equal(uid, r1.Uid);   // the original keeps it
            Assert.NotEqual(uid, copy.Uid);
            Assert.False(string.IsNullOrEmpty(old.Uid));
            Assert.Equal(4, sch.Parts.Select(p => p.Uid).Distinct().Count());
        }

        [Fact]
        public void ARenamedPartKeepsItsPlaceOnThePcb()
        {
            var sch = new Schematic();
            var r = sch.AddPart(Lib, "res-1000", new GridPoint(0, 0));
            sch.AddPart(Lib, "led3-red", new GridPoint(20, 0));
            var board = BoardDocument.Synchronize(sch, Lib);
            var comp = board.Find(r.RefDes)!;
            comp.X = 12.5;
            comp.Y = 7.5;
            comp.Rotation = 90;

            r.RefDes = "R42";   // renamed without telling the board
            var preview = BoardSync.Preview(sch, Lib, board);
            Assert.Empty(preview.Added);
            Assert.Empty(preview.Removed);
            BoardDocument.Synchronize(sch, Lib, board);
            var same = board.Find("R42");
            Assert.Same(comp, same);
            Assert.Equal((12.5, 7.5, 90), (same!.X, same.Y, same.Rotation));
            Assert.Equal(2, board.Components.Count);
        }

        [Fact]
        public void ARenamedPartKeepsItsPlaceInTheMontaj()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            sch.Assembly = null;
            var package = new ProjectPackage { Schematic = sch };
            var w = ProjectAssembly.Build(package, Lib);
            var sonar = sch.Parts.Single(p => p.PartId == "hc-sr04");
            var obj = w.Objects.Single(o => o.ProjectRef == sch.ProjectId + "/" + sonar.RefDes);
            obj.Pose.X = 0.42;

            sonar.RefDes = "U9";
            var report = ProjectAssembly.Sync(w, package, Lib);
            Assert.Equal(0, report.Added);
            Assert.Equal(0, report.Removed);
            var same = w.Objects.Single(o => o.ProjectRef == sch.ProjectId + "/U9");
            Assert.Same(obj, same);
            Assert.Equal(0.42, same.Pose.X);
        }

        private static Schematic TwoResistors(bool labelled)
        {
            // R1 and R2 joined through the middle net: labelled "B", or left unnamed (then called after its first pin)
            var sch = new Schematic();
            var r1 = sch.AddPart(Lib, "res-1000", new GridPoint(0, 0));
            var r2 = sch.AddPart(Lib, "res-1000", new GridPoint(20, 0));
            sch.Labels.Add(new NetLabel { Name = "A", Position = r1.Transform(-3, 0) });
            sch.Labels.Add(new NetLabel { Name = "C", Position = r2.Transform(3, 0) });
            if (labelled)
            {
                sch.Labels.Add(new NetLabel { Name = "B", Position = r1.Transform(3, 0) });
                sch.Labels.Add(new NetLabel { Name = "B", Position = r2.Transform(-3, 0) });
            }
            else
            {
                sch.AddWire(r1.Transform(3, 0), r2.Transform(-3, 0));
            }

            return sch;
        }

        [Fact]
        public void ARenamedNetKeepsItsTracks()
        {
            // The label B renamed to PWM: the same pads, the same net under a new name. Its copper used to be deleted.
            var sch = TwoResistors(labelled: true);
            var board = BoardDocument.Synchronize(sch, Lib);
            board.Tracks.Add(new Track { Net = "B", Layer = CopperLayer.Top, X1 = 1, Y1 = 1, X2 = 9, Y2 = 1 });
            board.Vias.Add(new Via { Net = "B", X = 9, Y = 1 });
            foreach (var l in sch.Labels.Where(l => l.Name == "B")) l.Name = "PWM";

            var preview = BoardSync.Preview(sch, Lib, board);
            Assert.Equal(0, preview.TracksRemoved);
            Assert.Equal(0, preview.ViasRemoved);
            Assert.Empty(preview.Nets);   // a rename, not pads moving to another net
            Assert.False(preview.Destructive);
            BoardDocument.Synchronize(sch, Lib, board);
            Assert.Equal("PWM", Assert.Single(board.Tracks).Net);
            Assert.Equal("PWM", Assert.Single(board.Vias).Net);
        }

        [Fact]
        public void RenamingAPartOnAnUnnamedNetKeepsTheNetsTracks()
        {
            // An unnamed net is called after its first pin ("R1.2"): renaming R1 renames the net, and its tracks follow.
            var sch = TwoResistors(labelled: false);
            var board = BoardDocument.Synchronize(sch, Lib);
            string net = board.Find("R1")!.PadNets.Values.First(n => n != "A");
            board.Tracks.Add(new Track { Net = net, Layer = CopperLayer.Top, X1 = 1, Y1 = 1, X2 = 9, Y2 = 1 });
            sch.Parts.Single(p => p.RefDes == "R1").RefDes = "R7";

            var preview = BoardSync.Preview(sch, Lib, board);
            Assert.Equal(new[] { ("R1", "R7") }, preview.Renamed.ToArray());
            Assert.Equal(0, preview.TracksRemoved);
            Assert.False(preview.Destructive);
            Assert.Contains(preview.Lines(), l => l == "R1 → R7: adı değişecek, yeri ve izleri korunur");
            BoardDocument.Synchronize(sch, Lib, board);
            string now = board.Find("R7")!.PadNets.Values.First(n => n != "A");
            Assert.Equal(now, Assert.Single(board.Tracks).Net);
        }

        [Fact]
        public void CodeFollowsItsBoardWhenTheBoardIsRenamed()
        {
            // Renamed in the inspector or by find and replace, a board used to leave its code behind with no board.
            var sch = new Schematic();
            var uno = sch.AddPart(Lib, "arduino-uno", new GridPoint(0, 0));
            var package = new ProjectPackage { Schematic = sch };
            package.Firmware.Add(new FirmwareSource { BoardRefDes = uno.RefDes, Source = "void setup(){}\nvoid loop(){}\n" });   // from before identities
            package.FollowRenamedBoards();
            Assert.Equal(uno.Uid, package.Firmware[0].BoardUid);

            uno.RefDes = "U7";
            package.FollowRenamedBoards();
            Assert.Same(package.Firmware[0], package.FirmwareFor("U7"));
        }

        [Fact]
        public void APartThatComesBackWithoutItsIdentityKeepsItsPlace()
        {
            // An undo can bring a part back as it was before it had an identity: the designator decides, as it did before.
            var sch = new Schematic();
            var r = sch.AddPart(Lib, "res-1000", new GridPoint(0, 0));
            var board = BoardDocument.Synchronize(sch, Lib);
            var comp = board.Find(r.RefDes)!;
            comp.X = 4.5;
            r.Uid = string.Empty;

            var preview = BoardSync.Preview(sch, Lib, board);
            Assert.True(preview.IsEmpty, preview.Summary());
            BoardDocument.Synchronize(sch, Lib, board);
            Assert.Same(comp, board.Find(r.RefDes));
            Assert.Equal(4.5, comp.X);
            Assert.Equal(r.Uid, comp.PartUid);
        }

        [Fact]
        public void ABoardAndMontajFromBeforeIdsStillMatchByDesignator()
        {
            // Saved before parts had ids: nothing to go by but the designators, and they still match.
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            sch.Assembly = null;
            var package = new ProjectPackage { Schematic = sch };
            var w = ProjectAssembly.Build(package, Lib);
            var board = BoardDocument.Synchronize(sch, Lib);
            foreach (var p in sch.Parts) p.Uid = string.Empty;
            foreach (var c in board.Components) c.PartUid = string.Empty;
            foreach (var o in w.Objects) o.PartUid = null;

            Assert.False(ProjectAssembly.Sync(w, package, Lib).Changed);
            var preview = BoardSync.Preview(sch, Lib, board);
            Assert.Empty(preview.Added);
            Assert.Empty(preview.Removed);
            Assert.All(sch.Parts, p => Assert.False(string.IsNullOrEmpty(p.Uid)));   // given ids on the way
        }
    }
}
