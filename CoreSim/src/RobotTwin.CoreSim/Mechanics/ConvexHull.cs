using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>Plane n·x = d with outward unit normal n.</summary>
    public readonly struct HullPlane
    {
        public HullPlane(Vec3 normal, double offset)
        {
            Normal = normal;
            Offset = offset;
        }

        public Vec3 Normal { get; }
        public double Offset { get; }

        /// <summary>Positive outside.</summary>
        public double Distance(Vec3 p) => Normal.Dot(p) - Offset;
    }

    /// <summary>
    /// 3D convex hull (Quickhull). Coplanar triangles are merged into planar facets and
    /// points lying on a facet are re-checked, so a cube always yields its 8 corners
    /// and 6 facets. Degenerate input (all points on a plane or a line) gives a flat
    /// hull with <see cref="IsDegenerate"/> set.
    /// </summary>
    public sealed class ConvexHull
    {
        private ConvexHull()
        {
        }

        /// <summary>Hull vertices (subset of the input points).</summary>
        public IReadOnlyList<Vec3> Vertices { get; private set; } = Array.Empty<Vec3>();

        /// <summary>Outward-wound triangles over <see cref="Vertices"/> (facets fan-triangulated).</summary>
        public IReadOnlyList<int> Indices { get; private set; } = Array.Empty<int>();

        /// <summary>One plane per planar facet.</summary>
        public IReadOnlyList<HullPlane> Planes { get; private set; } = Array.Empty<HullPlane>();

        /// <summary>Vertex loops (counter-clockwise from outside) of the planar facets.</summary>
        public IReadOnlyList<int[]> Facets { get; private set; } = Array.Empty<int[]>();

        public bool IsDegenerate { get; private set; }
        public double Tolerance { get; private set; }

        public static ConvexHull FromMesh(MeshModel mesh, double tolerance = -1) => Compute(mesh.Vertices, tolerance);

        /// <summary>Largest signed plane distance (≤ 0 inside; outside it is a lower bound of the true distance).</summary>
        public double SignedDistance(Vec3 p)
        {
            double best = double.NegativeInfinity;
            foreach (var pl in Planes) best = Math.Max(best, pl.Distance(p));
            return best;
        }

        public bool Contains(Vec3 p, double tolerance = 0) => !IsDegenerate && SignedDistance(p) <= tolerance;

        public MeshModel ToMesh()
        {
            var m = new MeshModel { Name = "hull" };
            m.Vertices.AddRange(Vertices);
            m.Indices.AddRange(Indices);
            return m;
        }

        /// <param name="tolerance">Coplanarity tolerance in metres; negative = automatic (1e-9 of the extent, at least float round-off).</param>
        public static ConvexHull Compute(IReadOnlyList<Vec3> input, double tolerance = -1)
        {
            // Unique points only.
            var seen = new HashSet<Vec3Key>();
            var pts = new List<Vec3>(Math.Min(input.Count, 1 << 20));
            double ax = 0, ay = 0, az = 0;
            foreach (var p in input)
            {
                if (double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z)) continue;
                if (!seen.Add(new Vec3Key(p))) continue;
                pts.Add(p);
                ax = Math.Max(ax, Math.Abs(p.X));
                ay = Math.Max(ay, Math.Abs(p.Y));
                az = Math.Max(az, Math.Abs(p.Z));
            }

            var b = pts.Count > 0 ? Aabb.FromPoints(pts) : new Aabb(Vec3.Zero, Vec3.Zero);
            double extent = Math.Max(1e-300, b.Size.Length);
            double tol = tolerance >= 0 ? tolerance : Math.Max(1e-9 * extent, 3 * 2.2e-16 * (ax + ay + az) * 8);
            var hull = new ConvexHull { Tolerance = tol };
            if (pts.Count == 0) return Degenerate(hull, pts, new List<int>());

            var qh = new Quickhull(pts, tol);
            if (!qh.Build())
            {
                return Degenerate(hull, pts, qh.InitialExtremes());
            }

            qh.Finish(hull);
            return hull;
        }

        // ------------------------------------------------------------------ degenerate (flat) input

        private static ConvexHull Degenerate(ConvexHull hull, List<Vec3> pts, List<int> extremes)
        {
            hull.IsDegenerate = true;
            if (pts.Count <= 2)
            {
                hull.Vertices = pts.ToArray();
                return hull;
            }

            // Find a plane through the farthest pair and the point farthest from that line.
            int i0 = extremes.Count > 0 ? extremes[0] : 0, i1 = i0;
            double best = -1;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = (pts[i] - pts[i0]).Length;
                if (d > best)
                {
                    best = d;
                    i1 = i;
                }
            }

            var dir = (pts[i1] - pts[i0]).Normalized();
            int i2 = -1;
            best = hull.Tolerance;
            for (int i = 0; i < pts.Count; i++)
            {
                double d = (pts[i] - pts[i0]).Cross(dir).Length;
                if (d > best)
                {
                    best = d;
                    i2 = i;
                }
            }

            if (i2 < 0)
            {
                hull.Vertices = new[] { pts[i0], pts[i1] };
                return hull;
            }

            var n = (pts[i1] - pts[i0]).Cross(pts[i2] - pts[i0]).Normalized();
            var loop = Planar2DHull(pts, Enumerable.Range(0, pts.Count).ToList(), n, hull.Tolerance);
            var verts = loop.Select(i => pts[i]).ToArray();
            var idx = new List<int>();
            for (int k = 1; k + 1 < verts.Length; k++)
            {
                idx.AddRange(new[] { 0, k, k + 1 }); // front
                idx.AddRange(new[] { 0, k + 1, k }); // back
            }

            hull.Vertices = verts;
            hull.Indices = idx;
            hull.Facets = new[] { Enumerable.Range(0, verts.Length).ToArray(), Enumerable.Range(0, verts.Length).Reverse().ToArray() };
            hull.Planes = new[] { new HullPlane(n, n.Dot(verts[0])), new HullPlane(-n, -n.Dot(verts[0])) };
            return hull;
        }

        /// <summary>Counter-clockwise (about <paramref name="normal"/>) 2D hull of points on a plane, collinear points dropped.</summary>
        internal static List<int> Planar2DHull(IReadOnlyList<Vec3> pts, List<int> ids, Vec3 normal, double tol)
        {
            var u = Math.Abs(normal.X) < 0.9 ? Vec3.UnitX.Cross(normal).Normalized() : Vec3.UnitY.Cross(normal).Normalized();
            var v = normal.Cross(u);
            var p2 = ids.Select(i => (I: i, X: pts[i].Dot(u), Y: pts[i].Dot(v))).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (p2.Count < 3) return p2.Select(p => p.I).ToList();

            double Cross((int I, double X, double Y) o, (int I, double X, double Y) a, (int I, double X, double Y) c) =>
                (a.X - o.X) * (c.Y - o.Y) - (a.Y - o.Y) * (c.X - o.X);

            bool Turn((int I, double X, double Y) o, (int I, double X, double Y) a, (int I, double X, double Y) c)
            {
                // Keep a only if it is left of o→c by more than the tolerance.
                double len = Math.Sqrt((c.X - o.X) * (c.X - o.X) + (c.Y - o.Y) * (c.Y - o.Y));
                return Cross(o, a, c) > tol * Math.Max(len, 1e-300);
            }

            var lower = new List<(int I, double X, double Y)>();
            foreach (var p in p2)
            {
                while (lower.Count >= 2 && !Turn(lower[lower.Count - 2], lower[lower.Count - 1], p)) lower.RemoveAt(lower.Count - 1);
                lower.Add(p);
            }

            var upper = new List<(int I, double X, double Y)>();
            for (int i = p2.Count - 1; i >= 0; i--)
            {
                var p = p2[i];
                while (upper.Count >= 2 && !Turn(upper[upper.Count - 2], upper[upper.Count - 1], p)) upper.RemoveAt(upper.Count - 1);
                upper.Add(p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            var loop = lower.Concat(upper).Select(p => p.I).ToList();
            // Drop near-duplicate consecutive points.
            var clean = new List<int>();
            foreach (int i in loop)
            {
                if (clean.Count > 0 && (pts[clean[clean.Count - 1]] - pts[i]).Length <= tol) continue;
                clean.Add(i);
            }

            if (clean.Count > 1 && (pts[clean[0]] - pts[clean[clean.Count - 1]]).Length <= tol) clean.RemoveAt(clean.Count - 1);
            return clean;
        }

        // ------------------------------------------------------------------ quickhull

        private sealed class Face
        {
            public readonly int[] V = new int[3];
            public readonly int[] Adj = new int[3];
            public Vec3 N;
            public double D;
            public bool Alive = true;
            public List<int>? Outside;
            public int Mark;
        }

        private sealed class Quickhull
        {
            private readonly List<Vec3> _p;
            private readonly double _tol;
            private readonly List<Face> _faces = new List<Face>();
            private readonly List<int> _coplanar = new List<int>();
            private readonly Stack<int> _pending = new Stack<int>();
            private int _mark;

            public Quickhull(List<Vec3> p, double tol)
            {
                _p = p;
                _tol = tol;
            }

            public List<int> InitialExtremes()
            {
                var list = new List<int>();
                int minX = 0;
                for (int i = 1; i < _p.Count; i++)
                    if (_p[i].X < _p[minX].X)
                        minX = i;
                list.Add(minX);
                return list;
            }

            private double Dist(Face f, int i) => f.N.Dot(_p[i]) - f.D;

            private int NewFace(int a, int b, int c)
            {
                var f = new Face();
                f.V[0] = a;
                f.V[1] = b;
                f.V[2] = c;
                var n = (_p[b] - _p[a]).Cross(_p[c] - _p[a]);
                f.N = n.Normalized();
                // Centroid-based offset is more stable than a single vertex.
                f.D = f.N.Dot((_p[a] + _p[b] + _p[c]) / 3);
                _faces.Add(f);
                return _faces.Count - 1;
            }

            public bool Build()
            {
                int n = _p.Count;
                if (n < 4) return false;

                // Initial simplex from axis extremes.
                var ext = new int[6];
                for (int i = 0; i < n; i++)
                {
                    var q = _p[i];
                    if (q.X < _p[ext[0]].X) ext[0] = i;
                    if (q.X > _p[ext[1]].X) ext[1] = i;
                    if (q.Y < _p[ext[2]].Y) ext[2] = i;
                    if (q.Y > _p[ext[3]].Y) ext[3] = i;
                    if (q.Z < _p[ext[4]].Z) ext[4] = i;
                    if (q.Z > _p[ext[5]].Z) ext[5] = i;
                }

                int a = ext[0], b = ext[1];
                double best = -1;
                for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                {
                    double d = (_p[ext[i]] - _p[ext[j]]).Length;
                    if (d > best)
                    {
                        best = d;
                        a = ext[i];
                        b = ext[j];
                    }
                }

                if (best <= _tol) return false;
                var dir = (_p[b] - _p[a]).Normalized();
                int c = -1;
                best = _tol;
                for (int i = 0; i < n; i++)
                {
                    double d = (_p[i] - _p[a]).Cross(dir).Length;
                    if (d > best)
                    {
                        best = d;
                        c = i;
                    }
                }

                if (c < 0) return false;
                var nrm = (_p[b] - _p[a]).Cross(_p[c] - _p[a]).Normalized();
                int dIdx = -1;
                best = _tol;
                for (int i = 0; i < n; i++)
                {
                    double d = Math.Abs(nrm.Dot(_p[i] - _p[a]));
                    if (d > best)
                    {
                        best = d;
                        dIdx = i;
                    }
                }

                if (dIdx < 0) return false;

                // Orient so the base (a, b, c) faces away from d.
                if (nrm.Dot(_p[dIdx] - _p[a]) > 0) (b, c) = (c, b);
                int f0 = NewFace(a, b, c);
                int f1 = NewFace(a, dIdx, b);
                int f2 = NewFace(b, dIdx, c);
                int f3 = NewFace(c, dIdx, a);
                // Adjacency: edge k of a face is (V[k], V[k+1]).
                Link(f0, 0, f1, 2); // a-b
                Link(f0, 1, f2, 2); // b-c
                Link(f0, 2, f3, 2); // c-a
                Link(f1, 0, f3, 1); // a-d / d-a
                Link(f1, 1, f2, 0); // d-b / b-d
                Link(f2, 1, f3, 0); // d-c / c-d

                var all = new List<int>(n);
                for (int i = 0; i < n; i++)
                    if (i != a && i != b && i != c && i != dIdx)
                        all.Add(i);
                Assign(all, new[] { f0, f1, f2, f3 });

                // Main loop over faces that still have outside points.
                while (_pending.Count > 0)
                {
                    int fi = _pending.Pop();
                    var f = _faces[fi];
                    if (!f.Alive || f.Outside == null || f.Outside.Count == 0) continue;
                    AddPoint(fi);
                }

                return true;
            }

            private void Link(int f, int e, int g, int ge)
            {
                _faces[f].Adj[e] = g;
                _faces[g].Adj[ge] = f;
            }

            private void Assign(List<int> points, IReadOnlyList<int> faces)
            {
                foreach (int i in points)
                {
                    int bestFace = -1;
                    double bestD = _tol, maxD = double.NegativeInfinity;
                    foreach (int fi in faces)
                    {
                        double d = Dist(_faces[fi], i);
                        if (d > maxD) maxD = d;
                        if (d > bestD)
                        {
                            bestD = d;
                            bestFace = fi;
                        }
                    }

                    if (bestFace >= 0)
                    {
                        var bf = _faces[bestFace];
                        if (bf.Outside == null)
                        {
                            bf.Outside = new List<int>();
                            _pending.Push(bestFace);
                        }

                        bf.Outside.Add(i);
                    }
                    else if (maxD >= -_tol) _coplanar.Add(i);
                }
            }

            private void AddPoint(int fi)
            {
                var f0 = _faces[fi];
                // Farthest outside point is the eye.
                int eye = f0.Outside![0];
                double far = Dist(f0, eye);
                foreach (int i in f0.Outside)
                {
                    double d = Dist(f0, i);
                    if (d > far)
                    {
                        far = d;
                        eye = i;
                    }
                }

                // Visible faces and ordered horizon (iterative DFS).
                _mark++;
                var visible = new List<int> { fi };
                f0.Mark = _mark;
                var horizon = new List<(int A, int B, int Outer)>();
                var stack = new Stack<(int Face, int Start, int K)>();
                stack.Push((fi, 0, 0));
                while (stack.Count > 0)
                {
                    var (f, start, k) = stack.Pop();
                    if (k >= 3) continue;
                    stack.Push((f, start, k + 1));
                    int e = (start + k) % 3;
                    var face = _faces[f];
                    int g = face.Adj[e];
                    var gf = _faces[g];
                    if (gf.Mark == _mark) continue;
                    if (Dist(gf, eye) > _tol)
                    {
                        gf.Mark = _mark;
                        visible.Add(g);
                        int back = Array.IndexOf(gf.Adj, f);
                        stack.Push((g, (back + 1) % 3, 0));
                    }
                    else
                    {
                        horizon.Add((face.V[e], face.V[(e + 1) % 3], g));
                    }
                }

                // Orphaned outside points.
                var orphans = new List<int>();
                foreach (int v in visible)
                {
                    var vf = _faces[v];
                    vf.Alive = false;
                    if (vf.Outside != null)
                    {
                        foreach (int i in vf.Outside)
                            if (i != eye)
                                orphans.Add(i);
                    }

                    vf.Outside = null;
                }

                // Cone of new faces over the horizon.
                var created = new List<int>(horizon.Count);
                var byStart = new Dictionary<int, int>(horizon.Count);
                var byEnd = new Dictionary<int, int>(horizon.Count);
                foreach (var (a, b, outer) in horizon)
                {
                    int nf = NewFace(a, b, eye);
                    created.Add(nf);
                    var of = _faces[outer];
                    // outer face has edge (b, a)
                    for (int k = 0; k < 3; k++)
                    {
                        if (of.V[k] == b && of.V[(k + 1) % 3] == a)
                        {
                            Link(nf, 0, outer, k);
                            break;
                        }
                    }

                    byStart[a] = nf;
                    byEnd[b] = nf;
                }

                foreach (int nf in created)
                {
                    var f = _faces[nf];
                    // edge 1: (b, eye) is shared with the new face starting at b, whose edge 2 is (eye, b).
                    if (byStart.TryGetValue(f.V[1], out int next))
                    {
                        f.Adj[1] = next;
                        _faces[next].Adj[2] = nf;
                    }
                }

                Assign(orphans, created);
            }

            /// <summary>Merges coplanar triangles into facets, re-inserts on-facet points and rebuilds the hull.</summary>
            public void Finish(ConvexHull hull)
            {
                var alive = new List<int>();
                for (int i = 0; i < _faces.Count; i++)
                    if (_faces[i].Alive)
                        alive.Add(i);

                // Group coplanar neighbours (flood fill against the seed plane).
                var facetOf = new Dictionary<int, int>();
                var facets = new List<(Vec3 N, double D, HashSet<int> Pts)>();
                foreach (int seed in alive)
                {
                    if (facetOf.ContainsKey(seed)) continue;
                    var sf = _faces[seed];
                    int id = facets.Count;
                    var members = new List<int>();
                    var q = new Queue<int>();
                    q.Enqueue(seed);
                    facetOf[seed] = id;
                    while (q.Count > 0)
                    {
                        int f = q.Dequeue();
                        members.Add(f);
                        foreach (int g in _faces[f].Adj)
                        {
                            if (facetOf.ContainsKey(g) || !_faces[g].Alive) continue;
                            var gf = _faces[g];
                            if (gf.N.Dot(sf.N) < 0.5) continue;
                            bool coplanar = true;
                            foreach (int v in gf.V)
                            {
                                if (Math.Abs(Dist(sf, v)) > _tol)
                                {
                                    coplanar = false;
                                    break;
                                }
                            }

                            if (!coplanar) continue;
                            facetOf[g] = id;
                            q.Enqueue(g);
                        }
                    }

                    // Area-weighted plane of the facet.
                    var nsum = Vec3.Zero;
                    var pset = new HashSet<int>();
                    foreach (int f in members)
                    {
                        var ff = _faces[f];
                        nsum += (_p[ff.V[1]] - _p[ff.V[0]]).Cross(_p[ff.V[2]] - _p[ff.V[0]]);
                        foreach (int v in ff.V) pset.Add(v);
                    }

                    var nrm = nsum.Length > 0 ? nsum.Normalized() : sf.N;
                    double d = pset.Max(v => nrm.Dot(_p[v]));
                    facets.Add((nrm, d, pset));
                }

                // Points dropped within the tolerance of a facet may still be its corners
                // (float noise on flat CAD faces). Skipped when it would be too costly.
                if ((long)_coplanar.Count * facets.Count < 20_000_000L)
                foreach (int i in _coplanar)
                {
                    for (int f = 0; f < facets.Count; f++)
                    {
                        if (Math.Abs(facets[f].N.Dot(_p[i]) - facets[f].D) <= _tol) facets[f].Pts.Add(i);
                    }
                }

                var index = new Dictionary<int, int>();
                var verts = new List<Vec3>();
                var tris = new List<int>();
                var loops = new List<int[]>();
                var planes = new List<HullPlane>();
                int Map(int i)
                {
                    if (!index.TryGetValue(i, out int k))
                    {
                        k = verts.Count;
                        verts.Add(_p[i]);
                        index[i] = k;
                    }

                    return k;
                }

                foreach (var (n, d, pset) in facets)
                {
                    var loop = Planar2DHull(_p, pset.ToList(), n, _tol);
                    if (loop.Count < 3) continue;
                    var mapped = loop.Select(Map).ToArray();
                    loops.Add(mapped);
                    planes.Add(new HullPlane(n, d));
                    for (int k = 1; k + 1 < mapped.Length; k++)
                    {
                        tris.Add(mapped[0]);
                        tris.Add(mapped[k]);
                        tris.Add(mapped[k + 1]);
                    }
                }

                hull.Vertices = verts;
                hull.Indices = tris;
                hull.Facets = loops;
                hull.Planes = planes;
            }
        }
    }

    /// <summary>Contact points and helpers that turn imported models into simulation geometry.</summary>
    public static class ContactGeometry
    {
        /// <summary>
        /// Up to <paramref name="maxPoints"/> well-spread convex-hull vertices (farthest-point
        /// sampling), in the model frame, for <see cref="RigidBody.ContactPoints"/>.
        /// </summary>
        public static List<Vec3> ContactPointsFromMesh(MeshModel mesh, int maxPoints = 16) =>
            ContactPointsFromHull(ConvexHull.FromMesh(mesh), maxPoints);

        public static List<Vec3> ContactPointsFromHull(ConvexHull hull, int maxPoints = 16)
        {
            var v = hull.Vertices;
            var result = new List<Vec3>();
            if (v.Count == 0 || maxPoints <= 0) return result;
            if (v.Count <= maxPoints)
            {
                result.AddRange(v);
                return result;
            }

            var centroid = Vec3.Zero;
            foreach (var p in v) centroid += p;
            centroid /= v.Count;

            // Seed: farthest from the centroid; then repeatedly the vertex farthest from the chosen set.
            var minD = new double[v.Count];
            int first = 0;
            double best = -1;
            for (int i = 0; i < v.Count; i++)
            {
                double d = (v[i] - centroid).Length;
                if (d > best + 1e-15)
                {
                    best = d;
                    first = i;
                }
            }

            for (int i = 0; i < v.Count; i++) minD[i] = double.PositiveInfinity;
            int current = first;
            for (int n = 0; n < maxPoints; n++)
            {
                result.Add(v[current]);
                minD[current] = -1;
                int next = -1;
                best = -1;
                for (int i = 0; i < v.Count; i++)
                {
                    if (minD[i] < 0) continue;
                    double d = (v[i] - v[current]).Length;
                    if (d < minD[i]) minD[i] = d;
                    if (minD[i] > best + 1e-15)
                    {
                        best = minD[i];
                        next = i;
                    }
                }

                if (next < 0) break;
                current = next;
            }

            return result;
        }

        /// <summary>
        /// Configures a rigid body from a chassis model: mass properties, principal axes and
        /// hull contact points. The body origin becomes the centre of mass, its axes stay the
        /// model axes; returns the COM so other model-frame points (wheel mounts, sensors) can
        /// be converted with <c>p − com</c>. Existing contact points are replaced.
        /// </summary>
        public static Vec3 ConfigureFromMesh(this RigidBody body, MeshModel mesh, MassProperties massProperties, int maxContactPoints = 16)
        {
            body.SetMassProperties(massProperties);
            var com = massProperties.CenterOfMass;
            body.ContactPoints.Clear();
            foreach (var p in ContactPointsFromMesh(mesh, maxContactPoints)) body.ContactPoints.Add(p - com);
            return com;
        }
    }
}
