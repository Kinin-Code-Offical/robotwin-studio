using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>Height of the glass's turning axis above the base (m).</summary>
        public const double PourAxisZ = 0.12;

        /// <summary>
        /// [liquid] A glass of water turned over by a servo, pouring into a bowl. An SG90 lies on its side on a stand with
        /// its horn axis horizontal; an arm on the horn holds a 70 mm glass whose axis crosses the horn axis at mid-height,
        /// so the servo only has to carry the water's shifting weight. An ESP32 tips the glass 90° slowly, holds it while it
        /// empties into the bowl and stands it up again.
        /// </summary>
        public static WorldDocument GlassPour(PartLibrary lib)
        {
            var w = new WorldDocument
            {
                Title = "Su bardağı",
                Description = "Servo dolu bardağı yavaşça devirir; su kâseye dökülür (parçacıklı sıvı, gerçek yoğunluk, viskozite ve yüzey gerilimi)."
            };
            var deck = w.AddBody("box", 0.44, 0.26, 0.012, new WorldPose { X = 0.0 }, "AHŞAP");
            deck.Name = "Taban";
            deck.IsStatic = true;
            const double top = 0.012;
            double zs = top + PourAxisZ;
            // SG90 on its side (roll −90°: its top, with the horn, faces +Y); it lies on its +Y face, 5.9 mm below its origin.
            var stand = w.AddBody("box", 0.03, 0.026, zs - 0.0059 - top, new WorldPose { X = -0.002, Y = -0.062, Z = top }, "AHŞAP");
            stand.Name = "Servo ayağı";
            stand.IsStatic = true;
            var servo = w.AddComponent(lib, "servo-sg90", new WorldPose { X = 0, Y = -0.062, Z = zs, Roll = -90 });
            servo.Name = "Servo (SG90)";
            // Screwed to the stand, which stands on the base: fixed in the world. (A 9 g servo body held only by a joint
            // between a fixed stand and the 0.4 kg glass of water would be the weak link of the solver, not of reality.)
            servo.IsStatic = true;
            // Horn axis: 5.4 mm from the body centre along x, 31 mm out of its top (now along +Y).
            double ax = servo.Pose.X - 0.0054, hornY = servo.Pose.Y + 0.031;
            var arm = w.AddBody("box", 0.03, 0.003, 0.05, new WorldPose { X = ax, Y = hornY + 0.0035, Z = zs - 0.025 }, "PLA");
            arm.Name = "Bardak kolu";
            arm.Color = "#E87A2C";
            // The glass: Ø70 mm, 100 mm tall, 2.5 mm wall, 6 mm base; its axis crosses the horn axis at half its height.
            const double ro = 0.035, height = 0.10, wall = 0.0025, bottom = 0.006;
            double gy = hornY + 0.005 + ro;
            var glass = w.AddBody("cylinder", 2 * ro, 2 * ro, height, new WorldPose { X = ax, Y = gy, Z = zs - height / 2 }, "CAM");
            glass.Name = "Bardak";
            glass.MassKg = 0.21;
            glass.Parameters["tube.wall"] = "0.0025";
            glass.Parameters["tube.bottom"] = "0.006";
            // 193 ml of water: 64 mm across (half a millimetre off the wall), 60 mm deep.
            double ri = ro - wall - 0.0005;
            var water = w.AddBody("cylinder", 2 * ri, 2 * ri, 0.06, new WorldPose { X = ax, Y = gy, Z = glass.Pose.Z + bottom + 0.0005 }, "PLA");
            water.Name = "Su";
            water.Parameters["liquid"] = "water";
            water.MassKg = 998.2 * System.Math.PI * ri * ri * 0.06;
            // The bowl it pours into, on the side the glass tips to (servo angle 90 → 0 turns the glass's top towards −x).
            var bowl = w.AddBody("cylinder", 0.15, 0.15, 0.04, new WorldPose { X = ax - 0.092, Y = gy, Z = top }, "PLA");
            bowl.Name = "Kâse";
            bowl.Color = "#EEEDE8";
            bowl.MassKg = 0.18;
            bowl.Parameters["tube.wall"] = "0.003";
            bowl.Parameters["tube.bottom"] = "0.004";

            var esp = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.13, Y = 0.06, Z = OnPins(top) });
            esp.Name = "ESP32";
            esp.Firmware = new Formats.FirmwareSource { FileName = "su-bardagi.ino", Target = "esp32:esp32:esp32", Source = GlassPourSketch };
            var bat = w.AddComponent(lib, "bat-4aa", new WorldPose { X = 0.13, Y = -0.05, Z = top, Yaw = 90 });
            bat.Name = "4×AA pil kutusu";
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = esp.Id, Pin = "VIN" }, 24, "#E5484D").Length = 0;
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = esp.Id, Pin = "GND1" }, 24, "#1F1F23").Length = 0;
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = servo.Id, Pin = "V+" }, 24, "#E5484D").Length = 0;
            w.Connect(new CableEnd { ObjectId = servo.Id, Pin = "GND" }, new CableEnd { ObjectId = esp.Id, Pin = "GND2" }, 24, "#5A3A22").Length = 0;
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "IO25" }, new CableEnd { ObjectId = servo.Id, Pin = "SIG" }, 24, "#F2A33B").Length = 0;
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = servo.Id, ObjectB = stand.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = arm.Id, ObjectB = servo.Id, PortB = "horn" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = glass.Id, ObjectB = arm.Id });
            foreach (var o in new[] { esp, bat }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = deck.Id });
            return w;
        }

        /// <summary>Sketch of <see cref="GlassPour"/>.</summary>
        public const string GlassPourSketch = @"// Su bardağı (ESP32 + SG90).
// Bardak 1 s dik bekler, 3 s'de yavaşça 90° devrilir, su boşalana kadar 4 s tutulur, 2 s'de dikilir.
#include <Arduino.h>
#include <ESP32Servo.h>

const int SERVO = 25;
const int DIK = 90, DEVRIK = 0;   // servo açısı: 90 = bardak dik, 0 = yan yatmış
Servo servo;

void yavasca(int from, int to, unsigned long ms) {
  int adim = from < to ? 1 : -1;
  int n = abs(to - from);
  for (int a = from; a != to; a += adim) {
    servo.write(a);
    delay(ms / n);
  }
  servo.write(to);
}

void setup() {
  Serial.begin(115200);
  servo.attach(SERVO);
  servo.write(DIK);
  delay(1000);
  Serial.println(""Devriliyor"");
  yavasca(DIK, DEVRIK, 3000);
  Serial.println(""Dokuluyor"");
  delay(4000);
  Serial.println(""Dikiliyor"");
  yavasca(DEVRIK, DIK, 2000);
  Serial.println(""Bitti"");
}

void loop() {
}
";
    }
}
