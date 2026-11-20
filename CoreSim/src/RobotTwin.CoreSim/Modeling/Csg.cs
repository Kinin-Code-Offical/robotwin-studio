using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    public enum BooleanOp
    {
        /// <summary>A ∪ B</summary>
        Union,

        /// <summary>A − B</summary>
        Difference,

        /// <summary>A ∩ B</summary>
        Intersection
    }

    /// <summary>
    /// Constructive solid geometry on closed polygon meshes with BSP trees (the csg.js
    /// scheme with coplanar-face handling), followed by welding, T-junction repair and
    /// coplanar-face dissolving so the result is a clean, watertight <see cref="PolyMesh"/>.
    /// Only the polygons near the other operand's bounding box take part in clipping,
    /// which keeps large meshes fast and untouched faces exact.
    /// </summary>
    public static class Csg
    {
        private readonly struct Plane
        {
            public Plane(Vec3 n, double w)
            {
                N = n;
                W = w;
            }

            public readonly Vec3 N;
            public readonly double W;
            public Plane Flipped => new Plane(-N, -W);
        }

        private sealed class Poly
        {
            public Poly(Vec3[] v, Plane p)
            {
                V = v;
                P = p;
            }

            public readonly Vec3[] V;
            public readonly Plane P;

            public Poly Flipped()
            {
                var r = (Vec3[])V.Clone();
                Array.Reverse(r);
                return new Poly(r, P.Flipped);
            }
        }

        private sealed class Node
        {
            public Plane Plane;
            public Node? Front, Back;
        }

        private const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

        public static PolyMesh Union(PolyMesh a, PolyMesh b) => Apply(a, b, BooleanOp.Union);
        public static PolyMesh Difference(PolyMesh a, PolyMesh b) => Apply(a, b, BooleanOp.Difference);
        public static PolyMesh Intersection(PolyMesh a, PolyMesh b) => Apply(a, b, BooleanOp.Intersection);

        /// <summary>Applies the operation with each tool in turn (A − B − C …, A ∪ B ∪ C …).</summary>
        public static PolyMesh Apply(PolyMesh target, IEnumerable<PolyMesh> tools, BooleanOp op)
        {
            var result = target;
            foreach (var t in tools) result = Apply(result, t, op);
            return result;
        }

        public static PolyMesh Apply(PolyMesh a, PolyMesh b, BooleanOp op)
        {
            var ba = a.Bounds;
            var bb = b.Bounds;
            double size = Math.Max(1e-9, Math.Max(MaxExtent(ba), MaxExtent(bb)));
            double eps = Math.Max(1e-8, size * 1e-7);
            double weldTol = Math.Max(1e-6, size * 1e-6);

            if (a.IsEmpty || b.IsEmpty || !ba.Expanded(weldTol).Intersects(bb))
            {
                // Disjoint operands: trivial results without any clipping.
                switch (op)
                {
                    case BooleanOp.Union:
                        var u = a.Clone();
                        u.Append(b);
                        return u;
                    case BooleanOp.Difference:
                        return a.Clone();
                    default:
                        return new PolyMesh();
                }
            }

            var polysA = ToPolys(a, eps);
            var polysB = ToPolys(b, eps);
            var treeA = Build(polysA, eps);
            var treeB = Build(polysB, eps);
            var boxA = ba.Expanded(weldTol * 10);
            var boxB = bb.Expanded(weldTol * 10);
            Split(polysA, boxB, out var nearA, out var farA);
            Split(polysB, boxA, out var nearB, out var farB);

            var result = new List<Poly>();
            switch (op)
            {
                case BooleanOp.Union:
                {
                    result.AddRange(farA);
                    result.AddRange(farB);
                    result.AddRange(Clip(treeB, nearA, false, eps));
                    var bOut = Clip(treeA, nearB, false, eps);
                    // Drop B faces coplanar with (and facing the same way as) A faces.
                    result.AddRange(Flip(Clip(treeA, Flip(bOut), false, eps)));
                    break;
                }

                case BooleanOp.Difference:
                {
                    result.AddRange(farA);
                    result.AddRange(Flip(Clip(treeB, Flip(nearA), false, eps)));
                    var bIn = Clip(treeA, nearB, true, eps);
                    result.AddRange(Clip(treeA, Flip(bIn), true, eps));
                    break;
                }

                default:
                {
                    var bIn = Clip(treeA, nearB, true, eps);
                    result.AddRange(Flip(Clip(treeB, Flip(nearA), true, eps)));
                    result.AddRange(Flip(Clip(treeA, Flip(bIn), true, eps)));
                    break;
                }
            }

            return ToMesh(result, weldTol);
        }

        private static double MaxExtent(Aabb b) => b.IsEmpty ? 0 : Math.Max(b.Size.X, Math.Max(b.Size.Y, b.Size.Z));

        private static List<Poly> Flip(List<Poly> polys)
        {
            var r = new List<Poly>(polys.Count);
            foreach (var p in polys) r.Add(p.Flipped());
            return r;
        }

        private static void Split(List<Poly> polys, Aabb box, out List<Poly> near, out List<Poly> far)
        {
            near = new List<Poly>();
            far = new List<Poly>();
            foreach (var p in polys)
            {
                var b = Aabb.FromPoints(p.V);
                if (b.Intersects(box)) near.Add(p);
                else far.Add(p);
            }
        }

        private static List<Poly> ToPolys(PolyMesh m, double eps)
        {
            var list = new List<Poly>(m.Faces.Count);
            for (int fi = 0; fi < m.Faces.Count; fi++)
            {
                var f = m.Faces[fi];
                if (f.Length < 3) continue;
                var pts = new Vec3[f.Length];
                for (int i = 0; i < f.Length; i++) pts[i] = m.Vertices[f[i]];
                var n = PolyMesh.NewellNormal(pts);
                if (n.Length < eps * eps) continue;
                if (IsConvexPlanar(pts, n.Normalized(), eps))
                {
                    var nn = n.Normalized();
                    list.Add(new Poly(pts, new Plane(nn, nn.Dot(Centroid(pts)))));
                    continue;
                }

                // Concave or non-planar faces: use triangles (each exactly planar).
                var tri = Triangulator.TriangulatePolygon(pts, n);
                for (int t = 0; t + 2 < tri.Count; t += 3)
                {
                    var tp = new[] { pts[tri[t]], pts[tri[t + 1]], pts[tri[t + 2]] };
                    var tn = (tp[1] - tp[0]).Cross(tp[2] - tp[0]);
                    if (tn.Length < eps * eps) continue;
                    tn = tn.Normalized();
                    list.Add(new Poly(tp, new Plane(tn, tn.Dot(tp[0]))));
                }
            }

            return list;
        }

        private static Vec3 Centroid(Vec3[] pts)
        {
            var c = Vec3.Zero;
            foreach (var p in pts) c += p;
            return c / pts.Length;
        }

        private static bool IsConvexPlanar(Vec3[] pts, Vec3 n, double eps)
        {
            double w = n.Dot(pts[0]);
            foreach (var p in pts)
            {
                if (Math.Abs(n.Dot(p) - w) > eps * 0.25) return false;
            }

            for (int i = 0; i < pts.Length; i++)
            {
                var a = pts[(i - 1 + pts.Length) % pts.Length];
                var b = pts[i];
                var c = pts[(i + 1) % pts.Length];
                if ((b - a).Cross(c - b).Dot(n) < -eps) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ BSP

        private static Node? Build(List<Poly> polys, double eps)
        {
            if (polys.Count == 0) return null;
            var root = new Node();
            var stack = new Stack<(Node Node, List<Poly> Polys)>();
            stack.Push((root, polys));
            while (stack.Count > 0)
            {
                var (node, list) = stack.Pop();
                int splitter = ChooseSplitter(list, eps);
                node.Plane = list[splitter].P;
                var front = new List<Poly>();
                var back = new List<Poly>();
                var coplanar = new List<Poly>();
                // The splitter always stays in this node, so every level consumes a polygon (termination).
                for (int i = 0; i < list.Count; i++)
                {
                    if (i == splitter) continue;
                    SplitPoly(node.Plane, list[i], coplanar, coplanar, front, back, eps);
                }
                if (front.Count > 0)
                {
                    node.Front = new Node();
                    stack.Push((node.Front, front));
                }

                if (back.Count > 0)
                {
                    node.Back = new Node();
                    stack.Push((node.Back, back));
                }
            }

            return root;
        }

        /// <summary>Picks, among a few candidates, the plane that splits the fewest polygons.</summary>
        private static int ChooseSplitter(List<Poly> list, double eps)
        {
            if (list.Count <= 3) return 0;
            int candidates = Math.Min(5, list.Count);
            int best = 0;
            int bestScore = int.MaxValue;
            int sample = Math.Min(list.Count, 64);
            for (int c = 0; c < candidates; c++)
            {
                int idx = c * list.Count / candidates;
                var plane = list[idx].P;
                int splits = 0, front = 0, back = 0;
                for (int s = 0; s < sample; s++)
                {
                    var p = list[s * list.Count / sample];
                    int type = 0;
                    foreach (var v in p.V)
                    {
                        double t = plane.N.Dot(v) - plane.W;
                        type |= t < -eps ? Back : t > eps ? Front : Coplanar;
                    }

                    if (type == Spanning) splits++;
                    else if (type == Front) front++;
                    else if (type == Back) back++;
                }

                int score = splits * 8 + Math.Abs(front - back);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = idx;
                }
            }

            return best;
        }

        /// <summary>
        /// Splits a polygon by a plane. Coplanar polygons go to coplanarFront/coplanarBack by
        /// orientation (or to front/back when those lists are null).
        /// </summary>
        private static void SplitPoly(Plane plane, Poly poly, List<Poly>? coFront, List<Poly>? coBack, List<Poly> front, List<Poly> back, double eps)
        {
            int n = poly.V.Length;
            int polyType = 0;
            Span<int> types = n <= 64 ? stackalloc int[n] : new int[n];
            for (int i = 0; i < n; i++)
            {
                double t = plane.N.Dot(poly.V[i]) - plane.W;
                int type = t < -eps ? Back : t > eps ? Front : Coplanar;
                polyType |= type;
                types[i] = type;
            }

            switch (polyType)
            {
                case Coplanar:
                    if (plane.N.Dot(poly.P.N) > 0) (coFront ?? front).Add(poly);
                    else (coBack ?? back).Add(poly);
                    break;
                case Front:
                    front.Add(poly);
                    break;
                case Back:
                    back.Add(poly);
                    break;
                default:
                {
                    var f = new List<Vec3>(n + 1);
                    var b = new List<Vec3>(n + 1);
                    for (int i = 0; i < n; i++)
                    {
                        int j = (i + 1) % n;
                        int ti = types[i], tj = types[j];
                        var vi = poly.V[i];
                        var vj = poly.V[j];
                        if (ti != Back) f.Add(vi);
                        if (ti != Front) b.Add(vi);
                        if ((ti | tj) == Spanning)
                        {
                            // Interpolate from the lexicographically smaller end so both faces
                            // sharing this edge compute bit-identical points.
                            Vec3 p0 = vi, p1 = vj;
                            if (Less(vj, vi)) (p0, p1) = (vj, vi);
                            double t = (plane.W - plane.N.Dot(p0)) / plane.N.Dot(p1 - p0);
                            var v = p0 + (p1 - p0) * t;
                            f.Add(v);
                            b.Add(v);
                        }
                    }

                    if (f.Count >= 3) front.Add(new Poly(f.ToArray(), poly.P));
                    if (b.Count >= 3) back.Add(new Poly(b.ToArray(), poly.P));
                    break;
                }
            }
        }

        private static bool Less(Vec3 a, Vec3 b) => a.X != b.X ? a.X < b.X : a.Y != b.Y ? a.Y < b.Y : a.Z < b.Z;

        /// <summary>
        /// Removes the parts of <paramref name="polys"/> inside the solid of the tree
        /// (inside its complement when <paramref name="inverted"/>).
        /// </summary>
        private static List<Poly> Clip(Node? root, List<Poly> polys, bool inverted, double eps)
        {
            var result = new List<Poly>();
            if (root == null)
            {
                // Empty solid: everything is outside (inverted: everything is inside the complement).
                if (!inverted) result.AddRange(polys);
                return result;
            }

            var stack = new Stack<(Node Node, List<Poly> Polys)>();
            stack.Push((root, polys));
            while (stack.Count > 0)
            {
                var (node, list) = stack.Pop();
                var plane = inverted ? node.Plane.Flipped : node.Plane;
                var frontChild = inverted ? node.Back : node.Front;
                var backChild = inverted ? node.Front : node.Back;
                var front = new List<Poly>();
                var back = new List<Poly>();
                foreach (var p in list) SplitPoly(plane, p, front, back, front, back, eps);
                if (front.Count > 0)
                {
                    if (frontChild != null) stack.Push((frontChild, front));
                    else result.AddRange(front);
                }

                if (back.Count > 0 && backChild != null) stack.Push((backChild, back));
            }

            return result;
        }

        // ------------------------------------------------------------------ result

        private static PolyMesh ToMesh(List<Poly> polys, double tol)
        {
            var m = new PolyMesh();
            foreach (var p in polys)
            {
                int[] f = new int[p.V.Length];
                for (int i = 0; i < f.Length; i++) f[i] = m.AddVertex(p.V[i]);
                m.Faces.Add(f);
            }

            Cleanup(m, tol);
            return m;
        }

        /// <summary>Weld, T-junction repair, coplanar dissolve and collinear-vertex removal.</summary>
        public static void Cleanup(PolyMesh m, double tol)
        {
            m.Weld(tol);
            m.RemoveDegenerateFaces(tol * tol);
            for (int pass = 0; pass < 3; pass++)
            {
                if (m.FixTJunctions(tol) == 0) break;
            }

            m.DissolveCoplanar(1e-3, tol * 10);
            m.RemoveCollinearVertices(tol);
            m.RemoveDegenerateFaces(tol * tol);
        }
    }
}
