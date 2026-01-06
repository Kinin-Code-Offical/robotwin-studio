using System;

namespace RobotTwin.CoreSim.Models.Physics
{
    public static class ComponentVariation
    {
        public static double NormalizePercent(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            double abs = Math.Abs(value);
            if (abs > 1.0) return value / 100.0;
            return value;
        }

        public static double ApplyTolerance(double nominal, double tolerance, double sampleSigned)
        {
            if (tolerance == 0.0) return nominal;
            double tol = NormalizePercent(tolerance);
            return nominal * (1.0 + sampleSigned * tol);
        }

        public static double ApplyAging(double nominal, double ratePerYear, double years)
        {
            if (years <= 0.0 || ratePerYear == 0.0) return nominal;
            double rate = NormalizePercent(ratePerYear);
            return nominal * (1.0 + rate * years);
        }

        public static double ApplyNoise(double value, double noiseRms, double sampleSigned)
        {
            if (noiseRms <= 0.0) return value;
            return value + noiseRms * sampleSigned;
        }
    }
}
