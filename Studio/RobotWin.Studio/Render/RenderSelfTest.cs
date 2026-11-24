using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Render
{
    /// <summary>
    /// [render] <c>--render-test file</c>: checks our renderer end to end. First how <see cref="SceneMirror"/> reads WPF
    /// scenes (layered materials, glows, gradients, nested transforms, lamps); then small scenes drawn by
    /// <see cref="GpuRenderer"/> offscreen and read back pixel by pixel: a lit colour, the sun's shadow, a glow added to
    /// what is behind, a see-through layer, a picture's orientation. On a machine without a GPU the drawing runs on WARP.
    /// </summary>
    internal static class RenderSelfTest
    {
        public static int Run(string outPath)
        {
            var log = new StringBuilder();
            int failures = 0;
            void Check(string name, bool ok, string detail)
            {
                log.AppendLine((ok ? "ok    " : "HATA  ") + name + ": " + detail);
                if (!ok) failures++;
            }

            MirrorChecks(Check);
            var renderer = GpuRenderer.CreateOffscreen(out string driver);
            log.AppendLine("sürücü: " + driver);
            if (renderer == null)
            {
                Check("GPU", false, "Direct3D 11 ne ekran kartında ne WARP'ta açıldı");
            }
            else
            {
                using (renderer)
                    DrawChecks(renderer, Check);
            }

            log.AppendLine(failures == 0 ? "sonuç: geçti" : $"sonuç: {failures} hata");
            File.WriteAllText(outPath, log.ToString());
            return failures == 0 ? 0 : 1;
        }

        // ================================================================ the scene mirror

        private static void MirrorChecks(Action<string, bool, string> check)
        {
            var mirror = new SceneMirror();

            // A part's body: its colour, then an empty charring layer (WPF draws the layers over each other).
            var body = Layers(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD8))), new DiffuseMaterial(new SolidColorBrush(Colors.Transparent)));
            mirror.Read(View(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), body)));
            var m = mirror.Items.Single().Material;
            check("katmanlı gövde", !m.Transparent && m.Overlay.W == 0 && m.BaseColor.Z > m.BaseColor.X, $"saydam={m.Transparent}, örtü={m.Overlay.W:0.00}, renk={m.BaseColor}");

            // Half charred: a black layer at half cover over white.
            var charred = Layers(new DiffuseMaterial(Brushes.White), new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0))));
            mirror.Read(View(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), charred)));
            m = mirror.Items.Single().Material;
            check("yarı kömürleşmiş katman", Math.Abs(m.Overlay.W - 0x80 / 255f) < 0.01 && m.Overlay.X < 0.01, $"örtü={m.Overlay}");

            // Only light of its own: added to what is behind, in the colour chosen.
            mirror.Read(View(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(255, 128, 0))))));
            m = mirror.Items.Single().Material;
            check("yalnız ışıyan", m.Unlit && Math.Abs(m.BaseColor.X - 1) < 1e-3 && Math.Abs(m.BaseColor.Y - Linear(128)) < 1e-3, $"ışıksız={m.Unlit}, renk={m.BaseColor}");

            // A fully clear pick target draws nothing.
            mirror.Read(View(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), new DiffuseMaterial(new SolidColorBrush(Colors.Transparent)))));
            check("tam saydam hedef", mirror.Items.Count == 0, $"{mirror.Items.Count} öğe");

            // A frozen gradient becomes a picture over its own square; one that changes stays a colour.
            var gradient = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
            gradient.Freeze();
            mirror.Read(View(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), new DiffuseMaterial(gradient))));
            m = mirror.Items.Single().Material;
            check("donmuş geçiş → resim", m.Albedo is { Relative: true } && m.Albedo.Value.Image is BitmapSource { PixelWidth: 256 }, $"resim={m.Albedo?.Image?.GetType().Name ?? "yok"}");
            mirror.Read(View(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), new DiffuseMaterial(new LinearGradientBrush(Colors.Red, Colors.Blue, 0)))));
            m = mirror.Items.Single().Material;
            check("değişen geçiş → ortalama renk", m.Albedo == null && m.BaseColor.X > 0.1 && m.BaseColor.Z > 0.1, $"renk={m.BaseColor}");

            // Transforms: the visual's after the model's, the group's after its child's.
            var inner = Model(Box(0, 0, 0, 0.1, 0.1, 0.1), new DiffuseMaterial(Brushes.White));
            inner.Transform = new ScaleTransform3D(2, 2, 2);
            var group = new Model3DGroup { Transform = new TranslateTransform3D(0, 1, 0) };
            group.Children.Add(inner);
            var visual = new ModelVisual3D { Content = group, Transform = new TranslateTransform3D(1, 0, 0) };
            var view = new Viewport3D();
            view.Children.Add(visual);
            mirror.Read(view);
            var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(1, 0, 0), mirror.Items.Single().World);
            check("iç içe dönüşümler", (p - new System.Numerics.Vector3(3, 1, 0)).Length() < 1e-5, $"(1,0,0) → {p}");

            // Lights: the sun, a lamp moved with its group, a spot's cone.
            var lights = new Model3DGroup();
            lights.Children.Add(new DirectionalLight(Colors.White, new Vector3D(0, 0, -1)));
            var lampGroup = new Model3DGroup { Transform = new TranslateTransform3D(0, 0, 2) };
            lampGroup.Children.Add(new PointLight(Colors.Orange, new Point3D(0.5, 0, 0)) { Range = 3, QuadraticAttenuation = 1 });
            lampGroup.Children.Add(new SpotLight(Colors.White, new Point3D(), new Vector3D(0, 0, -1), 60, 30));
            lights.Children.Add(lampGroup);
            mirror.Read(View(lights));
            var lamp = mirror.Lamps.FirstOrDefault(l => !l.Spot);
            var spot = mirror.Lamps.FirstOrDefault(l => l.Spot);
            check("ışıklar", mirror.Suns.Count == 1 && mirror.Lamps.Count == 2 && Math.Abs(lamp.Position.Z - 2) < 1e-6 && Math.Abs(spot.CosOuter - Math.Cos(Math.PI / 6)) < 1e-4,
                $"güneş {mirror.Suns.Count}, lamba {mirror.Lamps.Count}, lamba z={lamp.Position.Z}, spot dış cos={spot.CosOuter:0.000}");
        }

        // ================================================================ drawn and read back

        private const int Size = 256;

        private static void DrawChecks(GpuRenderer renderer, Action<string, bool, string> check)
        {
            var target = renderer.CreateTarget();
            target.Resize(Size, Size);
            var mirror = new SceneMirror();
            var top = new GpuRenderer.Camera(new Point3D(0, 0, 1), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 30, 0.01, 10);
            byte[] Draw(Model3D scene, GpuRenderer.Camera camera, Color background)
            {
                mirror.Read(View(scene));
                renderer.Render(target, mirror, camera, background);
                return renderer.ReadPixels(target);
            }

            // 1) A red box lit by the sun, on the studio's dark ground colour.
            var red = new Model3DGroup();
            red.Children.Add(Sun(new Vector3D(-0.3, 0.4, -0.85)));
            red.Children.Add(new AmbientLight(Color.FromRgb(0x50, 0x50, 0x50)));
            red.Children.Add(Model(Box(0, 0, 0, 0.1, 0.1, 0.1), Plastic(Color.FromRgb(0xD0, 0x20, 0x20))));
            var px = Draw(red, new GpuRenderer.Camera(new Point3D(0.4, -0.4, 0.3), new Vector3D(-0.4, 0.4, -0.3), new Vector3D(0, 0, 1), 40, 0.01, 10), Color.FromRgb(0x0B, 0x0B, 0x0D));
            var (r, g, b) = At(px, Size / 2, Size / 2);
            var corner = At(px, 2, 2);
            check("ışıklı renk", r > 90 && r > 2 * g && r > 2 * b, $"orta ({r},{g},{b})");
            check("zemin rengi", Near(corner, (0x0B, 0x0B, 0x0D), 2), $"köşe {corner}");

            // 2) The sun's shadow: a box over a white floor, the light slanting along +x; seen from straight above.
            var shadow = new Model3DGroup();
            shadow.Children.Add(Sun(new Vector3D(0.5, 0, -1)));
            shadow.Children.Add(new AmbientLight(Color.FromRgb(0x40, 0x40, 0x40)));
            shadow.Children.Add(Model(Box(0, 0, 0, 1, 1, 0.002), Plastic(Colors.White)));
            shadow.Children.Add(Model(Box(0, 0, 0.1, 0.04, 0.04, 0.04), Plastic(Colors.Gray)));
            px = Draw(shadow, top, Colors.Black);
            double inShadow = Luma(At(px, PixelX(0.05, 1), Size / 2)), inLight = Luma(At(px, PixelX(-0.1, 1), Size / 2));
            check("güneş gölgesi", inShadow < 0.6 * inLight, $"gölgede {inShadow:0}, ışıkta {inLight:0}");

            // 3) Light of its own over black: exactly its colour.
            var glow = new Model3DGroup();
            glow.Children.Add(Model(Box(0, 0, 0, 0.4, 0.4, 0.002), new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0, 128, 255)))));
            px = Draw(glow, top, Colors.Black);
            var gc = At(px, Size / 2, Size / 2);
            check("kendi ışığı eklenir", Near(gc, (0, 128, 255), 3), $"orta {gc}");

            // 4) A half see-through blue layer over a lit white floor: both show.
            var clear = new Model3DGroup();
            clear.Children.Add(Sun(new Vector3D(0, 0, -1)));
            clear.Children.Add(new AmbientLight(Color.FromRgb(0x40, 0x40, 0x40)));
            clear.Children.Add(Model(Box(0, 0, 0, 1, 1, 0.002), Plastic(Colors.White)));
            clear.Children.Add(Model(Box(0, 0, 0.05, 0.3, 0.3, 0.002), new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(0x80, 0x20, 0x40, 0xE0)))));
            px = Draw(clear, top, Colors.Black);
            var cc = At(px, Size / 2, Size / 2);
            check("yarı saydam katman", cc.B > cc.R + 20 && cc.R > 40, $"orta {cc}");

            // 5) A picture's orientation: red on the left half, green on the right.
            var bmp = new WriteableBitmap(2, 1, 96, 96, PixelFormats.Bgra32, null);
            bmp.WritePixels(new System.Windows.Int32Rect(0, 0, 2, 1), new byte[] { 0, 0, 255, 255, 0, 255, 0, 255 }, 8, 0);
            bmp.Freeze();
            var picture = new Model3DGroup();
            picture.Children.Add(Sun(new Vector3D(0, 0, -1)));
            picture.Children.Add(new AmbientLight(Color.FromRgb(0x40, 0x40, 0x40)));
            var quad = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new Point3D(-0.2, -0.2, 0), new Point3D(0.2, -0.2, 0), new Point3D(0.2, 0.2, 0), new Point3D(-0.2, 0.2, 0) },
                TextureCoordinates = new PointCollection { new System.Windows.Point(0, 1), new System.Windows.Point(1, 1), new System.Windows.Point(1, 0), new System.Windows.Point(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
            };
            picture.Children.Add(new GeometryModel3D(quad, new DiffuseMaterial(new ImageBrush(bmp))));
            px = Draw(picture, top, Colors.Black);
            var left = At(px, PixelX(-0.1, 1), Size / 2);
            var right = At(px, PixelX(0.1, 1), Size / 2);
            check("resmin yönü", left.R > 2 * left.G && right.G > 2 * right.R, $"sol {left}, sağ {right}");

            // 6) An orthographic camera (the modeller's side views) with WPF's infinite far plane, on a clear background:
            //    a red square 0.2 wide in a view 1 wide; around it the panel behind shows through (alpha 0).
            // (The sun slants: straight down, a glossy face seen straight down by an orthographic camera mirrors it everywhere.)
            var square = new Model3DGroup();
            square.Children.Add(Sun(new Vector3D(-0.3, 0.4, -0.85)));
            square.Children.Add(new AmbientLight(Color.FromRgb(0x40, 0x40, 0x40)));
            square.Children.Add(Model(Box(0, 0, 0, 0.2, 0.2, 0.01), Plastic(Color.FromRgb(0xD0, 0x20, 0x20))));
            var ortho = GpuRenderer.Camera.Ortho(new Point3D(0, 0, 5), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 1, 0.1, double.PositiveInfinity);
            px = Draw(square, ortho, Colors.Transparent);
            var mid = At(px, Size / 2, Size / 2);
            int inside = (int)(Size / 2 + 0.08 * Size), outside = (int)(Size / 2 + 0.15 * Size);
            check("dik kamera", mid.R > 90 && mid.R > 2 * mid.G && At(px, inside, Size / 2).R > 90 && Alpha(px, outside, Size / 2) == 0,
                $"orta {mid}, kenar içi {At(px, inside, Size / 2)}, dışı alfa {Alpha(px, outside, Size / 2)}");

            // 7) A perspective camera with WPF's infinite far plane still draws (depth near / distance).
            px = Draw(red, new GpuRenderer.Camera(new Point3D(0.4, -0.4, 0.3), new Vector3D(-0.4, 0.4, -0.3), new Vector3D(0, 0, 1), 40, 0.01, double.PositiveInfinity), Colors.Transparent);
            var far = At(px, Size / 2, Size / 2);
            check("sonsuz uzak düzlem", far.R > 90 && far.R > 2 * far.G && Alpha(px, 2, 2) == 0, $"orta {far}, köşe alfa {Alpha(px, 2, 2)}");

            target.Dispose();
        }

        // ================================================================ helpers

        private static Viewport3D View(Model3D content)
        {
            var view = new Viewport3D();
            view.Children.Add(new ModelVisual3D { Content = content });
            return view;
        }

        private static GeometryModel3D Model(MeshGeometry3D mesh, Material material) => new GeometryModel3D(mesh, material);

        private static MaterialGroup Layers(params Material[] layers)
        {
            var g = new MaterialGroup();
            foreach (var l in layers) g.Children.Add(l);
            return g;
        }

        private static Material Plastic(Color c) => Layers(new DiffuseMaterial(new SolidColorBrush(c)), new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), 30));

        private static DirectionalLight Sun(Vector3D direction) => new DirectionalLight(Color.FromRgb(0xC0, 0xC0, 0xC0), direction);

        /// <summary>A box centred at (x, y, z), its faces wound counter-clockwise seen from outside (WPF's front).</summary>
        private static MeshGeometry3D Box(double x, double y, double z, double sx, double sy, double sz)
        {
            var mesh = new MeshGeometry3D();
            var c = new Vector3D(x, y, z);
            void Face(Vector3D n, Vector3D u, Vector3D v)
            {
                int i = mesh.Positions.Count;
                foreach (var (a, b) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    mesh.Positions.Add((Point3D)(c + n + a * u + b * v));
                    mesh.Normals.Add(n);
                }

                foreach (int k in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i + k);
            }

            double hx = sx / 2, hy = sy / 2, hz = sz / 2;
            Face(new Vector3D(hx, 0, 0), new Vector3D(0, hy, 0), new Vector3D(0, 0, hz));
            Face(new Vector3D(-hx, 0, 0), new Vector3D(0, 0, hz), new Vector3D(0, hy, 0));
            Face(new Vector3D(0, hy, 0), new Vector3D(0, 0, hz), new Vector3D(hx, 0, 0));
            Face(new Vector3D(0, -hy, 0), new Vector3D(hx, 0, 0), new Vector3D(0, 0, hz));
            Face(new Vector3D(0, 0, hz), new Vector3D(hx, 0, 0), new Vector3D(0, hy, 0));
            Face(new Vector3D(0, 0, -hz), new Vector3D(0, hy, 0), new Vector3D(hx, 0, 0));
            // The normals were the half extents: make them unit.
            for (int i = 0; i < mesh.Normals.Count; i++)
            {
                var n = mesh.Normals[i];
                n.Normalize();
                mesh.Normals[i] = n;
            }

            return mesh;
        }

        /// <summary>The column where a point at x on the floor (z = 0) shows, for the camera straight above at <paramref name="height"/> (30° wide).</summary>
        private static int PixelX(double x, double height) => (int)Math.Round(Size / 2.0 + x / (height * Math.Tan(15 * Math.PI / 180)) * Size / 2);

        private static (int R, int G, int B) At(byte[] px, int x, int y)
        {
            int i = (y * Size + x) * 4;
            return (px[i + 2], px[i + 1], px[i]);
        }

        private static int Alpha(byte[] px, int x, int y) => px[(y * Size + x) * 4 + 3];

        private static bool Near((int R, int G, int B) c, (int R, int G, int B) want, int tolerance) =>
            Math.Abs(c.R - want.R) <= tolerance && Math.Abs(c.G - want.G) <= tolerance && Math.Abs(c.B - want.B) <= tolerance;

        private static double Luma((int R, int G, int B) c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        private static float Linear(byte v)
        {
            double c = v / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        }
    }
}
