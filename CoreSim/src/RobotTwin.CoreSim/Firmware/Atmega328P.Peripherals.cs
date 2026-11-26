using System;
using System.Collections.Generic;
using System.Text;

namespace RobotTwin.CoreSim.Firmware
{
    public sealed partial class Atmega328P
    {
        // Register addresses (data space).
        private const int PINB = 0x23, DDRB = 0x24, PORTB = 0x25, PINC = 0x26, DDRC = 0x27, PORTC = 0x28, PIND = 0x29, DDRD = 0x2A, PORTD = 0x2B;
        private const int TIFR0 = 0x35, TIFR1 = 0x36, TIFR2 = 0x37, PCIFR = 0x3B, EIFR = 0x3C, EIMSK = 0x3D;
        private const int EECR = 0x3F, EEDR = 0x40, EEARL = 0x41, EEARH = 0x42;
        private const int TCCR0A = 0x44, TCCR0B = 0x45, TCNT0 = 0x46, OCR0A = 0x47, OCR0B = 0x48;
        private const int SPCR = 0x4C, SPSR = 0x4D, SPDR = 0x4E;
        private const int SREG = 0x5F, WDTCSR = 0x60;
        private const int PCICR = 0x68, EICRA = 0x69, PCMSK0 = 0x6B, PCMSK1 = 0x6C, PCMSK2 = 0x6D;
        private const int TIMSK0 = 0x6E, TIMSK1 = 0x6F, TIMSK2 = 0x70;
        private const int ADCL = 0x78, ADCH = 0x79, ADCSRA = 0x7A, ADMUX = 0x7C;
        private const int TCCR1A = 0x80, TCCR1B = 0x81, TCNT1L = 0x84, TCNT1H = 0x85, ICR1L = 0x86, ICR1H = 0x87;
        private const int OCR1AL = 0x88, OCR1AH = 0x89, OCR1BL = 0x8A, OCR1BH = 0x8B;
        private const int TCCR2A = 0xB0, TCCR2B = 0xB1, TCNT2 = 0xB2, OCR2A = 0xB3, OCR2B = 0xB4;
        private const int TWSR = 0xB9, TWDR = 0xBB, TWCR = 0xBC;
        private const int UCSR0A = 0xC0, UCSR0B = 0xC1, UBRR0L = 0xC4, UBRR0H = 0xC5, UDR0 = 0xC6;

        private static readonly int[] Div01 = { 0, 1, 8, 64, 256, 1024, 0, 0 };
        private static readonly int[] Div2 = { 0, 1, 8, 32, 64, 128, 256, 1024 };

        private readonly bool[] _inputs = new bool[24];
        private readonly double[] _analog = new double[8];
        private byte _lastPinB, _lastPinC, _lastPinD;

        // Timers
        private int _t0Count, _t2Count, _t1Count;
        private bool _t0Up = true, _t1Up = true, _t2Up = true;
        private long _t0Pre, _t1Pre, _t2Pre;
        private int _t1Temp;

        // USART
        private readonly Queue<byte> _rxQueue = new Queue<byte>();
        private readonly StringBuilder _txOut = new StringBuilder();
        private int _txBuffer = -1, _txShift = -1;
        private double _txCycles, _rxCycles;

        // ADC
        private double _adcCycles = -1;

        // EEPROM
        private int _eempeWindow;
        private double _eepromBusy;

        // Watchdog
        private double _wdtCounter;

        private void ResetPeripherals()
        {
            _t0Count = _t1Count = _t2Count = 0;
            _t0Up = _t1Up = _t2Up = true;
            _t0Pre = _t1Pre = _t2Pre = 0;
            _txBuffer = _txShift = -1;
            _txCycles = _rxCycles = 0;
            _adcCycles = -1;
            _eempeWindow = 0;
            _eepromBusy = 0;
            _wdtCounter = 0;
            _rxQueue.Clear();
            ResetUartRx();
            _d[UCSR0A] = 0x20;
            ResetTwi();
            _lastPinB = ReadPinRegister(DDRB, PORTB, 8);
            _lastPinC = ReadPinRegister(DDRC, PORTC, 16);
            _lastPinD = ReadPinRegister(DDRD, PORTD, 0);
        }

        // ================================================================ external API

        private static void PinToPort(int pin, out int ddr, out int port, out int bit, out int inputBase)
        {
            if (pin < 8)
            {
                ddr = DDRD; port = PORTD; bit = pin; inputBase = 0;
            }
            else if (pin < 14)
            {
                ddr = DDRB; port = PORTB; bit = pin - 8; inputBase = 8;
            }
            else
            {
                ddr = DDRC; port = PORTC; bit = pin - 14; inputBase = 16;
            }
        }

