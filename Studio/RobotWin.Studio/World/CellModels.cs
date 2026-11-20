using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Models3D;

namespace RobotWin.Studio.World
{
    /// <summary>Live parts of a robot-cell model (arm links, fingers, cup, belt, indicator lamps).</summary>
    public sealed class CellVisual
    {
        public string PartId { get; init; } = string.Empty;
        public ArmModel? Arm { get; init; }

        /// <summary>One transform per arm link (base frame, metres).</summary>
        public MatrixTransform3D[] Links { get; init; } = Array.Empty<MatrixTransform3D>();

        /// <summary>Gripper fingers: translation along the tool Y axis (metres), sign of the closing direction.</summary>
        public (TranslateTransform3D T, double Side)[] Fingers { get; init; } = Array.Empty<(TranslateTransform3D, double)>();

        public TranslateTransform3D? Cup { get; init; }
        public TranslateTransform3D? Belt { get; init; }
        public double BeltPitch { get; init; } = 0.1;

        /// <summary>Indicator lamp (photo-eye, light curtain, controller status).</summary>
        public SolidColorBrush? Lamp { get; init; }
    }

    /// <summary>
    /// [arm] Detailed procedural 3D models of the robot-cell parts: three arm models with one
    /// group per moving link, gripper fingers and suction cups that move, a conveyor with a
    /// moving belt, light curtain, pallet, fence panels and pedestal. Original generic designs.
    /// </summary>
    public static class CellModels
    {
        private const double Mm = 0.001;
        private static readonly ConditionalWeakTable<ObjectModel, CellVisual> Visuals = new ConditionalWeakTable<ObjectModel, CellVisual>();
        private static readonly Matrix3D MmToM = new Matrix3D(Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, 1);

        // Flat colours (no gradients): off-white castings, graphite drives, steel-blue accent.
        private const uint White = 0xE4E6E9, Graphite = 0x2B2D31, Dark = 0x44474D, Accent = 0x2F7FA8, Steel = 0xB4B8BF, Alu = 0xC3C7CD, Black = 0x1B1C1F,
            Yellow = 0xE2B93B, Wood = 0xC9A16B, WoodDark = 0xAE8753, Belt = 0x2A2B2F, ServoBlue = 0x22416E;

        public static CellVisual? VisualOf(ObjectModel model) => Visuals.TryGetValue(model, out var v) ? v : null;

        /// <summary>Model of a robot-cell part, or null for other objects.</summary>
        public static ObjectModel? TryBuild(WorldObject o, PartLibrary library)
        {
            if (o.Kind != WorldObjectKind.Component || !CellParts.IsCellPart(o.PartId)) return null;
            var def = library.Find(o.PartId!);
            if (def == null) return null;
            var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
            var p = library.ResolveParameters(def, preset, o.Parameters);
            var m = new ObjectModel();
            CellVisual visual;
            switch (o.PartId)
            {
                case CellParts.ArmId:
                    visual = BuildArm(m, o, p);
                    break;
                case CellParts.GripperId:
                    visual = BuildGripper(m, p);
                    break;
                case CellParts.VacuumId:
                    visual = BuildVacuum(m, p);
                    break;
                case CellParts.ConveyorId:
                    visual = BuildConveyor(m, p);
                    break;
                case CellParts.CurtainId:
                    visual = BuildCurtain(m, p);
                    break;
                case CellParts.PalletId:
                    visual = BuildPallet(m, p);
                    break;
                case CellParts.FenceId:
                    visual = BuildFence(m, p);
                    break;
                default:
                    visual = BuildPedestal(m, p);
                    break;
            }

            if (CellGeometry.For(o) is { } g)
            {
                m.CollisionBounds = new Rect3D(g.Min.X, g.Min.Y, g.Min.Z, g.Max.X - g.Min.X, g.Max.Y - g.Min.Y, g.Max.Z - g.Min.Z);
            }

            m.Bounds = m.Body.Bounds;
            Visuals.Add(m, visual);
            return m;
        }

        // ================================================================ mesh kit (millimetres)

        private sealed class Kit
        {
            private readonly Dictionary<Material, MeshBuilder> _meshes = new Dictionary<Material, MeshBuilder>();
            private readonly List<Material> _order = new List<Material>();
            public Frame F { get; } = new Frame();

