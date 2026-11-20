using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    // [terrain] Move / rotate / scale gizmo of the selected object (screen-space picking, no hit tests).
    public sealed partial class WorldScene
    {
        private enum GizmoPart
        {
            None,
            X,
            Y,
            Z,
            Yaw
        }

        private GizmoPart _gizmoHot, _gizmoDrag;
        private Point _gizmoStart;
        private bool _gizmoMoved, _gizmoScale;
        private WorldPose? _gizmoPose;
        private Vec3 _gizmoSize;
        private double _gizmoAngle0;

        private static readonly Material GizmoX = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D)));
        private static readonly Material GizmoY = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x3C, 0xD0, 0x64)));
        private static readonly Material GizmoZ = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x46, 0x7C, 0xF0)));
        private static readonly Material GizmoRing = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xF2, 0xC9, 0x4C)));
        private static readonly Material GizmoHot = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));

        /// <summary>The move/rotate/scale gizmo is shown on the selection (Seç aracı, not simulating).</summary>
        public bool ShowGizmo { get; set; } = true;

        private WorldObject? GizmoTarget()
        {
            if (!ShowGizmo || _terrainEditing || Tool != WorldTool.Select || World == null || Selected == null) return null;
            if (Session != null && Session.State != Simulation.SimState.Stopped) return null;
            var o = World.Find(Selected);
            return o == null || WorldBuilding.IsLocked(o) ? null : o;
        }

        private double GizmoLength => Math.Max(0.02, _distance * 0.09);

        private (Point3D Origin, Vector3D X, Vector3D Y, Vector3D Z) GizmoFrame(WorldObject o)
        {
            var origin = new Point3D(o.Pose.X, o.Pose.Y, o.Pose.Z);
            double yaw = o.Pose.Yaw * Math.PI / 180;
            return (origin, new Vector3D(Math.Cos(yaw), Math.Sin(yaw), 0), new Vector3D(-Math.Sin(yaw), Math.Cos(yaw), 0), new Vector3D(0, 0, 1));
        }

        /// <summary>Draws the gizmo into the overlay group (hook from RebuildOverlay).</summary>
        private void AppendGizmo(Model3DGroup g)
        {
            var o = GizmoTarget();
            if (o == null) return;
            var (c, ax, ay, az) = GizmoFrame(o);
            double L = GizmoLength, r = L * 0.018;
            void Arrow(Vector3D dir, Material mat, GizmoPart part)
            {
                var m = _gizmoHot == part || _gizmoDrag == part ? GizmoHot : mat;
                var tip = c + dir * L;
                g.Children.Add(new GeometryModel3D(Tube(new List<Point3D> { c, tip }, r, 6), m));
                // Cone head (or a cube while scaling).
                if (_gizmoScale && _gizmoDrag == part) g.Children.Add(new GeometryModel3D(M.Box(tip.X, tip.Y, tip.Z, r * 7, r * 7, r * 7), m));
                else g.Children.Add(new GeometryModel3D(Cone(tip, dir, r * 3.5, L * 0.14), m));
            }

            Arrow(ax, GizmoX, GizmoPart.X);
            Arrow(ay, GizmoY, GizmoPart.Y);
            Arrow(az, GizmoZ, GizmoPart.Z);
            var ring = new List<Point3D>();
            for (int i = 0; i <= 64; i++)
            {
                double a = 2 * Math.PI * i / 64;
                ring.Add(c + (ax * Math.Cos(a) + ay * Math.Sin(a)) * L * 0.75);
            }

            g.Children.Add(new GeometryModel3D(Tube(ring, r * 0.8, 5), _gizmoHot == GizmoPart.Yaw || _gizmoDrag == GizmoPart.Yaw ? GizmoHot : GizmoRing));
        }

        private static MeshGeometry3D Cone(Point3D tipBase, Vector3D dir, double radius, double length)
        {
            dir.Normalize();
            var up = Math.Abs(dir.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(dir, up);
            u.Normalize();
            var v = Vector3D.CrossProduct(dir, u);
            var mesh = new MeshGeometry3D();
            var apex = tipBase + dir * length;
            const int n = 12;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                var p0 = tipBase + (u * Math.Cos(a0) + v * Math.Sin(a0)) * radius;
                var p1 = tipBase + (u * Math.Cos(a1) + v * Math.Sin(a1)) * radius;
                int k = mesh.Positions.Count;
                mesh.Positions.Add(p0);
                mesh.Positions.Add(p1);
                mesh.Positions.Add(apex);
                mesh.Positions.Add(tipBase);
                mesh.TriangleIndices.Add(k);
                mesh.TriangleIndices.Add(k + 1);
                mesh.TriangleIndices.Add(k + 2);
                mesh.TriangleIndices.Add(k + 3);
                mesh.TriangleIndices.Add(k + 1);
                mesh.TriangleIndices.Add(k);
            }

            return mesh;
        }

        private static double SegmentDistance(Point p, Point a, Point b)
        {
            var ab = b - a;
            double l2 = ab.LengthSquared;
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        private GizmoPart GizmoHit(Point p)
        {
            var o = GizmoTarget();
            if (o == null) return GizmoPart.None;
            var (c, ax, ay, az) = GizmoFrame(o);
            double L = GizmoLength;
            var pc = Project(c);
            if (!pc.Visible) return GizmoPart.None;
            var best = GizmoPart.None;
            double bestD = 8;
            foreach (var (dir, part) in new[] { (ax, GizmoPart.X), (ay, GizmoPart.Y), (az, GizmoPart.Z) })
            {
                var pt = Project(c + dir * L * 1.14);
                if (!pt.Visible) continue;
                double d = SegmentDistance(p, pc.Screen, pt.Screen);
                if (d < bestD && (p - pc.Screen).Length > 6)
                {
                    bestD = d;
                    best = part;
                }
            }

            Point? prev = null;
            for (int i = 0; i <= 48; i++)
            {
                double a = 2 * Math.PI * i / 48;
                var q = Project(c + (ax * Math.Cos(a) + ay * Math.Sin(a)) * L * 0.75);
                if (!q.Visible)
                {
                    prev = null;
                    continue;
                }

                if (prev is { } pp)
                {
                    double d = SegmentDistance(p, pp, q.Screen);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = GizmoPart.Yaw;
                    }
                }

                prev = q.Screen;
            }

            return best;
        }

        private bool GizmoMouseDown(MouseButtonEventArgs e, Point p)
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1) return false;
            var part = GizmoHit(p);
            var o = GizmoTarget();
            if (part == GizmoPart.None || o == null) return false;
            _gizmoDrag = part;
            _gizmoStart = p;
            _gizmoMoved = false;
            _gizmoScale = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && part != GizmoPart.Yaw && CanScale(o);
            _gizmoPose = o.Pose.Clone();
            _gizmoSize = SizeOf(o);
            var pc = Project(new Point3D(o.Pose.X, o.Pose.Y, o.Pose.Z)).Screen;
            _gizmoAngle0 = Math.Atan2(p.Y - pc.Y, p.X - pc.X);
            CaptureMouse();
            RebuildOverlay();
            return true;
        }

        private static bool CanScale(WorldObject o) => o.Kind == WorldObjectKind.Body || WorldBuilding.IsProp(o);

        private static Vec3 SizeOf(WorldObject o)
        {
            if (WorldBuilding.PropOf(o) is { } def) return TerrainProps.SizeOf(o.Parameters, def);
            return new Vec3(o.SizeX, o.SizeY, o.SizeZ);
        }

        private bool GizmoMouseMove(MouseEventArgs e, Point p)
        {
            if (_gizmoDrag == GizmoPart.None)
            {
                if (_drag != null || _orbit != null) return false;
                var hot = GizmoHit(p);
                if (hot != _gizmoHot)
                {
                    _gizmoHot = hot;
                    RebuildOverlay();
                }

                return false;
            }

            var o = World?.Find(Selected);
            if (o == null || _gizmoPose == null) return true;
            if (!_gizmoMoved)
            {
                if ((p - _gizmoStart).Length < 2) return true;
                BeginEdit?.Invoke();
                _gizmoMoved = true;
            }

            var start = new WorldObject { Pose = _gizmoPose };
            var (c, ax, ay, az) = GizmoFrame(start);
            if (_gizmoDrag == GizmoPart.Yaw)
            {
                var pc = Project(c).Screen;
                double a = Math.Atan2(p.Y - pc.Y, p.X - pc.X);
                double deg = -(a - _gizmoAngle0) * 180 / Math.PI;
                // Screen y points down: seen from above, dragging counter-clockwise turns +yaw.
                if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) deg = Math.Round(deg / 5) * 5;
                double target = _gizmoPose.Yaw + deg;
                World!.RotateAssembly(o, target - o.Pose.Yaw);
                RefreshPlaced(o);
                return true;
            }

            var dir = _gizmoDrag == GizmoPart.X ? ax : _gizmoDrag == GizmoPart.Y ? ay : az;
            double L = GizmoLength;
            var p0 = Project(c).Screen;
            var p1 = Project(c + dir * L).Screen;
            var axis = p1 - p0;
            if (axis.LengthSquared < 4) return true;
            double t = Vector.Multiply(p - _gizmoStart, axis) / axis.LengthSquared * L;
            if (_gizmoScale)
            {
                int k = _gizmoDrag == GizmoPart.X ? 0 : _gizmoDrag == GizmoPart.Y ? 1 : 2;
                double s0 = k == 0 ? _gizmoSize.X : k == 1 ? _gizmoSize.Y : _gizmoSize.Z;
                double s = Math.Max(0.002, Snap(s0 + t));
                var size = new Vec3(k == 0 ? s : _gizmoSize.X, k == 1 ? s : _gizmoSize.Y, k == 2 ? s : _gizmoSize.Z);
                if (WorldBuilding.IsProp(o)) WorldBuilding.ResizeProp(o, size);
                else
                {
                    o.SizeX = size.X;
                    o.SizeY = size.Y;
                    o.SizeZ = size.Z;
                }

                Rebuild();
                return true;
            }

            double nx = Snap(_gizmoPose.X + dir.X * t), ny = Snap(_gizmoPose.Y + dir.Y * t), nz = _gizmoPose.Z + dir.Z * t;
            if (_gizmoDrag == GizmoPart.Z) nz = Snap(nz);
            else if (SurfaceSnap && World!.Terrain != null && Math.Abs(_gizmoPose.Z - GroundZAt(_gizmoPose.X, _gizmoPose.Y)) < 1e-3) nz = GroundZAt(nx, ny);
            MoveGroupMates(o, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z);
            World!.MoveAssembly(o, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z);
            RefreshPlaced(o);
            return true;
        }

        private void RefreshPlaced(WorldObject o)
        {
            foreach (var moved in new[] { o }.Concat(World!.MountedOn(o.Id)))
                if (_items.TryGetValue(moved.Id, out var mi))
                    mi.Placed = PoseMatrix(moved.Pose);
            UpdateKinematics();
            RebuildCables();
            RebuildOverlay();
        }

        private bool GizmoMouseUp()
        {
            if (_gizmoDrag == GizmoPart.None) return false;
            var part = _gizmoDrag;
            bool scale = _gizmoScale;
            _gizmoDrag = GizmoPart.None;
            _gizmoScale = false;
            ReleaseMouseCapture();
            if (_gizmoMoved && World?.Find(Selected) is { } o)
            {
                string what = part == GizmoPart.Yaw ? $"{o.Name} döndürüldü ({o.Pose.Yaw:0}°)"
                    : scale ? $"{o.Name} ölçeklendi"
                    : $"{o.Name} taşındı ({o.Pose.X * 1000:F0}, {o.Pose.Y * 1000:F0}, {o.Pose.Z * 1000:F0} mm)";
                EditCommitted?.Invoke(what);
            }

            RebuildOverlay();
            return true;
        }
    }
}
