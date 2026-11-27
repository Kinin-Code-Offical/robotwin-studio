using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    public enum ShapeKind
    {
        /// <summary>A cloud of contact points (vehicle chassis corners); its convex hull acts as the solid others hit.</summary>
        Points,
        Box,
        Sphere,

        /// <summary>Solid cylinder along the body z axis.</summary>
        Cylinder,
        ConvexHull,

        /// <summary>Arbitrary (concave) triangle mesh, static bodies only.</summary>
        Mesh
    }

    /// <summary>
    /// Collision geometry of a body, expressed in the body frame (origin = centre of mass).
    /// Polyhedral shapes keep their vertices (contact candidates) and outward face planes
    /// (for signed distances); round shapes are handled analytically.
    /// </summary>
    public sealed class CollisionShape
    {
        private static readonly Vec3[] NoVerts = Array.Empty<Vec3>();
        private static readonly HullPlane[] NoPlanes = Array.Empty<HullPlane>();

        internal Vec3[] Verts = NoVerts;
        internal HullPlane[] Planes = NoPlanes;
        internal bool[] FloorPlane = Array.Empty<bool>();
        internal bool[] Caster = Array.Empty<bool>();
        internal MeshBvh? Bvh;

        private CollisionShape(ShapeKind kind)
        {
            Kind = kind;
        }

        public ShapeKind Kind { get; }
        public Vec3 HalfExtents { get; private set; }
        public double Radius { get; private set; }
        public double HalfHeight { get; private set; }

        /// <summary>Contact candidate vertices (body frame).</summary>
        public IReadOnlyList<Vec3> Vertices => Verts;

        private (double Lo, double Hi)[]? _planeSpans;

        /// <summary>
        /// [perf] How far the vertices reach along each face normal (normalised), lowest and highest: what a separating-axis
        /// test needs for the face axes, worked out once instead of over every vertex for every axis of every test.
        /// </summary>
        internal (double Lo, double Hi)[] PlaneSpans
        {
            get
            {
                var spans = _planeSpans;
                if (spans != null && spans.Length == Planes.Length) return spans;
                spans = new (double, double)[Planes.Length];
                for (int k = 0; k < Planes.Length; k++)
                {
                    var n = Planes[k].Normal;
                    double nl = n.Length;
                    if (nl >= 1e-9) n /= nl;
                    double lo = double.MaxValue, hi = double.MinValue;
                    foreach (var v in Verts)
                    {
                        double q = v.Dot(n);
                        if (q < lo) lo = q;
                        if (q > hi) hi = q;
                    }

                    spans[k] = (lo, hi);
                }

                return _planeSpans = spans;
            }
        }

        public MeshModel? Mesh { get; private set; }

        /// <summary>Radius of a sphere about the body origin that encloses the shape.</summary>
        public double BoundingRadius { get; private set; }

        /// <summary>
        /// The middle of the shape (body frame) and the radius about it that holds the whole solid: tighter than
        /// <see cref="BoundingRadius"/> for a convex piece of a concave body, which lies off the body's origin.
        /// </summary>
        internal Vec3 LocalCentre { get; private set; }

        internal double LocalRadius => _localRadius >= 0 ? _localRadius : BoundingRadius;
        private double _localRadius = -1;

        private void Enclose(IEnumerable<Vec3> points)
        {
            var lo = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
            var hi = -lo;
            int n = 0;
            foreach (var v in points)
            {
                lo = new Vec3(Math.Min(lo.X, v.X), Math.Min(lo.Y, v.Y), Math.Min(lo.Z, v.Z));
                hi = new Vec3(Math.Max(hi.X, v.X), Math.Max(hi.Y, v.Y), Math.Max(hi.Z, v.Z));
                n++;
            }

            if (n == 0) return;
            var c = (lo + hi) * 0.5;
            double r = 0;
            foreach (var v in points) r = Math.Max(r, (v - c).Length);
            LocalCentre = c;
            _localRadius = r;
        }

        /// <summary>Solid volume (m³), 0 for point clouds.</summary>
        public double Volume { get; private set; }

        internal bool HasSolid => Kind == ShapeKind.Sphere || Kind == ShapeKind.Cylinder || Kind == ShapeKind.Mesh || Planes.Length > 0;
        internal bool IsRound => Kind == ShapeKind.Sphere || Kind == ShapeKind.Cylinder;

        // ------------------------------------------------------------------ factories

        public static CollisionShape Box(Vec3 size)
        {
            var h = new Vec3(Math.Abs(size.X) / 2, Math.Abs(size.Y) / 2, Math.Abs(size.Z) / 2);
            var s = new CollisionShape(ShapeKind.Box) { HalfExtents = h, Volume = 8 * h.X * h.Y * h.Z };
            var v = new Vec3[8];
            int k = 0;
            for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            for (int m = 0; m < 2; m++)
                v[k++] = new Vec3(i == 0 ? -h.X : h.X, j == 0 ? -h.Y : h.Y, m == 0 ? -h.Z : h.Z);
            s.Verts = v;
            s.Planes = new[]
            {
                new HullPlane(Vec3.UnitX, h.X), new HullPlane(-Vec3.UnitX, h.X), new HullPlane(Vec3.UnitY, h.Y), new HullPlane(-Vec3.UnitY, h.Y),
                new HullPlane(Vec3.UnitZ, h.Z), new HullPlane(-Vec3.UnitZ, h.Z)
            };
            s.FloorPlane = new bool[6];
            s.Caster = new bool[8];
            s.BoundingRadius = h.Length;
            return s;
        }

        public static CollisionShape Sphere(double radius)
        {
            double r = Math.Abs(radius);
            return new CollisionShape(ShapeKind.Sphere) { Radius = r, BoundingRadius = r, Volume = 4.0 / 3 * Math.PI * r * r * r };
        }

        /// <summary>Solid cylinder of the given radius and height along the body z axis.</summary>
        public static CollisionShape Cylinder(double radius, double height)
        {
            double r = Math.Abs(radius), hh = Math.Abs(height) / 2;
            return new CollisionShape(ShapeKind.Cylinder)
            {
                Radius = r, HalfHeight = hh, BoundingRadius = Math.Sqrt(r * r + hh * hh), Volume = Math.PI * r * r * 2 * hh
            };
        }

        /// <summary>Convex hull of points (body frame); at most <paramref name="maxVertices"/> well-spread contact vertices.</summary>
        public static CollisionShape ConvexHull(IReadOnlyList<Vec3> points, int maxVertices = 32)
        {
            var hull = Mechanics.ConvexHull.Compute(points);
            return FromHull(hull, maxVertices);
        }

        public static CollisionShape FromHull(ConvexHull hull, int maxVertices = 32)
        {
            var s = new CollisionShape(ShapeKind.ConvexHull);
            var verts = ContactGeometry.ContactPointsFromHull(hull, maxVertices);
            s.Verts = verts.ToArray();
            s.Caster = new bool[s.Verts.Length];
            if (!hull.IsDegenerate)
            {
                s.Planes = new HullPlane[hull.Planes.Count];
                for (int i = 0; i < s.Planes.Length; i++) s.Planes[i] = hull.Planes[i];
                s.FloorPlane = new bool[s.Planes.Length];
                try
                {
                    s.Volume = MassProperties.FromMesh(hull.ToMesh(), 1).Volume;
                }
                catch (ArgumentException)
                {
                    s.Volume = 0;
                }
            }

            double r = 0;
            foreach (var v in hull.Vertices) r = Math.Max(r, v.Length);
            s.BoundingRadius = r;
            s.Enclose(hull.Vertices);
            return s;
        }

        /// <summary>
        /// [rigidbody] The same solid moved by <paramref name="d"/> in the body frame (a body whose centre of mass was set
        /// by hand moves its solid the other way): boxes, hulls and point clouds exactly (their corners and faces moved);
        /// a sphere or a cylinder, whose round test needs it on the origin, as a fine hull of its surface.
        /// </summary>
        public CollisionShape Translated(Vec3 d)
        {
            if (d.LengthSquared < 1e-24) return this;
            switch (Kind)
            {
                case ShapeKind.Box:
                case ShapeKind.ConvexHull:
                case ShapeKind.Points:
                    {
                        var s = new CollisionShape(Kind == ShapeKind.Points ? ShapeKind.Points : ShapeKind.ConvexHull)
                        {
                            Verts = new Vec3[Verts.Length],
                            Planes = new HullPlane[Planes.Length],
                            FloorPlane = (bool[])FloorPlane.Clone(),
                            Caster = (bool[])Caster.Clone(),
                            Volume = Volume,
                            BoundingRadius = BoundingRadius + d.Length,
                            LocalCentre = LocalCentre + d,
                            _localRadius = LocalRadius
                        };
                        for (int i = 0; i < Verts.Length; i++) s.Verts[i] = Verts[i] + d;
                        for (int i = 0; i < Planes.Length; i++) s.Planes[i] = new HullPlane(Planes[i].Normal, Planes[i].Offset + Planes[i].Normal.Dot(d));
                        return s;
                    }

                case ShapeKind.Sphere:
                    {
                        var pts = new List<Vec3>();
                        const int rings = 8, segs = 16;   // every corner on the sphere, the poles too
                        for (int i = 0; i <= rings; i++)
                        {
                            double th = Math.PI * i / rings;
                            for (int j = 0; j < (i == 0 || i == rings ? 1 : segs); j++)
                            {
                                double ph = 2 * Math.PI * j / segs;
                                pts.Add(new Vec3(Radius * Math.Sin(th) * Math.Cos(ph), Radius * Math.Sin(th) * Math.Sin(ph), Radius * Math.Cos(th)) + d);
                            }
                        }

                        return ConvexHull(pts, pts.Count);
                    }

                case ShapeKind.Cylinder:
                    {
                        var pts = new List<Vec3>();
                        const int segs = 48;
                        for (int j = 0; j < segs; j++)
                        {
                            double ph = 2 * Math.PI * j / segs;
                            pts.Add(new Vec3(Radius * Math.Cos(ph), Radius * Math.Sin(ph), -HalfHeight) + d);
                            pts.Add(new Vec3(Radius * Math.Cos(ph), Radius * Math.Sin(ph), HalfHeight) + d);
                        }

                        return ConvexHull(pts, 96);
                    }

                default:
                    return this;   // a triangle mesh is static: it has no centre of mass to move
            }
        }

        /// <summary>Contact point cloud (e.g. chassis corners) plus low-friction caster points.</summary>
        public static CollisionShape Points(IReadOnlyList<Vec3> points, IReadOnlyList<Vec3>? casters = null)
        {
            var s = new CollisionShape(ShapeKind.Points);
            int n = points.Count, c = casters?.Count ?? 0;
            s.Verts = new Vec3[n + c];
            s.Caster = new bool[n + c];
            double r = 0;
            for (int i = 0; i < n; i++)
            {
                s.Verts[i] = points[i];
                r = Math.Max(r, points[i].Length);
            }

            for (int i = 0; i < c; i++)
            {
                s.Verts[n + i] = casters![i];
                s.Caster[n + i] = true;
                r = Math.Max(r, casters[i].Length);
            }

            s.BoundingRadius = r;
            if (n + c >= 4)
            {
                var hull = Mechanics.ConvexHull.Compute(s.Verts);
                if (!hull.IsDegenerate)
                {
                    s.Planes = new HullPlane[hull.Planes.Count];
                    for (int i = 0; i < s.Planes.Length; i++) s.Planes[i] = hull.Planes[i];
                    s.FloorPlane = new bool[s.Planes.Length];
                }
            }

            return s;
        }

        /// <summary>Concave triangle mesh in the body frame (static bodies only).</summary>
        public static CollisionShape TriangleMesh(MeshModel mesh)
        {
            var s = new CollisionShape(ShapeKind.Mesh) { Mesh = mesh, Bvh = new MeshBvh(mesh) };
            double r = 0;
            foreach (var v in mesh.Vertices) r = Math.Max(r, v.Length);
            s.BoundingRadius = r;
            // A handful of hull vertices let the mesh's corners hit dynamic bodies too.
            var hull = Mechanics.ConvexHull.FromMesh(mesh);
            s.Verts = hull.IsDegenerate ? NoVerts : ContactGeometry.ContactPointsFromHull(hull, 32).ToArray();
            s.Caster = new bool[s.Verts.Length];
            return s;
        }

        /// <summary>
        /// Marks faces that lie on the ground plane z = <paramref name="groundZ"/> when the body is
        /// at the given pose: such faces never push other bodies down into the floor.
        /// </summary>
        public void MarkFloorFaces(Vec3 position, Quat orientation, double groundZ = 0, double tolerance = 1e-3)
        {
            for (int i = 0; i < Planes.Length; i++)
            {
                var n = orientation.Rotate(Planes[i].Normal);
                if (n.Z > -0.99) continue;
                // World plane: n·p = offset + n·position; for a face pointing straight down that is z = −offset'.
                double worldOffset = Planes[i].Offset + n.Dot(position);
                FloorPlane[i] = Math.Abs(worldOffset / n.Z - groundZ) < tolerance;
            }

            HasFloorFace = Array.IndexOf(FloorPlane, true) >= 0;
        }

        /// <summary>A face rests on the floor (see <see cref="MarkFloorFaces"/>): below it the signed distance leaves the point to the ground.</summary>
        internal bool HasFloorFace { get; private set; }

        /// <summary>Exact world-space bounding box of the shape at a pose.</summary>
        public Aabb WorldBounds(Vec3 position, Quat orientation)
        {
            switch (Kind)
            {
                case ShapeKind.Sphere:
                    return new Aabb(position - new Vec3(Radius, Radius, Radius), position + new Vec3(Radius, Radius, Radius));
                case ShapeKind.Cylinder:
                {
                    var a = orientation.Rotate(Vec3.UnitZ);
                    var e = new Vec3(Radius * Math.Sqrt(Math.Max(0, 1 - a.X * a.X)) + HalfHeight * Math.Abs(a.X),
                        Radius * Math.Sqrt(Math.Max(0, 1 - a.Y * a.Y)) + HalfHeight * Math.Abs(a.Y),
                        Radius * Math.Sqrt(Math.Max(0, 1 - a.Z * a.Z)) + HalfHeight * Math.Abs(a.Z));
                    return new Aabb(position - e, position + e);
                }

                case ShapeKind.Box:
                {
                    var r = Mat3.FromQuat(orientation);
                    var h = HalfExtents;
                    var e = new Vec3(Math.Abs(r.M00) * h.X + Math.Abs(r.M01) * h.Y + Math.Abs(r.M02) * h.Z,
                        Math.Abs(r.M10) * h.X + Math.Abs(r.M11) * h.Y + Math.Abs(r.M12) * h.Z,
                        Math.Abs(r.M20) * h.X + Math.Abs(r.M21) * h.Y + Math.Abs(r.M22) * h.Z);
                    return new Aabb(position - e, position + e);
                }

                case ShapeKind.Mesh when Mesh != null:
                {
                    var b = Aabb.Empty;
                    foreach (var v in Mesh.Vertices) b = b.Include(position + orientation.Rotate(v));
                    return b;
                }

                default:
                {
                    if (Verts.Length == 0) return new Aabb(position, position);
                    var b = Aabb.Empty;
                    foreach (var v in Verts) b = b.Include(position + orientation.Rotate(v));
                    return b;
                }
            }
        }

        // ------------------------------------------------------------------ local queries

        /// <summary>
        /// Signed distance from a body-frame point to the solid (negative inside) and the outward
        /// normal there. Returns false when the shape has no solid or the point is far/irrelevant.
        /// </summary>
        internal bool SignedDistance(Vec3 q, double maxDistance, out double distance, out Vec3 normal)
        {
            distance = double.PositiveInfinity;
            normal = Vec3.UnitZ;
            // [perf] A point farther than maxDistance from the sphere that holds the whole solid is farther from the solid
            // too: settled without its faces or its tree (a cable next to a part made of many convex pieces asks every
            // piece about every bit of wire, several times a substep).
            if (Kind == ShapeKind.ConvexHull || Kind == ShapeKind.Points || Kind == ShapeKind.Mesh)
            {
                double reach = LocalRadius + maxDistance;
                if (reach < 1e150 && (q - LocalCentre).LengthSquared > reach * reach) return false;
            }

            switch (Kind)
            {
                case ShapeKind.Sphere:
                {
                    double len = q.Length;
                    distance = len - Radius;
                    normal = len > 1e-12 ? q / len : Vec3.UnitZ;
                    return distance < maxDistance;
                }

                case ShapeKind.Box:
                {
                    var h = HalfExtents;
                    double ox = Math.Abs(q.X) - h.X, oy = Math.Abs(q.Y) - h.Y, oz = Math.Abs(q.Z) - h.Z;
                    if (ox > 0 || oy > 0 || oz > 0)
                    {
                        var c = new Vec3(Math.Max(-h.X, Math.Min(h.X, q.X)), Math.Max(-h.Y, Math.Min(h.Y, q.Y)), Math.Max(-h.Z, Math.Min(h.Z, q.Z)));
                        var d = q - c;
                        distance = d.Length;
                        if (distance >= maxDistance) return false;
                        normal = distance > 1e-12 ? d / distance : Vec3.UnitZ;
                        return true;
                    }

                    if (ox >= oy && ox >= oz)
                    {
                        distance = ox;
                        normal = new Vec3(q.X >= 0 ? 1 : -1, 0, 0);
                    }
                    else if (oy >= oz)
                    {
                        distance = oy;
                        normal = new Vec3(0, q.Y >= 0 ? 1 : -1, 0);
                    }
                    else
                    {
                        distance = oz;
                        normal = new Vec3(0, 0, q.Z >= 0 ? 1 : -1);
                    }

                    return true;
                }

                case ShapeKind.Cylinder:
                {
                    double rho = Math.Sqrt(q.X * q.X + q.Y * q.Y);
                    double dr = rho - Radius, dz = Math.Abs(q.Z) - HalfHeight;
                    var radial = rho > 1e-12 ? new Vec3(q.X / rho, q.Y / rho, 0) : Vec3.UnitX;
                    var axial = new Vec3(0, 0, q.Z >= 0 ? 1 : -1);
                    if (dr > 0 && dz > 0)
                    {
                        distance = Math.Sqrt(dr * dr + dz * dz);
                        normal = (radial * dr + axial * dz) / distance;
                    }
                    else if (dr > dz)
                    {
                        distance = dr;
                        normal = radial;
                    }
                    else
                    {
                        distance = dz;
                        normal = axial;
                    }

                    return distance < maxDistance;
                }

                case ShapeKind.Mesh:
                {
                    if (Bvh == null || !Bvh.ClosestPoint(q, maxDistance + 0.05, out var c, out var fn, out double dist)) return false;
                    var d = q - c;
                    bool inside = fn.Dot(d) < 0;
                    if (!inside && dist >= maxDistance) return false;
                    distance = inside ? -dist : dist;
                    normal = dist > 1e-9 ? (inside ? -d / dist : d / dist) : fn;
                    if (inside && (normal.Dot(fn) < 0.3)) normal = fn;
                    return true;
                }

                default:
                {
                    if (Planes.Length == 0) return false;
                    double best = double.NegativeInfinity;
                    int bi = -1;
                    for (int i = 0; i < Planes.Length; i++)
                    {
                        double d = Planes[i].Distance(q);
                        if (FloorPlane[i])
                        {
                            // Below a face that rests on the floor: the ground handles it.
                            if (d > 0) return false;
                            continue;
                        }

                        if (d > best)
                        {
                            best = d;
                            bi = i;
                            if (best >= maxDistance) return false;
                        }
                    }

                    if (bi < 0) return false;
                    distance = best;
                    normal = Planes[bi].Normal;
                    return true;
                }
            }
        }

        /// <summary>Deepest point of a round shape in direction <paramref name="dir"/> (body frame).</summary>
        internal Vec3 Support(Vec3 dir)
        {
            switch (Kind)
            {
                case ShapeKind.Sphere:
                    return dir.Normalized() * Radius;
                case ShapeKind.Cylinder:
                {
                    var radial = new Vec3(dir.X, dir.Y, 0);
                    double len = radial.Length;
                    var rim = len > 1e-9 ? radial * (Radius / len) : Vec3.Zero;
                    return rim + new Vec3(0, 0, dir.Z >= 0 ? HalfHeight : -HalfHeight);
                }

                case ShapeKind.Box:
                    return new Vec3(dir.X >= 0 ? HalfExtents.X : -HalfExtents.X, dir.Y >= 0 ? HalfExtents.Y : -HalfExtents.Y,
                        dir.Z >= 0 ? HalfExtents.Z : -HalfExtents.Z);
                default:
                {
                    var best = Vec3.Zero;
                    double bd = double.NegativeInfinity;
                    foreach (var v in Verts)
                    {
                        double d = v.Dot(dir);
                        if (d > bd)
                        {
                            bd = d;
                            best = v;
                        }
                    }

                    return best;
                }
            }
        }

        /// <summary>
        /// Contact candidates of a cylinder toward direction <paramref name="dir"/> (body frame):
        /// the deepest rim point of each cap, plus rim samples when a cap faces the direction.
        /// </summary>
        /// <summary>
        /// Points of the tread line a cylinder lying on its side touches with: both edges and the middle. With the edges
        /// alone, a wheel turning on the spot stuck at one edge and slid at the other (two points cannot share the scrub
        /// of a turn), so a differential-drive robot turned about the inner edges, 26 % faster than its wheel spacing says;
        /// with the middle, the edges' scrub cancels and the wheel rolls about its centre line, as a tyre's patch does.
        /// </summary>
        internal const int TreadPoints = 3;

        internal int CylinderCandidates(Vec3 dir, Span<Vec3> output)
        {
            int n = 0;
            var radial = new Vec3(dir.X, dir.Y, 0);
            double len = radial.Length;
            double dl = dir.Length;
            if (len > 1e-6 * Math.Max(dl, 1e-12))
            {
                var rim = radial * (Radius / len);
                output[n++] = rim + new Vec3(0, 0, HalfHeight);
                output[n++] = rim + new Vec3(0, 0, -HalfHeight);
                output[n++] = rim;   // [phys] the middle of the tread
            }

            // Caps facing the direction (upright cylinder on a face): a ring of samples for a stable support.
            if (Math.Abs(dir.Z) > 0.2 * dl)
            {
                double z = dir.Z > 0 ? HalfHeight : -HalfHeight;
                for (int i = 0; i < 6; i++)
                {
                    double a = i * Math.PI / 3;
                    output[n++] = new Vec3(Radius * Math.Cos(a), Radius * Math.Sin(a), z);
                }
            }

            return n;
        }

        /// <summary>Ray in the body frame; returns the entry distance and outward normal.</summary>
        internal bool RaycastLocal(Vec3 o, Vec3 d, double maxDistance, out double t, out Vec3 n)
        {
            t = 0;
            n = Vec3.UnitZ;
            // [perf] A ray that passes outside the sphere holding the whole solid misses it.
            if (Kind == ShapeKind.ConvexHull || Kind == ShapeKind.Points || Kind == ShapeKind.Mesh)
            {
                double dd = d.LengthSquared;
                if (dd > 0)
                {
                    double along = Math.Max(0, Math.Min(maxDistance, (LocalCentre - o).Dot(d) / dd));
                    double radius = LocalRadius;
                    if ((o + d * along - LocalCentre).LengthSquared > radius * radius) return false;
                }
            }

            switch (Kind)
            {
                case ShapeKind.Sphere:
                {
                    double b = o.Dot(d), c = o.LengthSquared - Radius * Radius;
                    if (c > 0 && b > 0) return false;
                    double disc = b * b - c;
                    if (disc < 0) return false;
                    t = Math.Max(0, -b - Math.Sqrt(disc));
                    if (t > maxDistance) return false;
                    n = (o + d * t).Normalized();
                    return true;
                }

                case ShapeKind.Box:
                    return RayPlanes(o, d, maxDistance, out t, out n);
                case ShapeKind.Cylinder:
                {
                    double best = double.PositiveInfinity;
                    var bn = Vec3.UnitZ;
                    // Side
                    double a = d.X * d.X + d.Y * d.Y;
                    if (a > 1e-14)
                    {
                        double b = o.X * d.X + o.Y * d.Y, c = o.X * o.X + o.Y * o.Y - Radius * Radius;
                        double disc = b * b - a * c;
                        if (disc >= 0)
                        {
                            double ts = (-b - Math.Sqrt(disc)) / a;
                            if (ts >= 0)
                            {
                                var p = o + d * ts;
                                if (Math.Abs(p.Z) <= HalfHeight && ts < best)
                                {
                                    best = ts;
                                    bn = new Vec3(p.X, p.Y, 0).Normalized();
                                }
                            }
                        }
                    }

                    // Caps
                    if (Math.Abs(d.Z) > 1e-14)
                    {
                        for (int cap = 0; cap < 2; cap++)   // [perf] both caps without an array per ray
                        {
                            double z = cap == 0 ? HalfHeight : -HalfHeight;
                            double tc = (z - o.Z) / d.Z;
                            if (tc < 0 || tc >= best) continue;
                            if ((z > 0 && d.Z > 0) || (z < 0 && d.Z < 0)) continue;
                            var p = o + d * tc;
                            if (p.X * p.X + p.Y * p.Y <= Radius * Radius)
                            {
                                best = tc;
                                bn = new Vec3(0, 0, Math.Sign(z));
                            }
                        }
                    }

                    if (o.X * o.X + o.Y * o.Y <= Radius * Radius && Math.Abs(o.Z) <= HalfHeight) best = 0;
                    if (best > maxDistance) return false;
                    t = best;
                    n = bn;
                    return true;
                }

                case ShapeKind.Mesh:
                    if (Bvh != null && Bvh.Raycast(o, d, maxDistance, out var hit))
                    {
                        t = hit.Distance;
                        n = hit.Normal;
                        return true;
                    }

                    return false;
                default:
                    return Planes.Length > 0 && RayPlanes(o, d, maxDistance, out t, out n);
            }
        }

        private bool RayPlanes(Vec3 o, Vec3 d, double maxDistance, out double t, out Vec3 n)
        {
            double tEnter = 0, tExit = maxDistance;
            n = Vec3.UnitZ;
            t = 0;
            bool entered = false;
            for (int i = 0; i < Planes.Length; i++)
            {
                var pl = Planes[i];
                double denom = pl.Normal.Dot(d), dist = pl.Distance(o);
                if (Math.Abs(denom) < 1e-14)
                {
                    if (dist > 0) return false;
                    continue;
                }

                double ti = -dist / denom;
                if (denom < 0)
                {
                    if (ti > tEnter)
                    {
                        tEnter = ti;
                        n = pl.Normal;
                        entered = true;
                    }
                }
                else if (ti < tExit)
                {
                    tExit = ti;
                }

                if (tEnter > tExit) return false;
            }

            if (!entered && Planes.Length > 0)
            {
                // Origin inside the solid.
                tEnter = 0;
            }

            t = tEnter;
            return t <= maxDistance;
        }
    }
}
