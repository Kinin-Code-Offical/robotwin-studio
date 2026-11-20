using System;
using System.Collections.Generic;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Numerics;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis.Fem
{
    /// <summary>A concentrated force on the part (body frame, metres / newtons).</summary>
    public sealed class PointLoad
    {
        public PointLoad(Vec3 point, Vec3 force, string source = "")
        {
            Point = point;
            Force = force;
            Source = source;
        }

        public Vec3 Point { get; }
        public Vec3 Force { get; }
        public string Source { get; }
    }

    /// <summary>Loads on a part at one instant (body frame).</summary>
    public sealed class FemLoadCase
    {
        public double Time { get; set; }
        public List<PointLoad> Loads { get; } = new List<PointLoad>();

        /// <summary>Gravity in the body frame (m/s²).</summary>
        public Vec3 Gravity { get; set; }

        /// <summary>Angular velocity of the part (body frame, rad/s) for centrifugal loads.</summary>
        public Vec3 Omega { get; set; }

        /// <summary>
        /// Inertia relief: the part is free; the rigid-body acceleration that balances every applied
        /// load is applied as a d'Alembert body force and a statically determinate 3-2-1 support removes
        /// the rigid motion (its reactions are ~0). False = only the user's fixed supports hold the part.
        /// </summary>
        public bool InertiaRelief { get; set; } = true;
    }

    /// <summary>Result of one static solve.</summary>
    public sealed class FemSolution
    {
        public double Time { get; set; }

        /// <summary>Nodal displacement (3 per node, m).</summary>
        public double[] Displacement { get; set; } = Array.Empty<double>();

        /// <summary>Nodal stress (6 per node: σxx σyy σzz τxy τyz τzx, Pa), averaged over the elements.</summary>
        public double[] Stress { get; set; } = Array.Empty<double>();

        /// <summary>Nodal von Mises stress (Pa).</summary>
        public double[] VonMises { get; set; } = Array.Empty<double>();

        public double MaxVonMises { get; set; }
        public int MaxVonMisesNode { get; set; }
        public double MaxDisplacement { get; set; }
        public double MinSafetyFactor { get; set; }

        /// <summary>Rigid-body acceleration balancing the loads (inertia relief), body frame.</summary>
        public Vec3 ReliefAcceleration { get; set; }

        /// <summary>|support reactions| / |applied loads| — ≈0 when inertia relief balanced the part.</summary>
        public double ReactionRatio { get; set; }

        public double TotalLoad { get; set; }
        public SolveReport Solver { get; set; }

        public double DisplacementMagnitude(int node)
        {
            double x = Displacement[3 * node], y = Displacement[3 * node + 1], z = Displacement[3 * node + 2];
            return Math.Sqrt(x * x + y * y + z * z);
        }
    }

    /// <summary>
    /// [analysis] Linear static elasticity on a <see cref="TetMesh"/> (4- or 10-node tetrahedra,
    /// isotropic Hooke material, small strains). Stiffness is assembled once; any number of load
    /// cases are solved with block-Jacobi preconditioned conjugate gradients (warm started).
    /// </summary>
    public sealed class ElasticModel
    {
        private static readonly double[][] Gauss4;
        private static readonly double[] Gauss4W;
        private static readonly double[][] Gauss5;
        private static readonly double[] Gauss5W;

        static ElasticModel()
        {
            double a = 0.5854101966249685, b = 0.1381966011250105;
            Gauss4 = new[] { new[] { a, b, b, b }, new[] { b, a, b, b }, new[] { b, b, a, b }, new[] { b, b, b, a } };
            Gauss4W = new[] { 1.0 / 24, 1.0 / 24, 1.0 / 24, 1.0 / 24 };
            double s = 1.0 / 6;
            Gauss5 = new[] { new[] { 0.25, 0.25, 0.25, 0.25 }, new[] { 0.5, s, s, s }, new[] { s, 0.5, s, s }, new[] { s, s, 0.5, s }, new[] { s, s, s, 0.5 } };
            Gauss5W = new[] { -2.0 / 15, 3.0 / 40, 3.0 / 40, 3.0 / 40, 3.0 / 40 };
        }

        private readonly int _npe;
        private double[]? _nodeMass;
        private Vec3[]? _nodeMoment;
        private double[]? _lastU;
        private List<int>? _surfaceList;

        public ElasticModel(TetMesh mesh, StructuralMaterial material)
        {
            Mesh = mesh;
            Material = material;
            _npe = mesh.NodesPerElement;
            Fixed = new bool[mesh.Nodes.Count * 3];
        }

        public TetMesh Mesh { get; }
        public StructuralMaterial Material { get; }
        public SparseMatrix? Stiffness { get; private set; }

        /// <summary>Fixed displacement components (3 per node).</summary>
        public bool[] Fixed { get; }

        public double Mass { get; private set; }
        public Vec3 CenterOfMass { get; private set; }

        /// <summary>Inertia tensor about the centre of mass (row-major 3×3).</summary>
        public double[] Inertia { get; private set; } = new double[9];

        public double Tolerance { get; set; } = 1e-8;

        /// <summary>
        /// Uniform free thermal strain α·ΔT of the part (temperature above the assembly temperature).
        /// A free part just grows; where it is held it builds thermal stress σ = D(ε − α·ΔT·I).
        /// </summary>
        public double ThermalStrain { get; set; }

        /// <summary>Equivalent nodal loads of the thermal strain: ∫ Bᵀ·D·(α·ΔT·I) dV = (3λ+2μ)·α·ΔT·∫∇N dV.</summary>
        private void AddThermalLoad(double[] f)
        {
            if (ThermalStrain == 0) return;
            double k = (3 * Material.Lame + 2 * Material.ShearModulus) * ThermalStrain;
            var n = new double[10];
            var dn = new double[30];
            var g = new double[30];
            var rules = Mesh.Order == 2 ? Gauss4 : new[] { new[] { 0.25, 0.25, 0.25, 0.25 } };
            var weights = Mesh.Order == 2 ? Gauss4W : new[] { 1.0 / 6 };
            foreach (var el in Mesh.Elements)
            {
                for (int q = 0; q < rules.Length; q++)
                {
                    double w = weights[q] * Math.Abs(Gradients(el, rules[q], n, dn, g)) * k;
                    for (int a = 0; a < _npe; a++)
                    {
                        int o = 3 * el[a];
                        f[o] += w * g[3 * a];
                        f[o + 1] += w * g[3 * a + 1];
                        f[o + 2] += w * g[3 * a + 2];
                    }
                }
            }
        }
        public int DofCount => Mesh.Nodes.Count * 3;

        public void FixNodes(Func<Vec3, bool> where, bool x = true, bool y = true, bool z = true)
        {
            for (int n = 0; n < Mesh.Nodes.Count; n++)
            {
                if (!where(Mesh.Nodes[n])) continue;
                if (x) Fixed[3 * n] = true;
                if (y) Fixed[3 * n + 1] = true;
                if (z) Fixed[3 * n + 2] = true;
            }
        }

        public bool HasUserSupports
        {
            get
            {
                foreach (bool f in Fixed)
                    if (f) return true;
                return false;
            }
        }

        // ================================================================ shape functions

        /// <summary>Shape functions and their derivatives with respect to (ξ, η, ζ) = (L1, L2, L3).</summary>
        internal static void Shape(int order, double[] l, double[] n, double[] dn)
        {
            // dL/dξ for L0..L3
            if (order == 1)
            {
                for (int i = 0; i < 4; i++) n[i] = l[i];
                SetD(dn, 0, -1, -1, -1);
                SetD(dn, 1, 1, 0, 0);
                SetD(dn, 2, 0, 1, 0);
                SetD(dn, 3, 0, 0, 1);
                return;
            }

            for (int i = 0; i < 4; i++)
            {
                n[i] = l[i] * (2 * l[i] - 1);
                double f = 4 * l[i] - 1;
                SetD(dn, i, f * DL(i, 0), f * DL(i, 1), f * DL(i, 2));
            }

            for (int k = 0; k < 6; k++)
            {
                int a = TetMesh.Edges[k, 0], b = TetMesh.Edges[k, 1];
                n[4 + k] = 4 * l[a] * l[b];
                SetD(dn, 4 + k, 4 * (l[a] * DL(b, 0) + l[b] * DL(a, 0)), 4 * (l[a] * DL(b, 1) + l[b] * DL(a, 1)), 4 * (l[a] * DL(b, 2) + l[b] * DL(a, 2)));
            }
        }

        private static double DL(int i, int c) => i == 0 ? -1 : i - 1 == c ? 1 : 0;

        private static void SetD(double[] dn, int i, double a, double b, double c)
        {
            dn[3 * i] = a;
            dn[3 * i + 1] = b;
            dn[3 * i + 2] = c;
        }

        /// <summary>Gradients dN/dx (3 per node) at natural point l; returns det J.</summary>
        private double Gradients(int[] el, double[] l, double[] n, double[] dn, double[] g)
        {
            Shape(Mesh.Order, l, n, dn);
            double j00 = 0, j01 = 0, j02 = 0, j10 = 0, j11 = 0, j12 = 0, j20 = 0, j21 = 0, j22 = 0;
            for (int a = 0; a < _npe; a++)
            {
                var x = Mesh.Nodes[el[a]];
                double d0 = dn[3 * a], d1 = dn[3 * a + 1], d2 = dn[3 * a + 2];
                j00 += x.X * d0;
                j01 += x.X * d1;
                j02 += x.X * d2;
                j10 += x.Y * d0;
                j11 += x.Y * d1;
                j12 += x.Y * d2;
                j20 += x.Z * d0;
                j21 += x.Z * d1;
                j22 += x.Z * d2;
            }

            double det = j00 * (j11 * j22 - j12 * j21) - j01 * (j10 * j22 - j12 * j20) + j02 * (j10 * j21 - j11 * j20);
            double inv = 1 / det;
            // inverse of J
            double i00 = (j11 * j22 - j12 * j21) * inv, i01 = (j02 * j21 - j01 * j22) * inv, i02 = (j01 * j12 - j02 * j11) * inv;
            double i10 = (j12 * j20 - j10 * j22) * inv, i11 = (j00 * j22 - j02 * j20) * inv, i12 = (j02 * j10 - j00 * j12) * inv;
            double i20 = (j10 * j21 - j11 * j20) * inv, i21 = (j01 * j20 - j00 * j21) * inv, i22 = (j00 * j11 - j01 * j10) * inv;
            for (int a = 0; a < _npe; a++)
            {
                double d0 = dn[3 * a], d1 = dn[3 * a + 1], d2 = dn[3 * a + 2];
                // dN/dx_r = Σ_c invJ[c][r] dN/dξ_c
                g[3 * a] = i00 * d0 + i10 * d1 + i20 * d2;
                g[3 * a + 1] = i01 * d0 + i11 * d1 + i21 * d2;
                g[3 * a + 2] = i02 * d0 + i12 * d1 + i22 * d2;
            }

            return det;
        }

        // ================================================================ assembly

        public void Assemble(CancellationToken cancel = default)
        {
            var k = SparseMatrix.FromNodeGraph(Mesh.Nodes.Count, Mesh.Elements, 3);
            double lam = Material.Lame, mu = Material.ShearModulus;
            var n = new double[10];
            var dn = new double[30];
            var g = new double[30];
            var ke = new double[_npe * 3 * _npe * 3];
            var rules = Mesh.Order == 2 ? Gauss4 : new[] { new[] { 0.25, 0.25, 0.25, 0.25 } };
            var weights = Mesh.Order == 2 ? Gauss4W : new[] { 1.0 / 6 };
            int m3 = _npe * 3;
            for (int e = 0; e < Mesh.Elements.Count; e++)
            {
                if ((e & 1023) == 0) cancel.ThrowIfCancellationRequested();
                var el = Mesh.Elements[e];
                Array.Clear(ke, 0, ke.Length);
                for (int q = 0; q < rules.Length; q++)
                {
                    double det = Gradients(el, rules[q], n, dn, g);
                    double w = weights[q] * Math.Abs(det);
                    for (int a = 0; a < _npe; a++)
                    {
                        double ax = g[3 * a], ay = g[3 * a + 1], az = g[3 * a + 2];
                        for (int b = 0; b < _npe; b++)
                        {
                            double bx = g[3 * b], by = g[3 * b + 1], bz = g[3 * b + 2];
                            double dot = mu * (ax * bx + ay * by + az * bz);
                            int r0 = 3 * a * m3 + 3 * b;
                            // K_ab,ij = λ a_i b_j + μ a_j b_i + μ δij (a·b)
                            ke[r0] += w * (lam * ax * bx + mu * ax * bx + dot);
                            ke[r0 + 1] += w * (lam * ax * by + mu * ay * bx);
                            ke[r0 + 2] += w * (lam * ax * bz + mu * az * bx);
                            ke[r0 + m3] += w * (lam * ay * bx + mu * ax * by);
                            ke[r0 + m3 + 1] += w * (lam * ay * by + mu * ay * by + dot);
                            ke[r0 + m3 + 2] += w * (lam * ay * bz + mu * az * by);
                            ke[r0 + 2 * m3] += w * (lam * az * bx + mu * ax * bz);
                            ke[r0 + 2 * m3 + 1] += w * (lam * az * by + mu * ay * bz);
                            ke[r0 + 2 * m3 + 2] += w * (lam * az * bz + mu * az * bz + dot);
                        }
                    }
                }

                for (int a = 0; a < _npe; a++)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int row = 3 * el[a] + i;
                        for (int b = 0; b < _npe; b++)
                        {
                            int pos = k.Find(row, 3 * el[b]);
                            int src = (3 * a + i) * m3 + 3 * b;
                            k.Values[pos] += ke[src];
                            k.Values[pos + 1] += ke[src + 1];
                            k.Values[pos + 2] += ke[src + 2];
                        }
                    }
                }
            }

            Stiffness = k;
            ComputeMassProperties();
        }

        /// <summary>Mass, centre of mass, inertia and the nodal integrals ρ∫N dV and ρ∫N (x−c) dV used for body forces.</summary>
        private void ComputeMassProperties()
        {
            int nn = Mesh.Nodes.Count;
            var nm = new double[nn];
            var nx = new Vec3[nn];
            var n = new double[10];
            var dn = new double[30];
            var g = new double[30];
            double rho = Material.Density;
            double mass = 0;
            var first = Vec3.Zero;
            foreach (var el in Mesh.Elements)
            {
                for (int q = 0; q < Gauss5.Length; q++)
                {
                    double det = Gradients(el, Gauss5[q], n, dn, g);
                    double w = Gauss5W[q] * Math.Abs(det) * rho;
                    var x = Vec3.Zero;
                    for (int a = 0; a < _npe; a++) x += Mesh.Nodes[el[a]] * n[a];
                    for (int a = 0; a < _npe; a++)
                    {
                        nm[el[a]] += w * n[a];
                        nx[el[a]] += x * (w * n[a]);
                    }

                    mass += w;
                    first += x * w;
                }
            }

            var c = mass > 0 ? first / mass : Vec3.Zero;
            for (int i = 0; i < nn; i++) nx[i] -= c * nm[i];
            // Inertia about c from the straight-sided corner tetrahedra (exact second moments of a tetrahedron).
            double ixx = 0, iyy = 0, izz = 0, ixy = 0, iyz = 0, izx = 0;
            foreach (var el in Mesh.Elements)
            {
                var p = new[] { Mesh.Nodes[el[0]] - c, Mesh.Nodes[el[1]] - c, Mesh.Nodes[el[2]] - c, Mesh.Nodes[el[3]] - c };
                double v = Math.Abs(TetMesh.TetVolume(p[0], p[1], p[2], p[3]));
                // ∫ x_i x_j dV = V/20 (Σ_a x_ai x_aj + Σ_a x_ai Σ_b x_bj)
                double Sxx = 0, Syy = 0, Szz = 0, Sxy = 0, Syz = 0, Szx = 0;
                var s = p[0] + p[1] + p[2] + p[3];
                foreach (var a in p)
                {
                    Sxx += a.X * a.X;
                    Syy += a.Y * a.Y;
                    Szz += a.Z * a.Z;
                    Sxy += a.X * a.Y;
                    Syz += a.Y * a.Z;
                    Szx += a.Z * a.X;
                }

                double f = v / 20 * rho;
                double xx = f * (Sxx + s.X * s.X), yy = f * (Syy + s.Y * s.Y), zz = f * (Szz + s.Z * s.Z);
                double xy = f * (Sxy + s.X * s.Y), yz = f * (Syz + s.Y * s.Z), zx = f * (Szx + s.Z * s.X);
                ixx += yy + zz;
                iyy += xx + zz;
                izz += xx + yy;
                ixy -= xy;
                iyz -= yz;
                izx -= zx;
            }

            Mass = mass;
            CenterOfMass = c;
            Inertia = new[] { ixx, ixy, izx, ixy, iyy, iyz, izx, iyz, izz };
            _nodeMass = nm;
            _nodeMoment = nx;
        }

        // ================================================================ loads and solve

        private List<int> SurfaceList()
        {
            if (_surfaceList != null) return _surfaceList;
            var list = new List<int>(Mesh.SurfaceNodes);
            list.Sort();
            return _surfaceList = list;
        }

        /// <summary>Spreads a point force over the surface nodes within ~1.5 element sizes (linear weights).</summary>
        public void AddPointLoad(double[] f, Vec3 point, Vec3 force)
        {
            var surf = SurfaceList();
            double r = Math.Max(1e-9, 1.5 * Mesh.ElementSize);
            double wsum = 0;
            int nearest = -1;
            double best = double.PositiveInfinity;
            foreach (int n in surf)
            {
                double d = (Mesh.Nodes[n] - point).Length;
                if (d < best)
                {
                    best = d;
                    nearest = n;
                }

                if (d < r) wsum += 1 - d / r;
            }

            if (wsum <= 1e-12)
            {
                if (nearest < 0) return;
                f[3 * nearest] += force.X;
                f[3 * nearest + 1] += force.Y;
                f[3 * nearest + 2] += force.Z;
                return;
            }

            foreach (int n in surf)
            {
                double d = (Mesh.Nodes[n] - point).Length;
                if (d >= r) continue;
                double w = (1 - d / r) / wsum;
                f[3 * n] += force.X * w;
                f[3 * n + 1] += force.Y * w;
                f[3 * n + 2] += force.Z * w;
            }
        }

        /// <summary>
        /// Uniform traction (Pa, vector) on the boundary faces whose centroid satisfies <paramref name="where"/>,
        /// as consistent nodal loads (quadratic faces: mid nodes carry 1/3 of the face force each, corners 0).
        /// Returns the total applied force.
        /// </summary>
        public Vec3 AddTraction(double[] f, Func<Vec3, bool> where, Vec3 traction)
        {
            var total = Vec3.Zero;
            foreach (var s in Mesh.Surface)
            {
                var a = Mesh.Nodes[s[0]];
                var b = Mesh.Nodes[s[1]];
                var c = Mesh.Nodes[s[2]];
                if (!where((a + b + c) / 3)) continue;
                double area = 0.5 * (b - a).Cross(c - a).Length;
                var force = traction * area;
                total += force;
                if (Mesh.Order == 2)
                {
                    for (int k = 3; k < 6; k++)
                    {
                        f[3 * s[k]] += force.X / 3;
                        f[3 * s[k] + 1] += force.Y / 3;
                        f[3 * s[k] + 2] += force.Z / 3;
                    }
                }
                else
                {
                    for (int k = 0; k < 3; k++)
                    {
                        f[3 * s[k]] += force.X / 3;
                        f[3 * s[k] + 1] += force.Y / 3;
                        f[3 * s[k] + 2] += force.Z / 3;
                    }
                }
            }

            return total;
        }

        /// <summary>Uniform body acceleration field a (gravity or d'Alembert): f = ρ∫N a dV.</summary>
        public void AddBodyAcceleration(double[] f, Vec3 a)
        {
            if (_nodeMass == null) throw new InvalidOperationException("Assemble first");
            for (int n = 0; n < _nodeMass.Length; n++)
            {
                f[3 * n] += _nodeMass[n] * a.X;
                f[3 * n + 1] += _nodeMass[n] * a.Y;
                f[3 * n + 2] += _nodeMass[n] * a.Z;
            }
        }

        private Vec3 InertiaTimes(Vec3 v) => new Vec3(
            Inertia[0] * v.X + Inertia[1] * v.Y + Inertia[2] * v.Z,
            Inertia[3] * v.X + Inertia[4] * v.Y + Inertia[5] * v.Z,
            Inertia[6] * v.X + Inertia[7] * v.Y + Inertia[8] * v.Z);

        /// <summary>Builds the load vector of a case; with inertia relief also returns the balancing acceleration.</summary>
        public double[] LoadVector(FemLoadCase lc, out Vec3 reliefAccel, out double totalLoad)
        {
            if (_nodeMass == null || _nodeMoment == null) Assemble();
            var f = new double[DofCount];
            var sumF = Vec3.Zero;
            var sumM = Vec3.Zero;
            totalLoad = 0;
            foreach (var l in lc.Loads)
            {
                AddPointLoad(f, l.Point, l.Force);
                sumF += l.Force;
                sumM += (l.Point - CenterOfMass).Cross(l.Force);
                totalLoad += l.Force.Length;
            }

            AddBodyAcceleration(f, lc.Gravity);
            AddThermalLoad(f);   // self-balanced: it never changes the inertia-relief acceleration
            totalLoad += Mass * lc.Gravity.Length;
            reliefAccel = Vec3.Zero;
            if (!lc.InertiaRelief) return f;

            // Rigid acceleration: m·a0 = ΣF + m·g ; I·α + ω×(Iω) = ΣM (about the centre of mass).
            var a0 = (sumF + lc.Gravity * Mass) / Math.Max(1e-12, Mass);
            var w = lc.Omega;
            var rhs = sumM - w.Cross(InertiaTimes(w));
            var inv = (double[])Inertia.Clone();
            var alpha = Vec3.Zero;
            if (ConjugateGradient.Invert(inv, 3))
                alpha = new Vec3(inv[0] * rhs.X + inv[1] * rhs.Y + inv[2] * rhs.Z, inv[3] * rhs.X + inv[4] * rhs.Y + inv[5] * rhs.Z, inv[6] * rhs.X + inv[7] * rhs.Y + inv[8] * rhs.Z);
            reliefAccel = a0;
            // d'Alembert body force −ρ(a0 + α×r + ω×(ω×r)), r = x − c.
            for (int n = 0; n < _nodeMass.Length; n++)
            {
                var s = _nodeMoment[n];
                var fn = a0 * _nodeMass[n] + alpha.Cross(s) + w.Cross(w.Cross(s));
                f[3 * n] -= fn.X;
                f[3 * n + 1] -= fn.Y;
                f[3 * n + 2] -= fn.Z;
            }

            return f;
        }

        /// <summary>Statically determinate 3-2-1 support (used with inertia relief when the user fixed nothing).</summary>
        public bool[] ThreeTwoOne()
        {
            var fixedDof = new bool[DofCount];
            int corners = Mesh.Nodes.Count;
            int a = -1;
            double best = double.PositiveInfinity;
            var used = new HashSet<int>();
            foreach (var el in Mesh.Elements)
                for (int i = 0; i < 4; i++)
                    used.Add(el[i]);
            var list = new List<int>(used);
            list.Sort();
            foreach (int n in list)
            {
                double d = (Mesh.Nodes[n] - CenterOfMass).Length;
                if (d < best)
                {
                    best = d;
                    a = n;
                }
            }

            int b = a;
            best = -1;
            foreach (int n in list)
            {
                double d = (Mesh.Nodes[n] - Mesh.Nodes[a]).Length;
                if (d > best)
                {
                    best = d;
                    b = n;
                }
            }

            var ab = (Mesh.Nodes[b] - Mesh.Nodes[a]).Normalized();
            int c = a;
            best = -1;
            foreach (int n in list)
            {
                var v = Mesh.Nodes[n] - Mesh.Nodes[a];
                double d = (v - ab * v.Dot(ab)).Length;
                if (d > best)
                {
                    best = d;
                    c = n;
                }
            }

            fixedDof[3 * a] = fixedDof[3 * a + 1] = fixedDof[3 * a + 2] = true;
            // B: the two directions most perpendicular to AB.
            var comps = new[] { Math.Abs(ab.X), Math.Abs(ab.Y), Math.Abs(ab.Z) };
            int major = comps[0] >= comps[1] && comps[0] >= comps[2] ? 0 : comps[1] >= comps[2] ? 1 : 2;
            for (int i = 0; i < 3; i++)
                if (i != major)
                    fixedDof[3 * b + i] = true;
            // C: the direction closest to the normal of plane ABC.
            var nrm = ab.Cross(Mesh.Nodes[c] - Mesh.Nodes[a]).Normalized();
            var nc = new[] { Math.Abs(nrm.X), Math.Abs(nrm.Y), Math.Abs(nrm.Z) };
            int nmaj = nc[0] >= nc[1] && nc[0] >= nc[2] ? 0 : nc[1] >= nc[2] ? 1 : 2;
            if (c != a && c != b) fixedDof[3 * c + nmaj] = true;
            return fixedDof;
        }

        public FemSolution Solve(FemLoadCase lc, CancellationToken cancel = default)
        {
            if (Stiffness == null) Assemble(cancel);
            var f = LoadVector(lc, out var relief, out double total);
            bool[] fixedDof;
            if (lc.InertiaRelief && !HasUserSupports) fixedDof = ThreeTwoOne();
            else fixedDof = Fixed;
            var sol = Solve(f, fixedDof, cancel);
            sol.Time = lc.Time;
            sol.ReliefAcceleration = relief;
            sol.TotalLoad = total;
            sol.ReactionRatio = total > 0 ? Reactions(sol.Displacement, f, fixedDof) / total : 0;
            return sol;
        }

        /// <summary>Solves K·u = f with the given fixed components and recovers stresses.</summary>
        public FemSolution Solve(double[] f, bool[] fixedDof, CancellationToken cancel = default)
        {
            if (Stiffness == null) Assemble(cancel);
            var u = _lastU != null && _lastU.Length == DofCount ? (double[])_lastU.Clone() : new double[DofCount];
            var rep = ConjugateGradient.Solve(Stiffness!, f, u, fixedDof, Tolerance, 0, cancel);
            _lastU = u;
            var sol = new FemSolution { Displacement = u, Solver = rep };
            RecoverStress(sol);
            return sol;
        }

        private double Reactions(double[] u, double[] f, bool[] fixedDof)
        {
            var ku = new double[u.Length];
            Stiffness!.Multiply(u, ku);
            var r = Vec3.Zero;
            for (int n = 0; n < u.Length / 3; n++)
            {
                double rx = fixedDof[3 * n] ? ku[3 * n] - f[3 * n] : 0;
                double ry = fixedDof[3 * n + 1] ? ku[3 * n + 1] - f[3 * n + 1] : 0;
                double rz = fixedDof[3 * n + 2] ? ku[3 * n + 2] - f[3 * n + 2] : 0;
                r += new Vec3(rx, ry, rz);
            }

            return r.Length;
        }

        private void RecoverStress(FemSolution sol)
        {
            int nn = Mesh.Nodes.Count;
            var s = new double[nn * 6];
            var count = new int[nn];
            var n = new double[10];
            var dn = new double[30];
            var g = new double[30];
            double lam = Material.Lame, mu = Material.ShearModulus;
            var u = sol.Displacement;
            var l = new double[4];
            foreach (var el in Mesh.Elements)
            {
                for (int a = 0; a < _npe; a++)
                {
                    Array.Clear(l, 0, 4);
                    if (a < 4) l[a] = 1;
                    else
                    {
                        l[TetMesh.Edges[a - 4, 0]] = 0.5;
                        l[TetMesh.Edges[a - 4, 1]] = 0.5;
                    }

                    Gradients(el, l, n, dn, g);
                    double exx = 0, eyy = 0, ezz = 0, gxy = 0, gyz = 0, gzx = 0;
                    for (int b = 0; b < _npe; b++)
                    {
                        int nb = el[b];
                        double ux = u[3 * nb], uy = u[3 * nb + 1], uz = u[3 * nb + 2];
                        double gx = g[3 * b], gy = g[3 * b + 1], gz = g[3 * b + 2];
                        exx += ux * gx;
                        eyy += uy * gy;
                        ezz += uz * gz;
                        gxy += ux * gy + uy * gx;
                        gyz += uy * gz + uz * gy;
                        gzx += uz * gx + ux * gz;
                    }

                    double tr = lam * (exx + eyy + ezz) - (3 * lam + 2 * mu) * ThermalStrain;
                    int o = el[a] * 6;
                    s[o] += tr + 2 * mu * exx;
                    s[o + 1] += tr + 2 * mu * eyy;
                    s[o + 2] += tr + 2 * mu * ezz;
                    s[o + 3] += mu * gxy;
                    s[o + 4] += mu * gyz;
                    s[o + 5] += mu * gzx;
                    count[el[a]]++;
                }
            }

            var vm = new double[nn];
            double max = 0, maxU = 0;
            int maxNode = 0;
            for (int i = 0; i < nn; i++)
            {
                if (count[i] > 0)
                    for (int k = 0; k < 6; k++)
                        s[i * 6 + k] /= count[i];
                vm[i] = VonMises(s, i);
                if (vm[i] > max)
                {
                    max = vm[i];
                    maxNode = i;
                }

                maxU = Math.Max(maxU, sol.DisplacementMagnitude(i));
            }

            sol.Stress = s;
            sol.VonMises = vm;
            sol.MaxVonMises = max;
            sol.MaxVonMisesNode = maxNode;
            sol.MaxDisplacement = maxU;
            sol.MinSafetyFactor = max > 0 ? Material.Yield / max : double.PositiveInfinity;
        }

        public static double VonMises(double[] s, int node)
        {
            int o = node * 6;
            double sx = s[o], sy = s[o + 1], sz = s[o + 2], txy = s[o + 3], tyz = s[o + 4], tzx = s[o + 5];
            return Math.Sqrt(0.5 * ((sx - sy) * (sx - sy) + (sy - sz) * (sy - sz) + (sz - sx) * (sz - sx)) + 3 * (txy * txy + tyz * tyz + tzx * tzx));
        }

        /// <summary>Displacement interpolated at a point (nearest node).</summary>
        public int NearestNode(Vec3 p)
        {
            int best = -1;
            double bd = double.PositiveInfinity;
            for (int i = 0; i < Mesh.Nodes.Count; i++)
            {
                double d = (Mesh.Nodes[i] - p).LengthSquared;
                if (d < bd)
                {
                    bd = d;
                    best = i;
                }
            }

            return best;
        }
    }
}
