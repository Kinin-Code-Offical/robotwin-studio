using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [gerber-check] The exported fabrication files read back by an independent reader and laid over the board: a whole
    /// routed board matches; files spoiled the ways real exports go wrong do not, and the report says where.
    /// </summary>
    public class GerberCheckTests
    {
        private readonly ITestOutputHelper _out;

        public GerberCheckTests(ITestOutputHelper output) => _out = output;

        internal static BoardDocument RoutedBoard()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            board.CornerRadius = 3;   // the outline's corners are arcs
            AutoPlacer.Place(board);
            Assert.True(new AutoRouter(board).Route().Complete);
            board.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.Bottom });
            board.MountingHoles.Add(new MountingHole { X = 4, Y = 4, Drill = 3.2, PadDiameter = 6 });
            board.MountingHoles.Add(new MountingHole { X = 66, Y = 46, Drill = 3.2 });
            board.Texts.Add(new BoardText { Text = "ROBOTWIN", X = 35, Y = 46, Size = 1.5 });
            ZoneFiller.FillAll(board);
            return board;
        }

        [Fact]
        public void AWholeBoardsFilesMatchIt()
        {
            var board = RoutedBoard();
            var files = GerberExporter.Generate(board, "motor");
            var report = GerberCheck.Check(board, files);
            foreach (var l in report.Layers) _out.WriteLine($"{l.File} [{l.Function}] {l.ExpectedArea:0.0} / {l.FileArea:0.0} mm² {(l.Ok ? "ok" : string.Join("; ", l.Differences.Take(3).Concat<object>(l.Warnings.Take(3))))}");
            foreach (var d in report.Drills) _out.WriteLine($"{d.File} [{d.Function}] {d.Expected} / {d.Found} {(d.Ok ? "ok" : string.Join("; ", d.Problems.Take(3)))}");
            _out.WriteLine(report.Summary());
            Assert.True(report.Ok, report.Summary());
            Assert.Equal(9, report.Layers.Count);   // 2 copper, 2 mask, 2 paste, 2 silk, outline
            // the motor driver is all through-hole: no paste, nothing on the bottom silk
            Assert.All(report.Layers.Where(l => !l.Function.StartsWith("Paste", StringComparison.Ordinal) && l.Function != "Legend,Bot"), l => Assert.True(l.FileArea > 0, l.File));
            Assert.Contains(report.Drills, d => d.Function.StartsWith("Plated", StringComparison.Ordinal) && d.Expected > 10);
            Assert.Contains(report.Drills, d => d.Function.StartsWith("NonPlated", StringComparison.Ordinal) && d.Expected == 1);
        }

        private static (BoardDocument Board, Dictionary<string, string> Files) Spoiled(string suffix, Func<string, string> spoil)
        {
            var board = RoutedBoard();
            var files = GerberExporter.Generate(board, "motor");
            string key = files.Keys.Single(k => k.EndsWith(suffix, StringComparison.Ordinal));
            string before = files[key];
            files[key] = spoil(before);
            Assert.NotEqual(before, files[key]);
            return (board, files);
        }

        [Fact]
        public void AMissingPadIsFoundWhereItIs()
        {
            // drop the last flash of the top copper (the plated mounting hole's pad, flashed last)
            var (board, files) = Spoiled("F_Cu.gtl", g =>
            {
                int at = g.LastIndexOf("D03*", StringComparison.Ordinal);
                int start = g.LastIndexOf('\n', at - 1) + 1;
                return g.Remove(start, at + 5 - start);
            });
            var report = GerberCheck.Check(board, files);
            var layer = report.Layers.Single(l => l.File.EndsWith("F_Cu.gtl", StringComparison.Ordinal));
            _out.WriteLine(report.Summary());
            Assert.False(report.Ok);
            var spot = Assert.Single(layer.Differences);
            Assert.True(spot.Missing);
            var flashed = board.Pads().Where(p => p.OnLayer(CopperLayer.Top)).Select(p => (p.X, p.Y))
                .Concat(board.Vias.Select(v => (v.X, v.Y))).Concat(board.MountingHoles.Where(h => h.Plated).Select(h => (h.X, h.Y)));
            Assert.Contains(flashed, p => Math.Abs(p.X - spot.X) < 0.3 && Math.Abs(p.Y - spot.Y) < 0.3);
            Assert.InRange(spot.Area, Math.PI * 9 * 0.95, Math.PI * 9 * 1.02);   // the 6 mm pad, whole
        }

        [Fact]
        public void WrongUnitsAreCaught()
        {
            var (board, files) = Spoiled("B_Mask.gbs", g => g.Replace("%MOMM*%", "%MOIN*%"));
            var report = GerberCheck.Check(board, files);
            var layer = report.Layers.Single(l => l.File.EndsWith("B_Mask.gbs", StringComparison.Ordinal));
            _out.WriteLine(report.Summary());
            Assert.True(layer.Differences.Count > 3);
        }

        [Fact]
        public void AnArcTurningTheWrongWayIsCaught()
        {
            // the outline's rounded corners drawn the other way round: each becomes a three-quarter circle
            var (board, files) = Spoiled("Edge_Cuts.gm1", g => g.Replace("G02", "G0X").Replace("G03", "G02").Replace("G0X", "G03"));
            var report = GerberCheck.Check(board, files);
            var layer = report.Layers.Single(l => l.File.EndsWith("Edge_Cuts.gm1", StringComparison.Ordinal));
            _out.WriteLine(string.Join("; ", layer.Differences));
            Assert.True(layer.Differences.Count(d => !d.Missing) >= 4, "each corner's wrong arc is extra copper on the outline");
        }

        [Fact]
        public void AMissingOrResizedHoleIsCaught()
        {
            var (board, files) = Spoiled("-PTH.drl", d =>
            {
                var lines = d.Split('\n').ToList();
                int hole = lines.FindLastIndex(l => l.StartsWith("X", StringComparison.Ordinal));
                lines.RemoveAt(hole);
                int tool = lines.FindIndex(l => l.StartsWith("T1C", StringComparison.Ordinal));
                lines[tool] = "T1C9.999";
                return string.Join("\n", lines);
            });
            var report = GerberCheck.Check(board, files);
            var pth = report.Drills.Single(d => d.File.EndsWith("-PTH.drl", StringComparison.Ordinal));
            _out.WriteLine(string.Join("; ", pth.Problems.Take(5)));
            Assert.Contains(pth.Problems, p => p.StartsWith("delik eksik", StringComparison.Ordinal));
            Assert.Contains(pth.Problems, p => p.StartsWith("delik çapı farklı", StringComparison.Ordinal));
        }

        [Fact]
        public void TheReaderPaintsRegionsArcsAndClearPolarity()
        {
            // a 5 mm radius disc as a region of two G03 half circles, a 2 mm radius hole cleared out of it, an 0.5 mm line
            const string gerber = "%FSLAX46Y46*%\n%MOMM*%\n%ADD10C,0.5*%\n%ADD11R,2X1*%\n%LPD*%\nG75*\nG36*\nX15000000Y10000000D02*\n"
                + "G03X5000000Y10000000I-5000000J0D01*\nG03X15000000Y10000000I5000000J0D01*\nG37*\n%LPC*%\nD10*\nG01*\n"
                + "X8000000Y10000000D02*\nG75*\nG03X8000000Y10000000I2000000J0D01*\n%LPD*%\nD11*\nX20000000Y20000000D03*\nM02*\n";
            var image = GerberReader.Parse(gerber);
            Assert.Empty(image.Warnings);
            var r = new GerberRaster(0, 0, 25, 25, 0.01);
            r.Draw(image);
            double area = r.Count() * 1e-4;
            // disc 25π, minus the ring drawn with a 0.5 mm clear pen around a 2 mm circle (≈ 2π·2·0.5), plus a 2×1 flash
            double expected = Math.PI * 25 - 2 * Math.PI * 2 * 0.5 + 2;
            _out.WriteLine($"{area:0.000} mm² (expected ≈ {expected:0.000})");
            Assert.InRange(area, expected - 0.15, expected + 0.15);
            Assert.True(r.Bits[(int)(10 / 0.01) * r.W + (int)(10 / 0.01)], "the middle of the disc stays dark: only the pen's ring was cleared");
        }

        [Fact]
        public void ExcellonInOtherDialectsIsRead()
        {
            // inch, leading zeros kept (the format digits 2.4), integer coordinates
            var (holes, warnings) = GerberReader.ParseExcellon("M48\nINCH,LZ\nT1C0.0320\n%\nT1\nX010000Y005000\nX02Y01\nM30\n");
            Assert.Empty(warnings);
            Assert.Equal(2, holes.Count);
            Assert.Equal(0.8128, holes[0].D, 4);
            Assert.Equal(25.4, holes[0].X, 3);
            Assert.Equal(12.7, holes[0].Y, 3);
            Assert.Equal(50.8, holes[1].X, 3);
        }
    }
}
