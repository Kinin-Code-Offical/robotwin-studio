using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>Selection helpers of the modeller's edit mode (Blender's L, Alt+click loops, Ctrl+Numpad +/−).</summary>
    public static class MeshSelect
    {
        /// <summary>Every vertex connected to the seeds through faces (L / Ctrl+L: a whole piece).</summary>
        public static HashSet<int> Linked(PolyMesh m, IEnumerable<int> seeds)
        {
            var vf = m.VertexFaces();
            var result = new HashSet<int>();
            var stack = new Stack<int>();
            foreach (int s in seeds)
            {
                if (s >= 0 && s < m.Vertices.Count && result.Add(s)) stack.Push(s);
            }

            while (stack.Count > 0)
            {
                int v = stack.Pop();
                foreach (int f in vf[v])
                {
                    foreach (int w in m.Faces[f])
                    {
                        if (result.Add(w)) stack.Push(w);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The edge loop through edge (a, b): it continues straight through every vertex with four edges
        /// (taking the edge that shares no face with the incoming one) and stops at poles, borders or where it closes.
        /// Returns the loop's vertices in order.
        /// </summary>
        public static List<int> EdgeLoop(PolyMesh m, int a, int b)
        {
            var ef = m.EdgeFaces();
            var nb = new Dictionary<int, HashSet<int>>();
            foreach (var (x, y) in m.Edges())
            {
                if (!nb.TryGetValue(x, out var sx)) nb[x] = sx = new HashSet<int>();
                if (!nb.TryGetValue(y, out var sy)) nb[y] = sy = new HashSet<int>();
                sx.Add(y);
                sy.Add(x);
            }

            var seen = new HashSet<int> { a, b };
            var loop = new List<int> { a, b };
            loop.AddRange(Extend(a, b, a, seen, nb, ef, m.Vertices.Count, out bool closed));
            if (!closed)
            {
                var back = Extend(b, a, -1, seen, nb, ef, m.Vertices.Count, out _);
                back.Reverse();
                loop.InsertRange(0, back);
            }

            return loop;
        }

        /// <summary>Vertices after <paramref name="cur"/> going straight on; <paramref name="closed"/> when it gets back to <paramref name="first"/>.</summary>
        private static List<int> Extend(int prev, int cur, int first, HashSet<int> seen, Dictionary<int, HashSet<int>> nb, Dictionary<long, List<int>> ef, int limit, out bool closed)
        {
            closed = false;
            var path = new List<int>();
            for (int guard = 0; guard < limit; guard++)
            {
                if (!nb.TryGetValue(cur, out var around) || around.Count != 4) break;
                if (!ef.TryGetValue(PolyMesh.EdgeKey(prev, cur), out var inFaces)) break;
                int next = -1;
                bool ambiguous = false;
                foreach (int n in around)
                {
                    if (n == prev || !ef.TryGetValue(PolyMesh.EdgeKey(cur, n), out var nf) || nf.Any(inFaces.Contains)) continue;
                    if (next >= 0) ambiguous = true;
                    next = n;
                }

                if (next < 0 || ambiguous) break;
                if (next == first)
                {
                    closed = true;
                    break;
                }

                if (!seen.Add(next)) break;
                path.Add(next);
                prev = cur;
                cur = next;
            }

            return path;
        }

        /// <summary>Faces of the ring of quads crossing edge (a, b): the face loop of Alt+click in face mode.</summary>
        public static List<int> FaceLoop(PolyMesh m, int a, int b)
        {
            var (_, faces) = MeshCut.EdgeRing(m, a, b);
            return faces;
        }

        /// <summary>Select more: adds every vertex of the faces that touch the selection.</summary>
        public static HashSet<int> Grow(PolyMesh m, ISet<int> verts)
        {
            var result = new HashSet<int>(verts);
            foreach (var f in m.Faces)
            {
                if (f.Any(verts.Contains)) result.UnionWith(f);
            }

            return result;
        }

        /// <summary>Select less: drops the vertices that touch a face not fully selected.</summary>
        public static HashSet<int> Shrink(PolyMesh m, ISet<int> verts)
        {
            var result = new HashSet<int>(verts);
            foreach (var f in m.Faces)
            {
                if (f.All(verts.Contains)) continue;
                foreach (int v in f) result.Remove(v);
            }

            return result;
        }
    }
}
