using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analysis.Numerics
{
    /// <summary>One-sided amplitude spectrum of a real signal.</summary>
    public sealed class Spectrum
    {
        public Spectrum(double[] frequency, double[] amplitude)
        {
            Frequency = frequency;
            Amplitude = amplitude;
        }

        /// <summary>Bin frequencies (Hz).</summary>
        public double[] Frequency { get; }

        /// <summary>Peak amplitude of a sinusoid at that frequency (window-corrected).</summary>
        public double[] Amplitude { get; }

        /// <summary>Frequency of the largest bin above <paramref name="minHz"/> (parabolic interpolation between bins).</summary>
        public double PeakFrequency(double minHz = 0)
        {
            int best = -1;
            for (int i = 1; i < Amplitude.Length; i++)
            {
                if (Frequency[i] < minHz) continue;
                if (best < 0 || Amplitude[i] > Amplitude[best]) best = i;
            }

            if (best <= 0) return 0;
            if (best >= Amplitude.Length - 1) return Frequency[best];
            double a = Amplitude[best - 1], b = Amplitude[best], c = Amplitude[best + 1];
            double den = a - 2 * b + c;
            double shift = Math.Abs(den) > 1e-300 ? 0.5 * (a - c) / den : 0;
            double df = Frequency[1] - Frequency[0];
            return Frequency[best] + shift * df;
        }
    }

    /// <summary>
    /// [analysis] Signal processing for the results workspace: radix-2 FFT, Hann-windowed amplitude
    /// spectra, spectrograms (short-time FFT), resampling, numerical derivative and integral.
    /// </summary>
    public static class Spectral
    {
        /// <summary>In-place complex FFT (length must be a power of two). inverse = true for the unscaled inverse.</summary>
        public static void Fft(double[] re, double[] im, bool inverse = false)
        {
            int n = re.Length;
            if (n <= 1) return;
            if ((n & (n - 1)) != 0) throw new ArgumentException("FFT length must be a power of two");
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    (re[i], re[j]) = (re[j], re[i]);
                    (im[i], im[j]) = (im[j], im[i]);
                }
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                double wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    int half = len >> 1;
                    for (int k = 0; k < half; k++)
                    {
                        int a = i + k, b = a + half;
                        double tr = re[b] * cr - im[b] * ci;
                        double ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        double ncr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = ncr;
                    }
                }
            }
        }

        public static int NextPow2(int n)
        {
            int p = 1;
            while (p < n) p <<= 1;
            return p;
        }

        /// <summary>
        /// Amplitude spectrum of uniformly sampled data (sample rate <paramref name="fs"/>): the mean is
        /// removed, a Hann window applied (amplitude-corrected), zero-padded to a power of two.
        /// </summary>
        public static Spectrum AmplitudeSpectrum(IReadOnlyList<double> x, double fs, int start = 0, int count = -1, bool window = true)
        {
            if (count < 0) count = x.Count - start;
            count = Math.Max(0, Math.Min(count, x.Count - start));
            int n = NextPow2(Math.Max(2, count));
            var re = new double[n];
            var im = new double[n];
            double mean = 0;
            int valid = 0;
            for (int i = 0; i < count; i++)
            {
                double v = x[start + i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                mean += v;
                valid++;
            }

            mean = valid > 0 ? mean / valid : 0;
            double wsum = 0;
            for (int i = 0; i < count; i++)
            {
                double w = window && count > 1 ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (count - 1)) : 1;
                double v = x[start + i];
                if (double.IsNaN(v) || double.IsInfinity(v)) v = mean;
                re[i] = (v - mean) * w;
                wsum += w;
            }

            Fft(re, im);
            int bins = n / 2 + 1;
            var f = new double[bins];
            var a = new double[bins];
            double scale = wsum > 0 ? 2.0 / wsum : 0;
            for (int k = 0; k < bins; k++)
            {
                f[k] = k * fs / n;
                a[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]) * (k == 0 ? scale / 2 : scale);
            }

            return new Spectrum(f, a);
        }

        /// <summary>
        /// Short-time Fourier transform magnitude: rows = time frames, columns = frequency bins
        /// (amplitude, or dB when <paramref name="decibel"/>). Returns frame centre times and bin frequencies.
        /// </summary>
        public static (double[] Times, double[] Frequencies, double[,] Magnitude) Spectrogram(IReadOnlyList<double> x, double fs, double t0, int window = 256,
            int hop = 64, bool decibel = true, int maxFrames = 400)
        {
            window = NextPow2(Math.Max(8, Math.Min(window, Math.Max(8, x.Count))));
            if (x.Count < window) window = Math.Max(8, NextPow2(x.Count) / 2);
            hop = Math.Max(1, hop);
            int frames = x.Count >= window ? 1 + (x.Count - window) / hop : 1;
            if (frames > maxFrames)
            {
                hop = Math.Max(1, (x.Count - window) / (maxFrames - 1));
                frames = x.Count >= window ? 1 + (x.Count - window) / hop : 1;
            }

            int bins = window / 2 + 1;
            var mag = new double[frames, bins];
            var times = new double[frames];
            var freqs = new double[bins];
            for (int k = 0; k < bins; k++) freqs[k] = k * fs / window;
            for (int fIdx = 0; fIdx < frames; fIdx++)
            {
                int s = fIdx * hop;
                var sp = AmplitudeSpectrum(x, fs, s, Math.Min(window, x.Count - s));
                times[fIdx] = t0 + (s + window / 2.0) / fs;
                for (int k = 0; k < bins && k < sp.Amplitude.Length; k++)
                {
                    double v = sp.Amplitude[k];
                    mag[fIdx, k] = decibel ? 20 * Math.Log10(Math.Max(v, 1e-12)) : v;
                }
            }

            return (times, freqs, mag);
        }

        /// <summary>Central-difference derivative (one-sided at the ends).</summary>
        public static double[] Derivative(IReadOnlyList<double> t, IReadOnlyList<double> y)
        {
            int n = y.Count;
            var d = new double[n];
            if (n < 2) return d;
            for (int i = 0; i < n; i++)
            {
                int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
                double dt = t[b] - t[a];
                d[i] = dt > 0 ? (y[b] - y[a]) / dt : 0;
            }

            return d;
        }

        /// <summary>Cumulative trapezoidal integral starting at 0.</summary>
        public static double[] Integral(IReadOnlyList<double> t, IReadOnlyList<double> y)
        {
            int n = y.Count;
            var s = new double[n];
            for (int i = 1; i < n; i++)
            {
                double a = y[i - 1], b = y[i];
                if (double.IsNaN(a)) a = 0;
                if (double.IsNaN(b)) b = 0;
                s[i] = s[i - 1] + 0.5 * (a + b) * (t[i] - t[i - 1]);
            }

            return s;
        }

        /// <summary>Linear interpolation of y(t) at time x (clamped at the ends); t ascending.</summary>
        public static double Interpolate(IReadOnlyList<double> t, IReadOnlyList<double> y, double x)
        {
            int n = t.Count;
            if (n == 0) return double.NaN;
            if (x <= t[0]) return y[0];
            if (x >= t[n - 1]) return y[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (t[mid] <= x) lo = mid;
                else hi = mid;
            }

            double f = (x - t[lo]) / Math.Max(1e-300, t[hi] - t[lo]);
            return y[lo] + (y[hi] - y[lo]) * f;
        }

        /// <summary>Index of the sample nearest to time x.</summary>
        public static int NearestIndex(IReadOnlyList<double> t, double x)
        {
            int n = t.Count;
            if (n == 0) return -1;
            if (x <= t[0]) return 0;
            if (x >= t[n - 1]) return n - 1;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (t[mid] <= x) lo = mid;
                else hi = mid;
            }

            return x - t[lo] <= t[hi] - x ? lo : hi;
        }

        /// <summary>Basic statistics ignoring NaN: min, max, mean, RMS.</summary>
        public static (double Min, double Max, double Mean, double Rms) Stats(IReadOnlyList<double> y, int start = 0, int count = -1)
        {
            if (count < 0) count = y.Count - start;
            double min = double.PositiveInfinity, max = double.NegativeInfinity, sum = 0, sq = 0;
            int n = 0;
            for (int i = start; i < start + count && i < y.Count; i++)
            {
                double v = y[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
                sq += v * v;
                n++;
            }

            return n == 0 ? (double.NaN, double.NaN, double.NaN, double.NaN) : (min, max, sum / n, Math.Sqrt(sq / n));
        }
    }
}
