using System;
using System.Collections.Generic;
using System.Globalization;

namespace RobotTwin.CoreSim.Analysis.Fem
{
    /// <summary>[fatigue] What the fatigue check found for one part.</summary>
    public sealed class FatigueResult
    {
        /// <summary>Miner's damage of the recorded run at the worst node (1 = failure).</summary>
        public double Damage { get; set; }

        public int WorstNode { get; set; } = -1;

        /// <summary>How many times the recorded run can repeat before the worst node fails (∞ when no cycle damages it).</summary>
        public double Repetitions => Damage > 0 ? 1 / Damage : double.PositiveInfinity;

        /// <summary>The same as running time, s.</summary>
        public double LifeSeconds { get; set; } = double.PositiveInfinity;

        /// <summary>The largest stress amplitude counted at the worst node, Pa.</summary>
        public double PeakAmplitude { get; set; }

        /// <summary>The cycles counted at the worst node (half cycles as 0.5).</summary>
        public double Cycles { get; set; }
    }

    /// <summary>
    /// [fatigue] Stress-life (S-N) fatigue of a part from its analysis frames: at each node the von Mises history is
    /// counted into cycles by rainflow (ASTM E1049-85, half cycles from the residue), each cycle's damage taken from the
    /// material's S-N line (Basquin: 0.9·UTS at 10³ cycles, the fatigue strength at 10⁷; below the endurance limit no
    /// damage for steels, the line goes on for aluminium and polymers, which have none) and summed by Miner's rule. Von
    /// Mises has no sign, so each cycle's peak is taken as a fully reversed amplitude (mean zero): on the safe side for a
    /// load that only pulls (0 → peak), exact for one that reverses. Cycles faster than the frames are not seen.
    /// </summary>
    public static class Fatigue
    {
        /// <summary>Cycles to failure at a fully reversed amplitude <paramref name="amplitude"/> (Pa); ∞ under the endurance limit.</summary>
        public static double CyclesToFailure(StructuralMaterial m, double amplitude)
        {
            if (amplitude <= 0) return double.PositiveInfinity;
            double sUlt = 0.9 * m.Ultimate, sE = m.FatigueStrength;
            if (sUlt <= sE || sE <= 0) return double.PositiveInfinity;
            if (amplitude >= m.Ultimate) return 1;
            if (amplitude < sE && m.EnduranceLimit) return double.PositiveInfinity;
            // log N = 3 + 4 · log(sUlt / S) / log(sUlt / sE): 10³ at 0.9·UTS, 10⁷ at the fatigue strength
            double n = 3 + 4 * Math.Log(sUlt / amplitude) / Math.Log(sUlt / sE);
            return Math.Pow(10, Math.Max(0, n));
        }

        /// <summary>
        /// Rainflow counting of a series (ASTM E1049-85, 5.4.4): the reversals kept, ranges of three points compared, a
        /// range closed by a larger one counts a full cycle, what remains counts half cycles. Each cycle as (range, mean, count).
        /// </summary>
        public static List<(double Range, double Mean, double Count)> Rainflow(IReadOnlyList<double> series)
        {
            var cycles = new List<(double, double, double)>();
            // the reversals: the series' turning points
            var rev = new List<double>();
            foreach (double v in series)
            {
                if (rev.Count >= 1 && v == rev[rev.Count - 1]) continue;
                if (rev.Count >= 2 && (rev[rev.Count - 1] - rev[rev.Count - 2]) * (v - rev[rev.Count - 1]) > 0) rev[rev.Count - 1] = v;
                else rev.Add(v);
            }

            var stack = new List<double>();
            foreach (double p in rev)
            {
                stack.Add(p);
                while (stack.Count >= 3)
                {
                    double x = Math.Abs(stack[stack.Count - 1] - stack[stack.Count - 2]);
                    double y = Math.Abs(stack[stack.Count - 2] - stack[stack.Count - 3]);
                    if (x < y) break;
                    if (stack.Count == 3)
                    {
                        // Y contains the starting point: half a cycle, the start dropped
                        cycles.Add((y, (stack[0] + stack[1]) / 2, 0.5));
                        stack.RemoveAt(0);
                    }
                    else
                    {
                        cycles.Add((y, (stack[stack.Count - 2] + stack[stack.Count - 3]) / 2, 1.0));
                        stack.RemoveAt(stack.Count - 2);
                        stack.RemoveAt(stack.Count - 2);
                    }
                }
            }

            for (int i = 0; i + 1 < stack.Count; i++) cycles.Add((Math.Abs(stack[i + 1] - stack[i]), (stack[i + 1] + stack[i]) / 2, 0.5));
            return cycles;
        }

