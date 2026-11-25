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

        /// <summary>A [Fact] skipped unless RW_ESP32_SPI_ELF names the SPI sketch's real-chip ELF.</summary>
        public sealed class SpiElfFactAttribute : FactAttribute
        {
            public SpiElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_SPI_ELF"))) Skip = "RW_ESP32_SPI_ELF verilmedi.";
            }
        }

        /// <summary>An SPI device that answers every byte with its complement and counts its frames and bytes.</summary>
        private sealed class ComplementDevice : RobotTwin.CoreSim.Firmware.ISpiDevice
        {
            public int Frames, DataFrames, Bytes;
            public bool Selected;
            private int _inFrame;

            public void Select(bool selected)
            {
                Selected = selected;
                if (selected)
                {
                    Frames++;
                    _inFrame = 0;
                }
                else if (_inFrame > 0)
                {
                    DataFrames++;
                }
            }

            public byte Transfer(byte mosi)
            {
                Bytes++;
                _inFrame++;
                return (byte)~mosi;
            }
        }

        [SpiElfFact]
        public void SpiReachesTheSelectedDevice()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_SPI_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated SPI" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var device = new ComplementDevice();
            esp.Spi.Slaves.Add(new RobotTwin.CoreSim.Firmware.SpiSlave("D1", device, 18, 19, 23, 5, () => true));
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            esp.Log += s => _out.WriteLine("günlük: " + s);
            for (int i = 0; i < 8000 && !serial.ToString().Contains("spi bitti"); i++)
            {
                esp.Advance(100e-6, sim.Time);
                sim.Step(100e-6);
            }

            string text = serial.ToString();
            _out.WriteLine(text);
            Assert.Contains("spi EF DF CFBF FF D8 bos FF", text);
            // pinMode(5, OUTPUT) drives the pin low until digitalWrite(5, HIGH): an empty select, as a real device sees it.
            Assert.Equal(1, device.DataFrames);
            Assert.Equal(44, device.Bytes);   // 1 + 1 + 2 + 40 while selected; the last byte reaches no one
            Assert.False(device.Selected);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_WDT_ELF names the hung sketch's real-chip ELF (interrupts off, a spin in setup).</summary>
        public sealed class WdtElfFactAttribute : FactAttribute
        {
            public WdtElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_WDT_ELF"))) Skip = "RW_ESP32_WDT_ELF verilmedi.";
            }
        }

        [WdtElfFact]
        public void InterruptWatchdogPanicsAndRestartsAHungBoard()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_WDT_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated watchdog" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            esp.Log += s => _out.WriteLine("günlük: " + s);
            for (int i = 0; i < 20000 && esp.BootCount < 2; i++)
            {
                esp.Advance(100e-6, sim.Time);
                sim.Step(100e-6);
            }

            for (int i = 0; i < 2000; i++)
            {
                esp.Advance(100e-6, sim.Time);
                sim.Step(100e-6);
            }

            string text = serial.ToString();
            _out.WriteLine(text);
            // As on the chip: one core reports (the other stalled), then the board starts again and the sketch with it.
            Assert.Contains("Guru Meditation Error: Core  1 panic'ed (Interrupt wdt timeout on CPU1)", text);
            Assert.DoesNotContain("Cache error", text);
            Assert.True(esp.BootCount >= 2);
            int first = text.IndexOf("bekci testi", StringComparison.Ordinal);
            Assert.True(first >= 0 && text.IndexOf("bekci testi", first + 1, StringComparison.Ordinal) > first);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_SLEEP_ELF names the deep-sleep sketch's real-chip ELF.</summary>
        public sealed class SleepElfFactAttribute : FactAttribute
        {
            public SleepElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_SLEEP_ELF"))) Skip = "RW_ESP32_SLEEP_ELF verilmedi.";
            }
        }

        [SleepElfFact]
        public void DeepSleepKeepsRtcMemoryAndWakesOnTheTimer()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_SLEEP_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated deep sleep" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            esp.Log += s => _out.WriteLine("günlük: " + s);
            bool slept = false;
            for (int i = 0; i < 20000 && !serial.ToString().Contains("uyku bitti"); i++)
            {
                esp.Advance(100e-6, sim.Time);
                sim.Step(100e-6);
                slept |= esp.State == Esp32BoardState.DeepSleep;
            }

            string text = serial.ToString();
            _out.WriteLine(text);
            _out.WriteLine($"bitiş {sim.Time:F3} s");
            Assert.True(slept);
            Assert.Contains("uyandi 1 neden 0", text);   // power-on: no wake cause
            Assert.Contains("rst:0x5 (DEEPSLEEP_RESET)", text);
            Assert.Contains("uyandi 2 neden 4", text);   // ESP_SLEEP_WAKEUP_TIMER, the counter kept in RTC memory
            Assert.Contains("uyandi 3 neden 4", text);
            Assert.Contains("uyku bitti", text);
            Assert.InRange(sim.Time, 0.4, 0.9);          // two 200 ms sleeps and three starts
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_EDGES_ELF names the edge-counting sketch's real-chip ELF.</summary>
        public sealed class EdgesElfFactAttribute : FactAttribute
        {
            public EdgesElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_EDGES_ELF"))) Skip = "RW_ESP32_EDGES_ELF verilmedi.";
            }
        }

        [EdgesElfFact]
        public void AttachInterruptCountsEveryRisingEdge()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_EDGES_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new RobotTwin.CoreSim.Firmware.Xtensa.Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            double volts = 0;
            soc.PadVoltage = gpio => gpio == 4 ? volts : 0;
            soc.Run(240_000L * 300);   // start-up, setup() attaches the interrupt
            Assert.Contains("kesme hazir", soc.Uart0.ToString());
            for (int i = 0; i < 10; i++)
            {
                // 50 Hz square wave on GPIO4: each rising edge goes through the pad's Schmitt trigger, the pin interrupt,
                // the interrupt matrix and IDF's GPIO ISR service to the sketch's handler.
                volts = 3.3;
                soc.SamplePads();
                soc.Run(240_000L * 10);
                volts = 0;
                soc.SamplePads();
                soc.Run(240_000L * 10);
            }

            soc.Run(240_000L * 50);
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            Assert.Contains("kenar 10", text);
            Assert.DoesNotContain("kenar 11", text);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_TOUCH_ELF names the touchRead sketch's real-chip ELF.</summary>
        public sealed class TouchElfFactAttribute : FactAttribute
        {
            public TouchElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_TOUCH_ELF"))) Skip = "RW_ESP32_TOUCH_ELF verilmedi.";
            }
        }

        [TouchElfFact]
        public void TouchReadFollowsAFingerThroughIdfsFilter()
        {
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_TOUCH_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new RobotTwin.CoreSim.Firmware.Xtensa.Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            int count = 75;
            soc.TouchValue = gpio => gpio == 4 ? count : 75;
            soc.Run(240_000L * 200);
            Assert.Contains("dokunma 75", soc.Uart0.ToString());
            int before = soc.Uart0.Length;
            count = 12;   // a finger on T0: IDF's software IIR filter (run every 10 ms by an esp_timer) brings touchRead down to it
            soc.Run(240_000L * 1500);
            string after = soc.Uart0.ToString(before, soc.Uart0.Length - before);
            _out.WriteLine(after);
            var values = Regex.Matches(after, @"dokunma (\d+)").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
            Assert.True(values.Count > 10);
            Assert.True(values[0] > values[values.Count - 1]);   // it falls gradually, as on the chip
            Assert.InRange(values[values.Count - 1], 12, 14);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_PCNT_ELF names the pulse counter sketch's real-chip ELF.</summary>
        public sealed class PcntElfFactAttribute : FactAttribute
        {
            public PcntElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_PCNT_ELF"))) Skip = "RW_ESP32_PCNT_ELF verilmedi.";
            }
        }

        [PcntElfFact]
        public void PulseCounterCountsTheChipsOwnPwm()
        {
            // IDF's pulse_cnt driver counts the rising edges of LEDC's 1 kHz PWM on GPIO18: about one a millisecond, the
            // count accumulated past the ±100 limits by the driver, and the watch point at 100 reached each time.
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_PCNT_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new RobotTwin.CoreSim.Firmware.Xtensa.Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            soc.Run(240_000L * 1600);
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            Assert.Contains("pcnt hazir", text);
            var lines = Regex.Matches(text, @"t=(\d+) sayac=(\d+) esik=(\d+)").Cast<Match>().ToList();
            Assert.True(lines.Count >= 3);
            var last = lines[lines.Count - 1];
            var first = lines[0];
            double ms = int.Parse(last.Groups[1].Value) - int.Parse(first.Groups[1].Value);
            double counted = int.Parse(last.Groups[2].Value) - int.Parse(first.Groups[2].Value);
            Assert.InRange(counted / ms, 0.97, 1.03);                       // 1 kHz: one rising edge a millisecond
            Assert.InRange(int.Parse(last.Groups[3].Value), int.Parse(last.Groups[2].Value) / 100 - 1, int.Parse(last.Groups[2].Value) / 100 + 1);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_LSLEEP_ELF names the light sleep sketch's real-chip ELF.</summary>
        public sealed class LightSleepElfFactAttribute : FactAttribute
        {
            public LightSleepElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_LSLEEP_ELF"))) Skip = "RW_ESP32_LSLEEP_ELF verilmedi.";
            }
        }

        [LightSleepElfFact]
        public void LightSleepHaltsTheChipUntilTheTimerOrAGpioWakesIt()
        {
            // esp_light_sleep_start with a 0.5 s timer and GPIO4's low level as wake sources: while GPIO4 is high the chip
            // sleeps 0.5 s each time (millis() goes on by that, the cause is the timer); pulled low, it wakes at once (GPIO).
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_LSLEEP_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new RobotTwin.CoreSim.Firmware.Xtensa.Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            bool pressed = false;
            soc.PadVoltage = gpio => gpio == 4 && !pressed ? 3.3 : 0;
            int sleptTurns = 0;
            void RunFor(double seconds)
            {
                double end = soc.Seconds + seconds;
                while (soc.Seconds < end)
                {
                    soc.SamplePads();
                    soc.Run((long)Math.Max(1, Math.Ceiling(Math.Min(1e-3, end - soc.Seconds) * soc.CpuHz)));
                    if (soc.LightSleeping) sleptTurns++;
                }
            }

            RunFor(1.6);
            string timerPart = soc.Uart0.ToString();
            pressed = true;
            RunFor(0.05);
            pressed = false;
            RunFor(0.3);
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            Assert.Contains("hafif uyku hazir", timerPart);
            var timerWakes = Regex.Matches(timerPart, @"uyandi neden=(\d+) sure=(\d+)").Cast<Match>().ToList();
            Assert.True(timerWakes.Count >= 2, "slept and woke on the timer");
            foreach (var m in timerWakes)
            {
                Assert.Equal("4", m.Groups[1].Value);                       // ESP_SLEEP_WAKEUP_TIMER
                Assert.InRange(int.Parse(m.Groups[2].Value), 495, 506);     // millis() went on by the sleep
            }

            Assert.Matches(@"uyandi neden=7 sure=\d+", text.Substring(timerPart.Length));   // ESP_SLEEP_WAKEUP_GPIO
            Assert.True(soc.LightSleeps >= 3);
            Assert.True(sleptTurns > 0);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_QUAD_ELF names the quadrature pulse counter sketch's real-chip ELF.</summary>
        public sealed class QuadElfFactAttribute : FactAttribute
        {
            public QuadElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_QUAD_ELF"))) Skip = "RW_ESP32_QUAD_ELF verilmedi.";
            }
        }

        [QuadElfFact]
        public void EmulatedPcntDecodesTheEncoderMotorInTheCircuit()
        {
            // [encoder] The real firmware's pulse_cnt x4 decoding on the emulator's PCNT, fed by a JGA25 encoder motor's
            // C1/C2 in the circuit: from the moment the counter starts, it counts what the encoder turns, edge for edge.
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_QUAD_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var sch = new Schematic { Title = "ESP32 emulated encoder" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            var motor = sch.AddPart(Lib, "motor-jga25-encoder", new GridPoint(40, 0));
            var bat = sch.AddPart(Lib, "bat-4nimh", new GridPoint(70, 0));
            Label(sch, board, "GND2", "GND");
            Label(sch, board, "3V3", "VCC");
            Label(sch, bat, "+", "VM");
            Label(sch, bat, "-", "GND");
            Label(sch, motor, "M1", "VM");
            Label(sch, motor, "M2", "GND");
            Label(sch, motor, "VCC", "VCC");
            Label(sch, motor, "GND", "GND");
            Label(sch, motor, "C1", "ENCA");
            Label(sch, motor, "C2", "ENCB");
            Label(sch, board, "IO4", "ENCA");
            Label(sch, board, "IO5", "ENCB");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var esp = new Esp32EmulatedBoard(File.ReadAllBytes(elf), File.Exists(merged) ? File.ReadAllBytes(merged) : null, compiled, "U1");
            var serial = new StringBuilder();
            esp.SerialOutput += s => serial.Append(s);
            var encoder = compiled.Circuit.Devices.OfType<RobotTwin.CoreSim.Sandbox.Sensing.QuadratureEncoder>().Single();
            long? atStart = null, atLastLine = null;
            int lines = 0;
            const double slice = 100e-6;
            for (int i = 0; i < 9000; i++)
            {
                esp.Advance(slice, sim.Time);
                sim.Step(slice);
                string so = serial.ToString();
                if (atStart == null && so.Contains("pcnt hazir")) atStart = encoder.Count;
                int n = Regex.Matches(so, @"sayac=-?\d+ sinir=\d+\r?\n").Count;
                if (n > lines)
                {
                    lines = n;
                    atLastLine = encoder.Count;   // what the encoder had turned when the line was printed
                }
            }

            string text = serial.ToString();
            _out.WriteLine(text);
            Assert.NotNull(atStart);
            var last = Regex.Matches(text, @"sayac=(-?\d+) sinir=(\d+)\r?\n").Cast<Match>().Last();
            int counted = int.Parse(last.Groups[1].Value);
            long turned = atLastLine!.Value - atStart!.Value;
            _out.WriteLine($"encoder turned {turned} from the counter's start to the last line, counted {counted}");
            Assert.True(Math.Abs(turned) > 400, "the motor turned");
            Assert.InRange(Math.Abs(counted), Math.Abs(turned) - 4, Math.Abs(turned) + 4);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_FLASH_ELF names the Preferences + LittleFS sketch's real-chip ELF.</summary>
        public sealed class FlashElfFactAttribute : FactAttribute
        {
            public FlashElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_FLASH_ELF"))) Skip = "RW_ESP32_FLASH_ELF verilmedi.";
            }
        }

        [FlashElfFact]
        public void PreferencesAndLittleFsReadAndWriteTheFlash()
        {
            // The flash driver takes the chip's size from g_rom_flashchip (esp_rom_spiflash_config_param): NVS keeps a
            // boot counter, LittleFS formats its partition, writes a file and reads it back, and the boot reads the core
            // dump partition without an error.
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_FLASH_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new RobotTwin.CoreSim.Firmware.Xtensa.Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            soc.Run(240_000L * 3000);
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            Assert.DoesNotContain("Failed to read core dump", text);
            Assert.Contains("nvs boots=1", text);
            Assert.Contains("littlefs 'robotwin flash'", text);
        }

        /// <summary>A [Fact] skipped unless RW_ESP32_NEOPIXEL_ELF names the NeoPixel sketch's real-chip ELF.</summary>
        public sealed class NeoPixelElfFactAttribute : FactAttribute
        {
            public NeoPixelElfFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_ESP32_NEOPIXEL_ELF"))) Skip = "RW_ESP32_NEOPIXEL_ELF verilmedi.";
            }
        }

        [NeoPixelElfFact]
        public void RgbLedWriteAndRmtWriteReachTheStrips()
        {
            // rgbLedWrite(5, 10, 20, 30), nine GRB bytes through rmtWrite on GPIO18 (IDF's RMT TX driver, 10 MHz ticks), then
            // three pixels through Adafruit_NeoPixel on GPIO19.
            string elf = Environment.GetEnvironmentVariable("RW_ESP32_NEOPIXEL_ELF")!;
            string merged = Path.ChangeExtension(elf, ".merged.bin");
            var soc = new RobotTwin.CoreSim.Firmware.Xtensa.Esp32Soc();
            soc.Load(File.ReadAllBytes(elf));
            if (File.Exists(merged)) soc.LoadFlash(File.ReadAllBytes(merged));
            var frames = new List<(int Gpio, byte[] Bytes)>();
            soc.RmtFrame += (gpio, bytes) => frames.Add((gpio, bytes));
            soc.Run(240_000L * 300);
            string text = soc.Uart0.ToString();
            _out.WriteLine(text);
            foreach (var (g, b) in frames) _out.WriteLine($"IO{g}: {BitConverter.ToString(b)}");
            Assert.Contains("serit yazildi", text);
            Assert.Contains(frames, f => f.Gpio == 5 && f.Bytes.SequenceEqual(new byte[] { 20, 10, 30 }));
            Assert.Contains(frames, f => f.Gpio == 18 && f.Bytes.SequenceEqual(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99 }));
            // Adafruit_NeoPixel (RobotWin's, on the RMT API) with three GRB pixels on GPIO19: red, green, blue.
            Assert.Contains("kutuphane yazildi", text);
            Assert.Contains(frames, f => f.Gpio == 19 && f.Bytes.SequenceEqual(new byte[] { 0, 255, 0, 255, 0, 0, 0, 0, 255 }));
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
