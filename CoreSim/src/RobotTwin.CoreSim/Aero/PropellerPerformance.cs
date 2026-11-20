using System;

namespace RobotTwin.CoreSim.Aero
{
    /// <summary>
    /// [aero] Fixed-pitch propeller in forward flight as a function of the advance ratio J = V/(n·D)
    /// (V = axial inflow speed, n = rev/s, D = diameter):
    /// <list type="bullet">
    /// <item>C_T(J) = C_T0·(1 − J/J₀): thrust falls almost linearly and vanishes near the zero-thrust
    /// advance ratio J₀ ≈ 1.05·P/D (P = geometric pitch). Beyond J₀ the propeller windmills (C_T &lt; 0).</item>
    /// <item>C_P(J) = C_P0·(1 − (J/J_P)²) with J_P = 1.2·J₀, giving the typical efficiency curve
    /// η = J·C_T/C_P peaking at ≈ 0.7–0.8 around J ≈ 0.75·J₀.</item>
    /// </list>
    /// These are fits to the trend of the UIUC propeller database (Brandt &amp; Selig 2011, AIAA 2011-1255)
    /// for small RC propellers; the static values C_T0, C_P0 come from the part catalogue.
    /// </summary>
    public static class PropellerPerformance
    {
        public static double ZeroThrustAdvance(double pitch, double diameter) => 1.05 * Math.Max(0.1, pitch / Math.Max(1e-6, diameter));

        /// <summary>Advance ratio J = V/(n·D) for an axial inflow (m/s) and a speed in rad/s.</summary>
        public static double AdvanceRatio(double axialSpeed, double omega, double diameter)
        {
            double n = Math.Abs(omega) / (2 * Math.PI);
            if (n * diameter < 1e-6) return axialSpeed > 0 ? 10 : 0;
            return axialSpeed / (n * diameter);
        }

        /// <summary>C_T(J)/C_T0.</summary>
        public static double ThrustRatio(double j, double j0)
        {
            if (j <= 0) return 1 + Math.Min(0.3, -j * 0.3);   // reverse inflow (descending into the disc): slight increase, bounded
            return Math.Max(-0.5, 1 - j / j0);
        }

        /// <summary>C_P(J)/C_P0.</summary>
        public static double PowerRatio(double j, double j0)
        {
            if (j <= 0) return 1;
            double jp = 1.2 * j0;
            return Math.Max(-0.3, 1 - (j / jp) * (j / jp));
        }

        public static double Efficiency(double j, double j0, double ct0, double cp0)
        {
            double cp = cp0 * PowerRatio(j, j0);
            return cp <= 1e-9 ? 0 : j * ct0 * ThrustRatio(j, j0) / cp;
        }
    }
}
