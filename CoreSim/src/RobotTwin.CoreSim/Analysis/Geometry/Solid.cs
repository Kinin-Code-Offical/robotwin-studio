using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Analysis.Geometry
{
    /// <summary>
    /// [analysis] A solid described by a signed distance (negative inside). Used to mesh parts for FEM,
    /// to build thermal grids and to voxelise bodies for the flow solver. Distances only need to be
    /// exact near the surface (snapping); far away a conservative estimate is fine.
    /// </summary>
    public abstract class Solid
    {
        public abstract Aabb Bounds { get; }

        public abstract double Distance(Vec3 p);

        public bool Contains(Vec3 p) => Distance(p) < 0;

        /// <summary>Outward normal by central differences of the distance.</summary>
        public virtual Vec3 Gradient(Vec3 p, double h)
        {
            double dx = Distance(p + new Vec3(h, 0, 0)) - Distance(p - new Vec3(h, 0, 0));
            double dy = Distance(p + new Vec3(0, h, 0)) - Distance(p - new Vec3(0, h, 0));
            double dz = Distance(p + new Vec3(0, 0, h)) - Distance(p - new Vec3(0, 0, h));
            return new Vec3(dx, dy, dz).Normalized();
        }

        /// <summary>Moves a point onto the surface (a few Newton steps along the gradient).</summary>
        public Vec3 Project(Vec3 p, double h)
        {
            for (int i = 0; i < 4; i++)
            {
                double d = Distance(p);
                if (Math.Abs(d) < 1e-9 * Math.Max(1e-6, h)) break;
                var g = Gradient(p, Math.Max(1e-7, h * 1e-3));
                if (g.LengthSquared < 0.25) break;
                p -= g * d;
            }

            return p;
        }

        public static Solid Box(Vec3 min, Vec3 max) => new BoxSolid(min, max);

        public static Solid Box(Vec3 size) => new BoxSolid(size * -0.5, size * 0.5);

        /// <summary>Cylinder along z centred at the origin.</summary>
        public static Solid Cylinder(double radius, double height) => new CylinderSolid(Vec3.Zero, radius, height / 2);

        public static Solid CylinderAt(Vec3 centre, double radius, double height) => new CylinderSolid(centre, radius, height / 2);

        public static Solid Sphere(double radius) => new SphereSolid(Vec3.Zero, radius);

        public Solid Minus(Solid other) => new DifferenceSolid(this, other);

        public Solid Transformed(Vec3 position, Quat orientation) => new TransformedSolid(this, position, orientation);

        /// <summary>Solid of a collision shape (body frame); null for shapes without volume.</summary>
        public static Solid? FromShape(CollisionShape? shape)
        {
            if (shape == null) return null;
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                    return new BoxSolid(shape.HalfExtents * -1, shape.HalfExtents);
                case ShapeKind.Sphere:
                    return new SphereSolid(Vec3.Zero, shape.Radius);
                case ShapeKind.Cylinder:
                    return new CylinderSolid(Vec3.Zero, shape.Radius, shape.HalfHeight);
                case ShapeKind.ConvexHull when shape.Planes.Length > 0:
                    return new HullSolid(shape.Planes, shape.Vertices);
                case ShapeKind.Mesh when shape.Mesh != null:
                    return new MeshSolid(shape.Mesh, Vec3.Zero);
                default:
                    return null;
            }
        }
    }

    public sealed class BoxSolid : Solid
    {
        private readonly Vec3 _c, _h;

        public BoxSolid(Vec3 min, Vec3 max)
        {
            _c = (min + max) * 0.5;
            _h = (max - min) * 0.5;
        }

        public Vec3 HalfExtents => _h;
        public override Aabb Bounds => new Aabb(_c - _h, _c + _h);

        public override double Distance(Vec3 p)
        {
            var q = p - _c;
            double ox = Math.Abs(q.X) - _h.X, oy = Math.Abs(q.Y) - _h.Y, oz = Math.Abs(q.Z) - _h.Z;
            double ax = Math.Max(ox, 0), ay = Math.Max(oy, 0), az = Math.Max(oz, 0);
            double outside = Math.Sqrt(ax * ax + ay * ay + az * az);
            double inside = Math.Min(Math.Max(ox, Math.Max(oy, oz)), 0);
            return outside + inside;
        }
    }

    public sealed class SphereSolid : Solid
    {
        private readonly Vec3 _c;
        private readonly double _r;

        public SphereSolid(Vec3 centre, double radius)
        {
            _c = centre;
            _r = radius;
        }

        public override Aabb Bounds => new Aabb(_c - new Vec3(_r, _r, _r), _c + new Vec3(_r, _r, _r));
        public override double Distance(Vec3 p) => (p - _c).Length - _r;

        public override Vec3 Gradient(Vec3 p, double h)
        {
            var d = p - _c;
            return d.Length > 1e-15 ? d.Normalized() : Vec3.UnitZ;
        }
    }

    /// <summary>Cylinder along z.</summary>
    public sealed class CylinderSolid : Solid
    {
        private readonly Vec3 _c;
        private readonly double _r, _hh;

        public CylinderSolid(Vec3 centre, double radius, double halfHeight)
        {
            _c = centre;
            _r = radius;
            _hh = halfHeight;
        }

        public override Aabb Bounds => new Aabb(_c - new Vec3(_r, _r, _hh), _c + new Vec3(_r, _r, _hh));

        public override double Distance(Vec3 p)
        {
            var q = p - _c;
            double dr = Math.Sqrt(q.X * q.X + q.Y * q.Y) - _r;
            double dz = Math.Abs(q.Z) - _hh;
            double ar = Math.Max(dr, 0), az = Math.Max(dz, 0);
            return Math.Min(Math.Max(dr, dz), 0) + Math.Sqrt(ar * ar + az * az);
        }
    }

    public sealed class DifferenceSolid : Solid
    {
        private readonly Solid _a, _b;

        public DifferenceSolid(Solid a, Solid b)
        {
            _a = a;
            _b = b;
        }

        public override Aabb Bounds => _a.Bounds;
        public override double Distance(Vec3 p) => Math.Max(_a.Distance(p), -_b.Distance(p));
    }

    public sealed class UnionSolid : Solid
    {
        private readonly List<Solid> _parts;

        public UnionSolid(IEnumerable<Solid> parts)
        {
            _parts = new List<Solid>(parts);
        }

        public override Aabb Bounds
        {
            get
            {
                var b = Aabb.Empty;
                foreach (var s in _parts) b = b.Union(s.Bounds);
                return b;
            }
        }

        public override double Distance(Vec3 p)
        {
            double d = double.PositiveInfinity;
            foreach (var s in _parts) d = Math.Min(d, s.Distance(p));
            return d;
        }
    }

    /// <summary>A solid placed in another frame: world point p maps to local q = R⁻¹(p − x).</summary>
    public sealed class TransformedSolid : Solid
    {
        private readonly Solid _s;
        private readonly Vec3 _x;
        private readonly Quat _q;

        public TransformedSolid(Solid s, Vec3 position, Quat orientation)
        {
            _s = s;
            _x = position;
            _q = orientation;
        }

        public override Aabb Bounds
        {
            get
            {
                var b = _s.Bounds;
                var box = Aabb.Empty;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vec3((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z);
                    box = box.Include(_x + _q.Rotate(c));
                }

                return box;
            }
        }

        public override double Distance(Vec3 p) => _s.Distance(_q.InverseRotate(p - _x));
    }

    /// <summary>Convex hull from its face planes (signed distance is exact inside, a lower bound outside).</summary>
    public sealed class HullSolid : Solid
    {
        private readonly HullPlane[] _planes;
        private readonly Aabb _bounds;

        public HullSolid(IReadOnlyList<HullPlane> planes, IReadOnlyList<Vec3> vertices)
        {
            _planes = new HullPlane[planes.Count];
            for (int i = 0; i < planes.Count; i++) _planes[i] = planes[i];
            _bounds = Aabb.FromPoints(vertices);
        }

        public override Aabb Bounds => _bounds;

        public override double Distance(Vec3 p)
        {
            double d = double.NegativeInfinity;
            foreach (var pl in _planes) d = Math.Max(d, pl.Distance(p));
            return d;
        }
    }

    /// <summary>Closed triangle mesh (frame offset subtracted): inside by the nearest face's normal, distance by BVH.</summary>
    public sealed class MeshSolid : Solid
    {
        private readonly MeshBvh _bvh;
        private readonly Vec3 _offset;
        private readonly Aabb _bounds;

        public MeshSolid(MeshModel mesh, Vec3 offset)
        {
            _bvh = new MeshBvh(mesh);
            _offset = offset;
            _bounds = new Aabb(_bvh.Bounds.Min - offset, _bvh.Bounds.Max - offset);
        }

        public override Aabb Bounds => _bounds;

        public override double Distance(Vec3 p)
        {
            var q = p + _offset;
            double reach = (_bounds.Size.Length + 1e-3) * 2;
            if (!_bvh.ClosestPoint(q, reach, out var c, out var fn, out double dist)) return reach;
            var d = q - c;
            bool inside = fn.Dot(d) < 0;
            return inside ? -dist : dist;
        }
    }
}
