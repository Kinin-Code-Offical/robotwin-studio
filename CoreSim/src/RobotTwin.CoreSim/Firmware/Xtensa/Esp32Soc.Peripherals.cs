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

        private void RouteInterrupt(int cpu, int source, int cpuInterrupt)
        {
            if (cpu < 0 || cpu > 1 || source < 0 || source >= 69) return;
            map(cpu, source, cpuInterrupt);

            void map(int c, int s, int i) => _interruptMap[c, s] = i is >= 0 and < 32 ? i : -1;
        }

        /// <summary>Raises peripheral interrupt <paramref name="source"/> on whichever core and CPU interrupt the matrix routes it to.</summary>
        public void RaisePeripheral(int source)
        {
            if (source < 0 || source >= 69) return;
            if (_interruptMap[0, source] >= 0) Pro.RaiseInterrupt(_interruptMap[0, source]);
            if (_interruptMap[1, source] >= 0) App.RaiseInterrupt(_interruptMap[1, source]);
        }

        /// <summary>Peripheral reads the chip models; false leaves the register file's value (and notes the register).</summary>
        private bool ReadPeripheral(uint register, out uint value)
        {
            if (ReadFlashRegister(register, out value)) return true;
            if (register == 0x3FF4800C)
            {
                // RTC_CNTL_TIME_UPDATE: the latched RTC time is always valid (bit 30)
                _registers.TryGetValue(register, out value);
                value |= 1u << 30;
                return true;
            }

            if (register == 0x3FF48010 || register == 0x3FF48014)
            {
                // RTC_CNTL_TIME0/1: the RTC slow clock's 48-bit count (150 kHz), from the PRO_CPU's cycles at 240 MHz
                ulong rtc = Pro.Cycles * 150_000UL / 240_000_000UL;
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
