using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>
    /// [uart-rx] The USART's receiver as the datasheet builds it (section 20.8): it watches RxD (PD0) for a falling edge,
    /// checks the start bit at its middle, takes each bit by the majority of three samples around the middle, checks the
    /// parity and the first stop bit, and puts the character with its error flags (FE0, DOR0, UPE0) into a two-character
    /// receive buffer. A third character waits in the shift register; a fourth's start bit while both are full loses it,
    /// and the next character read carries DOR0. Disabling the receiver flushes the buffer.
    /// The line comes from the circuit as timed level changes (<see cref="FeedUartRxLine"/>), known up to the slice the
    /// circuit has simulated: a character is decoded once the line up to its stop bit is known, at most one slice after
    /// the stop bit's middle. Text typed into the serial monitor (<see cref="WriteSerial"/>) enters the same buffer one
    /// frame time apart and is never lost.
    /// </summary>
    public sealed partial class Atmega328P
    {
        private const int UCSR0C = 0xC2;
        private const byte Fe0 = 0x10, Dor0 = 0x08, Upe0 = 0x04;
        private readonly Queue<(byte Data, byte Flags)> _rxFifo = new Queue<(byte, byte)>();
        private (byte Data, byte Flags)? _rxShiftFrame;
        private bool _rxOverrun;
        private byte _rxLast;
        private readonly List<(ulong Cycle, bool Level)> _rxEdges = new List<(ulong, bool)>();
        private bool _rxLineLevel = true;
        private ulong _rxScan;

        /// <summary>[uart-rx] The receiver is on (RXEN0): it owns PD0 as an input.</summary>
        public bool UartRxEnabled => (_d[UCSR0B] & 0x10) != 0;

        /// <summary>[uart-rx] Data bits per character (UCSZ0[2:0]: 5–9).</summary>
        public int UartCharacterBits
        {
            get
            {
                int ucsz = ((_d[UCSR0B] & 0x04) != 0 ? 4 : 0) | ((_d[UCSR0C] >> 1) & 3);
                return ucsz switch { 0 => 5, 1 => 6, 2 => 7, 7 => 9, _ => 8 };
            }
        }

        /// <summary>[uart-rx] 0 = none, 2 = even, 3 = odd (UPM0[1:0]).</summary>
        public int UartParity => (_d[UCSR0C] >> 4) & 3;

        /// <summary>[uart-rx] Stop bits the transmitter sends (USBS0); the receiver checks only the first.</summary>
        public int UartStopBits => (_d[UCSR0C] & 0x08) != 0 ? 2 : 1;

        /// <summary>[uart-rx] Bits in one frame: start, data, parity, stop.</summary>
        public int UartFrameBits => 1 + UartCharacterBits + (UartParity >= 2 ? 1 : 0) + UartStopBits;

        /// <summary>[uart-rx] Characters waiting in the receive buffer (0–2).</summary>
        public int UartRxBuffered => _rxFifo.Count;

        /// <summary>
        /// [uart-rx] The RxD line's level changes as the circuit drove them (oldest first, cycles on this core's clock),
        /// with the line known up to <paramref name="knownUntil"/>. <paramref name="lineLevel"/>, when given and nothing is
        /// pending, is the line's level now (the first look at a line that was not watched).
        /// </summary>
        public void FeedUartRxLine(IEnumerable<(ulong Cycle, bool Level)> edges, ulong knownUntil, bool? lineLevel = null)
        {
            if (lineLevel is { } now && _rxEdges.Count == 0) _rxLineLevel = now;
            foreach (var e in edges)
            {
                if (_rxEdges.Count > 0 && e.Cycle < _rxEdges[_rxEdges.Count - 1].Cycle) continue;   // keep it in time order
                _rxEdges.Add(e);
            }

            if (_rxEdges.Count > 8192) _rxEdges.RemoveRange(0, _rxEdges.Count - 8192);
            if (UartRxEnabled) DecodeUartRx(knownUntil);
        }

        private bool RxLevelAt(double cycle)
        {
            bool level = _rxLineLevel;
            foreach (var (c, l) in _rxEdges)
            {
                if (c > cycle) break;
                level = l;
            }

            return level;
        }

        /// <summary>The receiver's majority of three samples around <paramref name="centre"/> (samples 8, 9, 10 of 16; 4, 5, 6 of 8 with U2X).</summary>
        private bool RxSample(double centre, double bit)
        {
            double gap = bit / ((_d[UCSR0A] & 0x02) != 0 ? 8 : 16);
            int high = (RxLevelAt(centre - gap) ? 1 : 0) + (RxLevelAt(centre) ? 1 : 0) + (RxLevelAt(centre + gap) ? 1 : 0);
            return high >= 2;
        }

        private void DecodeUartRx(ulong knownUntil)
        {
            double bit = UartBitCycles;
            int chars = UartCharacterBits, parity = UartParity;
            int lastBit = 1 + chars + (parity >= 2 ? 1 : 0);   // the first stop bit's index
            while (true)
            {
                // The next falling edge after the receiver was last ready: a start bit, if it holds.
                int at = -1;
                bool before = _rxLineLevel;
                for (int i = 0; i < _rxEdges.Count; i++)
                {
                    var (c, l) = _rxEdges[i];
                    if (c >= _rxScan && before && !l)
                    {
                        at = i;
                        break;
                    }

                    before = l;
                }

                if (at < 0) break;
                ulong start = _rxEdges[at].Cycle;
                double stopCentre = start + (lastBit + 0.5) * bit;
                if (stopCentre + bit / 8 > knownUntil) break;   // the frame's line is not all known yet
                if (RxSample(start + 0.5 * bit, bit))
                {
                    _rxScan = start + 1;   // a glitch, not a start bit: look for the next falling edge
                    continue;
                }

                // A start bit while the buffer and the shift register are full: the waiting character is lost.
                if (_rxFifo.Count >= 2 && _rxShiftFrame != null)
                {
                    _rxShiftFrame = null;
                    _rxOverrun = true;
                }

                int data = 0, ones = 0;
                for (int k = 0; k < chars; k++)
                {
                    if (!RxSample(start + (1 + k + 0.5) * bit, bit)) continue;
                    data |= 1 << k;
                    ones++;
                }

                byte flags = 0;
                if (parity >= 2)
                {
                    bool p = RxSample(start + (1 + chars + 0.5) * bit, bit);
                    if (p) ones++;
                    bool even = ones % 2 == 0;
                    if (parity == 2 ? !even : even) flags |= Upe0;
                }

                if (!RxSample(stopCentre, bit)) flags |= Fe0;
                if (_rxOverrun) flags |= Dor0;
                _rxOverrun = false;
                StoreRx((byte)data, flags);
                _rxScan = (ulong)stopCentre;   // ready for the next start bit from the stop bit's middle on
            }

            // Forget the line before where the receiver is now, keeping its level there.
            int drop = 0;
            while (drop < _rxEdges.Count && _rxEdges[drop].Cycle < _rxScan) _rxLineLevel = _rxEdges[drop++].Level;
            if (drop > 0) _rxEdges.RemoveRange(0, drop);
        }

        private void StoreRx(byte data, byte flags)
        {
            if (_rxFifo.Count < 2) _rxFifo.Enqueue((data, flags));
            else _rxShiftFrame = (data, flags);
            SyncRxFlags();
        }

        /// <summary>RXC0 while a character waits; FE0, DOR0 and UPE0 are the first waiting character's.</summary>
        private void SyncRxFlags()
        {
            byte a = (byte)(_d[UCSR0A] & ~(0x80 | Fe0 | Dor0 | Upe0));
            if (_rxFifo.Count > 0) a |= (byte)(0x80 | _rxFifo.Peek().Flags);
            _d[UCSR0A] = a;
        }

        /// <summary>Reading UDR0: the first waiting character; the shift register's moves up behind it.</summary>
        private byte ReadUdr0()
        {
            if (_rxFifo.Count > 0) _rxLast = _rxFifo.Dequeue().Data;
            if (_rxShiftFrame is { } waiting && _rxFifo.Count < 2)
            {
                _rxFifo.Enqueue(waiting);
                _rxShiftFrame = null;
            }

            SyncRxFlags();
            return _rxLast;
        }

        private byte PeekUdr0() => _rxFifo.Count > 0 ? _rxFifo.Peek().Data : _rxLast;

        private void ResetUartRx()
        {
            _rxFifo.Clear();
            _rxShiftFrame = null;
            _rxOverrun = false;
            _rxEdges.Clear();
            _rxLineLevel = true;
            _rxScan = Cycles;
            _d[UCSR0C] = 0x06;   // reset value: asynchronous, no parity, one stop bit, 8 data bits
        }

        private void WriteUcsr0b(byte v)
        {
            bool was = UartRxEnabled;
            _d[UCSR0B] = v;
            if (was && !UartRxEnabled)
            {
                // Disabling the receiver flushes its buffer.
                _rxFifo.Clear();
                _rxShiftFrame = null;
                _rxOverrun = false;
                SyncRxFlags();
            }
            else if (!was && UartRxEnabled)
            {
                _rxScan = Cycles;   // what the line did before the receiver was on is not a frame
            }
        }
    }
}
