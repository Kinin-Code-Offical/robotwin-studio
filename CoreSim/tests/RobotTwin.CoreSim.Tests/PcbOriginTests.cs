using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[origins] KiCad's grid origin and drill/place file origin: the fabrication files measure from the latter, and both survive saving and KiCad.</summary>
    public class PcbOriginTests
    {
        private static BoardDocument Board()
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.Vias.Add(new Via { X = 15, Y = 25, Diameter = 0.8, Drill = 0.4, Net = "N" });
            return b;
        }

        [Fact]
        public void WithoutAnOriginTheFilesMeasureFromTheBottomLeftCorner()
        {
            var files = GerberExporter.Generate(Board(), "t");
            Assert.Contains("X15000000Y5000000D03*", files["t-F_Cu.gtl"]);
            Assert.Contains("X15.000Y5.000", files["t-PTH.drl"]);
        }

        [Fact]
        public void TheDrillPlaceOriginMovesEveryFabricationFile()
        {
            var b = Board();
            b.AuxOrigin = new PcbPoint(10, 20);
            var files = GerberExporter.Generate(b, "t");
            Assert.Contains("X5000000Y-5000000D03*", files["t-F_Cu.gtl"]);   // (15 − 10, 20 − 25), Y up
            Assert.Contains("X5.000Y-5.000", files["t-PTH.drl"]);
            // the independent reader still lays every file over the board exactly
            var report = GerberCheck.Check(b, files);
            Assert.All(report.Layers, l => Assert.Empty(l.Differences));
            Assert.All(report.Drills, d => Assert.True(d.Ok, string.Join("; ", d.Problems)));
        }

        [Fact]
        public void AWholeRoutedBoardStillChecksWithAnOrigin()
        {
            var b = GerberCheckTests.RoutedBoard();
            b.AuxOrigin = new PcbPoint(4, 46);   // the bottom-left mounting hole's centre
            var report = GerberCheck.Check(b, GerberExporter.Generate(b, "m"));
            Assert.All(report.Layers, l => Assert.True(l.Ok, l.File + ": " + string.Join("; ", l.Differences.Take(3))));
            Assert.All(report.Drills, d => Assert.True(d.Ok, d.File + ": " + string.Join("; ", d.Problems.Take(3))));
            var cpl = GerberExporter.Generate(b, "m")["m-CPL.csv"].Split('\n')[1].Split(',');
            var comp = b.Components.OrderBy(c => c.RefDes, System.StringComparer.Ordinal).First(c => FootprintLibrary.Get(c.FootprintId) != null);
            var box = comp.Courtyard(FootprintLibrary.Get(comp.FootprintId)!);
            Assert.Equal(((box.MinX + box.MaxX) / 2 - 4).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "mm", cpl[3]);
        }

        [Fact]
        public void BothOriginsSurviveSavingAndKiCad()
        {
            var b = Board();
            b.AuxOrigin = new PcbPoint(5, 25);
            b.GridOrigin = new PcbPoint(2.5, 2.5);
            var copy = b.Clone();
            Assert.Equal(new PcbPoint(5, 25), copy.AuxOrigin);
            Assert.Equal(new PcbPoint(2.5, 2.5), copy.GridOrigin);
            Assert.Null(Board().Clone().AuxOrigin);

            var text = KiCadExporter.ExportPcb(b, "t");
            Assert.Contains("(aux_axis_origin 5 25)", text);
            Assert.Contains("(grid_origin 2.5 2.5)", text);
            var layout = KiCadBoardImport.Read(text);
            var back = Board();
            BoardDiff.Apply(back, layout);
            Assert.Equal(5, back.AuxOrigin!.Value.X, 6);
            Assert.Equal(25, back.AuxOrigin!.Value.Y, 6);
            Assert.Equal(2.5, back.GridOrigin!.Value.X, 6);
        }
    }
}
