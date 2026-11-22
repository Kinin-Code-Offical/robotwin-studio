using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [launch] Recovery parachutes in the world: a canopy of alternating gores on its shroud lines, drawn
    /// where the physics puts it — downstream of the body along the relative air, opening as the canopy fills.
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _chuteVisual = new ModelVisual3D();
        private readonly Dictionary<string, Model3D> _chutes = new Dictionary<string, Model3D>(StringComparer.OrdinalIgnoreCase);
        private bool _chuteAttached;
        private static Material? _goreA, _goreB, _line;

        private static Material Flat(Color c)
        {
            var m = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(c)), new SpecularMaterial(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 12) } };
            m.Freeze();
            return m;
        }

        /// <summary>Draws (or removes, when <paramref name="open"/> ≤ 0) the parachute of an object.</summary>
        public void Parachute(string objectId, double open, Point3D attach, Vector3D pull, double diameter)
        {
            if (!_chuteAttached)
            {
                _view.Children.Add(_chuteVisual);
                _chuteAttached = true;
            }

            var group = _chuteVisual.Content as Model3DGroup ?? (Model3DGroup)(_chuteVisual.Content = new Model3DGroup());
            if (_chutes.TryGetValue(objectId, out var old))
            {
                group.Children.Remove(old);
                _chutes.Remove(objectId);
            }

            if (open <= 0 || pull.LengthSquared < 1e-12) return;
            _goreA ??= Flat(Color.FromRgb(0xF2, 0x6B, 0x1D));
            _goreB ??= Flat(Color.FromRgb(0xF4, 0xF1, 0xEA));
            _line ??= Flat(Color.FromRgb(0x30, 0x30, 0x34));
            pull.Normalize();
            // Canopy frame: w along the pull, u and v across it.
            var helper = Math.Abs(pull.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(helper, pull);
            u.Normalize();
            var v = Vector3D.CrossProduct(pull, u);
            double fill = Math.Max(0.15, Math.Min(1, Math.Sqrt(open)));
            double r = diameter / 2 * fill, lines = diameter * 1.1, depth = diameter * 0.32 * (0.6 + 0.4 * fill);
            var skirt = attach + pull * lines;
            const int gores = 12, rings = 6;
            var model = new Model3DGroup();
            var a = new MeshGeometry3D();
            var b = new MeshGeometry3D();
            Point3D At(double ring, double gore)
            {
                double t = ring / rings, ang = 2 * Math.PI * gore / gores;
                double rr = r * Math.Cos(t * Math.PI / 2), h = depth * Math.Sin(t * Math.PI / 2);
                return skirt + pull * h + (u * Math.Cos(ang) + v * Math.Sin(ang)) * rr;
            }

            for (int g = 0; g < gores; g++)
            {
                var mesh = g % 2 == 0 ? a : b;
                for (int k = 0; k < rings; k++)
                {
                    int i0 = mesh.Positions.Count;
                    mesh.Positions.Add(At(k, g));
                    mesh.Positions.Add(At(k, g + 1));
                    mesh.Positions.Add(At(k + 1, g + 1));
                    mesh.Positions.Add(At(k + 1, g));
                    mesh.TriangleIndices.Add(i0);
                    mesh.TriangleIndices.Add(i0 + 1);
                    mesh.TriangleIndices.Add(i0 + 2);
                    mesh.TriangleIndices.Add(i0);
                    mesh.TriangleIndices.Add(i0 + 2);
                    mesh.TriangleIndices.Add(i0 + 3);
                }
            }

            a.Freeze();
            b.Freeze();
            model.Children.Add(new GeometryModel3D(a, _goreA) { BackMaterial = _goreA });
            model.Children.Add(new GeometryModel3D(b, _goreB) { BackMaterial = _goreB });

            // Shroud lines: a thin flat ribbon from each gore seam to the attachment point.
            var lm = new MeshGeometry3D();
            double w = Math.Max(0.0008, diameter * 0.004);
            for (int g = 0; g < gores; g += 2)
            {
                var top = At(0, g);
                var dir = top - attach;
                var side = Vector3D.CrossProduct(dir, pull);
                if (side.LengthSquared < 1e-12) side = u;
                side.Normalize();
                side *= w / 2;
                int i0 = lm.Positions.Count;
                lm.Positions.Add(attach - side);
                lm.Positions.Add(attach + side);
                lm.Positions.Add(top + side);
                lm.Positions.Add(top - side);
                lm.TriangleIndices.Add(i0);
                lm.TriangleIndices.Add(i0 + 1);
                lm.TriangleIndices.Add(i0 + 2);
                lm.TriangleIndices.Add(i0);
                lm.TriangleIndices.Add(i0 + 2);
                lm.TriangleIndices.Add(i0 + 3);
            }

            lm.Freeze();
            model.Children.Add(new GeometryModel3D(lm, _line) { BackMaterial = _line });
            model.Freeze();
            group.Children.Add(model);
            _chutes[objectId] = model;
        }

        /// <summary>Per frame: every body with a parachute, as far open as the physics (or the replayed run) says.</summary>
        private void ChuteFrame()
        {
            var physics = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Physics : null;
            if (World == null || physics == null && Playback == null)
            {
                if (_chutes.Count > 0) ClearParachutes();
                return;
            }

            foreach (var o in World.Objects)
            {
                if (!o.Parameters.ContainsKey("parachute")) continue;
                double open, diameter = double.TryParse(o.Parameters["parachute"], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? dv : 0.45;
                Point3D attach;
                Vector3D pull;
                if (Playback is { } pb)
                {
                    open = pb.Value("chute/" + o.Id + "/open");
                    if (double.IsNaN(open) || pb.PoseOf(o.Id) is not { } pose) continue;
                    var top = pose.Position + pose.Orientation.Rotate(new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, o.Shape == "sphere" ? o.SizeX : o.SizeZ));
                    attach = new Point3D(top.X, top.Y, top.Z);
                    pull = new Vector3D(pb.Value("chute/" + o.Id + "/px"), pb.Value("chute/" + o.Id + "/py"), pb.Value("chute/" + o.Id + "/pz"));
                }
                else
                {
                    if (physics!.ParachuteOf(o.Id) is not { } c) continue;
                    open = c.Deployed ? Math.Max(0.05, c.Open) : 0;
                    attach = new Point3D(c.Attach.X, c.Attach.Y, c.Attach.Z);
                    pull = new Vector3D(c.Pull.X, c.Pull.Y, c.Pull.Z);
                    diameter = c.Diameter;
                }

                if (open <= 0)
                {
                    if (_chutes.ContainsKey(o.Id)) Parachute(o.Id, 0, default, default, 0);
                    continue;
                }

                Parachute(o.Id, open, attach, pull, diameter);
            }
        }

        /// <summary>Removes every drawn parachute (the simulation stopped).</summary>
        public void ClearParachutes()
        {
            _chutes.Clear();
            _chuteVisual.Content = null;
        }
    }
}
