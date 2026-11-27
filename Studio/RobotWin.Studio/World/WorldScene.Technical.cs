using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [technical] The joints and centre of mass of the selection, drawn over the view (always in sight, as SolidWorks
    /// draws its centre-of-mass symbol and mates: never hidden inside the part or behind the handle's arrows): each joint's
    /// anchor as an amber dot with the axis it turns about or slides along and, dashed, how far it may go (a hinge's
    /// angle range as an arc round the axis, a slider's travel along it); the centre of mass as the drawing symbol (a
    /// circle with two dark quarters).
    /// </summary>
    internal sealed class TechMarks : FrameworkElement
    {
        public readonly System.Collections.Generic.List<(Point Anchor, Point? A, Point? B)> Joints = new System.Collections.Generic.List<(Point, Point?, Point?)>();
        public readonly System.Collections.Generic.List<Point[]> Limits = new System.Collections.Generic.List<Point[]>();
        public readonly System.Collections.Generic.List<Point[]> Turns = new System.Collections.Generic.List<Point[]>();
        public Point? Com;

        private static readonly Brush Amber = Frozen(new SolidColorBrush(Color.FromRgb(0xF2, 0xB8, 0x3B)));
        private static readonly Brush Light = Frozen(new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF5)));
        private static readonly Brush Dark = Frozen(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)));
        private static readonly Pen AxisPen = Frozen(new Pen(Amber, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        private static readonly Pen Outline = Frozen(new Pen(Dark, 1.2));
        private static readonly Pen LimitPen = Frozen(new Pen(Amber, 1.5) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0), StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });

        private static T Frozen<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        public TechMarks() => IsHitTestVisible = false;

        protected override void OnRender(DrawingContext dc)
        {
            foreach (var line in Limits)
            {
                if (line.Length < 2) continue;
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(line[0], false, false);
                    for (int i = 1; i < line.Length; i++) s.LineTo(line[i], true, true);
                }

                g.Freeze();
                dc.DrawGeometry(null, LimitPen, g);
                // its two ends: where it stops
                dc.DrawEllipse(Amber, null, line[0], 2.5, 2.5);
                dc.DrawEllipse(Amber, null, line[^1], 2.5, 2.5);
            }

            // [technical] which way a shaft's torque turns: an arc round it with its head where it pushes
            foreach (var arc in Turns)
            {
                if (arc.Length < 3) continue;
                var g = new StreamGeometry();
                using (var s = g.Open())
                {
                    s.BeginFigure(arc[0], false, false);
                    for (int i = 1; i < arc.Length; i++) s.LineTo(arc[i], true, true);
                }

                g.Freeze();
                dc.DrawGeometry(null, AxisPen, g);
                var tip = arc[^1];
                var dir = tip - arc[^2];
                if (dir.Length < 1e-6) continue;
                dir.Normalize();
                var side = new Vector(-dir.Y, dir.X);
                var head = new StreamGeometry();
                using (var s = head.Open())
                {
                    s.BeginFigure(tip + dir * 5, true, true);
                    s.LineTo(tip - dir * 3 + side * 4.5, true, true);
                    s.LineTo(tip - dir * 3 - side * 4.5, true, true);
                }

                head.Freeze();
                dc.DrawGeometry(Amber, null, head);
            }

            foreach (var (anchor, a, b) in Joints)
            {
                if (a is { } pa && b is { } pb) dc.DrawLine(AxisPen, pa, pb);
                dc.DrawEllipse(Amber, Outline, anchor, 4.5, 4.5);
            }

            if (Com is { } c)
            {
                const double r = 7;
                dc.DrawEllipse(Light, null, c, r, r);
                dc.DrawGeometry(Dark, null, Quarter(c, r, 180));   // top left
                dc.DrawGeometry(Dark, null, Quarter(c, r, 0));     // bottom right
                dc.DrawEllipse(null, Outline, c, r, r);
            }
        }

        /// <summary>A quarter of a disc from <paramref name="start"/>° (screen: 0 = right, 90 = down) a quarter turn on.</summary>
        private static Geometry Quarter(Point c, double r, double start)
        {
            double a0 = start * Math.PI / 180, a1 = (start + 90) * Math.PI / 180;
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(c, true, true);
                s.LineTo(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false, false);
                s.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
            }

            g.Freeze();
            return g;
        }
    }

    /// <summary>
    /// [technical] What the selected object is tied to, drawn on it (the roadmap's "seçilen nesnenin teknik vurgusu", as
    /// SolidWorks shows a part's mates and centre of mass on selection): its cables lit in the accent colour, its joints as
    /// their anchor with the axis they turn or slide along, its centre of mass where the physics puts it; and a small label
    /// beside it with its mass, cables and joints (and its speed while the world runs). Off from the view's overlay menu.
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly Border _techLabel = new Border
        {
            IsHitTestVisible = false, Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 3, 7, 3),
            BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };

        private readonly TextBlock _techText = new TextBlock { FontSize = 11.5 };
        private readonly TranslateTransform _techLabelAt = new TranslateTransform();
        private readonly TechMarks _techMarks = new TechMarks();
        private Point3D? _techAt;   // where the label stands (world): the top of the selection
        private Point3D? _techCom;
        private readonly System.Collections.Generic.List<(Point3D Anchor, Point3D? A, Point3D? B)> _techJoints = new System.Collections.Generic.List<(Point3D, Point3D?, Point3D?)>();
        private readonly System.Collections.Generic.List<Point3D[]> _techLimits = new System.Collections.Generic.List<Point3D[]>();
        private readonly System.Collections.Generic.List<Point3D[]> _techTurns = new System.Collections.Generic.List<Point3D[]>();

        private bool _showTechnical = true;

        /// <summary>The selected object's technical highlight (cables, joints, centre of mass, its values beside it).</summary>
        public bool ShowTechnical
        {
            get => _showTechnical;
            set
            {
                if (_showTechnical == value) return;
                _showTechnical = value;
                RebuildOverlay();
                RebuildCables();
            }
        }

        /// <summary>The object's estimated mass (g) when the physics has no body for it (the inspector's "Otomatik kütle").</summary>
        public Func<WorldObject, double>? EstimatedMassGrams { get; set; }

        private void InitTechnical()
        {
            _techLabel.Child = _techText;
            _techLabel.SetResourceReference(Border.BackgroundProperty, "B.Overlay");
            _techLabel.SetResourceReference(Border.BorderBrushProperty, "B.BorderStrong");
            _techText.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            _techLabel.RenderTransform = _techLabelAt;
            Children.Add(_techMarks);
            Children.Add(_techLabel);
        }

        /// <summary>A cable that ends on the selected object: lit with it.</summary>
        private bool TiedToSelection(WorldCable c) =>
            _showTechnical && Selected != null &&
            (string.Equals(c.A.ObjectId, Selected, StringComparison.OrdinalIgnoreCase) || string.Equals(c.B.ObjectId, Selected, StringComparison.OrdinalIgnoreCase));

        /// <summary>The joints and centre of mass of the selection into the overlay, and the label's text.</summary>
        private void AppendTechnical(Model3DGroup g)
        {
            _techAt = null;
            _techCom = null;
            _techJoints.Clear();
            _techLimits.Clear();
            _techTurns.Clear();
            if (!_showTechnical || World == null || Selected == null || !_items.TryGetValue(Selected, out var it) || it.Model.Bounds.IsEmpty)
            {
                PlaceTechLabel();
                return;
            }

            var bounds = new MatrixTransform3D(ExplodedMatrix(it.Object.Id, it.Current)).TransformBounds(it.Model.Bounds);
            double size = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));

            // its joints: the anchor, and the axis a hinge turns about or a slider runs along
            int joints = 0;
            foreach (var j in World.Joints)
            {
                bool a = string.Equals(j.ObjectA, Selected, StringComparison.OrdinalIgnoreCase);
                if (!a && !string.Equals(j.ObjectB, Selected, StringComparison.OrdinalIgnoreCase)) continue;
                joints++;
                var (anchor, axis) = JointNow(j);
                if (j.Kind != WorldJointKind.Fixed && axis.Length > 1e-9)
                {
                    axis.Normalize();
                    double half = Math.Max(0.01, Math.Min(0.25, size * 0.6));
                    _techJoints.Add((anchor, anchor - axis * half, anchor + axis * half));
                    if (LimitOf(j, size) is { } limit) _techLimits.Add(limit);
                }
                else _techJoints.Add((anchor, null, null));
            }

            // the centre of mass where the physics has it
            var (com, mass, speed) = BodyOf(it, bounds);
            _techCom = com;

            // [technical] a servo's or motor's output shaft: its axis through the horn / shaft, and while the world runs the
            // way its torque turns (an arrowed arc round it) with the torque in the label
            double? torque = null;
            if (it.Model.Port is { } port)
            {
                var pivot = it.Current.Transform(port.Pivot);
                var shaft = it.Current.Transform(port.Axis);
                if (shaft.Length > 1e-9)
                {
                    shaft.Normalize();
                    double reach = Math.Max(0.008, Math.Min(0.08, size * 0.5));
                    _techJoints.Add((pivot, pivot - shaft * reach * 0.3, pivot + shaft * reach));
                    if (Session is { State: not Simulation.SimState.Stopped } && Session.Physics?.PortJoint(it.Object.Id) is { } rotor)
                    {
                        double t = Math.Abs(rotor.MotorTorque) > 1e-9 ? rotor.MotorTorque : rotor.DriveTorque;
                        if (Math.Abs(t) > 1e-6)
                        {
                            torque = t;
                            _techTurns.Add(TurnArc(pivot + shaft * reach * 0.6, shaft, Math.Max(0.006, Math.Min(0.04, size * 0.22)), Math.Sign(t)));
                        }
                    }
                }
            }


            int cables = World.Cables.Count(TiedToSelection);
            var parts = new System.Collections.Generic.List<string> { string.IsNullOrEmpty(it.Object.Name) ? it.Object.Id : it.Object.Name };
            if (mass > 0) parts.Add(mass < 1 ? (mass * 1000).ToString("0.#", CultureInfo.CurrentCulture) + " g" : mass.ToString("0.###", CultureInfo.CurrentCulture) + " kg");
            parts.Add(Lang.T(cables + " kablo"));
            parts.Add(Lang.T(joints + " bağlantı"));
            if (speed is double v && Session is { State: not Simulation.SimState.Stopped }) parts.Add(v.ToString("0.00", CultureInfo.CurrentCulture) + " m/s");
            if (torque is double tq) parts.Add("τ " + tq.ToString("0.000", CultureInfo.CurrentCulture) + " N·m");
            _techText.Text = string.Join(" · ", parts);
            _techAt = new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ);
            PlaceTechLabel();
        }

        /// <summary>
        /// [technical] How far a joint may go, as a line in the world: a hinge's range (°) as an arc round its axis, from the
        /// side its other object lies on (its angle as placed is 0); a slider's travel (m) along its axis. Null when free.
        /// </summary>
        private Point3D[]? LimitOf(WorldJoint j, double size)
        {
            if (j.Lower is not { } lo || j.Upper is not { } hi || hi <= lo) return null;
            // worked out where the objects were placed, then carried with the first object (the arc does not turn with the part it limits)
            var anchor = new Point3D(j.AnchorX, j.AnchorY, j.AnchorZ);
            var axis = new Vector3D(j.AxisX, j.AxisY, j.AxisZ);
            if (axis.Length < 1e-9) return null;
            axis.Normalize();
            if (!_items.TryGetValue(j.ObjectA, out var first)) return null;
            if (j.AnchorX == 0 && j.AnchorY == 0 && j.AnchorZ == 0) anchor = new Point3D(first.Placed.OffsetX, first.Placed.OffsetY, first.Placed.OffsetZ);
            var moved = first.Placed;
            if (!moved.HasInverse) return null;
            moved.Invert();
            moved *= first.Current;
            if (j.Kind == WorldJointKind.Slider)
                return new[] { moved.Transform(anchor + axis * lo), moved.Transform(anchor + axis * hi) };
            if (j.Kind != WorldJointKind.Hinge) return null;

            // the arc's zero: towards the selected object's middle, square to the axis (the part it limits, as placed); a
            // part centred on the axis (a bearing, a hub) looks towards the other one; any square direction as a last resort
            Vector3D Towards(string? id)
            {
                if (id == null || !_items.TryGetValue(id, out var o) || o.Model.Bounds.IsEmpty) return new Vector3D();
                var d = Middle(new MatrixTransform3D(o.Placed).TransformBounds(o.Model.Bounds)) - anchor;
                return d - axis * Vector3D.DotProduct(d, axis);
            }

            var zero = Towards(Selected);
            if (zero.Length < 1e-3) zero = Towards(string.Equals(j.ObjectA, Selected, StringComparison.OrdinalIgnoreCase) ? j.ObjectB : j.ObjectA);
            if (zero.Length < 1e-9) zero = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0));
            zero.Normalize();
            var side = Vector3D.CrossProduct(axis, zero);
            double radius = Math.Max(0.01, Math.Min(0.15, size * 0.5));
            int n = Math.Max(6, (int)Math.Ceiling((hi - lo) / 6));
            var pts = new Point3D[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double a = (lo + (hi - lo) * i / n) * Math.PI / 180;
                pts[i] = moved.Transform(anchor + (zero * Math.Cos(a) + side * Math.Sin(a)) * radius);
            }

            return pts;
        }

        /// <summary>Three quarters of a circle round an axis, turning the way the right hand's fingers do for a positive sign.</summary>
        private static Point3D[] TurnArc(Point3D centre, Vector3D axis, double radius, int sign)
        {
            var u = Vector3D.CrossProduct(axis, Math.Abs(axis.Z) < 0.9 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0));
            u.Normalize();
            var w = Vector3D.CrossProduct(axis, u);
            const int n = 18;
            var pts = new Point3D[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double a = sign * 1.5 * Math.PI * i / n;
                pts[i] = centre + (u * Math.Cos(a) + w * Math.Sin(a)) * radius;
            }

            return pts;
        }

        private static Point3D Middle(Rect3D b) => new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);

        /// <summary>
        /// A joint's anchor and axis now: kept in world coordinates of the placed objects, carried with its first object as
        /// it moves in a run; (0, 0, 0) means the joint has no anchor of its own: the object's origin.
        /// </summary>
        private (Point3D Anchor, Vector3D Axis) JointNow(WorldJoint j)
        {
            var anchor = new Point3D(j.AnchorX, j.AnchorY, j.AnchorZ);
            var axis = new Vector3D(j.AxisX, j.AxisY, j.AxisZ);
            if (!_items.TryGetValue(j.ObjectA, out var a)) return (anchor, axis);
            if (j.AnchorX == 0 && j.AnchorY == 0 && j.AnchorZ == 0) anchor = new Point3D(a.Placed.OffsetX, a.Placed.OffsetY, a.Placed.OffsetZ);
            var placed = a.Placed;
            if (!placed.HasInverse) return (anchor, axis);
            placed.Invert();
            var moved = placed * a.Current;   // placed → now
            return (moved.Transform(anchor), moved.Transform(axis));
        }

        /// <summary>The object's centre of mass (world), its mass (kg) and speed (m/s) from its physics body; its middle without one.</summary>
        private (Point3D Com, double Mass, double? Speed) BodyOf(Item it, Rect3D bounds)
        {
            double mass = it.Object.MassKg ?? (EstimatedMassGrams?.Invoke(it.Object) ?? 0) / 1000;
            var body = Session?.Physics?.Scene.FindBody(it.Object.Id);
            if (body == null && !CablesSettling) body = _cableLayout?.Scene.FindBody(it.Object.Id);
            if (body != null)
            {
                var p = body.Position;
                bool moving = !body.IsStatic && body.Mass > 0 && !double.IsInfinity(body.Mass);
                return (new Point3D(p.X, p.Y, p.Z), moving ? body.Mass : mass, moving ? body.Velocity.Length : null);
            }

            return (new Point3D(bounds.X + bounds.SizeX / 2, bounds.Y + bounds.SizeY / 2, bounds.Z + bounds.SizeZ / 2), mass, null);
        }

        /// <summary>The label just above the selection and the marks on screen (each frame: the camera may have moved).</summary>
        private void PlaceTechLabel()
        {
            Point? On(Point3D? p) => p is { } w && Project(w) is var (s, _, visible) && visible ? s : null;
            bool hadMarks = _techMarks.Com != null || _techMarks.Joints.Count > 0 || _techMarks.Limits.Count > 0;
            _techMarks.Joints.Clear();
            foreach (var (anchor, a, b) in _techJoints)
                if (On(anchor) is { } pa) _techMarks.Joints.Add((pa, On(a), On(b)));
            _techMarks.Limits.Clear();
            foreach (var line in _techLimits)
            {
                var screen = line.Select(p => On(p)).ToList();
                if (screen.All(p => p != null)) _techMarks.Limits.Add(screen.Select(p => p!.Value).ToArray());
            }

            hadMarks |= _techMarks.Turns.Count > 0;
            _techMarks.Turns.Clear();
            foreach (var arc in _techTurns)
            {
                var screen = arc.Select(p => On(p)).ToList();
                if (screen.All(p => p != null)) _techMarks.Turns.Add(screen.Select(p => p!.Value).ToArray());
            }
            _techMarks.Com = On(_techCom);
            if (hadMarks || _techMarks.Com != null || _techMarks.Joints.Count > 0) _techMarks.InvalidateVisual();

            if (_techAt is not { } at)
            {
                _techLabel.Visibility = Visibility.Collapsed;
                return;
            }

            var (s, _, visible) = Project(at);
            if (!visible || ActualWidth <= 0)
            {
                _techLabel.Visibility = Visibility.Collapsed;
                return;
            }

            _techLabel.Visibility = Visibility.Visible;
            _techLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var d = _techLabel.DesiredSize;
            double x = Math.Max(4, Math.Min(ActualWidth - d.Width - 4, s.X - d.Width / 2));
            double y = s.Y - d.Height - 14;
            if (y < 56) y = s.Y + 14;   // under the top's tool strips it would hide: below the point instead
            y = Math.Max(4, Math.Min(ActualHeight - d.Height - 4, y));
            _techLabelAt.X = Math.Round(x);
            _techLabelAt.Y = Math.Round(y);
        }
    }
}