            public MeshBuilder this[uint rgb] => Get(Mat.Plastic(rgb, 0.18, 24));

            public MeshBuilder Get(Material m)
            {
                if (!_meshes.TryGetValue(m, out var mb))
                {
                    mb = new MeshBuilder(F);
                    _meshes[m] = mb;
                    _order.Add(m);
                }

                return mb;
            }

            public MeshBuilder Metal(uint rgb) => Get(Mat.Metal(rgb, 0.6, 40));

            /// <summary>Geometry in metres.</summary>
            public Model3DGroup Build()
            {
                var g = new Model3DGroup();
                foreach (var m in _order)
                {
                    var mb = _meshes[m];
                    if (mb.IsEmpty) continue;
                    g.Children.Add(new GeometryModel3D(mb.ToGeometry(), m));
                }

                return new Model3DGroup { Children = { g }, Transform = new MatrixTransform3D(MmToM) };
            }

            public void Box(uint c, double cx, double cy, double cz, double sx, double sy, double sz) => this[c].Box(cx, cy, cz, sx, sy, sz);

            public void CylX(uint c, double x0, double x1, double cy, double cz, double r, int seg = 28) =>
                this[c].Rod(new Point3D(x0, cy, cz), new Point3D(x1, cy, cz), r, seg);

            public void CylY(uint c, double cx, double y0, double y1, double cz, double r, int seg = 28) =>
                this[c].Rod(new Point3D(cx, y0, cz), new Point3D(cx, y1, cz), r, seg);

            public void CylZ(uint c, double cx, double cy, double z0, double z1, double r, int seg = 28) => this[c].CylinderZ(cx, cy, z0, z1, r, seg);
        }

        private static Matrix3D ToMatrix(Transform3 t)
        {
            var m = Matrix3D.Identity;
            m.Rotate(new Quaternion(t.Q.X, t.Q.Y, t.Q.Z, t.Q.W));
            m.Translate(new Vector3D(t.P.X, t.P.Y, t.P.Z));
            return m;
        }

        public static Matrix3D MatrixOf(Transform3 t) => ToMatrix(t);

        private static void AddPins(ObjectModel m, IEnumerable<string> pins, Func<int, Point3D> at)
        {
            int i = 0;
            foreach (var pin in pins) m.Pins.Add(new PinAnchor(pin, pin, at(i++)));
        }

