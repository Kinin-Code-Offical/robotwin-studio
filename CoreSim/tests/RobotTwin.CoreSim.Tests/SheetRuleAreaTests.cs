using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[rule-area] KiCad's rule area on the sheet: the nets inside it take its class; a directive label wins over it.</summary>
    public class SheetRuleAreaTests
    {
        [Fact]
        public void TheNetsInsideTakeItsClassAndADirectiveWins()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var lib = PartLibrary.Default;
            var five = sch.Labels.First(l => l.Name == "5V");
            var vbat = sch.Labels.First(l => l.Name == "VBAT");
            var p = five.Position;
            sch.Shapes.Add(new SchematicShape { Kind = SheetShapeKind.RuleArea, NetClass = "Logic", Points = { new GridPoint(p.X - 1, p.Y - 1), new GridPoint(p.X + 1, p.Y + 1) } });
            var nl = sch.BuildNetlist(lib);
            var classes = sch.NetClassDirectives(nl, lib);
            Assert.Equal("Logic", classes["5V"]);
            Assert.False(classes.ContainsKey("VBAT"));   // outside the area

            // a directive label on the same net is the last word
            sch.Labels.Add(new NetLabel { NetClass = "Signal", Position = p });
            Assert.Equal("Signal", sch.NetClassDirectives(sch.BuildNetlist(lib), lib)["5V"]);

            // a pin inside counts too: an area round the battery's + pin's label point gives VBAT its class
            var q = vbat.Position;
            sch.Shapes.Add(new SchematicShape { Kind = SheetShapeKind.RuleArea, NetClass = NetClass.PowerName, Points = { new GridPoint(q.X - 0.5, q.Y - 0.5), new GridPoint(q.X + 0.5, q.Y + 0.5) } });
            var board = BoardDocument.Synchronize(sch, lib);
            Assert.Equal(NetClass.PowerName, board.ClassNameFor("VBAT"));
            Assert.Equal("Signal", board.ClassNameFor("5V"));
        }

        [Fact]
        public void ARuleAreaIsCopiedAndSavedWithItsClass()
        {
            var sch = new Schematic();
            var area = new SchematicShape { Kind = SheetShapeKind.RuleArea, NetClass = "HV", Points = { new GridPoint(0, 0), new GridPoint(10, 6) } };
            sch.Shapes.Add(area);
            Assert.Equal(new GridRect(0, 0, 10, 6).MaxX, area.Bounds().MaxX);
            var pasted = SchematicFragment.FromItems(new object[] { area }, PartLibrary.Default, "t").PasteInto(sch, PartLibrary.Default, new GridPoint(30, 30));
            Assert.Equal("HV", Assert.IsType<SchematicShape>(Assert.Single(pasted.Items)).NetClass);
            Assert.Equal("HV", Schematic.FromJson(sch.ToJson()).Shapes[0].NetClass);
        }
    }
}
