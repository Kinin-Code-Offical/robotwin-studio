using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Analysis.Cfd
{
    public enum LbmBoundary
    {
        /// <summary>Wraps around to the opposite side.</summary>
        Periodic,

        /// <summary>Equilibrium at the free-stream velocity (far field / inlet).</summary>
        FreeStream,

        /// <summary>No-slip wall (half-way bounce-back).</summary>
        Wall,

        /// <summary>Zero-gradient outflow.</summary>
        Outflow,

        /// <summary>Open far field: ambient pressure (ρ = 1) outside, the velocity the edge already has. Flow displaced by
        /// a body leaves freely instead of being held at the free-stream speed as by <see cref="FreeStream"/>.</summary>
        Open
    }

    /// <summary>
    /// [analysis] Two-dimensional lattice-Boltzmann flow solver (D2Q9). Collision: regularized BGK
    /// (stable at the real Reynolds number) with a Smagorinsky sub-grid eddy viscosity. Solid cells use
    /// interpolated bounce-back (Bouzidi) where the true surface is known (<see cref="SetWallFractions"/>),
    /// half-way bounce-back elsewhere; the force on the body comes from momentum exchange over every
    /// fluid–solid link. Lattice units: Δx = Δt = 1, c_s² = 1/3, p = c_s²·ρ. Fluid cells are updated in
    /// parallel row blocks; the result is identical for any number of threads.
    /// </summary>
    public sealed class Lbm2D
    {
        public static readonly int[] Cx = { 0, 1, 0, -1, 0, 1, -1, -1, 1 };
        public static readonly int[] Cy = { 0, 0, 1, 0, -1, 1, 1, -1, -1 };
        public static readonly double[] W = { 4.0 / 9, 1.0 / 9, 1.0 / 9, 1.0 / 9, 1.0 / 9, 1.0 / 36, 1.0 / 36, 1.0 / 36, 1.0 / 36 };
        public static readonly int[] Opp = { 0, 3, 4, 1, 2, 7, 8, 5, 6 };

        private float[] _f, _g;
        private readonly double[] _rowFx, _rowFy;
        private int[]? _wallBase;
        private float[] _wall = Array.Empty<float>();

        public Lbm2D(int nx, int ny, double tau, double inletUx, double inletUy = 0)
        {
            Nx = nx;
            Ny = ny;
            Tau = tau;
            Ux0 = inletUx;
            Uy0 = inletUy;
            Solid = new bool[nx * ny];
            _f = new float[9 * nx * ny];
            _g = new float[9 * nx * ny];
            _rowFx = new double[ny];
            _rowFy = new double[ny];
            Ux = new float[nx * ny];
            Uy = new float[nx * ny];
            Rho = new float[nx * ny];
            Initialise(ux: inletUx, uy: inletUy);
        }

        public int Nx { get; }
        public int Ny { get; }

        /// <summary>Molecular relaxation time τ0 (ν = (τ0 − ½)/3).</summary>
        public double Tau { get; set; }

        /// <summary>Smagorinsky constant (0 = plain BGK / DNS).</summary>
        public double Smagorinsky { get; set; }

        /// <summary>Regularized collision (default); false = plain BGK.</summary>
        public bool Regularized { get; set; } = true;

        /// <summary>Inflow profile u_x(y) at a <see cref="LbmBoundary.FreeStream"/> left edge (null = uniform <see cref="Ux0"/>).</summary>
        public Func<int, double>? InletProfile { get; set; }

        /// <summary>Fluid–solid links that use the interpolated (curved) wall.</summary>
        public int CurvedLinks { get; private set; }

        public double Ux0 { get; set; }
        public double Uy0 { get; set; }

        /// <summary>Uniform body force per unit volume (lattice units), e.g. a pressure gradient.</summary>
        public double ForceX { get; set; }

        public double ForceY { get; set; }

        public LbmBoundary Left { get; set; } = LbmBoundary.FreeStream;
        public LbmBoundary Right { get; set; } = LbmBoundary.Outflow;
        public LbmBoundary Bottom { get; set; } = LbmBoundary.FreeStream;
        public LbmBoundary Top { get; set; } = LbmBoundary.FreeStream;

        public bool[] Solid { get; }
        public float[] Ux { get; }
        public float[] Uy { get; }
        public float[] Rho { get; }

        /// <summary>Force on the solid cells over the last step (lattice units, per unit depth).</summary>
        public double BodyFx { get; private set; }

        public double BodyFy { get; private set; }
        public long Steps { get; private set; }
        public double Viscosity => (Tau - 0.5) / 3;

        public int Index(int x, int y) => y * Nx + x;

        public void Initialise(double ux, double uy, double rho = 1)
        {
            int n = Nx * Ny;
            for (int i = 0; i < n; i++)
            {
                for (int q = 0; q < 9; q++) _f[q * n + i] = (float)Equilibrium(q, rho, ux, uy);
                Ux[i] = (float)ux;
                Uy[i] = (float)uy;
                Rho[i] = (float)rho;
            }
        }

        /// <summary>Sets one cell to equilibrium at a velocity (to seed a perturbation).</summary>
        public void SetCell(int x, int y, double rho, double ux, double uy)
        {
            int n = Nx * Ny, i = Index(x, y);
            for (int q = 0; q < 9; q++) _f[q * n + i] = (float)Equilibrium(q, rho, ux, uy);
        }

        public static double Equilibrium(int q, double rho, double ux, double uy)
        {
            double cu = Cx[q] * ux + Cy[q] * uy;
            double uu = ux * ux + uy * uy;
            return W[q] * rho * (1 + 3 * cu + 4.5 * cu * cu - 1.5 * uu);
        }

        /// <summary>
        /// Where the true surface lies on each link from a fluid cell into a solid one: <paramref name="fraction"/>(x, y, q)
        /// gets the fluid cell and the direction q pointing into the solid and returns Δ ∈ (0, 1], the fraction of the link
        /// to the surface, or NaN when unknown (that link keeps the half-way wall). Call after <see cref="Solid"/> is set.
        /// </summary>
        public void SetWallFractions(Func<int, int, int, double> fraction)
        {
            int nx = Nx, ny = Ny, n = nx * ny;
            var bases = new int[n];
            var list = new List<float>();
            int curved = 0;
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int i = y * nx + x;
                bases[i] = -1;
                if (Solid[i]) continue;
                for (int q = 1; q < 9; q++)
                {
                    int sx = x + Cx[q], sy = y + Cy[q];
                    if (sx < 0 || sy < 0 || sx >= nx || sy >= ny || !Solid[sy * nx + sx]) continue;
                    double d = fraction(x, y, q);
                    if (double.IsNaN(d)) continue;
                    if (bases[i] < 0)
                    {
                        bases[i] = list.Count;
                        for (int k = 0; k < 9; k++) list.Add(float.NaN);
                    }

                    list[bases[i] + q] = (float)Math.Max(1e-3, Math.Min(1, d));
                    curved++;
                }
            }

            _wallBase = bases;
            _wall = list.ToArray();
            CurvedLinks = curved;
        }

        public void Step(int count = 1, CancellationToken cancel = default)
        {
            for (int s = 0; s < count; s++)
            {
                if ((s & 31) == 0) cancel.ThrowIfCancellationRequested();
                StepOnce();
            }
        }

        private void StepOnce()
        {
            int nx = Nx, ny = Ny, n = nx * ny;
            var src = _f;
            var dst = _g;
            double tau0 = Tau, cs = Smagorinsky;
            double fxB = ForceX, fyB = ForceY;
            bool regularized = Regularized;
            var profile = InletProfile;
            var wallBase = _wallBase;
            var wall = _wall;
            const int block = 8;
            int blocks = (ny + block - 1) / block;
            Mechanics.WorkTeam.For(blocks, 1, b =>   // [perf] threads that stay awake from one step to the next
            {
                Span<double> fq = stackalloc double[9];
                Span<double> fe = stackalloc double[9];
                int y0 = b * block, y1 = Math.Min(ny, y0 + block);
                for (int y = y0; y < y1; y++)
                {
                    double rowFx = 0, rowFy = 0;
                    for (int x = 0; x < nx; x++)
                    {
                        int i = y * nx + x;
                        if (Solid[i])
                        {
                            for (int q = 0; q < 9; q++) dst[q * n + i] = 0;
                            continue;
                        }

                        int wb = wallBase != null ? wallBase[i] : -1;
                        for (int q = 0; q < 9; q++)
                        {
                            int sx = x - Cx[q], sy = y - Cy[q];
                            bool special = false;
                            if (sy < 0 || sy >= ny)
                            {
                                var bc = sy < 0 ? Bottom : Top;
                                if (bc == LbmBoundary.Periodic) sy = (sy + ny) % ny;
                                else if (bc == LbmBoundary.Wall)
                                {
                                    fq[q] = src[Opp[q] * n + i];
                                    special = true;
                                }
                                else if (bc == LbmBoundary.Outflow) sy = y;
                                else if (bc == LbmBoundary.Open)
                                {
                                    fq[q] = Equilibrium(q, 1, Ux[i], Uy[i]);
                                    special = true;
                                }
                                else
                                {
                                    fq[q] = Equilibrium(q, 1, Ux0, Uy0);
                                    special = true;
                                }
                            }

                            if (!special && (sx < 0 || sx >= nx))
                            {
                                var bc = sx < 0 ? Left : Right;
                                if (bc == LbmBoundary.Periodic) sx = (sx + nx) % nx;
                                else if (bc == LbmBoundary.Wall)
                                {
                                    fq[q] = src[Opp[q] * n + i];
                                    special = true;
                                }
                                else if (bc == LbmBoundary.Outflow) sx = x;
                                else if (bc == LbmBoundary.Open)
                                {
                                    fq[q] = Equilibrium(q, 1, Ux[i], Uy[i]);
                                    special = true;
                                }
                                else if (sx < 0)
                                {
                                    // Velocity inlet as a moving wall half a cell outside (Ladd): the mass flux is exactly ρ·u_in.
                                    double ui = profile != null ? profile(y) : Ux0;
                                    fq[q] = src[Opp[q] * n + i] + 6 * W[q] * (Cx[q] * ui + Cy[q] * Uy0);
                                    special = true;
                                    continue;
                                }
                                else
                                {
                                    fq[q] = Equilibrium(q, 1, Ux0, Uy0);
                                    special = true;
                                }
                            }

                            if (special) continue;
                            int j = sy * nx + sx;
                            if (Solid[j])
                            {
                                // o points from this node into the solid; f*_o reflects off the wall and comes back as f_q.
                                int o = Opp[q];
                                double fo = src[o * n + i];
                                double fin = fo;
                                double delta = wb >= 0 ? wall[wb + o] : double.NaN;
                                if (!double.IsNaN(delta))
                                {
                                    if (delta < 0.5)
                                    {
                                        int ax = x + Cx[q], ay = y + Cy[q];
                                        if (ax >= 0 && ay >= 0 && ax < nx && ay < ny && !Solid[ay * nx + ax])
                                            fin = 2 * delta * fo + (1 - 2 * delta) * src[o * n + ay * nx + ax];
                                    }
                                    else
                                    {
                                        fin = fo / (2 * delta) + (2 * delta - 1) / (2 * delta) * src[q * n + i];
                                    }
                                }

                                fq[q] = fin;
                                // momentum given to the body over this link: (f*_o + f_q)·c_o
                                rowFx += Cx[o] * (fo + fin);
                                rowFy += Cy[o] * (fo + fin);
                            }
                            else
                            {
                                fq[q] = src[q * n + j];
                            }
                        }

                        double rho = 0, mx = 0, my = 0;
                        for (int q = 0; q < 9; q++)
                        {
                            rho += fq[q];
                            mx += Cx[q] * fq[q];
                            my += Cy[q] * fq[q];
                        }

                        double ux = mx / rho, uy = my / rho;
                        double tau = tau0;
                        double pxx = 0, pyy = 0, pxy = 0;
                        if (cs > 0 || regularized)
                        {
                            for (int q = 0; q < 9; q++)
                            {
                                double neq = fq[q] - Equilibrium(q, rho, ux, uy);
                                pxx += Cx[q] * Cx[q] * neq;
                                pyy += Cy[q] * Cy[q] * neq;
                                pxy += Cx[q] * Cy[q] * neq;
                            }
                        }

                        if (cs > 0)
                        {
                            double qn = Math.Sqrt(pxx * pxx + pyy * pyy + 2 * pxy * pxy);
                            tau = 0.5 * (tau0 + Math.Sqrt(tau0 * tau0 + 18 * Math.Sqrt(2) * cs * cs * qn / rho));
                        }

                        // body force by the velocity shift of the equilibrium (Shan–Chen); physical u = u + F/(2ρ)
                        double uex = ux + tau * fxB / rho, uey = uy + tau * fyB / rho;
                        double keep = 1 - 1 / tau;
                        for (int q = 0; q < 9; q++) fe[q] = Equilibrium(q, rho, uex, uey);
                        if (regularized && (fxB != 0 || fyB != 0))
                        {
                            // Second moment of the non-equilibrium part relative to the shifted equilibrium.
                            pxx = pyy = pxy = 0;
                            for (int q = 0; q < 9; q++)
                            {
                                double neq = fq[q] - fe[q];
                                pxx += Cx[q] * Cx[q] * neq;
                                pyy += Cy[q] * Cy[q] * neq;
                                pxy += Cx[q] * Cy[q] * neq;
                            }
                        }

                        // With a body force the non-equilibrium part also carries momentum ρ(u − u_eq) = −τF; keep it, or the
                        // force would be applied only τ times.
                        double jx = rho * (ux - uex), jy = rho * (uy - uey);
                        for (int q = 0; q < 9; q++)
                        {
                            double neq = regularized
                                ? W[q] * (3 * (Cx[q] * jx + Cy[q] * jy)
                                          + 4.5 * ((Cx[q] * Cx[q] - 1.0 / 3) * pxx + (Cy[q] * Cy[q] - 1.0 / 3) * pyy + 2 * Cx[q] * Cy[q] * pxy))
                                : fq[q] - fe[q];
                            dst[q * n + i] = (float)(fe[q] + keep * neq);
                        }
                        Rho[i] = (float)rho;
                        Ux[i] = (float)(ux + 0.5 * fxB / rho);
                        Uy[i] = (float)(uy + 0.5 * fyB / rho);
                    }

                    _rowFx[y] = rowFx;
                    _rowFy[y] = rowFy;
                }
            });

            double fx = 0, fy = 0;
            for (int y = 0; y < ny; y++)
            {
                fx += _rowFx[y];
                fy += _rowFy[y];
            }

            BodyFx = fx;
            BodyFy = fy;
            (_f, _g) = (_g, _f);
            Steps++;
        }

        /// <summary>Vorticity ∂uy/∂x − ∂ux/∂y (central differences; 0 in solids).</summary>
        public float[] Vorticity()
        {
            var w = new float[Nx * Ny];
            for (int y = 1; y < Ny - 1; y++)
            for (int x = 1; x < Nx - 1; x++)
            {
                int i = Index(x, y);
                if (Solid[i]) continue;
                w[i] = 0.5f * (Uy[i + 1] - Uy[i - 1]) - 0.5f * (Ux[i + Nx] - Ux[i - Nx]);
            }

            return w;
        }
    }
}
