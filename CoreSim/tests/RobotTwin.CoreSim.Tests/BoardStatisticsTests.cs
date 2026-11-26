using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[board-stats] The routed motor driver counted as KiCad's Board Statistics counts it.</summary>
    public class BoardStatisticsTests
    {
        [Fact]
        public void TheStatisticsAddUpToTheBoard()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            AutoPlacer.Place(board);
            new AutoRouter(board).Route();
            board.MountingHoles.Add(new MountingHole { X = 4, Y = 4, Drill = 3.2 });
            board.MountingHoles.Add(new MountingHole { X = 66, Y = 4, Drill = 3.2, PadDiameter = 6 });
            board.Components[0].Side = CopperLayer.Bottom;

            var st = BoardStatistics.Of(board);
            var pads = board.Pads().ToList();

            Assert.Equal(board.Components.Count, st.PartsTop + st.PartsBottom);
            Assert.Equal(1, st.PartsBottom);
            Assert.Equal(board.Components.Count, st.PartsThroughHole + st.PartsSmd);
            Assert.Equal(pads.Count, st.PadsThroughHole + st.PadsSmd + st.PadsNonPlated);
            Assert.Equal(board.Vias.Count, st.ViasThrough + st.ViasBlindBuried);
            Assert.Equal(board.Tracks.Count, st.Tracks);
            Assert.Equal(board.Tracks.Sum(t => t.Length), st.TrackLength, 6);
            Assert.Equal(st.TrackLength, st.LengthByLayer.Values.Sum(), 6);
            Assert.InRange(st.Area, 70 * 50 - 2, 70 * 50);   // the default outline has rounded corners

            // every drilled thing is in the drill table once: through-hole pads, vias, mounting holes
            Assert.Equal(pads.Count(p => p.Pad.IsThroughHole) + board.Vias.Count + 2, st.Holes);
            var mounting = st.Drills.Where(d => d.Kind == "montaj deliği").ToList();
            Assert.Equal(2, mounting.Count);   // one plated, one not: two lines of the same size
            Assert.Contains(mounting, d => d.Plated && d.Diameter == 3.2 && d.Count == 1);
            Assert.Contains(mounting, d => !d.Plated && d.Diameter == 3.2 && d.Count == 1);
            Assert.Equal(st.Drills.OrderBy(d => d.Diameter).Select(d => d.Diameter), st.Drills.Select(d => d.Diameter));

            string text = st.ToText();
            Assert.Contains("Kart: 70 × 50 mm, alan 34.99", text);
            Assert.Contains($"Delikler: {st.Holes}", text);
            Assert.Contains("Ø3.2 mm · kaplamasız · montaj deliği × 1", text);
        }
    }
}
