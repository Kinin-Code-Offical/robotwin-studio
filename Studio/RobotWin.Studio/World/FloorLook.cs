using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [surface] The world's floor as its material looks: one metre of concrete, floorboards, asphalt, carpet, tiles,
    /// grass, sand or ice at real size (1 px ≈ 1 mm), with faint 10 cm lines left on it for placing things. A world
    /// without a floor material keeps the dark editing grid.
    /// </summary>
    internal static class FloorLook
    {
        private const int N = 1024;
        private static readonly Dictionary<string, BitmapSource> Tiles = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);

        /// <summary>One metre of the floor for <paramref name="material"/> (canonical or alias), or null for the editing grid.</summary>
        public static BitmapSource? Tile(string? material)
        {
            if (string.IsNullOrWhiteSpace(material)) return null;
            string m = RobotTwin.CoreSim.Mechanics.Materials.Canonical(material);
            if (m is not ("concrete" or "wood" or "mdf" or "asphalt" or "carpet" or "tile" or "grass" or "sand" or "regolith" or "ice" or "steel" or "aluminium" or "rubber" or "paper")) return null;
            lock (Tiles)
            {
                if (Tiles.TryGetValue(m, out var cached)) return cached;
            }

            var px = new byte[N * N * 4];
            System.Threading.Tasks.Parallel.For(0, N, y =>
            {
                double v = (y + 0.5) / N;
                for (int x = 0; x < N; x++)
                {
                    double u = (x + 0.5) / N;
                    var c = Shade(m, u, v, x, y);
                    // 10 cm lines for placing things: a slight darkening, a pixel wide.
                    if (x % 100 == 0 || y % 100 == 0 || x == N - 1 || y == N - 1) c = SurfaceLook.Shade(c, 0.82);
                    int o = (y * N + x) * 4;
                    px[o] = c.B;
                    px[o + 1] = c.G;
                    px[o + 2] = c.R;
                    px[o + 3] = 255;
                }
            });

            var bmp = BitmapSource.Create(N, N, 96, 96, PixelFormats.Bgra32, null, px, N * 4);
            bmp.Freeze();
            lock (Tiles) Tiles[m] = bmp;
            return bmp;
        }

        private static double Fbm(double u, double v, int c, int seed, int oct = 5) => SurfaceLook.Fbm(u, v, c, c, seed, oct);

        private static double Noise(double u, double v, int c, int seed) => SurfaceLook.Noise(u * c, v * c, c, c, seed);

        private static Color Rgb(int r, int g, int b) => Color.FromRgb((byte)r, (byte)g, (byte)b);

        private static Color Shade(string m, double u, double v, int x, int y)
        {
            switch (m)
            {
                case "concrete":
                {
                    // Trowelled slab: broad cloudy tone, fine sand grain, scattered aggregate.
                    double cloud = Fbm(u, v, 4, 31) - 0.5;
                    double grain = Noise(u, v, 512, 32) - 0.5;
                    double k = 1 + 0.10 * cloud + 0.06 * grain;
                    if (SurfaceLook.Hash(x / 2, y / 2, 33) > 0.996) k *= 0.8;
                    return SurfaceLook.Shade(Rgb(0x8E, 0x8C, 0x88), k);
                }
                case "asphalt":
                {
                    double grain = Noise(u, v, 400, 34) - 0.5;
                    double cloud = Fbm(u, v, 6, 35) - 0.5;
                    double k = 1 + 0.25 * grain + 0.10 * cloud;
                    if (SurfaceLook.Hash(x / 3, y / 3, 36) > 0.985) return SurfaceLook.Shade(Rgb(0x8A, 0x86, 0x80), 0.9 + 0.2 * grain);   // stones
                    return SurfaceLook.Shade(Rgb(0x3A, 0x3A, 0x3C), k);
                }
                case "wood":
                case "mdf":
                {
                    if (m == "mdf")
                        return SurfaceLook.Shade(Rgb(0xA8, 0x86, 0x5E), 1 + 0.05 * (Fbm(u, v, 8, 37) - 0.5) + 0.03 * (Noise(u, v, 300, 38) - 0.5));
                    // Floorboards 10 cm wide (ten across a metre), staggered ends, each its own tone, grain along u.
                    int board = (int)(v * 10);
                    double off = SurfaceLook.Hash(board, 1, 39);
                    double along = u + off;
                    int piece = (int)Math.Floor(along * 1.0 + 0.5 * (board % 2));
                    double fv = v * 10 - board;
                    double tone = 0.88 + 0.22 * SurfaceLook.Hash(board, piece, 40);
                    double rings = v * 60 + 0.8 * (Fbm(u, v, 2, 41) - 0.5) + board * 0.37;
                    double ring = rings - Math.Floor(rings);
                    double k = tone * (1.02 - 0.16 * Math.Pow(ring, 6)) + 0.05 * (Noise(u, v, 200, 42) - 0.5);
                    double endGap = (along * 1.0 + 0.5 * (board % 2)) - piece;
                    if (fv < 0.012 || endGap < 0.002) k *= 0.55;   // the seams between boards
                    return SurfaceLook.Shade(Rgb(0xB0, 0x82, 0x52), k);
                }
                case "carpet":
                {
                    double pile = Noise(u, v, 700, 43) - 0.5;
                    double cloud = Fbm(u, v, 5, 44) - 0.5;
                    return SurfaceLook.Shade(Rgb(0x52, 0x5A, 0x6A), 1 + 0.18 * pile + 0.08 * cloud);
                }
                case "tile":
                {
                    // 25 × 25 cm ceramic tiles with 3 mm grout, four to the metre.
                    double tu = u * 1000 % 250, tv = v * 1000 % 250;
                    if (tu < 3 || tv < 3) return Rgb(0x9A, 0x96, 0x8E);
                    int ti = (int)(u * 4), tj = (int)(v * 4);
                    double tone = 0.96 + 0.06 * SurfaceLook.Hash(ti, tj, 45);
                    double speck = Noise(u, v, 300, 46) - 0.5;
                    return SurfaceLook.Shade(Rgb(0xD8, 0xD4, 0xCC), tone + 0.04 * speck);
                }
                case "grass":
                {
                    double blades = Noise(u, v, 600, 47) - 0.5;
                    double patch = Fbm(u, v, 5, 48) - 0.5;
                    var g = SurfaceLook.Mix(Rgb(0x3E, 0x6E, 0x2C), Rgb(0x6E, 0x8A, 0x3A), Math.Max(0, Math.Min(1, 0.5 + patch * 1.6)));
                    return SurfaceLook.Shade(g, 1 + 0.28 * blades);
                }
                case "sand":
                case "regolith":
                {
                    double grain = Noise(u, v, 700, 49) - 0.5;
                    double ripple = Math.Sin((v * 40 + 2 * (Fbm(u, v, 3, 50) - 0.5)) * Math.PI * 2);
                    var b = m == "sand" ? Rgb(0xC8, 0xB0, 0x82) : Rgb(0x8C, 0x8A, 0x86);
                    return SurfaceLook.Shade(b, 1 + 0.12 * grain + 0.04 * ripple);
                }
                case "ice":
                {
                    double cloud = Fbm(u, v, 4, 51) - 0.5;
                    double crack = Math.Abs(Fbm(u, v, 6, 52) - 0.5) < 0.006 ? 0.85 : 1;
                    return SurfaceLook.Shade(Rgb(0xC8, 0xDC, 0xE6), (1 + 0.08 * cloud) * crack);
                }
                case "steel":
                case "aluminium":
                {
                    // A chequer plate: raised teardrops in a diagonal pattern every 3 cm.
                    double row = SurfaceLook.Noise(u, v * 512, 1, 512, 53);
                    double k = 0.9 + 0.12 * row;
                    double cu = (u * 100 / 3) % 1, cv = (v * 100 / 3) % 1;
                    bool alt = ((int)(u * 100 / 3) + (int)(v * 100 / 3)) % 2 == 0;
                    double du = cu - 0.5, dv = cv - 0.5;
                    double r = alt ? (du + dv) * (du + dv) * 6 + (du - dv) * (du - dv) * 60 : (du - dv) * (du - dv) * 6 + (du + dv) * (du + dv) * 60;
                    if (r < 1) k *= 1.18;
                    return SurfaceLook.Shade(m == "steel" ? Rgb(0x8C, 0x90, 0x96) : Rgb(0xB4, 0xB8, 0xBE), k);
                }
                case "rubber":
                    return SurfaceLook.Shade(Rgb(0x2A, 0x2A, 0x2C), 1 + 0.10 * (Noise(u, v, 500, 54) - 0.5));
                default:
                    return SurfaceLook.Shade(Rgb(0xC8, 0xA8, 0x78), 1 + 0.10 * (Noise(u, v, 300, 55) - 0.5));
            }
        }
    }
}
