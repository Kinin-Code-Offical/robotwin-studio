using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>2D point / vector (sketch plane, millimetres).</summary>
    public readonly struct Vec2
    {
        public Vec2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        public double Dot(Vec2 b) => X * b.X + Y * b.Y;

        /// <summary>Z of the 3D cross product (positive when b is counter-clockwise from this).</summary>
        public double Cross(Vec2 b) => X * b.Y - Y * b.X;

        public double Length => Math.Sqrt(X * X + Y * Y);
        public Vec2 Rotated(double radians) => new Vec2(X * Math.Cos(radians) - Y * Math.Sin(radians), X * Math.Sin(radians) + Y * Math.Cos(radians));
        public override string ToString() => $"({X:0.###}, {Y:0.###})";
    }

    /// <summary>
    /// Affine transform p → M·p + T (3×3 linear part, may contain non-uniform scale or shear).
    /// Composition <c>a * b</c> applies <c>b</c> first.
    /// </summary>
    public readonly struct Affine
    {
        public Affine(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22, double tx, double ty, double tz)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M20 = m20;
            M21 = m21;
            M22 = m22;
            Tx = tx;
            Ty = ty;
            Tz = tz;
        }

        public double M00 { get; }
        public double M01 { get; }
        public double M02 { get; }
        public double M10 { get; }
        public double M11 { get; }
        public double M12 { get; }
        public double M20 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double Tx { get; }
        public double Ty { get; }
        public double Tz { get; }

        public static readonly Affine Identity = new Affine(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);

        public Vec3 Translation => new Vec3(Tx, Ty, Tz);

        public static Affine Translate(Vec3 t) => new Affine(1, 0, 0, 0, 1, 0, 0, 0, 1, t.X, t.Y, t.Z);

        public static Affine Scale(Vec3 s) => new Affine(s.X, 0, 0, 0, s.Y, 0, 0, 0, s.Z, 0, 0, 0);

        public static Affine Scale(double s) => Scale(new Vec3(s, s, s));

        public static Affine FromQuat(Quat q)
        {
            var x = q.Rotate(Vec3.UnitX);
            var y = q.Rotate(Vec3.UnitY);
            var z = q.Rotate(Vec3.UnitZ);
            return new Affine(x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z, 0, 0, 0);
        }

        /// <summary>Rotation about an axis through the origin (degrees, right hand).</summary>
        public static Affine Rotate(Vec3 axis, double degrees) => FromQuat(Quat.FromAxisAngle(axis, degrees * Math.PI / 180));

        /// <summary>Euler XYZ in degrees (X applied first, then Y, then Z — Blender "XYZ" and WorldPose roll/pitch/yaw).</summary>
        public static Affine Euler(Vec3 degrees) => FromQuat(EulerQuat(degrees));

        public static Quat EulerQuat(Vec3 degrees)
        {
            const double k = Math.PI / 180;
            return Quat.FromAxisAngle(Vec3.UnitZ, degrees.Z * k) * Quat.FromAxisAngle(Vec3.UnitY, degrees.Y * k) * Quat.FromAxisAngle(Vec3.UnitX, degrees.X * k);
        }

        /// <summary>Translation · Rotation · Scale.</summary>
        public static Affine Trs(Vec3 position, Vec3 rotationDegrees, Vec3 scale) => Translate(position) * Euler(rotationDegrees) * Scale(scale);

        /// <summary>Mirror across the plane through <paramref name="point"/> with normal <paramref name="normal"/>.</summary>
        public static Affine Mirror(Vec3 normal, Vec3 point)
        {
            var n = normal.Normalized();
            var lin = new Affine(1 - 2 * n.X * n.X, -2 * n.X * n.Y, -2 * n.X * n.Z, -2 * n.Y * n.X, 1 - 2 * n.Y * n.Y, -2 * n.Y * n.Z, -2 * n.Z * n.X, -2 * n.Z * n.Y, 1 - 2 * n.Z * n.Z, 0, 0, 0);
            return Translate(point) * lin * Translate(-point);
        }

        public static Affine operator *(Affine a, Affine b) => new Affine(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
            a.M00 * b.Tx + a.M01 * b.Ty + a.M02 * b.Tz + a.Tx, a.M10 * b.Tx + a.M11 * b.Ty + a.M12 * b.Tz + a.Ty, a.M20 * b.Tx + a.M21 * b.Ty + a.M22 * b.Tz + a.Tz);

        public Vec3 Apply(Vec3 p) => new Vec3(M00 * p.X + M01 * p.Y + M02 * p.Z + Tx, M10 * p.X + M11 * p.Y + M12 * p.Z + Ty, M20 * p.X + M21 * p.Y + M22 * p.Z + Tz);

        public Vec3 ApplyVector(Vec3 v) => new Vec3(M00 * v.X + M01 * v.Y + M02 * v.Z, M10 * v.X + M11 * v.Y + M12 * v.Z, M20 * v.X + M21 * v.Y + M22 * v.Z);

        public double Determinant =>
            M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);

        public Affine Inverse()
        {
            double det = Determinant;
            if (Math.Abs(det) < 1e-300) return Identity;
            double inv = 1.0 / det;
            double a00 = (M11 * M22 - M12 * M21) * inv, a01 = (M02 * M21 - M01 * M22) * inv, a02 = (M01 * M12 - M02 * M11) * inv;
            double a10 = (M12 * M20 - M10 * M22) * inv, a11 = (M00 * M22 - M02 * M20) * inv, a12 = (M02 * M10 - M00 * M12) * inv;
            double a20 = (M10 * M21 - M11 * M20) * inv, a21 = (M01 * M20 - M00 * M21) * inv, a22 = (M00 * M11 - M01 * M10) * inv;
            return new Affine(a00, a01, a02, a10, a11, a12, a20, a21, a22,
                -(a00 * Tx + a01 * Ty + a02 * Tz), -(a10 * Tx + a11 * Ty + a12 * Tz), -(a20 * Tx + a21 * Ty + a22 * Tz));
        }

        /// <summary>Column lengths: the scale along the local axes.</summary>
        public Vec3 AxisScale => new Vec3(new Vec3(M00, M10, M20).Length, new Vec3(M01, M11, M21).Length, new Vec3(M02, M12, M22).Length);

        public override string ToString() => $"[{M00:0.###} {M01:0.###} {M02:0.###} | {Tx:0.###}; {M10:0.###} {M11:0.###} {M12:0.###} | {Ty:0.###}; {M20:0.###} {M21:0.###} {M22:0.###} | {Tz:0.###}]";
    }
}
