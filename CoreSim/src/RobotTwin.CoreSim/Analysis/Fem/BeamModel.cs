using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analysis.Numerics;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis.Fem
{
    /// <summary>Cross-section of a beam: rectangle (width along local y, height along local z) or solid circle.</summary>
    public sealed class BeamSection
    {
        public bool Circular { get; set; }
        public double Width { get; set; } = 0.01;
        public double Height { get; set; } = 0.01;
        public double Diameter { get; set; } = 0.01;

        public double Area => Circular ? Math.PI * Diameter * Diameter / 4 : Width * Height;

        /// <summary>Second moment about local y (bending in the x–z plane).</summary>
        public double Iy => Circular ? Math.PI * Math.Pow(Diameter, 4) / 64 : Width * Math.Pow(Height, 3) / 12;

        /// <summary>Second moment about local z (bending in the x–y plane).</summary>
        public double Iz => Circular ? Math.PI * Math.Pow(Diameter, 4) / 64 : Height * Math.Pow(Width, 3) / 12;

        /// <summary>Saint-Venant torsion constant.</summary>
        public double J
        {
            get
            {
                if (Circular) return Math.PI * Math.Pow(Diameter, 4) / 32;
                double a = Math.Max(Width, Height), b = Math.Min(Width, Height);
                return a * b * b * b * (1.0 / 3 - 0.21 * b / a * (1 - Math.Pow(b / a, 4) / 12));
            }
        }

        /// <summary>Largest torsional shear stress per unit torque (1/m³).</summary>
        public double TorsionStressPerTorque
        {
            get
            {
                if (Circular) return Diameter / 2 / J;
                double a = Math.Max(Width, Height), b = Math.Min(Width, Height);
                double alpha = 1.0 / (3 + 1.8 * b / a);
                return 1 / (alpha * a * b * b);
            }
        }
    }

    /// <summary>Result of a beam solve: nodal displacements/rotations and stress along the beam.</summary>
    public sealed class BeamSolution
    {
        public double Time { get; set; }

        /// <summary>6 per node: ux uy uz θx θy θz (global frame).</summary>
        public double[] Dof { get; set; } = Array.Empty<double>();

        /// <summary>Largest von Mises stress at each node (outer fibres, Pa).</summary>
        public double[] VonMises { get; set; } = Array.Empty<double>();

        /// <summary>Axial force, shear y/z, torque, bending moments y/z at each node (local frame, N and N·m).</summary>
        public double[] InternalForces { get; set; } = Array.Empty<double>();

        public double MaxVonMises { get; set; }
        public double MaxDisplacement { get; set; }
        public double MinSafetyFactor { get; set; }

        public Vec3 Displacement(int node) => new Vec3(Dof[6 * node], Dof[6 * node + 1], Dof[6 * node + 2]);
    }

    /// <summary>
    /// [analysis] Straight 3D Euler–Bernoulli frame (12-DOF elements: axial, torsion, bending in two
    /// planes) for slender links. Uniform section and material; loads are point forces/moments at
    /// nodes plus body accelerations; supports are clamped/pinned nodes or inertia relief.
    /// </summary>
    public sealed class BeamModel
    {
        public BeamModel(Vec3 start, Vec3 end, int elements, BeamSection section, StructuralMaterial material, Vec3? up = null)
        {
            Start = start;
            End = end;
            Elements = Math.Max(1, elements);
            Section = section;
            Material = material;
            Axis = (end - start).Normalized();
            var u = up ?? Vec3.UnitZ;
            if (Math.Abs(u.Normalized().Dot(Axis)) > 0.95) u = Math.Abs(Axis.Dot(Vec3.UnitX)) < 0.9 ? Vec3.UnitX : Vec3.UnitY;
            // z = the "up" direction made perpendicular to the axis, y = z × x (right-handed x, y, z).
            LocalZ = (u - Axis * u.Dot(Axis)).Normalized();
            LocalY = LocalZ.Cross(Axis).Normalized();
            Fixed = new bool[NodeCount * 6];
        }

        public Vec3 Start { get; }
        public Vec3 End { get; }
        public int Elements { get; }
        public int NodeCount => Elements + 1;
        public BeamSection Section { get; }
        public StructuralMaterial Material { get; }
        public Vec3 Axis { get; }
        public Vec3 LocalY { get; private set; }
        public Vec3 LocalZ { get; private set; }
        public double Length => (End - Start).Length;
        public bool[] Fixed { get; }
        public Vec3 NodePosition(int i) => Start + (End - Start) * ((double)i / Elements);

        public void Clamp(int node)
        {
            for (int i = 0; i < 6; i++) Fixed[node * 6 + i] = true;
        }

        public int NearestNode(Vec3 p)
        {
            double t = (p - Start).Dot(Axis) / Math.Max(1e-12, Length);
            return Math.Max(0, Math.Min(Elements, (int)Math.Round(t * Elements)));
        }

        /// <summary>Element stiffness in local coordinates (12×12, order: u1 v1 w1 θx1 θy1 θz1, u2 …).</summary>
        private double[] LocalStiffness()
        {
            double e = Material.E, g = Material.ShearModulus, l = Length / Elements;
            double a = Section.Area, iy = Section.Iy, iz = Section.Iz, j = Section.J;
            var k = new double[144];
            void S(int r, int c, double v)
            {
                k[r * 12 + c] += v;
                if (r != c) k[c * 12 + r] += v;
            }

            double ea = e * a / l, gj = g * j / l;
            S(0, 0, ea); S(6, 6, ea); S(0, 6, -ea);
            S(3, 3, gj); S(9, 9, gj); S(3, 9, -gj);
            // bending in x–y plane (v, θz) with Iz
            double z12 = 12 * e * iz / (l * l * l), z6 = 6 * e * iz / (l * l), z4 = 4 * e * iz / l, z2 = 2 * e * iz / l;
            S(1, 1, z12); S(7, 7, z12); S(1, 7, -z12);
            S(1, 5, z6); S(1, 11, z6); S(5, 7, -z6); S(7, 11, -z6);
            S(5, 5, z4); S(11, 11, z4); S(5, 11, z2);
            // bending in x–z plane (w, θy) with Iy (sign flips: θy = −dw/dx)
            double y12 = 12 * e * iy / (l * l * l), y6 = 6 * e * iy / (l * l), y4 = 4 * e * iy / l, y2 = 2 * e * iy / l;
            S(2, 2, y12); S(8, 8, y12); S(2, 8, -y12);
            S(2, 4, -y6); S(2, 10, -y6); S(4, 8, y6); S(8, 10, y6);
            S(4, 4, y4); S(10, 10, y4); S(4, 10, y2);
            return k;
        }

        private double[] Rotation()
        {
            // rows: local axes in global coordinates
            return new[] { Axis.X, Axis.Y, Axis.Z, LocalY.X, LocalY.Y, LocalY.Z, LocalZ.X, LocalZ.Y, LocalZ.Z };
        }

        /// <summary>Solves for nodal forces/moments f (6 per node, global). Banded elimination (half bandwidth 11).</summary>
        public BeamSolution Solve(double[] f)
        {
            int n = NodeCount * 6;
            const int bw = 12;
            var band = new double[n, 2 * bw + 1];
            var kl = LocalStiffness();
            var r = Rotation();
            // global element matrix: T^T k T, T = blockdiag(R,R,R,R)
            var kg = new double[144];
            for (int i = 0; i < 12; i++)
            for (int j = 0; j < 12; j++)
            {
                double s = 0;
                int bi = i / 3, ii = i % 3, bj = j / 3, jj = j % 3;
                for (int p = 0; p < 3; p++)
                for (int q = 0; q < 3; q++)
                    s += r[p * 3 + ii] * kl[(bi * 3 + p) * 12 + bj * 3 + q] * r[q * 3 + jj];
                kg[i * 12 + j] = s;
            }

            for (int e = 0; e < Elements; e++)
            {
                for (int i = 0; i < 12; i++)
                for (int j = 0; j < 12; j++)
                {
                    int gi = e * 6 + i, gj = e * 6 + j;
                    band[gi, gj - gi + bw] += kg[i * 12 + j];
                }
            }

            var b = (double[])f.Clone();
            for (int i = 0; i < n; i++)
            {
                if (!Fixed[i]) continue;
                for (int c = 0; c <= 2 * bw; c++) band[i, c] = 0;
                for (int rr = Math.Max(0, i - bw); rr <= Math.Min(n - 1, i + bw); rr++) band[rr, i - rr + bw] = 0;
                band[i, bw] = 1;
                b[i] = 0;
            }

            // Gaussian elimination in the band (SPD after the fixed rows, no pivoting needed).
            for (int k = 0; k < n; k++)
            {
                double piv = band[k, bw];
                if (Math.Abs(piv) < 1e-300) throw new InvalidOperationException("Kiriş modeli tekil (mesnet eksik)");
                for (int i = k + 1; i <= Math.Min(n - 1, k + bw); i++)
                {
                    double m = band[i, k - i + bw] / piv;
                    if (m == 0) continue;
                    for (int j = k; j <= Math.Min(n - 1, k + bw); j++) band[i, j - i + bw] -= m * band[k, j - k + bw];
                    b[i] -= m * b[k];
                }
            }

            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = b[i];
                for (int j = i + 1; j <= Math.Min(n - 1, i + bw); j++) s -= band[i, j - i + bw] * x[j];
                x[i] = s / band[i, bw];
            }

            return Recover(x, kl, r);
        }

        private BeamSolution Recover(double[] x, double[] kl, double[] r)
        {
            var sol = new BeamSolution { Dof = x };
            int nn = NodeCount;
            var vm = new double[nn];
            var forces = new double[nn * 6];
            var cnt = new int[nn];
            var dl = new double[12];
            for (int e = 0; e < Elements; e++)
            {
                for (int i = 0; i < 12; i++)
                {
                    int blk = i / 3, ii = i % 3;
                    double s = 0;
                    for (int q = 0; q < 3; q++) s += r[ii * 3 + q] * x[e * 6 + blk * 3 + q];
                    dl[i] = s;
                }

                for (int end = 0; end < 2; end++)
                {
                    var fl = new double[6];
                    for (int i = 0; i < 6; i++)
                    {
                        double s = 0;
                        for (int j = 0; j < 12; j++) s += kl[(end * 6 + i) * 12 + j] * dl[j];
                        fl[i] = end == 0 ? -s : s;   // internal force convention: positive = tension / sagging at the section
                    }

                    int node = e + end;
                    for (int i = 0; i < 6; i++) forces[node * 6 + i] += fl[i];
                    cnt[node]++;
                }
            }

            double max = 0, maxU = 0;
            for (int i = 0; i < nn; i++)
            {
                for (int k = 0; k < 6; k++) forces[i * 6 + k] /= Math.Max(1, cnt[i]);
                double nAx = forces[i * 6], t = forces[i * 6 + 3], my = forces[i * 6 + 4], mz = forces[i * 6 + 5];
                double sigma;
                if (Section.Circular)
                {
                    double m = Math.Sqrt(my * my + mz * mz);
                    sigma = Math.Abs(nAx) / Section.Area + m * Section.Diameter / 2 / Section.Iy;
                }
                else
                {
                    sigma = Math.Abs(nAx) / Section.Area + Math.Abs(my) * Section.Height / 2 / Section.Iy + Math.Abs(mz) * Section.Width / 2 / Section.Iz;
                }

                double tau = Math.Abs(t) * Section.TorsionStressPerTorque;
                vm[i] = Math.Sqrt(sigma * sigma + 3 * tau * tau);
                max = Math.Max(max, vm[i]);
                maxU = Math.Max(maxU, sol.Displacement(i).Length);
            }

            sol.VonMises = vm;
            sol.InternalForces = forces;
            sol.MaxVonMises = max;
            sol.MaxDisplacement = maxU;
            sol.MinSafetyFactor = max > 0 ? Material.Yield / max : double.PositiveInfinity;
            return sol;
        }

        /// <summary>
        /// Solves a load case on a free link with inertia relief: point loads go to the nearest node
        /// (force + offset moment), gravity and the balancing rigid acceleration are lumped at the nodes,
        /// and the node nearest the centre is clamped (its reaction is ~0 because the loads balance).
        /// </summary>
        public BeamSolution Solve(FemLoadCase lc)
        {
            int nn = NodeCount;
            var f = new double[nn * 6];
            double mTotal = Material.Density * Section.Area * Length;
            double mNode = mTotal / Elements;
            var c = (Start + End) * 0.5;
            var sumF = Vec3.Zero;
            var sumM = Vec3.Zero;
            foreach (var l in lc.Loads)
            {
                int node = NearestNode(l.Point);
                var arm = l.Point - NodePosition(node);
                var mo = arm.Cross(l.Force);
                f[node * 6] += l.Force.X;
                f[node * 6 + 1] += l.Force.Y;
                f[node * 6 + 2] += l.Force.Z;
                f[node * 6 + 3] += mo.X;
                f[node * 6 + 4] += mo.Y;
                f[node * 6 + 5] += mo.Z;
                sumF += l.Force;
                sumM += (l.Point - c).Cross(l.Force);
            }

            var nodeMass = new double[nn];
            for (int i = 0; i < nn; i++) nodeMass[i] = i == 0 || i == nn - 1 ? mNode / 2 : mNode;
            var a0 = Vec3.Zero;
            var alpha = Vec3.Zero;
            if (lc.InertiaRelief)
            {
                a0 = (sumF + lc.Gravity * mTotal) / Math.Max(1e-12, mTotal);
                // lumped inertia about c (point masses along the axis + section rotary inertia neglected for the transverse axes)
                double ia = 0;
                for (int i = 0; i < nn; i++)
                {
                    double d = (NodePosition(i) - c).Length;
                    ia += nodeMass[i] * d * d;
                }

                var axial = sumM.Dot(Axis);
                var trans = sumM - Axis * axial;
                double polar = mTotal * (Section.Circular ? Section.Diameter * Section.Diameter / 8 : (Section.Width * Section.Width + Section.Height * Section.Height) / 12);
                alpha = trans / Math.Max(1e-18, ia) + Axis * (axial / Math.Max(1e-18, polar));
            }

            for (int i = 0; i < nn; i++)
            {
                var rr = NodePosition(i) - c;
                var acc = lc.Gravity - (lc.InertiaRelief ? a0 + alpha.Cross(rr) + lc.Omega.Cross(lc.Omega.Cross(rr)) : Vec3.Zero);
                f[i * 6] += nodeMass[i] * acc.X;
                f[i * 6 + 1] += nodeMass[i] * acc.Y;
                f[i * 6 + 2] += nodeMass[i] * acc.Z;
                if (lc.InertiaRelief)
                {
                    // torsional d'Alembert moment of the section about the axis
                    double polar = nodeMass[i] * (Section.Circular ? Section.Diameter * Section.Diameter / 8 : (Section.Width * Section.Width + Section.Height * Section.Height) / 12);
                    var tm = Axis * (-polar * alpha.Dot(Axis));
                    f[i * 6 + 3] += tm.X;
                    f[i * 6 + 4] += tm.Y;
                    f[i * 6 + 5] += tm.Z;
                }
            }

            bool any = false;
            foreach (bool b in Fixed) any |= b;
            if (lc.InertiaRelief && !any) Clamp(NearestNode(c));
            var sol = Solve(f);
            sol.Time = lc.Time;
            return sol;
        }
    }
}
