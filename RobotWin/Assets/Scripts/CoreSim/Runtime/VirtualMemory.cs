using System;

namespace RobotTwin.CoreSim.Runtime
{
    public class VirtualMemory
    {
        public const int FlashSizeBytes = 32 * 1024;
        public const int SramSizeBytes = 2 * 1024;
        public const int EepromSizeBytes = 1024;

        public byte[] Flash { get; } = new byte[FlashSizeBytes];
        public byte[] Sram { get; } = new byte[SramSizeBytes];
        public byte[] Eeprom { get; } = new byte[EepromSizeBytes];

        public void ClearFlash()
        {
            Array.Clear(Flash, 0, Flash.Length);
        }

        public void ClearSram()
        {
            Array.Clear(Sram, 0, Sram.Length);
        }

        public void ClearEeprom()
        {
            Array.Clear(Eeprom, 0, Eeprom.Length);
        }
    }
}
