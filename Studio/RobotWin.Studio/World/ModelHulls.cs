using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [hull] Rigid-body shape of an object taken from its own 3D model: the convex hull of the
    /// vertices of every solid piece of the model (object frame, metres). Thin pieces (leads,
    /// header pins, cable pigtails) and the moving output that the physics turns into a separate
    /// rotor (servo horn, motor shaft) are left out. A model's explicit collision bounds clip the
    /// rest; when those reach lower than the solid pieces (pin tails or feet standing on the
    /// floor) the hull's bottom is carried down to them, as the bounding box used to be.
    /// </summary>
    public static class ModelHulls
    {
        /// <summary>A piece whose two smaller extents are both under this is a wire or pin, not a body (m).</summary>
        public const double ThinPiece = 0.004;

        /// <summary>Hull vertex budget; finer models are snapped to a coarser grid until they fit.</summary>
        public const int MaxVertices = 120;

        private static readonly ConditionalWeakTable<ObjectModel, List<Vec3>> Cache = new ConditionalWeakTable<ObjectModel, List<Vec3>>();

        /// <summary>Hull points of the model (cached per built model); null when it spans no volume.</summary>
        public static List<Vec3>? Of(ObjectModel model)
        {
            if (!Cache.TryGetValue(model, out var points))
            {
                points = Compute(model);
                Cache.AddOrUpdate(model, points);
            }

            return points.Count >= 4 ? points : null;
        }

        private static List<Vec3> Compute(ObjectModel m)
        {
            var skip = new HashSet<Model3D>();
            if (m.Moving != null) skip.Add(m.Moving);
            if (m.PortGroup != null) skip.Add(m.PortGroup);
            if (m.Instance?.HaloModel is { } halo) skip.Add(halo);
            var clip = m.CollisionBounds;
            var raw = new List<Point3D>();
            Walk(m.Body, Matrix3D.Identity, skip, clip, raw);
            if (raw.Count < 4) return new List<Vec3>();

            // Pin tails / feet under the body: the bounding box used to stand on them, so the hull does too.
            if (!m.CollisionBounds.IsEmpty)
            {
                double floor = m.CollisionBounds.Z;
                double low = raw.Min(p => p.Z);
                if (low > floor + 0.0005)
                {
                    int n = raw.Count;
                    for (int i = 0; i < n; i++)
                        if (raw[i].Z <= low + 0.001) raw.Add(new Point3D(raw[i].X, raw[i].Y, floor));
                }
            }

            for (double grid = 0.0001; grid <= 0.008; grid *= 2)
            {
                var hull = ConvexHull.Compute(Snap(raw, grid));
                if (hull.IsDegenerate) return new List<Vec3>();
                if (hull.Vertices.Count <= MaxVertices || grid * 2 > 0.008) return hull.Vertices.ToList();
            }

            return new List<Vec3>();
        }

        private static List<Vec3> Snap(List<Point3D> raw, double grid)
        {
            var seen = new HashSet<(long, long, long)>();
            var result = new List<Vec3>();
            foreach (var p in raw)
            {
                var key = ((long)Math.Round(p.X / grid), (long)Math.Round(p.Y / grid), (long)Math.Round(p.Z / grid));
                if (seen.Add(key)) result.Add(new Vec3(key.Item1 * grid, key.Item2 * grid, key.Item3 * grid));
            }

            return result;
        }

        private static void Walk(Model3D model, Matrix3D parent, HashSet<Model3D> skip, Rect3D clip, List<Point3D> output)
        {
            if (skip.Contains(model)) return;
            var m = model.Transform?.Value ?? Matrix3D.Identity;
            m.Append(parent);
            switch (model)
            {
                case Model3DGroup g:
                    foreach (var c in g.Children) Walk(c, m, skip, clip, output);
                    break;
                case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh && mesh.Positions.Count > 0:
                    AddMesh(mesh, m, clip, output);
                    break;
            }
        }

        private static void AddMesh(MeshGeometry3D mesh, Matrix3D m, Rect3D clip, List<Point3D> output)
        {
            // Thin pieces are measured in their own frame (a lead stays thin however it is turned).
            var lb = mesh.Bounds;
            double scale = Math.Cbrt(Math.Abs(m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31)));
            var dims = new[] { lb.SizeX * scale, lb.SizeY * scale, lb.SizeZ * scale };
            Array.Sort(dims);
            if (dims[1] < ThinPiece) return;

            var pts = new Point3D[mesh.Positions.Count];
            for (int i = 0; i < pts.Length; i++) pts[i] = m.Transform(mesh.Positions[i]);
            if (!clip.IsEmpty)
            {
                const double pad = 0.0005;
                var near = new Rect3D(clip.X - pad, clip.Y - pad, clip.Z - pad, clip.SizeX + 2 * pad, clip.SizeY + 2 * pad, clip.SizeZ + 2 * pad);
                var wb = new Rect3D(pts[0], new Size3D(0, 0, 0));
                foreach (var p in pts) wb.Union(p);
                if (!wb.IntersectsWith(near)) return;
                // Clamping a piece that crosses the bounds keeps its section there (exact for extrusions along an axis).
                for (int i = 0; i < pts.Length; i++)
                {
                    var p = pts[i];
                    pts[i] = new Point3D(
                        Math.Clamp(p.X, clip.X, clip.X + clip.SizeX),
                        Math.Clamp(p.Y, clip.Y, clip.Y + clip.SizeY),
                        Math.Clamp(p.Z, clip.Z, clip.Z + clip.SizeZ));
                }
            }

            output.AddRange(pts);
        }
    }
}
