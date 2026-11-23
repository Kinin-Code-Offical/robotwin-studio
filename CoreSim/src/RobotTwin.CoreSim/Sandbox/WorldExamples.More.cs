using System;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// More world templates: a rover whose electronics live in a closed box with real cable holes, a compound
    /// pendulum to check the mechanics against theory, and a traffic light with a pedestrian button on an ESP32.
    /// </summary>
    public static partial class WorldExamples
    {
        /// <summary>
        /// [route] A two-wheel rover with its electronics in a closed, screwed PLA box on the chassis: a 4×AA pack, an
        /// ESP32 and a DRV8833 motor driver. The motor leads leave the box through a grommet low in its back wall and run
        /// over the chassis' free rear edge down to the motors' terminals underneath (not past the wheels). The ESP32
        /// drives a square: forward, a quarter turn on the spot, again.
        /// </summary>
        public static WorldDocument EnclosedRover(PartLibrary lib, double motorHeight = 0.0188, double shaftX = -0.011, double shaftZ = 0.0094, double shaftTip = 0.0185)
        {
            var w = new WorldDocument
            {
                Title = "Kapalı gövdeli gezgin",
                Description = "Elektronik kapalı kutuda; motor kabloları yan deliklerden çıkar. ESP32 kare çizer."
            };
            const double r = 0.0325;
            double motorZ = r - shaftZ, plateZ = motorZ + motorHeight, top = plateZ + 0.003;
            var plate = w.AddBody("plate", 0.16, 0.09, 0.003, new WorldPose { Z = plateZ }, "PLA");
            plate.Name = "Şase";
            const double axleX = -0.03 - 0.011;
            var ml = w.AddComponent(lib, "motor-tt", new WorldPose { X = axleX - shaftX, Y = 0.03, Z = motorZ });
            ml.Name = "Sol motor";
            var mr = w.AddComponent(lib, "motor-tt", new WorldPose { X = axleX - shaftX, Y = -0.03, Z = motorZ + motorHeight, Roll = 180 });
            mr.Name = "Sağ motor";
            var wl = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = axleX, Y = 0.03 + shaftTip + 0.015, Z = 0 }, "KAUÇUK");
            wl.Name = "Sol teker";
            var wr = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = axleX, Y = -0.03 - shaftTip - 0.015, Z = motorHeight - 2 * shaftZ }, "KAUÇUK");
            wr.Name = "Sağ teker";
            const double ball = 0.016;
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = 0.06 }, "ÇELİK");
            caster.Name = "Sarhoş teker";
            var post = w.AddBody("box", 0.012, 0.012, plateZ - ball * 0.8, new WorldPose { X = 0.06, Z = ball * 0.8 }, "PLA");
            post.Name = "Teker ayağı";

            // The box: 120 × 70 × 45 mm, 2 mm walls, a 10 mm grommet in its back wall 25 mm above its floor (above the
            // battery pack standing against that wall); the lid is screwed down.
            const double boxX = 0.015, boxH = 0.045, wall = 0.002;
            var box = w.AddBody("box", 0.12, 0.07, boxH, new WorldPose { X = boxX, Z = top }, "PLA");
            box.Name = "Elektronik kutusu";
            box.MassKg = 0.045;
            box.Color = "#3A3F47";
            box.Parameters["shell"] = "true";
            box.Parameters["wall"] = Inv(wall);
            box.Parameters["holes"] = "-x";
            box.Parameters["hole"] = "0.010";
            box.Parameters["hole.z"] = "0.025";
            var lid = w.AddBody("box", 0.12, 0.07, 0.002, new WorldPose { X = boxX, Z = top + boxH }, "PLA");
            lid.Name = "Kutu kapağı";
            lid.MassKg = 0.012;
            lid.Color = "#3A3F47";
            lid.Parameters["lid.of"] = box.Id;
            lid.Parameters["screwed"] = "true";

            double floor = top + wall;
            var bat = w.AddComponent(lib, "bat-4aa", new WorldPose { X = -0.013, Z = floor });
            bat.Name = "4×AA pil kutusu";
            // The board turned lengthwise on its header pins in foam tape; the driver breakout beside it.
            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.030, Y = -0.031, Z = OnPins(floor), Yaw = 90 });
            esp.Name = "ESP32";
            esp.Firmware = new Formats.FirmwareSource { FileName = "kare-gezgin.ino", Target = "esp32:esp32:esp32", Source = SquareRoverSketch };
            var drv = w.AddComponent(lib, "drv8833", new WorldPose { X = 0.040, Y = 0.015, Z = OnPins(floor), Yaw = 90 });
            drv.Name = "DRV8833 motor sürücü";

            foreach (var o in new[] { ml, mr, post, caster, box })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = plate.Id });
            foreach (var o in new[] { bat, esp, drv })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = box.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wl.Id, ObjectB = ml.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wr.Id, ObjectB = mr.Id, PortB = "shaft" });

            void Wire(WorldObject a, string pa, WorldObject b, string pb, double awg, string colour)
            {
                var cable = w.Connect(new CableEnd { ObjectId = a.Id, Pin = pa }, new CableEnd { ObjectId = b.Id, Pin = pb }, awg, colour);
                cable.Length = 0;   // cut to the way between the pins
            }

            Wire(bat, "+", drv, "VM", 22, "#E5484D");
            Wire(bat, "-", drv, "GND", 22, "#1F1F23");
            Wire(bat, "+", esp, "VIN", 24, "#E5484D");
            Wire(bat, "-", esp, "GND1", 24, "#1F1F23");
            Wire(esp, "IO25", drv, "AIN1", 26, "#F2A33B");
            Wire(esp, "IO26", drv, "AIN2", 26, "#F2D04B");
            Wire(esp, "IO27", drv, "BIN1", 26, "#3CD064");
            Wire(esp, "IO14", drv, "BIN2", 26, "#3E7CC8");
            Wire(drv, "AOUT1", ml, "A", 22, "#E5484D");
            Wire(drv, "AOUT2", ml, "B", 22, "#1F1F23");
            // The right motor is mounted mirrored, so it is wired the other way round to drive forward too.
            Wire(drv, "BOUT1", mr, "B", 22, "#E5484D");
            Wire(drv, "BOUT2", mr, "A", 22, "#1F1F23");
            return w;
        }

        /// <summary>Sketch of <see cref="EnclosedRover"/>: a square, timed (no sensors), the driver's inputs PWM'd.</summary>
        public const string SquareRoverSketch = @"// Kare çizen gezgin: ESP32 + DRV8833, iki TT motor.
