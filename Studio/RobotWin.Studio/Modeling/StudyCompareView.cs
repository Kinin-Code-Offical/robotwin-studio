using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// [part-study] A second view of the part beside the editor's: another study result (the previous solve) on the
    /// same camera and the same colour scale, so two results are seen side by side. It only shows: the camera is the
    /// editor view's own, and orbiting there turns both.
    /// </summary>
    internal sealed class StudyCompareView : Grid
    {
        private readonly Viewport3D _view = new Viewport3D { ClipToBounds = true, IsHitTestVisible = false };
        private readonly PerspectiveCamera _persp = new PerspectiveCamera();
        private readonly OrthographicCamera _ortho = new OrthographicCamera();
        private readonly DirectionalLight _key = new DirectionalLight(Color.FromRgb(0xC8, 0xC8, 0xCC), new Vector3D(0, 0, -1));
        private readonly DirectionalLight _fill = new DirectionalLight(Color.FromRgb(0x4A, 0x4C, 0x55), new Vector3D(0, 0, 1));
        private readonly ModelVisual3D _model = new ModelVisual3D();
        private readonly TextBlock _label = new TextBlock { Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        private readonly ViewCamera _cam;

        public StudyCompareView(ViewCamera cam)
        {
            _cam = cam;
            Background = new SolidColorBrush(ModelViewport.BackColor);
            ClipToBounds = true;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(0x3C, 0x3D, 0x44)));
            lights.Children.Add(_key);
            lights.Children.Add(_fill);
            _view.Children.Add(new ModelVisual3D { Content = lights });
            _view.Children.Add(_model);
            _view.Camera = _persp;
            Children.Add(_view);
            Render.GpuHost.Attach(this, _view, focus: () => _cam.Ortho ? _cam.Distance * 20 : _cam.Distance);
            Children.Add(Caption(_label, VerticalAlignment.Top));
            var divider = new Rectangle { Width = 1, HorizontalAlignment = HorizontalAlignment.Left };
            divider.SetResourceReference(Shape.FillProperty, "B.Border");
            Children.Add(divider);
            SizeChanged += (_, _) => Sync();
        }

        /// <summary>A line naming what a view shows, on the 3D view's dark overlay (white text on it in every theme).</summary>
        internal static Border Caption(TextBlock? text, VerticalAlignment where) => new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x10, 0x10, 0x14)),
            Child = text ?? new TextBlock { Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap },
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8),
            MaxWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = where,
            IsHitTestVisible = false,
        };

        /// <summary>The result to show (the part's frame, mm) placed as the part is, and the line naming it.</summary>
        public void Show(Model3D? model, Transform3D placement, string label)
        {
            if (model != null) model.Transform = placement;
            _model.Content = model;
            _label.Text = label;
            Sync();
        }

        /// <summary>Follows the editor view's camera (called whenever it moves).</summary>
        public void Sync()
        {
            var f = _cam.Forward;
            var up = _cam.Up;
            var pos = _cam.Position;
            if (_cam.Ortho)
            {
                var far = pos - f * _cam.Distance * 20;
                _ortho.Position = new Point3D(far.X, far.Y, far.Z);
                _ortho.LookDirection = new Vector3D(f.X, f.Y, f.Z);
                _ortho.UpDirection = new Vector3D(up.X, up.Y, up.Z);
                _ortho.Width = _cam.OrthoWidth;
                _ortho.NearPlaneDistance = _cam.Distance * 0.5;
                _ortho.FarPlaneDistance = _cam.Distance * 60;
                if (_view.Camera != _ortho) _view.Camera = _ortho;
            }
            else
            {
                _persp.Position = new Point3D(pos.X, pos.Y, pos.Z);
                _persp.LookDirection = new Vector3D(f.X, f.Y, f.Z);
                _persp.UpDirection = new Vector3D(up.X, up.Y, up.Z);
                _persp.FieldOfView = _cam.FieldOfView;
                _persp.NearPlaneDistance = Math.Max(0.01, _cam.Distance * 0.01);
                _persp.FarPlaneDistance = _cam.Distance * 60 + 2000;
                if (_view.Camera != _persp) _view.Camera = _persp;
            }

            var k = (f + _cam.Right * 0.55 - up * 0.75).Normalized();
            _key.Direction = new Vector3D(k.X, k.Y, k.Z);
            var fl = (f - _cam.Right * 0.6 + up * 0.5).Normalized();
            _fill.Direction = new Vector3D(fl.X, fl.Y, fl.Z);
        }
    }
}
