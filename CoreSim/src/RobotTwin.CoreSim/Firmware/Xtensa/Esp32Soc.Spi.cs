using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] The general-purpose SPI controllers SPI2 (HSPI, 0x3FF64000) and SPI3 (VSPI, 0x3FF65000) as Arduino's SPI
    /// library and IDF's spi_master drive them: a user transaction (CMD.USR) of the phases USER enables — command, address,
    /// MOSI (MOSI_DLEN+1 bits from W0–W15, byte 0 first) and MISO, full duplex (DOUTDIN) capturing MISO into the same buffer
    /// — in the bit order CTRL sets, in the SPI mode from PIN.CK_IDLE_EDGE and USER.CK_OUT_EDGE, at the clock SPI_CLOCK
    /// divides from APB. The bytes go over the board's <see cref="SpiBus"/> to the devices wired to the pins the matrix
    /// (or the IO_MUX) routes the controller's SCK, MISO and MOSI to; a hardware chip select (CS0–2 routed to a pad, not
    /// disabled in PIN) frames the transaction, a sketch's own CS pin frames it through <see cref="PadLevelChanged"/>. USR
    /// stays set, and TRANS_DONE (SLAVE bit 4; sources 30, 31) comes, after the transfer's time on the wire.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private static readonly uint[] SpiBase = { 0x3FF64000, 0x3FF65000 };

        // The controllers' signals in the GPIO matrix: SCK, MISO (Q), MOSI (D), CS0–2 for SPI2 (HSPI) and SPI3 (VSPI).
        private static readonly int[][] SpiSignals = { new[] { 8, 9, 10, 11, 61, 62 }, new[] { 63, 64, 65, 68, 69, 70 } };

        // Their own pads through the IO_MUX (function 1): SCK, MISO, MOSI, CS0.
        private static readonly int[][] SpiNativePads = { new[] { 14, 12, 13, 15 }, new[] { 18, 19, 23, 5 } };

        /// <summary>The bus the SPI2 and SPI3 controllers drive: the devices wired to the board (null: none).</summary>
        public SpiBus? Spi { get; set; }

        /// <summary>
        /// A pad's level as the firmware drives it changed (a GPIO written, its driver turned on or off; a pad left to
        /// itself reads high): the board passes it on, as chip selects frame SPI devices' transfers.
        /// </summary>
        public event Action<int, bool>? PadLevelChanged;

        private ulong _padLevels = ulong.MaxValue;
        private readonly double[] _spiDoneAt = { double.NaN, double.NaN };

        private int SpiIndex(uint register) => register >= SpiBase[0] && register < SpiBase[0] + 0x400 ? 0
            : register >= SpiBase[1] && register < SpiBase[1] + 0x400 ? 1 : -1;

        /// <summary>After a GPIO write: the pads whose driven level changed are reported.</summary>
        private void NotifyPadLevels()
        {
            if (PadLevelChanged == null) return;
            ulong levels = 0;
            for (int gpio = 0; gpio < 40; gpio++)
            {
                if (IoMuxOffset[gpio] == 0) continue;
                var drive = PadDrive(gpio);
                if (!drive.Driven || drive.Level || drive.IsPwm) levels |= 1UL << gpio;
            }

            ulong changed = levels ^ _padLevels;
            _padLevels = levels;
            for (int gpio = 0; gpio < 40 && changed != 0; gpio++)
                if ((changed >> gpio & 1) != 0) PadLevelChanged(gpio, (levels >> gpio & 1) != 0);
        }

        private bool ReadSpiRegister(uint register, out uint value)
        {
            value = 0;
            int n = SpiIndex(register);
            if (n < 0) return false;
            uint offset = register - SpiBase[n];
            if (offset == 0x00 || offset == 0x38) FinishSpi(n, Seconds);   // a poll sees the transfer end on time
            value = Reg(register);
            if (offset == 0x00 && !double.IsNaN(_spiDoneAt[n])) value |= 1u << 18;   // CMD.USR while the transfer runs
            return true;
        }

        private bool WriteSpiRegister(uint register, uint value)
        {
            int n = SpiIndex(register);
            if (n < 0) return false;
            uint offset = register - SpiBase[n];
            _registers[register] = value & ~SelfClearing(register);
            if (offset == 0x00 && (value & (1u << 18)) != 0)
            {
                SpiTransaction(n);
            }
            else if (offset == 0x38)
            {
                UpdateSpiLine(n);
            }

            return true;
        }

        /// <summary>The pad a controller's output signal leaves on (the matrix, or its own pad through the IO_MUX); -1: none.</summary>
        private int SpiOutPad(int n, int role)
        {
            int signal = SpiSignals[n][role];
            for (int gpio = 0; gpio < 40; gpio++)
            {
                if (IoMuxOffset[gpio] == 0) continue;
                int function = (int)((Reg(IoMux + IoMuxOffset[gpio]) >> 12) & 7);
                if (function == 2 && (_funcOut[gpio] & 0x1FF) == signal) return gpio;
            }

            int native = role < 3 ? SpiNativePads[n][role] : role == 3 ? SpiNativePads[n][3] : -1;
            return native >= 0 && ((Reg(IoMux + IoMuxOffset[native]) >> 12) & 7) == 1 ? native : -1;
        }

        /// <summary>The pad MISO comes in on: FUNC_IN_SEL of the controller's Q signal, or its own pad through the IO_MUX.</summary>
        private int SpiInPad(int n)
        {
            uint cfg = _funcIn[SpiSignals[n][1]];
            if ((cfg & (1u << 7)) != 0 && (cfg & 0x3F) < 40) return (int)(cfg & 0x3F);
            int native = SpiNativePads[n][1];
            return ((Reg(IoMux + IoMuxOffset[native]) >> 12) & 7) == 1 ? native : -1;
        }

        private void SpiTransaction(int n)
        {
            uint b = SpiBase[n];
            uint user = Reg(b + 0x1C), ctrl = Reg(b + 0x08), pin = Reg(b + 0x34), clock = Reg(b + 0x18);
            int sck = SpiOutPad(n, 0), mosi = SpiOutPad(n, 2), miso = SpiInPad(n);
            bool cpol = (pin & (1u << 29)) != 0, outEdge = (user & (1u << 7)) != 0;
            int mode = (cpol ? 2 : 0) | (cpol ^ outEdge ? 1 : 0);
            bool lsbOut = (ctrl & (1u << 26)) != 0, lsbIn = (ctrl & (1u << 25)) != 0;
            double hz = (clock & (1u << 31)) != 0 ? ApbHz
                : ApbHz / ((((clock >> 18) & 0x1FFF) + 1.0) * (((clock >> 12) & 0x3F) + 1.0));

            // A hardware chip select: CSn routed to a pad and not disabled in PIN.
            int cs = -1;
            for (int k = 0; k < 3 && cs < 0; k++)
                if ((pin & (1u << k)) == 0) cs = SpiOutPad(n, 3 + k);
            if (cs >= 0) Spi?.PinChanged(cs, false);

            int bits = 0;
            byte Exchange(byte value)
            {
                byte wire = lsbOut ? Reverse(value) : value;
                byte back = Spi != null && sck >= 0 ? Spi.Transfer(sck, miso, mosi, wire, mode, hz) : (byte)0xFF;
                bits += 8;
                return lsbIn ? Reverse(back) : back;
            }

            if ((user & (1u << 31)) != 0)
            {
                // command phase: USER2's value, its bit length (whole bytes, most significant first)
                uint user2 = Reg(b + 0x24);
                int cmdBits = (int)((user2 >> 28) & 0xF) + 1;
                for (int k = (cmdBits + 7) / 8 - 1; k >= 0; k--) Exchange((byte)(user2 >> (8 * k)));
            }

            if ((user & (1u << 30)) != 0)
            {
                // address phase: ADDR's top USER1.ADDR_BITLEN+1 bits
                uint addr = Reg(b + 0x04);
                int addrBits = (int)((Reg(b + 0x20) >> 26) & 0x3F) + 1;
                for (int k = 0; k < (addrBits + 7) / 8; k++) Exchange((byte)(addr >> (24 - 8 * k)));
            }

            if ((user & (1u << 29)) != 0) bits += (int)(Reg(b + 0x20) & 0xFF) + 1;   // dummy cycles

            bool fullDuplex = (user & 1) != 0;
            if ((user & (1u << 27)) != 0)
            {
                int mosiBytes = (int)(((Reg(b + 0x28) & 0xFFFFFF) + 8) / 8);
                uint baseOut = (user & (1u << 25)) != 0 ? 0x80u + 32 : 0x80u;   // USR_MOSI_HIGHPART: W8–W15
                for (int k = 0; k < mosiBytes && k < 64; k++)
                {
                    uint word = b + baseOut + (uint)(k / 4) * 4;
                    int shift = 8 * (k % 4);
                    byte back = Exchange((byte)(Reg(word) >> shift));
                    if (fullDuplex) _registers[word] = (Reg(word) & ~(0xFFu << shift)) | (uint)back << shift;
                }
            }

            if ((user & (1u << 28)) != 0 && !fullDuplex)
            {
                int misoBytes = (int)(((Reg(b + 0x2C) & 0xFFFFFF) + 8) / 8);
                uint baseIn = (user & (1u << 24)) != 0 ? 0x80u + 32 : 0x80u;    // USR_MISO_HIGHPART
                for (int k = 0; k < misoBytes && k < 64; k++)
                {
                    uint word = b + baseIn + (uint)(k / 4) * 4;
                    int shift = 8 * (k % 4);
                    byte back = Exchange(0xFF);
                    _registers[word] = (Reg(word) & ~(0xFFu << shift)) | (uint)back << shift;
                }
            }

            if (cs >= 0 && (pin & (1u << 30)) == 0) Spi?.PinChanged(cs, true);   // released unless CS_KEEP_ACTIVE
            _spiDoneAt[n] = Seconds + Math.Max(1, bits) / Math.Max(1, hz);
        }

        private static byte Reverse(byte v)
        {
            int r = 0;
            for (int i = 0; i < 8; i++) r |= (v >> i & 1) << (7 - i);
            return (byte)r;
        }

        /// <summary>Each turn: a transfer whose time is up ends: USR clears, TRANS_DONE rises.</summary>
        private void ServiceSpi()
        {
            for (int n = 0; n < 2; n++) FinishSpi(n, Seconds);
        }

        private void FinishSpi(int n, double now)
        {
            if (double.IsNaN(_spiDoneAt[n]) || now < _spiDoneAt[n]) return;
            _spiDoneAt[n] = double.NaN;
            uint slave = SpiBase[n] + 0x38;
            _registers[slave] = Reg(slave) | 1u << 4;
            UpdateSpiLine(n);
        }

        /// <summary>Sources 30 (SPI2) and 31 (SPI3): TRANS_DONE with TRANS_INTEN (SLAVE bits 4 and 9).</summary>
        private void UpdateSpiLine(int n)
        {
            uint slave = Reg(SpiBase[n] + 0x38);
            SetPeripheralLevel(30 + n, (slave & (1u << 4)) != 0 && (slave & (1u << 9)) != 0);
        }
    }
}
