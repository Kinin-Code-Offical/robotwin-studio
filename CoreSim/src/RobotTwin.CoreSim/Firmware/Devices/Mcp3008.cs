using System;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// Microchip MCP3008: 8-channel 10-bit SAR ADC on SPI (modes 0,0 and 1,1). Bit-level frame as in
    /// the datasheet (figure 6-1): after CS falls the device waits for the start bit on DIN, reads
    /// SGL/DIFF and D2..D0, samples on the next clock, outputs a null bit, then B9..B0 MSB first and
    /// (if the master keeps clocking) B1..B9 LSB first. DOUT is high impedance before the null bit
    /// (reads as 1 here). The input voltage comes from the solved circuit at the moment of sampling.
    /// </summary>
    public sealed class Mcp3008 : ISpiDevice
    {
        private enum Phase { WaitStart, Config, Sample, Null, Msb, Lsb, Done }

        private readonly Func<int, double> _channel;
        private readonly Func<double> _vref;
        private Phase _phase = Phase.Done;
        private int _bits;
        private int _config;
        private int _code;

        /// <param name="channelVolts">Voltage of CH0..CH7 relative to AGND.</param>
        /// <param name="vref">VREF relative to AGND.</param>
        public Mcp3008(Func<int, double> channelVolts, Func<double> vref)
        {
            _channel = channelVolts;
            _vref = vref;
        }

        /// <summary>Last conversion result (0..1023) and its channel configuration, for the UI.</summary>
        public int LastCode { get; private set; }

        public int LastConfig { get; private set; }

        public void Select(bool selected)
        {
            _phase = selected ? Phase.WaitStart : Phase.Done;
            _bits = 0;
        }

        public byte Transfer(byte mosi)
        {
            int result = 0;
            for (int i = 7; i >= 0; i--) result = (result << 1) | Clock((mosi >> i) & 1);
            return (byte)result;
        }

        private int Clock(int din)
        {
            switch (_phase)
            {
                case Phase.WaitStart:
                    if (din == 1)
                    {
                        _phase = Phase.Config;
                        _bits = 0;
                        _config = 0;
                    }

                    return 1;
                case Phase.Config:
                    _config = (_config << 1) | din;
                    if (++_bits == 4) _phase = Phase.Sample;
                    return 1;
                case Phase.Sample:
                    _code = Convert(_config);
                    LastCode = _code;
                    LastConfig = _config;
                    _phase = Phase.Null;
                    return 1;
                case Phase.Null:
                    _phase = Phase.Msb;
                    _bits = 0;
                    return 0;
                case Phase.Msb:
                {
                    int bit = (_code >> (9 - _bits)) & 1;
                    if (++_bits == 10)
                    {
                        _phase = Phase.Lsb;
                        _bits = 1;
                    }

                    return bit;
                }

                case Phase.Lsb:
                {
                    int bit = (_code >> _bits) & 1;
                    if (++_bits == 10) _phase = Phase.Done;
                    return bit;
                }

                default:
                    return 0;
            }
        }

        private int Convert(int config)
        {
            bool single = (config & 0x8) != 0;
            int ch = config & 0x7;
            double v;
            if (single) v = _channel(ch);
            else
            {
                // Pseudo-differential pairs: 000 = CH0+ CH1-, 001 = CH0- CH1+, …
                int pair = ch >> 1;
                bool swap = (ch & 1) != 0;
                double a = _channel(2 * pair), b = _channel(2 * pair + 1);
                v = swap ? b - a : a - b;
            }

            double vref = _vref();
            if (!(vref > 0.25) || double.IsNaN(v)) return 0;
            int code = (int)Math.Floor(1024 * v / vref);
            return Math.Max(0, Math.Min(1023, code));
        }
    }
}
