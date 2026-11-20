using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;
using V3 = RobotTwin.CoreSim.Mechanics.Vec3;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [objs] Ready parts and free-standing lights in the 3D view: spinning propellers blur into a
    /// disc, LEDs and lamps light the scene with real point lights (colour and intensity from the
    /// simulated current and the part's luminous flux) plus a glow, the room light can be dimmed,
    /// the camera is the listener of the positional audio, and a ready wheel or propeller dropped
    /// next to a free motor shaft or servo horn snaps onto it.
    /// </summary>
    public sealed partial class WorldScene
    {
        private sealed class LightSource
        {
            public Item It = null!;
            public Point3D Local;
            public double Size;
            public Model3DGroup Halo = null!;
            public MatrixTransform3D Place = null!;
            public SolidColorBrush Inner = null!, Outer = null!;
            public bool Shown;
            public double Lumens;
            public Color Colour;
            public Point3D World;
            public bool Rgb;

            // Pool of light on the floor: stacked flat discs (illuminance steps of ½).
            public Model3DGroup Pool = null!;
            public readonly List<(SolidColorBrush Brush, ScaleTransform3D Size, GeometryModel3D Disc)> Rings = new List<(SolidColorBrush, ScaleTransform3D, GeometryModel3D)>();
            public TranslateTransform3D PoolAt = new TranslateTransform3D();
            public bool PoolShown;
        }

        private static MeshGeometry3D? _unitDisc;

        private static MeshGeometry3D UnitDisc()
        {
            if (_unitDisc != null) return _unitDisc;
            var m = new MeshGeometry3D();
            const int n = 48;
            m.Positions.Add(new Point3D(0, 0, 0));
            m.Normals.Add(new Vector3D(0, 0, 1));
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                m.Positions.Add(new Point3D(Math.Cos(a), Math.Sin(a), 0));
                m.Normals.Add(new Vector3D(0, 0, 1));
            }

            for (int i = 0; i < n; i++)
            {
                m.TriangleIndices.Add(0);
                m.TriangleIndices.Add(1 + i);
                m.TriangleIndices.Add(1 + (i + 1) % n);
            }

            m.Freeze();
            _unitDisc = m;
            return m;
        }

        /// <summary>Illuminance (relative to E_ref) at each pool ring's edge; inner rings add up to these levels.</summary>
        private static readonly double[] RingLevel = { 1.0, 0.6, 0.36, 0.22, 0.13, 0.08, 0.05 };

        /// <summary>WPF evaluates every light per vertex: only the brightest few sources get a real light.</summary>
        private const int MaxPointLights = 8;

        private readonly List<LightSource> _sources = new List<LightSource>();
        private readonly List<PointLight> _pool = new List<PointLight>();
        private readonly List<(Light Light, Color Base)> _roomLights = new List<(Light, Color)>();
        private readonly ModelVisual3D _lampVisual = new ModelVisual3D();
        private readonly ModelVisual3D _glowVisual = new ModelVisual3D();
        private static MeshGeometry3D? _unitSphere;

        /// <summary>A dragged ready wheel / propeller was dropped onto a free port: (part id, parent id, port name).</summary>
        public event Action<string, string, string>? ReadySnapRequested;

        /// <summary>After every rebuild: light sources, their glow and light pool, the room light.</summary>
        private void RebuildReady()
        {
            _sources.Clear();
            _pool.Clear();
            _roomLights.Clear();
            var glows = new Model3DGroup();
            var lamps = new Model3DGroup();
            _unitSphere ??= Freeze(M.Sphere(1, 0, 0, 0, 18));
            if (World != null)
            {
                foreach (var it in _items.Values)
                {
                    var o = it.Object;
                    if (o.Kind != WorldObjectKind.Component) continue;
                    var def = o.PartId != null ? Library.Find(o.PartId) : null;
                    if (!WorldSession.IsLight(def)) continue;
                    Point3D local;
                    if (it.Model.Instance is { } inst) local = new Point3D(inst.HeatCentre.X * 0.001, inst.HeatCentre.Y * 0.001, inst.HeatCentre.Z * 0.001);
                    else
                    {
                        var b = it.Model.Bounds;
                        local = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ);
                    }

                    double size = def!.Id switch
                    {
                        "led-power" => 0.005,
                        "led-strip" => 0.004,
                        "lamp" => 0.009,
                        _ => (def.ResolveFootprint(o.Parameters).Contains("3MM", StringComparison.OrdinalIgnoreCase) ? 0.002 : 0.0032)
                    };
                    var src = new LightSource
                    {
                        It = it, Local = local, Size = size, Rgb = def.Id == "led-rgb",
                        Inner = new SolidColorBrush(Colors.Black), Outer = new SolidColorBrush(Colors.Black), Place = new MatrixTransform3D()
                    };
                    src.Halo = new Model3DGroup { Transform = src.Place };
                    src.Pool = new Model3DGroup { Transform = src.PoolAt };
                    for (int k = 0; k < RingLevel.Length; k++)
                    {
                        var brush = new SolidColorBrush(Colors.Black);
                        var ringSize = new ScaleTransform3D(0.01, 0.01, 1);
                        var group = new Transform3DGroup();
                        group.Children.Add(ringSize);
                        group.Children.Add(new TranslateTransform3D(0, 0, 0.0004 + 0.00012 * k));
                        var disc = new GeometryModel3D(UnitDisc(), Glow(brush)) { Transform = group };
                        src.Rings.Add((brush, ringSize, disc));
                        src.Pool.Children.Add(disc);
                    }

                    src.Halo.Children.Add(new GeometryModel3D(_unitSphere, Glow(src.Inner)) { Transform = new ScaleTransform3D(1, 1, 1) });
                    src.Halo.Children.Add(new GeometryModel3D(_unitSphere, Glow(src.Outer)) { Transform = new ScaleTransform3D(2.6, 2.6, 2.6) });
                    _sources.Add(src);
                }
            }

            for (int i = 0; i < Math.Min(MaxPointLights, _sources.Count); i++)
            {
                var pl = new PointLight(Colors.Black, new Point3D()) { ConstantAttenuation = 1, LinearAttenuation = 0, QuadraticAttenuation = 1, Range = 8 };
                _pool.Add(pl);
                lamps.Children.Add(pl);
            }

            _lampVisual.Content = lamps;
            _glowVisual.Content = glows;
            _view.Children.Remove(_lampVisual);
            _view.Children.Remove(_glowVisual);
            _view.Children.Add(_lampVisual);
            _view.Children.Add(_glowVisual);   // translucent glows after everything opaque
            if (_sources.Count > 0) TessellateGround();
            if (_environment.Content is Model3DGroup env)
            {
                foreach (var l in env.Children.OfType<Light>())
                {
                    if (l is AmbientLight a) _roomLights.Add((a, a.Color));
                    else if (l is DirectionalLight d) _roomLights.Add((d, d.Color));
                }
            }

            ApplyRoomLight();
        }

        private static MeshGeometry3D Freeze(MeshGeometry3D m)
        {
            m.Freeze();
            return m;
        }

        /// <summary>Pure emission: WPF adds it onto what is behind (no diffuse layer that would cover it).</summary>
        private static Material Glow(SolidColorBrush brush) => new EmissiveMaterial(brush);

        /// <summary>Dims the workshop lights to <see cref="WorldDocument.RoomLight"/> (ambient keeps a little so nothing is pitch black).</summary>
        public void ApplyRoomLight()
        {
            double k = Math.Max(0, Math.Min(1, World?.RoomLight ?? 1));
            foreach (var (light, c) in _roomLights)
            {
                double f = light is AmbientLight ? 0.22 + 0.78 * k : 0.1 + 0.9 * k;
                var col = Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
                if (light is AmbientLight a) a.Color = col;
                else if (light is DirectionalLight d) d.Color = col;
            }
        }

        /// <summary>
        /// WPF lights per vertex: the floor quad becomes a grid (≈ 2.5 cm cells) so point lights make pools of
        /// light on it. Same texture mapping as the original quad.
        /// </summary>
        private void TessellateGround()
        {
            if (_environment.Content is not Model3DGroup env) return;
            foreach (var gm in env.Children.OfType<GeometryModel3D>())
            {
                if (gm.Geometry is not MeshGeometry3D mesh || mesh.Positions.Count != 4 || mesh.TextureCoordinates.Count != 4) continue;
                if (mesh.Positions.Any(p => Math.Abs(p.Z) > 1e-9)) continue;
                double x0 = mesh.Positions.Min(p => p.X), x1 = mesh.Positions.Max(p => p.X), y0 = mesh.Positions.Min(p => p.Y), y1 = mesh.Positions.Max(p => p.Y);
                double w = x1 - x0, d = y1 - y0;
                if (w <= 0 || d <= 0) continue;
                // Texture: bilinear in the original corner coordinates.
                Point Uv(double x, double y)
                {
                    var t = mesh.TextureCoordinates;
                    double u = (x - x0) / w, v = (y - y0) / d;
                    Point At(double px, double py)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            if (Math.Abs(mesh.Positions[i].X - px) < 1e-9 && Math.Abs(mesh.Positions[i].Y - py) < 1e-9) return t[i];
                        }

                        return new Point(0, 0);
                    }

                    var a = At(x0, y0);
                    var b = At(x1, y0);
                    var c = At(x1, y1);
                    var e = At(x0, y1);
                    return new Point((1 - u) * (1 - v) * a.X + u * (1 - v) * b.X + u * v * c.X + (1 - u) * v * e.X,
                        (1 - u) * (1 - v) * a.Y + u * (1 - v) * b.Y + u * v * c.Y + (1 - u) * v * e.Y);
                }

                int nx = Math.Max(4, Math.Min(220, (int)Math.Ceiling(w / 0.025))), ny = Math.Max(4, Math.Min(220, (int)Math.Ceiling(d / 0.025)));
                var g = new MeshGeometry3D();
                var pos = new Point3DCollection((nx + 1) * (ny + 1));
                var nrm = new Vector3DCollection((nx + 1) * (ny + 1));
                var uv = new PointCollection((nx + 1) * (ny + 1));
                var idx = new Int32Collection(nx * ny * 6);
                for (int j = 0; j <= ny; j++)
                {
                    for (int i = 0; i <= nx; i++)
                    {
                        double x = x0 + w * i / nx, y = y0 + d * j / ny;
                        pos.Add(new Point3D(x, y, 0));
                        nrm.Add(new Vector3D(0, 0, 1));
                        uv.Add(Uv(x, y));
                    }
                }

                for (int j = 0; j < ny; j++)
                {
                    for (int i = 0; i < nx; i++)
                    {
                        int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, e = c + 1;
                        idx.Add(a);
                        idx.Add(b);
                        idx.Add(e);
                        idx.Add(a);
                        idx.Add(e);
                        idx.Add(c);
                    }
                }

                g.Positions = pos;
                g.Normals = nrm;
                g.TextureCoordinates = uv;
                g.TriangleIndices = idx;
                g.Freeze();
                gm.Geometry = g;
                return;
            }
        }

        /// <summary>Every frame: listener, propeller blur, lights.</summary>
        private void ReadyFrame()
        {
            var s = Session;
            bool live = s != null && s.State != Simulation.SimState.Stopped;
            if (s != null)
            {
                var c = _camera.Position;
                var f = _camera.LookDirection;
                var u = _camera.UpDirection;
                s.SetListener(new V3(c.X, c.Y, c.Z), new V3(f.X, f.Y, f.Z), new V3(u.X, u.Y, u.Z));
            }

            foreach (var it in _items.Values)
            {
                if (ReadyModels.VisualOf(it.Model) is not { Blur: not null } rv) continue;
                double w = live && s!.Physics != null ? s.Physics.HubSpeed(it.Object.Id) : 0;
                rv.ShowSpeed(double.IsNaN(w) ? 0 : w * 60 / (2 * Math.PI));
            }

            if (_sources.Count == 0) return;
            double room = Math.Max(0, Math.Min(1, World?.RoomLight ?? 1));
            // Illuminance that shows as full colour: the eye adapts to a dark room.
            double eRef = 250 * Math.Max(0.25, room);
            foreach (var src in _sources)
            {
                var (r, g, b, lm) = live ? s!.LightOutput(src.It.Object) : (0, 0, 0, 0);
                src.Lumens = lm;
                double max = Math.Max(r, Math.Max(g, b));
                src.Colour = max > 0 ? Color.FromRgb((byte)(255 * r / max), (byte)(255 * g / max), (byte)(255 * b / max)) : Colors.Black;
                src.World = src.It.Current.Transform(src.Local);
                if (src.Rgb && src.It.Model.Instance?.Light is { Brush: SolidColorBrush lens })
                {
                    // Three dies mix in the diffused lens.
                    double k = Math.Min(1, lm / 1.5);
                    lens.Color = Color.FromRgb((byte)(src.Colour.R * k), (byte)(src.Colour.G * k), (byte)(src.Colour.B * k));
                }

                bool on = lm > 0.003;
                if (on != src.Shown)
                {
                    src.Shown = on;
                    if (on) ((Model3DGroup)_glowVisual.Content).Children.Add(src.Halo);
                    else ((Model3DGroup)_glowVisual.Content).Children.Remove(src.Halo);
                }

                UpdatePool(src, on, eRef, room);
                if (!on) continue;
                double glow = Math.Min(1, (0.3 + 0.25 * Math.Log10(1 + lm)) * (1.25 - 0.6 * room));
                double radius = src.Size * (1 + 0.45 * Math.Log10(1 + lm));
                var m = Matrix3D.Identity;
                m.Scale(new Vector3D(radius, radius, radius));
                m.Translate(new Vector3D(src.World.X, src.World.Y, src.World.Z));
                src.Place.Matrix = m;
                src.Inner.Color = Scale(src.Colour, 0.55 * glow);
                src.Outer.Color = Scale(src.Colour, 0.16 * glow);
            }

            var order = _sources.Where(x => x.Lumens > 0.003).OrderByDescending(x => x.Lumens).ToList();
            for (int i = 0; i < _pool.Count; i++)
            {
                var pl = _pool[i];
                if (i >= order.Count)
                {
                    if (pl.Color != Colors.Black) pl.Color = Colors.Black;
                    continue;
                }

                var src = order[i];
                // Luminous intensity of a source radiating into roughly a hemisphere; E = I / d².
                double intensity = src.Lumens / (2 * Math.PI);
                pl.Position = src.World;
                pl.Color = src.Colour;
                pl.ConstantAttenuation = 1;
                pl.QuadraticAttenuation = eRef / Math.Max(1e-4, intensity);
            }
        }

        /// <summary>
        /// Light falling on the floor below a source: E(r) = I·h / (h² + r²)^1.5. The rings sit where E drops to
        /// E_ref, E_ref/2, E_ref/4 …, each adding its share, so the pool is bright under the light and fades in steps.
        /// The floor is drawn dark; the pool shows the light a normal (≈ 50 % reflective) floor would send back.
        /// </summary>
        private void UpdatePool(LightSource src, bool on, double eRef, double room)
        {
            double h = Math.Max(0.003, src.World.Z);
            double intensity = src.Lumens / (2 * Math.PI);
            bool show = on && intensity * h / Math.Pow(h * h, 1.5) > eRef * RingLevel[RingLevel.Length - 1];
            var group = (Model3DGroup)_glowVisual.Content;
            if (show != src.PoolShown)
            {
                src.PoolShown = show;
                if (show) group.Children.Insert(0, src.Pool);
                else group.Children.Remove(src.Pool);
            }

            if (!show) return;
            src.PoolAt.OffsetX = src.World.X;
            src.PoolAt.OffsetY = src.World.Y;
            double level = 0.3 * (1.1 - 0.7 * room);
            for (int k = 0; k < src.Rings.Count; k++)
            {
                var (brush, size, _) = src.Rings[k];
                double target = eRef * RingLevel[k];
                double reach = Math.Pow(intensity * h / target, 2.0 / 3.0) - h * h;
                double r = reach > 0 ? Math.Min(1.0, Math.Sqrt(reach)) : 0;
                size.ScaleX = size.ScaleY = Math.Max(1e-5, r);
                double share = RingLevel[k] - (k + 1 < RingLevel.Length ? RingLevel[k + 1] : 0);
                brush.Color = Scale(src.Colour, r > 0 ? share * level : 0);
            }
        }

        private static Color Scale(Color c, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
        }

        /// <summary>Snaps a dropped ready wheel / propeller onto the nearest free shaft or servo horn within 3 cm.</summary>
        private void TrySnapDropped(string id)
        {
            var world = World;
            var o = world?.Find(id);
            var spec = ReadyParts.Of(o);
            if (world == null || o == null || spec == null || !spec.MountsOnShaft) return;
            if (world.Joints.Any(j => j.ObjectA == id && !string.IsNullOrEmpty(j.PortB))) return;
            var hub = o.Pose.ToWorld(spec.Kind == ReadyPartKind.Propeller ? new V3(0, 0, spec.Width / 2) : new V3(0, 0, spec.Radius));
            string? best = null, port = null;
            double bestD = 0.03;
            foreach (var it in _items.Values)
            {
                if (it.Object.Id == id || it.Model.Port is not { } pf) continue;
                if (world.Joints.Any(j => j.ObjectB == it.Object.Id && j.PortB == pf.Name)) continue;
                var tip = it.Placed.Transform(pf.Pivot);
                double dd = Math.Sqrt((tip.X - hub.X) * (tip.X - hub.X) + (tip.Y - hub.Y) * (tip.Y - hub.Y) + (tip.Z - hub.Z) * (tip.Z - hub.Z));
                if (dd < bestD)
                {
                    bestD = dd;
                    best = it.Object.Id;
                    port = pf.Name;
                }
            }

            if (best != null && port != null) ReadySnapRequested?.Invoke(id, best, port);
        }
    }
}
