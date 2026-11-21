using System;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Analysis.Cfd
{
    /// <summary>
    /// [analysis] Three-dimensional lattice-Boltzmann flow solver (D3Q19, BGK + Smagorinsky LES,
    /// half-way bounce-back on solid cells, momentum-exchange body force). Inflow at −x, outflow at
    /// +x, far-field (free stream) on the four lateral sides. Parallel over z slices, deterministic.
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
        public double Tau { get; set; }
        public double Smagorinsky { get; set; } = 0.17;
        public double Ux0 { get; set; }
        public bool[] Solid { get; }
        public float[] Ux { get; }
        public float[] Uy { get; }
        public float[] Uz { get; }
        public float[] Rho { get; }
        public double BodyFx { get; private set; }
        public double BodyFy { get; private set; }
        public double BodyFz { get; private set; }
        public long Steps { get; private set; }
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
            var feqIn = new double[19];
            for (int q = 0; q < 19; q++) feqIn[q] = Equilibrium(q, 1, u0, 0, 0);
            Parallel.For(0, nz, z =>
            {
                Span<double> fq = stackalloc double[19];
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

                    for (int q = 0; q < 19; q++)
                    {
                        int sx = x - Cx[q], sy = y - Cy[q], sz = z - Cz[q];
                        if (sy < 0 || sy >= ny || sz < 0 || sz >= nz || sx < 0)
                        {
                            fq[q] = feqIn[q];
                            continue;
                        }

                        if (sx >= nx) sx = x;   // outflow: zero gradient
                        int j = (sz * ny + sy) * nx + sx;
                        if (Solid[j])
                        {
                            double fo = src[Opp[q] * n + i];
                            fq[q] = fo;
                            int o = Opp[q];
                            sfx += 2 * Cx[o] * fo;
                            sfy += 2 * Cy[o] * fo;
                            sfz += 2 * Cz[o] * fo;
                        }
                        else
                        {
                            fq[q] = src[q * n + j];
                        }
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
                    double tau = tau0;
                    if (cs > 0)
                    {
                        double pxx = 0, pyy = 0, pzz = 0, pxy = 0, pyz = 0, pzx = 0;
                        for (int q = 0; q < 19; q++)
                        {
                            double neq = fq[q] - Equilibrium(q, rho, ux, uy, uz);
                            pxx += Cx[q] * Cx[q] * neq;
                            pyy += Cy[q] * Cy[q] * neq;
                            pzz += Cz[q] * Cz[q] * neq;
                            pxy += Cx[q] * Cy[q] * neq;
                            pyz += Cy[q] * Cz[q] * neq;
                            pzx += Cz[q] * Cx[q] * neq;
                        }

                        double qn = Math.Sqrt(pxx * pxx + pyy * pyy + pzz * pzz + 2 * (pxy * pxy + pyz * pyz + pzx * pzx));
                        tau = 0.5 * (tau0 + Math.Sqrt(tau0 * tau0 + 18 * Math.Sqrt(2) * cs * cs * qn / rho));
                    }

                    double om = 1 / tau;
                    for (int q = 0; q < 19; q++) dst[q * n + i] = (float)(fq[q] - om * (fq[q] - Equilibrium(q, rho, ux, uy, uz)));
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
