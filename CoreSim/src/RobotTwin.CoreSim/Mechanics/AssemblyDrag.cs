using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [drag] Air drag of parts joined into one assembly. A body's drag is split into what really
    /// produces it: the stagnation pressure on the face that leads into the flow, the low (base)
    /// pressure behind the face that trails, and skin friction on the sides the flow runs along.
    /// A lone body keeps its measured Cd (skin friction is part of it).
    /// A face that a joined neighbour covers sees neither: a nose cone on a tube leaves the tube no
    /// front face, the tube leaves the cone no base, so a rocket's drag is the nose's front, the tail's
    /// base and the friction along it — not the sum of each part's drag as if it flew alone.
    /// <para>Coverage is found by sampling lines along each body axis through the actual collision
    /// shapes (box, sphere, cylinder, convex hull): where a neighbour's solid continues past this body's
    /// face, or starts within <see cref="Gap"/> of it, that part of the face is covered.</para>
    /// </summary>
    public static class AssemblyDrag
    {
        /// <summary>Share of an isolated body's pressure drag that acts on its leading face (the rest is base drag).</summary>
        public const double LeadingShare = 0.7;

        /// <summary>Skin friction coefficient: turbulent flat plate at Re ≈ 10⁵–10⁶ (0.074·Re^−0.2 ≈ 0.004–0.006).</summary>
        public const double FrictionCoefficient = 0.004;

        /// <summary>A neighbour starting this close past a face still covers it (m).</summary>
        public const double Gap = 0.01;

        private const int Samples = 12;

        /// <summary>
        /// Sets the directional drag of every body in <paramref name="bodies"/> (whose <see cref="RigidBody.DragArea"/>
        /// already holds its isolated-body Cd·A) from its own shape and the bodies it is joined to.
        /// </summary>
        public static void Apply(PhysicsScene scene, IEnumerable<SceneBody> bodies)
        {
            var neighbours = new Dictionary<SceneBody, List<SceneBody>>();
            foreach (var (a, b) in scene.NoCollide)
            {
                if (!neighbours.TryGetValue(a, out var la)) neighbours[a] = la = new List<SceneBody>();
                if (!neighbours.TryGetValue(b, out var lb)) neighbours[b] = lb = new List<SceneBody>();
                la.Add(b);
                lb.Add(a);
            }

            foreach (var body in bodies)
            {
                var shape = body.EffectiveShape;
                if (shape == null || !body.HasDrag || !Supported(shape)) continue;
                neighbours.TryGetValue(body, out var near);
                var (area, coverPlus, coverMinus) = Faces(body, near);
                var da = body.DragArea;
                // Friction along an axis acts on the faces of the other two axes (both sides each). A lone body's
                // measured Cd already contains it, so it is split off: the rest is pressure drag.
                Vec3 Friction(Vec3 exposed) => new Vec3(exposed.Y + exposed.Z, exposed.X + exposed.Z, exposed.X + exposed.Y) * FrictionCoefficient;
                var fullFriction = Friction(area * 2);
                var friction = Friction(new Vec3(
                    area.X * (2 - coverPlus.X - coverMinus.X),
                    area.Y * (2 - coverPlus.Y - coverMinus.Y),
                    area.Z * (2 - coverPlus.Z - coverMinus.Z)));
                var pressure = new Vec3(Math.Max(0.5 * da.X, da.X - fullFriction.X), Math.Max(0.5 * da.Y, da.Y - fullFriction.Y), Math.Max(0.5 * da.Z, da.Z - fullFriction.Z));
                friction = new Vec3(Math.Min(friction.X, da.X - pressure.X), Math.Min(friction.Y, da.Y - pressure.Y), Math.Min(friction.Z, da.Z - pressure.Z));
                // Pressure drag per axis: leading face + trailing (base) face, each only where uncovered.
                Vec3 Pressure(Vec3 lead, Vec3 trail) => new Vec3(
                    pressure.X * (LeadingShare * (1 - lead.X) + (1 - LeadingShare) * (1 - trail.X)),
                    pressure.Y * (LeadingShare * (1 - lead.Y) + (1 - LeadingShare) * (1 - trail.Y)),
                    pressure.Z * (LeadingShare * (1 - lead.Z) + (1 - LeadingShare) * (1 - trail.Z)));
                if (shape.Kind == ShapeKind.ConvexHull && HullFaces(shape) is { } hf)
                {
                    // A model's own surface: how steeply each face meets the flow, not a box around it.
                    static Vec3 Mul(Vec3 a, Vec3 b) => new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
                    var one = new Vec3(1, 1, 1);
                    var fwd = Mul(hf.LeadPlus, one - coverPlus) + Mul(hf.BaseMinus, one - coverMinus);
                    var back = Mul(hf.LeadMinus, one - coverMinus) + Mul(hf.BasePlus, one - coverPlus);
                    var hullFriction = Friction(new Vec3(
                        area.X * (2 - coverPlus.X - coverMinus.X),
                        area.Y * (2 - coverPlus.Y - coverMinus.Y),
                        area.Z * (2 - coverPlus.Z - coverMinus.Z)));
                    body.SetDirectionalDrag(fwd + hullFriction, back + hullFriction);
                }
                else body.SetDirectionalDrag(Pressure(coverPlus, coverMinus) + friction, Pressure(coverMinus, coverPlus) + friction);
                body.DragRound = RoundAxis(body, shape, area);
            }
        }

        /// <summary>Stagnation-side pressure coefficient of a face square to the flow (disc front, Hoerner ch. 3).</summary>
        public const double FrontPressure = 0.8;

        /// <summary>Base pressure coefficient behind a blunt trailing face with an attached boundary layer ahead of it.</summary>
        public const double BasePressure = 0.15;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CollisionShape, HullDrag> HullCache =
            new System.Runtime.CompilerServices.ConditionalWeakTable<CollisionShape, HullDrag>();

        /// <summary>Cd·A (m², body axes) of a hull's faces that point along +axis / −axis, as leading faces and as base.</summary>
        public sealed class HullDrag
        {
            public Vec3 LeadPlus, LeadMinus, BasePlus, BaseMinus;
        }

        /// <summary>
        /// Pressure drag of a convex model from its faces. A face meeting the flow at angle θ (θ = 90° square on)
        /// carries C_p ≈ 0.8·sin²θ on its projected area — Hoerner's subsonic forebody estimate, which gives a flat
        /// face 0.8, a 20° cone ≈ 0.09 and a slender ogive almost nothing; a trailing face carries base pressure
        /// ≈ 0.15·sin²θ, so a boat-tail recovers it and a blunt base does not.
        /// </summary>
        public static HullDrag? HullFaces(CollisionShape shape)
        {
            lock (HullCache)
            {
                if (HullCache.TryGetValue(shape, out var cached)) return cached;
            }

            if (shape.Vertices.Count < 4) return null;
            var hull = ConvexHull.Compute(shape.Vertices);
            if (hull.IsDegenerate) return null;
            var mesh = hull.ToMesh();
            var d = new HullDrag();
            double[] lp = new double[3], lm = new double[3], bp = new double[3], bm = new double[3];
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var (a, b, c) = mesh.Triangle(t);
                var n = (b - a).Cross(c - a);
                double twice = n.Length;
                if (twice < 1e-15) continue;
                n /= twice;
                double area = twice / 2;
                double[] comp = { n.X, n.Y, n.Z };
                for (int k = 0; k < 3; k++)
                {
                    double cos = comp[k], weight = area * Math.Abs(cos * cos * cos);   // projected area · sin²θ
                    if (cos > 0)
                    {
                        lp[k] += FrontPressure * weight;
                        bp[k] += BasePressure * weight;
                    }
                    else if (cos < 0)
                    {
                        lm[k] += FrontPressure * weight;
                        bm[k] += BasePressure * weight;
                    }
                }
            }

            d.LeadPlus = new Vec3(lp[0], lp[1], lp[2]);
            d.LeadMinus = new Vec3(lm[0], lm[1], lm[2]);
            d.BasePlus = new Vec3(bp[0], bp[1], bp[2]);
            d.BaseMinus = new Vec3(bm[0], bm[1], bm[2]);
            lock (HullCache)
            {
                if (!HullCache.TryGetValue(shape, out _)) HullCache.Add(shape, d);
            }

            return d;
        }

        /// <summary>
        /// Which way the body's cross-section is round (see <see cref="RigidBody.DragRound"/>): spheres and
        /// cylinders by kind; a model's hull by measuring its projected area at 45° in each pair of axes — a
        /// round section keeps its width (√(A_i² + A_j²)/√2), a flat one shows both faces ((A_i + A_j)/√2).
        /// </summary>
        private static int RoundAxis(SceneBody body, CollisionShape shape, Vec3 area)
        {
            if (shape.Kind == ShapeKind.Sphere) return 3;
            if (shape.Kind == ShapeKind.Cylinder) return 2;
            if (shape.Kind != ShapeKind.ConvexHull) return -1;
            var q = body.Orientation;
            double[] a = { area.X, area.Y, area.Z };
            Vec3[] e = { q.Rotate(Vec3.UnitX), q.Rotate(Vec3.UnitY), q.Rotate(Vec3.UnitZ) };
            bool[] round = new bool[3];   // round[k]: round in the plane of the other two axes
            for (int k = 0; k < 3; k++)
            {
                int i = (k + 1) % 3, j = (k + 2) % 3;
                double measured = ProjectedArea(body, (e[i] + e[j]).Normalized());
                double flat = (a[i] + a[j]) / Math.Sqrt(2), circle = Math.Sqrt(a[i] * a[i] + a[j] * a[j]) / Math.Sqrt(2);
                round[k] = Math.Abs(measured - circle) < Math.Abs(measured - flat);
            }

            if (round[0] && round[1] && round[2]) return 3;
            for (int k = 0; k < 3; k++)
                if (round[k]) return k;
            return -1;
        }

        /// <summary>Area of the body's shadow seen along <paramref name="dir"/> (m², sampled).</summary>
        public static double ProjectedArea(SceneBody body, Vec3 dir)
        {
            var shape = body.EffectiveShape;
            if (shape == null) return 0;
            double r = Math.Max(1e-4, shape.BoundingRadius), cell = 2 * r / Samples;
            var d = dir.Normalized();
            var u = Math.Abs(d.X) < 0.9 ? d.Cross(Vec3.UnitX).Normalized() : d.Cross(Vec3.UnitY).Normalized();
            var v = d.Cross(u);
            int hits = 0;
            for (int i = 0; i < Samples; i++)
                for (int j = 0; j < Samples; j++)
                    if (Interval(body, body.Position + u * (-r + (i + 0.5) * cell) + v * (-r + (j + 0.5) * cell), d) != null) hits++;
            return hits * cell * cell;
        }

        private static bool Supported(CollisionShape s) =>
            s.Kind == ShapeKind.Box || s.Kind == ShapeKind.Sphere || s.Kind == ShapeKind.Cylinder || s.Kind == ShapeKind.ConvexHull && s.Planes.Length > 0;

        /// <summary>
        /// Projected area of the body along each of its axes, and the covered fraction of its +axis and
        /// −axis faces.
        /// </summary>
        public static (Vec3 Area, Vec3 CoverPlus, Vec3 CoverMinus) Faces(SceneBody body, IReadOnlyList<SceneBody>? near)
        {
            var shape = body.EffectiveShape!;
            var (lo, hi) = LocalBounds(shape);
            var q = body.Orientation;
            double[] area = new double[3], plus = new double[3], minus = new double[3];
            double[] lo3 = { lo.X, lo.Y, lo.Z }, hi3 = { hi.X, hi.Y, hi.Z };
            for (int axis = 0; axis < 3; axis++)
            {
                // Sample the face over the body's own extent across this axis (a slender part is not missed).
                int ua = (axis + 1) % 3, va = (axis + 2) % 3;
                var d = q.Rotate(axis == 0 ? Vec3.UnitX : axis == 1 ? Vec3.UnitY : Vec3.UnitZ);
                var u = q.Rotate(ua == 0 ? Vec3.UnitX : ua == 1 ? Vec3.UnitY : Vec3.UnitZ);
                var v = q.Rotate(va == 0 ? Vec3.UnitX : va == 1 ? Vec3.UnitY : Vec3.UnitZ);
                double cu = (hi3[ua] - lo3[ua]) / Samples, cv = (hi3[va] - lo3[va]) / Samples;
                int hits = 0, coveredPlus = 0, coveredMinus = 0;
                for (int i = 0; i < Samples; i++)
                {
                    for (int j = 0; j < Samples; j++)
                    {
                        var origin = body.Position + u * (lo3[ua] + (i + 0.5) * cu) + v * (lo3[va] + (j + 0.5) * cv);
                        if (Interval(body, origin, d) is not { } own) continue;
                        hits++;
                        if (near == null) continue;
                        bool cp = false, cm = false;
                        foreach (var n in near)
                        {
                            if (n.EffectiveShape == null || !Supported(n.EffectiveShape) || Interval(n, origin, d) is not { } o) continue;
                            // Covered: the neighbour's solid reaches past this face and begins no further than Gap beyond it.
                            if (o.Max > own.Max + 1e-5 && o.Min < own.Max + Gap) cp = true;
                            if (o.Min < own.Min - 1e-5 && o.Max > own.Min - Gap) cm = true;
                        }

                        if (cp) coveredPlus++;
                        if (cm) coveredMinus++;
                    }
                }

                area[axis] = hits * cu * cv;
                plus[axis] = hits > 0 ? (double)coveredPlus / hits : 0;
                minus[axis] = hits > 0 ? (double)coveredMinus / hits : 0;
            }

            return (new Vec3(area[0], area[1], area[2]), new Vec3(plus[0], plus[1], plus[2]), new Vec3(minus[0], minus[1], minus[2]));
        }

        /// <summary>The shape's extent in its body frame (m).</summary>
        public static (Vec3 Min, Vec3 Max) LocalBounds(CollisionShape s)
        {
            switch (s.Kind)
            {
                case ShapeKind.Box: return (-s.HalfExtents, s.HalfExtents);
                case ShapeKind.Sphere: return (new Vec3(-s.Radius, -s.Radius, -s.Radius), new Vec3(s.Radius, s.Radius, s.Radius));
                case ShapeKind.Cylinder: return (new Vec3(-s.Radius, -s.Radius, -s.HalfHeight), new Vec3(s.Radius, s.Radius, s.HalfHeight));
            }

            if (s.Vertices.Count > 0)
            {
                var min = s.Vertices[0];
                var max = s.Vertices[0];
                foreach (var p in s.Vertices)
                {
                    min = Vec3.Min(min, p);
                    max = Vec3.Max(max, p);
                }

                return (min, max);
            }

            double r = Math.Max(1e-4, s.BoundingRadius);
            return (new Vec3(-r, -r, -r), new Vec3(r, r, r));
        }

        /// <summary>Where the line origin + t·dir runs through the body's solid (t range), or null.</summary>
        public static (double Min, double Max)? Interval(SceneBody body, Vec3 origin, Vec3 dir)
        {
            var s = body.EffectiveShape;
            if (s == null) return null;
            var q = body.Orientation;
            var o = q.InverseRotate(origin - body.Position);
            var d = q.InverseRotate(dir);
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            bool Slab(double oc, double dc, double half)
            {
                if (Math.Abs(dc) < 1e-12) return Math.Abs(oc) <= half;
                double t1 = (-half - oc) / dc, t2 = (half - oc) / dc;
                lo = Math.Max(lo, Math.Min(t1, t2));
                hi = Math.Min(hi, Math.Max(t1, t2));
                return lo <= hi;
            }

            switch (s.Kind)
            {
                case ShapeKind.Box:
                {
                    var h = s.HalfExtents;
                    if (!Slab(o.X, d.X, h.X) || !Slab(o.Y, d.Y, h.Y) || !Slab(o.Z, d.Z, h.Z)) return null;
                    return (lo, hi);
                }

                case ShapeKind.Sphere:
                {
                    double b = o.Dot(d), c = o.Dot(o) - s.Radius * s.Radius, disc = b * b - c;
                    if (disc < 0) return null;
                    double sq = Math.Sqrt(disc);
                    return (-b - sq, -b + sq);
                }

                case ShapeKind.Cylinder:
                {
                    // Round about the body z axis, ±HalfHeight along it.
                    if (!Slab(o.Z, d.Z, s.HalfHeight)) return null;
                    double a = d.X * d.X + d.Y * d.Y, b = o.X * d.X + o.Y * d.Y, c = o.X * o.X + o.Y * o.Y - s.Radius * s.Radius;
                    if (a < 1e-12)
                    {
                        if (c > 0) return null;
                    }
                    else
                    {
                        double disc = b * b - a * c;
                        if (disc < 0) return null;
                        double sq = Math.Sqrt(disc);
                        lo = Math.Max(lo, (-b - sq) / a);
                        hi = Math.Min(hi, (-b + sq) / a);
                    }

                    return lo <= hi ? (lo, hi) : null;
                }

                case ShapeKind.ConvexHull:
                {
                    foreach (var p in s.Planes)
                    {
                        double dn = p.Normal.Dot(d), dist = p.Normal.Dot(o) - p.Offset;
                        if (Math.Abs(dn) < 1e-12)
                        {
                            if (dist > 0) return null;
                            continue;
                        }

                        double t = -dist / dn;
                        if (dn > 0) hi = Math.Min(hi, t);
                        else lo = Math.Max(lo, t);
                        if (lo > hi) return null;
                    }

                    return double.IsInfinity(lo) || double.IsInfinity(hi) ? null : (lo, hi);
                }

                default:
                    return null;
            }
        }
    }
}
