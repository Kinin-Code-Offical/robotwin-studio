using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [surface] The look of a surface finish on a world part: a texture drawn at real size (wood grain, carbon
    /// twill, brushed lines, kraft fibres, 0.2 mm print layers, rust) tinted with the part's colour, and the
    /// shine of the finish. Textures are projected per triangle on the plane its normal faces most, in
    /// millimetres, so boxes, cylinders, wings and imported models all take them without their own UVs.
    /// Flat colours only: every texture is made of solid strokes.
    /// </summary>
    internal static class SurfaceLook
    {
        private static readonly Dictionary<string, BitmapSource> Tiles = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);

        /// <summary>Real size of one texture tile (m) for a finish.</summary>
        private static double TileSize(string id) => id switch
        {
            "ahsap" => 0.06,
            "karbon" => 0.012,
            "fircalanmis" => 0.02,
            "kraft" => 0.03,
            "baski" => 0.002,
            "oksitli" => 0.04,
            _ => 0.05
        };

        /// <summary>Specular intensity (0–1) and power of a finish.</summary>
        public static (double Intensity, double Power) Shine(string id) => id switch
        {
            "mat-boya" => (0.03, 10),
            "parlak-boya" => (0.6, 70),
            "parlatilmis" => (0.95, 110),
            "fircalanmis" => (0.5, 25),
            "eloksal" => (0.35, 35),
            "oksitli" => (0.05, 10),
            "ahsap" => (0.08, 15),
            "karbon" => (0.55, 60),
            "kraft" => (0.03, 8),
            "kaucuk" => (0.02, 6),
            "baski" => (0.18, 25),
            _ => (0.12, 40)
        };

        private static Color Shade(Color c, double k) =>
            Color.FromArgb(c.A, (byte)Math.Max(0, Math.Min(255, c.R * k)), (byte)Math.Max(0, Math.Min(255, c.G * k)), (byte)Math.Max(0, Math.Min(255, c.B * k)));

        private static Color Mix(Color a, Color b, double t) =>
            Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        /// <summary>The finish's texture tile tinted with <paramref name="c"/> (null for a plain finish).</summary>
        public static BitmapSource? Tile(string id, Color c)
        {
            if (id is not ("ahsap" or "karbon" or "fircalanmis" or "kraft" or "baski" or "oksitli")) return null;
            string key = id + "|" + c;
            lock (Tiles)
            {
                if (Tiles.TryGetValue(key, out var cached)) return cached;
            }

            const int n = 128;
            var rnd = new Random(id.GetHashCode() & 0x7FFF);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                void Rect(Color col, double x, double y, double w, double h) => dc.DrawRectangle(new SolidColorBrush(col), null, new Rect(x, y, w, h));
                Rect(c, 0, 0, n, n);
                switch (id)
                {
                    case "ahsap":
                        // Growth rings: wavy bands of late wood, a few pores.
                        for (int k = 0; k < 9; k++)
                        {
                            double y0 = k * n / 9.0 + rnd.NextDouble() * 4, amp = 3 + rnd.NextDouble() * 4, w = 2 + rnd.NextDouble() * 3;
                            var geo = new StreamGeometry();
                            using (var g = geo.Open())
                            {
                                g.BeginFigure(new Point(0, y0), true, true);
                                for (int x = 0; x <= n; x += 4) g.LineTo(new Point(x, y0 + amp * Math.Sin(x / (double)n * 2 * Math.PI + k)), true, false);
                                for (int x = n; x >= 0; x -= 4) g.LineTo(new Point(x, y0 + w + amp * Math.Sin(x / (double)n * 2 * Math.PI + k)), true, false);
                            }

                            dc.DrawGeometry(new SolidColorBrush(Shade(c, 0.78)), null, geo);
                        }

                        for (int k = 0; k < 40; k++) Rect(Shade(c, 0.7), rnd.Next(n), rnd.Next(n), 2, 1);
                        break;
                    case "karbon":
                        // 2×2 twill: tows running both ways, stepping one tow per row; dark resin gaps.
                        Rect(Shade(c, 0.45), 0, 0, n, n);
                        int t = n / 8;
                        for (int r = 0; r < 8; r++)
                            for (int q = 0; q < 8; q++)
                            {
                                bool warp = ((q + r) / 2) % 2 == 0;
                                Rect(warp ? Shade(c, 1.35) : Shade(c, 0.95), q * t + 1, r * t + 1, t - 2, t - 2);
                                if (warp) Rect(Shade(c, 1.6), q * t + 2, r * t + t / 2 - 1, t - 4, 1);
                                else Rect(Shade(c, 1.15), q * t + t / 2 - 1, r * t + 2, 1, t - 4);
                            }

                        break;
                    case "fircalanmis":
                        for (int y = 0; y < n; y++)
                            if (rnd.NextDouble() < 0.7) Rect(Shade(c, 0.9 + 0.2 * rnd.NextDouble()), 0, y, n, 1);
                        break;
                    case "kraft":
                        for (int k = 0; k < 900; k++) Rect(Shade(c, 0.85 + 0.25 * rnd.NextDouble()), rnd.Next(n), rnd.Next(n), 1 + rnd.Next(3), 1);
                        for (int k = 0; k < 30; k++) Rect(Shade(c, 0.7), rnd.Next(n), rnd.Next(n), 6 + rnd.Next(10), 1);
                        break;
                    case "baski":
                        // One 0.2 mm layer per 12.8 px band: a darker seam where the layers meet.
                        for (int k = 0; k < 10; k++) Rect(Shade(c, 0.82), 0, k * n / 10.0 + n / 10.0 - 3, n, 3);
                        break;
                    case "oksitli":
                        var rust = Color.FromRgb(0x8B, 0x4A, 0x22);
                        for (int k = 0; k < 220; k++)
                        {
                            double s = 2 + rnd.NextDouble() * 9;
                            dc.DrawEllipse(new SolidColorBrush(Mix(c, rust, 0.35 + 0.5 * rnd.NextDouble())), null, new Point(rnd.Next(n), rnd.Next(n)), s, s * 0.7);
                        }

                        break;
                }
            }

            var bmp = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            lock (Tiles) Tiles[key] = bmp;
            return bmp;
        }

        /// <summary>
        /// Gives the parts of <paramref name="model"/> drawn in the main colour <paramref name="c"/> the finish of
        /// <paramref name="o"/>: texture at real size and the finish's shine, keeping the per-instance heat glow.
        /// </summary>
        public static void Apply(Model3DGroup model, WorldObject o, Color c, EmissiveMaterial? heat)
        {
            var finish = SurfaceFinish.For(o.Parameters.TryGetValue("surface", out var s) ? s : null);
            if (finish == null) return;
            string id = finish.Id;
            var tile = Tile(id, c);
            var (spec, power) = Shine(id);
            Color shown = id switch
            {
                "eloksal" => Shade(c, 0.55),
                "parlatilmis" => Mix(c, Colors.White, 0.25),
                "kaucuk" => Mix(c, Color.FromRgb(0x22, 0x22, 0x24), 0.7),
                _ => c
            };

            Material Make()
            {
                var g = new MaterialGroup();
                if (tile != null)
                {
                    var ib = new ImageBrush(tile) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
                    RenderOptions.SetBitmapScalingMode(ib, BitmapScalingMode.HighQuality);
                    ib.Freeze();
                    g.Children.Add(new DiffuseMaterial(ib));
                }
                else g.Children.Add(new DiffuseMaterial(new SolidColorBrush(shown)));

                if (heat != null) g.Children.Add(heat);
                if (spec > 0) g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb((byte)(spec * 255), 255, 255, 255)), power));
                return g;
            }

            Walk(model, c, tile != null ? TileSize(id) : 0, Make);
        }

        private static void Walk(Model3DGroup group, Color c, double tile, Func<Material> make)
        {
            for (int i = 0; i < group.Children.Count; i++)
            {
                switch (group.Children[i])
                {
                    case Model3DGroup sub when !sub.IsFrozen:
                        Walk(sub, c, tile, make);
                        break;
                    case GeometryModel3D gm when !gm.IsFrozen && MainColour(gm.Material) is { } col && Near(col, c) && gm.Geometry is MeshGeometry3D mesh:
                        gm.Geometry = tile > 0 ? Project(mesh, tile) : mesh;
                        gm.Material = make();
                        break;
                }
            }
        }

        private static Color? MainColour(Material? m)
        {
            switch (m)
            {
                case DiffuseMaterial d when d.Brush is SolidColorBrush b: return b.Color;
                case MaterialGroup g:
                    foreach (var x in g.Children)
                        if (MainColour(x) is { } c) return c;
                    return null;
                default: return null;
            }
        }

        private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) <= 2 && Math.Abs(a.G - b.G) <= 2 && Math.Abs(a.B - b.B) <= 2;

        /// <summary>Unshared copy of a mesh with UVs on the plane each triangle faces most, one unit per <paramref name="tile"/> metres.</summary>
        private static MeshGeometry3D Project(MeshGeometry3D src, double tile)
        {
            var m = new MeshGeometry3D();
            var pos = src.Positions;
            var idx = src.TriangleIndices;
            int count = idx.Count > 0 ? idx.Count : pos.Count;
            bool hasNormals = src.Normals.Count == pos.Count;
            for (int t = 0; t + 2 < count; t += 3)
            {
                int i0 = idx.Count > 0 ? idx[t] : t, i1 = idx.Count > 0 ? idx[t + 1] : t + 1, i2 = idx.Count > 0 ? idx[t + 2] : t + 2;
                var a = pos[i0];
                var b = pos[i1];
                var d = pos[i2];
                var n = Vector3D.CrossProduct(b - a, d - a);
                double ax = Math.Abs(n.X), ay = Math.Abs(n.Y), az = Math.Abs(n.Z);
                Point Uv(Point3D p) => ax >= ay && ax >= az ? new Point(p.Y / tile, p.Z / tile) : ay >= az ? new Point(p.X / tile, p.Z / tile) : new Point(p.X / tile, p.Y / tile);
                foreach (int k in new[] { i0, i1, i2 })
                {
                    m.Positions.Add(pos[k]);
                    if (hasNormals) m.Normals.Add(src.Normals[k]);
                    m.TextureCoordinates.Add(Uv(pos[k]));
                    m.TriangleIndices.Add(m.Positions.Count - 1);
                }
            }

            m.Freeze();
            return m;
        }
    }
}