        /// <summary>Level the outside world applies to an Arduino pin (D0-D13 = 0-13, A0-A5 = 14-19).</summary>
        public void SetPinInput(int pin, bool high)
        {
            if (pin < 0 || pin >= PinCount) return;
            PinToPort(pin, out _, out _, out int bit, out int inputBase);
            if (_inputs[inputBase + bit] == high) return;
            _inputs[inputBase + bit] = high;
            DetectPinChanges();
        }

        /// <summary>Voltage on an ADC channel (A0-A7).</summary>
        public void SetAnalogInput(int channel, double volts)
        {
            if (channel >= 0 && channel < _analog.Length) _analog[channel] = volts;
        }

        public double ArefVoltage { get; set; } = 5.0;

        public PinOutput GetPin(int pin)
        {
            PinToPort(pin, out int ddr, out int port, out int bit, out _);
            bool driving = (_d[ddr] & (1 << bit)) != 0;
            bool level = (_d[port] & (1 << bit)) != 0;
            if (TwiOwnsPin(pin))
            {
                // Open-drain SDA/SCL: released (idle), pulled up only by the PORTC pull-up.
                level = level && !driving;
                driving = false;
            }

            if (pin == 0 && UartRxEnabled)
            {
                // [uart-rx] The receiver overrides PD0: an input whatever DDRD says, PORTD0 still its pull-up.
                return new PinOutput(false, level, level, double.NaN, 0);
            }

            if (pin == 1 && UartTxEnabled)
            {
                // [uart] The transmitter overrides PD1: an output carrying the line's level.
                driving = true;
                level = UartTxLevel(Cycles);
                return new PinOutput(true, level, false, double.NaN, 0);
            }

            double duty = double.NaN, freq = 0;
            if (driving) HardwarePwm(pin, out duty, out freq);
            return new PinOutput(driving, level, !driving && level, duty, freq);
        }

        public string ReadSerial()
        {
            if (_txOut.Length == 0) return string.Empty;
            string s = _txOut.ToString();
            _txOut.Clear();
            return s;
        }

        public void WriteSerial(string text)
        {
            foreach (char c in text) _rxQueue.Enqueue((byte)c);
        }

        // ================================================================ I/O dispatch

        private byte ReadPinRegister(int ddr, int port, int inputBase)
        {
            byte d = _d[ddr], p = _d[port];
            int value = 0;
            for (int b = 0; b < 8; b++)
            {
                bool level = (d & (1 << b)) != 0 ? (p & (1 << b)) != 0 : _inputs[inputBase + b];
                if (level) value |= 1 << b;
            }

            if (ddr == DDRC && TwiEnabled) value |= 0x30; // SDA/SCL owned by the TWI: idle bus reads high

            return (byte)value;
        }

        private byte ReadIo(int a)
        {
            switch (a)
            {
                case PINB: return ReadPinRegister(DDRB, PORTB, 8);
                case PINC: return ReadPinRegister(DDRC, PORTC, 16);
                case PIND: return ReadPinRegister(DDRD, PORTD, 0);
                case SREG: return Sreg;
                case TCNT0: return (byte)_t0Count;
                case TCNT2: return (byte)_t2Count;
                case TCNT1L:
                    _t1Temp = _t1Count >> 8;
                    return (byte)_t1Count;
                case TCNT1H:
                case ICR1H:
                case OCR1AH:
                case OCR1BH:
                    return (byte)_t1Temp;
                case ICR1L:
                case OCR1AL:
                case OCR1BL:
                    _t1Temp = _d[a + 1];
                    return _d[a];
                case UDR0:
                    return ReadUdr0();

                case SPDR:
                    _d[SPSR] &= 0x7F;
                    return _d[SPDR];
                default:
                    return _d[a];
            }
        }

        /// <summary>
        /// [registers] A data-space byte as the program would read it, without the read's side effects: SREG from the
        /// flags, PINx from the pins, TCNTx from the counters (TCNT1 both bytes at once, not through the TEMP latch), UDR0
        /// without taking the byte off the receiver, SPDR without clearing SPIF. What a debugger shows;
        /// <see cref="ReadData"/> gives the raw memory behind it.
        /// </summary>
        public byte Peek(int address)
        {
            if (address < 0x20 || address >= 0x100) return ReadData(address);
            switch (address)
            {
                case PINB: return ReadPinRegister(DDRB, PORTB, 8);
                case PINC: return ReadPinRegister(DDRC, PORTC, 16);
                case PIND: return ReadPinRegister(DDRD, PORTD, 0);
                case SREG: return Sreg;
                case TCNT0: return (byte)_t0Count;
                case TCNT2: return (byte)_t2Count;
                case TCNT1L: return (byte)_t1Count;
                case TCNT1H: return (byte)(_t1Count >> 8);
                case UDR0: return PeekUdr0();
                default: return _d[address];
            }
        }

