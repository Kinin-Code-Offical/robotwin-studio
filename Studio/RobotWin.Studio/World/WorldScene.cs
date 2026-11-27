using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Assembly3D;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    public enum WorldTool
    {
        Select,
        Cable,
        Joint,

        /// <summary>[mate] Hizala: two faces or axes, then a relation between them.</summary>
        Mate
    }

    /// <summary>3D editor and live view of a <see cref="WorldDocument"/>.</summary>
    public sealed partial class WorldScene : Grid
    {
        private sealed class Item
        {
            public WorldObject Object = null!;
            public ObjectModel Model = null!;
            public ModelVisual3D Visual = null!;
            public Matrix3D Placed;
            public Matrix3D Current;
            public RotateTransform3D? PortRotation;
            public double PortAngle;
        }

        private readonly Viewport3D _view = new Viewport3D { ClipToBounds = true };
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 45, NearPlaneDistance = 0.005, FarPlaneDistance = 200 };
        private readonly ModelVisual3D _environment = new ModelVisual3D();
        private readonly ModelVisual3D _cables = new ModelVisual3D();
        private readonly ModelVisual3D _overlay = new ModelVisual3D();
        private readonly EffectsLayer _effects = new EffectsLayer();
        private readonly TextBlock _hint = new TextBlock();
        private readonly Dictionary<string, Item> _items = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Visual3D, (string ObjectId, string Pin)> _anchorVisuals = new Dictionary<Visual3D, (string, string)>();
        private readonly Dictionary<string, Point3D> _smoke = new Dictionary<string, Point3D>(StringComparer.OrdinalIgnoreCase);
        private double _yaw = 1.15, _pitch = 0.62, _distance = 0.9;   // from the front (−Y), +X to the right
        private Point3D _target = new Point3D(0, 0, 0.02);
        private Point? _orbit;
        private bool _pan;
        private string? _drag;
        private Point3D _dragOffset;
        private double _dragPlaneZ;
        private Point _dragStartScreen;
        private double _dragStartZ;
        private bool _dragMoved;
        private DateTime _lastFrame = DateTime.Now;

        public WorldScene()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0D));
            ClipToBounds = true;
            _view.Camera = _camera;
            _view.Children.Add(_environment);
            _view.Children.Add(_cables);
            _view.Children.Add(_belts);   // [drive] World/WorldScene.Belts.cs
            _view.Children.Add(_overlay);
            _view.Children.Add(_replay);   // [render] replay overlays (World/WorldScene.Playback.cs)
            Children.Add(_view);
            _effects.Project = Project;
            _effects.IsHitTestVisible = false;
            Children.Add(_effects);
            AddGizmoLayer();   // [gizmo] the handles, drawn over the view (World/WorldScene.Gizmo.cs)
            _hint.Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x97));   // TextMuted: 5.9:1 on the canvas
            _hint.FontSize = 12;
            _hint.Margin = new Thickness(14, 0, 14, 10);
            _hint.TextWrapping = TextWrapping.Wrap;   // a long hint goes onto a second line rather than being cut off
            _hint.VerticalAlignment = VerticalAlignment.Bottom;
            _hint.HorizontalAlignment = HorizontalAlignment.Left;
            _hint.IsHitTestVisible = false;
            Children.Add(_hint);
            InitRadio();   // [radio] World/WorldScene.Radio.cs
            InitSdk();   // [sdk] World/WorldScene.Sdk.cs
            Focusable = true;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseWheel += (_, e) =>
            {
                _distance = Math.Max(0.05, Math.Min(60, _distance * Math.Pow(0.9986, e.Delta)));
                UpdateCamera();
            };
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible) CompositionTarget.Rendering += OnFrame;
                else CompositionTarget.Rendering -= OnFrame;
                UpdateBackgroundRun();
            };
            UpdateHint();
            UpdateCamera();
            ApplyQuality();
            Shell.AppSettings.Changed += (_, _) => ApplyQuality();
        }

        /// <summary>Ayarlar → 3D kalitesi: low turns edge antialiasing off.</summary>
        private void ApplyQuality() =>
            RenderOptions.SetEdgeMode(_view, Shell.AppSettings.Current.Quality3D == "low" ? EdgeMode.Aliased : EdgeMode.Unspecified);

        // ================================================================ public surface

        public WorldDocument? World { get; private set; }
        public PartLibrary Library { get; set; } = PartLibrary.Default;
        public WorldSession? Session
        {
            get => _session;
            set
            {
                if (_session != null) _session.StateChanged -= OnSessionState;
                _session = value;
                if (_session != null) _session.StateChanged += OnSessionState;
                UpdateBackgroundRun();
            }
        }

        private WorldSession? _session;
        private bool _backgroundRun;

        private void OnSessionState(object? sender, EventArgs e) => UpdateBackgroundRun();

        /// <summary>
        /// A running world keeps running while its page is out of view (writing code beside it, stepping a program that
        /// stopped at a breakpoint): only the simulation advances then, the 3D view catches up when it is shown again.
        /// </summary>
        private void UpdateBackgroundRun()
        {
            bool want = !IsVisible && _session is { State: Simulation.SimState.Running };
            if (want == _backgroundRun) return;
            _backgroundRun = want;
            if (want) CompositionTarget.Rendering += OnBackgroundFrame;
            else CompositionTarget.Rendering -= OnBackgroundFrame;
        }

        private void OnBackgroundFrame(object? sender, EventArgs e)
        {
            var s = _session;
            if (s == null || s.State != Simulation.SimState.Running || IsVisible)
            {
                UpdateBackgroundRun();
                return;
            }

            using (Simulation.SimThread.Hold())   // [perf] drawing runs beside the simulation thread: hold it off while reading
            using (Shell.Perf.Measure("simülasyon")) s.Frame();
        }

        private WorldTool _tool;

        public WorldTool Tool
        {
            get => _tool;
            set
            {
                _tool = value;
                _pendingPin = null;
                _pinTip.IsOpen = false;
                RebuildOverlay();
                UpdateHint();
            }
        }

        public string? Selected { get; private set; }

        /// <summary>Camera follows the selected object (or the first moving one) while simulating.</summary>
        public bool Follow { get; set; }

        /// <summary>Snap grid in metres (0 = off).</summary>
        public double Grid { get; set; } = 0.005;

        public event Action<string?>? SelectionChanged;

        /// <summary>Raised once before an interactive move changes the document (for undo).</summary>
        public event Action? BeginEdit;

        /// <summary>Raised when an interactive edit finished.</summary>
        public event Action<string>? EditCommitted;

        /// <summary>Cable tool: both ends picked.</summary>
        public event Action<CableEnd, CableEnd>? CableRequested;

        /// <summary>Joint tool: an object was clicked (the page pairs it with the selection).</summary>
        public event Action<string>? JointTargetPicked;

        private CableEnd? _pendingPin;

        public void Load(WorldDocument? world)
        {
            World = world;
            Rebuild();
            if (world != null) FrameAll();
        }

        /// <summary>
        /// [perf] Where the last full rebuild of the scene spent its time (environment, each object's model, cables, overlay,
        /// lights), slowest objects first: the benchmark prints it beside the frame times.
        /// </summary>
        public string LastLoadProfile { get; private set; } = string.Empty;

        /// <summary>Re-creates every object visual (after an edit of the document).</summary>
        public void Rebuild()
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            double Lap()
            {
                double ms = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                return ms;
            }

            var slow = new List<(string Name, double Ms)>();
            _items.Clear();
            PreviewAngles = null;   // [kin] an edited world is shown as built
            for (int i = _view.Children.Count - 1; i >= 0; i--)
            {
                var c = _view.Children[i];
                if (c != _environment && c != _cables && c != _belts && c != _overlay && c != _replay) _view.Children.RemoveAt(i);
            }

            BuildEnvironment();
            double environment = Lap();
            if (World == null)
            {
                _cables.Content = null;
                _belts.Content = null;
                _overlay.Content = null;
                return;
            }

            double models = 0;
            foreach (var o in World.Objects)
            {
                var one = System.Diagnostics.Stopwatch.StartNew();
                var model = ObjectModels.Build(o, World, Library);
                double took = one.Elapsed.TotalMilliseconds;
                models += took;
                slow.Add((o.Name.Length > 0 ? o.Name : o.Id, took));
                var group = new Model3DGroup();
                group.Children.Add(model.Body);
                if (!WorldBuilding.IsProp(o) && FixingMarks(o, model) is { } fixings) group.Children.Add(fixings); // [terrain] props are scenery, not clamped parts   // [phys] "Sabit" shown as screwed/clamped
                RotateTransform3D? rot = null;
                if (model.Moving != null && model.Port is { } port)
                {
                    rot = new RotateTransform3D(new AxisAngleRotation3D(port.Axis, 0), port.Pivot);
                    model.Moving.Transform = rot;
                    group.Children.Add(model.Moving);
                }

                var visual = new ModelVisual3D { Content = group };
                var placed = PoseMatrix(o.Pose);
                visual.Transform = new MatrixTransform3D(placed);
                if (!EditorHidden(o)) _view.Children.Add(visual); // [terrain] hidden in the editor
                _items[o.Id] = new Item { Object = o, Model = model, Visual = visual, Placed = placed, Current = placed, PortRotation = rot };
            }

            double placing = Lap() - models;
            if (Selected != null && !_items.ContainsKey(Selected)) Select(null);
            _alsoSelected.RemoveAll(x => !_items.ContainsKey(x));   // [multiselect] what was deleted leaves the selection
            UpdateKinematics();
            _cableLayoutStale = true;   // [phys] parts changed: lay the cables out again
            Lap();
            RebuildCables();
            double cables = Lap();
            RebuildOverlay();
            double overlay = Lap();
            RebuildReady();   // [objs] lights, glows, room light
            double ready = Lap();
            var top = slow.OrderByDescending(x => x.Ms).Take(3).Where(x => x.Ms >= 5).Select(x => $"{x.Name} {x.Ms:F0}");
            LastLoadProfile = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "ortam {0:F0} · modeller {1:F0} ({2}) · yerleştirme {3:F0} · kablolar {4:F0} [{7}] · seçim {5:F0} · ışıklar {6:F0} ms",
                environment, models, string.Join(", ", top), placing, cables, overlay, ready, LastCableProfile);
        }

        /// <summary>Cable picked in the scene (select tool), or null.</summary>
        public string? SelectedCable { get; private set; }

        /// <summary>A cable was picked (or the cable selection cleared).</summary>
        public event Action<string?>? CableSelectionChanged;

        public void SelectCable(string? cableId)
        {
            if (SelectedCable == cableId) return;
            SelectedCable = cableId;
            if (cableId != null && Selected != null)
            {
                Selected = null;
                RebuildOverlay();
                SelectionChanged?.Invoke(null);
            }

            RebuildCables();
            CableSelectionChanged?.Invoke(cableId);
        }

        /// <summary>Selects one object (or none): the others selected with Ctrl+click are let go (World/WorldScene.Selection.cs).</summary>
        public void Select(string? id)
        {
            if (_alsoSelected.Count > 0)
            {
                _alsoSelected.Clear();
                if (string.Equals(Selected, id, StringComparison.OrdinalIgnoreCase))
                {
                    RebuildOverlay();
                    SelectionChanged?.Invoke(id);
                    return;
                }
            }

            SelectCore(id);
        }

        private void SelectCore(string? id)
        {
            if (id != null && SelectedCable != null)
            {
                SelectedCable = null;
                RebuildCables();
                CableSelectionChanged?.Invoke(null);
            }

            if (Selected == id) return;
            Selected = id;
            RebuildOverlay();
            SelectionChanged?.Invoke(id);
        }

        /// <summary>Moves the camera so every object is in view.</summary>
        /// <summary>
        /// [home] The world as a picture for its file: every object framed from a three-quarter view (front left, from above),
        /// on a clear background (the start page puts its own sky behind it). The view on screen does not move.
        /// </summary>
        public byte[]? RenderPreview(int width = 640, int height = 400)
        {
            if (World == null || World.Objects.Count == 0 || _view.ActualWidth < 20 || _view.ActualHeight < 20) return null;
            var keep = (_target, _distance, _yaw, _pitch);
            double opacity = _view.Opacity;
            try
            {
                _yaw = 0.95;
                _pitch = 0.48;
                FrameAll();
                _distance *= 0.92;   // a little closer than the editor's framing: the picture is small
                UpdateCamera();
                _view.Opacity = 1;
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen()) dc.DrawRectangle(new VisualBrush(_view) { Stretch = Stretch.UniformToFill }, null, new Rect(0, 0, width, height));
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(dv);
                return Branding.Logo.EncodePng(bmp);
            }
            finally
            {
                (_target, _distance, _yaw, _pitch) = keep;
                UpdateCamera();
                _view.Opacity = opacity;
            }
        }

        public void FrameAll()
        {
            if (World == null || World.Objects.Count == 0)
            {
                _target = new Point3D(0, 0, 0.02);
                _distance = 0.9;
                UpdateCamera();
                return;
            }

            var b = Rect3D.Empty;
            foreach (var it in _items.Values) b.Union(it.Visual.Content.Bounds is { IsEmpty: false } bb ? it.Visual.Transform.TransformBounds(bb) : Rect3D.Empty);
            if (b.IsEmpty) return;
            _target = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 3);
            _distance = Math.Max(0.25, Math.Max(b.SizeX, Math.Max(b.SizeY, b.SizeZ)) * 1.9);
            UpdateCamera();
        }

        /// <summary>Camera distance from its target (m).</summary>
        public double CameraDistance
        {
            get => _distance;
            set
            {
                _distance = Math.Max(0.02, value);
                UpdateCamera();
            }
        }

        public void FrameSelected()
        {
            if (Selected == null || !_items.TryGetValue(Selected, out var it)) return;
            var b = it.Visual.Transform.TransformBounds(it.Visual.Content.Bounds);
            _target = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
            _distance = Math.Max(0.12, Math.Max(b.SizeX, Math.Max(b.SizeY, b.SizeZ)) * 3);
            UpdateCamera();
        }

        private void FollowTarget()
        {
            var id = Selected ?? World?.Objects.FirstOrDefault(o => !o.IsStatic)?.Id;
            if (id == null || !_items.TryGetValue(id, out var it)) return;
            var c = it.Current.Transform(new Point3D(0, 0, 0));
            // Glide towards the object so the view does not jerk; the further it has got away (a rocket), the faster the catch-up.
            double gap = Math.Sqrt((c.X - _target.X) * (c.X - _target.X) + (c.Y - _target.Y) * (c.Y - _target.Y) + (c.Z + 0.02 - _target.Z) * (c.Z + 0.02 - _target.Z));
            double k = Math.Min(1, 0.15 + gap / Math.Max(0.05, _distance));
            _target = new Point3D(_target.X + (c.X - _target.X) * k, _target.Y + (c.Y - _target.Y) * k, _target.Z + (c.Z + 0.02 - _target.Z) * k);
            UpdateCamera();
        }

        /// <summary>[hierarchy] Where the view looks (Unity's Move To View puts a part here).</summary>
        public Point3D ViewTarget => _target;

        /// <summary>Frames everything at its current (simulated) place.</summary>
        public void FrameCurrent()
        {
            UpdateKinematics();
            var b = Rect3D.Empty;
            foreach (var it in _items.Values)
            {
                if (!it.Visual.Content.Bounds.IsEmpty) b.Union(new MatrixTransform3D(it.Current).TransformBounds(it.Visual.Content.Bounds));
            }

            if (b.IsEmpty) return;
            _target = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 3);
            _distance = Math.Max(0.25, Math.Max(b.SizeX, Math.Max(b.SizeY, b.SizeZ)) * 1.9);
            UpdateCamera();
        }

        public void TopView()
        {
            _yaw = Math.PI / 2;   // +X right, +Y up on screen
            _pitch = 1.5;
            UpdateCamera();
        }

        /// <summary>Point on the ground in front of the camera where new objects appear.</summary>
        public Point3D DropPoint() => new Point3D(SnapX(_target.X), SnapY(_target.Y), GroundZAt(SnapX(_target.X), SnapY(_target.Y))); // [terrain] on the terrain

        public Point3D WorldAnchor(WorldObject o, string pin)
        {
            if (!_items.TryGetValue(o.Id, out var it)) return new Point3D(o.Pose.X, o.Pose.Y, o.Pose.Z);
            var a = it.Model.Pins.FirstOrDefault(p => p.Pin == pin);
            return a.Pin == null ? new Point3D(o.Pose.X, o.Pose.Y, o.Pose.Z) : it.Current.Transform(a.Local);
        }

        public IReadOnlyList<PinAnchor> PinsOf(string objectId) =>
            _items.TryGetValue(objectId, out var it) ? it.Model.Pins : (IReadOnlyList<PinAnchor>)Array.Empty<PinAnchor>();

        /// <summary>Mass of an object in kg (set by the page; default 10 g).</summary>
        public Func<WorldObject, double>? MassOf { get; set; }

        /// <summary>Physical body of an object for the world physics, taken from its 3D model.</summary>
        public WorldBodyGeometry GeometryOf(WorldObject o)
        {
            if (ReadyParts.Geometry(o) is { } readyGeometry) return readyGeometry;   // [objs] World/WorldScene.Ready.cs
            if (DriveParts.Geometry(o) is { } driveGeometry) return driveGeometry;   // [drive] Sandbox/DriveParts.cs
            var g = new WorldBodyGeometry { MassKg = MassOf?.Invoke(o) ?? 0.01 };
            if (_items.TryGetValue(o.Id, out var it) && !it.Model.Bounds.IsEmpty)
            {
                var b = it.Model.CollisionBounds.IsEmpty ? it.Model.Bounds : it.Model.CollisionBounds;
                g.Min = new RobotTwin.CoreSim.Mechanics.Vec3(b.X, b.Y, b.Z);
                g.Max = new RobotTwin.CoreSim.Mechanics.Vec3(b.X + b.SizeX, b.Y + b.SizeY, b.Z + b.SizeZ);
                if (it.Model.Sensor is { } sr)
                {
                    g.SensorOrigin = new RobotTwin.CoreSim.Mechanics.Vec3(sr.Origin.X, sr.Origin.Y, sr.Origin.Z);
                    g.SensorAxis = new RobotTwin.CoreSim.Mechanics.Vec3(sr.Axis.X, sr.Axis.Y, sr.Axis.Z);
                }

                if (it.Model.Port is { } pf)
                {
                    g.PortName = pf.Name;
                    g.PortPivot = new RobotTwin.CoreSim.Mechanics.Vec3(pf.Pivot.X, pf.Pivot.Y, pf.Pivot.Z);
                    g.PortAxis = new RobotTwin.CoreSim.Mechanics.Vec3(pf.Axis.X, pf.Axis.Y, pf.Axis.Z);
                }
            }
            else
            {
                g.Min = new RobotTwin.CoreSim.Mechanics.Vec3(-0.01, -0.01, 0);
                g.Max = new RobotTwin.CoreSim.Mechanics.Vec3(0.01, 0.01, 0.01);
            }

            if (o.Kind == WorldObjectKind.Body)
            {
                g.Shape = o.Shape switch { "wheel" => "cylinder-y", "cylinder" => "cylinder-z", "sphere" => "sphere", _ => "box" };
                // Exact primitives stay primitives; anything else (a wing's airfoil, taper and sweep) takes its model's shape.
                if (g.Shape == "box" && o.Shape != "box" && o.Shape != "plate" && !WorldPhysics.IsShell(o) && it != null && ModelHulls.Of(it.Model) is { } wingHull)
                {
                    g.Hull = wingHull;
                    g.Shape = "hull";
                }
            }
            else if ((o.Kind == WorldObjectKind.Component || o.Kind == WorldObjectKind.Board) && it != null && ModelHulls.Of(it.Model) is { } hull)
            {
                // [hull] the part's own 3D model, not its bounding box: round cans roll, boards keep their outline.
                g.Hull = hull;
                g.Shape = "hull";
            }
            else if (o.Kind == WorldObjectKind.Model && o.Mesh != null)
            {
                try
                {
                    // Same placement as the visual: centred in X/Y, resting on z = 0.
                    var mesh = RobotTwin.CoreSim.Mechanics.MeshModel.FromEmbedded(o.Mesh);
                    var mb = mesh.Bounds;
                    mesh.Translate(new RobotTwin.CoreSim.Mechanics.Vec3(-(mb.Min.X + mb.Max.X) / 2, -(mb.Min.Y + mb.Max.Y) / 2, -mb.Min.Z));
                    g.Mesh = mesh;
                    g.Shape = "mesh";
                }
                catch (FormatException)
                {
                }
            }

            return g;
        }

        public PortFrame? PortOf(string objectId) => _items.TryGetValue(objectId, out var it) ? it.Model.Port : null;

        public Rect3D BoundsOf(string objectId) => _items.TryGetValue(objectId, out var it) ? it.Model.Bounds : Rect3D.Empty;

        /// <summary>Length of a cable as drawn (sagging between its ends) in metres.</summary>
        public double CableLength(WorldCable cable)
        {
            return RopeLength(cable);   // [phys] the same length the rope gets (World/WorldScene.Cables.cs)
        }

        // ================================================================ scene building

        public static Matrix3D PoseMatrix(WorldPose p)
        {
            var m = Matrix3D.Identity;
            m.Rotate(new Quaternion(new Vector3D(1, 0, 0), p.Roll));
            m.Rotate(new Quaternion(new Vector3D(0, 1, 0), p.Pitch));
            m.Rotate(new Quaternion(new Vector3D(0, 0, 1), p.Yaw));
            m.Translate(new Vector3D(p.X, p.Y, p.Z));
            return m;
        }

        private void BuildEnvironment()
        {
            var g = new Model3DGroup();
            g.Children.Add(new AmbientLight(Color.FromRgb(0x58, 0x58, 0x60)));
            g.Children.Add(new DirectionalLight(Color.FromRgb(0xE6, 0xE2, 0xDA), new Vector3D(-0.45, 0.6, -1)));
            g.Children.Add(new DirectionalLight(Color.FromRgb(0x48, 0x4C, 0x58), new Vector3D(0.6, -0.4, -0.3)));
            var env = World?.Environment;
            double w = env?.Width ?? 4, d = env?.Depth ?? 3;
            var ground = new MeshGeometry3D
            {
                Positions = new Point3DCollection { new Point3D(-w / 2, -d / 2, 0), new Point3D(w / 2, -d / 2, 0), new Point3D(w / 2, d / 2, 0), new Point3D(-w / 2, d / 2, 0) },
                TextureCoordinates = new PointCollection { new Point(0, w), new Point(w, w), new Point(w, 0), new Point(0, 0) },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
                Normals = new Vector3DCollection { new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1) }
            };
            SyncTerrain(); // [terrain] sculpted terrain replaces the flat floor
            // [surface] the floor as its material looks (World/FloorLook.cs), else the editing grid
            var look = FloorLook.Tile(env?.FloorMaterial);
            var tile = look != null
                ? new ImageBrush(look) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) }
                : GridBrush(w, d);   // [grid] lines at the snap step, from the grid's position (World/WorldScene.Grid.cs)
            RenderOptions.SetBitmapScalingMode(tile, BitmapScalingMode.HighQuality);
            if (!TerrainReplacesFloor) g.Children.Add(new GeometryModel3D(ground, new DiffuseMaterial(tile)) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x12))) });
            if (UprightGrid(w, d) is { } upright) g.Children.Add(upright);   // [grid] Unity's grid plane XZ / YZ
            if (env != null)
            {
                var wall = M.Matte(Color.FromRgb(0x4C, 0x4E, 0x55), 0.05);   // painted grey: a room's wall stands out from the dark backdrop
                if (env.Walls)
                {
                    // As high as the walls the robot collides with and the sensors see (WorldSpec.WallHeight).
                    double wh = Math.Max(0.01, env.WallHeight);
                    g.Children.Add(M.Model(M.Box(0, -d / 2 - 0.01, wh / 2, w, 0.02, wh), wall));
                    g.Children.Add(M.Model(M.Box(0, d / 2 + 0.01, wh / 2, w, 0.02, wh), wall));
                    g.Children.Add(M.Model(M.Box(-w / 2 - 0.01, 0, wh / 2, 0.02, d, wh), wall));
                    g.Children.Add(M.Model(M.Box(w / 2 + 0.01, 0, wh / 2, 0.02, d, wh), wall));
                }

                foreach (var ob in env.Obstacles)
                {
                    g.Children.Add(M.Model(M.Box(ob.X, ob.Y, ob.Height / 2, ob.SizeX, ob.SizeY, ob.Height), M.Matte(Color.FromRgb(0x6A, 0x5C, 0x4A), 0.05)));
                }
            }

            AppendSense(g);   // [sense] track mat and tapes (World/WorldScene.Sense.cs)
            _environment.Content = g;
        }

        private void RebuildOverlay()
        {
            foreach (var v in _anchorVisuals.Keys.ToList()) _view.Children.Remove(v);
            _anchorVisuals.Clear();
            var g = new Model3DGroup();
            foreach (var selectedId in World != null ? SelectedAll : Array.Empty<string>())   // [multiselect] every selected object's box
            {
                if (!_items.TryGetValue(selectedId, out var sel)) continue;
                bool active = string.Equals(selectedId, Selected, StringComparison.OrdinalIgnoreCase);
                var b = sel.Model.Bounds;
                if (!b.IsEmpty)
                {
                    var accent = new EmissiveMaterial(new SolidColorBrush(active ? Color.FromArgb(0xFF, 0x6E, 0x9B, 0xFF) : Color.FromArgb(0xFF, 0xA9, 0xC3, 0xFF)));
                    double e = active ? 0.0012 : 0.0008;
                    var box = new Model3DGroup();
                    foreach (var (a, c) in BoxEdges(b)) box.Children.Add(new GeometryModel3D(Edge(a, c, e), accent));
                    box.Transform = new MatrixTransform3D(ExplodedMatrix(sel.Object.Id, sel.Current));   // [explode] the box goes where the part is shown
                    g.Children.Add(box);
                }
            }

            AppendGap(g);   // [clearance] World/WorldScene.Explode.cs
            AppendGizmo(g); // [terrain] move/rotate/scale gizmo
            _overlay.Content = g;

            if (Tool == WorldTool.Cable && World != null)
            {
                foreach (var it in _items.Values)
                {
                    foreach (var pin in it.Model.Pins)
                    {
                        bool pending = _pendingPin != null && _pendingPin.ObjectId == it.Object.Id && _pendingPin.Pin == pin.Pin;
                        var mat = new MaterialGroup();
                        mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(pending ? Color.FromRgb(0x3C, 0xD0, 0x64) : Color.FromRgb(0xF2, 0xB8, 0x3B))));
                        mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(pending ? Color.FromRgb(0x20, 0x80, 0x30) : Color.FromRgb(0x60, 0x40, 0x10))));
                        var p = it.Current.Transform(pin.Local);
                        var v = new ModelVisual3D { Content = M.Model(M.Sphere(pending ? 0.0026 : 0.0018, p.X, p.Y, p.Z, 10), mat) };
                        _view.Children.Add(v);
                        _anchorVisuals[v] = (it.Object.Id, pin.Pin);
                    }
                }
            }
        }

        private static IEnumerable<(Point3D, Point3D)> BoxEdges(Rect3D b)
        {
            var p = new Point3D[8];
            for (int i = 0; i < 8; i++) p[i] = new Point3D(b.X + ((i & 1) != 0 ? b.SizeX : 0), b.Y + ((i & 2) != 0 ? b.SizeY : 0), b.Z + ((i & 4) != 0 ? b.SizeZ : 0));
            int[,] e = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
            for (int i = 0; i < 12; i++) yield return (p[e[i, 0]], p[e[i, 1]]);
        }

        private static MeshGeometry3D Edge(Point3D a, Point3D b, double r) => Tube(new List<Point3D> { a, b }, r, 4);

        /// <summary>Tube mesh along a polyline.</summary>
        public static MeshGeometry3D Tube(IReadOnlyList<Point3D> pts, double radius, int sides = 8)
        {
            var mesh = new MeshGeometry3D();
            FillTube(mesh, pts, radius, sides);
            return mesh;
        }

        /// <summary>
        /// [perf] Fills a mesh with a tube along a polyline: its points and normals built in collections of the right size and
        /// frozen before they go in (one change for the mesh, not one per point); the triangles stay when their count does.
        /// </summary>
        public static void FillTube(MeshGeometry3D mesh, IReadOnlyList<Point3D> pts, double radius, int sides = 8)
        {
            int n = pts.Count;
            var positions = new Point3DCollection(n * sides);
            var normals = new Vector3DCollection(n * sides);
            Span<double> cos = stackalloc double[sides], sin = stackalloc double[sides];
            for (int s = 0; s < sides; s++)
            {
                cos[s] = Math.Cos(2 * Math.PI * s / sides);
                sin[s] = Math.Sin(2 * Math.PI * s / sides);
            }

            for (int i = 0; i < n; i++)
            {
                var dir = i + 1 < n ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
                if (dir.Length < 1e-9) dir = new Vector3D(1, 0, 0);
                dir.Normalize();
                var up = Math.Abs(dir.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
                var u = Vector3D.CrossProduct(dir, up);
                u.Normalize();
                var v = Vector3D.CrossProduct(dir, u);
                for (int s = 0; s < sides; s++)
                {
                    var normal = u * cos[s] + v * sin[s];
                    positions.Add(pts[i] + normal * radius);
                    normals.Add(normal);
                }
            }

            positions.Freeze();
            normals.Freeze();
            mesh.Positions = positions;
            mesh.Normals = normals;
            int triangles = Math.Max(0, n - 1) * sides * 6;
            if (mesh.TriangleIndices.Count == triangles && mesh.TriangleIndices.IsFrozen) return;
            var indices = new Int32Collection(triangles);
            for (int i = 0; i + 1 < n; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b = i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                    indices.Add(a);
                    indices.Add(c);
                    indices.Add(b);
                    indices.Add(b);
                    indices.Add(c);
                    indices.Add(d);
                }
            }

            indices.Freeze();
            mesh.TriangleIndices = indices;
        }

        /// <summary>A hanging cable: quadratic curve sagging between the ends, never below the floor.</summary>
        private static List<Point3D> CablePath(Point3D a, Point3D b)
        {
            double span = (b - a).Length;
            var mid = new Point3D((a.X + b.X) / 2, (a.Y + b.Y) / 2, Math.Max(0.003, Math.Min(a.Z, b.Z) - 0.18 * span));
            var pts = new List<Point3D>();
            const int n = 18;
            for (int i = 0; i <= n; i++)
            {
                double t = i / (double)n, u = 1 - t;
                var p = new Point3D(u * u * a.X + 2 * u * t * mid.X + t * t * b.X, u * u * a.Y + 2 * u * t * mid.Y + t * t * b.Y, u * u * a.Z + 2 * u * t * mid.Z + t * t * b.Z);
                p.Z = Math.Max(0.0015, p.Z);
                pts.Add(p);
            }

            return pts;
        }

        public void RebuildCables()
        {
            if (World == null)
            {
                _cables.Content = null;
                return;
            }

            var g = new Model3DGroup();
            _cableModels.Clear();
            _cableSeen.Clear();
            foreach (var c in World.Cables)
            {
                var a = World.Find(c.A.ObjectId);
                var b = World.Find(c.B.ObjectId);
                if (a == null || b == null) continue;
                if (!WorldBuilding.ActiveInHierarchy(World, a) || !WorldBuilding.ActiveInHierarchy(World, b)) continue;   // [active] not in the run, not drawn
                var pa = WorldAnchor(a, c.A.Pin);
                var pb = WorldAnchor(b, c.B.Pin);
                double r = Math.Max(0.0006, Math.Sqrt(WorldCable.CrossSection(c.Awg) / Math.PI) + 0.0003);
                Color color;
                try
                {
                    color = (Color)ColorConverter.ConvertFromString(c.Color);
                }
                catch (FormatException)
                {
                    color = Colors.OrangeRed;
                }

                var state = Session?.CableState(c);
                if (state is { Burned: true }) color = Color.FromRgb(0x2A, 0x20, 0x1A);
                if (CableColor?.Invoke(c) is Color replay) color = replay;   // [render] current colours in a replay
                int heat = 0;
                if (state is { Burned: false } s && s.Power > 0)
                {
                    // Glows as it heats toward its rating (in steps of 5 % of it, so the look can be shared).
                    double load = Math.Abs(s.Current) / WorldCable.Ampacity(c.Awg);
                    if (load > 0.8) heat = (int)Math.Round(Math.Min(load, 3.0) * 20);
                }

                bool selected = c.Id == SelectedCable;
                if (selected) r *= 1.8;   // selected: thicker and lit in the accent colour

                // [phys] along the rope (drapes over parts, pulled-out plugs hang free)
                var path = ExplodedCable(a, b, ref pa, ref pb, out var apart) ? apart : RopePath(c, pa, pb, out pa, out pb, out _, out _);   // [explode] a moved end: a slack line
                var look = CableLookOf(c.Id);
                look.Tube.Material = CableMaterial(color, heat, selected);
                // [perf] The tube is built again only when the rope moved (or its thickness changed); the GPU keeps the rest.
                if (r != look.Radius || !SamePath(look.Path, path))
                {
                    FillTube(look.Mesh, path, r, 8);
                    look.Path = path;
                    look.Radius = r;
                }

                // Plugs at the ends: one box moved where each plug is.
                Place(look.PlugA, pa);
                Place(look.PlugB, pb);
                _cableModels[look.Tube] = c.Id;
                g.Children.Add(look.Tube);
                g.Children.Add(look.PlugModelA);
                g.Children.Add(look.PlugModelB);
            }

            if (_cableLooks.Count > _cableSeen.Count)
                foreach (var gone in _cableLooks.Keys.Where(k => !_cableSeen.Contains(k)).ToList()) _cableLooks.Remove(gone);
            AppendUsbCables(g);   // [radio] PC → board USB cables
            _cables.Content = g;
        }

        // ================================================================ [perf] what a cable looks like, kept from frame to frame

        /// <summary>A cable's tube and plugs: the same models every frame, the tube's mesh refilled only when the rope moved.</summary>
        private sealed class CableLook
        {
            public readonly MeshGeometry3D Mesh = new MeshGeometry3D();
            public readonly GeometryModel3D Tube;
            public readonly TranslateTransform3D PlugA = new TranslateTransform3D(), PlugB = new TranslateTransform3D();
            public readonly GeometryModel3D PlugModelA, PlugModelB;
            public List<Point3D> Path = new List<Point3D>();
            public double Radius = double.NaN;

            public CableLook()
            {
                Tube = new GeometryModel3D { Geometry = Mesh };
                PlugModelA = new GeometryModel3D(PlugMesh, PlugMaterial) { Transform = PlugA };
                PlugModelB = new GeometryModel3D(PlugMesh, PlugMaterial) { Transform = PlugB };
            }
        }

        private static readonly MeshGeometry3D PlugMesh = Frozen(M.Box(0, 0, 0, 0.0026, 0.0026, 0.004));
        private static readonly Material PlugMaterial = Frozen(M.Matte(Color.FromRgb(0x1A, 0x1A, 0x1D)));
        private readonly Dictionary<string, CableLook> _cableLooks = new Dictionary<string, CableLook>(StringComparer.Ordinal);
        private readonly HashSet<string> _cableSeen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<(Color Color, int Heat, bool Selected), Material> _cableMaterials = new Dictionary<(Color, int, bool), Material>();

        private static T Frozen<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        private CableLook CableLookOf(string id)
        {
            _cableSeen.Add(id);
            if (!_cableLooks.TryGetValue(id, out var look)) _cableLooks[id] = look = new CableLook();
            return look;
        }

        private static void Place(TranslateTransform3D t, Point3D p)
        {
            t.OffsetX = p.X;   // the same value again changes nothing
            t.OffsetY = p.Y;
            t.OffsetZ = p.Z;
        }

        /// <summary>A cable's material, shared by every cable that looks the same (colour, how hot it glows, selected).</summary>
        private Material CableMaterial(Color color, int heat, bool selected)
        {
            var key = (color, heat, selected);
            if (_cableMaterials.TryGetValue(key, out var found)) return found;
            if (_cableMaterials.Count > 512) _cableMaterials.Clear();   // a replay colours cables by their current: keep it bounded
            var mat = new MaterialGroup();
            mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
            mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x50, 255, 255, 255)), 30));
            if (heat > 0)
            {
                double load = heat / 20.0;
                mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb((byte)Math.Min(255, 120 * load), (byte)Math.Min(120, 30 * load), 0))));
            }

            if (selected) mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))));
            mat.Freeze();
            return _cableMaterials[key] = mat;
        }

        /// <summary>Whether a rope lies where it lay (every point within 20 µm: closer than anything on screen can show).</summary>
        private static bool SamePath(List<Point3D> before, List<Point3D> now)
        {
            if (before.Count != now.Count) return false;
            const double tolerance2 = 2e-5 * 2e-5;
            for (int i = 0; i < now.Count; i++)
            {
                var d = now[i] - before[i];
                if (d.LengthSquared > tolerance2) return false;
            }

            return true;
        }

        // ================================================================ kinematics (servo horns, motor shafts and what is fixed to them)

        private void UpdateKinematics()
        {
            if (World == null) return;
            foreach (var it in _items.Values)
            {
                bool live = Session != null && Session.State != Simulation.SimState.Stopped;
                double angle = Playback != null ? Playback.PortAngle(it.Object.Id) : !live ? PreviewAngle(it.Object.Id) : Session!.Physics != null ? Session.Physics.PortAngle(it.Object.Id) : Session.PortAngle(it.Object);
                it.PortAngle = angle;
                if (it.PortRotation != null) ((AxisAngleRotation3D)it.PortRotation.Rotation).Angle = angle * 180 / Math.PI;
                if (it.Model.PortSpin != null) it.Model.PortSpin.Angle = it.Model.PortSign * angle * 180 / Math.PI;
                if (it.Model.FlapRotation != null)   // [aero] elevator / rudder / ailerons follow the live deflection
                    it.Model.FlapRotation.Angle = -it.Model.FlapSign * (live && Session!.Physics != null ? Session.Physics.WingDeflection(it.Object.Id) : 0);
            }

            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var physics = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Physics : null;
            Func<string, (RobotTwin.CoreSim.Mechanics.Vec3 Position, RobotTwin.CoreSim.Mechanics.Quat Orientation)?>? poseOf =
                Playback != null ? Playback.PoseOf : physics != null ? physics.RenderPoseOf : null;   // [interpolate] drawn between steps
            if (poseOf != null)
            {
                foreach (var it in _items.Values)
                {
                    if (poseOf(it.Object.Id) is { } pose)
                    {
                        var q = new Quaternion(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W);
                        var m = Matrix3D.Identity;
                        m.Rotate(q);
                        m.Translate(new Vector3D(pose.Position.X, pose.Position.Y, pose.Position.Z));
                        it.Current = m;
                    }
                }
            }
            else
            {
                foreach (var it in _items.Values) Resolve(it, done, 0);
            }
            UpdateCell();   // [arm] arm links, tools on flanges, belts, feeder cartons (World/WorldScene.Cell.cs)
            foreach (var it in _items.Values) ((MatrixTransform3D)it.Visual.Transform).Matrix = ExplodedMatrix(it.Object.Id, it.Current);   // [shell] World/WorldScene.Shells.cs
            UpdateBelts();   // [drive] GT2 belts round their pulleys (World/WorldScene.Belts.cs)
        }

        private Matrix3D Resolve(Item it, HashSet<string> done, int depth)
        {
            if (done.Contains(it.Object.Id) || depth > 12) return it.Current;
            done.Add(it.Object.Id);
            it.Current = it.Placed;
            foreach (var j in World!.Joints)
            {
                if (j.Kind != WorldJointKind.Fixed)
                {
                    // [drive] A previewed hinge or slider (an imported robot's motorised axis, by joint id): the part turns
                    // about, or slides along, the joint's axis as built, carried by its parent.
                    if (j.ObjectA != it.Object.Id || PreviewAngles == null || !PreviewAngles.TryGetValue(j.Id, out double value)) continue;
                    var axis = new Vector3D(j.AxisX, j.AxisY, j.AxisZ);
                    if (axis.Length < 1e-9) axis = new Vector3D(0, 0, 1);
                    axis.Normalize();
                    var anchor = j.AnchorX == 0 && j.AnchorY == 0 && j.AnchorZ == 0 ? it.Placed.Transform(new Point3D()) : new Point3D(j.AnchorX, j.AnchorY, j.AnchorZ);
                    var turn = Matrix3D.Identity;
                    if (j.Kind == WorldJointKind.Hinge) turn.RotateAt(new Quaternion(axis, value * 180 / Math.PI), anchor);
                    else turn.Translate(axis * value);
                    if (j.ObjectB != null && _items.TryGetValue(j.ObjectB, out var holder))
                    {
                        var holderNow = Resolve(holder, done, depth + 1);
                        var back = holder.Placed;
                        back.Invert();
                        it.Current = it.Placed * turn * back * holderNow;
                    }
                    else
                    {
                        it.Current = it.Placed * turn;
                    }

                    break;
                }

                string? parentId = null, port = null;
                if (j.ObjectA == it.Object.Id && j.ObjectB != null)
                {
                    parentId = j.ObjectB;
                    port = j.PortB;
                }
                else if (j.ObjectB == it.Object.Id && !string.IsNullOrEmpty(j.PortA))
                {
                    parentId = j.ObjectA;
                    port = j.PortA;
                }

                if (parentId == null || !_items.TryGetValue(parentId, out var parent)) continue;
                var parentNow = Resolve(parent, done, depth + 1);
                var inv = parent.Placed;
                inv.Invert();
                var motion = Matrix3D.Identity;
                if (!string.IsNullOrEmpty(port) && parent.Model.Port is { } pf && pf.Name == port)
                {
                    motion.RotateAt(new Quaternion(pf.Axis, parent.PortAngle * 180 / Math.PI), pf.Pivot);
                }

                it.Current = it.Placed * inv * motion * parentNow;
                break;
            }

            return it.Current;
        }

        // ================================================================ live frame

        private TimeSpan _lastRendering = TimeSpan.MinValue;

        private void OnFrame(object? sender, EventArgs e)
        {
            // WPF may raise Rendering more than once for one frame (a D3DImage that changed asks for another pass):
            // the scene is built and drawn once per frame shown.
            if (e is RenderingEventArgs re)
            {
                if (re.RenderingTime == _lastRendering) return;
                _lastRendering = re.RenderingTime;
            }

            // [perf] Drawing runs beside the simulation thread (Simulation/SimThread.cs): it is held off while the frame reads
            // the run and builds the scene from it; the GPU draws what was built while the simulation goes on. A run that
            // cannot keep up gets three quarters of the time: the scene is rebuilt less often, the GPU redraws it between.
            if (Simulation.SimThread.FrameDue(_sceneStart, _sceneCost))
            {
                var now = DateTime.Now;
                double dt = Math.Min(0.1, (now - _lastFrame).TotalSeconds);
                _lastFrame = now;
                _sceneStart = System.Diagnostics.Stopwatch.GetTimestamp();
                using (Simulation.SimThread.Hold())
                {
                    long built = System.Diagnostics.Stopwatch.GetTimestamp();
                    FrameScene(dt);
                    _sceneCost = (System.Diagnostics.Stopwatch.GetTimestamp() - built) / (double)System.Diagnostics.Stopwatch.Frequency;
                }
            }

            GpuFrame();   // [render] our GPU renderer draws what the frame built (World/WorldScene.Gpu.cs)
        }

        private long _sceneStart;     // [perf] when the last frame began building the scene (Stopwatch ticks)
        private double _sceneCost;    // and how long it took (s)

        private void FrameScene(double dt)
        {
            TerrainFrame(dt); // [terrain] LOD, brush strokes
            var s = Session;
            if (s != null && s.State == Simulation.SimState.Running)
            {
                using (Shell.Perf.Measure("simülasyon")) s.Frame();
            }

            if (World != null && s != null && s.State != Simulation.SimState.Stopped)
            {
                using (Shell.Perf.Measure("kinematik"))
                {
                    UpdateKinematics();
                    if (Follow) FollowTarget();
                }

                using (Shell.Perf.Measure("canlı")) UpdateLive(dt);
                using (Shell.Perf.Measure("kablolar")) RebuildCables();
                if (Selected != null)
                {
                    using (Shell.Perf.Measure("seçim")) RebuildOverlay();
                }
            }

            using (Shell.Perf.Measure("efektler"))
            {
                _effects.Step(dt);
                _effects.InvalidateVisual();
            }

            using (Shell.Perf.Measure("hazır parçalar")) ReadyFrame();   // [objs] listener, propeller blur, LED / lamp lights
            using (Shell.Perf.Measure("radyo")) RadioFrame();   // [radio] waves, RF links, controller lightbars
            using (Shell.Perf.Measure("sensörler")) SenseFrame();   // [sense] sensor overlays, camera sensors (World/WorldScene.Sense.cs)
            using (Shell.Perf.Measure("rüzgâr")) SdkFrame();   // [sdk] wind arrows, wind sock, force-field regions
            using (Shell.Perf.Measure("paraşüt")) ChuteFrame();   // [launch] canopies (World/WorldScene.Parachutes.cs)
            using (Shell.Perf.Measure("jetler")) PlumeFrame();   // [gas] exhaust flames and smoke in the 3D scene (World/WorldScene.Plume.cs)
            if (Playback == null) LiquidFrame();   // [liquid] water and other liquids (World/WorldScene.Liquids.cs)
        }

        private readonly ModelVisual3D _beams = new ModelVisual3D();

        /// <summary>Ultrasonic beams: a thin line to what the sensor sees, a dot where it hits, coloured by distance.</summary>
        private void UpdateBeams()
        {
            var physics = Session?.Physics;
            if (physics == null || Session!.State == Simulation.SimState.Stopped)
            {
                if (_beams.Content != null) _beams.Content = null;
                return;
            }

            if (!_view.Children.Contains(_beams)) _view.Children.Add(_beams);
            var g = new Model3DGroup();
            foreach (var it in _items.Values)
            {
                if (it.Model.Sensor == null || physics.SonarBeam(it.Object.Id) is not { } beam) continue;
                var a = new Point3D(beam.From.X, beam.From.Y, beam.From.Z);
                var b = new Point3D(beam.To.X, beam.To.Y, beam.To.Z);
                bool hit = !double.IsInfinity(beam.Range);
                var color = !hit ? Color.FromRgb(0x6C, 0x6C, 0x74) : beam.Range < 0.15 ? Color.FromRgb(0xE5, 0x48, 0x4D) : beam.Range < 0.5 ? Color.FromRgb(0xD4, 0xA8, 0x4B) : Color.FromRgb(0x2F, 0xC2, 0xB0);
                var mat = new EmissiveMaterial(new SolidColorBrush(color));
                g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { a, b }, 0.0007, 6), mat));
                if (hit) g.Children.Add(new GeometryModel3D(Assembly3D.Mesh.Sphere(0.003, b.X, b.Y, b.Z, 10), mat));
            }

            _beams.Content = g;
        }

        private void UpdateLive(double dt)
        {
            UpdateBeams();
            foreach (var it in _items.Values)
            {
                double t = Session!.Temperature(it.Object);
                bool failed = Session.IsFailed(it.Object.Id);
                // Red-hot tint from 70 °C, full at 250 °C; plastics glow from just below their glass
                // transition (where they start to sag) and are fully lit at their melting point.
                double heat = double.IsNaN(t) ? 0 : Math.Max(0, Math.Min(1, (t - 70) / 180));
                if (Session.Physics is { } thermalPhysics)   // [thermal] part temperature from the world's heat network
                {
                    double tb = thermalPhysics.TemperatureOf(it.Object.Id);
                    var mat = thermalPhysics.MaterialOf(it.Object.Id);
                    if (mat != null && !double.IsNaN(mat.GlassTransition) && !double.IsNaN(mat.MeltingPoint))
                        heat = Math.Max(heat, Math.Max(0, Math.Min(1, (tb - (mat.GlassTransition - 10)) / (mat.MeltingPoint - mat.GlassTransition + 10))));
                    else heat = Math.Max(heat, Math.Max(0, Math.Min(1, (tb - 70) / 180)));
                }
                ((SolidColorBrush)it.Model.Heat.Brush).Color = failed ? Color.FromRgb(0x18, 0x08, 0x02) : Color.FromRgb((byte)(200 * heat), (byte)(50 * heat * heat), 0);
                if (it.Model.Instance is { } inst)
                {
                    inst.SetCharred(failed);
                    if (inst.Light != null) inst.SetBrightness(Session.Brightness(it.Object, string.Empty));
                }

                foreach (var (refDes, part) in it.Model.Parts)
                {
                    double pt = Session.PartTemperature(it.Object, refDes);
                    double ph = double.IsNaN(pt) ? 0 : Math.Max(0, Math.Min(1, (pt - 70) / 180));
                    part.SetHeat(Color.FromRgb((byte)(200 * ph), (byte)(50 * ph * ph), 0));
                    part.SetCharred(Session.PartFailed(it.Object, refDes));
                    if (part.Light != null) part.SetBrightness(Session.Brightness(it.Object, refDes));
                }

                foreach (var kv in it.Model.Lights)
                {
                    double b = Session.Brightness(it.Object, kv.Key);
                    ((SolidColorBrush)kv.Value.Brush).Color = Color.FromRgb((byte)(255 * b), (byte)(90 * b), (byte)(70 * b));
                }

                if (it.Model.Pixels.Count > 0) ShowPixels(it.Model, Session.LedStripOf(it.Object));   // [neopixel]

                if (it.Model.Screen != null)
                {
                    switch (Session.DisplayOf(it.Object))
                    {
                        case RobotTwin.CoreSim.Firmware.Devices.Ssd1306 oled:
                            it.Model.Screen.ImageSource = Editor.DisplayRenderer.Oled(oled, it.Model.ScreenStyle);
                            break;
                        case RobotTwin.CoreSim.Firmware.Devices.Pcf8574Lcd lcd:
                            it.Model.Screen.ImageSource = Editor.DisplayRenderer.Lcd(lcd, it.Model.ScreenStyle);
                            break;
                    }

                    if (it.Model.ScreenMaterial != null && it.Model.Screen.ImageSource != null && it.Model.ScreenMaterial.Brush != it.Model.Screen)
                    {
                        it.Model.ScreenMaterial.Brush = it.Model.Screen;
                    }
                }

                if (failed || heat > 0.55)
                {
                    var bb = it.Visual.Transform.TransformBounds(it.Visual.Content.Bounds);
                    var top = new Point3D(bb.X + bb.SizeX / 2, bb.Y + bb.SizeY / 2, bb.Z + bb.SizeZ);
                    if (Shell.AppSettings.Current.Effects) _effects.EmitSmoke(top, failed ? 6 : 10 * heat, dt, failed);
                }
            }
        }

        /// <summary>Spark burst where a part failed.</summary>
        public void Burst(string objectId)
        {
            if (!Shell.AppSettings.Current.Effects || !_items.TryGetValue(objectId, out var it)) return;
            var bb = it.Visual.Transform.TransformBounds(it.Visual.Content.Bounds);
            _effects.Burst(new Point3D(bb.X + bb.SizeX / 2, bb.Y + bb.SizeY / 2, bb.Z + bb.SizeZ), false);
        }

        public void ClearEffects()
        {
            _effects.Clear();
            ClearParachutes();   // [launch]
            ClearPlume();   // [gas]
            UpdateKinematics();
            foreach (var it in _items.Values)
            {
                ((SolidColorBrush)it.Model.Heat.Brush).Color = Colors.Black;
                foreach (var l in it.Model.Lights.Values) ((SolidColorBrush)l.Brush).Color = Colors.Black;
                ShowPixels(it.Model, null);
                it.Model.Instance?.SetCharred(false);
                it.Model.Instance?.SetBrightness(0);
                foreach (var (_, part) in it.Model.Parts)
                {
                    part.SetHeat(Colors.Black);
                    part.SetCharred(false);
                    part.SetBrightness(0);
                }
            }

            RebuildCables();
        }

        /// <summary>
        /// [neopixel] An addressable strip's LEDs in the colours they latched while it has its supply (dark otherwise),
        /// redrawn only when a new frame arrived. The PWM duty is shown as the eye sees it, so dim colours stay visible.
        /// </summary>
        private static void ShowPixels(ObjectModel model, RobotTwin.CoreSim.Firmware.Devices.Ws2812Load? strip)
        {
            int frame = strip != null && strip.Powered ? strip.Strip.Frames : -1;
            if (frame == model.PixelFrame) return;
            model.PixelFrame = frame;
            for (int i = 0; i < model.Pixels.Count; i++)
            {
                (byte R, byte G, byte B) c = frame >= 0 && i < strip!.Strip.Count ? strip.Strip.Colors[i] : ((byte)0, (byte)0, (byte)0);
                static byte Seen(byte v) => (byte)Math.Round(255 * Math.Sqrt(v / 255.0));
                ((SolidColorBrush)model.Pixels[i].Brush).Color = Color.FromRgb(Seen(c.R), Seen(c.G), Seen(c.B));
            }
        }

        // ================================================================ camera & projection

        private void UpdateCamera()
        {
            var dir = new Vector3D(Math.Cos(_pitch) * Math.Cos(_yaw), Math.Cos(_pitch) * Math.Sin(_yaw), -Math.Sin(_pitch));
            _camera.Position = _target - dir * _distance;
            _camera.LookDirection = dir * _distance;
            _camera.UpDirection = new Vector3D(0, 0, 1);
            _gizmoLayer?.InvalidateVisual();   // [gizmo] the handles follow the camera
        }

        private (Point Screen, double Depth, bool Visible) Project(Point3D p)
        {
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            var d = p - _camera.Position;
            double z = Vector3D.DotProduct(d, look);
            if (z <= 0.001) return (default, z, false);
            double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
            double f = w / 2 / Math.Tan(_camera.FieldOfView * Math.PI / 360);
            _effects.Focal = f;
            return (new Point(w / 2 + Vector3D.DotProduct(d, right) / z * f, h / 2 - Vector3D.DotProduct(d, up) / z * f), z, true);
        }

        /// <summary>Ray from the camera through a screen point.</summary>
        private (Point3D Origin, Vector3D Dir) Ray(Point screen)
        {
            var look = _camera.LookDirection;
            look.Normalize();
            var right = Vector3D.CrossProduct(look, _camera.UpDirection);
            right.Normalize();
            var up = Vector3D.CrossProduct(right, look);
            double w = Math.Max(1, ActualWidth), h = Math.Max(1, ActualHeight);
            double f = w / 2 / Math.Tan(_camera.FieldOfView * Math.PI / 360);
            var dir = look * f + right * (screen.X - w / 2) - up * (screen.Y - h / 2);
            dir.Normalize();
            return (_camera.Position, dir);
        }

        private Point3D? OnPlane(Point screen, double z)
        {
            var (o, d) = Ray(screen);
            if (Math.Abs(d.Z) < 1e-6) return null;
            double t = (z - o.Z) / d.Z;
            if (t <= 0) return null;
            return o + d * t;
        }

        private double Snap(double v) => Grid > 0 && SnapOn && !Keyboard.IsKeyDown(Key.LeftAlt) ? Math.Round(v / Grid) * Grid : v;

        // ================================================================ interaction

        private readonly Dictionary<Model3D, string> _cableModels = new Dictionary<Model3D, string>();

        /// <summary>Cable under the cursor in the last <see cref="Pick"/> (only when no object was hit first).</summary>
        private string? _pickedCable;

        private (string? ObjectId, string? Pin) Pick(Point p)
        {
            string? obj = null, pin = null;
            _pickedCable = null;
            VisualTreeHelper.HitTest(_view, PickFilter, r => // [terrain] skip floor/terrain
            {
                if (r is RayMeshGeometry3DHitTestResult hit)
                {
                    if (hit.VisualHit == _cables && hit.ModelHit != null && _cableModels.TryGetValue(hit.ModelHit, out var cid))
                    {
                        if (Tool == WorldTool.Cable) return HitTestResultBehavior.Continue;
                        _pickedCable = cid;
                        return HitTestResultBehavior.Stop;
                    }

                    if (_anchorVisuals.TryGetValue(hit.VisualHit, out var a))
                    {
                        obj = a.ObjectId;
                        pin = a.Pin;
                        return HitTestResultBehavior.Stop;
                    }

                    foreach (var it in _items.Values)
                    {
                        if (it.Visual == hit.VisualHit)
                        {
                            obj ??= it.Object.Id;
                            if (Tool != WorldTool.Cable) return HitTestResultBehavior.Stop;
                            break;
                        }
                    }
                }

                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(p));
            return (obj, pin);
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            var p = e.GetPosition(this);
            if (PointPickDown(e, p)) return;   // [kin] a target point for an arm (World/WorldScene.Kinematics.cs)
            if (!ReadOnly && e.ChangedButton == MouseButton.Left && ShowsRotate && SurfaceSnapHeld && TurnSelectedToward(p)) return;   // [look-at] Unity's rotate towards a surface
            if (!ReadOnly && EditorMouseDown(e, p)) return; // [terrain] terrain brush, prop placement, gizmo
            if (e.ChangedButton != MouseButton.Left)
            {
                _orbit = p;
                _pan = true;
                CaptureMouse();
                return;
            }

            if (e.ClickCount == 2)
            {
                if (Pick(p).ObjectId is { } dbl)
                {
                    Select(dbl);
                    FrameSelected();
                }
                else
                {
                    TopView();
                }

                return;
            }

            var (id, pin) = Pick(p);
            switch (ReadOnly ? WorldTool.Select : Tool)
            {
                case WorldTool.Cable:
                    if (id != null && pin != null)
                    {
                        if (_pendingPin == null)
                        {
                            _pendingPin = new CableEnd { ObjectId = id, Pin = pin };
                            RebuildOverlay();
                            UpdateHint();
                        }
                        else if (!(_pendingPin.ObjectId == id && _pendingPin.Pin == pin))
                        {
                            var a = _pendingPin;
                            _pendingPin = null;
                            CableRequested?.Invoke(a, new CableEnd { ObjectId = id, Pin = pin });
                            RebuildOverlay();
                            UpdateHint();
                        }

                        return;
                    }

                    break;
                case WorldTool.Joint:
                    if (id != null)
                    {
                        JointTargetPicked?.Invoke(id);
                        return;
                    }

                    break;
                case WorldTool.Mate:
                    if (SurfaceAt(p) is { } surface)
                    {
                        MateFacePicked?.Invoke(surface.Id, surface.Point, surface.Normal);
                        return;
                    }

                    break;
                default:
                    if (id != null && World?.Find(id) is { } o)
                    {
                        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                        {
                            ToggleSelect(id);   // [multiselect] Unity's Ctrl+click
                            return;
                        }

                        Select(id);
                        if (!ReadOnly && (Session == null || Session.State == Simulation.SimState.Stopped) && !EditorLocked(o)) // [terrain] locked objects stay
                        {
                            _drag = id;
                            _dragMoved = false;
                            _dragPlaneZ = o.Pose.Z;
                            _dragStartScreen = p;
                            _dragStartZ = o.Pose.Z;
                            var hitPlane = OnPlane(p, _dragPlaneZ);
                            _dragOffset = hitPlane.HasValue ? new Point3D(o.Pose.X - hitPlane.Value.X, o.Pose.Y - hitPlane.Value.Y, 0) : new Point3D();
                            CaptureMouse();
                        }

                        return;
                    }

                    if (_pickedCable != null)
                    {
                        SelectCable(_pickedCable);
                        return;
                    }

                    if (MarqueeDown(p)) return;   // [multiselect] Shift+drag: a rectangle (World/WorldScene.Marquee.cs)
                    Select(null);
                    SelectCable(null);
                    break;
            }

            _orbit = p;
            _pan = false;
            CaptureMouse();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            UpdateThermalHover(e.GetPosition(_view));   // [thermal] World/WorldScene.Thermal.cs
            var p = e.GetPosition(this);
            if (EditorMouseMove(e, p)) return; // [terrain]
            if (_drag != null && World?.Find(_drag) is { } o && _items.TryGetValue(_drag, out var it))
            {
                if (!_dragMoved)
                {
                    if ((p - _dragStartScreen).Length < 3) return;
                    BeginEdit?.Invoke();
                    _dragMoved = true;
                }

                double nx = o.Pose.X, ny = o.Pose.Y, nz = o.Pose.Z;
                bool onCorner = VertexSnap(o, p, ref nx, ref ny, ref nz);   // [vertex-snap] V: corner onto corner (World/WorldScene.VertexSnap.cs)
                if (onCorner)
                {
                    // placed by its corner: no grid, no surface
                }
                else if (SurfaceSnapHeld && SurfaceUnder(p, o) is { } onto)
                {
                    // [surface-snap] Ctrl+Shift: the part's base onto the surface under the cursor (World/WorldScene.SurfaceSnap.cs)
                    nx = onto.X;
                    ny = onto.Y;
                    nz = onto.Z;
                }
                else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    double dz = -(p.Y - _dragStartScreen.Y) * _distance * 0.0016;
                    nz = Math.Max(0, SnapZ(_dragStartZ + dz));
                }
                else if (OnPlane(p, _dragPlaneZ) is { } q)
                {
                    nx = SnapX(q.X + _dragOffset.X);   // [grid] each axis its own step, from the grid's position
                    ny = SnapY(q.Y + _dragOffset.Y);
                }

                if (!onCorner) SnapDrag(o, p, ref nx, ref ny, ref nz); // [terrain] follow the terrain surface
                MoveGroupMates(o, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z); // [terrain] groups move together
                // Whatever is mounted on the object (arms on a servo horn, a wheel on a shaft …) comes along.
                World.MoveAssembly(o, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z);
                foreach (var moved in new[] { o }.Concat(World.MountedOn(o.Id)))
                {
                    if (_items.TryGetValue(moved.Id, out var mi)) mi.Placed = PoseMatrix(moved.Pose);
                }

                UpdateKinematics();
                RebuildCables();
                RebuildOverlay();
                return;
            }

            if (MarqueeMove(p)) return;   // [multiselect]
            if (_orbit == null && _gizmoDrag == GizmoPart.None) UpdateVertexPivot(p);   // [vertex-pivot] World/WorldScene.VertexSnap.cs
            if (_orbit == null)
            {
                if (Tool == WorldTool.Cable) ShowPinTip(p);
                return;
            }

            var d = p - _orbit.Value;
            _orbit = p;
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
                _pitch = Math.Max(-0.1, Math.Min(1.55, _pitch + d.Y * 0.006));
            }

            UpdateCamera();
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (MarqueeUp(e.GetPosition(this))) return;   // [multiselect]
            _vertexOffset = null;   // [vertex-snap] the next drag takes its own corner
            if (EditorMouseUp(e)) return; // [terrain]
            if (_drag != null && _dragMoved && World?.Find(_drag) is { } o)
            {
                EditCommitted?.Invoke($"{o.Name} taşındı ({o.Pose.X * 1000:F0}, {o.Pose.Y * 1000:F0}, {o.Pose.Z * 1000:F0} mm)");
            }

            if (_drag != null && _dragMoved) TrySnapDropped(_drag);   // [objs] drop a wheel / propeller onto a shaft
            _drag = null;
            _orbit = null;
            ReleaseMouseCapture();
        }

        private readonly ToolTip _pinTip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Relative };

        /// <summary>Cable tool: names the pin under the cursor (and its voltage while simulating).</summary>
        private void ShowPinTip(Point p)
        {
            var (id, pin) = Pick(p);
            var o = id != null ? World?.Find(id) : null;
            if (o == null || pin == null || !_items.TryGetValue(o.Id, out var it))
            {
                _pinTip.IsOpen = false;
                return;
            }

            var anchor = it.Model.Pins.FirstOrDefault(a => a.Pin == pin);
            string text = $"{o.Name} · {anchor.Label}";
            if (Session != null && Session.State != Simulation.SimState.Stopped)
            {
                double v = Session.Voltage(new CableEnd { ObjectId = o.Id, Pin = pin });
                if (!double.IsNaN(v)) text += "  ·  " + RobotTwin.CoreSim.Design.SiValue.Format(v, "V");
            }

            int wired = World!.Cables.Count(c => (c.A.ObjectId == o.Id && c.A.Pin == pin) || (c.B.ObjectId == o.Id && c.B.Pin == pin));
            if (wired > 0) text += $"  ·  {wired} kablo";
            _pinTip.Content = text;
            _pinTip.PlacementTarget = this;
            _pinTip.HorizontalOffset = p.X + 14;
            _pinTip.VerticalOffset = p.Y + 10;
            _pinTip.IsOpen = true;
        }

        public void CancelTool()
        {
            _pendingPin = null;
            RebuildOverlay();
            UpdateHint();
        }

        /// <summary>[overlays] The hint line under the scene shown or not.</summary>
        public bool ShowHint
        {
            get => _hint.Visibility == Visibility.Visible;
            set => _hint.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>[overlays] Draws the overlays again after one was switched (the handle, the sensors' beams).</summary>
        public void RefreshOverlays()
        {
            RebuildOverlay();
            InvalidateVisual();
        }

        private void UpdateHint()
        {
            _hint.Text = Tool switch
            {
                WorldTool.Cable => _pendingPin == null
                    ? "Kablo: ilk pini seçin (sarı noktalar) · Esc: iptal"
                    : $"Kablo: {_pendingPin.Pin} seçildi, ikinci pini tıklayın · Esc: iptal",
                WorldTool.Joint => "Bağla: seçili nesnenin takılacağı nesneyi tıklayın (servo kolu, motor mili, gövde) · Esc: iptal",
                WorldTool.Mate => "Hizala: iki parçanın yüzünü ya da eksenini (milin, tekerin yuvarlak yanı) tıklayın, sonra ilişkiyi seçin · Esc: iptal",
                _ => "Sol: seç/taşı (Shift: yükseklik, Alt: ızgarasız, Ctrl: seçime ekle) · boşlukta sol: döndür, Shift+sürükle: dikdörtgenle seç · sağ: kaydır · teker: yakınlaştır · çift tık: odakla/üstten"
            };
        }
    }
}
