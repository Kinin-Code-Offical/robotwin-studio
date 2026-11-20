using System;
using RobotTwin.CoreSim.Analysis.Thermal;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[analysis] Finite-volume heat conduction against analytic solutions.</summary>
    public class ThermalValidationTests
    {
        private readonly ITestOutputHelper _out;

        public ThermalValidationTests(ITestOutputHelper output)
        {
            _out = output;
        }

        /// <summary>Slab 0 ≤ x ≤ L at T0, face x = 0 suddenly held at T1, x = L insulated: Fourier series solution.</summary>
        private static double Analytic(double x, double t, double l, double alpha, double t0, double t1)
        {
            double sum = 0;
            for (int n = 0; n < 200; n++)
            {
                double lam = (2 * n + 1) * Math.PI / (2 * l);
                sum += 4 / ((2 * n + 1) * Math.PI) * Math.Sin(lam * x) * Math.Exp(-lam * lam * alpha * t);
            }

            return t1 + (t0 - t1) * sum;
        }

        [Fact]
        public void TransientOneDimensionalConductionMatchesFourierSeries()
        {
            // Aluminium bar 50 mm, 100 cells; Crank–Nicolson.
            double l = 0.05, k = 167, rhoCp = 2700 * 896, t0 = 20, t1 = 100;
            int nx = 100;
            var g = new ThermalGrid(nx, 1, 1, l / nx, 0.01, 0.01, Vec3.Zero) { Theta = 0.5, Radiation = false };
            g.SetMaterial(k, rhoCp);
            g.SetUniformTemperature(t0);
            for (int s = 0; s < 6; s++) g.Sides[s].Kind = ThermalSideKind.Insulated;
            g.Sides[0].Kind = ThermalSideKind.FixedTemperature;
            g.Sides[0].Temperature = t1;
            double alpha = k / rhoCp, dt = 0.01;
            double maxErr = 0;
            foreach (double target in new[] { 0.5, 2.0, 5.0 })
            {
                while (g.Time < target - 1e-9) g.Step(dt);
                for (int i = 0; i < nx; i++)
                {
                    double x = (i + 0.5) * l / nx;
                    double exact = Analytic(x, g.Time, l, alpha, t0, t1);
                    maxErr = Math.Max(maxErr, Math.Abs(g.T[i] - exact));
                }

                _out.WriteLine($"t = {g.Time:F2} s: T(L) FV {g.T[nx - 1]:F4} °C, analytic {Analytic((nx - 0.5) * l / nx, g.Time, l, alpha, t0, t1):F4} °C");
            }

            _out.WriteLine($"max |error| {maxErr:F4} K over ΔT = {t1 - t0} K");
            Assert.True(maxErr < 0.1, $"max error {maxErr}");
        }

        [Fact]
        public void HeatedPlateReachesTheConvectionEnergyBalance()
        {
            // 2D board 50 × 50 × 1.6 mm, 1 W uniformly, h = 10 W/m²K on both faces and the edges, no radiation:
            // steady mean temperature rise = P / (h·A_total) (high in-plane conduction → nearly uniform).
            var g = new ThermalGrid(25, 25, 1, 0.002, 0.002, 0.0016, Vec3.Zero) { Radiation = false, H = 10 };
            g.SetMaterial(200, 2.4e6);
            g.SetUniformTemperature(25);
            g.AmbientC = 25;
            g.AddSource(new Vec3(0, 0, 0), new Vec3(0.05, 0.05, 0.0016), 1.0);
            for (int i = 0; i < 400; i++) g.Step(5);
            double area = 2 * 0.05 * 0.05 + 4 * 0.05 * 0.0016;
            double expected = 1.0 / (10 * area);
            double rise = g.MeanTemperature() - 25;
            _out.WriteLine($"steady rise FV {rise:F3} K, energy balance {expected:F3} K");
            Assert.InRange(rise / expected, 0.98, 1.02);
        }
    }
}
