using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>Rigid transform (rotation + translation, metres): <c>p_world = P + Q·p_local</c>.</summary>
    public readonly struct Transform3
    {
        public Transform3(Vec3 position, Quat rotation)
        {
            P = position;
            Q = rotation;
        }

        public Vec3 P { get; }
        public Quat Q { get; }

        public static readonly Transform3 Identity = new Transform3(Vec3.Zero, Quat.Identity);

        public static Transform3 Translation(double x, double y, double z) => new Transform3(new Vec3(x, y, z), Quat.Identity);

        public static Transform3 Rotation(Vec3 axis, double angle) => new Transform3(Vec3.Zero, Quat.FromAxisAngle(axis, angle));

        /// <summary>a ∘ b: first b, then a.</summary>
        public static Transform3 operator *(Transform3 a, Transform3 b) => new Transform3(a.P + a.Q.Rotate(b.P), (a.Q * b.Q).Normalized());

        public Vec3 Apply(Vec3 local) => P + Q.Rotate(local);

        public Vec3 ApplyVector(Vec3 local) => Q.Rotate(local);

        public Transform3 Inverse()
        {
            var qi = Q.Conjugate;
            return new Transform3(-qi.Rotate(P), qi);
        }

        public Vec3 XAxis => Q.Rotate(Vec3.UnitX);
        public Vec3 YAxis => Q.Rotate(Vec3.UnitY);
        public Vec3 ZAxis => Q.Rotate(Vec3.UnitZ);

        /// <summary>Pose from position (m) and roll/pitch/yaw (rad) with R = Rz(yaw)·Ry(pitch)·Rx(roll).</summary>
        public static Transform3 FromRpy(double x, double y, double z, double roll, double pitch, double yaw) =>
            new Transform3(new Vec3(x, y, z), Rpy(roll, pitch, yaw));

        public static Quat Rpy(double roll, double pitch, double yaw) =>
            (Quat.FromAxisAngle(Vec3.UnitZ, yaw) * Quat.FromAxisAngle(Vec3.UnitY, pitch) * Quat.FromAxisAngle(Vec3.UnitX, roll)).Normalized();

        /// <summary>Roll, pitch, yaw (rad) of the rotation (R = Rz·Ry·Rx).</summary>
        public (double Roll, double Pitch, double Yaw) ToRpy() => Q.ToEuler();

        /// <summary>Angle (rad) of the rotation taking a to b.</summary>
        public static double AngleBetween(Quat a, Quat b)
        {
            var d = (a.Conjugate * b).Normalized();
            double w = Math.Min(1.0, Math.Abs(d.W));
            return 2 * Math.Acos(w);
        }

        /// <summary>Rotation vector (axis·angle, rad) of the relative rotation a⁻¹·b expressed in the world: log(b·a⁻¹).</summary>
        public static Vec3 RotationError(Quat from, Quat to)
        {
            var d = (to * from.Conjugate).Normalized();
            if (d.W < 0) d = new Quat(-d.W, -d.X, -d.Y, -d.Z);
            var v = new Vec3(d.X, d.Y, d.Z);
            double s = v.Length;
            if (s < 1e-12) return Vec3.Zero;
            double angle = 2 * Math.Atan2(s, d.W);
            return v * (angle / s);
        }

        /// <summary>Spherical interpolation of unit quaternions.</summary>
        public static Quat Slerp(Quat a, Quat b, double t)
        {
            double dot = a.W * b.W + a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            if (dot < 0)
            {
                b = new Quat(-b.W, -b.X, -b.Y, -b.Z);
                dot = -dot;
            }

            if (dot > 0.9995)
            {
                return new Quat(a.W + (b.W - a.W) * t, a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t).Normalized();
            }

            double th = Math.Acos(dot), s = Math.Sin(th);
            double wa = Math.Sin((1 - t) * th) / s, wb = Math.Sin(t * th) / s;
            return new Quat(a.W * wa + b.W * wb, a.X * wa + b.X * wb, a.Y * wa + b.Y * wb, a.Z * wa + b.Z * wb).Normalized();
        }

        public static Transform3 Lerp(Transform3 a, Transform3 b, double t) => new Transform3(a.P + (b.P - a.P) * t, Slerp(a.Q, b.Q, t));

        public override string ToString()
        {
            var (r, p, y) = ToRpy();
            return $"({P.X * 1000:0.#}, {P.Y * 1000:0.#}, {P.Z * 1000:0.#}) mm · ({r * 180 / Math.PI:0.#}, {p * 180 / Math.PI:0.#}, {y * 180 / Math.PI:0.#})°";
        }
    }
}
