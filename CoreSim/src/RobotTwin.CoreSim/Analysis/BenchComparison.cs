using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>One measured signal from the bench (time in seconds).</summary>
    public sealed class BenchSeries
    {
        public string Name { get; set; } = string.Empty;
        public double[] T { get; set; } = Array.Empty<double>();
        public double[] V { get; set; } = Array.Empty<double>();
    }

    /// <summary>How a simulated signal compares with the same signal measured on the bench.</summary>
    public sealed class BenchComparison
    {
        /// <summary>Time shift that best aligns the measurement to the simulation (s, + = measurement later).</summary>
        public double Lag { get; set; }

        /// <summary>Least-squares fit measured ≈ Gain·simulated + Offset over the overlap.</summary>
        public double Gain { get; set; } = 1;

        public double Offset { get; set; }

        public double Rmse { get; set; }

        /// <summary>RMSE relative to the measured range.</summary>
        public double Nrmse { get; set; }

        public double MaxError { get; set; }
        public double MaxErrorTime { get; set; }

        /// <summary>Share of the overlap where |sim − meas| is within the tolerance (fraction of the measured range).</summary>
        public double Agreement { get; set; }

        /// <summary>First time (s) the two stay apart by more than the tolerance for ≥ 50 ms, or NaN.</summary>
        public double FirstDivergence { get; set; } = double.NaN;

        public int Samples { get; set; }

        public string Summary(string unit)
        {
            var ci = CultureInfo.InvariantCulture;
            string div = double.IsNaN(FirstDivergence) ? "ayrılma yok" : "ilk ayrılma t = " + FirstDivergence.ToString("0.000", ci) + " s";
            return string.Format(ci, "gecikme {0:+0.000;-0.000} s · ölçüm ≈ {1:0.00}×sim {2:+0.###;-0.###} {7} · RMSE {3:0.###} {7} (%{4:0.0}) · en büyük fark {5:0.###} {7} @ {6:0.000} s · uyum %{8:0} · {9}",
                Lag, Gain, Offset, Rmse, Nrmse * 100, MaxError, MaxErrorTime, unit, Agreement * 100, div);
        }

        /// <summary>
        /// Reads a bench log: a header row, then numbers separated by ',', ';' or tabs (decimal point
        /// or comma). The first column is time; its header decides the unit ("ms", "us"/"µs", else s).
        /// </summary>
        public static List<BenchSeries> ParseCsv(string text)
        {
            var lines = text.Replace("\r", string.Empty).Split('\n').Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count < 3) throw new FormatException("CSV: başlık ve en az iki veri satırı gerekir.");
            char sep = lines[0].Contains('\t') ? '\t' : lines[0].Contains(';') ? ';' : ',';
            var header = lines[0].Split(sep).Select(h => h.Trim().Trim('"')).ToArray();
            string th = header[0].ToLowerInvariant();
            double scale = th.Contains("ms") ? 1e-3 : th.Contains("us") || th.Contains("µs") ? 1e-6 : 1;
            var cols = Enumerable.Range(0, header.Length).Select(_ => new List<double>()).ToArray();
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split(sep);
                if (f.Length < header.Length) continue;
                var vals = new double[header.Length];
                bool ok = true;
                for (int i = 0; i < header.Length && ok; i++)
                {
                    string s = f[i].Trim().Trim('"');
                    if (sep != ',' && s.Contains(',') && !s.Contains('.')) s = s.Replace(',', '.');
                    ok = double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i]);
                }

                if (!ok) continue;
                for (int i = 0; i < header.Length; i++) cols[i].Add(vals[i]);
            }

            var t = cols[0].Select(x => x * scale).ToArray();
            if (t.Length > 0)
            {
                double t0 = t[0];
                for (int i = 0; i < t.Length; i++) t[i] -= t0;   // logs often start at millis() since boot
            }

            var list = new List<BenchSeries>();
            for (int c = 1; c < header.Length; c++) list.Add(new BenchSeries { Name = header[c], T = t, V = cols[c].ToArray() });
            return list;
        }

        private static double Interp(double[] t, double[] v, double x)
        {
            if (t.Length == 0) return double.NaN;
            if (x <= t[0]) return v[0];
            if (x >= t[^1]) return v[^1];
            int lo = 0, hi = t.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (t[mid] <= x) lo = mid;
                else hi = mid;
            }

            double k = (x - t[lo]) / Math.Max(1e-15, t[hi] - t[lo]);
            return v[lo] + (v[hi] - v[lo]) * k;
        }

        /// <summary>
        /// Compares a simulated signal with a measurement: best time alignment (±<paramref name="maxLag"/>),
        /// linear gain/offset, errors, agreement within <paramref name="tolerance"/> (fraction of the
        /// measured range) and where they first diverge.
        /// </summary>
        public static BenchComparison Compare(double[] simT, double[] simV, BenchSeries meas, double tolerance = 0.1, double maxLag = 0.5)
        {
            var r = new BenchComparison();
            if (simT.Length < 2 || meas.T.Length < 2) return r;
            double lo = Math.Max(simT[0], meas.T[0]), hi = Math.Min(simT[^1], meas.T[^1]);
            if (hi <= lo) return r;
            int n = Math.Min(4000, Math.Max(50, simT.Length));
            double dt = (hi - lo) / (n - 1);
            double Sim(double x) => Interp(simT, simV, x);

            // Lag: maximise the normalised cross-correlation over ±maxLag.
            double bestLag = 0, best = double.NegativeInfinity;
            int lags = (int)Math.Min(400, Math.Ceiling(maxLag / Math.Max(dt, 1e-4)));
            for (int k = -lags; k <= lags; k++)
            {
                double lag = k * Math.Max(dt, 1e-4), sa = 0, sb = 0, sab = 0, saa = 0, sbb = 0;
                int m = 0;
                for (int i = 0; i < n; i++)
                {
                    double x = lo + i * dt;
                    if (x + lag < meas.T[0] || x + lag > meas.T[^1]) continue;
                    double a = Sim(x), b = Interp(meas.T, meas.V, x + lag);
                    sa += a; sb += b; sab += a * b; saa += a * a; sbb += b * b;
                    m++;
                }

                if (m < 10) continue;
                double cov = sab - sa * sb / m, va = saa - sa * sa / m, vb = sbb - sb * sb / m;
                double c = va > 1e-30 && vb > 1e-30 ? cov / Math.Sqrt(va * vb) : -Math.Abs(sab / m - sa * sb / (m * (double)m));
                if (c > best)
                {
                    best = c;
                    bestLag = lag;
                }
            }

            r.Lag = bestLag;
            var sim = new List<double>();
            var mea = new List<double>();
            var times = new List<double>();
            for (int i = 0; i < n; i++)
            {
                double x = lo + i * dt;
                if (x + bestLag < meas.T[0] || x + bestLag > meas.T[^1]) continue;
                sim.Add(Sim(x));
                mea.Add(Interp(meas.T, meas.V, x + bestLag));
                times.Add(x);
            }

            int cnt = sim.Count;
            r.Samples = cnt;
            if (cnt < 2) return r;
            double ms = sim.Average(), mm = mea.Average();
            double sxx = 0, sxy = 0;
            for (int i = 0; i < cnt; i++)
            {
                sxx += (sim[i] - ms) * (sim[i] - ms);
                sxy += (sim[i] - ms) * (mea[i] - mm);
            }

            r.Gain = sxx > 1e-30 ? sxy / sxx : 1;
            r.Offset = mm - r.Gain * ms;
            double range = Math.Max(1e-12, mea.Max() - mea.Min());
            double tol = tolerance * range, se = 0, within = 0, runStart = double.NaN;
            for (int i = 0; i < cnt; i++)
            {
                double e = Math.Abs(sim[i] - mea[i]);
                se += e * e;
                if (e > r.MaxError)
                {
                    r.MaxError = e;
                    r.MaxErrorTime = times[i];
                }

                if (e <= tol)
                {
                    within++;
                    runStart = double.NaN;
                }
                else
                {
                    if (double.IsNaN(runStart)) runStart = times[i];
                    if (double.IsNaN(r.FirstDivergence) && times[i] - runStart >= 0.05) r.FirstDivergence = runStart;
                }
            }

            r.Rmse = Math.Sqrt(se / cnt);
            r.Nrmse = r.Rmse / range;
            r.Agreement = within / cnt;
            return r;
        }
    }
}
