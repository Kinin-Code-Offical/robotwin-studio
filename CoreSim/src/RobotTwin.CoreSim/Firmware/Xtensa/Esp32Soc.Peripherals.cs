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
