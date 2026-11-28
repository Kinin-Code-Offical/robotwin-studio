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
            // At the press (1.6 s of the world, millis() counting from the application's start 0.293 s after the reset), not
            // at the next timer wake-up.
            Assert.InRange(int.Parse(gpio.Groups[3].Value), 1306, 1309);
            Assert.True(board.LightSleeps >= 3);
        }

        [Esp32Fact]
        public void McpwmDriverDrivesAHalfBridgePairInTheFastMode()
        {
            // [motor] The same sketch as the emulator's MCPWM check: 20 kHz, 30 %, 2 µs dead time on each edge; the pins
            // carry A at 26 % and the inverted B at 66 %.
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            esp.Firmware = new FirmwareSource { FileName = "mcpwm.ino", Source = McpwmSketch };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-mcpwm-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            runner.Step(0.6);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            _out.WriteLine(text);
            Assert.Contains("mcpwm hazir", text);
            var pins = runner.Compiled!.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.DigitalPin>().ToList();
            var a = pins.Single(p => p.Id.EndsWith(".IO18", StringComparison.Ordinal));
            var b = pins.Single(p => p.Id.EndsWith(".IO19", StringComparison.Ordinal));
            _out.WriteLine($"A {a.PwmFrequency} Hz {a.PwmDuty:P1}, B {b.PwmFrequency} Hz {b.PwmDuty:P1}");
            Assert.True(a.PwmActive && b.PwmActive);
            Assert.Equal(20000, a.PwmFrequency, 3);
            Assert.Equal(0.26, a.PwmDuty, 6);
            Assert.Equal(20000, b.PwmFrequency, 3);
            Assert.Equal(0.66, b.PwmDuty, 6);
        }

        private const string McpwmSketch = @"
#include ""driver/mcpwm_prelude.h""

void setup() {
  Serial.begin(115200);
  mcpwm_timer_handle_t timer = NULL;
  mcpwm_timer_config_t tcfg = {};
  tcfg.group_id = 0;
  tcfg.clk_src = MCPWM_TIMER_CLK_SRC_DEFAULT;
  tcfg.resolution_hz = 10000000;
  tcfg.period_ticks = 500;
  tcfg.count_mode = MCPWM_TIMER_COUNT_MODE_UP;
  ESP_ERROR_CHECK(mcpwm_new_timer(&tcfg, &timer));
  mcpwm_oper_handle_t oper = NULL;
  mcpwm_operator_config_t ocfg = {};
  ocfg.group_id = 0;
  ESP_ERROR_CHECK(mcpwm_new_operator(&ocfg, &oper));
  ESP_ERROR_CHECK(mcpwm_operator_connect_timer(oper, timer));
  mcpwm_cmpr_handle_t cmp = NULL;
  mcpwm_comparator_config_t ccfg = {};
  ccfg.flags.update_cmp_on_tez = true;
  ESP_ERROR_CHECK(mcpwm_new_comparator(oper, &ccfg, &cmp));
  mcpwm_gen_handle_t ga = NULL, gb = NULL;
  mcpwm_generator_config_t gcfg = {};
  gcfg.gen_gpio_num = 18;
  ESP_ERROR_CHECK(mcpwm_new_generator(oper, &gcfg, &ga));
  gcfg.gen_gpio_num = 19;
  ESP_ERROR_CHECK(mcpwm_new_generator(oper, &gcfg, &gb));
  ESP_ERROR_CHECK(mcpwm_comparator_set_compare_value(cmp, 150));
  ESP_ERROR_CHECK(mcpwm_generator_set_action_on_timer_event(ga,
      MCPWM_GEN_TIMER_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, MCPWM_TIMER_EVENT_EMPTY, MCPWM_GEN_ACTION_HIGH)));
  ESP_ERROR_CHECK(mcpwm_generator_set_action_on_compare_event(ga,
      MCPWM_GEN_COMPARE_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, cmp, MCPWM_GEN_ACTION_LOW)));
  mcpwm_dead_time_config_t dt = {};
  dt.posedge_delay_ticks = 20;
  ESP_ERROR_CHECK(mcpwm_generator_set_dead_time(ga, ga, &dt));
  dt.posedge_delay_ticks = 0;
  dt.negedge_delay_ticks = 20;
  dt.flags.invert_output = true;
  ESP_ERROR_CHECK(mcpwm_generator_set_dead_time(ga, gb, &dt));
  ESP_ERROR_CHECK(mcpwm_timer_enable(timer));
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(timer, MCPWM_TIMER_START_NO_STOP));
  Serial.println(""mcpwm hazir"");
}

