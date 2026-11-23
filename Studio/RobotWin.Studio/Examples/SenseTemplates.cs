using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;

namespace RobotWin.Studio.Examples
{
    /// <summary>[sense] Worlds that show the sensors: a line follower on a mixed track, a sensor rover in a room.</summary>
    public static class SenseTemplates
    {
        private static PartLibrary Lib => PartLibrary.Default;

        /// <summary>Oval track: matte black, then a faded and a glossy section on the far straight (polylines, metres).</summary>
        public static List<FloorTape> MixedOval(double halfStraight = 0.6, double radius = 0.45, double width = 0.019)
        {
            List<double[]> Arc(double cx, double cy, double from, double to)
            {
                var pts = new List<double[]>();
                int n = 24;
                for (int i = 0; i <= n; i++)
                {
                    double a = from + (to - from) * i / n;
                    pts.Add(new[] { cx + radius * Math.Cos(a), cy + radius * Math.Sin(a) });
                }

                return pts;
            }

            return new List<FloorTape>
            {
                new FloorTape { Name = "Alt düzlük (mat)", Finish = TapeFinish.Matte, Width = width, Points = { new[] { -halfStraight, -radius }, new[] { halfStraight, -radius } } },
                new FloorTape { Name = "Sağ viraj (mat)", Finish = TapeFinish.Matte, Width = width, Points = Arc(halfStraight, 0, -Math.PI / 2, Math.PI / 2) },
                new FloorTape { Name = "Üst düzlük (solmuş)", Finish = TapeFinish.Faded, Wear = 0.6, Seed = 3, Width = width, Points = { new[] { halfStraight, radius }, new[] { 0.0, radius } } },
                new FloorTape { Name = "Üst düzlük (parlak)", Finish = TapeFinish.Glossy, Width = width, Points = { new[] { 0.0, radius }, new[] { -halfStraight, radius } } },
                new FloorTape { Name = "Sol viraj (mat)", Finish = TapeFinish.Matte, Width = width, Points = Arc(-halfStraight, 0, Math.PI / 2, 3 * Math.PI / 2) }
            };
        }

