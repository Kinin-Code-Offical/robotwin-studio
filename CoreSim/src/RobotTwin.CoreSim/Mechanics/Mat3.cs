using System;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// Row-major 3×3 matrix (used for inertia tensors and rotation matrices).
    /// <c>M[r, c]</c>; a rotation matrix's columns are the rotated frame's axes.
    /// </summary>
    public readonly struct Mat3 : IEquatable<Mat3>
    {
        public Mat3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
        {
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
            M20 = m20; M21 = m21; M22 = m22;
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

        public static readonly Mat3 Zero = new Mat3(0, 0, 0, 0, 0, 0, 0, 0, 0);
        public static readonly Mat3 Identity = new Mat3(1, 0, 0, 0, 1, 0, 0, 0, 1);

        public double this[int r, int c]
        {
            get
            {
                switch (r * 3 + c)
                {
                    case 0: return M00;
                    case 1: return M01;
                    case 2: return M02;
                    case 3: return M10;
                    case 4: return M11;
                    case 5: return M12;
                    case 6: return M20;
                    case 7: return M21;
                    case 8: return M22;
                    default: throw new ArgumentOutOfRangeException(nameof(r));
                }
            }
        }

        public static Mat3 Diagonal(Vec3 d) => new Mat3(d.X, 0, 0, 0, d.Y, 0, 0, 0, d.Z);

        /// <summary>Symmetric matrix from its diagonal and the off-diagonal entries (xy, xz, yz).</summary>
        public static Mat3 Symmetric(double xx, double yy, double zz, double xy, double xz, double yz) =>
            new Mat3(xx, xy, xz, xy, yy, yz, xz, yz, zz);

        public static Mat3 FromColumns(Vec3 c0, Vec3 c1, Vec3 c2) =>
            new Mat3(c0.X, c1.X, c2.X, c0.Y, c1.Y, c2.Y, c0.Z, c1.Z, c2.Z);

        public Vec3 Column(int c) => new Vec3(this[0, c], this[1, c], this[2, c]);
        public Vec3 Diag => new Vec3(M00, M11, M22);
        public double Trace => M00 + M11 + M22;

        public double Determinant =>
            M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20);

        public Mat3 Transpose() => new Mat3(M00, M10, M20, M01, M11, M21, M02, M12, M22);

        /// <summary>Cross-product matrix: Skew(a)·b = a × b.</summary>
        public static Mat3 Skew(Vec3 a) => new Mat3(0, -a.Z, a.Y, a.Z, 0, -a.X, -a.Y, a.X, 0);

        /// <summary>Inverse (zero matrix when singular).</summary>
        public Mat3 Inverse()
        {
            double c00 = M11 * M22 - M12 * M21, c01 = M12 * M20 - M10 * M22, c02 = M10 * M21 - M11 * M20;
            double det = M00 * c00 + M01 * c01 + M02 * c02;
            if (Math.Abs(det) < 1e-300) return Zero;
            double inv = 1.0 / det;
            return new Mat3(
                c00 * inv, (M02 * M21 - M01 * M22) * inv, (M01 * M12 - M02 * M11) * inv,
                c01 * inv, (M00 * M22 - M02 * M20) * inv, (M02 * M10 - M00 * M12) * inv,
                c02 * inv, (M01 * M20 - M00 * M21) * inv, (M00 * M11 - M01 * M10) * inv);
        }

        /// <summary>Outer product a·bᵀ.</summary>
        public static Mat3 Outer(Vec3 a, Vec3 b) =>
            new Mat3(a.X * b.X, a.X * b.Y, a.X * b.Z, a.Y * b.X, a.Y * b.Y, a.Y * b.Z, a.Z * b.X, a.Z * b.Y, a.Z * b.Z);

        public static Mat3 operator +(Mat3 a, Mat3 b) => new Mat3(
            a.M00 + b.M00, a.M01 + b.M01, a.M02 + b.M02,
            a.M10 + b.M10, a.M11 + b.M11, a.M12 + b.M12,
            a.M20 + b.M20, a.M21 + b.M21, a.M22 + b.M22);

        public static Mat3 operator -(Mat3 a, Mat3 b) => a + b * -1.0;

        public static Mat3 operator *(Mat3 a, double s) => new Mat3(
            a.M00 * s, a.M01 * s, a.M02 * s, a.M10 * s, a.M11 * s, a.M12 * s, a.M20 * s, a.M21 * s, a.M22 * s);

        public static Mat3 operator *(double s, Mat3 a) => a * s;

        public static Vec3 operator *(Mat3 m, Vec3 v) => new Vec3(
            m.M00 * v.X + m.M01 * v.Y + m.M02 * v.Z,
            m.M10 * v.X + m.M11 * v.Y + m.M12 * v.Z,
            m.M20 * v.X + m.M21 * v.Y + m.M22 * v.Z);

        public static Mat3 operator *(Mat3 a, Mat3 b) => new Mat3(
            a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20, a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21, a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
            a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20, a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21, a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
            a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20, a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21, a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);

        /// <summary>R·diag(d)·Rᵀ for a rotation given as a quaternion (world tensor of principal moments).</summary>
        public static Mat3 RotatedDiagonal(Quat q, Vec3 d)
        {
            var r = FromQuat(q);
            double a00 = r.M00 * d.X, a01 = r.M01 * d.Y, a02 = r.M02 * d.Z;
            double a10 = r.M10 * d.X, a11 = r.M11 * d.Y, a12 = r.M12 * d.Z;
            double a20 = r.M20 * d.X, a21 = r.M21 * d.Y, a22 = r.M22 * d.Z;
            double m00 = a00 * r.M00 + a01 * r.M01 + a02 * r.M02;
            double m01 = a00 * r.M10 + a01 * r.M11 + a02 * r.M12;
            double m02 = a00 * r.M20 + a01 * r.M21 + a02 * r.M22;
            double m11 = a10 * r.M10 + a11 * r.M11 + a12 * r.M12;
            double m12 = a10 * r.M20 + a11 * r.M21 + a12 * r.M22;
            double m22 = a20 * r.M20 + a21 * r.M21 + a22 * r.M22;
            return new Mat3(m00, m01, m02, m01, m11, m12, m02, m12, m22);
        }

        /// <summary>Rotation matrix of a unit quaternion (columns = rotated axes).</summary>
        public static Mat3 FromQuat(Quat q)
        {
            q = q.Normalized();
            double w = q.W, x = q.X, y = q.Y, z = q.Z;
            return new Mat3(
                1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y),
                2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x),
                2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y));
        }

        /// <summary>Quaternion of a proper rotation matrix (Shepperd's method).</summary>
        public Quat ToQuat()
        {
            double tr = Trace;
            double w, x, y, z;
            if (tr > 0)
            {
                double s = Math.Sqrt(tr + 1.0) * 2;
                w = 0.25 * s;
                x = (M21 - M12) / s;
                y = (M02 - M20) / s;
                z = (M10 - M01) / s;
            }
            else if (M00 > M11 && M00 > M22)
            {
                double s = Math.Sqrt(1.0 + M00 - M11 - M22) * 2;
                w = (M21 - M12) / s;
                x = 0.25 * s;
                y = (M01 + M10) / s;
                z = (M02 + M20) / s;
            }
            else if (M11 > M22)
            {
                double s = Math.Sqrt(1.0 + M11 - M00 - M22) * 2;
                w = (M02 - M20) / s;
                x = (M01 + M10) / s;
                y = 0.25 * s;
                z = (M12 + M21) / s;
            }
            else
            {
                double s = Math.Sqrt(1.0 + M22 - M00 - M11) * 2;
                w = (M10 - M01) / s;
                x = (M02 + M20) / s;
                y = (M12 + M21) / s;
                z = 0.25 * s;
            }

            var q = new Quat(w, x, y, z).Normalized();
            return q.W < 0 ? new Quat(-q.W, -q.X, -q.Y, -q.Z) : q;
        }

        /// <summary>R·M·Rᵀ: re-expresses a tensor given in a frame rotated by <paramref name="rotation"/>.</summary>
        public Mat3 Rotated(Quat rotation)
        {
            var r = FromQuat(rotation);
            return r * this * r.Transpose();
        }

        public double MaxAbs()
        {
            double m = 0;
            for (int i = 0; i < 9; i++) m = Math.Max(m, Math.Abs(this[i / 3, i % 3]));
            return m;
        }

        /// <summary>
        /// Eigen-decomposition of a symmetric matrix with cyclic Jacobi rotations:
        /// this = V·diag(values)·Vᵀ. V is a proper rotation (det +1) whose columns are
        /// matched to the x, y, z axes as closely as possible, so an already diagonal
        /// tensor returns V = I with the values in x, y, z order.
        /// </summary>
        public (Vec3 Values, Mat3 Vectors) SymmetricEigen()
        {
            var a = new double[3, 3];
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                a[r, c] = 0.5 * (this[r, c] + this[c, r]);
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

            double scale = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (r != c) scale = Math.Max(scale, Math.Abs(a[r, c]));
            if (scale == 0) return (Vec3.Zero, Identity);

            // Off-diagonal round-off noise would otherwise rotate degenerate pairs by 45°.
            double noise = 1e-13 * scale;
            for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (r != c && Math.Abs(a[r, c]) < noise) a[r, c] = 0;

            for (int sweep = 0; sweep < 64; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off <= 1e-15 * scale) break;
                for (int p = 0; p < 2; p++)
                {
                    for (int q = p + 1; q < 3; q++)
                    {
                        double apq = a[p, q];
                        if (Math.Abs(apq) <= 1e-18 * scale) continue;
                        double theta = (a[q, q] - a[p, p]) / (2 * apq);
                        double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        if (theta == 0) t = 1;
                        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                        for (int k = 0; k < 3; k++)
                        {
                            double akp = a[k, p], akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            double apk = a[p, k], aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }

                        for (int k = 0; k < 3; k++)
                        {
                            double vkp = v[k, p], vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }

            // Assign eigenvectors to axes: best |cos| match over all permutations.
            int[][] perms = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };
            int[] best = perms[0];
            double bestScore = double.NegativeInfinity;
            foreach (var perm in perms)
            {
                // perm[axis] = eigen column for that axis; score with the column's component on that axis.
                double score = 0;
                for (int axis = 0; axis < 3; axis++) score += Math.Abs(v[axis, perm[axis]]);
                if (score > bestScore + 1e-12)
                {
                    bestScore = score;
                    best = perm;
                }
            }

            var cols = new Vec3[3];
            var vals = new double[3];
            for (int axis = 0; axis < 3; axis++)
            {
                int e = best[axis];
                var col = new Vec3(v[0, e], v[1, e], v[2, e]);
                double comp = axis == 0 ? col.X : axis == 1 ? col.Y : col.Z;
                if (comp < 0) col = -col;
                cols[axis] = col.Normalized();
                vals[axis] = a[e, e];
            }

            var m = FromColumns(cols[0], cols[1], cols[2]);
            if (m.Determinant < 0)
            {
                // Flip the axis that is least aligned with its model axis.
                int weakest = 0;
                double w = double.MaxValue;
                for (int axis = 0; axis < 3; axis++)
                {
                    double comp = Math.Abs(axis == 0 ? cols[axis].X : axis == 1 ? cols[axis].Y : cols[axis].Z);
                    if (comp < w)
                    {
                        w = comp;
                        weakest = axis;
                    }
                }

                cols[weakest] = -cols[weakest];
                m = FromColumns(cols[0], cols[1], cols[2]);
            }

            return (new Vec3(vals[0], vals[1], vals[2]), m);
        }

        public bool Equals(Mat3 o) =>
            M00 == o.M00 && M01 == o.M01 && M02 == o.M02 && M10 == o.M10 && M11 == o.M11 && M12 == o.M12 && M20 == o.M20 &&
            M21 == o.M21 && M22 == o.M22;

        public override bool Equals(object? obj) => obj is Mat3 m && Equals(m);
        public override int GetHashCode() => HashCode.Combine(M00, M11, M22, M01, M02, M12);

        public override string ToString() =>
            $"[{M00:G5} {M01:G5} {M02:G5}; {M10:G5} {M11:G5} {M12:G5}; {M20:G5} {M21:G5} {M22:G5}]";
    }
}
