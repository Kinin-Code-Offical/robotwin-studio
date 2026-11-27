using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[problems] The DRC fix "Hattı yeniden çiz": one net routed again among the others, the rest left as it was.</summary>
    public class RerouteNetTests
    {
        private static BoardDocument Routed()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            AutoPlacer.Place(board);
            Assert.True(new AutoRouter(board).Route().Complete);
            return board;
        }

        [Fact]
        public void AMissingConnectionNamesItsNetAndRoutingThatNetAloneMendsIt()
        {
            var board = Routed();
            string net = board.Tracks.GroupBy(t => t.Net).OrderByDescending(g => g.Count()).First().Key;
            var others = board.Tracks.Where(t => t.Net != net).Select(t => (t.Net, t.X1, t.Y1, t.X2, t.Y2, t.Layer)).ToList();
            board.Tracks.RemoveAll(t => t.Net == net);
            board.Vias.RemoveAll(v => v.Net == net);

            var unrouted = DesignRuleChecker.Check(board).Where(v => v.Rule == "Bağlanmamış").ToList();
            Assert.NotEmpty(unrouted);
            Assert.All(unrouted, v => Assert.Equal(net, v.Net));

            var r = new AutoRouter(board).RouteNets(new[] { net });
            Assert.True(r.Complete, string.Join("; ", r.Failed));
            Assert.Contains(board.Tracks, t => t.Net == net);
            // every other net's copper is where it was
            Assert.Equal(others, board.Tracks.Where(t => t.Net != net).Select(t => (t.Net, t.X1, t.Y1, t.X2, t.Y2, t.Layer)).ToList());
            Assert.DoesNotContain(DesignRuleChecker.Check(board), v => v.Rule == "Bağlanmamış");
        }

        [Fact]
        public void AClearanceMarkerNamesTheTracksNetButAPadPairNone()
        {
            var board = Routed();
            var a = board.Tracks.First();
            var b = board.Tracks.First(t => t.Net != a.Net && t.Layer == a.Layer);
            // b's track laid over a's: too near (a short)
            board.Tracks.Add(new Track { Net = b.Net, Layer = a.Layer, Width = a.Width, X1 = a.X1, Y1 = a.Y1, X2 = a.X2, Y2 = a.Y2 });
            var near = DesignRuleChecker.Check(board).Where(v => v.Message.Contains("↔") && v.Rule != "Ped-ped aralığı").ToList();
            Assert.NotEmpty(near);
            Assert.All(near, v => Assert.False(string.IsNullOrEmpty(v.Net)));
        }
    }
}
