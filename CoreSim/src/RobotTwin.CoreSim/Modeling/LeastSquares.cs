using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>
    /// [sketch] [mate] The solver under the sketch and assembly constraints: a damped Gauss–Newton in its minimum-norm
    /// (dual) form, δ = −Jᵀ·(J·Jᵀ + λ·D)⁻¹·r, so whatever no equation holds stays where it was; λ grows when a step does
    /// not pay and keeps repeated equations, whose rows make J·Jᵀ singular, solvable. And the rank of the equations,
    /// which tells the freedom left and the rows that repeat earlier ones.
    /// </summary>
    internal static class LeastSquares
    {
        /// <summary>Central-difference Jacobian of <paramref name="f"/> at <paramref name="x"/> (x is restored).</summary>
        public static double[,] Jacobian(Func<double[], double[]> f, double[] x, int rows)
        {
            int n = x.Length;
            var j = new double[rows, n];
            for (int c = 0; c < n; c++)
            {
                double h = 1e-6 * Math.Max(1.0, Math.Abs(x[c])), keep = x[c];
                x[c] = keep + h;
                var rp = f(x);
                x[c] = keep - h;
                var rm = f(x);
                x[c] = keep;
                for (int r = 0; r < rows; r++) j[r, c] = (rp[r] - rm[r]) / (2 * h);
            }

            return j;
        }

        /// <summary>
        /// Drives every residual of <paramref name="f"/> to within <paramref name="tolerance"/>, moving the unknowns as
        /// little as it can. Returns the unknowns, the residuals there and the iterations taken.
        /// </summary>
        public static (double[] X, double[] R, int Iterations) MinNorm(Func<double[], double[]> f, double[] start, double tolerance, int maxIterations = 200)
        {
            var x = (double[])start.Clone();
            var r = f(x);
            double cost = Sum(r), lambda = 1e-6;
            int n = x.Length, it = 0;
            for (; it < maxIterations && Max(r) > tolerance; it++)
            {
                int m = r.Length;
                var j = Jacobian(f, x, m);
                var a = new double[m, m];
                double scale = 0;
                for (int p = 0; p < m; p++)
                {
                    for (int q = p; q < m; q++)
                    {
                        double sum = 0;
                        for (int k = 0; k < n; k++) sum += j[p, k] * j[q, k];
                        a[p, q] = a[q, p] = sum;
                    }

                    scale = Math.Max(scale, a[p, p]);
                }

                bool improved = false;
                for (int tries = 0; tries < 16; tries++)
                {
                    var damped = (double[,])a.Clone();
                    for (int p = 0; p < m; p++) damped[p, p] += lambda * (a[p, p] + 1e-9 * scale) + 1e-14 * scale;
                    var y = SolveLinear(damped, (double[])r.Clone());
                    if (y == null)
                    {
                        lambda = Math.Max(1e-9, lambda * 10);
                        continue;
                    }

                    var trial = new double[n];
                    for (int c = 0; c < n; c++)
                    {
                        double d = 0;
                        for (int k = 0; k < m; k++) d += j[k, c] * y[k];
                        trial[c] = x[c] - d;
                    }

                    var rt = f(trial);
                    double ct = Sum(rt);
                    if (ct < cost)
                    {
                        x = trial;
                        r = rt;
                        cost = ct;
                        lambda = Math.Max(1e-12, lambda * 0.1);
                        improved = true;
                        break;
                    }

                    lambda = Math.Max(1e-9, lambda * 10);
                }

                if (!improved) break;
            }

            return (x, r, it);
        }

        public static double Sum(double[] r)
        {
            double s = 0;
            foreach (double v in r) s += v * v;
            return s;
        }

        public static double Max(double[] r)
        {
            double s = 0;
            foreach (double v in r) s = Math.Max(s, Math.Abs(v));
            return s;
        }

        /// <summary>Gaussian elimination with partial pivoting; null when singular.</summary>
        public static double[]? SolveLinear(double[,] a, double[] b)
        {
            int n = b.Length;
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++)
                    if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                if (Math.Abs(a[piv, c]) < 1e-300) return null;
                if (piv != c)
                {
                    for (int k = 0; k < n; k++) (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
                    (b[c], b[piv]) = (b[piv], b[c]);
                }

                for (int r = c + 1; r < n; r++)
                {
                    double f = a[r, c] / a[c, c];
                    if (f == 0) continue;
                    for (int k = c; k < n; k++) a[r, k] -= f * a[c, k];
                    b[r] -= f * b[c];
                }
            }

            var x = new double[n];
            for (int r = n - 1; r >= 0; r--)
            {
                double s = b[r];
                for (int k = r + 1; k < n; k++) s -= a[r, k] * x[k];
                x[r] = s / a[r, r];
            }

            return x;
        }

        /// <summary>The rows of J (in order) that are combinations of earlier ones, by Gram–Schmidt; and J's rank.</summary>
        public static List<int> DependentRows(double[,] j, out int rank)
        {
            int m = j.GetLength(0), n = j.GetLength(1);
            var basis = new List<double[]>();
            var dependent = new List<int>();
            for (int r = 0; r < m; r++)
            {
                var v = new double[n];
                double norm0 = 0;
                for (int c = 0; c < n; c++)
                {
                    v[c] = j[r, c];
                    norm0 += v[c] * v[c];
                }

                norm0 = Math.Sqrt(norm0);
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (var b in basis)
                    {
                        double d = 0;
                        for (int c = 0; c < n; c++) d += v[c] * b[c];
                        for (int c = 0; c < n; c++) v[c] -= d * b[c];
                    }
                }

                double norm = 0;
                for (int c = 0; c < n; c++) norm += v[c] * v[c];
                norm = Math.Sqrt(norm);
                if (norm0 < 1e-12 || norm < 1e-7 * Math.Max(1.0, norm0))
                {
                    dependent.Add(r);
                    continue;
                }

                for (int c = 0; c < n; c++) v[c] /= norm;
                basis.Add(v);
            }

            rank = basis.Count;
            return dependent;
        }
    }
}
