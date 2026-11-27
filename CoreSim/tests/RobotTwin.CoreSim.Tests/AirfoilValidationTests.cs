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
    /// flat-plate limit, aspect ratio 50), and the fall just past the stall onto the separated flow's lift at five
    /// Reynolds numbers (sharp at 1.6·10⁵, over a few degrees at 10⁶).
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
                Assert.InRange(at - alpha, -1.0, 1.5);
            }
        }

        // Table 2 past the stall (C_l at whole degrees)
        private static readonly (double Re, (double Alpha, double Cl)[] Rows)[] PastStall =
        {
            (160_000, new[] { (11.0, 0.1095), (12.0, 0.1533), (13.0, 0.2030), (14.0, 0.2546), (16.0, 0.3620), (18.0, 0.4768), (20.0, 0.5870), (25.0, 0.8572) }),
            (360_000, new[] { (12.0, 0.4832), (13.0, 0.2759), (14.0, 0.2893), (16.0, 0.3792), (18.0, 0.5047), (20.0, 0.6120), (25.0, 0.8780) }),
            (700_000, new[] { (14.0, 0.6284), (15.0, 0.4907), (16.0, 0.4696), (17.0, 0.5195), (18.0, 0.5584), (20.0, 0.6474) }),
            (1_000_000, new[] { (14.0, 0.8846), (15.0, 0.7108), (16.0, 0.6060), (17.0, 0.5906), (18.0, 0.6030), (20.0, 0.6716) }),
            (2_000_000, new[] { (14.0, 1.1614), (15.0, 1.0478), (16.0, 0.9221) }),
        };

        [Fact]
        public void PastTheStallTheLiftFallsOntoTheSeparatedFlowsAsInTheTable()
        {
            double worst = 0, sum = 0;
            int n = 0;
            foreach (var (re, rows) in PastStall)
            {
                foreach (var (a, cl) in rows)
                {
                    double m = Foil.Evaluate(a * Math.PI / 180, 0, re).Cl;
                    _out.WriteLine($"Re {re,9:0} {a,4:0}°: C_l {m:0.000} (table {cl:0.000}, {m - cl:+0.000;-0.000})");
                    worst = Math.Max(worst, Math.Abs(m - cl));
                    sum += Math.Abs(m - cl);
                    n++;
                }
            }

            _out.WriteLine($"past the stall: mean |ΔC_l| {sum / n:0.000}, largest {worst:0.000} over {n} points");
            Assert.True(sum / n < 0.04, $"mean {sum / n:0.000}");
            Assert.True(worst < 0.12, $"largest {worst:0.000}");
        }
    }
}
