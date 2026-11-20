using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// [objs] Ready-made wheels, casters and propellers, and free-standing light sources
    /// (RGB LED, power LED on a star, 12 V LED strip), from their datasheet dimensions.
    /// Wheels: centred on the origin, axle along +Y (the side facing away from the motor).
    /// Propellers: hub bottom on z = 0, spinning about +Z. Ball caster: standing on its ball.
    /// </summary>
    internal static partial class Gen
    {
        // ================================================================== dispatch

        internal static void ReadyPart(PartBuilder b, ReadyPartSpec s, string? colour = null)
        {
            double d = s.Diameter * 1000, w = s.Width * 1000;
            uint hub = ParseRgb(colour ?? s.Color, ParseRgb(s.Color, 0x2A2A2Eu));
            switch (s.Style)
            {
                case "small": SmallWheel(b, d, w, hub); break;
                case "n20": N20Wheel(b, d, w, hub); break;
                case "offroad": OffroadWheel(b, d, w, hub); break;
                case "omni": OmniWheel(b, s, hub); break;
                case "mecanum": MecanumWheel(b, s, hub); break;
                case "caster": BallCaster(b, s); break;
                case "prop": PropellerBlades(b, s, hub); break;
                default: Wheel(b, d, w, "tt"); break;
            }
        }

        /// <summary>Light-source parts with their own models (any footprint).</summary>
        internal static bool TryLightModel(PartBuilder b, ModelRequest r)
        {
            switch (r.Id)
            {
                case "led-rgb": RgbLed(b, r); return true;
                case "led-power": PowerLed(b, r); return true;
                case "led-strip": LedStrip(b, r); return true;
                default: return false;
            }
        }

        private static uint ParseRgb(string? hex, uint fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            string h = hex!.Trim().TrimStart('#');
            return h.Length == 6 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        /// <summary>D-shaped bore faces on both sides of a hub (flat at x = ±flat).</summary>
        private static void DBore(PartBuilder b, double r, double flat, double z0, double z1)
        {
            var dd = MeshBuilder.Circle(0, 0, r, 18).Select(p => new Point(Math.Max(-flat, Math.Min(flat, p.X)), p.Y)).ToList();
            b[Mat.Plastic(0x0A0A0Cu, 0)].Extrude(dd, z1 - 0.01, z1 + 0.01, false, true, false);
            b[Mat.Plastic(0x0A0A0Cu, 0)].Extrude(dd, z0 - 0.01, z0 + 0.01, false, false, true);
        }

        /// <summary>Rubber tyre section revolved about Z: flat tread with rounded shoulders.</summary>
        private static void Tyre(PartBuilder b, double R, double ri, double w, double shoulder, uint rgb = 0x1B1B1Cu)
        {
            double s = Math.Min(shoulder, w / 3);
            b[Mat.Plastic(rgb, 0.04, 8)].Lathe(new[]
            {
                new Point(ri, -w / 2), new Point(R - s, -w / 2), new Point(R - s * 0.3, -w / 2 + s * 0.3), new Point(R, -w / 2 + s),
                new Point(R, w / 2 - s), new Point(R - s * 0.3, w / 2 - s * 0.3), new Point(R - s, w / 2), new Point(ri, w / 2),
                new Point(ri, w / 2), new Point(ri, -w / 2)
            }, 56);
        }

        // ================================================================== wheels

        /// <summary>43 × 19 mm robot wheel: soft tyre with fine circumferential grooves, black six-hole hub, 3 mm D bore.</summary>
        private static void SmallWheel(PartBuilder b, double d, double w, uint hub)
        {
            double R = d / 2, ri = R - 5.5;
            using (b.At(Mx.Rx(-90)))
            {
                Tyre(b, R, ri, w, 2.4);
                var groove = b[Mat.Plastic(0x0B0B0Cu, 0.02)];
                foreach (double z in new[] { -w / 4, 0.0, w / 4 }) groove.Torus(R - 0.1, 0.32, 72, 6, z);
                var h = b[Mat.Plastic(hub, 0.2)];
                h.Lathe(new[] { new Point(ri - 1.6, -w / 2 + 1.0), new Point(ri + 0.05, -w / 2 + 1.0), new Point(ri + 0.05, -w / 2 + 1.0), new Point(ri + 0.05, w / 2 - 1.0),
                    new Point(ri + 0.05, w / 2 - 1.0), new Point(ri - 1.6, w / 2 - 1.0), new Point(ri - 1.6, w / 2 - 1.0), new Point(ri - 1.6, -w / 2 + 1.0) }, 48);
                h.Cylinder(ri - 1.5, -1.3, 1.3, 48);
                h.Cylinder(4.6, -w / 2 + 0.6, w / 2 - 0.6, 24);
                var hole = b[Mat.Plastic(0x070708u, 0)];
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3, rr = (ri + 4.6) / 2;
                    hole.CylinderZ(rr * Math.Cos(a), rr * Math.Sin(a), -1.32, 1.32, (ri - 4.6) * 0.28, 16);
                }

                DBore(b, 1.5, 1.0, -w / 2 + 0.6, w / 2 - 0.6);
            }
        }

        /// <summary>34 × 7 mm N20 wheel: O-ring-like tyre on a white four-spoke hub with a long boss toward the motor.</summary>
        private static void N20Wheel(PartBuilder b, double d, double w, uint hub)
        {
            double R = d / 2, rt = w / 2;
            using (b.At(Mx.Rx(-90)))
            {
                var tyre = new List<Point>();
                for (int i = 0; i <= 16; i++)
                {
                    double a = -Math.PI / 2 + Math.PI * i / 16;
                    tyre.Add(new Point(R - rt + rt * Math.Cos(a), rt * Math.Sin(a)));
                }

                b[Mat.Plastic(0x1B1B1Cu, 0.05, 8)].Lathe(tyre, 56);
                double ri = R - rt - 0.2;
                var h = b[Mat.Plastic(hub, 0.25)];
                h.Lathe(new[] { new Point(ri - 1.6, -w / 2 + 0.6), new Point(ri + 0.4, -w / 2 + 0.6), new Point(ri + 0.4, -w / 2 + 0.6), new Point(ri + 0.4, w / 2 - 0.6),
                    new Point(ri + 0.4, w / 2 - 0.6), new Point(ri - 1.6, w / 2 - 0.6), new Point(ri - 1.6, w / 2 - 0.6), new Point(ri - 1.6, -w / 2 + 0.6) }, 48);
                for (int i = 0; i < 4; i++)
                {
                    using (b.At(Mx.Rz(90.0 * i + 45))) h.Box((3.5 + ri - 1.6) / 2, 0, 0, ri - 1.6 - 3.5 + 0.4, 2.6, 1.6);
                }

                h.Cylinder(3.5, -6.0, 3.5, 24);
                DBore(b, 1.5, 1.0, -6.0, 3.5);
            }
        }

        /// <summary>85 × 35 mm off-road tyre: staggered knobs on a soft carcass, five-spoke rim, 12 mm hex hub.</summary>
        private static void OffroadWheel(PartBuilder b, double d, double w, uint rim)
        {
            double R = d / 2, kh = 3.4, Rc = R - kh, ri = R - 14;
            using (b.At(Mx.Rx(-90)))
            {
                Tyre(b, Rc, ri, w, 6.0);
                // Knobs: centre block and two shoulder blocks per row, shoulder rows offset by half a pitch.
                var knob = b[Mat.Plastic(0x1E1E20u, 0.04, 8)];
                const int rows = 22;
                for (int i = 0; i < rows; i++)
                {
                    double a = 360.0 * i / rows;
                    using (b.At(Mx.Rz(a)))
                    {
                        knob.Box(Rc + kh / 2 - 0.2, 0, 0, kh + 0.4, 6.5, 9.0);
                    }

                    using (b.At(Mx.Rz(a + 180.0 / rows)))
                    {
                        foreach (int side in new[] { -1, 1 })
                        {
                            knob.Box(Rc + kh / 2 - 0.4, 0, side * (w / 2 - 6.0), kh, 7.0, 9.5);
                            // The shoulder lug wraps onto the sidewall.
                            knob.Box(Rc - 2.2, 0, side * (w / 2 - 0.4), 5.0, 6.0, 2.2);
                        }
                    }
                }

                var metal = b[Mat.Metal(rim, 0.75, 40)];
                metal.Lathe(new[] { new Point(ri - 2.2, -w / 2 + 2.5), new Point(ri + 0.1, -w / 2 + 2.5), new Point(ri + 0.1, -w / 2 + 2.5), new Point(ri + 0.1, w / 2 - 2.5),
                    new Point(ri + 0.1, w / 2 - 2.5), new Point(ri - 2.2, w / 2 - 2.5), new Point(ri - 2.2, w / 2 - 2.5), new Point(ri - 2.2, -w / 2 + 2.5) }, 56);
                metal.Torus(ri - 0.6, 1.6, 56, 10, w / 2 - 2.5);
                metal.Torus(ri - 0.6, 1.6, 56, 10, -w / 2 + 2.5);
                for (int i = 0; i < 5; i++)
                {
                    using (b.At(Mx.Rz(72.0 * i))) metal.Box((8.0 + ri - 2.2) / 2, 0, w / 4, ri - 2.2 - 8.0 + 0.6, 7.0, 4.0);
                }

                metal.Cylinder(9.0, w / 4 - 3.0, w / 4 + 2.0, 32);
                var hex = MeshBuilder.Circle(0, 0, 6.9, 6, Math.PI / 6);
                b[Mat.Plastic(0x18181Au, 0.3)].Extrude(hex, -w / 2 + 2.0, w / 4 + 2.3, false);
                b[Mat.Steel].CylinderZ(0, 0, w / 4 + 2.3, w / 4 + 3.3, 4.2, 6);
                DBore(b, 3.0, 2.5, -w / 2 + 2.0, w / 4 + 3.3);
            }
        }

        /// <summary>Barrel roller about its local Z whose outer surface follows the wheel's circle (R from the wheel axis).</summary>
        private static void Roller(PartBuilder b, Material m, double R, double rc, double half, double tilt)
        {
            var pr = new List<Point> { new Point(0, -half) };
            const int n = 10;
            for (int i = 0; i <= n; i++)
            {
                double z = -half + 2 * half * i / n;
                double t = z * Math.Sin(tilt);
                double r = Math.Max(1.2, Math.Sqrt(Math.Max(0, R * R - t * t)) - rc);
                pr.Add(new Point(r, z));
            }

            pr.Add(new Point(0, half));
            b[m].Lathe(pr, 18);
        }

        /// <summary>60 mm double omni wheel: 2 × 9 rubber rollers tangent to the rim, plastic plates, D bore.</summary>
        private static void OmniWheel(PartBuilder b, ReadyPartSpec s, uint plate)
        {
            double R = s.Diameter * 500, w = s.Width * 1000, rr = 6.2, rc = R - rr;
            int n = Math.Max(4, s.Rollers / 2);
            double half = rc * Math.Tan(Math.PI / n) * 0.9;
            var rubber = Mat.Plastic(0x3A3A3Fu, 0.08, 10);
            var pm = Mat.Plastic(plate, 0.25, 25);
            using (b.At(Mx.Rx(-90)))
            {
                foreach (var (zr, phase) in new[] { (-w / 4, 0.0), (w / 4, Math.PI / n) })
                {
                    for (int k = 0; k < n; k++)
                    {
                        double a = phase + 2 * Math.PI * k / n;
                        var u = V(Math.Cos(a), Math.Sin(a), 0);
                        var t = V(-Math.Sin(a), Math.Cos(a), 0);
                        var c = P(rc * u.X, rc * u.Y, zr);
                        using (b.At(Mx.Frame(c, u, Vector3D.CrossProduct(t, u), t))) Roller(b, rubber, R, rc, half, Math.PI / 2);
                        // Axle pins into the plate brackets at both roller ends.
                        var e0 = c - t * half;
                        var e1 = c + t * half;
                        b[Mat.Steel].Rod(e0 - t * 1.2, e0 + t * 0.4, 0.9, 8);
                        b[Mat.Steel].Rod(e1 - t * 0.4, e1 + t * 1.2, 0.9, 8);
                    }

                    // Plate of this roller row with brackets between the rollers.
                    var pl = b[pm];
                    pl.Cylinder(rc - 3.2, zr - 1.4, zr + 1.4, 48);
                    for (int k = 0; k < n; k++)
                    {
                        double a = phase + 2 * Math.PI * (k + 0.5) / n;
                        using (b.At(Mx.Rz(a * 180 / Math.PI))) pl.Box(rc - 1.6, 0, zr, 5.0, 3.0, 6.0);
                    }
                }

                b[pm].Cylinder(rc - 7.5, -w / 4, w / 4, 40);
                b[Mat.Plastic(0x1E1E21u, 0.3)].Cylinder(6.0, -w / 2 + 1.0, w / 2 - 1.0, 24);
                DBore(b, 2.75, 1.85, -w / 2 + 1.0, w / 2 - 1.0);
            }
        }

        /// <summary>65 mm mecanum wheel: ten rollers at 45° between two metal side plates, D bore.</summary>
        private static void MecanumWheel(PartBuilder b, ReadyPartSpec s, uint plate)
        {
            double R = s.Diameter * 500, w = s.Width * 1000, rr = 7.0, rc = R - rr;
            int n = Math.Max(6, s.Rollers);
            double tilt = s.RollerAngleDeg * Math.PI / 180;
            double half = Math.Min((w / 2 - 2.0) / Math.Cos(tilt), rc * Math.Tan(Math.PI / n) / Math.Sin(tilt) * 1.25);
            var rubber = Mat.Plastic(0x2A2A2Eu, 0.1, 12);
            var metal = Mat.Metal(plate, 0.8, 45);
            using (b.At(Mx.Rx(-90)))
            {
                for (int k = 0; k < n; k++)
                {
                    double a = 2 * Math.PI * k / n;
                    var u = V(Math.Cos(a), Math.Sin(a), 0);
                    var t = V(-Math.Sin(a), Math.Cos(a), 0);
                    // Roller axis leans 45° out of the wheel plane; the hand decides which way (A / B).
                    var ax = t * Math.Sin(tilt) - V(0, 0, 1) * (s.Hand * Math.Cos(tilt));
                    ax.Normalize();
                    var c = P(rc * u.X, rc * u.Y, 0);
                    using (b.At(Mx.Frame(c, u, Vector3D.CrossProduct(ax, u), ax))) Roller(b, rubber, R, rc, half, tilt);
                    // Bent brackets from both side plates to the roller axle ends.
                    foreach (int e in new[] { -1, 1 })
                    {
                        var end = c + ax * (e * (half + 0.6));
                        var toPlate = P(end.X - u.X * 2.5, end.Y - u.Y * 2.5, Math.Sign(end.Z) * (w / 2 - 1.6));
                        b[metal].Rod(end, toPlate, 1.3, 8);
                        b[Mat.Steel].Rod(end - ax * (e * 1.2), end + ax * (e * 0.4), 1.0, 8);
                    }
                }

                foreach (int side in new[] { -1, 1 })
                {
                    var pl = b[metal];
                    pl.Cylinder(rc - 3.0, side * (w / 2 - 3.0), side * (w / 2 - 0.4), 48);
                    // Lightening holes.
                    for (int i = 0; i < 5; i++)
                    {
                        double a = 2 * Math.PI * i / 5;
                        b[Mat.Plastic(0x0B0B0Cu, 0)].CylinderZ((rc - 3.0 + 7.5) / 2 * Math.Cos(a), (rc - 3.0 + 7.5) / 2 * Math.Sin(a),
                            side * (w / 2 - 0.42), side * (w / 2 - 0.38), 3.6, 14);
                    }
                }

                b[Mat.Plastic(0x1E1E21u, 0.3)].Cylinder(7.5, -w / 2 + 0.5, w / 2 - 0.5, 28);
                DBore(b, 2.75, 1.85, -w / 2 + 0.5, w / 2 - 0.5);
            }
        }

        /// <summary>Ball caster: chrome steel ball in a black cup with a two-hole mounting flange on top.</summary>
        private static void BallCaster(PartBuilder b, ReadyPartSpec s)
        {
            double rb = s.Diameter * 500, h = s.Width * 1000, rh = s.HousingDiameter * 500;
            b[Mat.Chrome].Sphere(0, 0, rb, rb, 28);
            var body = b.Body(0x202024u, 0.25);
            double cupR = rb + 1.6, z0 = rb * 0.7;
            var cup = new List<Point> { new Point(rb * 0.86, z0), new Point(cupR, z0 + 0.8) };
            cup.Add(new Point(cupR, h - 2.4));
            cup.Add(new Point(cupR, h - 2.4));
            cup.Add(new Point(0, h - 2.4));
            body.Lathe(cup, 36);
            // Flange: rounded plate across the full housing width, two screw holes.
            var outline = new List<Point>();
            double fx = rh, fy = rh * 0.42;
            for (int i = 0; i <= 18; i++)
            {
                double a = -Math.PI / 2 + Math.PI * i / 18;
                outline.Add(new Point(fx - fy + fy * Math.Cos(a), fy * Math.Sin(a)));
            }

            for (int i = 0; i <= 18; i++)
            {
                double a = Math.PI / 2 + Math.PI * i / 18;
                outline.Add(new Point(-fx + fy + fy * Math.Cos(a), fy * Math.Sin(a)));
            }

            body.Extrude(outline, h - 2.4, h, false);
            foreach (int sx in new[] { -1, 1 })
            {
                b[Mat.Plastic(0x060607u, 0)].CylinderZ(sx * (fx - fy), 0, h - 0.01, h + 0.01, 1.6, 14);
                b[Mat.Steel].CylinderZ(sx * (fx - fy), 0, h + 0.01, h + 0.6, 2.6, 6);
            }

            b.Heat(P(0, 0, h));
        }

        // ================================================================== propellers

        /// <summary>
        /// Hobby propeller with real blade geometry: blade angle β(r) = atan(pitch / 2πr), chord tapering to
        /// a rounded tip, cambered thin section (flat-bottomed, thicker at the root), handed for CCW/CW.
        /// The blades lie at mid-hub height; the hub has the shaft bore and, for 5" props, a prop nut.
        /// </summary>
        private static void PropellerBlades(PartBuilder b, ReadyPartSpec s, uint rgb)
        {
            double R = s.Diameter * 500, hubR = s.HubDiameter * 500, hh = s.Width * 1000, pitch = Math.Max(1, s.Pitch * 1000);
            int blades = Math.Max(2, s.Blades), hand = s.Hand >= 0 ? 1 : -1;
            bool micro = s.Diameter < 0.08;
            var mat = Mat.Plastic(rgb, 0.4, 40);
            var m = b[mat];
            m.CylinderZ(0, 0, 0, hh, hubR, 28);
            b[Mat.Plastic(0x050506u, 0)].CylinderZ(0, 0, hh - 0.005, hh + 0.01, Math.Max(0.45, s.Bore * 500), 14);
            if (!micro)
            {
                var nut = MeshBuilder.Circle(0, 0, 4.6, 6, Math.PI / 6);
                b[Mat.Chrome].Extrude(nut, hh, hh + 4.0, false);
                b[Mat.Chrome].Lathe(new[] { new Point(3.9, hh + 4.0), new Point(3.2, hh + 5.6), new Point(1.6, hh + 6.6), new Point(0, hh + 6.9) }, 24);
            }

            double cmax = R * (micro ? 0.26 : blades >= 3 ? 0.17 : 0.2);
            double zc = hh / 2, r0 = hubR * 0.8;
            const int stations = 16, around = 10;
            for (int k = 0; k < blades; k++)
            {
                using (b.At(Mx.Rz(360.0 * k / blades)))
                {
                    var sections = new List<Point3D[]>();
                    for (int i = 0; i < stations; i++)
                    {
                        double x = (double)i / (stations - 1), r = r0 + x * (R - r0);
                        double chord = cmax * (0.58 + 0.42 * Math.Sin(Math.PI * Math.Min(1, x / 0.9)));
                        if (x > 0.78) chord *= 1 - 0.7 * Math.Pow((x - 0.78) / 0.22, 2);
                        chord = Math.Max(0.6, chord);
                        double beta = Math.Atan(pitch / (2 * Math.PI * Math.Max(r, 1)));
                        double thick = Math.Max(0.35, chord * (0.11 - 0.06 * x));
                        double camber = chord * 0.045;
                        // Section frame in (y, z): chord direction leading → trailing edge, and the upper (suction) normal.
                        var ec = new Vector(-hand * Math.Cos(beta), -Math.Sin(beta));
                        var en = new Vector(-hand * Math.Sin(beta), Math.Cos(beta));
                        var le = -0.35 * chord * ec;
                        var sec = new Point3D[around];
                        for (int j = 0; j < around; j++)
                        {
                            // Upper surface LE→TE for the first half, lower TE→LE for the second.
                            int half = around / 2;
                            double sc, nn;
                            if (j < half)
                            {
                                sc = (1 - Math.Cos(Math.PI * j / (half - 1))) / 2;
                                nn = camber * 4 * sc * (1 - sc) + thick * 0.5 * Math.Sqrt(Math.Max(0, sc)) * (1 - sc) * 1.9;
                            }
                            else
                            {
                                sc = (1 + Math.Cos(Math.PI * (j - half) / (half - 1))) / 2;
                                nn = camber * 4 * sc * (1 - sc) - thick * 0.12 * Math.Sqrt(Math.Max(0, sc)) * (1 - sc) * 1.9;
                            }

                            var q = le + ec * (sc * chord) + en * nn;
                            sec[j] = P(r, q.X, zc + q.Y);
                        }

                        sections.Add(sec);
                    }

                    for (int i = 0; i + 1 < sections.Count; i++)
                    {
                        var a = sections[i];
                        var c = sections[i + 1];
                        var mid = Centroid(a);
                        for (int j = 0; j < around; j++)
                        {
                            int jn = (j + 1) % around;
                            var n = Vector3D.CrossProduct(c[jn] - a[j], c[j] - a[jn]);
                            var outward = (a[j] - mid) + (a[jn] - mid);
                            outward.X = 0;
                            if (Vector3D.DotProduct(n, outward) < 0) n = -n;
                            m.Face(a[j], a[jn], c[jn], c[j], n);
                        }
                    }

                    // Rounded tip cap.
                    var tip = sections[sections.Count - 1];
                    var tc = Centroid(tip);
                    for (int j = 0; j < around; j++)
                    {
                        var p0 = tip[j];
                        var p1 = tip[(j + 1) % around];
                        var nrm = Vector3D.CrossProduct(p1 - p0, tc - p0);
                        if (nrm.X < 0) m.Triangle(p0, tc, p1);
                        else m.Triangle(p0, p1, tc);
                    }
                }
            }
        }

        private static Point3D Centroid(Point3D[] pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts)
            {
                x += p.X;
                y += p.Y;
                z += p.Z;
            }

            return P(x / pts.Length, y / pts.Length, z / pts.Length);
        }

        // ================================================================== light sources

        /// <summary>5 mm four-lead RGB LED, diffused lens, three dies on the lead frame (common cathode = longest lead).</summary>
        private static void RgbLed(PartBuilder b, ModelRequest r)
        {
            string[] pins = { "R", "K", "G", "B" };
            var pads = pins.Select((p, i) => PadOfPin(r, p, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            var pos = new Point[4];
            for (int i = 0; i < 4; i++)
            {
                var pad = r.Pad(pads[i]);
                pos[i] = pad != null ? new Point(pad.X, -pad.Y) : new Point((i - 1.5) * 2.54, 0);
            }

            double cx = pos.Average(p => p.X), cy = pos.Average(p => p.Y), zb = 1.0, total = 8.6, rad = 2.5, zt = zb + total;
            var row = new Vector(pos[3].X - pos[0].X, pos[3].Y - pos[0].Y);
            if (row.Length < 1e-6) row = new Vector(1, 0);
            row.Normalize();
            var tin = b[Mat.Tin];
            for (int i = 0; i < 4; i++)
            {
                // Leads fan in from the 2.54 mm pads to 1 mm inside the epoxy.
                double xi = cx + row.X * (i - 1.5), yi = cy + row.Y * (i - 1.5);
                tin.Strip(MeshBuilder.Smooth(new[] { P(pos[i].X, pos[i].Y, Tail), P(pos[i].X, pos[i].Y, zb - 0.6), P(xi, yi, zb + 0.3) }, 0.4, 4), 0.45, 0.45, V(-row.Y, row.X, 0));
                tin.Box(xi, yi, zb + 1.8, 0.45, 0.45, 3.0);
                b.Anchor(pads[i], P(pos[i].X, pos[i].Y, Tail), V(0, 0, -1));
            }

            // Cathode anvil with the three dies.
            tin.Box(cx - 0.5, cy, zb + 3.5, 1.6, 0.5, 0.3);
            b[Mat.Plastic(0xE0302Au, 0.5)].Box(cx - 1.0, cy, zb + 3.75, 0.32, 0.32, 0.2);
            b[Mat.Plastic(0x30D060u, 0.5)].Box(cx - 0.5, cy, zb + 3.75, 0.32, 0.32, 0.2);
            b[Mat.Plastic(0x3070F0u, 0.5)].Box(cx, cy, zb + 3.75, 0.32, 0.32, 0.2);
            var lens = b.Lens(Mat.C(0xF0F0ECu, 215), Colors.White);
            var outline = MeshBuilder.Circle(cx, cy, 2.9, 32).Select(p => new Point(Math.Max(p.X, cx - 2.9 + 0.55), p.Y)).ToList();
            lens.Extrude(outline, zb, zb + 1.0, true);
            var pr = new List<Point> { new Point(rad, zb + 1.0), new Point(rad, zt - rad) };
            for (int i = 1; i <= 9; i++)
            {
                double ang = Math.PI / 2 * i / 9;
                pr.Add(new Point(rad * Math.Cos(ang), zt - rad + rad * Math.Sin(ang)));
            }

            pr[pr.Count - 1] = new Point(0, zt);
            using (b.At(cx, cy, 0)) lens.Lathe(pr, 28);
            b.Heat(P(cx, cy, zt - 1.5));
        }

        private static Color LightOf(string colour) => (colour ?? "white").ToLowerInvariant() switch
        {
            "red" or "kırmızı" => Color.FromRgb(255, 60, 40),
            "green" or "yeşil" => Color.FromRgb(70, 255, 110),
            "blue" or "mavi" => Color.FromRgb(80, 130, 255),
            "warm" or "sıcak" => Color.FromRgb(255, 214, 160),
            "yellow" or "sarı" => Color.FromRgb(255, 225, 60),
            _ => Color.FromRgb(255, 255, 250)
        };

        /// <summary>1 W / 3 W LED: ceramic emitter with a silicone dome on a 20 mm aluminium star, leads soldered on (3 W: finned heat sink below).</summary>
        private static void PowerLed(PartBuilder b, ModelRequest r)
        {
            string pa = PadOfPin(r, "A", "1"), pk = PadOfPin(r, "K", "2");
            bool sink = r.Num("rth", 40) < 35;
            double z0 = 0;
            if (sink)
            {
                var al = b[Mat.Aluminium];
                al.BoxZ(0, 0, 0, 22, 22, 2.5);
                for (int i = 0; i < 6; i++) al.BoxZ(-9.5 + i * 3.8, 0, 2.5, 1.4, 22, 9.0);
                al.BoxZ(0, 0, 11.5, 22, 22, 1.5);
                z0 = 13.0;
            }

            // Star: a 20 mm disc with six notches, white solder mask on aluminium.
            var star = new List<Point>();
            for (int i = 0; i < 96; i++)
            {
                double a = 2 * Math.PI * i / 96;
                double notch = Math.Pow(Math.Max(0, Math.Cos(6 * (a - Math.PI / 6))), 10);
                double rr = 10.0 - 1.9 * notch;
                star.Add(new Point(rr * Math.Cos(a), rr * Math.Sin(a)));
            }

            b[Mat.Aluminium].Extrude(star, z0, z0 + 1.5, false);
            b.Body(0xF2F2EEu, 0.25).Extrude(star.Select(p => new Point(p.X * 0.985, p.Y * 0.985)).ToList(), z0 + 1.5, z0 + 1.56, false, true, false);
            var copper = b[Mat.Plastic(0xC98A4Au, 0.5, 40)];
            foreach (int sx in new[] { -1, 1 }) copper.BoxZ(sx * 6.4, 0, z0 + 1.56, 3.0, 4.0, 0.08);
            // Emitter: ceramic body, phosphor, silicone dome.
            string colour = r.Text("color", "white");
            b[Mat.Plastic(0xF4F2EAu, 0.35)].BoxZ(0, 0, z0 + 1.56, 5.5, 5.5, 0.9);
            b[Mat.Plastic(colour == "white" || colour == "warm" ? 0xE8D070u : 0xD8D8D0u, 0.3)].CylinderZ(0, 0, z0 + 2.46, z0 + 2.5, 1.6, 20);
            var lens = b.Lens(Mat.C(0xF8F8F4u, 110), LightOf(colour));
            var dome = new List<Point>();
            for (int i = 0; i <= 10; i++)
            {
                double a = Math.PI / 2 * i / 10;
                dome.Add(new Point(2.75 * Math.Cos(a), z0 + 2.46 + 2.75 * Math.Sin(a)));
            }

            dome[dome.Count - 1] = new Point(0, z0 + 2.46 + 2.75);
            lens.Lathe(dome, 28);
            // Leads soldered to the pads, running off to +x.
            // Leads soldered to the pads, each running off its own side.
            foreach (var (colourRgb, pad, sx) in new[] { (0xC8282Cu, pa, 1), (0x1E1E22u, pk, -1) })
            {
                var start = P(sx * 6.4, 0, z0 + 1.9);
                var path = MeshBuilder.Smooth(new[] { start, P(sx * 9.5, 0, z0 + 2.4), P(sx * 16.0, 0, z0 + 2.0), P(sx * 34.0, 0, Math.Max(1.0, z0 * 0.5)) }, 2.0, 6);
                b[Mat.Plastic(colourRgb, 0.35, 30)].Tube(path, 0.6, 8, true, false);
                b[Mat.Solder].Sphere(start.X, start.Y, start.Z - 0.1, 0.9, 10);
                var end = path[path.Count - 1];
                b[Mat.Copper].Rod(end, end + V(sx * 2.0, 0, 0), 0.35, 6);
                b.Anchor(pad, end + V(sx * 2.0, 0, 0), V(sx, 0, 0));
            }

            b.Heat(P(0, 0, z0 + 5.2));
        }

        /// <summary>12 V 5050 LED strip, 10 mm wide: three LEDs and a resistor per 50 mm section, leads at the start.</summary>
        private static void LedStrip(PartBuilder b, ModelRequest r)
        {
            int n = Math.Max(1, Math.Min(10, (int)Math.Round(r.Num("sections", 3))));
            double len = n * 50.0, wid = 10.0, t = 0.3;
            string colour = r.Text("color", "white");
            b.Body(0xF4F4F0u, 0.2).BoxZ(len / 2, 0, 0, len, wid, t);
            var copper = b[Mat.Plastic(0xD9A458u, 0.5, 40)];
            var led = b[Mat.Plastic(0xF8F8F4u, 0.45, 40)];
            var res = b[Mat.Plastic(0x1A1A1Cu, 0.3)];
            bool white = colour == "white" || colour == "warm";
            var lens = b.Lens(white ? Mat.C(0xF0DC80u, 230) : Mat.C(0xF4F0F0u, 200), LightOf(colour));
            for (int s = 0; s < n; s++)
            {
                double x0 = s * 50.0;
                if (s > 0)
                {
                    // Cut line with its solder pads.
                    copper.BoxZ(x0 - 1.8, -2.6, t, 2.4, 3.0, 0.05);
                    copper.BoxZ(x0 + 1.8, -2.6, t, 2.4, 3.0, 0.05);
                    copper.BoxZ(x0 - 1.8, 2.6, t, 2.4, 3.0, 0.05);
                    copper.BoxZ(x0 + 1.8, 2.6, t, 2.4, 3.0, 0.05);
                }

                for (int k = 0; k < 3; k++)
                {
                    double x = x0 + 8.33 + k * 16.67;
                    led.BoxZ(x, 0, t, 5.0, 5.0, 1.5);
                    lens.CylinderZ(x, 0, t + 1.5, t + 1.62, 2.0, 20);
                }

                res.BoxZ(x0 + 42.0, 2.9, t, 1.6, 0.8, 0.45);
            }

            copper.BoxZ(2.0, -2.6, t, 2.4, 3.0, 0.05);
            copper.BoxZ(2.0, 2.6, t, 2.4, 3.0, 0.05);
            string pp = PadOfPin(r, "+", "1"), pm = PadOfPin(r, "-", "2");
            foreach (var (dy, colourRgb, pad) in new[] { (-2.6, 0xC8282Cu, pp), (2.6, 0x1E1E22u, pm) })
            {
                var path = MeshBuilder.Smooth(new[] { P(2.0, dy, t + 0.4), P(-6.0, dy, 1.2), P(-26.0, dy * 1.6, 0.8) }, 2.0, 6);
                b[Mat.Plastic(colourRgb, 0.35, 30)].Tube(path, 0.55, 8, true, false);
                var end = path[path.Count - 1];
                b[Mat.Copper].Rod(end, end + V(-2.0, 0, 0), 0.3, 6);
                b.Anchor(pad, end + V(-2.0, 0, 0), V(-1, 0, 0));
            }

            b.Heat(P(len / 2, 0, t + 1.6));
        }
    }
}
