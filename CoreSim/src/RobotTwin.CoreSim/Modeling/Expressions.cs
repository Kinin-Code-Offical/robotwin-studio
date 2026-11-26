using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [param] Formulas for model dimensions: numbers (a decimal point or comma: "2.5", "2,5"), + − * / % ^, brackets,
    /// parameter names (letters of any language, digits, _), pi, and functions in degrees as CAD programs use them:
    /// sin cos tan asin acos atan atan2 sqrt abs min max round floor ceil hypot. Function arguments are split by ";" (or a
    /// comma with a space after it, "max(2, 5)"), since "2,5" is a number.
    /// </summary>
    public static class Expression
    {
        /// <summary>Evaluates <paramref name="text"/>; names are looked up with <paramref name="lookup"/> (null when unknown).</summary>
        public static bool TryEvaluate(string text, Func<string, double?>? lookup, out double value, out string? error)
        {
            value = 0;
            error = null;
            try
            {
                var p = new Parser(text, lookup);
                value = p.Run();
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    error = "sonuç sayı değil";
                    return false;
                }

                return true;
            }
            catch (FormatException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>The names a formula uses (its parameters, without functions and pi).</summary>
        public static List<string> Names(string text)
        {
            var names = new List<string>();
            try
            {
                new Parser(text, n =>
                {
                    if (!names.Contains(n)) names.Add(n);
                    return 1.0;
                }).Run();
            }
            catch (FormatException)
            {
                // A broken formula names what it named before the break.
            }

            return names;
        }

        /// <summary>Whether a text is usable as a parameter name.</summary>
        public static bool IsName(string? name)
        {
            if (string.IsNullOrEmpty(name) || !(char.IsLetter(name![0]) || name[0] == '_')) return false;
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return !Functions.Contains(name.ToLowerInvariant()) && name.ToLowerInvariant() != "pi";
        }

        private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
        {
            "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "sqrt", "abs", "min", "max", "round", "floor", "ceil", "hypot"
        };

        private sealed class Parser
        {
            private readonly string _s;
            private readonly Func<string, double?>? _lookup;
            private int _i;

            public Parser(string s, Func<string, double?>? lookup)
            {
                _s = s ?? "";
                _lookup = lookup;
            }

            public double Run()
            {
                Skip();
                if (_i < _s.Length && _s[_i] == '=') _i++;   // "=a/2" as typed into a field
                double v = Sum();
                Skip();
                if (_i < _s.Length) throw new FormatException($"beklenmeyen '{_s[_i]}'");
                return v;
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
                    else if (Eat('-') || Eat('−')) v -= Product();
                    else return v;
                }
            }

            private double Product()
            {
                double v = Unary();
                while (true)
                {
                    if (Eat('*') || Eat('×')) v *= Unary();
                    else if (Eat('/'))
                    {
                        double d = Unary();
                        if (d == 0) throw new FormatException("sıfıra bölme");
                        v /= d;
                    }
                    else if (Eat('%')) v %= Unary();
                    else return v;
                }
            }

            private double Unary()
            {
                if (Eat('-') || Eat('−')) return -Unary();
                if (Eat('+')) return Unary();
                double v = Primary();
                if (Eat('^')) v = Math.Pow(v, Unary());
                return v;
            }

            private double Primary()
            {
                Skip();
                if (_i >= _s.Length) throw new FormatException("formül eksik");
                char c = _s[_i];
                if (Eat('('))
                {
                    double v = Sum();
                    if (!Eat(')')) throw new FormatException("')' eksik");
                    return v;
                }

                if (char.IsDigit(c) || c == '.') return Number();
                if (char.IsLetter(c) || c == '_' || c == 'π')
                {
                    if (c == 'π')
                    {
                        _i++;
                        return Math.PI;
                    }

                    int start = _i;
                    while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] == '_')) _i++;
                    string name = _s.Substring(start, _i - start);
                    string lower = name.ToLowerInvariant();
                    if (lower == "pi") return Math.PI;
                    if (Functions.Contains(lower))
                    {
                        if (!Eat('(')) throw new FormatException($"{name}(…) bekleniyor");
                        var args = new List<double>();
                        if (!Eat(')'))
                        {
                            do args.Add(Sum());
                            while (Separator());
                            if (!Eat(')')) throw new FormatException("')' eksik");
                        }

                        return Call(lower, args);
                    }

                    double? v = _lookup?.Invoke(name);
                    if (v == null) throw new FormatException($"'{name}' tanımlı değil");
                    return v.Value;
                }

                throw new FormatException($"beklenmeyen '{c}'");
            }

            /// <summary>An argument separator: ";" or a comma (a comma inside a number was taken by the number).</summary>
            private bool Separator() => Eat(';') || Eat(',');

            private double Number()
            {
                int start = _i;
                var sb = new StringBuilder();
                while (_i < _s.Length && char.IsDigit(_s[_i])) sb.Append(_s[_i++]);
                // A decimal point, or a comma with digits on both sides ("2,5"); "max(2, 5)" keeps its comma.
                if (_i < _s.Length && (_s[_i] == '.' || (_s[_i] == ',' && _i > start && _i + 1 < _s.Length && char.IsDigit(_s[_i + 1]))))
                {
                    sb.Append('.');
                    _i++;
                    while (_i < _s.Length && char.IsDigit(_s[_i])) sb.Append(_s[_i++]);
                }

                if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E') && _i + 1 < _s.Length && (char.IsDigit(_s[_i + 1]) || _s[_i + 1] == '-' || _s[_i + 1] == '+'))
                {
                    sb.Append('e');
                    _i++;
                    if (_s[_i] == '-' || _s[_i] == '+') sb.Append(_s[_i++]);
                    while (_i < _s.Length && char.IsDigit(_s[_i])) sb.Append(_s[_i++]);
                }

                if (!double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) throw new FormatException("sayı okunamadı");
                return v;
            }

            private static double Call(string f, List<double> a)
            {
                const double deg = Math.PI / 180;
                void Need(int n)
                {
                    if (a.Count != n) throw new FormatException($"{f} {n} değer alır");
                }

                switch (f)
                {
                    case "sin": Need(1); return Math.Sin(a[0] * deg);
                    case "cos": Need(1); return Math.Cos(a[0] * deg);
                    case "tan": Need(1); return Math.Tan(a[0] * deg);
                    case "asin": Need(1); return Math.Asin(a[0]) / deg;
                    case "acos": Need(1); return Math.Acos(a[0]) / deg;
                    case "atan": Need(1); return Math.Atan(a[0]) / deg;
                    case "atan2": Need(2); return Math.Atan2(a[0], a[1]) / deg;
                    case "sqrt": Need(1); return Math.Sqrt(a[0]);
                    case "abs": Need(1); return Math.Abs(a[0]);
                    case "floor": Need(1); return Math.Floor(a[0]);
                    case "ceil": Need(1); return Math.Ceiling(a[0]);
                    case "hypot": Need(2); return Math.Sqrt(a[0] * a[0] + a[1] * a[1]);
                    case "round":
                        if (a.Count == 1) return Math.Round(a[0], MidpointRounding.AwayFromZero);
                        Need(2);
                        return Math.Round(a[0], (int)Math.Max(0, Math.Min(12, a[1])), MidpointRounding.AwayFromZero);
                    case "min":
                    case "max":
                    {
                        if (a.Count == 0) throw new FormatException($"{f} en az bir değer alır");
                        double r = a[0];
                        foreach (double x in a) r = f == "min" ? Math.Min(r, x) : Math.Max(r, x);
                        return r;
                    }

                    default: throw new FormatException($"'{f}' bilinmiyor");
                }
            }
        }
    }
}
