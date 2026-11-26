using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    // [terrain] "Arazi" edit mode of the world view: terrain rendering, sculpt/paint brush, ramps,
    // stamps, roads, prop placement, surface snapping, hide/lock/group.
    public sealed partial class WorldScene
    {
        private TerrainView? _terrainView;
        private readonly ModelVisual3D _terrainOverlay = new ModelVisual3D();
        private bool _terrainEditing;
        private bool _stroking;
        private Point _lastMouse;
        private Point3D? _hover;
        private double _strokeTarget;
        private Point3D? _rampStart;
        private readonly List<(double X, double Y)> _road = new List<(double, double)>();
        private string? _pendingProp;

        /// <summary>Sculpt/paint the terrain with the left mouse button (right: orbit, middle: pan).</summary>
        public bool TerrainEditing
        {
            get => _terrainEditing;
            set
            {
                if (_terrainEditing == value) return;
                _terrainEditing = value;
                _stroking = false;
                _rampStart = null;
                if (!value) _road.Clear();
                UpdateTerrainOverlay();
                RebuildOverlay();
                if (value) UpdateTerrainHint();
                else UpdateHint();
            }
        }

        public TerrainTool TerrainTool { get; set; } = TerrainTool.Raise;
        public TerrainBrush Brush { get; } = new TerrainBrush();

        /// <summary>Dragged and new objects sit on the terrain surface.</summary>
        public bool SurfaceSnap { get; set; } = true;

        /// <summary>Placed/dragged single objects tilt with the local slope.</summary>
        public bool AlignToNormal { get; set; }

        /// <summary>Prop waiting to be placed with the next click (null = none).</summary>
        public string? PendingProp
        {
            get => _pendingProp;
            set
            {
                _pendingProp = value;
                UpdateTerrainOverlay();
                if (value != null) _hint.Text = $"{TerrainProps.Find(value)?.Name ?? value}: yerleştirmek için araziye tıklayın (Shift: art arda) · Esc: iptal";
                else if (_terrainEditing) UpdateTerrainHint();
                else UpdateHint();
            }
        }

        /// <summary>A terrain stroke finished (its name, for the status line and the dirty flag).</summary>
        public event Action<string>? TerrainEdited;

        /// <summary>A prop should be placed at (x, y).</summary>
        public event Action<string, double, double>? PropPlaceRequested;

        /// <summary>The flatten/set-height eyedropper picked a height (m).</summary>
        public event Action<double>? TerrainHeightPicked;

        /// <summary>Number of points of the road being drawn.</summary>
        public int RoadPoints => _road.Count;

        public TerrainView? TerrainRenderer => _terrainView;

        // ================================================================ environment

        private bool TerrainReplacesFloor => World?.Terrain != null;

        /// <summary>Keeps the terrain renderer in step with the world's terrain object (called from BuildEnvironment).</summary>
        private void SyncTerrain()
        {
            var map = World?.Terrain;
            if (_terrainView != null && _terrainView.Map == map) return;
            if (_terrainView != null)
            {
                _terrainView.Detach();
                _environment.Children.Remove(_terrainView.Visual);
                _terrainView = null;
            }

            if (!_environment.Children.Contains(_terrainOverlay)) _environment.Children.Add(_terrainOverlay);
            // The hint line stays readable over bright terrain.
            _hint.Background = map == null ? null : new SolidColorBrush(Color.FromArgb(0xC8, 0x0B, 0x0B, 0x0D));
            _hint.Foreground = new SolidColorBrush(map == null ? Color.FromRgb(0x6C, 0x6C, 0x74) : Color.FromRgb(0xC8, 0xC8, 0xD0));
            _hint.Padding = map == null ? new Thickness(0) : new Thickness(8, 3, 8, 3);
            if (map == null) return;
            int tex = Shell.AppSettings.Current.Quality3D == "low" ? 64 : 128;
            _terrainView = new TerrainView(map, tex);
            _environment.Children.Insert(0, _terrainView.Visual);
            _terrainView.BuildAll(_camera.Position);
        }

        /// <summary>Builds every pending terrain chunk now (snapshots).</summary>
        public void FlushTerrain() => _terrainView?.BuildAll(_camera.Position);

        /// <summary>Camera over the whole terrain.</summary>
        public void FrameTerrain()
        {
            var map = World?.Terrain;
            if (map == null)
            {
                FrameAll();
                return;
            }

            var (lo, hi) = map.HeightRange;
            _target = new Point3D(map.CenterX, map.CenterY, (lo + hi) / 2);
            _distance = Math.Max(map.Width, map.Depth) * 1.05;
            _pitch = 0.75;
            _yaw = 1.2;
            UpdateCamera();
            FlushTerrain();
        }

        /// <summary>Camera looking at a point from a given distance, pitch and yaw.</summary>
        public void LookAt(Point3D target, double distance, double pitch, double yaw)
        {
            _target = target;
            _distance = distance;
            _pitch = pitch;
            _yaw = yaw;
            UpdateCamera();
            FlushTerrain();
        }

        /// <summary>Ground height under (x, y): terrain or floor.</summary>
        public double GroundZAt(double x, double y) => World?.Terrain?.Height(x, y) ?? 0.0;

        /// <summary>Point of the ground under the cursor (terrain surface, else the floor plane).</summary>
        private Point3D? PickGround(Point screen)
        {
            var (o, d) = Ray(screen);
            var map = World?.Terrain;
            if (map != null)
            {
                var origin = new Vec3(o.X, o.Y, o.Z);
                var dir = new Vec3(d.X, d.Y, d.Z);
                if (map.Raycast(origin, dir, Math.Max(10, _distance * 6), out double t, insideOnly: true))
                {
                    var p = origin + dir * t;
                    return new Point3D(p.X, p.Y, p.Z);
                }

                return null;
            }

            return OnPlane(screen, 0);
        }

        /// <summary>Hit-test filter of <see cref="Pick"/>: the environment (floor, terrain, brush) is never an object.</summary>
        private HitTestFilterBehavior PickFilter(DependencyObject d) =>
            d == _environment ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue;

        // ================================================================ frame

        private void TerrainFrame(double dt)
        {
            if (_terrainView == null) return;
            // Depth precision over large terrains: the near plane follows the zoom.
            _camera.NearPlaneDistance = Math.Max(0.005, Math.Min(0.5, _distance * 0.01));
            if (_stroking)
            {
                if (_terrainEditing) _hover = PickGround(_lastMouse);
                ApplyStroke(Math.Min(0.05, dt));
                UpdateTerrainOverlay();
            }

            using (Shell.Perf.Measure("arazi")) _terrainView.Update(_camera.Position, _stroking ? 8 : 4);
        }

        // ================================================================ mouse (hooks from OnMouseDown/Move/Up)

        private bool EditorMouseDown(MouseButtonEventArgs e, Point p)
        {
            _lastMouse = p;
            bool editable = Session == null || Session.State == Simulation.SimState.Stopped || _terrainEditing;
            if (_pendingProp != null)
            {
                if (e.ChangedButton == MouseButton.Left && PickGround(p) is { } g)
                {
                    string id = _pendingProp;
                    if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) PendingProp = null;
                    PropPlaceRequested?.Invoke(id, Snap(g.X), Snap(g.Y));
                    return true;
                }

                if (e.ChangedButton == MouseButton.Right)
                {
                    PendingProp = null;
                    return true;
                }
            }

            if (!_terrainEditing) return GizmoMouseDown(e, p);
            if (World?.Terrain is not { } map || !editable) return false;

            if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                // Left is the brush here: right orbits, middle pans.
                _orbit = p;
                _pan = e.ChangedButton == MouseButton.Middle;
                CaptureMouse();
                return true;
            }

            if (e.ChangedButton != MouseButton.Left) return false;
            var hit = PickGround(p);
            _hover = hit;
            if (hit is not { } h) return true;
            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            switch (TerrainTool)
            {
                case TerrainTool.Road:
                    if (e.ClickCount == 2)
                    {
                        FinishRoad();
                        return true;
                    }

                    _road.Add((Snap(h.X), Snap(h.Y)));
                    UpdateTerrainOverlay();
                    UpdateTerrainHint();
                    return true;
                case TerrainTool.Stamp:
                    map.History.BeginStroke(StampName(Brush.Stamp));
                    double height = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -Brush.StampHeight : Brush.StampHeight;
                    TerrainSculpt.Stamp(map, Brush.Stamp, h.X, h.Y, Brush.Angle, height, Brush.StampLength, Brush.StampWidth);
                    EndStroke(map);
                    return true;
                case TerrainTool.Ramp:
                    _rampStart = h;
                    CaptureMouse();
                    UpdateTerrainOverlay();
                    return true;
                case TerrainTool.SetHeight when ctrl:
                case TerrainTool.Flatten when ctrl:
                    Brush.TargetHeight = h.Z;
                    TerrainHeightPicked?.Invoke(h.Z);
                    UpdateTerrainHint();
                    return true;
            }

            _strokeTarget = TerrainTool == TerrainTool.SetHeight ? Brush.TargetHeight : h.Z;
            map.History.BeginStroke(ToolName(TerrainTool));
            _stroking = true;
            CaptureMouse();
            ApplyStroke(1.0 / 60);
            return true;
        }

        private bool EditorMouseMove(MouseEventArgs e, Point p)
        {
            _lastMouse = p;
            if (GizmoMouseMove(e, p)) return true;
            if (!_terrainEditing && _pendingProp == null) return false;
            if (_orbit != null) return false; // the normal camera code orbits/pans
            _hover = PickGround(p);
            UpdateTerrainOverlay();
            if (_terrainEditing) UpdateTerrainHint();
            return true;
        }

        private bool EditorMouseUp(MouseButtonEventArgs e)
        {
            if (GizmoMouseUp()) return true;
            if (!_terrainEditing || World?.Terrain is not { } map) return false;
            if (_rampStart is { } a && e.ChangedButton == MouseButton.Left)
            {
                _rampStart = null;
                if (PickGround(_lastMouse) is { } b && Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)) > map.CellSize)
                {
                    double hb = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? Brush.TargetHeight : b.Z;
                    map.History.BeginStroke("Rampa");
                    TerrainSculpt.Ramp(map, a.X, a.Y, a.Z, b.X, b.Y, hb, 2 * Brush.Radius, Brush.Hardness);
                    EndStroke(map);
                }

                ReleaseMouseCapture();
                UpdateTerrainOverlay();
                return true;
            }

            if (_stroking)
            {
                _stroking = false;
                EndStroke(map);
                ReleaseMouseCapture();
                return true;
            }

            return false;
        }

        private void EndStroke(TerrainMap map)
        {
            string name = map.History.UndoName ?? "Arazi";
            if (map.History.EndStroke(map)) TerrainEdited?.Invoke(name);
            UpdateTerrainHint();
        }

        private void ApplyStroke(double dt)
        {
            if (World?.Terrain is not { } map || _hover is not { } h) return;
            var b = Brush;
            bool invert = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            switch (TerrainTool)
            {
                case TerrainTool.Raise:
                case TerrainTool.Lower:
                {
                    double sign = (TerrainTool == TerrainTool.Lower) ^ invert ? -1 : 1;
                    TerrainSculpt.Raise(map, b, h.X, h.Y, sign * TerrainSculpt.RaiseRate(b) * dt);
                    break;
                }

                case TerrainTool.Smooth:
                    TerrainSculpt.Smooth(map, b, h.X, h.Y, Math.Min(1, b.Strength * dt * 14));
                    break;
                case TerrainTool.Flatten:
                case TerrainTool.SetHeight:
                    TerrainSculpt.Flatten(map, b, h.X, h.Y, _strokeTarget, Math.Min(1, b.Strength * dt * 10));
                    break;
                case TerrainTool.Noise:
                    TerrainSculpt.Noise(map, b, h.X, h.Y, (invert ? -1 : 1) * b.Strength * Math.Max(0.004, b.NoiseScale * 0.12) * dt * 3);
                    break;
                case TerrainTool.Erode:
                    TerrainSculpt.Erode(map, b, h.X, h.Y, 3, b.Strength);
                    break;
                case TerrainTool.Paint:
                    TerrainSculpt.Paint(map, b, h.X, h.Y, Math.Min(1, b.Strength * dt * 8));
                    break;
            }
        }

        /// <summary>Lays the road drawn so far (Enter / double click).</summary>
        public void FinishRoad()
        {
            if (World?.Terrain is not { } map || _road.Count < 2)
            {
                _road.Clear();
                UpdateTerrainOverlay();
                return;
            }

            map.History.BeginStroke("Yol");
            TerrainSculpt.Road(map, _road.ToList(), Brush.RoadWidth, Brush.Layer, Brush.RoadFlatten);
            _road.Clear();
            EndStroke(map);
            UpdateTerrainOverlay();
        }

        /// <summary>Shows the brush at a point as if the cursor were there (snapshots, demos).</summary>
        public void ShowBrushAt(double x, double y)
        {
            _hover = new Point3D(x, y, GroundZAt(x, y));
            UpdateTerrainOverlay();
            UpdateTerrainHint();
        }

        public void CancelRoad()
        {
            _road.Clear();
            _rampStart = null;
            UpdateTerrainOverlay();
            UpdateTerrainHint();
        }

        public void RemoveLastRoadPoint()
        {
            if (_road.Count > 0) _road.RemoveAt(_road.Count - 1);
            UpdateTerrainOverlay();
            UpdateTerrainHint();
        }

        /// <summary>Undo/redo of terrain strokes; returns the stroke name or null.</summary>
        public string? TerrainUndo(bool redo)
        {
            if (World?.Terrain is not { } map) return null;
            var name = redo ? map.History.Redo(map) : map.History.Undo(map);
            if (name != null) TerrainEdited?.Invoke(name);
            return name;
        }

        // ================================================================ brush overlay and readout

        private static readonly Material BrushOuter = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF)));
        private static readonly Material BrushInner = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x3A, 0x55, 0x99)));
        private static readonly Material RoadMat = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x3B)));

        private void UpdateTerrainOverlay()
        {
            var map = World?.Terrain;
            if ((!_terrainEditing && _pendingProp == null) || _hover is not { } h)
            {
                if (_terrainOverlay.Content != null) _terrainOverlay.Content = null;
                if (!_terrainEditing) return;
                if (_road.Count == 0) return;
            }

            var g = new Model3DGroup();
            double lift = Math.Max(0.003, _distance * 0.004);
            double r = Math.Max(0.0008, _distance * 0.0016);
            Point3D OnGround(double x, double y) => new Point3D(x, y, GroundZAt(x, y) + lift);
            void Ring(double cx, double cy, double radius, Material mat)
            {
                var pts = new List<Point3D>();
                int n = 72;
                for (int i = 0; i <= n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    pts.Add(OnGround(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
                }

                g.Children.Add(new GeometryModel3D(Tube(pts, r, 5), mat));
            }

            if (_hover is { } c)
            {
                if (_pendingProp != null && TerrainProps.Find(_pendingProp) is { } def)
                {
                    Ring(c.X, c.Y, Math.Sqrt(def.SizeX * def.SizeX + def.SizeY * def.SizeY) / 2, BrushOuter);
                }
                else if (_terrainEditing && TerrainTool == TerrainTool.Stamp)
                {
                    // Stamp footprint, turned by its angle.
                    double a = Brush.Angle * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
                    if (Brush.Stamp == TerrainStamp.Bump || Brush.Stamp == TerrainStamp.Pothole) Ring(c.X, c.Y, Brush.StampLength / 2, BrushOuter);
                    else
                    {
                        double hl = Brush.StampLength / 2, hw = Brush.StampWidth / 2;
                        var corners = new[] { (-hl, -hw), (hl, -hw), (hl, hw), (-hl, hw), (-hl, -hw) };
                        var pts = new List<Point3D>();
                        for (int i = 0; i + 1 < corners.Length; i++)
                        {
                            for (int k = 0; k < 12; k++)
                            {
                                double t = k / 12.0;
                                double u = corners[i].Item1 + (corners[i + 1].Item1 - corners[i].Item1) * t, v = corners[i].Item2 + (corners[i + 1].Item2 - corners[i].Item2) * t;
                                pts.Add(OnGround(c.X + u * ca - v * sa, c.Y + u * sa + v * ca));
                            }
                        }

                        pts.Add(pts[0]);
                        g.Children.Add(new GeometryModel3D(Tube(pts, r, 5), BrushOuter));
                        // Arrow of the stamp direction.
                        g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { OnGround(c.X, c.Y), OnGround(c.X + hl * ca, c.Y + hl * sa) }, r, 5), BrushInner));
                    }
                }
                else if (_terrainEditing && TerrainTool == TerrainTool.Road)
                {
                    Ring(c.X, c.Y, Brush.RoadWidth / 2, BrushOuter);
                }
                else if (_terrainEditing)
                {
                    Ring(c.X, c.Y, Brush.Radius, BrushOuter);
                    if (Brush.Hardness > 0.02 && Brush.Hardness < 0.98) Ring(c.X, c.Y, Brush.Radius * Brush.Hardness, BrushInner);
                }

                if (_rampStart is { } a0)
                {
                    var pts = new List<Point3D>();
                    for (int i = 0; i <= 24; i++)
                    {
                        double t = i / 24.0;
                        double x = a0.X + (c.X - a0.X) * t, y = a0.Y + (c.Y - a0.Y) * t;
                        pts.Add(new Point3D(x, y, a0.Z + (c.Z - a0.Z) * t + lift));
                    }

                    g.Children.Add(new GeometryModel3D(Tube(pts, r * 1.5, 6), RoadMat));
                }
            }

            if (_terrainEditing && _road.Count > 0 && map != null)
            {
                var pts = _road.ToList();
                if (_hover is { } hv && TerrainTool == TerrainTool.Road) pts.Add((hv.X, hv.Y));
                var line = TerrainSculpt.Spline(pts, Math.Max(0.02, map.CellSize));
                g.Children.Add(new GeometryModel3D(Tube(line.Select(q => OnGround(q.X, q.Y)).ToList(), r * 1.5, 6), RoadMat));
                foreach (var q in _road)
                {
                    var pg = OnGround(q.X, q.Y);
                    g.Children.Add(new GeometryModel3D(M.Sphere(r * 4, pg.X, pg.Y, pg.Z, 10), RoadMat));
                }
            }

            _terrainOverlay.Content = g;
        }

        public static string FormatLength(double m)
        {
            double a = Math.Abs(m);
            if (a < 0.1) return (m * 1000).ToString("0.#", CultureInfo.InvariantCulture) + " mm";
            if (a < 1) return (m * 100).ToString("0.##", CultureInfo.InvariantCulture) + " cm";
            return m.ToString("0.###", CultureInfo.InvariantCulture) + " m";
        }

        public static string ToolName(TerrainTool t) => t switch
        {
            TerrainTool.Raise => "Yükselt",
            TerrainTool.Lower => "Alçalt",
            TerrainTool.Smooth => "Yumuşat",
            TerrainTool.Flatten => "Düzleştir",
            TerrainTool.SetHeight => "Yüksekliğe ayarla",
            TerrainTool.Noise => "Pürüz",
            TerrainTool.Erode => "Aşındır",
            TerrainTool.Ramp => "Rampa",
            TerrainTool.Stamp => "Damga",
            TerrainTool.Paint => "Boya",
            _ => "Yol"
        };

        public static string StampName(TerrainStamp s) => s switch
        {
            TerrainStamp.Bump => "Tümsek",
            TerrainStamp.Pothole => "Çukur",
            TerrainStamp.SpeedBump => "Hız kesici",
            TerrainStamp.Kerb => "Bordür",
            _ => "Basamak"
        };

        private void UpdateTerrainHint()
        {
            if (!_terrainEditing) return;
            string tool = ToolName(TerrainTool);
            string how = TerrainTool switch
            {
                TerrainTool.Raise => "sol: yükselt · Shift: alçalt",
                TerrainTool.Lower => "sol: alçalt · Shift: yükselt",
                TerrainTool.Flatten => "sol: tıklanan yüksekliğe düzleştir · Ctrl+tık: yüksekliği al",
                TerrainTool.SetHeight => $"sol: {FormatLength(Brush.TargetHeight)} yüksekliğe çek · Ctrl+tık: yüksekliği al",
                TerrainTool.Noise => "sol: pürüz ekle · Shift: ters",
                TerrainTool.Ramp => "başlangıçtan bitişe sürükleyin · Ctrl: bitiş = hedef yükseklik",
                TerrainTool.Stamp => $"tık: {StampName(Brush.Stamp)} · Shift: ters · Q/E: döndür",
                TerrainTool.Paint => $"sol: {TerrainLayers.Get(Brush.Layer).Name} boya",
                TerrainTool.Road => _road.Count == 0 ? "noktalara tıklayın · Enter/çift tık: bitir · Geri: son noktayı sil" : $"{_road.Count} nokta · Enter/çift tık: yolu döşe · Esc: iptal",
                _ => string.Empty
            };
            string at = string.Empty;
            if (_hover is { } h && World?.Terrain is { } map)
            {
                var layer = map.LayerAt(h.X, h.Y);
                var f = layer.Rubber;
                at = $"  ·  x {h.X:0.00} m  y {h.Y:0.00} m  ·  yükseklik {FormatLength(h.Z)}  ·  eğim {map.SlopeDegrees(h.X, h.Y):0.0}°  ·  {layer.Name} (μ kauçuk {f.Static:0.00}/{f.Kinetic:0.00}, Crr {f.Rolling:0.000}{(layer.IsLoose ? ", gevşek zemin" : string.Empty)})";
            }

            _hint.Text = $"Arazi · {tool}: {how} · fırça {FormatLength(Brush.Radius)}{at}  ·  sağ: döndür, orta: kaydır";
        }

        // ================================================================ snapping, hide / lock / group (hooks)

        /// <summary>Drag on the terrain: follow the surface under the cursor (and tilt with it).</summary>
        private void SnapDrag(WorldObject o, Point screen, ref double nx, ref double ny, ref double nz)
        {
            if (World?.Terrain == null || !SurfaceSnap || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            if (PickGround(screen) is { } g)
            {
                nx = Snap(g.X + _dragOffset.X);
                ny = Snap(g.Y + _dragOffset.Y);
            }

            nz = GroundZAt(nx, ny);
            if (AlignToNormal && World.MountedOn(o.Id).Count == 0)
            {
                var copy = new WorldObject { Pose = new WorldPose { X = nx, Y = ny, Yaw = o.Pose.Yaw } };
                WorldBuilding.PlaceOnGround(World, copy, true);
                o.Pose.Roll = copy.Pose.Roll;
                o.Pose.Pitch = copy.Pose.Pitch;
            }
        }

        /// <summary>Objects of the same group move together.</summary>
        private void MoveGroupMates(WorldObject o, double dx, double dy, double dz)
        {
            if (World == null || (dx == 0 && dy == 0 && dz == 0)) return;
            foreach (var mate in WorldBuilding.GroupMates(World, o))
            {
                if (WorldBuilding.IsLocked(mate)) continue;
                World.MoveAssembly(mate, dx, dy, dz);
                foreach (var moved in new[] { mate }.Concat(World.MountedOn(mate.Id)))
                    if (_items.TryGetValue(moved.Id, out var mi))
                        mi.Placed = PoseMatrix(moved.Pose);
            }
        }

        private bool EditorHidden(WorldObject o) => WorldBuilding.IsHidden(o) || (World != null && !WorldBuilding.ActiveInHierarchy(World, o));   // [active] off: out of the view

        private static bool EditorLocked(WorldObject o) => WorldBuilding.IsLocked(o);
    }
}
