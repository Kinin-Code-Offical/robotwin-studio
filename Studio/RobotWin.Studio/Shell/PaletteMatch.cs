using System;
using System.Globalization;

namespace RobotWin.Studio.Shell
{
    /// <summary>One entry of the command palette.</summary>
    public sealed class PaletteCommand
    {
        public PaletteCommand(string group, string title, Action run, string? shortcut = null, string? detail = null)
        {
            Group = group;
            Title = title;
            Run = run;
            Shortcut = shortcut ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public string Group { get; }
        public string Title { get; }
        public string Shortcut { get; }
        public string Detail { get; }
        public Action Run { get; }

        /// <summary>[palette] Other words it answers to (verbs and synonyms: "ekle yerleştir koy", English too).</summary>
        public string Keywords { get; init; } = string.Empty;

        /// <summary>[palette] The page it belongs to ("schematic", "pcb", "world" …): ranked first there; null for everywhere.</summary>
        public string? Context { get; init; }

        /// <summary>[palette] Offered only while this holds (a part is selected, a world is open …); null: always.</summary>
        public Func<bool>? Ready { get; init; }

        /// <summary>[palette] A long list (every part, every probe): shown only when the query names it.</summary>
        public bool OnlyWhenAsked { get; init; }
    }

    /// <summary>[palette] How a query finds a command: word by word, or the letters in order (no WPF: tested on its own).</summary>
    public static class PaletteMatch
    {
        private static readonly char[] Separators = { ' ', '\'', '’', '(', ')', ',', '.', ':', '/', '-', '…' };

        /// <summary>
        /// [palette] A query word by word, in any order ("MOSFET ekle", "servo motorun akımını izle"): every word must be found in
        /// the command's title, group, keywords or detail (Turkish or the language shown) — a word that starts a word there, one
        /// inside it, or its stem without a Turkish case ending ("motorun" → "motor", "akımını" → "akım"); a near spelling as a
        /// last resort. -1 when a word is not there.
        /// </summary>
        public static int WordScore(PaletteCommand c, string query, string? context)
        {
            var words = Fold(query).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return 0;
            string title = Fold(c.Title + " " + Lang.T(c.Title));
            string rest = Fold(c.Group + " " + Lang.T(c.Group) + " " + c.Keywords + " " + c.Detail);
            var titleWords = title.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var restWords = rest.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            int total = 0;
            foreach (string w in words)
            {
                int best = Math.Max(WordIn(w, titleWords, title) + 10, WordIn(w, restWords, rest));
                if (best < 10) return -1;
                total += best;
            }

            if (context != null && c.Context == context) total += 60;   // the page in front first
            return total - title.Length / 8;
        }

        private static int WordIn(string w, string[] words, string whole)
        {
            int best = int.MinValue / 2;
            string stem = w.Length > 5 ? w.Substring(0, Math.Max(4, w.Length - 3)) : w;
            foreach (string t in words)
            {
                if (t == w) return 100;
                if (t.StartsWith(w, StringComparison.Ordinal)) best = Math.Max(best, 80);
                else if (w.StartsWith(t, StringComparison.Ordinal) && t.Length >= 4) best = Math.Max(best, 60);   // "motorun" for "motor"
                else if (t.StartsWith(stem, StringComparison.Ordinal)) best = Math.Max(best, 55);
            }

            if (best < 0 && w.Length >= 3 && whole.IndexOf(w, StringComparison.Ordinal) >= 0) best = 40;
            return best;
        }

        public static string Fold(string s) => s.ToLower(new CultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ş', 's').Replace('ğ', 'g').Replace('ü', 'u').Replace('ö', 'o').Replace('ç', 'c');

        /// <summary>Subsequence match score: consecutive and word-start hits rank higher; -1 = no match.</summary>
        public static int Score(string text, string query)
        {
            if (query.Length == 0) return 0;
            string t = Fold(text), q = Fold(query);
            int direct = t.IndexOf(q, StringComparison.Ordinal);
            if (direct >= 0) return 1000 - direct * 3 - t.Length / 4 + (direct == 0 || t[direct - 1] == ' ' ? 200 : 0);
            int score = 0, ti = 0, run = 0;
            foreach (char c in q)
            {
                if (c == ' ') continue;
                int found = t.IndexOf(c, ti);
                if (found < 0) return -1;
                run = found == ti ? run + 1 : 0;
                score += 10 + run * 8 + (found == 0 || t[found - 1] == ' ' ? 15 : 0);
                ti = found + 1;
            }

            return score - t.Length / 4;
        }
    }
}
