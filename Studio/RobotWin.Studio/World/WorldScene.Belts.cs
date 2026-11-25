using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [drive] GT2 belts round their pulleys: the straight spans tangent to both pitch circles and the arcs they wrap, the
    /// belt's back (0.63 mm land over the pulley's tips) and its teeth (2 mm pitch, 0.75 mm deep) sitting in the first
    /// pulley's grooves. Redrawn each frame from where the pulleys are and how far the first has turned, so the teeth run
    /// with the belt.
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _belts = new ModelVisual3D();
        private static readonly Material BeltRubber = M.Matte(Color.FromRgb(0x1D, 0x1D, 0x20), 0.12);

        private void UpdateBelts()
        {
            if (World?.Couplings == null)
            {
                _belts.Content = null;
                return;
            }

            var group = new Model3DGroup();
            foreach (var c in World.Couplings)
            {
                if (c.Kind != WorldCoupling.Belt || c.PulleyA == null || c.PulleyB == null) continue;
                if (!_items.TryGetValue(c.PulleyA, out var a) || !_items.TryGetValue(c.PulleyB, out var b)) continue;
                if (DriveParts.Of(a.Object) is not { } sa || DriveParts.Of(b.Object) is not { } sb) continue;
                if (Belt(a.Current, sa, b.Current, sb) is { } mesh) group.Children.Add(new GeometryModel3D(mesh, BeltRubber) { BackMaterial = BeltRubber });
            }

            _belts.Content = group.Children.Count > 0 ? group : null;
        }

        /// <summary>The belt's solid round two pulleys (world metres), or null when they cannot carry one.</summary>
        private static MeshGeometry3D? Belt(Matrix3D fa, DrivePartSpec sa, Matrix3D fb, DrivePartSpec sb)
        {
            var n = fa.Transform(new Vector3D(0, 0, 1));
            n.Normalize();
            var ca = fa.Transform(new Point3D(0, 0, sa.Length / 2));
            var cb = fb.Transform(new Point3D(0, 0, sb.Length / 2));
            var e1 = cb - ca;
            e1 -= n * Vector3D.DotProduct(e1, n);
            double centres = e1.Length;
            double ra = sa.PitchDiameter / 2, rb = sb.PitchDiameter / 2;
            if (centres <= ra + rb + 0.001) return null;
            e1 /= centres;
            var e2 = Vector3D.CrossProduct(n, e1);
            double phi = Math.Asin(Math.Max(-1, Math.Min(1, (rb - ra) / centres)));

            // The loop, counter-clockwise about n: A's wrap, the lower span, B's wrap, the upper span.
            var path = new List<(double X, double Y, double Nx, double Ny)>();
            void Arc(double cx, double r, double t0, double t1)
            {
                int steps = Math.Max(2, (int)Math.Ceiling((t1 - t0) / (4 * Math.PI / 180)));
                for (int i = 0; i <= steps; i++)
                {
                    double t = t0 + (t1 - t0) * i / steps;
                    path.Add((cx + r * Math.Cos(t), r * Math.Sin(t), Math.Cos(t), Math.Sin(t)));
                }
            }

            Arc(0, ra, Math.PI / 2 + phi, 3 * Math.PI / 2 - phi);
            Arc(centres, rb, -Math.PI / 2 - phi, Math.PI / 2 + phi);
            path.Add(path[0]);

            // Running length along the pitch line, for the teeth.
            var s = new double[path.Count];
            for (int i = 1; i < path.Count; i++) s[i] = s[i - 1] + Math.Sqrt(Sq(path[i].X - path[i - 1].X) + Sq(path[i].Y - path[i - 1].Y));
            double length = s[^1];

            // The first pulley's turn: its grooves (between its teeth) hold the belt's teeth.
            var xa = fa.Transform(new Vector3D(1, 0, 0));
            double turn = Math.Atan2(Vector3D.DotProduct(xa, e2), Vector3D.DotProduct(xa, e1));
            double grooveAngle = turn + Math.PI / sa.Teeth;   // a groove half a tooth from the pulley's tooth 0
            // Where on A's wrap (its arc starts at π/2 + φ) that groove is, as a distance along the belt.
            double fromStart = grooveAngle - (Math.PI / 2 + phi);
            double phase = ra * (fromStart - 2 * Math.PI * Math.Floor(fromStart / (2 * Math.PI)));
            const double pitch = 0.002, land = 0.00063, depth = 0.00075, tooth = 0.00115, width = 0.006;
            double inner = -0.000254, outer = inner + land;   // the land rests on the pulley's tips, 0.254 mm inside the pitch line

            var mesh = new MeshGeometry3D();
            Point3D At(double x, double y, double radial, double nx, double ny, double h) =>
                ca + e1 * (x + nx * radial) + e2 * (y + ny * radial) + n * h;
            void Quad(Point3D p0, Point3D p1, Point3D p2, Point3D p3, Vector3D normal)
            {
                int k = mesh.Positions.Count;
                foreach (var p in new[] { p0, p1, p2, p3})
                {
                    mesh.Positions.Add(p);
                    mesh.Normals.Add(normal);
                }

                var check = Vector3D.DotProduct(Vector3D.CrossProduct(p1 - p0, p2 - p0), normal);
                if (check >= 0)
                {
                    mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 1); mesh.TriangleIndices.Add(k + 2);
                    mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 2); mesh.TriangleIndices.Add(k + 3);
                }
                else
                {
                    mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 2); mesh.TriangleIndices.Add(k + 1);
                    mesh.TriangleIndices.Add(k); mesh.TriangleIndices.Add(k + 3); mesh.TriangleIndices.Add(k + 2);
                }
            }

            double w = width / 2;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var (x0, y0, nx0, ny0) = path[i];
                var (x1, y1, nx1, ny1) = path[i + 1];
                var out0 = e1 * nx0 + e2 * ny0;
                var out1 = e1 * nx1 + e2 * ny1;
                var outN = out0 + out1;
                outN.Normalize();
                // Back, inside face, and both edges.
                Quad(At(x0, y0, outer, nx0, ny0, -w), At(x1, y1, outer, nx1, ny1, -w), At(x1, y1, outer, nx1, ny1, w), At(x0, y0, outer, nx0, ny0, w), outN);
                Quad(At(x0, y0, inner, nx0, ny0, -w), At(x0, y0, inner, nx0, ny0, w), At(x1, y1, inner, nx1, ny1, w), At(x1, y1, inner, nx1, ny1, -w), -outN);
                Quad(At(x0, y0, inner, nx0, ny0, w), At(x0, y0, outer, nx0, ny0, w), At(x1, y1, outer, nx1, ny1, w), At(x1, y1, inner, nx1, ny1, w), n);
                Quad(At(x0, y0, inner, nx0, ny0, -w), At(x1, y1, inner, nx1, ny1, -w), At(x1, y1, outer, nx1, ny1, -w), At(x0, y0, outer, nx0, ny0, -w), -n);
            }

            // The teeth: every 2 mm along the pitch line, from the groove the first pulley holds.
            int count = (int)Math.Floor(length / pitch);
            int seg = 0;
            for (int k = 0; k < count; k++)
            {
                double at = phase + k * pitch;
                at -= length * Math.Floor(at / length);
                while (seg > 0 && s[seg] > at) seg--;
                while (seg + 1 < path.Count - 1 && s[seg + 1] < at) seg++;
                double f = (at - s[seg]) / Math.Max(1e-12, s[seg + 1] - s[seg]);
                var (x0, y0, nx0, ny0) = path[seg];
                var (x1, y1, nx1, ny1) = path[seg + 1];
                double x = x0 + (x1 - x0) * f, y = y0 + (y1 - y0) * f, nx = nx0 + (nx1 - nx0) * f, ny = ny0 + (ny1 - ny0) * f;
                double nl = Math.Sqrt(nx * nx + ny * ny);
                nx /= nl;
                ny /= nl;
                double tx = -ny, ty = nx;   // along the belt
                var o = e1 * nx + e2 * ny;
                var t = e1 * tx + e2 * ty;
                Point3D P(double along, double radial, double h) => At(x + tx * along, y + ty * along, radial, nx, ny, h);
                double hw = tooth / 2, root = inner, tip = inner - depth, tw = hw * 0.7;
                // A trapezoid across the belt's width: its flanks, its top, and its ends.
                Quad(P(-hw, root, -w), P(-tw, tip, -w), P(-tw, tip, w), P(-hw, root, w), -t);
                Quad(P(hw, root, -w), P(hw, root, w), P(tw, tip, w), P(tw, tip, -w), t);
                Quad(P(-tw, tip, -w), P(tw, tip, -w), P(tw, tip, w), P(-tw, tip, w), -o);
                Quad(P(-hw, root, w), P(-tw, tip, w), P(tw, tip, w), P(hw, root, w), n);
                Quad(P(-hw, root, -w), P(hw, root, -w), P(tw, tip, -w), P(-tw, tip, -w), -n);
            }

            mesh.Freeze();
            return mesh;
        }

        private static double Sq(double v) => v * v;
    }
}
