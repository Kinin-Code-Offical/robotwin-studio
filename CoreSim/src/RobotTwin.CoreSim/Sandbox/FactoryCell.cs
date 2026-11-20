using System;
using System.Globalization;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [arm] "Fabrika hücresi": a fenced robot work cell. A belt conveyor with a box feeder brings
    /// cartons to an end stop; its photo-eye is wired to the robot's DI1. A 6-axis arm on a
    /// pedestal picks each carton with a vacuum cup and stacks them 3 × 2 × 2 on a pallet; when
    /// the pallet is full it is exchanged and the cycle continues. A light curtain guards the
    /// pallet opening and is wired to the robot's SAFE input.
    /// </summary>
    public static class FactoryCell
    {
        public const double PedestalHeight = 0.40;
        public const double ConveyorHeight = 0.60;
        public const double PalletX = 0.0, PalletY = -0.56;
        public static readonly (double X, double Y, double Z) Box = (0.16, 0.12, 0.10);

        public static WorldDocument Build()
        {
            var lib = PartLibrary.Default;
            var w = new WorldDocument
            {
                Title = "Fabrika hücresi",
                Description = "Konveyör kutuları getirir, 6 eksenli kol vakumla alıp palete 3×2×2 dizer; ışık perdesi ve çit ile korunan hücre.",
                Environment = new WorldSpec { Name = "Fabrika", Width = 5, Depth = 4, Walls = false, Friction = 0.7, FloorMaterial = "concrete" }
            };

            // The arm first: the page selects it and shows its teach pendant.
            var arm = w.AddComponent(lib, "arm-rw6-850", new WorldPose { Z = PedestalHeight });
            arm.Name = "Robot";
            arm.IsStatic = true;
            arm.Parameters["autostart"] = "true";
            arm.Parameters["q0"] = "0, -10, 25, 0, 75, 0";
            var ped = w.AddComponent(lib, "robot-pedestal-400", new WorldPose());
            ped.Name = "Kaide";
            ped.IsStatic = true;

            var cup = w.AddComponent(lib, "vacuum-cup-50", new WorldPose());
            cup.Name = "Vantuz";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = cup.Id, ObjectB = arm.Id, PortB = "flange" });
            CellGeometry.SeatTools(w, arm);

            // Conveyor runs toward −X and ends 0.45 m in front of the robot.
            var cv = w.AddComponent(lib, "conveyor-feed", new WorldPose { X = 1.25, Yaw = 180 });
            cv.Name = "Konveyör";
            cv.IsStatic = true;
            cv.Parameters["speed"] = "0.30";
            cv.Parameters["feed_period"] = "5";
            cv.Parameters["height"] = ConveyorHeight.ToString(CultureInfo.InvariantCulture);

            var pallet = w.AddComponent(lib, "pallet-800x600", new WorldPose { X = PalletX, Y = PalletY });
            pallet.Name = "Palet";
            pallet.IsStatic = true;

            var curtain = w.AddComponent(lib, "light-curtain-900", new WorldPose { X = 0, Y = -1.0, Yaw = 90 });
            curtain.Name = "Işık perdesi";
            curtain.IsStatic = true;

            // Fence: back, left and right (the right side leaves room for the conveyor).
            void Fence(string name, double x, double y, double width, double yaw)
            {
                var f = w.AddComponent(lib, "fence-1000", new WorldPose { X = x, Y = y, Yaw = yaw });
                f.Name = name;
                f.IsStatic = true;
                f.Parameters["width"] = width.ToString("0.###", CultureInfo.InvariantCulture);
            }

            Fence("Çit arka 1", -0.475, 0.85, 0.95, 0);
            Fence("Çit arka 2", 0.475, 0.85, 0.95, 0);
            Fence("Çit sol 1", -0.95, 0.375, 0.95, 90);
            Fence("Çit sol 2", -0.95, -0.55, 0.9, 90);
            Fence("Çit sağ 1", 0.95, 0.55, 0.6, 90);
            Fence("Çit sağ 2", 0.95, -0.6, 0.8, 90);

            // I/O wiring: photo-eye → DI1, light curtain OSSD → SAFE, common 0 V.
            var wire = "#F2A33B";
            w.Connect(new CableEnd { ObjectId = cv.Id, Pin = "SENSOR" }, new CableEnd { ObjectId = arm.Id, Pin = "DI1" }, 24, wire);
            w.Connect(new CableEnd { ObjectId = cv.Id, Pin = "GND" }, new CableEnd { ObjectId = arm.Id, Pin = "GND" }, 24, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = curtain.Id, Pin = "OSSD" }, new CableEnd { ObjectId = arm.Id, Pin = "SAFE" }, 24, "#E5C04B");
            w.Connect(new CableEnd { ObjectId = curtain.Id, Pin = "GND" }, new CableEnd { ObjectId = arm.Id, Pin = "GND" }, 24, "#1F1F23");

            // Program (points in the robot base frame: mm and degrees; tool Z pointing down = rx 180).
            double pickX = 1.25 - (1.6 / 2 - 0.02 - Box.X / 2);     // box centre at the end stop
            double pickZ = ConveyorHeight + Box.Z - 0.006 - PedestalHeight;
            double placeX = PalletX - 0.18, placeY = PalletY - 0.07;
            double placeZ = 0.144 + Box.Z - 0.003 - PedestalHeight;
            arm.Parameters["points"] = string.Join("\n",
                "BEKLEME = J(0, -10, 25, 0, 75, 0)",
                Fmt("ALMA", pickX, 0, pickZ),
                Fmt("BIRAKMA", placeX, placeY, placeZ));
            arm.Parameters["program"] = string.Join("\n",
                "; Fabrika hücresi: konveyörden vakumla al, palete 3 x 2 x 2 diz",
                "PALET 3 2 2 180 140 100",
                "MOVEJ BEKLEME HIZ=60",
                "GIRISBEKLE 1 1          ; konveyör sonu fotoseli",
                "MOVEL ALMA OFS=0,0,120 HIZ=1200",
                "MOVEL ALMA HIZ=250",
                "KAVRA",
                "TUTUSBEKLE 1.0",
                "MOVEL ALMA OFS=0,0,150 HIZ=900",
                "MOVEJ BIRAKMA PAL OFS=0,0,300 HIZ=90",
                "MOVEL BIRAKMA PAL OFS=0,0,40 HIZ=900",
                "MOVEL BIRAKMA PAL HIZ=150",
                "BIRAK",
                "BEKLE 0.25",
                "MOVEL BIRAKMA PAL OFS=0,0,150 HIZ=900",
                "PALETSONRAKI");
            return w;
        }

        /// <summary>
        /// "SCARA al-bırak": a SCARA on a riser beside a work table moves four aluminium blocks with a
        /// parallel gripper from one row to the other and back, forever (friction grasp, 150 mm Z stroke).
        /// </summary>
        public static WorldDocument Scara()
        {
            var lib = PartLibrary.Default;
            var w = new WorldDocument
            {
                Title = "SCARA al-bırak",
                Description = "SCARA paralel tutucuyla dört alüminyum bloğu bir sıradan diğerine taşır ve geri getirir.",
                Environment = new WorldSpec { Name = "Atölye", Width = 3, Depth = 2.5, Walls = false }
            };
            const double tableTop = 0.75, riser = 0.10;
            var robot = w.AddComponent(lib, "arm-rws-500", new WorldPose { X = -0.25, Z = tableTop + riser });
            robot.Name = "SCARA";
            robot.IsStatic = true;
            robot.Parameters["autostart"] = "true";
            robot.Parameters["q0"] = "30, -60, 20, 0";
            var table = w.AddBody("box", 1.0, 0.8, tableTop, new WorldPose { X = 0.0 }, "AHŞAP");
            table.Name = "Masa";
            table.IsStatic = true;
            var stand = w.AddComponent(lib, "robot-pedestal-400", new WorldPose { X = -0.25, Z = tableTop });
            stand.Name = "Yükseltici";
            stand.IsStatic = true;
            stand.Parameters["height"] = riser.ToString(CultureInfo.InvariantCulture);
            stand.Parameters["size"] = "0.3";
            var gripper = w.AddComponent(lib, "gripper-2f-60", new WorldPose());
            gripper.Name = "Tutucu";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = gripper.Id, ObjectB = robot.Id, PortB = "flange" });
            CellGeometry.SeatTools(w, robot);

            // Rows along X (fingers close along Y), robot base frame.
            double[] xs = { 0.22, 0.28, 0.34, 0.40 };
            const double yA = -0.15, yB = 0.20, block = 0.05;
            for (int k = 0; k < xs.Length; k++)
            {
                var b = w.AddBody("box", 0.04, 0.03, block, new WorldPose { X = -0.25 + xs[k], Y = yA, Z = tableTop }, "ALÜMİNYUM");
                b.Name = "Blok " + (k + 1).ToString(CultureInfo.InvariantCulture);
                b.MassKg = 0.16;
                b.Color = k % 2 == 0 ? "#3E7CC8" : "#E5484D";
            }

            double grip = block - 0.025 - riser, above = block - riser + 0.05;
            var points = new System.Text.StringBuilder();
            var program = new System.Text.StringBuilder("; SCARA: blokları A sırasından B sırasına, sonra geri taşı\nHIZ 100\n");
            for (int k = 0; k < xs.Length; k++)
            {
                points.AppendLine(string.Format(CultureInfo.InvariantCulture, "A{0} = C({1:0.#}, {2:0.#}, {3:0.#}, 0)", k + 1, xs[k] * 1000, yA * 1000, grip * 1000));
                points.AppendLine(string.Format(CultureInfo.InvariantCulture, "B{0} = C({1:0.#}, {2:0.#}, {3:0.#}, 0)", k + 1, xs[k] * 1000, yB * 1000, grip * 1000));
            }

            string up = string.Format(CultureInfo.InvariantCulture, "OFS=0,0,{0:0}", (above - grip) * 1000);
            foreach (var (from, to) in new[] { ("A", "B"), ("B", "A") })
            {
                for (int k = 1; k <= xs.Length; k++)
                {
                    program.Append($"MOVEJ {from}{k} {up} HIZ=100\nMOVEL {from}{k} HIZ=600\nKAVRA\nTUTUSBEKLE 1.0\nMOVEL {from}{k} {up} HIZ=1500\n");
                    program.Append($"MOVEJ {to}{k} {up} HIZ=100\nMOVEL {to}{k} HIZ=600\nBIRAK\nBEKLE 0.3\nMOVEL {to}{k} {up} HIZ=1500\n");
                }
            }

            robot.Parameters["points"] = points.ToString().TrimEnd().Replace("\r", string.Empty);
            robot.Parameters["program"] = program.ToString().TrimEnd();
            return w;
        }

        private static string Fmt(string name, double x, double y, double z) =>
            string.Format(CultureInfo.InvariantCulture, "{0} = C({1:0.#}, {2:0.#}, {3:0.#}, 180, 0, 0)", name, x * 1000, y * 1000, z * 1000);
    }
}
