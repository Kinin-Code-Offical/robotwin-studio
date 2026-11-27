using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[refgeom] SolidWorks' reference geometry: a plane, an axis or a point.</summary>
    public enum ReferenceKind
    {
        /// <summary>A plane: the object's own XY plane, its normal the object's Z.</summary>
        Plane,

        /// <summary>An axis: the line along the object's Z through its origin.</summary>
        Axis,

        /// <summary>A point: the object's origin.</summary>
        Point
    }

    /// <summary>[refgeom] What makes a model object a reference: its kind and the size it is drawn at. It has no solid.</summary>
    public sealed class ModelReference
    {
        public ReferenceKind Kind { get; set; }

        /// <summary>The size it is drawn at (mm): a plane's side, an axis's length.</summary>
        public double Size { get; set; } = 80;

        public ModelReference Clone() => (ModelReference)MemberwiseClone();
    }

    /// <summary>
    /// [refgeom] SolidWorks' reference planes, axes and points, as model objects with no solid: placed by their transform
    /// (moved, turned and parented like any object) and drawn as outlines. Made the ways SolidWorks makes them (a plane
    /// through three points, offset from another, square to an axis; an axis through two points, along a round part, where
    /// two planes meet; a point at a place or the middle of parts); mirrors and circular patterns take them.
    /// </summary>
    public static class ModelReferences
    {
        /// <summary>A reference of <paramref name="kind"/> at <paramref name="origin"/>, its Z along <paramref name="direction"/>.</summary>
        public static ModelObject Add(ModelDocument doc, ReferenceKind kind, Vec3 origin, Vec3 direction, string? name = null, double size = 80)
        {
            var o = new ModelObject
            {
                Id = NewId(doc),
                Name = name ?? NextName(doc, kind),
                Reference = new ModelReference { Kind = kind, Size = size },
                Color = kind == ReferenceKind.Point ? "#E0A43A" : "#5B8DEF"
            };
            doc.Objects.Add(o);
            ModelOps.SetWorldMatrix(doc, o, Affine.Translate(origin) * Affine.FromQuat(ZTo(direction)));
            return o;
        }

        private static string NewId(ModelDocument doc)
        {
            var used = new HashSet<string>(doc.Objects.Select(o => o.Id), StringComparer.Ordinal);
            for (int i = 1; ; i++)
            {
                string id = "ref" + i;
                if (!used.Contains(id)) return id;
            }
        }

        /// <summary>"Düzlem1", "Eksen2", "Nokta1": the next free name, as SolidWorks numbers them.</summary>
        public static string NextName(ModelDocument doc, ReferenceKind kind)
        {
            string stem = kind == ReferenceKind.Plane ? "Düzlem" : kind == ReferenceKind.Axis ? "Eksen" : "Nokta";
            var names = new HashSet<string>(doc.Objects.Select(o => o.Name), StringComparer.Ordinal);
            for (int i = 1; ; i++)
            {
                if (!names.Contains(stem + i)) return stem + i;
            }
        }

        /// <summary>The turn that takes +Z onto <paramref name="direction"/> by the shortest way.</summary>
        public static Quat ZTo(Vec3 direction)
        {
            var d = direction.Length > 1e-12 ? direction.Normalized() : Vec3.UnitZ;
            var axis = Vec3.UnitZ.Cross(d);
            double sin = axis.Length, cos = Vec3.UnitZ.Dot(d);
            if (sin < 1e-12) return cos > 0 ? Quat.Identity : Quat.FromAxisAngle(Vec3.UnitX, Math.PI);
            return Quat.FromAxisAngle(axis / sin, Math.Atan2(sin, cos));
        }

        /// <summary>Where a reference is in the model: its origin and its Z (a plane's normal, an axis's direction).</summary>
        public static (Vec3 Origin, Vec3 Direction) Frame(ModelDocument doc, ModelObject o)
        {
            var m = doc.WorldMatrix(o);
            var z = m.ApplyVector(Vec3.UnitZ);
            return (m.Apply(Vec3.Zero), z.Length > 1e-12 ? z.Normalized() : Vec3.UnitZ);
        }

        // ------------------------------------------------------------------ planes

        /// <summary>A plane through three points (their order sets which way it faces); null when they lie on a line.</summary>
        public static ModelObject? PlaneThrough(ModelDocument doc, Vec3 a, Vec3 b, Vec3 c)
        {
            var n = (b - a).Cross(c - a);
            if (n.Length < 1e-9) return null;
            return Add(doc, ReferenceKind.Plane, (a + b + c) / 3, n);
        }

        /// <summary>A plane parallel to a plane, <paramref name="offset"/> mm along its normal.</summary>
        public static ModelObject PlaneOffset(ModelDocument doc, ModelObject plane, double offset)
        {
            var (p, n) = Frame(doc, plane);
            return Add(doc, ReferenceKind.Plane, p + n * offset, n, size: plane.Reference?.Size ?? 80);
        }

        /// <summary>A plane square to an axis, through a point on it (the axis's origin when none is given).</summary>
        public static ModelObject PlaneNormalTo(ModelDocument doc, ModelObject axis, Vec3? through = null)
        {
            var (p, d) = Frame(doc, axis);
            var at = through is { } t ? p + d * (t - p).Dot(d) : p;
            return Add(doc, ReferenceKind.Plane, at, d);
        }

        /// <summary>A plane through an axis, turned <paramref name="degrees"/> from the plane given about it (SolidWorks' "at angle").</summary>
        public static ModelObject PlaneAtAngle(ModelDocument doc, ModelObject plane, ModelObject axis, double degrees)
        {
            var (_, n) = Frame(doc, plane);
            var (p, d) = Frame(doc, axis);
            var turned = Affine.Rotate(d, degrees).ApplyVector(n);
            return Add(doc, ReferenceKind.Plane, p, turned);
        }

        /// <summary>The plane half way between two parallel planes (SolidWorks' mid plane); null when they are not parallel.</summary>
        public static ModelObject? MidPlane(ModelDocument doc, ModelObject a, ModelObject b)
        {
            var (pa, na) = Frame(doc, a);
            var (pb, nb) = Frame(doc, b);
            if (na.Cross(nb).Length > 1e-6) return null;
            var mid = pa + na * ((pb - pa).Dot(na) / 2);
            return Add(doc, ReferenceKind.Plane, mid, na);
        }

        // ------------------------------------------------------------------ axes

        /// <summary>An axis through two points; null when they are the same point.</summary>
        public static ModelObject? AxisThrough(ModelDocument doc, Vec3 a, Vec3 b)
        {
            if ((b - a).Length < 1e-9) return null;
            return Add(doc, ReferenceKind.Axis, (a + b) / 2, b - a, size: Math.Max(40, (b - a).Length * 1.4));
        }

        /// <summary>The axis of a round part (cylinder, cone, tube, torus, revolve, a hole's own Z): its own Z through its origin.</summary>
        public static ModelObject? AxisOf(ModelDocument doc, ModelObject part)
        {
            if (part.Primitive == null || !IsRound(part.Primitive.Kind)) return null;
            var m = doc.WorldMatrix(part);
            var bounds = doc.WorldMesh(part).Bounds;
            double size = bounds.IsEmpty ? 80 : Math.Max(40, (bounds.Max - bounds.Min).Length * 1.2);
            return Add(doc, ReferenceKind.Axis, m.Apply(Vec3.Zero), m.ApplyVector(Vec3.UnitZ), size: size);
        }

        /// <summary>Whether a primitive is turned about its own Z.</summary>
        public static bool IsRound(PrimitiveKind k) => k is PrimitiveKind.Cylinder or PrimitiveKind.Cone or PrimitiveKind.Tube or PrimitiveKind.Torus or PrimitiveKind.Revolve;

        /// <summary>The axis where two planes meet; null when they are parallel.</summary>
        public static ModelObject? AxisWhere(ModelDocument doc, ModelObject a, ModelObject b)
        {
            var (pa, na) = Frame(doc, a);
            var (pb, nb) = Frame(doc, b);
            var d = na.Cross(nb);
            double l2 = d.Dot(d);
            if (l2 < 1e-12) return null;
            // the point on both planes nearest the origin: (h1 (n2 × d) + h2 (d × n1)) / |d|²
            double h1 = na.Dot(pa), h2 = nb.Dot(pb);
            var p = (nb.Cross(d) * h1 + d.Cross(na) * h2) / l2;
            // brought near the planes' own origins so it is drawn where they are
            var mid = (pa + pb) / 2;
            var dn = d.Normalized();
            p += dn * (mid - p).Dot(dn);
            return Add(doc, ReferenceKind.Axis, p, dn);
        }

        // ------------------------------------------------------------------ points

        public static ModelObject PointAt(ModelDocument doc, Vec3 p) => Add(doc, ReferenceKind.Point, p, Vec3.UnitZ, size: 10);

        /// <summary>A point at the middle of parts' bounds (SolidWorks' centre of face, for a whole part).</summary>
        public static ModelObject? PointAtCenter(ModelDocument doc, IEnumerable<ModelObject> parts)
        {
            var solid = parts.Where(o => !o.IsReference).ToList();
            if (solid.Count == 0) return null;
            var b = ModelOps.WorldBounds(doc, solid);
            return b.IsEmpty ? null : PointAt(doc, (b.Min + b.Max) / 2);
        }

        // ------------------------------------------------------------------ used by features

        /// <summary>Mirrored copies of parts across a reference plane.</summary>
        public static List<ModelObject> MirrorCopy(ModelDocument doc, IEnumerable<ModelObject> parts, ModelObject plane)
        {
            var (p, n) = Frame(doc, plane);
            return ModelOps.MirrorCopy(doc, parts.Where(o => !o.IsReference), n, p);
        }

        /// <summary>A circular pattern of parts about a reference axis.</summary>
        public static List<ModelObject> ArrayCircular(ModelDocument doc, IEnumerable<ModelObject> parts, int count, double totalDegrees, ModelObject axis)
        {
            var (p, d) = Frame(doc, axis);
            return ModelOps.ArrayCircular(doc, parts.Where(o => !o.IsReference), count, totalDegrees, d, p);
        }
    }
}
