using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[erc-policy] What the user set for one ERC rule (KiCad's ERC severities): as the checker says, or always one level, or not checked.</summary>
    public enum ErcRuleSeverity
    {
        Default,
        Error,
        Warning,
        Info,
        Ignore
    }

    /// <summary>
    /// [erc-policy] An ERC marker the user accepted (KiCad's ERC exclusion), by what it is about rather than where it is:
    /// the rule, the part and the net. It holds while the same rule finds the same thing on them, wherever they are moved.
    /// </summary>
    public sealed class ErcExclusion
    {
        public string Code { get; set; } = string.Empty;
        public string? RefDes { get; set; }
        public string? Net { get; set; }

        /// <summary>The marker's message as it was when excluded; numbers are not compared.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Why it was accepted.</summary>
        public string Comment { get; set; } = string.Empty;
    }

    /// <summary>[erc-policy] One ERC rule: its id, its name, what it reports by default (null: depends on the case) and what it looks at.</summary>
    public sealed record ErcRuleInfo(string Code, string Name, ErcSeverity? Default, string Description);

    public static class ErcRuleCatalog
    {
        public static IReadOnlyList<ErcRuleInfo> Rules { get; } = new[]
        {
            new ErcRuleInfo("unknown-part", "Bilinmeyen parça", ErcSeverity.Error, "Parça kütüphanede yok"),
            new ErcRuleInfo("not-annotated", "Numaralandırılmamış", ErcSeverity.Error, "Parçanın tanımlayıcısı numarasız (R?)"),
            new ErcRuleInfo("duplicate-refdes", "Yinelenen tanımlayıcı", ErcSeverity.Error, "Aynı tanımlayıcı birden çok parçada"),
            new ErcRuleInfo("single-pin-net", "Tek pinli hat", ErcSeverity.Warning, "Hatta yalnızca bir pin var"),
            new ErcRuleInfo("pin-unconnected", "Bağlanmamış pin", null, "Pin hiçbir yere bağlı değil (geliştirme kartlarında bilgi)"),
            new ErcRuleInfo("nc-connected", "Bağlı NC pini", ErcSeverity.Warning, "'Bağlantı yok' işaretli pin bir hatta"),
            new ErcRuleInfo("output-conflict", "Çıkış çakışması", ErcSeverity.Error, "İki çıkış aynı hattı sürüyor"),
            new ErcRuleInfo("output-to-power", "Çıkış beslemede", ErcSeverity.Error, "Bir çıkış besleme hattına bağlı"),
            new ErcRuleInfo("supply-conflict", "Besleme çakışması", ErcSeverity.Warning, "İki besleme kaynağı aynı hatta"),
            new ErcRuleInfo("power-not-driven", "Beslenmeyen güç girişi", ErcSeverity.Error, "Güç girişinin hattında kaynak yok"),
            new ErcRuleInfo("input-not-driven", "Sürülmeyen giriş", ErcSeverity.Warning, "Girişin hattını hiçbir şey sürmüyor"),
            new ErcRuleInfo("wire-dangling", "Boşta kablo ucu", ErcSeverity.Warning, "Kablo ucu hiçbir pine ya da kabloya değmiyor"),
            new ErcRuleInfo("label-unconnected", "Bağlanmamış etiket", ErcSeverity.Warning, "Etiket hiçbir kabloya ya da pine değmiyor"),
            new ErcRuleInfo("label-single", "Tek etiket", ErcSeverity.Warning, "Aynı adda başka etiket yok ve hatta tek pin var"),
            new ErcRuleInfo("nc-floating", "Boşta NC işareti", ErcSeverity.Warning, "NC işareti bir pinin üzerinde değil"),
            new ErcRuleInfo("hier-label-no-pin", "Pinsiz hiyerarşik etiket", ErcSeverity.Warning, "Hiyerarşik etiketin sayfa sembolünde pini yok"),
            new ErcRuleInfo("sheet-pin-no-label", "Etiketsiz sayfa pini", ErcSeverity.Warning, "Sayfa pininin alt sayfada hiyerarşik etiketi yok")
        };

        public static ErcRuleInfo? Find(string code) => Rules.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.Ordinal));
    }

    /// <summary>[erc-policy] One ERC run after the user's severities and exclusions.</summary>
    public sealed class ErcResult
    {
        public List<ErcViolation> Shown { get; } = new List<ErcViolation>();
        public List<(ErcViolation Violation, ErcExclusion Exclusion)> Excluded { get; } = new List<(ErcViolation, ErcExclusion)>();
        public Dictionary<string, int> Ignored { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Errors => Shown.Count(v => v.Severity == ErcSeverity.Error);
        public int Warnings => Shown.Count(v => v.Severity == ErcSeverity.Warning);
        public int Infos => Shown.Count(v => v.Severity == ErcSeverity.Info);
    }

    /// <summary>[erc-policy] The user's say over ERC: a severity per rule and accepted markers, both saved with the schematic.</summary>
    public static class ErcPolicy
    {
        private static readonly Regex Numbers = new Regex(@"[-+]?\d+([.,]\d+)?", RegexOptions.CultureInvariant);

        public static ErcRuleSeverity SeverityOf(Schematic s, string code) =>
            s.ErcSeverities.TryGetValue(code, out var v) ? v : ErcRuleSeverity.Default;

        public static void SetSeverity(Schematic s, string code, ErcRuleSeverity severity)
        {
            if (severity == ErcRuleSeverity.Default) s.ErcSeverities.Remove(code);
            else s.ErcSeverities[code] = severity;
        }

        public static ErcResult Apply(Schematic s, IEnumerable<ErcViolation> violations)
        {
            var result = new ErcResult();
            foreach (var v in violations)
            {
                var setting = SeverityOf(s, v.Code);
                if (setting == ErcRuleSeverity.Ignore)
                {
                    result.Ignored[v.Code] = result.Ignored.TryGetValue(v.Code, out int n) ? n + 1 : 1;
                    continue;
                }

                var level = setting switch
                {
                    ErcRuleSeverity.Error => ErcSeverity.Error,
                    ErcRuleSeverity.Warning => ErcSeverity.Warning,
                    ErcRuleSeverity.Info => ErcSeverity.Info,
                    _ => v.Severity
                };
                var shown = level == v.Severity ? v : new ErcViolation(level, v.Code, v.Message, v.Location, v.RefDes, v.Net);
                if (s.ErcExclusions.FirstOrDefault(e => Matches(e, v)) is { } ex) result.Excluded.Add((shown, ex));
                else result.Shown.Add(shown);
            }

            return result;
        }

        /// <summary>Same rule, same part, same net and the same message apart from its numbers.</summary>
        public static bool Matches(ErcExclusion e, ErcViolation v) =>
            string.Equals(e.Code, v.Code, StringComparison.Ordinal)
            && string.Equals(e.RefDes ?? string.Empty, v.RefDes ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Net ?? string.Empty, v.Net ?? string.Empty, StringComparison.Ordinal)
            && string.Equals(Numbers.Replace(e.Message, "#"), Numbers.Replace(v.Message, "#"), StringComparison.Ordinal);

        public static ErcExclusion Exclude(Schematic s, ErcViolation v, string comment = "")
        {
            if (s.ErcExclusions.FirstOrDefault(e => Matches(e, v)) is { } existing)
            {
                if (comment.Length > 0) existing.Comment = comment;
                return existing;
            }

            var e = new ErcExclusion { Code = v.Code, RefDes = v.RefDes, Net = v.Net, Message = v.Message, Comment = comment };
            s.ErcExclusions.Add(e);
            return e;
        }

        public static bool Include(Schematic s, ErcViolation v) => s.ErcExclusions.RemoveAll(e => Matches(e, v)) > 0;
    }
}