// Sensör yok, süreyle: 1,6 s ileri, 0,45 s yerinde sağa dönüş, tekrar.
// DRV8833: AIN1/AIN2 sol motor, BIN1/BIN2 sağ motor. Bir girişe PWM, diğerine 0: o yönde döner.
#include <Arduino.h>

const int AIN1 = 25, AIN2 = 26, BIN1 = 27, BIN2 = 14;
const int HIZ = 190;   // 0..255

void sur(int sol, int sag) {
  analogWrite(AIN1, sol > 0 ? sol : 0);
  analogWrite(AIN2, sol < 0 ? -sol : 0);
  analogWrite(BIN1, sag > 0 ? sag : 0);
  analogWrite(BIN2, sag < 0 ? -sag : 0);
}

void setup() {
  Serial.begin(115200);
  sur(0, 0);
  Serial.println(""Kare gezgin: 1 s sonra başlıyor"");
  delay(1000);
}

int kenar = 0;

void loop() {
  Serial.print(""Kenar "");
  Serial.println(kenar + 1);
  sur(HIZ, HIZ);
  delay(1600);
  sur(HIZ, -HIZ);
  delay(450);
  sur(0, 0);
  delay(200);
  kenar = (kenar + 1) % 4;
}
";

        /// <summary>
        /// A compound pendulum on a ball-bearing pivot: a 280 mm aluminium flat bar and a 40 mm steel ball, let go from
        /// 30°. Its period follows T = 2π√(I/(m·g·h)) (with the large-swing correction, ≈ 1.15 s here) and its swing dies
        /// away slowly through air drag and the bearing's friction.
        /// </summary>
        public static WorldDocument Pendulum(PartLibrary lib)
        {
            var w = new WorldDocument
            {
                Title = "Sarkaç",
                Description = "30°'den bırakılan bileşik sarkaç: periyot teoriyle karşılaştırılabilir, hava ve yatak sürtünmesiyle söner."
            };
            const double h = 0.42, rod = 0.28, bob = 0.04, angle = 30;
            var foot = w.AddBody("box", 0.20, 0.16, 0.018, new WorldPose(), "AHŞAP");
            foot.Name = "Taban";
            foot.IsStatic = true;
            foot.MassKg = 0.4;
            // Two uprights and a cross bar: the pivot sits under the bar's middle.
            foreach (double y in new[] { -0.06, 0.06 })
            {
                var up = w.AddBody("box", 0.02, 0.02, h - 0.018, new WorldPose { Y = y, Z = 0.018 }, "AHŞAP");
                up.Name = y < 0 ? "Sağ dikme" : "Sol dikme";
                up.IsStatic = true;
            }

            var bar = w.AddBody("box", 0.02, 0.14, 0.02, new WorldPose { Z = h }, "AHŞAP");
            bar.Name = "Üst kiriş";
            bar.IsStatic = true;
            var bearing = w.AddBody("box", 0.02, 0.02, 0.024, new WorldPose { Z = h - 0.024 }, "ALÜMİNYUM");
            bearing.Name = "Rulman yatağı";
            bearing.IsStatic = true;

            // The axle runs through the bearing block 12 mm under the cross bar and a hole 6 mm from the bar's top end, so
            // the bar's top corners (7.2 mm from the axle) swing clear of the cross bar; the bar hangs turned 30° about the
            // axle (y).
            double pivotZ = h - 0.012, a = angle * Math.PI / 180;
            var down = new Vec3(-Math.Sin(a), 0, -Math.Cos(a));   // along the bar, away from the pivot (pitch 210° turns +z here)
            var topEnd = new Vec3(0, 0, pivotZ) - down * 0.006;
            var arm = w.AddBody("box", 0.012, 0.003, rod + 0.006, new WorldPose { X = topEnd.X, Z = topEnd.Z, Pitch = 180 + angle }, "ALÜMİNYUM");
            arm.Name = "Sarkaç kolu (alüminyum lama)";
            arm.MassKg = 0.0278;   // 12 × 3 × 286 mm of 2700 kg/m³
            var centre = new Vec3(0, 0, pivotZ) + down * (rod + bob / 2);
            var ball = w.AddBody("sphere", bob, bob, bob, new WorldPose { X = centre.X, Z = centre.Z - bob / 2 }, "ÇELİK");
            ball.Name = "Sarkaç topu (Ø40 çelik)";
            ball.MassKg = 0.263;   // 4/3·π·0.02³·7850
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = ball.Id, ObjectB = arm.Id });
            w.Joints.Add(new WorldJoint
            {
                Id = w.NextId("J"), Kind = WorldJointKind.Hinge, ObjectA = arm.Id, ObjectB = bearing.Id,
                AnchorX = 0, AnchorY = 0, AnchorZ = pivotZ, AxisX = 0, AxisY = 1, AxisZ = 0, Friction = 0.0004
            });
            return w;
        }

        /// <summary>
        /// A model traffic light with a pedestrian button: an ESP32 drives the cars' red, yellow and green and the
        /// pedestrians' red and green LEDs through 220 Ω resistors; the button (to ground, internal pull-up) asks for a
        /// crossing. The pedestrian presses it 3 s in; in the app it can be clicked any time.
        /// </summary>
        public static WorldDocument TrafficLight(PartLibrary lib)
        {
            var w = new WorldDocument
            {
                Title = "Trafik ışığı",
                Description = "ESP32 araç ve yaya ışıklarını yönetir; yaya butonu geçiş ister.",
                RoomLight = 0.25
            };
            var board = w.AddBody("box", 0.26, 0.16, 0.006, new WorldPose(), "AHŞAP");
            board.Name = "Maket tabanı";
            board.IsStatic = true;
            const double deck = 0.006;
            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = -0.09, Y = -0.02, Z = OnPins(deck) });
            esp.Name = "ESP32";
            esp.Firmware = new Formats.FirmwareSource { FileName = "trafik-isigi.ino", Target = "esp32:esp32:esp32", Source = TrafficLightSketch };
            var bat = w.AddComponent(lib, "bat-4aa", new WorldPose { X = -0.09, Y = 0.04, Z = deck, Yaw = 90 });
            bat.Name = "4×AA pil kutusu";

            WorldObject Light(string color, string name, double x, double y, string pin)
            {
                var res = w.AddComponent(lib, "res-220", new WorldPose { X = x - 0.02, Y = y, Z = OnPins(deck), Yaw = 90 });
                res.Name = "220 Ω (" + name + ")";
                var led = w.AddComponent(lib, "led5-" + color, new WorldPose { X = x, Y = y, Z = OnPins(deck) });
                led.Name = name;
                w.Connect(new CableEnd { ObjectId = esp.Id, Pin = pin }, new CableEnd { ObjectId = res.Id, Pin = "A" }, 26, "#F2A33B").Length = 0;
                w.Connect(new CableEnd { ObjectId = res.Id, Pin = "B" }, new CableEnd { ObjectId = led.Id, Pin = "A" }, 26, "#F2A33B").Length = 0;
                w.Connect(new CableEnd { ObjectId = led.Id, Pin = "K" }, new CableEnd { ObjectId = esp.Id, Pin = "GND2" }, 26, "#1F1F23").Length = 0;
                foreach (var o in new[] { res, led }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = board.Id });
                return led;
            }

            Light("red", "Araç kırmızı", 0.03, 0.05, "IO25");
            Light("yellow", "Araç sarı", 0.03, 0.02, "IO26");
            Light("green", "Araç yeşil", 0.03, -0.01, "IO27");
            Light("red", "Yaya kırmızı", 0.09, 0.05, "IO14");
            Light("green", "Yaya yeşil", 0.09, 0.02, "IO33");
            var button = w.AddComponent(lib, "tact-6mm", new WorldPose { X = 0.09, Y = -0.04, Z = OnPins(deck) });
            button.Name = "Yaya butonu";
            button.Parameters["close.at"] = "3";
            button.Parameters["open.at"] = "3.3";
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "IO32" }, new CableEnd { ObjectId = button.Id, Pin = "A" }, 26, "#3E7CC8").Length = 0;
            w.Connect(new CableEnd { ObjectId = button.Id, Pin = "B" }, new CableEnd { ObjectId = esp.Id, Pin = "GND1" }, 26, "#1F1F23").Length = 0;
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = esp.Id, Pin = "VIN" }, 24, "#E5484D").Length = 0;
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = esp.Id, Pin = "GND1" }, 24, "#1F1F23").Length = 0;
            foreach (var o in new[] { esp, bat, button }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = board.Id });
            return w;
        }

        /// <summary>Sketch of <see cref="TrafficLight"/>.</summary>
        public const string TrafficLightSketch = @"// Trafik ışığı ve yaya butonu (ESP32).
