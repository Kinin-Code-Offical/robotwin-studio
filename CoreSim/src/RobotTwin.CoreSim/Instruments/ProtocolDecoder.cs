using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotTwin.CoreSim.Instruments
{
    /// <summary>[decode] One decoded piece of a serial bus: from when to when, what (a byte, START, an address), and whether it was wrong.</summary>
    public sealed class DecodedField
    {
        public DecodedField(double start, double end, string text, string kind, bool error = false, int value = -1)
        {
            Start = start;
            End = end;
            Text = text;
            Kind = kind;
            Error = error;
            Value = value;
        }

        public double Start { get; }

        public double End { get; }

        public string Text { get; }

        /// <summary>"data", "start", "stop", "address", "ack", "nack", "parity".</summary>
        public string Kind { get; }

        public bool Error { get; }

        /// <summary>The byte, when the field is one; −1 otherwise.</summary>
        public int Value { get; }
    }

    /// <summary>
    /// [decode] Serial protocol decoders for the oscilloscope, as a scope's decode option reads the probed wires: UART (start
    /// bit, data LSB first, optional parity, stop bit; framing and parity errors), I²C (START/STOP, 7-bit address and R/W,
    /// ACK/NACK, data bytes, repeated START) and SPI (the clock's sampling edge by mode, MSB or LSB first, chip select). They
    /// work on the sampled voltages with a logic threshold, so a slow edge, a glitch or a wrong baud rate shows as it would.
    /// </summary>
    public static class ProtocolDecoder
    {
        private static bool[] Logic(double[] v, double threshold)
        {
            var b = new bool[v.Length];
            for (int i = 0; i < v.Length; i++) b[i] = v[i] > threshold;
            return b;
        }

        private static string Byte(int b) => "0x" + b.ToString("X2", CultureInfo.InvariantCulture) + (b >= 32 && b < 127 ? " '" + (char)b + "'" : string.Empty);

        /// <summary>
        /// UART on one wire sampled every <paramref name="dt"/> s from <paramref name="t0"/>: idle high (low when
        /// <paramref name="inverted"/>), each frame a start bit, <paramref name="dataBits"/> data bits LSB first, an optional
        /// parity bit ("none", "even", "odd") and a stop bit, every bit sampled at its middle.
        /// </summary>
        public static List<DecodedField> Uart(double[] v, double t0, double dt, double baud, double threshold, int dataBits = 8, string parity = "none", bool inverted = false)
        {
            var result = new List<DecodedField>();
            if (v.Length < 2 || baud <= 0 || dt <= 0) return result;
            var level = Logic(v, threshold);
            if (inverted) for (int i = 0; i < level.Length; i++) level[i] = !level[i];
            double bit = 1 / baud / dt;   // samples per bit
            bool Sample(double at)
            {
                int k = (int)Math.Round(at);
                return k >= 0 && k < level.Length && level[k];
            }

            int bits = dataBits + (parity == "none" ? 0 : 1);
            int idx = 1;
            while (idx < level.Length)
            {
                // A start bit: a falling edge after idle high.
                if (!(level[idx - 1] && !level[idx]))
                {
                    idx++;
                    continue;
                }

                double s = idx;
                if (s + (bits + 1.5) * bit >= level.Length) break;   // the frame runs past the record
                if (Sample(s + 0.5 * bit))
                {
                    idx++;   // a glitch, not a start bit
                    continue;
                }

                int value = 0, ones = 0;
                for (int k = 0; k < dataBits; k++)
                {
                    if (!Sample(s + (k + 1.5) * bit)) continue;
                    value |= 1 << k;
                    ones++;
                }

                bool parityError = false;
                if (parity != "none")
                {
                    bool p = Sample(s + (dataBits + 1.5) * bit);
                    if (p) ones++;
                    parityError = parity == "even" ? ones % 2 != 0 : ones % 2 == 0;
                }

                bool stop = Sample(s + (bits + 1.5) * bit);
                double start = t0 + s * dt, end = t0 + (s + (bits + 2) * bit) * dt;
                string text = Byte(value) + (!stop ? " (çerçeve hatası)" : parityError ? " (eşlik hatası)" : string.Empty);
                result.Add(new DecodedField(start, end, text, "data", !stop || parityError, value));
                idx = (int)(s + (bits + 1) * bit + 0.5 * bit);   // past the stop bit's middle: the next start edge
            }

            return result;
        }

        /// <summary>I²C on SCL and SDA: START, the address byte (7 bits and R/W), ACK/NACK after every byte, data bytes, STOP.</summary>
        public static List<DecodedField> I2c(double[] scl, double[] sda, double t0, double dt, double threshold)
        {
            var result = new List<DecodedField>();
            int n = Math.Min(scl.Length, sda.Length);
            if (n < 2 || dt <= 0) return result;
            var c = Logic(scl, threshold);
            var d = Logic(sda, threshold);
            bool inFrame = false;
            int bitCount = 0, value = 0;
            bool first = false;
            double byteStart = 0;
            for (int i = 1; i < n; i++)
            {
                double t = t0 + i * dt;
                if (c[i] && c[i - 1] && d[i - 1] && !d[i])
                {
                    // START (or repeated START): SDA falls while SCL is high.
                    result.Add(new DecodedField(t, t, inFrame ? "Sr" : "S", "start"));
                    inFrame = true;
                    first = true;
                    bitCount = 0;
                    value = 0;
                    continue;
                }

                if (c[i] && c[i - 1] && !d[i - 1] && d[i])
                {
                    result.Add(new DecodedField(t, t, "P", "stop"));
                    inFrame = false;
                    continue;
                }

                if (!inFrame || !(c[i] && !c[i - 1])) continue;   // bits are read on SCL's rising edge
                if (bitCount == 0) byteStart = t;
                if (bitCount < 8)
                {
                    value = (value << 1) | (d[i] ? 1 : 0);
                    bitCount++;
                    continue;
                }

                // The ninth clock: ACK (SDA low) or NACK.
                bool ack = !d[i];
                if (first)
                {
                    int address = value >> 1;
                    bool read = (value & 1) != 0;
                    result.Add(new DecodedField(byteStart, t, "0x" + address.ToString("X2", CultureInfo.InvariantCulture) + (read ? " R" : " W"), "address", false, value));
                    first = false;
                }
                else
                {
                    result.Add(new DecodedField(byteStart, t, Byte(value), "data", false, value));
                }

                result.Add(new DecodedField(t, t, ack ? "ACK" : "NACK", ack ? "ack" : "nack", !ack));
                bitCount = 0;
                value = 0;
            }

            return result;
        }

        /// <summary>
        /// SPI on SCK and one data line (MOSI or MISO), with an optional active-low chip select: SPI mode 0–3 sets the clock's idle
        /// level and the edge data is sampled on; 8-bit words, MSB first unless <paramref name="msbFirst"/> is false.
        /// </summary>
        public static List<DecodedField> Spi(double[] sck, double[] data, double[]? cs, double t0, double dt, double threshold, int mode = 0, bool msbFirst = true)
        {
            var result = new List<DecodedField>();
            int n = Math.Min(sck.Length, data.Length);
            if (cs != null) n = Math.Min(n, cs.Length);
            if (n < 2 || dt <= 0) return result;
            var k = Logic(sck, threshold);
            var d = Logic(data, threshold);
            var sel = cs != null ? Logic(cs, threshold) : null;
            bool cpol = mode >= 2, cpha = mode % 2 == 1;
            // Mode 0 and 3 sample on the rising edge, 1 and 2 on the falling one.
            bool sampleOnRise = cpol == cpha;
            int bits = 0, value = 0;
            double wordStart = 0;
            for (int i = 1; i < n; i++)
            {
                double t = t0 + i * dt;
                if (sel != null && sel[i] && !sel[i - 1])
                {
                    if (bits > 0) result.Add(new DecodedField(wordStart, t, $"{bits} bit (yarım)", "data", true));
                    bits = 0;
                    value = 0;
                    continue;
                }

                if (sel != null && sel[i]) continue;   // not selected
                bool edge = sampleOnRise ? k[i] && !k[i - 1] : !k[i] && k[i - 1];
                if (!edge) continue;
                if (bits == 0) wordStart = t;
                int bit = d[i] ? 1 : 0;
                value = msbFirst ? (value << 1) | bit : value | (bit << bits);
                bits++;
                if (bits < 8) continue;
                result.Add(new DecodedField(wordStart, t, Byte(value), "data", false, value));
                bits = 0;
                value = 0;
            }

            return result;
        }
    }
}
