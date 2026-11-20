using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Editor;

namespace RobotWin.Studio.Simulation
{
    /// <summary>Histogram of a Monte Carlo analysis with the nominal value, mean and ±3σ marked.</summary>
    public sealed class HistogramView : FrameworkElement
    {
        private static readonly Color Bar = Color.FromRgb(0x6E, 0x9B, 0xFF);
        private static readonly Color Axis = Color.FromRgb(0x32, 0x32, 0x37);
        private static readonly Color Muted = Color.FromRgb(0x6C, 0x6C, 0x74);
        private static readonly Color Text = Color.FromRgb(0xA0, 0xA0, 0xA8);
        private static readonly Color Nominal = Color.FromRgb(0xD4, 0xA8, 0x4B);
        private static readonly Color MeanColor = Color.FromRgb(0x5B, 0xB9, 0x8B);

        private MonteCarloResult? _result;

        public MonteCarloResult? Result
        {
            get => _result;
            set
            {
                _result = value;
                InvalidateVisual();
            }
        }

        public string Unit { get; set; } = "V";

        private double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        private FormattedText Label(string s, Color c, double size = 10.5) =>
            new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typefaces.Mono, size, SymbolRenderer.Brush(c), Dpi);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(SymbolRenderer.Brush(Color.FromRgb(0x0E, 0x0E, 0x10)), null, new Rect(0, 0, w, h));
            var r = _result;
            var plot = new Rect(44, 34, Math.Max(10, w - 60), Math.Max(10, h - 64));
            var axisPen = new Pen(SymbolRenderer.Brush(Axis), 1);
            axisPen.Freeze();
            dc.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
            dc.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
            if (r == null || r.Histogram.Length == 0)
            {
                var hint = Label(r == null ? "Monte Carlo sonucu yok: soldan ayarlayıp 'Çalıştır'a basın." : "Geçerli sonuç yok.", Muted, 11.5);
                dc.DrawText(hint, new Point(plot.Left + 12, plot.Top + plot.Height / 2 - hint.Height / 2));
                return;
            }

            int max = Math.Max(1, r.Histogram.Max());
            int bins = r.Histogram.Length;
            double bw = plot.Width / bins;
            var bar = SymbolRenderer.Brush(Color.FromArgb(0xC0, Bar.R, Bar.G, Bar.B));
            for (int i = 0; i < bins; i++)
            {
                double bh = plot.Height * r.Histogram[i] / max;
                if (bh <= 0) continue;
                dc.DrawRectangle(bar, null, new Rect(plot.Left + i * bw + 1, plot.Bottom - bh, Math.Max(1, bw - 2), bh));
                if (bw > 18)
                {
                    var n = Label(r.Histogram[i].ToString(CultureInfo.InvariantCulture), Text, 9.5);
                    dc.DrawText(n, new Point(plot.Left + i * bw + bw / 2 - n.Width / 2, plot.Bottom - bh - n.Height - 1));
                }
            }

            double lo = r.BinStart, hi = r.BinStart + r.BinWidth * bins;
            double X(double v) => plot.Left + (v - lo) / (hi - lo) * plot.Width;
            void Marker(double v, Color c, string text, int row)
            {
                if (double.IsNaN(v)) return;
                double x = Math.Max(plot.Left, Math.Min(plot.Right, X(v)));
                var pen = new Pen(SymbolRenderer.Brush(c), 1.5) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
                pen.Freeze();
                dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var ft = Label(text, c, 10);
                double tx = Math.Min(plot.Right - ft.Width, Math.Max(plot.Left, x + 4));
                dc.DrawText(ft, new Point(tx, plot.Top - 32 + row * 14));
            }

            Marker(r.Nominal, Nominal, "nominal " + SiValue.Format(r.Nominal, Unit, 4), 0);
            Marker(r.Mean, MeanColor, "ortalama " + SiValue.Format(r.Mean, Unit, 4), 1);

            var lft = Label(SiValue.Format(lo, Unit, 4), Text);
            dc.DrawText(lft, new Point(plot.Left, plot.Bottom + 4));
            var rft = Label(SiValue.Format(hi, Unit, 4), Text);
            dc.DrawText(rft, new Point(plot.Right - rft.Width, plot.Bottom + 4));
            var mid = Label(SiValue.Format((lo + hi) / 2, Unit, 4), Muted);
            dc.DrawText(mid, new Point(plot.Left + plot.Width / 2 - mid.Width / 2, plot.Bottom + 4));
            var ymax = Label(max.ToString(CultureInfo.InvariantCulture), Text);
            dc.DrawText(ymax, new Point(plot.Left - ymax.Width - 6, plot.Top - 2));
            var y0 = Label("0", Text);
            dc.DrawText(y0, new Point(plot.Left - y0.Width - 6, plot.Bottom - y0.Height + 2));
        }
    }
}
