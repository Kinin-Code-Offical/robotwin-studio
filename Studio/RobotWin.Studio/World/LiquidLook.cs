using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [liquid] How a liquid looks. WPF has no shaders, so the optics are done per vertex on the CPU: the Fresnel
    /// reflectance of the surface towards the camera (Schlick's approximation with the liquid's refractive index) picks a
    /// colour from a gradient between the light coming through the liquid (its body colour, mostly clear for water) and
    /// the sky it reflects at grazing angles; a sharp specular highlight carries the sun.
    /// </summary>
    public static class LiquidLook
    {
        /// <summary>Reflectance at normal incidence, ((n − 1)/(n + 1))², n = 1.333 for water.</summary>
        public const double WaterF0 = 0.0204;

        private static readonly Color Sky = Color.FromRgb(0xDC, 0xEA, 0xF4);

        private static Color Body(LiquidKind kind, double alpha) =>
            Color.FromArgb((byte)Math.Round(255 * alpha), (byte)(kind.Color >> 16), (byte)(kind.Color >> 8), (byte)kind.Color);

        /// <summary>Transmitted alpha at normal incidence: clear liquids mostly let the light through.</summary>
        private static double BaseAlpha(LiquidKind kind) => Math.Max(0.22, Math.Min(0.96, 0.22 + 0.75 * kind.Opacity));

        /// <summary>A plain material (the editor's volume, drops in the editor).</summary>
        public static Material Material(LiquidKind kind, double alpha)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Body(kind, Math.Max(alpha, BaseAlpha(kind))))));
            g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0xE0, 255, 255, 255)), 80));
            g.Freeze();
            return g;
        }

        /// <summary>
        /// The surface material: texture u = Fresnel reflectance (0 = looking straight in, 1 = grazing), mapped to the
        /// transmitted body colour → the reflected sky; the sky reflection also glows a little so it shows in shade.
        /// </summary>
        public static Material Surface(LiquidKind kind)
        {
            double a0 = BaseAlpha(kind);
            var diffuse = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            diffuse.GradientStops.Add(new GradientStop(Body(kind, a0), 0));
            diffuse.GradientStops.Add(new GradientStop(Color.FromArgb(0xE6, Sky.R, Sky.G, Sky.B), 1));
            var glow = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x70, Sky.R, Sky.G, Sky.B), 1));
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(diffuse));
            g.Children.Add(new EmissiveMaterial(glow));
            g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0xF0, 255, 255, 255)), 120));
            g.Freeze();
            return g;
        }

        /// <summary>Schlick: F = F0 + (1 − F0)(1 − cos θ)⁵, θ between the surface normal and the view direction.</summary>
        public static double Fresnel(double cosTheta) => WaterF0 + (1 - WaterF0) * Math.Pow(1 - Math.Max(0, Math.Min(1, cosTheta)), 5);

        /// <summary>Exposure of the eye (s): a falling drop is seen as the streak it draws in that time, like a camera at 60 fps.</summary>
        public const double Exposure = 1.0 / 60;

        /// <summary>A WPF mesh of the liquid's surface (and its drops) with Fresnel texture coordinates for this eye point.</summary>
        public static MeshGeometry3D Mesh(LiquidMesh surface, double dropRadius, Point3D eye)
        {
            var m = new MeshGeometry3D();
            int vertices = surface.VertexCount + surface.Drops.Length / 3 * 12;
            var pos = new Point3DCollection(vertices);
            var nrm = new Vector3DCollection(vertices);
            var tex = new PointCollection(vertices);
            var idx = new Int32Collection(surface.Triangles.Length + surface.Drops.Length / 3 * 60);
            void Vertex(Point3D p, Vector3D n)
            {
                pos.Add(p);
                nrm.Add(n);
                var v = eye - p;
                double len = v.Length;
                double c = len > 1e-12 ? Vector3D.DotProduct(n, v) / len : 1;
                tex.Add(new Point(Fresnel(Math.Abs(c)), 0));
            }

            for (int i = 0; i < surface.VertexCount; i++)
                Vertex(new Point3D(surface.Positions[3 * i], surface.Positions[3 * i + 1], surface.Positions[3 * i + 2]),
                    new Vector3D(surface.Normals[3 * i], surface.Normals[3 * i + 1], surface.Normals[3 * i + 2]));
            foreach (int t in surface.Triangles) idx.Add(t);
            // Drops: icosahedra (12 vertices, 20 faces), each vertex normal radial.
            bool moving = surface.DropVelocities.Length == surface.Drops.Length;
            for (int d = 0; d + 2 < surface.Drops.Length; d += 3)
            {
                int b = pos.Count;
                var c = new Point3D(surface.Drops[d], surface.Drops[d + 1], surface.Drops[d + 2]);
                // Stretched along its velocity by the distance it covers during the exposure (centred on where it is).
                var vel = moving ? new Vector3D(surface.DropVelocities[d], surface.DropVelocities[d + 1], surface.DropVelocities[d + 2]) : new Vector3D();
                double streak = vel.Length * Exposure;
                var dir = streak > 1e-9 ? vel / vel.Length : new Vector3D();
                foreach (var v in Ico)
                {
                    var p = c + v * dropRadius;
                    if (streak > 0.2 * dropRadius) p += dir * (Vector3D.DotProduct(v, dir) * 0.5 * streak);
                    Vertex(p, v);
                }

                foreach (int t in IcoFaces) idx.Add(b + t);
            }

            m.Positions = pos;
            m.Normals = nrm;
            m.TextureCoordinates = tex;
            m.TriangleIndices = idx;
            m.Freeze();
            return m;
        }

        private static readonly Vector3D[] Ico = BuildIco();

        private static readonly int[] IcoFaces =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        private static Vector3D[] BuildIco()
        {
            double t = (1 + Math.Sqrt(5)) / 2;
            var v = new[]
            {
                new Vector3D(-1, t, 0), new Vector3D(1, t, 0), new Vector3D(-1, -t, 0), new Vector3D(1, -t, 0),
                new Vector3D(0, -1, t), new Vector3D(0, 1, t), new Vector3D(0, -1, -t), new Vector3D(0, 1, -t),
                new Vector3D(t, 0, -1), new Vector3D(t, 0, 1), new Vector3D(-t, 0, -1), new Vector3D(-t, 0, 1)
            };
            for (int i = 0; i < v.Length; i++) v[i].Normalize();
            return v;
        }
    }
}
