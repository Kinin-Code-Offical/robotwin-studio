using System;
using System.Linq;
using RobotTwin.CoreSim.Aero;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [validation] The NACA 0012 section polar against the published table it is drawn from: Sheldahl &amp; Klimas,
    /// "Aerodynamic Characteristics of Seven Symmetrical Airfoil Sections Through 180-Degree Angle of Attack" (Sandia
    /// SAND80-2114, 1981), Table 2: the attached flow and the drag at Re = 360 000, the stall (C_l,max and its angle) at
    /// six Reynolds numbers from 8·10⁴ to 2·10⁶, and the deep stall to 90° (a 2-D section: the Viterna model at its
    /// flat-plate limit, aspect ratio 50). Just past the stall the table's lift falls sharply (0.29 at 14° for Re 3.6·10⁵)
    /// while the model keeps it up: that range is printed, not asserted, and the guide says so.
    /// </summary>
    public class AirfoilValidationTests
    {
        private readonly ITestOutputHelper _out;

        public AirfoilValidationTests(ITestOutputHelper output) => _out = output;

        // Table 2, Re = 360 000
        private static readonly (double Alpha, double Cl, double Cd)[] Table =
        {
            (0, 0.0000, 0.0079), (2, 0.2200, 0.0084), (4, 0.4400, 0.0098), (6, 0.6600, 0.0125), (8, 0.8542, 0.0153),
            (10, 0.9811, 0.0184), (11, 0.9132, 0.0204), (12, 0.4832, 0.0217), (14, 0.2893, 0.1060), (20, 0.6120, 0.2970),
            (30, 0.9150, 0.5700), (45, 1.0850, 1.0750), (60, 0.8750, 1.4700), (90, 0.0700, 1.8000),
        };

        // Table 2, the largest C_l and its angle at each Reynolds number
        private static readonly (double Re, double ClMax, double Alpha)[] Stall =
        {
            (80_000, 0.7227, 7), (160_000, 0.8527, 9), (360_000, 0.9811, 10), (700_000, 1.0749, 11), (1_000_000, 1.1212, 12), (2_000_000, 1.2169, 13),
        };

        private static readonly ParametricAirfoil Foil = ParametricAirfoil.Naca0012.WithAspectRatio(50);

        /// <summary>The stall: the first maximum of C_l going up from 0° (past it the lift falls before the flat plate's hump).</summary>
        private static (double Cl, double Alpha) Peak(double re)
        {
            double prev = double.MinValue;
            for (int i = 0; i < 600; i++)
            {
                double cl = Foil.Evaluate(i * 0.05 * Math.PI / 180, 0, re).Cl;
                if (cl < prev - 1e-9) return (prev, (i - 1) * 0.05);
                prev = cl;
            }

            return (prev, 30);
        }

        [Fact]
        public void Naca0012FollowsSheldahlAndKlimasAtRe360000()
        {
            const double Re = 360_000;
            foreach (var (a, cl, cd) in Table)
            {
                var c = Foil.Evaluate(a * Math.PI / 180, 0, Re);
                _out.WriteLine($"{a,4:0}°: Cl {c.Cl,7:0.000} (table {cl,7:0.000})   Cd {c.Cd,7:0.0000} (table {cd,7:0.0000})");
            }

            foreach (var (a, cl, cd) in Table.Where(r => r.Alpha <= 8))
            {
                var c = Foil.Evaluate(a * Math.PI / 180, 0, Re);
                Assert.InRange(c.Cl - cl, -0.03, 0.03);                 // the lift line
                Assert.InRange(c.Cd / cd, 0.85, 1.15);                  // the profile drag
            }

            foreach (var (a, cl, cd) in Table.Where(r => r.Alpha >= 45))
            {
                var c = Foil.Evaluate(a * Math.PI / 180, 0, Re);
                Assert.InRange(c.Cl - cl, -0.1, 0.1);                   // deep stall: a flat plate
                Assert.InRange(c.Cd / cd, 0.85, 1.15);
            }
        }

        [Fact]
        public void Naca0012StallsWhereTheTableDoesFrom80000To2Million()
        {
            foreach (var (re, clMax, alpha) in Stall)
            {
                var (cl, at) = Peak(re);
                _out.WriteLine($"Re {re,9:0}: C_l,max {cl:0.000} at {at:0.0}° (table {clMax:0.000} at {alpha:0}°, {(cl / clMax - 1) * 100:+0.0;-0.0} %)");
                Assert.InRange(cl / clMax, 0.94, 1.06);
                Assert.InRange(at - alpha, -2.0, 1.0);
            }
        }
    }
}
