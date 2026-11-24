using System;
using System.Collections.Generic;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Debug
{
    /// <summary>[debug] One row of a DWARF line table: the code at <see cref="Address"/> belongs to a source line.</summary>
    public readonly struct LineRow
    {
        public LineRow(uint address, string file, int line, bool isStatement, bool endSequence)
        {
            Address = address;
            File = file;
            Line = line;
            IsStatement = isStatement;
            EndSequence = endSequence;
        }

        public uint Address { get; }
        public string File { get; }
        public int Line { get; }
        public bool IsStatement { get; }
        public bool EndSequence { get; }
    }

    /// <summary>[debug] Kind of a variable's type, enough to show its value.</summary>
    public enum DebugTypeKind
    {
        Unknown,
        Base,
        Pointer,
        Array,
        Struct,
        Enum,
    }

    /// <summary>[debug] A variable's type as DWARF describes it (typedefs, const and volatile already looked through).</summary>
    public sealed class DebugType
    {
        public DebugTypeKind Kind { get; internal set; }
        public string Name { get; internal set; } = string.Empty;
        public int Size { get; internal set; }

        /// <summary>DW_ATE_*: 2 bool, 4 float, 5 signed, 6 signed char, 7 unsigned, 8 unsigned char.</summary>
        public int Encoding { get; internal set; }

        /// <summary>Element of an array, target of a pointer.</summary>
        public DebugType? Element { get; internal set; }

        public int Count { get; internal set; }
        public List<(string Name, int Offset, DebugType Type)> Members { get; } = new List<(string, int, DebugType)>();
        public List<(string Name, long Value)> Enumerators { get; } = new List<(string, long)>();

        public bool IsChar => Kind == DebugTypeKind.Base && Size == 1 && (Encoding == 6 || Encoding == 8);
    }

    /// <summary>[debug] A variable with a fixed place in RAM: a global, or a function's static.</summary>
    public sealed class DebugVariable
    {
        public string Name { get; internal set; } = string.Empty;

        /// <summary>Function a static belongs to (empty for a global).</summary>
        public string Scope { get; internal set; } = string.Empty;

        /// <summary>Address in the MCU's data space (0x100 = the first byte of SRAM on the ATmega328P).</summary>
        public int Address { get; internal set; }

        public DebugType Type { get; internal set; } = new DebugType();
        public string File { get; internal set; } = string.Empty;
        public int Line { get; internal set; }

        public string DisplayName => Scope.Length > 0 ? Scope + "::" + Name : Name;
    }

    /// <summary>[debug] A function's code range.</summary>
    public readonly struct DebugFunction
    {
        public DebugFunction(string name, uint low, uint high)
        {
            Name = name;
            Low = low;
            High = high;
        }

        public string Name { get; }
        public uint Low { get; }
        public uint High { get; }
    }

    internal sealed class DwarfCursor
    {
        private readonly byte[] _d;

        public DwarfCursor(byte[] d, int pos = 0)
        {
            _d = d;
            Pos = pos;
        }

        public int Pos;

        public byte U8() => _d[Pos++];

        public ushort U16()
        {
            ushort v = (ushort)(_d[Pos] | (_d[Pos + 1] << 8));
            Pos += 2;
            return v;
        }

        public uint U32()
        {
            uint v = (uint)(_d[Pos] | (_d[Pos + 1] << 8) | (_d[Pos + 2] << 16) | (_d[Pos + 3] << 24));
            Pos += 4;
            return v;
        }

        public ulong U64()
        {
            ulong lo = U32(), hi = U32();
            return lo | (hi << 32);
        }

        public ulong Uleb()
        {
            ulong r = 0;
            int shift = 0;
            while (true)
            {
                byte b = _d[Pos++];
                if (shift < 64) r |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return r;
                shift += 7;
            }
        }

        public long Sleb()
        {
            long r = 0;
            int shift = 0;
            byte b;
            do
            {
                b = _d[Pos++];
                if (shift < 64) r |= (long)(b & 0x7F) << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            if (shift < 64 && (b & 0x40) != 0) r |= -1L << shift;
            return r;
        }

        public ulong Sized(int size) => size switch
        {
            1 => U8(),
            2 => U16(),
            8 => U64(),
            _ => U32(),
        };

        public string CStr()
        {
            int end = Pos;
            while (_d[end] != 0) end++;
            string s = Encoding.UTF8.GetString(_d, Pos, end - Pos);
            Pos = end + 1;
            return s;
        }

        public byte[] Bytes(int n)
        {
            var b = new byte[n];
            Array.Copy(_d, Pos, b, 0, n);
            Pos += n;
            return b;
        }
    }

    /// <summary>
    /// [debug] Reads what a source-level debugger needs from DWARF 2–4 (what avr-gcc writes): the line tables
    /// (address ↔ file and line), the variables that live at a fixed RAM address with their types, and the
    /// functions' code ranges. Anything it does not understand is skipped, never guessed.
    /// </summary>
    public static class DwarfReader
    {
        /// <summary>Line tables by their offset in .debug_line (a compile unit's DW_AT_stmt_list).</summary>
        public static Dictionary<int, (List<string> Files, List<LineRow> Rows)> ReadLines(byte[] d)
        {
            var units = new Dictionary<int, (List<string>, List<LineRow>)>();
            int pos = 0;
            while (pos + 4 <= d.Length)
            {
                int start = pos;
                var c = new DwarfCursor(d, pos);
                uint length = c.U32();
                if (length == 0 || length == 0xFFFFFFFF) break;   // 64-bit DWARF is not written for the AVR
                int end = c.Pos + (int)length;
                if (end > d.Length) break;
                pos = end;
                try
                {
                    ushort version = c.U16();
                    if (version < 2 || version > 4) continue;
                    uint headerLength = c.U32();
                    int program = c.Pos + (int)headerLength;
                    byte minInst = c.U8();
                    if (version >= 4) c.U8();   // maximum_operations_per_instruction
                    bool defaultIsStmt = c.U8() != 0;
                    sbyte lineBase = (sbyte)c.U8();
                    byte lineRange = c.U8();
                    byte opcodeBase = c.U8();
                    if (lineRange == 0 || opcodeBase == 0) continue;
                    var opLengths = new byte[opcodeBase];
                    for (int i = 1; i < opcodeBase; i++) opLengths[i] = c.U8();
                    var dirs = new List<string>();
                    while (true)
                    {
                        string s = c.CStr();
                        if (s.Length == 0) break;
                        dirs.Add(s);
                    }

                    var files = new List<string> { string.Empty };   // DWARF 2–4 number files from 1
                    while (true)
                    {
                        string name = c.CStr();
                        if (name.Length == 0) break;
                        int dir = (int)c.Uleb();
                        c.Uleb();
                        c.Uleb();
                        files.Add(Join(dirs, dir, name));
                    }

                    var rows = new List<LineRow>();
                    c.Pos = program;
                    uint address = 0;
                    int file = 1, line = 1;
                    bool isStmt = defaultIsStmt;

                    void Emit(bool endSequence) => rows.Add(new LineRow(address, file >= 0 && file < files.Count ? files[file] : string.Empty, line, isStmt, endSequence));

                    while (c.Pos < end)
                    {
                        byte op = c.U8();
                        if (op >= opcodeBase)
                        {
                            int adjusted = op - opcodeBase;
                            address += (uint)(adjusted / lineRange * minInst);
                            line += lineBase + adjusted % lineRange;
                            Emit(false);
                            continue;
                        }

                        switch (op)
                        {
                            case 0:
                            {
                                int len = (int)c.Uleb();
                                int next = c.Pos + len;
                                byte sub = len > 0 ? c.U8() : (byte)0;
                                if (sub == 1)
                                {
                                    Emit(true);
                                    address = 0;
                                    file = 1;
                                    line = 1;
                                    isStmt = defaultIsStmt;
                                }
                                else if (sub == 2)
                                {
                                    address = (uint)c.Sized(len - 1);
                                }
                                else if (sub == 3)
                                {
                                    string name = c.CStr();
                                    int dir = (int)c.Uleb();
                                    files.Add(Join(dirs, dir, name));
                                }

                                c.Pos = next;
                                break;
                            }

                            case 1: Emit(false); break;
                            case 2: address += (uint)(c.Uleb() * minInst); break;
                            case 3: line += (int)c.Sleb(); break;
                            case 4: file = (int)c.Uleb(); break;
                            case 5: c.Uleb(); break;
                            case 6: isStmt = !isStmt; break;
                            case 7: break;
                            case 8: address += (uint)((255 - opcodeBase) / lineRange * minInst); break;
                            case 9: address += c.U16(); break;
                            case 10:
                            case 11: break;
                            case 12: c.Uleb(); break;
                            default:
                                for (int i = 0; i < opLengths[op]; i++) c.Uleb();
                                break;
                        }
                    }

                    units[start] = (files, rows);
                }
                catch (IndexOutOfRangeException)
                {
                    // a damaged unit: the others still count
                }
            }

            return units;
        }

        private static string Join(List<string> dirs, int dir, string name)
        {
            if (name.StartsWith("/", StringComparison.Ordinal) || (name.Length > 2 && name[1] == ':')) return name;
            return dir > 0 && dir <= dirs.Count ? dirs[dir - 1].TrimEnd('/', '\\') + "/" + name : name;
        }

        // ================================================================ .debug_info

        private sealed class Abbrev
        {
            public int Tag;
            public bool Children;
            public readonly List<(int Attr, int Form, long Const)> Specs = new List<(int, int, long)>();
        }

        private sealed class Die
        {
            public int Offset;
            public int Tag;
            public int Unit;   // offset of its compile unit
            public Die? Parent;
            public readonly List<Die> Kids = new List<Die>();
            public readonly Dictionary<int, object> At = new Dictionary<int, object>();

            /// <summary>DW_AT_high_pc was written in a constant form: a size, not an address (DWARF 4).</summary>
            public bool HighIsOffset;

            public string Str(int at) => At.TryGetValue(at, out var v) && v is string s ? s : string.Empty;

            public long Num(int at, long fallback = 0) => At.TryGetValue(at, out var v) ? v switch
            {
                ulong u => (long)u,
                long l => l,
                _ => fallback,
            } : fallback;

            public int Ref(int at) => At.TryGetValue(at, out var v) && v is DieRef r ? r.Offset : -1;

            public byte[]? Block(int at) => At.TryGetValue(at, out var v) ? v as byte[] : null;
        }

        private readonly struct DieRef
        {
            public DieRef(int offset) => Offset = offset;

            public int Offset { get; }
        }

        private const int AtRanges = 0x55, TagInlined = 0x1d;

        private const int AtName = 0x03, AtLocation = 0x02, AtByteSize = 0x0b, AtStmtList = 0x10, AtLowPc = 0x11, AtHighPc = 0x12,
            AtConstValue = 0x1c, AtUpperBound = 0x2f, AtAbstractOrigin = 0x31, AtCount = 0x37, AtMemberLocation = 0x38,
            AtDeclFile = 0x3a, AtDeclLine = 0x3b, AtDeclaration = 0x3c, AtEncoding = 0x3e, AtSpecification = 0x47, AtType = 0x49;

        private const int TagArray = 0x01, TagClass = 0x02, TagEnum = 0x04, TagMember = 0x0d, TagPointer = 0x0f, TagReference = 0x10,
            TagCompileUnit = 0x11, TagStruct = 0x13, TagSubroutineType = 0x15, TagTypedef = 0x16, TagUnion = 0x17, TagSubrange = 0x21,
            TagBase = 0x24, TagConst = 0x26, TagEnumerator = 0x28, TagSubprogram = 0x2e, TagVariable = 0x34, TagVolatile = 0x35,
            TagRestrict = 0x37, TagRvalueReference = 0x42, TagAtomic = 0x47;

        /// <summary>
        /// The variables at fixed RAM addresses (data space = ELF address − 0x800000 on the AVR) and the functions'
        /// code ranges. <paramref name="lineFiles"/> gives each unit's file table (from <see cref="ReadLines"/>).
        /// </summary>
        public static (List<DebugVariable> Variables, List<DebugFunction> Functions) ReadInfo(
            byte[] info, byte[] abbrev, byte[] str, Dictionary<int, (List<string> Files, List<LineRow> Rows)> lineFiles, byte[]? ranges = null)
        {
            var dies = new Dictionary<int, Die>();
            var unitFiles = new Dictionary<int, List<string>>();
            int pos = 0;
            while (pos + 11 <= info.Length)
            {
                int unitStart = pos;
                var c = new DwarfCursor(info, pos);
                uint length = c.U32();
                if (length == 0 || length == 0xFFFFFFFF) break;
                int end = c.Pos + (int)length;
                if (end > info.Length) break;
                pos = end;
                try
                {
                    ushort version = c.U16();
                    if (version < 2 || version > 4) continue;
                    int abbrevOffset = (int)c.U32();
                    int addressSize = c.U8();
                    var table = ReadAbbrevs(abbrev, abbrevOffset);
                    Die? parent = null;
                    while (c.Pos < end)
                    {
                        int offset = c.Pos;
                        int code = (int)c.Uleb();
                        if (code == 0)
                        {
                            parent = parent?.Parent;
                            continue;
                        }

                        if (!table.TryGetValue(code, out var a)) break;   // cannot go on without the layout
                        var die = new Die { Offset = offset, Tag = a.Tag, Unit = unitStart, Parent = parent };
                        foreach (var (attr, form, constant) in a.Specs)
                        {
                            object? value = ReadForm(c, form, constant, addressSize, version, unitStart, str);
                            if (value != null) die.At[attr] = value;
                            if (attr == AtHighPc && form != 0x01) die.HighIsOffset = true;
                        }

                        dies[offset] = die;
                        parent?.Kids.Add(die);
                        if (die.Tag == TagCompileUnit && die.At.TryGetValue(AtStmtList, out var sl))
                        {
                            int stmt = sl is ulong u ? (int)u : sl is long l ? (int)l : -1;
                            if (lineFiles.TryGetValue(stmt, out var lf)) unitFiles[unitStart] = lf.Files;
                        }

                        if (a.Children) parent = die;
                    }
                }
                catch (IndexOutOfRangeException)
                {
                }
                catch (ArgumentException)
                {
                }
            }

            var types = new Dictionary<int, DebugType>();
            var variables = new List<DebugVariable>();
            var functions = new List<DebugFunction>();
            var seen = new HashSet<int>();
            var unitBase = new Dictionary<int, ulong>();
            foreach (var die in dies.Values)
            {
                if (die.Tag == TagCompileUnit && die.At.TryGetValue(AtLowPc, out var cuLow) && cuLow is ulong cb) unitBase[die.Unit] = cb;
            }

            foreach (var die in dies.Values)
            {
                if (die.Tag == TagSubprogram || die.Tag == TagInlined)
                {
                    // A function, or a function's code inlined into another (loop() inside main() after LTO):
                    // its range, or its list of ranges when the code is in pieces.
                    string name = NameOf(die, dies);
                    if (name.Length == 0) continue;
                    if (die.At.TryGetValue(AtLowPc, out var lowObj) && die.At.TryGetValue(AtHighPc, out var highObj) && lowObj is ulong low)
                    {
                        ulong high = highObj switch
                        {
                            ulong u => u,
                            long l => (ulong)l,
                            _ => 0,
                        };
                        if (die.HighIsOffset) high += low;   // DWARF 4: a size from low_pc
                        if (high > low) functions.Add(new DebugFunction(name, (uint)low, (uint)high));
                    }
                    else if (ranges != null && die.At.TryGetValue(AtRanges, out var ro) && ro is ulong rangeOffset)
                    {
                        ulong b = unitBase.TryGetValue(die.Unit, out var ub) ? ub : 0;
                        foreach (var (rl, rh) in Ranges(ranges, (int)rangeOffset, b)) functions.Add(new DebugFunction(name, (uint)rl, (uint)rh));
                    }

                    continue;
                }

                if (die.Tag != TagVariable) continue;
                var loc = die.Block(AtLocation);
                if (loc == null || loc.Length < 3 || loc[0] != 0x03) continue;   // DW_OP_addr: a fixed place
                int size = loc.Length >= 5 ? 4 : 2;
                ulong address = 0;
                for (int i = size; i >= 1; i--) address = (address << 8) | loc[i];
                if (loc.Length > 1 + size)
                {
                    if (loc[1 + size] != 0x23) continue;   // anything but "+ offset" makes a value, not a place
                    address += new DwarfCursor(loc, 2 + size).Uleb();
                }

                if (address < 0x800000 || address >= 0x810000) continue;   // the AVR's data space
                int data = (int)(address - 0x800000);
                if (!seen.Add(data)) continue;

                var decl = Origin(die, dies);
                string varName = decl.Str(AtName);
                if (varName.Length == 0) continue;
                int typeRef = die.Ref(AtType) >= 0 ? die.Ref(AtType) : decl.Ref(AtType);
                int fileIndex = (int)decl.Num(AtDeclFile, 0);
                string file = unitFiles.TryGetValue(decl.Unit, out var files) && fileIndex > 0 && fileIndex < files.Count ? files[fileIndex] : string.Empty;
                var scope = die.Parent;
                while (scope != null && scope.Tag != TagSubprogram) scope = scope.Parent;
                variables.Add(new DebugVariable
                {
                    Name = varName,
                    Scope = scope != null ? NameOf(scope, dies) : string.Empty,
                    Address = data,
                    Type = TypeOf(typeRef, dies, types, 0),
                    File = file,
                    Line = (int)decl.Num(AtDeclLine, 0),
                });
            }

            functions.Sort((x, y) => x.Low.CompareTo(y.Low));
            return (variables, functions);
        }

        /// <summary>A .debug_ranges list: (begin, end) pairs from the unit's base address, (0, 0) at its end.</summary>
        private static IEnumerable<(ulong Low, ulong High)> Ranges(byte[] d, int offset, ulong unitBase)
        {
            var list = new List<(ulong, ulong)>();
            ulong b = unitBase;
            var c = new DwarfCursor(d, offset);
            try
            {
                for (int guard = 0; guard < 4096 && c.Pos + 8 <= d.Length; guard++)
                {
                    ulong begin = c.U32(), end = c.U32();
                    if (begin == 0 && end == 0) break;
                    if (begin == 0xFFFFFFFF)
                    {
                        b = end;   // base address selection
                        continue;
                    }

                    if (end > begin) list.Add((b + begin, b + end));
                }
            }
            catch (IndexOutOfRangeException)
            {
            }

            return list;
        }

        /// <summary>The declaration a definition refers to (DW_AT_specification / DW_AT_abstract_origin), else itself.</summary>
        private static Die Origin(Die die, Dictionary<int, Die> dies)
        {
            var d = die;
            for (int hop = 0; hop < 4; hop++)
            {
                if (d.Str(AtName).Length > 0) return d;
                int r = d.Ref(AtSpecification) >= 0 ? d.Ref(AtSpecification) : d.Ref(AtAbstractOrigin);
                if (r < 0 || !dies.TryGetValue(r, out var next)) return d;
                d = next;
            }

            return d;
        }

        private static string NameOf(Die die, Dictionary<int, Die> dies) => Origin(die, dies).Str(AtName);

        private static DebugType TypeOf(int offset, Dictionary<int, Die> dies, Dictionary<int, DebugType> memo, int depth)
        {
            if (offset < 0 || !dies.TryGetValue(offset, out var die)) return new DebugType { Kind = DebugTypeKind.Unknown, Name = "void" };
            if (memo.TryGetValue(offset, out var known)) return known;
            if (depth > 12) return new DebugType { Kind = DebugTypeKind.Unknown, Name = "…" };
            switch (die.Tag)
            {
                case TagTypedef:
                {
                    var inner = TypeOf(die.Ref(AtType), dies, memo, depth + 1);
                    var t = Copy(inner);
                    t.Name = die.Str(AtName).Length > 0 ? die.Str(AtName) : inner.Name;
                    memo[offset] = t;
                    return t;
                }

                case TagConst:
                case TagVolatile:
                case TagRestrict:
                case TagAtomic:
                {
                    var t = TypeOf(die.Ref(AtType), dies, memo, depth + 1);
                    memo[offset] = t;
                    return t;
                }
            }

            var type = new DebugType { Name = die.Str(AtName), Size = (int)die.Num(AtByteSize, 0) };
            memo[offset] = type;   // before the children: a struct may point to itself
            switch (die.Tag)
            {
                case TagBase:
                    type.Kind = DebugTypeKind.Base;
                    type.Encoding = (int)die.Num(AtEncoding, 0);
                    break;
                case TagPointer:
                case TagReference:
                case TagRvalueReference:
                    type.Kind = DebugTypeKind.Pointer;
                    if (type.Size == 0) type.Size = 2;
                    type.Element = TypeOf(die.Ref(AtType), dies, memo, depth + 1);
                    type.Name = (type.Element.Name.Length > 0 ? type.Element.Name : "void") + "*";
                    break;
                case TagArray:
                {
                    type.Kind = DebugTypeKind.Array;
                    var element = TypeOf(die.Ref(AtType), dies, memo, depth + 1);
                    var dims = new List<int>();
                    foreach (var k in die.Kids)
                    {
                        if (k.Tag != TagSubrange) continue;
                        if (k.At.ContainsKey(AtCount)) dims.Add((int)k.Num(AtCount, 0));
                        else if (k.At.ContainsKey(AtUpperBound)) dims.Add((int)k.Num(AtUpperBound, -1) + 1);
                        else dims.Add(0);
                    }

                    if (dims.Count == 0) dims.Add(0);
                    // int m[2][3] is an array of 2 arrays of 3
                    for (int i = dims.Count - 1; i >= 1; i--)
                    {
                        element = new DebugType { Kind = DebugTypeKind.Array, Element = element, Count = dims[i], Size = Math.Max(0, dims[i]) * element.Size, Name = element.Name + "[" + dims[i] + "]" };
                    }

                    type.Element = element;
                    type.Count = dims[0];
                    if (type.Size == 0) type.Size = Math.Max(0, type.Count) * element.Size;
                    type.Name = element.Name + "[" + type.Count + "]";
                    break;
                }

                case TagStruct:
                case TagClass:
                case TagUnion:
                    type.Kind = DebugTypeKind.Struct;
                    foreach (var m in die.Kids)
                    {
                        if (m.Tag != TagMember || m.Str(AtName).Length == 0) continue;
                        int at = 0;
                        if (m.At.TryGetValue(AtMemberLocation, out var ml))
                        {
                            if (ml is byte[] expr && expr.Length >= 2 && expr[0] == 0x23) at = (int)new DwarfCursor(expr, 1).Uleb();   // DW_OP_plus_uconst
                            else if (ml is ulong u) at = (int)u;
                            else if (ml is long l) at = (int)l;
                        }

                        type.Members.Add((m.Str(AtName), at, TypeOf(m.Ref(AtType), dies, memo, depth + 1)));
                    }

                    break;
                case TagEnum:
                    type.Kind = DebugTypeKind.Enum;
                    foreach (var e in die.Kids)
                    {
                        if (e.Tag == TagEnumerator) type.Enumerators.Add((e.Str(AtName), e.Num(AtConstValue, 0)));
                    }

                    if (type.Size == 0) type.Size = 2;
                    break;
                case TagSubroutineType:
                    type.Kind = DebugTypeKind.Unknown;
                    type.Name = "fonksiyon";
                    break;
                default:
                    type.Kind = DebugTypeKind.Unknown;
                    break;
            }

            return type;
        }

        private static DebugType Copy(DebugType t)
        {
            var c = new DebugType { Kind = t.Kind, Name = t.Name, Size = t.Size, Encoding = t.Encoding, Element = t.Element, Count = t.Count };
            c.Members.AddRange(t.Members);
            c.Enumerators.AddRange(t.Enumerators);
            return c;
        }

        private static Dictionary<int, Abbrev> ReadAbbrevs(byte[] d, int offset)
        {
            var table = new Dictionary<int, Abbrev>();
            var c = new DwarfCursor(d, offset);
            while (c.Pos < d.Length)
            {
                int code = (int)c.Uleb();
                if (code == 0) break;
                var a = new Abbrev { Tag = (int)c.Uleb(), Children = c.U8() != 0 };
                while (true)
                {
                    int attr = (int)c.Uleb(), form = (int)c.Uleb();
                    if (attr == 0 && form == 0) break;
                    long constant = form == 0x21 ? c.Sleb() : 0;   // DW_FORM_implicit_const
                    a.Specs.Add((attr, form, constant));
                }

                table[code] = a;
            }

            return table;
        }

        /// <summary>Reads one attribute value: numbers as ulong / long, strings, blocks as byte[], references as <see cref="DieRef"/>.</summary>
        private static object? ReadForm(DwarfCursor c, int form, long constant, int addressSize, int version, int unit, byte[] str)
        {
            switch (form)
            {
                case 0x01: return c.Sized(addressSize);                         // addr
                case 0x03: return c.Bytes(c.U16());                              // block2
                case 0x04: return c.Bytes((int)c.U32());                         // block4
                case 0x05: return (ulong)c.U16();                                // data2
                case 0x06: return (ulong)c.U32();                                // data4
                case 0x07: return c.U64();                                       // data8
                case 0x08: return c.CStr();                                      // string
                case 0x09: return c.Bytes((int)c.Uleb());                        // block
                case 0x0a: return c.Bytes(c.U8());                               // block1
                case 0x0b: return (ulong)c.U8();                                 // data1
                case 0x0c: return (ulong)c.U8();                                 // flag
                case 0x0d: return c.Sleb();                                      // sdata
                case 0x0e: return StrAt(str, (int)c.U32());                      // strp
                case 0x0f: return c.Uleb();                                      // udata
                case 0x10: return new DieRef((int)(version <= 2 ? c.Sized(addressSize) : c.U32()));   // ref_addr: from the section start
                case 0x11: return new DieRef(unit + c.U8());                     // ref1 … ref_udata: from the unit start
                case 0x12: return new DieRef(unit + c.U16());
                case 0x13: return new DieRef(unit + (int)c.U32());
                case 0x14: return new DieRef(unit + (int)c.U64());
                case 0x15: return new DieRef(unit + (int)c.Uleb());
                case 0x16: return ReadForm(c, (int)c.Uleb(), constant, addressSize, version, unit, str);   // indirect
                case 0x17: return (ulong)c.U32();                                // sec_offset
                case 0x18: return c.Bytes((int)c.Uleb());                        // exprloc
                case 0x19: return (ulong)1;                                      // flag_present
                case 0x20: c.U64(); return null;                                 // ref_sig8
                case 0x21: return constant;                                      // implicit_const
                case 0x1f20: c.U32(); return null;                               // GNU_ref_alt
                case 0x1f21: c.U32(); return null;                               // GNU_strp_alt
                default: throw new ArgumentException("DWARF biçimi okunamıyor: 0x" + form.ToString("X"));
            }
        }

        private static string StrAt(byte[] str, int offset)
        {
            if (offset < 0 || offset >= str.Length) return string.Empty;
            int end = offset;
            while (end < str.Length && str[end] != 0) end++;
            return Encoding.UTF8.GetString(str, offset, end - offset);
        }
    }
}
