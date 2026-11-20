using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// Editable polygon mesh (millimetres, Z up). Faces are vertex index loops,
    /// counter-clockwise seen from outside, and may have any number of corners
    /// (convex or concave, no holes). This is the modelling representation;
    /// <see cref="Triangulate"/> produces a <see cref="MeshModel"/> for display, export and physics.
    /// </summary>
    public sealed class PolyMesh
    {
        public List<Vec3> Vertices { get; } = new List<Vec3>();
        public List<int[]> Faces { get; } = new List<int[]>();

        public int AddVertex(Vec3 p)
        {
            Vertices.Add(p);
            return Vertices.Count - 1;
        }

        public int AddFace(params int[] corners)
        {
            Faces.Add(corners);
            return Faces.Count - 1;
        }

        public PolyMesh Clone()
        {
            var m = new PolyMesh();
            m.Vertices.AddRange(Vertices);
            foreach (var f in Faces) m.Faces.Add((int[])f.Clone());
            return m;
        }

        public Aabb Bounds => Vertices.Count == 0 ? Aabb.Empty : Aabb.FromPoints(Vertices);

        public bool IsEmpty => Faces.Count == 0;

        // ------------------------------------------------------------------ face geometry

        /// <summary>Area-weighted normal by Newell's method (length = 2 × area).</summary>
        public Vec3 FaceNormalRaw(int face) => NewellNormal(Faces[face], Vertices);

        public Vec3 FaceNormal(int face) => FaceNormalRaw(face).Normalized();

        public double FaceArea(int face) => FaceNormalRaw(face).Length / 2;

        public Vec3 FaceCenter(int face)
        {
            var f = Faces[face];
            var c = Vec3.Zero;
            foreach (int i in f) c += Vertices[i];
            return f.Length > 0 ? c / f.Length : c;
        }

        public Vec3[] FacePoints(int face) => Faces[face].Select(i => Vertices[i]).ToArray();

        public static Vec3 NewellNormal(IReadOnlyList<int> loop, IReadOnlyList<Vec3> verts)
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0, n = loop.Count; i < n; i++)
            {
                var a = verts[loop[i]];
                var b = verts[loop[(i + 1) % n]];
                x += (a.Y - b.Y) * (a.Z + b.Z);
                y += (a.Z - b.Z) * (a.X + b.X);
                z += (a.X - b.X) * (a.Y + b.Y);
            }

            return new Vec3(x, y, z);
        }

        public static Vec3 NewellNormal(IReadOnlyList<Vec3> pts)
        {
            double x = 0, y = 0, z = 0;
            for (int i = 0, n = pts.Count; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % n];
                x += (a.Y - b.Y) * (a.Z + b.Z);
                y += (a.Z - b.Z) * (a.X + b.X);
                z += (a.X - b.X) * (a.Y + b.Y);
            }

            return new Vec3(x, y, z);
        }

        // ------------------------------------------------------------------ topology

        public static long EdgeKey(int a, int b) => a < b ? (long)a << 32 | (uint)b : (long)b << 32 | (uint)a;

        public static (int A, int B) EdgeOf(long key) => ((int)(key >> 32), (int)(key & 0xFFFFFFFF));

        /// <summary>Unique undirected edges (A &lt; B).</summary>
        public List<(int A, int B)> Edges()
        {
            var seen = new HashSet<long>();
            var list = new List<(int, int)>();
            foreach (var f in Faces)
            {
                for (int i = 0; i < f.Length; i++)
                {
                    int a = f[i], b = f[(i + 1) % f.Length];
                    if (a == b) continue;
                    if (seen.Add(EdgeKey(a, b))) list.Add(a < b ? (a, b) : (b, a));
                }
            }

            return list;
        }

        /// <summary>Faces using each undirected edge.</summary>
        public Dictionary<long, List<int>> EdgeFaces()
        {
            var map = new Dictionary<long, List<int>>();
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var f = Faces[fi];
                for (int i = 0; i < f.Length; i++)
                {
                    long k = EdgeKey(f[i], f[(i + 1) % f.Length]);
                    if (!map.TryGetValue(k, out var l)) map[k] = l = new List<int>(2);
                    l.Add(fi);
                }
            }

            return map;
        }

        /// <summary>Faces using each vertex.</summary>
        public List<int>[] VertexFaces()
        {
            var vf = new List<int>[Vertices.Count];
            for (int i = 0; i < vf.Length; i++) vf[i] = new List<int>(4);
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                foreach (int v in Faces[fi])
                {
                    if (vf[v].Count == 0 || vf[v][vf[v].Count - 1] != fi) vf[v].Add(fi);
                }
            }

            return vf;
        }

        // ------------------------------------------------------------------ transforms

        /// <summary>In place; a mirroring transform (negative determinant) reverses the faces so they stay outward.</summary>
        public PolyMesh Transform(Affine m)
        {
            for (int i = 0; i < Vertices.Count; i++) Vertices[i] = m.Apply(Vertices[i]);
            if (m.Determinant < 0) FlipAll();
            return this;
        }

        public PolyMesh Transformed(Affine m) => Clone().Transform(m);

        public PolyMesh Translate(Vec3 d)
        {
            for (int i = 0; i < Vertices.Count; i++) Vertices[i] += d;
            return this;
        }

        public void FlipAll()
        {
            foreach (var f in Faces) Array.Reverse(f);
        }

        public void FlipFace(int face) => Array.Reverse(Faces[face]);

        /// <summary>Appends another mesh; returns the vertex offset of its vertices.</summary>
        public int Append(PolyMesh other)
        {
            int baseV = Vertices.Count;
            Vertices.AddRange(other.Vertices);
            foreach (var f in other.Faces) Faces.Add(f.Select(i => i + baseV).ToArray());
            return baseV;
        }

        // ------------------------------------------------------------------ triangles

        /// <summary>Triangulated copy (same units). <paramref name="triangleFace"/> maps each triangle to its polygon.</summary>
        public MeshModel Triangulate(out int[] triangleFace)
        {
            var m = new MeshModel();
            m.Vertices.AddRange(Vertices);
            var map = new List<int>(Faces.Count * 2);
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var f = Faces[fi];
                if (f.Length < 3) continue;
                if (f.Length == 3)
                {
                    m.AddTriangle(f[0], f[1], f[2]);
                    map.Add(fi);
                    continue;
                }

                var pts = new Vec3[f.Length];
                for (int i = 0; i < f.Length; i++) pts[i] = Vertices[f[i]];
                var normal = NewellNormal(pts);
                if (normal.Length < 1e-18) continue;
                var tri = Triangulator.TriangulatePolygon(pts, normal);
                for (int t = 0; t + 2 < tri.Count; t += 3)
                {
                    m.AddTriangle(f[tri[t]], f[tri[t + 1]], f[tri[t + 2]]);
                    map.Add(fi);
                }
            }

            triangleFace = map.ToArray();
            return m;
        }

        public MeshModel Triangulate() => Triangulate(out _);

        /// <summary>Triangle mesh in metres (for the world, physics and file export).</summary>
        public MeshModel ToMeshModel(double unitToMetres = 0.001)
        {
            var m = Triangulate();
            if (unitToMetres != 1.0) m.Scale(unitToMetres);
            return m;
        }

        /// <summary>One polygon per triangle; vertices welded by <paramref name="weldTolerance"/> (model units after <paramref name="scale"/>).</summary>
        public static PolyMesh FromMeshModel(MeshModel mesh, double scale = 1.0, double weldTolerance = 1e-6)
        {
            var p = new PolyMesh();
            foreach (var v in mesh.Vertices) p.Vertices.Add(v * scale);
            for (int t = 0; t < mesh.TriangleCount; t++) p.Faces.Add(new[] { mesh.Indices[3 * t], mesh.Indices[3 * t + 1], mesh.Indices[3 * t + 2] });
            p.Weld(weldTolerance);
            return p;
        }

        // ------------------------------------------------------------------ measurements

        /// <summary>Enclosed volume (signed: negative when the faces point inwards).</summary>
        public double Volume()
        {
            double v = 0;
            var tri = Triangulate();
            for (int t = 0; t < tri.TriangleCount; t++)
            {
                var (a, b, c) = tri.Triangle(t);
                v += a.Dot(b.Cross(c));
            }

            return v / 6;
        }

        public double SurfaceArea()
        {
            double s = 0;
            for (int f = 0; f < Faces.Count; f++) s += FaceArea(f);
            return s;
        }

        public MeshTopology Topology() => Triangulate().AnalyzeTopology();

        /// <summary>Watertight, two-manifold and consistently oriented.</summary>
        public bool IsClosed => Topology().IsClosed;

        // ------------------------------------------------------------------ cleanup

        /// <summary>Drops vertices no face uses; returns the number removed.</summary>
        public int RemoveUnusedVertices()
        {
            var used = new bool[Vertices.Count];
            foreach (var f in Faces)
                foreach (int i in f) used[i] = true;
            var remap = new int[Vertices.Count];
            var kept = new List<Vec3>(Vertices.Count);
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (used[i])
                {
                    remap[i] = kept.Count;
                    kept.Add(Vertices[i]);
                }
                else remap[i] = -1;
            }

            int removed = Vertices.Count - kept.Count;
            if (removed == 0) return 0;
            Vertices.Clear();
            Vertices.AddRange(kept);
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var f = Faces[fi];
                for (int k = 0; k < f.Length; k++) f[k] = remap[f[k]];
            }

            return removed;
        }

        /// <summary>
        /// Merges vertices closer than <paramref name="tolerance"/>, removes repeated corners and
        /// faces that collapse. Returns the number of vertices merged away.
        /// </summary>
        public int Weld(double tolerance)
        {
            int before = Vertices.Count;
            var tmp = new MeshModel();
            tmp.Vertices.AddRange(Vertices);
            var remap = tmp.WeldMap(Math.Max(0, tolerance), out int unique);
            var pos = new Vec3[unique];
            var count = new int[unique];
            for (int i = 0; i < Vertices.Count; i++)
            {
                // keep the first representative exactly (no averaging drift)
                if (count[remap[i]]++ == 0) pos[remap[i]] = Vertices[i];
            }

            Vertices.Clear();
            Vertices.AddRange(pos);
            for (int fi = Faces.Count - 1; fi >= 0; fi--)
            {
                var f = Faces[fi];
                for (int k = 0; k < f.Length; k++) f[k] = remap[f[k]];
                var clean = CleanLoop(f);
                if (clean == null) Faces.RemoveAt(fi);
                else Faces[fi] = clean;
            }

            RemoveUnusedVertices();
            return before - Vertices.Count;
        }

        /// <summary>Removes cyclically repeated corners and a→b→a spikes; null when fewer than three remain.</summary>
        internal static int[]? CleanLoop(int[] f)
        {
            var l = new List<int>(f.Length);
            foreach (int i in f)
            {
                if (l.Count > 0 && l[l.Count - 1] == i) continue;
                l.Add(i);
            }

            while (l.Count > 1 && l[0] == l[l.Count - 1]) l.RemoveAt(l.Count - 1);
            bool changed = true;
            while (changed && l.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < l.Count && l.Count >= 3; i++)
                {
                    int prev = l[(i - 1 + l.Count) % l.Count], next = l[(i + 1) % l.Count];
                    if (prev == next)
                    {
                        // spike prev→cur→prev
                        int a = i, b = (i + 1) % l.Count;
                        if (a > b) (a, b) = (b, a);
                        l.RemoveAt(b);
                        l.RemoveAt(a);
                        changed = true;
                        break;
                    }
                }
            }

            return l.Count >= 3 ? l.ToArray() : null;
        }

        /// <summary>Removes faces with (almost) no area.</summary>
        public int RemoveDegenerateFaces(double minArea = 1e-12)
        {
            int removed = Faces.RemoveAll(f => f.Length < 3 || NewellNormal(f, Vertices).Length / 2 < minArea);
            if (removed > 0) RemoveUnusedVertices();
            return removed;
        }

        /// <summary>
        /// Removes corners that lie on a straight line between their neighbours when every face
        /// that uses the vertex sees it that way (so neighbouring faces stay watertight).
        /// </summary>
        public int RemoveCollinearVertices(double tolerance = 1e-7)
        {
            var vf = VertexFaces();
            var removable = new bool[Vertices.Count];
            int count = 0;
            for (int v = 0; v < Vertices.Count; v++)
            {
                if (vf[v].Count == 0 || vf[v].Count > 2) continue;
                bool ok = true;
                foreach (int fi in vf[v])
                {
                    var f = Faces[fi];
                    if (f.Length <= 3)
                    {
                        ok = false;
                        break;
                    }

                    int k = Array.IndexOf(f, v);
                    var a = Vertices[f[(k - 1 + f.Length) % f.Length]];
                    var b = Vertices[f[(k + 1) % f.Length]];
                    var p = Vertices[v];
                    var ab = b - a;
                    double len = ab.Length;
                    if (len < 1e-12)
                    {
                        ok = false;
                        break;
                    }

                    double dist = (p - a).Cross(ab).Length / len;
                    double t = (p - a).Dot(ab) / (len * len);
                    if (dist > tolerance * Math.Max(1, len) || t <= 0 || t >= 1)
                    {
                        ok = false;
                        break;
                    }
                }

                if (vf[v].Count == 1)
                {
                    // An open-boundary vertex used by one face only: removing it keeps the boundary straight.
                    ok = ok && true;
                }

                if (ok)
                {
                    removable[v] = true;
                    count++;
                }
            }

            if (count == 0) return 0;
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var f = Faces[fi];
                if (!f.Any(i => removable[i])) continue;
                var kept = f.Where(i => !removable[i]).ToArray();
                if (kept.Length >= 3) Faces[fi] = kept;
            }

            RemoveUnusedVertices();
            return count;
        }

        /// <summary>
        /// Inserts vertices that lie on the inside of another face's edge into that edge
        /// (removes T-junctions so the triangulation is watertight). Returns the number of insertions.
        /// </summary>
        public int FixTJunctions(double tolerance)
        {
            if (Vertices.Count == 0) return 0;
            // Vertices sorted by X for a sweep over each edge's bounding range.
            var order = Enumerable.Range(0, Vertices.Count).OrderBy(i => Vertices[i].X).ToArray();
            var xs = order.Select(i => Vertices[i].X).ToArray();
            int inserted = 0;
            var cache = new Dictionary<long, int[]>();
            for (int fi = 0; fi < Faces.Count; fi++)
            {
                var f = Faces[fi];
                List<int>? result = null;
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    long key = EdgeKey(a, b);
                    if (!cache.TryGetValue(key, out var mids))
                    {
                        mids = PointsOnSegment(a, b, order, xs, tolerance);
                        cache[key] = mids;
                    }

                    if (mids.Length > 0 && result == null)
                    {
                        result = new List<int>(f.Length + mids.Length);
                        for (int j = 0; j < k; j++)
                        {
                            result.Add(f[j]);
                        }
                    }

                    if (result == null) continue;
                    result.Add(a);
                    if (mids.Length == 0) continue;
                    // mids are ordered from min(a,b) to max(a,b) index-wise; orient from a to b.
                    var pa = Vertices[a];
                    var sorted = mids.OrderBy(m => (Vertices[m] - pa).Length).ToArray();
                    result.AddRange(sorted);
                    inserted += sorted.Length;
                }

                if (result != null) Faces[fi] = result.ToArray();
            }

            return inserted;
        }

        private int[] PointsOnSegment(int a, int b, int[] order, double[] xs, double tol)
        {
            var pa = Vertices[a];
            var pb = Vertices[b];
            double x0 = Math.Min(pa.X, pb.X) - tol, x1 = Math.Max(pa.X, pb.X) + tol;
            double y0 = Math.Min(pa.Y, pb.Y) - tol, y1 = Math.Max(pa.Y, pb.Y) + tol;
            double z0 = Math.Min(pa.Z, pb.Z) - tol, z1 = Math.Max(pa.Z, pb.Z) + tol;
            var d = pb - pa;
            double len2 = d.Dot(d);
            if (len2 < tol * tol) return Array.Empty<int>();
            int lo = LowerBound(xs, x0);
            List<int>? found = null;
            for (int s = lo; s < xs.Length && xs[s] <= x1; s++)
            {
                int i = order[s];
                if (i == a || i == b) continue;
                var p = Vertices[i];
                if (p.Y < y0 || p.Y > y1 || p.Z < z0 || p.Z > z1) continue;
                double t = (p - pa).Dot(d) / len2;
                if (t <= 1e-9 || t >= 1 - 1e-9) continue;
                var closest = pa + d * t;
                var off = p - closest;
                if (off.Dot(off) > tol * tol) continue;
                // ignore points that coincide with the end points (welding handles those)
                if ((p - pa).Dot(p - pa) <= tol * tol || (p - pb).Dot(p - pb) <= tol * tol) continue;
                (found ??= new List<int>()).Add(i);
            }

            return found == null ? Array.Empty<int>() : found.ToArray();
        }

        private static int LowerBound(double[] xs, double x)
        {
            int lo = 0, hi = xs.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (xs[mid] < x) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        /// <summary>
        /// Merges groups of adjacent coplanar faces into larger polygons ("limited dissolve").
        /// Regions bounded by one loop become one polygon; regions with holes are
        /// re-triangulated and merged into convex pieces.
        /// </summary>
        public int DissolveCoplanar(double angleDegrees = 0.01, double distanceTolerance = 1e-6)
        {
            int fc = Faces.Count;
            if (fc < 2) return 0;
            var normals = new Vec3[fc];
            var offsets = new double[fc];
            for (int i = 0; i < fc; i++)
            {
                normals[i] = FaceNormal(i);
                offsets[i] = normals[i].Dot(Vertices[Faces[i][0]]);
            }

            var parent = Enumerable.Range(0, fc).ToArray();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            double cosTol = Math.Cos(angleDegrees * Math.PI / 180);
            foreach (var kv in EdgeFaces())
            {
                if (kv.Value.Count != 2) continue;
                int f = kv.Value[0], g = kv.Value[1];
                if (f == g) continue;
                if (normals[f].Dot(normals[g]) < cosTol) continue;
                if (Math.Abs(offsets[f] - offsets[g]) > distanceTolerance * Math.Max(1, Math.Abs(offsets[f]))) continue;
                int rf = Find(f), rg = Find(g);
                if (rf != rg) parent[rf] = rg;
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < fc; i++)
            {
                int r = Find(i);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
                l.Add(i);
            }

            int merged = 0;
            var newFaces = new List<int[]>(fc);
            foreach (var g in groups.Values)
            {
                if (g.Count == 1)
                {
                    newFaces.Add(Faces[g[0]]);
                    continue;
                }

                var loops = BoundaryLoops(g, out bool simple);
                if (!simple || loops.Count == 0)
                {
                    foreach (int f in g) newFaces.Add(Faces[f]);
                    continue;
                }

                var n = Vec3.Zero;
                foreach (int f in g) n += FaceNormalRaw(f);
                if (loops.Count == 1)
                {
                    newFaces.Add(loops[0].ToArray());
                    merged += g.Count - 1;
                    continue;
                }

                // Outer loop = the one with the largest area along the region normal.
                int outerIdx = 0;
                double best = double.NegativeInfinity;
                for (int i = 0; i < loops.Count; i++)
                {
                    double a = NewellNormal(loops[i], Vertices).Dot(n.Normalized());
                    if (a > best)
                    {
                        best = a;
                        outerIdx = i;
                    }
                }

                var pieces = TriangulateRegion(loops, outerIdx, n.Normalized());
                if (pieces == null)
                {
                    foreach (int f in g) newFaces.Add(Faces[f]);
                    continue;
                }

                newFaces.AddRange(pieces);
                merged += g.Count - pieces.Count;
            }

            Faces.Clear();
            Faces.AddRange(newFaces);
            RemoveUnusedVertices();
            return merged;
        }

        /// <summary>Directed boundary loops of a face set; <paramref name="simple"/> is false when a vertex has several outgoing boundary edges.</summary>
        internal List<List<int>> BoundaryLoops(IEnumerable<int> faceSet, out bool simple)
        {
            simple = true;
            var directed = new HashSet<long>();
            var faces = faceSet.ToList();
            foreach (int fi in faces)
            {
                var f = Faces[fi];
                for (int k = 0; k < f.Length; k++) directed.Add(Directed(f[k], f[(k + 1) % f.Length]));
            }

            var next = new Dictionary<int, int>();
            foreach (int fi in faces)
            {
                var f = Faces[fi];
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (directed.Contains(Directed(b, a))) continue;
                    if (next.ContainsKey(a))
                    {
                        simple = false;
                        return new List<List<int>>();
                    }

                    next[a] = b;
                }
            }

            var loops = new List<List<int>>();
            var visited = new HashSet<int>();
            foreach (int start in next.Keys)
            {
                if (visited.Contains(start)) continue;
                var loop = new List<int>();
                int cur = start;
                int guard = 0;
                while (visited.Add(cur))
                {
                    loop.Add(cur);
                    if (!next.TryGetValue(cur, out cur) || ++guard > next.Count + 1)
                    {
                        simple = false;
                        return new List<List<int>>();
                    }
                }

                if (cur != start)
                {
                    simple = false;
                    return new List<List<int>>();
                }

                if (loop.Count >= 3) loops.Add(loop);
            }

            return loops;
        }

        private static long Directed(int a, int b) => (long)a << 32 | (uint)b;

        /// <summary>Triangulates a planar region with holes and merges the triangles into convex polygons (Hertel–Mehlhorn).</summary>
        private List<int[]>? TriangulateRegion(List<List<int>> loops, int outerIdx, Vec3 normal)
        {
            var (u, v) = Triangulator.Basis(normal);
            var order = new List<int>();
            Vec2 P(int i) => new Vec2(Vertices[i].Dot(u), Vertices[i].Dot(v));
            var outer = loops[outerIdx].Select(P).ToList();
            order.AddRange(loops[outerIdx]);
            var holes = new List<IReadOnlyList<Vec2>>();
            for (int i = 0; i < loops.Count; i++)
            {
                if (i == outerIdx) continue;
                holes.Add(loops[i].Select(P).ToList());
                order.AddRange(loops[i]);
            }

            var tri = Triangulator.Triangulate(outer, holes);
            if (tri.Count < 3) return null;
            var polys = new List<List<int>>();
            for (int t = 0; t + 2 < tri.Count; t += 3) polys.Add(new List<int> { order[tri[t]], order[tri[t + 1]], order[tri[t + 2]] });
            // Region area must be preserved (sanity check of the triangulation).
            double expected = 0;
            foreach (var l in loops) expected += NewellNormal(l, Vertices).Dot(normal) / 2;
            double got = polys.Sum(p => NewellNormal(p, Vertices).Dot(normal) / 2);
            if (Math.Abs(expected - got) > 1e-6 * Math.Max(1, Math.Abs(expected))) return null;
            MergeConvex(polys, normal);
            return polys.Select(p => p.ToArray()).ToList();
        }

        private void MergeConvex(List<List<int>> polys, Vec3 normal)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                var edgeOwner = new Dictionary<long, int>();
                for (int pi = 0; pi < polys.Count && !changed; pi++)
                {
                    var p = polys[pi];
                    for (int k = 0; k < p.Count && !changed; k++)
                    {
                        int a = p[k], b = p[(k + 1) % p.Count];
                        if (edgeOwner.TryGetValue(Directed(b, a), out int qi) && qi != pi)
                        {
                            var mergedPoly = MergeAlong(polys[qi], p, a, b);
                            if (mergedPoly != null && IsConvex(mergedPoly, normal))
                            {
                                polys[qi] = mergedPoly;
                                polys.RemoveAt(pi);
                                changed = true;
                            }
                        }
                        else edgeOwner[Directed(a, b)] = pi;
                    }
                }
            }
        }

        /// <summary>Joins polygon q (which has edge b→a) and p (which has edge a→b).</summary>
        private static List<int>? MergeAlong(List<int> q, List<int> p, int a, int b)
        {
            int qi = -1;
            for (int k = 0; k < q.Count; k++)
            {
                if (q[k] == b && q[(k + 1) % q.Count] == a)
                {
                    qi = k;
                    break;
                }
            }

            int pi = -1;
            for (int k = 0; k < p.Count; k++)
            {
                if (p[k] == a && p[(k + 1) % p.Count] == b)
                {
                    pi = k;
                    break;
                }
            }

            if (qi < 0 || pi < 0) return null;
            var r = new List<int>(q.Count + p.Count - 2);
            // walk q from a (after b) around to b, then p from b's successor around to a's predecessor
            for (int k = 0; k < q.Count; k++) r.Add(q[(qi + 1 + k) % q.Count]); // a ... b
            for (int k = 2; k < p.Count; k++) r.Add(p[(pi + k) % p.Count]); // after b ... before a
            return r.Distinct().Count() == r.Count ? r : null;
        }

        private bool IsConvex(List<int> poly, Vec3 normal)
        {
            for (int k = 0; k < poly.Count; k++)
            {
                var a = Vertices[poly[(k - 1 + poly.Count) % poly.Count]];
                var b = Vertices[poly[k]];
                var c = Vertices[poly[(k + 1) % poly.Count]];
                if ((b - a).Cross(c - b).Dot(normal) < -1e-12) return false;
            }

            return true;
        }

        public override string ToString() => $"PolyMesh: {Vertices.Count} vertices, {Faces.Count} faces";
    }
}
