using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Models3D
{
    /// <summary>Transform stack shared by all meshes of a part under construction.</summary>
    internal sealed class Frame
    {
        private readonly Stack<Matrix3D> _stack = new Stack<Matrix3D>();

        public Matrix3D M { get; private set; } = Matrix3D.Identity;

        /// <summary>True when the current transform mirrors (triangles must be wound the other way).</summary>
        public bool Mirrored { get; private set; }

        public void Push(Matrix3D local)
        {
            _stack.Push(M);
            M = local * M;
            Mirrored = M.Determinant < 0;
        }

        public void Pop()
        {
            M = _stack.Pop();
            Mirrored = M.Determinant < 0;
        }

        public Scope At(Matrix3D local)
        {
            Push(local);
            return new Scope(this);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly Frame _f;
            public Scope(Frame f) => _f = f;
            public void Dispose() => _f.Pop();
        }
    }

    /// <summary>Matrix helpers (WPF row-vector convention: <c>a * b</c> applies a first).</summary>
    internal static class Mx
    {
        public static Matrix3D T(double x, double y, double z) => new Matrix3D(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1);
        public static Matrix3D S(double x, double y, double z) => new Matrix3D(x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1);
        public static Matrix3D S(double k) => S(k, k, k);

        public static Matrix3D R(Vector3D axis, double degrees)
        {
            var m = Matrix3D.Identity;
            if (Math.Abs(degrees) > 1e-12) m.Rotate(new Quaternion(axis, degrees));
            return m;
        }

        public static Matrix3D Rx(double degrees) => R(new Vector3D(1, 0, 0), degrees);
        public static Matrix3D Ry(double degrees) => R(new Vector3D(0, 1, 0), degrees);
        public static Matrix3D Rz(double degrees) => R(new Vector3D(0, 0, 1), degrees);

        /// <summary>Local frame with the given origin and axes (axes are images of local x, y, z).</summary>
        public static Matrix3D Frame(Point3D o, Vector3D x, Vector3D y, Vector3D z) =>
            new Matrix3D(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, o.X, o.Y, o.Z, 1);

        /// <summary>Frame whose local +Z points from a to b (origin at a).</summary>
        public static Matrix3D Along(Point3D a, Point3D b)
        {
            var z = b - a;
            z.Normalize();
            var helper = Math.Abs(z.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var x = Vector3D.CrossProduct(helper, z);
            x.Normalize();
            var y = Vector3D.CrossProduct(z, x);
            return Frame(a, x, y, z);
        }
    }

    /// <summary>
    /// Triangle mesh under construction. Every primitive is placed through the shared
    /// <see cref="Frame"/>; triangle winding follows the outward normals even through
    /// mirroring transforms, so models need no back material.
    /// </summary>
    internal sealed class MeshBuilder
    {
        private readonly Frame _f;
        private readonly List<Point3D> _p = new List<Point3D>();
        private readonly List<Vector3D> _n = new List<Vector3D>();
        private readonly List<Point> _uv = new List<Point>();
        private readonly List<int> _i = new List<int>();

        public MeshBuilder(Frame frame) => _f = frame;

        public int TriangleCount => _i.Count / 3;
        public bool IsEmpty => _i.Count == 0;
        internal Frame Frame => _f;

        public MeshGeometry3D ToGeometry()
        {
            var m = new MeshGeometry3D
            {
                Positions = new Point3DCollection(_p),
                Normals = new Vector3DCollection(_n),
                TextureCoordinates = new PointCollection(_uv),
                TriangleIndices = new Int32Collection(_i)
            };
            m.Freeze();
            return m;
        }

        public void Append(MeshBuilder other)
        {
            int b = _p.Count;
            _p.AddRange(other._p);
            _n.AddRange(other._n);
            _uv.AddRange(other._uv);
            foreach (int i in other._i) _i.Add(i + b);
        }

        // ------------------------------------------------------------------ low level

        public int V(Point3D p, Vector3D n, Point uv)
        {
            var m = _f.M;
            _p.Add(m.Transform(p));
            var tn = m.Transform(n);
            if (tn.LengthSquared > 1e-24) tn.Normalize();
            _n.Add(tn);
            _uv.Add(uv);
            return _p.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            _i.Add(a);
            if (_f.Mirrored)
            {
                _i.Add(c);
                _i.Add(b);
            }
            else
            {
                _i.Add(b);
                _i.Add(c);
            }
        }

        /// <summary>Flat quad; corners in order around the face, wound to face along <paramref name="n"/>.</summary>
        public MeshBuilder Face(Point3D a, Point3D b, Point3D c, Point3D d, Vector3D? normal = null)
        {
            var n = normal ?? Vector3D.CrossProduct(c - a, d - b);
            if (n.LengthSquared < 1e-24) return this;
            n.Normalize();
            if (Vector3D.DotProduct(Vector3D.CrossProduct(b - a, c - a), n) < 0)
            {
                (b, d) = (d, b);
                int i0 = V(a, n, new Point(0, 0)), i1 = V(b, n, new Point(0, 1)), i2 = V(c, n, new Point(1, 1)), i3 = V(d, n, new Point(1, 0));
                Tri(i0, i1, i2);
                Tri(i0, i2, i3);
            }
            else
            {
                int i0 = V(a, n, new Point(0, 0)), i1 = V(b, n, new Point(1, 0)), i2 = V(c, n, new Point(1, 1)), i3 = V(d, n, new Point(0, 1));
                Tri(i0, i1, i2);
                Tri(i0, i2, i3);
            }

            return this;
        }

        public MeshBuilder Triangle(Point3D a, Point3D b, Point3D c)
        {
            var n = Vector3D.CrossProduct(b - a, c - a);
            if (n.LengthSquared < 1e-24) return this;
            n.Normalize();
            Tri(V(a, n, new Point(0, 0)), V(b, n, new Point(1, 0)), V(c, n, new Point(0, 1)));
            return this;
        }

        // ------------------------------------------------------------------ boxes

        public MeshBuilder Box(double cx, double cy, double cz, double sx, double sy, double sz)
        {
            double x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
            return Hexa(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0),
                new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1));
        }

        /// <summary>Box standing on z0 (bottom face at z0).</summary>
        public MeshBuilder BoxZ(double cx, double cy, double z0, double sx, double sy, double h) => Box(cx, cy, z0 + h / 2, sx, sy, h);

        /// <summary>Box between two corners.</summary>
        public MeshBuilder Block(double x0, double y0, double z0, double x1, double y1, double z1) =>
            Box((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, Math.Abs(x1 - x0), Math.Abs(y1 - y0), Math.Abs(z1 - z0));

        /// <summary>Any convex hexahedron: bottom four corners then the top four, both counter-clockwise from above.</summary>
        public MeshBuilder Hexa(Point3D b0, Point3D b1, Point3D b2, Point3D b3, Point3D t0, Point3D t1, Point3D t2, Point3D t3, bool top = true, bool bottom = true)
        {
            var c = new Point3D((b0.X + b2.X + t0.X + t2.X) / 4, (b0.Y + b2.Y + t0.Y + t2.Y) / 4, (b0.Z + b2.Z + t0.Z + t2.Z) / 4);
            void F(Point3D a, Point3D b, Point3D cc, Point3D d)
            {
                var n = Vector3D.CrossProduct(cc - a, d - b);
                var mid = new Point3D((a.X + cc.X) / 2, (a.Y + cc.Y) / 2, (a.Z + cc.Z) / 2);
                if (Vector3D.DotProduct(n, mid - c) < 0) n = -n;
                Face(a, b, cc, d, n);
            }

            if (top) F(t0, t1, t2, t3);
            if (bottom) F(b3, b2, b1, b0);
            F(b0, b1, t1, t0);
            F(b1, b2, t2, t1);
            F(b2, b3, t3, t2);
            F(b3, b0, t0, t3);
            return this;
        }

        /// <summary>Box with draft: the top face is inset by <paramref name="inset"/> on every side.</summary>
        public MeshBuilder Tapered(double cx, double cy, double z0, double sx, double sy, double h, double inset, double insetY = double.NaN)
        {
            double iy = double.IsNaN(insetY) ? inset : insetY;
            double x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z1 = z0 + h;
            return Hexa(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0),
                new Point3D(x0 + inset, y0 + iy, z1), new Point3D(x1 - inset, y0 + iy, z1), new Point3D(x1 - inset, y1 - iy, z1), new Point3D(x0 + inset, y1 - iy, z1));
        }

        /// <summary>Rounded rectangle extruded from z0 to z0 + h (smooth corners).</summary>
        public MeshBuilder RoundedBox(double cx, double cy, double z0, double sx, double sy, double h, double r, int cornerSeg = 4, bool top = true, bool bottom = true)
        {
            r = Math.Max(0, Math.Min(r, Math.Min(sx, sy) / 2 - 1e-4));
            if (r <= 1e-4) return Box(cx, cy, z0 + h / 2, sx, sy, h);
            return Extrude(RoundedRect(cx, cy, sx, sy, r, cornerSeg), z0, z0 + h, true, top, bottom);
        }

        public static List<Point> RoundedRect(double cx, double cy, double sx, double sy, double r, int seg)
        {
            var pts = new List<Point>();
            double hx = sx / 2 - r, hy = sy / 2 - r;
            var centres = new[] { new Point(cx + hx, cy + hy), new Point(cx - hx, cy + hy), new Point(cx - hx, cy - hy), new Point(cx + hx, cy - hy) };
            for (int k = 0; k < 4; k++)
            {
                for (int i = 0; i <= seg; i++)
                {
                    double a = Math.PI / 2 * (k + (double)i / seg);
                    pts.Add(new Point(centres[k].X + r * Math.Cos(a), centres[k].Y + r * Math.Sin(a)));
                }
            }

            return pts;
        }

        public static List<Point> Circle(double cx, double cy, double r, int seg, double a0 = 0)
        {
            var pts = new List<Point>(seg);
            for (int i = 0; i < seg; i++)
            {
                double a = a0 + 2 * Math.PI * i / seg;
                pts.Add(new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }

            return pts;
        }

        // ------------------------------------------------------------------ extrusion

        /// <summary>Prism from a simple polygon (any winding) between z0 and z1.</summary>
        public MeshBuilder Extrude(IList<Point> outline, double z0, double z1, bool smooth = false, bool top = true, bool bottom = true)
        {
            int n = outline.Count;
            if (n < 3) return this;
            var pts = new List<Point>(outline);
            if (SignedArea(pts) < 0) pts.Reverse();
            // drop a duplicated closing point
            if ((pts[0] - pts[n - 1]).Length < 1e-9)
            {
                pts.RemoveAt(n - 1);
                n--;
            }

            var edgeN = new Vector[n];
            for (int i = 0; i < n; i++)
            {
                var d = pts[(i + 1) % n] - pts[i];
                var en = new Vector(d.Y, -d.X);
                if (en.Length > 1e-12) en.Normalize();
                edgeN[i] = en;
            }

            double perim = 0;
            for (int i = 0; i < n; i++) perim += (pts[(i + 1) % n] - pts[i]).Length;
            double run = 0;
            for (int i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                Vector na = edgeN[i], nb = edgeN[i];
                if (smooth)
                {
                    na = edgeN[i] + edgeN[(i + n - 1) % n];
                    nb = edgeN[i] + edgeN[(i + 1) % n];
                    if (na.Length > 1e-12) na.Normalize();
                    if (nb.Length > 1e-12) nb.Normalize();
                }

                double len = (b - a).Length;
                double u0 = run / perim, u1 = (run + len) / perim;
                run += len;
                int i0 = V(new Point3D(a.X, a.Y, z0), new Vector3D(na.X, na.Y, 0), new Point(u0, 1));
                int i1 = V(new Point3D(b.X, b.Y, z0), new Vector3D(nb.X, nb.Y, 0), new Point(u1, 1));
                int i2 = V(new Point3D(b.X, b.Y, z1), new Vector3D(nb.X, nb.Y, 0), new Point(u1, 0));
                int i3 = V(new Point3D(a.X, a.Y, z1), new Vector3D(na.X, na.Y, 0), new Point(u0, 0));
                Tri(i0, i1, i2);
                Tri(i0, i2, i3);
            }

            if (top || bottom)
            {
                var tris = Triangulate(pts);
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in pts)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }

                Point Uv(Point p) => new Point((p.X - minX) / Math.Max(1e-9, maxX - minX), (p.Y - minY) / Math.Max(1e-9, maxY - minY));
                if (top)
                {
                    int b = -1;
                    for (int i = 0; i < n; i++)
                    {
                        int k = V(new Point3D(pts[i].X, pts[i].Y, z1), new Vector3D(0, 0, 1), Uv(pts[i]));
                        if (i == 0) b = k;
                    }

                    foreach (var (x, y, z) in tris) Tri(b + x, b + y, b + z);
                }

                if (bottom)
                {
                    int b = -1;
                    for (int i = 0; i < n; i++)
                    {
                        int k = V(new Point3D(pts[i].X, pts[i].Y, z0), new Vector3D(0, 0, -1), Uv(pts[i]));
                        if (i == 0) b = k;
                    }

                    foreach (var (x, y, z) in tris) Tri(b + x, b + z, b + y);
                }
            }

            return this;
        }

        public static double SignedArea(IList<Point> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].X * q.Y - q.X * p[i].Y;
            }

            return a / 2;
        }

        /// <summary>Ear clipping of a counter-clockwise simple polygon.</summary>
        public static List<(int, int, int)> Triangulate(IList<Point> p)
        {
            var result = new List<(int, int, int)>();
            var idx = new List<int>();
            for (int i = 0; i < p.Count; i++) idx.Add(i);
            int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k + idx.Count - 1) % idx.Count], ib = idx[k], ic = idx[(k + 1) % idx.Count];
                    var a = p[ia];
                    var b = p[ib];
                    var c = p[ic];
                    double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (cross <= 1e-12) continue;
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (InTriangle(p[j], a, b, c))
                        {
                            inside = true;
                            break;
                        }
                    }

                    if (inside) continue;
                    result.Add((ia, ib, ic));
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }

                if (!clipped) break;
            }

            if (idx.Count == 3) result.Add((idx[0], idx[1], idx[2]));
            else if (idx.Count > 3)
            {
                for (int k = 1; k + 1 < idx.Count; k++) result.Add((idx[0], idx[k], idx[k + 1]));
            }

            return result;
        }

        private static bool InTriangle(Point p, Point a, Point b, Point c)
        {
            double d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            double d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
            double d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        /// <summary>Rectangular plate (x0..x1, y0..y1, z0..z1) with a round through hole.</summary>
        public MeshBuilder PlateWithHole(double x0, double y0, double x1, double y1, double z0, double z1, double hx, double hy, double hr, int seg = 24)
        {
            // Rays from the hole centre to the rectangle boundary, including the four corners.
            var angles = new List<double>();
            for (int i = 0; i < seg; i++) angles.Add(2 * Math.PI * i / seg);
            foreach (var (cx, cy) in new[] { (x1, y1), (x0, y1), (x0, y0), (x1, y0) })
            {
                double a = Math.Atan2(cy - hy, cx - hx);
                if (a < 0) a += 2 * Math.PI;
                angles.Add(a);
            }

            angles.Sort();
            Point Outer(double a)
            {
                double dx = Math.Cos(a), dy = Math.Sin(a);
                double t = double.MaxValue;
                if (dx > 1e-12) t = Math.Min(t, (x1 - hx) / dx);
                if (dx < -1e-12) t = Math.Min(t, (x0 - hx) / dx);
                if (dy > 1e-12) t = Math.Min(t, (y1 - hy) / dy);
                if (dy < -1e-12) t = Math.Min(t, (y0 - hy) / dy);
                return new Point(hx + dx * t, hy + dy * t);
            }

            int m = angles.Count;
            for (int face = 0; face < 2; face++)
            {
                double z = face == 0 ? z1 : z0;
                var n = new Vector3D(0, 0, face == 0 ? 1 : -1);
                for (int i = 0; i < m; i++)
                {
                    double a0 = angles[i], a1 = angles[(i + 1) % m] + (i + 1 == m ? 2 * Math.PI : 0);
                    var o0 = Outer(a0);
                    var o1 = Outer(a1);
                    var in0 = new Point(hx + hr * Math.Cos(a0), hy + hr * Math.Sin(a0));
                    var in1 = new Point(hx + hr * Math.Cos(a1), hy + hr * Math.Sin(a1));
                    int a = V(new Point3D(in0.X, in0.Y, z), n, new Point(0, 0));
                    int b = V(new Point3D(o0.X, o0.Y, z), n, new Point(1, 0));
                    int c = V(new Point3D(o1.X, o1.Y, z), n, new Point(1, 1));
                    int d = V(new Point3D(in1.X, in1.Y, z), n, new Point(0, 1));
                    if (face == 0)
                    {
                        Tri(a, b, c);
                        Tri(a, c, d);
                    }
                    else
                    {
                        Tri(a, c, b);
                        Tri(a, d, c);
                    }
                }
            }

            // Outer walls
            Face(new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Vector3D(0, -1, 0));
            Face(new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1), new Vector3D(1, 0, 0));
            Face(new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Vector3D(0, 1, 0));
            Face(new Point3D(x0, y1, z0), new Point3D(x0, y0, z0), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Vector3D(-1, 0, 0));
            // Hole wall (normals pointing into the hole)
            using (_f.At(Mx.T(hx, hy, 0)))
            {
                Lathe(new[] { new Point(hr, z1), new Point(hr, z0) }, seg);
            }

            return this;
        }

        // ------------------------------------------------------------------ surfaces of revolution

        /// <summary>
        /// Revolves a profile of (radius, z) points about the local Z axis. Normals point
        /// to the left of the direction of travel (outwards for a profile running
        /// bottom-centre → rim → top-centre). A repeated point starts a sharp crease.
        /// </summary>
        public MeshBuilder Lathe(IList<Point> profile, int seg, double a0 = 0, double a1 = 2 * Math.PI)
        {
            if (profile.Count < 2) return this;
            double zMin = double.MaxValue, zMax = double.MinValue;
            foreach (var p in profile)
            {
                zMin = Math.Min(zMin, p.Y);
                zMax = Math.Max(zMax, p.Y);
            }

            double zs = Math.Max(1e-9, zMax - zMin);
            int start = 0;
            for (int k = 1; k <= profile.Count; k++)
            {
                bool split = k == profile.Count || (profile[k] - profile[k - 1]).Length < 1e-9;
                if (!split) continue;
                int end = k - 1;
                if (end > start) LatheRun(profile, start, end, seg, a0, a1, zMin, zs);
                start = k;
            }

            return this;
        }

        private void LatheRun(IList<Point> pr, int s, int e, int seg, double a0, double a1, double zMin, double zs)
        {
            int count = e - s + 1;
            var normals = new Vector[count];
            for (int k = 0; k < count; k++)
            {
                var prev = pr[s + Math.Max(0, k - 1)];
                var next = pr[s + Math.Min(count - 1, k + 1)];
                var t = next - prev;
                var nn = new Vector(t.Y, -t.X);
                if (nn.Length > 1e-12) nn.Normalize();
                normals[k] = nn;
            }

            int ring = seg + 1;
            int baseIndex = -1;
            for (int k = 0; k < count; k++)
            {
                var p = pr[s + k];
                for (int j = 0; j <= seg; j++)
                {
                    double a = a0 + (a1 - a0) * j / seg;
                    double c = Math.Cos(a), sn = Math.Sin(a);
                    int idx = V(new Point3D(p.X * c, p.X * sn, p.Y), new Vector3D(normals[k].X * c, normals[k].X * sn, normals[k].Y),
                        new Point((double)j / seg, 1 - (p.Y - zMin) / zs));
                    if (k == 0 && j == 0) baseIndex = idx;
                }
            }

            for (int k = 0; k + 1 < count; k++)
            {
                bool poleA = Math.Abs(pr[s + k].X) < 1e-9, poleB = Math.Abs(pr[s + k + 1].X) < 1e-9;
                for (int j = 0; j < seg; j++)
                {
                    int a = baseIndex + k * ring + j, b = a + 1, c = a + ring + 1, d = a + ring;
                    if (!poleA) Tri(a, b, c);
                    if (!poleB) Tri(a, c, d);
                    if (poleA && !poleB) continue;
                }
            }
        }

        /// <summary>Closed cylinder along local Z from z0 to z1.</summary>
        public MeshBuilder Cylinder(double r, double z0, double z1, int seg = 24, bool bottom = true, bool top = true)
        {
            var pr = new List<Point>();
            if (bottom)
            {
                pr.Add(new Point(0, z0));
                pr.Add(new Point(r, z0));
            }

            pr.Add(new Point(r, z0));
            pr.Add(new Point(r, z1));
            if (top)
            {
                pr.Add(new Point(r, z1));
                pr.Add(new Point(0, z1));
            }

            return Lathe(pr, seg);
        }

        /// <summary>Vertical cylinder centred at (cx, cy) from z0 to z1.</summary>
        public MeshBuilder CylinderZ(double cx, double cy, double z0, double z1, double r, int seg = 24, bool bottom = true, bool top = true)
        {
            using (_f.At(Mx.T(cx, cy, 0))) Cylinder(r, z0, z1, seg, bottom, top);
            return this;
        }

        /// <summary>Cone frustum along local Z.</summary>
        public MeshBuilder Frustum(double r0, double r1, double z0, double z1, int seg = 24, bool bottom = true, bool top = true)
        {
            var pr = new List<Point>();
            if (bottom)
            {
                pr.Add(new Point(0, z0));
                pr.Add(new Point(r0, z0));
            }

            pr.Add(new Point(r0, z0));
            pr.Add(new Point(r1, z1));
            if (top && r1 > 1e-9)
            {
                pr.Add(new Point(r1, z1));
                pr.Add(new Point(0, z1));
            }

            return Lathe(pr, seg);
        }

        /// <summary>Cylinder between two points.</summary>
        public MeshBuilder Rod(Point3D a, Point3D b, double r, int seg = 12, bool caps = true)
        {
            double len = (b - a).Length;
            if (len < 1e-9) return this;
            using (_f.At(Mx.Along(a, b))) Cylinder(r, 0, len, seg, caps, caps);
            return this;
        }

        public MeshBuilder Sphere(double cx, double cy, double cz, double r, int seg = 16)
        {
            int rings = Math.Max(4, seg / 2);
            var pr = new List<Point>();
            for (int i = 0; i <= rings; i++)
            {
                double phi = -Math.PI / 2 + Math.PI * i / rings;
                pr.Add(new Point(r * Math.Cos(phi), r * Math.Sin(phi)));
            }

            pr[0] = new Point(0, -r);
            pr[rings] = new Point(0, r);
            using (_f.At(Mx.T(cx, cy, cz))) Lathe(pr, seg);
            return this;
        }

        /// <summary>Torus about local Z (major radius R, tube radius r).</summary>
        public MeshBuilder Torus(double R, double r, int seg = 32, int tubeSeg = 12, double z = 0)
        {
            var pr = new List<Point>();
            for (int i = 0; i <= tubeSeg; i++)
            {
                double a = -Math.PI + 2 * Math.PI * i / tubeSeg;
                pr.Add(new Point(R + r * Math.Cos(a), z + r * Math.Sin(a)));
            }

            return Lathe(pr, seg);
        }

        // ------------------------------------------------------------------ tubes (leads, wires, cables)

        /// <summary>Round tube swept along a polyline (use <see cref="Smooth"/> for bends).</summary>
        public MeshBuilder Tube(IList<Point3D> path, double r, int seg = 10, bool capStart = true, bool capEnd = true)
        {
            int n = path.Count;
            if (n < 2) return this;
            var t = new Vector3D[n];
            for (int i = 0; i < n; i++)
            {
                Vector3D d;
                if (i == 0) d = path[1] - path[0];
                else if (i == n - 1) d = path[n - 1] - path[n - 2];
                else
                {
                    var d0 = path[i] - path[i - 1];
                    var d1 = path[i + 1] - path[i];
                    if (d0.LengthSquared > 1e-18) d0.Normalize();
                    if (d1.LengthSquared > 1e-18) d1.Normalize();
                    d = d0 + d1;
                    if (d.LengthSquared < 1e-12) d = d1;
                }

                if (d.LengthSquared < 1e-18) d = i > 0 ? t[i - 1] : new Vector3D(0, 0, 1);
                d.Normalize();
                t[i] = d;
            }

            var helper = Math.Abs(t[0].Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var nrm = Vector3D.CrossProduct(helper, t[0]);
            nrm.Normalize();
            int ring = seg + 1;
            int first = -1;
            double along = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    nrm -= Vector3D.DotProduct(nrm, t[i]) * t[i];
                    if (nrm.LengthSquared < 1e-12) nrm = Vector3D.CrossProduct(helper, t[i]);
                    nrm.Normalize();
                    along += (path[i] - path[i - 1]).Length;
                }

                var bin = Vector3D.CrossProduct(t[i], nrm);
                for (int j = 0; j <= seg; j++)
                {
                    double a = 2 * Math.PI * j / seg;
                    var dir = Math.Cos(a) * nrm + Math.Sin(a) * bin;
                    int k = V(path[i] + dir * r, dir, new Point((double)j / seg, along));
                    if (i == 0 && j == 0) first = k;
                }
            }

            for (int i = 0; i + 1 < n; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int a = first + i * ring + j, b = a + 1, c = a + ring + 1, d = a + ring;
                    Tri(a, b, c);
                    Tri(a, c, d);
                }
            }

            if (capStart) Cap(path[0], -t[0], r, seg);
            if (capEnd) Cap(path[n - 1], t[n - 1], r, seg);
            return this;
        }

        private void Cap(Point3D c, Vector3D normal, double r, int seg)
        {
            var helper = Math.Abs(normal.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(helper, normal);
            u.Normalize();
            var v = Vector3D.CrossProduct(normal, u);
            int centre = V(c, normal, new Point(0.5, 0.5));
            int first = -1;
            for (int j = 0; j <= seg; j++)
            {
                double a = 2 * Math.PI * j / seg;
                int k = V(c + (Math.Cos(a) * u + Math.Sin(a) * v) * r, normal, new Point(0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a)));
                if (j == 0) first = k;
            }

            for (int j = 0; j < seg; j++) Tri(centre, first + j, first + j + 1);
        }

        /// <summary>Rectangular-section strip swept along a polyline in a plane (flat leads, tabs).</summary>
        public MeshBuilder Strip(IList<Point3D> path, double width, double thickness, Vector3D side)
        {
            side.Normalize();
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var d = b - a;
                if (d.LengthSquared < 1e-18) continue;
                d.Normalize();
                var up = Vector3D.CrossProduct(d, side);
                up.Normalize();
                var w = side * (width / 2);
                var h = up * (thickness / 2);
                // Slight overlap at the joints hides seams.
                var ext = d * Math.Min(thickness / 2, (b - a).Length * 0.25);
                var a2 = i > 0 ? a - ext : a;
                var b2 = i + 2 < path.Count ? b + ext : b;
                Hexa(a2 - w - h, a2 + w - h, a2 + w + h, a2 - w + h, b2 - w - h, b2 + w - h, b2 + w + h, b2 - w + h);
            }

            return this;
        }

        /// <summary>Rounds the corners of a polyline with quadratic arcs.</summary>
        public static List<Point3D> Smooth(IList<Point3D> corners, double radius, int seg = 6)
        {
            var result = new List<Point3D> { corners[0] };
            for (int i = 1; i + 1 < corners.Count; i++)
            {
                var p = corners[i];
                var din = p - corners[i - 1];
                var dout = corners[i + 1] - p;
                double lin = din.Length, lout = dout.Length;
                if (lin < 1e-9 || lout < 1e-9)
                {
                    result.Add(p);
                    continue;
                }

                din /= lin;
                dout /= lout;
                double l = Math.Min(radius, Math.Min(lin, lout) * 0.5);
                var a = p - din * l;
                var b = p + dout * l;
                for (int k = 0; k <= seg; k++)
                {
                    double t = (double)k / seg;
                    double u = 1 - t;
                    result.Add(new Point3D(u * u * a.X + 2 * u * t * p.X + t * t * b.X, u * u * a.Y + 2 * u * t * p.Y + t * t * b.Y, u * u * a.Z + 2 * u * t * p.Z + t * t * b.Z));
                }
            }

            result.Add(corners[corners.Count - 1]);
            return result;
        }

        // ------------------------------------------------------------------ textured quads

        /// <summary>Horizontal rectangle facing +Z; texture u along +x, v along +y of the builder frame.</summary>
        public MeshBuilder TopQuad(double cx, double cy, double z, double sx, double sy, double rotationDeg = 0)
        {
            using (_f.At(Mx.Rz(rotationDeg) * Mx.T(cx, cy, z)))
            {
                var n = new Vector3D(0, 0, 1);
                int a = V(new Point3D(-sx / 2, -sy / 2, 0), n, new Point(0, 0));
                int b = V(new Point3D(sx / 2, -sy / 2, 0), n, new Point(1, 0));
                int c = V(new Point3D(sx / 2, sy / 2, 0), n, new Point(1, 1));
                int d = V(new Point3D(-sx / 2, sy / 2, 0), n, new Point(0, 1));
                Tri(a, b, c);
                Tri(a, c, d);
            }

            return this;
        }

        /// <summary>
        /// Rectangle facing <paramref name="normal"/> with the texture upright along
        /// <paramref name="up"/> as seen by a viewer in front of it (in the final model frame).
        /// </summary>
        public MeshBuilder FrontQuad(Point3D centre, Vector3D normal, Vector3D up, double w, double h)
        {
            normal.Normalize();
            up.Normalize();
            var right = _f.Mirrored ? Vector3D.CrossProduct(normal, up) : Vector3D.CrossProduct(up, normal);
            right.Normalize();
            var tl = centre - right * (w / 2) + up * (h / 2);
            var tr = centre + right * (w / 2) + up * (h / 2);
            var br = centre + right * (w / 2) - up * (h / 2);
            var bl = centre - right * (w / 2) - up * (h / 2);
            int a = V(tl, normal, new Point(0, 0)), b = V(tr, normal, new Point(1, 0)), c = V(br, normal, new Point(1, 1)), d = V(bl, normal, new Point(0, 1));
            // Wind so the face points along the normal in the builder's local frame.
            if (Vector3D.DotProduct(Vector3D.CrossProduct(tr - tl, br - tl), normal) >= 0)
            {
                Tri(a, b, c);
                Tri(a, c, d);
            }
            else
            {
                Tri(a, c, b);
                Tri(a, d, c);
            }

            return this;
        }
    }
}
