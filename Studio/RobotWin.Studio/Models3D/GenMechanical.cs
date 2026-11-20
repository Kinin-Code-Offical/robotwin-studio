using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Models3D
{
    internal static partial class Gen
    {
        // ================================================================== cables

        /// <summary>Flat ribbon of insulated wires along a path (offsets across the ribbon in local y).</summary>
        private static void Ribbon(PartBuilder b, IList<Point3D> centre, uint[] colours, double r, double pitch)
        {
            for (int i = 0; i < colours.Length; i++)
            {
                double off = (i - (colours.Length - 1) / 2.0) * pitch;
                var path = centre.Select(p => P(p.X, p.Y + off, p.Z)).ToList();
                b[Mat.Plastic(colours[i], 0.35, 30)].Tube(path, r, 10);
            }
        }

        /// <summary>Female Dupont housing (black, 2.54 mm) whose sockets face +x; returns socket centres.</summary>
        private static List<Point3D> DupontHousing(PartBuilder b, double x0, double cy, double cz, int n)
        {
            double w = n * 2.54;
            b[Mat.Plastic(0x18181Au, 0.12)].Block(x0, cy - w / 2, cz - 1.27, x0 + 14.0, cy + w / 2, cz + 1.27);
            var sockets = new List<Point3D>();
            for (int i = 0; i < n; i++)
            {
                double y = cy - w / 2 + 1.27 + i * 2.54;
                b[Mat.Plastic(0x050505u, 0)].Block(x0 + 14.0, y - 0.55, cz - 0.55, x0 + 14.02, y + 0.55, cz + 0.55);
                sockets.Add(P(x0 + 14.03, y, cz));
            }

            return sockets;
        }

        private static string PinOfPad(ModelRequest r, string pad) =>
            r.Def?.Pins.FirstOrDefault(d => string.Equals(r.Def.ResolvePad(d, r.P), pad, StringComparison.OrdinalIgnoreCase))?.Name ?? pad;

        private static string PadOfPin(ModelRequest r, string pin, string fallback)
        {
            var d = r.Def?.FindPin(pin);
            return d != null ? r.Def!.ResolvePad(d, r.P) : fallback;
        }

        // ================================================================== servos

        internal static void Servo(PartBuilder b, ModelRequest r)
        {
            string model = r.Model.ToUpperInvariant();
            bool big = model.Contains("996") || model.Contains("3218") || model.Contains("995") || model.Contains("STANDARD");
            bool metal = model.Contains("MG") || model.Contains("DS");
            uint caseRgb = big || metal ? 0x1D1D20u : 0x2F63C8u;
            double L = big ? 40.7 : 22.8, W = big ? 19.7 : 12.2, tabZ = big ? 26.9 : 15.9, tabT = 2.5, topZ = big ? 36.5 : 22.7;
            double span = big ? 54.0 : 32.3, sx = -L / 2 + (big ? 10.0 : 5.9), dome = big ? 7.0 : 5.9, domeH = big ? 3.0 : 4.0;
            var body = b.Body(caseRgb, 0.3);
            body.RoundedBox(0, 0, 0, L, W, tabZ, 0.8, 2);
            body.RoundedBox(0, 0, tabZ + tabT, L, W, topZ - tabZ - tabT, 0.8, 2);
            // Mounting tabs with their screw holes.
            foreach (int s in new[] { -1, 1 })
            {
                double xa = s < 0 ? -span / 2 : L / 2 - 0.1, xb = s < 0 ? -L / 2 + 0.1 : span / 2, hx = s * (span / 2 - (big ? 4.5 : 2.4));
                body.PlateWithHole(xa, -W / 2, xb, W / 2, tabZ, tabZ + tabT, hx, 0, big ? 2.2 : 1.0, 16);
            }

            body.Box(0, 0, tabZ + tabT / 2, L, W, tabT);
            using (b.At(sx, 0, topZ))
            {
                body.Cylinder(dome, 0, domeH, 28, false, true);
                if (!big) body.Cylinder(3.0, 0, domeH * 0.6, 18, false, true);
            }

            if (!big) body.CylinderZ(sx + 6.5, 0, topZ, topZ + 2.4, 3.0, 18, false, true);
            b.Heat(P(0, 0, topZ));
            var ink = big || metal ? Color.FromRgb(0xD8, 0xD8, 0xD8) : Color.FromRgb(0xF2, 0xF2, 0xF2);
            string name = r.Model.Length > 0 ? r.Model : "SG90";
            FrontLabel(b, name, P(2.0, W / 2 + 0.01, tabZ * 0.62), V(0, 1, 0), V(0, 0, 1), L * 0.55, tabZ * 0.26, ink, true);
            FrontLabel(b, big ? "High Torque" : "9g Micro Servo", P(2.0, W / 2 + 0.01, tabZ * 0.32), V(0, 1, 0), V(0, 0, 1), L * 0.6, tabZ * 0.16, ink);

            // Output spline and horn (rotates about the shaft).
            double hz = topZ + domeH;
            var horn = b.Rotating("horn", P(sx, 0, hz), V(0, 0, 1), P(sx, 0, hz + (big ? 6 : 4.5)));
            horn[metal ? Mat.Brass : Mat.WhitePlastic].CylinderZ(sx, 0, hz, hz + 2.6, big ? 2.9 : 2.4, 18);
            var hornMat = Mat.Plastic(0xF4F4F0u, 0.25);
            double hubR = big ? 5.0 : 3.5, hubH = big ? 4.5 : 3.8, armT = 1.6, az = hz + hubH - armT;
            horn[hornMat].CylinderZ(sx, 0, hz + 0.5, hz + hubH, hubR, 24);
            var arms = big ? new[] { 0.0, 90.0, 180.0, 270.0 } : new[] { 0.0 };
            double armLen = big ? 20.0 : 15.5;
            foreach (double a in arms)
            {
                using (b.At(Mx.Rz(a) * Mx.T(sx, 0, 0)))
                {
                    var outline = new List<Point> { new Point(0, -hubR * 0.85) };
                    for (int i = 0; i <= 8; i++)
                    {
                        double t = -Math.PI / 2 + Math.PI * i / 8;
                        outline.Add(new Point(armLen + 1.8 * Math.Cos(t), 1.8 * Math.Sin(t)));
                    }

                    outline.Add(new Point(0, hubR * 0.85));
                    horn[hornMat].Extrude(outline, az, az + armT, false);
                    for (int i = 0; i < (big ? 4 : 5); i++) horn[Mat.Plastic(0x2A2A2Cu, 0)].CylinderZ(armLen - i * 2.6, 0, az + armT, az + armT + 0.01, 0.55, 10, false, true);
                }
            }

            horn[Mat.Steel].CylinderZ(sx, 0, hz + hubH, hz + hubH + 0.4, 1.2, 12, false, true);

            // Cable: brown / red / orange ribbon to a Dupont plug.
            double cz = 4.5;
            var path = MeshBuilder.Smooth(new[] { P(L / 2 - 1.0, 0, cz), P(L / 2 + 8.0, 0, cz), P(L / 2 + 20.0, 0, 1.0), P(L / 2 + 60.0, 0, 1.3) }, 6.0, 8);
            var order = new[] { "GND", "V+", "SIG" };
            var colours = new[] { 0x6B4428u, 0xC8282Cu, 0xF08A1Eu };
            Ribbon(b, path, colours, 0.55, 1.15);
            var sockets = DupontHousing(b, L / 2 + 60.0, 0, 1.3, 3);
            for (int i = 0; i < 3; i++) b.Anchor(PadOfPin(r, order[i], (i + 1).ToString(CultureInfo.InvariantCulture)), sockets[i], V(1, 0, 0));
        }

        // ================================================================== DC motors

        internal static void Motor(PartBuilder b, ModelRequest r)
        {
            string m = r.Model.ToUpperInvariant();
            var a = r.Def?.FindPin("A") != null ? PadOfPin(r, "A", "1") : "1";
            var bb = r.Def?.FindPin("B") != null ? PadOfPin(r, "B", "2") : "2";
            if (m.Contains("KV") || m.Contains("BRUSHLESS") || m.Contains("2204") || m.Contains("2306")) Outrunner(b, a, bb);   // [esc]
            else if (m.Contains("CORELESS") || m.Contains("8520") || m.Contains("0720") || m.Contains("720"))
                Coreless(b, m.Contains("0720") || m.StartsWith("07", StringComparison.Ordinal) ? 7.0 : 8.5, a, bb);
            else if (m.Contains("N20")) N20(b, a, bb);
            else if (m.Contains("25GA") || m.Contains("JGA25") || m.Contains("370")) Gear370(b, m.Contains("JGA") || m.Contains("ENC"), a, bb);
            else if (m.Contains("TT")) TtMotor(b, a, bb);
            else if (m.Contains("130")) Can130(b, -13.75, 0, 7.5, a, bb, true);
            else TtMotor(b, a, bb);
        }

        /// <summary>130-size can (flattened Ø20, 27.5 long) along +x from x0, axis at height cz; terminals at the rear.</summary>
        private static void Can130(PartBuilder b, double x0, double cy, double cz, string padA, string padB, bool standalone)
        {
            using (b.At(Mx.Ry(90) * Mx.T(x0, cy, cz)))
            {
                var can = MeshBuilder.Circle(0, 0, 10.0, 40).Select(p => new Point(p.X, Math.Max(-7.5, Math.Min(7.5, p.Y)))).ToList();
                if (standalone) b.Body(0xC9CCD2u, 0.8).Extrude(can, 0, 25.0, true);
                else b[Mat.Steel].Extrude(can, 0, 25.0, true);
                b[Mat.Plastic(0xE8E6DEu, 0.2)].Extrude(MeshBuilder.Circle(0, 0, 9.6, 32).Select(p => new Point(p.X, Math.Max(-7.2, Math.Min(7.2, p.Y)))).ToList(), 25.0, 27.5, true);
                b[Mat.Brass].Cylinder(2.2, 27.5, 28.4, 14);
                b[Mat.Steel].Cylinder(2.6, -1.2, 0, 16);
                b[Mat.Steel].Cylinder(1.0, -9.0, -1.2, 10);
            }

            // Solder tabs with holes on the end cap.
            foreach (var (dy, pad) in new[] { (-5.2, padA), (5.2, padB) })
            {
                double x = x0 + 27.5 + 2.0;
                b[Mat.Brass].Block(x0 + 26.5, cy + dy - 1.0, cz - 3.4, x + 0.6, cy + dy + 1.0, cz - 3.0);
                b[Mat.Brass].Block(x - 0.2, cy + dy - 1.0, cz - 3.4, x + 0.3, cy + dy + 1.0, cz + 1.8);
                b.Anchor(pad, P(x + 0.3, cy + dy, cz + 1.0), V(1, 0, 0));
            }

            if (standalone) b.Heat(P(x0 + 12, cy, cz + 7.5));
        }

        private static void TtMotor(PartBuilder b, string padA, string padB)
        {
            // Yellow gearbox 37.5 × 18.8 × 22.4 mm with the double-flat output shaft 11.3 mm from its front.
            double gx0 = -34.0, gx1 = gx0 + 37.5, hw = 9.4, gh = 22.4, sx = gx0 + 11.3, sz = 11.2;
            var box = b.Body(0xF2C230u, 0.25);
            box.RoundedBox((gx0 + gx1) / 2, 0, 0, gx1 - gx0, 2 * hw, gh, 1.2, 2);
            box.BoxZ(gx0 + 4.0, 0, gh - 0.01, 6.0, 4.5, 2.4);
            b.Heat(P((gx0 + gx1) / 2, 0, gh));
            var dark = b[Mat.Plastic(0x6E5410u, 0.1)];
            foreach (double z in new[] { 3.6, 18.8 })
            {
                foreach (int s in new[] { -1, 1 })
                {
                    using (b.At(Mx.Rx(90) * Mx.T(gx0 + 25.5, s * (hw + 0.005), z))) dark.Cylinder(1.6, -0.01, 0.01, 14);
                }
            }

            b[Mat.Plastic(0xD9A91Eu, 0.2)].Box((gx0 + gx1) / 2, 0, gh / 2, gx1 - gx0 + 0.02, 2 * hw + 0.02, 0.5);
            // Motor can with the white strap; terminals at the far end.
            Can130(b, gx1, 0, sz, padA, padB, false);
            b[Mat.WhitePlastic].Block(gx1 + 9.0, -10.4, 0.6, gx1 + 13.0, 10.4, 21.8);
            // Output shaft through both sides (rotates about y).
            var shaft = b.Rotating("shaft", P(sx, 0, sz), V(0, 1, 0), P(sx, hw + 9.0, sz));
            using (b.At(Mx.Rx(-90) * Mx.T(sx, 0, sz)))
            {
                var dd = MeshBuilder.Circle(0, 0, 2.7, 24).Select(p => new Point(Math.Max(-1.85, Math.Min(1.85, p.X)), p.Y)).ToList();
                shaft[Mat.Plastic(0xF6F2E2u, 0.3)].Extrude(dd, -hw - 9.0, hw + 9.0, true);
                shaft[Mat.Plastic(0xF6F2E2u, 0.3)].Cylinder(4.2, -hw - 1.0, hw + 1.0, 24);
            }
        }

        private static void Gear370(PartBuilder b, bool encoder, string padA, string padB)
        {
            double cz = 12.5, gx0 = -36.0, gl = 21.0, ml = 30.8;
            using (b.At(Mx.Ry(90) * Mx.T(gx0, 0, cz)))
            {
                b.Body(0xC9A96Au, 0.8).Lathe(new[] { new Point(0, 0), new Point(12.0, 0), new Point(12.5, 0.6), new Point(12.5, gl), new Point(12.5, gl) }, 36);
                b[Mat.Steel].Cylinder(12.2, gl, gl + 0.8, 36);
                b[Mat.Nickel].Lathe(new[] { new Point(12.2, gl + 0.8), new Point(12.2, gl + ml - 0.8), new Point(11.6, gl + ml), new Point(0, gl + ml) }, 36);
                b[Mat.Plastic(0x1A1A1Cu, 0.15)].Cylinder(10.5, gl + ml, gl + ml + 3.2, 28);
                if (encoder)
                {
                    b[Mat.Plastic(0x2B2B2Eu, 0.3)].Cylinder(10.0, gl + ml + 5.0, gl + ml + 6.5, 28);
                    b[Mat.Plastic(0x1D6B3Au, 0.15)].Cylinder(12.0, gl + ml + 3.2, gl + ml + 4.8, 32);
                }

                // Front: bushing and D-shaft (rotates about x).
                b[Mat.Brass].Cylinder(3.5, -1.2, 0, 18);
            }

            b.Heat(P(gx0 + gl / 2, 0, cz + 12.5));
            foreach (int s in new[] { -1, 1 })
            {
                using (b.At(Mx.Ry(90) * Mx.T(gx0 - 0.01, s * 8.5, cz))) b[Mat.Plastic(0x101010u, 0)].Cylinder(1.4, -0.01, 0.0, 12, true, false);
            }

            var shaft = b.Rotating("shaft", P(gx0, 0, cz), V(-1, 0, 0), P(gx0 - 13.2, 0, cz));
            using (b.At(Mx.Ry(-90) * Mx.T(gx0 - 1.2, 0, cz)))
            {
                var d = MeshBuilder.Circle(0, 0, 2.0, 20).Select(p => new Point(Math.Min(1.5, p.X), p.Y)).ToList();
                shaft[Mat.Steel].Extrude(d, 0, 12.0, true);
            }

            double rear = gx0 + gl + ml + 3.2;
            foreach (var (dy, pad) in new[] { (-4.0, padA), (4.0, padB) })
            {
                b[Mat.Brass].Block(rear, dy - 1.0, cz + 4.0, rear + 3.0, dy + 1.0, cz + 4.4);
                b.Anchor(pad, P(rear + 3.0, dy, cz + 4.2), V(1, 0, 0));
            }
        }

        private static void N20(PartBuilder b, string padA, string padB)
        {
            double cz = 5.0;
            b.Body(0xC4C8CEu, 0.8).BoxZ(-9.5, 0, 0, 9.0, 12.0, 10.0);
            b[Mat.Brass].BoxZ(-9.5, 0, 0.3, 9.2, 10.0, 9.4);
            using (b.At(Mx.Ry(90) * Mx.T(-5.0, 0, cz)))
            {
                var can = MeshBuilder.Circle(0, 0, 6.0, 32).Select(p => new Point(Math.Max(-5.0, Math.Min(5.0, p.X)), p.Y)).ToList();
                b[Mat.Nickel].Extrude(can, 0, 15.0, true);
                b[Mat.Plastic(0x1A1A1Cu, 0.15)].Extrude(can, 15.0, 16.8, true);
            }

            b.Heat(P(-9.5, 0, 10));
            var shaft = b.Rotating("shaft", P(-14.0, 0, cz), V(-1, 0, 0), P(-24.0, 0, cz));
            using (b.At(Mx.Ry(-90) * Mx.T(-14.0, 0, cz)))
            {
                var d = MeshBuilder.Circle(0, 0, 1.5, 16).Select(p => new Point(Math.Min(1.0, p.X), p.Y)).ToList();
                shaft[Mat.Steel].Extrude(d, 0, 10.0, true);
            }

            foreach (var (dy, pad) in new[] { (-2.8, padA), (2.8, padB) })
            {
                b[Mat.Brass].Block(11.8, dy - 0.8, cz - 0.2, 14.2, dy + 0.8, cz + 0.2);
                b.Anchor(pad, P(14.2, dy, cz), V(1, 0, 0));
            }
        }

        /// <summary>[esc] 2204-class outrunner: Ø28 base with four M3 holes, Ø27 bell turning with the 5 mm prop shaft, three phase leads.</summary>
        private static void Outrunner(PartBuilder b, string padA, string padB)
        {
            const double baseH = 3.0, bellH = 17.0, top = baseH + bellH;
            b[Mat.Plastic(0x1A1A1Cu, 0.3)].Cylinder(14.0, 0, baseH, 36);
            b.Heat(P(0, 0, top * 0.6));
            var bell = b.Rotating("shaft", P(0, 0, top), V(0, 0, 1), P(0, 0, top + 6.0));
            bell[Mat.Plastic(0xC8282Cu, 0.6)].Lathe(new[] { new Point(13.5, baseH + 0.4), new Point(13.5, top - 1.0), new Point(12.5, top), new Point(3.0, top), new Point(3.0, top + 1.5), new Point(0, top + 1.5) }, 40);
            bell[Mat.Chrome].CylinderZ(0, 0, top + 1.5, top + 6.0, 2.5, 14);
            // Three phase leads (the ESC drives them; two are named for the averaged model).
            int k = 0;
            foreach (var (dy, colour, pad) in new[] { (-2.0, 0x1E1E22u, padA), (0.0, 0x1E1E22u, (string?)null), (2.0, 0x1E1E22u, padB) })
            {
                var path = MeshBuilder.Smooth(new[] { P(0, dy, 1.5), P(16.0, dy, 1.5), P(45.0, dy, 1.0) }, 2.0, 6);
                b[Mat.Plastic(colour, 0.35, 30)].Tube(path, 0.55, 8, true, false);
                var end = path[path.Count - 1];
                b[Mat.Copper].Rod(end, end + V(2.0, 0, 0), 0.35, 6);
                if (pad != null) b.Anchor(pad, end + V(2.0, 0, 0), V(1, 0, 0));
                k++;
            }
        }

        private static void Coreless(PartBuilder b, double dia, string padA, string padB)
        {
            double r = dia / 2, len = 20.0;
            using (b.At(0, 0, 0))
            {
                b[Mat.Plastic(0x1A1A1Cu, 0.2)].Cylinder(r - 0.1, 0, 1.6, 24);
                b.Body(0xD8DBE0u, 0.95).Lathe(new[] { new Point(r, 1.6), new Point(r, len - 0.6), new Point(r - 0.4, len), new Point(0, len) }, 32);
                b[Mat.Brass].Cylinder(1.3, len, len + 0.8, 14);
            }

            b.Heat(P(0, 0, len * 0.6));
            var shaft = b.Rotating("shaft", P(0, 0, len), V(0, 0, 1), P(0, 0, len + 5.5));
            shaft[Mat.Chrome].CylinderZ(0, 0, len + 0.8, len + 5.5, 0.5, 10);
            // Leads out of the bottom cap.
            foreach (var (dy, colour, pad) in new[] { (-1.2, 0xC8282Cu, padA), (1.2, 0x1E1E22u, padB) })
            {
                var path = MeshBuilder.Smooth(new[] { P(0, dy, 1.0), P(r + 1.5, dy, 1.0), P(r + 30.0, dy, 0.6) }, 1.5, 6);
                b[Mat.Plastic(colour, 0.35, 30)].Tube(path, 0.45, 8, true, false);
                var end = path[path.Count - 1];
                b[Mat.Copper].Rod(end, end + V(1.5, 0, 0), 0.25, 6);
                b.Anchor(pad, end + V(1.5, 0, 0), V(1, 0, 0));
            }
        }

        // ================================================================== batteries

        /// <summary>Print for a cell lying along x: u round the can (u = 0.25 faces up), v along the axis; text runs along the axis.</summary>
        private static BitmapSource CellPrint(uint bg, uint fg, string text, double aspect)
        {
            int h = 512, w = Math.Max(64, (int)(h * aspect));
            return Tex.Render(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(Mat.C(bg)), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new SolidColorBrush(Mat.C(Mix(bg, 0xFFFFFF, 0.25))), null, new Rect(0, 0, w, h * 0.12));
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var ft = Tex.Text(lines[i], w * (i == 0 ? 0.075 : 0.05), Mat.C(fg), i == 0);
                    double s = Math.Min(1, h * 0.7 / Math.Max(1, ft.Width));
                    dc.PushTransform(new MatrixTransform(0, -1, -1, 0, w * (0.25 + (i == 0 ? 0.045 : -0.04)), h * 0.55));
                    dc.PushTransform(new ScaleTransform(s, s));
                    dc.DrawText(ft, new Point(-ft.Width / 2, -ft.Height / 2));
                    dc.Pop();
                    dc.Pop();
                }
            });
        }

        private static BitmapSource WrapPrint(uint bg, uint fg, string text, double aspect, uint? band = null, bool mirror = false, double centerU = 0.5, double maxU = 0.4)
        {
            int h = 256, w = Math.Max(64, (int)(h * aspect));
            return Tex.Render(w, h, dc =>
            {
                if (mirror) dc.PushTransform(new ScaleTransform(-1, 1, w / 2.0, 0));
                dc.DrawRectangle(new SolidColorBrush(Mat.C(bg)), null, new Rect(0, 0, w, h));
                if (band.HasValue) dc.DrawRectangle(new SolidColorBrush(Mat.C(band.Value)), null, new Rect(0, 0, w, h * 0.22));
                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var ft = Tex.Text(lines[i], h * (i == 0 ? 0.16 : 0.1), Mat.C(fg), i == 0);
                    double s = Math.Min(1, w * maxU / Math.Max(1, ft.Width));
                    dc.PushTransform(new TranslateTransform(w * centerU, h * (0.42 + i * 0.2)));
                    dc.PushTransform(new ScaleTransform(s, s));
                    dc.DrawText(ft, new Point(-ft.Width / 2, -ft.Height / 2));
                    dc.Pop();
                    dc.Pop();
                }
            });
        }

        /// <summary>Cylindrical cell lying along x (centre at cx, cy, axis height = radius).</summary>
        private static void Cell(PartBuilder b, double cx, double cy, double dia, double len, uint wrap, uint ink, string text, bool body, string? padPlus = null, string? padMinus = null, double zAxis = double.NaN, bool flip = false)
        {
            double r = dia / 2, cz = double.IsNaN(zAxis) ? r : zAxis;
            var tex = Tex.Get($"cell|{wrap:X6}|{ink:X6}|{text}|{F(dia)}x{F(len)}", () => CellPrint(wrap, ink, text, Math.PI * dia / len));
            using (b.At(Mx.Ry(flip ? -90 : 90) * Mx.T(cx + (flip ? len / 2 : -len / 2), cy, cz)))
            {
                var mb = body ? b.Body(tex, Mat.C(wrap), 0.4) : b[Mat.Image($"cellmat|{wrap:X6}|{text}|{F(dia)}", () => tex, 0.4)];
                mb.Lathe(new[] { new Point(r - 0.5, 0.15), new Point(r, 0.6), new Point(r, len - 0.6), new Point(r - 0.5, len - 0.15) }, 36, Math.PI / 2, 2.5 * Math.PI);
                b[Mat.Nickel].Cylinder(r - 0.5, 0.05, 0.2, 28, true, false);
                b[Mat.Nickel].Cylinder(r - 0.5, len - 0.2, len - 0.05, 28, false, true);
                b[Mat.Nickel].Cylinder(dia > 16 ? 3.5 : 2.6, len - 0.1, len + (dia > 16 ? 0.6 : 1.1), 20);
            }

            double sign = flip ? -1 : 1;
            if (padPlus != null) b.Anchor(padPlus, P(cx + sign * (len / 2 + 1.1), cy, cz), V(sign, 0, 0));
            if (padMinus != null) b.Anchor(padMinus, P(cx - sign * (len / 2 + 0.05), cy, cz), V(-sign, 0, 0));
        }

        private static void Leads(PartBuilder b, Point3D from, Vector3D dir, double length, string padPlus, string padMinus, double r = 0.7, double gap = 2.0)
        {
            var side = Vector3D.CrossProduct(dir, V(0, 0, 1));
            if (side.LengthSquared < 1e-9) side = V(1, 0, 0);
            side.Normalize();
            foreach (var (s, colour, pad) in new[] { (-0.5, 0xC8282Cu, padPlus), (0.5, 0x1E1E22u, padMinus) })
            {
                var start = from + side * (s * gap);
                var path = MeshBuilder.Smooth(new[] { start, start + dir * (length * 0.4) + V(0, 0, 0.5), start + dir * length + side * (s * gap * 0.5) }, length * 0.2, 6);
                b[Mat.Plastic(colour, 0.35, 30)].Tube(path, r, 10, true, false);
                var end = path[path.Count - 1];
                b[Mat.Tin].Rod(end, end + dir * 2.0, r * 0.55, 8);
                b.Anchor(pad, end + dir * 2.0, dir);
            }
        }

        internal static void Battery(PartBuilder b, ModelRequest r)
        {
            string chem = r.Text("chemistry", "Alkaline");
            string model = r.Model.ToUpperInvariant();
            int cells = Math.Max(1, (int)Math.Round(r.Num("cells", 1)));
            double cap = r.Num("capacity", 2.5);
            string plus = PadOfPin(r, "+", "1"), minus = PadOfPin(r, "-", "2");
            if (chem == "LithiumCoin" || model.Contains("CR2032"))
            {
                using (b.At(0, 0, 0))
                {
                    b.Body(0xC9CCD2u, 0.9).Lathe(new[] { new Point(0, 0), new Point(9.6, 0), new Point(9.6, 0), new Point(10.0, 0.4), new Point(10.0, 3.0), new Point(9.7, 3.2), new Point(0, 3.2) }, 40);
                }

                TopLabel(b, "CR2032\n3V  +", 0, 0, 3.21, 12, 6, Color.FromRgb(0x30, 0x30, 0x34), 0, true);
                b.Heat(P(0, 0, 3.2));
                b.Anchor(plus, P(0, 0, 3.2), V(0, 0, 1));
                b.Anchor(minus, P(0, 0, 0), V(0, 0, -1));
                return;
            }

            if (model.StartsWith("9V", StringComparison.Ordinal))
            {
                var tex = Tex.Get("9v-wrap", () => WrapPrint(0x1A1A1Cu, 0xE2B54Au, "9V\nALKALINE", 2 * (26.5 + 17.5) / 46.4, 0xE2B54Au, true, 0.75, 0.24));
                b.Body(tex, Mat.C(0x1A1A1Cu), 0.35).RoundedBox(0, 0, 0, 26.5, 17.5, 46.4, 1.6, 3);
                b[Mat.Steel].RoundedBox(0, 0, 46.4, 26.5, 17.5, 0.6, 1.6, 3);
                b[Mat.Nickel].CylinderZ(-6.35, 0, 47.0, 49.6, 2.9, 20);
                b[Mat.Nickel].Extrude(MeshBuilder.Circle(6.35, 0, 4.0, 6), 47.0, 49.4);
                // Snap clip with the leads.
                b[Mat.Plastic(0x1A1A1Cu, 0.2)].RoundedBox(0, 0, 49.6, 26.0, 16.0, 4.6, 2.0, 3);
                Leads(b, P(12.5, 0, 51.9), V(1, 0, 0), 30, plus, minus, 0.6, 2.4);
                b.Heat(P(0, 0, 45));
                return;
            }

            if (chem == "LeadAcid")
            {
                b.Body(0x1C1C1Eu, 0.2).RoundedBox(0, 0, 0, 151, 65, 89, 2.0, 2);
                b[Mat.Plastic(0x5A5C62u, 0.2)].RoundedBox(0, 0, 89, 151, 65, 5, 2.0, 2);
                FrontLabel(b, "12V  7Ah", P(0, 32.51, 50), V(0, 1, 0), V(0, 0, 1), 90, 20, Color.FromRgb(0xF0, 0xF0, 0xF0), true);
                FrontLabel(b, "SEALED LEAD-ACID BATTERY", P(0, 32.51, 32), V(0, 1, 0), V(0, 0, 1), 110, 8, Color.FromRgb(0xE2, 0xB5, 0x4A));
                foreach (var (x, colour, pad) in new[] { (-60.0, 0xC8282Cu, plus), (60.0, 0x1E1E22u, minus) })
                {
                    b[Mat.Plastic(colour, 0.3)].BoxZ(x, 20, 94, 12, 10, 3);
                    b[Mat.Tin].BoxZ(x, 20, 94, 6.3, 0.8, 9);
                    b.Anchor(pad, P(x, 20, 103), V(0, 0, 1));
                }

                b.Heat(P(0, 0, 94));
                return;
            }

            bool lipo = chem == "LithiumIon" && !model.Contains("18650");
            if (lipo)
            {
                bool micro = cap < 1.0 && cells == 1;
                // Pack size follows the energy: a 2S 2200 mAh pack is 105 × 34 × 16 mm, a 2S 450 mAh one ≈ 62 × 20 × 10 mm.
                double k = micro ? 1 : Math.Max(0.45, Math.Min(2.0, Math.Cbrt(cap * cells / 4.4)));
                double L = micro ? 40 : 105 * k, W = micro ? 19 : 34 * k, H = micro ? 7 : (6.8 * cells + 2.6) * k;
                string label = micro ? $"{cap * 1000:0}mAh\n1S 3.7V LiPo" : $"{cap * 1000:0}mAh {cells}S1P\n{cells * 3.7:0.0}V  25C  LiPo";
                using (b.At(Mx.Ry(90) * Mx.T(-L / 2, 0, H / 2)))
                {
                    b.Body(micro ? 0xE6C22Au : 0x2A4C8Cu, 0.5).Extrude(MeshBuilder.RoundedRect(0, 0, H, W, Math.Min(H, W) * 0.3, 4), 0, L, true, false, false);
                }

                TopLabel(b, label, 0, 0, H + 0.01, L * 0.7, W * 0.55, micro ? Color.FromRgb(0x1A, 0x1A, 0x1C) : Color.FromRgb(0xF0, 0xF0, 0xF0), 0, true);

                b[Mat.Plastic(micro ? 0xD8B424u : 0x22406Fu, 0.4)].RoundedBox(-L / 2, 0, 0.2, 0.6, W - 1.0, H - 0.4, 0.6, 2);
                b[Mat.Plastic(micro ? 0xD8B424u : 0x22406Fu, 0.4)].RoundedBox(L / 2, 0, 0.2, 0.6, W - 1.0, H - 0.4, 0.6, 2);
                b.Heat(P(0, 0, H));
                if (micro)
                {
                    Leads(b, P(L / 2, 0, H / 2), V(1, 0, 0), 25, plus, minus, 0.45, 1.6);
                    b[Mat.WhitePlastic].Block(L / 2 + 25, -2.6, H / 2 - 1.6, L / 2 + 31, 2.6, H / 2 + 1.6);
                    return;
                }

                // Main leads to an XT60, balance lead to a JST-XH.
                Leads(b, P(L / 2, -6, H / 2), V(1, 0, 0), 45, plus, minus, 1.6, 4.0);
                b[Mat.Plastic(0xF2C21Cu, 0.35)].Block(L / 2 + 47.0, -14.0, H / 2 - 4.2, L / 2 + 63.0, 2.0, H / 2 + 4.2);
                var colours = new List<uint> { 0x1E1E22u };
                for (int i = 0; i < cells; i++) colours.Add(0xC8282Cu);
                var bal = MeshBuilder.Smooth(new[] { P(L / 2, 8, H / 2), P(L / 2 + 15, 9, H / 2 + 1), P(L / 2 + 35, 12, H / 2) }, 6, 6);
                Ribbon(b, bal, colours.ToArray(), 0.35, 0.75);
                b[Mat.WhitePlastic].Block(L / 2 + 35, 12 - colours.Count * 1.25, H / 2 - 2.4, L / 2 + 41, 12 + colours.Count * 1.25, H / 2 + 2.4);
                return;
            }

            if (chem == "LithiumIon" || model.Contains("18650") || chem == "LiFePO4")
            {
                bool big = chem == "LiFePO4";
                double dia = big ? 32.0 : 18.6, len = big ? 70.0 : 65.2;
                string text = big ? "LiFePO4 32700\n3.2V 6000mAh" : "18650 Li-ion\n3.7V 3000mAh";
                int n = Math.Max(1, big ? cells : 1);
                for (int i = 0; i < n; i++)
                    Cell(b, 0, (i - (n - 1) / 2.0) * (dia + 0.2), dia, len, big ? 0x2F8F5Au : 0x2D6FB0u, 0xF0F0F0u, text, i == 0, n == 1 ? plus : null, n == 1 ? minus : null, double.NaN, i % 2 == 1);
                if (n > 1)
                {
                    b[Mat.Clear(0x2F6CF2u, 70, 0.6)].RoundedBox(0, 0, -0.3, len + 2.0, n * (dia + 0.2) + 1.0, dia + 0.6, dia / 2, 4);
                    Leads(b, P(len / 2 + 1, 0, dia / 2), V(1, 0, 0), 35, plus, minus, 1.0, 3.0);
                }

                b.Heat(P(0, 0, dia));
                return;
            }

            // AA / AAA alkaline or NiMH: a loose cell or a holder with leads.
            bool nimh = chem == "NiMH";
            uint wrapRgb = nimh ? 0x2F8F5Au : 0x2D3E8Cu;
            string cellText = nimh ? "NiMH  AA\n1.2V 2000mAh" : "AA  1.5V\nALKALINE  LR6";
            if (cells == 1 && !model.Contains("×") && !model.Contains("X"))
            {
                Cell(b, 0, 0, 14.5, 50.5, wrapRgb, 0xF2F2F2u, cellText, true, plus, minus);
                b.Heat(P(0, 0, 14.5));
                return;
            }

            int nc = Math.Max(1, cells);
            double hw = nc * 14.8 + 2.4, hl = 57.5, hh = 15.5;
            var holder = b.Body(0x18181Au, 0.15);
            holder.BoxZ(0, 0, 0, hl, hw, 1.4);
            holder.BoxZ(0, -hw / 2 + 0.7, 0, hl, 1.4, hh * 0.75);
            holder.BoxZ(0, hw / 2 - 0.7, 0, hl, 1.4, hh * 0.75);
            holder.BoxZ(-hl / 2 + 1.0, 0, 0, 2.0, hw, hh);
            holder.BoxZ(hl / 2 - 1.0, 0, 0, 2.0, hw, hh);
            for (int i = 0; i < nc; i++)
                Cell(b, 0, -hw / 2 + 1.2 + 7.4 + i * 14.8, 14.5, 50.5, wrapRgb, 0xF2F2F2u, cellText, false, null, null, 8.6, i % 2 == 1);
            Leads(b, P(hl / 2, 0, 4.0), V(1, 0, 0), 35, plus, minus, 0.7, 2.6);
            b.Heat(P(0, 0, hh));
        }

        // ================================================================== lamp, instruments

        internal static void Lamp(PartBuilder b, ModelRequest r)
        {
            string a = PadOfPin(r, "A", "1"), c = PadOfPin(r, "B", "2");
            // Bakelite holder with two screw terminals, E10 bulb.
            b.Body(0x2A1C14u, 0.25).CylinderZ(0, 0, 0, 12, 9.0, 32);
            foreach (var (x, pad) in new[] { (-10.5, a), (10.5, c) })
            {
                b[Mat.Brass].BoxZ(x, 0, 0, 5, 7, 1.2);
                b[Mat.Steel].CylinderZ(x, 0, 1.2, 2.6, 1.6, 14);
                b.Anchor(pad, P(x, 0, 2.6), V(0, 0, 1));
            }

            using (b.At(0, 0, 12))
            {
                var thread = new List<Point>();
                for (int i = 0; i <= 12; i++) thread.Add(new Point(i % 2 == 0 ? 5.0 : 4.6, i * 0.75));
                b[Mat.Nickel].Lathe(thread, 28);
                var glass = new List<Point> { new Point(4.2, 9.0), new Point(4.0, 10.5) };
                for (int i = 0; i <= 14; i++)
                {
                    double t = -0.35 + (Math.PI / 2 + 0.35) * i / 14;
                    glass.Add(new Point(5.6 * Math.Cos(t), 16.5 + 5.6 * Math.Sin(t)));
                }

                glass[glass.Count - 1] = new Point(0, 22.1);
                b[Mat.Plastic(0x6A6A6Eu, 0.6)].Rod(P(-1.2, 0, 9.5), P(-1.5, 0, 15.5), 0.12, 6);
                b[Mat.Plastic(0x6A6A6Eu, 0.6)].Rod(P(1.2, 0, 9.5), P(1.5, 0, 15.5), 0.12, 6);
                var coil = new List<Point3D>();
                for (int i = 0; i <= 60; i++)
                {
                    double t = i / 60.0, ang = t * Math.PI * 12;
                    coil.Add(P(-1.5 + 3.0 * t, 0.3 * Math.Sin(ang), 15.5 + 0.3 * Math.Cos(ang)));
                }

                b[Mat.Plastic(0x4A4A4Eu, 0.6)].Tube(coil, 0.06, 4, false, false);
                b.Lens(Mat.C(0xFFF6E0u, 90), Color.FromRgb(255, 214, 140)).Lathe(glass, 28);
            }

            b.Heat(P(0, 0, 34));
        }

        internal static void Instrument(PartBuilder b, ModelRequest r)
        {
            string title = r.Id switch
            {
                "ammeter" => "A",
                "voltmeter" => "V",
                "signal-generator" => "~ " + r.Text("frequency", "1k") + "Hz",
                "current-source" => r.Text("current", "10m") + "A",
                _ => r.Text("voltage", "5") + " V"
            };
            b.Body(0x2A2C31u, 0.2).RoundedBox(0, 0, 0, 60, 40, 28, 2.5, 3);
            b[Mat.Plastic(0x0E1A10u, 0.5)].BoxZ(-8, 0, 28, 30, 18, 0.3);
            TopLabel(b, title, -8, 0, 28.31, 26, 12, Color.FromRgb(0x7C, 0xF2, 0x8C), 0, true, "Consolas");
            var pins = r.Def?.Pins ?? new List<PinDefinition>();
            uint[] post = { 0xC8282Cu, 0x1E1E22u };
            for (int i = 0; i < Math.Min(2, Math.Max(2, pins.Count)); i++)
            {
                double y = i == 0 ? -9 : 9;
                b[Mat.Plastic(post[i], 0.4)].CylinderZ(20, y, 28, 34, 3.2, 20);
                b[Mat.Brass].CylinderZ(20, y, 34, 36, 1.2, 12);
                string pad = i < pins.Count ? r.Def!.ResolvePad(pins[i], r.P) : (i + 1).ToString(CultureInfo.InvariantCulture);
                b.Anchor(pad, P(20, y, 36), V(0, 0, 1));
            }

            b.Heat(P(0, 0, 28));
        }

        // ================================================================== wheels and propellers

        internal static void Wheel(PartBuilder b, double diameter, double width, string style)
        {
            double R = diameter / 2, ri = R - Math.Max(4.0, diameter * 0.11), w = width;
            using (b.At(Mx.Rx(-90)))
            {
                // Rubber tyre with tread blocks.
                b[Mat.Rubber].Lathe(new[]
                {
                    new Point(ri, -w / 2), new Point(R - 2.0, -w / 2), new Point(R - 0.4, -w / 2 + 0.8), new Point(R, -w / 2 + 2.2),
                    new Point(R, w / 2 - 2.2), new Point(R - 0.4, w / 2 - 0.8), new Point(R - 2.0, w / 2), new Point(ri, w / 2),
                    new Point(ri, w / 2), new Point(ri, -w / 2)
                }, 48);
                int treads = Math.Max(16, (int)(diameter / 2.6));
                var tread = b[Mat.Plastic(0x202022u, 0.05)];
                for (int i = 0; i < treads; i++)
                {
                    using (b.At(Mx.Rz(360.0 * i / treads)))
                    {
                        tread.Box(R + 0.45, 0, -w / 4 - 0.6, 1.0, 2.2, w / 2 - 3.0);
                        using (b.At(Mx.Rz(180.0 / treads))) tread.Box(R + 0.45, 0, w / 4 + 0.6, 1.0, 2.2, w / 2 - 3.0);
                    }
                }

                // Yellow TT hub: rim, spokes and the D-bore boss.
                var hub = b[Mat.Plastic(style == "black" ? 0x2A2A2Eu : 0xF2C230u, 0.25)];
                hub.Lathe(new[] { new Point(ri - 2.4, -w / 2 + 1.5), new Point(ri + 0.1, -w / 2 + 1.5), new Point(ri + 0.1, -w / 2 + 1.5), new Point(ri + 0.1, w / 2 - 1.5), new Point(ri + 0.1, w / 2 - 1.5), new Point(ri - 2.4, w / 2 - 1.5), new Point(ri - 2.4, w / 2 - 1.5), new Point(ri - 2.4, -w / 2 + 1.5) }, 40);
                hub.Cylinder(6.0, -w / 2 + 1.0, w / 2 - 1.0, 24);
                for (int i = 0; i < 6; i++)
                {
                    using (b.At(Mx.Rz(60.0 * i))) hub.Box((6.0 + ri - 2.4) / 2, 0, 0, ri - 2.4 - 6.0 + 0.4, 3.2, w * 0.45);
                }

                hub.Cylinder(ri - 2.4, -0.8, 0.8, 40);
                var dd = MeshBuilder.Circle(0, 0, 2.75, 16).Select(p => new Point(Math.Max(-1.9, Math.Min(1.9, p.X)), p.Y)).ToList();
                b[Mat.Plastic(0x101010u, 0)].Extrude(dd, w / 2 - 0.99, w / 2 - 0.98, false, true, false);
                b[Mat.Plastic(0x101010u, 0)].Extrude(dd, -w / 2 + 0.98, -w / 2 + 0.99, false, false, true);
            }
        }

        internal static void Propeller(PartBuilder b, double diameter, int blades, uint rgb)
        {
            double R = diameter / 2;
            var mat = Mat.Plastic(rgb, 0.45, 40);
            b[mat].CylinderZ(0, 0, -2.0, 2.0, 2.6, 20);
            b[mat].CylinderZ(0, 0, 2.0, 2.6, 1.6, 16, false, true);
            int sections = 9;
            for (int k = 0; k < Math.Max(2, blades); k++)
            {
                using (b.At(Mx.Rz(360.0 * k / Math.Max(2, blades))))
                {
                    Point3D[] Section(int i)
                    {
                        double t = (double)i / (sections - 1), rr = 2.2 + t * (R - 2.2);
                        double chord = Math.Max(1.6, (R * 0.22) * Math.Sin(Math.PI * (0.18 + 0.82 * t)) + (1 - t) * 1.6);
                        double pitchDeg = 24 - 14 * t, th = 0.9 - 0.45 * t;
                        double c = Math.Cos(pitchDeg * Math.PI / 180), s = Math.Sin(pitchDeg * Math.PI / 180);
                        double le = chord * 0.4, te = -chord * 0.6;
                        return new[]
                        {
                            P(rr, te * c, te * s - th / 2), P(rr, le * c, le * s - th / 2), P(rr, le * c, le * s + th / 2), P(rr, te * c, te * s + th / 2)
                        };
                    }

                    for (int i = 0; i + 1 < sections; i++)
                    {
                        var a = Section(i);
                        var c2 = Section(i + 1);
                        // Sections run along x: build the hexahedron with x as the "height".
                        b[mat].Hexa(a[0], a[1], a[2], a[3], c2[0], c2[1], c2[2], c2[3], i + 2 == sections, i == 0);
                    }
                }
            }
        }
    }
}
