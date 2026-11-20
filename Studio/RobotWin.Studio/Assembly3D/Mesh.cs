using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Assembly3D
{
    /// <summary>Procedural meshes in metres, Z up.</summary>
    public static class Mesh
    {
        public static MeshGeometry3D Box(double cx, double cy, double cz, double sx, double sy, double sz)
        {
            var m = new MeshGeometry3D();
            double x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2, z0 = cz - sz / 2, z1 = cz + sz / 2;
            Quad(m, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), new Vector3D(0, 0, 1));
            Quad(m, new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), new Vector3D(0, 0, -1));
            Quad(m, new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1), new Vector3D(1, 0, 0));
            Quad(m, new Point3D(x0, y1, z0), new Point3D(x0, y0, z0), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Vector3D(-1, 0, 0));
            Quad(m, new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Vector3D(0, 1, 0));
            Quad(m, new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), new Vector3D(0, -1, 0));
            m.Freeze();
            return m;
        }

        /// <summary>Flat textured rectangle on the XY plane (top face only), UV 0..1 across.</summary>
        public static MeshGeometry3D TexturedTop(double cx, double cy, double z, double sx, double sy)
        {
            var m = new MeshGeometry3D();
            double x0 = cx - sx / 2, x1 = cx + sx / 2, y0 = cy - sy / 2, y1 = cy + sy / 2;
            m.Positions.Add(new Point3D(x0, y1, z));
            m.Positions.Add(new Point3D(x1, y1, z));
            m.Positions.Add(new Point3D(x1, y0, z));
            m.Positions.Add(new Point3D(x0, y0, z));
            for (int i = 0; i < 4; i++) m.Normals.Add(new Vector3D(0, 0, 1));
            m.TextureCoordinates.Add(new Point(0, 0));
            m.TextureCoordinates.Add(new Point(1, 0));
            m.TextureCoordinates.Add(new Point(1, 1));
            m.TextureCoordinates.Add(new Point(0, 1));
            m.TriangleIndices.Add(0);
            m.TriangleIndices.Add(3);
            m.TriangleIndices.Add(2);
            m.TriangleIndices.Add(0);
            m.TriangleIndices.Add(2);
            m.TriangleIndices.Add(1);
            m.Freeze();
            return m;
        }

        /// <summary>Closed cylinder centred at the origin along the given axis (0 = X, 1 = Y, 2 = Z).</summary>
        public static MeshGeometry3D Cylinder(double radius, double length, int axis, int segments = 28, double cx = 0, double cy = 0, double cz = 0)
        {
            var m = new MeshGeometry3D();
            Point3D P(double a, double r, double h)
            {
                double u = Math.Cos(a) * r, v = Math.Sin(a) * r;
                return axis == 0 ? new Point3D(cx + h, cy + u, cz + v) : axis == 1 ? new Point3D(cx + u, cy + h, cz + v) : new Point3D(cx + u, cy + v, cz + h);
            }

            Vector3D N(double a)
            {
                double u = Math.Cos(a), v = Math.Sin(a);
                return axis == 0 ? new Vector3D(0, u, v) : axis == 1 ? new Vector3D(u, 0, v) : new Vector3D(u, v, 0);
            }

            var up = axis == 0 ? new Vector3D(1, 0, 0) : axis == 1 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
            double h0 = -length / 2, h1 = length / 2;
            for (int i = 0; i < segments; i++)
            {
                double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
                int b = m.Positions.Count;
                m.Positions.Add(P(a0, radius, h0));
                m.Positions.Add(P(a1, radius, h0));
                m.Positions.Add(P(a1, radius, h1));
                m.Positions.Add(P(a0, radius, h1));
                m.Normals.Add(N(a0));
                m.Normals.Add(N(a1));
                m.Normals.Add(N(a1));
                m.Normals.Add(N(a0));
                AddQuadIndices(m, b, axis == 1);

                // caps
                b = m.Positions.Count;
                m.Positions.Add(P(0, 0, h1));
                m.Positions.Add(P(a0, radius, h1));
                m.Positions.Add(P(a1, radius, h1));
                for (int k = 0; k < 3; k++) m.Normals.Add(up);
                AddTri(m, b, b + 1, b + 2, axis == 1);
                b = m.Positions.Count;
                m.Positions.Add(P(0, 0, h0));
                m.Positions.Add(P(a1, radius, h0));
                m.Positions.Add(P(a0, radius, h0));
                for (int k = 0; k < 3; k++) m.Normals.Add(-up);
                AddTri(m, b, b + 1, b + 2, axis == 1);
            }

            m.Freeze();
            return m;
        }

        public static MeshGeometry3D Sphere(double radius, double cx, double cy, double cz, int seg = 16)
        {
            var m = new MeshGeometry3D();
            for (int i = 0; i <= seg; i++)
            {
                double th = Math.PI * i / seg;
                for (int j = 0; j <= seg; j++)
                {
                    double ph = 2 * Math.PI * j / seg;
                    var n = new Vector3D(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th));
                    m.Positions.Add(new Point3D(cx + n.X * radius, cy + n.Y * radius, cz + n.Z * radius));
                    m.Normals.Add(n);
                }
            }

            for (int i = 0; i < seg; i++)
            {
                for (int j = 0; j < seg; j++)
                {
                    int a = i * (seg + 1) + j, b = a + seg + 1;
                    m.TriangleIndices.Add(a);
                    m.TriangleIndices.Add(b);
                    m.TriangleIndices.Add(a + 1);
                    m.TriangleIndices.Add(a + 1);
                    m.TriangleIndices.Add(b);
                    m.TriangleIndices.Add(b + 1);
                }
            }

            m.Freeze();
            return m;
        }

        /// <summary>Thin disc in the XY plane (motion-blurred propeller).</summary>
        public static MeshGeometry3D Disc(double radius, double z, int segments = 36)
        {
            var m = new MeshGeometry3D();
            m.Positions.Add(new Point3D(0, 0, z));
            m.Normals.Add(new Vector3D(0, 0, 1));
            for (int i = 0; i <= segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                m.Positions.Add(new Point3D(Math.Cos(a) * radius, Math.Sin(a) * radius, z));
                m.Normals.Add(new Vector3D(0, 0, 1));
            }

            for (int i = 1; i <= segments; i++)
            {
                m.TriangleIndices.Add(0);
                m.TriangleIndices.Add(i);
                m.TriangleIndices.Add(i + 1);
                m.TriangleIndices.Add(0);
                m.TriangleIndices.Add(i + 1);
                m.TriangleIndices.Add(i);
            }

            m.Freeze();
            return m;
        }

        private static void Quad(MeshGeometry3D m, Point3D a, Point3D b, Point3D c, Point3D d, Vector3D n)
        {
            int i = m.Positions.Count;
            m.Positions.Add(a);
            m.Positions.Add(b);
            m.Positions.Add(c);
            m.Positions.Add(d);
            for (int k = 0; k < 4; k++) m.Normals.Add(n);
            m.TextureCoordinates.Add(new Point(0, 1));
            m.TextureCoordinates.Add(new Point(1, 1));
            m.TextureCoordinates.Add(new Point(1, 0));
            m.TextureCoordinates.Add(new Point(0, 0));
            m.TriangleIndices.Add(i);
            m.TriangleIndices.Add(i + 1);
            m.TriangleIndices.Add(i + 2);
            m.TriangleIndices.Add(i);
            m.TriangleIndices.Add(i + 2);
            m.TriangleIndices.Add(i + 3);
        }

        private static void AddQuadIndices(MeshGeometry3D m, int b, bool flip)
        {
            AddTri(m, b, b + 1, b + 2, flip);
            AddTri(m, b, b + 2, b + 3, flip);
        }

        private static void AddTri(MeshGeometry3D m, int a, int b, int c, bool flip)
        {
            m.TriangleIndices.Add(a);
            if (flip)
            {
                m.TriangleIndices.Add(c);
                m.TriangleIndices.Add(b);
            }
            else
            {
                m.TriangleIndices.Add(b);
                m.TriangleIndices.Add(c);
            }
        }

        public static GeometryModel3D Model(MeshGeometry3D mesh, Material material) =>
            new GeometryModel3D(mesh, material) { BackMaterial = material };

        public static Material Matte(Color c, double specular = 0)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (specular > 0) g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb((byte)(specular * 255), 255, 255, 255)), 40));
            return g;
        }

        public static Color Rgb(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}
