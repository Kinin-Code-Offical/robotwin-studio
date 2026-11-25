using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>[ac] One point of a frequency response: the frequency (Hz) and the output over the input as a complex number.</summary>
    public readonly struct AcPoint
    {
        public AcPoint(double frequency, Complex gain)
        {
            Frequency = frequency;
            Gain = gain;
        }

        public double Frequency { get; }

        public Complex Gain { get; }

        /// <summary>|H| in decibels and the phase in degrees.</summary>
        public double Decibels => 20 * Math.Log10(Math.Max(1e-30, Gain.Magnitude));

        public double PhaseDegrees => Gain.Phase * 180 / Math.PI;
    }

    /// <summary>
    /// [ac] Small-signal AC analysis about the present operating point, as SPICE's .AC: every device stamps itself as it
    /// does in a backward-Euler step of length h, which is affine in 1/h (a capacitor C/h, an inductor's branch −L/h, a
    /// transistor's conductances at its operating point independent of h). Stamping at two step lengths separates the
    /// circuit into G + C/h; the frequency response is then (G + jωC)·x = b with the chosen voltage source driven by 1 V
    /// and every other independent source at rest. Nothing is integrated, so the circuit's state is untouched.
    /// </summary>
    public sealed partial class AnalogSimulator
    {
        /// <summary>
        /// The response at <paramref name="frequencies"/> (Hz) of the voltage between <paramref name="outPlus"/> and
        /// <paramref name="outMinus"/> (nodes; 0 = ground) to the voltage source <paramref name="sourceId"/>, about the
        /// operating point the simulator is at (solve one first, or run the circuit until it settles).
        /// </summary>
        public List<AcPoint> AcResponse(string sourceId, int outPlus, int outMinus, IEnumerable<double> frequencies)
        {
            if (!_initialized) throw new InvalidOperationException("Call Initialize() first.");
            var source = _devices.OfType<VoltageSource>().FirstOrDefault(d => d.Id == sourceId)
                         ?? throw new ArgumentException($"'{sourceId}' bir gerilim kaynağı değil.", nameof(sourceId));
            int n = _size;
            const double h1 = 1e-3, h2 = 2e-3;
            var m1 = Linearised(h1);
            var m2 = Linearised(h2);
            double s1 = 1 / h1, s2 = 1 / h2;
            var g = new double[n, n];
            var c = new double[n, n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    double cij = (m1[i, j] - m2[i, j]) / (s1 - s2);
                    c[i, j] = cij;
                    g[i, j] = m1[i, j] - s1 * cij;
                }
            }

            // The device states the two stamps set up are the next real step's to set again.
            _forceBackwardEuler = true;
            var result = new List<AcPoint>();
            foreach (double f in frequencies)
            {
                double w = 2 * Math.PI * f;
                var a = new Complex[n, n];
                for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    a[i, j] = new Complex(g[i, j], w * c[i, j]);
                var b = new Complex[n];
                b[source.BranchBase] = Complex.One;
                var x = SolveComplex(a, b);
                if (x == null)
                {
                    result.Add(new AcPoint(f, new Complex(double.NaN, double.NaN)));
                    continue;
                }

                Complex vp = outPlus > 0 ? x[outPlus - 1] : Complex.Zero, vm = outMinus > 0 ? x[outMinus - 1] : Complex.Zero;
                result.Add(new AcPoint(f, vp - vm));
            }

            return result;
        }

        /// <summary>The circuit's Jacobian at the operating point for a backward-Euler step of <paramref name="h"/> s (no history: right-hand sides dropped).</summary>
        private double[,] Linearised(double h)
        {
            var info = new StepInfo(Time, h, false, false);
            foreach (var device in _devices) device.BeginStep(info);
            ConfigureStamp(Time, h, false, false, Options.Gmin, 1.0);
            _mna.Clear();
            _stamp.X = _x;
            foreach (var device in _devices) device.Stamp(_stamp);
            _mna.AddDiagonal(_nodeCount, _stamp.Gmin);
            var m = new double[_size, _size];
            for (int i = 0; i < _size; i++)
            for (int j = 0; j < _size; j++)
                m[i, j] = _mna.Get(i, j);
            return m;
        }

        /// <summary>Gaussian elimination with partial pivoting on a dense complex system; null when singular.</summary>
        private static Complex[]? SolveComplex(Complex[,] a, Complex[] b)
        {
            int n = b.Length;
            for (int k = 0; k < n; k++)
            {
                int pivot = k;
                double best = a[k, k].Magnitude;
                for (int i = k + 1; i < n; i++)
                {
                    double m = a[i, k].Magnitude;
                    if (m > best)
                    {
                        best = m;
                        pivot = i;
                    }
                }

                if (best < 1e-300) return null;
                if (pivot != k)
                {
                    for (int j = k; j < n; j++) (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
                    (b[k], b[pivot]) = (b[pivot], b[k]);
                }

                for (int i = k + 1; i < n; i++)
                {
                    var f = a[i, k] / a[k, k];
                    if (f == Complex.Zero) continue;
                    for (int j = k; j < n; j++) a[i, j] -= f * a[k, j];
                    b[i] -= f * b[k];
                }
            }

            var x = new Complex[n];
            for (int i = n - 1; i >= 0; i--)
            {
                var s = b[i];
                for (int j = i + 1; j < n; j++) s -= a[i, j] * x[j];
                x[i] = s / a[i, i];
            }

            return x;
        }

        /// <summary>Frequencies from <paramref name="from"/> to <paramref name="to"/> Hz, <paramref name="perDecade"/> to a decade (logarithmic).</summary>
        public static IEnumerable<double> Decades(double from, double to, int perDecade = 20)
        {
            double lo = Math.Log10(Math.Max(1e-6, from)), hi = Math.Log10(Math.Max(from, to));
            int count = Math.Max(1, (int)Math.Round((hi - lo) * perDecade));
            for (int i = 0; i <= count; i++) yield return Math.Pow(10, lo + (hi - lo) * i / count);
        }

        /// <summary>The first frequency where the response falls 3 dB below its value at the lowest frequency (a corner), or NaN.</summary>
        public static double Corner(IReadOnlyList<AcPoint> response)
        {
            if (response.Count < 2) return double.NaN;
            double reference = response[0].Decibels - 3.0103;
            for (int i = 1; i < response.Count; i++)
            {
                double a = response[i - 1].Decibels, b = response[i].Decibels;
                if (a >= reference && b < reference)
                {
                    double t = (reference - a) / (b - a);
                    return Math.Pow(10, Math.Log10(response[i - 1].Frequency) + t * (Math.Log10(response[i].Frequency) - Math.Log10(response[i - 1].Frequency)));
                }
            }

            return double.NaN;
        }
    }
}
