using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// A small Markdown parser (CommonMark/GFM subset) producing a plain object tree.
// No WPF here: rendering lives in MarkdownView.cs; this file is linked into the unit tests.
namespace RobotWin.Studio.Help
{
    // ------------------------------------------------------------------ blocks

    public abstract class MdBlock
    {
    }

    public sealed class MdHeading : MdBlock
    {
        public int Level { get; set; }
        public List<MdInline> Content { get; set; } = new List<MdInline>();

        /// <summary>GitHub-style anchor ("kurulum-ve-ilk-adimlar" style slug), unique within the document.</summary>
        public string Anchor { get; set; } = string.Empty;

        public string Text => MdInline.PlainText(Content);
    }

    public sealed class MdParagraph : MdBlock
    {
        public List<MdInline> Content { get; set; } = new List<MdInline>();
    }

    public sealed class MdCodeBlock : MdBlock
    {
        public string Language { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
    }

    public sealed class MdList : MdBlock
    {
        public bool Ordered { get; set; }
        public int Start { get; set; } = 1;
        public List<MdListItem> Items { get; set; } = new List<MdListItem>();
    }

    public sealed class MdListItem
    {
        public List<MdBlock> Blocks { get; set; } = new List<MdBlock>();

        /// <summary>GFM task list: null = plain item, true/false = [x] / [ ].</summary>
        public bool? Checked { get; set; }
    }

    public enum MdAlign
    {
        None,
        Left,
        Center,
        Right
    }

    public sealed class MdTable : MdBlock
    {
        public List<List<MdInline>> Header { get; set; } = new List<List<MdInline>>();
        public List<MdAlign> Align { get; set; } = new List<MdAlign>();
        public List<List<List<MdInline>>> Rows { get; set; } = new List<List<List<MdInline>>>();
    }

    public sealed class MdQuote : MdBlock
    {
        public List<MdBlock> Blocks { get; set; } = new List<MdBlock>();
    }

    public sealed class MdRule : MdBlock
    {
    }

    // ------------------------------------------------------------------ inlines

    public abstract class MdInline
    {
        public static string PlainText(IEnumerable<MdInline> inlines)
        {
            var sb = new StringBuilder();
            foreach (var i in inlines) i.AppendText(sb);
            return sb.ToString();
        }

        internal abstract void AppendText(StringBuilder sb);
    }

    public sealed class MdText : MdInline
    {
        public MdText(string text) => Text = text;
        public string Text { get; set; }
        internal override void AppendText(StringBuilder sb) => sb.Append(Text);
    }

    public enum MdStyle
    {
        Bold,
        Italic,
        Strike
    }

    public sealed class MdStyled : MdInline
    {
        public MdStyle Style { get; set; }
        public List<MdInline> Children { get; set; } = new List<MdInline>();
        internal override void AppendText(StringBuilder sb) => sb.Append(PlainText(Children));
    }

    public sealed class MdCode : MdInline
    {
        public MdCode(string code) => Code = code;
        public string Code { get; }
        internal override void AppendText(StringBuilder sb) => sb.Append(Code);
    }

    public sealed class MdLink : MdInline
    {
        public string Url { get; set; } = string.Empty;
        public List<MdInline> Children { get; set; } = new List<MdInline>();
        internal override void AppendText(StringBuilder sb) => sb.Append(PlainText(Children));
    }

    public sealed class MdImage : MdInline
    {
        public string Source { get; set; } = string.Empty;
        public string Alt { get; set; } = string.Empty;
        internal override void AppendText(StringBuilder sb) => sb.Append(Alt);
    }

    public sealed class MdLineBreak : MdInline
    {
        internal override void AppendText(StringBuilder sb) => sb.Append('\n');
    }

    // ------------------------------------------------------------------ document

    public sealed class MarkdownDocument
    {
        public List<MdBlock> Blocks { get; } = new List<MdBlock>();

        public IEnumerable<MdHeading> Headings => Walk(Blocks).OfType<MdHeading>();

        /// <summary>First level-1 heading (the page title), if any.</summary>
        public string? Title => Headings.FirstOrDefault(h => h.Level == 1)?.Text;

