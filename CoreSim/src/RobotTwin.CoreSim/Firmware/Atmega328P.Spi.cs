using System;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// [spi] The ATmega328P's SPI master as the datasheet (section 19) has it: writing SPDR clocks the byte out on SCK
    /// (D13) and MOSI (D11) at f/4…f/128 (SPI2X doubles it), in the mode CPOL/CPHA set and the order DORD sets, every edge
    /// at its own cycle; MISO (D12) is sampled on the sampling edges: from the device the chip select picks on
    /// <see cref="Spi"/>, or from the pin as the outside world drives it when none is selected. After the eighth bit SPIF is
    /// set (and the SPI vector taken with SPIE); a write while a byte is on its way is lost and sets WCOL; SPIF/WCOL clear
    /// by the vector or by reading SPSR and then touching SPDR. In master mode MISO is an input whatever DDRB says, and SS
    /// (D10) made an input and pulled low takes the master role away (MODF: MSTR cleared, SPIF set). Slave mode is not
    /// modelled (the Uno is the master of its bus).
    /// </summary>
    public sealed partial class Atmega328P
    {
        private const byte SpIe = 0x80, SpEn = 0x40, SpDord = 0x20, SpMstr = 0x10, SpCpol = 0x08, SpCpha = 0x04;
        private const byte SpIf = 0x80, SpWcol = 0x40, Spi2x = 0x01;
        private const int SpiSsPin = 10, SpiMosiPin = 11, SpiMisoPin = 12, SpiSckPin = 13;
        private static readonly int[] SpiDividers = { 4, 16, 64, 128 };

        private int _spiEdge = -1;      // edges done in the byte being clocked (0…16); −1 = idle
        private ulong _spiStart;        // the cycle the byte started
        private int _spiHalf;           // CPU cycles per half SCK period
        private byte _spiOut, _spiIn, _spiReply;
        private bool _spiFromBus;
        private bool _spiSck, _spiMosi; // the levels the SPI puts on SCK and MOSI
        private bool _spiFlagRead;      // SPSR read with SPIF set: touching SPDR now clears it

        /// <summary>The SPI devices wired to SCK (D13), MISO (D12) and MOSI (D11), chip selects on any pin (Arduino numbers).</summary>
        public SpiBus Spi { get; } = new SpiBus();

        /// <summary>SPE and MSTR: D13 and D11 belong to the SPI, D12 is its input.</summary>
        public bool SpiMaster => (_d[SPCR] & (SpEn | SpMstr)) == (SpEn | SpMstr);

        /// <summary>The SCK frequency the firmware set (SPR1:0, SPI2X), Hz.</summary>
        public double SpiClockHz => ClockHz / SpiDivider();

        /// <summary>A byte is being clocked out.</summary>
        public bool SpiBusy => _spiEdge >= 0;

        private int SpiDivider() => SpiDividers[_d[SPCR] & 3] / ((_d[SPSR] & Spi2x) != 0 ? 2 : 1);

        private int SpiMode => (_d[SPCR] >> 2) & 3;

        private void ResetSpi()
        {
            _spiEdge = -1;
            _spiSck = _spiMosi = false;
            _spiFlagRead = false;
            foreach (var s in Spi.Slaves)
                if (s.Selected) Spi.PinChanged(s.Cs, true);   // the pins let go at reset: chip selects float high
        }

        /// <summary>Level of a pin the SPI owns in master mode, or null when the port has it.</summary>
        private bool? SpiPinLevel(int pin, bool output)
        {
            if (!SpiMaster) return null;
            if (pin == SpiMisoPin) return _inputs[8 + 4];                 // MISO: an input whatever DDRB says
            if (!output) return null;                                     // SCK/MOSI driven only where DDRB says output
            if (pin == SpiSckPin) return _spiSck;
            if (pin == SpiMosiPin) return _spiMosi;
            return null;
        }

        private byte ReadSpsr()
        {
            if ((_d[SPSR] & SpIf) != 0) _spiFlagRead = true;
            return _d[SPSR];
        }

        private byte ReadSpdr()
        {
            ClearSpiFlagsAfterRead();
            return _d[SPDR];
        }

        private void ClearSpiFlagsAfterRead()
        {
            if (!_spiFlagRead) return;
            _d[SPSR] &= unchecked((byte)~(SpIf | SpWcol));
            _spiFlagRead = false;
        }

        private void WriteSpsr(byte v) => _d[SPSR] = (byte)((_d[SPSR] & (SpIf | SpWcol)) | (v & Spi2x));

        private void WriteSpcr(byte v)
        {
            bool was = (_d[SPCR] & SpEn) != 0;
            _d[SPCR] = v;
            if ((v & SpEn) == 0)
            {
                _spiEdge = -1;   // switched off: the byte on its way is dropped
                if (was) DetectPinChanges();
                return;
            }

            if (!SpiBusy) _spiSck = (v & SpCpol) != 0;   // SCK idles at CPOL
            CheckSpiModeFault();
            DetectPinChanges();
        }

        /// <summary>MODF: SS made an input and pulled low while the SPI is master — another master wants the bus.</summary>
        private void CheckSpiModeFault()
        {
            if (!SpiMaster) return;
            bool ssInput = (_d[DDRB] & 0x04) == 0;
            if (!ssInput || _inputs[8 + 2]) return;
            _d[SPCR] &= unchecked((byte)~SpMstr);
            _d[SPSR] |= SpIf;
            _spiEdge = -1;
        }

        private void WriteSpdr(byte v)
        {
            ClearSpiFlagsAfterRead();
            if (SpiBusy)
            {
                _d[SPSR] |= SpWcol;   // single-buffered: the byte on its way goes on, this one is lost
                return;
            }

            if (!SpiMaster) return;   // disabled, or a slave waiting for a clock that never comes here
            CheckSpiModeFault();
            if (!SpiMaster) return;

            _spiOut = v;
            _spiIn = 0;
            _spiStart = Cycles;
            _spiHalf = Math.Max(1, SpiDivider() / 2);
            _spiEdge = 0;
            bool lsbFirst = (_d[SPCR] & SpDord) != 0;

            // The device the chip select picks answers from its protocol model, its byte as it goes on the wire (MSB first).
            _spiFromBus = false;
            foreach (var s in Spi.Slaves)
            {
                if (!s.Selected || s.Sck != SpiSckPin || s.Mosi != SpiMosiPin) continue;
                _spiFromBus = true;
                break;
            }

            if (_spiFromBus)
            {
                byte wire = lsbFirst ? Reverse(v) : v;
                byte reply = Spi.Transfer(SpiSckPin, SpiMisoPin, SpiMosiPin, wire, SpiMode, SpiClockHz);
                _spiReply = lsbFirst ? Reverse(reply) : reply;
            }

            _spiSck = (_d[SPCR] & SpCpol) != 0;
            if ((_d[SPCR] & SpCpha) == 0) _spiMosi = SpiBit(_spiOut, 0);   // CPHA 0: the first bit is out before the first edge
            DetectPinChanges();
        }

        private bool SpiBit(byte value, int index) => (_d[SPCR] & SpDord) != 0 ? (value >> index & 1) != 0 : (value >> (7 - index) & 1) != 0;

        private void SpiSample(int index)
        {
            bool bit = _spiFromBus ? SpiBit(_spiReply, index) : _inputs[8 + 4];
            if (!bit) return;
            _spiIn |= (_d[SPCR] & SpDord) != 0 ? (byte)(1 << index) : (byte)(0x80 >> index);
        }

        private void AdvanceSpi()
        {
            bool cpha = (_d[SPCR] & SpCpha) != 0, cpol = (_d[SPCR] & SpCpol) != 0;
            while (_spiEdge >= 0 && _spiEdge < 16)
            {
                ulong at = _spiStart + (ulong)((_spiEdge + 1) * _spiHalf);
                if (at > Cycles) return;
                int k = ++_spiEdge;
                int index = (k - 1) / 2;
                bool leading = (k & 1) == 1;
                _spiSck = leading ? !cpol : cpol;
                if (leading)
                {
                    if (cpha) _spiMosi = SpiBit(_spiOut, index);
                    else SpiSample(index);
                }
                else
                {
                    if (cpha) SpiSample(index);
                    else if (index < 7) _spiMosi = SpiBit(_spiOut, index + 1);
                }

                DetectPinChanges(at);
            }

            if (_spiEdge == 16)
            {
                _spiEdge = -1;
                _d[SPDR] = _spiIn;
                _d[SPSR] |= SpIf;
            }
        }

        private static byte Reverse(byte b)
        {
            int r = 0;
            for (int i = 0; i < 8; i++)
                if ((b >> i & 1) != 0) r |= 0x80 >> i;
            return (byte)r;
        }
    }
}
