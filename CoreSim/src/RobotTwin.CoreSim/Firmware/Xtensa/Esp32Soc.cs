using System;
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
        public const uint RomBase = 0x40000000, RomEnd = 0x40060000;      // mask ROM (served by traps)
        public const uint PeripheralBase = 0x3FF00000, PeripheralEnd = 0x3FF80000;
        public const uint AhbBase = 0x60000000, AhbEnd = 0x60080000;      // the same peripherals over the AHB

        private readonly byte[] _dram = new byte[DramSize];
        private readonly byte[] _iram = new byte[IramSize];
        private readonly byte[] _rtcFast = new byte[RtcFastSize];
        private readonly byte[] _rtcSlow = new byte[RtcSlowSize];
        private readonly byte[] _irom = new byte[IromEnd - 0x400C0000];
        private readonly byte[] _drom = new byte[DromEnd - DromBase];
        private readonly Dictionary<uint, uint> _registers = new Dictionary<uint, uint>();
        private readonly Dictionary<uint, Func<XtensaCore, bool>> _romFunctions = new Dictionary<uint, Func<XtensaCore, bool>>();
        private readonly Dictionary<uint, string> _romNames = new Dictionary<uint, string>();

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
        }

        /// <summary>The PRO_CPU (core 0): it boots.</summary>
        public XtensaCore Pro { get; }

        /// <summary>The APP_CPU (core 1): held in reset until the PRO_CPU releases it.</summary>
        public XtensaCore App { get; }

        /// <summary>Whether the APP_CPU runs (DPORT releases it).</summary>
        public bool AppRunning { get; private set; }

        /// <summary>What the firmware wrote to UART0 (its boot log and Serial).</summary>
        public StringBuilder Uart0 { get; } = new StringBuilder();

        /// <summary>Why the chip stopped, when it did (a ROM function nobody serves, a bus error …); null while it runs.</summary>
        public string? Stopped { get; private set; }

        /// <summary>Peripheral registers read or written that nothing models yet: address → (reads, writes, last value).</summary>
        public Dictionary<uint, (int Reads, int Writes, uint Last)> Unmodelled { get; } = new Dictionary<uint, (int, int, uint)>();

        // ================================================================ loading

        /// <summary>Loads an application ELF as the bootloader would: its segments into RAM and flash, the PRO_CPU at its entry.</summary>
        public void Load(byte[] elfBytes)
        {
            var elf = ElfImage.Read(elfBytes);
            foreach (var (address, bytes, memorySize) in elf.Segments)
            {
                for (uint i = 0; i < memorySize; i++) Poke(address + i, i < bytes.Length ? bytes[i] : (byte)0);
            }

            // The mask ROM's functions, by the names the ELF gives their addresses.
            foreach (var symbol in elf.Symbols)
            {
                if (symbol.Value >= RomBase && symbol.Value < RomEnd && !_romNames.ContainsKey(symbol.Value)) _romNames[symbol.Value] = symbol.Name;
            }

            BindRom();
            // As the second-stage bootloader leaves it when it calls the application's entry (call_start_cpu0, which
            // begins with ENTRY): windows on, user vector mode, CALLINC of a CALLX4, interrupts at level 0, and its
            // stack near the top of the ROM's reserved DRAM, the caller pointer below it pointing at itself.
            Pro.Reset(elf.Entry);
            Pro.Ps = 0x00040020 | (1u << 16);
            Pro.SetA(1, BootStack);
            Write32(BootStack - 12, BootStack);
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
            return (null, 0);
        }

        private bool IsCachedFlash(uint a) => _flash != null && ((a >= 0x400C2000 && a < IromEnd) || (a >= DromBase && a < DromEnd));

        private static bool IsPeripheral(uint a, out uint register)
        {
            if (a >= PeripheralBase && a < PeripheralEnd)
            {
                register = a;
                return true;
            }

            if (a >= AhbBase && a < AhbEnd)
            {
                register = a - AhbBase + PeripheralBase;
                return true;
            }

            register = 0;
            return false;
        }

        public byte Read8(uint a)
        {
            var (m, o) = Resolve(a);
            if (m != null) return m[o];
            return (byte)(Read32(a & ~3u) >> (int)((a & 3) * 8));
        }

        public ushort Read16(uint a)
        {
            var (m, o) = Resolve(a);
            if (m != null) return (ushort)(m[o] | m[o + 1] << 8);
            return (ushort)(Read32(a & ~3u) >> (int)((a & 2) * 8));
        }

        public uint Read32(uint a)
        {
            var (m, o) = Resolve(a);
            if (m != null) return (uint)(m[o] | m[o + 1] << 8 | m[o + 2] << 16 | m[o + 3] << 24);
            if (IsPeripheral(a, out uint register)) return ReadRegister(register);
            return 0;   // ROM data, external RAM, holes: zero
        }

        public void Write8(uint a, byte value)
        {
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
            if (!WritePeripheral(register, merged)) Note(register, false, merged);
            _registers[register] = merged;
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
            Unmodelled[register] = read ? (n.Reads + 1, n.Writes, value) : (n.Reads, n.Writes + 1, value);
        }

        // ================================================================ running

        /// <summary>Runs both cores for <paramref name="cycles"/> clock cycles (the APP_CPU once released), in small turns.</summary>
        public void Run(long cycles, int turn = 1000)
        {
            long done = 0;
            while (done < cycles && Stopped == null && !Pro.AtBreak)
            {
                int step = (int)Math.Min(turn, cycles - done);
                try
                {
                    Pro.Run(step);
                    if (AppRunning) App.Run(step);
                }
                catch (InvalidOperationException ex)
                {
                    Stopped = ex.Message;
                }

                done += step;
            }
        }

        // ================================================================ the mask ROM, served

        private bool ServeRom(XtensaCore core)
        {
            if (_romFunctions.TryGetValue(core.Pc, out var serve) && serve(core)) return true;
            _romNames.TryGetValue(core.Pc, out var name);
            Stopped = $"ROM işlevi karşılanmadı: {name ?? "?"} @0x{core.Pc:X8} (çekirdek {core.CoreId})";
            core.AtBreak = true;
            return true;
        }

        /// <summary>Serves the ROM function called <paramref name="name"/> (when the firmware links it) with <paramref name="body"/>.</summary>
        private void Rom(string name, Func<XtensaCore, bool> body)
        {
            foreach (var pair in _romNames)
                if (pair.Value == name)
                    _romFunctions[pair.Key] = body;
        }
    }
}
