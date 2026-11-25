using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [drive] A spur gear's true outline: involute flanks (20° pressure angle) from the base circle to the tip circle
    /// (addendum m), a radial flank below the base circle down to the root (dedendum 1.25 m), the root arc between teeth.
    /// Tooth 0 is centred on +X. The models draw it and the assembly checks two gears' teeth against it.
    /// </summary>
    public static class GearGeometry
    {
        public const double PressureAngle = 20 * Math.PI / 180;

        private static double Inv(double a) => Math.Tan(a) - a;

        /// <summary>The outline, counter-clockwise, in the module's units (pass mm for a model, m for physics).</summary>
        public static List<(double X, double Y)> Outline(double module, int teeth, int flankSteps = 6)
        {
            double rp = module * teeth / 2, rb = rp * Math.Cos(PressureAngle), ra = rp + module, rf = Math.Max(rp - 1.25 * module, 0.2 * rp);
            double invP = Inv(PressureAngle);
            // Half the tooth's angular width at a radius.
            double Half(double r) => Math.PI / (2 * teeth) + invP - (r > rb ? Inv(Math.Acos(rb / r)) : 0);
            var pts = new List<(double X, double Y)>();
            void Polar(double r, double t)
            {
                var p = (r * Math.Cos(t), r * Math.Sin(t));
                // No zero-length edges where the root meets the base circle.
                if (pts.Count == 0 || Math.Abs(pts[^1].X - p.Item1) + Math.Abs(pts[^1].Y - p.Item2) > 1e-9 * module) pts.Add(p);
            }

            double r0 = Math.Max(rf, Math.Min(rb, ra));
            for (int k = 0; k < teeth; k++)
            {
                double c = 2 * Math.PI * k / teeth;
                Polar(rf, c - Half(r0));
                for (int i = 0; i <= flankSteps; i++)
                {
                    double r = r0 + (ra - r0) * i / flankSteps;
                    Polar(r, c - Half(r));
                }

                Polar(ra, c);
                for (int i = flankSteps; i >= 0; i--)
                {
                    double r = r0 + (ra - r0) * i / flankSteps;
                    Polar(r, c + Half(r));
                }

                Polar(rf, c + Half(r0));
                double next = 2 * Math.PI * (k + 1) / teeth - Half(r0), here = c + Half(r0);
                for (int i = 1; i < 3; i++) Polar(rf, here + (next - here) * i / 3);
            }

            return pts;
        }

        /// <summary>
        /// How far gear B must turn (rad, counter-clockwise about the common axis) so that, where the two pitch circles
        /// touch, B's tooth space sits on A's tooth: <paramref name="toothA"/> and <paramref name="toothB"/> are the angles
        /// of each gear's tooth 0 measured from the line towards the other gear's centre.
        /// </summary>
        public static double MeshTurn(int teethA, double toothA, int teethB, double toothB)
        {
            double pitchA = 2 * Math.PI / teethA, pitchB = 2 * Math.PI / teethB;
            // A's nearest tooth to the line, as an arc on its pitch circle (in modules: r = z/2).
            double offA = Mod(toothA + pitchA / 2, pitchA) - pitchA / 2;
            double arcA = teethA / 2.0 * offA;
            // Moving together along the common tangent, a B tooth at angle β from its line sits at arc −(z_B/2)·β; its space
            // must be where A's tooth is, half a pitch (π/2 modules of arc) from a B tooth.
            double want = Mod(-(arcA + Math.PI / 2) / (teethB / 2.0), pitchB);
            double turn = Mod(want - Mod(toothB, pitchB), pitchB);
            return turn > pitchB / 2 ? turn - pitchB : turn;
        }

        private static double Mod(double x, double m) => x - m * Math.Floor(x / m);
    }
}