// Araçlar yeşilde bekler. Yaya butona basınca (en az 4 s yeşilden sonra): sarı 2 s, kırmızı;
// yayaya yeşil 5 s, yaya yeşili 2 s yanıp söner, yaya kırmızı; araçlara kırmızı+sarı 1 s, yeşil.
#include <Arduino.h>

const int ARAC_K = 25, ARAC_S = 26, ARAC_Y = 27, YAYA_K = 14, YAYA_Y = 33, BUTON = 32;

enum Faz { ARAC_YESIL, ARAC_SARI, YAYA_GEC, YAYA_BITIYOR, HAZIRLAN };
Faz faz = ARAC_YESIL;
unsigned long fazBasi = 0;
bool istek = false;

void isiklar(int ak, int as, int ay, int yk, int yy) {
  digitalWrite(ARAC_K, ak); digitalWrite(ARAC_S, as); digitalWrite(ARAC_Y, ay);
  digitalWrite(YAYA_K, yk); digitalWrite(YAYA_Y, yy);
}

void gec(Faz yeni, const char* ad) {
  faz = yeni;
  fazBasi = millis();
  Serial.println(ad);
}

void setup() {
  Serial.begin(115200);
  const int cikislar[] = { ARAC_K, ARAC_S, ARAC_Y, YAYA_K, YAYA_Y };
  for (int p : cikislar) pinMode(p, OUTPUT);
  pinMode(BUTON, INPUT_PULLUP);
  isiklar(LOW, LOW, HIGH, HIGH, LOW);
  gec(ARAC_YESIL, ""Araçlara yeşil"");
}

