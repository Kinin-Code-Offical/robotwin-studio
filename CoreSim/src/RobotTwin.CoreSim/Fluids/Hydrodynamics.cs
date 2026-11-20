using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Fluids
{
    /// <summary>Submerged part of a body under a plane: volume (m³) and centre of buoyancy (world).</summary>
    public readonly struct Submersion
    {
        public Submersion(double volume, Vec3 centroid, double fraction)
        {
            Volume = volume;
            Centroid = centroid;
            Fraction = fraction;
        }

        public double Volume { get; }
        public Vec3 Centroid { get; }

        /// <summary>Submerged volume / total volume.</summary>
        public double Fraction { get; }
    }

    /// <summary>
    /// [fluid] Closed-surface description of a collision shape for exact submerged-volume computation:
    /// spheres analytically (spherical cap), everything else as a closed triangle mesh (boxes exactly,
    /// cylinders as a 32-gon prism with the radius corrected to the true cross-section area, convex
    /// hulls and meshes from their triangles). Cached per shape.
    /// </summary>
    public sealed class HydroShape
    {
        private static readonly ConditionalWeakTable<CollisionShape, HydroShape> Cache = new ConditionalWeakTable<CollisionShape, HydroShape>();

        internal Vec3[] V = Array.Empty<Vec3>();
        internal int[] T = Array.Empty<int>();
        internal Vec3[] World = Array.Empty<Vec3>();

        public bool IsSphere { get; private set; }
        public double Radius { get; private set; }
        public double Volume { get; private set; }

        /// <summary>Drag area C_D·A (m²) for flow along the body x, y, z axes (fully immersed, Newton regime).</summary>
        public Vec3 DragArea { get; private set; }

        /// <summary>Equivalent-sphere diameter (m), the length of the Reynolds number.</summary>
        public double Length { get; private set; }

        public int TriangleCount => T.Length / 3;

        public static HydroShape For(CollisionShape shape)
        {
            lock (Cache)
            {
                if (Cache.TryGetValue(shape, out var h)) return h;
                h = Build(shape);
                Cache.Add(shape, h);
                return h;
            }
        }

        private static HydroShape Build(CollisionShape s)
        {
            var h = new HydroShape();
            switch (s.Kind)
            {
                case ShapeKind.Sphere:
                    h.IsSphere = true;
                    h.Radius = s.Radius;
                    h.Volume = 4.0 / 3 * Math.PI * s.Radius * s.Radius * s.Radius;
                    double a = Math.PI * s.Radius * s.Radius * 0.47;
                    h.DragArea = new Vec3(a, a, a);
                    break;
                case ShapeKind.Box:
                {
                    var e = s.HalfExtents;
                    h.SetMesh(BoxMesh(e));
                    // Flat-faced block: C_D ≈ 1.05 (cube face-on, Hoerner).
                    h.DragArea = new Vec3(4 * e.Y * e.Z, 4 * e.X * e.Z, 4 * e.X * e.Y) * 1.05;
                    break;
                }

                case ShapeKind.Cylinder:
                {
                    double r = s.Radius, hh = s.HalfHeight;
                    h.SetMesh(CylinderMesh(r, hh, 32));
                    // Side-on C_D ≈ 1.0 (subcritical), end-on C_D ≈ 0.82 for L/D ≈ 1–2 (Hoerner, Fluid-Dynamic Drag, ch. 3).
                    double side = 2 * r * 2 * hh * 1.0;
                    h.DragArea = new Vec3(side, side, Math.PI * r * r * 0.82);
                    break;
                }

                default:
                {
                    MeshModel? mesh = s.Mesh;
                    if (mesh == null && s.Vertices.Count >= 4)
                    {
                        var hull = ConvexHull.Compute(s.Vertices);
                        if (!hull.IsDegenerate) mesh = hull.ToMesh();
                    }

                    if (mesh != null) h.SetMesh(mesh);
                    var b = h.Bounds();
                    var sz = b.Max - b.Min;
                    h.DragArea = new Vec3(sz.Y * sz.Z, sz.X * sz.Z, sz.X * sz.Y) * 1.0;
                    break;
                }
            }

            if (!h.IsSphere)
            {
                if (h.ComputeVolume() < 0)
                {
                    // Inward-wound mesh: flip so that submerged tetrahedra count positive.
                    for (int i = 0; i < h.T.Length; i += 3) (h.T[i + 1], h.T[i + 2]) = (h.T[i + 2], h.T[i + 1]);
                }

                h.Volume = Math.Abs(h.ComputeVolume());
                if (h.Volume <= 0) h.Volume = s.Volume;
            }

            h.Length = 2 * Math.Pow(3 * Math.Max(1e-12, h.Volume) / (4 * Math.PI), 1.0 / 3);
            return h;
        }

        private void SetMesh(MeshModel m)
        {
            V = m.Vertices.ToArray();
            T = m.Indices.ToArray();
            World = new Vec3[V.Length];
        }

        private (Vec3 Min, Vec3 Max) Bounds()
        {
            if (V.Length == 0) return (Vec3.Zero, Vec3.Zero);
            var lo = V[0];
            var hi = V[0];
            foreach (var v in V)
            {
                lo = Vec3.Min(lo, v);
                hi = Vec3.Max(hi, v);
            }

            return (lo, hi);
        }

        private double ComputeVolume()
        {
            double vol = 0;
            for (int i = 0; i < T.Length; i += 3) vol += V[T[i]].Dot(V[T[i + 1]].Cross(V[T[i + 2]])) / 6;
            return vol;
        }

        private static MeshModel BoxMesh(Vec3 e)
        {
            var m = new MeshModel();
            for (int i = 0; i < 8; i++) m.AddVertex(new Vec3((i & 1) != 0 ? e.X : -e.X, (i & 2) != 0 ? e.Y : -e.Y, (i & 4) != 0 ? e.Z : -e.Z));
            int[] q =
            {
                0, 2, 3, 1,   // -z
                4, 5, 7, 6,   // +z
                0, 1, 5, 4,   // -y
                2, 6, 7, 3,   // +y
                0, 4, 6, 2,   // -x
                1, 3, 7, 5    // +x
            };
            for (int f = 0; f < 6; f++)
            {
                m.AddTriangle(q[4 * f], q[4 * f + 1], q[4 * f + 2]);
                m.AddTriangle(q[4 * f], q[4 * f + 2], q[4 * f + 3]);
            }

            return m;
        }

        private static MeshModel CylinderMesh(double r, double hh, int n)
        {
            // Radius scaled so the n-gon has the circle's area: r' = r·√(2π/(n·sin(2π/n))).
            double rr = r * Math.Sqrt(2 * Math.PI / (n * Math.Sin(2 * Math.PI / n)));
            var m = new MeshModel();
            int top = m.AddVertex(new Vec3(0, 0, hh)), bot = m.AddVertex(new Vec3(0, 0, -hh));
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                m.AddVertex(new Vec3(rr * Math.Cos(a), rr * Math.Sin(a), hh));
                m.AddVertex(new Vec3(rr * Math.Cos(a), rr * Math.Sin(a), -hh));
            }

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                int t0 = 2 + 2 * i, b0 = 3 + 2 * i, t1 = 2 + 2 * j, b1 = 3 + 2 * j;
                m.AddTriangle(top, t0, t1);
                m.AddTriangle(bot, b1, b0);
                m.AddTriangle(t0, b0, b1);
                m.AddTriangle(t0, b1, t1);
            }

            return m;
        }

        /// <summary>
        /// Submerged volume and centroid below the plane through <paramref name="planePoint"/> with upward
        /// normal <paramref name="n"/>. Mesh shapes: every triangle is clipped to the submerged half-space and
        /// the signed tetrahedra it forms with a point on the plane are summed (the water-plane lid adds
        /// none) — exact for any closed polyhedron. Spheres: spherical cap.
        /// </summary>
        public Submersion Submerge(Vec3 position, Quat orientation, Vec3 planePoint, Vec3 n)
        {
            if (IsSphere)
            {
                double zc = (position - planePoint).Dot(n);
                double r = Radius;
                double hcap = Math.Max(0, Math.Min(2 * r, r - zc));
                if (hcap <= 0) return new Submersion(0, position, 0);
                double vol = Math.PI * hcap * hcap * (3 * r - hcap) / 3;
                double off = hcap >= 2 * r ? 0 : 3 * (2 * r - hcap) * (2 * r - hcap) / (4 * (3 * r - hcap));
                return new Submersion(vol, position - n * off, vol / Volume);
            }

            if (T.Length == 0) return new Submersion(0, position, 0);
            for (int i = 0; i < V.Length; i++) World[i] = position + orientation.Rotate(V[i]);
            var o = planePoint;
            double total = 0;
            var moment = Vec3.Zero;
            Span<Vec3> poly = stackalloc Vec3[4];
            for (int t = 0; t < T.Length; t += 3)
            {
                var a = World[T[t]];
                var b = World[T[t + 1]];
                var c = World[T[t + 2]];
                double da = (a - o).Dot(n), db = (b - o).Dot(n), dc = (c - o).Dot(n);
                if (da >= 0 && db >= 0 && dc >= 0) continue;
                int k;
                if (da <= 0 && db <= 0 && dc <= 0)
                {
                    poly[0] = a;
                    poly[1] = b;
                    poly[2] = c;
                    k = 3;
                }
                else
                {
                    k = 0;
                    Clip(a, da, b, db, poly, ref k);
                    Clip(b, db, c, dc, poly, ref k);
                    Clip(c, dc, a, da, poly, ref k);
                }

                for (int j = 1; j + 1 < k; j++)
                {
                    var p0 = poly[0] - o;
                    var p1 = poly[j] - o;
                    var p2 = poly[j + 1] - o;
                    double v = p0.Dot(p1.Cross(p2)) / 6;
                    total += v;
                    moment += (p0 + p1 + p2) * (v / 4);
                }
            }

            if (total <= 1e-15) return new Submersion(0, position, 0);
            return new Submersion(total, o + moment / total, Math.Min(1, total / Volume));
        }

        private static void Clip(Vec3 p, double dp, Vec3 q, double dq, Span<Vec3> poly, ref int k)
        {
            // Sutherland–Hodgman edge step for the half-space d ≤ 0.
            if (dp <= 0 && k < 4) poly[k++] = p;
            if ((dp < 0 && dq > 0) || (dp > 0 && dq < 0))
            {
                double t = dp / (dp - dq);
                if (k < 4) poly[k++] = p + (q - p) * t;
            }
        }

        /// <summary>Submerged triangles' areas, centroids and outward normals in world space (after <see cref="Submerge"/>), for face-wise damping.</summary>
        internal void ForEachWetFace(Vec3 planePoint, Vec3 n, Action<Vec3, Vec3, double> face)
        {
            for (int t = 0; t < T.Length; t += 3)
            {
                var a = World[T[t]];
                var b = World[T[t + 1]];
                var c = World[T[t + 2]];
                var cen = (a + b + c) / 3;
                if ((cen - planePoint).Dot(n) > 0) continue;
                var cr = (b - a).Cross(c - a);
                double area = cr.Length / 2;
                if (area <= 0) continue;
                face(cen, cr / (2 * area), area);
            }
        }
    }

    /// <summary>What the water did to a body in the last step.</summary>
    public struct HydroReadout
    {
        public double SubmergedVolume, Fraction, Depth, Reynolds;
        public Vec3 Buoyancy, Drag, CenterOfBuoyancy;
        public double AddedMass;
    }

    /// <summary>
    /// [fluid] Forces of a liquid on a rigid body, applied once per physics step:
    /// <list type="bullet">
    /// <item>Buoyancy F_b = −ρ·g·V_sub at the centre of buoyancy (Archimedes) from the exactly clipped
    /// submerged volume; waves tilt the clipping plane to the local surface slope.</item>
    /// <item>Drag F = −½ρ·C_D(Re)·A·|u|·u relative to the moving water (current + wave orbital velocity),
    /// per-axis C_D·A of the shape, scaled by the submerged fraction; C_D(Re) follows Schiller–Naumann
    /// 24/Re·(1 + 0.15Re^0.687) in the viscous range (glycerin, honey, small bodies), never below the shape's
    /// Newton-regime value. Limited so it cannot reverse the velocity within a step.</item>
    /// <item>Rotational damping: each wetted face pushing into the liquid by its rotation gets ½ρ·(ω×r·n)²·A.</item>
    /// <item>Added mass m_a = C_a·ρ·V_sub (C_a = 0.5, the sphere value, used for all shapes): implemented by
    /// scaling the hydro and gravity forces by m/(m + m_a), i.e. (m + m_a)·a = F for free motion in the liquid.</item>
    /// </list>
    /// </summary>
    public static class HydroForces
    {
        public const double AddedMassCoefficient = 0.5;

        public static double SchillerNaumann(double re) => re < 1e-6 ? 1e6 : re < 1000 ? 24 / re * (1 + 0.15 * Math.Pow(re, 0.687)) : 0.44;

        public static HydroReadout Apply(RigidBody b, HydroShape shape, WaterBody w, double time, double dt, Vec3 gravity)
        {
            var r = new HydroReadout();
            if (b.IsStatic) return r;
            var pos = b.Position;
            if (!w.ContainsXY(pos.X, pos.Y) || pos.Z < w.Min.Z - shape.Length) return r;
            double eta = w.SurfaceAt(pos.X, pos.Y, time);
            var n = w.NormalAt(pos.X, pos.Y, time);
            var sub = shape.Submerge(pos, b.Orientation, new Vec3(pos.X, pos.Y, eta), n);
            r.Depth = eta - pos.Z;
            if (sub.Volume <= 0) return r;
            double rho = w.Liquid.Density;
            r.SubmergedVolume = sub.Volume;
            r.Fraction = sub.Fraction;
            r.CenterOfBuoyancy = sub.Centroid;
            var fb = gravity * (-rho * sub.Volume);
            b.AddForceAtPoint(fb, sub.Centroid);
            r.Buoyancy = fb;

            double ma = AddedMassCoefficient * rho * sub.Volume;
            r.AddedMass = ma;
            double meff = b.Mass + ma;
            var rel = b.Velocity - w.VelocityAt(sub.Centroid, time);
            double sp = rel.Length;
            var fd = Vec3.Zero;
            if (sp > 1e-9)
            {
                var lb = b.Orientation.InverseRotate(rel) / sp;
                double cda = Math.Abs(lb.X) * shape.DragArea.X + Math.Abs(lb.Y) * shape.DragArea.Y + Math.Abs(lb.Z) * shape.DragArea.Z;
                double re = rho * sp * shape.Length / Math.Max(1e-9, w.Liquid.Viscosity);
                r.Reynolds = re;
                double fre = Math.Max(1.0, SchillerNaumann(re) / 0.47);
                double k = 0.5 * rho * cda * fre * sub.Fraction * sp;
                k = Math.Min(k, meff / Math.Max(1e-6, dt));
                fd = rel * -k;
                b.AddForce(fd);
            }

            r.Drag = fd;
            // Face-wise rotational damping (meshes); spheres: Stokes-like rotational drag 8πμr³ω.
            var wv = b.AngularVelocity;
            if (wv.LengthSquared > 1e-10)
            {
                if (shape.IsSphere)
                {
                    b.AddTorque(wv * (-8 * Math.PI * w.Liquid.Viscosity * Math.Pow(shape.Radius, 3) * sub.Fraction));
                }
                else
                {
                    var torque = Vec3.Zero;
                    var com = pos;
                    shape.ForEachWetFace(new Vec3(pos.X, pos.Y, eta), n, (c, fn, area) =>
                    {
                        double vn = wv.Cross(c - com).Dot(fn);
                        if (vn <= 0) return;
                        var f = fn * (-0.5 * rho * area * vn * vn);
                        torque += (c - com).Cross(f);
                    });
                    // Limit to what would stop the spin within a step about the weakest axis.
                    double imin = Math.Min(b.Inertia.X, Math.Min(b.Inertia.Y, b.Inertia.Z));
                    double tmax = imin * wv.Length / Math.Max(1e-6, dt);
                    if (torque.Length > tmax) torque = torque * (tmax / torque.Length);
                    b.AddTorque(torque);
                }
            }

            // Added mass: (m + m_a)·a = m·g + F_hydro  ⇒  extra = −m_a/(m + m_a)·(m·g + F_hydro).
            var free = gravity * b.Mass + fb + fd;
            b.AddForce(free * (-ma / meff));
            return r;
        }
    }
}
