using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [thermal] Thermal camera view: the parts' own 3D models are coloured by temperature, point by
    /// point (a board's heat map across the PCB and its parts, a lumped part in its node's colour),
    /// in the "iron" palette a thermal camera uses. <see cref="SurfacePointAt"/> gives the object and
    /// the point under the mouse so the exact temperature can be shown on hover.
    /// </summary>
    public sealed partial class WorldScene
    {
        private sealed class ThermalSurface
        {
            public string ObjectId = string.Empty;
            public GeometryModel3D Model = null!;
            public Geometry3D? Geometry;
            public Material? Material, BackMaterial;
            public MeshGeometry3D Mesh = null!;
            public Point3D[] Local = Array.Empty<Point3D>();   // vertices in the object's frame
            public Transform3D ToObject = Transform3D.Identity;
        }

        private readonly Dictionary<GeometryModel3D, ThermalSurface> _thermalSurfaces = new Dictionary<GeometryModel3D, ThermalSurface>();
        private static ImageBrush? _ironBrush;

        /// <summary>True while the thermal camera colours the models.</summary>
        public bool ThermalCamera => _thermalSurfaces.Count > 0;

        /// <summary>
        /// Thermal-camera "iron" palette (black → purple → red → orange → yellow → white), monotonic in
        /// brightness so it still reads in grey and for most colour-vision deficiencies.
        /// </summary>
        public static Color Iron(double t)
        {
            t = double.IsFinite(t) ? Math.Clamp(t, 0, 1) : 0;
            (double P, byte R, byte G, byte B)[] stops =
            {
                (0.00, 0x00, 0x00, 0x0A), (0.15, 0x20, 0x00, 0x5A), (0.35, 0x8C, 0x00, 0x8C), (0.55, 0xDC, 0x28, 0x2D),
                (0.72, 0xF5, 0x78, 0x00), (0.88, 0xFF, 0xC8, 0x14), (1.00, 0xFF, 0xFF, 0xF0)
            };
            for (int i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].P) continue;
                var (p0, r0, g0, b0) = stops[i - 1];
                var (p1, r1, g1, b1) = stops[i];
                double k = (t - p0) / (p1 - p0);
                return Color.FromRgb((byte)(r0 + (r1 - r0) * k), (byte)(g0 + (g1 - g0) * k), (byte)(b0 + (b1 - b0) * k));
            }

            return Color.FromRgb(0xFF, 0xFF, 0xF0);
        }

        private static ImageBrush IronBrush()
        {
            if (_ironBrush != null) return _ironBrush;
            const int n = 256;
            var px = new byte[n * 4];
            for (int i = 0; i < n; i++)
            {
                var c = Iron(i / (double)(n - 1));
                px[i * 4] = c.B;
                px[i * 4 + 1] = c.G;
                px[i * 4 + 2] = c.R;
                px[i * 4 + 3] = 255;
            }

            var bmp = BitmapSource.Create(n, 1, 96, 96, PixelFormats.Bgra32, null, px, n * 4);
            bmp.Freeze();
            _ironBrush = new ImageBrush(bmp) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1), Stretch = Stretch.Fill };
            _ironBrush.Freeze();
            return _ironBrush;
        }

        /// <summary>
        /// Colours every model by <paramref name="temperatureAt"/> (object id, point in the object's
        /// frame → °C, or null to leave that object as it is), mapped from <paramref name="low"/> (black)
        /// to <paramref name="high"/> (white). Pass null to give the models their own materials back.
        /// </summary>
        public void SetThermalCamera(Func<string, Point3D, double?>? temperatureAt, double low, double high)
        {
            if (temperatureAt == null)
            {
                foreach (var s in _thermalSurfaces.Values)
                {
                    s.Model.Geometry = s.Geometry;
                    s.Model.Material = s.Material;
                    s.Model.BackMaterial = s.BackMaterial;
                }

                _thermalSurfaces.Clear();
                return;
            }

            if (_thermalSurfaces.Count == 0)
                foreach (var it in _items.Values)
                    Collect(it.Object.Id, it.Visual.Content, Transform3D.Identity);
            double span = Math.Max(1e-6, high - low);
            var brush = IronBrush();
            var mat = new MaterialGroup { Children = { new DiffuseMaterial(brush), new EmissiveMaterial(brush) { Color = Color.FromRgb(0x60, 0x60, 0x60) } } };
            mat.Freeze();
            foreach (var s in _thermalSurfaces.Values)
            {
                var uv = new PointCollection(s.Local.Length);
                bool any = false;
                foreach (var p in s.Local)
                {
                    double? t = temperatureAt(s.ObjectId, p);
                    if (t is double v && double.IsFinite(v)) any = true;
                    double k = t is double tv && double.IsFinite(tv) ? (tv - low) / span : 0;
                    uv.Add(new Point(Math.Clamp(k, 0.002, 0.998), 0.5));
                }

                if (!any)
                {
                    s.Model.Geometry = s.Geometry;
                    s.Model.Material = s.Material;
                    s.Model.BackMaterial = s.BackMaterial;
                    continue;
                }

                uv.Freeze();
                s.Mesh.TextureCoordinates = uv;
                s.Model.Geometry = s.Mesh;
                s.Model.Material = mat;
                s.Model.BackMaterial = mat;
            }
        }

        /// <summary>Remembers each mesh of an object with its vertices in the object's frame.</summary>
        private void Collect(string objectId, Model3D? model, Transform3D parent)
        {
            switch (model)
            {
                case Model3DGroup g:
                    var gt = Combine(g.Transform, parent);
                    foreach (var c in g.Children) Collect(objectId, c, gt);
                    break;
                case GeometryModel3D gm when gm.Geometry is MeshGeometry3D mesh && !gm.IsFrozen && !_thermalSurfaces.ContainsKey(gm):
                    var tf = Combine(gm.Transform, parent);
                    var local = new Point3D[mesh.Positions.Count];
                    for (int i = 0; i < local.Length; i++) local[i] = tf.Transform(mesh.Positions[i]);
                    var copy = new MeshGeometry3D { Positions = mesh.Positions, TriangleIndices = mesh.TriangleIndices, Normals = mesh.Normals };
                    _thermalSurfaces[gm] = new ThermalSurface
                    {
                        ObjectId = objectId, Model = gm, Geometry = gm.Geometry, Material = gm.Material, BackMaterial = gm.BackMaterial,
                        Mesh = copy, Local = local, ToObject = tf
                    };
                    break;
            }
        }

        private static Transform3D Combine(Transform3D? inner, Transform3D outer)
        {
            if (inner == null || inner.Value.IsIdentity) return outer;
            if (outer.Value.IsIdentity) return inner;
            var m = inner.Value;
            m.Append(outer.Value);
            return new MatrixTransform3D(m);
        }

        /// <summary>The object and the point on its model (object frame) under the screen point, or null.</summary>
        public (string ObjectId, Point3D Local)? SurfacePointAt(Point screen)
        {
            (string, Point3D)? found = null;
            VisualTreeHelper.HitTest(_view, PickFilter, r =>
            {
                if (r is not RayMeshGeometry3DHitTestResult hit) return HitTestResultBehavior.Continue;
                foreach (var it in _items.Values)
                {
                    if (it.Visual != hit.VisualHit) continue;
                    var p = hit.PointHit;
                    if (hit.ModelHit is GeometryModel3D gm && _thermalSurfaces.TryGetValue(gm, out var s))
                    {
                        // From the hit triangle's own vertices: unambiguous whatever space PointHit is in.
                        p = (Point3D)(hit.VertexWeight1 * (Vector3D)s.Local[hit.VertexIndex1] + hit.VertexWeight2 * (Vector3D)s.Local[hit.VertexIndex2] + hit.VertexWeight3 * (Vector3D)s.Local[hit.VertexIndex3]);
                    }

                    found = (it.Object.Id, p);
                    return HitTestResultBehavior.Stop;
                }

                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(screen));
            return found;
        }

        /// <summary>The 3D view itself (for mouse handling by owners such as the render window).</summary>
        public FrameworkElement View3D => _view;

        /// <summary>Text for the part under the mouse while the thermal camera is on (object id, point in its frame), or null.</summary>
        public Func<string, Point3D, string?>? ThermalHoverText { get; set; }

        private System.Windows.Controls.Border? _thermalHoverBox;
        private readonly System.Windows.Controls.TextBlock _thermalHover = new System.Windows.Controls.TextBlock { Foreground = Brushes.White, FontSize = 12.5, Margin = new Thickness(7, 4, 7, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };

        private void UpdateThermalHover(Point p)
        {
            if (!ThermalCamera || ThermalHoverText == null || SurfacePointAt(p) is not { } hit || ThermalHoverText(hit.ObjectId, hit.Local) is not { } text)
            {
                if (_thermalHoverBox != null) _thermalHoverBox.Visibility = Visibility.Collapsed;
                return;
            }

            if (_thermalHoverBox == null)
            {
                _thermalHoverBox = new System.Windows.Controls.Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x10, 0x10, 0x14)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x37)),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Child = _thermalHover,
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false
                };
                Children.Add(_thermalHoverBox);
            }

            _thermalHover.Text = text;
            _thermalHoverBox.Margin = new Thickness(p.X + 16, p.Y + 12, 0, 0);
            _thermalHoverBox.Visibility = Visibility.Visible;
        }
    }
}
