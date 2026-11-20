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
        private const double ModZ = 2.5;

        /// <summary>
        /// Frame for a module on a single-row header: pad 1 at the origin, the row running
        /// along +y, the module board extending towards +x. Returns the pin positions.
        /// </summary>
        private static Frame.Scope ModuleFrame(PartBuilder b, ModelRequest r, out List<(string Name, double Y)> pins, out double yc)
        {
            var pads = r.Pads.ToList();
            var first = r.Pad("1") ?? pads.OrderBy(p => p.Y).ThenBy(p => p.X).First();
            var far = pads.OrderByDescending(p => (p.X - first.X) * (p.X - first.X) + (p.Y - first.Y) * (p.Y - first.Y)).First();
            double dx = far.X - first.X, dy = far.Y - first.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double ang = len < 1e-6 ? 0 : Math.Atan2(dy, dx) * 180 / Math.PI - 90;
            double ux = len < 1e-6 ? 0 : dx / len, uy = len < 1e-6 ? 1 : dy / len;
            pins = pads.Select(p => (p.Name, (p.X - first.X) * ux + (p.Y - first.Y) * uy)).OrderBy(p => p.Item2).ToList();
            yc = pins.Count > 0 ? (pins[0].Y + pins[pins.Count - 1].Y) / 2 : 0;
            return b.At(Mx.Rz(ang) * Mx.T(first.X, first.Y, 0));
        }

        /// <summary>Header under a module board: plastic spacer, pins through both boards, solder on top.</summary>
        private static void ModulePins(PartBuilder b, IEnumerable<(string Name, double Y)> pins, double z0 = ModZ, double x = 0)
        {
            double top = z0 + 1.6;
            foreach (var (_, y) in pins)
            {
                b[Mat.Plastic(0x1C1C1Fu, 0.08)].Tapered(x, y, 0, 2.5, 2.5, z0, 0.1);
                SquarePin(b, x, y, Tail, top + 0.7, 0.64, Mat.Gold);
                using (b.At(x, y, top)) b[Mat.Solder].Frustum(0.8, 0.35, 0, 0.55, 12, false, true);
            }
        }

        private static void PinLabels(DrawingContext dc, IEnumerable<(string Name, double Y)> pins, IReadOnlyList<string> names, double x, double size = 0.9, double angle = 0)
        {
            int i = 0;
            foreach (var (_, y) in pins)
            {
                if (i < names.Count) SilkText(dc, names[i], x, y, size, true, angle);
                PadRing(dc, 0, y, 0.85, 0.5, i == 0);
                i++;
            }
        }

        private static IReadOnlyList<string> PinNames(ModelRequest r, IEnumerable<(string Name, double Y)> pins) =>
            pins.Select(p => r.Def?.Pins.FirstOrDefault(d => string.Equals(r.Def.ResolvePad(d, r.P), p.Name, StringComparison.OrdinalIgnoreCase))?.Name ?? p.Name).ToList();

        private static void SmdScatter(PartBuilder b, double top, params (double X, double Y, double Rot, bool Cap)[] parts)
        {
            foreach (var (x, y, rot, cap) in parts) SmdChip(b, x, y, top, 1.6, 0.8, cap ? 0.8 : 0.5, cap ? 0xB48A5Eu : 0x16161Au, rot);
        }

        // ================================================================== HC-SR04

        internal static void HcSr04(PartBuilder b, ModelRequest r)
        {
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                const double zb = 3.2, H = 20.0, L = 45.0, px0 = -2.6, px1 = -1.0;
                var names = PinNames(r, pins);
                // Right-angle header: pins up from the board, bent through the vertical PCB.
                foreach (var (_, y) in pins)
                {
                    b[Mat.Plastic(0x1C1C1Fu, 0.08)].BoxZ(0.1, y, zb - 0.2, 2.3, 2.5, 2.5);
                    b[Mat.Gold].Strip(MeshBuilder.Smooth(new[] { P(0, y, Tail), P(0, y, zb + 1.3), P(px0 - 1.0, y, zb + 1.3) }, 0.6, 3), 0.64, 0.64, V(0, 1, 0));
                    using (b.At(Mx.Ry(-90) * Mx.T(px0, y, zb + 1.3))) b[Mat.Solder].Frustum(0.85, 0.35, 0, 0.5, 12, false, true);
                }

                b[Mat.Plastic(0x1A4C95u, 0.15)].Block(px0, yc - L / 2, zb, px1, yc + L / 2, zb + H);
                var tex = Mat.Image("hcsr04-front", () => Tex.Render(900, 400, dc =>
                {
                    dc.PushTransform(new ScaleTransform(20, 20));
                    dc.DrawRectangle(new SolidColorBrush(Mat.C(0x1C56A6u)), null, new Rect(0, 0, L, H));
                    SilkText(dc, "HC-SR04", L / 2, 3.2, 2.0, true, 0, true);
                    SilkText(dc, "T", 9.5, 18.6, 1.6, true, 0, true);
                    SilkText(dc, "R", 35.5, 18.6, 1.6, true, 0, true);
                    SilkText(dc, "Vcc Trig Echo Gnd", L / 2, 16.7, 1.1);
                    foreach (var (x, y) in new[] { (1.6, 1.6), (L - 1.6, 1.6), (1.6, H - 1.6), (L - 1.6, H - 1.6) }) MountHole(dc, x, y, 1.6);
                    dc.Pop();
                }));
                b[tex].FrontQuad(P(px0 - 0.01, yc, zb + H / 2), V(-1, 0, 0), V(0, 0, 1), L, H);

                // Transducers: aluminium cans with the black mesh front.
                foreach (int s in new[] { -1, 1 })
                {
                    double ty = yc + s * 13.0, tz = zb + 10.6;
                    using (b.At(Mx.Ry(-90) * Mx.T(px0, ty, tz)))
                    {
                        b[Mat.Plastic(0x1A1A1Cu, 0.2)].Cylinder(8.3, 0, 1.2, 32, false, true);
                        b.Body(0xC9CCD2u, 0.75).Lathe(new[] { new Point(8.05, 1.2), new Point(8.05, 12.1), new Point(7.9, 12.3), new Point(6.9, 12.3), new Point(6.9, 12.3), new Point(6.9, 11.9) }, 36);
                        b[Mat.Plastic(0x2C2C30u, 0.05)].Cylinder(6.9, 11.85, 11.9, 32, false, true);
                    }
                }

                b.Heat(P(px0 - 6, yc, zb + 10));
                using (b.At(Mx.Ry(-90) * Mx.T(px0, yc, zb + 16.7)))
                {
                    b[Mat.Nickel].RoundedBox(0, 0, 0, 4.65, 11.0, 3.4, 2.3, 6);
                }

                // Driver chips on the back.
                foreach (var (y, z, w) in new[] { (yc - 9.0, zb + 11.0, 8.7), (yc + 9.0, zb + 11.0, 8.7), (yc, zb + 5.0, 5.0) })
                    b[Mat.Epoxy].Block(px1, y - w / 2, z - 2.0, px1 + 1.5, y + w / 2, z + 2.0);
                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        // ================================================================== SSD1306 OLED

        internal static void Oled(PartBuilder b, ModelRequest r)
        {
            bool small = r.Text("height", "64") == "32";
            string colour = r.Text("color", "white");
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                ModulePins(b, pins);
                double top = ModZ + 1.6;
                var names = PinNames(r, pins);
                if (small)
                {
                    // 0.91" 128×32: 38 × 12 mm, header on the short side.
                    Pcb(b, -2.0, yc - 6.0, 36.0, yc + 6.0, ModZ, 0x1D1D22u, "oled32", dc =>
                    {
                        PinLabels(dc, pins, names, 2.4, 0.8, -90);
                    }, 1.2, 0.6);
                    b.Body(0x08080Au, 0.6).BoxZ(19.0, yc, top, 30.0, 11.5, 1.2);
                    b.Screen("OLED", colour, Mat.C(0x050506u)).TopQuad(19.0, yc - 0.4, top + 1.21, 22.4, 5.6);
                    b.Heat(P(19, yc, top + 1.2));
                }
                else
                {
                    // 0.96" 128×64: 27.3 × 27.8 mm board, header along the top edge (here vertical).
                    Pcb(b, -2.2, yc - 13.65, 25.6, yc + 13.65, ModZ, 0x1C4E9Au, "oled64", dc =>
                    {
                        PinLabels(dc, pins, names, 2.6, 0.9, -90);
                        foreach (var (x, y) in new[] { (0.0, yc - 11.6), (0.0, yc + 11.6), (23.5, yc - 11.6), (23.5, yc + 11.6) }) MountHole(dc, x, y, 2.0);
                    }, 1.6, 0.8);
                    SmdScatter(b, top, (4.2, yc - 9.0, 90, true), (4.2, yc - 6.5, 90, false), (4.2, yc + 6.5, 90, false), (4.2, yc + 9.0, 90, true));
                    b.Body(0x08080Au, 0.6).BoxZ(14.3, yc, top, 19.3, 26.7, 1.4);
                    b[Mat.Plastic(0xB8702Au, 0.3)].BoxZ(24.4, yc, top, 1.6, 12.0, 1.2);
                    b.Screen("OLED", colour, Mat.C(0x050506u)).TopQuad(13.2, yc, top + 1.41, 21.74, 10.86, -90);
                    b.Heat(P(14, yc, top + 1.4));
                }

                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        // ================================================================== character LCD with I²C backpack

        internal static void Lcd(PartBuilder b, ModelRequest r)
        {
            bool big = r.Text("rows", "2") == "4" || r.Text("columns", "16") == "20";
            string backlight = r.Text("backlight", "blue");
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                ModulePins(b, pins);
                double top = ModZ + 1.6;
                var names = PinNames(r, pins);
                // PCF8574 backpack under the display.
                Pcb(b, -1.6, yc - 8.0, 40.0, yc + 11.0, ModZ, 0x1C1C20u, "i2cpack", dc =>
                {
                    PinLabels(dc, pins, names, 2.6, 0.85);
                    for (int i = 0; i < 16; i++) PadRing(dc, 2.6 + i * 2.54, yc + 9.4, 0.75, 0.45);
                }, 1.6, 0.5);
                Soic(b, 18.0, yc + 1.5, top, 16, 1.27, 7.5, 0, "PCF8574T");
                b[Mat.Plastic(0x2152B8u, 0.3)].BoxZ(30.5, yc - 2.5, top, 9.5, 4.8, 6.5);
                b[Mat.Brass].CylinderZ(27.3, yc - 2.5, top + 6.5, top + 7.0, 1.1, 14);
                MaleHeaderPins(b, new[] { (36.0, yc + 4.0), (36.0, yc + 6.54) }, top, top + 6.5, top - 1.0);
                b[Mat.Plastic(0x1A1A1Cu, 0.1)].BoxZ(36.0, yc + 5.27, top + 3.0, 2.5, 5.0, 4.0);

                double lcdW = big ? 98.0 : 80.0, lcdH = big ? 60.0 : 36.0, lcdZ = 11.6;
                double x0 = -3.5, y0 = yc - 9.3, cx = x0 + lcdW / 2, cy = y0 + lcdH / 2;
                // 16-pin header carrying the LCD over the backpack.
                var row = new List<(double, double)>();
                for (int i = 0; i < 16; i++) row.Add((2.6 + i * 2.54, yc + 9.4));
                MaleHeaderPins(b, row, top, lcdZ + 2.1, top - 0.8, 2.5);
                Pcb(b, x0, y0, x0 + lcdW, y0 + lcdH, lcdZ, 0x2C7A33u, big ? "lcd2004" : "lcd1602", dc =>
                {
                    foreach (var (x, y) in new[] { (x0 + 2.5, y0 + 2.5), (x0 + lcdW - 2.5, y0 + 2.5), (x0 + 2.5, y0 + lcdH - 2.5), (x0 + lcdW - 2.5, y0 + lcdH - 2.5) }) MountHole(dc, x, y, 3.0);
                    for (int i = 0; i < 16; i++) PadRing(dc, 2.6 + i * 2.54, yc + 9.4, 0.75, 0.45);
                }, 1.6, 0.6);
                double bw = big ? 97.0 : 71.2, bh = big ? 40.0 : 24.2, bz = lcdZ + 1.6, bt = big ? 9.5 : 7.0;
                double vw = big ? 77.0 : 64.5, vh = big ? 25.2 : 16.0;
                double bcx = cx + (big ? 0 : -0.5), bcy = cy + (big ? 0 : 1.2);
                var frame = b.Body(0x141416u, 0.4);
                frame.BoxZ(bcx, bcy - (bh + vh) / 4, bz, bw, (bh - vh) / 2, bt);
                frame.BoxZ(bcx, bcy + (bh + vh) / 4, bz, bw, (bh - vh) / 2, bt);
                frame.BoxZ(bcx - (bw + vw) / 4, bcy, bz, (bw - vw) / 2, vh, bt);
                frame.BoxZ(bcx + (bw + vw) / 4, bcy, bz, (bw - vw) / 2, vh, bt);
                b[Mat.Plastic(0x101418u, 0.2)].BoxZ(bcx, bcy, bz, vw, vh, bt - 0.6);
                b.Screen("LCD", backlight, Mat.C(backlight == "green" ? 0x4A5A22u : 0x12204Au)).TopQuad(bcx, bcy, bz + bt - 0.59, big ? 74.0 : 62.0, big ? 23.0 : 13.6);
                b[Mat.Clear(0xDCE6F0u, 40, 0.95)].BoxZ(bcx, bcy, bz + bt - 0.5, vw, vh, 0.3);
                b.Heat(P(bcx, bcy, bz + bt));
                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        // ================================================================== GY-521 (MPU-6050)

        internal static void Gy521(PartBuilder b, ModelRequest r)
        {
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                ModulePins(b, pins);
                double top = ModZ + 1.6;
                string[] labels = { "VCC", "GND", "SCL", "SDA", "XDA", "XCL", "AD0", "INT" };
                Pcb(b, -1.8, yc - 10.6, 14.6, yc + 10.6, ModZ, 0x1F4F9Cu, "gy521|" + pins.Count, dc =>
                {
                    PinLabels(dc, pins, labels, 3.0, 0.9);
                    MountHole(dc, 11.6, yc - 7.6, 3.0);
                    MountHole(dc, 11.6, yc + 7.6, 3.0);
                    SilkText(dc, "GY-521", 12.0, yc, 1.5, true, -90, true);
                    var pen = Frozen(new Pen(SilkBrush, 0.25));
                    dc.DrawLine(pen, new Point(6.5, yc + 5.5), new Point(9.5, yc + 5.5));
                    dc.DrawLine(pen, new Point(6.5, yc + 5.5), new Point(6.5, yc + 2.5));
                    SilkText(dc, "X", 10.3, yc + 5.5, 0.9);
                    SilkText(dc, "Y", 6.5, yc + 1.8, 0.9);
                }, 1.6, 0.6);
                Quad(b, 7.6, yc - 1.5, top, 4.0, 0.9, 6, 0, "MPU", 0, true);
                Sot23Like(b, 10.5, yc - 6.8, top);
                SmdLed(b, 4.5, yc + 7.5, top, 0x3BD45Au, 90);
                SmdScatter(b, top, (4.5, yc - 6.0, 90, true), (4.5, yc - 3.4, 90, false), (4.5, yc + 4.0, 90, false), (10.0, yc + 2.8, 0, true));
                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        private static void Sot23Like(PartBuilder b, double cx, double cy, double z0)
        {
            b[Mat.Epoxy].Tapered(cx, cy, z0 + 0.05, 2.9, 1.3, 1.0, 0.06);
            foreach (var (dx, s) in new[] { (-0.95, 1), (0.95, 1), (0.0, -1) })
                b[Mat.Tin].Strip(new List<Point3D> { P(cx + dx, cy + s * 0.5, z0 + 0.5), P(cx + dx, cy + s * 0.8, z0 + 0.5), P(cx + dx, cy + s * 0.95, z0 + 0.08), P(cx + dx, cy + s * 1.2, z0 + 0.08) }, 0.4, 0.13, V(1, 0, 0));
        }

        // ================================================================== motor drivers

        internal static void MotorDriver(PartBuilder b, ModelRequest r)
        {
            bool l298 = r.ModelHas("L298");
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                ModulePins(b, pins);
                double top = ModZ + 1.6;
                var names = PinNames(r, pins);
                if (!l298)
                {
                    Pcb(b, -1.3, yc - 10.15, 19.0, yc + 10.15, ModZ, 0x5B2C8Eu, "tb6612", dc =>
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            PadRing(dc, 0, yc - 8.89 + i * 2.54, 0.85, 0.5);
                            PadRing(dc, 17.78, yc - 8.89 + i * 2.54, 0.85, 0.5);
                        }

                        PinLabels(dc, pins, names, 2.6, 0.85);
                        SilkText(dc, "TB6612FNG", 9.0, yc - 7.8, 1.2, true, 0, true);
                    }, 1.6, 0.6);
                    Soic(b, 9.0, yc, top, 24, 0.65, 5.3, 90, "TB6612");
                    b.Body(0x1A1A1Du, 0.3).Box(9.0, yc, top + 1.5, 4.8, 7.4, 0.1);
                    b.Heat(P(9.0, yc, top + 1.6));
                    SmdScatter(b, top, (13.5, yc - 5.0, 90, true), (13.5, yc + 5.0, 90, true), (4.5, yc + 7.5, 0, true));
                }
                else
                {
                    L298Board(b, pins, names, yc, top);
                }

                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        private static void L298Board(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            double x0 = -3.0, x1 = 40.0, y0 = yc - 21.5, y1 = yc + 21.5;
            Pcb(b, x0, y0, x1, y1, ModZ, 0xB22A2Eu, "l298n", dc =>
            {
                PinLabels(dc, pins, names, 3.2, 0.95);
                foreach (var (x, y) in new[] { (x0 + 3.0, y0 + 3.0), (x1 - 3.0, y0 + 3.0), (x0 + 3.0, y1 - 3.0), (x1 - 3.0, y1 - 3.0) }) MountHole(dc, x, y, 3.0);
                SilkText(dc, "L298N", 18.0, y1 - 9.5, 1.6, true, 0, true);
                SilkText(dc, "OUT1 OUT2", 30.0, y0 + 9.2, 1.0);
                SilkText(dc, "OUT3 OUT4", 30.0, y1 - 9.2, 1.0);
                SilkText(dc, "12V GND 5V", 34.0, yc, 1.0, true, -90);
            }, 1.6, 1.0);
            // Heatsink with fins and the Multiwatt-15 driver standing against it.
            double hx = 19.0, hy = yc - 3.0;
            var sink = b.Body(0x1E1E21u, 0.35);
            sink.BoxZ(hx, hy, top, 15.0, 4.0, 25.0);
            for (int i = 0; i < 7; i++) sink.BoxZ(hx - 6.6 + i * 2.2, hy + 7.0, top + 0.5, 1.2, 10.0, 24.0);
            b.Heat(P(hx, hy, top + 25));
            b[Mat.Epoxy].BoxZ(hx, hy - 3.0, top + 3.0, 20.0, 2.0, 10.0);
            for (int i = 0; i < 15; i++) b[Mat.Tin].Box(hx - 9.0 + i * 1.27, hy - 3.0, top + 1.5, 0.5, 0.4, 3.0);

            FrontLabel(b, "L298N", P(hx, hy - 4.02, top + 9.0), V(0, -1, 0), V(0, 0, 1), 9.0, 2.2, Marking, true);
            // Screw terminals: motor outputs at the ends, power on the side.
            foreach (var (tx, ty, rot) in new[] { (31.0, y0 + 4.5, 0.0), (31.0, y1 - 4.5, 180.0), (36.5, yc, 90.0) })
            {
                int n = rot == 90.0 ? 3 : 2;
                using (b.At(Mx.Rz(rot) * Mx.T(tx, ty, top)))
                {
                    for (int i = 0; i < n; i++)
                    {
                        double x = (i - (n - 1) / 2.0) * 5.08;
                        using (b.At(Mx.Frame(P(x - 2.48, 0, 0), V(0, 1, 0), V(0, 0, 1), V(1, 0, 0))))
                            b[Mat.Plastic(0x1D64C8u, 0.25)].Extrude(new[] { new Point(-3.6, 0), new Point(4.0, 0), new Point(4.0, 7.4), new Point(3.0, 10.0), new Point(-3.6, 10.0) }, 0, 4.96);
                        b[Mat.Steel].CylinderZ(x, -0.3, 9.4, 10.25, 1.6, 16);
                        b[Mat.Plastic(0x0A0A0Bu, 0)].Block(x - 1.3, 4.02, 2.7, x + 1.3, 4.04, 5.5);
                    }
                }
            }

            // Electrolytics, 78M05 regulator, control header with jumpers, LED.
            foreach (var (x, y) in new[] { (6.0, y0 + 6.0), (6.0, y1 - 6.0) })
            {
                using (b.At(x, y, top))
                {
                    b[Mat.Plastic(0x1D2C55u, 0.3)].Lathe(new[] { new Point(3.95, 0.2), new Point(4.0, 0.6), new Point(4.0, 11.2), new Point(3.7, 11.6), new Point(3.6, 11.6) }, 24);
                    b[Mat.Aluminium].Cylinder(3.6, 11.4, 11.55, 24, false, true);
                }
            }

            b[Mat.Epoxy].BoxZ(12.0, y1 - 7.0, top, 6.5, 6.0, 2.3);
            b[Mat.Tin].BoxZ(12.0, y1 - 10.4, top, 5.2, 1.4, 0.5);
            var ctl = new List<(double, double)>();
            for (int i = 0; i < 6; i++) ctl.Add((24.0, yc - 6.35 + i * 2.54));
            MaleHeaderPins(b, ctl, top, top + 8.5, top - 1.0);
            foreach (var (x, y) in new[] { (24.0, yc - 6.35), (24.0, yc + 6.35) }) b[Mat.Plastic(0x1A1A1Cu, 0.2)].BoxZ(x + 1.27, y, top + 2.5, 5.0, 2.5, 6.0);
            SmdLed(b, 10.0, yc + 2.0, top, 0xE02020u, 90);
        }

        // ================================================================== brushed flight controller

        internal static void FlightController(PartBuilder b, ModelRequest r)
        {
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                ModulePins(b, pins);
                double top = ModZ + 1.6, cx = 12.0;
                var names = PinNames(r, pins);
                Pcb(b, -1.6, yc - 13.0, 25.6, yc + 13.0, ModZ, 0x121214u, "fc", dc =>
                {
                    PinLabels(dc, pins, names, 2.8, 0.85);
                    foreach (var (x, y) in new[] { (cx - 10, yc - 10), (cx + 10, yc - 10), (cx - 10, yc + 10), (cx + 10, yc + 10) }) MountHole(dc, x, y, 2.0);
                    SilkText(dc, "F3 BRUSHED", cx, yc + 7.5, 1.3, true, 0, true);
                    SilkText(dc, "▲ FRONT", cx + 8.5, yc, 1.0, true, -90);
                }, 1.0, 1.0);
                Quad(b, cx, yc - 1.0, top, 7.0, 1.4, 12, 45, "STM32F303", 0.5, true);
                Quad(b, cx + 6.5, yc + 4.0, top, 4.0, 0.9, 6, 0, "MPU6000", 0);
                foreach (var (x, y) in new[] { (cx - 8.0, yc - 8.0), (cx + 8.0, yc - 8.0), (cx - 8.0, yc + 8.0), (cx + 8.0, yc + 8.0) })
                {
                    Sot23Like(b, x + (x < cx ? 3.2 : -3.2), y, top);
                    b[Mat.Plastic(0xEDEDE6u, 0.2)].BoxZ(x, y, top, 4.2, 3.2, 3.4);
                    b[Mat.Plastic(0x101010u, 0)].BoxZ(x, y, top + 3.4, 3.2, 2.0, 0.02);
                }

                UsbSmall(b, -0.4, yc + 0.0, top, true);
                SmdLed(b, cx - 4.0, yc + 5.0, top, 0x2F6CF2u, 0);
                SmdLed(b, cx - 4.0, yc + 6.5, top, 0xE02020u, 0);
                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        // ================================================================== sensor modules

        internal static void SensorModule(PartBuilder b, ModelRequest r)
        {
            string q = r.Text("quantity").ToLowerInvariant();
            using (ModuleFrame(b, r, out var pins, out double yc))
            {
                ModulePins(b, pins);
                double top = ModZ + 1.6;
                var names = PinNames(r, pins);
                switch (q)
                {
                    case "reflectance":
                        Tcrt5000(b, pins, names, yc, top);
                        break;
                    case "obstacle":
                        IrObstacle(b, pins, names, yc, top);
                        break;
                    case "motion":
                        Pir(b, pins, names, yc, top);
                        break;
                    case "moisture":
                        SoilProbe(b, pins, names, yc, top);
                        break;
                    case "position":
                        Joystick(b, pins, names, yc, top);
                        break;
                    case "sound":
                        Microphone(b, pins, names, yc, top);
                        break;
                    default:
                        GenericSensor(b, r, pins, names, yc, top);
                        break;
                }

                for (int i = 0; i < pins.Count; i++) b.Anchor(pins[i].Name, P(0, pins[i].Y, Tail), V(0, 0, -1));
            }
        }

        private static void ComparatorBoard(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top, double len, double wid, string key, string title)
        {
            Pcb(b, -2.5, yc - wid / 2, len - 2.5, yc + wid / 2, ModZ, 0x1F55A8u, key, dc =>
            {
                PinLabels(dc, pins, names, 2.8, 0.85);
                MountHole(dc, len / 2 - 1.0, yc, 3.0);
                SilkText(dc, title, len * 0.36, yc + wid / 2 - 1.6, 0.9);
            }, 1.6, 0.6);
            Soic(b, 9.0, yc - 3.6, top, 8, 1.27, 3.9, 0, "LM393");
            // 3362 trimmer with its white cross knob.
            b[Mat.Plastic(0x2152B8u, 0.3)].BoxZ(8.5, yc + 3.4, top, 6.6, 6.8, 4.6);
            b[Mat.WhitePlastic].CylinderZ(8.5, yc + 3.4, top + 4.6, top + 5.4, 2.2, 18);
            b[Mat.Plastic(0x303030u, 0)].Box(8.5, yc + 3.4, top + 5.42, 3.2, 0.5, 0.04);
            b[Mat.Plastic(0x303030u, 0)].Box(8.5, yc + 3.4, top + 5.42, 0.5, 3.2, 0.04);
            SmdLed(b, 15.0, yc - 4.5, top, 0xE02020u, 90);
            SmdLed(b, 15.0, yc + 4.5, top, 0x3BD45Au, 90);
            SmdScatter(b, top, (13.0, yc, 90, false), (16.5, yc, 90, true), (3.5, yc + 5.0, 0, false));
        }

        private static void Tcrt5000(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            ComparatorBoard(b, pins, names, yc, top, 32.0, 14.0, "tcrt5000", "TCRT5000");
            var housing = b.Body(0x18181Bu, 0.25);
            housing.BoxZ(26.9, yc, top, 5.8, 10.2, 7.0);
            b.Heat(P(26.9, yc, top + 7));
            using (b.At(26.9, yc - 2.5, top + 7.0))
            {
                b[Mat.Clear(0x2A46D8u, 210, 0.9)].Lathe(new[] { new Point(1.45, -0.6), new Point(1.45, 0.4), new Point(1.0, 1.05), new Point(0, 1.3) }, 18);
            }

            using (b.At(26.9, yc + 2.5, top + 7.0))
            {
                b[Mat.Plastic(0x0E0E10u, 0.7)].Lathe(new[] { new Point(1.45, -0.6), new Point(1.45, 0.4), new Point(1.0, 1.05), new Point(0, 1.3) }, 18);
            }


        }

        private static void IrObstacle(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            ComparatorBoard(b, pins, names, yc, top, 31.0, 14.0, "fc51", "FC-51");
            foreach (var (dy, dark) in new[] { (-3.0, false), (3.0, true) })
            {
                double y = yc + dy, z = top + 2.6;
                b[Mat.Tin].Strip(new List<Point3D> { P(24.0, y - 1.27, top + 0.2), P(24.0, y - 1.27, z), P(26.0, y - 1.27, z) }, 0.45, 0.45, V(0, 1, 0));
                b[Mat.Tin].Strip(new List<Point3D> { P(24.0, y + 1.27, top + 0.2), P(24.0, y + 1.27, z), P(26.0, y + 1.27, z) }, 0.45, 0.45, V(0, 1, 0));
                using (b.At(Mx.Ry(90) * Mx.T(26.0, y, z)))
                {
                    var pr = new List<Point> { new Point(0, 0), new Point(2.9, 0), new Point(2.9, 0), new Point(2.9, 1.0), new Point(2.5, 1.0), new Point(2.5, 1.0), new Point(2.5, 6.1) };
                    for (int i = 1; i <= 8; i++)
                    {
                        double a = Math.PI / 2 * i / 8;
                        pr.Add(new Point(2.5 * Math.Cos(a), 6.1 + 2.5 * Math.Sin(a)));
                    }

                    pr[pr.Count - 1] = new Point(0, 8.6);
                    if (dark) b[Mat.Plastic(0x101012u, 0.6)].Lathe(pr, 24);
                    else
                    {
                        b.Body(0x1A1A1Du, 0.4).Cylinder(2.75, 0.2, 5.0, 24);
                        b[Mat.Clear(0xEEF3F8u, 120, 0.9)].Lathe(pr, 24);
                    }
                }
            }

            b.Heat(P(30, yc, top + 2.6));
        }

        private static void Pir(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            Pcb(b, -2.5, yc - 12.0, 29.5, yc + 12.0, ModZ, 0x2D7A3Au, "pir", dc =>
            {
                PinLabels(dc, pins, names, 2.8, 0.85);
                SilkText(dc, "HC-SR501", 14.0, yc + 10.6, 0.9);
            }, 1.6, 0.6);
            b[Mat.WhitePlastic].BoxZ(14.0, yc, top, 23.0, 23.0, 1.6);
            using (b.At(14.0, yc, top + 1.6))
            {
                var pr = new List<Point> { new Point(11.4, 0) };
                for (int i = 1; i <= 16; i++)
                {
                    double a = Math.PI / 2 * i / 16;
                    double ripple = i % 2 == 0 ? 0 : 0.25;
                    pr.Add(new Point((11.4 - ripple) * Math.Cos(a), (11.4 - ripple) * Math.Sin(a)));
                }

                pr[pr.Count - 1] = new Point(0, 11.4);
                b.Body(Color.FromRgb(0xF2, 0xF0, 0xEA), 0.25).Lathe(pr, 14);
            }

            b.Heat(P(14, yc, top + 13));

            foreach (double dy in new[] { -8.5, -3.5 })
            {
                b[Mat.Plastic(0xE87E1Eu, 0.3)].BoxZ(27.6, yc + dy, top, 3.6, 4.0, 2.6);
                b[Mat.Plastic(0x303030u, 0)].Box(27.6, yc + dy, top + 2.62, 2.2, 0.4, 0.04);
            }
        }

        private static void SoilProbe(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            const double L = 88.0;
            Pcb(b, -3.0, yc - 11.5, L, yc + 11.5, ModZ, 0x141416u, "soil", dc =>
            {
                PinLabels(dc, pins, names, 2.8, 0.9);
                var white = Frozen(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xEA)));
                dc.DrawRectangle(white, null, new Rect(46, yc - 9.5, L - 47, 19));
                SilkText(dc, "Capacitive Soil Moisture Sensor v1.2", 30, yc + 9.6, 1.1);
                SilkText(dc, "↑ max depth", 44.0, yc - 8.5, 1.0);
            }, 1.6, 1.0);
            b[Mat.Plastic(0x141416u, 0.1)].Extrude(new[] { new Point(L - 0.01, yc - 11.5), new Point(L + 9.0, yc - 2.0), new Point(L + 9.0, yc + 2.0), new Point(L - 0.01, yc + 11.5) }, ModZ, ModZ + 1.6);
            Soic(b, 18.0, yc + 3.0, top, 8, 1.27, 3.9, 0, "NE555");
            Sot23Like(b, 10.0, yc - 5.0, top);
            b.Body(0x16161Au, 0.2).Box(18.0, yc + 3.0, top + 1.6, 4.0, 2.0, 0.05);
            b.Heat(P(18, yc, top + 1.6));
            SmdScatter(b, top, (24.0, yc - 4.0, 90, true), (26.5, yc - 4.0, 90, false), (24.0, yc + 7.0, 0, false));
        }

        private static void Joystick(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            Pcb(b, -2.5, yc - 13.0, 31.5, yc + 13.0, ModZ, 0x1A1A1Eu, "joystick", dc =>
            {
                PinLabels(dc, pins, names, 2.8, 0.85);
                SilkText(dc, "KY-023", 16, yc + 11.6, 0.9);
            }, 1.6, 0.8);
            double cx = 17.0;
            b.Body(0x202024u, 0.2).BoxZ(cx, yc, top, 16.0, 16.0, 9.0);
            b[Mat.Plastic(0x2152B8u, 0.3)].BoxZ(cx + 9.4, yc, top + 1.5, 2.8, 10.0, 6.0);
            b[Mat.Plastic(0x2152B8u, 0.3)].BoxZ(cx, yc - 9.4, top + 1.5, 10.0, 2.8, 6.0);
            b.Heat(P(cx, yc, top + 9));
            var stick = b.Rotating("stick", P(cx, yc, top + 9.0), V(0, 1, 0), P(cx, yc, top + 20));
            stick[Mat.Steel].CylinderZ(cx, yc, top + 9.0, top + 14.0, 1.6, 12);
            using (b.At(cx, yc, top + 14.0))
            {
                stick[Mat.Plastic(0x1A1A1Cu, 0.25)].Lathe(new[] { new Point(0, 0), new Point(6.0, 0), new Point(6.0, 0), new Point(9.0, 2.5), new Point(9.5, 4.5), new Point(9.0, 5.8), new Point(9.0, 5.8), new Point(6.5, 5.3), new Point(0, 5.5) }, 32);
            }
        }

        private static void Microphone(PartBuilder b, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            ComparatorBoard(b, pins, names, yc, top, 34.0, 15.0, "mic", "SOUND");
            using (b.At(28.0, yc, top))
            {
                b.Body(0xC4C8CEu, 0.8).Lathe(new[] { new Point(4.85, 0.4), new Point(4.85, 6.5), new Point(4.6, 6.8), new Point(4.2, 6.8) }, 28);
                b[Mat.Plastic(0x151517u, 0.05)].Cylinder(4.25, 6.4, 6.75, 28, false, true);
            }

            b.Heat(P(28, yc, top + 6.8));
        }

        private static void GenericSensor(PartBuilder b, ModelRequest r, List<(string Name, double Y)> pins, IReadOnlyList<string> names, double yc, double top)
        {
            double wid = Math.Max(15.0, pins.Count * 2.54 + 6.0);
            Pcb(b, -2.5, yc - wid / 2, 18.5, yc + wid / 2, ModZ, 0x1F55A8u, "sensor|" + pins.Count, dc =>
            {
                PinLabels(dc, pins, names, 2.8, 0.85);
                MountHole(dc, 15.5, yc, 3.0);
            }, 1.6, 0.6);
            Soic(b, 9.0, yc, top, 8, 1.27, 3.9, 90, PartMarking(r, 8));
            b.Body(0x16161Au, 0.2).Box(9.0, yc, top + 1.6, 3.0, 3.0, 0.05);
            b.Heat(P(9, yc, top + 1.6));
            SmdLed(b, 13.5, yc - 3.5, top, 0xE02020u, 90);
            SmdScatter(b, top, (13.5, yc + 3.0, 90, false), (5.0, yc + 4.5, 0, true));
        }
    }
}
