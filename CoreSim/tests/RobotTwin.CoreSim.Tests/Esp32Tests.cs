using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Esp32;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>A [Fact] that is skipped (with the reason shown) when MSVC is not installed.</summary>
    public sealed class Esp32FactAttribute : FactAttribute
    {
        public Esp32FactAttribute()
        {
            if (!Esp32SketchCompiler.IsToolchainAvailable)
            {
                Skip = "MSVC Build Tools (vcvars64.bat) not found: ESP32 sketch tests need Visual Studio 'Desktop development with C++'.";
            }
        }
    }

    /// <summary>Compiles every test sketch once, in parallel, for the whole test class.</summary>
    public sealed class Esp32SketchFixture : IDisposable
    {
        public const string Blink = @"
// Blink for ESP32 (pin 2) with serial output
const int LED = 2;

void setup() {
  Serial.begin(115200);
  pinMode(LED, OUTPUT);
  Serial.println(""ESP32 blink start"");
}

void loop() {
  digitalWrite(LED, HIGH);
  Serial.println(""LED ON"");
  delay(500);
  digitalWrite(LED, LOW);
  Serial.println(""LED OFF"");
  delay(500);
}
";

        public const string AnalogRead = @"
#include <Arduino.h>

void setup() {
  Serial.begin(115200);
  analogReadResolution(12);
}

void loop() {
  int raw = analogRead(34);
  uint32_t mv = analogReadMilliVolts(34);
  Serial.println(describe(raw, mv));
  Serial.printf(""hex=%X bin="", raw);
  Serial.println(raw, BIN);
  delay(100);
}

// Defined after use: needs a generated prototype (String return type, default argument).
String describe(int raw, unsigned long mv, const char *label = ""ADC"") {
  return String(label) + "" raw="" + raw + "" mv="" + mv;
}
";

        public const string Interrupt = @"
volatile int presses = 0;
volatile unsigned long lastIsrMicros = 0;

void IRAM_ATTR onButton() {
  presses++;
  lastIsrMicros = micros();
}

void setup() {
  Serial.begin(115200);
  pinMode(4, INPUT_PULLUP);
  attachInterrupt(digitalPinToInterrupt(4), onButton, FALLING);
}

void loop() {
  static int reported = -1;
  if (presses != reported) {
    reported = presses;
    Serial.print(""presses="");
    Serial.println(reported);
  }
  delay(20);
}
";

        public const string Pwm = @"
void setup() {
  Serial.begin(115200);
  // core 3.x API: pin based
  ledcAttach(5, 1000, 8);
  ledcWrite(5, 64);                  // 25 %
  // core 2.x API: channel based
  ledcSetup(1, 5000, 10);
  ledcAttachPin(18, 1);
  ledcWrite(1, 768);                 // 75 %
  analogWrite(19, 255);              // full on (8 bit default)
  Serial.println(""pwm ready"");
}

void loop() {
  delay(1000);
}
";

        public const string CompileError = @"void setup() {
  Serial.begin(115200);
}

void loop() {
  int x = undefinedVariable + 1;
  Serial.println(x);
}
";

        public const string Hung = @"
void setup() {
  Serial.begin(115200);
  Serial.println(""about to hang"");
}

void loop() {
  delay(100);
  if (millis() > 500) {
    volatile unsigned long spin = 0;
    while (1) { spin++; }
  }
}
";

        public const string Ultrasonic = @"
const int TRIG = 5;
const int ECHO = 18;

void setup() {
  Serial.begin(115200);
  pinMode(TRIG, OUTPUT);
  pinMode(ECHO, INPUT);
}

void loop() {
  digitalWrite(TRIG, LOW);
  delayMicroseconds(2);
  digitalWrite(TRIG, HIGH);          // 10 us trigger: shorter than a co-simulation slice
  delayMicroseconds(10);
  digitalWrite(TRIG, LOW);
  unsigned long us = pulseIn(ECHO, HIGH, 30000);
  Serial.printf(""echo=%lu dist=%.1f\n"", us, us * 0.0343 / 2.0);
  delay(60);
}
";

        /// <summary>[pcnt-pwm] A pulse counter on the pin the chip's own LEDC drives: it counts the PWM's rising edges.</summary>
        public const string PcntLedc = @"
#include ""driver/pulse_cnt.h""
pcnt_unit_handle_t unit = NULL;
void setup() {
  Serial.begin(115200);
  ledcAttach(18, 1000, 8);
  ledcWrite(18, 64);   // 1 kHz, 25 %
  pcnt_unit_config_t cfg = {};
  cfg.low_limit = -30000;
  cfg.high_limit = 30000;
  ESP_ERROR_CHECK(pcnt_new_unit(&cfg, &unit));
  pcnt_chan_config_t ch = {};
  ch.edge_gpio_num = 18;
  ch.level_gpio_num = -1;
  pcnt_channel_handle_t c;
  ESP_ERROR_CHECK(pcnt_new_channel(unit, &ch, &c));
  pcnt_channel_set_edge_action(c, PCNT_CHANNEL_EDGE_ACTION_INCREASE, PCNT_CHANNEL_EDGE_ACTION_HOLD);
  ESP_ERROR_CHECK(pcnt_unit_enable(unit));
  ESP_ERROR_CHECK(pcnt_unit_clear_count(unit));
  ESP_ERROR_CHECK(pcnt_unit_start(unit));
  delay(200);
  int a = 0;
  pcnt_unit_get_count(unit, &a);
  ledcChangeFrequency(18, 4000, 8);   // faster for the next 100 ms
  delay(100);
  int b = 0;
  pcnt_unit_get_count(unit, &b);
  Serial.printf(""pwm a=%d b=%d\n"", a, b);
}
void loop() { delay(100); }
";

        /// <summary>[pcnt-filter] Two pulse counters on one pin: one filters glitches up to 10 µs, the other up to 1 µs.</summary>
        public const string PcntFilter = @"
#include ""driver/pulse_cnt.h""
pcnt_unit_handle_t wide = NULL, narrow = NULL;
static pcnt_unit_handle_t Unit(uint32_t glitchNs) {
  pcnt_unit_handle_t u = NULL;
  pcnt_unit_config_t cfg = {};
  cfg.low_limit = -30000;
  cfg.high_limit = 30000;
  ESP_ERROR_CHECK(pcnt_new_unit(&cfg, &u));
  pcnt_glitch_filter_config_t f = {};
  f.max_glitch_ns = glitchNs;
  ESP_ERROR_CHECK(pcnt_unit_set_glitch_filter(u, &f));
  pcnt_chan_config_t ch = {};
  ch.edge_gpio_num = 4;
  ch.level_gpio_num = -1;
  pcnt_channel_handle_t c;
  ESP_ERROR_CHECK(pcnt_new_channel(u, &ch, &c));
  pcnt_channel_set_edge_action(c, PCNT_CHANNEL_EDGE_ACTION_INCREASE, PCNT_CHANNEL_EDGE_ACTION_HOLD);
  ESP_ERROR_CHECK(pcnt_unit_enable(u));
  ESP_ERROR_CHECK(pcnt_unit_clear_count(u));
  ESP_ERROR_CHECK(pcnt_unit_start(u));
  return u;
}
void setup() {
  Serial.begin(115200);
  wide = Unit(10000);
  narrow = Unit(1000);
  Serial.printf(""too wide %d\n"", (int)pcnt_unit_set_glitch_filter(wide, NULL));   // not in the init state any more
}
void loop() {
  int a = 0, b = 0;
  pcnt_unit_get_count(wide, &a);
  pcnt_unit_get_count(narrow, &b);
  Serial.printf(""wide=%d narrow=%d\n"", a, b);
  delay(2);
}
";

        /// <summary>[mem-budget] A sketch that takes more heap than a real ESP32 has: a task's 16 KB stack, then 50 KB chunks.</summary>
        public const string Memory = @"
