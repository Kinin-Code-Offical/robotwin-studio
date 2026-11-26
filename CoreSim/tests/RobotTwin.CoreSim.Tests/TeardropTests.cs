using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[teardrop] Tracks flaring into vias and pads, as KiCad and Altium do, never closer to another net than the rules allow.</summary>
    public class TeardropTests
    {
        private static BoardDocument Board(bool on = true)
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.Teardrops.Enabled = on;
            b.Vias.Add(new Via { Net = "SIG", X = 10, Y = 15, Diameter = 0.9, Drill = 0.45 });
            b.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 10, Y1 = 15, X2 = 25, Y2 = 15, Width = 0.2 });
            return b;
        }

        [Fact]
        public void TheyAreOffUntilSwitchedOn()
        {
            Assert.Empty(Teardrops.Of(Board(on: false)));
        }

        [Fact]
        public void AThinTrackFlaresIntoItsVia()
        {
            var board = Board();
            var drop = Assert.Single(Teardrops.Of(board));
            Assert.Equal("SIG", drop.Net);
            Assert.Equal(CopperLayer.Top, drop.Layer);
            var (minX, minY, maxX, maxY) = Polygon.Bounds(drop.Outline);
            Assert.InRange(maxY - minY, 0.8, 0.91);   // as wide as the via where it starts
            Assert.InRange(maxX, 10.45 + 0.4, 10.45 + 0.46);   // half the via's size beyond its edge (KiCad's 50 %)
            Assert.NotEqual(0, Polygon.Winding(drop.Outline, 10.6, 15));   // copper just outside the via, on the track
            // the shape narrows to the track: at its tip only the track's width is left
            Assert.Equal(0, Polygon.Winding(drop.Outline, 10.8, 15.2));
        }

        [Fact]
        public void ATrackEnteringOffCentreKeepsItsTeardropOnItsOwnLine()
        {
            var board = Board();
            board.Tracks[0].Y1 = board.Tracks[0].Y2 = 15.2;   // 0.2 mm above the via's centre
            var drop = Assert.Single(Teardrops.Of(board));
            var (_, minY, maxX, maxY) = Polygon.Bounds(drop.Outline);
            Assert.InRange((minY + maxY) / 2, 15.19, 15.21);   // centred on the track, not on the via
            Assert.True(maxY <= 15.2 + 0.25 + 1e-9);   // and inside the via's sides where it starts
            Assert.NotEqual(0, Polygon.Winding(drop.Outline, maxX - 0.02, 15.2));   // its tip is on the track
        }

        [Fact]
        public void ATrackAsWideAsThePadHasNone()
        {
            var board = Board();
            board.Tracks[0].Width = 0.85;
            Assert.Empty(Teardrops.Of(board));
        }

        [Fact]
        public void AnotherNetBesideNarrowsItOrStopsIt()
        {
            var board = Board();
            // a track of another net passing 0.65 mm above the via's centre line
            board.Tracks.Add(new Track { Net = "GND", Layer = CopperLayer.Top, X1 = 8, Y1 = 15.65, X2 = 14, Y2 = 15.65, Width = 0.2 });
            foreach (var drop in Teardrops.Of(board))
            {
                double gap = Teardrops.SegmentToPolygon(8, 15.65, 14, 15.65, drop.Outline) - 0.1;
                Assert.True(gap >= board.ClearanceBetween("SIG", "GND") - 1e-9, "gap " + gap);
            }

            // on the other layer it is no obstacle
            board.Tracks[1].Layer = CopperLayer.Bottom;
            var free = Assert.Single(Teardrops.Of(board));
            Assert.InRange(Polygon.Bounds(free.Outline).MaxY - Polygon.Bounds(free.Outline).MinY, 0.8, 0.91);
        }

        [Fact]
        public void TheyGoToTheGerberAndOtherNetsPoursKeepClear()
        {
            var board = Board();
            int Regions(BoardDocument b) => GerberExporter.Generate(b).First(kv => kv.Key.Contains("F_Cu")).Value.Split("G36").Length - 1;
            int regionsOn = Regions(board);
            board.Teardrops.Enabled = false;
            Assert.Equal(Regions(board) + 1, regionsOn);   // one region more: the teardrop

            board.Teardrops.Enabled = true;
            var zone = new Zone { Net = "GND", Layer = CopperLayer.Top, Outline = { new PcbPoint(2, 2), new PcbPoint(38, 2), new PcbPoint(38, 28), new PcbPoint(2, 28) } };
            board.Zones.Add(zone);
            ZoneFiller.Fill(board, zone);
            var drop = Assert.Single(Teardrops.Of(board));
            double clearance = board.ClearanceBetween("SIG", "GND");
            foreach (var region in zone.FilledRegions)
            foreach (var contour in region.Contours())
                Assert.True(Teardrops.PolygonToPolygon(contour, drop.Outline) >= clearance - 0.02, "the pour comes too close");
        }
    }
}
