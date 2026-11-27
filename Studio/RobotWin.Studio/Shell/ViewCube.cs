using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [viewcube] SolidWorks' view selector / a CAD view cube: a cube turned as the camera is, with no words on it. Its faces
    /// are tinted by their axis (X red, Y green, Z blue; the plus side stronger), a face under the cursor lights up with its
    /// view's name in a tooltip, and a click turns the camera to look at that face; a click near a corner gives the
    /// isometric view from that corner. The camera's turn comes in as a yaw about Z and a pitch down (the world view's).
    /// </summary>
    public sealed class ViewCube : FrameworkElement
    {
        private double _yaw = 1.15, _pitch = 0.62;
        private int _hot = -1;   // face 0–5, corner 6–13, −1 none

        public ViewCube()
        {
            Width = 84;
            Height = 84;
            Cursor = Cursors.Hand;
            System.Windows.Controls.ToolTipService.SetInitialShowDelay(this, 250);
            MouseMove += (_, e) => SetHot(HitTest(e.GetPosition(this)));
            MouseLeave += (_, _) => SetHot(-1);
            MouseLeftButtonUp += (_, e) =>
            {
                int hit = HitTest(e.GetPosition(this));
                if (hit < 0) return;
                var (yaw, pitch) = ViewOf(hit);
                ViewRequested?.Invoke(yaw, pitch);
                e.Handled = true;
            };
            MouseDown += (_, e) => e.Handled = true;
        }

        /// <summary>Raised with the yaw and pitch of the view a face or a corner stands for.</summary>
        public event Action<double, double>? ViewRequested;

        /// <summary>The camera's turn now (radians): the cube follows it.</summary>
        public void SetCamera(double yaw, double pitch)
        {
            _yaw = yaw;
            _pitch = pitch;
            InvalidateVisual();
        }

        // the faces: outward normal, the axis' colour, the view's name (tooltip only)
        private static readonly (Vector3 N, Color C, string Name)[] Faces =
        {
            (new Vector3(0, 0, 1), Color.FromRgb(0x46, 0x7C, 0xF0), "Üst"),
            (new Vector3(0, 0, -1), Color.FromRgb(0x46, 0x7C, 0xF0), "Alt"),
            (new Vector3(0, -1, 0), Color.FromRgb(0x3C, 0xD0, 0x64), "Ön"),
            (new Vector3(0, 1, 0), Color.FromRgb(0x3C, 0xD0, 0x64), "Arka"),
            (new Vector3(1, 0, 0), Color.FromRgb(0xE5, 0x48, 0x4D), "Sağ"),
            (new Vector3(-1, 0, 0), Color.FromRgb(0xE5, 0x48, 0x4D), "Sol")
        };

        private readonly struct Vector3
        {
            public Vector3(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public double Dot(Vector3 o) => X * o.X + Y * o.Y + Z * o.Z;
        }

        private (Vector3 Look, Vector3 Right, Vector3 Up) Basis()
        {
            var look = new Vector3(Math.Cos(_pitch) * Math.Cos(_yaw), Math.Cos(_pitch) * Math.Sin(_yaw), -Math.Sin(_pitch));
            // right = look × Z, up = right × look
            var right = new Vector3(look.Y, -look.X, 0);
            double rl = Math.Sqrt(right.X * right.X + right.Y * right.Y);
            right = rl > 1e-9 ? new Vector3(right.X / rl, right.Y / rl, 0) : new Vector3(1, 0, 0);
            var up = new Vector3(right.Y * look.Z - right.Z * look.Y, right.Z * look.X - right.X * look.Z, right.X * look.Y - right.Y * look.X);
            return (look, right, up);
        }

        private Point Project(Vector3 v, (Vector3 Look, Vector3 Right, Vector3 Up) b)
        {
            double s = Math.Min(ActualWidth, ActualHeight) * 0.29;
            return new Point(ActualWidth / 2 + v.Dot(b.Right) * s, ActualHeight / 2 - v.Dot(b.Up) * s);
        }

        /// <summary>A face's four corners, in order round it.</summary>
        private static Vector3[] Quad(Vector3 n)
        {
            // two axes across the normal
            var u = Math.Abs(n.Z) > 0.5 ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
            var w = new Vector3(n.Y * u.Z - n.Z * u.Y, n.Z * u.X - n.X * u.Z, n.X * u.Y - n.Y * u.X);
            Vector3 P(double a, double c) => new Vector3(n.X + u.X * a + w.X * c, n.Y + u.Y * a + w.Y * c, n.Z + u.Z * a + w.Z * c);
            return new[] { P(-1, -1), P(1, -1), P(1, 1), P(-1, 1) };
        }

        private static IEnumerable<Vector3> Corners()
        {
            foreach (int x in new[] { -1, 1 })
                foreach (int y in new[] { -1, 1 })
                    foreach (int z in new[] { -1, 1 })
                        yield return new Vector3(x, y, z);
        }

        private int HitTest(Point p)
        {
            var b = Basis();
            // corners first (the isometric views), then the faces facing the camera
            int k = 0;
            foreach (var c in Corners())
            {
                if (c.Dot(b.Look) < 0 && (Project(c, b) - p).Length < 9) return 6 + k;
                k++;
            }

            for (int i = 0; i < Faces.Length; i++)
            {
                if (Faces[i].N.Dot(b.Look) >= -1e-6) continue;
                var poly = Quad(Faces[i].N).Select(v => Project(v, b)).ToArray();
                if (Inside(p, poly)) return i;
            }

            return -1;
        }

        private static bool Inside(Point p, Point[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>The camera's yaw and pitch for a face (looking at it) or a corner (looking from it, isometric).</summary>
        private static (double Yaw, double Pitch) ViewOf(int hit)
        {
            Vector3 from = hit < 6 ? Faces[hit].N : Corners().ElementAt(hit - 6);
            double len = Math.Sqrt(from.Dot(from));
            double lx = -from.X / len, ly = -from.Y / len, lz = -from.Z / len;   // the camera looks back at the cube from there
            double pitch = Math.Asin(Math.Max(-1, Math.Min(1, -lz)));
            pitch = Math.Max(-1.5, Math.Min(1.5, pitch));   // straight down or up keeps a yaw: +X to the right, as the top view does
            double yaw = Math.Abs(lx) + Math.Abs(ly) < 1e-9 ? Math.PI / 2 : Math.Atan2(ly, lx);
            return (yaw, pitch);
        }

        private void SetHot(int hot)
        {
            if (hot == _hot) return;
            _hot = hot;
            ToolTip = hot < 0 ? null : hot < 6 ? Lang.T(Faces[hot].Name) + " " + Lang.T("görünüm") : Lang.T("Köşeden (izometrik)");
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var b = Basis();
            // no backdrop: the cube stands on the view itself (a clear square only so the whole corner takes the cursor)
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            var edge = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0x1E, 0x1E, 0x24)), 1);
            edge.Freeze();
            // far faces first, so the near ones lie over them
            foreach (int i in Enumerable.Range(0, Faces.Length).Where(i => Faces[i].N.Dot(b.Look) < -1e-6).OrderByDescending(i => Faces[i].N.Dot(b.Look)))
            {
                var (n, c, _) = Faces[i];
                bool plus = n.X + n.Y + n.Z > 0;
                double facing = -n.Dot(b.Look);
                byte a = (byte)(i == _hot ? 0xF0 : (plus ? 0xB0 : 0x70) * (0.55 + 0.45 * facing));
                var fill = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    var q = Quad(n).Select(v => Project(v, b)).ToArray();
                    ctx.BeginFigure(q[0], true, true);
                    for (int k = 1; k < q.Length; k++) ctx.LineTo(q[k], true, true);
                }

                g.Freeze();
                dc.DrawGeometry(fill, edge, g);
            }

            // the corners facing the camera: small dots, lit under the cursor
            int idx = 0;
            foreach (var corner in Corners())
            {
                if (corner.Dot(b.Look) < 0)
                {
                    bool hot = _hot == 6 + idx;
                    dc.DrawEllipse(new SolidColorBrush(hot ? Color.FromRgb(0xF2, 0xD0, 0x4C) : Color.FromArgb(0xC8, 0xE6, 0xE6, 0xEA)), null, Project(corner, b), hot ? 4.5 : 2.6, hot ? 4.5 : 2.6);
                }

                idx++;
            }
        }
    }
}
