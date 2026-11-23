using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Cfd;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[analysis] Lattice-Boltzmann flow solver against analytic and benchmark flows.</summary>
    public class CfdValidationTests
    {
        private readonly ITestOutputHelper _out;

        public CfdValidationTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Fact]
        public void PoiseuilleChannelMatchesTheParabola()
        {
            int ny = 34;
            var lbm = new Lbm2D(4, ny, 0.9, 0)
            {
                Left = LbmBoundary.Periodic, Right = LbmBoundary.Periodic, Bottom = LbmBoundary.Wall, Top = LbmBoundary.Wall, ForceX = 1e-6
            };
            lbm.Step(30000);
            double nu = lbm.Viscosity, h = ny, maxErr = 0, umax = lbm.ForceX * h * h / (8 * nu);
            for (int y = 0; y < ny; y++)
            {
                double s = y + 0.5;   // half-way bounce-back: walls at y = −½ and y = ny − ½
                double exact = lbm.ForceX / (2 * nu) * s * (h - s);
                maxErr = Math.Max(maxErr, Math.Abs(lbm.Ux[lbm.Index(2, y)] - exact));
            }

            _out.WriteLine($"u_max analytic {umax:E4}, LBM {lbm.Ux[lbm.Index(2, ny / 2)]:E4}, max error {maxErr / umax * 100:F3} % of u_max");
            Assert.True(maxErr / umax < 0.01);
        }

        /// <summary>
        /// Schäfer–Turek benchmark (DFG 1996) "2D-1": cylinder D = 0.1 in a 0.41 m channel, centre (0.2, 0.2), parabolic
        /// inflow, Re = 20, steady. Reference Cd = 5.5795, Cl = 0.0106.
        /// </summary>
        [Fact]
        public void SchaeferTurekCylinderAtRe20MatchesTheBenchmarkDrag()
        {
            var watch = Stopwatch.StartNew();
            var lbm = Channel(20, 20, out double k);
            double prev = double.NaN, fx = 0, fy = 0;
            for (int it = 0; it < 100; it++)
            {
                fx = fy = 0;
                for (int i = 0; i < 1000; i++)
                {
                    lbm.Step();
                    fx += lbm.BodyFx;
                    fy += lbm.BodyFy;
                }

                fx /= 1000;
                fy /= 1000;
                if (!double.IsNaN(prev) && Math.Abs(fx - prev) < 1e-6 * Math.Abs(fx) && lbm.Steps > 30000) break;
                prev = fx;
            }

            double cd = fx * k, cl = fy * k;
            _out.WriteLine($"D = 20 cells, {lbm.CurvedLinks} curved links, {lbm.Steps} steps in {watch.Elapsed.TotalSeconds:F1} s: Cd {cd:F4} (ref 5.5795, {(cd / 5.5795 - 1) * 100:+0.00;-0.00} %), Cl {cl:F4} (ref 0.0106)");
            Assert.InRange(cd / 5.5795, 0.98, 1.02);
            Assert.InRange(cl, 0.005, 0.016);
        }

        /// <summary>
        /// Schäfer–Turek "2D-2": same channel at Re = 100, periodic vortex shedding. Reference Strouhal 0.300 (0.295–0.305),
        /// Cd max 3.23 (3.22–3.24), Cl max 1.00 (0.99–1.01).
        /// </summary>
        [Fact]
        public void SchaeferTurekCylinderAtRe100ShedsAtTheBenchmarkFrequency()
        {
            var watch = Stopwatch.StartNew();
            int d = 20;
            var lbm = Channel(d, 100, out double k);
            double ub = lbm.Ux0;
            int period = (int)(d / (0.3 * ub));
            lbm.Step(20 * 22 * d * 2);   // ≈ 2 flow-throughs of the 2.2 m channel: shedding settles
            var cl = new List<double>();
            var cd = new List<double>();
            for (int i = 0; i < 12 * period; i++)
            {
                lbm.Step();
                cl.Add(lbm.BodyFy * k);
                cd.Add(lbm.BodyFx * k);
            }

            double mean = cl.Average();
            var ups = new List<int>();
            for (int i = 1; i < cl.Count; i++)
                if (cl[i - 1] < mean && cl[i] >= mean)
                    ups.Add(i);
            double st = (ups.Count - 1) / (double)(ups[ups.Count - 1] - ups[0]) * d / ub;
            _out.WriteLine($"D = {d}, τ = {lbm.Tau:F3}, {lbm.Steps} steps in {watch.Elapsed.TotalSeconds:F1} s: St {st:F4} (ref 0.300), Cd max {cd.Max():F4} (ref 3.23), Cl max {cl.Max():F4} (ref 1.00)");
            Assert.InRange(st, 0.285, 0.315);
            Assert.InRange(cd.Max() / 3.23, 0.95, 1.05);
            Assert.InRange(cl.Max(), 0.8, 1.1);
        }

        [Fact]
        public void SphereDragAtRe100IsWithinTenPercentOfTheStandardCurve()
        {
            // Schiller–Naumann Cd = 24/Re·(1 + 0.15·Re^0.687) ≈ 1.09 at Re = 100 (standard drag curve, itself ±5 %).
            double re = 100, cd = Sphere(10, 6, 3, 7, re, out string info);
            double sn = 24 / re * (1 + 0.15 * Math.Pow(re, 0.687));
            _out.WriteLine($"{info}: Cd {cd:F3}, Schiller–Naumann {sn:F3} ({(cd / sn - 1) * 100:+0.0;-0.0} %)");
            Assert.InRange(cd / sn, 0.92, 1.10);
        }

        [Fact]
        public void SphereDragAtRe20IsWithinFifteenPercentOfTheStandardCurve()
        {
            // At Re 20 the disturbance decays slowly (∝ 1/r): a 4 D wide domain still adds ~10 % drag.
            double re = 20, cd = Sphere(10, 4, 2, 6, re, out string info);
            double sn = 24 / re * (1 + 0.15 * Math.Pow(re, 0.687));
            _out.WriteLine($"{info}: Cd {cd:F3}, Schiller–Naumann {sn:F3} ({(cd / sn - 1) * 100:+0.0;-0.0} %)");
            Assert.InRange(cd / sn, 0.92, 1.15);
        }

        [Fact]
        public void CubeAtRe100000RunsAtTheRealReynoldsNumberAndDragsLikeACube()
        {
            // A sharp-edged cube face-on: separation is fixed at the edges, so Cd ≈ 1.05 over Re 10⁴–10⁶ (Hoerner).
            // τ0 − ½ ≈ 2·10⁻⁵: stable only through the regularized collision, LES and the absorbing layer. On 12 cells per
            // side the drag is ≈ 16 % low; 20 cells give 1.01 (−4 %).
            int d = 12, nx = 8 * d, ny = 4 * d, nz = ny;
            double u = 0.06, re = 1e5;
            var lbm = new Lbm3D(nx, ny, nz, 3 * u * d / re + 0.5, u) { Smagorinsky = 0.17, FarField = LbmFarField.Open, Sponge = d / 2 };
            int x0 = 2 * d, y0 = (ny - d) / 2, z0 = (nz - d) / 2;
            for (int z = z0; z < z0 + d; z++)
            for (int y = y0; y < y0 + d; y++)
            for (int x = x0; x < x0 + d; x++)
                lbm.Solid[lbm.Index(x, y, z)] = true;   // faces fall half-way between nodes: the half-way wall is exact
            var watch = Stopwatch.StartNew();
            int flowThrough = (int)(nx / u);
            lbm.Step(flowThrough);
            double fx = 0;
            for (int i = 0; i < 2 * flowThrough; i++)
            {
                lbm.Step();
                fx += lbm.BodyFx;
            }

            double cd = fx / (2 * flowThrough) / (0.5 * u * u * d * d);
            _out.WriteLine($"{nx}×{ny}×{nz}, τ0 = {lbm.Tau:F6}, {lbm.Steps} steps in {watch.Elapsed.TotalSeconds:F1} s: Cd {cd:F3} (Hoerner ≈ 1.05, {(cd / 1.05 - 1) * 100:+0.0;-0.0} %)");
            Assert.False(double.IsNaN(cd));
            Assert.InRange(cd / 1.05, 0.78, 1.15);
        }

        /// <summary>DFG channel: H = 4.1 D, length 22 D, cylinder centre 2 D from the inlet and the bottom wall.</summary>
        private static Lbm2D Channel(int d, double re, out double cdFactor)
        {
            double ub = 0.05, nu = ub * d / re;
            int ny = (int)Math.Round(4.1 * d), nx = 22 * d;
            var lbm = new Lbm2D(nx, ny, 3 * nu + 0.5, ub) { Top = LbmBoundary.Wall, Bottom = LbmBoundary.Wall, Right = LbmBoundary.Open };
            lbm.InletProfile = j =>
            {
                double s = (j + 0.5) / ny;
                return 6 * ub * s * (1 - s);   // parabola with mean ub
            };
            double xc = 2 * d - 0.5, yc = 2 * d - 0.5, r = d / 2.0;
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                lbm.Solid[lbm.Index(x, y)] = (x - xc) * (x - xc) + (y - yc) * (y - yc) < r * r;
                lbm.SetCell(x, y, 1, lbm.InletProfile(y), 0);
            }

            lbm.SetWallFractions((x, y, q) => Circle(x - xc, y - yc, Lbm2D.Cx[q], Lbm2D.Cy[q], 0, 0, r));
            cdFactor = 2 / (ub * ub * d);
            return lbm;
        }

        private static double Sphere(int d, double lateral, double up, double down, double re, out string info)
        {
            int nx = (int)((up + down) * d), ny = (int)(lateral * d), nz = ny;
            double u = 0.05, nu = u * d / re;
            var lbm = new Lbm3D(nx, ny, nz, 3 * nu + 0.5, u) { Smagorinsky = 0, FarField = LbmFarField.Open };
            double cx = up * d, cy = ny / 2.0, cz = nz / 2.0, r = d / 2.0;
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
                lbm.Solid[lbm.Index(x, y, z)] = (x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz) < r * r;
            lbm.SetWallFractions((x, y, z, q) => Circle(x - cx, y - cy, Lbm3D.Cx[q], Lbm3D.Cy[q], z - cz, Lbm3D.Cz[q], r));
            var watch = Stopwatch.StartNew();
            double prev = double.NaN, fx = 0;
            int block = (int)(nx / u / 8);
            for (int k = 0; k < 64; k++)
            {
                fx = 0;
                for (int i = 0; i < block; i++)
                {
                    lbm.Step();
                    fx += lbm.BodyFx;
                }

                fx /= block;
                if (!double.IsNaN(prev) && Math.Abs(fx - prev) < 2e-4 * Math.Abs(fx) && lbm.Steps > 2 * nx / u) break;
                prev = fx;
            }

            info = $"{nx}×{ny}×{nz} (D = {d}, {lateral} D wide, open far field), {lbm.CurvedLinks} curved links, {lbm.Steps} steps in {watch.Elapsed.TotalSeconds:F1} s";
            return fx / (0.5 * u * u * Math.PI * d * d / 4);
        }

        /// <summary>Fraction of the link (dx, dy, dz) from the node at offset (ox, oy, oz) from the centre to a sphere/circle of radius r.</summary>
        private static double Circle(double ox, double oy, double dx, double dy, double oz, double dz, double r)
        {
            double a = dx * dx + dy * dy + dz * dz, b = 2 * (ox * dx + oy * dy + oz * dz), c = ox * ox + oy * oy + oz * oz - r * r;
            double disc = b * b - 4 * a * c;
            if (disc < 0) return double.NaN;
            double t = (-b - Math.Sqrt(disc)) / (2 * a);
            return t > 0 && t <= 1.0001 ? t : double.NaN;
        }
    }
}
