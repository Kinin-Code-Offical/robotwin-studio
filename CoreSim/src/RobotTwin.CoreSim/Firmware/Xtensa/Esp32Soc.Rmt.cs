using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [esp32emu] [neopixel] The remote-control peripheral (RMT, 0x3FF56000) as IDF's RMT TX driver uses it for WS2812
    /// strips (Arduino's rgbLedWrite, NeoPixel libraries): eight channels, each sending 32-bit items from RMT RAM
    /// (0x3FF56800, 64 words a block; level and duration of two halves, ticks of APB or REF_TICK over DIV_CNT) item by item
    /// over time once TX_START is set, wrapping in its blocks and raising TX_THR_EVENT every TX_LIM items so the driver can
    /// refill, and TX_END at the end marker (a zero duration), on source 47. The pulses a channel sent are decoded as the
    /// strip decodes them (a high time over 0.55 µs is a 1) and the bytes reach the strip wired to the pad the matrix routes
    /// the channel's signal to (<see cref="RmtFrame"/>).
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint Rmt = 0x3FF56000, RmtRam = 0x3FF56800;
        private const int RmtSignal0 = 87, RmtInterruptSource = 47;

        /// <summary>A channel finished a transmission: the pad it left on and the bytes its pulses carried (WS2812 timing).</summary>
        public event Action<int, byte[]>? RmtFrame;

        private sealed class RmtChannel
        {
            public bool Sending;
            public int Item;              // the next item, counted from the channel's first word
            public int SinceThreshold;    // items sent since the last TX_THR_EVENT
            public double NextAt;         // when the next item starts (chip seconds)
            public readonly List<bool> Bits = new List<bool>();
        }

        private readonly RmtChannel[] _rmt = { new RmtChannel(), new RmtChannel(), new RmtChannel(), new RmtChannel(),
                                               new RmtChannel(), new RmtChannel(), new RmtChannel(), new RmtChannel() };
        private uint _rmtIntRaw, _rmtIntEnable;

        private bool ReadRmtRegister(uint register, out uint value)
        {
            value = 0;
            if (register < Rmt || register >= Rmt + 0x100) return false;
            switch (register - Rmt)
            {
                case 0xA0: value = _rmtIntRaw; return true;
                case 0xA4: value = _rmtIntRaw & _rmtIntEnable; return true;
                case 0xA8: value = _rmtIntEnable; return true;
            }

            value = Reg(register);
            return true;
        }

        private bool WriteRmtRegister(uint register, uint value)
        {
            if (register < Rmt || register >= Rmt + 0x100) return false;
            uint offset = register - Rmt;
            switch (offset)
            {
                case 0xA8:
                    _rmtIntEnable = value;
                    break;
                case 0xAC:
                    _rmtIntRaw &= ~value;
                    break;
                default:
                    if (offset >= 0x20 && offset < 0x60 && (offset & 4) != 0)
                    {
                        // CHnCONF1: TX_START (bit 0) starts sending from the channel's first word; MEM_RD_RST (bit 3) rewinds.
                        int ch = (int)((offset - 0x24) / 8);
                        _registers[register] = value & ~1u;
                        if ((value & (1u << 3)) != 0) _rmt[ch].Item = 0;
                        if ((value & 1u) != 0) StartRmt(ch);
                        return true;
                    }

                    return true;
            }

            SetPeripheralLevel(RmtInterruptSource, (_rmtIntRaw & _rmtIntEnable) != 0);
            return true;
        }

        private void StartRmt(int ch)
        {
            var c = _rmt[ch];
            c.Sending = true;
            c.Item = 0;
            c.SinceThreshold = 0;
            c.Bits.Clear();
            c.NextAt = Seconds;
        }

        /// <summary>The seconds one tick of channel <paramref name="ch"/> lasts.</summary>
        private double RmtTick(int ch)
        {
            uint conf0 = Reg(Rmt + 0x20 + 8 * (uint)ch), conf1 = Reg(Rmt + 0x24 + 8 * (uint)ch);
            uint div = conf0 & 0xFF;
            double clock = (conf1 & (1u << 17)) != 0 ? ApbHz : 1e6;
            return (div == 0 ? 256 : div) / clock;
        }

        /// <summary>Each turn: every sending channel sends the items whose time has come.</summary>
        private void ServiceRmt()
        {
            double now = Seconds;
            for (int ch = 0; ch < 8; ch++)
            {
                var c = _rmt[ch];
                if (!c.Sending) continue;
                double tick = RmtTick(ch);
                uint conf0 = Reg(Rmt + 0x20 + 8 * (uint)ch);
                int blocks = (int)Math.Max(1, (conf0 >> 24) & 0xF);
                int words = Math.Min(blocks, 8 - ch) * 64;
                bool wrap = (Reg(Rmt + 0xF0) & 2) != 0;
                uint limit = Reg(Rmt + 0xD0 + 4 * (uint)ch) & 0x1FF;
                for (int guard = 0; guard < 4096 && c.NextAt <= now; guard++)
                {
                    int index = wrap ? c.Item % words : Math.Min(c.Item, words - 1);
                    uint item = Reg(RmtRam + (uint)(64 * ch + index) * 4);
                    uint d0 = item & 0x7FFF, d1 = (item >> 16) & 0x7FFF;
                    bool l0 = (item & 0x8000) != 0;
                    if (d0 == 0 || (!wrap && c.Item >= words))
                    {
                        // The end marker: the transmission is over.
                        c.Sending = false;
                        _rmtIntRaw |= 1u << (3 * ch);
                        DeliverRmt(ch);
                        break;
                    }

                    // One WS2812 bit per item: its high half's length decides (T0H ≈ 0.4 µs, T1H ≈ 0.8 µs).
                    double high = l0 ? d0 * tick : d1 * tick;
                    c.Bits.Add(high > 0.55e-6);
                    c.NextAt += (d0 + (d1 == 0 ? 0 : d1)) * tick;
                    c.Item++;
                    if (d1 == 0)
                    {
                        // An end marker in the second half: done after this item.
                        c.Sending = false;
                        _rmtIntRaw |= 1u << (3 * ch);
                        DeliverRmt(ch);
                        break;
                    }

                    if (wrap && limit > 0 && ++c.SinceThreshold >= limit)
                    {
                        c.SinceThreshold = 0;
                        _rmtIntRaw |= 1u << (24 + ch);   // TX_THR_EVENT: the driver refills the half already sent
                    }
                }
            }

            SetPeripheralLevel(RmtInterruptSource, (_rmtIntRaw & _rmtIntEnable) != 0);
        }

        /// <summary>The bits a channel sent, as bytes (most significant bit first), to the pad its signal leaves on.</summary>
        private void DeliverRmt(int ch)
        {
            var bits = _rmt[ch].Bits;
            var bytes = new byte[bits.Count / 8];
            for (int i = 0; i < bytes.Length * 8; i++)
                if (bits[i]) bytes[i / 8] |= (byte)(0x80 >> (i % 8));
            bits.Clear();
            int pad = -1;
            for (int gpio = 0; gpio < 40 && pad < 0; gpio++)
                if (IoMuxOffset[gpio] != 0 && (_funcOut[gpio] & 0x1FF) == RmtSignal0 + ch) pad = gpio;
            if (pad >= 0 && bytes.Length > 0) RmtFrame?.Invoke(pad, bytes);
        }
    }
}
