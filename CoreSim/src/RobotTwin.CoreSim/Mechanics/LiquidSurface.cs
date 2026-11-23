using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>[liquid] A liquid's surface as a triangle mesh with smooth normals, plus the loose drops.</summary>
    public sealed class LiquidMesh
    {
        public float[] Positions { get; set; } = Array.Empty<float>();
        public float[] Normals { get; set; } = Array.Empty<float>();
        public int[] Triangles { get; set; } = Array.Empty<int>();

        /// <summary>Particles flying alone (spray, drops): x, y, z; drawn as small drops, too thin for the surface field.</summary>
        public float[] Drops { get; set; } = Array.Empty<float>();

        /// <summary>Velocity of each drop (vx, vy, vz; empty when not known), to draw it as a camera would see it.</summary>
        public float[] DropVelocities { get; set; } = Array.Empty<float>();

        public int VertexCount => Positions.Length / 3;
        public int TriangleCount => Triangles.Length / 3;
    }

    /// <summary>
    /// [liquid] Reconstructs the free surface from particles:
    /// <list type="number">
    /// <item>particle centres are smoothed towards their neighbours' mean (Yu &amp; Turk 2013), so a resting surface is
    /// flat instead of showing the lattice;</item>
    /// <item>each particle's kernel is stretched along the shape of its neighbourhood (Yu &amp; Turk's anisotropic kernels,
    /// from the covariance of the neighbours): flat in a sheet one particle thick, long along a thin stream, round inside
    /// the liquid, so a film over a dry floor and a falling stream are continuous;</item>
    /// <item>a density field φ = Σ W(|G_j(x − x̄_j)|) det G_j / S0 is sampled on a grid (1 inside the liquid, 0 in air);</item>
    /// <item>the surface φ = ½ is extracted with surface nets (one vertex per crossed cell, at the mean of its edge
    /// crossings; a quad across every crossed grid edge), which gives a closed, evenly spread mesh;</item>
    /// <item>normals are the field's gradient at each vertex.</item>
    /// </list>
    /// </summary>
    public static class LiquidSurface
    {
        /// <param name="xyz">Particle positions.</param>
        /// <param name="spacing">Particle spacing at rest (m).</param>
        /// <param name="cell">Grid cell as a fraction of the spacing (0.5 live, 0.33 for a render).</param>
        /// <param name="maxCells">Grid size limit; the cell grows when the liquid has spread wide.</param>
        public static LiquidMesh Build(float[] xyz, double spacing, double cell = 0.5, int maxCells = 6_000_000, float[]? velocities = null)
        {
            int n = xyz.Length / 3;
            var mesh = new LiquidMesh();
            if (n == 0) return mesh;
            double l = spacing, h = 2 * l, sigma = 1 / (Math.PI * l * l * l);
            double S0 = 0;
            for (int i = -3; i <= 3; i++)
            for (int j = -3; j <= 3; j++)
            for (int k = -3; k <= 3; k++)
                S0 += Kernel(Math.Sqrt(i * i + j * j + k * k) * spacing, l, sigma);

            // 1. Neighbours (hash grid of cell h) and smoothed centres.
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < n; i++)
            {
                long key = Key((int)Math.Floor(xyz[3 * i] / h), (int)Math.Floor(xyz[3 * i + 1] / h), (int)Math.Floor(xyz[3 * i + 2] / h));
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                list.Add(i);
            }

            // Per particle: its smoothed centre (Yu & Turk), how many neighbours it has, and the shape of its neighbourhood:
            // the weighted covariance of the neighbours' positions, whose principal axes say whether the liquid around it is a
            // volume (isotropic), a sheet (one small axis) or a stream (two small axes).
            var smooth = new float[3 * n];
            var neighbours = new int[n];
            var axes = new float[9 * n];      // e1, e2, e3 (unit, rows)
            var stretch = new float[3 * n];   // the kernel's reach along them, as a multiple of its radius
            double iso3 = IsotropicVariance(spacing, h);
            Parallel.For(0, n, i =>
            {
                double px = xyz[3 * i], py = xyz[3 * i + 1], pz = xyz[3 * i + 2];
                int cx = (int)Math.Floor(px / h), cy = (int)Math.Floor(py / h), cz = (int)Math.Floor(pz / h);
                double sw = 0, mx = 0, my = 0, mz = 0, cxx = 0, cyy = 0, czz = 0, cxy = 0, cyz = 0, czx = 0;
                int nb = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!grid.TryGetValue(Key(cx + dx, cy + dy, cz + dz), out var list)) continue;
                    foreach (int j in list)
                    {
                        double ex = xyz[3 * j] - px, ey = xyz[3 * j + 1] - py, ez = xyz[3 * j + 2] - pz;
                        double r = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                        if (r >= h) continue;
                        double q = r / h, w = 1 - q * q * q;
                        sw += w;
                        mx += w * ex;
                        my += w * ey;
                        mz += w * ez;
                        cxx += w * ex * ex;
                        cyy += w * ey * ey;
                        czz += w * ez * ez;
                        cxy += w * ex * ey;
                        cyz += w * ey * ez;
                        czx += w * ez * ex;
                        nb++;
                    }
                }

                mx /= sw;
                my /= sw;
                mz /= sw;
                const double lambda = 0.9;
                smooth[3 * i] = (float)(px + lambda * mx);
                smooth[3 * i + 1] = (float)(py + lambda * my);
                smooth[3 * i + 2] = (float)(pz + lambda * mz);
                neighbours[i] = nb;
                // Covariance about the weighted mean.
                var C = new double[3, 3];
                C[0, 0] = cxx / sw - mx * mx;
                C[1, 1] = cyy / sw - my * my;
                C[2, 2] = czz / sw - mz * mz;
                C[0, 1] = C[1, 0] = cxy / sw - mx * my;
                C[1, 2] = C[2, 1] = cyz / sw - my * mz;
                C[2, 0] = C[0, 2] = czx / sw - mz * mx;
                Eigen(C, out var values, out var vectors);
                // Reach along each axis ∝ the neighbours' spread along it, relative to a full neighbourhood; no axis below a
                // sixth of the longest (a film thinner than a particle keeps a thickness), none beyond 1.5 kernel radii.
                double l1 = Math.Max(values[0], 1e-30);
                for (int k = 0; k < 3; k++)
                {
                    double v = Math.Max(values[k], l1 / 36);
                    stretch[3 * i + k] = (float)Math.Max(0.12, Math.Min(1.5, Math.Sqrt(v / iso3)));
                    axes[9 * i + 3 * k] = (float)vectors[0, k];
                    axes[9 * i + 3 * k + 1] = (float)vectors[1, k];
                    axes[9 * i + 3 * k + 2] = (float)vectors[2, k];
                }
            });

            // A particle with fewer than two neighbours is spray: drawn as a drop (it has no sheet or stream to belong to).
            var drops = new List<float>();
            var dropVel = new List<float>();
            var body = new List<int>(n);
            bool withVel = velocities != null && velocities.Length >= 3 * n;
            for (int i = 0; i < n; i++)
            {
                if (neighbours[i] < 3)
                {
                    for (int a = 0; a < 3; a++)
                    {
                        drops.Add(xyz[3 * i + a]);
                        if (withVel) dropVel.Add(velocities![3 * i + a]);
                    }
                }
                else body.Add(i);
            }

            mesh.Drops = drops.ToArray();
            mesh.DropVelocities = dropVel.ToArray();
            if (body.Count == 0) return mesh;

            // 2. Field on a grid around the liquid: φ(x) = Σ W(|G_j (x − x̄_j)|) det G_j / S0, each kernel stretched along its
            // particle's neighbourhood (G = R diag(1/a) Rᵀ; det G keeps every particle's share of volume).
            double lx = double.MaxValue, ly = double.MaxValue, lz = double.MaxValue, hx = double.MinValue, hy = double.MinValue, hz = double.MinValue;
            foreach (int i in body)
            {
                lx = Math.Min(lx, smooth[3 * i]);
                hx = Math.Max(hx, smooth[3 * i]);
                ly = Math.Min(ly, smooth[3 * i + 1]);
                hy = Math.Max(hy, smooth[3 * i + 1]);
                lz = Math.Min(lz, smooth[3 * i + 2]);
                hz = Math.Max(hz, smooth[3 * i + 2]);
            }

            double c = cell * spacing;
            double pad = 1.5 * h + 2 * c;
            lx -= pad;
            ly -= pad;
            lz -= pad;
            hx += pad;
            hy += pad;
            hz += pad;
            int gx = (int)Math.Ceiling((hx - lx) / c) + 1, gy = (int)Math.Ceiling((hy - ly) / c) + 1, gz = (int)Math.Ceiling((hz - lz) / c) + 1;
            while ((long)gx * gy * gz > maxCells)
            {
                c *= 1.25;
                gx = (int)Math.Ceiling((hx - lx) / c) + 1;
                gy = (int)Math.Ceiling((hy - ly) / c) + 1;
                gz = (int)Math.Ceiling((hz - lz) / c) + 1;
            }

            var phi = new float[gx * gy * gz];
            // Splatted by z slabs in parallel (each slab owns its nodes, so no two threads write the same value).
            var byLayer = new List<int>[gz];
            for (int z = 0; z < gz; z++) byLayer[z] = new List<int>();
            foreach (int i in body)
            {
                double reachI = h * Math.Max(stretch[3 * i], Math.Max(stretch[3 * i + 1], stretch[3 * i + 2]));
                int kz = (int)Math.Floor((smooth[3 * i + 2] - lz) / c), r = (int)Math.Ceiling(reachI / c);
                for (int z = Math.Max(0, kz - r); z <= Math.Min(gz - 1, kz + r + 1); z++) byLayer[z].Add(i);
            }

            double inv = 1 / S0;
            Parallel.For(0, gz, z =>
            {
                double wz = lz + z * c;
                foreach (int i in byLayer[z])
                {
                    double px = smooth[3 * i], py = smooth[3 * i + 1], pz = smooth[3 * i + 2];
                    double a1 = stretch[3 * i], a2 = stretch[3 * i + 1], a3 = stretch[3 * i + 2];
                    double reachI = h * Math.Max(a1, Math.Max(a2, a3));
                    double ez = wz - pz;
                    if (Math.Abs(ez) >= reachI) continue;
                    double e1x = axes[9 * i] / a1, e1y = axes[9 * i + 1] / a1, e1z = axes[9 * i + 2] / a1;
                    double e2x = axes[9 * i + 3] / a2, e2y = axes[9 * i + 4] / a2, e2z = axes[9 * i + 5] / a2;
                    double e3x = axes[9 * i + 6] / a3, e3y = axes[9 * i + 7] / a3, e3z = axes[9 * i + 8] / a3;
                    double scale = inv / (a1 * a2 * a3);
                    int x0 = Math.Max(0, (int)Math.Floor((px - reachI - lx) / c)), x1 = Math.Min(gx - 1, (int)Math.Ceiling((px + reachI - lx) / c));
                    int y0 = Math.Max(0, (int)Math.Floor((py - reachI - ly) / c)), y1 = Math.Min(gy - 1, (int)Math.Ceiling((py + reachI - ly) / c));
                    for (int y = y0; y <= y1; y++)
                    {
                        double ey = ly + y * c - py;
                        int row = (z * gy + y) * gx;
                        for (int x = x0; x <= x1; x++)
                        {
                            double ex = lx + x * c - px;
                            double u1 = e1x * ex + e1y * ey + e1z * ez, u2 = e2x * ex + e2y * ey + e2z * ez, u3 = e3x * ex + e3y * ey + e3z * ez;
                            double r2 = u1 * u1 + u2 * u2 + u3 * u3;
                            if (r2 >= h * h) continue;
                            phi[row + x] += (float)(Kernel(Math.Sqrt(r2), l, sigma) * scale);
                        }
                    }
                }
            });

            // 3. Surface nets on φ = ½ (half a spacing beyond the outer particles, like the solver's walls). Thin sheets and
            // streams close by the stretched kernels, not by a lower level.
            const float iso = 0.5f;
            int Node(int x, int y, int z) => (z * gy + y) * gx + x;
            var vertexOf = new int[(gx - 1) * (gy - 1) * (gz - 1)];
            var pos = new List<float>();
            var nrm = new List<float>();
            for (int z = 0; z < gz - 1; z++)
            for (int y = 0; y < gy - 1; y++)
            for (int x = 0; x < gx - 1; x++)
            {
                int ci = (z * (gy - 1) + y) * (gx - 1) + x;
                vertexOf[ci] = -1;
                int mask = 0;
                for (int k = 0; k < 8; k++)
                    if (phi[Node(x + (k & 1), y + ((k >> 1) & 1), z + ((k >> 2) & 1))] > iso)
                        mask |= 1 << k;
                if (mask == 0 || mask == 255) continue;
                // The mean of the crossings on the cube's 12 edges.
                double sx = 0, sy = 0, sz = 0;
                int count = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = Edges[2 * e], b = Edges[2 * e + 1];
                    bool ia = (mask & (1 << a)) != 0, ib = (mask & (1 << b)) != 0;
                    if (ia == ib) continue;
                    float fa = phi[Node(x + (a & 1), y + ((a >> 1) & 1), z + ((a >> 2) & 1))];
                    float fb = phi[Node(x + (b & 1), y + ((b >> 1) & 1), z + ((b >> 2) & 1))];
                    double t = (iso - fa) / (fb - fa);
                    sx += (a & 1) + t * ((b & 1) - (a & 1));
                    sy += ((a >> 1) & 1) + t * (((b >> 1) & 1) - ((a >> 1) & 1));
                    sz += ((a >> 2) & 1) + t * (((b >> 2) & 1) - ((a >> 2) & 1));
                    count++;
                }

                double fx = x + sx / count, fy = y + sy / count, fz = z + sz / count;
                vertexOf[ci] = pos.Count / 3;
                pos.Add((float)(lx + fx * c));
                pos.Add((float)(ly + fy * c));
                pos.Add((float)(lz + fz * c));
                // Outward normal: −∇φ (trilinear gradient at the vertex).
                var gr = Gradient(phi, gx, gy, gz, fx, fy, fz);
                double gl = Math.Sqrt(gr.X * gr.X + gr.Y * gr.Y + gr.Z * gr.Z);
                if (gl < 1e-12) gl = 1;
                nrm.Add((float)(-gr.X / gl));
                nrm.Add((float)(-gr.Y / gl));
                nrm.Add((float)(-gr.Z / gl));
            }

            int Cell(int x, int y, int z) => x < 0 || y < 0 || z < 0 || x >= gx - 1 || y >= gy - 1 || z >= gz - 1 ? -1 : vertexOf[(z * (gy - 1) + y) * (gx - 1) + x];
            var tris = new List<int>();
            void Quad(int a, int b, int cc, int d, bool flip)
            {
                if (a < 0 || b < 0 || cc < 0 || d < 0) return;
                if (flip)
                {
                    tris.Add(a);
                    tris.Add(cc);
                    tris.Add(b);
                    tris.Add(a);
                    tris.Add(d);
                    tris.Add(cc);
                }
                else
                {
                    tris.Add(a);
                    tris.Add(b);
                    tris.Add(cc);
                    tris.Add(a);
                    tris.Add(cc);
                    tris.Add(d);
                }
            }

            for (int z = 1; z < gz - 1; z++)
            for (int y = 1; y < gy - 1; y++)
            for (int x = 1; x < gx - 1; x++)
            {
                bool inside = phi[Node(x, y, z)] > iso;
                // Edge along +x from this node: the four cells around it, counter-clockwise seen from +x.
                if (x < gx - 1 && inside != phi[Node(x + 1, y, z)] > iso)
                    Quad(Cell(x, y - 1, z - 1), Cell(x, y, z - 1), Cell(x, y, z), Cell(x, y - 1, z), !inside);
                if (y < gy - 1 && inside != phi[Node(x, y + 1, z)] > iso)
                    Quad(Cell(x - 1, y, z - 1), Cell(x - 1, y, z), Cell(x, y, z), Cell(x, y, z - 1), !inside);
                if (z < gz - 1 && inside != phi[Node(x, y, z + 1)] > iso)
                    Quad(Cell(x - 1, y - 1, z), Cell(x, y - 1, z), Cell(x, y, z), Cell(x - 1, y, z), !inside);
            }

            mesh.Positions = pos.ToArray();
            mesh.Normals = nrm.ToArray();
            mesh.Triangles = tris.ToArray();
            return mesh;
        }

        /// <summary>Per-axis variance of a full neighbourhood (the rest lattice) with the same weights: the isotropic reference.</summary>
        private static double IsotropicVariance(double spacing, double h)
        {
            double sw = 0, sxx = 0;
            for (int i = -3; i <= 3; i++)
            for (int j = -3; j <= 3; j++)
            for (int k = -3; k <= 3; k++)
            {
                double r = Math.Sqrt(i * i + j * j + k * k) * spacing;
                if (r >= h) continue;
                double q = r / h, w = 1 - q * q * q;
                sw += w;
                sxx += w * (i * spacing) * (i * spacing);
            }

            return sxx / sw;
        }

        /// <summary>Eigen-decomposition of a symmetric 3×3 matrix (cyclic Jacobi): values descending, vectors as columns.</summary>
        private static void Eigen(double[,] m, out double[] values, out double[,] vectors)
        {
            var a = (double[,])m.Clone();
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 12; sweep++)
            {
                double off = a[0, 1] * a[0, 1] + a[0, 2] * a[0, 2] + a[1, 2] * a[1, 2];
                if (off < 1e-30) break;
                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double cs = 1 / Math.Sqrt(t * t + 1), sn = t * cs;
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = cs * akp - sn * akq;
                        a[k, q] = sn * akp + cs * akq;
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = cs * apk - sn * aqk;
                        a[q, k] = sn * apk + cs * aqk;
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = cs * vkp - sn * vkq;
                        v[k, q] = sn * vkp + cs * vkq;
                    }
                }
            }

            var order = new[] { 0, 1, 2 };
            System.Array.Sort(order, (x, y) => a[y, y].CompareTo(a[x, x]));
            values = new double[3];
            vectors = new double[3, 3];
            for (int c = 0; c < 3; c++)
            {
                values[c] = a[order[c], order[c]];
                for (int r = 0; r < 3; r++) vectors[r, c] = v[r, order[c]];
            }
        }

        // Cube corners k = x + 2y + 4z; the 12 edges as corner pairs.
        private static readonly int[] Edges = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };

        private static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

        private static double Kernel(double r, double l, double sigma)
        {
            double q = r / l;
            if (q >= 2) return 0;
            if (q < 1) return sigma * (1 - 1.5 * q * q + 0.75 * q * q * q);
            double t = 2 - q;
            return sigma * 0.25 * t * t * t;
        }

        private static Vec3 Gradient(float[] f, int gx, int gy, int gz, double x, double y, double z)
        {
            double Sample(double u, double v, double w)
            {
                u = Math.Max(0, Math.Min(gx - 1.001, u));
                v = Math.Max(0, Math.Min(gy - 1.001, v));
                w = Math.Max(0, Math.Min(gz - 1.001, w));
                int i = (int)u, j = (int)v, k = (int)w;
                double a = u - i, b = v - j, cc = w - k, s = 0;
                for (int q = 0; q < 8; q++)
                {
                    int di = q & 1, dj = (q >> 1) & 1, dk = (q >> 2) & 1;
                    double wt = (di == 1 ? a : 1 - a) * (dj == 1 ? b : 1 - b) * (dk == 1 ? cc : 1 - cc);
                    s += wt * f[((k + dk) * gy + j + dj) * gx + i + di];
                }

                return s;
            }

            const double e = 0.5;
            return new Vec3(Sample(x + e, y, z) - Sample(x - e, y, z), Sample(x, y + e, z) - Sample(x, y - e, z), Sample(x, y, z + e) - Sample(x, y, z - e));
        }
    }
}
