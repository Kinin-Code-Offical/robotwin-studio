using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Modeling;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Splits a concave solid into convex pieces for collision, so that a tray, a cup, a tube or a U bracket
    /// can hold or surround other bodies the way the real part does (a single convex hull would fill the
    /// hollow). The solid is cut with planes into closed pieces (<see cref="MeshCut.Slice"/>), greedily:
    /// the piece whose hull overshoots it most is split by the plane that removes the most hull volume.
    /// Candidate planes are the axis planes through its vertex coordinates (walls and steps of CAD parts)
    /// and planes through its deepest notch (the inside of a bend or a bore). Splitting stops once every
    /// piece's hull exceeds the piece by less than <c>tolerance</c> of the whole solid's volume.
    /// </summary>
    public static class ConvexDecomposition
    {
        /// <summary>Hull volume allowed in excess of the solid, as a fraction of the solid's volume.</summary>
        public const double DefaultTolerance = 0.03;

        public const int DefaultMaxPieces = 24;

        private const int AxisCandidates = 12;

        private static readonly Dictionary<long, List<List<Vec3>>> Cache = new Dictionary<long, List<List<Vec3>>>();
        private static readonly object CacheLock = new object();

        /// <summary>
        /// Convex pieces of <paramref name="mesh"/> as point sets in the mesh frame. One set (all vertices)
        /// when the mesh is convex enough, open (not watertight) or cannot be cut cleanly.
        /// </summary>
        public static List<List<Vec3>> Decompose(MeshModel mesh, double tolerance = DefaultTolerance, int maxPieces = DefaultMaxPieces)
        {
            long key = Hash(mesh, tolerance, maxPieces);
            lock (CacheLock)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached.Select(p => new List<Vec3>(p)).ToList();
            }

            var result = Compute(mesh, tolerance, maxPieces);
            lock (CacheLock)
            {
                if (Cache.Count > 256) Cache.Clear();
                Cache[key] = result.Select(p => new List<Vec3>(p)).ToList();
            }

            return result;
        }

        private sealed class Piece
        {
            public PolyMesh Mesh = null!;
            public double Volume;
            public double HullVolume;
            public bool Final;
            public double Excess => HullVolume - Volume;
        }

        private static List<List<Vec3>> Compute(MeshModel mesh, double tolerance, int maxPieces)
        {
            var whole = new List<List<Vec3>> { new List<Vec3>(mesh.Vertices) };
            if (maxPieces < 2 || mesh.Vertices.Count < 8) return whole;
            PolyMesh poly;
            try
            {
                poly = PolyMesh.FromMeshModel(mesh, 1.0, 1e-9 * Math.Max(1e-6, mesh.Bounds.Size.Length));
            }
            catch (ArgumentException)
            {
                return whole;
            }

            if (!Watertight(poly)) return whole;
            double volume = poly.Volume();
            if (volume <= 0) return whole;
            double hull = HullVolume(poly.Vertices);
            double allowed = tolerance * volume;
            if (hull - volume <= allowed) return whole;

            var pieces = new List<Piece> { new Piece { Mesh = poly, Volume = volume, HullVolume = hull } };
            while (pieces.Count < maxPieces)
            {
                Piece? worst = null;
                foreach (var p in pieces)
                {
                    if (!p.Final && p.Excess > allowed && (worst == null || p.Excess > worst.Excess)) worst = p;
                }

                if (worst == null) break;
                var split = BestSplit(worst, volume);
                if (split == null)
                {
                    worst.Final = true;
                    continue;
                }

                pieces.Remove(worst);
                pieces.AddRange(split);
            }

            return pieces.Count < 2 ? whole : pieces.Select(p => new List<Vec3>(p.Mesh.Vertices)).ToList();
        }

        /// <summary>
        /// The cut that leaves the least hull volume (the pieces on each side split into their separate parts:
        /// a plane through a U leaves two arms, never one piece whose hull bridges the gap). A cut along the plane
        /// of a face at an inside corner wins a near tie, and goes ahead even when no cut shaves off hull yet
        /// (a tray's floor and walls only lose hull volume once all of them are apart).
        /// </summary>
        private static List<Piece>? BestSplit(Piece piece, double total)
        {
            double size = Math.Max(1e-9, piece.Mesh.Bounds.Size.Length);
            var reflex = ReflexPlanes(piece.Mesh, size);
            // A part with a few flat inside faces (a tray, a bracket, a box with a pocket) is taken apart exactly
            // along those faces; curved insides (a bore, a bend) have many and are searched for the best plane.
            if (reflex.Count > 0 && reflex.Count <= MaxStructural)
            {
                var exact = Evaluate(piece, total, reflex.Select(r => (r.Point, r.Normal, true)), double.MaxValue, out _);
                if (exact != null) return exact;
            }

            var candidates = Candidates(piece.Mesh).Concat(reflex.Take(AxisCandidates).Select(r => (r.Point, r.Normal, true)));
            var best = Evaluate(piece, total, candidates, piece.HullVolume * (1 - 1e-3) - 1e-4 * total, out var structural);
            return best ?? structural;
        }

        private const int MaxStructural = 8;

        /// <summary>
        /// Tries the planes; returns the parts of the cut with the least hull volume below <paramref name="limit"/>
        /// (null if none), and in <paramref name="structural"/> the best cut along an inside face whatever its score.
        /// </summary>
        private static List<Piece>? Evaluate(Piece piece, double total, IEnumerable<(Vec3 Point, Vec3 Normal, bool Structural)> planes, double limit, out List<Piece>? structural)
        {
            List<Piece>? best = null;
            structural = null;
            double bestScore = limit;
            double structuralScore = double.MaxValue;
            foreach (var (point, normal, isStructural) in planes)
            {
                PolyMesh a, b;
                try
                {
                    (a, b) = MeshCut.Slice(piece.Mesh, point, normal);
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is IndexOutOfRangeException)
                {
                    continue;
                }

                if (a.IsEmpty || b.IsEmpty || !Watertight(a) || !Watertight(b)) continue;
                double va = a.Volume(), vb = b.Volume();
                if (va <= 1e-6 * total || vb <= 1e-6 * total || Math.Abs(va + vb - piece.Volume) > 1e-6 * total + 1e-9 * piece.Volume) continue;
                var parts = Parts(a).Concat(Parts(b)).Where(x => x.Volume > 1e-7 * total).ToList();
                if (parts.Count < 2) continue;
                double score = parts.Sum(x => x.HullVolume);
                if (isStructural && score < structuralScore)
                {
                    structuralScore = score;
                    structural = parts;
                }

                if (score >= bestScore) continue;
                bestScore = score;
                best = parts;
            }

            if (structural != null && best != null && structuralScore <= bestScore + 0.01 * piece.HullVolume) return structural;
            return best;
        }

        /// <summary>The separate solids of a mesh (vertex-connected face groups), each with its volume and hull.</summary>
        private static IEnumerable<Piece> Parts(PolyMesh m)
        {
            var parent = Enumerable.Range(0, m.Vertices.Count).ToArray();
            int Find(int x)
            {
                while (parent[x] != x) x = parent[x] = parent[parent[x]];
                return x;
            }

            foreach (var f in m.Faces)
            {
                for (int k = 1; k < f.Length; k++)
                {
                    int ra = Find(f[0]), rb = Find(f[k]);
                    if (ra != rb) parent[ra] = rb;
                }
            }

            var groups = new Dictionary<int, List<int[]>>();
            foreach (var f in m.Faces)
            {
                int r = Find(f[0]);
                if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int[]>();
                l.Add(f);
            }

            if (groups.Count == 1)
            {
                yield return new Piece { Mesh = m, Volume = m.Volume(), HullVolume = HullVolume(m.Vertices) };
                yield break;
            }

            foreach (var g in groups.Values)
            {
                var sub = new PolyMesh();
                var map = new Dictionary<int, int>();
                foreach (var f in g)
                {
                    var nf = new int[f.Length];
                    for (int k = 0; k < f.Length; k++)
                    {
                        if (!map.TryGetValue(f[k], out int nv)) map[f[k]] = nv = sub.AddVertex(m.Vertices[f[k]]);
                        nf[k] = nv;
                    }

                    sub.Faces.Add(nf);
                }

                yield return new Piece { Mesh = sub, Volume = sub.Volume(), HullVolume = HullVolume(sub.Vertices) };
            }
        }

        private static IEnumerable<(Vec3 Point, Vec3 Normal, bool Structural)> Candidates(PolyMesh m)
        {
            var bounds = m.Bounds;
            double size = Math.Max(1e-9, bounds.Size.Length);
            for (int axis = 0; axis < 3; axis++)
            {
                var n = axis == 0 ? Vec3.UnitX : axis == 1 ? Vec3.UnitY : Vec3.UnitZ;
                double lo = Coord(bounds.Min, axis), hi = Coord(bounds.Max, axis);
                double tol = 1e-6 * size;
                var coords = m.Vertices.Select(v => Coord(v, axis)).Where(c => c > lo + tol && c < hi - tol).OrderBy(c => c).ToList();
                var distinct = new List<double>();
                foreach (double c in coords)
                {
                    if (distinct.Count == 0 || c - distinct[distinct.Count - 1] > tol) distinct.Add(c);
                }

                if (distinct.Count > AxisCandidates)
                {
                    var thin = new List<double>(AxisCandidates);
                    for (int k = 0; k < AxisCandidates; k++) thin.Add(distinct[(int)((k + 0.5) * distinct.Count / AxisCandidates)]);
                    distinct = thin;
                }

                foreach (double c in distinct) yield return (WithCoord(bounds.Center, axis, c), n, false);
            }

            // planes through the deepest notch: the inside of a bend, the wall of a bore
            var hull = ConvexHull.Compute(m.Vertices);
            if (hull.IsDegenerate) yield break;
            Vec3 notch = default;
            double deepest = 0;
            foreach (var v in m.Vertices)
            {
                double d = hull.SignedDistance(v);
                if (d < deepest)
                {
                    deepest = d;
                    notch = v;
                }
            }

            if (deepest > -1e-6 * size) yield break;
            var centroid = Vec3.Zero;
            foreach (var v in hull.Vertices) centroid += v;
            centroid /= hull.Vertices.Count;
            var toNotch = notch - centroid;
            for (int axis = 0; axis < 3; axis++)
            {
                var e = axis == 0 ? Vec3.UnitX : axis == 1 ? Vec3.UnitY : Vec3.UnitZ;
                yield return (notch, e, false);
                var radial = toNotch.Cross(e);
                if (radial.Length > 1e-9 * size) yield return (notch, radial.Normalized(), false);
            }
        }

        /// <summary>
        /// Planes of the faces at reflex (inside) edges, where the surface folds inward: cutting along them is
        /// how a polyhedron is taken apart into convex pieces. Largest faces first, duplicates merged.
        /// </summary>
        private static List<(Vec3 Point, Vec3 Normal)> ReflexPlanes(PolyMesh m, double size)
        {
            var faceOf = new Dictionary<long, int>();
            for (int f = 0; f < m.Faces.Count; f++)
            {
                var loop = m.Faces[f];
                for (int k = 0; k < loop.Length; k++) faceOf[(long)loop[k] << 32 | (uint)loop[(k + 1) % loop.Length]] = f;
            }

            var normals = new Vec3[m.Faces.Count];
            var centres = new Vec3[m.Faces.Count];
            for (int f = 0; f < m.Faces.Count; f++)
            {
                normals[f] = m.FaceNormal(f);
                centres[f] = m.FaceCenter(f);
            }

            var faces = new HashSet<int>();
            double tol = 1e-7 * size;
            foreach (var kv in faceOf)
            {
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                if (a > b || !faceOf.TryGetValue((long)b << 32 | (uint)a, out int g)) continue;
                int f = kv.Value;
                // reflex: each face's centre lies outside the other's plane
                if ((centres[g] - m.Vertices[a]).Dot(normals[f]) > tol && (centres[f] - m.Vertices[a]).Dot(normals[g]) > tol)
                {
                    faces.Add(f);
                    faces.Add(g);
                }
            }

            var planes = new List<(Vec3, Vec3, double Area)>();
            foreach (int f in faces.OrderByDescending(m.FaceArea))
            {
                var n = normals[f];
                bool dup = false;
                foreach (var (pp, pn, _) in planes)
                {
                    if (Math.Abs(pn.Dot(n)) > 1 - 1e-9 && Math.Abs(pn.Dot(centres[f] - pp)) < tol)
                    {
                        dup = true;
                        break;
                    }
                }

                if (dup) continue;
                planes.Add((centres[f], n, m.FaceArea(f)));
            }

            return planes.Select(p => (p.Item1, p.Item2)).ToList();
        }

        private static double Coord(Vec3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static Vec3 WithCoord(Vec3 v, int axis, double c) => axis == 0 ? new Vec3(c, v.Y, v.Z) : axis == 1 ? new Vec3(v.X, c, v.Z) : new Vec3(v.X, v.Y, c);

        /// <summary>Volume of the convex hull of the points (0 when they span no volume).</summary>
        public static double HullVolume(IReadOnlyList<Vec3> points)
        {
            if (points.Count < 4) return 0;
            var hull = ConvexHull.Compute(points);
            if (hull.IsDegenerate) return 0;
            var v = hull.Vertices;
            var idx = hull.Indices;
            double six = 0;
            for (int i = 0; i + 2 < idx.Count; i += 3) six += v[idx[i]].Dot(v[idx[i + 1]].Cross(v[idx[i + 2]]));
            return Math.Abs(six) / 6;
        }

        /// <summary>Every edge used once in each direction: a closed, consistently wound surface.</summary>
        private static bool Watertight(PolyMesh m)
        {
            if (m.Faces.Count < 4) return false;
            var directed = new Dictionary<long, int>();
            foreach (var f in m.Faces)
            {
                for (int k = 0; k < f.Length; k++)
                {
                    int a = f[k], b = f[(k + 1) % f.Length];
                    if (a == b) continue;
                    long key = (long)a << 32 | (uint)b;
                    directed[key] = directed.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }

            foreach (var kv in directed)
            {
                if (kv.Value != 1) return false;
                int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xFFFFFFFF);
                if (!directed.TryGetValue((long)b << 32 | (uint)a, out int back) || back != 1) return false;
            }

            return true;
        }

        private static long Hash(MeshModel mesh, double tolerance, int maxPieces)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                void Mix(double x)
                {
                    h ^= BitConverter.DoubleToInt64Bits(x);
                    h *= 1099511628211L;
                }

                Mix(tolerance);
                Mix(maxPieces);
                Mix(mesh.Vertices.Count);
                foreach (var v in mesh.Vertices)
                {
                    Mix(v.X);
                    Mix(v.Y);
                    Mix(v.Z);
                }

                foreach (int i in mesh.Indices) Mix(i);
                return h;
            }
        }
    }
}
