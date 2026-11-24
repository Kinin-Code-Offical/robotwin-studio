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
            public Regex Key = null!;
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
            lock (Cache) return Misses?.ToArray() ?? Array.Empty<string>();
        }

        /// <summary>Texts the pack produced (its values, and what its patterns made): seen again, they are not misses.</summary>
        private static readonly HashSet<string> Produced = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.Ordinal);

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
            lock (Cache)
            {
                Cache.Clear();
                Produced.Clear();
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
                            if (p.Value.GetString() is { Length: > 0 } v && Compile(p.Name, v) is { } rx)
                            {
                                var parts = Regex.Split(p.Name, @"\{[^{}]*\}");
                                own.Add(new Pattern
                                {
                                    Key = rx, Value = v, Prefix = parts[0], Suffix = parts[parts.Length - 1],
                                    Anchor = parts.OrderByDescending(x => x.Length).First(), Literal = parts.Sum(x => x.Length)
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

                lock (Cache) Produced.UnionWith(Exact.Values);
                Current = id;
            }
            catch (JsonException)
            {
                Clear();
            }
        }

        /// <summary>
        /// A pattern's key as a regular expression: "{…}" is any text (a number, a name), the value's {0}, {1} … take
        /// them in order. Null for a key that would match nearly any text ("{…}: {…}") or that changes nothing.
        /// </summary>
        private static Regex? Compile(string key, string value)
        {
            var parts = Regex.Split(key, @"\{[^{}]*\}");
            if (parts.Length < 2 || parts.Sum(s => s.Count(char.IsLetter)) < 2) return null;
            int n = 0;
            if (Regex.Replace(key, @"\{[^{}]*\}", _ => "{" + n++ + "}") == value) return null;
            var sb = new StringBuilder("^");
            for (int i = 0; i < parts.Length; i++)
            {
                sb.Append(Regex.Escape(parts[i]));
                if (i == parts.Length - 1) break;
                // A hole stays a whole word: after "yol" it cannot begin with a letter, so "{…} yol{…}" leaves
                // "yollarla" alone; before "una" it cannot end with one.
                bool after = parts[i].Length > 0 && char.IsLetter(parts[i][parts[i].Length - 1]);
                bool before = parts[i + 1].Length > 0 && char.IsLetter(parts[i + 1][0]);
                sb.Append('(').Append(after ? @"(?!\p{L})" : string.Empty).Append(".+?").Append(before ? @"(?<!\p{L})" : string.Empty).Append(')');
            }

            return new Regex(sb.Append('$').ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant);
        }

        /// <summary>A Turkish text in the language in use (itself when the pack has none).</summary>
        public static string T(string text)
        {
            if (Current == "tr" || string.IsNullOrEmpty(text)) return text;
            if (Exact.TryGetValue(text, out var hit)) return hit;
            lock (Cache)   // the splash window translates on its own thread
            {
                if (Cache.TryGetValue(text, out hit)) return hit;
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
                    string t = T(lines[i]);
                    any |= !ReferenceEquals(t, lines[i]) && t != lines[i];
                    lines[i] = t;
                }

                if (any) result = string.Join("\n", lines);
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
            }

            lock (Cache)
            {
                if (Cache.Count < 20000) Cache[text] = result;
                if (!ReferenceEquals(result, text) && Produced.Count < 50000) Produced.Add(result);
                if (Misses != null && ReferenceEquals(result, text) && text.Any(char.IsLetter) && !Produced.Contains(text)) Misses.Add(text);
            }

            return result;
        }

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
