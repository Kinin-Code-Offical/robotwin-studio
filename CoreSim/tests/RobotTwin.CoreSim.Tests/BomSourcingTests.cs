using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[bom] The motor driver's BOM made ready to buy: sources, costing for a batch, the suppliers' order files.</summary>
    public class BomSourcingTests
    {
        private readonly ITestOutputHelper _out;

        public BomSourcingTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void PartsWithAModelGetItAsTheirMpn()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var lines = BomBuilder.Build(sch, PartLibrary.Default);
            foreach (var l in lines) _out.WriteLine($"{l.ReferenceText,-8} {l.Value,-14} {l.Footprint,-12} MPN '{BomSourcing.SourceOf(sch, PartLibrary.Default, l).Mpn}'");
            var q = lines.Single(l => l.References.Contains("Q1"));
            Assert.Equal("IRLZ44N", BomSourcing.SourceOf(sch, PartLibrary.Default, q).Mpn, ignoreCase: true);
        }

        [Fact]
        public void ABatchIsCostedWithTheSuppliersMinimums()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var lib = PartLibrary.Default;
            var lines = BomBuilder.Build(sch, lib);
            var resistor = lines.First(l => l.References[0].StartsWith("R", StringComparison.Ordinal));
            var mosfet = lines.Single(l => l.References.Contains("Q1"));
            var diode = lines.Single(l => l.References.Contains("D1"));
            BomSourcing.Keep(sch, resistor, new BomSource { Manufacturer = "UNI-ROYAL", Mpn = "MFR-25FBF52", Supplier = "LCSC", SupplierPn = "C58673", UnitPrice = 0.004, MinimumOrder = 100 });
            BomSourcing.Keep(sch, mosfet, new BomSource { Manufacturer = "Infineon", Mpn = "IRLZ44NPBF", Supplier = "Digi-Key", SupplierPn = "IRLZ44NPBF-ND", UnitPrice = 1.42 });
            BomSourcing.Keep(sch, diode, new BomSource { Manufacturer = "onsemi", Mpn = "1N5819RLG", Supplier = "Mouser", SupplierPn = "863-1N5819RLG", UnitPrice = 0.31, MinimumOrder = 1 });

            var cost = BomSourcing.Cost(sch, lib, lines, 10);
            foreach (var l in cost.Lines) _out.WriteLine($"{l.Line.ReferenceText,-8} need {l.Needed,3} order {l.Order,3} × {l.Source.UnitPrice:0.###} = {l.Cost:0.00}");
            _out.WriteLine($"total {cost.Total:0.00} {cost.Currency}, {cost.PerBoard:0.00} a board, {cost.Unpriced} lines without a price");
            var r = cost.Lines.Single(l => l.Line == resistor);
            Assert.Equal(resistor.Quantity * 10, r.Needed);
            Assert.Equal(100, r.Order);                                   // the reel's minimum, not the 10 the boards need
            Assert.Equal(0.4, r.Cost, 6);
            Assert.Equal(14.2, cost.Lines.Single(l => l.Line == mosfet).Cost, 6);
            Assert.Equal(0.4 + 14.2 + 3.1, cost.Total, 6);
            Assert.Equal(cost.Total / 10, cost.PerBoard, 6);
            Assert.Equal(lines.Count - 3, cost.Unpriced);

            // each supplier's own order file carries only its lines
            string jlc = BomSourcing.ToJlcpcb(cost), dk = BomSourcing.ToDigiKey(cost), mouser = BomSourcing.ToMouser(cost);
            _out.WriteLine(jlc + dk + mouser);
            Assert.Equal(2, jlc.Trim().Split('\n').Length);
            Assert.Contains("C58673", jlc);
            Assert.Contains("10,IRLZ44NPBF-ND,Q1", dk);
            Assert.Contains("863-1N5819RLG,10,D1", mouser);
            Assert.Contains("MFR-25FBF52", BomSourcing.ToCsv(cost));
        }

        [Fact]
        public void TheBenchsInstrumentsAreNotBoughtForTheBoard()
        {
            var sch = new Schematic();
            sch.AddPart(PartLibrary.Default, "res-1000", new GridPoint(0, 0));
            sch.AddPart(PartLibrary.Default, "signal-generator", new GridPoint(20, 0));
            sch.AddPart(PartLibrary.Default, "multimeter", new GridPoint(40, 0));
            sch.AddPart(PartLibrary.Default, "line-fault", new GridPoint(60, 0));
            var bom = BomBuilder.Build(sch, PartLibrary.Default);
            var line = Assert.Single(bom);
            Assert.Equal("R1", line.ReferenceText);
        }

        [Fact]
        public void TheSourcesAreSavedWithTheSchematic()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var line = BomBuilder.Build(sch, PartLibrary.Default).Single(l => l.References.Contains("Q1"));
            BomSourcing.Keep(sch, line, new BomSource { Mpn = "IRLZ44NPBF", Supplier = "Digi-Key", UnitPrice = 1.42 });
            sch.BomBoards = 25;
            var json = System.Text.Json.JsonSerializer.Serialize(sch, RobotTwin.CoreSim.Formats.RobotWinFormats.Json);
            var back = System.Text.Json.JsonSerializer.Deserialize<Schematic>(json, RobotTwin.CoreSim.Formats.RobotWinFormats.Json)!;
            var again = BomBuilder.Build(back, PartLibrary.Default).Single(l => l.References.Contains("Q1"));
            var s = BomSourcing.SourceOf(back, PartLibrary.Default, again);
            Assert.Equal(("IRLZ44NPBF", "Digi-Key", 1.42), (s.Mpn, s.Supplier, s.UnitPrice));
            Assert.Equal(25, back.BomBoards);

            BomSourcing.Keep(back, again, new BomSource());   // cleared: forgotten
            Assert.Empty(back.Sourcing);
        }
    }
}
