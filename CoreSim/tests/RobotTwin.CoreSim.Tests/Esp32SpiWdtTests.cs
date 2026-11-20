using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>ESP32 sketches talk to wired SPI devices, and the task watchdog really fires.</summary>
    [Trait("Category", "Firmware")]
    public class Esp32SpiWdtTests
    {
        private readonly ITestOutputHelper _out;

        public Esp32SpiWdtTests(ITestOutputHelper output) => _out = output;

        private static PartLibrary Lib => PartLibrary.Default;

        private string Run(WorldDocument w, WorldObject esp, string sketch, double seconds)
        {
            esp.Firmware = new FirmwareSource { FileName = "test.ino", Source = sketch };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-spi-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var options = new RunnerOptions
            {
                Esp32Builder = src =>
                {
                    var build = new Esp32SketchCompiler().Compile(src, dir);
                    Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);
                    return build.DllPath;
                }
            };
            using var runner = new WorldRunner(w, options);
            runner.Step(seconds);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            foreach (var e in runner.DrainEvents()) _out.WriteLine($"[{e.Time:0.000}] {e.Source}: {e.Message}");
            _out.WriteLine(text);
            return text;
        }

        private static void Wire(WorldDocument w, WorldObject a, string pa, WorldObject b, string pb) =>
            w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb });

        [Esp32Fact]
        public void Mcp3008ReadsTheCircuitVoltagesOverSpi()
        {
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            var adc = w.AddComponent(Lib, "mcp3008", new WorldPose { X = 0.1 });
            Wire(w, esp, "3V3", adc, "VDD");
            Wire(w, esp, "3V3", adc, "VREF");
            Wire(w, esp, "GND1", adc, "AGND");
            Wire(w, esp, "GND2", adc, "DGND");
            Wire(w, esp, "IO18", adc, "CLK");
            Wire(w, esp, "IO23", adc, "DIN");
            Wire(w, esp, "IO19", adc, "DOUT");
            Wire(w, esp, "IO5", adc, "CS");
            Wire(w, esp, "3V3", adc, "CH0");
            Wire(w, esp, "GND3", adc, "CH1");
            Wire(w, esp, "IO25", adc, "CH2");   // the ESP32's own DAC output
            const string sketch = @"
#include <SPI.h>
int readCh(int ch) {
  digitalWrite(5, LOW);
  SPI.transfer(0x01);
  uint8_t hi = SPI.transfer(0x80 | (ch << 4));
  uint8_t lo = SPI.transfer(0x00);
  digitalWrite(5, HIGH);
  return ((hi & 0x03) << 8) | lo;
}
void setup() {
  Serial.begin(115200);
  pinMode(5, OUTPUT); digitalWrite(5, HIGH);
  dacWrite(25, 128);
  SPI.begin();
  SPI.beginTransaction(SPISettings(1000000, MSBFIRST, SPI_MODE0));
  delay(20);
  Serial.printf(""ch0=%d ch1=%d ch2=%d\n"", readCh(0), readCh(1), readCh(2));
  SPI.endTransaction();
  SPI.beginTransaction(SPISettings(8000000, MSBFIRST, SPI_MODE0));
  Serial.printf(""fast ch0=%d\n"", readCh(0));
  SPI.endTransaction();
}
void loop() { delay(100); }
";
            string text = Run(w, esp, sketch, 0.6);
            var line = text.Split('\n').First(l => l.StartsWith("ch0=", StringComparison.Ordinal));
            int Val(string key) => int.Parse(line.Split(key + "=")[1].Split(' ')[0].Trim());
            Assert.InRange(Val("ch0"), 1015, 1023);     // CH0 = VREF
            Assert.InRange(Val("ch1"), 0, 3);           // grounded
            Assert.InRange(Val("ch2"), 470, 560);       // DAC 128/255 × 3.3 V ≈ 1.65 V → ≈ 512
            // 8 MHz is above the MCP3008's 3.6 MHz limit (even less at 3.3 V): the reading is garbage.
            int fast = int.Parse(text.Split("fast ch0=")[1].Split('\n')[0].Trim());
            Assert.NotEqual(Val("ch0"), fast);
        }

        [Esp32Fact]
        public void Bmp280OverSpiReturnsItsIdAndTheAirPressureAtItsHeight()
        {
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            esp.IsStatic = true;
            var baro = w.AddComponent(Lib, "bmp280", new WorldPose { X = 0.1, Z = 100 });
            baro.IsStatic = true;   // on a 100 m mast
            Wire(w, esp, "3V3", baro, "VCC");
            Wire(w, esp, "GND1", baro, "GND");
            Wire(w, esp, "IO18", baro, "SCL");
            Wire(w, esp, "IO23", baro, "SDA");
            Wire(w, esp, "IO19", baro, "SDO");
            Wire(w, esp, "IO5", baro, "CSB");
            const string sketch = @"
#include <SPI.h>
uint8_t rd(int cs, uint8_t reg) { digitalWrite(cs, LOW); SPI.transfer(reg | 0x80); uint8_t v = SPI.transfer(0); digitalWrite(cs, HIGH); return v; }
void wr(uint8_t reg, uint8_t v) { digitalWrite(5, LOW); SPI.transfer(reg & 0x7F); SPI.transfer(v); digitalWrite(5, HIGH); }
uint16_t u16(uint8_t r) { return rd(5, r) | (rd(5, r + 1) << 8); }
int16_t s16(uint8_t r) { return (int16_t)u16(r); }
void setup() {
  Serial.begin(115200);
  pinMode(5, OUTPUT); digitalWrite(5, HIGH);
  pinMode(4, OUTPUT); digitalWrite(4, HIGH);
  SPI.begin();
  SPI.beginTransaction(SPISettings(4000000, MSBFIRST, SPI_MODE0));
  Serial.printf(""id=0x%02X wrongcs=0x%02X\n"", rd(5, 0xD0), rd(4, 0xD0));
  uint16_t T1 = u16(0x88); int16_t T2 = s16(0x8A), T3 = s16(0x8C);
  uint16_t P1 = u16(0x8E); int16_t P2 = s16(0x90), P3 = s16(0x92), P4 = s16(0x94), P5 = s16(0x96), P6 = s16(0x98), P7 = s16(0x9A), P8 = s16(0x9C), P9 = s16(0x9E);
  wr(0xF4, (5 << 5) | (5 << 2) | 1);   // forced, x16 / x16
  delay(50);
  int32_t adcP = ((int32_t)rd(5, 0xF7) << 12) | ((int32_t)rd(5, 0xF8) << 4) | (rd(5, 0xF9) >> 4);
  int32_t adcT = ((int32_t)rd(5, 0xFA) << 12) | ((int32_t)rd(5, 0xFB) << 4) | (rd(5, 0xFC) >> 4);
  int32_t v1 = ((((adcT >> 3) - ((int32_t)T1 << 1))) * ((int32_t)T2)) >> 11;
  int32_t v2 = (((((adcT >> 4) - ((int32_t)T1)) * ((adcT >> 4) - ((int32_t)T1))) >> 12) * ((int32_t)T3)) >> 14;
  int32_t tfine = v1 + v2;
  int64_t a = ((int64_t)tfine) - 128000, b = a * a * (int64_t)P6;
  b = b + ((a * (int64_t)P5) << 17); b = b + (((int64_t)P4) << 35);
  a = ((a * a * (int64_t)P3) >> 8) + ((a * (int64_t)P2) << 12);
  a = (((((int64_t)1) << 47) + a)) * ((int64_t)P1) >> 33;
  int64_t p = 1048576 - adcP; p = (((p << 31) - b) * 3125) / a;
  a = (((int64_t)P9) * (p >> 13) * (p >> 13)) >> 25; b = (((int64_t)P8) * p) >> 19;
  p = ((p + a + b) >> 8) + (((int64_t)P7) << 4);
  Serial.printf(""T=%ld P=%ld\n"", (long)((tfine * 5 + 128) >> 8), (long)(p / 256));
}
void loop() { delay(100); }
";
            string text = Run(w, esp, sketch, 0.5);
            Assert.Contains("id=0x58", text);
            Assert.Contains("wrongcs=0xFF", text);   // CS on an unwired pin: nothing answers, MISO floats high
            var line = text.Split('\n').First(l => l.StartsWith("T=", StringComparison.Ordinal));
            double t = int.Parse(line.Split("T=")[1].Split(' ')[0]) / 100.0;
            double p = int.Parse(line.Split("P=")[1].Trim());
            double expected = Bmp280.PressureAt(100);
            _out.WriteLine($"T {t} °C, P {p} Pa, ISA at 100 m {expected:0} Pa");
            Assert.InRange(t, w.Environment.AmbientC - 0.1, w.Environment.AmbientC + 0.1);
            Assert.InRange(p, expected - 8, expected + 8);
        }

        [Esp32Fact]
        public void TaskWatchdogRebootsAStarvedTaskWithResetReasonTaskWdt()
        {
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            const string sketch = @"
#include <esp_task_wdt.h>
int n = 0;
void setup() {
  Serial.begin(115200);
  Serial.printf(""boot reason=%d\n"", (int)esp_reset_reason());
  esp_task_wdt_config_t c = { 1000, 0, true };
  Serial.printf(""init=0x%x\n"", (int)esp_task_wdt_init(&c));
  Serial.printf(""reconf=0x%x add=0x%x\n"", (int)esp_task_wdt_reconfigure(&c), (int)esp_task_wdt_add(NULL));
}
void loop() {
  if (n++ < 4) { esp_task_wdt_reset(); delay(300); }
  else { delay(50); }   // stops feeding: the watchdog must fire ~1 s later
}
";
            string text = Run(w, esp, sketch, 4.0);
            Assert.Contains("boot reason=1", text);
            Assert.Contains("init=0x103", text);     // already running since boot (IDF 5)
            Assert.Contains("reconf=0x0 add=0x0", text);
            Assert.Contains("task_wdt: Task watchdog got triggered", text);
            Assert.Contains(" - loopTask (CPU 1)", text);
            Assert.Contains("boot reason=6", text);  // ESP_RST_TASK_WDT after the reboot
        }
    }
}
