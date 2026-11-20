using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>Turntable camera (Z up) with perspective and orthographic projection, in millimetres.</summary>
    public sealed class ViewCamera
    {
        public double Yaw = Math.PI / 2 - 0.7;   // looking along +Y when π/2
        public double Pitch = 0.5;
        public double Distance = 220;
        public Vec3 Target = new Vec3(0, 0, 10);
        public bool Ortho;
        public double FieldOfView = 40;
        public double Width = 800, Height = 600;

        public Vec3 Forward => new Vec3(Math.Cos(Pitch) * Math.Cos(Yaw), Math.Cos(Pitch) * Math.Sin(Yaw), -Math.Sin(Pitch));
        public Vec3 Right => new Vec3(Math.Sin(Yaw), -Math.Cos(Yaw), 0);
        public Vec3 Up => Right.Cross(Forward);
        public Vec3 Position => Target - Forward * Distance;

        /// <summary>Focal length in pixels (perspective).</summary>
        public double Focal => Width / 2 / Math.Tan(FieldOfView * Math.PI / 360);

        /// <summary>Visible width at the target (orthographic width).</summary>
        public double OrthoWidth => 2 * Distance * Math.Tan(FieldOfView * Math.PI / 360);

        public double OrthoScale => OrthoWidth / Math.Max(1, Width);

        /// <summary>Screen position and view depth of a point; Visible false behind the camera.</summary>
        public (Point Screen, double Depth, bool Visible) Project(Vec3 p)
        {
            var d = p - Position;
            double z = d.Dot(Forward);
            double x = d.Dot(Right), y = d.Dot(Up);
            if (Ortho)
            {
                double s = OrthoScale;
                return (new Point(Width / 2 + x / s, Height / 2 - y / s), z, true);
            }

            if (z <= Distance * 1e-4) return (new Point(double.NaN, double.NaN), z, false);
            double f = Focal;
            return (new Point(Width / 2 + f * x / z, Height / 2 - f * y / z), z, true);
        }

        public Point ProjectPoint(Vec3 p) => Project(p).Screen;

        /// <summary>World ray under a screen point.</summary>
        public (Vec3 Origin, Vec3 Dir) Ray(Point screen)
        {
            if (Ortho)
            {
                double s = OrthoScale;
                var o = Position - Forward * Distance * 50 + Right * ((screen.X - Width / 2) * s) - Up * ((screen.Y - Height / 2) * s);
                return (o, Forward);
            }

            var dir = (Forward * Focal + Right * (screen.X - Width / 2) - Up * (screen.Y - Height / 2)).Normalized();
            return (Position, dir);
        }

        /// <summary>World size of one pixel at the point.</summary>
        public double PixelSize(Vec3 p)
        {
            if (Ortho) return OrthoScale;
            double z = Math.Max(Distance * 1e-3, (p - Position).Dot(Forward));
            return z / Focal;
        }

        /// <summary>Direction from the point towards the eye.</summary>
        public Vec3 ToEye(Vec3 p) => Ortho ? -Forward : (Position - p).Normalized();

        public Vec3? OnPlane(Point screen, Vec3 planePoint, Vec3 normal)
        {
            var (o, d) = Ray(screen);
            double den = d.Dot(normal);
            if (Math.Abs(den) < 1e-9) return null;
            double t = (planePoint - o).Dot(normal) / den;
            return t < 0 && !Ortho ? null : o + d * t;
        }
    }

    /// <summary>
    /// Depth-tested lines and dots of constant pixel width, built as camera-facing quads
    /// (WPF 3D has no line primitive). Rebuilt whenever the camera moves.
    /// </summary>
    public sealed class Ribbons
    {
        private readonly ViewCamera _cam;
        private readonly Dictionary<Color, (Point3DCollection P, Int32Collection I)> _batches = new Dictionary<Color, (Point3DCollection, Int32Collection)>();

        public Ribbons(ViewCamera cam, double bias = 0.0015)
        {
            _cam = cam;
            Bias = bias;
        }

        /// <summary>Fraction of the view depth by which geometry is pulled towards the eye (draws over coplanar faces).</summary>
        public double Bias { get; set; }

        private (Point3DCollection P, Int32Collection I) Batch(Color c)
        {
            if (!_batches.TryGetValue(c, out var b)) _batches[c] = b = (new Point3DCollection(), new Int32Collection());
            return b;
        }

        private Vec3 Pull(Vec3 p)
        {
            if (Bias <= 0) return p;
            double depth = _cam.Ortho ? _cam.Distance : Math.Max(0, (p - _cam.Position).Dot(_cam.Forward));
            return p + _cam.ToEye(p) * (depth * Bias);
        }

        public void Line(Vec3 a, Vec3 b, Color color, double widthPx)
        {
            var pa = Pull(a);
            var pb = Pull(b);
            var d = pb - pa;
            if (d.Length < 1e-12) return;
            var mid = (pa + pb) * 0.5;
            var side = d.Cross(_cam.ToEye(mid)).Normalized();
            if (side.Length < 0.5) side = _cam.Up;
            double ha = _cam.PixelSize(pa) * widthPx / 2, hb = _cam.PixelSize(pb) * widthPx / 2;
            var (p, i) = Batch(color);
            int n = p.Count;
            p.Add(P(pa - side * ha));
            p.Add(P(pa + side * ha));
            p.Add(P(pb + side * hb));
            p.Add(P(pb - side * hb));
            i.Add(n);
            i.Add(n + 1);
            i.Add(n + 2);
            i.Add(n);
            i.Add(n + 2);
            i.Add(n + 3);
        }

        public void Dot(Vec3 c, Color color, double sizePx)
        {
            var pc = Pull(c);
            double h = _cam.PixelSize(pc) * sizePx / 2;
            var r = _cam.Right * h;
            var u = _cam.Up * h;
            var (p, i) = Batch(color);
            int n = p.Count;
            p.Add(P(pc - r - u));
            p.Add(P(pc + r - u));
            p.Add(P(pc + r + u));
            p.Add(P(pc - r + u));
            i.Add(n);
            i.Add(n + 1);
            i.Add(n + 2);
            i.Add(n);
            i.Add(n + 2);
            i.Add(n + 3);
        }

        private static Point3D P(Vec3 v) => new Point3D(v.X, v.Y, v.Z);

        public bool IsEmpty => _batches.Count == 0;

        public Model3DGroup Build()
        {
            var g = new Model3DGroup();
            foreach (var kv in _batches)
            {
                var mesh = new MeshGeometry3D { Positions = kv.Value.P, TriangleIndices = kv.Value.I };
                mesh.Freeze();
                var mat = ModelGeometry.Unlit(kv.Key);
                g.Children.Add(new GeometryModel3D(mesh, mat) { BackMaterial = mat });
            }

            g.Freeze();
            return g;
        }
    }

    /// <summary>WPF geometry and materials for modelling objects.</summary>
    public static class ModelGeometry
    {
        private static readonly Dictionary<Color, Material> UnlitCache = new Dictionary<Color, Material>();

        public static Material Unlit(Color c)
        {
            if (UnlitCache.TryGetValue(c, out var m)) return m;
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Colors.Black)));
            g.Children.Add(new EmissiveMaterial(new SolidColorBrush(c)));
            g.Freeze();
            UnlitCache[c] = g;
            return g;
        }

        public static Material Solid(Color c, bool xray)
        {
            var g = new MaterialGroup();
            if (xray)
            {
                g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x55, c.R, c.G, c.B))));
            }
            else
            {
                g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
                g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF)), 36));
            }

            g.Freeze();
            return g;
        }

        public static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                return (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                return fallback;
            }
        }

        /// <summary>
        /// Triangle geometry of a polygon mesh with flat or auto-smooth normals; only faces
        /// passing <paramref name="filter"/> are emitted.
        /// </summary>
        public static MeshGeometry3D BuildSolid(PolyMesh m, MeshModel tri, int[] triFace, bool smooth, double angleDeg, Func<int, bool>? filter = null)
        {
            var geo = new MeshGeometry3D();
            int fc = m.Faces.Count;
            var fn = new Vec3[fc];
            var raw = new Vec3[fc];
            for (int f = 0; f < fc; f++)
            {
                raw[f] = m.FaceNormalRaw(f);
                fn[f] = raw[f].Normalized();
            }

            List<int>[]? vf = smooth ? m.VertexFaces() : null;
            double cosTol = Math.Cos(angleDeg * Math.PI / 180);
            var cornerCache = smooth ? new Dictionary<long, Vector3D>() : null;
            Vector3D Normal(int v, int f)
            {
                if (!smooth) return new Vector3D(fn[f].X, fn[f].Y, fn[f].Z);
                long key = (long)v << 32 | (uint)f;
                if (cornerCache!.TryGetValue(key, out var c)) return c;
                var sum = Vec3.Zero;
                foreach (int g in vf![v])
                {
                    if (fn[g].Dot(fn[f]) >= cosTol) sum += raw[g];
                }

                var n = sum.Normalized();
                if (n.Length < 0.5) n = fn[f];
                c = new Vector3D(n.X, n.Y, n.Z);
                cornerCache[key] = c;
                return c;
            }

            var pos = new Point3DCollection(tri.TriangleCount * 3);
            var nor = new Vector3DCollection(tri.TriangleCount * 3);
            var idx = new Int32Collection(tri.TriangleCount * 3);
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                int f = triFace[t];
                if (filter != null && !filter(f)) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = tri.Indices[3 * t + k];
                    var p = tri.Vertices[v];
                    idx.Add(pos.Count);
                    pos.Add(new Point3D(p.X, p.Y, p.Z));
                    nor.Add(Normal(v, f));
                }
            }

            geo.Positions = pos;
            geo.Normals = nor;
            geo.TriangleIndices = idx;
            geo.Freeze();
            return geo;
        }

        public static Matrix3D ToMatrix(Affine a) => new Matrix3D(
            a.M00, a.M10, a.M20, 0,
            a.M01, a.M11, a.M21, 0,
            a.M02, a.M12, a.M22, 0,
            a.Tx, a.Ty, a.Tz, 1);
    }
}