static void worker(void *arg) { for (;;) vTaskDelay(100); }

void setup() {
  Serial.begin(115200);
  Serial.printf(""boot %u\n"", (unsigned)ESP.getFreeHeap());
  xTaskCreate(worker, ""worker"", 16384, NULL, 1, NULL);
  Serial.printf(""task %u\n"", (unsigned)ESP.getFreeHeap());
}

void loop() {
  static int n = 0;
  if (n < 6) {
    char *p = (char *)malloc(50000);
    memset(p, 1, 50000);
    n++;
    Serial.printf(""chunk %d %u\n"", n, (unsigned)ESP.getFreeHeap());
  }
  delay(50);
}
";

        public const string Rtos = @"
#include <WiFi.h>

SemaphoreHandle_t tick;
QueueHandle_t queue;
volatile int fastCount = 0;

void producer(void *arg) {
  for (int i = 0;; i++) {
    xQueueSend(queue, &i, portMAX_DELAY);
    vTaskDelay(pdMS_TO_TICKS(50));
  }
}

void fast(void *arg) {
  for (;;) {
    fastCount++;
    xSemaphoreGive(tick);
    vTaskDelay(10);
  }
}

void setup() {
  Serial.begin(115200);
  Serial.println(ESP.getChipModel());
  tick = xSemaphoreCreateBinary();
  queue = xQueueCreate(8, sizeof(int));
  xTaskCreatePinnedToCore(producer, ""producer"", 4096, NULL, 2, NULL, 0);
  xTaskCreate(fast, ""fast"", 2048, NULL, 1, NULL);
  WiFi.mode(WIFI_STA);
  WiFi.begin(""lab"", ""secret"");
}

void loop() {
  static bool wifiReported = false;
  static unsigned long wifiStart = millis();
  int v;
  if (xQueueReceive(queue, &v, pdMS_TO_TICKS(100)) == pdTRUE && v % 5 == 0) {
    Serial.printf(""q=%d fast=%d t=%lu\n"", v, fastCount, millis());
  }
  if (!wifiReported && WiFi.status() == WL_CONNECTED) {
    wifiReported = true;
    Serial.println(""wifi "" + WiFi.localIP().toString() + "" after "" + String(millis() - wifiStart) + "" ms"");
  }
  if (Serial.available()) {
    String cmd = Serial.readStringUntil('\n');
    cmd.trim();
    Serial.println(""echo:"" + cmd + "" n="" + String(cmd.substring(cmd.indexOf(' ') + 1).toInt() * 2));
  }
}
";

        // [libs] what third-party libraries reach for below Arduino's API: the GPIO register file (OneWire's bit-banged bus),
        // port register pointers (Adafruit BusIO's software SPI), the Client / UDP interfaces (PubSubClient, NTPClient), the
        // RTC GPIO driver, GCC's bit built-ins (FastLED) and a C++11-or-later __cplusplus (MFRC522)
        public const string Registers = @"
#include <soc/gpio_struct.h>
#include <driver/rtc_io.h>
#include <Client.h>
#include <Udp.h>
#include <WiFi.h>
#include <WiFiUdp.h>

static int takesClient(Client& c) { return c.connected(); }
static int takesUdp(UDP& u) { return u.begin(1234); }
WiFiClient net;
WiFiUDP udp;

void setup() {
  Serial.begin(115200);
  pinMode(18, OUTPUT);
  GPIO.out_w1ts = 1u << 18;
  int a = (GPIO.out >> 18) & 1;
  int high = digitalRead(18);
  GPIO.out_w1tc = 1u << 18;
  int low = digitalRead(18);
  GPIO.enable_w1tc = 1u << 18;
  int en = (GPIO.enable >> 18) & 1;
  GPIO.enable_w1ts = 1u << 18;
  GPIO.out1_w1ts.val = 1u << (33 - 32);
  int high33 = (GPIO.out1.val >> 1) & 1;

  pinMode(21, OUTPUT);
  volatile uint32_t* out = portOutputRegister(digitalPinToPort(21));
  uint32_t mask = digitalPinToBitMask(21);
  *out |= mask;
  delay(1);
  int set = digitalRead(21);
  *out &= ~mask;
  delay(1);
  int cleared = digitalRead(21);

  Serial.printf(""REGS a=%d high=%d low=%d en=%d high33=%d set=%d cleared=%d\n"", a, high, low, en, high33, set, cleared);
  Serial.printf(""BITS pop=%d clz=%d ctz=%d clz0=%d cpp=%d\n"", __builtin_popcount(0xF0F0u), __builtin_clz(1u), __builtin_ctz(8u),
                __builtin_clz(0u), __cplusplus >= 201103L ? 1 : 0);
  WiFi.mode(WIFI_STA);   // the network stack first, as on the chip
  Serial.printf(""NET client=%d udp=%d rtc4=%d rtc5=%d\n"", takesClient(net), takesUdp(udp), (int)rtc_gpio_is_valid_gpio(GPIO_NUM_4),
                (int)rtc_gpio_is_valid_gpio(GPIO_NUM_5));
}

void loop() { delay(100); }
";

        // [esp32] a socket before Wi-Fi is started: the chip's lwIP asserts on its missing core lock and restarts
        public const string NoStack = @"
#include <WiFi.h>
WiFiClient c;
void setup() {
  Serial.begin(115200);
  Serial.println(""start"");
  c.connect(IPAddress(10, 0, 0, 1), 80);
  Serial.println(""after"");
}
void loop() { delay(10); }
";

        // [motor] the MCPWM capture unit: an external 2 kHz 30 % signal on GPIO4 and the chip's own 1 kHz 25 % LEDC on GPIO18
        public const string Capture = @"
#include ""driver/mcpwm_cap.h""
struct Seen { volatile uint32_t rise[6], fall[6]; volatile int nr, nf; };
Seen seen[2];
static bool IRAM_ATTR onCap(mcpwm_cap_channel_handle_t, const mcpwm_capture_event_data_t* e, void* ctx) {
  Seen* s = (Seen*)ctx;
  if (e->cap_edge == MCPWM_CAP_EDGE_POS) { if (s->nr < 6) s->rise[s->nr++] = e->cap_value; }
  else if (s->nf < 6) s->fall[s->nf++] = e->cap_value;
  return false;
}
static void channel(mcpwm_cap_timer_handle_t t, int gpio, Seen* s) {
  mcpwm_capture_channel_config_t cc = {};
  cc.gpio_num = gpio;
  cc.prescale = 1;
  cc.flags.pos_edge = 1;
  cc.flags.neg_edge = 1;
  mcpwm_cap_channel_handle_t ch = NULL;
  ESP_ERROR_CHECK(mcpwm_new_capture_channel(t, &cc, &ch));
  mcpwm_capture_event_callbacks_t cbs = {};
  cbs.on_cap = onCap;
  ESP_ERROR_CHECK(mcpwm_capture_channel_register_event_callbacks(ch, &cbs, s));
  ESP_ERROR_CHECK(mcpwm_capture_channel_enable(ch));
}
static void report(const char* name, Seen& s) {
  uint32_t period = s.rise[2] - s.rise[1], high = 0;
  for (int i = 0; i < s.nf; i++) if ((int32_t)(s.fall[i] - s.rise[1]) > 0) { high = s.fall[i] - s.rise[1]; break; }
  Serial.printf(""%s period=%u high=%u\n"", name, (unsigned)period, (unsigned)high);
}
void setup() {
  Serial.begin(115200);
  ledcAttach(18, 1000, 10);
  ledcWrite(18, 256);
  mcpwm_capture_timer_config_t tc = {};
  tc.group_id = 0;
  tc.clk_src = MCPWM_CAPTURE_CLK_SRC_DEFAULT;
  mcpwm_cap_timer_handle_t timer = NULL;
  ESP_ERROR_CHECK(mcpwm_new_capture_timer(&tc, &timer));
  channel(timer, 4, &seen[0]);
  channel(timer, 18, &seen[1]);
  ESP_ERROR_CHECK(mcpwm_capture_timer_enable(timer));
  ESP_ERROR_CHECK(mcpwm_capture_timer_start(timer));
  uint32_t res = 0;
  mcpwm_capture_timer_get_resolution(timer, &res);
  delay(8);
  Serial.printf(""res=%u\n"", (unsigned)res);
  report(""ext"", seen[0]);
  report(""own"", seen[1]);
}
void loop() { delay(100); }
";

        // [motor] the MCPWM's event callbacks: a 10 kHz up timer and a 5 kHz up-down one, each with a comparator
        public const string McpwmEvents = @"
