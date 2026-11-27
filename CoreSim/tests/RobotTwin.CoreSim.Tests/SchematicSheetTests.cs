using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sheets] KiCad's hierarchy: sheet symbols, sheet pins and hierarchical labels, sheet-local nets.</summary>
    public class SchematicSheetTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        private static GridPoint Tip(Schematic s, SchematicPart part, string pin) =>
            s.PinPositions(Lib).First(x => ReferenceEquals(x.Part, part) && x.Pin.Name == pin).Position;

        /// <summary>R1 on the root, R2 on a sub-sheet at the very same place; R1.2 runs to the sheet pin IN, R2.1 to the label IN.</summary>
        private static (Schematic S, SchematicSheet Motor, SchematicPart R1, SchematicPart R2) Design()
        {
            var s = new Schematic();
            var r1 = new SchematicPart { RefDes = "R1", PartId = "resistor", Position = new GridPoint(10, 10) };
            s.Parts.Add(r1);
            var motor = new SchematicSheet { Name = "Motor", Position = new GridPoint(30, 4), Width = 12, Height = 8 };
            s.Sheets.Add(motor);
            var r2 = new SchematicPart { RefDes = "R2", PartId = "resistor", Position = new GridPoint(10, 10), Sheet = motor.Uid };
            s.Parts.Add(r2);

            var pinAt = new GridPoint(30, 6);
            motor.Pins.Add(new SheetPin { Name = "IN", Position = pinAt });
            var r1b = Tip(s, r1, r1.PartId == "resistor" ? Lib.Get("resistor").Pins[1].Name : "2");
            s.Wires.Add(new SchematicWire { Points = { r1b, new GridPoint(r1b.X, pinAt.Y), pinAt } });

            var r2a = Tip(s, r2, Lib.Get("resistor").Pins[0].Name);
            s.Labels.Add(new NetLabel { Name = "IN", Position = r2a, Hierarchical = true, Sheet = motor.Uid });

            // the same local name on both sheets: two nets
            s.Labels.Add(new NetLabel { Name = "X", Position = Tip(s, r1, Lib.Get("resistor").Pins[0].Name) });
            s.Labels.Add(new NetLabel { Name = "X", Position = Tip(s, r2, Lib.Get("resistor").Pins[1].Name), Sheet = motor.Uid });
            return (s, motor, r1, r2);
        }

        [Fact]
        public void ASheetPinJoinsItsHierarchicalLabelAndLocalNamesStayOnTheirSheet()
        {
            var (s, _, _, _) = Design();
            var nl = s.BuildNetlist(Lib);
            string p0 = Lib.Get("resistor").Pins[0].Name, p1 = Lib.Get("resistor").Pins[1].Name;
            var joined = nl.NetOf("R1", p1)!;
            Assert.Same(joined, nl.NetOf("R2", p0));   // through the sheet pin and the hierarchical label
            Assert.Equal("/Motor/IN", joined.Name);

            var rootX = nl.NetOf("R1", p0)!;
            var subX = nl.NetOf("R2", p1)!;
            Assert.NotSame(rootX, subX);   // a local label is its own sheet's
            Assert.Equal("X", rootX.Name);
            Assert.Equal("/Motor/X", subX.Name);
            Assert.Equal(3, nl.Nets.Count);   // and the parts at the very same place do not short
        }

        [Fact]
        public void GlobalLabelsAndPowerJoinEverySheet()
        {
            var (s, motor, r1, r2) = Design();
            string p0 = Lib.Get("resistor").Pins[0].Name, p1 = Lib.Get("resistor").Pins[1].Name;
            foreach (var l in s.Labels.Where(l => l.Name == "X")) l.Global = true;
            var nl = s.BuildNetlist(Lib);
            Assert.Same(nl.NetOf("R1", p0), nl.NetOf("R2", p1));
            Assert.Equal("X", nl.NetOf("R1", p0)!.Name);

            // ground symbols on two sheets are one ground
            foreach (var l in s.Labels.Where(l => l.Name == "X").ToList()) s.Labels.Remove(l);
            var g1 = new SchematicPart { RefDes = "#PWR1", PartId = "ground", Position = Tip(s, r1, p0) };
            var g2 = new SchematicPart { RefDes = "#PWR2", PartId = "ground", Position = Tip(s, r2, p1), Sheet = motor.Uid };
            s.Parts.Add(g1);
            s.Parts.Add(g2);
            g1.Position = g1.Position - (Tip(s, g1, Lib.Get("ground").Pins[0].Name) - g1.Position);
            g2.Position = g2.Position - (Tip(s, g2, Lib.Get("ground").Pins[0].Name) - g2.Position);
            nl = s.BuildNetlist(Lib);
            Assert.Same(nl.NetOf("R1", p0), nl.NetOf("R2", p1));
            Assert.True(nl.NetOf("R1", p0)!.IsGround);
        }

        [Fact]
        public void PinsComeFromTheLabelsAndASheetGoesWithEverythingOnIt()
        {
            var (s, motor, _, _) = Design();
            s.Labels.Add(new NetLabel { Name = "EN", Position = new GridPoint(2, 2), Hierarchical = true, Shape = LabelShape.Output, Sheet = motor.Uid });
            Assert.Equal(1, s.ImportSheetPins(motor));   // IN has its pin already
            var en = motor.Pins.Single(p => p.Name == "EN");
            Assert.Equal(motor.Position.X + motor.Width, en.Position.X);   // an output goes on the right edge
            Assert.Equal(LabelShape.Output, en.Shape);

            var inner = new SchematicSheet { Name = "Sürücü", Sheet = motor.Uid, Position = new GridPoint(40, 40) };
            s.Sheets.Add(inner);
            s.Wires.Add(new SchematicWire { Points = { new GridPoint(0, 0), new GridPoint(5, 0) }, Sheet = inner.Uid });
            Assert.Equal("/Motor/Sürücü/", s.SheetPath(inner.Uid));
            Assert.Equal(2, s.SheetDepth(inner.Uid));

            int before = s.Parts.Count + s.Wires.Count + s.Labels.Count;
            Assert.True(SchematicEditing.Delete(s, new object[] { motor }) == 1);
            Assert.Empty(s.Sheets);
            Assert.DoesNotContain(s.Parts, p => p.Sheet != null);
            Assert.DoesNotContain(s.Wires, w => w.Sheet != null);
            Assert.DoesNotContain(s.Labels, l => l.Sheet != null);
            Assert.True(s.Parts.Count + s.Wires.Count + s.Labels.Count < before);
        }

        [Fact]
        public void EditingSeesOnlyItsOwnSheet()
        {
            var (s, motor, r1, r2) = Design();
            var everywhere = new GridRect(-100, -100, 100, 100);
            var root = SchematicEditing.ItemsInRect(s, Lib, everywhere, true);
            var sub = SchematicEditing.ItemsInRect(s, Lib, everywhere, true, motor.Uid);
            Assert.Contains(r1, root);
            Assert.DoesNotContain(r2, root);
            Assert.Contains(motor, root);   // the symbol is drawn on the root
            Assert.Contains(r2, sub);
            Assert.DoesNotContain(r1, sub);
            Assert.Equal(SchematicEditing.AllItems(s, motor.Uid).Count(), sub.Count);

            // a wire ending on the sub-sheet where a root pin happens to be is still dangling there
            string p0 = Lib.Get("resistor").Pins[0].Name;
            var stub = new SchematicWire { Points = { Tip(s, r1, p0) + new GridPoint(0, 5), Tip(s, r1, p0) }, Sheet = motor.Uid };
            s.Wires.Add(stub);
            s.Labels.RemoveAll(l => l.Sheet == motor.Uid && l.Name == "X");
            Assert.Contains(SchematicEditing.DanglingEnds(s, Lib, motor.Uid), d => ReferenceEquals(d.Wire, stub));
            Assert.DoesNotContain(SchematicEditing.DanglingEnds(s, Lib), d => ReferenceEquals(d.Wire, stub));

            // moving the symbol takes its pins
            SchematicEditing.Translate(new object[] { motor }, new GridPoint(2, 3));
            Assert.Equal(new GridPoint(32, 9), motor.Pins[0].Position);
        }

        [Fact]
        public void ErcFindsLooseHierarchy()
        {
            var (s, motor, _, _) = Design();
            var ok = ElectricalRulesChecker.Check(s, Lib);
            Assert.DoesNotContain(ok, v => v.Code is "hier-label-no-pin" or "sheet-pin-no-label");

            motor.Pins.Add(new SheetPin { Name = "OUT", Position = new GridPoint(42, 6) });
            s.Labels.Add(new NetLabel { Name = "FAULT", Position = new GridPoint(0, 0), Hierarchical = true, Sheet = motor.Uid });
            var bad = ElectricalRulesChecker.Check(s, Lib);
            var noLabel = Assert.Single(bad, v => v.Code == "sheet-pin-no-label");
            Assert.Null(noLabel.Sheet);   // the pin is on the root
            var noPin = Assert.Single(bad, v => v.Code == "hier-label-no-pin");
            Assert.Equal(motor.Uid, noPin.Sheet);
        }

        [Fact]
        public void KiCadGetsAFileASheet()
        {
            var (s, motor, _, _) = Design();
            var board = RobotTwin.CoreSim.Pcb.BoardDocument.Synchronize(s, Lib);
            var files = RobotTwin.CoreSim.Pcb.KiCadExporter.GenerateProject(s, Lib, board, "hier");
            string root = files["hier/hier.kicad_sch"], sub = files["hier/Motor.kicad_sch"];
            Assert.Contains("(sheet", root);
            Assert.Contains("\"Sheetfile\" \"Motor.kicad_sch\"", root);
            Assert.Contains("\"R1\"", root);
            Assert.DoesNotContain("\"R2\"", root);   // each part in its own sheet's file
            Assert.Contains("\"R2\"", sub);
            Assert.DoesNotContain("sheet_instances", sub);
            Assert.Contains("/Motor/IN", sub);   // the nets still join by their global labels
            Assert.Contains("/Motor/IN", root);
        }

        [Fact]
        public void KiCadRoundTripKeepsTheSheets()
        {
            var (s, _, _, _) = Design();
            var board = RobotTwin.CoreSim.Pcb.BoardDocument.Synchronize(s, Lib);
            var files = RobotTwin.CoreSim.Pcb.KiCadExporter.GenerateProject(s, Lib, board, "hier");
            var back = RobotTwin.CoreSim.Formats.KiCadSchematicImport.Import(files["hier/hier.kicad_sch"], Lib,
                name => files.TryGetValue("hier/" + System.IO.Path.GetFileName(name), out var t) ? t : null).Schematic;
            var motor = Assert.Single(back.Sheets);
            Assert.Equal("Motor", motor.Name);
            Assert.Equal(motor.Uid, back.Parts.Single(p => p.RefDes == "R2").Sheet);   // into its own sheet again
            Assert.Null(back.Parts.Single(p => p.RefDes == "R1").Sheet);

            string Groups(Schematic x) => string.Join(" | ", x.BuildNetlist(Lib).Nets.Where(n => n.Connections.Count > 0)
                .Select(n => string.Join(",", n.Connections.Select(c => c.ToString()).OrderBy(c => c, StringComparer.Ordinal))).OrderBy(g => g, StringComparer.Ordinal));
            Assert.Equal(Groups(s), Groups(back));   // the same pins on the same nets
        }

        [Fact]
        public void SheetsAreSavedAndLoaded()
        {
            var (s, motor, _, _) = Design();
            var back = Schematic.FromJson(s.ToJson());
            var m = Assert.Single(back.Sheets);
            Assert.Equal(motor.Uid, m.Uid);
            Assert.Equal("IN", Assert.Single(m.Pins).Name);
            Assert.Equal(motor.Uid, back.Parts.Single(p => p.RefDes == "R2").Sheet);
            Assert.True(back.Labels.Single(l => l.Name == "IN").Hierarchical);
            Assert.Null(back.Parts.Single(p => p.RefDes == "R1").Sheet);
            Assert.Equal(s.BuildNetlist(Lib).Nets.Select(n => n.Name).OrderBy(n => n), back.BuildNetlist(Lib).Nets.Select(n => n.Name).OrderBy(n => n));
            Assert.DoesNotContain("\"sheet\"", new Schematic { Parts = { new SchematicPart { RefDes = "R9", PartId = "resistor" } } }.ToJson());   // a one-sheet file stays as it was
        }
    }
}
