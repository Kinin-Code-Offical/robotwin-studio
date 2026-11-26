using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[kicad-in] A board exported to KiCad, worked on there, and brought back: its layout read, compared and carried over.</summary>
    public class KiCadBoardImportTests
    {
        private readonly ITestOutputHelper _out;

        public KiCadBoardImportTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void AnExportedBoardComesBackUnchanged()
        {
            var board = GerberCheckTests.RoutedBoard();
            var layout = KiCadBoardImport.Read(KiCadExporter.ExportPcb(board, "motor"));
            var changes = BoardDiff.Compare(board, layout);
            foreach (var c in changes) _out.WriteLine(c.Text);
            _out.WriteLine($"{layout.Components.Count} parts, {layout.Tracks.Count} tracks, {layout.Vias.Count} vias, {layout.Zones.Count} zones, {layout.Holes.Count} holes; shift {layout.Shift}; {string.Join("; ", layout.Warnings)}");
            Assert.Empty(changes);
            Assert.Equal(board.Components.Count, layout.Components.Count);
            Assert.Equal(board.Tracks.Count, layout.Tracks.Count);
            Assert.Equal(board.MountingHoles.Count, layout.Holes.Count);
            Assert.Equal(1.6, layout.Thickness!.Value, 3);
        }

        [Fact]
        public void WorkDoneInKiCadIsListedAndCarriedOver()
        {
            var board = GerberCheckTests.RoutedBoard();
            var layout = KiCadBoardImport.Read(KiCadExporter.ExportPcb(board, "motor"));
            // what a designer did in KiCad: moved a resistor, turned the regulator, deleted one track
            var r = layout.Components.First(c => c.RefDes.StartsWith("R", StringComparison.Ordinal));
            var u = layout.Components.First(c => c.RefDes.StartsWith("U", StringComparison.Ordinal));
            r.X += 5;
            u.Rotation = (u.Rotation + 90) % 360;
            var gone = layout.Tracks[0];
            layout.Tracks.RemoveAt(0);

            var changes = BoardDiff.Compare(board, layout);
            foreach (var c in changes) _out.WriteLine(c.Text);
            Assert.Contains(changes, c => c.What == "moved" && c.Text.StartsWith(r.RefDes + " ", StringComparison.Ordinal));
            Assert.Contains(changes, c => c.What == "turned" && c.Text.StartsWith(u.RefDes + " ", StringComparison.Ordinal));
            Assert.Contains(changes, c => c.What == "routed" && c.Text.StartsWith(gone.Net + ":", StringComparison.Ordinal));

            int n = BoardDiff.Apply(board, layout);
            Assert.Equal(changes.Count(c => c.Applies), n);
            Assert.Equal(r.X, board.Components.Single(c => c.RefDes == r.RefDes).X, 6);
            Assert.Equal((int)u.Rotation, board.Components.Single(c => c.RefDes == u.RefDes).Rotation);
            Assert.Equal(layout.Tracks.Count, board.Tracks.Count);
            Assert.Empty(BoardDiff.Compare(board, layout));   // now they agree
        }

        [Fact]
        public void ABoardDrawnInKiCadIsShiftedToItsCornerAndItsArcsFollowed()
        {
            // drawn where KiCad's page puts it (corner at 100, 50), a part at 45°, an arc track, a KiCad 6 style reference
            const string pcb = "(kicad_pcb (version 20221018) (generator pcbnew)\n" +
                "  (general (thickness 1.2))\n" +
                "  (layers (0 \"F.Cu\" signal) (31 \"B.Cu\" signal) (44 \"Edge.Cuts\" user))\n" +
                "  (net 0 \"\") (net 1 \"GND\")\n" +
                "  (gr_rect (start 100 50) (end 150 90) (stroke (width 0.1) (type default)) (layer \"Edge.Cuts\"))\n" +
                "  (footprint \"Resistor_SMD:R_0603\" (layer \"F.Cu\") (at 110 60 45)\n" +
                "    (fp_text reference \"R1\" (at 0 -1.4) (layer \"F.SilkS\")) (fp_text value \"10k\" (at 0 1.4) (layer \"F.Fab\"))\n" +
                "    (pad \"1\" smd rect (at -0.8 0 45) (size 0.8 0.9) (layers \"F.Cu\") (net 1 \"GND\")))\n" +
                "  (arc (start 120 60) (mid 127.071 62.929) (end 130 70) (width 0.3) (layer \"F.Cu\") (net 1))\n" +
                "  (via (at 140 80) (size 0.8) (drill 0.4) (layers \"F.Cu\" \"B.Cu\") (net 1))\n" +
                ")\n";
            var layout = KiCadBoardImport.Read(pcb);
            _out.WriteLine($"shift {layout.Shift}, size {layout.Size}, {layout.Tracks.Count} chords, warnings: {string.Join("; ", layout.Warnings)}");
            Assert.Equal((100.0, 50.0), layout.Shift);
            Assert.Equal((50.0, 40.0), layout.Size!.Value);
            var r1 = Assert.Single(layout.Components);
            Assert.Equal("R1", r1.RefDes);
            Assert.Equal("10k", r1.Value);
            Assert.Equal((10.0, 10.0, 45.0), (r1.X, r1.Y, r1.Rotation));
            Assert.True(layout.Tracks.Count >= 9, "a quarter circle in chords of at most 10°");
            Assert.All(layout.Tracks, t => Assert.Equal("GND", t.Net));
            Assert.Equal((20.0, 10.0), (layout.Tracks[0].X1, layout.Tracks[0].Y1));
            Assert.Equal(30.0, layout.Tracks.Last().X2, 6);
            Assert.Equal(20.0, layout.Tracks.Last().Y2, 6);
            double mid = layout.Tracks.Select(t => Math.Sqrt(Math.Pow(t.X2 - 20, 2) + Math.Pow(t.Y2 - 20, 2))).Max();
            Assert.InRange(mid, 9.99, 10.01);   // every chord end on the 10 mm circle round (20, 20)
            var via = Assert.Single(layout.Vias);
            Assert.Equal((40.0, 30.0), (via.X, via.Y));
            Assert.Equal(1.2, layout.Thickness!.Value, 6);

            // against a board that has R1: its 45° is reported and rounded to a quarter turn
            var board = new BoardDocument { Width = 50, Height = 40 };
            board.Components.Add(new BoardComponent { RefDes = "R1", FootprintId = "R_0603", X = 10, Y = 10 });
            var changes = BoardDiff.Compare(board, layout);
            foreach (var c in changes) _out.WriteLine(c.Text);
            Assert.Contains(changes, c => c.What == "angle");
        }
    }
}
