using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Devices;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// TWI master of the ATmega328P with real Arduino firmware (bundled Wire library,
    /// sketches under Fixtures/I2cSketches, compiled hex in Fixtures) and the I²C
    /// device models on their own.
    /// </summary>
    [Trait("Category", "Firmware")]
    public class I2cDeviceTests
    {
        private readonly ITestOutputHelper _output;

        public I2cDeviceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // ================================================================ helpers

        private static string LoadFixture(string name)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            if (!File.Exists(path))
            {
                // Fallback when the fixtures were not copied next to the test assembly.
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null && !File.Exists(path))
                {
                    path = Path.Combine(dir.FullName, "tests", "RobotTwin.CoreSim.Tests", "Fixtures", name);
                    if (!File.Exists(path)) path = Path.Combine(dir.FullName, "CoreSim", "tests", "RobotTwin.CoreSim.Tests", "Fixtures", name);
                    dir = dir.Parent;
                }
            }

            Assert.True(File.Exists(path), "Fixture not found: " + name);
            return File.ReadAllText(path);
        }

        /// <summary>Runs the MCU in 1 ms slices until <paramref name="marker"/> appears on Serial.</summary>
        private string RunUntil(Atmega328P mcu, string marker, double maxSeconds)
        {
            var serial = new StringBuilder();
            var watch = Stopwatch.StartNew();
            const double slice = 0.001;
            for (double t = 0; t < maxSeconds; t += slice)
            {
                mcu.Run(slice);
                string chunk = mcu.ReadSerial();
                if (chunk.Length == 0) continue;
                serial.Append(chunk);
                if (serial.ToString().Contains(marker)) break;
            }

            watch.Stop();
            _output.WriteLine($"{marker}: {mcu.Cycles / mcu.ClockHz * 1000.0:F1} ms simulated in {watch.Elapsed.TotalMilliseconds:F0} ms wall clock");
            return serial.ToString();
        }

        private static Atmega328P Boot(string hexFixture, params II2cDevice[] devices)
        {
            var mcu = new Atmega328P();
            mcu.LoadHex(LoadFixture(hexFixture));
            mcu.I2c.Devices.AddRange(devices);
            return mcu;
        }

        private static void Send(I2cBus bus, byte address, params byte[] bytes)
        {
            Assert.True(bus.BeginTransfer(address, false));
            foreach (byte b in bytes) Assert.True(bus.WriteByte(b));
            bus.EndTransfer();
        }

        private static string SerialValue(string serial, string key)
        {
            foreach (string line in serial.Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith(key + "=", StringComparison.Ordinal)) return l.Substring(key.Length + 1);
            }

            throw new Xunit.Sdk.XunitException($"'{key}=' not found in serial output:\n{serial}");
        }

        // ================================================================ firmware: SSD1306

        [Fact]
        public void Ssd1306SketchDrawsBorderCheckerAndPixels()
        {
            var oled = new Ssd1306();
            var mcu = Boot("I2cSsd1306.ino.hex", oled);
            string serial = RunUntil(mcu, "SSD1306 DONE", 2.0);

            Assert.Contains("SSD1306 DONE", serial);
            Assert.Equal(0, mcu.UnknownInstructions);
            Assert.True(oled.DisplayOn);
            Assert.True(oled.ChargePumpEnabled);
            Assert.True(oled.PanelPowered);
            Assert.Equal(0xCF, oled.Contrast);
            Assert.Equal(Ssd1306AddressingMode.Horizontal, oled.AddressingMode);
            Assert.True(oled.SegmentRemap);
            Assert.True(oled.ComScanReversed);
            Assert.Equal(64, oled.MultiplexRatio);
            Assert.Equal(0x12, oled.ComPinsConfig);
            Assert.Equal(100000.0, mcu.TwiClockHz, 6); // setClock(100000) restored after the 400 kHz frame

            static bool Expected(int x, int y)
            {
                if (x == 0 || x == 127 || y == 0 || y == 63) return true;
                if (x >= 16 && x < 24 && y >= 16 && y < 24 && (((x - 16) + (y - 16)) & 1) == 0) return true;
                return (x == 100 && y == 10) || (x == 64 && y == 32) || (x == 5 && y == 50);
            }

            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    Assert.True(Expected(x, y) == oled.GetPixel(x, y), $"pixel ({x},{y}) expected {(Expected(x, y) ? "on" : "off")}");
                }
            }
        }

        // ================================================================ firmware: PCF8574 + HD44780

        [Fact]
        public void LcdSketchPrintsTextNumberAndCustomCharacter()
        {
            var lcd = new Pcf8574Lcd(16, 2, 0x27);
            var mcu = Boot("I2cLcd.ino.hex", lcd);
            string serial = RunUntil(mcu, "LCD DONE", 3.0);

            Assert.Contains("LCD DONE", serial);
            Assert.Equal(0, mcu.UnknownInstructions);
            Assert.Equal("Hello RobotWin  ", lcd.Line(0));
            Assert.Equal("12345\0          ", lcd.Line(1));
            Assert.Equal("Hello RobotWin", lcd.Line(0).TrimEnd());
            Assert.Equal('H', lcd.CharAt(0, 0));
            Assert.Equal((byte)'5', lcd.CodeAt(1, 4));
            Assert.Equal(0, lcd.CodeAt(1, 5));
            Assert.True(lcd.Backlight);
            Assert.True(lcd.DisplayOn);
            Assert.False(lcd.CursorOn);
            Assert.False(lcd.BlinkOn);
            Assert.True(lcd.FourBitMode);
            Assert.True(lcd.TwoLineMode);
            Assert.Equal(0x46, lcd.CursorAddress);

            var heart = new byte[] { 0x00, 0x0A, 0x1F, 0x1F, 0x0E, 0x04, 0x00, 0x00 };
            Assert.Equal(heart, lcd.Cgram.Take(8).ToArray());
            Assert.Equal(heart, lcd.Glyph(lcd.CodeAt(1, 5)));

            // Busy flag + address counter read back through the PCF8574 (RW=1, two nibbles).
            Assert.Equal("46", SerialValue(serial, "STATUS"));
        }

        // ================================================================ firmware: MPU-6050

        [Fact]
        public void Mpu6050SketchReadsWhoAmIAndScaledSensorData()
        {
            var imu = new Mpu6050 { AccelX = 0, AccelY = 0, AccelZ = 9.81, GyroX = 1.0, TemperatureC = 25.0 };
            var mcu = Boot("I2cMpu6050.ino.hex", imu);

            var traffic = new List<(ulong Cycles, int PriorStatus, bool Read, byte Value, bool Ack)>();
            mcu.I2c.Traffic += (address, read, value, ack) => traffic.Add((mcu.Cycles, mcu.ReadData(0xB9) & 0xF8, read, value, ack));

            string serial = RunUntil(mcu, "MPU DONE", 1.0);
            Assert.Contains("MPU DONE", serial);
            Assert.Equal(0, mcu.UnknownInstructions);

            Assert.Equal("0", SerialValue(serial, "AZ0"));      // asleep after power-on: registers are zero
            Assert.Equal("68", SerialValue(serial, "WHO"));
            Assert.Equal("14", SerialValue(serial, "N"));
            Assert.Equal("0", SerialValue(serial, "AX"));
            Assert.Equal("0", SerialValue(serial, "AY"));
            int az = int.Parse(SerialValue(serial, "AZ"));
            Assert.Equal((int)Math.Round(9.81 / Mpu6050.StandardGravity * 16384), az); // 16390
            Assert.InRange(az, 16384 - 50, 16384 + 50);
            Assert.Equal((int)Math.Round((25.0 - 36.53) * 340), int.Parse(SerialValue(serial, "TEMP")));
            Assert.Equal((int)Math.Round(131.0 * 180.0 / Math.PI), int.Parse(SerialValue(serial, "GX")));
            Assert.Equal("0", SerialValue(serial, "GY"));
            Assert.Equal("0", SerialValue(serial, "GZ"));
            Assert.Equal((int)Math.Round(9.81 / Mpu6050.StandardGravity * 8192), int.Parse(SerialValue(serial, "AZ4")));
            Assert.Equal((int)Math.Round(65.5 * 180.0 / Math.PI), int.Parse(SerialValue(serial, "GX500")));
            Assert.Equal("2", SerialValue(serial, "NACK")); // endTransmission: address NACK
            Assert.Equal("0", SerialValue(serial, "RQ"));   // requestFrom on a missing device
            Assert.False(imu.Sleeping);
            Assert.Equal(4, imu.AccelFullScaleG);
            Assert.Equal(500, imu.GyroFullScaleDps);

            // A START + SLA+W + register + value write at 100 kHz: 10 µs + 3 × 90 µs plus ISR overhead.
            int wake = int.Parse(SerialValue(serial, "TWAKE"));
            _output.WriteLine($"wake-up write transaction measured by micros(): {wake} us");
            Assert.InRange(wake, 280, 360);

            // Bytes are 9 SCL periods (1440 cycles at 100 kHz) apart on the bus.
            var gaps = new List<double>();
            for (int i = 1; i < traffic.Count; i++)
            {
                if (traffic[i].PriorStatus == 0x18 || traffic[i].PriorStatus == 0x28 || traffic[i].PriorStatus == 0x50 || traffic[i].PriorStatus == 0x40)
                    gaps.Add(traffic[i].Cycles - traffic[i - 1].Cycles);
            }

            Assert.NotEmpty(gaps);
            _output.WriteLine($"byte-to-byte gap: min {gaps.Min()} max {gaps.Max()} cycles");
            Assert.True(gaps.Min() >= 1440, $"bytes closer than 9 SCL periods: {gaps.Min()} cycles");
            Assert.True(gaps.Min() < 1440 + 400, $"unexpected inter-byte latency: {gaps.Min()} cycles");

            // Register reads use a repeated START (status 0x10) for SLA+R, not STOP + START.
            Assert.Contains(traffic, e => e.PriorStatus == 0x10 && e.Read && e.Value == ((0x68 << 1) | 1) && e.Ack);
            // The missing device at 0x50 NACKs its address.
            Assert.Contains(traffic, e => e.PriorStatus == 0x08 && !e.Read && e.Value == (0x50 << 1) && !e.Ack);
        }

        [Fact]
        public void TwiTakesOverSdaSclPinsWhileEnabled()
        {
            var mcu = Boot("I2cMpu6050.ino.hex", new Mpu6050());
            RunUntil(mcu, "MPU DONE", 1.0);
            Assert.True(mcu.TwiEnabled);
            for (int pin = 18; pin <= 19; pin++)
            {
                var state = mcu.GetPin(pin);
                Assert.False(state.Driving);
                Assert.True(state.Pullup); // twi_init() enables the internal pull-ups
            }
        }

        [Fact]
        public void TwiRegistersPrescalerWriteCollisionAndStartTiming()
        {
            var mcu = new Atmega328P();
            mcu.LoadProgram(
                0xE003, 0x9300, 0x00B9,  // ldi r16,3    ; sts TWSR,r16  (TWPS = 3: status bits stay read-only)
                0xE002, 0x9300, 0x00B8,  // ldi r16,2    ; sts TWBR,r16
                0xEA04, 0x9300, 0x00BC,  // ldi r16,0xA4 ; sts TWCR,r16  (TWINT|TWSTA|TWEN)
                0xEA1A, 0x9310, 0x00BB,  // ldi r17,0xAA ; sts TWDR,r17  (START still running: write collision)
                0xCFFF);                 // rjmp .
            mcu.RunCycles(100);
            Assert.Equal(16e6 / (16 + 2 * 2 * 64), mcu.TwiClockHz, 6);
            Assert.Equal(0xFB, mcu.ReadData(0xB9));          // idle status 0xF8 | TWPS 3
            Assert.Equal(0x08, mcu.ReadData(0xBC) & 0x88);   // TWWC set, TWINT not yet
            Assert.Equal(0xFF, mcu.ReadData(0xBB));          // TWDR write was rejected
            mcu.RunCycles(200);                              // START = one SCL period = 272 cycles
            Assert.Equal(0x80, mcu.ReadData(0xBC) & 0x80);
            Assert.Equal(0x0B, mcu.ReadData(0xB9));          // 0x08 START | TWPS 3
            Assert.True(mcu.TwiEnabled);
        }

        // ================================================================ SSD1306 model

        private static Ssd1306 StandardOled(I2cBus bus, int height = 64)
        {
            var oled = new Ssd1306(height);
            bus.Devices.Add(oled);
            Send(bus, 0x3C, 0x00, 0xAE, 0xA8, (byte)(height - 1), 0xD3, 0x00, 0x40, 0x8D, 0x14, 0x20, 0x00, 0xA1, 0xC8,
                0xDA, (byte)(height == 64 ? 0x12 : 0x02), 0x81, 0x8F, 0xA4, 0xA6, 0xAF);
            return oled;
        }

        [Fact]
        public void Ssd1306HorizontalModeWrapsInsideColumnAndPageWindow()
        {
            var bus = new I2cBus();
            var oled = StandardOled(bus);
            Send(bus, 0x3C, 0x00, 0x21, 10, 12, 0x22, 2, 3);
            Send(bus, 0x3C, 0x40, 1, 2, 3, 4, 5, 6, 7);
            byte[] ram = oled.Gddram;
            Assert.Equal(7, ram[2 * 128 + 10]); // 7th byte wrapped back to (col 10, page 2)
            Assert.Equal(2, ram[2 * 128 + 11]);
            Assert.Equal(3, ram[2 * 128 + 12]);
            Assert.Equal(4, ram[3 * 128 + 10]);
            Assert.Equal(5, ram[3 * 128 + 11]);
            Assert.Equal(6, ram[3 * 128 + 12]);
            Assert.Equal(0, ram[2 * 128 + 13]);
            Assert.Equal(0, ram[4 * 128 + 10]);
            Assert.Equal(11, oled.Column);
            Assert.Equal(2, oled.Page);
        }

        [Fact]
        public void Ssd1306VerticalModeWalksPagesFirst()
        {
            var bus = new I2cBus();
            var oled = StandardOled(bus);
            Send(bus, 0x3C, 0x00, 0x20, 0x01, 0x21, 0, 1, 0x22, 0, 1);
            Send(bus, 0x3C, 0x40, 1, 2, 3, 4, 5);
            byte[] ram = oled.Gddram;
            Assert.Equal(5, ram[0 * 128 + 0]);
            Assert.Equal(2, ram[1 * 128 + 0]);
            Assert.Equal(3, ram[0 * 128 + 1]);
            Assert.Equal(4, ram[1 * 128 + 1]);
            Assert.Equal(Ssd1306AddressingMode.Vertical, oled.AddressingMode);
            Assert.Equal(1, oled.Page);
            Assert.Equal(0, oled.Column);
        }

        [Fact]
        public void Ssd1306PageModeWrapsColumnWithinPage()
        {
            var bus = new I2cBus();
            var oled = StandardOled(bus);
            // page mode, page 3, column 126 (low nibble E, high nibble 7)
            Send(bus, 0x3C, 0x00, 0x20, 0x02, 0xB3, 0x0E, 0x17);
            Assert.Equal(126, oled.Column);
            Send(bus, 0x3C, 0x40, 0xA1, 0xA2, 0xA3);
            byte[] ram = oled.Gddram;
            Assert.Equal(0xA1, ram[3 * 128 + 126]);
            Assert.Equal(0xA2, ram[3 * 128 + 127]);
            Assert.Equal(0xA3, ram[3 * 128 + 0]); // wrapped to column 0, same page
            Assert.Equal(0, ram[4 * 128 + 0]);
            Assert.Equal(3, oled.Page);
            Assert.Equal(1, oled.Column);
        }

        [Fact]
        public void Ssd1306SingleByteControlAndParametersAcrossTransactions()
        {
            var bus = new I2cBus();
            var oled = new Ssd1306();
            bus.Devices.Add(oled);
            // Co=1 control bytes: one command / one data byte each.
            Send(bus, 0x3C, 0x80, 0xB1, 0x80, 0x05, 0xC0, 0x55, 0x80, 0xAF, 0x80, 0xA7);
            Assert.Equal(0x55, oled.Gddram[1 * 128 + 5]);
            Assert.True(oled.DisplayOn);
            Assert.True(oled.Inverted);
            // A command's parameters may arrive in later transactions (Adafruit sends 0x21, 0 then 127).
            Send(bus, 0x3C, 0x00, 0x81);
            Send(bus, 0x3C, 0x00, 0x42);
            Assert.Equal(0x42, oled.Contrast);
            // Scroll setup is recorded.
            Send(bus, 0x3C, 0x00, 0x26, 0x00, 0x00, 0x07, 0x07, 0x00, 0xFF, 0x2F);
            Assert.True(oled.ScrollActive);
            Assert.Equal(new byte[] { 0x26, 0x00, 0x00, 0x07, 0x07, 0x00, 0xFF }, oled.ScrollSetup);
            Send(bus, 0x3C, 0x00, 0x2E);
            Assert.False(oled.ScrollActive);
        }

        [Fact]
        public void Ssd1306NacksOtherAddressesAndHonoursAlternateAddress()
        {
            var bus = new I2cBus();
            bus.Devices.Add(new Ssd1306());
            Assert.False(bus.BeginTransfer(0x3D, false));
            bus.EndTransfer();
            Assert.True(bus.BeginTransfer(0x3C, false));
            bus.EndTransfer();

            var bus2 = new I2cBus();
            bus2.Devices.Add(new Ssd1306(32, 0x3D));
            Assert.False(bus2.BeginTransfer(0x3C, false));
            Assert.True(bus2.BeginTransfer(0x3D, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Ssd1306(48));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Ssd1306(64, 0x3E));
        }

        [Fact]
        public void Ssd1306PanelAppliesRemapScanInvertStartLineAndPower()
        {
            var bus = new I2cBus();
            var oled = StandardOled(bus);
            Send(bus, 0x3C, 0x00, 0x21, 0, 127, 0x22, 0, 7);
            Send(bus, 0x3C, 0x40, 0x01); // RAM column 0, row 0
            Assert.True(oled.GetPixel(0, 0));
            Assert.False(oled.GetPixel(127, 0));
            Assert.False(oled.GetPixel(0, 63));

            int v = oled.Version;
            Send(bus, 0x3C, 0x00, 0xA0); // segment remap off: column 0 shows on the right edge
            Assert.True(oled.Version > v);
            Assert.True(oled.GetPixel(127, 0));
            Send(bus, 0x3C, 0x00, 0xC0); // COM scan normal: row 0 at the bottom
            Assert.True(oled.GetPixel(127, 63));
            Send(bus, 0x3C, 0x00, 0xA1, 0xC8, 0x41); // upright again, start line 1
            Assert.True(oled.GetPixel(0, 63));   // row (63 + 1) & 63 = 0
            Assert.False(oled.GetPixel(0, 0));
            Send(bus, 0x3C, 0x00, 0x40, 0xA7); // inverse
            Assert.False(oled.GetPixel(0, 0));
            Assert.True(oled.GetPixel(1, 0));
            Send(bus, 0x3C, 0x00, 0xA6, 0xA5); // entire display on
            Assert.True(oled.GetPixel(50, 40));
            Send(bus, 0x3C, 0x00, 0xA4, 0xAE); // display off
            Assert.False(oled.GetPixel(0, 0));
            Send(bus, 0x3C, 0x00, 0xAF, 0x8D, 0x10); // on, but charge pump disabled: panel dark
            Assert.False(oled.PanelPowered);
            Assert.False(oled.GetPixel(0, 0));
            oled.ExternalVcc = true;
            Assert.True(oled.GetPixel(0, 0));
        }

        [Fact]
        public void Ssd1306Module128x32UsesSequentialComPins()
        {
            var bus = new I2cBus();
            var oled = StandardOled(bus, 32);
            Assert.Equal(32, oled.Height);
            Send(bus, 0x3C, 0x00, 0x21, 0, 127, 0x22, 0, 3);
            Send(bus, 0x3C, 0x00, 0xB3, 0x05, 0x10); // page 3, column 5
            Send(bus, 0x3C, 0x40, 0x40);              // row 30
            Assert.True(oled.GetPixel(5, 30));
            Assert.False(oled.GetPixel(5, 31));
            Assert.False(oled.GetPixel(5, 32)); // outside the panel
            Send(bus, 0x3C, 0x00, 0xDA, 0x12);   // wrong (128x64) COM pin setting: every other line, half the panel dark
            for (int y = 0; y < 32; y++) Assert.False(oled.GetPixel(5, y), $"row 30 should not be visible (y={y})");
        }

        // ================================================================ PCF8574 / HD44780 models

        /// <summary>LiquidCrystal_I2C byte sequence on the model, without delays.</summary>
        private sealed class LcdDriver
        {
            private readonly I2cBus _bus;
            private readonly byte _address;
            private byte _backlight = 0x08;

            public LcdDriver(I2cBus bus, byte address)
            {
                _bus = bus;
                _address = address;
            }

            public void Init(bool twoLine = true)
            {
                Expander(0);
                Write4(0x30);
                Write4(0x30);
                Write4(0x30);
                Write4(0x20);
                Command((byte)(0x20 | (twoLine ? 0x08 : 0)));
                Command(0x0C);
                Command(0x01);
                Command(0x06);
                Command(0x02);
            }

            public void Command(byte v) => Send(v, 0);

            public void Data(byte v) => Send(v, 1);

            public void Print(string s)
            {
                foreach (char c in s) Data((byte)c);
            }

            public void SetCursor(int col, int row) => Command((byte)(0x80 | (col + new[] { 0x00, 0x40, 0x14, 0x54 }[row])));

            public void Write4(byte v)
            {
                Expander(v);
                Expander((byte)(v | 0x04));
                Expander((byte)(v & ~0x04));
            }

            public byte ReadStatus(bool data = false)
            {
                byte rs = (byte)(data ? 1 : 0);
                Expander((byte)(0xF0 | 0x02 | rs | 0x04));
                byte hi = ReadPins();
                Expander((byte)(0xF0 | 0x02 | rs));
                Expander((byte)(0xF0 | 0x02 | rs | 0x04));
                byte lo = ReadPins();
                Expander((byte)(0xF0 | 0x02 | rs));
                return (byte)((hi & 0xF0) | (lo >> 4));
            }

            private byte ReadPins()
            {
                Assert.True(_bus.BeginTransfer(_address, true));
                byte v = _bus.ReadByte(false);
                _bus.EndTransfer();
                return v;
            }

            private void Send(byte v, byte mode)
            {
                Write4((byte)((v & 0xF0) | mode));
                Write4((byte)(((v << 4) & 0xF0) | mode));
            }

            private void Expander(byte v) => I2cDeviceTests.Send(_bus, _address, (byte)(v | _backlight));
        }

        [Fact]
        public void Pcf8574LatchesOutputsAndReadsQuasiBidirectionalPins()
        {
            var bus = new I2cBus();
            var io = new Pcf8574(0x20);
            bus.Devices.Add(io);
            Assert.Equal(0xFF, io.Port);
            int v = io.Version;
            Send(bus, 0x20, 0x5A);
            Assert.Equal(0x5A, io.Port);
            Assert.True(io.Version > v);
            io.ExternalInputs = 0xF0; // something pulls P0-P3 low
            Assert.True(bus.BeginTransfer(0x20, true));
            Assert.Equal(0x50, bus.ReadByte(false));
            bus.EndTransfer();
            Assert.False(bus.BeginTransfer(0x21, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Pcf8574(0x30));
            Assert.Equal(0x3F, new Pcf8574Lcd(16, 2, 0x3F).Address);
        }

        [Fact]
        public void Hd44780TwentyByFourUsesInterleavedLineAddresses()
        {
            var bus = new I2cBus();
            var lcd = new Pcf8574Lcd(20, 4, 0x27);
            bus.Devices.Add(lcd);
            var drv = new LcdDriver(bus, 0x27);
            drv.Init();
            for (int row = 0; row < 4; row++)
            {
                drv.SetCursor(0, row);
                drv.Print("Row" + row);
            }

            Assert.Equal("Row0", lcd.Line(0).TrimEnd());
            Assert.Equal("Row1", lcd.Line(1).TrimEnd());
            Assert.Equal("Row2", lcd.Line(2).TrimEnd());
            Assert.Equal("Row3", lcd.Line(3).TrimEnd());
            Assert.Equal((byte)'R', lcd.Ddram[0x14]);
            Assert.Equal((byte)'R', lcd.Ddram[0x54]);
            Assert.Equal(20, lcd.Line(3).Length);

            // Writing past the end of row 0 continues on row 2 (same DDRAM line).
            drv.SetCursor(18, 0);
            drv.Print("ABCD");
            Assert.Equal("AB", lcd.Line(0).Substring(18));
            Assert.StartsWith("CD", lcd.Line(2));
            Assert.True(lcd.TryGetCursorCell(out int r, out int c));
            Assert.Equal((2, 2), (r, c));
        }

        [Fact]
        public void Hd44780EntryModeShiftClearHomeAndCursor()
        {
            var bus = new I2cBus();
            var lcd = new Pcf8574Lcd();
            bus.Devices.Add(lcd);
            var drv = new LcdDriver(bus, 0x27);
            drv.Init();
            Assert.True(lcd.Backlight);
            Assert.True(lcd.DisplayOn);

            drv.Command(0x04); // entry mode: decrement
            drv.SetCursor(5, 0);
            drv.Print("abc");
            Assert.Equal("   cba          ", lcd.Line(0));
            Assert.Equal(0x02, lcd.CursorAddress);

            drv.Command(0x18); // display shift left
            Assert.Equal("  cba           ", lcd.Line(0));
            drv.Command(0x1C); // display shift right
            drv.Command(0x1C);
            Assert.Equal("    cba         ", lcd.Line(0));
            drv.Command(0x02); // home: shift undone, address 0
            Assert.Equal("   cba          ", lcd.Line(0));
            Assert.Equal(0, lcd.CursorAddress);

            drv.Command(0x0F); // cursor + blink
            Assert.True(lcd.CursorOn);
            Assert.True(lcd.BlinkOn);
            drv.Command(0x14); // cursor right
            Assert.Equal(1, lcd.CursorAddress);

            drv.Command(0x07); // increment + shift on write: text stays put, display scrolls
            drv.SetCursor(0, 1);
            drv.Print("xy");
            Assert.Equal(2, lcd.DisplayShift);

            drv.Command(0x01); // clear
            Assert.Equal(new string(' ', 16), lcd.Line(0));
            Assert.Equal(new string(' ', 16), lcd.Line(1));
            Assert.Equal(0, lcd.DisplayShift);
            Assert.True(lcd.EntryIncrement);

            drv.Command(0x08); // display off
            Assert.False(lcd.DisplayOn);
        }

        [Fact]
        public void Hd44780ReadsBusyFlagAddressAndDataThroughPcf8574()
        {
            var bus = new I2cBus();
            var lcd = new Pcf8574Lcd();
            bus.Devices.Add(lcd);
            var drv = new LcdDriver(bus, 0x27);
            drv.Init();
            drv.SetCursor(3, 1);
            Assert.Equal(0x43, drv.ReadStatus());
            drv.Print("Z");
            Assert.Equal(0x44, drv.ReadStatus());
            drv.SetCursor(3, 1);
            Assert.Equal((byte)'Z', drv.ReadStatus(data: true)); // data read advances the address
            Assert.Equal(0x44, drv.ReadStatus());

            drv.Command(0x48); // CGRAM address 8 (character 1)
            drv.Data(0x15);
            drv.Data(0x0A);
            Assert.Equal(0x15, lcd.Cgram[8]);
            Assert.Equal(0x0A, lcd.Cgram[9]);
            Assert.True(lcd.AddressInCgram);
            Assert.Equal(10, lcd.CursorAddress);
        }

        [Fact]
        public void Hd44780ReinitialisationRecoversFromNibbleDesync()
        {
            var bus = new I2cBus();
            var lcd = new Pcf8574Lcd();
            bus.Devices.Add(lcd);
            var drv = new LcdDriver(bus, 0x27);
            drv.Init();
            drv.Write4(0x40); // stray single nibble: controller now expects a low nibble
            drv.Init();        // MCU reset without LCD power cycle: 3,3,3,2 resynchronises
            drv.Print("OK");
            Assert.Equal("OK", lcd.Line(0).TrimEnd());
            Assert.True(lcd.FourBitMode);
        }

        [Fact]
        public void Hd44780FontHasA00GlyphsAndUserCharacters()
        {
            Assert.Equal(new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x00 }, Hd44780Font.Glyph((byte)'A'));
            Assert.Equal(new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04, 0x00 }, Hd44780Font.Glyph((byte)'!'));
            Assert.Equal(new byte[8], Hd44780Font.Glyph(0x20));
            Assert.Equal(new byte[8], Hd44780Font.Glyph(0x03)); // user char without CGRAM: blank
            var cgram = new byte[64];
            cgram[3 * 8] = 0x1F;
            Assert.Equal(0x1F, Hd44780Font.Glyph(0x0B, cgram)[0]); // 8-15 mirror 0-7
            Assert.Equal('¥', Hd44780Font.ToChar(0x5C));
            Assert.Equal('→', Hd44780Font.ToChar(0x7E));
            Assert.Equal('°', Hd44780Font.ToChar(0xDF));
            Assert.Equal('█', Hd44780Font.ToChar(0xFF));
            Assert.Equal('ｱ', Hd44780Font.ToChar(0xB1));
            Assert.Equal((char)2, Hd44780Font.ToChar(0x02));
        }

        // ================================================================ MPU-6050 model

        [Fact]
        public void Mpu6050RegisterPointerAutoIncrementsOnWriteAndRead()
        {
            var bus = new I2cBus();
            var imu = new Mpu6050();
            bus.Devices.Add(imu);
            Assert.True(imu.Sleeping);

            Send(bus, 0x68, 0x19, 0x07, 0x03, 0x18, 0x10); // SMPLRT_DIV, CONFIG, GYRO_CONFIG, ACCEL_CONFIG
            Assert.Equal(2000, imu.GyroFullScaleDps);
            Assert.Equal(8, imu.AccelFullScaleG);

            Send(bus, 0x68, 0x19);
            Assert.True(bus.BeginTransfer(0x68, true));
            var read = new[] { bus.ReadByte(true), bus.ReadByte(true), bus.ReadByte(true), bus.ReadByte(false) };
            bus.EndTransfer();
            Assert.Equal(new byte[] { 0x07, 0x03, 0x18, 0x10 }, read);
            Assert.Equal(0x1D, imu.RegisterPointer);

            Send(bus, 0x68, 0x75);
            Assert.True(bus.BeginTransfer(0x68, true));
            Assert.Equal(0x68, bus.ReadByte(false));
            bus.EndTransfer();
            Send(bus, 0x68, 0x75, 0x12); // WHO_AM_I is read-only
            Assert.Equal(0x68, imu.PeekRegister(0x75));
            Assert.False(bus.BeginTransfer(0x69, false));
        }

        private static short ReadWord(I2cBus bus, byte address, byte register)
        {
            Send(bus, address, register);
            Assert.True(bus.BeginTransfer(address, true));
            int hi = bus.ReadByte(true), lo = bus.ReadByte(false);
            bus.EndTransfer();
            return (short)((hi << 8) | lo);
        }

        [Fact]
        public void Mpu6050SleepsUntilWokenAndScalesAndSaturates()
        {
            var bus = new I2cBus();
            var imu = new Mpu6050(0x69) { AccelX = -9.80665, AccelZ = 100.0, GyroZ = -0.5 };
            bus.Devices.Add(imu);
            Assert.Equal(0, ReadWord(bus, 0x69, 0x3B)); // asleep: zeros

            Send(bus, 0x69, 0x6B, 0x00);
            Assert.Equal(-16384, ReadWord(bus, 0x69, 0x3B));
            Assert.Equal(32767, ReadWord(bus, 0x69, 0x3F)); // 100 m/s² saturates at +-2 g
            Assert.Equal((short)Math.Round(-0.5 * 180 / Math.PI * 131), ReadWord(bus, 0x69, 0x47));
            Assert.Equal((short)Math.Round((25.0 - 36.53) * 340), ReadWord(bus, 0x69, 0x41));

            Send(bus, 0x69, 0x1C, 0x18); // +-16 g
            Assert.Equal((short)Math.Round(100.0 / 9.80665 * 2048), ReadWord(bus, 0x69, 0x3F));

            // Back to sleep: registers keep the last sample.
            Send(bus, 0x69, 0x6B, 0x40);
            imu.AccelZ = 0;
            Assert.Equal((short)Math.Round(100.0 / 9.80665 * 2048), ReadWord(bus, 0x69, 0x3F));

            // INT_STATUS.DATA_RDY is set by a fresh sample and cleared by reading it.
            Send(bus, 0x69, 0x6B, 0x00);
            Assert.Equal(1, ReadWord(bus, 0x69, 0x3A) >> 8);
            Assert.Equal(0, imu.PeekRegister(0x3A));

            // DEVICE_RESET restores defaults and sleep.
            Send(bus, 0x69, 0x6B, 0x80);
            Assert.True(imu.Sleeping);
            Assert.Equal(0, imu.PeekRegister(0x1C));
            Assert.Equal(0x68, imu.PeekRegister(0x75));
        }

        [Fact]
        public void Mpu6050NoiseIsDeterministicPerSeed()
        {
            short Sample(int seed)
            {
                var bus = new I2cBus();
                var imu = new Mpu6050 { AccelNoise = 0.5, NoiseSeed = seed };
                imu.Reset();
                bus.Devices.Add(imu);
                Send(bus, 0x68, 0x6B, 0x00);
                return ReadWord(bus, 0x68, 0x3F);
            }

            Assert.Equal(Sample(1), Sample(1));
            short a = Sample(1);
            Assert.InRange(a, 16384 - 3000, 16384 + 3000);
            Assert.NotEqual(16384, a);
        }
    }
}
