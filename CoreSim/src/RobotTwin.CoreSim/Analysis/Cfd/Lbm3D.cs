using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Analysis.Cfd
{
    /// <summary>How the four lateral sides of the 3D domain treat the flow.</summary>
    public enum LbmFarField
    {
        /// <summary>Equilibrium at the free-stream velocity (a velocity boundary: confines the flow like a moving wall).</summary>
        FreeStream,

        /// <summary>Open: ambient pressure (ρ = 1) outside, the velocity the edge already has; flow displaced by the body
        /// leaves sideways, as in unbounded air.</summary>
        Open
    }

    /// <summary>
    /// [analysis] Three-dimensional lattice-Boltzmann flow solver (D3Q19). Collision: regularized BGK (Latt &amp; Chopard
    /// 2006: the non-equilibrium part is rebuilt from its second moment, which keeps the scheme stable down to τ → ½, i.e.
    /// at the real Reynolds number) with a Smagorinsky sub-grid eddy viscosity (LES). Walls: interpolated bounce-back
    /// (Bouzidi, Firdaouss &amp; Lallemand 2001) where the distance to the true surface along each link is known, which makes
    /// curved walls second-order accurate instead of a staircase; half-way bounce-back elsewhere. Force on the body by
    /// momentum exchange over every fluid–solid link. Inflow at −x, zero-gradient outflow at +x, far field on the four
    /// lateral sides (<see cref="FarField"/>). Parallel over z slices, deterministic.
    /// </summary>
    public sealed class Lbm3D
    {
        public static readonly int[] Cx = { 0, 1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
        public static readonly int[] Cy = { 0, 0, 0, 1, -1, 0, 0, 1, -1, -1, 1, 0, 0, 0, 0, 1, -1, 1, -1 };
        public static readonly int[] Cz = { 0, 0, 0, 0, 0, 1, -1, 0, 0, 0, 0, 1, -1, -1, 1, 1, -1, -1, 1 };
        public static readonly double[] W;
        public static readonly int[] Opp = new int[19];

        static Lbm3D()
        {
            W = new double[19];
            for (int q = 0; q < 19; q++)
            {
                int s = Math.Abs(Cx[q]) + Math.Abs(Cy[q]) + Math.Abs(Cz[q]);
                W[q] = s == 0 ? 1.0 / 3 : s == 1 ? 1.0 / 18 : 1.0 / 36;
                for (int p = 0; p < 19; p++)
                    if (Cx[p] == -Cx[q] && Cy[p] == -Cy[q] && Cz[p] == -Cz[q])
                        Opp[q] = p;
            }
        }

        private float[] _f, _g;
        private readonly double[] _sliceF;

        // [walls] Per boundary cell, 19 wall fractions Δ (NaN = no interpolation on that link): the true surface lies Δ·|c|
        // from the fluid node along the link into the solid.
        private int[]? _wallBase;
        private float[] _wall = Array.Empty<float>();

        public Lbm3D(int nx, int ny, int nz, double tau, double inletUx)
        {
            Nx = nx;
            Ny = ny;
            Nz = nz;
            Tau = tau;
            Ux0 = inletUx;
            long n = (long)nx * ny * nz;
            if (n * 19 > int.MaxValue) throw new ArgumentException("ızgara çok büyük");
            Solid = new bool[n];
            _f = new float[19 * n];
            _g = new float[19 * n];
            Ux = new float[n];
            Uy = new float[n];
            Uz = new float[n];
            Rho = new float[n];
            _sliceF = new double[nz * 3];
            int cells = (int)n;
            for (int i = 0; i < cells; i++)
            {
                for (int q = 0; q < 19; q++) _f[q * cells + i] = (float)Equilibrium(q, 1, inletUx, 0, 0);
                Ux[i] = (float)inletUx;
                Rho[i] = 1;
            }
        }

        public int Nx { get; }
        public int Ny { get; }
        public int Nz { get; }

        /// <summary>Molecular relaxation time τ0 (ν = (τ0 − ½)/3 in lattice units).</summary>
        public double Tau { get; set; }

        /// <summary>Smagorinsky constant of the sub-grid model (0 = none: direct simulation).</summary>
        public double Smagorinsky { get; set; } = 0.17;

        /// <summary>Regularized collision (default); false = plain BGK.</summary>
        public bool Regularized { get; set; } = true;

        public LbmFarField FarField { get; set; } = LbmFarField.FreeStream;

        /// <summary>
        /// Width (cells) of the absorbing layer along the inlet, outlet and the four sides: the viscosity rises smoothly to
        /// <see cref="SpongeViscosity"/> there, so pressure waves and eddies die out before they reach the boundary instead of
        /// reflecting back at the body (needed at high Reynolds numbers, where the fluid itself barely damps them).
        /// 0 = none.
        /// </summary>
        public int Sponge { get; set; }

        /// <summary>Kinematic viscosity (lattice units) at the outer edge of the absorbing layer.</summary>
        public double SpongeViscosity { get; set; } = 0.03;

        public double Ux0 { get; set; }

        /// <summary>
        /// [air] A box in still or moving ambient air instead of a wind tunnel: there is no inlet, and every face is a far
        /// field. A population entering along the ambient air's direction carries the ambient air
        /// (<see cref="AmbientX"/>, …); every other one enters at ambient pressure (ρ = 1) with the velocity the edge
        /// already has, so air pushed by a moving part leaves the box freely.
        /// </summary>
        public bool WorldBox { get; set; }

        /// <summary>[air] Ambient air velocity at the faces of a <see cref="WorldBox"/> (lattice units).</summary>
        public double AmbientX { get; set; }

        public double AmbientY { get; set; }
        public double AmbientZ { get; set; }

        /// <summary>
        /// [air] Velocity of the solid surface in each solid cell (lattice units, x, y, z per cell; null = every wall at
        /// rest). A moving wall gives the air its own velocity (Ladd 1994): air is pushed and dragged by moving parts.
        /// </summary>
        public float[]? WallVelocity { get; set; }

        /// <summary>
        /// [air] Signed distance to the nearest solid surface per cell (cells; negative inside). When set and no
        /// <see cref="SetWallFractions"/> table exists, each fluid–solid link takes its true wall position Δ = d₀/(d₀ − d₁)
        /// from it, so a moving part keeps a curved wall without rebuilding a table.
        /// </summary>
        public float[]? WallDistance { get; set; }

        /// <summary>[air] Only links into these solid cells add to <see cref="BodyFx"/> (null = every solid).</summary>
        public bool[]? ForceMask { get; set; }

        public bool[] Solid { get; }
        public float[] Ux { get; }
        public float[] Uy { get; }
        public float[] Uz { get; }
        public float[] Rho { get; }
        public double BodyFx { get; private set; }
        public double BodyFy { get; private set; }
        public double BodyFz { get; private set; }
        public long Steps { get; private set; }

        /// <summary>Fluid–solid links that use the interpolated (curved) wall.</summary>
        public int CurvedLinks { get; private set; }

        public int Index(int x, int y, int z) => (z * Ny + y) * Nx + x;

        public static double Equilibrium(int q, double rho, double ux, double uy, double uz)
        {
            double cu = Cx[q] * ux + Cy[q] * uy + Cz[q] * uz;
            double uu = ux * ux + uy * uy + uz * uz;
            return W[q] * rho * (1 + 3 * cu + 4.5 * cu * cu - 1.5 * uu);
        }

        public void SetCell(int x, int y, int z, double ux, double uy, double uz)
        {
            int n = Nx * Ny * Nz, i = Index(x, y, z);
            for (int q = 0; q < 19; q++) _f[q * n + i] = (float)Equilibrium(q, 1, ux, uy, uz);
        }

        /// <summary>
        /// [air] A cell a moving part has just left becomes air again: it starts at equilibrium with the density of the
        /// air around it and the velocity of the wall that left it. Set <see cref="Solid"/>[i] = false as well.
        /// </summary>
        public void Uncover(int i, double ux, double uy, double uz)
        {
            int nx = Nx, ny = Ny, nz = Nz, n = nx * ny * nz;
            int x = i % nx, y = i / nx % ny, z = i / (nx * ny);
            double rho = 0;
            int count = 0;
            for (int q = 1; q < 7; q++)
            {
                int ax = x + Cx[q], ay = y + Cy[q], az = z + Cz[q];
                if (ax < 0 || ay < 0 || az < 0 || ax >= nx || ay >= ny || az >= nz) continue;
                int a = (az * ny + ay) * nx + ax;
                if (Solid[a]) continue;
                rho += Rho[a];
                count++;
            }

            rho = count > 0 ? rho / count : 1;
            for (int q = 0; q < 19; q++) _f[q * n + i] = (float)Equilibrium(q, rho, ux, uy, uz);
            Rho[i] = (float)rho;
            Ux[i] = (float)ux;
            Uy[i] = (float)uy;
            Uz[i] = (float)uz;
        }

        /// <summary>
        /// [walls] Where the true surface lies on each link from a fluid cell into a solid one: <paramref name="fraction"/>
        /// (x, y, z, q) gets the fluid cell and the direction q pointing into the solid and returns Δ ∈ (0, 1], the
        /// fraction of the link length to the surface, or NaN when it is not known (that link keeps the half-way wall).
        /// Call after <see cref="Solid"/> is set.
        /// </summary>
        public void SetWallFractions(Func<int, int, int, int, double> fraction)
        {
            int nx = Nx, ny = Ny, nz = Nz, n = nx * ny * nz;
            var bases = new int[n];
            var list = new List<float>();
            int curved = 0;
            for (int i = 0; i < n; i++) bases[i] = -1;
            for (int z = 0; z < nz; z++)
            for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++)
            {
                int i = (z * ny + y) * nx + x;
                if (Solid[i]) continue;
                int start = -1;
                for (int q = 1; q < 19; q++)
                {
                    int sx = x + Cx[q], sy = y + Cy[q], sz = z + Cz[q];
                    if (sx < 0 || sy < 0 || sz < 0 || sx >= nx || sy >= ny || sz >= nz || !Solid[(sz * ny + sy) * nx + sx]) continue;
                    double d = fraction(x, y, z, q);
                    if (double.IsNaN(d)) continue;
                    if (start < 0)
                    {
                        start = list.Count;
                        for (int k = 0; k < 19; k++) list.Add(float.NaN);
                    }

                    list[start + q] = (float)Math.Max(1e-3, Math.Min(1, d));
                    curved++;
                }

                bases[i] = start;
            }

            _wallBase = bases;
            _wall = list.ToArray();
            CurvedLinks = curved;
        }

        public void Step(int count = 1, CancellationToken cancel = default)
        {
            for (int s = 0; s < count; s++)
            {
                if ((s & 7) == 0) cancel.ThrowIfCancellationRequested();
                StepOnce();
            }
        }

        private void StepOnce()
        {
            int nx = Nx, ny = Ny, nz = Nz, n = nx * ny * nz;
            var src = _f;
            var dst = _g;
            double tau0 = Tau, cs = Smagorinsky, u0 = Ux0;
            int sponge = Sponge;
            double tauSponge = Math.Max(tau0, 3 * SpongeViscosity + 0.5);
            bool regularized = Regularized, open = FarField == LbmFarField.Open;
            var wallBase = _wallBase;
            var wall = _wall;
            var feqIn = new double[19];
            for (int q = 0; q < 19; q++) feqIn[q] = Equilibrium(q, 1, u0, 0, 0);
            bool box = WorldBox;
            double ax0 = AmbientX, ay0 = AmbientY, az0 = AmbientZ;
            var feqAmb = new double[19];
            for (int q = 0; q < 19; q++) feqAmb[q] = Equilibrium(q, 1, ax0, ay0, az0);
            var wv = WallVelocity;
            var wd = wallBase == null ? WallDistance : null;
            var mask = ForceMask;
            Mechanics.WorkTeam.For(nz, 1, z =>   // [perf] threads that stay awake from one step to the next
            {
                Span<double> fq = stackalloc double[19];
                Span<double> fe = stackalloc double[19];
                double sfx = 0, sfy = 0, sfz = 0;
                for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int i = (z * ny + y) * nx + x;
                    if (Solid[i])
                    {
                        for (int q = 0; q < 19; q++) dst[q * n + i] = 0;
                        continue;
                    }

                    int wb = wallBase != null ? wallBase[i] : -1;
                    for (int q = 0; q < 19; q++)
                    {
                        int sx = x - Cx[q], sy = y - Cy[q], sz = z - Cz[q];
                        if (box && (sx < 0 || sx >= nx || sy < 0 || sy >= ny || sz < 0 || sz >= nz))
                        {
                            // [air] Ambient air flows in along its own direction; elsewhere the face is open at ambient pressure.
                            fq[q] = Cx[q] * ax0 + Cy[q] * ay0 + Cz[q] * az0 > 0 ? feqAmb[q] : Equilibrium(q, 1, Ux[i], Uy[i], Uz[i]);
                            continue;
                        }

                        if (sx < 0)
                        {
                            // Velocity inlet as a moving wall half a cell upstream (Ladd): the mass flux is exactly ρ·u0.
                            fq[q] = src[Opp[q] * n + i] + 6 * W[q] * Cx[q] * u0;
                            continue;
                        }

                        if (sy < 0 || sy >= ny || sz < 0 || sz >= nz)
                        {
                            if (!open)
                            {
                                fq[q] = feqIn[q];
                                continue;
                            }

                            // Open far field: ambient pressure (ρ = 1) outside, the velocity this edge already has.
                            fq[q] = Equilibrium(q, 1, Ux[i], Uy[i], Uz[i]);
                            continue;
                        }

                        if (sx >= nx) sx = x;   // outflow: zero gradient
                        int j = (sz * ny + sy) * nx + sx;
                        if (!Solid[j])
                        {
                            fq[q] = src[q * n + j];
                            continue;
                        }

                        // The population arriving from the wall. o points from this node into the solid.
                        int o = Opp[q];
                        double fo = src[o * n + i];
                        double fin = fo;
                        double delta = wb >= 0 ? wall[wb + o] : double.NaN;
                        if (wd != null)
                        {
                            // [air] The signed distance is linear along a link across a flat face: Δ where it crosses zero.
                            double d0 = wd[i], d1 = wd[j];
                            if (d0 > 0 && d1 < 0) delta = Math.Max(1e-3, Math.Min(1, d0 / (d0 - d1)));
                        }

                        if (!double.IsNaN(delta))
                        {
                            if (delta < 0.5)
                            {
                                // f_q(x) = 2Δ f*_o(x) + (1 − 2Δ) f*_o(x + c_q): needs the next fluid node away from the wall.
                                int ax = x + Cx[q], ay = y + Cy[q], az = z + Cz[q];
                                if (ax >= 0 && ay >= 0 && az >= 0 && ax < nx && ay < ny && az < nz)
                                {
                                    int a = (az * ny + ay) * nx + ax;
                                    if (!Solid[a]) fin = 2 * delta * fo + (1 - 2 * delta) * src[o * n + a];
                                }
                            }
                            else
                            {
                                // f_q(x) = f*_o(x) / 2Δ + (2Δ − 1)/2Δ · f*_q(x)
                                fin = fo / (2 * delta) + (2 * delta - 1) / (2 * delta) * src[q * n + i];
                            }
                        }

                        double uwx = 0, uwy = 0, uwz = 0;
                        if (wv != null)
                        {
                            // [air] Moving wall (Ladd): + 2 w ρ (c_q·u_w)/c_s²; Bouzidi's far-side rule scales it by 1/2Δ.
                            uwx = wv[3 * j];
                            uwy = wv[3 * j + 1];
                            uwz = wv[3 * j + 2];
                            double cu = Cx[q] * uwx + Cy[q] * uwy + Cz[q] * uwz;
                            if (cu != 0)
                            {
                                double add = 6 * W[q] * Rho[i] * cu;
                                if (!double.IsNaN(delta) && delta >= 0.5) add /= 2 * delta;
                                fin += add;
                            }
                        }

                        fq[q] = fin;
                        if (mask != null && !mask[j]) continue;
                        // Momentum given to the body over this link (Mei et al.): (f*_o + f_q) c_o; on a moving wall the
                        // Galilean-invariant form of Wen et al. (2014) subtracts u_w (f*_o − f_q).
                        double mom = fo + fin, slip = fo - fin;
                        // [air] Gauge force: the ambient pressure (ρ = 1 at rest: 2 w_o per link) pushes on every side of a
                        // part in air; on a part resting on the ground it would otherwise press it down through the floor.
                        if (box) mom -= 2 * W[o];
                        sfx += Cx[o] * mom - uwx * slip;
                        sfy += Cy[o] * mom - uwy * slip;
                        sfz += Cz[o] * mom - uwz * slip;
                    }

                    double rho = 0, mx = 0, my = 0, mz = 0;
                    for (int q = 0; q < 19; q++)
                    {
                        double v = fq[q];
                        rho += v;
                        mx += Cx[q] * v;
                        my += Cy[q] * v;
                        mz += Cz[q] * v;
                    }

                    double ux = mx / rho, uy = my / rho, uz = mz / rho;
                    double pxx = 0, pyy = 0, pzz = 0, pxy = 0, pyz = 0, pzx = 0;
                    for (int q = 0; q < 19; q++)
                    {
                        fe[q] = Equilibrium(q, rho, ux, uy, uz);
                        double neq = fq[q] - fe[q];
                        pxx += Cx[q] * Cx[q] * neq;
                        pyy += Cy[q] * Cy[q] * neq;
                        pzz += Cz[q] * Cz[q] * neq;
                        pxy += Cx[q] * Cy[q] * neq;
                        pyz += Cy[q] * Cz[q] * neq;
                        pzx += Cz[q] * Cx[q] * neq;
                    }

                    double t0 = tau0;
                    if (sponge > 0)
                    {
                        int edge = Math.Min(Math.Min(Math.Min(y, ny - 1 - y), Math.Min(z, nz - 1 - z)), Math.Min(x, nx - 1 - x));
                        if (edge < sponge)
                        {
                            double r = (sponge - edge) / (double)sponge;
                            t0 = tau0 + (tauSponge - tau0) * r * r;
                        }
                    }

                    double tau = t0;
                    if (cs > 0)
                    {
                        double qn = Math.Sqrt(pxx * pxx + pyy * pyy + pzz * pzz + 2 * (pxy * pxy + pyz * pyz + pzx * pzx));
                        tau = 0.5 * (t0 + Math.Sqrt(t0 * t0 + 18 * Math.Sqrt(2) * cs * cs * qn / rho));
                    }

                    double keep = 1 - 1 / tau;
                    for (int q = 0; q < 19; q++)
                    {
                        double feq = fe[q];
                        double neq;
                        if (regularized)
                        {
                            // Q_q : Π_neq with Q = c c − I/3; f_neq = w/(2 c_s⁴) Q:Π = 4.5 w Q:Π.
                            double cx = Cx[q], cy = Cy[q], cz = Cz[q];
                            double qp = (cx * cx - 1.0 / 3) * pxx + (cy * cy - 1.0 / 3) * pyy + (cz * cz - 1.0 / 3) * pzz
                                        + 2 * (cx * cy * pxy + cy * cz * pyz + cz * cx * pzx);
                            neq = 4.5 * W[q] * qp;
                        }
                        else
                        {
                            neq = fq[q] - feq;
                        }

                        dst[q * n + i] = (float)(feq + keep * neq);
                    }

                    Rho[i] = (float)rho;
                    Ux[i] = (float)ux;
                    Uy[i] = (float)uy;
                    Uz[i] = (float)uz;
                }

                _sliceF[3 * z] = sfx;
                _sliceF[3 * z + 1] = sfy;
                _sliceF[3 * z + 2] = sfz;
            });

            double fx = 0, fy = 0, fz = 0;
            for (int z = 0; z < nz; z++)
            {
                fx += _sliceF[3 * z];
                fy += _sliceF[3 * z + 1];
                fz += _sliceF[3 * z + 2];
            }

            BodyFx = fx;
            BodyFy = fy;
            BodyFz = fz;
            (_f, _g) = (_g, _f);
            Steps++;
        }
    }
}
