using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] SPICE model library reader (.lib, .mod, .cir, .sub): .model cards, .subckt
    // definitions, .param values, .include of local files. Dialects: SPICE3/ngspice,
    // PSpice and LTspice syntax for the parts that matter to model libraries.

    /// <summary>A <c>.model NAME TYPE(...)</c> card.</summary>
    public sealed class SpiceModelCard
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Upper-case model type: D, NPN, PNP, NMOS, PMOS, VDMOS, NJF, SW ...</summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>Numeric parameters (upper-case keys).</summary>
        public Dictionary<string, double> Params { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Non-numeric parameters (mfg=..., type=...).</summary>
        public Dictionary<string, string> Text { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Bare flags such as LTspice's "pchan".</summary>
        public HashSet<string> Flags { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string File { get; set; } = string.Empty;
        public int Line { get; set; }

        /// <summary>The logical source line (continuations joined, comments removed).</summary>
        public string Source { get; set; } = string.Empty;

        public double Get(string key, double fallback) => Params.TryGetValue(key, out var v) ? v : fallback;

        public bool Has(string key) => Params.ContainsKey(key);

        /// <summary>D / NPN / PNP / NMOS / PMOS after resolving VDMOS (pchan flag) and aliases.</summary>
        public string Kind
        {
            get
            {
                switch (Type)
                {
                    case "VDMOS": return Flags.Contains("PCHAN") || Text.ContainsKey("PCHAN") ? "PMOS" : "NMOS";
                    case "LPNP": return "PNP";
                    default: return Type;
                }
            }
        }

        public override string ToString() => $".model {Name} {Type}";
    }

    /// <summary>One element line (R1 a b 1k).</summary>
    public sealed class SpiceElement
    {
        /// <summary>Upper-case first letter of the name.</summary>
        public char Kind { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Tokens after the name (parentheses and commas removed, "k = v" joined to "k=v").</summary>
        public List<string> Tokens { get; } = new List<string>();

        public string File { get; set; } = string.Empty;
        public int Line { get; set; }
        public string Source { get; set; } = string.Empty;

        public override string ToString() => Source;
    }

    /// <summary>A <c>.subckt</c> definition.</summary>
    public sealed class SpiceSubcircuit
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Pins { get; } = new List<string>();

        /// <summary>Default parameter expressions (PARAMS: a=1 b={a*2}).</summary>
        public Dictionary<string, string> Parameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<SpiceElement> Elements { get; } = new List<SpiceElement>();

        /// <summary>Models and subcircuits defined inside this subcircuit (local scope).</summary>
        public Dictionary<string, SpiceModelCard> Models { get; } = new Dictionary<string, SpiceModelCard>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, SpiceSubcircuit> Subcircuits { get; } = new Dictionary<string, SpiceSubcircuit>(StringComparer.OrdinalIgnoreCase);

        /// <summary>.param lines inside the subcircuit.</summary>
        public Dictionary<string, string> LocalParams { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public string File { get; set; } = string.Empty;
        public int Line { get; set; }

        /// <summary>Logical source lines from .subckt to .ends.</summary>
        public List<string> Source { get; } = new List<string>();

        public string Description { get; set; } = string.Empty;

        public override string ToString() => $".subckt {Name} {string.Join(" ", Pins)}";
    }

    /// <summary>Everything read from one SPICE file (plus its includes).</summary>
    public sealed class SpiceLibrary
    {
        public string Title { get; set; } = string.Empty;
        public Dictionary<string, SpiceModelCard> Models { get; } = new Dictionary<string, SpiceModelCard>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpiceSubcircuit> Subcircuits { get; } = new Dictionary<string, SpiceSubcircuit>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Global .param expressions.</summary>
        public Dictionary<string, string> Params { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Global .param logical lines (kept to make extracted subcircuits self-contained).</summary>
        public List<string> ParamSource { get; } = new List<string>();

        /// <summary>Elements found outside any .subckt (a whole circuit; not imported as a part).</summary>
        public List<SpiceElement> TopLevel { get; } = new List<SpiceElement>();

        public ImportLog Log { get; } = new ImportLog();

        /// <summary>Order in which models/subcircuits appeared (for previews).</summary>
        public List<string> Order { get; } = new List<string>();

        /// <summary>
        /// A self-contained SPICE text for one subcircuit: the global parameters, every model
        /// card and subcircuit it uses (transitively) and the subcircuit itself.
        /// </summary>
        public string Extract(string subcktName)
        {
            if (!Subcircuits.TryGetValue(subcktName, out var root)) throw new KeyNotFoundException(subcktName);
            var models = new List<SpiceModelCard>();
            var subs = new List<SpiceSubcircuit>();
            var seenModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenSubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(SpiceSubcircuit s, int depth)
            {
                if (depth > 32 || !seenSubs.Add(s.Name)) return;
                foreach (var e in s.Elements)
                {
                    foreach (var t in e.Tokens)
                    {
                        if (s.Models.ContainsKey(t)) continue;
                        if (Models.TryGetValue(t, out var m) && seenModels.Add(m.Name)) models.Add(m);
                    }

                    if (e.Kind == 'X')
                    {
                        string? name = SpiceSyntax.SubcircuitNameOf(e);
                        if (name != null && !s.Subcircuits.ContainsKey(name) && Subcircuits.TryGetValue(name, out var child)) Visit(child, depth + 1);
                    }
                }

                if (!ReferenceEquals(s, root)) subs.Add(s);
            }

            Visit(root, 0);
            var sb = new StringBuilder();
            sb.Append("* ").Append(root.Name).Append(" — RobotWin tarafından ").Append(string.IsNullOrEmpty(root.File) ? "SPICE" : Path.GetFileName(root.File)).Append(" dosyasından ayıklandı\n");
            foreach (var p in ParamSource) sb.Append(p).Append('\n');
            foreach (var m in models) sb.Append(m.Source).Append('\n');
            foreach (var s in subs)
            {
                foreach (var l in s.Source) sb.Append(l).Append('\n');
            }

            foreach (var l in root.Source) sb.Append(l).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>Lexical helpers shared by the parser and the circuit builder.</summary>
    public static class SpiceSyntax
    {
        /// <summary>
        /// Parses a SPICE number: "1k", "4.7u", "10MEG", "2.2nF", "1e-3", "4k7", "25mil".
        /// Case-insensitive; M = milli, MEG = mega; trailing unit letters are ignored.
        /// </summary>
        public static bool TryParseNumber(string? text, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text!.Trim().Replace('µ', 'u');
            int i = 0;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            int digitsStart = i;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                while (i < s.Length && char.IsDigit(s[i])) i++;
            }

            if (i == digitsStart || (i == digitsStart + 1 && s[digitsStart] == '.')) return false;
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int expStart = i;
                while (i < s.Length && char.IsDigit(s[i])) i++;
                if (i == expStart) i = save;
            }

            if (!double.TryParse(s.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) return false;
            string rest = s.Substring(i).ToUpperInvariant();
            double scale = 1;
            int used = 0;
            if (rest.StartsWith("MEG", StringComparison.Ordinal)) { scale = 1e6; used = 3; }
            else if (rest.StartsWith("MIL", StringComparison.Ordinal)) { scale = 25.4e-6; used = 3; }
            else if (rest.Length > 0)
            {
                switch (rest[0])
                {
                    case 'T': scale = 1e12; used = 1; break;
                    case 'G': scale = 1e9; used = 1; break;
                    case 'K': scale = 1e3; used = 1; break;
                    case 'M': scale = 1e-3; used = 1; break;
                    case 'U': scale = 1e-6; used = 1; break;
                    case 'N': scale = 1e-9; used = 1; break;
                    case 'P': scale = 1e-12; used = 1; break;
                    case 'F': scale = 1e-15; used = 1; break;
                }
            }

            string tail = rest.Substring(used);
            // "4k7": digits after the multiplier are the fraction.
            if (used > 0 && tail.Length > 0 && char.IsDigit(tail[0]) && s.IndexOf('.') < 0 && s.IndexOf('e') < 0 && s.IndexOf('E') < 0)
            {
                int j = 0;
                while (j < tail.Length && char.IsDigit(tail[j])) j++;
                if (double.TryParse(s.Substring(0, i) + "." + tail.Substring(0, j), NumberStyles.Float, CultureInfo.InvariantCulture, out double frac))
                {
                    number = frac;
                    tail = tail.Substring(j);
                }
            }

            // Whatever is left must be a unit word (V, A, F, H, OHM, HZ, S, Ω ...), never digits.
            foreach (char c in tail)
            {
                if (!char.IsLetter(c) && c != 'Ω' && c != '°') return false;
            }

            value = number * scale;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Splits a logical line into tokens: whitespace, commas and parentheses separate; "k = v" becomes "k=v"; {…} and '…' stay whole.</summary>
        public static List<string> Tokenize(string line)
        {
            var raw = new List<string>();
            var sb = new StringBuilder();
            int brace = 0;
            bool quote = false;
            void Flush()
            {
                if (sb.Length > 0) raw.Add(sb.ToString());
                sb.Clear();
            }

            foreach (char c in line)
            {
                if (quote)
                {
                    sb.Append(c);
                    if (c == '\'' || c == '"') quote = false;
                    continue;
                }

                if (brace > 0)
                {
                    sb.Append(c);
                    if (c == '{') brace++;
                    else if (c == '}') brace--;
                    continue;
                }

                if (c == '{')
                {
                    brace = 1;
                    sb.Append(c);
                }
                else if (c == '\'' || c == '"')
                {
                    quote = true;
                    sb.Append(c);
                }
                else if (char.IsWhiteSpace(c) || c == ',' || c == '(' || c == ')')
                {
                    Flush();
                }
                else if (c == '=')
                {
                    Flush();
                    raw.Add("=");
                }
                else
                {
                    sb.Append(c);
                }
            }

            Flush();
            var tokens = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i] == "=" && tokens.Count > 0 && i + 1 < raw.Count && raw[i + 1] != "=")
                {
                    tokens[tokens.Count - 1] = tokens[tokens.Count - 1] + "=" + raw[i + 1];
                    i++;
                }
                else if (raw[i] != "=")
                {
                    tokens.Add(raw[i]);
                }
            }

            return tokens;
        }

        public static bool IsAssignment(string token, out string key, out string value)
        {
            int eq = token.IndexOf('=');
            if (eq > 0 && token[0] != '{')
            {
                key = token.Substring(0, eq);
                value = token.Substring(eq + 1);
                return true;
            }

            key = value = string.Empty;
            return false;
        }

        /// <summary>The subcircuit name of an X line: the last token before any "params:"/assignment.</summary>
        public static string? SubcircuitNameOf(SpiceElement x)
        {
            string? last = null;
            foreach (var t in x.Tokens)
            {
                if (t.Equals("params:", StringComparison.OrdinalIgnoreCase) || IsAssignment(t, out _, out _)) break;
                last = t;
            }

            return last;
        }

        public static string Unquote(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0]) return s.Substring(1, s.Length - 2);
            return s;
        }
    }

    /// <summary>Small arithmetic evaluator for {…} parameter expressions.</summary>
    public sealed class SpiceExpression
    {
        private readonly string _s;
        private readonly Func<string, double?> _lookup;
        private int _i;

        private SpiceExpression(string s, Func<string, double?> lookup)
        {
            _s = s;
            _lookup = lookup;
        }

        /// <summary>Evaluates "1k", "{R1*2}", "'a+b'", "{max(a,b)}"; returns false with a reason when it cannot.</summary>
        public static bool TryEvaluate(string text, Func<string, double?> lookup, out double value, out string error)
        {
            value = 0;
            error = string.Empty;
            string s = SpiceSyntax.Unquote(text.Trim());
            if (s.StartsWith("{", StringComparison.Ordinal) && s.EndsWith("}", StringComparison.Ordinal)) s = s.Substring(1, s.Length - 2);
            if (SpiceSyntax.TryParseNumber(s, out value)) return true;
            try
            {
                var e = new SpiceExpression(s, lookup);
                value = e.Sum();
                e.Skip();
                if (e._i < s.Length) throw new FormatException($"beklenmeyen '{s.Substring(e._i)}'");
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new FormatException("sonuç sayı değil");
                return true;
            }
            catch (FormatException ex)
            {
                error = $"'{text}' ifadesi hesaplanamadı: {ex.Message}";
                return false;
            }
        }

        private void Skip()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private bool Eat(char c)
        {
            Skip();
            if (_i < _s.Length && _s[_i] == c)
            {
                _i++;
                return true;
            }

            return false;
        }

        private double Sum()
        {
            double v = Product();
            while (true)
            {
                if (Eat('+')) v += Product();
                else if (Eat('-')) v -= Product();
                else return v;
            }
        }

        private double Product()
        {
            double v = Power();
            while (true)
            {
                if (Eat('*'))
                {
                    if (Eat('*')) v = Math.Pow(v, Power());
                    else v *= Power();
                }
                else if (Eat('/')) v /= Power();
                else return v;
            }
        }

        private double Power()
        {
            double v = Unary();
            if (Eat('^')) return Math.Pow(v, Power());
            return v;
        }

        private double Unary()
        {
            if (Eat('-')) return -Unary();
            if (Eat('+')) return Unary();
            return Atom();
        }

        private double Atom()
        {
            Skip();
            if (Eat('('))
            {
                double v = Sum();
                if (!Eat(')')) throw new FormatException("')' eksik");
                return v;
            }

            if (_i >= _s.Length) throw new FormatException("ifade eksik");
            int start = _i;
            char c = _s[_i];
            if (char.IsDigit(c) || c == '.')
            {
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '.' ||
                                          ((_s[_i] == '+' || _s[_i] == '-') && _i > start && (_s[_i - 1] == 'e' || _s[_i - 1] == 'E') && char.IsDigit(_s[start]))))
                {
                    _i++;
                }

                string num = _s.Substring(start, _i - start);
                if (!SpiceSyntax.TryParseNumber(num, out double v)) throw new FormatException($"'{num}' sayı değil");
                return v;
            }

            if (char.IsLetter(c) || c == '_')
            {
                while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                string name = _s.Substring(start, _i - start);
                if (Eat('('))
                {
                    var args = new List<double>();
                    if (!Eat(')'))
                    {
                        do args.Add(Sum());
                        while (Eat(','));
                        if (!Eat(')')) throw new FormatException("')' eksik");
                    }

                    return Call(name, args);
                }

                var p = _lookup(name);
                if (p.HasValue) return p.Value;
                throw new FormatException($"bilinmeyen parametre '{name}'");
            }

            throw new FormatException($"beklenmeyen '{c}'");
        }

        private static double Call(string name, List<double> a)
        {
            double A(int i) => i < a.Count ? a[i] : throw new FormatException(name + "() için argüman eksik");
            switch (name.ToLowerInvariant())
            {
                case "sqrt": return Math.Sqrt(A(0));
                case "abs": return Math.Abs(A(0));
                case "exp": return Math.Exp(A(0));
                case "ln":
                case "log": return Math.Log(A(0));
                case "log10": return Math.Log10(A(0));
                case "min": return Math.Min(A(0), A(1));
                case "max": return Math.Max(A(0), A(1));
                case "pow":
                case "pwr": return Math.Pow(A(0), A(1));
                case "sin": return Math.Sin(A(0));
                case "cos": return Math.Cos(A(0));
                case "tan": return Math.Tan(A(0));
                case "atan": return Math.Atan(A(0));
                case "int": return Math.Truncate(A(0));
                case "round": return Math.Round(A(0));
                case "limit": return Math.Max(A(1), Math.Min(A(2), A(0)));
                default: throw new FormatException($"desteklenmeyen fonksiyon '{name}()'");
            }
        }
    }

    /// <summary>Reads SPICE libraries. Never throws on malformed content: problems go to <see cref="SpiceLibrary.Log"/>.</summary>
    public static class SpiceParser
    {
        private const int MaxIncludeDepth = 6;

        public static readonly string[] Extensions = { ".lib", ".mod", ".cir", ".sub", ".sp", ".spi", ".spice", ".model", ".txt", ".net", ".ckt", ".inc" };

        public static SpiceLibrary ParseFile(string path)
        {
            var lib = new SpiceLibrary();
            try
            {
                ParseInto(lib, ReadText(path), path, 0, IsCircuitFile(path));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lib.Log.Error("dosya okunamadı: " + ex.Message, path);
            }

            Finish(lib);
            return lib;
        }

        public static SpiceLibrary Parse(string text, string fileName = "")
        {
            var lib = new SpiceLibrary();
            ParseInto(lib, text ?? string.Empty, fileName, 0, IsCircuitFile(fileName));
            Finish(lib);
            return lib;
        }

        private static bool IsCircuitFile(string path)
        {
            string ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
            return ext == ".cir" || ext == ".net" || ext == ".ckt" || ext == ".sp";
        }

        private static string ReadText(string path)
        {
            var bytes = System.IO.File.ReadAllBytes(path);
            if (bytes.Length > 64 * 1024 * 1024) throw new IOException("dosya 64 MB'tan büyük");
            // UTF-8 when valid, otherwise Latin-1 (old vendor files).
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.GetEncoding(28591).GetString(bytes);
            }
        }

        private readonly struct Logical
        {
            public Logical(string text, int line)
            {
                Text = text;
                Line = line;
            }

            public string Text { get; }
            public int Line { get; }
        }

        /// <summary>Removes comments and joins '+' continuation lines.</summary>
        private static List<Logical> LogicalLines(string text, bool firstIsTitle, out string title)
        {
            title = string.Empty;
            var result = new List<Logical>();
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                string raw = lines[n];
                if (n == 0 && raw.Length > 0 && raw[0] == '﻿') raw = raw.Substring(1);
                if (n == 0 && firstIsTitle && raw.Trim().Length > 0 && !raw.TrimStart().StartsWith("*", StringComparison.Ordinal) && !raw.TrimStart().StartsWith(".", StringComparison.Ordinal))
                {
                    title = raw.Trim();
                    continue;
                }

                string s = StripComment(raw).TrimEnd();
                string trimmed = s.TrimStart();
                if (trimmed.Length == 0) continue;
                if (trimmed[0] == '*') continue;
                if (trimmed[0] == '+')
                {
                    if (result.Count > 0)
                    {
                        var last = result[result.Count - 1];
                        result[result.Count - 1] = new Logical(last.Text + " " + trimmed.Substring(1).Trim(), last.Line);
                    }

                    continue;
                }

                result.Add(new Logical(trimmed, n + 1));
            }

            return result;
        }

        private static string StripComment(string line)
        {
            bool quote = false;
            int brace = 0;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"' || c == '\'') quote = !quote;
                if (quote) continue;
                if (c == '{') brace++;
                else if (c == '}') brace = Math.Max(0, brace - 1);
                if (brace > 0) continue;
                if (c == ';') return line.Substring(0, i);
                if (c == '$' && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return line.Substring(0, i);
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/' && (i == 0 || char.IsWhiteSpace(line[i - 1]))) return line.Substring(0, i);
            }

            return line;
        }

        private static void ParseInto(SpiceLibrary lib, string text, string file, int depth, bool firstIsTitle)
        {
            var lines = LogicalLines(text, firstIsTitle, out string title);
            if (title.Length > 0 && lib.Title.Length == 0)
            {
                lib.Title = title;
                lib.Log.Info("ilk satır başlık olarak okundu: " + title, file, 1);
            }

            var stack = new Stack<SpiceSubcircuit>();
            var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool control = false;
            string pendingComment = string.Empty;

            foreach (var l in lines)
            {
                var tokens = SpiceSyntax.Tokenize(l.Text);
                if (tokens.Count == 0) continue;
                string head = tokens[0];
                string headUpper = head.ToUpperInvariant();

                if (control)
                {
                    if (headUpper == ".ENDC") control = false;
                    continue;
                }

                if (stack.Count > 0) stack.Peek().Source.Add(l.Text);

                if (head[0] == '.')
                {
                    switch (headUpper)
                    {
                        case ".SUBCKT":
                        {
                            if (tokens.Count < 2)
                            {
                                lib.Log.Error(".subckt satırında ad yok.", file, l.Line);
                                continue;
                            }

                            var sub = new SpiceSubcircuit { Name = tokens[1], File = file, Line = l.Line };
                            sub.Source.Add(l.Text);
                            bool inParams = false;
                            for (int i = 2; i < tokens.Count; i++)
                            {
                                string t = tokens[i];
                                if (t.Equals("params:", StringComparison.OrdinalIgnoreCase) || t.Equals("param:", StringComparison.OrdinalIgnoreCase))
                                {
                                    inParams = true;
                                    continue;
                                }

                                if (SpiceSyntax.IsAssignment(t, out var k, out var v))
                                {
                                    inParams = true;
                                    sub.Parameters[k] = v;
                                }
                                else if (inParams)
                                {
                                    lib.Log.Warn($"'{t}' parametre listesinde anlaşılamadı; yok sayıldı.", file, l.Line, sub.Name);
                                }
                                else
                                {
                                    sub.Pins.Add(t);
                                }
                            }

                            if (sub.Pins.Count == 0) lib.Log.Warn("alt devrenin hiç bacağı yok.", file, l.Line, sub.Name);
                            var dup = sub.Pins.GroupBy(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                            if (dup != null) lib.Log.Warn($"'{dup.Key}' bacağı birden çok kez yazılmış (aynı düğüme bağlanır).", file, l.Line, sub.Name);
                            sub.Description = pendingComment;
                            stack.Push(sub);
                            break;
                        }

                        case ".ENDS":
                        {
                            if (stack.Count == 0)
                            {
                                lib.Log.Warn(".ends karşılığı olan .subckt yok; yok sayıldı.", file, l.Line);
                                break;
                            }

                            var sub = stack.Pop();
                            if (tokens.Count > 1 && !tokens[1].Equals(sub.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                lib.Log.Warn($".ends {tokens[1]} açık olan '{sub.Name}' alt devresini kapatıyor.", file, l.Line, sub.Name);
                            }

                            if (stack.Count > 0)
                            {
                                stack.Peek().Subcircuits[sub.Name] = sub;
                            }
                            else
                            {
                                if (lib.Subcircuits.ContainsKey(sub.Name)) lib.Log.Warn($"'{sub.Name}' alt devresi ikinci kez tanımlanmış; sonraki kullanıldı.", file, l.Line, sub.Name);
                                else lib.Order.Add(sub.Name);
                                lib.Subcircuits[sub.Name] = sub;
                            }

                            break;
                        }

                        case ".MODEL":
                        {
                            var card = ParseModel(tokens, l, file, lib.Log);
                            if (card == null) break;
                            if (stack.Count > 0)
                            {
                                stack.Peek().Models[card.Name] = card;
                            }
                            else
                            {
                                if (lib.Models.ContainsKey(card.Name)) lib.Log.Warn($"'{card.Name}' modeli ikinci kez tanımlanmış; sonraki kullanıldı.", file, l.Line, card.Name);
                                else lib.Order.Add(card.Name);
                                lib.Models[card.Name] = card;
                            }

                            break;
                        }

                        case ".PARAM":
                        case ".PARAMS":
                        {
                            var target = stack.Count > 0 ? stack.Peek().LocalParams : lib.Params;
                            for (int i = 1; i < tokens.Count; i++)
                            {
                                if (SpiceSyntax.IsAssignment(tokens[i], out var k, out var v)) target[k] = v;
                                else lib.Log.Warn($".param içinde '{tokens[i]}' anlaşılamadı.", file, l.Line);
                            }

                            if (stack.Count == 0) lib.ParamSource.Add(l.Text);
                            break;
                        }

                        case ".INCLUDE":
                        case ".INC":
                        case ".LIB":
                        {
                            if (headUpper == ".LIB" && tokens.Count == 2 && !LooksLikePath(tokens[1]))
                            {
                                break; // section marker inside a library file
                            }

                            if (tokens.Count < 2)
                            {
                                if (headUpper != ".LIB") lib.Log.Warn(head + " satırında dosya adı yok.", file, l.Line);
                                break;
                            }

                            Include(lib, SpiceSyntax.Unquote(tokens[1]), file, l.Line, depth);
                            break;
                        }

                        case ".ENDL":
                            break;
                        case ".END":
                            if (stack.Count > 0) lib.Log.Error($"dosya bitti ama '{stack.Peek().Name}' alt devresi kapanmadı (.ends eksik).", file, l.Line);
                            return;
                        case ".CONTROL":
                            control = true;
                            break;
                        case ".FUNC":
                            lib.Log.Warn(".func kullanıcı fonksiyonları desteklenmiyor; bu fonksiyonu kullanan ifadeler hesaplanamaz.", file, l.Line);
                            break;
                        default:
                            if (ignored.Add(headUpper))
                            {
                                lib.Log.Info($"{head} komutu kütüphane içe aktarımında kullanılmıyor (analiz/çıktı komutu); yok sayıldı.", file, l.Line);
                            }

                            break;
                    }

                    pendingComment = string.Empty;
                    continue;
                }

                var element = new SpiceElement
                {
                    Kind = char.ToUpperInvariant(head[0]),
                    Name = head,
                    File = file,
                    Line = l.Line,
                    Source = l.Text
                };
                element.Tokens.AddRange(tokens.Skip(1));
                if (stack.Count > 0) stack.Peek().Elements.Add(element);
                else lib.TopLevel.Add(element);
            }

            while (stack.Count > 0)
            {
                var sub = stack.Pop();
                lib.Log.Error($"'{sub.Name}' alt devresi kapanmadı (.ends eksik); dosya sonuna kadar okunan kısım kullanıldı.", file, sub.Line, sub.Name);
                if (stack.Count == 0)
                {
                    if (!lib.Subcircuits.ContainsKey(sub.Name)) lib.Order.Add(sub.Name);
                    lib.Subcircuits[sub.Name] = sub;
                }
                else
                {
                    stack.Peek().Subcircuits[sub.Name] = sub;
                }
            }
        }

        private static bool LooksLikePath(string token)
        {
            string t = SpiceSyntax.Unquote(token);
            return t.Contains('.') || t.Contains('/') || t.Contains('\\') || token.StartsWith("\"", StringComparison.Ordinal) || token.StartsWith("'", StringComparison.Ordinal);
        }

        private static void Include(SpiceLibrary lib, string name, string fromFile, int line, int depth)
        {
            if (depth >= MaxIncludeDepth)
            {
                lib.Log.Error($"'{name}' dahil edilmedi: iç içe dahil etme sınırı ({MaxIncludeDepth}) aşıldı.", fromFile, line);
                return;
            }

            string? dir = string.IsNullOrEmpty(fromFile) ? null : Path.GetDirectoryName(Path.GetFullPath(fromFile));
            string? path = null;
            foreach (var candidate in new[] { name, dir != null ? Path.Combine(dir, name) : null, dir != null ? Path.Combine(dir, Path.GetFileName(name)) : null })
            {
                if (candidate != null && System.IO.File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
            }

            if (path == null)
            {
                lib.Log.Warn($"'{name}' dosyası bulunamadı; içindeki modeller eksik kalabilir (dosyayı aynı klasöre koyup yeniden içe aktarın).", fromFile, line);
                return;
            }

            try
            {
                lib.Log.Info($"'{Path.GetFileName(path)}' dahil edildi.", fromFile, line);
                ParseInto(lib, ReadText(path), path, depth + 1, false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                lib.Log.Error($"'{name}' okunamadı: {ex.Message}", fromFile, line);
            }
        }

        private static SpiceModelCard? ParseModel(List<string> tokens, Logical l, string file, ImportLog log)
        {
            if (tokens.Count < 3)
            {
                log.Error(".model satırı eksik: '.model AD TÜR(parametreler)' bekleniyordu.", file, l.Line);
                return null;
            }

            var card = new SpiceModelCard { Name = tokens[1], File = file, Line = l.Line, Source = l.Text };
            string type = tokens[2];
            // "D(IS=..." was split at '(' already; "ako:" forms are not supported.
            if (type.StartsWith("ako:", StringComparison.OrdinalIgnoreCase))
            {
                log.Error("'ako:' (başka modelden türetme) desteklenmiyor.", file, l.Line, card.Name);
                return null;
            }

            card.Type = type.ToUpperInvariant();
            for (int i = 3; i < tokens.Count; i++)
            {
                string t = tokens[i];
                if (SpiceSyntax.IsAssignment(t, out var k, out var v))
                {
                    string key = k.ToUpperInvariant();
                    if (SpiceSyntax.TryParseNumber(v, out double num)) card.Params[key] = num;
                    else if (v.StartsWith("{", StringComparison.Ordinal) || v.StartsWith("'", StringComparison.Ordinal))
                    {
                        card.Text[key] = v; // expression: evaluated when the model is used
                    }
                    else card.Text[key] = SpiceSyntax.Unquote(v);
                }
                else
                {
                    card.Flags.Add(t.ToUpperInvariant());
                }
            }

            return card;
        }

        private static void Finish(SpiceLibrary lib)
        {
            if (lib.TopLevel.Count > 0)
            {
                lib.Log.Info($"{lib.TopLevel.Count} eleman hiçbir .subckt içinde değil (tam devre); parça olarak yalnız .model ve .subckt tanımları alınır.");
            }

            if (lib.Models.Count == 0 && lib.Subcircuits.Count == 0)
            {
                lib.Log.Error("dosyada .model veya .subckt tanımı bulunamadı.");
            }
        }
    }
}