        private static SolidColorBrush LampBrush() => new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10));

        private static GeometryModel3D LampModel(SolidColorBrush lamp, MeshGeometry3D mesh)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x22))));
            g.Children.Add(new EmissiveMaterial(lamp));
            return new GeometryModel3D(mesh, g);
        }

        // ================================================================ arms

        private static CellVisual BuildArm(ObjectModel m, WorldObject o, IReadOnlyDictionary<string, string> p)
        {
            var model = ArmCatalog.Get(p.TryGetValue("model", out var id) ? id : null);
            var baseKit = new Kit();
            var links = Enumerable.Range(0, model.Dof).Select(_ => new Kit()).ToArray();
            Vector3D pinDir;
            Point3D pinOrigin;
            switch (model.Id)
            {
                case ArmCatalog.Scara:
                    Scara(baseKit, links);
                    pinOrigin = new Point3D(-121, -60, 60);
                    pinDir = new Vector3D(0, 9, 0);
                    break;
                case ArmCatalog.Desktop6:
                    Desktop(baseKit, links);
                    pinOrigin = new Point3D(-70, -40, 14);
                    pinDir = new Vector3D(0, 6.2, 0);
                    break;
                default:
                    Industrial(baseKit, links);
                    pinOrigin = new Point3D(-171, -52, 60);
                    pinDir = new Vector3D(0, 8, 0);
                    break;
            }

            m.Body.Children.Add(baseKit.Build());
            var q0 = CellGeometry.StartJoints(o);
            var T = ArmKinematics.LinkTransforms(model, q0);
            var transforms = new MatrixTransform3D[model.Dof];
            for (int i = 0; i < model.Dof; i++)
            {
                transforms[i] = new MatrixTransform3D(ToMatrix(T[i]));
                var g = new Model3DGroup { Transform = transforms[i] };
                g.Children.Add(links[i].Build());
                m.Body.Children.Add(g);
            }

            // Controller I/O connector strip on the back of the base: two rows (inputs, outputs + 0 V).
            var ins = CellParts.ArmInputs;
            var outs = CellParts.ArmOutputs.Concat(new[] { "GND" }).ToArray();
            var lamp = LampBrush();
            var pinKit = new Kit();
            for (int r = 0; r < 2; r++)
            {
                var names = r == 0 ? ins : outs;
                for (int i = 0; i < names.Length; i++)
                {
                    var at = pinOrigin + pinDir * (i * 2.0) + new Vector3D(0, 0, -r * (model.Id == ArmCatalog.Desktop6 ? 7 : 14));
                    double s = model.Id == ArmCatalog.Desktop6 ? 3 : 5;
                    pinKit.Metal(0xD9B04C).Box(at.X - 1, at.Y, at.Z, 2, s, s);
                    m.Pins.Add(new PinAnchor(names[i], names[i], new Point3D((at.X - 2) * Mm, at.Y * Mm, at.Z * Mm)));
                }
            }

            m.Body.Children.Add(pinKit.Build());
            // Status lamp on top of the connector box.
            var lampAt = pinOrigin + new Vector3D(6, -6, model.Id == ArmCatalog.Desktop6 ? 14 : 34);
            m.Body.Children.Add(new Model3DGroup
            {
                Children = { LampModel(lamp, Assembly3D.Mesh.Sphere((model.Id == ArmCatalog.Desktop6 ? 3 : 6) * Mm, lampAt.X * Mm, lampAt.Y * Mm, lampAt.Z * Mm, 12)) }
            });

            var flange = ArmKinematics.Forward(model, q0);
            m.Port = new PortFrame("flange", "Robot flanşı", new Point3D(flange.P.X, flange.P.Y, flange.P.Z), new Vector3D(flange.ZAxis.X, flange.ZAxis.Y, flange.ZAxis.Z));
            return new CellVisual { PartId = CellParts.ArmId, Arm = model, Links = transforms, Lamp = lamp };
        }

        /// <summary>6-axis 850 mm arm (d1 330, a1 75, a2 365, a3 35, d4 405, d6 80).</summary>
        private static void Industrial(Kit b, Kit[] l)
        {
            // Base: foot plate, cast housing, J1 bearing, cable/connector box at the back.
            b[Dark].RoundedBox(0, 0, 0, 270, 270, 18, 30);
            foreach (var (x, y) in new[] { (-105.0, -105.0), (105.0, -105.0), (-105.0, 105.0), (105.0, 105.0) }) b.Metal(Steel).CylinderZ(x, y, 18, 25, 9, 12);
            b[White].Frustum(118, 104, 18, 150, 40);
            b[Accent].CylinderZ(0, 0, 128, 134, 107, 40);
            b.CylZ(Graphite, 0, 0, 150, 165, 106, 40);
            b[Graphite].RoundedBox(-140, 0, 25, 62, 130, 110, 8);

            // L1: turntable, column, two shoulder cheeks, J1 and J2 motors.
            var c = l[0];
            c.CylZ(White, 0, 0, 165, 186, 104, 40);
            c[White].RoundedBox(12, 0, 186, 190, 186, 100, 40);
            c.CylY(White, 75, 55, 96, 330, 84, 36);
            c.CylY(White, 75, -96, -55, 330, 84, 36);
            c.Box(White, 50, 0, 290, 120, 190, 40);
            c.CylY(Graphite, 75, -150, -96, 330, 52);
            c.CylY(Accent, 75, -158, -150, 330, 44);
            c.CylZ(Graphite, -86, 0, 186, 292, 42);
            c.CylZ(Accent, -86, 0, 292, 300, 35);

            // L2: upper arm between the cheeks.
            var u = l[1];
            u.CylY(White, 75, -54, 54, 330, 76, 36);
            u[White].Tapered(75, 0, 330, 122, 104, 365, 10, 8);
            u.CylY(White, 75, -50, 50, 695, 62, 32);
            u.Box(Graphite, 75 - 62, 0, 520, 4, 60, 220);

            // L3: elbow with J3 motor, forearm root, J4 motor behind.
            var e = l[2];
            e.CylY(White, 75, 50, 82, 695, 62, 32);
            e.CylY(White, 75, -82, -50, 695, 62, 32);
            e.CylY(Graphite, 75, 82, 128, 695, 44);
            e.CylY(Accent, 75, 128, 135, 695, 38);
            e[White].RoundedBox(140, 0, 672, 160, 132, 112, 22);
            e.CylX(Graphite, -24, 62, 0, 730, 40);
            e.CylX(Accent, -32, -24, 0, 730, 34);

            // L4: forearm tube and wrist fork.
            var f = l[3];
            f.CylX(Graphite, 218, 228, 0, 730, 47, 36);
            f.CylX(White, 228, 445, 0, 730, 42, 36);
            f[White].RoundedBox(465, 45, 692, 56, 14, 76, 6);
            f[White].RoundedBox(465, -45, 692, 56, 14, 76, 6);
            f.Box(White, 440, 0, 730, 20, 104, 60);

            // L5: wrist body and nose.
            var w = l[4];
            w.CylY(White, 480, -37, 37, 730, 37, 32);
            w.CylX(White, 480, 532, 0, 730, 31, 32);
            w.CylX(Accent, 526, 534, 0, 730, 32.5, 32);

            // L6: tool flange with bolt holes and a dowel.
            var t = l[5];
            t.Get(Mat.Metal(Steel, 0.7, 45)).Rod(new Point3D(534, 0, 730), new Point3D(560, 0, 730), 31.5, 36);
            foreach (var (y, z) in new[] { (18.0, 0.0), (-18.0, 0.0), (0.0, 18.0), (0.0, -18.0) }) t.CylX(Black, 559.6, 560.4, y, 730 + z, 3.2, 10);
            t.CylX(Graphite, 559.5, 563, 12.7, 730 + 12.7, 2.5, 10);
        }

        /// <summary>SCARA, 250 + 250 mm arms, quill at x = 500.</summary>
        private static void Scara(Kit b, Kit[] l)
        {
            b[Dark].RoundedBox(0, 0, 0, 240, 280, 15, 20);
            b[White].RoundedBox(-20, 0, 15, 200, 240, 325, 30);
            b.Box(Graphite, 81, 0, 190, 4, 150, 190);
            b.Box(Accent, 82, 0, 300, 4, 150, 8);
            b.CylZ(Graphite, 0, 0, 340, 400, 96, 40);

            var a1 = l[0];
            a1.CylZ(White, 0, 0, 400, 466, 95, 40);
            a1.CylZ(White, 250, 0, 400, 466, 68, 36);
            a1[White].Tapered(125, 0, 400, 250, 150, 66, 0, 8);
            a1.Box(Accent, 125, 0, 466.5, 180, 30, 1.5);

            var a2 = l[1];
            a2.CylZ(Graphite, 250, 0, 466, 476, 64, 36);
            a2.CylZ(White, 250, 0, 476, 538, 62, 36);
            a2[White].Tapered(375, 0, 476, 250, 118, 62, 0, 6);
            a2[White].RoundedBox(505, 0, 440, 136, 122, 170, 32);
            a2.CylZ(Graphite, 505, 0, 610, 640, 36, 28);
            a2.Box(Accent, 571.5, 0, 525, 2, 70, 12);

            var q = l[2];
            q.Get(Mat.Metal(0xD5D8DD, 0.9, 60)).CylinderZ(500, 0, 180, 640, 10, 20);
            q.CylZ(Graphite, 500, 0, 640, 652, 15, 20);

            var t = l[3];
            t.Get(Mat.Metal(Steel, 0.7, 45)).CylinderZ(500, 0, 168, 180, 25, 32);
            t.Box(Graphite, 515, 0, 186, 6, 6, 12);
        }

        /// <summary>Desktop servo arm (d1 72, a2 105, d4 98, d6 35).</summary>
        private static void Desktop(Kit b, Kit[] l)
        {
            b.CylZ(0x26272B, 0, 0, 0, 40, 62, 40);
            b.Get(Mat.Metal(Alu, 0.6, 40)).CylinderZ(0, 0, 40, 45, 56, 40);
            b[0x26272B].RoundedBox(-58, 0, 2, 22, 70, 30, 4);

            var t = l[0];
            t.CylZ(White, 0, 0, 45, 52, 48, 36);
            t.Get(Mat.Metal(Alu, 0.6, 40)).Box(0, 27, 66, 30, 3, 30);
            t.Get(Mat.Metal(Alu, 0.6, 40)).Box(0, -27, 66, 30, 3, 30);
            t.Box(ServoBlue, 0, -12, 71, 40, 20, 38);

            var u = l[1];
            u.Get(Mat.Metal(Alu, 0.6, 40)).Box(0, 23.5, 124.5, 22, 3, 115);
            u.Get(Mat.Metal(Alu, 0.6, 40)).Box(0, -23.5, 124.5, 22, 3, 115);
            u.CylY(White, 0, 20, 22, 72, 10, 16);
            u.CylY(White, 0, -22, -20, 72, 10, 16);
            u.Box(Alu, 0, 0, 100, 18, 44, 3);

            var e = l[2];
            e.Box(ServoBlue, 6, 0, 177, 38, 20, 40);
            e.CylY(White, 0, 10, 13, 177, 9, 16);
            e.Get(Mat.Metal(Alu, 0.6, 40)).Box(32, 0, 177, 18, 34, 3);

            var f = l[3];
            f.Box(ServoBlue, 52, 0, 177, 30, 22, 36);
            f.Get(Mat.Metal(Alu, 0.6, 40)).Box(82, 15, 177, 36, 3, 22);
            f.Get(Mat.Metal(Alu, 0.6, 40)).Box(82, -15, 177, 36, 3, 22);

            var w = l[4];
            w.CylY(White, 98, -13, 13, 177, 9, 16);
            w.Box(ServoBlue, 112, 0, 177, 24, 22, 30);

            var fl = l[5];
            fl.Get(Mat.Metal(Alu, 0.6, 40)).Rod(new Point3D(124, 0, 177), new Point3D(133, 0, 177), 14, 24);
        }

        // ================================================================ tools (tool frame: +Z = approach)

        private static CellVisual BuildGripper(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double stroke = Math.Max(4, CellParts.Num(p, "stroke", 60));
            var k = new Kit();
            k.Get(Mat.Metal(Steel, 0.7, 45)).CylinderZ(0, 0, 0, 8, 31.5, 32);
            k[Graphite].RoundedBox(0, 0, 8, ToolGeometry.GripperHousingX * 1000, ToolGeometry.GripperHousingY * 1000, ToolGeometry.GripperHousingZ * 1000 - 8, 8);
            k.Box(Accent, 0, -ToolGeometry.GripperHousingY * 500 - 0.5, 40, 56, 1, 8);
            k.Get(Mat.Metal(Steel, 0.7, 45)).Box(0, 0, ToolGeometry.GripperHousingZ * 1000 - 1.5, 80, 22, 3);
            m.Body.Children.Add(k.Build());
            var fingers = new List<(TranslateTransform3D, double)>();
            double gap = ToolGeometry.FingerMinGap * 1000 + stroke;
            foreach (double side in new[] { -1.0, 1.0 })
            {
                var fk = new Kit();
                double fy = ToolGeometry.FingerY * 1000, fz = ToolGeometry.FingerZ * 1000;
                fk.Get(Mat.Metal(Alu, 0.6, 40)).Box(0, 0, 0, ToolGeometry.FingerX * 1000, fy, fz);
                fk.Box(Black, 0, -side * (fy / 2 + 0.8), fz / 2 - 22, ToolGeometry.FingerX * 1000 - 4, 1.6, 36);
                var tr = new TranslateTransform3D();
                var g = new Model3DGroup { Transform = new Transform3DGroup { Children = { new TranslateTransform3D(0, side * (gap / 2 + fy / 2) * Mm, (ToolGeometry.GripperHousingZ + ToolGeometry.FingerZ / 2)), tr } } };
                g.Children.Add(fk.Build());
                m.Body.Children.Add(g);
                fingers.Add((tr, side));
            }

            return new CellVisual { PartId = CellParts.GripperId, Fingers = fingers.ToArray() };
        }

        private static CellVisual BuildVacuum(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double d = Math.Max(10, CellParts.Num(p, "diameter", 50));
            int cups = (int)Math.Max(1, CellParts.Num(p, "cups", 1));
            var layout = ToolGeometry.CupLayout(cups, d / 1000);
            var k = new Kit();
            double mount = ToolGeometry.VacuumMountZ * 1000, stem = ToolGeometry.VacuumStemZ * 1000, cupZ = ToolGeometry.VacuumCupZ * 1000;
            k.Get(Mat.Metal(Steel, 0.7, 45)).CylinderZ(0, 0, 0, 10, 31.5, 32);
            k.CylZ(Graphite, 0, 0, 10, mount, 30, 32);
            k.CylZ(Accent, 0, 0, 28, 33, 31, 32);
            if (cups > 1)
            {
                double x0 = layout.Min(c => c.X) * 1000 - 10, x1 = layout.Max(c => c.X) * 1000 + 10;
                double y0 = layout.Min(c => c.Y) * 1000 - 10, y1 = layout.Max(c => c.Y) * 1000 + 10;
                k.Get(Mat.Metal(Alu, 0.6, 40)).Box((x0 + x1) / 2, (y0 + y1) / 2, mount - 5, x1 - x0, y1 - y0, 10);
            }

            foreach (var (x, y) in layout) k.Get(Mat.Metal(Steel, 0.7, 45)).CylinderZ(x * 1000, y * 1000, mount, mount + stem, 7, 16);
            m.Body.Children.Add(k.Build());
            var ck = new Kit();
            double z0 = mount + stem;
            foreach (var (x, y) in layout)
            {
                using (ck.F.At(Mx.T(x * 1000, y * 1000, 0)))
                {
                    ck[Black].Frustum(d * 0.22, d * 0.30, z0, z0 + cupZ * 0.25, 28);
                    ck[Black].Frustum(d * 0.38, d * 0.30, z0 + cupZ * 0.25, z0 + cupZ * 0.5, 28);
                    ck[Black].Frustum(d * 0.30, d * 0.40, z0 + cupZ * 0.5, z0 + cupZ * 0.75, 28);
                    ck[0x30343A].Frustum(d * 0.40, d * 0.5, z0 + cupZ * 0.75, z0 + cupZ, 32);
                }
            }

            var tr = new TranslateTransform3D();
            var g = new Model3DGroup { Transform = tr };
            g.Children.Add(ck.Build());
            m.Body.Children.Add(g);
            return new CellVisual { PartId = CellParts.VacuumId, Cup = tr };
        }

        // ================================================================ conveyor

        private static CellVisual BuildConveyor(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double L = CellParts.Num(p, "length", 1.6) * 1000, W = CellParts.Num(p, "width", 0.3) * 1000, H = CellParts.Num(p, "height", 0.6) * 1000;
            bool feed = p.TryGetValue("feed", out var fd) && fd.StartsWith("kut", StringComparison.OrdinalIgnoreCase);
            var (bx, by, bz) = CellParts.Size3(p, "box", (0.16, 0.12, 0.10));
            var k = new Kit();
            var alu = k.Get(Mat.Metal(Alu, 0.5, 30));
            foreach (double s in new[] { -1.0, 1.0 })
            {
                alu.Box(0, s * (W / 2 + 15), H - 32, L, 30, 64);
                k.Box(Dark, 0, s * (W / 2 + 30.5), H - 32, L - 20, 1, 20);
            }

            k.Box(Belt, 0, 0, H - 6, L - 40, W, 12);
            foreach (double s in new[] { -1.0, 1.0 }) k.CylY(Steel, s * (L / 2 - 22), -W / 2, W / 2, H - 30, 30, 28);
            foreach (double x in new[] { -(L / 2 - 120), L / 2 - 120 })
            {
                foreach (double s in new[] { -1.0, 1.0 })
                {
                    alu.Box(x, s * (W / 2 + 15), (H - 64) / 2, 40, 40, H - 64);
                    k.Box(Dark, x, s * (W / 2 + 15), 4, 90, 70, 8);
                }

                alu.Box(x, 0, 220, 30, W + 30, 30);
            }

            if (L > 1300) alu.Box(0, 0, 220, L - 240, 30, 30);
            double gap = (feed ? Math.Min(W / 1000, by + 0.03) : W / 1000) * 1000;
            foreach (double s in new[] { -1.0, 1.0 })
            {
                k.Box(0xEDEDEA, 0, s * (gap / 2 + 6), H + 25, L - 20, 12, 46);
                for (double x = -L / 2 + 150; x < L / 2 - 100; x += 400) alu.Box(x, s * (gap / 2 + 12 + (W / 2 + 15 - gap / 2 - 12) / 2), H + 20, 20, W / 2 + 15 - gap / 2 - 12, 10);
            }

            k.Box(Yellow, L / 2 - 10, 0, H + 40, 20, W + 20, 80);
            // Photo-eye and its reflector across the belt.
            k.Box(Graphite, L / 2 - 60, -(W / 2 + 34), H + 25, 24, 20, 34);
            k.Box(0xC8423A, L / 2 - 60, W / 2 + 33, H + 25, 22, 3, 30);
            // Drive motor and gearbox on the discharge end.
            k.CylY(Graphite, L / 2 - 80, -(W / 2 + 40), -(W / 2 + 150), H - 80, 46);
            k.Box(Dark, L / 2 - 80, -(W / 2 + 36), H - 80, 110, 12, 110);
            // Junction box with the I/O terminals.
            k.Box(Graphite, L / 2 - 240, -(W / 2 + 52), H - 130, 90, 44, 70);
            if (feed)
            {
                // Feeder magazine at the infeed end.
                double fx = -L / 2 + 30 + bx * 500;
                double hx = bx * 500 + 25, hy = by * 500 + 25, top = H + bz * 1000 + 260;
                foreach (double sx in new[] { -1.0, 1.0 })
                {
                    foreach (double sy in new[] { -1.0, 1.0 }) k.Box(Yellow, fx + sx * hx, sy * hy, (H + top) / 2 + 30, 16, 16, top - H - 60);
                }

                k.Box(Yellow, fx, hy, top - 8, 2 * hx + 16, 16, 16);
                k.Box(Yellow, fx, -hy, top - 8, 2 * hx + 16, 16, 16);
                k.Box(Yellow, fx + hx, 0, top - 8, 16, 2 * hy, 16);
                k.Box(Yellow, fx - hx, 0, top - 8, 16, 2 * hy, 16);
            }

            m.Body.Children.Add(k.Build());

            // Belt cleats/marks that travel with the belt.
            var bk = new Kit();
            const double pitch = 100;
            for (double x = -L / 2 + 30; x <= L / 2 - 30 - pitch; x += pitch) bk.Box(0x46474D, x, 0, H + 0.4, 8, W - 12, 0.8);
            var bt = new TranslateTransform3D();
            m.Body.Children.Add(new Model3DGroup { Children = { bk.Build() }, Transform = bt });

            var lamp = LampBrush();
            m.Body.Children.Add(new Model3DGroup
            {
                Children = { LampModel(lamp, Assembly3D.Mesh.Box((L / 2 - 60) * Mm, -(W / 2 + 34) * Mm, (H + 45) * Mm, 8 * Mm, 8 * Mm, 6 * Mm)) }
            });
            AddPins(m, new[] { "RUN", "SENSOR", "GND" }, i => new Point3D((L / 2 - 270 + i * 30) * Mm, -(W / 2 + 76) * Mm, (H - 130) * Mm));
            var pk = new Kit();
            for (int i = 0; i < 3; i++) pk.Metal(0xD9B04C).Box(L / 2 - 270 + i * 30, -(W / 2 + 75), H - 130, 6, 2, 6);
            m.Body.Children.Add(pk.Build());
            return new CellVisual { PartId = CellParts.ConveyorId, Belt = bt, BeltPitch = pitch * Mm, Lamp = lamp };
        }

        // ================================================================ light curtain

        private static CellVisual BuildCurtain(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double W = CellParts.Num(p, "width", 0.9) * 1000, Hc = CellParts.Num(p, "height", 1.2) * 1000;
            double basez = WorldPhysics.CurtainBase * 1000, hp = basez + Hc + 80;
            var k = new Kit();
            foreach (double s in new[] { -1.0, 1.0 })
            {
                double y = s * (W / 2 + 25);
                k.Box(Dark, 0, y, 5, 130, 130, 10);
                k.Box(Yellow, 0, y, hp / 2 + 5, 50, 50, hp - 10);
                k.Box(0x3A1414, 0, -s * (W / 2 + 0.5), basez + Hc / 2, 28, 1, Hc + 20);
                k.Box(Graphite, 0, y, hp + 6, 54, 54, 12);
            }

            m.Body.Children.Add(k.Build());
            var lamp = LampBrush();
            m.Body.Children.Add(new Model3DGroup
            {
                Children = { LampModel(lamp, Assembly3D.Mesh.Box(0, -(W / 2 + 25) * Mm, (hp + 14) * Mm, 30 * Mm, 30 * Mm, 4 * Mm)) }
            });
            AddPins(m, new[] { "OSSD", "GND" }, i => new Point3D(26 * Mm, (-(W / 2 + 25) - 8 + i * 16) * Mm, 120 * Mm));
            var pk = new Kit();
            for (int i = 0; i < 2; i++) pk.Metal(0xD9B04C).Box(25.5, -(W / 2 + 25) - 8 + i * 16, 120, 2, 6, 6);
            m.Body.Children.Add(pk.Build());
            return new CellVisual { PartId = CellParts.CurtainId, Lamp = lamp };
        }

        // ================================================================ passive equipment

        private static CellVisual BuildPallet(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double X = CellParts.Num(p, "size_x", 0.8) * 1000, Y = CellParts.Num(p, "size_y", 0.6) * 1000, H = CellParts.Num(p, "height", 0.144) * 1000;
            var k = new Kit();
            double t = 22, blockH = H - 2 * t - 22;
            int boards = Math.Max(3, (int)Math.Round(Y / 120));
            double bw = Math.Min(140, (Y - (boards - 1) * 20) / boards);
            for (int i = 0; i < boards; i++)
            {
                double y = -Y / 2 + bw / 2 + i * (Y - bw) / (boards - 1);
                k.Box(Wood, 0, y, H - t / 2, X, bw, t);
            }

            foreach (double sy in new[] { -1.0, 0.0, 1.0 })
            {
                double y = sy * (Y / 2 - 50);
                k.Box(WoodDark, 0, y, H - t - 11, X, 100, 22);
                k.Box(Wood, 0, y, t / 2, X, 100, t);
                foreach (double sx in new[] { -1.0, 0.0, 1.0 }) k.Box(WoodDark, sx * (X / 2 - 50), y, t + blockH / 2, 100, 100, blockH);
            }

            m.Body.Children.Add(k.Build());
            return new CellVisual { PartId = CellParts.PalletId };
        }

        private static CellVisual BuildFence(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double W = CellParts.Num(p, "width", 1.0) * 1000, H = CellParts.Num(p, "height", 1.6) * 1000;
            var k = new Kit();
            foreach (double s in new[] { -1.0, 1.0 })
            {
                k.Box(Yellow, s * (W / 2 - 30), 0, H / 2, 50, 50, H);
                k.Box(Dark, s * (W / 2 - 30), 0, 4, 110, 110, 8);
            }

            double x0 = -W / 2 + 60, x1 = W / 2 - 60, z0 = 120, z1 = H - 30;
            k.Box(Graphite, 0, 0, z0, x1 - x0, 22, 22);
            k.Box(Graphite, 0, 0, z1, x1 - x0, 22, 22);
            k.Box(Graphite, x0 + 8, 0, (z0 + z1) / 2, 16, 22, z1 - z0);
            k.Box(Graphite, x1 - 8, 0, (z0 + z1) / 2, 16, 22, z1 - z0);
            for (double x = x0 + 50; x < x1 - 20; x += 50) k.Box(0x3A3D42, x, 0, (z0 + z1) / 2, 3, 3, z1 - z0);
            for (double z = z0 + 50; z < z1 - 20; z += 50) k.Box(0x3A3D42, 0, 0, z, x1 - x0, 3, 3);
            m.Body.Children.Add(k.Build());
            return new CellVisual { PartId = CellParts.FenceId };
        }

        private static CellVisual BuildPedestal(ObjectModel m, IReadOnlyDictionary<string, string> p)
        {
            double H = CellParts.Num(p, "height", 0.4) * 1000, S = CellParts.Num(p, "size", 0.4) * 1000;
            var k = new Kit();
            k[Dark].RoundedBox(0, 0, 0, S, S, 20, 10);
            k[0x3A3D42].RoundedBox(0, 0, 20, S - 110, S - 110, H - 40, 20);
            k[Dark].RoundedBox(0, 0, H - 20, S - 40, S - 40, 20, 10);
            foreach (var (x, y) in new[] { (-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0) })
            {
                k.Metal(Steel).CylinderZ(x * (S / 2 - 25), y * (S / 2 - 25), 20, 34, 10, 12);
                k.Box(Yellow, x * (S / 2 - 70), y * (S / 2 - 70), H / 2, 6, 6, H - 60);
            }

            m.Body.Children.Add(k.Build());
            return new CellVisual { PartId = CellParts.PedestalId };
        }
    }
}