        /// <summary>
        /// Arduino Uno line follower: 2S LiPo, DRV8833, two TT motors with wheels and a ball caster, a
        /// 5-channel TCRT5000 module 6 mm above the floor, on a white mat with a mixed track by a sunny window.
        /// </summary>
        public static WorldDocument LineFollower()
        {
            var w = new WorldDocument { Title = "Çizgi izleyen robot", Description = "Mat, solmuş ve parlak bant bölümleri; pencere ışığı (IR) açık." };
            w.Environment = new WorldSpec { Name = "Çizgi pisti", Width = 2.2, Depth = 1.5, Walls = false, Friction = 0.9, Track = "none" };
            w.Environment.Sensing = new SensingEnvironment
            {
                AmbientLux = 650, AmbientIr = 0.3, LightElevationDeg = 72, LightAzimuthDeg = 135, FloorReflectance = 0.85, FloorGloss = 0.03,
                Tapes = MixedOval()
            };

            var (pivot, _, motorHeight) = World.ObjectModels.Mount(Lib, "motor-tt");
            double shaftX = pivot.X, shaftZ = pivot.Z, shaftTip = pivot.Y > 0.005 ? pivot.Y : 0.0185;
            const double r = 0.0325;
            double motorZ = r - shaftZ, plateZ = motorZ + motorHeight;
            double x0 = -0.385, y0 = -0.45;   // sensor bar starts on the bottom straight, heading +X

            var plate = w.AddBody("plate", 0.22, 0.09, 0.003, new WorldPose { X = x0, Y = y0, Z = plateZ }, "AKRİLİK");   // 90 mm: the wheels run clear of the edges
            plate.Name = "Şase";
            plate.Color = "#3E7CC8";
            var ml = w.AddComponent(Lib, "motor-tt", new WorldPose { X = x0 - 0.04, Y = y0 + 0.03, Z = motorZ });
            ml.Name = "Sol motor";
            var mr = w.AddComponent(Lib, "motor-tt", new WorldPose { X = x0 - 0.04, Y = y0 - 0.03, Z = motorZ, Yaw = 180 });
            mr.Name = "Sağ motor";
            var wl = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = ml.Pose.X + shaftX, Y = ml.Pose.Y + shaftTip + 0.015, Z = 0 });
            var wr = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = mr.Pose.X - shaftX, Y = mr.Pose.Y - shaftTip - 0.015, Z = 0 });
            const double ball = 0.016;
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = x0 + 0.055, Y = y0, Z = 0 }, "ÇELİK");
            caster.Name = "Sarhoş teker";
            var post = w.AddBody("box", 0.012, 0.012, plateZ - ball * 0.8, new WorldPose { X = x0 + 0.055, Y = y0, Z = ball * 0.8 }, "PLA");
            post.Name = "Teker ayağı";

            var uno = w.AddComponent(Lib, "arduino-uno", new WorldPose { X = x0 - 0.0646, Y = y0 + 0.012, Z = WorldExamples.OnPins(plateZ + 0.003) });   // on its standoffs, at the back
            uno.Name = "Arduino Uno";
            uno.Parameters["firmware"] = "builtin:line-follower.hex";
            uno.Parameters["usb"] = "false";
            var drv = w.AddComponent(Lib, "drv8833", new WorldPose { X = x0 + 0.045, Y = y0 + 0.035, Z = plateZ + 0.003 });
            // Ahead of the Uno (not on top of it), between the wheels' tracks: the pack's weight rides on the caster.
            var bat = w.AddComponent(Lib, "bat-2s-lipo", new WorldPose { X = x0 + 0.0575, Y = y0 - 0.012, Z = plateZ + 0.003 });
            bat.Name = "LiPo 2S";
            var bar = w.AddComponent(Lib, "line-5ch-module", new WorldPose { X = x0 + 0.085, Y = y0, Z = 0.006 });
            bar.Name = "Çizgi sensörü (6 mm)";

            foreach (var o in new[] { ml, mr, post, caster, uno, drv, bat, bar }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wl.Id, ObjectB = ml.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wr.Id, ObjectB = mr.Id, PortB = "shaft" });

            // Power: LiPo → Uno VIN and DRV8833 VM, common ground.
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = uno.Id, Pin = "VIN" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = uno.Id, Pin = "GND1" }, 22, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = drv.Id, Pin = "VM" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = drv.Id, Pin = "GND" }, 22, "#1F1F23");
            // Motors (the right one is mirrored, so its leads are swapped to drive forward).
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D5" }, new CableEnd { ObjectId = drv.Id, Pin = "AIN1" }, 26, "#F2A33B");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D9" }, new CableEnd { ObjectId = drv.Id, Pin = "AIN2" }, 26, "#F2D04B");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D6" }, new CableEnd { ObjectId = drv.Id, Pin = "BIN1" }, 26, "#3CD064");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "D10" }, new CableEnd { ObjectId = drv.Id, Pin = "BIN2" }, 26, "#3E7CC8");
            w.Connect(new CableEnd { ObjectId = drv.Id, Pin = "AOUT1" }, new CableEnd { ObjectId = ml.Id, Pin = "A" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = drv.Id, Pin = "AOUT2" }, new CableEnd { ObjectId = ml.Id, Pin = "B" }, 22, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = drv.Id, Pin = "BOUT1" }, new CableEnd { ObjectId = mr.Id, Pin = "B" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = drv.Id, Pin = "BOUT2" }, new CableEnd { ObjectId = mr.Id, Pin = "A" }, 22, "#1F1F23");
            // Sensor: 5 V from the Uno, OUT1 (left) … OUT5 (right) → A0 … A4.
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = bar.Id, Pin = "VCC" }, 26, "#E5484D");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND2" }, new CableEnd { ObjectId = bar.Id, Pin = "GND" }, 26, "#1F1F23");
            for (int i = 1; i <= 5; i++)
            {
                w.Connect(new CableEnd { ObjectId = bar.Id, Pin = "OUT" + i }, new CableEnd { ObjectId = uno.Id, Pin = "A" + (i - 1) }, 26, "#F2A33B");
            }

            return w;
        }

        /// <summary>ESP32 reading a downward-looking OV2640 over the mixed track (esp_camera.h, grayscale QQVGA).</summary>
        public static WorldDocument CameraLine()
        {
            var w = new WorldDocument { Title = "ESP32 kamera ile bant", Description = "OV2640 bandı yukarıdan görür; ESP32 çizimi kareleri esp_camera.h ile okur." };
            w.Environment = new WorldSpec { Name = "Çizgi pisti", Width = 2.2, Depth = 1.5, Walls = false, Track = "none" };
            w.Environment.Sensing = new SensingEnvironment { AmbientLux = 400, AmbientIr = 0, FloorReflectance = 0.85, Tapes = MixedOval() };
            var stand = w.AddBody("box", 0.04, 0.04, 0.22, new WorldPose { X = 0.2, Y = -0.75 }, "AHŞAP");
            stand.Name = "Kamera direği";
            stand.IsStatic = true;
            var cam = w.AddComponent(Lib, "cam-ov2640", new WorldPose { X = 0.2, Y = -0.73, Z = 0.22, Yaw = 90, Pitch = 50 });
            cam.Name = "OV2640 (aşağı bakıyor)";
            cam.IsStatic = true;
            cam.Parameters["framesize"] = "QQVGA";
            cam.Parameters["format"] = "GRAYSCALE";
            var esp = w.AddComponent(Lib, "esp32-devkit-v1", new WorldPose { X = 0.32, Y = -0.75 });
            esp.Name = "ESP32";
            esp.Parameters["firmware"] = "builtin:esp32-camera-line.ino";
            cam.Parameters["host"] = esp.Id;
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "3V3" }, new CableEnd { ObjectId = cam.Id, Pin = "VCC" }, 26, "#E5484D");
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "GND1" }, new CableEnd { ObjectId = cam.Id, Pin = "GND" }, 26, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "IO26" }, new CableEnd { ObjectId = cam.Id, Pin = "SDA" }, 26, "#3E7CC8");
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "IO27" }, new CableEnd { ObjectId = cam.Id, Pin = "SCL" }, 26, "#F2D04B");
            return w;
        }

        /// <summary>
        /// Sensor rover in a walled room: OV2640 camera, A1-class LiDAR, GPS, MPU-6050, HMC5883L and two
        /// encoder motors; one motor runs through a 10 Ω resistor so the rover drives in a circle.
        /// </summary>
        public static WorldDocument SensorRover()
        {
            var w = new WorldDocument { Title = "Sensör gezgini", Description = "Kamera, LiDAR, GPS, IMU, pusula ve enkoderler aynı anda." };
            w.Environment = new WorldSpec { Name = "Oda", Width = 3.0, Depth = 2.4, Walls = true, Friction = 0.9, Track = "none" };
            w.Environment.Sensing = new SensingEnvironment { AmbientLux = 300, FloorReflectance = 0.45, FloorGloss = 0.04, Sky = "open" };
            w.Environment.Obstacles.Add(new WorldObstacle { X = 0.9, Y = 0.55, SizeX = 0.3, SizeY = 0.3, Height = 0.25 });
            w.Environment.Obstacles.Add(new WorldObstacle { X = -0.8, Y = -0.6, SizeX = 0.5, SizeY = 0.2, Height = 0.18 });
            w.Environment.Obstacles.Add(new WorldObstacle { X = -1.0, Y = 0.7, SizeX = 0.2, SizeY = 0.4, Height = 0.3 });

            // JGA25 encoder motors: Ø25 mm, shaft 12.5 mm above the motor base → 65 mm wheels touch the floor.
            const double r = 0.0325, shaftZ = 0.0125, motorH = 0.025;
            double motorZ = r - shaftZ, plateZ = motorZ + motorH;
            var plate = w.AddBody("plate", 0.24, 0.16, 0.004, new WorldPose { X = 0, Y = -0.35, Z = plateZ }, "ALÜMİNYUM");
            plate.Name = "Gezgin şasesi";
            var ml = w.AddComponent(Lib, "motor-jga25-encoder", new WorldPose { X = -0.05, Y = -0.35 + 0.07, Z = motorZ });
            ml.Name = "Sol enkoderli motor";
            var mr = w.AddComponent(Lib, "motor-jga25-encoder", new WorldPose { X = -0.05, Y = -0.35 - 0.07, Z = motorZ, Yaw = 180 });
            mr.Name = "Sağ enkoderli motor";
            var wl = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.05, Y = ml.Pose.Y + 0.0095 + 0.013, Z = 0 });
            var wr = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = -0.05, Y = mr.Pose.Y - 0.0095 - 0.013, Z = 0 });
            const double ball = 0.02;
            var caster = w.AddBody("sphere", ball, ball, ball, new WorldPose { X = 0.09, Y = -0.35, Z = 0 }, "ÇELİK");
            var post = w.AddBody("box", 0.014, 0.014, plateZ - ball * 0.8, new WorldPose { X = 0.09, Y = -0.35, Z = ball * 0.8 }, "PLA");

            double top = plateZ + 0.004;
            var uno = w.AddComponent(Lib, "arduino-uno", new WorldPose { X = -0.07, Y = -0.35, Z = WorldExamples.OnPins(top) });
            uno.Name = "Güç dağıtımı (Uno, USB)";
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = 0.03, Y = -0.35 - 0.05, Z = top });
            var res = w.AddComponent(Lib, "res-10R-2W", new WorldPose { X = 0.03, Y = -0.35 + 0.06, Z = WorldExamples.OnPins(top) });   // standing on its leads
            // A 20 mm post in the free strip between the Uno, the battery and the resistor lifts the scanner above them all.
            var lidar = w.AddComponent(Lib, "lidar-a1", new WorldPose { X = 0.0, Y = -0.32, Z = top + 0.03 });
            lidar.Name = "2D LiDAR";
            var lidarMast = w.AddBody("box", 0.02, 0.02, 0.03, new WorldPose { X = 0.0, Y = -0.32, Z = top }, "PLA");
            lidarMast.Name = "LiDAR ayağı";
            var cam = w.AddComponent(Lib, "cam-ov2640", new WorldPose { X = 0.11, Y = -0.35, Z = top });
            cam.Name = "Ön kamera";
            cam.Parameters["framesize"] = "QVGA";
            cam.Parameters["format"] = "RGB565";
            var gps = w.AddComponent(Lib, "gps-neo6m", new WorldPose { X = -0.1, Y = -0.35 + 0.06, Z = top });
            var imu = w.AddComponent(Lib, "gy-521", new WorldPose { X = -0.1, Y = -0.35 - 0.06, Z = WorldExamples.OnPins(top) });
            imu.Name = "IMU";
            var mag = w.AddComponent(Lib, "gy-271", new WorldPose { X = 0.06, Y = -0.35 + 0.06, Z = top });
            mag.Name = "Pusula";

            foreach (var o in new[] { ml, mr, caster, post, uno, bat, res, lidarMast, cam, gps, imu, mag }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = plate.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = lidar.Id, ObjectB = lidarMast.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wl.Id, ObjectB = ml.Id, PortB = "shaft" });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = wr.Id, ObjectB = mr.Id, PortB = "shaft" });

            // Motors from the battery; the right one through 10 Ω (slower → the rover circles).
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = ml.Id, Pin = "M1" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = ml.Id, Pin = "M2" }, 22, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = res.Id, Pin = "A" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = res.Id, Pin = "B" }, new CableEnd { ObjectId = mr.Id, Pin = "M2" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = mr.Id, Pin = "M1" }, 22, "#1F1F23");
            // Sensor rails from the Uno (USB powered).
            void Power(WorldObject o, string vcc, string rail)
            {
                w.Connect(new CableEnd { ObjectId = uno.Id, Pin = rail }, new CableEnd { ObjectId = o.Id, Pin = vcc }, 26, "#E5484D");
                w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND2" }, new CableEnd { ObjectId = o.Id, Pin = "GND" }, 26, "#1F1F23");
            }

            Power(lidar, "VCC", "5V");
            Power(gps, "VCC", "5V");
            Power(imu, "VCC", "5V");
            Power(mag, "VCC", "5V");
            Power(cam, "VCC", "3V3");
            Power(ml, "VCC", "5V");
            Power(mr, "VCC", "5V");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = uno.Id, Pin = "GND3" }, 22, "#1F1F23");

            // Things to look at.
            var red = w.AddBody("box", 0.12, 0.12, 0.12, new WorldPose { X = 0.75, Y = -0.35 }, "PLA");
            red.Color = "#E5484D";
            red.Name = "Kırmızı kutu";
            var yellow = w.AddBody("cylinder", 0.1, 0.1, 0.2, new WorldPose { X = 0.5, Y = 0.3 }, "PLA");
            yellow.Color = "#F2D04B";
            yellow.Name = "Sarı silindir";
            var green = w.AddBody("box", 0.08, 0.2, 0.08, new WorldPose { X = -0.4, Y = 0.4 }, "PLA");
            green.Color = "#3CD064";
            green.Name = "Yeşil blok";
            return w;
        }
    }
}
