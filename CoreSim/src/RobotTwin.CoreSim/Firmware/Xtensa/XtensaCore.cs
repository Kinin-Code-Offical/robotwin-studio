using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// What an Xtensa core reads and writes through: the chip's memory map (RAM, flash cache, ROM, peripherals). Every
    /// access is naturally aligned (the core masks the low address bits as the ISA does).
    /// </summary>
    public interface IXtensaBus
    {
        byte Read8(uint address);
        ushort Read16(uint address);
        uint Read32(uint address);
        void Write8(uint address, byte value);
        void Write16(uint address, ushort value);
        void Write32(uint address, uint value);
    }

    /// <summary>Exception causes (EXCCAUSE) the core raises itself.</summary>
    public static class XtensaCause
    {
        public const int IllegalInstruction = 0;
        public const int Syscall = 1;
        public const int InstructionFetchError = 2;
        public const int LoadStoreError = 3;
        public const int Level1Interrupt = 4;
        public const int Alloca = 5;
        public const int IntegerDivideByZero = 6;
        public const int Privileged = 8;
        public const int LoadStoreAlignment = 9;
        public const int Coprocessor0Disabled = 32;
    }

    /// <summary>
    /// [esp32emu] RobotWin's own Xtensa LX6 core, configured as the ESP32's (docs/gelistirici/ESP32_MODLARI.md, stage
    /// 2): the windowed register file (64 physical registers, CALL4/8/12, ENTRY, RETW, overflow and underflow
    /// exceptions with L32E/S32E), dense 16-bit instructions, zero-overhead loops, MUL16/MULL/MULUH/MULSH, QUOS/QUOU/REMS/
    /// REMU, MIN/MAX/CLAMPS/SEXT/NSA, booleans, S32C1I, the single-precision FPU, levels 1–5 interrupts and NMI,
    /// CCOUNT/CCOMPARE timers, and exceptions to the vectors at VECBASE. Cycles are counted per instruction.
    /// </summary>
    public sealed partial class XtensaCore
    {
        /// <summary>The ESP32's EXCMLEVEL: interrupts above it are taken even inside an exception handler.</summary>
        public const int ExcmLevel = 3;

        /// <summary>Level of each of the 32 interrupts, as the ESP32 configures them (14 is the NMI, level 7).</summary>
        public static readonly int[] InterruptLevel =
        {
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 3, 1, 1, 7, 3,
            5, 1, 1, 2, 2, 2, 3, 3, 4, 4, 5, 3, 4, 3, 4, 5,
        };

        /// <summary>Interrupts the CCOMPARE0–2 timers raise.</summary>
        public static readonly int[] TimerInterrupt = { 6, 15, 16 };

        // Vector offsets from VECBASE (the ESP32's relocatable vectors).
        public const uint WindowOverflow4 = 0x000, WindowUnderflow4 = 0x040, WindowOverflow8 = 0x080, WindowUnderflow8 = 0x0C0;
        public const uint WindowOverflow12 = 0x100, WindowUnderflow12 = 0x140, Level2Vector = 0x180, DebugVector = 0x280;
        public const uint NmiVector = 0x2C0, KernelExceptionVector = 0x300, UserExceptionVector = 0x340, DoubleExceptionVector = 0x3C0;

        private readonly IXtensaBus _bus;
        private readonly uint[] _ar = new uint[64];

        public XtensaCore(IXtensaBus bus, int coreId = 0)
        {
            _bus = bus;
            CoreId = coreId;
            Reset(0x40000400);
        }

        public int CoreId { get; }

        public uint Pc;
        public int WindowBase;

        /// <summary>One bit per 4-register frame that holds a live call frame.</summary>
        public uint WindowStart;

        public uint Sar, Lbeg, Lend, Lcount, Ps, Vecbase, ExcCause, ExcVaddr, Depc, Scompare1, ThreadPtr, Litbase, CpEnable;
        public uint IntEnable, Interrupt, Ccount, AccLo, AccHi, Fcr, Fsr, AtomCtl, MemCtl, CacheAttr;
        public readonly uint[] Epc = new uint[8], Eps = new uint[8], ExcSave = new uint[8], Ccompare = new uint[3], Misc = new uint[4], M = new uint[4];

        /// <summary>The 16 boolean registers b0–b15 (BR).</summary>
        public uint Br;

        /// <summary>The FPU's registers f0–f15.</summary>
        public readonly float[] F = new float[16];

        /// <summary>Clock cycles run (CCOUNT is its low 32 bits plus whatever software wrote).</summary>
        public ulong Cycles { get; private set; }

        /// <summary>Waiting in WAITI for an interrupt.</summary>
        public bool Waiting { get; private set; }

        /// <summary>Stopped at BREAK / BREAK.N (a debugger or a test stops here).</summary>
        public bool AtBreak { get; set; }

        /// <summary>Instructions the core does not know (each raises IllegalInstruction; counted for reports).</summary>
        public long IllegalInstructions { get; private set; }

        /// <summary>The last exception cause taken (for reports and tests), -1 when none.</summary>
        public int LastException { get; private set; } = -1;

        // PS fields
        public int IntLevel => (int)(Ps & 0xF);
        public bool Excm => (Ps & 0x10) != 0;
        public bool UserMode => (Ps & 0x20) != 0;
        public int Owb => (int)((Ps >> 8) & 0xF);
        public int CallInc => (int)((Ps >> 16) & 3);
        public bool Woe => (Ps & 0x40000) != 0;

        private void SetExcm(bool on) => Ps = on ? Ps | 0x10 : Ps & ~0x10u;
        private void SetOwb(int owb) => Ps = (Ps & ~0xF00u) | (uint)((owb & 0xF) << 8);
        private void SetCallInc(int n) => Ps = (Ps & ~0x30000u) | (uint)((n & 3) << 16);
        private void SetIntLevel(int level) => Ps = (Ps & ~0xFu) | (uint)(level & 0xF);

        /// <summary>The core as after reset: PS.INTLEVEL 15 with EXCM, window 0, executing from <paramref name="resetVector"/>.</summary>
        public void Reset(uint resetVector)
        {
            Array.Clear(_ar, 0, _ar.Length);
            Pc = resetVector;
            WindowBase = 0;
            WindowStart = 1;
            Ps = 0x1F;
            Vecbase = 0x40000000;
            Lcount = Lbeg = Lend = Sar = 0;
            IntEnable = Interrupt = 0;
            Waiting = AtBreak = false;
            CpEnable = 0;
        }

        // ================================================================ registers

        /// <summary>Address register a<paramref name="r"/> of the current window.</summary>
        public uint A(int r) => _ar[(WindowBase * 4 + r) & 63];

        public void SetA(int r, uint value) => _ar[(WindowBase * 4 + r) & 63] = value;

        /// <summary>A physical register (ar0–ar63), for debuggers and tests.</summary>
        public uint Physical(int index) => _ar[index & 63];

        private bool WindowStartAt(int frame) => (WindowStart & (1u << (frame & 15))) != 0;

        private void SetWindowStartAt(int frame, bool on)
        {
            uint bit = 1u << (frame & 15);
            WindowStart = on ? WindowStart | bit : WindowStart & ~bit;
        }

        /// <summary>
        /// The ISA's WindowCheck: before an instruction touches registers in the frames after the current one
        /// (<paramref name="frames"/> = the highest register it uses ÷ 4), any of those frames still live is spilled
        /// first through a window overflow exception; the instruction runs again after it. True when one was raised.
        /// </summary>
        private bool WindowCheck(int frames)
        {
            if (frames == 0 || !Woe || Excm) return false;
            int n = 0;
            if (frames >= 1 && WindowStartAt(WindowBase + 1)) n = 1;
            else if (frames >= 2 && WindowStartAt(WindowBase + 2)) n = 2;
            else if (frames >= 3 && WindowStartAt(WindowBase + 3)) n = 3;
            if (n == 0) return false;
            SetOwb(WindowBase);
            int m = (WindowBase + n) & 15;
            SetExcm(true);
            Epc[1] = Pc;
            uint vector = WindowStartAt(m + 1) ? WindowOverflow4 : WindowStartAt(m + 2) ? WindowOverflow8 : WindowOverflow12;
            WindowBase = m;
            _nextPc = Vecbase + vector;
            _excepted = true;
            return true;
        }

        // ================================================================ exceptions and interrupts

        /// <summary>Raises a general exception: to the user or kernel vector, or the double-exception vector inside one.</summary>
        private void Exception(int cause, uint vaddr = 0)
        {
            _excepted = true;
            LastException = cause;
            ExcCause = (uint)cause;
            ExcVaddr = vaddr;
            if (Excm)
            {
                Depc = Pc;
                _nextPc = Vecbase + DoubleExceptionVector;
                return;
            }

            Epc[1] = Pc;
            SetExcm(true);
            _nextPc = Vecbase + (UserMode ? UserExceptionVector : KernelExceptionVector);
        }

        /// <summary>
        /// [esp32emu] The level-triggered interrupt lines outside the core drives (peripherals through the interrupt matrix):
        /// pending while held, whatever INTCLEAR does, as on the chip; they drop when the peripheral lets go.
        /// </summary>
        public uint ExternalLevels { get; set; }

        /// <summary>Takes the highest pending, enabled interrupt the current level allows; true when one was taken.</summary>
        private bool TakeInterrupt()
        {
            uint pending = (Interrupt | ExternalLevels) & IntEnable;
            if (pending == 0) return false;
            int best = 0, bestLevel = 0;
            for (int i = 0; i < 32; i++)
            {
                if ((pending & (1u << i)) == 0) continue;
                int level = InterruptLevel[i];
                if (level > bestLevel)
                {
                    bestLevel = level;
                    best = i;
                }
            }

            if (bestLevel <= IntLevel || (bestLevel <= ExcmLevel && Excm)) return false;
            Waiting = false;
            if (bestLevel == 1)
            {
                // Level 1 goes through the general exception vectors.
                LastException = XtensaCause.Level1Interrupt;
                ExcCause = XtensaCause.Level1Interrupt;
                Epc[1] = Pc;
                Pc = Vecbase + (UserMode ? UserExceptionVector : KernelExceptionVector);
                SetExcm(true);
                return true;
            }

            Epc[bestLevel] = Pc;
            Eps[bestLevel] = Ps;
            SetIntLevel(bestLevel);
            SetExcm(true);
            Pc = Vecbase + (bestLevel == 7 ? NmiVector : Level2Vector + (uint)(bestLevel - 2) * 0x40);
            LastException = 100 + best;
            return true;
        }

        /// <summary>Raises interrupt <paramref name="number"/> (a peripheral through the interrupt matrix, or a level line).</summary>
        public void RaiseInterrupt(int number) => Interrupt |= 1u << number;

        /// <summary>Drops a level-triggered interrupt line.</summary>
        public void ClearInterrupt(int number) => Interrupt &= ~(1u << number);

        // ================================================================ running

        private uint _nextPc;
        private bool _branched, _excepted;

        /// <summary>Runs <paramref name="cycles"/> clock cycles (or a little more: an instruction is not cut).</summary>
        public void Run(long cycles)
        {
            long end = (long)Cycles + cycles;
            while ((long)Cycles < end && !AtBreak)
            {
                if (Waiting)
                {
                    // WAITI: nothing runs until an interrupt; time jumps to the next CCOMPARE match or the slice's end.
                    if (TakeInterrupt()) continue;
                    long until = end;
                    for (int i = 0; i < 3; i++)
                    {
                        if ((IntEnable & (1u << TimerInterrupt[i])) == 0) continue;
                        uint left = Ccompare[i] - Ccount;
                        if (left != 0 && left < until - (long)Cycles) until = (long)Cycles + left;
                    }

                    Tick((int)Math.Max(1, until - (long)Cycles));
                    if (!TakeInterrupt()) continue;
                }

                Tick(Step());
            }
        }

        /// <summary>Advances the clock: CCOUNT, and the CCOMPARE timers' interrupts as CCOUNT passes them.</summary>
        private void Tick(int cycles)
        {
            uint before = Ccount;
            Ccount += (uint)cycles;
            Cycles += (ulong)cycles;
            for (int i = 0; i < 3; i++)
            {
                // Passed when the compare value lies in (before, after], counted modulo 2^32.
                if (Ccompare[i] - before - 1 < (uint)cycles) RaiseInterrupt(TimerInterrupt[i]);
            }
        }

        /// <summary>
        /// [esp32emu] Code the chip serves itself instead of running instructions (the mask ROM's functions, high-level
        /// emulated): asked for every PC in [<see cref="TrapLow"/>, <see cref="TrapHigh"/>); true when it handled the call
        /// (it set the result and the next PC), false to run the instruction there.
        /// </summary>
        public Func<XtensaCore, bool>? Trap { get; set; }

        public uint TrapLow { get; set; }

        /// <summary>The cycles the trap just served took (10 unless it says otherwise: a busy-wait spends its time here).</summary>
        public int TrapCycles { get; set; } = 10;
        public uint TrapHigh { get; set; }

        /// <summary>
        /// [esp32emu] Returns from a function the core entered with CALLn (or CALL0) and a trap served: its result in the
        /// callee's a2 (and a3 for 64-bit), execution back after the call. No window was rotated, so nothing is restored.
        /// </summary>
        public void ReturnFromCall(uint result, uint? high = null)
        {
            int n = CallInc;
            uint ra = A(n * 4);
            SetA(n * 4 + 2, result);
            if (high.HasValue) SetA(n * 4 + 3, high.Value);
            Pc = n == 0 ? ra : (Pc & 0xC0000000) | (ra & 0x3FFFFFFF);
        }

        /// <summary>Argument <paramref name="index"/> of a function the core just called (a2, a3 … of the callee's window).</summary>
        public uint Argument(int index) => A(CallInc * 4 + 2 + index);

        /// <summary>Runs one instruction (or takes an interrupt); its cycles.</summary>
        public int Step()
        {
            if (TakeInterrupt()) return 1;
            if (Waiting) return 1;
            if (Trap != null && Pc >= TrapLow && Pc < TrapHigh && Trap(this))
            {
                int spent = TrapCycles;
                TrapCycles = 10;
                return spent;
            }

            uint pc = Pc;
            uint b0 = _bus.Read8(pc);
            int op0 = (int)(b0 & 0xF);
            int length = op0 >= 8 ? 2 : 3;
            uint insn = length == 2 ? b0 | (uint)_bus.Read8(pc + 1) << 8 : b0 | (uint)_bus.Read8(pc + 1) << 8 | (uint)_bus.Read8(pc + 2) << 16;
            _nextPc = pc + (uint)length;
            _branched = _excepted = false;
            int cycles = length == 2 ? ExecuteNarrow(insn) : ExecuteWide(insn);
            // Zero-overhead loop: falling through the instruction that ends at LEND goes back to LBEG while LCOUNT lasts.
            if (!_branched && !_excepted && _nextPc == Lend && Lcount != 0 && !Excm)
            {
                Lcount--;
                _nextPc = Lbeg;
            }

            if (_branched || _excepted)
            {
                // The last jumps (from, to), for bring-up reports: where control went before something went wrong.
                _jumps[_jumpIndex++ & (_jumps.Length - 1)] = (pc, _nextPc);
                if (JumpTargets != null)
                {
                    ulong edge = (ulong)pc << 32 | _nextPc;
                    JumpTargets.TryGetValue(edge, out long n);
                    JumpTargets[edge] = n + 1;
                }
            }

            Pc = _nextPc;
            return cycles;
        }

        /// <summary>
        /// [esp32emu] When set, every jump is counted by (from &lt;&lt; 32 | to): calls land on function entries, so it is a
        /// call graph of what the firmware runs, for bring-up and for comparing the two ESP32 modes. Off (null) by
        /// default; it costs time.
        /// </summary>
        public System.Collections.Generic.Dictionary<ulong, long>? JumpTargets { get; set; }

        private readonly (uint From, uint To)[] _jumps = new (uint, uint)[64];
        private int _jumpIndex;

        /// <summary>The last 64 jumps, calls, returns and exceptions (from, to), oldest first.</summary>
        public (uint From, uint To)[] RecentJumps()
        {
            var list = new System.Collections.Generic.List<(uint, uint)>();
            for (int i = 0; i < _jumps.Length; i++)
            {
                var j = _jumps[(_jumpIndex + i) & (_jumps.Length - 1)];
                if (j.From != 0 || j.To != 0) list.Add(j);
            }

            return list.ToArray();
        }

        private void Branch(uint target)
        {
            _nextPc = target;
            _branched = true;
        }

        /// <summary>Where the last instruction the core does not know was (for bring-up reports).</summary>
        public uint LastIllegalPc { get; private set; }

        /// <summary>Stop at the first instruction the core does not know, instead of raising the exception (bring-up).</summary>
        public bool BreakOnIllegal { get; set; }

        private void Illegal()
        {
            IllegalInstructions++;
            LastIllegalPc = Pc;
            if (BreakOnIllegal)
            {
                AtBreak = true;
                _nextPc = Pc;
                _excepted = true;
                return;
            }

            Exception(XtensaCause.IllegalInstruction);
        }
    }
}
