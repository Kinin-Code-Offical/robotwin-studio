using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Firmware.Debug
{
    /// <summary>[stack] A parameter or local variable as one frame sees it.</summary>
    public sealed class DebugLocalValue
    {
        public string Name { get; internal set; } = string.Empty;
        public string TypeName { get; internal set; } = string.Empty;

        /// <summary>Its value as text; "—" when the compiler kept no place for it here (optimized out).</summary>
        public string Value { get; internal set; } = "—";

        public bool IsParameter { get; internal set; }

        /// <summary>False when its value is not known at this point of the program.</summary>
        public bool Available { get; internal set; }

        /// <summary>Where it was found: "r24", "r24:r25", "0x08F2", "değer" (made from others).</summary>
        public string Where { get; internal set; } = string.Empty;
    }

    /// <summary>
    /// [stack] One frame of the call stack: a function (or a function's code inlined into its caller) with the source line
    /// it is at and its parameters and locals there.
    /// </summary>
    public sealed class DebugFrame
    {
        public string Function { get; internal set; } = string.Empty;
        public SourcePlace? Place { get; internal set; }

        /// <summary>The (byte) address it is at: where the program stopped, or in a caller the address it returns to.</summary>
        public int Pc { get; internal set; }

        /// <summary>Its code was inlined into the frame below it in the list (no call of its own on the chip).</summary>
        public bool Inlined { get; internal set; }

        /// <summary>
        /// [stack] An interrupt handler (avr-libc's __vector_N): its name as the datasheet gives it ("TIMER0_OVF"); the
        /// frame below it is the code it interrupted, at the very instruction it resumes. Empty for any other function.
        /// </summary>
        public string Interrupt { get; internal set; } = string.Empty;

        public List<DebugLocalValue> Locals { get; } = new List<DebugLocalValue>();
    }

    /// <summary>
    /// [stack] The call stack and the locals of a stopped AVR program, from its DWARF: the frames unwound with the
    /// compiler's call frame information (.debug_frame: where each function keeps its return address and the registers it
    /// saved, so a caller's registers are known again), each frame split into the functions inlined into it (with
    /// link-time optimisation loop() and the sketch's helpers live inside main()), and each function's parameters and
    /// locals read from where the compiler put them at that address (a register, a pair of them, the stack, or a value it
    /// computes from others; .debug_loc lists them by address).
    /// </summary>
    public sealed partial class FirmwareDebugInfo
    {
        private readonly List<DebugScope> _scopes = new List<DebugScope>();
        private byte[] _loc = Array.Empty<byte>();
        private readonly List<Fde> _fdes = new List<Fde>();

        private sealed class Cie
        {
            public int CodeAlign = 1, DataAlign = -1, RaColumn = 36;
            public byte[] Program = Array.Empty<byte>();
        }

        private sealed class Fde
        {
            public uint Low, High;
            public Cie Cie = new Cie();
            public byte[] Program = Array.Empty<byte>();
        }

        private sealed class Rules
        {
            public int CfaReg = 32, CfaOff;
            public Dictionary<int, int> Saved = new Dictionary<int, int>();
        }

        /// <summary>Reads .debug_frame (DWARF 2/3 CIEs and FDEs, 4-byte addresses as avr-gcc writes them).</summary>
        private void ReadFrames(byte[] d)
        {
            var cies = new Dictionary<int, Cie>();
            int pos = 0;
            try
            {
                while (pos + 8 <= d.Length)
                {
                    int start = pos;
                    var c = new DwarfCursor(d, pos);
                    uint length = c.U32();
                    if (length == 0 || length == 0xFFFFFFFF) break;
                    int end = c.Pos + (int)length;
                    if (end > d.Length) break;
                    uint id = c.U32();
                    if (id == 0xFFFFFFFF)
                    {
                        var cie = new Cie();
                        int version = c.U8();
                        while (c.Pos < end && d[c.Pos] != 0) c.Pos++;   // augmentation (avr-gcc writes none)
                        c.Pos++;
                        cie.CodeAlign = (int)c.Uleb();
                        cie.DataAlign = (int)c.Sleb();
                        cie.RaColumn = version == 1 ? c.U8() : (int)c.Uleb();
                        cie.Program = d.Skip(c.Pos).Take(end - c.Pos).ToArray();
                        cies[start] = cie;
                    }
                    else if (cies.TryGetValue((int)id, out var cie))
                    {
                        uint low = c.U32(), range = c.U32();
                        _fdes.Add(new Fde { Low = low, High = low + range, Cie = cie, Program = d.Skip(c.Pos).Take(end - c.Pos).ToArray() });
                    }

                    pos = end;
                }
            }
            catch (IndexOutOfRangeException)
            {
            }
        }

        /// <summary>The frame rules in force at <paramref name="at"/>: the CIE's, then the FDE's up to that address.</summary>
        private static Rules? RulesAt(Fde f, uint at)
        {
            var r = new Rules();
            Dictionary<int, int>? initial = null;
            var remembered = new Stack<(int, int, Dictionary<int, int>)>();
            uint loc = f.Low;
            bool Run(byte[] program, bool fde)
            {
                var c = new DwarfCursor(program);
                while (c.Pos < program.Length)
                {
                    byte op = c.U8();
                    int high = op & 0xC0, low = op & 0x3F;
                    if (high == 0x40)
                    {
                        loc += (uint)(low * f.Cie.CodeAlign);
                        if (fde && loc > at) return true;
                        continue;
                    }

                    if (high == 0x80)
                    {
                        r.Saved[low] = (int)c.Uleb() * f.Cie.DataAlign;
                        continue;
                    }

                    if (high == 0xC0)
                    {
                        if (initial != null && initial.TryGetValue(low, out int o)) r.Saved[low] = o;
                        else r.Saved.Remove(low);
                        continue;
                    }

                    switch (op)
                    {
                        case 0x00: break;
                        case 0x01: loc = c.U32(); if (fde && loc > at) return true; break;
                        case 0x02: loc += (uint)(c.U8() * f.Cie.CodeAlign); if (fde && loc > at) return true; break;
                        case 0x03: loc += (uint)(c.U16() * f.Cie.CodeAlign); if (fde && loc > at) return true; break;
                        case 0x04: loc += (uint)(c.U32() * f.Cie.CodeAlign); if (fde && loc > at) return true; break;
                        case 0x05: { int reg = (int)c.Uleb(); r.Saved[reg] = (int)c.Uleb() * f.Cie.DataAlign; break; }
                        case 0x06: { int reg = (int)c.Uleb(); if (initial != null && initial.TryGetValue(reg, out int o)) r.Saved[reg] = o; else r.Saved.Remove(reg); break; }
                        case 0x07: r.Saved.Remove((int)c.Uleb()); break;   // undefined
                        case 0x08: r.Saved.Remove((int)c.Uleb()); break;   // same value
                        case 0x09: c.Uleb(); c.Uleb(); break;             // in another register: not used by avr-gcc
                        case 0x0a: remembered.Push((r.CfaReg, r.CfaOff, new Dictionary<int, int>(r.Saved))); break;
                        case 0x0b:
                            if (remembered.Count > 0)
                            {
                                var (cr, co, sv) = remembered.Pop();
                                r.CfaReg = cr;
                                r.CfaOff = co;
                                r.Saved = sv;
                            }

                            break;
                        case 0x0c: r.CfaReg = (int)c.Uleb(); r.CfaOff = (int)c.Uleb(); break;
                        case 0x0d: r.CfaReg = (int)c.Uleb(); break;
                        case 0x0e: r.CfaOff = (int)c.Uleb(); break;
                        case 0x11: { int reg = (int)c.Uleb(); r.Saved[reg] = (int)c.Sleb() * f.Cie.DataAlign; break; }
                        case 0x12: r.CfaReg = (int)c.Uleb(); r.CfaOff = (int)c.Sleb() * f.Cie.DataAlign; break;
                        case 0x13: r.CfaOff = (int)c.Sleb() * f.Cie.DataAlign; break;
                        default: return false;   // an expression rule: not followed
                    }
                }

                return true;
            }

            try
            {
                if (!Run(f.Cie.Program, false)) return null;
                initial = new Dictionary<int, int>(r.Saved);
                return Run(f.Program, true) ? r : null;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary>A register's value as an address register: SP (32), or the 16-bit pair from rN (Y = r28:r29).</summary>
        private static int? Pair(int?[] regs, int reg)
        {
            if (reg == 32) return regs[32];
            if (reg < 0 || reg >= 31) return null;
            return regs[reg] is int lo && regs[reg + 1] is int hi ? lo | hi << 8 : (int?)null;
        }

        private static readonly int[] CallSaved = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 28, 29 };

        /// <summary>
        /// [stack] The call stack of a stopped program, innermost first: <paramref name="pc"/> (byte address) and
        /// <paramref name="sp"/> as the CPU has them, <paramref name="read"/> its data space (r0–r31 at 0–31, then RAM).
        /// </summary>
        public IReadOnlyList<DebugFrame> Stack(int pc, int sp, Func<int, byte> read, int maxFrames = 24)
        {
            var frames = new List<DebugFrame>();
            var regs = new int?[33];
            for (int i = 0; i < 32; i++) regs[i] = read(i);
            regs[32] = sp;
            uint at = (uint)pc;
            bool interrupted = false;
            for (int depth = 0; depth < 16 && frames.Count < maxFrames; depth++)
            {
                // in a caller, the address it returns to is past the call: its code (and locals) are those of the call;
                // code an interrupt broke into resumes at that very address
                uint here = depth == 0 || interrupted ? at : at - 1;
                var fde = _fdes.FirstOrDefault(f => here >= f.Low && here < f.High);
                var rules = fde != null ? RulesAt(fde, here) : null;
                int? cfa = rules != null && Pair(regs, rules.CfaReg) is int cr ? cr + rules.CfaOff : (int?)null;
                int before = frames.Count;
                AddFrames(frames, here, (int)at, regs, cfa, read);
                var handler = frames.Count > before ? frames[frames.Count - 1] : null;
                int vector = -1;
                interrupted = handler != null && handler.Function.StartsWith("__vector_", StringComparison.Ordinal)
                              && int.TryParse(handler.Function.Substring(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out vector);
                if (interrupted) handler!.Interrupt = AvrRegisters.VectorName(vector);
                if (fde == null || rules == null || cfa == null || !rules.Saved.TryGetValue(fde.Cie.RaColumn, out int raOff)) break;
                int raAt = cfa.Value + raOff;
                int ra = read(raAt) << 8 | read(raAt + 1);
                var next = new int?[33];
                foreach (int r in CallSaved) next[r] = regs[r];
                next[1] = 0;   // the zero register
                foreach (var (reg, off) in rules.Saved)
                    if (reg < 32) next[reg] = read(cfa.Value + off);
                next[32] = cfa;
                regs = next;
                uint back = (uint)ra * 2;
                if (ra == 0 || back == at || FunctionAt((int)back) == null) break;
                at = back;
            }

            return frames;
        }

        /// <summary>The functions at one physical frame: the inlined ones innermost first, then the real one.</summary>
        private void AddFrames(List<DebugFrame> frames, uint here, int pc, int?[] regs, int? cfa, Func<int, byte> read)
        {
            var chain = new List<DebugScope>();
            DebugScope? FindIn(List<DebugScope> list)
            {
                foreach (var s in list)
                    if (s.Contains(here)) return s;
                return null;
            }

            for (var s = FindIn(_scopes); s != null; s = FindIn(s.Children)) chain.Add(s);
            if (chain.Count == 0)
            {
                frames.Add(new DebugFrame { Function = FunctionAt(pc) ?? "?", Place = Locate((int)here), Pc = pc });
                return;
            }

            var function = chain.LastOrDefault(s => s.Kind == DebugScopeKind.Function) ?? chain[0];
            long? frameBase = FrameBase(function, here, regs, cfa, read);
            SourcePlace? place = Locate((int)here);
            // from the innermost scope out: each function (real or inlined) is a frame with the locals of its blocks
            var pending = new List<DebugScope>();
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                var s = chain[i];
                pending.Add(s);
                if (s.Kind == DebugScopeKind.Block) continue;
                var frame = new DebugFrame { Function = s.Name, Place = place, Pc = pc, Inlined = s.Kind == DebugScopeKind.Inlined };
                foreach (var scope in Enumerable.Reverse(pending))
                    foreach (var local in scope.Locals)
                        frame.Locals.Add(Value(local, scope, here, regs, cfa, frameBase, read));
                frames.Add(frame);
                pending.Clear();
                // the caller's line is the line this inlined code was called from
                if (s.Kind == DebugScopeKind.Inlined && s.CallLine > 0)
                {
                    string? sketch = SketchPath(s.CallFile);
                    place = new SourcePlace(sketch ?? s.CallFile, s.CallLine, sketch != null);
                }
            }
        }

        private long? FrameBase(DebugScope function, uint here, int?[] regs, int? cfa, Func<int, byte> read)
        {
            byte[]? expr = function.FrameBase ?? (function.FrameBaseList >= 0 ? LocationAt(function.FrameBaseList, function.UnitBase, here) : null);
            if (expr == null) return null;
            var v = Evaluate(expr, 2, regs, cfa, null, read);
            if (v == null) return null;
            return v.Value.Kind == ValueKind.Register ? Pair(regs, v.Value.Register) : v.Value.Number;
        }

        /// <summary>A location list's expression at an address (begin/end from the unit's base; a base-address entry moves it).</summary>
        private byte[]? LocationAt(int offset, uint unitBase, uint address)
        {
            if (offset < 0 || offset >= _loc.Length) return null;
            var c = new DwarfCursor(_loc, offset);
            uint b = unitBase;
            try
            {
                for (int guard = 0; guard < 4096 && c.Pos + 8 <= _loc.Length; guard++)
                {
                    uint begin = c.U32(), end = c.U32();
                    if (begin == 0 && end == 0) return null;
                    if (begin == 0xFFFFFFFF)
                    {
                        b = end;
                        continue;
                    }

                    int len = c.U16();
                    if (address >= b + begin && address < b + end) return _loc.Skip(c.Pos).Take(len).ToArray();
                    c.Pos += len;
                }
            }
            catch (IndexOutOfRangeException)
            {
            }

            return null;
        }

        private DebugLocalValue Value(DebugLocal local, DebugScope scope, uint here, int?[] regs, int? cfa, long? frameBase, Func<int, byte> read)
        {
            var v = new DebugLocalValue { Name = local.Name, TypeName = local.Type.Name.Length > 0 ? local.Type.Name : "?", IsParameter = local.IsParameter };
            byte[]? expr = local.Location ?? (local.LocationList >= 0 ? LocationAt(local.LocationList, scope.UnitBase, here) : null);
            int size = Math.Max(1, Math.Min(8, local.Type.Size));
            if (expr == null) return v;
            var bytes = Bytes(expr, size, regs, cfa, frameBase, read, out string where);
            if (bytes == null) return v;
            v.Available = true;
            v.Where = where;
            v.Value = FormatBytes(local.Type, bytes, read);
            return v;
        }

        /// <summary>A value of a type from its bytes (aggregates beyond them read as zero).</summary>
        public static string FormatBytes(DebugType type, byte[] bytes, Func<int, byte>? memory = null)
        {
            const int Base = 0x100000;   // far past the AVR's data space
            return Format(type, Base, a => a >= Base && a - Base < bytes.Length ? bytes[a - Base] : memory != null && a < Base ? memory(a) : (byte)0, 0);
        }

        private enum ValueKind
        {
            Number,
            Register,
            Memory
        }

        private readonly struct Loc
        {
            public Loc(ValueKind kind, long number, int register)
            {
                Kind = kind;
                Number = number;
                Register = register;
            }

            public ValueKind Kind { get; }
            public long Number { get; }
            public int Register { get; }
        }

        /// <summary>
        /// A DWARF location expression's value: a register (DW_OP_reg), an address (most operators push one), or a value
        /// (DW_OP_stack_value); null when it needs something unknown (a register a caller does not keep, an entry value).
        /// </summary>
        private static Loc? Evaluate(byte[] expr, int addressSize, int?[] regs, int? cfa, long? frameBase, Func<int, byte> read)
        {
            var stack = new Stack<long>();
            var c = new DwarfCursor(expr);
            try
            {
                while (c.Pos < expr.Length)
                {
                    byte op = c.U8();
                    if (op >= 0x30 && op <= 0x4f) { stack.Push(op - 0x30); continue; }                 // lit0…31
                    if (op >= 0x50 && op <= 0x6f) return new Loc(ValueKind.Register, 0, op - 0x50);  // reg0…31
                    if (op >= 0x70 && op <= 0x8f)                                                       // breg0…31
                    {
                        long off = c.Sleb();
                        if (Pair(regs, op - 0x70) is not int b) return null;
                        stack.Push(b + off);
                        continue;
                    }

                    switch (op)
                    {
                        case 0x03: stack.Push(addressSize == 4 ? c.U32() : c.U16()); break;   // addr
                        case 0x06: { long a = stack.Pop(); stack.Push(Data(a, read) | Data(a + 1, read) << 8); break; }   // deref: a 16-bit pointer
                        case 0x08: stack.Push(c.U8()); break;
                        case 0x09: stack.Push((sbyte)c.U8()); break;
                        case 0x0a: stack.Push(c.U16()); break;
                        case 0x0b: stack.Push((short)c.U16()); break;
                        case 0x0c: stack.Push(c.U32()); break;
                        case 0x0d: stack.Push((int)c.U32()); break;
                        case 0x10: stack.Push((long)c.Uleb()); break;
                        case 0x11: stack.Push(c.Sleb()); break;
                        case 0x12: stack.Push(stack.Peek()); break;                     // dup
                        case 0x13: stack.Pop(); break;                                  // drop
                        case 0x14: { long a = stack.Pop(), b = stack.Peek(); stack.Push(a); stack.Push(b); break; }   // over
                        case 0x16: { long a = stack.Pop(), b = stack.Pop(); stack.Push(a); stack.Push(b); break; }    // swap
                        case 0x1a: { long a = stack.Pop(); stack.Push(stack.Pop() & a); break; }
                        case 0x1c: { long a = stack.Pop(); stack.Push(stack.Pop() - a); break; }
                        case 0x1e: { long a = stack.Pop(); stack.Push(stack.Pop() * a); break; }
                        case 0x1f: stack.Push(-stack.Pop()); break;
                        case 0x20: stack.Push(~stack.Pop()); break;
                        case 0x21: { long a = stack.Pop(); stack.Push(stack.Pop() | a); break; }
                        case 0x22: { long a = stack.Pop(); stack.Push(stack.Pop() + a); break; }
                        case 0x23: stack.Push(stack.Pop() + (long)c.Uleb()); break;    // plus_uconst
                        case 0x24: { long a = stack.Pop(); stack.Push(stack.Pop() << (int)a); break; }
                        case 0x25: { long a = stack.Pop(); stack.Push((long)((ulong)stack.Pop() >> (int)a)); break; }
                        case 0x26: { long a = stack.Pop(); stack.Push(stack.Pop() >> (int)a); break; }
                        case 0x27: { long a = stack.Pop(); stack.Push(stack.Pop() ^ a); break; }
                        case 0x90: return new Loc(ValueKind.Register, 0, (int)c.Uleb());   // regx
                        case 0x91:                                                          // fbreg
                        {
                            long off = c.Sleb();
                            if (frameBase == null) return null;
                            stack.Push(frameBase.Value + off);
                            break;
                        }

                        case 0x92:                                                          // bregx
                        {
                            int reg = (int)c.Uleb();
                            long off = c.Sleb();
                            if (Pair(regs, reg) is not int b) return null;
                            stack.Push(b + off);
                            break;
                        }

                        case 0x96: break;                                                   // nop
                        case 0x9c: if (cfa == null) return null; stack.Push(cfa.Value); break;   // call_frame_cfa
                        case 0x9f: return stack.Count > 0 ? new Loc(ValueKind.Number, stack.Pop(), 0) : null;   // stack_value
                        default: return null;                                               // entry values, pieces here …: not known
                    }
                }
            }
            catch (InvalidOperationException)
            {
                return null;   // a malformed expression
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }

            return stack.Count > 0 ? new Loc(ValueKind.Memory, stack.Pop(), 0) : null;
        }

        /// <summary>A data-space byte from a DWARF address (the AVR's RAM is at 0x800000 in its ELF).</summary>
        private static long Data(long address, Func<int, byte> read) => read((int)(address >= 0x800000 ? address - 0x800000 : address));

        /// <summary>
        /// A local's bytes from its location expression, piece by piece (DW_OP_piece: an int in r24 and r25, a long in
        /// four registers or half in a register and half on the stack); null when any piece is unknown.
        /// </summary>
        private static byte[]? Bytes(byte[] expr, int size, int?[] regs, int? cfa, long? frameBase, Func<int, byte> read, out string where)
        {
            where = string.Empty;
            var result = new List<byte>();
            var places = new List<string>();
            int start = 0;
            var c = new DwarfCursor(expr);
            bool pieces = false;
            try
            {
                while (true)
                {
                    // the next piece: its expression runs to a DW_OP_piece (or the end)
                    int end = PieceEnd(expr, start, out int pieceSize, out int next);
                    var part = expr.Skip(start).Take(end - start).ToArray();
                    if (pieceSize > 0) pieces = true;
                    int want = pieceSize > 0 ? pieceSize : size - result.Count;
                    if (want <= 0) break;
                    if (part.Length == 0)
                    {
                        // an empty piece: that part of the value is optimized out
                        return null;
                    }

                    var loc = Evaluate(part, 4, regs, cfa, frameBase, read);
                    if (loc == null) return null;
                    switch (loc.Value.Kind)
                    {
                        case ValueKind.Register:
                            for (int i = 0; i < want; i++)
                            {
                                int r = loc.Value.Register + i;
                                if (r >= 32 || regs[r] is not int b) return null;
                                result.Add((byte)b);
                            }

                            places.Add(want == 1 ? "r" + loc.Value.Register : "r" + loc.Value.Register + "–r" + (loc.Value.Register + want - 1));
                            break;
                        case ValueKind.Memory:
                            for (int i = 0; i < want; i++) result.Add((byte)Data(loc.Value.Number + i, read));
                            long shown = loc.Value.Number >= 0x800000 ? loc.Value.Number - 0x800000 : loc.Value.Number;
                            places.Add("0x" + shown.ToString("X4", CultureInfo.InvariantCulture));
                            break;
                        default:
                            for (int i = 0; i < want; i++) result.Add((byte)(loc.Value.Number >> (8 * i)));
                            places.Add("değer");
                            break;
                    }

                    if (next < 0 || result.Count >= size) break;
                    start = next;
                }
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }

            _ = pieces;
            where = string.Join(" + ", places);
            while (result.Count < size) result.Add(0);
            return result.ToArray();
        }

        /// <summary>Where the piece starting at <paramref name="start"/> ends: at its DW_OP_piece (its size out, and where the next one starts) or at the end.</summary>
        private static int PieceEnd(byte[] expr, int start, out int pieceSize, out int next)
        {
            var c = new DwarfCursor(expr, start);
            pieceSize = 0;
            next = -1;
            while (c.Pos < expr.Length)
            {
                int at = c.Pos;
                byte op = c.U8();
                if (op == 0x93)
                {
                    pieceSize = (int)c.Uleb();
                    next = c.Pos < expr.Length ? c.Pos : -1;
                    return at;
                }

                SkipOperands(op, c);
            }

            return expr.Length;
        }

        private static void SkipOperands(byte op, DwarfCursor c)
        {
            if (op >= 0x70 && op <= 0x8f) { c.Sleb(); return; }
            switch (op)
            {
                case 0x03: c.U32(); break;
                case 0x08: case 0x09: c.U8(); break;
                case 0x0a: case 0x0b: c.U16(); break;
                case 0x0c: case 0x0d: c.U32(); break;
                case 0x10: case 0x23: case 0x90: c.Uleb(); break;
                case 0x11: case 0x91: c.Sleb(); break;
                case 0x92: c.Uleb(); c.Sleb(); break;
                case 0x94: c.U8(); break;
                case 0x9e: { int n = (int)c.Uleb(); c.Pos += n; break; }
                case 0xf3: { int n = (int)c.Uleb(); c.Pos += n; break; }   // GNU_entry_value
            }
        }
    }
}
