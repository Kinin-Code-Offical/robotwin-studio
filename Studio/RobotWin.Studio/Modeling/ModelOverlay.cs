using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// 2D layer over the 3D view: view/mode labels, the operator header, the axis navigator,
    /// the transform gizmo, the box-selection rectangle and the measurement label.
    /// </summary>
    internal sealed class ModelOverlay : FrameworkElement
    {
        private readonly ModelViewport _vp;
        private static readonly Typeface Ui = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface UiBold = new Typeface(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xEF)));
        private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x94)));
        private static readonly Brush PanelBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xD8, 0x1C, 0x1C, 0x1F)));
        private static readonly Pen PanelPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x37)), 1));

        private const double NavRadius = 42;
        private const double GizmoLen = 92;

        public ModelOverlay(ModelViewport vp)
        {
            _vp = vp;
            IsHitTestVisible = false;
        }

        private static T Freeze<T>(T f) where T : Freezable
        {
            f.Freeze();
            return f;
        }

        private ViewCamera Cam => _vp.Cam;
        private double Dip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Text(string s, double size, Brush b, bool bold = false) =>
            new FormattedText(RobotWin.Studio.Shell.Lang.T(s), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold ? UiBold : Ui, size, b, Dip);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 10 || h < 10) return;
            var ed = _vp.Editor;

            // ---- view / mode labels (top left, after the tool strip)
            double x0 = 64;
            dc.DrawText(Text(_vp.ViewName, 12.5, TextBrush, true), new Point(x0, 12));
            string mode = ed.Mode == EditorMode.Edit
                ? $"Düzenleme modu · {(ed.Element == ElementMode.Vertex ? "Nokta" : ed.Element == ElementMode.Edge ? "Kenar" : "Yüz")}" + (_vp.Proportional ? " · orantılı" : string.Empty)
                : "Nesne modu";
            string obj = ed.Active?.Name ?? "—";
            dc.DrawText(Text($"({mode})  {obj}", 11.5, MutedBrush), new Point(x0, 30));
            string unit = $"Izgara {FormatLen(_vp.GridStep)} · birim mm";
            dc.DrawText(Text(unit, 11, MutedBrush), new Point(x0, 47));

            // ---- operator header
            if (_vp.ModalHeader is { } header)
            {
                var ft = Text(header, 12.5, TextBrush);
                ft.MaxTextWidth = Math.Max(100, w - 160);
                var r = new Rect(w / 2 - Math.Min(ft.Width, ft.MaxTextWidth) / 2 - 12, h - 52, Math.Min(ft.Width, ft.MaxTextWidth) + 24, ft.Height + 12);
                dc.DrawRoundedRectangle(PanelBrush, PanelPen, r, 6, 6);
                dc.DrawText(ft, new Point(r.X + 12, r.Y + 6));
            }

            DrawReferences(dc);   // [refgeom]
            if (_vp.ProportionalCircle is { } pc)   // [proportional] the circle of influence, dashed
            {
                var pen = new Pen(MutedBrush, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
                dc.DrawEllipse(null, pen, pc.Centre, pc.Radius, pc.Radius);
            }
            DrawNavigator(dc, w);
            if (_vp.GizmoVisible) DrawGizmo(dc);

            // ---- box selection
            if (_vp.BoxRect is { } box)
            {
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xD0)), 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x18, 0x6E, 0x9B, 0xFF)), pen, box);
            }

            // ---- measurement label
            if (_vp.MeasureSegment is { } seg && (seg.B - seg.A).Length > 1e-9)
            {
                var d = seg.B - seg.A;
                var mid = Cam.Project((seg.A + seg.B) * 0.5);
                if (mid.Visible)
                {
                    var ft = Text(string.Format(CultureInfo.InvariantCulture, "{0:0.##} mm", d.Length), 13, TextBrush, true);
                    var ft2 = Text(string.Format(CultureInfo.InvariantCulture, "X {0:0.##}  Y {1:0.##}  Z {2:0.##}", d.X, d.Y, d.Z), 11, MutedBrush);
                    double bw = Math.Max(ft.Width, ft2.Width) + 16;
                    var r = new Rect(mid.Screen.X + 10, mid.Screen.Y - 20, bw, ft.Height + ft2.Height + 10);
                    dc.DrawRoundedRectangle(PanelBrush, PanelPen, r, 5, 5);
                    dc.DrawText(ft, new Point(r.X + 8, r.Y + 4));
                    dc.DrawText(ft2, new Point(r.X + 8, r.Y + 6 + ft.Height));
                }
            }
        }

        /// <summary>[refgeom] Reference planes (a lightly filled square), axes (a centre line) and points (a cross), each named; flat, no shading.</summary>
        private void DrawReferences(DrawingContext dc)
        {
            var sel = new HashSet<string>(_vp.Editor.Selected);
            foreach (var (o, lines, label) in _vp.ReferenceOutlines())
            {
                bool on = sel.Contains(o.Id);
                Color col;
                try
                {
                    col = on ? Color.FromRgb(0xF0, 0xA0, 0x30) : (Color)ColorConverter.ConvertFromString(o.Color);
                }
                catch (FormatException)
                {
                    col = Color.FromRgb(0x5B, 0x8D, 0xEF);
                }

                var pen = new Pen(new SolidColorBrush(col), on ? 2 : 1.2);
                if (o.Reference!.Kind == ReferenceKind.Axis) pen.DashStyle = new DashStyle(new double[] { 8, 3, 2, 3 }, 0);   // a centre line
                if (o.Reference.Kind == ReferenceKind.Plane && lines.Count == 4)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(lines[0].A, true, true);
                        for (int i = 1; i < 4; i++) c.LineTo(lines[i].A, true, false);
                    }

                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(on ? (byte)0x30 : (byte)0x18, col.R, col.G, col.B)), pen, g);
                }
                else
                {
                    foreach (var (a, b) in lines) dc.DrawLine(pen, a, b);
                }

                dc.DrawText(Text(o.Name, 11, new SolidColorBrush(col)), label);
            }
        }

        private static string FormatLen(double mm) => mm >= 1000 ? $"{mm / 1000:0.##} m" : mm >= 10 ? $"{mm / 10:0.##} cm" : $"{mm:0.###} mm";

        // ================================================================ navigator

        private Point NavCenter => new Point(ActualWidth - NavRadius - 22, NavRadius + 22);

        private IEnumerable<(Vec3 Axis, Point P, double Depth, int Index, bool Positive)> NavBubbles()
        {
            var c = NavCenter;
            for (int i = 0; i < 3; i++)
            {
                foreach (int s in new[] { 1, -1 })
                {
                    var a = (i == 0 ? Vec3.UnitX : i == 1 ? Vec3.UnitY : Vec3.UnitZ) * s;
                    var p = new Point(c.X + a.Dot(Cam.Right) * NavRadius * 0.82, c.Y - a.Dot(Cam.Up) * NavRadius * 0.82);
                    yield return (a, p, a.Dot(Cam.Forward), i, s > 0);
                }
            }
        }

        private void DrawNavigator(DrawingContext dc, double w)
        {
            var c = NavCenter;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(0x30, 0x40, 0x40, 0x48)), null, c, NavRadius + 6, NavRadius + 6);
            foreach (var b in NavBubbles().OrderByDescending(b => b.Depth))
            {
                var col = b.Index == 0 ? ModelViewport.AxisXColor : b.Index == 1 ? ModelViewport.AxisYColor : ModelViewport.AxisZColor;
                if (b.Positive)
                {
                    dc.DrawLine(new Pen(new SolidColorBrush(col), 2), c, b.P);
                    dc.DrawEllipse(new SolidColorBrush(col), null, b.P, 9, 9);
                    var t = Text(b.Index == 0 ? "X" : b.Index == 1 ? "Y" : "Z", 11, new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x12)), true);
                    dc.DrawText(t, new Point(b.P.X - t.Width / 2, b.P.Y - t.Height / 2));
                }
                else
                {
                    var dark = Color.FromArgb(0xFF, (byte)(col.R * 0.45), (byte)(col.G * 0.45), (byte)(col.B * 0.45));
                    dc.DrawEllipse(new SolidColorBrush(dark), new Pen(new SolidColorBrush(col), 1.2), b.P, 7, 7);
                }
            }

            // small buttons under the navigator: perspective/ortho and frame all
            var r1 = OrthoButton;
            var r2 = FrameButton;
            dc.DrawRoundedRectangle(PanelBrush, PanelPen, r1, 4, 4);
            dc.DrawRoundedRectangle(PanelBrush, PanelPen, r2, 4, 4);
            // [toolbar] icons, not words: a frustum (perspective) or a square (orthographic); the frame-all corners
            DrawGlyph(dc, Cam.Ortho ? "M5,5 H19 V19 H5 Z" : "M3,8 L21,4 V20 L3,16 Z M9,7 V17 M15,5.6 V18.4", r1);
            DrawGlyph(dc, "M4,9 V4 H9 M15,4 H20 V9 M20,15 V20 H15 M9,20 H4 V15", r2);
        }

        private void DrawGlyph(DrawingContext dc, string path, Rect r)
        {
            var g = Geometry.Parse(path);
            double k = 14.0 / 24;
            dc.PushTransform(new TranslateTransform(r.X + (r.Width - 14) / 2, r.Y + (r.Height - 14) / 2));
            dc.PushTransform(new ScaleTransform(k, k));
            var pen = new Pen(MutedBrush, 1.6 / k) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawGeometry(null, pen, g);
            dc.Pop();
            dc.Pop();
        }

        private Rect OrthoButton => new Rect(NavCenter.X - 28, NavCenter.Y + NavRadius + 14, 26, 22);
        private Rect FrameButton => new Rect(NavCenter.X + 2, NavCenter.Y + NavRadius + 14, 26, 22);

        /// <summary>0 = none, 1 = axis bubble (axis returned), 2 = navigator background (orbit), 3 = ortho toggle, 4 = frame all.</summary>
        public int HitNavigator(Point p, out Vec3 axis)
        {
            axis = Vec3.Zero;
            if (OrthoButton.Contains(p)) return 3;
            if (FrameButton.Contains(p)) return 4;
            foreach (var b in NavBubbles().OrderBy(b => b.Depth))
            {
                if ((b.P - p).Length <= 10)
                {
                    axis = b.Axis;
                    return 1;
                }
            }

            return (p - NavCenter).Length <= NavRadius + 6 ? 2 : 0;
        }

        // ================================================================ gizmo

        private static Vec3 Ax(int i) => i == 0 ? Vec3.UnitX : i == 1 ? Vec3.UnitY : Vec3.UnitZ;

        private static Color AxisColor(int i) => i == 0 ? ModelViewport.AxisXColor : i == 1 ? ModelViewport.AxisYColor : ModelViewport.AxisZColor;

        private static Color Hot(Color c) => Color.FromRgb((byte)Math.Min(255, c.R + 70), (byte)Math.Min(255, c.G + 70), (byte)Math.Min(255, c.B + 70));

        private (Point C, Point[] Ends) GizmoAxes(Vec3 pivot)
        {
            double px = Cam.PixelSize(pivot);
            var c = Cam.ProjectPoint(pivot);
            var ends = new Point[3];
            for (int i = 0; i < 3; i++) ends[i] = Cam.ProjectPoint(pivot + Ax(i) * (px * GizmoLen));
            return (c, ends);
        }

        private List<Point> Ring(Vec3 pivot, int axis, double radiusPx)
        {
            double px = Cam.PixelSize(pivot);
            var a = Ax(axis);
            var u = Ax((axis + 1) % 3);
            var v = a.Cross(u);
            var pts = new List<Point>(65);
            for (int k = 0; k <= 64; k++)
            {
                double t = 2 * Math.PI * k / 64;
                pts.Add(Cam.ProjectPoint(pivot + (u * Math.Cos(t) + v * Math.Sin(t)) * (px * radiusPx)));
            }

            return pts;
        }

        private void DrawGizmo(DrawingContext dc)
        {
            var drag = _vp.RingDrag;
            var pivot = drag?.Pivot ?? _vp.Pivot();   // a turn keeps its pivot while the selection turns
            if (pivot == null) return;
            var tool = _vp.Editor.Tool;
            var (c, ends) = GizmoAxes(pivot.Value);
            if (double.IsNaN(c.X) || ends.Any(e => double.IsNaN(e.X))) return;
            int hot = drag is { } dragging ? (dragging.Axis >= 0 ? dragging.Axis : 3) : _vp.HotAxis;
            if (tool == ModelTool.Rotate)
            {
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(hot == 3 ? Colors.White : Color.FromRgb(0x9A, 0x9A, 0xA4)), hot == 3 ? 2.2 : 1.4), c, GizmoLen + 10, GizmoLen + 10);
                if (drag is { Axis: >= 0 } d) DrawSector(dc, pivot.Value, d.Axis, d.Degrees, d.Start);
                for (int i = 0; i < 3; i++)
                {
                    // [gizmo] as Unity: the half of each ring facing the camera (the back half would cross the front)
                    var pts = Ring(pivot.Value, i, GizmoLen * 0.85);
                    var front = FrontHalf(pivot.Value, i, pts.Count);
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        bool open = false;
                        for (int k = 0; k + 1 < pts.Count; k++)
                        {
                            if (!front[k])
                            {
                                open = false;
                                continue;
                            }

                            if (!open) g.BeginFigure(pts[k], false, false);
                            g.LineTo(pts[k + 1], true, true);
                            open = true;
                        }
                    }

                    var col = hot == i ? Hot(AxisColor(i)) : AxisColor(i);
                    dc.DrawGeometry(null, new Pen(new SolidColorBrush(col), hot == i ? 3.2 : 2.2), geo);
                }

                if (drag is { } dd)
                {
                    var label = new FormattedText($"{dd.Degrees:0.#}°", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 1.0);
                    dc.DrawText(label, new Point(c.X + GizmoLen + 16, c.Y - label.Height / 2));
                }

                return;
            }

            for (int i = 0; i < 3; i++)
            {
                var dir = ends[i] - c;
                if (dir.Length < 4) continue;
                var col = hot == i ? Hot(AxisColor(i)) : AxisColor(i);
                var brush = new SolidColorBrush(col);
                var unit = dir / dir.Length;
                var from = c + unit * 14;
                dc.DrawLine(new Pen(brush, hot == i ? 3.4 : 2.4), from, ends[i]);
                if (tool == ModelTool.Scale)
                {
                    dc.DrawRectangle(brush, null, new Rect(ends[i].X - 5, ends[i].Y - 5, 10, 10));
                }
                else
                {
                    var n = new Vector(-unit.Y, unit.X);
                    var tip = ends[i] + unit * 12;
                    var geo = new StreamGeometry();
                    using (var g = geo.Open())
                    {
                        g.BeginFigure(tip, true, true);
                        g.LineTo(ends[i] + n * 6, true, true);
                        g.LineTo(ends[i] - n * 6, true, true);
                    }

                    dc.DrawGeometry(brush, null, geo);
                }
            }

            if (tool == ModelTool.Move)
            {
                for (int ex = 0; ex < 3; ex++)
                {
                    int a = (ex + 1) % 3, b = (ex + 2) % 3;
                    var pc = PlaneHandle(c, ends, a, b);
                    if (pc == null) continue;
                    var col = AxisColor(ex);
                    var fill = new SolidColorBrush(Color.FromArgb(hot == 4 + ex ? (byte)0xE0 : (byte)0x90, col.R, col.G, col.B));
                    dc.DrawRectangle(fill, null, new Rect(pc.Value.X - 6, pc.Value.Y - 6, 12, 12));
                }
            }

            dc.DrawEllipse(hot == 3 ? new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)) : null,
                new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE6)), 1.6), c, 9, 9);
        }

        /// <summary>[gizmo] Which segments of a ring (as <see cref="Ring"/> samples it) face the camera.</summary>
        private bool[] FrontHalf(Vec3 pivot, int axis, int count)
        {
            var a = Ax(axis);
            var u = Ax((axis + 1) % 3);
            var v = a.Cross(u);
            var toCamera = Cam.Position - pivot;
            var front = new bool[count];
            for (int k = 0; k + 1 < count; k++)
            {
                double t = 2 * Math.PI * (k + 0.5) / 64;
                front[k] = (u * Math.Cos(t) + v * Math.Sin(t)).Dot(toCamera) >= -1e-9;
            }

            return front;
        }

        /// <summary>[gizmo] The turn so far as a flat pie on the ring's plane, from where the drag began.</summary>
        private void DrawSector(DrawingContext dc, Vec3 pivot, int axis, double degrees, Point start)
        {
            if (Math.Abs(degrees) < 0.05) return;
            var pts = Ring(pivot, axis, GizmoLen * 0.85);
            int k0 = 0;
            double best = double.MaxValue;
            for (int k = 0; k < pts.Count - 1; k++)
            {
                double dd = (pts[k] - start).LengthSquared;
                if (dd < best)
                {
                    best = dd;
                    k0 = k;
                }
            }

            double px = Cam.PixelSize(pivot);
            var a = Ax(axis);
            var u = Ax((axis + 1) % 3);
            var v = a.Cross(u);
            double t0 = 2 * Math.PI * k0 / 64, sweep = degrees * Math.PI / 180;
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(degrees) / 4));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(Cam.ProjectPoint(pivot), true, true);
                for (int s = 0; s <= steps; s++)
                {
                    double t = t0 + sweep * s / steps;
                    g.LineTo(Cam.ProjectPoint(pivot + (u * Math.Cos(t) + v * Math.Sin(t)) * (px * GizmoLen * 0.85)), true, true);
                }
            }

            var col = AxisColor(axis);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x40, col.R, col.G, col.B)), new Pen(new SolidColorBrush(Color.FromArgb(0x90, col.R, col.G, col.B)), 1), geo);
        }

        private static Point? PlaneHandle(Point c, Point[] ends, int a, int b)
        {
            var da = ends[a] - c;
            var db = ends[b] - c;
            if (da.Length < 8 || db.Length < 8) return null;
            return c + (da + db) * 0.3;
        }

        /// <summary>−1 none, 0..2 axis, 3 centre (free / uniform / view ring), 4..6 plane excluding axis (move).</summary>
        public int HitGizmo(Point p)
        {
            var pivot = _vp.Pivot();
            if (pivot == null) return -1;
            var tool = _vp.Editor.Tool;
            var (c, ends) = GizmoAxes(pivot.Value);
            if (double.IsNaN(c.X)) return -1;
            if (tool == ModelTool.Rotate)
            {
                int best = -1;
                double bestD = 7;
                for (int i = 0; i < 3; i++)
                {
                    var pts = Ring(pivot.Value, i, GizmoLen * 0.85);
                    var front = FrontHalf(pivot.Value, i, pts.Count);   // [gizmo] only the half that is drawn
                    for (int k = 0; k + 1 < pts.Count; k++)
                    {
                        if (!front[k]) continue;
                        double d = ModelViewport.SegmentDistance(p, pts[k], pts[k + 1], out _);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = i;
                        }
                    }
                }

                if (best >= 0) return best;
                return Math.Abs((p - c).Length - (GizmoLen + 10)) < 7 ? 3 : -1;
            }

            if ((p - c).Length <= 11) return 3;
            if (tool == ModelTool.Move)
            {
                for (int ex = 0; ex < 3; ex++)
                {
                    var pc = PlaneHandle(c, ends, (ex + 1) % 3, (ex + 2) % 3);
                    if (pc != null && Math.Abs(p.X - pc.Value.X) <= 7 && Math.Abs(p.Y - pc.Value.Y) <= 7) return 4 + ex;
                }
            }

            int hit = -1;
            double hd = 8;
            for (int i = 0; i < 3; i++)
            {
                var dir = ends[i] - c;
                if (dir.Length < 4) continue;
                var tip = ends[i] + dir / dir.Length * 12;
                double d = ModelViewport.SegmentDistance(p, c + dir / dir.Length * 12, tip, out _);
                if (d < hd)
                {
                    hd = d;
                    hit = i;
                }
            }

            return hit;
        }
    }
}
