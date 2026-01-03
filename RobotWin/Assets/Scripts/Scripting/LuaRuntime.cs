using System;
using System.Collections.Generic;
using UnityEngine;

namespace RobotTwin.Scripting
{
    public sealed class LuaContext
    {
        public readonly Dictionary<string, float> Variables = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Func<float[], float>> Functions = new Dictionary<string, Func<float[], float>>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class LuaRuntime
    {
        public LuaRuntime()
        {
        }

        public LuaContext CreateDefaultContext()
        {
            var context = new LuaContext();
            RegisterMath(context);
            return context;
        }

        public void Execute(string script, LuaContext context)
        {
            if (context == null || string.IsNullOrWhiteSpace(script)) return;
            foreach (var rawLine in script.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal)) continue;
                var parser = new LuaParser(line, context);
                parser.ExecuteLine();
            }
        }

        public float EvaluateExpression(string expression, LuaContext context)
        {
            if (context == null) return 0f;
            var parser = new LuaParser(expression, context);
            return parser.ParseExpression();
        }

        private static void RegisterMath(LuaContext context)
        {
            context.Functions["sin"] = args => Mathf.Sin(args[0]);
            context.Functions["cos"] = args => Mathf.Cos(args[0]);
            context.Functions["tan"] = args => Mathf.Tan(args[0]);
            context.Functions["abs"] = args => Mathf.Abs(args[0]);
            context.Functions["min"] = args => Mathf.Min(args[0], args[1]);
            context.Functions["max"] = args => Mathf.Max(args[0], args[1]);
            context.Functions["clamp"] = args => Mathf.Clamp(args[0], args[1], args[2]);
            context.Functions["pow"] = args => Mathf.Pow(args[0], args[1]);
            context.Functions["sqrt"] = args => Mathf.Sqrt(args[0]);
            context.Functions["log"] = args => Mathf.Log(args[0]);
            context.Functions["exp"] = args => Mathf.Exp(args[0]);
        }

        private sealed class LuaParser
        {
            private readonly string _text;
            private readonly LuaContext _context;
            private int _index;

            public LuaParser(string text, LuaContext context)
            {
                _text = text ?? string.Empty;
                _context = context;
            }

            public void ExecuteLine()
            {
                SkipWhitespace();
                int start = _index;
                string name = ReadIdentifier();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    SkipWhitespace();
                    if (Match('='))
                    {
                        float value = ParseExpression();
                        _context.Variables[name] = value;
                        return;
                    }
                    _index = start;
                }
                ParseExpression();
            }

            public float ParseExpression()
            {
                float value = ParseTerm();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('+')) value += ParseTerm();
                    else if (Match('-')) value -= ParseTerm();
                    else break;
                }
                return value;
            }

            private float ParseTerm()
            {
                float value = ParseFactor();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('*')) value *= ParseFactor();
                    else if (Match('/')) value /= ParseFactor();
                    else break;
                }
                return value;
            }

            private float ParseFactor()
            {
                SkipWhitespace();
                if (Match('+')) return ParseFactor();
                if (Match('-')) return -ParseFactor();
                if (Match('('))
                {
                    float value = ParseExpression();
                    Match(')');
                    return value;
                }

                string ident = ReadIdentifier();
                if (!string.IsNullOrWhiteSpace(ident))
                {
                    SkipWhitespace();
                    if (Match('('))
                    {
                        var args = new List<float>();
                        SkipWhitespace();
                        if (!Peek(')'))
                        {
                            args.Add(ParseExpression());
                            while (true)
                            {
                                SkipWhitespace();
                                if (!Match(',')) break;
                                args.Add(ParseExpression());
                            }
                        }
                        Match(')');
                        if (_context.Functions.TryGetValue(ident, out var func))
                        {
                            return func(args.ToArray());
                        }
                        return 0f;
                    }

                    if (_context.Variables.TryGetValue(ident, out var value)) return value;
                    return 0f;
                }

                return ReadNumber();
            }

            private string ReadIdentifier()
            {
                SkipWhitespace();
                int start = _index;
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (char.IsLetterOrDigit(c) || c == '_')
                    {
                        _index++;
                        continue;
                    }
                    break;
                }
                if (_index == start) return string.Empty;
                return _text.Substring(start, _index - start);
            }

            private float ReadNumber()
            {
                SkipWhitespace();
                int start = _index;
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (char.IsDigit(c) || c == '.')
                    {
                        _index++;
                        continue;
                    }
                    break;
                }
                if (start == _index) return 0f;
                float.TryParse(_text.Substring(start, _index - start), out var value);
                return value;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index])) _index++;
            }

            private bool Match(char c)
            {
                if (_index >= _text.Length) return false;
                if (_text[_index] != c) return false;
                _index++;
                return true;
            }

            private bool Peek(char c)
            {
                if (_index >= _text.Length) return false;
                return _text[_index] == c;
            }
        }
    }
}
