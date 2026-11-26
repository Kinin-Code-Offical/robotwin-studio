using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using RobotTwin.CoreSim.Sandbox;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [variants] Alternatives of one design: the same schematic with parts left off, values changed or parts swapped. The
    /// active one is what is simulated and listed; the drawing and the board stay one.
    /// </summary>
    public class VariantTests
    {
        private static PartLibrary Lib => PartLibrary.Default;

        private static (Schematic Sch, SchematicPart R1, SchematicPart Led) Divider()
        {
            var sch = new Schematic { Title = "LED" };
            var bat = sch.AddPart(Lib, "bat-9v", new GridPoint(0, 0));
            var r1 = sch.AddPart(Lib, "res-1000", new GridPoint(20, 0));
            var led = sch.AddPart(Lib, "led3-red", new GridPoint(40, 0));
            return (sch, r1, led);
        }

        [Fact]
        public void TheDesignAsDrawnIsItsOwnResolution()
        {
            var (sch, _, _) = Divider();
            Assert.Same(sch, Variants.Active(sch));
            sch.Variants.Add(new DesignVariant { Name = "Boş" });
            sch.ActiveVariant = "Boş";
            Assert.Same(sch, Variants.Active(sch));   // an alternative that changes nothing
        }

        [Fact]
        public void AnAlternativeLeavesPartsOffChangesValuesAndSwapsParts()
        {
            var (sch, r1, led) = Divider();
            var v = new DesignVariant { Name = "Parlak" };
            v.Edit(r1).Parameters["resistance"] = "330";
            v.Edit(led).PartId = "led3-green";
            sch.Variants.Add(v);
            var off = new DesignVariant { Name = "LED'siz" };
            off.Edit(led).NotFitted = true;
            sch.Variants.Add(off);

            sch.ActiveVariant = "Parlak";
            var bright = Variants.Active(sch);
            Assert.NotSame(sch, bright);
            Assert.Equal("330", bright.FindPart(r1.RefDes)!.Parameters["resistance"]);
            Assert.Equal("led3-green", bright.FindPart(led.RefDes)!.PartId);
            Assert.False(sch.FindPart(r1.RefDes)!.Parameters.ContainsKey("resistance"));   // the drawing is untouched

            sch.ActiveVariant = "LED'siz";
            var without = Variants.Active(sch);
            Assert.Null(without.FindPart(led.RefDes));
            Assert.Equal(3, sch.Parts.Count);

            // The bill of materials is the alternative's.
            Assert.DoesNotContain(BomBuilder.Build(without, Lib), l => l.References.Contains(led.RefDes));
        }

        [Fact]
        public void ARenamedPartKeepsItsOverrides()
        {
            var (sch, r1, _) = Divider();
            var v = new DesignVariant { Name = "330" };
            v.Edit(r1).Parameters["resistance"] = "330";
            sch.Variants.Add(v);
            sch.ActiveVariant = "330";
            r1.RefDes = "R9";
            Assert.Equal("330", Variants.Active(sch).FindPart("R9")!.Parameters["resistance"]);
        }

        [Fact]
        public void AlternativesTravelWithTheFile()
        {
            var (sch, r1, led) = Divider();
            var v = new DesignVariant { Name = "LED'siz", Note = "pil ömrü için" };
            v.Edit(led).NotFitted = true;
            v.Edit(r1).Parameters["resistance"] = "4.7k";
            sch.Variants.Add(v);
            sch.ActiveVariant = "LED'siz";
            var back = Schematic.FromJson(sch.ToJson());
            var w = Assert.Single(back.Variants);
            Assert.Equal("pil ömrü için", w.Note);
            Assert.True(w.Parts[led.Uid].NotFitted);
            Assert.Equal("4.7k", w.Parts[r1.Uid].Parameters["resistance"]);
            Assert.Equal("LED'siz", back.ActiveVariant);
        }

        [Fact]
        public void TidyingDropsOverridesOfPartsThatAreGone()
        {
            var (sch, r1, led) = Divider();
            var v = new DesignVariant { Name = "x" };
            v.Edit(led).NotFitted = true;
            v.Edit(r1);   // empty
            sch.Parts.Remove(led);
            v.Tidy(sch);
            Assert.Empty(v.Parts);
        }
            [Fact]
        public void TheFabricationFilesAreTheAlternatives()
        {
            var (sch, r1, led) = Divider();
            var board = BoardDocument.Synchronize(sch, Lib);
            var v = new DesignVariant { Name = "x" };
            v.Edit(led).NotFitted = true;
            v.Edit(r1).Parameters["resistance"] = "330";
            sch.Variants.Add(v);
            sch.ActiveVariant = "x";
            var fitting = Variants.FittingOf(sch, Lib)!;
            string bom = GerberExporter.Bom(board, fitting), cpl = GerberExporter.PickAndPlace(board, fitting);
            bool Placed(string csv, string refDes) => csv.Split('\n').Any(l => l.StartsWith(refDes + ",", System.StringComparison.Ordinal));
            Assert.False(Placed(cpl, led.RefDes));
            Assert.DoesNotContain("\"" + led.RefDes + "\"", bom);
            Assert.Contains("\"330\"", bom);
            Assert.True(Placed(GerberExporter.PickAndPlace(board), led.RefDes));   // the design as drawn
            Assert.NotNull(board.Find(led.RefDes));   // its pads stay on the board
        }

        [Fact]
        public void KiCadMarksThePartsTheChosenAlternativeLeavesOff()
        {
            var (sch, _, led) = Divider();
            Assert.DoesNotContain("(dnp yes)", KiCadExporter.ExportSchematic(sch, Lib));
            var v = new DesignVariant { Name = "x" };
            v.Edit(led).NotFitted = true;
            sch.Variants.Add(v);
            sch.ActiveVariant = "x";
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(KiCadExporter.ExportSchematic(sch, Lib), @"\(dnp yes\)"));
        }

        [Fact]
        public void TheMontajPutsTogetherTheActiveAlternative()
        {
            var sch = VehicleTemplates.ObstacleAvoider(Lib);
            sch.Assembly = null;
            var package = new ProjectPackage { Schematic = sch };
            var w = ProjectAssembly.Build(package, Lib);
            var sonar = sch.Parts.Single(p => p.PartId == "hc-sr04");
            string refOf = sch.ProjectId + "/" + sonar.RefDes;
            Assert.Contains(w.Objects, o => o.ProjectRef == refOf);

            var v = new DesignVariant { Name = "Sensörsüz" };
            v.Edit(sonar).NotFitted = true;
            sch.Variants.Add(v);
            sch.ActiveVariant = v.Name;
            Assert.Equal(1, ProjectAssembly.Sync(w, package, Lib).Removed);
            Assert.DoesNotContain(w.Objects, o => o.ProjectRef == refOf);

            sch.ActiveVariant = null;
            Assert.Equal(1, ProjectAssembly.Sync(w, package, Lib).Added);
            Assert.Contains(w.Objects, o => o.ProjectRef == refOf);
        }
            [Fact]
        public void AlternativesAreComparedOnTheSameRun()
        {
            // 9 V, a resistor and an LED: 330 Ω in place of 1 kΩ draws about three times the power; without the LED nothing flows.
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-9v", new GridPoint(0, 0));
            var r = sch.AddPart(Lib, "res-1000", new GridPoint(20, 0));
            var led = sch.AddPart(Lib, "led3-red", new GridPoint(40, 0));
            VehicleTemplates.Tie(sch, Lib, bat, "+", "VBAT");
            VehicleTemplates.Tie(sch, Lib, bat, "-", "GND");
            VehicleTemplates.Tie(sch, Lib, r, "A", "VBAT");
            VehicleTemplates.Tie(sch, Lib, r, "B", "LA");
            VehicleTemplates.Tie(sch, Lib, led, "A", "LA");
            VehicleTemplates.Tie(sch, Lib, led, "K", "GND");
            var bright = new DesignVariant { Name = "Parlak" };
            bright.Edit(r).Parameters["resistance"] = "330";
            var off = new DesignVariant { Name = "LED'siz" };
            off.Edit(led).NotFitted = true;
            sch.Variants.Add(bright);
            sch.Variants.Add(off);

            var results = VariantComparison.Run(sch, Lib, 0.2, "LA");
            Assert.Equal(new[] { "", "Parlak", "LED'siz" }, results.Select(x => x.Name));
            Assert.All(results, x => Assert.Null(x.Error));
            var drawn = results[0];
            Assert.InRange(drawn.SupplyW, 0.04, 0.08);   // 9 V × (9 − 2) V / 1 kΩ
            Assert.InRange(results[1].SupplyW / drawn.SupplyW, 2.5, 3.5);
            Assert.True(results[2].SupplyW < drawn.SupplyW * 0.01, results[2].SupplyW.ToString("G3"));
            Assert.Equal(drawn.Parts - 1, results[2].Parts);
            Assert.InRange(drawn.NetV, 1.6, 2.4);   // the LED's forward voltage
            Assert.True(drawn.ChargeMah > 0);
        }

        /// <summary>[variants-code] With the boards' code running, the alternatives differ as the code makes them: an Uno lights a strip in one, not in the other.</summary>
        [Fact]
        public void AlternativesAreComparedWithTheBoardsCodeRunning()
        {
            // the Uno on its USB power, the strip on the Uno's own 5 V pin
            var sch = new Schematic();
            var uno = sch.AddPart(Lib, "arduino-uno", new GridPoint(30, 0));
            var strip = sch.AddPart(Lib, "ws2812-strip", new GridPoint(80, 0));
            strip.Parameters["count"] = "3";
            uno.Parameters["firmware"] = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Fixtures", "neopixel-avr.hex");
            VehicleTemplates.Tie(sch, Lib, uno, "5V", "5V");
            VehicleTemplates.Tie(sch, Lib, uno, "GND1", "GND");
            VehicleTemplates.Tie(sch, Lib, strip, "VDD", "5V");
            VehicleTemplates.Tie(sch, Lib, strip, "GND", "GND");
            VehicleTemplates.Tie(sch, Lib, uno, "D6", "DATA");
            VehicleTemplates.Tie(sch, Lib, strip, "DIN", "DATA");
            var bare = new DesignVariant { Name = "Şeritsiz" };
            bare.Edit(strip).NotFitted = true;
            sch.Variants.Add(bare);

            var withCode = VariantComparison.Run(sch, Lib, 0.2, null, 25, default, new RunnerOptions());
            Assert.All(withCode, x => Assert.Null(x.Error));
            Assert.Contains("shown 2", withCode[0].Serial[uno.RefDes]);   // the code ran in each
            Assert.Contains("shown 2", withCode[1].Serial[uno.RefDes]);
            // the strip after the code's second frame: LED0 (20, 10, 30), LED1 half green, LED2 dim blue, ~1 mA each idle: ~18 mA at 5 V
            double lit = withCode[0].SupplyW - withCode[1].SupplyW;
            Assert.True(lit is > 0.07 and < 0.11, $"lit {lit:G3} W: {withCode[0].SupplyW:G4} vs {withCode[1].SupplyW:G4}; {string.Join(" | ", withCode[0].Notes)}; {withCode[0].Serial[uno.RefDes]}");

            var circuitOnly = VariantComparison.Run(sch, Lib, 0.2);
            Assert.True(circuitOnly[0].SupplyW - circuitOnly[1].SupplyW < 0.05, "without the code the strip stays dark");
            Assert.Empty(circuitOnly[0].Serial);
        }
    }
}
