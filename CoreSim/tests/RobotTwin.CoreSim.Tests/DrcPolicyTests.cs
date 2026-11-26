using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[drc-policy] The user's severities per rule and accepted markers (KiCad's Violation Severity and exclusions), saved with the board.</summary>
    public class DrcPolicyTests
    {
        /// <summary>Two parallel tracks of different nets 0.1 mm apart (the rule wants 0.2) and one thinner than the minimum.</summary>
        private static BoardDocument Board()
        {
            var b = new BoardDocument { Width = 40, Height = 30 };
            b.Tracks.Add(new Track { Net = "A", Layer = CopperLayer.Top, X1 = 5, Y1 = 10, X2 = 30, Y2 = 10, Width = 0.3 });
            b.Tracks.Add(new Track { Net = "B", Layer = CopperLayer.Top, X1 = 5, Y1 = 10.4, X2 = 30, Y2 = 10.4, Width = 0.3 });
            b.Tracks.Add(new Track { Net = "C", Layer = CopperLayer.Top, X1 = 5, Y1 = 20, X2 = 30, Y2 = 20, Width = 0.1 });
            return b;
        }

        [Fact]
        public void ARulesSeverityChangesHowItsMarkersCount()
        {
            var board = Board();
            var raw = DesignRuleChecker.Check(board);
            Assert.Contains(raw, v => v.Rule == "Minimum yol genişliği" && v.Severity == DrcSeverity.Error);

            DrcPolicy.SetSeverity(board, "Minimum yol genişliği", DrcRuleSeverity.Warning);
            var r = DrcPolicy.Apply(board, raw);
            Assert.All(r.Shown.Where(v => v.Rule == "Minimum yol genişliği"), v => Assert.Equal(DrcSeverity.Warning, v.Severity));

            DrcPolicy.SetSeverity(board, "Boşta yol ucu", DrcRuleSeverity.Error);   // a warning made an error
            r = DrcPolicy.Apply(board, raw);
            Assert.Contains(r.Shown, v => v.Rule == "Boşta yol ucu" && v.Severity == DrcSeverity.Error);

            DrcPolicy.SetSeverity(board, "Minimum yol genişliği", DrcRuleSeverity.Ignore);
            r = DrcPolicy.Apply(board, raw);
            Assert.DoesNotContain(r.Shown, v => v.Rule == "Minimum yol genişliği");
            Assert.Equal(raw.Count(v => v.Rule == "Minimum yol genişliği"), r.Ignored["Minimum yol genişliği"]);

            DrcPolicy.SetSeverity(board, "Minimum yol genişliği", DrcRuleSeverity.Default);
            Assert.False(board.Rules.Severities.ContainsKey("Minimum yol genişliği"));
            r = DrcPolicy.Apply(board, raw);
            Assert.Contains(r.Shown, v => v.Rule == "Minimum yol genişliği" && v.Severity == DrcSeverity.Error);
        }

        [Fact]
        public void AnExcludedMarkerStaysExcludedUntilItMovesOrChanges()
        {
            var board = Board();
            var clearance = DesignRuleChecker.Check(board).Single(v => v.Rule == "Yol-yol aralığı");
            DrcPolicy.Exclude(board, clearance, "tasarım gereği");

            var r = DrcPolicy.Apply(board, DesignRuleChecker.Check(board));
            Assert.DoesNotContain(r.Shown, v => v.Rule == "Yol-yol aralığı");
            var (excluded, why) = Assert.Single(r.Excluded);
            Assert.Equal("tasarım gereği", why.Comment);
            Assert.Equal(clearance.Message, excluded.Message);

            // a little closer: the numbers in the message change, the marker is the same one
            board.Tracks[1].Y1 = board.Tracks[1].Y2 = 10.38;
            r = DrcPolicy.Apply(board, DesignRuleChecker.Check(board));
            Assert.DoesNotContain(r.Shown, v => v.Rule == "Yol-yol aralığı");
            Assert.Single(r.Excluded);

            // the same pair somewhere else on the board is a new marker
            foreach (var t in board.Tracks.Take(2))
            {
                t.Y1 += 8;
                t.Y2 += 8;
            }

            r = DrcPolicy.Apply(board, DesignRuleChecker.Check(board));
            Assert.Contains(r.Shown, v => v.Rule == "Yol-yol aralığı");
            Assert.Empty(r.Excluded);

            // taken back
            board.Tracks[0].Y1 = board.Tracks[0].Y2 = 10;
            board.Tracks[1].Y1 = board.Tracks[1].Y2 = 10.4;
            var again = DesignRuleChecker.Check(board).Single(v => v.Rule == "Yol-yol aralığı");
            Assert.True(DrcPolicy.Include(board, again));
            Assert.Empty(board.DrcExclusions);
            Assert.Contains(DrcPolicy.Apply(board, DesignRuleChecker.Check(board)).Shown, v => v.Rule == "Yol-yol aralığı");
        }

        [Fact]
        public void SeveritiesAndExclusionsAreSavedWithTheBoard()
        {
            var board = Board();
            DrcPolicy.SetSeverity(board, "Serigrafi pedde", DrcRuleSeverity.Ignore);
            DrcPolicy.Exclude(board, DesignRuleChecker.Check(board).First(v => v.Rule == "Yol-yol aralığı"), "kabul");

            var copy = board.Clone();
            Assert.Equal(DrcRuleSeverity.Ignore, DrcPolicy.SeverityOf(copy, "Serigrafi pedde"));
            var e = Assert.Single(copy.DrcExclusions);
            Assert.Equal("Yol-yol aralığı", e.Rule);
            Assert.Equal("kabul", e.Comment);
            Assert.Single(DrcPolicy.Apply(copy, DesignRuleChecker.Check(copy)).Excluded);
        }

        [Fact]
        public void TheReportListsMarkersExclusionsAndRulesNotChecked()
        {
            var board = Board();
            DrcPolicy.SetSeverity(board, "Minimum yol genişliği", DrcRuleSeverity.Ignore);
            DrcPolicy.Exclude(board, DesignRuleChecker.Check(board).Single(v => v.Rule == "Yol-yol aralığı"), "tasarım gereği");
            var r = DrcPolicy.Apply(board, DesignRuleChecker.Check(board));
            string text = DrcPolicy.Report(board, r, "deneme", new DateTime(2026, 11, 26, 18, 0, 0));

            Assert.Contains("deneme", text);
            Assert.Contains("2026-11-26 18:00", text);
            Assert.Contains($"{r.Errors} hata, {r.Warnings} uyarı, 1 istisna", text);
            Assert.Contains("** İstisnalar **", text);
            Assert.Contains("tasarım gereği", text);
            Assert.Contains("** Denetlenmeyen kurallar **", text);
            Assert.Contains("Minimum yol genişliği (1 işaret bildirilmedi)", text);
            Assert.EndsWith("** Rapor sonu **" + Environment.NewLine, text);
        }

        [Fact]
        public void EveryRuleTheCheckersReportIsInTheCatalogue()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            AutoPlacer.Place(board);
            var tracks = board.Tracks;
            var pads = board.Pads().Where(p => p.Net == "VBAT").Take(1).Concat(board.Pads().Where(p => p.Net == "GND").Take(1)).ToList();
            tracks.Add(new Track { Net = "VBAT", X1 = pads[0].X, Y1 = pads[0].Y, X2 = pads[1].X, Y2 = pads[1].Y, Width = 0.1 });
            var all = DesignRuleChecker.Check(board).Concat(DfmChecker.Check(board).Issues).Concat(DesignRuleChecker.Check(Board())).ToList();
            Assert.NotEmpty(all);
            Assert.All(all.Select(v => v.Rule).Distinct(), rule => Assert.NotNull(DrcRuleCatalog.Find(rule)));
            Assert.Equal(DrcRuleCatalog.Rules.Count, DrcRuleCatalog.Rules.Select(r => r.Rule).Distinct().Count());
        }
    }
}
