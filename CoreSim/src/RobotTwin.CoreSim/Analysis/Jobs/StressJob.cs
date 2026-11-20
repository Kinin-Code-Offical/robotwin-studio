using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis.Jobs
{
    /// <summary>A part to analyse: its solid in the body frame and how it sits in the world.</summary>
    public sealed class PartGeometry
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public Solid Solid { get; set; } = null!;
        public string MaterialName { get; set; } = "PLA";
        public bool IsStatic { get; set; }
        public Vec3 Position { get; set; }
        public Quat Orientation { get; set; } = Quat.Identity;
        public double Mass { get; set; }

        /// <summary>Body centre relative to the object origin, in the object frame (replay placement).</summary>
        public Vec3 BodyOffset { get; set; }

        /// <summary>"box", "cylinder", "sphere", "hull", "mesh".</summary>
        public string Shape { get; set; } = "box";

        /// <summary>Box half extents; cylinder (r, r, half height).</summary>
        public Vec3 Size { get; set; }
    }

    /// <summary>
    /// [analysis] Linear static stress of one part at chosen instants of the recorded run: tetrahedral
    /// FEM (or a beam model for slender links) loaded with the recorded contact forces, joint
    /// reactions, gravity and inertia (inertia relief), material from the part.
    /// </summary>
    public static class StressJob
    {
        /// <summary>Indices of the instants to solve: half evenly spaced, half at the largest load peaks.</summary>
        public static List<int> ChooseInstants(IReadOnlyList<LoadSample> loads, int count)
        {
            int m = loads.Count;
            var set = new SortedSet<int>();
            if (m == 0) return new List<int>();
            count = Math.Max(1, Math.Min(count, m));
            int even = Math.Max(1, (count + 1) / 2);
            for (int k = 0; k < even; k++) set.Add(even == 1 ? m - 1 : (int)Math.Round(k * (m - 1.0) / (even - 1)));
            int sep = Math.Max(1, m / (2 * count));
            foreach (int i in Enumerable.Range(0, m).OrderByDescending(i => loads[i].Magnitude).ThenBy(i => i))
            {
                if (set.Count >= count) break;
                if (set.Any(j => Math.Abs(j - i) < sep)) continue;
                set.Add(i);
            }

            return set.ToList();
        }

        public static StressResult Run(PartGeometry part, AnalysisSettings s, IReadOnlyList<LoadSample> loads, CancellationToken cancel, Action<double>? progress = null)
        {
            var mat = StructuralMaterial.For(part.MaterialName);
            var result = new StressResult { ObjectId = part.ObjectId, Name = part.Name, Material = mat.Name, Yield = mat.Yield, BodyOffset = part.BodyOffset };
            var instants = ChooseInstants(loads, s.StressInstants);
            if (instants.Count == 0)
            {
                result.Notes.Add("Bu parça için kayıtlı yük yok.");
                return result;
            }

            var b = part.Solid.Bounds.Size;
            var dims = new[] { b.X, b.Y, b.Z }.OrderByDescending(x => x).ToArray();
            bool slender = dims[1] > 0 && dims[0] / dims[1] >= 6 && (part.Shape == "box" || part.Shape == "cylinder");
            bool beam = s.StressMethod == "beam" ? part.Shape == "box" || part.Shape == "cylinder" : s.StressMethod == "auto" && slender;
            if (beam) return RunBeam(part, mat, result, loads, instants, cancel, progress);
            return RunTet(part, mat, s, result, loads, instants, cancel, progress);
        }

        private static FemLoadCase CaseOf(LoadSample ls, bool relief)
        {
            var lc = new FemLoadCase { Time = ls.Time, Gravity = ls.Gravity, Omega = ls.Omega, InertiaRelief = relief };
            foreach (var (p, f, _) in ls.Loads) lc.Loads.Add(new PointLoad(p, f));
            return lc;
        }

        private static StressResult RunTet(PartGeometry part, StructuralMaterial mat, AnalysisSettings s, StressResult result, IReadOnlyList<LoadSample> loads, List<int> instants,
            CancellationToken cancel, Action<double>? progress)
        {
            var size = part.Solid.Bounds.Size;
            double vol = Math.Max(1e-12, size.X * size.Y * size.Z);
            double h = s.StressElementSize > 0 ? s.StressElementSize : Math.Pow(vol / Math.Max(50, s.StressTargetCells), 1.0 / 3);
            var mesh = TetMesh.FromSolid(part.Solid, h, 2);
            if (mesh.Elements.Count == 0)
            {
                result.Notes.Add("Ağ oluşturulamadı (parça eleman boyutundan küçük).");
                return result;
            }

            result.Method = "tet";
            result.Order = mesh.Order;
            result.ElementSize = mesh.ElementSize;
            result.Nodes = new float[mesh.Nodes.Count * 3];
            for (int i = 0; i < mesh.Nodes.Count; i++)
            {
                result.Nodes[3 * i] = (float)mesh.Nodes[i].X;
                result.Nodes[3 * i + 1] = (float)mesh.Nodes[i].Y;
                result.Nodes[3 * i + 2] = (float)mesh.Nodes[i].Z;
            }

            result.Elements = mesh.Elements.SelectMany(e => e).ToArray();
            result.Surface = mesh.Surface.SelectMany(f => f).ToArray();
            result.Notes.Add($"{mesh.Elements.Count} ikinci derece dörtyüzlü (T10), {mesh.Nodes.Count} düğüm, eleman boyu {mesh.ElementSize * 1000:0.##} mm.");
            if (mesh.UnsnappedNodes > 0) result.Notes.Add($"{mesh.UnsnappedNodes} sınır düğümü eğri yüzeye taşınamadı (basamaklı kaldı).");
            // Relative CG residual by accuracy: thin parts are ill-conditioned, and 1e-6 already gives
            // stresses within a fraction of a percent of the 1e-8 reference.
            double tol = s.Accuracy switch { AccuracyPreset.Reference => 1e-8, AccuracyPreset.Fine => 1e-7, _ => 1e-6 };
            var model = new ElasticModel(mesh, mat) { Tolerance = tol };
            bool relief = true;
            if (part.IsStatic)
            {
                // A static part stands on its lowest face: hold the nodes there, no inertia relief.
                double minZ = mesh.Nodes.Min(n => (part.Position + part.Orientation.Rotate(n)).Z);
                model.FixNodes(n => (part.Position + part.Orientation.Rotate(n)).Z < minZ + 0.5 * mesh.ElementSize);
                relief = !model.HasUserSupports;
                if (!relief) result.Notes.Add("Sabit parça: en alt yüzeyi mesnetli kabul edildi.");
            }

            if (relief) result.Notes.Add("Serbest parça: atalet dengesi (inertia relief) ile çözüldü — yükleri dengeleyen rijit ivme gövde kuvveti olarak uygulandı.");
            model.Assemble(cancel);
            for (int k = 0; k < instants.Count; k++)
            {
                cancel.ThrowIfCancellationRequested();
                var ls = loads[instants[k]];
                var sol = model.Solve(CaseOf(ls, relief), cancel);
                var frame = new StressFrame
                {
                    Time = ls.Time,
                    MaxVonMises = sol.MaxVonMises,
                    MaxDisplacement = sol.MaxDisplacement,
                    MinSafetyFactor = sol.MinSafetyFactor,
                    ReactionRatio = sol.ReactionRatio,
                    TotalLoad = sol.TotalLoad,
                    Iterations = sol.Solver.Iterations,
                    LoadCount = ls.Loads.Count,
                    VonMises = sol.VonMises.Select(v => (float)v).ToArray(),
                    Displacement = sol.Displacement.Select(v => (float)v).ToArray()
                };
                result.Frames.Add(frame);
                progress?.Invoke((k + 1.0) / instants.Count);
            }

            return result;
        }

        private static StressResult RunBeam(PartGeometry part, StructuralMaterial mat, StressResult result, IReadOnlyList<LoadSample> loads, List<int> instants,
            CancellationToken cancel, Action<double>? progress)
        {
            var bb = part.Solid.Bounds;
            var c = bb.Center;
            var sz = bb.Size;
            Vec3 axis, up;
            var section = new BeamSection();
            double len;
            if (part.Shape == "cylinder" && sz.Z >= sz.X)
            {
                axis = Vec3.UnitZ;
                up = Vec3.UnitX;
                len = sz.Z;
                section.Circular = true;
                section.Diameter = Math.Min(sz.X, sz.Y);
            }
            else
            {
                var d = new[] { (sz.X, Vec3.UnitX), (sz.Y, Vec3.UnitY), (sz.Z, Vec3.UnitZ) }.OrderByDescending(x => x.Item1).ToArray();
                axis = d[0].Item2;
                len = d[0].Item1;
                up = d[2].Item2;   // thinnest direction = section height
                section.Width = d[1].Item1;
                section.Height = d[2].Item1;
                if (part.Shape == "cylinder")
                {
                    section.Circular = true;
                    section.Diameter = Math.Min(d[1].Item1, d[2].Item1);
                }
            }

            var start = c - axis * (len / 2);
            var end = c + axis * (len / 2);
            const int elements = 24;
            result.Method = "beam";
            result.BeamCircular = section.Circular;
            result.BeamUp = up;
            result.BeamWidth = section.Circular ? section.Diameter : section.Width;
            result.BeamHeight = section.Circular ? section.Diameter : section.Height;
            result.ElementSize = len / elements;
            var proto = new BeamModel(start, end, elements, section, mat, up);
            result.Nodes = new float[(elements + 1) * 3];
            for (int i = 0; i <= elements; i++)
            {
                var p = proto.NodePosition(i);
                result.Nodes[3 * i] = (float)p.X;
                result.Nodes[3 * i + 1] = (float)p.Y;
                result.Nodes[3 * i + 2] = (float)p.Z;
            }

            result.Elements = Enumerable.Range(0, elements).SelectMany(i => new[] { i, i + 1 }).ToArray();
            result.Notes.Add($"İnce uzun parça: {elements} elemanlı 3B Euler–Bernoulli kiriş ({(section.Circular ? "Ø" + (section.Diameter * 1000).ToString("0.#") + " mm daire" : (section.Width * 1000).ToString("0.#") + " × " + (section.Height * 1000).ToString("0.#") + " mm dikdörtgen")} kesit).");
            result.Notes.Add("Gerilme: eksenel + iki eksende eğilme + burulma, dış liflerde von Mises; kesme gerilmesi ihmal edildi.");
            for (int k = 0; k < instants.Count; k++)
            {
                cancel.ThrowIfCancellationRequested();
                var ls = loads[instants[k]];
                var beam = new BeamModel(start, end, elements, section, mat, up);
                var sol = beam.Solve(CaseOf(ls, true));
                var disp = new float[(elements + 1) * 3];
                for (int i = 0; i <= elements; i++)
                {
                    var u = sol.Displacement(i);
                    disp[3 * i] = (float)u.X;
                    disp[3 * i + 1] = (float)u.Y;
                    disp[3 * i + 2] = (float)u.Z;
                }

                result.Frames.Add(new StressFrame
                {
                    Time = ls.Time,
                    MaxVonMises = sol.MaxVonMises,
                    MaxDisplacement = sol.MaxDisplacement,
                    MinSafetyFactor = sol.MinSafetyFactor,
                    TotalLoad = ls.Magnitude,
                    LoadCount = ls.Loads.Count,
                    VonMises = sol.VonMises.Select(v => (float)v).ToArray(),
                    Displacement = disp
                });
                progress?.Invoke((k + 1.0) / instants.Count);
            }

            return result;
        }
    }
}
