using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>
    /// A node of a KiCad-style s-expression: either an atom (a bare token or a quoted string)
    /// or a list. Lists usually start with a bare head token: <c>(pad "1" smd rect ...)</c>.
    ///
    /// <see cref="Parse"/> is deliberately tolerant so that hand-edited and third-party files
    /// still load: a byte-order mark, unbalanced trailing parentheses, an unterminated string,
    /// text after the root list and <c>#</c> comment lines (KiCad 4/5 era) are accepted.
    /// Quoted strings understand KiCad's escapes (\" \\ \n \r \t) plus the usual C escapes;
    /// an unknown escape keeps the character after the backslash.
    /// </summary>
    public sealed class SExpression
    {
        private SExpression(string? atom, bool quoted)
        {
            Atom = atom;
            IsQuoted = quoted;
        }

        /// <summary>Atom text (unescaped for quoted strings); null for a list.</summary>
        public string? Atom { get; }

        /// <summary>True when the atom was written as a quoted string.</summary>
        public bool IsQuoted { get; }

        public List<SExpression> Items { get; } = new List<SExpression>();

        public bool IsList => Atom == null;

        /// <summary>First item of a list when it is an unquoted atom ("pad", "fp_line" ...), else "".</summary>
        public string Head => IsList && Items.Count > 0 && Items[0].Atom != null && !Items[0].IsQuoted ? Items[0].Atom! : string.Empty;

        public int Count => Items.Count;

        public static SExpression List(params SExpression[] items)
        {
            var list = new SExpression(null, false);
            list.Items.AddRange(items);
            return list;
        }

        public static SExpression Token(string text, bool quoted = false) => new SExpression(text ?? string.Empty, quoted);

        /// <summary>Child lists with the given head.</summary>
        public IEnumerable<SExpression> Children(string head) =>
            Items.Where(i => i.IsList && string.Equals(i.Head, head, StringComparison.Ordinal));

        /// <summary>Child lists whose head is any of the given names.</summary>
        public IEnumerable<SExpression> Children(params string[] heads) =>
            Items.Where(i => i.IsList && Array.IndexOf(heads, i.Head) >= 0);

        public SExpression? Child(string head) => Children(head).FirstOrDefault();

        /// <summary>Every list below this one (depth first) with the given head.</summary>
        public IEnumerable<SExpression> Descendants(string head)
        {
            foreach (var item in Items)
            {
                if (!item.IsList) continue;
                if (item.Head == head) yield return item;
                foreach (var d in item.Descendants(head)) yield return d;
            }
        }

        /// <summary>Atom at <paramref name="index"/> (0 = head), or null when missing or a list.</summary>
        public string? Text(int index) => index >= 0 && index < Items.Count ? Items[index].Atom : null;

        /// <summary>Numeric atom at <paramref name="index"/>, or <paramref name="fallback"/>.</summary>
        public double Number(int index, double fallback = 0.0) =>
            TryNumber(Text(index), out double v) ? v : fallback;

        /// <summary>Atoms after the head (both quoted and bare).</summary>
        public IEnumerable<string> Atoms() => Items.Skip(1).Where(i => !i.IsList).Select(i => i.Atom!);

        /// <summary>Numeric atoms after the head, skipping non-numeric tokens (e.g. "oval").</summary>
        public List<double> Numbers()
        {
            var result = new List<double>();
            for (int i = 1; i < Items.Count; i++)
            {
                if (TryNumber(Items[i].Atom, out double v)) result.Add(v);
            }

            return result;
        }

        /// <summary>Text of the first atom of child <c>(head value)</c>, e.g. ChildText("layer") → "F.Cu".</summary>
        public string? ChildText(string head, int index = 1) => Child(head)?.Text(index);

        /// <summary>
        /// A boolean property in any KiCad spelling: a bare flag atom (<c>hide</c>, KiCad 6/7),
        /// a list <c>(hide)</c> or <c>(hide yes)</c> (KiCad 8), with "no" meaning false.
        /// </summary>
        public bool Flag(string name)
        {
            for (int i = 1; i < Items.Count; i++)
            {
                var item = Items[i];
                if (!item.IsList)
                {
                    if (!item.IsQuoted && string.Equals(item.Atom, name, StringComparison.Ordinal)) return true;
                    continue;
                }

                if (item.Head != name) continue;
                string? v = item.Text(1);
                return v == null || !string.Equals(v, "no", StringComparison.OrdinalIgnoreCase) && !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public static bool TryNumber(string? text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        public override string ToString()
        {
            if (!IsList) return IsQuoted ? "\"" + Atom!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : Atom!;
            return "(" + string.Join(" ", Items.Select(i => i.ToString())) + ")";
        }

        // ------------------------------------------------------------------ parsing

        /// <summary>Parses the first top-level list of <paramref name="text"/>.</summary>
        /// <exception cref="FormatException">The text contains no list at all.</exception>
        public static SExpression Parse(string text)
        {
            var all = ParseAll(text);
            foreach (var node in all)
            {
                if (node.IsList) return node;
            }

            throw new FormatException("No s-expression list found.");
        }

        /// <summary>Parses every top-level item (lists and stray atoms).</summary>
        public static List<SExpression> ParseAll(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var roots = new List<SExpression>();
            var stack = new Stack<SExpression>();
            int i = 0, n = text.Length;
            bool lineStart = true;

            void AddNode(SExpression node)
            {
                if (stack.Count > 0) stack.Peek().Items.Add(node);
                else roots.Add(node);
            }

            while (i < n)
            {
                char c = text[i];
                if (c == '﻿' || char.IsWhiteSpace(c))
                {
                    if (c == '\n') lineStart = true;
                    i++;
                    continue;
                }

                if (c == '#' && lineStart && stack.Count == 0)
                {
                    // Comment line outside any list (old KiCad library headers).
                    while (i < n && text[i] != '\n') i++;
                    continue;
                }

                lineStart = false;
                if (c == '(')
                {
                    var list = new SExpression(null, false);
                    AddNode(list);
                    stack.Push(list);
                    i++;
                }
                else if (c == ')')
                {
                    if (stack.Count > 0) stack.Pop();   // a stray ')' is ignored
                    i++;
                }
                else if (c == '"')
                {
                    AddNode(new SExpression(ReadQuoted(text, ref i), true));
                }
                else
                {
                    int start = i;
                    while (i < n && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')' && text[i] != '"') i++;
                    AddNode(new SExpression(text.Substring(start, i - start), false));
                }
            }

            return roots;   // unclosed lists are implicitly closed at the end of the text
        }

        private static string ReadQuoted(string text, ref int i)
        {
            var sb = new StringBuilder();
            int n = text.Length;
            i++;   // opening quote
            while (i < n)
            {
                char c = text[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\' || i >= n)
                {
                    sb.Append(c);
                    continue;
                }

                char e = text[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'a': sb.Append('\a'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'x':
                    {
                        int start = i, value = 0;
                        while (i < n && i - start < 2 && Uri.IsHexDigit(text[i])) value = value * 16 + Convert.ToInt32(text[i++].ToString(), 16);
                        if (i == start) sb.Append('x');
                        else sb.Append((char)value);
                        break;
                    }

                    default:
                        if (e >= '0' && e <= '7')
                        {
                            int value = e - '0', digits = 1;
                            while (i < n && digits < 3 && text[i] >= '0' && text[i] <= '7')
                            {
                                value = value * 8 + (text[i++] - '0');
                                digits++;
                            }

                            sb.Append((char)value);
                        }
                        else
                        {
                            sb.Append(e);   // \" \\ \' and anything unknown
                        }

                        break;
                }
            }

            return sb.ToString();   // unterminated string: take the rest
        }
    }
}
