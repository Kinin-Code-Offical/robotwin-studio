using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// [drc-policy] What the user set for one rule, as KiCad's Violation Severity page: as the checker reports it, always an
    /// error, always a warning, or not checked at all.
    /// </summary>
    public enum DrcRuleSeverity
    {
        Default,
        Error,
        Warning,
        Ignore
    }

    /// <summary>
    /// [drc-policy] A marker the user looked at and accepted (KiCad's exclusion): it stays out of the counts and off the
    /// board until what it points at changes (another rule, other items, or moved more than <see cref="DrcPolicy.Reach"/>).
    /// </summary>
    public sealed class DrcExclusion
    {
        public string Rule { get; set; } = string.Empty;

        /// <summary>The marker's message as it was when excluded; numbers are not compared, so a small change keeps it.</summary>
        public string Message { get; set; } = string.Empty;

        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Why it was accepted (shown in the list and the report).</summary>
        public string Comment { get; set; } = string.Empty;
    }

    /// <summary>[drc-policy] One rule the checkers report: its group, what it reports by default (null: depends on the case) and what it looks at.</summary>
    public sealed record DrcRuleInfo(string Rule, string Group, DrcSeverity? Default, string Description);

    /// <summary>[drc-policy] Every rule DRC, DFM/DFT, length matching and the enclosure check report, grouped as KiCad groups its severities.</summary>
    public static class DrcRuleCatalog
    {
        public const string Electrical = "Elektriksel";
        public const string Manufacturing = "Üretim";
        public const string SignalIntegrity = "Sinyal bütünlüğü";
        public const string Housekeeping = "Düzen ve bakım";
        public const string Dfm = "Üretim ve test (DFM/DFT)";
        public const string Mechanical = "Mekanik";

        public static IReadOnlyList<DrcRuleInfo> Rules { get; } = new[]
        {
            new DrcRuleInfo("Kısa devre", Electrical, DrcSeverity.Error, "Ayrı hatların bakırı birbirine değiyor"),
            new DrcRuleInfo("Yol-yol aralığı", Electrical, DrcSeverity.Error, "İki yol arası, hat sınıfının aralığından az"),
            new DrcRuleInfo("Yol-ped aralığı", Electrical, DrcSeverity.Error, "Yol ile ped arası"),
            new DrcRuleInfo("Yol-delik aralığı", Electrical, DrcSeverity.Error, "Yol ile kaplamasız delik arası"),
            new DrcRuleInfo("Ped-ped aralığı", Electrical, DrcSeverity.Error, "İki ped arası"),
            new DrcRuleInfo("Via-yol aralığı", Electrical, DrcSeverity.Error, "Via ile yol arası"),
            new DrcRuleInfo("Via-ped aralığı", Electrical, DrcSeverity.Error, "Via ile ped arası"),
            new DrcRuleInfo("Via-via aralığı", Electrical, DrcSeverity.Error, "İki via arası"),
            new DrcRuleInfo("Bağlanmamış", Electrical, DrcSeverity.Error, "Hattın pedleri bakırla birleşmemiş (dolgular sayılır)"),
            new DrcRuleInfo("Akım kapasitesi", Electrical, DrcSeverity.Error, "Yol, simülasyondaki akıma IPC-2221'e göre dar"),
            new DrcRuleInfo("Katman yok", Electrical, DrcSeverity.Error, "Yol ya da ped kartta olmayan bir katmanda"),
            new DrcRuleInfo("Via katmanları", Electrical, DrcSeverity.Error, "Via'nın katman aralığı geçersiz"),
            new DrcRuleInfo("Minimum yol genişliği", Manufacturing, DrcSeverity.Error, "Yol, kuraldaki en küçük genişlikten ince"),
            new DrcRuleInfo("Minimum delik", Manufacturing, DrcSeverity.Error, "Delik, kuraldaki en küçük delikten küçük"),
            new DrcRuleInfo("Halka genişliği", Manufacturing, DrcSeverity.Error, "Deliğin çevresindeki bakır halka dar"),
            new DrcRuleInfo("Delik aralığı", Manufacturing, DrcSeverity.Error, "Yol ya da via montaj deliğine yakın"),
            new DrcRuleInfo("Delik-delik aralığı", Manufacturing, DrcSeverity.Error, "İki deliğin kenarları birbirine yakın"),
            new DrcRuleInfo("Kart kenarı", Manufacturing, DrcSeverity.Error, "Bakır kart kenarına yakın"),
            new DrcRuleInfo("Kör/gömülü via", Manufacturing, DrcSeverity.Error, "Kart ayarları kör/gömülü via'ya izin vermiyor"),
            new DrcRuleInfo("Yasak bölge", Electrical, DrcSeverity.Error, "Yasak bölgenin içinde yol, via, dolgu ya da parça"),
            new DrcRuleInfo("Gövde çakışması", Mechanical, DrcSeverity.Warning, "Parça gövdeleri (courtyard) üst üste ya da montaj deliğinde"),
            new DrcRuleInfo("Kutu", Mechanical, null, "Kart ve parçalar kutunun duvarına, tabanına, kapağına giriyor ya da çok yakın"),
            new DrcRuleInfo("Uzunluk eşleme", SignalIntegrity, DrcSeverity.Warning, "Uzunluk grubundaki hatlar toleranstan farklı"),
            new DrcRuleInfo("Fark çifti uzunluğu", SignalIntegrity, DrcSeverity.Warning, "Fark çiftinin iki hattı farklı uzunlukta"),
            new DrcRuleInfo("Fark çifti eşleşmesi", SignalIntegrity, DrcSeverity.Warning, "Fark çiftinin hatları bulunamadı ya da eşleşmiyor"),
            new DrcRuleInfo("Empedans", SignalIntegrity, DrcSeverity.Warning, "Yolun genişliği, katmanında hat sınıfının hedef empedansını tutturmuyor"),
            new DrcRuleInfo("Fark empedansı", SignalIntegrity, DrcSeverity.Warning, "Fark çiftinin genişliği ve aralığı hedef fark empedansını tutturmuyor"),
            new DrcRuleInfo("Serigrafi pedde", Housekeeping, DrcSeverity.Warning, "Serigrafi pedin üstünden geçiyor (Gerber'de kırpılır)"),
            new DrcRuleInfo("Boşta yol ucu", Housekeeping, DrcSeverity.Warning, "Yolun bir ucu hiçbir yere bağlı değil"),
            new DrcRuleInfo("Bağlantısız via", Housekeeping, DrcSeverity.Warning, "Via ikiden az katmana bağlı"),
            new DrcRuleInfo("Dolgu güncel değil", Housekeeping, DrcSeverity.Warning, "Bakır dolgu son değişiklikten önce yapılmış"),
            new DrcRuleInfo("Dolgu yapılmamış", Housekeeping, DrcSeverity.Warning, "Bakır bölgesi hiç doldurulmamış"),
            new DrcRuleInfo("DFM · Asit tuzağı", Dfm, DrcSeverity.Warning, "Dar açılı yol birleşimleri"),
            new DrcRuleInfo("DFM · Maske köprüsü", Dfm, DrcSeverity.Warning, "Pedler arasında lehim maskesi şeridi kalmıyor"),
            new DrcRuleInfo("DFM · Pedde via", Dfm, DrcSeverity.Warning, "Via, SMD pedinin içinde"),
            new DrcRuleInfo("DFM · Kenara yakın SMD", Dfm, DrcSeverity.Warning, "SMD parça kart kenarına yakın"),
            new DrcRuleInfo("DFM · Fidüsyel yok", Dfm, DrcSeverity.Warning, "Dizgi hattı için fidüsyel işaret yok"),
            new DrcRuleInfo("DFM · Mezar taşı riski", Dfm, DrcSeverity.Warning, "Küçük iki uçlu parçanın pedleri dengesiz"),
            new DrcRuleInfo("DFM · Okunmaz serigrafi", Dfm, DrcSeverity.Warning, "Serigrafi yazısı üreticinin basabileceğinden küçük"),
            new DrcRuleInfo("DFM · Delik çeşidi", Dfm, DrcSeverity.Warning, "Çok sayıda farklı delik çapı"),
            new DrcRuleInfo("DFT · Test erişimi yok", Dfm, DrcSeverity.Warning, "Hatta test probunun değebileceği nokta yok"),
            new DrcRuleInfo("DFT · Prob aralığı", Dfm, DrcSeverity.Warning, "Test noktaları birbirine çok yakın")
        };

        public static DrcRuleInfo? Find(string rule) => Rules.FirstOrDefault(r => string.Equals(r.Rule, rule, StringComparison.Ordinal));

        // [drc-nav] what a marker means for the board, in the order to deal with it (D3's DRC issue navigator)
        public const string Critical = "Kritik";
        public const string Unconnected = "Bağlanmamış";
        public const string Fabrication = "Üretim";
        public const string Length = "Uzunluk";
        public const string MechanicalFit = "Mekanik";
        public const string Other = "Diğer";

        /// <summary>The navigator's categories, the most urgent first.</summary>
        public static IReadOnlyList<string> Categories { get; } = new[] { Critical, Unconnected, Fabrication, Length, MechanicalFit, Other };

        /// <summary>
        /// [drc-nav] A rule's category: Kritik (the board would not work: shorts, clearances, current, layers, rule areas),
        /// Bağlanmamış (open nets, dangling ends, lone vias), Üretim (what the fab or the assembly line minds: sizes, rings,
        /// edges, DFM/DFT, silkscreen, pours), Uzunluk (length and pair matching), Mekanik (bodies, the enclosure).
        /// </summary>
        public static string CategoryOf(string rule)
        {
            switch (rule)
            {
                case "Bağlanmamış":
                case "Boşta yol ucu":
                case "Bağlantısız via":
                    return Unconnected;
                case "Serigrafi pedde":
                case "Dolgu güncel değil":
                case "Dolgu yapılmamış":
                    return Fabrication;
            }

            return Find(rule)?.Group switch
            {
                Electrical => Critical,
                Manufacturing or Dfm => Fabrication,
                SignalIntegrity => Length,
                Mechanical => MechanicalFit,
                _ => rule.StartsWith("DFM", StringComparison.Ordinal) || rule.StartsWith("DFT", StringComparison.Ordinal) ? Fabrication : Other
            };
        }

        public static IEnumerable<string> Groups => Rules.Select(r => r.Group).Distinct();
    }

    /// <summary>[drc-policy] One DRC run after the user's severities and exclusions.</summary>
    public sealed class DrcResult
    {
        /// <summary>Markers to show and count, with their severity as the user set it.</summary>
        public List<DrcViolation> Shown { get; } = new List<DrcViolation>();

        /// <summary>Markers the user accepted (KiCad's exclusions), each with the exclusion it matched.</summary>
        public List<(DrcViolation Violation, DrcExclusion Exclusion)> Excluded { get; } = new List<(DrcViolation, DrcExclusion)>();

        /// <summary>How many markers each ignored rule would have reported.</summary>
        public Dictionary<string, int> Ignored { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Errors => Shown.Count(v => v.Severity == DrcSeverity.Error);
        public int Warnings => Shown.Count(v => v.Severity == DrcSeverity.Warning);
    }

    /// <summary>
    /// [drc-policy] The user's say over DRC, as KiCad's: a severity per rule (<see cref="DesignRules.Severities"/>) and
    /// excluded markers (<see cref="BoardDocument.DrcExclusions"/>), both saved with the board; and the text report.
    /// </summary>
    public static class DrcPolicy
    {
        /// <summary>How far (mm) an excluded marker may move and still be the same one.</summary>
        public const double Reach = 1.0;

        private static readonly Regex Numbers = new Regex(@"[-+]?\d+([.,]\d+)?", RegexOptions.CultureInvariant);

        public static DrcRuleSeverity SeverityOf(BoardDocument board, string rule) =>
            board.Rules.Severities.TryGetValue(rule, out var s) ? s : DrcRuleSeverity.Default;

        /// <summary>Sets a rule's severity; <see cref="DrcRuleSeverity.Default"/> forgets the setting.</summary>
        public static void SetSeverity(BoardDocument board, string rule, DrcRuleSeverity severity)
        {
            if (severity == DrcRuleSeverity.Default) board.Rules.Severities.Remove(rule);
            else board.Rules.Severities[rule] = severity;
        }

        /// <summary>The run as the user wants to see it: ignored rules dropped, severities changed, excluded markers set apart.</summary>
        public static DrcResult Apply(BoardDocument board, IEnumerable<DrcViolation> violations)
        {
            var result = new DrcResult();
            foreach (var v in violations)
            {
                var setting = SeverityOf(board, v.Rule);
                if (setting == DrcRuleSeverity.Ignore)
                {
                    result.Ignored[v.Rule] = result.Ignored.TryGetValue(v.Rule, out int n) ? n + 1 : 1;
                    continue;
                }

                var shown = setting switch
                {
                    DrcRuleSeverity.Error when v.Severity != DrcSeverity.Error => new DrcViolation(DrcSeverity.Error, v.Rule, v.Message, v.X, v.Y, v.Layer),
                    DrcRuleSeverity.Warning when v.Severity != DrcSeverity.Warning => new DrcViolation(DrcSeverity.Warning, v.Rule, v.Message, v.X, v.Y, v.Layer),
                    _ => v
                };
                if (board.DrcExclusions.FirstOrDefault(e => Matches(e, v)) is { } ex) result.Excluded.Add((shown, ex));
                else result.Shown.Add(shown);
            }

            return result;
        }

        /// <summary>Whether an exclusion covers a marker: same rule, same message apart from its numbers, within <see cref="Reach"/>.</summary>
        public static bool Matches(DrcExclusion e, DrcViolation v) =>
            string.Equals(e.Rule, v.Rule, StringComparison.Ordinal)
            && Math.Abs(e.X - v.X) <= Reach && Math.Abs(e.Y - v.Y) <= Reach
            && string.Equals(Shape(e.Message), Shape(v.Message), StringComparison.Ordinal);

        private static string Shape(string message) => Numbers.Replace(message, "#");

        /// <summary>Accepts a marker: it stays out of the counts until it changes.</summary>
        public static DrcExclusion Exclude(BoardDocument board, DrcViolation v, string comment = "")
        {
            if (board.DrcExclusions.FirstOrDefault(e => Matches(e, v)) is { } existing)
            {
                if (comment.Length > 0) existing.Comment = comment;
                return existing;
            }

            var e = new DrcExclusion { Rule = v.Rule, Message = v.Message, X = v.X, Y = v.Y, Comment = comment };
            board.DrcExclusions.Add(e);
            return e;
        }

        /// <summary>Takes a marker's exclusion back; true when there was one.</summary>
        public static bool Include(BoardDocument board, DrcViolation v) => board.DrcExclusions.RemoveAll(e => Matches(e, v)) > 0;

        /// <summary>
        /// A text report as KiCad's DRC report: date, board, counts, then every marker by severity, the excluded ones and
        /// the ignored rules. <paramref name="translate"/> turns each heading, rule and message into the user's language.
        /// </summary>
        public static string Report(BoardDocument board, DrcResult result, string boardName, DateTime when, Func<string, string>? translate = null)
        {
            var t = translate ?? (x => x);
            var c = CultureInfo.InvariantCulture;
            var s = new StringBuilder();
            s.AppendLine("** " + t("RobotWin Studio DRC raporu") + ": " + boardName + " **");
            s.AppendLine("** " + when.ToString("yyyy-MM-dd HH:mm", c) + " **");
            s.AppendLine("** " + t($"Kart: {board.Width.ToString("0.##", c)} × {board.Height.ToString("0.##", c)} mm, {board.LayerCount} katman") + " **");
            s.AppendLine();
            s.AppendLine("** " + t($"{result.Errors} hata, {result.Warnings} uyarı, {result.Excluded.Count} istisna") + " **");
            foreach (var sev in new[] { DrcSeverity.Error, DrcSeverity.Warning })
            {
                var list = result.Shown.Where(v => v.Severity == sev).OrderBy(v => v.Rule, StringComparer.Ordinal).ToList();
                if (list.Count == 0) continue;
                s.AppendLine();
                s.AppendLine("** " + t(sev == DrcSeverity.Error ? "Hatalar" : "Uyarılar") + " **");
                foreach (var v in list) s.AppendLine(Line(v, c, t));
            }

            if (result.Excluded.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("** " + t("İstisnalar") + " **");
                foreach (var (v, e) in result.Excluded) s.AppendLine(Line(v, c, t) + (e.Comment.Length > 0 ? "  — " + e.Comment : string.Empty));
            }

            var ignored = board.Rules.Severities.Where(kv => kv.Value == DrcRuleSeverity.Ignore).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            if (ignored.Count > 0)
            {
                s.AppendLine();
                s.AppendLine("** " + t("Denetlenmeyen kurallar") + " **");
                foreach (var r in ignored) s.AppendLine("    " + t(r) + (result.Ignored.TryGetValue(r, out int n) ? " (" + t($"{n} işaret bildirilmedi") + ")" : string.Empty));
            }

            s.AppendLine();
            s.AppendLine("** " + t("Rapor sonu") + " **");
            return s.ToString();
        }

        private static string Line(DrcViolation v, CultureInfo c, Func<string, string> t) =>
            $"[{t(v.Rule)}]: {t(v.Message)}{Environment.NewLine}    @ ({v.X.ToString("0.###", c)} mm, {v.Y.ToString("0.###", c)} mm)" + (v.Layer is { } l ? " · " + t(PcbLayers.DisplayName(l)) : string.Empty);
    }
}