        /// <summary>All text, one block per line (for search).</summary>
        public string PlainText()
        {
            var sb = new StringBuilder();
            foreach (var b in Walk(Blocks))
            {
                switch (b)
                {
                    case MdHeading h:
                        sb.AppendLine(h.Text);
                        break;
                    case MdParagraph p:
                        sb.AppendLine(MdInline.PlainText(p.Content));
                        break;
                    case MdCodeBlock c:
                        sb.AppendLine(c.Code);
                        break;
                    case MdTable t:
                        sb.AppendLine(string.Join("  ", t.Header.Select(MdInline.PlainText)));
                        foreach (var row in t.Rows) sb.AppendLine(string.Join("  ", row.Select(MdInline.PlainText)));
                        break;
                }
            }

            return sb.ToString();
        }

        public static IEnumerable<MdBlock> Walk(IEnumerable<MdBlock> blocks)
        {
            foreach (var b in blocks)
            {
                yield return b;
                IEnumerable<MdBlock>? inner = b switch
                {
                    MdQuote q => q.Blocks,
                    MdList l => l.Items.SelectMany(i => i.Blocks),
                    _ => null
                };
                if (inner == null) continue;
                foreach (var x in Walk(inner)) yield return x;
            }
        }
    }

    public static class MarkdownParser
    {
        private static readonly Regex Atx = new Regex(@"^ {0,3}(#{1,6})(?:[ \t]+(.*?))?(?:[ \t]+#+)?[ \t]*$");
        private static readonly Regex Fence = new Regex(@"^( {0,3})(`{3,}|~{3,})\s*([^`\s]*)");
        private static readonly Regex Rule = new Regex(@"^ {0,3}((\*[ \t]*){3,}|(-[ \t]*){3,}|(_[ \t]*){3,})$");
        private static readonly Regex Bullet = new Regex(@"^( {0,12})([-*+]|\d{1,9}[.)])([ \t]+|$)(.*)$");
        private static readonly Regex TableSep = new Regex(@"^\s*\|?\s*:?-+:?\s*(\|\s*:?-+:?\s*)*\|?\s*$");
        private static readonly Regex HtmlComment = new Regex(@"<!--.*?-->", RegexOptions.Singleline);
        private static readonly Regex HtmlImg = new Regex(@"<img\s[^>]*?src\s*=\s*[""']([^""']+)[""'][^>]*?(?:alt\s*=\s*[""']([^""']*)[""'])?[^>]*>", RegexOptions.IgnoreCase);

