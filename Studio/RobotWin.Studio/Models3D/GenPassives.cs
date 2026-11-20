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
    internal static partial class Gen
    {
        // ================================================================== axial parts

        /// <summary>Frame with pad A at the origin and pad B on +x; returns the pitch.</summary>
        private static Frame.Scope AxialFrame(PartBuilder b, Pad a, Pad c, out double pitch)
        {
            double dx = c.X - a.X, dy = c.Y - a.Y;
            pitch = Math.Sqrt(dx * dx + dy * dy);
            double ang = Math.Atan2(dy, dx) * 180 / Math.PI;
            return b.At(Mx.Rz(ang) * Mx.T(a.X, a.Y, 0));
        }

        /// <summary>Lead bent from pad (x = xPad) up and across to the body end at xEnd, height zc.</summary>
        private static void BentLead(PartBuilder b, double xPad, double xEnd, double zc, double r, Material? m = null)
        {
            var path = MeshBuilder.Smooth(new[] { P(xPad, 0, Tail), P(xPad, 0, zc), P(xEnd, 0, zc) }, Math.Max(0.6, r * 3), 6);
            b[m ?? Mat.Tin].Tube(path, r, 8);
        }

        private static (Pad A, Pad B) TwoPads(ModelRequest r)
        {
            var pads = r.Pads.Where(p => p.IsThroughHole).ToList();
            if (pads.Count < 2) pads = r.Pads.ToList();
            if (pads.Count < 2) throw new InvalidOperationException("two pads needed");
            var a = pads.OrderBy(p => p.X).ThenBy(p => p.Y).First();
            var c = pads.Where(p => !ReferenceEquals(p, a)).OrderByDescending(p => Math.Abs(p.X - a.X) + Math.Abs(p.Y - a.Y)).First();
            return (a, c);
        }

        /// <summary>Radius profile of a resistor-style body (bulged end caps, slimmer middle).</summary>
        private static double DogBone(double s, double len, double rad)
        {
            double cap = len * 0.22, e = Math.Min(s, len - s);
            if (e < 0.45) return rad * (0.80 + 0.20 * Math.Sqrt(Math.Max(0, e / 0.45)));
            if (e < cap) return rad;
            if (e < cap + 0.45) return rad * (1 - 0.12 * (e - cap) / 0.45);
            return rad * 0.88;
        }

        private static List<Point> DogBoneProfile(double len, double rad)
        {
            var s = new List<double> { 0, 0.04, 0.12, 0.25, 0.45, len * 0.22, len * 0.22 + 0.22, len * 0.22 + 0.45 };
            var pts = new List<Point> { new Point(0, 0) };
            var all = s.Concat(s.Select(x => len - x)).Where(x => x >= 0 && x <= len).Distinct().OrderBy(x => x).ToList();
            pts.Add(new Point(DogBone(0, len, rad), 0));
            foreach (double x in all) pts.Add(new Point(DogBone(x, len, rad), x));
            pts.Add(new Point(DogBone(len, len, rad), len));
            pts.Add(new Point(0, len));
            // Keep a crease at the flat end faces.
            pts.Insert(2, pts[1]);
            pts.Insert(pts.Count - 1, pts[pts.Count - 2]);
            return pts;
        }

        private static void Band(MeshBuilder mb, double z0, double z1, Func<double, double> radius)
        {
            double r0 = radius(z0) + 0.025, r1 = radius(z1) + 0.025, rm = radius((z0 + z1) / 2) + 0.03;
            mb.Lathe(new[] { new Point(r0 - 0.06, z0), new Point(r0, z0 + 0.03), new Point(rm, (z0 + z1) / 2), new Point(r1, z1 - 0.03), new Point(r1 - 0.06, z1) }, 28);
        }

        internal static void Axial(PartBuilder b, ModelRequest r)
        {
            var (pa, pb) = TwoPads(r);
            using (AxialFrame(b, pa, pb, out double pitch))
            {
                switch (r.Id)
                {
                    case "fuse": GlassFuse(b, r, pitch); break;
                    case "switch": ReedSwitch(b, pitch); break;
                    case "inductor": ColourCoded(b, r, pitch, inductor: true); break;
                    default: ColourCoded(b, r, pitch, inductor: false); break;
                }
            }
        }

        private static void ColourCoded(PartBuilder b, ModelRequest r, double pitch, bool inductor)
        {
            double power = r.Num("power", 0.25);
            double len, rad;
            if (inductor)
            {
                len = 9.0;
                rad = 1.75;
            }
            else if (power >= 2)
            {
                len = 15.0;
                rad = 2.5;
            }
            else if (power >= 1)
            {
                len = 11.0;
                rad = 2.0;
            }
            else if (power >= 0.5)
            {
                len = 9.0;
                rad = 1.6;
            }
            else
            {
                len = 6.3;
                rad = 1.25;
            }

            len = Math.Max(3.0, Math.Min(len, pitch - 2.2));
            double xs = (pitch - len) / 2, zc = rad + 0.35;
            double leadR = power >= 1 ? 0.4 : 0.3;
            BentLead(b, 0, xs + 0.4, zc, leadR);
            BentLead(b, pitch, xs + len - 0.4, zc, leadR);

            uint bodyRgb;
            uint[] bands;
            bool five;
            if (inductor)
            {
                bodyRgb = 0x6FA85Eu;
                double uH = r.Num("inductance", 100e-6) * 1e6;
                bands = ResistorBands(uH, 10, out five);
            }
            else
            {
                double ohms = r.Num("resistance", SiValue.TryParse(r.Value, out var v) ? v : 1000);
                double tol = r.Num("tolerance", 5);
                bands = ResistorBands(ohms, tol, out five);
                bodyRgb = power >= 1 ? 0x7FA0B8u : five ? 0x4F8AC9u : 0xD9C092u;
            }

            using (b.At(Mx.Ry(90) * Mx.T(xs, 0, zc)))
            {
                b.Body(bodyRgb, 0.22).Lathe(DogBoneProfile(len, rad), 28);
                b.Heat(P(len / 2, 0, 0));
                double bw = len * 0.075;
                double[] at = five ? new[] { 0.13, 0.26, 0.38, 0.50, 0.82 } : new[] { 0.15, 0.30, 0.44, 0.80 };
                if (bands.Length == 3) at = new[] { 0.15, 0.30, 0.44 };
                for (int i = 0; i < bands.Length && i < at.Length; i++)
                {
                    double zc2 = at[i] * len;
                    var mat = bands[i] == GoldBand ? Mat.Metal(GoldBand, 0.6, 40) : bands[i] == SilverBand ? Mat.Metal(SilverBand, 0.7, 40) : Mat.Plastic(bands[i], 0.15);
                    Band(b[mat], zc2 - bw / 2, zc2 + bw / 2, z => DogBone(z, len, rad));
                }
            }
        }

        private static void GlassFuse(PartBuilder b, ModelRequest r, double pitch)
        {
            double len = Math.Max(6, Math.Min(10, pitch - 3.0)), rad = 1.8, cap = 1.9;
            double xs = (pitch - len) / 2, zc = rad + 0.4;
            BentLead(b, 0, xs + 0.3, zc, 0.3);
            BentLead(b, pitch, xs + len - 0.3, zc, 0.3);
            using (b.At(Mx.Ry(90) * Mx.T(xs, 0, zc)))
            {
                var nickel = b[Mat.Nickel];
                nickel.Lathe(new[] { new Point(0, 0), new Point(rad - 0.3, 0), new Point(rad + 0.05, 0.3), new Point(rad + 0.05, cap), new Point(rad - 0.2, cap) }, 24);
                nickel.Lathe(new[] { new Point(rad - 0.2, len - cap), new Point(rad + 0.05, len - cap), new Point(rad + 0.05, len - 0.3), new Point(rad - 0.3, len), new Point(0, len) }, 24);
                b[Mat.Clear(0xEAF3F5u, 70, 0.9)].Lathe(new[] { new Point(rad, cap), new Point(rad, len - cap) }, 24);
                bool slow = r.Text("speed").Equals("slow", StringComparison.OrdinalIgnoreCase);
                if (slow)
                {
                    var helix = new List<Point3D>();
                    for (int i = 0; i <= 160; i++)
                    {
                        double t = i / 160.0, a = t * 2 * Math.PI * 9;
                        helix.Add(P(Math.Cos(a) * 0.35, Math.Sin(a) * 0.35, cap + t * (len - 2 * cap)));
                    }

                    b.Body(0xB87333u, 0.6).Tube(helix, 0.07, 5, false, false);
                }
                else
                {
                    b.Body(0xC9CCD0u, 0.6).Rod(P(0, 0, cap - 0.2), P(0, 0, len - cap + 0.2), 0.06, 6);
                }
            }
        }

        private static void ReedSwitch(PartBuilder b, double pitch)
        {
            double len = Math.Max(8, Math.Min(14, pitch - 1.6)), rad = 1.25;
            double xs = (pitch - len) / 2, zc = rad + 0.3;
            BentLead(b, 0, xs + 0.6, zc, 0.28, Mat.Nickel);
            BentLead(b, pitch, xs + len - 0.6, zc, 0.28, Mat.Nickel);
            using (b.At(Mx.Ry(90) * Mx.T(xs, 0, zc)))
            {
                var pr = new List<Point> { new Point(0, 0) };
                for (int i = 1; i <= 5; i++)
                {
                    double a = Math.PI / 2 * i / 5;
                    pr.Add(new Point(rad * Math.Sin(a), 0.9 * (1 - Math.Cos(a))));
                }

                pr.Add(new Point(rad, len - 0.9));
                for (int i = 1; i <= 5; i++)
                {
                    double a = Math.PI / 2 * i / 5;
                    pr.Add(new Point(rad * Math.Cos(a), len - 0.9 + 0.9 * Math.Sin(a)));
                }

                pr[pr.Count - 1] = new Point(0, len);
                b[Mat.Clear(0xE4F2EEu, 70, 0.95)].Lathe(pr, 20);
                var reed = b.Body(0xC9B26Au, 0.7);
                reed.Box(0.12, 0, len * 0.3, 0.2, 0.7, len * 0.48);
                reed.Box(-0.12, 0, len * 0.7, 0.2, 0.7, len * 0.48);
            }
        }

        // ================================================================== axial diodes

        internal static void AxialDiode(PartBuilder b, ModelRequest r)
        {
            var (p1, p2) = TwoPads(r);
            var k = r.PinPad("K") ?? r.Pad("1") ?? p1;
            var a = ReferenceEquals(k, p1) ? p2 : p1;
            string fp = r.FpId;
            bool glass = fp.Contains("35") && !fp.Contains("DO41");
            double len, rad;
            if (fp.Contains("201") || fp.Contains("DO-15"))
            {
                len = fp.Contains("DO-15") ? 7.0 : 9.5;
                rad = fp.Contains("DO-15") ? 1.8 : 2.65;
            }
            else if (glass)
            {
                len = 3.8;
                rad = 0.9;
            }
            else
            {
                len = 5.2;
                rad = 1.35;
            }

            using (AxialFrame(b, a, k, out double pitch))
            {
                len = Math.Min(len, pitch - 1.6);
                double xs = (pitch - len) / 2, zc = rad + 0.3;
                double leadR = rad >= 2 ? 0.6 : rad >= 1.3 ? 0.4 : 0.26;
                BentLead(b, 0, xs + 0.4, zc, leadR);
                BentLead(b, pitch, xs + len - 0.4, zc, leadR);
                using (b.At(Mx.Ry(90) * Mx.T(xs, 0, zc)))
                {
                    var profile = new List<Point>
                    {
                        new Point(0, 0), new Point(rad * 0.62, 0), new Point(rad * 0.62, 0), new Point(rad * 0.92, 0.12), new Point(rad, 0.4),
                        new Point(rad, len - 0.4), new Point(rad * 0.92, len - 0.12), new Point(rad * 0.62, len), new Point(rad * 0.62, len), new Point(0, len)
                    };
                    b.Heat(P(len / 2, 0, 0));
                    if (glass)
                    {
                        // Glass DO-35: the slugs and die show through the tinted glass.
                        var slug = b.Body(0xC8CBD0u, 0.8);
                        slug.Cylinder(rad * 0.62, 0.05, len * 0.36, 16);
                        slug.Cylinder(rad * 0.62, len * 0.64, len - 0.05, 16);
                        b[Mat.Plastic(0x202022u, 0.4)].Cylinder(rad * 0.45, len * 0.36, len * 0.64, 12);
                        b[Mat.Clear(r.Id == "zener" ? 0xE8803Cu : 0xE65A30u, 150, 0.9)].Lathe(profile, 24);
                        Band(b[Mat.Plastic(0x101012u, 0.3)], len - 1.05, len - 0.45, _ => rad);
                    }
                    else
                    {
                        string text = DiodeText(r);
                        var tex = Tex.Get("dio|" + text + "|" + F(rad), () => AxialPrint(0x1C1C1Fu, 0xBFC3C8u, 0.06, 0.24, text, 0xC8CCD0u, 2 * Math.PI * rad / len));
                        b.Body(tex, Mat.C(0x1C1C1Fu), 0.35).Lathe(profile, 28, -Math.PI / 2, 1.5 * Math.PI);
                    }
                }
            }
        }

        private static string DiodeText(ModelRequest r)
        {
            string m = PartMarking(r, 8);
            if (m.StartsWith("1N", StringComparison.OrdinalIgnoreCase)) m = m.Substring(2);
            return m;
        }

        /// <summary>
        /// Print wrapped around an axial body: u runs round the circumference, v along the axis
        /// (v = 0 at the +x end, where the band is). Text runs along the axis.
        /// </summary>
        private static BitmapSource AxialPrint(uint body, uint band, double band0, double band1, string text, uint textRgb, double aspect)
        {
            int h = 256, w = Math.Max(64, (int)(h * aspect));
            return Tex.Render(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(Mat.C(body)), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new SolidColorBrush(Mat.C(band)), null, new Rect(0, h * band0, w, h * (band1 - band0)));
                if (string.IsNullOrEmpty(text)) return;
                var ft = Tex.Text(text, w * 0.11, Mat.C(textRgb), true);
                double avail = h * (1 - band1 - 0.08);
                double s = Math.Min(1, avail / Math.Max(1, ft.Width));
                // Text along the axis, upright when seen from above (texture u = round, v = along).
                dc.PushTransform(new MatrixTransform(0, 1, 1, 0, w * 0.75, h * (band1 + 0.04) + avail / 2));
                dc.PushTransform(new ScaleTransform(s, s));
                dc.DrawText(ft, new Point(-ft.Width / 2, -ft.Height / 2));
                dc.Pop();
                dc.Pop();
            });
        }

        // ================================================================== electrolytic capacitor

        internal static void Electrolytic(PartBuilder b, ModelRequest r)
        {
            double d = 6.3;
            var parts = r.FpId.Split('-');
            if (parts.Length >= 2 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dd)) d = dd;
            else if (r.Fp != null) d = Math.Max(4, Math.Min(r.Fp.CourtyardMaxX - r.Fp.CourtyardMinX, r.Fp.CourtyardMaxY - r.Fp.CourtyardMinY) - 1.0);
            double h = d <= 5 ? 11 : d <= 6.3 ? 11 : d <= 8 ? 11.5 : d <= 10 ? 16 : 21;
            var (pa, pb) = TwoPads(r);
            var plus = r.PinPad("+") ?? r.Pad("1") ?? pa;
            var minus = ReferenceEquals(plus, pa) ? pb : pa;
            double cx = (pa.X + pb.X) / 2, cy = (pa.Y + pb.Y) / 2;
            double toMinus = Math.Atan2(minus.Y - cy, minus.X - cx) * 180 / Math.PI;
            double rad = d / 2;
            foreach (var pad in new[] { pa, pb }) LeadDown(b, pad.X, pad.Y, 0.6, d >= 10 ? 0.3 : 0.25);

            using (b.At(Mx.Rz(toMinus) * Mx.T(cx, cy, 0.3)))
            {
                // Rubber seal at the bottom.
                b[Mat.Rubber].Cylinder(rad - 0.45, 0, 0.5, 24);
                // Printed PVC sleeve with the minus stripe facing the − lead.
                string cap = CapText(r.Num("capacitance", 100e-6));
                string volts = r.Text("voltage", "16");
                var tex = Tex.Get($"ecap|{cap}|{volts}|{F(d)}", () => SleevePrint(cap + "  " + volts + "V", h / (Math.PI * d)));
                var profile = new List<Point>
                {
                    new Point(rad - 0.45, 0.15), new Point(rad - 0.08, 0.35), new Point(rad, 0.8),
                    new Point(rad, h - 1.9), new Point(rad - 0.35, h - 1.55), new Point(rad, h - 1.2),
                    new Point(rad, h - 0.45), new Point(rad - 0.15, h - 0.1), new Point(rad - 0.5, h - 0.02)
                };
                b.Body(tex, Mat.C(0x1D2C55u), 0.3).Lathe(profile, 32, -Math.PI / 2, 1.5 * Math.PI);
                b.Heat(P(0, 0, h));
                // Aluminium top with the scored safety vent.
                b[Mat.Aluminium].Lathe(new[] { new Point(rad - 0.5, h - 0.05), new Point(rad - 0.55, h - 0.12), new Point(0, h - 0.12) }, 32);
                var vent = b[Mat.Plastic(0x6E7076u, 0.4)];
                if (d >= 6)
                {
                    vent.Box(0, 0, h - 0.1, (rad - 0.7) * 2, 0.22, 0.04);
                    vent.Box(0, 0, h - 0.1, 0.22, (rad - 0.7) * 2, 0.04);
                }
                else
                {
                    vent.Box(0, 0, h - 0.1, (rad - 0.7) * 2, 0.2, 0.04);
                }
            }
        }

        private static string CapText(double f)
        {
            if (f >= 1e-6) return (f * 1e6).ToString("0.#", CultureInfo.InvariantCulture) + "µF";
            if (f >= 1e-9) return (f * 1e9).ToString("0.#", CultureInfo.InvariantCulture) + "nF";
            return (f * 1e12).ToString("0", CultureInfo.InvariantCulture) + "pF";
        }

        /// <summary>Sleeve print: u round the can (stripe centred at u = 0.25 = the − side), v from top to bottom.</summary>
        private static BitmapSource SleevePrint(string text, double aspectHOverW)
        {
            int w = 512, h = Math.Max(64, (int)(w * aspectHOverW));
            return Tex.Render(w, h, dc =>
            {
                dc.DrawRectangle(new SolidColorBrush(Mat.C(0x1D2C55u)), null, new Rect(0, 0, w, h));
                double sx = w * 0.17, s0 = w * 0.25 - sx / 2;
                dc.DrawRectangle(new SolidColorBrush(Mat.C(0xA9B8CFu)), null, new Rect(s0, 0, sx, h));
                var minus = new SolidColorBrush(Mat.C(0x1D2C55u));
                for (double y = h * 0.12; y < h * 0.9; y += w * 0.12)
                {
                    dc.DrawRectangle(minus, null, new Rect(s0 + sx * 0.3, y, sx * 0.4, sx * 0.09));
                }

                var ft = Tex.Text(text, w * 0.055, Mat.C(0xE8ECF2u), true);
                var ft2 = Tex.Text("105°C", w * 0.045, Mat.C(0xE8ECF2u));
                double s = Math.Min(1, h * 0.8 / Math.Max(1, ft.Width));
                foreach (var (f, u) in new[] { (ft, 0.66), (ft2, 0.76) })
                {
                    dc.PushTransform(new MatrixTransform(0, 1, 1, 0, w * u, h / 2.0));
                    dc.PushTransform(new ScaleTransform(s, s));
                    dc.DrawText(f, new Point(-f.Width / 2, -f.Height / 2));
                    dc.Pop();
                    dc.Pop();
                }
            });
        }

        // ================================================================== radial small parts

        internal static void Radial(PartBuilder b, ModelRequest r)
        {
            var (pa, pb) = TwoPads(r);
            using (AxialFrame(b, pa, pb, out double pitch))
            {
                switch (r.Id)
                {
                    case "ldr": Ldr(b, pitch); break;
                    case "ntc": Ntc(b, pitch); break;
                    case "fuse": Polyfuse(b, r, pitch); break;
                    default: CeramicCap(b, r, pitch); break;
                }
            }
        }

        private static void CeramicCap(PartBuilder b, ModelRequest r, double pitch)
        {
            double c = r.Num("capacitance", 100e-9);
            bool disc = c <= 1.01e-9;
            double dia = pitch >= 4.5 ? 6.5 : 4.6, t = disc ? 2.4 : 2.8;
            double zc = 2.6 + dia / 2, cx = pitch / 2;
            // Legs from the coated stem down to the pads.
            var tin = b[Mat.Tin];
            foreach (double x in new[] { 0.0, pitch })
            {
                double xi = cx + (x - cx) * 0.55;
                tin.Tube(MeshBuilder.Smooth(new[] { P(x, 0, Tail), P(x, 0, 1.6), P(xi, 0, zc - dia * 0.3) }, 0.8, 5), 0.25, 8);
            }

            using (b.At(Mx.Rx(90) * Mx.T(cx, 0, zc)))
            {
                var pr = new List<Point>();
                for (int i = 0; i <= 10; i++)
                {
                    double a = -Math.PI / 2 + Math.PI * i / 10;
                    double rr = dia / 2 - t / 2 * (1 - Math.Cos(a)) * (disc ? 0.6 : 0.9);
                    pr.Add(new Point(rr, t / 2 * Math.Sin(a)));
                }

                pr.Insert(0, new Point(0, -t / 2));
                pr.Add(new Point(0, t / 2));
                b.Body(disc ? 0xC9783Au : 0x2F68B8u, disc ? 0.15 : 0.35).Lathe(pr, 28);
            }

            // Drip of the dipped coating round the legs.
            b[Mat.Plastic(disc ? 0xC9783Au : 0x2F68B8u, 0.3)].Box(cx, 0, zc - dia / 2 + 0.2, dia * 0.5, t * 0.6, 1.0);
            string code = ValueCode(c, 1e-12);
            FrontLabel(b, code, P(cx, t / 2 + 0.03, zc), V(0, 1, 0), V(0, 0, 1), dia * 0.55, dia * 0.3, disc ? Color.FromRgb(0x2A, 0x1A, 0x10) : Color.FromRgb(0xE8, 0xEC, 0xF2), true);
        }

        private static void Ntc(PartBuilder b, double pitch)
        {
            double cx = pitch / 2, zc = 4.6;
            var tin = b[Mat.Copper];
            foreach (double x in new[] { 0.0, pitch })
                tin.Tube(MeshBuilder.Smooth(new[] { P(x, 0, Tail), P(x, 0, 1.4), P(cx + (x - cx) * 0.25, 0, zc - 0.8) }, 1.2, 6), 0.2, 6);
            using (b.At(cx, 0, zc))
            {
                var pr = new List<Point> { new Point(0, -1.6) };
                for (int i = 1; i < 12; i++)
                {
                    double a = -Math.PI / 2 + Math.PI * i / 12;
                    double rr = 1.45 * Math.Cos(a) * (1 + 0.15 * Math.Sin(a));
                    pr.Add(new Point(rr, 1.5 * Math.Sin(a) - (a < 0 ? 0.25 * Math.Cos(a) : 0)));
                }

                pr.Add(new Point(0, 1.5));
                b.Body(0x1F1F22u, 0.55).Lathe(pr, 20);
            }
        }

        private static void Ldr(PartBuilder b, double pitch)
        {
            double cx = pitch / 2, z0 = 3.4, dia = 5.1, t = 2.0;
            var tin = b[Mat.Tin];
            foreach (double x in new[] { 0.0, pitch })
                tin.Tube(MeshBuilder.Smooth(new[] { P(x, 0, Tail), P(x, 0, z0 + 0.3) }, 0.5, 2), 0.25, 8);
            b.Body(0xE3D3A8u, 0.25).CylinderZ(cx, 0, z0, z0 + t - 0.05, dia / 2, 28);
            var tex = Mat.Image("ldr-face", () => Tex.Render(256, 256, dc =>
            {
                dc.DrawEllipse(new SolidColorBrush(Mat.C(0xE3D3A8u)), null, new Point(128, 128), 128, 128);
                var silver = new SolidColorBrush(Mat.C(0xC9CDD2u));
                dc.DrawRectangle(silver, null, new Rect(20, 30, 216, 26));
                dc.DrawRectangle(silver, null, new Rect(20, 200, 216, 26));
                var track = new Pen(new SolidColorBrush(Mat.C(0xB4552Eu)), 12) { LineJoin = PenLineJoin.Round };
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(40, 56), false, false);
                    for (int i = 0; i < 7; i++)
                    {
                        double x = 40 + i * 29;
                        g.LineTo(new Point(x, 200), true, true);
                        g.LineTo(new Point(x + 14.5, 200), true, true);
                        g.LineTo(new Point(x + 14.5, 56), true, true);
                        g.LineTo(new Point(x + 29, 56), true, true);
                    }
                }

                dc.DrawGeometry(null, track, geo);
            }), 0.4);
            b[tex].Extrude(MeshBuilder.Circle(cx, 0, dia / 2 - 0.05, 28), z0 + t - 0.05, z0 + t - 0.04, false, true, false);
            b[Mat.Clear(0xF0F4F6u, 60, 0.9)].CylinderZ(cx, 0, z0 + t - 0.04, z0 + t + 0.1, dia / 2, 28, false, true);
        }

        private static void Polyfuse(PartBuilder b, ModelRequest r, double pitch)
        {
            double cx = pitch / 2, w = Math.Max(5, pitch + 2.2), hgt = 7.0, t = 3.0, z0 = 2.2;
            var tin = b[Mat.Tin];
            foreach (double x in new[] { 0.0, pitch }) tin.Rod(P(x, 0, Tail), P(x, 0, z0 + 0.8), 0.3, 8);
            using (b.At(Mx.Rx(90) * Mx.T(cx, 0, z0 + hgt / 2)))
            {
                b.Body(0xE6BF35u, 0.25).RoundedBox(0, 0, -t / 2, w, hgt, t, 1.6, 4);
            }

            string code = ((int)Math.Round(r.Num("rating", 0.5) * 100)).ToString("000", CultureInfo.InvariantCulture);
            FrontLabel(b, code, P(cx, t / 2 + 0.03, z0 + hgt / 2), V(0, 1, 0), V(0, 0, 1), w * 0.5, hgt * 0.3, Color.FromRgb(0x20, 0x20, 0x20), true);
        }

        // ================================================================== LED

        internal static (Color Tint, Color Light) LedColours(string colour)
        {
            switch ((colour ?? "red").ToLowerInvariant())
            {
                case "green":
                case "yeşil": return (Mat.C(0x2DB34Au, 205), Color.FromRgb(70, 255, 110));
                case "blue":
                case "mavi": return (Mat.C(0x2F6CF2u, 205), Color.FromRgb(80, 140, 255));
                case "yellow":
                case "sarı": return (Mat.C(0xF2C21Cu, 205), Color.FromRgb(255, 225, 60));
                case "orange":
                case "turuncu": return (Mat.C(0xF07820u, 205), Color.FromRgb(255, 150, 40));
                case "white":
                case "beyaz": return (Mat.C(0xEEF3F8u, 120), Color.FromRgb(255, 255, 255));
                case "ir": return (Mat.C(0x3A3A48u, 175), Color.FromRgb(90, 20, 30));
                case "uv": return (Mat.C(0x8A40E0u, 180), Color.FromRgb(170, 80, 255));
                default: return (Mat.C(0xD82525u, 205), Color.FromRgb(255, 50, 40));
            }
        }

        internal static void Led(PartBuilder b, ModelRequest r)
        {
            var (p1, p2) = TwoPads(r);
            var k = r.PinPad("K") ?? r.Pad("1") ?? p1;
            var a = ReferenceEquals(k, p1) ? p2 : p1;
            bool small = r.FpId.Contains("3MM") || r.FpId.Contains("D3");
            double rad = small ? 1.5 : 2.5, flange = small ? 1.9 : 2.9, total = small ? 5.3 : 8.6, zb = 1.0;
            var (tint, light) = LedColours(r.Text("color", "red"));
            using (AxialFrame(b, k, a, out double pitch))
            {
                double cx = pitch / 2, zt = zb + total;
                var tin = b[Mat.Tin];
                // Legs: square leads up into the epoxy.
                foreach (double x in new[] { 0.0, pitch })
                {
                    double xi = cx + Math.Sign(x - cx) * Math.Min(1.27, Math.Abs(x - cx));
                    tin.Strip(MeshBuilder.Smooth(new[] { P(x, 0, Tail), P(x, 0, zb - 0.5), P(xi, 0, zb + 0.2) }, 0.4, 4), 0.5, 0.5, V(0, 1, 0));
                }

                // Lead frame inside the epoxy: cathode anvil with the cup, anode post, bond wire.
                double xk = cx - Math.Min(1.27, cx), xa = cx + Math.Min(1.27, cx);
                double frameTop = zb + total * 0.5;
                tin.Box(xk, 0, (zb + frameTop) / 2, 0.5, 0.5, frameTop - zb);
                tin.Hexa(P(xk - 0.25, -0.25, frameTop), P(xk + 0.25, -0.25, frameTop), P(xk + 0.25, 0.25, frameTop), P(xk - 0.25, 0.25, frameTop),
                    P(xk - 0.25, -0.3, frameTop + 0.9), P(cx + 0.2, -0.3, frameTop + 0.9), P(cx + 0.2, 0.3, frameTop + 0.9), P(xk - 0.25, 0.3, frameTop + 0.9));
                tin.Box(xa, 0, (zb + frameTop + 0.6) / 2, 0.5, 0.5, frameTop + 0.6 - zb);
                tin.Box(xa - 0.2, 0, frameTop + 0.75, 0.9, 0.5, 0.3);
                b[Mat.Plastic(0x18181Au, 0.5)].Box(cx - 0.25, 0, frameTop + 0.95, 0.35, 0.35, 0.12);
                b[Mat.Gold].Tube(MeshBuilder.Smooth(new[] { P(cx - 0.25, 0, frameTop + 1.0), P(cx, 0, frameTop + 1.6), P(xa - 0.4, 0, frameTop + 0.9) }, 0.4, 6), 0.03, 4, false, false);

                // Epoxy lens: flange with the cathode flat, body and dome.
                var lens = b.Lens(tint, light);
                var outline = MeshBuilder.Circle(cx, 0, flange, 32).Select(p => new Point(Math.Max(p.X, cx - flange + 0.55), p.Y)).ToList();
                lens.Extrude(outline, zb, zb + 1.0, true);
                var pr = new List<Point> { new Point(rad, zb + 1.0), new Point(rad, zt - rad) };
                for (int i = 1; i <= 9; i++)
                {
                    double ang = Math.PI / 2 * i / 9;
                    pr.Add(new Point(rad * Math.Cos(ang), zt - rad + rad * Math.Sin(ang)));
                }

                pr[pr.Count - 1] = new Point(0, zt);
                using (b.At(cx, 0, 0))
                {
                    lens.Lathe(pr, 28);
                }

                b.Heat(P(cx, 0, zt));
            }
        }

        // ================================================================== SMD chips

        internal static void Chip(PartBuilder b, ModelRequest r)
        {
            string f = r.FpId;
            double len = 2.0, wid = 1.25;
            if (f.Contains("0603")) (len, wid) = (1.6, 0.8);
            else if (f.Contains("1206")) (len, wid) = (3.2, 1.6);
            else if (f.Contains("0402")) (len, wid) = (1.0, 0.5);
            var (pa, pb) = TwoPads(r);
            bool led = r.Id == "led" || f.StartsWith("LED", StringComparison.Ordinal);
            bool cap = !led && (r.Id == "capacitor" || r.Id == "electrolytic" || f.StartsWith("C", StringComparison.Ordinal) && !f.StartsWith("CP", StringComparison.Ordinal));
            double h = led ? 0.75 : cap ? 0.8 : 0.5;
            using (AxialFrame(b, pa, pb, out double pitch))
            {
                double cx = pitch / 2, cap0 = len * 0.2;
                var tin = b[Mat.Tin];
                tin.Box(cx - len / 2 + cap0 / 2, 0, h / 2 + 0.02, cap0, wid + 0.02, h + 0.02);
                tin.Box(cx + len / 2 - cap0 / 2, 0, h / 2 + 0.02, cap0, wid + 0.02, h + 0.02);
                // Solder fillets onto the pads.
                var solder = b[Mat.Solder];
                foreach (int s in new[] { -1, 1 })
                {
                    double xe = cx + s * (len / 2 + 0.01), xo = xe + s * 0.4, hw = wid / 2 - 0.02;
                    solder.Hexa(P(xo, -hw, 0.001), P(xe, -hw, 0.001), P(xe, hw, 0.001), P(xo, hw, 0.001),
                        P(xo, -hw, 0.02), P(xe, -hw, h * 0.7), P(xe, hw, h * 0.7), P(xo, hw, 0.02));
                }

                if (led)
                {
                    var (tint, light) = LedColours(r.Text("color", "red"));
                    b.Body(0xF0F0EAu, 0.2).Box(cx, 0, 0.25, len - 2 * cap0 + 0.02, wid, 0.42);
                    b.Lens(Color.FromArgb(150, tint.R, tint.G, tint.B), light).Box(cx, 0, 0.6, len - 2 * cap0, wid * 0.9, 0.3);
                }
                else if (cap)
                {
                    b.Body(0xB48A5Eu, 0.25).Box(cx, 0, h / 2 + 0.02, len - 2 * cap0 + 0.02, wid, h);
                }
                else
                {
                    b.Body(0x16161Au, 0.25).Box(cx, 0, h / 2 + 0.02, len - 2 * cap0 + 0.02, wid, h);
                    double ohms = r.Num("resistance", SiValue.TryParse(r.Value, out var v) ? v : 1000);
                    TopLabel(b, ValueCode(ohms, 1), cx, 0, h + 0.03, (len - 2 * cap0) * 0.9, wid * 0.55, Color.FromRgb(0xEE, 0xEE, 0xEE), 0, true);
                }
            }
        }

        // ================================================================== crystal

        internal static void Crystal(PartBuilder b, ModelRequest r)
        {
            var (pa, pb) = TwoPads(r);
            using (AxialFrame(b, pa, pb, out double pitch))
            {
                double cx = pitch / 2;
                foreach (double x in new[] { 0.0, pitch }) LeadDown(b, x, 0, 0.5, 0.22);
                b[Mat.Plastic(0x1A1A1Cu, 0.1)].RoundedBox(cx, 0, 0, 11.4, 5.0, 0.5, 2.4, 6);
                b.Body(0xC5C9CFu, 0.9).RoundedBox(cx, 0, 0.5, 11.0, 4.65, 3.0, 2.3, 8);
                b.Heat(P(cx, 0, 3.5));
                string f = r.Text("frequency", r.Value);
                TopLabel(b, string.IsNullOrWhiteSpace(f) ? "16.000" : f, cx, 0, 3.51, 6.4, 1.8, Color.FromRgb(0x28, 0x28, 0x2C), 0, true);
            }
        }
    }
}
