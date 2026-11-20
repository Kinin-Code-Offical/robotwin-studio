using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Sdk.Expressions
{
    /// <summary>
    /// A syntax or name error in an expression program. <see cref="Line"/> and <see cref="Column"/>
    /// are 1-based; the message is Turkish (shown as-is in the UI).
    /// </summary>
    public sealed class ExpressionException : Exception
    {
        public ExpressionException(string message, int position, int line, int column)
            : base($"Satır {line}, sütun {column}: {message}")
        {
            Position = position;
            Line = line;
            Column = column;
            Detail = message;
        }

        public int Position { get; }
        public int Line { get; }
        public int Column { get; }

        /// <summary>The message without the position prefix.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// The variables an expression program may read and write. Each name maps to a slot of a
    /// <c>double[]</c> passed to <see cref="ExpressionProgram.Run"/>. Names are case-sensitive
    /// ("F.x" and "f.x" differ) so that physics notation stays unambiguous.
    /// </summary>
    public sealed class ExpressionScope
    {
        private readonly Dictionary<string, int> _slots = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> _writable = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Count => _slots.Count;

        public IEnumerable<string> Names => _slots.Keys;

        /// <summary>Declares a variable (or returns its existing slot). Writable variables are the program's outputs.</summary>
        public int Declare(string name, bool writable = false)
        {
            if (!_slots.TryGetValue(name, out int slot))
            {
                slot = _slots.Count;
                _slots[name] = slot;
            }

            if (writable) _writable.Add(name);
            return slot;
        }

        /// <summary>Another spelling for a declared variable ("Fx" for "F.x").</summary>
        public void Alias(string alias, string target) => _aliases[alias] = target;

        public bool TryGet(string name, out int slot)
        {
            if (_aliases.TryGetValue(name, out var target)) name = target;
            return _slots.TryGetValue(name, out slot);
        }

        public int this[string name] => TryGet(name, out int s) ? s : throw new KeyNotFoundException(name);

        public bool IsWritable(string name)
        {
            if (_aliases.TryGetValue(name, out var target)) name = target;
            return _writable.Contains(name);
        }

        public string Canonical(string name) => _aliases.TryGetValue(name, out var target) ? target : name;

        public bool Contains(string name) => TryGet(name, out _);

        /// <summary>A fresh slot array large enough for every variable (locals included).</summary>
        public double[] NewSlots() => new double[Math.Max(1, _slots.Count)];

        public ExpressionScope Clone()
        {
            var c = new ExpressionScope();
            foreach (var kv in _slots.OrderBy(k => k.Value)) c.Declare(kv.Key, _writable.Contains(kv.Key));
            foreach (var kv in _aliases) c._aliases[kv.Key] = kv.Value;
            return c;
        }
    }

    /// <summary>
    /// A compiled program of the in-app expression language:
    /// <code>
    /// k = 20; c = 0.5          # locals
    /// F.x = -k*(x - x0) - c*vx # outputs
    /// wind.x = 3*sin(t)
    /// </code>
    /// Statements are separated by ';' or new lines; '#' or '//' start a comment. Operators by
    /// increasing precedence: <c>?:</c>, <c>||</c>, <c>&amp;&amp;</c>, comparisons, <c>+ -</c>,
    /// <c>* / %</c>, unary <c>- + !</c>, <c>^</c> (power, right-associative, so <c>-2^2 = -4</c>).
    /// Booleans are numbers (0 = false). There are no loops or user functions, so every program
    /// finishes in a bounded number of operations.
    /// </summary>
    public sealed class ExpressionProgram
    {
        public const int MaxSourceLength = 8000;
        public const int MaxDepth = 64;
        public const int MaxNodes = 4000;

        private readonly Action<double[]>[] _statements;

        private ExpressionProgram(string source, ExpressionScope scope, Action<double[]>[] statements, List<string> assigned, List<string> read)
        {
            Source = source;
            Scope = scope;
            _statements = statements;
            Assigned = assigned;
            Read = read;
        }

        public string Source { get; }

        /// <summary>The scope the program was compiled against, including its local variables.</summary>
        public ExpressionScope Scope { get; }

        /// <summary>Canonical names of the outputs and locals the program assigns, in order of first assignment.</summary>
        public IReadOnlyList<string> Assigned { get; }

        /// <summary>Canonical names of the variables the program reads.</summary>
        public IReadOnlyList<string> Read { get; }

        /// <summary>
        /// Compiles a program. Unknown names may be assigned (they become locals); reading a name
        /// that is neither declared nor assigned earlier is an error, as is assigning a read-only
        /// variable. The scope is copied: locals never leak into the caller's scope.
        /// </summary>
        public static ExpressionProgram Compile(string source, ExpressionScope scope)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Length > MaxSourceLength) throw new ExpressionException($"Program çok uzun ({source.Length} karakter; en fazla {MaxSourceLength}).", 0, 1, 1);
            var own = scope.Clone();
            var parser = new Parser(source, own);
            var statements = parser.ParseProgram();
            return new ExpressionProgram(source, own, statements, parser.Assigned, parser.ReadNames);
        }

        /// <summary>Compiles a single expression (no assignment) to a function of the scope's slots.</summary>
        public static Func<double[], double> CompileExpression(string source, ExpressionScope scope)
        {
            if (source.Length > MaxSourceLength) throw new ExpressionException("İfade çok uzun.", 0, 1, 1);
            var parser = new Parser(source, scope.Clone());
            return parser.ParseSingleExpression();
        }

        /// <summary>Runs every statement in order on <paramref name="slots"/> (from <see cref="Scope"/>.NewSlots()).</summary>
        public void Run(double[] slots)
        {
            for (int i = 0; i < _statements.Length; i++) _statements[i](slots);
        }

        /// <summary>Evaluates a stand-alone expression with optional variables (convenience; compiles every call).</summary>
        public static double Evaluate(string expression, IReadOnlyDictionary<string, double>? variables = null)
        {
            var scope = new ExpressionScope();
            if (variables != null) foreach (var kv in variables) scope.Declare(kv.Key);
            var f = CompileExpression(expression, scope);
            var slots = new double[Math.Max(1, scope.Count)];
            if (variables != null) foreach (var kv in variables) slots[scope[kv.Key]] = kv.Value;
            return f(slots);
        }

        /// <summary>Names of the built-in functions with their argument counts (for completion and documentation).</summary>
        public static IReadOnlyList<(string Name, string Signature, string Description)> Functions => Builtins.Help;

        // ============================================================== lexer

        private enum Tok { Number, Name, Op, Separator, End }

        private readonly struct Token
        {
            public Token(Tok kind, string text, double value, int pos)
            {
                Kind = kind;
                Text = text;
                Value = value;
                Pos = pos;
            }

            public Tok Kind { get; }
            public string Text { get; }
            public double Value { get; }
            public int Pos { get; }
            public override string ToString() => Kind == Tok.End ? "satır sonu" : Kind == Tok.Separator ? "';'" : "'" + Text + "'";
        }

        private static List<Token> Lex(string s, Func<string, int, ExpressionException> error)
        {
            var list = new List<Token>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\n' || c == ';')
                {
                    list.Add(new Token(Tok.Separator, c.ToString(), 0, i));
                    i++;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                if (c == '#' || (c == '/' && i + 1 < s.Length && s[i + 1] == '/'))
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }

                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    int start = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                    if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                    {
                        int save = i;
                        i++;
                        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                        if (i < s.Length && char.IsDigit(s[i]))
                        {
                            while (i < s.Length && char.IsDigit(s[i])) i++;
                        }
                        else i = save;
                    }

                    string text = s.Substring(start, i - start);
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) throw error($"Geçersiz sayı '{text}'.", start);
                    if (i < s.Length && (char.IsLetter(s[i]) || s[i] == '_')) throw error($"Sayıdan sonra '{s[i]}' beklenmiyordu (çarpım için '*' yazın: 2*x).", i);
                    list.Add(new Token(Tok.Number, text, v, start));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || (s[i] == '.' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] == '_')))) i++;
                    list.Add(new Token(Tok.Name, s.Substring(start, i - start), 0, start));
                    continue;
                }

                string two = i + 1 < s.Length ? s.Substring(i, 2) : string.Empty;
                if (two == "<=" || two == ">=" || two == "==" || two == "!=" || two == "&&" || two == "||" || two == "**")
                {
                    list.Add(new Token(Tok.Op, two == "**" ? "^" : two, 0, i));
                    i += 2;
                    continue;
                }

                if ("+-*/%^()<>!?:,=".IndexOf(c) >= 0)
                {
                    list.Add(new Token(Tok.Op, c.ToString(), 0, i));
                    i++;
                    continue;
                }

                throw error($"Beklenmeyen karakter '{c}'.", i);
            }

            list.Add(new Token(Tok.End, string.Empty, 0, s.Length));
            return list;
        }

        // ============================================================== parser → closures

        private delegate double Node(double[] slots);

        private sealed class Parser
        {
            private readonly string _src;
            private readonly ExpressionScope _scope;
            private readonly List<Token> _t;
            private readonly HashSet<string> _assignedSoFar = new HashSet<string>(StringComparer.Ordinal);
            private int _p;
            private int _depth;
            private int _nodes;

            public Parser(string source, ExpressionScope scope)
            {
                _src = source;
                _scope = scope;
                _t = Lex(source, Error);
            }

            public List<string> Assigned { get; } = new List<string>();
            public List<string> ReadNames { get; } = new List<string>();

            private ExpressionException Error(string message, int pos)
            {
                int line = 1, col = 1;
                for (int i = 0; i < pos && i < _src.Length; i++)
                {
                    if (_src[i] == '\n')
                    {
                        line++;
                        col = 1;
                    }
                    else col++;
                }

                return new ExpressionException(message, pos, line, col);
            }

            private Token Peek => _t[_p];

            private bool IsOp(string op) => Peek.Kind == Tok.Op && Peek.Text == op;

            private void Expect(string op)
            {
                if (!IsOp(op)) throw Error($"'{op}' bekleniyordu, {Peek} bulundu.", Peek.Pos);
                _p++;
            }

            public Action<double[]>[] ParseProgram()
            {
                var list = new List<Action<double[]>>();
                while (true)
                {
                    while (Peek.Kind == Tok.Separator) _p++;
                    if (Peek.Kind == Tok.End) break;
                    list.Add(ParseStatement());
                    if (Peek.Kind != Tok.Separator && Peek.Kind != Tok.End) throw Error($"İfade bitmeliydi; {Peek} beklenmiyordu (iki ifadeyi ';' ile ayırın).", Peek.Pos);
                }

                if (list.Count == 0) throw Error("Program boş.", 0);
                return list.ToArray();
            }

            public Func<double[], double> ParseSingleExpression()
            {
                while (Peek.Kind == Tok.Separator) _p++;
                if (Peek.Kind == Tok.End) throw Error("İfade boş.", 0);
                var n = ParseExpr();
                while (Peek.Kind == Tok.Separator) _p++;
                if (Peek.Kind != Tok.End) throw Error($"İfade bitmeliydi; {Peek} beklenmiyordu.", Peek.Pos);
                return s => n(s);
            }

            private Action<double[]> ParseStatement()
            {
                if (Peek.Kind == Tok.Name && _t[_p + 1].Kind == Tok.Op && _t[_p + 1].Text == "=")
                {
                    var target = Peek;
                    _p += 2;
                    string name = _scope.Canonical(target.Text);
                    if (Builtins.IsConstant(name)) throw Error($"'{name}' bir sabittir, değer atanamaz.", target.Pos);
                    if (Builtins.IsFunction(name)) throw Error($"'{name}' bir fonksiyon adıdır, değişken olarak kullanılamaz.", target.Pos);
                    int slot;
                    if (_scope.TryGet(name, out slot))
                    {
                        if (!_scope.IsWritable(name) && !_assignedSoFar.Contains(name)) throw Error($"'{name}' salt okunur bir değişkendir.", target.Pos);
                    }
                    else
                    {
                        if (name.Contains('.')) throw Error($"Bilinmeyen çıktı '{name}'. Yazılabilenler: {string.Join(", ", WritableNames())}.", target.Pos);
                        slot = _scope.Declare(name, writable: true);
                    }

                    var value = ParseExpr();
                    if (_assignedSoFar.Add(name)) Assigned.Add(name);
                    int k = slot;
                    return s => s[k] = value(s);
                }

                var expr = ParseExpr();
                // A bare expression in a program is allowed when the scope has a single implicit output ("value").
                if (_scope.TryGet("value", out int v) && _scope.IsWritable("value"))
                {
                    if (_assignedSoFar.Add("value")) Assigned.Add("value");
                    return s => s[v] = expr(s);
                }

                throw Error("Atama bekleniyordu (ör. F.x = -k*x).", _t[Math.Max(0, _p - 1)].Pos);
            }

            private IEnumerable<string> WritableNames() => _scope.Names.Where(_scope.IsWritable).Where(n => n.Contains('.')).Take(12);

            private Node Count(Node n)
            {
                if (++_nodes > MaxNodes) throw Error($"İfade çok karmaşık (en fazla {MaxNodes} düğüm).", Peek.Pos);
                return n;
            }

            private Node ParseExpr()
            {
                if (++_depth > MaxDepth) throw Error($"İç içe geçme çok derin (en fazla {MaxDepth}).", Peek.Pos);
                try
                {
                    return ParseTernary();
                }
                finally
                {
                    _depth--;
                }
            }

            private Node ParseTernary()
            {
                var c = ParseOr();
                if (!IsOp("?")) return c;
                _p++;
                var a = ParseExpr();
                Expect(":");
                var b = ParseExpr();
                return Count(s => c(s) != 0 ? a(s) : b(s));
            }

            private Node ParseOr()
            {
                var l = ParseAnd();
                while (IsOp("||"))
                {
                    _p++;
                    var a = l;
                    var r = ParseAnd();
                    l = Count(s => a(s) != 0 || r(s) != 0 ? 1.0 : 0.0);
                }

                return l;
            }

            private Node ParseAnd()
            {
                var l = ParseCompare();
                while (IsOp("&&"))
                {
                    _p++;
                    var a = l;
                    var r = ParseCompare();
                    l = Count(s => a(s) != 0 && r(s) != 0 ? 1.0 : 0.0);
                }

                return l;
            }

            private Node ParseCompare()
            {
                var l = ParseAdd();
                while (Peek.Kind == Tok.Op && (Peek.Text == "<" || Peek.Text == ">" || Peek.Text == "<=" || Peek.Text == ">=" || Peek.Text == "==" || Peek.Text == "!="))
                {
                    string op = Peek.Text;
                    _p++;
                    var a = l;
                    var r = ParseAdd();
                    l = op switch
                    {
                        "<" => Count(s => a(s) < r(s) ? 1.0 : 0.0),
                        ">" => Count(s => a(s) > r(s) ? 1.0 : 0.0),
                        "<=" => Count(s => a(s) <= r(s) ? 1.0 : 0.0),
                        ">=" => Count(s => a(s) >= r(s) ? 1.0 : 0.0),
                        "==" => Count(s => a(s) == r(s) ? 1.0 : 0.0),
                        _ => Count(s => a(s) != r(s) ? 1.0 : 0.0)
                    };
                }

                return l;
            }

            private Node ParseAdd()
            {
                var l = ParseMul();
                while (IsOp("+") || IsOp("-"))
                {
                    bool plus = Peek.Text == "+";
                    _p++;
                    var a = l;
                    var r = ParseMul();
                    l = plus ? Count(s => a(s) + r(s)) : Count(s => a(s) - r(s));
                }

                return l;
            }

            private Node ParseMul()
            {
                var l = ParseUnary();
                while (IsOp("*") || IsOp("/") || IsOp("%"))
                {
                    string op = Peek.Text;
                    _p++;
                    var a = l;
                    var r = ParseUnary();
                    l = op switch
                    {
                        "*" => Count(s => a(s) * r(s)),
                        "/" => Count(s => a(s) / r(s)),
                        _ => Count(s => Builtins.Mod(a(s), r(s)))
                    };
                }

                return l;
            }

            private Node ParseUnary()
            {
                if (IsOp("-"))
                {
                    _p++;
                    var a = ParseUnary();
                    return Count(s => -a(s));
                }

                if (IsOp("+"))
                {
                    _p++;
                    return ParseUnary();
                }

                if (IsOp("!"))
                {
                    _p++;
                    var a = ParseUnary();
                    return Count(s => a(s) == 0 ? 1.0 : 0.0);
                }

                return ParsePower();
            }

            private Node ParsePower()
            {
                var b = ParsePrimary();
                if (!IsOp("^")) return b;
                _p++;
                if (++_depth > MaxDepth) throw Error("İç içe geçme çok derin.", Peek.Pos);
                var e = ParseUnary();   // right-associative: 2^3^2 = 2^9; 2^-1 = 0.5
                _depth--;
                return Count(s => Math.Pow(b(s), e(s)));
            }

            private Node ParsePrimary()
            {
                var tok = Peek;
                switch (tok.Kind)
                {
                    case Tok.Number:
                    {
                        _p++;
                        double v = tok.Value;
                        return Count(_ => v);
                    }

                    case Tok.Name:
                    {
                        _p++;
                        if (IsOp("("))
                        {
                            _p++;
                            var args = new List<Node>();
                            if (!IsOp(")"))
                            {
                                while (true)
                                {
                                    args.Add(ParseExpr());
                                    if (IsOp(",")) { _p++; continue; }
                                    break;
                                }
                            }

                            Expect(")");
                            return Count(Builtins.Call(tok.Text, args.ToArray(), m => Error(m, tok.Pos)));
                        }

                        if (Builtins.TryConstant(tok.Text, out double c) && !_scope.Contains(tok.Text)) return Count(_ => c);
                        string name = _scope.Canonical(tok.Text);
                        if (_scope.TryGet(name, out int slot))
                        {
                            if (!ReadNames.Contains(name)) ReadNames.Add(name);
                            int k = slot;
                            return Count(s => s[k]);
                        }

                        if (Builtins.IsFunction(tok.Text)) throw Error($"'{tok.Text}' bir fonksiyondur; parantezle çağırın: {tok.Text}(…).", tok.Pos);
                        string hint = Suggest(tok.Text);
                        throw Error($"Bilinmeyen değişken '{tok.Text}'." + (hint.Length > 0 ? $" Bunu mu demek istediniz: {hint}?" : string.Empty), tok.Pos);
                    }

                    case Tok.Op when tok.Text == "(":
                    {
                        _p++;
                        var e = ParseExpr();
                        Expect(")");
                        return e;
                    }

                    case Tok.End:
                    case Tok.Separator:
                        throw Error("İfade eksik: bir sayı, değişken veya '(' bekleniyordu.", tok.Pos);
                    default:
                        throw Error($"{tok} beklenmiyordu.", tok.Pos);
                }
            }

            private string Suggest(string name)
            {
                string best = string.Empty;
                int bestD = 3;
                foreach (var n in _scope.Names.Concat(Builtins.ConstantNames))
                {
                    int d = Distance(name, n);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = n;
                    }
                }

                return best;
            }

            private static int Distance(string a, string b)
            {
                if (Math.Abs(a.Length - b.Length) > 2) return 99;
                var d = new int[a.Length + 1, b.Length + 1];
                for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
                for (int j = 0; j <= b.Length; j++) d[0, j] = j;
                for (int i = 1; i <= a.Length; i++)
                {
                    for (int j = 1; j <= b.Length; j++)
                    {
                        int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                        d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    }
                }

                return d[a.Length, b.Length];
            }
        }

        // ============================================================== built-ins

        private static class Builtins
        {
            private static readonly Dictionary<string, double> Constants = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["pi"] = Math.PI,
                ["tau"] = 2 * Math.PI,
                ["e"] = Math.E,
                ["g0"] = 9.80665,
                ["true"] = 1,
                ["false"] = 0
            };

            public static readonly (string Name, string Signature, string Description)[] Help =
            {
                ("sin", "sin(x)", "sinüs (radyan)"), ("cos", "cos(x)", "kosinüs"), ("tan", "tan(x)", "tanjant"),
                ("asin", "asin(x)", "ters sinüs"), ("acos", "acos(x)", "ters kosinüs"), ("atan", "atan(x)", "ters tanjant"),
                ("atan2", "atan2(y, x)", "dört bölgeli açı"), ("sinh", "sinh(x)", "hiperbolik sinüs"), ("cosh", "cosh(x)", "hiperbolik kosinüs"),
                ("tanh", "tanh(x)", "hiperbolik tanjant"), ("sqrt", "sqrt(x)", "karekök"), ("cbrt", "cbrt(x)", "küpkök"),
                ("abs", "abs(x)", "mutlak değer"), ("exp", "exp(x)", "e üzeri x"), ("ln", "ln(x)", "doğal logaritma"), ("log", "log(x)", "doğal logaritma"),
                ("log10", "log10(x)", "10 tabanlı logaritma"), ("log2", "log2(x)", "2 tabanlı logaritma"), ("pow", "pow(a, b)", "a üzeri b"),
                ("min", "min(a, b, …)", "en küçük"), ("max", "max(a, b, …)", "en büyük"), ("clamp", "clamp(x, alt, üst)", "aralığa sınırla"),
                ("sign", "sign(x)", "işaret (−1, 0, 1)"), ("floor", "floor(x)", "aşağı yuvarla"), ("ceil", "ceil(x)", "yukarı yuvarla"),
                ("round", "round(x)", "en yakın tamsayı"), ("frac", "frac(x)", "kesir kısmı"), ("hypot", "hypot(a, b[, c])", "vektör uzunluğu"),
                ("lerp", "lerp(a, b, s)", "doğrusal ara değer"), ("step", "step(kenar, x)", "x ≥ kenar ise 1"), ("smoothstep", "smoothstep(a, b, x)", "yumuşak geçiş 0→1"),
                ("deg", "deg(rad)", "radyandan dereceye"), ("rad", "rad(derece)", "dereceden radyana"), ("if", "if(koşul, a, b)", "koşul ≠ 0 ise a, değilse b"),
                ("noise", "noise(x[, tohum])", "tekrarlanabilir pürüzsüz gürültü, −1…1"), ("hash", "hash(x[, tohum])", "tekrarlanabilir rastgele sayı, 0…1"),
                ("square", "square(t, periyot)", "kare dalga ±1"), ("saw", "saw(t, periyot)", "testere dişi dalga −1…1"), ("pulse", "pulse(t, başlangıç, süre)", "aralıkta 1, dışında 0")
            };

            private static readonly HashSet<string> FunctionNames = new HashSet<string>(Help.Select(h => h.Name), StringComparer.Ordinal);

            public static IEnumerable<string> ConstantNames => Constants.Keys;

            public static bool IsFunction(string name) => FunctionNames.Contains(name);

            public static bool IsConstant(string name) => Constants.ContainsKey(name);

            public static bool TryConstant(string name, out double v) => Constants.TryGetValue(name, out v);

            public static double Mod(double a, double b) => b == 0 ? double.NaN : a - b * Math.Floor(a / b);

            public static Node Call(string name, Node[] a, Func<string, ExpressionException> error)
            {
                void Arity(int min, int max)
                {
                    if (a.Length < min || a.Length > max)
                    {
                        string want = min == max ? min.ToString(CultureInfo.InvariantCulture) : $"{min}–{(max == int.MaxValue ? "∞" : max.ToString(CultureInfo.InvariantCulture))}";
                        throw error($"'{name}' {want} bağımsız değişken alır, {a.Length} verildi.");
                    }
                }

                Node F1(Func<double, double> f)
                {
                    Arity(1, 1);
                    var x = a[0];
                    return s => f(x(s));
                }

                Node F2(Func<double, double, double> f)
                {
                    Arity(2, 2);
                    var x = a[0];
                    var y = a[1];
                    return s => f(x(s), y(s));
                }

                switch (name)
                {
                    case "sin": return F1(Math.Sin);
                    case "cos": return F1(Math.Cos);
                    case "tan": return F1(Math.Tan);
                    case "asin": return F1(Math.Asin);
                    case "acos": return F1(Math.Acos);
                    case "atan": return F1(Math.Atan);
                    case "atan2": return F2(Math.Atan2);
                    case "sinh": return F1(Math.Sinh);
                    case "cosh": return F1(Math.Cosh);
                    case "tanh": return F1(Math.Tanh);
                    case "sqrt": return F1(Math.Sqrt);
                    case "cbrt": return F1(x => x < 0 ? -Math.Pow(-x, 1.0 / 3) : Math.Pow(x, 1.0 / 3));
                    case "abs": return F1(Math.Abs);
                    case "exp": return F1(Math.Exp);
                    case "ln":
                    case "log": return F1(Math.Log);
                    case "log10": return F1(Math.Log10);
                    case "log2": return F1(x => Math.Log(x) / Math.Log(2));
                    case "pow": return F2(Math.Pow);
                    case "sign": return F1(x => double.IsNaN(x) ? double.NaN : Math.Sign(x));
                    case "floor": return F1(Math.Floor);
                    case "ceil": return F1(Math.Ceiling);
                    case "round": return F1(x => Math.Round(x, MidpointRounding.AwayFromZero));
                    case "frac": return F1(x => x - Math.Floor(x));
                    case "deg": return F1(x => x * 180 / Math.PI);
                    case "rad": return F1(x => x * Math.PI / 180);
                    case "step": return F2((edge, x) => x >= edge ? 1.0 : 0.0);
                    case "square": return F2((t, p) => p <= 0 ? double.NaN : Mod(t, p) < p / 2 ? 1.0 : -1.0);
                    case "saw": return F2((t, p) => p <= 0 ? double.NaN : 2 * Mod(t, p) / p - 1);
                    case "min":
                    case "max":
                    {
                        Arity(1, int.MaxValue);
                        var args = a;
                        bool isMin = name == "min";
                        return s =>
                        {
                            double r = args[0](s);
                            for (int i = 1; i < args.Length; i++)
                            {
                                double v = args[i](s);
                                if (double.IsNaN(v)) return double.NaN;
                                r = isMin ? Math.Min(r, v) : Math.Max(r, v);
                            }

                            return r;
                        };
                    }

                    case "clamp":
                    {
                        Arity(3, 3);
                        var x = a[0];
                        var lo = a[1];
                        var hi = a[2];
                        return s => Math.Max(lo(s), Math.Min(hi(s), x(s)));
                    }

                    case "lerp":
                    {
                        Arity(3, 3);
                        var x = a[0];
                        var y = a[1];
                        var t = a[2];
                        return s =>
                        {
                            double av = x(s);
                            return av + (y(s) - av) * t(s);
                        };
                    }

                    case "smoothstep":
                    {
                        Arity(3, 3);
                        var e0 = a[0];
                        var e1 = a[1];
                        var x = a[2];
                        return s =>
                        {
                            double l = e0(s), h = e1(s);
                            double u = h == l ? (x(s) >= h ? 1 : 0) : Math.Max(0, Math.Min(1, (x(s) - l) / (h - l)));
                            return u * u * (3 - 2 * u);
                        };
                    }

                    case "hypot":
                    {
                        Arity(2, 3);
                        var x = a[0];
                        var y = a[1];
                        var z = a.Length > 2 ? a[2] : null;
                        return z == null ? s => Math.Sqrt(x(s) * x(s) + y(s) * y(s)) : s =>
                        {
                            double xv = x(s), yv = y(s), zv = z(s);
                            return Math.Sqrt(xv * xv + yv * yv + zv * zv);
                        };
                    }

                    case "if":
                    {
                        Arity(3, 3);
                        var c = a[0];
                        var x = a[1];
                        var y = a[2];
                        return s => c(s) != 0 ? x(s) : y(s);
                    }

                    case "pulse":
                    {
                        Arity(3, 3);
                        var t = a[0];
                        var start = a[1];
                        var len = a[2];
                        return s =>
                        {
                            double tv = t(s), st = start(s);
                            return tv >= st && tv < st + len(s) ? 1.0 : 0.0;
                        };
                    }

                    case "noise":
                    {
                        Arity(1, 2);
                        var x = a[0];
                        var seed = a.Length > 1 ? a[1] : null;
                        return s => SdkRandom.ValueNoise(x(s), seed == null ? 0 : (long)seed(s));
                    }

                    case "hash":
                    {
                        Arity(1, 2);
                        var x = a[0];
                        var seed = a.Length > 1 ? a[1] : null;
                        return s => SdkRandom.Hash01(x(s), seed == null ? 0 : (long)seed(s));
                    }
                }

                throw error($"Bilinmeyen fonksiyon '{name}'.");
            }
        }
    }

    /// <summary>Helpers for describing the variables of a scope in the UI.</summary>
    public static class ExpressionHelp
    {
        public static string FunctionList() =>
            string.Join(", ", ExpressionProgram.Functions.Select(f => f.Signature));

        /// <summary>Formats an exception for the UI, with a caret under the error position.</summary>
        public static string Describe(ExpressionException ex, string source)
        {
            var lines = source.Split('\n');
            if (ex.Line < 1 || ex.Line > lines.Length) return ex.Message;
            var sb = new StringBuilder();
            sb.AppendLine(ex.Message);
            sb.AppendLine(lines[ex.Line - 1].TrimEnd('\r'));
            sb.Append(new string(' ', Math.Max(0, ex.Column - 1))).Append('^');
            return sb.ToString();
        }
    }
}