        public static MarkdownDocument Parse(string? markdown)
        {
            var doc = new MarkdownDocument();
            string text = HtmlComment.Replace((markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\t", "    "), string.Empty);
            // A UTF-8 byte order mark read as text.
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            doc.Blocks.AddRange(ParseBlocks(text.Split('\n').ToList()));
            AssignAnchors(doc);
            return doc;
        }

        private static void AssignAnchors(MarkdownDocument doc)
        {
            var used = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var h in doc.Headings)
            {
                string slug = Slug(h.Text);
                if (used.TryGetValue(slug, out int n))
                {
                    used[slug] = n + 1;
                    slug = slug + "-" + (n + 1).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    used[slug] = 0;
                }

                h.Anchor = slug;
            }
        }

        /// <summary>GitHub's heading anchor: lower case, punctuation removed, spaces to dashes.</summary>
        public static string Slug(string heading)
        {
            var sb = new StringBuilder();
            foreach (char c in heading.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR")))
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('-');
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ blocks

        private static List<MdBlock> ParseBlocks(List<string> lines)
        {
            var blocks = new List<MdBlock>();
            int i = 0;
            while (i < lines.Count)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                var fence = Fence.Match(line);
                if (fence.Success)
                {
                    string marker = fence.Groups[2].Value;
                    int indent = fence.Groups[1].Value.Length;
                    var code = new List<string>();
                    i++;
                    while (i < lines.Count && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
                    {
                        string l = lines[i];
                        int strip = 0;
                        while (strip < indent && strip < l.Length && l[strip] == ' ') strip++;
                        code.Add(l.Substring(strip));
                        i++;
                    }

                    i++; // closing fence
                    blocks.Add(new MdCodeBlock { Language = fence.Groups[3].Value, Code = string.Join("\n", code) });
                    continue;
                }

                var atx = Atx.Match(line);
                if (atx.Success)
                {
                    blocks.Add(new MdHeading { Level = atx.Groups[1].Value.Length, Content = ParseInlines(atx.Groups[2].Value.Trim()) });
                    i++;
                    continue;
                }

                if (Rule.IsMatch(line))
                {
                    blocks.Add(new MdRule());
                    i++;
                    continue;
                }

                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    var inner = new List<string>();
                    while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        string l = lines[i].TrimStart();
                        if (l.StartsWith(">", StringComparison.Ordinal))
                        {
                            l = l.Substring(1);
                            if (l.StartsWith(" ", StringComparison.Ordinal)) l = l.Substring(1);
                        }
                        else if (StartsBlock(lines[i]))
                        {
                            break;
                        }

                        inner.Add(l);
                        i++;
                    }

                    blocks.Add(new MdQuote { Blocks = ParseBlocks(inner) });
                    continue;
                }

                if (i + 1 < lines.Count && line.Contains('|') && TableSep.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
                {
                    var header = SplitRow(line);
                    var aligns = SplitRow(lines[i + 1]).Select(ParseAlign).ToList();
                    var table = new MdTable { Header = header.Select(ParseInlines).ToList(), Align = aligns };
                    i += 2;
                    while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]) && lines[i].Contains('|'))
                    {
                        var cells = SplitRow(lines[i]);
                        while (cells.Count < header.Count) cells.Add(string.Empty);
                        table.Rows.Add(cells.Take(Math.Max(header.Count, 1)).Select(ParseInlines).ToList());
                        i++;
                    }

                    while (table.Align.Count < table.Header.Count) table.Align.Add(MdAlign.None);
                    blocks.Add(table);
                    continue;
                }

                var bullet = Bullet.Match(line);
                if (bullet.Success && bullet.Groups[1].Value.Length <= 3)
                {
                    blocks.Add(ParseList(lines, ref i));
                    continue;
                }

                // Paragraph (with setext heading underline support).
                var para = new List<string>();
                while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]))
                {
                    string t = lines[i].Trim();
                    if (para.Count > 0 && (Regex.IsMatch(t, @"^=+$") || Regex.IsMatch(t, @"^-+$")))
                    {
                        blocks.Add(new MdHeading { Level = t[0] == '=' ? 1 : 2, Content = ParseInlines(string.Join(" ", para.Select(p => p.Trim()))) });
                        para.Clear();
                        i++;
                        break;
                    }

                    if (para.Count > 0 && (StartsBlock(lines[i]) || (Bullet.Match(lines[i]) is { Success: true } b && b.Groups[4].Value.Length > 0 && b.Groups[1].Value.Length <= 3)))
                        break;
                    if (para.Count > 0 && i + 1 < lines.Count && lines[i].Contains('|') && TableSep.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
                        break;
                    para.Add(lines[i]);
                    i++;
                }

                if (para.Count > 0) blocks.Add(new MdParagraph { Content = ParseInlines(JoinParagraph(para)) });
            }

