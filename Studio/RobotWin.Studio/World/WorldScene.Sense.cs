using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sense] Sensors in the 3D view: the taped floor (matte, faded with worn patches, glossy),
    /// live sensing spots of line sensors, LiDAR returns, camera frustums and GPS fixes; and the
    /// camera sensors' own renders of the world (offscreen, at their pixel size and frame rate).
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _senseOverlay = new ModelVisual3D();
        private readonly OffscreenWorld _offscreen = new OffscreenWorld();
        private int _senseGeneration;
        private int _gizmoGeneration = -1;
        private string? _gizmoSelection;
        private double _lastOverlay;

        /// <summary>Incremented whenever the scene's models are rebuilt (other views re-link their copies).</summary>
        public int Generation => _senseGeneration;

        /// <summary>Editor overlays of sensors (spots, scans, frustums).</summary>
        public bool ShowSensorOverlays { get; set; } = true;

        /// <summary>Wall-clock time spent rendering camera frames in the last update (ms).</summary>
        public double CameraRenderMs { get; private set; }

        internal Model3D? EnvironmentModel => _environment.Content as Model3D;

        /// <summary>The sculpted ground (grass, runway, roads), or null for a flat floor.</summary>
        internal Model3D? TerrainModel => _terrainView?.Model;

        /// <summary>Every object's model and its current placement (physics pose while simulating).</summary>
        internal List<(string Id, Model3D Content, Matrix3D Matrix)> LiveItems() =>
            _items.Values.Select(it => (it.Object.Id, (Model3D)it.Visual.Content, CurrentMatrix(it))).ToList();

        /// <summary>World matrix of an object right now (from the physics while it runs, so it is correct between screen frames).</summary>
        private Matrix3D CurrentMatrix(Item it)
        {
            var physics = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Physics : null;
            if (physics?.PoseOf(it.Object.Id) is { } pose)
            {
                var m = Matrix3D.Identity;
                m.Rotate(new Quaternion(pose.Orientation.X, pose.Orientation.Y, pose.Orientation.Z, pose.Orientation.W));
                m.Translate(new Vector3D(pose.Position.X, pose.Position.Y, pose.Position.Z));
                return m;
            }

            return it.Current;
        }

        /// <summary>Looks down on the whole floor (track overview).</summary>
        public void FrameFloor()
        {
            var env = World?.Environment;
            double w = env?.Width ?? 2, d = env?.Depth ?? 2;
            _target = new Point3D(0, 0, 0);
            _yaw = Math.PI / 2;
            _pitch = 1.45;
            _distance = Math.Max(w, d) * 1.3;
            UpdateCamera();
        }

        /// <summary>Low close view of an object (line sensors are seen from the side, spots on the floor).</summary>
        public void LookLow(string objectId)
        {
            if (!_items.TryGetValue(objectId, out var it)) return;
            var m = CurrentMatrix(it);
            var c = m.Transform(new Point3D(0, 0, 0));
            var fwd = m.Transform(new Vector3D(1, 0, 0));
            _target = new Point3D(c.X, c.Y, 0.004);
            // In front of the part, just above the floor, looking back under it.
            _yaw = Math.Atan2(-fwd.Y, -fwd.X) + 0.35;
            _pitch = 0.07;
            _distance = 0.3;
            UpdateCamera();
        }

        // ================================================================ floor

        private static readonly Color MatteTape = Color.FromRgb(0x15, 0x15, 0x17);

        /// <summary>Light track mat and the tapes, added to the environment model.</summary>
        private void AppendSense(Model3DGroup g)
        {
            _senseGeneration++;
            var env = World?.Environment;
            if (env == null) return;
            var floor = new FloorOptics(env);
            if (env.Sensing != null)
            {
                byte v = (byte)Math.Max(0x20, Math.Min(0xEE, 0x18 + 0xE0 * env.Sensing.FloorReflectance));
                var mat = M.Matte(Color.FromRgb(v, v, (byte)Math.Max(0, v - 4)), env.Sensing.FloorGloss > 0.05 ? 0.3 : 0);
                g.Children.Add(M.Model(Quad(-env.Width / 2, -env.Depth / 2, env.Width / 2, env.Depth / 2, 0.0002), mat));
            }

            foreach (var tape in floor.Tapes)
            {
                if (tape.Points.Count < 2) continue;
                Color c;
                try
                {
                    c = (Color)ColorConverter.ConvertFromString(tape.DisplayColor());
                }
                catch (FormatException)
                {
                    c = MatteTape;
                }

                Material m = tape.Finish switch
                {
                    TapeFinish.Glossy => Glossy(c, 90),
                    TapeFinish.Metallic => Glossy(c, 60),
                    _ => M.Matte(c)
                };
                g.Children.Add(M.Model(Strip(tape, 0.0006), m));
                if (tape.Finish == TapeFinish.Faded) g.Children.Add(Wear(floor, tape));
            }
        }

        private static Material Glossy(Color c, double power)
        {
            var mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            mg.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)), power));
            return mg;
        }

        private static MeshGeometry3D Quad(double x0, double y0, double x1, double y1, double z)
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions = new Point3DCollection { new Point3D(x0, y0, z), new Point3D(x1, y0, z), new Point3D(x1, y1, z), new Point3D(x0, y1, z) };
            mesh.Normals = new Vector3DCollection { new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1) };
            mesh.TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 };
            return mesh;
        }

        /// <summary>A tape as flat rectangles per segment plus round joints.</summary>
        private static MeshGeometry3D Strip(FloorTape tape, double z)
        {
            var mesh = new MeshGeometry3D();
            double h = tape.Width / 2;
            void Tri(Point3D a, Point3D b, Point3D c)
            {
                int i = mesh.Positions.Count;
                mesh.Positions.Add(a);
                mesh.Positions.Add(b);
                mesh.Positions.Add(c);
                for (int k = 0; k < 3; k++) mesh.Normals.Add(new Vector3D(0, 0, 1));
                mesh.TriangleIndices.Add(i);
                mesh.TriangleIndices.Add(i + 1);
                mesh.TriangleIndices.Add(i + 2);
            }

            for (int i = 0; i + 1 < tape.Points.Count; i++)
            {
                double x0 = tape.Points[i][0], y0 = tape.Points[i][1], x1 = tape.Points[i + 1][0], y1 = tape.Points[i + 1][1];
                double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double nx = -dy / len * h, ny = dx / len * h;
                var a = new Point3D(x0 + nx, y0 + ny, z);
                var b = new Point3D(x0 - nx, y0 - ny, z);
                var c = new Point3D(x1 - nx, y1 - ny, z);
                var d = new Point3D(x1 + nx, y1 + ny, z);
                Tri(a, b, c);
                Tri(a, c, d);
            }

            for (int i = 1; i + 1 < tape.Points.Count; i++)
            {
                var centre = new Point3D(tape.Points[i][0], tape.Points[i][1], z);
                const int n = 12;
                for (int k = 0; k < n; k++)
                {
                    double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                    Tri(centre, new Point3D(centre.X + h * Math.Cos(a0), centre.Y + h * Math.Sin(a0), z), new Point3D(centre.X + h * Math.Cos(a1), centre.Y + h * Math.Sin(a1), z));
                }
            }

            return mesh;
        }

        /// <summary>Worn patches of a faded tape, from the same reflectance map the sensors read (3 mm cells).</summary>
        private static Model3D Wear(FloorOptics floor, FloorTape tape)
        {
            var light = new MeshGeometry3D();
            var mid = new MeshGeometry3D();
            const double cell = 0.003;
            double half = tape.Width / 2;
            for (int i = 0; i + 1 < tape.Points.Count; i++)
            {
                double x0 = tape.Points[i][0], y0 = tape.Points[i][1], x1 = tape.Points[i + 1][0], y1 = tape.Points[i + 1][1];
                double dx = x1 - x0, dy = y1 - y0, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double ux = dx / len, uy = dy / len;
                for (double s = cell / 2; s < len; s += cell)
                {
                    for (double t = -half + cell / 2; t < half; t += cell)
                    {
                        double x = x0 + ux * s - uy * t, y = y0 + uy * s + ux * t;
                        var sample = floor.At(x, y);
                        var target = sample.Diffuse > 0.5 ? light : sample.Diffuse > 0.3 ? mid : null;
                        if (target == null) continue;
                        int k = target.Positions.Count;
                        double hc = cell / 2;
                        target.Positions.Add(new Point3D(x - hc, y - hc, 0.0008));
                        target.Positions.Add(new Point3D(x + hc, y - hc, 0.0008));
                        target.Positions.Add(new Point3D(x + hc, y + hc, 0.0008));
                        target.Positions.Add(new Point3D(x - hc, y + hc, 0.0008));
                        for (int n = 0; n < 4; n++) target.Normals.Add(new Vector3D(0, 0, 1));
                        foreach (int idx in new[] { 0, 1, 2, 0, 2, 3 }) target.TriangleIndices.Add(k + idx);
                    }
                }
            }

            var g = new Model3DGroup();
            g.Children.Add(M.Model(mid, M.Matte(Color.FromRgb(0x7A, 0x7A, 0x7E))));
            g.Children.Add(M.Model(light, M.Matte(Color.FromRgb(0xB4, 0xB4, 0xB6))));
            return g;
        }

        // ================================================================ per frame

        /// <summary>Called from OnFrame: sensor overlays (camera frames are rendered from the session's Updated event, see WorldPage.Sense).</summary>
        private void SenseFrame()
        {
            if (!_view.Children.Contains(_senseOverlay)) _view.Children.Add(_senseOverlay);
            if (!ShowSensorOverlays)
            {
                _senseOverlay.Content = null;
                return;
            }

            bool live = Session != null && Session.State != Simulation.SimState.Stopped && Session.Sensing != null;
            if (live)
            {
                double now = Environment.TickCount64 / 1000.0;
                if (now - _lastOverlay < 1.0 / 30) return;
                _lastOverlay = now;
                _senseOverlay.Content = LiveOverlay(Session!.Sensing!);
                _gizmoGeneration = -1;
            }
            else if (_gizmoGeneration != _senseGeneration || _gizmoSelection != Selected)
            {
                _gizmoGeneration = _senseGeneration;
                _gizmoSelection = Selected;
                _senseOverlay.Content = StaticGizmos();
            }
        }

        private static readonly Material Accent = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x6E, 0x9B, 0xFF)));
        private static readonly Material IrBeam = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x9B, 0x6B, 0xFF)));
        private static readonly Material LidarHit = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
        private static readonly Material GpsMark = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xD4, 0xA8, 0x4B)));

        /// <summary>Sensor gizmos while editing: where line sensors look, camera frustums, LiDAR scan planes.</summary>
        private Model3DGroup StaticGizmos()
        {
            var g = new Model3DGroup();
            if (World == null) return g;
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                if (o.Kind != WorldObjectKind.Component) continue;
                var m = it.Current;
                if (SensingParts.IsLineSensor(o.PartId))
                {
                    foreach (var ch in SensingParts.LineChannels(o.PartId))
                    {
                        var a = m.Transform(new Point3D(ch.Centre.X, ch.Centre.Y, ch.Centre.Z));
                        var dir = m.Transform(new Vector3D(0, 0, -1));
                        if (dir.Z > -0.05) continue;
                        double t = a.Z / -dir.Z;
                        var b = a + dir * t;
                        g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { a, b }, 0.0003, 4), IrBeam));
                        g.Children.Add(new GeometryModel3D(Ring(b, 0.0025, 0.0004), IrBeam));
                    }
                }
                else if (o.PartId == SensingParts.CameraId)
                {
                    var p = Params(o);
                    var spec = CameraModels.Find(p.TryGetValue("model", out var md) ? md : null);
                    var size = CameraModels.ParseFrameSize(p.TryGetValue("framesize", out var fs) ? fs : null, spec.DefaultFrameSize);
                    double fov = p.TryGetValue("fov", out var fv) && double.TryParse(fv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f) && f > 1 ? f : spec.HorizontalFovDeg;
                    var lens = SensingParts.CameraLens(spec.Id);
                    Frustum(g, m.Transform(new Point3D(lens.X, lens.Y, lens.Z)), m.Transform(new Vector3D(1, 0, 0)), m.Transform(new Vector3D(0, 0, 1)), fov, spec.VerticalFovDeg(fov, size.Width, size.Height), 0.12);
                }
                else if (o.PartId == SensingParts.LidarId)
                {
                    var c = SensingParts.LidarCentre(Params(o).TryGetValue("model", out var md) ? md : null);
                    var centre = m.Transform(new Point3D(c.X, c.Y, c.Z));
                    g.Children.Add(new GeometryModel3D(Ring(centre, 0.12, 0.0005), LidarHit));
                }
            }

            return g;
        }

        private Dictionary<string, string> Params(WorldObject o)
        {
            var def = o.PartId != null ? Library.Find(o.PartId) : null;
            if (def == null) return new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase);
            var preset = o.PresetId != null ? Library.FindPreset(o.PresetId) : null;
            return new Dictionary<string, string>(Library.ResolveParameters(def, preset, o.Parameters), StringComparer.OrdinalIgnoreCase);
        }

        private void Frustum(Model3DGroup g, Point3D eye, Vector3D fwd, Vector3D up, double hfov, double vfov, double range)
        {
            fwd.Normalize();
            up.Normalize();
            var right = Vector3D.CrossProduct(fwd, up);
            double tx = Math.Tan(hfov * Math.PI / 360) * range, ty = Math.Tan(vfov * Math.PI / 360) * range;
            var corners = new[]
            {
                eye + fwd * range + right * tx + up * ty, eye + fwd * range - right * tx + up * ty,
                eye + fwd * range - right * tx - up * ty, eye + fwd * range + right * tx - up * ty
            };
            for (int i = 0; i < 4; i++)
            {
                g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { eye, corners[i] }, 0.0004, 4), Accent));
                g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { corners[i], corners[(i + 1) % 4] }, 0.0004, 4), Accent));
            }
        }

        private static MeshGeometry3D Ring(Point3D c, double r, double w)
        {
            var pts = new List<Point3D>();
            for (int i = 0; i <= 32; i++)
            {
                double a = 2 * Math.PI * i / 32;
                pts.Add(new Point3D(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), c.Z + 0.0006));
            }

            return Tube(pts, w, 4);
        }

        private static MeshGeometry3D Disc(Point3D c, double r, double z)
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(new Point3D(c.X, c.Y, z));
            mesh.Normals.Add(new Vector3D(0, 0, 1));
            const int n = 14;
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                mesh.Positions.Add(new Point3D(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a), z));
                mesh.Normals.Add(new Vector3D(0, 0, 1));
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1 + i);
                mesh.TriangleIndices.Add(1 + (i + 1) % n);
            }

            return mesh;
        }

        /// <summary>Live sensing overlay from the latest readings.</summary>
        private Model3DGroup LiveOverlay(WorldSensing sensing)
        {
            var g = new Model3DGroup();
            foreach (var r in sensing.Sensors.All)
            {
                switch (r)
                {
                    case LineSensorReading line:
                        foreach (var ch in line.Channels)
                        {
                            if (double.IsInfinity(ch.Distance)) continue;
                            double s = Math.Max(0, Math.Min(1, ch.Signal));
                            byte v = (byte)(0x22 + 0xCC * s);
                            var spotMat = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(v, v, v)));
                            var edge = ch.DigitalLow == true ? Color.FromRgb(0x3C, 0xD0, 0x64) : ch.DigitalLow == false ? Color.FromRgb(0xF2, 0xA3, 0x3B) : Color.FromRgb(0x6E, 0x9B, 0xFF);
                            var spot = new Point3D(ch.Spot.X, ch.Spot.Y, Math.Max(0.0009, ch.Spot.Z + 0.0009));
                            double radius = Math.Max(0.0015, ch.SpotRadius);
                            g.Children.Add(new GeometryModel3D(Disc(spot, radius, spot.Z), spotMat));
                            g.Children.Add(new GeometryModel3D(Ring(spot, radius, 0.0004), new EmissiveMaterial(new SolidColorBrush(edge))));
                        }

                        break;
                    case LidarReading lidar when lidar.Scan.Points.Length > 0:
                    {
                        var mesh = new MeshGeometry3D();
                        int step = Math.Max(1, lidar.Scan.Points.Length / 3000);
                        double hs = 0.006;
                        for (int i = 0; i < lidar.Scan.Points.Length; i += step)
                        {
                            var p = lidar.Scan.Points[i];
                            if (double.IsNaN(p.X)) continue;
                            AddCube(mesh, p.X, p.Y, p.Z, hs);
                        }

                        g.Children.Add(new GeometryModel3D(mesh, LidarHit));
                        var o = lidar.Scan.Origin;
                        g.Children.Add(new GeometryModel3D(Ring(new Point3D(o.X, o.Y, o.Z), 0.05, 0.0005), LidarHit));
                        break;
                    }

                    case GpsReading gps when gps.Fix.Valid && World?.Find(gps.ObjectId) is { } go:
                    {
                        var (pos, _) = sensing.PoseOf(go);
                        var p = new Point3D(pos.X + gps.Fix.ErrorEast, pos.Y + gps.Fix.ErrorNorth, 0.002);
                        g.Children.Add(new GeometryModel3D(Disc(p, 0.04, 0.002), GpsMark));
                        g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { new Point3D(pos.X, pos.Y, 0.003), p }, 0.0008, 4), GpsMark));
                        break;
                    }
                }
            }

            foreach (var cam in sensing.Cameras)
            {
                var (pos, fwd, up, _) = sensing.CameraFrame(cam);
                Frustum(g, new Point3D(pos.X, pos.Y, pos.Z), new Vector3D(fwd.X, fwd.Y, fwd.Z), new Vector3D(up.X, up.Y, up.Z), cam.HorizontalFovDeg, cam.VerticalFovDeg, 0.12);
            }

            return g;
        }

        private static void AddCube(MeshGeometry3D mesh, double x, double y, double z, double h)
        {
            int b = mesh.Positions.Count;
            for (int i = 0; i < 8; i++) mesh.Positions.Add(new Point3D(x + ((i & 1) != 0 ? h : -h), y + ((i & 2) != 0 ? h : -h), z + ((i & 4) != 0 ? h : -h)));
            int[] idx = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            foreach (int i in idx) mesh.TriangleIndices.Add(b + i);
        }

        // ================================================================ camera sensors

        /// <summary>Renders the cameras whose next frame is due (simulated time), within a wall-clock budget.</summary>
        public void RenderDueCameras(bool force = false)
        {
            var session = Session;
            var sensing = session?.Sensing;
            if (sensing == null || session!.Simulator == null || World == null || sensing.Cameras.Count == 0) return;
            if (WorldSensing.JpegEncoder == null) WorldSensing.JpegEncoder = OffscreenWorld.EncodeJpeg;
            double time = session.Time;
            var sw = Stopwatch.StartNew();
            bool synced = false;
            foreach (var cam in sensing.Cameras)
            {
                if (!force && !cam.Due(time)) continue;
                if (!cam.Powered) continue;
                if (sw.Elapsed.TotalMilliseconds > 35) break;   // the computer cannot keep up: the frame comes later
                if (!synced)
                {
                    _offscreen.Sync(EnvironmentModel, LiveItems().Select(x => (x.Content, x.Matrix)).ToList(), _senseGeneration, TerrainModel);
                    synced = true;
                }

                var (pos, fwd, up, q) = sensing.CameraFrame(cam);
                // Start just in front of the lens so the camera's own body is not in the picture.
                var eye = pos + fwd * 0.001;
                var px = _offscreen.Render(cam.Width, cam.Height, new Point3D(eye.X, eye.Y, eye.Z), new Vector3D(fwd.X, fwd.Y, fwd.Z), new Vector3D(up.X, up.Y, up.Z), cam.HorizontalFovDeg);
                var (_, w) = sensing.Motion(cam.Object);
                var local = q.InverseRotate(w);
                var motion = new CameraMotion { Yaw = local.Z, Pitch = local.Y, Roll = local.X };
                cam.Submit(px, cam.Width, cam.Height, time, motion, sensing.Environment.AmbientLux, sensing.Sensors);
            }

            CameraRenderMs = sw.Elapsed.TotalMilliseconds;
        }
    }
}
