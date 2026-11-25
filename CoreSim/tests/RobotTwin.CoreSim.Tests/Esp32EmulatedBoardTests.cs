using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [esp32hw] The hardware-compatible mode in a circuit: the esp32-dimmer firmware built for the real chip
    /// (RW_ESP32_ELF, its merged image beside it) runs on the emulated board, a 10k/4.7k divider on IO34 as the pot, LEDs
    /// on IO25 and IO2. Skipped without an ELF.
    /// </summary>
    public class Esp32EmulatedBoardTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly ITestOutputHelper _out;

        public Esp32EmulatedBoardTests(ITestOutputHelper output) => _out = output;

        private static GridPoint PinAt(SchematicPart part, string pin)
        {
            var p = Lib.Get(part.PartId).FindPin(pin) ?? throw new ArgumentException(part.PartId + " has no pin " + pin);
            return part.Transform(p.X, p.Y);
        }

        private static void Label(Schematic sch, SchematicPart part, string pin, string net) =>
            sch.Labels.Add(new NetLabel { Name = net, Position = PinAt(part, pin) });

        /// <summary>A [Fact] skipped unless RW_ESP32_ECHO_ELF names the echo sketch's real-chip ELF (prints 300 characters, then echoes in capitals).</summary>
        public sealed class EchoElfFactAttribute : FactAttribute
        {
            public EchoElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_ECHO_ELF"))) Skip = "RW_ESP32_ECHO_ELF verilmedi.";
            }
        }

        [EchoElfFact]
        public void SerialGoesBothWays()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_ECHO_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated echo" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            esp.Log += s => _out.WriteLine("günlük: " + s);
            void run(double seconds)
            {
                for (int i = 0; i < (int)Math.Round(seconds / 100e-6); i++)
                {
                    esp.Advance(100e-6, sim.Time);
                    sim.Step(100e-6);
                }
            }

            run(0.3);
            string boot = serial.ToString();
            _out.WriteLine(boot);
            Assert.Contains(string.Concat(Enumerable.Repeat("0123456789", 30)), boot);   // longer than the FIFO: the TX-empty interrupt carried it
            Assert.Contains("echo hazir", boot);
            Assert.Equal(115942, esp.SerialBaud, 0);
            serial.Clear();
            esp.SendSerial("merhaba robot" + (char)10);
            run(0.05);
            _out.WriteLine(serial.ToString());
            Assert.Contains("MERHABA ROBOT", serial.ToString());
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_I2C_ELF names the I2C sketch's real-chip ELF (scan, register write/read, a long write).</summary>
        public sealed class I2cElfFactAttribute : FactAttribute
        {
            public I2cElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_I2C_ELF"))) Skip = "RW_ESP32_I2C_ELF verilmedi.";
            }
        }

        /// <summary>A register-file I²C module: the first byte written after a START sets the register pointer, the rest store; reads return from it.</summary>
        private sealed class RegisterDevice : RobotTwin.CoreSim.Firmware.II2cDevice
        {
            public RegisterDevice(byte address) => Address = address;
            public byte Address { get; }
            public readonly byte[] Memory = new byte[256];
            private byte _pointer;
            private bool _first;
            public int Writes;

            public bool Start(bool read)
            {
                _first = !read;
                return true;
            }

            public bool Write(byte value)
            {
                if (_first) _pointer = value;
                else
                {
                    Memory[_pointer++] = value;
                    Writes++;
                }

                _first = false;
                return true;
            }

            public byte Read(bool ack) => Memory[_pointer++];

            public void Stop()
            {
            }
        }

        [I2cElfFact]
        public void WireTalksToTheModulesOnTheBus()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_I2C_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated I2C" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var device = new RegisterDevice(0x48);
            device.Memory[0x11] = 0x5A;
            esp.I2c.Devices.Add(device);
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            esp.Log += s => _out.WriteLine("günlük: " + s);
            for (int i = 0; i < 8000 && !serial.ToString().Contains("i2c bitti"); i++)
            {
                esp.Advance(100e-6, sim.Time);
                sim.Step(100e-6);
            }

            string text = serial.ToString();
            _out.WriteLine(text);
            Assert.Contains("cihaz 0x48", text);
            Assert.DoesNotContain("cihaz 0x50", text);
            Assert.Contains("yazma 0", text);
            Assert.Contains("okunan AB CD", text);
            Assert.Contains("uzun 0", text);
            Assert.Contains("yok 2", text);   // NACK on the address: endTransmission's 2
            Assert.Equal(0xAB, device.Memory[0x10]);
            for (int i = 0; i < 40; i++) Assert.Equal(i, device.Memory[0x20 + i]);
        }

        [Esp32SocBringUpTests.ElfFact]
        public void DimmerFirmwareReadsThePotAndDrivesTheLed()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated dimmer" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            Label(sch, board, "3V3", "VCC");
            var top = sch.AddPart(Lib, "res-10000", new GridPoint(20, 30));
            var bottom = sch.AddPart(Lib, "res-4700", new GridPoint(20, 40));
            Label(sch, top, "A", "VCC");
            Label(sch, top, "B", "POT");
            Label(sch, bottom, "A", "POT");
            Label(sch, bottom, "B", "GND");
            Label(sch, board, "IO34", "POT");
            var r1 = sch.AddPart(Lib, "res-330", new GridPoint(40, 10));
            var led1 = sch.AddPart(Lib, "led5-red", new GridPoint(50, 10));
            Label(sch, board, "IO25", "L1");
            Label(sch, r1, "A", "L1");
            Label(sch, r1, "B", "L1K");
            Label(sch, led1, "A", "L1K");
            Label(sch, led1, "K", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);

            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            esp.Log += s => _out.WriteLine("günlük: " + s);
            const double slice = 100e-6;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 6000; i++)
            {
                esp.Advance(slice, sim.Time);
                sim.Step(slice);
            }

            _out.WriteLine($"0,6 s simülasyon {wall.Elapsed.TotalSeconds:F2} s'de");
            _out.WriteLine(serial.ToString());
            Assert.Equal(Esp32BoardState.Running, esp.State);
            string text = serial.ToString();
            Assert.Contains("rst:0x1 (POWERON_RESET)", text);
            Assert.Contains("ESP32 dimmer hazir", text);

            var pot = compiled.Device<DigitalPin>("U1.IO34")!;
            double potMv = pot.Voltage * 1000;
            var reading = Regex.Matches(text, @"pot=\s*(\d+)\s+(\d+) mV").Cast<Match>().LastOrDefault();
            Assert.NotNull(reading);
            int mv = int.Parse(reading!.Groups[2].Value);
            _out.WriteLine($"pad {potMv:F0} mV, firmware {mv} mV, ham {reading.Groups[1].Value}");
            Assert.InRange(mv, potMv - 5, potMv + 5);

            var led = compiled.Device<DigitalPin>("U1.IO25")!;
            Assert.True(led.PwmActive);
            Assert.Equal(5000, led.PwmFrequency, 3);
            int raw = int.Parse(reading.Groups[1].Value);
            Assert.Equal(raw * 1023 / 4095 / 1024.0, led.PwmDuty, 6);   // map(raw, 0, 4095, 0, 1023), integer, over 10 bits
        }
    }
}
