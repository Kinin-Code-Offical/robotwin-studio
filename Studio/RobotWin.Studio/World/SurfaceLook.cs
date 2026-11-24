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
    /// twill, brushed lines, kraft fibres, 0.2 mm print layers, rust, plywood veneers) tinted with the part's colour,
    /// and the shine of the finish. Textures are projected per triangle on the plane its normal faces most, in
    /// millimetres, so boxes, cylinders, wings and imported models all take them without their own UVs. A part with no
    /// finish of its own shows its material's surface (wood grain on wood, print layers on PLA, brushed aluminium):
    /// the look only, its emissivity stays its material's.
    /// </summary>
    internal static class SurfaceLook
    {
        private static readonly Dictionary<string, BitmapSource> Tiles = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);

        /// <summary>Pixels along a texture tile's side.</summary>
        private const int TilePixels = 512;

        /// <summary>Real size of one texture tile (m) for a finish.</summary>
        private static double TileSize(string id) => id switch
        {
            "ahsap" => 0.06,
            "kontrplak-kenar" => 0.06,
            "karbon" => 0.012,
            "fircalanmis" => 0.02,
            "kraft" => 0.03,
            "baski" => 0.002,
            "baski-ust" => 0.0016,
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
            "ahsap" or "kontrplak-kenar" => (0.08, 15),
            "karbon" => (0.55, 60),
            "kraft" => (0.03, 8),
            "kaucuk" => (0.02, 6),
            "baski" or "baski-ust" => (0.18, 25),
            _ => (0.12, 40)
        };

        /// <summary>The surface a material shows when the part has no finish of its own ("" for a plain one).</summary>
        public static string ByMaterial(string? material) => RobotTwin.CoreSim.Mechanics.Materials.Canonical(material) switch
        {
            "wood" => "ahsap",
            "paper" => "kraft",
            "pla" or "petg" or "abs" or "nylon" => "baski",
            "carbon" => "karbon",
            "aluminium" or "steel" => "fircalanmis",
            "rubber" => "kaucuk",
            _ => string.Empty
        };

        internal static Color Shade(Color c, double k) =>
            Color.FromArgb(c.A, (byte)Math.Max(0, Math.Min(255, c.R * k)), (byte)Math.Max(0, Math.Min(255, c.G * k)), (byte)Math.Max(0, Math.Min(255, c.B * k)));

        internal static Color Mix(Color a, Color b, double t) =>
            Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ------------------------------------------------------------------ tileable noise

        internal static double Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 982451653);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215.0;
            }
        }

        /// <summary>Value noise on a lattice of <paramref name="px"/> × <paramref name="py"/> cells that wraps (tiles).</summary>
        internal static double Noise(double x, double y, int px, int py, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int Wx(int v) => ((v % px) + px) % px;
            int Wy(int v) => ((v % py) + py) % py;
            double a = Hash(Wx(x0), Wy(y0), seed), b = Hash(Wx(x0 + 1), Wy(y0), seed);
            double c = Hash(Wx(x0), Wy(y0 + 1), seed), d = Hash(Wx(x0 + 1), Wy(y0 + 1), seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        /// <summary>Fractal noise (0–1) over the tile, <paramref name="cx"/> × <paramref name="cy"/> cells at the coarsest octave.</summary>
        internal static double Fbm(double u, double v, int cx, int cy, int seed, int octaves = 4)
        {
            double sum = 0, amp = 0.5, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                int f = 1 << o;
                sum += amp * Noise(u * cx * f, v * cy * f, cx * f, cy * f, seed + o * 17);
                norm += amp;
                amp *= 0.5;
            }

            return sum / norm;
        }

        /// <summary>The finish's texture tile tinted with <paramref name="c"/> (null for a plain finish).</summary>
        public static BitmapSource? Tile(string id, Color c)
        {
            if (id is not ("ahsap" or "kontrplak-kenar" or "karbon" or "fircalanmis" or "kraft" or "baski" or "baski-ust" or "oksitli")) return null;
            string key = id + "|" + c;
            lock (Tiles)
            {
                if (Tiles.TryGetValue(key, out var cached)) return cached;
            }

            const int n = TilePixels;
            var px = new byte[n * n * 4];
            var rust = Color.FromRgb(0x8B, 0x4A, 0x22);
            System.Threading.Tasks.Parallel.For(0, n, y =>
            {
                double v = (y + 0.5) / n;
                for (int x = 0; x < n; x++)
                {
                    double u = (x + 0.5) / n;
                    Color col = c;
                    switch (id)
                    {
                        case "ahsap":
                        {
                            // Growth rings across v, gently bent by slow noise along the board (a sliced face: long,
                            // nearly parallel lines); each ring ends in a thin band of darker late wood. Fibres run along u.
                            double d = v * 6 + 0.55 * (Fbm(u, v, 1, 2, 11) - 0.5) + 0.12 * (Fbm(u, v, 2, 8, 13) - 0.5);
                            double ring = d - Math.Floor(d);
                            double late = Math.Pow(ring, 7);
                            double fibre = Noise(u * 3, v * 200, 3, 200, 7) - 0.5;
                            double mottle = Fbm(u, v, 2, 3, 21) - 0.5;
                            double k = 1.03 - 0.17 * late + 0.06 * fibre + 0.06 * mottle;
                            if (Hash(x / 3, y, 5) > 0.9985) k *= 0.7;   // pores, drawn out along the grain
                            col = Shade(c, k);
                            break;
                        }
                        case "kontrplak-kenar":
                        {
                            // Veneers of 1.4 mm stacked along v (a 6 cm tile holds 43), alternate tones, a thin glue line.
                            double t = v * (0.06 / 0.0014);
                            int ply = (int)Math.Floor(t);
                            double f = t - ply;
                            double tone = (ply % 2 == 0 ? 1.0 : 0.9) + 0.06 * (Hash(ply, 3, 9) - 0.5);
                            double grain = 0.05 * (Noise(u * 4, v * 300, 4, 300, 3) - 0.5);
                            col = f < 0.06 ? Shade(c, 0.62) : Shade(c, tone + grain);
                            break;
                        }
                        case "baski":
                        {
                            // Ten 0.2 mm layers across v: each a rounded bead, brighter on its crown, seams darker.
                            double t = v * 10 - Math.Floor(v * 10);
                            double bead = Math.Pow(Math.Sin(Math.PI * t), 0.6);
                            double wobble = 0.03 * (Noise(u * 8, v * 10, 8, 10, 4) - 0.5);
                            col = Shade(c, 0.80 + 0.24 * bead + wobble);
                            break;
                        }
                        case "baski-ust":
                        {
                            // The top skin: 0.4 mm extrusion lines at 45° (four across a 1.6 mm tile).
                            double t = (u + v) * 4;
                            t -= Math.Floor(t);
                            double bead = Math.Pow(Math.Sin(Math.PI * t), 0.6);
                            col = Shade(c, 0.86 + 0.18 * bead);
                            break;
                        }
                        case "fircalanmis":
                        {
                            // Fine scratches along u: each row its own brightness, drifting slowly along it.
                            double row = Noise(u * 2, v * 256, 2, 256, 8);
                            double fine = Noise(u * 6, v * 512, 6, 512, 9);
                            col = Shade(c, 0.86 + 0.16 * row + 0.08 * fine);
                            break;
                        }
                        case "karbon":
                        {
                            // 2×2 twill of 8 tows a side: fibres along each tow, resin in the gaps, sheen across the tow.
                            int tx = (int)(u * 8), ty = (int)(v * 8);
                            double fu = u * 8 - tx, fv = v * 8 - ty;
                            bool warp = ((tx + ty) / 2) % 2 == 0;
                            double across = warp ? fv : fu;
                            double edge = Math.Min(across, 1 - across);
                            double sheen = Math.Sin(Math.PI * across);
                            double fibres = warp ? Noise(u * 40, v * 400, 40, 400, 6) - 0.5 : Noise(u * 400, v * 40, 400, 40, 6) - 0.5;
                            double k = edge < 0.06 ? 0.45 : (warp ? 1.1 : 0.85) * (0.8 + 0.35 * sheen) + 0.08 * fibres;
                            col = Shade(c, k);
                            break;
                        }
                        case "kraft":
                        {
                            double fibres = Noise(u * 40, v * 160, 40, 160, 12) - 0.5;
                            double mottle = Fbm(u, v, 6, 6, 14) - 0.5;
                            col = Shade(c, 1.0 + 0.12 * fibres + 0.12 * mottle);
                            break;
                        }
                        case "oksitli":
                        {
                            double patch = Fbm(u, v, 4, 4, 15, 5);
                            double pit = Noise(u * 64, v * 64, 64, 64, 16);
                            double r = Math.Max(0, Math.Min(1, (patch - 0.42) * 4));
                            col = Shade(Mix(c, rust, r * 0.85), 0.9 + 0.2 * pit);
                            break;
                        }
                    }

                    int o = (y * n + x) * 4;
                    px[o] = col.B;
                    px[o + 1] = col.G;
                    px[o + 2] = col.R;
                    px[o + 3] = 255;
                }
            });

            var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
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
            string id = finish?.Id ?? string.Empty;
            if (id.Length == 0) id = ByMaterial(o.Material);   // the material's own surface (the look only)
            if (id.Length == 0) return;
            // The faces a part's own Z faces (top and bottom) and its sides can differ: plywood shows its grain on top
            // and its veneers on the edges, a print its layer lines on the sides and its 45° top skin above.
            bool plywood = id == "ahsap" && (o.Name + " " + o.PresetId + " " + o.Material).IndexOf("kontrplak", StringComparison.OrdinalIgnoreCase) >= 0
                           || id == "ahsap" && (o.Name + " " + o.PresetId).IndexOf("plywood", StringComparison.OrdinalIgnoreCase) >= 0;
            string side = plywood ? "kontrplak-kenar" : id;
            string top = id == "baski" ? "baski-ust" : id;
            Walk(model, c, Look(top, c, heat), Look(side, c, heat));
        }

        /// <summary>A finish's material and its tile size (0 without a texture).</summary>
        private static (Func<Material> Make, double Tile) Look(string id, Color c, EmissiveMaterial? heat)
        {
            var tile = Tile(id, c);
            var (spec, power) = Shine(id);
            Color shown = id switch
            {
                "eloksal" => Shade(c, 0.55),
                "parlatilmis" => Mix(c, Colors.White, 0.25),
                "kaucuk" => Mix(c, Color.FromRgb(0x22, 0x22, 0x24), 0.7),
                _ => c
            };

            Material made = null!;
            Material Make()
            {
                if (made != null) return made;
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
                made = g;
                return g;
            }

            return (Make, tile != null ? TileSize(id) : 0);
        }

        private static void Walk(Model3DGroup group, Color c, (Func<Material> Make, double Tile) top, (Func<Material> Make, double Tile) side)
        {
            for (int i = 0; i < group.Children.Count; i++)
            {
                switch (group.Children[i])
                {
                    case Model3DGroup sub when !sub.IsFrozen:
                        Walk(sub, c, top, side);
                        break;
                    case GeometryModel3D gm when !gm.IsFrozen && MainColour(gm.Material) is { } col && Near(col, c) && gm.Geometry is MeshGeometry3D mesh:
                        var (up, around) = Project(mesh, top.Tile, side.Tile);
                        if (up == null || around == null)
                        {
                            // one kind of face only
                            bool isTop = around == null;
                            gm.Geometry = isTop ? up! : around!;
                            gm.Material = isTop ? top.Make() : side.Make();
                            if (gm.BackMaterial != null) gm.BackMaterial = gm.Material;
                            break;
                        }

                        gm.Geometry = up;
                        gm.Material = top.Make();
                        var edges = new GeometryModel3D(around, side.Make()) { Transform = gm.Transform };
                        if (gm.BackMaterial != null)
                        {
                            gm.BackMaterial = gm.Material;
                            edges.BackMaterial = edges.Material;
                        }

                        group.Children.Insert(++i, edges);
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

        /// <summary>
        /// Unshared copies of a mesh with UVs on the plane each triangle faces most, one unit per tile (m): the
        /// triangles facing the part's own Z (top, bottom) and the others (sides), each null when there are none.
        /// </summary>
        private static (MeshGeometry3D? Up, MeshGeometry3D? Around) Project(MeshGeometry3D src, double topTile, double sideTile)
        {
            MeshGeometry3D? up = null, around = null;
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
                bool facesZ = az >= ax && az >= ay;
                var m = facesZ ? up ??= new MeshGeometry3D() : around ??= new MeshGeometry3D();
                double tile = facesZ ? topTile : sideTile;
                Point Uv(Point3D p) => tile <= 0 ? new Point(0, 0) : ax >= ay && ax >= az ? new Point(p.Y / tile, p.Z / tile) : ay >= az ? new Point(p.X / tile, p.Z / tile) : new Point(p.X / tile, p.Y / tile);
                foreach (int k in new[] { i0, i1, i2 })
                {
                    m.Positions.Add(pos[k]);
                    if (hasNormals) m.Normals.Add(src.Normals[k]);
                    m.TextureCoordinates.Add(Uv(pos[k]));
                    m.TriangleIndices.Add(m.Positions.Count - 1);
                }
            }

            up?.Freeze();
            around?.Freeze();
            return (up, around);
        }
    }
}
