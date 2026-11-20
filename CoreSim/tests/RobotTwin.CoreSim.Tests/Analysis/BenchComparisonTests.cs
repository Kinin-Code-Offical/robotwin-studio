using System;
using System.Globalization;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analysis;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests.Analysis
{
    public class BenchComparisonTests
    {
        private readonly ITestOutputHelper _out;

        public BenchComparisonTests(ITestOutputHelper output) => _out = output;

        /// <summary>Simulated servo current: moving pulses of 250 mA on a 50 mA idle, 2 Hz.</summary>
        private static double Sim(double t) => 0.05 + 0.2 * (Math.Sin(2 * Math.PI * 2 * t) > 0 ? 1 : 0.2);

        private static (double[] T, double[] V) SimRun()
        {
            var t = Enumerable.Range(0, 2001).Select(i => i * 1e-3).ToArray();
            return (t, t.Select(Sim).ToArray());
        }

        /// <summary>A bench log as an Arduino would print it: millis() since boot; semicolons and decimal commas.</summary>
        private static BenchSeries Log(Func<double, double> value)
        {
            var csv = new StringBuilder("t_ms;akim_mA\n");
            var rnd = new Random(3);
            for (int i = 0; i <= 420; i++)
            {
                double tau = i * 5e-3;   // 200 Hz logger
                double v = value(tau) + (rnd.NextDouble() - 0.5) * 0.004;
                csv.Append((5000 + tau * 1000).ToString("0", CultureInfo.InvariantCulture)).Append(';')
                   .Append((v * 1000).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',')).Append('\n');
            }

            var s = BenchComparison.ParseCsv(csv.ToString()).Single();
            for (int i = 0; i < s.V.Length; i++) s.V[i] /= 1000;   // mA → A
            return s;
        }

        [Fact]
        public void FindsTheTimeLagAndTheCloneGain()
        {
            var (t, v) = SimRun();
            // A clone draws 30 % more; the log's clock runs 40 ms behind the motion.
            var meas = Log(tau => 1.3 * Sim(tau - 0.04));
            var r = BenchComparison.Compare(t, v, meas);
            _out.WriteLine(r.Summary("A"));
            Assert.InRange(r.Lag, 0.03, 0.05);
            Assert.InRange(r.Gain, 1.25, 1.35);
            Assert.InRange(r.Offset, -0.02, 0.02);
        }

        [Fact]
        public void FindsWhereTheBenchDiverges()
        {
            var (t, v) = SimRun();
            // Same part, but the real arm hits an end stop at 1.5 s and stalls at 650 mA.
            var meas = Log(tau => tau < 1.5 ? Sim(tau) : 0.65);
            var r = BenchComparison.Compare(t, v, meas);
            _out.WriteLine(r.Summary("A"));
            Assert.InRange(r.Lag, -0.01, 0.01);
            Assert.InRange(r.FirstDivergence, 1.45, 1.6);
            Assert.InRange(r.Agreement, 0.6, 0.85);   // agrees until the stall, then not
        }
    }
}
