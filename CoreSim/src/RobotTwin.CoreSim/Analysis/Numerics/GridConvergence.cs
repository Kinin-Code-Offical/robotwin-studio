using System;

namespace RobotTwin.CoreSim.Analysis.Numerics
{
    /// <summary>[convergence] How a result behaves over three grids, finest first.</summary>
    public enum ConvergenceKind
    {
        /// <summary>The differences shrink with the same sign: Richardson extrapolation applies.</summary>
        Monotonic,

        /// <summary>The differences change sign: the result oscillates about its limit (bounded by the grids' spread).</summary>
        Oscillatory,

        /// <summary>The differences grow: the grids are not yet in the asymptotic range (or the quantity is singular).</summary>
        Divergent,

        /// <summary>The three results are equal to rounding.</summary>
        Converged
    }

    /// <summary>[convergence] A three-grid study of one result (Celik et al. 2008, ASME V&amp;V 20 / Roache's GCI).</summary>
    public sealed class ConvergenceStudy
    {
        /// <summary>Element sizes (finest first) and the result on each.</summary>
        public double H1, H2, H3, F1, F2, F3;

        public ConvergenceKind Kind;

        /// <summary>The observed order of accuracy (NaN unless monotonic).</summary>
        public double Order = double.NaN;

        /// <summary>The Richardson-extrapolated value (the fine result unless monotonic).</summary>
        public double Extrapolated = double.NaN;

        /// <summary>The fine grid's convergence index: the relative band the grid-independent value lies in (95 %).</summary>
        public double GciFine = double.NaN;

        /// <summary>The relative change from the medium to the fine grid.</summary>
        public double RelativeChange;
    }

    /// <summary>
    /// [convergence] Grid convergence by Richardson extrapolation. Three grids (sizes h1 &lt; h2 &lt; h3, any refinement
    /// ratios): the observed order p from the two differences (solved by fixed-point iteration when the ratios differ), the
    /// extrapolated value, and the fine grid's convergence index GCI = 1.25 · |f1 − f2| / |f1| / (r21^p − 1). Two grids:
    /// the formal order and a safety factor of 3.
    /// </summary>
    public static class GridConvergence
    {
        public const double ThreeGridSafety = 1.25, TwoGridSafety = 3.0;

        public static ConvergenceStudy ThreeGrids(double h1, double f1, double h2, double f2, double h3, double f3)
        {
            if (!(h1 > 0 && h2 > h1 && h3 > h2)) throw new ArgumentException("Grid sizes must grow: h1 < h2 < h3.");
            var s = new ConvergenceStudy { H1 = h1, H2 = h2, H3 = h3, F1 = f1, F2 = f2, F3 = f3, Extrapolated = f1 };
            double e21 = f2 - f1, e32 = f3 - f2;
            double scale = Math.Max(Math.Abs(f1), 1e-300);
            s.RelativeChange = Math.Abs(e21) / scale;
            if (Math.Abs(e21) <= 1e-12 * scale && Math.Abs(e32) <= 1e-12 * scale)
            {
                s.Kind = ConvergenceKind.Converged;
                s.GciFine = 0;
                return s;
            }

            double ratio = Math.Abs(e21) > 0 ? e32 / e21 : double.PositiveInfinity;
            if (ratio < 0)
            {
                s.Kind = ConvergenceKind.Oscillatory;
                s.GciFine = ThreeGridSafety * Math.Max(Math.Abs(e21), Math.Abs(e32)) / scale;   // bounded by the spread
                return s;
            }

            if (Math.Abs(e32) <= Math.Abs(e21))
            {
                s.Kind = ConvergenceKind.Divergent;
                return s;
            }

            s.Kind = ConvergenceKind.Monotonic;
            double r21 = h2 / h1, r32 = h3 / h2;
            double p = Math.Log(Math.Abs(e32 / e21)) / Math.Log(r21);
            for (int i = 0; i < 100 && Math.Abs(r21 - r32) > 1e-9; i++)
            {
                // ln|e32/e21| + q(p), q(p) = ln((r21^p − s)/(r32^p − s)), s = 1 for monotonic convergence
                double q = Math.Log((Math.Pow(r21, p) - 1) / (Math.Pow(r32, p) - 1));
                double next = Math.Abs(Math.Log(Math.Abs(e32 / e21)) + q) / Math.Log(r21);
                if (Math.Abs(next - p) < 1e-10) break;
                p = next;
            }

            s.Order = p;
            double rp = Math.Pow(r21, p);
            s.Extrapolated = (rp * f1 - f2) / (rp - 1);
            s.GciFine = ThreeGridSafety * s.RelativeChange / (rp - 1);
            return s;
        }

        /// <summary>Two grids: the fine grid's convergence index with the method's formal order and a safety factor of 3.</summary>
        public static double TwoGrids(double hFine, double fFine, double hCoarse, double fCoarse, double formalOrder = 2)
        {
            double r = hCoarse / hFine;
            if (!(r > 1.0001) || Math.Abs(fFine) < 1e-300) return double.NaN;
            return TwoGridSafety * Math.Abs((fCoarse - fFine) / fFine) / (Math.Pow(r, formalOrder) - 1);
        }
    }
}
