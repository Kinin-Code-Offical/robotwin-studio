using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Aero
{
    /// <summary>
    /// Air state seen by an aerodynamic model, in the model's own frame (the frame of the object it is
    /// attached to: x forward, y left, z up for aircraft parts; z along the axis for rockets).
    /// </summary>
    public readonly struct AeroState
    {
        public AeroState(Vec3 velocity, Vec3 rates, double density, double speedOfSound, double viscosity)
        {
            Velocity = velocity;
            Rates = rates;
            Density = density;
            SpeedOfSound = speedOfSound;
            Viscosity = viscosity;
        }

        /// <summary>Velocity of the frame origin relative to the surrounding air (m/s, model frame).</summary>
        public Vec3 Velocity { get; }

        /// <summary>Angular velocity of the frame (rad/s, model frame).</summary>
        public Vec3 Rates { get; }

        public double Density { get; }
        public double SpeedOfSound { get; }

        /// <summary>Dynamic viscosity (Pa·s).</summary>
        public double Viscosity { get; }

        /// <summary>Velocity of a point of the frame relative to the air.</summary>
        public Vec3 PointVelocity(Vec3 r) => Velocity + Rates.Cross(r);
    }

    /// <summary>Aerodynamic loads in the model frame, about the frame origin.</summary>
    public struct AeroLoads
    {
        public Vec3 Force;
        public Vec3 Moment;

        /// <summary>Angle of attack / sideslip of the reference flow (rad), airspeed (m/s), dynamic pressure (Pa), Mach number.</summary>
        public double Alpha, Beta, Airspeed, DynamicPressure, Mach;

        /// <summary>Overall lift coefficient referred to the model's reference area, and the fraction of the span that is stalled.</summary>
        public double LiftCoefficient, StalledFraction;

        public static AeroLoads operator +(AeroLoads a, AeroLoads b) => new AeroLoads { Force = a.Force + b.Force, Moment = a.Moment + b.Moment };
    }

    /// <summary>
    /// [aero] A real-time aerodynamic force model. The world integration evaluates every model once per
    /// physics step and applies the result to the owning body. The offline analysis mode can replace any
    /// model with one built from CFD results (e.g. <see cref="CoefficientTableModel"/>) without touching
    /// the rest of the simulation. Implementations must be deterministic and allocation-free per call.
    /// </summary>
    public interface IAeroForceModel
    {
        string Name { get; }

        AeroLoads Evaluate(in AeroState state);
    }

    /// <summary>
    /// [aero] Whole-body aerodynamics from coefficient tables in body axes: C_X, C_Y, C_Z (forces) and
    /// C_l, C_m, C_n (moments) as functions of angle of attack α and sideslip β (degrees), optionally
    /// per Mach number, plus rate-damping derivatives. Force = q·S·C, moment = q·S·L·C. This is the
    /// interchange format for the offline CFD/wind-tunnel analysis: tabulate there, fly here.
    /// </summary>
    public sealed class CoefficientTableModel : IAeroForceModel
    {
        private readonly double[] _alpha;
        private readonly double[] _beta;
        private readonly double[,,] _c;   // [6, alpha, beta]

        /// <param name="alphaDeg">Ascending α breakpoints (deg).</param>
        /// <param name="betaDeg">Ascending β breakpoints (deg).</param>
        /// <param name="coefficients">[6, α, β]: C_X, C_Y, C_Z, C_l, C_m, C_n.</param>
        public CoefficientTableModel(string name, double referenceArea, double referenceLength, double[] alphaDeg, double[] betaDeg, double[,,] coefficients)
        {
            if (coefficients.GetLength(0) != 6 || coefficients.GetLength(1) != alphaDeg.Length || coefficients.GetLength(2) != betaDeg.Length)
                throw new ArgumentException("Katsayı tablosunun boyutu α/β kırılma noktalarıyla uyuşmuyor.", nameof(coefficients));
            Name = name;
            ReferenceArea = referenceArea;
            ReferenceLength = referenceLength;
            _alpha = alphaDeg;
            _beta = betaDeg;
            _c = coefficients;
        }

        public string Name { get; }
        public double ReferenceArea { get; }
        public double ReferenceLength { get; }

        /// <summary>Rate damping C_lp, C_mq, C_nr (per non-dimensional rate p·L/2V …).</summary>
        public Vec3 RateDamping { get; set; } = new Vec3(-0.4, -10, -0.15);

        public AeroLoads Evaluate(in AeroState s)
        {
            var v = -s.Velocity;   // air relative to the body
            double speed = v.Length;
            var loads = new AeroLoads { Airspeed = speed };
            if (speed < 1e-3) return loads;
            double alpha = Math.Atan2(-s.Velocity.Z, s.Velocity.X);
            double beta = Math.Asin(Math.Max(-1, Math.Min(1, s.Velocity.Y / speed)));
            double q = 0.5 * s.Density * speed * speed;
            double ad = alpha * 180 / Math.PI, bd = beta * 180 / Math.PI;
            Span<double> c = stackalloc double[6];
            for (int k = 0; k < 6; k++) c[k] = Interp(k, ad, bd);
            double l2v = ReferenceLength / (2 * speed);
            var p = s.Rates;
            double sq = q * ReferenceArea;
            loads.Force = new Vec3(c[0], c[1], c[2]) * sq;
            loads.Moment = new Vec3(c[3] + RateDamping.X * p.X * l2v, c[4] + RateDamping.Y * p.Y * l2v, c[5] + RateDamping.Z * p.Z * l2v) * (sq * ReferenceLength);
            loads.Alpha = alpha;
            loads.Beta = beta;
            loads.DynamicPressure = q;
            loads.Mach = speed / Math.Max(1, s.SpeedOfSound);
            loads.LiftCoefficient = -c[2] * Math.Cos(alpha) + c[0] * Math.Sin(alpha);
            return loads;
        }

        private double Interp(int k, double a, double b)
        {
            var (ia, ta) = Bracket(_alpha, a);
            var (ib, tb) = Bracket(_beta, b);
            int ia1 = Math.Min(ia + 1, _alpha.Length - 1), ib1 = Math.Min(ib + 1, _beta.Length - 1);
            double c00 = _c[k, ia, ib], c10 = _c[k, ia1, ib], c01 = _c[k, ia, ib1], c11 = _c[k, ia1, ib1];
            return (c00 * (1 - ta) + c10 * ta) * (1 - tb) + (c01 * (1 - ta) + c11 * ta) * tb;
        }

        private static (int, double) Bracket(double[] x, double v)
        {
            if (x.Length == 1 || v <= x[0]) return (0, 0);
            if (v >= x[x.Length - 1]) return (x.Length - 1, 0);
            int i = 0;
            while (i < x.Length - 2 && x[i + 1] < v) i++;
            return (i, (v - x[i]) / (x[i + 1] - x[i]));
        }
    }

    /// <summary>A trailing-edge control surface on part of a <see cref="LiftingSurface"/>.</summary>
    public sealed class ControlSurface
    {
        public string Name { get; set; } = "Kanatçık";

        /// <summary>Spanwise extent in the surface's span coordinate η (−1 … +1 for symmetric surfaces, 0 … 1 otherwise).</summary>
        public double EtaStart { get; set; }

        public double EtaEnd { get; set; } = 1;

        /// <summary>Flap chord / local chord.</summary>
        public double ChordRatio { get; set; } = 0.25;

        /// <summary>Deflection (rad): + moves the trailing edge so that the surface's lift (along its normal) increases.</summary>
        public double Deflection { get; set; }

        /// <summary>Mechanical travel limit (rad).</summary>
        public double MaxDeflection { get; set; } = 30 * Math.PI / 180;

        /// <summary>Aerodynamic hinge moment of the last evaluation (N·m, + = pushes towards + deflection).</summary>
        public double HingeMoment { get; internal set; }

        internal bool Covers(double eta) => eta >= Math.Min(EtaStart, EtaEnd) && eta <= Math.Max(EtaStart, EtaEnd);
    }

    /// <summary>
    /// [aero] A lifting surface (wing, horizontal/vertical tail, canard, fin) by strip theory with a
    /// lifting-line induced-angle correction:
    /// <list type="bullet">
    /// <item>The span is cut into strips; each sees the local air velocity V + ω×r (so roll/pitch/yaw
    /// damping and asymmetric stall come out of the geometry), minus the spanwise component
    /// (simple sweep theory).</item>
    /// <item>Strip α_e = α − α_i with α_i = C_L/(π·e·AR) (Prandtl lifting line), solved by fixed-point
    /// iteration; this gives the finite-wing lift slope a₀/(1 + a₀/(π·e·AR)) and induced drag C_L²/(π·e·AR).</item>
    /// <item>Section coefficients from an <see cref="IAirfoilPolar"/> over ±180° (stall and post-stall).</item>
    /// <item>Control surfaces shift α by τ·η(δ)·δ (<see cref="FlapTheory"/>) and report their hinge moment;
    /// plain-flap profile drag increment ΔC_D = 0.0144·E·(|δ|−10°) per degree (Raymer, Eq. 12.61).</item>
    /// <item>Optional downwash from a wing ahead: ε = 2·C_L,w/(π·AR_w)·κ (far-field lifting-line value, κ ≈ 1).</item>
    /// </list>
    /// Geometry is given in the frame of the object the surface belongs to.
    /// </summary>
    public sealed class LiftingSurface : IAeroForceModel
    {
        private Strip[] _strips = Array.Empty<Strip>();
        private bool _dirty = true;
        private double _area, _ar;

        private struct Strip
        {
            public Vec3 R, C, N, S;
            public double Chord, Area, Eta;
            public int Control;
            public double AlphaI;
        }

        public string Name { get; set; } = "Kanat";

        /// <summary>Quarter-chord point of the root (for symmetric surfaces the centre line).</summary>
        public Vec3 Root { get; set; }

        /// <summary>Forward chord direction (leading edge side).</summary>
        public Vec3 ChordAxis { get; set; } = Vec3.UnitX;

        /// <summary>Span direction (towards the η &gt; 0 tip). The surface normal is Chord × Span.</summary>
        public Vec3 SpanAxis { get; set; } = Vec3.UnitY;

        /// <summary>Tip-to-tip span for symmetric surfaces, root-to-tip otherwise (m).</summary>
        public double Span { get; set; } = 1.0;

        public double RootChord { get; set; } = 0.2;
        public double TipChord { get; set; } = 0.2;

        /// <summary>Quarter-chord sweep (rad, + = tips aft).</summary>
        public double Sweep { get; set; }

        /// <summary>Dihedral (rad, + = tips towards the normal).</summary>
        public double Dihedral { get; set; }

        /// <summary>Incidence relative to the chord axis (rad, + = leading edge towards the normal).</summary>
        public double Incidence { get; set; }

        /// <summary>Mirrored about the root (main wings, horizontal tails); false for a single panel (vertical fin).</summary>
        public bool Symmetric { get; set; } = true;

        /// <summary>Strips per half span.</summary>
        public int StripsPerSide { get; set; } = 4;

        /// <summary>Oswald span efficiency e.</summary>
        public double Oswald { get; set; } = 0.85;

        /// <summary>Aspect ratio override (0 = from the planform; a single panel on a fuselage uses 1.55·b²/S, the end-plate effect).</summary>
        public double AspectRatioOverride { get; set; }

        public IAirfoilPolar Airfoil { get; set; } = ParametricAirfoil.ClarkY;

        public List<ControlSurface> Controls { get; } = new List<ControlSurface>();

        /// <summary>Wing whose downwash this surface flies in (horizontal tails), and the downwash factor κ.</summary>
        public LiftingSurface? DownwashSource { get; set; }

        public double DownwashFactor { get; set; } = 1.0;

        /// <summary>Planform area (m²).</summary>
        public double Area
        {
            get
            {
                Build();
                return _area;
            }
        }

        public double AspectRatio
        {
            get
            {
                Build();
                return _ar;
            }
        }

        /// <summary>Lift coefficient of the last evaluation (referred to <see cref="Area"/>).</summary>
        public double LastLiftCoefficient { get; private set; }

        /// <summary>Strip quarter-chord points and their last local lift coefficients (for drawing and inspection).</summary>
        public IEnumerable<(Vec3 Point, double Cl, bool Stalled)> StripStates()
        {
            Build();
            for (int i = 0; i < _strips.Length; i++) yield return (_strips[i].R, _lastCl.Length > i ? _lastCl[i] : 0, _lastStall.Length > i && _lastStall[i]);
        }

        private double[] _lastCl = Array.Empty<double>();
        private bool[] _lastStall = Array.Empty<bool>();

        /// <summary>Marks the geometry changed (call after editing properties once it has been evaluated).</summary>
        public void Invalidate() => _dirty = true;

        private void Build()
        {
            if (!_dirty) return;
            _dirty = false;
            var c0 = ChordAxis.Normalized();
            var s0 = (SpanAxis - c0 * SpanAxis.Dot(c0)).Normalized();
            var n0 = c0.Cross(s0);
            int per = Math.Max(1, StripsPerSide);
            var list = new List<Strip>();
            double half = Symmetric ? Span / 2 : Span;
            double cg = Math.Cos(Dihedral), sg = Math.Sin(Dihedral);
            double ci = Math.Cos(Incidence), si = Math.Sin(Incidence);
            foreach (int side in Symmetric ? new[] { -1, 1 } : new[] { 1 })
            {
                var s = s0 * (side * cg) + n0 * sg;            // outward span direction of this half
                var n = n0 * cg - s0 * (side * sg);            // its normal
                var c = c0 * ci + n * si;
                var nn = n * ci - c0 * si;
                for (int k = 0; k < per; k++)
                {
                    double e0 = (double)k / per, e1 = (double)(k + 1) / per, em = (e0 + e1) / 2;
                    double y = em * half;
                    double chord = RootChord + (TipChord - RootChord) * em;
                    var r = Root + s * y - c0 * (y * Math.Tan(Sweep));
                    var st = new Strip
                    {
                        R = r, C = c, N = nn, S = s, Chord = chord, Area = chord * (e1 - e0) * half, Eta = side * em, Control = -1
                    };
                    for (int j = 0; j < Controls.Count; j++)
                    {
                        if (Controls[j].Covers(st.Eta)) st.Control = j;
                    }

                    list.Add(st);
                }
            }

            _strips = list.ToArray();
            _lastCl = new double[_strips.Length];
            _lastStall = new bool[_strips.Length];
            _area = 0;
            foreach (var st in _strips) _area += st.Area;
            double b = Symmetric ? Span : Span;
            _ar = AspectRatioOverride > 0 ? AspectRatioOverride : Symmetric ? b * b / Math.Max(1e-9, _area) : 1.55 * b * b / Math.Max(1e-9, _area);
            if (Airfoil is ParametricAirfoil pa && Math.Abs(pa.AspectRatio - _ar) > 1e-6) Airfoil = pa.WithAspectRatio(_ar);
        }

        public AeroLoads Evaluate(in AeroState state)
        {
            Build();
            var loads = new AeroLoads();
            foreach (var cs in Controls) cs.HingeMoment = 0;
            double k = 1.0 / (Math.PI * Math.Max(0.3, Oswald) * Math.Max(0.5, _ar));
            double eps = DownwashSource != null ? DownwashFactor * 2 * DownwashSource.LastLiftCoefficient / (Math.PI * Math.Max(0.5, DownwashSource.AspectRatio)) : 0;
            double stallAngle = Airfoil.StallAngle;
            double liftSum = 0, refQ = 0, stalledArea = 0;
            var vref = state.Velocity;
            double speedRef = vref.Length;
            for (int i = 0; i < _strips.Length; i++)
            {
                ref var st = ref _strips[i];
                var u = -state.PointVelocity(st.R);                 // air velocity relative to the strip
                u -= st.S * u.Dot(st.S);                           // spanwise flow does not lift
                double uc = -u.Dot(st.C), un = u.Dot(st.N);
                double v2 = uc * uc + un * un;
                if (v2 < 1e-6)
                {
                    _lastCl[i] = 0;
                    continue;
                }

                double speed = Math.Sqrt(v2);
                double alpha = Math.Atan2(un, uc) - eps;
                double q = 0.5 * state.Density * v2;
                double re = state.Density * speed * st.Chord / Math.Max(1e-7, state.Viscosity);
                ControlSurface? ctl = st.Control >= 0 ? Controls[st.Control] : null;
                double delta = ctl == null ? 0 : Math.Max(-ctl.MaxDeflection, Math.Min(ctl.MaxDeflection, ctl.Deflection));
                double flap = ctl == null ? 0 : FlapTheory.EquivalentAlpha(ctl.ChordRatio, delta);
                // Lifting-line induced angle, damped fixed point starting from the previous step's value.
                double ai = st.AlphaI;
                SectionCoefficients sc = default;
                for (int it = 0; it < 3; it++)
                {
                    sc = Airfoil.Evaluate(alpha - ai, flap, re);
                    double target = sc.Cl * k;
                    target = Math.Max(-0.35, Math.Min(0.35, target));
                    ai += 0.7 * (target - ai);
                }

                st.AlphaI = ai;
                double ae = alpha - ai;
                // Section forces in the strip's flow frame (rotated by the induced angle: induced drag).
                double cl = sc.Cl, cd = sc.Cd;
                if (ctl != null && Math.Abs(delta) > 10 * Math.PI / 180)
                    cd += 0.0144 * ctl.ChordRatio * (Math.Abs(delta) - 10 * Math.PI / 180) * 180 / Math.PI;
                double ca = Math.Cos(alpha + eps), sa = Math.Sin(alpha + eps);
                // Lift ⟂ local flow (incl. downwash), drag along it; the induced angle tilts the section force back.
                var drag = st.C * -ca + st.N * sa;       // unit vector along the air flow
                var lift = st.C * sa + st.N * ca;        // unit vector ⟂ flow, = normal at α = 0
                double cai = Math.Cos(ai + eps), sai = Math.Sin(ai + eps);
                // Downwash turns the effective flow down by α_i: the section lift tilts back towards the
                // drag direction (that tilt IS the induced drag D_i = L·sin α_i) and the drag turns with the flow.
                var liftEff = lift * cai + drag * sai;
                var dragEff = drag * cai - lift * sai;
                double qs = q * st.Area;
                var f = liftEff * (cl * qs) + dragEff * (cd * qs);
                var m = st.C.Cross(st.N) * (sc.Cm * qs * st.Chord);
                loads.Force += f;
                loads.Moment += st.R.Cross(f) + m;
                liftSum += cl * qs;
                refQ += qs;
                bool stalled = Math.Abs(ae + flap) > Math.Abs(stallAngle) + 0.02 && Math.Abs(cl) > 0;
                _lastCl[i] = cl;
                _lastStall[i] = stalled;
                if (stalled) stalledArea += st.Area;
                if (ctl != null)
                {
                    var (cha, chd) = FlapTheory.HingeCoefficients(ctl.ChordRatio);
                    double aLim = Math.Max(-0.3, Math.Min(0.3, ae));
                    double ch = cha * aLim + chd * delta;
                    double cf = ctl.ChordRatio * st.Chord;
                    ctl.HingeMoment += ch * q * (st.Area * ctl.ChordRatio) * cf;
                }
            }

            LastLiftCoefficient = refQ > 0 ? liftSum / refQ : 0;
            loads.LiftCoefficient = LastLiftCoefficient;
            loads.StalledFraction = _area > 0 ? stalledArea / _area : 0;
            loads.Airspeed = speedRef;
            loads.DynamicPressure = 0.5 * state.Density * speedRef * speedRef;
            loads.Mach = speedRef / Math.Max(1, state.SpeedOfSound);
            if (speedRef > 1e-6)
            {
                var c0 = ChordAxis.Normalized();
                var s0 = (SpanAxis - c0 * SpanAxis.Dot(c0)).Normalized();
                var n0 = c0.Cross(s0);
                var u = -vref;
                loads.Alpha = Math.Atan2(u.Dot(n0), -u.Dot(c0)) + Incidence;
                loads.Beta = Math.Asin(Math.Max(-1, Math.Min(1, u.Dot(s0) / speedRef)));
            }

            return loads;
        }
    }
}
