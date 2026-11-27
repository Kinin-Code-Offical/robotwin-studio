using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>[gizmo] What the selection's handles do (Unity's W / E / R / Y; Q leaves only selecting).</summary>
    public enum GizmoMode
    {
        None,
        Move,
        Rotate,
        Scale,
        All,

        /// <summary>[rect] Unity's Rect Tool (T): resize by edges and corners on the face turned to the camera (World/WorldScene.Rect.cs).</summary>
        Rect
    }

    /// <summary>
    /// [gizmo] The selection's transform handles, as Unity's (studied in place): Move (W) with an arrow per axis, a
    /// square per plane turned to the camera and the middle for the screen plane; Rotate (E) with a ring per axis (only
    /// the half facing the camera), an outer ring about the view and the inside for a free turn, the turned angle shown
    /// as a filled slice while dragging; Scale (R) with a cube per axis and the middle for all three; All (Y) together.
    /// The handles sit on the pivot or the middle of the part, along its own axes or the world's. The grid and 15° steps
    /// apply unless Alt is held. Everything mounted on the part comes with it, joints included.
    /// </summary>
    public sealed partial class WorldScene
    {
        private enum GizmoPart
        {
            None,
            X,
            Y,
            Z,
            PlaneYZ,
            PlaneXZ,
            PlaneXY,
            Screen,
            RotX,
            RotY,
            RotZ,
            RotView,
            RotFree,
            ScaleX,
            ScaleY,
            ScaleZ,
            ScaleAll,
            RectEdge,   // [rect]
            RectCorner
        }

        private GizmoPart _gizmoHot, _gizmoDrag;
        private Point _gizmoStart;
        private bool _gizmoMoved;
        private WorldPose? _gizmoPose;
        private Vec3 _gizmoSize;
        private Point3D _gizmoPivot;
        private Vector3D _gizmoAxis;
        private Point3D? _gizmoHit0;
        private double _gizmoAngle;
        private Vector3D _gizmoFrom;
        private List<(WorldObject Part, WorldPose Pose)>? _gizmoParts;
        private List<(WorldJoint Joint, double Ax, double Ay, double Az, double Dx, double Dy, double Dz)>? _gizmoJoints;
        private string? _gizmoReadout;

        private static readonly Color AxisX = Color.FromRgb(0xE5, 0x48, 0x4D);
        private static readonly Color AxisY = Color.FromRgb(0x3C, 0xD0, 0x64);
        private static readonly Color AxisZ = Color.FromRgb(0x46, 0x7C, 0xF0);
        private static readonly Color Active = Color.FromRgb(0xF2, 0xD0, 0x4C);   // Unity's: the handle under the cursor and the one dragged
        private static readonly Color ViewRing = Color.FromRgb(0xD8, 0xD8, 0xDE);

        /// <summary>The handles shown on the selection (W, E, R, Y; None = select only, Q).</summary>
        public GizmoMode Gizmo { get; set; } = GizmoMode.Move;

        /// <summary>The handles turn with the part (its own axes) rather than lying along the world's.</summary>
        public bool GizmoLocal { get; set; } = true;

        /// <summary>The handles sit in the middle of the part's shape rather than on its pivot.</summary>
        public bool GizmoCenter { get; set; }

        /// <summary>Rotation step (degrees) when not holding Alt.</summary>
        public double RotateStep { get; set; } = 15;

        /// <summary>The move/rotate/scale gizmo is shown on the selection (Seç aracı, not simulating).</summary>
        public bool ShowGizmo { get; set; } = true;

        /// <summary>[render] A replay: nothing can be moved, wired or edited; the camera still orbits and parts can be selected.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Raised when a handle choice changes from the keyboard (the page's buttons follow).</summary>
        public event Action? GizmoChanged;

        public void SetGizmo(GizmoMode mode)
        {
            Gizmo = mode;
            _gizmoHot = GizmoPart.None;
            RebuildOverlay();
            GizmoChanged?.Invoke();
        }

        private WorldObject? GizmoTarget()
        {
            if (!ShowGizmo || ReadOnly || Gizmo == GizmoMode.None || _terrainEditing || Tool != WorldTool.Select || World == null || Selected == null) return null;
            if (Session != null && Session.State != Simulation.SimState.Stopped) return null;
            var o = World.Find(Selected);
            return o == null || WorldBuilding.IsLocked(o) ? null : o;
        }

        private double GizmoLength => Math.Max(0.02, _distance * 0.09);

        private static Vector3D V(Vec3 v) => new Vector3D(v.X, v.Y, v.Z);

        private static Vec3 W3(Vector3D v) => new Vec3(v.X, v.Y, v.Z);

        /// <summary>Where the handles stand and which way their axes point.</summary>
        private (Point3D Origin, Vector3D X, Vector3D Y, Vector3D Z) GizmoFrame(WorldObject o, bool forScale = false)
        {
            var origin = new Point3D(o.Pose.X, o.Pose.Y, o.Pose.Z);
            if (GizmoCenter && _items.TryGetValue(o.Id, out var it) && !it.Visual.Content.Bounds.IsEmpty)
            {
                var b = new MatrixTransform3D(ExplodedMatrix(o.Id, it.Current)).TransformBounds(it.Visual.Content.Bounds);
                origin = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
            }

            if (VertexPivotFor(o) is { } corner) origin = corner;   // [vertex-pivot] V held: on the corner nearest the cursor

            if (!GizmoLocal && !forScale) return (origin, new Vector3D(1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, 0, 1));
            var q = o.Pose.Orientation;   // scale always runs along the part's own axes (as Unity's)
            return (origin, V(q.Rotate(Vec3.UnitX)), V(q.Rotate(Vec3.UnitY)), V(q.Rotate(Vec3.UnitZ)));
        }

        private Vector3D ViewDir()
        {
            var d = _camera.LookDirection;
            d.Normalize();
            return d;
        }

        private bool ShowsMove => Gizmo is GizmoMode.Move or GizmoMode.All;
        private bool ShowsRotate => Gizmo is GizmoMode.Rotate or GizmoMode.All;
        private bool ShowsScale => Gizmo is GizmoMode.Scale or GizmoMode.All;

        // ================================================================ drawing (a 2D layer over the scene: always on top, as Unity's)

        /// <summary>[gizmo] The layer the handles are drawn on, over the 3D view; it does not take the mouse.</summary>
        private sealed class GizmoLayer : FrameworkElement
        {
            private readonly WorldScene _scene;

            public GizmoLayer(WorldScene scene)
            {
                _scene = scene;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext dc) => _scene.DrawGizmo(dc);
        }

        private GizmoLayer? _gizmoLayer;

        /// <summary>Adds the handles' layer over the view (once, from the constructor).</summary>
        private void AddGizmoLayer()
        {
            _gizmoLayer = new GizmoLayer(this);
            Children.Insert(Children.IndexOf(_view) + 1, _gizmoLayer);
        }

        /// <summary>The handles are drawn again (the selection, the camera or a drag changed).</summary>
        private void AppendGizmo(Model3DGroup g) => _gizmoLayer?.InvalidateVisual();

        private static Pen Line(Color c, double w, byte alpha = 0xFF)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        private static Brush Fill(Color c, byte alpha = 0xFF)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        private void DrawPolyline(DrawingContext dc, IEnumerable<Point3D> pts, Pen pen)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                bool open = false;
                foreach (var q in pts)
                {
                    var s = Project(q);
                    if (!s.Visible)
                    {
                        open = false;
                        continue;
                    }

                    if (!open)
                    {
                        ctx.BeginFigure(s.Screen, false, false);
                        open = true;
                    }
                    else
                    {
                        ctx.LineTo(s.Screen, true, true);
                    }
                }
            }

            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        private void DrawPolygon(DrawingContext dc, IReadOnlyList<Point3D> pts, Brush? fill, Pen? pen)
        {
            var s = pts.Select(Project).ToList();
            if (s.Any(x => !x.Visible)) return;
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(s[0].Screen, fill != null, true);
                for (int i = 1; i < s.Count; i++) ctx.LineTo(s[i].Screen, true, true);
            }

            geo.Freeze();
            dc.DrawGeometry(fill, pen, geo);
        }

        /// <summary>Draws the handles: arrows, plane squares, rings, scale cubes and the middle, in screen pixels.</summary>
        private void DrawGizmo(DrawingContext dc)
        {
            var o = GizmoTarget();
            if (o == null) return;
            if (ShowsRect)
            {
                DrawRect(dc, o);   // [rect] World/WorldScene.Rect.cs
                return;
            }

            var (c, ax, ay, az) = GizmoFrame(o);
            var pc = Project(c);
            if (!pc.Visible) return;
            double L = GizmoLength;
            bool dragging = _gizmoDrag != GizmoPart.None;
            bool On(GizmoPart part) => _gizmoHot == part || _gizmoDrag == part;
            bool Dim(GizmoPart part) => dragging && _gizmoDrag != part;
            Color Col(GizmoPart part, Color c0) => On(part) ? Active : c0;

            if (ShowsMove)
            {
                // plane squares first, under the arrows; on the side facing the camera, coloured by their normal's axis
                var eye = _camera.Position;
                foreach (var (a, b, part, col) in new[] { (ay, az, GizmoPart.PlaneYZ, AxisX), (ax, az, GizmoPart.PlaneXZ, AxisY), (ax, ay, GizmoPart.PlaneXY, AxisZ) })
                {
                    if (Dim(part)) continue;
                    var (p0, p1, p2, p3) = PlaneSquare(c, a, b, L, eye);
                    DrawPolygon(dc, new[] { p0, p1, p2, p3 }, Fill(Col(part, col), On(part) ? (byte)0x90 : (byte)0x48), Line(Col(part, col), 1));
                }

                foreach (var (dir, part, col) in new[] { (ax, GizmoPart.X, AxisX), (ay, GizmoPart.Y, AxisY), (az, GizmoPart.Z, AxisZ) })
                {
                    if (Dim(part) && _gizmoDrag is not (GizmoPart.PlaneXY or GizmoPart.PlaneXZ or GizmoPart.PlaneYZ or GizmoPart.Screen)) continue;
                    var tip = Project(c + dir * L);
                    if (!tip.Visible) continue;
                    var colour = Col(part, col);
                    dc.DrawLine(Line(colour, On(part) ? 3 : 2), pc.Screen, tip.Screen);
                    // the head: a filled triangle along the projected axis
                    var d = tip.Screen - pc.Screen;
                    if (d.Length < 1) continue;
                    d.Normalize();
                    var n = new Vector(-d.Y, d.X);
                    var head = new StreamGeometry();
                    using (var ctx = head.Open())
                    {
                        ctx.BeginFigure(tip.Screen + d * 13, true, true);
                        ctx.LineTo(tip.Screen + n * 5, true, true);
                        ctx.LineTo(tip.Screen - n * 5, true, true);
                    }

                    head.Freeze();
                    dc.DrawGeometry(Fill(colour), null, head);
                }
            }

            if (ShowsRotate)
            {
                double R = L * (Gizmo == GizmoMode.All ? 1.0 : 0.85);
                var view = ViewDir();
                var (vu, vv) = Perp(view);
                if (!Dim(GizmoPart.RotView))
                    DrawPolyline(dc, Enumerable.Range(0, 97).Select(i => c + (vu * Math.Cos(2 * Math.PI * i / 96) + vv * Math.Sin(2 * Math.PI * i / 96)) * R * 1.18),
                        Line(Col(GizmoPart.RotView, ViewRing), On(GizmoPart.RotView) ? 2.5 : 1.5));
                // the free-turn ball's outline, faint (Unity's grey sphere)
                if (Gizmo == GizmoMode.Rotate && !dragging)
                    DrawPolyline(dc, Enumerable.Range(0, 97).Select(i => c + (vu * Math.Cos(2 * Math.PI * i / 96) + vv * Math.Sin(2 * Math.PI * i / 96)) * R),
                        Line(ViewRing, 1, _gizmoHot == GizmoPart.RotFree ? (byte)0xA0 : (byte)0x40));

                // dragging a ring: the angle turned so far, a filled slice from where it started
                if (_gizmoDrag is GizmoPart.RotX or GizmoPart.RotY or GizmoPart.RotZ or GizmoPart.RotView && Math.Abs(_gizmoAngle) > 1e-6)
                {
                    var n = _gizmoAxis;
                    var from = _gizmoFrom - n * Vector3D.DotProduct(_gizmoFrom, n);
                    if (from.Length > 1e-9)
                    {
                        from.Normalize();
                        var side = Vector3D.CrossProduct(n, from);
                        int steps = Math.Max(2, (int)(Math.Abs(_gizmoAngle) / (Math.PI / 48)));
                        var fan = new List<Point3D> { _gizmoPivot };
                        double rr = _gizmoDrag == GizmoPart.RotView ? R * 1.18 : R;
                        for (int i = 0; i <= steps; i++)
                        {
                            double a = _gizmoAngle * i / steps;
                            fan.Add(_gizmoPivot + (from * Math.Cos(a) + side * Math.Sin(a)) * rr);
                        }

                        DrawPolygon(dc, fan, Fill(Active, 0x50), Line(Active, 1));
                    }
                }

                foreach (var (u, v, part, col) in new[] { (ay, az, GizmoPart.RotX, AxisX), (az, ax, GizmoPart.RotY, AxisY), (ax, ay, GizmoPart.RotZ, AxisZ) })
                {
                    if (Dim(part)) continue;
                    foreach (var run in RingRuns(c, u, v, R, view, _gizmoDrag == part))
                        DrawPolyline(dc, run, Line(Col(part, col), On(part) ? 3 : 2));
                }
            }

            if (ShowsScale)
            {
                var (_, sx, sy, sz) = GizmoFrame(o, forScale: true);
                double reach = Gizmo == GizmoMode.All ? L * 1.32 : L;
                foreach (var (dir, part, col) in new[] { (sx, GizmoPart.ScaleX, AxisX), (sy, GizmoPart.ScaleY, AxisY), (sz, GizmoPart.ScaleZ, AxisZ) })
                {
                    if (Dim(part) && _gizmoDrag != GizmoPart.ScaleAll) continue;
                    var tip = Project(c + dir * reach);
                    if (!tip.Visible) continue;
                    var colour = Col(part, col);
                    if (Gizmo == GizmoMode.Scale) dc.DrawLine(Line(colour, On(part) ? 3 : 2), pc.Screen, tip.Screen);
                    dc.DrawRectangle(Fill(colour), null, new Rect(tip.Screen.X - 4.5, tip.Screen.Y - 4.5, 9, 9));
                }
            }

            // the middle: the screen plane (move) or all three axes (scale)
            if (ShowsMove || ShowsScale)
            {
                var part = ShowsScale ? GizmoPart.ScaleAll : GizmoPart.Screen;
                if (ShowsScale) dc.DrawRectangle(Fill(Col(part, ViewRing)), null, new Rect(pc.Screen.X - 5, pc.Screen.Y - 5, 10, 10));
                else dc.DrawRectangle(Fill(Col(part, ViewRing), On(part) ? (byte)0x60 : (byte)0x00), Line(Col(part, ViewRing), 1.2), new Rect(pc.Screen.X - 6, pc.Screen.Y - 6, 12, 12));
            }
        }

        /// <summary>A plane handle's corners: a small square in the quadrant of the two axes nearer the camera.</summary>
        private static (Point3D, Point3D, Point3D, Point3D) PlaneSquare(Point3D c, Vector3D a, Vector3D b, double L, Point3D eye)
        {
            var toEye = eye - c;
            double sa = Vector3D.DotProduct(toEye, a) >= 0 ? 1 : -1, sb = Vector3D.DotProduct(toEye, b) >= 0 ? 1 : -1;
            double lo = L * 0.12, hi = L * 0.32;
            Point3D P(double u, double v) => c + a * (u * sa) + b * (v * sb);
            return (P(lo, lo), P(hi, lo), P(hi, hi), P(lo, hi));
        }

        private static (Vector3D U, Vector3D V) Perp(Vector3D n)
        {
            var up = Math.Abs(n.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var u = Vector3D.CrossProduct(n, up);
            u.Normalize();
            var v = Vector3D.CrossProduct(n, u);
            v.Normalize();
            return (u, v);
        }

        /// <summary>A ring about an axis split into the runs that face the camera (or the whole ring).</summary>
        private static IEnumerable<List<Point3D>> RingRuns(Point3D c, Vector3D u, Vector3D v, double R, Vector3D view, bool whole)
        {
            const int n = 72;
            var run = new List<Point3D>();
            for (int i = 0; i <= n; i++)
            {
                double a = 2 * Math.PI * i / n;
                var d = u * Math.Cos(a) + v * Math.Sin(a);
                bool front = whole || Vector3D.DotProduct(d, view) <= 0.08;
                if (front) run.Add(c + d * R);
                else if (run.Count > 0)
                {
                    if (run.Count > 1) yield return run;
                    run = new List<Point3D>();
                }
            }

            if (run.Count > 1) yield return run;
        }

        // ================================================================ picking (screen space)

        private static double SegmentDistance(Point p, Point a, Point b)
        {
            var ab = b - a;
            double l2 = ab.LengthSquared;
            double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, Vector.Multiply(p - a, ab) / l2));
            return (p - (a + ab * t)).Length;
        }

        private double PolylineDistance(Point p, IEnumerable<Point3D> pts)
        {
            double best = double.MaxValue;
            Point? prev = null;
            foreach (var q in pts)
            {
                var s = Project(q);
                if (!s.Visible)
                {
                    prev = null;
                    continue;
                }

                if (prev is { } pp) best = Math.Min(best, SegmentDistance(p, pp, s.Screen));
                prev = s.Screen;
            }

            return best;
        }

        private static bool InQuad(Point p, Point a, Point b, Point c, Point d)
        {
            double Side(Point u, Point v) => (v.X - u.X) * (p.Y - u.Y) - (v.Y - u.Y) * (p.X - u.X);
            double s1 = Side(a, b), s2 = Side(b, c), s3 = Side(c, d), s4 = Side(d, a);
            return (s1 >= 0 && s2 >= 0 && s3 >= 0 && s4 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0 && s4 <= 0);
        }

        private GizmoPart GizmoHit(Point p)
        {
            var o = GizmoTarget();
            if (o == null) return GizmoPart.None;
            if (ShowsRect) return RectHit(o, p);   // [rect]
            var (c, ax, ay, az) = GizmoFrame(o);
            double L = GizmoLength;
            var pc = Project(c);
            if (!pc.Visible) return GizmoPart.None;
            var best = GizmoPart.None;
            double bestD = 8;
            void Try(double d, GizmoPart part)
            {
                if (d >= bestD) return;
                bestD = d;
                best = part;
            }

            // the middle first: the screen plane or uniform scale
            if ((ShowsMove || ShowsScale) && (p - pc.Screen).Length < 9) return ShowsScale ? GizmoPart.ScaleAll : GizmoPart.Screen;

            if (ShowsScale)
            {
                var (_, sx, sy, sz) = GizmoFrame(o, forScale: true);
                double reach = Gizmo == GizmoMode.All ? L * 1.32 : L;
                foreach (var (dir, part) in new[] { (sx, GizmoPart.ScaleX), (sy, GizmoPart.ScaleY), (sz, GizmoPart.ScaleZ) })
                {
                    var t = Project(c + dir * reach);
                    if (t.Visible) Try((p - t.Screen).Length - 2, part);
                    if (Gizmo == GizmoMode.Scale && t.Visible) Try(SegmentDistance(p, pc.Screen, t.Screen) + 1, part);
                }
            }

            if (ShowsMove)
            {
                foreach (var (dir, part) in new[] { (ax, GizmoPart.X), (ay, GizmoPart.Y), (az, GizmoPart.Z) })
                {
                    var t = Project(c + dir * L * 1.16);
                    if (t.Visible && (p - pc.Screen).Length > 6) Try(SegmentDistance(p, pc.Screen, t.Screen), part);
                }

                var eye = _camera.Position;
                foreach (var (a, b, part) in new[] { (ay, az, GizmoPart.PlaneYZ), (ax, az, GizmoPart.PlaneXZ), (ax, ay, GizmoPart.PlaneXY) })
                {
                    var (q0, q1, q2, q3) = PlaneSquare(c, a, b, L, eye);
                    var s = new[] { q0, q1, q2, q3 }.Select(Project).ToList();
                    if (s.All(x => x.Visible) && InQuad(p, s[0].Screen, s[1].Screen, s[2].Screen, s[3].Screen)) Try(2, part);
                }
            }

            if (ShowsRotate)
            {
                double R = L * (Gizmo == GizmoMode.All ? 1.0 : 0.85);
                var view = ViewDir();
                foreach (var (u, v, part) in new[] { (ay, az, GizmoPart.RotX), (az, ax, GizmoPart.RotY), (ax, ay, GizmoPart.RotZ) })
                    foreach (var run in RingRuns(c, u, v, R, view, false))
                        Try(PolylineDistance(p, run), part);
                var (vu, vv) = Perp(view);
                Try(PolylineDistance(p, Enumerable.Range(0, 73).Select(i => c + (vu * Math.Cos(2 * Math.PI * i / 72) + vv * Math.Sin(2 * Math.PI * i / 72)) * R * 1.18)), GizmoPart.RotView);
                if (best == GizmoPart.None && Gizmo == GizmoMode.Rotate)
                {
                    // inside the rings: a free turn (Unity's trackball)
                    var edge = Project(c + vu * R);
                    if (edge.Visible && (p - pc.Screen).Length < (edge.Screen - pc.Screen).Length) return GizmoPart.RotFree;
                }
            }

            return best;
        }

        // ================================================================ dragging

        private bool GizmoMouseDown(MouseButtonEventArgs e, Point p)
        {
            if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1) return false;
            var part = GizmoHit(p);
            var o = GizmoTarget();
            if (part == GizmoPart.None || o == null) return false;
            if (part is GizmoPart.ScaleX or GizmoPart.ScaleY or GizmoPart.ScaleZ or GizmoPart.ScaleAll or GizmoPart.RectEdge or GizmoPart.RectCorner && !CanScale(o))
            {
                _hint.Text = $"{o.Name} ölçeklenemez: bir hazır parçanın boyu modelinden gelir (gövdeler ve arazi nesneleri ölçeklenir)";
                return true;
            }

            _gizmoDrag = part;
            _gizmoStart = p;
            _gizmoMoved = false;
            _gizmoPose = o.Pose.Clone();
            _gizmoSize = SizeOf(o);
            var (c, ax, ay, az) = GizmoFrame(o);
            _gizmoPivot = c;
            _gizmoAngle = 0;
            _gizmoAxis = part switch
            {
                GizmoPart.RotX => ax,
                GizmoPart.RotY => ay,
                GizmoPart.RotZ => az,
                GizmoPart.PlaneYZ => ax,
                GizmoPart.PlaneXZ => ay,
                GizmoPart.PlaneXY => az,
                _ => -ViewDir()
            };
            _gizmoAxis.Normalize();
            _gizmoHit0 = PlaneHit(p, c, _gizmoAxis);
            _gizmoFrom = (_gizmoHit0 ?? c + ax) - c;
            // what a turn must put back each step: the parts' poses and their joints' anchors and axes
            _gizmoOthers = OtherSelectedRoots(o);   // [multiselect] the rest of the selection moves and turns with it
            _gizmoParts = new[] { o }.Concat(World!.MountedOn(o.Id)).Concat(_gizmoOthers.SelectMany(x => new[] { x }.Concat(World.MountedOn(x.Id))))
                .Distinct().Select(x => (x, x.Pose.Clone())).ToList();
            var ids = new HashSet<string>(_gizmoParts.Select(x => x.Part.Id), StringComparer.OrdinalIgnoreCase);
            _gizmoJoints = World.Joints.Where(j => ids.Contains(j.ObjectA)).Select(j => (j, j.AnchorX, j.AnchorY, j.AnchorZ, j.AxisX, j.AxisY, j.AxisZ)).ToList();
            if (ShowsRect) RectBegin(o, p);   // [rect] the face's plane and middle for moves and turns
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

        /// <summary>Where the cursor's ray meets a plane through a point (null when it runs along it).</summary>
        private Point3D? PlaneHit(Point screen, Point3D at, Vector3D normal)
        {
            var (o, d) = Ray(screen);
            double den = Vector3D.DotProduct(d, normal);
            if (Math.Abs(den) < 1e-6) return null;
            double t = Vector3D.DotProduct(at - o, normal) / den;
            return t < 0 ? null : o + d * t;
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

            bool free = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || !SnapOn;   // [grid] snapping off: every drag is free
            var start = new WorldObject { Pose = _gizmoPose };
            var (c0, ax, ay, az) = GizmoFrame(start);
            c0 = _gizmoPivot;
            double L = GizmoLength;

            switch (_gizmoDrag)
            {
                case GizmoPart.RotX or GizmoPart.RotY or GizmoPart.RotZ or GizmoPart.RotView:
                {
                    // the angle the cursor has turned about the pivot, seen along the axis
                    var hit = PlaneHit(p, c0, _gizmoAxis);
                    double angle;
                    if (hit is { } h && _gizmoHit0 != null)
                    {
                        var a = _gizmoFrom;
                        var b = h - c0;
                        angle = Math.Atan2(Vector3D.DotProduct(Vector3D.CrossProduct(a, b), _gizmoAxis), Vector3D.DotProduct(a, b));
                    }
                    else
                    {
                        var pc = Project(c0).Screen;
                        angle = -(Math.Atan2(p.Y - pc.Y, p.X - pc.X) - Math.Atan2(_gizmoStart.Y - pc.Y, _gizmoStart.X - pc.X));
                    }

                    double deg = angle * 180 / Math.PI;
                    if (!free && RotateStep > 0) deg = Math.Round(deg / RotateStep) * RotateStep;
                    _gizmoAngle = deg * Math.PI / 180;
                    TurnAssembly(o, Quat.FromAxisAngle(W3(_gizmoAxis), _gizmoAngle));
                    string axis = _gizmoDrag switch { GizmoPart.RotX => "X", GizmoPart.RotY => "Y", GizmoPart.RotZ => "Z", _ => "görüş" };
                    Readout($"{o.Name}: {axis} ekseni çevresinde {deg:0.#}°{(free ? string.Empty : $" ({RotateStep:0.#}° adım; Alt: serbest)")}");
                    return true;
                }

                case GizmoPart.RotFree:
                {
                    // the trackball: the drag across the screen turns about the screen axis across it
                    var view = ViewDir();
                    var right = Vector3D.CrossProduct(view, new Vector3D(0, 0, 1));
                    if (right.Length < 1e-6) right = new Vector3D(1, 0, 0);
                    right.Normalize();
                    var up = Vector3D.CrossProduct(right, view);
                    var d = p - _gizmoStart;
                    var axisV = up * d.X + right * d.Y;
                    if (axisV.Length < 1e-9) return true;
                    axisV.Normalize();
                    double ang = d.Length * 0.01;
                    _gizmoAxis = axisV;
                    TurnAssembly(o, Quat.FromAxisAngle(W3(axisV), ang));
                    Readout($"{o.Name}: serbest döndürme {ang * 180 / Math.PI:0.#}°");
                    return true;
                }

                case GizmoPart.RectEdge or GizmoPart.RectCorner:
                    return RectResize(o, p, free);   // [rect]

                case GizmoPart.ScaleX or GizmoPart.ScaleY or GizmoPart.ScaleZ or GizmoPart.ScaleAll:
                {
                    var (_, sx, sy, sz) = GizmoFrame(start, forScale: true);
                    Vec3 size;
                    if (_gizmoDrag == GizmoPart.ScaleAll)
                    {
                        double k = Math.Max(0.05, 1 + (p.X - _gizmoStart.X - (p.Y - _gizmoStart.Y)) * 0.01);
                        size = new Vec3(SnapSize(_gizmoSize.X * k, free), SnapSize(_gizmoSize.Y * k, free), SnapSize(_gizmoSize.Z * k, free));
                    }
                    else
                    {
                        var dir = _gizmoDrag == GizmoPart.ScaleX ? sx : _gizmoDrag == GizmoPart.ScaleY ? sy : sz;
                        double t = AlongScreen(p, c0, dir, L);
                        int k = _gizmoDrag == GizmoPart.ScaleX ? 0 : _gizmoDrag == GizmoPart.ScaleY ? 1 : 2;
                        double s0 = k == 0 ? _gizmoSize.X : k == 1 ? _gizmoSize.Y : _gizmoSize.Z;
                        double s = SnapSize(s0 + t, free);
                        size = new Vec3(k == 0 ? s : _gizmoSize.X, k == 1 ? s : _gizmoSize.Y, k == 2 ? s : _gizmoSize.Z);
                    }

                    if (WorldBuilding.IsProp(o)) WorldBuilding.ResizeProp(o, size);
                    else
                    {
                        o.SizeX = size.X;
                        o.SizeY = size.Y;
                        o.SizeZ = size.Z;
                    }

                    Readout($"{o.Name}: {size.X * 1000:0.#} × {size.Y * 1000:0.#} × {size.Z * 1000:0.#} mm");
                    Rebuild();
                    return true;
                }
            }

            // moving: along an axis, in a plane, or in the screen plane
            Vector3D delta;
            if (_gizmoDrag is GizmoPart.X or GizmoPart.Y or GizmoPart.Z)
            {
                var dir = _gizmoDrag == GizmoPart.X ? ax : _gizmoDrag == GizmoPart.Y ? ay : az;
                delta = dir * AlongScreen(p, c0, dir, L);
            }
            else
            {
                var hit = PlaneHit(p, c0, _gizmoAxis);
                if (hit is not { } h || _gizmoHit0 is not { } h0) return true;
                delta = h - h0;
            }

            double nx = _gizmoPose.X + delta.X, ny = _gizmoPose.Y + delta.Y, nz = _gizmoPose.Z + delta.Z;
            if (!free && GridTurned && _gizmoDrag is GizmoPart.X or GizmoPart.Y or GizmoPart.Z)
            {
                // [grid] a turned grid: an arrow moves its part in whole steps along itself
                var dir = _gizmoDrag == GizmoPart.X ? ax : _gizmoDrag == GizmoPart.Y ? ay : az;
                double horizontal = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                double along = SnapAlong(Vector3D.DotProduct(delta, dir), horizontal > 0.5 ? StepX : StepZ);
                nx = _gizmoPose.X + dir.X * along;
                ny = _gizmoPose.Y + dir.Y * along;
                nz = _gizmoPose.Z + dir.Z * along;
            }
            else if (!free && GridTurned && Math.Abs(delta.Z) < 1e-12)
            {
                (nx, ny) = SnapXY(nx, ny);   // [grid] a turned grid: onto its own points
            }
            else if (!free)
            {
                // the grid along the world's axes the move uses
                if (Math.Abs(delta.X) > 1e-12 || _gizmoDrag != GizmoPart.Z) nx = SnapX(nx);   // [grid] each axis its own step
                if (Math.Abs(delta.Y) > 1e-12 || _gizmoDrag != GizmoPart.Z) ny = SnapY(ny);
                if (Math.Abs(delta.Z) > 1e-12) nz = SnapZ(nz);
            }

            if (_gizmoDrag is GizmoPart.X or GizmoPart.Y && SurfaceSnap && World!.Terrain != null && Math.Abs(_gizmoPose.Z - GroundZAt(_gizmoPose.X, _gizmoPose.Y)) < 1e-3) nz = GroundZAt(nx, ny);
            if (_gizmoDrag == GizmoPart.Screen && VertexSnap(o, p, ref nx, ref ny, ref nz))   // [vertex-snap] Unity's V
            {
                // placed by its corner: no grid, no surface
            }
            else if (_gizmoDrag == GizmoPart.Screen && SurfaceSnapHeld && SurfaceUnder(p, o) is { } onto)   // [surface-snap] Unity's Ctrl+Shift
            {
                nx = onto.X;
                ny = onto.Y;
                nz = onto.Z;
            }
            MoveGroupMates(o, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z);
            foreach (var other in _gizmoOthers) World!.MoveAssembly(other, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z);   // [multiselect]
            World!.MoveAssembly(o, nx - o.Pose.X, ny - o.Pose.Y, nz - o.Pose.Z);
            Readout($"{o.Name}: ({nx * 1000:0.#}, {ny * 1000:0.#}, {nz * 1000:0.#}) mm · Δ {(nx - _gizmoPose.X) * 1000:0.#}, {(ny - _gizmoPose.Y) * 1000:0.#}, {(nz - _gizmoPose.Z) * 1000:0.#}");
            RefreshPlaced(o);
            return true;
        }

        /// <summary>How far along an axis the cursor has gone since the drag began (metres), measured on the screen.</summary>
        private double AlongScreen(Point p, Point3D c, Vector3D dir, double L)
        {
            var p0 = Project(c).Screen;
            var p1 = Project(c + dir * L).Screen;
            var axis = p1 - p0;
            return axis.LengthSquared < 4 ? 0 : Vector.Multiply(p - _gizmoStart, axis) / axis.LengthSquared * L;
        }

        private double SnapSize(double v, bool free)
        {
            double step = SizeStep > 0 ? SizeStep : Grid;   // [grid] its own size step, else the move step
            if (free || step <= 0) return Math.Max(0.001, v);
            double s = Math.Round(v / step) * step;
            return Math.Max(0.001, s > 0 ? s : v);
        }

        /// <summary>Puts the assembly back as it was at the drag's start and turns it by the whole rotation so far.</summary>
        private void TurnAssembly(WorldObject o, Quat q)
        {
            if (_gizmoParts == null || _gizmoJoints == null) return;
            foreach (var (part, pose) in _gizmoParts)
            {
                part.Pose.X = pose.X;
                part.Pose.Y = pose.Y;
                part.Pose.Z = pose.Z;
                part.Pose.Roll = pose.Roll;
                part.Pose.Pitch = pose.Pitch;
                part.Pose.Yaw = pose.Yaw;
            }

            foreach (var (j, ax, ay, az, dx, dy, dz) in _gizmoJoints)
            {
                j.AnchorX = ax;
                j.AnchorY = ay;
                j.AnchorZ = az;
                j.AxisX = dx;
                j.AxisY = dy;
                j.AxisZ = dz;
            }

            World!.RotateAssembly(o, q, W3((Vector3D)_gizmoPivot));
            // [multiselect] Unity: about the one centre (Merkez), or each about its own pivot (Pivot)
            foreach (var other in _gizmoOthers) World.RotateAssembly(other, q, GizmoCenter ? W3((Vector3D)_gizmoPivot) : other.Pose.Position);
            RefreshPlaced(o);
        }

        private List<WorldObject> _gizmoOthers = new List<WorldObject>();

        /// <summary>
        /// The other selected objects that move by themselves: not the active one, not locked, and not riding on another
        /// selected object (that one carries them).
        /// </summary>
        private List<WorldObject> OtherSelectedRoots(WorldObject active)
        {
            var world = World;
            if (world == null) return new List<WorldObject>();
            var picked = SelectedAll.Select(id => world.Find(id)).Where(x => x != null).Cast<WorldObject>().ToList();
            var ids = new HashSet<string>(picked.Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
            bool Rides(WorldObject x)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { x.Id };
                for (string? parent = world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, x.Id, StringComparison.OrdinalIgnoreCase))?.ObjectB;
                     parent != null && seen.Add(parent);
                     parent = world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, parent, StringComparison.OrdinalIgnoreCase))?.ObjectB)
                    if (ids.Contains(parent)) return true;
                return false;
            }

            return picked.Where(x => !ReferenceEquals(x, active) && !EditorLocked(x) && !Rides(x)).ToList();
        }

        private void Readout(string text)
        {
            _gizmoReadout = text;
            _hint.Text = text;
        }

        private void RefreshPlaced(WorldObject o)
        {
            foreach (var moved in new[] { o }.Concat(World!.MountedOn(o.Id)).Concat(_gizmoOthers.SelectMany(x => new[] { x }.Concat(World.MountedOn(x.Id)))))
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
            _gizmoDrag = GizmoPart.None;
            _gizmoAngle = 0;
            _gizmoParts = null;
            _gizmoJoints = null;
            _vertexOffset = null;   // [vertex-snap]
            var movedOthers = _gizmoOthers;
            _gizmoOthers = new List<WorldObject>();
            ReleaseMouseCapture();
            if (_gizmoMoved && World?.Find(Selected) is { } o)
            {
                string what = part switch
                {
                    GizmoPart.RotX or GizmoPart.RotY or GizmoPart.RotZ or GizmoPart.RotView or GizmoPart.RotFree => $"{o.Name} döndürüldü ({o.Pose.Roll:0.#}°, {o.Pose.Pitch:0.#}°, {o.Pose.Yaw:0.#}°)",
                    GizmoPart.ScaleX or GizmoPart.ScaleY or GizmoPart.ScaleZ or GizmoPart.ScaleAll => $"{o.Name} ölçeklendi",
                    GizmoPart.RectEdge or GizmoPart.RectCorner => $"{o.Name} boyutlandırıldı ({o.SizeX * 1000:0.#} × {o.SizeY * 1000:0.#} × {o.SizeZ * 1000:0.#} mm)",
                    _ => $"{o.Name} taşındı ({o.Pose.X * 1000:F0}, {o.Pose.Y * 1000:F0}, {o.Pose.Z * 1000:F0} mm)"
                };
                if (movedOthers.Count > 0) what += $" · +{movedOthers.Count} nesne";   // [multiselect]
                EditCommitted?.Invoke(what);
            }

            if (_gizmoReadout != null)
            {
                _gizmoReadout = null;
                UpdateHint();
            }

            RebuildOverlay();
            return true;
        }
    }
}
