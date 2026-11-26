using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[keepout] A rule area: the pour leaves it out, the DRC reports a track and a via inside, KiCad gets a keepout zone.</summary>
    public class RuleAreaTests
    {
        private static BoardDocument Board()
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.RuleAreas.Add(new RuleArea
            {
                Name = "Anten", Layers = { CopperLayer.Top },
                Outline = { new PcbPoint(25, 5), new PcbPoint(35, 5), new PcbPoint(35, 15), new PcbPoint(25, 15) }
            });
            return b;
        }

        [Fact]
        public void TheDrcReportsWhatIsInsideOnItsLayersOnly()
        {
            var b = Board();
            b.Tracks.Add(new Track { Net = "A", Layer = CopperLayer.Top, X1 = 20, Y1 = 10, X2 = 30, Y2 = 10, Width = 0.3 });   // crosses in
            b.Tracks.Add(new Track { Net = "B", Layer = CopperLayer.Bottom, X1 = 20, Y1 = 12, X2 = 30, Y2 = 12, Width = 0.3 });   // other layer: allowed
            b.Tracks.Add(new Track { Net = "C", Layer = CopperLayer.Top, X1 = 5, Y1 = 20, X2 = 20, Y2 = 20, Width = 0.3 });     // outside
            b.Vias.Add(new Via { Net = "D", X = 30, Y = 8 });                                                                   // through: on top too
            var found = RuleAreaCheck.Check(b);
            Assert.Contains(found, v => v.Message.StartsWith("A yolu 'Anten' içinde"));
            Assert.Contains(found, v => v.Message.StartsWith("D viası 'Anten' içinde"));
            Assert.DoesNotContain(found, v => v.Message.StartsWith("B ") || v.Message.StartsWith("C "));
            Assert.Contains(DesignRuleChecker.Check(b), v => v.Rule == RuleAreaCheck.Rule);
            Assert.NotNull(DrcRuleCatalog.Find(RuleAreaCheck.Rule));
        }

        [Fact]
        public void ThePourLeavesItOutAndKiCadGetsAKeepout()
        {
            var b = Board();
            var zone = new Zone { Net = "GND", Layer = CopperLayer.Top, IslandRemoval = IslandRemoval.Never };   // no pads on this board: keep the islands
            b.Zones.Add(zone);
            ZoneFiller.FillAll(b);
            Assert.True(zone.IsFilled);
            Assert.DoesNotContain(zone.FilledRegions, r => r.Contains(30, 10));   // the area's middle stays bare
            Assert.Contains(zone.FilledRegions, r => r.Contains(10, 10));    // elsewhere copper
            Assert.DoesNotContain(RuleAreaCheck.Check(b), v => v.Message.Contains("dolgusu"));

            // moving the area makes the fill out of date
            b.RuleAreas[0].Outline = b.RuleAreas[0].Outline.Select(p => new PcbPoint(p.X - 15, p.Y)).ToList();
            Assert.True(ZoneFiller.IsOutdated(b, zone));

            string pcb = KiCadExporter.ExportPcb(b, "deneme");
            Assert.Matches(@"\(keepout\s*\(tracks not_allowed\)\s*\(vias not_allowed\)\s*\(pads allowed\)\s*\(copperpour not_allowed\)\s*\(footprints allowed\)", pcb);
            Assert.Matches(@"\(name\s+""Anten""\)", pcb);
            Assert.Single(b.Clone().RuleAreas);
        }
    }
}
