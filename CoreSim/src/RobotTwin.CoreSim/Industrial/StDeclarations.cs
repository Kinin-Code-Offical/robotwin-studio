using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Industrial
{
    /// <summary>[plc-vars] One declared variable as the variable table shows it.</summary>
    public sealed class StVariable
    {
        /// <summary>The block it sits in: VAR, VAR_GLOBAL, VAR_INPUT, VAR_OUTPUT, VAR_IN_OUT, VAR_TEMP.</summary>
        public string Section { get; set; } = "VAR";

        public string Name { get; set; } = string.Empty;

        /// <summary>Its address ("%IX0.0", "%QX0.2", "%MW3"); empty when it has none.</summary>
        public string Location { get; set; } = string.Empty;

        /// <summary>Its type as written ("BOOL", "INT", "TON", "ARRAY[1..8] OF INT").</summary>
        public string Type { get; set; } = "BOOL";

        /// <summary>Its initial value as written (":= 5"), without the ":="; empty when none.</summary>
        public string Initial { get; set; } = string.Empty;

        /// <summary>The comment on its line, without (* *); empty when none.</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>The line as a VAR block holds it: <c>Name AT %IX0.0 : BOOL := TRUE; (* … *)</c>.</summary>
        public string ToLine()
        {
            var sb = new StringBuilder(Name);
            if (Location.Length > 0) sb.Append(" AT ").Append(Location);
            sb.Append(" : ").Append(Type.Length > 0 ? Type : "BOOL");
            if (Initial.Length > 0) sb.Append(" := ").Append(Initial);
            sb.Append(';');
            if (Comment.Length > 0) sb.Append(" (* ").Append(Comment).Append(" *)");
            return sb.ToString();
        }
    }

    /// <summary>
    /// [plc-vars] A program's VAR … END_VAR blocks as a table and back: each one-variable line becomes a row (section, name,
    /// address, type, initial value, comment); what the table cannot hold (several names on a line, a TYPE block, the code)
    /// is kept as it was. <see cref="Write"/> puts the rows back in place of the blocks they came from, so switching between
    /// the table and the text loses nothing.
    /// </summary>
    public static class StDeclarations
    {
        private static readonly Regex Line = new Regex(
            @"^\s*(?<name>[A-Za-z_À-ɏ][A-Za-z0-9_À-ɏ]*)\s*(?:AT\s+(?<at>%[A-Za-z]+[0-9.]*))?\s*:\s*(?<type>[^:;]+?)\s*(?::=\s*(?<init>[^;]+?))?\s*;\s*(?:\(\*(?<comment>.*?)\*\))?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Head = new Regex(@"^\s*(?<section>VAR_GLOBAL|VAR_INPUT|VAR_OUTPUT|VAR_IN_OUT|VAR_TEMP|VAR)\b(?<rest>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex End = new Regex(@"^\s*END_VAR\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The variables of every VAR block in <paramref name="text"/>, in order (lines the table cannot hold are left out).</summary>
        public static List<StVariable> Read(string text)
        {
            var list = new List<StVariable>();
            string? section = null;
            foreach (string raw in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                if (section == null)
                {
                    var h = Head.Match(raw);
                    if (h.Success) section = h.Groups["section"].Value.ToUpperInvariant();
                    continue;
                }

                if (End.IsMatch(raw))
                {
                    section = null;
                    continue;
                }

                var m = Line.Match(raw);
                if (!m.Success) continue;
                list.Add(new StVariable
                {
                    Section = section,
                    Name = m.Groups["name"].Value,
                    Location = m.Groups["at"].Value.ToUpperInvariant(),
                    Type = m.Groups["type"].Value.Trim(),
                    Initial = m.Groups["init"].Value.Trim(),
                    Comment = m.Groups["comment"].Value.Trim(),
                });
            }

            return list;
        }

        /// <summary>
        /// <paramref name="text"/> with its VAR blocks' table-held lines replaced by <paramref name="rows"/>: each section's rows
        /// go into that section's first block (in the rows' order), lines the table could not hold stay; a section the text
        /// does not have yet gets a block of its own before the code. Blocks left with nothing are removed.
        /// </summary>
        public static string Write(string text, IReadOnlyList<StVariable> rows)
        {
            var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n').ToList();
            var bySection = rows.GroupBy(r => (r.Section.Length > 0 ? r.Section : "VAR").ToUpperInvariant()).ToDictionary(g => g.Key, g => g.ToList());
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var output = new List<string>();
            string? section = null;
            int blockStart = -1;
            bool kept = false;
            string indent = "    ";
            foreach (string raw in lines)
            {
                if (section == null)
                {
                    var h = Head.Match(raw);
                    if (h.Success)
                    {
                        section = h.Groups["section"].Value.ToUpperInvariant();
                        blockStart = output.Count;
                        kept = false;
                        output.Add(raw);
                        if (placed.Add(section) && bySection.TryGetValue(section, out var mine))
                        {
                            foreach (var r in mine) output.Add(indent + r.ToLine());
                            kept = mine.Count > 0;
                        }

                        continue;
                    }

                    output.Add(raw);
                    continue;
                }

                if (End.IsMatch(raw))
                {
                    output.Add(raw);
                    if (!kept && output.Count - blockStart == 2) output.RemoveRange(blockStart, 2);   // nothing left in it
                    section = null;
                    continue;
                }

                if (Line.IsMatch(raw)) continue;   // a table line: the rows stand for it now
                if (raw.Trim().Length > 0) kept = true;   // a line the table could not hold stays
                var lead = raw.Length - raw.TrimStart().Length;
                if (raw.Trim().Length > 0 && lead > 0) indent = raw.Substring(0, lead);
                output.Add(raw);
            }

            // sections the text did not have: their own blocks, before the first line of code
            var missing = bySection.Where(kv => !placed.Contains(kv.Key) && kv.Value.Count > 0).ToList();
            if (missing.Count > 0)
            {
                int at = LastBlockEnd(output) + 1;
                var add = new List<string>();
                foreach (var (sec, rs) in missing)
                {
                    add.Add(sec);
                    add.AddRange(rs.Select(r => indent + r.ToLine()));
                    add.Add("END_VAR");
                }

                output.InsertRange(Math.Min(at, output.Count), add);
            }

            return string.Join("\n", output);
        }

        /// <summary>The line after which new blocks go: the last END_VAR (or before everything when there is none).</summary>
        private static int LastBlockEnd(List<string> lines)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
                if (End.IsMatch(lines[i])) return i;
            int first = lines.FindIndex(l => l.TrimStart().StartsWith("(*", StringComparison.Ordinal));
            return first == 0 ? lines.FindIndex(l => l.Contains("*)")) : -1;
        }

        /// <summary>Whether <paramref name="name"/> is a valid identifier for a new variable.</summary>
        public static bool IsName(string name) => Regex.IsMatch(name ?? string.Empty, @"^[A-Za-z_À-ɏ][A-Za-z0-9_À-ɏ]*$");
    }
}
