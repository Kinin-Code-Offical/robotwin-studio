using System;

namespace RobotTwin.CoreSim.Analog
{
    /// <summary>
    /// Sparse LU factorisation for <see cref="MnaSystem"/>.
    /// <para>
    /// <see cref="Analyze"/> picks row and column pivots with Markowitz ordering and
    /// threshold partial pivoting on the current values, and records the fill-in
    /// pattern of L and U. <see cref="Refactor"/> then repeats the numeric
    /// factorisation (left-looking, column by column) with that fixed pivot
    /// sequence for each new set of values, so Newton iterations and time steps
    /// pay only for the nonzeros. A pivot that has become too small relative to
    /// its column makes <see cref="Refactor"/> fail; the caller then analyses
    /// again or falls back to the dense solver.
    /// </para>
    /// </summary>
    internal sealed class SparseLu
    {
        /// <summary>Markowitz threshold: a pivot must be at least this fraction of its column's largest entry.</summary>
        private const double AnalysisThreshold = 0.1;

        /// <summary>Looser threshold accepted when the pivot sequence is reused (SPICE's pivrel).</summary>
        private const double RefactorThreshold = 1e-3;

        /// <summary>Same absolute floor as the dense solver.</summary>
        private const double AbsolutePivot = 1e-30;

        /// <summary>A pivot below this fraction of its original column is cancellation noise: treat as singular.</summary>
        private const double NoisePivot = 1e-15;

        private int _n;

        // Pivot sequence: step k eliminates original row _p[k] and original column _q[k].
        private int[] _p = Array.Empty<int>();
        private int[] _q = Array.Empty<int>();

        // A in permuted coordinates, by column: value slot and permuted row of each entry.
        private int[] _aPtr = Array.Empty<int>();
        private int[] _aSlot = Array.Empty<int>();
        private int[] _aRow = Array.Empty<int>();

        // Strict upper triangle of U by column (rows ascending), unit-lower L by column.
        private int[] _uPtr = Array.Empty<int>();
        private int[] _uRow = Array.Empty<int>();
        private double[] _uVal = Array.Empty<double>();
        private int[] _lPtr = Array.Empty<int>();
        private int[] _lRow = Array.Empty<int>();
        private double[] _lVal = Array.Empty<double>();
        private double[] _diag = Array.Empty<double>();

        private double[] _x = Array.Empty<double>();

        // Analysis work space (dense, only touched when the pivot order is rebuilt).
        private double[] _w = Array.Empty<double>();
        private bool[] _s = Array.Empty<bool>();
        private int[] _rowCount = Array.Empty<int>();
        private int[] _colCount = Array.Empty<int>();
        private int[] _rowRank = Array.Empty<int>();
        private int[] _colRank = Array.Empty<int>();

        public bool IsAnalyzed { get; private set; }

        public void Invalidate() => IsAnalyzed = false;

