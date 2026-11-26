using System;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Analysis.Numerics;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[convergence] Three-grid Richardson extrapolation and the grid convergence index, on a known series and on a cantilever.</summary>
    public class GridConvergenceTests
    {
        private readonly ITestOutputHelper _out;

        public GridConvergenceTests(ITestOutputHelper output) => _out = output;

        [Fact]
        public void ASecondOrderSeriesGivesItsOrderAndItsLimit()
        {
            static double F(double h) => 1 + 0.5 * h * h;
            var s = GridConvergence.ThreeGrids(0.1, F(0.1), 0.15, F(0.15), 0.2, F(0.2));   // ratios 1.5 and 1.33: not constant
            Assert.Equal(ConvergenceKind.Monotonic, s.Kind);
            Assert.Equal(2.0, s.Order, 6);
            Assert.Equal(1.0, s.Extrapolated, 9);
            Assert.True(s.GciFine > 0 && s.GciFine < 0.01);

            var osc = GridConvergence.ThreeGrids(1, 1.00, 2, 1.02, 4, 0.99);
            Assert.Equal(ConvergenceKind.Oscillatory, osc.Kind);
            var div = GridConvergence.ThreeGrids(1, 1.0, 2, 1.1, 4, 1.15);   // the change grows as the grid refines
            Assert.Equal(ConvergenceKind.Divergent, div.Kind);
            Assert.Equal(3 * 0.04 / (4 - 1), GridConvergence.TwoGrids(1, 1.0, 2, 1.04), 12);
        }

        [Fact]
        public void ACantileversTipDeflectionConvergesToBeamTheory()
        {
            // L = 100 mm, 10 × 10 mm steel, 100 N at the tip; three meshes refined alike in every direction.
            double l = 0.1, b = 0.01, h = 0.01, p = 100;
            var steel = new StructuralMaterial { Name = "steel", E = 200e9, Nu = 0.3, Yield = 250e6, Density = 7850 };
            double Tip(int nx, int nyz)
            {
                var mesh = TetMesh.Box(new Vec3(0, -b / 2, -h / 2), new Vec3(l, b / 2, h / 2), nx, nyz, nyz, order: 2);
                var model = new ElasticModel(mesh, steel) { Tolerance = 1e-11 };
                model.FixNodes(x => x.X < 1e-9);
                model.Assemble();
                var f = new double[model.DofCount];
                model.AddTraction(f, c => c.X > l - 1e-9, new Vec3(0, 0, -p / (b * h)));
                var sol = model.Solve(f, model.Fixed);
                return -sol.Displacement[3 * model.NearestNode(new Vec3(l, 0, 0)) + 2];
            }

            double w1 = Tip(40, 4), w2 = Tip(20, 2), w3 = Tip(10, 1);
            var s = GridConvergence.ThreeGrids(l / 40, w1, l / 20, w2, l / 10, w3);
            double inertia = b * h * h * h / 12;
            double timoshenko = p * l * l * l / (3 * steel.E * inertia) + p * l / (5.0 / 6 * steel.ShearModulus * b * h);
            _out.WriteLine($"tip {w3 * 1e3:F5} → {w2 * 1e3:F5} → {w1 * 1e3:F5} mm; {s.Kind}, order {s.Order:F2}, extrapolated {s.Extrapolated * 1e3:F5} mm, GCI {s.GciFine * 100:F3} %; Timoshenko {timoshenko * 1e3:F5} mm");
            Assert.Equal(ConvergenceKind.Monotonic, s.Kind);
            Assert.InRange(s.Extrapolated / timoshenko, 0.99, 1.01);
            Assert.True(Math.Abs(w1 / timoshenko - 1) <= s.GciFine * 1.5 + 0.01, "the fine result lies within its convergence band of the limit");
        }
    }
}
