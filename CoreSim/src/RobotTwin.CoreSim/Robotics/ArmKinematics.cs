using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>Result of an inverse-kinematics query.</summary>
    public sealed class IkResult
    {
        public bool Success { get; set; }
        public double[] Joints { get; set; } = Array.Empty<double>();

        /// <summary>Remaining position (m) and orientation (rad) error.</summary>
        public double PositionError { get; set; }
        public double OrientationError { get; set; }

        /// <summary>"analitik" or "sayısal (DLS)".</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>Why it failed (Turkish), empty on success.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>True when the solution sits close to a singular configuration.</summary>
        public bool NearSingular { get; set; }
    }

    /// <summary>
    /// Forward and inverse kinematics of an <see cref="ArmModel"/> (product of exponentials):
    /// link frames, flange pose, geometric Jacobian, closed-form IK for spherical-wrist 6R arms
    /// and SCARAs, and a damped-least-squares solver for everything else (and as a fallback).
    /// </summary>
    public static class ArmKinematics
    {
        /// <summary>Motion of joint <paramref name="i"/> by <paramref name="q"/> (exponential of its screw).</summary>
        public static Transform3 JointTransform(ArmJointSpec j, double q)
        {
            if (j.IsPrismatic) return new Transform3(j.Axis.Normalized() * q, Quat.Identity);
            var rot = Quat.FromAxisAngle(j.Axis, q);
            // Rotation about an axis through Point: p' = R(p - c) + c.
            return new Transform3(j.Point - rot.Rotate(j.Point), rot);
        }

        /// <summary>
        /// Placement of every link relative to its zero-configuration pose (base frame):
        /// result[i] moves link i, i.e. the product of the motions of joints 0..i.
        /// </summary>
        public static Transform3[] LinkTransforms(ArmModel m, IReadOnlyList<double> q)
        {
            var result = new Transform3[m.Dof];
            var t = Transform3.Identity;
            for (int i = 0; i < m.Dof; i++)
            {
                t = t * JointTransform(m.Joints[i], q[i]);
                result[i] = t;
            }

            return result;
        }

        /// <summary>Flange pose in the base frame.</summary>
        public static Transform3 Forward(ArmModel m, IReadOnlyList<double> q)
        {
            var t = Transform3.Identity;
            for (int i = 0; i < m.Dof; i++) t = t * JointTransform(m.Joints[i], q[i]);
            return t * m.Flange;
        }

        /// <summary>Tool centre point pose in the base frame (<paramref name="tool"/>: flange → TCP).</summary>
        public static Transform3 Forward(ArmModel m, IReadOnlyList<double> q, Transform3 tool) => Forward(m, q) * tool;

        /// <summary>
        /// Geometric Jacobian (6 × n) at the given point (base frame): rows 0–2 linear velocity of
        /// the point, rows 3–5 angular velocity, per unit joint rate.
        /// </summary>
        public static double[,] Jacobian(ArmModel m, IReadOnlyList<double> q, Vec3 point)
        {
            var jac = new double[6, m.Dof];
            var t = Transform3.Identity;
            for (int i = 0; i < m.Dof; i++)
            {
                var spec = m.Joints[i];
                var axis = t.ApplyVector(spec.Axis.Normalized());
                if (spec.IsPrismatic)
                {
                    jac[0, i] = axis.X;
                    jac[1, i] = axis.Y;
                    jac[2, i] = axis.Z;
                }
                else
                {
                    var c = t.Apply(spec.Point);
                    var v = axis.Cross(point - c);
                    jac[0, i] = v.X;
                    jac[1, i] = v.Y;
                    jac[2, i] = v.Z;
                    jac[3, i] = axis.X;
                    jac[4, i] = axis.Y;
                    jac[5, i] = axis.Z;
                }

                t = t * JointTransform(spec, q[i]);
            }

            return jac;
        }

        /// <summary>Yoshikawa manipulability √det(J·Jᵀ) of the task rows (0 = singular).</summary>
        public static double Manipulability(ArmModel m, IReadOnlyList<double> q, Transform3 tool)
        {
            var tcp = Forward(m, q, tool);
            var j = Jacobian(m, q, tcp.P);
            var rows = TaskRows(m);
            int r = rows.Length;
            var a = new double[r, r];
            for (int x = 0; x < r; x++)
            {
                for (int y = 0; y < r; y++)
                {
                    double s = 0;
                    for (int k = 0; k < m.Dof; k++) s += Scale(rows[x]) * j[rows[x], k] * Scale(rows[y]) * j[rows[y], k];
                    a[x, y] = s;
                }
            }

            double det = Determinant(a);
            return Math.Sqrt(Math.Max(0, det));
        }

        // Angular rows are scaled by a characteristic length so mm and rad are comparable.
        private static double Scale(int row) => row >= 3 ? 0.3 : 1.0;

        /// <summary>Controlled task-space rows: 6 for a 6-axis arm, x/y/z/yaw for a SCARA, position for fewer axes.</summary>
        private static int[] TaskRows(ArmModel m) => m.Ik == ArmIkKind.Scara4 || m.Dof == 4 ? new[] { 0, 1, 2, 5 } : m.Dof >= 6 ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 0, 1, 2 };

        // ================================================================ inverse kinematics

        /// <summary>
        /// Joint values putting the TCP at <paramref name="target"/> (base frame), the solution
        /// closest to <paramref name="seed"/> within joint limits. Closed form where the
        /// geometry allows, damped least squares otherwise.
        /// </summary>
        public static IkResult Inverse(ArmModel m, Transform3 target, IReadOnlyList<double> seed, Transform3? tool = null)
        {
            var toolT = tool ?? Transform3.Identity;
            var flange = target * toolT.Inverse();
            IkResult? analytic = m.Ik switch
            {
                ArmIkKind.SphericalWrist6 => SolveSphericalWrist(m, flange, seed),
                ArmIkKind.Scara4 => SolveScara(m, flange, seed),
                _ => null
            };
            if (analytic != null && analytic.Success)
            {
                Measure(m, analytic, target, toolT);
                if (analytic.PositionError < 1e-6 && analytic.OrientationError < 1e-5) return analytic;
            }

            var numeric = SolveDls(m, target, seed, toolT);
            if (numeric.Success || analytic == null) return numeric;
            // Neither converged: report the analytic diagnosis (it knows why: out of reach, limits).
            return analytic.Success ? numeric : analytic;
        }

        private static void Measure(ArmModel m, IkResult r, Transform3 target, Transform3 tool)
        {
            var got = Forward(m, r.Joints, tool);
            r.PositionError = (got.P - target.P).Length;
            r.OrientationError = OrientationError(m, got.Q, target.Q);
        }

        /// <summary>Orientation error that the arm can control (yaw only for a SCARA).</summary>
        private static double OrientationError(ArmModel m, Quat got, Quat want)
        {
            if (m.Ik == ArmIkKind.Scara4 || m.Dof == 4)
            {
                var e = Transform3.RotationError(got, want);
                return Math.Abs(e.Z);
            }

            return m.Dof >= 6 ? Transform3.AngleBetween(got, want) : 0;
        }

        private static double Wrap(double a)
        {
            a = Math.IEEERemainder(a, 2 * Math.PI);
            return a;
        }

        /// <summary>Picks, among candidate solutions (and their ±2π copies), the valid one nearest the seed.</summary>
        private static IkResult Choose(ArmModel m, List<double[]> candidates, IReadOnlyList<double> seed, string failMessage)
        {
            double best = double.PositiveInfinity;
            double[]? bestQ = null;
            int n = m.Dof;
            var trial = new double[n];
            foreach (var c in candidates)
            {
                // Each revolute joint: the representation within limits nearest the seed.
                bool ok = true;
                double cost = 0;
                for (int i = 0; i < n; i++)
                {
                    var j = m.Joints[i];
                    if (j.IsPrismatic)
                    {
                        if (c[i] < j.Min - 1e-9 || c[i] > j.Max + 1e-9)
                        {
                            ok = false;
                            break;
                        }

                        trial[i] = Math.Max(j.Min, Math.Min(j.Max, c[i]));
                    }
                    else
                    {
                        double baseAngle = seed[i] + Wrap(c[i] - seed[i]);
                        double pick = double.NaN, pickCost = double.PositiveInfinity;
                        for (int k = -2; k <= 2; k++)
                        {
                            double v = baseAngle + k * 2 * Math.PI;
                            if (v < j.Min - 1e-9 || v > j.Max + 1e-9) continue;
                            double d = Math.Abs(v - seed[i]);
                            if (d < pickCost)
                            {
                                pickCost = d;
                                pick = v;
                            }
                        }

                        if (double.IsNaN(pick))
                        {
                            ok = false;
                            break;
                        }

                        trial[i] = Math.Max(j.Min, Math.Min(j.Max, pick));
                    }

                    // Heavy proximal joints count more: prefer moving the wrist over the whole arm.
                    double w = i < 3 ? 2.0 : 1.0;
                    cost += w * Math.Abs(trial[i] - seed[i]);
                }

                if (!ok || cost >= best) continue;
                best = cost;
                bestQ = (double[])trial.Clone();
            }

            if (bestQ == null) return new IkResult { Success = false, Joints = Copy(seed), Method = "analitik", Message = failMessage };
            return new IkResult { Success = true, Joints = bestQ, Method = "analitik" };
        }

        private static double[] Copy(IReadOnlyList<double> q)
        {
            var r = new double[q.Count];
            for (int i = 0; i < r.Length; i++) r[i] = q[i];
            return r;
        }

        /// <summary>
        /// Closed form for J1 ∥ Z, J2 ∥ J3 ∥ Y, wrist X–Y–X intersecting (up to 8 solutions:
        /// front/back × elbow up/down × wrist flip). Singular wrist (J5 ≈ 0) keeps J4 at the seed.
        /// </summary>
        private static IkResult SolveSphericalWrist(ArmModel m, Transform3 flange, IReadOnlyList<double> seed)
        {
            double d1 = m.D1, a1 = m.A1, a2 = m.A2, a3 = m.A3, d4 = m.D4, d6 = m.D6;
            var zAxis = flange.ZAxis;
            var c = flange.P - zAxis * d6;
            double L3 = Math.Sqrt(d4 * d4 + a3 * a3), phi = Math.Atan2(d4, a3);
            var candidates = new List<double[]>();
            string why = "Hedef erişim dışında";
            double rho = Math.Sqrt(c.X * c.X + c.Y * c.Y);
            var q1s = new List<double>();
            if (rho < 1e-6)
            {
                // Wrist centre on the J1 axis: shoulder singularity, any J1 works; keep the current one.
                q1s.Add(seed[0]);
                why = "Omuz tekilliği (bilek merkezi J1 ekseninde)";
            }
            else
            {
                double t = Math.Atan2(c.Y, c.X);
                q1s.Add(t);
                q1s.Add(t + Math.PI);
            }

            var mr = Mat3.FromQuat(m.Flange.Q);
            var target = Mat3.FromQuat(flange.Q);
            foreach (double q1 in q1s)
            {
                double radial = Math.Cos(q1) * c.X + Math.Sin(q1) * c.Y;
                double r = radial - a1, s = c.Z - d1;
                double k = (r * r + s * s - a2 * a2 - L3 * L3) / (2 * a2 * L3);
                if (k > 1 + 1e-9 || k < -1 - 1e-9) continue;
                k = Math.Max(-1, Math.Min(1, k));
                double ac = Math.Acos(k);
                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    double q3 = sign * ac - phi;
                    double vx = d4 * Math.Cos(q3) + a3 * Math.Sin(q3);
                    double vz = a2 - d4 * Math.Sin(q3) + a3 * Math.Cos(q3);
                    double q2 = Math.Atan2(r, s) - Math.Atan2(vx, vz);

                    // Wrist: Rx(q4)·Ry(q5)·Rx(q6) = (Rz(q1)·Ry(q2+q3))ᵀ · R · Mᵀ.
                    var r03 = Mat3.FromQuat(Quat.FromAxisAngle(Vec3.UnitZ, q1) * Quat.FromAxisAngle(Vec3.UnitY, q2 + q3));
                    var w = r03.Transpose() * target * mr.Transpose();
                    double c5 = Math.Max(-1, Math.Min(1, w.M00));
                    double b = Math.Acos(c5);
                    if (Math.Sin(b) < 1e-6)
                    {
                        // Wrist singular: only q4 + q6 (or q4 − q6) is defined.
                        double q4 = seed[3];
                        double sum = Math.Atan2(w.M21, w.M11);
                        double q6 = c5 > 0 ? sum - q4 : q4 - sum;
                        candidates.Add(new[] { q1, q2, q3, q4, c5 > 0 ? 0.0 : Math.PI, q6 });
                        why = "Bilek tekilliği (J5 ≈ 0)";
                        continue;
                    }

                    foreach (double s5 in new[] { 1.0, -1.0 })
                    {
                        double q5 = s5 * b;
                        double sb = Math.Sin(q5);
                        double q4 = Math.Atan2(w.M10 / sb, -w.M20 / sb);
                        double q6 = Math.Atan2(w.M01 / sb, w.M02 / sb);
                        candidates.Add(new[] { q1, q2, q3, q4, q5, q6 });
                    }
                }
            }

            if (candidates.Count == 0) return new IkResult { Success = false, Joints = Copy(seed), Method = "analitik", Message = why };
            var res = Choose(m, candidates, seed, "Çözüm eklem sınırları dışında");
            if (res.Success)
            {
                res.NearSingular = Math.Abs(Math.Sin(res.Joints[4])) < Math.Sin(3 * Math.PI / 180) || rho < 0.02;
            }

            return res;
        }

        /// <summary>Closed form for a SCARA (RRPR): planar 2-link (elbow left/right), quill depth, tool roll.</summary>
        private static IkResult SolveScara(ArmModel m, Transform3 flange, IReadOnlyList<double> seed)
        {
            double a1 = m.A1, a2 = m.A2;
            double x = flange.P.X, y = flange.P.Y;
            double k = (x * x + y * y - a1 * a1 - a2 * a2) / (2 * a1 * a2);
            if (k > 1 + 1e-9 || k < -1 - 1e-9) return new IkResult { Success = false, Joints = Copy(seed), Method = "analitik", Message = "Hedef erişim dışında" };
            k = Math.Max(-1, Math.Min(1, k));
            // Depth: zero-configuration flange height minus target height along the (downward) quill.
            var j3 = m.Joints[2];
            double depth = (flange.P - m.Flange.P).Dot(j3.Axis.Normalized());
            // Tool roll: R = Rz(q1+q2+q4)·M.
            var rel = flange.Q * m.Flange.Q.Conjugate;
            var xAxis = rel.Rotate(Vec3.UnitX);
            double phiTotal = Math.Atan2(xAxis.Y, xAxis.X);
            var candidates = new List<double[]>();
            foreach (double sign in new[] { 1.0, -1.0 })
            {
                double q2 = sign * Math.Acos(k);
                double q1 = Math.Atan2(y, x) - Math.Atan2(a2 * Math.Sin(q2), a1 + a2 * Math.Cos(q2));
                double q4 = phiTotal - q1 - q2;
                candidates.Add(new[] { q1, q2, depth, q4 });
            }

            var res = Choose(m, candidates, seed, "Çözüm eklem sınırları dışında");
            if (res.Success) res.NearSingular = Math.Abs(Math.Sin(res.Joints[1])) < Math.Sin(3 * Math.PI / 180);
            return res;
        }

        /// <summary>
        /// Damped least squares (Levenberg–Marquardt style) with adaptive damping near
        /// singularities and joint-limit clamping.
        /// </summary>
        public static IkResult SolveDls(ArmModel m, Transform3 target, IReadOnlyList<double> seed, Transform3 tool, int maxIterations = 200)
        {
            int n = m.Dof;
            var q = Copy(seed);
            var rows = TaskRows(m);
            int r = rows.Length;
            double lambda = 0.02;
            double lastErr = double.PositiveInfinity;
            bool nearSingular = false;
            for (int it = 0; it < maxIterations; it++)
            {
                var cur = Forward(m, q, tool);
                var ep = target.P - cur.P;
                var er = Transform3.RotationError(cur.Q, target.Q);
                var e6 = new[] { ep.X, ep.Y, ep.Z, er.X, er.Y, er.Z };
                double err = 0;
                var e = new double[r];
                for (int k = 0; k < r; k++)
                {
                    e[k] = e6[rows[k]] * Scale(rows[k]);
                    err += e[k] * e[k];
                }

                err = Math.Sqrt(err);
                if (ep.Length < 1e-7 && (r < 4 || OrientationError(m, cur.Q, target.Q) < 1e-6)) break;
                lambda = err < lastErr ? Math.Max(1e-4, lambda * 0.5) : Math.Min(1.0, lambda * 4);
                lastErr = err;
                var jFull = Jacobian(m, q, cur.P);
                var jac = new double[r, n];
                for (int a = 0; a < r; a++)
                {
                    for (int b = 0; b < n; b++) jac[a, b] = jFull[rows[a], b] * Scale(rows[a]);
                }

                // dq = Jᵀ (J Jᵀ + λ² I)⁻¹ e
                var jjt = new double[r, r];
                for (int a = 0; a < r; a++)
                {
                    for (int b = 0; b < r; b++)
                    {
                        double s = 0;
                        for (int k = 0; k < n; k++) s += jac[a, k] * jac[b, k];
                        jjt[a, b] = s + (a == b ? lambda * lambda : 0);
                    }
                }

                double manip = Math.Sqrt(Math.Max(0, Determinant(jjt)));
                nearSingular = manip < 1e-4;
                var y = Solve(jjt, e);
                if (y == null) break;
                double maxStep = 0;
                var dq = new double[n];
                for (int k = 0; k < n; k++)
                {
                    double s = 0;
                    for (int a = 0; a < r; a++) s += jac[a, k] * y[a];
                    dq[k] = s;
                    maxStep = Math.Max(maxStep, Math.Abs(s));
                }

                double limit = 0.3;
                double scale = maxStep > limit ? limit / maxStep : 1;
                for (int k = 0; k < n; k++) q[k] += dq[k] * scale;
                q = m.Clamp(q);
            }

            var res = new IkResult { Joints = q, Method = "sayısal (DLS)", NearSingular = nearSingular };
            Measure(m, res, target, tool);
            res.Success = res.PositionError < 1e-4 && res.OrientationError < 1e-3;
            if (!res.Success) res.Message = nearSingular ? "Tekil konum: hedefe yaklaşılamıyor" : "Hedef erişim dışında veya eklem sınırında";
            return res;
        }

        // ================================================================ small dense linear algebra

        internal static double[]? Solve(double[,] a, double[] b)
        {
            int n = b.Length;
            var m = new double[n, n + 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) m[i, j] = a[i, j];
                m[i, n] = b[i];
            }

            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++)
                {
                    if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
                }

                if (Math.Abs(m[piv, c]) < 1e-14) return null;
                if (piv != c)
                {
                    for (int j = 0; j <= n; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
                }

                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = m[r, c] / m[c, c];
                    if (f == 0) continue;
                    for (int j = c; j <= n; j++) m[r, j] -= f * m[c, j];
                }
            }

            var x = new double[n];
            for (int i = 0; i < n; i++) x[i] = m[i, n] / m[i, i];
            return x;
        }

        internal static double Determinant(double[,] a)
        {
            int n = a.GetLength(0);
            var m = (double[,])a.Clone();
            double det = 1;
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++)
                {
                    if (Math.Abs(m[r, c]) > Math.Abs(m[piv, c])) piv = r;
                }

                if (Math.Abs(m[piv, c]) < 1e-300) return 0;
                if (piv != c)
                {
                    for (int j = 0; j < n; j++) (m[c, j], m[piv, j]) = (m[piv, j], m[c, j]);
                    det = -det;
                }

                det *= m[c, c];
                for (int r = c + 1; r < n; r++)
                {
                    double f = m[r, c] / m[c, c];
                    for (int j = c; j < n; j++) m[r, j] -= f * m[c, j];
                }
            }

            return det;
        }
    }
}
