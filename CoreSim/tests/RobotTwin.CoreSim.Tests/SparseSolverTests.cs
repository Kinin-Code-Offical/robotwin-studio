using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>The sparse LU inside <see cref="MnaSystem"/> against the original dense LU.</summary>
    [Trait("Category", "Analog")]
    public class SparseSolverTests
    {
        private readonly struct Entry
        {
            public Entry(int row, int col, double value)
            {
                Row = row;
                Col = col;
                Value = value;
            }

            public int Row { get; }
            public int Col { get; }
            public double Value { get; }
        }

        private static (MnaSystem Sparse, MnaSystem Dense) Pair(int n)
        {
            var sparse = new MnaSystem();
            var dense = new MnaSystem { UseDenseSolver = true };
            sparse.Resize(n);
            dense.Resize(n);
            return (sparse, dense);
        }

        private static void Load(MnaSystem mna, IEnumerable<Entry> entries, double[] rhs)
        {
            mna.Clear();
            foreach (var e in entries) mna.Add(e.Row, e.Col, e.Value);
            for (int i = 0; i < rhs.Length; i++) mna.AddRhs(i, rhs[i]);
        }

        private static void AssertSameSolution(MnaSystem sparse, MnaSystem dense, string context)
        {
            int n = sparse.Size;
            var xs = new double[n];
            var xd = new double[n];
            Assert.True(dense.Solve(xd, out _), "dense solve failed: " + context);
            Assert.True(sparse.Solve(xs, out _), "sparse solve failed: " + context);
            double scale = xd.Max(Math.Abs);
            double worst = 0.0;
            for (int i = 0; i < n; i++) worst = Math.Max(worst, Math.Abs(xs[i] - xd[i]));
            Assert.True(worst <= 1e-9 * scale, $"{context}: max |sparse - dense| = {worst:E3} (scale {scale:E3})");
        }

        private static List<Entry> RandomDiagonallyDominant(Random rng, int n, double density)
        {
            var entries = new List<Entry>();
            var rowSum = new double[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j || rng.NextDouble() >= density) continue;
                    double v = (rng.NextDouble() * 2.0 - 1.0) * Math.Pow(10.0, rng.Next(-3, 4));
                    entries.Add(new Entry(i, j, v));
                    rowSum[i] += Math.Abs(v);
                }
            }

            for (int i = 0; i < n; i++)
            {
                double sign = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
                entries.Add(new Entry(i, i, sign * (rowSum[i] * (1.0 + rng.NextDouble()) + 1e-3)));
            }

            // Shuffle so entries (and duplicates) arrive in arbitrary order.
            return entries.OrderBy(_ => rng.Next()).ToList();
        }

        private static double[] RandomRhs(Random rng, int n) =>
            Enumerable.Range(0, n).Select(_ => rng.NextDouble() * 20.0 - 10.0).ToArray();

        [Fact]
        public void RandomDiagonallyDominantSystemsMatchDense()
        {
            var rng = new Random(12345);
            foreach (int n in new[] { 1, 2, 3, 5, 8, 13, 25, 40, 75, 150 })
            {
                foreach (double density in new[] { 0.02, 0.1, 0.3 })
                {
                    var (sparse, dense) = Pair(n);
                    var entries = RandomDiagonallyDominant(rng, n, density);
                    for (int round = 0; round < 4; round++)
                    {
                        // Same pattern, new values: exercises the reused pivot order.
                        var scaled = entries.Select(e => new Entry(e.Row, e.Col, e.Value * (0.5 + rng.NextDouble()))).ToList();
                        var rhs = RandomRhs(rng, n);
                        Load(sparse, scaled, rhs);
                        Load(dense, scaled, rhs);
                        AssertSameSolution(sparse, dense, $"n={n} density={density} round={round}");
                    }

                    Assert.Equal(0, sparse.DenseSolves);
                }
            }
        }

        /// <summary>
        /// Random resistor networks with grounded and floating voltage sources and
        /// current sources, stamped as MNA does: the branch rows have zero diagonals.
        /// </summary>
        private static List<Entry> RandomMna(Random rng, int nodes, int sources, double gmin, out int size)
        {
            var entries = new List<Entry>();
            void Conductance(int a, int b, double g)
            {
                if (a >= 0) entries.Add(new Entry(a, a, g));
                if (b >= 0) entries.Add(new Entry(b, b, g));
                if (a >= 0 && b >= 0)
                {
                    entries.Add(new Entry(a, b, -g));
                    entries.Add(new Entry(b, a, -g));
                }
            }

            // A random spanning tree to ground keeps every node referenced, plus extra links.
            for (int i = 0; i < nodes; i++)
            {
                int parent = rng.Next(-1, i);
                Conductance(i, parent, Math.Pow(10.0, rng.NextDouble() * 6.0 - 4.0));
            }

            for (int k = 0; k < nodes; k++)
            {
                int a = rng.Next(-1, nodes), b = rng.Next(-1, nodes);
                if (a != b) Conductance(a, b, Math.Pow(10.0, rng.NextDouble() * 8.0 - 6.0));
            }

            for (int i = 0; i < nodes; i++) entries.Add(new Entry(i, i, gmin));

            // Voltage sources between distinct nodes (or to ground) along a forest, so no source loops.
            var used = new HashSet<int>();
            size = nodes;
            for (int s = 0; s < sources; s++)
            {
                int plus = rng.Next(0, nodes);
                if (!used.Add(plus)) continue;
                int minus = rng.NextDouble() < 0.5 ? -1 : rng.Next(0, nodes);
                if (minus == plus || (minus >= 0 && used.Contains(minus))) minus = -1;
                int branch = size++;
                entries.Add(new Entry(plus, branch, 1.0));
                entries.Add(new Entry(branch, plus, 1.0));
                if (minus >= 0)
                {
                    entries.Add(new Entry(minus, branch, -1.0));
                    entries.Add(new Entry(branch, minus, -1.0));
                }
            }

            return entries;
        }

        [Fact]
        public void RandomMnaSystemsWithVoltageSourceBranchesMatchDense()
        {
            var rng = new Random(2024);
            for (int trial = 0; trial < 60; trial++)
            {
                int nodes = rng.Next(2, 120);
                var entries = RandomMna(rng, nodes, rng.Next(1, Math.Max(2, nodes / 4)), trial % 3 == 0 ? 0.0 : 1e-12, out int size);
                var (sparse, dense) = Pair(size);
                for (int round = 0; round < 3; round++)
                {
                    var rhs = RandomRhs(rng, size);
                    // Nonlinear-like changes: conductances move, the +-1 incidence entries stay exact.
                    var values = entries.Select(e => Math.Abs(Math.Abs(e.Value) - 1.0) < 1e-15 ? e : new Entry(e.Row, e.Col, e.Value * (0.2 + 2.0 * rng.NextDouble()))).ToList();
                    Load(sparse, values, rhs);
                    Load(dense, values, rhs);
                    AssertSameSolution(sparse, dense, $"trial={trial} nodes={nodes} size={size} round={round}");
                }

                Assert.Equal(0, sparse.DenseSolves);
            }
        }

        [Fact]
        public void PatternThatGrowsBetweenSolvesIsReanalysed()
        {
            // A pin that alternates between driving towards the supply and towards ground
            // stamps different entries; the learned pattern must grow, not go stale.
            var (sparse, dense) = Pair(3);
            var rhs = new[] { 0.0, 0.0, 0.0 };
            var common = new List<Entry>
            {
                new Entry(0, 2, 1.0), new Entry(2, 0, 1.0), // 5 V source on node 0 (branch 2)
                new Entry(1, 1, 1e-3)                        // load on node 1
            };
            rhs[2] = 5.0;
            var high = common.Concat(new[] { new Entry(0, 0, 0.04), new Entry(1, 1, 0.04), new Entry(0, 1, -0.04), new Entry(1, 0, -0.04) }).ToList();
            var low = common.Concat(new[] { new Entry(1, 1, 0.04) }).ToList();
            for (int i = 0; i < 6; i++)
            {
                var entries = i % 2 == 0 ? low : high;
                Load(sparse, entries, rhs);
                Load(dense, entries, rhs);
                AssertSameSolution(sparse, dense, $"switch {i}");
            }

            Assert.True(sparse.NonZeroCount >= 6);
            Assert.True(sparse.Analyses >= 2);
            Assert.True(sparse.Analyses <= 3, $"pattern should settle, analysed {sparse.Analyses} times");
        }

        [Fact]
        public void VoltageSourceLoopIsSingularAtTheSameUnknownAsDense()
        {
            // Node 0 driven by two sources (branches 1 and 2): structurally singular.
            var entries = new List<Entry>
            {
                new Entry(0, 0, 1e-12),
                new Entry(0, 1, 1.0), new Entry(1, 0, 1.0),
                new Entry(0, 2, 1.0), new Entry(2, 0, 1.0)
            };
            var rhs = new[] { 0.0, 5.0, 3.0 };
            var (sparse, dense) = Pair(3);
            Load(sparse, entries, rhs);
            Load(dense, entries, rhs);
            var x = new double[3];
            Assert.False(dense.Solve(x, out int denseIndex));
            Assert.False(sparse.Solve(x, out int sparseIndex));
            Assert.Equal(denseIndex, sparseIndex);
            Assert.Equal(2, sparseIndex);

            // The same system keeps failing on later Newton iterations, and a healthy
            // system afterwards goes back to the sparse path.
            Load(sparse, entries, rhs);
            Assert.False(sparse.Solve(x, out sparseIndex));
            Assert.Equal(2, sparseIndex);
            var healthy = entries.Where(e => e.Row != 2 && e.Col != 2).Append(new Entry(2, 2, 1.0)).ToList();
            Load(sparse, healthy, rhs);
            Load(dense, healthy, rhs);
            int denseSolves = sparse.DenseSolves;
            AssertSameSolution(sparse, dense, "after singular");
            Assert.Equal(denseSolves, sparse.DenseSolves);
        }

        [Fact]
        public void FloatingNodeIsSingularUntilShuntedLikeTheSimulatorDoes()
        {
            // Nodes 1 and 2 form an island (resistor between them, no gmin): singular.
            // The simulator then adds growing shunts on every node diagonal and retries.
            var entries = new List<Entry>
            {
                new Entry(0, 0, 1e-3),
                new Entry(1, 1, 1e-3), new Entry(2, 2, 1e-3), new Entry(1, 2, -1e-3), new Entry(2, 1, -1e-3)
            };
            var rhs = new[] { 1e-3, 0.0, 0.0 };
            var (sparse, dense) = Pair(3);
            Load(sparse, entries, rhs);
            Load(dense, entries, rhs);
            var xs = new double[3];
            var xd = new double[3];
            Assert.False(dense.Solve(xd, out int denseIndex));
            Assert.False(sparse.Solve(xs, out int sparseIndex));
            Assert.Equal(denseIndex, sparseIndex);

            sparse.AddDiagonal(3, 1e-9);
            dense.AddDiagonal(3, 1e-9);
            AssertSameSolution(sparse, dense, "shunted island");
        }

        [Fact]
        public void GetAndAddDiagonalSeeTheAssembledValues()
        {
            var mna = new MnaSystem();
            mna.Resize(3);
            mna.Add(0, 1, 2.0);
            mna.Add(0, 1, 0.5);
            mna.AddDiagonal(2, 1.0);
            mna.AddConductance(0, -1, 3.0);
            mna.AddRhs(2, 4.0);
            Assert.Equal(2.5, mna.Get(0, 1));
            Assert.Equal(4.0, mna.Get(0, 0));
            Assert.Equal(1.0, mna.Get(1, 1));
            Assert.Equal(0.0, mna.Get(2, 2));
            Assert.Equal(0.0, mna.Get(1, 0));
            Assert.Equal(4.0, mna.GetRhs(2));
            mna.Clear();
            Assert.Equal(0.0, mna.Get(0, 1));
            Assert.Equal(0.0, mna.GetRhs(2));
        }

        private static AnalogSimulator Simulate(Func<AnalogCircuit> build, bool dense, int steps, double dt, Action<AnalogSimulator>? each = null)
        {
            var sim = new AnalogSimulator(build());
            var field = typeof(AnalogSimulator).GetField("_mna", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field);
            ((MnaSystem)field!.GetValue(sim)!).UseDenseSolver = dense;
            sim.Initialize(InitialState.OperatingPoint);
            for (int i = 0; i < steps; i++)
            {
                sim.Step(dt);
                each?.Invoke(sim);
            }

            return sim;
        }

        [Fact]
        public void TransientSimulationMatchesDenseSolver()
        {
            AnalogCircuit Build()
            {
                var c = new AnalogCircuit();
                int vcc = c.Node("vcc"), a = c.Node("a"), b = c.Node("b"), g = c.Node("g"), d = c.Node("d");
                c.Add(new VoltageSource("V1", vcc, 0, 5.0));
                c.Add(new Resistor("R1", vcc, a, 330));
                c.Add(new Led("D1", a, b, 2.1));
                c.Add(new Capacitor("C1", b, 0, 10e-6));
                c.Add(new Resistor("R2", b, 0, 1000));
                c.Add(new Resistor("RG", vcc, g, 10e3));
                c.Add(new Resistor("RG2", g, 0, 10e3));
                c.Add(new Resistor("RD", vcc, d, 100));
                c.Add(new Inductor("L1", d, 0, 1e-3) { SeriesResistance = 5.0 });
                return c;
            }

            var sparse = Simulate(Build, dense: false, steps: 200, dt: 1e-4);
            var dense = Simulate(Build, dense: true, steps: 200, dt: 1e-4);
            foreach (var node in new[] { "vcc", "a", "b", "g", "d" })
            {
                double s = sparse.NodeVoltage(node), r = dense.NodeVoltage(node);
                Assert.True(Math.Abs(s - r) <= 1e-9 * Math.Max(1.0, Math.Abs(r)), $"{node}: sparse {s:R} dense {r:R}");
            }
        }

        [Fact]
        public void FloatingIslandRecoveryBehavesAsWithDenseSolver()
        {
            AnalogCircuit Build()
            {
                var c = new AnalogCircuit();
                int vcc = c.Node("vcc"), x = c.Node("x"), y = c.Node("y");
                c.Add(new VoltageSource("V1", vcc, 0, 5.0));
                c.Add(new Resistor("R1", vcc, 0, 100));
                c.Add(new Resistor("RF", x, y, 1000)); // goes nowhere
                return c;
            }

            AnalogSimulator Run(bool dense)
            {
                var sim = new AnalogSimulator(Build(), new AnalogOptions { Gmin = 0.0 });
                ((MnaSystem)typeof(AnalogSimulator).GetField("_mna", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sim)!).UseDenseSolver = dense;
                sim.Initialize(InitialState.OperatingPoint);
                for (int i = 0; i < 5; i++) sim.Step(1e-4);
                return sim;
            }

            var sparse = Run(false);
            var dense = Run(true);
            // [islands] The resistor going nowhere is its own island and gets its reference before the solve: no singular
            // matrix to recover from, and the two solvers still agree.
            Assert.DoesNotContain(sparse.Events, e => e.Kind == AnalogEventKind.Warning && e.DeviceId == "solver");
            Assert.Equal(dense.Events.Select(e => e.ToString()), sparse.Events.Select(e => e.ToString()));
            foreach (var node in new[] { "vcc", "x", "y" })
            {
                Assert.Equal(dense.NodeVoltage(node), sparse.NodeVoltage(node), 9);
            }
        }
    }
}
