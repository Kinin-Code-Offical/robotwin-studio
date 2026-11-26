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
        Block,

        /// <summary>[plc-struct] A structure: its fields are variables of their own ("P.X").</summary>
        Struct,

        /// <summary>[plc-string] Text (STRING, WSTRING): up to its declared length of characters.</summary>
        String
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
    /// [plc-pou] Arrays (<c>ARRAY[1..8] OF INT</c>, of timers and blocks too), the program's own FUNCTION_BLOCKs (each
    /// instance keeps its own variables between scans) and FUNCTIONs (no state; named or positional arguments).
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

        /// <summary>
        /// [mbclient] The plant network the program's MB_READ and MB_WRITE blocks reach: a Modbus device's data table by its unit
        /// id (null: nothing answers at that address). Set by the PLC that runs the program.
        /// </summary>
        public Func<int, ModbusImage?>? ModbusNetwork { get; set; }

        /// <summary>The program's time (ms), advanced by each scan.</summary>
        public double Clock { get; private set; }

        /// <summary>Why the last scan stopped (a run-time fault), or null.</summary>
        public string? Fault { get; private set; }

        public long Scans { get; private set; }

        /// <summary>The variables by name (upper case), for watching: their type and value as text (a STRING in quotes: 'abc').</summary>
        public IEnumerable<(string Name, StType Type, string Value)> Watch =>
            _vars.Values.Where(v => v.Type != StType.Block && v.Type != StType.Struct).SelectMany(v => v.Texts != null
                ? v.Texts.Select((x, i) => (v.ElementName(i), v.Type, Quote(x)))
                : v.Elements != null
                    ? v.Elements.Select((x, i) => (v.ElementName(i), v.Type, Format(v.Type, x)))
                    : new[] { (v.Name, v.Type, v.Type == StType.String ? Quote(v.Text) : Format(v.Type, v.Value)) });

        /// <summary>
        /// [opcua] Every variable an OPC UA client sees: its name (an array's elements one by one, "A[3]"), type, declared type
        /// (INT, REAL …), whether it can be written (not an input), and its value now ([plc-string] a STRING's in Text).
        /// </summary>
        public IEnumerable<(string Name, StType Type, string TypeName, bool Writable, double Value, string? Text)> Tags =>
            _vars.Values.Where(v => v.Type != StType.Block && v.Type != StType.Struct).SelectMany(v => v.Texts != null
                ? v.Texts.Select((x, i) => (v.ElementName(i), v.Type, v.TypeName, true, 0.0, (string?)x))
                : v.Elements != null
                    ? v.Elements.Select((x, i) => (v.ElementName(i), v.Type, v.TypeName, true, x, (string?)null))
                    : new[] { (v.Name, v.Type, v.TypeName, v.Area != 'I', v.Value, v.Type == StType.String ? v.Text : null) });

        public double Read(string name)
        {
            string key = name.Trim().ToUpperInvariant();
            if (Element(key) is var (array, index)) return array.Elements?[index] ?? double.NaN;   // an array's element: A[3], P.ARR[2], A[2].X, M[1,2]
            if (_vars.TryGetValue(key, out var exact) && exact.Type != StType.Block && exact.Type != StType.Struct && !exact.IsArray)
                return exact.Type == StType.String ? double.NaN : exact.Value;   // a field: P.X
            int dot = key.IndexOf('.');
            if (dot > 0 && !key.StartsWith("%", StringComparison.Ordinal))
                return _vars.TryGetValue(key.Substring(0, dot), out var b) && b.Block != null ? b.Block[key.Substring(dot + 1)] : double.NaN;   // a block's member: T1.Q
            return _vars.TryGetValue(key, out var v) && !v.IsArray && v.Type != StType.String ? v.Value : double.NaN;
        }

        /// <summary>[plc-string] A STRING variable's (or element's, or a block member's) text; null when the name is not one.</summary>
        public string? ReadText(string name)
        {
            string key = name.Trim().ToUpperInvariant();
            if (Element(key) is var (array, index)) return array.Texts?[index];
            if (_vars.TryGetValue(key, out var v) && v.Type == StType.String && !v.IsArray) return v.Text;
            int dot = key.IndexOf('.');
            return dot > 0 && _vars.TryGetValue(key.Substring(0, dot), out var b) && b.Block != null && b.Block.Texts.TryGetValue(key.Substring(dot + 1), out var t) ? t : null;
        }

        /// <summary>
        /// [plc-pou] "A[3]": the array and the element's place in it, or null. [plc-array] "M[1,2]" for an array of more
        /// dimensions; "A[3].X" through the field's array of an array of structures.
        /// </summary>
        private (Var Array, int Index)? Element(string key)
        {
            int open = key.IndexOf('['), close = key.IndexOf(']');
            if (open <= 0 || close < open) return null;
            string root = key.Substring(0, open), path = key.Substring(close + 1);
            var parts = key.Substring(open + 1, close - open - 1).Split(',');
            var at = new int[parts.Length];
            for (int k = 0; k < parts.Length; k++)
                if (!int.TryParse(parts[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out at[k])) return null;
            var v = path.Length == 0 && _vars.TryGetValue(root, out var named) && named.DisplayRoot == null ? named
                : _vars.Values.FirstOrDefault(x => x.DisplayRoot == root && x.DisplayPath == path);
            if (v == null || v.Elements == null && v.Texts == null) return null;
            if (v.Dims == null) return at.Length == 1 && at[0] >= v.Lo && at[0] < v.Lo + v.Length ? (v, at[0] - v.Lo) : null;
            if (at.Length != v.Dims.Length) return null;
            int linear = 0;
            for (int k = 0; k < at.Length; k++)
            {
                if (at[k] < v.DimLos![k] || at[k] >= v.DimLos[k] + v.Dims[k]) return null;
                linear = linear * v.Dims[k] + at[k] - v.DimLos[k];
            }

            return (v, linear);
        }

        /// <summary>
        /// [hmi] Writes a variable from outside (an operator panel, a test), as an HMI writes a PLC's memory: it holds until the
        /// program changes it. False for a name that is not there, a block, an input (its pin sets it every scan), or a STRING
        /// (<see cref="WriteText"/>).
        /// </summary>
        public bool Write(string name, double value)
        {
            if (Element(name.Trim().ToUpperInvariant()) is var (array, index))
            {
                if (array.Elements == null) return false;
                array.Elements[index] = Coerce(array.Type, array.TypeName, value);
                return true;
            }

            if (!_vars.TryGetValue(name.Trim().ToUpperInvariant(), out var v) || v.Type is StType.Block or StType.String || v.IsArray || v.Area == 'I') return false;
            v.Set(value);
            if (v.Area == 'Q') Outputs[v.Address] = v.Value != 0;
            else if (v.Area == 'M') Memory[v.Address] = (short)Wrap("INT", v.Value);
            return true;
        }

        /// <summary>[plc-string] Writes a STRING variable or element from outside (cut to its length); false for anything else.</summary>
        public bool WriteText(string name, string text)
        {
            if (Element(name.Trim().ToUpperInvariant()) is var (array, index))
            {
                if (array.Texts == null) return false;
                array.Texts[index] = array.Clip(text);
                return true;
            }

            if (!_vars.TryGetValue(name.Trim().ToUpperInvariant(), out var v) || v.Type != StType.String || v.IsArray) return false;
            v.SetText(text);
            return true;
        }

        /// <summary>[plc-string] A STRING as IEC 61131-3 writes it: in single quotes, $ before a quote, a dollar or a control character.</summary>
        internal static string Quote(string s)
        {
            var b = new StringBuilder("'");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\'': b.Append("$'"); break;
                    case '$': b.Append("$$"); break;
                    case '\n': b.Append("$N"); break;
                    case '\r': b.Append("$R"); break;
                    case '\t': b.Append("$T"); break;
                    case '\f': b.Append("$P"); break;
                    default:
                        if (c < ' ') b.Append('$').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }

            return b.Append('\'').ToString();
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
            public char Section;   // [plc-pou] 'I' VAR_INPUT, 'O' VAR_OUTPUT, '\0' the others
            public int Lo;   // [plc-pou] an array's first index
            public double[]? Elements, InitialElements;   // an array of values
            public Block[]? Blocks;   // an array of block instances
            public StructDef? Struct;   // [plc-struct] a structure (its fields are variables of their own)
            public int StructCount;   // an array of structures: how many (Lo its first index)
            public List<Var>? Leaves;   // a structure's fields, nested ones opened up, in their order
            public string? DisplayRoot, DisplayPath;   // a field of an array of structures: "A" and ".X", shown as A[3].X
            public string Text = string.Empty, InitialText = string.Empty;   // [plc-string] a STRING's text
            public string[]? Texts, InitialTexts;   // an array of STRINGs
            public int MaxLength = 80;   // STRING[n]: at most n characters (80 when not given, as CODESYS and TwinCAT)
            public int[]? Dims, DimLos;   // [plc-array] an array of more dimensions: each one's length and first index (Lo is then 0)

            public bool IsArray => Elements != null || Blocks != null || Texts != null;

            /// <summary>An array of values or of STRINGs (not of blocks).</summary>
            public bool HasElements => Elements != null || Texts != null;

            public string ElementName(int i) =>
                (DisplayRoot ?? Name) + "[" + Subscripts(i) + "]" + (DisplayRoot != null ? DisplayPath : string.Empty);

            /// <summary>[plc-array] An element's indices from its place: "3", or "1,2" in an array of two dimensions.</summary>
            public string Subscripts(int i)
            {
                if (Dims == null) return (Lo + i).ToString(CultureInfo.InvariantCulture);
                var at = new int[Dims.Length];
                for (int k = Dims.Length - 1; k >= 0; k--)
                {
                    at[k] = DimLos![k] + i % Dims[k];
                    i /= Dims[k];
                }

                return string.Join(",", at.Select(x => x.ToString(CultureInfo.InvariantCulture)));
            }

            /// <summary>An element of an array of structures from its index, or a run-time fault outside it.</summary>
            public int StructSlot(double index, int line)
            {
                long i = (long)Math.Floor(index);
                if (i < Lo || i >= Lo + StructCount) throw new StRunException($"{line}. satırda {Name}[{i}]: dizi {Lo}..{Lo + StructCount - 1} arası.");
                return (int)(i - Lo);
            }

            public int Length => Elements?.Length ?? Texts?.Length ?? Blocks?.Length ?? 0;

            /// <summary>[plc-string] Text cut to this STRING's length, as a PLC keeps it.</summary>
            public string Clip(string s) => s.Length > MaxLength ? s.Substring(0, MaxLength) : s;

            public void SetText(string s) => Text = Clip(s);

            /// <summary>An element's place from its index, or a run-time fault when the index is outside the array (as a PLC stops).</summary>
            public int Slot(double index, int line)
            {
                long i = (long)Math.Floor(index);
                if (i < Lo || i >= Lo + Length)
                    throw new StRunException($"{line}. satırda {DisplayRoot ?? Name}[{i}]{(DisplayRoot != null ? DisplayPath : string.Empty)}: dizi {Lo}..{Lo + Length - 1} arası.");
                return (int)(i - Lo);
            }

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
                Text = InitialText;
                Block?.Reset();
                if (Elements != null && InitialElements != null) Array.Copy(InitialElements, Elements, Elements.Length);
                if (Texts != null && InitialTexts != null) Array.Copy(InitialTexts, Texts, Texts.Length);
                if (Blocks != null)
                    foreach (var b in Blocks) b.Reset();
            }

            public void Set(double v) => Value = Coerce(Type, TypeName, v);
        }

        /// <summary>[plc-struct] A structure type: its fields in their order.</summary>
        private sealed class StructDef
        {
            public string Name = string.Empty;
            public readonly List<FieldDef> Fields = new List<FieldDef>();
        }

        private sealed class FieldDef
        {
            public string Name = string.Empty;
            public string TypeName = string.Empty;
            public int Lo, Length;   // an array field
            public int[]? Dims, DimLos;   // [plc-array] an array field of more dimensions
            public int MaxLength = 80;   // [plc-string] STRING[n]
            public Expr? Init;
            public List<Expr>? Inits;
            public int Line;
        }

        /// <summary>
        /// [plc-struct] Copies a variable into another of the same shape: a value, a whole array, or a structure field by field;
        /// a slot ≥ 0 is the element of an array of structures (or of an array) on that side.
        /// </summary>
        private static void CopyVar(Var s, int sSlot, Var d, int dSlot)
        {
            if (s.Leaves != null && d.Leaves != null)
            {
                for (int k = 0; k < s.Leaves.Count && k < d.Leaves.Count; k++) CopyVar(s.Leaves[k], sSlot, d.Leaves[k], dSlot);
                return;
            }

            if (s.Elements != null && sSlot < 0 && d.Elements != null && dSlot < 0)
            {
                for (int i = 0; i < Math.Min(s.Elements.Length, d.Elements.Length); i++) d.Elements[i] = Coerce(d.Type, d.TypeName, s.Elements[i]);
                return;
            }

            if (s.Type == StType.String)
            {
                // [plc-string] a whole array of STRINGs, an element, or one STRING; cut to the receiver's length
                if (s.Texts != null && sSlot < 0 && d.Texts != null && dSlot < 0)
                {
                    for (int i = 0; i < Math.Min(s.Texts.Length, d.Texts.Length); i++) d.Texts[i] = d.Clip(s.Texts[i]);
                    return;
                }

                string t = sSlot >= 0 && s.Texts != null ? s.Texts[sSlot] : s.Text;
                if (dSlot >= 0 && d.Texts != null) d.Texts[dSlot] = d.Clip(t);
                else d.SetText(t);
                return;
            }

            double v = sSlot >= 0 && s.Elements != null ? s.Elements[sSlot] : s.Value;
            if (dSlot >= 0 && d.Elements != null) d.Elements[dSlot] = Coerce(d.Type, d.TypeName, v);
            else d.Set(v);
        }

        /// <summary>The slot of an access: an element of an array of structures (or of an array), or −1 for the whole.</summary>
        private static int SlotOf(Var v, Expr? at, StProgram p, int line) =>
            at == null ? -1 : v.Type == StType.Struct ? v.StructSlot(at.Eval(p), line) : v.Slot(at.Eval(p), line);

        /// <summary>A value as a variable of this type keeps it: BOOL 0/1, integers truncated and wrapped, REAL and TIME as they are.</summary>
        private static double Coerce(StType type, string typeName, double v) => type switch
        {
            StType.Bool => v != 0 ? 1 : 0,
            StType.Int => Wrap(typeName, Math.Truncate(v)),
            _ => v
        };

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

            /// <summary>[plc-string] Its STRING inputs and outputs (the program's own blocks).</summary>
            public readonly Dictionary<string, string> Texts = new Dictionary<string, string>(StringComparer.Ordinal);

            public string Text(string m) => Texts.TryGetValue(m, out var t) ? t : string.Empty;

            public abstract IReadOnlyCollection<string> Inputs { get; }

            public abstract IReadOnlyCollection<string> Outputs { get; }

            public double this[string m]
            {
                get => Members.TryGetValue(m, out var v) ? v : 0;
                set => Members[m] = value;
            }

            public abstract void Call(double clock);

            /// <summary>A call that needs the program around it (the network a Modbus block talks over).</summary>
            public virtual void Call(double clock, StProgram program) => Call(clock);

            public virtual void Reset()
            {
                Members.Clear();
                Texts.Clear();
            }

            /// <summary>[plc-pou] A member's declared type (the program's own blocks), or null for the standard ones.</summary>
            public virtual StType? TypeOf(string member) => null;

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
                "MB_READ" => new ModbusClient(false),
                "MB_WRITE" => new ModbusClient(true),
                "UA_READ" => new UaClientBlock(false),
                "UA_WRITE" => new UaClientBlock(true),
                _ => null
            };
        }

        /// <summary>
        /// [mbclient] MB_READ and MB_WRITE: a Modbus client request on the plant network. A rising edge of REQ asks the device
        /// at UNIT: MB_READ with FC 1 (coil), 2 (discrete input), 3 (holding register) or 4 (input register) at ADDR, MB_WRITE
        /// with FC 5 (coil) or 6 (holding register) of VALUE. BUSY until the answer comes back (5 ms, the round trip of a
        /// plant network); then DONE for one scan (MB_READ's VALUE holds the word or bit, signed as INT), or ERROR with STATUS:
        /// the device's exception code (1 function, 2 address, 3 value) or 11 when nothing answered within 200 ms.
        /// </summary>
        private sealed class ModbusClient : Block
        {
            public const double Latency = 5, Timeout = 200;
            private static readonly string[] ReadIn = { "REQ", "UNIT", "FC", "ADDR" }, WriteIn = { "REQ", "UNIT", "FC", "ADDR", "VALUE" };
            private static readonly string[] ReadOut = { "DONE", "BUSY", "ERROR", "STATUS", "VALUE" }, WriteOut = { "DONE", "BUSY", "ERROR", "STATUS" };
            private readonly bool _write;
            private bool _last;
            private double _due = double.NaN;
            private byte[]? _reply;

            public ModbusClient(bool write) => _write = write;

            public override IReadOnlyCollection<string> Inputs => _write ? WriteIn : ReadIn;

            public override IReadOnlyCollection<string> Outputs => _write ? WriteOut : ReadOut;

            public override void Call(double clock) => Call(clock, null!);

            public override void Call(double clock, StProgram program)
            {
                this["DONE"] = 0;
                bool req = this["REQ"] != 0;
                if (req && !_last && double.IsNaN(_due))
                {
                    int fc = (int)this["FC"], addr = (int)this["ADDR"];
                    if (fc == 0) fc = _write ? 6 : 3;
                    var image = program?.ModbusNetwork?.Invoke((int)this["UNIT"]);
                    bool offline = false;
                    if (image != null) lock (image.Gate) offline = image.Offline;
                    if (image == null || offline)
                    {
                        _reply = null;
                        _due = clock + Timeout;
                    }
                    else
                    {
                        int v = (int)this["VALUE"];
                        byte[] pdu = _write
                            ? fc == 5
                                ? new byte[] { 5, (byte)(addr >> 8), (byte)addr, (byte)(v != 0 ? 0xFF : 0), 0 }
                                : new byte[] { (byte)fc, (byte)(addr >> 8), (byte)addr, (byte)(v >> 8), (byte)v }
                            : new byte[] { (byte)fc, (byte)(addr >> 8), (byte)addr, 0, 1 };
                        _reply = ModbusTcpServer.Answer(image, pdu);
                        _due = clock + Latency;
                    }

                    this["BUSY"] = 1;
                    this["ERROR"] = 0;
                    this["STATUS"] = 0;
                }

                _last = req;
                if (double.IsNaN(_due) || clock < _due) return;
                _due = double.NaN;
                this["BUSY"] = 0;
                if (_reply == null)
                {
                    this["ERROR"] = 1;
                    this["STATUS"] = 11;   // gateway target device failed to respond
                }
                else if ((_reply[0] & 0x80) != 0)
                {
                    this["ERROR"] = 1;
                    this["STATUS"] = _reply.Length > 1 ? _reply[1] : 4;
                }
                else
                {
                    this["DONE"] = 1;
                    if (!_write && _reply.Length >= 3)
                    {
                        this["VALUE"] = _reply[0] <= 2
                            ? _reply[2] & 1
                            : _reply.Length >= 4 ? (short)(_reply[2] << 8 | _reply[3]) : 0;
                    }
                }
            }

            public override void Reset()
            {
                base.Reset();
                _last = false;
                _due = double.NaN;
                _reply = null;
            }
        }

        /// <summary>
        /// [opcua-client] UA_READ and UA_WRITE: the PLC as an OPC UA client. A rising edge of REQ reads (or writes) the variable
        /// NODE ("ns=1;s=Line.Speed", "ns=2;i=1001") on the server at URL ("opc.tcp://host:4840"): UA_READ gives VALUE (a
        /// number or BOOL as 0/1) and TEXT (a String's text, any value's as text); UA_WRITE writes VALUE, or TEXT to a String
        /// node, converted to the node's own type. BUSY while the request is on the network (beside the scan: the scan never
        /// waits), then DONE for one scan, or ERROR with STATUS: the OPC UA status code's upper word (0x8034 = 32820 the node
        /// is not there, 0x803B not writable, 0x8074 wrong type, 0x8005 = 32773 no connection). The connection stays open
        /// between requests and is made again after a failure.
        /// </summary>
        private sealed class UaClientBlock : Block
        {
            public const double NoConnection = 0x8005;
            private static readonly string[] ReadIn = { "REQ", "URL", "NODE" }, WriteIn = { "REQ", "URL", "NODE", "VALUE", "TEXT" };
            private static readonly string[] ReadOut = { "DONE", "BUSY", "ERROR", "STATUS", "VALUE", "TEXT" }, WriteOut = { "DONE", "BUSY", "ERROR", "STATUS" };
            private readonly bool _write;
            private bool _last;
            private System.Threading.Tasks.Task<(uint Status, double Value, string Text)>? _pending;
            private OpcUa.UaClient? _client;
            private readonly Dictionary<string, OpcUa.UaType> _types = new Dictionary<string, OpcUa.UaType>(StringComparer.Ordinal);

            public UaClientBlock(bool write) => _write = write;

            public override IReadOnlyCollection<string> Inputs => _write ? WriteIn : ReadIn;

            public override IReadOnlyCollection<string> Outputs => _write ? WriteOut : ReadOut;

            public override void Call(double clock)
            {
                this["DONE"] = 0;
                bool req = this["REQ"] != 0;
                if (req && !_last && _pending == null)
                {
                    string url = Text("URL").Trim(), node = Text("NODE");
                    double value = this["VALUE"];
                    string text = Text("TEXT");
                    _pending = System.Threading.Tasks.Task.Run(() => Request(url, node, value, text));
                    this["BUSY"] = 1;
                    this["ERROR"] = 0;
                    this["STATUS"] = 0;
                }

                _last = req;
                if (_pending == null || !_pending.IsCompleted) return;
                var (status, v, t) = _pending.Result;
                _pending = null;
                this["BUSY"] = 0;
                if (status != 0)
                {
                    this["ERROR"] = 1;
                    this["STATUS"] = status >> 16;
                    return;
                }

                this["DONE"] = 1;
                if (_write) return;
                this["VALUE"] = v;
                Texts["TEXT"] = t;
            }

            /// <summary>On a worker thread: one read or write, connecting first when there is no open connection to URL.</summary>
            private (uint, double, string) Request(string url, string node, double value, string text)
            {
                if (!OpcUa.UaClient.TryParseNodeId(node, out var id)) return (0x80330000, 0, string.Empty);   // BadNodeIdInvalid
                try
                {
                    if (_client == null || _client.Url != url || !_client.Connected)
                    {
                        _client?.Dispose();
                        _client = null;
                        _client = OpcUa.UaClient.Connect(url);
                    }

                    if (!_write)
                    {
                        var d = _client.Read(id);
                        if (!d.HasValue || (d.Status & 0x80000000) != 0) return (d.Status == 0 ? 0x80000000 : d.Status, 0, string.Empty);
                        _types[node] = d.Value.Type;
                        return (0, Number(d.Value), Shown(d.Value));
                    }

                    if (!_types.TryGetValue(node, out var type))
                    {
                        var d = _client.Read(id);   // the node's own type, once
                        if (!d.HasValue || (d.Status & 0x80000000) != 0) return (d.Status == 0 ? 0x80000000 : d.Status, 0, string.Empty);
                        _types[node] = type = d.Value.Type;
                    }

                    return (_client.Write(id, Typed(type, value, text)), 0, string.Empty);
                }
                catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or FormatException or AggregateException or ObjectDisposedException or InvalidOperationException)
                {
                    _client?.Dispose();
                    _client = null;
                    return (0x80050000, 0, string.Empty);   // BadCommunicationError
                }
            }

            private static double Number(OpcUa.Variant v) => v.Value switch
            {
                bool b => b ? 1 : 0,
                string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0,
                IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
                _ => 0
            };

            private static string Shown(OpcUa.Variant v) => v.Value switch
            {
                null => string.Empty,
                string s => s,
                bool b => b ? "TRUE" : "FALSE",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.Value.ToString() ?? string.Empty
            };

            /// <summary>The value given, in the node's type (a number wrapped as that type does, TEXT for a String).</summary>
            private static OpcUa.Variant Typed(OpcUa.UaType type, double v, string text)
            {
                long n = (long)Math.Round(v);
                return type switch
                {
                    OpcUa.UaType.Boolean => new OpcUa.Variant(type, v != 0),
                    OpcUa.UaType.SByte => new OpcUa.Variant(type, unchecked((sbyte)n)),
                    OpcUa.UaType.Byte => new OpcUa.Variant(type, unchecked((byte)n)),
                    OpcUa.UaType.Int16 => new OpcUa.Variant(type, unchecked((short)n)),
                    OpcUa.UaType.UInt16 => new OpcUa.Variant(type, unchecked((ushort)n)),
                    OpcUa.UaType.Int32 => new OpcUa.Variant(type, unchecked((int)n)),
                    OpcUa.UaType.UInt32 => new OpcUa.Variant(type, unchecked((uint)n)),
                    OpcUa.UaType.Int64 => new OpcUa.Variant(type, n),
                    OpcUa.UaType.UInt64 => new OpcUa.Variant(type, unchecked((ulong)n)),
                    OpcUa.UaType.Float => new OpcUa.Variant(type, (float)v),
                    OpcUa.UaType.String => new OpcUa.Variant(type, text),
                    _ => new OpcUa.Variant(OpcUa.UaType.Double, v)
                };
            }

            public override void Reset()
            {
                base.Reset();
                _last = false;
                _pending = null;
                _types.Clear();
                _client?.Dispose();
                _client = null;
            }
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

        /// <summary>
        /// [plc-pou] An instance of the program's own FUNCTION_BLOCK: its variables (inputs, outputs, locals, blocks of its
        /// own) live in the instance and keep their values between calls; a call takes the inputs given, runs the body,
        /// and leaves the outputs to read (<c>inst.Q</c>) or to pass on (<c>Q =&gt; x</c>).
        /// </summary>
        private sealed class UserBlock : Block
        {
            private readonly Dictionary<string, Var> _vars;
            private readonly List<Stmt> _body;
            private readonly List<string> _inputs, _outputs, _inOuts;

            public UserBlock(Dictionary<string, Var> vars, List<string> order, List<Stmt> body)
            {
                _vars = vars;
                _body = body;
                _inputs = order.Where(n => vars[n].Section == 'I').ToList();
                _outputs = order.Where(n => vars[n].Section == 'O').ToList();
                _inOuts = order.Where(n => vars[n].Section == 'X').ToList();
                Publish();
            }

            /// <summary>[plc-struct] Its VAR_IN_OUT parameters.</summary>
            public IReadOnlyCollection<string> InOuts => _inOuts;

            /// <summary>[plc-struct] One of its own variables (a VAR_IN_OUT the caller's value is copied into).</summary>
            public Var Inner(string name) => _vars[name];

            public override IReadOnlyCollection<string> Inputs => _inputs;

            public override IReadOnlyCollection<string> Outputs => _outputs;

            public override StType? TypeOf(string member) => _vars.TryGetValue(member, out var v) && v.Section != '\0' && !v.IsArray ? v.Type : null;

            public override void Call(double clock) => throw new StRunException("Blok programsız çağrıldı.");

            public override void Call(double clock, StProgram program)
            {
                foreach (string name in _inputs)
                {
                    var input = _vars[name];
                    if (input.Type == StType.String)
                    {
                        if (Texts.TryGetValue(name, out var t)) input.SetText(t);
                    }
                    else if (Members.TryGetValue(name, out double v)) input.Set(v);
                }

                program.Run(_body);   // RETURN (or EXIT outside a loop) ends the call
                Publish();
            }

            public override void Reset()
            {
                Members.Clear();
                Texts.Clear();
                foreach (var v in _vars.Values) v.Reset();
                Publish();
            }

            private void Publish()
            {
                foreach (string name in _inputs.Concat(_outputs))
                {
                    var v = _vars[name];
                    if (v.Type == StType.String) Texts[name] = v.Text;
                    else Members[name] = v.Value;
                }
            }
        }

        /// <summary>[plc-pou] One of the program's own FUNCTIONs: compiled once, its variables fresh at every call.</summary>
        private sealed class UserFunction
        {
            public string Name = string.Empty;
            public Var Result = null!;
            public readonly List<Var> Inputs = new List<Var>();

            /// <summary>[plc-struct] VAR_INPUT and VAR_IN_OUT in their declared order: the positional arguments.</summary>
            public readonly List<Var> Params = new List<Var>();
            public Dictionary<string, Var> Vars = new Dictionary<string, Var>(StringComparer.Ordinal);
            public List<Stmt> Body = new List<Stmt>();
        }

        private abstract class Expr
        {
            public int Line;
            public StType Type;

            public abstract double Eval(StProgram p);

            /// <summary>[plc-string] The text of a STRING expression (the compiler calls Eval only on the others).</summary>
            public virtual string Str(StProgram p) => throw new StRunException($"{Line}. satırda bir metin bekleniyordu.");
        }

        private sealed class Const : Expr
        {
            public double Value;
            public string? Text;   // [plc-string] 'abc'

            public override double Eval(StProgram p) => Value;

            public override string Str(StProgram p) => Text ?? base.Str(p);
        }

        private sealed class Ref : Expr
        {
            public Var Var = null!;

            public override double Eval(StProgram p) => Var.Value;

            public override string Str(StProgram p) => Var.Text;
        }

        private sealed class Member : Expr
        {
            public Var Var = null!;
            public string Name = string.Empty;

            public override double Eval(StProgram p) => Var.Block![Name];

            public override string Str(StProgram p) => Var.Block!.Text(Name);
        }

        /// <summary>[plc-pou] An array's element: <c>A[i]</c>.</summary>
        private sealed class Index : Expr
        {
            public Var Var = null!;
            public Expr At = null!;

            public override double Eval(StProgram p) => Var.Elements![Var.Slot(At.Eval(p), Line)];

            public override string Str(StProgram p) => Var.Texts![Var.Slot(At.Eval(p), Line)];
        }

        /// <summary>
        /// [plc-array] The place of an element of an array of more dimensions (<c>M[i, j]</c>), row by row as IEC 61131-3 lays
        /// them out; each index is checked against its own dimension (a run-time fault outside it, as a PLC stops).
        /// </summary>
        private sealed class Flatten : Expr
        {
            public string Name = string.Empty;
            public int[] Dims = null!, Los = null!;
            public List<Expr> Parts = new List<Expr>();

            public override double Eval(StProgram p)
            {
                var at = new long[Parts.Count];
                for (int k = 0; k < Parts.Count; k++) at[k] = (long)Math.Floor(Parts[k].Eval(p));
                long linear = 0;
                for (int k = 0; k < Parts.Count; k++)
                {
                    if (at[k] < Los[k] || at[k] >= Los[k] + Dims[k])
                        throw new StRunException($"{Line}. satırda {Name}[{string.Join(",", at)}]: {k + 1}. boyut {Los[k]}..{Los[k] + Dims[k] - 1} arası.");
                    linear = linear * Dims[k] + at[k] - Los[k];
                }

                return linear;
            }
        }

        /// <summary>[plc-pou] A member of a block in an array: <c>T[i].Q</c>.</summary>
        private sealed class IndexedMember : Expr
        {
            public Var Var = null!;
            public Expr At = null!;
            public string Name = string.Empty;

            public override double Eval(StProgram p) => Var.Blocks![Var.Slot(At.Eval(p), Line)][Name];

            public override string Str(StProgram p) => Var.Blocks![Var.Slot(At.Eval(p), Line)].Text(Name);
        }

        /// <summary>[plc-pou] A call of one of the program's FUNCTIONs: the arguments first, then fresh variables, then the body.</summary>
        private sealed class FunctionCall : Expr
        {
            public UserFunction Function = null!;
            public List<(Var Input, Expr Value)> Args = new List<(Var, Expr)>();
            public List<(Var Inner, Var Outer, Expr? OuterAt)> InOuts = new List<(Var, Var, Expr?)>();   // [plc-struct]

            public override double Eval(StProgram p)
            {
                Invoke(p);
                return Function.Result.Value;
            }

            public override string Str(StProgram p)
            {
                Invoke(p);
                return Function.Result.Text;
            }

            private void Invoke(StProgram p)
            {
                var values = new double[Args.Count];
                var texts = new string?[Args.Count];
                for (int i = 0; i < Args.Count; i++)
                {
                    if (Args[i].Input.Type == StType.String) texts[i] = Args[i].Value.Str(p);
                    else values[i] = Args[i].Value.Eval(p);
                }

                var slots = InOuts.Select(x => SlotOf(x.Outer, x.OuterAt, p, Line)).ToList();
                foreach (var v in Function.Vars.Values) v.Reset();
                for (int i = 0; i < Args.Count; i++)
                {
                    if (texts[i] != null) Args[i].Input.SetText(texts[i]!);
                    else Args[i].Input.Set(values[i]);
                }

                for (int k = 0; k < InOuts.Count; k++) CopyVar(InOuts[k].Outer, slots[k], InOuts[k].Inner, -1);
                p.Spend();
                p.Run(Function.Body);
                for (int k = 0; k < InOuts.Count; k++) CopyVar(InOuts[k].Inner, -1, InOuts[k].Outer, slots[k]);
            }
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
                if (A.Type == StType.String)
                {
                    // [plc-string] texts compare character by character (their codes), as IEC 61131-3 orders them
                    int c = string.CompareOrdinal(A.Str(p), B.Str(p));
                    return Op switch
                    {
                        "=" => c == 0, "<>" => c != 0, "<" => c < 0, "<=" => c <= 0, ">" => c > 0, _ => c >= 0
                    } ? 1 : 0;
                }

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

            public override string Str(StProgram p)
            {
                // [plc-string] IEC 61131-3's string functions: positions count from 1, lengths and positions outside the text are cut to it
                string S(int i) => Args[i].Str(p);
                int N(int i) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Floor(Args[i].Eval(p))));
                switch (Name)
                {
                    case "LEFT":
                    {
                        string s = S(0);
                        return s.Substring(0, Math.Max(0, Math.Min(N(1), s.Length)));
                    }

                    case "RIGHT":
                    {
                        string s = S(0);
                        int l = Math.Max(0, Math.Min(N(1), s.Length));
                        return s.Substring(s.Length - l);
                    }

                    case "MID":
                    {
                        string s = S(0);
                        int l = N(1), at = N(2);
                        if (at < 1 || at > s.Length || l <= 0) return string.Empty;
                        return s.Substring(at - 1, Math.Min(l, s.Length - at + 1));
                    }

                    case "CONCAT":
                        return string.Concat(Args.Select(a => a.Str(p)));
                    case "INSERT":
                    {
                        string s = S(0), add = S(1);
                        int at = Math.Max(0, Math.Min(N(2), s.Length));
                        return s.Substring(0, at) + add + s.Substring(at);
                    }

                    case "DELETE":
                    {
                        string s = S(0);
                        int l = N(1), at = N(2);
                        if (at < 1 || at > s.Length || l <= 0) return s;
                        return s.Remove(at - 1, Math.Min(l, s.Length - at + 1));
                    }

                    case "REPLACE":
                    {
                        string s = S(0), add = S(1);
                        int l = Math.Max(0, N(2)), at = Math.Max(1, Math.Min(N(3), s.Length + 1));
                        return s.Substring(0, at - 1) + add + s.Substring(Math.Min(s.Length, at - 1 + l));
                    }

                    case "SEL":
                        return Args[0].Eval(p) != 0 ? S(2) : S(1);
                    default:
                        if (Name.EndsWith("_TO_STRING", StringComparison.Ordinal) || Name.EndsWith("_TO_WSTRING", StringComparison.Ordinal))
                        {
                            var a = Args[0];
                            if (a.Type == StType.String) return a.Str(p);
                            return TextOf(a.Type, Name.Substring(0, Name.IndexOf("_TO_", StringComparison.Ordinal)), a.Eval(p));
                        }

                        return base.Str(p);
                }
            }

            /// <summary>[plc-string] A value as X_TO_STRING writes it: TRUE/FALSE, 42, 1.5 (REAL to its 7 digits), T#1s500ms.</summary>
            private static string TextOf(StType type, string from, double v) => type switch
            {
                StType.Bool => v != 0 ? "TRUE" : "FALSE",
                StType.Time => TimeText(v),
                StType.Real => from == "REAL" ? ((float)v).ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture),
                _ => ((long)v).ToString(CultureInfo.InvariantCulture)
            };

            /// <summary>[plc-string] A time as TIME_TO_STRING writes it: T#1h2m3s4ms (T#0ms for none).</summary>
            internal static string TimeText(double ms)
            {
                if (ms == 0) return "T#0ms";
                var b = new StringBuilder(ms < 0 ? "T#-" : "T#");
                double rest = Math.Abs(ms);
                foreach (var (unit, size) in new[] { ("d", 86_400_000.0), ("h", 3_600_000.0), ("m", 60_000.0), ("s", 1000.0) })
                {
                    double n = Math.Floor(rest / size);
                    if (n > 0) b.Append(n.ToString(CultureInfo.InvariantCulture)).Append(unit);
                    rest -= n * size;
                }

                if (rest > 1e-9) b.Append(rest.ToString("0.###", CultureInfo.InvariantCulture)).Append("ms");
                return b.ToString();
            }

            /// <summary>[plc-string] STRING_TO_X: the text read as that type ('42', '1.5', 'TRUE', 'T#1s', '16#FF'); 0 when it is not one, as a PLC gives.</summary>
            private static double Parse(string text, string to)
            {
                string s = text.Trim().Replace("_", string.Empty);
                switch (to)
                {
                    case "BOOL":
                        return s.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || s == "1" ? 1 : 0;
                    case "TIME":
                    {
                        int hash = s.IndexOf('#');
                        if (hash >= 0) s = s.Substring(hash + 1);
                        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double plain)) return plain;
                        bool negative = s.StartsWith("-", StringComparison.Ordinal);
                        try
                        {
                            return Lexer.Duration(negative ? s.Substring(1) : s, 0) * (negative ? -1 : 1);
                        }
                        catch (StCompileException)
                        {
                            return 0;
                        }
                    }

                    case "REAL" or "LREAL":
                        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : 0;
                    default:
                    {
                        int hash = s.IndexOf('#');
                        if (hash > 0 && int.TryParse(s.Substring(0, hash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int radix) && radix is 2 or 8 or 16)
                        {
                            try
                            {
                                return Wrap(to, Convert.ToInt64(s.Substring(hash + 1), radix));
                            }
                            catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                            {
                                return 0;
                            }
                        }

                        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n)) return Wrap(to, n);
                        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && Math.Abs(d) < 9e18
                            ? Wrap(to, Math.Round(d, MidpointRounding.ToEven)) : 0;
                    }
                }
            }

            public override double Eval(StProgram p)
            {
                double A(int i) => Args[i].Eval(p);
                switch (Name)
                {
                    case "LEN": return Args[0].Str(p).Length;
                    case "FIND":
                    {
                        string s = Args[0].Str(p), what = Args[1].Str(p);
                        return what.Length == 0 ? 0 : s.IndexOf(what, StringComparison.Ordinal) + 1;
                    }

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
                            if (Args[0].Type == StType.String) return Parse(Args[0].Str(p), to);   // [plc-string] STRING_TO_INT('42')
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
                if (Value.Type == StType.String)
                {
                    string s = Value.Str(p);
                    if (Member != null) Target.Block!.Texts[Member] = s;
                    else Target.SetText(s);
                    return Flow.Next;
                }

                double v = Value.Eval(p);
                if (Member != null) Target.Block![Member] = v;
                else Target.Set(v);
                return Flow.Next;
            }
        }

        /// <summary>[plc-pou] <c>A[i] := x;</c> or, for an array of blocks, <c>T[i].IN := x;</c>.</summary>
        private sealed class AssignElement : Stmt
        {
            public Var Target = null!;
            public Expr At = null!;
            public string? Member;
            public Expr Value = null!;

            public override Flow Run(StProgram p)
            {
                int slot = Target.Slot(At.Eval(p), Line);
                if (Value.Type == StType.String)
                {
                    string s = Value.Str(p);
                    if (Member != null) Target.Blocks![slot].Texts[Member] = s;
                    else Target.Texts![slot] = Target.Clip(s);
                    return Flow.Next;
                }

                double v = Value.Eval(p);
                if (Member != null) Target.Blocks![slot][Member] = v;
                else Target.Elements![slot] = Coerce(Target.Type, Target.TypeName, v);
                return Flow.Next;
            }
        }

        /// <summary>[plc-struct] <c>P := Q;</c>, <c>A[i] := P;</c>: a structure copied field by field.</summary>
        private sealed class StructAssign : Stmt
        {
            public Var Dst = null!, Src = null!;
            public Expr? DstAt, SrcAt;

            public override Flow Run(StProgram p)
            {
                int s = SlotOf(Src, SrcAt, p, Line), d = SlotOf(Dst, DstAt, p, Line);
                CopyVar(Src, s, Dst, d);
                return Flow.Next;
            }
        }

        private sealed class BlockCall : Stmt
        {
            public Var Instance = null!;
            public Expr? At;   // [plc-pou] the element, for a block in an array: T[i](…)
            public List<(string Name, Expr Value)> Inputs = new List<(string, Expr)>();
            public List<(string Name, Var Target)> Outputs = new List<(string, Var)>();
            public List<(string Name, Var Outer, Expr? OuterAt)> InOuts = new List<(string, Var, Expr?)>();   // [plc-struct] VAR_IN_OUT

            public override Flow Run(StProgram p)
            {
                var b = At != null ? Instance.Blocks![Instance.Slot(At.Eval(p), Line)] : Instance.Block!;
                if (InOuts.Count > 0) return RunInOut(p, (UserBlock)b);
                GiveInputs(p, b);
                b.Call(p.Clock, p);
                TakeOutputs(b);
                return Flow.Next;
            }

            private void GiveInputs(StProgram p, Block b)
            {
                foreach (var (name, value) in Inputs)
                {
                    if (value.Type == StType.String) b.Texts[name] = value.Str(p);
                    else b[name] = value.Eval(p);
                }
            }

            private void TakeOutputs(Block b)
            {
                foreach (var (name, target) in Outputs)
                {
                    if (target.Type == StType.String) target.SetText(b.Text(name));
                    else target.Set(b[name]);
                }
            }

            /// <summary>[plc-struct] A call with VAR_IN_OUT: the caller's variables copied in, the block run, then copied back.</summary>
            private Flow RunInOut(StProgram p, UserBlock b)
            {
                var slots = InOuts.Select(x => SlotOf(x.Outer, x.OuterAt, p, Line)).ToList();
                for (int k = 0; k < InOuts.Count; k++) CopyVar(InOuts[k].Outer, slots[k], b.Inner(InOuts[k].Name), -1);
                GiveInputs(p, b);
                b.Call(p.Clock, p);
                for (int k = 0; k < InOuts.Count; k++) CopyVar(b.Inner(InOuts[k].Name), -1, InOuts[k].Outer, slots[k]);
                TakeOutputs(b);
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
            Text,   // [plc-string] 'abc' (its characters, the $ escapes read)
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

                    if (c == '\'' || c == '"')
                    {
                        // [plc-string] 'text' (STRING) or "text" (WSTRING); $' $" $$ $L $N $P $R $T and $hh ($hhhh in a WSTRING)
                        char quote = c;
                        var text = new StringBuilder();
                        i++;
                        while (true)
                        {
                            if (i >= s.Length || s[i] == '\n') throw new StCompileException(line, $"Metin kapanmamış ({quote} eksik).");
                            char ch = s[i++];
                            if (ch == quote) break;
                            if (ch != '$')
                            {
                                text.Append(ch);
                                continue;
                            }

                            if (i >= s.Length) throw new StCompileException(line, "Metin $ ile bitiyor.");
                            char e = char.ToUpperInvariant(s[i]);
                            int hex = quote == '"' ? 4 : 2;
                            if (Uri.IsHexDigit(s[i]) && i + hex <= s.Length && s.Substring(i, hex).All(Uri.IsHexDigit))
                            {
                                text.Append((char)Convert.ToInt32(s.Substring(i, hex), 16));
                                i += hex;
                                continue;
                            }

                            text.Append(e switch
                            {
                                '$' => '$',
                                '\'' => '\'',
                                '"' => '"',
                                'L' or 'N' => '\n',
                                'P' => '\f',
                                'R' => '\r',
                                'T' => '\t',
                                _ => throw new StCompileException(line, $"Metinde '${s[i]}' tanınmadı ($$, $', $N, $R, $T, $L, $P ya da $ ve iki onaltılı rakam).")
                            });
                            i++;
                        }

                        list.Add(new Token(Tok.Text, text.ToString(), line));
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

                        if (i < s.Length && s[i] == '#' && word is "BOOL" or "INT" or "DINT" or "UINT" or "WORD" or "REAL" or "LREAL" or "STRING" or "WSTRING")
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
            internal static double Duration(string text, int line)
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
                "BOOL", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD", "REAL", "LREAL", "TIME",
                "STRING", "WSTRING"
            };

            private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
            {
                "ABS", "SQRT", "SIN", "COS", "TAN", "ATAN", "EXP", "LN", "TRUNC", "MIN", "MAX", "LIMIT", "SEL"
            };

            /// <summary>
            /// [plc-string] IEC 61131-3's string functions and their inputs in order (S a STRING, N an integer; "+" more of the
            /// last), by the names they take when given by name: MID(IN := s, L := 2, P := 3).
            /// </summary>
            private static readonly Dictionary<string, (string Kinds, string[] Names)> TextFunctions = new Dictionary<string, (string, string[])>(StringComparer.Ordinal)
            {
                ["LEN"] = ("S", new[] { "IN" }),
                ["LEFT"] = ("SN", new[] { "IN", "L" }),
                ["RIGHT"] = ("SN", new[] { "IN", "L" }),
                ["MID"] = ("SNN", new[] { "IN", "L", "P" }),
                ["CONCAT"] = ("S+", new[] { "IN1", "IN2", "IN3", "IN4", "IN5", "IN6", "IN7", "IN8" }),
                ["INSERT"] = ("SSN", new[] { "IN1", "IN2", "P" }),
                ["DELETE"] = ("SNN", new[] { "IN", "L", "P" }),
                ["REPLACE"] = ("SSNN", new[] { "IN1", "IN2", "L", "P" }),
                ["FIND"] = ("SS", new[] { "IN1", "IN2" })
            };

            private static bool Standard(string name) => Elementary.Contains(name) || Block.Make(name) != null || Functions.Contains(name) || TextFunctions.ContainsKey(name);

            private readonly List<Token> _t;
            private readonly Dictionary<string, Var> _vars = new Dictionary<string, Var>(StringComparer.Ordinal);
            private readonly List<string> _order = new List<string>();   // [plc-pou] declaration order (a FUNCTION's positional arguments)
            private readonly Dictionary<string, Pou> _pous;
            private readonly Dictionary<string, StructDef> _structs;
            private readonly Dictionary<string, Dictionary<string, int>> _enums;
            private readonly int _depth;
            private readonly string? _inside;   // "FUNCTION_BLOCK" or "FUNCTION" while compiling one, null for the program
            private int _i;

            public Parser(List<Token> tokens)
                : this(tokens, new Dictionary<string, Pou>(StringComparer.Ordinal), new Dictionary<string, StructDef>(StringComparer.Ordinal),
                    new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal), 0, null)
            {
            }

            private Parser(List<Token> tokens, Dictionary<string, Pou> pous, Dictionary<string, StructDef> structs, Dictionary<string, Dictionary<string, int>> enums, int depth, string? inside)
            {
                _t = tokens;
                _pous = pous;
                _structs = structs;
                _enums = enums;
                _depth = depth;
                _inside = inside;
            }
            /// <summary>
            /// [plc-struct] Takes the TYPE … END_TYPE parts out of the source and reads them: structures
            /// (<c>Point : STRUCT X, Y : REAL; END_STRUCT;</c>) and enumerations (<c>Mode : (Idle, Run := 10, Fault);</c>).
            /// </summary>
            private void ExtractTypes()
            {
                for (int i = 0; i < _t.Count; i++)
                {
                    if (!_t[i].Is("TYPE")) continue;
                    int j = i + 1;
                    while (j < _t.Count && _t[j].Kind != Tok.End && !_t[j].Is("END_TYPE")) j++;
                    if (j >= _t.Count || !_t[j].Is("END_TYPE")) throw new StCompileException(_t[i].Line, "TYPE için END_TYPE eksik.");
                    int last = j + 1 < _t.Count && _t[j + 1].Is(";") ? j + 1 : j;
                    var tokens = _t.GetRange(i + 1, j - i - 1);
                    tokens.Add(new Token(Tok.End, string.Empty, _t[j].Line));
                    _t.RemoveRange(i, last - i + 1);
                    i--;
                    new Parser(tokens, _pous, _structs, _enums, _depth, null).TypeDeclarations();
                }
            }

            private void TypeDeclarations()
            {
                while (Peek.Kind != Tok.End)
                {
                    var name = Ident("tür adı");
                    if (Standard(name.Text))
                        throw new StCompileException(name.Line, $"'{name.Text}' standart bir adın; başka bir ad verin.");
                    if (_structs.ContainsKey(name.Text) || _enums.ContainsKey(name.Text)) throw new StCompileException(name.Line, $"'{name.Text}' türü iki kez tanımlanmış.");
                    Expect(":", "':'");
                    if (Accept("STRUCT"))
                    {
                        var def = new StructDef { Name = name.Text };
                        while (!Accept("END_STRUCT"))
                        {
                            if (Peek.Kind == Tok.End) throw new StCompileException(name.Line, $"{name.Text}: END_STRUCT eksik.");
                            var fields = new List<Token> { Ident("alan adı") };
                            while (Accept(",")) fields.Add(Ident("alan adı"));
                            Expect(":", "':' (alanın türü)");
                            int lo = 0, length = 0;
                            int[]? dims = null, dimLos = null;
                            if (Accept("ARRAY")) (lo, length, dims, dimLos) = ArrayBounds(fields[0].Line);
                            var type = Ident("tür");
                            int maxLength = TextLength(type.Text);
                            Expr? init = null;
                            List<Expr>? inits = null;
                            if (Accept(":="))
                            {
                                if (length > 0) inits = ArrayInits();
                                else init = Expression();
                            }

                            Expect(";", "';'");
                            foreach (var f in fields)
                            {
                                if (def.Fields.Any(x => x.Name == f.Text)) throw new StCompileException(f.Line, $"{name.Text}: '{f.Text}' alanı iki kez.");
                                def.Fields.Add(new FieldDef
                                {
                                    Name = f.Text, TypeName = type.Text, Lo = lo, Length = length, Dims = dims, DimLos = dimLos, MaxLength = maxLength,
                                    Init = init, Inits = inits, Line = f.Line
                                });
                            }
                        }

                        if (def.Fields.Count == 0) throw new StCompileException(name.Line, $"{name.Text} yapısının alanı yok.");
                        _structs[name.Text] = def;
                    }
                    else if (Accept("("))
                    {
                        var values = new Dictionary<string, int>(StringComparer.Ordinal);
                        int next = 0;
                        do
                        {
                            var v = Ident("sayım değeri");
                            if (Accept(":="))
                            {
                                bool negative = Accept("-");
                                if (Peek.Kind != Tok.Number) throw new StCompileException(Peek.Line, "Sayım değeri bir tamsayı olmalı.");
                                next = (int)Next().Value * (negative ? -1 : 1);
                            }

                            if (values.ContainsKey(v.Text)) throw new StCompileException(v.Line, $"{name.Text}: '{v.Text}' iki kez.");
                            values[v.Text] = next++;
                        }
                        while (Accept(","));

                        Expect(")", "')'");
                        if (Accept(":=")) Ident("başlangıç değeri");
                        _enums[name.Text] = values;
                    }
                    else throw new StCompileException(Peek.Line, $"{name.Text}: STRUCT ya da '(' (sayım) bekleniyordu.");

                    Accept(";");
                }
            }

            /// <summary>[plc-struct] An enumeration's value by its name ("Run") or as "Mode#Run" (the name already read); null when it is not one.</summary>
            private int? EnumConstant(Token name)
            {
                if (_enums.TryGetValue(name.Text, out var typed) && Peek.Is("#"))
                {
                    Next();
                    var v = Ident("sayım değeri");
                    if (!typed.TryGetValue(v.Text, out int x)) throw new StCompileException(v.Line, $"{name.Text} sayımında '{v.Text}' yok ({string.Join(", ", typed.Keys)}).");
                    return x;
                }

                foreach (var e in _enums.Values)
                    if (e.TryGetValue(name.Text, out int x)) return x;
                return null;
            }

            /// <summary>
            /// [plc-struct] A structure variable opened up: a marker for the whole, and a variable per field ("P.X"); in an
            /// array of structures each field is an array ("A.X", shown as A[3].X). An array inside an array of structures
            /// (two dimensions) is not supported.
            /// </summary>
            private Var DeclareStruct(string name, StructDef def, char section, int lo, int count, int[]? dims, int[]? dimLos, string? root, string path, int line, int depth = 0)
            {
                if (depth > 8) throw new StCompileException(line, $"{def.Name}: yapılar 8 kattan derin iç içe (kendini mi içeriyor?).");
                var marker = new Var
                {
                    Name = name, Type = StType.Struct, TypeName = def.Name, Struct = def, Section = section, Lo = lo, StructCount = count, Dims = dims, DimLos = dimLos,
                    Leaves = new List<Var>()
                };
                _vars[name] = marker;
                foreach (var f in def.Fields)
                {
                    string full = name + "." + f.Name;
                    if (_structs.TryGetValue(f.TypeName, out var inner))
                    {
                        Var child;
                        if (f.Length > 0)
                        {
                            if (count > 0) throw new StCompileException(f.Line, $"{def.Name}.{f.Name}: yapı dizisinin içinde dizi alanı olamaz; diziyi çok boyutlu tanımlayın (ARRAY[1..3, 1..4] OF …).");
                            child = DeclareStruct(full, inner, section, f.Lo, f.Length, f.Dims, f.DimLos, full, string.Empty, f.Line, depth + 1);
                        }
                        else child = DeclareStruct(full, inner, section, lo, count, dims, dimLos, root, path + "." + f.Name, f.Line, depth + 1);
                        marker.Leaves.AddRange(child.Leaves!);
                        continue;
                    }

                    var leaf = new Var { Name = full, Section = section };
                    if (Elementary.Contains(f.TypeName))
                    {
                        leaf.TypeName = f.TypeName;
                        leaf.Type = ElementaryType(f.TypeName);
                        leaf.MaxLength = f.MaxLength;
                    }
                    else if (_enums.ContainsKey(f.TypeName))
                    {
                        leaf.TypeName = "DINT";
                        leaf.Type = StType.Int;
                    }
                    else throw new StCompileException(f.Line, $"{def.Name}.{f.Name}: '{f.TypeName}' türü yok (yapı alanları BOOL, INT, REAL, TIME, STRING, sayım ya da başka bir yapı olabilir).");

                    if (f.Length > 0)
                    {
                        if (count > 0) throw new StCompileException(f.Line, $"{def.Name}.{f.Name}: yapı dizisinin içinde dizi alanı olamaz; diziyi çok boyutlu tanımlayın (ARRAY[1..3, 1..4] OF …).");
                        leaf.Lo = f.Lo;
                        leaf.Dims = f.Dims;
                        leaf.DimLos = f.DimLos;
                        MakeElements(leaf, f.Length);
                        if (f.Inits != null)
                        {
                            if (f.Inits.Count > f.Length) throw new StCompileException(f.Line, $"{def.Name}.{f.Name} {f.Length} elemanlı; {f.Inits.Count} başlangıç değeri verilmiş.");
                            for (int k = 0; k < f.Inits.Count; k++) InitializeElement(leaf, k, f.Inits[k]);
                        }
                    }
                    else if (count > 0)
                    {
                        leaf.Lo = lo;
                        leaf.Dims = dims;
                        leaf.DimLos = dimLos;
                        MakeElements(leaf, count);
                        leaf.DisplayRoot = root;
                        leaf.DisplayPath = path + "." + f.Name;
                        if (f.Init != null)
                            for (int k = 0; k < count; k++) InitializeElement(leaf, k, f.Init);
                    }
                    else if (f.Init != null) Initialize(leaf, f.Init);

                    Remember(leaf);
                    _vars[full] = leaf;
                    marker.Leaves.Add(leaf);
                }

                return marker;
            }

            /// <summary>[plc-string] An array's elements: values, or texts for an array of STRINGs.</summary>
            private static void MakeElements(Var v, int length)
            {
                if (v.Type == StType.String) v.Texts = Enumerable.Repeat(string.Empty, length).ToArray();
                else v.Elements = new double[length];
            }

            /// <summary>An array's initial elements, for a cold restart.</summary>
            private static void Remember(Var v)
            {
                if (v.Elements != null) v.InitialElements = (double[])v.Elements.Clone();
                if (v.Texts != null) v.InitialTexts = (string[])v.Texts.Clone();
            }

            /// <summary>A variable's initial value from a constant expression; a STRING takes only text and the others no text.</summary>
            private static void Initialize(Var v, Expr e)
            {
                CheckText(v.Name, v.Type, v.TypeName, e);
                if (v.Type == StType.String)
                {
                    v.SetText(e.Str(null!));
                    v.InitialText = v.Text;
                }
                else
                {
                    v.Set(e.Eval(null!));
                    v.Initial = v.Value;
                }
            }

            private static void InitializeElement(Var v, int k, Expr e)
            {
                CheckText(v.Name, v.Type, v.TypeName, e);
                if (v.Texts != null) v.Texts[k] = v.Clip(e.Str(null!));
                else v.Elements![k] = Coerce(v.Type, v.TypeName, e.Eval(null!));
            }

            /// <summary>[plc-string] A STRING takes text, the others take no text.</summary>
            private static void CheckText(string name, StType type, string typeName, Expr e)
            {
                if (type == StType.String && e.Type != StType.String)
                    throw new StCompileException(e.Line, $"{name} {typeName}: bir metin verin ('…' ya da INT_TO_STRING(…) gibi).");
                if (type != StType.String && e.Type == StType.String)
                    throw new StCompileException(e.Line, $"{name} {typeName}: metin verilemez (STRING_TO_{(type == StType.Bool ? "BOOL" : type == StType.Real ? "REAL" : type == StType.Time ? "TIME" : "INT")}(…) ile çevirin).");
            }

            /// <summary>
            /// [plc-array] After ARRAY: [lo..hi] or [lo..hi, lo..hi …] and OF. One dimension: its first index and length; more:
            /// the whole length, laid out row by row (Lo 0), with each dimension's length and first index.
            /// </summary>
            private (int Lo, int Length, int[]? Dims, int[]? Los) ArrayBounds(int line)
            {
                Expect("[", "'[' (dizinin sınırları)");
                var los = new List<int>();
                var dims = new List<int>();
                do
                {
                    int lo = Bound();
                    Expect("..", "'..'");
                    int hi = Bound();
                    if (hi < lo) throw new StCompileException(line, $"Dizinin sınırları ters: [{lo}..{hi}].");
                    if ((long)hi - lo + 1 > MaxElements) throw new StCompileException(line, $"Bir dizi en çok {MaxElements} eleman olabilir.");
                    los.Add(lo);
                    dims.Add(hi - lo + 1);
                }
                while (Accept(","));

                Expect("]", "']'");
                Expect("OF", "OF");
                if (dims.Count > 8) throw new StCompileException(line, "Bir dizi en çok 8 boyutlu olabilir.");
                long total = dims.Aggregate(1L, (a, d) => a * d);
                if (total > MaxElements) throw new StCompileException(line, $"Bir dizi en çok {MaxElements} eleman olabilir; bu {total}.");
                return dims.Count == 1 ? (los[0], dims[0], null, null) : (0, (int)total, dims.ToArray(), los.ToArray());
            }

            private const int MaxElements = 65536;

            /// <summary>An array's initial values: [1, 2, 3], with IEC 61131-3's repetition: [10(0), 2(1)] is ten 0s and two 1s.</summary>
            private List<Expr> ArrayInits()
            {
                Expect("[", "'[' (dizinin başlangıç değerleri)");
                var list = new List<Expr>();
                do
                {
                    if (Peek.Kind == Tok.Number && !Peek.Text.Contains('.') && _t[_i + 1].Is("("))
                    {
                        var n = Next();
                        Next();
                        var e = Expression();
                        Expect(")", "')'");
                        if (n.Value < 0 || n.Value > MaxElements) throw new StCompileException(n.Line, $"Tekrar sayısı {n.Value} geçersiz.");
                        for (int k = 0; k < (int)n.Value; k++) list.Add(e);
                    }
                    else list.Add(Expression());
                }
                while (Accept(","));

                Expect("]", "']'");
                return list;
            }

            /// <summary>[plc-string] STRING[n] or STRING(n) after the type's name: at most n characters (80 when not given).</summary>
            private int TextLength(string type)
            {
                if (type is not ("STRING" or "WSTRING")) return 80;
                string? close = Accept("[") ? "]" : Accept("(") ? ")" : null;
                if (close == null) return 80;
                var n = Peek;
                if (n.Kind != Tok.Number || n.Text.Contains('.')) throw new StCompileException(n.Line, $"STRING uzunluğu bir tamsayı olmalı, '{Shown(n)}' bulundu.");
                Next();
                if (n.Value < 1 || n.Value > 65535) throw new StCompileException(n.Line, $"STRING uzunluğu 1..65535 olmalı, {n.Value} verilmiş.");
                Expect(close, $"'{close}'");
                return (int)n.Value;
            }

            /// <summary>
            /// The indices inside [ … ] of an array's element, and the ']': one, or [plc-array] one per dimension (M[i, j]),
            /// turned into the element's place.
            /// </summary>
            private Expr Subscript(Var v)
            {
                string name = v.DisplayRoot ?? v.Name;
                var first = IndexExpression();
                if (v.Dims == null)
                {
                    if (Peek.Is(",")) throw new StCompileException(Peek.Line, $"{name} tek boyutlu: {name}[i] gibi bir indis alır.");
                    Expect("]", "']'");
                    return first;
                }

                var f = new Flatten { Line = first.Line, Name = name, Dims = v.Dims, Los = v.DimLos!, Type = StType.Int };
                f.Parts.Add(first);
                while (Accept(",")) f.Parts.Add(IndexExpression());
                Expect("]", "']'");
                if (f.Parts.Count != v.Dims.Length)
                    throw new StCompileException(first.Line, $"{name} {v.Dims.Length} boyutlu: {name}[{string.Join(", ", "ijklmnop".Take(v.Dims.Length))}] gibi {v.Dims.Length} indis alır.");
                return f;
            }

            /// <summary>
            /// [plc-struct] What a name followed by fields and indices reaches: a value (P.X, A[i].X, P.ARR[j]), or a whole
            /// structure (P, A[i]) with the element when it is one of an array of structures.
            /// </summary>
            private (Var Var, Expr? At) Access(Var root)
            {
                var cur = root;
                Expr? at = null;
                while (true)
                {
                    if (cur.Type == StType.Struct)
                    {
                        if (cur.StructCount > 0 && at == null)
                        {
                            if (Peek.Is(".")) throw new StCompileException(Peek.Line, $"{cur.Name} bir yapı dizisi: {cur.Name}[i].alan gibi.");
                            if (!Accept("[")) return (cur, null);
                            at = Subscript(cur);
                            continue;
                        }

                        if (!Accept(".")) return (cur, at);
                        var f = Ident("alan adı");
                        if (!_vars.TryGetValue(cur.Name + "." + f.Text, out var next))
                            throw new StCompileException(f.Line, $"{cur.TypeName} yapısının '{f.Text}' alanı yok ({string.Join(", ", cur.Struct!.Fields.Select(x => x.Name))}).");
                        cur = next;
                        continue;
                    }

                    if (cur.HasElements && cur.DisplayRoot == null && at == null)
                    {
                        Expect("[", $"'[' ({cur.Name} bir dizi: {cur.Name}[i] gibi)");
                        at = Subscript(cur);
                    }

                    return (cur, at);
                }
            }

            /// <summary>[plc-struct] A variable given for a VAR_IN_OUT: the same type and shape as the parameter.</summary>
            private (Var Outer, Expr? At) InOutArgument(Var param, string owner, int line)
            {
                var tok = Ident("değişken (VAR_IN_OUT bir değişken ister)");
                var root = Lookup(tok);
                var (outer, at) = root.Type == StType.Struct || root.HasElements && !param.HasElements ? Access(root) : (root, null);
                bool same = param.Type == StType.Struct
                    ? outer.Type == StType.Struct && outer.TypeName == param.TypeName && (outer.StructCount == 0 || at != null)
                    : outer.Type == param.Type && outer.Type != StType.Struct && outer.Type != StType.Block
                      && (!param.HasElements ? !outer.HasElements || at != null
                          : outer.HasElements && at == null && outer.Length == param.Length && (outer.Dims?.Length ?? 1) == (param.Dims?.Length ?? 1));
                if (!same) throw new StCompileException(line, $"{owner}.{param.Name} {Describe(param)} ister; {outer.Name} {Describe(outer)}.");
                return (outer, at);
            }

            private static string Describe(Var v) =>
                v.Type == StType.Struct ? (v.StructCount > 0 ? $"{v.TypeName} dizisi" : v.TypeName + " yapısı")
                : v.HasElements && v.DisplayRoot == null ? $"{v.Length} elemanlı {v.TypeName} dizisi" : v.TypeName;


            /// <summary>[plc-pou] A FUNCTION_BLOCK or FUNCTION of the source: its tokens, compiled when first used.</summary>
            private sealed class Pou
            {
                public string Kind = string.Empty;
                public string Name = string.Empty;
                public int Line;
                public List<Token> Tokens = new List<Token>();
                public UserFunction? Function;
                public bool Compiling;
            }

            /// <summary>
            /// [plc-pou] Takes the FUNCTION_BLOCK … END_FUNCTION_BLOCK and FUNCTION … END_FUNCTION parts out of the source (in
            /// any order, before or after the program), so that what is left is the program and each can be used by name.
            /// </summary>
            private void ExtractPous()
            {
                for (int i = 0; i < _t.Count; i++)
                {
                    string? kind = _t[i].Is("FUNCTION_BLOCK") ? "FUNCTION_BLOCK" : _t[i].Is("FUNCTION") ? "FUNCTION" : null;
                    if (kind == null) continue;
                    string end = "END_" + kind;
                    int j = i + 1;
                    while (j < _t.Count && _t[j].Kind != Tok.End && !_t[j].Is(end))
                    {
                        if (_t[j].Is("FUNCTION_BLOCK") || _t[j].Is("FUNCTION") || _t[j].Is("PROGRAM"))
                            throw new StCompileException(_t[j].Line, $"{kind} {end} ile kapanmadan {_t[j].Text} başlıyor.");
                        j++;
                    }

                    if (j >= _t.Count || !_t[j].Is(end)) throw new StCompileException(_t[i].Line, $"{kind} için {end} eksik.");
                    if (i + 1 >= j || _t[i + 1].Kind != Tok.Ident) throw new StCompileException(_t[i].Line, $"{kind}'dan sonra bir ad bekleniyordu.");
                    var name = _t[i + 1];
                    if (Standard(name.Text))
                        throw new StCompileException(name.Line, $"'{name.Text}' standart bir adın; başka bir ad verin.");
                    if (_pous.ContainsKey(name.Text)) throw new StCompileException(name.Line, $"'{name.Text}' iki kez tanımlanmış.");
                    int last = j + 1 < _t.Count && _t[j + 1].Is(";") ? j + 1 : j;
                    var tokens = _t.GetRange(i, last - i + 1);
                    tokens.Add(new Token(Tok.End, string.Empty, _t[last].Line));
                    _pous[name.Text] = new Pou { Kind = kind, Name = name.Text, Line = _t[i].Line, Tokens = tokens };
                    _t.RemoveRange(i, last - i + 1);
                    i--;
                }
            }

            /// <summary>[plc-pou] A new instance of a FUNCTION_BLOCK: its declarations and body compiled for this instance's own variables.</summary>
            private UserBlock Instantiate(Pou fb, int line)
            {
                if (_depth >= 16) throw new StCompileException(line, $"{fb.Name}: bloklar 16 kattan derin iç içe (bir blok kendini mi içeriyor?).");
                var sub = new Parser(new List<Token>(fb.Tokens), _pous, _structs, _enums, _depth + 1, "FUNCTION_BLOCK");
                sub.Expect("FUNCTION_BLOCK", "FUNCTION_BLOCK");
                sub.Next();
                while (sub.Peek.Is("VAR") || sub.Peek.Is("VAR_INPUT") || sub.Peek.Is("VAR_OUTPUT") || sub.Peek.Is("VAR_TEMP") || sub.Peek.Is("VAR_IN_OUT")) sub.Declarations();
                var body = sub.Statements("END_FUNCTION_BLOCK");
                sub.Expect("END_FUNCTION_BLOCK", "END_FUNCTION_BLOCK");
                return new UserBlock(sub._vars, sub._order, body);
            }

            /// <summary>[plc-pou] A FUNCTION, compiled once (it keeps nothing between calls); calling itself, even through another, is an error as in IEC 61131-3.</summary>
            private UserFunction CompileFunction(Pou f, int line)
            {
                if (f.Function != null) return f.Function;
                if (f.Compiling) throw new StCompileException(line, $"{f.Name} kendini çağırıyor: fonksiyonlar özyinelemeli olamaz.");
                f.Compiling = true;
                var sub = new Parser(new List<Token>(f.Tokens), _pous, _structs, _enums, _depth + 1, "FUNCTION");
                sub.Expect("FUNCTION", "FUNCTION");
                var name = sub.Next();
                sub.Expect(":", $"':' ({name.Text} fonksiyonunun dönüş türü)");
                var typeTok = sub.Ident("dönüş türü");
                if (!Elementary.Contains(typeTok.Text)) throw new StCompileException(typeTok.Line, $"{name.Text}: dönüş türü BOOL, INT, REAL, TIME ya da STRING gibi bir tür olmalı.");
                var result = new Var { Name = name.Text, TypeName = typeTok.Text, Type = ElementaryType(typeTok.Text), MaxLength = sub.TextLength(typeTok.Text) };
                sub._vars[result.Name] = result;
                while (sub.Peek.Is("VAR") || sub.Peek.Is("VAR_INPUT") || sub.Peek.Is("VAR_TEMP") || sub.Peek.Is("VAR_OUTPUT") || sub.Peek.Is("VAR_IN_OUT")) sub.Declarations();
                var fn = new UserFunction { Name = name.Text, Result = result, Vars = sub._vars };
                fn.Inputs.AddRange(sub._order.Select(n => sub._vars[n]).Where(v => v.Section == 'I'));
                fn.Params.AddRange(sub._order.Select(n => sub._vars[n]).Where(v => v.Section is 'I' or 'X'));
                fn.Body = sub.Statements("END_FUNCTION");   // a call of itself in here finds it still compiling
                sub.Expect("END_FUNCTION", "END_FUNCTION");
                f.Compiling = false;
                f.Function = fn;
                return fn;
            }

            private static StType ElementaryType(string type) =>
                type == "BOOL" ? StType.Bool : type is "REAL" or "LREAL" ? StType.Real : type == "TIME" ? StType.Time : type is "STRING" or "WSTRING" ? StType.String : StType.Int;

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
                ExtractTypes();
                ExtractPous();
                string name = "PLC";
                bool wrapped = Accept("PROGRAM");
                if (wrapped)
                {
                    if (Peek.Kind != Tok.Ident) throw new StCompileException(Peek.Line, "PROGRAM'dan sonra bir ad bekleniyordu.");
                    name = Next().Text;
                }

                while (Peek.Is("VAR") || Peek.Is("VAR_GLOBAL") || Peek.Is("VAR_INPUT") || Peek.Is("VAR_OUTPUT") || Peek.Is("VAR_TEMP") || Peek.Is("VAR_IN_OUT")) Declarations();
                var body = Statements(wrapped ? new[] { "END_PROGRAM" } : Array.Empty<string>());
                if (wrapped) Expect("END_PROGRAM", "END_PROGRAM");
                Accept(";");
                if (Peek.Kind != Tok.End) throw new StCompileException(Peek.Line, $"'{Shown(Peek)}' fazla.");
                return new StProgram(name, _vars, body);
            }

            private void Declarations()
            {
                var head = Next();
                if (head.Is("VAR_IN_OUT") && _inside == null) throw new StCompileException(head.Line, "VAR_IN_OUT yalnız FUNCTION_BLOCK ve FUNCTION içinde olur.");
                char section = _inside == null ? '\0' : head.Is("VAR_INPUT") ? 'I' : head.Is("VAR_OUTPUT") ? 'O' : head.Is("VAR_IN_OUT") ? 'X' : '\0';
                if (_inside == "FUNCTION" && head.Is("VAR_OUTPUT")) throw new StCompileException(head.Line, "Bir FUNCTION'ın çıkışı kendi adıdır; birden çok çıkış için FUNCTION_BLOCK kullanın.");
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

                    if (at != null && _inside != null) throw new StCompileException(names[0].Line, $"{_inside} içinde AT ile adres bağlanmaz; adresi programda bağlayıp girişle geçirin.");
                    Expect(":", "':' (tür)");
                    // [plc-pou] ARRAY[lo..hi] OF type; [plc-array] ARRAY[lo..hi, lo..hi] OF type
                    int lo = 0, length = 0;
                    int[]? dims = null, dimLos = null;
                    bool array = Accept("ARRAY");
                    if (array)
                    {
                        (lo, length, dims, dimLos) = ArrayBounds(names[0].Line);
                        if (at != null) throw new StCompileException(names[0].Line, "Bir diziye AT ile adres bağlanmaz.");
                    }

                    var typeTok = Ident("tür");
                    string type = typeTok.Text;
                    int maxLength = TextLength(type);
                    Expr? init = null;
                    List<Expr>? inits = null;
                    if (_structs.TryGetValue(type, out var structDef))
                    {
                        // [plc-struct] P : Point := (X := 1.0, Y := 2.0);
                        var fieldInits = new List<(Token Field, Expr Value)>();
                        if (Accept(":="))
                        {
                            if (array) throw new StCompileException(typeTok.Line, "Yapı dizisine başlangıç değeri verilmez; alanın türdeki başlangıç değeri kullanılır.");
                            Expect("(", "'(' (yapının başlangıç değerleri)");
                            do
                            {
                                var f = Ident("alan adı");
                                Expect(":=", "':='");
                                fieldInits.Add((f, Expression()));
                            }
                            while (Accept(","));
                            Expect(")", "')'");
                        }

                        Expect(";", "';'");
                        if (at != null) throw new StCompileException(names[0].Line, "Bir yapıya AT ile adres bağlanmaz.");
                        if (section is 'I' or 'O') throw new StCompileException(typeTok.Line, "Yapı giriş ya da çıkış olamaz; VAR_IN_OUT ile geçirin.");
                        foreach (var n in names)
                        {
                            if (_vars.ContainsKey(n.Text)) throw new StCompileException(n.Line, $"'{n.Text}' iki kez tanımlanmış.");
                            var marker = DeclareStruct(n.Text, structDef, section, lo, array ? length : 0, dims, dimLos, n.Text, string.Empty, n.Line);
                            foreach (var (f, value) in fieldInits)
                            {
                                if (!_vars.TryGetValue(n.Text + "." + f.Text, out var leaf) || leaf.Type == StType.Struct || leaf.HasElements)
                                    throw new StCompileException(f.Line, $"{structDef.Name} yapısının '{f.Text}' değer alanı yok.");
                                Initialize(leaf, value);
                            }

                            _order.Add(marker.Name);
                        }

                        continue;
                    }

                    bool enumType = _enums.ContainsKey(type);
                    if (enumType) type = "DINT";   // [plc-struct] an enumeration is kept as its value
                    if (Accept(":="))
                    {
                        if (array) inits = ArrayInits();
                        else init = Expression();
                    }

                    Expect(";", "';'");
                    foreach (var n in names)
                    {
                        if (_vars.ContainsKey(n.Text)) throw new StCompileException(n.Line, $"'{n.Text}' iki kez tanımlanmış.");
                        var v = new Var { Name = n.Text, TypeName = type, Section = section, Lo = lo, Dims = dims, DimLos = dimLos, MaxLength = maxLength };
                        Pou? fb = _pous.TryGetValue(type, out var pou) && pou.Kind == "FUNCTION_BLOCK" ? pou : null;
                        if (Elementary.Contains(type))
                        {
                            v.Type = ElementaryType(type);
                            if (array) MakeElements(v, length);
                        }
                        else if ((Block.Make(type) != null || fb != null) && section == 'X')
                        {
                            throw new StCompileException(typeTok.Line, "Bir blok örneği VAR_IN_OUT olamaz.");
                        }
                        else if (Block.Make(type) != null || fb != null)
                        {
                            if (_inside == "FUNCTION") throw new StCompileException(typeTok.Line, "Fonksiyonlar durum tutmaz: blok örneklerini FUNCTION_BLOCK içinde kullanın.");
                            if (section != '\0') throw new StCompileException(typeTok.Line, "Bir blok örneği giriş ya da çıkış olamaz; VAR içinde tanımlayın.");
                            v.Type = StType.Block;
                            Block Make() => fb != null ? Instantiate(fb, typeTok.Line) : Block.Make(type)!;
                            if (array)
                            {
                                v.Blocks = new Block[length];
                                for (int k = 0; k < length; k++) v.Blocks[k] = Make();
                            }
                            else v.Block = Make();
                        }
                        else
                        {
                            string own = _pous.Count == 0 ? string.Empty : ", " + string.Join(", ", _pous.Values.Where(x => x.Kind == "FUNCTION_BLOCK").Select(x => x.Name));
                            throw new StCompileException(typeTok.Line, $"'{type}' türü yok (BOOL, INT, REAL, TIME, STRING, TON, TOF, TP, R_TRIG, F_TRIG, CTU, CTD, CTUD, SR, RS, MB_READ, MB_WRITE, UA_READ, UA_WRITE{own}).");
                        }

                        if (at != null) Bind(v, at, n.Line);
                        if (init != null)
                        {
                            if (v.Type == StType.Block) throw new StCompileException(n.Line, "Bir blok örneğine başlangıç değeri verilmez.");
                            Initialize(v, init);
                        }

                        if (v.HasElements)
                        {
                            if (inits != null)
                            {
                                if (inits.Count > length) throw new StCompileException(n.Line, $"{n.Text} {length} elemanlı; {inits.Count} başlangıç değeri verilmiş.");
                                for (int k = 0; k < inits.Count; k++) InitializeElement(v, k, inits[k]);
                            }

                            Remember(v);
                        }
                        else if (inits != null) throw new StCompileException(n.Line, "Blok dizisine başlangıç değeri verilmez.");

                        _vars[v.Name] = v;
                        _order.Add(v.Name);
                    }
                }
            }

            /// <summary>An array bound: a (signed) integer.</summary>
            private int Bound()
            {
                bool negative = Accept("-");
                if (Peek.Kind != Tok.Number || Peek.Text.Contains('.')) throw new StCompileException(Peek.Line, $"Dizi sınırı bir tamsayı olmalı, '{Shown(Peek)}' bulundu.");
                int v = (int)Next().Value;
                return negative ? -v : v;
            }

            private static void Bind(Var v, string address, int line)
            {
                var (area, index) = Address(address, line);
                if (v.Type == StType.String) throw new StCompileException(line, $"{v.Name} bir metin: AT ile adrese bağlanmaz.");
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
                if (_inside != null) throw new StCompileException(a.Line, $"{_inside} içinde doğrudan adres ({a.Text}) kullanılmaz; programda okuyup girişle geçirin.");
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

                if (target.Type == StType.Struct)
                {
                    // [plc-struct] P.X := …;  A[i].X := …;  P := Q;  A[i] := P;
                    var (dst, dstAt) = Access(target);
                    Expect(":=", "':='");
                    if (dst.Type == StType.Struct)
                    {
                        if (dst.StructCount > 0 && dstAt == null) throw new StCompileException(t.Line, $"{dst.Name} bir yapı dizisi: bütünü atanmaz, {dst.Name}[i] := … gibi.");
                        var srcTok = Ident($"bir {dst.TypeName} yapısı");
                        var srcRoot = Lookup(srcTok);
                        var (src, srcAt) = srcRoot.Type == StType.Struct ? Access(srcRoot) : (srcRoot, null);
                        if (src.Type != StType.Struct || src.TypeName != dst.TypeName || (src.StructCount > 0 && srcAt == null))
                            throw new StCompileException(srcTok.Line, $"{dst.Name} bir {dst.TypeName} yapısı; {src.Name} {Describe(src)}.");
                        Expect(";", "';'");
                        return new StructAssign { Line = t.Line, Dst = dst, DstAt = dstAt, Src = src, SrcAt = srcAt };
                    }

                    var fe = Expression();
                    Expect(";", "';' (komutun sonu)");
                    Check(dst, fe);
                    return dstAt != null ? new AssignElement { Line = t.Line, Target = dst, At = dstAt, Value = fe } : new Assign { Line = t.Line, Target = dst, Value = fe };
                }

                if (target.IsArray)
                {
                    // [plc-pou] A[i] := x;  T[i](IN := …);  T[i].PT := …;
                    Expect("[", $"'[' ({target.Name} bir dizi: {target.Name}[i] gibi)");
                    var at = Subscript(target);
                    if (target.Blocks != null)
                    {
                        var sample = target.Blocks[0];
                        if (Accept("."))
                        {
                            var m = Ident("blok girişi");
                            if (!sample.Inputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{target.TypeName} bloğunun '{m.Text}' girişi yok ({string.Join(", ", sample.Inputs)}).");
                            Expect(":=", "':='");
                            var value = Expression();
                            Expect(";", "';'");
                            CheckText($"{target.Name}[i].{m.Text}", MemberType(sample, m.Text), target.TypeName, value);
                            return new AssignElement { Line = t.Line, Target = target, At = at, Member = m.Text, Value = value };
                        }

                        var call = (BlockCall)BlockCallStatement(target, t.Line, sample);
                        call.At = at;
                        return call;
                    }

                    Expect(":=", "':='");
                    var ev = Expression();
                    Expect(";", "';' (komutun sonu)");
                    Check(target, ev);
                    return new AssignElement { Line = t.Line, Target = target, At = at, Value = ev };
                }

                if (target.Type == StType.Block)
                {
                    if (Accept("."))
                    {
                        var m = Ident("blok girişi");
                        if (!target.Block!.Inputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{target.TypeName} bloğunun '{m.Text}' girişi yok ({string.Join(", ", target.Block.Inputs)}).");
                        Expect(":=", "':='");
                        var value = Expression();
                        Expect(";", "';'");
                        CheckText($"{target.Name}.{m.Text}", MemberType(target.Block, m.Text), target.TypeName, value);
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
                CheckText(target.DisplayRoot != null ? target.DisplayRoot + "[i]" + target.DisplayPath : target.Name, target.Type, target.TypeName, e);
                if (target.Type == StType.Bool && e.Type != StType.Bool && e is not Const)
                    throw new StCompileException(e.Line, $"{target.Name} BOOL: sağ taraf bir koşul olmalı (örneğin x > 0).");
                if (target.Type != StType.Bool && e.Type == StType.Bool)
                    throw new StCompileException(e.Line, $"{target.Name} {target.TypeName}: BOOL atanamaz (BOOL_TO_INT kullanın).");
            }

            private Expr IndexExpression()
            {
                var e = Expression();
                if (e.Type is StType.Bool or StType.Real or StType.String) throw new StCompileException(e.Line, "Dizi indisi bir tamsayı olmalı.");
                return e;
            }

            private Stmt BlockCallStatement(Var instance, int line, Block? sample = null)
            {
                var call = new BlockCall { Line = line, Instance = instance };
                var block = sample ?? instance.Block!;
                Expect("(", "'(' (blok çağrısı)");
                if (!Accept(")"))
                {
                    do
                    {
                        var m = Ident("blok parametresi");
                        if (Accept(":="))
                        {
                            if (block is UserBlock ub && ub.InOuts.Contains(m.Text))
                            {
                                // [plc-struct] VAR_IN_OUT: the caller's own variable, copied in and back
                                var (outer, outerAt) = InOutArgument(ub.Inner(m.Text), instance.TypeName, m.Line);
                                call.InOuts.Add((m.Text, outer, outerAt));
                                continue;
                            }

                            if (!block.Inputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{instance.TypeName} bloğunun '{m.Text}' girişi yok ({string.Join(", ", block.Inputs)}).");
                            var value = Expression();
                            CheckText($"{instance.TypeName}.{m.Text}", MemberType(block, m.Text), instance.TypeName, value);
                            call.Inputs.Add((m.Text, value));
                        }
                        else if (Accept("=>"))
                        {
                            if (!block.Outputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{instance.TypeName} bloğunun '{m.Text}' çıkışı yok ({string.Join(", ", block.Outputs)}).");
                            var tok = Peek;
                            var target = tok.Kind == Tok.Address ? Direct(Next()) : Lookup(Ident("değişken"));
                            if ((MemberType(block, m.Text) == StType.String) != (target.Type == StType.String) || target.IsArray || target.Type is StType.Struct or StType.Block)
                                throw new StCompileException(m.Line, $"{instance.TypeName}.{m.Text} {target.Name} değişkenine verilemez: türleri uymuyor.");
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

                if (block is UserBlock withInOut)
                {
                    var missing = withInOut.InOuts.Where(n => call.InOuts.All(x => x.Name != n)).ToList();
                    if (missing.Count > 0) throw new StCompileException(line, $"{instance.TypeName}: VAR_IN_OUT '{string.Join("', '", missing)}' her çağrıda verilmeli.");
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
                if (s.Selector.Type is StType.Real or StType.Bool or StType.String) throw new StCompileException(line, "CASE bir tamsayı üzerinde çalışır.");
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
                if (_t[j].Kind == Tok.Ident)
                {
                    // [plc-struct] an enumeration's value: Run: or Mode#Run:
                    bool known = _enums.Values.Any(e => e.ContainsKey(_t[j].Text)) || _enums.ContainsKey(_t[j].Text) && _t[j + 1].Is("#");
                    if (!known) return false;
                    j += _t[j + 1].Is("#") ? 3 : 1;
                    return _t[j].Is(":") || _t[j].Is(",") || _t[j].Is("..");
                }

                if (_t[j].Is("-")) j++;
                if (_t[j].Kind != Tok.Number) return false;
                j++;
                return _t[j].Is(":") || _t[j].Is(",") || _t[j].Is("..");
            }

            private double Label()
            {
                if (Peek.Kind == Tok.Ident && !_vars.ContainsKey(Peek.Text))
                {
                    var name = Next();
                    return EnumConstant(name) ?? throw new StCompileException(name.Line, $"Durum etiketi bir tamsayı ya da sayım değeri olmalı, '{name.Text}' bulundu.");
                }

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
                if (new[] { from, to, by }.Any(x => x != null && x.Type is StType.String or StType.Bool)) throw new StCompileException(line, "FOR sınırları sayı olmalı.");
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
                if (a.Type == StType.String || b.Type == StType.String) throw new StCompileException(line, $"{op} metinle yapılmaz.");
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
                    a = Compare(op, a, Relation());
                }

                return a;
            }

            private Expr Relation()
            {
                var a = Sum();
                while (Peek.Is("<") || Peek.Is("<=") || Peek.Is(">") || Peek.Is(">="))
                {
                    var op = Next();
                    a = Compare(op, a, Sum());
                }

                return a;
            }

            /// <summary>A comparison: [plc-string] texts only with texts (character by character), numbers with numbers.</summary>
            private static Expr Compare(Token op, Expr a, Expr b)
            {
                if ((a.Type == StType.String) != (b.Type == StType.String))
                    throw new StCompileException(op.Line, "Bir metin yalnız bir metinle karşılaştırılır (INT_TO_STRING ya da STRING_TO_INT ile çevirin).");
                return new Binary { Line = op.Line, Op = op.Text, A = a, B = b, Type = StType.Bool };
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
                    Arith(a, b, op.Line);
                    return new Binary { Line = op.Line, Op = "**", A = a, B = b, Type = StType.Real };
                }

                return a;
            }

            private static StType Arith(Expr a, Expr b, int line)
            {
                if (a.Type == StType.String || b.Type == StType.String)
                    throw new StCompileException(line, "Metinle hesap yapılmaz: birleştirmek için CONCAT, sayıya çevirmek için STRING_TO_INT kullanın.");
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
                    if (a.Type == StType.String) throw new StCompileException(line, "NOT metinle yapılmaz.");
                    return new Unary { Line = line, Op = "NOT", A = a, Type = a.Type == StType.Bool ? StType.Bool : StType.Int };
                }

                if (Peek.Is("-"))
                {
                    int line = Next().Line;
                    var a = Unary();
                    if (a.Type == StType.String) throw new StCompileException(line, "Bir metnin eksisi olmaz.");
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
                    case Tok.Text:
                        return new Const { Line = t.Line, Text = t.Text, Type = StType.String };
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
                        if (Peek.Is("(") && TextFunctions.TryGetValue(t.Text, out var signature)) return TextCall(t, signature);
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
                            CheckCallText(t, call.Args);
                            call.Type = CallType(t.Text, call.Args);
                            return call;
                        }

                        if (Peek.Is("(") && _pous.TryGetValue(t.Text, out var pou) && pou.Kind == "FUNCTION") return FunctionCallExpression(t, pou);
                        if (!_vars.ContainsKey(t.Text) && EnumConstant(t) is int constant) return new Const { Line = t.Line, Value = constant, Type = StType.Int };
                        var v = Lookup(t);
                        if (v.Type == StType.Struct)
                        {
                            var (field, fieldAt) = Access(v);
                            if (field.Type == StType.Struct) throw new StCompileException(t.Line, $"{field.Name} bir yapı: bir değer değil, alanlarından birini kullanın ({field.Name}.{field.Struct!.Fields[0].Name} gibi).");
                            return fieldAt != null ? new Index { Line = t.Line, Var = field, At = fieldAt, Type = field.Type } : new Ref { Line = t.Line, Var = field, Type = field.Type };
                        }

                        if (v.IsArray)
                        {
                            Expect("[", $"'[' ({v.Name} bir dizi: {v.Name}[i] gibi)");
                            var at = Subscript(v);
                            if (v.Blocks == null) return new Index { Line = t.Line, Var = v, At = at, Type = v.Type };
                            Expect(".", $"'.' ({v.Name}[i] bir blok: {v.Name}[i].Q gibi)");
                            var bm = Ident("blok çıkışı");
                            var sample = v.Blocks[0];
                            if (!sample.Outputs.Contains(bm.Text) && !sample.Inputs.Contains(bm.Text))
                                throw new StCompileException(bm.Line, $"{v.TypeName} bloğunun '{bm.Text}' üyesi yok ({string.Join(", ", sample.Outputs)}).");
                            return new IndexedMember { Line = t.Line, Var = v, At = at, Name = bm.Text, Type = MemberType(sample, bm.Text) };
                        }

                        if (v.Type == StType.Block)
                        {
                            Expect(".", $"'.' ({v.Name} bir blok: {v.Name}.Q gibi)");
                            var m = Ident("blok çıkışı");
                            if (!v.Block!.Outputs.Contains(m.Text) && !v.Block.Inputs.Contains(m.Text))
                                throw new StCompileException(m.Line, $"{v.TypeName} bloğunun '{m.Text}' üyesi yok ({string.Join(", ", v.Block.Outputs)}).");
                            return new Member { Line = t.Line, Var = v, Name = m.Text, Type = MemberType(v.Block, m.Text) };
                        }

                        return new Ref { Line = t.Line, Var = v, Type = v.Type };
                    }

                    default:
                        throw new StCompileException(t.Line, $"Bir değer bekleniyordu, '{Shown(t)}' bulundu.");
                }
            }

            private static StType MemberType(Block b, string member) =>
                b.TypeOf(member) ?? (member is "URL" or "NODE" or "TEXT" ? StType.String : member is "Q" or "Q1" or "QU" or "QD" or "IN" or "CLK" or "CU" or "CD" or "R" or "LD" or "S" or "S1" or "R1" or "REQ" or "DONE" or "BUSY" or "ERROR" ? StType.Bool
                    : member is "ET" or "PT" ? StType.Time : StType.Int);

            /// <summary>[plc-pou] Scale(raw := x, hi := 10.0) or Scale(x, 0.0, 10.0): named arguments, or positional in the order of VAR_INPUT.</summary>
            private Expr FunctionCallExpression(Token name, Pou pou)
            {
                var fn = CompileFunction(pou, name.Line);
                var call = new FunctionCall { Line = name.Line, Function = fn, Type = fn.Result.Type };
                Expect("(", "'('");
                if (!Accept(")"))
                {
                    int position = 0;
                    do
                    {
                        Var input;
                        if (Peek.Kind == Tok.Ident && _t[_i + 1].Is(":="))
                        {
                            var arg = Next();
                            Next();
                            input = fn.Params.FirstOrDefault(x => x.Name == arg.Text)
                                    ?? throw new StCompileException(arg.Line, $"{fn.Name} fonksiyonunun '{arg.Text}' girişi yok ({string.Join(", ", fn.Params.Select(x => x.Name))}).");
                        }
                        else
                        {
                            if (position >= fn.Params.Count) throw new StCompileException(Peek.Line, $"{fn.Name} {fn.Params.Count} giriş alır.");
                            input = fn.Params[position];
                        }

                        if (call.Args.Any(a => a.Input == input) || call.InOuts.Any(a => a.Inner == input)) throw new StCompileException(name.Line, $"{fn.Name}: '{input.Name}' iki kez verilmiş.");
                        if (input.Section == 'X')
                        {
                            // [plc-struct] VAR_IN_OUT: the caller's own variable, copied in and back
                            var (outer, outerAt) = InOutArgument(input, fn.Name, name.Line);
                            call.InOuts.Add((input, outer, outerAt));
                            position++;
                            continue;
                        }

                        var value = Expression();
                        CheckText($"{fn.Name}.{input.Name}", input.Type, input.TypeName, value);
                        if (input.Type == StType.Bool && value.Type != StType.Bool && value is not Const)
                            throw new StCompileException(value.Line, $"{fn.Name}.{input.Name} BOOL: bir koşul verin.");
                        if (input.Type != StType.Bool && value.Type == StType.Bool)
                            throw new StCompileException(value.Line, $"{fn.Name}.{input.Name} {input.TypeName}: BOOL verilemez.");
                        call.Args.Add((input, value));
                        position++;
                    }
                    while (Accept(","));

                    Expect(")", "')'");
                }

                var absent = fn.Params.Where(x => x.Section == 'X' && call.InOuts.All(a => a.Inner != x)).ToList();
                if (absent.Count > 0) throw new StCompileException(name.Line, $"{fn.Name}: VAR_IN_OUT '{string.Join("', '", absent.Select(x => x.Name))}' her çağrıda verilmeli.");
                return call;
            }

            /// <summary>
            /// [plc-string] A string function: its inputs in order or by name (MID(IN := s, L := 2, P := 3)), each a STRING or an
            /// integer as the function takes it.
            /// </summary>
            private Expr TextCall(Token name, (string Kinds, string[] Names) signature)
            {
                Next();
                var given = new List<(Token? Name, Expr Value)>();
                if (!Accept(")"))
                {
                    do
                    {
                        Token? arg = null;
                        if (Peek.Kind == Tok.Ident && _t[_i + 1].Is(":="))
                        {
                            arg = Next();
                            Next();
                        }

                        given.Add((arg, Expression()));
                    }
                    while (Accept(","));
                    Expect(")", "')'");
                }

                bool more = signature.Kinds.EndsWith("+", StringComparison.Ordinal);
                string kinds = signature.Kinds.TrimEnd('+');
                var args = new Expr?[more ? Math.Max(given.Count, kinds.Length + 1) : kinds.Length];
                for (int k = 0; k < given.Count; k++)
                {
                    int at = k;
                    if (given[k].Name is Token n)
                    {
                        at = Array.IndexOf(signature.Names, n.Text);
                        if (at < 0) throw new StCompileException(n.Line, $"{name.Text} fonksiyonunun '{n.Text}' girişi yok ({string.Join(", ", signature.Names.Take(more ? 2 : signature.Names.Length))}{(more ? " …" : string.Empty)}).");
                    }

                    if (at >= args.Length) throw new StCompileException(name.Line, $"{name.Text} {kinds.Length} değer alır.");
                    if (args[at] != null) throw new StCompileException(name.Line, $"{name.Text}: '{signature.Names[at]}' iki kez verilmiş.");
                    args[at] = given[k].Value;
                }

                if (args.Any(a => a == null))
                    throw new StCompileException(name.Line, more
                        ? $"{name.Text} en az iki metin ister."
                        : $"{name.Text} {kinds.Length} değer ister: {name.Text}({string.Join(", ", signature.Names)}).");
                var call = new Call { Line = name.Line, Name = name.Text };
                for (int k = 0; k < args.Length; k++)
                {
                    var a = args[k]!;
                    bool text = (k < kinds.Length ? kinds[k] : kinds[kinds.Length - 1]) == 'S';
                    if (text && a.Type != StType.String) throw new StCompileException(a.Line, $"{name.Text}: {k + 1}. değer bir metin olmalı.");
                    if (!text && a.Type is StType.String or StType.Bool or StType.Real) throw new StCompileException(a.Line, $"{name.Text}: {k + 1}. değer bir tamsayı (uzunluk ya da konum) olmalı.");
                    call.Args.Add(a);
                }

                call.Type = name.Text is "LEN" or "FIND" ? StType.Int : StType.String;
                return call;
            }

            /// <summary>[plc-string] The other functions take numbers; X_TO_Y takes text only from STRING (STRING_TO_INT) and SEL picks between two of a kind.</summary>
            private static void CheckCallText(Token name, List<Expr> args)
            {
                if (name.Text.Contains("_TO_"))
                {
                    bool fromText = name.Text.StartsWith("STRING_TO_", StringComparison.Ordinal) || name.Text.StartsWith("WSTRING_TO_", StringComparison.Ordinal);
                    if (fromText && args[0].Type != StType.String) throw new StCompileException(name.Line, $"{name.Text} bir metin ister.");
                    if (!fromText && args[0].Type == StType.String) throw new StCompileException(name.Line, $"{name.Text} bir sayı ister; metin için STRING_TO_… kullanın.");
                    return;
                }

                if (name.Text == "SEL")
                {
                    if (args[0].Type == StType.String) throw new StCompileException(name.Line, "SEL'in ilk değeri bir koşul olmalı.");
                    if ((args[1].Type == StType.String) != (args[2].Type == StType.String)) throw new StCompileException(name.Line, "SEL'in iki seçeneği aynı türde olmalı.");
                    return;
                }

                if (args.Any(a => a.Type == StType.String)) throw new StCompileException(name.Line, $"{name.Text} sayı ister; metin verilmiş.");
            }

            private static StType CallType(string name, List<Expr> args)
            {
                if (name.Contains("_TO_"))
                {
                    string to = name.Substring(name.IndexOf("_TO_", StringComparison.Ordinal) + 4);
                    return to == "BOOL" ? StType.Bool : to is "REAL" or "LREAL" ? StType.Real : to == "TIME" ? StType.Time : to is "STRING" or "WSTRING" ? StType.String : StType.Int;
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
