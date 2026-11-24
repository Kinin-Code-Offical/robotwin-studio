using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Mechanics
{
    public sealed partial class LiquidBody
    {
        /// <summary>
        /// [liquid] A rigid body's signed distance sampled once on a grid in its own frame (bodies do not deform), for the
        /// bodies whose exact distance is expensive: concave ones made of pieces (a glass is 25 convex pieces), hulls and
        /// meshes. A query is then a trilinear interpolation, and the normal is the field's gradient.
        /// </summary>
        private sealed class Field
        {
            public Vec3 Lo;
            public double Cell;
            public int Nx, Ny, Nz;
            public float[] D = Array.Empty<float>();

            /// <summary>Φ: the share of the kernel the body's solid covers around each node (½ at a flat face).</summary>
            public float[] Phi = Array.Empty<float>();
            public CollisionShape? Shape;
            public CollisionShape[]? Pieces;
            public double Far;
        }

        private readonly Dictionary<SceneBody, Field?> _fields = new Dictionary<SceneBody, Field?>();

        private Field? FieldOf(SceneBody b)
        {
            var shape = b.EffectiveShape;
            if (_fields.TryGetValue(b, out var f) && (f == null || (f.Shape == shape && f.Pieces == b.Pieces))) return f;
            f = null;
            if (shape != null && (b.Pieces != null || shape.Kind == ShapeKind.ConvexHull || shape.Kind == ShapeKind.Mesh)) f = BuildField(b, shape);
            _fields[b] = f;
            return f;
        }

        private Field? BuildField(SceneBody b, CollisionShape shape)
        {
            // Bounds of the solid in the body frame, plus the reach of the pressure kernel.
            var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            void Include(CollisionShape s)
            {
                switch (s.Kind)
                {
                    case ShapeKind.Box:
                        lo = Min(lo, -s.HalfExtents);
                        hi = Max(hi, s.HalfExtents);
                        break;
                    case ShapeKind.Sphere:
                        lo = Min(lo, new Vec3(-s.Radius, -s.Radius, -s.Radius));
                        hi = Max(hi, new Vec3(s.Radius, s.Radius, s.Radius));
                        break;
                    case ShapeKind.Cylinder:
                        lo = Min(lo, new Vec3(-s.Radius, -s.Radius, -s.HalfHeight));
                        hi = Max(hi, new Vec3(s.Radius, s.Radius, s.HalfHeight));
                        break;
                    case ShapeKind.Mesh when s.Mesh != null:
                        foreach (var v in s.Mesh.Vertices)
                        {
                            lo = Min(lo, v);
                            hi = Max(hi, v);
                        }

                        break;
                    default:
                        if (s.Vertices.Count == 0)
                        {
                            double r = s.BoundingRadius;
                            lo = Min(lo, new Vec3(-r, -r, -r));
                            hi = Max(hi, new Vec3(r, r, r));
                        }

                        foreach (var v in s.Vertices)
                        {
                            lo = Min(lo, v);
                            hi = Max(hi, v);
                        }

                        break;
                }
            }

            if (b.Pieces != null)
                foreach (var p in b.Pieces)
                    Include(p);
            else Include(shape);
            double margin = _h + Spacing;
            lo -= new Vec3(margin, margin, margin);
            hi += new Vec3(margin, margin, margin);
            // A third of the spacing (thin walls keep a sample inside them), coarser only for very large bodies.
            double cell = Math.Max(0.0006, Spacing / 3);
            var size = hi - lo;
            while (size.X / cell * size.Y / cell * size.Z / cell > 3e6) cell *= 1.2;
            var f = new Field
            {
                Lo = lo,
                Cell = cell,
                Nx = (int)Math.Ceiling(size.X / cell) + 1,
                Ny = (int)Math.Ceiling(size.Y / cell) + 1,
                Nz = (int)Math.Ceiling(size.Z / cell) + 1,
                Shape = shape,
                Pieces = b.Pieces,
                Far = 2 * margin
            };
            f.D = new float[f.Nx * f.Ny * f.Nz];
            Parallel.For(0, f.Nz, z =>
            {
                for (int y = 0; y < f.Ny; y++)
                for (int x = 0; x < f.Nx; x++)
                {
                    var p = lo + new Vec3(x * cell, y * cell, z * cell);
                    f.D[(z * f.Ny + y) * f.Nx + x] = b.SignedDistanceSolid(shape, p, f.Far, out double d, out _) ? (float)Math.Min(d, f.Far) : (float)f.Far;
                }
            });

            // Φ(x) = ∫_solid W(|x − y|) dy (Koschier & Bender's density map): each cell's solid share from its signed
            // distance, convolved with the kernel. Corners, thin walls and whatever lies behind them come out right.
            double cell3 = cell * cell * cell;
            var occ = new float[f.D.Length];
            for (int i = 0; i < occ.Length; i++) occ[i] = (float)Math.Max(0, Math.Min(1, 0.5 - f.D[i] / cell));
            int r = (int)Math.Ceiling(_h / cell);
            var taps = new List<(int Dx, int Dy, int Dz, float W)>();
            for (int dz = -r; dz <= r; dz++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                double w = W(Math.Sqrt(dx * dx + dy * dy + dz * dz) * cell) * cell3;
                if (w > 0) taps.Add((dx, dy, dz, (float)w));
            }

            f.Phi = new float[f.D.Length];
            int nx = f.Nx, ny = f.Ny, nz = f.Nz;
            // [perf] The distance field changes by at most the distance moved, so a node farther than the kernel's reach
            // plus a cell from the solid has no solid under its kernel (Φ = 0), and one that deep inside has it all (Φ = the
            // kernel's integral): only the band around the surface is summed. Exact; the glass and bowl took 2.9 s to build.
            double band = _h + cell;
            double full = 0;
            foreach (var t in taps) full += t.W;
            Parallel.For(0, nz, z =>
            {
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    float dn = f.D[(z * ny + y) * nx + x];
                    if (dn > band) continue;   // Φ stays 0
                    if (dn < -band && x >= r && y >= r && z >= r && x < nx - r && y < ny - r && z < nz - r)
                    {
                        f.Phi[(z * ny + y) * nx + x] = (float)full;
                        continue;
                    }

                    double sum = 0;
                    foreach (var (dx, dy, dz, w) in taps)
                    {
                        int xx = x + dx, yy = y + dy, zz = z + dz;
                        if (xx < 0 || yy < 0 || zz < 0 || xx >= nx || yy >= ny || zz >= nz) continue;
                        float o = occ[(zz * ny + yy) * nx + xx];
                        if (o > 0) sum += o * w;
                    }

                    f.Phi[(z * ny + y) * nx + x] = (float)sum;
                }
            });
            return f;
        }

        private static Vec3 Min(Vec3 a, Vec3 b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        private static Vec3 Max(Vec3 a, Vec3 b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        /// <summary>Signed distance to body b's solid at a body-frame point, and the outward normal (body frame); f is its field, if any.</summary>
        private static bool Distance(Field? f, SceneBody b, CollisionShape shape, Vec3 local, double maxDistance, out double distance, out Vec3 normal)
        {
            if (f == null) return b.SignedDistanceSolid(shape, local, maxDistance, out distance, out normal);
            double u = (local.X - f.Lo.X) / f.Cell, v = (local.Y - f.Lo.Y) / f.Cell, w = (local.Z - f.Lo.Z) / f.Cell;
            normal = Vec3.UnitZ;
            distance = f.Far;
            if (u < 0 || v < 0 || w < 0 || u >= f.Nx - 1 || v >= f.Ny - 1 || w >= f.Nz - 1) return false;
            int i = (int)u, j = (int)v, k = (int)w;
            double a = u - i, c = v - j, e = w - k;
            int nx = f.Nx, nxy = f.Nx * f.Ny, o = k * nxy + j * nx + i;
            var D = f.D;
            double d000 = D[o], d100 = D[o + 1], d010 = D[o + nx], d110 = D[o + nx + 1];
            double d001 = D[o + nxy], d101 = D[o + nxy + 1], d011 = D[o + nxy + nx], d111 = D[o + nxy + nx + 1];
            double x00 = d000 + (d100 - d000) * a, x10 = d010 + (d110 - d010) * a, x01 = d001 + (d101 - d001) * a, x11 = d011 + (d111 - d011) * a;
            double y0 = x00 + (x10 - x00) * c, y1 = x01 + (x11 - x01) * c;
            distance = y0 + (y1 - y0) * e;
            if (distance >= maxDistance) return false;
            // Gradient of the trilinear interpolant.
            double gx = ((d100 - d000) * (1 - c) + (d110 - d010) * c) * (1 - e) + ((d101 - d001) * (1 - c) + (d111 - d011) * c) * e;
            double gy = (x10 - x00) * (1 - e) + (x11 - x01) * e;
            double gz = y1 - y0;
            double gl = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            normal = gl > 1e-12 ? new Vec3(gx / gl, gy / gl, gz / gl) : Vec3.UnitZ;
            return true;
        }

        /// <summary>Φ and its gradient (body frame) at a body-frame point of a field; false outside the field.</summary>
        private static bool SamplePhi(Field f, Vec3 local, out double phi, out Vec3 grad)
        {
            phi = 0;
            grad = Vec3.Zero;
            double u = (local.X - f.Lo.X) / f.Cell, v = (local.Y - f.Lo.Y) / f.Cell, w = (local.Z - f.Lo.Z) / f.Cell;
            if (u < 0 || v < 0 || w < 0 || u >= f.Nx - 1 || v >= f.Ny - 1 || w >= f.Nz - 1) return false;
            int i = (int)u, j = (int)v, k = (int)w;
            double a = u - i, c = v - j, e = w - k;
            int nx = f.Nx, nxy = f.Nx * f.Ny, o = k * nxy + j * nx + i;
            var P = f.Phi;
            double p000 = P[o], p100 = P[o + 1], p010 = P[o + nx], p110 = P[o + nx + 1];
            double p001 = P[o + nxy], p101 = P[o + nxy + 1], p011 = P[o + nxy + nx], p111 = P[o + nxy + nx + 1];
            double x00 = p000 + (p100 - p000) * a, x10 = p010 + (p110 - p010) * a, x01 = p001 + (p101 - p001) * a, x11 = p011 + (p111 - p011) * a;
            double y0 = x00 + (x10 - x00) * c, y1 = x01 + (x11 - x01) * c;
            phi = y0 + (y1 - y0) * e;
            double gx = ((p100 - p000) * (1 - c) + (p110 - p010) * c) * (1 - e) + ((p101 - p001) * (1 - c) + (p111 - p011) * c) * e;
            double gy = (x10 - x00) * (1 - e) + (x11 - x01) * e;
            double gz = y1 - y0;
            grad = new Vec3(gx, gy, gz) / f.Cell;
            return true;
        }
    }
}