void loop() {
  delay(100);
}
";

        [Esp32Fact]
        public void McpwmSyncAndCarrierInTheFastMode()
        {
            // [motor] Timer B takes timer A's empty as its sync with phase 250: B's empty comes 750 us after A's. B's operator
            // drives IO19 at 50 % through a 1 MHz carrier (IDF: the 80 MHz group clock / 8 / 10, a first pulse of two
            // periods): 2 + 498 · 0.5 us high of 500, 25.1 % at the carrier's frequency, as the real chip build gives.
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            esp.Firmware = new FirmwareSource { FileName = "sync.ino", Source = McpwmSyncSketch };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-mcpwm-sync-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            runner.Step(0.6);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            _out.WriteLine(text);
            Assert.Contains("SONUC phase=750", text);
            var pin = runner.Compiled!.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.DigitalPin>().Single(p => p.Id.EndsWith(".IO19", StringComparison.Ordinal));
            _out.WriteLine($"IO19 {pin.PwmFrequency} Hz {pin.PwmDuty:P1}");
            Assert.Equal(1e6, pin.PwmFrequency, 1);
            Assert.Equal(0.251, pin.PwmDuty, 4);
        }

        internal const string McpwmSyncSketch = @"
#include ""driver/mcpwm_prelude.h""
volatile int64_t tA = 0, tB = 0;
static bool IRAM_ATTR onA(mcpwm_timer_handle_t, const mcpwm_timer_event_data_t*, void*) { tA = esp_timer_get_time(); return false; }
static bool IRAM_ATTR onB(mcpwm_timer_handle_t, const mcpwm_timer_event_data_t*, void*) { tB = esp_timer_get_time(); return false; }
void setup() {
  Serial.begin(115200);
  mcpwm_timer_config_t c = {};
  c.group_id = 0; c.clk_src = MCPWM_TIMER_CLK_SRC_DEFAULT; c.resolution_hz = 1000000; c.period_ticks = 1000; c.count_mode = MCPWM_TIMER_COUNT_MODE_UP;
  mcpwm_timer_handle_t a = NULL, b = NULL;
  ESP_ERROR_CHECK(mcpwm_new_timer(&c, &a));
  ESP_ERROR_CHECK(mcpwm_new_timer(&c, &b));
  mcpwm_timer_event_callbacks_t ca = {}; ca.on_empty = onA;
  ESP_ERROR_CHECK(mcpwm_timer_register_event_callbacks(a, &ca, NULL));
  mcpwm_timer_event_callbacks_t cb = {}; cb.on_empty = onB;
  ESP_ERROR_CHECK(mcpwm_timer_register_event_callbacks(b, &cb, NULL));
  mcpwm_sync_handle_t s = NULL;
  mcpwm_timer_sync_src_config_t sc = {}; sc.timer_event = MCPWM_TIMER_EVENT_EMPTY;
  ESP_ERROR_CHECK(mcpwm_new_timer_sync_src(a, &sc, &s));
  mcpwm_timer_sync_phase_config_t ph = {}; ph.sync_src = s; ph.count_value = 250; ph.direction = MCPWM_TIMER_DIRECTION_UP;
  ESP_ERROR_CHECK(mcpwm_timer_set_phase_on_sync(b, &ph));
  mcpwm_oper_handle_t op = NULL;
  mcpwm_operator_config_t oc = {}; oc.group_id = 0;
  ESP_ERROR_CHECK(mcpwm_new_operator(&oc, &op));
  ESP_ERROR_CHECK(mcpwm_operator_connect_timer(op, b));
  mcpwm_cmpr_handle_t cmp = NULL;
  mcpwm_comparator_config_t cc = {}; cc.flags.update_cmp_on_tez = true;
  ESP_ERROR_CHECK(mcpwm_new_comparator(op, &cc, &cmp));
  ESP_ERROR_CHECK(mcpwm_comparator_set_compare_value(cmp, 500));
  mcpwm_gen_handle_t g = NULL;
  mcpwm_generator_config_t gc = {}; gc.gen_gpio_num = 19;
  ESP_ERROR_CHECK(mcpwm_new_generator(op, &gc, &g));
  ESP_ERROR_CHECK(mcpwm_generator_set_action_on_timer_event(g, MCPWM_GEN_TIMER_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, MCPWM_TIMER_EVENT_EMPTY, MCPWM_GEN_ACTION_HIGH)));
  ESP_ERROR_CHECK(mcpwm_generator_set_action_on_compare_event(g, MCPWM_GEN_COMPARE_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, cmp, MCPWM_GEN_ACTION_LOW)));
  mcpwm_carrier_config_t k = {}; k.frequency_hz = 1000000; k.duty_cycle = 0.5; k.first_pulse_duration_us = 2;
  ESP_ERROR_CHECK(mcpwm_operator_apply_carrier(op, &k));
  ESP_ERROR_CHECK(mcpwm_timer_enable(a));
  ESP_ERROR_CHECK(mcpwm_timer_enable(b));
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(b, MCPWM_TIMER_START_NO_STOP));
  delayMicroseconds(300);
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(a, MCPWM_TIMER_START_NO_STOP));
  delay(20);
  int d = (int)((tB - tA) % 1000); if (d < 0) d += 1000;
  Serial.printf(""SONUC phase=%d\n"", d);
}
void loop() { delay(100); }
";

        [Esp32Fact]
        public void ServoPulsesComeInTheLibrarysTimerTicks()
        {
            // As the ESP32Servo library the hardware build uses: a 10-bit LEDC timer at 50 Hz, the pulse in 19.53 us ticks,
            // truncated: write(87) maps to 1441 us, sent as 73 ticks (1425 us); 400 Hz for an ESC, 1700 us as 696 ticks.
            var w = new WorldDocument();
            var esp = w.AddComponent(Lib, Esp32Parts.DevKitCId, new WorldPose());
            esp.Firmware = new FirmwareSource
            {
                FileName = "servo.ino",
                Source = @"#include <ESP32Servo.h>
Servo elev, esc;
void setup() {
  esc.setPeriodHertz(400);
  esc.attach(25, 1000, 2000);
  esc.writeMicroseconds(1700);
  elev.attach(26);
  elev.write(87);
  Serial.begin(115200);
  Serial.printf(""us=%d ticks=%d\n"", elev.readMicroseconds(), elev.readTicks());
}
void loop() { delay(10); }
"
            };
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-servo-" + Guid.NewGuid().ToString("N").Substring(0, 8));
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
            runner.Step(0.6);
            string text = string.Concat(runner.ReadSerial(null, false).Values);
            _out.WriteLine(text);
            Assert.Contains("us=1425 ticks=73", text);
            var pins = runner.Compiled!.Circuit.Devices.OfType<RobotTwin.CoreSim.Analog.Devices.DigitalPin>().ToList();
            var servo = pins.Single(p => p.Id.EndsWith(".IO26", StringComparison.Ordinal));
            var escPin = pins.Single(p => p.Id.EndsWith(".IO25", StringComparison.Ordinal));
            Assert.Equal(50, servo.PwmFrequency, 3);
            Assert.Equal(73 / 1024.0, servo.PwmDuty, 9);
            Assert.Equal(400, escPin.PwmFrequency, 3);
            Assert.Equal(696 / 1024.0, escPin.PwmDuty, 9);
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
