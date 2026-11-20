using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] Sculpt and paint tools of the "Arazi" editor (brush math, stamps, ramps, roads).

    public enum TerrainTool
    {
        Raise,
        Lower,
        Smooth,
        Flatten,
        SetHeight,
        Noise,
        Erode,
        Ramp,
        Stamp,
        Paint,
        Road
    }

    public enum TerrainStamp
    {
        /// <summary>Round hump (tümsek).</summary>
        Bump,

        /// <summary>Round hole with a flat bottom (çukur).</summary>
        Pothole,

        /// <summary>Speed bump across the stamp direction (hız kesici).</summary>
        SpeedBump,

        /// <summary>Raised kerb strip (bordür).</summary>
        Kerb,

        /// <summary>One step up, a platform behind it (basamak).</summary>
        Step
    }

    /// <summary>Brush settings of the terrain editor.</summary>
    public sealed class TerrainBrush
    {
        /// <summary>Radius (m).</summary>
        public double Radius { get; set; } = 0.3;

        /// <summary>0…1.</summary>
        public double Strength { get; set; } = 0.5;

        /// <summary>Fraction of the radius at full strength (0 = soft cone, 1 = hard disc).</summary>
        public double Hardness { get; set; } = 0.35;

        /// <summary>Target of "Yüksekliğe ayarla" (m).</summary>
        public double TargetHeight { get; set; } = 0.05;

        /// <summary>Feature size of the roughness noise (m).</summary>
        public double NoiseScale { get; set; } = 0.25;

        /// <summary>Layer index for painting and roads.</summary>
        public int Layer { get; set; }

        public TerrainStamp Stamp { get; set; } = TerrainStamp.Bump;

        /// <summary>Stamp height (m, positive; potholes go down by it).</summary>
        public double StampHeight { get; set; } = 0.03;

        /// <summary>Stamp size along its direction (m).</summary>
        public double StampLength { get; set; } = 0.3;

        /// <summary>Stamp size across its direction (m).</summary>
        public double StampWidth { get; set; } = 1.0;

        /// <summary>Stamp/ramp direction (degrees from +x).</summary>
        public double Angle { get; set; }

        public double RoadWidth { get; set; } = 0.6;
        public bool RoadFlatten { get; set; } = true;

        /// <summary>Falloff weight (0…1) at a distance from the brush centre.</summary>
        public double Falloff(double d) => TerrainSculpt.Falloff(d, Radius, Hardness);

        public TerrainBrush Clone() => (TerrainBrush)MemberwiseClone();
    }

    /// <summary>The operations behind the terrain tools. Each one records undo and raises <see cref="TerrainMap.Changed"/>.</summary>
    public static class TerrainSculpt
    {
        /// <summary>1 inside hardness·R, smoothstep to 0 at R.</summary>
        public static double Falloff(double d, double radius, double hardness)
        {
            if (radius <= 0 || d >= radius) return 0;
            double inner = Math.Max(0, Math.Min(1, hardness)) * radius;
            if (d <= inner) return 1;
            double t = (radius - d) / Math.Max(1e-12, radius - inner);
            return t * t * (3 - 2 * t);
        }

        /// <summary>Height change rate (m/s) of raise/lower at full weight.</summary>
        public static double RaiseRate(TerrainBrush b) => b.Strength * Math.Max(0.02, b.Radius) * 0.8;

        private static (int C0, int R0, int C1, int R1) Range(TerrainMap map, double x, double y, double radius) =>
            map.VertexRange(x - radius, y - radius, x + radius, y + radius);

        private static TerrainChange Done(TerrainMap map, int c0, int r0, int c1, int r1, bool heights, bool paint)
        {
            // Neighbouring normals change with the heights.
            int g = heights ? 1 : 0;
            map.NotifyChanged(c0 - g, r0 - g, c1 + g, r1 + g, heights, paint);
            return new TerrainChange(Math.Max(0, c0 - g), Math.Max(0, r0 - g), Math.Min(map.Columns - 1, c1 + g), Math.Min(map.Rows - 1, r1 + g), heights, paint);
        }

        /// <summary>Adds <paramref name="amount"/> metres (negative lowers) times the falloff.</summary>
        public static TerrainChange Raise(TerrainMap map, TerrainBrush b, double x, double y, double amount)
        {
            var (c0, r0, c1, r1) = Range(map, x, y, b.Radius);
            if (c1 < c0 || r1 < r0 || amount == 0) return default;
            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            for (int r = r0; r <= r1; r++)
            {
                double dy = map.VertexY(r) - y;
                for (int c = c0; c <= c1; c++)
                {
                    double dx = map.VertexX(c) - x;
                    double w = b.Falloff(Math.Sqrt(dx * dx + dy * dy));
                    if (w <= 0) continue;
                    h[r * map.Columns + c] += (float)(amount * w);
                }
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>Pulls heights towards the 3×3 average; <paramref name="rate"/> 0…1 per call.</summary>
        public static TerrainChange Smooth(TerrainMap map, TerrainBrush b, double x, double y, double rate)
        {
            var (c0, r0, c1, r1) = Range(map, x, y, b.Radius);
            if (c1 < c0 || r1 < r0) return default;
            map.BeginModify(c0, r0, c1, r1);
            int ec0 = Math.Max(0, c0 - 1), er0 = Math.Max(0, r0 - 1), ec1 = Math.Min(map.Columns - 1, c1 + 1), er1 = Math.Min(map.Rows - 1, r1 + 1);
            var src = map.CopyHeights(ec0, er0, ec1, er1);
            int sw = ec1 - ec0 + 1;
            var h = map.Heights;
            rate = Math.Max(0, Math.Min(1, rate));
            for (int r = r0; r <= r1; r++)
            {
                double dy = map.VertexY(r) - y;
                for (int c = c0; c <= c1; c++)
                {
                    double dx = map.VertexX(c) - x;
                    double w = b.Falloff(Math.Sqrt(dx * dx + dy * dy)) * rate;
                    if (w <= 0) continue;
                    double sum = 0, wsum = 0;
                    for (int j = -1; j <= 1; j++)
                    {
                        int rr = r + j;
                        if (rr < er0 || rr > er1) continue;
                        for (int i = -1; i <= 1; i++)
                        {
                            int cc = c + i;
                            if (cc < ec0 || cc > ec1) continue;
                            double k = (i == 0 ? 2 : 1) * (j == 0 ? 2 : 1);
                            sum += src[(rr - er0) * sw + (cc - ec0)] * k;
                            wsum += k;
                        }
                    }

                    int idx = r * map.Columns + c;
                    double cur = h[idx];
                    h[idx] = (float)(cur + (sum / wsum - cur) * w);
                }
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>Pulls heights towards <paramref name="target"/>; <paramref name="rate"/> 0…1 per call (1 = exactly flat inside the hard core).</summary>
        public static TerrainChange Flatten(TerrainMap map, TerrainBrush b, double x, double y, double target, double rate)
        {
            var (c0, r0, c1, r1) = Range(map, x, y, b.Radius);
            if (c1 < c0 || r1 < r0) return default;
            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            rate = Math.Max(0, Math.Min(1, rate));
            for (int r = r0; r <= r1; r++)
            {
                double dy = map.VertexY(r) - y;
                for (int c = c0; c <= c1; c++)
                {
                    double dx = map.VertexX(c) - x;
                    double w = b.Falloff(Math.Sqrt(dx * dx + dy * dy)) * rate;
                    if (w <= 0) continue;
                    int idx = r * map.Columns + c;
                    h[idx] = (float)(h[idx] + (target - h[idx]) * w);
                }
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>Adds fractal noise (feature size <see cref="TerrainBrush.NoiseScale"/>) of amplitude <paramref name="amount"/> m.</summary>
        public static TerrainChange Noise(TerrainMap map, TerrainBrush b, double x, double y, double amount)
        {
            var (c0, r0, c1, r1) = Range(map, x, y, b.Radius);
            if (c1 < c0 || r1 < r0) return default;
            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            double f = 1 / Math.Max(0.01, b.NoiseScale);
            for (int r = r0; r <= r1; r++)
            {
                double vy = map.VertexY(r), dy = vy - y;
                for (int c = c0; c <= c1; c++)
                {
                    double vx = map.VertexX(c), dx = vx - x;
                    double w = b.Falloff(Math.Sqrt(dx * dx + dy * dy));
                    if (w <= 0) continue;
                    h[r * map.Columns + c] += (float)(amount * w * Fbm(vx * f, vy * f, 4));
                }
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>
        /// Thermal erosion: wherever the slope to a neighbour exceeds the talus angle, material
        /// slides down; <paramref name="iterations"/> passes with transfer <paramref name="rate"/> 0…1.
        /// </summary>
        public static TerrainChange Erode(TerrainMap map, TerrainBrush b, double x, double y, int iterations, double rate, double talusDeg = 30)
        {
            var (c0, r0, c1, r1) = Range(map, x, y, b.Radius);
            if (c1 < c0 || r1 < r0) return default;
            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            int cols = map.Columns;
            double talus = Math.Tan(talusDeg * Math.PI / 180) * map.CellSize;
            int w0 = c1 - c0 + 1, h0 = r1 - r0 + 1;
            var weight = new double[w0 * h0];
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                double dx = map.VertexX(c) - x, dy = map.VertexY(r) - y;
                weight[(r - r0) * w0 + (c - c0)] = b.Falloff(Math.Sqrt(dx * dx + dy * dy));
            }

            var delta = new double[w0 * h0];
            rate = Math.Max(0, Math.Min(1, rate)) * 0.5;
            for (int it = 0; it < iterations; it++)
            {
                Array.Clear(delta, 0, delta.Length);
                for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    int li = (r - r0) * w0 + (c - c0);
                    double w = weight[li];
                    if (w <= 0) continue;
                    double hc = h[r * cols + c];
                    // Steepest lower neighbour inside the brush.
                    int best = -1;
                    double drop = talus;
                    for (int k = 0; k < 4; k++)
                    {
                        int cc = c + (k == 0 ? 1 : k == 1 ? -1 : 0), rr = r + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (cc < c0 || cc > c1 || rr < r0 || rr > r1) continue;
                        double d = hc - h[rr * cols + cc];
                        if (d > drop)
                        {
                            drop = d;
                            best = (rr - r0) * w0 + (cc - c0);
                        }
                    }

                    if (best < 0) continue;
                    double move = (drop - talus) * 0.5 * rate * w;
                    delta[li] -= move;
                    delta[best] += move;
                }

                for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    h[r * cols + c] += (float)delta[(r - r0) * w0 + (c - c0)];
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>Blends <paramref name="amount"/> (0…1 at full weight) of the brush layer into the surface.</summary>
        public static TerrainChange Paint(TerrainMap map, TerrainBrush b, double x, double y, double amount)
        {
            var (c0, r0, c1, r1) = Range(map, x, y, b.Radius);
            if (c1 < c0 || r1 < r0 || amount <= 0) return default;
            map.BeginModify(c0, r0, c1, r1);
            int layer = Math.Max(0, Math.Min(TerrainLayers.Count - 1, b.Layer));
            for (int r = r0; r <= r1; r++)
            {
                double dy = map.VertexY(r) - y;
                for (int c = c0; c <= c1; c++)
                {
                    double dx = map.VertexX(c) - x;
                    double w = b.Falloff(Math.Sqrt(dx * dx + dy * dy));
                    if (w <= 0) continue;
                    map.PaintVertex(r * map.Columns + c, layer, Math.Min(1, amount * w));
                }
            }

            return Done(map, c0, r0, c1, r1, false, true);
        }

        /// <summary>
        /// Straight ramp from (x0, y0, h0) to (x1, y1, h1), <paramref name="width"/> wide; the sides
        /// blend into the ground over the soft part of the width (hardness).
        /// </summary>
        public static TerrainChange Ramp(TerrainMap map, double x0, double y0, double h0, double x1, double y1, double h1, double width, double hardness)
        {
            double lx = x1 - x0, ly = y1 - y0, len = Math.Sqrt(lx * lx + ly * ly);
            if (len < 1e-6) return default;
            double ux = lx / len, uy = ly / len, half = width / 2;
            var (c0, r0, c1, r1) = map.VertexRange(Math.Min(x0, x1) - half, Math.Min(y0, y1) - half, Math.Max(x0, x1) + half, Math.Max(y0, y1) + half);
            if (c1 < c0 || r1 < r0) return default;
            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            for (int r = r0; r <= r1; r++)
            {
                double py = map.VertexY(r) - y0;
                for (int c = c0; c <= c1; c++)
                {
                    double px = map.VertexX(c) - x0;
                    double t = px * ux + py * uy;
                    if (t < 0 || t > len) continue;
                    double s = Math.Abs(-px * uy + py * ux);
                    double w = Falloff(s, half, hardness);
                    if (w <= 0) continue;
                    double target = h0 + (h1 - h0) * (t / len);
                    int idx = r * map.Columns + c;
                    h[idx] = (float)(h[idx] + (target - h[idx]) * w);
                }
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>Height added by a stamp at local coordinates (u along its direction, v across).</summary>
        public static double StampProfile(TerrainStamp kind, double u, double v, double height, double length, double width, double cell)
        {
            double hl = length / 2, hw = width / 2;
            double bevel = Math.Max(0.004, cell * 0.5);
            switch (kind)
            {
                case TerrainStamp.Bump:
                {
                    double d = Math.Sqrt(u * u + v * v);
                    return d >= hl ? 0 : height * 0.5 * (1 + Math.Cos(Math.PI * d / hl));
                }

                case TerrainStamp.Pothole:
                {
                    double d = Math.Sqrt(u * u + v * v) / hl;
                    if (d >= 1) return 0;
                    double p = d < 0.55 ? 1 : 0.5 * (1 + Math.Cos(Math.PI * (d - 0.55) / 0.45));
                    return -height * p;
                }

                case TerrainStamp.SpeedBump:
                {
                    if (Math.Abs(u) >= hl || Math.Abs(v) >= hw) return 0;
                    double c = Math.Cos(Math.PI * u / length);
                    return height * c * c * EndTaper(v, hw, Math.Min(hw * 0.25, 0.1));
                }

                case TerrainStamp.Kerb:
                {
                    if (Math.Abs(v) >= hw) return 0;
                    double p = Clamp01((hl - Math.Abs(u)) / bevel + 0.5);
                    return height * p * EndTaper(v, hw, bevel);
                }

                case TerrainStamp.Step:
                {
                    if (Math.Abs(v) >= hw || u <= -bevel || u >= hl) return 0;
                    double rise = Clamp01(u / bevel + 0.5);
                    // The platform runs out over the last 30 % of the stamp length.
                    double fall = u < hl * 0.7 ? 1 : 0.5 * (1 + Math.Cos(Math.PI * (u - hl * 0.7) / (hl * 0.3)));
                    return height * rise * fall * EndTaper(v, hw, bevel);
                }

                default:
                    return 0;
            }
        }

        private static double EndTaper(double v, double hw, double taper)
        {
            double edge = hw - Math.Abs(v);
            if (taper <= 0 || edge >= taper) return 1;
            double t = Math.Max(0, edge / taper);
            return t * t * (3 - 2 * t);
        }

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Adds a stamp shape centred at (x, y), turned by <paramref name="angleDeg"/>.</summary>
        public static TerrainChange Stamp(TerrainMap map, TerrainStamp kind, double x, double y, double angleDeg, double height, double length, double width)
        {
            double a = angleDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            double ext = Math.Sqrt(length * length + width * width) / 2 + map.CellSize;
            if (kind == TerrainStamp.Bump || kind == TerrainStamp.Pothole) ext = length / 2 + map.CellSize;
            var (c0, r0, c1, r1) = map.VertexRange(x - ext, y - ext, x + ext, y + ext);
            if (c1 < c0 || r1 < r0) return default;
            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            for (int r = r0; r <= r1; r++)
            {
                double py = map.VertexY(r) - y;
                for (int c = c0; c <= c1; c++)
                {
                    double px = map.VertexX(c) - x;
                    double u = px * ca + py * sa, v = -px * sa + py * ca;
                    double dz = StampProfile(kind, u, v, height, length, width, map.CellSize);
                    if (dz != 0) h[r * map.Columns + c] += (float)dz;
                }
            }

            return Done(map, c0, r0, c1, r1, true, false);
        }

        /// <summary>Catmull–Rom curve through the points, sampled about every <paramref name="spacing"/> metres.</summary>
        public static List<(double X, double Y)> Spline(IReadOnlyList<(double X, double Y)> pts, double spacing)
        {
            var result = new List<(double, double)>();
            if (pts.Count == 0) return result;
            if (pts.Count == 1)
            {
                result.Add(pts[0]);
                return result;
            }

            spacing = Math.Max(1e-3, spacing);
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var p0 = pts[Math.Max(0, i - 1)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
                double seg = Math.Sqrt((p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y));
                int n = Math.Max(1, (int)Math.Ceiling(seg / spacing));
                for (int k = 0; k < n; k++)
                {
                    double t = k / (double)n, t2 = t * t, t3 = t2 * t;
                    double x = 0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);
                    double y = 0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);
                    result.Add((x, y));
                }
            }

            result.Add(pts[pts.Count - 1]);
            return result;
        }

        /// <summary>
        /// Lays a road strip along a spline through <paramref name="points"/>: paints the layer
        /// (full inside the width, blended over the shoulder) and, when <paramref name="flatten"/>,
        /// levels it across while following the ground along its length (smoothed).
        /// </summary>
        public static TerrainChange Road(TerrainMap map, IReadOnlyList<(double X, double Y)> points, double width, int layer, bool flatten, double shoulder = -1)
        {
            if (points.Count < 2) return default;
            var line = Spline(points, Math.Max(0.01, map.CellSize * 0.5));
            if (shoulder < 0) shoulder = Math.Max(map.CellSize * 1.5, width * 0.25);
            double half = width / 2, reach = half + shoulder;
            // Centre-line heights smoothed along the road (about one road width).
            var raw = new double[line.Count];
            for (int i = 0; i < line.Count; i++) raw[i] = map.Height(line[i].X, line[i].Y);
            var centre = new double[line.Count];
            double spacing = Math.Max(0.01, map.CellSize * 0.5);
            int win = Math.Max(1, (int)Math.Round(width / spacing));
            for (int i = 0; i < line.Count; i++)
            {
                double s = 0;
                int n = 0;
                for (int k = Math.Max(0, i - win); k <= Math.Min(line.Count - 1, i + win); k++)
                {
                    s += raw[k];
                    n++;
                }

                centre[i] = s / n;
            }

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var p in line)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            var (c0, r0, c1, r1) = map.VertexRange(minX - reach, minY - reach, maxX + reach, maxY + reach);
            if (c1 < c0 || r1 < r0) return default;
            int w0 = c1 - c0 + 1, h0 = r1 - r0 + 1;
            var best = new double[w0 * h0];
            var bestH = new double[w0 * h0];
            for (int i = 0; i < best.Length; i++) best[i] = double.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i];
                var b = line[i + 1];
                double sx = b.X - a.X, sy = b.Y - a.Y, l2 = sx * sx + sy * sy;
                var (sc0, sr0, sc1, sr1) = map.VertexRange(Math.Min(a.X, b.X) - reach, Math.Min(a.Y, b.Y) - reach, Math.Max(a.X, b.X) + reach, Math.Max(a.Y, b.Y) + reach);
                for (int r = Math.Max(r0, sr0); r <= Math.Min(r1, sr1); r++)
                {
                    double py = map.VertexY(r);
                    for (int c = Math.Max(c0, sc0); c <= Math.Min(c1, sc1); c++)
                    {
                        double px = map.VertexX(c);
                        double t = l2 > 1e-12 ? Math.Max(0, Math.Min(1, ((px - a.X) * sx + (py - a.Y) * sy) / l2)) : 0;
                        double dx = px - (a.X + sx * t), dy = py - (a.Y + sy * t);
                        double d = Math.Sqrt(dx * dx + dy * dy);
                        int li = (r - r0) * w0 + (c - c0);
                        if (d < best[li])
                        {
                            best[li] = d;
                            bestH[li] = centre[i] + (centre[i + 1] - centre[i]) * t;
                        }
                    }
                }
            }

            map.BeginModify(c0, r0, c1, r1);
            var h = map.Heights;
            layer = Math.Max(0, Math.Min(TerrainLayers.Count - 1, layer));
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                int li = (r - r0) * w0 + (c - c0);
                double d = best[li];
                if (d >= reach) continue;
                double w = d <= half ? 1 : Falloff(d - half, shoulder, 0);
                if (w <= 0) continue;
                int idx = r * map.Columns + c;
                map.PaintVertex(idx, layer, w);
                if (flatten) h[idx] = (float)(h[idx] + (bestH[li] - h[idx]) * w);
            }

            return Done(map, c0, r0, c1, r1, flatten, true);
        }

        // ================================================================== noise

        private static double Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (double)0xFFFFFF * 2 - 1;
            }
        }

        /// <summary>Smooth value noise in −1…1.</summary>
        public static double ValueNoise(double x, double y)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double fx = x - xi, fy = y - yi;
            double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            double a = Hash(xi, yi), b = Hash(xi + 1, yi), c = Hash(xi, yi + 1), d = Hash(xi + 1, yi + 1);
            return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
        }

        /// <summary>Fractal (fBm) value noise in about −1…1.</summary>
        public static double Fbm(double x, double y, int octaves)
        {
            double sum = 0, amp = 0.5, norm = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(x, y);
                norm += amp;
                x = x * 2.03 + 17.1;
                y = y * 2.03 - 9.7;
                amp *= 0.5;
            }

            return sum / norm;
        }
    }
}
