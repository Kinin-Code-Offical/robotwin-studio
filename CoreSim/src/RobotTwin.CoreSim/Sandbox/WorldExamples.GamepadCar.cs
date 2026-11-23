using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [radio] Controller board of the gamepad car as a real design: ESP32 DevKit V1 and a DRV8833
        /// module plugged onto a carrier PCB with screw terminals for the battery and the two motors, a
        /// power switch and a 470 µF bulk capacitor on the motor supply. The ESP32 runs on the battery
        /// through VIN (not over USB), with the Bluepad32 sketch esp32-gamepad-car.ino. Schematic, PCB
        /// (auto-placed and auto-routed) and firmware open from the world with "Kartı düzenle".
        /// </summary>
        public static ProjectPackage GamepadCarBoard(PartLibrary lib)
        {
            var s = new Schematic { Title = "Kumandalı araba kartı" };
            var esp = s.AddPart(lib, Esp32Parts.DevKitV1Id, new GridPoint(0, 0));
            esp.Parameters["firmware"] = "builtin:esp32-gamepad-car.ino";
            esp.Parameters["usb"] = "false";   // powered from the car's battery, not a PC
            var drv = s.AddPart(lib, "drv8833", new GridPoint(36, 0));
            var bat = s.AddPart(lib, "term-2", new GridPoint(-34, 14));
            var sw = s.AddPart(lib, "toggle-spst", new GridPoint(-22, 14));
            sw.Parameters["closed"] = "true";
            var cap = s.AddPart(lib, "ecap-470u", new GridPoint(-12, 22));
            var left = s.AddPart(lib, "term-2", new GridPoint(56, -6));
            var right = s.AddPart(lib, "term-2", new GridPoint(56, 6));
            void T(SchematicPart part, string pin, string net) => VehicleTemplates.Tie(s, lib, part, pin, net);
            T(bat, "1", "VBAT");
            T(bat, "2", "GND");
            T(sw, "A", "VBAT");
            T(sw, "B", "VM");
            T(cap, "+", "VM");
            T(cap, "-", "GND");
            T(esp, "VIN", "VM");
            T(esp, "GND1", "GND");
            T(drv, "VM", "VM");
            T(drv, "GND", "GND");
            foreach (var (io, input) in new[] { ("IO25", "AIN1"), ("IO26", "AIN2"), ("IO27", "BIN1"), ("IO14", "BIN2") })
            {
                T(esp, io, input);
                T(drv, input, input);
            }

            T(drv, "AOUT1", "MOT_L1");
            T(drv, "AOUT2", "MOT_L2");
            T(drv, "BOUT1", "MOT_R1");
            T(drv, "BOUT2", "MOT_R2");
            T(left, "1", "MOT_L1");
            T(left, "2", "MOT_L2");
            T(right, "1", "MOT_R1");
            T(right, "2", "MOT_R2");

            var board = BoardDocument.Synchronize(s, lib);
            board.Width = 80;
            board.Height = 50;
            AutoPlacer.Place(board);
            new AutoRouter(board).Route();
            s.Board = board;
            return new ProjectPackage { Schematic = s };
        }

        /// <summary>
        /// [radio] Two-wheel car driven by a DualSense over Bluetooth: TT gear motors under an acrylic
        /// plate (shafts at wheel-radius height so the 65 mm wheels stand on the floor), a ball caster in
        /// front, a USB-C rechargeable 2S Li-ion pack and the controller board on top. The left motor is mounted mirrored,
        /// so its leads go to the terminal the other way round and both wheels drive forward together.
        /// The shaft position (<paramref name="shaftX"/>, <paramref name="shaftZ"/>, <paramref name="shaftTip"/>)
        /// and gearbox height come from the motor model in use, so each wheel sits exactly on its axle.
        /// </summary>
        public static WorldDocument GamepadCar(PartLibrary lib, double motorHeight = 0.0224, double shaftX = -0.0227, double shaftZ = 0.0112, double shaftTip = 0.0184)
        {
            var w = new WorldDocument { Title = "Kumandalı araba" };
            const double r = 0.0325, motorY = 0.04, axleX = -0.04;
            double motorZ = r - shaftZ, plateZ = motorZ + motorHeight, deck = plateZ + 0.003;
            var plate = w.AddBody("plate", 0.20, 0.10, 0.003, new WorldPose { Z = plateZ }, "AKRİLİK");   // 100 mm wide: the wheels run 10 mm clear of its edges
            plate.Name = "Şase";
            plate.Color = "#3E7CC8";

            // Left motor: shaft towards +Y. The right one is the same motor flipped upside down (rolled 180°
            // about its length), as in a real 2WD kit: its shaft points to −Y on the same axle and the two
            // motors are mirror images, so the car's mass is symmetric.
            var left = w.AddComponent(lib, "motor-tt", new WorldPose { X = axleX - shaftX, Y = motorY, Z = motorZ });
            left.Name = "Sol motor";
            var right = w.AddComponent(lib, "motor-tt", new WorldPose { X = axleX - shaftX, Y = -motorY, Z = motorZ + motorHeight, Roll = 180 });
            right.Name = "Sağ motor";
            var wheelL = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = axleX, Y = motorY + shaftTip + 0.015, Z = 0 }, "KAUÇUK");
            wheelL.Name = "Sol teker";
            var wheelR = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = axleX, Y = -motorY - shaftTip - 0.015, Z = motorHeight - 2 * shaftZ }, "KAUÇUK");
            wheelR.Name = "Sağ teker";

            const double ball = 0.016, casterX = 0.08;
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = casterX }, "ÇELİK");
            caster.Name = "Sarhoş teker";
            var post = w.AddBody("box", 0.012, 0.012, plateZ - ball * 0.8, new WorldPose { X = casterX, Z = ball * 0.8 }, "PLA");
            post.Name = "Teker ayağı";

            // 2×18650 pack with its own BMS and USB-C charging port: plug a USB-C cable in to charge it.
            var bat = w.AddComponent(lib, "bat-2s-usbc", new WorldPose { X = -0.055, Z = deck });
            bat.Name = "2S Li-ion paket (USB-C)";
            var design = w.AddDesign(GamepadCarBoard(lib));
            // 12 mm ahead of the pack's leads: their plug lies on the deck in front of the board, not among its parts.
            var board = w.AddBoard(design, new WorldPose { X = 0.057, Z = deck + 0.004 });
            board.Name = "Araba kartı";

            foreach (var o in new[] { right, left, post, caster, bat, board })
                w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = o.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = wheelL.Id, ObjectB = left.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = wheelR.Id, ObjectB = right.Id, PortB = "shaft" });

            // Board terminals in the order they were added: battery, left motor, right motor.
            var terminals = design.Package.Schematic.Parts.Where(p => p.PartId == "terminal-2").Select(p => p.RefDes).ToList();
            CableEnd B(int terminal, string pin) => new CableEnd { ObjectId = board.Id, Pin = terminals[terminal] + "." + pin };
            CableEnd E(WorldObject o, string pin) => new CableEnd { ObjectId = o.Id, Pin = pin };
            w.Connect(E(bat, "+"), B(0, "1"), 22, "#E5484D");
            w.Connect(E(bat, "-"), B(0, "2"), 22, "#1F1F23");
            w.Connect(B(1, "1"), E(left, "A"), 22, "#E5484D");
            w.Connect(B(1, "2"), E(left, "B"), 22, "#1F1F23");
            w.Connect(B(2, "1"), E(right, "B"), 22, "#E5484D");   // mirrored motor: leads swapped
            w.Connect(B(2, "2"), E(right, "A"), 22, "#1F1F23");

            // The controller lies on the table in front of the car, already bonded to the car's ESP32.
            var pad = w.AddComponent(lib, "dualsense", new WorldPose { X = 0.05, Y = -0.32, Yaw = 90 });
            pad.Name = "DualSense";
            pad.Parameters["pairedWith"] = board.Id;
            pad.Parameters["source"] = "otomatik";
            return w;
        }
    }
}