        /// <summary>[registers] The status register's flags (I T H S V N Z C, bit 7 first).</summary>
        public byte StatusRegister => Sreg;

        private void WriteIo(int a, byte v)
        {
            switch (a)
            {
                case PINB: _d[PORTB] ^= v; DetectPinChanges(); LogPorts(); return;
                case PINC: _d[PORTC] ^= v; DetectPinChanges(); LogPorts(); return;
                case PIND: _d[PORTD] ^= v; DetectPinChanges(); LogPorts(); return;
                case DDRB: case PORTB: case DDRC: case PORTC: case DDRD: case PORTD:
                    _d[a] = v;
                    DetectPinChanges();
                    LogPorts();
                    return;
                case SREG: Sreg = v; return;
                case TIFR0: case TIFR1: case TIFR2: case PCIFR: case EIFR:
                    _d[a] &= (byte)~v; // write 1 to clear
                    return;
                case TCNT0: _t0Count = v; return;
                case TCNT2: _t2Count = v; return;
                case TCNT1H: case ICR1H: case OCR1AH: case OCR1BH:
                    _t1Temp = v;
                    return;
                case TCNT1L:
                    _t1Count = (_t1Temp << 8) | v;
                    return;
                case ICR1L: case OCR1AL: case OCR1BL:
                    _d[a] = v;
                    _d[a + 1] = (byte)_t1Temp;
                    return;
                case UCSR0A:
                    _d[a] = (byte)((_d[a] & 0xBC) | (v & 0x03) | ((_d[a] & 0x40) != 0 && (v & 0x40) == 0 ? 0x40 : 0));
                    return;
                case UCSR0B:
                    WriteUcsr0b(v);
                    return;
                case UDR0:
                    UartWrite(v);
                    return;
                case ADCSRA:
                {
                    byte old = _d[a];
                    byte next = (byte)((v & ~0x10) | (old & 0x10));
                    if ((v & 0x10) != 0) next &= unchecked((byte)~0x10); // ADIF: write 1 clears
                    _d[a] = next;
                    if ((next & 0x80) != 0 && (v & 0x40) != 0 && _adcCycles < 0) StartAdc();
                    if ((next & 0x80) == 0) _adcCycles = -1;
                    return;
                }

                case EECR:
                    EepromControl(v);
                    return;
                case SPDR:
                    _d[SPDR] = 0xFF; // no device on the bus answers
                    _d[SPSR] |= 0x80;
                    return;
                case TWCR:
                    TwiControl(v);
                    return;
                case TWSR:
                    TwiWriteStatus(v);
                    return;
                case TWDR:
                    TwiWriteData(v);
                    return;
                case WDTCSR:
                    _d[a] = (byte)(v & ~0x10);
                    if ((v & 0x80) != 0) _d[a] &= 0x7F;
                    _wdtCounter = 0;
                    return;
                default:
                    _d[a] = v;
                    return;
            }
        }

        /// <summary>When set, every DDRx/PORTx/PINx write is recorded with its cycle (for edge-accurate co-simulation).</summary>
        public bool RecordPortWrites { get; set; }

        /// <summary>Port register snapshots taken at each write while <see cref="RecordPortWrites"/> is on.</summary>
        public List<PortSnapshot> PortLog { get; } = new List<PortSnapshot>();

        private void LogPorts()
        {
            if (!RecordPortWrites) return;
            PortLog.Add(new PortSnapshot(Cycles, _d[DDRB], _d[PORTB], _d[DDRC], _d[PORTC], _d[DDRD], _d[PORTD]));
        }

        private void DetectPinChanges()
        {
            byte b = ReadPinRegister(DDRB, PORTB, 8), c = ReadPinRegister(DDRC, PORTC, 16), d = ReadPinRegister(DDRD, PORTD, 0);
            if (((b ^ _lastPinB) & _d[PCMSK0]) != 0) _d[PCIFR] |= 1;
            if (((c ^ _lastPinC) & _d[PCMSK1]) != 0) _d[PCIFR] |= 2;
            if (((d ^ _lastPinD) & _d[PCMSK2]) != 0) _d[PCIFR] |= 4;

            // INT0 = PD2, INT1 = PD3
            for (int n = 0; n < 2; n++)
            {
                int mask = 1 << (2 + n);
                bool was = (_lastPinD & mask) != 0, now = (d & mask) != 0;
                if (was == now) continue;
                int isc = (_d[EICRA] >> (2 * n)) & 3;
                if (isc == 1 || (isc == 2 && was && !now) || (isc == 3 && !was && now)) _d[EIFR] |= (byte)(1 << n);
            }

            if (Trace != null) TracePins(b, c, d);
            _lastPinB = b;
            _lastPinC = c;
            _lastPinD = d;
        }

