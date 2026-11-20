using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotTwin.CoreSim.Instruments
{
    /// <summary>Formats interpolated strings with the invariant culture (the app shows "4.99 V", never "4,99 V").</summary>
    public static class InvariantText
    {
        public static string I(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Time-ordered (time, value) samples an instrument can read (the "analog input").</summary>
    public interface ISampleSource
    {
        int Count { get; }

        (double T, double V) this[int i] { get; }

        /// <summary>Index of the first sample at or after <paramref name="time"/>.</summary>
        int LowerBound(double time);
    }

    /// <summary>
    /// Ring buffer of (time, value) samples taken at the solver's accepted steps. Between
    /// samples the signal is treated as piecewise linear, which is exactly how the
    /// trapezoidal integrator sees it.
    /// </summary>
    public sealed class SampleBuffer : ISampleSource
    {
        private readonly double[] _t;
        private readonly double[] _v;
        private int _start;

        public SampleBuffer(int capacity = 200_000)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            _t = new double[capacity];
            _v = new double[capacity];
        }

        public int Capacity => _t.Length;

        public int Count { get; private set; }

        public double FirstTime => Count == 0 ? double.NaN : _t[_start];

        public double LastTime => Count == 0 ? double.NaN : _t[(_start + Count - 1) % _t.Length];

        public double Last => Count == 0 ? double.NaN : _v[(_start + Count - 1) % _v.Length];

        /// <summary>Appends a sample; samples that do not advance time are dropped.</summary>
        public void Add(double t, double v)
        {
            int cap = _t.Length;
            if (Count > 0)
            {
                int lastIdx = (_start + Count - 1) % cap;
                if (t <= _t[lastIdx]) return;
            }

            if (Count < cap)
            {
                int idx = (_start + Count) % cap;
                _t[idx] = t;
                _v[idx] = v;
                Count++;
            }
            else
            {
                _t[_start] = t;
                _v[_start] = v;
                _start = (_start + 1) % cap;
            }
        }

        public void Clear()
        {
            _start = 0;
            Count = 0;
        }

        public (double T, double V) this[int i]
        {
            get
            {
                int idx = (_start + i) % _t.Length;
                return (_t[idx], _v[idx]);
            }
        }

        public int LowerBound(double time) => SampleMath.LowerBound(this, time);

        /// <summary>Linearly interpolated value at <paramref name="time"/> (NaN outside the stored span).</summary>
        public double ValueAt(double time) => SampleMath.ValueAt(this, time);
    }

    public static class SampleMath
    {
        public static int LowerBound(ISampleSource s, double time)
        {
            int lo = 0, hi = s.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (s[mid].T < time) lo = mid + 1;
                else hi = mid;
            }

            return lo;
        }

        public static double ValueAt(ISampleSource s, double time)
        {
            int n = s.Count;
            if (n == 0) return double.NaN;
            var first = s[0];
            var last = s[n - 1];
            if (time < first.T || time > last.T) return double.NaN;
            int i = LowerBound(s, time);
            if (i >= n) return last.V;
            var b = s[i];
            if (b.T == time || i == 0) return b.V;
            var a = s[i - 1];
            double span = b.T - a.T;
            return span <= 0.0 ? b.V : a.V + (b.V - a.V) * (time - a.T) / span;
        }

        /// <summary>Time-weighted mean of the piecewise-linear signal over [from, to] (NaN without data).</summary>
        public static double Mean(ISampleSource s, double from, double to)
        {
            if (s.Count < 2 || to <= from) return double.NaN;
            from = Math.Max(from, s[0].T);
            to = Math.Min(to, s[s.Count - 1].T);
            if (to <= from) return s.Count > 0 ? s[s.Count - 1].V : double.NaN;
            double area = 0.0;
            int i = Math.Max(1, LowerBound(s, from));
            double tPrev = from, vPrev = ValueAt(s, from);
            for (; i < s.Count; i++)
            {
                var (t, v) = s[i];
                if (t >= to)
                {
                    double vEnd = ValueAt(s, to);
                    area += 0.5 * (vPrev + vEnd) * (to - tPrev);
                    tPrev = to;
                    break;
                }

                area += 0.5 * (vPrev + v) * (t - tPrev);
                tPrev = t;
                vPrev = v;
            }

            double span = tPrev - from;
            return span > 0 ? area / span : vPrev;
        }
    }

    /// <summary>The 1-2-5 sequence used by every V/div, A/div and time/div knob.</summary>
    public static class Steps125
    {
        /// <summary>The nearest 1-2-5 value to <paramref name="value"/> (by ratio).</summary>
        public static double Snap(double value)
        {
            if (!(value > 0) || double.IsInfinity(value)) return 1.0;
            double exp = Math.Floor(Math.Log10(value));
            double best = 1.0, bestErr = double.MaxValue;
            for (double e = exp - 1; e <= exp + 1; e++)
            {
                foreach (double m in new[] { 1.0, 2.0, 5.0 })
                {
                    double candidate = m * Math.Pow(10, e);
                    double err = Math.Abs(Math.Log(candidate / value));
                    if (err < bestErr)
                    {
                        bestErr = err;
                        best = candidate;
                    }
                }
            }

            return Clean(best);
        }

        /// <summary>The next (direction &gt; 0) or previous 1-2-5 step, clamped to [min, max].</summary>
        public static double Next(double value, int direction, double min = 0.0, double max = double.MaxValue)
        {
            double v = Snap(value);
            double exp = Math.Floor(Math.Log10(v) + 1e-9);
            double mant = Math.Round(v / Math.Pow(10, exp));
            double next;
            if (direction > 0)
            {
                next = mant >= 5 ? Math.Pow(10, exp + 1) : mant >= 2 ? 5 * Math.Pow(10, exp) : 2 * Math.Pow(10, exp);
            }
            else if (direction < 0)
            {
                next = mant <= 1 ? 5 * Math.Pow(10, exp - 1) : mant <= 2 ? Math.Pow(10, exp) : 2 * Math.Pow(10, exp);
            }
            else
            {
                next = v;
            }

            next = Clean(next);
            if (min > 0 && next < min) next = min;
            if (next > max) next = max;
            return next;
        }

        /// <summary>The smallest 1-2-5 value that is at least <paramref name="value"/>.</summary>
        public static double AtLeast(double value)
        {
            double v = Snap(value);
            while (v < value * (1 - 1e-9)) v = Next(v, 1);
            return v;
        }

        public static IReadOnlyList<double> Range(double min, double max)
        {
            var list = new List<double>();
            for (double v = Snap(min); v <= max * (1 + 1e-9); v = Next(v, 1))
            {
                list.Add(v);
                if (list.Count > 200) break;
            }

            return list;
        }

        private static double Clean(double v) => double.Parse(v.ToString("G3", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Small deterministic random source (xorshift64*) so instrument noise is reproducible.</summary>
    public sealed class InstrumentRandom
    {
        private ulong _state;
        private double _spare = double.NaN;

        public InstrumentRandom(ulong seed)
        {
            _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        public static ulong Hash(string text, ulong salt = 0)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL ^ salt;
                foreach (char c in text ?? string.Empty)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }

                return Mix(h);
            }
        }

        public static ulong Mix(ulong z)
        {
            unchecked
            {
                z += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public ulong NextULong()
        {
            unchecked
            {
                _state ^= _state >> 12;
                _state ^= _state << 25;
                _state ^= _state >> 27;
                return _state * 0x2545F4914F6CDD1DUL;
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform in [-1, 1).</summary>
        public double NextSigned() => NextDouble() * 2.0 - 1.0;

        /// <summary>Standard normal (Box-Muller).</summary>
        public double NextGaussian()
        {
            if (!double.IsNaN(_spare))
            {
                double s = _spare;
                _spare = double.NaN;
                return s;
            }

            double u1 = Math.Max(NextDouble(), 1e-300), u2 = NextDouble();
            double r = Math.Sqrt(-2.0 * Math.Log(u1));
            _spare = r * Math.Sin(2.0 * Math.PI * u2);
            return r * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
