using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[integrity] A board against its schematic: what bringing it up to date changes, and parts that come back.</summary>
    public class BoardSyncTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        // R1 between A and B, R2 between B and C: C is R2's alone.
        private static Schematic TwoResistors()
        {
            var sch = new Schematic { Title = "İki direnç" };
            var r1 = sch.AddPart(Lib, "res-1000", new GridPoint(0, 0));
            var r2 = sch.AddPart(Lib, "res-1000", new GridPoint(20, 0));
            sch.Labels.Add(new NetLabel { Name = "A", Position = r1.Transform(-3, 0) });
            sch.Labels.Add(new NetLabel { Name = "B", Position = r1.Transform(3, 0) });
            sch.Labels.Add(new NetLabel { Name = "B", Position = r2.Transform(-3, 0) });
            sch.Labels.Add(new NetLabel { Name = "C", Position = r2.Transform(3, 0) });
            return sch;
        }

        [Fact]
        public void AnUpToDateBoardHasNoChanges()
        {
            var sch = TwoResistors();
            var board = BoardDocument.Synchronize(sch, Lib);
            var changes = BoardSync.Preview(sch, Lib, board);
            Assert.True(changes.IsEmpty);
            Assert.False(changes.Destructive);
            Assert.Empty(changes.SummaryLines());
        }

        [Fact]
        public void ThePreviewIsWhatSynchronisingDoes()
        {
            var sch = TwoResistors();
            var board = BoardDocument.Synchronize(sch, Lib);
            var r2 = board.Find("R2")!;
            r2.X = 10;
            r2.Y = 20;
            var pads = board.Pads().Where(p => p.Net == "C").ToList();
            board.Tracks.Add(new Track { Net = "C", X1 = 10, Y1 = 20, X2 = 30, Y2 = 20 });   // C goes with R2
            board.Tracks.Add(new Track { Net = "B", X1 = 0, Y1 = 0, X2 = 10, Y2 = 0 });      // B stays (R1 has it)

            sch.Parts.RemoveAll(p => p.RefDes == "R2");
            sch.AddPart(Lib, "res-100", new GridPoint(40, 0)).RefDes = "R3";   // unconnected (AddPart would reuse the free R2)
            var changes = BoardSync.Preview(sch, Lib, board);
            Assert.Equal(new[] { "R3" }, changes.Added);
            Assert.Equal(new[] { "R2" }, changes.Removed);
            Assert.Equal(1, changes.TracksRemoved);
            Assert.Equal(20, changes.TrackLengthRemoved, 6);
            Assert.True(changes.Destructive);
            Assert.Equal(2, board.Tracks.Count);   // the preview left the board alone
            Assert.NotNull(board.Find("R2"));
            Assert.Contains("1 parça kalkacak (R2)", changes.SummaryLines());
            Assert.Contains("1 iz silindi (20 mm)", changes.SummaryLines(done: true));

            BoardDocument.Synchronize(sch, Lib, board);
            Assert.Equal(new[] { "R1", "R3" }, board.Components.Select(c => c.RefDes).OrderBy(r => r).ToArray());
            Assert.Single(board.Tracks);
            Assert.True(BoardSync.Preview(sch, Lib, board).IsEmpty);
        }

        [Fact]
        public void APartThatComesBackStandsWhereItStood()
        {
            var sch = TwoResistors();
            var board = BoardDocument.Synchronize(sch, Lib);
            var r2 = board.Find("R2")!;
            r2.X = 12.7;
            r2.Y = 25.4;
            r2.Rotation = 90;
            r2.Side = CopperLayer.Bottom;
            var part = sch.FindPart("R2")!;
            sch.Parts.Remove(part);
            BoardDocument.Synchronize(sch, Lib, board);
            Assert.Null(board.Find("R2"));
            Assert.Single(board.Removed);

            sch.Parts.Add(part);   // undone in the schematic
            var changes = BoardSync.Preview(sch, Lib, board);
            Assert.Equal(new[] { "R2" }, changes.Added);
            BoardDocument.Synchronize(sch, Lib, board);
            var back = board.Find("R2")!;
            Assert.Equal((12.7, 25.4, 90, CopperLayer.Bottom), (back.X, back.Y, back.Rotation, back.Side));
            Assert.Empty(board.Removed);
        }

        [Fact]
        public void AChangedFootprintAndMovedPadsAreTold()
        {
            var sch = TwoResistors();
            var board = BoardDocument.Synchronize(sch, Lib);
            string real = board.Find("R1")!.FootprintId;
            board.Find("R1")!.FootprintId = "OLD-PACKAGE";   // as laid out before the part changed
            var first = board.Find("R2")!.PadNets.Keys.First();
            board.Find("R2")!.PadNets[first] = "ELSEWHERE";
            var changes = BoardSync.Preview(sch, Lib, board);
            Assert.Equal(new[] { ("R1", "OLD-PACKAGE", real) }, changes.Footprints.ToArray());
            var moved = Assert.Single(changes.Nets);
            Assert.Equal(("R2", first, "ELSEWHERE"), (moved.RefDes, moved.Pad, moved.From));
            Assert.True(changes.Destructive);   // a changed footprint redoes the layout around it
            Assert.Contains(changes.Lines(), l => l.StartsWith("R1: ayak izi OLD-PACKAGE → "));
        }

        [Fact]
        public void TheProjectCheckSaysWhenABoardIsBehind()
        {
            var sch = TwoResistors();
            var board = BoardDocument.Synchronize(sch, Lib);
            Assert.Null(ProjectCheck.BoardBehind(BoardSync.Preview(sch, Lib, board), "PCB"));
            sch.AddPart(Lib, "res-100", new GridPoint(40, 0));
            var note = ProjectCheck.BoardBehind(BoardSync.Preview(sch, Lib, board), "PCB")!;
            Assert.Equal(IssueLevel.Note, note.Level);   // only an addition: nothing laid out is lost
            Assert.Equal(IssueFix.OpenPcb, note.Fix);
            sch.Parts.RemoveAll(p => p.RefDes == "R2");
            var warning = ProjectCheck.BoardBehind(BoardSync.Preview(sch, Lib, board), "PCB")!;
            Assert.Equal(IssueLevel.Warning, warning.Level);
            Assert.Contains("1 parça kalkacak (R2)", warning.Text);
        }
    }
}
