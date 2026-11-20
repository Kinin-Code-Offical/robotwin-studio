using System;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Mechanics;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[analysis] Structural solvers against textbook results.</summary>
    public class FemValidationTests
    {
        private readonly ITestOutputHelper _out;

        public FemValidationTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static StructuralMaterial Steel => new StructuralMaterial { Name = "steel", E = 200e9, Nu = 0.3, Yield = 250e6, Density = 7850 };

        [Fact]
        public void HeatedBarIsStressFreeWhenFreeAndCarriesEAlphaDeltaTWhenClamped()
        {
            // 100 × 10 × 10 mm aluminium bar heated by ΔT = 50 K: free, it grows α·ΔT·L and stays
            // stress-free; held between rigid walls (ux = 0 at both ends, lateral free) it carries
            // σxx = −E·α·ΔT everywhere.
            double l = 0.1, b = 0.01, dT = 50, alpha = 23e-6;
            var al = new StructuralMaterial { Name = "Al", E = 69e9, Nu = 0.33, Yield = 276e6, Density = 2700, Alpha = alpha };
            var mesh = TetMesh.Box(new Vec3(0, -b / 2, -b / 2), new Vec3(l, b / 2, b / 2), 20, 4, 4, order: 2);

            var free = new ElasticModel(mesh, al) { Tolerance = 1e-10, ThermalStrain = alpha * dT };
            var fsol = free.Solve(new FemLoadCase { InertiaRelief = true });
            int tip = free.NearestNode(new Vec3(l, 0, 0)), root = free.NearestNode(new Vec3(0, 0, 0));
            double growth = fsol.Displacement[3 * tip] - fsol.Displacement[3 * root];
            _out.WriteLine($"free: growth {growth * 1e6:F2} µm (α·ΔT·L = {alpha * dT * l * 1e6:F2} µm), max σ_vM {fsol.MaxVonMises / 1e6:F4} MPa");
            Assert.InRange(growth / (alpha * dT * l), 0.99, 1.01);
            Assert.True(fsol.MaxVonMises < 0.01 * al.E * alpha * dT, "a freely expanding part is stress-free");

            var held = new ElasticModel(mesh, al) { Tolerance = 1e-10, ThermalStrain = alpha * dT };
            for (int i = 0; i < mesh.Nodes.Count; i++)
            {
                var x = mesh.Nodes[i];
                if (x.X < 1e-9 || x.X > l - 1e-9) held.Fixed[3 * i] = true;              // walls
                if (Math.Abs(x.Y) < 1e-9) held.Fixed[3 * i + 1] = true;                   // symmetry y = 0
                if (Math.Abs(x.Z) < 1e-9) held.Fixed[3 * i + 2] = true;                   // symmetry z = 0
            }

            held.Assemble();
            var f = held.LoadVector(new FemLoadCase { InertiaRelief = false }, out _, out _);
            var hsol = held.Solve(f, held.Fixed);
            int mid = held.NearestNode(new Vec3(l / 2, b / 4, b / 4));
            double sxx = hsol.Stress[6 * mid];
            _out.WriteLine($"clamped: σxx mid-span {sxx / 1e6:F2} MPa, −E·α·ΔT = {-al.E * alpha * dT / 1e6:F2} MPa");
            Assert.InRange(sxx / (-al.E * alpha * dT), 0.98, 1.02);
        }

        [Fact]
        public void PlaLosesItsStiffnessAboveTheGlassTransition()
        {
            var pla = StructuralMaterial.For("PLA");
            _out.WriteLine($"PLA E/E25: 40 °C {pla.ModulusFactor(40):F2}, 55 °C {pla.ModulusFactor(55):F2}, 60 °C {pla.ModulusFactor(60):F2}, 70 °C {pla.ModulusFactor(70):F3}; {pla.StateAt(70)}, {pla.StateAt(170)}");
            Assert.InRange(pla.ModulusFactor(40), 0.85, 1.0);
            Assert.True(pla.ModulusFactor(70) < 0.1, "above T_g PLA is rubbery");
            Assert.Equal("eriyor", pla.StateAt(170));
            var steel = StructuralMaterial.For("ÇELİK");
            Assert.True(steel.ModulusFactor(150) > 0.95, "steel barely notices 150 °C");
        }

        [Fact]
        public void CantileverTipDeflectionAndRootStressMatchBeamTheory()
        {
            // L = 100 mm, 10 × 10 mm section, P = 100 N at the tip (−z), clamped at x = 0.
            double l = 0.1, b = 0.01, h = 0.01, p = 100;
            var mesh = TetMesh.Box(new Vec3(0, -b / 2, -h / 2), new Vec3(l, b / 2, h / 2), 40, 4, 4, order: 2);
            var model = new ElasticModel(mesh, Steel) { Tolerance = 1e-10 };
            model.FixNodes(x => x.X < 1e-9);
            model.Assemble();
            var f = new double[model.DofCount];
            var applied = model.AddTraction(f, c => c.X > l - 1e-9, new Vec3(0, 0, -p / (b * h)));
            Assert.InRange(applied.Z, -p * 1.0001, -p * 0.9999);
            var sol = model.Solve(f, model.Fixed);
            double inertia = b * h * h * h / 12;
            double eb = p * l * l * l / (3 * Steel.E * inertia);
            double shear = p * l / (5.0 / 6 * Steel.ShearModulus * b * h);   // Timoshenko shear deflection
            int tip = model.NearestNode(new Vec3(l, 0, 0));
            double w = -sol.Displacement[3 * tip + 2];
            _out.WriteLine($"T10 {mesh.Nodes.Count} nodes, {mesh.Elements.Count} el, {sol.Solver}");
            _out.WriteLine($"tip deflection FEM {w * 1e3:F5} mm, Euler–Bernoulli {eb * 1e3:F5} mm, Timoshenko {(eb + shear) * 1e3:F5} mm, error vs Timoshenko {(w / (eb + shear) - 1) * 100:F2} %");
            Assert.InRange(w / (eb + shear), 0.97, 1.02);

            // Bending stress at the top fibre along the span, extrapolated to the root: σ = P·L·c / I.
            double SigmaTop(double x)
            {
                int n = model.NearestNode(new Vec3(x, 0, h / 2));
                return sol.Stress[n * 6];
            }

            double s1 = SigmaTop(0.25 * l), s2 = SigmaTop(0.5 * l);
            double root = s1 + (s1 - s2) * (0.25 / 0.25);
            double rootTheory = p * l * (h / 2) / inertia;
            _out.WriteLine($"σxx(L/4) = {s1 / 1e6:F2} MPa (theory {p * 0.75 * l * h / 2 / inertia / 1e6:F2}), σxx(L/2) = {s2 / 1e6:F2} MPa (theory {p * 0.5 * l * h / 2 / inertia / 1e6:F2})");
            _out.WriteLine($"root stress (extrapolated) {root / 1e6:F2} MPa, theory {rootTheory / 1e6:F2} MPa, error {(root / rootTheory - 1) * 100:F2} %");
            Assert.InRange(s1 / (p * 0.75 * l * h / 2 / inertia), 0.95, 1.05);
            Assert.InRange(root / rootTheory, 0.95, 1.05);
        }

        [Fact]
        public void PlateWithHoleHasStressConcentrationOfAboutThree()
        {
            // Quarter of a 50 × 80 mm plate with a Ø10 mm hole (d/W = 0.2) in tension σ0 along y; symmetry planes x = 0, y = 0, z = 0.
            double a = 0.005, halfW = 0.025, halfL = 0.04, t = 0.001, s0 = 1e6;
            var plate = Solid.Box(new Vec3(0, 0, 0), new Vec3(halfW, halfL, t)).Minus(Solid.CylinderAt(Vec3.Zero, a, 1));
            var mesh = TetMesh.FromSolid(plate, 0.0008, order: 2);
            var model = new ElasticModel(mesh, Steel) { Tolerance = 1e-10 };
            model.FixNodes(x => x.X < 1e-9, true, false, false);
            model.FixNodes(x => x.Y < 1e-9, false, true, false);
            model.FixNodes(x => x.Z < 1e-9, false, false, true);
            model.Assemble();
            var f = new double[model.DofCount];
            model.AddTraction(f, c => c.Y > halfL - 1e-9, new Vec3(0, s0, 0));
            var sol = model.Solve(f, model.Fixed);
            // Peak σyy on the hole edge at the net section (x = a, y = 0).
            double peak = 0;
            for (int n = 0; n < mesh.Nodes.Count; n++)
            {
                var x = mesh.Nodes[n];
                if (x.Y < 1e-6 && Math.Abs(Math.Sqrt(x.X * x.X + x.Y * x.Y) - a) < 1e-4) peak = Math.Max(peak, sol.Stress[n * 6 + 1]);
            }

            double kt = peak / s0;
            double dw = 2 * a / (2 * halfW);
            double peterson = (3.0 - 3.14 * dw + 3.667 * dw * dw - 1.527 * dw * dw * dw) / (1 - dw);
            _out.WriteLine($"{mesh.Nodes.Count} nodes, {mesh.Elements.Count} T10, unsnapped {mesh.UnsnappedNodes}, {sol.Solver}");
            _out.WriteLine($"Kt (gross) FEM {kt:F3}; Peterson finite-width {peterson:F3}; infinite plate (Kirsch) 3.000");
            Assert.InRange(kt, 2.8, 3.4);
            Assert.InRange(kt / peterson, 0.92, 1.08);
        }

        [Fact]
        public void BeamElementCantileverIsExact()
        {
            double l = 0.1, b = 0.01, h = 0.01, p = 100;
            var beam = new BeamModel(Vec3.Zero, new Vec3(l, 0, 0), 10, new BeamSection { Width = b, Height = h }, Steel);
            beam.Clamp(0);
            var f = new double[beam.NodeCount * 6];
            f[10 * 6 + 2] = -p;
            var sol = beam.Solve(f);
            double inertia = b * h * h * h / 12;
            double eb = p * l * l * l / (3 * Steel.E * inertia);
            double w = -sol.Displacement(10).Z;
            double root = sol.VonMises[0];
            double rootTheory = p * l * (h / 2) / inertia;
            _out.WriteLine($"beam tip {w * 1e3:F6} mm vs {eb * 1e3:F6} mm; root σ {root / 1e6:F3} MPa vs {rootTheory / 1e6:F3} MPa");
            Assert.InRange(w / eb, 0.9999, 1.0001);
            Assert.InRange(root / rootTheory, 0.999, 1.001);

            // Torsion of a round bar: φ = T·L/(G·J), τ = T·r/J.
            var shaft = new BeamModel(Vec3.Zero, new Vec3(0.2, 0, 0), 8, new BeamSection { Circular = true, Diameter = 0.01 }, Steel);
            shaft.Clamp(0);
            var ft = new double[shaft.NodeCount * 6];
            ft[8 * 6 + 3] = 5;
            var st = shaft.Solve(ft);
            double jj = Math.PI * Math.Pow(0.01, 4) / 32;
            Assert.InRange(st.Dof[8 * 6 + 3] / (5 * 0.2 / (Steel.ShearModulus * jj)), 0.9999, 1.0001);
            Assert.InRange(st.VonMises[0] / (Math.Sqrt(3) * 5 * 0.005 / jj), 0.999, 1.001);
        }

        [Fact]
        public void InertiaReliefBalancesAFreeAcceleratingBar()
        {
            // A free bar pulled at one end accelerates; inertia relief gives σxx(x) = F·(1 − x/L)/A and ~zero support reactions.
            double l = 0.1, b = 0.01, force = 50;
            var mesh = TetMesh.Box(new Vec3(0, -b / 2, -b / 2), new Vec3(l, b / 2, b / 2), 20, 2, 2, order: 2);
            var model = new ElasticModel(mesh, Steel);
            var lc = new FemLoadCase { InertiaRelief = true };
            lc.Loads.Add(new PointLoad(new Vec3(l, 0, 0), new Vec3(force, 0, 0)));
            var sol = model.Solve(lc);
            double mid = sol.Stress[model.NearestNode(new Vec3(l / 2, 0, 0)) * 6];
            _out.WriteLine($"relief a = {sol.ReliefAcceleration}, σ(L/2) = {mid / 1e3:F2} kPa (theory {force * 0.5 / (b * b) / 1e3:F2}), reaction ratio {sol.ReactionRatio:E2}");
            Assert.InRange(sol.ReliefAcceleration.X, force / model.Mass * 0.99, force / model.Mass * 1.01);
            Assert.InRange(mid / (force * 0.5 / (b * b)), 0.9, 1.1);
            Assert.True(sol.ReactionRatio < 1e-3);
        }
    }
}
