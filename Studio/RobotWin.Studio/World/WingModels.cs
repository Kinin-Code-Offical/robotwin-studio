using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [aero] 3D model of a wing / tail / fin object: a real airfoil section (NACA 4-digit thickness and
    /// camber lines; Clark Y drawn as NACA 2412) lofted along the span with taper, sweep and dihedral,
    /// leading edge at +x. The control surface is its own part hinged at (1 − control chord) and
    /// rotated with the live deflection while simulating.
    /// </summary>
    public static class WingModels
    {
        private static double P(WorldObject o, string key, double fallback) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        /// <summary>(max camber, its position) of the airfoil family (thickness comes from the object's height).</summary>
        private static (double M, double Pc) Camber(string? airfoil) => (airfoil ?? "clarky").Trim().ToLowerInvariant() switch
        {
            "naca0012" or "flat" or "flatplate" => (0, 0.4),
            "naca2412" or "clarky" or "clark-y" => (0.02, 0.4),
            "naca4412" => (0.04, 0.4),
            _ => (0.02, 0.4)
        };

        /// <summary>Upper and lower surface points of a unit chord at stations x ∈ [0, 1] (x from the leading edge).</summary>
        private static List<(double X, double Zu, double Zl)> Section(double tc, double m, double pc, int n)
        {
            var pts = new List<(double, double, double)>();
            for (int i = 0; i <= n; i++)
            {
                double beta = Math.PI * i / n;
                double x = 0.5 * (1 - Math.Cos(beta));   // cosine spacing: fine at the nose and the tail
                double yt = 5 * tc * (0.2969 * Math.Sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x);
                double yc = m <= 0 ? 0 : x < pc ? m / (pc * pc) * (2 * pc * x - x * x) : m / ((1 - pc) * (1 - pc)) * (1 - 2 * pc + 2 * pc * x - x * x);
                pts.Add((x, yc + yt, yc - yt));
            }

            return pts;
        }

        public static ObjectModel Build(WorldObject o, Color color)
        {
            var m = new ObjectModel();
            double root = Math.Max(0.005, o.SizeX), span = Math.Max(0.01, o.SizeY), thick = Math.Max(0.0005, o.SizeZ);
            double tip = Math.Max(0.003, P(o, "tipchord", root));
            double sweep = P(o, "sweep", 0) * Math.PI / 180, dihedral = P(o, "dihedral", 0) * Math.PI / 180;
            bool symmetric = !o.Parameters.TryGetValue("symmetric", out var sym) || !string.Equals(sym, "false", StringComparison.OrdinalIgnoreCase);
            var (cm, pc) = Camber(o.Parameters.TryGetValue("airfoil", out var af) ? af : null);
            double tc = Math.Min(0.25, thick / root);
            string control = o.Parameters.TryGetValue("control", out var k) ? k.Trim().ToLowerInvariant() : "none";
            bool hasFlap = control.Length > 0 && control != "none";
            double cr = hasFlap ? Math.Max(0.1, Math.Min(0.5, P(o, "control.chord", 0.3))) : 0;
            var sec = Section(tc, cm, pc, 24);

            // Spanwise stations: (span coordinate s ≥ 0 along the half span, η sign of the half).
            double half = symmetric ? span / 2 : span;
            var halves = symmetric ? new[] { -1, 1 } : new[] { 1 };
            var bodyMesh = new MeshGeometry3D();
            var flapMesh = new MeshGeometry3D();
            foreach (int side in halves)
            {
                // Two stations per half: root and tip (straight taper).
                var stations = new[] { 0.0, half };
                for (int si = 0; si + 1 < stations.Length; si++)
                {
                    double s0 = stations[si], s1 = stations[si + 1];
                    AddLoft(bodyMesh, sec, side, s0, s1, root, tip, half, sweep, dihedral, symmetric, thick, 0, 1 - cr);
                    if (hasFlap && Covers(control, side)) AddLoft(flapMesh, sec, side, s0, s1, root, tip, half, sweep, dihedral, symmetric, thick, 1 - cr, 1);
                    else if (hasFlap) AddLoft(bodyMesh, sec, side, s0, s1, root, tip, half, sweep, dihedral, symmetric, thick, 1 - cr, 1);
                }
            }

            var mat = new MaterialGroup();
            mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), 25));
            m.Body.Children.Add(new GeometryModel3D(bodyMesh, mat) { BackMaterial = mat });
            if (hasFlap)
            {
                var flapColor = Color.FromRgb((byte)(color.R * 0.75), (byte)(color.G * 0.75), (byte)(color.B * 0.75));
                var fm = new MaterialGroup();
                fm.Children.Add(new DiffuseMaterial(new SolidColorBrush(flapColor)));
                var flap = new Model3DGroup();
                flap.Children.Add(new GeometryModel3D(flapMesh, fm) { BackMaterial = fm });
                // Hinge along the span at the root's hinge chord station, mid thickness.
                double hx = root / 2 - (1 - cr) * root;
                var axis = symmetric ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
                var rot = new AxisAngleRotation3D(axis, 0);
                flap.Transform = new RotateTransform3D(rot, new Point3D(hx, 0, symmetric ? thick / 2 : 0));
                m.Body.Children.Add(flap);
                m.FlapRotation = rot;
                m.FlapSign = symmetric ? 1 : -1;
            }

            m.Bounds = new Rect3D(-root / 2 - Math.Abs(Math.Tan(sweep)) * half, -(symmetric ? span / 2 : thick), 0,
                root + 2 * Math.Abs(Math.Tan(sweep)) * half, symmetric ? span : thick * 2, symmetric ? thick + Math.Abs(Math.Sin(dihedral)) * half : span);
            return m;
        }

        private static bool Covers(string control, int side) => control switch
        {
            "aileron" => true,
            _ => true
        };

        /// <summary>Surface between two span stations for the chord range [c0, c1] (fractions from the leading edge).</summary>
        private static void AddLoft(MeshGeometry3D mesh, List<(double X, double Zu, double Zl)> sec, int side, double s0, double s1,
            double root, double tip, double half, double sweep, double dihedral, bool symmetric, double thick, double c0, double c1)
        {
            Point3D Pt(double s, double x, double z)
            {
                double chord = root + (tip - root) * (s / half);
                double xqc = root / 2 - root / 4;                       // root quarter chord (object x)
                double le = xqc - s * Math.Tan(sweep) + chord / 4;     // leading edge x at this station
                double px = le - x * chord;
                double pz = z * chord;
                if (symmetric)
                    return new Point3D(px, side * s * Math.Cos(dihedral), thick / 2 + pz + s * Math.Sin(dihedral));
                return new Point3D(px, pz, s);                          // fin: span up, thickness sideways
            }

            var ring = new List<(double X, double Z)>();
            // Outline: upper surface LE→TE, lower surface TE→LE, limited to [c0, c1].
            foreach (var p in sec)
                if (p.X >= c0 - 1e-9 && p.X <= c1 + 1e-9) ring.Add((p.X, p.Zu));
            for (int i = sec.Count - 1; i >= 0; i--)
                if (sec[i].X >= c0 - 1e-9 && sec[i].X <= c1 + 1e-9) ring.Add((sec[i].X, sec[i].Zl));
            int n = ring.Count;
            if (n < 3) return;
            int b0 = mesh.Positions.Count;
            foreach (var (x, z) in ring) mesh.Positions.Add(Pt(s0, x, z));
            foreach (var (x, z) in ring) mesh.Positions.Add(Pt(s1, x, z));
            for (int i = 0; i < n; i++)
            {
                int a = b0 + i, b = b0 + (i + 1) % n, c = b0 + n + (i + 1) % n, d = b0 + n + i;
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(b);
                mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(a);
                mesh.TriangleIndices.Add(c);
                mesh.TriangleIndices.Add(d);
            }

            // End caps (fan).
            foreach (int off in new[] { b0, b0 + n })
            {
                for (int i = 1; i + 1 < n; i++)
                {
                    mesh.TriangleIndices.Add(off);
                    mesh.TriangleIndices.Add(off + i);
                    mesh.TriangleIndices.Add(off + i + 1);
                }
            }
        }
    }
}