        /// <summary>[timeline] The pin and interrupt recorder, once <see cref="StartTrace"/> has been called.</summary>
        public McuTrace? Trace { get; private set; }

        /// <summary>[timeline] Starts recording pin edges and interrupts (from the pins' levels now); keeps a running trace.</summary>
        public McuTrace StartTrace(int capacity = 32768)
        {
            if (Trace != null) return Trace;
            var levels = new bool[PinCount];
            for (int pin = 0; pin < PinCount; pin++) levels[pin] = PinLevel(_lastPinB, _lastPinC, _lastPinD, pin);
            Trace = new McuTrace(Cycles, levels, capacity);
            return Trace;
        }

        private static bool PinLevel(byte b, byte c, byte d, int pin) =>
            pin < 8 ? (d & (1 << pin)) != 0 : pin < 14 ? (b & (1 << (pin - 8))) != 0 : (c & (1 << (pin - 14))) != 0;

        private void TracePins(byte b, byte c, byte d)
        {
            if (b == _lastPinB && c == _lastPinC && d == _lastPinD) return;
            for (int pin = 0; pin < PinCount; pin++)
            {
                bool now = PinLevel(b, c, d, pin);
                if (now != PinLevel(_lastPinB, _lastPinC, _lastPinD, pin)) Trace!.Add(new TraceEvent(Cycles, TraceKind.Pin, (byte)pin, now));
            }
        }

        // ================================================================ interrupts

        private int PendingVector()
        {
            byte eimsk = _d[EIMSK];
            if ((eimsk & 1) != 0 && ((_d[EIFR] & 1) != 0 || (((_d[EICRA] & 3) == 0) && (_lastPinD & 0x04) == 0))) return 1;
            if ((eimsk & 2) != 0 && ((_d[EIFR] & 2) != 0 || (((_d[EICRA] >> 2) & 3) == 0 && (_lastPinD & 0x08) == 0))) return 2;
            byte pc = (byte)(_d[PCIFR] & _d[PCICR]);
            if ((pc & 1) != 0) return 3;
            if ((pc & 2) != 0) return 4;
            if ((pc & 4) != 0) return 5;
            if ((_d[WDTCSR] & 0xC0) == 0xC0) return 6;
            byte t2 = (byte)(_d[TIFR2] & _d[TIMSK2]);
            if ((t2 & 2) != 0) return 7;
            if ((t2 & 4) != 0) return 8;
            if ((t2 & 1) != 0) return 9;
            byte t1 = (byte)(_d[TIFR1] & _d[TIMSK1]);
            if ((t1 & 0x20) != 0) return 10;
            if ((t1 & 2) != 0) return 11;
            if ((t1 & 4) != 0) return 12;
            if ((t1 & 1) != 0) return 13;
            byte t0 = (byte)(_d[TIFR0] & _d[TIMSK0]);
            if ((t0 & 2) != 0) return 14;
            if ((t0 & 4) != 0) return 15;
            if ((t0 & 1) != 0) return 16;
            if ((_d[SPSR] & 0x80) != 0 && (_d[SPCR] & 0x80) != 0) return 17;
            byte ua = _d[UCSR0A], ub = _d[UCSR0B];
            if ((ua & 0x80) != 0 && (ub & 0x80) != 0) return 18;
            if ((ua & 0x20) != 0 && (ub & 0x20) != 0) return 19;
            if ((ua & 0x40) != 0 && (ub & 0x40) != 0) return 20;
            if ((_d[ADCSRA] & 0x18) == 0x18) return 21;
            if ((_d[EECR] & 0x08) != 0 && (_d[EECR] & 0x02) == 0) return 22;
            if ((_d[TWCR] & 0x81) == 0x81) return 24;
            return -1;
        }

