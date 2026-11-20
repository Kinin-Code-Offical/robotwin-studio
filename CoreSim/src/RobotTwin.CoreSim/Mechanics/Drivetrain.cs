using System;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Brush tyre model with combined slip and first-order relaxation (Pacejka, "Tire and
    /// Vehicle Dynamics", ch. 3 and 7). The tread deflections zx, zy obey
    /// ż = v_slip − |Vx|/σ·z, so the transient slips κ' = zx/σ and tan α' = zy/σ approach the
    /// steady slip ratio κ = (ωR − Vx)/|Vx| and slip angle tan α = −Vy/|Vx| within one
    /// relaxation length σ of rolling. At standstill the deflection acts as a spring, which
    /// gives true static friction (no creep on slopes). The force follows the isotropic brush
    /// characteristic F = μN(3θσ − 3θ²σ² + θ³σ³), θ = C/(3μ), up to full sliding, after which it
    /// falls from the static to the kinetic friction level.
    /// </summary>
    public sealed class BrushTyre
    {
        /// <summary>Longitudinal slip stiffness per unit load, C_κ/N (1 per unit slip).</summary>
        public double SlipStiffness { get; set; } = 12.0;

        /// <summary>Cornering stiffness per unit load, C_α/N (1/rad).</summary>
        public double CorneringStiffness { get; set; } = 10.0;

        /// <summary>Relaxation length σ (m): about the contact half-length; a few millimetres for small hard robot tyres.</summary>
        public double RelaxationLength { get; set; } = 0.006;

        /// <summary>Damping ratio of the tread spring at standstill.</summary>
        public double LowSpeedDampingRatio { get; set; } = 0.7;

        /// <summary>Below this rolling speed the low-speed damping fades in (m/s).</summary>
        public double LowSpeedThreshold { get; set; } = 0.15;

        public double DeflectionX { get; private set; }
        public double DeflectionY { get; private set; }
        public double LongitudinalForce { get; private set; }
        public double LateralForce { get; private set; }

        /// <summary>Transient slip ratio κ' and slip angle tan α'.</summary>
        public double SlipRatio => DeflectionX / RelaxationLength;

        public double SlipAngle => Math.Atan(DeflectionY / RelaxationLength);
        public bool Sliding { get; private set; }

        public void Reset()
        {
            DeflectionX = DeflectionY = LongitudinalForce = LateralForce = 0;
            Sliding = false;
        }

        /// <summary>
        /// Normalised brush characteristic F/N for combined slip <paramref name="s"/> ≥ 0.
        /// </summary>
        public static double Characteristic(double s, double stiffness, double muStatic, double muKinetic)
        {
            if (muStatic <= 0) return 0;
            double theta = stiffness / (3 * muStatic);
            double x = theta * s;
            if (x < 1) return muStatic * (3 * x - 3 * x * x + x * x * x);
            return muKinetic + (muStatic - muKinetic) * Math.Exp(-2 * (x - 1));
        }

        /// <summary>
        /// Advances the tread state by <paramref name="h"/> and returns the tyre forces in the
        /// contact frame (x along the rolling direction, y lateral).
        /// </summary>
        /// <param name="normal">Normal load N (≥ 0).</param>
        /// <param name="vx">Contact-point velocity along the rolling direction.</param>
        /// <param name="vy">Contact-point velocity along the lateral direction.</param>
        /// <param name="surfaceSpeed">Tread speed ω·R.</param>
        public (double Fx, double Fy) Update(double normal, double vx, double vy, double surfaceSpeed, double muStatic, double muKinetic, double h)
        {
            if (normal <= 0)
            {
                // Tread relaxes freely in the air.
                DeflectionX *= Math.Exp(-h / 0.01);
                DeflectionY *= Math.Exp(-h / 0.01);
                LongitudinalForce = LateralForce = 0;
                Sliding = false;
                return (0, 0);
            }

            double sigma = Math.Max(1e-4, RelaxationLength);
            double sx = surfaceSpeed - vx, sy = -vy;
            double rolling = Math.Abs(vx);
            double a = rolling / sigma;
            // Exact update of ż = s − a·z for constant inputs over the step.
            double zx0 = DeflectionX, zy0 = DeflectionY;
            if (a * h > 1e-9)
            {
                double e = Math.Exp(-a * h), g = (1 - e) / a;
                DeflectionX = zx0 * e + sx * g;
                DeflectionY = zy0 * e + sy * g;
            }
            else
            {
                DeflectionX = zx0 + sx * h;
                DeflectionY = zy0 + sy * h;
            }

            double ck = SlipStiffness, ca = CorneringStiffness, ratio = ca / Math.Max(1e-9, ck);
            // The bristles cannot deflect beyond the point where the whole patch slides:
            // clamp the deflection at the sliding onset s = 3μ/C (no wind-up, no unwinding lag).
            double onset = 3 * Math.Max(muStatic, 1e-6) / Math.Max(1e-9, ck);
            double ex = DeflectionX / sigma, ey = DeflectionY / sigma * ratio;
            double s = Math.Sqrt(ex * ex + ey * ey);
            if (s > onset)
            {
                DeflectionX *= onset / s;
                DeflectionY *= onset / s;
                ex *= onset / s;
                ey *= onset / s;
                s = onset;
            }

            // Once sliding, the level follows the actual slip (falling from μs towards μk).
            double fx = 0, fy = 0;
            double effective = s;
            Sliding = s >= onset * 0.999;
            if (Sliding)
            {
                double vref = Math.Max(rolling, 0.05);
                double ix = sx / vref, iy = sy / vref * ratio;
                effective = Math.Max(s, Math.Sqrt(ix * ix + iy * iy));
            }

            if (s > 1e-12)
            {
                double f = Characteristic(effective, ck, muStatic, muKinetic) * normal;
                fx = f * ex / s;
                fy = f * ey / s;
            }

            double adhesion = Math.Max(0, 1 - s / onset);

            // Tread damping at low speed (the standstill spring would otherwise ring); only while gripping.
            double ck2 = ck;
            double fade = Math.Max(0, 1 - rolling / Math.Max(1e-6, LowSpeedThreshold)) * adhesion;
            if (fade > 0)
            {
                double k = ck2 * normal / sigma;
                double c = 2 * LowSpeedDampingRatio * Math.Sqrt(k * normal / 9.81) * fade;
                fx += c * sx;
                fy += c * sy * ratio;
            }

            // Friction circle.
            double limit = muStatic * normal, mag = Math.Sqrt(fx * fx + fy * fy);
            if (mag > limit && mag > 0)
            {
                fx *= limit / mag;
                fy *= limit / mag;
            }

            LongitudinalForce = fx;
            LateralForce = fy;
            return (fx, fy);
        }
    }

    /// <summary>
    /// Gear train between a motor's output and a wheel: ratio and efficiency (the circuit's
    /// <see cref="Analog.Devices.DcMotor"/> applies them), plus backlash. While the gap is
    /// closed the wheel turns with the gear and the mesh carries torque; when the mesh torque
    /// would change sign the teeth separate, the wheel runs free until the gear has crossed
    /// the gap, then the teeth engage again on the other flank.
    /// </summary>
    public sealed class Gearbox
    {
        /// <summary>Total free play at the output shaft (rad).</summary>
        public double Backlash { get; set; }

        /// <summary>Mesh efficiency (power forward).</summary>
        public double Efficiency { get; set; } = 1.0;

        /// <summary>Gear angle minus wheel angle, within ±Backlash/2.</summary>
        public double Gap { get; private set; }

        /// <summary>+1 or −1 when a flank is in contact, 0 while crossing the gap.</summary>
        public int Engaged { get; private set; } = 1;

        public double WheelSpeed { get; private set; }

        /// <summary>Torque the gear exerts on the wheel (output shaft side) in the last step.</summary>
        public double MeshTorque { get; private set; }

        public void Reset(double wheelSpeed = 0)
        {
            Gap = Backlash / 2;
            Engaged = 1;
            WheelSpeed = wheelSpeed;
            MeshTorque = 0;
        }

        /// <summary>The wheel turns with the gear (no backlash, or a flank in contact).</summary>
        public bool Coupled => Engaged != 0 || Backlash <= 0;

        /// <summary>
        /// Co-simulation update after a mechanics step in which the wheel either followed the gear
        /// (coupled) or spun freely at <paramref name="wheelSpeed"/>: decides whether the teeth
        /// separate or engage for the next step.
        /// </summary>
        public void Update(double gearSpeed, double previousGearSpeed, double wheelSpeed, double loadTorque, double wheelInertia, double h)
        {
            double half = Math.Max(0, Backlash) / 2;
            if (half <= 0)
            {
                Engaged = 1;
                MeshTorque = loadTorque + wheelInertia * (gearSpeed - previousGearSpeed) / h;
                WheelSpeed = gearSpeed;
                return;
            }

            if (Engaged != 0)
            {
                double mesh = loadTorque + wheelInertia * (gearSpeed - previousGearSpeed) / h;
                WheelSpeed = gearSpeed;
                if (mesh * Engaged < 0)
                {
                    Engaged = 0;
                    MeshTorque = 0;
                }
                else MeshTorque = mesh;

                return;
            }

            WheelSpeed = wheelSpeed;
            MeshTorque = 0;
            Gap += (gearSpeed - wheelSpeed) * h;
            if (Gap >= half)
            {
                Gap = half;
                Engaged = 1;
            }
            else if (Gap <= -half)
            {
                Gap = -half;
                Engaged = -1;
            }
        }

        /// <summary>
        /// Advances by <paramref name="h"/>: the gear (output shaft) turns at
        /// <paramref name="gearSpeed"/> (was <paramref name="previousGearSpeed"/>), the wheel of
        /// inertia <paramref name="wheelInertia"/> feels <paramref name="loadTorque"/> from the
        /// ground (opposing positive spin when positive). Returns the wheel speed.
        /// </summary>
        public double Step(double gearSpeed, double previousGearSpeed, double loadTorque, double wheelInertia, double h)
        {
            double half = Math.Max(0, Backlash) / 2;
            if (half <= 0)
            {
                Engaged = 1;
                Gap = 0;
                MeshTorque = loadTorque + wheelInertia * (gearSpeed - previousGearSpeed) / h;
                WheelSpeed = gearSpeed;
                return WheelSpeed;
            }

            if (Engaged != 0)
            {
                double mesh = loadTorque + wheelInertia * (gearSpeed - previousGearSpeed) / h;
                if (mesh * Engaged < 0)
                {
                    // Teeth separate: the wheel coasts.
                    Engaged = 0;
                    MeshTorque = 0;
                }
                else
                {
                    MeshTorque = mesh;
                    WheelSpeed = gearSpeed;
                    Gap = Engaged * half;
                    return WheelSpeed;
                }
            }

            // Free wheel: ground load and inertia only.
            WheelSpeed -= loadTorque / Math.Max(wheelInertia, 1e-12) * h;
            Gap += (gearSpeed - WheelSpeed) * h;
            if (Gap >= half)
            {
                Gap = half;
                Engaged = 1;
                WheelSpeed = gearSpeed;
            }
            else if (Gap <= -half)
            {
                Gap = -half;
                Engaged = -1;
                WheelSpeed = gearSpeed;
            }

            MeshTorque = 0;
            return WheelSpeed;
        }

        /// <summary>Typical efficiency of a spur gearbox: about 4:1 per stage at 90 % (plastic) or 92 % (metal) per stage.</summary>
        public static double TypicalEfficiency(double ratio, bool metal = false)
        {
            if (ratio <= 1.0001) return 1.0;
            int stages = Math.Max(1, (int)Math.Ceiling(Math.Log(ratio) / Math.Log(4.0) - 1e-9));
            return Math.Pow(metal ? 0.92 : 0.90, stages);
        }
    }

    /// <summary>
    /// Hobby servo as a geared DC motor under proportional control: duty u = clamp(e/Band),
    /// zero inside the dead band; torque τ = τ_stall·(u·V/V_rated) − (τ_stall/ω₀)·ω − τ_friction,
    /// limited to the stall torque. This reproduces the datasheet stall torque, no-load speed
    /// and the supply current (proportional to motor torque) of an SG90-class servo.
    /// </summary>
    public sealed class ServoMotor
    {
        /// <summary>SG90: 1.8 kg·cm at 4.8 V.</summary>
        public double StallTorque { get; set; } = 0.18;

        /// <summary>Datasheet no-load speed at the rated voltage (rad/s); SG90 0.1 s/60°.</summary>
        public double NoLoadSpeed { get; set; } = Math.PI / 3 / 0.1;

        public double RatedVoltage { get; set; } = 4.8;
        public double SupplyVoltage { get; set; } = 4.8;

        /// <summary>Error at which the drive reaches full duty (rad).</summary>
        public double ProportionalBand { get; set; } = 4 * Math.PI / 180;

        /// <summary>Dead band of the position loop (rad).</summary>
        public double DeadBand { get; set; } = 1 * Math.PI / 180;

        public double IdleCurrent { get; set; } = 0.006;
        public double MovingCurrent { get; set; } = 0.25;
        public double StallCurrent { get; set; } = 0.65;

        /// <summary>Commanded output angle (rad, joint frame). NaN = no pulses yet (motor off).</summary>
        public double CommandAngle { get; set; } = double.NaN;

        /// <summary>Internal gear/brush friction torque implied by the no-load current.</summary>
        public double FrictionTorque => StallTorque * Math.Max(0, Math.Min(0.9, (MovingCurrent - IdleCurrent) / Math.Max(1e-9, StallCurrent - IdleCurrent)));

        /// <summary>Speed constant of the internal motor so that the free speed (after friction) matches the datasheet.</summary>
        public double MotorNoLoadSpeed => NoLoadSpeed / Math.Max(0.1, 1 - FrictionTorque / StallTorque);

        public double Duty { get; private set; }
        public double SupplyCurrent { get; private set; }

        /// <summary>Motor torque at the output (N·m) from the torque–speed line in the last update.</summary>
        public double MotorTorque { get; private set; }

        /// <summary>Configures the joint's drive for the next step from the command and the measured angle.</summary>
        public void Drive(SceneJoint joint)
        {
            double v = Math.Max(0, SupplyVoltage / Math.Max(0.1, RatedVoltage));
            // Gear and brush friction acts in the joint whether powered or not (Coulomb: resists motion both ways).
            joint.Friction = FrictionTorque;
            if (double.IsNaN(CommandAngle) || v < 0.4)
            {
                // Unpowered: open winding, back-driven only against friction.
                Duty = 0;
                joint.Drive = JointDrive.None;
                return;
            }

            double e = CommandAngle - joint.Angle;
            double u = Math.Abs(e) <= DeadBand ? 0 : Math.Max(-1, Math.Min(1, e / ProportionalBand));
            Duty = u;
            // DC motor torque–speed line: τ = τ_stall·u·V/V_rated − (τ_stall/ω₀)·ω, i.e. a damper of
            // gain τ_stall/ω₀ towards u·ω₀·V/V_rated, limited to the stall torque.
            double c = StallTorque / MotorNoLoadSpeed;
            joint.Drive = JointDrive.Velocity;
            joint.TargetSpeed = u * v * MotorNoLoadSpeed;
            joint.Damping = c;
            joint.MaxTorque = StallTorque * Math.Max(v, 1e-3);
        }

        /// <summary>Supply current from the torque the joint drive delivered.</summary>
        public double UpdateCurrent(SceneJoint joint)
        {
            double v = SupplyVoltage / Math.Max(0.1, RatedVoltage);
            if (double.IsNaN(CommandAngle) || v < 0.4)
            {
                SupplyCurrent = v > 0.1 ? IdleCurrent : 0;
                return SupplyCurrent;
            }

            // Winding current i = (u·V − Ke·ω)/R is proportional to the motor torque given by the
            // torque–speed line at the measured shaft speed (it includes overcoming friction).
            MotorTorque = Math.Max(-StallTorque * v, Math.Min(StallTorque * v, StallTorque / MotorNoLoadSpeed * (joint.TargetSpeed - joint.Speed)));
            double frac = Math.Abs(MotorTorque) / Math.Max(1e-9, StallTorque * Math.Max(v, 1e-3));
            SupplyCurrent = IdleCurrent + (StallCurrent - IdleCurrent) * Math.Min(1.0, frac) * Math.Min(1.4, v);
            return SupplyCurrent;
        }

        /// <summary>Datasheet numbers of common servos: (stall N·m, s/60°, idle A, moving A, stall A).</summary>
        public static ServoMotor ForModel(string? model)
        {
            string m = (model ?? string.Empty).ToUpperInvariant();
            if (m.Contains("MG996")) return new ServoMotor { StallTorque = 1.08, NoLoadSpeed = Math.PI / 3 / 0.17, MovingCurrent = 0.5, StallCurrent = 2.5, RatedVoltage = 6.0 };
            if (m.Contains("DS3218")) return new ServoMotor { StallTorque = 1.96, NoLoadSpeed = Math.PI / 3 / 0.16, MovingCurrent = 0.6, StallCurrent = 2.8, RatedVoltage = 6.0 };
            if (m.Contains("MG90")) return new ServoMotor { StallTorque = 0.216, NoLoadSpeed = Math.PI / 3 / 0.1, MovingCurrent = 0.3, StallCurrent = 0.7 };
            return new ServoMotor();
        }
    }
}