void loop() {
  unsigned long t = millis() - fazBasi;
  if (digitalRead(BUTON) == LOW && !istek) {
    istek = true;
    Serial.println(""Yaya butona bastı"");
  }

  switch (faz) {
    case ARAC_YESIL:
      isiklar(LOW, LOW, HIGH, HIGH, LOW);
      if (istek && t >= 4000) gec(ARAC_SARI, ""Araçlara sarı"");
      break;
    case ARAC_SARI:
      isiklar(LOW, HIGH, LOW, HIGH, LOW);
      if (t >= 2000) gec(YAYA_GEC, ""Araçlara kırmızı, yayaya yeşil"");
      break;
    case YAYA_GEC:
      isiklar(HIGH, LOW, LOW, LOW, HIGH);
      if (t >= 5000) gec(YAYA_BITIYOR, ""Yaya yeşili yanıp sönüyor"");
      break;
    case YAYA_BITIYOR:
      isiklar(HIGH, LOW, LOW, LOW, (t / 250) % 2 == 0 ? HIGH : LOW);
      if (t >= 2000) gec(HAZIRLAN, ""Araçlara kırmızı + sarı"");
      break;
    case HAZIRLAN:
      isiklar(HIGH, HIGH, LOW, HIGH, LOW);
      if (t >= 1000) { istek = false; gec(ARAC_YESIL, ""Araçlara yeşil""); }
      break;
  }
  delay(10);
}
";
    }
}
