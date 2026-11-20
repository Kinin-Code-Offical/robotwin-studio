using System;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>Ready-made robot and drone projects: circuit plus mechanical assembly.</summary>
    public static class VehicleTemplates
    {
        /// <summary>Connects a pin to a named net by dropping a label on the pin tip.</summary>
        public static void Tie(Schematic sch, PartLibrary lib, SchematicPart part, string pin, string net)
        {
            var def = lib.Get(part.PartId);
            var p = def.FindPin(pin) ?? throw new ArgumentException($"{part.RefDes} has no pin {pin}");
            sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(p.X, p.Y) });
        }

        /// <summary>
        /// Analog line follower: two TCRT5000 reflectance sensors straddle the tape;
        /// each one, through a 74HC04 inverter, gates the PWM input of the motor on
        /// its own side. A sensor that sees black stops its motor so the robot turns
        /// back onto the line. Powered by 4×AA NiMH.
        /// </summary>
        public static Schematic LineFollower(PartLibrary lib)
        {
            var sch = new Schematic { Title = "Çizgi izleyen robot" };
            var bat = sch.AddPart(lib, "bat-4nimh", new GridPoint(0, 0));
            Tie(sch, lib, bat, "+", "VBAT");
            Tie(sch, lib, bat, "-", "GND");

            var sl = sch.AddPart(lib, "tcrt5000", new GridPoint(14, -8));
            var sr = sch.AddPart(lib, "tcrt5000", new GridPoint(14, 8));
            foreach (var (s, net) in new[] { (sl, "SL"), (sr, "SR") })
            {
                Tie(sch, lib, s, "VCC", "VBAT");
                Tie(sch, lib, s, "GND", "GND");
                Tie(sch, lib, s, "OUT", net);
            }

            var il = sch.AddPart(lib, "74hc04", new GridPoint(26, -8));
            var ir = sch.AddPart(lib, "74hc04", new GridPoint(26, 8));
            foreach (var (g, input, output) in new[] { (il, "SL", "PWML"), (ir, "SR", "PWMR") })
            {
                Tie(sch, lib, g, "A", input);
                Tie(sch, lib, g, "Y", output);
                Tie(sch, lib, g, "VCC", "VBAT");
                Tie(sch, lib, g, "GND", "GND");
            }

            var dl = sch.AddPart(lib, "tb6612-ch", new GridPoint(40, -8));
            var dr = sch.AddPart(lib, "tb6612-ch", new GridPoint(40, 8));
            var ml = sch.AddPart(lib, "motor-tt", new GridPoint(54, -8));
            var mr = sch.AddPart(lib, "motor-tt", new GridPoint(54, 8));
            foreach (var (d, m, pwm, outNet) in new[] { (dl, ml, "PWML", "ML"), (dr, mr, "PWMR", "MR") })
            {
                Tie(sch, lib, d, "VM", "VBAT");
                Tie(sch, lib, d, "GND", "GND");
                Tie(sch, lib, d, "PWM", pwm);
                Tie(sch, lib, d, "OUT", outNet);
                Tie(sch, lib, m, "A", outNet);
                Tie(sch, lib, m, "B", "GND");
            }

            sch.Assembly = AssemblyDocument.AutoConfigure(sch, lib, new AssemblyDocument { Kind = VehicleKind.Rover, Arena = "oval" });
            return sch;
        }

        /// <summary>
        /// Brushed micro quadcopter: 1S 300 mAh LiPo, F3 brushed flight controller
        /// (IMU + PID + four MOSFETs) and four 8520 coreless motors with 55 mm props.
        /// </summary>
        public static Schematic MicroQuadcopter(PartLibrary lib)
        {
            var sch = new Schematic { Title = "Mikro quadcopter" };
            var bat = sch.AddPart(lib, "bat-1s-lipo-300", new GridPoint(0, 0));
            Tie(sch, lib, bat, "+", "VBAT");
            Tie(sch, lib, bat, "-", "GND");
            var fc = sch.AddPart(lib, "fc-brushed", new GridPoint(18, 0));
            Tie(sch, lib, fc, "VBAT", "VBAT");
            Tie(sch, lib, fc, "GND", "GND");
            for (int i = 1; i <= 4; i++)
            {
                var m = sch.AddPart(lib, "motor-8520", new GridPoint(36, -12 + (i - 1) * 8));
                Tie(sch, lib, fc, "M" + i, "M" + i);
                Tie(sch, lib, m, "A", "M" + i);
                Tie(sch, lib, m, "B", "GND");
            }

            sch.Assembly = AssemblyDocument.AutoConfigure(sch, lib, new AssemblyDocument { Kind = VehicleKind.Quadcopter, Arena = "empty" });
            return sch;
        }

        /// <summary>
        /// Arduino Uno robot that avoids obstacles with an HC-SR04: real compiled
        /// firmware (builtin:obstacle-avoider.hex) drives two TB6612 channels from
        /// D5/D6; 2S LiPo into VIN. Runs in the obstacle arena.
        /// </summary>
        public static Schematic ObstacleAvoider(PartLibrary lib, string firmware = "builtin:obstacle-avoider.hex")
        {
            var sch = new Schematic { Title = "Engelden kaçan robot (Arduino)" };
            var bat = sch.AddPart(lib, "bat-2s-lipo", new GridPoint(-30, 0));
            Tie(sch, lib, bat, "+", "VBAT");
            Tie(sch, lib, bat, "-", "GND");
            var uno = sch.AddPart(lib, "arduino-uno", new GridPoint(0, 0));
            uno.Parameters["firmware"] = firmware;
            uno.Parameters["usb"] = "false";
            Tie(sch, lib, uno, "VIN", "VBAT");
            Tie(sch, lib, uno, "GND1", "GND");
            Tie(sch, lib, uno, "5V", "5V");
            Tie(sch, lib, uno, "D9", "TRIG");
            Tie(sch, lib, uno, "D10", "ECHO");
            Tie(sch, lib, uno, "D5", "PWML");
            Tie(sch, lib, uno, "D6", "PWMR");

            var us = sch.AddPart(lib, "hc-sr04", new GridPoint(28, -14));
            Tie(sch, lib, us, "VCC", "5V");
            Tie(sch, lib, us, "GND", "GND");
            Tie(sch, lib, us, "TRIG", "TRIG");
            Tie(sch, lib, us, "ECHO", "ECHO");

            var dl = sch.AddPart(lib, "tb6612-ch", new GridPoint(28, 2));
            var dr = sch.AddPart(lib, "tb6612-ch", new GridPoint(28, 16));
            var ml = sch.AddPart(lib, "motor-tt", new GridPoint(44, 2));
            var mr = sch.AddPart(lib, "motor-tt", new GridPoint(44, 16));
            foreach (var (d, m, pwm, outNet) in new[] { (dl, ml, "PWML", "ML"), (dr, mr, "PWMR", "MR") })
            {
                Tie(sch, lib, d, "VM", "VBAT");
                Tie(sch, lib, d, "GND", "GND");
                Tie(sch, lib, d, "PWM", pwm);
                Tie(sch, lib, d, "OUT", outNet);
                Tie(sch, lib, m, "A", outNet);
                Tie(sch, lib, m, "B", "GND");
            }

            sch.Assembly = AssemblyDocument.AutoConfigure(sch, lib, new AssemblyDocument { Kind = VehicleKind.Rover, Arena = "obstacles" });
            // Motors are ordered by x in the schematic; here they share x, so set the sides explicitly.
            sch.Assembly.Motors.Clear();
            sch.Assembly.Motors.Add(new MotorMount { RefDes = ml.RefDes, Role = "L" });
            sch.Assembly.Motors.Add(new MotorMount { RefDes = mr.RefDes, Role = "R" });
            return sch;
        }

        /// <summary>Two-motor rover chassis without sensors, for the user's own controller (Arduino, ESP32 …).</summary>
        public static AssemblyDocument EmptyRover() => new AssemblyDocument { Kind = VehicleKind.Rover, Arena = "obstacles" };
    }
}
