using System;

namespace RobotTwin.CoreSim.Sdk
{
    /// <summary>
    /// Seeded, platform-independent random numbers (SplitMix64 + Box–Muller). Unlike
    /// <see cref="System.Random"/> the sequence is fixed by this code, so a seed gives the same
    /// turbulence, gusts and behaviour noise on every machine and .NET version.
    /// </summary>
    public sealed class SdkRandom
    {
        private ulong _state;
        private double _spare;
        private bool _hasSpare;

        public SdkRandom(long seed)
        {
            Seed = seed;
            _state = (ulong)seed ^ 0x9E3779B97F4A7C15UL;
        }

        public long Seed { get; }

        /// <summary>A new generator whose stream depends on this seed and a label (one stream per behaviour/axis).</summary>
        public static SdkRandom Derive(long seed, string label)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                foreach (char c in label ?? string.Empty)
                {
                    h ^= c;
                    h *= 1099511628211UL;
                }

                return new SdkRandom((long)(h ^ (ulong)seed * 0xBF58476D1CE4E5B9UL));
            }
        }

        public ulong NextUInt64()
        {
            unchecked
            {
                ulong z = _state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform in [min, max).</summary>
        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Standard normal (mean 0, standard deviation 1).</summary>
        public double NextGaussian()
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare;
            }

            double u, v, s;
            do
            {
                u = 2 * NextDouble() - 1;
                v = 2 * NextDouble() - 1;
                s = u * u + v * v;
            }
            while (s >= 1 || s == 0);

            double m = Math.Sqrt(-2 * Math.Log(s) / s);
            _spare = v * m;
            _hasSpare = true;
            return u * m;
        }

        /// <summary>Exponentially distributed waiting time with the given mean.</summary>
        public double NextExponential(double mean) => -mean * Math.Log(1 - NextDouble());

        // ------------------------------------------------------------ stateless

        private static ulong Mix(ulong z)
        {
            unchecked
            {
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static double Lattice(long i, long seed) =>
            (Mix(unchecked((ulong)i * 0x9E3779B97F4A7C15UL ^ (ulong)seed * 0xD1B54A32D192ED03UL)) >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Repeatable pseudo-random value in [0, 1) for a number and a seed (the expression language's hash()).</summary>
        public static double Hash01(double x, long seed = 0)
        {
            long bits = BitConverter.DoubleToInt64Bits(x);
            return Lattice(bits, seed);
        }

        /// <summary>Smooth 1-D value noise in [−1, 1]: integer lattice values with a C¹ blend.</summary>
        public static double ValueNoise(double x, long seed = 0)
        {
            if (double.IsNaN(x) || double.IsInfinity(x)) return 0;
            double f = Math.Floor(x);
            long i = (long)f;
            double u = x - f;
            u = u * u * (3 - 2 * u);
            double a = Lattice(i, seed) * 2 - 1, b = Lattice(i + 1, seed) * 2 - 1;
            return a + (b - a) * u;
        }
    }
}
