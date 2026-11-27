using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[bus] KiCad's buses: a bus line joins nothing by touching; its members join by name, also through sheet pins.</summary>
    public class SchematicBusTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        private static GridPoint Tip(Schematic s, SchematicPart part, int k) =>
            s.PinPositions(Lib).First(x => ReferenceEquals(x.Part, part) && x.Pin.Name == Lib.Get("resistor").Pins[k].Name).Position;

        [Fact]
        public void ABusShortsNothingItTouches()
        {
            var s = new Schematic();
            var r1 = new SchematicPart { RefDes = "R1", PartId = "resistor", Position = new GridPoint(0, 0) };
            var r2 = new SchematicPart { RefDes = "R2", PartId = "resistor", Position = new GridPoint(0, 10) };
            s.Parts.Add(r1);
            s.Parts.Add(r2);
            // a bus running from one pin to the other (and past both): the two stay apart
            var a = Tip(s, r1, 1);
            var b = Tip(s, r2, 1);
            s.Wires.Add(new SchematicWire { Bus = true, Points = { a, new GridPoint(a.X + 5, a.Y), new GridPoint(a.X + 5, b.Y), b } });
            var nl = s.BuildNetlist(Lib);
            string p1 = Lib.Get("resistor").Pins[1].Name;
            Assert.NotSame(nl.NetOf("R1", p1), nl.NetOf("R2", p1));
            Assert.Null(nl.WireNets[0]);
            Assert.Empty(nl.Junctions);
            Assert.DoesNotContain(SchematicEditing.DanglingEnds(s, Lib), d => d.Wire.Bus);   // a bus may end free

            // the same pins labelled D0 on both: joined by the name, as a bus's members are
            s.Labels.Add(new NetLabel { Name = "D0", Position = a });
            s.Labels.Add(new NetLabel { Name = "D0", Position = b });
            nl = s.BuildNetlist(Lib);
            Assert.Same(nl.NetOf("R1", p1), nl.NetOf("R2", p1));
        }

        [Fact]
        public void ABusThroughASheetPinJoinsEachMember()
        {
            var s = new Schematic();
            var sheet = new SchematicSheet { Name = "Veri", Position = new GridPoint(30, 0), Width = 10, Height = 6 };
            s.Sheets.Add(sheet);
            var parts = new[] { "R1", "R2", "R3", "R4" }.Select((r, i) => new SchematicPart
            {
                RefDes = r, PartId = "resistor", Position = new GridPoint(0, i % 2 * 10), Sheet = i < 2 ? null : sheet.Uid
            }).ToArray();
            s.Parts.AddRange(parts);
            for (int i = 0; i < 4; i++) s.Labels.Add(new NetLabel { Name = "D" + (i % 2), Position = Tip(s, parts[i], 0), Sheet = parts[i].Sheet });

            // the root's bus carries D[0..1] to the pin; inside, the bus's hierarchical label of the same name
            var pin = new GridPoint(30, 2);
            sheet.Pins.Add(new SheetPin { Name = "D[0..1]", Position = pin });
            s.Wires.Add(new SchematicWire { Bus = true, Points = { new GridPoint(20, 2), pin } });
            s.Labels.Add(new NetLabel { Name = "D[0..1]", Position = new GridPoint(5, -5), Hierarchical = true, Sheet = sheet.Uid });
            s.Wires.Add(new SchematicWire { Bus = true, Sheet = sheet.Uid, Points = { new GridPoint(5, -5), new GridPoint(15, -5) } });

            var nl = s.BuildNetlist(Lib);
            string p0 = Lib.Get("resistor").Pins[0].Name;
            Assert.Same(nl.NetOf("R1", p0), nl.NetOf("R3", p0));   // D0 with D0
            Assert.Same(nl.NetOf("R2", p0), nl.NetOf("R4", p0));   // D1 with D1
            Assert.NotSame(nl.NetOf("R1", p0), nl.NetOf("R2", p0));
            Assert.Equal("D0", nl.NetOf("R3", p0)!.Name);   // named by the root's label

            Assert.DoesNotContain(ElectricalRulesChecker.Check(s, Lib), v => v.Code == "label-single" && v.Net == "D[0..1]");
            Assert.True(Schematic.FromJson(s.ToJson()).Wires.Count(w => w.Bus) == 2);
        }
    }
}
