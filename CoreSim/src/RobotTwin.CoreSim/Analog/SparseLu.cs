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

        // [bigscene] Analysis work space: the active submatrix as a pool of entries listed by row and by column (only
        // touched when the pivot order is rebuilt). It was a dense n × n array searched whole at every pivot: n³ work, half
        // a second for a world of 25 robots (850 unknowns) and minutes for thousands.
        private int[] _eRow = Array.Empty<int>();
        private int[] _eCol = Array.Empty<int>();
        private double[] _eVal = Array.Empty<double>();
        private int _entries;
        private int[][] _byRow = Array.Empty<int[]>();
        private int[][] _byCol = Array.Empty<int[]>();
        private int[] _rowLen = Array.Empty<int>();
        private int[] _colLen = Array.Empty<int>();
        private int[] _mark = Array.Empty<int>();

        // The dense reference analysis (tests compare the sparse one against it).
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
            EnsureAnalysis(n, nnz);
            _entries = 0;
            for (int i = 0; i < n; i++)
            {
                _rowLen[i] = 0;
                _colLen[i] = 0;
                _rowCount[i] = 0;
                _colCount[i] = 0;
                _rowRank[i] = -1;
                _colRank[i] = -1;
                _mark[i] = -1;
            }

            for (int e = 0; e < nnz; e++) NewEntry(rows[e], cols[e], values[e]);   // MnaSystem gives each (row, col) once

            var pivotCols = new int[16];
            var pivotVals = new double[16];
            for (int k = 0; k < n; k++)
            {
                // The same choice as the dense search (AnalyzeDense): columns in order, the lowest Markowitz cost among
                // entries within the threshold of their column, a diagonal before others, then the largest ratio; equal
                // candidates go to the lowest column, then the lowest row (the dense scan's first).
                int bestRow = -1, bestCol = -1;
                long bestCost = long.MaxValue;
                double bestRatio = 0.0;
                bool bestDiagonal = false;
                for (int j = 0; j < n; j++)
                {
                    if (_colRank[j] >= 0) continue;
                    var list = _byCol[j];
                    int len = _colLen[j];
                    double colMax = 0.0;
                    for (int t = 0; t < len; t++)
                    {
                        int e = list[t];
                        if (_rowRank[_eRow[e]] >= 0) continue;
                        double a = Math.Abs(_eVal[e]);
                        if (a > colMax) colMax = a;
                    }

                    if (!(colMax > AbsolutePivot) || double.IsInfinity(colMax)) return false;

                    double limit = AnalysisThreshold * colMax;
                    long colCost = _colCount[j] - 1;
                    for (int t = 0; t < len; t++)
                    {
                        int e = list[t];
                        int i = _eRow[e];
                        if (_rowRank[i] >= 0) continue;
                        double a = Math.Abs(_eVal[e]);
                        if (a < limit || !(a > AbsolutePivot)) continue;
                        long cost = (_rowCount[i] - 1) * colCost;
                        double ratio = a / colMax;
                        bool diagonal = i == j;
                        bool better = cost < bestCost ||
                                      (cost == bestCost && diagonal && !bestDiagonal) ||
                                      (cost == bestCost && diagonal == bestDiagonal && ratio > bestRatio) ||
                                      (cost == bestCost && diagonal == bestDiagonal && ratio == bestRatio && j == bestCol && i < bestRow);
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

                // The pivot row and column leave the active submatrix; the pivot row's active part is kept for the update.
                double pivot = 0.0;
                int pivotLen = 0;
                for (int t = 0, len = _rowLen[p]; t < len; t++)
                {
                    int e = _byRow[p][t];
                    int j = _eCol[e];
                    if (j == q) pivot = _eVal[e];
                    if (_colRank[j] >= 0) continue;
                    _colCount[j]--;
                    if (pivotLen == pivotCols.Length)
                    {
                        Array.Resize(ref pivotCols, pivotLen * 2);
                        Array.Resize(ref pivotVals, pivotLen * 2);
                    }

                    pivotCols[pivotLen] = j;
                    pivotVals[pivotLen++] = _eVal[e];
                }

                for (int t = 0, len = _colLen[q]; t < len; t++)
                {
                    int i = _eRow[_byCol[q][t]];
                    if (_rowRank[i] < 0) _rowCount[i]--;
                }

                // Right-looking elimination of the active submatrix (values and fill-in), as the dense one does it.
                for (int t = 0, len = _colLen[q]; t < len; t++)
                {
                    int eq = _byCol[q][t];
                    int i = _eRow[eq];
                    if (_rowRank[i] >= 0) continue;
                    double factor = _eVal[eq] / pivot;
                    var row = _byRow[i];
                    int rowLen = _rowLen[i];
                    for (int r = 0; r < rowLen; r++) _mark[_eCol[row[r]]] = row[r];
                    for (int c = 0; c < pivotLen; c++)
                    {
                        int j = pivotCols[c];
                        int f = _mark[j];
                        if (f >= 0) _eVal[f] -= factor * pivotVals[c];
                        else NewEntry(i, j, 0.0 - factor * pivotVals[c]);
                    }

                    row = _byRow[i];
                    for (int r = 0, end = _rowLen[i]; r < end; r++) _mark[_eCol[row[r]]] = -1;
                }
            }

            BuildPatternFromEntries(n, nnz, rows, cols);
            IsAnalyzed = true;
            return true;
        }

        /// <summary>Adds entry (row, col) to the analysis' active submatrix; counts it in its row and column.</summary>
        private int NewEntry(int row, int col, double value)
        {
            int e = _entries++;
            if (e == _eRow.Length)
            {
                int capacity = Math.Max(16, e * 2);
                Array.Resize(ref _eRow, capacity);
                Array.Resize(ref _eCol, capacity);
                Array.Resize(ref _eVal, capacity);
            }

            _eRow[e] = row;
            _eCol[e] = col;
            _eVal[e] = value;
            Append(ref _byRow[row], ref _rowLen[row], e);
            Append(ref _byCol[col], ref _colLen[col], e);
            _rowCount[row]++;
            _colCount[col]++;
            return e;
        }

        private static void Append(ref int[] list, ref int length, int value)
        {
            if (length == list.Length) Array.Resize(ref list, Math.Max(4, length * 2));
            list[length++] = value;
        }

        private void EnsureAnalysis(int n, int nnz)
        {
            if (_byRow.Length < n)
            {
                var byRow = new int[n][];
                var byCol = new int[n][];
                for (int i = 0; i < n; i++)
                {
                    byRow[i] = i < _byRow.Length ? _byRow[i] : new int[4];
                    byCol[i] = i < _byCol.Length ? _byCol[i] : new int[4];
                }

                _byRow = byRow;
                _byCol = byCol;
                _rowLen = new int[n];
                _colLen = new int[n];
                _mark = new int[n];
            }

            if (_eRow.Length < nnz)
            {
                _eRow = new int[nnz * 2];
                _eCol = new int[nnz * 2];
                _eVal = new double[nnz * 2];
            }
        }

        /// <summary>L and U by column from the analysis' entries (every one the elimination made), rows ascending.</summary>
        private void BuildPatternFromEntries(int n, int nnz, int[] rows, int[] cols)
        {
            BuildPermutedA(n, nnz, rows, cols);
            int uCount = 0, lCount = 0;
            for (int c = 0; c < n; c++)
            {
                int q = _q[c];
                for (int t = 0, len = _colLen[q]; t < len; t++)
                {
                    int r = _rowRank[_eRow[_byCol[q][t]]];
                    if (r < c) uCount++;
                    else if (r > c) lCount++;
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
                int uFrom = u, lFrom = l;
                for (int t = 0, len = _colLen[q]; t < len; t++)
                {
                    int r = _rowRank[_eRow[_byCol[q][t]]];
                    if (r < c) _uRow[u++] = r;
                    else if (r > c) _lRow[l++] = r;
                }

                Array.Sort(_uRow, uFrom, u - uFrom);
                Array.Sort(_lRow, lFrom, l - lFrom);
            }

            _uPtr[n] = u;
            _lPtr[n] = l;
        }

        /// <summary>
        /// [test] The dense analysis the sparse one replaced: the same pivots and pattern from an n × n work space searched
        /// whole at every step. Kept only to check <see cref="Analyze"/> against.
        /// </summary>
        internal bool AnalyzeDense(int n, int nnz, int[] rows, int[] cols, double[] values)
        {
            IsAnalyzed = false;
            _n = n;
            EnsureCapacity(n);
            if (_w.Length < n * n)
            {
                _w = new double[n * n];
                _s = new bool[n * n];
            }

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

        /// <summary>[test] The pivot sequence: step k eliminates row PivotRows[k] and column PivotColumns[k].</summary>
        internal int[] PivotRows => _p[.._n];

        internal int[] PivotColumns => _q[.._n];

        /// <summary>[test] The L and U pattern: (column, row) of each entry in permuted order.</summary>
        internal (int[] UPtr, int[] URow, int[] LPtr, int[] LRow) Pattern =>
            (_uPtr[..(_n + 1)], _uRow[.._uPtr[_n]], _lPtr[..(_n + 1)], _lRow[.._lPtr[_n]]);

        private void BuildPermutedA(int n, int nnz, int[] rows, int[] cols)
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
        }

        private void BuildPattern(int n, int nnz, int[] rows, int[] cols)
        {
            BuildPermutedA(n, nnz, rows, cols);

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
        }
    }
}
