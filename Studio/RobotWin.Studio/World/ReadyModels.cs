using System;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Models3D;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>[objs] Live parts of a ready wheel / propeller model (spinning blades and their blur disc).</summary>
    public sealed class ReadyVisual
    {
        internal ReadyVisual(ReadyPartSpec spec, Model3DGroup holder, Model3DGroup? blades, GeometryModel3D? blur, SolidColorBrush? blurBrush, Color colour)
        {
            Spec = spec;
            Holder = holder;
            Blades = blades;
            Blur = blur;
            BlurBrush = blurBrush;
            Colour = colour;
        }

        public ReadyPartSpec Spec { get; }

        /// <summary>Group holding the blades and (while spinning) the blur disc.</summary>
        public Model3DGroup Holder { get; }

        public Model3DGroup? Blades { get; }
        public GeometryModel3D? Blur { get; }
        public SolidColorBrush? BlurBrush { get; }
        public Color Colour { get; }

        private double _shown = -1;

        /// <summary>
        /// Shows a propeller at a speed (rpm): the blades alone when slow, blades fading into a translucent
        /// disc around 300–1500 rpm, only the disc above (the eye — and a 60 Hz frame — no longer sees blades).
        /// </summary>
        public void ShowSpeed(double rpm)
        {
            if (Blur == null || BlurBrush == null || Blades == null) return;
            double s = Math.Abs(rpm);
            double k = Math.Max(0, Math.Min(1, (s - 300) / 1200));
            if (Math.Abs(k - _shown) < 0.01) return;
            _shown = k;
            bool disc = k > 0.001;
            bool blades = k < 0.999;
            if (disc && !Holder.Children.Contains(Blur)) Holder.Children.Add(Blur);
            else if (!disc && Holder.Children.Contains(Blur)) Holder.Children.Remove(Blur);
            if (blades && !Holder.Children.Contains(Blades)) Holder.Children.Insert(0, Blades);
            else if (!blades && Holder.Children.Contains(Blades)) Holder.Children.Remove(Blades);
            double alpha = (Spec.Blades >= 3 ? 0.5 : 0.4) * k;
            BlurBrush.Color = Color.FromArgb((byte)(255 * alpha), Colour.R, Colour.G, Colour.B);
        }
    }

    /// <summary>[objs] World models of the ready-made parts (<see cref="ReadyParts"/>).</summary>
    public static class ReadyModels
    {
        private const double Mm = 0.001;
        private static readonly ConditionalWeakTable<ObjectModel, ReadyVisual> Visuals = new ConditionalWeakTable<ObjectModel, ReadyVisual>();

        /// <summary>Model of a ready part in its model frame (mm; wheels centred on the hub, propellers on the hub bottom).</summary>
        public static ComponentInstance Instance(ReadyPartSpec spec, string? colour = null) =>
            ComponentModels.Template(new ModelRequest("ready", spec.Id + "|" + (colour ?? spec.Color), b => Gen.ReadyPart(b, spec, colour))).Instantiate();

        public static ReadyVisual? VisualOf(ObjectModel m) => Visuals.TryGetValue(m, out var v) ? v : null;

        public static ObjectModel? TryBuild(WorldObject o)
        {
            var spec = ReadyParts.Of(o);
            if (spec == null) return null;
            ComponentInstance inst;
            try
            {
                inst = Instance(spec, o.Color);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                return null;
            }

            var m = new ObjectModel { Instance = inst };
            double lift = spec.IsWheel ? spec.Radius : 0;
            var holder = new Model3DGroup();
            var scale = new Matrix3D(Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, Mm, 0, 0, 0, lift, 1);
            var root = new Model3DGroup { Transform = new MatrixTransform3D(scale) };
            root.Children.Add(inst.Root);
            holder.Children.Add(root);
            m.Body.Children.Add(holder);
            if (inst.Glow != null) m.Heat = inst.Glow;
            var b = inst.Bounds;
            m.Bounds = new Rect3D(b.X * Mm, b.Y * Mm, b.Z * Mm + lift, b.SizeX * Mm, b.SizeY * Mm, b.SizeZ * Mm);
            if (ReadyParts.Geometry(o) is { } g) m.CollisionBounds = new Rect3D(g.Min.X, g.Min.Y, g.Min.Z, g.Max.X - g.Min.X, g.Max.Y - g.Min.Y, g.Max.Z - g.Min.Z);

            GeometryModel3D? blur = null;
            SolidColorBrush? brush = null;
            var colour = ParseColour(o.Color ?? spec.Color);
            if (spec.Kind == ReadyPartKind.Propeller)
            {
                // Translucent swept disc (thin; the hub stays solid inside it).
                brush = new SolidColorBrush(Color.FromArgb(0, colour.R, colour.G, colour.B));
                var mat = new DiffuseMaterial(brush);
                blur = new GeometryModel3D(M.Cylinder(spec.Radius, 0.0004, 2, 72, 0, 0, spec.Width / 2), mat) { BackMaterial = mat };
            }

            Visuals.AddOrUpdate(m, new ReadyVisual(spec, holder, root, blur, brush, colour));
            return m;
        }

        private static Color ParseColour(string? hex)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex) ? Colors.Gray : (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                return Colors.Gray;
            }
        }
    }
}
