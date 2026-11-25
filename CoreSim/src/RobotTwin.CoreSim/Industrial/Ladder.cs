using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Industrial
{
    /// <summary>[plc] What a ladder cell holds.</summary>
    public enum LdKind
    {
        /// <summary>Nothing: power does not pass.</summary>
        Empty,

        /// <summary>A horizontal wire: power passes.</summary>
        Wire,

        /// <summary>Normally open contact --[ ]--: passes while its operand is TRUE.</summary>
        Contact,

        /// <summary>Normally closed contact --[/]--: passes while its operand is FALSE.</summary>
        ContactNot,

        /// <summary>Rising-edge contact --[P]--: passes for one scan when its operand turns TRUE.</summary>
        ContactRise,

        /// <summary>Falling-edge contact --[N]--.</summary>
        ContactFall,

        /// <summary>Coil --( )--: its operand takes the power.</summary>
        Coil,

        /// <summary>Negated coil --(/)--.</summary>
        CoilNot,

        /// <summary>Set coil --(S)--: TRUE while powered, stays.</summary>
        Set,

        /// <summary>Reset coil --(R)--.</summary>
        Reset,

        /// <summary>On-delay timer block: IN is the power, PT its parameter, the output its Q.</summary>
        Ton,

        Tof,

        Tp,

        /// <summary>Up counter: CU is the power, PV its parameter, R its second operand, the output its Q.</summary>
        Ctu,

        /// <summary>Comparison block: passes while "operand op parameter" holds (e.g. Seviye &gt;= 50).</summary>
        Compare
    }

    /// <summary>[plc] One cell of a rung's grid.</summary>
    public sealed class LdCell
    {
        public int Row { get; set; }

        public int Col { get; set; }

        public LdKind Kind { get; set; }

        /// <summary>The variable or address (Start, %IX0.0, T1 for a block instance).</summary>
        public string Operand { get; set; } = string.Empty;

        /// <summary>A block's parameter (PT: T#2s, PV: 5) or a comparison ("&gt;= 50").</summary>
        public string Param { get; set; } = string.Empty;

        /// <summary>A counter's reset operand.</summary>
        public string Reset { get; set; } = string.Empty;

        /// <summary>A vertical link on this cell's right side down to the row below: the two rows' power joins there.</summary>
        public bool LinkDown { get; set; }
    }

    /// <summary>[plc] A rung: a grid of cells between the left power rail and the coils on the right.</summary>
    public sealed class LdRung
    {
        public string Comment { get; set; } = string.Empty;

        public List<LdCell> Cells { get; set; } = new List<LdCell>();

        public LdCell? At(int row, int col) => Cells.FirstOrDefault(c => c.Row == row && c.Col == col);

        [JsonIgnore]
        public int Rows => Cells.Count == 0 ? 1 : Cells.Max(c => c.Row) + 1;

        [JsonIgnore]
        public int Columns => Cells.Count == 0 ? 1 : Cells.Max(c => c.Col) + 1;
    }

    /// <summary>
    /// [plc] A ladder diagram (IEC 61131-3 LD): declarations as in Structured Text, then rungs. It runs by turning into
    /// Structured Text (<see cref="ToStructuredText"/>) that the same compiler and interpreter run: each rung's power flows
    /// from the left rail column by column; a cell passes it on according to its kind, vertical links join the power of the
    /// rows they connect (a parallel branch), and the coils at a row's end take what reaches them. Rungs run top to bottom.
    /// </summary>
    public sealed class LdProgram
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>The VAR … END_VAR blocks (Structured Text): the program's variables and their addresses.</summary>
        public string Declarations { get; set; } = string.Empty;

        public List<LdRung> Rungs { get; set; } = new List<LdRung>();

        public string Save() => JsonSerializer.Serialize(this, Json);

        public static LdProgram Load(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new LdProgram();
            try
            {
                return JsonSerializer.Deserialize<LdProgram>(text, Json) ?? new LdProgram();
            }
            catch (JsonException)
            {
                return new LdProgram();
            }
        }

        private static bool IsCoil(LdKind k) => k is LdKind.Coil or LdKind.CoilNot or LdKind.Set or LdKind.Reset;

        private static bool IsBlock(LdKind k) => k is LdKind.Ton or LdKind.Tof or LdKind.Tp or LdKind.Ctu;

        /// <summary>
        /// The program as Structured Text: the declarations, the block instances and edge memories it needs, and one section per
        /// rung that computes its power column by column into hidden variables (so a long rung stays a short program).
        /// </summary>
        public string ToStructuredText(out List<StError> problems)
        {
            var found = new List<StError>();
            problems = found;
            var body = new StringBuilder();
            var hidden = new List<string>();
            var blocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // instance → type, declared if the user did not
            var declared = new HashSet<string>(Declared(Declarations), StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < Rungs.Count; k++)
            {
                var rung = Rungs[k];
                int line = k + 1;
                body.Append("(* basamak ").Append(line.ToString(CultureInfo.InvariantCulture));
                if (rung.Comment.Length > 0) body.Append(": ").Append(rung.Comment.Replace("*)", "* )"));
                body.AppendLine(" *)");
                int rows = rung.Rows, cols = rung.Columns;
                var coils = rung.Cells.Where(c => IsCoil(c.Kind)).ToList();
                int last = coils.Count == 0 ? cols : coils.Min(c => c.Col);   // the coils' column: the rung's logic ends before it
                // node[r] = the power at the right side of the current column, row r ("TRUE" at the rail for the rows that start).
                var node = new string?[rows];
                for (int r = 0; r < rows; r++) node[r] = "TRUE";
                for (int c = 0; c < last; c++)
                {
                    var outs = new string?[rows];
                    for (int r = 0; r < rows; r++)
                    {
                        var cell = rung.At(r, c);
                        string? input = node[r];
                        if (cell == null || cell.Kind == LdKind.Empty || input == null)
                        {
                            outs[r] = null;
                            continue;
                        }

                        string op = cell.Operand.Trim();
                        string? Need(string what)
                        {
                            if (op.Length > 0) return op;
                            found.Add(new StError(line, $"{line}. basamak, {r + 1}. satır {c + 1}. sütun: {what} için bir değişken yazın."));
                            return null;
                        }

                        switch (cell.Kind)
                        {
                            case LdKind.Wire:
                                outs[r] = input;
                                break;
                            case LdKind.Contact:
                                outs[r] = Need("kontak") is { } a ? And(input, a) : null;
                                break;
                            case LdKind.ContactNot:
                                outs[r] = Need("kontak") is { } b ? And(input, "NOT " + b) : null;
                                break;
                            case LdKind.ContactRise:
                            case LdKind.ContactFall:
                            {
                                if (Need("kenar kontağı") is not { } e) break;
                                string trig = $"_K{line}_{r}_{c}";
                                blocks[trig] = cell.Kind == LdKind.ContactRise ? "R_TRIG" : "F_TRIG";
                                body.AppendLine($"{trig}(CLK := {e});");
                                outs[r] = And(input, trig + ".Q");
                                break;
                            }

                            case LdKind.Compare:
                            {
                                string cmp = cell.Param.Trim();
                                if (Need("karşılaştırma") is not { } v) break;
                                if (cmp.Length == 0)
                                {
                                    found.Add(new StError(line, $"{line}. basamak: karşılaştırma için '>= 50' gibi bir koşul yazın."));
                                    break;
                                }

                                outs[r] = And(input, $"({v} {cmp})");
                                break;
                            }

                            default:   // a block: its IN (CU) is the power, its output its Q
                            {
                                if (Need("blok") is not { } inst) break;
                                string type = cell.Kind switch { LdKind.Ton => "TON", LdKind.Tof => "TOF", LdKind.Tp => "TP", _ => "CTU" };
                                if (!declared.Contains(inst)) blocks[inst] = type;
                                string param = cell.Param.Trim();
                                if (type == "CTU")
                                {
                                    string reset = cell.Reset.Trim().Length > 0 ? cell.Reset.Trim() : "FALSE";
                                    body.AppendLine($"{inst}(CU := {input}, R := {reset}, PV := {(param.Length > 0 ? param : "1")});");
                                }
                                else
                                {
                                    body.AppendLine($"{inst}(IN := {input}, PT := {(param.Length > 0 ? param : "T#1s")});");
                                }

                                outs[r] = inst + ".Q";
                                break;
                            }
                        }
                    }

                    // Vertical links on this column's right side join their rows' power (a branch closing or opening).
                    for (int r = 0; r < rows;)
                    {
                        int end = r;
                        while (end + 1 < rows && rung.At(end, c)?.LinkDown == true) end++;
                        var parts = Enumerable.Range(r, end - r + 1).Select(i => outs[i]).Where(x => x != null).Distinct().ToList();
                        string? joined = parts.Count == 0 ? null : parts.Count == 1 ? parts[0] : "(" + string.Join(" OR ", parts) + ")";
                        if (joined != null && joined.Length > 60)
                        {
                            // A long expression gets a name: the next columns read it.
                            string name = $"_L{line}_{r}_{c}";
                            hidden.Add(name);
                            body.AppendLine($"{name} := {joined};");
                            joined = name;
                        }

                        for (int i = r; i <= end; i++) outs[i] = end > r ? joined : outs[i] == null ? null : joined;
                        r = end + 1;
                    }

                    node = outs;
                }

                foreach (var coil in coils.OrderBy(x => x.Row))
                {
                    string? power = coil.Row < node.Length ? node[coil.Row] : null;
                    string target = coil.Operand.Trim();
                    if (target.Length == 0)
                    {
                        found.Add(new StError(line, $"{line}. basamak, {coil.Row + 1}. satır: bobin için bir değişken yazın."));
                        continue;
                    }

                    string p = power ?? "FALSE";
                    body.AppendLine(coil.Kind switch
                    {
                        LdKind.Coil => $"{target} := {p};",
                        LdKind.CoilNot => $"{target} := NOT ({p});",
                        LdKind.Set => $"IF {p} THEN {target} := TRUE; END_IF",
                        _ => $"IF {p} THEN {target} := FALSE; END_IF"
                    });
                }

                if (coils.Count == 0 && rung.Cells.Any(c => c.Kind != LdKind.Empty && !IsBlock(c.Kind)))
                    found.Add(new StError(line, $"{line}. basamağın sonunda bobin yok."));
            }

            var st = new StringBuilder();
            st.AppendLine(Declarations.Trim());
            if (blocks.Count > 0 || hidden.Count > 0)
            {
                st.AppendLine("VAR");
                foreach (var b in blocks) st.AppendLine($"    {b.Key} : {b.Value};");
                foreach (var h in hidden) st.AppendLine($"    {h} : BOOL;");
                st.AppendLine("END_VAR");
            }

            st.Append(body);
            return st.ToString();
        }

        /// <summary>Compiles the ladder: the program, or null with the errors (by rung, or the Structured Text's).</summary>
        public StProgram? Compile(out List<StError> errors)
        {
            string st = ToStructuredText(out var problems);
            if (problems.Count > 0)
            {
                errors = problems;
                return null;
            }

            return StProgram.Compile(st, out errors);
        }

        private static string And(string power, string term) => power == "TRUE" ? term : power + " AND " + term;

        /// <summary>The names the declarations declare (to leave the user's own block instances alone).</summary>
        private static IEnumerable<string> Declared(string declarations)
        {
            foreach (var raw in declarations.Split('\n'))
            {
                string line = raw.Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0 || line.StartsWith("(*", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
                string names = line.Substring(0, colon);
                int at = names.IndexOf(" AT ", StringComparison.OrdinalIgnoreCase);
                if (at >= 0) names = names.Substring(0, at);
                foreach (var n in names.Split(',')) yield return n.Trim();
            }
        }
    }
}
