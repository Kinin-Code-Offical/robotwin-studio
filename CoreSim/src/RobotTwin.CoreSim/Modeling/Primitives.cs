using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>Describes one editable parameter of a primitive (for property panels).</summary>
    public sealed class ParamSpec
    {
        public ParamSpec(string key, string label, string unit, double @default, double min, double max, bool integer = false)
        {
            Key = key;
            Label = label;
            Unit = unit;
            Default = @default;
            Min = min;
            Max = max;
            Integer = integer;
        }

        public string Key { get; }
        public string Label { get; }
        public string Unit { get; }
        public double Default { get; }
        public double Min { get; }
        public double Max { get; }
        public bool Integer { get; }

        public double Clamp(double v)
        {
            v = Math.Max(Min, Math.Min(Max, v));
            return Integer ? Math.Round(v) : v;
        }
    }

    /// <summary>Parameter lists and Turkish names of the primitive kinds.</summary>
    public static class PrimitiveSchema
    {
        private const double Big = 10000;

        private static readonly Dictionary<PrimitiveKind, ParamSpec[]> Specs = new Dictionary<PrimitiveKind, ParamSpec[]>
        {
            [PrimitiveKind.Box] = new[]
            {
                new ParamSpec("x", "Genişlik X", "mm", 20, 0.01, Big), new ParamSpec("y", "Derinlik Y", "mm", 20, 0.01, Big), new ParamSpec("z", "Yükseklik Z", "mm", 20, 0.01, Big),
                new ParamSpec("fillet", "Kenar yuvarlatma", "mm", 0, 0, Big), new ParamSpec("chamfer", "Pah", "mm", 0, 0, Big), new ParamSpec("seg", "Yuvarlatma bölümü", "", 4, 1, 32, true)
            },
            [PrimitiveKind.Cylinder] = new[]
            {
                new ParamSpec("d", "Çap", "mm", 20, 0.01, Big), new ParamSpec("h", "Yükseklik", "mm", 20, 0.01, Big), new ParamSpec("seg", "Bölüm sayısı", "", 48, 3, 256, true)
            },
            [PrimitiveKind.Cone] = new[]
            {
                new ParamSpec("d1", "Alt çap", "mm", 20, 0, Big), new ParamSpec("d2", "Üst çap", "mm", 0, 0, Big), new ParamSpec("h", "Yükseklik", "mm", 20, 0.01, Big),
                new ParamSpec("seg", "Bölüm sayısı", "", 48, 3, 256, true)
            },
            [PrimitiveKind.Sphere] = new[]
            {
                new ParamSpec("d", "Çap", "mm", 20, 0.01, Big), new ParamSpec("seg", "Bölüm sayısı", "", 48, 4, 256, true), new ParamSpec("rings", "Halka sayısı", "", 24, 3, 128, true)
            },
            [PrimitiveKind.Torus] = new[]
            {
                new ParamSpec("dm", "Ana çap", "mm", 30, 0.01, Big), new ParamSpec("dt", "Kesit çapı", "mm", 8, 0.01, Big),
                new ParamSpec("seg", "Bölüm sayısı", "", 48, 3, 256, true), new ParamSpec("rings", "Kesit bölümü", "", 20, 3, 128, true)
            },
            [PrimitiveKind.Tube] = new[]
            {
                new ParamSpec("do", "Dış çap", "mm", 20, 0.02, Big), new ParamSpec("di", "İç çap", "mm", 14, 0.01, Big), new ParamSpec("h", "Yükseklik", "mm", 20, 0.01, Big),
                new ParamSpec("seg", "Bölüm sayısı", "", 48, 3, 256, true)
            },
            [PrimitiveKind.Wedge] = new[]
            {
                new ParamSpec("x", "Genişlik X", "mm", 20, 0.01, Big), new ParamSpec("y", "Derinlik Y", "mm", 20, 0.01, Big), new ParamSpec("z", "Yükseklik Z", "mm", 10, 0.01, Big)
            },
            [PrimitiveKind.Plate] = new[]
            {
                new ParamSpec("x", "Genişlik X", "mm", 80, 0.1, Big), new ParamSpec("y", "Derinlik Y", "mm", 50, 0.1, Big), new ParamSpec("t", "Kalınlık", "mm", 3, 0.05, Big),
                new ParamSpec("r", "Köşe yarıçapı", "mm", 4, 0, Big), new ParamSpec("hd", "Köşe deliği çapı", "mm", 3.2, 0, Big), new ParamSpec("hi", "Delik kenar payı", "mm", 5, 0, Big),
                new ParamSpec("seg", "Bölüm sayısı", "", 32, 8, 256, true)
            },
            [PrimitiveKind.Extrude] = new[]
            {
                new ParamSpec("h", "Kalınlık (Z)", "mm", 5, 0.01, Big), new ParamSpec("seg", "Bölüm sayısı", "", 48, 8, 256, true)
            },
            [PrimitiveKind.Revolve] = new[]
            {
                new ParamSpec("angle", "Açı", "°", 360, 1, 360), new ParamSpec("seg", "Bölüm sayısı", "", 48, 3, 256, true)
            }
        };

        public static IReadOnlyList<ParamSpec> For(PrimitiveKind kind) => Specs.TryGetValue(kind, out var s) ? s : Array.Empty<ParamSpec>();

        public static IReadOnlyList<ParamSpec> For(PrimitiveSpec spec) =>
            spec.Kind == PrimitiveKind.Part ? RobotParts.Find(spec.Template)?.Params ?? (IReadOnlyList<ParamSpec>)Array.Empty<ParamSpec>() : For(spec.Kind);

        public static string Name(PrimitiveKind kind) => kind switch
        {
            PrimitiveKind.Box => "Kutu",
            PrimitiveKind.Cylinder => "Silindir",
            PrimitiveKind.Cone => "Koni",
            PrimitiveKind.Sphere => "Küre",
            PrimitiveKind.Torus => "Simit",
            PrimitiveKind.Tube => "Boru",
            PrimitiveKind.Wedge => "Kama",
            PrimitiveKind.Plate => "Delikli plaka",
            PrimitiveKind.Extrude => "Profil",
            PrimitiveKind.Revolve => "Döner gövde",   // a profile turned about an axis (not "Döndürme": that is the rotation fields' word)
            _ => "Parça"
        };

        /// <summary>A new spec of the kind with default parameters (and a default sketch where needed).</summary>
        public static PrimitiveSpec Create(PrimitiveKind kind)
        {
            var s = new PrimitiveSpec { Kind = kind };
            foreach (var p in For(kind)) s.Values[p.Key] = p.Default;
            if (kind == PrimitiveKind.Extrude)
            {
                s.Sketch = new Sketch2D { Outer = SketchShape.Rect(40, 24, 0, 0, 3) };
                s.Sketch.Holes.Add(SketchShape.Circle(8));
            }
            else if (kind == PrimitiveKind.Revolve)
            {
                // A pulley-like profile: X = radius, Y = height.
                s.Sketch = new Sketch2D
                {
                    Outer = new SketchShape { Kind = SketchShapeKind.Custom, Points = new List<double> { 3, 0, 15, 0, 15, 2, 12, 4, 12, 8, 15, 10, 15, 12, 3, 12 } }
                };
            }
            else if (kind == PrimitiveKind.Plate) s.Holes = new List<HoleSpec>();

            return s;
        }

        /// <summary>Height of the generated solid below its origin (to place new objects on the floor).</summary>
        public static double BottomOffset(PrimitiveSpec s)
        {
            var b = PrimitiveBuilder.Build(s).Bounds;
            return b.IsEmpty ? 0 : -b.Min.Z;
        }
    }

    /// <summary>Generates closed polygon meshes (mm, centred on the origin) for primitives.</summary>
    public static class PrimitiveBuilder
    {
        public static PolyMesh Build(PrimitiveSpec s)
        {
            double G(string k) => s.Get(k);
            int I(string k) => (int)Math.Round(Math.Max(1, s.Get(k)));
            switch (s.Kind)
            {
                case PrimitiveKind.Box:
                {
                    double ch = G("chamfer"), fi = G("fillet");
                    if (ch > 0) return RoundedBox(G("x"), G("y"), G("z"), ch, 1, true);
                    return RoundedBox(G("x"), G("y"), G("z"), fi, Math.Max(1, I("seg")), false);
                }

                case PrimitiveKind.Cylinder: return Cone(G("d") / 2, G("d") / 2, G("h"), I("seg"));
                case PrimitiveKind.Cone: return Cone(G("d1") / 2, G("d2") / 2, G("h"), I("seg"));
                case PrimitiveKind.Sphere: return Sphere(G("d") / 2, I("seg"), I("rings"));
                case PrimitiveKind.Torus: return Torus(G("dm") / 2, G("dt") / 2, I("seg"), I("rings"));
                case PrimitiveKind.Tube: return Tube(G("do") / 2, G("di") / 2, G("h"), I("seg"));
                case PrimitiveKind.Wedge: return Wedge(G("x"), G("y"), G("z"));
                case PrimitiveKind.Plate: return Plate(G("x"), G("y"), G("t"), G("r"), G("hd"), G("hi"), s.Holes, I("seg"));
                case PrimitiveKind.Extrude:
                {
                    var sk = s.Sketch ?? new Sketch2D();
                    int seg = I("seg");
                    return Extrude(sk.Outer.ToPolygon(seg), sk.Holes.Select(h => h.ToPolygon(seg)).ToList(), G("h"));
                }

                case PrimitiveKind.Revolve:
                {
                    var sk = s.Sketch ?? new Sketch2D();
                    return Revolve(sk.Outer.ToPolygon(I("seg")), G("angle"), I("seg"));
                }

                case PrimitiveKind.Part:
                    return RobotParts.Build(s);
                default:
                    return Box(20, 20, 20);
            }
        }

        // ------------------------------------------------------------------ boxes

        public static PolyMesh Box(double sx, double sy, double sz) => RoundedBox(sx, sy, sz, 0, 1, false);

        /// <summary>
        /// Box with every edge rounded (radius <paramref name="r"/>, <paramref name="segments"/> per quarter)
        /// or chamfered. Built by projecting a subdivided cube onto the offset surface.
        /// </summary>
        public static PolyMesh RoundedBox(double sx, double sy, double sz, double r, int segments, bool chamfer)
        {
            var h = new[] { Math.Abs(sx) / 2, Math.Abs(sy) / 2, Math.Abs(sz) / 2 };
            r = Math.Max(0, Math.Min(r, Math.Min(h[0], Math.Min(h[1], h[2])) * 0.999));
            var inner = new[] { h[0] - r, h[1] - r, h[2] - r };
            int k = r <= 1e-9 ? 0 : chamfer ? 1 : Math.Max(1, segments);
            // zone offsets: 0 … r (tan spacing gives uniform angles for fillets)
            var offs = new double[k + 1];
            for (int i = 0; i <= k; i++) offs[i] = k == 0 ? 0 : chamfer ? r * i / k : r * Math.Tan(Math.PI / 4 * i / k);
            double[] Samples(int axis)
            {
                var l = new List<double>();
                for (int i = k; i >= 0; i--) l.Add(-inner[axis] - offs[i]);
                for (int i = 0; i <= k; i++) l.Add(inner[axis] + offs[i]);
                var d = new List<double>();
                foreach (double v in l)
                {
                    if (d.Count == 0 || Math.Abs(v - d[d.Count - 1]) > 1e-9) d.Add(v);
                }

                return d.ToArray();
            }

            var m = new PolyMesh();
            for (int a = 0; a < 3; a++)
            {
                int b = (a + 1) % 3, c = (a + 2) % 3;
                var sb = Samples(b);
                var sc = Samples(c);
                foreach (int sign in new[] { 1, -1 })
                {
                    var idx = new int[sb.Length, sc.Length];
                    for (int i = 0; i < sb.Length; i++)
                    {
                        for (int j = 0; j < sc.Length; j++)
                        {
                            var p = new double[3];
                            p[a] = sign * (inner[a] + (k == 0 ? 0 : r));
                            p[b] = sb[i];
                            p[c] = sc[j];
                            idx[i, j] = m.AddVertex(Project(p, inner, r, chamfer));
                        }
                    }

                    for (int i = 0; i + 1 < sb.Length; i++)
                    {
                        for (int j = 0; j + 1 < sc.Length; j++)
                        {
                            if (sign > 0) m.AddFace(idx[i, j], idx[i + 1, j], idx[i + 1, j + 1], idx[i, j + 1]);
                            else m.AddFace(idx[i, j], idx[i, j + 1], idx[i + 1, j + 1], idx[i + 1, j]);
                        }
                    }
                }
            }

            double size = Math.Max(h[0], Math.Max(h[1], h[2]));
            m.Weld(Math.Max(1e-9, size * 1e-9));
            m.RemoveDegenerateFaces(1e-14);
            if (chamfer) m.DissolveCoplanar(1e-3, 1e-7);
            return m;
        }

        private static Vec3 Project(double[] p, double[] inner, double r, bool chamfer)
        {
            var q = new double[3];
            var d = new double[3];
            for (int i = 0; i < 3; i++)
            {
                q[i] = Math.Max(-inner[i], Math.Min(inner[i], p[i]));
                d[i] = p[i] - q[i];
            }

            double len = chamfer ? Math.Abs(d[0]) + Math.Abs(d[1]) + Math.Abs(d[2]) : Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            if (len < 1e-15 || r <= 0) return new Vec3(p[0], p[1], p[2]);
            double s = r / len;
            return new Vec3(q[0] + d[0] * s, q[1] + d[1] * s, q[2] + d[2] * s);
        }

        // ------------------------------------------------------------------ round solids

        /// <summary>Cone / cylinder / frustum along Z centred on the origin (a zero radius makes an apex).</summary>
        public static PolyMesh Cone(double r1, double r2, double height, int segments)
        {
            segments = Math.Max(3, segments);
            r1 = Math.Max(0, r1);
            r2 = Math.Max(0, r2);
            if (r1 <= 0 && r2 <= 0) r1 = 1e-3;
            double z0 = -height / 2, z1 = height / 2;
            var m = new PolyMesh();
            int[] Ring(double r, double z)
            {
                if (r <= 0) return new[] { m.AddVertex(new Vec3(0, 0, z)) };
                var ring = new int[segments];
                for (int i = 0; i < segments; i++)
                {
                    double a = 2 * Math.PI * i / segments;
                    ring[i] = m.AddVertex(new Vec3(r * Math.Cos(a), r * Math.Sin(a), z));
                }

                return ring;
            }

            var bottom = Ring(r1, z0);
            var top = Ring(r2, z1);
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                if (bottom.Length == 1) m.AddFace(bottom[0], top[j], top[i]);
                else if (top.Length == 1) m.AddFace(bottom[i], bottom[j], top[0]);
                else m.AddFace(bottom[i], bottom[j], top[j], top[i]);
            }

            if (bottom.Length > 1) m.AddFace(Enumerable.Reverse(bottom).ToArray());
            if (top.Length > 1) m.AddFace(top.ToArray());
            return m;
        }

        public static PolyMesh Cylinder(double radius, double height, int segments = 48) => Cone(radius, radius, height, segments);

        public static PolyMesh Sphere(double radius, int segments = 48, int rings = 24)
        {
            segments = Math.Max(3, segments);
            rings = Math.Max(2, rings);
            var m = new PolyMesh();
            int south = m.AddVertex(new Vec3(0, 0, -radius));
            for (int r = 1; r < rings; r++)
            {
                double phi = Math.PI * r / rings - Math.PI / 2;
                for (int s = 0; s < segments; s++)
                {
                    double th = 2 * Math.PI * s / segments;
                    m.AddVertex(new Vec3(radius * Math.Cos(phi) * Math.Cos(th), radius * Math.Cos(phi) * Math.Sin(th), radius * Math.Sin(phi)));
                }
            }

            int north = m.AddVertex(new Vec3(0, 0, radius));
            int V(int r, int s) => 1 + (r - 1) * segments + (s % segments);
            for (int s = 0; s < segments; s++)
            {
                m.AddFace(south, V(1, s + 1), V(1, s));
                m.AddFace(north, V(rings - 1, s), V(rings - 1, s + 1));
            }

            for (int r = 1; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++) m.AddFace(V(r, s), V(r, s + 1), V(r + 1, s + 1), V(r + 1, s));
            }

            return m;
        }

        public static PolyMesh Torus(double major, double minor, int segments = 48, int rings = 20)
        {
            segments = Math.Max(3, segments);
            rings = Math.Max(3, rings);
            minor = Math.Min(minor, major * 0.999);
            var m = new PolyMesh();
            for (int i = 0; i < segments; i++)
            {
                double u = 2 * Math.PI * i / segments;
                for (int j = 0; j < rings; j++)
                {
                    double v = 2 * Math.PI * j / rings;
                    double rr = major + minor * Math.Cos(v);
                    m.AddVertex(new Vec3(rr * Math.Cos(u), rr * Math.Sin(u), minor * Math.Sin(v)));
                }
            }

            int V(int i, int j) => (i % segments) * rings + (j % rings);
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < rings; j++) m.AddFace(V(i, j), V(i + 1, j), V(i + 1, j + 1), V(i, j + 1));
            }

            return m;
        }

        public static PolyMesh Tube(double outer, double inner, double height, int segments = 48)
        {
            segments = Math.Max(3, segments);
            inner = Math.Max(0, Math.Min(inner, outer * 0.999));
            if (inner <= 1e-9) return Cylinder(outer, height, segments);
            var m = new PolyMesh();
            double z0 = -height / 2, z1 = height / 2;
            var ob = new int[segments];
            var ot = new int[segments];
            var ib = new int[segments];
            var it = new int[segments];
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments, c = Math.Cos(a), s = Math.Sin(a);
                ob[i] = m.AddVertex(new Vec3(outer * c, outer * s, z0));
                ot[i] = m.AddVertex(new Vec3(outer * c, outer * s, z1));
                ib[i] = m.AddVertex(new Vec3(inner * c, inner * s, z0));
                it[i] = m.AddVertex(new Vec3(inner * c, inner * s, z1));
            }

            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                m.AddFace(ob[i], ob[j], ot[j], ot[i]);
                m.AddFace(ib[j], ib[i], it[i], it[j]);
                m.AddFace(ot[i], ot[j], it[j], it[i]);
                m.AddFace(ob[j], ob[i], ib[i], ib[j]);
            }

            return m;
        }

        /// <summary>Ramp: full height at −X, zero at +X, centred on the origin.</summary>
        public static PolyMesh Wedge(double sx, double sy, double sz)
        {
            double x0 = -sx / 2, x1 = sx / 2, y0 = -sy / 2, y1 = sy / 2, z0 = -sz / 2, z1 = sz / 2;
            var m = new PolyMesh();
            int a = m.AddVertex(new Vec3(x0, y0, z0)), b = m.AddVertex(new Vec3(x1, y0, z0)), c = m.AddVertex(new Vec3(x1, y1, z0)), d = m.AddVertex(new Vec3(x0, y1, z0));
            int e = m.AddVertex(new Vec3(x0, y0, z1)), f = m.AddVertex(new Vec3(x0, y1, z1));
            m.AddFace(a, d, c, b); // bottom
            m.AddFace(a, e, f, d); // back (−X)
            m.AddFace(e, b, c, f); // slope
            m.AddFace(a, b, e); // −Y
            m.AddFace(d, f, c); // +Y
            return m;
        }

        // ------------------------------------------------------------------ sketches

        public static PolyMesh Plate(double sx, double sy, double t, double corner, double holeD, double inset, IEnumerable<HoleSpec>? holes, int segments)
        {
            var outer = SketchShape.Rect(sx, sy, 0, 0, corner).ToPolygon(segments);
            var hs = new List<List<Vec2>>();
            if (holeD > 0)
            {
                double hx = sx / 2 - inset, hy = sy / 2 - inset;
                if (hx > holeD / 2 && hy > holeD / 2)
                {
                    foreach (var (x, y) in new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) }) hs.Add(SketchShape.Circle(holeD, x, y).ToPolygon(Math.Max(16, segments / 2)));
                }
            }

            if (holes != null)
            {
                foreach (var h in holes)
                {
                    if (h.D > 0) hs.Add(SketchShape.Circle(h.D, h.X, h.Y).ToPolygon(Math.Max(16, segments / 2)));
                }
            }

            return Extrude(outer, hs, t);
        }

        /// <summary>Extrudes an outline with holes from z = −h/2 to +h/2.</summary>
        public static PolyMesh Extrude(List<Vec2> outer, List<List<Vec2>> holes, double height)
        {
            var m = new PolyMesh();
            if (outer.Count < 3) return m;
            outer = Ccw(outer, true);
            var hs = holes.Where(h => h.Count >= 3).Select(h => Ccw(h, false)).ToList();
            double z0 = -height / 2, z1 = height / 2;
            var loops = new List<List<Vec2>> { outer };
            loops.AddRange(hs);
            var bottomIdx = new List<int[]>();
            var topIdx = new List<int[]>();
            foreach (var loop in loops)
            {
                var b = new int[loop.Count];
                var t = new int[loop.Count];
                for (int i = 0; i < loop.Count; i++)
                {
                    b[i] = m.AddVertex(new Vec3(loop[i].X, loop[i].Y, z0));
                    t[i] = m.AddVertex(new Vec3(loop[i].X, loop[i].Y, z1));
                }

                for (int i = 0; i < loop.Count; i++)
                {
                    int j = (i + 1) % loop.Count;
                    m.AddFace(b[i], b[j], t[j], t[i]);
                }

                bottomIdx.Add(b);
                topIdx.Add(t);
            }

            if (hs.Count == 0)
            {
                m.AddFace(topIdx[0].ToArray());
                m.AddFace(Enumerable.Reverse(bottomIdx[0]).ToArray());
                return m;
            }

            var flat = new List<int>();
            var flatTop = new List<int>();
            foreach (var l in bottomIdx) flat.AddRange(l);
            foreach (var l in topIdx) flatTop.AddRange(l);
            var tri = Triangulator.Triangulate(outer, hs.Cast<IReadOnlyList<Vec2>>().ToList());
            for (int i = 0; i + 2 < tri.Count; i += 3)
            {
                m.AddFace(flatTop[tri[i]], flatTop[tri[i + 1]], flatTop[tri[i + 2]]);
                m.AddFace(flat[tri[i]], flat[tri[i + 2]], flat[tri[i + 1]]);
            }

            m.DissolveCoplanar(1e-3, 1e-7);
            return m;
        }

        /// <summary>Revolves a profile (X = radius ≥ 0, Y = height) about Z by <paramref name="angleDeg"/>.</summary>
        public static PolyMesh Revolve(List<Vec2> profile, double angleDeg, int segments)
        {
            var m = new PolyMesh();
            var prof = profile.Select(p => new Vec2(Math.Max(0, p.X), p.Y)).ToList();
            if (prof.Count < 3) return m;
            prof = Ccw(prof, true);
            angleDeg = Math.Max(1, Math.Min(360, angleDeg));
            bool full = angleDeg >= 359.999;
            int steps = Math.Max(1, (int)Math.Ceiling(segments * angleDeg / 360.0));
            int rings = full ? steps : steps + 1;
            int n = prof.Count;
            var idx = new int[rings, n];
            for (int s = 0; s < rings; s++)
            {
                double a = angleDeg * Math.PI / 180 * s / steps;
                double c = Math.Cos(a), sn = Math.Sin(a);
                for (int i = 0; i < n; i++) idx[s, i] = m.AddVertex(new Vec3(prof[i].X * c, prof[i].X * sn, prof[i].Y));
            }

            for (int s = 0; s < steps; s++)
            {
                int s1 = (s + 1) % rings;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    m.AddFace(idx[s, i], idx[s1, i], idx[s1, j], idx[s, j]);
                }
            }

            if (!full)
            {
                var start = new int[n];
                var end = new int[n];
                for (int i = 0; i < n; i++)
                {
                    start[i] = idx[0, i];
                    end[i] = idx[rings - 1, n - 1 - i];
                }

                m.AddFace(start);
                m.AddFace(end);
            }

            double size = prof.Max(p => Math.Max(Math.Abs(p.X), Math.Abs(p.Y)));
            m.Weld(Math.Max(1e-9, size * 1e-9));
            m.RemoveDegenerateFaces(1e-14);
            return m;
        }

        private static List<Vec2> Ccw(List<Vec2> loop, bool ccw)
        {
            var l = new List<Vec2>(loop);
            if ((Triangulator.SignedArea(l) > 0) != ccw) l.Reverse();
            return l;
        }
    }
}
