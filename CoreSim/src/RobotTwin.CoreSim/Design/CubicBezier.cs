using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [bezier] A cubic Bézier curve (KiCad's bezier / gr_curve): start, two control points, end. Cut into straight pieces
    /// for drawing, picking, export and the Gerber, as finely as the tolerance asks.
    /// </summary>
    public static class CubicBezier
    {
        /// <summary>The point at t (0 … 1).</summary>
        public static (double X, double Y) At(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double t)
        {
            double u = 1 - t;
            double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
            return (a * x0 + b * x1 + c * x2 + d * x3, a * y0 + b * y1 + c * y2 + d * y3);
        }

        /// <summary>
        /// Points along the curve, ends included, no piece straying more than <paramref name="tolerance"/> from it: the
        /// chord error of n even pieces is at most ¾·max|second difference| / n² (the control polygon's bend bounds it).
        /// </summary>
        public static List<(double X, double Y)> Flatten(double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3, double tolerance)
        {
            double ddx = Math.Max(Math.Abs(x0 - 2 * x1 + x2), Math.Abs(x1 - 2 * x2 + x3));
            double ddy = Math.Max(Math.Abs(y0 - 2 * y1 + y2), Math.Abs(y1 - 2 * y2 + y3));
            double bend = Math.Sqrt(ddx * ddx + ddy * ddy);
            int n = (int)Math.Ceiling(Math.Sqrt(0.75 * bend / Math.Max(tolerance, 1e-9)));
            n = Math.Max(4, Math.Min(256, n));
            var pts = new List<(double, double)>(n + 1);
            for (int i = 0; i <= n; i++) pts.Add(At(x0, y0, x1, y1, x2, y2, x3, y3, (double)i / n));
            return pts;
        }
    }
}
