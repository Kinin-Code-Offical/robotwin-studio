using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>
    /// Modified-nodal-analysis system A*x = b. Entries are kept sparse: the
    /// pattern is learned from the stamps after <see cref="Resize"/> (it only
    /// grows), and <see cref="Solve"/> uses a sparse LU (<see cref="SparseLu"/>)
    /// whose pivot order is reused across Newton iterations and time steps. When
    /// the pattern grows or a reused pivot turns bad the order is analysed again;
    /// a matrix the sparse path cannot factor goes to the original dense LU with
    /// partial pivoting, so singular-matrix detection behaves exactly as before.
    /// </summary>
    public sealed class MnaSystem
    {
        // Assembled matrix in coordinate form: entry e is (_rows[e], _cols[e]) with value _vals[e].
        // [bigscene] Where each (row, col) is kept: a dense size × size table up to DenseIndexLimit unknowns, a hash beyond
        // (the table alone was 1.6 GB at 20 000 unknowns, and row × size overflowed past 46 340).
        private const int DenseIndexLimit = 1024;
        private bool _dense = true;
        private int[] _slotOf = Array.Empty<int>();
        private long[] _hashKey = Array.Empty<long>();
        private int[] _hashSlot = Array.Empty<int>();
        private int _hashShift;
        private int[] _rows = Array.Empty<int>();
        private int[] _cols = Array.Empty<int>();
        private double[] _vals = Array.Empty<double>();
        private int _nnz;
        private bool _patternChanged;
        private int[] _diagSlot = Array.Empty<int>();

        private double[] _b = Array.Empty<double>();
        private readonly SparseLu _sparse = new SparseLu();

        // Dense fallback (allocated on first use).
        private double[] _lu = Array.Empty<double>();
        private double[] _work = Array.Empty<double>();
        private int[] _perm = Array.Empty<int>();

        private int _size;

        public int Size => _size;

        /// <summary>
        /// [split] When the circuit is solved in parts: each device's additions are checked to fall inside this part and
        /// taken to its own numbering (<see cref="UnknownWatch"/>); null when this system holds the whole circuit.
        /// </summary>
        internal UnknownWatch? Watch { get; set; }

        /// <summary>Always use the dense LU (reference path for tests and diagnostics).</summary>
        internal bool UseDenseSolver { get; set; }

        /// <summary>Number of times the sparse pivot order was (re)built.</summary>
        internal int Analyses { get; private set; }

        /// <summary>Number of solves that went to the dense LU.</summary>
        internal int DenseSolves { get; private set; }

        /// <summary>Number of stored (structurally nonzero) matrix entries.</summary>
        internal int NonZeroCount => _nnz;

        public void Resize(int size)
        {
            if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
            _size = size;
            _dense = size <= DenseIndexLimit;
            if (_dense)
            {
                if (_slotOf.Length < size * size) _slotOf = new int[size * size];
                for (int i = 0; i < size * size; i++) _slotOf[i] = -1;
            }
            else
            {
                int bits = 6;
                while ((1 << bits) < 8 * size) bits++;
                ResetHash(bits);
            }
            if (_diagSlot.Length < size) _diagSlot = new int[size];
            for (int i = 0; i < size; i++) _diagSlot[i] = -1;
            _nnz = 0;
            _patternChanged = true;
            _sparse.Invalidate();
            if (_vals.Length < 4 * size + 16)
            {
                int capacity = 4 * size + 16;
                _rows = new int[capacity];
                _cols = new int[capacity];
                _vals = new double[capacity];
            }

            if (_b.Length < size) _b = new double[size];
            Array.Clear(_b, 0, size);
        }

        public void Clear()
        {
            Array.Clear(_vals, 0, _nnz);
            Array.Clear(_b, 0, _size);
        }

        public void Add(int row, int col, double value)
        {
            if (row < 0 || col < 0) return;
            if (Watch != null && !Watch.Map(ref row, ref col)) return;
            int slot = _dense ? _slotOf[row * _size + col] : Find(row, col);
            if (slot < 0) slot = NewSlot(row, col);
            _vals[slot] += value;
        }

        /// <summary>
        /// Conductance stamp between unknowns <paramref name="ra"/> and <paramref name="rb"/>
        /// (negative = ground); same additions, in the same order, as four <see cref="Add"/> calls.
        /// </summary>
        internal void AddConductance(int ra, int rb, double g)
        {
            if (Watch != null && !Watch.MapPair(ref ra, ref rb)) return;
            int slot;
            if (ra >= 0)
            {
                slot = _diagSlot[ra];
                if (slot < 0) slot = NewSlot(ra, ra);
                _vals[slot] += g;
            }

            if (rb >= 0)
            {
                slot = _diagSlot[rb];
                if (slot < 0) slot = NewSlot(rb, rb);
                _vals[slot] += g;
            }

            if (ra >= 0 && rb >= 0)
            {
                slot = Slot(ra, rb);
                if (slot < 0) slot = NewSlot(ra, rb);
                _vals[slot] -= g;
                slot = Slot(rb, ra);
                if (slot < 0) slot = NewSlot(rb, ra);
                _vals[slot] -= g;
            }
        }

        private int Slot(int row, int col) => _dense ? _slotOf[row * _size + col] : Find(row, col);

        private int Find(int row, int col)
        {
            long key = (long)row * _size + col;
            int mask = _hashKey.Length - 1;
            for (int h = Hash(key); ; h = (h + 1) & mask)
            {
                long k = _hashKey[h];
                if (k == key) return _hashSlot[h];
                if (k < 0) return -1;
            }
        }

        private int Hash(long key) => (int)((ulong)(key * unchecked((long)0x9E3779B97F4A7C15UL)) >> _hashShift);

        private void ResetHash(int bits)
        {
            _hashShift = 64 - bits;
            _hashKey = new long[1 << bits];
            _hashSlot = new int[1 << bits];
            for (int i = 0; i < _hashKey.Length; i++) _hashKey[i] = -1;
        }

        private void Remember(int row, int col, int slot)
        {
            if (_dense)
            {
                _slotOf[row * _size + col] = slot;
                return;
            }

            if (2 * (_nnz + 1) > _hashKey.Length)
            {
                // keep it at most half full: everything in again, in a table twice the size
                ResetHash(65 - _hashShift);
                for (int e = 0; e < _nnz; e++)
                    if (e != slot) Insert((long)_rows[e] * _size + _cols[e], e);
            }

            Insert((long)row * _size + col, slot);
        }

        private void Insert(long key, int slot)
        {
            int mask = _hashKey.Length - 1;
            int h = Hash(key);
            while (_hashKey[h] >= 0) h = (h + 1) & mask;
            _hashKey[h] = key;
            _hashSlot[h] = slot;
        }

        /// <summary>Adds <paramref name="value"/> to the first <paramref name="count"/> diagonal entries.</summary>
        internal void AddDiagonal(int count, double value)
        {
            var diag = _diagSlot;
            var vals = _vals;
            for (int i = 0; i < count; i++)
            {
                int slot = diag[i];
                if (slot < 0)
                {
                    slot = NewSlot(i, i);
                    vals = _vals;
                }

                vals[slot] += value;
            }
        }

        public void AddRhs(int row, double value)
        {
            if (row < 0) return;
            if (Watch != null && !Watch.Map(ref row)) return;
            _b[row] += value;
        }

        /// <summary>[split] An addition of the simulator's own (a reference tie, a shunt), already in this system's numbering.</summary>
        internal void AddOwn(int row, int col, double value)
        {
            int slot = Slot(row, col);
            if (slot < 0) slot = NewSlot(row, col);
            _vals[slot] += value;
        }

        /// <summary>[split] A right-hand side of the simulator's own, already in this system's numbering.</summary>
        internal void AddOwnRhs(int row, double value) => _b[row] += value;

        public double Get(int row, int col)
        {
            int slot = Slot(row, col);
            return slot < 0 ? 0.0 : _vals[slot];
        }

        public double GetRhs(int row) => _b[row];

        /// <summary>
        /// [islands] The groups of node unknowns that the stamped matrix joins to one another but not to ground: no
        /// conductance, capacitor or source from any of them to the reference. The test is structural, not by size: any
        /// entry joins two unknowns, and any conductance left over to ground grounds a group, however small it is now (a
        /// junction at 0 V conducts next to nothing at the first iteration, and a lot at the last). A branch equation that
        /// holds a node against ground (V(a) = E) grounds its group too. Each floating group comes with its largest node
        /// diagonal, the scale of a reference that conditions it well. Call it after the devices have stamped, before gmin.
        /// </summary>
        internal List<(List<int> Nodes, double Scale)> FloatingIslands(int nodeCount)
        {
            int n = _size;
            var result = new List<(List<int> Nodes, double Scale)>();
            if (nodeCount <= 0) return result;
            var parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            var rowSum = new double[n];
            var rowMax = new double[n];
            for (int e = 0; e < _nnz; e++)
            {
                double v = _vals[e];
                if (v == 0.0 || double.IsNaN(v)) continue;
                int r = _rows[e], c = _cols[e];
                if (c < nodeCount)
                {
                    rowSum[r] += v;
                    rowMax[r] = Math.Max(rowMax[r], Math.Abs(v));
                }

                if (r == c) continue;
                int a = Find(r), b = Find(c);
                if (a != b) parent[a] = b;
            }

            var grounded = new HashSet<int>();
            for (int r = 0; r < n; r++)
            {
                if (rowMax[r] <= 0.0) continue;
                // Above the round-off of a row whose conductances cancel (g - g summed in another order).
                if (Math.Abs(rowSum[r]) > 1e-13 * rowMax[r]) grounded.Add(Find(r));
            }

            var groups = new Dictionary<int, (List<int> Nodes, double Scale)>();
            for (int r = 0; r < nodeCount; r++)
            {
                int root = Find(r);
                if (grounded.Contains(root)) continue;
                if (!groups.TryGetValue(root, out var g)) g = (new List<int>(), 0.0);
                g.Nodes.Add(r);
                int d = _diagSlot[r];
                double diag = d >= 0 ? Math.Abs(_vals[d]) : 0.0;
                groups[root] = (g.Nodes, Math.Max(g.Scale, diag));
            }

            result.AddRange(groups.Values);
            return result;
        }

        private int NewSlot(int row, int col)
        {
            if (row >= _size || col >= _size) throw new IndexOutOfRangeException();
            if (_nnz == _vals.Length)
            {
                int capacity = Math.Max(16, _vals.Length * 2);
                Array.Resize(ref _rows, capacity);
                Array.Resize(ref _cols, capacity);
                Array.Resize(ref _vals, capacity);
            }

            int slot = _nnz++;
            _rows[slot] = row;
            _cols[slot] = col;
            _vals[slot] = 0.0;
            Remember(row, col, slot);
            if (row == col) _diagSlot[row] = slot;
            _patternChanged = true;
            return slot;
        }

        /// <summary>
        /// Solves the system into <paramref name="x"/>. Returns false and reports
        /// the offending unknown when the matrix is singular.
        /// </summary>
        public bool Solve(double[] x, out int singularIndex)
        {
            int n = Size;
            singularIndex = -1;
            if (n == 0) return true;
            if (UseDenseSolver) return SolveDense(x, out singularIndex);

            bool factored = false;
            if (!_patternChanged && _sparse.IsAnalyzed) factored = _sparse.Refactor(_vals);
            if (!factored)
            {
                Analyses++;
                _patternChanged = false;
                factored = _sparse.Analyze(n, _nnz, _rows, _cols, _vals) && _sparse.Refactor(_vals);
            }

            if (factored && _sparse.Solve(_b, x)) return true;

            // Numerically singular (or not finite) for the sparse path: the dense LU
            // decides, exactly as it always has, and reports the singular unknown.
            _sparse.Invalidate();
            if (n > DenseFallbackLimit) return false;   // [bigscene] too big to hold dense: singular, unknown not named
            return SolveDense(x, out singularIndex);
        }

        /// <summary>[bigscene] The dense LU takes n² doubles: 128 MB at this size, so not beyond.</summary>
        private const int DenseFallbackLimit = 4096;

        private bool SolveDense(double[] x, out int singularIndex)
        {
            int n = Size;
            singularIndex = -1;
            DenseSolves++;
            if (_lu.Length < n * n) _lu = new double[n * n];
            if (_work.Length < n)
            {
                _work = new double[n];
                _perm = new int[n];
            }

            Array.Clear(_lu, 0, n * n);
            for (int e = 0; e < _nnz; e++) _lu[_rows[e] * n + _cols[e]] = _vals[e];
            for (int i = 0; i < n; i++) _perm[i] = i;

            for (int k = 0; k < n; k++)
            {
                int pivotRow = k;
                double pivotAbs = Math.Abs(_lu[k * n + k]);
                for (int r = k + 1; r < n; r++)
                {
                    double candidate = Math.Abs(_lu[r * n + k]);
                    if (candidate > pivotAbs)
                    {
                        pivotAbs = candidate;
                        pivotRow = r;
                    }
                }

                if (!(pivotAbs > 1e-30) || double.IsNaN(pivotAbs) || double.IsInfinity(pivotAbs))
                {
                    singularIndex = k;
                    return false;
                }

                if (pivotRow != k)
                {
                    int rowK = k * n, rowP = pivotRow * n;
                    for (int c = 0; c < n; c++)
                    {
                        double tmp = _lu[rowK + c];
                        _lu[rowK + c] = _lu[rowP + c];
                        _lu[rowP + c] = tmp;
                    }

                    int tp = _perm[k];
                    _perm[k] = _perm[pivotRow];
                    _perm[pivotRow] = tp;
                }

                double pivot = _lu[k * n + k];
                for (int r = k + 1; r < n; r++)
                {
                    int rowR = r * n;
                    double factor = _lu[rowR + k];
                    if (factor == 0.0) continue;
                    factor /= pivot;
                    _lu[rowR + k] = factor;
                    int rowK = k * n;
                    for (int c = k + 1; c < n; c++)
                    {
                        _lu[rowR + c] -= factor * _lu[rowK + c];
                    }
                }
            }

            // Forward substitution (L has unit diagonal).
            for (int i = 0; i < n; i++)
            {
                double sum = _b[_perm[i]];
                int row = i * n;
                for (int c = 0; c < i; c++) sum -= _lu[row + c] * _work[c];
                _work[i] = sum;
            }

            // Back substitution.
            for (int i = n - 1; i >= 0; i--)
            {
                double sum = _work[i];
                int row = i * n;
                for (int c = i + 1; c < n; c++) sum -= _lu[row + c] * x[c];
                double value = sum / _lu[row + i];
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    singularIndex = i;
                    return false;
                }

                x[i] = value;
            }

            return true;
        }
    }
}
