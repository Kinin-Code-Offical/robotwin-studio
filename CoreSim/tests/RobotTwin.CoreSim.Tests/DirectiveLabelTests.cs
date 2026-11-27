using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[directive] KiCad's directive label (netclass flag): it names no net, it gives its net a class the board takes.</summary>
    public class DirectiveLabelTests
    {
        [Fact]
        public void ADirectiveGivesItsNetAClassTheBoardTakes()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var lib = PartLibrary.Default;
            var before = sch.BuildNetlist(lib);
            var on = sch.Labels.First(l => l.Name == "5V");   // the directive sits where a 5 V pin meets its label
            string net = before.LabelNets[sch.Labels.IndexOf(on)]!;
            Assert.Equal("5V", net);

            sch.Labels.Add(new NetLabel { Name = "BOŞ VER", NetClass = "Motor", Position = on.Position });   // a stray name does not rename it
            var after = sch.BuildNetlist(lib);
            Assert.Equal(before.Nets.Select(n => n.Name).OrderBy(n => n), after.Nets.Select(n => n.Name).OrderBy(n => n));
            Assert.Equal("Motor", sch.NetClassDirectives(after)[net]);

            var board = BoardDocument.Synchronize(sch, lib);
            Assert.NotNull(board.FindClass("Motor"));   // a class the board did not have starts as Default's copy
            Assert.Equal("Motor", board.ClassNameFor(net));
            Assert.Equal(board.DefaultClass.TrackWidth, board.FindClass("Motor")!.TrackWidth);

            // to an existing class, and kept when the board is brought up to date again
            sch.Labels.Last().NetClass = NetClass.PowerName;
            var again = BoardDocument.Synchronize(sch, lib, board);
            Assert.Equal(NetClass.PowerName, again.ClassNameFor(net));
        }

        [Fact]
        public void ADirectiveIsCopiedWithItsClass()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var directive = new NetLabel { NetClass = "Signal", Position = sch.Labels[0].Position };
            sch.Labels.Add(directive);
            var pasted = SchematicFragment.FromItems(new object[] { directive }, PartLibrary.Default, "t").PasteInto(sch, PartLibrary.Default, new GridPoint(90, 90));
            Assert.Equal("Signal", Assert.IsType<NetLabel>(Assert.Single(pasted.Items)).NetClass);
            Assert.Equal("Signal", Schematic.FromJson(sch.ToJson()).Labels.Last().NetClass);
        }
    }
}
