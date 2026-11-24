using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [lang] Language packs: the interface is written in Turkish, and a pack maps its texts to another language. A pack is
    /// a JSON file of exact texts and of patterns whose {…} parts stand for numbers and names:
    /// <code>{ "language": "English", "strings": { "Ayarlar": "Settings" }, "patterns": { "{0} parça · {1} kablo": "{0} parts · {1} cables" } }</code>
    /// The built-in packs are embedded (Lang/*.json); a user's own packs go in %APPDATA%\RobotWin\lang. Every element
    /// that loads has its text, header, content and tooltip looked up, so a pack covers XAML and code alike; with Turkish
    /// nothing is installed and nothing costs. Texts a page writes later (live values) are translated where they are set
    /// through <see cref="T"/>.
    /// </summary>
    public static partial class Lang
    {
        private static readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly List<Pattern> Patterns = new List<Pattern>();

        /// <summary>A pattern: its expression, its value, and the text it must begin with and contain (checked first, cheaply).</summary>
        private sealed class Pattern
        {
            /// <summary>The expression, built the first time a text gets past the cheap checks (most never do).</summary>
            public Regex Key => _key ??= new Regex(Source, RegexOptions.Singleline | RegexOptions.CultureInvariant);

            private Regex? _key;
            public string Source = string.Empty;
            public string Value = string.Empty;
            public string Prefix = string.Empty, Anchor = string.Empty, Suffix = string.Empty;
            public int Literal;
        }

        /// <summary>
        /// --lang-missing: every text with letters the pack had no translation for (null otherwise). Filled from every
        /// thread, read at the end of the run.
        /// </summary>
        public static HashSet<string>? Misses { get; set; }

        /// <summary>The misses so far (<see cref="Misses"/>), safely from any thread.</summary>
        public static string[] MissesSoFar()
        {
            lock (Gate) return Misses?.ToArray() ?? Array.Empty<string>();
        }

        /// <summary>
        /// Texts the pack produced (its values, and what its patterns made): seen again, they are not misses. Like the
        /// cache it keeps two generations: live readouts (a value every frame) fill it with texts seen once, and a full
        /// set that stopped taking new ones left every later text to be worked out again from the patterns.
        /// </summary>
        private static HashSet<string> Produced = new HashSet<string>(StringComparer.Ordinal), OlderProduced = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Translations worked out so far, in two generations: a text asked for again moves to the newer one, so the interface's own texts stay while readouts' one-off values fall out.</summary>
        private static Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal), OlderCache = new Dictionary<string, string>(StringComparer.Ordinal);

        private const int CacheGeneration = 4000, ProducedGeneration = 8000;

        /// <summary>Guards the cache, the produced texts and the misses (the splash window translates on its own thread).</summary>
        private static readonly object Gate = new object();

        private static bool Cached(string text, out string hit)
        {
            if (Cache.TryGetValue(text, out hit!)) return true;
            if (!OlderCache.TryGetValue(text, out hit!)) return false;
            Remember(text, hit);
            return true;
        }

        private static void Remember(string text, string result)
        {
            if (Cache.Count >= CacheGeneration)
            {
                OlderCache = Cache;
                Cache = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            Cache[text] = result;
        }

        private static bool WasProduced(string text)
        {
            if (Produced.Contains(text)) return true;
            if (!OlderProduced.Contains(text)) return false;
            AddProduced(text);
            return true;
        }

        private static void AddProduced(string text)
        {
            if (Produced.Count >= ProducedGeneration)
            {
                OlderProduced = Produced;
                Produced = new HashSet<string>(StringComparer.Ordinal);
            }

            Produced.Add(text);
        }

        /// <summary>The language in use: "tr" (the interface's own) or a pack's id.</summary>
        public static string Current { get; private set; } = "tr";

        public static bool Active => Current != "tr" && (Exact.Count > 0 || Patterns.Count > 0);

        /// <summary>Folder of the user's own packs (one JSON per language; its file name is its id).</summary>
        public static string UserFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWin", "lang");

        /// <summary>The languages to choose from: Turkish, the built-in packs and the user's.</summary>
        public static List<(string Id, string Name)> Available()
        {
            // English (the main language) first, then Turkish (the interface's own), then the other built-in packs.
            var list = new List<(string, string)>();
            var asm = typeof(Lang).Assembly;
            foreach (var res in asm.GetManifestResourceNames().Where(n => n.Contains(".Lang.") && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).OrderBy(n => n.EndsWith(".en.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
            {
                string id = res.Substring(res.LastIndexOf(".Lang.", StringComparison.Ordinal) + 6);
                id = id.Substring(0, id.Length - 5);
                list.Add((id, NameOf(ReadResource(res)) ?? id));
                if (id == "en") list.Add(("tr", "Türkçe"));
            }

            if (!list.Any(x => x.Item1 == "tr")) list.Insert(0, ("tr", "Türkçe"));

            try
            {
                if (Directory.Exists(UserFolder))
                {
                    foreach (var f in Directory.GetFiles(UserFolder, "*.json"))
                    {
                        string id = Path.GetFileNameWithoutExtension(f);
                        if (list.Any(x => x.Item1 == id)) continue;
                        list.Add((id, (NameOf(File.ReadAllText(f)) ?? id) + " (kendi paketiniz)"));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            return list;
        }

        private static string ReadResource(string name)
        {
            using var s = typeof(Lang).Assembly.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }

        private static string? NameOf(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return doc.RootElement.TryGetProperty("language", out var l) ? l.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Loads the language of the settings (the user's pack first, then a built-in one); "tr" clears. A pack's texts
        /// that it lacks come from its fallback ("fallback" in the pack, English for every pack but English itself), so a
        /// new language can grow a text at a time without Turkish showing through.
        /// </summary>
        public static void Load(string id)
        {
            Clear();
            if (string.IsNullOrEmpty(id) || id == "tr") return;
            var json = Find(id);
            if (json == null) return;
            string? fallback = Meta(json, "fallback") ?? (id == "en" ? null : "en");
            var chain = new List<string> { json };
            if (fallback != null && fallback != id && fallback != "tr" && Find(fallback) is { } fb) chain.Add(fb);
            Apply(id, chain);
        }

        /// <summary>Loads a pack from its JSON text (exact texts and patterns) as language <paramref name="id"/>.</summary>
        public static void LoadJson(string id, string json)
        {
            Clear();
            Apply(id, new List<string> { json });
        }

        /// <summary>The culture of the language in use (numbers, dates): the pack's "culture", or the language's own.</summary>
        public static System.Globalization.CultureInfo Culture { get; private set; } = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

        private static void Clear()
        {
            Exact.Clear();
            Patterns.Clear();
            lock (Gate)
            {
                Cache.Clear();
                OlderCache.Clear();
                Produced.Clear();
                OlderProduced.Clear();
                _reverse = null;
            }

            Current = "tr";
            Culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        }

        private static string? Find(string id)
        {
            try
            {
                string user = Path.Combine(UserFolder, id + ".json");
                if (File.Exists(user)) return File.ReadAllText(user);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            var res = typeof(Lang).Assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(".Lang." + id + ".json", StringComparison.OrdinalIgnoreCase));
            return res != null ? ReadResource(res) : null;
        }

        private static string? Meta(string json, string name)
        {
            try
            {
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                return doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The packs in order (the language, then its fallback): the first one that has a text gives it.</summary>
        private static void Apply(string id, List<string> chain)
        {
            try
            {
                string? culture = null;
                foreach (var json in chain)
                {
                    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    culture ??= doc.RootElement.TryGetProperty("culture", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                    if (doc.RootElement.TryGetProperty("strings", out var strings))
                    {
                        foreach (var p in strings.EnumerateObject())
                        {
                            if (p.Value.GetString() is { Length: > 0 } v && !Exact.ContainsKey(p.Name)) Exact[p.Name] = v;
                        }
                    }

                    if (doc.RootElement.TryGetProperty("patterns", out var patterns))
                    {
                        var own = new List<Pattern>();
                        foreach (var p in patterns.EnumerateObject())
                        {
                            if (p.Value.GetString() is not { Length: > 0 } v) continue;
                            var parts = Holes(p.Name);
                            if (Compile(parts, v) is { } rx)   // checked now, built on first use
                            {
                                own.Add(new Pattern
                                {
                                    Source = rx, Value = v, Prefix = Gap.Split(parts[0])[0], Suffix = Gap.Split(parts[parts.Length - 1]).Last(),
                                    Anchor = parts.SelectMany(x => Gap.Split(x)).OrderByDescending(x => x.Length).First(), Literal = parts.Sum(x => x.Length)
                                });
                            }
                        }

                        // The first pattern that fits wins, so the most particular one (the most text of its own) goes first;
                        // the fallback's come after the language's own.
                        own.Sort((a, b) => b.Literal.CompareTo(a.Literal));
                        Patterns.AddRange(own);
                    }
                }

                try
                {
                    Culture = System.Globalization.CultureInfo.GetCultureInfo(culture ?? (id == "en" ? "en-US" : id));
                }
                catch (System.Globalization.CultureNotFoundException)
                {
                    Culture = System.Globalization.CultureInfo.InvariantCulture;
                }

                lock (Gate) OlderProduced.UnionWith(Exact.Values);   // the pack's own values: kept while asked for
                Current = id;
            }
            catch (JsonException)
            {
                Clear();
            }
        }

        /// <summary>
        /// Two or more spaces: a column gap. Readouts align their columns to the width of the numbers ("Sol        205" /
        /// "Sol         59"), so a pattern takes any gap of two or more spaces there.
        /// </summary>
        private static readonly Regex Gap = new Regex(" {2,}", RegexOptions.CultureInvariant);

        /// <summary>
        /// The Turkish text a translation came from (the pack's exact texts read backwards), or null: an example's object
        /// named in the language in use is still found by its Turkish name (--frame "Gövde borusu").
        /// </summary>
        public static string? Source(string translated)
        {
            if (Current == "tr") return translated;
            lock (Gate)
            {
                if (_reverse == null)
                {
                    _reverse = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var kv in Exact) _reverse.TryAdd(kv.Value, kv.Key);
                }
            }

            return _reverse.TryGetValue(translated, out var tr) ? tr : null;
        }

        private static Dictionary<string, string>? _reverse;

        /// <summary>A key's text between its holes ("a {x} b {y}" → "a ", " b ", ""), in one pass.</summary>
        private static string[] Holes(string key)
        {
            var parts = new List<string>();
            int start = 0;
            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] != '{') continue;
                int end = key.IndexOf('}', i + 1);
                if (end < 0) break;
                int inner = key.IndexOf('{', i + 1);
                if (inner >= 0 && inner < end) continue;   // "{{": not a hole
                parts.Add(key.Substring(start, i - start));
                start = end + 1;
                i = end;
            }

            parts.Add(key.Substring(start));
            return parts.ToArray();
        }

        /// <summary>
        /// A pattern's key as a regular expression: "{…}" is any text (a number, a name), the value's {0}, {1} … take
        /// them in order. Null for a key that would match nearly any text ("{…}: {…}") or that changes nothing.
        /// </summary>
        private static string? Compile(string[] parts, string value)
        {
            if (parts.Length < 2 || parts.Sum(s => s.Count(char.IsLetter)) < 2) return null;
            var same = new StringBuilder(parts[0]);
            for (int i = 1; i < parts.Length; i++) same.Append('{').Append(i - 1).Append('}').Append(parts[i]);
            if (same.ToString() == value) return null;
            var sb = new StringBuilder("^");
            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(string.Join(@"\s+", Gap.Split(parts[i]).Select(Regex.Escape)));   // a wide number leaves one space
                if (i == parts.Length - 1) break;
                // A hole stays a whole word: after "yol" it cannot begin with a letter, so "{…} yol{…}" leaves
                // "yollarla" alone; before "una" it cannot end with one.
                bool after = parts[i].Length > 0 && char.IsLetter(parts[i][parts[i].Length - 1]);
                bool before = parts[i + 1].Length > 0 && char.IsLetter(parts[i + 1][0]);
                // Two holes side by side ("%{value}{suffix}") cannot be told apart by text: the first is a number, as the
                // code writes it, else "%100" would split as "1" and "00".
                if (parts[i + 1].Length == 0 && i + 1 < parts.Length - 1)
                {
                    sb.Append(@"([-+]?[0-9]+(?:[.,][0-9]+)*)");
                    continue;
                }

                // The last hole, at the very end, may be empty: the code puts an optional ending there ("{(choked ? " (…)" : "")}").
                bool optional = i == parts.Length - 2 && parts[parts.Length - 1].Length == 0;
                sb.Append('(').Append(after ? @"(?!\p{L})" : string.Empty).Append(optional ? ".*?" : ".+?").Append(before ? @"(?<!\p{L})" : string.Empty).Append(')');
            }

            return sb.Append('$').ToString();
        }

        /// <summary>A Turkish text in the language in use (itself when the pack has none).</summary>
        public static string T(string text)
        {
            if (Current == "tr" || string.IsNullOrEmpty(text)) return text;
            if (Exact.TryGetValue(text, out var hit)) return hit;
            lock (Gate)
            {
                if (Cached(text, out hit)) return hit;
                // A text the pack made (a live readout translated where it is set, seen again by its element): as it is.
                if (WasProduced(text)) return text;
            }

            string result = text;
            // A menu's access key ("_Yardım"): the text without it, the key on the translation's first letter ("_Help").
            if (text.Length > 1 && text[0] == '_' && char.IsLetter(text[1]) && text.IndexOf('_', 1) < 0 && T(text.Substring(1)) is var plain && plain != text.Substring(1))
            {
                result = "_" + plain;
            }
            // A text of several lines: line by line (notes made of sentences that the pack has one by one).
            else if (text.Contains('\n'))
            {
                var lines = text.Split('\n');
                bool any = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    // A "\r\n" text: each line without its "\r" (a pattern's last hole would take it in, and what the
                    // value puts after the hole would land on the next line).
                    bool cr = lines[i].EndsWith("\r", StringComparison.Ordinal);
                    string line = cr ? lines[i].Substring(0, lines[i].Length - 1) : lines[i];
                    string t = T(line);
                    if (!ReferenceEquals(t, line) && t != line)
                    {
                        any = true;
                        lines[i] = cr ? t + "\r" : t;
                    }
                }

                if (any) result = string.Join("\n", lines);
            }
            // Joined from texts the pack has word for word ("İç direnç (0 = varsayılan) (Ω)", "Duvar · beton"): that
            // reading is certain, so it goes before the patterns, one of which ("İç {…}") would cut it elsewhere.
            else if (ExactParts(text) is { } parts)
            {
                result = parts;
            }
            else
            {
                foreach (var pat in Patterns)
                {
                    // Most patterns are ruled out by their own text before the expression runs (canvases draw live values).
                    if (!text.StartsWith(pat.Prefix, StringComparison.Ordinal) || !text.EndsWith(pat.Suffix, StringComparison.Ordinal)
                        || text.IndexOf(pat.Anchor, StringComparison.Ordinal) < 0) continue;
                    var m = pat.Key.Match(text);
                    if (!m.Success) continue;
                    string value = pat.Value;
                    // The parts are translated too ("Durum: Hazır"); each is shorter than the text, so this ends.
                    var args = m.Groups.Cast<Group>().Skip(1).Select(g => (object)(g.Value.Length < text.Length ? T(g.Value) : g.Value)).ToArray();
                    try
                    {
                        result = string.Format(System.Globalization.CultureInfo.InvariantCulture, value, args);
                    }
                    catch (FormatException)
                    {
                        result = text;   // a pack's value with a stray brace: the text stays as it was
                    }

                    break;
                }

                // Texts the interface joins from parts ("Bantlı Konveyör · sabit", "Durum: Hazır", "A → B"): each part
                // on its own when the whole has no translation.
                if (ReferenceEquals(result, text)) result = Joined(text);
            }

            lock (Gate)
            {
                Remember(text, result);
                if (!ReferenceEquals(result, text)) AddProduced(result);
                if (Misses != null && ReferenceEquals(result, text) && text.Any(char.IsLetter) && !WasProduced(text)) Misses.Add(text);
            }

            return result;
        }

        /// <summary>A percentage the way the language writes it: "%15" in Turkish, "15%" in English.</summary>
        public static string Percent(double value, string format = "0.#", IFormatProvider? numbers = null)
        {
            var c = Current == "tr" ? Turkish : Culture;
            string n = value.ToString(format, numbers ?? c);
            return c.NumberFormat.PercentPositivePattern switch { 0 => n + " %", 1 => n + "%", 2 => "%" + n, _ => "% " + n };
        }

        /// <summary>
        /// A file dialog's filter ("Ölçüm kaydı (*.csv)|*.csv|Tüm dosyalar|*.*") in the language in use: the descriptions
        /// translated, the patterns left alone (the dialog is Windows' own, so nothing translates it on the way).
        /// </summary>
        public static string Filter(string filter)
        {
            if (Current == "tr" || string.IsNullOrEmpty(filter)) return filter;
            var parts = filter.Split('|');
            for (int i = 0; i < parts.Length; i += 2) parts[i] = T(parts[i]);
            return string.Join("|", parts);
        }

        private static readonly string[] Joints = { " · ", " → ", " — ", ": " };

        /// <summary>A text joined from parts, each part translated (the text itself when no part changes).</summary>
        private static string Joined(string text)
        {
            foreach (var sep in Joints)
            {
                if (text.IndexOf(sep, StringComparison.Ordinal) <= 0) continue;
                var parts = text.Split(new[] { sep }, StringSplitOptions.None);
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0 || parts[i].Length >= text.Length) continue;
                    string t = T(parts[i]);
                    if (!ReferenceEquals(t, parts[i]) && t != parts[i])
                    {
                        parts[i] = t;
                        any = true;
                    }
                }

                if (any) return string.Join(sep, parts);
            }

            // A trailing aside in brackets: "Analog Entegre (6)", "In3.Cu  (İç 3)" — the name and the aside each.
            var m = Aside.Match(text);
            if (m.Success)
            {
                string head = T(m.Groups[1].Value), inner = T(m.Groups[3].Value);
                if (head != m.Groups[1].Value || inner != m.Groups[3].Value) return head + m.Groups[2].Value + "(" + inner + ")";
            }

            return text;
        }

        /// <summary>
        /// A text joined from parts the pack has exactly (a name and its aside in brackets, parts between the joints),
        /// translated part by part; null when a part with letters has no exact translation.
        /// </summary>
        private static string? ExactParts(string text)
        {
            bool Known(string part, out string t)
            {
                t = part;
                if (!part.Any(char.IsLetter)) return true;   // a number, a sign
                if (Exact.TryGetValue(part, out var e))
                {
                    t = e;
                    return true;
                }

                string core = part.Trim();
                if (core.Length > 0 && core.Length < part.Length && Exact.TryGetValue(core, out e))
                {
                    t = part.Replace(core, e);
                    return true;
                }

                // A part joined from known parts itself ("Durum: Hazır" inside "… · Durum: Hazır").
                if (part.Length < text.Length && ExactParts(part) is { } inner)
                {
                    t = inner;
                    return true;
                }

                return false;
            }

            var m = Aside.Match(text);
            if (m.Success && Exact.ContainsKey(m.Groups[1].Value))
            {
                // The aside in brackets: known, or a unit the same in any language ("Ω", "mm", "V/µs").
                string aside = m.Groups[3].Value;
                if (Known(aside, out var inner) || IsUnit(aside)) return Exact[m.Groups[1].Value] + m.Groups[2].Value + "(" + (inner ?? aside) + ")";
            }
            // A numbered copy of a named thing ("Duvar 2", the second wall a world made): the name and its number.
            var n = Numbered.Match(text);
            if (n.Success && Exact.TryGetValue(n.Groups[1].Value, out var named)) return named + n.Groups[2].Value;
            foreach (var sep in Joints)
            {
                if (text.IndexOf(sep, StringComparison.Ordinal) <= 0) continue;
                var parts = text.Split(new[] { sep }, StringSplitOptions.None);
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!Known(parts[i], out var t)) return null;
                    if (t != parts[i]) any = true;
                    parts[i] = t;
                }

                return any ? string.Join(sep, parts) : null;
            }

            return null;
        }

        private static readonly char[] TurkishLetters = "çğıöşüÇĞİÖŞÜ".ToCharArray();

        /// <summary>A unit or a symbol: at most three letters, none of them Turkish, no word the pack translates.</summary>
        private static bool IsUnit(string s) => s.Count(char.IsLetter) <= 3 && s.IndexOfAny(TurkishLetters) < 0 && !Exact.ContainsKey(s.Trim());

        private static readonly Regex Numbered = new Regex(@"^(.*\p{L}.*?)( \d{1,4})$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Regex Aside = new Regex(@"^(.*\S)(\s+)\(([^()]+)\)$", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        /// <summary>
        /// An example sketch in the language in use: each whole line the pack has (a comment, a printed message) is
        /// replaced, nothing else. Only exact lines, never patterns, so code is never touched.
        /// </summary>
        public static string Code(string source)
        {
            if (Current == "tr" || string.IsNullOrEmpty(source)) return source;
            bool crlf = source.Contains("\r\n");
            var lines = source.Replace("\r\n", "\n").Split('\n');
            bool any = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0 && Exact.TryGetValue(lines[i], out var t))
                {
                    lines[i] = t;
                    any = true;
                }
            }

            if (!any) return source;
            string result = string.Join("\n", lines);
            return crlf ? result.Replace("\n", "\r\n") : result;
        }

        private static readonly System.Globalization.CultureInfo Turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

        /// <summary>
        /// Texts to search in, lower-cased: each one as shown (in the language in use) and as written (Turkish), so a
        /// search finds "resistor" and "direnç" alike. Searched with <see cref="Fold"/>.
        /// </summary>
        public static string Searchable(params string?[] texts)
        {
            var sb = new StringBuilder();
            foreach (var t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                sb.Append(t.ToLower(Turkish)).Append('\n');
                if (Current != "tr") sb.Append(T(t).ToLower(Culture)).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>What the user typed, lower-cased the way of the language in use.</summary>
        public static string Fold(string query) => query.ToLower(Current == "tr" ? Turkish : Culture);

        /// <summary>Number of exact texts and patterns loaded.</summary>
        public static (int Exact, int Patterns) Size => (Exact.Count, Patterns.Count);
    }
}
