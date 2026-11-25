using System;
using System.Linq;

namespace RobotTwin.CoreSim.Instruments
{
    /// <summary>
    /// [scope] The oscilloscope's math channel: A − B, A + B, A × B (a voltage times a current is the power), ∫A dt (a
    /// current's charge, a power's energy) and dA/dt (a slew rate), on what the screen shows, in the units that follow.
    /// </summary>
    public static class ScopeMath
    {
        public sealed class Result
        {
            public Result(string name, string unit, double[] values, bool clipped)
            {
                Name = name;
                Unit = unit;
                Values = values;
                Clipped = clipped;
            }

            public string Name { get; }
            public string Unit { get; }
            public double[] Values { get; }
            public bool Clipped { get; }
        }

        /// <summary>The math trace; null when it cannot be made (a source is off, or A ± B of different units).</summary>
        public static Result? Compute(ScopeMathFunction f, ScopeTrace? a, ScopeTrace? b, double dt, int sourceA, int sourceB)
        {
            if (f == ScopeMathFunction.Off || a == null) return null;
            string na = "CH" + (sourceA + 1), nb = "CH" + (sourceB + 1);
            int n = a.Values.Length;
            var v = new double[n];
            switch (f)
            {
                case ScopeMathFunction.Add:
                case ScopeMathFunction.Subtract:
                case ScopeMathFunction.Multiply:
                {
                    if (b == null || b.Values.Length != n) return null;
                    bool sum = f != ScopeMathFunction.Multiply;
                    if (sum && a.Unit != b.Unit) return null;   // volts and amps do not add
                    for (int i = 0; i < n; i++)
                        v[i] = f == ScopeMathFunction.Add ? a.Values[i] + b.Values[i] : f == ScopeMathFunction.Subtract ? a.Values[i] - b.Values[i] : a.Values[i] * b.Values[i];
                    string name = f == ScopeMathFunction.Add ? na + "+" + nb : f == ScopeMathFunction.Subtract ? na + "−" + nb : na + "×" + nb;
                    return new Result(name, sum ? a.Unit : Product(a.Unit, b.Unit), v, a.Clipped || b.Clipped);
                }

                case ScopeMathFunction.Integrate:
                    // the trapezoid rule from the screen's left edge
                    for (int i = 1; i < n; i++) v[i] = v[i - 1] + 0.5 * (a.Values[i] + a.Values[i - 1]) * dt;
                    return new Result("∫" + na, Integral(a.Unit), v, a.Clipped);

                case ScopeMathFunction.Differentiate:
                    if (n < 2 || dt <= 0) return null;
                    for (int i = 0; i < n; i++)
                    {
                        int lo = Math.Max(0, i - 1), hi = Math.Min(n - 1, i + 1);
                        v[i] = (a.Values[hi] - a.Values[lo]) / ((hi - lo) * dt);   // central differences inside, one-sided at the ends
                    }

                    return new Result("d" + na + "/dt", a.Unit + "/s", v, a.Clipped);
            }

            return null;
        }

        /// <summary>The unit of a product: V × A = W, V × V = V², else "a·b".</summary>
        public static string Product(string a, string b)
        {
            if ((a == "V" && b == "A") || (a == "A" && b == "V")) return "W";
            if (a == b && (a == "V" || a == "A")) return a + "²";
            return a + "·" + b;
        }

        /// <summary>The unit of a time integral: A·s = C, W·s = J, V·s for a voltage.</summary>
        public static string Integral(string unit) => unit switch
        {
            "A" => "C",
            "W" => "J",
            _ => unit + "·s"
        };

        /// <summary>A 1-2-5 scale (per division) that puts the trace's largest value about three divisions from the centre.</summary>
        public static double NiceScale(double[] values)
        {
            double peak = values.Length == 0 ? 0 : values.Max(x => Math.Abs(x));
            if (!(peak > 0) || double.IsNaN(peak) || double.IsInfinity(peak)) return 1.0;
            double raw = peak / 3.0;
            double decade = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 })
                if (m * decade >= raw) return m * decade;
            return 10 * decade;
        }
    }
}
