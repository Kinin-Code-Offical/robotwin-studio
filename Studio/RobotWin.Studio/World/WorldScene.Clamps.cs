using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    // [phys] An object marked "Sabit" is fixed to the table in the physics; show how: four screw
    // heads on its top face (bodies) or two small clamp jaws at its ends (parts), so a stand that
    // never tips is visibly screwed down rather than mysteriously stable.
    public sealed partial class WorldScene
    {
        private static Model3D? FixingMarks(WorldObject o, ObjectModel model)
        {
            if (!o.IsStatic) return null;
            if (DriveParts.Of(o) is { } drive) return DriveFixing(drive);   // [drive] its own way of being held
            var b = model.CollisionBounds.IsEmpty ? model.Bounds : model.CollisionBounds;
            if (b.IsEmpty || b.SizeX < 0.004 || b.SizeY < 0.004) return null;
            var steel = M.Matte(Color.FromRgb(0x9A, 0x9C, 0xA2), 0.4);
            var slot = M.Matte(Color.FromRgb(0x2A, 0x2A, 0x2E));
            var g = new Model3DGroup();
            double top = b.Z + b.SizeZ;
            if (o.Kind == WorldObjectKind.Body)
            {
                double inset = Math.Min(0.006, Math.Min(b.SizeX, b.SizeY) * 0.2);
                double r = Math.Min(0.0028, Math.Min(b.SizeX, b.SizeY) * 0.08);
                foreach (var sx in new[] { -1, 1 })
                foreach (var sy in new[] { -1, 1 })
                {
                    double x = b.X + b.SizeX / 2 + sx * (b.SizeX / 2 - inset), y = b.Y + b.SizeY / 2 + sy * (b.SizeY / 2 - inset);
                    g.Children.Add(M.Model(M.Cylinder(r, 0.0012, 2, 16, x, y, top + 0.0006), steel));
                    g.Children.Add(M.Model(M.Box(x, y, top + 0.0013, r * 1.6, r * 0.35, 0.0004), slot));
                }
            }
            else
            {
                // Small clamp jaws over both ends.
                double y = b.Y + b.SizeY / 2, w = Math.Min(0.01, b.SizeY * 0.6);
                foreach (var x in new[] { b.X - 0.002, b.X + b.SizeX + 0.002 })
                {
                    g.Children.Add(M.Model(M.Box(x, y, top / 2 + 0.001, 0.003, w, top + 0.002), steel));
                    double inward = x < b.X ? 0.003 : -0.003;
                    g.Children.Add(M.Model(M.Box(x + inward, y, top + 0.0015, 0.008, w, 0.003), steel));
                }
            }

            return g;
        }

        /// <summary>
        /// [drive] How a fixed drive part is held: a pillow block by two M6 bolts with washers in its base slots; a bearing
        /// pressed into its housing and a guide rod clamped in its blocks show nothing on themselves.
        /// </summary>
        private static Model3D? DriveFixing(DrivePartSpec s)
        {
            if (s.Kind != DrivePartKind.BearingHousing) return null;
            var steel = M.Matte(Color.FromRgb(0x9A, 0x9C, 0xA2), 0.4);
            var g = new Model3DGroup();
            foreach (double x in new[] { -0.021, 0.021 })
            {
                g.Children.Add(M.Model(M.Cylinder(0.006, 0.0012, 2, 24, x, 0, 0.0056), steel));   // washer on the base
                g.Children.Add(M.Model(M.Cylinder(0.0058, 0.004, 2, 6, x, 0, 0.0082), steel));   // hex head, 10 mm across flats
            }

            return g;
        }
    }
}
