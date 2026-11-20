using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Aero
{
    public enum RocketComponentKind
    {
        NoseCone,
        BodyTube,
        FinSet
    }

    public enum NoseShape
    {
        Ogive,
        Cone,
        Parabolic,
        Elliptical
    }

    /// <summary>
    /// [aero] One component of a rocket airframe with Barrowman normal-force aerodynamics
    /// (Barrowman &amp; Barrowman 1966, "The Theoretical Prediction of the Center of Pressure", NARAM-8;
    /// Barrowman 1967 thesis) extended to large angles with crossflow (Galejs 1999, "Wind instability")
    /// and zero-lift drag from skin friction and base drag (Niskanen 2009, "Development of an Open Source
    /// model rocket simulation software", OpenRocket technical documentation, ch. 3).
    /// <para>Frame: the rocket axis is the local +Z (towards the nose), z = 0 at the bottom of the
    /// component. Each component applies its own normal force at its own centre of pressure, so the
    /// assembly's total C_Nα and CP equal Barrowman's sums, and pitch damping comes from the local
    /// flow at each component (ω × r).</para>
    /// <list type="bullet">
    /// <item>Nose: C_Nα = 2; CP from the tip at 2/3·L (cone), 0.466·L (ogive), 1/2·L (parabolic), 1/3·L (elliptical).</item>
    /// <item>Fins (3 or 4): C_Nα = [1 + R/(s+R)]·4N(s/d)²/(1 + √(1 + (2l_m/(C_r+C_t))²)), CP from the root
    /// leading edge X_s(C_r+2C_t)/(3(C_r+C_t)) + [(C_r+C_t) − C_rC_t/(C_r+C_t)]/6; Prandtl–Glauert 1/√(1−M²).</item>
    /// <item>Normal force C_N = C_Nα·sin α·cos α + K·(A_plan/A_ref)·sin²α (K = 1.1 body, 1.2 fins) at large α.</item>
    /// <item>Skin friction C_f = 0.074·Re^−0.2 (turbulent) or 1.328/√Re (laminar, Re &lt; 5·10⁵), ×(1 − 0.1M²);
    /// body factor (1 + 1/(2f_B)), fin factor (1 + 2t/c); base drag 0.12 + 0.13M² on the aft-most component.</item>
    /// </list>
    /// </summary>
    public sealed class RocketComponentAero : IAeroForceModel
    {
        public string Name { get; set; } = "Roket parçası";
        public RocketComponentKind Kind { get; set; }

        /// <summary>Length along the axis (nose length, tube length, fin root chord).</summary>
        public double Length { get; set; } = 0.1;

        /// <summary>Body diameter (m) at this component (fins: the tube they sit on).</summary>
        public double Diameter { get; set; } = 0.025;

        public NoseShape Nose { get; set; } = NoseShape.Ogive;

        // fins
        public int FinCount { get; set; } = 3;
        public double RootChord { get; set; } = 0.05;
        public double TipChord { get; set; } = 0.025;
        public double FinSpan { get; set; } = 0.04;

        /// <summary>Axial distance from the root leading edge to the tip leading edge (m).</summary>
        public double FinSweep { get; set; } = 0.02;

        public double FinThickness { get; set; } = 0.002;

        /// <summary>Total rocket length (Reynolds number and fineness), set by the assembly.</summary>
        public double RocketLength { get; set; } = 0.5;

        /// <summary>This component ends the airframe (carries the base drag).</summary>
        public bool IsAft { get; set; }

        public double ReferenceArea => Math.PI * Diameter * Diameter / 4;

        /// <summary>Barrowman normal-force slope (per rad, referred to <see cref="ReferenceArea"/>), incompressible.</summary>
        public double NormalForceSlope
        {
            get
            {
                switch (Kind)
                {
                    case RocketComponentKind.NoseCone:
                        return 2.0;
                    case RocketComponentKind.FinSet:
                    {
                        double r = Diameter / 2, s = FinSpan, d = Diameter;
                        double lm = Math.Sqrt(s * s + Math.Pow(FinSweep + TipChord / 2 - RootChord / 2, 2));
                        double k = 1 + r / (s + r);
                        double n = FinCount;
                        double cn = 4 * n * (s / d) * (s / d) / (1 + Math.Sqrt(1 + Math.Pow(2 * lm / Math.Max(1e-6, RootChord + TipChord), 2)));
                        if (FinCount != 3 && FinCount != 4) cn *= 1.0;   // Barrowman is derived for 3–4 fins; others scale ∝ N already
                        return k * cn;
                    }

                    default:
                        return 0;
                }
            }
        }

        /// <summary>Centre of pressure (local z, m).</summary>
        public double CenterOfPressure
        {
            get
            {
                switch (Kind)
                {
                    case RocketComponentKind.NoseCone:
                        double f = Nose switch { NoseShape.Cone => 2.0 / 3, NoseShape.Parabolic => 0.5, NoseShape.Elliptical => 1.0 / 3, _ => 0.466 };
                        return Length - f * Length;
                    case RocketComponentKind.FinSet:
                    {
                        double cr = RootChord, ct = TipChord, xs = FinSweep;
                        double xf = xs * (cr + 2 * ct) / (3 * (cr + ct)) + ((cr + ct) - cr * ct / (cr + ct)) / 6;
                        return cr - xf;
                    }

                    default:
                        return Length / 2;
                }
            }
        }

        /// <summary>Planform (side-view) area for the crossflow term (m²).</summary>
        public double PlanformArea => Kind switch
        {
            RocketComponentKind.FinSet => 0.5 * (RootChord + TipChord) * FinSpan * Math.Max(1, FinCount / 2.0),
            RocketComponentKind.NoseCone => 0.5 * Diameter * Length,
            _ => Diameter * Length
        };

        public double WettedArea => Kind switch
        {
            RocketComponentKind.FinSet => 2 * FinCount * 0.5 * (RootChord + TipChord) * FinSpan,
            RocketComponentKind.NoseCone => Math.PI * Diameter / 2 * Math.Sqrt(Diameter * Diameter / 4 + Length * Length) * (Nose == NoseShape.Cone ? 1 : 1.1),
            _ => Math.PI * Diameter * Length
        };

        /// <summary>Zero-lift drag coefficient contribution referred to <see cref="ReferenceArea"/>.</summary>
        public double ZeroLiftDrag(double reynolds, double mach)
        {
            double re = Math.Max(1e4, reynolds);
            double cf = re < 5e5 ? 1.328 / Math.Sqrt(re) : 0.074 * Math.Pow(re, -0.2);
            cf *= 1 - 0.1 * Math.Min(0.8, mach) * Math.Min(0.8, mach);
            double form = Kind == RocketComponentKind.FinSet
                ? 1 + 2 * FinThickness / Math.Max(1e-4, 0.5 * (RootChord + TipChord))
                : 1 + 1 / (2 * Math.Max(1, RocketLength / Math.Max(1e-4, Diameter)));
            double cd = cf * form * WettedArea / ReferenceArea;
            if (IsAft && Kind != RocketComponentKind.FinSet) cd += 0.12 + 0.13 * Math.Min(1, mach) * Math.Min(1, mach);
            return cd;
        }

        public AeroLoads Evaluate(in AeroState state)
        {
            var loads = new AeroLoads();
            var rcp = new Vec3(0, 0, CenterOfPressure);
            var u = -state.PointVelocity(rcp);                       // air relative to the component
            double speed = u.Length;
            if (speed < 1e-4) return loads;
            double ua = u.Z;
            var cross = new Vec3(u.X, u.Y, 0);
            double uc = cross.Length;
            double alpha = Math.Atan2(uc, -ua);                      // 0 = flying nose first
            double q = 0.5 * state.Density * speed * speed;
            double mach = speed / Math.Max(1, state.SpeedOfSound);
            double beta = Math.Sqrt(Math.Max(0.36, 1 - mach * mach));
            double aref = ReferenceArea;
            double sa = Math.Sin(alpha), ca = Math.Cos(alpha);
            double kCross = Kind == RocketComponentKind.FinSet ? 1.2 : 1.1;
            double cn = NormalForceSlope / beta * sa * ca + kCross * PlanformArea / aref * sa * sa;
            var force = Vec3.Zero;
            if (uc > 1e-9) force += cross * (q * aref * cn / uc);   // pushed along the crossflow
            double re = state.Density * speed * RocketLength / Math.Max(1e-7, state.Viscosity);
            double cd = ZeroLiftDrag(re, mach) * Math.Abs(ca);
            force += u * (q * aref * cd / speed);
            loads.Force = force;
            loads.Moment = rcp.Cross(force);
            loads.Alpha = alpha;
            loads.Airspeed = speed;
            loads.DynamicPressure = q;
            loads.Mach = mach;
            loads.LiftCoefficient = cn;
            return loads;
        }
    }

    /// <summary>
    /// [aero] Recovery parachute. Inflation follows the filling-time model of Knacke ("Parachute
    /// Recovery Systems Design Manual", 1992, §5): t_f = n·D₀/v at line stretch with n ≈ 8 for solid
    /// flat/hemispherical canopies, and the drag area growing as (C_D·S)(t) = (C_D·S)₀·(t/t_f)² until fully
    /// open. The opening shock is not prescribed: it is the force the inflating canopy produces on the
    /// decelerating airframe (reported as <see cref="PeakForce"/>). C_D ≈ 0.75 (flat circular, Knacke Table 5-1).
    /// </summary>
    public sealed class ParachuteModel
    {
        public double Diameter { get; set; } = 0.45;
        public double DragCoefficient { get; set; } = 0.75;
        public double FillConstant { get; set; } = 8;

        /// <summary>Delay from the ejection to line stretch (canopy out of the tube), s.</summary>
        public double DeployDelay { get; set; } = 0.15;

        public bool Deployed { get; private set; }
        public double DeployTime { get; private set; } = double.NaN;
        public double FillTime { get; private set; } = double.NaN;
        public double PeakForce { get; private set; }
        public double Force { get; private set; }

        /// <summary>Fully open drag area (m²).</summary>
        public double FullDragArea => DragCoefficient * Math.PI * Diameter * Diameter / 4;

        public void Deploy(double time)
        {
            if (Deployed) return;
            Deployed = true;
            DeployTime = time;
        }

        /// <summary>Current drag area at a time, given the airspeed at line stretch.</summary>
        public double DragArea(double time, double airspeed)
        {
            if (!Deployed || time < DeployTime + DeployDelay) return 0;
            if (double.IsNaN(FillTime)) FillTime = FillConstant * Diameter / Math.Max(2.0, airspeed);
            double t = (time - DeployTime - DeployDelay) / FillTime;
            return t >= 1 ? FullDragArea : FullDragArea * t * t;
        }

        /// <summary>Drag force (N) on the attachment for air velocity relative to it (m/s).</summary>
        public Vec3 DragForce(double time, Vec3 airRelative, double density)
        {
            double sp = airRelative.Length;
            double cda = DragArea(time, sp);
            var f = airRelative * (0.5 * density * cda * sp);
            Force = f.Length;
            if (Force > PeakForce) PeakForce = Force;
            return f;
        }

        public bool FullyOpen(double time) => Deployed && !double.IsNaN(FillTime) && time >= DeployTime + DeployDelay + FillTime;
    }
}
