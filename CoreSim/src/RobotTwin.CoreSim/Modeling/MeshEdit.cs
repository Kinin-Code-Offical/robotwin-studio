using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>Result of a topology operation: the faces/vertices the user keeps working with.</summary>
    public sealed class EditResult
    {
        public List<int> Faces { get; } = new List<int>();
        public List<int> Vertices { get; } = new List<int>();

        /// <summary>Direction of an extrusion (unit), zero otherwise.</summary>
        public Vec3 Direction { get; set; }
    }

    /// <summary>Edit-mode operations on <see cref="PolyMesh"/> (Blender-style).</summary>
    public static class MeshEdit
    {
        // ------------------------------------------------------------------ selection helpers

        /// <summary>Faces whose every corner is in the vertex set.</summary>
        public static List<int> FacesOfVertices(PolyMesh m, ISet<int> verts)
        {
            var r = new List<int>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                if (m.Faces[f].All(verts.Contains)) r.Add(f);
            }

            return r;
        }

        public static HashSet<int> VerticesOfFaces(PolyMesh m, IEnumerable<int> faces)
        {
            var s = new HashSet<int>();
            foreach (int f in faces)
            {
                if (f >= 0 && f < m.Faces.Count) s.UnionWith(m.Faces[f]);
            }

            return s;
        }

        /// <summary>Area-weighted average normal of a face set.</summary>
        public static Vec3 AverageNormal(PolyMesh m, IEnumerable<int> faces)
        {
            var n = Vec3.Zero;
            foreach (int f in faces) n += m.FaceNormalRaw(f);
            return n.Normalized();
        }

        // ------------------------------------------------------------------ extrude

        /// <summary>
        /// Extrudes a connected or disconnected region of faces along its average normal
        /// (or <paramref name="direction"/>), adding side walls along the region boundary.
        /// </summary>
        public static EditResult ExtrudeFaces(PolyMesh m, IEnumerable<int> faces, double distance, Vec3? direction = null)
        {
            var sel = new HashSet<int>(faces.Where(f => f >= 0 && f < m.Faces.Count));
            var result = new EditResult();
            if (sel.Count == 0) return result;
            var dir = direction?.Normalized() ?? AverageNormal(m, sel);
            if (dir.Length < 0.5) dir = m.FaceNormal(sel.First());
            result.Direction = dir;

            var directed = new HashSet<long>();
            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++) directed.Add(Dir(loop[k], loop[(k + 1) % loop.Length]));
            }

            // Vertices shared with unselected faces are duplicated; the rest just move.
            var usedOutside = new HashSet<int>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                if (!sel.Contains(f)) usedOutside.UnionWith(m.Faces[f]);
            }

            var regionVerts = VerticesOfFaces(m, sel);
            var dup = new Dictionary<int, int>();
            foreach (int v in regionVerts)
            {
                if (usedOutside.Contains(v)) dup[v] = m.AddVertex(m.Vertices[v]);
            }

            var sides = new List<int[]>();
            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++)
                {
                    int a = loop[k], b = loop[(k + 1) % loop.Length];
                    if (directed.Contains(Dir(b, a))) continue; // interior edge of the region
                    int a2 = dup.TryGetValue(a, out int x) ? x : a;
                    int b2 = dup.TryGetValue(b, out int y) ? y : b;
                    if (a2 == a && b2 == b) continue;
                    sides.Add(new[] { a, b, b2, a2 });
                }
            }

            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++)
                {
                    if (dup.TryGetValue(loop[k], out int nv)) loop[k] = nv;
                }
            }

            var moved = VerticesOfFaces(m, sel);
            foreach (int v in moved) m.Vertices[v] += dir * distance;
            foreach (var s in sides) m.Faces.Add(s);
            result.Faces.AddRange(sel);
            result.Vertices.AddRange(moved);
            return result;
        }

        private static long Dir(int a, int b) => (long)a << 32 | (uint)b;

        // ------------------------------------------------------------------ inset

        /// <summary>
        /// Insets a face region by <paramref name="thickness"/> (mm) within the faces' plane,
        /// adding a ring of faces; <paramref name="individual"/> insets every face on its own.
        /// </summary>
        public static EditResult InsetFaces(PolyMesh m, IEnumerable<int> faces, double thickness, bool individual = false)
        {
            var result = new EditResult();
            var list = faces.Where(f => f >= 0 && f < m.Faces.Count).Distinct().ToList();
            if (list.Count == 0) return result;
            if (individual)
            {
                foreach (int f in list)
                {
                    var r = InsetRegion(m, new HashSet<int> { f }, thickness);
                    result.Faces.AddRange(r.Faces);
                    result.Vertices.AddRange(r.Vertices);
                }

                return result;
            }

            return InsetRegion(m, new HashSet<int>(list), thickness);
        }

        private static EditResult InsetRegion(PolyMesh m, HashSet<int> sel, double t)
        {
            var result = new EditResult();
            var directed = new HashSet<long>();
            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++) directed.Add(Dir(loop[k], loop[(k + 1) % loop.Length]));
            }

            // boundary: next/prev along the region outline
            var next = new Dictionary<int, int>();
            var prev = new Dictionary<int, int>();
            var boundary = new List<(int A, int B)>();
            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++)
                {
                    int a = loop[k], b = loop[(k + 1) % loop.Length];
                    if (directed.Contains(Dir(b, a))) continue;
                    boundary.Add((a, b));
                    next[a] = b;
                    prev[b] = a;
                }
            }

            if (boundary.Count == 0) return result;
            var vertNormal = new Dictionary<int, Vec3>();
            foreach (int f in sel)
            {
                var n = m.FaceNormalRaw(f);
                foreach (int v in m.Faces[f]) vertNormal[v] = (vertNormal.TryGetValue(v, out var acc) ? acc : Vec3.Zero) + n;
            }

            var newIndex = new Dictionary<int, int>();
            foreach (var (a, _) in boundary)
            {
                if (newIndex.ContainsKey(a) || !prev.ContainsKey(a) || !next.ContainsKey(a)) continue;
                var p = m.Vertices[prev[a]];
                var v = m.Vertices[a];
                var q = m.Vertices[next[a]];
                var n = vertNormal[a].Normalized();
                var in1 = n.Cross((v - p).Normalized()).Normalized();
                var in2 = n.Cross((q - v).Normalized()).Normalized();
                double denom = 1 + in1.Dot(in2);
                var offset = denom > 0.05 ? (in1 + in2) * (t / denom) : (in1 + in2).Normalized() * t;
                newIndex[a] = m.AddVertex(v + offset);
            }

            foreach (var (a, b) in boundary)
            {
                if (!newIndex.TryGetValue(a, out int a2) || !newIndex.TryGetValue(b, out int b2)) continue;
                m.Faces.Add(new[] { a, b, b2, a2 });
            }

            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++)
                {
                    if (newIndex.TryGetValue(loop[k], out int nv)) loop[k] = nv;
                }
            }

            result.Faces.AddRange(sel);
            result.Vertices.AddRange(VerticesOfFaces(m, sel));
            result.Direction = AverageNormal(m, sel);
            return result;
        }

        // ------------------------------------------------------------------ delete / merge

        public static void DeleteFaces(PolyMesh m, IEnumerable<int> faces)
        {
            var del = new HashSet<int>(faces);
            var kept = new List<int[]>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                if (!del.Contains(f)) kept.Add(m.Faces[f]);
            }

            m.Faces.Clear();
            m.Faces.AddRange(kept);
            m.RemoveUnusedVertices();
        }

        public static void DeleteVertices(PolyMesh m, IEnumerable<int> verts)
        {
            var del = new HashSet<int>(verts);
            m.Faces.RemoveAll(f => f.Any(del.Contains));
            m.RemoveUnusedVertices();
        }

        public static void DeleteEdges(PolyMesh m, IEnumerable<(int A, int B)> edges)
        {
            var del = new HashSet<long>(edges.Select(e => PolyMesh.EdgeKey(e.A, e.B)));
            m.Faces.RemoveAll(f =>
            {
                for (int k = 0; k < f.Length; k++)
                {
                    if (del.Contains(PolyMesh.EdgeKey(f[k], f[(k + 1) % f.Length]))) return true;
                }

                return false;
            });
            m.RemoveUnusedVertices();
        }

        /// <summary>Collapses the vertices into one at <paramref name="at"/> (default: their centre). Returns the surviving vertex.</summary>
        public static int MergeVertices(PolyMesh m, IEnumerable<int> verts, Vec3? at = null)
        {
            var set = verts.Distinct().Where(v => v >= 0 && v < m.Vertices.Count).ToList();
            if (set.Count == 0) return -1;
            var c = Vec3.Zero;
            foreach (int v in set) c += m.Vertices[v];
            c /= set.Count;
            int keep = set[0];
            m.Vertices[keep] = at ?? c;
            var s = new HashSet<int>(set);
            for (int fi = m.Faces.Count - 1; fi >= 0; fi--)
            {
                var f = m.Faces[fi];
                for (int k = 0; k < f.Length; k++)
                {
                    if (s.Contains(f[k])) f[k] = keep;
                }

                var clean = PolyMesh.CleanLoop(f);
                if (clean == null) m.Faces.RemoveAt(fi);
                else m.Faces[fi] = clean;
            }

            // keep index stable: removing unused vertices may shift it
            var p = m.Vertices[keep];
            m.RemoveUnusedVertices();
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                if (m.Vertices[i].X == p.X && m.Vertices[i].Y == p.Y && m.Vertices[i].Z == p.Z) return i;
            }

            return -1;
        }

        /// <summary>"Merge by distance": welds vertices closer than <paramref name="distance"/>. Returns the number removed.</summary>
        public static int MergeByDistance(PolyMesh m, double distance) => m.Weld(distance);

        // ------------------------------------------------------------------ fill

        /// <summary>Creates a face through the selected vertices (ordered around their centre). Returns the face index or −1.</summary>
        public static int Fill(PolyMesh m, IEnumerable<int> verts)
        {
            var list = verts.Distinct().ToList();
            if (list.Count < 3) return -1;
            var pts = list.Select(v => m.Vertices[v]).ToList();
            var c = Vec3.Zero;
            foreach (var p in pts) c += p;
            c /= pts.Count;
            // best-fit normal: Newell over the convex ordering in a rough plane
            var n = Vec3.Zero;
            for (int i = 0; i < pts.Count; i++)
            {
                for (int j = i + 1; j < pts.Count; j++)
                {
                    var cr = (pts[i] - c).Cross(pts[j] - c);
                    n += cr.Dot(n) >= 0 ? cr : -cr;
                }
            }

            if (n.Length < 1e-12) return -1;
            var (u, w) = Triangulator.Basis(n);
            var ordered = list.OrderBy(v => Math.Atan2((m.Vertices[v] - c).Dot(w), (m.Vertices[v] - c).Dot(u))).ToArray();
            // orient against existing neighbours: an existing face using a→b means we must use b→a
            var existing = new HashSet<long>();
            foreach (var f in m.Faces)
                for (int k = 0; k < f.Length; k++) existing.Add(Dir(f[k], f[(k + 1) % f.Length]));
            int same = 0, opposite = 0;
            for (int k = 0; k < ordered.Length; k++)
            {
                int a = ordered[k], b = ordered[(k + 1) % ordered.Length];
                if (existing.Contains(Dir(a, b))) same++;
                if (existing.Contains(Dir(b, a))) opposite++;
            }

            if (same > opposite) Array.Reverse(ordered);
            return m.AddFace(ordered);
        }

        // ------------------------------------------------------------------ subdivide

        /// <summary>Splits each selected face into quads around its centre (edge midpoints are shared with neighbours).</summary>
        public static EditResult Subdivide(PolyMesh m, IEnumerable<int>? faces = null)
        {
            var result = new EditResult();
            var sel = faces == null ? new HashSet<int>(Enumerable.Range(0, m.Faces.Count)) : new HashSet<int>(faces.Where(f => f >= 0 && f < m.Faces.Count));
            if (sel.Count == 0) return result;
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long k = PolyMesh.EdgeKey(a, b);
                if (!mid.TryGetValue(k, out int v)) mid[k] = v = m.AddVertex((m.Vertices[a] + m.Vertices[b]) * 0.5);
                return v;
            }

            var newFaces = new List<int[]>();
            var replaced = new List<int>();
            foreach (int f in sel)
            {
                var loop = m.Faces[f];
                int n = loop.Length;
                int c = m.AddVertex(m.FaceCenter(f));
                for (int i = 0; i < n; i++)
                {
                    int a = loop[i];
                    int mNext = Mid(a, loop[(i + 1) % n]);
                    int mPrev = Mid(loop[(i - 1 + n) % n], a);
                    newFaces.Add(new[] { a, mNext, c, mPrev });
                }
            }

            // Insert midpoints into unselected neighbours so the surface stays watertight.
            for (int f = 0; f < m.Faces.Count; f++)
            {
                if (sel.Contains(f)) continue;
                var loop = m.Faces[f];
                List<int>? nl = null;
                for (int k = 0; k < loop.Length; k++)
                {
                    int a = loop[k], b = loop[(k + 1) % loop.Length];
                    if (mid.TryGetValue(PolyMesh.EdgeKey(a, b), out int mv))
                    {
                        if (nl == null)
                        {
                            nl = new List<int>();
                            for (int j = 0; j < k; j++) nl.Add(loop[j]);
                        }

                        nl.Add(a);
                        nl.Add(mv);
                    }
                    else nl?.Add(a);
                }

                if (nl != null) m.Faces[f] = nl.ToArray();
            }

            var keep = new List<int[]>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                if (!sel.Contains(f)) keep.Add(m.Faces[f]);
            }

            m.Faces.Clear();
            m.Faces.AddRange(keep);
            int start = m.Faces.Count;
            m.Faces.AddRange(newFaces);
            for (int i = start; i < m.Faces.Count; i++) result.Faces.Add(i);
            result.Vertices.AddRange(VerticesOfFaces(m, result.Faces));
            return result;
        }

        /// <summary>One level of Catmull–Clark subdivision of the whole mesh (smooths and quadrangulates).</summary>
        public static PolyMesh CatmullClark(PolyMesh m)
        {
            int vc = m.Vertices.Count;
            var r = new PolyMesh();
            var facePts = new Vec3[m.Faces.Count];
            for (int f = 0; f < m.Faces.Count; f++) facePts[f] = m.FaceCenter(f);
            var edgeFaces = m.EdgeFaces();
            var edgePoint = new Dictionary<long, Vec3>();
            foreach (var kv in edgeFaces)
            {
                var (a, b) = PolyMesh.EdgeOf(kv.Key);
                var mp = (m.Vertices[a] + m.Vertices[b]) * 0.5;
                edgePoint[kv.Key] = kv.Value.Count == 2 ? (m.Vertices[a] + m.Vertices[b] + facePts[kv.Value[0]] + facePts[kv.Value[1]]) * 0.25 : mp;
            }

            // vertex rules
            var vFaces = m.VertexFaces();
            var vEdges = new List<long>[vc];
            for (int i = 0; i < vc; i++) vEdges[i] = new List<long>();
            foreach (var k in edgeFaces.Keys)
            {
                var (a, b) = PolyMesh.EdgeOf(k);
                vEdges[a].Add(k);
                vEdges[b].Add(k);
            }

            var newV = new Vec3[vc];
            for (int v = 0; v < vc; v++)
            {
                var p = m.Vertices[v];
                var boundaryEdges = vEdges[v].Where(k => edgeFaces[k].Count != 2).ToList();
                if (boundaryEdges.Count >= 2)
                {
                    var sum = Vec3.Zero;
                    foreach (var k in boundaryEdges.Take(2))
                    {
                        var (a, b) = PolyMesh.EdgeOf(k);
                        sum += m.Vertices[a == v ? b : a];
                    }

                    newV[v] = p * 0.75 + sum * 0.125;
                    continue;
                }

                int n = vFaces[v].Count;
                if (n < 3 || vEdges[v].Count == 0)
                {
                    newV[v] = p;
                    continue;
                }

                var q = Vec3.Zero;
                foreach (int f in vFaces[v]) q += facePts[f];
                q /= n;
                var rr = Vec3.Zero;
                foreach (var k in vEdges[v])
                {
                    var (a, b) = PolyMesh.EdgeOf(k);
                    rr += (m.Vertices[a] + m.Vertices[b]) * 0.5;
                }

                rr /= vEdges[v].Count;
                newV[v] = (q + rr * 2 + p * (n - 3)) / n;
            }

            r.Vertices.AddRange(newV);
            var edgeIndex = new Dictionary<long, int>();
            foreach (var kv in edgePoint) edgeIndex[kv.Key] = r.AddVertex(kv.Value);
            for (int f = 0; f < m.Faces.Count; f++)
            {
                int c = r.AddVertex(facePts[f]);
                var loop = m.Faces[f];
                int n = loop.Length;
                for (int i = 0; i < n; i++)
                {
                    int a = loop[i];
                    int en = edgeIndex[PolyMesh.EdgeKey(a, loop[(i + 1) % n])];
                    int ep = edgeIndex[PolyMesh.EdgeKey(loop[(i - 1 + n) % n], a)];
                    r.AddFace(a, en, c, ep);
                }
            }

            r.RemoveUnusedVertices();
            return r;
        }

        // ------------------------------------------------------------------ normals

        /// <summary>
        /// Makes face orientation consistent across every connected part and points it outwards
        /// (positive volume). Returns the number of faces flipped.
        /// </summary>
        public static int RecalculateNormals(PolyMesh m)
        {
            int fc = m.Faces.Count;
            var edgeFaces = m.EdgeFaces();
            var flip = new bool[fc];
            var visited = new bool[fc];
            int flipped = 0;
            for (int seed = 0; seed < fc; seed++)
            {
                if (visited[seed]) continue;
                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(seed);
                visited[seed] = true;
                while (queue.Count > 0)
                {
                    int f = queue.Dequeue();
                    component.Add(f);
                    var loop = m.Faces[f];
                    for (int k = 0; k < loop.Length; k++)
                    {
                        int a = loop[k], b = loop[(k + 1) % loop.Length];
                        if (flip[f]) (a, b) = (b, a);
                        var nb = edgeFaces[PolyMesh.EdgeKey(a, b)];
                        if (nb.Count != 2) continue;
                        int g = nb[0] == f ? nb[1] : nb[0];
                        if (g == f || visited[g]) continue;
                        // g must traverse the edge b→a
                        bool gHasAB = HasDirected(m.Faces[g], a, b);
                        flip[g] = gHasAB;
                        visited[g] = true;
                        queue.Enqueue(g);
                    }
                }

                // orientation of the component: signed volume about its centre
                var c = Vec3.Zero;
                int cnt = 0;
                foreach (int f in component)
                {
                    foreach (int v in m.Faces[f])
                    {
                        c += m.Vertices[v];
                        cnt++;
                    }
                }

                c /= Math.Max(1, cnt);
                double vol = 0;
                foreach (int f in component)
                {
                    var n = m.FaceNormalRaw(f);
                    if (flip[f]) n = -n;
                    vol += n.Dot(m.FaceCenter(f) - c);
                }

                if (vol < 0)
                {
                    foreach (int f in component) flip[f] = !flip[f];
                }
            }

            for (int f = 0; f < fc; f++)
            {
                if (!flip[f]) continue;
                m.FlipFace(f);
                flipped++;
            }

            return flipped;
        }

        private static bool HasDirected(int[] loop, int a, int b)
        {
            for (int k = 0; k < loop.Length; k++)
            {
                if (loop[k] == a && loop[(k + 1) % loop.Length] == b) return true;
            }

            return false;
        }

        public static void FlipFaces(PolyMesh m, IEnumerable<int> faces)
        {
            foreach (int f in faces.Distinct())
            {
                if (f >= 0 && f < m.Faces.Count) m.FlipFace(f);
            }
        }

        /// <summary>Splits every selected polygon into triangles.</summary>
        public static void TriangulateFaces(PolyMesh m, IEnumerable<int> faces)
        {
            var sel = new HashSet<int>(faces);
            var result = new List<int[]>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var loop = m.Faces[f];
                if (!sel.Contains(f) || loop.Length <= 3)
                {
                    result.Add(loop);
                    continue;
                }

                var pts = loop.Select(i => m.Vertices[i]).ToList();
                var tri = Triangulator.TriangulatePolygon(pts, PolyMesh.NewellNormal(pts));
                for (int t = 0; t + 2 < tri.Count; t += 3) result.Add(new[] { loop[tri[t]], loop[tri[t + 1]], loop[tri[t + 2]] });
            }

            m.Faces.Clear();
            m.Faces.AddRange(result);
        }
    }
}
