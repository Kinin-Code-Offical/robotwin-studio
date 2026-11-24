using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Analysis
{
    /// <summary>One curve of a <see cref="LinePlot"/>.</summary>
    public sealed class PlotSeries
    {
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public double[] X { get; set; } = Array.Empty<double>();
        public double[] Y { get; set; } = Array.Empty<double>();
        public Color Color { get; set; } = Colors.SteelBlue;

        /// <summary>Optional uncertainty band (same length as <see cref="X"/>), drawn as a translucent fill.</summary>
        public double[]? Lo { get; set; }

        public double[]? Hi { get; set; }
    }

    /// <summary>
    /// Light-weight x–y plot (time series, spectra): nice tick spacing, grid, legend, min/max-per-pixel
    /// decimation so a million-sample channel draws as fast as a short one. Flat colours, no effects.
    /// </summary>
    public sealed class LinePlot : FrameworkElement
    {
        public static readonly Color[] Palette =
        {
            Color.FromRgb(0x3B, 0x82, 0xF6), Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x10, 0xB9, 0x81),
            Color.FromRgb(0xEF, 0x44, 0x44), Color.FromRgb(0xA8, 0x55, 0xF7), Color.FromRgb(0x06, 0xB6, 0xD4)
        };

        public List<PlotSeries> Series { get; } = new List<PlotSeries>();

        public string XLabel { get; set; } = "t (s)";

        public string Title { get; set; } = string.Empty;

        public void Refresh() => InvalidateVisual();

        /// <summary>Time cursor (vertical line), or null.</summary>
        public double? Cursor { get; set; }

        /// <summary>The user clicked / dragged on the plot at this x value (replay: jump to that second).</summary>
        public event Action<double>? Seek;

        private double _vx0, _vx1;
        private Rect _varea = Rect.Empty;

        private void SeekAt(Point p)
        {
            if (_varea.IsEmpty || _varea.Width <= 0 || Seek == null) return;
            double x = _vx0 + (p.X - _varea.Left) / _varea.Width * (_vx1 - _vx0);
            Seek(Math.Max(_vx0, Math.Min(_vx1, x)));
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            SeekAt(e.GetPosition(this));
            CaptureMouse();
        }

        protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsMouseCaptured) SeekAt(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            ReleaseMouseCapture();
        }

        private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var text = Res("B.TextSecondary", Brushes.Gray);
            var grid = new Pen(Res("B.Border", Brushes.DimGray), 1);
            dc.DrawRectangle(Res("B.Base", Brushes.Black), null, new Rect(0, 0, w, h));
            var face = new Typeface("Segoe UI");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            FormattedText T(string s, Brush b, double size = 12) => new FormattedText(RobotWin.Studio.Shell.Lang.T(s), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, b, dpi);

            var live = Series.Where(s => s.X.Length > 0 && s.X.Length == s.Y.Length).ToList();
            if (Title.Length > 0) dc.DrawText(T(Title, Res("B.Text", Brushes.White), 12), new Point(8, 4));
            if (live.Count == 0)
            {
                dc.DrawText(T("Grafik için soldan bir veya daha fazla kanal seçin.", text), new Point(12, h / 2 - 8));
                return;
            }

            double x0 = live.Min(s => s.X[0]), x1 = live.Max(s => s.X[^1]);
            double y0 = double.PositiveInfinity, y1 = double.NegativeInfinity;
            foreach (var s in live)
            {
                foreach (var arr in new[] { s.Y, s.Lo, s.Hi })
                {
                    if (arr == null) continue;
                    foreach (double v in arr)
                    {
                        if (double.IsFinite(v))
                        {
                            if (v < y0) y0 = v;
                            if (v > y1) y1 = v;
                        }
                    }
                }
            }

            if (!double.IsFinite(y0)) return;
            if (x1 <= x0) x1 = x0 + 1;
            if (y1 - y0 < 1e-12 * Math.Max(1, Math.Abs(y1))) { y0 -= 0.5; y1 += 0.5; }
            double pad = (y1 - y0) * 0.06;
            y0 -= pad;
            y1 += pad;

            var area = new Rect(64, Title.Length > 0 ? 24 : 10, Math.Max(10, w - 76), Math.Max(10, h - (Title.Length > 0 ? 24 : 10) - 46));
            double X(double v) => area.Left + (v - x0) / (x1 - x0) * area.Width;
            double Y(double v) => area.Bottom - (v - y0) / (y1 - y0) * area.Height;

            string unit = live.Select(s => s.Unit).Distinct().Count() == 1 ? live[0].Unit : string.Empty;
            foreach (double v in Ticks(y0, y1, Math.Max(2, (int)(area.Height / 40))))
            {
                dc.DrawLine(grid, new Point(area.Left, Y(v)), new Point(area.Right, Y(v)));
                var ft = T(SiValue.Format(v, unit), text, 11.5);
                dc.DrawText(ft, new Point(area.Left - ft.Width - 6, Y(v) - ft.Height / 2));
            }

            foreach (double v in Ticks(x0, x1, Math.Max(2, (int)(area.Width / 90))))
            {
                dc.DrawLine(grid, new Point(X(v), area.Top), new Point(X(v), area.Bottom));
                var ft = T(v.ToString("0.###", CultureInfo.InvariantCulture), text, 11.5);
                dc.DrawText(ft, new Point(X(v) - ft.Width / 2, area.Bottom + 4));
            }

            var xl = T(XLabel, text, 11.5);
            dc.DrawText(xl, new Point(area.Right - xl.Width, area.Bottom + 18));
            _vx0 = x0;
            _vx1 = x1;
            _varea = area;
            dc.PushClip(new RectangleGeometry(area));
            foreach (var s in live) DrawBand(dc, s, X, Y);
            foreach (var s in live) DrawSeries(dc, s, area, X, Y);
            if (Cursor is double cur && cur >= x0 && cur <= x1)
            {
                var pen = new Pen(Res("B.Text", Brushes.White), 1);
                dc.DrawLine(pen, new Point(X(cur), area.Top), new Point(X(cur), area.Bottom));
            }

            dc.Pop();

            double lx = area.Left + 8, ly = area.Bottom + 18;
            foreach (var s in live)
            {
                var b = new SolidColorBrush(s.Color);
                dc.DrawRectangle(b, null, new Rect(lx, ly + 4, 10, 3));
                var ft = T(s.Name, text, 11.5);
                dc.DrawText(ft, new Point(lx + 14, ly - 2));
                lx += ft.Width + 30;
            }
        }

        private static void DrawBand(DrawingContext dc, PlotSeries s, Func<double, double> X, Func<double, double> Y)
        {
            if (s.Lo == null || s.Hi == null || s.Lo.Length != s.X.Length || s.Hi.Length != s.X.Length || s.X.Length < 2) return;
            int step = Math.Max(1, s.X.Length / 1500);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(s.X[0]), Y(s.Hi[0])), true, true);
                for (int i = step; i < s.X.Length; i += step) g.LineTo(new Point(X(s.X[i]), Y(s.Hi[i])), false, false);
                for (int i = s.X.Length - 1; i >= 0; i -= step) g.LineTo(new Point(X(s.X[i]), Y(s.Lo[i])), false, false);
            }

            geo.Freeze();
            var fill = new SolidColorBrush(Color.FromArgb(0x40, s.Color.R, s.Color.G, s.Color.B));
            fill.Freeze();
            dc.DrawGeometry(fill, null, geo);
        }

        private static void DrawSeries(DrawingContext dc, PlotSeries s, Rect area, Func<double, double> X, Func<double, double> Y)
        {
            var pen = new Pen(new SolidColorBrush(s.Color), 1.4);
            pen.Freeze();
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                int n = s.X.Length;
                // Min/max per pixel column keeps peaks visible at any zoom.
                int px = -1;
                double lo = 0, hi = 0, first = 0, last = 0;
                void Flush()
                {
                    if (px < 0) return;
                    double x = area.Left + px + 0.5;
                    var a = new Point(x, Y(first));
                    if (!started)
                    {
                        g.BeginFigure(a, false, false);
                        started = true;
                    }
                    else g.LineTo(a, true, false);
                    if (hi != lo)
                    {
                        g.LineTo(new Point(x, Y(lo)), true, false);
                        g.LineTo(new Point(x, Y(hi)), true, false);
                    }

                    g.LineTo(new Point(x, Y(last)), true, false);
                }

                for (int i = 0; i < n; i++)
                {
                    double v = s.Y[i];
                    if (!double.IsFinite(v)) continue;
                    int c = (int)(X(s.X[i]) - area.Left);
                    if (c != px)
                    {
                        Flush();
                        px = c;
                        lo = hi = first = last = v;
                    }
                    else
                    {
                        if (v < lo) lo = v;
                        if (v > hi) hi = v;
                        last = v;
                    }
                }

                Flush();
            }

            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        /// <summary>Round tick values (1, 2, 5 × 10ⁿ) covering [a, b].</summary>
        public static IEnumerable<double> Ticks(double a, double b, int count)
        {
            double raw = (b - a) / Math.Max(1, count);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double step = raw / mag < 1.5 ? mag : raw / mag < 3.5 ? 2 * mag : raw / mag < 7.5 ? 5 * mag : 10 * mag;
            for (double v = Math.Ceiling(a / step) * step; v <= b + step * 1e-9; v += step) yield return Math.Abs(v) < step * 1e-9 ? 0 : v;
        }

        /// <summary>Blue → cyan → green → yellow → red scale for a value in [0, 1] (stress, temperature).</summary>
        public static Color Heat(double t)
        {
            t = Math.Clamp(double.IsFinite(t) ? t : 0, 0, 1);
            double r = Math.Clamp(1.5 - Math.Abs(4 * t - 3), 0, 1);
            double g = Math.Clamp(1.5 - Math.Abs(4 * t - 2), 0, 1);
            double b = Math.Clamp(1.5 - Math.Abs(4 * t - 1), 0, 1);
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        /// <summary>Blue – white – red scale for a signed value in [−1, 1] (vorticity, pressure).</summary>
        public static Color Diverging(double t)
        {
            t = Math.Clamp(double.IsFinite(t) ? t : 0, -1, 1);
            byte k = (byte)(255 * (1 - Math.Abs(t)));
            return t >= 0 ? Color.FromRgb(255, k, k) : Color.FromRgb(k, k, 255);
        }
    }
}
