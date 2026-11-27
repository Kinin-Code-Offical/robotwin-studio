using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Render
{
    /// <summary>
    /// [render] Shows a WPF 3D view drawn by our GPU renderer. It reads the view's models and camera each frame
    /// (<see cref="Draw"/>), draws them on the GPU and shows the picture through a D3DImage; the WPF view stays where it
    /// is, unseen, so picking, dragging and every scene builder work on it as before.
    /// </summary>
    internal sealed class GpuViewport : Image
    {
        private readonly D3DImage _image = new D3DImage();
        private readonly SceneMirror _mirror = new SceneMirror();
        private readonly Viewport3D _source;
        private GpuRenderer.Target? _target;
        private bool _setBuffer = true;

        public GpuViewport(Viewport3D source)
        {
            _source = source;
            Source = _image;
            Stretch = Stretch.Fill;
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
            // After the screen was locked or the display changed, the surface must be handed over again.
            _image.IsFrontBufferAvailableChanged += (_, _) => _setBuffer = true;
        }

        /// <summary>The colour behind everything.</summary>
        public Color Clear { get; set; } = Colors.Transparent;

        /// <summary>How far ahead the view's camera looks (its orbit distance), when the view knows; the sun's shadow is sharpest there.</summary>
        public Func<double>? FocusDistance { get; set; }

        /// <summary>Things drawn last frame (for the performance overlay).</summary>
        public int LastItems { get; private set; }

        /// <summary>Raised once when the GPU stopped working (the device was lost for good): the view should go back to WPF.</summary>
        public event Action<string>? Failed;

        /// <summary>Draws the view now; false when it could not (hidden, no size, no GPU).</summary>
        public bool Draw()
        {
            if (!IsVisible || !_image.IsFrontBufferAvailable) return false;
            if (_source.Camera is not ProjectionCamera camera || camera is not (PerspectiveCamera or OrthographicCamera)) return false;
            if (!_source.IsVisible) return false;
            // The size of the view it stands in for (an Image takes its size from its picture, which has none at first).
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = (int)Math.Ceiling(_source.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(_source.ActualHeight * dpi.DpiScaleY);
            if (w < 2 || h < 2) return false;
            var window = Window.GetWindow(this);
            if (window == null) return false;
            var renderer = GpuRenderer.Get(new WindowInteropHelper(window).EnsureHandle());
            if (renderer == null)
            {
                Failed?.Invoke(GpuRenderer.Failure ?? "GPU");
                return false;
            }

            _target ??= renderer.CreateTarget();
            using (Shell.Perf.Measure("3B sahne okuma")) _mirror.Read(_source);
            LastItems = _mirror.Items.Count;
            using var timing = Shell.Perf.Measure("GPU çizim");
            _image.Lock();
            try
            {
                bool changed = _target.Resize(w, h);
                var cam = Camera(camera);
                if (FocusDistance?.Invoke() is double focus && focus > 0) cam = cam with { Focus = (float)focus };
                renderer.Render(_target, _mirror, cam, Clear);
                if (changed || _setBuffer)
                {
                    // Software fallback on: a remote desktop or a picture of the window (RenderTargetBitmap) still shows it.
                    _image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _target.Surface, true);
                    _setBuffer = false;
                }

                _image.AddDirtyRect(new Int32Rect(0, 0, _target.Width, _target.Height));
                return true;
            }
            catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException || ex is COMException || ex is InvalidOperationException)
            {
                _target.Dispose();
                _target = null;
                Failed?.Invoke(ex.Message);
                return false;
            }
            finally
            {
                _image.Unlock();
            }
        }

        private static GpuRenderer.Camera Camera(ProjectionCamera c)
        {
            var position = c.Position;
            var look = c.LookDirection;
            var up = c.UpDirection;
            if (c.Transform is { } t && !t.Value.IsIdentity)
            {
                var m = t.Value;
                position = m.Transform(position);
                look = m.Transform(look);
                up = m.Transform(up);
            }

            return c is OrthographicCamera o
                ? GpuRenderer.Camera.Ortho(position, look, up, o.Width, o.NearPlaneDistance, o.FarPlaneDistance)
                : new GpuRenderer.Camera(position, look, up, ((PerspectiveCamera)c).FieldOfView, c.NearPlaneDistance, c.FarPlaneDistance);
        }
    }
}
