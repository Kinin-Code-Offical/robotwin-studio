using System;
using System.Linq;
using RobotTwin.CoreSim.Analysis.Studies;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    /// <summary>[part-study] A static study of a part against beam theory: a steel cantilever held at one end, loaded at the other.</summary>
    public class PartStudyTests
    {
        private readonly ITestOutputHelper _out;

        public PartStudyTests(ITestOutputHelper output) => _out = output;

        /// <summary>A 100 × 10 × 10 mm bar along X; its end faces' indices.</summary>
        private static (PolyMesh Bar, int Root, int Tip) Bar()
        {
            var bar = PrimitiveBuilder.Box(100, 10, 10);
            int root = -1, tip = -1;
            for (int f = 0; f < bar.Faces.Count; f++)
            {
                var n = bar.FaceNormal(f);
                if (n.X < -0.9) root = f;
                if (n.X > 0.9) tip = f;
            }

            Assert.True(root >= 0 && tip >= 0);
            return (bar, root, tip);
        }

        [Fact]
        public void ASteelCantileverBendsAsBeamTheorySays()
        {
            var (bar, root, tip) = Bar();
            var study = new PartStudy { Material = "ÇELİK", ElementSize = 2.5 };
            study.Fixtures.Add(new StudyFixture { Faces = { root } });
            study.Loads.Add(new StudyLoad { Faces = { tip }, Kind = StudyLoad.Force, Magnitude = 100, X = 0, Y = 0, Z = -1 });
            Assert.True(PartStudyRunner.Check(bar, study).Ready);
            var r = PartStudyRunner.Run(bar, study);
            foreach (string n in r.Notes) _out.WriteLine(n);

            // Euler–Bernoulli: δ = F L³ / (3 E I), σ = F L c / I; I = b h³ / 12
            double E = 200e9, L = 0.1, b = 0.01, hh = 0.01, I = b * hh * hh * hh / 12, F = 100;
            double delta = F * L * L * L / (3 * E * I), sigma = F * L * (hh / 2) / I;
            _out.WriteLine($"tip {r.MaxDisplacement * 1e3:0.0000} mm (beam {delta * 1e3:0.0000} mm), σ p98 {r.P98 / 1e6:0.0} MPa, max {r.MaxVonMises / 1e6:0.0} MPa (beam {sigma / 1e6:0.0} MPa), SF {r.MinSafetyFactor:0.0}");
            // shear deformation and the root's restraint add a little to the slender-beam value
            Assert.InRange(r.MaxDisplacement / delta, 0.95, 1.12);
            Assert.InRange(r.P98 / sigma, 0.5, 1.3);
            Assert.True(r.MaxVonMises >= r.P98);
            Assert.InRange(r.MinSafetyFactor, 235e6 / (2.5 * sigma), 235e6 / (0.5 * sigma));
            Assert.True(r.FixedNodes > 10 && r.LoadedNodes > 10);
            Assert.Contains(r.Assumptions, a => a.Contains("Doğrusal elastik"));

            // the tip goes down, the root does not move
            int lowest = Enumerable.Range(0, r.Nodes.Length).OrderBy(i => r.Displacement[3 * i + 2]).First();
            Assert.True(r.Nodes[lowest].X > 0.045, "the most deflected node is at the free end");
            int atRoot = Enumerable.Range(0, r.Nodes.Length).OrderBy(i => r.Nodes[i].X).First();
            Assert.True(r.DisplacementAt(atRoot) < 1e-12);
        }

        [Fact]
        public void TheReferenceSolutionTheSummaryQuotesMatchesBeamTheory()
        {
            var r = PartStudyRunner.Reference();
            _out.WriteLine($"reference: FEM {r.Fem * 1e3:0.0000} mm, Timoshenko {r.Theory * 1e3:0.0000} mm, {r.Deviation * 100:0.00} %");
            Assert.True(PartStudyRunner.ReferenceSolved);
            Assert.InRange(r.Theory, 0.19e-3, 0.21e-3);   // F L³ / 3EI = 0.2 mm, shear adds under 1 %
            Assert.InRange(Math.Abs(r.Deviation), 0, 0.05);
        }

        [Fact]
        public void APressureAndTheMeshCheckGiveTheSameAnswerAndAnUncertainty()
        {
            var (bar, root, tip) = Bar();
            int top = Enumerable.Range(0, bar.Faces.Count).First(f => bar.FaceNormal(f).Z > 0.9);
            // 10 kPa on the top face (100 × 10 mm): 10 N spread along the bar, half the tip's moment
            var study = new PartStudy { Material = "ALÜMİNYUM", ElementSize = 3, MeshCheck = true };
            study.Fixtures.Add(new StudyFixture { Faces = { root } });
            study.Loads.Add(new StudyLoad { Faces = { top }, Kind = StudyLoad.Pressure, Magnitude = 10_000 });
            var r = PartStudyRunner.Run(bar, study);
            double E = 69e9, L = 0.1, I = 0.01 * Math.Pow(0.01, 3) / 12, w = 10.0 / L;
            double delta = w * Math.Pow(L, 4) / (8 * E * I);   // uniformly loaded cantilever
            _out.WriteLine($"tip {r.MaxDisplacement * 1e6:0.00} µm (beam {delta * 1e6:0.00} µm), GCI {r.MeshGci * 100:0.0} %, extrapolated {r.MeshExtrapolated / 1e6:0.00} MPa");
            Assert.InRange(r.MaxDisplacement / delta, 0.9, 1.2);
            Assert.False(double.IsNaN(r.MeshGci));
            Assert.InRange(r.TotalLoad, 9.5, 10.5);
        }

        /// <summary>
        /// [validation] The tensile bar: a 100 × 10 × 10 mm steel bar held at one end and pulled with 1 kN along its axis
        /// at the other. Away from the held end the stress is uniform, σ = F / A = 10 MPa, and the free end moves
        /// δ = F L / (E A) = 5 µm (Saint-Venant: the held face, kept from narrowing, stiffens the bar a little).
        /// </summary>
        [Fact]
        public void ATensileBarCarriesFOverAAndStretchesFLOverEA()
        {
            var (bar, root, tip) = Bar();
            var study = new PartStudy { Material = "ÇELİK", ElementSize = 2.5 };
            study.Fixtures.Add(new StudyFixture { Faces = { root } });
            study.Loads.Add(new StudyLoad { Faces = { tip }, Kind = StudyLoad.Force, Magnitude = 1000, X = 1, Y = 0, Z = 0 });
            var r = PartStudyRunner.Run(bar, study);
            double E = 200e9, A = 1e-4, L = 0.1, F = 1000;
            double sigma = F / A, delta = F * L / (E * A);
            double x0 = r.Nodes.Min(n => n.X), x1 = r.Nodes.Max(n => n.X);   // the bar is centred on the origin
            var middle = Enumerable.Range(0, r.Nodes.Length).Where(i => r.Nodes[i].X > x0 + 0.4 * L && r.Nodes[i].X < x0 + 0.6 * L).ToArray();
            double mid = middle.Average(i => r.VonMises[i]);
            double tipMove = Enumerable.Range(0, r.Nodes.Length).Where(i => r.Nodes[i].X > x1 - 1e-6).Average(i => r.Displacement[3 * i]);
            _out.WriteLine($"tension: mid σ {mid / 1e6:0.000} MPa (F/A {sigma / 1e6:0.000}), spread {middle.Min(i => r.VonMises[i]) / 1e6:0.000}–{middle.Max(i => r.VonMises[i]) / 1e6:0.000} MPa; tip {tipMove * 1e6:0.0000} µm (FL/EA {delta * 1e6:0.0000} µm, {(tipMove / delta - 1) * 100:0.00} %)");
            Assert.InRange(mid / sigma, 0.98, 1.02);
            Assert.InRange(tipMove / delta, 0.97, 1.01);
            Assert.InRange(r.TotalLoad, 999, 1001);
        }

        [Fact]
        public void TheCheckSaysWhatIsMissing()
        {
            var (bar, root, tip) = Bar();
            var study = new PartStudy();
            var c = PartStudyRunner.Check(bar, study);
            Assert.False(c.Ready);
            Assert.Contains(c.Errors, e => e.Contains("Sabit yüzey yok"));
            Assert.Contains(c.Errors, e => e.Contains("Yük yok"));
            study.Fixtures.Add(new StudyFixture { Faces = { root } });
            study.Gravity = true;
            Assert.True(PartStudyRunner.Check(bar, study).Ready);
            study.Loads.Add(new StudyLoad { Faces = { 99 } });
            Assert.Contains(PartStudyRunner.Check(bar, study).Errors, e => e.Contains("artık parçada yok"));
            study.Loads[0].Faces = new() { root };
            Assert.Contains(PartStudyRunner.Check(bar, study).Warnings, w => w.Contains("sabit yüzeylerde"));
            Assert.Throws<InvalidOperationException>(() => PartStudyRunner.Run(bar, new PartStudy()));
            Assert.Equal(1, PartStudyRunner.TriangleDistance(new Vec3(0, 0, 1), new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0)), 9);
            Assert.Equal(1, PartStudyRunner.TriangleDistance(new Vec3(2, 0, 0), new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 1, 0)), 9);
        }
    }
}
