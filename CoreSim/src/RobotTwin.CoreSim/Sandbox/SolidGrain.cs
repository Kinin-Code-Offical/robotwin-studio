using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [gas] A cast BATES grain — one or more cored cylindrical segments, inhibited on the outside — split
    /// into axial zones from the head end to the nozzle. Each zone keeps its own core regression and, until
    /// it burns, its own temperature profile, so what real motors do at start-up comes out of the physics:
    /// <list type="bullet">
    /// <item>Ignition: an unlit surface is heated by the gas flowing past it — forced convection from the
    /// local port mass flux (Dittus–Boelter, Nu = 0.023·Re^0.8·Pr^0.4) plus radiation from the
    /// particle-laden gas — and the heat soaks into the propellant (1-D transient conduction, implicit).
    /// A zone lights when its surface reaches the propellant's ignition temperature. The igniter's gas
    /// enters at the head and cools as it gives heat to the grain, so the flame spreads aft; an igniter
    /// that is too small leaves the grain unlit.</item>
    /// <item>Erosive burning: r = a·pⁿ + α·G^0.8·D^−0.2·exp(−β·r·ρ/G) (Lenoir–Robillard, β = 53; α from
    /// the products' transport properties), with the port mass flux G growing towards the nozzle — the
    /// start-up pressure spike of a small port.</item>
    /// <item>Burnout zone by zone: the core reaches the casing first where the flux is highest, which
    /// exposes the wall there and gives the motor its tail-off.</item>
    /// </list>
    /// </summary>
    public sealed class SolidGrain
    {
        private const double Sigma = 5.670374e-8;
        private const double Beta = 53.0;          // Lenoir–Robillard
        private const int Nodes = 48;              // conduction grid below the surface
        private const double Dx = 8e-6;            // m (0.38 mm deep: well past the soak depth √(α·t) ≈ 0.03 mm at 5 ms)

        private readonly double[] _web, _rate, _mdot, _flowIn, _surface;
        private readonly double[][] _temp;
        private readonly double[] _endHead, _endAft;
        private readonly bool[] _lit;
        private readonly double _alpha;
        private readonly double _initialK;
        private double _endRate;

        public SolidGrain(Propellant propellant, double outer, double core, double segmentLength, int segments, double initialK)
        {
            Propellant = propellant;
            Outer = outer;
            Core = core;
            SegmentLength = segmentLength;
            Segments = Math.Max(1, segments);
            ZonesPerSegment = Math.Max(2, (int)Math.Ceiling(8.0 / Segments));
            int n = Segments * ZonesPerSegment;
            _web = new double[n];
            _rate = new double[n];
            _mdot = new double[n];
            _flowIn = new double[n];
            _surface = new double[n];
            _lit = new bool[n];
            _temp = new double[n][];
            _endHead = new double[Segments];
            _endAft = new double[Segments];
            _initialK = initialK;
            for (int i = 0; i < n; i++)
            {
                _temp[i] = new double[Nodes];
                for (int j = 0; j < Nodes; j++) _temp[i][j] = initialK;
                _surface[i] = initialK;
            }

            // Lenoir–Robillard α from the products at the flame temperature (Sutton & Biblarz §12.3).
            var g = propellant.Products;
            double mu = Viscosity(g, propellant.FlameTemperature), pr = Prandtl(g);
            double ts = propellant.BurningSurfaceTemperature;
            _alpha = 0.0288 * g.Cp * Math.Pow(mu, 0.2) * Math.Pow(pr, -2.0 / 3) / (propellant.Density * propellant.SolidHeatCapacity)
                     * Math.Max(0, propellant.FlameTemperature - ts) / Math.Max(1, ts - initialK);
        }

        public Propellant Propellant { get; }
        public double Outer { get; }
        public double Core { get; }
        public double SegmentLength { get; }
        public int Segments { get; }
        public int ZonesPerSegment { get; }
        public int Zones => _web.Length;

        /// <summary>Lenoir–Robillard erosive coefficient α (SI) of this propellant at this grain temperature.</summary>
        public double ErosiveAlpha => _alpha;

        public bool IsLit(int zone) => _lit[zone];
        public double Web(int zone) => _web[zone];
        public double SurfaceTemperature(int zone) => _lit[zone] ? Propellant.BurningSurfaceTemperature : _surface[zone];
        public double ZoneRate(int zone) => _rate[zone];

        // ------------------------------------------------------------------ geometry

        private double Diameter(int i) => Core + 2 * _web[i];

        private (double A, double B) Extent(int i)
        {
            int s = i / ZonesPerSegment, k = i % ZonesPerSegment;
            double a = k * SegmentLength / ZonesPerSegment, b = (k + 1) * SegmentLength / ZonesPerSegment;
            return (Math.Max(a, _endHead[s]), Math.Min(b, SegmentLength - _endAft[s]));
        }

        private double Length(int i)
        {
            var (a, b) = Extent(i);
            return Math.Max(0, b - a);
        }

        /// <summary>Whether a zone still holds propellant.</summary>
        public bool Present(int i) => Diameter(i) < Outer && Length(i) > 0;

        private double CoreArea(int i) => Present(i) ? Math.PI * Diameter(i) * Length(i) : 0;

        /// <summary>End faces a zone carries: the head end of its segment's first zone, the aft end of the last.</summary>
        private double EndArea(int i)
        {
            int s = i / ZonesPerSegment, k = i % ZonesPerSegment;
            if (SegmentLength - _endHead[s] - _endAft[s] <= 0) return 0;
            double a = 0;
            if (k == 0 && Present(i)) a += Math.PI / 4 * (Outer * Outer - Diameter(i) * Diameter(i));
            if (k == ZonesPerSegment - 1 && Present(i)) a += Math.PI / 4 * (Outer * Outer - Diameter(i) * Diameter(i));
            return a;
        }

        private double PortArea(int i) => Math.PI / 4 * Math.Min(Outer, Diameter(i)) * Math.Min(Outer, Diameter(i));

        public double Volume
        {
            get
            {
                double v = 0;
                for (int i = 0; i < Zones; i++)
                    if (Present(i)) v += Math.PI / 4 * (Outer * Outer - Diameter(i) * Diameter(i)) * Length(i);
                return v;
            }
        }

        public double InitialVolume => Segments * Math.PI / 4 * (Outer * Outer - Core * Core) * SegmentLength;

        public double TotalArea
        {
            get
            {
                double a = 0;
                for (int i = 0; i < Zones; i++) a += CoreArea(i) + EndArea(i);
                return a;
            }
        }

        public double LitArea
        {
            get
            {
                double a = 0;
                for (int i = 0; i < Zones; i++)
                    if (_lit[i]) a += CoreArea(i) + EndArea(i);
                return a;
            }
        }

        /// <summary>Share of the remaining surface that is burning (0 before ignition, 1 once the flame has spread).</summary>
        public double LitFraction
        {
            get
            {
                double total = TotalArea;
                return total > 0 ? LitArea / total : 0;
            }
        }

        public bool AnyLit
        {
            get
            {
                for (int i = 0; i < Zones; i++)
                    if (_lit[i] && Present(i)) return true;
                return false;
            }
        }

        public bool EverLit { get; private set; }

        public bool BurnedOut
        {
            get
            {
                for (int i = 0; i < Zones; i++)
                    if (Present(i)) return false;
                return true;
            }
        }

        /// <summary>Hottest surface that is not yet burning (K).</summary>
        public double HottestUnlitSurface
        {
            get
            {
                double t = _initialK;
                for (int i = 0; i < Zones; i++)
                    if (!_lit[i] && Present(i)) t = Math.Max(t, _surface[i]);
                return t;
            }
        }

        /// <summary>Casing wall no longer covered by propellant (m²): where the core burned through and the regressed segment ends.</summary>
        public double ExposedWall
        {
            get
            {
                double a = 0;
                double nominal = SegmentLength / ZonesPerSegment;
                for (int i = 0; i < Zones; i++) a += Math.PI * Outer * (Present(i) ? nominal - Length(i) : nominal);

                return a;
            }
        }

        // ------------------------------------------------------------------ gas properties

        /// <summary>Dynamic viscosity of combustion gas, Sutton &amp; Biblarz's estimate μ = 46.6·10⁻¹⁰·M^0.5·T_R^0.6 lb/in·s, in SI units.</summary>
        public static double Viscosity(GasSpecies g, double t) => 1.184e-7 * Math.Sqrt(8314.46 / g.R) * Math.Pow(Math.Max(200, t), 0.6);

        /// <summary>Eucken's estimate Pr = 4γ / (9γ − 5).</summary>
        public static double Prandtl(GasSpecies g) => 4 * g.Gamma / (9 * g.Gamma - 5);

        /// <summary>Forced convection in a tube, Dittus–Boelter: h = 0.023·k/D·Re^0.8·Pr^0.4 (W/m²·K).</summary>
        public static double TubeConvection(GasSpecies g, double t, double massFlux, double diameter)
        {
            double mu = Viscosity(g, t), pr = Prandtl(g), k = mu * g.Cp / pr;
            double re = Math.Abs(massFlux) * diameter / mu;
            return 0.023 * k / Math.Max(1e-4, diameter) * Math.Pow(Math.Max(0, re), 0.8) * Math.Pow(pr, 0.4);
        }

        // ------------------------------------------------------------------ burning

        /// <summary>
        /// Regression rates and gas generation at chamber pressure <paramref name="p"/> (Pa) with
        /// <paramref name="igniterFlow"/> (kg/s) entering at the head; returns the total mass rate (kg/s).
        /// </summary>
        public double Evaluate(double p, double igniterFlow)
        {
            var pr = Propellant;
            double r0 = pr.Rate(p);
            _endRate = r0;
            double upstream = Math.Max(0, igniterFlow), total = 0;
            for (int i = 0; i < Zones; i++)
            {
                _flowIn[i] = upstream;
                _mdot[i] = 0;
                _rate[i] = 0;
                if (!_lit[i] || !Present(i)) continue;
                double port = PortArea(i), d = Diameter(i), ac = CoreArea(i), ae = EndArea(i);
                double r = r0;
                for (int it = 0; it < 4; it++)
                {
                    double m = pr.Density * (ac * r + ae * r0);
                    double gFlux = (upstream + 0.5 * m) / Math.Max(1e-9, port);
                    double re = gFlux > 1 ? _alpha * Math.Pow(gFlux, 0.8) * Math.Pow(d, -0.2) * Math.Exp(-Beta * r * pr.Density / gFlux) : 0;
                    r = r0 + re;
                }

                _rate[i] = r;
                _mdot[i] = pr.Density * (ac * r + ae * r0);
                upstream += _mdot[i];
                total += _mdot[i];
            }

            return total;
        }

        /// <summary>
        /// Advances the grain by <paramref name="h"/> seconds after <see cref="Evaluate"/>: lit zones regress,
        /// unlit ones soak up heat from the gas flowing past them (the igniter's gas at <paramref name="igniterK"/>
        /// entering at the head, else the chamber gas at <paramref name="chamberK"/>) and light at the ignition
        /// temperature. Returns the heat taken from the gas (J); newly lit zones are added to <paramref name="lit"/>.
        /// </summary>
        public double Advance(double h, double igniterFlow, double igniterK, double chamberK, GasSpecies gas, List<int>? lit = null)
        {
            var pr = Propellant;
            for (int i = 0; i < Zones; i++)
                if (_lit[i] && Present(i)) _web[i] += _rate[i] * h;
            for (int s = 0; s < Segments; s++)
            {
                if (_lit[s * ZonesPerSegment]) _endHead[s] += _endRate * h;
                if (_lit[s * ZonesPerSegment + ZonesPerSegment - 1]) _endAft[s] += _endRate * h;
            }

            double heat = 0;
            double tIn = igniterFlow > 1e-9 ? igniterK : chamberK;
            double kp = pr.SolidConductivity, rc = pr.Density * pr.SolidHeatCapacity;
            for (int i = 0; i < Zones; i++)
            {
                double mIn = _flowIn[i];
                if (_lit[i])
                {
                    double mOut = mIn + _mdot[i];
                    tIn = mOut > 1e-9 ? (mIn * tIn + _mdot[i] * pr.FlameTemperature) / mOut : chamberK;
                    continue;
                }

                if (!Present(i)) continue;
                double tLoc = mIn > 1e-7 ? tIn : chamberK;
                double ts = _temp[i][0];
                double hc = Math.Max(25, TubeConvection(gas, tLoc, mIn / Math.Max(1e-9, PortArea(i)), Diameter(i)));
                double rad = pr.GasEmissivity * Sigma * (Math.Pow(tLoc, 4) - Math.Pow(ts, 4));
                double area = CoreArea(i) + EndArea(i);
                double q = Conduct(_temp[i], h, hc, tLoc, rad, kp, rc, _initialK);   // W/m² into the surface
                _surface[i] = _temp[i][0];
                double qj = q * area * h;
                heat += qj;
                if (mIn > 1e-7) tIn = Math.Max(_surface[i], tLoc - qj / (h * mIn * gas.Cp));
                if (_surface[i] >= pr.IgnitionTemperature)
                {
                    _lit[i] = true;
                    EverLit = true;
                    lit?.Add(i);
                }
            }

            return heat;
        }

        /// <summary>
        /// One implicit (backward-Euler) step of 1-D conduction into the propellant with a convective surface
        /// (linearised: hc·(T_gas − T_surface,new)) plus a radiative flux; the deepest node stays at the initial
        /// temperature. Returns the flux into the surface (W/m²).
        /// </summary>
        internal static double Conduct(double[] t, double h, double hc, double tGas, double rad, double k, double rc, double deepK)
        {
            int n = t.Length;
            Span<double> a = stackalloc double[n], b = stackalloc double[n], c = stackalloc double[n], d = stackalloc double[n];
            double kk = k / Dx, c0 = rc * Dx / 2 / h, ci = rc * Dx / h;
            a[0] = 0;
            b[0] = c0 + hc + kk;
            c[0] = -kk;
            d[0] = c0 * t[0] + hc * tGas + rad;
            for (int j = 1; j < n - 1; j++)
            {
                a[j] = -kk;
                b[j] = ci + 2 * kk;
                c[j] = -kk;
                d[j] = ci * t[j];
            }

            a[n - 1] = 0;
            b[n - 1] = 1;
            c[n - 1] = 0;
            d[n - 1] = deepK;
            // Thomas algorithm.
            for (int j = 1; j < n; j++)
            {
                double m = a[j] / b[j - 1];
                b[j] -= m * c[j - 1];
                d[j] -= m * d[j - 1];
            }

            t[n - 1] = d[n - 1] / b[n - 1];
            for (int j = n - 2; j >= 0; j--) t[j] = (d[j] - c[j] * t[j + 1]) / b[j];
            return hc * (tGas - t[0]) + rad;
        }
    }
}
