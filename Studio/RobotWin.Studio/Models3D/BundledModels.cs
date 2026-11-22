using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// Real part models shipped with the app (Models3D/Bundled/*.obj): converted from the makers'
    /// STEP files, in millimetres, already placed in the part's model frame (right-handed, z up).
    /// Smooth faces stay smooth (normals averaged over faces within 40°), edges stay sharp.
    /// </summary>
    internal static class BundledModels
    {
        private static readonly Dictionary<string, MeshModel?> Cache = new Dictionary<string, MeshModel?>(StringComparer.Ordinal);
        private static readonly object Gate = new object();
        private static readonly double Crease = Math.Cos(40 * Math.PI / 180);

        /// <summary>Set false to draw the generated models instead (tests, comparisons).</summary>
        internal static bool Enabled { get; set; } = true;

        internal static MeshModel? Get(string name)
        {
            if (!Enabled) return null;
            lock (Gate)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;
                var m = Read(name);
                Cache[name] = m;
                return m;
            }
        }

        private static MeshModel? Read(string name)
        {
            using var s = typeof(BundledModels).Assembly.GetManifestResourceStream("RobotWin.Studio.Models3D.Bundled." + name + ".obj");
            if (s == null) return null;
            try
            {
                return MeshModel.LoadObj(s, new MeshLoadOptions { Units = MeshUnits.Millimeter });
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Draws a bundled model into <paramref name="b"/> (or a moving sub-part). Triangles whose colour is
        /// near <paramref name="bodyRgb"/> become the per-instance body (heat glow, charring);
        /// <paramref name="material"/> may give any colour its own material (metal can, brass), and
        /// <paramref name="recolour"/> changes colours (a blue SG90 from the same case).
        /// </summary>
        internal static bool Draw(PartBuilder b, string name, uint? bodyRgb = null, Func<Color, Material?>? material = null, Func<Color, Color>? recolour = null, double spec = 0.25)
        {
            var mesh = Get(name);
            if (mesh == null) return false;
            int n = mesh.TriangleCount;
            var raw = new Vector3D[n];
            var unit = new Vector3D[n];
            var adj = new List<int>[mesh.Vertices.Count];
            for (int t = 0; t < n; t++)
            {
                var (a, c1, c2) = mesh.Triangle(t);
                var nn = Vector3D.CrossProduct(new Vector3D(c1.X - a.X, c1.Y - a.Y, c1.Z - a.Z), new Vector3D(c2.X - a.X, c2.Y - a.Y, c2.Z - a.Z));
                raw[t] = nn;
                if (nn.LengthSquared > 0) nn.Normalize();
                unit[t] = nn;
                for (int k = 0; k < 3; k++)
                {
                    int v = mesh.Indices[3 * t + k];
                    (adj[v] ??= new List<int>(6)).Add(t);
                }
            }

            // Undo the builder's layout flip (the files are in the model frame) and go back to millimetres.
            using (b.At(Mx.S(1000) * Mx.S(1, -1, 1)))
            {
                var byColour = new Dictionary<uint, MeshBuilder>();
                for (int t = 0; t < n; t++)
                {
                    if (unit[t].LengthSquared == 0) continue;
                    var mc = mesh.ColorOf(t);
                    uint key = mc.ToArgb();
                    if (!byColour.TryGetValue(key, out var mb))
                    {
                        var col = Color.FromRgb(mc.R, mc.G, mc.B);
                        var own = material?.Invoke(col);
                        bool body = bodyRgb.HasValue && Near(col, bodyRgb.Value);
                        if (recolour != null) col = recolour(col);
                        mb = body ? b.Body(col, spec) : own != null ? b[own] : b[Mat.Plastic(col, spec, 25)];
                        byColour[key] = mb;
                    }

                    int i0 = -1, i1 = -1, i2 = -1;
                    for (int k = 0; k < 3; k++)
                    {
                        int v = mesh.Indices[3 * t + k];
                        var sum = new Vector3D();
                        foreach (int f in adj[v])
                            if (Vector3D.DotProduct(unit[f], unit[t]) >= Crease) sum += raw[f];
                        if (sum.LengthSquared == 0) sum = unit[t];
                        var p = mesh.Vertices[v];
                        int id = mb.V(new Point3D(p.X, p.Y, p.Z), sum, new Point(0, 0));
                        if (k == 0) i0 = id;
                        else if (k == 1) i1 = id;
                        else i2 = id;
                    }

                    mb.Tri(i0, i1, i2);
                }
            }

            return true;
        }

        private static bool Near(Color c, uint rgb) =>
            Math.Abs(c.R - (int)((rgb >> 16) & 0xFF)) <= 6 && Math.Abs(c.G - (int)((rgb >> 8) & 0xFF)) <= 6 && Math.Abs(c.B - (int)(rgb & 0xFF)) <= 6;

        /// <summary>Light neutral grey: the metal of a motor can or shaft bushing.</summary>
        internal static bool IsBrightMetal(Color c) => c.R > 220 && c.G > 220 && c.B > 220 && Math.Abs(c.R - c.B) < 8;
    }
}
