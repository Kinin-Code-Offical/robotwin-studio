using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>Parametric solid kinds of the modeller.</summary>
    public enum PrimitiveKind
    {
        Box,
        Cylinder,
        Cone,
        Sphere,
        Torus,
        Tube,
        Wedge,

        /// <summary>Flat plate with rounded corners, corner holes and extra holes.</summary>
        Plate,

        /// <summary>A 2D sketch (outer shape + holes) extruded along Z.</summary>
        Extrude,

        /// <summary>A 2D sketch (X = radius, Y = height) revolved about Z.</summary>
        Revolve,

        /// <summary>A parametric robot part from <see cref="RobotParts"/>.</summary>
        Part,

        /// <summary>[loft] A solid that passes from the bottom profile (<see cref="PrimitiveSpec.Sketch"/>) to the top one (<see cref="PrimitiveSpec.Sketch2"/>).</summary>
        Loft,

        /// <summary>[sweep] The profile (<see cref="PrimitiveSpec.Sketch"/>) carried along a 3D path (<see cref="PrimitiveSpec.Path"/>).</summary>
        Sweep
    }

    /// <summary>Object placement relative to its parent: position (mm), Euler XYZ rotation (degrees), scale.</summary>
    public sealed class ModelTransform
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RX { get; set; }
        public double RY { get; set; }
        public double RZ { get; set; }
        public double SX { get; set; } = 1;
        public double SY { get; set; } = 1;
        public double SZ { get; set; } = 1;

        [JsonIgnore]
        public Vec3 Position
        {
            get => new Vec3(X, Y, Z);
            set
            {
                X = value.X;
                Y = value.Y;
                Z = value.Z;
            }
        }

        [JsonIgnore]
        public Vec3 Rotation
        {
            get => new Vec3(RX, RY, RZ);
            set
            {
                RX = value.X;
                RY = value.Y;
                RZ = value.Z;
            }
        }

        [JsonIgnore]
        public Vec3 Scale
        {
            get => new Vec3(SX, SY, SZ);
            set
            {
                SX = value.X;
                SY = value.Y;
                SZ = value.Z;
            }
        }

        [JsonIgnore]
        public bool IsIdentity => X == 0 && Y == 0 && Z == 0 && RX == 0 && RY == 0 && RZ == 0 && SX == 1 && SY == 1 && SZ == 1;

        public Affine ToAffine() => Affine.Trs(Position, Rotation, Scale);

        public ModelTransform Clone() => (ModelTransform)MemberwiseClone();
    }

    public enum SketchShapeKind
    {
        Rectangle,
        Circle,

        /// <summary>Regular polygon (hexagon for nut traps …).</summary>
        Polygon,

        /// <summary>Stadium / oblong slot.</summary>
        Slot,

        /// <summary>Free polygon from <see cref="SketchShape.Points"/>.</summary>
        Custom
    }

    /// <summary>A closed 2D shape of a sketch (millimetres, centred at X, Y).</summary>
    public sealed class SketchShape
    {
        public SketchShapeKind Kind { get; set; } = SketchShapeKind.Rectangle;
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Rectangle width / slot overall length.</summary>
        public double Width { get; set; } = 40;

        /// <summary>Rectangle height / slot width.</summary>
        public double Height { get; set; } = 20;

        /// <summary>Circle / polygon circumradius.</summary>
        public double Radius { get; set; } = 5;

        public double CornerRadius { get; set; }
        public int Sides { get; set; } = 6;

        /// <summary>Rotation about the centre (degrees).</summary>
        public double Rotation { get; set; }

        /// <summary>Custom outline as x0, y0, x1, y1 … (relative to X, Y).</summary>
        public List<double>? Points { get; set; }

        public SketchShape Clone()
        {
            var c = (SketchShape)MemberwiseClone();
            c.Points = Points == null ? null : new List<double>(Points);
            return c;
        }

        public static SketchShape Rect(double w, double h, double x = 0, double y = 0, double corner = 0) =>
            new SketchShape { Kind = SketchShapeKind.Rectangle, Width = w, Height = h, X = x, Y = y, CornerRadius = corner };

        public static SketchShape Circle(double diameter, double x = 0, double y = 0) =>
            new SketchShape { Kind = SketchShapeKind.Circle, Radius = diameter / 2, X = x, Y = y };

        public static SketchShape Slot(double length, double width, double x = 0, double y = 0, double rotation = 0) =>
            new SketchShape { Kind = SketchShapeKind.Slot, Width = length, Height = width, X = x, Y = y, Rotation = rotation };

        public static SketchShape Ngon(int sides, double radius, double x = 0, double y = 0, double rotation = 0) =>
            new SketchShape { Kind = SketchShapeKind.Polygon, Sides = sides, Radius = radius, X = x, Y = y, Rotation = rotation };

        /// <summary>Counter-clockwise outline; <paramref name="segments"/> = points per full circle.</summary>
        public List<Vec2> ToPolygon(int segments = 48)
        {
            segments = Math.Max(8, segments);
            var pts = new List<Vec2>();
            switch (Kind)
            {
                case SketchShapeKind.Circle:
                    for (int i = 0; i < segments; i++)
                    {
                        double a = 2 * Math.PI * i / segments;
                        pts.Add(new Vec2(Radius * Math.Cos(a), Radius * Math.Sin(a)));
                    }

                    break;
                case SketchShapeKind.Polygon:
                {
                    int n = Math.Max(3, Sides);
                    for (int i = 0; i < n; i++)
                    {
                        double a = 2 * Math.PI * i / n;
                        pts.Add(new Vec2(Radius * Math.Cos(a), Radius * Math.Sin(a)));
                    }

                    break;
                }

                case SketchShapeKind.Slot:
                {
                    double w = Math.Max(1e-6, Math.Min(Height, Width)), l = Math.Max(Width, Height);
                    double r = w / 2, c = l / 2 - r;
                    int half = Math.Max(4, segments / 2);
                    for (int i = 0; i <= half; i++)
                    {
                        double a = -Math.PI / 2 + Math.PI * i / half;
                        pts.Add(new Vec2(c + r * Math.Cos(a), r * Math.Sin(a)));
                    }

                    for (int i = 0; i <= half; i++)
                    {
                        double a = Math.PI / 2 + Math.PI * i / half;
                        pts.Add(new Vec2(-c + r * Math.Cos(a), r * Math.Sin(a)));
                    }

                    break;
                }

                case SketchShapeKind.Custom:
                    if (Points != null)
                    {
                        for (int i = 0; i + 1 < Points.Count; i += 2) pts.Add(new Vec2(Points[i], Points[i + 1]));
                    }

                    break;
                default:
                {
                    double hw = Width / 2, hh = Height / 2;
                    double r = Math.Max(0, Math.Min(CornerRadius, Math.Min(hw, hh)));
                    if (r <= 1e-9)
                    {
                        pts.Add(new Vec2(-hw, -hh));
                        pts.Add(new Vec2(hw, -hh));
                        pts.Add(new Vec2(hw, hh));
                        pts.Add(new Vec2(-hw, hh));
                    }
                    else
                    {
                        int q = Math.Max(2, segments / 4);
                        var centres = new[] { new Vec2(hw - r, -hh + r), new Vec2(hw - r, hh - r), new Vec2(-hw + r, hh - r), new Vec2(-hw + r, -hh + r) };
                        double[] start = { -Math.PI / 2, 0, Math.PI / 2, Math.PI };
                        for (int c = 0; c < 4; c++)
                        {
                            for (int i = 0; i <= q; i++)
                            {
                                double a = start[c] + Math.PI / 2 * i / q;
                                pts.Add(centres[c] + new Vec2(r * Math.Cos(a), r * Math.Sin(a)));
                            }
                        }
                    }

                    break;
                }
            }

            double rot = Rotation * Math.PI / 180;
            var origin = new Vec2(X, Y);
            var result = new List<Vec2>(pts.Count);
            foreach (var p in pts)
            {
                var q = (rot != 0 ? p.Rotated(rot) : p) + origin;
                if (result.Count > 0 && (q - result[result.Count - 1]).Length < 1e-9) continue;
                result.Add(q);
            }

            while (result.Count > 1 && (result[0] - result[result.Count - 1]).Length < 1e-9) result.RemoveAt(result.Count - 1);
            if (Triangulator.SignedArea(result) < 0) result.Reverse();
            return result;
        }
    }

    /// <summary>A 2D profile: one outer shape and any number of holes.</summary>
    public sealed class Sketch2D
    {
        public SketchShape Outer { get; set; } = SketchShape.Rect(40, 20);
        public List<SketchShape> Holes { get; set; } = new List<SketchShape>();

        /// <summary>[sketch] The dimensioned sketch this profile comes from, if it is drawn as one (its loops make Outer and Holes).</summary>
        public ConstrainedSketch? Constrained { get; set; }

        public Sketch2D Clone() => new Sketch2D { Outer = Outer.Clone(), Holes = Holes.Select(h => h.Clone()).ToList(), Constrained = Constrained?.Clone() };

        /// <summary>The dimensioned sketch's loops as this profile; false (and the profile as it was) when it has no closed loop.</summary>
        public bool ApplyConstrained()
        {
            if (Constrained?.ToProfile() is not { } profile) return false;
            Outer = profile.Outer;
            Holes = profile.Holes;
            return true;
        }
    }

    /// <summary>A round hole through a plate (centre and diameter in mm).</summary>
    public sealed class HoleSpec
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double D { get; set; } = 3.2;
        public HoleSpec Clone() => (HoleSpec)MemberwiseClone();
    }

    /// <summary>The parameters of a parametric object (all lengths in mm).</summary>
    public sealed class PrimitiveSpec
    {
        public PrimitiveKind Kind { get; set; }

        /// <summary>Template id for <see cref="PrimitiveKind.Part"/>.</summary>
        public string? Template { get; set; }

        public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);
        public Sketch2D? Sketch { get; set; }

        /// <summary>[loft] The top profile of a loft; null for every other kind.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public Sketch2D? Sketch2 { get; set; }

        /// <summary>[sweep] The path a sweep follows: x, y, z (mm) of each point in turn; null for every other kind.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<double>? Path { get; set; }

        public List<HoleSpec>? Holes { get; set; }

        /// <summary>[param] Dimensions given by a formula of the model's parameters (key → formula); null when none.</summary>
        public Dictionary<string, string>? Expressions { get; set; }

        /// <summary>Value of a parameter, or its schema default.</summary>
        public double Get(string key)
        {
            if (Values.TryGetValue(key, out double v)) return v;
            var spec = PrimitiveSchema.For(this).FirstOrDefault(p => p.Key == key);
            return spec?.Default ?? 0;
        }

        public void Set(string key, double value) => Values[key] = value;

        public PrimitiveSpec Clone() => new PrimitiveSpec
        {
            Kind = Kind,
            Template = Template,
            Values = new Dictionary<string, double>(Values, StringComparer.Ordinal),
            Sketch = Sketch?.Clone(),
            Sketch2 = Sketch2?.Clone(),
            Path = Path == null ? null : new List<double>(Path),
            Holes = Holes?.Select(h => h.Clone()).ToList(),
            Expressions = Expressions == null ? null : new Dictionary<string, string>(Expressions, StringComparer.Ordinal)
        };

        /// <summary>Stable text key of every parameter (cache key for the generated mesh).</summary>
        public string CacheKey() => JsonSerializer.Serialize(this, RobotWinFormats.Json);
    }

    /// <summary>Compact polygon mesh storage: deflated little-endian doubles / ints in base64.</summary>
    public sealed class MeshData
    {
        public int VertexCount { get; set; }
        public int FaceCount { get; set; }

        /// <summary>x, y, z doubles (mm).</summary>
        public string Positions { get; set; } = string.Empty;

        /// <summary>For each face: corner count, then the corner indices (int32).</summary>
        public string Faces { get; set; } = string.Empty;

        public static MeshData From(PolyMesh mesh)
        {
            var pos = new double[mesh.Vertices.Count * 3];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                pos[3 * i] = mesh.Vertices[i].X;
                pos[3 * i + 1] = mesh.Vertices[i].Y;
                pos[3 * i + 2] = mesh.Vertices[i].Z;
            }

            var faces = new List<int>(mesh.Faces.Count * 5);
            foreach (var f in mesh.Faces)
            {
                faces.Add(f.Length);
                faces.AddRange(f);
            }

            var pb = new byte[pos.Length * 8];
            Buffer.BlockCopy(pos, 0, pb, 0, pb.Length);
            var fb = new byte[faces.Count * 4];
            Buffer.BlockCopy(faces.ToArray(), 0, fb, 0, fb.Length);
            return new MeshData { VertexCount = mesh.Vertices.Count, FaceCount = mesh.Faces.Count, Positions = Pack(pb), Faces = Pack(fb) };
        }

        public PolyMesh ToPolyMesh()
        {
            var m = new PolyMesh();
            var pb = Unpack(Positions);
            var fb = Unpack(Faces);
            if (pb.Length != VertexCount * 24) throw new InvalidDataException("Model ağı bozuk (köşe verisi).");
            var pos = new double[VertexCount * 3];
            Buffer.BlockCopy(pb, 0, pos, 0, pb.Length);
            for (int i = 0; i < VertexCount; i++) m.Vertices.Add(new Vec3(pos[3 * i], pos[3 * i + 1], pos[3 * i + 2]));
            var ints = new int[fb.Length / 4];
            Buffer.BlockCopy(fb, 0, ints, 0, ints.Length * 4);
            int k = 0;
            for (int f = 0; f < FaceCount; f++)
            {
                if (k >= ints.Length) throw new InvalidDataException("Model ağı bozuk (yüz verisi).");
                int n = ints[k++];
                if (n < 3 || k + n > ints.Length) throw new InvalidDataException("Model ağı bozuk (yüz verisi).");
                var face = new int[n];
                for (int j = 0; j < n; j++)
                {
                    int idx = ints[k++];
                    if (idx < 0 || idx >= VertexCount) throw new InvalidDataException("Model ağı bozuk (köşe indeksi).");
                    face[j] = idx;
                }

                m.Faces.Add(face);
            }

            return m;
        }

        private static string Pack(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
            return Convert.ToBase64String(ms.ToArray());
        }

        private static byte[] Unpack(string text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
            using var input = new MemoryStream(Convert.FromBase64String(text));
            using var z = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return output.ToArray();
        }
    }

    /// <summary>One object of a model: a parametric primitive or an editable mesh.</summary>
    public sealed class ModelObject
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>Parent object (its transform applies on top of ours); null = document root.</summary>
        public string? ParentId { get; set; }

        public bool Visible { get; set; } = true;
        public ModelTransform Transform { get; set; } = new ModelTransform();

        /// <summary>Material name (PLA, PETG, ABS, ALÜMİNYUM …), see <see cref="ModelMaterials"/>.</summary>
        public string Material { get; set; } = "PLA";

        /// <summary>Infill fraction 0..1 for printed materials.</summary>
        public double Infill { get; set; } = 0.3;

        /// <summary>Display colour "#RRGGBB".</summary>
        public string Color { get; set; } = "#B9BFC9";

        public bool SmoothShading { get; set; }

        /// <summary>Edges sharper than this stay hard when smooth shading (degrees).</summary>
        public double AutoSmoothAngle { get; set; } = 35;

        public PrimitiveSpec? Primitive { get; set; }
        public MeshData? Mesh { get; set; }

        /// <summary>[refgeom] A reference plane, axis or point (Modeling/ModelReferences.cs): no solid, drawn as an outline; null for a part.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public ModelReference? Reference { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsReference => Reference != null;

        /// <summary>[history] The features after the base, applied in order (Modeling/ModelFeatures.cs); null when none.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public List<ModelFeature>? Features { get; set; }

        [JsonIgnore]
        public bool IsPrimitive => Primitive != null;

        [JsonIgnore]
        private PolyMesh? _cache;

        [JsonIgnore]
        private string? _cacheKey;

        /// <summary>The object's geometry in its own coordinate frame (a shared, cached instance: do not modify).</summary>
        public PolyMesh LocalMesh()
        {
            string? key = Primitive != null ? Primitive.CacheKey() : Mesh != null ? "mesh" : null;
            if (key == null) return new PolyMesh();
            if (Features is { Count: > 0 }) key += "|" + ModelFeatures.Stamp(Features);   // [history] the features rebuild with their base
            if (_cache != null && _cacheKey == key) return _cache;
            var mesh = Primitive != null ? PrimitiveBuilder.Build(Primitive) : Mesh!.ToPolyMesh();
            if (Features is { Count: > 0 }) mesh = ModelFeatures.Apply(mesh, Features);
            _cache = mesh;
            _cacheKey = key;
            return mesh;
        }

        /// <summary>Replaces the geometry with an editable mesh (drops the primitive parameters).</summary>
        public void SetMesh(PolyMesh mesh)
        {
            Primitive = null;
            Features = null;   // [history] the mesh is the whole part now
            Mesh = MeshData.From(mesh);
            _cache = mesh.Clone();
            _cacheKey = "mesh";
        }

        /// <summary>Call after changing <see cref="Mesh"/> or <see cref="Primitive"/> directly.</summary>
        public void Invalidate()
        {
            _cache = null;
            _cacheKey = null;
        }

        public ModelObject Clone()
        {
            var c = (ModelObject)MemberwiseClone();
            c.Transform = Transform.Clone();
            c.Primitive = Primitive?.Clone();
            c.Reference = Reference?.Clone();
            c.Features = Features?.Select(f => f.Clone()).ToList();
            c.Mesh = Mesh == null ? null : new MeshData { VertexCount = Mesh.VertexCount, FaceCount = Mesh.FaceCount, Positions = Mesh.Positions, Faces = Mesh.Faces };
            // the cache is immutable by convention, sharing it is safe
            return c;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// A 3D model being built in the modeller: a set of objects (primitives and meshes)
    /// in millimetres, saved as a <c>.rwmodel</c> package.
    /// </summary>
    public sealed class ModelDocument
    {
        public const string Format = "robotwin.model";
        public const int FormatVersion = 1;
        public const string Extension = ".rwmodel";

        public string Title { get; set; } = "Yeni Model";
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// [assembly] Stable id of this document: a part sent to Montaj remembers it, so sending it again updates that
        /// part. Empty in files saved before it existed; see <see cref="EnsureUid"/>.
        /// </summary>
        public string Uid { get; set; } = string.Empty;

        /// <summary>
        /// Gives the document an id when it has none: one derived from its file path for an older file (so it is the
        /// same every time that file is opened, and its parts already in a Montaj are found again), a new one otherwise.
        /// </summary>
        public string EnsureUid(string? path)
        {
            if (!string.IsNullOrEmpty(Uid)) return Uid;
            if (string.IsNullOrEmpty(path))
            {
                Uid = Guid.NewGuid().ToString("N");
                return Uid;
            }

            using var sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
            Uid = "f" + BitConverter.ToString(hash, 0, 15).Replace("-", string.Empty).ToLowerInvariant();
            return Uid;
        }

        /// <summary>Length unit of every coordinate (always "mm").</summary>
        public string Units { get; set; } = "mm";

        /// <summary>Snap increment (mm).</summary>
        public double GridMm { get; set; } = 1;

        public List<ModelObject> Objects { get; set; } = new List<ModelObject>();

        /// <summary>[param] Named values and formulas the dimensions can use (Modeling/ModelParameters.cs); null when none.</summary>
        public List<ModelParameter>? Parameters { get; set; }

        [JsonIgnore]
        public byte[]? Preview { get; set; }

        public ModelObject? Find(string? id) => id == null ? null : Objects.FirstOrDefault(o => o.Id == id);

        public IEnumerable<ModelObject> Children(string id) => Objects.Where(o => o.ParentId == id);

        /// <summary>The object and everything parented under it.</summary>
        public List<ModelObject> WithDescendants(IEnumerable<ModelObject> roots)
        {
            var result = new List<ModelObject>();
            var seen = new HashSet<string>();
            var queue = new Queue<ModelObject>(roots);
            while (queue.Count > 0)
            {
                var o = queue.Dequeue();
                if (!seen.Add(o.Id)) continue;
                result.Add(o);
                foreach (var c in Children(o.Id)) queue.Enqueue(c);
            }

            return result;
        }

        public string NextId()
        {
            int max = 0;
            foreach (var o in Objects)
            {
                if (o.Id.StartsWith("M", StringComparison.Ordinal) && int.TryParse(o.Id.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) max = Math.Max(max, n);
            }

            return "M" + (max + 1).ToString(CultureInfo.InvariantCulture);
        }

        public string UniqueName(string baseName)
        {
            if (Objects.All(o => !string.Equals(o.Name, baseName, StringComparison.OrdinalIgnoreCase))) return baseName;
            for (int i = 2; ; i++)
            {
                string n = baseName + "." + i.ToString("000", CultureInfo.InvariantCulture);
                if (Objects.All(o => !string.Equals(o.Name, n, StringComparison.OrdinalIgnoreCase))) return n;
            }
        }

        /// <summary>Adds an object with a fresh id and unique name.</summary>
        public ModelObject Add(ModelObject o)
        {
            o.Id = NextId();
            o.Name = UniqueName(string.IsNullOrWhiteSpace(o.Name) ? "Nesne" : o.Name);
            Objects.Add(o);
            return o;
        }

        /// <summary>Local → document (world) transform, including parents.</summary>
        public Affine WorldMatrix(ModelObject o)
        {
            var m = o.Transform.ToAffine();
            var seen = new HashSet<string> { o.Id };
            var p = Find(o.ParentId);
            while (p != null && seen.Add(p.Id))
            {
                m = p.Transform.ToAffine() * m;
                p = Find(p.ParentId);
            }

            return m;
        }

        /// <summary>Parent chain transform (identity for root objects).</summary>
        public Affine ParentMatrix(ModelObject o)
        {
            var p = Find(o.ParentId);
            return p == null ? Affine.Identity : WorldMatrix(p);
        }

        /// <summary>The object's geometry in document coordinates (a new mesh).</summary>
        public PolyMesh WorldMesh(ModelObject o) => o.LocalMesh().Transformed(WorldMatrix(o));

        /// <summary>Removes objects; their children move up to the removed object's parent, keeping their placement.</summary>
        public void Remove(ModelObject o)
        {
            foreach (var c in Children(o.Id).ToList())
            {
                var world = WorldMatrix(c);
                c.ParentId = o.ParentId;
                ModelOps.SetWorldMatrix(this, c, world);
            }

            Objects.Remove(o);
        }

        // ------------------------------------------------------------------ files

        public string ToJson() => JsonSerializer.Serialize(this, RobotWinFormats.Json);

        public static ModelDocument FromJson(string json)
        {
            var doc = JsonSerializer.Deserialize<ModelDocument>(json, RobotWinFormats.Json) ?? throw new InvalidDataException("Model verisi okunamadı.");
            doc.Objects ??= new List<ModelObject>();
            foreach (var o in doc.Objects)
            {
                o.Transform ??= new ModelTransform();
                if (o.Primitive != null) o.Primitive.Values ??= new Dictionary<string, double>(StringComparer.Ordinal);
            }

            return doc;
        }

        public ModelDocument Clone()
        {
            var c = FromJson(ToJson());
            c.Preview = Preview;
            return c;
        }

        public void Save(string path)
        {
            var info = new Manifest { Format = Format, FormatVersion = FormatVersion, Title = Title, Description = Description };
            info.Summary["objects"] = Objects.Count.ToString(CultureInfo.InvariantCulture);
            info.Summary["units"] = Units;
            RobotWinFormats.SaveAtomically(path, zip =>
            {
                RobotWinFormats.WriteText(zip, "manifest.json", JsonSerializer.Serialize(info, RobotWinFormats.Json));
                RobotWinFormats.WriteText(zip, "model.json", ToJson());
                if (Preview != null) RobotWinFormats.WriteBytes(zip, "preview.png", Preview);
            });
        }

        public static ModelDocument Load(string path)
        {
            using var zip = ZipFile.OpenRead(path);
            var info = RobotWinFormats.ReadManifest(zip, Format, FormatVersion);
            var doc = FromJson(RobotWinFormats.ReadText(zip, "model.json") ?? throw new InvalidDataException("Model dosyasında model.json yok."));
            if (string.IsNullOrWhiteSpace(doc.Title)) doc.Title = info.Title;
            doc.Preview = RobotWinFormats.ReadBytes(zip, "preview.png");
            return doc;
        }
    }
}
