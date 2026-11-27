using System;

namespace RobotWin.Studio.World
{
    /// <summary>[grid] The grid's frame on the floor: its position, its turn about the vertical and its two steps.</summary>
    public static class GridFrame
    {
        /// <summary>The grid point nearest a place: in the turned frame, each axis rounded to its own step (0: left as it is).</summary>
        public static (double X, double Y) Nearest(double x, double y, double originX, double originY, double angleDeg, double stepX, double stepY)
        {
            static double Round(double v, double step) => step > 0 ? Math.Round(v / step) * step : v;
            double a = angleDeg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            double dx = x - originX, dy = y - originY;
            double u = Round(c * dx + s * dy, stepX), v = Round(-s * dx + c * dy, stepY);
            return (originX + c * u - s * v, originY + s * u + c * v);
        }
    }
}
