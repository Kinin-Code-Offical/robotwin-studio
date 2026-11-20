using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sense] The "Oyun" (game) view next to the editor's scene view: the world seen from a chosen
    /// camera object (ideal render at its field of view and aspect, or the sensor's actual output with
    /// its pixels, noise and exposure) or from a chase camera behind the robot — no gizmos, grid
    /// highlights or selection, with a small HUD (time, frame rate, real-time factor).
    /// </summary>
    public sealed class GameView : Grid
    {
        private readonly Viewport3D _view = new Viewport3D { ClipToBounds = true, IsHitTestVisible = false };
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 60, NearPlaneDistance = 0.002, FarPlaneDistance = 150 };
        private readonly ModelVisual3D _environment = new ModelVisual3D();
        private readonly List<ModelVisual3D> _items = new List<ModelVisual3D>();
        private readonly Border _frame = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
        private readonly Grid _stage = new Grid();
        private readonly Image _sensorImage = new Image { Stretch = Stretch.Uniform, Visibility = Visibility.Collapsed };
        private readonly TextBlock _hud = new TextBlock { Margin = new Thickness(8), Padding = new Thickness(8, 4, 8, 4), FontSize = 12, Foreground = Brushes.White };
        private readonly Border _hudBox = new Border { Background = new SolidColorBrush(Color.FromArgb(0xB0, 0x0B, 0x0B, 0x0D)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(4), IsHitTestVisible = false, Margin = new Thickness(8) };
        private readonly ComboBox _source = new ComboBox { MinWidth = 220, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        private readonly CheckBox _sensorMode = new CheckBox { Content = "Sensör çıktısı (gerçek piksel)", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0xB8, 0xB8, 0xC0)) };
        private WriteableBitmap? _bitmap;
        private long _shownFrame = -1;
        private int _generation = -1;
        private Point3D _chasePos = new Point3D(-1, -1, 0.6);
        private Point3D _chaseLook;
        private DateTime _lastTick = DateTime.Now;
        private double _fps;
        private int _frames;
        private DateTime _fpsStart = DateTime.Now;
        private bool _hooked;

        public GameView()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x5C, 0x62));
            ClipToBounds = true;
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            RowDefinitions.Add(new RowDefinition());

            var bar = new Border { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2B)), BorderThickness = new Thickness(0, 0, 0, 1) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 8, 0) };
            row.Children.Add(new TextBlock { Text = "Oyun", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White });
            row.Children.Add(new TextBlock { Text = "Görüntü kaynağı:", Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromRgb(0x8C, 0x8C, 0x94)) });
            row.Children.Add(_source);
            row.Children.Add(_sensorMode);
            bar.Child = row;
            Children.Add(bar);

            _view.Camera = _camera;
            _view.Children.Add(_environment);
            _stage.Children.Add(_view);
            _stage.Children.Add(_sensorImage);
            RenderOptions.SetBitmapScalingMode(_sensorImage, BitmapScalingMode.NearestNeighbor);
            _frame.Child = _stage;
            _frame.Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x5C, 0x62));
            var body = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D)) };
            body.Children.Add(_frame);
            _hudBox.Child = _hud;
            body.Children.Add(_hudBox);
            SetRow(body, 1);
            Children.Add(body);
            body.SizeChanged += (_, _) => Layout();

            _source.SelectionChanged += (_, _) =>
            {
                _shownFrame = -1;
                Layout();
            };
            _sensorMode.Click += (_, _) =>
            {
                _shownFrame = -1;
                Layout();
            };
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible && !_hooked)
                {
                    CompositionTarget.Rendering += OnRendering;
                    _hooked = true;
                }
                else if (!IsVisible && _hooked)
                {
                    CompositionTarget.Rendering -= OnRendering;
                    _hooked = false;
                }
            };
        }

        public WorldScene? Scene { get; set; }
        public WorldSession? Session { get; set; }

        private sealed class Source
        {
            public string Label = string.Empty;
            public string? CameraId;
            public bool Top;
            public override string ToString() => Label;
        }

        /// <summary>Re-lists the camera objects (after the world changed).</summary>
        public void RefreshSources()
        {
            var world = Scene?.World;
            string? keep = (_source.SelectedItem as Source)?.CameraId ?? ((_source.SelectedItem as Source)?.Top == true ? "#top" : null);
            _source.Items.Clear();
            _source.Items.Add(new Source { Label = "Takip kamerası (robotun arkası)" });
            _source.Items.Add(new Source { Label = "Üstten takip", Top = true });
            if (world != null)
            {
                foreach (var o in world.Objects.Where(o => o.PartId == SensingParts.CameraId))
                {
                    _source.Items.Add(new Source { Label = "Kamera: " + o.Name, CameraId = o.Id });
                }
            }

            int index = 0;
            for (int i = 0; i < _source.Items.Count; i++)
            {
                var s = (Source)_source.Items[i];
                if ((keep == "#top" && s.Top) || (keep != null && s.CameraId == keep)) index = i;
            }

            // A world with a camera opens on it.
            if (keep == null && _source.Items.Count > 2) index = 2;
            _source.SelectedIndex = index;
        }

        /// <summary>Shows a camera object (or the chase camera for null).</summary>
        public void ShowCamera(string? cameraObjectId, bool sensorOutput)
        {
            RefreshSources();
            for (int i = 0; i < _source.Items.Count; i++)
            {
                if (((Source)_source.Items[i]).CameraId == cameraObjectId) _source.SelectedIndex = i;
            }

            _sensorMode.IsChecked = sensorOutput;
            _shownFrame = -1;
            Layout();
        }

        private Source? Current => _source.SelectedItem as Source;

        private (CameraSpec Spec, CameraFrameSize Size, double Fov)? CameraInfo(WorldObject o)
        {
            var lib = Scene?.Library ?? PartLibrary.Default;
            var def = o.PartId != null ? lib.Find(o.PartId) : null;
            if (def == null) return null;
            var preset = o.PresetId != null ? lib.FindPreset(o.PresetId) : null;
            var p = lib.ResolveParameters(def, preset, o.Parameters);
            var spec = CameraModels.Find(p.TryGetValue("model", out var m) ? m : null);
            var size = CameraModels.ParseFrameSize(p.TryGetValue("framesize", out var f) ? f : null, spec.DefaultFrameSize);
            double fov = p.TryGetValue("fov", out var fv) && double.TryParse(fv, NumberStyles.Float, CultureInfo.InvariantCulture, out double ff) && ff > 1 ? ff : spec.HorizontalFovDeg;
            return (spec, size, fov);
        }

        /// <summary>Letterboxes the picture to the camera's aspect ratio.</summary>
        private void Layout()
        {
            var body = (FrameworkElement)_frame.Parent;
            double w = Math.Max(10, body.ActualWidth), h = Math.Max(10, body.ActualHeight);
            double aspect = w / h;
            var src = Current;
            var o = src?.CameraId != null ? Scene?.World?.Find(src.CameraId) : null;
            if (o != null && CameraInfo(o) is { } info) aspect = info.Size.Width / (double)info.Size.Height;
            double fw = w, fh = w / aspect;
            if (fh > h)
            {
                fh = h;
                fw = h * aspect;
            }

            _frame.Width = fw;
            _frame.Height = fh;
            bool sensor = o != null && _sensorMode.IsChecked == true;
            _sensorImage.Visibility = sensor ? Visibility.Visible : Visibility.Collapsed;
            _view.Visibility = sensor ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var scene = Scene;
            if (scene?.World == null) return;
            if (_source.Items.Count == 0) RefreshSources();
            var now = DateTime.Now;
            double dt = Math.Min(0.1, (now - _lastTick).TotalSeconds);
            _lastTick = now;
            _frames++;
            if ((now - _fpsStart).TotalSeconds >= 0.5)
            {
                _fps = _frames / (now - _fpsStart).TotalSeconds;
                _frames = 0;
                _fpsStart = now;
            }

            var items = scene.LiveItems();
            if (_generation != scene.Generation || items.Count != _items.Count)
            {
                _generation = scene.Generation;
                _environment.Content = scene.EnvironmentModel;
                foreach (var v in _items) _view.Children.Remove(v);
                _items.Clear();
                foreach (var it in items)
                {
                    var v = new ModelVisual3D { Content = it.Content, Transform = new MatrixTransform3D() };
                    _items.Add(v);
                    _view.Children.Add(v);
                }

                RefreshSources();
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (!ReferenceEquals(_items[i].Content, items[i].Content)) _items[i].Content = items[i].Content;
                ((MatrixTransform3D)_items[i].Transform).Matrix = items[i].Matrix;
            }

            var src = Current;
            var world = scene.World;
            string cameraLine = string.Empty;
            var cam = src?.CameraId != null ? world.Find(src.CameraId) : null;
            if (cam != null && CameraInfo(cam) is { } info)
            {
                var m = items.FirstOrDefault(x => x.Id == cam.Id).Matrix;
                var unit = Session?.Sensing?.Cameras.FirstOrDefault(c => c.Object.Id == cam.Id);
                var lens = SensingParts.CameraLens(info.Spec.Id);
                var p = m.Transform(new Point3D(lens.X, lens.Y, lens.Z));
                var fwd = m.Transform(new Vector3D(1, 0, 0));
                var up = m.Transform(new Vector3D(0, 0, 1));
                _camera.Position = p + fwd * 0.001;
                _camera.LookDirection = fwd;
                _camera.UpDirection = up;
                _camera.FieldOfView = unit?.HorizontalFovDeg ?? info.Fov;
                if (unit?.Latest is { } frame)
                {
                    cameraLine = $"\n{info.Spec.Name} · {frame.Width}×{frame.Height} {frame.Format} · {frame.AchievedFps:0.#}/{frame.TargetFps:0.#} fps · pozlama {frame.ExposureMs:0.##} ms · kazanç {frame.Gain:0.0}×";
                    if (_sensorMode.IsChecked == true && frame.FrameIndex != _shownFrame) ShowFrame(frame);
                }
                else
                {
                    cameraLine = $"\n{info.Spec.Name} · {info.Size.Width}×{info.Size.Height} · {info.Spec.SensorFps(info.Size.Width, info.Size.Height):0} fps" +
                                 (Session?.Sensing == null ? " · simülasyon çalışınca sensör görüntüsü gelir" : string.Empty);
                }
            }
            else
            {
                Chase(items, world, src?.Top == true, dt);
            }

            double t = Session?.Time ?? 0;
            string state = Session == null || Session.State == Simulation.SimState.Stopped ? "durdu" : Session.State == Simulation.SimState.Paused ? "duraklatıldı" : "çalışıyor";
            _hud.Text = $"t = {t:0.000} s · {state}\nekran {_fps:0} fps · gerçek zaman ×{(Session?.RealtimeFactor ?? 0):0.00}" + cameraLine;
        }

        private void ShowFrame(CameraReading frame)
        {
            _shownFrame = frame.FrameIndex;
            if (_bitmap == null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
            {
                _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgr32, null);
                _sensorImage.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.Bgra, frame.Width * 4, 0);
        }

        /// <summary>Chase camera behind (or above) the selected or first moving object.</summary>
        private void Chase(List<(string Id, Model3D Content, Matrix3D Matrix)> items, WorldDocument world, bool top, double dt)
        {
            string? id = Scene?.Selected;
            var target = id != null ? world.Find(id) : null;
            if (target == null || target.IsStatic) target = world.Objects.FirstOrDefault(o => !o.IsStatic && o.Kind == WorldObjectKind.Body) ?? world.Objects.FirstOrDefault(o => !o.IsStatic);
            if (target == null) return;
            var m = items.FirstOrDefault(x => x.Id == target.Id).Matrix;
            var pos = m.Transform(new Point3D(0, 0, 0));
            var fwd = m.Transform(new Vector3D(1, 0, 0));
            fwd.Z = 0;
            if (fwd.Length < 1e-6) fwd = new Vector3D(1, 0, 0);
            fwd.Normalize();
            var desired = top ? pos + new Vector3D(-0.001, 0, 1.2) : pos - fwd * 0.45 + new Vector3D(0, 0, 0.22);
            double k = 1 - Math.Exp(-dt * 4);
            _chasePos = new Point3D(_chasePos.X + (desired.X - _chasePos.X) * k, _chasePos.Y + (desired.Y - _chasePos.Y) * k, _chasePos.Z + (desired.Z - _chasePos.Z) * k);
            var look = top ? pos : pos + fwd * 0.15;
            _chaseLook = new Point3D(_chaseLook.X + (look.X - _chaseLook.X) * k, _chaseLook.Y + (look.Y - _chaseLook.Y) * k, _chaseLook.Z + (look.Z - _chaseLook.Z) * k);
            _camera.Position = _chasePos;
            _camera.LookDirection = _chaseLook - _chasePos;
            _camera.UpDirection = top ? fwd : new Vector3D(0, 0, 1);
            _camera.FieldOfView = 60;
        }
    }
}