        private void AcknowledgeInterrupt(int vector)
        {
            switch (vector)
            {
                case 1: _d[EIFR] &= unchecked((byte)~1); break;
                case 2: _d[EIFR] &= unchecked((byte)~2); break;
                case 3: _d[PCIFR] &= unchecked((byte)~1); break;
                case 4: _d[PCIFR] &= unchecked((byte)~2); break;
                case 5: _d[PCIFR] &= unchecked((byte)~4); break;
                case 6:
                    _d[WDTCSR] &= 0x7F;
                    if ((_d[WDTCSR] & 0x08) != 0) _d[WDTCSR] &= unchecked((byte)~0x40); // next timeout resets
                    break;
                case 7: _d[TIFR2] &= unchecked((byte)~2); break;
                case 8: _d[TIFR2] &= unchecked((byte)~4); break;
                case 9: _d[TIFR2] &= unchecked((byte)~1); break;
                case 10: _d[TIFR1] &= unchecked((byte)~0x20); break;
                case 11: _d[TIFR1] &= unchecked((byte)~2); break;
                case 12: _d[TIFR1] &= unchecked((byte)~4); break;
                case 13: _d[TIFR1] &= unchecked((byte)~1); break;
                case 14: _d[TIFR0] &= unchecked((byte)~2); break;
                case 15: _d[TIFR0] &= unchecked((byte)~4); break;
                case 16: _d[TIFR0] &= unchecked((byte)~1); break;
                case 17: _d[SPSR] &= 0x7F; break;
                case 20: _d[UCSR0A] &= unchecked((byte)~0x40); break;
                case 21: _d[ADCSRA] &= unchecked((byte)~0x10); break;
            }
        }

        // ================================================================ time-driven peripherals

        private void AdvancePeripherals(int cycles)
        {
            AdvanceTimer8(cycles, ref _t0Pre, ref _t0Count, ref _t0Up, TCCR0A, TCCR0B, OCR0A, OCR0B, TIFR0, Div01);
            AdvanceTimer8(cycles, ref _t2Pre, ref _t2Count, ref _t2Up, TCCR2A, TCCR2B, OCR2A, OCR2B, TIFR2, Div2);
            AdvanceTimer1(cycles);

            if (_txShift >= 0 || _txBuffer >= 0) AdvanceUartTx(cycles);
            if (_rxQueue.Count > 0) AdvanceUartRx(cycles);
            if (_twiAction != TwiAction.None) AdvanceTwi(cycles);
            if (_adcCycles >= 0)
            {
                _adcCycles -= cycles;
                if (_adcCycles < 0) FinishAdc();
            }

            if (_eempeWindow > 0) _eempeWindow -= cycles;
            if (_eepromBusy > 0)
            {
                _eepromBusy -= cycles;
                if (_eepromBusy <= 0) _d[EECR] &= unchecked((byte)~0x02);
            }

            byte wdt = _d[WDTCSR];
            if ((wdt & 0x48) != 0)
            {
                _wdtCounter += cycles;
                int wdp = (wdt & 7) | ((wdt >> 2) & 8);
                double timeout = 2048.0 * (1 << Math.Min(wdp, 9)) / 128000.0 * ClockHz;
                if (_wdtCounter >= timeout)
                {
                    _wdtCounter = 0;
                    if ((wdt & 0x40) != 0) _d[WDTCSR] |= 0x80;
                    else if ((wdt & 0x08) != 0)
                    {
                        Reset();
                        _d[0x54] |= 0x08; // MCUSR.WDRF
                    }
                }
            }
        }

        private void AdvanceTimer8(int cycles, ref long pre, ref int count, ref bool up, int tccrA, int tccrB, int ocrA, int ocrB, int tifr, int[] divs)
        {
            int div = divs[_d[tccrB] & 7];
            if (div == 0) return;
            pre += cycles;
            if (pre < div) return;
            int wgm = (_d[tccrA] & 3) | ((_d[tccrB] & 8) >> 1);
            int top = wgm == 2 || wgm == 5 || wgm == 7 ? _d[ocrA] : 0xFF;
            bool phase = wgm == 1 || wgm == 5;
            int a = _d[ocrA], b = _d[ocrB];
            while (pre >= div)
            {
                pre -= div;
                if (phase)
                {
                    if (up)
                    {
                        if (++count >= top)
                        {
                            count = top;
                            up = false;
                        }
                    }
                    else if (--count <= 0)
                    {
                        count = 0;
                        up = true;
                        _d[tifr] |= 1;
                    }
                }
                else if (count == top)
                {
                    count = 0;
                    if (wgm != 2 || top == 0xFF) _d[tifr] |= 1;
                }
                else
                {
                    count = (count + 1) & 0xFF;
                }

                if (count == a) _d[tifr] |= 2;
                if (count == b) _d[tifr] |= 4;
            }
        }

