using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[encoder] The fast mode's pulse counter driver (driver/pulse_cnt.h) on the circuit's own encoder edges.</summary>
    [Trait("Category", "Firmware")]
    public class Esp32PcntTests
    {
        private readonly ITestOutputHelper _out;

        public Esp32PcntTests(ITestOutputHelper output) => _out = output;

        private static PartLibrary Lib => PartLibrary.Default;

        private static void Wire(WorldDocument w, WorldObject a, string pa, WorldObject b, string pb) =>
            w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb });

        private const string QuadratureSketch = @"
#include ""driver/pulse_cnt.h""
pcnt_unit_handle_t unit = NULL;
volatile int limits = 0;
static bool onReach(pcnt_unit_handle_t u, const pcnt_watch_event_data_t *e, void *ctx) { limits++; return false; }
void setup() {
  Serial.begin(115200);
  pcnt_unit_config_t ucfg = {};
  ucfg.low_limit = -1000;
  ucfg.high_limit = 1000;
  ucfg.flags.accum_count = 1;
  ESP_ERROR_CHECK(pcnt_new_unit(&ucfg, &unit));
  pcnt_chan_config_t a = {}; a.edge_gpio_num = 4; a.level_gpio_num = 5;
  pcnt_chan_config_t b = {}; b.edge_gpio_num = 5; b.level_gpio_num = 4;
  pcnt_channel_handle_t ca, cb;
  ESP_ERROR_CHECK(pcnt_new_channel(unit, &a, &ca));
  ESP_ERROR_CHECK(pcnt_new_channel(unit, &b, &cb));
  // IDF's rotary encoder example: x4 decoding of A/B in hardware.
  pcnt_channel_set_edge_action(ca, PCNT_CHANNEL_EDGE_ACTION_DECREASE, PCNT_CHANNEL_EDGE_ACTION_INCREASE);
  pcnt_channel_set_level_action(ca, PCNT_CHANNEL_LEVEL_ACTION_KEEP, PCNT_CHANNEL_LEVEL_ACTION_INVERSE);
  pcnt_channel_set_edge_action(cb, PCNT_CHANNEL_EDGE_ACTION_INCREASE, PCNT_CHANNEL_EDGE_ACTION_DECREASE);
  pcnt_channel_set_level_action(cb, PCNT_CHANNEL_LEVEL_ACTION_KEEP, PCNT_CHANNEL_LEVEL_ACTION_INVERSE);
  ESP_ERROR_CHECK(pcnt_unit_add_watch_point(unit, 1000));
  ESP_ERROR_CHECK(pcnt_unit_add_watch_point(unit, -1000));
  pcnt_event_callbacks_t cbs = {}; cbs.on_reach = onReach;
  ESP_ERROR_CHECK(pcnt_unit_register_event_callbacks(unit, &cbs, NULL));
  ESP_ERROR_CHECK(pcnt_unit_enable(unit));
  ESP_ERROR_CHECK(pcnt_unit_clear_count(unit));
  ESP_ERROR_CHECK(pcnt_unit_start(unit));
  Serial.println(""pcnt hazir"");
}
void loop() {
  int c = 0;
  pcnt_unit_get_count(unit, &c);
  Serial.printf(""sayac=%d sinir=%d\n"", c, limits);
  delay(100);
}
";

        [Esp32Fact]
        public void PulseCounterDecodesTheEncoderMotorsQuadrature()
        {
            // A JGA25 encoder motor (858 lines a turn at the output) runs from 4×NiMH through a button held 0.5–1.0 s,
            // then stops; the ESP32 decodes A/B ×4 with the pulse counter. Its count is the encoder's own, edge for edge.
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            var motor = w.AddComponent(Lib, "motor-jga25-encoder", new WorldPose { X = 0.12 });
            var bat = w.AddComponent(Lib, "bat-4nimh", new WorldPose { X = 0.25 });
            var button = w.AddComponent(Lib, "tact-6mm", new WorldPose { X = 0.2, Y = 0.05 });
            button.Parameters["close.at"] = "0.5";   // after the boot and setup()
            button.Parameters["open.at"] = "1.0";
            Wire(w, bat, "+", button, "A");
            Wire(w, button, "B", motor, "M1");
            Wire(w, bat, "-", motor, "M2");
            Wire(w, esp, "3V3", motor, "VCC");
            Wire(w, esp, "GND1", motor, "GND");
            Wire(w, esp, "GND2", bat, "-");
            Wire(w, esp, "IO4", motor, "C1");
            Wire(w, esp, "IO5", motor, "C2");
            esp.Firmware = new FirmwareSource { FileName = "pcnt.ino", Source = QuadratureSketch };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-pcnt-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            runner.Step(2.0);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            foreach (var e in runner.DrainEvents()) _out.WriteLine($"[{e.Time:0.000}] {e.Source}: {e.Message}");
            _out.WriteLine(text);
            var encoder = runner.Compiled!.Circuit.Devices.OfType<QuadratureEncoder>().Single();
            var last = Regex.Matches(text, @"sayac=(-?\d+) sinir=(\d+)").Cast<Match>().Last();
            int counted = int.Parse(last.Groups[1].Value), limits = int.Parse(last.Groups[2].Value);
            _out.WriteLine($"encoder {encoder.Count}, counted {counted}, limits {limits}");
            Assert.Contains("pcnt hazir", text);
            Assert.True(Math.Abs(encoder.Count) > 400, "the motor turned");
            Assert.InRange(Math.Abs(counted), Math.Abs(encoder.Count) - 2, Math.Abs(encoder.Count) + 2);
            Assert.Equal(Math.Abs(counted) / 1000, limits);   // a watch point at ±1000 for each thousand
        }

        [Esp32Fact]
        public void LightSleepWakesOnTheTimerAndOnAButtonsLevel()
        {
            // The fast mode's light sleep: driver/gpio.h sets up IO4 with its pull-up and a low-level wake, the timer wakes
            // the chip every 0.5 s (millis() goes on by it); the button (IO4 to ground) pressed at 1.6 s wakes it at once.
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            var button = w.AddComponent(Lib, "tact-6mm", new WorldPose { X = 0.1 });
            button.Parameters["close.at"] = "1.6";
            button.Parameters["open.at"] = "1.65";
            Wire(w, esp, "IO4", button, "A");
            Wire(w, button, "B", esp, "GND1");
            esp.Firmware = new FirmwareSource
            {
                FileName = "lsleep.ino",
                Source = @"
#include ""driver/gpio.h""
void setup() {
  Serial.begin(115200);
  gpio_config_t io = {};
  io.pin_bit_mask = 1ULL << 4;
  io.mode = GPIO_MODE_INPUT;
  io.pull_up_en = GPIO_PULLUP_ENABLE;
  ESP_ERROR_CHECK(gpio_config(&io));
  esp_sleep_enable_timer_wakeup(500000);
  ESP_ERROR_CHECK(gpio_wakeup_enable(GPIO_NUM_4, GPIO_INTR_LOW_LEVEL));
  esp_sleep_enable_gpio_wakeup();
  Serial.println(""hafif uyku hazir"");
}
void loop() {
  unsigned long before = millis();
  esp_light_sleep_start();
  unsigned long after = millis();
  Serial.printf(""uyandi neden=%d sure=%lu t=%lu\n"", (int)esp_sleep_get_wakeup_cause(), after - before, after);
  if (esp_sleep_get_wakeup_cause() == ESP_SLEEP_WAKEUP_GPIO) {
    while (gpio_get_level(GPIO_NUM_4) == 0) delay(10);
  }
}
"
            };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-lsleep-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            runner.Step(1.55);
            var board = runner.Boards.Single(b => b.Esp != null).Esp!;
            Assert.True(board.LightSleeping);                  // between wake-ups it sleeps, drawing its sleep current
            runner.Step(0.45);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            _out.WriteLine(text);
            var wakes = Regex.Matches(text, @"uyandi neden=(\d+) sure=(\d+) t=(\d+)").Cast<Match>().ToList();
            var timer = wakes.Where(m => m.Groups[1].Value == "4").ToList();
            Assert.True(timer.Count >= 2);
            foreach (var m in timer) Assert.InRange(int.Parse(m.Groups[2].Value), 499, 501);
            var gpio = wakes.Single(m => m.Groups[1].Value == "7");
            Assert.InRange(int.Parse(gpio.Groups[3].Value), 1600, 1602);   // at the press, not at the next timer wake-up
            Assert.True(board.LightSleeps >= 3);
        }

        [Esp32Fact]
        public void EspErrorCheckAbortsAndRestartsTheBoardAsTheChipDoes()
        {
            // ESP_ERROR_CHECK on a failing call (limits the wrong way round) prints the chip's report and the board
            // restarts with ESP_RST_PANIC.
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            esp.Firmware = new FirmwareSource
            {
                FileName = "check.ino",
                Source = @"
#include ""driver/pulse_cnt.h""
void setup() {
  Serial.begin(115200);
  Serial.printf(""reason=%d\n"", (int)esp_reset_reason());
  pcnt_unit_config_t ucfg = {};
  ucfg.low_limit = 10;
  ucfg.high_limit = -10;
  pcnt_unit_handle_t unit;
  ESP_ERROR_CHECK(pcnt_new_unit(&ucfg, &unit));
}
void loop() { delay(100); }
"
            };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-check-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            runner.Step(1.5);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            _out.WriteLine(text);
            Assert.Contains("invalid limit range:[10,-10]", text);
            Assert.Contains("ESP_ERROR_CHECK failed: esp_err_t 0x102 (ESP_ERR_INVALID_ARG)", text);
            Assert.Contains("expression: pcnt_new_unit(&ucfg, &unit)", text);
            Assert.Contains("reason=1", text);
            Assert.Contains("reason=4", text);   // ESP_RST_PANIC after the abort
        }
    }
}
