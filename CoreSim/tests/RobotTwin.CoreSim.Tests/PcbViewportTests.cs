using System.Collections.Generic;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[viewports] KiCad 8's saved views are kept with the board, and none are written when there are none.</summary>
    public class PcbViewportTests
    {
        [Fact]
        public void SavedViewsComeBackWithTheBoard()
        {
            var board = new BoardDocument { Viewports = new List<BoardViewport> { new BoardViewport { Name = "Güç", X = 8, Y = 12, Width = 22 } } };
            var back = board.Clone();
            Assert.Single(back.Viewports!);
            Assert.Equal("Güç", back.Viewports![0].Name);
            Assert.Equal(22, back.Viewports[0].Width);
            Assert.Null(new BoardDocument().Clone().Viewports);
        }
    }
}
