using System;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>[d6] How a configurable joint lets one of its six axes move (Unity's ConfigurableJointMotion).</summary>
    public enum AxisMotion
    {
        /// <summary>The axis does not move.</summary>
        Locked,

        /// <summary>The axis moves within its limit.</summary>
        Limited,

        /// <summary>The axis moves freely.</summary>
        Free
    }

    /// <summary>
    /// [d6] The configurable joint's settings (Unity's ConfigurableJoint, PhysX's D6): in the joint frame (X = the joint's
    /// axis, Y = its secondary axis, Z = X × Y) each of the three slides and three turns is locked, limited or free. The
    /// limited slides share one distance limit (a sphere, a disc or a segment, as in Unity); the turn about X (the twist)
    /// has a lower and an upper angle (<see cref="Lower"/>, <see cref="Upper"/>); the turns about Y and Z (the swings) have
    /// one angle each and, both limited, make an elliptic cone. Position drives pull the slides toward
    /// <see cref="TargetOffset"/> and the turns toward <see cref="TargetRotation"/> (Unity's slerp drive) as soft,
    /// implicit spring–dampers. A character joint (shoulder, hip) is this with the slides locked.
    /// </summary>
    public sealed partial class SceneJoint
    {
        /// <summary>[d6] The joint frame in the parent's body frame (world when there is none) and in the child's.</summary>
        internal Quat FrameP = Quat.Identity, FrameC = Quat.Identity;

        public AxisMotion MotionX { get; set; }
        public AxisMotion MotionY { get; set; }
        public AxisMotion MotionZ { get; set; }
        public AxisMotion AngularMotionX { get; set; }
        public AxisMotion AngularMotionY { get; set; }
        public AxisMotion AngularMotionZ { get; set; }

        /// <summary>[d6] How far the child's anchor may leave the parent's along the limited slides (m); 0 locks them.</summary>
        public double LinearLimit { get; set; }

        /// <summary>[d6] The swing limit about Y (rad, Unity's swing 1 / angular Y limit); below 0.01° it locks the axis.</summary>
        public double Swing1Limit { get; set; }

        /// <summary>[d6] The swing limit about Z (rad, Unity's swing 2 / angular Z limit).</summary>
        public double Swing2Limit { get; set; }

        /// <summary>[d6] Where the slides' drive pulls the child's anchor (joint frame, m).</summary>
        public Vec3 TargetOffset { get; set; }

        /// <summary>[d6] The slides' drive speed (joint frame, m/s).</summary>
        public Vec3 TargetVelocity { get; set; }

        /// <summary>[d6] The child frame's orientation the turns' drive pulls toward, in the parent's joint frame.</summary>
        public Quat TargetRotation { get; set; } = Quat.Identity;

        /// <summary>[d6] The turns' drive spin (joint frame, rad/s).</summary>
        public Vec3 TargetAngularVelocity { get; set; }

        /// <summary>[d6] The slides' drive: spring (N/m), damper (N·s/m) and its force limit (N).</summary>
        public double LinearDriveStiffness { get; set; }

        public double LinearDriveDamping { get; set; }
        public double LinearDriveMaxForce { get; set; } = double.PositiveInfinity;

        /// <summary>[d6] The turns' drive: spring (N·m/rad), damper (N·m·s/rad) and its torque limit (N·m).</summary>
        public double AngularDriveStiffness { get; set; }

        public double AngularDriveDamping { get; set; }
        public double AngularDriveMaxTorque { get; set; } = double.PositiveInfinity;

        /// <summary>[d6] The child's anchor from the parent's in the joint frame (m), as the last substep found it.</summary>
        public Vec3 LinearOffset { get; internal set; }

        /// <summary>[d6] The turn about X (rad), from the swing–twist split of the child frame in the parent's.</summary>
        public double Twist { get; internal set; }

        /// <summary>[d6] The swing about Y (rad).</summary>
        public double Swing1 { get; internal set; }

        /// <summary>[d6] The swing about Z (rad).</summary>
        public double Swing2 { get; internal set; }

        /// <summary>[d6] Sets the secondary axis (world, at the bodies' poses now): the joint frame's Y, what swing 1 turns about.</summary>
        public void SetSecondaryAxis(Vec3 worldSecondary) => InitFrame(WorldAxis, worldSecondary);

        internal void InitFrame(Vec3 worldAxis, Vec3 worldSecondary)
        {
            var x = worldAxis.Length > 1e-12 ? worldAxis.Normalized() : Vec3.UnitZ;
            var y = worldSecondary - x * worldSecondary.Dot(x);
            if (y.Length < 1e-9) y = x.AnyPerpendicular();
            y = y.Normalized();
            var world = Mat3.FromColumns(x, y, x.Cross(y)).ToQuat();
            var qP = ParentBody?.Orientation ?? Quat.Identity;
            FrameP = (qP.Conjugate * world).Normalized();
            FrameC = (Rest.Conjugate * FrameP).Normalized();
        }

        internal AxisMotion Linear(int k) => k == 0 ? MotionX : k == 1 ? MotionY : MotionZ;

        /// <summary>The child frame relative to the parent's split into a twist about X and a swing (axis-angle about Y and Z).</summary>
        internal static (double Twist, double Swing1, double Swing2) SwingTwist(Quat rel)
        {
            if (rel.W < 0) rel = new Quat(-rel.W, -rel.X, -rel.Y, -rel.Z);
            double tl = Math.Sqrt(rel.W * rel.W + rel.X * rel.X);
            var twist = tl > 1e-12 ? new Quat(rel.W / tl, rel.X / tl, 0, 0) : Quat.Identity;
            double twistAngle = 2 * Math.Atan2(twist.X, twist.W);
            var swing = rel * twist.Conjugate;
            if (swing.W < 0) swing = new Quat(-swing.W, -swing.X, -swing.Y, -swing.Z);
            double sv = Math.Sqrt(swing.Y * swing.Y + swing.Z * swing.Z);
            if (sv < 1e-12) return (twistAngle, 2 * swing.Y, 2 * swing.Z);
            double angle = 2 * Math.Atan2(sv, swing.W);
            return (twistAngle, angle * swing.Y / sv, angle * swing.Z / sv);
        }

        /// <summary>[d6] The rotation of a twist about X then a swing about the axis (0, swing1, swing2) (rad): the inverse of the split.</summary>
        public static Quat FromSwingTwist(double twist, double swing1, double swing2)
        {
            double s = Math.Sqrt(swing1 * swing1 + swing2 * swing2);
            var swing = s > 1e-12 ? Quat.FromAxisAngle(new Vec3(0, swing1 / s, swing2 / s), s) : Quat.Identity;
            return (swing * Quat.FromAxisAngle(Vec3.UnitX, twist)).Normalized();
        }
    }

    public sealed partial class PhysicsScene
    {
        // [d6] Row slots of a configurable joint, past the hinge's and slider's (twist locked / limits reuse RoleAng0, RoleLower, RoleUpper).
        private const int RoleLin0 = 12, RoleLinLimit = 15, RoleSwingA = 16, RoleSwingB = 17, RoleSwingC = 18, RoleSwingD = 19, RoleLinDrive0 = 20, RoleAngDrive0 = 23;

        /// <summary>A drive's row, solved after the limits so it sees the velocity they leave.</summary>
        private static bool IsDriveSlot(int slot) => slot == RoleDrive || (slot >= RoleLinDrive0 && slot < RoleAngDrive0 + 3);

        /// <summary>[d6] Whether the configurable joint holds its anchors together (then the point block does, better conditioned).</summary>
        private static bool D6PointLocked(SceneJoint j) =>
            j.MotionX == AxisMotion.Locked && j.MotionY == AxisMotion.Locked && j.MotionZ == AxisMotion.Locked;

        private static void ZeroSlots(SceneJoint j, int first, int count)
        {
            for (int k = first; k < first + count; k++) j.Impulse[k] = 0;
        }

        /// <summary>[d6] The rows of a configurable joint (Unity's ConfigurableJoint): its slides, its turns and its drives.</summary>
        private void PrepareConfigurable(SceneJoint j, int ji, int p, int c, Quat qP, Quat qC, Vec3 rP, Vec3 rC, Vec3 d, double h)
        {
            var fP = qP * j.FrameP;
            var fC = qC * j.FrameC;
            Vec3 ax0 = fP.Rotate(Vec3.UnitX), ax1 = fP.Rotate(Vec3.UnitY), ax2 = fP.Rotate(Vec3.UnitZ);   // no arrays: this runs every substep
            Vec3 Ax(int k) => k == 0 ? ax0 : k == 1 ? ax1 : ax2;
            static double Of(Vec3 v, int k) => k == 0 ? v.X : k == 1 ? v.Y : v.Z;
            j.LinearOffset = new Vec3(d.Dot(ax0), d.Dot(ax1), d.Dot(ax2));
            var rPd = rP + d;

            // ---- slides
            bool point = D6PointLocked(j);
            var limited = Vec3.Zero;
            bool anyLimited = false;
            for (int k = 0; k < 3; k++)
            {
                var m = j.Linear(k);
                if (m == AxisMotion.Limited && j.LinearLimit <= 0) m = AxisMotion.Locked;
                if (m == AxisMotion.Locked && !point) AddLinear(ji, RoleLin0 + k, p, c, rPd, rC, Ax(k), d.Dot(Ax(k)));
                else j.Impulse[RoleLin0 + k] = 0;
                if (m == AxisMotion.Limited)
                {
                    limited += Ax(k) * d.Dot(Ax(k));
                    anyLimited = true;
                }
            }

            double len = limited.Length;
            if (anyLimited && len > 1e-9)
            {
                // One row along where the anchor has gone: a sphere, a disc or a segment of radius LinearLimit.
                var n = limited / len;
                var lc = n;
                var ac = rC.Cross(n);
                var lp = -n;
                var ap = -rPd.Cross(n);
                AddRow(new Row
                {
                    P = p, C = c, Joint = ji, Slot = RoleLinLimit, LP = -lp, AP = -ap, LC = -lc, AC = -ac, Mass = RowMass(p, c, lp, ap, lc, ac),
                    Err = j.LinearLimit - len, Type = RowType.Limit
                });
            }
            else j.Impulse[RoleLinLimit] = 0;

            if (j.LinearDriveStiffness > 0 || j.LinearDriveDamping > 0)
            {
                double ks = Math.Max(0, j.LinearDriveStiffness), kd = Math.Max(0, j.LinearDriveDamping), den = kd + h * ks;
                double maxImp = double.IsPositiveInfinity(j.LinearDriveMaxForce) ? double.PositiveInfinity : Math.Max(0, j.LinearDriveMaxForce) * h;
                for (int k = 0; k < 3; k++)
                {
                    if (j.Linear(k) == AxisMotion.Locked)
                    {
                        j.Impulse[RoleLinDrive0 + k] = 0;
                        continue;
                    }

                    var a = Ax(k);
                    var lc = a;
                    var ac = rC.Cross(a);
                    var lp = -a;
                    var ap = -rPd.Cross(a);
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleLinDrive0 + k, LP = lp, AP = ap, LC = lc, AC = ac, Mass = RowMass(p, c, lp, ap, lc, ac),
                        Err = d.Dot(a) - Of(j.TargetOffset, k), Target = Of(j.TargetVelocity, k), Lo = -maxImp, Hi = maxImp, Type = RowType.Spring, Gamma = 1 / (h * den), BiasCoef = ks / den
                    });
                }
            }
            else ZeroSlots(j, RoleLinDrive0, 3);

            // ---- turns
            var (twist, swing1, swing2) = SceneJoint.SwingTwist(fP.Conjugate * fC);
            j.Twist = twist;
            j.Swing1 = swing1;
            j.Swing2 = swing2;
            var mx = j.AngularMotionX;
            var my = j.AngularMotionY;
            var mz = j.AngularMotionZ;
            if (my == AxisMotion.Limited && j.Swing1Limit < 1.7e-4) my = AxisMotion.Locked;
            if (mz == AxisMotion.Limited && j.Swing2Limit < 1.7e-4) mz = AxisMotion.Locked;
            if (mx == AxisMotion.Limited && !j.Lower.HasValue && !j.Upper.HasValue) mx = AxisMotion.Free;

            if (mx == AxisMotion.Locked && my == AxisMotion.Locked && mz == AxisMotion.Locked)
            {
                // As a fixed joint: the whole orientation error at once.
                var qe = fC * fP.Conjugate;
                double sgn = qe.W < 0 ? -1 : 1;
                var e = new Vec3(qe.X, qe.Y, qe.Z) * (2 * sgn);
                AddAngular(ji, RoleAng0, p, c, Vec3.UnitX, e.X);
                AddAngular(ji, RoleAng1, p, c, Vec3.UnitY, e.Y);
                AddAngular(ji, RoleAng2, p, c, Vec3.UnitZ, e.Z);
                j.Impulse[RoleLower] = j.Impulse[RoleUpper] = 0;
                ZeroSlots(j, RoleSwingA, 4);
            }
            else
            {
                // The twist turns about the mean of the parent's and the child's X; the swings about the parent's Y and Z.
                var tw = ax0 + fC.Rotate(Vec3.UnitX);
                tw = tw.Length > 1e-6 ? tw.Normalized() : ax0;
                if (mx == AxisMotion.Locked) AddAngular(ji, RoleAng0, p, c, tw, twist);
                else j.Impulse[RoleAng0] = 0;
                if (mx == AxisMotion.Limited)
                {
                    double lo = j.Lower ?? -Math.PI, hi = j.Upper ?? Math.PI;
                    AngularLimit(ji, RoleLower, p, c, tw, twist - lo);
                    AngularLimit(ji, RoleUpper, p, c, -tw, hi - twist);
                }
                else j.Impulse[RoleLower] = j.Impulse[RoleUpper] = 0;

                if (my == AxisMotion.Locked) AddAngular(ji, RoleAng1, p, c, ax1, swing1);
                else j.Impulse[RoleAng1] = 0;
                if (mz == AxisMotion.Locked) AddAngular(ji, RoleAng2, p, c, ax2, swing2);
                else j.Impulse[RoleAng2] = 0;

                if (my == AxisMotion.Limited && mz == AxisMotion.Limited)
                {
                    // The elliptic cone: one row along the ellipse's normal where the swing crosses it.
                    double ly = j.Swing1Limit, lz = j.Swing2Limit;
                    double fr = Math.Sqrt(swing1 / ly * (swing1 / ly) + swing2 / lz * (swing2 / lz));
                    if (fr > 1e-9)
                    {
                        double by = swing1 / fr, bz = swing2 / fr;
                        double gy = by / (ly * ly), gz = bz / (lz * lz), gl = Math.Sqrt(gy * gy + gz * gz);
                        gy /= gl;
                        gz /= gl;
                        var dir = ax1 * gy + ax2 * gz;
                        AngularLimit(ji, RoleSwingA, p, c, -dir, by * gy + bz * gz - (swing1 * gy + swing2 * gz));
                    }
                    else j.Impulse[RoleSwingA] = 0;

                    ZeroSlots(j, RoleSwingB, 3);
                }
                else
                {
                    if (my == AxisMotion.Limited)
                    {
                        AngularLimit(ji, RoleSwingA, p, c, ax1, swing1 + j.Swing1Limit);
                        AngularLimit(ji, RoleSwingB, p, c, -ax1, j.Swing1Limit - swing1);
                    }
                    else ZeroSlots(j, RoleSwingA, 2);

                    if (mz == AxisMotion.Limited)
                    {
                        AngularLimit(ji, RoleSwingC, p, c, ax2, swing2 + j.Swing2Limit);
                        AngularLimit(ji, RoleSwingD, p, c, -ax2, j.Swing2Limit - swing2);
                    }
                    else ZeroSlots(j, RoleSwingC, 2);
                }
            }

            if ((j.AngularDriveStiffness > 0 || j.AngularDriveDamping > 0) && !(mx == AxisMotion.Locked && my == AxisMotion.Locked && mz == AxisMotion.Locked))
            {
                // Unity's slerp drive: toward the target orientation along the whole error, one soft row per free turn.
                double ks = Math.Max(0, j.AngularDriveStiffness), kd = Math.Max(0, j.AngularDriveDamping), den = kd + h * ks;
                double maxImp = double.IsPositiveInfinity(j.AngularDriveMaxTorque) ? double.PositiveInfinity : Math.Max(0, j.AngularDriveMaxTorque) * h;
                var qe = fC * (fP * j.TargetRotation).Conjugate;
                double sgn = qe.W < 0 ? -1 : 1;
                var e = new Vec3(qe.X, qe.Y, qe.Z) * (2 * sgn);
                var spin = fP.Rotate(j.TargetAngularVelocity);
                for (int k = 0; k < 3; k++)
                {
                    if ((k == 0 ? mx : k == 1 ? my : mz) == AxisMotion.Locked)
                    {
                        j.Impulse[RoleAngDrive0 + k] = 0;
                        continue;
                    }

                    var dir = Ax(k);
                    AddRow(new Row
                    {
                        P = p, C = c, Joint = ji, Slot = RoleAngDrive0 + k, AP = -dir, AC = dir, Mass = RowMass(p, c, Vec3.Zero, -dir, Vec3.Zero, dir),
                        Err = e.Dot(dir), Target = spin.Dot(dir), Lo = -maxImp, Hi = maxImp, Type = RowType.Spring, Gamma = 1 / (h * den), BiasCoef = ks / den,
                        Angular = true
                    });
                }
            }
            else ZeroSlots(j, RoleAngDrive0, 3);
        }

        /// <summary>A one-sided angular limit: a positive impulse turns the child about <paramref name="dir"/>; <paramref name="err"/> is the room left.</summary>
        private void AngularLimit(int ji, int slot, int p, int c, Vec3 dir, double err)
        {
            AddRow(new Row
            {
                P = p, C = c, Joint = ji, Slot = slot, AP = -dir, AC = dir, Mass = RowMass(p, c, Vec3.Zero, -dir, Vec3.Zero, dir), Err = err,
                Type = RowType.Limit, Angular = true
            });
        }
    }
}
