using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Aero
{
    /// <summary>Section (2-D) coefficients at one angle of attack: lift, drag, pitching moment about the quarter chord.</summary>
    public readonly struct SectionCoefficients
    {
        public SectionCoefficients(double cl, double cd, double cm)
        {
            Cl = cl;
            Cd = cd;
            Cm = cm;
        }

        public double Cl { get; }
        public double Cd { get; }
        public double Cm { get; }
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Cl={0:0.###} Cd={1:0.####} Cm={2:0.###}", Cl, Cd, Cm);
    }

    /// <summary>
    /// An airfoil section polar over the full ±180° range. α in radians (positive = nose up / air
    /// from below), <paramref name="flapEffect"/> is the flap's equivalent change of angle of attack
    /// τ·δ (rad), Re the chord Reynolds number. Implementations must be thread-safe and allocation-free.
    /// </summary>
    public interface IAirfoilPolar
    {
        string Name { get; }

        SectionCoefficients Evaluate(double alpha, double flapEffect, double reynolds);

        /// <summary>Angle (rad) of maximum lift with the flap neutral (for stall warnings and tests).</summary>
        double StallAngle { get; }
    }

    /// <summary>
    /// [aero] Parametric airfoil: thin-airfoil linear lift below stall and the Viterna–Corrigan
    /// post-stall model above it, valid over the full circle.
    /// <list type="bullet">
    /// <item>Attached flow: C_l = a₀·(α − α₀ + τδ), C_d = C_d0(Re) + k·(C_l − C_l,md)², C_m = C_m0 + ΔC_m,flap.</item>
    /// <item>Stall at C_l,max (ΔC_l,max from the flap ≈ 0.6·a₀·τδ for δ &gt; 0).</item>
    /// <item>Post-stall (Viterna &amp; Corrigan 1982, NASA CP-2230): C_D = B₁ sin²α + B₂ cos α,
    /// C_L = A₁ sin 2α + A₂ cos²α / sin α, B₁ = C_D,max, A₁ = B₁/2, B₂/A₂ chosen for continuity at
    /// stall; C_D,max = 1.11 + 0.018·AR. Beyond 90° the plate is reversed: C_L(α) = −0.7·C_L(π − α)
    /// (the usual AirfoilPrep convention).</item>
    /// <item>Post-stall pitching moment: the normal force acts at a centre of pressure moving from the
    /// quarter chord at stall to mid-chord at 90° and to the three-quarter chord in reversed flow.</item>
    /// <item>Reynolds number: C_d0 ∝ (Re_ref/Re)^0.2 (turbulent flat-plate friction scaling), C_l,max
    /// reduced by up to 25 % below Re = 10⁵ (low-Re laminar separation; McGhee/Selig UIUC data trend).</item>
    /// </list>
    /// Section data of the presets are approximations of published wind-tunnel results (sources per preset).
    /// </summary>
    public sealed class ParametricAirfoil : IAirfoilPolar
    {
        public string Name { get; init; } = "Airfoil";

        /// <summary>Section lift slope a₀ (1/rad), 2π for the thin-airfoil ideal.</summary>
        public double LiftSlope { get; init; } = 2 * Math.PI * 0.95;

        /// <summary>Zero-lift angle α₀ (rad, negative for cambered sections).</summary>
        public double ZeroLiftAngle { get; init; }

        public double ClMax { get; init; } = 1.3;

        /// <summary>Most negative lift coefficient before negative stall.</summary>
        public double ClMin { get; init; } = -1.0;

        /// <summary>Minimum profile drag at the reference Reynolds number.</summary>
        public double Cd0 { get; init; } = 0.010;

        /// <summary>Lift coefficient at minimum drag.</summary>
        public double ClMinDrag { get; init; } = 0.2;

        /// <summary>Quadratic profile-drag factor k in C_d = C_d0 + k·(C_l − C_l,md)².</summary>
        public double DragFactor { get; init; } = 0.010;

        /// <summary>Quarter-chord pitching moment in attached flow.</summary>
        public double Cm0 { get; init; }

        /// <summary>Reynolds number at which <see cref="Cd0"/> and <see cref="ClMax"/> apply.</summary>
        public double ReferenceReynolds { get; init; } = 3e5;

        /// <summary>
        /// [aero] How C_l,max grows with the Reynolds number: its change per e-fold of Re (C_l,max(Re) = C_l,max + s·ln(Re /
        /// Re_ref)), as published section data show it over 10⁴…10⁷. 0 keeps the simple rule: a stall penalty of up to
        /// 25 % below the reference and none above.
        /// </summary>
        public double ClMaxReynoldsSlope { get; init; }

        public double ThicknessRatio { get; init; } = 0.12;

        /// <summary>Aspect ratio used for the post-stall C_D,max (set by the wing; 2-D sections use a large value).</summary>
        public double AspectRatio { get; init; } = 10;

        public double StallAngle => ZeroLiftAngle + ClMax / LiftSlope;

        public double CdMax => 1.11 + 0.018 * Math.Min(50, Math.Max(0, AspectRatio));

        public ParametricAirfoil WithAspectRatio(double ar) => new ParametricAirfoil
        {
            Name = Name, LiftSlope = LiftSlope, ZeroLiftAngle = ZeroLiftAngle, ClMax = ClMax, ClMin = ClMin, Cd0 = Cd0, ClMinDrag = ClMinDrag,
            DragFactor = DragFactor, Cm0 = Cm0, ReferenceReynolds = ReferenceReynolds, ClMaxReynoldsSlope = ClMaxReynoldsSlope, ThicknessRatio = ThicknessRatio, AspectRatio = ar
        };

        public SectionCoefficients Evaluate(double alpha, double flapEffect, double reynolds)
        {
            // Wrap to (−π, π].
            double a = alpha;
            if (a > Math.PI || a <= -Math.PI) a = Math.IEEERemainder(a, 2 * Math.PI);
            double re = Math.Max(1e3, reynolds);
            double reFactor = Math.Pow(ReferenceReynolds / re, 0.2);
            reFactor = Math.Max(0.6, Math.Min(2.5, reFactor));
            double cd0 = Cd0 * reFactor;
            // C_l,max with the Reynolds number: the section's own law when it has one, else a low-Re penalty only
            // (up to −25 % below the reference, none above).
            double lowRe = ClMaxReynoldsSlope > 0
                ? Math.Max(0.4, Math.Min(1.8, 1.0 + ClMaxReynoldsSlope / Math.Max(0.1, ClMax) * Math.Log(re / ReferenceReynolds)))
                : re >= ReferenceReynolds ? 1.0 : Math.Max(0.75, 1.0 - 0.25 * Math.Log10(ReferenceReynolds / re));
            double clMax = ClMax * lowRe + (flapEffect > 0 ? 0.6 * LiftSlope * flapEffect : 0);
            double clMin = ClMin * lowRe + (flapEffect < 0 ? 0.6 * LiftSlope * flapEffect : 0);
            double aEff = a + flapEffect;
            double clLin = LiftSlope * (aEff - ZeroLiftAngle);
            double cmFlap = FlapMoment(flapEffect);
            double alphaStall = ZeroLiftAngle - flapEffect + clMax / LiftSlope;      // geometric α at positive stall
            double alphaNeg = ZeroLiftAngle - flapEffect + clMin / LiftSlope;        // geometric α at negative stall
            if (a <= alphaStall && a >= alphaNeg)
            {
                double dcl = clLin - ClMinDrag;
                return new SectionCoefficients(clLin, cd0 + DragFactor * dcl * dcl, Cm0 + cmFlap);
            }

            // Post-stall (Viterna–Corrigan) on the stalled side, reflected plate beyond 90°.
            bool positive = a > alphaStall;
            double sStall = Math.Min(Math.PI / 2 - 0.05, Math.Max(1e-3, positive ? alphaStall : -alphaNeg));
            double clSigned = positive ? clMax : clMin;
            double clS = Math.Max(0.05, Math.Abs(clSigned));
            double cdS = cd0 + DragFactor * (clSigned - ClMinDrag) * (clSigned - ClMinDrag);
            double x = Math.Max(sStall, positive ? a : -a);   // angle measured towards the stalled side
            double cl, cd;
            if (x <= Math.PI / 2)
            {
                (cl, cd) = Viterna(x, sStall, clS, cdS);
            }
            else
            {
                double xr = Math.PI - x;
                if (xr >= sStall)
                {
                    var v = Viterna(xr, sStall, clS, cdS);
                    cl = -0.7 * v.Cl;
                    cd = v.Cd;
                }
                else
                {
                    // Last few degrees before the flow is exactly reversed: lift fades to zero, drag to the profile value.
                    double f = xr / sStall;
                    cl = -0.7 * clS * f;
                    cd = 1.5 * cd0 + (cdS - 1.5 * cd0) * f;
                }
            }

            cd = Math.Max(cd, cd0);
            if (!positive) cl = -cl;
            // Pitching moment: normal force at a centre of pressure moving aft with the angle.
            double cn = cl * Math.Cos(a) + cd * Math.Sin(a);
            double xcp = x <= Math.PI / 2
                ? 0.25 + 0.25 * (x - sStall) / Math.Max(1e-3, Math.PI / 2 - sStall)
                : 0.5 + 0.25 * (x - Math.PI / 2) / (Math.PI / 2);
            double w = Math.Max(0, Math.Min(1, (x - sStall) / 0.1));
            double cm = (Cm0 + cmFlap) * (1 - w) - cn * (xcp - 0.25);
            return new SectionCoefficients(cl, cd, cm);
        }

        private (double Cl, double Cd) Viterna(double x, double stall, double clS, double cdS)
        {
            double b1 = CdMax;
            double ss = Math.Sin(stall), cs = Math.Cos(stall);
            double b2 = (cdS - b1 * ss * ss) / cs;
            double a1 = b1 / 2;
            double a2 = (clS - b1 * ss * cs) * ss / (cs * cs);
            double sx = Math.Sin(x), cx = Math.Cos(x);
            double cl = a1 * Math.Sin(2 * x) + a2 * cx * cx / Math.Max(1e-6, sx);
            double cd = b1 * sx * sx + b2 * cx;
            return (cl, Math.Max(cd, 0.0));
        }

        /// <summary>
        /// Thin-airfoil pitching moment of a plain flap: ΔC_m,c/4 = −½·sin θ_h·(1 − cos θ_h)·δ. Expressed per
        /// equivalent angle τδ it is −0.97…−1.16 for flap chord ratios 0.3…0.2; −1.05·τδ is used.
        /// </summary>
        private static double FlapMoment(double flapEffect) => -1.05 * flapEffect;

        // ================================================================ presets

        /// <summary>
        /// NACA 0012, symmetric 12 %, from Sheldahl &amp; Klimas 1981 (SAND80-2114, Table 2): a₀ ≈ 0.11/deg, C_l,max 0.98 at
        /// Re = 3.6·10⁵ growing ≈ 0.153 per e-fold of Re (0.72 at 8·10⁴ … 1.22 at 2·10⁶), C_d0 ≈ 0.008 (Abbott &amp; von
        /// Doenhoff 1959, "Theory of Wing Sections", App. IV, alike). Checked against that table in AirfoilValidationTests.
        /// </summary>
        public static ParametricAirfoil Naca0012 => new ParametricAirfoil
        {
            Name = "NACA 0012", LiftSlope = 0.11 * 180 / Math.PI, ZeroLiftAngle = 0, ClMax = 0.98, ClMin = -0.98, Cd0 = 0.0080, ClMinDrag = 0,
            DragFactor = 0.008, Cm0 = 0, ReferenceReynolds = 3.6e5, ClMaxReynoldsSlope = 0.153, ThicknessRatio = 0.12
        };

        /// <summary>
        /// NACA 2412, 2 % camber: α₀ ≈ −2.1°, a₀ ≈ 0.105/deg, C_l,max ≈ 1.35 at Re ≈ 3·10⁵, C_m,c/4 ≈ −0.05
        /// (Abbott &amp; von Doenhoff 1959, App. IV).
        /// </summary>
        public static ParametricAirfoil Naca2412 => new ParametricAirfoil
        {
            Name = "NACA 2412", LiftSlope = 0.105 * 180 / Math.PI, ZeroLiftAngle = -2.1 * Math.PI / 180, ClMax = 1.35, ClMin = -1.0, Cd0 = 0.0090,
            ClMinDrag = 0.2, DragFactor = 0.009, Cm0 = -0.05, ReferenceReynolds = 3e5, ThicknessRatio = 0.12
        };

        /// <summary>
        /// Clark Y, flat-bottomed 11.7 % (classic RC trainer section): α₀ ≈ −3.5°, a₀ ≈ 0.1/deg, C_l,max ≈ 1.3
        /// at Re ≈ 2·10⁵, C_m,c/4 ≈ −0.08 (NACA TR-628 data; Selig et al., "Summary of Low-Speed Airfoil Data" vol. 1–3).
        /// </summary>
        public static ParametricAirfoil ClarkY => new ParametricAirfoil
        {
            Name = "Clark Y", LiftSlope = 0.100 * 180 / Math.PI, ZeroLiftAngle = -3.5 * Math.PI / 180, ClMax = 1.30, ClMin = -0.8, Cd0 = 0.0110,
            ClMinDrag = 0.35, DragFactor = 0.010, Cm0 = -0.08, ReferenceReynolds = 2e5, ThicknessRatio = 0.117
        };

        /// <summary>Thin flat plate (fins, foam tails): a₀ ≈ 0.09/deg, early stall ≈ 9° (C_l,max ≈ 0.8), Cd0 ≈ 0.015 at Re ≈ 10⁵ (Pelletier &amp; Mueller 2000, J. Aircraft 37(5)).</summary>
        public static ParametricAirfoil FlatPlate => new ParametricAirfoil
        {
            Name = "Düz plaka", LiftSlope = 0.09 * 180 / Math.PI, ZeroLiftAngle = 0, ClMax = 0.8, ClMin = -0.8, Cd0 = 0.015, ClMinDrag = 0,
            DragFactor = 0.03, Cm0 = 0, ReferenceReynolds = 1e5, ThicknessRatio = 0.03
        };

        public static IReadOnlyList<string> PresetNames { get; } = new[] { "clark-y", "naca2412", "naca0012", "flat" };

        public static ParametricAirfoil Preset(string? id) => (id ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "naca2412" or "naca-2412" or "2412" => Naca2412,
            "naca0012" or "naca-0012" or "0012" => Naca0012,
            "flat" or "plate" or "duz" or "düz" => FlatPlate,
            _ => ClarkY
        };
    }

    /// <summary>
    /// [aero] Tabulated polar (α in degrees, C_l, C_d, C_m) with linear interpolation — the hook for
    /// measured wind-tunnel data, XFOIL output or the offline CFD analysis. A flap is applied as a
    /// shift of the angle of attack by τδ. Outside the table the polar is extended with flat-plate
    /// values (C_l = sin 2α, C_d = 1.98 sin²α).
    /// </summary>
    public sealed class TabulatedAirfoil : IAirfoilPolar
    {
        private readonly double[] _a, _cl, _cd, _cm;

        public TabulatedAirfoil(string name, IEnumerable<(double AlphaDeg, double Cl, double Cd, double Cm)> rows)
        {
            Name = name;
            var list = rows.OrderBy(r => r.AlphaDeg).ToList();
            if (list.Count < 2) throw new ArgumentException("Bir kutup tablosunda en az iki satır olmalı.", nameof(rows));
            _a = list.Select(r => r.AlphaDeg * Math.PI / 180).ToArray();
            _cl = list.Select(r => r.Cl).ToArray();
            _cd = list.Select(r => r.Cd).ToArray();
            _cm = list.Select(r => r.Cm).ToArray();
            int best = 0;
            for (int i = 1; i < _cl.Length; i++) if (_cl[i] > _cl[best]) best = i;
            StallAngle = _a[best];
        }

        public string Name { get; }
        public double StallAngle { get; }

        /// <summary>Parses "alpha cl cd [cm]" lines (comments with # or ;, commas allowed), the usual XFOIL/UIUC polar text.</summary>
        public static TabulatedAirfoil Parse(string name, string text)
        {
            var rows = new List<(double, double, double, double)>();
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                var f = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 3) continue;
                if (!double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double a)) continue;
                if (!double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cl)) continue;
                if (!double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double cd)) continue;
                double cm = f.Length > 3 && double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double m) ? m : 0;
                rows.Add((a, cl, cd, cm));
            }

            return new TabulatedAirfoil(name, rows);
        }

        public SectionCoefficients Evaluate(double alpha, double flapEffect, double reynolds)
        {
            double a = alpha + flapEffect;
            if (a > Math.PI || a <= -Math.PI) a = Math.IEEERemainder(a, 2 * Math.PI);
            int n = _a.Length;
            if (a < _a[0] || a > _a[n - 1])
            {
                double s = Math.Sin(a);
                return new SectionCoefficients(Math.Sin(2 * a), 1.98 * s * s + _cd.Min(), -0.5 * Math.Sin(2 * a) * 0.25);
            }

            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (_a[mid] <= a) lo = mid;
                else hi = mid;
            }

            double t = (a - _a[lo]) / Math.Max(1e-12, _a[hi] - _a[lo]);
            return new SectionCoefficients(_cl[lo] + t * (_cl[hi] - _cl[lo]), _cd[lo] + t * (_cd[hi] - _cd[lo]), _cm[lo] + t * (_cm[hi] - _cm[lo]));
        }
    }

    /// <summary>
    /// [aero] Plain trailing-edge flap (aileron, elevator, rudder) from thin-airfoil theory (Glauert 1927;
    /// Abbott &amp; von Doenhoff §8). For a flap of chord ratio E = c_f/c hinged at x_h = (1 − E)c, with
    /// cos θ_h = 2E − 1:
    /// <list type="bullet">
    /// <item>effectiveness τ = ∂α/∂δ = 1 − (θ_h − sin θ_h)/π, reduced by η(δ) for large deflections
    /// (η = 1 to 12°, linearly to 0.55 at 40°; trend of Raymer, "Aircraft Design", Fig. 16.7 / DATCOM);</item>
    /// <item>hinge moment coefficients C_hα, C_hδ: the thin-airfoil load
    /// ΔC_p(θ) = 4[A₀(1+cos θ)/sin θ + (δ/π)·ln|sin((θ+θ_h)/2)/sin((θ−θ_h)/2)|], A₀ = α + δ(π − θ_h)/π,
    /// integrated over the flap about the hinge numerically, times a viscous correction of 0.6
    /// (boundary layers reduce measured hinge moments to roughly 50–70 % of thin-airfoil values;
    /// NACA TR-868 / ESDU 89009 trend).</item>
    /// </list>
    /// </summary>
    public static class FlapTheory
    {
        public const double ViscousHingeFactor = 0.6;

        private static readonly Dictionary<int, (double Cha, double Chd)> Cache = new Dictionary<int, (double, double)>();

        public static double HingeAngle(double chordRatio) => Math.Acos(Math.Max(-1, Math.Min(1, 2 * Math.Max(0.01, Math.Min(0.99, chordRatio)) - 1)));

        /// <summary>Flap effectiveness τ (small deflections).</summary>
        public static double Effectiveness(double chordRatio)
        {
            double th = HingeAngle(chordRatio);
            return 1 - (th - Math.Sin(th)) / Math.PI;
        }

        /// <summary>Large-deflection factor η(δ).</summary>
        public static double DeflectionFactor(double deflection)
        {
            double d = Math.Abs(deflection) * 180 / Math.PI;
            if (d <= 12) return 1.0;
            return Math.Max(0.45, 1.0 - 0.45 * (d - 12) / 28.0);
        }

        /// <summary>Equivalent change of angle of attack τ·η(δ)·δ (rad).</summary>
        public static double EquivalentAlpha(double chordRatio, double deflection) => Effectiveness(chordRatio) * DeflectionFactor(deflection) * deflection;

        /// <summary>
        /// Hinge moment coefficients (per rad, referred to flap area × flap chord, positive = trailing edge down),
        /// including the viscous correction.
        /// </summary>
        public static (double ChAlpha, double ChDelta) HingeCoefficients(double chordRatio)
        {
            int key = (int)Math.Round(Math.Max(0.01, Math.Min(0.99, chordRatio)) * 1000);
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var v)) return v;
            }

            double e = key / 1000.0;
            double cha = HingeIntegral(e, 1, 0) * ViscousHingeFactor;
            double chd = HingeIntegral(e, 0, 1) * ViscousHingeFactor;
            lock (Cache) Cache[key] = (cha, chd);
            return (cha, chd);
        }

        /// <summary>C_h = −1/(c_f²) ∫ ΔC_p (x − x_h) dx over the flap (chord c = 1), midpoint rule in θ.</summary>
        private static double HingeIntegral(double e, double alpha, double delta)
        {
            double th = HingeAngle(e);
            double xh = 1 - e;
            double a0 = alpha + delta * (Math.PI - th) / Math.PI;
            const int n = 4000;
            double sum = 0, dth = (Math.PI - th) / n;
            for (int i = 0; i < n; i++)
            {
                double t = th + (i + 0.5) * dth;
                double s = Math.Sin(t);
                double x = 0.5 * (1 - Math.Cos(t));
                double num = Math.Sin((t + th) / 2), den = Math.Sin((t - th) / 2);
                double dcp = 4 * (a0 * (1 + Math.Cos(t)) / Math.Max(1e-9, s) + delta / Math.PI * Math.Log(Math.Abs(num / Math.Max(1e-12, den))));
                sum += dcp * (x - xh) * 0.5 * s * dth;
            }

            return -sum / (e * e);
        }
    }
}
