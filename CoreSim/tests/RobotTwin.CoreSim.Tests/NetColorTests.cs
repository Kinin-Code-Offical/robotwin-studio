using System.Text.Json;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[net-colors] KiCad's net colours: kept with the board, a view-only setting.</summary>
    public class NetColorTests
    {
        [Fact]
        public void ANetsColourIsKeptWithTheBoard()
        {
            var board = new BoardDocument();
            board.NetColors["VBAT"] = "#E5484D";
            Assert.Equal("#E5484D", board.NetColorOf("VBAT"));
            Assert.Null(board.NetColorOf("GND"));
            Assert.Null(board.NetColorOf(null));

            var again = JsonSerializer.Deserialize<BoardDocument>(JsonSerializer.Serialize(board, RobotWinFormats.Json), RobotWinFormats.Json)!;
            Assert.Equal("#E5484D", again.NetColorOf("VBAT"));
        }

        [Fact]
        public void ColoursDoNotChangeTheManufacturingFiles()
        {
            var board = new BoardDocument { Width = 30, Height = 20 };
            board.Tracks.Add(new Track { Net = "VBAT", Layer = CopperLayer.Top, X1 = 2, Y1 = 2, X2 = 20, Y2 = 2, Width = 0.5 });
            string before = string.Join("|", GerberExporter.Generate(board).Values);
            board.NetColors["VBAT"] = "#E5484D";
            Assert.Equal(before, string.Join("|", GerberExporter.Generate(board).Values));
        }
    }
}
