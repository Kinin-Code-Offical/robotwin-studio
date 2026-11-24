using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class XtensaCore
    {
        private static readonly int[] B4Const = { -1, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 16, 32, 64, 128, 256 };
        private static readonly uint[] B4ConstU = { 32768, 65536, 2, 3, 4, 5, 6, 7, 8, 10, 12, 16, 32, 64, 128, 256 };

        private const int TakenBranch = 3;

        private static int Sx(uint value, int bits) => (int)(value << (32 - bits)) >> (32 - bits);

        /// <summary>Leading zero bits of a word (32 for zero).</summary>
        private static int LeadingZeros(uint x)
        {
            if (x == 0) return 32;
            int n = 0;
            if ((x & 0xFFFF0000) == 0) { n += 16; x <<= 16; }
            if ((x & 0xFF000000) == 0) { n += 8; x <<= 8; }
            if ((x & 0xF0000000) == 0) { n += 4; x <<= 4; }
            if ((x & 0xC0000000) == 0) { n += 2; x <<= 2; }
            if ((x & 0x80000000) == 0) n += 1;
            return n;
        }

        private static int Frames(int a, int b = 0, int c = 0) => Math.Max(a, Math.Max(b, c)) >> 2;

        private uint L32(uint address) => _bus.Read32(address & ~3u);
        private void S32(uint address, uint value) => _bus.Write32(address & ~3u, value);
        private bool Bool(int index) => (Br & (1u << index)) != 0;
        private void SetBool(int index, bool on) => Br = on ? Br | (1u << index) : Br & ~(1u << index);

        /// <summary>A taken conditional branch to pc + 4 + offset.</summary>
        private int BranchIf(bool taken, int offset)
        {
            if (!taken) return 1;
            Branch((uint)((int)Pc + 4 + offset));
            return TakenBranch;
        }

        // ================================================================ 16-bit (code density) instructions

        private int ExecuteNarrow(uint insn)
        {
            int op0 = (int)(insn & 0xF), t = (int)((insn >> 4) & 0xF), s = (int)((insn >> 8) & 0xF), r = (int)((insn >> 12) & 0xF);
            switch (op0)
            {
                case 0x8:   // L32I.N at, as, r*4
                    if (WindowCheck(Frames(s, t))) return 1;
                    SetA(t, L32(A(s) + (uint)(r * 4)));
                    return 1;
                case 0x9:   // S32I.N
                    if (WindowCheck(Frames(s, t))) return 1;
                    S32(A(s) + (uint)(r * 4), A(t));
                    return 1;
                case 0xA:   // ADD.N
                    if (WindowCheck(Frames(r, s, t))) return 1;
                    SetA(r, A(s) + A(t));
                    return 1;
                case 0xB:   // ADDI.N ar, as, imm (0 means -1)
                    if (WindowCheck(Frames(r, s))) return 1;
                    SetA(r, A(s) + (uint)(t == 0 ? -1 : t));
                    return 1;
                case 0xC:
                    if (WindowCheck(Frames(s))) return 1;
                    if ((t & 8) == 0)
                    {
                        // MOVI.N as, -32..95
                        int imm7 = ((t & 7) << 4) | r;
                        SetA(s, (uint)(imm7 >= 96 ? imm7 - 128 : imm7));
                        return 1;
                    }

                    {
                        int imm6 = ((t & 3) << 4) | r;
                        bool zero = A(s) == 0;
                        return BranchIf((t & 4) == 0 ? zero : !zero, imm6);   // BEQZ.N / BNEZ.N
                    }

                case 0xD:
                    if (r == 0)
                    {
                        // MOV.N at, as
                        if (WindowCheck(Frames(s, t))) return 1;
                        SetA(t, A(s));
                        return 1;
                    }

                    if (r == 0xF)
                    {
                        if (t == 2)
                        {
                            AtBreak = true;   // BREAK.N
                            _nextPc = Pc;
                            return 1;
                        }

                        if (s == 0)
                        {
                            switch (t)
                            {
                                case 0: Branch(A(0)); return TakenBranch;   // RET.N
                                case 1: return Retw();                       // RETW.N
                                case 3: return 1;                            // NOP.N
                            }
                        }
                    }

                    Illegal();
                    return 1;
                default:
                    Illegal();
                    return 1;
            }
        }

        // ================================================================ 24-bit instructions

        private int ExecuteWide(uint insn)
        {
            int op0 = (int)(insn & 0xF);
            int t = (int)((insn >> 4) & 0xF), s = (int)((insn >> 8) & 0xF), r = (int)((insn >> 12) & 0xF);
            int op1 = (int)((insn >> 16) & 0xF), op2 = (int)((insn >> 20) & 0xF);
            uint imm8 = (insn >> 16) & 0xFF;
            switch (op0)
            {
                case 0: return Qrst(insn, t, s, r, op1, op2);
                case 1:
                {
                    // L32R at, literal: a negative word offset from the next word-aligned PC
                    if (WindowCheck(Frames(t))) return 1;
                    uint address = (uint)(((int)Pc + 3 & ~3) + ((int)(0xFFFC0000u | ((insn >> 8) << 2))));
                    SetA(t, L32(address));
                    return 1;
                }

                case 2: return LoadStore(t, s, r, imm8);
                case 3: return LoadStoreFloat(t, s, r, imm8);
                case 4:
                    Illegal();   // MAC16 (not emitted by the ESP32's compiler)
                    return 1;
                case 5:
                {
                    // CALL0 / CALL4 / CALL8 / CALL12
                    int n = t & 3;
                    if (WindowCheck(n)) return 1;
                    uint target = (uint)(((int)Pc & ~3) + (Sx(insn >> 6, 18) << 2) + 4);
                    Call(n, target);
                    return TakenBranch;
                }

                case 6: return Si(insn, t, s, r, imm8);
                case 7: return BranchRri8(t, s, r, imm8);
                default:
                    Illegal();
                    return 1;
            }
        }

        private void Call(int n, uint target)
        {
            uint ret = Pc + 3;
            if (n == 0)
            {
                SetA(0, ret);
            }
            else
            {
                SetA(n * 4, (uint)(n << 30) | (ret & 0x3FFFFFFF));
                SetCallInc(n);
            }

            Branch(target);
        }

        private int Retw()
        {
            uint a0 = A(0);
            int n = (int)(a0 >> 30);
            if (n == 0 || !Woe || Excm)
            {
                Illegal();
                return 1;
            }

            uint target = (Pc & 0xC0000000) | (a0 & 0x3FFFFFFF);
            int owb = WindowBase;
            WindowBase = (WindowBase - n) & 15;
            if (WindowStartAt(WindowBase))
            {
                SetWindowStartAt(owb, false);
                Branch(target);
                return TakenBranch;
            }

            // The caller's frame was spilled: the underflow handler reloads it, then RETW runs again.
            SetExcm(true);
            Epc[1] = Pc;
            SetOwb(owb);
            _nextPc = Vecbase + (n == 1 ? WindowUnderflow4 : n == 2 ? WindowUnderflow8 : WindowUnderflow12);
            _excepted = true;
            return 1;
        }

        private int Qrst(uint insn, int t, int s, int r, int op1, int op2)
        {
            switch (op1)
            {
                case 0: return Rst0(t, s, r, op2);
                case 1: return Rst1(t, s, r, op2);
                case 2: return Rst2(t, s, r, op2);
                case 3: return Rst3(t, s, r, op2);
                case 4:
                case 5:
                {
                    // EXTUI ar, at, shift, bits
                    if (WindowCheck(Frames(r, t))) return 1;
                    int shift = ((op1 & 1) << 4) | s;
                    int bits = op2 + 1;
                    SetA(r, (A(t) >> shift) & (uint)((1L << bits) - 1));
                    return 1;
                }

                case 8:
                {
                    // LSX / LSXU / SSX / SSXU fr, as, at
                    if (!FpuEnabled()) return 1;
                    if (WindowCheck(Frames(s, t))) return 1;
                    uint address = A(s) + A(t);
                    switch (op2)
                    {
                        case 0: F[r] = BitConverter.Int32BitsToSingle((int)L32(address)); return 1;
                        case 1: F[r] = BitConverter.Int32BitsToSingle((int)L32(address)); SetA(s, address); return 1;
                        case 4: S32(address, (uint)BitConverter.SingleToInt32Bits(F[r])); return 1;
                        case 5: S32(address, (uint)BitConverter.SingleToInt32Bits(F[r])); SetA(s, address); return 1;
                    }

                    break;
                }

                case 9:
                {
                    // L32E / S32E at, as, offset -64..-4: the window handlers' spill and reload
                    uint address = A(s) + (uint)((r - 16) * 4);
                    if (op2 == 0)
                    {
                        SetA(t, L32(address));
                        return 1;
                    }

                    if (op2 == 4)
                    {
                        S32(address, A(t));
                        return 1;
                    }

                    break;
                }

                case 0xA: return Fp0(t, s, r, op2);
                case 0xB: return Fp1(t, s, r, op2);
            }

            Illegal();
            return 1;
        }

        private int Rst0(int t, int s, int r, int op2)
        {
            switch (op2)
            {
                case 0: return St0(t, s, r);
                case 1:
                case 2:
                case 3:
                    if (WindowCheck(Frames(r, s, t))) return 1;
                    SetA(r, op2 == 1 ? A(s) & A(t) : op2 == 2 ? A(s) | A(t) : A(s) ^ A(t));
                    return 1;
                case 4: return St1(t, s, r);
                case 6:
                    if (WindowCheck(Frames(r, t))) return 1;
                    if (s == 0)
                    {
                        SetA(r, (uint)-(int)A(t));   // NEG
                        return 1;
                    }

                    if (s == 1)
                    {
                        int v = (int)A(t);
                        SetA(r, (uint)(v < 0 ? -v : v));   // ABS
                        return 1;
                    }

                    break;
                case 8:
                case 9:
                case 0xA:
                case 0xB:
                case 0xC:
                case 0xD:
                case 0xE:
                case 0xF:
                {
                    // ADD, ADDX2/4/8, SUB, SUBX2/4/8
                    if (WindowCheck(Frames(r, s, t))) return 1;
                    int shift = op2 & 3;
                    uint a = A(s) << shift;
                    SetA(r, op2 < 0xC ? a + A(t) : a - A(t));
                    return 1;
                }
            }

            Illegal();
            return 1;
        }

        private int St0(int t, int s, int r)
        {
            switch (r)
            {
                case 0:
                {
                    int m = t >> 2, n = t & 3;
                    if (m == 2)
                    {
                        if (n == 0)
                        {
                            Branch(A(0));   // RET
                            return TakenBranch;
                        }

                        if (n == 1) return Retw();
                        if (n == 2)
                        {
                            if (WindowCheck(Frames(s))) return 1;
                            Branch(A(s));   // JX
                            return TakenBranch;
                        }
                    }
                    else if (m == 3)
                    {
                        // CALLX0 / 4 / 8 / 12
                        if (WindowCheck(Math.Max(Frames(s), n))) return 1;
                        Call(n, A(s));
                        return TakenBranch;
                    }

                    break;
                }

                case 1:
                {
                    // MOVSP at, as: the callers' frames must be in memory first (an alloca moves the stack)
                    if (WindowCheck(Frames(s, t))) return 1;
                    if (Woe && (WindowStartAt(WindowBase - 1) || WindowStartAt(WindowBase - 2) || WindowStartAt(WindowBase - 3)))
                    {
                        Exception(XtensaCause.Alloca);
                        return 1;
                    }

                    SetA(t, A(s));
                    return 1;
                }

                case 2:
                    return 1;   // ISYNC, RSYNC, ESYNC, DSYNC, EXCW, MEMW, EXTW, NOP
                case 3:
                    if (t == 0)
                    {
                        switch (s)
                        {
                            case 0:   // RFE
                            case 1:   // RFUE
                                SetExcm(false);
                                Branch(Epc[1]);
                                return TakenBranch;
                            case 2:   // RFDE
                                SetExcm(false);
                                Branch(Depc);
                                return TakenBranch;
                            case 4:   // RFWO
                                SetExcm(false);
                                SetWindowStartAt(WindowBase, false);
                                WindowBase = Owb;
                                Branch(Epc[1]);
                                return TakenBranch;
                            case 5:   // RFWU
                                SetExcm(false);
                                SetWindowStartAt(WindowBase, true);
                                WindowBase = Owb;
                                Branch(Epc[1]);
                                return TakenBranch;
                        }
                    }
                    else if (t == 1 && s >= 1 && s <= 7)
                    {
                        // RFI level
                        Ps = Eps[s];
                        Branch(Epc[s]);
                        return TakenBranch;
                    }

                    break;
                case 4:
                    AtBreak = true;   // BREAK s, t
                    _nextPc = Pc;
                    return 1;
                case 5:
                    if (s == 0)
                    {
                        Exception(XtensaCause.Syscall);
                        return 1;
                    }

                    return 1;   // SIMCALL
                case 6:
                    // RSIL at, level
                    if (WindowCheck(Frames(t))) return 1;
                    SetA(t, Ps);
                    SetIntLevel(s);
                    return 1;
                case 7:
                    // WAITI level
                    SetIntLevel(s);
                    Waiting = true;
                    return 1;
                case 8:
                case 9:
                case 0xA:
                case 0xB:
                {
                    // ANY4, ALL4, ANY8, ALL8 bt, bs
                    int width = r < 0xA ? 4 : 8;
                    uint mask = ((1u << width) - 1) << s;
                    bool any = (Br & mask) != 0, all = (Br & mask) == mask;
                    SetBool(t, (r & 1) == 0 ? any : all);
                    return 1;
                }
            }

            Illegal();
            return 1;
        }

        private int St1(int t, int s, int r)
        {
            switch (r)
            {
                case 0: if (WindowCheck(Frames(s))) return 1; Sar = A(s) & 31; return 1;                    // SSR
                case 1: if (WindowCheck(Frames(s))) return 1; Sar = 32 - (A(s) & 31); return 1;            // SSL
                case 2: if (WindowCheck(Frames(s))) return 1; Sar = (A(s) & 3) * 8; return 1;              // SSA8L
                case 3: if (WindowCheck(Frames(s))) return 1; Sar = 32 - (A(s) & 3) * 8; return 1;         // SSA8B
                case 4: Sar = (uint)(((t & 1) << 4) | s); return 1;                                          // SSAI
                case 6: if (WindowCheck(Frames(t))) return 1; SetA(t, 0); return 1;                          // RER (no external registers)
                case 7: return 1;                                                                            // WER
                case 8: WindowBase = (WindowBase + Sx((uint)t, 4)) & 15; return 1;                           // ROTW
                case 0xE:
                {
                    // NSA at, as: redundant sign bits
                    if (WindowCheck(Frames(s, t))) return 1;
                    int v = (int)A(s);
                    uint x = (uint)(v < 0 ? ~v : v);
                    SetA(t, x == 0 ? 31u : (uint)(LeadingZeros(x) - 1));
                    return 1;
                }

                case 0xF:
                    // NSAU at, as: leading zeros
                    if (WindowCheck(Frames(s, t))) return 1;
                    SetA(t, (uint)LeadingZeros(A(s)));
                    return 1;
            }

            Illegal();
            return 1;
        }

        private int Rst1(int t, int s, int r, int op2)
        {
            switch (op2)
            {
                case 0:
                case 1:
                    // SLLI ar, as, 32 - field (the same funnel as SLL)
                    if (WindowCheck(Frames(r, s))) return 1;
                    SetA(r, (uint)(((ulong)A(s) << 32) >> (((op2 & 1) << 4) | t)));
                    return 1;
                case 2:
                case 3:
                    if (WindowCheck(Frames(r, t))) return 1;
                    SetA(r, (uint)((int)A(t) >> (((op2 & 1) << 4) | s)));   // SRAI
                    return 1;
                case 4:
                    if (WindowCheck(Frames(r, t))) return 1;
                    SetA(r, A(t) >> s);   // SRLI
                    return 1;
                case 6:
                {
                    // XSR at, sr
                    if (WindowCheck(Frames(t))) return 1;
                    int sr = (r << 4) | s;
                    uint old = ReadSpecial(sr);
                    WriteSpecial(sr, A(t));
                    SetA(t, old);
                    return 1;
                }

                case 8: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, (uint)((((ulong)A(s) << 32) | A(t)) >> (int)Sar)); return 1;   // SRC
                case 9: if (WindowCheck(Frames(r, t))) return 1; SetA(r, (uint)((ulong)A(t) >> (int)Sar)); return 1;                      // SRL
                case 0xA: if (WindowCheck(Frames(r, s))) return 1; SetA(r, (uint)(((ulong)A(s) << 32) >> (int)Sar)); return 1;           // SLL
                case 0xB: if (WindowCheck(Frames(r, t))) return 1; SetA(r, (uint)((long)(int)A(t) >> (int)Sar)); return 1;              // SRA
                case 0xC: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, (A(s) & 0xFFFF) * (A(t) & 0xFFFF)); return 1;            // MUL16U
                case 0xD: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, (uint)((short)A(s) * (short)A(t))); return 1;            // MUL16S
                case 0xF: return 1;   // RFDO, RFDD … (no debug option): nothing
            }

            Illegal();
            return 1;
        }

        private int Rst2(int t, int s, int r, int op2)
        {
            if (op2 <= 4)
            {
                // ANDB, ANDBC, ORB, ORBC, XORB br, bs, bt
                bool a = Bool(s), b = Bool(t);
                SetBool(r, op2 switch { 0 => a & b, 1 => a & !b, 2 => a | b, 3 => a | !b, _ => a ^ b });
                return 1;
            }

            if (WindowCheck(Frames(r, s, t))) return 1;
            uint x = A(s), y = A(t);
            switch (op2)
            {
                case 8: SetA(r, x * y); return 2;                                              // MULL
                case 0xA: SetA(r, (uint)(((ulong)x * y) >> 32)); return 2;                    // MULUH
                case 0xB: SetA(r, (uint)(((long)(int)x * (int)y) >> 32)); return 2;           // MULSH
                case 0xC:
                case 0xD:
                case 0xE:
                case 0xF:
                    if (y == 0)
                    {
                        Exception(XtensaCause.IntegerDivideByZero);
                        return 1;
                    }

                    switch (op2)
                    {
                        case 0xC: SetA(r, x / y); break;                                                          // QUOU
                        case 0xD: SetA(r, (int)x == int.MinValue && (int)y == -1 ? x : (uint)((int)x / (int)y)); break;   // QUOS
                        case 0xE: SetA(r, x % y); break;                                                          // REMU
                        default: SetA(r, (int)y == -1 ? 0u : (uint)((int)x % (int)y)); break;                     // REMS
                    }

                    return 12;
            }

            Illegal();
            return 1;
        }

        private int Rst3(int t, int s, int r, int op2)
        {
            switch (op2)
            {
                case 0:
                    if (WindowCheck(Frames(t))) return 1;
                    SetA(t, ReadSpecial((r << 4) | s));   // RSR
                    return 1;
                case 1:
                    if (WindowCheck(Frames(t))) return 1;
                    WriteSpecial((r << 4) | s, A(t));      // WSR
                    return 1;
                case 2:
                {
                    // SEXT ar, as, 7..22
                    if (WindowCheck(Frames(r, s))) return 1;
                    int bits = t + 8;
                    SetA(r, (uint)Sx(A(s), bits));
                    return 1;
                }

                case 3:
                {
                    // CLAMPS ar, as, 7..22
                    if (WindowCheck(Frames(r, s))) return 1;
                    int b = t + 7;
                    long v = (int)A(s), lo = -(1L << b), hi = (1L << b) - 1;
                    SetA(r, (uint)(int)Math.Clamp(v, lo, hi));
                    return 1;
                }

                case 4: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, (uint)Math.Min((int)A(s), (int)A(t))); return 1;   // MIN
                case 5: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, (uint)Math.Max((int)A(s), (int)A(t))); return 1;   // MAX
                case 6: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, Math.Min(A(s), A(t))); return 1;                   // MINU
                case 7: if (WindowCheck(Frames(r, s, t))) return 1; SetA(r, Math.Max(A(s), A(t))); return 1;                   // MAXU
                case 8:
                case 9:
                case 0xA:
                case 0xB:
                {
                    // MOVEQZ, MOVNEZ, MOVLTZ, MOVGEZ ar, as, at
                    if (WindowCheck(Frames(r, s, t))) return 1;
                    int v = (int)A(t);
                    bool move = op2 switch { 8 => v == 0, 9 => v != 0, 0xA => v < 0, _ => v >= 0 };
                    if (move) SetA(r, A(s));
                    return 1;
                }

                case 0xC:
                case 0xD:
                    // MOVF / MOVT ar, as, bt
                    if (WindowCheck(Frames(r, s))) return 1;
                    if (Bool(t) == (op2 == 0xD)) SetA(r, A(s));
                    return 1;
                case 0xE:
                    // RUR ar, ur
                    if (WindowCheck(Frames(r))) return 1;
                    SetA(r, ReadUser((s << 4) | t));
                    return 1;
                case 0xF:
                    // WUR at, ur
                    if (WindowCheck(Frames(t))) return 1;
                    WriteUser((r << 4) | s, A(t));
                    return 1;
            }

            Illegal();
            return 1;
        }

        private int LoadStore(int t, int s, int r, uint imm8)
        {
            if (WindowCheck(Frames(s, t))) return 1;
            uint a = A(s);
            switch (r)
            {
                case 0: SetA(t, _bus.Read8(a + imm8)); return 1;                                   // L8UI
                case 1: SetA(t, _bus.Read16((a + imm8 * 2) & ~1u)); return 1;                      // L16UI
                case 2: SetA(t, L32(a + imm8 * 4)); return 1;                                      // L32I
                case 4: _bus.Write8(a + imm8, (byte)A(t)); return 1;                               // S8I
                case 5: _bus.Write16((a + imm8 * 2) & ~1u, (ushort)A(t)); return 1;                // S16I
                case 6: S32(a + imm8 * 4, A(t)); return 1;                                         // S32I
                case 7: return 1;                                                                  // cache operations
                case 9: SetA(t, (uint)(short)_bus.Read16((a + imm8 * 2) & ~1u)); return 1;         // L16SI
                case 0xA: SetA(t, (uint)Sx((uint)(s << 8) | imm8, 12)); return 1;                  // MOVI
                case 0xB: SetA(t, L32(a + imm8 * 4)); return 1;                                    // L32AI
                case 0xC: SetA(t, a + (uint)(sbyte)imm8); return 1;                                // ADDI
                case 0xD: SetA(t, a + (uint)((sbyte)imm8 << 8)); return 1;                         // ADDMI
                case 0xE:
                {
                    // S32C1I at, as, imm: compare with SCOMPARE1 and swap (FreeRTOS's spinlocks)
                    uint address = a + imm8 * 4;
                    uint old = L32(address);
                    if (old == Scompare1) S32(address, A(t));
                    SetA(t, old);
                    return 2;
                }

                case 0xF: S32(a + imm8 * 4, A(t)); return 1;                                       // S32RI
            }

            Illegal();
            return 1;
        }

        private int Si(uint insn, int t, int s, int r, uint imm8)
        {
            int n = t & 3, m = t >> 2;
            switch (n)
            {
                case 0:
                    Branch((uint)((int)Pc + 4 + Sx(insn >> 6, 18)));   // J
                    return TakenBranch;
                case 1:
                {
                    // BEQZ, BNEZ, BLTZ, BGEZ as, label (12-bit offset)
                    if (WindowCheck(Frames(s))) return 1;
                    int v = (int)A(s);
                    bool taken = m switch { 0 => v == 0, 1 => v != 0, 2 => v < 0, _ => v >= 0 };
                    return BranchIf(taken, Sx(insn >> 12, 12));
                }

                case 2:
                {
                    // BEQI, BNEI, BLTI, BGEI as, constant, label
                    if (WindowCheck(Frames(s))) return 1;
                    int v = (int)A(s), c = B4Const[r];
                    bool taken = m switch { 0 => v == c, 1 => v != c, 2 => v < c, _ => v >= c };
                    return BranchIf(taken, (sbyte)imm8);
                }

                default:
                    switch (m)
                    {
                        case 0:
                        {
                            // ENTRY as, frame: the callee's window, its stack frame carved out
                            int inc = CallInc;
                            if (s > 3 || !Woe || Excm)
                            {
                                Illegal();
                                return 1;
                            }

                            if (WindowCheck(inc)) return 1;
                            uint frame = (insn >> 12) << 3;
                            uint sp = A(s) - frame;
                            SetA(inc * 4 + s, sp);
                            WindowBase = (WindowBase + inc) & 15;
                            SetWindowStartAt(WindowBase, true);
                            return 1;
                        }

                        case 1:
                            switch (r)
                            {
                                case 0: return BranchIf(!Bool(s), (sbyte)imm8);   // BF
                                case 1: return BranchIf(Bool(s), (sbyte)imm8);    // BT
                                case 8:
                                case 9:
                                case 0xA:
                                {
                                    // LOOP, LOOPNEZ, LOOPGTZ as, end
                                    if (WindowCheck(Frames(s))) return 1;
                                    uint count = A(s);
                                    Lbeg = Pc + 3;
                                    Lend = Pc + 4 + imm8;
                                    Lcount = count - 1;
                                    bool skip = r == 9 ? count == 0 : r == 0xA && (int)count <= 0;
                                    if (skip)
                                    {
                                        Lcount = 0;
                                        Branch(Lend);
                                        return TakenBranch;
                                    }

                                    return 1;
                                }
                            }

                            break;
                        default:
                        {
                            // BLTUI, BGEUI as, constant, label
                            if (WindowCheck(Frames(s))) return 1;
                            uint v = A(s), c = B4ConstU[r];
                            return BranchIf(m == 2 ? v < c : v >= c, (sbyte)imm8);
                        }
                    }

                    break;
            }

            Illegal();
            return 1;
        }

        private int BranchRri8(int t, int s, int r, uint imm8)
        {
            if (WindowCheck(Frames(s, t))) return 1;
            uint a = A(s), b = A(t);
            int offset = (sbyte)imm8;
            switch (r)
            {
                case 0: return BranchIf((a & b) == 0, offset);                      // BNONE
                case 1: return BranchIf(a == b, offset);                            // BEQ
                case 2: return BranchIf((int)a < (int)b, offset);                   // BLT
                case 3: return BranchIf(a < b, offset);                             // BLTU
                case 4: return BranchIf((~a & b) == 0, offset);                     // BALL
                case 5: return BranchIf((a & (1u << (int)(b & 31))) == 0, offset);  // BBC
                case 6:
                case 7: return BranchIf((a & (1u << (((r & 1) << 4) | t))) == 0, offset);   // BBCI
                case 8: return BranchIf((a & b) != 0, offset);                      // BANY
                case 9: return BranchIf(a != b, offset);                            // BNE
                case 0xA: return BranchIf((int)a >= (int)b, offset);                // BGE
                case 0xB: return BranchIf(a >= b, offset);                          // BGEU
                case 0xC: return BranchIf((~a & b) != 0, offset);                   // BNALL
                case 0xD: return BranchIf((a & (1u << (int)(b & 31))) != 0, offset);   // BBS
                default: return BranchIf((a & (1u << (((r & 1) << 4) | t))) != 0, offset);   // BBSI
            }
        }

        // ================================================================ special and user registers

        public uint ReadSpecial(int sr)
        {
            switch (sr)
            {
                case 0: return Lbeg;
                case 1: return Lend;
                case 2: return Lcount;
                case 3: return Sar;
                case 4: return Br;
                case 5: return Litbase;
                case 12: return Scompare1;
                case 16: return AccLo;
                case 17: return AccHi;
                case 32: case 33: case 34: case 35: return M[sr - 32];
                case 72: return (uint)WindowBase;
                case 73: return WindowStart;
                case 97: return MemCtl;
                case 98: return CacheAttr;
                case 99: return AtomCtl;
                case 192: return Depc;
                case 224: return CpEnable;
                case 226: return Interrupt;
                case 228: return IntEnable;
                case 230: return Ps;
                case 231: return Vecbase;
                case 232: return ExcCause;
                case 234: return Ccount;
                case 235: return CoreId == 0 ? 0xCDCDu : 0xABABu;   // PRID: PRO_CPU, APP_CPU
                case 238: return ExcVaddr;
            }

            if (sr >= 177 && sr <= 183) return Epc[sr - 176];
            if (sr >= 194 && sr <= 199) return Eps[sr - 192];
            if (sr >= 209 && sr <= 215) return ExcSave[sr - 208];
            if (sr >= 240 && sr <= 242) return Ccompare[sr - 240];
            if (sr >= 244 && sr <= 247) return Misc[sr - 244];
            return 0;
        }

        public void WriteSpecial(int sr, uint value)
        {
            switch (sr)
            {
                case 0: Lbeg = value; return;
                case 1: Lend = value; return;
                case 2: Lcount = value; return;
                case 3: Sar = value & 63; return;
                case 4: Br = value & 0xFFFF; return;
                case 5: Litbase = value; return;
                case 12: Scompare1 = value; return;
                case 16: AccLo = value; return;
                case 17: AccHi = value & 0xFF; return;
                case 32: case 33: case 34: case 35: M[sr - 32] = value; return;
                case 72: WindowBase = (int)(value & 15); return;
                case 73: WindowStart = value & 0xFFFF; return;
                case 97: MemCtl = value; return;
                case 98: CacheAttr = value; return;
                case 99: AtomCtl = value; return;
                case 192: Depc = value; return;
                case 224: CpEnable = value & 0xFF; return;
                case 226: Interrupt |= value; return;        // INTSET
                case 227: Interrupt &= ~value; return;       // INTCLEAR
                case 228: IntEnable = value; return;
                case 230: Ps = value; return;
                case 231: Vecbase = value; return;
                case 232: ExcCause = value; return;
                case 234: Ccount = value; return;
                case 238: ExcVaddr = value; return;
            }

            if (sr >= 177 && sr <= 183) Epc[sr - 176] = value;
            else if (sr >= 194 && sr <= 199) Eps[sr - 192] = value;
            else if (sr >= 209 && sr <= 215) ExcSave[sr - 208] = value;
            else if (sr >= 240 && sr <= 242)
            {
                Ccompare[sr - 240] = value;
                ClearInterrupt(TimerInterrupt[sr - 240]);   // writing CCOMPAREn acknowledges its interrupt
            }
            else if (sr >= 244 && sr <= 247) Misc[sr - 244] = value;
        }

        private uint ReadUser(int ur) => ur switch { 231 => ThreadPtr, 232 => Fcr, 233 => Fsr, _ => 0 };

        private void WriteUser(int ur, uint value)
        {
            if (ur == 231) ThreadPtr = value;
            else if (ur == 232) Fcr = value;
            else if (ur == 233) Fsr = value;
        }
    }
}
