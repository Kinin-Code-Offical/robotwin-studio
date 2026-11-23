using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis.Fem
{
    /// <summary>
    /// [analysis] Tetrahedral mesh, linear (4-node) or quadratic (10-node, VTK node order:
    /// corners 0–3, then mid-edge nodes of edges 01, 12, 20, 03, 13, 23).
    /// </summary>
    public sealed class TetMesh
    {
        public static readonly int[,] Edges = { { 0, 1 }, { 1, 2 }, { 2, 0 }, { 0, 3 }, { 1, 3 }, { 2, 3 } };

        public List<Vec3> Nodes { get; } = new List<Vec3>();
        public List<int[]> Elements { get; } = new List<int[]>();

        /// <summary>1 = linear tetrahedra, 2 = quadratic (T10).</summary>
        public int Order { get; private set; } = 1;

        public int NodesPerElement => Order == 2 ? 10 : 4;

        /// <summary>Boundary faces as 6-node triangles (a, b, c, ab, bc, ca; mid nodes = −1 for linear), outward ordered.</summary>
        public List<int[]> Surface { get; } = new List<int[]>();

        /// <summary>Nodes on the boundary.</summary>
        public HashSet<int> SurfaceNodes { get; } = new HashSet<int>();

        /// <summary>Nominal element (grid cell) size (m).</summary>
        public double ElementSize { get; set; }

        /// <summary>Corner nodes that could not be moved onto the curved surface (would invert an element).</summary>
        public int UnsnappedNodes { get; private set; }

        /// <summary>Cells left out because they were not face-connected to the largest piece of the part.</summary>
        public int DroppedCells { get; private set; }

        public double Volume()
        {
            double v = 0;
            foreach (var e in Elements) v += TetVolume(Nodes[e[0]], Nodes[e[1]], Nodes[e[2]], Nodes[e[3]]);
            return v;
        }

        public Aabb Bounds() => Aabb.FromPoints(Nodes);

        public static double TetVolume(Vec3 a, Vec3 b, Vec3 c, Vec3 d) => (b - a).Dot((c - a).Cross(d - a)) / 6.0;

        /// <summary>
        /// Meshes a solid: a regular grid of size ≈ <paramref name="h"/> over its bounds, cells whose centre is
        /// inside kept, each split into 6 tetrahedra (Kuhn subdivision, conforming), boundary nodes moved onto
        /// the true surface where that keeps every element valid, then mid-edge nodes added for quadratic order.
        /// </summary>
        public static TetMesh FromSolid(Solid solid, double h, int order = 2, int maxCells = 400000)
        {
            var b = solid.Bounds;
            var size = b.Size;
            int nx = Math.Max(1, (int)Math.Round(size.X / h)), ny = Math.Max(1, (int)Math.Round(size.Y / h)), nz = Math.Max(1, (int)Math.Round(size.Z / h));
            while ((long)nx * ny * nz > maxCells)
            {
                nx = Math.Max(1, nx * 4 / 5);
                ny = Math.Max(1, ny * 4 / 5);
                nz = Math.Max(1, nz * 4 / 5);
            }

            double hx = size.X / nx, hy = size.Y / ny, hz = size.Z / nz;
            var o = b.Min;
            var keep = new bool[nx * ny * nz];
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                var c = o + new Vec3((i + 0.5) * hx, (j + 0.5) * hy, (k + 0.5) * hz);
                keep[(k * ny + j) * nx + i] = solid.Distance(c) < 0;
            }

            // Cells joined only at an edge or a corner, or not at all (a servo's case, flange and horn as separate pieces),
            // would be mechanisms or floating islands: the stiffness matrix is singular and the stresses meaningless. Only the
            // largest face-connected piece is meshed; loads on what is left out go to the nearest surface of the rest.
            int dropped = KeepLargestPiece(keep, nx, ny, nz);
            var mesh = BuildFromCells(nx, ny, nz, hx, hy, hz, o, keep);
            mesh.DroppedCells = dropped;
            mesh.ElementSize = Math.Min(hx, Math.Min(hy, hz));
            mesh.Snap(solid);
            if (order == 2) mesh.MakeQuadratic(solid);
            mesh.FindSurface();
            return mesh;
        }

        /// <summary>Keeps only the largest face-connected group of kept cells; returns how many cells were dropped.</summary>
        private static int KeepLargestPiece(bool[] keep, int nx, int ny, int nz)
        {
            var label = new int[keep.Length];
            var sizes = new List<int> { 0 };
            var stack = new Stack<int>();
            for (int s = 0; s < keep.Length; s++)
            {
                if (!keep[s] || label[s] != 0) continue;
                int id = sizes.Count, count = 0;
                label[s] = id;
                stack.Push(s);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    count++;
                    int i = c % nx, j = c / nx % ny, k = c / (nx * ny);
                    void Visit(int ii, int jj, int kk)
                    {
                        if (ii < 0 || jj < 0 || kk < 0 || ii >= nx || jj >= ny || kk >= nz) return;
                        int n = (kk * ny + jj) * nx + ii;
                        if (!keep[n] || label[n] != 0) return;
                        label[n] = id;
                        stack.Push(n);
                    }

                    Visit(i - 1, j, k);
                    Visit(i + 1, j, k);
                    Visit(i, j - 1, k);
                    Visit(i, j + 1, k);
                    Visit(i, j, k - 1);
                    Visit(i, j, k + 1);
                }

                sizes.Add(count);
            }

            if (sizes.Count <= 2) return 0;
            int best = 1;
            for (int id = 2; id < sizes.Count; id++)
                if (sizes[id] > sizes[best]) best = id;
            int dropped = 0;
            for (int c = 0; c < keep.Length; c++)
            {
                if (!keep[c] || label[c] == best) continue;
                keep[c] = false;
                dropped++;
            }

            return dropped;
        }

        /// <summary>Structured mesh of a box [min, max] with nx·ny·nz cells.</summary>
        public static TetMesh Box(Vec3 min, Vec3 max, int nx, int ny, int nz, int order = 2)
        {
            var size = max - min;
            var keep = new bool[nx * ny * nz];
            for (int i = 0; i < keep.Length; i++) keep[i] = true;
            var mesh = BuildFromCells(nx, ny, nz, size.X / nx, size.Y / ny, size.Z / nz, min, keep);
            mesh.ElementSize = Math.Min(size.X / nx, Math.Min(size.Y / ny, size.Z / nz));
            if (order == 2) mesh.MakeQuadratic(null);
            mesh.FindSurface();
            return mesh;
        }

        private static TetMesh BuildFromCells(int nx, int ny, int nz, double hx, double hy, double hz, Vec3 o, bool[] keep)
        {
            var mesh = new TetMesh();
            var index = new Dictionary<long, int>();
            int Node(int i, int j, int k)
            {
                long key = ((long)k * (ny + 1) + j) * (nx + 1) + i;
                if (index.TryGetValue(key, out int n)) return n;
                n = mesh.Nodes.Count;
                mesh.Nodes.Add(o + new Vec3(i * hx, j * hy, k * hz));
                index[key] = n;
                return n;
            }

            // Kuhn subdivision: 6 tetrahedra along the 000→111 diagonal, one per axis permutation.
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            var corner = new int[8];
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                if (!keep[(k * ny + j) * nx + i]) continue;
                for (int c = 0; c < 8; c++) corner[c] = Node(i + (c & 1), j + ((c >> 1) & 1), k + ((c >> 2) & 1));
                foreach (var p in perms)
                {
                    int v0 = 0, v1 = 1 << p[0], v2 = v1 | (1 << p[1]), v3 = 7;
                    var e = new[] { corner[v0], corner[v1], corner[v2], corner[v3] };
                    if (TetVolume(mesh.Nodes[e[0]], mesh.Nodes[e[1]], mesh.Nodes[e[2]], mesh.Nodes[e[3]]) < 0) (e[1], e[2]) = (e[2], e[1]);
                    mesh.Elements.Add(e);
                }
            }

            return mesh;
        }

        private static long FaceKey(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return ((long)a << 42) | ((long)b << 21) | (uint)c;
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static readonly int[][] Faces = { new[] { 1, 2, 3 }, new[] { 0, 3, 2 }, new[] { 0, 1, 3 }, new[] { 0, 2, 1 } };

        /// <summary>Boundary faces of the corner mesh (faces used by exactly one element), outward oriented.</summary>
        private List<(int Element, int Face)> BoundaryFaces()
        {
            var count = new Dictionary<long, (int Element, int Face, int Count)>();
            for (int e = 0; e < Elements.Count; e++)
            {
                var el = Elements[e];
                for (int f = 0; f < 4; f++)
                {
                    var fc = Faces[f];
                    long key = FaceKey(el[fc[0]], el[fc[1]], el[fc[2]]);
                    count[key] = count.TryGetValue(key, out var x) ? (x.Element, x.Face, x.Count + 1) : (e, f, 1);
                }
            }

            var list = new List<(int, int)>();
            foreach (var kv in count)
            {
                if (kv.Value.Count == 1) list.Add((kv.Value.Element, kv.Value.Face));
            }

            list.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2.CompareTo(b.Item2));
            return list;
        }

        /// <summary>Moves boundary corner nodes onto the solid's surface where no element degenerates.</summary>
        private void Snap(Solid solid)
        {
            var boundary = new SortedSet<int>();
            foreach (var (e, f) in BoundaryFaces())
            foreach (int c in Faces[f])
                boundary.Add(Elements[e][c]);
            var incident = new List<int>[Nodes.Count];
            for (int e = 0; e < Elements.Count; e++)
            foreach (int n in Elements[e])
                (incident[n] ??= new List<int>()).Add(e);
            var original = new double[Elements.Count];
            for (int e = 0; e < Elements.Count; e++) original[e] = Vol(e);
            double h = ElementSize;
            int failed = 0;
            foreach (int n in boundary)
            {
                var p = Nodes[n];
                double d = solid.Distance(p);
                if (Math.Abs(d) < 1e-9 * h || Math.Abs(d) > 1.2 * h) continue;
                var target = solid.Project(p, h);
                if ((target - p).Length > 1.5 * h) continue;
                bool ok = false;
                foreach (double f in new[] { 1.0, 0.7, 0.4 })
                {
                    Nodes[n] = p + (target - p) * f;
                    ok = true;
                    foreach (int e in incident[n])
                    {
                        if (Vol(e) < 0.08 * original[e])
                        {
                            ok = false;
                            break;
                        }
                    }

                    if (ok) break;
                }

                if (!ok)
                {
                    Nodes[n] = p;
                    failed++;
                }
            }

            UnsnappedNodes = failed;
        }

        private double Vol(int e)
        {
            var el = Elements[e];
            return TetVolume(Nodes[el[0]], Nodes[el[1]], Nodes[el[2]], Nodes[el[3]]);
        }

        /// <summary>Adds the six mid-edge nodes per element; boundary mid nodes follow a curved surface.</summary>
        private void MakeQuadratic(Solid? solid)
        {
            var boundaryEdges = new HashSet<long>();
            if (solid != null)
            {
                foreach (var (e, f) in BoundaryFaces())
                {
                    var fc = Faces[f];
                    var el = Elements[e];
                    boundaryEdges.Add(EdgeKey(el[fc[0]], el[fc[1]]));
                    boundaryEdges.Add(EdgeKey(el[fc[1]], el[fc[2]]));
                    boundaryEdges.Add(EdgeKey(el[fc[2]], el[fc[0]]));
                }
            }

            var mids = new Dictionary<long, int>();
            for (int e = 0; e < Elements.Count; e++)
            {
                var el = Elements[e];
                var q = new int[10];
                Array.Copy(el, q, 4);
                for (int k = 0; k < 6; k++)
                {
                    int a = el[Edges[k, 0]], b = el[Edges[k, 1]];
                    long key = EdgeKey(a, b);
                    if (!mids.TryGetValue(key, out int m))
                    {
                        var mid = (Nodes[a] + Nodes[b]) * 0.5;
                        if (solid != null && boundaryEdges.Contains(key))
                        {
                            double len = (Nodes[a] - Nodes[b]).Length;
                            var t = solid.Project(mid, ElementSize);
                            if ((t - mid).Length < 0.2 * len) mid = t;
                        }

                        m = Nodes.Count;
                        Nodes.Add(mid);
                        mids[key] = m;
                    }

                    q[4 + k] = m;
                }

                Elements[e] = q;
            }

            Order = 2;
            if (solid != null) StraightenBadElements();
        }

        /// <summary>Mid-edge nodes put back on the straight edge because the curved element would have been distorted.</summary>
        public int StraightenedNodes { get; private set; }

        /// <summary>
        /// A curved (T10) element is only valid while its Jacobian stays positive and well away from zero everywhere inside
        /// it; moving mid-edge nodes onto the true surface next to an already snapped, thin corner element can nearly fold
        /// it, and the stresses there blow up (a 0.02 mm displacement read as 10¹³ MPa). Each element is checked at its
        /// corners, mid-edges and centre; where the Jacobian drops below a fifth of the straight element's, its mid-edge
        /// nodes go back to the straight midpoints (always valid when the corners are).
        /// </summary>
        private void StraightenBadElements()
        {
            var probes = new List<double[]>
            {
                new[] { 1.0, 0, 0, 0 }, new[] { 0, 1.0, 0, 0 }, new[] { 0, 0, 1.0, 0 }, new[] { 0, 0, 0, 1.0 },
                new[] { 0.25, 0.25, 0.25, 0.25 }
            };
            for (int k = 0; k < 6; k++)
            {
                var l = new double[4];
                l[Edges[k, 0]] = 0.5;
                l[Edges[k, 1]] = 0.5;
                probes.Add(l);
            }

            var straight = new HashSet<int>();
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var q in Elements)
                {
                    double vol = TetVolume(Nodes[q[0]], Nodes[q[1]], Nodes[q[2]], Nodes[q[3]]);
                    double reference = 6 * Math.Abs(vol);
                    if (reference <= 0) continue;
                    double worst = double.MaxValue;
                    foreach (var l in probes) worst = Math.Min(worst, DetJ(q, l) * Math.Sign(vol));
                    if (worst >= 0.2 * reference) continue;
                    for (int k = 0; k < 6; k++)
                    {
                        int m = q[4 + k];
                        var mid = (Nodes[q[Edges[k, 0]]] + Nodes[q[Edges[k, 1]]]) * 0.5;
                        if ((Nodes[m] - mid).LengthSquared < 1e-24) continue;
                        Nodes[m] = mid;
                        straight.Add(m);
                        changed = true;
                    }
                }

                if (!changed) break;
            }

            StraightenedNodes = straight.Count;
        }

        /// <summary>det(∂x/∂ξ) of a T10 element at volume coordinates l (ξ = L2, L3, L4).</summary>
        private double DetJ(int[] q, double[] l)
        {
            // dN/dL_i for every node, then dx/dξ_a = Σ x (dN/dL_{a+1} − dN/dL_1).
            var j = new double[9];
            void Add(Vec3 x, double d1, double d2, double d3, double d4)
            {
                double a = d2 - d1, b = d3 - d1, c = d4 - d1;
                j[0] += x.X * a; j[1] += x.X * b; j[2] += x.X * c;
                j[3] += x.Y * a; j[4] += x.Y * b; j[5] += x.Y * c;
                j[6] += x.Z * a; j[7] += x.Z * b; j[8] += x.Z * c;
            }

            for (int i = 0; i < 4; i++)
            {
                double d = 4 * l[i] - 1;
                Add(Nodes[q[i]], i == 0 ? d : 0, i == 1 ? d : 0, i == 2 ? d : 0, i == 3 ? d : 0);
            }

            for (int k = 0; k < 6; k++)
            {
                int a = Edges[k, 0], b = Edges[k, 1];
                var dl = new double[4];
                dl[a] = 4 * l[b];
                dl[b] = 4 * l[a];
                Add(Nodes[q[4 + k]], dl[0], dl[1], dl[2], dl[3]);
            }

            return j[0] * (j[4] * j[8] - j[5] * j[7]) - j[1] * (j[3] * j[8] - j[5] * j[6]) + j[2] * (j[3] * j[7] - j[4] * j[6]);
        }

        private void FindSurface()
        {
            Surface.Clear();
            SurfaceNodes.Clear();
            var edgeMid = new Dictionary<long, int>();
            if (Order == 2)
            {
                foreach (var el in Elements)
                    for (int k = 0; k < 6; k++)
                        edgeMid[EdgeKey(el[Edges[k, 0]], el[Edges[k, 1]])] = el[4 + k];
            }

            foreach (var (e, f) in BoundaryFaces())
            {
                var el = Elements[e];
                var fc = Faces[f];
                int a = el[fc[0]], b = el[fc[1]], c = el[fc[2]];
                // Outward: the normal must point away from the fourth vertex.
                int opp = el[f];
                var n = (Nodes[b] - Nodes[a]).Cross(Nodes[c] - Nodes[a]);
                if (n.Dot(Nodes[opp] - Nodes[a]) > 0) (b, c) = (c, b);
                int ab = -1, bc = -1, ca = -1;
                if (Order == 2)
                {
                    ab = edgeMid[EdgeKey(a, b)];
                    bc = edgeMid[EdgeKey(b, c)];
                    ca = edgeMid[EdgeKey(c, a)];
                }

                Surface.Add(new[] { a, b, c, ab, bc, ca });
                SurfaceNodes.Add(a);
                SurfaceNodes.Add(b);
                SurfaceNodes.Add(c);
                if (ab >= 0)
                {
                    SurfaceNodes.Add(ab);
                    SurfaceNodes.Add(bc);
                    SurfaceNodes.Add(ca);
                }
            }
        }
    }
}