        private int Timer1Top(int wgm)
        {
            switch (wgm)
            {
                case 1: case 5: return 0xFF;
                case 2: case 6: return 0x1FF;
                case 3: case 7: return 0x3FF;
                case 4: case 9: case 11: case 15: return _d[OCR1AL] | (_d[OCR1AH] << 8);
                case 8: case 10: case 12: case 14: return _d[ICR1L] | (_d[ICR1H] << 8);
                default: return 0xFFFF;
            }
        }

        private void AdvanceTimer1(int cycles)
        {
            int div = Div01[_d[TCCR1B] & 7];
            if (div == 0) return;
            _t1Pre += cycles;
            if (_t1Pre < div) return;
            int wgm = (_d[TCCR1A] & 3) | ((_d[TCCR1B] >> 1) & 0xC);
            int top = Timer1Top(wgm);
            bool phase = wgm == 1 || wgm == 2 || wgm == 3 || wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11;
            bool ctc = wgm == 4 || wgm == 12;
            int a = _d[OCR1AL] | (_d[OCR1AH] << 8), b = _d[OCR1BL] | (_d[OCR1BH] << 8);
            while (_t1Pre >= div)
            {
                _t1Pre -= div;
                if (phase)
                {
                    if (_t1Up)
                    {
                        if (++_t1Count >= top)
                        {
                            _t1Count = top;
                            _t1Up = false;
                        }
                    }
                    else if (--_t1Count <= 0)
                    {
                        _t1Count = 0;
                        _t1Up = true;
                        _d[TIFR1] |= 1;
                    }
                }
                else if (_t1Count == top)
                {
                    _t1Count = 0;
                    if (!ctc || top == 0xFFFF) _d[TIFR1] |= 1;
                    if (wgm == 12) _d[TIFR1] |= 0x20;
                }
                else
                {
                    _t1Count = (_t1Count + 1) & 0xFFFF;
                }

                if (_t1Count == a) _d[TIFR1] |= 2;
                if (_t1Count == b) _d[TIFR1] |= 4;
            }
        }

        /// <summary>Duty and frequency of a timer-driven output compare pin, if any.</summary>
        private void HardwarePwm(int pin, out double duty, out double freq)
        {
            duty = double.NaN;
            freq = 0;
            int tccrA, ocr, com, div, wgm, top;
            bool phase, isA;
            switch (pin)
            {
                case 6: case 5:
                    isA = pin == 6;
                    tccrA = _d[TCCR0A];
                    com = isA ? (tccrA >> 6) & 3 : (tccrA >> 4) & 3;
                    wgm = (tccrA & 3) | ((_d[TCCR0B] & 8) >> 1);
                    top = wgm == 2 || wgm == 5 || wgm == 7 ? _d[OCR0A] : 0xFF;
                    phase = wgm == 1 || wgm == 5;
                    div = Div01[_d[TCCR0B] & 7];
                    ocr = isA ? _d[OCR0A] : _d[OCR0B];
                    break;
                case 11: case 3:
                    isA = pin == 11;
                    tccrA = _d[TCCR2A];
                    com = isA ? (tccrA >> 6) & 3 : (tccrA >> 4) & 3;
                    wgm = (tccrA & 3) | ((_d[TCCR2B] & 8) >> 1);
                    top = wgm == 2 || wgm == 5 || wgm == 7 ? _d[OCR2A] : 0xFF;
                    phase = wgm == 1 || wgm == 5;
                    div = Div2[_d[TCCR2B] & 7];
                    ocr = isA ? _d[OCR2A] : _d[OCR2B];
                    break;
                case 9: case 10:
                    isA = pin == 9;
                    tccrA = _d[TCCR1A];
                    com = isA ? (tccrA >> 6) & 3 : (tccrA >> 4) & 3;
                    wgm = (tccrA & 3) | ((_d[TCCR1B] >> 1) & 0xC);
                    top = Timer1Top(wgm);
                    phase = wgm == 1 || wgm == 2 || wgm == 3 || wgm == 8 || wgm == 9 || wgm == 10 || wgm == 11;
                    div = Div01[_d[TCCR1B] & 7];
                    ocr = isA ? _d[OCR1AL] | (_d[OCR1AH] << 8) : _d[OCR1BL] | (_d[OCR1BH] << 8);
                    break;
                default:
                    return;
            }

            if (com == 0 || div == 0 || top <= 0) return;
            bool ctcMode = (pin is 5 or 6 or 3 or 11) ? wgm == 2 : (wgm == 4 || wgm == 12);
            if (com == 1)
            {
                // Toggle on compare: a 50 % square wave at half the compare rate (tone-style output).
                if (!ctcMode && !(isA && (wgm == 7 || wgm == 15 || wgm == 5 || wgm == 9))) return;
                duty = 0.5;
                freq = ClockHz / (div * 2.0 * (top + 1));
                return;
            }

            if (ctcMode || wgm == 0) return;
            double d = phase ? (double)ocr / top : (ocr + 1.0) / (top + 1.0);
            if (ocr >= top) d = 1.0;
            if (com == 3) d = 1.0 - d;
            duty = Math.Max(0.0, Math.Min(1.0, d));
            freq = phase ? ClockHz / (div * 2.0 * top) : ClockHz / (div * (top + 1.0));
        }

