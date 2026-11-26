using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [uart-rx] The Uno's USART receiving from its RX pin (D0): frames decoded from the line like the chip does, with its
    /// frame, parity and overrun errors. The board runs a real avr-gcc build (Fixtures/uart-flags.ino) that polls the
    /// receiver and prints each byte in hex with the flags it came with (F frame error, O overrun, P parity).
    /// </summary>
    public class AvrUartRxTests
    {
        private const double Slice = 50e-6;
        private readonly ITestOutputHelper _out;

        public AvrUartRxTests(ITestOutputHelper output) => _out = output;

        private static Atmega328P Board(byte format, byte waitMs)
        {
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "uart-flags.hex")));
            mcu.Eeprom[0] = format;
            mcu.Eeprom[1] = waitMs;
            return mcu;
        }

        /// <summary>The line's edges for <paramref name="bytes"/> sent back to back from <paramref name="start"/>.</summary>
        private static List<(ulong Cycle, bool Level)> Line(Atmega328P mcu, ulong start, int chars, int parity, int stops, params (byte Value, bool BadStop, bool BadParity)[] bytes)
        {
            double bit = mcu.ClockHz / 9600.0;
            var levels = new List<bool>();
            foreach (var (value, badStop, badParity) in bytes)
            {
                levels.Add(false);
                int ones = 0;
                for (int k = 0; k < chars; k++)
                {
                    bool b = (value >> k & 1) != 0;
                    levels.Add(b);
                    if (b) ones++;
                }

                if (parity >= 2)
                {
                    bool p = parity == 2 ? ones % 2 != 0 : ones % 2 == 0;
                    levels.Add(badParity ? !p : p);
                }

                for (int s = 0; s < stops; s++) levels.Add(!(badStop && s == 0));
                if (badStop) levels.Add(true);   // the line has to rise again before the next start bit can fall
            }

            levels.Add(true);
            var edges = new List<(ulong, bool)>();
            bool level = true;
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i] == level) continue;
                level = levels[i];
                edges.Add((start + (ulong)Math.Round(i * bit), level));
            }

            return edges;
        }

        /// <summary>Runs the board in co-simulation slices, giving the receiver the line up to each slice's start, as the circuit would.</summary>
        private static string Drive(Atmega328P mcu, List<(ulong Cycle, bool Level)> line, double seconds)
        {
            var text = new StringBuilder();
            int next = 0;
            for (int i = 0; i < (int)Math.Round(seconds / Slice); i++)
            {
                ulong now = mcu.Cycles;
                var known = new List<(ulong, bool)>();
                while (next < line.Count && line[next].Cycle <= now) known.Add(line[next++]);
                mcu.FeedUartRxLine(known, now);
                mcu.Run(Slice);
                text.Append(mcu.ReadSerial());
            }

            return text.ToString();
        }

        private static string Started(Atmega328P mcu)
        {
            string boot = Drive(mcu, new List<(ulong, bool)>(), 0.01);
            Assert.Contains("ok", boot);
            return boot;
        }

        [Fact]
        public void BytesOnTheRxPinAreReceived()
        {
            var mcu = Board(0, 0);
            Started(mcu);
            Assert.False(mcu.GetPin(0).Driving, "the receiver owns D0 as an input");
            var line = Line(mcu, mcu.Cycles + 2000, 8, 0, 1, (0x48, false, false), (0x69, false, false), (0x00, false, false), (0xFF, false, false));
            string got = Drive(mcu, line, 0.03);   // printing three characters per byte takes longer than receiving it
            _out.WriteLine(got);
            Assert.Equal("48 69 00 FF ", got);
        }

        [Fact]
        public void ALowStopBitIsAFrameError()
        {
            var mcu = Board(0, 0);
            Started(mcu);
            var line = Line(mcu, mcu.Cycles + 2000, 8, 0, 1, (0x41, false, false), (0x42, true, false), (0x43, false, false));
            string got = Drive(mcu, line, 0.03);
            _out.WriteLine(got);
            Assert.Equal("41 42F 43 ", got);
        }

        [Theory]
        [InlineData(1, 8, 2, 1)]   // 8E1
        [InlineData(2, 8, 3, 1)]   // 8O1
        [InlineData(3, 7, 2, 2)]   // 7E2
        public void AWrongParityBitIsAParityError(byte format, int chars, int parity, int stops)
        {
            var mcu = Board(format, 0);
            Started(mcu);
            var line = Line(mcu, mcu.Cycles + 2000, chars, parity, stops, (0x41, false, false), (0x37, false, true), (0x2A, false, false));
            string got = Drive(mcu, line, 0.03);
            _out.WriteLine($"format {format}: {got}");
            Assert.Equal("41 37P 2A ", got);
        }

        [Fact]
        public void TheThreeSampleVoteFiltersShortGlitches()
        {
            // 0x55 = 1,0,1,0,… on the line; a low glitch in the middle of every 1 bit. 2 µs spoils one of the three samples
            // (13 µs apart with U2X at 9600 baud): the byte survives. 40 µs covers all three: those bits read 0.
            byte Received(double glitch)
            {
                var mcu = Board(0, 0);
                Started(mcu);
                double bit = mcu.UartBitCycles;
                ulong start = mcu.Cycles + 2000;
                var line = Line(mcu, start, 8, 0, 1, (0x55, false, false));
                foreach (int k in new[] { 1, 3, 5, 7 })   // data bits 0, 2, 4, 6 are the ones
                {
                    double centre = start + (k + 0.5) * bit;
                    line.Add(((ulong)(centre - glitch * mcu.ClockHz / 2), false));
                    line.Add(((ulong)(centre + glitch * mcu.ClockHz / 2), true));
                }

                line.Sort((a, b) => a.Cycle.CompareTo(b.Cycle));
                string got = Drive(mcu, line, 0.01).Trim();
                _out.WriteLine($"{glitch * 1e6:0} µs glitches: {got}");
                return Convert.ToByte(got.Substring(0, 2), 16);
            }

            Assert.Equal(0x55, Received(2e-6));
            Assert.Equal(0x00, Received(40e-6));
        }

        [Fact]
        public void BytesArrivingWhileTheProgramIsBusyOverrun()
        {
            var mcu = Board(0, 4);   // the loop looks only every 4 ms
            Started(mcu);
            var bytes = Encoding.ASCII.GetBytes("ABCDEFGH").Select(b => (b, false, false)).ToArray();
            string got = Drive(mcu, Line(mcu, mcu.Cycles + 2000, 8, 0, 1, bytes), 0.03);
            _out.WriteLine(got);
            var read = got.Trim().Split(' ');
            Assert.Equal("41", read[0]);
            Assert.Contains(read, r => r.EndsWith("O", StringComparison.Ordinal));
            Assert.True(read.Length < 8, "some bytes were lost");
            Assert.Equal("48", read[read.Length - 1].TrimEnd('O'));   // the last one waits in the buffer
        }

        [Fact]
        public void TheSerialMonitorStillGetsThrough()
        {
            var mcu = Board(0, 4);
            Started(mcu);
            mcu.WriteSerial("xyz");   // typed text waits for room: never lost, even to a slow program
            string got = Drive(mcu, new List<(ulong, bool)>(), 0.03);
            Assert.Equal("78 79 7A ", got);
        }

        [Fact]
        public void TheTransmitterSendsTheConfiguredFrame()
        {
            // 8E1: 11 bits a frame; the TX pin's waveform carries the parity bit, and a frame lasts 11 bit times.
            var mcu = Board(1, 0);
            Started(mcu);
            double bit = mcu.UartBitCycles;
            Assert.Equal(11, mcu.UartFrameBits);
            Assert.InRange(mcu.BaudRate, 9550, 9650);
            var frame = mcu.TxFrames.Last();
            var bits = Enumerable.Range(0, 11).Select(k => mcu.UartTxLevel(frame.Cycle + (ulong)((k + 0.5) * bit))).ToArray();
            int ones = Enumerable.Range(0, 8).Count(k => (frame.Value >> k & 1) != 0);
            Assert.False(bits[0]);
            Assert.Equal(ones % 2 != 0, bits[9]);   // even parity
            Assert.True(bits[10]);
        }

        [Fact]
        public void OneUnoTalksToAnotherOverAWire()
        {
            // U1 runs the servo arm (prints "Aci: 180" when the sweep turns, ~0.56 s in), its TX wired to U2's RX.
            var lib = PartLibrary.Default;
            var sch = new Schematic();
            var u1 = sch.AddPart(lib, "arduino-uno", new GridPoint(0, 0));
            var u2 = sch.AddPart(lib, "arduino-uno", new GridPoint(40, 0));
            void Net(SchematicPart part, string pin, string net)
            {
                var pd = lib.Get(part.PartId).FindPin(pin)!;
                sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
            }

            Net(u1, "D1", "LINK");
            Net(u2, "D0", "LINK");
            sch.Labels.Add(new NetLabel { Name = "GND", Position = u1.Transform(-7, -4) });
            sch.Labels.Add(new NetLabel { Name = "GND", Position = u2.Transform(-7, -4) });
            var compiled = CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var talker = new Atmega328P();
            talker.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servo-arm-debug.hex")));
            var listener = Board(0, 0);
            var a = new FirmwareCoSimulation(talker, compiled, u1.RefDes, () => compiled.PinVoltage(sim, u1.RefDes, "5V"));
            var b = new FirmwareCoSimulation(listener, compiled, u2.RefDes, () => compiled.PinVoltage(sim, u2.RefDes, "5V"));
            var heard = new StringBuilder();
            b.SerialOutput += s => heard.Append(s);
            for (int i = 0; i < (int)(0.7 / Slice); i++)
            {
                a.Advance(Slice, sim.Time);
                b.Advance(Slice, sim.Time);
                sim.Step(Slice);
            }

            string got = heard.ToString();
            _out.WriteLine(got);
            // "Aci: 180\r\n" in hex, as U2 received it off the wire
            Assert.Contains("41 63 69 3A 20 31 38 30 0D 0A ", got);
        }
    }
}
