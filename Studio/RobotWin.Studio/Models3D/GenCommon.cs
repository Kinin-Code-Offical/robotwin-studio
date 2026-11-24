using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// Model generators. All coordinates are footprint millimetres (x right, y down the
    /// board, z up from the board surface); dimensions follow the package datasheets.
    /// </summary>
    internal static partial class Gen
    {
        private const double Tail = ComponentModels.LeadTail;
        private static readonly Color Marking = Color.FromRgb(0xD6, 0xD6, 0xD2);
        private static readonly Color Silk = Color.FromRgb(0xF4, 0xF4, 0xF0);

        private static Point3D P(double x, double y, double z) => new Point3D(x, y, z);
        private static Vector3D V(double x, double y, double z) => new Vector3D(x, y, z);
        private static string F(double v, string fmt = "0.##") => v.ToString(fmt, CultureInfo.InvariantCulture);

        // ================================================================== leads and pins

        /// <summary>Round tinned lead from <paramref name="zTop"/> straight down through the board.</summary>
        private static void LeadDown(PartBuilder b, double x, double y, double zTop, double r = 0.3, Material? m = null) =>
            b[m ?? Mat.Tin].Rod(P(x, y, Tail), P(x, y, zTop), r, 8);

        /// <summary>Square pin (header contact) with chamfered tips.</summary>
        private static void SquarePin(PartBuilder b, double x, double y, double z0, double z1, double s = 0.64, Material? m = null)
        {
            var mb = b[m ?? Mat.Gold];
            double h = s / 2, t = Math.Min(0.35, (z1 - z0) / 4), k = s * 0.22;
            mb.Box(x, y, (z0 + t + z1 - t) / 2, s, s, z1 - z0 - 2 * t);
            mb.Hexa(P(x - h, y - h, z1 - t), P(x + h, y - h, z1 - t), P(x + h, y + h, z1 - t), P(x - h, y + h, z1 - t),
                P(x - k, y - k, z1), P(x + k, y - k, z1), P(x + k, y + k, z1), P(x - k, y + k, z1), true, false);
            mb.Hexa(P(x - k, y - k, z0), P(x + k, y - k, z0), P(x + k, y + k, z0), P(x - k, y + k, z0),
                P(x - h, y - h, z0 + t), P(x + h, y - h, z0 + t), P(x + h, y + h, z0 + t), P(x - h, y + h, z0 + t), false, true);
        }

        /// <summary>Flat formed lead (DIP / TO packages): wide shoulder, narrow tip through the board.</summary>
        private static void FlatLead(PartBuilder b, double x, double y, double zBody, double zShoulder, double wide, double narrow, double thick, Material? m = null)
        {
            var mb = b[m ?? Mat.Tin];
            mb.Box(x, y, (zBody + zShoulder) / 2, wide, thick, zBody - zShoulder);
            mb.Tapered(x, y, zShoulder - 0.6, wide, thick, 0.6, (wide - narrow) / 2, 0);
            mb.Box(x, y, (Tail + zShoulder - 0.6) / 2, narrow, thick, zShoulder - 0.6 - Tail);
        }

        // ================================================================== labels

        private static void TopLabel(PartBuilder b, string text, double cx, double cy, double z, double w, double h, Color fg, double rot = 0, bool bold = false, string font = "Segoe UI")
        {
            if (string.IsNullOrWhiteSpace(text) || w <= 0 || h <= 0) return;
            b[Tex.LabelMat(text, w / h, fg, Colors.Transparent, bold, font)].TopQuad(cx, cy, z, w, h, rot);
        }

        private static void FrontLabel(PartBuilder b, string text, Point3D c, Vector3D n, Vector3D up, double w, double h, Color fg, bool bold = false)
        {
            if (string.IsNullOrWhiteSpace(text) || w <= 0 || h <= 0) return;
            b[Tex.LabelMat(text, w / h, fg, Colors.Transparent, bold)].FrontQuad(c, n, up, w, h);
        }

        /// <summary>Short marking text for a package (part number without the "(1/2)" style suffixes).</summary>
        private static string PartMarking(ModelRequest r, int max = 10)
        {
            string m = r.Model;
            int paren = m.IndexOf('(');
            if (paren > 0) m = m.Substring(0, paren);
            m = m.Trim();
            if (m.Length == 0) m = r.Value;
            if (m.Length > max) m = m.Substring(0, max);
            return m;
        }

        // ================================================================== printed circuit boards

        /// <summary>
        /// Module PCB: FR4 slab between z0 and z0 + t, mask colour on the edges and a printed
        /// top texture drawn in footprint millimetres (<paramref name="draw"/> gets a context scaled to mm).
        /// </summary>
        private static void Pcb(PartBuilder b, double x0, double y0, double x1, double y1, double z0, uint mask, string texKey,
            Action<DrawingContext>? draw, double t = 1.6, double corner = 0.8, double ppmm = 24)
        {
            double w = x1 - x0, h = y1 - y0;
            var edge = Mat.Plastic(Mix(mask, 0x1E1E1Eu, 0.35), 0.1);
            b[edge].RoundedBox((x0 + x1) / 2, (y0 + y1) / 2, z0, w, h, t, corner, 3, false, true);
            double scale = Math.Min(ppmm, 4096 / Math.Max(w, h));   // sharp up close: silkscreen and part markings stay legible
            int pw = Math.Max(8, (int)(w * scale)), ph = Math.Max(8, (int)(h * scale));
            var tex = Mat.Image("pcb|" + texKey + "|" + F(w) + "x" + F(h), () => Tex.Render(pw, ph, dc =>
            {
                dc.PushTransform(new ScaleTransform(pw / w, ph / h));
                dc.PushTransform(new TranslateTransform(-x0, -y0));
                var maskBrush = new SolidColorBrush(Mat.C(mask));
                dc.DrawRoundedRectangle(maskBrush, null, new Rect(x0, y0, w, h), corner, corner);
                draw?.Invoke(dc);
                dc.Pop();
                dc.Pop();
            }), 0.18);
            b[tex].Extrude(MeshBuilder.RoundedRect((x0 + x1) / 2, (y0 + y1) / 2, w, h, corner, 3), z0 + t - 0.001, z0 + t, false, true, false);
        }

        private static uint Mix(uint a, uint c, double k)
        {
            byte M(int s) => (byte)(((a >> s) & 0xFF) * (1 - k) + ((c >> s) & 0xFF) * k);
            return (uint)(M(16) << 16 | M(8) << 8 | M(0));
        }

        // Drawing helpers for PCB textures (millimetre units).
        private static Brush SilkBrush => Frozen(new SolidColorBrush(Silk));
        private static Brush Gold => Frozen(new SolidColorBrush(Color.FromRgb(0xD8, 0xB0, 0x5A)));

        private static T Frozen<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        private static void SilkText(DrawingContext dc, string text, double x, double y, double size, bool centre = true, double angle = 0, bool bold = false, Brush? brush = null)
        {
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.SemiBold, FontStretches.Normal), size, brush ?? SilkBrush, 1.0);
            dc.PushTransform(new TranslateTransform(x, y));
            if (angle != 0) dc.PushTransform(new RotateTransform(angle));
            dc.DrawText(ft, centre ? new Point(-ft.Width / 2, -ft.Height / 2) : new Point(0, -ft.Height / 2));
            if (angle != 0) dc.Pop();
            dc.Pop();
        }

        private static void PadRing(DrawingContext dc, double x, double y, double outer = 0.85, double hole = 0.5, bool square = false)
        {
            if (square) dc.DrawRectangle(Gold, null, new Rect(x - outer, y - outer, outer * 2, outer * 2));
            else dc.DrawEllipse(Gold, null, new Point(x, y), outer, outer);
            dc.DrawEllipse(Brushes.Black, null, new Point(x, y), hole, hole);
        }

        private static void MountHole(DrawingContext dc, double x, double y, double d = 3.2)
        {
            dc.DrawEllipse(Frozen(new SolidColorBrush(Color.FromRgb(0xC9, 0xCC, 0xD1))), null, new Point(x, y), d / 2 + 0.9, d / 2 + 0.9);
            dc.DrawEllipse(Frozen(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10))), null, new Point(x, y), d / 2, d / 2);
        }

        private static void Trace(DrawingContext dc, uint mask, double width, params Point[] pts)
        {
            var pen = new Pen(new SolidColorBrush(Mat.C(Mix(mask, 0xFFFFFFu, 0.12))), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            for (int i = 0; i + 1 < pts.Length; i++) dc.DrawLine(pen, pts[i], pts[i + 1]);
        }

        // ================================================================== small parts used on modules

        /// <summary>Row of female header sockets (black, 8.5 mm) over the given pin positions.</summary>
        private static void FemaleHeaderPins(PartBuilder b, IEnumerable<(double X, double Y)> pins, double z0, double height = 8.5)
        {
            var body = b[Mat.Plastic(0x1C1C1Fu, 0.08)];
            var hole = b[Mat.Plastic(0x050505u, 0)];
            foreach (var (x, y) in pins)
            {
                body.Tapered(x, y, z0, 2.5, 2.5, height, 0.05);
                hole.Tapered(x, y, z0 + height, 1.15, 1.15, 0.01, 0.0);
                b[Mat.Plastic(0x050505u, 0)].Hexa(P(x - 0.55, y - 0.55, z0 + height - 0.7), P(x + 0.55, y - 0.55, z0 + height - 0.7), P(x + 0.55, y + 0.55, z0 + height - 0.7), P(x - 0.55, y + 0.55, z0 + height - 0.7),
                    P(x - 0.62, y - 0.62, z0 + height + 0.005), P(x + 0.62, y - 0.62, z0 + height + 0.005), P(x + 0.62, y + 0.62, z0 + height + 0.005), P(x - 0.62, y + 0.62, z0 + height + 0.005));
            }
        }

        /// <summary>Male header: plastic spacer blocks and gold pins.</summary>
        private static void MaleHeaderPins(PartBuilder b, IEnumerable<(double X, double Y)> pins, double zPlastic, double zTop, double zBottom, double plasticH = 2.5)
        {
            var body = b[Mat.Plastic(0x1C1C1Fu, 0.08)];
            foreach (var (x, y) in pins)
            {
                body.Tapered(x, y, zPlastic, 2.5, 2.5, plasticH, 0.12);
                SquarePin(b, x, y, zBottom, zTop);
            }
        }

        private static void SmdChip(PartBuilder b, double cx, double cy, double z0, double len, double wid, double h, uint body, double rot = 0, string? marking = null)
        {
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, z0)))
            {
                double cap = Math.Min(0.5, len * 0.2);
                b[Mat.Plastic(body, 0.2)].Box(0, 0, h / 2, len - 2 * cap + 0.02, wid, h);
                b[Mat.Tin].Box(-len / 2 + cap / 2, 0, h / 2, cap, wid + 0.02, h + 0.02);
                b[Mat.Tin].Box(len / 2 - cap / 2, 0, h / 2, cap, wid + 0.02, h + 0.02);
                if (marking != null) TopLabel(b, marking, 0, 0, h + 0.01, len * 0.55, wid * 0.62, Marking);
            }
        }

        /// <summary>Quad flat package (QFP / QFN when <paramref name="leads"/> is 0 length).</summary>
        private static void Quad(PartBuilder b, double cx, double cy, double z0, double size, double h, int perSide, double rot, string marking, double leadLen = 0.75, bool asBody = false)
        {
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, z0)))
            {
                (asBody ? b.Body(0x1A1A1Du, 0.3) : b[Mat.Epoxy]).Tapered(0, 0, 0.05, size, size, h, 0.12);
                if (asBody) b.Heat(P(0, 0, h));
                if (leadLen > 0)
                {
                    double pitch = (size - 1.4) / Math.Max(1, perSide - 1);
                    var tin = b[Mat.Tin];
                    for (int s = 0; s < 4; s++)
                    {
                        using (b.At(Mx.Rz(90 * s)))
                        {
                            for (int i = 0; i < perSide; i++)
                            {
                                double y = -((perSide - 1) * pitch) / 2 + i * pitch;
                                tin.Box(size / 2 + leadLen / 2 - 0.1, y, 0.08, leadLen + 0.2, Math.Min(0.3, pitch * 0.55), 0.12);
                            }
                        }
                    }
                }
                else
                {
                    var tin = b[Mat.Tin];
                    double pitch = (size - 1.0) / Math.Max(1, perSide - 1);
                    for (int s = 0; s < 4; s++)
                    {
                        using (b.At(Mx.Rz(90 * s)))
                        {
                            for (int i = 0; i < perSide; i++)
                            {
                                double y = -((perSide - 1) * pitch) / 2 + i * pitch;
                                tin.Box(size / 2 - 0.15, y, 0.06, 0.4, Math.Min(0.25, pitch * 0.5), 0.1);
                            }
                        }
                    }
                }

                b[Mat.Plastic(0x2A2A2Eu, 0.3)].CylinderZ(-size / 2 + 0.9, -size / 2 + 0.9, h + 0.05, h + 0.06, Math.Min(0.35, size * 0.06), 10, false, true);
                TopLabel(b, marking, 0, 0.1, h + 0.06, size * 0.8, size * 0.36, Marking);
            }
        }

        /// <summary>SOIC/SSOP gull-wing package, pins along its length (x).</summary>
        private static void Soic(PartBuilder b, double cx, double cy, double z0, int pins, double pitch, double bodyW, double rot, string marking, double h = 1.5)
        {
            int half = pins / 2;
            double len = half * pitch + 0.6;
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, z0)))
            {
                b[Mat.Epoxy].Tapered(0, 0, 0.1, len, bodyW, h, 0.1);
                var tin = b[Mat.Tin];
                for (int i = 0; i < half; i++)
                {
                    double x = -((half - 1) * pitch) / 2 + i * pitch;
                    foreach (int side in new[] { -1, 1 })
                    {
                        double yb = side * bodyW / 2;
                        var path = new List<Point3D> { P(x, yb - side * 0.1, h * 0.5), P(x, yb + side * 0.35, h * 0.5), P(x, yb + side * 0.55, 0.07), P(x, yb + side * 1.05, 0.07) };
                        tin.Strip(path, Math.Min(0.42, pitch * 0.5), 0.15, V(1, 0, 0));
                    }
                }

                b[Mat.Plastic(0x2A2A2Eu, 0.3)].CylinderZ(-len / 2 + 0.7, bodyW / 2 - 0.7, h + 0.1, h + 0.11, 0.3, 10, false, true);
                TopLabel(b, marking, 0, 0, h + 0.11, len * 0.75, bodyW * 0.4, Marking);
            }
        }

        private static void Sot223Body(PartBuilder b, double cx, double cy, double z0, double rot, string marking)
        {
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, z0)))
            {
                b[Mat.Epoxy].Tapered(0, 0, 0.1, 6.5, 3.5, 1.6, 0.1);
                var tin = b[Mat.Tin];
                for (int i = -1; i <= 1; i++)
                    tin.Strip(new List<Point3D> { P(i * 2.3, 1.6, 0.8), P(i * 2.3, 2.1, 0.8), P(i * 2.3, 2.5, 0.07), P(i * 2.3, 3.4, 0.07) }, 0.7, 0.26, V(1, 0, 0));
                tin.Strip(new List<Point3D> { P(0, -1.6, 0.8), P(0, -2.1, 0.8), P(0, -2.5, 0.07), P(0, -3.4, 0.07) }, 3.0, 0.26, V(1, 0, 0));
                TopLabel(b, marking, 0, 0, 1.71, 5.2, 1.4, Marking);
            }
        }

        private static void SmdElectrolytic(PartBuilder b, double cx, double cy, double z0, double d, double h)
        {
            // Plastic base, aluminium can with a black polarity mark on the top.
            b[Mat.Plastic(0x1A1A1Cu, 0.1)].BoxZ(cx, cy, z0, d + 0.3, d + 0.3, 1.0);
            using (b.At(cx, cy, z0 + 1.0))
            {
                b[Mat.Aluminium].Lathe(new[] { new Point(0, 0), new Point(d / 2, 0), new Point(d / 2, 0), new Point(d / 2, h - 1.4), new Point(d / 2 * 0.92, h - 1.2), new Point(d / 2, h - 1.0), new Point(d / 2, h - 0.3), new Point(d / 2 - 0.3, h - 0.0), new Point(d / 2 - 0.3, h - 0.0), new Point(0, h) }, 24);
                b[Mat.Plastic(0x101012u, 0.2)].Lathe(new[] { new Point(d / 2 - 0.25, h + 0.005), new Point(d / 2 - 1.1, h + 0.005) }, 24, Math.PI * 0.6, Math.PI * 1.4);
            }
        }

        private static void SmdLed(PartBuilder b, double cx, double cy, double z0, uint lens, double rot = 0)
        {
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, z0)))
            {
                b[Mat.Plastic(0xF2F2EEu, 0.2)].Box(0, 0, 0.25, 1.6, 0.8, 0.5);
                b[Mat.Clear(Mat.C(lens, 200), 0.9)].Box(0, 0, 0.6, 1.2, 0.7, 0.2);
                b[Mat.Tin].Box(-0.75, 0, 0.25, 0.3, 0.82, 0.52);
                b[Mat.Tin].Box(0.75, 0, 0.25, 0.3, 0.82, 0.52);
            }
        }

        private static void Crystal49(PartBuilder b, double cx, double cy, double z0, double rot, double len = 11.0, double wid = 4.65, double h = 3.6, string text = "16.000")
        {
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, z0)))
            {
                b[Mat.Nickel].RoundedBox(0, 0, 0, len, wid, h, wid / 2 - 0.05, 6);
                b[Mat.Nickel].RoundedBox(0, 0, 0, len + 0.5, wid + 0.5, 0.4, wid / 2 + 0.2, 6);
                TopLabel(b, text, 0, 0, h + 0.01, len * 0.6, wid * 0.42, Color.FromRgb(0x30, 0x30, 0x34));
            }
        }

        private static void TactSmd(PartBuilder b, double cx, double cy, double z0, double size, uint cap = 0x1E1E20u, double h = 1.6)
        {
            b[Mat.Plastic(0x2A2A2Eu, 0.1)].BoxZ(cx, cy, z0, size, size, h * 0.6);
            b[Mat.Steel].BoxZ(cx, cy, z0 + h * 0.6, size, size, 0.15);
            b[Mat.Plastic(cap, 0.25)].CylinderZ(cx, cy, z0 + h * 0.6, z0 + h + 0.35, size * 0.26, 16);
            var tin = b[Mat.Tin];
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                    tin.Box(cx + sx * (size / 2 + 0.3), cy + sy * size * 0.32, z0 + 0.1, 0.8, 0.5, 0.2);
        }

        /// <summary>USB-B receptacle (Arduino Uno), front face at x = x0 facing −x.</summary>
        private static void UsbB(PartBuilder b, double x0, double cy, double z0)
        {
            double len = 16.0, w = 12.0, h = 10.9;
            b[Mat.Nickel].BoxZ(x0 + len / 2, cy, z0, len, w, h);
            b[Mat.Plastic(0x070708u, 0)].Block(x0 - 0.01, cy - 4.0, z0 + 1.6, x0 + 0.3, cy + 4.0, z0 + 9.2);
            b[Mat.Plastic(0xE8E8E2u, 0.2)].Block(x0 - 0.02, cy - 2.6, z0 + 3.4, x0 + 0.4, cy + 2.6, z0 + 6.8);
            b[Mat.Nickel].Box(x0 + 0.2, cy, z0 + h - 0.6, 0.4, w + 0.6, 1.2);
        }

        /// <summary>Mini/micro USB receptacle, opening facing along +y (footprint) from y0.</summary>
        private static void UsbSmall(PartBuilder b, double cx, double y0, double z0, bool micro)
        {
            double w = micro ? 7.5 : 7.7, depth = micro ? 5.0 : 9.2, h = micro ? 2.6 : 3.9;
            b[Mat.Nickel].RoundedBox(cx, y0 - depth / 2, z0, w, depth, h, 0.6, 3);
            b[Mat.Plastic(0x060606u, 0)].Block(cx - w / 2 + 0.5, y0 - 0.3, z0 + 0.5, cx + w / 2 - 0.5, y0 + 0.01, z0 + h - 0.5);
        }

        /// <summary>DC barrel jack (5.5/2.1 mm), front face at x = x0 facing −x.</summary>
        private static void DcJack(PartBuilder b, double x0, double cy, double z0)
        {
            double len = 14.2, w = 9.0, h = 11.0;
            b[Mat.Plastic(0x18181Au, 0.15)].BoxZ(x0 + len / 2, cy, z0, len, w, h - 4.5);
            using (b.At(Mx.Ry(90) * Mx.T(x0, cy, z0 + h - 4.5)))
            {
                b[Mat.Plastic(0x18181Au, 0.15)].Cylinder(4.5, 0, len, 28);
            }

            b[Mat.Plastic(0x18181Au, 0.15)].BoxZ(x0 + len / 2, cy, z0 + h - 4.6, len, w, 0.1);
            using (b.At(Mx.Ry(90) * Mx.T(x0 - 0.02, cy, z0 + h - 4.5)))
            {
                b[Mat.Plastic(0x040404u, 0)].Cylinder(3.15, 0, 0.05, 24, true, false);
                b[Mat.Tin].Cylinder(1.0, -0.01, 3.0, 12);
            }
        }

        // ================================================================== resistor colour code

        internal static readonly uint[] DigitColours =
        {
            0x111111u, 0x6B3A1Eu, 0xC8201Eu, 0xF07818u, 0xF2D21Cu, 0x249A3Au, 0x2456C8u, 0x8236B0u, 0x8A8A8Au, 0xF2F2F2u
        };

        internal const uint GoldBand = 0xC9A23Cu, SilverBand = 0xC4C6CAu;

        /// <summary>
        /// Colour bands for a resistance: four bands (two digits) for ≥5 % parts, five bands
        /// (three digits) for precision parts; the last band is the tolerance.
        /// </summary>
        internal static uint[] ResistorBands(double ohms, double tolerancePct, out bool fiveBand)
        {
            fiveBand = tolerancePct > 0 && tolerancePct < 5;
            int digits = fiveBand ? 3 : 2;
            uint tol = tolerancePct switch
            {
                <= 0.05 => 0x8A8A8Au,
                <= 0.1 => DigitColours[7],
                <= 0.25 => DigitColours[6],
                <= 0.5 => DigitColours[5],
                <= 1 => DigitColours[1],
                <= 2 => DigitColours[2],
                <= 5 => GoldBand,
                _ => SilverBand
            };
            var bands = new List<uint>();
            if (ohms <= 0 || double.IsNaN(ohms) || double.IsInfinity(ohms))
            {
                for (int i = 0; i < digits; i++) bands.Add(DigitColours[0]);
                bands.Add(DigitColours[0]);
                bands.Add(tol);
                return bands.ToArray();
            }

            int exp = (int)Math.Floor(Math.Log10(ohms)) - (digits - 1);
            long mant = (long)Math.Round(ohms / Math.Pow(10, exp));
            if (mant >= (long)Math.Pow(10, digits))
            {
                mant /= 10;
                exp++;
            }

            exp = Math.Max(-2, Math.Min(9, exp));
            string s = mant.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
            foreach (char c in s.Substring(0, digits)) bands.Add(DigitColours[c - '0']);
            bands.Add(exp >= 0 ? DigitColours[exp] : exp == -1 ? GoldBand : SilverBand);
            if (tolerancePct < 20) bands.Add(tol);
            return bands.ToArray();
        }

        /// <summary>Three-digit capacitor / resistor code ("104" = 10 × 10⁴ pF).</summary>
        internal static string ValueCode(double value, double unit)
        {
            double v = value / unit;
            if (v <= 0) return "0";
            if (v < 10) return v.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', 'R');
            int exp = (int)Math.Floor(Math.Log10(v)) - 1;
            int two = (int)Math.Round(v / Math.Pow(10, exp));
            if (two >= 100)
            {
                two /= 10;
                exp++;
            }

            return two.ToString(CultureInfo.InvariantCulture) + exp.ToString(CultureInfo.InvariantCulture);
        }

        // ================================================================== defaults

        /// <summary>Anchors for pads the generator did not place: lead ends for through-hole, the pad for SMD.</summary>
        internal static void DefaultAnchors(PartBuilder b, ModelRequest r)
        {
            foreach (var pad in r.Pads)
            {
                if (b.Anchors.Any(a => string.Equals(a.Pad, pad.Name, StringComparison.OrdinalIgnoreCase))) continue;
                if (pad.IsThroughHole) b.Anchor(pad.Name, P(pad.X, pad.Y, Tail), V(0, 0, -1));
                else b.Anchor(pad.Name, P(pad.X, pad.Y, 0.05), V(0, 0, 1));
            }
        }

        /// <summary>Unknown part: a body over the courtyard with its reference and pins.</summary>
        internal static void Fallback(PartBuilder b, ModelRequest r)
        {
            var fp = r.Fp;
            double minX = -3, minY = -3, maxX = 3, maxY = 3, h = 3;
            if (fp != null)
            {
                minX = fp.CourtyardMinX;
                maxX = fp.CourtyardMaxX;
                minY = fp.CourtyardMinY;
                maxY = fp.CourtyardMaxY;
                h = Math.Max(0.8, Math.Min(12, fp.Height));
                if (maxX - minX < 0.5 || maxY - minY < 0.5)
                {
                    minX = fp.Pads.Count > 0 ? fp.Pads.Min(p => p.X) - 1.5 : -2;
                    maxX = fp.Pads.Count > 0 ? fp.Pads.Max(p => p.X) + 1.5 : 2;
                    minY = fp.Pads.Count > 0 ? fp.Pads.Min(p => p.Y) - 1.5 : -2;
                    maxY = fp.Pads.Count > 0 ? fp.Pads.Max(p => p.Y) + 1.5 : 2;
                }
            }

            double sx = Math.Max(1, maxX - minX - 0.4), sy = Math.Max(1, maxY - minY - 0.4);
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double z0 = fp != null && fp.Pads.Any(p => p.IsThroughHole) ? 0.4 : 0.05;
            b.Body(0x2B2C31u, 0.2).RoundedBox(cx, cy, z0, sx, sy, h - z0, Math.Min(0.6, Math.Min(sx, sy) / 4), 3);
            string label = string.IsNullOrWhiteSpace(r.RefDes) || r.RefDes.EndsWith("?", StringComparison.Ordinal) ? PartMarking(r, 14) : r.RefDes;
            double lw = sx * 0.8, lh = Math.Min(sy * 0.5, lw / Math.Max(1, label.Length) * 1.8);
            TopLabel(b, label, cx, cy, h + 0.01, lw, lh, Color.FromRgb(0xE6, 0xE6, 0xE0), 0, true);
            if (fp == null) return;
            foreach (var pad in fp.Pads)
            {
                if (pad.IsThroughHole) LeadDown(b, pad.X, pad.Y, z0 + 0.3, Math.Min(0.45, pad.Drill * 0.4));
                else b[Mat.Tin].Box(pad.X, pad.Y, 0.06, Math.Min(pad.Width, 1.4) * 0.8, Math.Min(pad.Height, 1.4) * 0.8, 0.12);
            }
        }
    }
}