        /// <summary>
        /// Chooses the pivot sequence for the matrix given as coordinate entries
        /// (<paramref name="rows"/>[e], <paramref name="cols"/>[e], <paramref name="values"/>[e])
        /// and builds the static L/U pattern. Returns false when no acceptable pivot
        /// exists at some step (numerically singular matrix).
        /// </summary>
        public bool Analyze(int n, int nnz, int[] rows, int[] cols, double[] values)
        {
            IsAnalyzed = false;
            _n = n;
            EnsureCapacity(n);
            Array.Clear(_w, 0, n * n);
            Array.Clear(_s, 0, n * n);
            Array.Clear(_rowCount, 0, n);
            Array.Clear(_colCount, 0, n);
            for (int i = 0; i < n; i++)
            {
                _rowRank[i] = -1;
                _colRank[i] = -1;
            }

            for (int e = 0; e < nnz; e++)
            {
                int idx = rows[e] * n + cols[e];
                _w[idx] = values[e];
                if (!_s[idx])
                {
                    _s[idx] = true;
                    _rowCount[rows[e]]++;
                    _colCount[cols[e]]++;
                }
            }

            for (int k = 0; k < n; k++)
            {
                int bestRow = -1, bestCol = -1;
                long bestCost = long.MaxValue;
                double bestRatio = 0.0;
                bool bestDiagonal = false;

                for (int j = 0; j < n; j++)
                {
                    if (_colRank[j] >= 0) continue;
                    double colMax = 0.0;
                    for (int i = 0; i < n; i++)
                    {
                        if (_rowRank[i] >= 0 || !_s[i * n + j]) continue;
                        double a = Math.Abs(_w[i * n + j]);
                        if (a > colMax) colMax = a;
                    }

                    if (!(colMax > AbsolutePivot) || double.IsInfinity(colMax)) return false;

                    double limit = AnalysisThreshold * colMax;
                    long colCost = _colCount[j] - 1;
                    for (int i = 0; i < n; i++)
                    {
                        if (_rowRank[i] >= 0 || !_s[i * n + j]) continue;
                        double a = Math.Abs(_w[i * n + j]);
                        if (a < limit || !(a > AbsolutePivot)) continue;
                        long cost = (_rowCount[i] - 1) * colCost;
                        double ratio = a / colMax;
                        bool diagonal = i == j;
                        bool better = cost < bestCost ||
                                      (cost == bestCost && diagonal && !bestDiagonal) ||
                                      (cost == bestCost && diagonal == bestDiagonal && ratio > bestRatio);
                        if (!better) continue;
                        bestCost = cost;
                        bestRow = i;
                        bestCol = j;
                        bestRatio = ratio;
                        bestDiagonal = diagonal;
                    }
                }

                if (bestRow < 0) return false;

                int p = bestRow, q = bestCol;
                _p[k] = p;
                _q[k] = q;
                _rowRank[p] = k;
                _colRank[q] = k;

                // The pivot row and column leave the active submatrix.
                for (int j = 0; j < n; j++)
                {
                    if (_colRank[j] < 0 && _s[p * n + j]) _colCount[j]--;
                }

                for (int i = 0; i < n; i++)
                {
                    if (_rowRank[i] < 0 && _s[i * n + q]) _rowCount[i]--;
                }

                // Right-looking elimination of the active submatrix (values and fill-in).
                double pivot = _w[p * n + q];
                for (int i = 0; i < n; i++)
                {
                    if (_rowRank[i] >= 0 || !_s[i * n + q]) continue;
                    double factor = _w[i * n + q] / pivot;
                    for (int j = 0; j < n; j++)
                    {
                        if (_colRank[j] >= 0 || !_s[p * n + j]) continue;
                        int idx = i * n + j;
                        _w[idx] -= factor * _w[p * n + j];
                        if (!_s[idx])
                        {
                            _s[idx] = true;
                            _rowCount[i]++;
                            _colCount[j]++;
                        }
                    }
                }
            }

            BuildPattern(n, nnz, rows, cols);
            IsAnalyzed = true;
            return true;
        }

        private void BuildPattern(int n, int nnz, int[] rows, int[] cols)
        {
            // Permuted A by column.
            Array.Clear(_aPtr, 0, n + 1);
            for (int e = 0; e < nnz; e++) _aPtr[_colRank[cols[e]] + 1]++;
            for (int c = 0; c < n; c++) _aPtr[c + 1] += _aPtr[c];
            if (_aSlot.Length < nnz)
            {
                _aSlot = new int[nnz];
                _aRow = new int[nnz];
            }

            var fill = _rowCount; // reuse as insertion cursor
            for (int c = 0; c < n; c++) fill[c] = _aPtr[c];
            for (int e = 0; e < nnz; e++)
            {
                int c = _colRank[cols[e]];
                int at = fill[c]++;
                _aSlot[at] = e;
                _aRow[at] = _rowRank[rows[e]];
            }

            // L and U pattern of the permuted matrix, read off the elimination's structure.
            int uCount = 0, lCount = 0;
            for (int c = 0; c < n; c++)
            {
                int q = _q[c];
                for (int r = 0; r < n; r++)
                {
                    if (r == c || !_s[_p[r] * n + q]) continue;
                    if (r < c) uCount++;
                    else lCount++;
                }
            }

            if (_uRow.Length < uCount)
            {
                _uRow = new int[uCount];
                _uVal = new double[uCount];
            }

            if (_lRow.Length < lCount)
            {
                _lRow = new int[lCount];
                _lVal = new double[lCount];
            }

            int u = 0, l = 0;
            for (int c = 0; c < n; c++)
            {
                _uPtr[c] = u;
                _lPtr[c] = l;
                int q = _q[c];
                for (int r = 0; r < n; r++)
                {
                    if (r == c || !_s[_p[r] * n + q]) continue;
                    if (r < c) _uRow[u++] = r;
                    else _lRow[l++] = r;
                }
            }

            _uPtr[n] = u;
            _lPtr[n] = l;
        }

