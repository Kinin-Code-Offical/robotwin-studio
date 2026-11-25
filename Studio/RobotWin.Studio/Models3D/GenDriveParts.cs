using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// [drive] Gears, pulleys, bearings, shafts, lead screws and their fittings from their real geometry (mm): involute
    /// teeth, GT2 grooves, a bearing's rings and shields, a trapezoidal multi-start thread. Frame: the axis along +Z, the
    /// bottom face on z = 0; a pillow block stands on its base with the bore along Y.
    /// </summary>
    internal static partial class Gen
    {
        internal static void DrivePart(PartBuilder b, DrivePartSpec s, string? colour = null)
        {
            uint rgb = ParseRgb(colour ?? s.Color, ParseRgb(s.Color, 0xB8BCC2u));
            switch (s.Kind)
            {
                case DrivePartKind.Gear: SpurGear(b, s, rgb); break;
                case DrivePartKind.Pulley: TimingPulley(b, s, rgb); break;
                case DrivePartKind.Bearing: BallBearing(b, s.Bore * 500, s.Diameter * 500, s.Length * 1000); break;
                case DrivePartKind.BearingHousing: PillowBlock(b, s, rgb); break;
                case DrivePartKind.Shaft: ShaftRod(b, s.Bore * 500, s.Length * 1000, rgb); break;
                case DrivePartKind.LeadScrew: LeadScrewRod(b, s, rgb); break;
                case DrivePartKind.LeadNut: LeadNut(b, s, rgb); break;
                case DrivePartKind.LinearBearing: LinearBearing(b, s); break;
                case DrivePartKind.Collar: Collar(b, s, rgb); break;
                default: Coupler(b, s, rgb); break;
            }
        }

        // ================================================================== helpers

        /// <summary>
        /// A prism with a round bore: the outline's walls, the bore's wall facing in, and both faces as rings between the
        /// outline and the bore (the outline must be star-shaped about the axis, as a gear's or pulley's is).
        /// </summary>
        private static void BoredPrism(MeshBuilder m, IList<Point> outline, double bore, double z0, double z1)
        {
            m.Extrude(outline, z0, z1, false, false, false);
            m.Lathe(new[] { new Point(bore, z1), new Point(bore, z0) }, Math.Max(16, outline.Count / 4));
            int n = outline.Count;
            for (int i = 0; i < n; i++)
            {
                var a = outline[i];
                var c = outline[(i + 1) % n];
                double ta = Math.Atan2(a.Y, a.X), tc = Math.Atan2(c.Y, c.X);
                var ia = new Point(bore * Math.Cos(ta), bore * Math.Sin(ta));
                var ic = new Point(bore * Math.Cos(tc), bore * Math.Sin(tc));
                m.Face(new Point3D(a.X, a.Y, z1), new Point3D(c.X, c.Y, z1), new Point3D(ic.X, ic.Y, z1), new Point3D(ia.X, ia.Y, z1), new Vector3D(0, 0, 1));
                m.Face(new Point3D(a.X, a.Y, z0), new Point3D(ia.X, ia.Y, z0), new Point3D(ic.X, ic.Y, z0), new Point3D(c.X, c.Y, z0), new Vector3D(0, 0, -1));
            }
        }

        /// <summary>A plain ring (a hub, a collar) from z0 to z1, its edges lightly chamfered.</summary>
        private static void Ring(MeshBuilder m, double ri, double ro, double z0, double z1, int seg = 40, double ch = 0.3)
        {
            ch = Math.Min(ch, Math.Min((ro - ri) / 3, (z1 - z0) / 3));
            m.Lathe(new[]
            {
                new Point(ri, z0), new Point(ro - ch, z0), new Point(ro - ch, z0), new Point(ro, z0 + ch), new Point(ro, z0 + ch), new Point(ro, z1 - ch),
                new Point(ro, z1 - ch), new Point(ro - ch, z1), new Point(ro - ch, z1), new Point(ri, z1), new Point(ri, z1), new Point(ri, z0)
            }, seg);
        }

        /// <summary>A set screw's hole seen from outside, on a hub of radius r at height z, pointing along X turned by angle.</summary>
        private static void SetScrew(PartBuilder b, double r, double z, double angleDeg, double hole = 1.5)
        {
            using (b.At(Mx.Ry(90) * Mx.Rz(angleDeg) * Mx.T(0, 0, z)))
                b[Mat.Plastic(0x0B0B0Du, 0.2)].Cylinder(hole, r - 0.2, r + 0.03, 12);
        }

        // ================================================================== gears

        /// <summary>
        /// Involute spur-gear outline (mm, counter-clockwise): 20° pressure angle, addendum m, dedendum 1.25 m. Below the
        /// base circle the flank runs radially to the root.
        /// </summary>
        internal static List<Point> GearOutline(double m, int z) =>
            RobotTwin.CoreSim.Mechanics.GearGeometry.Outline(m, z).Select(p => new Point(p.X, p.Y)).ToList();

        private static void SpurGear(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double m = s.Module * 1000, face = s.Length * 1000, bore = s.Bore * 500;
            var mat = s.Material == "brass" ? Mat.Metal(rgb, 0.85, 40) : Mat.Plastic(rgb, 0.25, 30);
            BoredPrism(b[mat], GearOutline(m, s.Teeth), bore, 0, face);
            if (s.HubLength > 0)
            {
                double hub = s.HubDiameter * 500, hl = s.HubLength * 1000;
                Ring(b[mat], bore, hub, face, face + hl, 40, 0.4);
                SetScrew(b, hub, face + hl / 2, 0);
                SetScrew(b, hub, face + hl / 2, 90);
            }
            else SetScrew(b, s.PitchDiameter * 500 - 1.25 * m, face / 2, 0, 1.2);
        }

        // ================================================================== pulleys

        /// <summary>A GT2 pulley's toothed outline: round-bottomed grooves of the belt's teeth, 2 mm apart on the pitch line.</summary>
        private static List<Point> Gt2Outline(int teeth, double R)
        {
            var pts = new List<Point>();
            int per = 8;
            for (int k = 0; k < teeth * per; k++)
            {
                double t = 2 * Math.PI * k / (teeth * per), phase = k % per / (double)per;
                // A groove 1.1 mm wide (on a 2 mm pitch), 0.75 mm deep, round at the bottom.
                double u = (phase - 0.5) / 0.28;
                double depth = Math.Abs(u) < 1 ? 0.75 * Math.Sqrt(1 - u * u) : 0;
                pts.Add(new Point((R - depth) * Math.Cos(t), (R - depth) * Math.Sin(t)));
            }

            return pts;
        }

        private static void TimingPulley(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double R = s.Diameter * 500, bore = s.Bore * 500, w = s.Length * 1000, fl = 0.75, rf = R + 1.75;
            var al = Mat.Metal(rgb, 0.75, 35);
            Ring(b[al], bore, rf, 0, fl, 48, 0.2);
            BoredPrism(b[al], Gt2Outline(s.Teeth, R), bore, fl, w - fl);
            Ring(b[al], bore, rf, w - fl, w, 48, 0.2);
            double hub = s.HubDiameter * 500, hl = s.HubLength * 1000;
            Ring(b[al], bore, hub, w, w + hl, 40, 0.4);
            SetScrew(b, hub, w + hl / 2, 0);
            SetScrew(b, hub, w + hl / 2, 90);
        }

        // ================================================================== bearings

        /// <summary>A shielded radial ball bearing (ZZ): outer and inner rings with chamfers, two pressed steel shields.</summary>
        internal static void BallBearing(PartBuilder b, double ri, double ro, double w)
        {
            double t = (ro - ri) * 0.27, ch = Math.Min(0.3, w / 10);
            var ring = b[Mat.Metal(0xD9DCE0u, 0.95, 70)];
            Ring(ring, ro - t, ro, 0, w, 48, ch);
            Ring(ring, ri, ri + t, 0, w, 40, ch);
            // The shields, a little below each face, and the snap ring that holds them.
            var shield = b[Mat.Metal(0xA9AEB5u, 0.7, 30)];
            foreach (double z in new[] { 0.35, w - 0.35 })
            {
                shield.Lathe(z < w / 2
                    ? new[] { new Point(ri + t + 0.15, z), new Point(ro - t - 0.1, z) }
                    : new[] { new Point(ro - t - 0.1, z), new Point(ri + t + 0.15, z) }, 48);
            }

            var gap = b[Mat.Plastic(0x16171Au, 0.1)];
            gap.Lathe(new[] { new Point(ri + t + 0.02, 0.3), new Point(ri + t + 0.15, 0.36) }, 40);
            gap.Lathe(new[] { new Point(ri + t + 0.15, w - 0.36), new Point(ri + t + 0.02, w - 0.3) }, 40);
        }

        /// <summary>
        /// KP08: a die-cast block on a slotted base, the bearing's boss on top; the bearing in it with its eccentric inner
        /// ring and set screws; a grease nipple. The bore along Y at the axis height.
        /// </summary>
        private static void PillowBlock(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double L = s.Diameter * 1000, W = s.Length * 1000, zc = s.AxisHeight * 1000, bore = s.Bore * 500;
            var cast = b[Mat.Metal(rgb, 0.35, 12)];
            cast.RoundedBox(0, 0, 0, L, W, 5, 2.5);
            // The web up to the boss.
            cast.Tapered(0, 0, 5, 26, W - 1, zc - 5, 3);
            var slot = b[Mat.Plastic(0x0D0D0Fu, 0.1)];
            foreach (double x in new[] { -21.0, 21.0 })
            {
                slot.RoundedBox(x, 0, 4.99, 9, 5.5, 0.03, 2.7);
                slot.RoundedBox(x, 0, -0.02, 9, 5.5, 0.03, 2.7);
            }

            using (b.At(Mx.Rx(-90) * Mx.T(0, -W / 2, zc)))
            {
                Ring(cast, 11.0, 14.0, 0, W, 48, 0.8);
                using (b.At(Mx.T(0, 0, (W - 7) / 2))) BallBearing(b, bore, 11.0, 7);
                // The inner ring's extended collar with its two set screws.
                var steel = b[Mat.Metal(0xC9CDD2u, 0.9, 60)];
                Ring(steel, bore, bore + 2.2, W, W + 4, 32, 0.3);
                using (b.At(Mx.T(0, 0, 0))) SetScrew(b, bore + 2.2, W + 2, 0, 1.2);
                SetScrew(b, bore + 2.2, W + 2, 120, 1.2);
            }

            // Grease nipple on top of the boss.
            b[Mat.Metal(0xB59A4Au, 0.8, 40)].CylinderZ(0, 0, zc + 13.8, zc + 16.5, 2.2, 6);
            b[Mat.Metal(0xB59A4Au, 0.8, 40)].Sphere(0, 0, zc + 17.2, 1.5, 10);
        }

        /// <summary>LM8UU: a steel sleeve with two retaining-ring grooves and black seals at the ends.</summary>
        private static void LinearBearing(PartBuilder b, DrivePartSpec s)
        {
            double ri = s.Bore * 500, ro = s.Diameter * 500, L = s.Length * 1000, g = 0.5;
            var steel = b[Mat.Metal(0xD4D8DDu, 0.92, 65)];
            double g1 = 3.5, g2 = L - 3.5 - 1.1;
            steel.Lathe(new[]
            {
                new Point(ri + 0.6, 0), new Point(ro - 0.4, 0), new Point(ro - 0.4, 0), new Point(ro, 0.4), new Point(ro, 0.4), new Point(ro, g1),
                new Point(ro, g1), new Point(ro - g, g1), new Point(ro - g, g1), new Point(ro - g, g1 + 1.1), new Point(ro - g, g1 + 1.1), new Point(ro, g1 + 1.1),
                new Point(ro, g1 + 1.1), new Point(ro, g2), new Point(ro, g2), new Point(ro - g, g2), new Point(ro - g, g2), new Point(ro - g, g2 + 1.1),
                new Point(ro - g, g2 + 1.1), new Point(ro, g2 + 1.1), new Point(ro, g2 + 1.1), new Point(ro, L - 0.4), new Point(ro, L - 0.4), new Point(ro - 0.4, L),
                new Point(ro - 0.4, L), new Point(ri + 0.6, L), new Point(ri + 0.6, L), new Point(ri + 0.6, 0)
            }, 48);
            var seal = b[Mat.Plastic(0x1A1A1Cu, 0.08)];
            seal.Lathe(new[] { new Point(ri, 0.15), new Point(ro - 0.6, 0.15) }, 40);
            seal.Lathe(new[] { new Point(ro - 0.6, L - 0.15), new Point(ri, L - 0.15) }, 40);
        }

        // ================================================================== rods and screws

        /// <summary>A ground steel rod, chamfered at both ends.</summary>
        private static void ShaftRod(PartBuilder b, double r, double L, uint rgb)
        {
            double ch = Math.Min(0.5, r / 4);
            b[Mat.Metal(rgb, 0.95, 80)].Lathe(new[]
            {
                new Point(0, 0), new Point(r - ch, 0), new Point(r - ch, 0), new Point(r, ch), new Point(r, ch), new Point(r, L - ch),
                new Point(r, L - ch), new Point(r - ch, L), new Point(r - ch, L), new Point(0, L)
            }, 28);
        }

        /// <summary>
        /// A trapezoidal thread as a helical surface: at every angle the radius follows the thread's section shifted by
        /// lead × angle / 2π, so a four-start T8×8 shows four threads and a T8×2 one.
        /// </summary>
        private static void LeadScrewRod(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double R = s.Bore * 500, L = s.Length * 1000, lead = s.Lead * 1000, pitch = s.Pitch * 1000, depth = 0.9;
            double Radius(double z, double u)
            {
                double f = (z - lead * u / (2 * Math.PI)) / pitch;
                f -= Math.Floor(f);
                // Crest, flank, root, flank (30° trapezoid).
                return f < 0.32 ? R : f < 0.5 ? R - depth * (f - 0.32) / 0.18 : f < 0.82 ? R - depth : R - depth * (1 - (f - 0.82) / 0.18);
            }

            const int around = 20;
            int rings = Math.Max(2, (int)Math.Ceiling(L / pitch * 8));
            var m = b[Mat.Metal(rgb, 0.9, 55)];
            var grid = new int[rings + 1, around + 1];
            Point3D At(int i, int k)
            {
                double z = L * i / rings, u = 2 * Math.PI * k / around;
                double r = Radius(z, u);
                return new Point3D(r * Math.Cos(u), r * Math.Sin(u), z);
            }

            for (int i = 0; i <= rings; i++)
            {
                for (int k = 0; k <= around; k++)
                {
                    var p = At(i, k);
                    // The surface's normal from its neighbours along the axis and around it.
                    var dz = At(Math.Min(rings, i + 1), k) - At(Math.Max(0, i - 1), k);
                    var du = At(i, k + 1) - At(i, k == 0 ? around - 1 : k - 1);
                    var n = Vector3D.CrossProduct(du, dz);
                    if (n.LengthSquared < 1e-12) n = new Vector3D(p.X, p.Y, 0);
                    grid[i, k] = m.V(p, n, new Point(k / (double)around, i / (double)rings));
                }
            }

            for (int i = 0; i < rings; i++)
            {
                for (int k = 0; k < around; k++)
                {
                    m.Tri(grid[i, k], grid[i, k + 1], grid[i + 1, k + 1]);
                    m.Tri(grid[i, k], grid[i + 1, k + 1], grid[i + 1, k]);
                }
            }

            // The ends, cut flat at the root.
            m.Lathe(new[] { new Point(0, 0), new Point(R - depth, 0) }, around);
            m.Lathe(new[] { new Point(R - depth, L), new Point(0, L) }, around);
        }

        /// <summary>The flanged brass nut: a 22 mm flange with four screw holes, the threaded body below.</summary>
        private static void LeadNut(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double bore = s.Bore * 500 - 0.9, rf = s.Diameter * 500, L = s.Length * 1000, flange = 3.5, body = 5.1;
            var brass = b[Mat.Metal(rgb, 0.85, 40)];
            brass.Lathe(new[]
            {
                new Point(bore, 0), new Point(body - 0.3, 0), new Point(body - 0.3, 0), new Point(body, 0.3), new Point(body, 0.3), new Point(body, L - flange),
                new Point(body, L - flange), new Point(rf - 0.4, L - flange), new Point(rf - 0.4, L - flange), new Point(rf, L - flange + 0.4), new Point(rf, L - flange + 0.4),
                new Point(rf, L - 0.4), new Point(rf, L - 0.4), new Point(rf - 0.4, L), new Point(rf - 0.4, L), new Point(bore, L), new Point(bore, L), new Point(bore, 0)
            }, 48);
            var hole = b[Mat.Plastic(0x0D0D0Fu, 0.1)];
            for (int i = 0; i < 4; i++)
            {
                double a = Math.PI / 4 + i * Math.PI / 2;
                hole.CylinderZ(8 * Math.Cos(a), 8 * Math.Sin(a), L - 0.01, L + 0.02, 1.75, 12);
                hole.CylinderZ(8 * Math.Cos(a), 8 * Math.Sin(a), L - flange - 0.02, L - flange + 0.01, 1.75, 12);
            }
        }

        private static void Collar(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double ri = s.Bore * 500, ro = s.Diameter * 500, L = s.Length * 1000;
            Ring(b[Mat.Metal(rgb, 0.5, 25)], ri, ro, 0, L, 40, 0.5);
            SetScrew(b, ro, L / 2, 0, 1.4);
        }

        /// <summary>A beam coupler: an aluminium cylinder with a helical slot in the middle, a bore of each size, a set screw at each end.</summary>
        private static void Coupler(PartBuilder b, DrivePartSpec s, uint rgb)
        {
            double ro = s.Diameter * 500, L = s.Length * 1000, b1 = s.Bore * 500, b2 = s.Bore2 * 500;
            b[Mat.Metal(rgb, 0.75, 35)].Lathe(new[]
            {
                new Point(b1, 0), new Point(ro - 0.4, 0), new Point(ro - 0.4, 0), new Point(ro, 0.4), new Point(ro, 0.4), new Point(ro, L - 0.4),
                new Point(ro, L - 0.4), new Point(ro - 0.4, L), new Point(ro - 0.4, L), new Point(b2, L), new Point(b2, L), new Point(b2, L / 2),
                new Point(b2, L / 2), new Point(b1, L / 2), new Point(b1, L / 2), new Point(b1, 0)
            }, 48);
            var path = new List<Point3D>();
            for (int i = 0; i <= 96; i++)
            {
                double t = i / 96.0, u = t * 2 * Math.PI * 4;
                path.Add(new Point3D((ro + 0.02) * Math.Cos(u), (ro + 0.02) * Math.Sin(u), L * 0.32 + L * 0.36 * t));
            }

            b[Mat.Plastic(0x101012u, 0.1)].Tube(path, 0.45, 6);
            SetScrew(b, ro, 3.5, 0, 1.4);
            SetScrew(b, ro, L - 3.5, 0, 1.4);
        }
    }
}