#include ""driver/mcpwm_prelude.h""
volatile int empty = 0, full = 0, reach = 0, stops = 0, emptyUd = 0, reachUd = 0;
static bool IRAM_ATTR onEmpty(mcpwm_timer_handle_t, const mcpwm_timer_event_data_t*, void* c) { (*(volatile int*)c)++; return false; }
static bool IRAM_ATTR onFull(mcpwm_timer_handle_t, const mcpwm_timer_event_data_t*, void*) { full++; return false; }
static bool IRAM_ATTR onStop(mcpwm_timer_handle_t, const mcpwm_timer_event_data_t*, void*) { stops++; return false; }
static bool IRAM_ATTR onReach(mcpwm_cmpr_handle_t, const mcpwm_compare_event_data_t*, void* c) { (*(volatile int*)c)++; return false; }
static mcpwm_timer_handle_t make(mcpwm_timer_count_mode_t mode, uint32_t ticks, volatile int* e, bool withFull) {
  mcpwm_timer_config_t tc = {};
  tc.group_id = 0;
  tc.clk_src = MCPWM_TIMER_CLK_SRC_DEFAULT;
  tc.resolution_hz = 1000000;
  tc.count_mode = mode;
  tc.period_ticks = ticks;
  mcpwm_timer_handle_t t = NULL;
  ESP_ERROR_CHECK(mcpwm_new_timer(&tc, &t));
  mcpwm_timer_event_callbacks_t cb = {};
  cb.on_empty = onEmpty;
  if (withFull) { cb.on_full = onFull; cb.on_stop = onStop; }
  ESP_ERROR_CHECK(mcpwm_timer_register_event_callbacks(t, &cb, (void*)e));
  return t;
}
static void comparator(mcpwm_timer_handle_t t, uint32_t value, volatile int* r) {
  mcpwm_operator_config_t oc = {};
  oc.group_id = 0;
  mcpwm_oper_handle_t op = NULL;
  ESP_ERROR_CHECK(mcpwm_new_operator(&oc, &op));
  ESP_ERROR_CHECK(mcpwm_operator_connect_timer(op, t));
  mcpwm_comparator_config_t cc = {};
  mcpwm_cmpr_handle_t c = NULL;
  ESP_ERROR_CHECK(mcpwm_new_comparator(op, &cc, &c));
  ESP_ERROR_CHECK(mcpwm_comparator_set_compare_value(c, value));
  mcpwm_comparator_event_callbacks_t cb = {};
  cb.on_reach = onReach;
  ESP_ERROR_CHECK(mcpwm_comparator_register_event_callbacks(c, &cb, (void*)r));
}
void setup() {
  Serial.begin(115200);
  mcpwm_timer_handle_t up = make(MCPWM_TIMER_COUNT_MODE_UP, 100, &empty, true);
  mcpwm_timer_handle_t ud = make(MCPWM_TIMER_COUNT_MODE_UP_DOWN, 200, &emptyUd, false);
  comparator(up, 30, &reach);
  comparator(ud, 50, &reachUd);
  ESP_ERROR_CHECK(mcpwm_timer_enable(up));
  ESP_ERROR_CHECK(mcpwm_timer_enable(ud));
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(up, MCPWM_TIMER_START_NO_STOP));
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(ud, MCPWM_TIMER_START_NO_STOP));
  delay(100);
  int e = empty, f = full, r = reach, eu = emptyUd, ru = reachUd;
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(up, MCPWM_TIMER_STOP_EMPTY));
  delay(5);
  Serial.printf(""SONUC empty=%d full=%d reach=%d ud_empty=%d ud_reach=%d stop=%d after=%d\n"", e, f, r, eu, ru, stops, empty - e);
}
void loop() { delay(100); }
";

        // [legacy] the legacy MCPWM driver (driver/mcpwm.h): three timers' outputs, dead time, and a capture channel
        public const string LegacyMcpwm = @"
#include ""driver/mcpwm.h""
volatile uint32_t rise[6], fall[6];
volatile int nr = 0, nf = 0;
static bool IRAM_ATTR onCap(mcpwm_unit_t, mcpwm_capture_channel_id_t, const cap_event_data_t* e, void*) {
  if (e->cap_edge == MCPWM_POS_EDGE) { if (nr < 6) rise[nr++] = e->cap_value; }
  else if (nf < 6) fall[nf++] = e->cap_value;
  return false;
}
void setup() {
  Serial.begin(115200);
  mcpwm_gpio_init(MCPWM_UNIT_0, MCPWM0A, 18);
  mcpwm_gpio_init(MCPWM_UNIT_0, MCPWM0B, 19);
  mcpwm_gpio_init(MCPWM_UNIT_0, MCPWM1A, 21);
  mcpwm_gpio_init(MCPWM_UNIT_0, MCPWM2A, 22);
  mcpwm_gpio_init(MCPWM_UNIT_0, MCPWM2B, 23);
  mcpwm_config_t c = {};
  c.frequency = 1000;
  c.cmpr_a = 25.0;
  c.cmpr_b = 60.0;
  c.counter_mode = MCPWM_UP_COUNTER;
  c.duty_mode = MCPWM_DUTY_MODE_0;
  ESP_ERROR_CHECK(mcpwm_init(MCPWM_UNIT_0, MCPWM_TIMER_0, &c));
  c.frequency = 2000;
  c.cmpr_a = 30.0;
  c.cmpr_b = 0;
  c.counter_mode = MCPWM_UP_DOWN_COUNTER;
  ESP_ERROR_CHECK(mcpwm_init(MCPWM_UNIT_0, MCPWM_TIMER_1, &c));
  c.frequency = 5000;
  c.cmpr_a = 40.0;
  c.counter_mode = MCPWM_UP_COUNTER;
  ESP_ERROR_CHECK(mcpwm_init(MCPWM_UNIT_0, MCPWM_TIMER_2, &c));
  ESP_ERROR_CHECK(mcpwm_deadtime_enable(MCPWM_UNIT_0, MCPWM_TIMER_2, MCPWM_ACTIVE_HIGH_COMPLIMENT_MODE, 20, 20));
  mcpwm_gpio_init(MCPWM_UNIT_1, MCPWM_CAP_0, 4);
  mcpwm_capture_config_t cc = {};
  cc.cap_edge = MCPWM_BOTH_EDGE;
  cc.cap_prescale = 1;
  cc.capture_cb = onCap;
  ESP_ERROR_CHECK(mcpwm_capture_enable_channel(MCPWM_UNIT_1, MCPWM_SELECT_CAP0, &cc));
  delay(20);
  uint32_t high = 0;
  for (int i = 0; i < nf; i++) if ((int32_t)(fall[i] - rise[1]) > 0) { high = fall[i] - rise[1]; break; }
  Serial.printf(""CAP res=%u period=%u high=%u edge=%u\n"", (unsigned)mcpwm_capture_get_resolution(MCPWM_UNIT_1), (unsigned)(rise[2] - rise[1]),
                (unsigned)high, (unsigned)mcpwm_capture_signal_get_edge(MCPWM_UNIT_1, MCPWM_SELECT_CAP0));
  Serial.printf(""SONUC f=%u a=%.1f b=%.1f us=%u"", (unsigned)mcpwm_get_frequency(MCPWM_UNIT_0, MCPWM_TIMER_0),
                mcpwm_get_duty(MCPWM_UNIT_0, MCPWM_TIMER_0, MCPWM_OPR_A), mcpwm_get_duty(MCPWM_UNIT_0, MCPWM_TIMER_0, MCPWM_OPR_B),
                (unsigned)mcpwm_get_duty_in_us(MCPWM_UNIT_0, MCPWM_TIMER_0, MCPWM_OPR_B));
  Serial.println();
}
void loop() { delay(100); }
";

        // [motor] the MCPWM's fault handler: a GPIO fault on GPIO4 (active low) trips two operators one shot; one is recovered once it is gone
        public const string McpwmFault = @"