        /// <summary>Miner's damage of one node's von Mises history (Pa) on the material's S-N line.</summary>
        public static double Damage(StructuralMaterial m, IReadOnlyList<double> vonMises, out double peakAmplitude, out double cycles)
        {
            // from zero: the part starts unloaded, so the first rise is a cycle too
            var series = new List<double>(vonMises.Count + 1) { 0 };
            series.AddRange(vonMises);
            double d = 0;
            peakAmplitude = 0;
            cycles = 0;
            foreach (var (range, mean, count) in Rainflow(series))
            {
                // the cycle's peak (mean + half range) as a fully reversed amplitude: von Mises has no sign
                double amplitude = mean + range / 2;
                peakAmplitude = Math.Max(peakAmplitude, amplitude);
                cycles += count;
                d += count / CyclesToFailure(m, amplitude);
            }

            return d;
        }

        /// <summary>The part's fatigue from its frames: the worst node's damage and life.</summary>
        public static FatigueResult Evaluate(StructuralMaterial m, StressResult r)
        {
            var result = new FatigueResult();
            if (r.Frames.Count == 0) return result;
            int nodes = r.Frames[0].VonMises.Length;
            var history = new double[r.Frames.Count];
            for (int n = 0; n < nodes; n++)
            {
                for (int k = 0; k < r.Frames.Count; k++) history[k] = n < r.Frames[k].VonMises.Length ? r.Frames[k].VonMises[n] : 0;
                double d = Damage(m, history, out double peak, out double cycles);
                if (d > result.Damage || result.WorstNode < 0)
                {
                    result.Damage = d;
                    result.WorstNode = n;
                    result.PeakAmplitude = peak;
                    result.Cycles = cycles;
                }
            }

            double span = r.Frames[r.Frames.Count - 1].Time - r.Frames[0].Time;
            result.LifeSeconds = result.Damage > 0 && span > 0 ? span / result.Damage : double.PositiveInfinity;
            return result;
        }

        /// <summary>The report's line about it.</summary>
        public static string Describe(StructuralMaterial m, FatigueResult f)
        {
            var c = CultureInfo.InvariantCulture;
            string mpa(double pa) => (pa / 1e6).ToString("0.#", c) + " MPa";
            if (f.WorstNode < 0) return "Yorulma: değerlendirilecek kare yok.";
            if (double.IsPositiveInfinity(f.Repetitions))
                return $"Yorulma: en büyük genlik {mpa(f.PeakAmplitude)}, {m.Name} için yorulma dayanımının ({mpa(m.FatigueStrength)}) altında: sonsuz ömür.";
            string life = double.IsPositiveInfinity(f.LifeSeconds) ? "—" : FormatDuration(f.LifeSeconds);
            return $"Yorulma (S-N, rainflow, Miner): en kötü düğümde {f.Cycles.ToString("0.#", c)} döngü, en büyük genlik {mpa(f.PeakAmplitude)}, bu koşunun hasarı D = {f.Damage.ToString("0.###E+0", c)}: koşu {f.Repetitions.ToString("0.###E+0", c)} kez tekrarlanabilir (≈ {life} çalışma).";
        }

        private static string FormatDuration(double s)
        {
            var c = CultureInfo.InvariantCulture;
            if (s < 120) return s.ToString("0.#", c) + " s";
            if (s < 7200) return (s / 60).ToString("0.#", c) + " dk";
            if (s < 172800) return (s / 3600).ToString("0.#", c) + " saat";
            if (s < 3.15e7 * 2) return (s / 86400).ToString("0.#", c) + " gün";
            return (s / 3.15e7).ToString("0.#", c) + " yıl";
        }
    }
}
