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
                ["espnow"] = EspNow
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
