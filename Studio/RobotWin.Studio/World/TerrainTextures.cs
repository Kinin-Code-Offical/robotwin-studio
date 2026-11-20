using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.World
{
    // [terrain] Procedural, tileable surface textures of the terrain layers (no image files needed).

    /// <summary>
    /// One 256×256 seamless texture per <see cref="TerrainLayer"/>, generated once from periodic
    /// noise and cellular patterns: asphalt aggregate, concrete pores, tiles with grout, grass
    /// blades, packed dirt, gravel stones, rippled sand, wet mud, wet asphalt sheen, cracked ice,
    /// carpet weave and a studded rubber mat. Pixels are 0xAARRGGBB.
    /// </summary>
    public static class TerrainTextures
    {
        public const int Size = 256;
        private static readonly Dictionary<int, uint[]> Cache = new Dictionary<int, uint[]>();
        private static readonly object Gate = new object();

        /// <summary>World size (m) one texture covers.</summary>
        public static double TileMeters(TerrainLayer l) => l.Id switch
        {
            "tile" => 0.6,
            "grass" => 0.5,
            "gravel" => 0.35,
            "sand" => 0.8,
            "carpet" => 0.25,
            "rubbermat" => 0.3,
            "ice" => 1.2,
            _ => 1.0
        };

        public static uint[] Get(int layer)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(layer, out var t)) return t;
                t = Build(TerrainLayers.Get(layer));
                Cache[layer] = t;
                return t;
            }
        }

        // ================================================================== periodic noise

        private static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        private static double H01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / (double)0xFFFFFF;

        private static int Wrap(int v, int p) => ((v % p) + p) % p;

        /// <summary>Value noise 0…1 with period <paramref name="period"/> lattice cells over the texture.</summary>
        private static double PNoise(double u, double v, int period, int seed)
        {
            double x = u * period, y = v * period;
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period), y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);
            double a = H01(x0, y0, seed), b = H01(x1, y0, seed), c = H01(x0, y1, seed), d = H01(x1, y1, seed);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }

        private static double Fbm(double u, double v, int period, int seed, int octaves = 4)
        {
            double s = 0, amp = 0.5, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                s += amp * PNoise(u, v, period << o, seed + o * 31);
                norm += amp;
                amp *= 0.5;
            }

            return s / norm;
        }

        /// <summary>Periodic Worley noise: distance to the nearest and second nearest jittered point (in cells), and the nearest cell's id.</summary>
        private static (double D1, double D2, uint Id) Worley(double u, double v, int period, int seed)
        {
            double x = u * period, y = v * period;
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double d1 = 9, d2 = 9;
            uint id = 0;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int cx = xi + i, cy = yi + j;
                int wx = Wrap(cx, period), wy = Wrap(cy, period);
                double px = cx + 0.15 + 0.7 * H01(wx, wy, seed), py = cy + 0.15 + 0.7 * H01(wx, wy, seed + 7);
                double d = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < d1)
                {
                    d2 = d1;
                    d1 = d;
                    id = Hash(wx, wy, seed + 13);
                }
                else if (d < d2) d2 = d;
            }

            return (d1, d2, id);
        }

        private static uint Rgb(double r, double g, double b)
        {
            static byte C(double v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
            return 0xFF000000u | (uint)C(r) << 16 | (uint)C(g) << 8 | C(b);
        }

        private static (double R, double G, double B) Col(uint argb) => ((argb >> 16) & 0xFF, (argb >> 8) & 0xFF, argb & 0xFF);

        private static (double, double, double) Mix((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
            (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

        private static (double, double, double) Scale((double R, double G, double B) a, double k) => (a.R * k, a.G * k, a.B * k);

        // ================================================================== layers

        private static uint[] Build(TerrainLayer layer)
        {
            var px = new uint[Size * Size];
            for (int j = 0; j < Size; j++)
            for (int i = 0; i < Size; i++)
            {
                double u = (i + 0.5) / Size, v = (j + 0.5) / Size;
                var (r, g, b) = Pixel(layer.Id, u, v, i, j, layer.ColorArgb);
                px[j * Size + i] = Rgb(r, g, b);
            }

            return px;
        }

        private static (double, double, double) Pixel(string id, double u, double v, int i, int j, uint fallback)
        {
            switch (id)
            {
                case "asphalt":
                case "wet":
                {
                    bool wet = id == "wet";
                    var basec = wet ? (40.0, 43.0, 49.0) : (58.0, 59.0, 62.0);
                    double grain = PNoise(u, v, 128, 3) * 0.6 + PNoise(u, v, 64, 4) * 0.4;
                    double stain = Fbm(u, v, 4, 5, 3);
                    var c = Scale(basec, 0.82 + 0.3 * grain + 0.18 * (stain - 0.5));
                    // Light aggregate chips.
                    double chip = H01(i, j, 11);
                    if (chip > 0.965) c = Mix(c, (150.0, 148.0, 142.0), 0.55 + 0.4 * (chip - 0.965) / 0.035);
                    else if (chip < 0.02) c = Scale(c, 0.6);
                    if (wet)
                    {
                        double sheen = Fbm(u, v, 3, 9, 3);
                        if (sheen > 0.55) c = Mix(c, (92.0, 104.0, 122.0), Math.Min(1, (sheen - 0.55) * 2.2));
                    }

                    return c;
                }

                case "concrete":
                {
                    double n = Fbm(u, v, 8, 21, 4);
                    double fine = PNoise(u, v, 128, 22);
                    var c = Scale((156.0, 154.0, 148.0), 0.86 + 0.22 * n + 0.08 * (fine - 0.5));
                    if (H01(i, j, 23) > 0.985) c = Scale(c, 0.7);
                    return c;
                }

                case "tile":
                {
                    // 2 × 2 tiles of 30 cm with 3 mm grout.
                    double tu = u * 2, tv = v * 2;
                    double fu = tu - Math.Floor(tu), fv = tv - Math.Floor(tv);
                    double grout = 0.006;
                    int tx = (int)Math.Floor(tu), ty = (int)Math.Floor(tv);
                    double shade = 0.95 + 0.08 * H01(tx, ty, 31);
                    if (fu < grout || fu > 1 - grout || fv < grout || fv > 1 - grout) return (168.0, 164.0, 156.0);
                    double n = PNoise(u, v, 32, 33) * 0.5 + PNoise(u, v, 128, 34) * 0.5;
                    return Scale((218.0, 214.0, 206.0), shade * (0.95 + 0.08 * n));
                }

                case "grass":
                {
                    double clump = Fbm(u, v, 8, 41, 4);
                    var dark = (52.0, 92.0, 34.0);
                    var light = (98.0, 140.0, 56.0);
                    var dry = (128.0, 132.0, 66.0);
                    var c = Mix(dark, light, clump);
                    double dryness = PNoise(u, v, 4, 42);
                    if (dryness > 0.62) c = Mix(c, dry, (dryness - 0.62) * 1.6);
                    // Blades: short streaks.
                    double blade = PNoise(u * 1.0, v * 0.25, 256, 43);
                    c = Scale(c, 0.78 + 0.42 * blade);
                    if (H01(i, j, 44) > 0.97) c = Mix(c, (150.0, 176.0, 90.0), 0.6);
                    return c;
                }

                case "dirt":
                {
                    double n = Fbm(u, v, 8, 51, 5);
                    var c = Mix((96.0, 70.0, 46.0), (138.0, 106.0, 74.0), n);
                    double fine = PNoise(u, v, 128, 52);
                    c = Scale(c, 0.9 + 0.2 * fine);
                    var (d1, _, id2) = Worley(u, v, 24, 53);
                    if (d1 < 0.12 && (id2 & 3) == 0) c = Mix(c, (150.0, 140.0, 124.0), 0.7);
                    return c;
                }

                case "gravel":
                {
                    var (d1, d2, cid) = Worley(u, v, 22, 61);
                    double edge = d2 - d1;
                    double tone = (cid & 0xFF) / 255.0;
                    var stone = Mix((112.0, 108.0, 102.0), (176.0, 166.0, 150.0), tone);
                    if ((cid >> 8 & 7) == 0) stone = Mix(stone, (150.0, 128.0, 104.0), 0.6);
                    // Rounded stones: lighter toward their middle, dark gaps between them.
                    double dome = Math.Max(0, 1 - d1 * 1.25);
                    var c = Scale(stone, 0.7 + 0.45 * dome);
                    if (edge < 0.08) c = Mix(c, (52.0, 48.0, 44.0), 1 - edge / 0.08);
                    c = Scale(c, 0.93 + 0.14 * PNoise(u, v, 128, 62));
                    return c;
                }

                case "sand":
                {
                    double warp = Fbm(u, v, 4, 71, 3);
                    double ripple = 0.5 + 0.5 * Math.Sin(2 * Math.PI * (v * 9 + warp * 1.6));
                    double grain = H01(i, j, 72);
                    var c = Mix((198.0, 176.0, 128.0), (222.0, 204.0, 158.0), ripple * 0.7 + 0.3 * Fbm(u, v, 16, 73, 3));
                    c = Scale(c, 0.92 + 0.12 * grain);
                    return c;
                }

                case "mud":
                {
                    double n = Fbm(u, v, 6, 81, 5);
                    var c = Mix((58.0, 42.0, 30.0), (92.0, 70.0, 50.0), n);
                    double wet = Fbm(u, v, 3, 82, 3);
                    if (wet > 0.58) c = Mix(c, (40.0, 32.0, 26.0), Math.Min(1, (wet - 0.58) * 3));
                    double gloss = PNoise(u, v, 48, 83);
                    if (wet > 0.6 && gloss > 0.8) c = Mix(c, (120.0, 110.0, 100.0), (gloss - 0.8) * 2);
                    // Tyre-track like streaks.
                    double track = PNoise(u * 0.2, v, 64, 84);
                    c = Scale(c, 0.9 + 0.16 * track);
                    return c;
                }

                case "ice":
                {
                    double n = Fbm(u, v, 6, 91, 4);
                    var c = Mix((196.0, 220.0, 232.0), (232.0, 242.0, 248.0), n);
                    var (d1, d2, _) = Worley(u, v, 6, 92);
                    if (d2 - d1 < 0.03) c = Mix(c, (250.0, 252.0, 255.0), 1 - (d2 - d1) / 0.03);
                    var (e1, e2, _) = Worley(u, v, 14, 93);
                    if (e2 - e1 < 0.02) c = Mix(c, (160.0, 190.0, 206.0), 0.5);
                    return c;
                }

                case "carpet":
                {
                    // Loop-pile weave: a checker of small loops plus fibre noise.
                    int cu = (int)(u * 128), cv = (int)(v * 128);
                    double loop = ((cu + cv) & 1) == 0 ? 1.0 : 0.88;
                    double fibre = H01(i, j, 101);
                    double n = Fbm(u, v, 8, 102, 3);
                    var c = Mix((78.0, 88.0, 110.0), (98.0, 108.0, 128.0), n);
                    return Scale(c, loop * (0.9 + 0.18 * fibre));
                }

                case "rubbermat":
                {
                    // Diamond studs on a 25 mm pitch.
                    double su = u * 12, sv = v * 12;
                    double fu = su - Math.Floor(su) - 0.5, fv = sv - Math.Floor(sv) - 0.5;
                    double diamond = Math.Abs(fu) + Math.Abs(fv);
                    var c = (36.0, 36.0, 40.0);
                    if (diamond < 0.28) c = (58.0, 58.0, 62.0);
                    else if (diamond < 0.32) c = (24.0, 24.0, 26.0);
                    double grain = PNoise(u, v, 128, 111);
                    return Scale(c, 0.92 + 0.16 * grain);
                }

                default:
                {
                    var c = Col(fallback);
                    return Scale(c, 0.9 + 0.2 * Fbm(u, v, 8, 1, 3));
                }
            }
        }

        // ================================================================== blending helpers

        /// <summary>Periodic low-frequency noise (0…1) used to break up layer borders and tiling, from a 64×64 table.</summary>
        private static readonly float[] Breakup = BuildBreakup();

        private static float[] BuildBreakup()
        {
            const int n = 64;
            var t = new float[n * n * 4];
            for (int k = 0; k < 4; k++)
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                t[k * n * n + j * n + i] = (float)Fbm((i + 0.5) / n, (j + 0.5) / n, 4, 200 + k * 17, 3);
            return t;
        }

        /// <summary>Breakup noise at world (x, y) for a channel (0…3), period 4 m, bilinear.</summary>
        public static double BreakupAt(double x, double y, int channel)
        {
            const int n = 64;
            double fx = x / 4.0 * n, fy = y / 4.0 * n;
            int xi = (int)Math.Floor(fx), yi = (int)Math.Floor(fy);
            double tx = fx - xi, ty = fy - yi;
            int x0 = Wrap(xi, n), x1 = Wrap(xi + 1, n), y0 = Wrap(yi, n), y1 = Wrap(yi + 1, n);
            int b = (channel & 3) * n * n;
            double a = Breakup[b + y0 * n + x0], c = Breakup[b + y0 * n + x1], d = Breakup[b + y1 * n + x0], e = Breakup[b + y1 * n + x1];
            return a + (c - a) * tx + (d - a) * ty + (a - c - d + e) * tx * ty;
        }
    }
}
