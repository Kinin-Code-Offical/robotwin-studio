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
                ["rtos"] = Rtos
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
