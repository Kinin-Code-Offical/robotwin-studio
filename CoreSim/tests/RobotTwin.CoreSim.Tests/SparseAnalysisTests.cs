using System;
using System.Collections.Generic;
using System.Diagnostics;
using RobotTwin.CoreSim.Analog;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [bigscene] The sparse LU's pivot analysis works on the active entries only; it must choose exactly what the dense
    /// n × n search it replaced chose (same pivots, same L/U pattern), so every solve stays bit for bit the same.
    /// </summary>
    public class SparseAnalysisTests
    {
        /// <summary>An MNA-like matrix: conductances between random nodes, a few voltage-source branches (zero diagonal).</summary>
        private static (int N, List<int> Rows, List<int> Cols, List<double> Values) Matrix(Random rng, int nodes, int sources, int links)
        {
            int n = nodes + sources;
            var at = new Dictionary<(int, int), double>();
            void Add(int r, int c, double v) => at[(r, c)] = (at.TryGetValue((r, c), out double old) ? old : 0) + v;
            for (int i = 0; i < nodes; i++) Add(i, i, 1e-12 + rng.NextDouble() * 1e-3);   // gmin and leakage
            for (int k = 0; k < links; k++)
            {
                int a = rng.Next(nodes), b = rng.Next(nodes);
                if (a == b) continue;
                double g = Math.Pow(10, -6 + 8 * rng.NextDouble());
                Add(a, a, g);
                Add(b, b, g);
                Add(a, b, -g);
                Add(b, a, -g);
            }

            for (int s = 0; s < sources; s++)
            {
                int br = nodes + s, plus = rng.Next(nodes);
                Add(plus, br, 1);
                Add(br, plus, 1);
                if (rng.Next(3) == 0)
                {
                    int minus = rng.Next(nodes);
                    if (minus != plus)
                    {
                        Add(minus, br, -1);
                        Add(br, minus, -1);
                    }
                }
            }

            var rows = new List<int>();
            var cols = new List<int>();
            var values = new List<double>();
            foreach (var kv in at)
            {
                rows.Add(kv.Key.Item1);
                cols.Add(kv.Key.Item2);
                values.Add(kv.Value);
            }

            return (n, rows, cols, values);
        }

        [Fact]
        public void TheSparseAnalysisChoosesTheDenseOnesPivotsAndPattern()
        {
            var rng = new Random(20261128);
            int compared = 0, singular = 0;
            for (int trial = 0; trial < 300; trial++)
            {
                int nodes = 3 + rng.Next(80), sources = rng.Next(1 + nodes / 6);
                var (n, rows, cols, values) = Matrix(rng, nodes, sources, rng.Next(nodes, nodes * 3));
                var sparse = new SparseLu();
                var dense = new SparseLu();
                bool a = sparse.Analyze(n, rows.Count, rows.ToArray(), cols.ToArray(), values.ToArray());
                bool b = dense.AnalyzeDense(n, rows.Count, rows.ToArray(), cols.ToArray(), values.ToArray());
                Assert.Equal(b, a);
                if (!a)
                {
                    singular++;
                    continue;
                }

                Assert.Equal(dense.PivotRows, sparse.PivotRows);
                Assert.Equal(dense.PivotColumns, sparse.PivotColumns);
                var (du, dur, dl, dlr) = dense.Pattern;
                var (su, sur, sl, slr) = sparse.Pattern;
                Assert.Equal(du, su);
                Assert.Equal(dur, sur);
                Assert.Equal(dl, sl);
                Assert.Equal(dlr, slr);
                compared++;
            }

            Assert.True(compared > 200, $"{compared} compared, {singular} singular");
        }

        [Fact]
        public void AHugeMatrixKeepsItsEntriesInAHashAndSolvesLikeASmallOne()
        {
            foreach (int n in new[] { 300, 5000 })   // under and over the dense index's limit
            {
                var mna = new MnaSystem();
                mna.Resize(n);
                var rng = new Random(n);
                var expect = new Dictionary<(int, int), double>();
                for (int k = 0; k < 4 * n; k++)
                {
                    int r = rng.Next(n), c = rng.Next(n);
                    double v = rng.NextDouble();
                    mna.Add(r, c, v);
                    expect[(r, c)] = (expect.TryGetValue((r, c), out double old) ? old : 0) + v;
                }

                foreach (var kv in expect) Assert.Equal(kv.Value, mna.Get(kv.Key.Item1, kv.Key.Item2));
                if (!expect.ContainsKey((n - 1, 0))) Assert.Equal(0.0, mna.Get(n - 1, 0));   // an entry never added reads 0

                // a resistor ladder from a 1 A source at node 0: 1 Ω between neighbours, 1 Ω to ground at the far end
                mna.Resize(n);
                mna.Clear();
                for (int i = 0; i + 1 < n; i++) mna.AddConductance(i, i + 1, 1.0);
                mna.AddConductance(n - 1, -1, 1.0);
                mna.AddRhs(0, 1.0);
                var x = new double[n];
                Assert.True(mna.Solve(x, out _));
                for (int i = 0; i < n; i += Math.Max(1, n / 7)) Assert.Equal(n - i, x[i], 6);   // V(i) = 1 A × (n − i) Ω
            }
        }

        [Fact]
        public void AThreeThousandNodeCircuitStartsAndStepsInMoments()
        {
            // an RC ladder of 3000 sections on a 5 V source: one connected circuit, too big for the old dense index
            var c = new AnalogCircuit();
            int prev = c.Node("IN");
            c.Add(new RobotTwin.CoreSim.Analog.Devices.VoltageSource("V1", prev, 0, 5.0));
            for (int i = 0; i < 3000; i++)
            {
                int next = c.Node("N" + i);
                c.Add(new RobotTwin.CoreSim.Analog.Devices.Resistor("R" + i, prev, next, 10));
                c.Add(new RobotTwin.CoreSim.Analog.Devices.Capacitor("C" + i, next, 0, 1e-9));
                prev = next;
            }

            c.Add(new RobotTwin.CoreSim.Analog.Devices.Resistor("RL", prev, 0, 30000));
            var watch = Stopwatch.StartNew();
            var sim = new AnalogSimulator(c);
            sim.Initialize(InitialState.OperatingPoint);
            for (int k = 0; k < 20; k++) sim.Step(50e-6);
            Assert.True(watch.Elapsed.TotalSeconds < 20, $"{watch.Elapsed.TotalSeconds:F1} s");
            // 30 kΩ of ladder over a 30 kΩ load (gmin's 3000 × 1 pS to ground takes some 0.1 mV)
            Assert.Equal(2.5, sim.NodeVoltage("N2999"), 3);
            Assert.Equal(5.0 * (1 - 10.0 / 60000), sim.NodeVoltage("N0"), 3);
        }

        [Fact]
        public void ABigSparseCircuitIsAnalysedInAFractionOfTheDenseTime()
        {
            var rng = new Random(7);
            // 25 robots' worth: 25 independent blocks of 30 nodes and 4 sources, as one matrix
            var rows = new List<int>();
            var cols = new List<int>();
            var values = new List<double>();
            int n = 0;
            for (int block = 0; block < 25; block++)
            {
                var (bn, r, c, v) = Matrix(rng, 30, 4, 70);
                for (int e = 0; e < r.Count; e++)
                {
                    rows.Add(r[e] + n);
                    cols.Add(c[e] + n);
                    values.Add(v[e]);
                }

                n += bn;
            }

            var sparse = new SparseLu();
            var dense = new SparseLu();
            var watch = Stopwatch.StartNew();
            Assert.True(sparse.Analyze(n, rows.Count, rows.ToArray(), cols.ToArray(), values.ToArray()));
            double sparseMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            Assert.True(dense.AnalyzeDense(n, rows.Count, rows.ToArray(), cols.ToArray(), values.ToArray()));
            double denseMs = watch.Elapsed.TotalMilliseconds;
            Assert.Equal(dense.PivotRows, sparse.PivotRows);
            Assert.Equal(dense.PivotColumns, sparse.PivotColumns);
            Assert.True(sparseMs * 5 < denseMs, $"sparse {sparseMs:F1} ms, dense {denseMs:F1} ms for {n} unknowns");
        }
    }
}
