namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class Esp32Soc
    {
        // UART0
        private const uint Uart0Fifo = 0x3FF40000, Uart0Status = 0x3FF4001C;

        // DPORT: the APP_CPU's reset, clock and boot address
        private const uint AppCpuCtrlA = 0x3FF0002C, AppCpuCtrlB = 0x3FF00030, AppCpuCtrlD = 0x3FF00038;

        /// <summary>The interrupt matrix: each core's CPU interrupt for each of the 69 peripheral sources (-1: none).</summary>
        private readonly int[,] _interruptMap = NewInterruptMap();

        private static int[,] NewInterruptMap()
        {
            var map = new int[2, 69];
            for (int cpu = 0; cpu < 2; cpu++)
                for (int s = 0; s < 69; s++)
                    map[cpu, s] = -1;
            return map;
        }

        /// <summary>
        /// CPU interrupts the matrix cannot drive (the core's own: the CCOMPARE timers 6/15/16, the software interrupts 7/29,
        /// profiling 11). IDF parks unused sources on 6 to disconnect them.
        /// </summary>
        private const uint InternalInterrupts = (1u << 6) | (1u << 7) | (1u << 11) | (1u << 15) | (1u << 16) | (1u << 29);

        /// <summary>Edge-triggered CPU interrupts: a rising source latches them until INTCLEAR; every other line is a level.</summary>
        private const uint EdgeInterrupts = (1u << 10) | (1u << 22) | (1u << 28) | (1u << 30);

        // The interrupt matrix's map registers, one word per source for each core.
        private const uint ProIntrMap = 0x3FF00104, AppIntrMap = 0x3FF00218;

        // DPORT_CPU_INTR_FROM_CPU_0..3: the four cross-core (and own-core) interrupt sources 24..27 software sets and clears.
        private const uint FromCpuIntr = 0x3FF000DC;
        private const int FromCpuSource = 24;

        /// <summary>Which of the 69 peripheral sources hold their interrupt line up now.</summary>
        private readonly bool[] _sourceLevel = new bool[69];

        private void RouteInterrupt(int cpu, int source, int cpuInterrupt)
        {
            if (cpu < 0 || cpu > 1 || source < 0 || source >= 69) return;
            _interruptMap[cpu, source] = cpuInterrupt is >= 0 and < 32 ? cpuInterrupt : -1;
            UpdateInterruptLines();
        }

        /// <summary>Raises peripheral interrupt <paramref name="source"/> once (an edge) on whichever core and CPU interrupt the matrix routes it to.</summary>
        public void RaisePeripheral(int source)
        {
            if (source < 0 || source >= 69) return;
            for (int cpu = 0; cpu < 2; cpu++)
            {
                int line = _interruptMap[cpu, source];
                if (line >= 0 && (InternalInterrupts & (1u << line)) == 0) Core(cpu).RaiseInterrupt(line);
            }
        }

        /// <summary>
        /// Holds (or lets go of) peripheral source <paramref name="source"/>'s interrupt line, as a peripheral's status does on
        /// the chip: a level CPU interrupt it routes to stays pending while any of its sources hold it.
        /// </summary>
        public void SetPeripheralLevel(int source, bool asserted)
        {
            if (source < 0 || source >= 69 || _sourceLevel[source] == asserted) return;
            _sourceLevel[source] = asserted;
            if (asserted)
            {
                for (int cpu = 0; cpu < 2; cpu++)
                {
                    int line = _interruptMap[cpu, source];
                    if (line >= 0 && (EdgeInterrupts & (1u << line)) != 0) Core(cpu).RaiseInterrupt(line);
                }
            }

            UpdateInterruptLines();
        }

        /// <summary>Whether peripheral source <paramref name="source"/> holds its line up.</summary>
        public bool PeripheralLevel(int source) => source >= 0 && source < 69 && _sourceLevel[source];

        private XtensaCore Core(int cpu) => cpu == 0 ? Pro : App;

        /// <summary>Each core's level lines: the OR of the held sources the matrix routes to them.</summary>
        private void UpdateInterruptLines()
        {
            for (int cpu = 0; cpu < 2; cpu++)
            {
                uint lines = 0;
                for (int s = 0; s < 69; s++)
                    if (_sourceLevel[s] && _interruptMap[cpu, s] >= 0) lines |= 1u << _interruptMap[cpu, s];
                Core(cpu).ExternalLevels = lines & ~InternalInterrupts & ~EdgeInterrupts;
            }
        }

        /// <summary>Peripheral reads the chip models; false leaves the register file's value (and notes the register).</summary>
        private bool ReadPeripheral(uint register, out uint value)
        {
            if (ReadFlashRegister(register, out value)) return true;
            if (ReadTimerRegister(register, out value)) return true;
            if (ReadAdcRegister(register, out value)) return true;
            if (ReadLedcRegister(register, out value)) return true;
            if (ReadEfuseRegister(register, out value)) return true;
            if (ReadGpioRegister(register, out value)) return true;
            if (register >= ProIntrMap && register < AppIntrMap + 69 * 4)
            {
                int cpu = register >= AppIntrMap ? 1 : 0;
                int source = (int)((register - (cpu == 0 ? ProIntrMap : AppIntrMap)) >> 2);
                value = _interruptMap[cpu, source] is int line and >= 0 ? (uint)line : 6;
                return true;
            }

            if (register >= FromCpuIntr && register < FromCpuIntr + 16)
            {
                value = _sourceLevel[FromCpuSource + (int)((register - FromCpuIntr) >> 2)] ? 1u : 0u;
                return true;
            }

            if (register == 0x3FF003F0 || register == 0x3FF00418)
            {
                // DPORT_PRO/APP_DCACHE_DBUG0: the cache state (bits 7..18) is 1, idle, so disabling it completes at once
                value = 1u << 7;
                return true;
            }

            if (register == 0x3FF4800C)
            {
                // RTC_CNTL_TIME_UPDATE: the latched RTC time is always valid (bit 30)
                _registers.TryGetValue(register, out value);
                value |= 1u << 30;
                return true;
            }

            if (register == 0x3FF48010 || register == 0x3FF48014)
            {
                // RTC_CNTL_TIME0/1: the RTC slow clock's 48-bit count (the 150 kHz RC oscillator), from chip time
                ulong rtc = (ulong)(Seconds * 150_000.0);
                value = register == 0x3FF48010 ? (uint)rtc : (uint)(rtc >> 32) & 0xFFFF;
                return true;
            }

            if (register == 0x3FF5F068 || register == 0x3FF60068)
            {
                // Timer group's RTC slow-clock calibration: always ready (bit 15)
                _registers.TryGetValue(register, out value);
                value |= 1u << 15;
                return true;
            }

            if (register == 0x3FF5F06C || register == 0x3FF6006C)
            {
                // ... and its result: XTAL (40 MHz) cycles during the requested slow-clock (150 kHz) cycles, from bit 7
                _registers.TryGetValue(register - 4, out uint config);
                uint slowCycles = (config >> 16) & 0x7FFF;
                value = (uint)(slowCycles * 40e6 / 150e3) << 7;
                return true;
            }

            switch (register)
            {
                case Uart0Fifo:
                    value = 0;
                    return true;
                case Uart0Status:
                    value = 0;   // TX FIFO empty, nothing received
                    return true;
                case AppCpuCtrlA:
                case AppCpuCtrlB:
                case AppCpuCtrlD:
                    _registers.TryGetValue(register, out value);
                    return true;
            }

            value = 0;
            return false;
        }

        /// <summary>Peripheral writes the chip models; false when nothing does yet (the register is noted).</summary>
        private bool WritePeripheral(uint register, uint value)
        {
            if (WriteFlashRegister(register, value)) return true;
            if (WriteTimerRegister(register, value)) return true;
            if (WriteAdcRegister(register, value)) return true;
            if (WriteLedcRegister(register, value)) return true;
            if (WriteEfuseRegister(register, value)) return true;
            if (WriteGpioRegister(register, value)) return true;
            if (register >= ProIntrMap && register < AppIntrMap + 69 * 4)
            {
                int cpu = register >= AppIntrMap ? 1 : 0;
                RouteInterrupt(cpu, (int)((register - (cpu == 0 ? ProIntrMap : AppIntrMap)) >> 2), (int)(value & 0x1F));
                return true;
            }

            if (register >= FromCpuIntr && register < FromCpuIntr + 16)
            {
                SetPeripheralLevel(FromCpuSource + (int)((register - FromCpuIntr) >> 2), (value & 1) != 0);
                return true;
            }

            switch (register)
            {
                case Uart0Fifo:
                    Uart0.Append((char)(value & 0xFF));
                    return true;
                case AppCpuCtrlB:
                case AppCpuCtrlD:
                    return true;
                case AppCpuCtrlA:
                    // Bit 0 holds the APP_CPU in reset; clearing it starts the core at the boot address.
                    if ((value & 1) == 0 && !AppRunning)
                    {
                        _registers.TryGetValue(AppCpuCtrlD, out uint boot);
                        App.Reset(boot != 0 ? boot : 0x40000400);
                        AppRunning = true;
                    }
                    else if ((value & 1) != 0)
                    {
                        AppRunning = false;
                    }

                    return true;
            }

            return false;
        }
    }
}
