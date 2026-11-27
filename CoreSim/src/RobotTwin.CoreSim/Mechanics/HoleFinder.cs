using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>[holes] A round hole found in a mesh: its axis, radius and how deep it runs (mesh units).</summary>
    public sealed class MeshHole
    {
        /// <summary>The middle of the hole on its axis.</summary>
        public Vec3 Center { get; internal set; }

        /// <summary>Unit axis (its sign carries no meaning).</summary>
        public Vec3 Axis { get; internal set; }

        public double Radius { get; internal set; }

        /// <summary>Length of the wall along the axis.</summary>
        public double Depth { get; internal set; }

        public double Diameter => Radius * 2;

        /// <summary>The two ends of the hole on its axis.</summary>
        public Vec3 EndA => Center - Axis * (Depth / 2);

        public Vec3 EndB => Center + Axis * (Depth / 2);

        /// <summary>The same hole in another frame (a part's pose).</summary>
        public MeshHole Transformed(Func<Vec3, Vec3> toWorld)
        {
            var c = toWorld(Center);
            var a = (toWorld(Center + Axis) - c).Normalized();
            return new MeshHole { Center = c, Axis = a, Radius = Radius, Depth = Depth };
        }
    }

    /// <summary>
    /// [holes] Finds round holes in a triangle mesh, as SolidWorks finds them for its Hole Alignment: a hole is a closed
    /// ring of wall faces whose normals all lie square to one axis, whose corners sit on one circle round that axis and
    /// whose normals point in toward it (a boss or a shaft points out and is not a hole). Works on any tessellation that
    /// keeps the wall's corners on the circle (our own primitives and booleans, STL and OBJ exports).
    /// </summary>
    public static class HoleFinder
    {
        private const double SquareToAxis = 0.035;           // |n·axis| of a wall face
        private static readonly double NeighbourCos = Math.Cos(50 * Math.PI / 180);   // the most two wall faces may turn
        private static readonly double SameCos = Math.Cos(0.3 * Math.PI / 180);       // turning less is one flat face

        public static List<MeshHole> Find(MeshModel mesh)
        {
            var result = new List<MeshHole>();
            int triCount = mesh.Indices.Count / 3;
            if (triCount < 6) return result;

            // weld the corners, so faces that share a position share an edge (flat-shaded meshes repeat them)
            var b = mesh.Bounds;
            double size = Math.Max((b.Max - b.Min).Length, 1e-9);
            double eps = size * 1e-7;
            var weld = new Dictionary<(long, long, long), int>();
            var points = new List<Vec3>();
            var id = new int[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                var v = mesh.Vertices[i];
                var key = ((long)Math.Round(v.X / eps), (long)Math.Round(v.Y / eps), (long)Math.Round(v.Z / eps));
                if (!weld.TryGetValue(key, out int w))
                {
                    w = points.Count;
                    weld[key] = w;
                    points.Add(v);
                }

                id[i] = w;
            }

            var tri = new int[triCount, 3];
            var normal = new Vec3[triCount];
            var area = new double[triCount];
            var edges = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t < triCount; t++)
            {
                for (int k = 0; k < 3; k++) tri[t, k] = id[mesh.Indices[t * 3 + k]];
                var n = (points[tri[t, 1]] - points[tri[t, 0]]).Cross(points[tri[t, 2]] - points[tri[t, 0]]);
                area[t] = n.Length / 2;
                normal[t] = n.Normalized();
                if (area[t] <= size * size * 1e-14) continue;
                for (int k = 0; k < 3; k++)
                {
                    int a = tri[t, k], c = tri[t, (k + 1) % 3];
                    if (a == c) continue;
                    var e = a < c ? (a, c) : (c, a);
                    if (!edges.TryGetValue(e, out var list)) edges[e] = list = new List<int>(2);
                    list.Add(t);
                }
            }

            IEnumerable<int> Neighbours(int t)
            {
                for (int k = 0; k < 3; k++)
                {
                    int a = tri[t, k], c = tri[t, (k + 1) % 3];
                    if (a == c || !edges.TryGetValue(a < c ? (a, c) : (c, a), out var list)) continue;
                    foreach (int o in list)
                        if (o != t)
                            yield return o;
                }
            }

            var used = new bool[triCount];
            for (int seed = 0; seed < triCount; seed++)
            {
                if (used[seed] || area[seed] <= 0 || normal[seed].LengthSquared < 0.5) continue;

                // the axis: square to this face's normal and its curved neighbour's
                Vec3? axis = null;
                foreach (int nb in Neighbours(seed))
                {
                    double d = normal[seed].Dot(normal[nb]);
                    if (d < SameCos && d > NeighbourCos)
                    {
                        axis = normal[seed].Cross(normal[nb]).Normalized();
                        break;
                    }
                }

                if (axis == null) continue;
                var ax = axis.Value;
                var patch = new List<int> { seed };
                var inPatch = new HashSet<int> { seed };
                for (int q = 0; q < patch.Count; q++)
                {
                    int cur = patch[q];
                    foreach (int nb in Neighbours(cur))
                    {
                        if (inPatch.Contains(nb) || used[nb] || area[nb] <= 0) continue;
                        if (Math.Abs(normal[nb].Dot(ax)) > SquareToAxis || normal[nb].Dot(normal[cur]) < NeighbourCos) continue;
                        inPatch.Add(nb);
                        patch.Add(nb);
                    }
                }

                foreach (int t in patch) used[t] = true;
                if (patch.Count < 6) continue;
                if (Fit(patch, tri, normal, area, points, ax) is { } hole) result.Add(hole);
            }

            return result;
        }

        private static MeshHole? Fit(List<int> patch, int[,] tri, Vec3[] normal, double[] area, List<Vec3> points, Vec3 seedAxis)
        {
            // refine the axis: the direction the wall's normals leave out (the smallest spread of n·a)
            var axis = RefineAxis(patch, normal, seedAxis);
            var u = axis.AnyPerpendicular().Normalized();
            var v = axis.Cross(u).Normalized();
            var corners = patch.SelectMany(t => new[] { tri[t, 0], tri[t, 1], tri[t, 2] }).Distinct().Select(i => points[i]).ToList();
            if (corners.Count < 6) return null;
            var origin = corners[0];

            // circle through the corners projected square to the axis (Kåsa's least squares)
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
            int n = corners.Count;
            var flat = new (double X, double Y)[n];
            for (int i = 0; i < n; i++)
            {
                var d = corners[i] - origin;
                double x = d.Dot(u), y = d.Dot(v), z = x * x + y * y;
                flat[i] = (x, y);
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sxz += x * z; syz += y * z; sz += z;
            }

            // [sxx sxy sx; sxy syy sy; sx sy n] [D E F]ᵀ = -[sxz syz sz]ᵀ
            if (!Solve3(sxx, sxy, sx, sxy, syy, sy, sx, sy, n, -sxz, -syz, -sz, out double dd, out double ee, out double ff)) return null;
            double cx = -dd / 2, cy = -ee / 2, r2 = cx * cx + cy * cy - ff;
            if (r2 <= 0) return null;
            double r = Math.Sqrt(r2);
            double worst = flat.Max(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - r));
            if (worst > r * 0.02) return null;   // not round: a slot, a flat side

            // all the way round: no gap of more than a quarter turn between the corners
            var angles = flat.Select(p => Math.Atan2(p.Y - cy, p.X - cx)).OrderBy(a => a).ToList();
            double gap = angles[0] + 2 * Math.PI - angles[angles.Count - 1];
            for (int i = 1; i < angles.Count; i++) gap = Math.Max(gap, angles[i] - angles[i - 1]);
            if (gap > Math.PI / 2) return null;

            var centerLine = origin + u * cx + v * cy;

            // in toward the axis (a hole), not out of it (a boss)
            double inward = 0;
            foreach (int t in patch)
            {
                var c = (points[tri[t, 0]] + points[tri[t, 1]] + points[tri[t, 2]]) / 3;
                var radial = c - centerLine;
                radial -= axis * radial.Dot(axis);
                inward += area[t] * Math.Sign(-normal[t].Dot(radial));
            }

            if (inward <= 0) return null;
            double lo = corners.Min(p => (p - centerLine).Dot(axis)), hi = corners.Max(p => (p - centerLine).Dot(axis));
            if (hi - lo <= r * 1e-3) return null;
            return new MeshHole { Center = centerLine + axis * ((lo + hi) / 2), Axis = axis, Radius = r, Depth = hi - lo };
        }

        private static Vec3 RefineAxis(List<int> patch, Vec3[] normal, Vec3 seed)
        {
            // the eigenvector of Σ n nᵀ with the smallest value, by a few rounds of inverse iteration on its 3×3 matrix
            double a = 0, bb = 0, c = 0, d = 0, e = 0, f = 0;
            foreach (int t in patch)
            {
                var n = normal[t];
                a += n.X * n.X; bb += n.X * n.Y; c += n.X * n.Z; d += n.Y * n.Y; e += n.Y * n.Z; f += n.Z * n.Z;
            }

            double shift = 1e-9 * (a + d + f);
            var x = seed;
            for (int i = 0; i < 8; i++)
            {
                if (!Solve3(a + shift, bb, c, bb, d + shift, e, c, e, f + shift, x.X, x.Y, x.Z, out double x0, out double x1, out double x2)) break;
                var next = new Vec3(x0, x1, x2).Normalized();
                if (next.LengthSquared < 0.5) break;
                x = next;
            }

            return x.Dot(seed) < 0 ? -x : x;
        }

        private static bool Solve3(double a11, double a12, double a13, double a21, double a22, double a23, double a31, double a32, double a33,
            double b1, double b2, double b3, out double x1, out double x2, out double x3)
        {
            double det = a11 * (a22 * a33 - a23 * a32) - a12 * (a21 * a33 - a23 * a31) + a13 * (a21 * a32 - a22 * a31);
            x1 = x2 = x3 = 0;
            double scale = Math.Abs(a11) + Math.Abs(a22) + Math.Abs(a33);
            if (Math.Abs(det) <= 1e-30 * Math.Max(1, scale * scale * scale)) return false;
            x1 = (b1 * (a22 * a33 - a23 * a32) - a12 * (b2 * a33 - a23 * b3) + a13 * (b2 * a32 - a22 * b3)) / det;
            x2 = (a11 * (b2 * a33 - a23 * b3) - b1 * (a21 * a33 - a23 * a31) + a13 * (a21 * b3 - b2 * a31)) / det;
            x3 = (a11 * (a22 * b3 - b2 * a32) - a12 * (a21 * b3 - b2 * a31) + b1 * (a21 * a32 - a22 * a31)) / det;
            return true;
        }
    }
}
