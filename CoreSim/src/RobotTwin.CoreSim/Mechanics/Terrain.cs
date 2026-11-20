using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Ground surface z = h(x, y). The default ground is the plane z = 0; slopes and
    /// uneven ground use <see cref="PlaneTerrain"/> or <see cref="HeightMapTerrain"/>.
    /// </summary>
    public abstract class Terrain
    {
        public abstract double Height(double x, double y);

        /// <summary>Unit surface normal (pointing up) at (x, y).</summary>
        public virtual Vec3 Normal(double x, double y)
        {
            var (gx, gy) = Gradient(x, y);
            return new Vec3(-gx, -gy, 1).Normalized();
        }

        /// <summary>(∂h/∂x, ∂h/∂y).</summary>
        public virtual (double Dx, double Dy) Gradient(double x, double y)
        {
            const double e = 1e-4;
            return ((Height(x + e, y) - Height(x - e, y)) / (2 * e), (Height(x, y + e) - Height(x, y - e)) / (2 * e));
        }

        /// <summary>Lowest and highest point of the surface (for quick rejection).</summary>
        public abstract (double Min, double Max) HeightRange { get; }

        /// <summary>
        /// Signed distance of a point above the surface measured along the local normal
        /// (negative = below the ground), with the normal and the surface point.
        /// </summary>
        public double Separation(Vec3 p, out Vec3 normal, out Vec3 surfacePoint)
        {
            double h = Height(p.X, p.Y);
            normal = Normal(p.X, p.Y);
            double s = (p.Z - h) * normal.Z;
            surfacePoint = p - normal * s;
            return s;
        }

        /// <summary>Ray against the surface (marching + bisection); distance or -1.</summary>
        public virtual double Raycast(Vec3 origin, Vec3 dir, double maxDistance)
        {
            var (lo, hi) = HeightRange;
            double f0 = origin.Z - Height(origin.X, origin.Y);
            if (f0 < 0) return 0;
            if (dir.Z >= 0 && origin.Z > hi) return -1;
            double step = Math.Max(0.005, maxDistance / 200), t = 0, prev = f0;
            while (t < maxDistance)
            {
                double tn = Math.Min(maxDistance, t + step);
                var p = origin + dir * tn;
                double f = p.Z - Height(p.X, p.Y);
                if (f <= 0)
                {
                    double a = t, b = tn;
                    for (int i = 0; i < 30; i++)
                    {
                        double m = 0.5 * (a + b);
                        var q = origin + dir * m;
                        if (q.Z - Height(q.X, q.Y) > 0) a = m;
                        else b = m;
                    }

                    return 0.5 * (a + b);
                }

                if (dir.Z < 0 && p.Z < lo - 1e-9) return tn;
                prev = f;
                t = tn;
            }

            _ = prev;
            return -1;
        }
    }

    /// <summary>Flat or inclined plane z = Z0 + Sx·x + Sy·y (a uniform slope).</summary>
    public sealed class PlaneTerrain : Terrain
    {
        public PlaneTerrain(double z0 = 0, double slopeX = 0, double slopeY = 0)
        {
            Z0 = z0;
            SlopeX = slopeX;
            SlopeY = slopeY;
            _normal = new Vec3(-slopeX, -slopeY, 1).Normalized();
        }

        private readonly Vec3 _normal;

        /// <summary>Plane rising along +x at <paramref name="angleRad"/>.</summary>
        public static PlaneTerrain Incline(double angleRad, double z0 = 0) => new PlaneTerrain(z0, Math.Tan(angleRad), 0);

        public double Z0 { get; }
        public double SlopeX { get; }
        public double SlopeY { get; }
        public bool IsFlat => SlopeX == 0 && SlopeY == 0;

        public override double Height(double x, double y) => Z0 + SlopeX * x + SlopeY * y;
        public override Vec3 Normal(double x, double y) => _normal;
        public override (double Dx, double Dy) Gradient(double x, double y) => (SlopeX, SlopeY);
        public override (double Min, double Max) HeightRange => IsFlat ? (Z0, Z0) : (double.NegativeInfinity, double.PositiveInfinity);

        public override double Raycast(Vec3 origin, Vec3 dir, double maxDistance)
        {
            double denom = dir.Dot(_normal);
            double s = (origin.Z - Height(origin.X, origin.Y)) * _normal.Z;
            if (s <= 0) return 0;
            if (denom >= -1e-12) return -1;
            double t = -s / denom;
            return t <= maxDistance ? t : -1;
        }
    }

    /// <summary>
    /// Regular grid of heights with bilinear interpolation (hills, ramps, bumps). Outside the
    /// grid the edge heights continue. Heights are row-major: index = row·Columns + column,
    /// row along y, column along x, starting at (OriginX, OriginY).
    /// </summary>
    public sealed class HeightMapTerrain : Terrain
    {
        private readonly double[] _h;
        private readonly double _min, _max;

        public HeightMapTerrain(int columns, int rows, double cellSize, double originX, double originY, IReadOnlyList<double> heights)
        {
            if (columns < 2 || rows < 2) throw new ArgumentException("height map needs at least 2×2 samples");
            if (heights.Count < columns * rows) throw new ArgumentException("not enough height samples");
            Columns = columns;
            Rows = rows;
            CellSize = Math.Max(1e-4, cellSize);
            OriginX = originX;
            OriginY = originY;
            _h = new double[columns * rows];
            _min = double.PositiveInfinity;
            _max = double.NegativeInfinity;
            for (int i = 0; i < _h.Length; i++)
            {
                _h[i] = heights[i];
                _min = Math.Min(_min, _h[i]);
                _max = Math.Max(_max, _h[i]);
            }
        }

        /// <summary>Samples <paramref name="f"/> on a grid covering width × depth centred on the origin.</summary>
        public static HeightMapTerrain FromFunction(double width, double depth, double cellSize, Func<double, double, double> f)
        {
            int cols = Math.Max(2, (int)Math.Ceiling(width / cellSize) + 1), rows = Math.Max(2, (int)Math.Ceiling(depth / cellSize) + 1);
            double ox = -(cols - 1) * cellSize / 2, oy = -(rows - 1) * cellSize / 2;
            var h = new double[cols * rows];
            for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                h[r * cols + c] = f(ox + c * cellSize, oy + r * cellSize);
            return new HeightMapTerrain(cols, rows, cellSize, ox, oy, h);
        }

        public int Columns { get; }
        public int Rows { get; }
        public double CellSize { get; }
        public double OriginX { get; }
        public double OriginY { get; }
        public IReadOnlyList<double> Heights => _h;
        public override (double Min, double Max) HeightRange => (_min, _max);

        private void Cell(double x, double y, out int c, out int r, out double fx, out double fy)
        {
            double u = (x - OriginX) / CellSize, v = (y - OriginY) / CellSize;
            u = Math.Max(0, Math.Min(Columns - 1 - 1e-9, u));
            v = Math.Max(0, Math.Min(Rows - 1 - 1e-9, v));
            c = Math.Min(Columns - 2, (int)u);
            r = Math.Min(Rows - 2, (int)v);
            fx = u - c;
            fy = v - r;
        }

        public override double Height(double x, double y)
        {
            Cell(x, y, out int c, out int r, out double fx, out double fy);
            int i = r * Columns + c;
            double h00 = _h[i], h10 = _h[i + 1], h01 = _h[i + Columns], h11 = _h[i + Columns + 1];
            return h00 * (1 - fx) * (1 - fy) + h10 * fx * (1 - fy) + h01 * (1 - fx) * fy + h11 * fx * fy;
        }

        public override (double Dx, double Dy) Gradient(double x, double y)
        {
            double u = (x - OriginX) / CellSize, v = (y - OriginY) / CellSize;
            bool outX = u < 0 || u > Columns - 1, outY = v < 0 || v > Rows - 1;
            Cell(x, y, out int c, out int r, out double fx, out double fy);
            int i = r * Columns + c;
            double h00 = _h[i], h10 = _h[i + 1], h01 = _h[i + Columns], h11 = _h[i + Columns + 1];
            double dx = ((h10 - h00) * (1 - fy) + (h11 - h01) * fy) / CellSize;
            double dy = ((h01 - h00) * (1 - fx) + (h11 - h10) * fx) / CellSize;
            return (outX ? 0 : dx, outY ? 0 : dy);
        }
    }

    /// <summary>A rectangular patch of the floor made of another material (ice, carpet, sand …).</summary>
    public sealed class SurfaceRegion
    {
        public SurfaceRegion(double x, double y, double sizeX, double sizeY, string material)
        {
            X = x;
            Y = y;
            SizeX = sizeX;
            SizeY = sizeY;
            Material = Materials.Canonical(material);
            MaterialIndex = Materials.IndexOf(Material);
        }

        internal int MaterialIndex { get; }

        public double X { get; }
        public double Y { get; }
        public double SizeX { get; }
        public double SizeY { get; }
        public string Material { get; }

        public bool Contains(double x, double y) => Math.Abs(x - X) <= SizeX / 2 && Math.Abs(y - Y) <= SizeY / 2;
    }
}
