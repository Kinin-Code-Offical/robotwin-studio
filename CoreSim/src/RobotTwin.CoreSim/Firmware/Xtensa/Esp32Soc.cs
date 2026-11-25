using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using RobotTwin.CoreSim.Firmware.Debug;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The ESP32 chip around our Xtensa cores (docs/gelistirici/ESP32_MODLARI.md, stage 3): its memory map
    /// (DRAM, IRAM, RTC fast and slow memory, the flash seen through the caches as IROM and DROM), the peripheral
    /// registers (also through their AHB aliases), the firmware loaded straight from the application's ELF (the
    /// second-stage bootloader's work), and the mask ROM's functions served in C# (high-level emulation) at the
    /// addresses the ELF's own symbol table gives them. Every peripheral register nobody models yet is remembered, so
    /// bringing up a new firmware shows what it waits on.
    /// </summary>
    public sealed partial class Esp32Soc : IXtensaBus
    {
        public const uint DramBase = 0x3FFAE000, DramSize = 0x52000;     // 328 KB data RAM
        public const uint IramBase = 0x40070000, IramSize = 0x30000;     // 192 KB instruction RAM (its first 64 KB is cache)
        public const uint RtcFastBase = 0x3FF80000, RtcFastIBase = 0x400C0000, RtcFastSize = 0x2000;
        public const uint RtcSlowBase = 0x50000000, RtcSlowSize = 0x2000;
        public const uint IromBase = 0x400C2000, IromEnd = 0x40C00000;    // flash, instruction bus
        public const uint DromBase = 0x3F400000, DromEnd = 0x3F800000;    // flash, data bus
        public const uint RomBase = 0x40000000, RomEnd = 0x40070000;      // mask ROM, 448 KB (served by traps)
        public const uint RomDataBase = 0x3FF90000, RomDataEnd = 0x3FFA0000; // the mask ROM's data (newlib's tables …), read-only
        public const uint PeripheralBase = 0x3FF00000, PeripheralEnd = 0x3FF80000;
        public const uint AhbBase = 0x60000000, AhbEnd = 0x60040000;      // the APB peripherals from 0x3FF40000 (UARTs …) over the AHB
        private const uint AhbPeripherals = 0x3FF40000;

        private readonly byte[] _dram = new byte[DramSize];
        private readonly byte[] _iram = new byte[IramSize];
        private readonly byte[] _rtcFast = new byte[RtcFastSize];
        private readonly byte[] _rtcSlow = new byte[RtcSlowSize];
        private readonly byte[] _irom = new byte[IromEnd - 0x400C0000];
        private readonly byte[] _drom = new byte[DromEnd - DromBase];
        private readonly byte[] _romData = new byte[RomDataEnd - RomDataBase];
        private readonly Dictionary<uint, uint> _registers = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, Func<XtensaCore, bool>> _romFunctions = new Dictionary<uint, Func<XtensaCore, bool>>();
        private readonly Dictionary<uint, string> _romNames = new Dictionary<uint, string>();
        private readonly Dictionary<string, uint> _romAddresses = new Dictionary<string, uint>();

        public Esp32Soc()
        {
            Pro = new XtensaCore(this, 0);
            App = new XtensaCore(this, 1);
            foreach (var core in new[] { Pro, App })
            {
                core.Trap = ServeRom;
                core.TrapLow = RomBase;
                core.TrapHigh = RomEnd;
            }

            BurnEfuse();
            // The UARTs' reset values, which drivers keep where they pass "unchanged": CONF0 (APB clock, 8N1), CONF1 (both
            // FIFO thresholds 0x60, RX timeout 10), CLKDIV 694 (115200 baud at 80 MHz).
            foreach (uint uart in UartBase)
            {
                _registers[uart + 0x20] = 0x0800001C;
                _registers[uart + 0x24] = 0x0A006060;
                _registers[uart + 0x14] = 0x000002B6;
            }

            // [motor] The MCPWM's: each timer's period 255 (TIMERn_CFG0), each operator's dead-time module bypassed on both
            // outputs (DTn_CFG's A/B_OUTBYPASS).
            foreach (uint mcpwm in McpwmBase)
            {
                for (uint t = 0; t < 3; t++) _registers[mcpwm + 0x04 + 0x10 * t] = 255u << 8;
                for (uint op = 0; op < 3; op++) _registers[mcpwm + 0x3C + 0x38 * op + 0x1C] = 3u << 15;
            }
        }

        /// <summary>The PRO_CPU (core 0): it boots.</summary>
        public XtensaCore Pro { get; }

        /// <summary>The APP_CPU (core 1): held in reset until the PRO_CPU releases it.</summary>
        public XtensaCore App { get; }

        /// <summary>Whether the APP_CPU runs (DPORT releases it).</summary>
        public bool AppRunning { get; private set; }

        /// <summary>What the firmware wrote to UART0 (its boot log and Serial).</summary>
        public StringBuilder Uart0 { get; } = new StringBuilder();


        /// <summary>
        /// The reset the ROM reports for this start (its RESET_REASON: 1 power-on, 5 deep-sleep wake, 12 software CPU reset,
        /// 15 brown-out …); the board sets it before loading.
        /// </summary>
        public int RomResetReason { get; set; } = 1;

        /// <summary>
        /// The chip has to start again and has stopped for it: the firmware asked (esp_restart: the ROM's software reset or
        /// RTC_CNTL SW_SYS_RST), or a watchdog ran out.
        /// </summary>
        public bool RestartRequested { get; private set; }

        /// <summary>Why it starts again, as the ROM will report it: 12 software CPU reset, 7/8 timer group 0/1 watchdog, 11 watchdog CPU reset.</summary>
        public int RestartReason { get; private set; } = 12;

        private void RequestRestart(int reason)
        {
            if (RestartRequested) return;
            RestartRequested = true;
            RestartReason = reason;
        }

        /// <summary>Why the chip stopped, when it did (a ROM function nobody serves, a bus error …); null while it runs.</summary>
        public string? Stopped { get; private set; }

        /// <summary>
        /// Peripheral registers read or written that nothing models yet: address → (reads, writes, last value, and the PC
        /// and core of the last access — which driver code wants it).
        /// </summary>
        public Dictionary<uint, (int Reads, int Writes, uint Last, uint Pc, int Core)> Unmodelled { get; } = new Dictionary<uint, (int, int, uint, uint, int)>();

        /// <summary>The core running now (the bus serves one at a time).</summary>
        private XtensaCore? _running;

        // ================================================================ loading

        /// <summary>Loads an application ELF as the bootloader would: its segments into RAM and flash, the PRO_CPU at its entry.</summary>
        public void Load(byte[] elfBytes)
        {
            var elf = ElfImage.Read(elfBytes);
            foreach (var (address, bytes, memorySize) in elf.Segments)
            {
                for (uint i = 0; i < memorySize; i++) Poke(address + i, i < bytes.Length ? bytes[i] : (byte)0);
            }

            // The mask ROM's functions, by the names the ELF gives their addresses, and the ROM's own variables it uses.
            foreach (var symbol in elf.Symbols)
            {
                if (symbol.Value >= RomBase && symbol.Value < RomEnd && !_romNames.ContainsKey(symbol.Value)) _romNames[symbol.Value] = symbol.Name;
                if (symbol.Value >= RomBase && symbol.Value < RomEnd) _romAddresses[symbol.Name] = symbol.Value;
                if (symbol.Name == "g_ticks_per_us_pro") _ticksPerUsPro = symbol.Value;
                if (symbol.Name == "g_ticks_per_us_app") _ticksPerUsApp = symbol.Value;
            }

            BindRom();
            FillRomData(elf);
            BindAppFunctions(elf);
            BindHostCalls(elf);
            BindWifi(elf);
            // As the second-stage bootloader leaves it when it calls the application's entry (call_start_cpu0, which
            // begins with ENTRY): windows on, user vector mode, CALLINC of a CALLX4, interrupts at level 0, and its
            // stack near the top of the ROM's reserved DRAM, the caller pointer below it pointing at itself.
            Pro.Reset(elf.Entry);
            Pro.Ps = 0x00040020 | (1u << 16);
            Pro.SetA(1, BootStack);
            Write32(BootStack - 12, BootStack);
            // g_rom_flashchip as the ROM and the bootloader leave it: the chip's id (C8 40 16), 4 MB, 64 KB blocks, 4 KB
            // sectors, 256-byte pages.
            WriteRomFlashChip(0xC84016, FlashSize, 0x10000, 0x1000, 0x100, 0xFFFF);
            // What the bootloader leaves in the RTC store registers: the 40 MHz crystal and the 80 MHz APB clock (each
            // value doubled in both halves, as rtc_clk checks), and the slow clock's calibration (150 kHz RC, Q13.19 µs).
            _registers[0x3FF480B0] = 0x00280028;
            _registers[0x3FF480B4] = 0x4C4B4C4B;
            _registers[0x3FF48050] = 0x00355555;
            // The bootloader's clock: the CPU on the PLL at 80 MHz (SOC_CLK_SEL 1, CPUPERIOD_SEL 0), told to the ROM.
            _registers[RtcClkConf] = 1u << 27;
            _registers[CpuPerConf] = 0;
            UpdateClocks();
            if (_ticksPerUsPro != 0) Write32(_ticksPerUsPro, 80);
            if (_ticksPerUsApp != 0) Write32(_ticksPerUsApp, 80);
        }

        /// <summary>The stack the bootloader runs on, which the application keeps until FreeRTOS starts.</summary>
        public const uint BootStack = 0x3FFE3F20;

        private void Poke(uint address, byte value)
        {
            var (memory, offset) = Resolve(address);
            if (memory != null) memory[offset] = value;
        }

        // ================================================================ the bus

        private (byte[]? Memory, uint Offset) Resolve(uint a)
        {
            if (a >= DramBase && a < DramBase + DramSize) return (_dram, a - DramBase);
            if (a >= IramBase && a < IramBase + IramSize) return (_iram, a - IramBase);
            if (a >= 0x400C2000 && a < IromEnd) return _flash != null ? ThroughCache(a) : (_irom, a - 0x400C0000);
            if (a >= DromBase && a < DromEnd) return _flash != null ? ThroughCache(a) : (_drom, a - DromBase);
            if (a >= RtcFastBase && a < RtcFastBase + RtcFastSize) return (_rtcFast, a - RtcFastBase);
            if (a >= RtcFastIBase && a < RtcFastIBase + RtcFastSize) return (_rtcFast, a - RtcFastIBase);
            if (a >= RtcSlowBase && a < RtcSlowBase + RtcSlowSize) return (_rtcSlow, a - RtcSlowBase);
            if (a >= RomDataBase && a < RomDataEnd) return (_romData, a - RomDataBase);
            return (null, 0);
        }

        /// <summary>Memory the firmware can read but not write: the flash through the cache, the mask ROM's data.</summary>
        private bool IsCachedFlash(uint a) => (_flash != null && ((a >= 0x400C2000 && a < IromEnd) || (a >= DromBase && a < DromEnd)))
                                              || (a >= RomDataBase && a < RomDataEnd);

        /// <summary>
        /// The mask ROM's data the firmware reads, put where the ELF's ROM symbols say: newlib's C-locale character classes
        /// (<c>_ctype_</c>, one flag byte per character after a leading 0, and <c>__ctype_ptr__</c> pointing at it, so
        /// isdigit/toupper/… work), <c>__mb_cur_max</c> (1: single-byte characters), and the bit-count tables.
        /// </summary>
        private void FillRomData(ElfImage elf)
        {
            uint ctype = 0;
            foreach (var symbol in elf.Symbols)
            {
                uint at = symbol.Value;
                if (at < RomDataBase || at >= RomDataEnd) continue;
                switch (symbol.Name)
                {
                    case "_ctype_":
                        ctype = at;
                        if (at + 257 > RomDataEnd) break;
                        _romData[at - RomDataBase] = 0;
                        for (int c = 0; c < 256; c++) _romData[at - RomDataBase + 1 + c] = CtypeFlags(c);
                        break;
                    case "__mb_cur_max":
                        _romData[at - RomDataBase] = 1;
                        break;
                    case "__popcount_tab":
                        for (int i = 0; i < 256 && at + i < RomDataEnd; i++) _romData[at - RomDataBase + i] = BitCount(i);
                        break;
                }
            }

            foreach (var symbol in elf.Symbols)
            {
                if (symbol.Name != "__ctype_ptr__" || ctype == 0 || symbol.Value < RomDataBase || symbol.Value + 4 > RomDataEnd) continue;
                uint at = symbol.Value - RomDataBase;
                for (int b = 0; b < 4; b++) _romData[at + b] = (byte)(ctype >> (8 * b));
            }
        }

        private static byte BitCount(int v)
        {
            byte n = 0;
            for (; v != 0; v &= v - 1) n++;
            return n;
        }

        /// <summary>
        /// The firmware's own functions the core serves rather than runs: libgcc's single-precision division (__divsf3) is
        /// the FPU's divide-step sequence (DIV0.S, NEXP01.S, MADDN.S, MKDADJ.S, ADDEXP.S, DIVN.S …), whose instructions the
        /// core does not model one by one; it gives the correctly rounded IEEE quotient, and so does this, in the ~30 cycles
        /// the sequence takes.
        /// </summary>
        private void BindAppFunctions(ElfImage elf)
        {
            foreach (var symbol in elf.Symbols)
            {
                if (symbol.Name != "__divsf3" || symbol.Value < IramBase || symbol.Value >= IromEnd) continue;
                foreach (var core in new[] { Pro, App })
                {
                    core.AddAppTrap(symbol.Value, c =>
                    {
                        float a = BitConverter.Int32BitsToSingle((int)c.Argument(0)), b = BitConverter.Int32BitsToSingle((int)c.Argument(1));
                        c.ReturnFromCall((uint)BitConverter.SingleToInt32Bits(a / b));
                        c.TrapCycles = 30;
                        return true;
                    });
                }
            }
        }

        /// <summary>newlib's class flags for one character in the C locale: _U 1, _L 2, _N 4, _S 8, _P 16, _C 32, _X 64, _B 128.</summary>
        private static byte CtypeFlags(int c)
        {
            const int U = 1, L = 2, N = 4, S = 8, P = 16, C = 32, X = 64, B = 128;
            if (c < 0x09) return C;
            if (c <= 0x0D) return C | S;
            if (c < 0x20) return C;
            if (c == 0x20) return S | B;
            if (c <= 0x2F) return P;
            if (c <= 0x39) return N;
            if (c <= 0x40) return P;
            if (c <= 0x46) return U | X;
            if (c <= 0x5A) return U;
            if (c <= 0x60) return P;
            if (c <= 0x66) return L | X;
            if (c <= 0x7A) return L;
            if (c <= 0x7E) return P;
            if (c == 0x7F) return C;
            return 0;
        }

        private static bool IsPeripheral(uint a, out uint register)
        {
            if (a >= PeripheralBase && a < PeripheralEnd)
            {
                register = a;
                return true;
            }

            if (a >= AhbBase && a < AhbEnd)
            {
                register = a - AhbBase + AhbPeripherals;
                return true;
            }

            register = 0;
            return false;
        }

        // The data RAM (the stacks, the heap, .data/.bss) takes most loads and stores: it is tried before anything else.

        public byte Read8(uint a)
        {
            uint d = a - DramBase;
            if (d < DramSize) return _dram[d];
            var (m, o) = Resolve(a);
            if (m != null) return m[o];
            return (byte)(Read32(a & ~3u) >> (int)((a & 3) * 8));
        }

        public ushort Read16(uint a)
        {
            uint d = a - DramBase;
            if (d < DramSize - 1) return BinaryPrimitives.ReadUInt16LittleEndian(_dram.AsSpan((int)d, 2));
            var (m, o) = Resolve(a);
            if (m != null) return (ushort)(m[o] | m[o + 1] << 8);
            return (ushort)(Read32(a & ~3u) >> (int)((a & 2) * 8));
        }

        public uint Read32(uint a)
        {
            uint d = a - DramBase;
            if (d < DramSize - 3) return BinaryPrimitives.ReadUInt32LittleEndian(_dram.AsSpan((int)d, 4));
            var (m, o) = Resolve(a);
            if (m != null) return (uint)(m[o] | m[o + 1] << 8 | m[o + 2] << 16 | m[o + 3] << 24);
            if (IsPeripheral(a, out uint register)) return ReadRegister(register);
            return 0;   // ROM data, external RAM, holes: zero
        }

        public void Write8(uint a, byte value)
        {
            uint d = a - DramBase;
            if (d < DramSize)
            {
                _dram[d] = value;
                return;
            }

            if (IsCachedFlash(a)) return;   // the flash is read-only through the cache
            var (m, o) = Resolve(a);
            if (m != null)
            {
                m[o] = value;
                return;
            }

            if (IsPeripheral(a, out uint register)) WriteRegister(register & ~3u, value, (int)(a & 3) * 8, 0xFFu);
        }

        public void Write16(uint a, ushort value)
        {
            uint d = a - DramBase;
            if (d < DramSize - 1)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(_dram.AsSpan((int)d, 2), value);
                return;
            }

            if (IsCachedFlash(a)) return;
            var (m, o) = Resolve(a);
            if (m != null)
            {
                m[o] = (byte)value;
                m[o + 1] = (byte)(value >> 8);
                return;
            }

            if (IsPeripheral(a, out uint register)) WriteRegister(register & ~3u, value, (int)(a & 2) * 8, 0xFFFFu);
        }

        public void Write32(uint a, uint value)
        {
            uint d = a - DramBase;
            if (d < DramSize - 3)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(_dram.AsSpan((int)d, 4), value);
                return;
            }

            if (IsCachedFlash(a)) return;
            var (m, o) = Resolve(a);
            if (m != null)
            {
                m[o] = (byte)value;
                m[o + 1] = (byte)(value >> 8);
                m[o + 2] = (byte)(value >> 16);
                m[o + 3] = (byte)(value >> 24);
                return;
            }

            if (IsPeripheral(a, out uint register)) WriteRegister(register, value, 0, 0xFFFFFFFFu);
        }

        private void WriteRegister(uint register, uint value, int shift, uint mask)
        {
            _registers.TryGetValue(register, out uint old);
            uint merged = (old & ~(mask << shift)) | ((value & mask) << shift);
            PadGeneration++;
            if (!WritePeripheral(register, merged)) Note(register, false, merged);
            _registers[register] = merged & ~SelfClearing(register);
            if (register == RtcClkConf || register == CpuPerConf || register == SysClkConf) UpdateClocks();
        }

        /// <summary>
        /// Bits the hardware clears by itself once it has acted on them, so they never read back (and a later
        /// read-modify-write of the register does not start the action again): SPI2/3 CMD.USR, I2C0/1 CTR.TRANS_START,
        /// RMT CHnCONF1.TX_START and its memory resets.
        /// </summary>
        private static uint SelfClearing(uint register)
        {
            switch (register)
            {
                case 0x3FF64000:
                case 0x3FF65000:
                    return 1u << 18;
                case 0x3FF53004:
                case 0x3FF67004:
                    return 1u << 5;
                default:
                    // RMT CHnCONF1: TX_START and the memory pointer resets (MEM_WR_RST, MEM_RD_RST, APB_MEM_RST). Kept, a
                    // driver's later read-modify-write of the register (its idle level, a pointer reset) started the
                    // channel again, which then read the last frame's end marker and ended at once.
                    if (register >= 0x3FF56024 && register <= 0x3FF5605C && (register & 7) == 4) return 1u | 1u << 2 | 1u << 3 | 1u << 4;
                    return 0;
            }
        }

        private uint ReadRegister(uint register)
        {
            if (ReadPeripheral(register, out uint value)) return value;
            _registers.TryGetValue(register, out value);
            Note(register, true, value);
            return value;
        }

        private void Note(uint register, bool read, uint value)
        {
            Unmodelled.TryGetValue(register, out var n);
            uint pc = _running?.Pc ?? 0;
            int core = _running?.CoreId ?? -1;
            Unmodelled[register] = read ? (n.Reads + 1, n.Writes, value, pc, core) : (n.Reads, n.Writes + 1, value, pc, core);
        }

        // ================================================================ running

        /// <summary>Runs both cores for <paramref name="cycles"/> clock cycles (the APP_CPU once released), in small turns.</summary>
        public void Run(long cycles, int turn = 1000)
        {
            long done = 0;
            while (done < cycles && Stopped == null && !Pro.AtBreak && !RestartRequested && !DeepSleepRequested)
            {
                // While either core is in, or about to take, a high-priority interrupt (level 4 and up), the cores take turns
                // of a few cycles: such handlers hand over through flags (the interrupt watchdog's livelock handshake …) whose
                // windows are shorter than a normal turn, and on the chip both cores run truly at once.
                // In light sleep neither core runs: time goes on to the wake-up in one turn (or to the end of the run).
                long quantum = _lightSleep ? Math.Min(CyclesToWake(), int.MaxValue) : AppRunning && (HighPriority(Pro) || HighPriority(App)) ? 16 : turn;
                if (!_lightSleep && quantum == turn && Idle(Pro, 0) && (!AppRunning || Idle(App, 1)))
                {
                    // Both cores asleep in WAITI with nothing pending: a longer turn (up to 25 µs, never past a core's next
                    // CCOMPARE, the FreeRTOS tick) keeps them asleep throughout; the peripherals catch up by time at its end.
                    long idle = Math.Min((long)(25e-6 * CpuHz), CyclesToCompare(Pro));
                    if (AppRunning) idle = Math.Min(idle, CyclesToCompare(App));
                    if (idle > turn) quantum = idle;
                }

                int step = (int)Math.Min(quantum, cycles - done);
                try
                {
                    BeginTurn();
                    _running = Pro;
                    if (!Stalled(0) && !_lightSleep) Pro.Run(step);
                    _running = App;
                    if (AppRunning && !Stalled(1) && !_lightSleep) App.Run(step);
                    _running = null;
                    EndTurn(step);
                }
                catch (InvalidOperationException ex)
                {
                    _running = null;
                    Stopped = ex.Message;
                }

                done += step;
            }
        }

        /// <summary>
        /// A core the other has stalled (esp_cpu_stall, as a panic does before it reports): RTC_CNTL_SW_CPU_STALL's
        /// SW_STALL_PROCPU/APPCPU_C1 at 0x21 with RTC_CNTL_OPTIONS0's SW_STALL_PROCPU/APPCPU_C0 at 2. It does not run until
        /// released.
        /// </summary>
        public bool Stalled(int cpu)
        {
            uint stall = Reg(0x3FF480AC), options = Reg(0x3FF48000);
            return cpu == 0
                ? ((stall >> 26) & 0x3F) == 0x21 && ((options >> 2) & 3) == 2
                : ((stall >> 20) & 0x3F) == 0x21 && (options & 3) == 2;
        }

        /// <summary>
        /// Moves on whenever what the pads drive may have changed (a peripheral register written, an LEDC update or fade
        /// under way): the board recomputes its pads only then, not every slice.
        /// </summary>
        public long PadGeneration { get; private set; }

        /// <summary>A core asleep in WAITI (or stalled) with no enabled interrupt pending.</summary>
        private bool Idle(XtensaCore c, int cpu) =>
            Stalled(cpu) || (c.Waiting && ((c.Interrupt | c.ExternalLevels) & c.IntEnable) == 0);

        /// <summary>Cycles until the core's next enabled CCOMPARE match (its tick), or a large number when none is armed.</summary>
        private static long CyclesToCompare(XtensaCore c)
        {
            long best = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if ((c.IntEnable & (1u << XtensaCore.TimerInterrupt[i])) == 0) continue;
                uint left = c.Ccompare[i] - c.Ccount;
                if (left != 0 && left < best) best = left;
            }

            return best;
        }

        /// <summary>The CPU interrupts of level 4 and up (the high-priority ones: the interrupt watchdog, cache errors, IPC …).</summary>
        private static readonly uint HighLevelInterrupts = HighLevelMask();

        private static uint HighLevelMask()
        {
            uint mask = 0;
            for (int i = 0; i < 32; i++)
                if (XtensaCore.InterruptLevel[i] >= 4) mask |= 1u << i;
            return mask;
        }

        /// <summary>A core in a high-priority handler, or with one pending and enabled.</summary>
        private static bool HighPriority(XtensaCore c) =>
            c.IntLevel >= 4 || ((c.Interrupt | c.ExternalLevels) & c.IntEnable & HighLevelInterrupts) != 0;

        // ================================================================ the mask ROM, served

        /// <summary>The stack the ROM gives the APP_CPU when it jumps to its boot address.</summary>
        public const uint AppBootStack = 0x3FFE7F20;

        private bool ServeRom(XtensaCore core)
        {
            if (core.Pc == HostCallReturn && FinishHostCall(core)) return true;
            if (core == App && core.Pc == 0x40000400)
            {
                // The ROM's APP_CPU start: it waits until the PRO_CPU set the boot address, then calls it as the
                // bootloader calls the application (windows on, user vectors, CALLINC of a CALLX4, its own stack).
                _registers.TryGetValue(0x3FF00038, out uint boot);
                if (boot == 0) return true;
                core.Pc = boot;
                core.Ps = 0x00040020 | (1u << 16);
                core.SetA(1, AppBootStack);
                Write32(AppBootStack - 12, AppBootStack);
                return true;
            }

            if (_romFunctions.TryGetValue(core.Pc, out var serve))
            {
                PadGeneration++;   // the ROM's GPIO helpers set pads without the bus
                if (serve(core)) return true;
            }
            _romNames.TryGetValue(core.Pc, out var name);
            Stopped = $"ROM işlevi karşılanmadı: {name ?? "?"} @0x{core.Pc:X8} (çekirdek {core.CoreId})";
            core.AtBreak = true;
            return true;
        }

        /// <summary>The ROM's flash description (esp_rom_spiflash_chip_t g_rom_flashchip, in the ROM's reserved DRAM).</summary>
        private const uint RomFlashChip = 0x3FFAE270;

        private void WriteRomFlashChip(uint deviceId, uint chipSize, uint blockSize, uint sectorSize, uint pageSize, uint statusMask)
        {
            Write32(RomFlashChip, deviceId);
            Write32(RomFlashChip + 4, chipSize);
            Write32(RomFlashChip + 8, blockSize);
            Write32(RomFlashChip + 12, sectorSize);
            Write32(RomFlashChip + 16, pageSize);
            Write32(RomFlashChip + 20, statusMask);
        }

        /// <summary>
        /// Serves the ROM function called <paramref name="name"/> (when the firmware links it) with <paramref name="body"/>,
        /// whichever of the ROM's names for that address the ELF lists first (intr_matrix_set is also
        /// esp_rom_route_intr_matrix, uart_tx_one_char esp_rom_uart_tx_one_char …).
        /// </summary>
        private void Rom(string name, Func<XtensaCore, bool> body)
        {
            if (_romAddresses.TryGetValue(name, out uint at)) _romFunctions[at] = body;
        }
    }
}
