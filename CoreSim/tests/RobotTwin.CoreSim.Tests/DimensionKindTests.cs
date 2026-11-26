using System;
using System.Linq;
using System.Text.Json;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[dims] KiCad's dimension kinds: aligned, orthogonal, radial, leader, centre mark.</summary>
    public class DimensionKindTests
    {
        [Fact]
        public void AnOrthogonalDimensionMeasuresOnlyAcrossOrOnlyUp()
        {
            var d = new BoardDimension { Kind = DimensionKind.Orthogonal, X1 = 0, Y1 = 0, X2 = 10, Y2 = 5, Offset = -3 };
            Assert.Equal(10, d.Length, 9);
            Assert.Equal("10.00 mm", d.Label());
            var geo = d.Geometry();
            Assert.Contains(geo.Lines, l => l.Y1 == -3 && l.Y2 == -3 && l.X1 == 0 && l.X2 == 10);   // the dimension line, 3 mm above the first point
            Assert.True(geo.TextY < -3);   // the value outside it
            Assert.Equal(0, geo.Angle);

            d.Vertical = true;
            Assert.Equal(5, d.Length, 9);
            Assert.Equal((1.0, 0.0), d.OffsetDirection());
            Assert.Contains(d.Geometry().Lines, l => l.X1 == -3 && l.X2 == -3 && l.Y1 == 0 && l.Y2 == 5);
        }

        [Fact]
        public void ARadiusIsWrittenWithRAndLeadsOutAlongTheRadius()
        {
            var d = new BoardDimension { Kind = DimensionKind.Radial, X1 = 5, Y1 = 5, X2 = 8, Y2 = 5, Offset = 4 };
            Assert.Equal(3, d.Length, 9);
            Assert.Equal("R 3.00 mm", d.Label());
            Assert.Equal("R 118 mil", d.Label(mil: true));
            var first = d.Geometry().Lines[0];
            Assert.Equal((8.0, 5.0, 12.0, 5.0), (first.X1, first.Y1, first.X2, first.Y2));   // from the circle 4 mm outwards
        }

        [Fact]
        public void ALeaderCarriesItsNoteAndACentreMarkNoText()
        {
            var leader = new BoardDimension { Kind = DimensionKind.Leader, X1 = 1, Y1 = 1, X2 = 6, Y2 = 4, Text = "M3 vida" };
            Assert.Equal(0, leader.Length);
            Assert.Equal("M3 vida", leader.Label());
            var lines = leader.Geometry().Lines;
            Assert.Equal((1.0, 1.0), (lines[0].X1, lines[0].Y1));   // from the arrow's tip
            Assert.True(lines.Count(l => l.X1 == 1 && l.Y1 == 1) >= 3);   // the line and its two barbs

            var centre = new BoardDimension { Kind = DimensionKind.Center, X1 = 5, Y1 = 5, X2 = 7, Y2 = 5 };
            Assert.Equal(string.Empty, centre.Label());
            Assert.Equal(2, centre.Geometry().Lines.Count);
            Assert.Equal(2, Silk.DimensionStrokes(centre).Count);   // a cross, no letters
        }

        [Fact]
        public void AnAlignedOneStaysAsItWasInFiles()
        {
            var aligned = new BoardDimension { X1 = 0, Y1 = 0, X2 = 3, Y2 = 4, Offset = 2 };
            Assert.Equal(5, aligned.Length, 9);
            Assert.Equal("5.00 mm", aligned.Label());
            string json = JsonSerializer.Serialize(aligned);
            Assert.DoesNotContain("\"Kind\"", json);
            Assert.DoesNotContain("\"Vertical\"", json);
            Assert.DoesNotContain("\"Text\"", json);

            var orth = JsonSerializer.Deserialize<BoardDimension>(JsonSerializer.Serialize(new BoardDimension { Kind = DimensionKind.Orthogonal, Vertical = true, Text = "H" }))!;
            Assert.Equal(DimensionKind.Orthogonal, orth.Kind);
            Assert.True(orth.Vertical);
            Assert.Equal("H", orth.Label());   // the override
        }
    }
}
