using System;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>3D vector in metres / SI units. Z points up.</summary>
    public readonly struct Vec3
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => a * s;
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSquared => X * X + Y * Y + Z * Z;
        public Vec3 Normalized() => Length > 1e-12 ? this / Length : Zero;
        public Vec3 Mul(Vec3 b) => new Vec3(X * b.X, Y * b.Y, Z * b.Z);

        /// <summary>Component-wise division (zero where the divisor is zero).</summary>
        public Vec3 Div(Vec3 b) => new Vec3(b.X != 0 ? X / b.X : 0, b.Y != 0 ? Y / b.Y : 0, b.Z != 0 ? Z / b.Z : 0);

        public static Vec3 Min(Vec3 a, Vec3 b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static Vec3 Max(Vec3 a, Vec3 b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        /// <summary>Some unit vector perpendicular to this (unit) vector.</summary>
        public Vec3 AnyPerpendicular()
        {
            var a = Math.Abs(X) < 0.57 ? UnitX : Math.Abs(Y) < 0.57 ? UnitY : UnitZ;
            return Cross(a).Normalized();
        }

        public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
    }

    /// <summary>Unit quaternion for orientation (body → world).</summary>
    public readonly struct Quat
    {
        public Quat(double w, double x, double y, double z)
        {
            W = w;
            X = x;
            Y = y;
            Z = z;
        }

        public double W { get; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static readonly Quat Identity = new Quat(1, 0, 0, 0);

        public static Quat FromAxisAngle(Vec3 axis, double angle)
        {
            var a = axis.Normalized();
            double s = Math.Sin(angle / 2);
            return new Quat(Math.Cos(angle / 2), a.X * s, a.Y * s, a.Z * s);
        }

        public static Quat FromYaw(double yaw) => FromAxisAngle(Vec3.UnitZ, yaw);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

        public Quat Conjugate => new Quat(W, -X, -Y, -Z);

        public Quat Normalized()
        {
            double n = Math.Sqrt(W * W + X * X + Y * Y + Z * Z);
            return n < 1e-12 ? Identity : new Quat(W / n, X / n, Y / n, Z / n);
        }

        /// <summary>Rotates a body-frame vector into the world frame.</summary>
        public Vec3 Rotate(Vec3 v)
        {
            var u = new Vec3(X, Y, Z);
            var t = 2.0 * u.Cross(v);
            return v + W * t + u.Cross(t);
        }

        /// <summary>Rotates a world-frame vector into the body frame.</summary>
        public Vec3 InverseRotate(Vec3 v) => Conjugate.Rotate(v);

        /// <summary>Roll (about x), pitch (about y), yaw (about z) in radians.</summary>
        public (double Roll, double Pitch, double Yaw) ToEuler()
        {
            double sinr = 2 * (W * X + Y * Z), cosr = 1 - 2 * (X * X + Y * Y);
            double roll = Math.Atan2(sinr, cosr);
            double sinp = 2 * (W * Y - Z * X);
            double pitch = Math.Abs(sinp) >= 1 ? Math.Sign(sinp) * Math.PI / 2 : Math.Asin(sinp);
            double siny = 2 * (W * Z + X * Y), cosy = 1 - 2 * (Y * Y + Z * Z);
            return (roll, pitch, Math.Atan2(siny, cosy));
        }
    }
}
