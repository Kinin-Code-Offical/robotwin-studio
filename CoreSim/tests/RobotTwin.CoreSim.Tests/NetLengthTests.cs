using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[pairs] Routed lengths, differential pairs and length-matched groups.</summary>
    public class NetLengthTests
    {
        private readonly ITestOutputHelper _out;

        public NetLengthTests(ITestOutputHelper output) => _out = output;

        private static BoardComponent Part(string refDes, double x, double y, string net1, string net2)
        {
            var c = new BoardComponent { RefDes = refDes, FootprintId = "R0805", X = x, Y = y };
            c.PadNets["1"] = net1;
            c.PadNets["2"] = net2;
            return c;
        }

        [Fact]
        public void PairsAreFoundFromTheirNames()
        {
            var b = new BoardDocument();
            b.Components.Add(Part("R1", 10, 10, "USB_D+", "USB_D-"));
            b.Components.Add(Part("R2", 20, 10, "CANH", "CANL"));
            b.Components.Add(Part("R3", 30, 10, "TX_P", "TX_N"));
            b.Components.Add(Part("R4", 40, 10, "VCC", "GND"));
            var pairs = NetLengths.Detect(b);
            foreach (var p in pairs) _out.WriteLine(p.ToString());
            Assert.Equal(3, pairs.Count);
            Assert.Contains(pairs, p => p.Positive == "USB_D+" && p.Negative == "USB_D-");
            Assert.Contains(pairs, p => p.Positive == "CANH" && p.Negative == "CANL");
            Assert.Contains(pairs, p => p.Positive == "TX_P" && p.Negative == "TX_N");
        }

        [Fact]
        public void ALengthIsItsTracksAndTheViaBarrelsItCrosses()
        {
            var b = new BoardDocument();
            b.Tracks.Add(new Track { Net = "A", Layer = CopperLayer.Top, X1 = 0, Y1 = 0, X2 = 10, Y2 = 0 });
            b.Tracks.Add(new Track { Net = "A", Layer = CopperLayer.Bottom, X1 = 10, Y1 = 0, X2 = 10, Y2 = 5 });
            b.Vias.Add(new Via { Net = "A", X = 10, Y = 0 });
            var lengths = NetLengths.Measure(b);
            Assert.Equal(10 + 5 + 1.6, lengths["A"], 6);
        }

        [Fact]
        public void APairLongerOnOneSideAndAGroupSpreadAreFlagged()
        {
            var b = new BoardDocument { Width = 60, Height = 30 };
            // Two 30 mm tracks side by side, 0.2 mm wide, 0.4 mm apart centre to centre: a 0.2 mm gap all along.
            b.Tracks.Add(new Track { Net = "D+", Layer = CopperLayer.Top, X1 = 10, Y1 = 10, X2 = 40, Y2 = 10, Width = 0.2 });
            b.Tracks.Add(new Track { Net = "D-", Layer = CopperLayer.Top, X1 = 10, Y1 = 10.4, X2 = 40, Y2 = 10.4, Width = 0.2 });
            var pair = new DiffPair { Positive = "D+", Negative = "D-", Gap = 0.2, MaxSkew = 1.25 };
            var r = NetLengths.Analyze(b, pair);
            _out.WriteLine($"{r.PositiveLength} / {r.NegativeLength}, coupled {r.Coupled:P0}, gap {r.MeanGap:0.###}");
            Assert.Equal(0, r.Skew, 6);
            Assert.InRange(r.Coupled, 0.95, 1.0);
            Assert.Equal(0.2, r.MeanGap, 3);

            // A 3 mm detour on D+ alone: 3 mm of skew, over the 1.25 mm the pair allows.
            b.Tracks.Add(new Track { Net = "D+", Layer = CopperLayer.Top, X1 = 40, Y1 = 10, X2 = 40, Y2 = 7, Width = 0.2 });
            b.DiffPairs.Add(pair);
            b.LengthGroups.Add(new LengthGroup { Name = "Veri", Nets = { "D+", "D-" }, Tolerance = 1.0 });
            var issues = NetLengths.Check(b);
            foreach (var v in issues) _out.WriteLine(v.ToString());
            Assert.Contains(issues, v => v.Rule == "Fark çifti uzunluğu" && v.Message.Contains("fark 3 mm"));
            Assert.Contains(issues, v => v.Rule == "Uzunluk eşleme" && v.Message.StartsWith("Veri"));
            Assert.DoesNotContain(issues, v => v.Rule == "Fark çifti eşleşmesi");
            Assert.Contains(DesignRuleChecker.Check(b), v => v.Rule == "Fark çifti uzunluğu");
        }
    }
}
