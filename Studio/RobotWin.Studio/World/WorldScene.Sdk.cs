using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sdk;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sdk] Wind and force-field visualisation: a grid of wind arrows over the floor, a wind
    /// sock in a corner (stands out straight in strong wind, droops in calm air) and the
    /// outlines of field regions (fans, conveyors, bounded fields). Live from the running
    /// world's wind model; a preview of the mean wind and the fans while stopped.
    /// WorldScene.cs only calls InitSdk() and SdkFrame().
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _sdkLayer = new ModelVisual3D();
        private DateTime _sdkNext = DateTime.MinValue;
        private string _sdkSignature = string.Empty;

        private static readonly Color WindArrow = Color.FromRgb(0x5A, 0xA9, 0xE6);
        private static readonly Color SockOrange = Color.FromRgb(0xF2, 0x6B, 0x1D);
        private static readonly Color SockWhite = Color.FromRgb(0xED, 0xED, 0xED);
        private static readonly Color Pole = Color.FromRgb(0x9A, 0x9A, 0xA2);

        /// <summary>Show wind arrows and field outlines (the world's "Rüzgârı göster" switch).</summary>
        private bool SdkVisible => World?.Sdk.ShowWind ?? false;

        private void InitSdk()
        {
            _sdkLayer.Content = null;
        }

        /// <summary>Forces the wind layer to be rebuilt on the next frame (after an Ortam edit).</summary>
        public void InvalidateWind()
        {
            _sdkSignature = string.Empty;
            _sdkNext = DateTime.MinValue;
        }

        private void SdkFrame()
        {
            if (!_view.Children.Contains(_sdkLayer)) _view.Children.Add(_sdkLayer);
            var now = DateTime.Now;
            if (now < _sdkNext) return;
            var world = World;
            if (world == null || !SdkVisible)
            {
                _sdkLayer.Content = null;
                _sdkNext = now.AddMilliseconds(500);
                return;
            }

            var runtime = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Sdk : null;
            Func<Vec3, Vec3> wind;
            if (runtime != null)
            {
                wind = runtime.WindAt;
                _sdkNext = now.AddMilliseconds(100);
                _sdkSignature = string.Empty;
            }
            else
            {
                _sdkNext = now.AddMilliseconds(400);
                string sig = JsonSerializer.Serialize(world.Sdk.Wind) + JsonSerializer.Serialize(world.Sdk.Fields) +
                             world.Environment.Width + "|" + world.Environment.Depth + "|" + world.Environment.WindX + "|" + world.Environment.WindY;
                if (sig == _sdkSignature && _sdkLayer.Content != null) return;
                _sdkSignature = sig;
                wind = WorldSdkRuntime.Preview(world);
            }

            _sdkLayer.Content = BuildWindModel(world, wind);
        }

        private Model3DGroup BuildWindModel(RobotTwin.CoreSim.Sandbox.WorldDocument world, Func<Vec3, Vec3> wind)
        {
            var g = new Model3DGroup();
            var env = world.Environment;
            double w = Math.Max(0.5, env.Width), d = Math.Max(0.5, env.Depth);
            double size = Math.Max(w, d);
            int nx = Math.Max(3, Math.Min(9, (int)Math.Round(w / size * 8))), ny = Math.Max(3, Math.Min(9, (int)Math.Round(d / size * 8)));
            double cell = Math.Min(w / nx, d / ny);
            double height = Math.Min(0.6, Math.Max(0.08, size * 0.04));
            var arrowMat = M.Matte(WindArrow);
            bool any = false;
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < ny; j++)
                {
                    var p = new Vec3(-w / 2 + (i + 0.5) * w / nx, -d / 2 + (j + 0.5) * d / ny, height);
                    any |= AddArrow(g, p, wind(p), cell, arrowMat);
                }
            }

            foreach (var f in world.Sdk.Fields.Where(f => f.Enabled)) AddField(g, f, wind, cell);
            if (any || world.Sdk.Fields.Count > 0 || world.Sdk.Wind.Speed > 0) AddSock(g, world, wind, size);
            return g;
        }

        /// <summary>An arrow at p along the wind; length grows with speed up to one cell at 10 m/s.</summary>
        private static bool AddArrow(Model3DGroup g, Vec3 p, Vec3 v, double cell, Material mat)
        {
            double speed = v.Length;
            if (speed < 0.05 || double.IsNaN(speed)) return false;
            double len = cell * 0.85 * Math.Min(1.0, 0.15 + speed / 10.0);
            var dir = v / speed;
            var a = new Point3D(p.X - dir.X * len / 2, p.Y - dir.Y * len / 2, p.Z - dir.Z * len / 2);
            var b = new Point3D(p.X + dir.X * len / 2, p.Y + dir.Y * len / 2, p.Z + dir.Z * len / 2);
            double r = Math.Max(0.0015, cell * 0.012);
            var headBase = new Point3D(b.X - dir.X * r * 6, b.Y - dir.Y * r * 6, b.Z - dir.Z * r * 6);
            g.Children.Add(new GeometryModel3D(Edge(a, headBase, r), mat));
            g.Children.Add(new GeometryModel3D(Cone(headBase, new Vector3D(dir.X, dir.Y, dir.Z), r * 3, r * 6), mat));
            return true;
        }

        private static MeshGeometry3D Cone(Point3D baseCentre, Vector3D axis, double radius, double length, int sides = 12)
        {
            axis.Normalize();
            var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0));
            u.Normalize();
            var v = Vector3D.CrossProduct(axis, u);
            var mesh = new MeshGeometry3D();
            var tip = baseCentre + axis * length;
            mesh.Positions.Add(tip);
            mesh.Positions.Add(baseCentre);
            for (int i = 0; i < sides; i++)
            {
                double a = 2 * Math.PI * i / sides;
                mesh.Positions.Add(baseCentre + (u * Math.Cos(a) + v * Math.Sin(a)) * radius);
            }

            for (int i = 0; i < sides; i++)
            {
                int k = 2 + i, n = 2 + (i + 1) % sides;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(k);
                mesh.TriangleIndices.Add(n);
                mesh.TriangleIndices.Add(1);
                mesh.TriangleIndices.Add(n);
                mesh.TriangleIndices.Add(k);
            }

            return mesh;
        }

        /// <summary>Wind sock on a pole in the far corner: points downwind, rises with speed (full at ~7 m/s).</summary>
        private static void AddSock(Model3DGroup g, RobotTwin.CoreSim.Sandbox.WorldDocument world, Func<Vec3, Vec3> wind, double size)
        {
            var env = world.Environment;
            double h = Math.Min(1.2, Math.Max(0.25, size * 0.12));
            var foot = new Point3D(env.Width / 2 - h * 0.4, env.Depth / 2 - h * 0.4, 0);
            var top = new Point3D(foot.X, foot.Y, h);
            g.Children.Add(new GeometryModel3D(Edge(foot, top, h * 0.012), M.Matte(Pole)));
            var v = wind(new Vec3(top.X, top.Y, top.Z));
            var horiz = new Vector3D(v.X, v.Y, 0);
            double speed = horiz.Length;
            if (speed < 1e-6) horiz = new Vector3D(1, 0, 0);
            horiz.Normalize();
            double lift = Math.Min(1.0, speed / 7.0);
            double elev = (-80 + 78 * lift) * Math.PI / 180;
            var dir = horiz * Math.Cos(elev) + new Vector3D(0, 0, Math.Sin(elev));
            double segment = h * 0.16, r0 = h * 0.07;
            var p = top;
            for (int k = 0; k < 5; k++)
            {
                var q = p + dir * segment;
                double r = r0 * (1 - 0.13 * k);
                g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { p, q }, r, 14), M.Matte(k % 2 == 0 ? SockOrange : SockWhite)));
                p = q;
            }
        }

        private static void AddField(Model3DGroup g, ForceFieldSpec f, Func<Vec3, Vec3> wind, double cell)
        {
            var colour = (f.Kind ?? string.Empty).ToLowerInvariant() switch
            {
                "fan" => Color.FromRgb(0x4C, 0xC9, 0xF0),
                "conveyor" => Color.FromRgb(0xF2, 0xC1, 0x4E),
                "vortex" => Color.FromRgb(0x7E, 0xE0, 0x81),
                _ => Color.FromRgb(0xB3, 0x88, 0xFF)
            };
            var mat = M.Matte(colour);
            double r = Math.Max(0.0012, cell * 0.006);
            bool boxed = f.Bounded || f.Kind == "fan" || f.Kind == "conveyor";
            if (boxed)
            {
                var q = f.Orientation;
                var h = f.Size * 0.5;
                var corners = new Point3D[8];
                int n = 0;
                foreach (var sx in new[] { -1, 1 })
                foreach (var sy in new[] { -1, 1 })
                foreach (var sz in new[] { -1, 1 })
                {
                    var c = f.Center + q.Rotate(new Vec3(sx * h.X, sy * h.Y, sz * h.Z));
                    corners[n++] = new Point3D(c.X, c.Y, c.Z);
                }

                foreach (var (a, b) in new[] { (0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7) })
                {
                    g.Children.Add(new GeometryModel3D(Edge(corners[a], corners[b], r), mat));
                }
            }

            switch ((f.Kind ?? string.Empty).ToLowerInvariant())
            {
                case "fan":
                {
                    // Arrows along the stream's centre line show its speed decaying downstream.
                    var dir = f.Direction;
                    var dl = f.Orientation.InverseRotate(dir);
                    double half = Math.Abs(dl.X) * f.SizeX / 2 + Math.Abs(dl.Y) * f.SizeY / 2 + Math.Abs(dl.Z) * f.SizeZ / 2;
                    for (int k = 0; k < 4; k++)
                    {
                        var p = f.Center + dir * (-half + (k + 0.5) * 2 * half / 4);
                        AddArrow(g, p, wind(p), Math.Max(cell * 0.5, half / 2.2), mat);
                    }

                    break;
                }

                case "radial":
                    g.Children.Add(new GeometryModel3D(M.Sphere(Math.Max(0.01, cell * 0.06), f.X, f.Y, f.Z), mat));
                    break;
                case "vortex":
                {
                    var axis = f.Direction.LengthSquared > 0.5 ? f.Direction : Vec3.UnitZ;
                    var u = axis.AnyPerpendicular().Normalized();
                    var v = axis.Cross(u);
                    var pts = new List<Point3D>();
                    for (int k = 0; k <= 32; k++)
                    {
                        double a = 2 * Math.PI * k / 32;
                        var p = f.Center + (u * Math.Cos(a) + v * Math.Sin(a)) * Math.Max(0.02, f.Radius);
                        pts.Add(new Point3D(p.X, p.Y, p.Z));
                    }

                    g.Children.Add(new GeometryModel3D(Tube(pts, r, 6), mat));
                    break;
                }

                case "uniform" when !f.Bounded:
                case "expression" when !f.Bounded:
                    break;
            }
        }
    }
}
