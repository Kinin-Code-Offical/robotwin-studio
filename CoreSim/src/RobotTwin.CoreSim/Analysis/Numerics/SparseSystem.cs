using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Analysis.Numerics
{
    /// <summary>
    /// [analysis] Symmetric sparse matrix in compressed-row form whose pattern comes from a node graph
    /// with <see cref="BlockSize"/> unknowns per node (3 for elasticity, 1 for heat). Assembly adds
    /// dense element blocks; the column of an entry is found by binary search in its row.
    /// </summary>
    public sealed class SparseMatrix
    {
        public SparseMatrix(int size, int[] rowStart, int[] columns)
        {
            Size = size;
            RowStart = rowStart;
            Columns = columns;
            Values = new double[columns.Length];
        }

        public int Size { get; }
        public int[] RowStart { get; }
        public int[] Columns { get; }
        public double[] Values { get; }
        public int NonZeros => Columns.Length;

        /// <summary>Pattern from node adjacency (each node is coupled to itself and its neighbours).</summary>
        public static SparseMatrix FromNodeGraph(int nodes, IReadOnlyList<int[]> elements, int blockSize)
        {
            var adj = new List<int>[nodes];
            for (int i = 0; i < nodes; i++) adj[i] = new List<int> { i };
            foreach (var e in elements)
            {
                for (int a = 0; a < e.Length; a++)
                for (int b = 0; b < e.Length; b++)
                    if (a != b) adj[e[a]].Add(e[b]);
            }

            var rowStart = new int[nodes * blockSize + 1];
            var cols = new List<int>();
            var uniq = new List<int>();
            for (int n = 0; n < nodes; n++)
            {
                uniq.Clear();
                adj[n].Sort();
                int last = -1;
                foreach (int m in adj[n])
                {
                    if (m == last) continue;
                    uniq.Add(m);
                    last = m;
                }

                for (int i = 0; i < blockSize; i++)
                {
                    int row = n * blockSize + i;
                    rowStart[row] = cols.Count;
                    foreach (int m in uniq)
                    for (int j = 0; j < blockSize; j++) cols.Add(m * blockSize + j);
                }
            }

            rowStart[nodes * blockSize] = cols.Count;
            return new SparseMatrix(nodes * blockSize, rowStart, cols.ToArray()) { BlockSize = blockSize };
        }

        public int BlockSize { get; private set; } = 1;

        public int Find(int row, int col)
        {
            int lo = RowStart[row], hi = RowStart[row + 1] - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                int c = Columns[mid];
                if (c == col) return mid;
                if (c < col) lo = mid + 1;
                else hi = mid - 1;
            }

            return -1;
        }

        public void Add(int row, int col, double value)
        {
            int k = Find(row, col);
            if (k < 0) throw new InvalidOperationException($"entry ({row},{col}) is not in the pattern");
            Values[k] += value;
        }

        public double Diagonal(int row)
        {
            int k = Find(row, row);
            return k < 0 ? 0 : Values[k];
        }

        public void Clear() => Array.Clear(Values, 0, Values.Length);

        /// <summary>y = A·x (rows in parallel; each row is summed in a fixed order, so the result is deterministic).</summary>
        public void Multiply(double[] x, double[] y)
        {
            int n = Size;
            // Threads pay off from a few thousand rows (a T10 row has ~80 entries).
            if (n < 3000)
            {
                for (int r = 0; r < n; r++) y[r] = RowDot(r, x);
                return;
            }

            int chunks = Math.Min(Environment.ProcessorCount * 4, (n + 511) / 512);
            int size = (n + chunks - 1) / chunks;
            // [perf] On the solver's work team, which stays awake from one iteration to the next (a CG solve makes
            // thousands of products; the thread pool woke its threads for each one).
            if (Mechanics.WorkTeam.Shared.TryRun(n, size, (from, to) =>
                {
                    for (int r = from; r < to; r++) y[r] = RowDot(r, x);
                }))
                return;
            Parallel.For(0, chunks, chunk =>
            {
                int end = Math.Min(n, (chunk + 1) * size);
                for (int r = chunk * size; r < end; r++) y[r] = RowDot(r, x);
            });
        }

        private double RowDot(int r, double[] x)
        {
            double s = 0;
            int end = RowStart[r + 1];
            for (int k = RowStart[r]; k < end; k++) s += Values[k] * x[Columns[k]];
            return s;
        }
    }

    /// <summary>Outcome of an iterative solve.</summary>
    public readonly struct SolveReport
    {
        public SolveReport(int iterations, double residual, bool converged)
        {
            Iterations = iterations;
            Residual = residual;
            Converged = converged;
        }

        public int Iterations { get; }

        /// <summary>Final ‖r‖/‖b‖.</summary>
        public double Residual { get; }

        public bool Converged { get; }
        public override string ToString() => $"{Iterations} it, |r|/|b| = {Residual:E2}{(Converged ? string.Empty : " (yakınsamadı)")}";
    }

    /// <summary>
    /// [analysis] Preconditioned conjugate gradients for SPD systems with fixed (zero) unknowns.
    /// Preconditioner: block Jacobi (inverse of each node's diagonal block), which handles the
    /// coupling of x, y, z displacements of a node and is fully deterministic.
    /// </summary>
    public static class ConjugateGradient
    {
        public static SolveReport Solve(SparseMatrix a, double[] b, double[] x, bool[]? fixedDof = null, double tolerance = 1e-9, int maxIterations = 0,
            CancellationToken cancel = default)
        {
            int n = a.Size;
            int bs = a.BlockSize;
            if (maxIterations <= 0) maxIterations = Math.Max(1000, 4 * n);
            var pre = BlockInverse(a, fixedDof);
            var r = new double[n];
            var z = new double[n];
            var p = new double[n];
            var q = new double[n];
            if (fixedDof != null)
            {
                for (int i = 0; i < n; i++)
                    if (fixedDof[i]) x[i] = 0;
            }

            a.Multiply(x, q);
            double bnorm = 0;
            for (int i = 0; i < n; i++)
            {
                bool f = fixedDof != null && fixedDof[i];
                r[i] = f ? 0 : b[i] - q[i];
                if (!f) bnorm += b[i] * b[i];
            }

            bnorm = Math.Sqrt(bnorm);
            if (bnorm == 0)
            {
                Array.Clear(x, 0, n);
                return new SolveReport(0, 0, true);
            }

            ApplyBlock(pre, bs, r, z);
            Array.Copy(z, p, n);
            double rz = Dot(r, z);
            double res = Math.Sqrt(Dot(r, r)) / bnorm;
            int it = 0;
            while (res > tolerance && it < maxIterations)
            {
                if ((it & 63) == 0) cancel.ThrowIfCancellationRequested();
                a.Multiply(p, q);
                if (fixedDof != null)
                {
                    for (int i = 0; i < n; i++)
                        if (fixedDof[i]) q[i] = 0;
                }

                double pq = Dot(p, q);
                if (pq <= 0) break;
                double alpha = rz / pq;
                double rr = 0;
                for (int i = 0; i < n; i++)
                {
                    x[i] += alpha * p[i];
                    r[i] -= alpha * q[i];
                    rr += r[i] * r[i];
                }

                res = Math.Sqrt(rr) / bnorm;
                ApplyBlock(pre, bs, r, z);
                double rzNew = Dot(r, z);
                double beta = rzNew / rz;
                rz = rzNew;
                for (int i = 0; i < n; i++) p[i] = z[i] + beta * p[i];
                it++;
            }

            return new SolveReport(it, res, res <= tolerance);
        }

        private static double Dot(double[] a, double[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }

        /// <summary>Inverse of each node's diagonal block (bs × bs), fixed unknowns get 0.</summary>
        private static double[] BlockInverse(SparseMatrix a, bool[]? fixedDof)
        {
            int bs = a.BlockSize;
            int nodes = a.Size / bs;
            var inv = new double[nodes * bs * bs];
            var m = new double[bs * bs];
            for (int nd = 0; nd < nodes; nd++)
            {
                for (int i = 0; i < bs; i++)
                for (int j = 0; j < bs; j++)
                {
                    int ri = nd * bs + i, cj = nd * bs + j;
                    bool fi = fixedDof != null && (fixedDof[ri] || fixedDof[cj]);
                    int k = a.Find(ri, cj);
                    double v = fi ? (i == j ? 1 : 0) : k >= 0 ? a.Values[k] : 0;
                    m[i * bs + j] = v;
                }

                if (!Invert(m, bs))
                {
                    for (int i = 0; i < bs * bs; i++) m[i] = 0;
                    for (int i = 0; i < bs; i++)
                    {
                        double d = a.Diagonal(nd * bs + i);
                        m[i * bs + i] = d != 0 ? 1 / d : 1;
                    }
                }

                for (int i = 0; i < bs; i++)
                {
                    int ri = nd * bs + i;
                    if (fixedDof != null && fixedDof[ri])
                    {
                        for (int j = 0; j < bs; j++)
                        {
                            m[i * bs + j] = 0;
                            m[j * bs + i] = 0;
                        }
                    }
                }

                Array.Copy(m, 0, inv, nd * bs * bs, bs * bs);
            }

            return inv;
        }

        private static void ApplyBlock(double[] inv, int bs, double[] r, double[] z)
        {
            int nodes = r.Length / bs;
            if (bs == 1)
            {
                for (int i = 0; i < r.Length; i++) z[i] = inv[i] * r[i];
                return;
            }

            for (int nd = 0; nd < nodes; nd++)
            {
                int o = nd * bs, mo = nd * bs * bs;
                for (int i = 0; i < bs; i++)
                {
                    double s = 0;
                    for (int j = 0; j < bs; j++) s += inv[mo + i * bs + j] * r[o + j];
                    z[o + i] = s;
                }
            }
        }

        /// <summary>In-place Gauss–Jordan inverse of a small dense matrix; false when singular.</summary>
        public static bool Invert(double[] m, int n)
        {
            var a = new double[n * 2 * n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) a[i * 2 * n + j] = m[i * n + j];
                a[i * 2 * n + n + i] = 1;
            }

            for (int c = 0; c < n; c++)
            {
                int piv = c;
                double best = Math.Abs(a[c * 2 * n + c]);
                for (int r = c + 1; r < n; r++)
                {
                    double v = Math.Abs(a[r * 2 * n + c]);
                    if (v > best)
                    {
                        best = v;
                        piv = r;
                    }
                }

                if (best < 1e-300) return false;
                if (piv != c)
                {
                    for (int j = 0; j < 2 * n; j++)
                    {
                        double t = a[c * 2 * n + j];
                        a[c * 2 * n + j] = a[piv * 2 * n + j];
                        a[piv * 2 * n + j] = t;
                    }
                }

                double d = a[c * 2 * n + c];
                for (int j = 0; j < 2 * n; j++) a[c * 2 * n + j] /= d;
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = a[r * 2 * n + c];
                    if (f == 0) continue;
                    for (int j = 0; j < 2 * n; j++) a[r * 2 * n + j] -= f * a[c * 2 * n + j];
                }
            }

            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                m[i * n + j] = a[i * 2 * n + n + j];
            return true;
        }
    }

    /// <summary>Small dense linear algebra helpers (Gaussian elimination with partial pivoting).</summary>
    public static class Dense
    {
        /// <summary>Solves A·x = b (A is n×n row-major, overwritten); returns false when singular.</summary>
        public static bool Solve(double[] a, double[] b, int n)
        {
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                double best = Math.Abs(a[c * n + c]);
                for (int r = c + 1; r < n; r++)
                {
                    double v = Math.Abs(a[r * n + c]);
                    if (v > best)
                    {
                        best = v;
                        piv = r;
                    }
                }

                if (best < 1e-300) return false;
                if (piv != c)
                {
                    for (int j = 0; j < n; j++)
                    {
                        double t = a[c * n + j];
                        a[c * n + j] = a[piv * n + j];
                        a[piv * n + j] = t;
                    }

                    double tb = b[c];
                    b[c] = b[piv];
                    b[piv] = tb;
                }

                for (int r = c + 1; r < n; r++)
                {
                    double f = a[r * n + c] / a[c * n + c];
                    if (f == 0) continue;
                    for (int j = c; j < n; j++) a[r * n + j] -= f * a[c * n + j];
                    b[r] -= f * b[c];
                }
            }

            for (int r = n - 1; r >= 0; r--)
            {
                double s = b[r];
                for (int j = r + 1; j < n; j++) s -= a[r * n + j] * b[j];
                b[r] = s / a[r * n + r];
            }

            return true;
        }
    }
}
