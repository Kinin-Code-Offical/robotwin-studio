using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Esp32
{
    /// <summary>One .ino (or tab) of a sketch.</summary>
    public sealed class SketchSourceFile
    {
        public SketchSourceFile(string path, string text)
        {
            Path = path;
            Text = text ?? string.Empty;
        }

        /// <summary>File name as it should appear in diagnostics (full path or a virtual name such as "sketch.ino").</summary>
        public string Path { get; }
        public string Text { get; }
    }

    /// <summary>
    /// Arduino-style sketch preprocessing: concatenates the .ino files, adds
    /// <c>#include &lt;Arduino.h&gt;</c>, generates prototypes for top-level function
    /// definitions (inserted before the first function, like the Arduino builder) and
    /// emits <c>#line</c> directives so compiler diagnostics point at the .ino lines.
    /// Default arguments move to the prototype (blanked in the definition, keeping columns).
    /// </summary>
    public static class ArduinoSketchPreprocessor
    {
        private static readonly HashSet<string> NotFunctionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "while", "for", "switch", "catch", "return", "sizeof", "decltype", "alignof", "static_assert", "defined", "do", "else"
        };

        private static readonly HashSet<string> NotTypeStarts = new HashSet<string>(StringComparer.Ordinal)
        {
            "class", "struct", "union", "enum", "typedef", "using", "namespace", "template", "return", "else", "new", "delete",
            "throw", "goto", "case", "default", "public", "private", "protected", "friend", "operator", "extern"
        };

        public sealed class FunctionInfo
        {
            public string Name { get; set; } = string.Empty;
            public string Prototype { get; set; } = string.Empty;
            public int MergedLine { get; set; }
        }

        public sealed class Result
        {
            public string Code { get; set; } = string.Empty;
            public List<FunctionInfo> Functions { get; } = new List<FunctionInfo>();
        }

        public static Result Process(IReadOnlyList<SketchSourceFile> files)
        {
            if (files == null || files.Count == 0) throw new ArgumentException("No sketch source.", nameof(files));

            // Concatenate the tabs, remembering where every line came from.
            var lineFile = new List<int>();
            var lineNumber = new List<int>();
            var merged = new StringBuilder();
            for (int f = 0; f < files.Count; f++)
            {
                string text = files[f].Text.Replace("\r\n", "\n").Replace('\r', '\n');
                if (text.Length > 0 && text[text.Length - 1] != '\n') text += "\n";
                int line = 1;
                foreach (char ch in text)
                {
                    merged.Append(ch);
                    if (ch == '\n')
                    {
                        lineFile.Add(f);
                        lineNumber.Add(line);
                        line++;
                    }
                }
            }

            string source = merged.ToString();
            char[] original = source.ToCharArray();
            char[] noComments;
            char[] masked;
            Mask(source, out noComments, out masked);

            int[] lineStart = LineStarts(source);
            var result = new Result();
            var declared = new HashSet<string>(StringComparer.Ordinal);
            var definitions = new List<(int HeaderStart, int HeaderEnd, string Name, int ParenOpen, int ParenClose)>();

            int depth = 0;
            int stmtStart = 0;
            for (int i = 0; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == '{')
                {
                    if (depth == 0)
                    {
                        var fn = TryParseHeader(masked, stmtStart, i);
                        if (fn != null) definitions.Add((fn.Value.Start, i, fn.Value.Name, fn.Value.Open, fn.Value.Close));
                    }

                    depth++;
                }
                else if (c == '}')
                {
                    if (depth > 0) depth--;
                    if (depth == 0) stmtStart = i + 1;
                }
                else if (c == ';' && depth == 0)
                {
                    var decl = TryParseHeader(masked, stmtStart, i);
                    if (decl != null) declared.Add(decl.Value.Name);
                    stmtStart = i + 1;
                }
            }

            // Prototypes, and default arguments removed from the definitions.
            var prototypes = new List<(int Line, string Text)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in definitions)
            {
                string header = Collapse(new string(noComments, d.HeaderStart, d.ParenClose + 1 - d.HeaderStart));
                int defLine = LineOf(lineStart, d.HeaderStart);
                result.Functions.Add(new FunctionInfo { Name = d.Name, Prototype = header + ";", MergedLine = defLine });
                if (d.Name == "setup" || d.Name == "loop") continue;   // declared by Arduino.h
                if (declared.Contains(d.Name)) continue;              // the sketch declares it itself
                if (!seen.Add(header)) continue;
                // Defaults stay in the prototype only (C++ allows them once): blank them in the definition.
                BlankDefaultArguments(masked, original, d.ParenOpen, d.ParenClose);
                prototypes.Add((defLine, header + ";"));
            }

            string body = new string(original);
            int insertLine = definitions.Count > 0 ? LineOf(lineStart, definitions[0].HeaderStart) : -1;

            var sb = new StringBuilder();
            sb.Append("#include <Arduino.h>\n");
            string[] lines = body.Split('\n');
            int currentFile = -1;
            for (int ln = 0; ln < lineFile.Count; ln++)
            {
                if (ln == insertLine && prototypes.Count > 0)
                {
                    foreach (var p in prototypes)
                    {
                        AppendLineDirective(sb, files[lineFile[p.Line]].Path, lineNumber[p.Line]);
                        sb.Append(p.Text).Append('\n');
                    }

                    currentFile = -1;
                }

                if (currentFile != lineFile[ln] || ln == insertLine)
                {
                    AppendLineDirective(sb, files[lineFile[ln]].Path, lineNumber[ln]);
                    currentFile = lineFile[ln];
                }

                sb.Append(lines[ln]).Append('\n');
            }

            result.Code = sb.ToString();
            return result;
        }

        private static void AppendLineDirective(StringBuilder sb, string path, int line)
        {
            sb.Append("#line ").Append(line.ToString(CultureInfo.InvariantCulture)).Append(" \"")
              .Append(path.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"\n");
        }

        private static int[] LineStarts(string s)
        {
            var starts = new List<int> { 0 };
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\n') starts.Add(i + 1);
            }

            return starts.ToArray();
        }

        private static int LineOf(int[] starts, int pos)
        {
            int lo = 0, hi = starts.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (starts[mid] <= pos) lo = mid;
                else hi = mid - 1;
            }

            return lo;
        }

        private static string Collapse(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool space = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = sb.Length > 0;
                    continue;
                }

                if (space) sb.Append(' ');
                space = false;
                sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>
        /// noComments: comments replaced by spaces. masked: additionally string/char literal
        /// contents and whole preprocessor lines replaced by spaces. Newlines are kept in both.
        /// </summary>
        private static void Mask(string s, out char[] noComments, out char[] masked)
        {
            noComments = s.ToCharArray();
            masked = s.ToCharArray();
            int n = s.Length;
            int i = 0;
            bool lineStart = true;
            while (i < n)
            {
                char c = s[i];
                if (c == '\n')
                {
                    lineStart = true;
                    i++;
                    continue;
                }

                if (lineStart && (c == ' ' || c == '\t'))
                {
                    i++;
                    continue;
                }

                if (lineStart && c == '#')
                {
                    // Preprocessor directive (with continuation lines): invisible to the scanner.
                    while (i < n && s[i] != '\n')
                    {
                        if (s[i] == '\\' && i + 1 < n && s[i + 1] == '\n')
                        {
                            masked[i] = ' ';
                            i += 2;
                            continue;
                        }

                        if (s[i] == '/' && i + 1 < n && s[i + 1] == '*')
                        {
                            int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                            int stop = end < 0 ? n : end + 2;
                            for (int k = i; k < stop; k++)
                            {
                                if (s[k] != '\n') { masked[k] = ' '; noComments[k] = ' '; }
                            }

                            i = stop;
                            continue;
                        }

                        if (s[i] == '/' && i + 1 < n && s[i + 1] == '/')
                        {
                            while (i < n && s[i] != '\n')
                            {
                                masked[i] = ' ';
                                noComments[i] = ' ';
                                i++;
                            }

                            break;
                        }

                        masked[i] = ' ';
                        i++;
                    }

                    continue;
                }

                lineStart = false;
                if (c == '/' && i + 1 < n && s[i + 1] == '/')
                {
                    while (i < n && s[i] != '\n')
                    {
                        masked[i] = ' ';
                        noComments[i] = ' ';
                        i++;
                    }

                    continue;
                }

                if (c == '/' && i + 1 < n && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    int stop = end < 0 ? n : end + 2;
                    for (int k = i; k < stop; k++)
                    {
                        if (s[k] != '\n')
                        {
                            masked[k] = ' ';
                            noComments[k] = ' ';
                        }
                    }

                    i = stop;
                    continue;
                }

                if (c == 'R' && i + 1 < n && s[i + 1] == '"' && (i == 0 || !IsIdent(s[i - 1]) || s[i - 1] == 'u' || s[i - 1] == 'U' || s[i - 1] == 'L' || s[i - 1] == '8'))
                {
                    int open = s.IndexOf('(', i + 2);
                    if (open > 0)
                    {
                        string delim = ")" + s.Substring(i + 2, open - i - 2) + "\"";
                        int end = s.IndexOf(delim, open + 1, StringComparison.Ordinal);
                        int stop = end < 0 ? n : end + delim.Length;
                        for (int k = i + 2; k < stop - 1; k++)
                        {
                            if (s[k] != '\n') masked[k] = ' ';
                        }

                        i = stop;
                        continue;
                    }
                }

                if (c == '"' || (c == '\'' && !(i > 0 && char.IsDigit(s[i - 1]) && i + 1 < n && char.IsDigit(s[i + 1]))))
                {
                    char quote = c;
                    int k = i + 1;
                    while (k < n && s[k] != quote && s[k] != '\n')
                    {
                        if (s[k] == '\\' && k + 1 < n) k++;
                        k++;
                    }

                    for (int m = i + 1; m < k && m < n; m++)
                    {
                        if (s[m] != '\n') masked[m] = ' ';
                    }

                    i = Math.Min(n, k + 1);
                    continue;
                }

                i++;
            }
        }

        private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

        /// <summary>Parses "type name(params)" in masked[start..end). Returns null when it is not a free function header.</summary>
        private static (int Start, string Name, int Open, int Close)? TryParseHeader(char[] masked, int start, int end)
        {
            int e = end - 1;
            while (e >= start && char.IsWhiteSpace(masked[e])) e--;
            if (e < start) return null;

            // Allow trailing qualifiers that are legal on free functions.
            string tail = TrailingWord(masked, start, e, out int wordStart);
            while (tail == "noexcept" || tail == "IRAM_ATTR" || tail == "ARDUINO_ISR_ATTR")
            {
                e = wordStart - 1;
                while (e >= start && char.IsWhiteSpace(masked[e])) e--;
                if (e < start) return null;
                tail = TrailingWord(masked, start, e, out wordStart);
            }

            if (masked[e] != ')') return null;

            // Matching '(' of the final ')'.
            int depth = 0;
            int open = -1;
            for (int k = e; k >= start; k--)
            {
                char ch = masked[k];
                if (ch == ')') depth++;
                else if (ch == '(')
                {
                    depth--;
                    if (depth == 0)
                    {
                        open = k;
                        break;
                    }
                }
            }

            if (open < 0) return null;
            int ne = open - 1;
            while (ne >= start && char.IsWhiteSpace(masked[ne])) ne--;
            if (ne < start || !IsIdent(masked[ne])) return null;
            int ns = ne;
            while (ns - 1 >= start && IsIdent(masked[ns - 1])) ns--;
            string name = new string(masked, ns, ne - ns + 1);
            if (char.IsDigit(name[0]) || NotFunctionNames.Contains(name)) return null;

            // Return type part: identifiers, '*', '&', '::' (qualified type names only), '<', '>', ',' and spaces.
            int ts = start;
            while (ts < ns && char.IsWhiteSpace(masked[ts])) ts++;
            if (ts >= ns) return null;
            for (int k = ts; k < ns; k++)
            {
                char ch = masked[k];
                if (!(IsIdent(ch) || char.IsWhiteSpace(ch) || ch == '*' || ch == '&' || ch == ':' || ch == '<' || ch == '>' || ch == ',')) return null;
            }

            // "Foo::bar(" is a method definition, not a free function.
            int before = ns - 1;
            while (before >= ts && char.IsWhiteSpace(masked[before])) before--;
            if (before >= ts && masked[before] == ':') return null;

            string firstWord = LeadingWord(masked, ts, ns);
            if (NotTypeStarts.Contains(firstWord)) return null;

            // Parameter list must not contain statement punctuation.
            for (int k = open + 1; k < e; k++)
            {
                char ch = masked[k];
                if (ch == ';' || ch == '{' || ch == '}') return null;
            }

            return (ts, name, open, e);
        }

        private static string LeadingWord(char[] masked, int start, int end)
        {
            int k = start;
            while (k < end && IsIdent(masked[k])) k++;
            return new string(masked, start, k - start);
        }

        private static string TrailingWord(char[] masked, int start, int e, out int wordStart)
        {
            int k = e;
            while (k >= start && IsIdent(masked[k])) k--;
            wordStart = k + 1;
            return wordStart <= e ? new string(masked, wordStart, e - wordStart + 1) : string.Empty;
        }

        /// <summary>Blanks "= value" of every parameter in original[open..close]. Returns true if any default was found.</summary>
        private static bool BlankDefaultArguments(char[] masked, char[] original, int open, int close)
        {
            bool any = false;
            int depth = 0;
            int angle = 0;
            int eq = -1;
            for (int k = open + 1; k <= close; k++)
            {
                char ch = masked[k];
                bool atEnd = k == close;
                if (!atEnd)
                {
                    if (ch == '(' || ch == '[' || ch == '{') depth++;
                    else if (ch == ')' || ch == ']' || ch == '}') depth--;
                    else if (ch == '<' && k > 0 && IsIdent(PrevNonSpace(masked, k))) angle++;
                    else if (ch == '>' && angle > 0) angle--;
                }

                if (atEnd || (ch == ',' && depth == 0 && angle == 0))
                {
                    if (eq >= 0)
                    {
                        for (int m = eq; m < k; m++)
                        {
                            if (original[m] != '\n') original[m] = ' ';
                        }

                        any = true;
                    }

                    eq = -1;
                    continue;
                }

                if (ch == '=' && depth == 0 && angle == 0 && eq < 0)
                {
                    char next = k + 1 < masked.Length ? masked[k + 1] : ' ';
                    char prev = masked[k - 1];
                    if (next != '=' && prev != '=' && prev != '!' && prev != '<' && prev != '>') eq = k;
                }
            }

            return any;
        }

        private static char PrevNonSpace(char[] masked, int k)
        {
            for (int m = k - 1; m >= 0; m--)
            {
                if (!char.IsWhiteSpace(masked[m])) return masked[m];
            }

            return ' ';
        }
    }
}
