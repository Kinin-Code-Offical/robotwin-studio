using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[fab-ready] The readiness screen: a routed board is ready; an unrouted one is not, and says which nets.</summary>
    public class FabReadinessTests
    {
        [Fact]
        public void ARoutedBoardIsReady()
        {
            var board = GerberCheckTests.RoutedBoard();
            var items = FabReadiness.Check(board, "motor");
            Assert.True(FabReadiness.Ready(items), string.Join("\n", items.Where(i => i.State == ReadinessState.Fail).Select(i => i.Name + ": " + i.Detail)));
            Assert.Equal(ReadinessState.Pass, items.Single(i => i.Name == "Bağlantı bütünlüğü").State);
            Assert.Equal(ReadinessState.Pass, items.Single(i => i.Name == "Gerber ve delik dosyaları").State);
            // what is not checked is listed as such
            Assert.Contains(items, i => i.State == ReadinessState.NotChecked && i.Name == "Üreticinin kural seti");   // no fab chosen
            Assert.Contains(items, i => i.State == ReadinessState.Pass && i.Name == "Empedans kontrolü" && i.Detail.Contains("istenmedi"));   // computed, no target
            Assert.Contains("denetlenmedi", FabReadiness.Summary(items));
        }

        [Fact]
        public void AnUnroutedBoardIsNotAndSaysWhichNets()
        {
            var board = GerberCheckTests.RoutedBoard();
            string net = board.Tracks[0].Net;
            board.Tracks.RemoveAll(t => t.Net == net);
            board.Vias.RemoveAll(v => v.Net == net);
            board.Zones.RemoveAll(z => z.Net == net);
            var items = FabReadiness.Check(board, "motor");
            var connection = items.Single(i => i.Name == "Bağlantı bütünlüğü");
            Assert.Equal(ReadinessState.Fail, connection.State);
            Assert.Contains(net, connection.Detail);
            Assert.False(FabReadiness.Ready(items));
        }

        [Fact]
        public void AStalePourIsAWarning()
        {
            var board = GerberCheckTests.RoutedBoard();
            board.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.Top });   // never filled
            var pours = FabReadiness.Check(board, "motor").Single(i => i.Name == "Bakır dolgular");
            Assert.Equal(ReadinessState.Warning, pours.State);
        }
    }
}
