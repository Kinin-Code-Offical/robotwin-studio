using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Models3D
{
    /// <summary>User-assigned meshes (STL/OBJ/PLY) for parts, drawn in place of the generated model.</summary>
    public static class CustomModels
    {
        static CustomModels()
        {
            // A new or replaced model must not be served from the template cache.
            PartModels3D.Changed += (_, _) => ComponentModels.ClearCache();
        }

        /// <summary>
        /// Loads a mesh file as a part model. Files whose size suggests millimetres
        /// (anything larger than half a metre when read as metres) are scaled to metres.
        /// </summary>
        public static PartModel3D Load(string path)
        {
            var mesh = MeshModel.Load(path);
            if (mesh.TriangleCount == 0) throw new InvalidDataException("Modelde üçgen yok.");
            var size = mesh.Bounds.Size;
            double extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (MeshModel.FormatFromExtension(path) != MeshFileFormat.Stl && extent > 0.5) mesh = mesh.Scale(0.001);
            // Rest the model on the board: its lowest point on z = 0, centred in x/y.
            var b = mesh.Bounds;
            mesh = mesh.Translate(new Vec3(-(b.Min.X + b.Max.X) / 2, -(b.Min.Y + b.Max.Y) / 2, -b.Min.Z));
            return new PartModel3D { Mesh = mesh.ToEmbedded(), Name = Path.GetFileName(path) };
        }

        internal static bool TryBuild(PartBuilder b, ModelRequest r)
        {
            var model = PartModels3D.For(r.P);
            if (model == null) return false;
            MeshModel mesh;
            try
            {
                mesh = MeshModel.FromEmbedded(model.Mesh);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }

            var m = Mx.S(model.Scale <= 0 ? 1000 : model.Scale * 1000) * Mx.Rx(model.RotationX) * Mx.Ry(model.RotationY) * Mx.Rz(model.RotationZ) *
                    Mx.T(model.OffsetX, model.OffsetY, model.OffsetZ);
            // Undo the builder's layout-Y flip: the mesh is already in the right-handed model frame.
            using (b.At(m * Mx.S(1, -1, 1)))
            {
                Color fallback = Color.FromRgb(0xB4, 0xB4, 0xB4);
                if (!string.IsNullOrWhiteSpace(model.Color))
                {
                    try
                    {
                        fallback = (Color)ColorConverter.ConvertFromString(model.Color);
                    }
                    catch (FormatException)
                    {
                    }
                }

                bool colours = mesh.TriangleColors != null || mesh.VertexColors != null || mesh.Groups.Count > 0;
                var byColour = new Dictionary<uint, MeshBuilder>();
                for (int t = 0; t < mesh.TriangleCount; t++)
                {
                    var c = colours ? mesh.ColorOf(t) : new MeshColor(fallback.R, fallback.G, fallback.B);
                    uint key = c.ToArgb();
                    if (!byColour.TryGetValue(key, out var mb))
                    {
                        var col = Color.FromArgb(c.A, c.R, c.G, c.B);
                        mb = c.A < 250 ? b[Mat.Clear(col, 0.4)] : b[Mat.Plastic(col, 0.25, 25)];
                        byColour[key] = mb;
                    }

                    var (p0, p1, p2) = mesh.Triangle(t);
                    mb.Triangle(new Point3D(p0.X, p0.Y, p0.Z), new Point3D(p1.X, p1.Y, p1.Z), new Point3D(p2.X, p2.Y, p2.Z));
                }
            }

            return true;
        }
    }

}
