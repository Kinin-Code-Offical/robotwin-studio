using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Industrial
{
    /// <summary>[rtu] A serial frame format: data bits, parity (0 none, 2 even, 3 odd) and stop bits; "8E1" and the like.</summary>
    public readonly struct SerialFormat
    {
        public SerialFormat(int dataBits, int parity, int stopBits)
        {
            DataBits = Math.Max(5, Math.Min(8, dataBits));
            Parity = parity == 2 || parity == 3 ? parity : 0;
            StopBits = stopBits == 2 ? 2 : 1;
        }

        public int DataBits { get; }
        public int Parity { get; }
        public int StopBits { get; }

        public int FrameBits => 1 + DataBits + (Parity >= 2 ? 1 : 0) + StopBits;

        /// <summary>"8N1", "8E1", "8O1", "8N2", "7E1"…; anything else is 8E1, Modbus RTU's default.</summary>
        public static SerialFormat Parse(string? text)
        {
            string s = (text ?? string.Empty).Trim().ToUpperInvariant();
            if (s.Length != 3 || !char.IsDigit(s[0]) || !char.IsDigit(s[2])) return new SerialFormat(8, 2, 1);
            int parity = s[1] == 'E' ? 2 : s[1] == 'O' ? 3 : 0;
            return new SerialFormat(s[0] - '0', parity, s[2] - '0');
        }

        public override string ToString() => $"{DataBits}{(Parity == 2 ? 'E' : Parity == 3 ? 'O' : 'N')}{StopBits}";
    }

    /// <summary>
    /// [rtu] A device's serial port on two circuit pins: it receives by watching its RX pin's level changes (timed inside the
    /// circuit's steps) and takes each bit at its middle, the way a UART does; it sends by scheduling its TX pin's levels at
    /// the bit edges, so the wire carries the frames bit by bit at the configured baud rate.
    /// </summary>
    public sealed class UartLine
    {
        private readonly List<(double Time, bool Level)> _edges = new List<(double, bool)>();
        private bool _level = true;
        private double _scan;   // the receiver looks for the next start bit from here
        private double _txBusyUntil;

        public UartLine(DigitalPin? rx, DigitalPin? tx, double baud, SerialFormat format)
        {
            Rx = rx;
            Tx = tx;
            Baud = Math.Max(1, baud);
            Format = format;
            if (rx != null) rx.RecordEdges = true;
            if (tx != null) tx.Mode = PinMode.OutputHigh;   // an idle line is high
        }

        public DigitalPin? Rx { get; }
        public DigitalPin? Tx { get; }
        public double Baud { get; }
        public SerialFormat Format { get; }

        public double BitTime => 1.0 / Baud;

        public double CharTime => Format.FrameBits / Baud;

        /// <summary>The characters whose frames the line has finished by <paramref name="now"/>: value, when the stop bit's middle passed, and errors.</summary>
        public List<(byte Value, double Time, bool FrameError, bool ParityError)> Receive(double now)
        {
            var got = new List<(byte, double, bool, bool)>();
            if (Rx == null) return got;
            foreach (var e in Rx.TakeEdges()) _edges.Add(e);
            double bit = BitTime;
            int chars = Format.DataBits, last = 1 + chars + (Format.Parity >= 2 ? 1 : 0);
            while (true)
            {
                int at = -1;
                bool before = _level;
                for (int i = 0; i < _edges.Count; i++)
                {
                    if (_edges[i].Time >= _scan && before && !_edges[i].Level)
                    {
                        at = i;
                        break;
                    }

                    before = _edges[i].Level;
                }

                if (at < 0) break;
                double start = _edges[at].Time, stop = start + (last + 0.5) * bit;
                if (stop > now) break;
                if (LevelAt(start + 0.5 * bit))
                {
                    _scan = start + 1e-9;   // a glitch, not a start bit
                    continue;
                }

                int value = 0, ones = 0;
                for (int k = 0; k < chars; k++)
                {
                    if (!LevelAt(start + (1 + k + 0.5) * bit)) continue;
                    value |= 1 << k;
                    ones++;
                }

                bool parityError = false;
                if (Format.Parity >= 2)
                {
                    if (LevelAt(start + (1 + chars + 0.5) * bit)) ones++;
                    parityError = Format.Parity == 2 ? ones % 2 != 0 : ones % 2 == 0;
                }

                got.Add(((byte)value, stop, !LevelAt(stop), parityError));
                _scan = stop;
            }

            int drop = 0;
            while (drop < _edges.Count && _edges[drop].Time < _scan) _level = _edges[drop++].Level;
            if (drop > 0) _edges.RemoveRange(0, drop);
            return got;
        }

        private bool LevelAt(double t)
        {
            bool level = _level;
            foreach (var (time, l) in _edges)
            {
                if (time > t) break;
                level = l;
            }

            return level;
        }

        /// <summary>Puts <paramref name="bytes"/> on the TX pin back to back from <paramref name="start"/> (or when the last send ends); returns when the last stop bit ends.</summary>
        public double Send(IReadOnlyList<byte> bytes, double start)
        {
            double t = Math.Max(start, _txBusyUntil), bit = BitTime;
            if (Tx == null) return t;
            foreach (byte b in bytes)
            {
                var levels = new List<bool> { false };
                int ones = 0;
                for (int k = 0; k < Format.DataBits; k++)
                {
                    bool one = (b >> k & 1) != 0;
                    levels.Add(one);
                    if (one) ones++;
                }

                if (Format.Parity >= 2) levels.Add(Format.Parity == 2 ? ones % 2 != 0 : ones % 2 == 0);
                for (int s = 0; s < Format.StopBits; s++) levels.Add(true);
                bool current = true;
                for (int k = 0; k < levels.Count; k++)
                {
                    if (levels[k] != current) Tx.Schedule(t + k * bit, levels[k] ? PinMode.OutputHigh : PinMode.OutputLow);
                    current = levels[k];
                }

                t += levels.Count * bit;
            }

            _txBusyUntil = t;
            return t;
        }
    }

    /// <summary>
    /// [rtu] A Modbus RTU slave on a serial line (Modbus over Serial Line, V1.02): a frame is what arrives between silences
    /// of 3.5 characters (1.75 ms above 19 200 baud); one with a broken character, a wrong CRC or another address is
    /// ignored without an answer, a broadcast (address 0) is carried out without one; otherwise the answer (the same
    /// function codes as the TCP server, <see cref="ModbusTcpServer.Answer"/>) goes out after the silence a master waits for.
    /// </summary>
    public sealed class ModbusRtuSlave
    {
        private readonly List<byte> _frame = new List<byte>();
        private bool _broken;
        private double _lastChar = double.NegativeInfinity;

        public ModbusRtuSlave(UartLine line, byte address, ModbusImage image)
        {
            Line = line;
            Address = address;
            Image = image;
        }

        public UartLine Line { get; }
        public byte Address { get; }
        public ModbusImage Image { get; }

        /// <summary>Frames answered, and frames dropped for a broken character or a CRC that does not hold.</summary>
        public long Answered { get; private set; }

        public long CrcErrors { get; private set; }

        public long CharacterErrors { get; private set; }

        /// <summary>The silence that ends a frame: 3.5 characters, at least 1.75 ms.</summary>
        public double Silence => Line.Baud > 19200 ? 1.75e-3 : 3.5 * Line.CharTime;

        public void Step(double now)
        {
            foreach (var (value, time, frameError, parityError) in Line.Receive(now))
            {
                if (_frame.Count > 0 && time - _lastChar > Silence) Finish(_lastChar, now);
                _frame.Add(value);
                _broken |= frameError || parityError;
                _lastChar = time;
            }

            if (_frame.Count > 0 && now - _lastChar >= Silence) Finish(_lastChar, now);
        }

        private void Finish(double end, double now)
        {
            var frame = _frame.ToArray();
            bool broken = _broken;
            _frame.Clear();
            _broken = false;
            if (broken)
            {
                CharacterErrors++;
                return;
            }

            if (frame.Length < 4) return;
            ushort crc = Crc(frame, frame.Length - 2);
            if ((frame[frame.Length - 2] | frame[frame.Length - 1] << 8) != crc)
            {
                CrcErrors++;
                return;
            }

            byte address = frame[0];
            if (address != Address && address != 0) return;
            bool offline;
            lock (Image.Gate) offline = Image.Offline;
            if (offline) return;
            var pdu = new byte[frame.Length - 3];
            Array.Copy(frame, 1, pdu, 0, pdu.Length);
            var reply = ModbusTcpServer.Answer(Image, pdu);
            if (address == 0) return;   // broadcast: carried out, never answered
            var bytes = new List<byte>(reply.Length + 3) { Address };
            bytes.AddRange(reply);
            ushort rc = Crc(bytes.ToArray(), bytes.Count);
            bytes.Add((byte)rc);
            bytes.Add((byte)(rc >> 8));
            Line.Send(bytes, Math.Max(now + Line.BitTime, end + Silence + Line.CharTime));   // never in the past: the pin takes it from its next step
            Answered++;
        }

        /// <summary>Modbus's CRC-16 (polynomial 0xA001 reflected, start 0xFFFF), low byte first on the wire.</summary>
        public static ushort Crc(IReadOnlyList<byte> data, int count)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < count; i++)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; k++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }

            return crc;
        }
    }
}
