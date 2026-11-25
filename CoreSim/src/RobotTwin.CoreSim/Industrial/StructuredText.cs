using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Industrial
{
    /// <summary>[plc] A type of IEC 61131-3 Structured Text, as this interpreter keeps it.</summary>
    public enum StType
    {
        Bool,
        Int,
        Real,
        Time,
        Block
    }

    /// <summary>[plc] A compile error in a Structured Text program: where (line, 1-based) and why (in Turkish, as the editor shows it).</summary>
    public sealed class StError
    {
        public StError(int line, string message)
        {
            Line = line;
            Message = message;
        }

        public int Line { get; }

        public string Message { get; }

        public override string ToString() => $"{Line}. satır: {Message}";
    }

    /// <summary>
    /// [plc] A Structured Text program (IEC 61131-3) for the world's PLC: compiled once into a tree, run once per scan.
    /// Variables of BOOL, INT/DINT/UINT/WORD, REAL/LREAL and TIME, the standard function blocks (TON, TOF, TP, R_TRIG,
    /// F_TRIG, CTU, CTD, CTUD, SR, RS), IF/ELSIF/ELSE, CASE (a station's state machine), FOR, WHILE, REPEAT, EXIT, and
    /// directly addressed inputs and outputs (<c>%IX0.0</c>, <c>%QX0.3</c>, <c>%MW2</c>) or names bound to them with AT.
    /// Integer types wrap as the PLC's do (INT is 16-bit signed); division by zero and a loop that does not end are run-time
    /// faults, as a real PLC stops on them.
    /// </summary>
    public sealed class StProgram
    {
        private readonly List<Stmt> _body;
        private readonly Dictionary<string, Var> _vars;
        private long _loopBudget;

        private StProgram(string name, Dictionary<string, Var> vars, List<Stmt> body)
        {
            Name = name;
            _vars = vars;
            _body = body;
        }

        public string Name { get; }

        /// <summary>The process image: inputs (%IX byte.bit), outputs (%QX), and memory words (%MW).</summary>
        public bool[] Inputs { get; } = new bool[64];

        public bool[] Outputs { get; } = new bool[64];

        public short[] Memory { get; } = new short[256];

        /// <summary>The program's time (ms), advanced by each scan.</summary>
        public double Clock { get; private set; }

        /// <summary>Why the last scan stopped (a run-time fault), or null.</summary>
        public string? Fault { get; private set; }

        public long Scans { get; private set; }

        /// <summary>The variables by name (upper case), for watching: their type and value as text.</summary>
        public IEnumerable<(string Name, StType Type, string Value)> Watch =>
            _vars.Values.Where(v => v.Type != StType.Block).Select(v => (v.Name, v.Type, Format(v.Type, v.Value)));

        public double Read(string name)
        {
            string key = name.Trim().ToUpperInvariant();
            int dot = key.IndexOf('.');
            if (dot > 0 && !key.StartsWith("%", StringComparison.Ordinal))
                return _vars.TryGetValue(key.Substring(0, dot), out var b) && b.Block != null ? b.Block[key.Substring(dot + 1)] : double.NaN;   // a block's member: T1.Q
            return _vars.TryGetValue(key, out var v) ? v.Value : double.NaN;
        }

        /// <summary>Compiles <paramref name="source"/>: the program, or null with the errors.</summary>
        public static StProgram? Compile(string source, out List<StError> errors)
        {
            errors = new List<StError>();
            try
            {
                var tokens = Lexer.Read(source);
                var parser = new Parser(tokens);
                var program = parser.Program();
                return program;
            }
            catch (StCompileException ex)
            {
                errors.Add(new StError(ex.Line, ex.Message));
                return null;
            }
        }

        /// <summary>
        /// One scan: <paramref name="elapsedMs"/> since the last one (the timers' time), the program run once over the process
        /// image. False when it faulted (the outputs then stay as they were, as a PLC in STOP keeps or clears them by setting).
        /// </summary>
        public bool Scan(double elapsedMs)
        {
            if (Fault != null) return false;
            Clock += Math.Max(0, elapsedMs);
            Scans++;
            _loopBudget = 1_000_000;
            try
            {
                foreach (var v in _vars.Values) v.Load(this);
                Run(_body);
                foreach (var v in _vars.Values) v.Store(this);
                return true;
            }
            catch (StRunException ex)
            {
                Fault = ex.Message;
                return false;
            }
        }

        /// <summary>Clears a fault and every variable back to its initial value (a cold restart).</summary>
        public void Restart()
        {
            Fault = null;
            Clock = 0;
            Scans = 0;
            foreach (var v in _vars.Values) v.Reset();
            Array.Clear(Outputs, 0, Outputs.Length);
        }

        // ================================================================ running

        private enum Flow
        {
            Next,
            Exit,
            Return
        }

        private Flow Run(List<Stmt> body)
        {
            foreach (var s in body)
            {
                var f = s.Run(this);
                if (f != Flow.Next) return f;
            }

            return Flow.Next;
        }

        private void Spend()
        {
            if (--_loopBudget < 0) throw new StRunException("Döngü bitmiyor (tarama süresi aşıldı).");
        }

        internal static string Format(StType t, double v) => t switch
        {
            StType.Bool => v != 0 ? "TRUE" : "FALSE",
            StType.Time => "T#" + v.ToString("0.###", CultureInfo.InvariantCulture) + "ms",
            StType.Real => v.ToString("0.######", CultureInfo.InvariantCulture),
            _ => ((long)v).ToString(CultureInfo.InvariantCulture)
        };

        // ================================================================ variables

        private sealed class Var
        {
            public string Name = string.Empty;
            public StType Type;
            public string TypeName = string.Empty;   // INT, UINT, WORD … for wrapping; TON … for blocks
            public double Value, Initial;
            public char Area;   // 'I', 'Q', 'M' or '\0'
            public int Address;   // bit index for I/Q, word index for M
            public Block? Block;

            public void Load(StProgram p)
            {
                switch (Area)
                {
                    case 'I':
                        Value = p.Inputs[Address] ? 1 : 0;
                        break;
                    case 'Q':
                        Value = p.Outputs[Address] ? 1 : 0;
                        break;
                    case 'M':
                        Value = p.Memory[Address];
                        break;
                }
            }

            public void Store(StProgram p)
            {
                switch (Area)
                {
                    case 'Q':
                        p.Outputs[Address] = Value != 0;
                        break;
                    case 'M':
                        p.Memory[Address] = (short)Wrap("INT", Value);
                        break;
                }
            }

            public void Reset()
            {
                Value = Initial;
                Block?.Reset();
            }

            public void Set(double v)
            {
                Value = Type switch
                {
                    StType.Bool => v != 0 ? 1 : 0,
                    StType.Int => Wrap(TypeName, Math.Truncate(v)),
                    _ => v
                };
            }
        }

        internal static double Wrap(string type, double v)
        {
            long x = (long)v;
            return type switch
            {
                "SINT" => (sbyte)x,
                "INT" => (short)x,
                "DINT" => (int)x,
                "USINT" or "BYTE" => (byte)x,
                "UINT" or "WORD" => (ushort)x,
                "UDINT" or "DWORD" => (uint)x,
                _ => x
            };
        }

        // ================================================================ function blocks

        private abstract class Block
        {
            public readonly Dictionary<string, double> Members = new Dictionary<string, double>(StringComparer.Ordinal);

            public abstract IReadOnlyCollection<string> Inputs { get; }

            public abstract IReadOnlyCollection<string> Outputs { get; }

            public double this[string m]
            {
                get => Members.TryGetValue(m, out var v) ? v : 0;
                set => Members[m] = value;
            }

            public abstract void Call(double clock);

            public virtual void Reset() => Members.Clear();

            public static Block? Make(string type) => type switch
            {
                "TON" => new Timer(Timer.Kind.On),
                "TOF" => new Timer(Timer.Kind.Off),
                "TP" => new Timer(Timer.Kind.Pulse),
                "R_TRIG" => new Edge(true),
                "F_TRIG" => new Edge(false),
                "CTU" => new Counter(true, false),
                "CTD" => new Counter(false, true),
                "CTUD" => new Counter(true, true),
                "SR" => new Bistable(true),
                "RS" => new Bistable(false),
                _ => null
            };
        }

        /// <summary>TON, TOF, TP: IN, PT → Q, ET (ms), on the program's clock.</summary>
        private sealed class Timer : Block
        {
            public enum Kind
            {
                On,
                Off,
                Pulse
            }

            private static readonly string[] In = { "IN", "PT" }, Out = { "Q", "ET" };
            private readonly Kind _kind;
            private double _start = double.NaN;
            private bool _last;

            public Timer(Kind kind) => _kind = kind;

            public override IReadOnlyCollection<string> Inputs => In;

            public override IReadOnlyCollection<string> Outputs => Out;

            public override void Call(double clock)
            {
                bool input = this["IN"] != 0;
                double pt = Math.Max(0, this["PT"]);
                switch (_kind)
                {
                    case Kind.On:
                        if (!input)
                        {
                            _start = double.NaN;
                            this["Q"] = 0;
                            this["ET"] = 0;
                        }
                        else
                        {
                            if (double.IsNaN(_start)) _start = clock;
                            double et = Math.Min(pt, clock - _start);
                            this["ET"] = et;
                            this["Q"] = et >= pt ? 1 : 0;
                        }

                        break;
                    case Kind.Off:
                        if (input)
                        {
                            _start = double.NaN;
                            this["Q"] = 1;
                            this["ET"] = 0;
                        }
                        else if (this["Q"] != 0)
                        {
                            if (double.IsNaN(_start)) _start = clock;
                            double et = Math.Min(pt, clock - _start);
                            this["ET"] = et;
                            if (et >= pt) this["Q"] = 0;
                        }

                        break;
                    default:
                        if (input && !_last && double.IsNaN(_start)) _start = clock;
                        if (!double.IsNaN(_start))
                        {
                            double et = Math.Min(pt, clock - _start);
                            this["ET"] = et;
                            this["Q"] = et < pt ? 1 : 0;
                            if (et >= pt && !input)
                            {
                                _start = double.NaN;
                                this["ET"] = 0;
                            }
                        }
                        else
                        {
                            this["Q"] = 0;
                        }

                        break;
                }

                _last = input;
            }

            public override void Reset()
            {
                base.Reset();
                _start = double.NaN;
                _last = false;
            }
        }

        /// <summary>R_TRIG, F_TRIG: CLK → Q for one scan on the edge.</summary>
        private sealed class Edge : Block
        {
            private static readonly string[] In = { "CLK" }, Out = { "Q" };
            private readonly bool _rising;
            private bool _last;

            public Edge(bool rising) => _rising = rising;

            public override IReadOnlyCollection<string> Inputs => In;

            public override IReadOnlyCollection<string> Outputs => Out;

            public override void Call(double clock)
            {
                bool clk = this["CLK"] != 0;
                this["Q"] = (_rising ? clk && !_last : !clk && _last) ? 1 : 0;
                _last = clk;
            }

            public override void Reset()
            {
                base.Reset();
                _last = false;
            }
        }

        /// <summary>CTU (CU, R, PV → Q, CV), CTD (CD, LD, PV → Q, CV), CTUD (both → QU, QD, CV): counting on rising edges.</summary>
        private sealed class Counter : Block
        {
            private static readonly string[] UpIn = { "CU", "R", "PV" }, DownIn = { "CD", "LD", "PV" }, BothIn = { "CU", "CD", "R", "LD", "PV" };
            private static readonly string[] UpOut = { "Q", "CV" }, BothOut = { "QU", "QD", "CV" };
            private readonly bool _up, _down;
            private bool _lastUp, _lastDown;

            public Counter(bool up, bool down)
            {
                _up = up;
                _down = down;
            }

            public override IReadOnlyCollection<string> Inputs => _up && _down ? BothIn : _up ? UpIn : DownIn;

            public override IReadOnlyCollection<string> Outputs => _up && _down ? BothOut : UpOut;

            public override void Call(double clock)
            {
                bool cu = this["CU"] != 0, cd = this["CD"] != 0;
                double cv = this["CV"], pv = this["PV"];
                if (_up && this["R"] != 0) cv = 0;
                else if (_down && this["LD"] != 0) cv = pv;
                else
                {
                    if (_up && cu && !_lastUp && cv < short.MaxValue) cv++;
                    if (_down && cd && !_lastDown && cv > short.MinValue) cv--;
                }

                _lastUp = cu;
                _lastDown = cd;
                this["CV"] = cv;
                if (_up && _down)
                {
                    this["QU"] = cv >= pv ? 1 : 0;
                    this["QD"] = cv <= 0 ? 1 : 0;
                }
                else
                {
                    this["Q"] = _up ? (cv >= pv ? 1 : 0) : (cv <= 0 ? 1 : 0);
                }
            }

            public override void Reset()
            {
                base.Reset();
                _lastUp = _lastDown = false;
            }
        }

        /// <summary>SR (set dominant: S1, R → Q1) and RS (reset dominant: S, R1 → Q1).</summary>
        private sealed class Bistable : Block
        {
            private static readonly string[] SrIn = { "S1", "R" }, RsIn = { "S", "R1" }, Out = { "Q1" };
            private readonly bool _setDominant;

            public Bistable(bool setDominant) => _setDominant = setDominant;

            public override IReadOnlyCollection<string> Inputs => _setDominant ? SrIn : RsIn;

            public override IReadOnlyCollection<string> Outputs => Out;

            public override void Call(double clock)
            {
                bool q = this["Q1"] != 0;
                q = _setDominant ? this["S1"] != 0 || (q && this["R"] == 0) : this["R1"] == 0 && (this["S"] != 0 || q);
                this["Q1"] = q ? 1 : 0;
            }
        }

        // ================================================================ tree

        private abstract class Expr
        {
            public int Line;
            public StType Type;

            public abstract double Eval(StProgram p);
        }

        private sealed class Const : Expr
        {
            public double Value;

            public override double Eval(StProgram p) => Value;
        }

        private sealed class Ref : Expr
        {
            public Var Var = null!;

            public override double Eval(StProgram p) => Var.Value;
        }

        private sealed class Member : Expr
        {
            public Var Var = null!;
            public string Name = string.Empty;

            public override double Eval(StProgram p) => Var.Block![Name];
        }

        private sealed class Unary : Expr
        {
            public string Op = string.Empty;
            public Expr A = null!;

            public override double Eval(StProgram p)
            {
                double a = A.Eval(p);
                return Op == "NOT" ? (Type == StType.Bool ? (a != 0 ? 0 : 1) : Wrap("DINT", ~(long)a)) : -a;
            }
        }

        private sealed class Binary : Expr
        {
            public string Op = string.Empty;
            public Expr A = null!, B = null!;

            public override double Eval(StProgram p)
            {
                double a = A.Eval(p);
                switch (Op)
                {
                    case "AND":
                        if (Type == StType.Bool) return a != 0 && B.Eval(p) != 0 ? 1 : 0;
                        return (long)a & (long)B.Eval(p);
                    case "OR":
                        if (Type == StType.Bool) return a != 0 || B.Eval(p) != 0 ? 1 : 0;
                        return (long)a | (long)B.Eval(p);
                }

                double b = B.Eval(p);
                bool integer = A.Type != StType.Real && B.Type != StType.Real;
                switch (Op)
                {
                    case "XOR":
                        return Type == StType.Bool ? ((a != 0) ^ (b != 0) ? 1 : 0) : (long)a ^ (long)b;
                    case "=":
                        return a == b ? 1 : 0;
                    case "<>":
                        return a != b ? 1 : 0;
                    case "<":
                        return a < b ? 1 : 0;
                    case "<=":
                        return a <= b ? 1 : 0;
                    case ">":
                        return a > b ? 1 : 0;
                    case ">=":
                        return a >= b ? 1 : 0;
                    case "+":
                        return a + b;
                    case "-":
                        return a - b;
                    case "*":
                        return a * b;
                    case "/":
                        if (integer)
                        {
                            if (b == 0) throw new StRunException($"{Line}. satırda sıfıra bölme.");
                            return Math.Truncate(a / b);
                        }

                        return a / b;
                    case "MOD":
                        if (b == 0) throw new StRunException($"{Line}. satırda sıfıra göre mod.");
                        return a % b;
                    case "**":
                        return Math.Pow(a, b);
                    default:
                        throw new StRunException("Bilinmeyen işlem " + Op);
                }
            }
        }

        private sealed class Call : Expr
        {
            public string Name = string.Empty;
            public List<Expr> Args = new List<Expr>();

            public override double Eval(StProgram p)
            {
                double A(int i) => Args[i].Eval(p);
                switch (Name)
                {
                    case "ABS": return Math.Abs(A(0));
                    case "SQRT": return Math.Sqrt(A(0));
                    case "SIN": return Math.Sin(A(0));
                    case "COS": return Math.Cos(A(0));
                    case "TAN": return Math.Tan(A(0));
                    case "ATAN": return Math.Atan(A(0));
                    case "EXP": return Math.Exp(A(0));
                    case "LN": return Math.Log(A(0));
                    case "TRUNC": return Math.Truncate(A(0));
                    case "MIN": return Args.Min(x => x.Eval(p));
                    case "MAX": return Args.Max(x => x.Eval(p));
                    case "LIMIT": return Math.Max(A(0), Math.Min(A(2), A(1)));
                    case "SEL": return A(0) != 0 ? A(2) : A(1);
                    default:
                        // Conversions: X_TO_Y; to an integer type it rounds, to BOOL it is "not zero".
                        if (Name.Contains("_TO_"))
                        {
                            string to = Name.Substring(Name.IndexOf("_TO_", StringComparison.Ordinal) + 4);
                            double v = A(0);
                            return to switch
                            {
                                "BOOL" => v != 0 ? 1 : 0,
                                "REAL" or "LREAL" or "TIME" => v,
                                _ => Wrap(to, Math.Round(v, MidpointRounding.ToEven))
                            };
                        }

                        throw new StRunException("Bilinmeyen işlev " + Name);
                }
            }
        }

        private abstract class Stmt
        {
            public int Line;

            public abstract Flow Run(StProgram p);
        }

        private sealed class Assign : Stmt
        {
            public Var Target = null!;
            public string? Member;
            public Expr Value = null!;

            public override Flow Run(StProgram p)
            {
                double v = Value.Eval(p);
                if (Member != null) Target.Block![Member] = v;
                else Target.Set(v);
                return Flow.Next;
            }
        }

        private sealed class BlockCall : Stmt
        {
            public Var Instance = null!;
            public List<(string Name, Expr Value)> Inputs = new List<(string, Expr)>();
            public List<(string Name, Var Target)> Outputs = new List<(string, Var)>();

            public override Flow Run(StProgram p)
            {
                var b = Instance.Block!;
                foreach (var (name, value) in Inputs) b[name] = value.Eval(p);
                b.Call(p.Clock);
                foreach (var (name, target) in Outputs) target.Set(b[name]);
                return Flow.Next;
            }
        }

        private sealed class If : Stmt
        {
            public List<(Expr Condition, List<Stmt> Body)> Branches = new List<(Expr, List<Stmt>)>();
            public List<Stmt>? Else;

            public override Flow Run(StProgram p)
            {
                foreach (var (c, body) in Branches)
                    if (c.Eval(p) != 0)
                        return p.Run(body);
                return Else != null ? p.Run(Else) : Flow.Next;
            }
        }

        private sealed class Case : Stmt
        {
            public Expr Selector = null!;
            public List<(List<(double Lo, double Hi)> Labels, List<Stmt> Body)> Arms = new List<(List<(double, double)>, List<Stmt>)>();
            public List<Stmt>? Else;

            public override Flow Run(StProgram p)
            {
                double s = Selector.Eval(p);
                foreach (var (labels, body) in Arms)
                    if (labels.Any(l => s >= l.Lo && s <= l.Hi))
                        return p.Run(body);
                return Else != null ? p.Run(Else) : Flow.Next;
            }
        }

        private sealed class For : Stmt
        {
            public Var Counter = null!;
            public Expr From = null!, To = null!;
            public Expr? By;
            public List<Stmt> Body = new List<Stmt>();

            public override Flow Run(StProgram p)
            {
                double step = By?.Eval(p) ?? 1, end = To.Eval(p);
                if (step == 0) throw new StRunException($"{Line}. satırda FOR adımı sıfır.");
                Counter.Set(From.Eval(p));
                while (step > 0 ? Counter.Value <= end : Counter.Value >= end)
                {
                    p.Spend();
                    var f = p.Run(Body);
                    if (f == Flow.Exit) break;
                    if (f == Flow.Return) return f;
                    Counter.Set(Counter.Value + step);
                }

                return Flow.Next;
            }
        }

        private sealed class While : Stmt
        {
            public Expr Condition = null!;
            public List<Stmt> Body = new List<Stmt>();
            public bool Repeat;   // REPEAT … UNTIL: the body first, until the condition holds

            public override Flow Run(StProgram p)
            {
                while (true)
                {
                    if (!Repeat && Condition.Eval(p) == 0) break;
                    p.Spend();
                    var f = p.Run(Body);
                    if (f == Flow.Exit) break;
                    if (f == Flow.Return) return f;
                    if (Repeat && Condition.Eval(p) != 0) break;
                }

                return Flow.Next;
            }
        }

        private sealed class Jump : Stmt
        {
            public Flow Kind;

            public override Flow Run(StProgram p) => Kind;
        }

        // ================================================================ lexer

        private enum Tok
        {
            Ident,
            Number,
            Time,
            Address,
            Symbol,
            End
        }

        private readonly struct Token
        {
            public Token(Tok kind, string text, int line, double value = 0)
            {
                Kind = kind;
                Text = text;
                Line = line;
                Value = value;
            }

            public Tok Kind { get; }
            public string Text { get; }
            public int Line { get; }
            public double Value { get; }

            public bool Is(string s) => (Kind == Tok.Ident || Kind == Tok.Symbol) && Text == s;
        }

        private static class Lexer
        {
            private static readonly string[] Symbols = { ":=", "=>", "<>", "<=", ">=", "**", "..", "(", ")", "[", "]", ",", ";", ":", ".", "=", "<", ">", "+", "-", "*", "/", "&", "#" };

            public static List<Token> Read(string source)
            {
                var list = new List<Token>();
                int i = 0, line = 1;
                string s = source ?? string.Empty;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '\n')
                    {
                        line++;
                        i++;
                        continue;
                    }

                    if (char.IsWhiteSpace(c))
                    {
                        i++;
                        continue;
                    }

                    if (c == '(' && i + 1 < s.Length && s[i + 1] == '*')
                    {
                        int end = s.IndexOf("*)", i + 2, StringComparison.Ordinal);
                        if (end < 0) throw new StCompileException(line, "Yorum (* … *) kapanmamış.");
                        line += s.Substring(i, end - i).Count(ch => ch == '\n');
                        i = end + 2;
                        continue;
                    }

                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }

                    if (c == '%')
                    {
                        int start = i++;
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '.')) i++;
                        list.Add(new Token(Tok.Address, s.Substring(start, i - start).ToUpperInvariant(), line));
                        continue;
                    }

                    if (char.IsLetter(c) || c == '_')
                    {
                        int start = i;
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                        string word = s.Substring(start, i - start).ToUpperInvariant();
                        if ((word == "T" || word == "TIME") && i < s.Length && s[i] == '#')
                        {
                            int t0 = ++i;
                            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '.')) i++;
                            list.Add(new Token(Tok.Time, s.Substring(t0, i - t0), line, Duration(s.Substring(t0, i - t0), line)));
                            continue;
                        }

                        if ((word == "TRUE" || word == "FALSE") && !(i < s.Length && s[i] == '#'))
                        {
                            list.Add(new Token(Tok.Ident, word, line));
                            continue;
                        }

                        if (i < s.Length && s[i] == '#' && word is "BOOL" or "INT" or "DINT" or "UINT" or "WORD" or "REAL" or "LREAL")
                        {
                            i++;   // a typed literal: INT#5 is 5
                            continue;
                        }

                        list.Add(new Token(Tok.Ident, word, line));
                        continue;
                    }

                    if (char.IsDigit(c))
                    {
                        int start = i;
                        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                        string digits = s.Substring(start, i - start).Replace("_", string.Empty);
                        if (i < s.Length && s[i] == '#')
                        {
                            int radix = int.Parse(digits, CultureInfo.InvariantCulture);
                            int v0 = ++i;
                            while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                            string body = s.Substring(v0, i - v0).Replace("_", string.Empty);
                            if (radix != 2 && radix != 8 && radix != 16) throw new StCompileException(line, $"{radix}# tabanı yok (2#, 8#, 16#).");
                            try
                            {
                                list.Add(new Token(Tok.Number, digits + "#" + body, line, Convert.ToInt64(body, radix)));
                            }
                            catch (FormatException)
                            {
                                throw new StCompileException(line, $"'{body}' {radix} tabanında bir sayı değil.");
                            }

                            continue;
                        }

                        if (i + 1 < s.Length && s[i] == '.' && char.IsDigit(s[i + 1]))
                        {
                            i++;
                            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '_')) i++;
                            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                            {
                                i++;
                                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                                while (i < s.Length && char.IsDigit(s[i])) i++;
                            }

                            string real = s.Substring(start, i - start).Replace("_", string.Empty);
                            list.Add(new Token(Tok.Number, real, line, double.Parse(real, CultureInfo.InvariantCulture)));
                            continue;
                        }

                        if (!long.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                            throw new StCompileException(line, $"'{digits}' bir sayı değil.");
                        list.Add(new Token(Tok.Number, digits, line, n));
                        continue;
                    }

                    string? sym = Symbols.FirstOrDefault(x => string.CompareOrdinal(s, i, x, 0, x.Length) == 0);
                    if (sym == null) throw new StCompileException(line, $"'{c}' burada beklenmiyor.");
                    list.Add(new Token(Tok.Symbol, sym, line));
                    i += sym.Length;
                }

                list.Add(new Token(Tok.End, string.Empty, line));
                return list;
            }

            /// <summary>"1s500ms", "2m", "250ms", "1.5s", "1h2m" → milliseconds.</summary>
            private static double Duration(string text, int line)
            {
                double total = 0;
                int i = 0;
                string t = text.Replace("_", string.Empty).ToLowerInvariant();
                bool any = false;
                while (i < t.Length)
                {
                    int start = i;
                    while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.')) i++;
                    if (start == i) throw new StCompileException(line, $"T#{text} bir süre değil (örnek T#1s500ms).");
                    double n = double.Parse(t.Substring(start, i - start), CultureInfo.InvariantCulture);
                    int u0 = i;
                    while (i < t.Length && char.IsLetter(t[i])) i++;
                    total += t.Substring(u0, i - u0) switch
                    {
                        "d" => n * 86_400_000,
                        "h" => n * 3_600_000,
                        "m" => n * 60_000,
                        "s" => n * 1000,
                        "ms" => n,
                        "us" => n / 1000,
                        _ => throw new StCompileException(line, $"T#{text}: birim d, h, m, s, ms ya da us olmalı.")
                    };
                    any = true;
                }

                if (!any) throw new StCompileException(line, $"T#{text} boş.");
                return total;
            }
        }

        // ================================================================ parser

        private sealed class Parser
        {
            private static readonly HashSet<string> Elementary = new HashSet<string>(StringComparer.Ordinal)
            {
                "BOOL", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD", "REAL", "LREAL", "TIME"
            };

            private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
            {
                "ABS", "SQRT", "SIN", "COS", "TAN", "ATAN", "EXP", "LN", "TRUNC", "MIN", "MAX", "LIMIT", "SEL"
            };

            private readonly List<Token> _t;
            private readonly Dictionary<string, Var> _vars = new Dictionary<string, Var>(StringComparer.Ordinal);
            private int _i;

            public Parser(List<Token> tokens) => _t = tokens;

            private Token Peek => _t[_i];

            private Token Next() => _t[_i++];

            private bool Accept(string s)
            {
                if (!Peek.Is(s)) return false;
                _i++;
                return true;
            }

            private Token Expect(string s, string what)
            {
                if (!Peek.Is(s)) throw new StCompileException(Peek.Line, $"{what} bekleniyordu, '{Shown(Peek)}' bulundu.");
                return Next();
            }

            private static string Shown(Token t) => t.Kind == Tok.End ? "programın sonu" : t.Text;

            public StProgram Program()
            {
                string name = "PLC";
                bool wrapped = Accept("PROGRAM");
                if (wrapped)
                {
                    if (Peek.Kind != Tok.Ident) throw new StCompileException(Peek.Line, "PROGRAM'dan sonra bir ad bekleniyordu.");
                    name = Next().Text;
                }

                while (Peek.Is("VAR") || Peek.Is("VAR_GLOBAL") || Peek.Is("VAR_INPUT") || Peek.Is("VAR_OUTPUT")) Declarations();
                var body = Statements(wrapped ? new[] { "END_PROGRAM" } : Array.Empty<string>());
                if (wrapped) Expect("END_PROGRAM", "END_PROGRAM");
                Accept(";");
                if (Peek.Kind != Tok.End) throw new StCompileException(Peek.Line, $"'{Shown(Peek)}' fazla.");
                return new StProgram(name, _vars, body);
            }

            private void Declarations()
            {
                Next();
                Accept("RETAIN");
                Accept("CONSTANT");
                while (!Accept("END_VAR"))
                {
                    if (Peek.Kind == Tok.End) throw new StCompileException(Peek.Line, "END_VAR eksik.");
                    var names = new List<Token> { Ident("değişken adı") };
                    while (Accept(",")) names.Add(Ident("değişken adı"));
                    string? at = null;
                    if (Accept("AT"))
                    {
                        if (Peek.Kind != Tok.Address) throw new StCompileException(Peek.Line, "AT'den sonra %IX0.0 gibi bir adres bekleniyordu.");
                        at = Next().Text;
                    }

                    Expect(":", "':' (tür)");
                    var typeTok = Ident("tür");
                    string type = typeTok.Text;
                    Expr? init = null;
                    if (Accept(":=")) init = Expression();
                    Expect(";", "';'");
                    foreach (var n in names)
                    {
                        if (_vars.ContainsKey(n.Text)) throw new StCompileException(n.Line, $"'{n.Text}' iki kez tanımlanmış.");
                        var v = new Var { Name = n.Text, TypeName = type };
                        if (Elementary.Contains(type))
                        {
                            v.Type = type == "BOOL" ? StType.Bool : type is "REAL" or "LREAL" ? StType.Real : type == "TIME" ? StType.Time : StType.Int;
                        }
                        else if (Block.Make(type) is { } b)
                        {
                            v.Type = StType.Block;
                            v.Block = b;
                        }
                        else
                        {
                            throw new StCompileException(typeTok.Line, $"'{type}' türü yok (BOOL, INT, REAL, TIME, TON, TOF, TP, R_TRIG, F_TRIG, CTU, CTD, CTUD, SR, RS).");
                        }

                        if (at != null) Bind(v, at, n.Line);
                        if (init != null)
                        {
                            if (v.Type == StType.Block) throw new StCompileException(n.Line, "Bir blok örneğine başlangıç değeri verilmez.");
                            v.Set(init.Eval(null!));
                            v.Initial = v.Value;
                        }

                        _vars[v.Name] = v;
                    }
                }
            }

            private static void Bind(Var v, string address, int line)
            {
                var (area, index) = Address(address, line);
                if ((area == 'I' || area == 'Q') && v.Type != StType.Bool) throw new StCompileException(line, $"{address} bir bit: {v.Name} BOOL olmalı.");
                if (area == 'M' && v.Type == StType.Bool) throw new StCompileException(line, $"{address} bir sözcük: {v.Name} INT olmalı.");
                v.Area = area;
                v.Address = index;
            }

            /// <summary>%IX1.3 → ('I', 11); %QX0.0 → ('Q', 0); %MW4 → ('M', 4).</summary>
            private static (char Area, int Index) Address(string a, int line)
            {
                if (a.Length >= 4 && (a[1] == 'I' || a[1] == 'Q') && a[2] == 'X')
                {
                    var parts = a.Substring(3).Split('.');
                    if (parts.Length == 2 && int.TryParse(parts[0], out int by) && int.TryParse(parts[1], out int bit) && bit is >= 0 and < 8 && by is >= 0 and < 8)
                        return (a[1], by * 8 + bit);
                }
                else if (a.Length >= 4 && a[1] == 'M' && a[2] == 'W' && int.TryParse(a.Substring(3), out int w) && w is >= 0 and < 256)
                {
                    return ('M', w);
                }

                throw new StCompileException(line, $"{a} adresi tanınmadı (%IX0.0–%IX7.7, %QX0.0–%QX7.7, %MW0–%MW255).");
            }

            private Token Ident(string what)
            {
                if (Peek.Kind != Tok.Ident) throw new StCompileException(Peek.Line, $"{what} bekleniyordu, '{Shown(Peek)}' bulundu.");
                return Next();
            }

            private Var Lookup(Token name)
            {
                if (_vars.TryGetValue(name.Text, out var v)) return v;
                throw new StCompileException(name.Line, $"'{name.Text}' tanımlı değil (VAR … END_VAR içinde tanımlayın).");
            }

            /// <summary>A direct address used in the code: a hidden variable bound to it.</summary>
            private Var Direct(Token a)
            {
                if (_vars.TryGetValue(a.Text, out var v)) return v;
                var (area, _) = Address(a.Text, a.Line);
                v = new Var { Name = a.Text, Type = area == 'M' ? StType.Int : StType.Bool, TypeName = area == 'M' ? "INT" : "BOOL" };
                Bind(v, a.Text, a.Line);
                _vars[a.Text] = v;
                return v;
            }

            private List<Stmt> Statements(params string[] until)
            {
                var list = new List<Stmt>();
                while (Peek.Kind != Tok.End && !until.Any(u => Peek.Is(u)))
                {
                    if (Accept(";")) continue;
                    list.Add(Statement());
                }

                return list;
            }

            private Stmt Statement()
            {
                var t = Peek;
                if (Accept("IF")) return IfStatement(t.Line);
                if (Accept("CASE")) return CaseStatement(t.Line);
                if (Accept("FOR")) return ForStatement(t.Line);
                if (Accept("WHILE"))
                {
                    var c = Condition();
                    Expect("DO", "DO");
                    var body = Statements("END_WHILE");
                    Expect("END_WHILE", "END_WHILE");
                    Accept(";");
                    return new While { Line = t.Line, Condition = c, Body = body };
                }

                if (Accept("REPEAT"))
                {
                    var body = Statements("UNTIL");
                    Expect("UNTIL", "UNTIL");
                    var c = Condition();
                    Expect("END_REPEAT", "END_REPEAT");
                    Accept(";");
                    return new While { Line = t.Line, Condition = c, Body = body, Repeat = true };
                }

                if (Accept("EXIT"))
                {
                    Expect(";", "';'");
                    return new Jump { Line = t.Line, Kind = Flow.Exit };
                }

                if (Accept("RETURN"))
                {
                    Expect(";", "';'");
                    return new Jump { Line = t.Line, Kind = Flow.Return };
                }

                Var target;
                if (t.Kind == Tok.Address) target = Direct(Next());
                else if (t.Kind == Tok.Ident) target = Lookup(Next());
                else throw new StCompileException(t.Line, $"Bir komut bekleniyordu, '{Shown(t)}' bulundu.");

                if (target.Type == StType.Block)
                {
                    if (Accept("."))
                    {
                        var m = Ident("blok girişi");
                        if (!target.Block!.Inputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{target.TypeName} bloğunun '{m.Text}' girişi yok ({string.Join(", ", target.Block.Inputs)}).");
                        Expect(":=", "':='");
                        var value = Expression();
                        Expect(";", "';'");
                        return new Assign { Line = t.Line, Target = target, Member = m.Text, Value = value };
                    }

                    return BlockCallStatement(target, t.Line);
                }

                Expect(":=", "':='");
                var e = Expression();
                Expect(";", "';' (komutun sonu)");
                Check(target, e);
                return new Assign { Line = t.Line, Target = target, Value = e };
            }

            private static void Check(Var target, Expr e)
            {
                if (target.Type == StType.Bool && e.Type != StType.Bool && e is not Const)
                    throw new StCompileException(e.Line, $"{target.Name} BOOL: sağ taraf bir koşul olmalı (örneğin x > 0).");
                if (target.Type != StType.Bool && e.Type == StType.Bool)
                    throw new StCompileException(e.Line, $"{target.Name} {target.TypeName}: BOOL atanamaz (BOOL_TO_INT kullanın).");
            }

            private Stmt BlockCallStatement(Var instance, int line)
            {
                var call = new BlockCall { Line = line, Instance = instance };
                Expect("(", "'(' (blok çağrısı)");
                if (!Accept(")"))
                {
                    do
                    {
                        var m = Ident("blok parametresi");
                        if (Accept(":="))
                        {
                            if (!instance.Block!.Inputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{instance.TypeName} bloğunun '{m.Text}' girişi yok ({string.Join(", ", instance.Block.Inputs)}).");
                            call.Inputs.Add((m.Text, Expression()));
                        }
                        else if (Accept("=>"))
                        {
                            if (!instance.Block!.Outputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{instance.TypeName} bloğunun '{m.Text}' çıkışı yok ({string.Join(", ", instance.Block.Outputs)}).");
                            var tok = Peek;
                            var target = tok.Kind == Tok.Address ? Direct(Next()) : Lookup(Ident("değişken"));
                            call.Outputs.Add((m.Text, target));
                        }
                        else
                        {
                            throw new StCompileException(m.Line, $"'{m.Text}' için ':=' ya da '=>' bekleniyordu.");
                        }
                    }
                    while (Accept(","));

                    Expect(")", "')'");
                }

                Expect(";", "';'");
                return call;
            }

            private Stmt IfStatement(int line)
            {
                var s = new If { Line = line };
                var c = Condition();
                Expect("THEN", "THEN");
                s.Branches.Add((c, Statements("ELSIF", "ELSE", "END_IF")));
                while (Accept("ELSIF"))
                {
                    var ec = Condition();
                    Expect("THEN", "THEN");
                    s.Branches.Add((ec, Statements("ELSIF", "ELSE", "END_IF")));
                }

                if (Accept("ELSE")) s.Else = Statements("END_IF");
                Expect("END_IF", "END_IF");
                Accept(";");
                return s;
            }

            private Stmt CaseStatement(int line)
            {
                var s = new Case { Line = line, Selector = Expression() };
                if (s.Selector.Type is StType.Real or StType.Bool) throw new StCompileException(line, "CASE bir tamsayı üzerinde çalışır.");
                Expect("OF", "OF");
                while (!Peek.Is("ELSE") && !Peek.Is("END_CASE"))
                {
                    if (Peek.Kind == Tok.End) throw new StCompileException(Peek.Line, "END_CASE eksik.");
                    var labels = new List<(double, double)>();
                    do
                    {
                        double lo = Label(), hi = lo;
                        if (Accept("..")) hi = Label();
                        labels.Add((lo, hi));
                    }
                    while (Accept(","));

                    Expect(":", "':' (durum etiketi)");
                    var body = new List<Stmt>();
                    while (!Peek.Is("ELSE") && !Peek.Is("END_CASE") && !LabelAhead())
                    {
                        if (Peek.Kind == Tok.End) throw new StCompileException(Peek.Line, "END_CASE eksik.");
                        if (Accept(";")) continue;
                        body.Add(Statement());
                    }

                    s.Arms.Add((labels, body));
                }

                if (Accept("ELSE")) s.Else = Statements("END_CASE");
                Expect("END_CASE", "END_CASE");
                Accept(";");
                return s;
            }

            /// <summary>The next tokens start a CASE label ("3:", "4..6:", "1, 2:", "-1:").</summary>
            private bool LabelAhead()
            {
                int j = _i;
                if (_t[j].Is("-")) j++;
                if (_t[j].Kind != Tok.Number) return false;
                j++;
                return _t[j].Is(":") || _t[j].Is(",") || _t[j].Is("..");
            }

            private double Label()
            {
                bool negative = Accept("-");
                if (Peek.Kind != Tok.Number) throw new StCompileException(Peek.Line, $"Durum etiketi bir tamsayı olmalı, '{Shown(Peek)}' bulundu.");
                double v = Next().Value;
                return negative ? -v : v;
            }

            private Stmt ForStatement(int line)
            {
                var counter = Lookup(Ident("sayaç değişkeni"));
                if (counter.Type != StType.Int) throw new StCompileException(line, "FOR sayacı bir tamsayı olmalı.");
                Expect(":=", "':='");
                var from = Expression();
                Expect("TO", "TO");
                var to = Expression();
                Expr? by = Accept("BY") ? Expression() : null;
                Expect("DO", "DO");
                var body = Statements("END_FOR");
                Expect("END_FOR", "END_FOR");
                Accept(";");
                return new For { Line = line, Counter = counter, From = from, To = to, By = by, Body = body };
            }

            private Expr Condition()
            {
                var e = Expression();
                if (e.Type != StType.Bool) throw new StCompileException(e.Line, "Burada bir koşul (BOOL) bekleniyordu.");
                return e;
            }

            // Precedence, lowest first: OR, XOR, AND (&), = <>, < <= > >=, + -, * / MOD, **, unary - NOT.
            private Expr Expression() => Or();

            private Expr Or()
            {
                var a = Xor();
                while (Peek.Is("OR"))
                {
                    int line = Next().Line;
                    a = Logic("OR", a, Xor(), line);
                }

                return a;
            }

            private Expr Xor()
            {
                var a = And();
                while (Peek.Is("XOR"))
                {
                    int line = Next().Line;
                    a = Logic("XOR", a, And(), line);
                }

                return a;
            }

            private Expr And()
            {
                var a = Equality();
                while (Peek.Is("AND") || Peek.Is("&"))
                {
                    int line = Next().Line;
                    a = Logic("AND", a, Equality(), line);
                }

                return a;
            }

            private static Expr Logic(string op, Expr a, Expr b, int line)
            {
                bool boolean = a.Type == StType.Bool || b.Type == StType.Bool;
                if (boolean && (a.Type != StType.Bool || b.Type != StType.Bool) && !(a is Const) && !(b is Const))
                    throw new StCompileException(line, $"{op} iki BOOL ya da iki tamsayı ister.");
                return new Binary { Line = line, Op = op, A = a, B = b, Type = boolean ? StType.Bool : StType.Int };
            }

            private Expr Equality()
            {
                var a = Relation();
                while (Peek.Is("=") || Peek.Is("<>"))
                {
                    var op = Next();
                    a = new Binary { Line = op.Line, Op = op.Text, A = a, B = Relation(), Type = StType.Bool };
                }

                return a;
            }

            private Expr Relation()
            {
                var a = Sum();
                while (Peek.Is("<") || Peek.Is("<=") || Peek.Is(">") || Peek.Is(">="))
                {
                    var op = Next();
                    a = new Binary { Line = op.Line, Op = op.Text, A = a, B = Sum(), Type = StType.Bool };
                }

                return a;
            }

            private Expr Sum()
            {
                var a = Product();
                while (Peek.Is("+") || Peek.Is("-"))
                {
                    var op = Next();
                    var b = Product();
                    a = new Binary { Line = op.Line, Op = op.Text, A = a, B = b, Type = Arith(a, b, op.Line) };
                }

                return a;
            }

            private Expr Product()
            {
                var a = Power();
                while (Peek.Is("*") || Peek.Is("/") || Peek.Is("MOD"))
                {
                    var op = Next();
                    var b = Power();
                    a = new Binary { Line = op.Line, Op = op.Text, A = a, B = b, Type = Arith(a, b, op.Line) };
                }

                return a;
            }

            private Expr Power()
            {
                var a = Unary();
                if (Peek.Is("**"))
                {
                    var op = Next();
                    var b = Power();
                    return new Binary { Line = op.Line, Op = "**", A = a, B = b, Type = StType.Real };
                }

                return a;
            }

            private static StType Arith(Expr a, Expr b, int line)
            {
                if (a.Type == StType.Bool || b.Type == StType.Bool) throw new StCompileException(line, "BOOL ile hesap yapılmaz (BOOL_TO_INT kullanın).");
                if (a.Type == StType.Real || b.Type == StType.Real) return StType.Real;
                if (a.Type == StType.Time || b.Type == StType.Time) return StType.Time;
                return StType.Int;
            }

            private Expr Unary()
            {
                if (Peek.Is("NOT"))
                {
                    int line = Next().Line;
                    var a = Unary();
                    return new Unary { Line = line, Op = "NOT", A = a, Type = a.Type == StType.Bool ? StType.Bool : StType.Int };
                }

                if (Peek.Is("-"))
                {
                    int line = Next().Line;
                    var a = Unary();
                    if (a is Const k) return new Const { Line = line, Value = -k.Value, Type = k.Type };
                    return new Unary { Line = line, Op = "-", A = a, Type = a.Type };
                }

                if (Peek.Is("+")) Next();
                return Primary();
            }

            private Expr Primary()
            {
                var t = Next();
                switch (t.Kind)
                {
                    case Tok.Number:
                        return new Const { Line = t.Line, Value = t.Value, Type = t.Text.Contains('.') || t.Text.Contains('E') || t.Text.Contains('e') ? StType.Real : StType.Int };
                    case Tok.Time:
                        return new Const { Line = t.Line, Value = t.Value, Type = StType.Time };
                    case Tok.Address:
                    {
                        var v = Direct(t);
                        return new Ref { Line = t.Line, Var = v, Type = v.Type };
                    }

                    case Tok.Symbol when t.Text == "(":
                    {
                        var e = Expression();
                        Expect(")", "')'");
                        return e;
                    }

                    case Tok.Ident:
                    {
                        if (t.Text == "TRUE" || t.Text == "FALSE") return new Const { Line = t.Line, Value = t.Text == "TRUE" ? 1 : 0, Type = StType.Bool };
                        if (Peek.Is("(") && (Functions.Contains(t.Text) || t.Text.Contains("_TO_")))
                        {
                            Next();
                            var call = new Call { Line = t.Line, Name = t.Text };
                            if (!Accept(")"))
                            {
                                do call.Args.Add(Expression());
                                while (Accept(","));
                                Expect(")", "')'");
                            }

                            int need = t.Text switch { "LIMIT" or "SEL" => 3, "MIN" or "MAX" => 2, _ => 1 };
                            if (call.Args.Count < need || (need != 2 && call.Args.Count != need)) throw new StCompileException(t.Line, $"{t.Text} {need} değer ister.");
                            call.Type = CallType(t.Text, call.Args);
                            return call;
                        }

                        var v = Lookup(t);
                        if (v.Type == StType.Block)
                        {
                            Expect(".", $"'.' ({v.Name} bir blok: {v.Name}.Q gibi)");
                            var m = Ident("blok çıkışı");
                            if (!v.Block!.Outputs.Contains(m.Text) && !v.Block.Inputs.Contains(m.Text))
                                throw new StCompileException(m.Line, $"{v.TypeName} bloğunun '{m.Text}' üyesi yok ({string.Join(", ", v.Block.Outputs)}).");
                            var type = m.Text is "Q" or "Q1" or "QU" or "QD" or "IN" or "CLK" or "CU" or "CD" or "R" or "LD" or "S" or "S1" or "R1" ? StType.Bool
                                : m.Text is "ET" or "PT" ? StType.Time : StType.Int;
                            return new Member { Line = t.Line, Var = v, Name = m.Text, Type = type };
                        }

                        return new Ref { Line = t.Line, Var = v, Type = v.Type };
                    }

                    default:
                        throw new StCompileException(t.Line, $"Bir değer bekleniyordu, '{Shown(t)}' bulundu.");
                }
            }

            private static StType CallType(string name, List<Expr> args)
            {
                if (name.Contains("_TO_"))
                {
                    string to = name.Substring(name.IndexOf("_TO_", StringComparison.Ordinal) + 4);
                    return to == "BOOL" ? StType.Bool : to is "REAL" or "LREAL" ? StType.Real : to == "TIME" ? StType.Time : StType.Int;
                }

                return name switch
                {
                    "SEL" => args[1].Type,
                    "MIN" or "MAX" or "LIMIT" or "ABS" => args.Any(a => a.Type == StType.Real) ? StType.Real : args[0].Type,
                    "TRUNC" => StType.Int,
                    _ => StType.Real
                };
            }
        }

        private sealed class StCompileException : Exception
        {
            public StCompileException(int line, string message) : base(message) => Line = line;

            public int Line { get; }
        }

        private sealed class StRunException : Exception
        {
            public StRunException(string message) : base(message)
            {
            }
        }
    }
}
