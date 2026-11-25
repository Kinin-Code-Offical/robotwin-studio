using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// [stepper] A NEMA 17 stepper (42.3 mm square, chamfered corners): the black anodised front and rear caps, the
    /// laminated stator between them, the Ø22 pilot boss and the four M3 holes on the front at 31 mm, the side connector;
    /// its Ø5 D-cut shaft as a part of its own (it turns). An A4988 carrier: the PCB, two rows of eight header pins, the
    /// QFN chip under a finned heat sink, the current-limit trimmer, the sense resistors. Millimetres; the motor stands on
    /// its rear cap with the shaft up, the carrier lies on its pins.
    /// </summary>
    internal static partial class Gen
    {
        /// <summary>The NEMA 17 square with its corners chamfered (mm), counter-clockwise.</summary>
        private static List<Point> NemaOutline(double side = 42.3, double chamfer = 4.8)
        {
            double h = side / 2, c = h - chamfer;
            return new List<Point>
            {
                new Point(c, -h), new Point(h, -c), new Point(h, c), new Point(c, h),
                new Point(-c, h), new Point(-h, c), new Point(-h, -c), new Point(-c, -h)
            };
        }

        internal static void StepperBody(PartBuilder b, double length)
        {
            double cap = Math.Min(8.5, length * 0.25), stack = length - 2 * cap;
            var anodised = b[Mat.Metal(0x232428u, 0.45, 25)];
            var steel = b[Mat.Metal(0x9DA2A9u, 0.75, 40)];
            var line = b[Mat.Plastic(0x18191Cu, 0.1)];
            anodised.Extrude(NemaOutline(), 0, cap);
            steel.Extrude(NemaOutline(42.0, 4.7), cap, cap + stack);
            // The laminations' seams: a few dark lines round the stack.
            for (int k = 1; k < 6; k++)
            {
                double z = cap + stack * k / 6.0;
                line.Extrude(NemaOutline(42.06, 4.72), z - 0.08, z + 0.08, false, false, false);
            }

            anodised.Extrude(NemaOutline(), cap + stack, length);
            // Pilot boss, the shaft's bearing seat and the four M3 holes on the front cap.
            anodised.Lathe(new[] { new Point(11, length), new Point(11, length + 2), new Point(11, length + 2), new Point(3.2, length + 2) }, 48);
            b[Mat.Metal(0xC9CDD2u, 0.9, 60)].Lathe(new[] { new Point(4.6, length + 2.05), new Point(2.6, length + 2.05) }, 32);
            foreach (var (x, y) in new[] { (15.5, 15.5), (-15.5, 15.5), (-15.5, -15.5), (15.5, -15.5) })
                line.CylinderZ(x, y, length - 0.02, length + 0.01, 1.5, 16);
            // Four rear screws holding the caps together.
            foreach (var (x, y) in new[] { (15.5, 15.5), (-15.5, 15.5), (-15.5, -15.5), (15.5, -15.5) })
            {
                b[Mat.Metal(0x3A3C41u, 0.6, 30)].CylinderZ(x, y, -0.6, 0.01, 2.6, 16);
                line.CylinderZ(x, y, -0.62, -0.58, 1.2, 6);
            }

            // The side connector (JST-PH 6) on the rear cap, its four used contacts.
            b[Mat.Plastic(0xEDEBE4u, 0.2)].Box(21.15 + 2.8, 0, cap / 2, 5.6, 13.5, 6.0);
            var contact = b[Mat.Metal(0xC9A54Cu, 0.8, 40)];
            foreach (double y in new[] { -3.0, -1.0, 1.0, 3.0 }) contact.Box(21.15 + 5.65, y, cap / 2, 0.2, 1.0, 2.0);
        }

        /// <summary>The Ø5 shaft above the boss, with its D flat (from the front cap, mm).</summary>
        internal static void StepperShaft(PartBuilder b, double length)
        {
            double z0 = length + 2, z1 = length + 24;
            var steel = b[Mat.Metal(0xD4D8DDu, 0.95, 75)];
            var d = MeshBuilder.Circle(0, 0, 2.5, 28).Select(p => new Point(Math.Min(p.X, 2.0), p.Y)).ToList();
            steel.Extrude(MeshBuilder.Circle(0, 0, 2.5, 28), z0, z0 + 9, true, false, true);
            steel.Extrude(d, z0 + 9, z1 - 0.4, true, false, false);
            steel.Lathe(new[] { new Point(2.5, z1 - 0.4), new Point(2.1, z1), new Point(2.1, z1), new Point(0, z1) }, 28);
        }

        /// <summary>The A4988 carrier, 15.2 × 20.3 mm, lying on its header pins (board underside 8.5 mm up).</summary>
        internal static void A4988Carrier(PartBuilder b, uint pcb = 0x1B6B3Au)
        {
            double z0 = 8.5, t = 1.6, top = z0 + t;
            b[Mat.Plastic(pcb, 0.25, 20)].RoundedBox(0, 0, z0, 15.24, 20.32, t, 0.6);
            var pin = b[Mat.Metal(0xC9A54Cu, 0.9, 50)];
            var spacer = b[Mat.Plastic(0x151517u, 0.1)];
            var pad = b[Mat.Metal(0xD8DCE0u, 0.9, 60)];
            for (int k = 0; k < 8; k++)
            {
                double y = 8.89 - 2.54 * k;
                foreach (double x in new[] { -6.35, 6.35 })
                {
                    pin.Box(x, y, (z0 + 0.5) / 2, 0.64, 0.64, z0 + 0.5);
                    pad.CylinderZ(x, y, top - 0.01, top + 0.25, 0.75, 12);   // the solder joint on top
                }

                spacer.Box(-6.35, y, z0 - 1.25, 2.4, 2.5, 2.5);
                spacer.Box(6.35, y, z0 - 1.25, 2.4, 2.5, 2.5);
            }

            // The chip (QFN 5 × 5) under a finned aluminium heat sink.
            b[Mat.Plastic(0x121214u, 0.3)].Box(0, 1.5, top + 0.45, 5, 5, 0.9);
            var sink = b[Mat.Metal(0x2A64B8u, 0.6, 30)];
            sink.Box(0, 1.5, top + 1.4, 9, 9, 1.2);
            for (int f = 0; f < 5; f++) sink.Box(-3.6 + 1.8 * f, 1.5, top + 3.4, 0.9, 9, 3.0);
            // The current-limit trimmer (Vref), with its cross slot.
            b[Mat.Plastic(0xE8E6DEu, 0.3)].CylinderZ(-2.4, -6.6, top, top + 1.6, 1.6, 20);
            b[Mat.Metal(0xC9CDD2u, 0.8, 40)].CylinderZ(-2.4, -6.6, top + 1.6, top + 2.1, 1.3, 20);
            var slot = b[Mat.Plastic(0x202124u, 0.1)];
            slot.Box(-2.4, -6.6, top + 2.15, 2.2, 0.35, 0.12);
            slot.Box(-2.4, -6.6, top + 2.15, 0.35, 2.2, 0.12);
            // Sense resistors and capacitors.
            var res = b[Mat.Plastic(0x111113u, 0.2)];
            res.Box(2.2, -6.0, top + 0.3, 2.0, 1.25, 0.55);
            res.Box(2.2, -8.0, top + 0.3, 2.0, 1.25, 0.55);
            var cap = b[Mat.Plastic(0xB8935Au, 0.3)];
            cap.Box(-2.4, 7.6, top + 0.4, 1.6, 0.8, 0.8);
            cap.Box(2.4, 7.6, top + 0.4, 1.6, 0.8, 0.8);
        }
    }
}
