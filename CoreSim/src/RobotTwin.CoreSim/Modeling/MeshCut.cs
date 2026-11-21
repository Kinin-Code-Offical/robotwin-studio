using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>What <see cref="MeshCut.Bisect"/> keeps.</summary>
    public enum BisectMode
    {
        /// <summary>Cut the faces along the plane and keep both sides (a new edge loop).</summary>
        Cut,

        /// <summary>Delete the part on the side the plane normal points to.</summary>
        ClearPositive,

        /// <summary>Delete the part behind the plane.</summary>
        ClearNegative
    }

    /// <summary>
    /// Cutting tools of the modeller (Blender's Bisect, Knife through, Loop Cut, Solidify and edge extrude)
    /// on <see cref="PolyMesh"/>. Lengths are millimetres like the rest of the modeller.
    /// </summary>
    public static class MeshCut
    {
        // ================================================================ bisect / slice

        /// <summary>
        /// Cuts faces with the plane through <paramref name="point"/> with normal <paramref name="normal"/>.
        /// Only <paramref name="faces"/> are cut (all when null); neighbouring faces get the new vertices so the
        /// surface stays watertight. With a clear mode one side is deleted and, when <paramref name="fill"/>,
        /// the opening is capped (holes in the section stay open, e.g. the bore of a tube).
        /// The result lists the vertices on the cut.
        /// </summary>
        public static EditResult Bisect(PolyMesh m, Vec3 point, Vec3 normal, IEnumerable<int>? faces = null, BisectMode mode = BisectMode.Cut, bool fill = true)
        {
            var result = new EditResult();
            var n = normal.Normalized();
            if (n.Length < 0.5 || m.Faces.Count == 0) return result;
            double eps = 1e-7 * Math.Max(1, m.Bounds.Size.Length);
            var dist = new List<double>(m.Vertices.Count);
            for (int i = 0; i < m.Vertices.Count; i++)
            {
                double d = (m.Vertices[i] - point).Dot(n);
                dist.Add(Math.Abs(d) < eps ? 0 : d);
            }

            var sel = faces == null ? null : new HashSet<int>(faces.Where(f => f >= 0 && f < m.Faces.Count));
            var cutVerts = new Dictionary<long, int>();

            int Cross(int a, int b)
            {
                long k = PolyMesh.EdgeKey(a, b);
                if (cutVerts.TryGetValue(k, out int v)) return v;
                // from the lower index so both faces of the edge get the very same point
                int lo = Math.Min(a, b), hi = Math.Max(a, b);
                double t = dist[lo] / (dist[lo] - dist[hi]);
                var pos = m.Vertices[lo] + (m.Vertices[hi] - m.Vertices[lo]) * t;
                pos -= n * (pos - point).Dot(n);
                v = m.AddVertex(pos);
                dist.Add(0);
                cutVerts[k] = v;
                return v;
            }

            var output = new List<int[]>();
            var side = new List<int>();          // +1 / -1 / 0 (on the plane), int.MinValue = not selected
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                var f = m.Faces[fi];
                if (sel != null && !sel.Contains(fi))
                {
                    output.Add(f);
                    side.Add(int.MinValue);
                    continue;
                }

                bool pos = false, neg = false;
                foreach (int v in f)
                {
                    if (dist[v] > 0) pos = true;
                    else if (dist[v] < 0) neg = true;
                }

                if (!(pos && neg))
                {
                    output.Add(f);
                    side.Add(pos ? 1 : neg ? -1 : 0);
                    continue;
                }

                var loop = new List<int>(f.Length + 2);
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    loop.Add(a);
                    if (dist[a] * dist[b] < 0) loop.Add(Cross(a, b));
                }

                foreach (var piece in SplitLoop(loop, dist, m.Vertices, n))
                {
                    output.Add(piece);
                    side.Add(SideOf(piece, dist));
                }
            }

            // faces outside the selection that share a cut edge take the new vertex too (no T-junctions)
            if (sel != null && cutVerts.Count > 0)
            {
                for (int i = 0; i < output.Count; i++)
                {
                    if (side[i] != int.MinValue) continue;
                    output[i] = InsertOnEdges(output[i], (x, y) => cutVerts.TryGetValue(PolyMesh.EdgeKey(x, y), out int v) ? new[] { v } : null);
                }
            }

            int clear = mode == BisectMode.ClearPositive ? 1 : mode == BisectMode.ClearNegative ? -1 : 0;
            m.Faces.Clear();
            for (int i = 0; i < output.Count; i++)
            {
                int s = side[i];
                if (clear != 0 && s != int.MinValue && (s == clear || s == 0)) continue;
                m.Faces.Add(output[i]);
            }

            if (clear != 0 && fill) Cap(m, dist, n * clear);

            var onCut = new HashSet<int>();
            foreach (var f in m.Faces)
            {
                foreach (int v in f)
                {
                    if (v < dist.Count && dist[v] == 0) onCut.Add(v);
                }
            }

            var remap = Compact(m);
            foreach (int v in onCut)
            {
                if (remap[v] >= 0) result.Vertices.Add(remap[v]);
            }

            result.Direction = n;
            return result;
        }

        /// <summary>Cuts a closed mesh in two closed pieces: (in front of the plane, behind it).</summary>
        public static (PolyMesh Positive, PolyMesh Negative) Slice(PolyMesh m, Vec3 point, Vec3 normal, bool fill = true)
        {
            var a = m.Clone();
            Bisect(a, point, normal, null, BisectMode.ClearNegative, fill);
            var b = m.Clone();
            Bisect(b, point, normal, null, BisectMode.ClearPositive, fill);
            return (a, b);
        }

        private static int SideOf(int[] loop, IReadOnlyList<double> dist)
        {
            foreach (int v in loop)
            {
                if (dist[v] > 0) return 1;
                if (dist[v] < 0) return -1;
            }

            return 0;
        }

        /// <summary>
        /// Splits a face loop (with the plane crossings already inserted as vertices at distance 0) into
        /// pieces that each lie on one side. Concave faces that cross several times are split along chords
        /// paired along the cut line.
        /// </summary>
        private static List<int[]> SplitLoop(List<int> loop, IReadOnlyList<double> dist, IReadOnlyList<Vec3> verts, Vec3 n)
        {
            int count = loop.Count;
            var crossings = new List<int>();
            for (int i = 0; i < count; i++)
            {
                if (dist[loop[i]] != 0) continue;
                if (dist[loop[(i - 1 + count) % count]] == 0) continue; // not the first vertex of a run on the plane
                int before = PrevSign(loop, dist, i), after = NextSign(loop, dist, i);
                if (before != 0 && after != 0 && before != after) crossings.Add(i);
            }

            if (crossings.Count < 2 || crossings.Count % 2 != 0) return new List<int[]> { loop.ToArray() };
            var partner = new Dictionary<int, int>();
            if (crossings.Count == 2)
            {
                partner[crossings[0]] = crossings[1];
                partner[crossings[1]] = crossings[0];
            }
            else
            {
                var fn = PolyMesh.NewellNormal(loop, verts).Normalized();
                var line = n.Cross(fn);
                var sorted = crossings.OrderBy(i => verts[loop[i]].Dot(line)).ToList();
                for (int k = 0; k + 1 < sorted.Count; k += 2)
                {
                    partner[sorted[k]] = sorted[k + 1];
                    partner[sorted[k + 1]] = sorted[k];
                }
            }

            var pieces = new List<int[]>();
            var used = new bool[count];
            for (int start = 0; start < count; start++)
            {
                if (used[start]) continue;
                var piece = new List<int> { loop[start] };
                int j = start;
                bool viaBoundary;
                used[j] = true;
                j = (j + 1) % count;
                viaBoundary = true;
                int guard = 0;
                while (j != start && guard++ < 4 * count)
                {
                    piece.Add(loop[j]);
                    if (viaBoundary && partner.TryGetValue(j, out int p))
                    {
                        j = p;
                        viaBoundary = false;
                        continue;
                    }

                    used[j] = true;
                    j = (j + 1) % count;
                    viaBoundary = true;
                }

                // a chord can bring the walk back onto the start vertex: drop the duplicate
                if (piece.Count > 1 && piece[piece.Count - 1] == piece[0]) piece.RemoveAt(piece.Count - 1);
                if (piece.Count >= 3) pieces.Add(piece.ToArray());
            }

            return pieces.Count > 0 ? pieces : new List<int[]> { loop.ToArray() };
        }

        private static int PrevSign(List<int> loop, IReadOnlyList<double> dist, int i)
        {
            for (int k = 1; k < loop.Count; k++)
            {
                double d = dist[loop[(i - k + loop.Count) % loop.Count]];
                if (d != 0) return Math.Sign(d);
            }

            return 0;
        }

        private static int NextSign(List<int> loop, IReadOnlyList<double> dist, int i)
        {
            for (int k = 1; k < loop.Count; k++)
            {
                double d = dist[loop[(i + k) % loop.Count]];
                if (d != 0) return Math.Sign(d);
            }

            return 0;
        }

        /// <summary>
        /// Closes the openings left on the plane: boundary edges lying on it are chained into loops,
        /// loops are nested by containment (outer, hole, island…) and each outer loop with its holes
        /// becomes a face (one n-gon without holes, triangles with them), facing <paramref name="outward"/>.
        /// </summary>
        private static void Cap(PolyMesh m, IReadOnlyList<double> dist, Vec3 outward)
        {
            var count = new Dictionary<long, int>();
            foreach (var f in m.Faces)
            {
                for (int k = 0; k < f.Length; k++)
                {
                    long key = PolyMesh.EdgeKey(f[k], f[(k + 1) % f.Length]);
                    count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            var next = new Dictionary<int, List<int>>();
            foreach (var f in m.Faces)
            {
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (dist[a] != 0 || dist[b] != 0 || count[PolyMesh.EdgeKey(a, b)] != 1) continue;
                    // the cap runs against the face's direction along the shared edge
                    if (!next.TryGetValue(b, out var l)) next[b] = l = new List<int>(1);
                    l.Add(a);
                }
            }

            var loops = new List<List<int>>();
            while (next.Count > 0)
            {
                int start = next.Keys.First();
                var loop = new List<int> { start };
                int cur = start;
                int guard = 0;
                while (guard++ < 100000)
                {
                    if (!next.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                    int nx = outs[outs.Count - 1];
                    outs.RemoveAt(outs.Count - 1);
                    if (outs.Count == 0) next.Remove(cur);
                    if (nx == start) break;
                    loop.Add(nx);
                    cur = nx;
                }

                if (loop.Count >= 3) loops.Add(loop);
            }

            if (loops.Count == 0) return;
            var (u, v) = Triangulator.Basis(outward);
            var flat = loops.Select(l => l.Select(i => new Vec2(m.Vertices[i].Dot(u), m.Vertices[i].Dot(v))).ToList()).ToList();
            var depth = new int[loops.Count];
            for (int i = 0; i < loops.Count; i++)
            {
                for (int j = 0; j < loops.Count; j++)
                {
                    if (i != j && Inside(flat[i][0], flat[j])) depth[i]++;
                }
            }

            for (int i = 0; i < loops.Count; i++)
            {
                if (depth[i] % 2 != 0) continue; // a hole: handled with its outer loop
                var holes = new List<int>();
                for (int j = 0; j < loops.Count; j++)
                {
                    if (depth[j] == depth[i] + 1 && Inside(flat[j][0], flat[i])) holes.Add(j);
                }

                if (holes.Count == 0)
                {
                    var face = loops[i].ToArray();
                    if (PolyMesh.NewellNormal(face, m.Vertices).Dot(outward) < 0) Array.Reverse(face);
                    m.Faces.Add(face);
                    continue;
                }

                var all = new List<int>(loops[i]);
                var holeRings = new List<IReadOnlyList<Vec2>>();
                foreach (int h in holes)
                {
                    all.AddRange(loops[h]);
                    holeRings.Add(flat[h]);
                }

                var tris = Triangulator.Triangulate(flat[i], holeRings);
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    var tri = new[] { all[tris[t]], all[tris[t + 1]], all[tris[t + 2]] };
                    if (PolyMesh.NewellNormal(tri, m.Vertices).Dot(outward) < 0) Array.Reverse(tri);
                    m.Faces.Add(tri);
                }
            }
        }

        private static bool Inside(Vec2 p, IReadOnlyList<Vec2> ring)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }

            return inside;
        }

        // ================================================================ loop cut

        /// <summary>
        /// The edge ring through edge (a, b): the edge, the opposite edges of the quads it runs across,
        /// and those quads. The ring stops at a face that is not a quad, at an open border or where it closes.
        /// </summary>
        public static (List<(int A, int B)> Edges, List<int> Faces) EdgeRing(PolyMesh m, int a, int b)
        {
            var edges = new List<(int, int)> { (a, b) };
            var faces = new List<int>();
            var ef = m.EdgeFaces();
            long startKey = PolyMesh.EdgeKey(a, b);
            if (!ef.TryGetValue(startKey, out var first)) return (edges, faces);
            var seenEdges = new HashSet<long> { startKey };
            var seenFaces = new HashSet<int>();
            for (int dir = 0; dir < Math.Min(2, first.Count); dir++)
            {
                int face = first[dir];
                int s0 = a, s1 = b;
                var chain = new List<(int, int)>();
                var chainFaces = new List<int>();
                while (true)
                {
                    if (seenFaces.Contains(face)) break;
                    var q = m.Faces[face];
                    if (q.Length != 4) break;
                    int i = Array.FindIndex(q, x => x == s0 || x == s1);
                    if (i < 0) break;
                    // index of the ring edge inside the quad
                    int at = -1;
                    for (int k = 0; k < 4; k++)
                    {
                        int x = q[k], y = q[(k + 1) % 4];
                        if ((x == s0 && y == s1) || (x == s1 && y == s0))
                        {
                            at = k;
                            break;
                        }
                    }

                    if (at < 0) break;
                    seenFaces.Add(face);
                    chainFaces.Add(face);
                    int o0 = q[(at + 3) % 4], o1 = q[(at + 2) % 4];
                    if (q[at] != s0) (o0, o1) = (o1, o0);
                    long key = PolyMesh.EdgeKey(o0, o1);
                    if (seenEdges.Contains(key)) break; // the ring closed
                    seenEdges.Add(key);
                    chain.Add((o0, o1));
                    if (!ef.TryGetValue(key, out var nf)) break;
                    int nextFace = -1;
                    foreach (int f in nf)
                    {
                        if (f != face && !seenFaces.Contains(f))
                        {
                            nextFace = f;
                            break;
                        }
                    }

                    if (nextFace < 0) break;
                    face = nextFace;
                    s0 = o0;
                    s1 = o1;
                }

                if (dir == 0)
                {
                    edges.AddRange(chain);
                    faces.AddRange(chainFaces);
                }
                else
                {
                    chain.Reverse();
                    chainFaces.Reverse();
                    edges.InsertRange(0, chain);
                    faces.InsertRange(0, chainFaces);
                }
            }

            return (edges, faces);
        }

        /// <summary>
        /// Loop cut (Ctrl+R): splits every quad of the edge ring through (a, b) with <paramref name="cuts"/>
        /// evenly spaced edge loops. Faces at the ends of the ring get the new vertices on their edge.
        /// The result lists the new vertices.
        /// </summary>
        public static EditResult LoopCut(PolyMesh m, int a, int b, int cuts = 1)
        {
            var result = new EditResult();
            cuts = Math.Max(1, Math.Min(64, cuts));
            var (edges, ringFaces) = EdgeRing(m, a, b);
            if (ringFaces.Count == 0) return result;
            var onEdge = new Dictionary<long, (int From, int[] Verts)>();
            foreach (var (s0, s1) in edges)
            {
                long key = PolyMesh.EdgeKey(s0, s1);
                if (onEdge.ContainsKey(key)) continue;
                var vs = new int[cuts];
                for (int k = 0; k < cuts; k++)
                {
                    double t = (k + 1.0) / (cuts + 1.0);
                    vs[k] = m.AddVertex(m.Vertices[s0] + (m.Vertices[s1] - m.Vertices[s0]) * t);
                    result.Vertices.Add(vs[k]);
                }

                onEdge[key] = (s0, vs);
            }

            int[] Along(int from, int to)
            {
                var e = onEdge[PolyMesh.EdgeKey(from, to)];
                if (e.From == from) return e.Verts;
                var r = (int[])e.Verts.Clone();
                Array.Reverse(r);
                return r;
            }

            var ring = new HashSet<int>(ringFaces);
            var output = new List<int[]>();
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                var f = m.Faces[fi];
                if (!ring.Contains(fi))
                {
                    output.Add(InsertOnEdges(f, (x, y) => onEdge.ContainsKey(PolyMesh.EdgeKey(x, y)) ? Along(x, y) : null));
                    continue;
                }

                int at = -1;
                for (int k = 0; k < 4; k++)
                {
                    if (onEdge.ContainsKey(PolyMesh.EdgeKey(f[k], f[(k + 1) % 4])) && onEdge.ContainsKey(PolyMesh.EdgeKey(f[(k + 2) % 4], f[(k + 3) % 4])))
                    {
                        at = k;
                        break;
                    }
                }

                if (at < 0)
                {
                    output.Add(f);
                    continue;
                }

                int A = f[at], B = f[(at + 1) % 4], C = f[(at + 2) % 4], D = f[(at + 3) % 4];
                var ab = Along(A, B);
                var dc = Along(D, C);
                var rowA = new List<int> { A };
                rowA.AddRange(ab);
                rowA.Add(B);
                var rowD = new List<int> { D };
                rowD.AddRange(dc);
                rowD.Add(C);
                for (int k = 0; k + 1 < rowA.Count; k++) output.Add(new[] { rowA[k], rowA[k + 1], rowD[k + 1], rowD[k] });
            }

            m.Faces.Clear();
            m.Faces.AddRange(output);
            return result;
        }

        // ================================================================ solidify / edge extrude

        /// <summary>
        /// Solidify: gives the surface a wall of <paramref name="thickness"/> mm, offset inward (against the
        /// normals) with even thickness at corners. Open surfaces get a rim along their border; a closed body
        /// becomes a hollow shell. The result lists the new inner vertices.
        /// </summary>
        public static EditResult Solidify(PolyMesh m, double thickness)
        {
            var result = new EditResult();
            if (m.Faces.Count == 0 || Math.Abs(thickness) < 1e-9) return result;
            int n = m.Vertices.Count;
            var fn = m.Faces.Select((_, i) => m.FaceNormal(i)).ToArray();
            var vf = m.VertexFaces();
            var count = new Dictionary<long, int>();
            foreach (var f in m.Faces)
            {
                for (int k = 0; k < f.Length; k++)
                {
                    long key = PolyMesh.EdgeKey(f[k], f[(k + 1) % f.Length]);
                    count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            for (int i = 0; i < n; i++)
            {
                var dir = Vec3.Zero;
                foreach (int f in vf[i]) dir += fn[f];
                dir = dir.Normalized();
                double scale = 1;
                if (vf[i].Count > 0)
                {
                    double minCos = vf[i].Min(f => fn[f].Dot(dir));
                    scale = 1 / Math.Max(0.25, minCos);
                }

                result.Vertices.Add(m.AddVertex(m.Vertices[i] - dir * (thickness * scale)));
            }

            int faces = m.Faces.Count;
            var rims = new List<int[]>();
            for (int fi = 0; fi < faces; fi++)
            {
                var f = m.Faces[fi];
                var inner = f.Select(v => v + n).Reverse().ToArray();
                m.Faces.Add(inner);
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (count[PolyMesh.EdgeKey(a, b)] == 1) rims.Add(new[] { b, a, a + n, b + n });
                }
            }

            m.Faces.AddRange(rims);
            return result;
        }

        /// <summary>
        /// Extrudes edges into new faces: every edge gets a quad to a copy of its two vertices; the copies
        /// (returned) are then moved by the caller. Quads continue the surface of the face next to the edge.
        /// </summary>
        public static EditResult ExtrudeEdges(PolyMesh m, IEnumerable<(int A, int B)> edges)
        {
            var result = new EditResult();
            var list = edges.Distinct().ToList();
            if (list.Count == 0) return result;
            var directed = new HashSet<long>();
            foreach (var f in m.Faces)
            {
                for (int k = 0; k < f.Length; k++) directed.Add((long)f[k] << 32 | (uint)f[(k + 1) % f.Length]);
            }

            var dup = new Dictionary<int, int>();
            int Dup(int v)
            {
                if (!dup.TryGetValue(v, out int c)) dup[v] = c = m.AddVertex(m.Vertices[v]);
                return c;
            }

            foreach (var (a, b) in list)
            {
                bool forward = directed.Contains((long)a << 32 | (uint)b);
                if (forward) m.Faces.Add(new[] { b, a, Dup(a), Dup(b) });
                else m.Faces.Add(new[] { a, b, Dup(b), Dup(a) });
            }

            result.Vertices.AddRange(dup.Values);
            return result;
        }

        // ================================================================ bevel

        /// <summary>
        /// Bevel (Ctrl+B): replaces the selected edges with flat chamfer faces <paramref name="width"/> mm
        /// wide on each side. Every face corner at a bevelled vertex moves inside its face so that it sits
        /// <paramref name="width"/> from each selected edge of that face; where three or more chamfers meet
        /// a corner patch closes the hole. The result lists the vertices of the new faces.
        /// </summary>
        public static EditResult Bevel(PolyMesh m, IEnumerable<(int A, int B)> edges, double width)
        {
            var result = new EditResult();
            var sel = new HashSet<long>(edges.Select(e => PolyMesh.EdgeKey(e.A, e.B)));
            if (sel.Count == 0 || width <= 0) return result;
            var ef = m.EdgeFaces();
            sel.RemoveWhere(k => !ef.TryGetValue(k, out var l) || l.Count != 2); // only edges between two faces
            if (sel.Count == 0) return result;

            var bevelVerts = new HashSet<int>();
            foreach (long k in sel)
            {
                var (a, b) = PolyMesh.EdgeOf(k);
                bevelVerts.Add(a);
                bevelVerts.Add(b);
            }

            // corner points: (face, vertex) → new vertex index
            var corner = new Dictionary<(int Face, int Vert), int>();
            var onEdge = new Dictionary<(int Vert, int Other), List<Vec3>>(); // points that one-sided corners put on an unselected edge
            var oneSided = new List<(int Face, int Vert, int Other)>();
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                var f = m.Faces[fi];
                for (int k = 0; k < f.Length; k++)
                {
                    int v = f[k];
                    if (!bevelVerts.Contains(v)) continue;
                    int prev = f[(k - 1 + f.Length) % f.Length], next = f[(k + 1) % f.Length];
                    bool sp = sel.Contains(PolyMesh.EdgeKey(prev, v)), sn = sel.Contains(PolyMesh.EdgeKey(v, next));
                    if (!sp && !sn) continue;
                    var pv = m.Vertices[v];
                    var dp = m.Vertices[prev] - pv;
                    var dn = m.Vertices[next] - pv;
                    double lp = dp.Length, ln = dn.Length;
                    if (lp < 1e-12 || ln < 1e-12) continue;
                    dp /= lp;
                    dn /= ln;
                    double sin = Math.Max(0.05, dp.Cross(dn).Length);
                    double reach = width / sin;
                    // stay short of the far end of each edge so neighbouring bevels cannot cross
                    double ap = Math.Min(reach, 0.49 * lp), an = Math.Min(reach, 0.49 * ln);
                    Vec3 c;
                    if (sp && sn) c = pv + dp * ap + dn * an;
                    else if (sp) c = pv + dn * an;
                    else c = pv + dp * ap;
                    if (sp && sn)
                    {
                        corner[(fi, v)] = m.AddVertex(c);
                        continue;
                    }

                    int other = sp ? next : prev;
                    if (!onEdge.TryGetValue((v, other), out var list)) onEdge[(v, other)] = list = new List<Vec3>(2);
                    list.Add(c);
                    oneSided.Add((fi, v, other));
                }
            }

            // one point per unselected edge end, shared by the faces on both sides
            var edgePoint = new Dictionary<(int Vert, int Other), int>();
            foreach (var kv in onEdge)
            {
                var avg = Vec3.Zero;
                foreach (var p in kv.Value) avg += p;
                edgePoint[kv.Key] = m.AddVertex(avg / kv.Value.Count);
            }

            foreach (var (face, vert, other) in oneSided) corner[(face, vert)] = edgePoint[(vert, other)];

            int Point(int face, int vert, int other) =>
                edgePoint.TryGetValue((vert, other), out int p) ? p : vert;

            var output = new List<int[]>(m.Faces.Count + sel.Count);
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                var f = m.Faces[fi];
                var loop = new List<int>(f.Length + 4);
                for (int k = 0; k < f.Length; k++)
                {
                    int v = f[k];
                    if (!bevelVerts.Contains(v))
                    {
                        loop.Add(v);
                        continue;
                    }

                    if (corner.TryGetValue((fi, v), out int c))
                    {
                        loop.Add(c);
                        continue;
                    }

                    int prev = f[(k - 1 + f.Length) % f.Length], next = f[(k + 1) % f.Length];
                    int a = Point(fi, v, prev), b = Point(fi, v, next);
                    loop.Add(a);
                    if (b != a) loop.Add(b);
                }

                output.Add(Dedup(loop));
            }

            // chamfer face along every selected edge
            foreach (long key in sel)
            {
                var (a, b) = PolyMesh.EdgeOf(key);
                var faces = ef[key];
                int fl = -1, fr = -1;
                foreach (int f in faces)
                {
                    var loop = m.Faces[f];
                    for (int k = 0; k < loop.Length; k++)
                    {
                        if (loop[k] == a && loop[(k + 1) % loop.Length] == b) fl = f;
                        if (loop[k] == b && loop[(k + 1) % loop.Length] == a) fr = f;
                    }
                }

                if (fl < 0 || fr < 0) continue;
                var quad = Dedup(new List<int> { corner[(fl, b)], corner[(fl, a)], corner[(fr, a)], corner[(fr, b)] });
                if (quad.Length >= 3)
                {
                    output.Add(quad);
                    result.Faces.Add(output.Count - 1);
                    result.Vertices.AddRange(quad);
                }
            }

            // corner patches where three or more chamfers meet
            var vf = m.VertexFaces();
            foreach (int v in bevelVerts)
            {
                var ring = RingAround(m, v, vf[v]);
                if (ring == null) continue;
                var pts = new List<int>();
                foreach (int f in ring)
                {
                    var loop = m.Faces[f];
                    int k = Array.IndexOf(loop, v);
                    int prev = loop[(k - 1 + loop.Length) % loop.Length], next = loop[(k + 1) % loop.Length];
                    if (corner.TryGetValue((f, v), out int c)) pts.Add(c);
                    else
                    {
                        pts.Add(Point(f, v, next));
                        pts.Add(Point(f, v, prev));
                    }
                }

                var patch = Dedup(pts);
                if (patch.Length < 3) continue;
                var normal = Vec3.Zero;
                foreach (int f in ring) normal += m.FaceNormalRaw(f);
                if (PolyMesh.NewellNormal(patch, m.Vertices).Dot(normal) < 0) Array.Reverse(patch);
                output.Add(patch);
                result.Faces.Add(output.Count - 1);
                result.Vertices.AddRange(patch);
            }

            m.Faces.Clear();
            m.Faces.AddRange(output);
            var remap = Compact(m);
            var verts = result.Vertices.Where(v => remap[v] >= 0).Select(v => remap[v]).Distinct().ToList();
            result.Vertices.Clear();
            result.Vertices.AddRange(verts);
            return result;
        }

        /// <summary>Faces around a vertex in rotation order (stepping across each face's edge before the vertex); null at an open border.</summary>
        private static List<int>? RingAround(PolyMesh m, int v, List<int> faces)
        {
            if (faces.Count < 3) return null;
            var byOutgoing = new Dictionary<int, int>(); // first vertex after v → face
            foreach (int f in faces)
            {
                var loop = m.Faces[f];
                int k = Array.IndexOf(loop, v);
                if (k < 0) return null;
                byOutgoing[loop[(k + 1) % loop.Length]] = f;
            }

            var ring = new List<int>(faces.Count);
            int cur = faces[0];
            for (int guard = 0; guard < faces.Count; guard++)
            {
                ring.Add(cur);
                var loop = m.Faces[cur];
                int k = Array.IndexOf(loop, v);
                int prev = loop[(k - 1 + loop.Length) % loop.Length];
                if (!byOutgoing.TryGetValue(prev, out int nf)) return null; // open border
                if (nf == faces[0]) return ring.Count == faces.Count ? ring : null;
                cur = nf;
            }

            return null;
        }

        private static int[] Dedup(List<int> loop)
        {
            var r = new List<int>(loop.Count);
            foreach (int v in loop)
            {
                if (r.Count == 0 || r[r.Count - 1] != v) r.Add(v);
            }

            while (r.Count > 1 && r[0] == r[r.Count - 1]) r.RemoveAt(r.Count - 1);
            return r.ToArray();
        }

        // ================================================================ helpers

        private static int[] InsertOnEdges(int[] face, Func<int, int, int[]?> between)
        {
            List<int>? loop = null;
            for (int k = 0; k < face.Length; k++)
            {
                int a = face[k], b = face[(k + 1) % face.Length];
                var add = between(a, b);
                if (add == null || add.Length == 0)
                {
                    loop?.Add(a);
                    continue;
                }

                loop ??= new List<int>(face.Take(k));
                loop.Add(a);
                loop.AddRange(add);
            }

            return loop?.ToArray() ?? face;
        }

        /// <summary>Drops vertices no face uses; returns old index → new index (-1 when dropped).</summary>
        private static int[] Compact(PolyMesh m)
        {
            var used = new bool[m.Vertices.Count];
            foreach (var f in m.Faces)
            {
                foreach (int v in f) used[v] = true;
            }

            var remap = new int[m.Vertices.Count];
            var kept = new List<Vec3>(m.Vertices.Count);
            for (int i = 0; i < used.Length; i++)
            {
                if (!used[i])
                {
                    remap[i] = -1;
                    continue;
                }

                remap[i] = kept.Count;
                kept.Add(m.Vertices[i]);
            }

            if (kept.Count == m.Vertices.Count) return remap;
            m.Vertices.Clear();
            m.Vertices.AddRange(kept);
            foreach (var f in m.Faces)
            {
                for (int k = 0; k < f.Length; k++) f[k] = remap[f[k]];
            }

            return remap;
        }
    }
}
