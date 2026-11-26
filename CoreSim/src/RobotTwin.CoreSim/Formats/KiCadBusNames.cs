using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>
    /// [kicad-sch] KiCad's bus names: a vector "D[0..7]" (members D0…D7, matched across buses by their place in it), a group
    /// "{SDA SCL}" or a named group "I2C{SDA SCL}" (members SDA, SCL or I2C.SDA, I2C.SCL, matched by the member's own
    /// name), a group naming a bus alias "{USB}" / "USB1{USB}" (the alias's members), and vectors inside groups. Braces
    /// after ~, _ or ^ are KiCad's text markup (overbar, subscript, superscript), part of a name, not a group.
    /// </summary>
    public static class KiCadBusNames
    {
        private static readonly Regex Vector = new Regex(@"^(.*?)\[(\d+)\.\.(\d+)\]$", RegexOptions.CultureInvariant);

        /// <summary>One member of a bus: the net name it stands for, and what it is matched by on another bus.</summary>
        public readonly struct Member
        {
            public Member(string net, string key)
            {
                Net = net;
                Key = key;
            }

            /// <summary>The name its wires are labelled with ("D3", "I2C.SDA").</summary>
            public string Net { get; }

            /// <summary>"#3" for a vector's fourth member, the member's own name for a group's ("SDA").</summary>
            public string Key { get; }

            public override string ToString() => Net;
        }

        /// <summary>The members a bus name stands for, or null when it names a single net.</summary>
        public static List<Member>? Members(string name, IReadOnlyDictionary<string, List<string>>? aliases = null)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int open = GroupOpen(name);
            if (open >= 0)
            {
                string prefix = name.Substring(0, open);
                string content = name.Substring(open + 1, name.Length - open - 2).Trim();
                var tokens = new List<string>(content.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries));
                if (tokens.Count == 1 && aliases != null && aliases.TryGetValue(tokens[0], out var aliased)) tokens = aliased;
                var members = new List<Member>();
                foreach (string token in tokens)
                {
                    var inner = VectorMembers(token);
                    if (inner != null)
                    {
                        foreach (var m in inner) members.Add(new Member(prefix.Length > 0 ? prefix + "." + m.Net : m.Net, m.Net));
                        continue;
                    }

                    if (aliases != null && aliases.TryGetValue(token, out var nested))
                    {
                        foreach (string n in nested) members.Add(new Member(prefix.Length > 0 ? prefix + "." + n : n, n));
                        continue;
                    }

                    members.Add(new Member(prefix.Length > 0 ? prefix + "." + token : token, token));
                }

                return members.Count > 0 ? members : null;
            }

            return VectorMembers(name);
        }

        private static List<Member>? VectorMembers(string name)
        {
            var m = Vector.Match(name);
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int a)
                || !int.TryParse(m.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int b)
                || Math.Abs(b - a) > 4096) return null;
            var list = new List<Member>();
            int step = b >= a ? 1 : -1;
            for (int i = a, k = 0; ; i += step, k++)
            {
                // matched by place in the vector: IO[0..7] on FP_IO[8..15] joins IO0 with FP_IO8, as KiCad does
                list.Add(new Member(m.Groups[1].Value + i.ToString(CultureInfo.InvariantCulture), "#" + k.ToString(CultureInfo.InvariantCulture)));
                if (i == b) break;
            }

            return list;
        }

        /// <summary>Where a name's closing group opens: the '{' matching its final '}', when that is not markup (after ~ _ ^); −1 otherwise.</summary>
        private static int GroupOpen(string name)
        {
            if (name[name.Length - 1] != '}') return -1;
            int depth = 0;
            for (int i = name.Length - 1; i >= 0; i--)
            {
                if (name[i] == '}') depth++;
                else if (name[i] == '{' && --depth == 0)
                    return i > 0 && (name[i - 1] == '~' || name[i - 1] == '_' || name[i - 1] == '^') ? -1 : i;
            }

            return -1;
        }
    }
}
