using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Debug
{
    /// <summary>[debug] Where a program address comes from in the source.</summary>
    public readonly struct SourcePlace
    {
        public SourcePlace(string file, int line, bool inSketch)
        {
            File = file;
            Line = line;
            InSketch = inSketch;
        }

        /// <summary>Path inside the sketch ("sketch.ino", "src/motor.cpp"), or the full path of a library / core file.</summary>
        public string File { get; }

        public int Line { get; }

        /// <summary>One of the user's own files (not the Arduino core or a library).</summary>
        public bool InSketch { get; }

        public override string ToString() => File + ":" + Line.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// [debug] A built program's debug information, read from the ELF avr-gcc wrote beside the HEX: which source
    /// line each address belongs to, where a breakpoint on a line goes, the functions, and the variables with a
    /// fixed place in RAM together with how to show their values.
    /// </summary>
    public sealed partial class FirmwareDebugInfo
    {
        private readonly List<(uint Start, uint End, SourcePlace Place, bool Statement)> _ranges = new List<(uint, uint, SourcePlace, bool)>();
        private readonly Dictionary<string, SortedDictionary<int, List<uint>>> _lineStarts = new Dictionary<string, SortedDictionary<int, List<uint>>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<DebugFunction> _functions = new List<DebugFunction>();

        private FirmwareDebugInfo()
        {
        }

        /// <summary>Lines of the user's files by word address: the first instruction of a statement there (0 = none).</summary>
        public int[] StatementLineByWord { get; private set; } = Array.Empty<int>();

        /// <summary>The user's files by word address (index into <see cref="SketchFiles"/>; −1 = not a statement start).</summary>
        public short[] StatementFileByWord { get; private set; } = Array.Empty<short>();

        /// <summary>The user's own source files that have code ("sketch.ino", "src/motor.cpp" …).</summary>
        public IReadOnlyList<string> SketchFiles { get; private set; } = Array.Empty<string>();

        /// <summary>Variables at fixed RAM addresses declared in the user's files (globals first, then functions' statics).</summary>
        public IReadOnlyList<DebugVariable> Variables { get; private set; } = Array.Empty<DebugVariable>();

        /// <summary>Every variable at a fixed address, the Arduino core's included.</summary>
        public IReadOnlyList<DebugVariable> AllVariables { get; private set; } = Array.Empty<DebugVariable>();

        public IReadOnlyList<DebugFunction> Functions => _functions;

        /// <summary>
        /// The sketch-relative path of a source file ("…/sketch/src/a.cpp" → "src/a.cpp"), or null for a file that is
        /// not one of the user's (the build writes the user's files under a folder named "sketch").
        /// </summary>
        public static string? SketchPath(string path)
        {
            string p = path.Replace('\\', '/');
            int i = p.LastIndexOf("/sketch/", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? p.Substring(i + 8) : null;
        }

        public static FirmwareDebugInfo FromElf(byte[] elfBytes)
        {
            var elf = ElfImage.Read(elfBytes);
            var info = new FirmwareDebugInfo();
            var lines = DwarfReader.ReadLines(elf.Section(".debug_line").ToArray());
            var sketchFiles = new List<string>();
            var fileIndex = new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase);
            uint top = 0;
            foreach (var (_, rows) in lines.Values)
            {
                for (int i = 0; i + 1 < rows.Count; i++)
                {
                    var r = rows[i];
                    if (r.EndSequence) continue;
                    var next = rows[i + 1];
                    if (next.Address <= r.Address) continue;
                    string? sketch = SketchPath(r.File);
                    var place = new SourcePlace(sketch ?? r.File, r.Line, sketch != null);
                    info._ranges.Add((r.Address, next.Address, place, r.IsStatement));
                    top = Math.Max(top, next.Address);
                    if (sketch == null || !r.IsStatement || r.Line <= 0) continue;
                    if (!fileIndex.ContainsKey(sketch))
                    {
                        fileIndex[sketch] = (short)sketchFiles.Count;
                        sketchFiles.Add(sketch);
                    }

                    if (!info._lineStarts.TryGetValue(sketch, out var byLine)) info._lineStarts[sketch] = byLine = new SortedDictionary<int, List<uint>>();
                    if (!byLine.TryGetValue(r.Line, out var starts)) byLine[r.Line] = starts = new List<uint>();
                    // A breakpoint goes where a line's code starts, once per run of rows of that line (a line inlined
                    // in two places, or a loop's body and its test, gets one stop each).
                    bool continues = i > 0 && !rows[i - 1].EndSequence && rows[i - 1].Line == r.Line && string.Equals(rows[i - 1].File, r.File, StringComparison.Ordinal) && rows[i - 1].Address < r.Address;
                    if (!continues && !starts.Contains(r.Address)) starts.Add(r.Address);
                }
            }

            info._ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
            info.SketchFiles = sketchFiles;
            int words = (int)Math.Min(Atmega328P.FlashWords, (top + 1) / 2 + 1);
            var lineByWord = new int[Math.Max(words, 1)];
            var fileByWord = new short[lineByWord.Length];
            for (int w = 0; w < fileByWord.Length; w++) fileByWord[w] = -1;
            foreach (var (file, byLine) in info._lineStarts)
            {
                short f = fileIndex[file];
                foreach (var (line, starts) in byLine)
                {
                    foreach (uint a in starts)
                    {
                        int w = (int)(a / 2);
                        if (w >= lineByWord.Length) continue;
                        lineByWord[w] = line;
                        fileByWord[w] = f;
                    }
                }
            }

            info.StatementLineByWord = lineByWord;
            info.StatementFileByWord = fileByWord;

            var (variables, functions) = DwarfReader.ReadInfo(elf.Section(".debug_info").ToArray(), elf.Section(".debug_abbrev").ToArray(), elf.Section(".debug_str").ToArray(), lines,
                elf.Section(".debug_ranges").ToArray(), info._scopes);
            info._loc = elf.Section(".debug_loc").ToArray();   // [stack] locals by address
            info.ReadFrames(elf.Section(".debug_frame").ToArray());   // [stack] how to unwind
            info._functions.AddRange(functions);
            if (info._functions.Count == 0)
            {
                foreach (var s in elf.Symbols.Where(s => s.IsFunction && s.Size > 0).OrderBy(s => s.Value)) info._functions.Add(new DebugFunction(s.Name, s.Value, s.Value + s.Size));
            }

            var mine = new List<DebugVariable>();
            foreach (var v in variables)
            {
                string? sketch = SketchPath(v.File);
                if (sketch == null) continue;
                v.File = sketch;
                mine.Add(v);
            }

            info.AllVariables = variables.OrderBy(v => v.Address).ToList();
            info.Variables = mine.OrderBy(v => v.Scope.Length > 0 ? 1 : 0).ThenBy(v => v.File, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Line).ToList();
            return info;
        }

        /// <summary>The source line of a program (byte) address, if the program has one for it.</summary>
        public SourcePlace? Locate(int byteAddress)
        {
            uint a = (uint)byteAddress;
            int lo = 0, hi = _ranges.Count - 1, found = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (_ranges[mid].Start <= a)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            if (found < 0) return null;
            // ranges may overlap where units meet; the latest start that still covers the address wins
            for (int i = found; i >= 0 && i > found - 8; i--)
            {
                if (_ranges[i].Start <= a && a < _ranges[i].End) return _ranges[i].Place;
            }

            return null;
        }

        /// <summary>The function the address is in (DWARF names; the symbol table when there is no DWARF).</summary>
        public string? FunctionAt(int byteAddress)
        {
            uint a = (uint)byteAddress;
            DebugFunction? best = null;
            foreach (var f in _functions)
            {
                if (f.Low > a) break;
                if (a < f.High && (best == null || f.High - f.Low < best.Value.High - best.Value.Low)) best = f;
            }

            return best?.Name;
        }

        /// <summary>The lines of a user file that have code (where a breakpoint can stop).</summary>
        public IReadOnlyCollection<int> CodeLines(string file) =>
            _lineStarts.TryGetValue(file, out var byLine) ? byLine.Keys : (IReadOnlyCollection<int>)Array.Empty<int>();

        /// <summary>
        /// Where a breakpoint on <paramref name="line"/> stops: that line's code, or, for a line without code (a comment,
        /// a declaration, a brace), the next line that has some within <paramref name="reach"/> lines. Line −1: nowhere.
        /// </summary>
        public (int Line, IReadOnlyList<int> Addresses) Breakpoint(string file, int line, int reach = 40)
        {
            if (!_lineStarts.TryGetValue(file, out var byLine)) return (-1, Array.Empty<int>());
            foreach (var (l, starts) in byLine)
            {
                if (l < line) continue;
                if (l > line + reach) break;
                return (l, starts.Select(a => (int)a).ToList());
            }

            return (-1, Array.Empty<int>());
        }

        // ================================================================ values

        /// <summary>A variable's value as text, read from data space through <paramref name="read"/> (no side effects).</summary>
        public static string Format(DebugVariable v, Func<int, byte> read) => Format(v.Type, v.Address, read, 0);

        private static string Format(DebugType t, int address, Func<int, byte> read, int depth)
        {
            switch (t.Kind)
            {
                case DebugTypeKind.Base:
                    return FormatBase(t, address, read);
                case DebugTypeKind.Pointer:
                    return "0x" + ((int)Unsigned(address, Math.Max(1, Math.Min(4, t.Size)), read)).ToString("X4", CultureInfo.InvariantCulture);
                case DebugTypeKind.Enum:
                {
                    long v = Signed(address, Math.Max(1, Math.Min(4, t.Size)), read);
                    foreach (var (name, value) in t.Enumerators)
                    {
                        if (value == v) return name;
                    }

                    return v.ToString(CultureInfo.InvariantCulture);
                }

                case DebugTypeKind.Array when t.Element != null:
                {
                    if (t.Element.IsChar)
                    {
                        var sb = new StringBuilder("\"");
                        for (int i = 0; i < Math.Min(t.Count, 40); i++)
                        {
                            byte b = read(address + i);
                            if (b == 0) break;
                            sb.Append(b >= 32 && b < 127 ? (char)b : '·');
                        }

                        return sb.Append('"').ToString();
                    }

                    if (depth > 2) return "{…}";
                    var parts = new List<string>();
                    int shown = Math.Min(t.Count, 8);
                    for (int i = 0; i < shown; i++) parts.Add(Format(t.Element, address + i * t.Element.Size, read, depth + 1));
                    return "{" + string.Join(", ", parts) + (t.Count > shown ? ", …" : string.Empty) + "}";
                }

                case DebugTypeKind.Struct:
                {
                    if (depth > 2 || t.Members.Count == 0) return "{…}";
                    var parts = t.Members.Take(6).Select(m => m.Name + " = " + Format(m.Type, address + m.Offset, read, depth + 1));
                    return "{" + string.Join(", ", parts) + (t.Members.Count > 6 ? ", …" : string.Empty) + "}";
                }

                default:
                {
                    int n = Math.Max(1, Math.Min(8, t.Size));
                    var bytes = Enumerable.Range(0, n).Select(i => read(address + i).ToString("X2", CultureInfo.InvariantCulture));
                    return string.Join(" ", bytes);
                }
            }
        }

        private static string FormatBase(DebugType t, int address, Func<int, byte> read)
        {
            int size = Math.Max(1, Math.Min(8, t.Size));
            switch (t.Encoding)
            {
                case 2:   // bool
                    return read(address) != 0 ? "true" : "false";
                case 4:   // float (double is 4 bytes on the AVR too)
                    if (size == 4) return BitConverter.Int32BitsToSingle((int)Unsigned(address, 4, read)).ToString("G7", CultureInfo.InvariantCulture);
                    if (size == 8) return BitConverter.Int64BitsToDouble((long)Unsigned(address, 8, read)).ToString("G15", CultureInfo.InvariantCulture);
                    break;
                case 6:   // signed char
                case 8:   // unsigned char
                {
                    long v = t.Encoding == 6 ? Signed(address, 1, read) : (long)Unsigned(address, 1, read);
                    byte b = read(address);
                    return b >= 32 && b < 127 ? v.ToString(CultureInfo.InvariantCulture) + " '" + (char)b + "'" : v.ToString(CultureInfo.InvariantCulture);
                }

                case 5:   // signed
                    return Signed(address, size, read).ToString(CultureInfo.InvariantCulture);
            }

            return Unsigned(address, size, read).ToString(CultureInfo.InvariantCulture);
        }

        private static ulong Unsigned(int address, int size, Func<int, byte> read)
        {
            ulong v = 0;
            for (int i = size - 1; i >= 0; i--) v = (v << 8) | read(address + i);
            return v;
        }

        private static long Signed(int address, int size, Func<int, byte> read)
        {
            ulong v = Unsigned(address, size, read);
            int bits = size * 8;
            if (bits < 64 && (v & (1UL << (bits - 1))) != 0) v |= ulong.MaxValue << bits;
            return (long)v;
        }
    }
}
