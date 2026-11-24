using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    // [kin] An arm's pose shown before it is run (servo horns turned to solved angles) and its reach, target and
    // skeleton drawn over the scene (Pages/WorldPage.Kinematics.cs).
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _kinematicsVisual = new ModelVisual3D();
        private IReadOnlyList<Vec3>? _reachSource;
        private GeometryModel3D? _reachModel;
        private (double Z, Action<Vec3> Done)? _pointPick;

        /// <summary>True while the next left click picks a point (see <see cref="PickPoint"/>).</summary>
        public bool PickingPoint => _pointPick != null;

        /// <summary>The next left click in the view gives the point on the plane z = <paramref name="planeZ"/> under it.</summary>
        public void PickPoint(double planeZ, Action<Vec3> done)
        {
            _pointPick = (planeZ, done);
            Cursor = Cursors.Cross;
        }

        public void CancelPointPick()
        {
            _pointPick = null;
            ClearValue(CursorProperty);
        }

        /// <summary>A pending point pick takes the click (true when it did).</summary>
        private bool PointPickDown(MouseButtonEventArgs e, Point p)
        {
            if (_pointPick is not { } pick || e.ChangedButton != MouseButton.Left) return false;
            var hit = OnPlane(p, pick.Z);
            CancelPointPick();
            if (hit is { } h) pick.Done(new Vec3(h.X, h.Y, h.Z));
            e.Handled = true;
            return true;
        }

        /// <summary>Servo horn / motor shaft angles (rad, by object id) shown while the world is stopped; null = as built.</summary>
        public IReadOnlyDictionary<string, double>? PreviewAngles { get; private set; }

        /// <summary>Turns the horns to <paramref name="angles"/> while the world is stopped (null puts them back as built).</summary>
        public void Preview(IReadOnlyDictionary<string, double>? angles)
        {
            PreviewAngles = angles;
            UpdateKinematics();
        }

        private double PreviewAngle(string objectId) => PreviewAngles != null && PreviewAngles.TryGetValue(objectId, out double a) ? a : 0;

        /// <summary>
        /// Draws an arm's reach (small points), its skeleton (axis points to the tip), the target and the point it
        /// reaches; everything null clears the drawing.
        /// </summary>
        public void ShowArm(IReadOnlyList<Vec3>? reach, Transform3 reachMotion, IReadOnlyList<Vec3>? skeleton, Vec3? target, Vec3? reached)
        {
            if (reach == null && skeleton == null && target == null && reached == null)
            {
                _kinematicsVisual.Content = null;
                return;
            }

            var g = new Model3DGroup();
            if (reach is { Count: > 0 })
            {
                if (!ReferenceEquals(reach, _reachSource) || _reachModel == null)
                {
                    // Built once per reach and moved with the base (a free base settles and slides while it runs).
                    var mesh = new MeshGeometry3D();
                    foreach (var p in reach) Octahedron(mesh, p, 0.0012);
                    mesh.Freeze();
                    _reachModel = M.Model(mesh, Glow(Color.FromRgb(0x6E, 0x9B, 0xFF), 0.55));
                    _reachModel.Freeze();
                    _reachSource = reach;
                }

                var moved = new Model3DGroup { Transform = new MatrixTransform3D(Matrix(reachMotion)) };
                moved.Children.Add(_reachModel);
                g.Children.Add(moved);
            }

            if (skeleton is { Count: > 1 })
            {
                var pts = new List<Point3D>();
                foreach (var p in skeleton) pts.Add(new Point3D(p.X, p.Y, p.Z));
                g.Children.Add(M.Model(Tube(pts, 0.0011, 6), Glow(Color.FromRgb(0xF0, 0xC0, 0x40), 0.9)));
                var joints = new MeshGeometry3D();
                for (int i = 0; i < skeleton.Count - 1; i++) Octahedron(joints, skeleton[i], 0.0028);
                joints.Freeze();
                g.Children.Add(M.Model(joints, Glow(Color.FromRgb(0xF0, 0xC0, 0x40), 0.9)));
            }

            if (target is { } t)
            {
                var mesh = new MeshGeometry3D();
                Octahedron(mesh, t, 0.004);
                mesh.Freeze();
                g.Children.Add(M.Model(mesh, Glow(Color.FromRgb(0xF0, 0x8A, 0x3C), 1)));
            }

            if (reached is { } r)
            {
                var mesh = new MeshGeometry3D();
                Octahedron(mesh, r, 0.0025);
                mesh.Freeze();
                g.Children.Add(M.Model(mesh, Glow(Color.FromRgb(0x3F, 0xC1, 0x6A), 1)));
            }

            g.Freeze();
            if (!_view.Children.Contains(_kinematicsVisual)) _view.Children.Add(_kinematicsVisual);
            _kinematicsVisual.Content = g;
        }

        private static Matrix3D Matrix(Transform3 t)
        {
            var m = Matrix3D.Identity;
            m.Rotate(new Quaternion(t.Q.X, t.Q.Y, t.Q.Z, t.Q.W));
            m.Translate(new Vector3D(t.P.X, t.P.Y, t.P.Z));
            return m;
        }

        private readonly ModelVisual3D _pathsVisual = new ModelVisual3D();

        /// <summary>[mobile] A vehicle's estimated (odometry, orange) and true (green) paths on the floor; nulls clear them.</summary>
        public void ShowPaths(IReadOnlyList<Vec3>? estimate, IReadOnlyList<Vec3>? truth)
        {
            if (estimate == null && truth == null)
            {
                _pathsVisual.Content = null;
                return;
            }

            var g = new Model3DGroup();
            void Line(IReadOnlyList<Vec3>? pts, Color c)
            {
                if (pts == null || pts.Count < 2) return;
                var list = new List<Point3D>(pts.Count);
                foreach (var p in pts) list.Add(new Point3D(p.X, p.Y, p.Z));
                g.Children.Add(M.Model(Tube(list, 0.0015, 6), Glow(c, 0.9)));
            }

            Line(truth, Color.FromRgb(0x3F, 0xC1, 0x6A));
            Line(estimate, Color.FromRgb(0xF0, 0x8A, 0x3C));
            g.Freeze();
            if (!_view.Children.Contains(_pathsVisual)) _view.Children.Add(_pathsVisual);
            _pathsVisual.Content = g;
        }

        /// <summary>A flat colour that stays readable in shadow (diffuse + emissive).</summary>
        private static Material Glow(Color c, double strength)
        {
            var m = new MaterialGroup();
            m.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb((byte)(c.R / 3), (byte)(c.G / 3), (byte)(c.B / 3)))));
            m.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(255 * strength), c.R, c.G, c.B))));
            m.Freeze();
            return m;
        }

        private static void Octahedron(MeshGeometry3D m, Vec3 c, double r)
        {
            int b = m.Positions.Count;
            m.Positions.Add(new Point3D(c.X + r, c.Y, c.Z));
            m.Positions.Add(new Point3D(c.X - r, c.Y, c.Z));
            m.Positions.Add(new Point3D(c.X, c.Y + r, c.Z));
            m.Positions.Add(new Point3D(c.X, c.Y - r, c.Z));
            m.Positions.Add(new Point3D(c.X, c.Y, c.Z + r));
            m.Positions.Add(new Point3D(c.X, c.Y, c.Z - r));
            int[] tri = { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
            foreach (int i in tri) m.TriangleIndices.Add(b + i);
        }
    }
}
