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
        Block
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

        /// <summary>The variables by name (upper case), for watching: their type and value as text.</summary>
        public IEnumerable<(string Name, StType Type, string Value)> Watch =>
            _vars.Values.Where(v => v.Type != StType.Block).SelectMany(v => v.Elements != null
                ? v.Elements.Select((x, i) => (v.Name + "[" + (v.Lo + i).ToString(CultureInfo.InvariantCulture) + "]", v.Type, Format(v.Type, x)))
                : new[] { (v.Name, v.Type, Format(v.Type, v.Value)) });

        public double Read(string name)
        {
            string key = name.Trim().ToUpperInvariant();
            if (Element(key) is var (array, index)) return array.Elements![index];   // an array's element: A[3]
            int dot = key.IndexOf('.');
            if (dot > 0 && !key.StartsWith("%", StringComparison.Ordinal))
                return _vars.TryGetValue(key.Substring(0, dot), out var b) && b.Block != null ? b.Block[key.Substring(dot + 1)] : double.NaN;   // a block's member: T1.Q
            return _vars.TryGetValue(key, out var v) && !v.IsArray ? v.Value : double.NaN;
        }

        /// <summary>[plc-pou] "A[3]": the array and the element's place in it, or null.</summary>
        private (Var Array, int Index)? Element(string key)
        {
            int open = key.IndexOf('[');
            if (open <= 0 || !key.EndsWith("]", StringComparison.Ordinal)) return null;
            if (!_vars.TryGetValue(key.Substring(0, open), out var v) || v.Elements == null) return null;
            if (!int.TryParse(key.Substring(open + 1, key.Length - open - 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return null;
            return i >= v.Lo && i < v.Lo + v.Elements.Length ? (v, i - v.Lo) : null;
        }

        /// <summary>
        /// [hmi] Writes a variable from outside (an operator panel, a test), as an HMI writes a PLC's memory: it holds until the
        /// program changes it. False for a name that is not there, a block, or an input (its pin sets it every scan).
        /// </summary>
        public bool Write(string name, double value)
        {
            if (Element(name.Trim().ToUpperInvariant()) is var (array, index))
            {
                array.Elements![index] = Coerce(array.Type, array.TypeName, value);
                return true;
            }

            if (!_vars.TryGetValue(name.Trim().ToUpperInvariant(), out var v) || v.Type == StType.Block || v.IsArray || v.Area == 'I') return false;
            v.Set(value);
            if (v.Area == 'Q') Outputs[v.Address] = v.Value != 0;
            else if (v.Area == 'M') Memory[v.Address] = (short)Wrap("INT", v.Value);
            return true;
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

            public bool IsArray => Elements != null || Blocks != null;

            public int Length => Elements?.Length ?? Blocks?.Length ?? 0;

            /// <summary>An element's place from its index, or a run-time fault when the index is outside the array (as a PLC stops).</summary>
            public int Slot(double index, int line)
            {
                long i = (long)Math.Floor(index);
                if (i < Lo || i >= Lo + Length) throw new StRunException($"{line}. satırda {Name}[{i}]: dizi {Lo}..{Lo + Length - 1} arası.");
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
                Block?.Reset();
                if (Elements != null && InitialElements != null) Array.Copy(InitialElements, Elements, Elements.Length);
                if (Blocks != null)
                    foreach (var b in Blocks) b.Reset();
            }

            public void Set(double v) => Value = Coerce(Type, TypeName, v);
        }

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

            public virtual void Reset() => Members.Clear();

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
            private readonly List<string> _inputs, _outputs;

            public UserBlock(Dictionary<string, Var> vars, List<string> order, List<Stmt> body)
            {
                _vars = vars;
                _body = body;
                _inputs = order.Where(n => vars[n].Section == 'I').ToList();
                _outputs = order.Where(n => vars[n].Section == 'O').ToList();
                Publish();
            }

            public override IReadOnlyCollection<string> Inputs => _inputs;

            public override IReadOnlyCollection<string> Outputs => _outputs;

            public override StType? TypeOf(string member) => _vars.TryGetValue(member, out var v) && v.Section != '\0' && !v.IsArray ? v.Type : null;

            public override void Call(double clock) => throw new StRunException("Blok programsız çağrıldı.");

            public override void Call(double clock, StProgram program)
            {
                foreach (string name in _inputs)
                    if (Members.TryGetValue(name, out double v)) _vars[name].Set(v);
                program.Run(_body);   // RETURN (or EXIT outside a loop) ends the call
                Publish();
            }

            public override void Reset()
            {
                Members.Clear();
                foreach (var v in _vars.Values) v.Reset();
                Publish();
            }

            private void Publish()
            {
                foreach (string name in _inputs) Members[name] = _vars[name].Value;
                foreach (string name in _outputs) Members[name] = _vars[name].Value;
            }
        }

        /// <summary>[plc-pou] One of the program's own FUNCTIONs: compiled once, its variables fresh at every call.</summary>
        private sealed class UserFunction
        {
            public string Name = string.Empty;
            public Var Result = null!;
            public readonly List<Var> Inputs = new List<Var>();
            public Dictionary<string, Var> Vars = new Dictionary<string, Var>(StringComparer.Ordinal);
            public List<Stmt> Body = new List<Stmt>();
        }

        private abstract class Expr
        {
            public int Line;
            public StType Type;

            public abstract double Eval(StProgram p);
        }

        private sealed class Const : Expr
        {
            public double Value;

            public override double Eval(StProgram p) => Value;
        }

        private sealed class Ref : Expr
        {
            public Var Var = null!;

            public override double Eval(StProgram p) => Var.Value;
        }

        private sealed class Member : Expr
        {
            public Var Var = null!;
            public string Name = string.Empty;

            public override double Eval(StProgram p) => Var.Block![Name];
        }

        /// <summary>[plc-pou] An array's element: <c>A[i]</c>.</summary>
        private sealed class Index : Expr
        {
            public Var Var = null!;
            public Expr At = null!;

            public override double Eval(StProgram p) => Var.Elements![Var.Slot(At.Eval(p), Line)];
        }

        /// <summary>[plc-pou] A member of a block in an array: <c>T[i].Q</c>.</summary>
        private sealed class IndexedMember : Expr
        {
            public Var Var = null!;
            public Expr At = null!;
            public string Name = string.Empty;

            public override double Eval(StProgram p) => Var.Blocks![Var.Slot(At.Eval(p), Line)][Name];
        }

        /// <summary>[plc-pou] A call of one of the program's FUNCTIONs: the arguments first, then fresh variables, then the body.</summary>
        private sealed class FunctionCall : Expr
        {
            public UserFunction Function = null!;
            public List<(Var Input, Expr Value)> Args = new List<(Var, Expr)>();

            public override double Eval(StProgram p)
            {
                var values = new double[Args.Count];
                for (int i = 0; i < Args.Count; i++) values[i] = Args[i].Value.Eval(p);
                foreach (var v in Function.Vars.Values) v.Reset();
                for (int i = 0; i < Args.Count; i++) Args[i].Input.Set(values[i]);
                p.Spend();
                p.Run(Function.Body);
                return Function.Result.Value;
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

            public override double Eval(StProgram p)
            {
                double A(int i) => Args[i].Eval(p);
                switch (Name)
                {
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
                double v = Value.Eval(p);
                if (Member != null) Target.Blocks![slot][Member] = v;
                else Target.Elements![slot] = Coerce(Target.Type, Target.TypeName, v);
                return Flow.Next;
            }
        }

        private sealed class BlockCall : Stmt
        {
            public Var Instance = null!;
            public Expr? At;   // [plc-pou] the element, for a block in an array: T[i](…)
            public List<(string Name, Expr Value)> Inputs = new List<(string, Expr)>();
            public List<(string Name, Var Target)> Outputs = new List<(string, Var)>();

            public override Flow Run(StProgram p)
            {
                var b = At != null ? Instance.Blocks![Instance.Slot(At.Eval(p), Line)] : Instance.Block!;
                foreach (var (name, value) in Inputs) b[name] = value.Eval(p);
                b.Call(p.Clock, p);
                foreach (var (name, target) in Outputs) target.Set(b[name]);
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

                        if (i < s.Length && s[i] == '#' && word is "BOOL" or "INT" or "DINT" or "UINT" or "WORD" or "REAL" or "LREAL")
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
            private static double Duration(string text, int line)
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
                "BOOL", "SINT", "INT", "DINT", "LINT", "USINT", "UINT", "UDINT", "ULINT", "BYTE", "WORD", "DWORD", "LWORD", "REAL", "LREAL", "TIME"
            };

            private static readonly HashSet<string> Functions = new HashSet<string>(StringComparer.Ordinal)
            {
                "ABS", "SQRT", "SIN", "COS", "TAN", "ATAN", "EXP", "LN", "TRUNC", "MIN", "MAX", "LIMIT", "SEL"
            };

            private readonly List<Token> _t;
            private readonly Dictionary<string, Var> _vars = new Dictionary<string, Var>(StringComparer.Ordinal);
            private readonly List<string> _order = new List<string>();   // [plc-pou] declaration order (a FUNCTION's positional arguments)
            private readonly Dictionary<string, Pou> _pous;
            private readonly int _depth;
            private readonly string? _inside;   // "FUNCTION_BLOCK" or "FUNCTION" while compiling one, null for the program
            private int _i;

            public Parser(List<Token> tokens) : this(tokens, new Dictionary<string, Pou>(StringComparer.Ordinal), 0, null)
            {
            }

            private Parser(List<Token> tokens, Dictionary<string, Pou> pous, int depth, string? inside)
            {
                _t = tokens;
                _pous = pous;
                _depth = depth;
                _inside = inside;
            }

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
                    if (Elementary.Contains(name.Text) || Block.Make(name.Text) != null || Functions.Contains(name.Text))
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
                var sub = new Parser(new List<Token>(fb.Tokens), _pous, _depth + 1, "FUNCTION_BLOCK");
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
                var sub = new Parser(new List<Token>(f.Tokens), _pous, _depth + 1, "FUNCTION");
                sub.Expect("FUNCTION", "FUNCTION");
                var name = sub.Next();
                sub.Expect(":", $"':' ({name.Text} fonksiyonunun dönüş türü)");
                var typeTok = sub.Ident("dönüş türü");
                if (!Elementary.Contains(typeTok.Text)) throw new StCompileException(typeTok.Line, $"{name.Text}: dönüş türü BOOL, INT, REAL ya da TIME gibi bir tür olmalı.");
                var result = new Var { Name = name.Text, TypeName = typeTok.Text, Type = ElementaryType(typeTok.Text) };
                sub._vars[result.Name] = result;
                while (sub.Peek.Is("VAR") || sub.Peek.Is("VAR_INPUT") || sub.Peek.Is("VAR_TEMP") || sub.Peek.Is("VAR_OUTPUT") || sub.Peek.Is("VAR_IN_OUT")) sub.Declarations();
                var fn = new UserFunction { Name = name.Text, Result = result, Vars = sub._vars };
                fn.Inputs.AddRange(sub._order.Select(n => sub._vars[n]).Where(v => v.Section == 'I'));
                fn.Body = sub.Statements("END_FUNCTION");   // a call of itself in here finds it still compiling
                sub.Expect("END_FUNCTION", "END_FUNCTION");
                f.Compiling = false;
                f.Function = fn;
                return fn;
            }

            private static StType ElementaryType(string type) =>
                type == "BOOL" ? StType.Bool : type is "REAL" or "LREAL" ? StType.Real : type == "TIME" ? StType.Time : StType.Int;

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
                ExtractPous();
                string name = "PLC";
                bool wrapped = Accept("PROGRAM");
                if (wrapped)
                {
                    if (Peek.Kind != Tok.Ident) throw new StCompileException(Peek.Line, "PROGRAM'dan sonra bir ad bekleniyordu.");
                    name = Next().Text;
                }

                while (Peek.Is("VAR") || Peek.Is("VAR_GLOBAL") || Peek.Is("VAR_INPUT") || Peek.Is("VAR_OUTPUT") || Peek.Is("VAR_TEMP")) Declarations();
                var body = Statements(wrapped ? new[] { "END_PROGRAM" } : Array.Empty<string>());
                if (wrapped) Expect("END_PROGRAM", "END_PROGRAM");
                Accept(";");
                if (Peek.Kind != Tok.End) throw new StCompileException(Peek.Line, $"'{Shown(Peek)}' fazla.");
                return new StProgram(name, _vars, body);
            }

            private void Declarations()
            {
                var head = Next();
                if (head.Is("VAR_IN_OUT")) throw new StCompileException(head.Line, "VAR_IN_OUT henüz yok: değeri VAR_INPUT ile verip VAR_OUTPUT ile geri alın.");
                char section = _inside == null ? '\0' : head.Is("VAR_INPUT") ? 'I' : head.Is("VAR_OUTPUT") ? 'O' : '\0';
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
                    // [plc-pou] ARRAY[lo..hi] OF type
                    int lo = 0, length = 0;
                    bool array = Accept("ARRAY");
                    if (array)
                    {
                        Expect("[", "'[' (dizinin sınırları)");
                        lo = Bound();
                        Expect("..", "'..'");
                        int hi = Bound();
                        Expect("]", "']'");
                        Expect("OF", "OF");
                        if (hi < lo) throw new StCompileException(names[0].Line, $"Dizinin sınırları ters: [{lo}..{hi}].");
                        if ((long)hi - lo + 1 > 10000) throw new StCompileException(names[0].Line, "Bir dizi en çok 10000 eleman olabilir.");
                        length = hi - lo + 1;
                        if (at != null) throw new StCompileException(names[0].Line, "Bir diziye AT ile adres bağlanmaz.");
                    }

                    var typeTok = Ident("tür");
                    string type = typeTok.Text;
                    Expr? init = null;
                    List<Expr>? inits = null;
                    if (Accept(":="))
                    {
                        if (array)
                        {
                            Expect("[", "'[' (dizinin başlangıç değerleri)");
                            inits = new List<Expr>();
                            do inits.Add(Expression());
                            while (Accept(","));
                            Expect("]", "']'");
                        }
                        else init = Expression();
                    }

                    Expect(";", "';'");
                    foreach (var n in names)
                    {
                        if (_vars.ContainsKey(n.Text)) throw new StCompileException(n.Line, $"'{n.Text}' iki kez tanımlanmış.");
                        var v = new Var { Name = n.Text, TypeName = type, Section = section, Lo = lo };
                        Pou? fb = _pous.TryGetValue(type, out var pou) && pou.Kind == "FUNCTION_BLOCK" ? pou : null;
                        if (Elementary.Contains(type))
                        {
                            v.Type = ElementaryType(type);
                            if (array) v.Elements = new double[length];
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
                            throw new StCompileException(typeTok.Line, $"'{type}' türü yok (BOOL, INT, REAL, TIME, TON, TOF, TP, R_TRIG, F_TRIG, CTU, CTD, CTUD, SR, RS, MB_READ, MB_WRITE{own}).");
                        }

                        if (at != null) Bind(v, at, n.Line);
                        if (init != null)
                        {
                            if (v.Type == StType.Block) throw new StCompileException(n.Line, "Bir blok örneğine başlangıç değeri verilmez.");
                            v.Set(init.Eval(null!));
                            v.Initial = v.Value;
                        }

                        if (v.Elements != null)
                        {
                            if (inits != null)
                            {
                                if (inits.Count > length) throw new StCompileException(n.Line, $"{n.Text} {length} elemanlı; {inits.Count} başlangıç değeri verilmiş.");
                                for (int k = 0; k < inits.Count; k++) v.Elements[k] = Coerce(v.Type, type, inits[k].Eval(null!));
                            }

                            v.InitialElements = (double[])v.Elements.Clone();
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

                if (target.IsArray)
                {
                    // [plc-pou] A[i] := x;  T[i](IN := …);  T[i].PT := …;
                    Expect("[", $"'[' ({target.Name} bir dizi: {target.Name}[i] gibi)");
                    var at = IndexExpression();
                    Expect("]", "']'");
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
                if (target.Type == StType.Bool && e.Type != StType.Bool && e is not Const)
                    throw new StCompileException(e.Line, $"{target.Name} BOOL: sağ taraf bir koşul olmalı (örneğin x > 0).");
                if (target.Type != StType.Bool && e.Type == StType.Bool)
                    throw new StCompileException(e.Line, $"{target.Name} {target.TypeName}: BOOL atanamaz (BOOL_TO_INT kullanın).");
            }

            private Expr IndexExpression()
            {
                var e = Expression();
                if (e.Type is StType.Bool or StType.Real) throw new StCompileException(e.Line, "Dizi indisi bir tamsayı olmalı.");
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
                            if (!block.Inputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{instance.TypeName} bloğunun '{m.Text}' girişi yok ({string.Join(", ", block.Inputs)}).");
                            call.Inputs.Add((m.Text, Expression()));
                        }
                        else if (Accept("=>"))
                        {
                            if (!block.Outputs.Contains(m.Text)) throw new StCompileException(m.Line, $"{instance.TypeName} bloğunun '{m.Text}' çıkışı yok ({string.Join(", ", block.Outputs)}).");
                            var tok = Peek;
                            var target = tok.Kind == Tok.Address ? Direct(Next()) : Lookup(Ident("değişken"));
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
                if (s.Selector.Type is StType.Real or StType.Bool) throw new StCompileException(line, "CASE bir tamsayı üzerinde çalışır.");
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
                if (_t[j].Is("-")) j++;
                if (_t[j].Kind != Tok.Number) return false;
                j++;
                return _t[j].Is(":") || _t[j].Is(",") || _t[j].Is("..");
            }

            private double Label()
            {
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
                    a = new Binary { Line = op.Line, Op = op.Text, A = a, B = Relation(), Type = StType.Bool };
                }

                return a;
            }

            private Expr Relation()
            {
                var a = Sum();
                while (Peek.Is("<") || Peek.Is("<=") || Peek.Is(">") || Peek.Is(">="))
                {
                    var op = Next();
                    a = new Binary { Line = op.Line, Op = op.Text, A = a, B = Sum(), Type = StType.Bool };
                }

                return a;
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
                    return new Binary { Line = op.Line, Op = "**", A = a, B = b, Type = StType.Real };
                }

                return a;
            }

            private static StType Arith(Expr a, Expr b, int line)
            {
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
                    return new Unary { Line = line, Op = "NOT", A = a, Type = a.Type == StType.Bool ? StType.Bool : StType.Int };
                }

                if (Peek.Is("-"))
                {
                    int line = Next().Line;
                    var a = Unary();
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
                            call.Type = CallType(t.Text, call.Args);
                            return call;
                        }

                        if (Peek.Is("(") && _pous.TryGetValue(t.Text, out var pou) && pou.Kind == "FUNCTION") return FunctionCallExpression(t, pou);
                        var v = Lookup(t);
                        if (v.IsArray)
                        {
                            Expect("[", $"'[' ({v.Name} bir dizi: {v.Name}[i] gibi)");
                            var at = IndexExpression();
                            Expect("]", "']'");
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
                b.TypeOf(member) ?? (member is "Q" or "Q1" or "QU" or "QD" or "IN" or "CLK" or "CU" or "CD" or "R" or "LD" or "S" or "S1" or "R1" or "REQ" or "DONE" or "BUSY" or "ERROR" ? StType.Bool
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
                            input = fn.Inputs.FirstOrDefault(x => x.Name == arg.Text)
                                    ?? throw new StCompileException(arg.Line, $"{fn.Name} fonksiyonunun '{arg.Text}' girişi yok ({string.Join(", ", fn.Inputs.Select(x => x.Name))}).");
                        }
                        else
                        {
                            if (position >= fn.Inputs.Count) throw new StCompileException(Peek.Line, $"{fn.Name} {fn.Inputs.Count} giriş alır.");
                            input = fn.Inputs[position];
                        }

                        if (call.Args.Any(a => a.Input == input)) throw new StCompileException(name.Line, $"{fn.Name}: '{input.Name}' iki kez verilmiş.");
                        var value = Expression();
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

                return call;
            }

            private static StType CallType(string name, List<Expr> args)
            {
                if (name.Contains("_TO_"))
                {
                    string to = name.Substring(name.IndexOf("_TO_", StringComparison.Ordinal) + 4);
                    return to == "BOOL" ? StType.Bool : to is "REAL" or "LREAL" ? StType.Real : to == "TIME" ? StType.Time : StType.Int;
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
