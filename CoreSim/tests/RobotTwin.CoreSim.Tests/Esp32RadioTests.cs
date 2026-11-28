using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox.Radio;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Radio sketches compiled once for the class.</summary>
    public sealed class Esp32RadioFixture : IDisposable
    {
        // [web] the core's WebServer and DNSServer on a soft AP, and a station asking it over the world's network
        /// <summary>[mdns] A soft-AP that answers to isik.local and offers _http._tcp on 80.</summary>
        public const string MdnsHostSketch = @"
#include <WiFi.h>
#include <ESPmDNS.h>
void setup() {
  Serial.begin(115200);
  WiFi.softAP(""RW-AP"", ""12345678"");
  bool ok = MDNS.begin(""isik"");
  MDNS.addService(""http"", ""tcp"", 80);
  Serial.printf(""mdns %d\n"", ok);
}
void loop() { delay(100); }
";

        /// <summary>[mdns] A station on that AP asking for isik.local and the _http._tcp services.</summary>
        public const string MdnsAskSketch = @"
#include <WiFi.h>
#include <ESPmDNS.h>
void setup() {
  Serial.begin(115200);
  WiFi.begin(""RW-AP"", ""12345678"");
  while (WiFi.status() != WL_CONNECTED) delay(20);
  MDNS.begin(""soran"");
  IPAddress ip = MDNS.queryHost(""isik"");
  Serial.printf(""HOST %s\n"", ip.toString().c_str());
  int n = MDNS.queryService(""http"", ""tcp"");
  Serial.printf(""SVC %d %s %s %u\n"", n, MDNS.hostname(0).c_str(), MDNS.address(0).toString().c_str(), (unsigned)MDNS.port(0));
  IPAddress none = MDNS.queryHost(""yok"", 200);
  Serial.printf(""NONE %s\n"", none.toString().c_str());
}
void loop() { delay(100); }
";

        public const string WebServerSketch = @"
#include <WiFi.h>
#include <WebServer.h>
#include <DNSServer.h>
WebServer server(80);
DNSServer dns;
void setup() {
  Serial.begin(115200);
  WiFi.softAP(""RW-AP"", ""12345678"");
  dns.start(53, ""*"", WiFi.softAPIP());
  server.on(""/"", []() { server.send(200, ""text/plain"", ""merhaba""); });
  server.on(""/led"", HTTP_GET, []() { server.send(200, ""text/plain"", ""on="" + server.arg(""on"") + "" name="" + server.arg(""name"") + "" n="" + String(server.args())); });
  server.on(""/form"", HTTP_POST, []() { server.send(200, ""text/plain"", ""x="" + server.arg(""x"") + "" y="" + server.arg(""y"") + "" plain="" + server.arg(""plain"")); });
  server.on(""/gizli"", []() {
    if (!server.authenticate(""admin"", ""1234"")) return server.requestAuthentication();
    server.send(200, ""text/plain"", ""icerde"");
  });
  server.on(""/parca"", []() {
    server.setContentLength(CONTENT_LENGTH_UNKNOWN);
    server.send(200, ""text/plain"", """");
    server.sendContent(""bir "");
    server.sendContent(""iki"");
  });
  server.onNotFound([]() { server.send(404, ""text/plain"", ""yok: "" + server.uri()); });
  server.begin();
  Serial.println(""hazir "" + WiFi.softAPIP().toString());
}
void loop() {
  server.handleClient();
  dns.processNextRequest();
  delay(1);
}
";

        public const string WebClientSketch = @"
#include <WiFi.h>
#include <WiFiUdp.h>
static String get(const char* request) {
  WiFiClient c;
  if (!c.connect(IPAddress(192, 168, 4, 1), 80)) return ""baglanamadi"";
  c.print(request);
  String r;
  unsigned long t0 = millis();
  while ((c.connected() || c.available()) && millis() - t0 < 2000) {
    while (c.available()) r += (char)c.read();
    delay(1);
  }
  c.stop();
  int body = r.indexOf(""\r\n\r\n"");
  String status = r.substring(0, r.indexOf(""\r\n""));
  String rest = body < 0 ? String("""") : r.substring(body + 4);
  rest.replace(""\r\n"", ""~"");
  return status + "" | "" + rest;
}
void setup() {
  Serial.begin(115200);
  WiFi.begin(""RW-AP"", ""12345678"");
  while (WiFi.status() != WL_CONNECTED) delay(50);
  delay(200);
  Serial.println(""R1 "" + get(""GET /led?on=1&name=r%C3%BCzgar HTTP/1.1\r\nHost: 192.168.4.1\r\nConnection: close\r\n\r\n""));
  Serial.println(""R2 "" + get(""POST /form HTTP/1.1\r\nHost: x\r\nContent-Type: application/x-www-form-urlencoded\r\nContent-Length: 10\r\n\r\nx=42&y=a+b""));
  Serial.println(""R3 "" + get(""GET /yok HTTP/1.1\r\nHost: x\r\n\r\n""));
  Serial.println(""R4 "" + get(""GET /gizli HTTP/1.1\r\nHost: x\r\nAuthorization: Basic YWRtaW46MTIzNA==\r\n\r\n""));
  Serial.println(""R5 "" + get(""GET /gizli HTTP/1.1\r\nHost: x\r\n\r\n""));
  Serial.println(""R6 "" + get(""GET /parca HTTP/1.1\r\nHost: x\r\n\r\n""));
  WiFiUDP u;
  u.begin(5353);
  uint8_t qd[] = {0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 5, 'r', 'o', 'b', 'o', 't', 5, 'l', 'o', 'c', 'a', 'l', 0, 0, 1, 0, 1};
  u.beginPacket(IPAddress(192, 168, 4, 1), 53);
  u.write(qd, sizeof qd);
  u.endPacket();
  unsigned long t0 = millis();
  int got = 0;
  while (millis() - t0 < 1000 && (got = u.parsePacket()) == 0) delay(5);
  uint8_t a[128] = {0};
  int n = got > 0 ? u.read(a, sizeof a) : 0;
  Serial.printf(""DNS n=%d id=%02X%02X rcode=%d ip=%d.%d.%d.%d\n"", n, a[0], a[1], n > 3 ? (a[3] & 15) : -1, n >= 4 ? a[n - 4] : 0, n >= 4 ? a[n - 3] : 0, n >= 4 ? a[n - 2] : 0, n >= 4 ? a[n - 1] : 0);
}
void loop() { delay(100); }
";

        public const string BleServer = @"
#include <BLEDevice.h>
#include <BLEServer.h>
#include <BLE2902.h>
#define SVC ""4fafc201-1fb5-459e-8fcc-c5c9c331914b""
#define CHR ""beb5483e-36e1-4688-b7f5-ea07361b26a8""
BLECharacteristic* ch;
volatile int clients = 0;
class Cb : public BLEServerCallbacks {
  void onConnect(BLEServer* s) { clients++; Serial.println(""client connected""); }
  void onDisconnect(BLEServer* s) { clients--; }
};
void setup() {
  Serial.begin(115200);
  BLEDevice::init(""RW-Sensor"");
  BLEServer* server = BLEDevice::createServer();
  server->setCallbacks(new Cb());
  BLEService* svc = server->createService(SVC);
  ch = svc->createCharacteristic(CHR, BLECharacteristic::PROPERTY_READ | BLECharacteristic::PROPERTY_NOTIFY);
  ch->addDescriptor(new BLE2902());
  svc->start();
  BLEAdvertising* adv = BLEDevice::getAdvertising();
  adv->addServiceUUID(SVC);
  BLEDevice::startAdvertising();
  Serial.println(""advertising as "" + BLEDevice::getAddress().toString());
}
int n = 100;
void loop() {
  ch->setValue(n);
  ch->notify();
  n++;
  delay(100);
}
";

        public const string BleScanner = @"
#include <BLEDevice.h>
#define SVC ""4fafc201-1fb5-459e-8fcc-c5c9c331914b""
#define CHR ""beb5483e-36e1-4688-b7f5-ea07361b26a8""
volatile int notes = 0;
volatile int lastValue = -1;
void onNotify(BLERemoteCharacteristic* c, uint8_t* data, size_t len, bool isNotify) {
  notes++;
  if (len >= 4) memcpy((void*)&lastValue, data, 4);
}
void setup() {
  Serial.begin(115200);
  BLEDevice::init(""RW-Scanner"");
  BLEScan* scan = BLEDevice::getScan();
  scan->setActiveScan(true);
  BLEScanResults found = scan->start(2, false);
  Serial.printf(""found=%d\n"", found.getCount());
  for (int i = 0; i < found.getCount(); i++) {
    BLEAdvertisedDevice d = found.getDevice(i);
    Serial.printf(""dev name=%s addr=%s rssi=%d svc=%d\n"", d.getName().c_str(), d.getAddress().toString().c_str(), d.getRSSI(),
                  d.isAdvertisingService(BLEUUID(SVC)) ? 1 : 0);
    if (d.getName() == ""RW-Sensor"") {
      BLEClient* client = BLEDevice::createClient();
      if (client->connect(&d)) {
        BLERemoteService* s = client->getService(SVC);
        BLERemoteCharacteristic* r = s ? s->getCharacteristic(CHR) : nullptr;
        if (r && r->canNotify()) {
          r->registerForNotify(onNotify);
          Serial.println(""subscribed"");
        }
      }
    }
  }
}
void loop() {
  delay(250);
  Serial.printf(""notes=%d last=%d\n"", notes, lastValue);
}
";

        public const string UdpPing = @"
#include <WiFi.h>
#include <WiFiUdp.h>
WiFiUDP udp;
IPAddress peer;
void setup() {
  Serial.begin(115200);
  WiFi.mode(WIFI_STA);
  WiFi.begin(""RoboTwin-Ev"", ""12345678"");
  while (WiFi.status() != WL_CONNECTED) delay(50);
  Serial.println(""ip "" + WiFi.localIP().toString() + "" rssi "" + String(WiFi.RSSI()));
  udp.begin(4210);
}
unsigned long last = 0;
int seq = 0;
void loop() {
  int len = udp.parsePacket();
  if (len > 0) {
    char buf[64] = {0};
    udp.read(buf, sizeof(buf) - 1);
    Serial.printf(""rx from %s: %s\n"", udp.remoteIP().toString().c_str(), buf);
  }
  if (millis() - last > 200) {
    last = millis();
    udp.beginPacket(IPAddress(192, 168, 1, 255), 4210);
    udp.printf(""hello %d from %s"", seq++, WiFi.localIP().toString().c_str());
    udp.endPacket();
  }
  delay(5);
}
";

        /// <summary>[espnow] A board that broadcasts a counter by ESP-NOW, answers what it hears, and counts the sends' outcomes.</summary>
        public const string EspNow = @"
#include <WiFi.h>
#include <esp_now.h>

uint8_t bcast[6] = {0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF};
volatile int rx = 0, ok = 0, fail = 0;
char last[64];
int lastRssi = 0;

void onRecv(const esp_now_recv_info_t *info, const uint8_t *data, int len) {
  rx++;
  lastRssi = info->rx_ctrl->rssi;
  int n = len < 63 ? len : 63;
  memcpy(last, data, n);
  last[n] = 0;
}

void onSent(const uint8_t *mac, esp_now_send_status_t status) {
  if (status == ESP_NOW_SEND_SUCCESS) ok++; else fail++;
}

void setup() {
  Serial.begin(115200);
  Serial.printf(""init before wifi %d\n"", (int)esp_now_init());
  WiFi.mode(WIFI_STA);
  Serial.printf(""init %d\n"", (int)esp_now_init());
  esp_now_register_recv_cb(onRecv);
  esp_now_register_send_cb(onSent);
  uint8_t ghost[6] = {0x02, 0x11, 0x22, 0x33, 0x44, 0x55};
  Serial.printf(""unknown peer %d\n"", (int)esp_now_send(ghost, (const uint8_t *)""x"", 1));
  esp_now_peer_info_t peer = {};
  memcpy(peer.peer_addr, bcast, 6);
  esp_now_add_peer(&peer);
  memcpy(peer.peer_addr, ghost, 6);
  esp_now_add_peer(&peer);
}

int seq = 0;
unsigned long lastSend = 0;
void loop() {
  if (millis() - lastSend >= 100) {
    lastSend = millis();
    char msg[32];
    snprintf(msg, sizeof msg, ""%s %d"", WiFi.macAddress().c_str(), seq++);
    esp_now_send(bcast, (const uint8_t *)msg, strlen(msg) + 1);
    if (seq == 5) {
      uint8_t ghost[6] = {0x02, 0x11, 0x22, 0x33, 0x44, 0x55};
      esp_now_send(ghost, (const uint8_t *)""?"", 2);   // nobody has this address: no ACK
    }
  }
  if (seq == 20) {
    Serial.printf(""rx %d ok %d fail %d rssi %d last %s\n"", rx, ok, fail, lastRssi, last);
    seq++;
  }
  delay(5);
}
";

        public Esp32RadioFixture()
        {
            Available = Esp32SketchCompiler.IsToolchainAvailable;
            OutputDir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-radio-tests", Guid.NewGuid().ToString("N").Substring(0, 8));
            if (!Available) return;
            Directory.CreateDirectory(OutputDir);
            GamepadCar = FindExample("esp32-gamepad-car.ino");
            var sources = new Dictionary<string, string>
            {
                ["ble-server"] = BleServer,
                ["ble-scanner"] = BleScanner,
                ["udp"] = UdpPing,
                ["car"] = GamepadCar,
                ["espnow"] = EspNow,
                ["web-server"] = WebServerSketch,
                ["web-client"] = WebClientSketch,
                ["mdns-host"] = MdnsHostSketch,
                ["mdns-ask"] = MdnsAskSketch
            };
            var compiler = new Esp32SketchCompiler();
            var results = new ConcurrentDictionary<string, Esp32BuildResult>();
            Parallel.ForEach(sources, kv => results[kv.Key] = compiler.Compile(kv.Value, Path.Combine(OutputDir, kv.Key)));
            Builds = new Dictionary<string, Esp32BuildResult>(results);
        }

        /// <summary>The example sketch shipped with the Studio (so the test checks what users get).</summary>
        private static string FindExample(string name)
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null)
            {
                string p = Path.Combine(d.FullName, "Studio", "RobotWin.Studio", "Firmware", name);
                if (File.Exists(p)) return File.ReadAllText(p);
                d = d.Parent;
            }

            throw new FileNotFoundException(name);
        }

        public bool Available { get; }
        public string OutputDir { get; }
        public string GamepadCar { get; } = string.Empty;
        public Dictionary<string, Esp32BuildResult> Builds { get; } = new Dictionary<string, Esp32BuildResult>();

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
    public class Esp32RadioTests : IClassFixture<Esp32RadioFixture>
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;
        private readonly Esp32RadioFixture _fx;
        private readonly ITestOutputHelper _out;

        public Esp32RadioTests(Esp32RadioFixture fx, ITestOutputHelper output)
        {
            _fx = fx;
            _out = output;
        }

        /// <summary>One ESP32 board in its own circuit, attached to a shared medium.</summary>
        private sealed class Board : IDisposable
        {
            public Board(RadioMedium medium, string id, Vec3 position, string dll)
            {
                var sch = new Schematic { Title = id };
                var part = sch.AddPart(Lib, Esp32Parts.DevKitCId, new GridPoint(0, 0));
                var pin = Lib.Get(part.PartId).FindPin("GND2")!;
                sch.Labels.Add(new NetLabel { Name = "GND", Position = part.Transform(pin.X, pin.Y) });
                Compiled = CircuitCompiler.Compile(sch, Lib);
                Sim = new AnalogSimulator(Compiled.Circuit);
                Sim.Initialize(InitialState.OperatingPoint);
                CoSim = new Esp32CoSimulation(dll, Compiled, "U1", null, new Esp32Options { BootDelay = 0.05 });
                CoSim.SerialOutput += s => Serial.Append(s);
                CoSim.Log += s => Log.Add(s);
                Node = medium.Add(new Esp32RadioNode(id, id, WorldRadio.MacFor(id, new byte[] { 0x24, 0x0A, 0xC4 })));
                Node.Position = () => position;
                Esp32RadioBridge.Attach(medium, Node, CoSim);
            }

            public CompiledCircuit Compiled { get; }
            public AnalogSimulator Sim { get; }
            public Esp32CoSimulation CoSim { get; }
            public Esp32RadioNode Node { get; }
            public StringBuilder Serial { get; } = new StringBuilder();
            public List<string> Log { get; } = new List<string>();

            public void Step(double dt)
            {
                CoSim.Advance(dt, Sim.Time);
                Sim.Step(dt);
            }

            public void Dispose() => CoSim.Dispose();
        }

        private static void Run(RadioMedium m, double seconds, double slice, params Board[] boards)
        {
            int n = (int)Math.Round(seconds / slice);
            for (int i = 0; i < n; i++)
            {
                double t = boards[0].Sim.Time;
                m.Advance(t, slice);
                foreach (var b in boards) b.Step(slice);
            }
        }

        [Esp32Fact]
        public void BleScanBetweenTwoSketchesFindsAdvertiserAndReceivesNotifications()
        {
            var m = new RadioMedium();
            using var server = new Board(m, "server", new Vec3(0, 0, 0.05), _fx.Dll("ble-server"));
            using var scanner = new Board(m, "scanner", new Vec3(1.5, 0.5, 0.05), _fx.Dll("ble-scanner"));
            Run(m, 4.0, 1e-3, server, scanner);
            string s = scanner.Serial.ToString();
            _out.WriteLine(server.Serial.ToString());
            _out.WriteLine(s);
            Assert.Contains("found=1", s);
            var dev = s.Split('\n').Single(l => l.StartsWith("dev name=RW-Sensor", StringComparison.Ordinal));
            Assert.Contains("addr=" + server.Node.BluetoothMacText.ToLowerInvariant(), dev);
            Assert.Contains("svc=1", dev);
            int rssi = int.Parse(dev.Substring(dev.IndexOf("rssi=", StringComparison.Ordinal) + 5).Split(' ')[0]);
            double expected = m.MeanRssi(server.Node, scanner.Node, RadioTech.Ble);
            Assert.InRange(rssi, expected - 14, expected + 14);   // ± 3.5 σ of the shadow fading
            Assert.Contains("subscribed", s);
            Assert.Contains("client connected", server.Serial.ToString());
            var last = s.Split('\n').Last(l => l.StartsWith("notes=", StringComparison.Ordinal));
            int notes = int.Parse(last.Split(' ')[0].Substring(6));
            int value = int.Parse(last.Split(' ')[1].Substring(5).Trim());
            _out.WriteLine(last);
            Assert.True(notes >= 5, "notifications: " + notes);
            Assert.True(value > 100, "value " + value);
        }

        [Esp32Fact]
        public void GamepadPairsWithBluepad32SketchAndDrivesMotors()
        {
            var m = new RadioMedium();
            using var car = new Board(m, "car", new Vec3(0, 0, 0.05), _fx.Dll("car"));
            var padPos = new Vec3(0.6, -0.4, 0.8);
            var pad = m.Add(new GamepadNode("pad", "DualSense", new byte[] { 0xA0, 0xAB, 0x51, 0x10, 0x20, 0x30 }, GamepadModel.DualSense));
            pad.Position = () => padPos;
            Run(m, 0.5, 1e-3, car);
            Assert.False(pad.Connected);
            m.StartPairing(pad);
            Run(m, 2.0, 1e-3, car);
            string serial = car.Serial.ToString();
            _out.WriteLine(serial);
            Assert.True(pad.Connected, pad.Status);
            Assert.Contains("Kumanda baglandi: DualSense (idx 0, A0:AB:51:10:20:30)", serial);
            Assert.Equal(car.Node.BluetoothMacText, pad.BondedHost);
            Assert.Equal(255, pad.Feedback.Green);   // setColorLED(0, 255, 64) reached the controller
            Assert.True(pad.Feedback.StrongMotor > 0);

            // Full throttle with R2, steering right.
            pad.Input.Throttle = 1023;
            pad.Input.AxisX = GamepadState.Axis(0.5);
            Run(m, 0.3, 1e-3, car);
            var left = car.CoSim.GetPin(25);
            var right = car.CoSim.GetPin(27);
            _out.WriteLine($"AIN1 duty {left.PwmDuty:F3}, BIN1 duty {right.PwmDuty:F3}");
            Assert.True(left.PwmAttached && left.PwmDuty > 0.95, "left " + left.PwmDuty);
            Assert.InRange(right.PwmDuty, 0.6, 0.7);   // gas 255 − steer 85 = 170 of 8 bits
            Assert.Equal(0.0, car.CoSim.GetPin(26).PwmDuty, 6);

            // Out of range: the link times out, the sketch stops the motors.
            padPos = new Vec3(500, 0, 0.8);
            Run(m, 2.6, 1e-3, car);
            serial = car.Serial.ToString();
            Assert.False(pad.Connected);
            Assert.Contains("Kumanda ayrildi, motorlar durdu", serial);
            Assert.Equal(0.0, car.CoSim.GetPin(25).PwmDuty, 6);

            // Back in range: reconnects without pairing (bonded).
            padPos = new Vec3(0.5, 0, 0.8);
            Run(m, 1.0, 1e-3, car);
            Assert.True(pad.Connected);
            Assert.Equal(2, car.Serial.ToString().Split(new[] { "Kumanda baglandi" }, StringSplitOptions.None).Length - 1);
        }

        [Esp32Fact]
        public void WorldWithUsbPoweredEsp32AndBondedControllerReconnectsAndDrivesDrv8833()
        {
            var world = new RobotTwin.CoreSim.Sandbox.WorldDocument();
            var esp = world.AddComponent(Lib, "esp32-devkit-v1", new RobotTwin.CoreSim.Sandbox.WorldPose());
            esp.Firmware = new RobotTwin.CoreSim.Formats.FirmwareSource { Source = _fx.GamepadCar, Target = "esp32-hal" };
            var drv = world.AddComponent(Lib, "drv8833", new RobotTwin.CoreSim.Sandbox.WorldPose { X = -0.05 });
            var bat = world.AddComponent(Lib, "bat-4aa", new RobotTwin.CoreSim.Sandbox.WorldPose { X = -0.1 });
            var motor = world.AddComponent(Lib, "motor-tt", new RobotTwin.CoreSim.Sandbox.WorldPose { X = -0.15 });
            var pc = world.AddComponent(Lib, "usb-pc", new RobotTwin.CoreSim.Sandbox.WorldPose { X = 0.4 });
            pc.Parameters["target"] = esp.Id;
            var pad = world.AddComponent(Lib, "dualsense", new RobotTwin.CoreSim.Sandbox.WorldPose { Y = -0.3 });
            pad.Parameters["pairedWith"] = esp.Id;
            RobotTwin.CoreSim.Sandbox.CableEnd E(RobotTwin.CoreSim.Sandbox.WorldObject o, string pin) => new RobotTwin.CoreSim.Sandbox.CableEnd { ObjectId = o.Id, Pin = pin };
            world.Connect(E(bat, "+"), E(drv, "VM"));
            world.Connect(E(bat, "-"), E(drv, "GND"));
            world.Connect(E(esp, "GND1"), E(drv, "GND"));
            world.Connect(E(esp, "IO25"), E(drv, "AIN1"));
            world.Connect(E(esp, "IO26"), E(drv, "AIN2"));
            world.Connect(E(drv, "AOUT1"), E(motor, "A"));
            world.Connect(E(drv, "AOUT2"), E(motor, "B"));
            var circuit = RobotTwin.CoreSim.Sandbox.WorldCompiler.Compile(world, Lib);
            var medium = WorldRadio.Build(world, circuit, Lib);
            var prog = circuit.Programmables.Single();
            var sim = new AnalogSimulator(circuit.Compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            using var cosim = new Esp32CoSimulation(_fx.Dll("car"), circuit.Compiled, prog.RefDes, null, new Esp32Options { BootDelay = 0.05 });
            var serial = new StringBuilder();
            cosim.SerialOutput += s => serial.Append(s);
            Esp32RadioBridge.Attach(medium, WorldRadio.NodeOf(medium, prog, world)!, cosim);
            var padNode = (GamepadNode)medium.Find(pad.Id)!;
            Assert.NotNull(padNode.BondedHost);
            void Run(double seconds)
            {
                for (int i = 0; i < (int)Math.Round(seconds / 2e-4); i++)
                {
                    cosim.Advance(2e-4, sim.Time);
                    medium.Advance(sim.Time, 2e-4);
                    sim.Step(2e-4);
                }
            }

            Run(1.2);
            _out.WriteLine(serial.ToString());
            Assert.Equal(Esp32BoardState.Running, cosim.State);   // powered over the USB cable
            Assert.True(padNode.Connected, padNode.Status);       // bonded: reconnects without pairing mode
            padNode.Input.Throttle = 1023;
            Run(0.6);
            double rpm = circuit.Compiled.DevicesOf(circuit.ComponentRef(motor.Id)!).OfType<RobotTwin.CoreSim.Analog.Devices.DcMotor>().Single().OutputRpm;
            _out.WriteLine($"motor {rpm:F0} rpm, AOUT1 {circuit.Compiled.PinVoltage(sim, circuit.ComponentRef(drv.Id)!, "AOUT1"):F2} V");
            Assert.True(rpm > 50, "motor should turn under R2: " + rpm);

            // Unplugging the USB cable powers the ESP32 down: the controller link drops after the supervision timeout.
            Assert.True(RobotTwin.CoreSim.Sandbox.Radio.WorldUsb.SetPlugged(circuit, esp.Id, false));
            Run(2.6);
            Assert.Equal(Esp32BoardState.Off, cosim.State);
            Assert.False(padNode.Connected);
        }

        /// <summary>[espnow] Two boards near each other hear each other's ESP-NOW broadcasts; a third 2 km away hears nothing (1 Mbps reaches a few hundred metres in the open).</summary>
        [Esp32Fact]
        public void EspNowBroadcastsReachTheBoardsInRange()
        {
            var m = new RadioMedium();
            using var a = new Board(m, "a", new Vec3(0, 0, 0.05), _fx.Dll("espnow"));
            using var b = new Board(m, "b", new Vec3(3, 0, 0.05), _fx.Dll("espnow"));
            using var far = new Board(m, "far", new Vec3(2000, 0, 0.05), _fx.Dll("espnow"));
            Run(m, 2.6, 1e-3, a, b, far);
            string sa = a.Serial.ToString(), sb = b.Serial.ToString(), sf = far.Serial.ToString();
            _out.WriteLine(sa);
            _out.WriteLine(sb);
            _out.WriteLine(sf);
            Assert.Contains("init before wifi 12394", sa);   // ESP_ERR_ESPNOW_INTERNAL: Wi-Fi not started
            Assert.Contains("init 0", sa);
            Assert.Contains("unknown peer 12393", sa);   // ESP_ERR_ESPNOW_NOT_FOUND: not a registered peer
            string Line(string text) => text.Split('\n').Single(l => l.StartsWith("rx ", StringComparison.Ordinal)).Trim();
            var la = Line(sa).Split(' ');
            var lb = Line(sb).Split(' ');
            var lf = Line(sf).Split(' ');
            Assert.InRange(int.Parse(la[1]), 15, 21);   // b's broadcasts, a few may fade out
            Assert.InRange(int.Parse(lb[1]), 15, 21);
            Assert.Equal("0", lf[1]);   // out of reach
            Assert.Equal("1", la[5]);   // the send to the address nobody has: no ACK after its retries
            Assert.True(int.Parse(la[3]) >= 18, "broadcasts always report success");
            Assert.Equal(b.Node.MacText.ToUpperInvariant(), la[9].ToUpperInvariant());   // what a heard came from b
            int rssi = int.Parse(la[7]);
            double expected = m.MeanRssi(b.Node, a.Node, RadioTech.WiFi);
            Assert.InRange(rssi, expected - 14, expected + 14);
        }

        [Esp32Fact]
        public void AWebServerOnASoftApAnswersAnotherBoard()
        {
            var m = new RadioMedium();
            m.ImplicitNetworkEnabled = false;
            using var server = new Board(m, "server", new Vec3(0, 0, 0.05), _fx.Dll("web-server"));
            using var client = new Board(m, "client", new Vec3(1.5, 0, 0.05), _fx.Dll("web-client"));
            Run(m, 4.0, 1e-3, server, client);
            string text = client.Serial.ToString();
            _out.WriteLine(server.Serial.ToString());
            _out.WriteLine(text);
            Assert.Contains("hazir 192.168.4.1", server.Serial.ToString());
            Assert.Contains("R1 HTTP/1.1 200 OK | on=1 name=rüzgar n=2", text);
            Assert.Contains("R2 HTTP/1.1 200 OK | x=42 y=a b plain=x=42&y=a+b", text);
            Assert.Contains("R3 HTTP/1.1 404 Not Found | yok: /yok", text);
            Assert.Contains("R4 HTTP/1.1 200 OK | icerde", text);
            Assert.Contains("R5 HTTP/1.1 401 Unauthorized |", text);
            Assert.Contains("R6 HTTP/1.1 200 OK | 4~bir ~3~iki~0~~", text);   // chunked
            Assert.Contains("DNS n=45 id=1234 rcode=0 ip=192.168.4.1", text);
        }

        /// <summary>[mdns] ESPmDNS between two fast-mode boards: the name and the service are found through the medium.</summary>
        [Esp32Fact]
        public void MdnsFindsANameAndAServiceOnTheNetwork()
        {
            var m = new RadioMedium();
            m.ImplicitNetworkEnabled = false;
            using var host = new Board(m, "host", new Vec3(0, 0, 0.05), _fx.Dll("mdns-host"));
            using var ask = new Board(m, "ask", new Vec3(1.5, 0, 0.05), _fx.Dll("mdns-ask"));
            Run(m, 5.0, 1e-3, host, ask);
            string text = ask.Serial.ToString();
            _out.WriteLine(host.Serial.ToString());
            _out.WriteLine(text);
            Assert.Contains("mdns 1", host.Serial.ToString());
            Assert.Contains("HOST 192.168.4.1", text);
            Assert.Contains("SVC 1 isik 192.168.4.1 80", text);
            Assert.Contains("NONE 0.0.0.0", text);
        }

        [Esp32Fact]
        public void UdpBroadcastBetweenEsp32sThroughARouter()
        {
            var m = new RadioMedium();
            var ap = m.Add(new AccessPointNode("router", "Modem", new byte[] { 0x5C, 0x49, 0x79, 1, 2, 4 }, new WifiNetwork { Ssid = "RoboTwin-Ev", Password = "12345678", Gateway = RadioMedium.Ip(192, 168, 1, 1), NextHost = 100 }));
            ap.Position = () => new Vec3(0, 2, 1.0);
            m.ImplicitNetworkEnabled = false;
            using var a = new Board(m, "a", new Vec3(-1, 0, 0.05), _fx.Dll("udp"));
            using var b = new Board(m, "b", new Vec3(2, 0, 0.05), _fx.Dll("udp"));
            Run(m, 3.0, 1e-3, a, b);
            _out.WriteLine(a.Serial.ToString());
            _out.WriteLine(b.Serial.ToString());
            string ipA = RadioMedium.FormatIp(a.Node.StaIp), ipB = RadioMedium.FormatIp(b.Node.StaIp);
            Assert.StartsWith("192.168.1.10", ipA);
            Assert.Contains("ip " + ipA + " rssi -", a.Serial.ToString());
            Assert.Contains("rx from " + ipA + ": hello", b.Serial.ToString());
            Assert.Contains("rx from " + ipB + ": hello", a.Serial.ToString());
        }
    }
}
