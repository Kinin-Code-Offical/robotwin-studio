using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>Length unit of the numbers stored in a model file.</summary>
    public enum MeshUnits
    {
        Millimeter,
        Centimeter,
        Meter,
        Inch
    }

    /// <summary>Which axis points up in the source file. Models are always stored Z-up.</summary>
    public enum MeshUpAxis
    {
        /// <summary>Z up (CAD, STL from slicers): no change.</summary>
        Z,

        /// <summary>Y up (Blender/glTF/many OBJ exports): rotated +90° about X so +Y becomes +Z.</summary>
        Y
    }

    public enum MeshFileFormat
    {
        Auto,
        Stl,
        Obj,
        Ply
    }

    public static class MeshUnitsExtensions
    {
        public static double ToMetres(this MeshUnits u)
        {
            switch (u)
            {
                case MeshUnits.Millimeter: return 0.001;
                case MeshUnits.Centimeter: return 0.01;
                case MeshUnits.Inch: return 0.0254;
                default: return 1.0;
            }
        }
    }

    /// <summary>8-bit RGBA colour.</summary>
    public readonly struct MeshColor : IEquatable<MeshColor>
    {
        public MeshColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
        public byte A { get; }

        public static readonly MeshColor DefaultGrey = new MeshColor(180, 180, 180);

        public static MeshColor FromFloat(double r, double g, double b, double a = 1.0) =>
            new MeshColor(ToByte(r), ToByte(g), ToByte(b), ToByte(a));

        private static byte ToByte(double v) => (byte)Math.Round(Math.Max(0, Math.Min(1, v)) * 255);

        /// <summary>Packed 0xAARRGGBB.</summary>
        public uint ToArgb() => (uint)A << 24 | (uint)R << 16 | (uint)G << 8 | B;

        public static MeshColor FromArgb(uint argb) => new MeshColor((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

        /// <summary>"#RRGGBB" or "#RRGGBBAA".</summary>
        public string ToHex() => A == 255 ? $"#{R:X2}{G:X2}{B:X2}" : $"#{R:X2}{G:X2}{B:X2}{A:X2}";

        public static MeshColor FromHex(string hex)
        {
            var s = hex.TrimStart('#');
            byte P(int i) => Convert.ToByte(s.Substring(i, 2), 16);
            return s.Length >= 8 ? new MeshColor(P(0), P(2), P(4), P(6)) : new MeshColor(P(0), P(2), P(4));
        }

        public bool Equals(MeshColor o) => R == o.R && G == o.G && B == o.B && A == o.A;
        public override bool Equals(object? obj) => obj is MeshColor c && Equals(c);
        public override int GetHashCode() => (int)ToArgb();
        public override string ToString() => ToHex();
    }

    /// <summary>A named part of a model (OBJ object/group/material), with an optional colour.</summary>
    public sealed class MeshGroup
    {
        public string Name { get; set; } = string.Empty;
        public string? Material { get; set; }
        public MeshColor? Color { get; set; }
        public override string ToString() => Material == null ? Name : $"{Name} [{Material}]";
    }

    /// <summary>Axis-aligned bounding box.</summary>
    public readonly struct Aabb
    {
        public Aabb(Vec3 min, Vec3 max)
        {
            Min = min;
            Max = max;
        }

        public Vec3 Min { get; }
        public Vec3 Max { get; }

        public static readonly Aabb Empty = new Aabb(
            new Vec3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
            new Vec3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

        public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
        public Vec3 Center => (Min + Max) * 0.5;
        public Vec3 Size => IsEmpty ? Vec3.Zero : Max - Min;

        public Aabb Include(Vec3 p) => new Aabb(
            new Vec3(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)),
            new Vec3(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));

        public Aabb Union(Aabb b) => b.IsEmpty ? this : IsEmpty ? b : Include(b.Min).Include(b.Max);

        public Aabb Expanded(double margin) =>
            new Aabb(Min - new Vec3(margin, margin, margin), Max + new Vec3(margin, margin, margin));

        public bool Contains(Vec3 p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

        public bool Intersects(Aabb b) =>
            Min.X <= b.Max.X && Max.X >= b.Min.X && Min.Y <= b.Max.Y && Max.Y >= b.Min.Y && Min.Z <= b.Max.Z && Max.Z >= b.Min.Z;

        public static Aabb FromPoints(IEnumerable<Vec3> points)
        {
            double x0 = double.PositiveInfinity, y0 = x0, z0 = x0, x1 = double.NegativeInfinity, y1 = x1, z1 = x1;
            foreach (var p in points)
            {
                if (p.X < x0) x0 = p.X;
                if (p.Y < y0) y0 = p.Y;
                if (p.Z < z0) z0 = p.Z;
                if (p.X > x1) x1 = p.X;
                if (p.Y > y1) y1 = p.Y;
                if (p.Z > z1) z1 = p.Z;
            }

            return new Aabb(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
        }

        /// <summary>Slab test. Returns the entry distance (0 when the origin is inside) or −1 on a miss.</summary>
        public double RayIntersect(Vec3 origin, Vec3 direction, double maxDistance = double.PositiveInfinity)
        {
            double tmin = 0, tmax = maxDistance;
            if (!Slab(origin.X, direction.X, Min.X, Max.X, ref tmin, ref tmax)) return -1;
            if (!Slab(origin.Y, direction.Y, Min.Y, Max.Y, ref tmin, ref tmax)) return -1;
            if (!Slab(origin.Z, direction.Z, Min.Z, Max.Z, ref tmin, ref tmax)) return -1;
            return tmin;
        }

        private static bool Slab(double o, double d, double lo, double hi, ref double tmin, ref double tmax)
        {
            if (Math.Abs(d) < 1e-300) return o >= lo && o <= hi;
            double t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            return tmin <= tmax;
        }

        public override string ToString() => $"[{Min} .. {Max}]";
    }

    /// <summary>Edge-based closedness report of a triangle mesh.</summary>
    public sealed class MeshTopology
    {
        /// <summary>Distinct vertex positions (after merging exact duplicates).</summary>
        public int UniquePositions { get; internal set; }

        public int Edges { get; internal set; }

        /// <summary>Edges used by exactly one triangle (holes).</summary>
        public int BoundaryEdges { get; internal set; }

        /// <summary>Edges used by more than two triangles.</summary>
        public int NonManifoldEdges { get; internal set; }

        /// <summary>Two-triangle edges whose triangles are wound inconsistently.</summary>
        public int InconsistentEdges { get; internal set; }

        public int DegenerateTriangles { get; internal set; }

        /// <summary>Watertight, two-manifold and consistently oriented.</summary>
        public bool IsClosed => Edges > 0 && BoundaryEdges == 0 && NonManifoldEdges == 0 && InconsistentEdges == 0;

        public override string ToString() =>
            $"edges {Edges}, boundary {BoundaryEdges}, non-manifold {NonManifoldEdges}, inconsistent {InconsistentEdges}, degenerate {DegenerateTriangles}";
    }

    /// <summary>
    /// Triangle mesh in metres, Z up (the simulator's convention). Vertices are shared
    /// between triangles; colours can be given per triangle, per group or per vertex,
    /// normals optionally per triangle corner.
    /// </summary>
    public sealed partial class MeshModel
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>Vertex positions (metres, model frame).</summary>
        public List<Vec3> Vertices { get; } = new List<Vec3>();

        /// <summary>Triangle vertex indices, three per triangle, counter-clockwise seen from outside.</summary>
        public List<int> Indices { get; } = new List<int>();

        /// <summary>Optional per-corner normals (Count == Indices.Count).</summary>
        public List<Vec3>? Normals { get; set; }

        /// <summary>Optional per-vertex colours (Count == Vertices.Count).</summary>
        public List<MeshColor>? VertexColors { get; set; }

        /// <summary>Optional per-triangle colours (Count == TriangleCount).</summary>
        public List<MeshColor>? TriangleColors { get; set; }

        public List<MeshGroup> Groups { get; } = new List<MeshGroup>();

        /// <summary>Optional group index of each triangle (Count == TriangleCount).</summary>
        public List<int>? TriangleGroups { get; set; }

        public int TriangleCount => Indices.Count / 3;

        public int AddVertex(Vec3 p)
        {
            Vertices.Add(p);
            return Vertices.Count - 1;
        }

        public int AddTriangle(int a, int b, int c)
        {
            Indices.Add(a);
            Indices.Add(b);
            Indices.Add(c);
            return TriangleCount - 1;
        }

        public (Vec3 A, Vec3 B, Vec3 C) Triangle(int t) =>
            (Vertices[Indices[3 * t]], Vertices[Indices[3 * t + 1]], Vertices[Indices[3 * t + 2]]);

        /// <summary>Unit normal from the winding (zero for a degenerate triangle).</summary>
        public Vec3 FaceNormal(int t)
        {
            var (a, b, c) = Triangle(t);
            return (b - a).Cross(c - a).Normalized();
        }

        /// <summary>Display colour of a triangle: triangle → group → mean vertex colour → grey.</summary>
        public MeshColor ColorOf(int t)
        {
            if (TriangleColors != null && t < TriangleColors.Count) return TriangleColors[t];
            if (TriangleGroups != null && t < TriangleGroups.Count)
            {
                int g = TriangleGroups[t];
                if (g >= 0 && g < Groups.Count && Groups[g].Color.HasValue) return Groups[g].Color!.Value;
            }

            if (VertexColors != null)
            {
                var c0 = VertexColors[Indices[3 * t]];
                var c1 = VertexColors[Indices[3 * t + 1]];
                var c2 = VertexColors[Indices[3 * t + 2]];
                return new MeshColor((byte)((c0.R + c1.R + c2.R) / 3), (byte)((c0.G + c1.G + c2.G) / 3), (byte)((c0.B + c1.B + c2.B) / 3),
                    (byte)((c0.A + c1.A + c2.A) / 3));
            }

            return MeshColor.DefaultGrey;
        }

        public Aabb Bounds => Vertices.Count == 0 ? Aabb.Empty : Aabb.FromPoints(Vertices);

        public double SurfaceArea()
        {
            double area = 0;
            for (int t = 0; t < TriangleCount; t++)
            {
                var (a, b, c) = Triangle(t);
                area += 0.5 * (b - a).Cross(c - a).Length;
            }

            return area;
        }

        /// <summary>Throws if index/attribute counts are inconsistent.</summary>
        public void Validate()
        {
            if (Indices.Count % 3 != 0) throw new InvalidOperationException("Index count is not a multiple of 3.");
            foreach (int i in Indices)
            {
                if (i < 0 || i >= Vertices.Count) throw new InvalidOperationException($"Vertex index {i} out of range.");
            }

            if (Normals != null && Normals.Count != Indices.Count) throw new InvalidOperationException("Normals must be per corner.");
            if (VertexColors != null && VertexColors.Count != Vertices.Count) throw new InvalidOperationException("Vertex colour count mismatch.");
            if (TriangleColors != null && TriangleColors.Count != TriangleCount) throw new InvalidOperationException("Triangle colour count mismatch.");
            if (TriangleGroups != null && TriangleGroups.Count != TriangleCount) throw new InvalidOperationException("Triangle group count mismatch.");
        }

        // ------------------------------------------------------------------ transforms

        public MeshModel Clone()
        {
            var m = new MeshModel { Name = Name };
            m.Vertices.AddRange(Vertices);
            m.Indices.AddRange(Indices);
            if (Normals != null) m.Normals = new List<Vec3>(Normals);
            if (VertexColors != null) m.VertexColors = new List<MeshColor>(VertexColors);
            if (TriangleColors != null) m.TriangleColors = new List<MeshColor>(TriangleColors);
            if (TriangleGroups != null) m.TriangleGroups = new List<int>(TriangleGroups);
            foreach (var g in Groups) m.Groups.Add(new MeshGroup { Name = g.Name, Material = g.Material, Color = g.Color });
            return m;
        }

        /// <summary>In place: p → rotation·(scale·p) + translation.</summary>
        public MeshModel Transform(Quat rotation, Vec3 translation, double scale = 1.0)
        {
            for (int i = 0; i < Vertices.Count; i++) Vertices[i] = rotation.Rotate(Vertices[i] * scale) + translation;
            if (Normals != null)
            {
                for (int i = 0; i < Normals.Count; i++) Normals[i] = rotation.Rotate(Normals[i]);
            }

            if (scale < 0) FlipWinding();
            return this;
        }

        public MeshModel Translate(Vec3 offset) => Transform(Quat.Identity, offset);

        public MeshModel Scale(double factor) => Transform(Quat.Identity, Vec3.Zero, factor);

        public MeshModel Transformed(Quat rotation, Vec3 translation, double scale = 1.0) => Clone().Transform(rotation, translation, scale);

        /// <summary>Reverses every triangle (inside-out models).</summary>
        public void FlipWinding()
        {
            for (int t = 0; t < TriangleCount; t++)
            {
                int i = 3 * t;
                (Indices[i + 1], Indices[i + 2]) = (Indices[i + 2], Indices[i + 1]);
                if (Normals != null)
                {
                    (Normals[i + 1], Normals[i + 2]) = (Normals[i + 2], Normals[i + 1]);
                    for (int k = 0; k < 3; k++) Normals[i + k] = -Normals[i + k];
                }
            }
        }

        /// <summary>Converts a Y-up model to Z-up (rotation +90° about X).</summary>
        public static Vec3 YUpToZUp(Vec3 p) => new Vec3(p.X, -p.Z, p.Y);

        public static Vec3 ZUpToYUp(Vec3 p) => new Vec3(p.X, p.Z, -p.Y);

        /// <summary>Appends another mesh (groups and colours are carried over).</summary>
        public void Append(MeshModel other)
        {
            int baseV = Vertices.Count, baseT = TriangleCount, baseG = Groups.Count;
            Vertices.AddRange(other.Vertices);
            foreach (int i in other.Indices) Indices.Add(i + baseV);

            if (Normals != null || other.Normals != null)
            {
                Normals ??= Enumerable.Repeat(Vec3.Zero, baseT * 3).ToList();
                Normals.AddRange(other.Normals ?? Enumerable.Repeat(Vec3.Zero, other.Indices.Count));
            }

            if (VertexColors != null || other.VertexColors != null)
            {
                VertexColors ??= Enumerable.Repeat(MeshColor.DefaultGrey, baseV).ToList();
                VertexColors.AddRange(other.VertexColors ?? Enumerable.Repeat(MeshColor.DefaultGrey, other.Vertices.Count));
            }

            if (TriangleColors != null || other.TriangleColors != null)
            {
                // Keep the display colour of both sides when only one has explicit triangle colours.
                if (TriangleColors == null)
                {
                    TriangleColors = new List<MeshColor>(baseT);
                    for (int t = 0; t < baseT; t++) TriangleColors.Add(ColorOf(t));
                }

                if (other.TriangleColors != null) TriangleColors.AddRange(other.TriangleColors);
                else
                    for (int t = 0; t < other.TriangleCount; t++) TriangleColors.Add(other.ColorOf(t));
            }

            foreach (var g in other.Groups) Groups.Add(new MeshGroup { Name = g.Name, Material = g.Material, Color = g.Color });
            if (TriangleGroups != null || other.TriangleGroups != null)
            {
                TriangleGroups ??= Enumerable.Repeat(-1, baseT).ToList();
                if (other.TriangleGroups != null) TriangleGroups.AddRange(other.TriangleGroups.Select(g => g < 0 ? -1 : g + baseG));
                else TriangleGroups.AddRange(Enumerable.Repeat(-1, other.TriangleCount));
            }
        }

        /// <summary>Copy of the triangles of one group (vertices compacted).</summary>
        public MeshModel ExtractGroup(int group)
        {
            var m = new MeshModel { Name = group >= 0 && group < Groups.Count ? Groups[group].Name : Name };
            var map = new Dictionary<int, int>();
            if (group >= 0 && group < Groups.Count)
                m.Groups.Add(new MeshGroup { Name = Groups[group].Name, Material = Groups[group].Material, Color = Groups[group].Color });
            if (Normals != null) m.Normals = new List<Vec3>();
            if (VertexColors != null) m.VertexColors = new List<MeshColor>();
            if (TriangleColors != null) m.TriangleColors = new List<MeshColor>();
            if (m.Groups.Count > 0) m.TriangleGroups = new List<int>();
            for (int t = 0; t < TriangleCount; t++)
            {
                int g = TriangleGroups != null ? TriangleGroups[t] : -1;
                if (g != group) continue;
                for (int k = 0; k < 3; k++)
                {
                    int v = Indices[3 * t + k];
                    if (!map.TryGetValue(v, out int nv))
                    {
                        nv = m.AddVertex(Vertices[v]);
                        map[v] = nv;
                        m.VertexColors?.Add(VertexColors![v]);
                    }

                    m.Indices.Add(nv);
                    m.Normals?.Add(Normals![3 * t + k]);
                }

                m.TriangleColors?.Add(TriangleColors![t]);
                m.TriangleGroups?.Add(0);
            }

            return m;
        }

        /// <summary>One mesh per group (useful to split a concave obstacle into convex-ish parts).</summary>
        public IReadOnlyList<MeshModel> SplitByGroup()
        {
            if (TriangleGroups == null || Groups.Count == 0) return new[] { Clone() };
            var used = new SortedSet<int>(TriangleGroups);
            return used.Select(ExtractGroup).Where(m => m.TriangleCount > 0).ToList();
        }

        /// <summary>
        /// Merges vertices closer than <paramref name="tolerance"/> (0 = identical positions only)
        /// and drops triangles that collapse. Per-vertex attributes keep the first vertex's value.
        /// </summary>
        public MeshModel Weld(double tolerance = 0)
        {
            var remap = WeldMap(tolerance, out int unique);
            var m = new MeshModel { Name = Name };
            var newIndex = new int[Vertices.Count];
            var firstOf = new int[unique];
            for (int i = 0; i < unique; i++) firstOf[i] = -1;
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (firstOf[remap[i]] < 0) firstOf[remap[i]] = i;
            }

            for (int u = 0; u < unique; u++) m.Vertices.Add(Vertices[firstOf[u]]);
            if (VertexColors != null) m.VertexColors = firstOf.Select(i => VertexColors[i]).ToList();
            for (int i = 0; i < Vertices.Count; i++) newIndex[i] = remap[i];
            foreach (var g in Groups) m.Groups.Add(new MeshGroup { Name = g.Name, Material = g.Material, Color = g.Color });
            if (Normals != null) m.Normals = new List<Vec3>();
            if (TriangleColors != null) m.TriangleColors = new List<MeshColor>();
            if (TriangleGroups != null) m.TriangleGroups = new List<int>();
            for (int t = 0; t < TriangleCount; t++)
            {
                int a = newIndex[Indices[3 * t]], b = newIndex[Indices[3 * t + 1]], c = newIndex[Indices[3 * t + 2]];
                if (a == b || b == c || a == c) continue;
                m.AddTriangle(a, b, c);
                if (Normals != null) m.Normals!.AddRange(new[] { Normals[3 * t], Normals[3 * t + 1], Normals[3 * t + 2] });
                m.TriangleColors?.Add(TriangleColors![t]);
                m.TriangleGroups?.Add(TriangleGroups![t]);
            }

            return m;
        }

        /// <summary>Maps each vertex to a canonical index (identical, or within tolerance on a grid).</summary>
        internal int[] WeldMap(double tolerance, out int unique)
        {
            var remap = new int[Vertices.Count];
            unique = 0;
            if (tolerance <= 0)
            {
                var map = new Dictionary<Vec3Key, int>(Vertices.Count);
                for (int i = 0; i < Vertices.Count; i++)
                {
                    var key = new Vec3Key(Vertices[i]);
                    if (!map.TryGetValue(key, out int u))
                    {
                        u = unique++;
                        map.Add(key, u);
                    }

                    remap[i] = u;
                }

                return remap;
            }

            // Grid hashing: look at the 27 neighbouring cells so points closer than the tolerance always merge.
            var grid = new Dictionary<(long, long, long), List<int>>();
            var reps = new List<Vec3>();
            double inv = 1.0 / tolerance, tol2 = tolerance * tolerance;
            for (int i = 0; i < Vertices.Count; i++)
            {
                var p = Vertices[i];
                long cx = (long)Math.Floor(p.X * inv), cy = (long)Math.Floor(p.Y * inv), cz = (long)Math.Floor(p.Z * inv);
                int found = -1;
                for (long dx = -1; dx <= 1 && found < 0; dx++)
                for (long dy = -1; dy <= 1 && found < 0; dy++)
                for (long dz = -1; dz <= 1 && found < 0; dz++)
                {
                    if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                    foreach (int u in list)
                    {
                        var d = reps[u] - p;
                        if (d.Dot(d) <= tol2)
                        {
                            found = u;
                            break;
                        }
                    }
                }

                if (found < 0)
                {
                    found = unique++;
                    reps.Add(p);
                    if (!grid.TryGetValue((cx, cy, cz), out var cell)) grid[(cx, cy, cz)] = cell = new List<int>();
                    cell.Add(found);
                }

                remap[i] = found;
            }

            return remap;
        }

        /// <summary>Closedness / manifoldness check on position-merged vertices.</summary>
        public MeshTopology AnalyzeTopology()
        {
            var remap = WeldMap(0, out int unique);
            var topo = new MeshTopology { UniquePositions = unique };
            // value: low 16 bits = uses in a<b direction, high 16 bits = uses in b<a direction
            var edges = new Dictionary<long, int>(Indices.Count, MixedLongComparer.Instance);
            for (int t = 0; t < TriangleCount; t++)
            {
                int a = remap[Indices[3 * t]], b = remap[Indices[3 * t + 1]], c = remap[Indices[3 * t + 2]];
                if (a == b || b == c || a == c)
                {
                    topo.DegenerateTriangles++;
                    continue;
                }

                AddEdge(edges, a, b);
                AddEdge(edges, b, c);
                AddEdge(edges, c, a);
            }

            topo.Edges = edges.Count;
            foreach (var v in edges.Values)
            {
                int fwd = v & 0xFFFF, back = v >> 16;
                int total = fwd + back;
                if (total == 1) topo.BoundaryEdges++;
                else if (total > 2) topo.NonManifoldEdges++;
                else if (fwd != 1) topo.InconsistentEdges++;
            }

            return topo;
        }

        private static void AddEdge(Dictionary<long, int> edges, int a, int b)
        {
            long key = a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;
            int inc = a < b ? 1 : 1 << 16;
            edges.TryGetValue(key, out int v);
            if ((v & 0xFFFF) < 0x7FFF && (v >> 16) < 0x7FFF) v += inc;
            edges[key] = v;
        }

        // ------------------------------------------------------------------ primitives

        /// <summary>Box centred on the origin.</summary>
        public static MeshModel CreateBox(Vec3 size)
        {
            var m = new MeshModel { Name = "box" };
            double hx = size.X / 2, hy = size.Y / 2, hz = size.Z / 2;
            for (int i = 0; i < 8; i++) m.AddVertex(new Vec3((i & 1) == 0 ? -hx : hx, (i & 2) == 0 ? -hy : hy, (i & 4) == 0 ? -hz : hz));
            int[] q =
            {
                0, 2, 3, 1, // -z
                4, 5, 7, 6, // +z
                0, 1, 5, 4, // -y
                2, 6, 7, 3, // +y
                0, 4, 6, 2, // -x
                1, 3, 7, 5 // +x
            };
            for (int f = 0; f < 6; f++)
            {
                m.AddTriangle(q[4 * f], q[4 * f + 1], q[4 * f + 2]);
                m.AddTriangle(q[4 * f], q[4 * f + 2], q[4 * f + 3]);
            }

            return m;
        }

        /// <summary>Closed cylinder along Z centred on the origin.</summary>
        public static MeshModel CreateCylinder(double radius, double height, int segments = 64)
        {
            segments = Math.Max(3, segments);
            var m = new MeshModel { Name = "cylinder" };
            double hz = height / 2;
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                m.AddVertex(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), -hz));
                m.AddVertex(new Vec3(radius * Math.Cos(a), radius * Math.Sin(a), hz));
            }

            int bottom = m.AddVertex(new Vec3(0, 0, -hz)), top = m.AddVertex(new Vec3(0, 0, hz));
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                int b0 = 2 * i, t0 = 2 * i + 1, b1 = 2 * j, t1 = 2 * j + 1;
                m.AddTriangle(b0, b1, t1);
                m.AddTriangle(b0, t1, t0);
                m.AddTriangle(bottom, b1, b0);
                m.AddTriangle(top, t0, t1);
            }

            return m;
        }

        /// <summary>UV sphere centred on the origin.</summary>
        public static MeshModel CreateSphere(double radius, int segments = 64, int rings = 32)
        {
            segments = Math.Max(3, segments);
            rings = Math.Max(2, rings);
            var m = new MeshModel { Name = "sphere" };
            int south = m.AddVertex(new Vec3(0, 0, -radius));
            for (int r = 1; r < rings; r++)
            {
                double phi = Math.PI * r / rings - Math.PI / 2;
                for (int s = 0; s < segments; s++)
                {
                    double th = 2 * Math.PI * s / segments;
                    m.AddVertex(new Vec3(radius * Math.Cos(phi) * Math.Cos(th), radius * Math.Cos(phi) * Math.Sin(th), radius * Math.Sin(phi)));
                }
            }

            int north = m.AddVertex(new Vec3(0, 0, radius));
            int V(int r, int s) => 1 + (r - 1) * segments + (s % segments);
            for (int s = 0; s < segments; s++)
            {
                m.AddTriangle(south, V(1, s + 1), V(1, s));
                m.AddTriangle(north, V(rings - 1, s), V(rings - 1, s + 1));
            }

            for (int r = 1; r < rings - 1; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    m.AddTriangle(V(r, s), V(r, s + 1), V(r + 1, s + 1));
                    m.AddTriangle(V(r, s), V(r + 1, s + 1), V(r + 1, s));
                }
            }

            return m;
        }

        public override string ToString() => $"{Name}: {Vertices.Count} vertices, {TriangleCount} triangles";
    }

    /// <summary>
    /// long comparer with a full 64-bit mix: the default long hash (lo ^ hi) collides
    /// massively for packed (a, b) index pairs with a ≈ b, making dictionaries quadratic.
    /// </summary>
    internal sealed class MixedLongComparer : IEqualityComparer<long>
    {
        public static readonly MixedLongComparer Instance = new MixedLongComparer();
        public bool Equals(long x, long y) => x == y;

        public int GetHashCode(long v)
        {
            unchecked
            {
                ulong h = (ulong)v;
                h ^= h >> 33;
                h *= 0xFF51AFD7ED558CCDUL;
                h ^= h >> 33;
                h *= 0xC4CEB9FE1A85EC53UL;
                h ^= h >> 33;
                return (int)h;
            }
        }
    }

    /// <summary>Exact-position hash key (−0 and +0 compare equal).</summary>
    internal readonly struct Vec3Key : IEquatable<Vec3Key>
    {
        private readonly long _x, _y, _z;

        public Vec3Key(Vec3 p)
        {
            _x = BitConverter.DoubleToInt64Bits(p.X + 0.0);
            _y = BitConverter.DoubleToInt64Bits(p.Y + 0.0);
            _z = BitConverter.DoubleToInt64Bits(p.Z + 0.0);
        }

        public bool Equals(Vec3Key o) => _x == o._x && _y == o._y && _z == o._z;
        public override bool Equals(object? obj) => obj is Vec3Key k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                ulong h = (ulong)_x * 0x9E3779B97F4A7C15UL;
                h ^= (ulong)_y * 0xC2B2AE3D27D4EB4FUL + (h << 6) + (h >> 2);
                h ^= (ulong)_z * 0x165667B19E3779F9UL + (h << 6) + (h >> 2);
                return (int)(h ^ (h >> 32));
            }
        }
    }
}
