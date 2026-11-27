using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [spin] Blender's Spin: the selected faces (or, with no face, the selected edges) swept round an axis in steps,
    /// each step a copy turned a little further, joined to the one before by side faces. A whole turn closes on itself;
    /// a part of a turn ends in a copy of the selection (and, for a free profile, starts in the original turned to face
    /// back). Points on the axis stay single, so a profile touching the axis closes to a point there (a lathe).
    /// </summary>
    public static class MeshSpin
    {
        public static EditResult Spin(PolyMesh m, IEnumerable<int> faces, IEnumerable<(int A, int B)> edges, Vec3 center, Vec3 axis, double degrees, int steps)
        {
            var result = new EditResult();
            steps = Math.Max(1, steps);
            var ax = axis.Normalized();
            if (ax.Length < 0.5 || Math.Abs(degrees) < 1e-9) return result;
            bool full = Math.Abs(Math.Abs(degrees) - 360) < 1e-6 || Math.Abs(degrees) > 360;
            if (full) degrees = 360 * Math.Sign(degrees);
            var region = new HashSet<int>(faces.Where(f => f >= 0 && f < m.Faces.Count));
            var sides = new List<(int A, int B)>();
            if (region.Count > 0)
            {
                var directed = new HashSet<long>();
                foreach (int f in region)
                {
                    var loop = m.Faces[f];
                    for (int k = 0; k < loop.Length; k++) directed.Add(Key(loop[k], loop[(k + 1) % loop.Length]));
                }

                foreach (int f in region)
                {
                    var loop = m.Faces[f];
                    for (int k = 0; k < loop.Length; k++)
                    {
                        int a = loop[k], b = loop[(k + 1) % loop.Length];
                        if (!directed.Contains(Key(b, a))) sides.Add((a, b));   // a boundary edge of the region, as its face runs
                    }
                }
            }
            else
            {
                sides.AddRange(edges.Where(e => e.A >= 0 && e.B >= 0 && e.A < m.Vertices.Count && e.B < m.Vertices.Count && e.A != e.B));
            }

            if (sides.Count == 0) return result;
            var verts = new HashSet<int>(sides.SelectMany(e => new[] { e.A, e.B }));
            if (region.Count > 0) verts.UnionWith(MeshEdit.VerticesOfFaces(m, region));
            bool OnAxis(int v)
            {
                var d = m.Vertices[v] - center;
                return (d - ax * d.Dot(ax)).Length < 1e-9;
            }

            // the rings: ring[k][v] is vertex v turned k steps (the original for k = 0; back to it after a whole turn)
            var rings = new List<Dictionary<int, int>> { verts.ToDictionary(v => v, v => v) };
            for (int k = 1; k <= steps; k++)
            {
                if (full && k == steps)
                {
                    rings.Add(rings[0]);
                    break;
                }

                var turn = Quat.FromAxisAngle(ax, degrees * k / steps * Math.PI / 180);
                var ring = new Dictionary<int, int>();
                foreach (int v in verts) ring[v] = OnAxis(v) ? v : m.AddVertex(center + turn.Rotate(m.Vertices[v] - center));
                rings.Add(ring);
            }

            // faces swept forward along the region's normal keep their winding; swept backward, they turn round
            bool forward = true;
            if (region.Count > 0)
            {
                var n = MeshEdit.AverageNormal(m, region);
                var c = MeshEdit.VerticesOfFaces(m, region).Aggregate(Vec3.Zero, (s, v) => s + m.Vertices[v]) / MeshEdit.VerticesOfFaces(m, region).Count;
                forward = n.Dot(ax.Cross(c - center)) * Math.Sign(degrees) >= 0;
            }

            // a side face; where a corner stays on the axis it closes to a triangle (or to nothing)
            void Add(params int[] corners)
            {
                var unique = new List<int>();
                foreach (int c in corners)
                    if (unique.Count == 0 || unique[unique.Count - 1] != c) unique.Add(c);
                while (unique.Count > 1 && unique[0] == unique[unique.Count - 1]) unique.RemoveAt(unique.Count - 1);
                if (unique.Count < 3 || unique.Distinct().Count() != unique.Count) return;
                if (!forward) unique.Reverse();
                result.Faces.Add(m.AddFace(unique.ToArray()));
            }

            for (int k = 1; k < rings.Count; k++)
            {
                var r0 = rings[k - 1];
                var r1 = rings[k];
                foreach (var (a, b) in sides) Add(r0[a], r0[b], r1[b], r1[a]);
            }

            if (region.Count > 0)
            {
                bool attached = Enumerable.Range(0, m.Faces.Count).Any(f => !region.Contains(f) && !result.Faces.Contains(f) && m.Faces[f].Any(MeshEdit.VerticesOfFaces(m, region).Contains));
                var originals = region.Select(f => m.Faces[f]).ToList();
                if (!full)
                {
                    var last = rings[rings.Count - 1];
                    foreach (var loop in originals)
                    {
                        var cap = loop.Select(v => last[v]).ToArray();
                        if (!forward) Array.Reverse(cap);
                        result.Faces.Add(m.AddFace(cap));
                    }
                }

                // the original faces: inside the swept solid when the region is part of a mesh or the turn is whole;
                // a free profile's start, turned to face back
                if (attached || full)
                {
                    foreach (int f in region.OrderByDescending(f => f)) m.Faces.RemoveAt(f);
                    result.Faces.Clear();   // indices moved: select the end ring below instead
                }
                else if (forward)
                {
                    foreach (int f in region) Array.Reverse(m.Faces[f]);
                }
                else
                {
                    // swept backward the side faces were turned round: the original start already faces back
                }
            }

            var end = rings[rings.Count - 1];
            result.Vertices.AddRange(end.Values.Distinct());
            if (result.Faces.Count == 0) result.Faces.AddRange(MeshEdit.FacesOfVertices(m, new HashSet<int>(end.Values)));
            return result;
        }

        private static long Key(int a, int b) => (long)a << 32 | (uint)b;
    }
}
