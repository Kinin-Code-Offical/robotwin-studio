using System;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Models3D;

namespace RobotWin.Studio.World
{
    /// <summary>[drive] World models of the gears, pulleys, bearings, shafts and screws (<see cref="DriveParts"/>).</summary>
    public static class DriveModels
    {
        private const double Mm = 0.001;

        /// <summary>Model of a part in its own frame (mm; the axis along +Z from z = 0).</summary>
        public static ComponentInstance Instance(DrivePartSpec spec, string? colour = null) =>
            ComponentModels.Template(new ModelRequest("drive", spec.Id + "|" + (colour ?? spec.Color), b => Gen.DrivePart(b, spec, colour))).Instantiate();

        public static ObjectModel? TryBuild(WorldObject o)
        {
            var spec = DriveParts.Of(o);
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
            var root = new Model3DGroup { Transform = new MatrixTransform3D(new Matrix3D(Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, 1)) };
            root.Children.Add(inst.Root);
            m.Body.Children.Add(root);
            var b = inst.Bounds;
            m.Bounds = new Rect3D(b.X * Mm, b.Y * Mm, b.Z * Mm, b.SizeX * Mm, b.SizeY * Mm, b.SizeZ * Mm);
            if (DriveParts.Geometry(o) is { } g) m.CollisionBounds = new Rect3D(g.Min.X, g.Min.Y, g.Min.Z, g.Max.X - g.Min.X, g.Max.Y - g.Min.Y, g.Max.Z - g.Min.Z);
            return m;
        }
    }
}
