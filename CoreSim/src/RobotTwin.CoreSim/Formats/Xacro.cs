using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>
    /// [urdf] Expands a ROS xacro file into plain URDF: properties and <c>${…}</c> expressions (Python-style arithmetic,
    /// comparisons, <c>and/or/not</c>, <c>pi</c>, math functions, strings), macros with default (<c>p:=1</c>) and block
    /// parameters (<c>*origin</c>, <c>**content</c>), <c>xacro:include</c>, <c>xacro:if</c> / <c>xacro:unless</c>,
    /// <c>xacro:insert_block</c>, <c>xacro:arg</c> with <c>$(arg …)</c>, <c>$(find pkg)</c> and <c>$(dirname)</c>.
    /// What it does not do (YAML files, Python beyond these expressions) is said in <see cref="Notes"/>.
    /// </summary>
    public sealed class Xacro
    {
        private readonly Dictionary<string, string> _args;
        private readonly List<Dictionary<string, object>> _scopes = new List<Dictionary<string, object>> { new Dictionary<string, object>(StringComparer.Ordinal) };
        private readonly Dictionary<string, Macro> _macros = new Dictionary<string, Macro>(StringComparer.Ordinal);
        private readonly Stack<string> _files = new Stack<string>();
        private int _depth;

        /// <summary>What was left out, for the import's report.</summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>Folders searched for <c>$(find pkg)</c> and <c>package://pkg</c> besides the file's own and its parents'.</summary>
        public List<string> PackagePaths { get; } = new List<string>();

        private sealed class Macro
        {
            public List<(string Name, string? Default, int Block)> Params = new List<(string, string?, int)>();
            public XElement Body = null!;
            public string File = string.Empty;
        }

        private Xacro(IReadOnlyDictionary<string, string>? args)
        {
            _args = args != null ? new Dictionary<string, string>(args.ToDictionary(k => k.Key, k => k.Value), StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>Whether a document uses xacro (a .urdf may too).</summary>
        public static bool Uses(XDocument doc) => doc.Root != null && doc.Root.DescendantsAndSelf().Any(e => IsXacro(e.Name)) ;

        private static bool IsXacro(XName n) => n.NamespaceName.IndexOf("xacro", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Expands a xacro file into a URDF document.</summary>
        public static XDocument Expand(string path, IReadOnlyDictionary<string, string>? args = null) => Expand(path, args, out _);

        public static XDocument Expand(string path, IReadOnlyDictionary<string, string>? args, out List<string> notes)
        {
            var x = new Xacro(args);
            var doc = x.ExpandFile(Path.GetFullPath(path));
            notes = x.Notes;
            return doc;
        }

        private XDocument ExpandFile(string path)
        {
            var source = XDocument.Load(path, LoadOptions.None);
            var root = source.Root ?? throw new InvalidDataException("Boş xacro dosyası.");
            _files.Push(path);
            var output = new XElement(Plain(root.Name), Attributes(root));
            foreach (var n in Process(root.Nodes())) output.Add(n);
            _files.Pop();
            return new XDocument(output);
        }

        private string Dir => Path.GetDirectoryName(_files.Peek()) ?? ".";

        private static XName Plain(XName n) => IsXacro(n) ? n.LocalName : XName.Get(n.LocalName, n.NamespaceName.Length > 0 && !IsXacro(n) ? n.NamespaceName : string.Empty);

        private IEnumerable<XAttribute> Attributes(XElement e) =>
            e.Attributes().Where(a => !a.IsNamespaceDeclaration && !IsXacro(a.Name)).Select(a => new XAttribute(a.Name.LocalName, Substitute(a.Value))).ToList();

        // ---------------------------------------------------------------- elements

        private List<XNode> Process(IEnumerable<XNode> nodes)
        {
            if (++_depth > 200) throw new InvalidDataException("xacro çok derin iç içe (sonsuz makro?).");
            var result = new List<XNode>();
            foreach (var node in nodes.ToList())
            {
                switch (node)
                {
                    case XElement e when IsXacro(e.Name):
                        result.AddRange(Directive(e));
                        break;
                    case XElement e:
                    {
                        var copy = new XElement(Plain(e.Name), Attributes(e));
                        foreach (var c in Process(e.Nodes())) copy.Add(c);
                        result.Add(copy);
                        break;
                    }

                    case XText t:
                        if (!string.IsNullOrWhiteSpace(t.Value)) result.Add(new XText(Substitute(t.Value)));
                        break;
                }
            }

            _depth--;
            return result;
        }

        private IEnumerable<XNode> Directive(XElement e)
        {
            string name = e.Name.LocalName;
            switch (name)
            {
                case "property":
                {
                    string pn = Attr(e, "name");
                    if (e.Attribute("value") != null) Scope[pn] = new Lazy(e.Attribute("value")!.Value);
                    else if (e.Attribute("default") != null && Lookup(pn) == null) Scope[pn] = new Lazy(e.Attribute("default")!.Value);
                    else if (e.Attribute("value") == null && e.Attribute("default") == null) Scope[pn] = new Block(e.Nodes().ToList());
                    return Array.Empty<XNode>();
                }

                case "arg":
                {
                    string an = Attr(e, "name");
                    if (!_args.ContainsKey(an) && e.Attribute("default") != null) _args[an] = Substitute(e.Attribute("default")!.Value);
                    return Array.Empty<XNode>();
                }

                case "macro":
                {
                    var m = new Macro { Body = e, File = _files.Peek() };
                    foreach (var p in Attr(e, "params").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        int block = p.StartsWith("**", StringComparison.Ordinal) ? 2 : p.StartsWith("*", StringComparison.Ordinal) ? 1 : 0;
                        string spec = p.Substring(block);
                        int eq = spec.IndexOf(":=", StringComparison.Ordinal);
                        string? def = null;
                        if (eq >= 0)
                        {
                            def = spec.Substring(eq + 2);
                            spec = spec.Substring(0, eq);
                            if (def.StartsWith("^|", StringComparison.Ordinal)) def = def.Substring(2);
                            else if (def == "^") def = null;
                        }

                        m.Params.Add((spec, def, block));
                    }

                    _macros[Attr(e, "name")] = m;
                    return Array.Empty<XNode>();
                }

                case "include":
                {
                    string file = Substitute(Attr(e, "filename"));
                    string full = Path.IsPathRooted(file) ? file : Path.GetFullPath(Path.Combine(Dir, file));
                    if (!File.Exists(full))
                    {
                        Notes.Add($"xacro:include bulunamadı: {file}");
                        return Array.Empty<XNode>();
                    }

                    var included = XDocument.Load(full).Root;
                    if (included == null) return Array.Empty<XNode>();
                    _files.Push(full);
                    var nodes = Process(included.Nodes());
                    _files.Pop();
                    return nodes;
                }

                case "if":
                case "unless":
                {
                    bool truth = Truth(Substitute(Attr(e, "value")));
                    return truth == (name == "if") ? Process(e.Nodes()) : (IEnumerable<XNode>)Array.Empty<XNode>();
                }

                case "insert_block":
                {
                    string bn = Substitute(Attr(e, "name"));
                    if (Lookup(bn) is Block b) return Process(b.Nodes);
                    Notes.Add($"xacro:insert_block: '{bn}' bloğu yok.");
                    return Array.Empty<XNode>();
                }

                case "load_yaml":
                    Notes.Add("xacro:load_yaml desteklenmiyor (YAML dosyası okunmadı).");
                    return Array.Empty<XNode>();
                default:
                    if (_macros.TryGetValue(name, out var macro)) return Call(macro, e);
                    Notes.Add($"xacro:{name} bilinmiyor; atlandı.");
                    return Array.Empty<XNode>();
            }
        }

        private IEnumerable<XNode> Call(Macro m, XElement call)
        {
            var scope = new Dictionary<string, object>(StringComparer.Ordinal);
            var blocks = call.Elements().ToList();
            int nextBlock = 0;
            foreach (var (pn, def, block) in m.Params)
            {
                if (block == 1)
                {
                    if (nextBlock < blocks.Count) scope[pn] = new Block(new List<XNode> { blocks[nextBlock++] });
                    continue;
                }

                if (block == 2)
                {
                    if (nextBlock < blocks.Count) scope[pn] = new Block(blocks[nextBlock++].Nodes().ToList());
                    continue;
                }

                var given = call.Attributes().FirstOrDefault(a => a.Name.LocalName == pn);
                if (given != null) scope[pn] = new Lazy(Substitute(given.Value), evaluated: true);
                else if (def != null) scope[pn] = new Lazy(def);
                else Notes.Add($"xacro makrosu çağrısında '{pn}' verilmemiş.");
            }

            _scopes.Add(scope);
            _files.Push(m.File);
            var result = Process(m.Body.Nodes());
            _files.Pop();
            _scopes.RemoveAt(_scopes.Count - 1);
            return result;
        }

        private Dictionary<string, object> Scope => _scopes[_scopes.Count - 1];

        private object? Lookup(string name)
        {
            for (int i = _scopes.Count - 1; i >= 0; i--)
                if (_scopes[i].TryGetValue(name, out var v)) return v;
            return null;
        }

        private static string Attr(XElement e, string name) => (string?)e.Attribute(name) ?? string.Empty;

        /// <summary>A property's raw text, evaluated when it is first used (in the scope it is used in).</summary>
        private sealed class Lazy
        {
            public Lazy(string text, bool evaluated = false)
            {
                Text = text;
                Done = evaluated;
            }

            public string Text;
            public bool Done;
            public bool Busy;
        }

        private sealed class Block
        {
            public Block(List<XNode> nodes) => Nodes = nodes;
            public List<XNode> Nodes { get; }
        }

        // ---------------------------------------------------------------- text substitution

        /// <summary>Replaces <c>$(…)</c> and <c>${…}</c> in a text; a text that is just one expression keeps its value's form.</summary>
        private string Substitute(string text)
        {
            if (text.IndexOf('$') < 0) return text;
            var sb = new StringBuilder();
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '$')
                {
                    sb.Append('$');
                    i += 2;
                    continue;
                }

                if (text[i] == '$' && i + 1 < text.Length && (text[i + 1] == '{' || text[i + 1] == '('))
                {
                    char open = text[i + 1], close = open == '{' ? '}' : ')';
                    int depth = 0, j = i + 1;
                    for (; j < text.Length; j++)
                    {
                        if (text[j] == open) depth++;
                        else if (text[j] == close && --depth == 0) break;
                    }

                    if (j >= text.Length) throw new InvalidDataException($"xacro: kapanmayan ifade: {text}");
                    string inner = text.Substring(i + 2, j - i - 2);
                    sb.Append(open == '{' ? Format(Evaluate(Substitute(inner))) : Function(inner));
                    i = j + 1;
                    continue;
                }

                sb.Append(text[i++]);
            }

            return sb.ToString();
        }

        private string Function(string inner)
        {
            var parts = inner.Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string fn = parts.Length > 0 ? parts[0] : string.Empty, arg = parts.Length > 1 ? Substitute(parts[1].Trim()) : string.Empty;
            switch (fn)
            {
                case "arg":
                    if (_args.TryGetValue(arg, out var v)) return v;
                    Notes.Add($"$(arg {arg}) verilmemiş; boş alındı.");
                    return string.Empty;
                case "find":
                    return FindPackage(arg) ?? throw new InvalidDataException($"$(find {arg}): '{arg}' paketi bulunamadı (URDF'nin yanında ya da üst klasörlerinde).");
                case "dirname":
                    return Dir;
                case "env":
                case "optenv":
                {
                    var p = arg.Split(new[] { ' ' }, 2);
                    return Environment.GetEnvironmentVariable(p[0]) ?? (p.Length > 1 ? p[1] : string.Empty);
                }

                case "eval":
                    return Format(Evaluate(arg));
                default:
                    Notes.Add($"$({fn} …) desteklenmiyor.");
                    return string.Empty;
            }
        }

        /// <summary>A ROS package's folder: one named so (or whose package.xml names it) next to the file, above it or in <see cref="PackagePaths"/>.</summary>
        private string? FindPackage(string name) => UrdfImport.FindPackage(name, Dir, PackagePaths);

        private static string Format(object v) => v switch
        {
            double d when Math.Abs(d - Math.Round(d)) < 1e-12 && Math.Abs(d) < 1e15 => ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            _ => v.ToString() ?? string.Empty
        };

        private bool Truth(string s)
        {
            string t = s.Trim();
            if (t.Equals("true", StringComparison.OrdinalIgnoreCase) || t == "1") return true;
            if (t.Equals("false", StringComparison.OrdinalIgnoreCase) || t == "0" || t.Length == 0) return false;
            return Truthy(Evaluate(t));
        }

        private static bool Truthy(object v) => v switch
        {
            bool b => b,
            double d => d != 0,
            string s => s.Length > 0 && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        // ---------------------------------------------------------------- expressions

        /// <summary>A property's value (evaluated once, its own references first).</summary>
        private object Value(string name)
        {
            switch (Lookup(name))
            {
                case Lazy lazy:
                    if (lazy.Done) return Literal(lazy.Text);
                    if (lazy.Busy) throw new InvalidDataException($"xacro: '{name}' özelliği kendine başvuruyor.");
                    lazy.Busy = true;
                    string text = Substitute(lazy.Text);
                    lazy.Busy = false;
                    lazy.Text = text;
                    lazy.Done = true;
                    return Literal(text);
                case Block:
                    throw new InvalidDataException($"xacro: '{name}' bir blok; ifadede kullanılamaz.");
            }

            switch (name)
            {
                case "pi":
                case "math.pi":
                    return Math.PI;
                case "True":
                case "true":
                    return true;
                case "False":
                case "false":
                    return false;
            }

            throw new InvalidDataException($"xacro: '{name}' tanımlı değil.");
        }

        private static object Literal(string text)
        {
            string t = text.Trim();
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return d;
            if (t.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return text;
        }

        private object Evaluate(string expr) => new Parser(expr, this).Run();

        /// <summary>Python-like expression subset (precedence: or, and, not, comparisons, + -, * / // %, unary, **).</summary>
        private sealed class Parser
        {
            private readonly string _s;
            private readonly Xacro _x;
            private int _i;

            public Parser(string s, Xacro x)
            {
                _s = s;
                _x = x;
            }

            public object Run()
            {
                var v = Or();
                Skip();
                if (_i < _s.Length) throw new InvalidDataException($"xacro: ifade okunamadı: {_s}");
                return v;
            }

            private void Skip()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            }

            private bool Word(string w)
            {
                Skip();
                if (string.CompareOrdinal(_s, _i, w, 0, w.Length) != 0) return false;
                int end = _i + w.Length;
                if (end < _s.Length && (char.IsLetterOrDigit(_s[end]) || _s[end] == '_')) return false;
                _i = end;
                return true;
            }

            private bool Op(string op)
            {
                Skip();
                if (string.CompareOrdinal(_s, _i, op, 0, op.Length) != 0) return false;
                _i += op.Length;
                return true;
            }

            private object Or()
            {
                var a = And();
                while (Word("or"))
                {
                    var b = And();
                    a = Truthy(a) ? a : b;
                }

                return a;
            }

            private object And()
            {
                var a = Not();
                while (Word("and"))
                {
                    var b = Not();
                    a = Truthy(a) ? b : a;
                }

                return a;
            }

            private object Not() => Word("not") ? !Truthy(Not()) : Compare();

            private object Compare()
            {
                var a = Sum();
                while (true)
                {
                    if (Op("==")) a = Equal(a, Sum());
                    else if (Op("!=")) a = !Equal(a, Sum());
                    else if (Op("<=")) a = D(a) <= D(Sum());
                    else if (Op(">=")) a = D(a) >= D(Sum());
                    else if (Op("<")) a = D(a) < D(Sum());
                    else if (Op(">")) a = D(a) > D(Sum());
                    else return a;
                }
            }

            private static bool Equal(object a, object b) =>
                a is string || b is string ? string.Equals(Format(a), Format(b), StringComparison.Ordinal) : Math.Abs(D(a) - D(b)) < 1e-12;

            private object Sum()
            {
                var a = Product();
                while (true)
                {
                    if (Op("+"))
                    {
                        var b = Product();
                        a = a is string || b is string ? Format(a) + Format(b) : D(a) + D(b);
                    }
                    else if (Op("-"))
                    {
                        a = D(a) - D(Product());
                    }
                    else
                    {
                        return a;
                    }
                }
            }

            private object Product()
            {
                var a = Unary();
                while (true)
                {
                    if (Op("**")) a = Math.Pow(D(a), D(Unary()));
                    else if (Op("*")) a = D(a) * D(Unary());
                    else if (Op("//")) a = Math.Floor(D(a) / D(Unary()));
                    else if (Op("/")) a = D(a) / D(Unary());
                    else if (Op("%")) a = D(a) % D(Unary());
                    else return a;
                }
            }

            private object Unary()
            {
                if (Op("-")) return -D(Unary());
                if (Op("+")) return D(Unary());
                var a = Atom();
                return Op("**") ? Math.Pow(D(a), D(Unary())) : a;
            }

            private object Atom()
            {
                Skip();
                if (_i >= _s.Length) throw new InvalidDataException($"xacro: ifade eksik: {_s}");
                char c = _s[_i];
                if (c == '(')
                {
                    _i++;
                    var v = Or();
                    if (!Op(")")) throw new InvalidDataException($"xacro: ')' eksik: {_s}");
                    return v;
                }

                if (c == '\'' || c == '"')
                {
                    int end = _s.IndexOf(c, _i + 1);
                    if (end < 0) throw new InvalidDataException($"xacro: kapanmayan dize: {_s}");
                    string str = _s.Substring(_i + 1, end - _i - 1);
                    _i = end + 1;
                    return str;
                }

                if (char.IsDigit(c) || c == '.')
                {
                    int start = _i;
                    while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E' ||
                                              ((_s[_i] == '-' || _s[_i] == '+') && _i > start && (_s[_i - 1] == 'e' || _s[_i - 1] == 'E')))) _i++;
                    return double.Parse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = _i;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_' || _s[_i] == '.')) _i++;
                    string id = _s.Substring(start, _i - start);
                    if (Op("("))
                    {
                        var args = new List<object>();
                        if (!Op(")"))
                        {
                            do args.Add(Or());
                            while (Op(","));
                            if (!Op(")")) throw new InvalidDataException($"xacro: ')' eksik: {_s}");
                        }

                        return Call(id.StartsWith("math.", StringComparison.Ordinal) ? id.Substring(5) : id, args);
                    }

                    return _x.Value(id);
                }

                throw new InvalidDataException($"xacro: beklenmeyen '{c}': {_s}");
            }

            private static object Call(string fn, List<object> a)
            {
                double A(int k) => D(a[k]);
                switch (fn)
                {
                    case "sin": return Math.Sin(A(0));
                    case "cos": return Math.Cos(A(0));
                    case "tan": return Math.Tan(A(0));
                    case "asin": return Math.Asin(A(0));
                    case "acos": return Math.Acos(A(0));
                    case "atan": return Math.Atan(A(0));
                    case "atan2": return Math.Atan2(A(0), A(1));
                    case "sqrt": return Math.Sqrt(A(0));
                    case "abs":
                    case "fabs": return Math.Abs(A(0));
                    case "pow": return Math.Pow(A(0), A(1));
                    case "radians": return A(0) * Math.PI / 180;
                    case "degrees": return A(0) * 180 / Math.PI;
                    case "floor": return Math.Floor(A(0));
                    case "ceil": return Math.Ceiling(A(0));
                    case "min": return a.Select(D).Min();
                    case "max": return a.Select(D).Max();
                    case "float": return A(0);
                    case "int": return Math.Truncate(A(0));
                    case "str": return Format(a[0]);
                    case "bool": return Truthy(a[0]);
                    default: throw new InvalidDataException($"xacro: '{fn}()' işlevi desteklenmiyor.");
                }
            }

            private static double D(object v) => v switch
            {
                double d => d,
                bool b => b ? 1 : 0,
                string s when double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double p) => p,
                _ => throw new InvalidDataException($"xacro: '{v}' bir sayı değil.")
            };
        }
    }
}
