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
        /// <summary>Pads of a single-row footprint ordered along the row, in a frame with the row on +x.</summary>
        private static Frame.Scope RowFrame(PartBuilder b, ModelRequest r, out List<double> xs)
        {
            var pads = r.Pads.ToList();
            var first = r.Pad("1") ?? pads.OrderBy(p => p.X).ThenBy(p => p.Y).First();
            var far = pads.OrderByDescending(p => (p.X - first.X) * (p.X - first.X) + (p.Y - first.Y) * (p.Y - first.Y)).First();
            double dx = far.X - first.X, dy = far.Y - first.Y, len = Math.Sqrt(dx * dx + dy * dy);
            double ang = len < 1e-6 ? 0 : Math.Atan2(dy, dx) * 180 / Math.PI;
            double ux = len < 1e-6 ? 1 : dx / len, uy = len < 1e-6 ? 0 : dy / len;
            xs = pads.Select(p => (p.X - first.X) * ux + (p.Y - first.Y) * uy).OrderBy(x => x).ToList();
            return b.At(Mx.Rz(ang) * Mx.T(first.X, first.Y, 0));
        }

        // ================================================================== TO-92

        internal static void To92(PartBuilder b, ModelRequest r)
        {
            using (RowFrame(b, r, out var xs))
            {
                double span = xs.Count > 0 ? xs[xs.Count - 1] : 5.08, cx = span / 2, z0 = 2.8, h = 4.8;
                var outline = MeshBuilder.Circle(cx, 0.2, 2.6, 36).Select(p => new Point(p.X, Math.Min(p.Y, 1.6))).ToList();
                b.Body(0x1B1B1Eu, 0.3).Extrude(outline, z0, z0 + h, true);
                b.Heat(P(cx, 0, z0 + h));
                var tin = b[Mat.Tin];
                int n = xs.Count;
                for (int i = 0; i < n; i++)
                {
                    double xb = cx + (i - (n - 1) / 2.0) * 1.27;
                    var path = MeshBuilder.Smooth(new[] { P(xs[i], 0, Tail), P(xs[i], 0, z0 - 1.4), P(xb, 0, z0 - 0.4), P(xb, 0, z0 + 0.3) }, 0.5, 4);
                    tin.Strip(path, 0.46, 0.38, V(1, 0, 0));
                }

                string text = PartMarking(r, 9);
                FrontLabel(b, text, P(cx, 1.62, z0 + h * 0.62), V(0, 1, 0), V(0, 0, 1), 3.9, 1.1, Marking, true);
                FrontLabel(b, "• 2318", P(cx, 1.62, z0 + h * 0.36), V(0, 1, 0), V(0, 0, 1), 2.6, 0.8, Marking);
            }
        }

        // ================================================================== TO-220 / TO-126

        internal static void To220(PartBuilder b, ModelRequest r)
        {
            using (RowFrame(b, r, out var xs))
            {
                double span = xs.Count > 0 ? xs[xs.Count - 1] : 5.08, cx = span / 2, zb = 3.0, bodyH = 9.15;
                // Metal tab with the mounting hole (back), epoxy body in front.
                using (b.At(Mx.Rx(90)))
                {
                    b[Mat.Tin].PlateWithHole(cx - 5.0, zb, cx + 5.0, zb + 15.6, 1.7, 3.0, cx, zb + 12.8, 1.8, 28);
                }

                b.Body(0x1A1A1Du, 0.3).Tapered(cx, -0.3, zb, 10.0, 3.4, bodyH, 0.12, 0.08);
                b.Heat(P(cx, 0, zb + bodyH / 2));
                foreach (double x in xs) FlatLead(b, x, 0, zb + 0.2, zb - 1.6, 1.4, 0.8, 0.5);
                string text = PartMarking(r, 10);
                FrontLabel(b, text, P(cx, 1.42, zb + bodyH * 0.62), V(0, 1, 0), V(0, 0, 1), 8.2, 2.0, Marking, true);
                FrontLabel(b, "2318  CHN", P(cx, 1.42, zb + bodyH * 0.35), V(0, 1, 0), V(0, 0, 1), 5.6, 1.3, Marking);
            }
        }

        internal static void To126(PartBuilder b, ModelRequest r)
        {
            using (RowFrame(b, r, out var xs))
            {
                double span = xs.Count > 0 ? xs[xs.Count - 1] : 4.56, cx = span / 2, zb = 3.0, h = 11.0;
                using (b.At(Mx.Rx(90)))
                {
                    b.Body(0x1A1A1Du, 0.3).PlateWithHole(cx - 3.9, zb, cx + 3.9, zb + h, -1.15, 1.45, cx, zb + h - 3.6, 1.6, 24);
                    b[Mat.Tin].PlateWithHole(cx - 3.6, zb + 1.0, cx + 3.6, zb + h - 0.6, 1.45, 1.55, cx, zb + h - 3.6, 1.6, 24);
                }

                b.Heat(P(cx, 0, zb + h / 2));
                foreach (double x in xs) FlatLead(b, x, 0, zb + 0.2, zb - 1.6, 1.2, 0.75, 0.5);
                FrontLabel(b, PartMarking(r, 8), P(cx, 1.17, zb + 3.4), V(0, 1, 0), V(0, 0, 1), 6.2, 1.6, Marking, true);
            }
        }

        // ================================================================== SOT-23 / SOT-223

        internal static void Sot23(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            double cx = pads.Average(p => p.X), cy = pads.Average(p => p.Y);
            double rows = pads.Max(p => p.Y) - pads.Min(p => p.Y), cols = pads.Max(p => p.X) - pads.Min(p => p.X);
            bool vertical = rows >= cols * 0.9;
            double rot = vertical ? 0 : 90;
            using (b.At(Mx.Rz(rot) * Mx.T(cx, cy, 0)))
            {
                b.Body(0x1C1C1Fu, 0.3).Tapered(0, 0, 0.06, 2.9, 1.3, 1.0, 0.06);
                b.Heat(P(0, 0, 1.0));
                var tin = b[Mat.Tin];
                foreach (var pad in pads)
                {
                    double lx = vertical ? pad.X - cx : pad.Y - cy, ly = vertical ? pad.Y - cy : -(pad.X - cx);
                    double s = Math.Sign(ly);
                    if (s == 0) s = 1;
                    tin.Strip(new List<Point3D> { P(lx, s * 0.5, 0.5), P(lx, s * 0.75, 0.5), P(lx, s * 0.9, 0.08), P(lx, s * Math.Max(1.15, Math.Abs(ly) + 0.25), 0.08) }, 0.4, 0.13, V(1, 0, 0));
                }

                string code = PartMarking(r, 6);
                TopLabel(b, code.Length > 4 ? code.Substring(code.Length - 4) : code, 0, 0, 1.07, 2.2, 0.8, Marking, 0, true);
            }
        }

        internal static void Sot223(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            var tab = pads.OrderByDescending(p => p.Width * p.Height).First();
            var small = pads.Where(p => !ReferenceEquals(p, tab)).ToList();
            double sy = small.Count > 0 ? small.Average(p => p.Y) : 3.15;
            double cx = small.Count > 0 ? small.Average(p => p.X) : 0, cy = (sy + tab.Y) / 2;
            double rot = sy > tab.Y ? 0 : 180;
            Sot223Body(b, cx, cy, 0.0, rot, PartMarking(r, 9));
            b.Body(0x1A1A1Du, 0.3).Box(cx, cy, 0.6, 6.4, 3.4, 0.2);
            b.Heat(P(cx, cy, 1.7));
        }

        // ================================================================== DIP and SOIC

        internal static void Dip(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            var p1 = r.Pad("1") ?? pads[0];
            var p2 = r.Pad("2") ?? pads.OrderBy(p => (p.X - p1.X) * (p.X - p1.X) + (p.Y - p1.Y) * (p.Y - p1.Y)).Skip(1).First();
            double ang = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) * 180 / Math.PI - 90;
            using (b.At(Mx.Rz(ang) * Mx.T(p1.X, p1.Y, 0)))
            {
                double ca = Math.Cos(-ang * Math.PI / 180), sa = Math.Sin(-ang * Math.PI / 180);
                var local = pads.Select(p => (Pad: p, X: (p.X - p1.X) * ca - (p.Y - p1.Y) * sa, Y: (p.X - p1.X) * sa + (p.Y - p1.Y) * ca)).ToList();
                double rs = Math.Max(2.54, local.Max(p => p.X)), span = Math.Max(0, local.Max(p => p.Y));
                double cx = rs / 2, cy = span / 2, len = span + 2.2, wid = rs - 1.27, z0 = 0.5, h = 3.3;
                var body = b.Body(0x18181Bu, 0.25);
                body.Tapered(cx, cy, z0, wid - 0.3, len - 0.3, 1.2, -0.15);
                body.Tapered(cx, cy, z0 + 1.2, wid, len, h - 1.2, 0.18);
                b.Heat(P(cx, cy, z0 + h));
                double top = z0 + h + 0.004;
                var dark = b[Mat.Plastic(0x0B0B0Cu, 0.05)];
                using (b.At(cx, cy - len / 2 + 0.18, 0)) dark.Lathe(new[] { new Point(0.85, top), new Point(0, top) }, 16, 0, Math.PI);

                using (b.At(cx - wid / 2 + 0.95, cy - len / 2 + 1.25, 0)) b[Mat.Plastic(0x101012u, 0.45)].Lathe(new[] { new Point(0.42, top), new Point(0, top) }, 14);

                foreach (var (pad, x, y) in local)
                {
                    double s = x < cx ? -1 : 1;
                    double xe = cx + s * (wid / 2 - 0.25);
                    b[Mat.Tin].Strip(new List<Point3D> { P(xe, y, z0 + 1.15), P(x, y, z0 + 1.15), P(x, y, 0.75) }, 1.4, 0.25, V(0, 1, 0));
                    b[Mat.Tin].Strip(new List<Point3D> { P(x, y, 0.85), P(x, y, 0.5), P(x, y, Tail) }, 0.5, 0.25, V(0, 1, 0));
                }

                string text = PartMarking(r, 10);
                TopLabel(b, text, cx, cy - 0.2, top + 0.002, Math.Min(len * 0.78, 2.2 + text.Length * 1.1), Math.Min(wid * 0.34, 2.0), Marking, 90, true);
                TopLabel(b, "2318 PHL", cx + wid * 0.24, cy, top + 0.002, Math.Min(len * 0.5, 7), Math.Min(wid * 0.18, 1.1), Marking, 90);
            }
        }

        internal static void Soic(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            var p1 = r.Pad("1") ?? pads[0];
            var p2 = r.Pad("2") ?? pads.OrderBy(p => (p.X - p1.X) * (p.X - p1.X) + (p.Y - p1.Y) * (p.Y - p1.Y)).Skip(1).First();
            double pitch = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
            double cx = pads.Average(p => p.X), cy = pads.Average(p => p.Y);
            double ang = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) * 180 / Math.PI;
            double rowGap = pads.Max(p => Math.Abs(-(p.X - cx) * Math.Sin(ang * Math.PI / 180) + (p.Y - cy) * Math.Cos(ang * Math.PI / 180))) * 2;
            double bodyW = Math.Max(2.0, rowGap - 2.2);
            Soic(b, cx, cy, 0, pads.Count, pitch, bodyW, ang, PartMarking(r, 10));
            b.Heat(P(cx, cy, 1.6));
        }

        // ================================================================== pin headers

        internal static void Header(PartBuilder b, ModelRequest r, bool female)
        {
            var pins = r.Pads.Where(p => p.IsThroughHole || r.Pads.Count > 0).Select(p => (p.X, p.Y)).ToList();
            if (female)
            {
                FemaleHeaderPins(b, pins, 0.0);
                foreach (var (x, y) in pins) SquarePin(b, x, y, Tail, 1.0, 0.6, Mat.Tin);
                foreach (var pad in r.Pads) b.Anchor(pad.Name, P(pad.X, pad.Y, 8.5), V(0, 0, 1));
            }
            else
            {
                MaleHeaderPins(b, pins, 0.0, 8.5, -3.0);
                foreach (var pad in r.Pads) b.Anchor(pad.Name, P(pad.X, pad.Y, 8.5), V(0, 0, 1));
            }

            if (pins.Count > 0) b.Heat(P(pins.Average(p => p.X), pins.Average(p => p.Y), 2.5));
        }

        // ================================================================== screw terminal

        internal static void Terminal(PartBuilder b, ModelRequest r)
        {
            using (RowFrame(b, r, out var xs))
            {
                double pitch = xs.Count > 1 ? xs[1] - xs[0] : 5.08;
                var side = new List<Point> { new Point(-3.6, 0), new Point(4.0, 0), new Point(4.0, 7.4), new Point(3.0, 10.0), new Point(-3.6, 10.0) };
                var body = b.Body(0x1D64C8u, 0.25);
                var steel = b[Mat.Steel];
                var dark = b[Mat.Plastic(0x0A0A0Bu, 0)];
                for (int i = 0; i < xs.Count; i++)
                {
                    double x = xs[i];
                    using (b.At(Mx.Frame(P(x - pitch / 2 + 0.06, 0, 0), V(0, 1, 0), V(0, 0, 1), V(1, 0, 0))))
                    {
                        body.Extrude(side, 0, pitch - 0.12);
                    }

                    // Screw sitting in its well; wire entry with the clamp cage on the front.
                    dark.CylinderZ(x, -0.3, 10.0, 10.006, 1.95, 20, false, true);
                    steel.CylinderZ(x, -0.3, 9.4, 10.25, 1.6, 20);
                    dark.Box(x, -0.3, 10.27, 2.8, 0.45, 0.06);
                    b[Mat.Nickel].Block(x - 1.75, 4.0, 2.3, x + 1.75, 4.02, 5.9);
                    dark.Block(x - 1.3, 4.02, 2.7, x + 1.3, 4.04, 5.5);
                    LeadDown(b, x, 0, 0.3, 0.5, Mat.Tin);
                    b.Anchor(r.Pads.OrderBy(p => p.X).ThenBy(p => p.Y).Skip(i).FirstOrDefault()?.Name ?? (i + 1).ToString(CultureInfo.InvariantCulture), P(x, 4.4, 4.1), V(0, 1, 0));
                }

                b.Heat(P((xs.First() + xs.Last()) / 2, 0, 10));
            }

            // Anchor names follow the pad order along the row.
            ReanchorRow(b, r);
        }

        private static void ReanchorRow(PartBuilder b, ModelRequest r)
        {
            // The row frame sorted pads by distance from pad 1; re-key anchors by pad name.
            var first = r.Pad("1") ?? r.Pads.OrderBy(p => p.X).ThenBy(p => p.Y).First();
            var ordered = r.Pads.OrderBy(p => (p.X - first.X) * (p.X - first.X) + (p.Y - first.Y) * (p.Y - first.Y)).ToList();
            var anchors = b.Anchors.ToList();
            b.Anchors.Clear();
            for (int i = 0; i < anchors.Count; i++)
            {
                string name = i < ordered.Count ? ordered[i].Name : anchors[i].Pad;
                b.Anchors.Add((name, anchors[i].Position, anchors[i].Direction));
            }
        }

        // ================================================================== switches

        internal static void Tact(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            double cx = pads.Average(p => p.X), cy = pads.Average(p => p.Y);
            bool wide = (pads.Max(p => p.X) - pads.Min(p => p.X)) >= (pads.Max(p => p.Y) - pads.Min(p => p.Y));
            b.Body(0x26262Au, 0.15).BoxZ(cx, cy, 0.15, 6.0, 6.0, 3.3);
            b.Heat(P(cx, cy, 3.5));
            var steel = b[Mat.Steel];
            steel.BoxZ(cx, cy, 3.45, 6.0, 6.0, 0.22);
            foreach (int s in new[] { -1, 1 })
            {
                foreach (int t in new[] { -1, 1 })
                {
                    if (wide) steel.BoxZ(cx + t * 1.9, cy + s * 3.05, 1.7, 1.1, 0.22, 2.0);
                    else steel.BoxZ(cx + s * 3.05, cy + t * 1.9, 1.7, 0.22, 1.1, 2.0);
                }
            }

            b[Mat.Plastic(0x1C1C1Eu, 0.3)].CylinderZ(cx, cy, 3.6, 5.0, 1.75, 24);
            foreach (var pad in pads)
            {
                double ex = wide ? cx + Math.Sign(pad.X - cx) * 2.7 : pad.X, ey = wide ? pad.Y : cy + Math.Sign(pad.Y - cy) * 2.7;
                var path = new List<Point3D> { P(ex, ey, 0.9), P(pad.X, pad.Y, 0.9), P(pad.X, pad.Y, Tail) };
                b[Mat.Tin].Strip(MeshBuilder.Smooth(path, 0.4, 3), 0.7, 0.3, wide ? V(0, 1, 0) : V(1, 0, 0));
            }
        }

        internal static void Toggle(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            double cx = pads.Average(p => p.X), cy = pads.Average(p => p.Y);
            double span = pads.Max(p => Math.Abs(p.X - cx)) * 2 + 4.0;
            double bx = Math.Max(8.0, span);
            b.Body(0x1D1D20u, 0.2).BoxZ(cx, cy, 0.3, bx, 5.4, 5.0);
            b[Mat.Steel].BoxZ(cx, cy, 5.3, bx + 0.4, 5.8, 0.4);
            b[Mat.Nickel].CylinderZ(cx, cy, 5.7, 8.7, 2.5, 20);
            var nut = MeshBuilder.Circle(cx, cy, 3.3, 6, Math.PI / 6);
            b[Mat.Steel].Extrude(nut, 5.7, 6.9);
            b.Heat(P(cx, cy, 5));
            using (b.At(Mx.Ry(18) * Mx.T(cx, cy, 8.7)))
            {
                b[Mat.Chrome].Frustum(0.9, 0.65, 0, 8.0, 14);
                b[Mat.Chrome].Sphere(0, 0, 8.0, 0.95, 12);
            }

            foreach (var pad in pads) FlatLead(b, pad.X, pad.Y, 0.6, 0.0, 1.6, 0.8, 0.4);
        }

        // ================================================================== potentiometers

        internal static void Pot(PartBuilder b, ModelRequest r)
        {
            using (RowFrame(b, r, out var xs))
            {
                double span = xs.Count > 0 ? xs[xs.Count - 1] : 5.0, cx = span / 2, cy = -4.4;
                b.Body(0x2F5DAEu, 0.2).RoundedBox(cx, cy, 0.4, 9.8, 10.2, 6.2, 0.6, 3);
                b.Heat(P(cx, cy, 6.6));
                var nickel = b[Mat.Nickel];
                nickel.BoxZ(cx, cy, 6.6, 9.8, 10.2, 0.35);
                nickel.CylinderZ(cx, cy, 6.9, 11.6, 3.5, 24);
                var knob = b.Rotating("knob", P(cx, cy, 11.6), V(0, 0, 1), P(cx, cy, 21.5));
                var knurl = new List<Point>();
                int teeth = 18;
                for (int i = 0; i < teeth * 2; i++)
                {
                    double a = Math.PI * i / teeth, rr = i % 2 == 0 ? 3.0 : 2.75;
                    knurl.Add(new Point(cx + rr * Math.Cos(a), cy + rr * Math.Sin(a)));
                }

                knob[Mat.Aluminium].Extrude(knurl, 11.6, 21.0);
                knob[Mat.Aluminium].CylinderZ(cx, cy, 21.0, 21.4, 2.75, 24, false, true);
                knob[Mat.Plastic(0x303236u, 0.2)].Box(cx, cy, 21.42, 5.4, 0.7, 0.04);
                foreach (double x in xs) FlatLead(b, x, 0, 0.6, 0.0, 1.2, 0.8, 0.35);
            }
        }

        internal static void Trimmer(PartBuilder b, ModelRequest r)
        {
            using (RowFrame(b, r, out var xs))
            {
                double span = xs.Count > 0 ? xs[xs.Count - 1] : 5.08, cx = span / 2;
                b.Body(0x2152B8u, 0.25).BoxZ(cx, 0, 0.3, 9.5, 4.8, 9.7);
                b.Heat(P(cx, 0, 10));
                double sx = cx - 9.5 / 2 + 1.7;
                var screw = b.Rotating("screw", P(sx, 0, 10.0), V(0, 0, 1), P(sx, 0, 10.6));
                screw[Mat.Brass].CylinderZ(sx, 0, 9.9, 10.6, 1.15, 18);
                screw[Mat.Plastic(0x2A2018u, 0)].Box(sx, 0, 10.62, 2.0, 0.35, 0.04);
                double ohms = r.Num("resistance", 10000);
                FrontLabel(b, ValueCode(ohms, 1), P(cx + 1.5, 2.42, 6.2), V(0, 1, 0), V(0, 0, 1), 3.6, 1.6, Color.FromRgb(0xF0, 0xF0, 0xF0), true);
                FrontLabel(b, "3296W", P(cx + 1.5, 2.42, 3.6), V(0, 1, 0), V(0, 0, 1), 4.4, 1.2, Color.FromRgb(0xF0, 0xF0, 0xF0));
                foreach (double x in xs) LeadDown(b, x, 0, 0.5, 0.25);
            }
        }

        // ================================================================== relay, buzzer

        internal static void Relay(PartBuilder b, ModelRequest r)
        {
            var fp = r.Fp!;
            double cx = (fp.CourtyardMinX + fp.CourtyardMaxX) / 2, cy = (fp.CourtyardMinY + fp.CourtyardMaxY) / 2;
            double sx = Math.Min(19.0, fp.CourtyardMaxX - fp.CourtyardMinX - 0.2), sy = Math.Min(15.5, fp.CourtyardMaxY - fp.CourtyardMinY - 0.2), h = 15.3;
            b[Mat.Plastic(0x174FAEu, 0.2)].BoxZ(cx, cy, 0.0, sx, sy, 1.2);
            b.Body(0x1C63D6u, 0.3).RoundedBox(cx, cy, 1.0, sx - 0.3, sy - 0.3, h - 1.0, 0.7, 3);
            b.Heat(P(cx, cy, h));
            string model = r.Text("model", "SRD-05VDC-SL-C");
            if (!model.StartsWith("SRD", StringComparison.OrdinalIgnoreCase)) model = "SRD-05VDC-SL-C";
            TopLabel(b, "SONGLE", cx, cy - sy * 0.3, h + 0.01, sx * 0.62, 2.6, Silk, 0, true);
            TopLabel(b, model, cx, cy - sy * 0.08, h + 0.01, sx * 0.78, 2.1, Silk, 0, true);
            TopLabel(b, "10A 250VAC  10A 125VAC\n10A 30VDC  10A 28VDC", cx, cy + sy * 0.2, h + 0.01, sx * 0.8, 3.2, Silk);
            TopLabel(b, "CE  ⓒ", cx - sx * 0.25, cy + sy * 0.39, h + 0.01, 4.0, 1.4, Silk);
            foreach (var pad in fp.Pads) b[Mat.Tin].Box(pad.X, pad.Y, (Tail + 0.4) / 2, pad.Name == "1" || pad.Name == "2" ? 0.5 : 1.1, 0.5, 0.4 - Tail);
        }

        internal static void Buzzer(PartBuilder b, ModelRequest r)
        {
            var (pa, pb) = TwoPads(r);
            double cx = (pa.X + pb.X) / 2, cy = (pa.Y + pb.Y) / 2;
            var plus = r.Pad("1") ?? pa;
            const double R = 6.0, H = 9.5;
            using (b.At(cx, cy, 0))
            {
                b[Mat.Plastic(0x101012u, 0.1)].Cylinder(R - 0.2, 0.2, 0.6, 32);
                var pr = new List<Point> { new Point(R, 0.5), new Point(R, H - 0.6), new Point(R - 0.15, H - 0.2), new Point(R - 0.5, H), new Point(1.5, H), new Point(1.5, H), new Point(1.5, H - 0.8), new Point(1.5, H - 0.8), new Point(0, H - 0.8) };
                b.Body(0x18181Au, 0.35).Lathe(pr, 36);
                b.Heat(P(0, 0, H));
            }

            double px = cx + (plus.X - cx) * 0.62, py = cy + (plus.Y - cy) * 0.62;
            TopLabel(b, "+", px, py, H + 0.01, 2.6, 2.6, Silk, 0, true);
            foreach (var pad in new[] { pa, pb }) LeadDown(b, pad.X, pad.Y, 0.5, 0.3);
        }

        // ================================================================== off-board connections

        /// <summary>Wire leads soldered into the pads (battery, motor, lamp, supply mounted off the board).</summary>
        internal static void Wires(PartBuilder b, ModelRequest r)
        {
            var pads = r.Pads.ToList();
            uint[] colours = r.Id switch
            {
                "signal-generator" => new uint[] { 0xE8C21Cu, 0x1E1E22u },
                "lamp" => new uint[] { 0xE8E8E2u, 0xE8E8E2u },
                "dc-motor" => new uint[] { 0xC8282Cu, 0x1E1E22u },
                _ => new uint[] { 0xC8282Cu, 0x1E1E22u }
            };
            double cx = pads.Average(p => p.X), cy = pads.Average(p => p.Y);
            for (int i = 0; i < pads.Count; i++)
            {
                var pad = pads[i];
                double off = (pad.X - cx) * 0.25;
                var ins = b[Mat.Plastic(colours[i % colours.Length], 0.35, 30)];
                b[Mat.Tin].Rod(P(pad.X, pad.Y, Tail), P(pad.X, pad.Y, 1.2), 0.42, 8);
                using (b.At(pad.X, pad.Y, 0)) b[Mat.Solder].Frustum(1.0, 0.45, 0, 0.7, 14, false, false);
                var path = MeshBuilder.Smooth(new[] { P(pad.X, pad.Y, 0.9), P(pad.X, pad.Y, 3.4), P(pad.X + off, pad.Y - 4.5, 5.0), P(pad.X + off * 2, pad.Y - 20, 4.2) }, 2.5, 8);
                ins.Tube(path, 0.8, 12, true, false);
                var end = path[path.Count - 1];
                b[Mat.Copper].Rod(end, end + V(off * 0.02, -0.8, 0), 0.42, 8);
                b.Anchor(pad.Name, end + V(0, -0.8, 0), V(0, -1, 0));
            }

        }

        /// <summary>Servo connector plugged onto a 3-pin header (the servo itself is off the board).</summary>
        internal static void ServoPlug(PartBuilder b, ModelRequest r)
        {
            var pins = r.Pads.Select(p => (p.X, p.Y)).ToList();
            MaleHeaderPins(b, pins, 0.0, 8.5, -3.0);
            double cx = pins.Average(p => p.X), cy = pins.Average(p => p.Y);
            bool alongY = (pins.Max(p => p.Y) - pins.Min(p => p.Y)) >= (pins.Max(p => p.X) - pins.Min(p => p.X));
            double lx = alongY ? 2.5 : pins.Count * 2.54, ly = alongY ? pins.Count * 2.54 : 2.5;
            b.Body(0x1B1B1Du, 0.15).BoxZ(cx, cy, 2.6, lx, ly, 12.0);
            b.Heat(P(cx, cy, 14.6));
            var colourOf = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) { ["GND"] = 0x5A3A22u, ["V+"] = 0xC8282Cu, ["SIG"] = 0xF08A1Eu };
            foreach (var pad in r.Pads)
            {
                string pin = r.Def?.Pins.FirstOrDefault(p => string.Equals(r.Def.ResolvePad(p, r.P), pad.Name, StringComparison.OrdinalIgnoreCase))?.Name ?? pad.Name;
                uint col = colourOf.TryGetValue(pin, out var c) ? c : 0x808080u;
                var path = MeshBuilder.Smooth(new[] { P(pad.X, pad.Y, 13.5), P(pad.X, pad.Y, 17.5), P(pad.X + 4, pad.Y, 19.5), P(pad.X + 26, pad.Y, 19.0) }, 3.0, 8);
                b[Mat.Plastic(col, 0.3, 25)].Tube(path, 0.55, 10, false, true);
                b.Anchor(pad.Name, path[path.Count - 1], V(1, 0, 0));
            }
        }
    }
}
