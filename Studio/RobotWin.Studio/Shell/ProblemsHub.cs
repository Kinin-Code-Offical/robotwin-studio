using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotWin.Studio.Shell
{
    /// <summary>[problems] How sure a problem is: an error, a warning, worth knowing, or a check not run since the last change.</summary>
    public enum ProblemLevel
    {
        Error,
        Warning,
        Info,
        Unverified
    }

    /// <summary>[problems] One thing a check found: its area, level, rule and message, and where it is (a jump there).</summary>
    public sealed class Problem
    {
        public Problem(string area, ProblemLevel level, string rule, string message, Action? go = null)
        {
            Area = area;
            Level = level;
            Rule = rule;
            Message = message;
            Go = go;
        }

        public string Area { get; }
        public ProblemLevel Level { get; }
        public string Rule { get; }
        public string Message { get; }

        /// <summary>Opens the editor, selects the item and shows it; null when it has no place.</summary>
        public Action? Go { get; }
    }

    /// <summary>[problems] What one area's checks say now: their problems, or that they have not run (and how to run them).</summary>
    public sealed class AreaReport
    {
        public List<Problem> Problems { get; } = new List<Problem>();

        /// <summary>Why there is no result (never run, or run before the last change); null when the result is current.</summary>
        public string? NotChecked { get; set; }

        /// <summary>Runs the area's checks (DRC, ERC, a build); null when they always run by themselves.</summary>
        public Action? Check { get; set; }

        public int Errors => Problems.Count(p => p.Level == ProblemLevel.Error);
        public int Warnings => Problems.Count(p => p.Level == ProblemLevel.Warning);
    }

    /// <summary>
    /// [problems] VS Code's Problems panel for the whole project (D4 "Proje sağlık merkezi"): every area (circuit ERC, PCB
    /// DRC, the boards' code, the assembly, the project's toolchain) hands what its last check found, with a jump to
    /// each place; an area whose check is out of date says so instead of looking clean. The status bar counts them and
    /// the window lists them by area.
    /// </summary>
    public static class ProblemsHub
    {
        public const string Circuit = "Devre";
        public const string Pcb = "PCB";
        public const string Code = "Kod";
        public const string Assembly = "Montaj";
        public const string Project = "Proje";

        /// <summary>[limits] What the parts' simulation models leave out (worth knowing when reading a result).</summary>
        public const string ModelLimits = "Model sınırları";

        private static readonly List<(string Area, Func<AreaReport?> Source)> Sources = new List<(string, Func<AreaReport?>)>();

        /// <summary>Raised when an area's result changed (a check ran, an edit made one out of date).</summary>
        public static event Action? Changed;

        /// <summary>Adds (or replaces) an area's source; areas list in the order they were first registered. A source returns null when its area does not apply (no board, no code built).</summary>
        public static void Register(string area, Func<AreaReport?> source)
        {
            int i = Sources.FindIndex(s => s.Area == area);
            if (i >= 0) Sources[i] = (area, source);
            else Sources.Add((area, source));
            Notify();
        }

        public static void Notify() => Changed?.Invoke();

        /// <summary>Every area's report now; a source that throws reports that as an unverified area.</summary>
        public static List<(string Area, AreaReport Report)> Collect()
        {
            var all = new List<(string, AreaReport)>();
            foreach (var (area, source) in Sources)
            {
                AreaReport? r;
                try
                {
                    r = source();
                    if (r == null) continue;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException)
                {
                    r = new AreaReport { NotChecked = "Denetim yapılamadı: " + ex.Message };
                }

                all.Add((area, r));
            }

            return all;
        }

        /// <summary>The status bar's figures: errors, warnings, and areas whose checks are out of date.</summary>
        public static (int Errors, int Warnings, int Unverified) Counts(IEnumerable<(string Area, AreaReport Report)> reports)
        {
            int e = 0, w = 0, u = 0;
            foreach (var (_, r) in reports)
            {
                e += r.Errors;
                w += r.Warnings;
                if (r.NotChecked != null) u++;
            }

            return (e, w, u);
        }

        /// <summary>One area's line in the window's header ("Devre: tamam", "PCB: 2 hata, 1 uyarı", "Kod: denetlenmedi").</summary>
        public static string Summary(string area, AreaReport r)
        {
            if (r.Errors + r.Warnings == 0)
            {
                int notes = r.Problems.Count;   // only worth knowing: said as such, not "tamam"
                return r.NotChecked != null ? $"{area}: denetlenmedi" : notes > 0 ? $"{area}: {notes} bilgi" : $"{area}: tamam";
            }
            string s = r.Errors > 0 && r.Warnings > 0 ? $"{r.Errors} hata, {r.Warnings} uyarı" : r.Errors > 0 ? $"{r.Errors} hata" : $"{r.Warnings} uyarı";
            return $"{area}: {s}" + (r.NotChecked != null ? " (eski)" : string.Empty);
        }
    }
}
