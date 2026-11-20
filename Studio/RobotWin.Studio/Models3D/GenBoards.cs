using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Models3D
{
    internal static partial class Gen
    {
        // ================================================================== Arduino Uno R3

        private const uint UnoTeal = 0x0D6E92u;

        /// <summary>
        /// Arduino Uno R3 (68.6 × 53.3 mm) sitting on its header tails over the footprint: the
        /// female headers stand exactly over the footprint pads (shield positions).
        /// </summary>
        internal static void ArduinoUno(PartBuilder b, ModelRequest r)
        {
            const double W = 68.58, H = 53.34, z0 = 2.0, t = 1.6, top = z0 + t;
            var pads = r.Pads.ToList();
            Pcb(b, 0, 0, W, H, z0, UnoTeal, "uno", dc => UnoSilk(dc, pads), t, 1.2);

            // Female headers over the shield pads, tails through the carrier board.
            FemaleHeaderPins(b, pads.Select(p => (p.X, p.Y)), top);
            foreach (var p in pads)
            {
                SquarePin(b, p.X, p.Y, Tail, z0 + 0.3, 0.64, Mat.Tin);
                b.Anchor(p.Name, P(p.X, p.Y, top + 8.5), V(0, 0, 1));
            }

            UsbB(b, -6.35, 15.3, top);
            DcJack(b, -1.8, 45.7, top);

            // ATmega328P-PU in its DIP-28 socket (notch towards the right edge).
            double cx = 47.3, cy = 37.6, len = 35.4;
            b[Mat.Plastic(0x1C1C1Fu, 0.1)].BoxZ(cx, cy, top, len + 1.0, 10.2, 1.4);
            for (int i = 0; i < 14; i++)
            {
                double x = cx - 13 * 2.54 / 2 + i * 2.54;
                foreach (int s in new[] { -1, 1 }) b[Mat.Tin].Strip(new List<Point3D> { P(x, cy + s * 3.3, top + 2.6), P(x, cy + s * 3.95, top + 2.6), P(x, cy + s * 3.95, top + 1.4) }, 0.9, 0.25, V(1, 0, 0));
            }

            var chip = b.Body(0x18181Bu, 0.3);
            chip.Tapered(cx, cy, top + 1.4, len, 7.1, 1.4, -0.12, -0.12);
            chip.Tapered(cx, cy, top + 2.8, len + 0.24, 7.34, 2.0, 0.15, 0.15);
            b.Heat(P(cx, cy, top + 4.8));
            var dark = b[Mat.Plastic(0x0B0B0Cu, 0.05)];
            using (b.At(cx + len / 2 - 0.15, cy, 0)) dark.Lathe(new[] { new Point(0.9, top + 4.805), new Point(0, top + 4.805) }, 16, Math.PI / 2, Math.PI * 1.5);
            TopLabel(b, "ATMEGA328P-PU", cx - 1.5, cy - 1.0, top + 4.81, 19.0, 2.2, Marking, 0, true);
            TopLabel(b, "1838   AVR", cx - 1.5, cy + 1.6, top + 4.81, 9.0, 1.6, Marking);

            // USB bridge (ATmega16U2), its crystal and ICSP, reset button, regulator, caps, LEDs.
            Quad(b, 21.0, 21.0, top, 5.0, 0.9, 8, 0, "16U2", 0);
            Crystal49(b, 27.0, 26.0, top, 0, 11.0, 4.65, 3.6);
            foreach (var (x0, y0) in new[] { (63.5, 24.5), (15.0, 6.0) })
            {
                var icsp = new List<(double X, double Y)>();
                for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) icsp.Add((x0 + j * 2.54, y0 + i * 2.54));
                MaleHeaderPins(b, icsp, top, top + 8.5, top - 1.5);
            }

            TactSmd(b, 5.2, 4.2, top, 6.0, 0x2A2A2Cu, 3.0);
            Sot223Body(b, 17.8, 45.4, top, 90, "1117-5.0");
            SmdElectrolytic(b, 17.4, 36.4, top, 6.3, 5.4);
            SmdElectrolytic(b, 24.4, 36.4, top, 6.3, 5.4);
            SmdChip(b, 13.6, 29.5, top, 4.3, 2.6, 2.1, 0x222225u, 90, "M7");
            SmdLed(b, 29.6, 9.6, top, 0xF2B21Cu);
            SmdLed(b, 29.6, 12.6, top, 0xF2C21Cu);
            SmdLed(b, 29.6, 15.0, top, 0xF2C21Cu);
            SmdLed(b, 60.4, 19.6, top, 0x3BD45Au);
            foreach (var (x, y, rot) in new[] { (35.0, 11.5, 0), (38.0, 11.5, 0), (41.0, 11.5, 0), (25.0, 31.0, 90), (33.0, 26.5, 0), (57.0, 26.0, 90), (12.0, 18.0, 90), (12.0, 23.0, 90) })
                SmdChip(b, x, y, top, 1.6, 0.8, 0.5, rot == 90 ? 0xB48A5Eu : 0x16161Au, rot);
        }

        private static void UnoSilk(DrawingContext dc, IReadOnlyList<Pad> pads)
        {
            var mask = Frozen(new SolidColorBrush(Mat.C(Mix(UnoTeal, 0xFFFFFFu, 0.1))));
            var pen = Frozen(new Pen(mask, 0.35));
            dc.DrawLine(pen, new Point(12, 20), new Point(30, 20));
            dc.DrawLine(pen, new Point(30, 20), new Point(34, 30));
            dc.DrawLine(pen, new Point(20, 30), new Point(20, 42));
            dc.DrawLine(pen, new Point(40, 6), new Point(40, 30));
            dc.DrawLine(pen, new Point(44, 6), new Point(48, 30));
            dc.DrawLine(pen, new Point(56, 30), new Point(64, 30));
            foreach (var (x, y) in new[] { (13.97, 50.8), (15.24, 2.54), (66.04, 45.72), (66.04, 17.78) }) MountHole(dc, x, y);
            foreach (var p in pads) PadRing(dc, p.X, p.Y, 0.9, 0.5);
            var silk = SilkBrush;
            // Logo: the infinity sign, ARDUINO and UNO.
            var logoPen = Frozen(new Pen(silk, 0.7));
            dc.DrawEllipse(null, logoPen, new Point(36.0, 20.5), 2.6, 2.1);
            dc.DrawEllipse(null, logoPen, new Point(41.0, 20.5), 2.6, 2.1);
            SilkText(dc, "—", 36.0, 20.3, 2.0, true, 0, true);
            SilkText(dc, "+", 41.0, 20.5, 2.4, true, 0, true);
            SilkText(dc, "ARDUINO", 52.0, 17.6, 3.2, true, 0, true);
            SilkText(dc, "UNO", 52.5, 24.0, 5.6, true, 0, true);
            SilkText(dc, "DIGITAL (PWM~)", 44.5, 7.6, 1.6, true, 0, true);
            SilkText(dc, "POWER", 34.3, 46.8, 1.7, true, 0, true);
            SilkText(dc, "ANALOG IN", 54.6, 46.8, 1.7, true, 0, true);
            SilkText(dc, "RESET", 5.2, 8.6, 1.2);
            SilkText(dc, "ON", 60.4, 17.8, 1.1);
            SilkText(dc, "L", 27.6, 9.6, 1.1);
            SilkText(dc, "TX", 27.4, 12.6, 1.0);
            SilkText(dc, "RX", 27.4, 15.0, 1.0);
            SilkText(dc, "ICSP", 61.0, 27.0, 1.2, true, -90);
            SilkText(dc, "AREF", 26.4, 5.3, 0.95, true, -90);
            foreach (var p in pads)
            {
                string name = p.Name switch { "GND1" or "GND2" or "GND3" => "GND", "3V3" => "3.3V", "NC" => string.Empty, _ => p.Name.StartsWith("D", StringComparison.Ordinal) ? p.Name.Substring(1) : p.Name };
                if (name.Length == 0 || p.Name == "AREF") continue;
                bool topRow = p.Y < 20;
                SilkText(dc, name, p.X, topRow ? p.Y + 3.4 : p.Y - 3.3, 0.95, true, -90);
            }

            SilkText(dc, "MADE IN ITALY", 50.0, 31.0, 1.0);
        }

        // ================================================================== Arduino Nano

        private const uint NanoBlue = 0x1E5AA8u;

        internal static void ArduinoNano(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            double minX = pads.Min(p => p.X), maxX = pads.Max(p => p.X), minY = pads.Min(p => p.Y), maxY = pads.Max(p => p.Y);
            double x0 = minX - 1.4, x1 = maxX + 1.4, y0 = minY - 4.3, y1 = maxY + 4.1, z0 = 2.5, top = z0 + 1.6;
            double cx = (x0 + x1) / 2;
            Pcb(b, x0, y0, x1, y1, z0, NanoBlue, "nano|" + F(maxX - minX), dc =>
            {
                foreach (var p in pads) PadRing(dc, p.X, p.Y, 0.85, 0.5, p.Name == "D1");
                for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++) PadRing(dc, cx - 2.54 + i * 2.54, y0 + 1.1 + j * 2.3, 0.7, 0.4);
                foreach (var p in pads)
                {
                    string name = p.Name.Replace("GND1", "GND").Replace("GND2", "GND").Replace("RESET2", "RST").Replace("RESET", "RST");
                    if (name == "D1") name = "TX1";
                    if (name == "D0") name = "RX0";
                    bool left = p.X < cx;
                    SilkText(dc, name, left ? p.X + 2.4 : p.X - 2.4, p.Y, 0.85);
                }

                SilkText(dc, "NANO", cx - 1.2, minY + 5.2, 2.2, true, 90, true);
                SilkText(dc, "ARDUINO", cx + 1.4, minY + 5.2, 1.0, true, 90);
                SilkText(dc, "TX RX PWR L", cx - 4.6, maxY - 10.0, 0.8, true, 90);
            }, 1.6, 0.6);
            foreach (var p in pads)
            {
                b[Mat.Plastic(0x1C1C1Fu, 0.08)].Tapered(p.X, p.Y, 0, 2.5, 2.5, z0, 0.1);
                SquarePin(b, p.X, p.Y, Tail, top + 0.6, 0.64, Mat.Gold);
                using (b.At(p.X, p.Y, top)) b[Mat.Solder].Frustum(0.8, 0.35, 0, 0.55, 12, false, true);
            }

            Quad(b, cx, (y0 + y1) / 2 - 1.5, top, 7.0, 1.0, 8, 45, "MEGA328P", 0.75, true);
            b.Heat(P(cx, (y0 + y1) / 2 - 1.5, top + 1.1));
            UsbSmall(b, cx, y1 + 1.3, top, false);
            TactSmd(b, cx, maxY - 9.5, top, 3.8, 0xEDEDE8u, 1.8);
            Crystal49(b, cx + 3.6, minY + 6.5, top, 90, 5.0, 3.2, 1.0, "16.000");
            foreach (var (y, c) in new[] { (maxY - 13.0, 0xF2C21Cu), (maxY - 11.6, 0xF2C21Cu), (maxY - 10.2, 0x3BD45Au), (maxY - 8.8, 0xF29A1Cu) })
                SmdLed(b, cx - 4.6, y, top, c, 90);
            foreach (var (x, y) in new[] { (cx - 3.6, minY + 4.0), (cx + 3.8, minY + 12.0), (cx - 3.8, maxY - 4.0), (cx + 4.0, maxY - 4.5) })
                SmdChip(b, x, y, top, 1.6, 0.8, 0.5, 0xB48A5Eu, 90);
        }

        // ================================================================== ESP32 development boards

        internal static void Esp32(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            var fp = r.Fp!;
            bool v1 = r.FpId.Contains("V1") || r.Id == Esp32Parts.DevKitV1Id;
            double x0 = fp.CourtyardMinX + 0.25, x1 = fp.CourtyardMaxX - 0.25, y0 = fp.CourtyardMinY + 0.25, y1 = fp.CourtyardMaxY - 0.25;
            double z0 = 2.5, top = z0 + 1.6, cx = (pads.Min(p => p.X) + pads.Max(p => p.X)) / 2, minY = pads.Min(p => p.Y), maxY = pads.Max(p => p.Y);
            uint mask = v1 ? 0x1A1F3Au : 0x141518u;
            Pcb(b, x0, y0, x1, y1, z0, mask, (v1 ? "esp32v1|" : "esp32c|") + F(x1 - x0), dc =>
            {
                foreach (var p in pads) PadRing(dc, p.X, p.Y, 0.85, 0.5, p.Name == "3V3" && !v1 || p.Name == "EN" && v1);
                foreach (var p in pads)
                {
                    string name = p.Name.StartsWith("IO", StringComparison.Ordinal) ? (v1 ? "D" + p.Name.Substring(2) : p.Name.Substring(2)) : p.Name.Replace("GND1", "GND").Replace("GND2", "GND").Replace("GND3", "GND");
                    if (v1 && name == "TX") name = "TX0";
                    if (v1 && name == "RX") name = "RX0";
                    bool left = p.X < cx;
                    SilkText(dc, name, left ? p.X + 2.3 : p.X - 2.3, p.Y, 0.85);
                }

                SilkText(dc, "EN", x0 + 3.8, y1 - 6.3, 1.0);
                SilkText(dc, "BOOT", x1 - 3.8, y1 - 6.3, 1.0);
                SilkText(dc, v1 ? "ESP32 DEVKIT V1 - DOIT" : "ESP32-DevKitC", cx, maxY - 12.6, 1.3, true, 0, true);
                SilkText(dc, v1 ? "docs.doit.am" : "ESPRESSIF", cx, maxY - 10.9, 0.9);
            }, 1.6, 0.8);
            foreach (var p in pads)
            {
                b[Mat.Plastic(0x1C1C1Fu, 0.08)].Tapered(p.X, p.Y, 0, 2.5, 2.5, z0, 0.1);
                SquarePin(b, p.X, p.Y, Tail, top + 0.6, 0.64, Mat.Gold);
                using (b.At(p.X, p.Y, top)) b[Mat.Solder].Frustum(0.8, 0.35, 0, 0.55, 12, false, true);
            }

            // ESP32-WROOM-32 module: 18 × 25.5 mm, antenna end over the board edge.
            double mx = cx, my0 = y0, my1 = y0 + 25.5;
            Pcb(b, mx - 9, my0, mx + 9, my1, top, 0x1B1E26u, "wroom", dc =>
            {
                var gold = Gold;
                for (int i = 0; i < 14; i++)
                {
                    double y = my0 + 7.5 + i * 1.27;
                    dc.DrawEllipse(gold, null, new Point(mx - 9, y), 0.45, 0.45);
                    dc.DrawEllipse(gold, null, new Point(mx + 9, y), 0.45, 0.45);
                }

                for (int i = 0; i < 10; i++) dc.DrawEllipse(gold, null, new Point(mx - 5.7 + i * 1.27, my1), 0.45, 0.45);
                var ant = Frozen(new Pen(gold, 0.5) { LineJoin = PenLineJoin.Miter });
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(mx - 7.5, my0 + 5.6), false, false);
                    double y = my0 + 5.6;
                    for (int i = 0; i < 6; i++)
                    {
                        double x = mx - 7.5 + i * 2.5;
                        g.LineTo(new Point(x, my0 + 1.2), true, false);
                        g.LineTo(new Point(x + 1.25, my0 + 1.2), true, false);
                        g.LineTo(new Point(x + 1.25, y - 1.6), true, false);
                        g.LineTo(new Point(x + 2.5, y - 1.6), true, false);
                    }
                }

                dc.DrawGeometry(null, ant, geo);
            }, 0.8, 0.3, 24);
            double canY0 = my0 + 7.0, canY1 = my1 - 0.8;
            b.Body(0xC4C8CEu, 0.85).RoundedBox(mx, (canY0 + canY1) / 2, top + 0.8, 16.6, canY1 - canY0, 2.3, 0.4, 2);
            b.Heat(P(mx, (canY0 + canY1) / 2, top + 3.1));
            var ink = Color.FromRgb(0x3A, 0x3C, 0x42);
            TopLabel(b, "ESP32-WROOM-32", mx, (canY0 + canY1) / 2 - 3.6, top + 3.11, 12.5, 1.8, ink, 0, true);
            TopLabel(b, "(M) 211-161007\nFCC ID:2AC7Z-ESPWROOM32", mx, (canY0 + canY1) / 2 + 0.6, top + 3.11, 12.5, 3.0, ink);
            TopLabel(b, "Ⓔ ESPRESSIF", mx, (canY0 + canY1) / 2 + 4.6, top + 3.11, 9.0, 1.6, ink, 0, true);

            UsbSmall(b, cx, y1 + 0.8, top, true);
            TactSmd(b, x0 + 3.8, y1 - 3.6, top, 3.6, 0x1E1E20u, 1.8);
            TactSmd(b, x1 - 3.8, y1 - 3.6, top, 3.6, 0x1E1E20u, 1.8);
            Quad(b, cx, maxY - 7.5, top, 5.0, 0.9, 7, 0, "CP2102", 0);
            Sot223Body(b, cx, my1 + 4.6, top, 0, "AMS1117");
            SmdLed(b, x0 + 4.2, my1 + 3.0, top, 0xE02020u, 90);
            if (v1) SmdLed(b, x1 - 4.2, my1 + 3.0, top, 0x2F6CF2u, 90);
            foreach (var (x, y) in new[] { (x0 + 4.5, my1 + 8.0), (x1 - 4.5, my1 + 8.0), (cx - 4.5, maxY - 3.0), (cx + 4.5, maxY - 3.0), (x0 + 4.5, my1 + 12.0) })
                SmdChip(b, x, y, top, 1.6, 0.8, 0.5, 0xB48A5Eu, 90);
        }
    }
}
