using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobotWin.Studio.Editor.Completion
{
    /// <summary>A completion proposal.</summary>
    public sealed class CompletionItem
    {
        public CompletionItem(string label, ApiKind kind, string detail, string insert, int caret, string? doc)
        {
            Label = label;
            Kind = kind;
            Detail = detail;
            Insert = insert;
            Caret = caret;
            Doc = doc;
        }

        public string Label { get; }
        public ApiKind Kind { get; }

        /// <summary>Signature(s) / type, one per line for overloads.</summary>
        public string Detail { get; }

        public string Insert { get; }

        /// <summary>Where the caret ends up, as an offset into <see cref="Insert"/>.</summary>
        public int Caret { get; }

        public string? Doc { get; }
        internal double Score { get; set; }
        public override string ToString() => Label;
    }

    /// <summary>Proposals for the text from <see cref="Start"/> to the caret.</summary>
    public sealed class CompletionResult
    {
        public CompletionResult(int start, int length, List<CompletionItem> items)
        {
            Start = start;
            Length = length;
            Items = items;
        }

        public int Start { get; }
        public int Length { get; }
        public List<CompletionItem> Items { get; }
    }

    /// <summary>Signatures of the call the caret is in, and which argument it is at.</summary>
    public sealed class SignatureHelp
    {
        public SignatureHelp(string name, List<string> signatures, int argument, string? doc)
        {
            Name = name;
            Signatures = signatures;
            Argument = argument;
            Doc = doc;
        }

        public string Name { get; }
        public List<string> Signatures { get; }
        public int Argument { get; }
        public string? Doc { get; }
    }

    /// <summary>The API visible to a sketch: the core's headers plus those it includes, with their own includes.</summary>
    public sealed class ApiIndex
    {
        public List<ApiSymbol> Symbols { get; } = new List<ApiSymbol>();
        public Dictionary<string, ClassInfo> Classes { get; } = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);
        public HashSet<string> Headers { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gathers the headers reachable from <paramref name="roots"/> (each read once) into one index.</summary>
        public static ApiIndex Build(IEnumerable<string> roots, Func<string, HeaderInfo?> load, int maxHeaders = 200)
        {
            var index = new ApiIndex();
            var queue = new Queue<string>(roots);
            while (queue.Count > 0 && index.Headers.Count < maxHeaders)
            {
                string name = queue.Dequeue();
                if (!index.Headers.Add(name)) continue;
                var h = load(name);
                if (h == null) continue;
                index.Symbols.AddRange(h.Symbols);
                foreach (var kv in h.Classes)
                {
                    if (index.Classes.TryGetValue(kv.Key, out var existing))
                    {
                        existing.Members.AddRange(kv.Value.Members.Where(m => !existing.Members.Any(x => x.Signature == m.Signature)));
                        foreach (var b in kv.Value.Bases) if (!existing.Bases.Contains(b)) existing.Bases.Add(b);
                    }
                    else
                    {
                        index.Classes[kv.Key] = kv.Value;
                    }
                }

                foreach (var inc in h.Includes) queue.Enqueue(Resolve(name, inc));
            }

            return index;
        }

        /// <summary>A quoted include relative to the including header's folder ("utility/twi.h" from "Wire.h").</summary>
        private static string Resolve(string from, string include)
        {
            int slash = from.LastIndexOfAny(new[] { '/', '\\' });
            return slash >= 0 && !include.Contains('/') && from.Contains('/') ? from.Substring(0, slash + 1) + include : include;
        }

        /// <summary>Public members of a class and of its bases.</summary>
        public IEnumerable<ApiSymbol> MembersOf(string type)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            stack.Push(type);
            while (stack.Count > 0)
            {
                string t = stack.Pop();
                if (!seen.Add(t) || !Classes.TryGetValue(t, out var c)) continue;
                foreach (var m in c.Members) yield return m;
                foreach (var b in c.Bases) stack.Push(b);
            }
        }
    }

    /// <summary>What the sketch itself declares.</summary>
    public sealed class SketchModel
    {
        public List<ApiSymbol> Symbols { get; } = new List<ApiSymbol>();

        /// <summary>Object name → its type (from "Servo kol;", "LiquidCrystal_I2C lcd(0x27, 16, 2);", "Foo *p = …").</summary>
        public Dictionary<string, string> Variables { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<string> Includes { get; } = new List<string>();

        private static readonly Regex Include = new Regex(@"^\s*#\s*include\s*[<""]([^>""]+)[>""]", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex Define = new Regex(@"^\s*#\s*define\s+([A-Za-z_]\w*)(\([^)]*\))?\s*(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex Function = new Regex(@"^[ \t]*(?:static\s+|inline\s+)*([A-Za-z_][\w:<>\*& ]*?)\s+\*?([A-Za-z_]\w*)\s*\(([^;{}()]*)\)\s*(?:const\s*)?\{", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex Declaration = new Regex(@"(?:^|[;{}(]|\bfor\s*\()\s*(?:const\s+|static\s+|volatile\s+|unsigned\s+|signed\s+)*([A-Za-z_][\w:<>]*)\s*[\*&]?\s+([A-Za-z_]\w*)\s*(\[[^\]]*\])?\s*(?=[;=,(\[)])", RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly HashSet<string> NotTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "return", "else", "case", "goto", "delete", "new", "throw", "using", "typedef", "define", "include", "if", "while", "for", "switch", "sizeof", "do"
        };

        public static SketchModel Scan(string text)
        {
            var m = new SketchModel();
            string code = CppHeaderScanner.StripComments(text);
            foreach (Match x in Include.Matches(code)) m.Includes.Add(x.Groups[1].Value.Trim());
            foreach (Match x in Define.Matches(code))
            {
                string n = x.Groups[1].Value;
                m.Symbols.Add(x.Groups[2].Success
                    ? new ApiSymbol(n, ApiKind.Function, n + x.Groups[2].Value)
                    : new ApiSymbol(n, ApiKind.Constant, (n + " " + x.Groups[3].Value.Trim()).Trim()));
            }

            var functions = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match x in Function.Matches(code))
            {
                string ret = x.Groups[1].Value.Trim(), name = x.Groups[2].Value;
                if (NotTypes.Contains(ret) || NotTypes.Contains(name) || !functions.Add(name)) continue;
                string args = Regex.Replace(x.Groups[3].Value.Trim(), @"\s+", " ");
                m.Symbols.Add(new ApiSymbol(name, ApiKind.Function, $"{ret} {name}({args})", null, ret));
            }

            foreach (Match x in Declaration.Matches(code))
            {
                string type = x.Groups[1].Value, name = x.Groups[2].Value;
                if (NotTypes.Contains(type) || NotTypes.Contains(name) || functions.Contains(name) || m.Variables.ContainsKey(name)) continue;
                m.Variables[name] = type;
                m.Symbols.Add(new ApiSymbol(name, ApiKind.Variable, $"{type} {name}{x.Groups[3].Value}", null, type));
            }

            // "Servo a, b;" — the names after the first.
            foreach (Match x in Regex.Matches(code, @"(?:^|[;{}])\s*([A-Za-z_]\w*)\s+([A-Za-z_]\w*(?:\s*\([^)]*\))?(?:\s*,\s*[A-Za-z_]\w*(?:\s*\([^)]*\))?)+)\s*;", RegexOptions.Multiline))
            {
                string type = x.Groups[1].Value;
                if (NotTypes.Contains(type)) continue;
                foreach (var part in x.Groups[2].Value.Split(','))
                {
                    string n = Regex.Replace(part, @"\(.*$", string.Empty).Trim();
                    if (n.Length == 0 || m.Variables.ContainsKey(n)) continue;
                    m.Variables[n] = type;
                    m.Symbols.Add(new ApiSymbol(n, ApiKind.Variable, $"{type} {n}", null, type));
                }
            }

            return m;
        }
    }

    /// <summary>
    /// [code] Completion for Arduino sketches: <c>#include &lt;</c> headers, members after <c>obj.</c> / <c>obj-&gt;</c> /
    /// <c>Class::</c> (the object's type read from its declaration in the sketch or the headers' globals, members of the
    /// class and its bases), and otherwise everything in scope: the sketch's own functions, variables and macros, the
    /// API of the core and of the included libraries, keywords, types and snippets. Ranked by how well the typed prefix
    /// matches (exact case, any case, word starts, then letters in order).
    /// </summary>
    public static class CompletionEngine
    {
        private static readonly Regex HeaderContext = new Regex(@"^\s*#\s*include\s*([<""])([^>""]*)$", RegexOptions.Compiled);
        private static readonly Regex MemberContext = new Regex(@"([A-Za-z_]\w*)\s*(?:\[[^\]]*\]\s*)?(\.|->|::)\s*([A-Za-z_]\w*)?$", RegexOptions.Compiled);

        public static CompletionResult? Complete(string text, int caret, ApiIndex api, IEnumerable<string> headers, bool explicitRequest)
        {
            caret = Math.Max(0, Math.Min(caret, text.Length));
            int lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
            string before = text.Substring(lineStart, caret - lineStart);

            var inc = HeaderContext.Match(before);
            if (inc.Success)
            {
                string typed = inc.Groups[2].Value;
                bool closeMissing = caret >= text.Length || (text[caret] != '>' && text[caret] != '"');
                string close = inc.Groups[1].Value == "<" ? ">" : "\"";
                var items = headers.Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(h => new CompletionItem(h, ApiKind.Header, "#include " + inc.Groups[1].Value + h + close, h + (closeMissing ? close : string.Empty), h.Length + (closeMissing ? 1 : 0), null))
                    .ToList();
                return Rank(items, typed, caret - typed.Length, explicitRequest || typed.Length == 0);
            }

            if (InCommentOrString(text, lineStart, caret)) return null;
            var mem = MemberContext.Match(before);
            var model = SketchModel.Scan(text);
            if (mem.Success)
            {
                string obj = mem.Groups[1].Value, op = mem.Groups[2].Value, prefix = mem.Groups[3].Value;
                IEnumerable<ApiSymbol> members;
                if (op == "::")
                {
                    members = api.MembersOf(obj);
                }
                else
                {
                    string? type = TypeOf(obj, model, api);
                    if (type == null) return null;
                    members = api.MembersOf(type);
                }

                var items = Group(members.Where(m => m.Kind == ApiKind.Method || m.Kind == ApiKind.Field || m.Kind == ApiKind.Constant), true);
                return Rank(items, prefix, caret - prefix.Length, true);
            }

            // A plain identifier.
            int start = caret;
            while (start > lineStart && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
            string word = text.Substring(start, caret - start);
            if (word.Length > 0 && char.IsDigit(word[0])) return null;
            if (!explicitRequest && word.Length < 2) return null;
            var all = new List<ApiSymbol>();
            all.AddRange(model.Symbols);
            all.AddRange(api.Symbols);
            all.AddRange(api.Classes.Keys.Select(c => new ApiSymbol(c, ApiKind.Class, "class " + c)));
            var list = Group(all, false);
            var names = new HashSet<string>(list.Select(i => i.Label), StringComparer.Ordinal);
            foreach (var k in ArduinoDocs.Keywords)
                if (names.Add(k)) list.Add(new CompletionItem(k, ApiKind.Keyword, "anahtar sözcük", k, k.Length, ArduinoDocs.Doc(k)));
            foreach (var t in ArduinoDocs.Types)
                if (names.Add(t)) list.Add(new CompletionItem(t, ApiKind.Type, "tür", t, t.Length, ArduinoDocs.Doc(t)));
            foreach (var (label, snippet, doc) in ArduinoDocs.Snippets)
            {
                int at = snippet.IndexOf('$');
                string body = snippet.Replace("$", string.Empty);
                string indent = new string(before.TakeWhile(ch => ch == ' ' || ch == '\t').ToArray());
                body = body.Replace("\n", "\n" + indent);
                list.Add(new CompletionItem(label, ApiKind.Snippet, "kalıp", body, at >= 0 ? at : body.Length, doc));
            }

            return Rank(list, word, start, explicitRequest);
        }

        /// <summary>The type of an object: declared in the sketch, or a global of the headers (Serial, Wire, EEPROM …).</summary>
        public static string? TypeOf(string obj, SketchModel model, ApiIndex api)
        {
            if (model.Variables.TryGetValue(obj, out var t)) return Strip(t);
            var g = api.Symbols.LastOrDefault(s => s.Kind == ApiKind.Variable && s.Name == obj && s.Type != null);
            if (g != null) return Strip(g.Type!);
            if (api.Classes.ContainsKey(obj)) return obj;
            return null;

            static string Strip(string type) => Regex.Replace(type, @"[\*&\s]|^.*::", string.Empty);
        }

        /// <summary>Overloads of one name become one entry listing every signature.</summary>
        private static List<CompletionItem> Group(IEnumerable<ApiSymbol> symbols, bool members)
        {
            var list = new List<CompletionItem>();
            foreach (var g in symbols.Where(s => !s.Name.StartsWith("_", StringComparison.Ordinal)).GroupBy(s => s.Name))
            {
                var first = g.First();
                var sigs = g.Select(s => s.Signature).Distinct().Take(6).ToList();
                bool call = first.Kind is ApiKind.Function or ApiKind.Method;
                bool args = g.Any(s => s.HasParameters);
                string insert = call ? first.Name + "()" : first.Name;
                int caret = call ? first.Name.Length + (args ? 1 : 2) : first.Name.Length;
                list.Add(new CompletionItem(first.Name, first.Kind, string.Join("\n", sigs), insert, caret, ArduinoDocs.Doc(first.Name, members ? first.Owner : null)));
            }

            return list;
        }

        private static CompletionResult? Rank(List<CompletionItem> items, string typed, int start, bool showAll)
        {
            var kept = new List<CompletionItem>();
            foreach (var it in items)
            {
                double s = Score(it.Label, typed);
                if (s <= 0 && !(showAll && typed.Length == 0)) continue;
                // Your own names and the documented Arduino API first, then the rest.
                s += it.Kind switch { ApiKind.Variable => 0.3, ApiKind.Function or ApiKind.Method => 0.2, ApiKind.Snippet => -0.2, ApiKind.Keyword or ApiKind.Type => 0.05, _ => 0 };
                if (it.Doc != null) s += 0.25;
                it.Score = s;
                kept.Add(it);
            }

            if (kept.Count == 0) return null;
            if (kept.Count == 1 && kept[0].Label == typed && kept[0].Kind != ApiKind.Snippet) return null;   // already complete
            var sorted = kept.OrderByDescending(i => i.Score).ThenBy(i => i.Label.Length).ThenBy(i => i.Label, StringComparer.Ordinal).Take(80).ToList();
            return new CompletionResult(start, typed.Length, sorted);
        }

        /// <summary>How well a name matches what was typed: 0 = not at all.</summary>
        public static double Score(string name, string typed)
        {
            if (typed.Length == 0) return 0.5;
            if (name.StartsWith(typed, StringComparison.Ordinal)) return 3 + (name.Length == typed.Length ? 0.5 : 0);
            if (name.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return 2.5;
            // Word starts: "dW" → digitalWrite, "lcdpr" → lcd.print is handled by members.
            string humps = new string(name.Where((ch, i) => i == 0 || char.IsUpper(ch) || name[i - 1] == '_').ToArray());
            if (humps.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) return 2;
            int k = 0;
            foreach (char ch in name)
                if (k < typed.Length && char.ToLowerInvariant(ch) == char.ToLowerInvariant(typed[k])) k++;
            if (k == typed.Length && typed.Length >= 2) return 1;
            return 0;
        }

        private static bool InCommentOrString(string text, int lineStart, int caret)
        {
            // Block comment open before the caret?
            int open = text.LastIndexOf("/*", Math.Max(0, caret - 1), StringComparison.Ordinal);
            if (open >= 0 && text.IndexOf("*/", open, StringComparison.Ordinal) is int close && (close < 0 || close >= caret)) return true;
            bool inString = false;
            char quote = '\0';
            for (int i = lineStart; i < caret; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == quote) inString = false;
                    continue;
                }

                if (c == '/' && i + 1 < caret && text[i + 1] == '/') return true;
                if (c == '"' || c == '\'')
                {
                    inString = true;
                    quote = c;
                }
            }

            return inString;
        }

        /// <summary>The call the caret is inside: its signatures and the argument the caret is at.</summary>
        public static SignatureHelp? Signature(string text, int caret, ApiIndex api)
        {
            caret = Math.Max(0, Math.Min(caret, text.Length));
            int depth = 0, comma = 0, limit = Math.Max(0, caret - 600);
            for (int i = caret - 1; i >= limit; i--)
            {
                char c = text[i];
                if (c == ';' || c == '{' || c == '}') return null;
                if (c == ')') depth++;
                else if (c == '(')
                {
                    if (depth > 0)
                    {
                        depth--;
                        continue;
                    }

                    // The name before the bracket, with its object if any.
                    var m = Regex.Match(text.Substring(Math.Max(0, i - 120), i - Math.Max(0, i - 120)), @"(?:([A-Za-z_]\w*)\s*(\.|->|::)\s*)?([A-Za-z_]\w*)\s*$");
                    if (!m.Success) return null;
                    string name = m.Groups[3].Value;
                    if (ArduinoDocs.Keywords.Contains(name)) return null;
                    IEnumerable<ApiSymbol> candidates;
                    string? owner = null;
                    var model = SketchModel.Scan(text);
                    if (m.Groups[1].Success)
                    {
                        string obj = m.Groups[1].Value;
                        owner = m.Groups[2].Value == "::" ? obj : TypeOf(obj, model, api);
                        if (owner == null) return null;
                        candidates = api.MembersOf(owner).Where(s => s.Name == name);
                    }
                    else
                    {
                        candidates = model.Symbols.Concat(api.Symbols).Where(s => s.Name == name && s.Kind == ApiKind.Function);
                        // A constructor: "Servo s(" is not one, but "LiquidCrystal_I2C lcd(" uses the class's constructors (not read).
                    }

                    var sigs = candidates.Select(s => s.Signature).Distinct().Take(8).ToList();
                    if (sigs.Count == 0) return null;
                    return new SignatureHelp(name, sigs, comma, ArduinoDocs.Doc(name, owner));
                }
                else if (c == ',' && depth == 0) comma++;
            }

            return null;
        }
    }
}
