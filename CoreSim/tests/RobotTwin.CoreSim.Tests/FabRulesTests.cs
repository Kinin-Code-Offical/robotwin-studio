using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[fab-rules] Boards against fabs' published limits, through the board's own DRC and DFM checkers.</summary>
    public class FabRulesTests
    {
        private readonly ITestOutputHelper _out;

        public FabRulesTests(ITestOutputHelper output) => _out = output;

        private static BoardDocument Board(int layers = 2)
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.Stackup.CopperLayers = layers;
            return b;
        }

        [Fact]
        public void LimitsFollowLayerCountAndCopperWeight()
        {
            var two = FabRules.For("jlcpcb", 2, 35)!;
            var four = FabRules.For("jlcpcb", 4, 35)!;
            var heavy = FabRules.For("jlcpcb", 2, 70)!;
            _out.WriteLine(two.Name + ": " + two.Limits);
            Assert.Equal(0.10, two.MinTrackWidth, 6);
            Assert.Equal(0.09, four.MinTrackWidth, 6);
            Assert.Equal(0.16, heavy.MinTrackWidth, 6);
            Assert.Equal(0.18, two.MinPadRing, 6);
            Assert.Equal(0.05, two.MinViaRing, 6);
            Assert.Equal(0.1524, FabRules.For("oshpark", 2, 35)!.MinTrackWidth, 6);
            Assert.Equal(0.127, FabRules.For("oshpark", 4, 35)!.MinTrackWidth, 6);
            Assert.Null(FabRules.For("nowhere", 2, 35));
            Assert.All(FabRules.Fabs, f => Assert.NotNull(FabRules.For(f.Id, 2, 35)));
        }

        [Fact]
        public void ABoardIsCheckedAgainstTheFabsLimitsNotItsOwn()
        {
            var b = Board();
            b.Rules.MinTrackWidth = 0.05;   // the board's own rule lets anything through
            b.Tracks.Add(new Track { Net = "A", Layer = CopperLayer.Top, X1 = 5, Y1 = 5, X2 = 20, Y2 = 5, Width = 0.12 });
            b.Vias.Add(new Via { Net = "A", X = 20, Y = 5, Diameter = 0.25, Drill = 0.15 });
            b.Tracks.Add(new Track { Net = "B", Layer = CopperLayer.Top, X1 = 5, Y1 = 10, X2 = 20, Y2 = 10, Width = 0.3 });

            Assert.Equal(ReadinessState.NotChecked, FabRules.Readiness(b).State);

            var jlc = FabRules.Check(b, FabRules.For("jlcpcb", b)!);
            foreach (var i in jlc) _out.WriteLine("JLCPCB  " + i.Rule + ": " + i.Message);
            Assert.Empty(jlc);   // 0.12 mm and a 0.15/0.25 via are inside its limits
            b.Fab = "jlcpcb";
            Assert.Equal(ReadinessState.Pass, FabRules.Readiness(b).State);

            var osh = FabRules.Check(b, FabRules.For("oshpark", b)!);
            foreach (var i in osh) _out.WriteLine("OSH Park  " + i.Rule + ": " + i.Message);
            Assert.Contains(osh, i => i.Rule == "Minimum yol genişliği");   // 6 mil
            Assert.Contains(osh, i => i.Rule == "Minimum delik");           // 10 mil
            Assert.Contains(osh, i => i.Rule == "Halka genişliği");         // 5 mil ring
            b.Fab = "oshpark";
            Assert.Equal(ReadinessState.Warning, FabRules.Readiness(b).State);

            // spacing too: 0.12 mm apart is fine at JLCPCB (0.10), not at OSH Park (0.1524)
            b.Tracks.Add(new Track { Net = "C", Layer = CopperLayer.Top, X1 = 5, Y1 = 10.42, X2 = 20, Y2 = 10.42, Width = 0.3 });
            Assert.DoesNotContain(FabRules.Check(b, FabRules.For("jlcpcb", b)!), i => i.Rule == "Yol-yol aralığı");
            Assert.Contains(FabRules.Check(b, FabRules.For("oshpark", b)!), i => i.Rule == "Yol-yol aralığı");
        }

        [Fact]
        public void LayerCountAndCopperPastTheFabFail()
        {
            var b = Board(6);
            b.Fab = "oshpark";
            var item = FabRules.Readiness(b);
            _out.WriteLine(item.Detail);
            Assert.Equal(ReadinessState.Fail, item.State);
            Assert.Contains("katman", item.Detail);

            var heavy = Board(4);
            heavy.Rules.CopperThicknessUm = 70;
            Assert.Contains(FabRules.Check(heavy, FabRules.For("oshpark", heavy)!), i => i.Rule == "Bakır ağırlığı");
            Assert.DoesNotContain(FabRules.Check(heavy, FabRules.For("jlcpcb", heavy)!), i => i.Rule == "Bakır ağırlığı");
        }

        [Fact]
        public void ViasHaveTheirOwnRingAndHoleSpacing()
        {
            var b = Board();
            b.Rules.MinAnnularRing = 0.15;
            b.Rules.MinHoleToHole = 0.45;
            b.Vias.Add(new Via { Net = "A", X = 10, Y = 10, Diameter = 0.3, Drill = 0.2 });   // 0.05 ring
            b.Vias.Add(new Via { Net = "B", X = 10.5, Y = 10, Diameter = 0.3, Drill = 0.2 }); // holes 0.3 apart
            var before = DesignRuleChecker.Check(b);
            Assert.Contains(before, v => v.Rule == "Halka genişliği");
            Assert.Contains(before, v => v.Rule == "Delik-delik aralığı");

            b.Rules.MinViaAnnularRing = 0.05;
            b.Rules.MinViaHoleToHole = 0.2;
            var after = DesignRuleChecker.Check(b);
            Assert.DoesNotContain(after, v => v.Rule == "Halka genişliği");
            Assert.DoesNotContain(after, v => v.Rule == "Delik-delik aralığı");

            // the fields survive a save
            var back = b.Clone();
            Assert.Equal(0.05, back.Rules.MinViaAnnularRing, 9);
            Assert.Equal(0.2, back.Rules.MinViaHoleToHole, 9);
        }
    }
}
