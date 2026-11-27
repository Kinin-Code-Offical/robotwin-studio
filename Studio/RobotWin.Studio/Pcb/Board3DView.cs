using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Assembly3D;
using RobotWin.Studio.Models3D;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// 3D view of the circuit board: FR4 slab with solder mask, copper, pads and
    /// silkscreen from the layout, and a detailed model of every part from the shared
    /// <see cref="ComponentModels"/> library. While the simulation runs LEDs light up,
    /// displays show their content, hot parts glow and burnt parts char and smoke.
    /// Units are millimetres, Z up, board centred on the origin.
    /// </summary>
    public sealed class Board3DView : Grid
    {
        private sealed class PartVisual3D
        {
            public string RefDes = string.Empty;
            public Point3D Center;
            public ComponentInstance Instance = null!;
            public Matrix3D ToBoard = Matrix3D.Identity;   // [crossprobe] the part's own frame → the board's
            public bool Charred;
            public double Brightness = -1;
        }

        private readonly Viewport3D _view = new Viewport3D { ClipToBounds = true };
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 40, NearPlaneDistance = 1, FarPlaneDistance = 5000 };
        private readonly ModelVisual3D _content = new ModelVisual3D();
        private readonly EffectsLayer _effects = new EffectsLayer();
        private readonly List<PartVisual3D> _parts = new List<PartVisual3D>();
        private readonly Dictionary<string, double> _failedAt = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private double _yaw = -1.9, _pitch = 0.75, _distance = 150;
        private Point3D _target;
        private Point? _drag;
        private bool _pan;
        private double _boardSpan = 100;
        private DateTime _last = DateTime.Now;

        private readonly ModelVisual3D _highlight = new ModelVisual3D();
        private string? _highlighted;

        /// <summary>
        /// [crossprobe] The part picked in the circuit or on the PCB, framed in the accent colour on the 3D board (the
        /// roadmap's "kartın üstündeki parçanın 3B kartta vurgusu"); null clears it. Kept across rebuilds.
        /// </summary>
        public void Highlight(string? refDes)
        {
            _highlighted = refDes;
            var part = refDes == null ? null : _parts.FirstOrDefault(p => string.Equals(p.RefDes, refDes, StringComparison.OrdinalIgnoreCase));
            var b = part == null ? Rect3D.Empty : new MatrixTransform3D(part.ToBoard).TransformBounds(part.Instance.Root.Bounds);
            if (part == null || b.IsEmpty)
            {
                _highlight.Content = null;
                return;
            }

            double pad = Math.Max(0.4, Math.Max(b.SizeX, b.SizeY) * 0.06);
            b = new Rect3D(b.X - pad, b.Y - pad, b.Z - pad * 0.5, b.SizeX + 2 * pad, b.SizeY + 2 * pad, b.SizeZ + pad);
            double r = Math.Max(0.12, _boardSpan * 0.0018);
            var accent = new MaterialGroup();
            accent.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF))));
            accent.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF))));
            accent.Freeze();
            var p = new Point3D[8];
            for (int i = 0; i < 8; i++) p[i] = new Point3D(b.X + ((i & 1) != 0 ? b.SizeX : 0), b.Y + ((i & 2) != 0 ? b.SizeY : 0), b.Z + ((i & 4) != 0 ? b.SizeZ : 0));
            int[,] e = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            var g = new Model3DGroup();
            for (int i = 0; i < 12; i++)
                g.Children.Add(new GeometryModel3D(World.WorldScene.Tube(new List<Point3D> { p[e[i, 0]], p[e[i, 1]] }, r, 4), accent));
            g.Freeze();
            _highlight.Content = g;
        }

        public Board3DView()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D));
            ClipToBounds = true;
            _view.Camera = _camera;
            _view.Children.Add(_content);
            _view.Children.Add(_highlight);   // [crossprobe] the picked part's frame
            Children.Add(_view);
            _gpu = Render.GpuHost.Attach(this, _view, auto: false, focus: () => _distance);   // [render] our GPU renderer draws the view
            _effects.Project = Project;
            Children.Add(_effects);
            Children.Add(new TextBlock
            {
                Text = "Sol tuş: döndür · Sağ tuş: kaydır · Teker: yakınlaştır · Çift tık: üstten",
                Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x97)),
                FontSize = 12,
                Margin = new Thickness(14, 0, 0, 10),
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Left,
                IsHitTestVisible = false
            });

            MouseDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    _yaw = -Math.PI / 2;
                    _pitch = 1.45;
                    UpdateCamera();
                    return;
                }

                _drag = e.GetPosition(this);
                _pan = e.ChangedButton != MouseButton.Left;
                CaptureMouse();
            };
            MouseUp += (_, _) =>
            {
                _drag = null;
                ReleaseMouseCapture();
            };
            MouseMove += (_, e) =>
            {
                if (_drag == null) return;
                var p = e.GetPosition(this);
                var d = p - _drag.Value;
                _drag = p;
                if (_pan)
                {
                    var right = Vector3D.CrossProduct(_camera.LookDirection, new Vector3D(0, 0, 1));
                    right.Normalize();
                    var fwd = new Vector3D(_camera.LookDirection.X, _camera.LookDirection.Y, 0);
                    if (fwd.Length > 1e-6) fwd.Normalize();
                    double k = _distance * 0.0016;
                    _target += -right * d.X * k + fwd * d.Y * k;
                }
                else
                {
                    _yaw -= d.X * 0.008;
                    _pitch = Math.Max(-1.5, Math.Min(1.52, _pitch + d.Y * 0.006));
                }

                UpdateCamera();
            };
            MouseWheel += (_, e) =>
            {
                _distance = Math.Max(10, Math.Min(_boardSpan * 6, _distance * Math.Pow(0.999, e.Delta)));
                UpdateCamera();
            };
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible) CompositionTarget.Rendering += OnFrame;
                else CompositionTarget.Rendering -= OnFrame;
            };
        }

        // ================================================================ scene

        public void Build(BoardDocument board, Schematic schematic, PartLibrary library)
        {
            _parts.Clear();
            _failedAt.Clear();
            _effects.Clear();
            var root = new Model3DGroup();
            root.Children.Add(new AmbientLight(Color.FromRgb(0x5C, 0x5C, 0x62)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0xE4, 0xE2, 0xDC), new Vector3D(-0.5, 0.7, -1)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0x58, 0x5C, 0x68), new Vector3D(0.6, -0.4, 0.4)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x36), new Vector3D(0.2, 0.3, 1)));

            // Every part comes from the shared component-model library (cached, frozen geometry).
            var scene = BoardModels.BuildScene(board, schematic, library);
            root.Children.Add(scene.Root);
            foreach (var pc in scene.Parts)
            {
                var c = pc.Centre;
                _parts.Add(new PartVisual3D
                {
                    RefDes = pc.RefDes,
                    Instance = pc.Instance,
                    ToBoard = pc.ToBoard,
                    Center = new Point3D(c.X / 1000, c.Y / 1000, c.Z / 1000)
                });
            }

            _content.Content = root;
            _boardSpan = Math.Max(board.Width, board.Height);
            Highlight(_highlighted);   // [crossprobe] the part picked before the rebuild
            _distance = _boardSpan * 1.7;
            _target = new Point3D(0, 0, 0);
            UpdateCamera();
        }

        // ================================================================ live state

        private readonly Render.GpuHost _gpu;
        private TimeSpan _lastRendering = TimeSpan.MinValue;

        private void OnFrame(object? sender, EventArgs e)
        {
            // Once per frame shown; drawn on the GPU after the frame moved everything.
            if (e is RenderingEventArgs re)
            {
                if (re.RenderingTime == _lastRendering) return;
                _lastRendering = re.RenderingTime;
            }

            try
            {
                Frame();
            }
            finally
            {
                _gpu.Draw();
            }
        }

        private void Frame()
        {
            var now = DateTime.Now;
            double dt = Math.Min(0.05, (now - _last).TotalSeconds);
            _last = now;
            var sim = Shell.Workspace.Current.Simulation;
            foreach (var v in _parts)
            {
                var devices = sim.Compiled?.DevicesOf(v.RefDes);
                double temp = 25;
                bool failed = false;
                double brightness = 0;
                if (devices != null && sim.Simulator != null)
                {
                    foreach (var d in devices)
                    {
                        if (sim.Simulator.Signals.TryGetValue(d.Id + ":T", out var tv)) temp = Math.Max(temp, tv);
                        failed |= d.IsFailed;
                        if (d is Led led) brightness = Math.Max(brightness, led.Brightness);
                        if (d is Lamp lamp && !lamp.IsFailed) brightness = Math.Max(brightness, lamp.Brightness);
                    }
                }

                var inst = v.Instance;
                inst.SetHeat(AssemblyScene.HeatColor(temp));
                if (inst.Light != null && Math.Abs(brightness - v.Brightness) > 0.004)
                {
                    v.Brightness = brightness;
                    inst.SetBrightness(Math.Min(1, brightness));
                }

                if (inst.Screen != null) UpdateScreen(inst, sim.I2cDeviceOf(v.RefDes));

                if (failed && !v.Charred)
                {
                    v.Charred = true;
                    inst.SetCharred(true);
                    _failedAt[v.RefDes] = sim.Time;
                    _effects.Burst(v.Center, v.RefDes.StartsWith("C", StringComparison.OrdinalIgnoreCase) || v.RefDes.StartsWith("BT", StringComparison.OrdinalIgnoreCase));
                }
                else if (!failed && v.Charred)
                {
                    v.Charred = false;
                    inst.SetCharred(false);
                }

                if (temp > 105) _effects.EmitSmoke(v.Center, Math.Min(30, (temp - 105) / 4), dt);
                if (v.Charred && _failedAt.TryGetValue(v.RefDes, out var at) && sim.Time - at < 8) _effects.EmitSmoke(v.Center, 15, dt, true);
            }
            _effects.Step(dt);
        }

        /// <summary>Live OLED / LCD picture on a display module's screen.</summary>
        private static void UpdateScreen(ComponentInstance inst, object? device)
        {
            var material = inst.Screen!;
            System.Windows.Media.Imaging.BitmapSource? picture = device switch
            {
                RobotTwin.CoreSim.Firmware.Devices.Ssd1306 o => Editor.DisplayRenderer.Oled(o, inst.ScreenStyle),
                RobotTwin.CoreSim.Firmware.Devices.Pcf8574Lcd l => Editor.DisplayRenderer.Lcd(l, inst.ScreenStyle),
                _ => null
            };
            if (picture == null) return;
            if (material.Brush is ImageBrush ib && ReferenceEquals(ib.ImageSource, picture)) return;
            var brush = new ImageBrush(picture);
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
            material.Brush = brush;
        }

        // ================================================================ camera

        private void UpdateCamera()
        {
            var dir = new Vector3D(Math.Cos(_pitch) * Math.Cos(_yaw), Math.Cos(_pitch) * Math.Sin(_yaw), Math.Sin(_pitch));
            _camera.Position = _target + dir * _distance;
            _camera.LookDirection = -dir;
            _camera.UpDirection = Math.Abs(_pitch) > 1.5 ? new Vector3D(0, 1, 0) : new Vector3D(0, 0, 1);
        }

        /// <summary>Effects work in metres; the board is in millimetres.</summary>
        private (Point Screen, double Depth, bool Visible) Project(Point3D pm)
        {
            var p = new Point3D(pm.X * 1000, pm.Y * 1000, pm.Z * 1000);
            double w = ActualWidth, h = ActualHeight;
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            if (right.Length < 1e-9) return (default, 0, false);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            var rel = p - _camera.Position;
            double z = Vector3D.DotProduct(rel, look);
            if (z < _camera.NearPlaneDistance) return (default, z, false);
            double f = w / 2 / Math.Tan(_camera.FieldOfView * Math.PI / 360);
            _effects.Focal = f;
            return (new Point(w / 2 + Vector3D.DotProduct(rel, right) / z * f, h / 2 - Vector3D.DotProduct(rel, up) / z * f), z / 1000.0, true);
        }
    }

    /// <summary>Textures for the 3D board.</summary>
    internal static class Editor3DTextures
    {
        public static System.Windows.Media.Imaging.BitmapSource BoardTop(BoardDocument board) => PcbTexture.Render(board);
    }
}
