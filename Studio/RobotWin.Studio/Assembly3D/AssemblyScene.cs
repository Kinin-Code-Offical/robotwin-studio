using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;
using static RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.Assembly3D
{
    /// <summary>A part drawn on the vehicle whose look follows its temperature and failure.</summary>
    public sealed class HeatVisual
    {
        public HeatVisual(string refDes, Point3D local, EmissiveMaterial glow, DiffuseMaterial body, Color baseColor)
        {
            RefDes = refDes;
            Local = local;
            Glow = glow;
            Body = body;
            BaseColor = baseColor;
        }

        public string RefDes { get; }

        /// <summary>Centre in the body frame (for smoke and fire emitters).</summary>
        public Point3D Local { get; }

        public EmissiveMaterial Glow { get; }
        public DiffuseMaterial Body { get; }
        public Color BaseColor { get; }
        public bool Charred { get; set; }
    }

    /// <summary>
    /// Builds the 3D scene of an assembly — arena, chassis or frame, motors, wheels,
    /// propellers, boards, batteries and sensors — and moves it every frame from
    /// the rigid-body state.
    /// </summary>
    public sealed partial class AssemblyScene // [phys] partial: Assembly3D/AssemblyScene.Rest.cs
    {
        private readonly MatrixTransform3D _bodyTransform = new MatrixTransform3D();
        private readonly TranslateTransform3D _shadowTransform = new TranslateTransform3D();
        private readonly TranslateTransform3D _comOffset = new TranslateTransform3D();
        private readonly DiffuseMaterial _shadowMaterial;
        private readonly List<(AxisAngleRotation3D Rotation, int Index, bool IsProp)> _spinners = new List<(AxisAngleRotation3D, int, bool)>();
        private readonly List<(Model3DGroup Prop, Model3D Blades, GeometryModel3D Blur, int Index)> _props = new List<(Model3DGroup, Model3D, GeometryModel3D, int)>();
        private readonly Dictionary<string, HeatVisual> _heat = new Dictionary<string, HeatVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string RefDes, DiffuseMaterial Material, string Style)> _screens = new List<(string, DiffuseMaterial, string)>();

        public AssemblyScene(AssemblyDocument assembly, Schematic schematic, PartLibrary library)
        {
            Assembly = assembly;
            Arena = assembly.CreateArena();
            Root = new Model3DGroup();
            Root.Children.Add(new AmbientLight(Color.FromRgb(0x5A, 0x5A, 0x62)));
            Root.Children.Add(new DirectionalLight(Color.FromRgb(0xE8, 0xE6, 0xE0), new Vector3D(-0.4, 0.6, -1)));
            Root.Children.Add(new DirectionalLight(Color.FromRgb(0x40, 0x44, 0x50), new Vector3D(0.6, -0.4, -0.3)));
            BuildArena();
            Vehicle = new Model3DGroup { Transform = _bodyTransform };
            if (assembly.Kind == VehicleKind.Quadcopter) BuildQuad(schematic, library);
            else if (assembly.Kind == VehicleKind.Rover) BuildRover(schematic, library);
            if (assembly.ChassisMesh != null)
            {
                try
                {
                    var model = MeshModel.FromEmbedded(assembly.ChassisMesh);
                    Vehicle.Children.Insert(0, MeshVisual.Build(model, MeshVisual.MaterialColor(assembly.ChassisMaterial)));
                }
                catch (System.IO.InvalidDataException)
                {
                }
            }

            // Everything above is in the chassis frame; the physics body origin is the centre of mass.
            var chassisFrame = new Model3DGroup { Transform = _comOffset };
            while (Vehicle.Children.Count > 0)
            {
                var c = Vehicle.Children[0];
                Vehicle.Children.RemoveAt(0);
                chassisFrame.Children.Add(c);
            }

            Vehicle.Children.Add(chassisFrame);
            // Soft contact shadow under the vehicle (follows x/y, fades with height).
            var shadowBrush = new RadialGradientBrush();
            shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0, 0, 0), 0));
            shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(90, 0, 0, 0), 0.55));
            shadowBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1));
            _shadowMaterial = new DiffuseMaterial(shadowBrush);
            double sr = assembly.Kind == VehicleKind.Quadcopter ? assembly.ArmLength + assembly.PropDiameter / 2 : Math.Max(assembly.Length, assembly.Width + 0.06) * 0.62;
            var shadow = new GeometryModel3D(TexturedTop(0, 0, 0.0008, sr * 2, sr * 2), _shadowMaterial) { Transform = _shadowTransform };
            Root.Children.Add(shadow);
            Root.Children.Add(Vehicle);
            RideHeight = assembly.Kind == VehicleKind.Quadcopter ? 0.012 : assembly.WheelRadius + 0.012;
            PlaceAtStart();
        }

        public AssemblyDocument Assembly { get; }
        public Arena Arena { get; }
        public Model3DGroup Root { get; }
        public Model3DGroup Vehicle { get; }
        public double RideHeight { get; }
        public IReadOnlyDictionary<string, HeatVisual> Heat => _heat;

        public Point3D BodyPosition { get; private set; }
        public Matrix3D BodyMatrix { get; private set; } = Matrix3D.Identity;

        public void PlaceAtStart()
        {
            _comOffset.OffsetX = _comOffset.OffsetY = _comOffset.OffsetZ = 0;
            double x = Assembly.StartX ?? Arena.StartX, y = Assembly.StartY ?? Arena.StartY;
            double yaw = Assembly.StartYaw.HasValue ? Assembly.StartYaw.Value * Math.PI / 180 : Arena.StartYaw;
            SetPose(new Vec3(x, y, Arena.GroundHeight(x, y) + RideHeight), Quat.FromYaw(yaw)); // [phys] on the floor or terrain where it starts
            ResetMotion(); // [phys] wheels, props and pushed objects back at rest
        }

        public void SetPose(Vec3 position, Quat q)
        {
            // Rotation matrix from the quaternion (row-vector convention used by WPF).
            double w = q.W, x = q.X, y = q.Y, z = q.Z;
            var m = new Matrix3D(
                1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y), 0,
                2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x), 0,
                2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y), 0,
                position.X, position.Y, position.Z, 1);
            _bodyTransform.Matrix = m;
            BodyMatrix = m;
            _shadowTransform.OffsetX = position.X;
            _shadowTransform.OffsetY = position.Y;
            double lift = Math.Max(0, position.Z - RideHeight);
            _shadowMaterial.Brush.Opacity = Math.Max(0.15, 1 - lift / 1.5);
            BodyPosition = new Point3D(position.X, position.Y, position.Z);
        }

        public Point3D ToWorld(Point3D local) => BodyMatrix.Transform(local);

        /// <summary>Moves wheels and propellers and updates heat glow from the running system.</summary>
        public void Update(MechatronicSystem mech, Func<string, double> temperature, Func<string, bool> failed)
        {
            _comOffset.OffsetX = -mech.CenterOfMass.X;
            _comOffset.OffsetY = -mech.CenterOfMass.Y;
            _comOffset.OffsetZ = -mech.CenterOfMass.Z;
            SetPose(mech.Body.Position, mech.Body.Orientation);
            UpdateBodies(mech);
            foreach (var (rot, index, isProp) in _spinners)
            {
                if (isProp)
                {
                    if (index < mech.Props.Count) rot.Angle = mech.Props[index].Prop.Angle * 180 / Math.PI % 360;
                }
                else if (index < mech.Wheels.Count)
                {
                    rot.Angle = mech.Wheels[index].Wheel.Angle * 180 / Math.PI % 360;
                }
            }

            foreach (var (prop, blades, blur, index) in _props)
            {
                if (index >= mech.Props.Count) continue;
                var link = mech.Props[index];
                double w = Math.Abs(link.Prop.AngularVelocity);
                bool fast = w > 160 && !link.Broken;
                bool shown = prop.Children.Contains(blades);
                if (fast && shown) prop.Children.Remove(blades);
                else if (!fast && !shown) prop.Children.Add(blades);
                blur.Material = fast ? _blurMaterial[index] : Transparent;
                blur.BackMaterial = blur.Material;
            }

            UpdateHeat(temperature, failed);
        }

        /// <summary>Shows the live content of OLED/LCD modules mounted on the vehicle.</summary>
        public void UpdateScreens(Func<string, object?> displayOf)
        {
            foreach (var (refDes, material, style) in _screens)
            {
                System.Windows.Media.Imaging.BitmapSource? picture = displayOf(refDes) switch
                {
                    RobotTwin.CoreSim.Firmware.Devices.Ssd1306 o => Editor.DisplayRenderer.Oled(o, style),
                    RobotTwin.CoreSim.Firmware.Devices.Pcf8574Lcd l => Editor.DisplayRenderer.Lcd(l, style),
                    _ => null
                };
                if (picture == null)
                {
                    if (material.Brush is not SolidColorBrush) material.Brush = new SolidColorBrush(Rgb(0x050506));
                    continue;
                }

                if (material.Brush is ImageBrush ib && ReferenceEquals(ib.ImageSource, picture)) continue;
                var brush = new ImageBrush(picture);
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
                material.Brush = brush;
            }
        }

        public void UpdateHeat(Func<string, double> temperature, Func<string, bool> failed)
        {
            foreach (var h in _heat.Values)
            {
                double t = temperature(h.RefDes);
                bool dead = failed(h.RefDes);
                if (dead && !h.Charred)
                {
                    h.Charred = true;
                    ((SolidColorBrush)h.Body.Brush).Color = Lerp(h.BaseColor, Rgb(0x15120F), 0.8);
                }
                else if (!dead && h.Charred)
                {
                    h.Charred = false;
                    ((SolidColorBrush)h.Body.Brush).Color = h.BaseColor;
                }

                ((SolidColorBrush)h.Glow.Brush).Color = HeatColor(double.IsNaN(t) ? 25 : t);
            }
        }

        /// <summary>Black body-ish glow: nothing below 70 °C, dull red at 120 °C, orange-yellow at 250 °C.</summary>
        public static Color HeatColor(double t)
        {
            double k = Math.Max(0, Math.Min(1, (t - 70) / 180));
            if (k <= 0) return Colors.Black;
            byte r = (byte)(255 * Math.Min(1, k * 1.8));
            byte g = (byte)(200 * Math.Max(0, k - 0.45) / 0.55);
            byte b = (byte)(90 * Math.Max(0, k - 0.8) / 0.2);
            return Color.FromRgb(r, g, b);
        }

        private static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private static readonly Material Transparent = CreateTransparent();

        private static Material CreateTransparent()
        {
            var m = new DiffuseMaterial(Brushes.Transparent);
            m.Freeze();
            return m;
        }
        private readonly Material[] _blurMaterial = new Material[4];

        // ------------------------------------------------------------------ arena

        private void BuildArena()
        {
            var a = Arena;
            double margin = 0.6;
            double w = a.Width + 2 * margin, d = a.Depth + 2 * margin;
            var floor = new GeometryModel3D(TexturedTop(0, 0, 0, w, d), new DiffuseMaterial(new ImageBrush(RenderFloor(a, margin))));
            Root.Children.Add(floor);
            // A large dark ground plane so the horizon is not empty.
            Root.Children.Add(Model(Box(0, 0, -0.012, 40, 40, 0.02), Matte(Rgb(0x141416)))); // [phys] top 2 mm under the floor: nothing seems to hover past the arena edge

            if (a.Walls)
            {
                var wall = Matte(Rgb(0x3A3A40));
                double h = 0.08, t = 0.02;
                Root.Children.Add(Model(Box(0, a.Depth / 2 + t / 2, h / 2, a.Width + 2 * t, t, h), wall));
                Root.Children.Add(Model(Box(0, -a.Depth / 2 - t / 2, h / 2, a.Width + 2 * t, t, h), wall));
                Root.Children.Add(Model(Box(a.Width / 2 + t / 2, 0, h / 2, t, a.Depth, h), wall));
                Root.Children.Add(Model(Box(-a.Width / 2 - t / 2, 0, h / 2, t, a.Depth, h), wall));
            }

            foreach (var o in a.Obstacles)
            {
                Root.Children.Add(Model(Box(o.X, o.Y, o.Height / 2, o.SizeX, o.SizeY, o.Height), Matte(Rgb(0x8B6B4A), 0.1)));
            }

            foreach (var mo in a.MeshObstacles) Root.Children.Add(MeshVisual.Build(mo.Mesh, Rgb(0x9A8A78)));

            if (a.Terrain != null) Root.Children.Add(BuildTerrain(a));

            // Ramps and posts (static) are drawn once; movable objects are previewed at their start
            // pose until the simulation supplies live bodies.
            var preview = new World3D(a);
            _ = preview.Movables;
            foreach (var b in preview.Scene.Bodies)
            {
                if (b == preview.Body || Equals(b.Tag, World3D.LegacyArenaTag)) continue;
                var visual = BodyVisual(b);
                if (visual == null) continue;
                if (b.IsStatic) Root.Children.Add(visual);
                else _previewMovables.Children.Add(visual);
            }

            Root.Children.Add(_previewMovables);
        }

        private readonly Model3DGroup _previewMovables = new Model3DGroup();
        private readonly Dictionary<SceneBody, MatrixTransform3D> _bodyVisuals = new Dictionary<SceneBody, MatrixTransform3D>();

        /// <summary>A drawable model of a scene body's collision shape, placed at its current pose.</summary>
        private static Model3D? BodyVisual(SceneBody b)
        {
            var shape = b.Shape;
            if (shape == null) return null;
            MeshModel mesh;
            switch (shape.Kind)
            {
                case ShapeKind.Box:
                    mesh = MeshModel.CreateBox(shape.HalfExtents * 2);
                    break;
                case ShapeKind.Sphere:
                    mesh = MeshModel.CreateSphere(shape.Radius, 24, 12);
                    break;
                case ShapeKind.Cylinder:
                    mesh = MeshModel.CreateCylinder(shape.Radius, shape.HalfHeight * 2, 24);
                    break;
                case ShapeKind.Mesh when shape.Mesh != null:
                    mesh = shape.Mesh;
                    break;
                default:
                    if (shape.Vertices.Count < 4) return null;
                    var hull = ConvexHull.Compute(shape.Vertices);
                    if (hull.IsDegenerate) return null;
                    mesh = hull.ToMesh();
                    break;
            }

            var color = b.Material switch
            {
                "rubber" or "foam" => Rgb(0x2A2A2E),
                "steel" or "aluminium" or "brass" => Rgb(0xA8ADB4),
                "paper" => Rgb(0xC9A46A),
                "fr4" => Rgb(0x1E5A32),
                "pla" or "petg" or "abs" or "plastic" or "nylon" => Rgb(0xD9DCE2),
                _ => Rgb(0xA67C52)
            };
            var model = MeshVisual.Build(mesh, color);
            model.Transform = new MatrixTransform3D(PoseMatrix(b.Position, b.Orientation));
            return model;
        }

        private static Matrix3D PoseMatrix(Vec3 p, Quat q)
        {
            double w = q.W, x = q.X, y = q.Y, z = q.Z;
            return new Matrix3D(
                1 - 2 * (y * y + z * z), 2 * (x * y + w * z), 2 * (x * z - w * y), 0,
                2 * (x * y - w * z), 1 - 2 * (x * x + z * z), 2 * (y * z + w * x), 0,
                2 * (x * z + w * y), 2 * (y * z - w * x), 1 - 2 * (x * x + y * y), 0,
                p.X, p.Y, p.Z, 1);
        }

        /// <summary>Moves (and creates on first sight) the visuals of the simulation's movable bodies, links and torn-off parts.</summary>
        private void UpdateBodies(MechatronicSystem mech)
        {
            if (_previewMovables.Children.Count > 0) _previewMovables.Children.Clear();
            foreach (var b in mech.World.Scene.Bodies)
            {
                if (b.IsStatic || b == mech.Body) continue;
                if (!_bodyVisuals.TryGetValue(b, out var transform))
                {
                    var visual = BodyVisual(b);
                    if (visual == null) continue;
                    transform = (MatrixTransform3D)visual.Transform;
                    _bodyVisuals[b] = transform;
                    Root.Children.Add(visual);
                }

                transform.Matrix = PoseMatrix(b.Position, b.Orientation);
            }
        }

        private static GeometryModel3D BuildTerrain(Arena a)
        {
            var t = a.Terrain!;
            int nx = 80, ny = Math.Max(2, (int)(80 * a.Depth / a.Width));
            var mesh = new MeshGeometry3D();
            var (lo, hi) = t.HeightRange;
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                double x = -a.Width / 2 + a.Width * i / nx, y = -a.Depth / 2 + a.Depth * j / ny;
                mesh.Positions.Add(new Point3D(x, y, t.Height(x, y) + 0.0005));
                var n = t.Normal(x, y);
                mesh.Normals.Add(new Vector3D(n.X, n.Y, n.Z));
                double k = double.IsInfinity(lo) || hi - lo < 1e-9 ? 0.5 : (t.Height(x, y) - lo) / (hi - lo);
                mesh.TextureCoordinates.Add(new Point(k, 0.5));
            }

            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a0 = j * (nx + 1) + i, a1 = a0 + 1, b0 = a0 + nx + 1, b1 = b0 + 1;
                mesh.TriangleIndices.Add(a0);
                mesh.TriangleIndices.Add(a1);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(a0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(b0);
            }

            var brush = new LinearGradientBrush(Rgb(0x5E6B4E), Rgb(0xB8A77C), new Point(0, 0.5), new Point(1, 0.5));
            var material = new DiffuseMaterial(brush);
            return new GeometryModel3D(mesh, material) { BackMaterial = material };
        }

        private static BitmapSource RenderFloor(Arena a, double margin)
        {
            double w = a.Width + 2 * margin, d = a.Depth + 2 * margin;
            double ppm = Math.Min(900, 4096 / Math.Max(w, d));
            int pw = (int)(w * ppm), ph = (int)(d * ppm);
            var dv = new DrawingVisual();
            using (var g = dv.RenderOpen())
            {
                Point P(double x, double y) => new Point((x + w / 2) * ppm, (d / 2 - y) * ppm);
                g.DrawRectangle(new SolidColorBrush(Rgb(0x1B1B1E)), null, new Rect(0, 0, pw, ph));
                bool paper = a.Lines.Count > 0;
                var inner = new Rect(P(-a.Width / 2, a.Depth / 2), P(a.Width / 2, -a.Depth / 2));
                g.DrawRectangle(new SolidColorBrush(paper ? Rgb(0xE9E7E2) : Rgb(0x4A4A50)), null, inner);

                // Grid every 10 cm (finer reference for flying).
                var gridPen = new Pen(new SolidColorBrush(paper ? Rgb(0xD6D3CC) : Rgb(0x55555C)), Math.Max(1, ppm * 0.002));
                var majorPen = new Pen(new SolidColorBrush(paper ? Rgb(0xC8C4BB) : Rgb(0x6A6A72)), Math.Max(1.5, ppm * 0.004));
                for (double x = -a.Width / 2; x <= a.Width / 2 + 1e-9; x += 0.1)
                {
                    bool major = Math.Abs(Math.Round(x * 2) - x * 2) < 1e-6;
                    g.DrawLine(major ? majorPen : gridPen, P(x, -a.Depth / 2), P(x, a.Depth / 2));
                }

                for (double y = -a.Depth / 2; y <= a.Depth / 2 + 1e-9; y += 0.1)
                {
                    bool major = Math.Abs(Math.Round(y * 2) - y * 2) < 1e-6;
                    g.DrawLine(major ? majorPen : gridPen, P(-a.Width / 2, y), P(a.Width / 2, y));
                }

                var tape = new Pen(new SolidColorBrush(Rgb(0x101010)), a.LineWidth * ppm) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                foreach (var line in a.Lines)
                {
                    var geo = new StreamGeometry();
                    using (var c = geo.Open())
                    {
                        c.BeginFigure(P(line[0].X, line[0].Y), false, false);
                        for (int i = 1; i < line.Count; i++) c.LineTo(P(line[i].X, line[i].Y), true, true);
                    }

                    g.DrawGeometry(null, tape, geo);
                }

                // Start marker
                var s = P(a.StartX, a.StartY);
                g.DrawEllipse(null, new Pen(new SolidColorBrush(Rgb(0x6E9BFF)), ppm * 0.006), s, ppm * 0.05, ppm * 0.05);
            }

            var bmp = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        // ------------------------------------------------------------------ vehicles

        private void BuildRover(Schematic schematic, PartLibrary library)
        {
            var a = Assembly;
            double deck = a.DeckZ;
            // Acrylic deck plate
            if (a.ChassisMesh == null) Vehicle.Children.Add(Model(Box(0, 0, deck / 2 - 0.002, a.Length, a.Width, 0.003), Matte(Rgb(0x2B3138, 235), 0.25)));
            // Ball caster
            double rh = a.WheelRadius + 0.012;
            Vehicle.Children.Add(Model(Box(a.Length / 2 - 0.025, 0, -rh / 2 + 0.002, 0.012, 0.012, rh - 0.012), Matte(Rgb(0x8A8A90), 0.4)));
            Vehicle.Children.Add(Model(Sphere(0.006, a.Length / 2 - 0.025, 0, -rh + 0.006), Matte(Rgb(0xC8C8CC), 0.8)));

            double halfTrack = a.Width / 2 + a.WheelWidth / 2 + 0.003;
            for (int i = 0; i < 2; i++)
            {
                string role = i == 0 ? "L" : "R";
                double side = i == 0 ? 1 : -1;
                var mount = a.MotorFor(role);
                double wz = a.WheelRadius - rh;

                // Gear motor under the deck: the real model of the motor part (TT, 25GA-370, N20 …),
                // its output shaft on the wheel axle.
                var motor = PartInstance(mount?.RefDes, schematic, library) ?? DefaultMotor();
                var shaft = motor.Find("shaft");
                var axis = shaft?.Axis ?? new Vector3D(0, 1, 0);
                var pivot = shaft?.Pivot ?? new Point3D();
                bool doubleShaft = shaft != null && Math.Abs(axis.Y) > 0.9;
                var toWheel = RotationOnto(axis, new Vector3D(0, doubleShaft ? Math.Sign(axis.Y) : side, 0));
                var p0 = toWheel.Transform(pivot);
                // Shaft centre (or front face) just inside the chassis edge, at axle height.
                double inset = doubleShaft ? 9.4 : 2.0;
                var m = Models3D.Mx.T(-p0.X, -p0.Y, -p0.Z) * toWheel * Models3D.Mx.S(0.001) * Models3D.Mx.T(a.AxleX, side * (a.Width / 2 - inset * 0.001), wz);
                var motorGroup = new Model3DGroup { Transform = new MatrixTransform3D(m) };
                motorGroup.Children.Add(motor.Root);
                Vehicle.Children.Add(motorGroup);
                if (mount != null && !string.IsNullOrEmpty(mount.RefDes))
                {
                    _heat[mount.RefDes] = MakeHeat(mount.RefDes, motor, m);
                }

                // Wheel: rubber tyre on a hub, spinning about the body Y axis.
                var rot = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
                var wheel = new Model3DGroup();
                wheel.Children.Add(Models3D.ComponentModels.Wheel(a.WheelRadius * 2000, a.WheelWidth * 1000));
                var tg = new Transform3DGroup();
                tg.Children.Add(new ScaleTransform3D(0.001, 0.001, 0.001));
                tg.Children.Add(new RotateTransform3D(rot));
                tg.Children.Add(new TranslateTransform3D(a.AxleX, side * halfTrack, wz));
                wheel.Transform = tg;
                Vehicle.Children.Add(wheel);
                _spinners.Add((rot, i, false));
            }

            AddModules(schematic, library, deck);
            AddSensors(schematic, library, deck);
        }

        private void BuildQuad(Schematic schematic, PartLibrary library)
        {
            var a = Assembly;
            double armW = 0.006;
            var frame = Matte(Rgb(0x2E2E33), 0.2);
            foreach (double ang in new[] { 45.0, -45.0 })
            {
                var arm = Model(Box(0, 0, 0.0, a.ArmLength * 2 + 0.01, armW, 0.003), frame);
                arm.Transform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), ang));
                Vehicle.Children.Add(arm);
            }

            double[] angles = { 45, -45, -135, 135 };
            for (int i = 0; i < 4; i++)
            {
                double t = angles[i] * Math.PI / 180;
                double x = a.ArmLength * Math.Cos(t), y = a.ArmLength * Math.Sin(t);
                var mount = a.MotorFor("M" + (i + 1));

                // Coreless motor (the motor part's own model) in its arm clip.
                var motor = PartInstance(mount?.RefDes, schematic, library) ?? DefaultCoreless();
                // Leads (model +x) towards the flight controller in the middle.
                var mm = Models3D.Mx.Rz(Math.Atan2(-y, -x) * 180 / Math.PI) * Models3D.Mx.S(0.001) * Models3D.Mx.T(x, y, -0.0085);
                var motorGroup = new Model3DGroup { Transform = new MatrixTransform3D(mm) };
                motorGroup.Children.Add(motor.Root);
                Vehicle.Children.Add(motorGroup);
                Vehicle.Children.Add(Model(Cylinder(0.0055, 0.006, 2, 20, x, y, -0.004), Matte(Rgb(0x111114))));
                // Landing leg
                Vehicle.Children.Add(Model(Box(x * 0.92, y * 0.92, -0.0065, 0.003, 0.003, 0.011), Matte(Rgb(0x111114))));
                if (mount != null && !string.IsNullOrEmpty(mount.RefDes)) _heat[mount.RefDes] = MakeHeat(mount.RefDes, motor, mm);

                // Propeller: blades when slow, translucent disc when spinning fast.
                bool front = i == 0 || i == 1;
                var bladeColor = front ? Rgb(0xE5484D) : Rgb(0x2B2B30);
                _blurMaterial[i] = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(70, bladeColor.R, bladeColor.G, bladeColor.B)));
                var rot = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);
                var prop = new Model3DGroup();
                var blades = Models3D.ComponentModels.Propeller(a.PropDiameter * 1000, 2, front ? 0xE5484Du : 0x2B2B30u);
                blades.Transform = new MatrixTransform3D(Models3D.Mx.S(0.001) * Models3D.Mx.T(0, 0, 0.0125));
                var blur = Model(Disc(a.PropDiameter / 2, 0.0125), Transparent);
                prop.Children.Add(blades);
                var tg = new Transform3DGroup();
                tg.Children.Add(new RotateTransform3D(rot));
                tg.Children.Add(new TranslateTransform3D(x, y, 0));
                prop.Transform = tg;
                Vehicle.Children.Add(prop);
                var blurModel = blur;
                blurModel.Transform = new TranslateTransform3D(x, y, 0);
                Vehicle.Children.Add(blurModel);
                _spinners.Add((rot, i, true));
                _props.Add((prop, blades, blurModel, i));
            }

            AddModules(schematic, library, 0.003);
            AddSensors(schematic, library, 0.003);
        }

        // ------------------------------------------------------------------ boards, batteries, modules

        private void AddModules(Schematic schematic, PartLibrary library, double deck)
        {
            double stackZ = deck;
            foreach (var m in Assembly.Modules)
            {
                var part = schematic.FindPart(m.RefDes);
                var def = part != null ? library.Find(part.PartId) : null;
                if (part == null || def == null) continue;
                var p = schematic.ResolveParameters(library, part);
                var inst = Models3D.ComponentModels.Create(def, p);
                var b = inst.Bounds;
                double height = b.SizeZ / 1000;
                double z = deck;
                if (Assembly.Kind == VehicleKind.Quadcopter)
                {
                    if (def.Symbol == SymbolKind.Battery) z = -0.004 - height;
                    else
                    {
                        z = stackZ;
                        stackZ += height + 0.002;
                    }
                }

                // Real model of the board / battery / module, centred on its mount and resting on the deck.
                var mx = Models3D.Mx.T(-(b.X + b.SizeX / 2), -(b.Y + b.SizeY / 2), -b.Z) * Models3D.Mx.S(0.001) *
                         Models3D.Mx.Rz(m.Rotation) * Models3D.Mx.T(m.X, m.Y, z);
                var group = new Model3DGroup { Transform = new MatrixTransform3D(mx) };
                group.Children.Add(inst.Root);
                Vehicle.Children.Add(group);
                if (inst.Screen != null) _screens.Add((m.RefDes, inst.Screen, inst.ScreenStyle));
                _heat[m.RefDes] = MakeHeat(m.RefDes, inst, mx);
            }

            // The user's own PCB, populated with its part models, on stand-offs above the deck.
            var board = schematic.Board;
            if (board != null && board.Components.Count > 0 && Assembly.Kind == VehicleKind.Rover)
            {
                var mounted = new HashSet<string>(Assembly.Modules.Select(x => x.RefDes).Concat(Assembly.Sensors.Select(x => x.RefDes)).Concat(Assembly.Motors.Select(x => x.RefDes)),
                    StringComparer.OrdinalIgnoreCase);
                var scene = Models3D.BoardModels.BuildScene(board, schematic, library, r => mounted.Contains(r));
                double bw = board.Width / 1000, bh = board.Height / 1000, z = deck + 0.0132;
                double cx = -Assembly.Length / 2 + bw / 2 + 0.01;
                var mx = Models3D.Mx.S(0.001) * Models3D.Mx.T(cx, 0, z);
                scene.Root.Transform = new MatrixTransform3D(mx);
                Vehicle.Children.Add(scene.Root);
                var brass = Matte(Rgb(0xC9A24A), 0.7);
                foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                    Vehicle.Children.Add(Model(Cylinder(0.0022, z - deck, 2, 6, cx + sx * (bw / 2 - 0.0035), sy * (bh / 2 - 0.0035), (z + deck) / 2), brass));
                foreach (var pc in scene.Parts)
                {
                    var inst = pc.Instance;
                    if (inst.Screen != null) _screens.Add((pc.RefDes, inst.Screen, inst.ScreenStyle));
                    if (inst.Body != null && inst.Glow != null && !_heat.ContainsKey(pc.RefDes)) _heat[pc.RefDes] = MakeHeat(pc.RefDes, inst, pc.ToBoard * mx);
                }
            }
        }

        private void AddSensors(Schematic schematic, PartLibrary library, double deck)
        {
            foreach (var s in Assembly.Sensors)
            {
                var mount = new Point3D(s.X, s.Y, s.Z + deck);
                var inst = PartInstance(s.RefDes, schematic, library);
                if (inst == null)
                {
                    AddSimpleSensor(s, deck);
                    continue;
                }

                var b = inst.Bounds;
                var c = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
                Matrix3D orient;
                Point3D anchor;
                switch (s.Kind)
                {
                    case SensorKind.Line:
                        // Module upside down, the reflective sensor at its front end looking at the floor.
                        orient = Models3D.Mx.Rx(180);
                        anchor = new Point3D(b.X + b.SizeX - 3, c.Y, b.Z + b.SizeZ);
                        break;
                    case SensorKind.Ultrasonic:
                        // The transducers face the model's −x: turn them forward; the module stands on its pins at the mount.
                        orient = Models3D.Mx.Rz(180);
                        anchor = new Point3D(c.X, c.Y, b.Z);
                        break;
                    case SensorKind.Obstacle:
                        orient = Matrix3D.Identity;
                        anchor = new Point3D(b.X + b.SizeX - 6, c.Y, c.Z);
                        break;
                    default:
                        orient = Matrix3D.Identity;
                        anchor = new Point3D(c.X, c.Y, b.Z);
                        break;
                }

                var mx = Models3D.Mx.T(-anchor.X, -anchor.Y, -anchor.Z) * orient * Models3D.Mx.S(0.001) * Models3D.Mx.Rz(s.Yaw) * Models3D.Mx.T(mount.X, mount.Y, mount.Z);
                var group = new Model3DGroup { Transform = new MatrixTransform3D(mx) };
                group.Children.Add(inst.Root);
                Vehicle.Children.Add(group);
                if (s.Kind == SensorKind.Line)
                {
                    // Bracket up to the deck.
                    double top = deck - 0.002;
                    Vehicle.Children.Add(Model(Box(s.X - 0.012, s.Y, (mount.Z + top) / 2, 0.002, 0.008, Math.Max(0.002, top - mount.Z)), Matte(Rgb(0x55555C))));
                }

                if (!_heat.ContainsKey(s.RefDes)) _heat[s.RefDes] = MakeHeat(s.RefDes, inst, mx);
            }
        }

        private void AddSimpleSensor(SensorMount s, double deck)
        {
            var group = new Model3DGroup();
            var pcb = Matte(Rgb(0x1D4F91), 0.15);
            group.Children.Add(Model(Box(0, 0, 0, 0.02, 0.016, 0.002), pcb));
            var tg = new Transform3DGroup();
            tg.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), s.Yaw)));
            tg.Children.Add(new TranslateTransform3D(s.X, s.Y, s.Z + deck));
            group.Transform = tg;
            Vehicle.Children.Add(group);
        }

        // ------------------------------------------------------------------ model helpers

        /// <summary>Standalone model of a schematic part (null when the part is unknown).</summary>
        private static Models3D.ComponentInstance? PartInstance(string? refDes, Schematic schematic, PartLibrary library)
        {
            if (string.IsNullOrEmpty(refDes)) return null;
            var part = schematic.FindPart(refDes);
            var def = part != null ? library.Find(part.PartId) : null;
            if (part == null || def == null) return null;
            return Models3D.ComponentModels.Create(def, schematic.ResolveParameters(library, part));
        }

        private static Models3D.ComponentInstance DefaultMotor()
        {
            var def = PartLibrary.Default.Get("dc-motor");
            return Models3D.ComponentModels.Create(def, PartLibrary.Default.ResolveParameters(def, PartLibrary.Default.FindPreset("motor-tt"), null));
        }

        private static Models3D.ComponentInstance DefaultCoreless()
        {
            var def = PartLibrary.Default.Get("dc-motor");
            return Models3D.ComponentModels.Create(def, PartLibrary.Default.ResolveParameters(def, PartLibrary.Default.FindPreset("motor-8520"), null));
        }

        /// <summary>Heat / char hooks of a model placed with <paramref name="toVehicle"/> (model mm → vehicle frame).</summary>
        private static HeatVisual MakeHeat(string refDes, Models3D.ComponentInstance inst, Matrix3D toVehicle)
        {
            var glow = inst.Glow ?? new EmissiveMaterial(new SolidColorBrush(Colors.Black));
            var body = inst.Body ?? new DiffuseMaterial(new SolidColorBrush(Colors.Transparent));
            return new HeatVisual(refDes, toVehicle.Transform(inst.HeatCentre), glow, body, inst.BaseColor);
        }

        /// <summary>Rotation taking direction a onto direction b.</summary>
        private static Matrix3D RotationOnto(Vector3D a, Vector3D b)
        {
            a.Normalize();
            b.Normalize();
            double dot = Math.Max(-1, Math.Min(1, Vector3D.DotProduct(a, b)));
            var axis = Vector3D.CrossProduct(a, b);
            if (axis.LengthSquared < 1e-12) return dot > 0 ? Matrix3D.Identity : Models3D.Mx.Rz(180);
            return Models3D.Mx.R(axis, Math.Acos(dot) * 180 / Math.PI);
        }
    }
    /// <summary>Size and colours of a board or module as it is drawn on the vehicle.</summary>
    public sealed class ModuleLook
    {
        public double Length { get; set; } = 0.02;
        public double Width { get; set; } = 0.02;
        public double Height { get; set; } = 0.0016;
        public Color Color { get; set; } = Mesh.Rgb(0x2F6B3A);
        public double Specular { get; set; } = 0.1;
        public List<(double X, double Y, double Z, double Sx, double Sy, double Sz, Color Color, double Specular)> Details { get; } =
            new List<(double, double, double, double, double, double, Color, double)>();

        private ModuleLook Add(double x, double y, double z, double sx, double sy, double sz, uint rgb, double spec = 0.1)
        {
            Details.Add((x, y, z + sz / 2, sx, sy, sz, Mesh.Rgb(rgb), spec));
            return this;
        }

        public static ModuleLook For(PartDefinition def, IReadOnlyDictionary<string, string> p, bool micro)
        {
            string id = def.Id;
            string model = p.TryGetValue("model", out var m) ? m : def.Name;
            if (id == "arduino-uno")
            {
                return new ModuleLook { Length = 0.0686, Width = 0.0533, Color = Mesh.Rgb(0x1B5E8F) }
                    .Add(0.004, -0.004, 0, 0.035, 0.009, 0.004, 0x18181A) // ATmega328P
                    .Add(-0.028, 0.012, 0, 0.016, 0.012, 0.011, 0xB9BBC0, 0.6) // USB-B
                    .Add(-0.029, -0.018, 0, 0.014, 0.009, 0.011, 0x18181A) // DC jack
                    .Add(0.0, 0.0245, 0, 0.055, 0.0025, 0.0085, 0x18181A)
                    .Add(0.0, -0.0245, 0, 0.045, 0.0025, 0.0085, 0x18181A);
            }

            if (id.IndexOf("nano", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ModuleLook { Length = 0.045, Width = 0.018, Color = Mesh.Rgb(0x1B5E8F) }
                    .Add(0.004, 0, 0, 0.007, 0.007, 0.0012, 0x18181A)
                    .Add(-0.02, 0, 0, 0.008, 0.008, 0.004, 0xB9BBC0, 0.6);
            }

            if (id.IndexOf("esp32", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new ModuleLook { Length = 0.0545, Width = 0.0279, Color = Mesh.Rgb(0x16161A) }
                    .Add(0.012, 0, 0, 0.018, 0.016, 0.0032, 0xC8CACE, 0.7) // shield can
                    .Add(0.0245, 0, 0, 0.006, 0.018, 0.0008, 0x1F1F24) // antenna area
                    .Add(-0.024, 0, 0, 0.006, 0.008, 0.003, 0xB9BBC0, 0.6);
            }

            if (id == "flight-controller")
            {
                return new ModuleLook { Length = 0.030, Width = 0.030, Color = Mesh.Rgb(0x141417) }
                    .Add(0, 0, 0, 0.007, 0.007, 0.001, 0x2A2A2E)
                    .Add(0.008, 0.008, 0, 0.003, 0.003, 0.0008, 0x2A2A2E)
                    .Add(-0.012, 0.012, 0, 0.002, 0.002, 0.0006, 0x5BB98B);
            }

            if (id == "motor-driver")
            {
                if (model.IndexOf("L298", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return new ModuleLook { Length = 0.043, Width = 0.043, Color = Mesh.Rgb(0xB3262B) }
                        .Add(0.0, 0.0, 0, 0.023, 0.016, 0.025, 0x1A1A1C, 0.3)
                        .Add(-0.015, 0.0, 0, 0.008, 0.03, 0.01, 0x2D6FB0);
                }

                return new ModuleLook { Length = 0.020, Width = 0.020, Color = Mesh.Rgb(0x5E2B8C) }
                    .Add(0, 0, 0, 0.006, 0.0065, 0.0012, 0x18181A);
            }

            if (def.Symbol == SymbolKind.Battery)
            {
                p.TryGetValue("chemistry", out var chem);
                double cap = SiValue.Parse(p.TryGetValue("capacity", out var c) ? c : "1", 1);
                int cells = (int)SiValue.Parse(p.TryGetValue("cells", out var n) ? n : "1", 1);
                if (chem == "LithiumIon" && cap < 1.0)
                {
                    return new ModuleLook { Length = 0.040, Width = 0.019, Height = 0.007, Color = Mesh.Rgb(0xD9A520), Specular = 0.5 }
                        .Add(-0.021, 0, -0.004, 0.004, 0.006, 0.002, 0xE5484D);
                }

                if (chem == "LithiumIon" || chem == "LiFePO4")
                {
                    if (cells <= 2 && cap >= 1.5)
                    {
                        var l = new ModuleLook { Length = 0.077, Width = 0.042, Height = 0.004, Color = Mesh.Rgb(0x18181A) };
                        for (int i = 0; i < Math.Max(1, cells); i++) l.Add(0, (i - (cells - 1) / 2.0) * 0.019, 0, 0.065, 0.018, 0.018, i % 2 == 0 ? 0x2D6FB0u : 0x2D6FB0u, 0.4);
                        return l;
                    }

                    return new ModuleLook { Length = 0.105, Width = 0.034, Height = 0.02 + 0.006 * cells, Color = Mesh.Rgb(0x2A2A2E), Specular = 0.4 }
                        .Add(0, 0, 0, 0.06, 0.03, 0.0005, 0x6E9BFF);
                }

                if (model.StartsWith("9V", StringComparison.OrdinalIgnoreCase))
                    return new ModuleLook { Length = 0.048, Width = 0.026, Height = 0.017, Color = Mesh.Rgb(0x2B2B30), Specular = 0.3 };

                var holder = new ModuleLook { Length = 0.058, Width = Math.Max(0.016, 0.0145 * Math.Max(1, cells)) + 0.004, Height = 0.004, Color = Mesh.Rgb(0x141416) };
                for (int i = 0; i < Math.Max(1, cells); i++)
                    holder.Add(0, (i - (cells - 1) / 2.0) * 0.0145, 0, 0.05, 0.014, 0.014, chem == "NiMH" ? 0x3A8F5Bu : 0x2C2C2Cu, 0.4);
                return holder;
            }

            if (def.SymbolVariant == "OLED")
            {
                return new ModuleLook { Length = 0.027, Width = 0.027, Color = Mesh.Rgb(0x1D4F91) }
                    .Add(0, 0, 0, 0.026, 0.0155, 0.0012, 0x0A0A0C, 0.4)
                    .Add(0, 0.0125, 0, 0.012, 0.0022, 0.0085, 0x18181A);
            }

            if (def.SymbolVariant == "LCD")
            {
                return new ModuleLook { Length = 0.080, Width = 0.036, Color = Mesh.Rgb(0x2E7D32) }
                    .Add(-0.002, 0, 0, 0.071, 0.024, 0.0068, 0x141416)
                    .Add(-0.002, 0, 0.0068, 0.066, 0.016, 0.0006, 0x0F1A3A, 0.3);
            }

            if (id == "breadboard") return new ModuleLook { Length = 0.082, Width = 0.055, Height = 0.0085, Color = Mesh.Rgb(0xEDEDEA) };
            if (micro) return new ModuleLook { Length = 0.012, Width = 0.012, Color = Mesh.Rgb(0x1D4F91) };
            return new ModuleLook { Length = 0.024, Width = 0.018, Color = Mesh.Rgb(0x1D4F91) }
                .Add(0, 0, 0, 0.006, 0.006, 0.0012, 0x18181A);
        }
    }

    /// <summary>Top-side picture of the user's PCB layout for the 3D view.</summary>
    public static class PcbTexture
    {
        public static BitmapSource Render(BoardDocument board)
        {
            const double ppmm = 20;
            int w = Math.Max(8, (int)(board.Width * ppmm)), h = Math.Max(8, (int)(board.Height * ppmm));
            var dv = new DrawingVisual();
            using (var g = dv.RenderOpen())
            {
                g.DrawRectangle(new SolidColorBrush(Mesh.Rgb(0x1E5631)), null, new Rect(0, 0, w, h));
                var copper = new SolidColorBrush(Mesh.Rgb(0x3E8A55));
                var bottom = new SolidColorBrush(Mesh.Rgb(0x2A6B40));
                // Copper pours of the visible (top) side, as their filled islands.
                foreach (var z in board.Zones.Where(z => z.Layer == CopperLayer.Top && z.IsFilled))
                {
                    foreach (var region in z.FilledRegions)
                    {
                        var geo = new StreamGeometry { FillRule = FillRule.EvenOdd };
                        using (var sg = geo.Open())
                        {
                            foreach (var c in region.Contours())
                            {
                                sg.BeginFigure(new Point(c[0].X * ppmm, c[0].Y * ppmm), true, true);
                                sg.PolyLineTo(c.Skip(1).Select(p => new Point(p.X * ppmm, p.Y * ppmm)).ToList(), true, false);
                            }
                        }

                        g.DrawGeometry(copper, null, geo);
                    }
                }

                // Only the outer layers are visible on a finished board.
                foreach (var t in board.Tracks.Where(t => PcbLayers.IsOuter(t.Layer)).OrderBy(t => t.Layer == CopperLayer.Top ? 1 : 0))
                {
                    var pen = new Pen(t.Layer == CopperLayer.Top ? copper : bottom, t.Width * ppmm) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    g.DrawLine(pen, new Point(t.X1 * ppmm, t.Y1 * ppmm), new Point(t.X2 * ppmm, t.Y2 * ppmm));
                }

                var pad = new SolidColorBrush(Mesh.Rgb(0xD8B26A));
                foreach (var p in board.Pads())
                {
                    var r = new Rect((p.X - p.Width / 2) * ppmm, (p.Y - p.Height / 2) * ppmm, p.Width * ppmm, p.Height * ppmm);
                    if (p.Pad.Shape == PadShape.Circle) g.DrawEllipse(pad, null, new Point(p.X * ppmm, p.Y * ppmm), r.Width / 2, r.Height / 2);
                    else g.DrawRectangle(pad, null, r);
                    if (p.Pad.IsThroughHole) g.DrawEllipse(Brushes.Black, null, new Point(p.X * ppmm, p.Y * ppmm), p.Pad.Drill * ppmm / 2, p.Pad.Drill * ppmm / 2);
                }

                foreach (var v in board.Vias)
                {
                    g.DrawEllipse(pad, null, new Point(v.X * ppmm, v.Y * ppmm), v.Diameter * ppmm / 2, v.Diameter * ppmm / 2);
                    g.DrawEllipse(Brushes.Black, null, new Point(v.X * ppmm, v.Y * ppmm), v.Drill * ppmm / 2, v.Drill * ppmm / 2);
                }

                var silk = new Pen(Brushes.White, 0.15 * ppmm);
                var tf = new Typeface("Segoe UI");
                foreach (var c in board.Components)
                {
                    var fp = FootprintLibrary.Get(c.FootprintId);
                    if (fp == null) continue;
                    var box = c.Courtyard(fp);
                    g.DrawRectangle(null, silk, new Rect(box.MinX * ppmm, box.MinY * ppmm, (box.MaxX - box.MinX) * ppmm, (box.MaxY - box.MinY) * ppmm));
                    var ft = new FormattedText(c.RefDes, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 1.2 * ppmm, Brushes.White, 1.0);
                    g.DrawText(ft, new Point(box.MinX * ppmm + 2, box.MinY * ppmm + 1));
                }
            }

            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }
    }
}
