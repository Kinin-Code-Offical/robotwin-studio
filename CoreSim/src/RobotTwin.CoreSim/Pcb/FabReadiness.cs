using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[fab-ready] How a check of the board's readiness came out; NotChecked says plainly that it was not done.</summary>
    public enum ReadinessState
    {
        Pass,
        Warning,
        Fail,
        NotChecked
    }

    /// <summary>[fab-ready] One line of the readiness screen: what was checked, how it came out and the figures behind it.</summary>
    public sealed record ReadinessItem(string Name, ReadinessState State, string Detail);

    /// <summary>
    /// [fab-ready] The manufacturing readiness screen (D3): before the files go to the fab, every check the board has in one
    /// list, each with its outcome: the outline and size, the design rules, every net connected, the pours up to date, the
    /// fab's and the test's concerns (DFM/DFT), the drills, the Gerber and drill files read back by the independent reader
    /// and laid over the board, the BOM and the pick-and-place. What is not checked here is listed as such, not left out.
    /// </summary>
    public static class FabReadiness
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<ReadinessItem> Check(BoardDocument board, string projectName = "robotwin")
        {
            var items = new List<ReadinessItem>();
            var stats = BoardStatistics.Of(board);

            // the outline and its size
            bool outline = stats.Area > 0;
            items.Add(new ReadinessItem("Kart kenarı ve ölçüler",
                !outline ? ReadinessState.Fail : stats.Width > 500 || stats.Height > 500 ? ReadinessState.Warning : ReadinessState.Pass,
                outline
                    ? string.Format(Inv, "{0:0.##} × {1:0.##} mm, {2} bakır katman, {3:0.##} mm kalınlık", stats.Width, stats.Height, stats.CopperLayers, stats.Thickness)
                      + (stats.Width > 500 || stats.Height > 500 ? " · çoğu üreticinin 500 mm sınırını aşıyor" : string.Empty)
                    : "Kart kenarı kapalı değil: üretici kartın biçimini bilemez"));

            // the design rules (what is not a connection: that has its own line)
            var drc = DesignRuleChecker.Check(board);
            var rules = drc.Where(v => DrcRuleCatalog.CategoryOf(v.Rule) != DrcRuleCatalog.Unconnected && v.Rule is not ("Dolgu güncel değil" or "Dolgu yapılmamış")).ToList();
            int errors = rules.Count(v => v.Severity == DrcSeverity.Error), warnings = rules.Count - errors;
            items.Add(new ReadinessItem("Tasarım kuralları (DRC)",
                errors > 0 ? ReadinessState.Fail : warnings > 0 ? ReadinessState.Warning : ReadinessState.Pass,
                errors + warnings == 0 ? "Kural ihlali yok" : $"{errors} hata, {warnings} uyarı" + Firsts(rules)));

            // every net connected
            var unrouted = DesignRuleChecker.UnroutedNets(board).Select(u => u.Net).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            items.Add(new ReadinessItem("Bağlantı bütünlüğü",
                unrouted.Count == 0 ? ReadinessState.Pass : ReadinessState.Fail,
                unrouted.Count == 0 ? $"{stats.Nets} hattın hepsi bakırla bağlı" : $"{unrouted.Count} hat bağlanmamış: " + string.Join(", ", unrouted.Take(5)) + (unrouted.Count > 5 ? " …" : string.Empty)));

            // the pours
            int stale = drc.Count(v => v.Rule is "Dolgu güncel değil" or "Dolgu yapılmamış");
            items.Add(new ReadinessItem("Bakır dolgular",
                stale > 0 ? ReadinessState.Warning : ReadinessState.Pass,
                board.Zones.Count == 0 ? "Kartta dolgu yok" : stale > 0 ? $"{stale} dolgu son değişiklikten önce yapılmış ya da hiç yapılmamış: yeniden doldurun" : $"{board.Zones.Count} dolgu güncel"));

            // the fab's and the test's concerns
            var dfm = DfmChecker.Check(board);
            items.Add(new ReadinessItem("Üretim ve test (DFM/DFT)",
                dfm.Issues.Count == 0 ? ReadinessState.Pass : ReadinessState.Warning,
                (dfm.Issues.Count == 0 ? "Uyarı yok" : $"{dfm.Issues.Count} uyarı" + Firsts(dfm.Issues)) + string.Format(Inv, " · test erişimi %{0:0}", dfm.Coverage * 100)));

            // the drills
            var drills = stats.Drills;
            double smallest = drills.Count == 0 ? 0 : drills.Min(d => d.Diameter);
            int sizes = drills.Select(d => Math.Round(d.Diameter, 3)).Distinct().Count();
            var drillState = drills.Count == 0 ? ReadinessState.Pass
                : smallest < board.Rules.MinDrill - 1e-9 ? ReadinessState.Fail
                : sizes > 8 ? ReadinessState.Warning : ReadinessState.Pass;
            items.Add(new ReadinessItem("Delikler", drillState,
                drills.Count == 0 ? "Delik yok"
                    : string.Format(Inv, "{0} delik, {1} çap, en küçüğü {2:0.###} mm (kural {3:0.###} mm)", stats.Holes, sizes, smallest, board.Rules.MinDrill)
                      + (sizes > 8 ? " · çok çap: üretici ek ücret alabilir" : string.Empty)));

            // the files themselves, read back by the independent reader
            try
            {
                var files = GerberExporter.Generate(board, projectName);
                var report = GerberCheck.Check(board, files);
                items.Add(new ReadinessItem("Gerber ve delik dosyaları", report.Ok ? ReadinessState.Pass : ReadinessState.Fail, report.Summary()));
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is FormatException)
            {
                items.Add(new ReadinessItem("Gerber ve delik dosyaları", ReadinessState.Fail, "Dosyalar üretilemedi: " + ex.Message));
            }

            // the BOM and the pick-and-place
            var noValue = board.Components.Where(c => string.IsNullOrWhiteSpace(c.Value)).Select(c => c.RefDes).ToList();
            var noFootprint = board.Components.Where(c => string.IsNullOrWhiteSpace(c.FootprintId)).Select(c => c.RefDes).ToList();
            int bomLines = Math.Max(0, GerberExporter.Bom(board).Split('\n').Count(l => l.Trim().Length > 0) - 1);
            items.Add(new ReadinessItem("Malzeme listesi (BOM)",
                noFootprint.Count > 0 ? ReadinessState.Fail : noValue.Count > 0 ? ReadinessState.Warning : ReadinessState.Pass,
                $"{board.Components.Count} parça, {bomLines} satır"
                + (noFootprint.Count > 0 ? " · kılıfı olmayan: " + string.Join(", ", noFootprint.Take(5)) : string.Empty)
                + (noValue.Count > 0 ? " · değeri yazılmamış: " + string.Join(", ", noValue.Take(5)) : string.Empty)));
            items.Add(new ReadinessItem("Dizgi dosyası (CPL)", ReadinessState.Pass,
                $"{stats.PartsSmd} SMD, {stats.PartsThroughHole} delikli parça; üstte {stats.PartsTop}, altta {stats.PartsBottom}"
                + (stats.PartsSmd == 0 ? " · SMD yok: dizgi makinesi gerekmez" : string.Empty)));

            // what this screen does not check: said, not left out
            items.Add(Impedance.Readiness(board));   // [impedance] from the stackup
            items.Add(FabRules.Readiness(board));   // [fab-rules] the chosen fab's published limits
            items.Add(new ReadinessItem("Panelleme", ReadinessState.NotChecked, "Panel (V-kesim, kırma tırnakları) hazırlanmadı: üretici kartı tek parça alır"));
            return items;
        }

        /// <summary>"7 tamam · 1 uyarı · 1 sorun · 3 denetlenmedi".</summary>
        public static string Summary(IReadOnlyCollection<ReadinessItem> items)
        {
            int pass = items.Count(i => i.State == ReadinessState.Pass), warn = items.Count(i => i.State == ReadinessState.Warning);
            int fail = items.Count(i => i.State == ReadinessState.Fail), none = items.Count(i => i.State == ReadinessState.NotChecked);
            return $"{pass} tamam · {warn} uyarı · {fail} sorun · {none} denetlenmedi";
        }

        /// <summary>Whether the files may go: nothing failed.</summary>
        public static bool Ready(IEnumerable<ReadinessItem> items) => items.All(i => i.State != ReadinessState.Fail);

        private static string Firsts(IEnumerable<DrcViolation> list)
        {
            var rules = list.GroupBy(v => v.Rule).OrderByDescending(g => g.Count()).Take(3).Select(g => $"{g.Key} ×{g.Count()}").ToList();
            return rules.Count == 0 ? string.Empty : " (" + string.Join(", ", rules) + ")";
        }
    }
}
