using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Analysis.Jobs;
using RobotTwin.CoreSim.Analysis.Numerics;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotTwin.CoreSim.Analysis.Studies
{
    /// <summary>[part-study] Faces of the part that are held: every node on them stays where it is (a fixed geometry).</summary>
    public sealed class StudyFixture
    {
        /// <summary>The part mesh's face indices.</summary>
        public List<int> Faces { get; set; } = new List<int>();
    }

    /// <summary>[part-study] A load on faces of the part.</summary>
    public sealed class StudyLoad
    {
        public const string Force = "kuvvet", Pressure = "basınç";

        public List<int> Faces { get; set; } = new List<int>();

        /// <summary><see cref="Force"/>: a total force spread over the faces along (X, Y, Z); <see cref="Pressure"/>: a pressure pushing on them.</summary>
        public string Kind { get; set; } = Force;

        /// <summary>Newtons for a force, pascals for a pressure.</summary>
        public double Magnitude { get; set; } = 10;

        /// <summary>A force's direction (normalised when used); a pressure acts along each face's inward normal.</summary>
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; } = -1;
    }

    /// <summary>
    /// [part-study] A linear static study of one part, set up as structural tools set one up: the material, the fixed faces,
    /// the loads (forces, pressures, gravity), the mesh's element size; saved with the part so it can be run again after a
    /// change. Face indices refer to the part's polygon mesh as it is when the study is defined.
    /// </summary>
    public sealed class PartStudy
    {
        public string Name { get; set; } = "Statik 1";

        /// <summary>The part (its object in the model) the study is on.</summary>
        public string ObjectId { get; set; } = string.Empty;

        /// <summary>A material name <see cref="StructuralMaterial.For"/> knows.</summary>
        public string Material { get; set; } = "PLA";

        public List<StudyFixture> Fixtures { get; set; } = new List<StudyFixture>();

        public List<StudyLoad> Loads { get; set; } = new List<StudyLoad>();

        /// <summary>The part's own weight (9.81 m/s² along −Z).</summary>
        public bool Gravity { get; set; }

        /// <summary>Element size (mm); 0: chosen from the part's size.</summary>
        public double ElementSize { get; set; }

        /// <summary>Re-solve on two coarser meshes and report the mesh's convergence (GCI).</summary>
        public bool MeshCheck { get; set; }

        public PartStudy Clone() => new PartStudy
        {
            Name = Name, ObjectId = ObjectId, Material = Material, Gravity = Gravity, ElementSize = ElementSize, MeshCheck = MeshCheck,
            Fixtures = Fixtures.Select(f => new StudyFixture { Faces = f.Faces.ToList() }).ToList(),
            Loads = Loads.Select(l => new StudyLoad { Faces = l.Faces.ToList(), Kind = l.Kind, Magnitude = l.Magnitude, X = l.X, Y = l.Y, Z = l.Z }).ToList(),
        };
    }

    /// <summary>[part-study] What a study is missing before it can be solved (Fusion's pre-check); empty when it is ready.</summary>
    public sealed class StudyCheck
    {
        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public bool Ready => Errors.Count == 0;
    }

    /// <summary>[part-study] A solved study: the mesh and its fields, the extremes, and how far to trust them.</summary>
    /// <summary>[part-study] The solver on a case with a known answer: its tip deflection beside beam theory's (m).</summary>
    public sealed class StudyReference
    {
        public double Fem { get; set; }

        public double Theory { get; set; }

        /// <summary>The relative difference, FEM against theory.</summary>
        public double Deviation => Theory > 0 ? Fem / Theory - 1 : double.NaN;
    }

    public sealed class PartStudyResult
    {
        public string Material { get; set; } = string.Empty;
        public double Yield { get; set; }

        /// <summary>The mesh's nodes (metres, the part's frame), its boundary triangles (6 nodes each, as <see cref="TetMesh.Surface"/>).</summary>
        public Vec3[] Nodes { get; set; } = Array.Empty<Vec3>();

        public int[] Surface { get; set; } = Array.Empty<int>();
        public int Elements { get; set; }
        public double ElementSize { get; set; }

        /// <summary>Nodal von Mises stress (Pa) and displacement (3 per node, m).</summary>
        public double[] VonMises { get; set; } = Array.Empty<double>();

        public double[] Displacement { get; set; } = Array.Empty<double>();
        public double MaxVonMises { get; set; }

        /// <summary>The 98th percentile of the nodal stress: the peak away from the singular spots under point loads and sharp corners.</summary>
        public double P98 { get; set; }

        public double MaxDisplacement { get; set; }
        public double MinSafetyFactor { get; set; }
        public double TotalLoad { get; set; }
        public int FixedNodes { get; set; }
        public int LoadedNodes { get; set; }
        public int Iterations { get; set; }
        public double Residual { get; set; }

        /// <summary>The fine mesh's convergence index (fraction); NaN when the mesh was not checked.</summary>
        public double MeshGci { get; set; } = double.NaN;

        /// <summary>The value the representative stress tends to as the mesh is refined; NaN when not checked.</summary>
        public double MeshExtrapolated { get; set; } = double.NaN;

        public List<string> Notes { get; } = new List<string>();

        /// <summary>What the model assumes (said beside the results, as the reliability summary).</summary>
        public List<string> Assumptions { get; } = new List<string>();

        public double DisplacementAt(int node) => Math.Sqrt(Displacement[3 * node] * Displacement[3 * node] + Displacement[3 * node + 1] * Displacement[3 * node + 1] + Displacement[3 * node + 2] * Displacement[3 * node + 2]);
    }

    /// <summary>
    /// [part-study] Runs a <see cref="PartStudy"/> with the app's own FEM: the part's mesh (mm) as a solid, quadratic
    /// tetrahedra (T10) of the chosen size, the fixed faces' nodes held, each force spread over its faces' nodes, each
    /// pressure as the faces' area times the pressure along their inward normals, gravity as a body force; a conjugate
    /// gradient solve; von Mises, displacement and the safety factor against the material's yield.
    /// </summary>
    public static class PartStudyRunner
    {
        private static readonly Lazy<StudyReference> _reference = new Lazy<StudyReference>(SolveReference, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// The reference solution the reliability summary quotes: a 100 × 10 × 10 mm steel cantilever held at one end and
        /// pushed down with 100 N at the other, solved as any study is (2.5 mm elements) and set against beam theory with
        /// shear (Timoshenko: δ = F L³ / 3EI + F L / κGA, κ = 5/6). Solved once per process, on first use.
        /// </summary>
        public static StudyReference Reference() => _reference.Value;

        /// <summary>The reference case has been solved (quoting it costs nothing now).</summary>
        public static bool ReferenceSolved => _reference.IsValueCreated;

        private static StudyReference SolveReference()
        {
            var bar = PrimitiveBuilder.Box(100, 10, 10);
            int root = -1, tip = -1;
            for (int f = 0; f < bar.Faces.Count; f++)
            {
                var n = bar.FaceNormal(f);
                if (n.X < -0.9) root = f;
                if (n.X > 0.9) tip = f;
            }

            var study = new PartStudy { Material = "ÇELİK", ElementSize = 2.5 };
            study.Fixtures.Add(new StudyFixture { Faces = { root } });
            study.Loads.Add(new StudyLoad { Faces = { tip }, Kind = StudyLoad.Force, Magnitude = 100, X = 0, Y = 0, Z = -1 });
            var r = Run(bar, study);
            var mat = StructuralMaterial.For("ÇELİK");
            double L = 0.1, b = 0.01, h = 0.01, F = 100, I = b * h * h * h / 12, A = b * h, G = mat.E / (2 * (1 + mat.Nu));
            double theory = F * L * L * L / (3 * mat.E * I) + F * L / (5.0 / 6.0 * G * A);
            return new StudyReference { Fem = r.MaxDisplacement, Theory = theory };
        }

        /// <summary>The pre-check: the faces exist, something holds the part, something loads it.</summary>
        public static StudyCheck Check(PolyMesh part, PartStudy study)
        {
            var c = new StudyCheck();
            if (part.IsEmpty) c.Errors.Add("Parçanın gövdesi yok.");
            bool Valid(IEnumerable<int> faces) => faces.Any() && faces.All(f => f >= 0 && f < part.Faces.Count);
            if (study.Fixtures.Count == 0 || study.Fixtures.All(f => f.Faces.Count == 0))
                c.Errors.Add("Sabit yüzey yok: parça yük altında serbestçe hareket eder (en az bir yüzeyi sabitleyin).");
            else if (study.Fixtures.Any(f => !Valid(f.Faces)))
                c.Errors.Add("Bir sabitlemenin yüzeyleri artık parçada yok (parça değişti): yeniden seçin.");
            if (study.Loads.Count == 0 && !study.Gravity) c.Errors.Add("Yük yok: bir kuvvet, basınç ya da yerçekimi ekleyin.");
            foreach (var l in study.Loads)
            {
                if (!Valid(l.Faces)) c.Errors.Add("Bir yükün yüzeyleri artık parçada yok (parça değişti): yeniden seçin.");
                if (l.Kind == StudyLoad.Force && Math.Abs(l.X) + Math.Abs(l.Y) + Math.Abs(l.Z) < 1e-12) c.Errors.Add("Bir kuvvetin yönü yok.");
                if (l.Magnitude == 0) c.Warnings.Add("Büyüklüğü sıfır olan bir yük var.");
            }

            var fixedFaces = new HashSet<int>(study.Fixtures.SelectMany(f => f.Faces));
            if (study.Loads.Any(l => l.Faces.All(fixedFaces.Contains))) c.Warnings.Add("Bir yük yalnız sabit yüzeylerde: doğrudan mesnede gider, gerilme oluşturmaz.");
            if (StructuralMaterial.Names.All(n => !string.Equals(StructuralMaterial.For(study.Material).Name, StructuralMaterial.For(n).Name, StringComparison.Ordinal)))
                c.Warnings.Add("Malzeme tanınmadı: PLA kullanılır.");
            return c;
        }

        public static PartStudyResult Run(PolyMesh part, PartStudy study, CancellationToken cancel = default, Action<string>? progress = null)
        {
            var check = Check(part, study);
            if (!check.Ready) throw new InvalidOperationException(check.Errors[0]);
            var mat = StructuralMaterial.For(study.Material);
            var result = new PartStudyResult { Material = mat.Name, Yield = mat.Yield };
            var tri = part.ToMeshModel(0.001);   // mm → m
            var solid = new MeshSolid(tri, Vec3.Zero);
            var size = solid.Bounds.Size;
            double h = study.ElementSize > 0 ? study.ElementSize * 0.001 : AutoSize(size);
            progress?.Invoke("ağ");
            var sol = Solve(part, study, solid, mat, h, cancel, out var mesh, out int fixedNodes, out int loadedNodes);
            result.Nodes = mesh.Nodes.ToArray();
            result.Surface = mesh.Surface.SelectMany(f => f).ToArray();
            result.Elements = mesh.Elements.Count;
            result.ElementSize = mesh.ElementSize;
            result.VonMises = sol.VonMises;
            result.Displacement = sol.Displacement;
            result.MaxVonMises = sol.MaxVonMises;
            result.P98 = StressJob.P98(sol.VonMises.Select(v => (float)v).ToArray());
            result.MaxDisplacement = sol.MaxDisplacement;
            result.MinSafetyFactor = sol.MinSafetyFactor;
            result.TotalLoad = sol.TotalLoad;
            result.FixedNodes = fixedNodes;
            result.LoadedNodes = loadedNodes;
            result.Iterations = sol.Solver.Iterations;
            result.Residual = sol.Solver.Residual;
            result.Notes.Add(string.Format(CultureInfo.CurrentCulture, "{0} ikinci derece dörtyüzlü (T10), {1} düğüm, eleman boyu {2:0.##} mm; {3} düğüm sabit, {4} düğüm yüklü.",
                mesh.Elements.Count, mesh.Nodes.Count, mesh.ElementSize * 1000, fixedNodes, loadedNodes));
            if (!sol.Solver.Converged) result.Notes.Add("Çözücü istenen artığa inmedi: sonuçlar yaklaşık.");
            if (mesh.UnsnappedNodes > 0) result.Notes.Add($"{mesh.UnsnappedNodes} sınır düğümü eğri yüzeye taşınamadı (basamaklı kaldı).");
            foreach (string w in check.Warnings) result.Notes.Add(w);
            result.Assumptions.Add("Doğrusal elastik malzeme, küçük yer değiştirme (gerilme akma sınırının altında geçerli).");
            result.Assumptions.Add("Sabit yüzeyler tamamen tutulur (gerçek bağlantıdan daha rijit).");
            result.Assumptions.Add("Kuvvetler yüzeydeki düğümlere eşit paylaştırılır; keskin köşe ve sabitlemenin kenarı tekil tepe verir, bu yüzden %98'lik değer de yazılır.");

            if (study.MeshCheck && mesh.Nodes.Count <= 20000)
            {
                progress?.Invoke("yakınsama");
                // two coarser meshes beside the user's: a three-grid study of the representative stress
                var medium = Solve(part, study, solid, mat, h * 1.4, cancel, out var m2, out _, out _);
                var coarse = Solve(part, study, solid, mat, h * 1.96, cancel, out var m3, out _, out _);
                double f2 = StressJob.P98(medium.VonMises.Select(v => (float)v).ToArray()), f3 = StressJob.P98(coarse.VonMises.Select(v => (float)v).ToArray());
                if (m3.ElementSize > m2.ElementSize * 1.05 && m2.ElementSize > mesh.ElementSize * 1.05)
                {
                    var g = GridConvergence.ThreeGrids(mesh.ElementSize, result.P98, m2.ElementSize, f2, m3.ElementSize, f3);
                    result.MeshGci = g.GciFine;
                    result.MeshExtrapolated = g.Extrapolated;
                }
                else
                {
                    result.MeshGci = Math.Abs(result.P98 - f2) / Math.Max(1e-12, result.P98);
                }
            }

            return result;
        }

        /// <summary>Distance from <paramref name="p"/> to the triangle a, b, c (its closest point, as Ericson finds it).</summary>
        public static double TriangleDistance(Vec3 p, Vec3 a, Vec3 b, Vec3 c)
        {
            Vec3 ab = b - a, ac = c - a, ap = p - a;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            if (d1 <= 0 && d2 <= 0) return (p - a).Length;
            Vec3 bp = p - b;
            double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
            if (d3 >= 0 && d4 <= d3) return (p - b).Length;
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return (p - (a + ab * (d1 / (d1 - d3)))).Length;
            Vec3 cp = p - c;
            double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
            if (d6 >= 0 && d5 <= d6) return (p - c).Length;
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return (p - (a + ac * (d2 / (d2 - d6)))).Length;
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return (p - (b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))))).Length;
            double denom = 1 / (va + vb + vc);
            return (p - (a + ab * (vb * denom) + ac * (vc * denom))).Length;
        }

        /// <summary>An element size giving a few thousand elements on the part (m).</summary>
        public static double AutoSize(Vec3 size)
        {
            double vol = Math.Max(1e-12, size.X * size.Y * size.Z);
            double thin = Math.Min(size.X, Math.Min(size.Y, size.Z));
            return Math.Max(thin / 4, Math.Min(thin / 2, Math.Pow(vol / 3000, 1.0 / 3)));
        }

        private static FemSolution Solve(PolyMesh part, PartStudy study, Solid solid, StructuralMaterial mat, double h, CancellationToken cancel,
            out TetMesh mesh, out int fixedNodes, out int loadedNodes)
        {
            var tm = TetMesh.FromSolid(solid, h, 2);
            mesh = tm;
            if (tm.Elements.Count == 0) throw new InvalidOperationException("Ağ oluşturulamadı: eleman boyu parçaya göre büyük.");
            var model = new ElasticModel(tm, mat.Clone()) { Tolerance = 1e-8 };
            double tol = 0.35 * tm.ElementSize;
            var boundary = tm.SurfaceNodes;

            // nodes on given faces: boundary nodes within a third of an element of the faces' polygons
            List<int> NodesOn(IEnumerable<int> faces)
            {
                var tris = new List<(Vec3 A, Vec3 B, Vec3 C)>();
                foreach (int f in faces.Distinct())
                {
                    var loop = part.Faces[f];
                    for (int k = 1; k + 1 < loop.Length; k++)
                        tris.Add((part.Vertices[loop[0]] * 0.001, part.Vertices[loop[k]] * 0.001, part.Vertices[loop[k + 1]] * 0.001));
                }

                var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
                var hi = new Vec3(double.MinValue, double.MinValue, double.MinValue);
                foreach (var (a, b, c) in tris)
                    foreach (var p in new[] { a, b, c })
                    {
                        lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                        hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                    }

                var list = new List<int>();
                foreach (int n in boundary)
                {
                    var p = tm.Nodes[n];
                    if (p.X < lo.X - tol || p.Y < lo.Y - tol || p.Z < lo.Z - tol || p.X > hi.X + tol || p.Y > hi.Y + tol || p.Z > hi.Z + tol) continue;
                    foreach (var (a, b, c) in tris)
                        if (TriangleDistance(p, a, b, c) <= tol)
                        {
                            list.Add(n);
                            break;
                        }
                }

                return list;
            }

            var held = new HashSet<int>(study.Fixtures.SelectMany(f => NodesOn(f.Faces)));
            if (held.Count == 0) throw new InvalidOperationException("Sabit yüzeylerde ağ düğümü bulunamadı: eleman boyunu küçültün.");
            model.FixNodes(p => false);
            for (int i = 0; i < tm.Nodes.Count; i++)
                if (held.Contains(i))
                    model.Fixed[3 * i] = model.Fixed[3 * i + 1] = model.Fixed[3 * i + 2] = true;
            fixedNodes = held.Count;

            var lc = new FemLoadCase { InertiaRelief = false, Gravity = study.Gravity ? new Vec3(0, 0, -9.81) : Vec3.Zero };
            var loaded = new HashSet<int>();
            foreach (var l in study.Loads)
            {
                if (l.Kind == StudyLoad.Pressure)
                {
                    // each face: its area × pressure, pushing along its inward normal, shared by the nodes on it
                    foreach (int f in l.Faces.Distinct())
                    {
                        var nodes = NodesOn(new[] { f });
                        if (nodes.Count == 0) continue;
                        var force = part.FaceNormal(f) * (-l.Magnitude * part.FaceArea(f) * 1e-6);
                        foreach (int n in nodes)
                        {
                            lc.Loads.Add(new PointLoad(tm.Nodes[n], force * (1.0 / nodes.Count), "basınç"));
                            loaded.Add(n);
                        }
                    }
                }
                else
                {
                    var nodes = NodesOn(l.Faces);
                    if (nodes.Count == 0) continue;
                    var dir = new Vec3(l.X, l.Y, l.Z).Normalized();
                    var share = dir * (l.Magnitude / nodes.Count);
                    foreach (int n in nodes)
                    {
                        lc.Loads.Add(new PointLoad(tm.Nodes[n], share, "kuvvet"));
                        loaded.Add(n);
                    }
                }
            }

            loadedNodes = loaded.Count;
            model.Assemble(cancel);
            return model.Solve(lc, cancel);
        }
    }
}
