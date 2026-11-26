using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [compat] The Arduino API and the AVR core's own libraries on the emulated Uno, from a real avr-gcc build
    /// (Fixtures/arduino-compat.ino): each test sends the sketch a command letter, drives its inputs at exact moments and
    /// checks what it prints and what its pins do (from the chip's own trace). The guide's compatibility table is this.
    /// </summary>
    public class ArduinoCompatTests
    {
        private readonly ITestOutputHelper _out;

        public ArduinoCompatTests(ITestOutputHelper output) => _out = output;

        private sealed class Bench
        {
            private readonly StringBuilder _serial = new StringBuilder();

            public Bench()
            {
                Mcu = new Atmega328P();
                Mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arduino-compat.hex")));
                Trace = Mcu.StartTrace();
                Mcu.SetPinInput(10, true);   // SoftwareSerial's RX line idles high
                Mcu.SetPinInput(2, true);
                Run(0.05);
                Assert.Contains("READY", _serial.ToString());
            }

            public Atmega328P Mcu { get; }
            public McuTrace Trace { get; }

            public double Seconds(ulong cycles) => cycles / Mcu.ClockHz;

            public void Run(double seconds)
            {
                int n = Math.Max(1, (int)Math.Round(seconds / 50e-6));
                for (int i = 0; i < n; i++)
                {
                    Mcu.Run(seconds / n);
                    _serial.Append(Mcu.ReadSerial());
                }
            }

            /// <summary>Sends the command, runs <paramref name="during"/> (given the cycle the command was sent at), and returns the line the test printed.</summary>
            public string Command(char c, double seconds, Action<ulong>? during = null)
            {
                int from = _serial.Length;
                ulong at = Mcu.Cycles;
                Mcu.WriteSerial(c.ToString());
                during?.Invoke(at);
                Run(seconds);
                var lines = _serial.ToString(from, _serial.Length - from).Split('\n');
                return lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(c + " ", StringComparison.Ordinal) || l == c.ToString()) ?? string.Empty;
            }

            public void SendSerial(string text) => Mcu.WriteSerial(text);

            /// <summary>Puts a UART frame stream on an input pin, bit by bit (8N1, idle high).</summary>
            public void DrivePinUart(int pin, string text, double baud)
            {
                double bit = 1 / baud;
                foreach (char ch in text)
                {
                    var levels = new List<bool> { false };
                    for (int k = 0; k < 8; k++) levels.Add((ch >> k & 1) != 0);
                    levels.Add(true);
                    foreach (bool level in levels)
                    {
                        Mcu.SetPinInput(pin, level);
                        Run(bit);
                    }
                }
            }

            /// <summary>Decodes 8N1 frames from a pin's recorded edges between two cycles.</summary>
            public string DecodePinUart(int pin, ulong from, ulong to, double baud)
            {
                var edges = Trace.Edges(pin, from, to);
                double bit = Mcu.ClockHz / baud;
                bool Level(double cycle)
                {
                    bool level = Trace.LevelAt(pin, from);
                    foreach (var (c, l) in edges)
                    {
                        if (c > cycle) break;
                        level = l;
                    }

                    return level;
                }

                var sb = new StringBuilder();
                double scan = from;
                foreach (var (c, l) in edges)
                {
                    if (l || c < scan) continue;
                    int v = 0;
                    for (int k = 0; k < 8; k++)
                        if (Level(c + (1.5 + k) * bit)) v |= 1 << k;
                    sb.Append((char)v);
                    scan = c + 9.5 * bit;
                }

                return sb.ToString();
            }
        }

        [Fact]
        public void ToneMakesItsFrequency()
        {
            var b = new Bench();
            ulong at = b.Mcu.Cycles;
            Assert.Equal("T done", b.Command('T', 0.1));
            var rises = b.Trace.Edges(8, at, b.Mcu.Cycles).Where(e => e.Level).Select(e => e.Cycle).ToList();
            double f = (rises.Count - 1) / b.Seconds(rises.Last() - rises.First());
            _out.WriteLine($"tone(8, 1000): {rises.Count} periods, {f:0.0} Hz");
            Assert.InRange(f, 995, 1005);
            Assert.InRange(rises.Count, 55, 62);   // for its 60 ms
        }

        [Fact]
        public void SoftwareSerialSendsAndReceives()
        {
            var b = new Bench();
            ulong at = b.Mcu.Cycles;
            string line = b.Command('S', 0.1, start =>
            {
                b.Run(0.015);                                 // the command arrives, "Merhaba" goes out on D11
                b.DrivePinUart(10, "Selam", 9600);           // then it hears "Selam" on D10
            });
            string sent = b.DecodePinUart(11, at, b.Mcu.Cycles, 9600);
            _out.WriteLine($"D11 carried '{sent}', it heard: '{line}'");
            Assert.Equal("Merhaba", sent);
            Assert.Equal("S Selam", line);
        }

        [Fact]
        public void PulseInMeasuresAPulse()
        {
            var b = new Bench();
            string line = b.Command('P', 0.05, _ =>
            {
                b.Run(0.005);
                b.Mcu.SetPinInput(7, true);
                b.Run(1234e-6);
                b.Mcu.SetPinInput(7, false);
            });
            _out.WriteLine(line);
            Assert.StartsWith("P ", line);
            Assert.InRange(int.Parse(line.Substring(2)), 1224, 1244);
        }

        [Fact]
        public void AnInterruptCountsFallingEdges()
        {
            var b = new Bench();
            string line = b.Command('I', 0.1, _ =>
            {
                b.Run(0.004);
                for (int i = 0; i < 25; i++)
                {
                    b.Mcu.SetPinInput(2, false);
                    b.Run(0.0005);
                    b.Mcu.SetPinInput(2, true);
                    b.Run(0.0015);
                }
            });
            _out.WriteLine(line);
            Assert.Equal("I 25", line);
        }

        [Fact]
        public void AnalogWriteGivesEachPinItsTimersPwm()
        {
            var b = new Bench();
            Assert.Equal("A set", b.Command('A', 0.05));
            foreach (var (pin, value, hz) in new[] { (3, 32, 490.2), (5, 64, 976.6), (6, 96, 976.6), (9, 128, 490.2), (10, 160, 490.2), (11, 192, 490.2) })
            {
                var p = b.Mcu.GetPin(pin);
                _out.WriteLine($"D{pin}: duty {p.PwmDuty:0.000} at {p.PwmFrequency:0.0} Hz (analogWrite {value})");
                Assert.True(p.IsPwm, $"D{pin} is driven by its timer");
                Assert.InRange(p.PwmFrequency, hz * 0.99, hz * 1.01);
                Assert.InRange(p.PwmDuty, (value - 1) / 256.0, (value + 1.5) / 256.0);
            }
        }

        [Fact]
        public void TimeIsKeptToTheMicrosecond()
        {
            var b = new Bench();
            ulong at = b.Mcu.Cycles;
            string line = b.Command('M', 0.1);
            var rises = b.Trace.Edges(12, at, b.Mcu.Cycles).Where(e => e.Level).Select(e => e.Cycle).ToList();
            double period = b.Seconds(rises.Last() - rises.First()) / (rises.Count - 1) * 1e6;
            _out.WriteLine($"{line} µs over delay(10); D12 period {period:0.0} µs (2 × delayMicroseconds(250) + digitalWrite)");
            Assert.InRange(long.Parse(line.Substring(2)), 10000, 10020);
            Assert.InRange(period, 500, 520);
        }

        [Fact]
        public void EepromKeepsAStruct()
        {
            var b = new Bench();
            Assert.Equal("E 3.25 123456 robot", b.Command('E', 0.08));
        }

        [Fact]
        public void WireScansAnEmptyBus()
        {
            var b = new Bench();
            string line = b.Command('W', 0.3);
            _out.WriteLine(line);
            Assert.Equal("W 0", line);
        }

        [Fact]
        public void WireFindsTheDevicesOnTheBus()
        {
            var b = new Bench();
            b.Mcu.I2c.Devices.Add(new Mpu6050(0x68));
            b.Mcu.I2c.Devices.Add(new Bmp280(0x76, () => 101325, () => 25, 1));
            Assert.Equal("W 2", b.Command('W', 0.3));
        }

        [Fact]
        public void SpiReadsAnMcp3008()
        {
            var b = new Bench();
            var adc = new Mcp3008(ch => ch == 0 ? 1.25 : 4.0, () => 5.0);
            b.Mcu.Spi.Slaves.Add(new SpiSlave("U2", adc, 13, 12, 11, 10, () => true));
            ulong at = b.Mcu.Cycles;
            string line = b.Command('X', 0.05);
            ulong end = b.Mcu.Cycles;

            // the logic analyzer's view: 24 clocks at 1 MHz while CS (D10) is low, MOSI read on SCK's rising edges (mode 0)
            var rises = b.Trace.Edges(13, at, end).Where(e => e.Level).Select(e => e.Cycle).ToList();
            var cs = b.Trace.Edges(10, at, end);
            var mosi = rises.Select(c => b.Trace.LevelAt(11, c)).ToList();
            var bytes = Enumerable.Range(0, mosi.Count / 8).Select(i => Enumerable.Range(0, 8).Aggregate(0, (v, k) => v << 1 | (mosi[i * 8 + k] ? 1 : 0))).ToList();
            _out.WriteLine($"{line}; SCK rose {rises.Count} times, MOSI {string.Join(" ", bytes.Select(x => x.ToString("X2")))}; CS {string.Join(", ", cs.Select(e => $"{(e.Level ? "↑" : "↓")}{b.Seconds(e.Cycle - at) * 1e6:0.0} µs"))}");
            Assert.InRange(int.Parse(line.Substring(2)), 255, 256);   // 1.25 V of 5 V on CH0
            Assert.Equal(24, rises.Count);
            Assert.Equal(new[] { 0x01, 0x80, 0x00 }, bytes);
            Assert.InRange(b.Seconds(rises[7] - rises[0]) * 1e6, 6.99, 7.01);   // 1 µs a bit within a byte
            Assert.Equal(2, cs.Count);
            Assert.True(cs[0].Cycle < rises[0] && rises[^1] < cs[1].Cycle, "the clocks fall inside the chip select");
        }

        [Fact]
        public void AnUnoReadsAnMcp3008WiredInTheCircuit()
        {
            // the runner finds the device on the Uno's own SPI pins through the cables, CH0 on the board's 3.3 V regulator
            var lib = PartLibrary.Default;
            var w = new WorldDocument();
            var uno = w.AddComponent(lib, "arduino-uno", new WorldPose());
            var adc = w.AddComponent(lib, "mcp3008", new WorldPose { X = 0.1 });
            void Wire(WorldObject a, string pa, WorldObject z, string pz) => w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = z.Id, Pin = pz });
            Wire(uno, "5V", adc, "VDD");
            Wire(uno, "5V", adc, "VREF");
            Wire(uno, "GND1", adc, "AGND");
            Wire(uno, "GND2", adc, "DGND");
            Wire(uno, "D13", adc, "CLK");
            Wire(uno, "D11", adc, "DIN");
            Wire(uno, "D12", adc, "DOUT");
            Wire(uno, "D10", adc, "CS");
            Wire(uno, "3V3", adc, "CH0");
            uno.Firmware = new FirmwareSource { FileName = "arduino-compat.hex", Hex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "arduino-compat.hex")) };
            using var runner = new WorldRunner(w, new RunnerOptions());
            runner.Step(0.1);
            runner.WriteSerial("X", null);
            runner.Step(0.1);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            foreach (var e in runner.DrainEvents()) _out.WriteLine($"[{e.Time:0.000}] {e.Source}: {e.Message}");
            _out.WriteLine(text);
            var line = text.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("X ", StringComparison.Ordinal));
            Assert.InRange(int.Parse(line.Substring(2)), 660, 690);   // 3.3 V of 5 V: 676
        }

        [Fact]
        public void SpiWithNoDeviceSelectedReadsTheMisoLine()
        {
            var b = new Bench();
            b.Mcu.SetPinInput(12, true);   // a pull-up on MISO, no device answering
            Assert.Equal("X 1023", b.Command('X', 0.05));
            var c = new Bench();
            c.Mcu.SetPinInput(12, false);
            Assert.Equal("X 0", c.Command('X', 0.05));
        }

        [Fact]
        public void SerialParsesNumbersAndLines()
        {
            var b = new Bench();
            string line = b.Command('R', 0.1, _ => b.SendSerial("123 abc\n"));
            Assert.Equal("R 246 [ abc]", line);
        }

        [Fact]
        public void DigitalAndAnalogInputsRead()
        {
            var b = new Bench();
            b.Mcu.SetPinInput(4, true);   // what the pull-up gives a pin nothing drives (the circuit does this in a co-simulation)
            b.Mcu.SetAnalogInput(0, 2.5);
            string line = b.Command('D', 0.05);
            _out.WriteLine(line);
            Assert.StartsWith("D 1 ", line);
            Assert.InRange(int.Parse(line.Substring(4)), 510, 513);
        }
    }
}
