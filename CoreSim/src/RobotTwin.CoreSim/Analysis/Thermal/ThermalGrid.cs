using System;
using System.Collections.Generic;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Numerics;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis.Thermal
{
    public enum ThermalSideKind
    {
        /// <summary>Convection (h) and radiation (ε) to the ambient.</summary>
        Convection,
        Insulated,
        FixedTemperature
    }

    /// <summary>Boundary condition of one side of the grid box.</summary>
    public sealed class ThermalSide
    {
        public ThermalSideKind Kind { get; set; } = ThermalSideKind.Convection;
        public double Temperature { get; set; } = 25;

        /// <summary>Convection coefficient override (W/m²K); NaN = the grid's default.</summary>
        public double H { get; set; } = double.NaN;
    }

    /// <summary>
    /// [analysis] Transient heat conduction by the finite-volume method on a regular 3D grid (a 2D
    /// board is nz = 1). Per-cell material (orthotropic conductivity, ρ·c_p), heat sources, void cells,
    /// convection and grey-body radiation on every exposed face (radiation linearised about the
    /// previous temperature each step), θ-method in time (θ = 1 backward Euler, ½ Crank–Nicolson),
    /// each step solved with preconditioned conjugate gradients.
    /// </summary>
    public sealed class ThermalGrid
    {
        public const double StefanBoltzmann = 5.670374419e-8;

        private readonly int[] _index;    // cell → unknown (−1 = void)
        private readonly List<int> _cells = new List<int>();
        private SparseMatrix? _pattern;

        public ThermalGrid(int nx, int ny, int nz, double dx, double dy, double dz, Vec3 origin, Func<int, int, int, bool>? solid = null)
        {
            Nx = nx;
            Ny = ny;
            Nz = nz;
            Dx = dx;
            Dy = dy;
            Dz = dz;
            Origin = origin;
            int n = nx * ny * nz;
            _index = new int[n];
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int c = Cell(i, j, k);
                if (solid == null || solid(i, j, k))
                {
                    _index[c] = _cells.Count;
                    _cells.Add(c);
                }
                else
                {
                    _index[c] = -1;
                }
            }

            Kx = Fill(n, 1);
            Ky = Fill(n, 1);
            Kz = Fill(n, 1);
            RhoCp = Fill(n, 1e6);
            Emissivity = Fill(n, 0.9);
            Source = new double[n];
            T = Fill(n, 25);
            for (int s = 0; s < 6; s++) Sides[s] = new ThermalSide();
        }

        private static double[] Fill(int n, double v)
        {
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }
        public double Dx { get; }
        public double Dy { get; }
        public double Dz { get; }
        public Vec3 Origin { get; }
        public int SolidCells => _cells.Count;

        public double[] Kx { get; }
        public double[] Ky { get; }
        public double[] Kz { get; }

        /// <summary>Volumetric heat capacity ρ·c_p (J/m³K).</summary>
        public double[] RhoCp { get; }

        public double[] Emissivity { get; }

        /// <summary>Heat generated in each cell (W).</summary>
        public double[] Source { get; }

        /// <summary>Temperature (°C).</summary>
        public double[] T { get; }

        public double AmbientC { get; set; } = 25;

        /// <summary>Default convection coefficient on exposed faces (W/m²K).</summary>
        public double H { get; set; } = 10;

        public bool Radiation { get; set; } = true;

        /// <summary>θ of the time scheme: 1 = backward Euler (robust), 0.5 = Crank–Nicolson (2nd order).</summary>
        public double Theta { get; set; } = 1;

        /// <summary>Sides −X, +X, −Y, +Y, −Z, +Z.</summary>
        public ThermalSide[] Sides { get; } = new ThermalSide[6];

        public double Time { get; private set; }
        public SolveReport LastSolve { get; private set; }

        public int Cell(int i, int j, int k) => (k * Ny + j) * Nx + i;
        public bool IsSolid(int cell) => _index[cell] >= 0;
        public Vec3 CellCentre(int i, int j, int k) => Origin + new Vec3((i + 0.5) * Dx, (j + 0.5) * Dy, (k + 0.5) * Dz);

        public void SetMaterial(double k, double rhoCp, double emissivity = 0.9)
        {
            for (int c = 0; c < Kx.Length; c++)
            {
                Kx[c] = Ky[c] = Kz[c] = k;
                RhoCp[c] = rhoCp;
                Emissivity[c] = emissivity;
            }
        }

        public void SetUniformTemperature(double t)
        {
            for (int c = 0; c < T.Length; c++) T[c] = t;
        }

        public void ClearSources() => Array.Clear(Source, 0, Source.Length);

        /// <summary>Adds power (W) spread uniformly over the solid cells inside a box.</summary>
        public void AddSource(Vec3 min, Vec3 max, double watts)
        {
            var list = new List<int>();
            for (int k = 0; k < Nz; k++)
            for (int j = 0; j < Ny; j++)
            for (int i = 0; i < Nx; i++)
            {
                int c = Cell(i, j, k);
                if (!IsSolid(c)) continue;
                var p = CellCentre(i, j, k);
                if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && p.Z >= min.Z && p.Z <= max.Z) list.Add(c);
            }

            if (list.Count == 0)
            {
                // smaller than a cell: the nearest solid cell
                var centre = (min + max) * 0.5;
                int best = -1;
                double bd = double.PositiveInfinity;
                foreach (int c in _cells)
                {
                    int i = c % Nx, j = (c / Nx) % Ny, k = c / (Nx * Ny);
                    double d = (CellCentre(i, j, k) - centre).LengthSquared;
                    if (d < bd)
                    {
                        bd = d;
                        best = c;
                    }
                }

                if (best >= 0) Source[best] += watts;
                return;
            }

            foreach (int c in list) Source[c] += watts / list.Count;
        }

        public double MaxTemperature()
        {
            double m = double.NegativeInfinity;
            foreach (int c in _cells) m = Math.Max(m, T[c]);
            return m;
        }

        public double MeanTemperature()
        {
            double s = 0;
            foreach (int c in _cells) s += T[c];
            return _cells.Count > 0 ? s / _cells.Count : double.NaN;
        }

        /// <summary>Stored heat relative to the ambient (J) — for energy checks.</summary>
        public double StoredHeat()
        {
            double v = Dx * Dy * Dz, s = 0;
            foreach (int c in _cells) s += RhoCp[c] * v * (T[c] - AmbientC);
            return s;
        }

        private SparseMatrix Pattern()
        {
            if (_pattern != null) return _pattern;
            int n = _cells.Count;
            var rowStart = new int[n + 1];
            var cols = new List<int>(n * 7);
            var nb = new List<int>(7);
            for (int u = 0; u < n; u++)
            {
                rowStart[u] = cols.Count;
                int c = _cells[u];
                int i = c % Nx, j = (c / Nx) % Ny, k = c / (Nx * Ny);
                nb.Clear();
                nb.Add(u);
                void Try(int ii, int jj, int kk)
                {
                    if (ii < 0 || jj < 0 || kk < 0 || ii >= Nx || jj >= Ny || kk >= Nz) return;
                    int v = _index[Cell(ii, jj, kk)];
                    if (v >= 0) nb.Add(v);
                }

                Try(i - 1, j, k);
                Try(i + 1, j, k);
                Try(i, j - 1, k);
                Try(i, j + 1, k);
                Try(i, j, k - 1);
                Try(i, j, k + 1);
                nb.Sort();
                cols.AddRange(nb);
            }

            rowStart[n] = cols.Count;
            return _pattern = new SparseMatrix(n, rowStart, cols.ToArray());
        }

        private static double Series(double half1, double k1, double half2, double k2) => 1.0 / (half1 / k1 + half2 / k2);

        /// <summary>Advances the field by dt (implicit, unconditionally stable for θ ≥ ½).</summary>
        public void Step(double dt, CancellationToken cancel = default)
        {
            var a = Pattern();
            a.Clear();
            int n = _cells.Count;
            var rhs = new double[n];
            var x = new double[n];
            double vol = Dx * Dy * Dz;
            double ax = Dy * Dz, ay = Dx * Dz, az = Dx * Dy;
            double th = Theta;
            for (int u = 0; u < n; u++)
            {
                int c = _cells[u];
                int i = c % Nx, j = (c / Nx) % Ny, k = c / (Nx * Ny);
                double tc = T[c];
                double cap = RhoCp[c] * vol / dt;
                double diag = cap;
                double r = cap * tc + Source[c];
                x[u] = tc;

                void Face(int ii, int jj, int kk, double area, double half, double kc, int side, Func<int, double> kOf)
                {
                    bool outside = ii < 0 || jj < 0 || kk < 0 || ii >= Nx || jj >= Ny || kk >= Nz;
                    int nc = outside ? -1 : Cell(ii, jj, kk);
                    int v = outside ? -1 : _index[nc];
                    if (v >= 0)
                    {
                        double g = area * Series(half, kc, half, kOf(nc));
                        diag += th * g;
                        a.Add(u, v, -th * g);
                        r += (1 - th) * g * (T[nc] - tc);
                        return;
                    }

                    // exposed face: domain side condition or (internal void) convection + radiation
                    var sd = outside ? Sides[side] : null;
                    if (sd != null && sd.Kind == ThermalSideKind.Insulated) return;
                    if (sd != null && sd.Kind == ThermalSideKind.FixedTemperature)
                    {
                        double g = area * kc / half;
                        diag += th * g;
                        r += g * sd.Temperature - (1 - th) * g * tc;
                        return;
                    }

                    double h = sd != null && !double.IsNaN(sd.H) ? sd.H : H;
                    if (Radiation)
                    {
                        double tk = tc + 273.15, ta = AmbientC + 273.15;
                        h += Emissivity[c] * StefanBoltzmann * (tk * tk + ta * ta) * (tk + ta);
                    }

                    // surface resistance in series with half a cell of conduction
                    double ge = area / (1.0 / Math.Max(1e-12, h) + half / kc);
                    diag += th * ge;
                    r += ge * AmbientC - (1 - th) * ge * tc;
                }

                Face(i - 1, j, k, ax, Dx / 2, Kx[c], 0, q => Kx[q]);
                Face(i + 1, j, k, ax, Dx / 2, Kx[c], 1, q => Kx[q]);
                Face(i, j - 1, k, ay, Dy / 2, Ky[c], 2, q => Ky[q]);
                Face(i, j + 1, k, ay, Dy / 2, Ky[c], 3, q => Ky[q]);
                Face(i, j, k - 1, az, Dz / 2, Kz[c], 4, q => Kz[q]);
                Face(i, j, k + 1, az, Dz / 2, Kz[c], 5, q => Kz[q]);
                a.Add(u, u, diag);
                rhs[u] = r;
            }

            LastSolve = ConjugateGradient.Solve(a, rhs, x, null, 1e-10, 0, cancel);
            for (int u = 0; u < n; u++) T[_cells[u]] = x[u];
            Time += dt;
        }

        /// <summary>Copy of the temperature field as floats (void cells = NaN).</summary>
        public float[] Snapshot()
        {
            var s = new float[T.Length];
            for (int c = 0; c < T.Length; c++) s[c] = IsSolid(c) ? (float)T[c] : float.NaN;
            return s;
        }
    }
}