        // ================================================================ USART0

        /// <summary>[uart] One frame's length in cycles: start, data, parity and stop bits as UCSR0B/C set them.</summary>
        private double UartFrameCycles() => UartBitCycles * UartFrameBits;

        /// <summary>Baud rate the firmware configured (0 when the transmitter is off).</summary>
        public double BaudRate => (_d[UCSR0B] & 0x08) == 0 ? 0 : ClockHz / UartBitCycles;

        /// <summary>
        /// [uart] The frames the transmitter has started, by the cycle their start bit began: the TX pin (PD1) carries them bit by
        /// bit (idle high, start bit low, the data bits least significant first, the parity bit if any, stop bits high), as
        /// the USART drives it.
        /// The co-simulation reads them off and prunes the finished ones.
        /// </summary>
        public List<(ulong Cycle, byte Value)> TxFrames { get; } = new List<(ulong, byte)>();

        /// <summary>[uart] The transmitter is on: it owns PD1, whatever DDRD says.</summary>
        public bool UartTxEnabled => (_d[UCSR0B] & 0x08) != 0;

        /// <summary>[uart] One bit's length in CPU cycles (UBRR0 + 1 times 16, or 8 with U2X0).</summary>
        public double UartBitCycles => ((_d[UCSR0A] & 0x02) != 0 ? 8 : 16) * ((_d[UBRR0L] | ((_d[UBRR0H] & 0x0F) << 8)) + 1.0);

        /// <summary>[uart] The TX line's level at <paramref name="cycle"/>: the bit of the frame being sent then, or idle high.</summary>
        public bool UartTxLevel(ulong cycle)
        {
            double bit = UartBitCycles;
            for (int i = TxFrames.Count - 1; i >= 0; i--)
            {
                var (start, value) = TxFrames[i];
                if (cycle < start) continue;
                return UartTxBit(value, (int)((cycle - start) / bit));
            }

            return true;
        }

        /// <summary>[uart] Bit <paramref name="k"/> of the frame carrying <paramref name="value"/> in the configured format (past its end: idle, high).</summary>
        public bool UartTxBit(byte value, int k)
        {
            int chars = UartCharacterBits, parity = UartParity;
            if (k == 0) return false;                                          // start bit
            if (k <= chars) return (value >> (k - 1) & 1) != 0;                // data, least significant first
            if (parity >= 2 && k == chars + 1)
            {
                int ones = 0;
                for (int b = 0; b < Math.Min(8, chars); b++) ones += (value >> b) & 1;
                return parity == 2 ? ones % 2 != 0 : ones % 2 == 0;            // even: makes the ones even; odd: odd
            }

            return true;                                                       // stop bits, then idle
        }

        private void UartWrite(byte v)
        {
            if ((_d[UCSR0B] & 0x08) == 0) return;
            _d[UCSR0A] &= unchecked((byte)~0x40); // TXC cleared by new data
            if (_txShift < 0)
            {
                _txShift = v;
                TxFrames.Add((Cycles, v));   // [uart] its start bit begins now
                if (TxFrames.Count > 64) TxFrames.RemoveRange(0, TxFrames.Count - 64);   // nobody reads them: keep the latest
                _txCycles = UartFrameCycles();
                _d[UCSR0A] |= 0x20;
            }
            else
            {
                _txBuffer = v;
                _d[UCSR0A] &= unchecked((byte)~0x20);
            }
        }

        private void AdvanceUartTx(int cycles)
        {
            _txCycles -= cycles;
            if (_txCycles > 0) return;
            if (_txShift >= 0) _txOut.Append((char)_txShift);
            if (_txOut.Length > 65536) _txOut.Remove(0, _txOut.Length - 65536);
            if (_txBuffer >= 0)
            {
                _txShift = _txBuffer;
                _txBuffer = -1;
                TxFrames.Add(((ulong)Math.Max(0, (long)Cycles + (long)Math.Round(_txCycles)), (byte)_txShift));   // [uart] right after the last stop bit
                _txCycles += UartFrameCycles();
                _d[UCSR0A] |= 0x20;
            }
            else
            {
                _txShift = -1;
                _d[UCSR0A] |= 0x40; // TXC
            }
        }

