using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[analysis] Each face of a body put in a class (−1: none), with the area (mm²) of every class.</summary>
    public sealed class FaceClasses
    {
        public FaceClasses(int faces, int classes)
        {
            Class = Enumerable.Repeat(-1, faces).ToArray();
            Area = new double[classes];
        }

        public int[] Class { get; }
        public double[] Area { get; }
    }

    /// <summary>
    /// [analysis] SolidWorks' Evaluate analyses on a body's faces, in the world (the body's mesh in mm, its placement):
    /// <see cref="Draft"/> (Draft Analysis: which faces leave a mould along the pull direction), <see cref="Curvature"/>
    /// (Curvature: how tightly each face bends, in radius bands) and <see cref="Symmetry"/> (Symmetry Check: which faces
    /// have a mirror image on the body across a plane). Display only; the classes colour the faces.
    /// </summary>
    public static class FaceAnalysis
    {
        // ================================================================ draft

        public const int DraftPositive = 0, DraftNegative = 1, DraftRequired = 2;

        /// <summary>
        /// Draft Analysis: a face's draft is 90° less its angle to the pull direction (asin n·p). At least
        /// <paramref name="minimumDegrees"/>: positive (it comes off the core side); at most −that: negative (the cavity
        /// side); in between, square to the pull or nearly: it needs draft (it would drag along the mould).
        /// </summary>
        public static FaceClasses Draft(PolyMesh mesh, Affine toWorld, Vec3 pull, double minimumDegrees)
        {
            var result = new FaceClasses(mesh.Faces.Count, 3);
            var p = pull.Normalized();
            double s = Math.Sin(Math.Max(0, minimumDegrees) * Math.PI / 180);
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var (n, area) = WorldFace(mesh, toWorld, f);
                if (area <= 0) continue;
                double d = n.Dot(p);
                int c = d >= s && d > 1e-9 ? DraftPositive : d <= -s && d < -1e-9 ? DraftNegative : DraftRequired;
                result.Class[f] = c;
                result.Area[c] += area;
            }

            return result;
        }

        /// <summary>The draft (°) of every face: 90° less its angle to the pull direction.</summary>
        public static double DraftAngle(PolyMesh mesh, Affine toWorld, int face, Vec3 pull)
        {
            var (n, _) = WorldFace(mesh, toWorld, face);
            return Math.Asin(Math.Max(-1, Math.Min(1, n.Dot(pull.Normalized())))) * 180 / Math.PI;
        }

        // ================================================================ curvature

        /// <summary>The radius bands (mm) of <see cref="Curvature"/>: class 0 flat, then tighter: above 50, 10…50, 2…10, below 2.</summary>
        public static readonly double[] CurvatureBands = { 50, 10, 2 };

        /// <summary>
        /// Curvature: how tightly each face bends with its smooth neighbours (the turn between their normals over the
        /// way between their centres, 1/radius); a crease sharper than <paramref name="creaseDegrees"/> is an edge, not a
        /// bend. Class 0: flat (radius over 1000 mm), then one class per band of <paramref name="bands"/> (radii, largest first).
        /// </summary>
        public static FaceClasses Curvature(PolyMesh mesh, Affine toWorld, double[]? bands = null, double creaseDegrees = 50)
        {
            bands ??= CurvatureBands;
            var result = new FaceClasses(mesh.Faces.Count, bands.Length + 2);
            int fc = mesh.Faces.Count;
            var normal = new Vec3[fc];
            var centre = new Vec3[fc];
            var area = new double[fc];
            for (int f = 0; f < fc; f++)
            {
                (normal[f], area[f]) = WorldFace(mesh, toWorld, f);
                centre[f] = toWorld.Apply(mesh.FaceCenter(f));
            }

            double crease = Math.Cos(creaseDegrees * Math.PI / 180);
            var k = new double[fc];
            foreach (var (a, b) in Neighbours(mesh))
            {
                if (area[a] <= 0 || area[b] <= 0) continue;
                double cos = Math.Max(-1, Math.Min(1, normal[a].Dot(normal[b])));
                if (cos < crease) continue;   // a crease, not a bend
                double gap = (centre[a] - centre[b]).Length;
                if (gap <= 1e-12) continue;
                double turn = Math.Acos(cos) / gap;
                if (turn > k[a]) k[a] = turn;
                if (turn > k[b]) k[b] = turn;
            }

            for (int f = 0; f < fc; f++)
            {
                if (area[f] <= 0) continue;
                double r = k[f] > 1e-12 ? 1 / k[f] : double.PositiveInfinity;
                int c = r > 1000 ? 0 : 1;
                while (c >= 1 && c <= bands.Length && r < bands[c - 1]) c++;
                result.Class[f] = c;
                result.Area[c] += area[f];
            }

            return result;
        }

        // ================================================================ symmetry

        public const int Symmetric = 0, Asymmetric = 1;

        /// <summary>
        /// Symmetry Check: a face is symmetric when its mirror image across the plane (through <paramref name="planePoint"/>,
        /// square to <paramref name="planeNormal"/>) lies on the body: its centre and points toward its corners land on a
        /// face of the body facing the mirrored way, within <paramref name="toleranceMm"/> (0: a thousandth of the body's size).
        /// </summary>
        public static FaceClasses Symmetry(PolyMesh mesh, Affine toWorld, Vec3 planePoint, Vec3 planeNormal, double toleranceMm = 0)
        {
            var result = new FaceClasses(mesh.Faces.Count, 2);
            var nPlane = planeNormal.Normalized();
            var tri = Triangles(mesh, toWorld);
            if (tri.Count == 0) return result;
            var lo = tri.Aggregate(new Vec3(double.MaxValue, double.MaxValue, double.MaxValue), (m, t) => Vec3.Min(m, Vec3.Min(t.A, Vec3.Min(t.B, t.C))));
            var hi = tri.Aggregate(new Vec3(double.MinValue, double.MinValue, double.MinValue), (m, t) => Vec3.Max(m, Vec3.Max(t.A, Vec3.Max(t.B, t.C))));
            double size = Math.Max((hi - lo).Length, 1e-9);
            double tol = toleranceMm > 0 ? toleranceMm : Math.Max(1e-3, size * 1e-3);
            var grid = new TriangleGrid(tri, lo, hi);

            Vec3 Mirror(Vec3 p) => p - nPlane * (2 * (p - planePoint).Dot(nPlane));
            Vec3 MirrorDir(Vec3 v) => v - nPlane * (2 * v.Dot(nPlane));
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var (n, area) = WorldFace(mesh, toWorld, f);
                if (area <= 0) continue;
                var c = toWorld.Apply(mesh.FaceCenter(f));
                var nm = MirrorDir(n);
                bool all = grid.OnSurface(Mirror(c), nm, tol);
                foreach (int vi in mesh.Faces[f])
                {
                    if (!all) break;
                    var v = toWorld.Apply(mesh.Vertices[vi]);
                    all = grid.OnSurface(Mirror(c + (v - c) * 0.8), nm, tol);
                }

                int cls = all ? Symmetric : Asymmetric;
                result.Class[f] = cls;
                result.Area[cls] += area;
            }

            return result;
        }

        // ================================================================ helpers

        private static (Vec3 Normal, double Area) WorldFace(PolyMesh mesh, Affine toWorld, int f)
        {
            var pts = mesh.FacePoints(f);
            if (pts.Length < 3) return (Vec3.Zero, 0);
            var n = Vec3.Zero;
            var w = pts.Select(toWorld.Apply).ToArray();
            for (int i = 0; i < w.Length; i++) n += w[i].Cross(w[(i + 1) % w.Length]);
            double len = n.Length;
            return len <= 1e-15 ? (Vec3.Zero, 0) : (n / len, len / 2);
        }

        /// <summary>Faces that share an edge (corners welded by position, so split meshes still join).</summary>
        private static IEnumerable<(int, int)> Neighbours(PolyMesh mesh)
        {
            var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = -lo;
            foreach (var v in mesh.Vertices)
            {
                lo = Vec3.Min(lo, v);
                hi = Vec3.Max(hi, v);
            }

            double eps = Math.Max((hi - lo).Length, 1e-9) * 1e-7;
            var weld = new Dictionary<(long, long, long), int>();
            var id = new int[mesh.Vertices.Count];
            for (int i = 0; i < id.Length; i++)
            {
                var v = mesh.Vertices[i];
                var key = ((long)Math.Round(v.X / eps), (long)Math.Round(v.Y / eps), (long)Math.Round(v.Z / eps));
                if (!weld.TryGetValue(key, out int w)) weld[key] = w = weld.Count;
                id[i] = w;
            }

            var edges = new Dictionary<(int, int), List<int>>();
            for (int f = 0; f < mesh.Faces.Count; f++)
            {
                var face = mesh.Faces[f];
                for (int i = 0; i < face.Length; i++)
                {
                    int a = id[face[i]], b = id[face[(i + 1) % face.Length]];
                    if (a == b) continue;
                    var e = a < b ? (a, b) : (b, a);
                    if (!edges.TryGetValue(e, out var l)) edges[e] = l = new List<int>(2);
                    l.Add(f);
                }
            }

            foreach (var l in edges.Values)
                for (int i = 0; i < l.Count; i++)
                for (int j = i + 1; j < l.Count; j++)
                    if (l[i] != l[j])
                        yield return (l[i], l[j]);
        }

        private readonly struct Tri
        {
            public Tri(Vec3 a, Vec3 b, Vec3 c)
            {
                A = a;
                B = b;
                C = c;
                N = (b - a).Cross(c - a).Normalized();
            }

            public Vec3 A { get; }
            public Vec3 B { get; }
            public Vec3 C { get; }
            public Vec3 N { get; }
        }

        private static List<Tri> Triangles(PolyMesh mesh, Affine toWorld)
        {
            var list = new List<Tri>();
            foreach (var f in mesh.Faces)
            {
                if (f.Length < 3) continue;
                var a = toWorld.Apply(mesh.Vertices[f[0]]);
                for (int i = 1; i + 1 < f.Length; i++)
                {
                    var t = new Tri(a, toWorld.Apply(mesh.Vertices[f[i]]), toWorld.Apply(mesh.Vertices[f[i + 1]]));
                    if (t.N.LengthSquared > 0.5) list.Add(t);
                }
            }

            return list;
        }

        /// <summary>Triangles bucketed on a coarse grid for "is this point on the surface" questions.</summary>
        private sealed class TriangleGrid
        {
            private readonly List<Tri> _tri;
            private readonly Vec3 _lo;
            private readonly double _cell;
            private readonly int _nx, _ny, _nz;
            private readonly Dictionary<int, List<int>> _cells = new Dictionary<int, List<int>>();

            public TriangleGrid(List<Tri> tri, Vec3 lo, Vec3 hi)
            {
                _tri = tri;
                var size = hi - lo;
                _cell = Math.Max(Math.Max(size.X, Math.Max(size.Y, size.Z)) / Math.Max(4, Math.Min(64, Math.Cbrt(tri.Count) * 2)), 1e-9);
                _lo = lo - new Vec3(_cell, _cell, _cell);
                _nx = (int)(size.X / _cell) + 3;
                _ny = (int)(size.Y / _cell) + 3;
                _nz = (int)(size.Z / _cell) + 3;
                for (int i = 0; i < tri.Count; i++)
                {
                    var t = tri[i];
                    var a = Cell(Vec3.Min(t.A, Vec3.Min(t.B, t.C)));
                    var b = Cell(Vec3.Max(t.A, Vec3.Max(t.B, t.C)));
                    for (int x = a.X; x <= b.X; x++)
                    for (int y = a.Y; y <= b.Y; y++)
                    for (int z = a.Z; z <= b.Z; z++)
                    {
                        int key = (z * _ny + y) * _nx + x;
                        if (!_cells.TryGetValue(key, out var l)) _cells[key] = l = new List<int>();
                        l.Add(i);
                    }
                }
            }

            private (int X, int Y, int Z) Cell(Vec3 p) =>
                (Clamp((int)((p.X - _lo.X) / _cell), _nx), Clamp((int)((p.Y - _lo.Y) / _cell), _ny), Clamp((int)((p.Z - _lo.Z) / _cell), _nz));

            private static int Clamp(int v, int n) => v < 0 ? 0 : v >= n ? n - 1 : v;

            /// <summary>Whether a point lies on a triangle facing the given way (within the tolerance).</summary>
            public bool OnSurface(Vec3 p, Vec3 facing, double tol)
            {
                var a = Cell(p - new Vec3(tol, tol, tol));
                var b = Cell(p + new Vec3(tol, tol, tol));
                for (int x = a.X; x <= b.X; x++)
                for (int y = a.Y; y <= b.Y; y++)
                for (int z = a.Z; z <= b.Z; z++)
                {
                    if (!_cells.TryGetValue((z * _ny + y) * _nx + x, out var list)) continue;
                    foreach (int i in list)
                    {
                        var t = _tri[i];
                        if (t.N.Dot(facing) < 0.98) continue;
                        if ((ClosestPoint(p, t) - p).Length <= tol) return true;
                    }
                }

                return false;
            }

            private static Vec3 ClosestPoint(Vec3 p, Tri t)
            {
                // Ericson, Real-Time Collision Detection 5.1.5
                var ab = t.B - t.A;
                var ac = t.C - t.A;
                var ap = p - t.A;
                double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
                if (d1 <= 0 && d2 <= 0) return t.A;
                var bp = p - t.B;
                double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
                if (d3 >= 0 && d4 <= d3) return t.B;
                double vc = d1 * d4 - d3 * d2;
                if (vc <= 0 && d1 >= 0 && d3 <= 0) return t.A + ab * (d1 / (d1 - d3));
                var cp = p - t.C;
                double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
                if (d6 >= 0 && d5 <= d6) return t.C;
                double vb = d5 * d2 - d1 * d6;
                if (vb <= 0 && d2 >= 0 && d6 <= 0) return t.A + ac * (d2 / (d2 - d6));
                double va = d3 * d6 - d5 * d4;
                if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return t.B + (t.C - t.B) * ((d4 - d3) / (d4 - d3 + d5 - d6));
                double denom = 1 / (va + vb + vc);
                return t.A + ab * (vb * denom) + ac * (vc * denom);
            }
        }
    }
}
