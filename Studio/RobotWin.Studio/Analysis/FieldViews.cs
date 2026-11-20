using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RobotTwin.CoreSim.Analysis;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Analysis
{
    /// <summary>
    /// FEM result on the part's surface: von Mises stress coloured per node, seen from an isometric
    /// view (painter-sorted triangles), with the deformation exaggerated so it is visible, plus a scale.
    /// </summary>
    public sealed class StressView : FrameworkElement
    {
        private StressResult? _result;
        private StressFrame? _frame;

        public double Yaw { get; set; } = -0.7;

        public double Pitch { get; set; } = 0.55;

        public void Show(StressResult? result, StressFrame? frame)
        {
            _result = result;
            _frame = frame;
            InvalidateVisual();
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            Yaw = (p.X / Math.Max(1, ActualWidth) - 0.5) * 2 * Math.PI;
            Pitch = Math.Clamp((p.Y / Math.Max(1, ActualHeight) - 0.5) * Math.PI, -1.5, 1.5);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(TryFindResource("B.Base") as Brush ?? Brushes.Black, null, new Rect(0, 0, w, h));
            var text = TryFindResource("B.TextSecondary") as Brush ?? Brushes.Gray;
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            var face = new Typeface("Segoe UI");
            FormattedText T(string s, double size = 11) => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, text, dpi);
            var r = _result;
            var f = _frame;
            if (r == null || f == null || r.NodeCount == 0 || f.VonMises.Length < r.NodeCount)
            {
                dc.DrawText(T(r?.Notes.FirstOrDefault() ?? "Gerilme sonucu yok."), new Point(12, 12));
                return;
            }

            int n = r.NodeCount;
            var pos = new Vec3[n];
            var b = new Aabb(Vec3.Zero, Vec3.Zero);
            double size = 0;
            for (int i = 0; i < n; i++) size = Math.Max(size, r.Node(i).Length);
            double scale = f.MaxDisplacement > 0 ? 0.08 * size / f.MaxDisplacement : 0;
            double cy = Math.Cos(Yaw), sy = Math.Sin(Yaw), cp = Math.Cos(Pitch), sp = Math.Sin(Pitch);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++)
            {
                var p = r.Node(i);
                if (f.Displacement.Length >= 3 * n) p += new Vec3(f.Displacement[3 * i], f.Displacement[3 * i + 1], f.Displacement[3 * i + 2]) * scale;
                double x = cy * p.X - sy * p.Y;
                double y1 = sy * p.X + cy * p.Y;
                double y = cp * p.Z - sp * y1;
                double z = sp * p.Z + cp * y1;
                pos[i] = new Vec3(x, y, z);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            double k = Math.Min((w - 120) / Math.Max(1e-9, maxX - minX), (h - 40) / Math.Max(1e-9, maxY - minY));
            double ox = 20 + (w - 120 - k * (maxX - minX)) / 2 - k * minX, oy = 20 + (h - 40 - k * (maxY - minY)) / 2 + k * maxY;
            Point P(int i) => new Point(ox + k * pos[i].X, oy - k * pos[i].Y);

            int stride = r.Method == "beam" ? 0 : r.Order == 2 ? 6 : 3;
            double vmax = Math.Max(1e-9, f.MaxVonMises);
            if (stride > 0)
            {
                int faces = r.Surface.Length / stride;
                var order = Enumerable.Range(0, faces).OrderBy(t => pos[r.Surface[t * stride]].Z + pos[r.Surface[t * stride + 1]].Z + pos[r.Surface[t * stride + 2]].Z).ToArray();
                foreach (int t in order)
                {
                    int a = r.Surface[t * stride], c = r.Surface[t * stride + 1], d = r.Surface[t * stride + 2];
                    double v = (f.VonMises[a] + f.VonMises[c] + f.VonMises[d]) / 3 / vmax;
                    var brush = new SolidColorBrush(LinePlot.Heat(v));
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(P(a), true, true);
                        ctx.LineTo(P(c), true, false);
                        ctx.LineTo(P(d), true, false);
                    }

                    dc.DrawGeometry(brush, new Pen(brush, 0.6), g);
                }
            }
            else
            {
                for (int e = 0; e + 1 < r.Elements.Length; e += 2)
                {
                    double v = (f.VonMises[r.Elements[e]] + f.VonMises[r.Elements[e + 1]]) / 2 / vmax;
                    dc.DrawLine(new Pen(new SolidColorBrush(LinePlot.Heat(v)), 6), P(r.Elements[e]), P(r.Elements[e + 1]));
                }
            }

            // Colour scale.
            double sx = w - 70, top = 30, height = h - 70;
            for (int i = 0; i < 64; i++)
                dc.DrawRectangle(new SolidColorBrush(LinePlot.Heat(1 - i / 63.0)), null, new Rect(sx, top + i * height / 64, 14, height / 64 + 1));
            dc.DrawText(T(RobotTwin.CoreSim.Design.SiValue.Format(vmax, "Pa"), 10), new Point(sx - 8, top - 16));
            dc.DrawText(T("0", 10), new Point(sx + 2, top + height + 2));
            dc.DrawText(T($"t = {f.Time:0.000} s · deformasyon ×{scale:0} · sürükleyerek döndür", 10), new Point(12, h - 18));
        }
    }

    /// <summary>2D flow field (vorticity or speed) as an image, solid cells dark.</summary>
    public static class FlowImage
    {
        public static BitmapSource? Render(CfdResult? r, bool vorticity)
        {
            if (r == null || r.Nx == 0 || r.Ny == 0 || r.Ux.Length < r.Nx * r.Ny) return null;
            int nx = r.Nx, ny = r.Ny;
            var px = new byte[nx * ny * 4];
            double max = 1e-12;
            var field = new double[nx * ny];
            for (int i = 0; i < field.Length; i++)
            {
                field[i] = vorticity && r.Vorticity.Length == field.Length ? r.Vorticity[i] : Math.Sqrt(r.Ux[i] * r.Ux[i] + r.Uy[i] * r.Uy[i]);
                if (!r.Solid[i]) max = Math.Max(max, Math.Abs(field[i]));
            }

            if (vorticity) max *= 0.3;   // the boundary layer peak would wash out the wake
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int i = y * nx + x;
                    int o = ((ny - 1 - y) * nx + x) * 4;
                    var c = r.Solid[i] ? Color.FromRgb(40, 40, 46) : vorticity ? LinePlot.Diverging(field[i] / max) : LinePlot.Heat(field[i] / max);
                    px[o] = c.B;
                    px[o + 1] = c.G;
                    px[o + 2] = c.R;
                    px[o + 3] = 255;
                }
            }

            var bmp = BitmapSource.Create(nx, ny, 96, 96, PixelFormats.Bgra32, null, px, nx * 4);
            bmp.Freeze();
            return bmp;
        }
    }
}
