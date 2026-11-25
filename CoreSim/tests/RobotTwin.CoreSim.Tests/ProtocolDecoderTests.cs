using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Instruments;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[decode] UART, I²C and SPI decoded from sampled wires, as a scope's decode option reads them.</summary>
    public class ProtocolDecoderTests
    {
        private const double Dt = 1e-6;   // 1 MSa/s

        /// <summary>Appends <paramref name="seconds"/> of a level (3.3 V or 0) to a sampled wire.</summary>
        private static void Hold(List<double> w, bool high, double seconds)
        {
            int n = (int)Math.Round(seconds / Dt);
            for (int i = 0; i < n; i++) w.Add(high ? 3.3 : 0);
        }

        private static double[] UartLine(string text, double baud, string parity = "none")
        {
            var w = new List<double>();
            double bit = 1 / baud;
            Hold(w, true, 5 * bit);
            foreach (char ch in text)
            {
                Hold(w, false, bit);
                int ones = 0;
                for (int k = 0; k < 8; k++)
                {
                    bool b = (ch >> k & 1) != 0;
                    if (b) ones++;
                    Hold(w, b, bit);
                }

                if (parity == "even") Hold(w, ones % 2 == 1, bit);
                Hold(w, true, 2 * bit);
            }

            Hold(w, true, 3 * bit);
            return w.ToArray();
        }

        [Fact]
        public void UartBytesAndAWrongBaudRateShowAsTheyWould()
        {
            var line = UartLine("Hi!", 9600);
            var fields = ProtocolDecoder.Uart(line, 0, Dt, 9600, 1.65);
            Assert.Equal(new[] { 'H', 'i', '!' }, fields.Select(f => (char)f.Value));
            Assert.All(fields, f => Assert.False(f.Error));
            Assert.Equal("0x48 'H'", fields[0].Text);

            var even = ProtocolDecoder.Uart(UartLine("A", 9600, "even"), 0, Dt, 9600, 1.65, parity: "even");
            Assert.False(Assert.Single(even).Error);
            var odd = ProtocolDecoder.Uart(UartLine("A", 9600, "even"), 0, Dt, 9600, 1.65, parity: "odd");
            Assert.True(Assert.Single(odd).Error);   // read as odd parity: a parity error

            var wrong = ProtocolDecoder.Uart(line, 0, Dt, 19200, 1.65);   // the scope set to twice the line's rate
            Assert.Contains(wrong, f => f.Error);
        }

        [Fact]
        public void AnI2cWriteReadsAsStartAddressDataAcksAndStop()
        {
            // START, 0x3C write, ACK, 0x00, ACK, 0xAF, NACK, STOP at 100 kHz (10 µs a bit).
            var scl = new List<double>();
            var sda = new List<double>();
            void Both(bool c, bool d, double s)
            {
                Hold(scl, c, s);
                Hold(sda, d, s);
            }

            Both(true, true, 20e-6);
            Both(true, false, 5e-6);   // START
            void Byte(int b, bool ack)
            {
                for (int k = 7; k >= -1; k--)
                {
                    bool bit = k >= 0 ? (b >> k & 1) != 0 : !ack;
                    Both(false, bit, 5e-6);
                    Both(true, bit, 5e-6);
                }
            }

            Byte(0x3C << 1, true);
            Byte(0x00, true);
            Byte(0xAF, false);
            Both(false, false, 5e-6);
            Both(true, false, 5e-6);
            Both(true, true, 10e-6);   // STOP
            var f = ProtocolDecoder.I2c(scl.ToArray(), sda.ToArray(), 0, Dt, 1.65);
            Assert.Equal(new[] { "S", "0x3C W", "ACK", "0x00", "ACK", "0xAF", "NACK", "P" }, f.Select(x => x.Text));
            Assert.True(f[6].Error);   // a NACK is flagged
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void SpiWordsAreReadOnTheirModesEdge(int mode)
        {
            var sck = new List<double>();
            var mosi = new List<double>();
            var cs = new List<double>();
            bool cpol = mode >= 2;
            void All(bool k, bool d, bool c, double s)
            {
                Hold(sck, k, s);
                Hold(mosi, d, s);
                Hold(cs, c, s);
            }

            All(cpol, false, true, 10e-6);
            All(cpol, false, false, 5e-6);   // select
            foreach (int b in new[] { 0xA5, 0x5A })
            {
                for (int k = 7; k >= 0; k--)
                {
                    bool bit = (b >> k & 1) != 0;
                    // Data changes on the edge the mode does not sample on, and holds through the one it does.
                    All(!cpol, bit, false, 2e-6);
                    All(cpol, bit, false, 2e-6);
                }
            }

            All(cpol, false, true, 10e-6);
            var f = ProtocolDecoder.Spi(sck.ToArray(), mosi.ToArray(), cs.ToArray(), 0, Dt, 1.65, mode);
            Assert.Equal(new[] { 0xA5, 0x5A }, f.Select(x => x.Value));
        }
    }
}
