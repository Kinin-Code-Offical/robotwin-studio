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
        Hole,

        /// <summary>
        /// [rib] SolidWorks' rib: a wall of a thickness standing on a line in a plane through the part, filled from the
        /// line to the part (a gusset in a bracket's corner).
        /// </summary>
        Rib
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

        /// <summary>[rib] The rib's line and plane in the part's frame: start x, y, z, end x, y, z, the plane's normal x, y, z.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double[]? Rib { get; set; }

        /// <summary>[rib] The rib's thickness (mm), half each side of its plane.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double Thickness { get; set; }

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
            Drill = Drill == null ? null : new DrillSpec { Type = Drill.Type, Size = Drill.Size, Fit = Drill.Fit, Depth = Drill.Depth },
            Rib = (double[]?)Rib?.Clone(),
            Thickness = Thickness
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

                if (f.Kind == FeatureKind.Rib)
                {
                    mesh = AddRib(mesh, f);
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

        /// <summary>
        /// [rib] The rib: a slab of its thickness on its plane, from its line (run on past its ends) towards the side of
        /// the part that is nearer it, out to the part's bounds (kept a hair inside, so no face of it lies on one of the
        /// part's), joined to the part. In a bracket's corner it fills the triangle between the line and the walls.
        /// </summary>
        private static PolyMesh AddRib(PolyMesh mesh, ModelFeature f)
        {
            if (f.Rib is not { Length: 9 } r || f.Thickness <= 0 || mesh.IsEmpty) return mesh;
            var a = new Vec3(r[0], r[1], r[2]);
            var b = new Vec3(r[3], r[4], r[5]);
            var n = new Vec3(r[6], r[7], r[8]).Normalized();
            var d = b - a;
            if (d.Length < 1e-9 || n.Length < 0.5) return mesh;
            d = d.Normalized();
            var across = n.Cross(d).Normalized();   // in the plane, square to the line
            var mid = (a + b) * 0.5;
            double toPlus = Hit(mesh, mid, across), toMinus = Hit(mesh, mid, across * -1);
            var side = toMinus < toPlus ? across * -1 : across;   // towards the part
            var bounds = mesh.Bounds;
            double reach = (bounds.Max - bounds.Min).Length * 2 + (b - a).Length;
            var p0 = a - d * reach;
            var p1 = b + d * reach;
            var p2 = p1 + side * reach;
            var p3 = p0 + side * reach;
            var half = n * (f.Thickness / 2);
            var slab = new PolyMesh();
            var lo = new[] { p0 - half, p1 - half, p2 - half, p3 - half }.Select(slab.AddVertex).ToArray();
            var hi = new[] { p0 + half, p1 + half, p2 + half, p3 + half }.Select(slab.AddVertex).ToArray();
            slab.AddFace(lo[3], lo[2], lo[1], lo[0]);
            slab.AddFace(hi[0], hi[1], hi[2], hi[3]);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                slab.AddFace(lo[i], lo[j], hi[j], hi[i]);
            }

            if (slab.Volume() < 0) slab.FlipAll();
            const double inside = 1e-3;
            var size = bounds.Max - bounds.Min;
            var box = PrimitiveBuilder.Box(Math.Max(size.X - 2 * inside, 1e-3), Math.Max(size.Y - 2 * inside, 1e-3), Math.Max(size.Z - 2 * inside, 1e-3))
                .Translate((bounds.Min + bounds.Max) * 0.5);
            var rib = Csg.Apply(slab, box, BooleanOp.Intersection);
            return rib.IsEmpty ? mesh : Csg.Apply(mesh, rib, BooleanOp.Union);
        }

        /// <summary>The distance along a ray to the first face of the mesh it meets (infinity when none).</summary>
        private static double Hit(PolyMesh m, Vec3 origin, Vec3 dir)
        {
            double best = double.PositiveInfinity;
            foreach (var face in m.Faces)
            {
                for (int k = 1; k + 1 < face.Length; k++)
                {
                    var v0 = m.Vertices[face[0]];
                    var e1 = m.Vertices[face[k]] - v0;
                    var e2 = m.Vertices[face[k + 1]] - v0;
                    var pv = dir.Cross(e2);
                    double det = e1.Dot(pv);
                    if (Math.Abs(det) < 1e-12) continue;
                    var tv = origin - v0;
                    double u = tv.Dot(pv) / det;
                    if (u < 0 || u > 1) continue;
                    var qv = tv.Cross(e1);
                    double v = dir.Dot(qv) / det;
                    if (v < 0 || u + v > 1) continue;
                    double t = e2.Dot(qv) / det;
                    if (t > 1e-9 && t < best) best = t;
                }
            }

            return best;
        }

        /// <summary>
        /// [rib] A rib as a feature: its line from <paramref name="start"/> to <paramref name="end"/> in the plane square to
        /// <paramref name="normal"/> (all in the part's own frame), <paramref name="thickness"/> mm thick.
        /// </summary>
        public static ModelFeature AddRib(ModelObject target, Vec3 start, Vec3 end, Vec3 normal, double thickness)
        {
            var f = new ModelFeature
            {
                Name = "Kaburga",
                Kind = FeatureKind.Rib,
                Rib = new[] { start.X, start.Y, start.Z, end.X, end.Y, end.Z, normal.X, normal.Y, normal.Z },
                Thickness = thickness
            };
            (target.Features ??= new List<ModelFeature>()).Add(f);
            target.Invalidate();
            return f;
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
                if (f.Rib != null)
                {
                    sb.Append('r').Append(f.Thickness.ToString("R", CultureInfo.InvariantCulture));
                    foreach (var v in f.Rib) sb.Append(',').Append(v.ToString("R", CultureInfo.InvariantCulture));
                }
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
            if (f.Kind is FeatureKind.Hole or FeatureKind.Rib) return null;   // no tool body to give back
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
