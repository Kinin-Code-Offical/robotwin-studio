using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The eFuse controller (0x3FF5A000) holding what the factory burns into every ESP32, as on a typical
    /// ESP32-D0WD-V3 (chip revision 3.0, the WROOM-32E's): the factory MAC with its CRC (BLK0), the chip revision bits
    /// (BLK0 CHIP_VER_REV1/REV2 with APB_CTRL_DATE bit 31), the package, the CPU-frequency rating, and the ADC reference
    /// calibration (ADC_VREF: 1100 mV + 7 mV steps) that IDF's line-fitting characterisation, and so
    /// analogReadMilliVolts, depends on. Reads come from the burned words; writes to the programming registers are kept
    /// but burn nothing.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint Efuse = 0x3FF5A000, EfuseEnd = 0x3FF5A200;
        private const uint ApbCtrlDate = 0x3FF6607C;

        private readonly uint[] _efuse = new uint[(EfuseEnd - Efuse) / 4];
        private byte[] _mac = { 0x02, 0x52, 0x57, 0x00, 0x00, 0x01 };
        private int _vrefMillivolts = 1107;

        /// <summary>
        /// The factory MAC (BLK0; Wi-Fi station's base address). Default 02:52:57:00:00:01, a locally administered
        /// address; give each emulated board in a scene its own.
        /// </summary>
        public byte[] FactoryMac
        {
            get => (byte[])_mac.Clone();
            set
            {
                if (value == null || value.Length != 6) throw new ArgumentException("A MAC address has 6 bytes.", nameof(value));
                _mac = (byte[])value.Clone();
                BurnEfuse();
            }
        }

        /// <summary>The ADC reference the factory measured, 1100 mV ± 15 steps of 7 mV (1100 itself cannot be burned: 0 means "not burned").</summary>
        public int AdcVrefMillivolts
        {
            get => _vrefMillivolts;
            set
            {
                _vrefMillivolts = Math.Clamp(value, 1100 - 15 * 7, 1100 + 15 * 7);
                BurnEfuse();
            }
        }

        /// <summary>Writes the factory's words into BLK0 (called at construction and when a burned value changes).</summary>
        private void BurnEfuse()
        {
            Array.Clear(_efuse, 0, 7);
            // RDATA1/2: MAC bytes 5..2 in RDATA1 (byte 5 lowest), bytes 1..0 in RDATA2 bits 0–15, the CRC8 in bits 16–23.
            _efuse[1] = (uint)(_mac[5] | _mac[4] << 8 | _mac[3] << 16 | _mac[2] << 24);
            _efuse[2] = (uint)(_mac[1] | _mac[0] << 8 | MacCrc8(_mac, 6) << 16);
            // RDATA3: CHIP_VER_REV1 (15), CHIP_CPU_FREQ_RATED (13; not the 160 MHz-limited part), package 1: D0WD (9–11).
            _efuse[3] = 1u << 15 | 1u << 13 | 1u << 9;
            // RDATA4: ADC_VREF (8–12), sign-magnitude in 7 mV steps from 1100 mV.
            int steps = (int)Math.Round((_vrefMillivolts - 1100) / 7.0);
            uint vref = steps >= 0 ? (uint)steps : 0x10u | (uint)-steps;
            _efuse[4] = (vref & 0x1F) << 8;
            // RDATA5: CHIP_VER_REV2 (20); wafer minor version 0.
            _efuse[5] = 1u << 20;
            // Revision 3's third bit lives outside the eFuse, in APB_CTRL_DATE.
            _registers[ApbCtrlDate] = (Reg(ApbCtrlDate) & 0x7FFFFFFF) | 0x80000000u;
        }

        /// <summary>The CRC the ROM checks the factory MAC with (esp_crc8: Dallas/Maxim, reflected 0x31, starting from 0).</summary>
        internal static byte MacCrc8(byte[] data, int length)
        {
            byte crc = 0;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[i];
                for (int b = 0; b < 8; b++) crc = (byte)((crc & 1) != 0 ? (crc >> 1) ^ 0x8C : crc >> 1);
            }

            return crc;
        }

        private bool ReadEfuseRegister(uint register, out uint value)
        {
            value = 0;
            if (register == ApbCtrlDate)
            {
                value = Reg(register);
                return true;
            }

            if (register < Efuse || register >= EfuseEnd) return false;
            value = _efuse[(register - Efuse) / 4];
            return true;
        }

        private bool WriteEfuseRegister(uint register, uint value)
        {
            if (register < Efuse || register >= EfuseEnd) return false;
            uint index = (register - Efuse) / 4;
            // The read-data words (BLK0 0x00–0x18, BLK1–3 0x38–0x94) are what is burned; the rest is the programming
            // interface (write data, timing, CMD), kept so the driver reads back what it set.
            bool readData = index <= 6 || (index >= 0x38 / 4 && index <= 0x94 / 4);
            // EFUSE_CMD (read / program) completes at once: it reads back 0, as when the controller is done.
            if (!readData && register != Efuse + 0x104) _efuse[index] = value;
            return true;
        }
    }
}
