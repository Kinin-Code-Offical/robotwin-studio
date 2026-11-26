using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[clearance-res] KiCad's Clearance Resolution: which rule holds two items apart, and how far apart they are.</summary>
    public class ClearanceResolutionTests
    {
        private static BoardDocument Board()
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 5, Y1 = 10, X2 = 30, Y2 = 10, Width = 0.25 });
            b.Tracks.Add(new Track { Net = "VBAT", Layer = CopperLayer.Top, X1 = 5, Y1 = 11, X2 = 30, Y2 = 11, Width = 0.75 });
            return b;
        }

        [Fact]
        public void TheWiderClassSetsTheClearanceAndTheGapIsMeasured()
        {
            var board = Board();
            board.AssignClass("VBAT", NetClass.PowerName);
            var power = board.ClassFor("VBAT");
            var r = ClearanceResolution.Between(board, board.Tracks[0], board.Tracks[1])!;
            Assert.Equal("SIG", r.NetA);
            Assert.Equal("VBAT", r.NetB);
            Assert.Equal(System.Math.Max(power.Clearance, board.ClassFor("SIG").Clearance), r.Required, 9);
            var (layer, actual) = Assert.Single(r.Layers);
            Assert.Equal(CopperLayer.Top, layer);
            Assert.Equal(1 - 0.125 - 0.375, actual, 6);   // centre lines 1 mm apart, less both half-widths
            Assert.Contains("sınıfı", r.Source);
            Assert.Equal(actual >= r.Required, r.Ok);
        }

        [Fact]
        public void OnDifferentLayersThereIsNothingToMeasure()
        {
            var board = Board();
            board.Tracks[1].Layer = CopperLayer.Bottom;
            var r = ClearanceResolution.Between(board, board.Tracks[0], board.Tracks[1])!;
            Assert.Empty(r.Layers);
            Assert.True(r.Ok);

            // a through via shares both
            board.Vias.Add(new Via { Net = "GND", X = 20, Y = 12, Diameter = 0.9 });
            var v = ClearanceResolution.Between(board, board.Tracks[1], board.Vias[0])!;
            Assert.Single(v.Layers);
            Assert.Equal(CopperLayer.Bottom, v.Layers[0].Layer);
            Assert.Equal(1 - 0.375 - 0.45, v.Layers[0].Actual, 6);
        }

        [Fact]
        public void TheSameNetHasNoRuleAndTouchingCopperIsZero()
        {
            var board = Board();
            board.Tracks[1].Net = "SIG";
            board.Tracks[1].Y1 = board.Tracks[1].Y2 = 10.2;
            var r = ClearanceResolution.Between(board, board.Tracks[0], board.Tracks[1])!;
            Assert.True(r.SameNet);
            Assert.True(r.Ok);
            Assert.Equal(0, r.Layers.Single().Actual, 9);
        }
    }
}
