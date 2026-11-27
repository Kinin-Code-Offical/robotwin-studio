using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[group] KiCad's groups on the sheet: named, saved, taken apart; a pasted group is a new one.</summary>
    public class SchematicGroupTests
    {
        private static (Schematic s, SchematicPart r1, SchematicWire w, NetLabel l, SchematicShape frame) Sheet()
        {
            var s = new Schematic();
            var r1 = new SchematicPart { RefDes = "R1", PartId = "resistor", Position = new GridPoint(10, 10) };
            s.Parts.Add(r1);
            s.Parts.Add(new SchematicPart { RefDes = "R2", PartId = "resistor", Position = new GridPoint(40, 10) });
            var w = new SchematicWire { Points = new List<GridPoint> { new GridPoint(12, 10), new GridPoint(20, 10) } };
            s.Wires.Add(w);
            var l = new NetLabel { Name = "VIN", Position = new GridPoint(20, 10) };
            s.Labels.Add(l);
            var frame = new SchematicShape { Kind = SheetShapeKind.Rectangle, Points = new List<GridPoint> { new GridPoint(5, 5), new GridPoint(25, 15) } };
            s.Shapes.Add(frame);
            return (s, r1, w, l, frame);
        }

        [Fact]
        public void ItemsGroupedTogetherAreFoundAndSaved()
        {
            var (s, r1, w, l, frame) = Sheet();
            string name = SchematicGroups.Group(s, new object[] { r1, w, l, frame });
            Assert.Equal("Grup 1", name);
            Assert.Equal(4, SchematicGroups.Members(s, name).Count);
            Assert.Null(s.FindPart("R2")!.Group);

            string second = SchematicGroups.Group(s, new object[] { s.FindPart("R2")! });
            Assert.Equal("Grup 2", second);
            Assert.Equal(new[] { "Grup 1", "Grup 2" }, SchematicGroups.Names(s));

            var back = Schematic.FromJson(s.ToJson());
            Assert.Equal(4, SchematicGroups.Members(back, "Grup 1").Count);
            Assert.Equal("Grup 1", back.Shapes[0].Group);
            Assert.DoesNotContain("\"group\":null", new Schematic().ToJson().Replace(" ", ""));
        }

        [Fact]
        public void UngroupFreesEveryMemberOfTheGroup()
        {
            var (s, r1, w, l, frame) = Sheet();
            SchematicGroups.Group(s, new object[] { r1, w, l, frame });
            Assert.Equal(4, SchematicGroups.Ungroup(s, new object[] { w }));
            Assert.All(SchematicGroups.Items(s), i => Assert.Null(SchematicGroups.GroupOf(i)));
            Assert.Empty(SchematicGroups.Names(s));
        }

        [Fact]
        public void APastedGroupIsANewGroupOfItsOwn()
        {
            var (s, r1, w, l, frame) = Sheet();
            SchematicGroups.Group(s, new object[] { r1, w, l, frame });
            var fragment = SchematicFragment.FromItems(new object[] { r1, w, l, frame }, PartLibrary.Default, "test");
            var pasted = fragment.PasteInto(s, PartLibrary.Default, new GridPoint(80, 40));
            Assert.Equal(4, pasted.Items.Count);
            var names = pasted.Items.Select(SchematicGroups.GroupOf).Distinct().ToList();
            Assert.Equal(new[] { "Grup 2" }, names);
            Assert.Equal(4, SchematicGroups.Members(s, "Grup 1").Count);
            Assert.Equal(4, SchematicGroups.Members(s, "Grup 2").Count);
        }
    }
}