            return blocks;
        }

        private static bool StartsBlock(string line) =>
            Fence.IsMatch(line) || Atx.IsMatch(line) || Rule.IsMatch(line) || line.TrimStart().StartsWith(">", StringComparison.Ordinal);

        /// <summary>Joins paragraph lines; "two spaces" or a backslash at the end of a line is a hard break.</summary>
        private static string JoinParagraph(List<string> lines)
        {
            var sb = new StringBuilder();
            for (int k = 0; k < lines.Count; k++)
            {
                string l = lines[k];
                bool hard = l.EndsWith("  ", StringComparison.Ordinal) || (l.EndsWith("\\", StringComparison.Ordinal) && !l.EndsWith("\\\\", StringComparison.Ordinal));
                string t = l.Trim();
                if (hard && t.EndsWith("\\", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);
                sb.Append(t);
                if (k < lines.Count - 1) sb.Append(hard ? "\n" : " ");
            }

            return sb.ToString();
        }

        private static MdList ParseList(List<string> lines, ref int i)
        {
            var first = Bullet.Match(lines[i]);
            bool ordered = char.IsDigit(first.Groups[2].Value[0]);
            char kind = ordered ? first.Groups[2].Value[^1] : first.Groups[2].Value[0];
            int baseIndent = first.Groups[1].Value.Length;
            var list = new MdList { Ordered = ordered };
            if (ordered) list.Start = int.Parse(first.Groups[2].Value.TrimEnd('.', ')'), CultureInfo.InvariantCulture);

            while (i < lines.Count)
            {
                var m = Bullet.Match(lines[i]);
                if (!m.Success || m.Groups[1].Value.Length != baseIndent) break;
                bool o = char.IsDigit(m.Groups[2].Value[0]);
                char k = o ? m.Groups[2].Value[^1] : m.Groups[2].Value[0];
                if (o != ordered || k != kind) break;

                int contentIndent = baseIndent + m.Groups[2].Value.Length + Math.Max(1, Math.Min(4, m.Groups[3].Value.Length));
                var itemLines = new List<string> { m.Groups[4].Value };
                i++;
                while (i < lines.Count)
                {
                    string l = lines[i];
                    if (string.IsNullOrWhiteSpace(l))
                    {
                        // A blank line continues the item only when indented content follows.
                        int j = i + 1;
                        while (j < lines.Count && string.IsNullOrWhiteSpace(lines[j])) j++;
                        if (j < lines.Count && Indent(lines[j]) >= contentIndent)
                        {
                            for (; i < j; i++) itemLines.Add(string.Empty);
                            continue;
                        }

                        break;
                    }

                    int ind = Indent(l);
                    var nb = Bullet.Match(l);
                    if (nb.Success && nb.Groups[1].Value.Length <= baseIndent) break;   // next item / other list
                    if (ind >= contentIndent || (nb.Success && ind > baseIndent))
                    {
                        itemLines.Add(ind >= contentIndent ? l.Substring(contentIndent) : l.TrimStart());
                    }
                    else if (ind > baseIndent || !StartsBlock(l))
                    {
                        itemLines.Add(l.TrimStart());                                   // lazy continuation
                    }
                    else
                    {
                        break;
                    }

                    i++;
                }

                var item = new MdListItem();
                var task = Regex.Match(itemLines[0], @"^\[([ xX])\][ \t]+(.*)$");
                if (task.Success)
                {
                    item.Checked = task.Groups[1].Value != " ";
                    itemLines[0] = task.Groups[2].Value;
                }

                item.Blocks = ParseBlocks(itemLines);
                list.Items.Add(item);

                // Blank lines between items keep the list going.
                int next = i;
                while (next < lines.Count && string.IsNullOrWhiteSpace(lines[next])) next++;
                if (next > i && next < lines.Count && Bullet.Match(lines[next]) is { Success: true } nm && nm.Groups[1].Value.Length == baseIndent) i = next;
            }

            return list;
        }

        private static int Indent(string l)
        {
            int n = 0;
            while (n < l.Length && l[n] == ' ') n++;
            return n;
        }

        private static List<string> SplitRow(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|", StringComparison.Ordinal)) t = t.Substring(1);
            if (t.EndsWith("|", StringComparison.Ordinal) && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t.Substring(0, t.Length - 1);
            var cells = new List<string>();
            var sb = new StringBuilder();
            bool code = false;
            for (int k = 0; k < t.Length; k++)
            {
                char c = t[k];
                if (c == '\\' && k + 1 < t.Length && t[k + 1] == '|')
                {
                    sb.Append('|');
                    k++;
                    continue;
                }

                if (c == '`') code = !code;
                if (c == '|' && !code)
                {
                    cells.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }

            cells.Add(sb.ToString().Trim());
            return cells;
        }

        private static MdAlign ParseAlign(string cell)
        {
            string c = cell.Trim();
            bool l = c.StartsWith(":", StringComparison.Ordinal), r = c.EndsWith(":", StringComparison.Ordinal);
            return l && r ? MdAlign.Center : r ? MdAlign.Right : l ? MdAlign.Left : MdAlign.None;
        }

        // ------------------------------------------------------------------ inlines

        private const string Escapable = "\\`*_{}[]()#+-.!|~<>\"'";

        public static List<MdInline> ParseInlines(string text)
        {
            // Standalone HTML images (<img src="…" alt="…">) become Markdown images.
            text = HtmlImg.Replace(text, m => $"![{m.Groups[2].Value}]({m.Groups[1].Value})");
            var result = new List<MdInline>();
            var sb = new StringBuilder();

            void Flush()
            {
                if (sb.Length == 0) return;
                AddText(result, sb.ToString());
                sb.Clear();
            }

            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length && Escapable.IndexOf(text[i + 1]) >= 0)
                {
                    sb.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '\n')
                {
                    Flush();
                    result.Add(new MdLineBreak());
                    i++;
                    continue;
                }

                if (c == '`')
                {
                    int run = Run(text, i, '`');
                    int close = FindRun(text, i + run, '`', run);
                    if (close > 0)
                    {
                        Flush();
                        string code = text.Substring(i + run, close - i - run);
                        if (code.Length > 1 && code[0] == ' ' && code[^1] == ' ') code = code.Substring(1, code.Length - 2);
                        result.Add(new MdCode(code));
                        i = close + run;
                        continue;
                    }

                    sb.Append(text, i, run);
                    i += run;
                    continue;
                }

                if ((c == '!' && i + 1 < text.Length && text[i + 1] == '[') || c == '[')
                {
                    bool image = c == '!';
                    int open = image ? i + 1 : i;
                    if (TryLink(text, open, out string label, out string url, out int end))
                    {
                        Flush();
                        if (image)
                        {
                            result.Add(new MdImage { Alt = label, Source = url });
                        }
                        else
                        {
                            // [![badge](img)](link): keep the image inside the link.
                            result.Add(new MdLink { Url = url, Children = ParseInlines(label) });
                        }

                        i = end;
                        continue;
                    }
                }

                if (c == '<')
                {
                    int gt = text.IndexOf('>', i + 1);
                    if (gt > i)
                    {
                        string inner = text.Substring(i + 1, gt - i - 1);
                        if (Regex.IsMatch(inner, @"^(https?://|mailto:)[^\s<>]+$", RegexOptions.IgnoreCase))
                        {
                            Flush();
                            result.Add(new MdLink { Url = inner, Children = new List<MdInline> { new MdText(inner) } });
                            i = gt + 1;
                            continue;
                        }

                        if (Regex.IsMatch(inner, @"^/?[a-zA-Z][a-zA-Z0-9]*(\s[^<>]*)?/?$"))
                        {
                            // Inline HTML: <br> is a line break, other tags are dropped (their text stays).
                            Flush();
                            if (Regex.IsMatch(inner, @"^br\s*/?$", RegexOptions.IgnoreCase)) result.Add(new MdLineBreak());
                            i = gt + 1;
                            continue;
                        }
                    }
                }

                if (c == '*' || c == '_' || (c == '~' && i + 1 < text.Length && text[i + 1] == '~'))
                {
                    int run = Run(text, i, c);
                    if (c == '~') run = 2;
                    bool leftFlanking = i + run < text.Length && !char.IsWhiteSpace(text[i + run]);
                    bool intraword = c == '_' && i > 0 && char.IsLetterOrDigit(text[i - 1]);
                    if (leftFlanking && !intraword && run <= 3)
                    {
                        int close = FindClosingDelimiter(text, i + run, c, run);
                        if (close > 0)
                        {
                            Flush();
                            var inner = ParseInlines(text.Substring(i + run, close - i - run));
                            MdInline node;
                            if (c == '~') node = new MdStyled { Style = MdStyle.Strike, Children = inner };
                            else if (run == 1) node = new MdStyled { Style = MdStyle.Italic, Children = inner };
                            else if (run == 2) node = new MdStyled { Style = MdStyle.Bold, Children = inner };
                            else node = new MdStyled { Style = MdStyle.Bold, Children = new List<MdInline> { new MdStyled { Style = MdStyle.Italic, Children = inner } } };
                            result.Add(node);
                            i = close + run;
                            continue;
                        }
                    }

                    sb.Append(text, i, run);
                    i += run;
                    continue;
                }

                sb.Append(c);
                i++;
            }

            Flush();
            return result;
        }

        private static readonly Regex BareUrl = new Regex(@"https?://[^\s<>()\[\]`]+[^\s<>()\[\]`.,;:!?'""]", RegexOptions.IgnoreCase);

        /// <summary>Adds plain text, turning bare URLs into links (GFM autolinks).</summary>
        private static void AddText(List<MdInline> into, string text)
        {
            int at = 0;
            foreach (Match m in BareUrl.Matches(text))
            {
                if (m.Index > at) into.Add(new MdText(text.Substring(at, m.Index - at)));
                into.Add(new MdLink { Url = m.Value, Children = new List<MdInline> { new MdText(m.Value) } });
                at = m.Index + m.Length;
            }

            if (at < text.Length) into.Add(new MdText(text.Substring(at)));
        }

        private static int Run(string s, int i, char c)
        {
            int n = 0;
            while (i + n < s.Length && s[i + n] == c) n++;
            return n;
        }

        private static int FindRun(string s, int from, char c, int length)
        {
            for (int k = from; k < s.Length; k++)
            {
                if (s[k] != c) continue;
                int run = Run(s, k, c);
                if (run == length) return k;
                k += run - 1;
            }

            return -1;
        }

        private static int FindClosingDelimiter(string s, int from, char c, int length)
        {
            for (int k = from; k < s.Length; k++)
            {
                if (s[k] == '\\')
                {
                    k++;
                    continue;
                }

                if (s[k] == '`')
                {
                    int run = Run(s, k, '`');
                    int close = FindRun(s, k + run, '`', run);
                    if (close > 0)
                    {
                        k = close + run - 1;
                        continue;
                    }
                }

                if (s[k] != c) continue;
                int r = c == '~' ? (Run(s, k, c) >= 2 ? 2 : 1) : Run(s, k, c);
                bool rightFlanking = k > from && !char.IsWhiteSpace(s[k - 1]);
                bool intraword = c == '_' && k + r < s.Length && char.IsLetterOrDigit(s[k + r]);
                if (r == length && rightFlanking && !intraword) return k;
                if (r > length && rightFlanking && !intraword && length < 3)
                {
                    // "**bold *and italic***": the closer is the trailing part of a longer run.
                    return k + r - length;
                }

                k += r - 1;
            }

            return -1;
        }

        /// <summary>[label](url "title") starting at <paramref name="open"/> ('[').</summary>
        private static bool TryLink(string s, int open, out string label, out string url, out int end)
        {
            label = url = string.Empty;
            end = open;
            int depth = 0, k = open;
            for (; k < s.Length; k++)
            {
                if (s[k] == '\\')
                {
                    k++;
                    continue;
                }

                if (s[k] == '[') depth++;
                else if (s[k] == ']' && --depth == 0) break;
            }

            if (k >= s.Length || k + 1 >= s.Length || s[k + 1] != '(') return false;
            int close = -1, paren = 0;
            for (int j = k + 2; j < s.Length; j++)
            {
                if (s[j] == '(') paren++;
                else if (s[j] == ')')
                {
                    if (paren == 0)
                    {
                        close = j;
                        break;
                    }

                    paren--;
                }
            }

            if (close < 0) return false;
            label = s.Substring(open + 1, k - open - 1);
            string dest = s.Substring(k + 2, close - k - 2).Trim();
            // Drop an optional "title".
            var t = Regex.Match(dest, @"^(\S+)\s+[""'(].*[""')]$");
            if (t.Success) dest = t.Groups[1].Value;
            if (dest.StartsWith("<", StringComparison.Ordinal) && dest.EndsWith(">", StringComparison.Ordinal)) dest = dest.Substring(1, dest.Length - 2);
            url = dest;
            end = close + 1;
            return true;
        }
    }

    /// <summary>A search hit inside a help page.</summary>
    public sealed class HelpSearchHit
    {
        public string TopicId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Snippet { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public static class HelpSearch
    {
        /// <summary>Case- and accent-tolerant (Turkish ı/i, ş/s …) occurrence count and a snippet around the first hit.</summary>
        public static HelpSearchHit? Match(string topicId, string title, string text, string query)
        {
            query = query.Trim();
            if (query.Length < 2) return null;
            string nt = Fold(text), nq = Fold(query);
            int first = nt.IndexOf(nq, StringComparison.Ordinal);
            bool inTitle = Fold(title).Contains(nq, StringComparison.Ordinal);
            if (first < 0 && !inTitle) return null;
            int count = 0;
            for (int at = first; at >= 0; at = nt.IndexOf(nq, at + nq.Length, StringComparison.Ordinal)) count++;
            string snippet = string.Empty;
            if (first >= 0)
            {
                int from = Math.Max(0, first - 50), to = Math.Min(text.Length, first + nq.Length + 70);
                snippet = (from > 0 ? "…" : string.Empty) + Regex.Replace(text.Substring(from, to - from), @"\s+", " ").Trim() + (to < text.Length ? "…" : string.Empty);
            }

            return new HelpSearchHit { TopicId = topicId, Title = title, Snippet = snippet, Count = count + (inTitle ? 1 : 0) };
        }

        /// <summary>Lower case without diacritics, length-preserving (so indices map back to the original text).</summary>
        public static string Fold(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                char c = ch switch
                {
                    'ı' or 'I' or 'İ' or 'i' => 'i',
                    'ş' or 'Ş' => 's',
                    'ğ' or 'Ğ' => 'g',
                    'ü' or 'Ü' => 'u',
                    'ö' or 'Ö' => 'o',
                    'ç' or 'Ç' => 'c',
                    'â' or 'Â' => 'a',
                    'î' or 'Î' => 'i',
                    'û' or 'Û' => 'u',
                    _ => char.ToLowerInvariant(ch)
                };
                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