#include ""driver/mcpwm_prelude.h""
volatile int trips = 0, enters = 0, exits = 0;
static bool IRAM_ATTR onEnter(mcpwm_fault_handle_t, const mcpwm_fault_event_data_t*, void*) { enters++; return false; }
static bool IRAM_ATTR onExit(mcpwm_fault_handle_t, const mcpwm_fault_event_data_t*, void*) { exits++; return false; }
static bool IRAM_ATTR onOst(mcpwm_oper_handle_t, const mcpwm_brake_event_data_t*, void*) { trips++; return false; }
static mcpwm_oper_handle_t stage(mcpwm_timer_handle_t t, int gpio, mcpwm_fault_handle_t f) {
  mcpwm_operator_config_t oc = {};
  oc.group_id = 0;
  mcpwm_oper_handle_t op = NULL;
  ESP_ERROR_CHECK(mcpwm_new_operator(&oc, &op));
  ESP_ERROR_CHECK(mcpwm_operator_connect_timer(op, t));
  mcpwm_comparator_config_t cc = {};
  mcpwm_cmpr_handle_t cmp = NULL;
  ESP_ERROR_CHECK(mcpwm_new_comparator(op, &cc, &cmp));
  ESP_ERROR_CHECK(mcpwm_comparator_set_compare_value(cmp, 300));
  mcpwm_generator_config_t gc = {};
  gc.gen_gpio_num = gpio;
  mcpwm_gen_handle_t g = NULL;
  ESP_ERROR_CHECK(mcpwm_new_generator(op, &gc, &g));
  ESP_ERROR_CHECK(mcpwm_generator_set_action_on_timer_event(g, MCPWM_GEN_TIMER_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, MCPWM_TIMER_EVENT_EMPTY, MCPWM_GEN_ACTION_HIGH)));
  ESP_ERROR_CHECK(mcpwm_generator_set_action_on_compare_event(g, MCPWM_GEN_COMPARE_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, cmp, MCPWM_GEN_ACTION_LOW)));
  mcpwm_brake_config_t bc = {};
  bc.fault = f;
  bc.brake_mode = MCPWM_OPER_BRAKE_MODE_OST;
  ESP_ERROR_CHECK(mcpwm_operator_set_brake_on_fault(op, &bc));
  ESP_ERROR_CHECK(mcpwm_generator_set_actions_on_brake_event(g,
    MCPWM_GEN_BRAKE_EVENT_ACTION(MCPWM_TIMER_DIRECTION_UP, MCPWM_OPER_BRAKE_MODE_OST, MCPWM_GEN_ACTION_LOW),
    MCPWM_GEN_BRAKE_EVENT_ACTION(MCPWM_TIMER_DIRECTION_DOWN, MCPWM_OPER_BRAKE_MODE_OST, MCPWM_GEN_ACTION_LOW),
    MCPWM_GEN_BRAKE_EVENT_ACTION_END()));
  mcpwm_operator_event_callbacks_t cb = {};
  cb.on_brake_ost = onOst;
  ESP_ERROR_CHECK(mcpwm_operator_register_event_callbacks(op, &cb, NULL));
  return op;
}
void setup() {
  Serial.begin(115200);
  mcpwm_timer_config_t tc = {};
  tc.group_id = 0;
  tc.clk_src = MCPWM_TIMER_CLK_SRC_DEFAULT;
  tc.resolution_hz = 1000000;
  tc.count_mode = MCPWM_TIMER_COUNT_MODE_UP;
  tc.period_ticks = 1000;
  mcpwm_timer_handle_t t = NULL;
  ESP_ERROR_CHECK(mcpwm_new_timer(&tc, &t));
  mcpwm_gpio_fault_config_t fc = {};
  fc.group_id = 0;
  fc.gpio_num = 4;
  fc.flags.active_level = 0;
  fc.flags.pull_up = 1;
  mcpwm_fault_handle_t f = NULL;
  ESP_ERROR_CHECK(mcpwm_new_gpio_fault(&fc, &f));
  mcpwm_fault_event_callbacks_t fcb = {};
  fcb.on_fault_enter = onEnter;
  fcb.on_fault_exit = onExit;
  ESP_ERROR_CHECK(mcpwm_fault_register_event_callbacks(f, &fcb, NULL));
  mcpwm_oper_handle_t a = stage(t, 18, f);
  mcpwm_oper_handle_t b = stage(t, 19, f);
  ESP_ERROR_CHECK(mcpwm_timer_enable(t));
  ESP_ERROR_CHECK(mcpwm_timer_start_stop(t, MCPWM_TIMER_START_NO_STOP));
  delay(25);   // the fault pin goes low (present) at 20 ms, high again at 40 ms
  int during = mcpwm_operator_recover_from_fault(b, f);
  delay(20);
  int rec = mcpwm_operator_recover_from_fault(b, f);
  delay(5);
  (void)a;
  Serial.printf(""SONUC trips=%d during=%d rec=%d enters=%d exits=%d\n"", trips, during, rec, enters, exits);
}
void loop() { delay(100); }
";

        public Esp32SketchFixture()
        {
            Available = Esp32SketchCompiler.IsToolchainAvailable;
            OutputDir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-tests", Guid.NewGuid().ToString("N").Substring(0, 8));
            if (!Available) return;
            Directory.CreateDirectory(OutputDir);

            // The compile-error sketch is built from a file so diagnostics carry its path.
            string badDir = Path.Combine(OutputDir, "BadSketch");
            Directory.CreateDirectory(badDir);
            BadSketchPath = Path.Combine(badDir, "BadSketch.ino");
            File.WriteAllText(BadSketchPath, CompileError);

            var sources = new Dictionary<string, string>
            {
                ["blink"] = Blink,
                ["analog"] = AnalogRead,
                ["interrupt"] = Interrupt,
                ["pwm"] = Pwm,
                ["error"] = BadSketchPath,
                ["hung"] = Hung,
                ["ultrasonic"] = Ultrasonic,
                ["rtos"] = Rtos,
                ["memory"] = Memory,
                ["pcnt-filter"] = PcntFilter,
                ["pcnt-ledc"] = PcntLedc,
                ["registers"] = Registers,
                ["no-stack"] = NoStack,
                ["capture"] = Capture,
                ["mcpwm-events"] = McpwmEvents,
                ["legacy-mcpwm"] = LegacyMcpwm,
                ["mcpwm-fault"] = McpwmFault
            };
            var compiler = new Esp32SketchCompiler();
            var results = new ConcurrentDictionary<string, Esp32BuildResult>();
            var sw = Stopwatch.StartNew();
            Parallel.ForEach(sources, kv => results[kv.Key] = compiler.Compile(kv.Value, Path.Combine(OutputDir, kv.Key)));
            TotalCompileTime = sw.Elapsed;
            Builds = new Dictionary<string, Esp32BuildResult>(results);
        }

        public bool Available { get; }
        public string OutputDir { get; }
        public string BadSketchPath { get; } = string.Empty;
        public Dictionary<string, Esp32BuildResult> Builds { get; } = new Dictionary<string, Esp32BuildResult>();
        public TimeSpan TotalCompileTime { get; }

        public string Dll(string key)
        {
            var build = Builds[key];
            Assert.True(build.Success, key + " did not compile:\n" + string.Join("\n", build.Diagnostics) + "\n" + build.Log);
            return build.DllPath!;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    [Trait("Category", "Firmware")]
    public class Esp32Tests : IClassFixture<Esp32SketchFixture>
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly Esp32SketchFixture _fx;
        private readonly ITestOutputHelper _out;

        public Esp32Tests(Esp32SketchFixture fixture, ITestOutputHelper output)
        {
            _fx = fixture;
            _out = output;
        }

        // ------------------------------------------------------------------ circuit helpers

        private static GridPoint PinAt(SchematicPart part, string pin)
        {
            var p = Lib.Get(part.PartId).FindPin(pin) ?? throw new ArgumentException(part.PartId + " has no pin " + pin);
            return part.Transform(p.X, p.Y);
        }

        private static void Label(Schematic sch, SchematicPart part, string pin, string net) =>
            sch.Labels.Add(new NetLabel { Name = net, Position = PinAt(part, pin) });

        private static (Schematic Sch, SchematicPart Board) NewBoard()
        {
            var sch = new Schematic { Title = "ESP32 test" };
            var board = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
            Label(sch, board, "GND2", "GND");
            return (sch, board);
        }

        private sealed class Rig : IDisposable
        {
            public Rig(CompiledCircuit compiled, string dll, Esp32Options? options = null)
            {
                Compiled = compiled;
                Sim = new AnalogSimulator(compiled.Circuit);
                Sim.Initialize(InitialState.OperatingPoint);
                CoSim = new Esp32CoSimulation(dll, compiled, "U1", null, options ?? new Esp32Options { BootDelay = 0.05 });
                CoSim.SerialOutput += s => Serial.Append(s);
            }

            public CompiledCircuit Compiled { get; }
            public AnalogSimulator Sim { get; }
            public Esp32CoSimulation CoSim { get; }
            public StringBuilder Serial { get; } = new StringBuilder();

            public void Run(double seconds, double slice, Action<double>? probe = null)
            {
                int n = (int)Math.Round(seconds / slice);
                for (int i = 0; i < n; i++)
                {
                    CoSim.Advance(slice, Sim.Time);
                    Sim.Step(slice);
                    probe?.Invoke(Sim.Time);
                }
            }

            public void Dispose() => CoSim.Dispose();
        }

        // ------------------------------------------------------------------ tests

        [Esp32Fact]
        public void CatalogHasEsp32BoardsWithFootprints()
        {
            var part = Lib.Get(Esp32Parts.DevKitCId);
            Assert.Equal(StandardParts.Boards, part.Category);
            Assert.Equal(38, part.Pins.Count);
            var fp = RobotTwin.CoreSim.Pcb.FootprintLibrary.Get(part.Footprint)!;
            Assert.Equal(25.4, fp.FindPad("CLK")!.X - fp.FindPad("SD2")!.X, 6);
            Assert.Equal(2.54, fp.FindPad("EN")!.Y - fp.FindPad("3V3")!.Y, 6);
            Assert.NotNull(Lib.FindPreset(Esp32Parts.DevKitV1Id));
        }

        [Esp32Fact]
        public void BlinkSketchTogglesLedAtOneHertzAndPrints()
        {
            _out.WriteLine("parallel compile of all test sketches: " + _fx.TotalCompileTime.TotalSeconds.ToString("F2") + " s");
            foreach (var kv in _fx.Builds)
            {
                _out.WriteLine($"{kv.Key}: build {kv.Value.BuildTime.TotalSeconds:F2} s, shim {(kv.Value.ShimFromCache ? "cached" : kv.Value.ShimBuildTime.TotalSeconds.ToString("F2") + " s")}, toolchain {kv.Value.ToolchainSetupTime.TotalSeconds:F2} s");
            }

            var (sch, board) = NewBoard();
            var r = sch.AddPart(Lib, "res-330", new GridPoint(16, 10));
            var led = sch.AddPart(Lib, "led5-red", new GridPoint(26, 10));
            var gnd = sch.AddPart(Lib, "gnd", new GridPoint(32, 16));
            sch.AddWire(PinAt(board, "IO2"), PinAt(r, "A"));
            sch.AddWire(PinAt(r, "B"), PinAt(led, "A"));
            sch.AddWire(PinAt(led, "K"), new GridPoint(PinAt(gnd, "GND").X, PinAt(led, "K").Y), PinAt(gnd, "GND"));
            var compiled = CircuitCompiler.Compile(sch, Lib);

            using var rig = new Rig(compiled, _fx.Dll("blink"));
            var diode = compiled.Device<Led>("D1")!;
            var pinEdges = new List<(double Time, bool Level)>();   // [unified-timeline] what the timeline records
            rig.CoSim.PinChanged += (t, gpio, level) =>
            {
                if (gpio == 2) pinEdges.Add((t, level));
            };
            var edges = new List<(double Time, bool On)>();
            bool on = false;
            double maxCurrent = 0;
            var wall = Stopwatch.StartNew();
            rig.Run(2.2, 100e-6, t =>
            {
                bool now = diode.Current > 0.5e-3;
                maxCurrent = Math.Max(maxCurrent, diode.Current);
                if (now != on)
                {
                    edges.Add((t, now));
                    on = now;
                }
            });
            _out.WriteLine($"2.2 s simulated in {wall.Elapsed.TotalSeconds:F2} s wall; LED edges at " +
                           string.Join(", ", edges.Select(e => e.Time.ToString("F4") + (e.On ? "↑" : "↓"))));
            _out.WriteLine(rig.Serial.ToString());

            Assert.Equal(Esp32BoardState.Running, rig.CoSim.State);
            Assert.InRange(maxCurrent, 2e-3, 8e-3);   // (3.3 V - Vf) / 330 Ω
            Assert.True(edges.Count >= 4, "LED edges: " + edges.Count);
            Assert.True(edges[0].On);
            for (int i = 1; i < edges.Count; i++)
            {
                Assert.InRange(edges[i].Time - edges[i - 1].Time, 0.499, 0.502);
                Assert.NotEqual(edges[i].On, edges[i - 1].On);
            }

            // the pin's reported edges are the LED's, at the same instants (the LED follows within a solver step)
            Assert.Equal(edges.Count, pinEdges.Count(e => e.Time <= edges[^1].Time + 1e-3));
            for (int i = 0; i < edges.Count; i++)
            {
                Assert.Equal(edges[i].On, pinEdges[i].Level);
                Assert.InRange(edges[i].Time - pinEdges[i].Time, -1e-4, 1e-3);
            }

            string serial = rig.Serial.ToString();
            Assert.Contains("ESP32 blink start", serial);
            Assert.Contains("rst:0x1 (POWERON_RESET)", serial);
            var lines = serial.Split(new[] { "\r\n" }, StringSplitOptions.None);
            Assert.True(lines.Count(l => l == "LED ON") >= 2);
            Assert.True(lines.Count(l => l == "LED OFF") >= 2);
        }

        [Esp32Fact]
        public void AnalogReadOfPotentiometerDividerOnIo34()
        {
            var (sch, board) = NewBoard();
            var pot = sch.AddPart(Lib, "pot-10k", new GridPoint(-20, 0));
            pot.Parameters["position"] = "0.3";
            Label(sch, pot, "A", "3V3");
            Label(sch, pot, "B", "GND");
            Label(sch, pot, "W", "POT");
            Label(sch, board, "3V3", "3V3");
            Label(sch, board, "IO34", "POT");
            var compiled = CircuitCompiler.Compile(sch, Lib);

            using var rig = new Rig(compiled, _fx.Dll("analog"));
            rig.Run(0.5, 200e-6);
            double v = compiled.PinVoltage(rig.Sim, "U1", "IO34");
            int expected = (int)Math.Round(v / 3.3 * 4095);
            string serial = rig.Serial.ToString();
            _out.WriteLine($"IO34 = {v:F4} V, expected raw {expected}");
            _out.WriteLine(serial);

            var line = serial.Split('\n').Select(l => l.Trim()).Last(l => l.StartsWith("ADC raw=", StringComparison.Ordinal));
            var parts = line.Split(' ');
            int raw = int.Parse(parts[1].Substring(4));
            int mv = int.Parse(parts[2].Substring(3));
            Assert.InRange(raw, expected - 3, expected + 3);
            Assert.InRange(mv, v * 1000 - 3, v * 1000 + 3);
            Assert.InRange(v, 0.5, 2.8);
            Assert.Contains("bin=" + Convert.ToString(raw, 2), serial.Replace("\r\n", string.Empty));
            Assert.Contains("hex=" + raw.ToString("X"), serial);
        }

        [Esp32Fact]
        public void AttachInterruptCountsButtonPresses()
        {
            var (sch, board) = NewBoard();
            var button = sch.AddPart(Lib, "tact-6mm", new GridPoint(-20, 0));
            Label(sch, button, "A", "BTN");
            Label(sch, button, "B", "GND");
            Label(sch, board, "IO4", "BTN");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var sw = compiled.Device<RobotTwin.CoreSim.Analog.Devices.Switch>("SW1")!;

            using var rig = new Rig(compiled, _fx.Dll("interrupt"));
            double[] pressAt = { 0.20, 0.45, 0.70 };
            int next = 0;
            rig.Run(1.0, 100e-6, t =>
            {
                if (next < pressAt.Length * 2)
                {
                    double at = pressAt[next / 2] + (next % 2 == 0 ? 0.0 : 0.05);
                    if (t >= at)
                    {
                        sw.Closed = next % 2 == 0;
                        next++;
                    }
                }
            });
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("presses=0", serial);
            Assert.Contains("presses=1", serial);
            Assert.Contains("presses=3", serial);
            Assert.DoesNotContain("presses=4", serial);
            Assert.True(rig.CoSim.GetPin(4).InputLevel, "pull-up should hold IO4 high when released");
        }

        [Esp32Fact]
        public void LedcWriteDrivesPwmDutyOnPins()
        {
            var (sch, board) = NewBoard();
            var r = sch.AddPart(Lib, "res-1000", new GridPoint(20, 0));
            Label(sch, board, "IO5", "PWM");
            Label(sch, r, "A", "PWM");
            Label(sch, r, "B", "GND");
            var compiled = CircuitCompiler.Compile(sch, Lib);

            using var rig = new Rig(compiled, _fx.Dll("pwm"));
            double high = 0, total = 0;
            rig.Run(0.2, 100e-6);
            rig.Sim.SubstepAccepted = null;
            // Average the pin voltage over 50 ms at solver resolution: should be 25 % of 3.3 V.
            double last = rig.Sim.Time;
            rig.Sim.SubstepAccepted = t =>
            {
                double v = compiled.PinVoltage(rig.Sim, "U1", "IO5");
                high += v * (t - last);
                total += t - last;
                last = t;
            };
            rig.Run(0.05, 1e-3);
            rig.Sim.SubstepAccepted = null;

            var io5 = compiled.Device<DigitalPin>("U1.IO5")!;
            var io18 = compiled.Device<DigitalPin>("U1.IO18")!;
            var io19 = compiled.Device<DigitalPin>("U1.IO19")!;
            Assert.True(io5.PwmActive);
            Assert.Equal(1000.0, io5.PwmFrequency, 6);
            Assert.Equal(0.25, io5.PwmDuty, 6);
            Assert.Equal(5000.0, io18.PwmFrequency, 6);
            Assert.Equal(0.75, io18.PwmDuty, 6);
            Assert.False(io19.PwmActive);                   // 255/255 = fully on
            Assert.Equal(PinMode.OutputHigh, io19.Mode);
            var state = rig.CoSim.GetPin(5);
            Assert.True(state.PwmAttached);
            Assert.Equal(0.25, state.PwmDuty, 6);
            double average = high / total;
            _out.WriteLine($"IO5 average {average:F3} V");
            Assert.InRange(average, 0.25 * 3.3 * 0.95, 0.25 * 3.3 * 1.05);
            Assert.Contains("pwm ready", rig.Serial.ToString());
        }

        [Esp32Fact]
        public void ShortTriggerPulseAndPulseInMeasureUltrasonicEcho()
        {
            var (sch, board) = NewBoard();
            var sensor = sch.AddPart(Lib, "hc-sr04", new GridPoint(-30, 0));
            Label(sch, sensor, "VCC", "VBUS");
            Label(sch, board, "5V", "VBUS");
            Label(sch, sensor, "GND", "GND");
            Label(sch, sensor, "TRIG", "TRIG");
            Label(sch, board, "IO5", "TRIG");
            Label(sch, sensor, "ECHO", "ECHO");
            Label(sch, board, "IO18", "ECHO");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var hc = compiled.Device<UltrasonicSensor>("U2")!;
            hc.Distance = 0.5;
            hc.RangeNoise = 0;   // [sensorspec] pulseIn's own precision is what this measures, not the sensor's jitter

            using var rig = new Rig(compiled, _fx.Dll("ultrasonic"));
            rig.Sim.SubstepAccepted = rig.CoSim.OnSubstep;   // exact edge times for pulseIn()
            rig.Run(0.5, 100e-6);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            var widths = serial.Split('\n').Where(l => l.StartsWith("echo=", StringComparison.Ordinal))
                .Select(l => int.Parse(l.Substring(5, l.IndexOf(' ') - 5))).ToList();
            Assert.True(widths.Count >= 3, "no echo measurements");
            double expected = 2 * 0.5 / 343.0 * 1e6;   // 2915 us
            Assert.All(widths, w => Assert.InRange(w, expected - 3, expected + 3));
            Assert.Contains("dist=50.0", serial);
        }

        [Esp32Fact]
        public void LibrariesReachTheRegistersAndInterfacesBelowArduinosApi()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("registers"));
            rig.Run(0.3, 1e-3);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("REGS a=1 high=1 low=0 en=0 high33=1 set=1 cleared=0", serial);
            Assert.Contains("BITS pop=8 clz=31 ctz=3 clz0=32 cpp=1", serial);
            Assert.Contains("NET client=0 udp=1 rtc4=1 rtc5=0", serial);
        }

        [Esp32Fact]
        public void TheCaptureUnitTimesAnInputsEdgesAndTheChipsOwnPwm()
        {
            var (sch, board) = NewBoard();
            var gen = sch.AddPart(Lib, "signal-generator", new GridPoint(-40, 0));
            gen.Parameters["frequency"] = "2000";
            gen.Parameters["duty"] = "0.3";
            gen.Parameters["amplitude"] = "3.3";
            gen.Parameters["rs"] = "0";
            Label(sch, gen, "+", "SIG");
            Label(sch, gen, "-", "GND");
            Label(sch, board, "IO4", "SIG");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("capture"), new Esp32Options { BootDelay = 0.002 });
            rig.Run(0.014, 1e-6);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("res=80000000", serial);
            Assert.Contains("own period=80000 high=20000", serial);   // the chip's own PWM: exact
            var ext = serial.Split('\n').Single(l => l.StartsWith("ext ", StringComparison.Ordinal)).Trim().Split(' ');
            Assert.InRange(int.Parse(ext[1].Substring(7)), 40000 - 160, 40000 + 160);   // 2 kHz, the circuit's edges within ~2 µs
            Assert.InRange(int.Parse(ext[2].Substring(5)), 12000 - 160, 12000 + 160);   // 30 %
        }

        [Esp32Fact]
        public void TheMcpwmsEventCallbacksComeAtTheirTicks()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("mcpwm-events"), new Esp32Options { BootDelay = 0.002 });
            rig.Run(0.15, 1e-3);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            var v = serial.Split('\n').Single(l => l.StartsWith("SONUC ", StringComparison.Ordinal)).Trim().Split(' ').Skip(1)
                .ToDictionary(x => x.Split('=')[0], x => int.Parse(x.Split('=')[1]));
            Assert.InRange(v["empty"], 999, 1000);     // 100 µs periods for 100 ms
            Assert.InRange(v["full"], 999, 1001);
            Assert.InRange(v["reach"], 999, 1001);
            Assert.InRange(v["ud_empty"], 499, 500);   // 200 µs up-down periods
            Assert.InRange(v["ud_reach"], 998, 1001);  // passed twice a period
            Assert.Equal(1, v["stop"]);
            Assert.Equal(0, v["after"]);
        }

        [Esp32Fact]
        public void TheLegacyMcpwmDriverDrivesAndCapturesAsTheRealOneDoes()
        {
            // the same numbers as the deprecated IDF driver's real-chip build on the emulator, counting up-down too (it writes
            // res / f - 1 as the peak: 2 kHz asked, 1002 Hz out)
            var (sch, board) = NewBoard();
            var gen = sch.AddPart(Lib, "signal-generator", new GridPoint(-40, 0));
            gen.Parameters["frequency"] = "2000";
            gen.Parameters["duty"] = "0.3";
            gen.Parameters["amplitude"] = "3.3";
            gen.Parameters["rs"] = "0";
            Label(sch, gen, "+", "SIG");
            Label(sch, gen, "-", "GND");
            Label(sch, board, "IO4", "SIG");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("legacy-mcpwm"), new Esp32Options { BootDelay = 0.002 });
            rig.Run(0.03, 1e-6);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("SONUC f=1000 a=25.0 b=60.0 us=600", serial);
            var pins = compiled.Circuit.Devices.OfType<DigitalPin>().ToDictionary(p => p.Id.Substring(p.Id.LastIndexOf('.') + 1));
            void Pwm(string id, double hz, double duty)
            {
                Assert.True(pins[id].PwmActive, id + " is not a PWM");
                Assert.Equal(hz, pins[id].PwmFrequency, 0);
                Assert.Equal(duty, pins[id].PwmDuty, 3);
            }

            Pwm("IO18", 1000, 0.25);
            Pwm("IO19", 1000, 0.60);
            Pwm("IO21", 1e6 / 998, 300.0 / 998);
            Pwm("IO22", 5000, 0.39);   // 40 % less the 2 µs rising-edge delay
            Pwm("IO23", 5000, 0.59);   // the inverse with the falling-edge delay
            var cap = serial.Split('\n').Single(l => l.StartsWith("CAP ", StringComparison.Ordinal)).Trim().Split(' ');
            Assert.Equal("res=80000000", cap[1]);
            Assert.InRange(int.Parse(cap[2].Substring(7)), 40000 - 160, 40000 + 160);
            Assert.InRange(int.Parse(cap[3].Substring(5)), 12000 - 160, 12000 + 160);
            Assert.Contains(cap[4], new[] { "edge=1", "edge=2" });
        }

        [Esp32Fact]
        public void AnMcpwmFaultBrakesTheOperatorsUntilTheyRecover()
        {
            // a 25 Hz square wave on GPIO4, high for 20 ms then low for 20 ms: the fault (active low) is there from 20 to 40 ms
            var (sch, board) = NewBoard();
            var gen = sch.AddPart(Lib, "signal-generator", new GridPoint(-40, 0));
            gen.Parameters["frequency"] = "25";
            gen.Parameters["duty"] = "0.5";
            gen.Parameters["amplitude"] = "3.3";
            gen.Parameters["rs"] = "0";
            Label(sch, gen, "+", "SIG");
            Label(sch, gen, "-", "GND");
            Label(sch, board, "IO4", "SIG");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("mcpwm-fault"), new Esp32Options { BootDelay = 0.002 });   // after the generator's first edge
            rig.Run(0.055, 1e-4);   // before the next fault at 60 ms
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("SONUC trips=2 during=259 rec=0 enters=1 exits=1", serial);   // 259: ESP_ERR_INVALID_STATE while present
            var pins = compiled.Circuit.Devices.OfType<DigitalPin>().ToDictionary(p => p.Id.Substring(p.Id.LastIndexOf('.') + 1));
            Assert.False(pins["IO18"].PwmActive);        // still tripped: its brake action, low
            Assert.True(pins["IO19"].PwmActive);         // recovered
            Assert.Equal(0.30, pins["IO19"].PwmDuty, 3);
        }

        [Esp32Fact]
        public void ASocketBeforeWifiRestartsTheChipAsItsLwipDoes()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("no-stack"));
            rig.Run(1.0, 1e-3);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("assert failed: xQueueSemaphoreTake queue.c:1709 (( pxQueue ))", serial);
            Assert.DoesNotContain("after", serial);
            Assert.True(serial.Split('\n').Count(l => l.Trim() == "start") >= 2, "the sketch did not start again");
        }

        [Esp32Fact]
        public void FreeRtosTasksQueuesSerialInputAndWifiStub()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("rtos"));
            bool sent = false;
            rig.Run(1.4, 1e-3, t =>
            {
                if (!sent && t >= 0.3)
                {
                    rig.CoSim.SendSerial("hello 21\n");
                    sent = true;
                }
            });
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            Assert.Contains("ESP32-D0WDQ6 (simulated)", serial);
            Assert.Contains("echo:hello 21 n=42", serial);
            var wifi = serial.Split('\n').Single(l => l.StartsWith("wifi ", StringComparison.Ordinal)).Trim();
            Assert.StartsWith("wifi 192.168.1.77 after ", wifi);
            Assert.InRange(int.Parse(wifi.Split(' ')[3]), 950, 1150);   // ~1 s of simulated time
            var q = serial.Split('\n').Where(l => l.StartsWith("q=", StringComparison.Ordinal)).ToList();
            Assert.True(q.Count >= 3, "queue lines: " + q.Count);
            // Producer runs every 50 ms: item 10 arrives ~500 ms after setup; the 10 ms task ran ~50 times by then.
            var line10 = q.First(l => l.StartsWith("q=10 ", StringComparison.Ordinal)).Trim();
            int fastAt10 = int.Parse(line10.Split(' ')[1].Substring(5));
            int tAt10 = int.Parse(line10.Split(' ')[2].Substring(2));
            Assert.InRange(tAt10, 500, 520);   // 10 × 50 ms after setup; millis() counts from the application's start, as on the chip
            Assert.InRange(fastAt10, 48, 53);
            Assert.True(rig.CoSim.WifiOn);
        }

        /// <summary>[pcnt-pwm] The chip counts its own LEDC PWM with its pulse counter (as the GPIO matrix feeds it on the hardware).</summary>
        [Esp32Fact]
        public void ThePulseCounterCountsTheChipsOwnPwm()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("pcnt-ledc"));
            rig.Run(0.5, 1e-3);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            var line = serial.Split('\n').Single(l => l.StartsWith("pwm ", StringComparison.Ordinal)).Trim().Split(' ');
            int a = int.Parse(line[1].Substring(2)), b = int.Parse(line[2].Substring(2));
            Assert.InRange(a, 199, 201);   // 1 kHz for 200 ms
            Assert.InRange(b - a, 398, 402);   // then 4 kHz for 100 ms
        }

        /// <summary>[pcnt-filter] 5 µs pulses at 20 kHz: the counter filtering up to 10 µs counts none, the one filtering up to 1 µs counts each.</summary>
        [Esp32Fact]
        public void ThePulseCountersGlitchFilterDropsShortPulses()
        {
            var (sch, board) = NewBoard();
            var gen = sch.AddPart(Lib, "signal-generator", new GridPoint(-40, 0));
            gen.Parameters["frequency"] = "20000";
            gen.Parameters["duty"] = "0.1";   // 5 µs high in each 50 µs
            gen.Parameters["amplitude"] = "3.3";
            gen.Parameters["rs"] = "0";
            Label(sch, gen, "+", "SIG");
            Label(sch, gen, "-", "GND");
            Label(sch, board, "IO4", "SIG");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("pcnt-filter"), new Esp32Options { BootDelay = 0.002 });
            rig.Run(0.010, 1e-6);   // fine slices: the board sees each edge within a microsecond
            string serial = rig.Serial.ToString();
            var last = serial.Split('\n').Last(l => l.StartsWith("wide=", StringComparison.Ordinal)).Trim();
            _out.WriteLine(serial.Length > 600 ? serial.Substring(serial.Length - 600) : serial);
            int wide = int.Parse(last.Split(' ')[0].Substring(5)), narrow = int.Parse(last.Split(' ')[1].Substring(7));
            Assert.Contains("too wide 259", serial);   // ESP_ERR_INVALID_STATE: the filter is set before enabling
            Assert.Equal(0, wide);
            Assert.InRange(narrow, 100, 200);   // one a pulse: 20 a millisecond over the ~7 ms the counters ran
        }

        /// <summary>[mem-budget] The fast mode runs on the PC's memory; the log says when a real ESP32 would have run out.</summary>
        [Esp32Fact]
        public void TheFastModeWarnsWhenARealBoardWouldRunOutOfMemory()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            using var rig = new Rig(compiled, _fx.Dll("memory"));
            var log = new List<string>();
            rig.CoSim.Log += log.Add;
            rig.Run(0.8, 1e-3);
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            _out.WriteLine(string.Join("\n", log));
            uint Free(string tag) => uint.Parse(serial.Split('\n').First(l => l.StartsWith(tag + " ", StringComparison.Ordinal)).Trim().Split(' ').Last());
            Assert.InRange(Free("boot") - Free("task"), 16384u, 16384u + 512);   // the task's stack came out of the heap
            Assert.Equal(0u, Free("chunk 6"));   // nothing left, as getFreeHeap would say on the chip
            Assert.Equal(2, rig.CoSim.MemoryWarning);
            Assert.True(rig.CoSim.PeakMemory > Esp32CoSimulation.HeapBudget);
            Assert.True(log.Count(l => l.Contains("yüzde")) <= 1);   // nearing the budget: said once at most (it may be passed between two checks)
            Assert.Single(log, l => l.Contains("bellek biterdi"));
        }

        [Esp32Fact]
        public void BrownOutAndEnButtonResetTheSketch()
        {
            var (sch, board) = NewBoard();
            board.Parameters["usb"] = "false";
            var psu = sch.AddPart(Lib, "psu-5v", new GridPoint(-40, 0));
            var sw = sch.AddPart(Lib, "toggle-spst", new GridPoint(-30, -10));
            var en = sch.AddPart(Lib, "tact-6mm", new GridPoint(-30, 10));
            sw.Parameters["closed"] = "true";
            Label(sch, psu, "+", "SRC");
            Label(sch, psu, "-", "GND");
            Label(sch, sw, "A", "SRC");
            Label(sch, sw, "B", "VBUS");
            Label(sch, board, "5V", "VBUS");
            Label(sch, en, "A", "ENPIN");
            Label(sch, en, "B", "GND");
            Label(sch, board, "EN", "ENPIN");
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var power = compiled.Device<RobotTwin.CoreSim.Analog.Devices.Switch>("SW1")!;
            var reset = compiled.Device<RobotTwin.CoreSim.Analog.Devices.Switch>("SW2")!;

            using var rig = new Rig(compiled, _fx.Dll("blink"));
            var log = new List<string>();
            rig.CoSim.Log += log.Add;
            rig.Run(0.3, 200e-6, t =>
            {
                if (t >= 0.10 && t < 0.1002) power.Closed = false;   // supply interrupted for 1 ms
                if (t >= 0.101 && t < 0.1012) power.Closed = true;
                if (t >= 0.20 && t < 0.2002) reset.Closed = true;    // EN button pressed for 20 ms
                if (t >= 0.22 && t < 0.2202) reset.Closed = false;
            });
            string serial = rig.Serial.ToString();
            _out.WriteLine(serial);
            _out.WriteLine(string.Join("\n", log));
            Assert.Contains("Brownout detector was triggered", serial);
            Assert.Contains("rst:0xf (RTCWDT_BROWN_OUT_RESET)", serial);
            Assert.Equal(3, rig.CoSim.BootCount);
            Assert.Equal(1, rig.CoSim.ResetReason);   // the last reset came from EN
            Assert.Equal(3, serial.Split(new[] { "ESP32 blink start" }, StringSplitOptions.None).Length - 1);
            Assert.Equal(Esp32BoardState.Running, rig.CoSim.State);
        }

        [Esp32Fact]
        public void CompileErrorMapsToInoLine()
        {
            var build = _fx.Builds["error"];
            _out.WriteLine(string.Join("\n", build.Diagnostics));
            Assert.False(build.Success);
            Assert.Null(build.DllPath);
            var error = build.Diagnostics.First(d => d.Severity == CompilerSeverity.Error);
            Assert.Equal(_fx.BadSketchPath, error.File, ignoreCase: true);
            Assert.Equal(6, error.Line);
            Assert.InRange(error.Column, 11, 30);
            Assert.Contains("undefinedVariable", error.Message);
        }

        [Esp32Fact]
        public void HungSketchTriggersWatchdogAndReboots()
        {
            var (sch, _) = NewBoard();
            var compiled = CircuitCompiler.Compile(sch, Lib);
            var options = new Esp32Options { BootDelay = 0.05, WatchdogMilliseconds = 1000 };
            var wall = Stopwatch.StartNew();
            using (var rig = new Rig(compiled, _fx.Dll("hung"), options))
            {
                rig.Run(0.8, 1e-3);
                string serial = rig.Serial.ToString();
                _out.WriteLine(serial);
                _out.WriteLine("wall: " + wall.Elapsed.TotalSeconds.ToString("F2") + " s");
                Assert.Contains("Task watchdog got triggered", serial);
                Assert.Contains("Rebooting...", serial);
                Assert.True(rig.CoSim.WatchdogResets >= 1);
                Assert.True(rig.CoSim.BootCount >= 2, "board should have rebooted");
                Assert.Equal(6, rig.CoSim.ResetReason);
                Assert.True(serial.IndexOf("about to hang", StringComparison.Ordinal) !=
                            serial.LastIndexOf("about to hang", StringComparison.Ordinal), "sketch should run again after the reset");
            }

            Assert.True(wall.Elapsed.TotalSeconds < 10, "took " + wall.Elapsed);
        }
    }
}