        private void AdvanceUartRx(int cycles)
        {
            if ((_d[UCSR0B] & 0x10) == 0) return;
            _rxCycles -= cycles;
            if (_rxCycles > 0) return;
            if (_rxFifo.Count >= 2)
            {
                _rxCycles = 0;
                return; // the buffer is full: the monitor's text waits (it is never lost)
            }

            StoreRx(_rxQueue.Dequeue(), 0);
            _rxCycles = UartFrameCycles();
        }

        // ================================================================ ADC

        private void StartAdc()
        {
            int ps = _d[ADCSRA] & 7;
            int prescaler = ps <= 1 ? 2 : 1 << ps;
            _adcCycles = 13.0 * prescaler;
        }

        private void FinishAdc()
        {
            _adcCycles = -1;
            byte admux = _d[ADMUX];
            int refs = admux >> 6;
            double vref = refs == 1 ? Vcc : refs == 3 ? 1.1 : ArefVoltage;
            int mux = admux & 0x0F;
            double vin = mux < 8 ? _analog[mux] : mux == 14 ? 1.1 : mux == 8 ? 0.314 : 0.0;
            int result = vref <= 0 ? 0 : (int)Math.Floor(vin / vref * 1024.0);
            result = Math.Max(0, Math.Min(1023, result));
            if ((admux & 0x20) != 0)
            {
                _d[ADCL] = (byte)((result & 3) << 6);
                _d[ADCH] = (byte)(result >> 2);
            }
            else
            {
                _d[ADCL] = (byte)result;
                _d[ADCH] = (byte)(result >> 8);
            }

            _d[ADCSRA] = (byte)((_d[ADCSRA] & ~0x40) | 0x10);
            if ((_d[ADCSRA] & 0x20) != 0 && (_d[0x7B] & 7) == 0) StartAdc(); // free running
        }

        // ================================================================ EEPROM

        private void EepromControl(byte v)
        {
            byte cur = _d[EECR];
            int address = (_d[EEARL] | (_d[EEARH] << 8)) & (EepromSize - 1);
            if ((v & 0x04) != 0 && (v & 0x02) == 0)
            {
                _eempeWindow = 4;
                _d[EECR] = (byte)((cur & 0x02) | (v & 0x3C));
                return;
            }

            if ((v & 0x02) != 0 && _eempeWindow > 0 && (cur & 0x02) == 0)
            {
                int mode = (v >> 4) & 3;
                byte value = mode == 1 ? (byte)0xFF : mode == 2 ? (byte)(_eeprom[address] & _d[EEDR]) : _d[EEDR];
                _eeprom[address] = value;
                _eepromBusy = 0.0034 * ClockHz;
                _d[EECR] = (byte)((v & 0x38) | 0x02);
                return;
            }

            if ((v & 0x01) != 0 && (cur & 0x02) == 0) _d[EEDR] = _eeprom[address];
            _d[EECR] = (byte)((cur & 0x02) | (v & 0x38));
        }

        // TWI (I2C) master: see Atmega328P.Twi.cs
    }

    /// <summary>GPIO register state at a cycle.</summary>
    public readonly struct PortSnapshot
    {
        public PortSnapshot(ulong cycle, byte ddrB, byte portB, byte ddrC, byte portC, byte ddrD, byte portD)
        {
            Cycle = cycle;
            DdrB = ddrB;
            PortB = portB;
            DdrC = ddrC;
            PortC = portC;
            DdrD = ddrD;
            PortD = portD;
        }

        public ulong Cycle { get; }
        public byte DdrB { get; }
        public byte PortB { get; }
        public byte DdrC { get; }
        public byte PortC { get; }
        public byte DdrD { get; }
        public byte PortD { get; }

        /// <summary>(driving, level/pull-up) of Arduino pin 0..19.</summary>
        public (bool Output, bool High) Pin(int pin)
        {
            byte ddr, port;
            int bit;
            if (pin < 8) { ddr = DdrD; port = PortD; bit = pin; }
            else if (pin < 14) { ddr = DdrB; port = PortB; bit = pin - 8; }
            else { ddr = DdrC; port = PortC; bit = pin - 14; }
            return ((ddr >> bit & 1) != 0, (port >> bit & 1) != 0);
        }
    }
}
