using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// A part model under construction. Generators work in footprint coordinates
    /// (millimetres, x right, y down the board as in the layout editor, z up away from
    /// the board surface); the builder converts to the right-handed model frame
    /// (x, −y, z) used by every consumer. Geometry is grouped by material.
    /// </summary>
    internal sealed class PartBuilder
    {
        private readonly List<Material> _order = new List<Material>();
        private readonly Dictionary<Material, MeshBuilder> _meshes = new Dictionary<Material, MeshBuilder>();

        public PartBuilder()
        {
            F = new Frame();
            F.Push(Mx.S(1, -1, 1));
        }

        private PartBuilder(Frame shared) => F = shared;

        public Frame F { get; }

        public MeshBuilder this[Material m]
        {
            get
            {
                if (!_meshes.TryGetValue(m, out var mb))
                {
                    mb = new MeshBuilder(F);
                    _meshes[m] = mb;
                    _order.Add(m);
                }

                return mb;
            }
        }

        public Frame.Scope At(Matrix3D local) => F.At(local);
        public Frame.Scope At(double x, double y, double z) => F.At(Mx.T(x, y, z));

        // ------------------------------------------------------------------ per-instance slots

        public MeshBuilder? BodyMesh { get; private set; }
        public Brush BodyBrush { get; private set; } = Brushes.Gray;
        public Color BodyColor { get; private set; }
        public double BodySpec { get; private set; }

        /// <summary>The main body: its material is per instance (heat glow, charring).</summary>
        public MeshBuilder Body(Color c, double spec = 0.15)
        {
            BodyMesh ??= new MeshBuilder(F);
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            BodyBrush = brush;
            BodyColor = c;
            BodySpec = spec;
            return BodyMesh;
        }

        public MeshBuilder Body(uint rgb, double spec = 0.15) => Body(Mat.C(rgb), spec);

        /// <summary>Textured main body (printed sleeve, label wrap); u/v as the primitive emits them.</summary>
        public MeshBuilder Body(BitmapSource image, Color average, double spec = 0.15)
        {
            BodyMesh ??= new MeshBuilder(F);
            var brush = new ImageBrush(image) { ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 1, 1) };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
            brush.Freeze();
            BodyBrush = brush;
            BodyColor = average;
            BodySpec = spec;
            return BodyMesh;
        }

        public MeshBuilder? LensMesh { get; private set; }
        public Color LensColor { get; private set; }
        public Color LightColor { get; private set; }

        /// <summary>Translucent light-emitting part (LED dome, bulb): emissive per instance.</summary>
        public MeshBuilder Lens(Color tint, Color light)
        {
            LensMesh ??= new MeshBuilder(F);
            LensColor = tint;
            LightColor = light;
            return LensMesh;
        }

        public MeshBuilder? HaloMesh { get; private set; }

        /// <summary>Glow shell around a light (only in the scene while lit).</summary>
        public MeshBuilder Halo() => HaloMesh ??= new MeshBuilder(F);

        public MeshBuilder? ScreenMesh { get; private set; }
        public string ScreenKind { get; private set; } = string.Empty;
        public string ScreenStyle { get; private set; } = string.Empty;
        public Color ScreenOff { get; private set; }

        /// <summary>Live display surface (OLED / LCD); texture u/v as <see cref="MeshBuilder.TopQuad"/>.</summary>
        public MeshBuilder Screen(string kind, string style, Color off)
        {
            ScreenMesh ??= new MeshBuilder(F);
            ScreenKind = kind;
            ScreenStyle = style;
            ScreenOff = off;
            return ScreenMesh;
        }

        // ------------------------------------------------------------------ moving parts, anchors

        public List<(string Name, PartBuilder Builder, Point3D Pivot, Vector3D Axis, Point3D Tip)> Moving { get; } =
            new List<(string, PartBuilder, Point3D, Vector3D, Point3D)>();

        /// <summary>A rotating sub-part (servo horn, motor shaft, knob); pivot and axis in the current frame.</summary>
        public PartBuilder Rotating(string name, Point3D pivot, Vector3D axis, Point3D? tip = null)
        {
            var child = new PartBuilder(F);
            var m = F.M;
            var a = m.Transform(axis);
            a.Normalize();
            Moving.Add((name, child, m.Transform(pivot), a, m.Transform(tip ?? pivot)));
            return child;
        }

        public List<(string Pad, Point3D Position, Vector3D Direction)> Anchors { get; } = new List<(string, Point3D, Vector3D)>();

        /// <summary>Where a cable attaches to a pad/pin (current frame); direction points away from the part.</summary>
        public void Anchor(string pad, Point3D p, Vector3D dir)
        {
            var m = F.M;
            var d = m.Transform(dir);
            if (d.LengthSquared > 1e-18) d.Normalize();
            Anchors.RemoveAll(a => string.Equals(a.Pad, pad, StringComparison.OrdinalIgnoreCase));
            Anchors.Add((pad, m.Transform(p), d));
        }

        public Point3D? HeatCentre { get; private set; }

        public void Heat(Point3D p) => HeatCentre = F.M.Transform(p);

        // ------------------------------------------------------------------ output

        /// <summary>Frozen geometry, split into opaque and translucent groups (translucent must be drawn last).</summary>
        internal (Model3DGroup Opaque, Model3DGroup Clear) BuildStatic(out int triangles)
        {
            var opaque = new Model3DGroup();
            var clear = new Model3DGroup();
            triangles = 0;
            foreach (var m in _order)
            {
                var mb = _meshes[m];
                if (mb.IsEmpty) continue;
                triangles += mb.TriangleCount;
                var model = new GeometryModel3D(mb.ToGeometry(), m);
                model.Freeze();
                (Mat.IsTransparent(m) ? clear : opaque).Children.Add(model);
            }

            opaque.Freeze();
            clear.Freeze();
            return (opaque, clear);
        }

        public ModelTemplate Build()
        {
            var (opaque, clear) = BuildStatic(out int tris);
            var t = new ModelTemplate { Opaque = opaque, Clear = clear };
            if (BodyMesh != null && !BodyMesh.IsEmpty)
            {
                t.Body = BodyMesh.ToGeometry();
                t.BodyBrush = BodyBrush;
                t.BodyColor = BodyColor;
                t.BodySpec = BodySpec;
                tris += BodyMesh.TriangleCount;
            }

            if (LensMesh != null && !LensMesh.IsEmpty)
            {
                t.Lens = LensMesh.ToGeometry();
                t.LensColor = LensColor;
                t.LightColor = LightColor;
                tris += LensMesh.TriangleCount;
            }

            if (HaloMesh != null && !HaloMesh.IsEmpty) t.Halo = HaloMesh.ToGeometry();
            if (ScreenMesh != null && !ScreenMesh.IsEmpty)
            {
                t.Screen = ScreenMesh.ToGeometry();
                t.ScreenKind = ScreenKind;
                t.ScreenStyle = ScreenStyle;
                t.ScreenOff = ScreenOff;
            }

            foreach (var (name, b, pivot, axis, tip) in Moving)
            {
                var (mo, mc) = b.BuildStatic(out int mt);
                var content = new Model3DGroup();
                content.Children.Add(mo);
                content.Children.Add(mc);
                content.Freeze();
                tris += mt;
                t.Moving.Add(new MovingTemplate(name, content, pivot, axis, tip));
            }

            t.Anchors.AddRange(Anchors);
            t.Triangles = tris;
            var bounds = Rect3D.Empty;
            void Union(Rect3D bb)
            {
                if (!bb.IsEmpty) bounds.Union(bb);
            }

            Union(t.Opaque.Bounds);
            Union(t.Clear.Bounds);
            if (t.Body != null) Union(t.Body.Bounds);
            if (t.Lens != null) Union(t.Lens.Bounds);
            if (t.Screen != null) Union(t.Screen.Bounds);
            foreach (var m in t.Moving) Union(m.Content.Bounds);
            t.Bounds = bounds.IsEmpty ? new Rect3D(0, 0, 0, 0.1, 0.1, 0.1) : bounds;
            t.HeatCentre = HeatCentre ?? new Point3D(t.Bounds.X + t.Bounds.SizeX / 2, t.Bounds.Y + t.Bounds.SizeY / 2, t.Bounds.Z + t.Bounds.SizeZ);
            return t;
        }
    }

    internal sealed class MovingTemplate
    {
        public MovingTemplate(string name, Model3DGroup content, Point3D pivot, Vector3D axis, Point3D tip)
        {
            Name = name;
            Content = content;
            Pivot = pivot;
            Axis = axis;
            Tip = tip;
        }

        public string Name { get; }
        public Model3DGroup Content { get; }
        public Point3D Pivot { get; }
        public Vector3D Axis { get; }
        public Point3D Tip { get; }
    }

    /// <summary>Cached, frozen geometry of one model; <see cref="Instantiate"/> adds the per-instance materials.</summary>
    internal sealed class ModelTemplate
    {
        public Model3DGroup Opaque = null!;
        public Model3DGroup Clear = null!;
        public MeshGeometry3D? Body;
        public Brush BodyBrush = Brushes.Gray;
        public Color BodyColor;
        public double BodySpec;
        public MeshGeometry3D? Lens;
        public MeshGeometry3D? Halo;
        public Color LensColor;
        public Color LightColor;
        public MeshGeometry3D? Screen;
        public string ScreenKind = string.Empty;
        public string ScreenStyle = string.Empty;
        public Color ScreenOff;
        public readonly List<MovingTemplate> Moving = new List<MovingTemplate>();
        public readonly List<(string Pad, Point3D Position, Vector3D Direction)> Anchors = new List<(string, Point3D, Vector3D)>();
        public Rect3D Bounds;
        public Point3D HeatCentre;
        public int Triangles;

        private static Brush Solid(Color c) => new SolidColorBrush(c);

        public ComponentInstance Instantiate()
        {
            var root = new Model3DGroup();
            // Draw order: opaque geometry, then everything that may sit inside a
            // translucent shell, then the translucent shells themselves.
            root.Children.Add(Opaque);
            DiffuseMaterial? overlay = null;
            EmissiveMaterial? glow = null;
            if (Body != null)
            {
                overlay = new DiffuseMaterial(Solid(Colors.Transparent));
                glow = new EmissiveMaterial(Solid(Colors.Black));
                var g = new MaterialGroup();
                g.Children.Add(new DiffuseMaterial(BodyBrush));
                g.Children.Add(overlay);
                g.Children.Add(glow);
                if (BodySpec > 0) g.Children.Add(new SpecularMaterial(Solid(Color.FromArgb((byte)Math.Min(255, BodySpec * 255), 255, 255, 255)), 30));
                root.Children.Add(new GeometryModel3D(Body, g));
            }

            var moving = new MovingPart[Moving.Count];
            for (int i = 0; i < Moving.Count; i++)
            {
                var m = Moving[i];
                var rot = new AxisAngleRotation3D(m.Axis, 0);
                var group = new Model3DGroup { Transform = new RotateTransform3D(rot, m.Pivot) };
                group.Children.Add(m.Content);
                ComponentModels.SetPartName(group, m.Name);
                root.Children.Add(group);
                moving[i] = new MovingPart(m.Name, group, rot, m.Pivot, m.Axis, m.Tip);
            }

            DiffuseMaterial? screen = null;
            if (Screen != null)
            {
                screen = new DiffuseMaterial(Solid(ScreenOff));
                var g = new MaterialGroup();
                g.Children.Add(screen);
                g.Children.Add(new EmissiveMaterial(Solid(Color.FromRgb(0x30, 0x30, 0x30))));
                root.Children.Add(new GeometryModel3D(Screen, g));
            }

            if (Clear.Children.Count > 0) root.Children.Add(Clear);

            EmissiveMaterial? light = null, halo = null;
            GeometryModel3D? haloModel = null;
            if (Lens != null)
            {
                light = new EmissiveMaterial(Solid(Colors.Black));
                var lensOverlay = new DiffuseMaterial(Solid(Colors.Transparent));
                var lensGlow = new EmissiveMaterial(Solid(Colors.Black));
                var g = new MaterialGroup();
                g.Children.Add(new DiffuseMaterial(Solid(LensColor)));
                g.Children.Add(lensOverlay);
                g.Children.Add(light);
                g.Children.Add(lensGlow);
                g.Children.Add(new SpecularMaterial(Solid(Color.FromArgb(230, 255, 255, 255)), 80));
                root.Children.Add(new GeometryModel3D(Lens, g));
                if (glow == null)
                {
                    overlay = lensOverlay;
                    glow = lensGlow;
                }

                if (Halo != null)
                {
                    halo = new EmissiveMaterial(Solid(Colors.Black));
                    var hg = new MaterialGroup();
                    hg.Children.Add(new DiffuseMaterial(Solid(Colors.Transparent)));
                    hg.Children.Add(halo);
                    haloModel = new GeometryModel3D(Halo, hg);
                }
            }

            return new ComponentInstance(root, overlay, glow, light, halo, LightColor, screen, ScreenKind, ScreenStyle, moving, Bounds, HeatCentre, Anchors)
            {
                HaloModel = haloModel
            };
        }
    }

    /// <summary>A rotating sub-part of a model instance (servo horn, motor shaft, pot knob).</summary>
    public sealed class MovingPart
    {
        internal MovingPart(string name, Model3DGroup group, AxisAngleRotation3D rotation, Point3D pivot, Vector3D axis, Point3D tip)
        {
            Name = name;
            Group = group;
            Rotation = rotation;
            Pivot = pivot;
            Axis = axis;
            Tip = tip;
        }

        /// <summary>"horn", "shaft", "knob", "rotor" …</summary>
        public string Name { get; }

        /// <summary>The sub-model; its Transform is the rotation about <see cref="Pivot"/>.</summary>
        public Model3DGroup Group { get; }

        public AxisAngleRotation3D Rotation { get; }

        /// <summary>Rotation centre and axis in the model frame (mm).</summary>
        public Point3D Pivot { get; }

        public Vector3D Axis { get; }

        /// <summary>End of the shaft / horn hub: where a wheel, propeller or arm attaches.</summary>
        public Point3D Tip { get; }

        /// <summary>Rotation in degrees (right-hand rule about <see cref="Axis"/>).</summary>
        public double Angle
        {
            get => Rotation.Angle;
            set => Rotation.Angle = value;
        }
    }

    /// <summary>
    /// One placed model: the scene graph plus the per-instance hooks used for live
    /// simulation feedback (heat glow and charring of the body, LED light, display content,
    /// moving parts). Static geometry inside is frozen and shared between instances.
    /// </summary>
    public sealed class ComponentInstance
    {
        internal ComponentInstance(Model3DGroup root, DiffuseMaterial? body, EmissiveMaterial? glow, EmissiveMaterial? light, EmissiveMaterial? halo,
            Color lightColor, DiffuseMaterial? screen, string screenKind, string screenStyle, IReadOnlyList<MovingPart> moving, Rect3D bounds, Point3D heatCentre,
            IReadOnlyList<(string Pad, Point3D Position, Vector3D Direction)> anchors)
        {
            Root = root;
            Body = body;
            Glow = glow;
            Light = light;
            Halo = halo;
            LightColor = lightColor;
            Screen = screen;
            ScreenKind = screenKind;
            ScreenStyle = screenStyle;
            Moving = moving;
            Bounds = bounds;
            HeatCentre = heatCentre;
            PadAnchors = anchors;
        }

        public Model3DGroup Root { get; }

        /// <summary>
        /// Char overlay of the body: a solid brush that is transparent (= <see cref="BaseColor"/>)
        /// normally; set it to a dark opaque colour to show a burnt part. Null without a body.
        /// </summary>
        public DiffuseMaterial? Body { get; }

        /// <summary>Heat glow of the body (black when cool).</summary>
        public EmissiveMaterial? Glow { get; }

        /// <summary>Colour of <see cref="Body"/>'s brush when the part is intact (transparent).</summary>
        public Color BaseColor => Colors.Transparent;

        /// <summary>LED / lamp emission; set with <see cref="SetBrightness"/>.</summary>
        public EmissiveMaterial? Light { get; }

        private EmissiveMaterial? Halo { get; }

        internal GeometryModel3D? HaloModel { get; set; }

        public Color LightColor { get; }

        /// <summary>Display surface whose brush shows the live OLED/LCD picture.</summary>
        public DiffuseMaterial? Screen { get; }

        /// <summary>"OLED" or "LCD" when <see cref="Screen"/> is set.</summary>
        public string ScreenKind { get; }

        /// <summary>Pixel colour (OLED) or backlight (LCD) for the display renderer.</summary>
        public string ScreenStyle { get; }

        public IReadOnlyList<MovingPart> Moving { get; }

        /// <summary>Model bounds in the model frame (mm).</summary>
        public Rect3D Bounds { get; }

        /// <summary>Where smoke and fire come out (model frame).</summary>
        public Point3D HeatCentre { get; }

        /// <summary>Cable attachment points by footprint pad name (model frame).</summary>
        public IReadOnlyList<(string Pad, Point3D Position, Vector3D Direction)> PadAnchors { get; }

        public MovingPart? Find(string name)
        {
            foreach (var m in Moving)
            {
                if (string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) return m;
            }

            return null;
        }

        /// <summary>Heat glow colour (black = cool).</summary>
        public void SetHeat(Color c)
        {
            if (Glow != null) ((SolidColorBrush)Glow.Brush).Color = c;
        }

        /// <summary>Shows or clears the burnt look.</summary>
        public void SetCharred(bool charred)
        {
            if (Body != null) ((SolidColorBrush)Body.Brush).Color = charred ? Color.FromArgb(235, 0x18, 0x14, 0x10) : Colors.Transparent;
        }

        /// <summary>LED / lamp brightness 0..1.</summary>
        public void SetBrightness(double b)
        {
            if (Light == null) return;
            b = Math.Max(0, Math.Min(1, b));
            ((SolidColorBrush)Light.Brush).Color = Color.FromRgb((byte)(LightColor.R * b), (byte)(LightColor.G * b), (byte)(LightColor.B * b));
            if (Halo == null || HaloModel == null) return;
            ((SolidColorBrush)Halo.Brush).Color = Color.FromRgb((byte)(LightColor.R * b * 0.28), (byte)(LightColor.G * b * 0.28), (byte)(LightColor.B * b * 0.28));
            // The glow shell is only in the scene while lit (an unlit shell would still hide what is behind it).
            bool shown = Root.Children.Contains(HaloModel);
            if (b > 0.03 && !shown) Root.Children.Add(HaloModel);
            else if (b <= 0.03 && shown) Root.Children.Remove(HaloModel);
        }
    }
}
