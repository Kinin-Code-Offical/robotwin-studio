using System;
using System.Collections.Generic;
using System.Diagnostics;
using RobotTwin.CoreSim.Analysis.Cfd;
using RobotTwin.CoreSim.Analysis.Numerics;
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

        [Fact]
        public void CylinderWakeSheddingAtRe100HasStrouhalNearTwoTenths()
        {
            int d = 16, nx = 20 * d, ny = 5 * d;
            double u = 0.06, re = 100;
            double nu = u * d / re;
            var lbm = new Lbm2D(nx, ny, 3 * nu + 0.5, u);
            double cx = 4.5 * d, cy = ny / 2.0;
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
                lbm.Solid[lbm.Index(x, y)] = (x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy) <= d * d / 4.0;
            // break the symmetry so shedding starts quickly
            for (int y = 0; y < ny; y++)
            for (int x = (int)cx + d; x < (int)cx + 3 * d; x++)
                lbm.SetCell(x, y, 1, u, 0.1 * u * Math.Sin(2 * Math.PI * y / ny));
            var watch = Stopwatch.StartNew();
            var cl = new List<double>();
            var cd = new List<double>();
            int steps = 24000, record = 12000;
            for (int s = 0; s < steps; s++)
            {
                lbm.Step();
                if (s >= record)
                {
                    cl.Add(lbm.BodyFy / (0.5 * u * u * d));
                    cd.Add(lbm.BodyFx / (0.5 * u * u * d));
                }
            }

            var sp = Spectral.AmplitudeSpectrum(cl, 1.0);
            double f = sp.PeakFrequency(1e-5);
            double st = f * d / u;
            var cdStats = Spectral.Stats(cd);
            _out.WriteLine($"{nx}×{ny}, D = {d}, τ = {lbm.Tau:F4}, {steps} steps in {watch.Elapsed.TotalSeconds:F1} s ({(double)steps * nx * ny / watch.Elapsed.TotalSeconds / 1e6:F0} MLUPS)");
            _out.WriteLine($"Strouhal {st:F3} (unconfined ≈ 0.165, blockage D/H = 0.2 raises it); mean Cd {cdStats.Mean:F3}, Cl amplitude {Spectral.Stats(cl).Max:F3}");
            Assert.InRange(st, 0.15, 0.23);
            Assert.InRange(cdStats.Mean, 1.1, 2.2);
        }

        [Fact]
        public void SphereDragAtLowReynoldsIsRoughlyRight()
        {
            // Re = 20: Schiller–Naumann Cd = 24/Re·(1 + 0.15·Re^0.687) ≈ 2.61. Coarse staircase sphere, so only roughly.
            int d = 10, nx = 8 * d, ny = 4 * d, nz = 4 * d;
            double u = 0.05, re = 20;
            double nu = u * d / re;
            var lbm = new Lbm3D(nx, ny, nz, 3 * nu + 0.5, u) { Smagorinsky = 0 };
            double cx = 2.5 * d, cy = ny / 2.0, cz = nz / 2.0;
            int area = 0;
            var proj = new bool[ny * nz];
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                bool s = (x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy) + (z + 0.5 - cz) * (z + 0.5 - cz) <= d * d / 4.0;
                lbm.Solid[lbm.Index(x, y, z)] = s;
                if (s && !proj[z * ny + y])
                {
                    proj[z * ny + y] = true;
                    area++;
                }
            }

            var watch = Stopwatch.StartNew();
            lbm.Step(2500);
            double fx = 0;
            for (int i = 0; i < 200; i++)
            {
                lbm.Step();
                fx += lbm.BodyFx;
            }

            fx /= 200;
            double cdArea = fx / (0.5 * u * u * area);
            double cdNominal = fx / (0.5 * u * u * Math.PI * d * d / 4);
            double sn = 24 / re * (1 + 0.15 * Math.Pow(re, 0.687));
            _out.WriteLine($"{nx}×{ny}×{nz}: {watch.Elapsed.TotalSeconds:F1} s; Cd (voxel frontal area {area}) {cdArea:F2}, Cd (nominal πD²/4) {cdNominal:F2}, Schiller–Naumann {sn:F2}");
            Assert.InRange(cdNominal / sn, 0.7, 1.45);
        }
    }
}
