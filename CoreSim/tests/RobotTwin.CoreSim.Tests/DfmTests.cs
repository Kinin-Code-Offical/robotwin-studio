using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[dfm] Manufacturability and testability: what a fab and an assembly line would flag.</summary>
    public class DfmTests
    {
        private readonly ITestOutputHelper _out;

        public DfmTests(ITestOutputHelper output) => _out = output;

        private static BoardComponent Part(string refDes, string footprint, double x, double y, params (string Pad, string Net)[] nets)
        {
            var c = new BoardComponent { RefDes = refDes, FootprintId = footprint, X = x, Y = y };
            foreach (var (pad, net) in nets) c.PadNets[pad] = net;
            return c;
        }

        private DfmReport Check(BoardDocument b)
        {
            var r = DfmChecker.Check(b);
            foreach (var v in r.Issues) _out.WriteLine(v.ToString());
            _out.WriteLine($"test coverage {r.TestableNets}/{r.Nets}");
            return r;
        }

        [Fact]
        public void AcidTrapsAreSharpJoinsOnly()
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.Tracks.Add(new Track { Net = "N1", Layer = CopperLayer.Top, X1 = 10, Y1 = 10, X2 = 20, Y2 = 10 });
            b.Tracks.Add(new Track { Net = "N1", Layer = CopperLayer.Top, X1 = 10, Y1 = 10, X2 = 17, Y2 = 17 });   // 45°
            b.Tracks.Add(new Track { Net = "N2", Layer = CopperLayer.Top, X1 = 10, Y1 = 20, X2 = 20, Y2 = 20 });
            b.Tracks.Add(new Track { Net = "N2", Layer = CopperLayer.Top, X1 = 10, Y1 = 20, X2 = 10, Y2 = 28 });   // 90°
            var r = Check(b);
            var traps = r.Issues.Where(v => v.Rule == "DFM · Asit tuzağı").ToList();
            Assert.Single(traps);
            Assert.Contains("N1", traps[0].Message);
            Assert.Contains("45°", traps[0].Message);
        }

        [Fact]
        public void ViasInPadsPartsAtTheEdgeAndLopsidedCopperAreFlagged()
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            var edge = Part("R1", "R0805", 1.6, 15, ("1", "A"), ("2", "B"));
            var mid = Part("R2", "R0805", 20, 15, ("1", "C"), ("2", "D"));
            b.Components.Add(edge);
            b.Components.Add(mid);
            var pads = b.Pads().Where(p => p.Component == mid).ToList();
            var p1 = pads.Single(p => p.Pad.Name == "1");
            var p2 = pads.Single(p => p.Pad.Name == "2");
            b.Vias.Add(new Via { Net = "C", X = p1.X, Y = p1.Y });
            b.Tracks.Add(new Track { Net = "C", Layer = CopperLayer.Top, X1 = p1.X, Y1 = p1.Y, X2 = p1.X - 8, Y2 = p1.Y, Width = 1.6 });
            b.Tracks.Add(new Track { Net = "D", Layer = CopperLayer.Top, X1 = p2.X, Y1 = p2.Y, X2 = p2.X + 8, Y2 = p2.Y, Width = 0.25 });
            b.Texts.Add(new BoardText { Text = "v1.0", X = 30, Y = 5, Size = 0.5, Thickness = 0.1 });
            var r = Check(b);
            Assert.Contains(r.Issues, v => v.Rule == "DFM · Pedde via" && v.Message.Contains("R2.1"));
            Assert.Contains(r.Issues, v => v.Rule == "DFM · Kenara yakın SMD" && v.Message.StartsWith("R1"));
            Assert.DoesNotContain(r.Issues, v => v.Rule == "DFM · Kenara yakın SMD" && v.Message.StartsWith("R2"));
            Assert.Contains(r.Issues, v => v.Rule == "DFM · Mezar taşı riski" && v.Message.StartsWith("R2: 1"));
            Assert.Contains(r.Issues, v => v.Rule == "DFM · Okunmaz serigrafi");
        }

        [Fact]
        public void TestAccessCoversThroughHolePadsButNotTopSmdOnlyNets()
        {
            var b = new BoardDocument { Width = 60, Height = 40 };
            b.Components.Add(Part("U1", "DIP-8", 20, 10, ("1", "VCC"), ("2", "SIG"), ("4", "GND"), ("8", "VCC")));
            b.Components.Add(Part("R1", "R0805", 40, 20, ("1", "SIG"), ("2", "MID")));
            b.Components.Add(Part("R2", "R0805", 40, 30, ("1", "MID"), ("2", "GND")));
            var r = Check(b);
            // VCC (two DIP pins), SIG (DIP pin), GND (DIP pin) are reachable; MID touches two top SMD pads only.
            Assert.Equal(4, r.Nets);   // VCC, SIG, GND, MID have two or more pads
            Assert.Equal(new[] { "MID" }, r.Untestable);
            Assert.Equal(3, r.TestableNets);
            Assert.Equal(0.75, r.Coverage, 3);
            Assert.Contains(r.Issues, v => v.Rule == "DFT · Test erişimi yok" && v.Message.StartsWith("MID"));
        }
    }
}
