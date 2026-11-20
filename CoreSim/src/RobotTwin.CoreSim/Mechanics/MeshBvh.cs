using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    public struct RayHit
    {
        /// <summary>Distance along the (normalised) ray direction.</summary>
        public double Distance;

        public Vec3 Point;

        /// <summary>Geometric normal of the hit triangle, facing the ray origin.</summary>
        public Vec3 Normal;

        /// <summary>Triangle index in the source mesh.</summary>
        public int Triangle;

        /// <summary>Barycentric coordinates of the hit (weights of vertices 1 and 2).</summary>
        public double U, V;
    }

    /// <summary>
    /// Bounding volume hierarchy over a triangle mesh for ray casts (Möller–Trumbore).
    /// The mesh is copied at construction; rebuild after changing it.
    /// </summary>
    public sealed class MeshBvh
    {
        private const int LeafSize = 4;

        // Per triangle (in BVH order): v0 and the two edges.
        private readonly double[] _tri;
        private readonly int[] _triId;

        // Nodes: bounds, and either children (left = index, right = left+... stored) or a leaf range.
        private double[] _box = Array.Empty<double>();
        private int[] _left = Array.Empty<int>(), _right = Array.Empty<int>(), _start = Array.Empty<int>(), _count = Array.Empty<int>();
        private int _nodes, _depth;

        public MeshBvh(MeshModel mesh)
        {
            int n = mesh.TriangleCount;
            TriangleCount = n;
            Bounds = mesh.Bounds;
            _triId = new int[n];
            var centroid = new double[3 * n];
            var lo = new double[3 * n];
            var hi = new double[3 * n];
            for (int t = 0; t < n; t++)
            {
                _triId[t] = t;
                var (a, b, c) = mesh.Triangle(t);
                centroid[3 * t] = (a.X + b.X + c.X) / 3;
                centroid[3 * t + 1] = (a.Y + b.Y + c.Y) / 3;
                centroid[3 * t + 2] = (a.Z + b.Z + c.Z) / 3;
                lo[3 * t] = Math.Min(a.X, Math.Min(b.X, c.X));
                lo[3 * t + 1] = Math.Min(a.Y, Math.Min(b.Y, c.Y));
                lo[3 * t + 2] = Math.Min(a.Z, Math.Min(b.Z, c.Z));
                hi[3 * t] = Math.Max(a.X, Math.Max(b.X, c.X));
                hi[3 * t + 1] = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                hi[3 * t + 2] = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            }

            Build(n, centroid, lo, hi);

            _tri = new double[9 * n];
            for (int i = 0; i < n; i++)
            {
                var (a, b, c) = mesh.Triangle(_triId[i]);
                var e1 = b - a;
                var e2 = c - a;
                int o = 9 * i;
                _tri[o] = a.X;
                _tri[o + 1] = a.Y;
                _tri[o + 2] = a.Z;
                _tri[o + 3] = e1.X;
                _tri[o + 4] = e1.Y;
                _tri[o + 5] = e1.Z;
                _tri[o + 6] = e2.X;
                _tri[o + 7] = e2.Y;
                _tri[o + 8] = e2.Z;
            }
        }

        public int TriangleCount { get; }
        public Aabb Bounds { get; }
        public int NodeCount => _nodes;
        public int Depth => _depth;

        private int NewNode()
        {
            if (_nodes == _left.Length)
            {
                int cap = Math.Max(16, _left.Length * 2);
                Array.Resize(ref _left, cap);
                Array.Resize(ref _right, cap);
                Array.Resize(ref _start, cap);
                Array.Resize(ref _count, cap);
                Array.Resize(ref _box, cap * 6);
            }

            return _nodes++;
        }

        private void Build(int n, double[] centroid, double[] lo, double[] hi)
        {
            if (n == 0) return;
            var work = new Stack<(int Node, int Start, int Count, int Depth)>();
            int root = NewNode();
            work.Push((root, 0, n, 1));
            while (work.Count > 0)
            {
                var (node, start, count, depth) = work.Pop();
                _depth = Math.Max(_depth, depth);
                double bx0 = double.PositiveInfinity, by0 = bx0, bz0 = bx0, bx1 = double.NegativeInfinity, by1 = bx1, bz1 = bx1;
                double cx0 = bx0, cy0 = bx0, cz0 = bx0, cx1 = bx1, cy1 = bx1, cz1 = bx1;
                for (int i = start; i < start + count; i++)
                {
                    int t = _triId[i];
                    bx0 = Math.Min(bx0, lo[3 * t]);
                    by0 = Math.Min(by0, lo[3 * t + 1]);
                    bz0 = Math.Min(bz0, lo[3 * t + 2]);
                    bx1 = Math.Max(bx1, hi[3 * t]);
                    by1 = Math.Max(by1, hi[3 * t + 1]);
                    bz1 = Math.Max(bz1, hi[3 * t + 2]);
                    cx0 = Math.Min(cx0, centroid[3 * t]);
                    cy0 = Math.Min(cy0, centroid[3 * t + 1]);
                    cz0 = Math.Min(cz0, centroid[3 * t + 2]);
                    cx1 = Math.Max(cx1, centroid[3 * t]);
                    cy1 = Math.Max(cy1, centroid[3 * t + 1]);
                    cz1 = Math.Max(cz1, centroid[3 * t + 2]);
                }

                int b = node * 6;
                _box[b] = bx0;
                _box[b + 1] = by0;
                _box[b + 2] = bz0;
                _box[b + 3] = bx1;
                _box[b + 4] = by1;
                _box[b + 5] = bz1;

                double ex = cx1 - cx0, ey = cy1 - cy0, ez = cz1 - cz0;
                int axis = ex >= ey && ex >= ez ? 0 : ey >= ez ? 1 : 2;
                double extent = axis == 0 ? ex : axis == 1 ? ey : ez;
                if (count <= LeafSize || extent <= 0)
                {
                    _left[node] = -1;
                    _start[node] = start;
                    _count[node] = count;
                    continue;
                }

                // Spatial median split; fall back to an even split if one side is empty.
                double mid = (axis == 0 ? cx0 : axis == 1 ? cy0 : cz0) + extent / 2;
                int i0 = start, i1 = start + count - 1;
                while (i0 <= i1)
                {
                    if (centroid[3 * _triId[i0] + axis] < mid) i0++;
                    else
                    {
                        (_triId[i0], _triId[i1]) = (_triId[i1], _triId[i0]);
                        i1--;
                    }
                }

                int leftCount = i0 - start;
                if (leftCount == 0 || leftCount == count)
                {
                    Array.Sort(_triId, start, count, Comparer<int>.Create((p, q) => centroid[3 * p + axis].CompareTo(centroid[3 * q + axis])));
                    leftCount = count / 2;
                }

                int l = NewNode(), r = NewNode();
                _left[node] = l;
                _right[node] = r;
                _count[node] = 0;
                work.Push((r, start + leftCount, count - leftCount, depth + 1));
                work.Push((l, start, leftCount, depth + 1));
            }
        }

        /// <summary>Nearest hit within <paramref name="maxDistance"/> (direction need not be normalised).</summary>
        public bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, out RayHit hit, bool cullBackFaces = false)
        {
            hit = default;
            double len = direction.Length;
            if (_nodes == 0 || len < 1e-300) return false;
            var d = direction / len;
            double ix = 1 / d.X, iy = 1 / d.Y, iz = 1 / d.Z;
            double best = maxDistance;
            int bestTri = -1;
            double bu = 0, bv = 0;
            var stack = new int[2 * _depth + 4];
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                int node = stack[--sp];
                if (!BoxHit(node, origin, ix, iy, iz, best, out _)) continue;
                if (_left[node] < 0)
                {
                    int end = _start[node] + _count[node];
                    for (int i = _start[node]; i < end; i++)
                    {
                        if (Intersect(i, origin, d, best, cullBackFaces, out double t, out double u, out double v))
                        {
                            best = t;
                            bestTri = i;
                            bu = u;
                            bv = v;
                        }
                    }

                    continue;
                }

                int l = _left[node], r = _right[node];
                bool hl = BoxHit(l, origin, ix, iy, iz, best, out double tl);
                bool hr = BoxHit(r, origin, ix, iy, iz, best, out double tr);
                // Push the far child first so the near one is processed first.
                if (hl && hr)
                {
                    if (tl <= tr)
                    {
                        stack[sp++] = r;
                        stack[sp++] = l;
                    }
                    else
                    {
                        stack[sp++] = l;
                        stack[sp++] = r;
                    }
                }
                else if (hl) stack[sp++] = l;
                else if (hr) stack[sp++] = r;
            }

            if (bestTri < 0) return false;
            int o = 9 * bestTri;
            var e1 = new Vec3(_tri[o + 3], _tri[o + 4], _tri[o + 5]);
            var e2 = new Vec3(_tri[o + 6], _tri[o + 7], _tri[o + 8]);
            var n = e1.Cross(e2).Normalized();
            if (n.Dot(d) > 0) n = -n;
            hit = new RayHit { Distance = best, Point = origin + d * best, Normal = n, Triangle = _triId[bestTri], U = bu, V = bv };
            return true;
        }

        /// <summary>Distance to the nearest hit, or <paramref name="maxDistance"/> if none.</summary>
        public double Distance(Vec3 origin, Vec3 direction, double maxDistance) =>
            Raycast(origin, direction, maxDistance, out var h) ? h.Distance : maxDistance;

        /// <summary>
        /// Closest point of the mesh surface to <paramref name="p"/> within <paramref name="maxDistance"/>,
        /// with the (winding-oriented, i.e. outward for a correctly wound closed mesh) face normal.
        /// </summary>
        public bool ClosestPoint(Vec3 p, double maxDistance, out Vec3 closest, out Vec3 faceNormal, out double distance)
        {
            closest = default;
            faceNormal = Vec3.UnitZ;
            distance = maxDistance;
            if (_nodes == 0) return false;
            double best2 = maxDistance * maxDistance;
            int bestTri = -1;
            var stack = new int[2 * _depth + 4];
            int sp = 0;
            stack[sp++] = 0;
            while (sp > 0)
            {
                int node = stack[--sp];
                if (BoxDistance2(node, p) > best2) continue;
                if (_left[node] < 0)
                {
                    int end = _start[node] + _count[node];
                    for (int i = _start[node]; i < end; i++)
                    {
                        var c = ClosestOnTriangle(i, p);
                        double d2 = (p - c).LengthSquared;
                        if (d2 < best2)
                        {
                            best2 = d2;
                            bestTri = i;
                            closest = c;
                        }
                    }

                    continue;
                }

                int l = _left[node], r = _right[node];
                double dl = BoxDistance2(l, p), dr = BoxDistance2(r, p);
                if (dl <= dr)
                {
                    stack[sp++] = r;
                    stack[sp++] = l;
                }
                else
                {
                    stack[sp++] = l;
                    stack[sp++] = r;
                }
            }

            if (bestTri < 0) return false;
            int o = 9 * bestTri;
            var e1 = new Vec3(_tri[o + 3], _tri[o + 4], _tri[o + 5]);
            var e2 = new Vec3(_tri[o + 6], _tri[o + 7], _tri[o + 8]);
            faceNormal = e1.Cross(e2).Normalized();
            distance = Math.Sqrt(best2);
            return true;
        }

        private double BoxDistance2(int node, Vec3 p)
        {
            int b = node * 6;
            double dx = Math.Max(0, Math.Max(_box[b] - p.X, p.X - _box[b + 3]));
            double dy = Math.Max(0, Math.Max(_box[b + 1] - p.Y, p.Y - _box[b + 4]));
            double dz = Math.Max(0, Math.Max(_box[b + 2] - p.Z, p.Z - _box[b + 5]));
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>Closest point on triangle i (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private Vec3 ClosestOnTriangle(int i, Vec3 p)
        {
            int k = 9 * i;
            var a = new Vec3(_tri[k], _tri[k + 1], _tri[k + 2]);
            var ab = new Vec3(_tri[k + 3], _tri[k + 4], _tri[k + 5]);
            var ac = new Vec3(_tri[k + 6], _tri[k + 7], _tri[k + 8]);
            var ap = p - a;
            double d1 = ab.Dot(ap), d2 = ac.Dot(ap);
            if (d1 <= 0 && d2 <= 0) return a;
            var bp = ap - ab;
            double d3 = ab.Dot(bp), d4 = ac.Dot(bp);
            if (d3 >= 0 && d4 <= d3) return a + ab;
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
            var cp = ap - ac;
            double d5 = ab.Dot(cp), d6 = ac.Dot(cp);
            if (d6 >= 0 && d5 <= d6) return a + ac;
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0) return a + ab + (ac - ab) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
            double denom = 1.0 / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        private bool BoxHit(int node, Vec3 o, double ix, double iy, double iz, double maxT, out double tEnter)
        {
            int b = node * 6;
            double t1 = (_box[b] - o.X) * ix, t2 = (_box[b + 3] - o.X) * ix;
            double tmin = Math.Min(t1, t2), tmax = Math.Max(t1, t2);
            t1 = (_box[b + 1] - o.Y) * iy;
            t2 = (_box[b + 4] - o.Y) * iy;
            tmin = Math.Max(tmin, Math.Min(t1, t2));
            tmax = Math.Min(tmax, Math.Max(t1, t2));
            t1 = (_box[b + 2] - o.Z) * iz;
            t2 = (_box[b + 5] - o.Z) * iz;
            tmin = Math.Max(tmin, Math.Min(t1, t2));
            tmax = Math.Min(tmax, Math.Max(t1, t2));
            tEnter = tmin;
            // NaN (origin on a slab plane with a zero direction component) counts as a hit: conservative.
            if (double.IsNaN(tmin) || double.IsNaN(tmax)) return true;
            return tmax >= Math.Max(tmin, 0) && tmin <= maxT;
        }

        private bool Intersect(int i, Vec3 o, Vec3 d, double maxT, bool cull, out double t, out double u, out double v)
        {
            int k = 9 * i;
            double e1x = _tri[k + 3], e1y = _tri[k + 4], e1z = _tri[k + 5];
            double e2x = _tri[k + 6], e2y = _tri[k + 7], e2z = _tri[k + 8];
            double px = d.Y * e2z - d.Z * e2y, py = d.Z * e2x - d.X * e2z, pz = d.X * e2y - d.Y * e2x;
            double det = e1x * px + e1y * py + e1z * pz;
            t = u = v = 0;
            double scale = Math.Abs(e1x) + Math.Abs(e1y) + Math.Abs(e1z) + Math.Abs(e2x) + Math.Abs(e2y) + Math.Abs(e2z);
            double eps = 1e-14 * scale * scale;
            if (cull ? det < eps : Math.Abs(det) < eps) return false;
            double inv = 1 / det;
            double sx = o.X - _tri[k], sy = o.Y - _tri[k + 1], sz = o.Z - _tri[k + 2];
            u = (sx * px + sy * py + sz * pz) * inv;
            if (u < 0 || u > 1) return false;
            double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
            v = (d.X * qx + d.Y * qy + d.Z * qz) * inv;
            if (v < 0 || u + v > 1) return false;
            t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            return t >= 0 && t < maxT;
        }
    }

    /// <summary>
    /// Static obstacle of arbitrary shape (an imported model in world coordinates). Ray casts
    /// (sensors) use the exact triangles; collision pushes contact points out through the
    /// obstacle's convex hull, so split strongly concave obstacles into convex parts
    /// (e.g. <see cref="MeshModel.SplitByGroup"/>) and add each as its own obstacle.
    /// </summary>
    public sealed class MeshObstacle
    {
        private readonly HullPlane[] _planes;
        private readonly bool[] _floorPlane;

        /// <param name="worldMesh">Mesh already in world coordinates (metres, Z up).</param>
        public MeshObstacle(MeshModel worldMesh, string? name = null)
        {
            Mesh = worldMesh;
            Name = name ?? worldMesh.Name;
            Bounds = worldMesh.Bounds;
            Bvh = new MeshBvh(worldMesh);
            Hull = ConvexHull.FromMesh(worldMesh);
            _planes = new HullPlane[Hull.Planes.Count];
            _floorPlane = new bool[_planes.Length];
            for (int i = 0; i < _planes.Length; i++)
            {
                _planes[i] = Hull.Planes[i];
                var n = _planes[i].Normal;
                // A face lying on the floor must not push points down into it.
                _floorPlane[i] = n.Z < -0.99 && Math.Abs(_planes[i].Offset / n.Z) < 1e-3;
            }
        }

        /// <summary>
        /// Places a model on the arena: rotated by <paramref name="yaw"/> about Z, centred on
        /// (x, y) and, when <paramref name="restOnFloor"/>, lifted so its lowest point touches z = 0.
        /// </summary>
        public static MeshObstacle Place(MeshModel model, double x, double y, double yaw = 0, bool restOnFloor = true, string? name = null)
        {
            var m = model.Transformed(Quat.FromYaw(yaw), Vec3.Zero);
            var b = m.Bounds;
            m.Translate(new Vec3(x - b.Center.X, y - b.Center.Y, restOnFloor ? -b.Min.Z : 0));
            return new MeshObstacle(m, name);
        }

        public string Name { get; }
        public MeshModel Mesh { get; }
        public Aabb Bounds { get; }
        public MeshBvh Bvh { get; }
        public ConvexHull Hull { get; }

        /// <summary>Distance along the ray to the obstacle surface, or <paramref name="maxRange"/>.</summary>
        public double Raycast(Vec3 origin, Vec3 direction, double maxRange)
        {
            if (Bounds.RayIntersect(origin, direction.Normalized(), maxRange) < 0) return maxRange;
            return Bvh.Distance(origin, direction, maxRange);
        }

        /// <summary>Point inside the obstacle's convex hull.</summary>
        public bool Contains(Vec3 p)
        {
            if (!Bounds.Contains(p) || Hull.IsDegenerate) return false;
            foreach (var pl in _planes)
                if (pl.Distance(p) > 0)
                    return false;
            return true;
        }

        /// <summary>
        /// For a point inside the hull: the shallowest way out (depth ≥ 0 and outward normal),
        /// ignoring faces that rest on the floor.
        /// </summary>
        public bool TryGetPenetration(Vec3 p, out double depth, out Vec3 normal)
        {
            depth = 0;
            normal = Vec3.UnitZ;
            if (Hull.IsDegenerate || !Bounds.Contains(p)) return false;
            double best = double.PositiveInfinity;
            for (int i = 0; i < _planes.Length; i++)
            {
                double dist = _planes[i].Distance(p);
                if (dist > 0) return false;
                if (_floorPlane[i]) continue;
                if (-dist < best)
                {
                    best = -dist;
                    normal = _planes[i].Normal;
                }
            }

            if (double.IsPositiveInfinity(best)) return false;
            depth = best;
            return true;
        }
    }
}
