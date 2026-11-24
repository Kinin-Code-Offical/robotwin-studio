using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotWin.Studio.Editor.Completion
{
    /// <summary>What a completion entry is.</summary>
    public enum ApiKind
    {
        Function,
        Method,
        Field,
        Class,
        Constant,
        Variable,
        Keyword,
        Type,
        Snippet,
        Header
    }

    /// <summary>One declaration read from a header or the sketch.</summary>
    public sealed class ApiSymbol
    {
        public ApiSymbol(string name, ApiKind kind, string signature, string? owner = null, string? type = null)
        {
            Name = name;
            Kind = kind;
            Signature = signature;
            Owner = owner;
            Type = type;
        }

        public string Name { get; }
        public ApiKind Kind { get; }

        /// <summary>As declared: "void attach(int pin)", "HardwareSerial Serial", "HIGH 0x1".</summary>
        public string Signature { get; }

        /// <summary>The class a member belongs to.</summary>
        public string? Owner { get; }

        /// <summary>A variable's / field's type, a function's return type.</summary>
        public string? Type { get; }

        /// <summary>The function takes arguments (the caret goes between the brackets).</summary>
        public bool HasParameters => Signature.Contains('(') && !Regex.IsMatch(Signature, @"\(\s*(void)?\s*\)");

        public override string ToString() => Signature;
    }

    /// <summary>A class or struct: its bases and its public members.</summary>
    public sealed class ClassInfo
    {
        public ClassInfo(string name) => Name = name;

        public string Name { get; }
        public List<string> Bases { get; } = new List<string>();
        public List<ApiSymbol> Members { get; } = new List<ApiSymbol>();
    }

    /// <summary>Everything one header declares.</summary>
    public sealed class HeaderInfo
    {
        public HeaderInfo(string name) => Name = name;

        public string Name { get; }

        /// <summary>Free functions, function-like macros, constants, global variables, enumerators.</summary>
        public List<ApiSymbol> Symbols { get; } = new List<ApiSymbol>();

        public Dictionary<string, ClassInfo> Classes { get; } = new Dictionary<string, ClassInfo>(StringComparer.Ordinal);

        /// <summary>Headers it includes (as written between the brackets / quotes).</summary>
        public List<string> Includes { get; } = new List<string>();
    }

    /// <summary>
    /// [code] Reads the declarations of a C/C++ header well enough for completion: classes and structs with their public
    /// methods and fields (and their bases), free functions, global <c>extern</c> objects, enumerators and macros. It is
    /// not a compiler: preprocessor conditions are ignored and template / macro trickery is skipped, which costs a few
    /// extra or missing entries, never a wrong signature for what it does report.
    /// </summary>
    public static class CppHeaderScanner
    {
        private static readonly Regex DefineLine = new Regex(@"^\s*#\s*define\s+([A-Za-z_]\w*)(\([^)]*\))?\s*(.*)$", RegexOptions.Compiled);
        private static readonly Regex IncludeLine = new Regex(@"^\s*#\s*include\s*[<""]([^>""]+)[>""]", RegexOptions.Compiled);
        private static readonly Regex Access = new Regex(@"^\s*(public|private|protected)\s*:\s*", RegexOptions.Compiled);
        private static readonly Regex ClassHead = new Regex(@"^(?:template\s*<.*>\s*)?(class|struct)\s+(?:\w+\s+)*?([A-Za-z_]\w*)\s*(?:final\s*)?(?::\s*(.+))?$", RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex EnumHead = new Regex(@"^(?:typedef\s+)?enum\s*(?:class|struct)?\s*([A-Za-z_]\w*)?\s*(?::\s*[\w\s]+)?$", RegexOptions.Compiled);
        private static readonly Regex FunctionDecl = new Regex(@"^(?<ret>.*?[\w\*&>\s])\s*(?<name>~?[A-Za-z_]\w*)\s*\((?<args>.*)\)\s*(?<tail>(?:const|override|final|noexcept|volatile|\s)*)(?:=\s*(?:0|default|delete))?$",
            RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex VariableDecl = new Regex(@"^(?<type>(?:const\s+|static\s+|volatile\s+|unsigned\s+|signed\s+|long\s+|struct\s+)*[A-Za-z_][\w:<>]*\s*[\*&]?)\s+(?<names>[\*&]?[A-Za-z_]\w*(?:\s*\[[^\]]*\])?(?:\s*=\s*[^,]+)?(?:\s*,\s*[\*&]?[A-Za-z_]\w*(?:\s*\[[^\]]*\])?(?:\s*=\s*[^,]+)?)*)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly string[] Noise =
        {
            "virtual", "static", "inline", "explicit", "extern", "constexpr", "IRAM_ATTR", "ICACHE_RAM_ATTR", "RW_API", "__inline", "__forceinline",
            "friend", "mutable", "register", "__attribute__((always_inline))", "__attribute__((noinline))"
        };

        public static HeaderInfo Scan(string text, string name)
        {
            var info = new HeaderInfo(name);
            var code = new StringBuilder(text.Length);
            // Comments out, preprocessor lines read and removed (line continuations joined first).
            text = text.Replace("\\\r\n", " ").Replace("\\\n", " ");
            text = StripComments(text);
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    var inc = IncludeLine.Match(line);
                    if (inc.Success)
                    {
                        info.Includes.Add(inc.Groups[1].Value.Trim());
                    }
                    else
                    {
                        var def = DefineLine.Match(line);
                        if (def.Success && !def.Groups[1].Value.StartsWith("_", StringComparison.Ordinal))
                        {
                            string dname = def.Groups[1].Value;
                            if (def.Groups[2].Success)
                                info.Symbols.Add(new ApiSymbol(dname, ApiKind.Function, dname + def.Groups[2].Value.Replace(",", ", ").Replace(",  ", ", ")));
                            else if (def.Groups[3].Value.Trim().Length > 0)
                                info.Symbols.Add(new ApiSymbol(dname, ApiKind.Constant, dname + " " + Shorten(def.Groups[3].Value.Trim(), 40)));
                        }
                    }

                    code.Append('\n');
                    continue;
                }

                code.Append(line).Append('\n');
            }

            Parse(code.ToString(), info);
            return info;
        }

        private static string Shorten(string s, int max) => s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        /// <summary>Removes // and /* */ comments, keeping string and character literals.</summary>
        public static string StripComments(string text)
        {
            var sb = new StringBuilder(text.Length);
            int i = 0, n = text.Length;
            while (i < n)
            {
                char c = text[i];
                if (c == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }

                if (c == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? n : end + 2;
                    for (int k = i; k < stop; k++)
                        if (text[k] == '\n') sb.Append('\n');   // keep line numbers
                    i = stop;
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    int start = i++;
                    while (i < n && text[i] != c && text[i] != '\n')
                    {
                        if (text[i] == '\\') i++;
                        i++;
                    }

                    i = Math.Min(n, i + 1);
                    sb.Append(text, start, i - start);
                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private enum Scope
        {
            Global,
            Class,
            Enum,
            Body,
            Other
        }

        private sealed class Frame
        {
            public Scope Scope;
            public ClassInfo? Class;
            public bool Public;
        }

        private static void Parse(string code, HeaderInfo info)
        {
            var stack = new Stack<Frame>();
            stack.Push(new Frame { Scope = Scope.Global, Public = true });
            var stmt = new StringBuilder();
            int paren = 0;
            for (int i = 0; i < code.Length; i++)
            {
                char c = code[i];
                var top = stack.Peek();
                if (top.Scope == Scope.Body || top.Scope == Scope.Other)
                {
                    // Skip a function body / initializer: only the braces matter.
                    if (c == '{') stack.Push(new Frame { Scope = Scope.Body });
                    else if (c == '}') stack.Pop();
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    // Literals are copied whole (a ';' or '{' inside one is not syntax).
                    int start = i++;
                    while (i < code.Length && code[i] != c)
                    {
                        if (code[i] == '\\') i++;
                        i++;
                    }

                    stmt.Append(code, start, Math.Min(code.Length, i + 1) - start);
                    continue;
                }

                if (c == '(') paren++;
                else if (c == ')') paren = Math.Max(0, paren - 1);
                if (paren > 0)
                {
                    stmt.Append(c);
                    continue;
                }

                if (c == ':' && top.Scope == Scope.Class && Access.IsMatch(stmt.ToString() + ":"))
                {
                    var m = Access.Match(stmt.ToString() + ":");
                    top.Public = m.Groups[1].Value == "public";
                    stmt.Clear();
                    continue;
                }

                if (c == ';')
                {
                    if (top.Scope == Scope.Enum) Enumerators(stmt.ToString(), info);
                    else Declaration(Clean(stmt.ToString()), top, info);
                    stmt.Clear();
                    continue;
                }

                if (c == '{')
                {
                    string head = Clean(stmt.ToString());
                    stmt.Clear();
                    if (top.Scope == Scope.Enum)
                    {
                        stack.Push(new Frame { Scope = Scope.Other });
                        continue;
                    }

                    var cm = ClassHead.Match(head);
                    if (cm.Success && !head.Contains('('))
                    {
                        var ci = new ClassInfo(cm.Groups[2].Value);
                        if (cm.Groups[3].Success)
                            foreach (var b in cm.Groups[3].Value.Split(','))
                            {
                                string bn = Regex.Replace(b, @"\b(public|private|protected|virtual)\b", string.Empty).Trim();
                                bn = Regex.Replace(bn, @"<.*$", string.Empty).Trim();
                                if (bn.Length > 0) ci.Bases.Add(bn.Split(':').Last());
                            }

                        if (!info.Classes.ContainsKey(ci.Name)) info.Classes[ci.Name] = ci;
                        else ci = info.Classes[ci.Name];
                        stack.Push(new Frame { Scope = Scope.Class, Class = ci, Public = cm.Groups[1].Value == "struct" });
                        continue;
                    }

                    if (EnumHead.IsMatch(head))
                    {
                        stack.Push(new Frame { Scope = top.Scope == Scope.Class && !top.Public ? Scope.Other : Scope.Enum });
                        continue;
                    }

                    if (Regex.IsMatch(head, @"^(namespace(\s+\w+)?|extern\s+""C\+?\+?"")$"))
                    {
                        stack.Push(new Frame { Scope = Scope.Global, Public = true });
                        continue;
                    }

                    // An inline function definition: its declaration counts, its body is skipped.
                    if (head.Contains('(') && !head.Contains('=')) Declaration(head, top, info);
                    stack.Push(new Frame { Scope = Scope.Body });
                    continue;
                }

                if (c == '}')
                {
                    if (top.Scope == Scope.Enum) Enumerators(stmt.ToString(), info);
                    stmt.Clear();
                    if (stack.Count > 1) stack.Pop();
                    continue;
                }

                stmt.Append(c);
            }
        }

        private static string Clean(string s)
        {
            s = Regex.Replace(s, @"__attribute__\s*\(\(.*?\)\)", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            s = Regex.Replace(s, @"^template\s*<[^>]*(<[^>]*>[^>]*)*>\s*", string.Empty);
            return s;
        }

        private static void Enumerators(string body, HeaderInfo info)
        {
            foreach (var part in body.Split(','))
            {
                string n = part.Split('=')[0].Trim();
                if (Regex.IsMatch(n, @"^[A-Za-z_]\w*$")) info.Symbols.Add(new ApiSymbol(n, ApiKind.Constant, part.Trim()));
            }
        }

        private static void Declaration(string s, Frame scope, HeaderInfo info)
        {
            if (s.Length == 0 || scope.Scope == Scope.Class && !scope.Public) return;
            if (Regex.IsMatch(s, @"^(typedef|using|return|friend|static_assert|template\b|class\s+\w+$|struct\s+\w+$)")) return;
            foreach (var w in Noise) s = Regex.Replace(s, @"(^|\s)" + Regex.Escape(w) + @"(?=\s)", " ");
            s = s.Trim();
            var cls = scope.Class;
            var f = FunctionDecl.Match(s);
            if (f.Success && !s.Contains("operator"))
            {
                string name = f.Groups["name"].Value;
                string ret = f.Groups["ret"].Value.Trim();
                if (name.StartsWith("~", StringComparison.Ordinal) || ret.Length == 0 || Regex.IsMatch(ret, @"[=,]") || name == "if" || name == "while") return;
                if (cls != null && name == cls.Name) return;   // constructor
                string args = Regex.Replace(f.Groups["args"].Value.Trim(), @"\s*,\s*", ", ");
                string sig = $"{ret} {name}({args})";
                if (f.Groups["tail"].Value.Contains("const")) sig += " const";
                if (cls != null) cls.Members.Add(new ApiSymbol(name, ApiKind.Method, sig, cls.Name, ret));
                else info.Symbols.Add(new ApiSymbol(name, ApiKind.Function, sig, null, ret));
                return;
            }

            if (s.Contains('(')) return;   // a constructor call or something we do not read
            var v = VariableDecl.Match(s);
            if (!v.Success) return;
            string type = Regex.Replace(v.Groups["type"].Value, @"\b(const|static|volatile|struct)\b", string.Empty).Trim();
            if (type is "return" or "goto" or "case" or "delete" or "new") return;
            foreach (var part in v.Groups["names"].Value.Split(','))
            {
                string n = Regex.Replace(part.Split('=')[0], @"\[.*$", string.Empty).Trim().TrimStart('*', '&').Trim();
                if (!Regex.IsMatch(n, @"^[A-Za-z_]\w*$")) continue;
                if (cls != null) cls.Members.Add(new ApiSymbol(n, ApiKind.Field, $"{type} {n}", cls.Name, type.TrimEnd('*', '&').Trim()));
                else info.Symbols.Add(new ApiSymbol(n, s.Contains("const ") && n.ToUpperInvariant() == n ? ApiKind.Constant : ApiKind.Variable, $"{type} {n}", null, type.TrimEnd('*', '&').Trim()));
            }
        }
    }
}