        /// <summary>
        /// Numeric factorisation with the analysed pivot sequence. <paramref name="values"/>
        /// is indexed by the entry numbers given to <see cref="Analyze"/>. Returns false
        /// when a pivot fails the threshold test (re-analysis needed) or is singular.
        /// </summary>
        public bool Refactor(double[] values)
        {
            int n = _n;
            var x = _x;
            var aPtr = _aPtr;
            var aSlot = _aSlot;
            var aRow = _aRow;
            var uPtr = _uPtr;
            var uRow = _uRow;
            var uVal = _uVal;
            var lPtr = _lPtr;
            var lRow = _lRow;
            var lVal = _lVal;

            for (int c = 0; c < n; c++)
            {
                double scale = 0.0;
                for (int e = aPtr[c], end = aPtr[c + 1]; e < end; e++)
                {
                    double v = values[aSlot[e]];
                    x[aRow[e]] += v;
                    double av = Math.Abs(v);
                    if (av > scale) scale = av;
                }

                int uStart = uPtr[c], uEnd = uPtr[c + 1];
                for (int e = uStart; e < uEnd; e++)
                {
                    int r = uRow[e];
                    double ur = x[r];
                    if (ur == 0.0) continue;
                    for (int f = lPtr[r], fEnd = lPtr[r + 1]; f < fEnd; f++)
                    {
                        x[lRow[f]] -= lVal[f] * ur;
                    }
                }

                for (int e = uStart; e < uEnd; e++)
                {
                    int r = uRow[e];
                    uVal[e] = x[r];
                    x[r] = 0.0;
                }

                double pivot = x[c];
                x[c] = 0.0;
                int lStart = lPtr[c], lEnd = lPtr[c + 1];
                double colMax = 0.0;
                for (int f = lStart; f < lEnd; f++)
                {
                    double a = Math.Abs(x[lRow[f]]);
                    if (a > colMax) colMax = a;
                }

                double pivotAbs = Math.Abs(pivot);
                if (!(pivotAbs > AbsolutePivot) || double.IsInfinity(pivotAbs) || double.IsInfinity(colMax) ||
                    pivotAbs < RefactorThreshold * colMax || pivotAbs < NoisePivot * scale)
                {
                    for (int f = lStart; f < lEnd; f++) x[lRow[f]] = 0.0;
                    for (int r = c + 1; r < n; r++) x[r] = 0.0;
                    return false;
                }

                _diag[c] = pivot;
                double inv = 1.0 / pivot;
                for (int f = lStart; f < lEnd; f++)
                {
                    int r = lRow[f];
                    lVal[f] = x[r] * inv;
                    x[r] = 0.0;
                }
            }

            return true;
        }

        /// <summary>Solves with the current factors. Returns false if the result is not finite.</summary>
        public bool Solve(double[] b, double[] result)
        {
            int n = _n;
            var x = _x;
            var p = _p;
            for (int k = 0; k < n; k++) x[k] = b[p[k]];

            var lPtr = _lPtr;
            var lRow = _lRow;
            var lVal = _lVal;
            for (int k = 0; k < n; k++)
            {
                double xk = x[k];
                if (xk == 0.0) continue;
                for (int f = lPtr[k], end = lPtr[k + 1]; f < end; f++) x[lRow[f]] -= lVal[f] * xk;
            }

            var uPtr = _uPtr;
            var uRow = _uRow;
            var uVal = _uVal;
            var diag = _diag;
            bool finite = true;
            for (int k = n - 1; k >= 0; k--)
            {
                double xk = x[k] / diag[k];
                if (double.IsNaN(xk) || double.IsInfinity(xk)) finite = false;
                x[k] = xk;
                if (xk == 0.0) continue;
                for (int e = uPtr[k], end = uPtr[k + 1]; e < end; e++) x[uRow[e]] -= uVal[e] * xk;
            }

            var q = _q;
            for (int k = 0; k < n; k++)
            {
                result[q[k]] = x[k];
                x[k] = 0.0;
            }

            return finite;
        }

        private void EnsureCapacity(int n)
        {
            if (_p.Length < n)
            {
                _p = new int[n];
                _q = new int[n];
                _diag = new double[n];
                _x = new double[n];
                _rowCount = new int[n];
                _colCount = new int[n];
                _rowRank = new int[n];
                _colRank = new int[n];
                _aPtr = new int[n + 1];
                _uPtr = new int[n + 1];
                _lPtr = new int[n + 1];
            }

            if (_w.Length < n * n)
            {
                _w = new double[n * n];
                _s = new bool[n * n];
            }
        }
    }
}
