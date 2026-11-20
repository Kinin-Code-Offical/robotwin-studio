using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Assembly3D
{
    /// <summary>Turns an imported <see cref="MeshModel"/> into WPF 3D geometry (one model per colour, flat shaded).</summary>
    public static class MeshVisual
    {
        public static Model3DGroup Build(MeshModel mesh, Color? fallback = null, double specular = 0.25)
        {
            var group = new Model3DGroup();
            var byColor = new Dictionary<uint, MeshGeometry3D>();
            var baseColor = fallback ?? Color.FromRgb(0xB4, 0xB4, 0xB4);
            bool hasColors = mesh.TriangleColors != null || mesh.VertexColors != null || mesh.Groups.Count > 0;
            for (int t = 0; t < mesh.TriangleCount; t++)
            {
                var c = hasColors ? mesh.ColorOf(t) : MeshColor.DefaultGrey;
                uint key = hasColors ? (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B) : 0u;
                if (!byColor.TryGetValue(key, out var geo))
                {
                    geo = new MeshGeometry3D();
                    byColor[key] = geo;
                }

                var a = mesh.Vertices[mesh.Indices[3 * t]];
                var b = mesh.Vertices[mesh.Indices[3 * t + 1]];
                var d = mesh.Vertices[mesh.Indices[3 * t + 2]];
                var n = (b - a).Cross(d - a).Normalized();
                var normal = new Vector3D(n.X, n.Y, n.Z);
                int i = geo.Positions.Count;
                geo.Positions.Add(new Point3D(a.X, a.Y, a.Z));
                geo.Positions.Add(new Point3D(b.X, b.Y, b.Z));
                geo.Positions.Add(new Point3D(d.X, d.Y, d.Z));
                geo.Normals.Add(normal);
                geo.Normals.Add(normal);
                geo.Normals.Add(normal);
                geo.TriangleIndices.Add(i);
                geo.TriangleIndices.Add(i + 1);
                geo.TriangleIndices.Add(i + 2);
            }

            foreach (var kv in byColor)
            {
                kv.Value.Freeze();
                var color = kv.Key == 0 && !hasColors ? baseColor : Color.FromArgb((byte)(kv.Key >> 24), (byte)(kv.Key >> 16), (byte)(kv.Key >> 8), (byte)kv.Key);
                group.Children.Add(Mesh.Model(kv.Value, Mesh.Matte(color, specular)));
            }

            return group;
        }

        /// <summary>Typical colour of a chassis material.</summary>
        public static Color MaterialColor(string material) => (material ?? string.Empty).ToUpperInvariant() switch
        {
            "PETG" => Color.FromRgb(0x2E, 0x7D, 0xB8),
            "ABS" => Color.FromRgb(0x3A, 0x3A, 0x40),
            "AKRİLİK" or "AKRILIK" => Color.FromArgb(0xD8, 0x9E, 0xC9, 0xE0),
            "ALÜMİNYUM" => Color.FromRgb(0xB8, 0xBC, 0xC4),
            "ÇELİK" => Color.FromRgb(0x8A, 0x8E, 0x96),
            "KARBON" => Color.FromRgb(0x22, 0x22, 0x26),
            "MDF" or "KONTRPLAK" => Color.FromRgb(0xC8, 0xA0, 0x6E),
            "FR4" => Color.FromRgb(0x1E, 0x56, 0x31),
            _ => Color.FromRgb(0xE8, 0x7A, 0x2C)
        };
    }
}
