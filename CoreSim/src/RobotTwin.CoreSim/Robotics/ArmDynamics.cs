using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>
    /// [robot-control] The torques an arm's drives must give along a motion, and what follows from them: inverse dynamics
    /// of the links (their masses and boxes, as <see cref="ArmLinkSpec"/> gives them) with a payload at the tool, the
    /// motion slowed until every axis stays within its peak torque, the heaviest payload a program can carry, and the
    /// program's cycle time. Gravity, the links' accelerations and their gyroscopic terms are all in; joint friction is
    /// added as Coulomb friction against the motion.
    /// </summary>
    public static class ArmDynamics
    {
        public const double Gravity = 9.81;

        /// <summary>The finite-difference step (s) of the link motions: the FK is smooth, the error is O(h²).</summary>
        private const double H = 1e-3;

        /// <summary>
        /// Joint torques (N·m; N for a prismatic axis) at <paramref name="q"/> moving at <paramref name="qd"/> and
        /// accelerating at <paramref name="qdd"/>, with <paramref name="payloadKg"/> at the tool centre point. Each axis
        /// carries everything after it: τᵢ = aᵢ · Σⱼ≥ᵢ [(cⱼ − pᵢ) × Fⱼ + Nⱼ] with Fⱼ = mⱼ(aⱼ − g), Nⱼ = Iⱼαⱼ + ωⱼ × Iⱼωⱼ
        /// (Newton–Euler), plus friction against the motion.
        /// </summary>
        public static double[] Torques(ArmModel m, IReadOnlyList<double> q, IReadOnlyList<double> qd, IReadOnlyList<double> qdd,
            double payloadKg = 0, Transform3? tool = null)
        {
            int n = m.Dof;
            var toolT = tool ?? Transform3.Identity;
            // Five configurations along q(t) = q + qd·t + qdd·t²/2 about t = 0.
            var frames = new Transform3[5][];
            var tcp = new Vec3[5];
            for (int s = 0; s < 5; s++)
            {
                double t = (s - 2) * H;
                var qs = new double[n];
                for (int i = 0; i < n; i++) qs[i] = q[i] + qd[i] * t + 0.5 * qdd[i] * t * t;
                frames[s] = ArmKinematics.LinkTransforms(m, qs);
                tcp[s] = ArmKinematics.Forward(m, qs, toolT).P;
            }

            var tau = new double[n];
            var g = new Vec3(0, 0, -Gravity);
            // Axes and their points now (the motions of the axes before them applied).
            var axes = new Vec3[n];
            var points = new Vec3[n];
            var before = Transform3.Identity;
            for (int i = 0; i < n; i++)
            {
                axes[i] = before.ApplyVector(m.Joints[i].Axis.Normalized());
                points[i] = before.Apply(m.Joints[i].Point);
                before = frames[2][i];
            }

            void Load(int carrier, Vec3 at, Vec3 force, Vec3 moment)
            {
                for (int i = 0; i <= carrier; i++)
                    tau[i] += m.Joints[i].IsPrismatic ? axes[i].Dot(force) : axes[i].Dot((at - points[i]).Cross(force) + moment);
            }

            int links = Math.Min(n, m.Links.Count);
            for (int j = 0; j < links; j++)
            {
                var link = m.Links[j];
                if (link.Mass <= 0) continue;
                var c = new Vec3[5];
                for (int s = 0; s < 5; s++) c[s] = frames[s][j].Apply(link.Center);
                var a = (c[3] - 2 * c[2] + c[1]) * (1 / (H * H));
                Vec3 Omega(int s) => AngularVelocity(frames[s - 1][j].Q, frames[s + 1][j].Q, 2 * H);
                var w = Omega(2);
                var alpha = (Omega(3) - Omega(1)) * (1 / (2 * H));
                var rot = frames[2][j].Q;
                // A solid box about its centre, turned with the link.
                double sx = link.Size.X, sy = link.Size.Y, sz = link.Size.Z, mass = link.Mass;
                var inertiaBody = new Vec3(mass / 12 * (sy * sy + sz * sz), mass / 12 * (sx * sx + sz * sz), mass / 12 * (sx * sx + sy * sy));
                Vec3 I(Vec3 v)
                {
                    var local = rot.Conjugate.Rotate(v);
                    return rot.Rotate(new Vec3(local.X * inertiaBody.X, local.Y * inertiaBody.Y, local.Z * inertiaBody.Z));
                }

                var force = (a - g) * mass;
                var moment = I(alpha) + w.Cross(I(w));
                Load(j, c[2], force, moment);
            }

            if (payloadKg > 0)
            {
                var a = (tcp[3] - 2 * tcp[2] + tcp[1]) * (1 / (H * H));
                Load(n - 1, tcp[2], (a - g) * payloadKg, Vec3.Zero);
            }

            for (int i = 0; i < n; i++)
            {
                double f = m.Joints[i].Friction;
                if (f > 0 && Math.Abs(qd[i]) > 1e-6) tau[i] += Math.Sign(qd[i]) * f;
            }

            return tau;
        }

        /// <summary>The angular velocity (world frame) that turns <paramref name="from"/> into <paramref name="to"/> in <paramref name="dt"/>.</summary>
        private static Vec3 AngularVelocity(Quat from, Quat to, double dt)
        {
            var d = (to * from.Conjugate).Normalized();
            if (d.W < 0) d = new Quat(-d.W, -d.X, -d.Y, -d.Z);
            double s = Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
            if (s < 1e-12) return Vec3.Zero;
            double angle = 2 * Math.Atan2(s, d.W);
            return new Vec3(d.X, d.Y, d.Z) * (angle / s / dt);
        }

        /// <summary>The worst torque along a motion: the highest |τᵢ| / peak torque over the samples, which axis, and when.</summary>
        public readonly struct TorqueCheck
        {
            public TorqueCheck(double ratio, int axis, double time, double[] peak)
            {
                Ratio = ratio;
                Axis = axis;
                Time = time;
                Peak = peak;
            }

            /// <summary>Highest |τ| / MaxTorque over the axes and the motion (≤ 1: every drive copes).</summary>
            public double Ratio { get; }

            /// <summary>The axis that came closest to (or past) its limit, −1 for a motion without samples.</summary>
            public int Axis { get; }

            /// <summary>When along the motion (s).</summary>
            public double Time { get; }

            /// <summary>Each axis's highest |τ| along the motion (N·m or N).</summary>
            public double[] Peak { get; }
        }

        /// <summary>The torques along <paramref name="motion"/>, sampled every <paramref name="dt"/>, against each axis's peak torque.</summary>
        public static TorqueCheck Check(ArmModel m, ArmMotion motion, double payloadKg = 0, Transform3? tool = null, double dt = 0.01)
        {
            int n = m.Dof;
            var peak = new double[n];
            double worst = 0, when = 0;
            int axis = -1;
            var q = new double[n];
            var qd = new double[n];
            var q2 = new double[n];
            var qd2 = new double[n];
            double total = motion.Duration;
            int samples = Math.Max(2, (int)Math.Ceiling(total / dt) + 1);
            for (int s = 0; s < samples; s++)
            {
                double t = total * s / (samples - 1);
                motion.Sample(t, q, qd);
                // The acceleration from the rates a moment later (the profiles are smooth inside their phases).
                double h = Math.Min(1e-3, Math.Max(1e-6, total / 1000));
                motion.Sample(Math.Min(total, t + h), q2, qd2);
                var qdd = new double[n];
                double step = Math.Min(total, t + h) - t;
                if (step > 0)
                    for (int i = 0; i < n; i++) qdd[i] = (qd2[i] - qd[i]) / step;
                var tau = Torques(m, q, qd, qdd, payloadKg, tool);
                for (int i = 0; i < n; i++)
                {
                    peak[i] = Math.Max(peak[i], Math.Abs(tau[i]));
                    double limit = m.Joints[i].MaxTorque;
                    if (limit <= 0) continue;
                    double r = Math.Abs(tau[i]) / limit;
                    if (r > worst)
                    {
                        worst = r;
                        axis = i;
                        when = t;
                    }
                }
            }

            return new TorqueCheck(worst, axis, when, peak);
        }

        /// <summary>
        /// How much a motion must be slowed (its time stretched; 1: not at all) for every axis to stay within
        /// <paramref name="margin"/> of its peak torque: accelerations fall with the square of the stretch, the weight does
        /// not. Infinity when the weight alone (the payload held still) is beyond a drive.
        /// </summary>
        public static double TimeScaleForTorque(ArmModel m, ArmMotion motion, double payloadKg = 0, Transform3? tool = null, double margin = 0.95)
        {
            if (motion.Duration <= 0 || Check(m, motion, payloadKg, tool).Ratio <= margin) return 1;
            // Very slow: only the weight is left. If that is too much, no slowing helps.
            if (Check(m, motion.Stretched(50), payloadKg, tool).Ratio > margin) return double.PositiveInfinity;
            double lo = 1, hi = 50;
            for (int k = 0; k < 30 && hi - lo > 1e-3; k++)
            {
                double mid = 0.5 * (lo + hi);
                if (Check(m, motion.Stretched(mid), payloadKg, tool).Ratio <= margin) hi = mid;
                else lo = mid;
            }

            return hi;
        }

        /// <summary>[deflection] A joint's stiffness, its own or estimated from its rating (see <see cref="ArmJointSpec.Stiffness"/>).</summary>
        public static double StiffnessOf(ArmModel m, int joint)
        {
            var j = m.Joints[joint];
            if (j.Stiffness > 0) return j.Stiffness;
            double perNm = m.ServoDriven ? 6 : 350;
            return Math.Max(1e-6, j.MaxTorque * perNm * (j.IsPrismatic ? 1000 : 1));
        }

        /// <summary>
        /// [deflection] How far the tool centre point sags at pose <paramref name="q"/> as the drives wind up under the
        /// weight they hold (Δq = K⁻¹·τ, Δx = J·Δq, linear in the small angles): under the arm's own weight with the payload
        /// (m), and under the payload alone (the change a pick makes, what teaching under no load does not see).
        /// </summary>
        public static (Vec3 Total, Vec3 FromPayload) Deflection(ArmModel m, IReadOnlyList<double> q, double payloadKg = 0, Transform3? tool = null)
        {
            var zero = new double[m.Dof];
            var loaded = Torques(m, q, zero, zero, payloadKg, tool);
            var empty = Torques(m, q, zero, zero, 0, tool);
            var tcp = ArmKinematics.Forward(m, q, tool ?? Transform3.Identity).P;
            var jac = ArmKinematics.Jacobian(m, q, tcp);
            Vec3 Sag(double[] tau)
            {
                double x = 0, y = 0, z = 0;
                for (int i = 0; i < m.Dof; i++)
                {
                    // The drive holds τ by winding back Δq = −τ/k against it: the load wins that much.
                    double dq = -tau[i] / StiffnessOf(m, i);
                    x += jac[0, i] * dq;
                    y += jac[1, i] * dq;
                    z += jac[2, i] * dq;
                }

                return new Vec3(x, y, z);
            }

            var total = Sag(loaded);
            return (total, total - Sag(empty));
        }

        /// <summary>
        /// The heaviest payload (kg) the arm can hold at every one of <paramref name="poses"/> within its drives' peak
        /// torque (held still: the weight only), found by bisection up to <paramref name="upTo"/>.
        /// </summary>
        public static double PayloadCapacity(ArmModel m, IEnumerable<IReadOnlyList<double>> poses, Transform3? tool = null, double upTo = 1000, double margin = 1.0)
        {
            var list = poses.ToList();
            if (list.Count == 0) return 0;
            var zero = new double[m.Dof];
            bool Holds(double kg)
            {
                foreach (var q in list)
                {
                    var tau = Torques(m, q, zero, zero, kg, tool);
                    for (int i = 0; i < m.Dof; i++)
                        if (m.Joints[i].MaxTorque > 0 && Math.Abs(tau[i]) > margin * m.Joints[i].MaxTorque) return false;
                }

                return true;
            }

            if (!Holds(0)) return 0;
            if (Holds(upTo)) return upTo;
            double lo = 0, hi = upTo;
            for (int k = 0; k < 40 && hi - lo > 1e-4; k++)
            {
                double mid = 0.5 * (lo + hi);
                if (Holds(mid)) lo = mid;
                else hi = mid;
            }

            return lo;
        }
    }
}
