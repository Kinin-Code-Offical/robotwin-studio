using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[history] What a feature does to the part it belongs to.</summary>
    public enum FeatureKind
    {
        /// <summary>Takes the tool body's volume away (SolidWorks' cut).</summary>
        Cut,

        /// <summary>Adds the tool body's volume (a boss, merged).</summary>
        Join,

        /// <summary>Keeps only what the part and the tool body share.</summary>
        Intersect,

        /// <summary>A hole of the hole wizard's kind, drilled at a point along a direction.</summary>
        Hole
    }

    /// <summary>
    /// [history] One step of a part's feature history: a tool body (a primitive with its parameters, or a mesh) placed in
    /// the part's own frame and cut from, joined to or intersected with it, or a hole wizard hole. It stays editable: its
    /// size and place can change, it can be suppressed, moved up or down the list, or deleted, and the part is rebuilt.
    /// </summary>
    public sealed class ModelFeature
    {
        public string Name { get; set; } = "";
        public FeatureKind Kind { get; set; }

        /// <summary>Left out of the part while set (SolidWorks' Suppress).</summary>
        public bool Suppressed { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PrimitiveSpec? Primitive { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MeshData? Mesh { get; set; }

        /// <summary>The tool's (or the hole's) placement in the part's frame: M00 … M22 then the translation, row by row.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double[]? Matrix { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DrillSpec? Drill { get; set; }

        [JsonIgnore]
        public Affine Placement
        {
            get => Matrix is { Length: 12 } m ? new Affine(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11]) : Affine.Identity;
            set => Matrix = new[] { value.M00, value.M01, value.M02, value.M10, value.M11, value.M12, value.M20, value.M21, value.M22, value.Tx, value.Ty, value.Tz };
        }

        public ModelFeature Clone() => new ModelFeature
        {
            Name = Name,
            Kind = Kind,
            Suppressed = Suppressed,
            Primitive = Primitive?.Clone(),
            Mesh = Mesh == null ? null : new MeshData { VertexCount = Mesh.VertexCount, FaceCount = Mesh.FaceCount, Positions = Mesh.Positions, Faces = Mesh.Faces },
            Matrix = (double[]?)Matrix?.Clone(),
            Drill = Drill == null ? null : new DrillSpec { Type = Drill.Type, Size = Drill.Size, Fit = Drill.Fit, Depth = Drill.Depth }
        };
    }

    /// <summary>
    /// [history] SolidWorks' editable feature history for a part: its base (a primitive with its parameters, or a mesh)
    /// and the features after it, applied in order every time the part is rebuilt. A boolean or a hole made in the
    /// modeller becomes a feature instead of being baked into a mesh, so its tool keeps its parameters.
    /// </summary>
    public static class ModelFeatures
    {
        /// <summary>The part with its features applied in order (the suppressed ones left out).</summary>
        public static PolyMesh Apply(PolyMesh mesh, IReadOnlyList<ModelFeature> features)
        {
            foreach (var f in features)
            {
                if (f.Suppressed) continue;
                if (f.Kind == FeatureKind.Hole)
                {
                    mesh = Drill(mesh, f);
                    continue;
                }

                var tool = ToolMesh(f);
                if (tool.IsEmpty) continue;
                var op = f.Kind == FeatureKind.Join ? BooleanOp.Union : f.Kind == FeatureKind.Intersect ? BooleanOp.Intersection : BooleanOp.Difference;
                mesh = Csg.Apply(mesh, tool, op);
            }

            return mesh;
        }

        /// <summary>A tool body where it sits in the part's frame.</summary>
        public static PolyMesh ToolMesh(ModelFeature f)
        {
            var m = f.Primitive != null ? PrimitiveBuilder.Build(f.Primitive).Clone() : f.Mesh?.ToPolyMesh() ?? new PolyMesh();
            return m.Transform(f.Placement);
        }

        private static PolyMesh Drill(PolyMesh mesh, ModelFeature f)
        {
            if (f.Drill == null) return mesh;
            var frame = f.Placement;
            var point = frame.Apply(Vec3.Zero);
            var n = frame.ApplyVector(Vec3.UnitZ).Normalized();
            double through = mesh.Vertices.Select(v => (point - v).Dot(n)).DefaultIfEmpty(0).Max() + 1;
            foreach (var tool in HoleWizard.Tools(f.Drill, through))
                mesh = Csg.Apply(mesh, tool.Transform(frame), BooleanOp.Difference);
            return mesh;
        }

        /// <summary>A short text that changes whenever anything that shapes the features does (the part's cache key).</summary>
        public static string Stamp(IReadOnlyList<ModelFeature> features)
        {
            var sb = new StringBuilder();
            foreach (var f in features)
            {
                sb.Append((int)f.Kind).Append(f.Suppressed ? 's' : 'a').Append(':');
                if (f.Primitive != null) sb.Append(f.Primitive.CacheKey());
                if (f.Mesh != null) sb.Append('m').Append(f.Mesh.VertexCount).Append(',').Append(f.Mesh.FaceCount).Append(',').Append(f.Mesh.Positions.Length);
                if (f.Drill != null) sb.Append('d').Append(f.Drill.Type).Append(f.Drill.Size).Append(f.Drill.Fit).Append(f.Drill.Depth.ToString("R", CultureInfo.InvariantCulture));
                if (f.Matrix != null)
                    foreach (var v in f.Matrix) sb.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
                sb.Append(';');
            }

            return sb.ToString();
        }

        private static string OpName(BooleanOp op) => op == BooleanOp.Union ? "Birleşim" : op == BooleanOp.Intersection ? "Kesişim" : "Fark";

        /// <summary>
        /// A boolean as features: each tool becomes one, placed in the target's frame (its primitive and parameters kept),
        /// and leaves the model. The target keeps its own parameters too.
        /// </summary>
        public static List<ModelFeature> AddBoolean(ModelDocument doc, ModelObject target, IEnumerable<ModelObject> tools, BooleanOp op)
        {
            var inverse = doc.WorldMatrix(target).Inverse();
            var made = new List<ModelFeature>();
            foreach (var t in tools.Where(t => t != target && !t.IsReference).ToList())
            {
                bool plain = t.Primitive != null && t.Features == null;
                var f = new ModelFeature
                {
                    Name = OpName(op) + ": " + t.Name,
                    Kind = op == BooleanOp.Union ? FeatureKind.Join : op == BooleanOp.Intersection ? FeatureKind.Intersect : FeatureKind.Cut,
                    Primitive = plain ? t.Primitive!.Clone() : null,
                    Mesh = plain ? null : MeshData.From(t.LocalMesh()),
                    Placement = inverse * doc.WorldMatrix(t)
                };
                (target.Features ??= new List<ModelFeature>()).Add(f);
                made.Add(f);
                doc.Remove(t);
            }

            target.Invalidate();
            return made;
        }

        /// <summary>A hole wizard hole as a feature, at a point of the model along the surface's outward normal there.</summary>
        public static ModelFeature AddHole(ModelDocument doc, ModelObject target, Vec3 worldPoint, Vec3 worldNormal, DrillSpec spec)
        {
            var inverse = doc.WorldMatrix(target).Inverse();
            var f = new ModelFeature
            {
                Name = "Delik: " + HoleWizard.Describe(spec),
                Kind = FeatureKind.Hole,
                Drill = new DrillSpec { Type = spec.Type, Size = spec.Size, Fit = spec.Fit, Depth = spec.Depth },
                Placement = HoleWizard.Frame(inverse.Apply(worldPoint), inverse.ApplyVector(worldNormal).Normalized())
            };
            (target.Features ??= new List<ModelFeature>()).Add(f);
            target.Invalidate();
            return f;
        }

        /// <summary>Moves a feature one step up or down the history.</summary>
        public static bool Move(ModelObject o, int index, int by)
        {
            if (o.Features == null || index < 0 || index >= o.Features.Count) return false;
            int to = index + by;
            if (to < 0 || to >= o.Features.Count) return false;
            var f = o.Features[index];
            o.Features.RemoveAt(index);
            o.Features.Insert(to, f);
            o.Invalidate();
            return true;
        }

        public static void Remove(ModelObject o, int index)
        {
            if (o.Features == null || index < 0 || index >= o.Features.Count) return;
            o.Features.RemoveAt(index);
            if (o.Features.Count == 0) o.Features = null;
            o.Invalidate();
        }

        /// <summary>Gives back a feature's tool as a model object of its own where it is (SolidWorks has no such step; it undoes a boolean).</summary>
        public static ModelObject? Extract(ModelDocument doc, ModelObject o, int index)
        {
            if (o.Features == null || index < 0 || index >= o.Features.Count) return null;
            var f = o.Features[index];
            if (f.Kind == FeatureKind.Hole) return null;
            var t = new ModelObject { Name = f.Name.Contains(": ") ? f.Name.Substring(f.Name.IndexOf(": ", StringComparison.Ordinal) + 2) : f.Name };
            if (f.Primitive != null) t.Primitive = f.Primitive.Clone();
            else t.SetMesh(f.Mesh?.ToPolyMesh() ?? new PolyMesh());
            doc.Add(t);
            ModelOps.SetWorldMatrix(doc, t, doc.WorldMatrix(o) * f.Placement);
            Remove(o, index);
            return t;
        }
    }
}
