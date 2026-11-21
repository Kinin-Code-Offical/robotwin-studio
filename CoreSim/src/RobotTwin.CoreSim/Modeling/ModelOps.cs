using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    public enum Axis
    {
        X,
        Y,
        Z
    }

    /// <summary>Object-level operations of the modeller (add, duplicate, join, booleans, mirror, arrays …).</summary>
    public static class ModelOps
    {
        public static Vec3 AxisVector(Axis a) => a == Axis.X ? Vec3.UnitX : a == Axis.Y ? Vec3.UnitY : Vec3.UnitZ;

        // ------------------------------------------------------------------ transforms

        /// <summary>Splits an affine matrix into translation, Euler XYZ rotation (degrees) and scale.</summary>
        public static (Vec3 Position, Vec3 Rotation, Vec3 Scale) Decompose(Affine m)
        {
            var cx = new Vec3(m.M00, m.M10, m.M20);
            var cy = new Vec3(m.M01, m.M11, m.M21);
            var cz = new Vec3(m.M02, m.M12, m.M22);
            double sx = cx.Length, sy = cy.Length, sz = cz.Length;
            if (m.Determinant < 0) sx = -sx;
            var rx = sx != 0 ? cx / sx : Vec3.UnitX;
            var ry = sy != 0 ? cy / sy : Vec3.UnitY;
            var rz = sz != 0 ? cz / sz : Vec3.UnitZ;
            return (m.Translation, EulerFromMatrix(rx, ry, rz), new Vec3(sx, sy, sz));
        }

        /// <summary>Euler XYZ (degrees) of a rotation given by its columns (R = Rz·Ry·Rx).</summary>
        public static Vec3 EulerFromMatrix(Vec3 c0, Vec3 c1, Vec3 c2)
        {
            double r00 = c0.X, r10 = c0.Y, r20 = c0.Z, r01 = c1.X, r11 = c1.Y, r21 = c1.Z, r22 = c2.Z;
            double a, b, c;
            if (Math.Abs(r20) < 0.9999999)
            {
                b = Math.Asin(-r20);
                a = Math.Atan2(r21, r22);
                c = Math.Atan2(r10, r00);
            }
            else
            {
                b = r20 < 0 ? Math.PI / 2 : -Math.PI / 2;
                a = 0;
                c = Math.Atan2(-r01, r11);
            }

            const double k = 180 / Math.PI;
            return new Vec3(Clean(a * k), Clean(b * k), Clean(c * k));
        }

        private static double Clean(double deg)
        {
            double r = Math.Round(deg, 9);
            return r == 0 ? 0 : r;
        }

        /// <summary>Sets the local transform so the object ends up with the given world matrix.</summary>
        public static void SetWorldMatrix(ModelDocument doc, ModelObject o, Affine world)
        {
            var local = doc.ParentMatrix(o).Inverse() * world;
            var (p, r, s) = Decompose(local);
            o.Transform.Position = Round(p);
            o.Transform.Rotation = r;
            o.Transform.Scale = Round(s);
        }

        private static Vec3 Round(Vec3 v) => new Vec3(Math.Round(v.X, 9), Math.Round(v.Y, 9), Math.Round(v.Z, 9));

        // ------------------------------------------------------------------ create

        /// <summary>Adds a primitive standing on the floor at (x, y).</summary>
        public static ModelObject AddPrimitive(ModelDocument doc, PrimitiveKind kind, double x = 0, double y = 0, double floorZ = 0)
        {
            var spec = PrimitiveSchema.Create(kind);
            var o = new ModelObject { Name = PrimitiveSchema.Name(kind), Primitive = spec };
            doc.Add(o);
            o.Transform.Position = new Vec3(x, y, floorZ + PrimitiveSchema.BottomOffset(spec));
            return o;
        }

        public static ModelObject AddMesh(ModelDocument doc, PolyMesh mesh, string name)
        {
            var o = new ModelObject { Name = name };
            o.SetMesh(mesh);
            return doc.Add(o);
        }

        /// <summary>Copies objects (with the same parent); returns the copies in order.</summary>
        public static List<ModelObject> Duplicate(ModelDocument doc, IEnumerable<ModelObject> objects)
        {
            var copies = new List<ModelObject>();
            foreach (var o in objects.ToList())
            {
                var c = o.Clone();
                c.Name = o.Name;
                doc.Add(c);
                copies.Add(c);
            }

            return copies;
        }

        // ------------------------------------------------------------------ combine

        /// <summary>Merges the objects' meshes into <paramref name="active"/> (Ctrl+J); the others are removed.</summary>
        public static ModelObject Join(ModelDocument doc, ModelObject active, IEnumerable<ModelObject> others)
        {
            var inv = doc.WorldMatrix(active).Inverse();
            var mesh = active.LocalMesh().Clone();
            foreach (var o in others.Where(o => o != active).ToList())
            {
                mesh.Append(doc.WorldMesh(o).Transform(inv));
                doc.Remove(o);
            }

            active.SetMesh(mesh);
            return active;
        }

        /// <summary>
        /// Boolean of the target with the tools (difference subtracts every tool). The target
        /// becomes an editable mesh; tools are removed unless <paramref name="keepTools"/> (then hidden).
        /// </summary>
        public static ModelObject Boolean(ModelDocument doc, ModelObject target, IEnumerable<ModelObject> tools, BooleanOp op, bool keepTools = false)
        {
            var toolList = tools.Where(t => t != target).ToList();
            var result = doc.WorldMesh(target);
            foreach (var t in toolList) result = Csg.Apply(result, doc.WorldMesh(t), op);
            result.Transform(doc.WorldMatrix(target).Inverse());
            target.SetMesh(result);
            foreach (var t in toolList)
            {
                if (keepTools) t.Visible = false;
                else doc.Remove(t);
            }

            return target;
        }

        /// <summary>Turns a primitive into an editable mesh (keeps the transform).</summary>
        public static void ConvertToMesh(ModelObject o)
        {
            if (o.Primitive == null) return;
            o.SetMesh(o.LocalMesh().Clone());
        }

        /// <summary>Bakes rotation and scale into the mesh (Ctrl+A); the position stays.</summary>
        public static void ApplyTransform(ModelDocument doc, ModelObject o)
        {
            var t = o.Transform;
            var lin = Affine.Euler(t.Rotation) * Affine.Scale(t.Scale);
            var mesh = o.LocalMesh().Transformed(lin);
            o.SetMesh(mesh);
            // children keep their world placement
            var children = doc.Children(o.Id).Select(c => (c, doc.WorldMatrix(c))).ToList();
            t.Rotation = Vec3.Zero;
            t.Scale = new Vec3(1, 1, 1);
            foreach (var (c, w) in children) SetWorldMatrix(doc, c, w);
        }

        /// <summary>Moves the object origin to the centre of its bounding box without moving the geometry.</summary>
        public static void OriginToGeometry(ModelDocument doc, ModelObject o)
        {
            var local = o.LocalMesh();
            if (local.IsEmpty) return;
            var c = local.Bounds.Center;
            var world = doc.WorldMatrix(o);
            var mesh = local.Clone().Translate(-c);
            o.SetMesh(mesh);
            SetWorldMatrix(doc, o, world * Affine.Translate(c));
        }

        /// <summary>Moves the selected faces of a mesh object into a new object (P → Separate).</summary>
        public static ModelObject? Separate(ModelDocument doc, ModelObject o, IEnumerable<int> faces)
        {
            var sel = new HashSet<int>(faces);
            if (sel.Count == 0 || o.Primitive != null) return null;
            var src = o.LocalMesh().Clone();
            var part = new PolyMesh();
            part.Vertices.AddRange(src.Vertices);
            foreach (int f in sel.Where(f => f < src.Faces.Count)) part.Faces.Add((int[])src.Faces[f].Clone());
            part.RemoveUnusedVertices();
            MeshEdit.DeleteFaces(src, sel);
            o.SetMesh(src);
            var c = o.Clone();
            c.Name = o.Name;
            doc.Add(c);
            c.SetMesh(part);
            return c;
        }

        // ------------------------------------------------------------------ slice

        /// <summary>
        /// Cuts an object in two with a world plane: <paramref name="o"/> keeps the part behind the plane,
        /// a new object gets the part in front; both openings are capped so each piece is a closed solid
        /// again. Returns null when the plane misses the object.
        /// </summary>
        public static ModelObject? Slice(ModelDocument doc, ModelObject o, Vec3 worldPoint, Vec3 worldNormal)
        {
            var src = o.LocalMesh();
            if (src.IsEmpty) return null;
            var w = doc.WorldMatrix(o);
            var lp = w.Inverse().Apply(worldPoint);
            // a plane's normal maps with the transpose of the linear part (exact under non-uniform scale)
            var n = worldNormal;
            var ln = new Vec3(w.M00 * n.X + w.M10 * n.Y + w.M20 * n.Z, w.M01 * n.X + w.M11 * n.Y + w.M21 * n.Z, w.M02 * n.X + w.M12 * n.Y + w.M22 * n.Z);
            var (front, back) = MeshCut.Slice(src, lp, ln);
            if (front.IsEmpty || back.IsEmpty) return null;
            o.SetMesh(back);
            var c = o.Clone();
            c.Name = o.Name;
            doc.Add(c);
            c.SetMesh(front);
            return c;
        }

        // ------------------------------------------------------------------ mirror & arrays

        private static bool IsSymmetricPrimitive(ModelObject o) =>
            o.Primitive != null && (o.Primitive.Kind == PrimitiveKind.Box || o.Primitive.Kind == PrimitiveKind.Cylinder || o.Primitive.Kind == PrimitiveKind.Cone ||
                                    o.Primitive.Kind == PrimitiveKind.Sphere || o.Primitive.Kind == PrimitiveKind.Torus || o.Primitive.Kind == PrimitiveKind.Tube);

        /// <summary>
        /// Mirrored copies across the document plane through <paramref name="center"/> perpendicular to <paramref name="axis"/>
        /// (left/right versions of a part). Symmetric primitives stay parametric.
        /// </summary>
        public static List<ModelObject> MirrorCopy(ModelDocument doc, IEnumerable<ModelObject> objects, Axis axis, Vec3 center)
        {
            var mirror = Affine.Mirror(AxisVector(axis), center);
            var localMirror = Affine.Scale(new Vec3(-1, 1, 1));
            var result = new List<ModelObject>();
            foreach (var o in objects.ToList())
            {
                var c = o.Clone();
                c.Name = o.Name;
                doc.Add(c);
                var world = mirror * doc.WorldMatrix(o) * localMirror;
                if (!IsSymmetricPrimitive(o)) c.SetMesh(o.LocalMesh().Transformed(localMirror));
                SetWorldMatrix(doc, c, world);
                result.Add(c);
            }

            return result;
        }

        /// <summary>Mirrors objects in place (their meshes are baked).</summary>
        public static void MirrorInPlace(ModelDocument doc, IEnumerable<ModelObject> objects, Axis axis, Vec3 center)
        {
            var mirror = Affine.Mirror(AxisVector(axis), center);
            var localMirror = Affine.Scale(new Vec3(-1, 1, 1));
            foreach (var o in objects.ToList())
            {
                var world = mirror * doc.WorldMatrix(o) * localMirror;
                if (!IsSymmetricPrimitive(o)) o.SetMesh(o.LocalMesh().Transformed(localMirror));
                SetWorldMatrix(doc, o, world);
            }
        }

        /// <summary><paramref name="count"/> − 1 copies, each moved by <paramref name="offset"/> (mm) from the previous one.</summary>
        public static List<ModelObject> ArrayLinear(ModelDocument doc, IEnumerable<ModelObject> objects, int count, Vec3 offset)
        {
            var result = new List<ModelObject>();
            var src = objects.ToList();
            for (int i = 1; i < Math.Max(1, count); i++)
            {
                foreach (var o in src)
                {
                    var c = o.Clone();
                    c.Name = o.Name;
                    doc.Add(c);
                    SetWorldMatrix(doc, c, Affine.Translate(offset * i) * doc.WorldMatrix(o));
                    result.Add(c);
                }
            }

            return result;
        }

        /// <summary>
        /// <paramref name="count"/> − 1 copies rotated about an axis through <paramref name="center"/>;
        /// <paramref name="totalDegrees"/> = 360 spreads them evenly around the circle.
        /// </summary>
        public static List<ModelObject> ArrayCircular(ModelDocument doc, IEnumerable<ModelObject> objects, int count, double totalDegrees, Axis axis, Vec3 center)
        {
            var result = new List<ModelObject>();
            var src = objects.ToList();
            count = Math.Max(1, count);
            double step = Math.Abs(totalDegrees) >= 359.999 ? totalDegrees / count : count > 1 ? totalDegrees / (count - 1) : 0;
            for (int i = 1; i < count; i++)
            {
                var rot = Affine.Translate(center) * Affine.Rotate(AxisVector(axis), step * i) * Affine.Translate(-center);
                foreach (var o in src)
                {
                    var c = o.Clone();
                    c.Name = o.Name;
                    doc.Add(c);
                    SetWorldMatrix(doc, c, rot * doc.WorldMatrix(o));
                    result.Add(c);
                }
            }

            return result;
        }

        // ------------------------------------------------------------------ export

        public static Aabb WorldBounds(ModelDocument doc, IEnumerable<ModelObject> objects)
        {
            var b = Aabb.Empty;
            foreach (var o in objects) b = b.Union(doc.WorldMesh(o).Bounds);
            return b;
        }

        /// <summary>All objects merged into one polygon mesh in document coordinates (mm).</summary>
        public static PolyMesh Merged(ModelDocument doc, IEnumerable<ModelObject> objects)
        {
            var m = new PolyMesh();
            foreach (var o in objects) m.Append(doc.WorldMesh(o));
            return m;
        }

        /// <summary>
        /// Triangle mesh in metres for export or the world, with per-triangle colours of the
        /// objects; <paramref name="name"/> becomes the mesh name.
        /// </summary>
        public static MeshModel ToMeshModel(ModelDocument doc, IEnumerable<ModelObject> objects, string name)
        {
            var result = new MeshModel { Name = name };
            foreach (var o in objects)
            {
                var tri = doc.WorldMesh(o).Triangulate();
                tri.Scale(0.001);
                MeshColor col;
                try
                {
                    col = MeshColor.FromHex(o.Color);
                }
                catch (FormatException)
                {
                    col = MeshColor.DefaultGrey;
                }

                tri.TriangleColors = Enumerable.Repeat(col, tri.TriangleCount).ToList();
                result.Append(tri);
            }

            return result;
        }

        /// <summary>Imports a triangle mesh (metres, from <see cref="MeshModel.Load(string, MeshLoadOptions)"/>) as a mm polygon mesh with merged coplanar faces.</summary>
        public static PolyMesh FromImported(MeshModel mesh)
        {
            var size = mesh.Bounds.Size;
            double tol = Math.Max(1e-6, Math.Max(size.X, Math.Max(size.Y, size.Z)) * 1000 * 1e-7);
            var p = PolyMesh.FromMeshModel(mesh, 1000, tol);
            if (p.Faces.Count <= 200_000) p.DissolveCoplanar(1e-3, tol * 10);
            return p;
        }
    }
}
