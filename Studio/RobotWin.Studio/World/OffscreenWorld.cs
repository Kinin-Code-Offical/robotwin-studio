using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sense] A copy of the world scene that is not on screen: the same 3D models (shared, not
    /// duplicated) placed with their current matrices, rendered from an arbitrary camera into a
    /// bitmap of an exact pixel size. Used by the camera sensors.
    /// </summary>
    internal sealed class OffscreenWorld
    {
        private readonly ContainerVisual _root = new ContainerVisual();
        private readonly DrawingVisual _background = new DrawingVisual();
        private readonly Viewport3DVisual _viewport = new Viewport3DVisual();
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { NearPlaneDistance = 0.002, FarPlaneDistance = 120 };
        private readonly ModelVisual3D _environment = new ModelVisual3D();
        private readonly List<ModelVisual3D> _items = new List<ModelVisual3D>();
        private int _generation = -1;
        private Size _bgSize;

        public OffscreenWorld()
        {
            _viewport.Camera = _camera;
            _viewport.Children.Add(_environment);
            _root.Children.Add(_background);
            _root.Children.Add(_viewport);
        }

        /// <summary>Colour behind everything (the room beyond the floor).</summary>
        public Color Background { get; set; } = Color.FromRgb(0x5A, 0x5C, 0x62);

        /// <summary>Re-links the models after the scene was rebuilt, then updates the matrices.</summary>
        public void Sync(Model3D? environment, IReadOnlyList<(Model3D Content, Matrix3D Matrix)> items, int generation)
        {
            if (generation != _generation || items.Count != _items.Count)
            {
                _generation = generation;
                _environment.Content = environment;
                foreach (var v in _items) _viewport.Children.Remove(v);
                _items.Clear();
                foreach (var (content, _) in items)
                {
                    var v = new ModelVisual3D { Content = content, Transform = new MatrixTransform3D() };
                    _items.Add(v);
                    _viewport.Children.Add(v);
                }
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (!ReferenceEquals(_items[i].Content, items[i].Content)) _items[i].Content = items[i].Content;
                ((MatrixTransform3D)_items[i].Transform).Matrix = items[i].Matrix;
            }
        }

        /// <summary>Renders BGRA32 pixels (premultiplied, opaque) seen from a pinhole camera.</summary>
        public byte[] Render(int width, int height, Point3D position, Vector3D forward, Vector3D up, double hfovDeg)
        {
            _camera.Position = position;
            _camera.LookDirection = forward;
            _camera.UpDirection = up;
            _camera.FieldOfView = hfovDeg;
            _viewport.Viewport = new Rect(0, 0, width, height);
            var size = new Size(width, height);
            if (size != _bgSize)
            {
                _bgSize = size;
                using var dc = _background.RenderOpen();
                dc.DrawRectangle(new SolidColorBrush(Background), null, new Rect(size));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(_root);
            var px = new byte[width * height * 4];
            rtb.CopyPixels(px, width * 4, 0);
            return px;
        }

        /// <summary>JPEG encoder for the ESP32 camera driver (esp32-camera quality 0–63, lower is better).</summary>
        public static byte[] EncodeJpeg(byte[] bgra, int width, int height, int quality)
        {
            var src = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, bgra, width * 4);
            var enc = new JpegBitmapEncoder { QualityLevel = Math.Max(5, Math.Min(100, 100 - quality * 1.4 > 0 ? (int)(100 - quality * 1.4) : 5)) };
            enc.Frames.Add(BitmapFrame.Create(src));
            using var ms = new System.IO.MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
    }
}
